using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Klassenbucheintrag(int Id, string Datum, int Stunde, int Unterricht, List<string> Klassen, string Fach, string Lehrer,
    string Thema, string Hausaufgabe, string? Faellig, string Notiz, string Geaendert, string? Anwesenheit = null, string NotizSchueler = "");

/// <summary>Fehlzeit einer Schülerin/eines Schülers in einer Stunde. Art „fehlt“ oder „verspaetet“, Status offen/entschuldigt/unentschuldigt.</summary>
public sealed record Fehlzeit(int Id, string Datum, int Stunde, int Unterricht, int SchuelerId, string Klasse, string Fach, string Art, int Minuten,
    string Status, string Bemerkung, string Lehrer, string Erstellt);

/// <summary>Vermerk im Klassenbuch (zur Klasse oder zu einzelnen Schülern), z. B. Ordnungsmaßnahme, Elterngespräch, Lob.</summary>
public sealed record Vermerk(int Id, string Datum, string Klasse, int? SchuelerId, string Art, string Text, string Lehrer, string Erstellt);

/// <summary>Anwesenheitseintrag beim Speichern (nur Abweichungen: fehlt oder verspätet).</summary>
public sealed record AnwesenheitEintrag(int SchuelerId, string Art, int Minuten, string? Bemerkung);

/// <summary>
/// Klassenbuch: je gehaltener Stunde Unterrichtsinhalt, Hausaufgabe (mit Fälligkeit) und interne Notiz.
/// <list type="bullet">
/// <item>Eintragen darf die unterrichtende Lehrkraft; Schulleitung und Administration dürfen alles.</item>
/// <item>Klassenkonten sehen Inhalte und Hausaufgaben ihrer Klasse, aber keine Notizen.</item>
/// </list>
/// </summary>
public sealed class Klassenbuchdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Plandienst _plan;

    public Klassenbuchdienst(Db db, Uhr uhr, Plandienst plan) { _db = db; _uhr = uhr; _plan = plan; }

    private static Klassenbucheintrag Lesen(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.Zahl("id"), r.Text("datum"), r.Zahl("stunde"), r.Zahl("unterricht"),
        JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new(), r.Text("fach"), r.Text("lehrer"), r.Text("thema"), r.Text("hausaufgabe"),
        r.TextOderNull("faellig"), r.Text("notiz"), r.Text("geaendert"), r.TextOderNull("anwesenheit"), r.Text("notiz_schueler"));

    private static Fehlzeit LesenFehlzeit(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.Zahl("id"), r.Text("datum"), r.Zahl("stunde"), r.Zahl("unterricht"),
        r.Zahl("schueler_id"), r.Text("klasse"), r.Text("fach"), r.Text("art"), r.Zahl("minuten"), r.Text("status"), r.Text("bemerkung"), r.Text("lehrer"), r.Text("erstellt"));

    private static Vermerk LesenVermerk(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.Zahl("id"), r.Text("datum"), r.Text("klasse"), r.ZahlOderNull("schueler_id"),
        r.Text("art"), r.Text("text"), r.Text("lehrer"), r.Text("erstellt"));

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    public static readonly string[] VermerkArten = { "Vermerk", "Ordnungsmaßnahme", "Elterngespräch", "Lob", "Material vergessen", "Hausaufgabe fehlt" };

    public List<Klassenbucheintrag> Zeitraum(DateOnly von, DateOnly bis) =>
        _db.Liste("SELECT * FROM klassenbuch WHERE datum >= @v AND datum <= @b ORDER BY datum, stunde", Lesen, Db.P("@v", von), Db.P("@b", bis));

    /// <summary>Einträge einer Klasse (neueste zuerst).</summary>
    public List<Klassenbucheintrag> Klasse(string klasse, DateOnly von, DateOnly bis) =>
        Zeitraum(von, bis).Where(e => e.Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase)).OrderByDescending(e => e.Datum).ThenByDescending(e => e.Stunde).ToList();

    /// <summary>Offene Hausaufgaben einer Klasse (fällig ab heute bzw. in den letzten 14 Tagen erteilt).</summary>
    public List<Klassenbucheintrag> Hausaufgaben(string klasse)
    {
        var heute = _uhr.Heute;
        return Zeitraum(heute.AddDays(-30), heute.AddDays(60))
            .Where(e => e.Hausaufgabe.Length > 0 && e.Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase) &&
                        (e.Faellig is string f ? DateOnly.Parse(f) >= heute : DateOnly.Parse(e.Datum) >= heute.AddDays(-14)))
            .OrderBy(e => e.Faellig ?? e.Datum).ToList();
    }

    public Klassenbucheintrag? Holen(DateOnly d, int stunde, int unterricht, string lehrer) =>
        _db.Einzel("SELECT * FROM klassenbuch WHERE datum = @d AND stunde = @s AND unterricht = @u AND lehrer = @l", Lesen,
            Db.P("@d", d), Db.P("@s", stunde), Db.P("@u", unterricht), Db.P("@l", lehrer));

    /// <summary>Darf der Benutzer zu dieser Stunde schreiben? Lehrkraft muss die Stunde laut Plan halten.</summary>
    public bool DarfSchreiben(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht)
    {
        if (b.Rolle is Rollen.Admin or Rollen.Leitung) return true;
        if (b.LehrerKuerzel is not string l) return false;
        return _plan.Karten(s, d, Elementtyp.Lehrer, l).Any(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis && k.Status is not ("entfall" or "abwesend"));
    }

    public Klassenbucheintrag Speichern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string thema, string hausaufgabe, DateOnly? faellig, string notiz, string? notizSchueler = null)
    {
        if (!DarfSchreiben(b, s, d, stunde, unterricht)) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (d > _uhr.Heute.AddDays(14)) throw new ArgumentException("Einträge sind höchstens zwei Wochen im Voraus möglich.");
        if (faellig is DateOnly f && f < d) throw new ArgumentException("Die Hausaufgabe kann nicht vor der Stunde fällig sein.");
        var karte = _plan.Karten(s, d, Elementtyp.Lehrer, b.LehrerKuerzel ?? "").FirstOrDefault(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis)
                    ?? s.Paket.Elemente.Klassen.SelectMany(k => _plan.Karten(s, d, Elementtyp.Klasse, k.Kurz)).FirstOrDefault(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis);
        if (karte is null) throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var lehrer = b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "";
        var klassen = JsonSerializer.Serialize(karte.Klassen.Select(k => k.K).ToList());
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, thema, hausaufgabe, faellig, notiz, geaendert, benutzer_id, notiz_schueler)
            VALUES (@d, @s, @u, @k, @f, @l, @t, @h, @fa, @n, @g, @b, COALESCE(@ns, ''))
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET thema = excluded.thema, hausaufgabe = excluded.hausaufgabe, faellig = excluded.faellig,
                notiz = excluded.notiz, notiz_schueler = CASE WHEN @ns IS NULL THEN klassenbuch.notiz_schueler ELSE excluded.notiz_schueler END, klassen = excluded.klassen, fach = excluded.fach, geaendert = excluded.geaendert, benutzer_id = excluded.benutzer_id
            """, Db.P("@d", d), Db.P("@s", karte.Von), Db.P("@u", unterricht), Db.P("@k", klassen), Db.P("@f", karte.Fach), Db.P("@l", lehrer),
            Db.P("@t", (thema ?? "").Trim()), Db.P("@h", (hausaufgabe ?? "").Trim()), Db.P("@fa", faellig), Db.P("@n", (notiz ?? "").Trim()), Db.P("@ns", notizSchueler?.Trim()),
            Db.P("@g", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
        return Holen(d, karte.Von, unterricht, lehrer)!;
    }

    /// <summary>Gehaltene Stunden einer Lehrkraft ohne Unterrichtsinhalt (letzte 14 Tage bis heute).</summary>
    public List<object> OffeneStunden(Stand s, string lehrer)
    {
        var heute = _uhr.Heute;
        var eintraege = Zeitraum(heute.AddDays(-14), heute).Where(e => e.Lehrer.Equals(lehrer, StringComparison.OrdinalIgnoreCase) && e.Thema.Length > 0)
            .Select(e => (e.Datum, e.Stunde, e.Unterricht)).ToHashSet();
        var res = new List<object>();
        for (var d = heute.AddDays(-14); d <= heute; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            foreach (var k in _plan.Karten(s, d, Elementtyp.Lehrer, lehrer).Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert"))
            {
                if (d == heute && string.CompareOrdinal(k.Ende, _uhr.Jetzt.ToString("HH:mm")) > 0) continue;
                if (!eintraege.Contains((k.Datum, k.Von, k.Unterricht!.Value))) res.Add(new { k.Datum, k.Von, k.Bis, k.Fach, klassen = k.Klassen.Select(x => x.K), unterricht = k.Unterricht });
            }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------ Anwesenheit & Fehlzeiten

    /// <summary>Schülerliste der Klassen (alphabetisch).</summary>
    public static List<WebSchueler> Schueler(Stand s, IEnumerable<string> klassen)
    {
        var k = klassen.ToHashSet(Ci);
        var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
        return s.Paket.Elemente.Schueler.Where(x => k.Contains(x.Klasse)).OrderBy(x => x.Klasse, de).ThenBy(x => x.Name, de).ThenBy(x => x.Vorname, de).ToList();
    }

    /// <summary>Klassenleitung, Schulleitung oder Administration?</summary>
    public static bool IstKlassenleitung(Benutzer b, Stand s, string klasse) =>
        b.Rolle is Rollen.Admin or Rollen.Leitung ||
        b.LehrerKuerzel is string l && s.Paket.Elemente.Klassen.Any(k => Ci.Equals(k.Kurz, klasse) && k.Klassenleitung.Contains(l, Ci));

    private Karte? KarteZu(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht) =>
        _plan.Karten(s, d, Elementtyp.Lehrer, b.LehrerKuerzel ?? "").FirstOrDefault(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis)
        ?? s.Paket.Elemente.Klassen.SelectMany(k => _plan.Karten(s, d, Elementtyp.Klasse, k.Kurz)).FirstOrDefault(k => k.Unterricht == unterricht && stunde >= k.Von && stunde <= k.Bis);

    public List<Fehlzeit> FehlzeitenZeitraum(DateOnly von, DateOnly bis, string? klasse = null) =>
        _db.Liste("SELECT * FROM fehlzeiten WHERE datum >= @v AND datum <= @b AND (@k IS NULL OR klasse = @k COLLATE NOCASE) ORDER BY datum, stunde", LesenFehlzeit,
            Db.P("@v", von), Db.P("@b", bis), Db.P("@k", klasse));

    /// <summary>Anwesenheitsliste einer Stunde: Schüler der Klassen mit erfassten Fehlzeiten.</summary>
    public object Anwesenheit(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht)
    {
        var karte = KarteZu(b, s, d, stunde, unterricht) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        var fz = FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis && klassen.Contains(f.Klasse, Ci))
            .GroupBy(f => f.SchuelerId).Select(g => g.OrderBy(f => f.Stunde).First()).ToList();
        var eintrag = Zeitraum(d, d).FirstOrDefault(e => e.Unterricht == unterricht && e.Stunde == karte.Von);
        return new
        {
            schueler = Schueler(s, klassen), fehlzeiten = fz, geprueft = eintrag?.Anwesenheit,
            darf = DarfSchreiben(b, s, d, stunde, unterricht) || klassen.Any(k => IstKlassenleitung(b, s, k)),
        };
    }

    /// <summary>Anwesenheit speichern: ersetzt die Fehlzeiten der Stunde (Entschuldigungsstatus bleibt erhalten) und markiert die Stunde als geprüft.</summary>
    public int AnwesenheitSpeichern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, List<AnwesenheitEintrag> eintraege)
    {
        var karte = KarteZu(b, s, d, stunde, unterricht) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        if (!DarfSchreiben(b, s, d, stunde, unterricht) && !klassen.Any(k => IstKlassenleitung(b, s, k)))
            throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (d > _uhr.Heute) throw new ArgumentException("Fehlzeiten können nicht im Voraus erfasst werden.");
        var schueler = Schueler(s, klassen).ToDictionary(x => x.Id);
        var alt = FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis && schueler.ContainsKey(f.SchuelerId)).ToList();
        var lehrer = b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "";
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        foreach (var f in alt) _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", f.Id));
        var anzahl = 0;
        foreach (var e in eintraege.Where(x => schueler.ContainsKey(x.SchuelerId)).DistinctBy(x => x.SchuelerId))
        {
            var art = e.Art == "verspaetet" ? "verspaetet" : "fehlt";
            var status = alt.FirstOrDefault(f => f.SchuelerId == e.SchuelerId && f.Art == art)?.Status ?? "offen";
            // Fehlen gilt für alle Stunden des Blocks, Verspätung nur für die erste
            for (var n = karte.Von; n <= (art == "fehlt" ? karte.Bis : karte.Von); n++)
            {
                _db.Ausfuehren("""
                    INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id)
                    VALUES (@d, @s, @u, @sid, @k, @f, @a, @m, @st, @bem, @l, @e, @b)
                    ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET unterricht = excluded.unterricht, fach = excluded.fach, art = excluded.art,
                        minuten = excluded.minuten, bemerkung = excluded.bemerkung, lehrer = excluded.lehrer
                    """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", unterricht), Db.P("@sid", e.SchuelerId), Db.P("@k", schueler[e.SchuelerId].Klasse), Db.P("@f", karte.Fach),
                    Db.P("@a", art), Db.P("@m", art == "verspaetet" ? Math.Clamp(e.Minuten, 1, 90) : 0), Db.P("@st", status), Db.P("@bem", (e.Bemerkung ?? "").Trim()),
                    Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id));
            }
            anzahl++;
        }
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, geaendert, benutzer_id, anwesenheit)
            VALUES (@d, @s, @u, @k, @f, @l, @g, @b, @g)
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET anwesenheit = excluded.anwesenheit
            """, Db.P("@d", d), Db.P("@s", karte.Von), Db.P("@u", unterricht), Db.P("@k", JsonSerializer.Serialize(klassen)), Db.P("@f", karte.Fach),
            Db.P("@l", lehrer), Db.P("@g", jetzt), Db.P("@b", b.Id));
        return anzahl;
    }

    /// <summary>Ganztägiges Fehlen (z. B. Krankmeldung über die Klassenleitung) für alle Unterrichtsstunden der Tage eintragen.</summary>
    public int FehlenEintragen(Benutzer b, Stand s, int schuelerId, DateOnly von, DateOnly bis, string status, string bemerkung)
    {
        var sch = s.Paket.Elemente.Schueler.FirstOrDefault(x => x.Id == schuelerId) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");
        if (!IstKlassenleitung(b, s, sch.Klasse)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann ganze Tage eintragen.");
        if (bis < von || bis.DayNumber - von.DayNumber > 60) throw new ArgumentException("Bitte einen Zeitraum von höchstens 60 Tagen wählen.");
        var anzahl = 0;
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            foreach (var k in _plan.Karten(s, d, Elementtyp.Klasse, sch.Klasse).Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung")))
                for (var n = k.Von; n <= k.Bis; n++)
                {
                    _db.Ausfuehren("""
                        INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id)
                        VALUES (@d, @s, @u, @sid, @k, @f, 'fehlt', 0, @st, @bem, @l, @e, @b)
                        ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET art = 'fehlt', minuten = 0, status = excluded.status, bemerkung = excluded.bemerkung
                        """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", k.Unterricht), Db.P("@sid", schuelerId), Db.P("@k", sch.Klasse), Db.P("@f", k.Fach),
                        Db.P("@st", Status(status)), Db.P("@bem", (bemerkung ?? "").Trim()), Db.P("@l", b.LehrerKuerzel ?? ""), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
                    anzahl++;
                }
        }
        return anzahl;
    }

    private static string Status(string? st) => st is "entschuldigt" or "unentschuldigt" ? st : "offen";

    /// <summary>Entschuldigungsstatus setzen (Klassenleitung, Schulleitung, Administration).</summary>
    public int StatusSetzen(Benutzer b, Stand s, IEnumerable<int> ids, string status)
    {
        var n = 0;
        foreach (var id in ids.Distinct())
        {
            var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", LesenFehlzeit, Db.P("@i", id));
            if (f is null) continue;
            if (!IstKlassenleitung(b, s, f.Klasse)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann entschuldigen.");
            n += _db.Ausfuehren("UPDATE fehlzeiten SET status = @s WHERE id = @i", Db.P("@s", Status(status)), Db.P("@i", id));
        }
        return n;
    }

    public bool FehlzeitLoeschen(Benutzer b, Stand s, int id)
    {
        var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", LesenFehlzeit, Db.P("@i", id));
        if (f is null) return false;
        if (!IstKlassenleitung(b, s, f.Klasse) && !Ci.Equals(f.Lehrer, b.LehrerKuerzel ?? "")) throw new UnauthorizedAccessException("Keine Berechtigung.");
        return _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", id)) > 0;
    }

    /// <summary>Fehlzeitenübersicht einer Klasse: Summen je Schüler und Einzeleinträge.</summary>
    public object Fehlzeitenuebersicht(Stand s, Benutzer b, string klasse, DateOnly von, DateOnly bis)
    {
        var liste = FehlzeitenZeitraum(von, bis, klasse);
        var schueler = Schueler(s, new[] { klasse });
        var summen = schueler.Select(x =>
        {
            var f = liste.Where(y => y.SchuelerId == x.Id).ToList();
            var fehlt = f.Where(y => y.Art == "fehlt").ToList();
            return new
            {
                x.Id, x.Vorname, x.Name,
                stunden = fehlt.Count, tage = fehlt.Select(y => y.Datum).Distinct().Count(),
                entschuldigt = fehlt.Count(y => y.Status == "entschuldigt"), unentschuldigt = fehlt.Count(y => y.Status == "unentschuldigt"),
                offen = fehlt.Count(y => y.Status == "offen"),
                verspaetungen = f.Count(y => y.Art == "verspaetet"), minuten = f.Where(y => y.Art == "verspaetet").Sum(y => y.Minuten),
            };
        }).ToList();
        return new { schueler = summen, eintraege = liste.OrderByDescending(f => f.Datum).ThenBy(f => f.Stunde).ToList(), verwalten = IstKlassenleitung(b, s, klasse) };
    }

    // ------------------------------------------------------------------------------------------ Vermerke

    public List<Vermerk> Vermerke(string klasse, DateOnly von, DateOnly bis) =>
        _db.Liste("SELECT * FROM vermerke WHERE klasse = @k COLLATE NOCASE AND datum >= @v AND datum <= @b ORDER BY datum DESC, id DESC", LesenVermerk,
            Db.P("@k", klasse), Db.P("@v", von), Db.P("@b", bis));

    public Vermerk VermerkAnlegen(Benutzer b, Stand s, DateOnly d, string klasse, int? schuelerId, string art, string text)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur das Kollegium kann Vermerke eintragen.");
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (schuelerId is int sid && !s.Paket.Elemente.Schueler.Any(x => x.Id == sid && Ci.Equals(x.Klasse, klasse))) throw new ArgumentException("Schüler gehört nicht zur Klasse.");
        var a = VermerkArten.FirstOrDefault(x => Ci.Equals(x, art)) ?? "Vermerk";
        var id = _db.Einfuegen("INSERT INTO vermerke (datum, klasse, schueler_id, art, text, lehrer, erstellt, benutzer_id) VALUES (@d, @k, @s, @a, @t, @l, @e, @b)", Db.P("@d", d), Db.P("@k", s.Klassen[klasse].Kurz), Db.P("@s", schuelerId), Db.P("@a", a), Db.P("@t", text.Trim()),
            Db.P("@l", b.LehrerKuerzel ?? b.Anzeigename), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
        return _db.Einzel("SELECT * FROM vermerke WHERE id = @i", LesenVermerk, Db.P("@i", id))!;
    }

    public bool VermerkLoeschen(Benutzer b, Stand s, int id)
    {
        var v = _db.Einzel("SELECT * FROM vermerke WHERE id = @i", LesenVermerk, Db.P("@i", id));
        if (v is null) return false;
        var eigen = Convert.ToInt64(_db.Wert("SELECT COALESCE(benutzer_id, 0) FROM vermerke WHERE id = @i", Db.P("@i", id)) ?? 0L) == b.Id;
        if (!eigen && !IstKlassenleitung(b, s, v.Klasse)) throw new UnauthorizedAccessException("Keine Berechtigung.");
        return _db.Ausfuehren("DELETE FROM vermerke WHERE id = @i", Db.P("@i", id)) > 0;
    }

    // ------------------------------------------------------------------------------------------ Wochenansicht

    /// <summary>Klassenbuch einer Woche: je Tag die Stunden der Klasse mit Inhalt, Hausaufgabe, Anwesenheitsprüfung und Fehlzeiten.</summary>
    public object Woche(Stand s, string klasse, DateOnly montag)
    {
        var bis = montag.AddDays(Math.Max(5, s.Paket.Zeitraster.TageProWoche) - 1);
        var eintraege = Zeitraum(montag, bis).Where(e => e.Klassen.Contains(klasse, Ci)).ToList();
        var fz = FehlzeitenZeitraum(montag, bis, klasse);
        var vermerke = Vermerke(klasse, montag, bis);
        var namen = s.Paket.Elemente.Schueler.ToDictionary(x => x.Id, x => $"{x.Name}, {x.Vorname}");
        var tage = new List<object>();
        for (var d = montag; d <= bis; d = d.AddDays(1))
        {
            var iso = Plandienst.Iso(d);
            var frei = _plan.Frei(s, d);
            var stunden = frei.Frei ? new List<object>() : _plan.Karten(s, d, Elementtyp.Klasse, klasse).Where(k => k.Unterricht.HasValue || k.Status == "entfall")
                .Select(k =>
                {
                    var e = eintraege.FirstOrDefault(x => x.Datum == iso && x.Unterricht == k.Unterricht && x.Stunde == k.Von);
                    var f = fz.Where(x => x.Datum == iso && x.Stunde >= k.Von && x.Stunde <= k.Bis).ToList();
                    return (object)new
                    {
                        k.Von, k.Bis, k.Beginn, k.Ende, k.Fach, k.FachName, k.Farbe, k.Status, k.Art, k.Unterricht,
                        lehrer = k.Lehrer.Select(x => x.K), raeume = k.Raeume.Select(x => x.K),
                        thema = e?.Thema ?? "", hausaufgabe = e?.Hausaufgabe ?? "", faellig = e?.Faellig, geprueft = e?.Anwesenheit,
                        fehlend = f.Where(x => x.Art == "fehlt").Select(x => x.SchuelerId).Distinct().Select(i => namen.GetValueOrDefault(i, "?")).ToList(),
                        verspaetet = f.Where(x => x.Art == "verspaetet").Select(x => $"{namen.GetValueOrDefault(x.SchuelerId, "?")} ({x.Minuten} min)").ToList(),
                    };
                }).ToList();
            tage.Add(new { datum = iso, frei = frei.Frei, freiGrund = frei.Grund, stunden, vermerke = vermerke.Where(v => v.Datum == iso).Select(v => new { v.Id, v.Art, v.Text, v.Lehrer, schueler = v.SchuelerId is int i ? namen.GetValueOrDefault(i, "") : "" }) });
        }
        return new { klasse, von = Plandienst.Iso(montag), bis = Plandienst.Iso(bis), tage, schuelerAnzahl = s.Paket.Elemente.Schueler.Count(x => Ci.Equals(x.Klasse, klasse)) };
    }
}
