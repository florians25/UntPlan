using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Mitteilung(int Id, int AbsenderId, string Absender, string Betreff, string Text, string Erstellt, bool Wichtig, string? Gelesen, List<string> Empfaenger,
    List<Anhang>? Anhaenge = null, List<string>? EntwurfEmpfaenger = null);

/// <summary>Anhang einer Mitteilung (ohne Inhalt).</summary>
public sealed record Anhang(int Id, string Name, string Typ, int Groesse);

/// <summary>Eigene Verteilerliste (Mitglieder sind Empfängerkennungen wie benutzer:5, klasse:7a, gruppe:kollegium).</summary>
public sealed record Mitteilungsliste(int Id, string Name, List<string> Mitglieder, List<string> Namen, string Erstellt);

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
    public List<object> Empfaengerliste(Benutzer ich, Stand? s, bool mitListen = true)
    {
        var res = new List<object>();
        if (mitListen && ich.Kollegium)
            foreach (var l in Listen(ich, s, false)) res.Add(new { id = "liste:" + l.Id, name = $"Liste: {l.Name}", art = "liste" });
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
        // eigene Listen in ihre Mitglieder auflösen (nicht verschachtelt)
        var liste = new List<string>();
        foreach (var k in kennungen.Distinct())
        {
            if (!erlaubt.Contains(k)) throw new ArgumentException("Unzulässiger Empfänger.");
            if (k.StartsWith("liste:") && int.TryParse(k[6..], out var lid) && Listen(ich, s, false).FirstOrDefault(x => x.Id == lid) is { } l)
                liste.AddRange(l.Mitglieder.Where(m => erlaubt.Contains(m) && !m.StartsWith("liste:")));
            else liste.Add(k);
        }
        foreach (var k in liste.Distinct())
        {
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
            neu = Db.LetzteId(c);
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
        Db.P("@b", ich)).Select(m => m with { Empfaenger = EmpfaengerNamen(m.Id), Anhaenge = Anhaenge(m.Id) }).ToList();

    public List<Mitteilung> Gesendet(int ich) => _db.Liste("""
        SELECT m.*, 'ich' AS absender FROM mitteilungen m WHERE m.absender_id = @b AND m.geloescht_absender = 0 AND m.entwurf = 0 ORDER BY m.id DESC LIMIT 300
        """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), "", r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), "", new()),
        Db.P("@b", ich)).Select(m => m with { Empfaenger = EmpfaengerNamen(m.Id), Anhaenge = Anhaenge(m.Id) }).ToList();

    // ------------------------------------------------------------------------------------------ Entwürfe

    public List<Mitteilung> Entwuerfe(int ich) => _db.Liste("""
        SELECT m.* FROM mitteilungen m WHERE m.absender_id = @b AND m.geloescht_absender = 0 AND m.entwurf = 1 ORDER BY m.id DESC LIMIT 300
        """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), "", r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), "", new(),
            null, System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.Text("entwurf_empfaenger")) ?? new()),
        Db.P("@b", ich)).Select(m => m with { Anhaenge = Anhaenge(m.Id) }).ToList();

    /// <summary>Entwurf anlegen bzw. ändern. Empfänger werden nur geprüft (aufgelöst wird erst beim Senden).</summary>
    public int EntwurfSpeichern(Benutzer ich, Stand? s, int? id, IEnumerable<string> empfaenger, string betreff, string text, bool wichtig)
    {
        if ((betreff ?? "").Length > 200 || (text ?? "").Length > 20000) throw new ArgumentException("Betreff oder Text ist zu lang.");
        var erlaubt = Empfaengerliste(ich, s).Select(x => (string)x.GetType().GetProperty("id")!.GetValue(x)!).ToHashSet();
        var liste = empfaenger.Distinct().ToList();
        if (liste.Any(e => !erlaubt.Contains(e))) throw new ArgumentException("Unzulässiger Empfänger.");
        var p = new[] { Db.P("@a", ich.Id), Db.P("@b", (betreff ?? "").Trim()), Db.P("@t", (text ?? "").Trim()), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")),
            Db.P("@w", wichtig && ich.Kollegium), Db.P("@em", System.Text.Json.JsonSerializer.Serialize(liste)), Db.P("@id", id) };
        if (id is null) return (int)_db.Einfuegen("INSERT INTO mitteilungen (absender_id, betreff, text, erstellt, wichtig, entwurf, entwurf_empfaenger) VALUES (@a, @b, @t, @e, @w, 1, @em)", p);
        if (_db.Ausfuehren("UPDATE mitteilungen SET betreff = @b, text = @t, erstellt = @e, wichtig = @w, entwurf_empfaenger = @em WHERE id = @id AND absender_id = @a AND entwurf = 1", p) == 0)
            throw new ArgumentException("Unbekannter Entwurf.");
        return id.Value;
    }

    /// <summary>Entwurf senden: Empfänger auflösen und zustellen.</summary>
    public int EntwurfSenden(Benutzer ich, Stand? s, int id)
    {
        var e = Entwuerfe(ich.Id).FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException("Unbekannter Entwurf.");
        if (string.IsNullOrWhiteSpace(e.Betreff)) throw new ArgumentException("Bitte einen Betreff eingeben.");
        if (string.IsNullOrWhiteSpace(e.Text)) throw new ArgumentException("Bitte einen Text eingeben.");
        var ids = Aufloesen(ich, s, e.EntwurfEmpfaenger ?? new());
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens einen Empfänger wählen.");
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "UPDATE mitteilungen SET entwurf = 0, erstellt = @e WHERE id = @m", Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@m", id));
            foreach (var b in ids) Db.Ausfuehren(c, "INSERT INTO mitteilung_empfaenger (mitteilung_id, benutzer_id) VALUES (@m, @b)", Db.P("@m", id), Db.P("@b", b));
        });
        return id;
    }

    // ------------------------------------------------------------------------------------------ Listen

    public List<Mitteilungsliste> Listen(Benutzer ich, Stand? s, bool mitNamen = true)
    {
        var namen = mitNamen ? Empfaengerliste(ich, s, false).ToDictionary(x => (string)x.GetType().GetProperty("id")!.GetValue(x)!, x => (string)x.GetType().GetProperty("name")!.GetValue(x)!) : new();
        return _db.Liste("SELECT * FROM mitteilung_listen WHERE besitzer_id = @b ORDER BY name", r =>
        {
            var m = System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.Text("mitglieder")) ?? new();
            return new Mitteilungsliste(r.Zahl("id"), r.Text("name"), m, m.Select(x => namen.GetValueOrDefault(x, x)).ToList(), r.Text("erstellt"));
        }, Db.P("@b", ich.Id));
    }

    public Mitteilungsliste ListeSpeichern(Benutzer ich, Stand? s, int? id, string name, IEnumerable<string> mitglieder)
    {
        if (!ich.Kollegium) throw new UnauthorizedAccessException("Listen stehen nur dem Kollegium zur Verfügung.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte einen Namen eingeben.");
        var erlaubt = Empfaengerliste(ich, s, false).Select(x => (string)x.GetType().GetProperty("id")!.GetValue(x)!).ToHashSet();
        var m = mitglieder.Distinct().Where(erlaubt.Contains).ToList();
        if (m.Count == 0) throw new ArgumentException("Bitte mindestens ein Mitglied wählen.");
        var p = new[] { Db.P("@b", ich.Id), Db.P("@n", name.Trim()), Db.P("@m", System.Text.Json.JsonSerializer.Serialize(m)), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO mitteilung_listen (besitzer_id, name, mitglieder, erstellt) VALUES (@b, @n, @m, @e)", p);
        else if (_db.Ausfuehren("UPDATE mitteilung_listen SET name = @n, mitglieder = @m WHERE id = @id AND besitzer_id = @b", p) == 0) throw new ArgumentException("Unbekannte Liste.");
        return Listen(ich, s).First(l => l.Id == id);
    }

    public void ListeLoeschen(Benutzer ich, int id) => _db.Ausfuehren("DELETE FROM mitteilung_listen WHERE id = @id AND besitzer_id = @b", Db.P("@id", id), Db.P("@b", ich.Id));

    // ------------------------------------------------------------------------------------------ Anhänge

    public const int MaxAnhang = 10 * 1024 * 1024;

    public List<Anhang> Anhaenge(int mitteilung) => _db.Liste("SELECT id, name, typ, groesse FROM mitteilung_anhaenge WHERE mitteilung_id = @m ORDER BY id",
        r => new Anhang(r.Zahl("id"), r.Text("name"), r.Text("typ"), r.Zahl("groesse")), Db.P("@m", mitteilung));

    /// <summary>Anhang an eine eigene Mitteilung (Entwurf) hängen.</summary>
    public Anhang AnhangHinzufuegen(Benutzer ich, int mitteilung, string name, string typ, byte[] daten)
    {
        var eigen = Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM mitteilungen WHERE id = @m AND absender_id = @a AND entwurf = 1", Db.P("@m", mitteilung), Db.P("@a", ich.Id)));
        if (eigen == 0) throw new ArgumentException("Anhänge können nur an eigene Entwürfe gehängt werden.");
        if (daten.Length == 0 || daten.Length > MaxAnhang) throw new ArgumentException("Ein Anhang darf höchstens 10 MB groß sein.");
        if (Anhaenge(mitteilung).Count >= 5) throw new ArgumentException("Höchstens 5 Anhänge je Mitteilung.");
        name = Path.GetFileName(name ?? "Anhang").Trim();
        if (name.Length == 0) name = "Anhang";
        if (name.Length > 120) name = name[..120];
        var id = _db.Einfuegen("INSERT INTO mitteilung_anhaenge (mitteilung_id, name, typ, groesse, daten) VALUES (@m, @n, @t, @g, @d)",
            Db.P("@m", mitteilung), Db.P("@n", name), Db.P("@t", string.IsNullOrWhiteSpace(typ) ? "application/octet-stream" : typ), Db.P("@g", daten.Length), Db.P("@d", daten));
        return Anhaenge(mitteilung).First(a => a.Id == id);
    }

    public void AnhangLoeschen(Benutzer ich, int id) => _db.Ausfuehren("""
        DELETE FROM mitteilung_anhaenge WHERE id = @i AND mitteilung_id IN (SELECT id FROM mitteilungen WHERE absender_id = @a AND entwurf = 1)
        """, Db.P("@i", id), Db.P("@a", ich.Id));

    /// <summary>Anhang laden (nur für Absender und Empfänger).</summary>
    public (string Name, string Typ, byte[] Daten)? AnhangLaden(Benutzer ich, int id) => _db.Einzel("""
        SELECT a.name, a.typ, a.daten FROM mitteilung_anhaenge a JOIN mitteilungen m ON m.id = a.mitteilung_id
        WHERE a.id = @i AND (m.absender_id = @b OR EXISTS (SELECT 1 FROM mitteilung_empfaenger e WHERE e.mitteilung_id = m.id AND e.benutzer_id = @b AND e.geloescht = 0))
        """, r => new Tuple<string, string, byte[]>(r.Text("name"), r.Text("typ"), (byte[])r["daten"]), Db.P("@i", id), Db.P("@b", ich.Id)) is { } t ? (t.Item1, t.Item2, t.Item3) : null;

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
