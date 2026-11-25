using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Vertretungs- und Änderungsstunden sind Einzelstunden: Jede Stunde einer Doppelstunde bzw. eines Blocks, die vertreten wird, entfällt,
/// einen anderen Raum bekommt oder verlegt wird, hat eine eigene Zeile mit eigener Stundennummer (Vertretungsliste, Kalender-Kennzahlen)
/// und im tagesaktuellen Plan ein eigenes Feld. Jede offene Stunde lässt sich einzeln (auch mit verschiedenen Lehrkräften) besetzen.
/// Ein verschobener Block wandert weiterhin als Ganzes, die Zeilen entstehen aber je Stunde; Rückfrage und Aufheben gelten für den Block.
/// </summary>
public class EinzelstundenTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Einzel", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Fach> F = new();
        public readonly List<Raum> R = new();
        public readonly DateOnly Montag;

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear();
            for (var i = 0; i < 4; i++)
            {
                L.Add(new Lehrer { Id = D.NeueId(), Kurzname = "L" + i, Wochensoll = 25 });
                K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"{5 + i}a" });
                F.Add(new Fach { Id = D.NeueId(), Kurzname = "F" + i });
                R.Add(new Raum { Id = D.NeueId(), Kurzname = "R" + i });
            }
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Faecher.AddRange(F); D.Raeume.AddRange(R);
            Montag = Kalender.Montag(Kalender.NaechsterUnterrichtstag(D, D.Einstellungen.SchuljahrVon.AddDays(21)));
            while (!Kalender.IstUnterrichtstag(D, Montag)) Montag = Montag.AddDays(7);
        }

        /// <summary>Block: Lehrer l / Klasse k / Fach l / Raum k am Montag in den Stunden von..von+laenge-1.</summary>
        public Unterricht U(int l, int k, int von, int laenge)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = laenge };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[l].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            for (var s = von; s < von + laenge; s++) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = s, Raeume = new List<int?> { R[k].Id } });
            return u;
        }

        public Abwesenheit Absenz(int lehrer, int von, int bis) =>
            AbsenzVerwaltung.Speichern(D, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = L[lehrer].Id, Von = Montag, Bis = Montag, StundeVon = von, StundeBis = bis } },
                Array.Empty<Abwesenheit>()).Single();

        public List<Vertretung> Zeilen(Unterricht u) => D.Vertretungen.Where(v => v.UnterrichtId == u.Id).OrderBy(v => v.Stunde).ToList();

        /// <summary>Einträge des Unterrichts im tagesaktuellen Plan am Montag.</summary>
        public List<TagesEintrag> Eintraege(Unterricht u) =>
            Tagesplan.Berechnen(D, Montag).Eintraege.Where(e => (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == u.Id).OrderBy(e => e.Stunde).ToList();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void AbsenzUeberBlockErgibtJeStundeEineOffeneZeile(int n)
    {
        var s = new Schule();
        var u = s.U(0, 0, 1, n);
        s.Absenz(0, 1, n);
        var zeilen = s.Zeilen(u);
        Assert.Equal(Enumerable.Range(1, n), zeilen.Select(v => v.Stunde));
        Assert.All(zeilen, v => Assert.True(v.Offen));
        Assert.Equal(n, zeilen.Select(v => v.Id).Distinct().Count());
        // Vertretungsliste bzw. Kalender: eine Zeile je Stunde
        var liste = Kalenderkennzahlen.Listenzeilen(s.D, s.Montag);
        Assert.Equal(Enumerable.Range(1, n), liste.Where(v => v.UnterrichtId == u.Id).Select(v => v.Stunde).OrderBy(x => x));
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((n, n), (k.Vertretungen, k.Offen));
        // tagesaktueller Plan: jede offene Stunde ist ein eigenes Feld
        Assert.All(s.Eintraege(u), e => Assert.True(e.AlsEinzelstunde));
    }

    [Fact]
    public void JedeStundeDesBlocksBekommtEineEigeneVertretung()
    {
        var s = new Schule();
        var u = s.U(0, 0, 1, 2);
        s.Absenz(0, 1, 2);
        var zeilen = s.Zeilen(u);
        Vertretungen.VertreterSetzen(s.D, zeilen[0], s.L[1].Id);
        // die zweite Stunde bleibt offen, bis sie eigens besetzt wird
        Assert.True(zeilen[1].Offen);
        Vertretungen.VertreterSetzen(s.D, zeilen[1], s.L[2].Id);
        Assert.Equal(new int?[] { s.L[1].Id, s.L[2].Id }, s.Zeilen(u).Select(v => v.VertreterId));
        Assert.All(s.Zeilen(u), v => Assert.False(v.Offen));
        var liste = Kalenderkennzahlen.Listenzeilen(s.D, s.Montag).Where(v => v.UnterrichtId == u.Id).ToList();
        Assert.Equal(2, liste.Count);
        var plan = s.Eintraege(u);
        Assert.Equal(new[] { s.L[1].Id, s.L[2].Id }, plan.Select(e => e.LehrerIds.Single()));
        Assert.All(plan, e => Assert.True(e.AlsEinzelstunde));
    }

    [Fact]
    public void RaumaenderungImBlockErgibtJeStundeEineZeile()
    {
        var s = new Schule();
        var u = s.U(0, 0, 3, 2);
        var stunden = Raumvertretungen.Block(s.D, s.Montag, 3, u.Id, 0);
        Assert.Equal(new[] { 3, 4 }, stunden);
        Raumvertretungen.Zuordnen(s.D, u.Id, 0, s.Montag, stunden, s.R[3].Id, false, RaumKollisionsLoesung.MitKollision);
        var zeilen = s.Zeilen(u);
        Assert.Equal(new[] { 3, 4 }, zeilen.Select(v => v.Stunde));
        Assert.All(zeilen, v => Assert.Equal(s.R[3].Id, v.VertretungsraumId));
        Assert.Equal(2, Kalenderkennzahlen.Listenzeilen(s.D, s.Montag).Count(v => v.UnterrichtId == u.Id));
        Assert.All(s.Eintraege(u), e => Assert.True(e.AlsEinzelstunde));
    }

    [Fact]
    public void VerlegterBlockWandertGanzMitZeilenJeStunde()
    {
        var s = new Schule();
        var u = s.U(0, 0, 1, 2);
        var a = Tagesverlegung.Analysieren(s.D, s.Montag, 1, u.Id, null, 2, s.Montag, 5);
        Assert.Equal(2, a.Schritte.Count);
        Tagesverlegung.Ausfuehren(s.D, a, VerlegeModus.Verlegen);
        var zeilen = s.Zeilen(u);
        Assert.Equal(new[] { 1, 2, 5, 6 }, zeilen.Select(v => v.Stunde));
        Assert.Single(zeilen.Select(v => v.BlockId).Distinct());
        Assert.NotNull(zeilen[0].BlockId);
        // jede Zielstunde mit eigener Herkunft
        Assert.Equal(new int?[] { 1, 2 }, zeilen.Where(v => v.Stunde >= 5).Select(v => v.VonStunde));
        Assert.Equal(4, Kalenderkennzahlen.Listenzeilen(s.D, s.Montag).Count(v => v.UnterrichtId == u.Id));
        // im Plan je Stunde ein Feld, der Block bleibt für das Ziehen zusammen
        var tp = Tagesplan.Berechnen(s.D, s.Montag);
        Assert.All(s.Eintraege(u), e => Assert.True(e.AlsEinzelstunde));
        Assert.Equal((5, 2), Tagesverlegung.Blockumfang(tp, 5, 1, u.Id, null));
        // Rückfrage und Aufheben gelten für den ganzen Block
        Assert.Equal(2, Verlegungsaufhebung.BlockZeilen(s.D, zeilen[0]).Count);
    }

    [Fact]
    public void RegulaererBlockUndVeranstaltungBleibenZusammengefasst()
    {
        var s = new Schule();
        var u = s.U(0, 0, 1, 2);
        Assert.All(s.Eintraege(u), e => Assert.False(e.AlsEinzelstunde));
        Assert.False(new TagesEintrag { Status = TagesStatus.Frei, Vertretung = new Vertretung { Art = VertretungsArt.Veranstaltung } }.AlsEinzelstunde);
        Assert.True(new TagesEintrag { Status = TagesStatus.Entfall, Vertretung = new Vertretung { Art = VertretungsArt.Entfall } }.AlsEinzelstunde);
    }
}
