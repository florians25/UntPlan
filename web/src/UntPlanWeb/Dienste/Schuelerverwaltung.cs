using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Schüler*in in der Schülerverwaltung (Liste aus UntPlan mit den in UntPlanWeb gepflegten Angaben).</summary>
public sealed record Schuelerdaten(int Id, string Vorname, string Name, string Klasse, string Geschlecht, string Geburtsdatum, string Email, string Bemerkung,
    string Quelle, string Geaendert, bool Bearbeiten);

/// <summary>Eingabe der Schülerverwaltung.</summary>
public sealed record SchuelerEingabe(string? Vorname, string? Name, string? Klasse, string? Geschlecht, string? Geburtsdatum, string? Email, string? Bemerkung);

/// <summary>
/// Schülerverwaltung über UntPlanWeb. Die Schülerliste kommt aus UntPlan; Änderungen, neue und gelöschte Schüler*innen werden
/// in UntPlanWeb gespeichert (Tabelle schueler_daten) und überlagern die übertragene Liste (auch nach einer neuen Übertragung).
/// Rechte:
/// <list type="bullet">
/// <item>Administration und Schulleitung: alle Klassen anzeigen, anlegen, bearbeiten (auch Klassenwechsel) und löschen.</item>
/// <item>Klassenleitung: Schüler*innen der eigenen Klassen anzeigen (mit persönlichen Angaben), anlegen und bearbeiten (ohne Klassenwechsel und Löschen).</item>
/// <item>übrige Lehrkräfte: nur Name, Klasse und Geschlecht anzeigen.</item>
/// <item>Klassen-/Schülerkonten: kein Zugriff.</item>
/// </list>
/// </summary>
public sealed class Schuelerverwaltung
{
    /// <summary>In UntPlanWeb angelegte Schüler*innen erhalten Ids ab diesem Wert (keine Überschneidung mit den Ids aus UntPlan).</summary>
    public const int ErsteEigeneId = 10_000_000;

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst _pakete;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);

    public Schuelerverwaltung(Db db, Uhr uhr, Paketdienst pakete) { _db = db; _uhr = uhr; _pakete = pakete; }

    private sealed record Zeile(int Id, string Vorname, string Name, string Klasse, string Geschlecht, string Geburtsdatum, string Email, string Bemerkung, bool Eigen, bool Geloescht, string Geaendert);

    private Dictionary<int, Zeile> Gespeichert() => _db.Liste("SELECT * FROM schueler_daten", r => new Zeile(r.Zahl("id"), r.Text("vorname"), r.Text("name"), r.Text("klasse"),
        r.Text("geschlecht"), r.Text("geburtsdatum"), r.Text("email"), r.Text("bemerkung"), r.Wahr("eigen"), r.Wahr("geloescht"), r.Text("geaendert"))).ToDictionary(z => z.Id);

    public static bool Verwalten(Benutzer b) => Rechtedienst.Darf(b, Recht.SchuelerVerwalten);

    /// <summary>Darf der Benutzer Schüler*innen dieser Klasse bearbeiten (persönliche Angaben)?</summary>
    public static bool DarfBearbeiten(Benutzer b, Stand s, string klasse) => Verwalten(b) || (b.Rolle == Rollen.Lehrer && Klassenbuchdienst.IstKlassenleitung(b, s, klasse));

    private static void Lesen(Benutzer b)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Die Schülerverwaltung steht nur dem Kollegium zur Verfügung.");
    }

    /// <summary>Schüler*innen (optional einer Klasse) mit den Angaben, die der Benutzer sehen darf.</summary>
    public List<Schuelerdaten> Liste(Benutzer b, Stand s, string? klasse)
    {
        Lesen(b);
        var gespeichert = Gespeichert();
        return s.Schueler.Where(x => klasse is null || Ci.Equals(x.Klasse, klasse))
            .OrderBy(x => x.Klasse, De).ThenBy(x => x.Name, De).ThenBy(x => x.Vorname, De)
            .Select(x =>
            {
                gespeichert.TryGetValue(x.Id, out var z);
                var bearbeiten = DarfBearbeiten(b, s, x.Klasse);
                return new Schuelerdaten(x.Id, x.Vorname, x.Name, x.Klasse, z?.Geschlecht ?? "", bearbeiten ? z?.Geburtsdatum ?? "" : "", bearbeiten ? z?.Email ?? "" : "",
                    bearbeiten ? z?.Bemerkung ?? "" : "", z?.Eigen == true ? "UntPlanWeb" : z != null ? "UntPlan (geändert)" : "UntPlan", z?.Geaendert ?? "", bearbeiten);
            }).ToList();
    }

    private static (string Vorname, string Name, string Klasse, string Geschlecht, string Geburtsdatum, string Email, string Bemerkung) Pruefen(Stand s, SchuelerEingabe e)
    {
        var vorname = (e.Vorname ?? "").Trim();
        var name = (e.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Bitte den Nachnamen eingeben.");
        if (vorname.Length > 100 || name.Length > 100) throw new ArgumentException("Der Name ist zu lang.");
        var klasse = (e.Klasse ?? "").Trim();
        if (!s.Klassen.TryGetValue(klasse, out var k)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        var geschlecht = (e.Geschlecht ?? "").Trim().ToLowerInvariant();
        if (geschlecht is not ("" or "m" or "w" or "d")) throw new ArgumentException("Unbekanntes Geschlecht.");
        var geburt = (e.Geburtsdatum ?? "").Trim();
        if (geburt.Length > 0 && !DateOnly.TryParseExact(geburt, "yyyy-MM-dd", out _)) throw new ArgumentException("Ungültiges Geburtsdatum.");
        var email = (e.Email ?? "").Trim();
        if (email.Length > 0 && (email.Length > 200 || !email.Contains('@') || email.Contains(' '))) throw new ArgumentException("Ungültige E-Mail-Adresse.");
        var bemerkung = (e.Bemerkung ?? "").Trim();
        if (bemerkung.Length > 1000) throw new ArgumentException("Die Bemerkung ist zu lang (höchstens 1000 Zeichen).");
        return (vorname, name, k.Kurz, geschlecht, geburt, email, bemerkung);
    }

    public Schuelerdaten Anlegen(Benutzer b, Stand s, SchuelerEingabe e)
    {
        Lesen(b);
        var w = Pruefen(s, e);
        if (!DarfBearbeiten(b, s, w.Klasse)) throw new UnauthorizedAccessException("Schüler*innen dürfen nur Administration, Schulleitung und die Klassenleitung anlegen.");
        var id = Math.Max(ErsteEigeneId, Convert.ToInt32(_db.Wert("SELECT COALESCE(MAX(id), 0) FROM schueler_daten") ?? 0) + 1);
        while (s.Schueler.Any(x => x.Id == id)) id++;
        _db.Ausfuehren("""
            INSERT INTO schueler_daten (id, vorname, name, klasse, geschlecht, geburtsdatum, email, bemerkung, eigen, geloescht, geaendert, benutzer_id)
            VALUES (@id, @v, @n, @k, @g, @gd, @e, @b, 1, 0, @z, @u)
            """, Db.P("@id", id), Db.P("@v", w.Vorname), Db.P("@n", w.Name), Db.P("@k", w.Klasse), Db.P("@g", w.Geschlecht), Db.P("@gd", w.Geburtsdatum),
            Db.P("@e", w.Email), Db.P("@b", w.Bemerkung), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@u", b.Id));
        _pakete.SchuelerLaden();
        return Liste(b, _pakete.Aktuell!, w.Klasse).First(x => x.Id == id);
    }

    public Schuelerdaten Aendern(Benutzer b, Stand s, int id, SchuelerEingabe e)
    {
        Lesen(b);
        var bisher = s.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");
        if (!DarfBearbeiten(b, s, bisher.Klasse)) throw new UnauthorizedAccessException("Sie dürfen diese Schülerin / diesen Schüler nicht bearbeiten.");
        var w = Pruefen(s, e);
        if (!Ci.Equals(w.Klasse, bisher.Klasse) && !Verwalten(b)) throw new UnauthorizedAccessException("Den Klassenwechsel nimmt die Administration vor.");
        var eigen = id >= ErsteEigeneId;
        var p = new[] { Db.P("@id", id), Db.P("@v", w.Vorname), Db.P("@n", w.Name), Db.P("@k", w.Klasse), Db.P("@g", w.Geschlecht), Db.P("@gd", w.Geburtsdatum),
            Db.P("@e", w.Email), Db.P("@b", w.Bemerkung), Db.P("@ei", eigen), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@u", b.Id) };
        if (_db.Ausfuehren("""
                UPDATE schueler_daten SET vorname = @v, name = @n, klasse = @k, geschlecht = @g, geburtsdatum = @gd, email = @e, bemerkung = @b,
                    geloescht = 0, geaendert = @z, benutzer_id = @u WHERE id = @id
                """, p) == 0)
            _db.Ausfuehren("""
                INSERT INTO schueler_daten (id, vorname, name, klasse, geschlecht, geburtsdatum, email, bemerkung, eigen, geloescht, geaendert, benutzer_id)
                VALUES (@id, @v, @n, @k, @g, @gd, @e, @b, @ei, 0, @z, @u)
                """, p);
        _pakete.SchuelerLaden();
        return Liste(b, _pakete.Aktuell!, w.Klasse).First(x => x.Id == id);
    }

    public void Loeschen(Benutzer b, Stand s, int id)
    {
        Lesen(b);
        if (!Verwalten(b)) throw new UnauthorizedAccessException("Schüler*innen löschen darf nur die Administration bzw. Schulleitung.");
        var x = s.Schueler.FirstOrDefault(y => y.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");
        var p = new[] { Db.P("@id", id), Db.P("@v", x.Vorname), Db.P("@n", x.Name), Db.P("@k", x.Klasse), Db.P("@ei", id >= ErsteEigeneId),
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@u", b.Id) };
        if (_db.Ausfuehren("UPDATE schueler_daten SET geloescht = 1, geaendert = @z, benutzer_id = @u WHERE id = @id", p) == 0)
            _db.Ausfuehren("INSERT INTO schueler_daten (id, vorname, name, klasse, eigen, geloescht, geaendert, benutzer_id) VALUES (@id, @v, @n, @k, @ei, 1, @z, @u)", p);
        _pakete.SchuelerLaden();
    }
}
