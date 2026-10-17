using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UntPlanMobile.Dienste;

/// <summary>Eine Kachel des eigenen Plans im Vergleichsstand: Zeichen (alle relevanten Felder) und Kurzbeschreibung.</summary>
public sealed record Planeintrag(string Z, string Was, string Std, string Teile);

/// <summary>
/// Vergleich zweier Stände des eigenen Plans (wie planwacht.js in der App): Nur was eine Lehrkraft bzw. Klasse wissen muss –
/// Art, Fach, Klasse, Lehrkraft, Raum, Hinweis, offene Vertretung. Hausaufgaben und Notizen lösen nichts aus.
/// </summary>
public static class Planvergleich
{
    private static readonly string[] Wt = { "So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa." };

    public static string Zeichen(Kachel c) => string.Join("|", new[]
    {
        c.Stunde.ToString(CultureInfo.InvariantCulture), c.Dauer.ToString(CultureInfo.InvariantCulture), c.Art, c.Fach, c.Klasse, c.Lehrkraft, c.Raum, c.Info,
        c.Offen ? "1" : "", c.LehrerAbwesend == true ? "1" : "", c.BereitschaftAbwesend == true ? "1" : "", c.AufsichtUebernommen == true ? "1" : "",
        c.AufsichtAbgegeben == true ? "1" : "",
    });

    public static Planeintrag Eintrag(Kachel c)
    {
        var was = c.Art switch
        {
            "cancelled" => "Entfall",
            "klausur" => "Prüfung",
            "supervision" => "Aufsicht",
            "standby" => "Bereitschaft",
            "change" when c.Geaendert.Count == 1 && c.Geaendert[0] == "room" => "Raumänderung",
            "change" => "Vertretung",
            "absence" or "gs" => "Veranstaltung",
            _ when c.Aufsicht == true => "Aufsicht",
            _ => "",
        };
        var std = c.VorErsterStunde == true ? "vor der 1. Std."
            : c.Aufsicht == true ? $"nach der {c.Stunde}. Std."
            : c.Dauer > 1 ? $"{c.Stunde}.–{c.Stunde + c.Dauer - 1}. Std." : $"{c.Stunde}. Std.";
        var teile = string.Join(" ", new[] { c.Fach, c.Klasse, c.Raum }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new Planeintrag(Zeichen(c), was, std, teile);
    }

    /// <summary>Stand je Tag ab <paramref name="ab"/>: sortierte Einträge (Ferien ohne Belang).</summary>
    public static Dictionary<string, List<Planeintrag>> Stand(IReadOnlyDictionary<string, List<Kachel>> tage, DateOnly ab)
    {
        var abIso = ab.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return tage.Where(t => string.CompareOrdinal(t.Key, abIso) >= 0)
            .ToDictionary(t => t.Key, t => t.Value.Where(c => c.Ferien != true && c.Art != "holiday").Select(Eintrag).OrderBy(e => e.Z, StringComparer.Ordinal).ToList());
    }

    /// <summary>Unterschiede als Zeilen für die Benachrichtigung („Mo. 19.10. 3. Std.: Vertretung M 7a R101“).</summary>
    public static List<string> Vergleich(IReadOnlyDictionary<string, List<Planeintrag>> alt, IReadOnlyDictionary<string, List<Planeintrag>> neu, DateOnly ab)
    {
        var abIso = ab.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var liste = new List<string>();
        foreach (var d in neu.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (string.CompareOrdinal(d, abIso) < 0 || !alt.TryGetValue(d, out var a)) continue;
            var n = neu[d];
            var dazu = n.Where(x => a.All(y => y.Z != x.Z)).ToList();
            var weg = a.Where(x => n.All(y => y.Z != x.Z)).ToList();
            if (dazu.Count == 0 && weg.Count == 0) continue;
            var tag = DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var kopf = $"{Wt[(int)tag.DayOfWeek]} {tag.Day}.{tag.Month}.";
            foreach (var x in dazu) liste.Add($"{kopf} {x.Std}: {string.Join(" ", new[] { x.Was, x.Teile }.Where(s => s.Length > 0))}".TrimEnd());
            foreach (var x in weg.Where(x => dazu.All(y => y.Std != x.Std)))
                liste.Add($"{kopf} {x.Std}: {(x.Teile.Length > 0 ? x.Teile : "Stunde")} nicht mehr im Plan");
        }
        return liste;
    }

    /// <summary>Laufende und nächste Woche (am Wochenende die beiden kommenden).</summary>
    public static DateOnly[] Wochen(DateOnly heute)
    {
        var m = Kachelbau.Montag(heute);
        if (heute.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) m = m.AddDays(7);
        return new[] { m, m.AddDays(7) };
    }

    /// <summary>Inhalt der Push-Nachricht (JSON, vom Service Worker angezeigt).</summary>
    public static byte[] Nachricht(List<string> zeilen, string? konto)
    {
        var titel = zeilen.Count == 1 ? "Änderung im Stundenplan" : $"{zeilen.Count} Änderungen im Stundenplan";
        if (!string.IsNullOrEmpty(konto)) titel += $" · {konto}";
        var text = string.Join("\n", zeilen.Take(4)) + (zeilen.Count > 4 ? $"\n… und {zeilen.Count - 4} weitere" : "");
        return JsonSerializer.SerializeToUtf8Bytes(new { title = titel, body = text, tag = "planwacht", url = "./" });
    }
}

/// <summary>
/// Benachrichtigung sofort nach einem Import: prüft regelmäßig (UNTPLANMOBILE_PUSH_INTERVALL Sekunden, Vorgabe 60, 0 = aus) den
/// Planstand von UntPlanWeb (Revision und Empfangszeit des Pakets aus <c>/api/app/sitzung</c>, ohne Anmeldung). Hat er sich geändert –
/// spätestens aber alle 15 Minuten (Rückmeldungen wie Raumbuchungen ändern den Plan ohne neues Paket) –, wird für jedes Konto mit
/// eingeschalteten Benachrichtigungen der eigene Plan (laufende und nächste Woche) geholt und mit dem zuletzt gesehenen Stand
/// verglichen. Bei Unterschieden geht ein Web-Push an alle Abos des Geräts.
/// </summary>
public sealed class Pushwache : BackgroundService
{
    private readonly IServiceProvider _dienste;
    private readonly Kontenspeicher _speicher;
    private readonly Vapid _vapid;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<Pushwache> _log;
    private readonly TimeSpan _intervall;
    private string? _planstand;
    private DateTimeOffset _vollZuletzt = DateTimeOffset.MinValue;

    public static readonly TimeSpan Voll = TimeSpan.FromMinutes(15);

    public Pushwache(IServiceProvider dienste, Kontenspeicher speicher, Vapid vapid, IHttpClientFactory http, ILogger<Pushwache> log, IConfiguration konfig)
    {
        _dienste = dienste;
        _speicher = speicher;
        _vapid = vapid;
        _http = http;
        _log = log;
        _intervall = TimeSpan.FromSeconds(int.TryParse(konfig["UNTPLANMOBILE_PUSH_INTERVALL"], out var s) ? Math.Max(0, s) : 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (_intervall == TimeSpan.Zero) return;
        try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (TaskCanceledException) { return; }
        using var takt = new PeriodicTimer(_intervall);
        do
        {
            try { await RundeAsync(false, stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "Pushwache: Prüfung fehlgeschlagen."); }
        }
        while (await takt.WaitForNextTickAsync(stop).ConfigureAwait(false));
    }

    /// <summary>Eine Prüfrunde. <paramref name="erzwingen"/>: alle Konten prüfen, auch ohne neuen Planstand (Tests).</summary>
    public async Task<int> RundeAsync(bool erzwingen, CancellationToken ct)
    {
        var konten = _speicher.PushKonten();
        if (konten.Count == 0) return 0;
        var stand = await PlanstandAsync(ct);
        var neu = stand != null && stand != _planstand;
        if (stand != null) _planstand = stand;
        var voll = DateTimeOffset.UtcNow - _vollZuletzt >= Voll;
        var gesendet = 0;
        foreach (var (geraet, konto, abos, anzahl) in konten)
        {
            // ohne Grundstand (gerade eingeschaltet) wird immer geholt, sonst nur bei neuem Planstand bzw. turnusmäßig
            if (!(erzwingen || neu || voll || konto.Stand is null)) continue;
            gesendet += await KontoPruefenAsync(geraet, konto, abos, anzahl > 1, ct);
        }
        if (voll) _vollZuletzt = DateTimeOffset.UtcNow;
        return gesendet;
    }

    /// <summary>Leichter Planstand: Revision und Empfangszeit des aktuellen Pakets (ohne Anmeldung abrufbar).</summary>
    private async Task<string?> PlanstandAsync(CancellationToken ct)
    {
        try
        {
            var web = _dienste.GetRequiredService<UntPlanWebVerbindung>();
            var c = new DefaultHttpContext { RequestServices = _dienste, RequestAborted = ct };
            var s = await web.AufrufAsync(c, HttpMethod.Get, "api/app/sitzung", anmeldungNoetig: false);
            return s?["paket"]?.ToJsonString();
        }
        catch (Exception ex) when (ex is UntPlanWebFehler or NichtAngemeldetException) { return null; }
    }

    private async Task<int> KontoPruefenAsync(string geraet, GespeichertesKonto konto, List<PushAbo> abos, bool mehrereKonten, CancellationToken ct)
    {
        var sitzung = _speicher.SitzungKlar(konto);
        if (sitzung is null) return 0;
        using var scope = _dienste.CreateScope();
        var c = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = ct };
        c.Items[UntPlanWebVerbindung.Cookie] = sitzung;
        var zeit = scope.ServiceProvider.GetRequiredService<Zeitgeber>();
        Dictionary<string, List<Planeintrag>> neu = new();
        try
        {
            var k = await scope.ServiceProvider.GetRequiredService<Kontodienst>().LadenAsync(c);
            if (k.Kuerzel.Length == 0) return 0;
            var wochen = scope.ServiceProvider.GetRequiredService<Wochendienst>();
            foreach (var montag in Planvergleich.Wochen(zeit.Heute))
            {
                var w = await wochen.WocheAsync(c, k, "mine", null, montag);
                // gesperrte Woche (Wochenbegrenzung): kein Vergleich – sonst erschienen alle Stunden als weggefallen
                if (w.Gesperrt == true) continue;
                foreach (var (tag, liste) in Planvergleich.Stand(w.Days, zeit.Heute)) neu[tag] = liste;
            }
        }
        catch (NichtAngemeldetException) { return 0; }
        catch (UntPlanWebFehler ex) { _log.LogInformation("Pushwache: Plan von {Login} nicht abrufbar ({Fehler}).", konto.Login, ex.Message); return 0; }

        var zeilen = konto.Stand is null ? new List<string>() : Planvergleich.Vergleich(konto.Stand, neu, zeit.Heute);
        _speicher.StandSetzen(geraet, konto.Id, neu);
        if (zeilen.Count == 0) return 0;
        return await SendenAsync(abos, Planvergleich.Nachricht(zeilen, mehrereKonten ? konto.Name : null), ct);
    }

    public async Task<int> SendenAsync(List<PushAbo> abos, byte[] nachricht, CancellationToken ct)
    {
        var http = _http.CreateClient("push");
        var n = 0;
        foreach (var abo in abos)
        {
            switch (await WebPush.SendenAsync(http, abo, nachricht, _vapid.Schluessel, _vapid.Kontakt, ct))
            {
                case PushErgebnis.Zugestellt: n++; break;
                case PushErgebnis.Abgelaufen: _speicher.AboEntfernen(abo.Endpoint); break;
                default: _log.LogInformation("Pushwache: Zustellung an {Dienst} fehlgeschlagen.", new Uri(abo.Endpoint).Host); break;
            }
        }
        return n;
    }
}

/// <summary>VAPID-Schlüsselpaar (P-256) des Servers – beim ersten Start erzeugt und in <c>vapid.json</c> im Datenordner abgelegt.</summary>
public sealed class Vapid
{
    public ECDsa Schluessel { get; }
    /// <summary>Öffentlicher Schlüssel (Base64url, 65 Byte) – applicationServerKey für pushManager.subscribe().</summary>
    public string Oeffentlich { get; }
    /// <summary>Kontakt für die Push-Dienste (UNTPLANMOBILE_VAPID_KONTAKT, mailto: oder https:).</summary>
    public string Kontakt { get; }

    public Vapid(Datenordner ordner, IConfiguration konfig)
    {
        Kontakt = konfig["UNTPLANMOBILE_VAPID_KONTAKT"] is { Length: > 0 } k ? k : "https://github.com/florians25/UntPlan";
        var datei = Path.Combine(ordner.Pfad, "vapid.json");
        ECParameters? p = null;
        try
        {
            if (File.Exists(datei) && JsonNode.Parse(File.ReadAllText(datei)) is JsonObject j)
                p = WebPush.AusPunkt(WebPush.AusB64(j["oeffentlich"]!.GetValue<string>()), WebPush.AusB64(j["privat"]!.GetValue<string>()));
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or NullReferenceException) { p = null; }
        if (p is null)
        {
            using var neu = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            p = neu.ExportParameters(true);
            File.WriteAllText(datei, new JsonObject { ["oeffentlich"] = WebPush.B64(WebPush.Punkt(p.Value)), ["privat"] = WebPush.B64(p.Value.D) }.ToJsonString());
            try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(datei, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch (IOException) { }
        }
        Schluessel = ECDsa.Create(p.Value);
        Oeffentlich = WebPush.B64(WebPush.Punkt(p.Value));
    }
}
