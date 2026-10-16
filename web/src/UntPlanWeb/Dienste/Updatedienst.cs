using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Einstellungen der Updateanzeige und des optionalen Updaters (Umgebungsvariablen):
/// <list type="bullet">
/// <item><c>UNTPLANWEB_UPDATE_PRUEFEN</c> = 0 schaltet die Prüfung auf neue Versionen ab (Standard: an).</item>
/// <item><c>UNTPLANWEB_UPDATE_URL</c> = Quelle der neuesten Version (Standard: Tags des Images <c>ghcr://florians25/untplanweb</c> – dieselbe
/// Nummerierung wie die laufende Version; die GitHub-Releases gehören zum Desktop-Programm und haben eine eigene Zählung).</item>
/// <item><c>UNTPLANWEB_UPDATE_TAG_PRAEFIX</c> = nur Tags mit diesem Präfix berücksichtigen (z. B. <c>web-v</c>).</item>
/// <item><c>UNTPLANWEB_UPDATE_MODUS</c> = <c>aus</c> | <c>watchtower</c> | <c>datei</c> – wie „Update installieren“ ausgelöst wird.</item>
/// <item><c>UNTPLANWEB_UPDATE_WATCHTOWER_URL</c>, <c>UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN</c> = HTTP-API von Watchtower (<c>http://watchtower:8080/v1/update</c>).</item>
/// <item><c>UNTPLANWEB_MOBILE_INTERN_URL</c> = Adresse von UntPlanMobile aus Sicht des Servers (z. B. <c>http://untplanmobile:8081</c>) für die Versionsanzeige.</item>
/// </list>
/// </summary>
public sealed class Updateeinstellungen
{
    /// <summary>Standardquelle: Tags des Web-Images (Version 1.0.N aus dem Web-Build, passt genau zu <c>docker compose pull</c>).</summary>
    public const string Standardquelle = "ghcr://florians25/untplanweb";

    public bool Pruefen { get; init; } = true;
    public string? Url { get; init; }
    public string TagPraefix { get; init; } = "";
    public string Modus { get; init; } = "aus";
    public string? WatchtowerUrl { get; init; }
    public string? WatchtowerToken { get; init; }
    public string? MobilUrl { get; init; }

    public static Updateeinstellungen Laden(IConfiguration c)
    {
        string? L(string n) => string.IsNullOrWhiteSpace(c[n]) ? null : c[n]!.Trim();
        var watchtower = L("UNTPLANWEB_UPDATE_WATCHTOWER_URL");
        var modus = (L("UNTPLANWEB_UPDATE_MODUS") ?? (watchtower != null ? "watchtower" : "aus")).ToLowerInvariant();
        if (modus is not ("aus" or "watchtower" or "datei")) modus = "aus";
        return new Updateeinstellungen
        {
            Pruefen = Updatepruefung.Schalter(c["UNTPLANWEB_UPDATE_PRUEFEN"], true),
            Url = L("UNTPLANWEB_UPDATE_URL") ?? Standardquelle,
            TagPraefix = L("UNTPLANWEB_UPDATE_TAG_PRAEFIX") ?? "",
            Modus = modus,
            WatchtowerUrl = watchtower,
            WatchtowerToken = L("UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN"),
            MobilUrl = L("UNTPLANWEB_MOBILE_INTERN_URL"),
        };
    }
}

public sealed record Updateanstoss(bool Ok, string Meldung, string? Sicherung);

/// <summary>
/// Updateanzeige und -auslösung für die Administration. Das Update selbst ist immer ein neues Container-Image
/// (<c>docker compose pull &amp;&amp; docker compose up -d</c>); die Migrationen laufen beim Start automatisch.
/// Aus der Oberfläche kann es – wenn eingerichtet – über Watchtower (HTTP-API) oder eine Anforderungsdatei für ein
/// Skript auf dem Server (<c>update.sh --wenn-angefordert</c>, z. B. per cron) angestoßen werden.
/// </summary>
public sealed class Updatedienst
{
    public const string Befehl = "docker compose pull && docker compose up -d";

    private readonly Updateeinstellungen _e;
    private readonly IHttpClientFactory _http;
    private readonly Konfiguration _k;
    private readonly Db _db;
    private readonly Protokoll _protokoll;

    public Updatepruefung Pruefung { get; }

    public Updatedienst(Updateeinstellungen e, IHttpClientFactory http, Konfiguration k, Db db, Protokoll protokoll)
    {
        _e = e;
        _http = http;
        _k = k;
        _db = db;
        _protokoll = protokoll;
        Pruefung = new Updatepruefung(http.CreateClient("update"), typeof(Updatedienst).Assembly, e.Url, e.Pruefen, e.TagPraefix);
    }

    public Updateeinstellungen Einstellungen => _e;

    /// <summary>Anforderungsdatei für den Modus „datei“ (im Datenordner, also auch auf dem Server sichtbar).</summary>
    public string Anforderungsdatei => Path.Combine(_k.Datenordner, "update", "angefordert.json");

    public bool Installierbar => _e.Modus switch
    {
        "watchtower" => _e.WatchtowerUrl != null,
        "datei" => true,
        _ => false,
    };

    public string? Angefordert => File.Exists(Anforderungsdatei) ? File.GetLastWriteTime(Anforderungsdatei).ToString("yyyy-MM-dd HH:mm") : null;

    public Task<Updatestand> StandAsync(bool erzwingen, CancellationToken ct) => Pruefung.StandAsync(erzwingen, ct);

    /// <summary>Version von UntPlanMobile (falls UNTPLANWEB_MOBILE_INTERN_URL gesetzt ist).</summary>
    public async Task<JsonElement?> MobilAsync(CancellationToken ct)
    {
        if (_e.MobilUrl is null) return null;
        try
        {
            using var zeit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            zeit.CancelAfter(TimeSpan.FromSeconds(4));
            var json = await _http.CreateClient("update").GetStringAsync(_e.MobilUrl.TrimEnd('/') + "/api/version", zeit.Token);
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    /// <summary>Update anstoßen (nur Administration). SQLite wird vorher gesichert.</summary>
    public async Task<Updateanstoss> InstallierenAsync(string benutzer, CancellationToken ct)
    {
        if (!Installierbar)
            return new Updateanstoss(false, $"Kein Updater eingerichtet. Bitte auf dem Server ausführen: {Befehl} (siehe README, Abschnitt „Update“).", null);
        var stand = await Pruefung.StandAsync(false, ct);
        string? sicherung = null;
        try { sicherung = _db.Sichern(Path.Combine(_k.Datenordner, "sicherungen")); AlteSicherungenLoeschen(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return new Updateanstoss(false, "Sicherung fehlgeschlagen: " + ex.Message, null); }

        if (_e.Modus == "datei")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Anforderungsdatei)!);
            await File.WriteAllTextAsync(Anforderungsdatei, JsonSerializer.Serialize(new
            {
                zeit = DateTime.Now.ToString("O"), benutzer, version = stand.Version, neueste = stand.Neueste, sicherung,
            }), ct);
            _protokoll.Schreiben(benutzer, "Update angefordert", $"{stand.Version} → {stand.Neueste ?? "neueste"} (Anforderungsdatei)");
            return new Updateanstoss(true, "Update angefordert. Das Update-Skript auf dem Server übernimmt es beim nächsten Lauf (in der Regel innerhalb einer Minute); danach startet UntPlanWeb neu.", sicherung);
        }

        // Watchtower: der Aufruf blockiert, bis das Update fertig ist – oft wird dabei dieser Container ersetzt.
        // Daher nur kurz auf eine Ablehnung warten und sonst als angestoßen melden.
        var anfrage = new HttpRequestMessage(HttpMethod.Post, _e.WatchtowerUrl); // nicht vorzeitig freigeben: läuft ggf. weiter
        if (_e.WatchtowerToken != null) anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _e.WatchtowerToken);
        var client = _http.CreateClient("update");
        var senden = client.SendAsync(anfrage, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        var fertig = await Task.WhenAny(senden, Task.Delay(TimeSpan.FromSeconds(5), ct));
        if (fertig == senden)
        {
            try
            {
                using var antwort = await senden;
                if (antwort.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return new Updateanstoss(false, "Watchtower hat die Anfrage abgelehnt (Token prüfen: UNTPLANWEB_UPDATE_WATCHTOWER_TOKEN).", sicherung);
                if (!antwort.IsSuccessStatusCode) return new Updateanstoss(false, $"Watchtower antwortet mit Fehler {(int)antwort.StatusCode}.", sicherung);
            }
            catch (HttpRequestException ex) { return new Updateanstoss(false, $"Watchtower nicht erreichbar ({ex.Message}) – läuft der Dienst (docker compose --profile updater up -d)?", sicherung); }
        }
        _protokoll.Schreiben(benutzer, "Update angestoßen", $"{stand.Version} → {stand.Neueste ?? "neueste"} (Watchtower)");
        return new Updateanstoss(true, "Update angestoßen. Watchtower lädt das neue Image und startet die Container neu – die Seite ist kurz nicht erreichbar.", sicherung);
    }

    private void AlteSicherungenLoeschen()
    {
        var ordner = new DirectoryInfo(Path.Combine(_k.Datenordner, "sicherungen"));
        if (!ordner.Exists) return;
        foreach (var f in ordner.GetFiles("untplanweb-*.db").OrderByDescending(f => f.Name).Skip(5)) f.Delete();
    }
}
