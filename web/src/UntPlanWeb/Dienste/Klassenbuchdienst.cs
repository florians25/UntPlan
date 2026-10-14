using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Klassenbucheintrag(int Id, string Datum, int Stunde, int Unterricht, List<string> Klassen, string Fach, string Lehrer,
    string Thema, string Hausaufgabe, string? Faellig, string Notiz, string Geaendert);

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
        r.TextOderNull("faellig"), r.Text("notiz"), r.Text("geaendert"));

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

    public Klassenbucheintrag Speichern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string thema, string hausaufgabe, DateOnly? faellig, string notiz)
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
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, thema, hausaufgabe, faellig, notiz, geaendert, benutzer_id)
            VALUES (@d, @s, @u, @k, @f, @l, @t, @h, @fa, @n, @g, @b)
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET thema = excluded.thema, hausaufgabe = excluded.hausaufgabe, faellig = excluded.faellig,
                notiz = excluded.notiz, klassen = excluded.klassen, fach = excluded.fach, geaendert = excluded.geaendert, benutzer_id = excluded.benutzer_id
            """, Db.P("@d", d), Db.P("@s", karte.Von), Db.P("@u", unterricht), Db.P("@k", klassen), Db.P("@f", karte.Fach), Db.P("@l", lehrer),
            Db.P("@t", (thema ?? "").Trim()), Db.P("@h", (hausaufgabe ?? "").Trim()), Db.P("@fa", faellig), Db.P("@n", (notiz ?? "").Trim()),
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
}
