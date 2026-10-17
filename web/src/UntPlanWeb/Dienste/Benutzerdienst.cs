using Microsoft.AspNetCore.Identity;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Rollen: Administration, Schulleitung, Lehrkraft, Schüler*in (bzw. Klasse), Erziehungsberechtigte.</summary>
public static class Rollen
{
    public const string Admin = "admin";
    public const string Leitung = "leitung";
    public const string Lehrer = "lehrer";
    public const string Schueler = "schueler";
    public const string Eltern = "eltern";

    public static readonly string[] Alle = { Admin, Leitung, Lehrer, Schueler, Eltern };

    public static bool Gueltig(string rolle) => Alle.Contains(rolle);

    public static string Name(string rolle) => rolle switch
    {
        Admin => "Administration",
        Leitung => "Schulleitung",
        Lehrer => "Lehrkraft",
        Schueler => "Schüler*in",
        Eltern => "Erziehungsberechtigte*r",
        _ => rolle,
    };
}

/// <summary>
/// Zusatzrechte: einzelne Rechte, die einem Konto zusätzlich zu seiner Rolle gegeben werden können (Administration → Benutzer).
/// </summary>
public static class Zusatzrechte
{
    public const string Nachrichten = "nachrichten";
    public const string Schuelerverwaltung = "schuelerverwaltung";
    public const string KlassenbuchAlle = "klassenbuch_alle";
    public const string OffeneStunden = "offene_stunden";

    public static readonly (string Id, string Name, string Beschreibung)[] Alle =
    {
        (Nachrichten, "Nachrichten zum Tag", "Nachrichten zum Tag pflegen und Mitteilungen an alle Benutzer senden (wie die Schulleitung)."),
        (Schuelerverwaltung, "Schülerverwaltung", "Schüler*innen aller Klassen anlegen, bearbeiten und löschen (nur Kollegium)."),
        (KlassenbuchAlle, "Klassenbuch – alle", "Klassenbuch aller Lehrkräfte einsehen, z. B. die offenen Stunden aller Lehrkräfte (nur Kollegium)."),
        (OffeneStunden, "Offene Stunden", "Offene Stunden aller Lehrkräfte kontrollieren und Lehrkräfte daran erinnern (nur Kollegium)."),
    };

    /// <summary>Bekannte Rechte aus einer Liste bzw. kommagetrennten Angabe (unbekannte werden ignoriert).</summary>
    public static List<string> Bereinigen(IEnumerable<string>? rechte) =>
        (rechte ?? Enumerable.Empty<string>()).Select(r => r.Trim().ToLowerInvariant()).Where(r => Alle.Any(a => a.Id == r)).Distinct().OrderBy(r => r).ToList();
}

public sealed record Benutzer(int Id, string Login, string Anzeigename, string Rolle, string Element, string Email, bool Gesperrt, bool PasswortAendern,
    string IcalToken, string Erstellt, string? LetzteAnmeldung, string? VorherigeAnmeldung = null, string Sprache = "Deutsch", bool Benachrichtigung = false)
{
    /// <summary>Kollegium (Lehrkräfte, Schulleitung, Administration): alle Pläne, Lehrerversion des Vertretungsplans.</summary>
    public bool Kollegium => Rolle is Rollen.Admin or Rollen.Leitung or Rollen.Lehrer;
    public bool IstAdmin => Rolle == Rollen.Admin;
    /// <summary>Nachrichten zum Tag pflegen (Recht „nachrichten.pflegen“; bisher Schulleitung, Administration und Zusatzrecht „Nachrichten“).</summary>
    public bool DarfNachrichten => Rechtedienst.Darf(this, Recht.NachrichtenPflegen);
    /// <summary>Effektive Rechte (Rechte-Modell, siehe <see cref="Rechtedienst"/>); null = Standardprofil der Basisrolle.</summary>
    public IReadOnlyDictionary<string, int>? Rechte { get; init; }
    /// <summary>Planfenster der Rolle: Wochen zurück bzw. voraus, die der Stundenplan angezeigt wird (Standard: unbegrenzt).</summary>
    public Planfenster Planfenster { get; init; }
    /// <summary>Zusatzrechte des Kontos (siehe <see cref="UntPlanWeb.Dienste.Zusatzrechte"/>).</summary>
    public List<string> Zusatzrechte { get; init; } = new();
    /// <summary>Zugeordnete Person aus der Personenliste (sonstiges Personal, Erziehungsberechtigte) – Lehrkräfte über das Kürzel, Schüler*innen über die Schülerzuordnung.</summary>
    public int? PersonId { get; init; }
    /// <summary>Lehrkraft mit Kürzel (eigener Plan, Meldungen, Klassenbuch).</summary>
    public string? LehrerKuerzel => Kollegium && Element.Length > 0 ? Element : null;
    /// <summary>Schüler- bzw. Elternkonto (eigene Seiten: Abwesenheiten, Fehlzeiten, Hausaufgaben des Kindes).</summary>
    public bool Familie => Rolle is Rollen.Schueler or Rollen.Eltern;
    /// <summary>Zugeordnete Schüler*innen (Schülerverwaltung): bei Schüler*innen die eigene Person, bei Erziehungsberechtigten die Kinder.</summary>
    public List<int> SchuelerIds { get; init; } = new();
    /// <summary>Klassen des Kontos: Klassen der zugeordneten Schüler*innen, sonst (Klassenkonto) die eingetragene Klasse.</summary>
    public List<string> Klassen { get; init; } = new();
    /// <summary>Klasse für „Mein Stundenplan“ (erste Klasse des Kontos).</summary>
    public string? KlassenKuerzel => Familie ? Klassen.FirstOrDefault() ?? (Element.Length > 0 ? Element : null) : null;
    /// <summary>Darf dieses Konto den Plan der Klasse sehen (eigene Klasse bzw. Klasse eines Kindes)?</summary>
    public bool HatKlasse(string klasse) => Familie && (Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase) || (Klassen.Count == 0 && Element.Equals(klasse, StringComparison.OrdinalIgnoreCase)));
}

public sealed class Benutzerdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst? _pakete;
    private readonly PasswordHasher<string> _hasher = new();
    public const int MaxFehlversuche = 5;

    public Benutzerdienst(Db db, Uhr uhr, Paketdienst? pakete = null) { _db = db; _uhr = uhr; _pakete = pakete; }

    /// <summary>Zugeordnete Schüler*innen und deren Klassen (aus der aktuellen Schülerliste) ergänzen.</summary>
    private Benutzer Ergaenzen(Benutzer b)
    {
        b = b with { Rechte = Rechtedienst.Laden(_db, b), Planfenster = Rechtedienst.PlanfensterLaden(_db, b) };
        if (!b.Familie) return b;
        var ids = SchuelerVon(b.Id);
        var schueler = _pakete?.Aktuell?.Schueler;
        var klassen = schueler is null ? new List<string>() : ids.Select(i => schueler.FirstOrDefault(x => x.Id == i)?.Klasse).Where(k => !string.IsNullOrEmpty(k)).Select(k => k!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return b with { SchuelerIds = ids, Klassen = klassen };
    }

    /// <summary>Ids der einem Konto zugeordneten Schüler*innen.</summary>
    public List<int> SchuelerVon(int benutzerId) =>
        _db.Liste("SELECT schueler_id FROM benutzer_schueler WHERE benutzer_id = @b ORDER BY schueler_id", r => r.Zahl("schueler_id"), Db.P("@b", benutzerId));

    /// <summary>
    /// Schüler*innen einem Schüler- bzw. Elternkonto zuordnen (ersetzt die bisherige Zuordnung). Schülerkonten gehören zu genau einer
    /// Person; Elternkonten können mehrere Kinder haben.
    /// </summary>
    public void SchuelerZuordnen(int benutzerId, IEnumerable<int> schuelerIds)
    {
        var b = Holen(benutzerId) ?? throw new ArgumentException("Unbekannter Benutzer.");
        var ids = schuelerIds.Distinct().ToList();
        if (!b.Familie && ids.Count > 0) throw new ArgumentException("Schüler*innen können nur Schüler- und Elternkonten zugeordnet werden.");
        if (b.Rolle == Rollen.Schueler && ids.Count > 1) throw new ArgumentException("Ein Schülerkonto gehört zu genau einer Schülerin bzw. einem Schüler.");
        if (_pakete?.Aktuell is { } s && ids.FirstOrDefault(i => !s.Schueler.Any(x => x.Id == i)) is int unbekannt and not 0)
            throw new ArgumentException($"Unbekannte Schülerin / unbekannter Schüler ({unbekannt}).");
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "DELETE FROM benutzer_schueler WHERE benutzer_id = @b", Db.P("@b", benutzerId));
            foreach (var i in ids) Db.Ausfuehren(c, "INSERT INTO benutzer_schueler (benutzer_id, schueler_id) VALUES (@b, @s)", Db.P("@b", benutzerId), Db.P("@s", i));
        });
    }

    private const string Spalten = "id, login, anzeigename, rolle, element, email, gesperrt, passwort_aendern, ical_token, erstellt, letzte_anmeldung, vorherige_anmeldung, sprache, benachrichtigung, zusatzrechte, person_id";

    private static Benutzer Lesen(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("login"), r.Text("anzeigename"), r.Text("rolle"),
        r.Text("element"), r.Text("email"), r.Wahr("gesperrt"), r.Wahr("passwort_aendern"), r.Text("ical_token"), r.Text("erstellt"), r.TextOderNull("letzte_anmeldung"),
        r.TextOderNull("vorherige_anmeldung"), r.Text("sprache"), r.Wahr("benachrichtigung"))
    {
        Zusatzrechte = r.Text("zusatzrechte").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        PersonId = r["person_id"] is DBNull or null ? null : Convert.ToInt32(r["person_id"]),
    };

    public bool Keiner => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM benutzer WHERE rolle = 'admin'")) == 0;

    public Benutzer? Holen(int id) => _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE id = @id", Lesen, Db.P("@id", id)) is { } b ? Ergaenzen(b) : null;
    public Benutzer? NachLogin(string login) => _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE login = @l", Lesen, Db.P("@l", login.Trim())) is { } b ? Ergaenzen(b) : null;
    public Benutzer? NachIcal(string token) => string.IsNullOrWhiteSpace(token) ? null : _db.Einzel($"SELECT {Spalten} FROM benutzer WHERE ical_token = @t AND gesperrt = 0", Lesen, Db.P("@t", token)) is { } b ? Ergaenzen(b) : null;

    public List<Benutzer> Alle()
    {
        var liste = _db.Liste($"SELECT {Spalten} FROM benutzer ORDER BY rolle, login COLLATE NOCASE", Lesen);
        var zuordnung = _db.Liste("SELECT benutzer_id, schueler_id FROM benutzer_schueler", r => (B: r.Zahl("benutzer_id"), S: r.Zahl("schueler_id"))).ToLookup(x => x.B, x => x.S);
        var schueler = _pakete?.Aktuell?.Schueler;
        liste = Rechtedienst.Laden(_db, liste);
        return liste.Select(b => !b.Familie ? b : b with
        {
            SchuelerIds = zuordnung[b.Id].ToList(),
            Klassen = schueler is null ? new() : zuordnung[b.Id].Select(i => schueler.FirstOrDefault(x => x.Id == i)?.Klasse).Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        }).ToList();
    }

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
    public (Anmeldeergebnis, Benutzer?) Anmelden(string login, string passwort, string? adresse = null, string? geraet = null)
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
        if (zeile.Gesperrt) { AnmeldungVermerken(zeile.Id, "gesperrt", adresse, geraet); return (Anmeldeergebnis.Gesperrt, null); }
        if (zeile.Bis is string bis && DateTime.TryParse(bis, out var b) && b > _uhr.Jetzt) return (Anmeldeergebnis.VoruebergehendGesperrt, null);
        var ergebnis = _hasher.VerifyHashedPassword(zeile.Login, zeile.Hash, passwort ?? "");
        if (ergebnis == PasswordVerificationResult.Failed)
        {
            var fehl = zeile.Fehl + 1;
            _db.Ausfuehren("UPDATE benutzer SET fehlversuche = @f, gesperrt_bis = @b WHERE id = @id", Db.P("@f", fehl >= MaxFehlversuche ? 0 : fehl),
                Db.P("@b", fehl >= MaxFehlversuche ? _uhr.Jetzt.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss") : null), Db.P("@id", zeile.Id));
            AnmeldungVermerken(zeile.Id, "falsch", adresse, geraet);
            return (Anmeldeergebnis.Falsch, null);
        }
        if (ergebnis == PasswordVerificationResult.SuccessRehashNeeded)
            _db.Ausfuehren("UPDATE benutzer SET passwort = @p WHERE id = @id", Db.P("@p", _hasher.HashPassword(zeile.Login, passwort!)), Db.P("@id", zeile.Id));
        // Die bisherige Anmeldung bleibt als „Letzte Anmeldung“ für die Startseite erhalten
        _db.Ausfuehren("UPDATE benutzer SET fehlversuche = 0, gesperrt_bis = NULL, vorherige_anmeldung = letzte_anmeldung, letzte_anmeldung = @z WHERE id = @id",
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")), Db.P("@id", zeile.Id));
        AnmeldungVermerken(zeile.Id, "ok", adresse, geraet);
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

    // ---------------------------------------------------------------- Benutzerverwaltung: Zuordnung, Zusatzrechte, Anmeldehistorie

    public sealed record Anmeldung(string Zeit, string Ergebnis, string Adresse, string Geraet);

    /// <summary>Anmeldeversuch vermerken (die letzten 50 je Konto bleiben erhalten).</summary>
    private void AnmeldungVermerken(int id, string ergebnis, string? adresse, string? geraet)
    {
        _db.Ausfuehren("INSERT INTO benutzer_anmeldungen (benutzer_id, zeit, ergebnis, adresse, geraet) VALUES (@b, @z, @e, @a, @g)",
            Db.P("@b", id), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")), Db.P("@e", ergebnis),
            Db.P("@a", Kuerzen(adresse, 60)), Db.P("@g", Kuerzen(geraet, 200)));
        var grenze = _db.Wert("SELECT id FROM benutzer_anmeldungen WHERE benutzer_id = @b ORDER BY id DESC LIMIT 1 OFFSET 49", Db.P("@b", id));
        if (grenze is not null and not DBNull)
            _db.Ausfuehren("DELETE FROM benutzer_anmeldungen WHERE benutzer_id = @b AND id < @g", Db.P("@b", id), Db.P("@g", Convert.ToInt64(grenze)));
    }

    private static string Kuerzen(string? s, int n) => (s ?? "").Length <= n ? s ?? "" : s![..n];

    /// <summary>Anmeldehistorie eines Kontos (neueste zuerst).</summary>
    public List<Anmeldung> Anmeldungen(int id, int anzahl = 50) =>
        _db.Liste("SELECT zeit, ergebnis, adresse, geraet FROM benutzer_anmeldungen WHERE benutzer_id = @b ORDER BY id DESC LIMIT " + Math.Clamp(anzahl, 1, 200),
            r => new Anmeldung(r.Text("zeit"), r.Text("ergebnis"), r.Text("adresse"), r.Text("geraet")), Db.P("@b", id));

    public void ZusatzrechteSetzen(int id, IEnumerable<string>? rechte) =>
        _db.Ausfuehren("UPDATE benutzer SET zusatzrechte = @r WHERE id = @id", Db.P("@r", string.Join(",", Zusatzrechte.Bereinigen(rechte))), Db.P("@id", id));

    public void PersonSetzen(int id, int? personId) =>
        _db.Ausfuehren("UPDATE benutzer SET person_id = @p WHERE id = @id", Db.P("@p", personId), Db.P("@id", id));

    public void RolleSetzen(int id, string rolle)
    {
        if (!Rollen.Gueltig(rolle)) throw new ArgumentException("Unbekannte Rolle.");
        _db.Ausfuehren("UPDATE benutzer SET rolle = @r WHERE id = @id", Db.P("@r", rolle), Db.P("@id", id));
        // Schülerzuordnung gibt es nur bei Schüler- und Elternkonten
        if (rolle is not (Rollen.Schueler or Rollen.Eltern)) _db.Ausfuehren("DELETE FROM benutzer_schueler WHERE benutzer_id = @b", Db.P("@b", id));
    }

    public void PasswortAendernErzwingen(int id, bool ja = true) =>
        _db.Ausfuehren("UPDATE benutzer SET passwort_aendern = @a WHERE id = @id", Db.P("@a", ja), Db.P("@id", id));

    /// <summary>
    /// Eindeutige Zuordnung prüfen: ein Lehrerkürzel gehört höchstens einem Konto des Kollegiums, eine Schülerin bzw. ein Schüler
    /// höchstens einem Schülerkonto und eine Person der Personenliste höchstens einem Konto. <paramref name="id"/> = eigenes Konto (beim Ändern).
    /// </summary>
    public void ZuordnungPruefen(int? id, string rolle, string? element, IEnumerable<int>? schuelerIds, int? personId = null)
    {
        var alle = Alle().Where(b => b.Id != id).ToList();
        var kuerzel = (element ?? "").Trim();
        if (rolle is Rollen.Admin or Rollen.Leitung or Rollen.Lehrer && kuerzel.Length > 0 &&
            alle.FirstOrDefault(b => b.Kollegium && b.Element.Equals(kuerzel, StringComparison.OrdinalIgnoreCase)) is { } l)
            throw new ArgumentException($"Das Kürzel {kuerzel} ist bereits dem Konto „{l.Login}“ zugeordnet.");
        if (rolle == Rollen.Schueler && schuelerIds != null)
            foreach (var s in schuelerIds)
                if (alle.FirstOrDefault(b => b.Rolle == Rollen.Schueler && b.SchuelerIds.Contains(s)) is { } k)
                    throw new ArgumentException($"Diese Schülerin bzw. dieser Schüler hat bereits das Schülerkonto „{k.Login}“.");
        if (personId is int p && alle.FirstOrDefault(b => b.PersonId == p) is { } x)
            throw new ArgumentException($"Diese Person ist bereits dem Konto „{x.Login}“ zugeordnet.");
    }

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
        var vorhanden = Alle().Where(b => b.Rolle != Rollen.Eltern).Select(b => (b.Rolle == Rollen.Schueler ? "k:" : "l:") + b.Element.ToLowerInvariant()).ToHashSet();
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
