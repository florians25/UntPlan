using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Veranstaltungen mit vielen Elementen (Lehrkräftekonferenz, pädagogischer Tag: alle 120 Lehrkräfte, alle 60 Klassen):
/// Vertretungen, Tagesplan, Aushang, Zählung, Export an UntPlanWeb (kompakt, rückwärtskompatibel), Kurztexte „Alle …“.
/// </summary>
public class GrosseVeranstaltungTests
{
    private static (Schuldatei d, DateOnly tag, Veranstaltung va) Konferenz(int von = 1, int bis = 99)
    {
        var d = GrosseSchule.Erzeugen();
        var tag = GrosseSchule.Tag(d);
        var va = GrosseSchule.AlleVeranstaltung(d, tag, von, bis, "Pädagogischer Tag");
        Vertretungen.Aktualisieren(d, tag);
        return (d, tag, va);
    }

    [Fact]
    public void AlleLehrkraefteUndKlassen_UnterrichtFindetAlsVeranstaltungStatt()
    {
        var (d, tag, va) = Konferenz();
        Assert.Equal(120, d.Lehrer.Count);
        Assert.Equal(60, d.Klassen.Count);
        var am = d.Vertretungen.Where(v => v.Datum == tag).ToList();
        var stunden = d.Zeitraster.StundenProTag;
        // je Stunde und Lehrkraft eine Teilnahmezeile (intern), alle mit Vertreter
        var teilnahme = am.Where(v => v.Art == VertretungsArt.Veranstaltung).ToList();
        Assert.Equal(120 * Enumerable.Range(1, stunden).Count(s => d.Zeitraster.Verfuegbar(Kalender.TagIndex(d, tag), s)), teilnahme.Count);
        Assert.All(teilnahme, v => Assert.NotNull(v.VertreterId));
        // jeder reguläre Unterricht (60 Klassen × 6 Stunden) entfällt als Veranstaltung – keine offenen Vertretungen, keine Doppelzeilen
        var regel = Tagesplan.Regulaer(d, tag);
        Assert.Equal(360, regel.Count);
        var unterricht = am.Where(v => v.UnterrichtId.HasValue).ToList();
        Assert.Equal(regel.Count, unterricht.Count);
        Assert.Equal(regel.Count, unterricht.Select(v => (v.UnterrichtId, v.ZeilenIndex, v.Stunde)).Distinct().Count());
        Assert.All(unterricht, v =>
        {
            Assert.Equal(VertretungsArt.Entfall, v.Art);
            Assert.True(v.InVeranstaltung);
            Assert.Equal(va.Id, v.VeranstaltungId);
            Assert.False(v.Offen);
        });
        // erneute Berechnung ändert nichts (keine zusätzlichen Zeilen)
        var anzahl = d.Vertretungen.Count;
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(anzahl, d.Vertretungen.Count);
    }

    [Fact]
    public void Tagesplan_JedeKlasseHatDieVeranstaltungEinmalJeStunde()
    {
        var (d, tag, va) = Konferenz();
        var tp = Tagesplan.Berechnen(d, tag);
        foreach (var k in d.Klassen.Take(5))
            for (var s = 1; s <= 6; s++)
            {
                var findet = tp.Stunde(s).Where(e => e.Findetstatt && e.KlassenIds.Contains(k.Id)).ToList();
                // nur die Veranstaltung (je Lehrkraft eine Teilnahme) – der reguläre Unterricht findet nicht statt
                Assert.All(findet, e => Assert.Equal(va.Id, e.Vertretung?.VeranstaltungId));
                Assert.Single(tp.Stunde(s), e => e.Status == TagesStatus.Entfall && e.Regel?.KlassenIds.Contains(k.Id) == true);
            }
        // Lehrerplan: jede Lehrkraft ist in der Veranstaltung gebunden (eine Teilnahme je Stunde)
        foreach (var l in d.Lehrer.Take(5))
            Assert.Equal(1, tp.VonLehrer(l.Id).Count(e => e.Stunde == 1 && e.Vertretung?.VeranstaltungId == va.Id));
    }

    [Fact]
    public void Aushang_EineZeileJeStundeMitAlleLehrkraefteUndAlleKlassen()
    {
        var (d, tag, va) = Konferenz(1, 6);
        var zeilen = Vertretungsplan.Zeilen(d, tag);
        var vaZeilen = zeilen.Where(z => z.ArtWert == VertretungsArt.Veranstaltung).ToList();
        Assert.Equal(6, vaZeilen.Count);
        Assert.All(vaZeilen, z =>
        {
            Assert.Equal("Alle Lehrkräfte", z.Vertreter);
            Assert.Equal("Alle Klassen", z.Klassen);
            Assert.Equal(60, z.KlassenIds.Count);
            Assert.True(z.AlleLehrer);
            Assert.True(z.AlleKlassen);
            Assert.Equal(va.Id, z.VeranstaltungId);
            Assert.Equal("Pädagogischer Tag", z.Text);
        });
        // Nummer = kleinste Vtr-Nr. der Stunde
        var erste = d.Vertretungen.Where(v => v.Datum == tag && v.Art == VertretungsArt.Veranstaltung && v.Stunde == 1).Min(v => v.Id);
        Assert.Equal(erste, vaZeilen.Single(z => z.Stunde == 1).Id);
        // Entfälle je Klasse bleiben (Schülerversion), aber keine Hunderte Teilnahmezeilen mehr
        Assert.Equal(360 + 6, zeilen.Count);
    }

    [Fact]
    public void Aushang_AlleAusserBeiEntfernterLehrkraftUndOffenemPlatz()
    {
        var (d, tag, va) = Konferenz(1, 6);
        var krank = d.Lehrer[5];
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = krank.Id, Von = tag, Bis = tag });
        VeranstaltungsLogik.LehrerEntfernen(d, va.Id, krank.Id, new[] { tag }, d.Abwesenheiten[^1].Id);
        Assert.Equal($"Alle Lehrkräfte außer {krank.Kurzname},?", VeranstaltungsLogik.LehrerKurztext(d, va, tag));
        var z = Vertretungsplan.Zeilen(d, tag, true).First(x => x.ArtWert == VertretungsArt.Veranstaltung);
        Assert.Equal($"Alle Lehrkräfte außer {krank.Kurzname},???", z.Vertreter);
        Assert.False(z.AlleLehrer);
        // Aushang ohne „nicht drucken“: offene Plätze erscheinen nicht
        Assert.Equal($"Alle Lehrkräfte außer {krank.Kurzname}", Vertretungsplan.Zeilen(d, tag).First(x => x.ArtWert == VertretungsArt.Veranstaltung).Vertreter);
        // bisheriges Format (vollständige Liste) bleibt erhalten
        Assert.EndsWith(",?", VeranstaltungsLogik.LehrerText(d, va, tag));
    }

    [Fact]
    public void KleineVeranstaltung_UnveraendertJeLehrkraftEineZeile()
    {
        var d = GrosseSchule.Erzeugen();
        var tag = GrosseSchule.Tag(d);
        var va = new Veranstaltung { Id = d.NeueId(), Text = "Exkursion", Von = tag, Bis = tag, StundeVon = 1, StundeBis = 2, KlassenIds = { d.Klassen[0].Id }, LehrerIds = { d.Lehrer[0].Id } };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        var z = Vertretungsplan.Zeilen(d, tag).Where(x => x.ArtWert == VertretungsArt.Veranstaltung).ToList();
        Assert.Equal(2, z.Count);
        Assert.All(z, x => Assert.Equal(d.Lehrer[0].Kurzname, x.Vertreter));
        Assert.All(z, x => Assert.Equal(d.Klassen[0].Kurzname, x.Klassen));
    }

    [Fact]
    public void Export_KompaktUndMitKennzeichen()
    {
        var (d, tag, va) = Konferenz(1, 6);
        var p = UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 1, TageZurueck = 0 });
        var t = p.Tage.Single(x => x.Datum == tag.ToString("yyyy-MM-dd"));
        // je Stunde EINE Veranstaltungsstunde mit allen Lehrkräften (statt 120 Stunden mit je 60 Klassen)
        var vaStunden = t.Stunden.Where(x => x.Veranstaltung == va.Id && x.Status == "zusaetzlich").ToList();
        Assert.Equal(6, vaStunden.Count);
        Assert.All(vaStunden, x =>
        {
            Assert.Equal(120, x.Lehrer.Count);
            Assert.Equal(60, x.Klassen.Count);
            Assert.NotNull(x.Vertretung);
        });
        // entfallener Unterricht verweist auf die Veranstaltung
        var entfall = t.Stunden.Where(x => x.Status == "entfall").ToList();
        Assert.Equal(360, entfall.Count);
        Assert.All(entfall, x => Assert.Equal(va.Id, x.Veranstaltung));
        // Vertretungszeilen: je Stunde eine, Klassen vollständig (Filter in UntPlanWeb), Vertreter „Alle Lehrkräfte“
        var vz = p.Vertretungen.Where(x => x.Veranstaltung == va.Id).ToList();
        Assert.Equal(6, vz.Count);
        Assert.All(vz, x =>
        {
            Assert.Equal("Alle Lehrkräfte", x.Vertreter);
            Assert.Equal(60, x.Klassen.Count);
            Assert.True(x.AlleKlassen);
            Assert.True(x.AlleLehrer);
            Assert.StartsWith("Veranst", x.Art);
        });
        // die Stunde verweist auf eine Vertretungszeile des Pakets (UntPlanWeb erkennt daran die Veranstaltung)
        Assert.All(vaStunden, x => Assert.Contains(vz, z => z.Id == x.Vertretung));
        // Termin einmal mit vollständigen Listen und Kennzeichen
        var termin = Assert.Single(p.Termine, x => x.Art == "veranstaltung");
        Assert.Equal(va.Id, termin.Id);
        Assert.True(termin.AlleKlassen);
        Assert.True(termin.AlleLehrer);
        Assert.Equal(120, termin.Lehrer.Count);
        Assert.Equal(60, termin.Klassen.Count);
    }

    [Fact]
    public void Export_JsonRueckwaertskompatibel()
    {
        var (d, tag, va) = Konferenz(1, 6);
        var json = UntPlanWebExport.Json(UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 1, TageZurueck = 0 }));
        // neue Felder in camelCase, alte bleiben
        Assert.Contains("\"veranstaltung\":" + va.Id, json);
        Assert.Contains("\"alleLehrer\":true", json);
        Assert.Contains("\"alleKlassen\":true", json);
        Assert.Contains("\"vertreter\":\"Alle Lehrkräfte\"", json.Replace("\\u00E4", "ä"));
        // ältere Pakete ohne die neuen Felder lassen sich lesen (Standardwerte)
        var alt = """{"revision":1,"tage":[{"datum":"2026-09-01","unterrichtstag":true,"stunden":[{"stunde":1,"status":"zusaetzlich","lehrer":["A"],"klassen":["5a"]}]}],"vertretungen":[{"id":1,"datum":"2026-09-01","stunde":1,"art":"Veranst.","vertreter":"A"}],"termine":[{"art":"veranstaltung","titel":"X"}]}""";
        var p = UntPlanWebExport.Lesen(alt)!;
        Assert.Null(p.Tage[0].Stunden[0].Veranstaltung);
        Assert.Null(p.Vertretungen[0].Veranstaltung);
        Assert.False(p.Vertretungen[0].AlleKlassen);
        Assert.Null(p.Termine[0].Id);
        Assert.False(p.Termine[0].AlleLehrer);
    }

    [Fact]
    public void Export_PaketDeutlichKleiner()
    {
        var (d, tag, _) = Konferenz();
        var json = UntPlanWebExport.Json(UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 1, TageZurueck = 0 }));
        // vorher: je Lehrkraft und Stunde eine Stunde und eine Vertretungszeile mit allen 60 Klassen (> 2 MB je Tag)
        Assert.True(json.Length < 600_000, $"Paket {json.Length / 1024} KB");
    }

    [Fact]
    public void Zaehlung_TeilnahmeIstKeineMinderleistungAuchFuerZusatzlehrkraft()
    {
        var d = GrosseSchule.Erzeugen();
        var tag = GrosseSchule.Tag(d);
        d.Vertretungseinstellungen.Zaehler.EntfallNurMitGrund = true;
        var va = new Veranstaltung { Id = d.NeueId(), Text = "Konferenz", Von = tag, Bis = tag, StundeVon = 1, StundeBis = 99, KlassenIds = d.Klassen.Select(k => k.Id).ToList(), LehrerIds = d.Lehrer.Take(100).Select(l => l.Id).ToList() };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        var zusatz = d.Lehrer[110];
        VeranstaltungsLogik.LehrerHinzufuegen(d, va.Id, tag, zusatz.Id, false);
        // Lehrkraft der Veranstaltung und zusätzlich eingetragene Lehrkraft: Entfall zählt nicht als Minderleistung (ohne Grund)
        foreach (var l in new[] { d.Lehrer[0], zusatz })
        {
            var z = Zaehlung.Lehrer(d, l, tag, tag);
            Assert.Contains(z.Zeilen, x => x.Code == "-E");
            Assert.DoesNotContain(z.Zeilen, x => x.Code == "-E" && x.Zaehlt);
        }
    }

    [Theory]
    [InlineData("alle", 60)]
    [InlineData("Alle Klassen", 60)]
    [InlineData("ALLE KLASSEN außer 5a, 5b", 58)]
    [InlineData("alle ausser 5a", 59)]
    [InlineData("5a,5b 5c", 3)]
    public void Auswahl_LesenVersteht_Alle(string text, int anzahl)
    {
        var d = GrosseSchule.Erzeugen();
        Assert.Equal(anzahl, VeranstaltungsAuswahl.Lesen(d, ElementTyp.Klasse, text).Count);
    }

    [Fact]
    public void Auswahl_KurztextUndLesenPassenZusammen()
    {
        var d = GrosseSchule.Erzeugen();
        var alle = d.Lehrer.Select(l => l.Id).ToList();
        Assert.Equal("Alle Lehrkräfte", VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, alle));
        var ohne = alle.Where(i => i != d.Lehrer[3].Id && i != d.Lehrer[7].Id).ToList();
        var text = VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, ohne);
        Assert.Equal($"Alle Lehrkräfte außer {d.Lehrer[3].Kurzname}, {d.Lehrer[7].Kurzname}", text);
        Assert.Equal(ohne.OrderBy(x => x), VeranstaltungsAuswahl.Lesen(d, ElementTyp.Lehrer, text).OrderBy(x => x));
        // viele fehlen: Liste
        var halb = alle.Take(60).ToList();
        Assert.Equal(string.Join(",", d.Lehrer.Take(60).Select(l => l.Kurzname)), VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, halb));
        // ignorierte Lehrkräfte zählen nicht zu „alle“
        d.Lehrer[0].Ignorieren = true;
        Assert.Equal("Alle Lehrkräfte", VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, alle.Skip(1)));
        Assert.Throws<FormatException>(() => VeranstaltungsAuswahl.Lesen(d, ElementTyp.Lehrer, "Alle außer XYZ"));
    }

    [Fact]
    public void VertretungsProbe_GleichesErgebnisWieVollstaendigeKopieOhneOriginalZuAendern()
    {
        var (d, tag, va) = Konferenz();
        // Änderung, die die Neuberechnung aufgreifen muss: Veranstaltung nur bis zur 4. Stunde
        va.StundeBis = 4;
        static string Sig(Schuldatei x, DateOnly t) => string.Join(";", x.Vertretungen.Where(v => v.Datum == t).Select(v => $"{v.UnterrichtId}/{v.ZeilenIndex}/{v.Stunde}/{v.Art}/{v.VertreterId}").OrderBy(s => s));
        var vorher = Sig(d, tag);
        var anzahl = d.Vertretungen.Count;
        var probe = Dokument.VertretungsProbe(d, tag, tag);
        var voll = Dokument.Klonen(d);
        Vertretungen.Aktualisieren(probe, tag, tag);
        Vertretungen.Aktualisieren(voll, tag, tag);
        Assert.Equal(Sig(voll, tag), Sig(probe, tag));
        Assert.NotEqual(vorher, Sig(probe, tag));
        // Original unverändert
        Assert.Equal(anzahl, d.Vertretungen.Count);
        Assert.Equal(vorher, Sig(d, tag));
    }
}
