using System.Drawing;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Sprint 25: Fensterlagen klemmen (Desktop-Anordnung) und „Entfall rückgängig“ im tagesaktuellen Stundenplan.</summary>
public class FensterlageTests
{
    private static readonly Rectangle Bildschirm = new(0, 0, 1920, 1040); // Arbeitsbereich ohne Taskleiste

    [Fact]
    public void GespeicherteLageMitNegativemYWirdUnterDenOberenRandGeholt()
    {
        var r = Fensterlage.Hauptfenster(new Rectangle(100, -60, 1200, 800), new[] { Bildschirm }, new Size(1000, 640));
        Assert.Equal(new Rectangle(100, 0, 1200, 800), r);
    }

    [Fact]
    public void TaskleisteObenWirdBeachtet()
    {
        var oben = new Rectangle(0, 48, 1920, 1032);
        var r = Fensterlage.Hauptfenster(new Rectangle(50, 10, 1200, 800), new[] { oben }, new Size(1000, 640));
        Assert.Equal(48, r.Y);
    }

    [Fact]
    public void ZuGrossesFensterWirdAufDenArbeitsbereichVerkleinert()
    {
        // z. B. auf einem 4K-Bildschirm bzw. bei 100 % gespeichert, jetzt 1366×728 bzw. 150 % Skalierung
        var klein = new Rectangle(0, 0, 1366, 728);
        var r = Fensterlage.Hauptfenster(new Rectangle(300, 200, 2400, 1300), new[] { klein }, new Size(1000, 640));
        Assert.Equal(klein, r);
        Assert.True(klein.Contains(r));
    }

    [Fact]
    public void MindestgroesseBleibtErhalten()
    {
        var r = Fensterlage.Hauptfenster(new Rectangle(10, 10, 300, 200), new[] { Bildschirm }, new Size(1000, 640));
        Assert.Equal(new Size(1000, 640), r.Size);
    }

    [Fact]
    public void FehlenderMonitorFuehrtAufDenNaechstenBildschirm()
    {
        // gespeichert auf einem zweiten Monitor rechts, der nicht mehr angeschlossen ist
        var r = Fensterlage.Hauptfenster(new Rectangle(2500, 100, 1200, 800), new[] { Bildschirm }, new Size(1000, 640));
        Assert.True(Bildschirm.Contains(r));
        Assert.Equal(1920 - 1200, r.X);
        Assert.Equal(100, r.Y);
    }

    [Fact]
    public void ZweiterMonitorLinksMitNegativenKoordinatenBleibtErhalten()
    {
        var links = new Rectangle(-1680, 0, 1680, 1010);
        var r = Fensterlage.Hauptfenster(new Rectangle(-1500, 30, 1200, 800), new[] { Bildschirm, links }, new Size(1000, 640));
        Assert.Equal(new Rectangle(-1500, 30, 1200, 800), r);
        // über dem oberen Rand des linken Monitors
        var r2 = Fensterlage.Hauptfenster(new Rectangle(-1500, -40, 1200, 800), new[] { Bildschirm, links }, new Size(1000, 640));
        Assert.Equal(0, r2.Y);
        Assert.True(links.Contains(r2));
    }

    [Fact]
    public void BeimVerschiebenBleibtDerProgrammkopfSichtbar()
    {
        var fenster = new Rectangle(200, -35, 1200, 800);
        var r = Fensterlage.TitelSichtbar(fenster, new[] { Bildschirm }, 23);
        Assert.Equal(0, r.Y);
        Assert.Equal(fenster.Size, r.Size);
        // nach unten aus dem Bild: der Kopf bleibt greifbar
        var unten = Fensterlage.TitelSichtbar(new Rectangle(200, 1100, 1200, 800), new[] { Bildschirm }, 23);
        Assert.Equal(1040 - 23, unten.Y);
        // seitlich darf das Fenster teilweise hinausragen, aber nicht ganz verschwinden
        var rechts = Fensterlage.TitelSichtbar(new Rectangle(1700, 100, 1200, 800), new[] { Bildschirm }, 23);
        Assert.Equal(1700, rechts.X);
        var weg = Fensterlage.TitelSichtbar(new Rectangle(5000, 100, 1200, 800), new[] { Bildschirm }, 23);
        Assert.Equal(1920 - 100, weg.X);
    }

    [Fact]
    public void WechselAufEinenDarueberLiegendenMonitorIstErlaubt()
    {
        var darueber = new Rectangle(0, -1080, 1920, 1080);
        var fenster = new Rectangle(100, -900, 1200, 800);
        Assert.Equal(fenster, Fensterlage.TitelSichtbar(fenster, new[] { Bildschirm, darueber }, 23));
        // über dessen oberen Rand hinaus nicht
        Assert.Equal(-1080, Fensterlage.TitelSichtbar(new Rectangle(100, -1200, 1200, 800), new[] { Bildschirm, darueber }, 23).Y);
    }

    [Fact]
    public void MonitoreUnterschiedlicherHoeheKlemmenAmRichtigenRand()
    {
        // rechter Monitor beginnt 200 px tiefer: Kopf über dessen Oberkante im leeren Bereich → auf seine Oberkante
        var rechts = new Rectangle(1920, 200, 1280, 984);
        var r = Fensterlage.TitelSichtbar(new Rectangle(2200, 100, 800, 600), new[] { Bildschirm, rechts }, 23);
        Assert.Equal(200, r.Y);
    }

    [Fact]
    public void MdiFensterRutschtNieUnterDasMenueband()
    {
        var mdi = new Size(1200, 700);
        Assert.Equal(new Rectangle(30, 0, 500, 400), Fensterlage.MdiFenster(new Rectangle(30, -80, 500, 400), mdi, false));
        Assert.Equal(new Rectangle(0, 0, 500, 400), Fensterlage.MdiFenster(new Rectangle(-50, -10, 500, 400), mdi, false));
        // unten/rechts hinaus: vollständig zurück in den Bereich
        Assert.Equal(new Rectangle(700, 300, 500, 400), Fensterlage.MdiFenster(new Rectangle(900, 650, 500, 400), mdi, false));
    }

    [Fact]
    public void MdiFensterBeimVerschiebenBehaeltGroesseBeimAnordnenPasstEs()
    {
        var mdi = new Size(800, 500);
        var gross = new Rectangle(100, -20, 1000, 700);
        // nur verschieben: Größe bleibt, Fenster steht oben links (Titelleiste sichtbar)
        Assert.Equal(new Rectangle(0, 0, 1000, 700), Fensterlage.MdiFenster(gross, mdi, false));
        // Anordnung/Größenänderung: auf den Bereich verkleinert
        Assert.Equal(new Rectangle(0, 0, 800, 500), Fensterlage.MdiFenster(gross, mdi, true));
    }

    [Fact]
    public void GespeicherteAnordnungEinesGroesserenBildschirmsPasstInDenBereich()
    {
        // auf 1920×1200 gespeichert: rechts unten ein Lehrerplan; jetzt nur 1280×600 Arbeitsbereich
        var alt = new[] { new Rectangle(0, 0, 700, 450), new Rectangle(700, 0, 1000, 600), new Rectangle(1300, 600, 600, 500), new Rectangle(10, -40, 300, 200) };
        var bereich = new Size(1280, 600);
        var neu = Fensterlage.Anordnung(alt, bereich);
        Assert.All(neu, r => Assert.True(new Rectangle(Point.Empty, bereich).Contains(r), r.ToString()));
        Assert.All(neu, r => Assert.True(r.Y >= 0));
        Assert.Equal(alt[0], neu[0]);
    }

    [Fact]
    public void UngueltigerBereichLaesstDieLageUnveraendert()
    {
        // minimiertes Hauptfenster bzw. Arbeitsbereich noch nicht angeordnet: nichts zerstören
        var r = new Rectangle(100, 50, 600, 400);
        Assert.Equal(r, Fensterlage.MdiFenster(r, new Size(0, 0), true));
        Assert.Equal(r, Fensterlage.MdiFenster(r, new Size(100, 30), true));
        Assert.False(Fensterlage.BereichGueltig(Rectangle.Empty));
    }
}

public class EntfallAufhebungTests
{
    private static (Schuldatei d, DateOnly tag) Daten()
    {
        var d = Testdaten.Verplant();
        return (d, Testdaten.Unterrichtstag(d));
    }

    private static RegelStunde NormaleStunde(Schuldatei d, DateOnly tag) =>
        Tagesplan.Regulaer(d, tag).First(r => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(r.Unterricht.Band));

    private static TagesEintrag Eintrag(Schuldatei d, DateOnly tag, RegelStunde r) =>
        Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(e => e.Unterricht?.Id == r.Unterricht.Id && e.Regel?.ZeilenIndex == r.ZeilenIndex);

    private static void LehrerAbwesend(Schuldatei d, int lehrer, DateOnly tag)
    {
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
    }

    [Fact]
    public void ManuellerEntfallBeiAnwesenderLehrkraftWirdWiederRegulaererUnterricht()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        Vertretungen.Entfallen(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        Assert.Equal(TagesStatus.Entfall, Eintrag(d, tag, r).Status);
        var v = Assert.Single(Entfallaufhebung.Zeilen(d, r.Unterricht.Id, null, tag, r.Stunde));
        Assert.Equal(Entfallaufhebung.Wirkung.Regulaer, Entfallaufhebung.WirkungVon(d, v));
        Assert.Equal(1, Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test"));
        var e = Eintrag(d, tag, r);
        Assert.Equal(TagesStatus.Regulaer, e.Status);
        Assert.Null(e.Vertretung);
        Assert.Contains(r.LehrerId!.Value, e.LehrerIds);
        Assert.DoesNotContain(d.Vertretungen, x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == r.Stunde);
    }

    [Fact]
    public void EntfallBeiAbwesenderLehrkraftWirdOffeneVertretung()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        LehrerAbwesend(d, r.LehrerId!.Value, tag);
        var offen = d.Vertretungen.Single(x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == r.Stunde);
        Assert.True(offen.Offen);
        // im Stundenplan entfallen lassen (Absenz-Zeile wird Entfall) und wieder aufheben → „???“
        Vertretungen.Entfallen(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        Assert.Equal(TagesStatus.Entfall, Eintrag(d, tag, r).Status);
        Assert.Equal(Entfallaufhebung.Wirkung.Offen, Entfallaufhebung.WirkungVon(d, offen));
        Assert.Equal(1, Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test"));
        Assert.True(offen.Offen);
        Assert.Equal(TagesStatus.Offen, Eintrag(d, tag, r).Status);
        // bleibt auch nach dem Neuberechnen offen
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(TagesStatus.Offen, Eintrag(d, tag, r).Status);
    }

    [Fact]
    public void ManuellerEntfallVorDerAbsenzWirdBeimAufhebenZurOffenenVertretungDerAbsenz()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        Vertretungen.Entfallen(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        // danach wird die Lehrkraft krank gemeldet: der manuelle Entfall hat Vorrang vor der Absenz
        LehrerAbwesend(d, r.LehrerId!.Value, tag);
        var v = Assert.Single(Entfallaufhebung.Zeilen(d, r.Unterricht.Id, null, tag, r.Stunde));
        Assert.Equal(VertretungsQuelle.Manuell, v.Quelle);
        Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        var neu = Assert.Single(d.Vertretungen, x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == r.Stunde);
        Assert.True(neu.Offen);
        Assert.Equal(VertretungsQuelle.Absenz, neu.Quelle);
        Assert.NotNull(neu.AbwesenheitId);
        Assert.Equal(TagesStatus.Offen, Eintrag(d, tag, r).Status);
    }

    [Fact]
    public void EntfallAusAbsenzOhneVertretungWirdOffen()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        r.Unterricht.KeineVertretung = true;
        LehrerAbwesend(d, r.LehrerId!.Value, tag);
        Assert.Equal(TagesStatus.Entfall, Eintrag(d, tag, r).Status);
        Assert.True(Entfallaufhebung.Moeglich(d, r.Unterricht.Id, null, tag, r.Stunde));
        Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        Assert.Equal(TagesStatus.Offen, Eintrag(d, tag, r).Status);
    }

    [Fact]
    public void DoppelstundeWirdAlsBlockErkanntUndAufgehoben()
    {
        var (d, tag) = Daten();
        // Unterricht mit zwei aufeinanderfolgenden Stunden an diesem Tag
        var tage = Enumerable.Range(0, 10).Select(i => Kalender.NaechsterUnterrichtstag(d, tag.AddDays(i))).Distinct();
        (DateOnly Tag, RegelStunde R)? fund = null;
        foreach (var t in tage)
        {
            var regel = Tagesplan.Regulaer(d, t).Where(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId)).ToList();
            var r0 = regel.FirstOrDefault(x => regel.Any(y => y.Unterricht.Id == x.Unterricht.Id && y.Stunde == x.Stunde + 1) && !regel.Any(y => y.Unterricht.Id == x.Unterricht.Id && y.Stunde == x.Stunde - 1));
            if (r0 != null) { fund = (t, r0); break; }
        }
        Assert.NotNull(fund);
        var (tg, r) = fund!.Value;
        var uid = r.Unterricht.Id;
        Vertretungen.Entfallen(d, uid, null, tg, r.Stunde, "test");
        Vertretungen.Entfallen(d, uid, null, tg, r.Stunde + 1, "test");
        Assert.Equal(new[] { r.Stunde, r.Stunde + 1 }, Entfallaufhebung.Block(d, uid, null, tg, r.Stunde + 1));
        Assert.DoesNotContain(r.Stunde + 2, Entfallaufhebung.Block(d, uid, null, tg, r.Stunde));
        Assert.Equal(2, Entfallaufhebung.BlockAufheben(d, uid, null, tg, r.Stunde, "test"));
        Assert.Empty(Entfallaufhebung.Block(d, uid, null, tg, r.Stunde));
        Assert.All(new[] { r.Stunde, r.Stunde + 1 }, s =>
            Assert.Equal(TagesStatus.Regulaer, Tagesplan.Berechnen(d, tg).Stunde(s).First(e => e.Unterricht?.Id == uid).Status));
    }

    [Fact]
    public void KopplungKlassenplanAlleZeilenLehrerplanNurEigeneZeile()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        var u = r.Unterricht;
        // zweite Kopplungszeile mit einer anderen Lehrkraft
        var andere = d.Lehrer.First(l => l.Id != r.LehrerId);
        u.Zeilen.Add(new Kopplungszeile { LehrerId = andere.Id, FachId = r.FachId, KlassenIds = r.KlassenIds.ToList() });
        // Lehrkraft der ersten Zeile abwesend, die zweite nicht
        LehrerAbwesend(d, r.LehrerId!.Value, tag);
        Vertretungen.Entfallen(d, u.Id, null, tag, r.Stunde, "test");
        Assert.Equal(2, Entfallaufhebung.Zeilen(d, u.Id, null, tag, r.Stunde).Count);
        // Lehrerplan der anwesenden Lehrkraft: nur ihre Zeile
        Assert.Single(Entfallaufhebung.Zeilen(d, u.Id, 1, tag, r.Stunde));
        Assert.Equal(1, Entfallaufhebung.Aufheben(d, u.Id, 1, tag, r.Stunde, "test"));
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.Equal(TagesStatus.Regulaer, tp.Stunde(r.Stunde).Single(e => e.Unterricht?.Id == u.Id && e.Regel?.ZeilenIndex == 1).Status);
        Assert.Equal(TagesStatus.Entfall, tp.Stunde(r.Stunde).Single(e => e.Unterricht?.Id == u.Id && e.Regel?.ZeilenIndex == 0).Status);
        // Klassenplan: übrige Zeile (abwesende Lehrkraft) wird offen
        Assert.Equal(1, Entfallaufhebung.Aufheben(d, u.Id, null, tag, r.Stunde, "test"));
        tp = Tagesplan.Berechnen(d, tag);
        Assert.Equal(TagesStatus.Offen, tp.Stunde(r.Stunde).Single(e => e.Unterricht?.Id == u.Id && e.Regel?.ZeilenIndex == 0).Status);
        Assert.Equal(TagesStatus.Regulaer, tp.Stunde(r.Stunde).Single(e => e.Unterricht?.Id == u.Id && e.Regel?.ZeilenIndex == 1).Status);
    }

    [Fact]
    public void VerlegteStundeBleibtUnberuehrt()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        var tp = Tagesplan.Berechnen(d, tag);
        var frei = Enumerable.Range(1, 8).First(s => s != r.Stunde &&
            Vertretungen.Feldstatus(d, tp, new[] { r.LehrerId!.Value }, r.KlassenIds, s, r.Unterricht.Id, r.RaumId) == Planung.Feldstatus.Frei);
        Vertretungen.Verlegen(d, r.Unterricht.Id, null, tag, r.Stunde, tag, frei);
        Assert.False(Entfallaufhebung.Moeglich(d, r.Unterricht.Id, null, tag, r.Stunde));
        Assert.Equal(0, Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test"));
    }

    [Fact]
    public void EntfallWegenKlausurDerKlasseHaeltDieLehrkraftWiederSelbst()
    {
        var (d, tag) = Daten();
        var r = NormaleStunde(d, tag);
        var andere = d.Lehrer.First(l => l.Id != r.LehrerId);
        d.Klausuren.Add(new Klausur { Id = d.NeueId(), Name = "Arbeit", Datum = tag, StundeVon = r.Stunde, StundeBis = r.Stunde, KlassenIds = r.KlassenIds.ToList(), LehrerIds = new() { andere.Id }, UnterrichtId = r.Unterricht.Id });
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(TagesStatus.Entfall, Eintrag(d, tag, r).Status);
        Entfallaufhebung.Aufheben(d, r.Unterricht.Id, null, tag, r.Stunde, "test");
        Vertretungen.Aktualisieren(d, tag);
        var e = Eintrag(d, tag, r);
        Assert.True(e.Findetstatt);
        Assert.Equal(new[] { r.LehrerId!.Value }, e.LehrerIds);
    }
}
