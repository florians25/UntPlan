using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Raumabsenz und Lehrerabsenz in derselben Stunde: Beide Gründe gelten in einer Zeile (Vertretung offen bzw. Vertreter und Raum offen „???“),
/// gleich in welcher Reihenfolge die Absenzen eingetragen werden; wird eine der beiden gelöscht, bleibt die Wirkung der anderen.
/// </summary>
public class RaumUndLehrerabsenzTests
{
    private sealed record Fall(Schuldatei D, DateOnly Tag, RegelStunde R)
    {
        public int Lehrer => R.LehrerId!.Value;
        public int Raum => R.RaumId!.Value;
        public Vertretung Zeile() => D.Vertretungen.Single(x => x.Datum == Tag && x.Stunde == R.Stunde && x.UnterrichtId == R.Unterricht.Id && x.ZeilenIndex == R.ZeilenIndex);
        public Vertretung? ZeileOderNichts() => D.Vertretungen.SingleOrDefault(x => x.Datum == Tag && x.Stunde == R.Stunde && x.UnterrichtId == R.Unterricht.Id && x.ZeilenIndex == R.ZeilenIndex);
        public TagesEintrag Eintrag() => Tagesplan.Berechnen(D, Tag).Stunde(R.Stunde).Single(e => e.Regel is { } r && r.Unterricht.Id == R.Unterricht.Id && r.ZeilenIndex == R.ZeilenIndex);
    }

    private static Fall Erzeugen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tagIx = Kalender.TagIndex(d, tag);
        var regel = Tagesplan.Regulaer(d, tag);
        // einfache Stunde, die bei abwesender Lehrkraft vertreten wird (keine Kopplung, kein Band, keine Bereitschaft, keine Betreuung)
        var r = regel.First(x => x.LehrerId.HasValue && x.RaumId.HasValue && x.KlassenIds.Count > 0 && x.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(x.Unterricht.Band)
            && !Vertretungen.IstBereitschaftsfach(d, x.FachId) && !x.Unterricht.KeineVertretung && x.Unterricht.BetreuungLehrerId is null
            && !(x.FachId is int f && d.Faecher.First(y => y.Id == f).KeineVertretung)
            && Vertretungen.Regel(d, x.KlassenIds, tagIx, x.Stunde) == VertretungsRegel.Vertreten
            && regel.Count(y => y.Stunde == x.Stunde && y.RaumId == x.RaumId) == 1
            && regel.Count(y => y.Stunde == x.Stunde && y.LehrerId == x.LehrerId) == 1);
        return new Fall(d, tag, r);
    }

    private static Abwesenheit Absenz(Fall f, AbwesenheitsArt art, int element) =>
        AbsenzVerwaltung.Speichern(f.D, new[] { new Abwesenheit { Art = art, ElementId = element, Von = f.Tag, Bis = f.Tag, StundeVon = f.R.Stunde, StundeBis = f.R.Stunde } },
            Array.Empty<Abwesenheit>()).Single();

    private static void BeideGelten(Fall f, Abwesenheit lehrer, Abwesenheit raum)
    {
        var v = f.Zeile();
        Assert.Equal(VertretungsArt.Vertretung, v.Art);
        Assert.True(v.Offen);
        Assert.Equal(lehrer.Id, v.AbwesenheitId);
        Assert.True(v.RaumGesperrt);
        Assert.Equal(raum.Id, v.RaumAbwesenheitId);
        Assert.True(v.RaumOffen);
        Assert.Equal(Vertretungsanzeige.Offen, Vertretungsanzeige.NeuerRaum(f.D, v));
        // Tagesplan: der gesperrte Raum ist nicht belegt
        var e = f.Eintrag();
        Assert.Equal(TagesStatus.Offen, e.Status);
        Assert.DoesNotContain(f.Raum, e.RaumIds);
        // Kalender: als Vertretung und als (offene) Raumvertretung gezählt
        var k = Kalenderkennzahlen.Tag(f.D, f.Tag);
        Assert.True(k.Raumvertretungen >= 1);
        Assert.True(k.RaeumeOffen >= 1);
        // UntPlanWeb: neuer Raum „???“
        var paket = UntPlanWebExport.PaketErstellen(f.D, f.Tag, new WebExportOptionen { TageVoraus = 1, TageZurueck = 0 });
        Assert.Equal("???", paket.Vertretungen.Single(x => x.Id == v.Id).NeuerRaum);
    }

    [Fact]
    public void ErstLehrerDannRaumabsenz()
    {
        var f = Erzeugen();
        var lehrer = Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        Assert.False(f.Zeile().RaumOffen);
        var raum = Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        BeideGelten(f, lehrer, raum);
    }

    [Fact]
    public void ErstRaumDannLehrerabsenz()
    {
        var f = Erzeugen();
        var raum = Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        Assert.Equal(VertretungsArt.Raumvertretung, f.Zeile().Art);
        var lehrer = Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        BeideGelten(f, lehrer, raum);
    }

    [Fact]
    public void VertreterEingetragenDannRaumabsenzRaumWirdOffen()
    {
        var f = Erzeugen();
        Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        var v = f.Zeile();
        var tp = Tagesplan.Berechnen(f.D, f.Tag);
        var vertreter = f.D.Lehrer.First(l => l.Id != f.Lehrer && Vertretungen.LehrerBelegt(f.D, tp, l.Id, f.R.Stunde) is null).Id;
        Vertretungen.VertreterSetzen(f.D, v, vertreter);
        Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        v = f.Zeile();
        Assert.Equal(vertreter, v.VertreterId);
        Assert.True(v.RaumOffen);
        Assert.DoesNotContain(f.Raum, f.Eintrag().RaumIds);
    }

    [Fact]
    public void RaumabsenzGeloeschtLehrervertretungBleibt()
    {
        var f = Erzeugen();
        var lehrer = Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        var raum = Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        AbsenzVerwaltung.Loeschen(f.D, new[] { raum });
        var v = f.Zeile();
        Assert.Equal(VertretungsArt.Vertretung, v.Art);
        Assert.True(v.Offen);
        Assert.Equal(lehrer.Id, v.AbwesenheitId);
        Assert.False(v.RaumGesperrt);
        Assert.Null(v.RaumAbwesenheitId);
        Assert.False(v.RaumOffen);
        Assert.Contains(f.Raum, f.Eintrag().RaumIds);
    }

    [Fact]
    public void LehrerabsenzGeloeschtRaumabsenzBleibt()
    {
        var f = Erzeugen();
        var lehrer = Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        var raum = Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        AbsenzVerwaltung.Loeschen(f.D, new[] { lehrer });
        var v = f.Zeile();
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal(f.Lehrer, v.VertreterId);
        Assert.Equal(raum.Id, v.AbwesenheitId);
        Assert.True(v.RaumOffen);
        Assert.Contains(f.Lehrer, f.Eintrag().LehrerIds);
        // beide gelöscht: keine Zeile mehr
        AbsenzVerwaltung.Loeschen(f.D, new[] { raum });
        Assert.Null(f.ZeileOderNichts());
    }

    [Fact]
    public void BearbeiteteVertretungLehrerabsenzGeloeschtWirdRaumvertretung()
    {
        var f = Erzeugen();
        var lehrer = Absenz(f, AbwesenheitsArt.Lehrer, f.Lehrer);
        var tp = Tagesplan.Berechnen(f.D, f.Tag);
        var vertreter = f.D.Lehrer.First(l => l.Id != f.Lehrer && Vertretungen.LehrerBelegt(f.D, tp, l.Id, f.R.Stunde) is null).Id;
        Vertretungen.VertreterSetzen(f.D, f.Zeile(), vertreter);
        Absenz(f, AbwesenheitsArt.Raum, f.Raum);
        AbsenzVerwaltung.Loeschen(f.D, new[] { lehrer });
        var v = f.Zeile();
        Assert.Equal(VertretungsArt.Raumvertretung, v.Art);
        Assert.Equal(f.Lehrer, v.VertreterId);
        Assert.True(v.RaumOffen);
    }
}
