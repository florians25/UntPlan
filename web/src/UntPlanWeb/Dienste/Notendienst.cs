using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Note eines Notenschemas, optional mit Wert (ohne Werte keine Durchschnitte).</summary>
public sealed record Note(string Text, double? Wert);

/// <summary>Notenschema (Stammdaten | Notenschemen), z. B. „1 – 5“, „+, ~, -“ oder „Bestanden / Nicht bestanden“.</summary>
public sealed record Notenschema(int Id, string Name, bool Aktiv, int Reihenfolge, List<Note> Noten)
{
    /// <summary>Durchschnittsnoten nur, wenn alle Noten einen Wert haben.</summary>
    public bool MitDurchschnitt => Noten.Count > 0 && Noten.All(n => n.Wert is not null);
}

/// <summary>Prüfungsart (Stammdaten | Prüfungsarten) mit Höchstzahlen je Schüler*in und Notenschema.</summary>
public sealed record Pruefungsart(int Id, string Kurz, string Name, bool Aktiv, bool Muendlich, bool Angekuendigt, bool Uebergeben, string Vordergrund,
    string Hintergrund, int MaxTag, int MaxWoche, int MaxZeitraum, int ZeitraumTage, int Gruppierung, int FreieTage, bool MitWochenende, int? NotenschemaId,
    double Gewichtung, int Reihenfolge)
{
    /// <summary>Name des Notenschemas (leer = keines: die Lehrkraft wählt beim Benutzen ein individuelles Schema).</summary>
    public string Notenschema { get; init; } = "";
}

/// <summary>Eingabe einer Prüfungsart (Formular „Neue Prüfungsart“).</summary>
public sealed record PruefungsartDaten(string? Kurz, string? Name, bool Aktiv = true, bool Muendlich = false, bool Angekuendigt = true, bool Uebergeben = true,
    string? Vordergrund = null, string? Hintergrund = null, int MaxTag = 0, int MaxWoche = 0, int MaxZeitraum = 0, int ZeitraumTage = 0, int Gruppierung = 0,
    int FreieTage = 0, bool MitWochenende = true, int? NotenschemaId = null, double Gewichtung = 1);

/// <summary>
/// Stammdaten für Noten und Prüfungen: Notenschemen (Noten mit optionalem Wert, Reihenfolge), Gesamtnotenschema und Prüfungsarten.
/// Die bisher fest hinterlegten Prüfungsarten (Klassenarbeit, Klausur, Test …) sind als Grundbestand angelegt und lassen sich erweitern.
/// </summary>
public sealed class Notendienst
{
    private const string GesamtSchluessel = "noten.gesamtschema";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Farbe = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private readonly Db _db;
    private readonly Einstellungen _einstellungen;

    public Notendienst(Db db, Einstellungen einstellungen) { _db = db; _einstellungen = einstellungen; }

    // ------------------------------------------------------------------------------------------ Notenschemen

    private static List<Note> NotenAus(string json)
    {
        try { return JsonSerializer.Deserialize<List<Note>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public List<Notenschema> Schemen() => _db.Liste("SELECT * FROM notenschemen ORDER BY reihenfolge, name",
        r => new Notenschema(r.Zahl("id"), r.Text("name"), r.Wahr("aktiv"), r.Zahl("reihenfolge"), NotenAus(r.Text("noten"))));

    public Notenschema? Schema(int id) => Schemen().FirstOrDefault(x => x.Id == id);

    /// <summary>Notenschema anlegen (id = null) bzw. ändern. Noten in der angegebenen Reihenfolge; leere Zeilen entfallen.</summary>
    public Notenschema SchemaSpeichern(int? id, string? name, bool aktiv, IEnumerable<Note>? noten)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 100) throw new ArgumentException("Bitte einen Namen mit höchstens 100 Zeichen eingeben.");
        if (Schemen().Any(x => x.Id != id && Ci.Equals(x.Name, name))) throw new ArgumentException($"Das Notenschema „{name}“ gibt es bereits.");
        var liste = (noten ?? Enumerable.Empty<Note>()).Select(n => n with { Text = (n.Text ?? "").Trim() }).Where(n => n.Text.Length > 0).ToList();
        if (liste.Count == 0) throw new ArgumentException("Bitte mindestens eine Note eingeben.");
        if (liste.Count > 100) throw new ArgumentException("Höchstens 100 Noten je Notenschema.");
        if (liste.Any(n => n.Text.Length > 30)) throw new ArgumentException("Eine Note darf höchstens 30 Zeichen lang sein.");
        if (liste.GroupBy(n => n.Text, Ci).FirstOrDefault(g => g.Count() > 1) is { } doppelt) throw new ArgumentException($"Die Note „{doppelt.Key}“ steht doppelt im Notenschema.");
        if (liste.Any(n => n.Wert is double w && (double.IsNaN(w) || w < -1000 || w > 1000))) throw new ArgumentException("Ungültiger Wert einer Note.");
        var json = JsonSerializer.Serialize(liste, Json);
        if (id is null)
        {
            var reihenfolge = Convert.ToInt32(_db.Wert("SELECT COALESCE(MAX(reihenfolge), 0) FROM notenschemen")) + 10;
            id = (int)_db.Einfuegen("INSERT INTO notenschemen (name, aktiv, reihenfolge, noten) VALUES (@n, @a, @r, @j)",
                Db.P("@n", name), Db.P("@a", aktiv), Db.P("@r", reihenfolge), Db.P("@j", json));
        }
        else if (_db.Ausfuehren("UPDATE notenschemen SET name = @n, aktiv = @a, noten = @j WHERE id = @id", Db.P("@n", name), Db.P("@a", aktiv), Db.P("@j", json), Db.P("@id", id)) == 0)
            throw new ArgumentException("Unbekanntes Notenschema.");
        return Schema(id.Value)!;
    }

    /// <summary>Notenschemen löschen; Prüfungsarten mit diesem Schema verlieren es (die Lehrkraft wählt dann ein individuelles Schema).</summary>
    public int SchemenLoeschen(IEnumerable<int> ids)
    {
        var n = 0;
        foreach (var id in ids.Distinct())
        {
            _db.Ausfuehren("UPDATE pruefungsarten SET notenschema_id = NULL WHERE notenschema_id = @id", Db.P("@id", id));
            n += _db.Ausfuehren("DELETE FROM notenschemen WHERE id = @id", Db.P("@id", id));
            if (Gesamtschema() == id) _einstellungen.Schreiben(GesamtSchluessel, "");
        }
        return n;
    }

    /// <summary>Reihenfolge der Notenschemen festlegen (alle Ids in der neuen Reihenfolge; ohne Angabe alphabetisch).</summary>
    public void SchemenSortieren(IEnumerable<int>? ids)
    {
        var alle = Schemen();
        var folge = ids?.Distinct().ToList() is { Count: > 0 } l ? l.Where(i => alle.Any(x => x.Id == i)).Concat(alle.Select(x => x.Id).Where(i => !l.Contains(i))).ToList()
            : alle.OrderBy(x => x.Name, StringComparer.Create(new CultureInfo("de-DE"), true)).Select(x => x.Id).ToList();
        for (var i = 0; i < folge.Count; i++) _db.Ausfuehren("UPDATE notenschemen SET reihenfolge = @r WHERE id = @id", Db.P("@r", (i + 1) * 10), Db.P("@id", folge[i]));
    }

    /// <summary>Gesamtnotenschema (für Gesamt- bzw. Zeugnisnoten); null = keines gewählt.</summary>
    public int? Gesamtschema() => int.TryParse(_einstellungen.Lesen(GesamtSchluessel), out var id) && Schema(id) != null ? id : null;

    public void GesamtschemaSetzen(int? id)
    {
        if (id is int i && Schema(i) is null) throw new ArgumentException("Unbekanntes Notenschema.");
        _einstellungen.Schreiben(GesamtSchluessel, id?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    /// <summary>Durchschnitt der Noten nach den Werten des Schemas; null, wenn das Schema keine Werte hat oder keine Note einen Wert hat.</summary>
    public static double? Durchschnitt(Notenschema schema, IEnumerable<string> noten)
    {
        if (!schema.MitDurchschnitt) return null;
        var werte = noten.Select(t => schema.Noten.FirstOrDefault(n => Ci.Equals(n.Text, t.Trim()))?.Wert).OfType<double>().ToList();
        return werte.Count == 0 ? null : Math.Round(werte.Average(), 2);
    }

    // ------------------------------------------------------------------------------------------ Prüfungsarten

    private Pruefungsart LesenArt(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("kurz"), r.Text("name"), r.Wahr("aktiv"), r.Wahr("muendlich"),
        r.Wahr("angekuendigt"), r.Wahr("uebergeben"), r.Text("vordergrund"), r.Text("hintergrund"), r.Zahl("max_tag"), r.Zahl("max_woche"), r.Zahl("max_zeitraum"),
        r.Zahl("zeitraum_tage"), r.Zahl("gruppierung"), r.Zahl("freie_tage"), r.Wahr("mit_wochenende"), r.ZahlOderNull("notenschema_id"),
        r["gewichtung"] is DBNull or null ? 1 : Convert.ToDouble(r["gewichtung"], CultureInfo.InvariantCulture), r.Zahl("reihenfolge"));

    /// <summary>Prüfungsarten (optional nur aktive) mit dem Namen ihres Notenschemas.</summary>
    public List<Pruefungsart> Arten(bool nurAktive = false)
    {
        var schemen = Schemen().ToDictionary(x => x.Id, x => x.Name);
        return _db.Liste("SELECT * FROM pruefungsarten ORDER BY reihenfolge, kurz", LesenArt)
            .Where(a => !nurAktive || a.Aktiv)
            .Select(a => a with { Notenschema = a.NotenschemaId is int i && schemen.TryGetValue(i, out var n) ? n : "" }).ToList();
    }

    private static int Anzahl(int n, string feld) => n is >= 0 and <= 99 ? n : throw new ArgumentException($"Bitte bei „{feld}“ eine Zahl von 0 bis 99 eingeben.");

    /// <summary>Prüfungsart anlegen (id = null) bzw. ändern.</summary>
    public Pruefungsart ArtSpeichern(int? id, PruefungsartDaten d)
    {
        var kurz = (d.Kurz ?? "").Trim();
        var name = (d.Name ?? "").Trim();
        if (kurz.Length is < 1 or > 20) throw new ArgumentException("Bitte einen Kurznamen mit höchstens 20 Zeichen eingeben.");
        if (name.Length > 100) throw new ArgumentException("Der Langname darf höchstens 100 Zeichen lang sein.");
        if (Arten().Any(a => a.Id != id && Ci.Equals(a.Kurz, kurz))) throw new ArgumentException($"Die Prüfungsart „{kurz}“ gibt es bereits.");
        var vg = string.IsNullOrWhiteSpace(d.Vordergrund) ? "#000000" : d.Vordergrund.Trim();
        var hg = string.IsNullOrWhiteSpace(d.Hintergrund) ? "#ffffff" : d.Hintergrund.Trim();
        if (!Farbe.IsMatch(vg) || !Farbe.IsMatch(hg)) throw new ArgumentException("Bitte Farben im Format #RRGGBB angeben.");
        if (d.NotenschemaId is int sid && Schema(sid) is null) throw new ArgumentException("Unbekanntes Notenschema.");
        if (double.IsNaN(d.Gewichtung) || d.Gewichtung is < 0 or > 100) throw new ArgumentException("Bitte einen Gewichtungsfaktor von 0 bis 100 eingeben.");
        var p = new[]
        {
            Db.P("@k", kurz), Db.P("@n", name), Db.P("@a", d.Aktiv), Db.P("@m", d.Muendlich), Db.P("@an", d.Angekuendigt), Db.P("@u", d.Uebergeben),
            Db.P("@vg", vg.ToLowerInvariant()), Db.P("@hg", hg.ToLowerInvariant()), Db.P("@mt", Anzahl(d.MaxTag, "Pro Tag")), Db.P("@mw", Anzahl(d.MaxWoche, "Pro Kalenderwoche")),
            Db.P("@mz", Anzahl(d.MaxZeitraum, "Pro Zeitbereich")), Db.P("@zt", Anzahl(d.ZeitraumTage, "innerhalb von Tagen")), Db.P("@gr", Anzahl(d.Gruppierung, "Gruppierung")),
            Db.P("@ft", Anzahl(d.FreieTage, "Keine Prüfung nach freien Tagen")), Db.P("@we", d.MitWochenende), Db.P("@ns", d.NotenschemaId),
            Db.P("@gw", Math.Round(d.Gewichtung, 2)), Db.P("@id", id),
        };
        if (id is null)
        {
            var reihenfolge = Convert.ToInt32(_db.Wert("SELECT COALESCE(MAX(reihenfolge), 0) FROM pruefungsarten")) + 10;
            id = (int)_db.Einfuegen("""
                INSERT INTO pruefungsarten (kurz, name, aktiv, muendlich, angekuendigt, uebergeben, vordergrund, hintergrund, max_tag, max_woche, max_zeitraum,
                    zeitraum_tage, gruppierung, freie_tage, mit_wochenende, notenschema_id, gewichtung, reihenfolge)
                VALUES (@k, @n, @a, @m, @an, @u, @vg, @hg, @mt, @mw, @mz, @zt, @gr, @ft, @we, @ns, @gw, @r)
                """, p.Append(Db.P("@r", reihenfolge)).ToArray());
        }
        else if (_db.Ausfuehren("""
                UPDATE pruefungsarten SET kurz = @k, name = @n, aktiv = @a, muendlich = @m, angekuendigt = @an, uebergeben = @u, vordergrund = @vg, hintergrund = @hg,
                    max_tag = @mt, max_woche = @mw, max_zeitraum = @mz, zeitraum_tage = @zt, gruppierung = @gr, freie_tage = @ft, mit_wochenende = @we,
                    notenschema_id = @ns, gewichtung = @gw WHERE id = @id
                """, p) == 0)
            throw new ArgumentException("Unbekannte Prüfungsart.");
        return Arten().First(a => a.Id == id);
    }

    public int ArtenLoeschen(IEnumerable<int> ids) => ids.Distinct().Sum(id => _db.Ausfuehren("DELETE FROM pruefungsarten WHERE id = @id", Db.P("@id", id)));

    /// <summary>Reihenfolge der Prüfungsarten festlegen (ohne Angabe alphabetisch nach Kurzname).</summary>
    public void ArtenSortieren(IEnumerable<int>? ids)
    {
        var alle = Arten();
        var folge = ids?.Distinct().ToList() is { Count: > 0 } l ? l.Where(i => alle.Any(x => x.Id == i)).Concat(alle.Select(x => x.Id).Where(i => !l.Contains(i))).ToList()
            : alle.OrderBy(x => x.Kurz, StringComparer.Create(new CultureInfo("de-DE"), true)).Select(x => x.Id).ToList();
        for (var i = 0; i < folge.Count; i++) _db.Ausfuehren("UPDATE pruefungsarten SET reihenfolge = @r WHERE id = @id", Db.P("@r", (i + 1) * 10), Db.P("@id", folge[i]));
    }
}
