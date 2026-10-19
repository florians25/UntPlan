using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Web.Contracts;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Räume beim Verlegen im tagesaktuellen Plan: „mitnehmen oder belassen?“, belegter bzw. gesperrter Zielraum.</summary>
public class VerlegungsraumTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Raum", "GY");
        public readonly List<Lehrer> L = new();
        public readonly List<Klasse> K = new();
        public readonly List<Raum> R = new();
        public readonly DateOnly Tag;

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear();
            D.Vertretungen.Clear(); D.Abwesenheiten.Clear(); D.Veranstaltungen.Clear();
            D.Faecher.Add(new Fach { Id = D.NeueId(), Kurzname = "M" });
            for (var i = 0; i < 6; i++)
            {
                L.Add(new Lehrer { Id = D.NeueId(), Kurzname = "L" + i, Wochensoll = 25 });
                R.Add(new Raum { Id = D.NeueId(), Kurzname = "R" + i, Kapazitaet = 30 });
            }
            for (var i = 0; i < 3; i++) K.Add(new Klasse { Id = D.NeueId(), Kurzname = $"{5 + i}a", StammraumId = R[i].Id });
            D.Lehrer.AddRange(L); D.Klassen.AddRange(K); D.Raeume.AddRange(R);
            var tag = Kalender.NaechsterUnterrichtstag(D, D.Einstellungen.SchuljahrVon.AddDays(20));
            while (Kalender.TagIndex(D, tag) != 0) tag = Kalender.NaechsterUnterrichtstag(D, tag);
            Tag = tag;
        }

        public Unterricht U(int l, int k, int raum, int stunde)
        {
            var u = new Unterricht { Id = D.NeueId(), Wochenstunden = 1 };
            u.Zeilen.Add(new Kopplungszeile { LehrerId = L[l].Id, FachId = D.Faecher[0].Id, KlassenIds = { K[k].Id } });
            D.Unterrichte.Add(u);
            D.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = stunde, Raeume = new List<int?> { R[raum].Id } });
            return u;
        }

        public List<int> RaeumeVon(int unterrichtId, int stunde) =>
            Tagesplan.Berechnen(D, Tag).Stunde(stunde).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == unterrichtId).SelectMany(e => e.RaumIds).ToList();
    }

    [Fact]
    public void AndererRaumAmZielFragtMitnehmenOderBelassen()
    {
        var s = new Schule();
        // 5a hat in der 1. Stunde Unterricht im Fachraum R4, in der 2. Stunde ist sie frei (Stammraum R0)
        var a = s.U(0, 0, 4, 1);
        var an = Tagesverlegung.Analysieren(s.D, s.Tag, 1, a.Id, null, 1, s.Tag, 2);
        var zeilen = Verlegungsraeume.Zeilen(s.D, an);
        Assert.True(Verlegungsraeume.MitnahmeFrageNoetig(zeilen));
        Assert.Equal(s.R[4].Id, zeilen[0].Quellraum);
        Assert.Equal(s.R[0].Id, zeilen[0].Zielraum);
        Assert.Contains("mitnehmen oder belassen", Verlegungsraeume.MitnahmeText(s.D, an, zeilen));
        Assert.Empty(Verlegungsraeume.Konflikte(s.D, an, VerlegeModus.Verlegen, RaumMitnahme.Mitnehmen, zeilen));

        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Mitnehmen));
        Assert.Equal(new[] { s.R[4].Id }, s.RaeumeVon(a.Id, 2));
    }

    [Fact]
    public void BelassenNimmtDenRaumDerNeuenZeit()
    {
        var s = new Schule();
        var a = s.U(0, 0, 4, 1);
        var an = Tagesverlegung.Analysieren(s.D, s.Tag, 1, a.Id, null, 1, s.Tag, 2);
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Belassen));
        Assert.Equal(new[] { s.R[0].Id }, s.RaeumeVon(a.Id, 2));
    }

    [Fact]
    public void GleicherRaumFragtNicht()
    {
        var s = new Schule();
        var a = s.U(0, 0, 0, 1);
        var an = Tagesverlegung.Analysieren(s.D, s.Tag, 1, a.Id, null, 1, s.Tag, 2);
        Assert.False(Verlegungsraeume.MitnahmeFrageNoetig(Verlegungsraeume.Zeilen(s.D, an)));
    }

    private static (Schule S, Unterricht A, Unterricht C, VerlegeAnalyse An) Belegt()
    {
        var s = new Schule();
        // 5a in R4 (1. Std.), wird in die 2. Std. verlegt; dort hat 6a (andere Lehrkraft) Unterricht im Raum R0 (Stammraum der 5a)
        var a = s.U(0, 0, 4, 1);
        var c = s.U(1, 1, 0, 2);
        var an = Tagesverlegung.Analysieren(s.D, s.Tag, 1, a.Id, null, 1, s.Tag, 2);
        Assert.False(an.Kollision);
        return (s, a, c, an);
    }

    [Fact]
    public void BelegterZielraumWirdErkannt()
    {
        var (s, _, c, an) = Belegt();
        var k = Verlegungsraeume.Konflikte(s.D, an, VerlegeModus.Verlegen, RaumMitnahme.Belassen);
        Assert.Single(k);
        Assert.Equal(s.R[0].Id, k[0].RaumId);
        Assert.Equal(c.Id, k[0].Belegung[0].Unterricht?.Id);
        Assert.True(Verlegungsraeume.AndereRaumvertretungMoeglich(k));
        Assert.Contains("Kollision", Verlegungsraeume.KonfliktText(s.D, an.Ziel, k));
    }

    [Fact]
    public void RaumvertretungFuerDieVerlegteStunde()
    {
        var (s, a, c, an) = Belegt();
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Belassen, RaumBelegtLoesung.EigeneRaumvertretung));
        var raum = Assert.Single(s.RaeumeVon(a.Id, 2));
        Assert.NotEqual(s.R[0].Id, raum);
        Assert.Equal(new[] { s.R[0].Id }, s.RaeumeVon(c.Id, 2));
    }

    [Fact]
    public void RaumvertretungFuerDenAnderenUnterricht()
    {
        var (s, a, c, an) = Belegt();
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Belassen, RaumBelegtLoesung.AndereRaumvertretung));
        Assert.Equal(new[] { s.R[0].Id }, s.RaeumeVon(a.Id, 2));
        var andere = Assert.Single(s.RaeumeVon(c.Id, 2));
        Assert.NotEqual(s.R[0].Id, andere);
        Assert.Contains(s.D.Vertretungen, v => v.UnterrichtId == c.Id && v.Datum == s.Tag && v.Stunde == 2 && v.VertretungsraumId == andere);
    }

    [Fact]
    public void KollisionLaesstBeideImRaum()
    {
        var (s, a, c, an) = Belegt();
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Belassen, RaumBelegtLoesung.Kollision));
        Assert.Equal(new[] { s.R[0].Id }, s.RaeumeVon(a.Id, 2));
        Assert.Equal(new[] { s.R[0].Id }, s.RaeumeVon(c.Id, 2));
    }

    [Fact]
    public void OhneFreienRaumBleibtDerRaumOffen()
    {
        var (s, a, c, an) = Belegt();
        // alle übrigen Räume sperren
        foreach (var r in s.R.Where(r => r.Id != s.R[0].Id))
            s.D.Abwesenheiten.Add(new Abwesenheit { Id = s.D.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = r.Id, Von = s.Tag, Bis = s.Tag });
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen, "", new VerlegeRaumWahl(RaumMitnahme.Belassen, RaumBelegtLoesung.EigeneRaumvertretung));
        Assert.Empty(s.RaeumeVon(a.Id, 2));
        Assert.Contains(s.D.Vertretungen, v => v.UnterrichtId == a.Id && v.Stunde == 2 && v.RaumOffen);
    }

    [Fact]
    public void GesperrterZielraumWirdErkannt()
    {
        var s = new Schule();
        var a = s.U(0, 0, 4, 1);
        s.D.Abwesenheiten.Add(new Abwesenheit { Id = s.D.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = s.R[0].Id, Von = s.Tag, Bis = s.Tag, StundeVon = 2, StundeBis = 2 });
        var an = Tagesverlegung.Analysieren(s.D, s.Tag, 1, a.Id, null, 1, s.Tag, 2);
        var k = Verlegungsraeume.Konflikte(s.D, an, VerlegeModus.Verlegen, RaumMitnahme.Belassen);
        Assert.True(Assert.Single(k).Gesperrt);
        Assert.False(Verlegungsraeume.AndereRaumvertretungMoeglich(k));
        Assert.Contains("gesperrt", Verlegungsraeume.KonfliktText(s.D, an.Ziel, k));
    }

    [Fact]
    public void OhneRaumwahlBleibtDasBisherigeVerhalten()
    {
        var (s, a, _, an) = Belegt();
        Tagesverlegung.Ausfuehren(s.D, an, VerlegeModus.Verlegen);
        // bisher: eigener Raum wird mitgenommen, wenn frei
        Assert.Equal(new[] { s.R[4].Id }, s.RaeumeVon(a.Id, 2));
    }

    [Fact]
    public void RaumbuchungInGesperrtemOderBelegtemRaumWirdGemeldet()
    {
        var s = new Schule();
        s.U(1, 1, 0, 2);
        s.D.Abwesenheiten.Add(new Abwesenheit { Id = s.D.NeueId(), Art = AbwesenheitsArt.Raum, ElementId = s.R[5].Id, Von = s.Tag, Bis = s.Tag });
        StplAenderung Buchung(string id, string raum) => new(new WebRueckmeldung { Id = id, Typ = WebRueckmeldungsTypen.Raumbuchung, Raum = raum, Von = s.Tag.ToString("yyyy-MM-dd"), StundeVon = 2 },
            s.Tag, 2, 2, "", StplAenderungsArt.Aktivitaet, null, null, "", "", raum, "", "", "", "", null, null);
        var hinweise = StundenplanAenderungen.RaumHinweise(s.D, new[] { Buchung("B1", "R0"), Buchung("B2", "R5"), Buchung("B3", "R3") });
        Assert.Equal(2, hinweise.Count);
        Assert.Contains(hinweise, h => h.StartsWith("R0") && h.Contains("belegt"));
        Assert.Contains(hinweise, h => h.StartsWith("R5") && h.Contains("gesperrt"));
        Assert.DoesNotContain(hinweise, h => h.StartsWith("R3"));
    }
}
