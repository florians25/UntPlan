using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>Rollen der Studio-Benutzer.</summary>
public static class Rollen
{
    /// <summary>Alles, inkl. Benutzerverwaltung.</summary>
    public const string Admin = "Admin";
    /// <summary>Dokumente öffnen und bearbeiten.</summary>
    public const string Planer = "Planer";
    /// <summary>Nur lesen (keine Änderungen, kein Speichern).</summary>
    public const string Leser = "Leser";

    public static readonly string[] Alle = { Admin, Planer, Leser };

    public static bool DarfSchreiben(ClaimsPrincipal p) => p.IsInRole(Admin) || p.IsInRole(Planer);
}

public sealed class StudioBenutzer
{
    public string Name { get; set; } = "";
    public string Anzeigename { get; set; } = "";
    public string Rolle { get; set; } = Rollen.Planer;
    public string Hash { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public DateTime? LetzteAnmeldung { get; set; }
}

/// <summary>
/// Einfache Benutzerverwaltung (Datei <c>benutzer.json</c> im Datenordner, Passwort-Hash nach ASP.NET Core Identity / PBKDF2).
/// Beim ersten Start wird das Admin-Konto aus STUDIO_ADMIN_BENUTZER/STUDIO_ADMIN_PASSWORT angelegt. Später: MultiUser-Anbindung.
/// </summary>
public sealed class Benutzerdienst
{
    private static readonly JsonSerializerOptions Optionen = new() { WriteIndented = true };
    private readonly PasswordHasher<string> _hasher = new();
    private readonly string _datei;
    private readonly object _sperre = new();
    private List<StudioBenutzer> _benutzer;

    public Benutzerdienst(StudioKonfiguration k, ILogger<Benutzerdienst> log)
    {
        _datei = Path.Combine(k.Datenordner, "benutzer.json");
        _benutzer = File.Exists(_datei) ? JsonSerializer.Deserialize<List<StudioBenutzer>>(File.ReadAllText(_datei)) ?? new() : new();
        if (_benutzer.Count == 0)
        {
            var passwort = k.AdminPasswort;
            if (passwort is null)
            {
                passwort = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
                log.LogWarning("UntPlan Studio: Admin-Konto \"{Benutzer}\" mit erzeugtem Passwort {Passwort} angelegt (STUDIO_ADMIN_PASSWORT setzen, um es vorzugeben).", k.AdminBenutzer, passwort);
            }
            _benutzer.Add(new StudioBenutzer { Name = k.AdminBenutzer, Anzeigename = "Administrator", Rolle = Rollen.Admin, Hash = _hasher.HashPassword(k.AdminBenutzer, passwort) });
            Schreiben();
        }
    }

    public IReadOnlyList<StudioBenutzer> Alle()
    {
        lock (_sperre) return _benutzer.ToList();
    }

    public StudioBenutzer? Holen(string name)
    {
        lock (_sperre) return _benutzer.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Prüft Benutzername und Passwort; liefert den Benutzer oder null.</summary>
    public StudioBenutzer? Pruefen(string name, string passwort)
    {
        lock (_sperre)
        {
            var b = _benutzer.FirstOrDefault(x => x.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (b is null || string.IsNullOrEmpty(passwort)) return null;
            var erg = _hasher.VerifyHashedPassword(b.Name, b.Hash, passwort);
            if (erg == PasswordVerificationResult.Failed) return null;
            if (erg == PasswordVerificationResult.SuccessRehashNeeded) b.Hash = _hasher.HashPassword(b.Name, passwort);
            b.LetzteAnmeldung = DateTime.Now;
            Schreiben();
            return b;
        }
    }

    public StudioBenutzer Anlegen(string name, string anzeigename, string rolle, string passwort)
    {
        name = (name ?? "").Trim();
        if (name.Length < 2 || name.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '.'))
            throw StudioFehler.Ungueltig("Der Benutzername muss mindestens 2 Zeichen lang sein und darf keine Leerzeichen, Punkte oder Schrägstriche enthalten.");
        if (!Rollen.Alle.Contains(rolle)) throw StudioFehler.Ungueltig($"Unbekannte Rolle \"{rolle}\".");
        PasswortPruefen(passwort);
        lock (_sperre)
        {
            if (_benutzer.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw StudioFehler.Ungueltig($"Den Benutzer \"{name}\" gibt es bereits.");
            var neu = new StudioBenutzer { Name = name, Anzeigename = string.IsNullOrWhiteSpace(anzeigename) ? name : anzeigename.Trim(), Rolle = rolle, Hash = _hasher.HashPassword(name, passwort) };
            _benutzer.Add(neu);
            Schreiben();
            return neu;
        }
    }

    public void PasswortSetzen(string name, string passwort)
    {
        PasswortPruefen(passwort);
        lock (_sperre)
        {
            var b = _benutzer.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw StudioFehler.NichtGefunden("Der Benutzer");
            b.Hash = _hasher.HashPassword(b.Name, passwort);
            Schreiben();
        }
    }

    public void Loeschen(string name)
    {
        lock (_sperre)
        {
            var b = _benutzer.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw StudioFehler.NichtGefunden("Der Benutzer");
            if (b.Rolle == Rollen.Admin && _benutzer.Count(x => x.Rolle == Rollen.Admin) == 1)
                throw StudioFehler.Ungueltig("Der letzte Administrator kann nicht gelöscht werden.");
            _benutzer.Remove(b);
            Schreiben();
        }
    }

    private static void PasswortPruefen(string passwort)
    {
        if (string.IsNullOrEmpty(passwort) || passwort.Length < 6) throw StudioFehler.Ungueltig("Das Passwort muss mindestens 6 Zeichen lang sein.");
    }

    private void Schreiben()
    {
        var tmp = _datei + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_benutzer, Optionen));
        File.Move(tmp, _datei, true);
    }
}
