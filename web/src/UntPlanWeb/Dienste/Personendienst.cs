using System.Globalization;
using System.Text;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Person der Personenliste (Lehrkraft, Schüler*in, Erziehungsberechtigte*r, sonstiges Personal) mit dem zugeordneten Konto.</summary>
public sealed record Person(string Art, string Schluessel, string Vorname, string Name, string Kurz, string Klasse, string Email, string Funktion,
    List<int> Kinder, Personenkonto? Konto, List<Personenkonto> Elternkonten);

/// <summary>Kurzangaben eines Kontos in der Personenliste.</summary>
public sealed record Personenkonto(int Id, string Login, string Rolle, bool Gesperrt, string? LetzteAnmeldung);

/// <summary>Eingabe für sonstiges Personal bzw. Erziehungsberechtigte.</summary>
public sealed record PersonEingabe(string? Art, string? Vorname, string? Name, string? Funktion, string? Email, List<int>? Kinder);

/// <summary>Neu angelegtes Konto mit Startpasswort (für die Liste der Zugangsdaten).</summary>
public sealed record NeuesKonto(string Login, string Passwort, string Rolle, string Name, string Zuordnung, string Klasse);

/// <summary>
/// Personen und Konten: Lehrkräfte (Stammdaten aus UntPlan), Schüler*innen (Schülerverwaltung), Erziehungsberechtigte und sonstiges
/// Personal (in UntPlanWeb gepflegt, Tabelle personen). Zeigt, welche Personen ein Konto haben, und legt fehlende Konten nach einem
/// Login-Schema mit Startpasswort an.
/// </summary>
public sealed class Personendienst
{
    public const string Lehrer = "lehrer", Schueler = "schueler", Eltern = "eltern", Personal = "personal";
    public static readonly string[] Arten = { Lehrer, Schueler, Eltern, Personal };
    /// <summary>Login-Schemata für neue Konten.</summary>
    public static readonly string[] Schemata = { "kuerzel", "vorname.nachname", "v.nachname", "nachname.vorname", "nachname" };

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst _pakete;
    private readonly Benutzerdienst _benutzer;
    private static readonly StringComparer De = StringComparer.Create(new CultureInfo("de-DE"), true);

    public Personendienst(Db db, Uhr uhr, Paketdienst pakete, Benutzerdienst benutzer) { _db = db; _uhr = uhr; _pakete = pakete; _benutzer = benutzer; }

    private static Personenkonto Kurz(Benutzer b) => new(b.Id, b.Login, b.Rolle, b.Gesperrt, b.LetzteAnmeldung);

    private sealed record Zeile(int Id, string Art, string Vorname, string Name, string Funktion, string Email, List<int> Kinder);

    private List<Zeile> Gespeichert() => _db.Liste("SELECT id, art, vorname, name, funktion, email, kinder FROM personen ORDER BY name, vorname",
        r => new Zeile(r.Zahl("id"), r.Text("art"), r.Text("vorname"), r.Text("name"), r.Text("funktion"), r.Text("email"),
            r.Text("kinder").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).ToList()));

    /// <summary>Personen einer Art (oder aller Arten) mit Konto-Status.</summary>
    public List<Person> Liste(string? art)
    {
        var konten = _benutzer.Alle();
        var s = _pakete.Aktuell;
        var res = new List<Person>();
        if (art is null or Lehrer && s != null)
            foreach (var l in s.Paket.Elemente.Lehrer.Where(l => l.Kurz.Length > 0).OrderBy(l => l.Kurz, De))
            {
                var k = konten.FirstOrDefault(b => b.Kollegium && b.Element.Equals(l.Kurz, StringComparison.OrdinalIgnoreCase));
                res.Add(new Person(Lehrer, l.Kurz, l.Vorname, l.Name, l.Kurz, "", l.Email ?? "", "", new(), k is null ? null : Kurz(k), new()));
            }
        if (art is null or Schueler && s != null)
            foreach (var x in s.Schueler.OrderBy(x => x.Klasse, De).ThenBy(x => x.Name, De).ThenBy(x => x.Vorname, De))
            {
                var k = konten.FirstOrDefault(b => b.Rolle == Rollen.Schueler && b.SchuelerIds.Contains(x.Id));
                var eltern = konten.Where(b => b.Rolle == Rollen.Eltern && b.SchuelerIds.Contains(x.Id)).Select(Kurz).ToList();
                res.Add(new Person(Schueler, x.Id.ToString(), x.Vorname, x.Name, "", x.Klasse, "", "", new(), k is null ? null : Kurz(k), eltern));
            }
        if (art is null or Eltern or Personal)
            foreach (var z in Gespeichert().Where(z => art is null || z.Art == art))
            {
                var k = konten.FirstOrDefault(b => b.PersonId == z.Id);
                res.Add(new Person(z.Art, z.Id.ToString(), z.Vorname, z.Name, "", "", z.Email, z.Funktion, z.Kinder, k is null ? null : Kurz(k), new()));
            }
        return res;
    }

    // ---------------------------------------------------------------- Erziehungsberechtigte und sonstiges Personal

    private (string Art, string Vorname, string Name, string Funktion, string Email, string Kinder) Pruefen(PersonEingabe e)
    {
        var art = (e.Art ?? "").Trim().ToLowerInvariant();
        if (art is not (Eltern or Personal)) throw new ArgumentException("Personen können als Erziehungsberechtigte oder sonstiges Personal angelegt werden.");
        var name = (e.Name ?? "").Trim();
        var vorname = (e.Vorname ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Bitte den Nachnamen eingeben.");
        if (name.Length > 100 || vorname.Length > 100) throw new ArgumentException("Der Name ist zu lang.");
        var email = (e.Email ?? "").Trim();
        if (email.Length > 0 && (email.Length > 200 || !email.Contains('@') || email.Contains(' '))) throw new ArgumentException("Ungültige E-Mail-Adresse.");
        var funktion = (e.Funktion ?? "").Trim();
        if (funktion.Length > 100) throw new ArgumentException("Die Funktion ist zu lang.");
        var kinder = art == Eltern ? (e.Kinder ?? new()).Distinct().ToList() : new List<int>();
        if (_pakete.Aktuell is { } s && kinder.FirstOrDefault(i => !s.Schueler.Any(x => x.Id == i)) is int u and not 0)
            throw new ArgumentException($"Unbekannte Schülerin / unbekannter Schüler ({u}).");
        return (art, vorname, name, funktion, email, string.Join(",", kinder));
    }

    public Person Anlegen(PersonEingabe e)
    {
        var w = Pruefen(e);
        var id = (int)_db.Einfuegen("INSERT INTO personen (art, vorname, name, funktion, email, kinder, erstellt) VALUES (@a, @v, @n, @f, @m, @k, @z)",
            Db.P("@a", w.Art), Db.P("@v", w.Vorname), Db.P("@n", w.Name), Db.P("@f", w.Funktion), Db.P("@m", w.Email), Db.P("@k", w.Kinder),
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")));
        return Liste(w.Art).First(p => p.Schluessel == id.ToString());
    }

    public Person Aendern(int id, PersonEingabe e)
    {
        var w = Pruefen(e);
        if (_db.Ausfuehren("UPDATE personen SET art = @a, vorname = @v, name = @n, funktion = @f, email = @m, kinder = @k WHERE id = @id",
                Db.P("@a", w.Art), Db.P("@v", w.Vorname), Db.P("@n", w.Name), Db.P("@f", w.Funktion), Db.P("@m", w.Email), Db.P("@k", w.Kinder), Db.P("@id", id)) == 0)
            throw new ArgumentException("Unbekannte Person.");
        return Liste(w.Art).First(p => p.Schluessel == id.ToString());
    }

    public void Loeschen(int id)
    {
        _db.Ausfuehren("UPDATE benutzer SET person_id = NULL WHERE person_id = @id", Db.P("@id", id));
        _db.Ausfuehren("DELETE FROM personen WHERE id = @id", Db.P("@id", id));
    }

    public bool PersonGibtEs(int id) => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM personen WHERE id = @id", Db.P("@id", id))) > 0;

    // ---------------------------------------------------------------- Konten anlegen

    /// <summary>Login nach Schema (Kleinbuchstaben, Umlaute umschrieben, nur a–z, 0–9, Punkt und Bindestrich).</summary>
    public static string Login(string schema, string vorname, string name, string kurz)
    {
        static string N(string s)
        {
            s = s.Trim().ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
            var sb = new StringBuilder();
            foreach (var c in s.Normalize(NormalizationForm.FormD))
                if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
                else if (c is '-' or ' ' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            return sb.ToString().Trim('-');
        }
        var v = N(vorname);
        var n = N(name);
        var k = N(kurz);
        var login = schema switch
        {
            "kuerzel" when k.Length > 0 => k,
            "v.nachname" when v.Length > 0 => $"{v[0]}.{n}",
            "nachname.vorname" when v.Length > 0 => $"{n}.{v}",
            "nachname" => n,
            _ => v.Length > 0 ? $"{v}.{n}" : n,
        };
        login = login.Trim('.', '-');
        if (login.Length < 2) login = (login + "-konto").Trim('-');
        return login.Length > 50 ? login[..50].Trim('.', '-') : login;
    }

    private string FreierLogin(string basis, HashSet<string> vergeben)
    {
        var login = basis;
        for (var i = 2; vergeben.Contains(login) || _benutzer.NachLogin(login) != null; i++) login = basis + i;
        vergeben.Add(login);
        return login;
    }

    /// <summary>
    /// Konten für Personen ohne Konto anlegen. <paramref name="schluessel"/>: Kürzel (Lehrkräfte), Schüler-Ids bzw. Personen-Ids; leer = alle
    /// Personen der Art ohne Konto (bei Schüler*innen optional nur einer <paramref name="klasse"/>). Personen mit Konto werden übersprungen.
    /// </summary>
    public List<NeuesKonto> KontenAnlegen(string art, IEnumerable<string>? schluessel, string? klasse, string? schema, string? rolle, IEnumerable<string>? leitungKuerzel = null)
    {
        if (!Arten.Contains(art)) throw new ArgumentException("Unbekannte Personenart.");
        schema = Schemata.Contains(schema) ? schema! : art == Lehrer ? "kuerzel" : "vorname.nachname";
        if (rolle != null && rolle.Length > 0 && !Rollen.Gueltig(rolle)) throw new ArgumentException("Unbekannte Rolle.");
        var leitung = (leitungKuerzel ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wahl = schluessel?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kandidaten = Liste(art).Where(p => p.Konto is null && (wahl is not { Count: > 0 } || wahl.Contains(p.Schluessel))
            && (string.IsNullOrEmpty(klasse) || p.Klasse.Equals(klasse, StringComparison.OrdinalIgnoreCase))).ToList();
        var vergeben = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var res = new List<NeuesKonto>();
        foreach (var p in kandidaten)
        {
            var login = FreierLogin(Login(schema, p.Vorname, p.Name, p.Kurz), vergeben);
            var pw = Sicherheit.Zufall(10);
            var name = string.Join(" ", new[] { p.Vorname, p.Name }.Where(x => x.Length > 0));
            switch (art)
            {
                case Lehrer:
                {
                    var r = rolle is Rollen.Admin or Rollen.Leitung or Rollen.Lehrer ? rolle : leitung.Contains(p.Kurz) ? Rollen.Leitung : Rollen.Lehrer;
                    _benutzer.Anlegen(login, pw, r, p.Kurz, name, p.Email, true);
                    res.Add(new NeuesKonto(login, pw, r, name, p.Kurz, ""));
                    break;
                }
                case Schueler:
                {
                    var b = _benutzer.Anlegen(login, pw, Rollen.Schueler, p.Klasse, name, "", true);
                    _benutzer.SchuelerZuordnen(b.Id, new[] { int.Parse(p.Schluessel) });
                    res.Add(new NeuesKonto(login, pw, Rollen.Schueler, name, $"{p.Name}, {p.Vorname}", p.Klasse));
                    break;
                }
                default:
                {
                    var r = art == Eltern ? Rollen.Eltern : string.IsNullOrEmpty(rolle) || rolle is Rollen.Schueler or Rollen.Eltern ? Rollen.Lehrer : rolle;
                    var b = _benutzer.Anlegen(login, pw, r, "", name, p.Email, true);
                    _benutzer.PersonSetzen(b.Id, int.Parse(p.Schluessel));
                    if (r == Rollen.Eltern && p.Kinder.Count > 0) _benutzer.SchuelerZuordnen(b.Id, p.Kinder);
                    res.Add(new NeuesKonto(login, pw, r, name, p.Funktion, ""));
                    break;
                }
            }
        }
        return res;
    }

    /// <summary>Zugangsdaten als CSV (Semikolon, für Excel).</summary>
    public static string Csv(IEnumerable<NeuesKonto> konten)
    {
        static string F(string s) => s.Contains(';') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        var sb = new StringBuilder("Benutzername;Startpasswort;Rolle;Name;Zuordnung;Klasse\r\n");
        foreach (var k in konten) sb.Append(string.Join(";", new[] { k.Login, k.Passwort, Rollen.Name(k.Rolle), k.Name, k.Zuordnung, k.Klasse }.Select(F))).Append("\r\n");
        return sb.ToString();
    }
}
