using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Mitteilung(int Id, int AbsenderId, string Absender, string Betreff, string Text, string Erstellt, bool Wichtig, string? Gelesen, List<string> Empfaenger);

/// <summary>
/// Interne Mitteilungen (Posteingang, Gesendet). Empfänger können einzelne Benutzer oder Gruppen sein:
/// <list type="bullet">
/// <item>alle Lehrkräfte</item>
/// <item>Schulleitung</item>
/// <item>eine Klasse (Klassenkonto und Klassenleitung)</item>
/// <item>alle</item>
/// </list>
/// Gruppen werden beim Senden aufgelöst.
/// </summary>
public sealed class Mitteilungsdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Benutzerdienst _benutzer;

    public Mitteilungsdienst(Db db, Uhr uhr, Benutzerdienst benutzer) { _db = db; _uhr = uhr; _benutzer = benutzer; }

    /// <summary>Auswahlliste der möglichen Empfänger (Gruppen und Benutzer) für den Absender.</summary>
    public List<object> Empfaengerliste(Benutzer ich, Stand? s)
    {
        var res = new List<object>();
        if (ich.Kollegium)
        {
            res.Add(new { id = "gruppe:kollegium", name = "Alle Lehrkräfte", art = "gruppe" });
            res.Add(new { id = "gruppe:leitung", name = "Schulleitung und Administration", art = "gruppe" });
            if (ich.DarfNachrichten) res.Add(new { id = "gruppe:alle", name = "Alle Benutzer", art = "gruppe" });
            foreach (var k in s?.Paket.Elemente.Klassen ?? new()) res.Add(new { id = "klasse:" + k.Kurz, name = $"Klasse {k.Kurz} (Klassenkonto und Klassenleitung)", art = "klasse" });
            foreach (var b in _benutzer.Alle().Where(b => b.Id != ich.Id && !b.Gesperrt))
                res.Add(new { id = "benutzer:" + b.Id, name = b.Anzeigename.Length > 0 ? $"{b.Anzeigename} ({b.Login})" : b.Login, art = Rollen.Name(b.Rolle) });
        }
        else
        {
            // Schüler-/Klassenkonten schreiben nur an ihre Klassenleitung und die Schulleitung
            var leitung = s != null && ich.KlassenKuerzel is string k && s.Klassen.TryGetValue(k, out var kl) ? kl.Klassenleitung : new List<string>();
            foreach (var b in _benutzer.Alle().Where(b => !b.Gesperrt && (b.Rolle == Rollen.Leitung || (b.LehrerKuerzel is string lk && leitung.Contains(lk, StringComparer.OrdinalIgnoreCase)))))
                res.Add(new { id = "benutzer:" + b.Id, name = b.Anzeigename.Length > 0 ? $"{b.Anzeigename} ({b.Login})" : b.Login, art = Rollen.Name(b.Rolle) });
        }
        return res;
    }

    /// <summary>Empfänger-Kennungen in Benutzer-Ids auflösen (nur erlaubte Empfänger).</summary>
    public HashSet<int> Aufloesen(Benutzer ich, Stand? s, IEnumerable<string> kennungen)
    {
        var erlaubt = Empfaengerliste(ich, s).Select(x => (string)x.GetType().GetProperty("id")!.GetValue(x)!).ToHashSet();
        var alle = _benutzer.Alle().Where(b => !b.Gesperrt).ToList();
        var res = new HashSet<int>();
        foreach (var k in kennungen.Distinct())
        {
            if (!erlaubt.Contains(k)) throw new ArgumentException("Unzulässiger Empfänger.");
            if (k == "gruppe:kollegium") res.UnionWith(alle.Where(b => b.Kollegium).Select(b => b.Id));
            else if (k == "gruppe:leitung") res.UnionWith(alle.Where(b => b.Rolle is Rollen.Leitung or Rollen.Admin).Select(b => b.Id));
            else if (k == "gruppe:alle") res.UnionWith(alle.Select(b => b.Id));
            else if (k.StartsWith("klasse:"))
            {
                var kl = k[7..];
                var leitung = s != null && s.Klassen.TryGetValue(kl, out var x) ? x.Klassenleitung : new List<string>();
                res.UnionWith(alle.Where(b => (b.KlassenKuerzel is string bk && bk.Equals(kl, StringComparison.OrdinalIgnoreCase)) ||
                                              (b.LehrerKuerzel is string lk && leitung.Contains(lk, StringComparer.OrdinalIgnoreCase))).Select(b => b.Id));
            }
            else if (k.StartsWith("benutzer:") && int.TryParse(k[9..], out var id)) res.Add(id);
        }
        res.Remove(ich.Id);
        return res;
    }

    public int Senden(Benutzer ich, Stand? s, IEnumerable<string> empfaenger, string betreff, string text, bool wichtig)
    {
        if (string.IsNullOrWhiteSpace(betreff)) throw new ArgumentException("Bitte einen Betreff eingeben.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (betreff.Length > 200 || text.Length > 20000) throw new ArgumentException("Betreff oder Text ist zu lang.");
        var ids = Aufloesen(ich, s, empfaenger);
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens einen Empfänger wählen.");
        var neu = 0L;
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "INSERT INTO mitteilungen (absender_id, betreff, text, erstellt, wichtig) VALUES (@a, @b, @t, @e, @w)",
                Db.P("@a", ich.Id), Db.P("@b", betreff.Trim()), Db.P("@t", text.Trim()), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@w", wichtig && ich.Kollegium));
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            neu = Convert.ToInt64(cmd.ExecuteScalar());
            foreach (var id in ids) Db.Ausfuehren(c, "INSERT INTO mitteilung_empfaenger (mitteilung_id, benutzer_id) VALUES (@m, @b)", Db.P("@m", neu), Db.P("@b", id));
        });
        return (int)neu;
    }

    private List<string> EmpfaengerNamen(int id) => _db.Liste("""
        SELECT CASE WHEN b.anzeigename <> '' THEN b.anzeigename ELSE b.login END AS n FROM mitteilung_empfaenger e JOIN benutzer b ON b.id = e.benutzer_id
        WHERE e.mitteilung_id = @m ORDER BY n
        """, r => r.Text("n"), Db.P("@m", id));

    public List<Mitteilung> Posteingang(int ich) => _db.Liste("""
        SELECT m.*, e.gelesen, CASE WHEN b.anzeigename <> '' THEN b.anzeigename ELSE b.login END AS absender
        FROM mitteilung_empfaenger e JOIN mitteilungen m ON m.id = e.mitteilung_id JOIN benutzer b ON b.id = m.absender_id
        WHERE e.benutzer_id = @b AND e.geloescht = 0 ORDER BY m.id DESC LIMIT 300
        """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), r.Text("absender"), r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), r.TextOderNull("gelesen"), new()),
        Db.P("@b", ich));

    public List<Mitteilung> Gesendet(int ich) => _db.Liste("""
        SELECT m.*, 'ich' AS absender FROM mitteilungen m WHERE m.absender_id = @b AND m.geloescht_absender = 0 ORDER BY m.id DESC LIMIT 300
        """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), "", r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), "", new()),
        Db.P("@b", ich)).Select(m => m with { Empfaenger = EmpfaengerNamen(m.Id) }).ToList();

    public int Ungelesen(int ich) => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM mitteilung_empfaenger WHERE benutzer_id = @b AND gelesen IS NULL AND geloescht = 0", Db.P("@b", ich)));

    public void Gelesen(int ich, int id) =>
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET gelesen = @z WHERE benutzer_id = @b AND mitteilung_id = @m AND gelesen IS NULL",
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", ich), Db.P("@m", id));

    public void Loeschen(int ich, int id)
    {
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET geloescht = 1 WHERE benutzer_id = @b AND mitteilung_id = @m", Db.P("@b", ich), Db.P("@m", id));
        _db.Ausfuehren("UPDATE mitteilungen SET geloescht_absender = 1 WHERE absender_id = @b AND id = @m", Db.P("@b", ich), Db.P("@m", id));
    }
}
