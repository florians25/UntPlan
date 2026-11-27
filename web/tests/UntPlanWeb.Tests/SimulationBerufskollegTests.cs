using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Schulsimulation am Berufskolleg (NRW): Bildungsgänge aus dem Klassennamen, Kammerprüfungen der Berufsschule, Fehlzeiten nach Teilzeit und Vollzeit,
/// betriebliche Gründe und Entschuldigung über den Ausbildungsbetrieb, Lernfelder und Lernsituationen, Abschlussprüfungen der Fachoberschule,
/// Ablage in Teams, Moodle bzw. LOGINEO, Sprechtag nur für Minderjährige bzw. über die Betriebe.
/// </summary>
public class SimulationBerufskollegTests
{
    /// <summary>Beispielpaket mit Klassen eines Berufskollegs (umbenannte Klassen samt Unterricht und Schüler*innen).</summary>
    private static async Task<Testserver> BerufskollegAsync()
    {
        var s = await Testserver.StartenAsync();
        var text = Testserver.Beispielpaket().ToJsonString();
        foreach (var (alt, neu) in new[] { ("7a", "ITA25"), ("7b", "ELG24"), ("6a", "FOS12A"), ("6b", "BFS1"), ("5b", "FSP2") })
            text = text.Replace($"\"{alt}\"", $"\"{neu}\"");
        var paket = JsonNode.Parse(text)!.AsObject();
        foreach (var k in paket["elemente"]!["klassen"]!.AsArray()) if (k!["kurz"]!.GetValue<string>() is "ITA25" or "ELG24" or "FOS12A" or "BFS1" or "FSP2") k["jahrgang"] = "";
        paket["revision"] = 2;
        Assert.True((await s.PaketSendenAsync(paket)).IsSuccessStatusCode);
        return s;
    }

    [Fact]
    public async Task Bildungsgaenge_aus_Klassennamen_und_Faechern()
    {
        await using var s = await BerufskollegAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        Assert.Equal("bs", Schulsimulation.Bildungsgang(st, "ITA25"));
        Assert.Equal("fos12", Schulsimulation.Bildungsgang(st, "FOS12A"));
        Assert.Equal("bfs", Schulsimulation.Bildungsgang(st, "BFS1"));
        Assert.Equal("fs", Schulsimulation.Bildungsgang(st, "FSP2"));
        Assert.Equal("sek1u", Schulsimulation.Bildungsgang(st, "5a"));
        Assert.Equal("sek2", Schulsimulation.Bildungsgang(st, "EF"));
        foreach (var (k, b) in new[] { ("FOS11B", "fos11"), ("FOS13", "fos13"), ("BG12", "bg"), ("AHR11", "bg"), ("PIA1", "fs"), ("HBFS1", "bfs"), ("AV1", "av"), ("IFK2", "av"), ("XYZ", "bk") })
            Assert.Equal(b, Schulsimulation.Bildungsgang(st, k));
        Assert.True(Schulsimulation.Teilzeit("bs"));
        Assert.True(Schulsimulation.Fehlfaktor("bfs") > Schulsimulation.Fehlfaktor("bs"));
        // Ausbildungsjahr aus dem Jahrgang im Klassennamen (Schuljahr 2026/27)
        Assert.Equal(2, Schulsimulation.Ausbildungsjahr(st, "ITA25"));
        Assert.Equal(3, Schulsimulation.Ausbildungsjahr(st, "ELG24"));
        Assert.Null(Schulsimulation.Ausbildungsjahr(st, "BFS1"));
        // Kammerprüfungen: Teil 1 im März des 2. Jahres, Teil 2 im Frühjahr des 3. Jahres, an einem Unterrichtstag der Klasse
        var sim = s.Dienst<Schulsimulation>();
        var k1 = sim.KammerpruefungDer(st, 4711, "ITA25")!;
        Assert.Equal("Abschlussprüfung Teil 1", k1.Name);
        Assert.Equal(3, k1.Tag.Month);
        var k2 = sim.KammerpruefungDer(st, 4711, "ELG24")!;
        Assert.Equal("Abschlussprüfung Teil 2", k2.Name);
        Assert.InRange(k2.Tag, new DateOnly(2027, 4, 22), new DateOnly(2027, 5, 20));
        Assert.Null(sim.KammerpruefungDer(st, 4711, "FOS12A"));
    }

    [Fact]
    public async Task Kammerpruefung_Fehlzeiten_Entschuldigungen_und_Lernfelder()
    {
        await using var s = await BerufskollegAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var sim = s.Dienst<Schulsimulation>();
        var uhr = s.Dienst<Uhr>();
        var admin = s.Dienst<Benutzerdienst>().NachLogin("admin")!;
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        // am Tag der Kammerprüfung: Klasse freigestellt (alle fehlen mit Beurlaubung), Lehrstoff vermerkt die Prüfung
        var kp = sim.KammerpruefungDer(st, 4711, "ITA25")!;
        uhr.Fest = kp.Tag.AddDays(-1).ToDateTime(new TimeOnly(18, 0));
        sim.Speichern(admin, new Simulationseinstellungen { Aktiv = true });
        uhr.Fest = kp.Tag.ToDateTime(new TimeOnly(23, 0));
        sim.Lauf();
        var db = s.Dienst<Db>();
        var fz = db.Liste("SELECT schueler_id, grund, bemerkung FROM fehlzeiten WHERE klasse = 'ITA25' AND datum = @d", r => (Id: r.Zahl("schueler_id"), Grund: r.Text("grund"), Bem: r.Text("bemerkung")), Db.P("@d", kp.Tag));
        Assert.NotEmpty(fz);
        Assert.Equal(st.Schueler.Count(x => x.Klasse == "ITA25"), fz.Select(x => x.Id).Distinct().Count());
        Assert.All(fz, x => { Assert.Equal("B", x.Grund); Assert.Contains("Abschlussprüfung Teil 1", x.Bem); });
        Assert.Contains(db.Liste("SELECT thema FROM klassenbuch WHERE datum = @d AND thema <> ''", r => r.Text("thema"), Db.P("@d", kp.Tag)), t => t.Contains("Kammer"));
        sim.Entfernen(admin);

        // Vergangenheit: Berufsschule mit betrieblichen Gründen und Entschuldigung über den Betrieb, Vollzeit (BFS) mit mehr Fehlzeiten als Teilzeit
        uhr.Fest = new DateTime(2026, 10, 14, 10, 0, 0);
        sim.Speichern(admin, new Simulationseinstellungen { Aktiv = true });
        sim.Nachpflegen(st, sim.Laden(), new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 13));
        var texte = db.Liste("SELECT f.klasse AS klasse, e.text AS text FROM fehlzeit_entschuldigungen e JOIN fehlzeiten f ON f.schueler_id = e.schueler_id AND f.datum = e.datum AND f.stunde = e.stunde",
            r => (Klasse: r.Text("klasse"), Text: r.Text("text")));
        Assert.Contains(texte, t => t.Klasse is "ITA25" or "ELG24" && (t.Text.Contains("Betrieb") || t.Text.Contains("Arbeitsunfähigkeit") || t.Text.Contains("Ausbild")));
        Assert.DoesNotContain(texte, t => t.Klasse is "5a" && t.Text.Contains("Betrieb"));
        var e = sim.Laden();
        var tage = Enumerable.Range(0, 200).Select(i => new DateOnly(2026, 9, 3).AddDays(i)).Where(d => !s.Dienst<Plandienst>().Frei(st, d).Frei).ToList();
        var aus = Schulsimulation.SchuelerausfaelleBerechnen(st, 4711, tage[0], tage[^1], tage);
        double Quote(string k) => aus.Count(a => a.Klasse == k && a.Art != "verspaetet") / (double)Math.Max(1, st.Schueler.Count(x => x.Klasse == k));
        Assert.True(Quote("BFS1") > Quote("ITA25"), $"BFS {Quote("BFS1")} BS {Quote("ITA25")}");
        Assert.Contains(aus, a => a.Art == "betrieb" && a.Klasse is "ITA25" or "ELG24");
        Assert.DoesNotContain(aus, a => a.Art == "betrieb" && a.Klasse is not ("ITA25" or "ELG24"));
        var (t1, n1) = Schulsimulation.EntschuldigungText(new Schulsimulation.Schuelerausfall(1, "ITA25", "krank", new() { tage[0], tage[1], tage[2] }, 1, 99, 0, "K"), "entschuldigt", new Zufall(5), "bs");
        Assert.Contains("Arbeitsunfähigkeitsbescheinigung", t1);
        Assert.Equal("attest", n1);

        // Lernfelder und Lernsituationen in beruflichen Fächern der Berufsschule; allgemeinbildende Fächer ohne
        var w = sim.Welt(st, e, DateTime.Parse(e.Start), DateTime.Parse(e.Start));
        var u = st.Unterricht.Values.First(x => x.Zeilen[0].Klassen.Contains("ITA25")).Id;
        var bl = sim.Bloecke(st, u);
        var lf = bl.Skip(3).Take(10).Select(b => sim.Lehrstoff(st, w, e, "AWE", "ITA25", u, b.Tag, b.Von)!).ToList();
        Assert.All(lf.Where(x => !x.StartsWith("Rückgabe") && !x.StartsWith("Klassenarbeit")), x => Assert.Matches(@"^LF \d+[a-z]?, LS \d+[a-z]?\.\d+: ", x));
        Assert.DoesNotContain(bl.Skip(3).Take(10).Select(b => sim.Lehrstoff(st, w, e, "D", "5a", u, b.Tag, b.Von)!), x => x.StartsWith("LF "));

        // Fachoberschule 12: Abschlussprüfungen in D, M, E im Frühjahr
        var hj2 = Schulsimulation.Halbjahre(st)[1];
        var plan = sim.Pruefungsplanung(st, e, w, hj2.Von, hj2.Bis, DateTime.Parse(e.Start)).Where(p => p.Klasse == "FOS12A").ToList();
        Assert.Contains(plan, p => p.Abschluss);
        Assert.All(plan.Where(p => p.Abschluss), p => { Assert.Contains(p.Fach, new[] { "D", "M", "E" }); Assert.InRange(p.Datum, new DateOnly(2027, 4, 15), new DateOnly(2027, 5, 31)); });
        Assert.Equal("Klausur", Schulsimulation.Pruefungsart(st, "FOS12A"));
        Assert.Equal("Klassenarbeit", Schulsimulation.Pruefungsart(st, "ITA25"));
    }

    [Fact]
    public async Task Mitteilungen_im_Ton_des_Berufskollegs_und_Sprechtag()
    {
        await using var s = await BerufskollegAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var b = new List<Schulsimulation.BetroffeneStunde> { new(new DateOnly(2026, 10, 13), 1, 2, "ITA25", "AWE") };
        var bk = Enumerable.Range(0, 300).Select(i => Schulsimulation.AufgabenText(b, new Zufall((ulong)i * 31 + 7), true, true)).ToList();
        Assert.Contains(bk, t => t.Contains("Moodle") || t.Contains("LOGINEO"));
        Assert.Contains(bk, t => t.Contains("Betriebe"));
        Assert.Contains(bk, t => t.Contains("Teams"));
        var allg = Enumerable.Range(0, 300).Select(i => Schulsimulation.AufgabenText(b, new Zufall((ulong)i * 31 + 7))).ToList();
        Assert.DoesNotContain(allg, t => t.Contains("Moodle") || t.Contains("Betriebe"));
        // Sprechtag: Berufsschule über die Betriebe, übrige Bildungsgänge nur Minderjährige
        Assert.All(st.Schueler.Where(x => x.Klasse == "ITA25"), x => Assert.True(Schulsimulation.Sprechtagberechtigt(st, x)));
        var fos = st.Schueler.Where(x => x.Klasse == "FOS12A").ToList();
        Assert.True(fos.Count(x => Schulsimulation.Sprechtagberechtigt(st, x)) < fos.Count / 3);
    }
}
