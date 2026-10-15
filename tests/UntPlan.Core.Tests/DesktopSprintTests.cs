using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Sprint „UntPlan Desktop“: Blockbildung im (Mini-)Stundenplan (UP-01), formatierte Tagestexte (UP-02), Anzeige
/// unterrichtsfreier Zeiten (UP-03), Kennzahlen der Ferien (UP-04), Verlegung aufheben und Absenzfolgen (Z1/Z2).
/// </summary>
public class DesktopSprintTests
{
    // ---------------------------------------------------------------- UP-01 Blockbildung

    private static List<PlanBlock<(string U, int S)>> Bloecke(int stunden, params (string U, int S)[] eintraege) =>
        Blockbildung.Anordnen<(string U, int S)>(stunden, s => eintraege.Where(e => e.S == s).ToList(), e => e.U);

    [Fact]
    public void DoppelstundeWirdEinBlockGetrennteUnterrichteNicht()
    {
        var b = Bloecke(14, ("M", 1), ("M", 2), ("D", 3), ("E", 4), ("E", 5), ("E", 6));
        Assert.Equal(3, b.Count);
        Assert.Equal((1, 2), (b[0].Start, b[0].Laenge));
        Assert.Equal((3, 1), (b[1].Start, b[1].Laenge));
        Assert.Equal((4, 3), (b[2].Start, b[2].Laenge));
        Assert.All(b, x => Assert.Equal(1, x.Spuren));
    }

    [Fact]
    public void GleichzeitigeBloeckeLiegenNebeneinanderOhneUeberdeckung()
    {
        var b = Bloecke(14, ("A", 1), ("A", 2), ("B", 2), ("B", 3), ("C", 5));
        var a = b.Single(x => x.Erster.U == "A");
        var bb = b.Single(x => x.Erster.U == "B");
        var c = b.Single(x => x.Erster.U == "C");
        Assert.Equal(2, a.Spuren);
        Assert.Equal(2, bb.Spuren);
        Assert.NotEqual(a.Spur, bb.Spur);
        Assert.Equal(1, c.Spuren);
        // zeitlich korrekt: jeder Eintrag liegt genau in seinem Block
        Assert.Equal(new[] { 1, 2 }, a.Alle.Select(x => x.S));
        Assert.Equal(new[] { 2, 3 }, bb.Alle.Select(x => x.S));
    }

    [Fact]
    public void OhneSchluesselWirdNichtZusammengefasst()
    {
        var b = Blockbildung.Anordnen<int>(4, s => s <= 2 ? new[] { s } : Array.Empty<int>(), _ => null);
        Assert.Equal(2, b.Count);
        Assert.All(b, x => Assert.Equal(1, x.Laenge));
    }

    [Fact]
    public void BlockWirdAnPausenzeilenGeteilt()
    {
        // Pausen nach der 2. und 4. Stunde (z. B. „1. P“, „2. P“): Block 2–5 wird in drei Teile gezeichnet
        var pausen = new HashSet<int> { 2, 4 };
        var teile = Blockbildung.Teile(2, 4, pausen);
        Assert.Equal(new[] { new BlockTeil(2, 1), new BlockTeil(3, 2), new BlockTeil(5, 1) }, teile);
        // Doppelstunde ohne Pause dazwischen bleibt ein Teil
        Assert.Single(Blockbildung.Teile(5, 2, pausen));
        // Einzelstunde vor einer Pause
        Assert.Equal(new[] { new BlockTeil(2, 1) }, Blockbildung.Teile(2, 1, pausen));
        // Teile decken lückenlos genau die Stunden des Blocks ab und enthalten keine Pause im Inneren
        var t2 = Blockbildung.Teile(1, 14, new HashSet<int> { 2, 4, 6, 9 });
        Assert.Equal(Enumerable.Range(1, 14), t2.SelectMany(t => Enumerable.Range(t.Start, t.Laenge)));
        Assert.All(t2, t => Assert.DoesNotContain(Enumerable.Range(t.Start, t.Laenge - 1), s => new[] { 2, 4, 6, 9 }.Contains(s)));
    }

    [Fact]
    public void VierzehnStundenWerdenNichtGequetschtSondernGescrollt()
    {
        var mindest = Blockbildung.MindestZeilenHoehe(8, klein: true);
        Assert.True(mindest >= 13);
        // kleines Fenster (wie im Fehlerbild): 14 Stunden und 3 Pausenzeilen in 180 Pixel
        var h = Blockbildung.ZeilenHoehe(180, 20, 3, 15, 14, mindest, out var scrollen);
        Assert.True(scrollen);
        Assert.Equal(mindest, h);
        var noetig = Blockbildung.MindestHoehe(20, 3, 15, 14, mindest);
        Assert.True(noetig > 180);
        // genug Platz: kein Rollbalken, Zeilen größer als die Mindesthöhe
        var h2 = Blockbildung.ZeilenHoehe(600, 20, 3, 15, 14, mindest, out var scrollen2);
        Assert.False(scrollen2);
        Assert.True(h2 > mindest);
    }

    // ---------------------------------------------------------------- UP-02 Tagestexte

    [Fact]
    public void TagestextHtmlWirdAufErlaubtesSubsetBereinigt()
    {
        var html = "<p onclick=\"x()\">Heute <b>fett</b> <strong>stark</strong> <i>kursiv</i> <u>unter</u> " +
                   "<span style=\"color:#FF0000; font-size:40px\">rot</span><script>alert(1)</script><img src=x onerror=alert(2)>" +
                   "<a href=\"javascript:alert(3)\">Link</a><br/>Zeile 2 &amp; 3 < 4<span style=\"color:expression(alert(1))\">x</span>";
        var s = TagestextHtml.Bereinigen(html);
        Assert.Equal("<p>Heute <b>fett</b> <b>stark</b> <i>kursiv</i> <u>unter</u> <span style=\"color:#ff0000\">rot</span>Link<br>Zeile 2 &amp; 3 &lt; 4<span>x</span></p>", s);
        Assert.DoesNotContain("script", s);
        Assert.DoesNotContain("onerror", s);
        Assert.DoesNotContain("javascript", s);
    }

    [Fact]
    public void TagestextHtmlSchliesstOffeneTagsUndWandeltRgb()
    {
        Assert.Equal("<b><i>a</i></b>b", TagestextHtml.Bereinigen("<b><i>a</b>b</i>"));
        Assert.Equal("<p>x</p><p>y</p>", TagestextHtml.Bereinigen("<p>x<p>y"));
        Assert.Equal("<span style=\"color:#0a141e\">t</span>", TagestextHtml.Bereinigen("<span style='color: rgb(10, 20, 30)'>t</span>"));
        Assert.Equal("a<br>&lt;b&gt;", TagestextHtml.AusText("a\n<b>"));
        Assert.Equal("Heute\nfett", TagestextHtml.ZuText("<p>Heute</p><p><b>fett</b></p>"));
    }

    [Fact]
    public void ExportBefuelltTextHtml()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        d.Tagestexte.Add(new Tagestext { Datum = tag, Text = "Konferenz heute", TextHtml = "<p><b>Konferenz</b> <span style=\"color:#ff0000\">heute</span><script>x</script></p>" });
        d.Tagestexte.Add(new Tagestext { Datum = tag, Text = "Ohne <Format>" });
        var t1 = d.Tagestexte[^2];
        var t2 = d.Tagestexte[^1];
        Assert.Equal("<p><b>Konferenz</b> <span style=\"color:#ff0000\">heute</span></p>", TagestextHtml.FuerExport(t1));
        Assert.Equal("Ohne &lt;Format&gt;", TagestextHtml.FuerExport(t2));
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        Assert.Contains(paket.Tagestexte, x => x.Text == "Konferenz heute" && x.TextHtml == "<p><b>Konferenz</b> <span style=\"color:#ff0000\">heute</span></p>");
        Assert.Contains(paket.Tagestexte, x => x.Text == "Ohne <Format>" && x.TextHtml == "Ohne &lt;Format&gt;");
        var w = new Web.Contracts.WebTagestext("2026-10-15", 1, "Konferenz heute", "alle", new()) { TextHtml = TagestextHtml.FuerExport(t1) };
        var json = System.Text.Json.JsonSerializer.Serialize(w, Web.Contracts.UntPlanWebSchema.Json);
        Assert.Contains("\"textHtml\":", json);
    }

    // ---------------------------------------------------------------- UP-03 unterrichtsfrei

    [Fact]
    public void UnterrichtsfreiOhneBezeichnungZeigtDasDatum()
    {
        var datum = new DateOnly(2021, 9, 3);
        Assert.Equal("3.9.2021", Kalender.FreiText("", datum, 20));
        Assert.Equal("3.9.2021", Kalender.FreiText(null, datum, 8));
        // sehr schmale Spalte: kurzes Datum
        Assert.Equal("3.9.", Kalender.FreiText("  ", datum, 5));
    }

    [Fact]
    public void UnterrichtsfreiMitKurzerUndUeberlangerBezeichnung()
    {
        var datum = new DateOnly(2021, 9, 3);
        Assert.Equal("Herbst", Kalender.FreiText("Herbst", datum, 12));
        var lang = Kalender.FreiText("Pädagogischer Tag des Kollegiums mit Fortbildung", datum, 12);
        Assert.EndsWith("…", lang);
        Assert.True(lang.Length <= 12);
        Assert.StartsWith("Pädagogisch", lang);
        // mit Messfunktion (Pixelbreite): passt nie → mindestens ein Buchstabe und „…“
        Assert.Equal("P…", Kalender.FreiText("Pädagogischer Tag", datum, _ => false));
    }

    [Fact]
    public void FreiBezeichnungAusFerienUndKalendertag()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        Assert.Equal("", Kalender.FreiBezeichnung(d, tag));
        var grund = new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "PT", Langname = "Pädagogischer Tag" };
        d.Abwesenheitsgruende.Add(grund);
        d.Kalender.Add(new Kalendertag { Datum = tag, Unterrichtsfrei = true, GrundId = grund.Id });
        Assert.True(Kalender.GanzerTagFrei(d, tag));
        Assert.Equal("Pädagogischer Tag", Kalender.FreiBezeichnung(d, tag));
        var morgen = tag.AddDays(1);
        d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = "Herbst", Langname = "Herbstferien", Von = morgen, Bis = morgen.AddDays(10) });
        Assert.Equal("Herbstferien", Kalender.FreiBezeichnung(d, morgen));
    }

    // ---------------------------------------------------------------- UP-04 Ferien

    [Fact]
    public void JahresdatenZaehlenFerienFeiertageUndUnterrichtstage()
    {
        var d = Testdaten.Verplant();
        d.Einstellungen.SchuljahrVon = new DateOnly(2021, 8, 18);
        d.Einstellungen.SchuljahrBis = new DateOnly(2022, 6, 24);
        d.Ferien.Clear();
        d.Kalender.Clear();
        var ohne = Kalender.JahresdatenBerechnen(d);
        d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = "3.9", Langname = "3.9.2021", Von = new DateOnly(2021, 9, 3), Bis = new DateOnly(2021, 9, 3) });
        d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = "TdE", Langname = "Tag der Deutschen Einheit", Von = new DateOnly(2021, 10, 4), Bis = new DateOnly(2021, 10, 4), Feiertag = true });
        var j = Kalender.JahresdatenBerechnen(d);
        Assert.Equal(311, j.SchuljahrTage);
        Assert.Equal(1, j.AnzahlFerien);
        Assert.Equal(1, j.AnzahlFeiertage);
        Assert.Equal(ohne.UnterrichtTage - 2, j.UnterrichtTage);
        Assert.Equal(2, j.Ferientage);
    }

    // ---------------------------------------------------------------- Z1/Z2 Verlegung aufheben, Absenzfolgen

    private static (Schuldatei d, DateOnly tag, RegelStunde r, int ziel) Verlegt()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, x.FachId) && x.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(x.Unterricht.Band));
        var tp = Tagesplan.Berechnen(d, tag);
        var frei = Enumerable.Range(1, 8).First(s => s != r.Stunde &&
            Vertretungen.Feldstatus(d, tp, new[] { r.LehrerId!.Value }, r.KlassenIds, s, r.Unterricht.Id, r.RaumId) == Feldstatus.Frei);
        Vertretungen.Verlegen(d, r.Unterricht.Id, null, tag, r.Stunde, tag, frei);
        return (d, tag, r, frei);
    }

    [Fact]
    public void VerlegungAufhebenBringtStundeAnDenUrsprungZurueck()
    {
        var (d, tag, r, ziel) = Verlegt();
        var zielZeile = d.Vertretungen.Single(v => v.Datum == tag && v.Stunde == ziel && v.UnterrichtId == r.Unterricht.Id);
        Assert.True(Verlegungsaufhebung.IstVerlegung(d, zielZeile));
        Assert.Equal(1, Verlegungsaufhebung.Aufheben(d, zielZeile));
        Assert.DoesNotContain(d.Vertretungen, v => v.UnterrichtId == r.Unterricht.Id && v.Datum == tag);
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.Contains(tp.VonKlasse(r.KlassenIds[0]), e => e.Stunde == r.Stunde && e.Unterricht?.Id == r.Unterricht.Id && e.Findetstatt);
        Assert.DoesNotContain(tp.VonKlasse(r.KlassenIds[0]), e => e.Stunde == ziel && e.Unterricht?.Id == r.Unterricht.Id);
    }

    [Fact]
    public void VerlegungUeberDenUrsprungAufheben()
    {
        var (d, tag, r, _) = Verlegt();
        // „Entfall“ auf dem verlegten Ursprung (Entfall „nach“): Verlegung aufheben statt Entfall
        Assert.NotNull(Verlegungsaufhebung.VerlegungAn(d, tag, r.Stunde, r.Unterricht.Id));
        Assert.Equal(1, Verlegungsaufhebung.Aufheben(d, tag, r.Stunde, r.Unterricht.Id));
        Assert.DoesNotContain(d.Vertretungen, v => v.UnterrichtId == r.Unterricht.Id && v.Datum == tag && v.IstEntfall);
        Assert.Equal(0, Verlegungsaufhebung.Aufheben(d, tag, r.Stunde, r.Unterricht.Id));
    }

    [Fact]
    public void AbsenzMitVerlegungImZeitraumWirdErkanntUndAufgehoben()
    {
        var (d, tag, r, ziel) = Verlegt();
        var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag };
        var gespeichert = AbsenzVerwaltung.Speichern(d, new[] { a }, Array.Empty<Abwesenheit>());
        var liste = Verlegungsaufhebung.VerlegungenImZeitraum(d, gespeichert[0]);
        var v = Assert.Single(liste);
        Assert.Equal(ziel, v.Stunde);
        var text = Verlegungsaufhebung.FrageText(d, "verlegte Stunden:", liste, "Soll die Verlegung rückgängig gemacht werden?");
        Assert.Contains($"{ziel}. Std.", text);
        Assert.Contains($"(von ", text);
        Verlegungsaufhebung.AlleAufheben(d, liste.Select(x => x.Id));
        // Stunde ist zurück am Ursprung – dort ist die Lehrkraft abwesend: offene Vertretung statt Entfall
        Assert.DoesNotContain(d.Vertretungen, x => x.Datum == tag && x.Stunde == ziel && x.UnterrichtId == r.Unterricht.Id);
        Assert.Contains(d.Vertretungen, x => x.Datum == tag && x.Stunde == r.Stunde && x.UnterrichtId == r.Unterricht.Id && x.Offen);
        // andere Lehrkraft / anderer Tag: keine Abfrage
        Assert.Empty(Verlegungsaufhebung.VerlegungenImZeitraum(d, new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag.AddDays(30), Bis = tag.AddDays(31) }));
    }

    [Fact]
    public void AbsenzMitSondereinsatzImZeitraumLoeschtSondereinsatz()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Tagesplan.Berechnen(d, tag);
        var lehrer = d.Lehrer.First(l => tp.Eintraege.Any(e => e.LehrerIds.Contains(l.Id)));
        var frei = Enumerable.Range(1, 8).First(s => !tp.Stunde(s).Any(e => e.Findetstatt && e.LehrerIds.Contains(lehrer.Id)));
        var neu = Vertretungen.Sondereinsatz(d, tag, frei, frei, new[] { d.Klassen[0].Id }, new[] { lehrer.Id }, d.Faecher[0].Id, null, "Projekt");
        Assert.NotEmpty(neu);
        var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = lehrer.Id, Von = tag, Bis = tag };
        var gespeichert = AbsenzVerwaltung.Speichern(d, new[] { a }, Array.Empty<Abwesenheit>());
        var sonder = Verlegungsaufhebung.SondereinsaetzeImZeitraum(d, gespeichert[0]);
        Assert.NotEmpty(sonder);
        Assert.Contains("Projekt", Verlegungsaufhebung.Beschreibung(d, sonder[0]));
        Assert.Equal(sonder.Count, Verlegungsaufhebung.SondereinsaetzeLoeschen(d, sonder.Select(v => v.Id)));
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.Art == VertretungsArt.Sondereinsatz && v.VertreterId == lehrer.Id);
        Assert.Empty(Verlegungsaufhebung.SondereinsaetzeImZeitraum(d, gespeichert[0]));
    }
}
