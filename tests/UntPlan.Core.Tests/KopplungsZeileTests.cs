using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Kopplungen in der Vertretungsliste wie im Vorbild: eine Zeile mit allen Klassen („HP12A, HP13A“) statt je Kopplungszeile einer Zeile –
/// in Liste und Kalender-Kennzahlen; Vertreter bzw. Entfall gelten für die ganze Kopplung. Gespeichert und exportiert wird weiter je
/// Kopplungszeile (Statistik; UntPlanWeb fasst die Karten einer Kopplung selbst zusammen).
/// </summary>
public class KopplungsZeileTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Kopplung", "GY");
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
                K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"K{i}" });
                F.Add(new Fach { Id = D.NeueId(), Kurzname = "F" + i });
                R.Add(new Raum { Id = D.NeueId(), Kurzname = "R" + i });
            }
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Faecher.AddRange(F); D.Raeume.AddRange(R);
            Montag = Kalender.Montag(Kalender.NaechsterUnterrichtstag(D, D.Einstellungen.SchuljahrVon.AddDays(21)));
            while (!Kalender.IstUnterrichtstag(D, Montag)) Montag = Montag.AddDays(7);
        }

        /// <summary>Kopplung in der 2. Stunde am Montag: je Zeile (Lehrer, Fach, Klasse), alle im Raum R0.</summary>
        public Unterricht Kopplung(params (int L, int F, int K)[] zeilen)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = 1 };
            foreach (var (l, f, k) in zeilen) u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[f].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 2, Raeume = zeilen.Select(_ => (int?)R[0].Id).ToList() });
            return u;
        }

        public void Absent(int l)
        {
            D.Abwesenheiten.Add(new Abwesenheit { Id = D.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = L[l].Id, Von = Montag, Bis = Montag });
            Vertretungen.Aktualisieren(D, Montag);
        }
    }

    [Fact]
    public void KopplungDerselbenLehrkraftIstEineZeileMitAllenKlassen()
    {
        var s = new Schule();
        s.Kopplung((0, 0, 0), (0, 0, 1));
        s.Absent(0);
        var zeilen = s.D.Vertretungen.Where(v => v.Datum == s.Montag).ToList();
        Assert.Equal(2, zeilen.Count); // gespeichert je Kopplungszeile (Statistik)
        Assert.Single(Vertretungen.NachKopplung(zeilen));
        Assert.Equal(2, Vertretungen.NachKopplung(zeilen)[0].Gruppe.Count);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((1, 1), (k.Vertretungen, k.Offen));
        var zeile = Assert.Single(Kalenderkennzahlen.Listenzeilen(s.D, s.Montag));
        Assert.True(zeile.Offen);
        Assert.Equal(2, Vertretungsplan.Zeilen(s.D, s.Montag).Count); // Export je Kopplungszeile
    }

    [Fact]
    public void VertreterFuerDieKopplungIstKeineKollision()
    {
        var s = new Schule();
        s.Kopplung((0, 0, 0), (0, 0, 1));
        s.Absent(0);
        var zeilen = s.D.Vertretungen.Where(v => v.Datum == s.Montag).ToList();
        foreach (var v in zeilen) Vertretungen.VertreterSetzen(s.D, v, s.L[2].Id);
        var tp = Tagesplan.Berechnen(s.D, s.Montag);
        Assert.All(zeilen, v => Assert.Null(Vertretungen.LehrerBelegt(s.D, tp, s.L[2].Id, 2, v)));
        Assert.Single(Vertretungen.NachKopplung(zeilen));
        Assert.Equal((1, 0), (Kalenderkennzahlen.Tag(s.D, s.Montag).Vertretungen, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen));
        Assert.Equal(s.L[2].Id, Assert.Single(Kalenderkennzahlen.Listenzeilen(s.D, s.Montag)).VertreterId);
        // nur eine Zeile anders vertreten: wieder zwei Zeilen
        Vertretungen.VertreterSetzen(s.D, zeilen[1], s.L[3].Id);
        Assert.Equal(2, Vertretungen.NachKopplung(zeilen).Count);
        Assert.Equal(2, Kalenderkennzahlen.Listenzeilen(s.D, s.Montag).Count);
    }

    [Fact]
    public void EntfallGiltFuerDieGanzeKopplung()
    {
        var s = new Schule();
        var u = s.Kopplung((0, 0, 0), (0, 0, 1));
        s.Absent(0);
        foreach (var v in s.D.Vertretungen.ToList()) Vertretungen.EntfallUmschalten(s.D, v);
        Assert.Single(Vertretungen.NachKopplung(s.D.Vertretungen));
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((1, 0, 1), (k.Vertretungen, k.Offen, k.Entfaelle));
        // Stunde einer Kopplung im Plan entfallen lassen: ebenfalls eine Zeile
        var t = new Schule();
        var u2 = t.Kopplung((0, 0, 0), (0, 0, 1));
        Vertretungen.Entfallen(t.D, u2.Id, null, t.Montag, 2);
        Assert.Equal(2, t.D.Vertretungen.Count);
        Assert.Single(Kalenderkennzahlen.Listenzeilen(t.D, t.Montag));
        Assert.Equal(1, Kalenderkennzahlen.Tag(t.D, t.Montag).Entfaelle);
        Assert.NotNull(u);
    }

    [Fact]
    public void VerschiedeneFaecherOderLehrkraefteBleibenEinzeln()
    {
        // wie im Vorbild „08D Bon M_G“ und „08D Bon M_E“: verschiedene Fächer stehen in eigenen Zeilen
        var s = new Schule();
        s.Kopplung((0, 0, 0), (0, 1, 1));
        s.Absent(0);
        Assert.Equal(2, Vertretungen.NachKopplung(s.D.Vertretungen).Count);
        Assert.Equal(2, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
        // zwei Lehrkräfte einer Kopplung fehlen: je Lehrkraft eine Zeile
        var t = new Schule();
        t.Kopplung((0, 0, 0), (1, 0, 1));
        t.Absent(0);
        t.Absent(1);
        Assert.Equal(2, Vertretungen.NachKopplung(t.D.Vertretungen).Count);
    }
}
