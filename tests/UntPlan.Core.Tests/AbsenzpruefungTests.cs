using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Prüfung von Absenzen vor dem Speichern: Fehler, Hinweise, Überschneidungen und Zusammenfassen.</summary>
public class AbsenzpruefungTests
{
    private static (Schuldatei D, DateOnly Tag, int Lehrer) Lage()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        return (d, tag, d.Lehrer[0].Id);
    }

    private static Abwesenheit Absenz(int lehrer, DateOnly von, DateOnly bis, int sv = 1, int sb = 99) =>
        new() { Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = von, Bis = bis, StundeVon = sv, StundeBis = sb };

    [Fact]
    public void GueltigeAbsenzOhneBefund()
    {
        var (d, tag, l) = Lage();
        Assert.Empty(Absenzpruefung.Pruefen(d, Absenz(l, tag, tag)));
        Assert.Empty(Absenzpruefung.Pruefen(d, Absenz(l, tag, tag, 2, 4)));
    }

    [Fact]
    public void FehlendesUndUnbekanntesElement()
    {
        var (d, tag, _) = Lage();
        var ohne = Absenz(0, tag, tag);
        Assert.Contains(Absenzpruefung.Fehler(d, ohne), b => b.Feld == Absenzpruefung.Element);
        Assert.DoesNotContain(Absenzpruefung.Fehler(d, ohne, elementPflicht: false), b => b.Feld == Absenzpruefung.Element);
        var unbekannt = Absenz(987654, tag, tag);
        Assert.Contains(Absenzpruefung.Fehler(d, unbekannt), b => b.Feld == Absenzpruefung.Element);
        // eine Raum-Nummer ist keine Lehrkraft
        var raum = Absenz(d.Raeume[0].Id, tag, tag);
        if (!d.Lehrer.Any(x => x.Id == raum.ElementId)) Assert.Contains(Absenzpruefung.Fehler(d, raum), b => b.Feld == Absenzpruefung.Element);
    }

    [Fact]
    public void BisVorVon()
    {
        var (d, tag, l) = Lage();
        var f = Absenzpruefung.Fehler(d, Absenz(l, tag, tag.AddDays(-1)));
        Assert.Contains(f, b => b.Feld == Absenzpruefung.Bis && b.Text.Contains("vor dem ersten Tag"));
    }

    [Fact]
    public void StundenAusserhalbUndVertauscht()
    {
        var (d, tag, l) = Lage();
        var letzte = Absenzpruefung.LetzteStunde(d);
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, tag, tag, 0)), b => b.Feld == Absenzpruefung.StundeVon);
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, tag, tag, letzte + 1)), b => b.Feld == Absenzpruefung.StundeVon);
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, tag, tag, 1, letzte + 1)), b => b.Feld == Absenzpruefung.StundeBis);
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, tag, tag, 4, 2)), b => b.Feld == Absenzpruefung.StundeBis && b.Text.Contains("vor ab Stunde"));
        // über mehrere Tage darf bis Stunde kleiner als ab Stunde sein
        var morgen = Kalender.NaechsterUnterrichtstag(d, tag);
        Assert.Empty(Absenzpruefung.Fehler(d, Absenz(l, tag, morgen, 4, 2)));
        // 99 = bis zur letzten Stunde
        Assert.Empty(Absenzpruefung.Fehler(d, Absenz(l, tag, tag, 3, 99)));
    }

    [Fact]
    public void AusserhalbDesSchuljahres()
    {
        var (d, _, l) = Lage();
        var vor = d.Einstellungen.SchuljahrVon.AddDays(-3);
        var nach = d.Einstellungen.SchuljahrBis.AddDays(2);
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, vor, vor)), b => b.Feld == Absenzpruefung.Von && b.Text.Contains("Schuljahr"));
        Assert.Contains(Absenzpruefung.Fehler(d, Absenz(l, d.Einstellungen.SchuljahrBis, nach)), b => b.Feld == Absenzpruefung.Bis && b.Text.Contains("Schuljahr"));
    }

    [Fact]
    public void WochenendeNurHinweis()
    {
        var (d, tag, l) = Lage();
        var samstag = tag;
        while (Kalender.TagIndex(d, samstag) >= 0) samstag = samstag.AddDays(1);
        var befunde = Absenzpruefung.Pruefen(d, Absenz(l, samstag, samstag));
        Assert.DoesNotContain(befunde, b => b.Art == Befundart.Fehler);
        Assert.Contains(befunde, b => b.Art == Befundart.Hinweis && b.Text.Contains("kein Unterricht"));
        // Beginn am Wochenende, danach Unterricht: Hinweis auf den freien ersten Tag
        var montag = Kalender.NaechsterUnterrichtstag(d, samstag);
        Assert.Contains(Absenzpruefung.Pruefen(d, Absenz(l, samstag, montag)), b => b.Art == Befundart.Hinweis && b.Text.Contains("unterrichtsfrei"));
    }

    [Fact]
    public void StundeLesen()
    {
        Assert.True(Absenzpruefung.StundeLesen("", 8, false, out var s, out _));
        Assert.Equal(1, s);
        Assert.True(Absenzpruefung.StundeLesen("", 8, true, out s, out _));
        Assert.Equal(8, s);
        Assert.True(Absenzpruefung.StundeLesen(" 3. ", 8, false, out s, out _));
        Assert.Equal(3, s);
        Assert.False(Absenzpruefung.StundeLesen("9", 8, false, out _, out var f1));
        Assert.Contains("1 bis 8", f1);
        Assert.False(Absenzpruefung.StundeLesen("x", 8, true, out _, out var f2));
        Assert.Contains("keine Stunde", f2);
        Assert.False(Absenzpruefung.StundeLesen("0", 8, true, out _, out _));
    }

    [Fact]
    public void UeberschneidungenDesselbenElements()
    {
        var (d, tag, l) = Lage();
        var anderer = d.Lehrer[1].Id;
        var t2 = Kalender.NaechsterUnterrichtstag(d, tag);
        var t3 = Kalender.NaechsterUnterrichtstag(d, t2);
        var vorhanden = Absenz(l, tag, t2);
        vorhanden.Id = d.NeueId();
        d.Abwesenheiten.Add(vorhanden);
        var halb = Absenz(l, t3, t3, 1, 3);
        halb.Id = d.NeueId();
        d.Abwesenheiten.Add(halb);
        var fremd = Absenz(anderer, tag, t3);
        fremd.Id = d.NeueId();
        d.Abwesenheiten.Add(fremd);

        // neue Absenz am zweiten Tag: überschneidet die vorhandene, nicht die der anderen Lehrkraft
        var neu = Absenz(l, t2, t2);
        Assert.Equal(new[] { vorhanden.Id }, Absenzpruefung.Ueberschneidungen(d, neu).Select(a => a.Id));
        Assert.True(Absenzpruefung.Enthalten(neu, vorhanden));
        // am dritten Tag ab der 4. Stunde: keine Überschneidung mit 1.–3. Stunde
        Assert.Empty(Absenzpruefung.Ueberschneidungen(d, Absenz(l, t3, t3, 4, 99)));
        Assert.Single(Absenzpruefung.Ueberschneidungen(d, Absenz(l, t3, t3, 3, 99)));
        // die Absenz selbst bzw. ausgenommene (gerade ersetzte) Absenzen zählen nicht
        var geaendert = vorhanden.Kopie();
        geaendert.Bis = t3;
        Assert.Equal(new[] { halb.Id }, Absenzpruefung.Ueberschneidungen(d, geaendert).Select(a => a.Id));
        Assert.Empty(Absenzpruefung.Ueberschneidungen(d, geaendert, new[] { halb.Id }));
        // Klassenabsenz mit derselben Nummer ist ein anderes Element
        Assert.Empty(Absenzpruefung.Ueberschneidungen(d, new Abwesenheit { Art = AbwesenheitsArt.Klasse, ElementId = l, Von = tag, Bis = t3 }));
    }

    [Fact]
    public void ZusammenfassenVerlaengert()
    {
        var (d, tag, l) = Lage();
        var t2 = Kalender.NaechsterUnterrichtstag(d, tag);
        var t3 = Kalender.NaechsterUnterrichtstag(d, t2);
        var basis = Absenz(l, t2, t2, 3, 99);
        basis.Id = 42;
        basis.Text = "";
        var weitere = Absenz(l, tag, t2, 2, 5);
        weitere.GrundId = 7;
        weitere.Text = "Fortbildung";
        var spaeter = Absenz(l, t3, t3, 1, 4);
        var z = Absenzpruefung.Zusammenfassen(basis, new[] { weitere, spaeter });
        Assert.Equal(42, z.Id);
        Assert.Equal((tag, 2), (z.Von, z.StundeVon));
        Assert.Equal((t3, 4), (z.Bis, z.StundeBis));
        Assert.Equal(7, z.GrundId);
        Assert.Equal("Fortbildung", z.Text);
        // die Basis selbst bleibt unverändert
        Assert.Equal((t2, 3), (basis.Von, basis.StundeVon));
    }

    [Fact]
    public void ZeitraumText()
    {
        var tag = new DateOnly(2026, 11, 12);
        Assert.Equal("12.11.", Absenzpruefung.Zeitraum(Absenz(1, tag, tag), 8));
        Assert.Equal("12.11. ab 3. Std. bis 5. Std.", Absenzpruefung.Zeitraum(Absenz(1, tag, tag, 3, 5), 8));
        Assert.Equal("12.11. bis 14.11.", Absenzpruefung.Zeitraum(Absenz(1, tag, tag.AddDays(2)), 8));
    }
}
