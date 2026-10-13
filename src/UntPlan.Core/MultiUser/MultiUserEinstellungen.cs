using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UntPlan.Core.MultiUser;

/// <summary>
/// Lokale MultiUser-Einstellungen des Arbeitsplatzes (%APPDATA%/UntPlan/multiuser.json): Verbindung und zuletzt gewählte Anmeldung.
/// Das Datenbank-Passwort wird unter Windows mit DPAPI (Benutzerkonto) geschützt, sonst nur Base64-kodiert.
/// </summary>
public sealed class MultiUserEinstellungen
{
    private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("UntPlan.MultiUser");

    public Verbindungsdaten Verbindung { get; set; } = new();
    /// <summary>Geschütztes Datenbank-Passwort ("dpapi:…" oder "b64:…").</summary>
    public string PasswortGeschuetzt { get; set; } = "";
    public string LetzteSchule { get; set; } = "";
    public string LetztesSchuljahr { get; set; } = "";
    public int LetzteVersion { get; set; }
    public string LetzterBenutzer { get; set; } = "";
    /// <summary>Beim Programmstart automatisch den Anmeldedialog zeigen.</summary>
    public bool BeimStartAnmelden { get; set; }

    [JsonIgnore]
    public bool Eingerichtet => Verbindung.IstDateiDatenbank ? Verbindung.Datei.Length > 0 : Verbindung.Server.Length > 0;

    public static string StandardPfad =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan", "multiuser.json");

    private static readonly JsonSerializerOptions Optionen = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static MultiUserEinstellungen Laden(string? pfad = null)
    {
        try
        {
            var e = JsonSerializer.Deserialize<MultiUserEinstellungen>(File.ReadAllText(pfad ?? StandardPfad), Optionen) ?? new();
            e.Verbindung.Passwort = Entschluesseln(e.PasswortGeschuetzt);
            return e;
        }
        catch
        {
            return new();
        }
    }

    public void Speichern(string? pfad = null)
    {
        pfad ??= StandardPfad;
        PasswortGeschuetzt = Verschluesseln(Verbindung.Passwort);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfad))!);
        File.WriteAllText(pfad, JsonSerializer.Serialize(this, Optionen));
    }

    public static string Verschluesseln(string klartext)
    {
        if (string.IsNullOrEmpty(klartext)) return "";
        var bytes = Encoding.UTF8.GetBytes(klartext);
        if (OperatingSystem.IsWindows())
        {
            try { return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropie, DataProtectionScope.CurrentUser)); }
            catch { /* DPAPI nicht verfügbar */ }
        }
        return "b64:" + Convert.ToBase64String(bytes);
    }

    public static string Entschluesseln(string geschuetzt)
    {
        try
        {
            if (geschuetzt.StartsWith("dpapi:") && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(geschuetzt[6..]), Entropie, DataProtectionScope.CurrentUser));
            if (geschuetzt.StartsWith("b64:")) return Encoding.UTF8.GetString(Convert.FromBase64String(geschuetzt[4..]));
        }
        catch { /* unlesbar → Passwort neu eingeben */ }
        return "";
    }
}
