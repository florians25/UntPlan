using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Entfälle beim Verlegen, Tauschen und Vorziehen wie im Vorbild: Ursprung „Entfall“ mit „(Le.) nach“, Ziel mit „Vertr. von“,
/// keine doppelten Zeilen, Rücknahme stellt den alten Zustand wieder her, Kalender-Kennzahlen bleiben stimmig.
/// </summary>
public class VerlegungEntfallTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Verlegung", "GY");
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
            while (!Kalender.IstUnterrichtstag(D, Montag) || !Kalender.IstUnterrichtstag(D, Montag.AddDays(1))) Montag = Montag.AddDays(7);
        }

        /// <summary>Unterricht Lehrer l / Klasse k / Fach l / Raum k an (Tag, Stunde).</summary>
        public Unterricht U(int l, int k, params (int Tag, int Stunde)[] stunden)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = stunden.Length };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = F[l].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            foreach (var (t, s) in stunden) D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = t, Stunde = s, Raeume = new List<int?> { R[k].Id } });
            return u;
        }

        public List<Vertretung> An(DateOnly t, int s) => D.Vertretungen.Where(v => v.Datum == t && v.Stunde == s).ToList();

        public void Absent(int l, DateOnly t)
        {
            D.Abwesenheiten.Add(new Abwesenheit { Id = D.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = L[l].Id, Von = t, Bis = t });
            Vertretungen.Aktualisieren(D, t);
        }
    }

    [Fact]
    public void VerlegungErzeugtEntfallMitNachUndZielMitVon()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag, 5);
        var q = Assert.Single(s.An(s.Montag, 2));
        var z = Assert.Single(s.An(s.Montag, 5));
        Assert.Equal(VertretungsArt.Entfall, q.Art);
        Assert.Equal((s.Montag, 5), (q.NachDatum!.Value, q.NachStunde!.Value));
        Assert.Null(q.VertreterId);
        Assert.Equal(VertretungsArt.Verlegung, z.Art);
        Assert.Equal((s.Montag, 2), (z.VonDatum!.Value, z.VonStunde!.Value));
        Assert.Equal(q.VerlegungsId, z.VerlegungsId);
        Assert.Equal(s.L[0].Id, z.VertreterId);
        Assert.Equal("---", Vertretungsanzeige.Vertreter(s.D, q));
        Assert.Equal("---", Vertretungsanzeige.NeuerRaum(s.D, q));
        Assert.Equal($"{s.Montag:d.M.} / 5", Vertretungsanzeige.Nach(q));
        Assert.Equal($"{s.Montag:d.M.} / 2", Vertretungsanzeige.Von(z));
        Assert.Equal("L0", Vertretungsanzeige.Vertreter(s.D, z));
    }

    [Fact]
    public void ReinerEntfallZeigtStricheUndEntfallBeiNach()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Entfallen(s.D, u.Id, null, s.Montag, 2);
        var e = Assert.Single(s.An(s.Montag, 2));
        Assert.Equal("---", Vertretungsanzeige.Vertreter(s.D, e));
        Assert.Equal("---", Vertretungsanzeige.NeuerRaum(s.D, e));
        Assert.Equal("---", Vertretungsanzeige.NeuesFach(s.D, e));
        Assert.Equal("Entfall", Vertretungsanzeige.Nach(e));
        // offene Vertretung: „???“
        s.U(1, 1, (0, 3));
        s.Absent(1, s.Montag);
        var o = Assert.Single(s.An(s.Montag, 3));
        Assert.True(o.Offen);
        Assert.Equal("???", Vertretungsanzeige.Vertreter(s.D, o));
        Assert.Equal("", Vertretungsanzeige.Nach(o));
    }

    [Fact]
    public void TauschErzeugtGespiegelteZeilenUndLaesstSichAufheben()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 1));
        var b = s.U(1, 0, (0, 4));
        Vertretungen.Tauschen(s.D, a.Id, s.Montag, 1, b.Id, s.Montag, 4);
        Assert.Equal(4, s.D.Vertretungen.Count);
        var tA = s.D.Vertretungen.Single(v => v.UnterrichtId == a.Id && v.Art == VertretungsArt.Tausch);
        var tB = s.D.Vertretungen.Single(v => v.UnterrichtId == b.Id && v.Art == VertretungsArt.Tausch);
        Assert.Equal((4, 1), (tA.Stunde, tA.VonStunde!.Value));
        Assert.Equal((1, 4), (tB.Stunde, tB.VonStunde!.Value));
        var qA = s.D.Vertretungen.Single(v => v.UnterrichtId == a.Id && v.Art == VertretungsArt.Entfall);
        var qB = s.D.Vertretungen.Single(v => v.UnterrichtId == b.Id && v.Art == VertretungsArt.Entfall);
        Assert.Equal((1, 4), (qA.Stunde, qA.NachStunde!.Value));
        Assert.Equal((4, 1), (qB.Stunde, qB.NachStunde!.Value));
        // in der Liste je Position eine Zeile „Tausch“ (Ursprung verdeckt), Kennzahlen zählen die sichtbaren Zeilen
        var paare = Vertretungen.ZusammengefassteZeilen(s.D.Vertretungen);
        Assert.Equal(qB.Id, paare[tA.Id].Id);
        Assert.Equal(qA.Id, paare[tB.Id].Id);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal(2, k.Vertretungen);
        Assert.Equal(0, k.Offen);
        Assert.Equal(0, k.Entfaelle);
        // Aufheben einer Seite hebt den ganzen Tausch auf
        Assert.Equal(2, Verlegungsaufhebung.Aufheben(s.D, tA));
        Assert.Empty(s.D.Vertretungen);
        Assert.Equal(0, Kalenderkennzahlen.Tag(s.D, s.Montag).Vertretungen);
    }

    [Fact]
    public void VorziehungFuelltOffeneStundeUndRuecknahmeStelltSieWiederHer()
    {
        var s = new Schule();
        var a = s.U(0, 0, (0, 2));
        var b = s.U(1, 0, (0, 5));
        s.Absent(0, s.Montag);
        var offen = Assert.Single(s.D.Vertretungen);
        Assert.True(offen.Offen);
        Assert.Equal(1, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
        var vz = VertretungsVorschlag.Vorziehungen(s.D, offen, VorziehungsModus.AusRandstunden, true).First(x => x.Eintrag.Unterricht?.Id == b.Id && x.Datum == s.Montag);
        VertretungsVorschlag.VorziehungAusfuehren(s.D, offen, vz);
        // offene Stunde entfällt, die vorgezogene Stunde kommt hierher, ihr Ursprung entfällt mit „(Le.) nach“
        Assert.Equal(VertretungsArt.Entfall, offen.Art);
        var ziel = s.An(s.Montag, 2).Single(v => v.UnterrichtId == b.Id);
        Assert.Equal(VertretungsArt.Verlegung, ziel.Art);
        Assert.Equal(5, ziel.VonStunde);
        var q = Assert.Single(s.An(s.Montag, 5));
        Assert.Equal((VertretungsArt.Entfall, 2), (q.Art, q.NachStunde!.Value));
        Assert.Equal(3, s.D.Vertretungen.Count);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal(0, k.Offen);
        Assert.Equal(2, k.Vertretungen); // Verlegung (mit der verdeckten offenen Stunde) und Ursprung
        // bei erneuter Berechnung bleibt alles, wie es ist
        Vertretungen.Aktualisieren(s.D, s.Montag);
        Assert.Equal(3, s.D.Vertretungen.Count);
        // Rücknahme: die offene Vertretung ist wieder da, Ursprung und Ziel verschwinden
        Assert.Equal(1, Verlegungsaufhebung.Aufheben(s.D, ziel));
        var wieder = Assert.Single(s.D.Vertretungen);
        Assert.Equal(a.Id, wieder.UnterrichtId);
        Assert.True(wieder.Offen);
        Assert.Equal(1, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
    }

    [Fact]
    public void VerlegungEinerEntfallenenStundeErsetztDenEntfall()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Entfallen(s.D, u.Id, null, s.Montag, 2);
        var e = Assert.Single(s.D.Vertretungen);
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag.AddDays(1), 3);
        var q = Assert.Single(s.An(s.Montag, 2));
        Assert.Same(e, q);
        Assert.Equal((s.Montag.AddDays(1), 3), (q.NachDatum!.Value, q.NachStunde!.Value));
        var z = Assert.Single(s.An(s.Montag.AddDays(1), 3));
        Assert.Equal(VertretungsArt.Verlegung, z.Art);
        Assert.Equal(2, s.D.Vertretungen.Count);
        // zurück an den Ursprung: wieder der Entfall
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag.AddDays(1), 3, s.Montag, 2);
        var wieder = Assert.Single(s.D.Vertretungen);
        Assert.Equal(VertretungsArt.Entfall, wieder.Art);
        Assert.Null(wieder.NachDatum);
    }

    [Fact]
    public void VerlegungEinerVertretenenStundeErsetztDieVertretung()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        s.Absent(0, s.Montag);
        var v = Assert.Single(s.D.Vertretungen);
        Vertretungen.VertreterSetzen(s.D, v, s.L[2].Id);
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag.AddDays(1), 3);
        var q = Assert.Single(s.An(s.Montag, 2));
        Assert.Same(v, q);
        Assert.Equal(VertretungsArt.Entfall, q.Art);
        Assert.Null(q.VertreterId);
        Assert.Equal(3, q.NachStunde);
        var z = Assert.Single(s.An(s.Montag.AddDays(1), 3));
        Assert.Equal(s.L[0].Id, z.VertreterId); // am Folgetag ist die Lehrkraft wieder da
        Assert.Equal(2, s.D.Vertretungen.Count);
        Vertretungen.Aktualisieren(s.D, s.Montag);
        Assert.Equal(2, s.D.Vertretungen.Count);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((1, 0, 1), (k.Vertretungen, k.Offen, k.Entfaelle));
    }

    [Fact]
    public void ErneuteVerlegungVerschiebtDasZielOhneNeueZeilen()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag, 5);
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 5, s.Montag, 7);
        Assert.Equal(2, s.D.Vertretungen.Count);
        var q = Assert.Single(s.An(s.Montag, 2));
        Assert.Equal(7, q.NachStunde);
        var z = Assert.Single(s.An(s.Montag, 7));
        Assert.Equal(2, z.VonStunde);
        Assert.Empty(s.An(s.Montag, 5));
    }

    [Fact]
    public void RuecknahmeStelltOffeneVertretungAmUrsprungWiederHer()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        s.Absent(0, s.Montag);
        Assert.Equal(1, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag.AddDays(1), 3);
        Assert.Equal(0, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
        Assert.Equal(1, Kalenderkennzahlen.Tag(s.D, s.Montag).Entfaelle);
        // Verlegung aufheben: wieder offene Vertretung, keine Entfall-Zeile
        var ziel = s.D.Vertretungen.Single(v => v.VonDatum.HasValue);
        Assert.Equal(1, Verlegungsaufhebung.Aufheben(s.D, ziel));
        var o = Assert.Single(s.D.Vertretungen);
        Assert.True(o.Offen);
        Assert.Null(o.NachDatum);
        Assert.Null(o.VerlegungsId);
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((1, 1, 0), (k.Vertretungen, k.Offen, k.Entfaelle));
        Assert.Empty(s.An(s.Montag.AddDays(1), 3));
    }

    [Fact]
    public void AufhebenEinerVerlegtenEntfallstundeLaesstDenEntfallStehen()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Entfallen(s.D, u.Id, null, s.Montag, 2);
        Vertretungen.Verlegen(s.D, u.Id, null, s.Montag, 2, s.Montag, 6);
        var ziel = s.D.Vertretungen.Single(v => v.VonDatum.HasValue);
        Assert.Equal(1, Verlegungsaufhebung.Aufheben(s.D, ziel));
        var e = Assert.Single(s.D.Vertretungen);
        Assert.Equal(VertretungsArt.Entfall, e.Art);
        Assert.Equal("Entfall", Vertretungsanzeige.Nach(e));
        var k = Kalenderkennzahlen.Tag(s.D, s.Montag);
        Assert.Equal((1, 0, 1), (k.Vertretungen, k.Offen, k.Entfaelle));
    }

    [Fact]
    public void VorgezogeneStundeEntfallenLassenOeffnetDieGefuellteStundeWieder()
    {
        var s = new Schule();
        s.U(0, 0, (0, 2));
        var b = s.U(1, 0, (0, 5));
        s.Absent(0, s.Montag);
        var offen = Assert.Single(s.D.Vertretungen);
        var vz = VertretungsVorschlag.Vorziehungen(s.D, offen, VorziehungsModus.AusRandstunden, true).First(x => x.Eintrag.Unterricht?.Id == b.Id && x.Datum == s.Montag);
        VertretungsVorschlag.VorziehungAusfuehren(s.D, offen, vz);
        // die vorgezogene Stunde an ihrem Ziel entfallen lassen: ihr Ursprung bleibt Entfall (ohne „nach“), die offene Stunde ist wieder offen
        Vertretungen.Entfallen(s.D, b.Id, null, s.Montag, 2);
        Assert.True(offen.Offen);
        var q = Assert.Single(s.An(s.Montag, 5));
        Assert.Equal(VertretungsArt.Entfall, q.Art);
        Assert.Null(q.NachDatum);
        Assert.Equal(2, s.D.Vertretungen.Count);
    }

    [Fact]
    public void VerlegenEinerVorgezogenenStundeOeffnetDieGefuellteStunde()
    {
        var s = new Schule();
        s.U(0, 0, (0, 2));
        var b = s.U(1, 0, (0, 5));
        s.Absent(0, s.Montag);
        var offen = Assert.Single(s.D.Vertretungen);
        var vz = VertretungsVorschlag.Vorziehungen(s.D, offen, VorziehungsModus.AusRandstunden, true).First(x => x.Eintrag.Unterricht?.Id == b.Id && x.Datum == s.Montag);
        VertretungsVorschlag.VorziehungAusfuehren(s.D, offen, vz);
        // die vorgezogene Stunde weiter in die 7. Stunde: die 2. Stunde ist wieder offen, Ursprung zeigt auf die 7. Stunde
        Vertretungen.Verlegen(s.D, b.Id, null, s.Montag, 2, s.Montag, 7);
        Assert.True(offen.Offen);
        Assert.Equal(7, Assert.Single(s.An(s.Montag, 5)).NachStunde);
        Assert.Equal(3, s.D.Vertretungen.Count);
        // zurück an den Ursprung: nur die offene Vertretung bleibt
        Vertretungen.Verlegen(s.D, b.Id, null, s.Montag, 7, s.Montag, 5);
        Assert.Same(offen, Assert.Single(s.D.Vertretungen));
    }

    [Fact]
    public void ZiehenEinerEntfallenenStundeImPlanungsdialogVerlegtSieOhneDoppelteZeile()
    {
        var s = new Schule();
        var u = s.U(0, 0, (0, 2));
        Vertretungen.Entfallen(s.D, u.Id, null, s.Montag, 2);
        var a = Tagesverlegung.Analysieren(s.D, s.Montag, 2, u.Id, null, 1, s.Montag, 6);
        Assert.Single(a.Schritte);
        Tagesverlegung.Ausfuehren(s.D, a, VerlegeModus.Verlegen);
        var q = Assert.Single(s.An(s.Montag, 2));
        Assert.Equal((VertretungsArt.Entfall, 6), (q.Art, q.NachStunde!.Value));
        Assert.Equal(VertretungsArt.Verlegung, Assert.Single(s.An(s.Montag, 6)).Art);
    }

    [Fact]
    public void ZiehenInDieLueckeEinerKrankenLehrkraftUndAufhebenOeffnetSieWieder()
    {
        var s = new Schule();
        s.U(0, 0, (0, 2));
        var b = s.U(1, 0, (0, 5));
        s.Absent(0, s.Montag);
        var offen = Assert.Single(s.D.Vertretungen);
        var a = Tagesverlegung.Analysieren(s.D, s.Montag, 5, b.Id, null, 1, s.Montag, 2);
        Tagesverlegung.Ausfuehren(s.D, a, VerlegeModus.Verlegen);
        Assert.Equal(VertretungsArt.Entfall, offen.Art);
        Assert.Equal(0, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
        var ziel = s.D.Vertretungen.Single(v => v.VonDatum.HasValue);
        Verlegungsaufhebung.Aufheben(s.D, ziel);
        Assert.True(Assert.Single(s.D.Vertretungen).Offen);
        Assert.Equal(1, Kalenderkennzahlen.Tag(s.D, s.Montag).Offen);
    }
}
