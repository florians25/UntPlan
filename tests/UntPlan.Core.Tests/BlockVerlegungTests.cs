using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Verschieben ganzer Blöcke (Doppelstunde, 3er-, 4er-Block, auch gekoppelt) im tagesaktuellen Plan: alle Stunden wandern gemeinsam, jeder
/// frei gewordene Ursprung bleibt offen („???“, „(Le.) nach“ = eigene Zielstunde), jedes Ziel zeigt „Vertr. von“. Überlappen Ursprung und Ziel
/// (Block um 1–3 Stunden verschoben), bleiben nur die wirklich frei gewordenen Stunden offen. Rückfrage, Entfall (Nein) und Aufheben (Ja,
/// Zurücksetzen, Strg+Z) gelten für den ganzen Block; erneutes Verschieben erzeugt keine doppelten Zeilen; Kalender-Kennzahlen stimmen.
/// </summary>
public class BlockVerlegungTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Block", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Fach> F = new();
        public readonly List<Raum> R = new();
        public readonly DateOnly Montag;
        public DateOnly Dienstag => Montag.AddDays(1);

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
            while (!Kalender.IstUnterrichtstag(D, Montag) || !Kalender.IstUnterrichtstag(D, Montag.AddDays(1))) Montag = Montag.AddDays(7);
        }

        /// <summary>Block: Lehrer l / Klasse k / Fach l / Raum k am Tag (0 = Montag) in den Stunden von..von+laenge-1.</summary>
        public Unterricht U(int l, int k, int tag, int von, int laenge)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = laenge };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[l].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            for (var s = von; s < von + laenge; s++) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = s, Raeume = new List<int?> { R[k].Id } });
            return u;
        }

        /// <summary>Kopplung zweier Zeilen (L0/5a/R0 und L1/6a/R1) als Block.</summary>
        public Unterricht Kopplung(int tag, int von, int laenge)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = laenge };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[0].Id, FachId = F[0].Id, KlassenIds = { K[0].Id } });
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[1].Id, FachId = F[0].Id, KlassenIds = { K[1].Id } });
            D.Unterrichte.Add(u);
            for (var s = von; s < von + laenge; s++) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = s, Raeume = new List<int?> { R[0].Id, R[1].Id } });
            return u;
        }

        public List<Vertretung> An(DateOnly t, int s) => D.Vertretungen.Where(v => v.Datum == t && v.Stunde == s).ToList();

        /// <summary>Block wie beim Ziehen im Stundenplan ablegen (ganze Länge, eine Änderung).</summary>
        public VerlegeAnalyse Ziehen(Unterricht u, DateOnly von, int vonStunde, int laenge, DateOnly ziel, int zielStunde, VerlegeModus modus = VerlegeModus.Verlegen)
        {
            var a = Tagesverlegung.Analysieren(D, von, vonStunde, u.Id, null, laenge, ziel, zielStunde);
            Assert.Equal(laenge, a.Schritte.Count);
            if (modus == VerlegeModus.Verlegen) Assert.False(a.Kollision);
            Tagesverlegung.Ausfuehren(D, a, modus);
            return a;
        }

        public string P(DateOnly t, string stunden) => $"{t:d.M.} / {stunden}";
    }

    private static void AlleRegulaer(Schule s, Unterricht u, DateOnly t, int von, int laenge)
    {
        var tp = Tagesplan.Berechnen(s.D, t);
        for (var st = von; st < von + laenge; st++)
        {
            var e = Assert.Single(tp.Stunde(st), x => (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == u.Id);
            Assert.Equal(TagesStatus.Regulaer, e.Status);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BlockWandertGanzUrsprungOffenJeStundeMitEigenemZiel(int n)
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, n);
        s.Ziehen(u, s.Montag, 1, n, s.Montag, 5);
        Assert.Equal(2 * n, s.D.Vertretungen.Count);
        var block = s.D.Vertretungen.Select(v => v.BlockId).Distinct().ToList();
        Assert.NotNull(Assert.Single(block));
        for (var i = 0; i < n; i++)
        {
            var q = Assert.Single(s.An(s.Montag, 1 + i));
            var z = Assert.Single(s.An(s.Montag, 5 + i));
            Assert.True(Vertretungen.IstOffenerUrsprung(q));
            Assert.Equal("???", Vertretungsanzeige.Vertreter(s.D, q));
            Assert.Equal(s.P(s.Montag, $"{5 + i}"), Vertretungsanzeige.Nach(q));
            Assert.Equal(VertretungsArt.Verlegung, z.Art);
            Assert.Equal(s.P(s.Montag, $"{1 + i}"), Vertretungsanzeige.Von(z));
            Assert.Equal(q.VerlegungsId, z.VerlegungsId);
            Assert.Equal(s.R[0].Id, z.ZielraumId);
        }
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((2 * n, n, 0), (k.Vertretungen, k.Offen, k.Entfaelle));
        // Rückfrage einmal für den ganzen Block, gleich ob am Ursprung oder Ziel gewählt
        var text = Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 1)[0]);
        Assert.Contains($"{s.P(s.Montag, $"1-{n}")} -> {s.P(s.Montag, $"5-{4 + n}")}", text);
        Assert.Equal(text, Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 4 + n)[0]));
        Assert.Equal(n, Verlegungsaufhebung.BlockZeilen(s.D, s.An(s.Montag, 2)[0]).Count);
        Assert.All(Verlegungsaufhebung.BlockZeilen(s.D, s.An(s.Montag, 2)[0]), v => Assert.True(Vertretungen.IstUrsprung(v)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NeinSetztAlleUrspruengeDesBlocksAufEntfall(int n)
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, n);
        s.Ziehen(u, s.Montag, 1, n, s.Montag, 5);
        var mitte = s.An(s.Montag, n)[0];
        Assert.Equal(n, Verlegungsaufhebung.BlockAufEntfallSetzen(s.D, new[] { mitte.Id }));
        for (var i = 0; i < n; i++)
        {
            var q = Assert.Single(s.An(s.Montag, 1 + i));
            Assert.Equal(VertretungsArt.Entfall, q.Art);
            Assert.Equal(s.P(s.Montag, $"{5 + i}"), Vertretungsanzeige.Nach(q));
            Assert.Equal(VertretungsArt.Verlegung, Assert.Single(s.An(s.Montag, 5 + i)).Art);
        }
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((2 * n, 0, n), (k.Vertretungen, k.Offen, k.Entfaelle));
        // ein zweites Mal ändert nichts
        Assert.Equal(0, Verlegungsaufhebung.BlockAufEntfallSetzen(s.D, new[] { mitte.Id }));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public void JaHebtDenGanzenBlockAuf(int n, bool amZiel)
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, n);
        s.Ziehen(u, s.Montag, 1, n, s.Dienstag, 2);
        var v = amZiel ? s.An(s.Dienstag, 3)[0] : s.An(s.Montag, n)[0];
        Assert.Equal(n, Verlegungsaufhebung.Aufheben(s.D, v));
        Assert.Empty(s.D.Vertretungen);
        AlleRegulaer(s, u, s.Montag, 1, n);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UeberlappendVorwaertsBleibtNurDerFreiGewordeneTeilOffen(int versatz)
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 4);
        s.Ziehen(u, s.Montag, 1, 4, s.Montag, 1 + versatz);
        // frei geworden: 1..versatz, neu: 5..4+versatz, dazwischen unverändert (keine Zeile, kein „???“, kein „(Le.) nach“)
        Assert.Equal(2 * versatz, s.D.Vertretungen.Count);
        for (var i = 0; i < versatz; i++)
        {
            var q = Assert.Single(s.An(s.Montag, 1 + i));
            Assert.True(Vertretungen.IstOffenerUrsprung(q));
            Assert.Equal(s.P(s.Montag, $"{5 + i}"), Vertretungsanzeige.Nach(q));
            var z = Assert.Single(s.An(s.Montag, 5 + i));
            Assert.Equal(s.P(s.Montag, $"{1 + i}"), Vertretungsanzeige.Von(z));
        }
        for (var st = 1 + versatz; st <= 4; st++) Assert.Empty(s.An(s.Montag, st));
        AlleRegulaer(s, u, s.Montag, 1 + versatz, 4 - versatz);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((2 * versatz, versatz, 0), (k.Vertretungen, k.Offen, k.Entfaelle));
        var text = Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 1)[0]);
        var frei = versatz == 1 ? "1" : $"1-{versatz}";
        var neu = versatz == 1 ? "5" : $"5-{4 + versatz}";
        Assert.Contains($"{s.P(s.Montag, frei)} -> {s.P(s.Montag, neu)}", text);
        // Nein: nur die frei gewordenen Stunden entfallen
        Assert.Equal(versatz, Verlegungsaufhebung.BlockAufEntfallSetzen(s.D, new[] { s.An(s.Montag, 1)[0].Id }));
        Assert.Equal((2 * versatz, 0, versatz), Kalenderkennzahlen.Tag(s.D, s.Montag) is var k2 ? (k2.Vertretungen, k2.Offen, k2.Entfaelle) : default);
        // Ja (auf dem Ziel): alles wieder wie vorher
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 5)[0]);
        Assert.Empty(s.D.Vertretungen);
        AlleRegulaer(s, u, s.Montag, 1, 4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UeberlappendRueckwaertsBleibtNurDerFreiGewordeneTeilOffen(int versatz)
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 4, 4); // 4–7
        s.Ziehen(u, s.Montag, 4, 4, s.Montag, 4 - versatz);
        Assert.Equal(2 * versatz, s.D.Vertretungen.Count);
        for (var i = 0; i < versatz; i++)
        {
            // frei: 8-versatz..7, neu: 4-versatz..3
            var q = Assert.Single(s.An(s.Montag, 8 - versatz + i));
            Assert.True(Vertretungen.IstOffenerUrsprung(q));
            var z = Assert.Single(s.An(s.Montag, 4 - versatz + i));
            Assert.Equal((q.Datum, q.Stunde), (z.VonDatum!.Value, z.VonStunde!.Value));
            Assert.Equal((z.Datum, z.Stunde), (q.NachDatum!.Value, q.NachStunde!.Value));
        }
        AlleRegulaer(s, u, s.Montag, 4, 4 - versatz);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((2 * versatz, versatz, 0), (k.Vertretungen, k.Offen, k.Entfaelle));
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 7)[0]);
        Assert.Empty(s.D.Vertretungen);
    }

    [Fact]
    public void ErneutesVerschiebenOhneDoppelteZeilenUndZurueck()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 4);
        s.Ziehen(u, s.Montag, 1, 4, s.Montag, 5);
        // 5–8 an einen anderen Tag: dieselben Zeilen, Ursprünge zeigen auf das neue Ziel
        s.Ziehen(u, s.Montag, 5, 4, s.Dienstag, 2);
        Assert.Equal(8, s.D.Vertretungen.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(s.P(s.Dienstag, $"{2 + i}"), Vertretungsanzeige.Nach(Assert.Single(s.An(s.Montag, 1 + i))));
            Assert.Equal(s.P(s.Montag, $"{1 + i}"), Vertretungsanzeige.Von(Assert.Single(s.An(s.Dienstag, 2 + i))));
        }
        Assert.Single(s.D.Vertretungen.Select(v => v.BlockId).Distinct());
        Assert.Contains($"{s.P(s.Montag, "1-4")} -> {s.P(s.Dienstag, "2-5")}", Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 1)[0]));
        // zurück auf den Montag, aber um zwei Stunden versetzt (3–6): 3–4 finden wieder regulär statt
        s.Ziehen(u, s.Dienstag, 2, 4, s.Montag, 3);
        Assert.Equal(4, s.D.Vertretungen.Count);
        Assert.Equal(s.P(s.Montag, "5"), Vertretungsanzeige.Nach(Assert.Single(s.An(s.Montag, 1))));
        Assert.Equal(s.P(s.Montag, "6"), Vertretungsanzeige.Nach(Assert.Single(s.An(s.Montag, 2))));
        Assert.Empty(s.An(s.Montag, 3));
        Assert.Empty(s.An(s.Montag, 4));
        Assert.Empty(s.D.Vertretungen.Where(v => v.Datum == s.Dienstag));
        Assert.Equal((4, 2, 0), Kalenderkennzahlen.Tag(s.D, s.Montag) is var k ? (k.Vertretungen, k.Offen, k.Entfaelle) : default);
        // Ziehen im Plan: der Block 3–6 (teils regulär, teils verlegt) wird als Ganzes gegriffen
        var tp = Tagesplan.Berechnen(s.D, s.Montag);
        Assert.Equal((3, 4), Tagesverlegung.Blockumfang(tp, 5, 2, u.Id, null));
        Assert.Equal((3, 4), Tagesverlegung.Blockumfang(tp, 3, 2, u.Id, null));
        // ganz zurück an den Ursprung: keine Zeile bleibt
        s.Ziehen(u, s.Montag, 3, 4, s.Montag, 1);
        Assert.Empty(s.D.Vertretungen);
        AlleRegulaer(s, u, s.Montag, 1, 4);
    }

    [Fact]
    public void BlockumfangErweitertReguläreDoppelstundeNicht()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 2);
        s.D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 3, Raeume = new List<int?> { s.R[1].Id } });
        var tp = Tagesplan.Berechnen(s.D, s.Montag);
        Assert.Equal((1, 2), Tagesverlegung.Blockumfang(tp, 1, 2, u.Id, null));
    }

    [Fact]
    public void VerschiebenInBelegteStundenFragtKollisionUndVerdraengt()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 3);
        var andere = s.U(1, 0, 0, 6, 1); // dieselbe Klasse in der 6. Stunde
        var a = Tagesverlegung.Analysieren(s.D, s.Montag, 1, u.Id, null, 3, s.Montag, 5);
        Assert.True(a.Kollision);
        Assert.False(a.TauschMoeglich);
        Assert.Contains("in der 6. Stunde: Kollision", Tagesverlegung.KollisionsFrage(s.D, a));
        Tagesverlegung.Ausfuehren(s.D, a, VerlegeModus.Verdraengen);
        for (var i = 0; i < 3; i++) Assert.Contains(s.An(s.Montag, 5 + i), v => v.Art == VertretungsArt.Verlegung && v.UnterrichtId == u.Id);
        Assert.Contains(s.An(s.Montag, 6), v => v.UnterrichtId == andere.Id && v.Art == VertretungsArt.Vormerkung);
        Assert.Equal(3, Verlegungsaufhebung.BlockZeilen(s.D, s.An(s.Montag, 1)[0]).Count);
    }

    [Fact]
    public void BlockMitEntfallenerStundeBehaeltDenEntfallUndRuecknahmeStelltIhnWiederHer()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 4);
        Vertretungen.Entfallen(s.D, u.Id, null, s.Montag, 2);
        s.Ziehen(u, s.Montag, 1, 4, s.Dienstag, 1);
        var q2 = Assert.Single(s.An(s.Montag, 2));
        Assert.Equal(VertretungsArt.Entfall, q2.Art);
        Assert.Equal(s.P(s.Dienstag, "2"), Vertretungsanzeige.Nach(q2));
        Assert.All(new[] { 1, 3, 4 }, st => Assert.True(Vertretungen.IstOffenerUrsprung(Assert.Single(s.An(s.Montag, st)))));
        Assert.Equal(4, s.An(s.Dienstag, 1).Count + s.An(s.Dienstag, 2).Count + s.An(s.Dienstag, 3).Count + s.An(s.Dienstag, 4).Count);
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 3)[0]);
        var e = Assert.Single(s.D.Vertretungen);
        Assert.Equal((s.Montag, 2, VertretungsArt.Entfall), (e.Datum, e.Stunde, e.Art));
        Assert.Null(e.BlockId);
        Assert.Null(e.VerlegungsId);
    }

    [Fact]
    public void BlocktauschGleicherLaengeUndAufheben()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, 1, 2);
        var b = s.U(1, 0, 0, 5, 2);
        var an = Tagesverlegung.Analysieren(s.D, s.Montag, 1, a.Id, null, 2, s.Montag, 5);
        Assert.True(an.TauschMoeglich);
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Tauschen);
        Assert.Equal(8, s.D.Vertretungen.Count);
        Assert.Single(s.D.Vertretungen.Select(v => v.BlockId).Distinct());
        Assert.Contains($"{s.P(s.Montag, "1-2")} -> {s.P(s.Montag, "5-6")}", Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 1).First(v => v.UnterrichtId == a.Id)));
        Assert.Contains($"{s.P(s.Montag, "5-6")} -> {s.P(s.Montag, "1-2")}", Verlegungsaufhebung.RueckfrageText(s.D, s.An(s.Montag, 1).First(v => v.UnterrichtId == b.Id)));
        // Nein auf dem Ursprung von A: beide Stunden von A entfallen, B bleibt getauscht
        var qa = s.An(s.Montag, 2).First(v => v.UnterrichtId == a.Id);
        Assert.Equal(2, Verlegungsaufhebung.BlockAufEntfallSetzen(s.D, new[] { qa.Id }));
        Assert.All(s.D.Vertretungen.Where(v => v.UnterrichtId == a.Id && Vertretungen.IstUrsprung(v)), v => Assert.True(v.IstEntfall));
        Assert.All(s.D.Vertretungen.Where(v => v.UnterrichtId == b.Id && Vertretungen.IstUrsprung(v)), v => Assert.False(v.IstEntfall));
        // Ja: der ganze Tausch beider Blöcke wird aufgehoben
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 6).First(v => v.UnterrichtId == b.Id));
        Assert.Empty(s.D.Vertretungen);
        AlleRegulaer(s, a, s.Montag, 1, 2);
        AlleRegulaer(s, b, s.Montag, 5, 2);
    }

    [Fact]
    public void GekoppelterBlockWandertMitAllenKopplungszeilen()
    {
        var s = new Schule();
        var u = s.Kopplung(0, 1, 3);
        s.Ziehen(u, s.Montag, 1, 3, s.Montag, 5);
        Assert.Equal(12, s.D.Vertretungen.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(2, s.An(s.Montag, 1 + i).Count(Vertretungen.IstOffenerUrsprung));
            Assert.Equal(2, s.An(s.Montag, 5 + i).Count(v => v.Art == VertretungsArt.Verlegung));
        }
        Assert.Single(s.D.Vertretungen.Select(v => v.BlockId).Distinct());
        var q = s.An(s.Montag, 2)[0];
        Assert.Equal(6, Verlegungsaufhebung.BlockZeilen(s.D, q).Count);
        Assert.Contains($"{s.P(s.Montag, "1-3")} -> {s.P(s.Montag, "5-7")}", Verlegungsaufhebung.RueckfrageText(s.D, q));
        Assert.Equal(6, Verlegungsaufhebung.BlockAufEntfallSetzen(s.D, new[] { q.Id }));
        Assert.Equal(6, s.D.Vertretungen.Count(v => Vertretungen.IstUrsprung(v) && v.IstEntfall));
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 7)[1]);
        Assert.Empty(s.D.Vertretungen);
    }

    [Fact]
    public void GekoppelterBlockUeberlappendVerschoben()
    {
        var s = new Schule();
        var u = s.Kopplung(0, 1, 4);
        s.Ziehen(u, s.Montag, 1, 4, s.Montag, 3);
        Assert.Equal(8, s.D.Vertretungen.Count);
        Assert.Equal(2, s.An(s.Montag, 1).Count(Vertretungen.IstOffenerUrsprung));
        Assert.Empty(s.An(s.Montag, 3));
        Assert.Empty(s.An(s.Montag, 4));
        Assert.Equal(2, s.An(s.Montag, 6).Count(v => v.Art == VertretungsArt.Verlegung));
        Verlegungsaufhebung.Aufheben(s.D, s.An(s.Montag, 2)[0]);
        Assert.Empty(s.D.Vertretungen);
    }

    [Fact]
    public void ZielZeigtRaumaenderungJeStundeGegenuberDemUrsprungsraum()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 2);
        // Stunde 2 fand schon in R2 statt
        Vertretungen.RaumAendern(s.D, u.Id, 0, s.Montag, 2, s.R[2].Id, false);
        s.Ziehen(u, s.Montag, 1, 2, s.Montag, 5);
        var z5 = s.An(s.Montag, 5).Single(v => v.VonDatum.HasValue);
        var z6 = s.An(s.Montag, 6).Single(v => v.VonDatum.HasValue);
        Assert.Equal(s.R[0].Id, z5.ZielraumId);
        Assert.Equal(s.R[2].Id, z6.ZielraumId);
    }

    [Fact]
    public void RueckgaengigUndKopieBehaltenDenBlock()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 4);
        var doc = new Dokument(s.D);
        doc.Aendern("Block verlegen (4 Stunden)", AenderungsArt.Vertretung, d =>
        {
            var a = Tagesverlegung.Analysieren(d, s.Montag, 1, u.Id, null, 4, s.Montag, 3);
            Tagesverlegung.Ausfuehren(d, a, VerlegeModus.Verlegen);
        });
        Assert.Equal(4, doc.Daten.Vertretungen.Count);
        var klon = Dokument.Klonen(doc.Daten);
        Assert.Equal(doc.Daten.Vertretungen.Select(v => v.BlockId), klon.Vertretungen.Select(v => v.BlockId));
        doc.Rueckgaengig();
        Assert.Empty(doc.Daten.Vertretungen);
    }
    [Fact]
    public void WebExportZeigtNurDieFreiGewordenenStundenOffen()
    {
        var s = new Schule();
        var u = s.U(0, 0, 0, 1, 4);
        s.Ziehen(u, s.Montag, 1, 4, s.Montag, 3);
        var p = UntPlanWebExport.PaketErstellen(s.D, s.Montag, new WebExportOptionen { TageVoraus = 1, TageZurueck = 0 });
        var tag = p.Tage.Single(t => t.Datum == s.Montag.ToString("yyyy-MM-dd"));
        string Status(int st) => string.Join(",", tag.Stunden.Where(x => x.Stunde == st && x.Unterricht == u.Id).Select(x => x.Status));
        Assert.Equal("offen", Status(1));
        Assert.Equal("offen", Status(2));
        Assert.Equal("regulaer", Status(3));
        Assert.Equal("regulaer", Status(4));
        Assert.Equal("zusaetzlich", Status(5));
        Assert.Equal("zusaetzlich", Status(6));
        var v = p.Vertretungen.Where(x => x.Datum == s.Montag.ToString("yyyy-MM-dd")).OrderBy(x => x.Stunde).ToList();
        Assert.Equal(new[] { 1, 2, 5, 6 }, v.Select(x => x.Stunde));
        Assert.Equal("???", v[0].Vertreter);
        Assert.EndsWith("/ 5", v[0].Nach);
        Assert.EndsWith("/ 2", v[3].VertrVon);
    }
}
