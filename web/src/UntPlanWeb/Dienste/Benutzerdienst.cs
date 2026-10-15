using Microsoft.AspNetCore.Identity;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Rollen: Administration, Schulleitung, Lehrkraft, Schüler/Klasse.</summary>
public static class Rollen
{
    public const string Admin = "admin";
    public const string Leitung = "leitung";
    public const string Lehrer = "lehrer";
    public const string Schueler = "schueler";

    public static readonly string[] Alle = { Admin, Leitung, Lehrer, Schueler };

    public static string Name(string rolle) => rolle switch
    {
        Admin => "Administration",
        Leitung => "Schulleitung",
        Lehrer => "Lehrkraft",
        Schueler => "Schüler*in / Klasse",
        _ => rolle,
    };
}

public sealed record Benutzer(int Id, string Login, string Anzeigename, string Rolle, string Element, string Email, bool Gesperrt, bool PasswortAendern,
    string IcalToken, string Erstellt, string? LetzteAnmeldung, string? VorherigeAnmeldung = null, string Sprache = "Deutsch", bool Benachrichtigung = false)
{
    /// <summary>Kollegium (Lehrkräfte, Schulleitung, Administration): alle Pläne, Lehrerversion des Vertretungsplans.</summary>
    public bool Kollegium => Rolle is Rollen.Admin or Rollen.Leitung or Rollen.Lehrer;
    public bool IstAdmin => Rolle == Rollen.Admin;
    public bool DarfNachrichten => Rolle is Rollen.Admin or Rollen.Leitung;
    /// <summary>Lehrkraft mit Kürzel (eigener Plan, Meldungen, Klassenbuch).</summary>
    public string? LehrerKuerzel => Kollegium && Element.Length > 0 ? Element : null;
    public string? KlassenKuerzel => Rolle == Rollen.Schueler && Element.Length > 0 ? Element : null;
}

public sealed class Benutzerdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly PasswordHasher<string> _hasher = new();
    public const int MaxFehlversuche = 5;

    public Benutzerdienst(Db db, Uhr uhr) { _db = db; _uhr = uhr; }

    private const string Spalten = "id, login, anzeigename, rolle, element, email, gesperrt, passwort_aendern, ical_token, erstellt, letzte_anmeldung, vorherige_anmeldung, sprache, benachrichtigung";

    private static Benutzer Lesen(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.Zahl("id"), r.Text("login"), r.Text("anzeigename"), r.Text("rolle"),
        r.Text("element"), r.Text("email"), r.Wahr("gesperrt"), r.Wahr("passwort_aendern"), r.Text("ical_token"), r.Text("erstellt"), r.TextOderNull("letzte_anmeldung"),
        r.TextOderNull("vorherige_anmeldung"), r.Text("sprache"), r.Wahr("benachrichtigung"));

    public bool Keiner => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM benutzer WHERE rolle = 'admin'")) == 0;

    public Benutzer? Holen(int id) => _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE id = @id", Lesen, Db.P("@id", id));
    public Benutzer? NachLogin(string login) => _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE login = @l", Lesen, Db.P("@l", login.Trim()));
    public Benutzer? NachIcal(string token) => string.IsNullOrWhiteSpace(token) ? null : _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE ical_token = @t AND gesperrt = 0", Lesen, Db.P("@t", token));

    public List<Benutzer> Alle() => _db.Liste($"SELECT {Spalten} FROM benutzer ORDER BY rolle, login COLLATE NOCASE", Lesen);

    public Benutzer Anlegen(string login, string passwort, string rolle, string element = "", string anzeigename = "", string email = "", bool passwortAendern = false)
    {
        login = login.Trim();
        if (login.Length is < 2 or > 60 || login.Any(c => char.IsWhiteSpace(c))) throw new ArgumentException("Der Benutzername muss 2–60 Zeichen ohne Leerzeichen haben.");
        if (!Rollen.Alle.Contains(rolle)) throw new ArgumentException("Unbekannte Rolle.");
        PasswortPruefen(passwort);
        if (NachLogin(login) != null) throw new ArgumentException($"Den Benutzer „{login}“ gibt es bereits.");
        var id = _db.Einfuegen("""
            INSERT INTO benutzer (login, anzeigename, rolle, element, email, passwort, passwort_aendern, ical_token, erstellt)
            VALUES (@l, @a, @r, @e, @m, @p, @pa, @t, @c)
            """, Db.P("@l", login), Db.P("@a", anzeigename.Trim()), Db.P("@r", rolle), Db.P("@e", element.Trim()), Db.P("@m", email.Trim()),
            Db.P("@p", _hasher.HashPassword(login, passwort)), Db.P("@pa", passwortAendern), Db.P("@t", Sicherheit.Zufall(32)), Db.P("@c", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")));
        return Holen((int)id)!;
    }

    public static void PasswortPruefen(string passwort)
    {
        if (string.IsNullOrEmpty(passwort) || passwort.Length < 8) throw new ArgumentException("Das Passwort muss mindestens 8 Zeichen lang sein.");
    }

    public enum Anmeldeergebnis { Ok, Falsch, Gesperrt, VoruebergehendGesperrt }

    /// <summary>Anmeldung mit Schutz gegen Ausprobieren: nach 5 Fehlversuchen 5 Minuten Pause.</summary>
    public (Anmeldeergebnis, Benutzer?) Anmelden(string login, string passwort)
    {
        var zeile = _db.Einzel("SELECT id, login, passwort, gesperrt, fehlversuche, gesperrt_bis FROM benutzer WHERE login = @l",
            r => new { Id = r.Zahl("id"), Login = r.Text("login"), Hash = r.Text("passwort"), Gesperrt = r.Wahr("gesperrt"), Fehl = r.Zahl("fehlversuche"), Bis = r.TextOderNull("gesperrt_bis") },
            Db.P("@l", (login ?? "").Trim()));
        if (zeile is null)
        {
            // gleich lange Prüfung wie bei vorhandenem Benutzer
            _hasher.VerifyHashedPassword("x", _hasher.HashPassword("x", "dummy-passwort"), passwort ?? "");
            return (Anmeldeergebnis.Falsch, null);
        }
        if (zeile.Gesperrt) return (Anmeldeergebnis.Gesperrt, null);
        if (zeile.Bis is string bis && DateTime.TryParse(bis, out var b) && b > _uhr.Jetzt) return (Anmeldeergebnis.VoruebergehendGesperrt, null);
        var ergebnis = _hasher.VerifyHashedPassword(zeile.Login, zeile.Hash, passwort ?? "");
        if (ergebnis == PasswordVerificationResult.Failed)
        {
            var fehl = zeile.Fehl + 1;
            _db.Ausfuehren("UPDATE benutzer SET fehlversuche = @f, gesperrt_bis = @b WHERE id = @id", Db.P("@f", fehl >= MaxFehlversuche ? 0 : fehl),
                Db.P("@b", fehl >= MaxFehlversuche ? _uhr.Jetzt.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss") : null), Db.P("@id", zeile.Id));
            return (Anmeldeergebnis.Falsch, null);
        }
        if (ergebnis == PasswordVerificationResult.SuccessRehashNeeded)
            _db.Ausfuehren("UPDATE benutzer SET passwort = @p WHERE id = @id", Db.P("@p", _hasher.HashPassword(zeile.Login, passwort!)), Db.P("@id", zeile.Id));
        // Die bisherige Anmeldung bleibt als „Letzte Anmeldung“ für die Startseite erhalten
        _db.Ausfuehren("UPDATE benutzer SET fehlversuche = 0, gesperrt_bis = NULL, vorherige_anmeldung = letzte_anmeldung, letzte_anmeldung = @z WHERE id = @id",
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")), Db.P("@id", zeile.Id));
        return (Anmeldeergebnis.Ok, Holen(zeile.Id));
    }

    /// <summary>Sicherheitsrichtlinie für selbst gewählte Passwörter (Administration → Rechte und Rollen).</summary>
    public sealed record Passwortregel(int MinLaenge = 8, bool Grossbuchstaben = false, bool Ziffern = false, bool Sonderzeichen = false);

    public Passwortregel Regel()
    {
        var json = _db.Wert("SELECT wert FROM einstellungen WHERE schluessel = 'einstellung:passwort'") as string;
        try { return json is null ? new Passwortregel() : System.Text.Json.JsonSerializer.Deserialize<Passwortregel>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new Passwortregel(); }
        catch (System.Text.Json.JsonException) { return new Passwortregel(); }
    }

    /// <summary>Prüft ein selbst gewähltes Passwort gegen die Sicherheitsrichtlinie.</summary>
    public void RegelPruefen(string passwort)
    {
        PasswortPruefen(passwort);
        var r = Regel();
        if (passwort.Length < Math.Max(8, r.MinLaenge)) throw new ArgumentException($"Das Passwort muss mindestens {Math.Max(8, r.MinLaenge)} Zeichen lang sein.");
        if (r.Grossbuchstaben && !passwort.Any(char.IsUpper)) throw new ArgumentException("Das Passwort muss einen Großbuchstaben enthalten.");
        if (r.Ziffern && !passwort.Any(char.IsDigit)) throw new ArgumentException("Das Passwort muss eine Ziffer enthalten.");
        if (r.Sonderzeichen && passwort.All(char.IsLetterOrDigit)) throw new ArgumentException("Das Passwort muss ein Sonderzeichen enthalten.");
    }

    /// <summary>Eigene Angaben (Profil): E-Mail, Sprache, Benachrichtigung über neue Mitteilungen.</summary>
    public void ProfilSpeichern(int id, string email, string sprache, bool benachrichtigung)
    {
        email = (email ?? "").Trim();
        if (email.Length > 0 && (!email.Contains('@') || email.Length > 200)) throw new ArgumentException("Bitte eine gültige E-Mail-Adresse eingeben.");
        _db.Ausfuehren("UPDATE benutzer SET email = @m, sprache = @s, benachrichtigung = @b WHERE id = @id",
            Db.P("@m", email), Db.P("@s", sprache is "English" ? "English" : "Deutsch"), Db.P("@b", benachrichtigung), Db.P("@id", id));
    }

    public void Sperren(int id, bool gesperrt) => _db.Ausfuehren("UPDATE benutzer SET gesperrt = @g WHERE id = @id", Db.P("@g", gesperrt), Db.P("@id", id));

    public void EmailSetzen(int id, string email) => _db.Ausfuehren("UPDATE benutzer SET email = @m WHERE id = @id", Db.P("@m", email.Trim()), Db.P("@id", id));

    public bool PasswortStimmt(int id, string passwort)
    {
        var z = _db.Einzel("SELECT login, passwort FROM benutzer WHERE id = @id", r => new { Login = r.Text("login"), Hash = r.Text("passwort") }, Db.P("@id", id));
        return z != null && _hasher.VerifyHashedPassword(z.Login, z.Hash, passwort ?? "") != PasswordVerificationResult.Failed;
    }

    public void PasswortSetzen(int id, string passwort, bool mussAendern = false)
    {
        PasswortPruefen(passwort);
        var b = Holen(id) ?? throw new ArgumentException("Unbekannter Benutzer.");
        _db.Ausfuehren("UPDATE benutzer SET passwort = @p, passwort_aendern = @a, fehlversuche = 0, gesperrt_bis = NULL WHERE id = @id",
            Db.P("@p", _hasher.HashPassword(b.Login, passwort)), Db.P("@a", mussAendern), Db.P("@id", id));
    }

    public void Aendern(int id, string anzeigename, string rolle, string element, string email, bool gesperrt)
    {
        if (!Rollen.Alle.Contains(rolle)) throw new ArgumentException("Unbekannte Rolle.");
        _db.Ausfuehren("UPDATE benutzer SET anzeigename = @a, rolle = @r, element = @e, email = @m, gesperrt = @g WHERE id = @id",
            Db.P("@a", anzeigename.Trim()), Db.P("@r", rolle), Db.P("@e", element.Trim()), Db.P("@m", email.Trim()), Db.P("@g", gesperrt), Db.P("@id", id));
    }

    public void Loeschen(int id) => _db.Ausfuehren("DELETE FROM benutzer WHERE id = @id", Db.P("@id", id));

    public string IcalNeu(int id)
    {
        var t = Sicherheit.Zufall(32);
        _db.Ausfuehren("UPDATE benutzer SET ical_token = @t WHERE id = @id", Db.P("@t", t), Db.P("@id", id));
        return t;
    }

    /// <summary>
    /// Konten aus den Stammdaten anlegen: je Lehrkraft (Benutzername = Kürzel) und je Klasse (klasse-7a) mit zufälligem
    /// Startpasswort, das bei der ersten Anmeldung geändert werden muss. Vorhandene Konten bleiben unverändert.
    /// </summary>
    public List<(string Login, string Passwort, string Rolle, string Element, string Name)> AusStammdaten(UntPlan.Web.Contracts.WebPaket p, bool lehrer, bool klassen,
        IEnumerable<string>? leitungKuerzel = null)
    {
        // Zuordnungsgruppen (Administration): diese Kürzel erhalten die Rolle Schulleitung
        var leitung = (leitungKuerzel ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var res = new List<(string, string, string, string, string)>();
        var vorhanden = Alle().Select(b => (b.Rolle == Rollen.Schueler ? "k:" : "l:") + b.Element.ToLowerInvariant()).ToHashSet();
        if (lehrer)
            foreach (var l in p.Elemente.Lehrer.Where(l => l.Kurz.Length > 0))
            {
                if (vorhanden.Contains("l:" + l.Kurz.ToLowerInvariant())) continue;
                var login = l.Kurz.ToLowerInvariant();
                if (NachLogin(login) != null) login = "lehrer-" + login;
                if (NachLogin(login) != null) continue;
                var pw = Sicherheit.Zufall(10);
                var name = string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var rolle = leitung.Contains(l.Kurz) ? Rollen.Leitung : Rollen.Lehrer;
                Anlegen(login, pw, rolle, l.Kurz, name, l.Email ?? "", true);
                res.Add((login, pw, rolle, l.Kurz, name));
            }
        if (klassen)
            foreach (var k in p.Elemente.Klassen.Where(k => k.Kurz.Length > 0))
            {
                if (vorhanden.Contains("k:" + k.Kurz.ToLowerInvariant())) continue;
                var login = "klasse-" + Sicherheit.Slug(k.Kurz);
                if (NachLogin(login) != null) continue;
                var pw = Sicherheit.Zufall(10);
                Anlegen(login, pw, Rollen.Schueler, k.Kurz, "Klasse " + k.Kurz, "", false);
                res.Add((login, pw, Rollen.Schueler, k.Kurz, "Klasse " + k.Kurz));
            }
        return res;
    }
}
