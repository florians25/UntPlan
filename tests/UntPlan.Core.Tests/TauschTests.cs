using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Stundentausch per Drag &amp; Drop (Stundenplan- und Vertretungsmodus): Prüfung beider Seiten, Kopplungen, Blöcke, Bänder, Fixierung.</summary>
public class TauschTests
{
    private sealed class Schule
    {
        public Schuldatei D = Demo.NeueDatei("Tausch", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Fach> F = new();

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear();
            for (var i = 0; i < 5; i++)
            {
                L.Add(new Lehrer { Id = D.NeueId(), Kurzname = "L" + i, Wochensoll = 25 });
                K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"{5 + i}a" });
                F.Add(new Fach { Id = D.NeueId(), Kurzname = "F" + i });
            }
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Faecher.AddRange(F);
        }

        /// <summary>Unterricht mit je einer Kopplungszeile (Lehrer, Klasse) und Stunden an (Tag, Stunde).</summary>
        public Unterricht U(IEnumerable<(int L, int K)> zeilen, params (int Tag, int Stunde)[] stunden)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = stunden.Length };
            var f = 0;
            foreach (var (l, k) in zeilen) u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[f++ % F.Count].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            foreach (var (t, s) in stunden) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = t, Stunde = s, Raeume = u.Zeilen.Select(_ => (int?)null).ToList() });
            return u;
        }

        public Unterricht U(int l, int k, params (int Tag, int Stunde)[] stunden) => U(new[] { (l, k) }, stunden);

        public List<Planstunde> Von(Unterricht u) => D.Stundenplan.Where(p => p.UnterrichtId == u.Id).OrderBy(p => p.Tag).ThenBy(p => p.Stunde).ToList();
        public DatenIndex Ix => new(D);
    }

    [Fact]
    public void EinzelstundeWirdMitDerStundeAmZielGetauscht()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        var b = s.U(1, 0, (0, 3));
        var t = Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund);
        Assert.NotNull(t);
        Assert.Equal("", grund);
        Assert.Equal(Feldstatus.TauschMoeglich, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 3));
        Planungsdienst.Tauschen(s.D, t!);
        Assert.Equal((0, 3), (s.Von(a)[0].Tag, s.Von(a)[0].Stunde));
        Assert.Equal((0, 1), (s.Von(b)[0].Tag, s.Von(b)[0].Stunde));
    }

    [Fact]
    public void KeinTauschWennDerPartnerAnDerQuelleKollidiert()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        s.U(1, 0, (0, 3));
        s.U(1, 1, (0, 1)); // Lehrer L1 ist an der Ursprungsposition schon in 6a
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund));
        Assert.Contains("Kollision des Tauschpartners", grund);
        Assert.Equal(Feldstatus.Kollision, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 3));
        // die alte, ungeprüfte Tauschlogik darf dann auch beim Verschieben keinen Doppelbesuch erzeugen
        Assert.NotEqual(Feldstatus.TauschMoeglich, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 3));
    }

    [Fact]
    public void FixierterPartnerUndFixierteQuelleVerhindernDenTausch()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        var b = s.U(1, 0, (0, 3));
        s.Von(b)[0].Fixiert = true;
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund));
        Assert.Contains("fixiert", grund);
        s.Von(b)[0].Fixiert = false;
        s.Von(a)[0].Fixiert = true;
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out _));
    }

    [Fact]
    public void DoppelstundeWirdMitDoppelstundeGetauscht()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1), (0, 2));
        var b = s.U(1, 0, (1, 3), (1, 4));
        var t = Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 1, 3, out _);
        Assert.NotNull(t);
        Assert.Equal(2, t!.Partner.Count);
        Planungsdienst.Tauschen(s.D, t);
        Assert.Equal(new[] { (1, 3), (1, 4) }, s.Von(a).Select(p => (p.Tag, p.Stunde)));
        Assert.Equal(new[] { (0, 1), (0, 2) }, s.Von(b).Select(p => (p.Tag, p.Stunde)));
    }

    [Fact]
    public void EinzelstundeZerreisstKeineDoppelstundeDesPartners()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        s.U(1, 0, (0, 3), (0, 4));
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund));
        Assert.Contains("längerer Block", grund);
        // in der Einzelstunden-Darstellung (einzelne Stunde einer Doppelstunde gezogen) ist ein Einzeltausch erlaubt
        var c = s.U(0, 0, (2, 1), (2, 2));
        s.U(1, 0, (2, 5), (2, 6));
        Assert.NotNull(Planungsdienst.TauschPruefen(s.D, s.Ix, new[] { s.Von(c)[0] }, 2, 5, out _));
    }

    [Fact]
    public void KopplungPrueftAlleKopplungszeilen()
    {
        var s = new Schule();
        // gekoppelter Unterricht: L0 in 5a und L2 in 6a
        var a = s.U(new[] { (0, 0), (2, 1) }, (0, 1));
        s.U(1, 0, (0, 3));
        Assert.NotNull(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out _));
        // weiterer Unterricht der 6a am Ziel: zwei behindernde Unterrichte → kein Tausch
        s.U(3, 1, (0, 3));
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund));
        Assert.Contains("mehrere Unterrichte", grund);
        Assert.Equal(Feldstatus.Kollision, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 3));
    }

    [Fact]
    public void BandWandertAlsGanzesMit()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        var b1 = s.U(1, 0, (0, 3));
        var b2 = s.U(2, 0, (0, 3));
        b1.Band = b2.Band = "WP";
        // zwei Band-Unterrichte am Ziel sind ein Tauschpartner, beide wandern an die Ursprungsposition
        var t = Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 3, out var grund);
        Assert.True(t != null, grund);
        Planungsdienst.Tauschen(s.D, t!);
        Assert.Equal(1, s.Von(b1)[0].Stunde);
        Assert.Equal(1, s.Von(b2)[0].Stunde);
        Assert.Equal(3, s.Von(a)[0].Stunde);
    }

    [Fact]
    public void FreiesZielIstKeinTausch()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 0, 4, out _));
        Assert.Equal(Feldstatus.Frei, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 4));
        Assert.Equal(Feldstatus.Kollision, Planungsdienst.Zielstatus(s.D, s.Ix, s.Von(a), 0, 1)); // eigene Position
    }

    [Fact]
    public void VerschiebenMitTauschNutztDieGepruefteLogik()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        var b = s.U(1, 0, (0, 3));
        Planungsdienst.Verschieben(s.D, s.Von(a)[0], 0, 3, true, false);
        Assert.Equal(3, s.Von(a)[0].Stunde);
        Assert.Equal(1, s.Von(b)[0].Stunde);
    }

    [Fact]
    public void BlockTauschImVertretungsmodus()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1), (0, 2));
        var b = s.U(1, 0, (0, 4), (0, 5));
        var tag = Kalender.NaechsterUnterrichtstag(s.D, s.D.Einstellungen.SchuljahrVon.AddDays(20));
        while (Kalender.TagIndex(s.D, tag) != 0) tag = Kalender.NaechsterUnterrichtstag(s.D, tag);
        var tp = Tagesplan.Berechnen(s.D, tag);
        Assert.Equal(Feldstatus.TauschMoeglich, Tagesverlegung.Zielstatus(s.D, tp, 1, a.Id, null, 2, tp, 4));
        var an = Tagesverlegung.Analysieren(s.D, tag, 1, a.Id, null, 2, tag, 4);
        Assert.True(an.TauschMoeglich);
        Assert.Equal(b.Id, an.TauschUnterricht);
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Tauschen);
        var neu = Tagesplan.Berechnen(s.D, tag).VonKlasse(s.K[0].Id).Where(e => e.Findetstatt).ToList();
        Assert.Equal(new[] { 4, 5 }, neu.Where(e => e.Unterricht?.Id == a.Id || e.Vertretung?.UnterrichtId == a.Id).Select(e => e.Stunde).OrderBy(x => x));
        Assert.Equal(new[] { 1, 2 }, neu.Where(e => e.Unterricht?.Id == b.Id || e.Vertretung?.UnterrichtId == b.Id).Select(e => e.Stunde).OrderBy(x => x));
        // überlappender Block (eine Stunde weiter) ist kein Tausch
        Assert.NotEqual(Feldstatus.TauschMoeglich, Tagesverlegung.Zielstatus(s.D, Tagesplan.Berechnen(s.D, tag), 4, a.Id, null, 2, Tagesplan.Berechnen(s.D, tag), 5));
    }

    [Fact]
    public void PausenKurzbezeichnungen()
    {
        var z = new Zeitraster();
        z.Pausen.Add(new Pause { NachStunde = 0, Bezeichnung = "Frühaufsicht" });
        z.Pausen.Add(new Pause { NachStunde = 2, Bezeichnung = "1. große Pause" });
        z.Pausen.Add(new Pause { NachStunde = 4, Bezeichnung = "2. große Pause" });
        z.Pausen.Add(new Pause { NachStunde = 6, Bezeichnung = "Mittagspause" });
        z.Pausen.Add(new Pause { NachStunde = 8, Bezeichnung = "NP" });
        Assert.Equal(new[] { "FA", "1. P", "2. P", "MP", "NP" }, z.Pausen.Select(p => Pausenbezeichnung.Kurz(z, p)));
    }

    [Fact]
    public void KleinesFormatFasstBloeckeZusammenUndZeigtPausen()
    {
        // wie im Vorbild: Doppelstunden und Blöcke als ein Feld, Pausenzeilen, Unterrichtsliste unter dem Plan
        var f = StundenplanFormat.KleinesFormat(ElementTyp.Klasse, "Klassenplan klein");
        Assert.True(f.Klein && !f.Einzelstunden && f.PausenAnzeigen && f.StundenplanlupeAnzeigen);
        Assert.False(f.ZeitenAnzeigen);
        var json = System.Text.Json.JsonSerializer.Serialize(f);
        var zurueck = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(json)!;
        Assert.True(zurueck.Klein && !zurueck.Einzelstunden && zurueck.PausenAnzeigen);
    }
}
