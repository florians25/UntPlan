namespace UntPlanStudio.Grundlagen;

/// <summary>
/// Einstellungen des Studio-Servers aus Umgebungsvariablen bzw. Konfiguration (siehe README, „Umgebungsvariablen“).
/// </summary>
public sealed class StudioKonfiguration
{
    /// <summary>Datenordner: Arbeitsdokumente (*.up), Benutzer, Benutzereinstellungen.</summary>
    public string Datenordner { get; init; } = "";
    public string AdminBenutzer { get; init; } = "admin";
    /// <summary>Passwort des Admin-Kontos beim ersten Start (leer = zufällig erzeugt und im Protokoll ausgegeben).</summary>
    public string? AdminPasswort { get; init; }
    /// <summary>Geänderte Dokumente werden spätestens nach so vielen Sekunden automatisch gespeichert.</summary>
    public int AutospeichernSekunden { get; init; } = 30;
    /// <summary>Nicht benutzte Dokumente werden nach so vielen Minuten gespeichert und aus dem Speicher entfernt.</summary>
    public int LeerlaufMinuten { get; init; } = 120;
    public int MaxUploadMb { get; init; } = 64;
    /// <summary>Anmeldecookie nur über HTTPS senden (null = automatisch nach Anfrage).</summary>
    public bool? SichereCookies { get; init; }

    public string Dokumentordner => Path.Combine(Datenordner, "dokumente");
    public string Benutzerordner => Path.Combine(Datenordner, "benutzer");

    public static StudioKonfiguration Laden(IConfiguration c, string inhaltsordner)
    {
        static int Zahl(string? s, int standard) => int.TryParse(s, out var v) && v > 0 ? v : standard;
        var daten = c["STUDIO_DATEN"];
        if (string.IsNullOrWhiteSpace(daten)) daten = Path.Combine(inhaltsordner, "daten");
        var sicher = c["STUDIO_SICHERE_COOKIES"]?.Trim().ToLowerInvariant();
        var k = new StudioKonfiguration
        {
            Datenordner = Path.GetFullPath(daten),
            AdminBenutzer = string.IsNullOrWhiteSpace(c["STUDIO_ADMIN_BENUTZER"]) ? "admin" : c["STUDIO_ADMIN_BENUTZER"]!.Trim(),
            AdminPasswort = string.IsNullOrWhiteSpace(c["STUDIO_ADMIN_PASSWORT"]) ? null : c["STUDIO_ADMIN_PASSWORT"],
            AutospeichernSekunden = Zahl(c["STUDIO_AUTOSPEICHERN_SEKUNDEN"], 30),
            LeerlaufMinuten = Zahl(c["STUDIO_LEERLAUF_MINUTEN"], 120),
            MaxUploadMb = Zahl(c["STUDIO_MAX_UPLOAD_MB"], 64),
            SichereCookies = sicher is "1" or "true" or "ja" ? true : sicher is "0" or "false" or "nein" ? false : null,
        };
        Directory.CreateDirectory(k.Dokumentordner);
        Directory.CreateDirectory(k.Benutzerordner);
        return k;
    }
}
