using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Planungsdialog: Stunden-Merker, Elementzeilen, freie Räume, Rangziffern, Bewertung zur aktuellen Zeit, Tauschketten, Sondereinsatz verlegen.</summary>
public class PlanungsdialogTests
{
    private sealed class Schule
    {
        public Schuldatei D = Demo.NeueDatei("Planungsdialog", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Fach> F = new();
        public readonly List<Raum> R = new();

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear(); D.Vertretungen.Clear();
            D.Zeitraster.TageProWoche = 5;
            D.Zeitraster.StundenProTag = 6;
            for (var i = 0; i < 5; i++)
            {
                L.Add(new Lehrer { Id = D.NeueId(), Kurzname = "L" + i, Wochensoll = 25 });
                K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"{5 + i}a" });
                F.Add(new Fach { Id = D.NeueId(), Kurzname = "F" + i });
                R.Add(new Raum { Id = D.NeueId(), Kurzname = "R" + i });
            }
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Faecher.AddRange(F); D.Raeume.AddRange(R);
        }

        public Unterricht U(IEnumerable<(int L, int K)> zeilen, int fach, params (int Tag, int Stunde)[] stunden)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = stunden.Length };
            foreach (var (l, k) in zeilen) u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[fach].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            foreach (var (t, s) in stunden) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = t, Stunde = s, Raeume = u.Zeilen.Select(_ => (int?)null).ToList() });
            return u;
        }

        public Unterricht U(int l, int k, int fach, params (int Tag, int Stunde)[] stunden) => U(new[] { (l, k) }, fach, stunden);
        public List<Planstunde> Von(Unterricht u) => D.Stundenplan.Where(p => p.UnterrichtId == u.Id).OrderBy(p => p.Tag).ThenBy(p => p.Stunde).ToList();
        public DatenIndex Ix => new(D);
    }

    [Fact]
    public void MerkerDerSummenzeilen()
    {
        var s = new Schule();
        var aktiv = s.U(new[] { (0, 0), (1, 1) }, 0, (0, 1));            // Kopplung L0/5a + L1/6a am Mo-1
        s.U(0, 2, 1, (0, 2));                                           // L0 einzeln Mo-2
        s.U(new[] { (1, 3), (2, 4) }, 2, (0, 3));                       // L1 in Kopplung Mo-3
        s.U(0, 2, 1, (0, 4)); s.U(1, 3, 1, (0, 4));                     // L0 und L1 Mo-4 → &
        var fix = s.U(0, 2, 3, (1, 1)); s.Von(fix)[0].Fixiert = true;  // fixiert Di-1
        s.L[0].Zeitwuensche.Set(1, 2, -3);
        var ix = s.Ix;
        var lehrer = aktiv.LehrerIds.ToList();
        Assert.Equal("O", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 0, 1));
        Assert.Equal("x", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 0, 2));
        Assert.Equal("X", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 0, 3));
        Assert.Equal("&", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 0, 4));
        Assert.Equal("*x*", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 1, 1));
        Assert.Equal("-3", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 1, 2));
        Assert.Equal("", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Lehrer, lehrer, 2, 2));
        // Klassen-Summenzeile: 5a/6a sind Mo-2 nicht belegt (L0 unterrichtet 7a)
        Assert.Equal("", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Klasse, aktiv.KlassenIds.ToList(), 0, 2));
        s.U(3, 0, 4, (0, 5));
        ix = s.Ix;
        Assert.Equal("x", Planungsbereich.Merker(s.D, ix, aktiv, ElementTyp.Klasse, aktiv.KlassenIds.ToList(), 0, 5));
    }

    [Fact]
    public void ElementzeileZeigtFachBzwKlasseUndZeitwunsch()
    {
        var s = new Schule();
        var aktiv = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 2, (0, 2));
        s.Von(b)[0].Fixiert = true;
        s.K[0].Zeitwuensche.Set(0, 3, -2);
        var ix = s.Ix;
        var z = Planungsbereich.Zelle(s.D, ix, aktiv, ElementTyp.Klasse, s.K[0].Id, 0, 1, StundenAnzeige.Fach);
        Assert.True(z.Eigene);
        Assert.Equal("F0", z.Text);
        z = Planungsbereich.Zelle(s.D, ix, aktiv, ElementTyp.Klasse, s.K[0].Id, 0, 2, StundenAnzeige.Fach);
        Assert.Equal("*F2", z.Text);
        Assert.False(z.Eigene);
        z = Planungsbereich.Zelle(s.D, ix, aktiv, ElementTyp.Lehrer, s.L[1].Id, 0, 2, StundenAnzeige.Klasse);
        Assert.Equal("*5a", z.Text);
        z = Planungsbereich.Zelle(s.D, ix, aktiv, ElementTyp.Klasse, s.K[0].Id, 0, 3, StundenAnzeige.Fach);
        Assert.True(z.Frei);
        Assert.Equal("-2", z.Text);
    }

    [Fact]
    public void RaumsummeZeigtFreieVonMoeglichenRaeumen()
    {
        var s = new Schule();
        var g = new Raumgruppe { Id = s.D.NeueId(), Kurzname = "RG", RaumIds = { s.R[0].Id, s.R[1].Id, s.R[2].Id } };
        s.D.Raumgruppen.Add(g);
        var aktiv = s.U(0, 0, 0);
        aktiv.Zeilen[0].FachraumgruppeId = g.Id;
        aktiv.Wochenstunden = 1;
        var b = s.U(1, 1, 1, (0, 1));
        s.Von(b)[0].Raeume = new List<int?> { s.R[1].Id };
        var ix = s.Ix;
        Assert.Equal((2, 3), Planungsbereich.RaeumeFrei(s.D, ix, aktiv, 0, 1));
        Assert.Equal((3, 3), Planungsbereich.RaeumeFrei(s.D, ix, aktiv, 0, 2));
    }

    [Fact]
    public void RangzifferBevorzugtLueckenlosePlaetze()
    {
        var s = new Schule();
        // Klasse 5a hat Mo-1 und Mo-2 Unterricht; eine weitere Stunde passt am besten direkt anschließend (keine Hohlstunde)
        s.U(1, 0, 1, (0, 1), (0, 2));
        var aktiv = s.U(0, 0, 0);
        aktiv.Wochenstunden = 1;
        var raenge = Platzbewertung.Raenge(s.D, s.Ix, aktiv);
        Assert.NotEmpty(raenge);
        Assert.DoesNotContain((0, 1), raenge.Keys); // belegt
        Assert.True(raenge[(0, 3)] < raenge[(0, 5)]);
        Assert.Equal(raenge.Count, raenge.Values.Distinct().Count());
        Assert.Equal(1, raenge.Values.Min());
    }

    [Fact]
    public void BewertungZurAktuellenZeitFindetStundenFuerEineHohlstunde()
    {
        var s = new Schule();
        // 5a: Mo-1, Mo-3 → Hohlstunde Mo-2; Di-5 könnte nach Mo-2 verlegt werden
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 1, (0, 3));
        var c = s.U(2, 0, 2, (1, 5));
        var fix = s.U(3, 0, 3, (2, 4));
        s.Von(fix)[0].Fixiert = true;
        var liste = Platzbewertung.ZurZeit(s.D, s.Ix, s.K[0].Id, 0, 2);
        Assert.Contains(liste, x => x.Stunde.UnterrichtId == c.Id);
        Assert.DoesNotContain(liste, x => x.Stunde.UnterrichtId == fix.Id);
        Assert.Equal(1, liste[0].Rang);
        // jede gefundene Stunde schließt die Hohlstunde Mo-2 kollisionsfrei
        Assert.All(liste, x => Assert.Empty(Planungsdienst.Hindernisse(s.D, s.Ix, s.Ix.Unterrichte[x.Stunde.UnterrichtId], 0, 2, new[] { x.Stunde })));
        Assert.True(liste.Select(x => x.Rang).SequenceEqual(Enumerable.Range(1, liste.Count)));
        _ = a; _ = b;
    }

    [Fact]
    public void TauschketteVerdraengtUndSchliesstAb()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));   // L0, 5a
        var b = s.U(1, 0, 1, (0, 2));   // L1, 5a
        var kette = Tauschkette.Beginnen(s.Von(a)[0]);
        Assert.Equal("Unt. " + a.Id + ": Mo1 -> ?", kette.Text(s.D));
        var plaetze = kette.Plaetze(s.D);
        Assert.NotEmpty(plaetze);
        var verdraengend = plaetze.Single(p => p.Tag == 0 && p.Stunde == 2);
        Assert.False(verdraengend.Abschluss);
        Assert.EndsWith("!!", verdraengend.Symbol);
        Assert.Contains(plaetze, p => p.Abschluss && p.Symbol.StartsWith('!'));
        Assert.Equal(Enumerable.Range(1, plaetze.Count), plaetze.Select(p => p.Rang));

        // Schritt 1: a nach Mo-2 – b wird verdrängt und ist die neue offene Stunde
        var g = kette.Tauschen(s.D, 0, 2);
        Assert.Equal(b.Id, g.VerdraengtUnterricht);
        Assert.Equal((0, 2), (s.Von(a)[0].Tag, s.Von(a)[0].Stunde));
        Assert.Empty(s.Von(b));
        Assert.Equal(b.Id, kette.UnterrichtId);
        Assert.False(kette.ImPlan);
        Assert.False(kette.Abgeschlossen);
        Assert.Equal("Unt. " + b.Id + ": Mo2 -> ?", kette.Text(s.D));

        // Schritt 2: b nach Mo-1 (frei geworden) – Kette abgeschlossen
        var frei = kette.Plaetze(s.D).Single(p => p.Tag == 0 && p.Stunde == 1);
        Assert.True(frei.Abschluss);
        kette.Tauschen(s.D, 0, 1);
        Assert.True(kette.Abgeschlossen);
        Assert.Equal((0, 1), (s.Von(b)[0].Tag, s.Von(b)[0].Stunde));
        Assert.Equal(2, kette.Glieder.Count);
        Assert.Empty(kette.Plaetze(s.D));
    }

    [Fact]
    public void TauschketteBietetKeineFixiertenOderGesperrtenPlaetze()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 1, (0, 2));
        s.Von(b)[0].Fixiert = true;
        s.K[0].Zeitwuensche.Set(1, 1, -3);
        var kette = Tauschkette.Beginnen(s.Von(a)[0]);
        var plaetze = kette.Plaetze(s.D);
        Assert.DoesNotContain(plaetze, p => p.Tag == 0 && p.Stunde == 2);
        Assert.DoesNotContain(plaetze, p => p.Tag == 1 && p.Stunde == 1);
        Assert.Throws<InvalidOperationException>(() => kette.Tauschen(s.D, 0, 2));
    }

    [Fact]
    public void TauschketteBeendenLaesstOffeneStundeUnverplant()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 1, (0, 2));
        var kette = Tauschkette.Beginnen(s.Von(a)[0]);
        var kopie = kette.Kopie();
        kette.Tauschen(s.D, 0, 2);
        kette.Beenden();
        Assert.True(kette.Abgeschlossen);
        Assert.Equal(1, s.Ix.NichtVerplant(b));
        Assert.False(kopie.Abgeschlossen);
        Assert.Empty(kopie.Glieder);
    }

    [Fact]
    public void SucheImAuswahlfeld()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 1, 1, (0, 2));
        var ix = s.Ix;
        Assert.Equal(new[] { a.Id }, Planungsbereich.Suchen(s.D, ix, a.Id.ToString()).Select(u => u.Id));
        Assert.Equal(new[] { b.Id }, Planungsbereich.Suchen(s.D, ix, "6a").Select(u => u.Id));
        Assert.Equal(new[] { a.Id, b.Id }, Planungsbereich.Suchen(s.D, ix, "F").Select(u => u.Id));
    }

    [Fact]
    public void HistorieListetAenderungenNeuesteZuerst()
    {
        var doc = new Dokument(Demo.NeueDatei("Historie", "GY"));
        doc.Aendern("Erster Schritt", AenderungsArt.Stammdaten, d => d.Einstellungen.Schulname = "A");
        doc.Aendern("Zweiter Schritt", AenderungsArt.Stammdaten, d => d.Einstellungen.Schulname = "B");
        Assert.Equal(new[] { "Zweiter Schritt", "Erster Schritt" }, doc.Historie.Take(2));
        doc.HistorieLoeschen();
        Assert.Empty(doc.Historie);
        Assert.False(doc.KannRueckgaengig);
    }

    [Fact]
    public void SondereinsatzVerlegenUndKopieren()
    {
        var s = new Schule();
        var montag = Kalender.Montag(s.D.Einstellungen.SchuljahrVon.AddDays(14));
        var neu = Vertretungen.Sondereinsatz(s.D, montag, 2, 2, new[] { s.K[0].Id }, new[] { s.L[0].Id }, s.F[0].Id, null, "Projekt");
        var id = neu[0].Id;
        var kopie = Sondereinsatzverlegung.Verlegen(s.D, id, montag.AddDays(1), 3, kopieren: true);
        Assert.Contains(s.D.Vertretungen, v => v.Id == id);
        Assert.Equal((montag.AddDays(1), 3), (kopie.Datum, kopie.Stunde));
        Assert.Equal("Projekt", kopie.Text);
        Assert.Equal(s.L[0].Id, kopie.VertreterId);
        var verlegt = Sondereinsatzverlegung.Verlegen(s.D, id, montag, 4, kopieren: false);
        Assert.DoesNotContain(s.D.Vertretungen, v => v.Id == id);
        Assert.Contains(s.D.Vertretungen, v => v.Id == verlegt.Id && v.Stunde == 4 && v.Datum == montag);
        Assert.Equal(2, s.D.Vertretungen.Count(v => v.Art == VertretungsArt.Sondereinsatz));
    }
}
