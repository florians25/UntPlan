using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Registriertes Gerät der iOS-App (Gerätetoken für Sofort-Benachrichtigungen).</summary>
public sealed record PushGeraet(int BenutzerId, string Token, string Plattform, string Umgebung, string Bundle, string Aktualisiert);

/// <summary>APNs-Zugang (Administration): Inhalt der .p8-Datei, Key-ID, Team-ID und Topic (Bundle-ID der App).</summary>
public sealed record PushKonfiguration(string Schluessel, string KeyId, string TeamId, string Topic)
{
    public bool Vollstaendig => Schluessel.Length > 0 && KeyId.Length > 0 && TeamId.Length > 0 && Topic.Length > 0;
}

/// <summary>Geänderte Stunde einer Lehrkraft (für die Benachrichtigung).</summary>
public sealed record PlanAenderung(string Datum, int Von, int Bis, string Fach, string Klassen, string Status);

/// <summary>
/// Sofort-Benachrichtigungen an die iOS-App über den Apple Push Notification service (APNs, HTTP/2, Token-Authentifizierung mit ES256).
/// <list type="bullet">
/// <item>Geräte melden ihr Token über <c>POST /api/app/push/geraet</c> an; ein Token kann mehreren Konten gehören.</item>
/// <item>Nach jedem Paket aus UntPlan werden die Karten der aktuellen und der nächsten Woche der Lehrkräfte mit registrierten Geräten
///   mit dem vorherigen Paket verglichen; geänderte Stunden gehen als sichtbare Benachrichtigung hinaus (ab vier Änderungen gebündelt).</item>
/// <item>Der Versand läuft im Hintergrund und lässt den Import nie scheitern. Ohne vollständige APNs-Konfiguration geschieht nichts.</item>
/// <item>Abgelaufene bzw. ungültige Tokens (410, BadDeviceToken, Unregistered) werden entfernt.</item>
/// </list>
/// </summary>
public sealed class Pushdienst
{
    public const string StandardTopic = "de.untplan.mobile";
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] Tage = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

    private readonly Db _db;
    private readonly Einstellungen _e;
    private readonly Plandienst _plan;
    private readonly Benutzerdienst _benutzer;
    private readonly Uhr _uhr;
    private readonly ILogger<Pushdienst> _log;
    private readonly object _sperre = new();
    private HttpClient? _client;
    private (string Schluessel, string Jwt, DateTimeOffset Zeit)? _jwt;

    /// <summary>Für Tests: eigener HTTP-Handler statt der Verbindung zu Apple.</summary>
    public HttpMessageHandler? Handler { get; set; }
    /// <summary>Für Tests: der zuletzt gestartete Hintergrundversand.</summary>
    public Task LetzterVersand { get; private set; } = Task.CompletedTask;

    public Pushdienst(Db db, Einstellungen e, Plandienst plan, Benutzerdienst benutzer, Uhr uhr, ILogger<Pushdienst> log, Paketdienst pakete)
    {
        _db = db; _e = e; _plan = plan; _benutzer = benutzer; _uhr = uhr; _log = log;
        pakete.Gespeichert += NachImport;
    }

    // ------------------------------------------------------------------------------------------ Geräte

    public void GeraetSpeichern(int benutzerId, string token, string? plattform, string? umgebung, string? bundle)
    {
        var t = (token ?? "").Trim();
        if (t.Length is < 8 or > 200 || !t.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("Ungültiges Gerätetoken.");
        var p = string.IsNullOrWhiteSpace(plattform) ? "ios" : plattform.Trim().ToLowerInvariant();
        if (p != "ios") throw new ArgumentException("Nur die Plattform „ios“ wird unterstützt.");
        var u = string.IsNullOrWhiteSpace(umgebung) ? "production" : umgebung.Trim().ToLowerInvariant();
        if (u is not ("sandbox" or "production")) throw new ArgumentException("Umgebung „sandbox“ oder „production“ erwartet.");
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");
        _db.Ausfuehren("""
            INSERT INTO push_geraete (benutzer_id, token, plattform, umgebung, bundle, erstellt, aktualisiert) VALUES (@b, @t, @p, @u, @bu, @j, @j)
            ON CONFLICT(benutzer_id, token) DO UPDATE SET plattform = excluded.plattform, umgebung = excluded.umgebung, bundle = excluded.bundle, aktualisiert = excluded.aktualisiert
            """, Db.P("@b", benutzerId), Db.P("@t", t), Db.P("@p", p), Db.P("@u", u), Db.P("@bu", (bundle ?? "").Trim()), Db.P("@j", jetzt));
    }

    /// <summary>Token vom Konto abmelden (benutzerId = null: von allen Konten, z. B. abgelaufen).</summary>
    public int GeraetLoeschen(int? benutzerId, string token) => benutzerId is int b
        ? _db.Ausfuehren("DELETE FROM push_geraete WHERE benutzer_id = @b AND token = @t", Db.P("@b", b), Db.P("@t", (token ?? "").Trim()))
        : _db.Ausfuehren("DELETE FROM push_geraete WHERE token = @t", Db.P("@t", (token ?? "").Trim()));

    public List<PushGeraet> Geraete(int? benutzerId = null) =>
        _db.Liste("SELECT * FROM push_geraete" + (benutzerId is null ? "" : " WHERE benutzer_id = @b") + " ORDER BY benutzer_id, token",
            r => new PushGeraet(r.Zahl("benutzer_id"), r.Text("token"), r.Text("plattform"), r.Text("umgebung"), r.Text("bundle"), r.Text("aktualisiert")),
            Db.P("@b", benutzerId ?? 0));

    // ------------------------------------------------------------------------------------------ Konfiguration

    public PushKonfiguration Konfiguration() => new(_e.Lesen("push_apns_schluessel") ?? "", _e.Lesen("push_apns_key_id") ?? "",
        _e.Lesen("push_apns_team_id") ?? "", (_e.Lesen("push_apns_topic") ?? "") is { Length: > 0 } t ? t : StandardTopic);

    /// <summary>APNs-Zugang speichern; schluessel = null lässt den hinterlegten Schlüssel unverändert, "" entfernt ihn.</summary>
    public void KonfigurationSpeichern(string? schluessel, string? keyId, string? teamId, string? topic)
    {
        if (schluessel is { Length: > 0 })
        {
            try { using var ec = SchluesselLaden(schluessel); }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException) { throw new ArgumentException("Der APNs-Schlüssel (.p8) konnte nicht gelesen werden."); }
            _e.Schreiben("push_apns_schluessel", schluessel.Trim());
        }
        else if (schluessel is not null) _e.Schreiben("push_apns_schluessel", "");
        if (keyId is not null) _e.Schreiben("push_apns_key_id", keyId.Trim());
        if (teamId is not null) _e.Schreiben("push_apns_team_id", teamId.Trim());
        if (topic is not null) _e.Schreiben("push_apns_topic", topic.Trim());
        lock (_sperre) _jwt = null;
    }

    // ------------------------------------------------------------------------------------------ JWT (ES256)

    private static ECDsa SchluesselLaden(string p8)
    {
        var t = p8.Trim();
        if (!t.Contains("-----BEGIN", StringComparison.Ordinal))
            t = "-----BEGIN PRIVATE KEY-----\n" + string.Join("\n", t.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)) + "\n-----END PRIVATE KEY-----";
        var ec = ECDsa.Create();
        try { ec.ImportFromPem(t); }
        catch { ec.Dispose(); throw; }
        if (ec.KeySize != 256) { ec.Dispose(); throw new ArgumentException("APNs erwartet einen P-256-Schlüssel."); }
        return ec;
    }

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Anbieter-Token für APNs: JWT mit Kopf {"alg":"ES256","kid":…} und Inhalt {"iss":Team-ID,"iat":…}, Signatur ES256 (r‖s, 64 Byte).</summary>
    public static string Jwt(string p8, string keyId, string teamId, DateTimeOffset zeit)
    {
        using var ec = SchluesselLaden(p8);
        var kopf = B64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["alg"] = "ES256", ["kid"] = keyId }));
        var inhalt = B64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["iss"] = teamId, ["iat"] = zeit.ToUnixTimeSeconds() }));
        var signatur = ec.SignData(Encoding.ASCII.GetBytes($"{kopf}.{inhalt}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{kopf}.{inhalt}.{B64Url(signatur)}";
    }

    /// <summary>Token höchstens 50 Minuten wiederverwenden (APNs verlangt eine Erneuerung nach spätestens einer Stunde).</summary>
    private string AktuellesJwt(PushKonfiguration k)
    {
        lock (_sperre)
        {
            var jetzt = DateTimeOffset.UtcNow;
            var schluessel = $"{k.KeyId}|{k.TeamId}|{k.Schluessel.GetHashCode()}";
            if (_jwt is { } j && j.Schluessel == schluessel && jetzt - j.Zeit < TimeSpan.FromMinutes(50)) return j.Jwt;
            var neu = Jwt(k.Schluessel, k.KeyId, k.TeamId, jetzt);
            _jwt = (schluessel, neu, jetzt);
            return neu;
        }
    }

    // ------------------------------------------------------------------------------------------ Versand

    private HttpClient Client()
    {
        lock (_sperre)
            return _client ??= new HttpClient(Handler ?? new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5) })
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                Timeout = TimeSpan.FromSeconds(20),
            };
    }

    /// <summary>APNs-Inhalt einer sichtbaren Benachrichtigung; datum (yyyy-MM-dd) öffnet in der App den Plan dieses Tages.</summary>
    public static string Inhalt(string titel, string text, string datum) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["aps"] = new Dictionary<string, object> { ["alert"] = new Dictionary<string, string> { ["title"] = titel, ["body"] = text }, ["sound"] = "default" },
        ["datum"] = datum,
    });

    /// <summary>
    /// Benachrichtigung an alle Geräte der Konten senden. Liefert die Zahl erfolgreich zugestellter Benachrichtigungen; ohne vollständige
    /// Konfiguration wird nichts gesendet (0). Ungültige Tokens werden entfernt.
    /// </summary>
    public async Task<int> SendenAsync(IEnumerable<int> benutzerIds, string titel, string text, string datum, CancellationToken ct = default)
    {
        var k = Konfiguration();
        if (!k.Vollstaendig) return 0;
        var ids = benutzerIds.ToHashSet();
        var geraete = Geraete().Where(g => ids.Contains(g.BenutzerId) && g.Plattform == "ios").DistinctBy(g => (g.Token, g.Umgebung)).ToList();
        if (geraete.Count == 0) return 0;
        string jwt;
        try { jwt = AktuellesJwt(k); }
        catch (Exception ex) { _log.LogWarning(ex, "APNs-Schlüssel ungültig"); return 0; }
        var inhalt = Inhalt(titel, text, datum);
        var ok = 0;
        foreach (var g in geraete)
        {
            var host = g.Umgebung == "sandbox" ? "api.sandbox.push.apple.com" : "api.push.apple.com";
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/3/device/{g.Token}")
            {
                Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                Content = new StringContent(inhalt, Encoding.UTF8, "application/json"),
            };
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
            anfrage.Headers.TryAddWithoutValidation("apns-topic", g.Bundle.Length > 0 ? g.Bundle : k.Topic);
            anfrage.Headers.TryAddWithoutValidation("apns-push-type", "alert");
            anfrage.Headers.TryAddWithoutValidation("apns-priority", "10");
            try
            {
                using var antwort = await Client().SendAsync(anfrage, ct);
                if (antwort.IsSuccessStatusCode) { ok++; continue; }
                var grund = "";
                try { grund = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("reason").GetString() ?? ""; } catch { /* ohne Grund */ }
                // Token abgelaufen bzw. ungültig: entfernen (410 Unregistered, 400 BadDeviceToken)
                if (antwort.StatusCode == HttpStatusCode.Gone || grund is "BadDeviceToken" or "Unregistered")
                    GeraetLoeschen(null, g.Token);
                else _log.LogWarning("APNs: {Status} {Grund}", (int)antwort.StatusCode, grund);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _log.LogWarning(ex, "APNs nicht erreichbar"); }
        }
        return ok;
    }

    // ------------------------------------------------------------------------------------------ Änderungen nach dem Import

    private static string Signatur(Karte k) =>
        $"{k.Von}-{k.Bis}|{k.Status}|{k.Fach}|{k.FachAlt}|{k.Unterricht}|{string.Join(",", k.Klassen.Select(w => w.K + "/" + w.Alt))}|" +
        $"{string.Join(",", k.Lehrer.Select(w => w.K + "/" + w.Alt))}|{string.Join(",", k.Raeume.Select(w => w.K + "/" + w.Alt))}";

    /// <summary>
    /// Geänderte Stunden je Lehrkraft (aktuelle und nächste Woche ab heute): Karten, deren Status, Fach, Klassen, Lehrkräfte oder Räume sich
    /// gegenüber dem vorherigen Paket geändert haben bzw. die neu dazukamen oder weggefallen sind. Ohne vorheriges Paket: keine.
    /// </summary>
    public Dictionary<string, List<PlanAenderung>> Aenderungen(Stand? alt, Stand neu, IEnumerable<string> lehrer, DateOnly heute)
    {
        var res = new Dictionary<string, List<PlanAenderung>>(Ci);
        if (alt is null) return res;
        var von = heute;
        var bis = Plandienst.Montag(heute).AddDays(13);
        foreach (var l in lehrer.Where(x => x.Length > 0).Distinct(Ci))
        {
            var liste = new List<PlanAenderung>();
            for (var d = von; d <= bis; d = d.AddDays(1))
            {
                var vorher = _plan.Frei(alt, d).Frei ? new List<Karte>() : _plan.Karten(alt, d, Elementtyp.Lehrer, l);
                var nachher = _plan.Frei(neu, d).Frei ? new List<Karte>() : _plan.Karten(neu, d, Elementtyp.Lehrer, l);
                var a = vorher.Select(Signatur).ToHashSet();
                var n = nachher.Select(Signatur).ToHashSet();
                var geaendert = nachher.Where(k => !a.Contains(Signatur(k))).Concat(vorher.Where(k => !n.Contains(Signatur(k)) && !nachher.Any(x => x.Von <= k.Bis && x.Bis >= k.Von)))
                    .Select(k => new PlanAenderung(k.Datum, k.Von, k.Bis, k.Fach, string.Join(",", k.Klassen.Select(w => w.K.Length > 0 ? w.K : w.Alt ?? "").Where(x => x.Length > 0)),
                        nachher.Contains(k) ? k.Status : "weg"))
                    .DistinctBy(x => (x.Datum, x.Von)).OrderBy(x => x.Von);
                liste.AddRange(geaendert);
            }
            if (liste.Count > 0) res[l] = liste;
        }
        return res;
    }

    /// <summary>Text der Benachrichtigung: bis drei Stunden einzeln („Mo 3. Std. D 7a entfällt“), sonst gebündelt.</summary>
    public static (string Titel, string Text) Nachricht(IReadOnlyList<PlanAenderung> liste)
    {
        static string Tag(string iso) => DateOnly.TryParse(iso, out var d) ? Tage[((int)d.DayOfWeek + 6) % 7] : "";
        static string Kurz(PlanAenderung a) =>
            $"{Tag(a.Datum)} {(a.Von == a.Bis ? $"{a.Von}." : $"{a.Von}.–{a.Bis}.")} Std. {string.Join(" ", new[] { a.Fach, a.Klassen }.Where(x => x.Length > 0))}"
            + (a.Status is "entfall" or "weg" ? " entfällt" : a.Status == "abwesend" ? " wird vertreten" : "");
        if (liste.Count <= 3)
            return ("Änderung im Stundenplan", string.Join("; ", liste.Select(Kurz)));
        var erste = liste.Min(a => a.Datum)!;
        var d = DateOnly.Parse(erste);
        return ("Änderungen im Stundenplan", $"{liste.Count} Änderungen ab {Tag(erste)} {d:dd.MM.} – bitte den Stundenplan prüfen.");
    }

    /// <summary>Nach einem Paket-Import: Änderungen ermitteln und im Hintergrund senden (nie blockierend, Fehler werden nur protokolliert).</summary>
    private void NachImport(Stand? alt, Stand neu)
    {
        try
        {
            if (alt is null || !Konfiguration().Vollstaendig) return;
            var geraete = Geraete();
            if (geraete.Count == 0) return;
            var heute = _uhr.Heute;
            LetzterVersand = Task.Run(async () =>
            {
                try
                {
                    // Lehrkraft je Konto mit registriertem Gerät
                    var konten = geraete.Select(g => g.BenutzerId).Distinct()
                        .Select(id => (Id: id, Kuerzel: _benutzer.Holen(id) is { Gesperrt: false } b ? b.LehrerKuerzel : null))
                        .Where(x => x.Kuerzel != null).ToList();
                    if (konten.Count == 0) return;
                    var aenderungen = Aenderungen(alt, neu, konten.Select(x => x.Kuerzel!), heute);
                    foreach (var (kuerzel, liste) in aenderungen)
                    {
                        var (titel, text) = Nachricht(liste);
                        await SendenAsync(konten.Where(x => Ci.Equals(x.Kuerzel, kuerzel)).Select(x => x.Id), titel, text, liste.Min(a => a.Datum)!);
                    }
                }
                catch (Exception ex) { _log.LogWarning(ex, "Benachrichtigungen nach dem Import fehlgeschlagen"); }
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Benachrichtigungen nach dem Import fehlgeschlagen"); }
    }
}
