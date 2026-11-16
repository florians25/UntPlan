using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Manuelles Planen per Drag &amp; Drop: Tauschkette (blauer Pfeil), Hohlstunden-Vorschläge, Grünschattierung, Texte „Block speichern“.</summary>
public class ZiehbewertungTests
{
    private sealed class Schule
    {
        public Schuldatei D = Demo.NeueDatei("Ziehen", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Fach> F = new();

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear();
            for (var i = 0; i < 4; i++)
            {
                L.Add(new Lehrer { Id = D.NeueId(), Kurzname = "L" + i, Wochensoll = 25 });
                K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"{5 + i}a" });
                F.Add(new Fach { Id = D.NeueId(), Kurzname = "F" + i });
            }
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Faecher.AddRange(F);
        }

        public Unterricht U(int l, int k, int f, params (int Tag, int Stunde)[] stunden)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = stunden.Length };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[f].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            foreach (var (t, s) in stunden) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = t, Stunde = s, Raeume = new List<int?> { null } });
            return u;
        }

        public List<Planstunde> Von(Unterricht u) => D.Stundenplan.Where(p => p.UnterrichtId == u.Id).OrderBy(p => p.Tag).ThenBy(p => p.Stunde).ToList();
        public DatenIndex Ix => new(D);
    }

    [Fact]
    public void TauschketteVerlegtDenVerdraengtenUnterrichtAnEinenFreienPlatz()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (1, 5));        // L0, 5a Di-5
        var b = s.U(1, 0, 1, (1, 3));        // L1, 5a Di-3
        // L1 ist Di-5 belegt: kein 2-er-Tausch möglich, aber eine Tauschkette
        s.U(1, 1, 2, (1, 5));
        Assert.Null(Planungsdienst.TauschPruefen(s.D, s.Ix, s.Von(a), 1, 3, out _));
        var k = Ziehbewertung.KettePruefen(s.D, s.Ix, s.Von(a), 1, 3);
        Assert.NotNull(k);
        Assert.Equal(b.Id, k!.PartnerId);
        Assert.False(k.PartnerTag == 1 && k.PartnerStunde == 5);
        Assert.StartsWith("Tauschkette: Di-3 -> ", Ziehbewertung.KettenOption(s.D, k));
        // Prüfung verändert den Plan nicht
        Assert.Equal((1, 5), (s.Von(a)[0].Tag, s.Von(a)[0].Stunde));
        Assert.True(Ziehbewertung.KetteAusfuehren(s.D, k));
        Assert.Equal((1, 3), (s.Von(a)[0].Tag, s.Von(a)[0].Stunde));
        Assert.Equal((k.PartnerTag, k.PartnerStunde), (s.Von(b)[0].Tag, s.Von(b)[0].Stunde));
        var ix = s.Ix;
        foreach (var p in s.D.Stundenplan)
            Assert.Empty(Planungsdienst.Hindernisse(s.D, ix, ix.Unterrichte[p.UnterrichtId], p.Tag, p.Stunde));
    }

    [Fact]
    public void KeineTauschketteBeiFixiertemPartnerOderFreiemZiel()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 1, (0, 2));
        Assert.Null(Ziehbewertung.KettePruefen(s.D, s.Ix, s.Von(a), 0, 4)); // Ziel frei: keine Kette nötig
        s.Von(b)[0].Fixiert = true;
        Assert.Null(Ziehbewertung.KettePruefen(s.D, s.Ix, s.Von(a), 0, 2));
    }

    [Fact]
    public void HohlstundenvorschlaegeNennenNurKollisionsfreieStunden()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        var b = s.U(1, 0, 1, (0, 3));
        var c = s.U(2, 0, 2, (1, 1));
        s.U(1, 1, 3, (0, 2));                 // L1 ist Mo-2 in 6a
        s.Von(c)[0].Fixiert = true;
        var v = Ziehbewertung.Hohlstundenvorschlaege(s.D, s.Ix, ElementTyp.Klasse, s.K[0].Id, 0, 2);
        Assert.Contains(s.Von(a)[0], v);
        Assert.DoesNotContain(s.Von(b)[0], v);   // Lehrerkollision
        Assert.DoesNotContain(s.Von(c)[0], v);   // fixiert
        Assert.Empty(Ziehbewertung.Hohlstundenvorschlaege(s.D, s.Ix, ElementTyp.Klasse, s.K[0].Id, 0, 1)); // Position belegt
    }

    [Fact]
    public void GruenschattierungBevorzugtZeitwuensche()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (0, 1));
        s.L[0].Zeitwuensche.Set(2, 3, 3);
        s.L[0].Zeitwuensche.Set(3, 3, -2);
        var felder = new[] { (2, 3), (3, 3), (4, 3), (4, 4) };
        var g = Ziehbewertung.Gruenschattierung(s.D, a, s.Von(a)[0], felder);
        Assert.Equal(4, g.Count);
        Assert.All(g.Values, x => Assert.InRange(x, 0, Ziehbewertung.Gruenstufen - 1));
        Assert.True(g[(2, 3)] <= g[(4, 3)]);
        Assert.True(g[(3, 3)] >= g[(4, 3)]);
    }

    [Fact]
    public void BlockSpeichernTexteWieImHandbuch()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, (1, 5));
        var b = s.U(0, 0, 1, (1, 1));
        var ix = s.Ix;
        var h = Planungsdienst.Hindernisse(s.D, ix, ix.Unterrichte[a.Id], 1, 1, s.Von(a));
        Assert.Equal($"Block speichern - Unterricht: {a.Id} F0 L0", Ziehbewertung.BlockSpeichernTitel(ix, a));
        var text = Ziehbewertung.BlockSpeichernText(s.D, ix, a, (1, 5), 1, 1, h);
        Assert.StartsWith($"Unterricht: {a.Id} F0 L0 - Di-5 --> Di-1", text);
        Assert.Contains("Anderer Unterricht behindert Verplanung - Anzahl hindernde Unterrichte: 1", text);
        Assert.Contains($"Unt: {b.Id} Fach: F1 Lehrer: L0", text);
        Assert.Contains("Behindernde Elemente: 2 L0, 5a", text);
        Assert.Equal($"2-er-Tausch: Di-5 (Unt.: {a.Id} F0 L0) <--> Di-1 (Unt.: {b.Id} Fach: F1 Lehrer: L0)",
            Ziehbewertung.TauschOption(s.D, ix, a, 1, 5, b, 1, 1));
    }
}
