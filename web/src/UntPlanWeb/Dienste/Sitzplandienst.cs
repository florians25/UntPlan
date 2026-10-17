using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Gespeicherter Sitzplan eines Wirkungsbereichs (Klasse, Unterricht oder Raum).</summary>
/// <param name="Bereich"><c>klasse</c>, <c>unterricht</c> oder <c>raum</c>.</param>
/// <param name="Schluessel">Klassenkürzel, Unterrichtsnummer bzw. „Raum|Lehrkraft“.</param>
/// <param name="Pult">Lage des Lehrerpults in der Darstellung: <c>vorne</c> (oben) oder <c>hinten</c> (unten, Sicht vom Pult).</param>
/// <param name="Belegung">Platz „Reihe-Spalte“ (Reihe 1 = am Pult) → Schüler-ID.</param>
/// <param name="OhneTisch">Plätze ohne Tisch (Gänge, Lücken) als „Reihe-Spalte“.</param>
public sealed record Sitzplan(string Bereich, string Schluessel, int Zeilen, int Spalten, string Pult, Dictionary<string, int> Belegung, List<string> OhneTisch,
    string Lehrer, string Geaendert);

/// <summary>Eingabe zum Speichern eines Sitzplans.</summary>
public sealed record SitzplanEingabe(int Zeilen, int Spalten, string? Pult, Dictionary<string, int>? Belegung, List<string>? OhneTisch);

/// <summary>Kontext einer Stunde für Sitzpläne: Unterricht, Räume und Klassen der Stunde.</summary>
public sealed record SitzplanKontext(int? Unterricht, List<string> Raeume, List<string> Klassen, string? Lehrer = null);

/// <summary>
/// Sitzpläne im Klassenbuch wie im Original-Modul „Sitzplan“:
/// <list type="bullet">
/// <item><b>Klasse</b>: gilt in jedem Unterricht der Klasse; nur die Klassenleitung (bzw. Schulleitung/Administration) legt ihn an.</item>
/// <item><b>Unterricht</b>: gilt nur in diesem Unterricht (der Klassen-Sitzplan wird dort nicht mehr angezeigt); Lehrkraft des Unterrichts.</item>
/// <item><b>Raum</b>: Sitzplan einer Lehrkraft für einen Raum aus ihren Unterrichten (z. B. Physiksaal); gilt in ihren Stunden in diesem Raum.</item>
/// </list>
/// Gültiger Plan einer Stunde: Unterricht vor Raum vor Klasse.
/// </summary>
public sealed class Sitzplandienst
{
    public const string Klasse = "klasse", Unterricht = "unterricht", Raum = "raum";
    public const int Maximum = 15;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex Platz = new(@"^\d{1,2}-\d{1,2}$", RegexOptions.Compiled);

    private readonly Db _db;
    private readonly Uhr _uhr;

    public Sitzplandienst(Db db, Uhr uhr) { _db = db; _uhr = uhr; }

    // ------------------------------------------------------------------------------------------ Schlüssel und Rechte

    public static bool IstBereich(string? bereich) => bereich is Klasse or Unterricht or Raum;

    /// <summary>Kanonischer Schlüssel eines Bereichs; wirft bei unbekannten Elementen.</summary>
    public static string Schluessel(Stand s, string bereich, string? klasse, int? unterricht, string? raum, string? lehrer) => bereich switch
    {
        Klasse => klasse is not null && s.Klassen.TryGetValue(klasse, out var k) ? k.Kurz : throw new ArgumentException($"Unbekannte Klasse {klasse}."),
        Unterricht => unterricht is int u && s.Unterricht.ContainsKey(u) ? u.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentException("Unbekannter Unterricht."),
        Raum => (raum is not null && s.Raeume.TryGetValue(raum, out var r) ? r.Kurz : throw new ArgumentException($"Unbekannter Raum {raum}."))
            + "|" + (lehrer is not null && s.Lehrer.TryGetValue(lehrer, out var l) ? l.Kurz : throw new ArgumentException("Für einen Raum-Sitzplan fehlt die Lehrkraft.")),
        _ => throw new ArgumentException("Unbekannter Wirkungsbereich (klasse, unterricht oder raum)."),
    };

    private static bool Leitung(Benutzer b) => Rechtedienst.Darf(b, Recht.KlassenbuchAlle);
    private static bool Eigene(Benutzer b) => Rechtedienst.Darf(b, Recht.SitzplanKlasse);

    /// <summary>Lehrkräfte eines Unterrichts (aus allen Zeilen).</summary>
    public static List<string> LehrerVon(Stand s, int unterricht) =>
        s.Unterricht.TryGetValue(unterricht, out var u) ? u.Zeilen.Select(z => z.Lehrer).Where(x => x.Length > 0).Distinct(Ci).ToList() : new();

    /// <summary>Klassen eines Unterrichts (aus allen Zeilen).</summary>
    public static List<string> KlassenVon(Stand s, int unterricht) =>
        s.Unterricht.TryGetValue(unterricht, out var u) ? u.Zeilen.SelectMany(z => z.Klassen).Distinct(Ci).ToList() : new();

    /// <summary>Eigener Unterricht: die Lehrkraft steht in einer Zeile des Unterrichts (Schulleitung/Administration: jeder).</summary>
    public static bool DarfUnterricht(Benutzer b, Stand s, int unterricht) =>
        s.Unterricht.ContainsKey(unterricht) && (Leitung(b) || Eigene(b) && b.LehrerKuerzel is string l && LehrerVon(s, unterricht).Contains(l, Ci));

    /// <summary>Räume aus den eigenen Unterrichten einer Lehrkraft (Stundenplan aller Perioden).</summary>
    public static HashSet<string> EigeneRaeume(Stand s, string lehrer)
    {
        var eigene = s.Unterricht.Values.Where(u => u.Zeilen.Any(z => Ci.Equals(z.Lehrer, lehrer))).Select(u => u.Id).ToHashSet();
        return s.Paket.Perioden.SelectMany(p => p.Stundenplan).Where(x => eigene.Contains(x.Unterricht)).SelectMany(x => x.Raeume).ToHashSet(Ci);
    }

    /// <summary>
    /// Raum-Sitzplan: eigene Räume der Lehrkraft (aus ihren Unterrichten) bzw. der Raum der aktuellen Stunde eines eigenen Unterrichts
    /// (z. B. nach einer Raumänderung). Schulleitung/Administration: jeder Raum.
    /// </summary>
    public static bool DarfRaum(Benutzer b, Stand s, string raum, string lehrer, SitzplanKontext? kontext = null)
    {
        if (!s.Raeume.ContainsKey(raum)) return false;
        if (Leitung(b)) return true;
        if (!Eigene(b) || b.LehrerKuerzel is not string l || !Ci.Equals(l, lehrer)) return false;
        if (EigeneRaeume(s, l).Contains(raum)) return true;
        return kontext?.Unterricht is int u && DarfUnterricht(b, s, u) && kontext.Raeume.Contains(raum, Ci);
    }

    public static bool DarfKlasse(Benutzer b, Stand s, string klasse) => s.Klassen.ContainsKey(klasse) && (Leitung(b) || Eigene(b) && Klassenbuchdienst.IstKlassenleitung(b, s, klasse));

    // ------------------------------------------------------------------------------------------ Lesen

    private static Sitzplan Lesen(System.Data.Common.DbDataReader r) => new(
        r.Text("bereich"), r.Text("schluessel"), r.Zahl("zeilen"), r.Zahl("spalten"), r.Text("pult") == "hinten" ? "hinten" : "vorne",
        Json<Dictionary<string, int>>(r.Text("belegung")) ?? new(), Json<List<string>>(r.Text("ohne_tisch")) ?? new(), r.Text("lehrer"), r.Text("geaendert"));

    private static T? Json<T>(string text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<T>(text); } catch (JsonException) { return null; }
    }

    public Sitzplan? Laden(string bereich, string schluessel) =>
        _db.Einzel("SELECT * FROM sitzplan_bereiche WHERE bereich = @b AND schluessel = @s", Lesen, Db.P("@b", bereich), Db.P("@s", schluessel));

    /// <summary>Sitzplan eines Bereichs oder null (unbekannte Elemente zählen als „kein Plan“).</summary>
    public Sitzplan? Laden(Stand s, string bereich, string? klasse, int? unterricht, string? raum, string? lehrer)
    {
        try { return Laden(bereich, Schluessel(s, bereich, klasse, unterricht, raum, lehrer)); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Leerer Plan (Vorgabe 5 Reihen × 6 Plätze, Pult vorne).</summary>
    public static Sitzplan Leer(string bereich, string schluessel) => new(bereich, schluessel, 5, 6, "vorne", new(), new(), "", "");

    /// <summary>Lehrkraft für Raum-Sitzpläne in einer Stunde: angegebene bzw. eigene, sonst die erste des Unterrichts.</summary>
    public static string? LehrerFuerRaum(Benutzer b, Stand s, SitzplanKontext k)
    {
        var liste = k.Unterricht is int u ? LehrerVon(s, u) : new List<string>();
        if (k.Lehrer is { Length: > 0 } x && s.Lehrer.ContainsKey(x)) return s.Lehrer[x].Kurz;
        // Lehrkräfte legen Raum-Sitzpläne immer unter dem eigenen Kürzel an; Schulleitung/Administration für die Lehrkraft des Unterrichts
        if (b.LehrerKuerzel is string l && s.Lehrer.ContainsKey(l) && (!Leitung(b) || liste.Count == 0 || liste.Contains(l, Ci))) return s.Lehrer[l].Kurz;
        return liste.FirstOrDefault();
    }

    /// <summary>Klassen der Stunde: angegebene (vorhandene) Klassen, sonst die Klassen des Unterrichts.</summary>
    public static List<string> Klassen(Stand s, SitzplanKontext k)
    {
        var res = k.Klassen.Where(s.Klassen.ContainsKey).Select(x => s.Klassen[x].Kurz).ToList();
        if (k.Unterricht is int u) res.AddRange(KlassenVon(s, u).Where(s.Klassen.ContainsKey).Select(x => s.Klassen[x].Kurz));
        return res.Distinct(Ci).ToList();
    }

    /// <summary>
    /// Gültiger Sitzplan einer Stunde: Unterricht vor Raum (Lehrkräfte des Unterrichts, Räume der Stunde) vor Klasse
    /// (erste Klasse der Stunde mit Sitzplan). Belegung auf die Schüler*innen der Stunde beschränkt.
    /// </summary>
    public Sitzplan? Aufloesen(Benutzer b, Stand s, SitzplanKontext k, ISet<int> schueler)
    {
        Sitzplan? plan = null;
        if (k.Unterricht is int u) plan = Laden(s, Unterricht, null, u, null, null);
        if (plan is null)
        {
            // Raum-Sitzpläne der Lehrkräfte dieses Unterrichts (eigener zuerst)
            var lehrer = k.Unterricht is int u2 ? LehrerVon(s, u2) : new List<string>();
            if (k.Lehrer is { Length: > 0 } hinweis) lehrer.Insert(0, hinweis);
            if (b.LehrerKuerzel is string eigen && lehrer.Contains(eigen, Ci)) lehrer.Insert(0, eigen);
            foreach (var r in k.Raeume)
            {
                foreach (var l in lehrer.Distinct(Ci))
                    if ((plan = Laden(s, Raum, null, null, r, l)) is not null) break;
                if (plan is not null) break;
            }
        }
        if (plan is null)
            foreach (var kl in Klassen(s, k))
                if ((plan = Laden(s, Klasse, kl, null, null, null)) is not null) break;
        return plan is null ? null : plan with { Belegung = plan.Belegung.Where(x => schueler.Contains(x.Value)).ToDictionary(x => x.Key, x => x.Value) };
    }

    // ------------------------------------------------------------------------------------------ Schreiben

    /// <summary>Eingabe bereinigen: Größe 1–15, nur gültige Plätze im Raster, nur erlaubte Schüler*innen, jede*r höchstens einmal, nicht auf Plätzen ohne Tisch.</summary>
    public static (int Zeilen, int Spalten, string Pult, Dictionary<string, int> Belegung, List<string> OhneTisch) Bereinigen(SitzplanEingabe d, ISet<int> erlaubt)
    {
        var zeilen = Math.Clamp(d.Zeilen, 1, Maximum);
        var spalten = Math.Clamp(d.Spalten, 1, Maximum);
        bool ImRaster(string p)
        {
            if (!Platz.IsMatch(p)) return false;
            var t = p.Split('-');
            return int.Parse(t[0]) is >= 1 and var z && z <= zeilen && int.Parse(t[1]) is >= 1 and var sp && sp <= spalten;
        }
        var ohne = (d.OhneTisch ?? new()).Where(ImRaster).Distinct().ToList();
        var belegung = (d.Belegung ?? new()).Where(x => ImRaster(x.Key) && erlaubt.Contains(x.Value) && !ohne.Contains(x.Key))
            .DistinctBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
        return (zeilen, spalten, d.Pult == "hinten" ? "hinten" : "vorne", belegung, ohne);
    }

    /// <summary>
    /// Sitzplan speichern. <paramref name="schueler"/> sind die Schüler*innen, die in diesem Kontext gesetzt werden dürfen.
    /// Raum-Sitzpläne gelten für mehrere Unterrichte: Plätze anderer Schüler*innen (nicht in diesem Kontext) bleiben erhalten, solange sie frei sind.
    /// </summary>
    public Sitzplan Speichern(Benutzer b, string bereich, string schluessel, SitzplanEingabe d, ISet<int> schueler, string lehrer = "")
    {
        var (zeilen, spalten, pult, belegung, ohne) = Bereinigen(d, schueler);
        if (bereich == Raum && Laden(bereich, schluessel) is { } alt)
        {
            var bereinigt = Bereinigen(new SitzplanEingabe(zeilen, spalten, pult, alt.Belegung, ohne), alt.Belegung.Values.ToHashSet()).Belegung;
            foreach (var (platz, id) in bereinigt)
                if (!schueler.Contains(id) && !belegung.ContainsKey(platz) && !belegung.ContainsValue(id)) belegung[platz] = id;
        }
        _db.Ausfuehren("""
            INSERT INTO sitzplan_bereiche (bereich, schluessel, zeilen, spalten, pult, belegung, ohne_tisch, lehrer, geaendert, benutzer_id)
            VALUES (@b, @s, @z, @sp, @p, @bel, @o, @l, @g, @u)
            ON CONFLICT(bereich, schluessel) DO UPDATE SET zeilen = excluded.zeilen, spalten = excluded.spalten, pult = excluded.pult, belegung = excluded.belegung,
                ohne_tisch = excluded.ohne_tisch, lehrer = excluded.lehrer, geaendert = excluded.geaendert, benutzer_id = excluded.benutzer_id
            """, Db.P("@b", bereich), Db.P("@s", schluessel), Db.P("@z", zeilen), Db.P("@sp", spalten), Db.P("@p", pult),
            Db.P("@bel", JsonSerializer.Serialize(belegung)), Db.P("@o", JsonSerializer.Serialize(ohne)), Db.P("@l", lehrer),
            Db.P("@g", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@u", b.Id));
        return Laden(bereich, schluessel)!;
    }

    public bool Loeschen(string bereich, string schluessel) =>
        _db.Ausfuehren("DELETE FROM sitzplan_bereiche WHERE bereich = @b AND schluessel = @s", Db.P("@b", bereich), Db.P("@s", schluessel)) > 0;

    // ------------------------------------------------------------------------------------------ Anzeige und Druck

    /// <summary>Anzeigename nach den Klassenbuch-Einstellungen (Vorname zuerst, Kurzform „Vorname N.“, Klasse anzeigen).</summary>
    public static string Anzeigename(WebSchueler x, bool vornameZuerst, bool kurz, bool klasse)
    {
        var name = kurz ? $"{x.Vorname} {(x.Name.Length > 0 ? x.Name[..1] + "." : "")}".Trim()
            : vornameZuerst ? $"{x.Vorname} {x.Name}".Trim() : $"{x.Name} {x.Vorname}".Trim();
        return klasse && x.Klasse.Length > 0 ? $"{name} ({x.Klasse})" : name;
    }

    /// <summary>Sitzplan als PDF (A4 quer, Schwarz-Weiß): Tische als Kästen mit Namen, Pult vorne bzw. hinten, Liste ohne Platz.</summary>
    public static byte[] Pdf(string schule, string titel, string untertitel, Sitzplan plan, IReadOnlyList<WebSchueler> schueler, bool vornameZuerst, bool kurz, bool klasse, string? ersteller,
        bool hochformat = false)
    {
        var nachId = schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var plaetze = new List<(int Reihe, int Spalte, string? Name, bool Tisch)>();
        var ohne = plan.OhneTisch.ToHashSet();
        for (var z = 1; z <= plan.Zeilen; z++)
            for (var sp = 1; sp <= plan.Spalten; sp++)
            {
                var k = $"{z}-{sp}";
                plaetze.Add((z, sp, plan.Belegung.TryGetValue(k, out var id) && nachId.TryGetValue(id, out var x) ? Anzeigename(x, vornameZuerst, kurz, false) : null, !ohne.Contains(k)));
            }
        var gesetzt = plan.Belegung.Values.ToHashSet();
        var ohnePlatz = schueler.Where(x => !gesetzt.Contains(x.Id)).Select(x => Anzeigename(x, vornameZuerst, kurz, klasse)).ToList();
        string? KlasseVon(int z, int sp) => klasse && plan.Belegung.TryGetValue($"{z}-{sp}", out var id) && nachId.TryGetValue(id, out var x) ? x.Klasse : null;
        return PdfBericht.Sitzplan(schule, titel, untertitel, plan.Zeilen, plan.Spalten, plan.Pult == "hinten",
            plaetze.Select(p => (p.Reihe, p.Spalte, p.Name, KlasseVon(p.Reihe, p.Spalte), p.Tisch)).ToList(), ohnePlatz, ersteller, hochformat);
    }
}
