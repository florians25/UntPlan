using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Dienste;

/// <summary>Keine (gültige) Anmeldung bei UntPlanWeb – wird zu HTTP 401.</summary>
public sealed class NichtAngemeldetException() : Exception("not_authenticated");

/// <summary>Fehlermeldung von UntPlanWeb (z. B. fehlende Berechtigung), mit HTTP-Status.</summary>
public sealed class UntPlanWebFehler(int status, string meldung) : Exception(meldung)
{
    public int Status { get; } = status;
}

/// <summary>
/// Verbindung zu UntPlanWeb (/api/app/*). Das Sitzungscookie von UntPlanWeb liegt im Browser als eigenes Cookie
/// „untplanmobile“ und wird bei jedem Aufruf als „untplanweb“ weitergereicht. Erneuert UntPlanWeb das Cookie
/// (gleitender Ablauf) oder meldet ab, wird das Browser-Cookie nachgeführt.
/// </summary>
public sealed class UntPlanWebVerbindung
{
    public const string Cookie = "untplanmobile";
    public const string WebCookie = "untplanweb";
    public const string CookieAblauf = "untplanmobile:ablauf";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Kontenspeicher? _speicher;

    public UntPlanWebVerbindung(HttpClient http, Kontenspeicher? speicher = null) { _http = http; _speicher = speicher; }

    public Uri? Adresse => _http.BaseAddress;

    /// <summary>
    /// Sitzung der laufenden Anfrage: eine in dieser Anfrage neu gesetzte (Anmeldung, Profilwechsel, Hintergrundprüfung) hat Vorrang
    /// vor dem Cookie des Browsers.
    /// </summary>
    public static string? Sitzung(HttpContext c) =>
        c.Items.TryGetValue(Cookie, out var neu) && neu is string n ? (n.Length > 0 ? n : null)
        : c.Request.Cookies.TryGetValue(Cookie, out var v) && !string.IsNullOrEmpty(v) ? v : null;

    /// <summary>Aufruf an UntPlanWeb. Ohne Anmeldung (oder bei 401 von UntPlanWeb) folgt <see cref="NichtAngemeldetException"/>.</summary>
    public Task<JsonNode?> AufrufAsync(HttpContext c, HttpMethod methode, string pfad, object? daten = null, bool anmeldungNoetig = true) =>
        AufrufMitInhaltAsync(c, methode, pfad, daten != null ? JsonContent.Create(daten, daten.GetType(), options: Json) : null, anmeldungNoetig);

    /// <summary>Datei als multipart/form-data an UntPlanWeb (z. B. Anhang einer Mitteilung).</summary>
    public Task<JsonNode?> HochladenAsync(HttpContext c, string pfad, string feld, string dateiname, string typ, Stream inhalt)
    {
        var form = new MultipartFormDataContent();
        var datei = new StreamContent(inhalt);
        datei.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(typ, out var t) ? t : new("application/octet-stream");
        form.Add(datei, feld, dateiname);
        return AufrufMitInhaltAsync(c, HttpMethod.Post, pfad, form, true);
    }

    /// <summary>Datei von UntPlanWeb laden (z. B. Anhang): Inhalt und Dateiname aus Content-Disposition.</summary>
    public async Task<(byte[] Daten, string? Name)> DateiAsync(HttpContext c, string pfad)
    {
        var sitzung = Sitzung(c) ?? throw new NichtAngemeldetException();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, pfad.TrimStart('/'));
        anfrage.Headers.Add("X-UntPlanWeb", "1");
        anfrage.Headers.Add("Cookie", $"{WebCookie}={sitzung}");
        HttpResponseMessage antwort;
        try { antwort = await _http.SendAsync(anfrage, c.RequestAborted); }
        catch (HttpRequestException ex) { throw new UntPlanWebFehler(502, $"UntPlanWeb ist nicht erreichbar ({ex.Message})."); }
        using (antwort)
        {
            CookieUebernehmen(c, antwort, true);
            if (antwort.StatusCode == HttpStatusCode.Unauthorized) throw new NichtAngemeldetException();
            if (!antwort.IsSuccessStatusCode) throw new UntPlanWebFehler((int)antwort.StatusCode, "Der Anhang ist nicht (mehr) vorhanden.");
            var cd = antwort.Content.Headers.ContentDisposition;
            return (await antwort.Content.ReadAsByteArrayAsync(c.RequestAborted), (cd?.FileNameStar ?? cd?.FileName)?.Trim('"'));
        }
    }

    private async Task<JsonNode?> AufrufMitInhaltAsync(HttpContext c, HttpMethod methode, string pfad, HttpContent? inhalt, bool anmeldungNoetig)
    {
        using var _inhalt = inhalt;
        var sitzung = Sitzung(c);
        if (sitzung is null && anmeldungNoetig) throw new NichtAngemeldetException();
        using var anfrage = new HttpRequestMessage(methode, pfad.TrimStart('/'));
        // UntPlanWeb verlangt diesen Kopf bei ändernden Aufrufen (Schutz vor fremden Seiten)
        anfrage.Headers.Add("X-UntPlanWeb", "1");
        anfrage.Headers.Accept.ParseAdd("application/json");
        if (sitzung != null) anfrage.Headers.Add("Cookie", $"{WebCookie}={sitzung}");
        if (inhalt != null) anfrage.Content = inhalt;

        HttpResponseMessage antwort;
        try { antwort = await _http.SendAsync(anfrage, c.RequestAborted); }
        catch (HttpRequestException ex) { throw new UntPlanWebFehler(502, $"UntPlanWeb ist nicht erreichbar ({ex.Message})."); }
        catch (TaskCanceledException) when (!c.RequestAborted.IsCancellationRequested) { throw new UntPlanWebFehler(504, "UntPlanWeb antwortet nicht."); }

        using (antwort)
        {
            CookieUebernehmen(c, antwort, anmeldungNoetig);
            var text = await antwort.Content.ReadAsStringAsync(c.RequestAborted);
            JsonNode? json = null;
            try { json = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
            if (antwort.StatusCode == HttpStatusCode.Unauthorized && anmeldungNoetig)
            {
                if (sitzung != null) _speicher?.SitzungUngueltig(sitzung);
                SitzungLoeschen(c);
                throw new NichtAngemeldetException();
            }
            if (!antwort.IsSuccessStatusCode)
            {
                var meldung = json is JsonObject o && o["fehler"] is JsonValue f ? f.GetValue<string>() : $"UntPlanWeb antwortet mit HTTP {(int)antwort.StatusCode}.";
                throw new UntPlanWebFehler((int)antwort.StatusCode, meldung);
            }
            return json;
        }
    }

    public async Task<T> HolenAsync<T>(HttpContext c, string pfad)
    {
        var json = await AufrufAsync(c, HttpMethod.Get, pfad);
        return json.Deserialize<T>(Json) ?? throw new UntPlanWebFehler(502, "Leere Antwort von UntPlanWeb.");
    }

    public Task<JsonNode?> GetAsync(HttpContext c, string pfad) => AufrufAsync(c, HttpMethod.Get, pfad);
    public Task<JsonNode?> PostAsync(HttpContext c, string pfad, object daten) => AufrufAsync(c, HttpMethod.Post, pfad, daten);
    public Task<JsonNode?> PutAsync(HttpContext c, string pfad, object daten) => AufrufAsync(c, HttpMethod.Put, pfad, daten);
    public Task<JsonNode?> DeleteAsync(HttpContext c, string pfad) => AufrufAsync(c, HttpMethod.Delete, pfad);

    /// <summary>Set-Cookie „untplanweb“ von UntPlanWeb als eigenes Cookie an den Browser weitergeben.</summary>
    private void CookieUebernehmen(HttpContext c, HttpResponseMessage antwort, bool erneuerung)
    {
        var alt = Sitzung(c);
        if (!antwort.Headers.TryGetValues("Set-Cookie", out var zeilen)) return;
        foreach (var zeile in zeilen)
        {
            var teile = zeile.Split(';', StringSplitOptions.TrimEntries);
            var gleich = teile[0].IndexOf('=');
            if (gleich < 0 || teile[0][..gleich] != WebCookie) continue;
            var wert = teile[0][(gleich + 1)..];
            DateTimeOffset? ablauf = null;
            foreach (var t in teile.Skip(1))
                if (t.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) &&
                    DateTimeOffset.TryParse(t[8..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a)) ablauf = a;
            if (wert.Length == 0 || ablauf < DateTimeOffset.UtcNow) SitzungLoeschen(c);
            else
            {
                SitzungSetzen(c, wert, ablauf ?? DateTimeOffset.UtcNow.AddDays(14));
                // gespeicherte Profile (mehrere Konten je Gerät, Benachrichtigungen) behalten die erneuerte Sitzung –
                // nicht bei Anmeldung bzw. Einmalanmeldung, dort kommt die Sitzung eines anderen Kontos
                if (alt != null && erneuerung) _speicher?.SitzungErneuert(alt, wert, ablauf ?? DateTimeOffset.UtcNow.AddDays(14));
            }
        }
    }

    public static void SitzungSetzen(HttpContext c, string wert, DateTimeOffset ablauf)
    {
        c.Items[Cookie] = wert;
        c.Items[CookieAblauf] = ablauf;
        if (c.Response.HasStarted) return;
        c.Response.Cookies.Append(Cookie, wert, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, Path = "/", Expires = ablauf, IsEssential = true,
        });
    }

    public static void SitzungLoeschen(HttpContext c)
    {
        c.Items[Cookie] = "";
        if (c.Response.HasStarted) return;
        c.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/", Secure = c.Request.IsHttps, SameSite = SameSiteMode.Lax, HttpOnly = true });
    }
}
