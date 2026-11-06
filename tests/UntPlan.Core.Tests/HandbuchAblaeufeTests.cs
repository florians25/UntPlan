using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Abläufe der Handbücher (Runde 3, Paket C): Entkoppeln und „Alle Entkoppeln“ (Stundenplan Kap. 3.3.5) sowie die Zeile „Verlegung“
/// einer durchgeführten Vorziehung in der Vertretungsliste (Vertretungsplanung Kap. 3.5).
/// </summary>
public class HandbuchAblaeufeTests
{
    private static Schuldatei Klein()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 1;
        d.Zeitraster.StundenProTag = 4;
        Dokument.Normalisieren(d);
        foreach (var k in new[] { "2a", "2b" }) d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = k });
        foreach (var l in new[] { "Cer", "Ander", "Callas" }) d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = l });
        foreach (var r in new[] { "R2a", "R2b", "Ps1" }) d.Raeume.Add(new Raum { Id = d.NeueId(), Kurzname = r });
        d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = "D" });
        return d;
    }

    /// <summary>Deutsch 2a,2b in drei Gruppen (Cer/R2a, Ander/R2b, Callas/Ps1) wie im Handbuch Kap. 3.3.3, eine verplante Stunde.</summary>
    private static Unterricht Kopplung(Schuldatei d)
    {
        var klassen = d.Klassen.Select(k => k.Id).ToList();
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 4 };
        for (var i = 0; i < 3; i++)
            u.Zeilen.Add(new Kopplungszeile { LehrerId = d.Lehrer[i].Id, FachId = d.Faecher[0].Id, KlassenIds = klassen.ToList(), FachraumId = d.Raeume[i].Id });
        d.Unterrichte.Add(u);
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = 0, Stunde = 1, Raeume = d.Raeume.Select(r => (int?)r.Id).ToList() });
        return u;
    }

    [Fact]
    public void EntkoppelnMachtGewaehlteZeilenZuEigenenUnterrichten()
    {
        var d = Klein();
        var u = Kopplung(d);
        var neue = Unterrichtswerkzeuge.Entkoppeln(d, u.Id, new[] { 1 });
        Assert.Single(neue);
        Assert.Equal(2, u.Zeilen.Count);
        Assert.Equal(new[] { d.Lehrer[0].Id, d.Lehrer[2].Id }, u.Zeilen.Select(z => z.LehrerId!.Value));
        var n = d.Unterrichte.Single(x => x.Id == neue[0]);
        Assert.Equal(d.Lehrer[1].Id, n.Zeilen.Single().LehrerId);
        Assert.Equal(4, n.Wochenstunden);
        // Räume der verplanten Stunde folgen den verbleibenden Kopplungszeilen; der neue Unterricht ist nicht verplant
        Assert.Equal(new int?[] { d.Raeume[0].Id, d.Raeume[2].Id }, d.Stundenplan.Single(p => p.UnterrichtId == u.Id).Raeume);
        Assert.DoesNotContain(d.Stundenplan, p => p.UnterrichtId == n.Id);
    }

    [Fact]
    public void EntkoppelnAllerZeilenLaesstDieErsteBeimAltenUnterricht()
    {
        var d = Klein();
        var u = Kopplung(d);
        var neue = Unterrichtswerkzeuge.Entkoppeln(d, u.Id, new[] { 0, 1, 2 });
        Assert.Equal(2, neue.Count);
        Assert.Equal(d.Lehrer[0].Id, u.Zeilen.Single().LehrerId);
        Assert.Equal(new[] { d.Lehrer[1].Id, d.Lehrer[2].Id }, neue.Select(id => d.Unterrichte.Single(x => x.Id == id).Zeilen.Single().LehrerId!.Value));
    }

    [Fact]
    public void AlleEntkoppelnLoestAuchKlassenkopplungenAuf()
    {
        var d = Klein();
        var u = Kopplung(d);
        var neue = Unterrichtswerkzeuge.AlleEntkoppeln(d, u.Id);
        // 3 Kopplungszeilen × 2 Klassen = 6 Unterrichte mit je einer Klasse und einer Lehrkraft
        Assert.Equal(5, neue.Count);
        var alle = neue.Select(id => d.Unterrichte.Single(x => x.Id == id)).Append(u).ToList();
        Assert.All(alle, x => Assert.True(x.Zeilen.Count == 1 && x.KlassenIds.Count() == 1));
        Assert.Equal(6, alle.Select(x => (x.Zeilen[0].LehrerId, x.Zeilen[0].KlassenIds[0])).Distinct().Count());
    }

    /// <summary>Beispielschule mit Vertretungszeitraster „Entfall ab der 6. Stunde“ und einer Absenz, für die eine Vorziehung möglich ist.</summary>
    private static (Schuldatei D, Vertretung Offen) MitVorziehung()
    {
        var d = Demo.Beispielschule();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 1 });
        d.AktivePeriode.Plan = erg.OrderBy(e => e.Kennzahlen.Bewertung).First().Plan;
        for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
            for (var s = 6; s <= d.Zeitraster.StundenProTag; s++) d.Vertretungseinstellungen.Zeitraster[t * 100 + s] = VertretungsRegel.Entfall;
        var montag = Kalender.NaechsterUnterrichtstag(d, new DateOnly(2026, 11, 16));
        foreach (var l in d.Lehrer)
            for (var i = 0; i < 5; i++)
            {
                var t = montag.AddDays(i);
                if (!Kalender.IstUnterrichtstag(d, t)) continue;
                var a = new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = t, Bis = t };
                d.Abwesenheiten.Add(a);
                Vertretungen.Aktualisieren(d, t);
                var v = d.Vertretungen.Where(x => x.Datum == t && x.LehrerId == l.Id && x.Offen && x.Art == VertretungsArt.Vertretung)
                    .FirstOrDefault(x => VertretungsVorschlag.Vorziehungen(d, x).Count > 0);
                if (v != null) return (d, v);
                d.Abwesenheiten.Remove(a);
                Vertretungen.Aktualisieren(d, t);
            }
        throw new InvalidOperationException("Keine Vorziehung in der Beispielschule.");
    }

    [Fact]
    public void VorziehungStehtAlsEineZeileVerlegungMitDerAbwesendenLehrkraft()
    {
        var (d, offen) = MitVorziehung();
        var vz = VertretungsVorschlag.Vorziehungen(d, offen)[0];
        VertretungsVorschlag.VorziehungAusfuehren(d, offen, vz);
        var tag = Vertretungen.Am(d, offen.Datum);
        var paare = Vertretungen.ZusammengefassteZeilen(tag);
        // die vorgezogene Stunde (Verlegung, „Vertr. von“) verdeckt die gefüllte offene Stunde der Absenz: in Klammern die abwesende Lehrkraft
        var (zielId, verdeckt) = paare.Single(p => p.Value.Id == offen.Id);
        var ziel = tag.Single(v => v.Id == zielId);
        Assert.Equal(VertretungsArt.Verlegung, ziel.Art);
        Assert.Equal((vz.Datum, vz.Stunde), (ziel.VonDatum!.Value, ziel.VonStunde!.Value));
        Assert.Equal((offen.Datum, offen.Stunde), (ziel.Datum, ziel.Stunde));
        Assert.Equal(VertretungsArt.Entfall, verdeckt.Art);
        Assert.Null(verdeckt.NachDatum);
        // die Ursprungsposition der vorgezogenen Stunde bleibt eine eigene Entfall-Zeile mit „(Le.) nach“
        Assert.Contains(tag, v => v.Art == VertretungsArt.Entfall && v.NachDatum == offen.Datum && v.NachStunde == offen.Stunde && !paare.Values.Contains(v));
        // gerade bearbeitete Zeilen werden nicht verdeckt
        Assert.DoesNotContain(Vertretungen.ZusammengefassteZeilen(tag, v => v.Id == offen.Id).Values, v => v.Id == offen.Id);
    }

    [Fact]
    public void StundentauschVerdecktNurDieWegverlegtenStunden()
    {
        var (d, offen) = MitVorziehung();
        var tp = Tagesplan.Berechnen(d, offen.Datum);
        bool Frei(TagesEintrag e) => e.Status == TagesStatus.Regulaer && e.Unterricht is { Zeilen.Count: 1 } && e.KlassenIds.Count == 1;
        var paar = (from a in tp.Eintraege.Where(Frei)
                    from b in tp.Eintraege.Where(Frei)
                    where a.KlassenIds[0] == b.KlassenIds[0] && b.Stunde > a.Stunde && a.Unterricht!.Id != b.Unterricht!.Id
                    select (a, b)).First();
        Vertretungen.Tauschen(d, paar.a.Unterricht!.Id, offen.Datum, paar.a.Stunde, paar.b.Unterricht!.Id, offen.Datum, paar.b.Stunde);
        var tag = Vertretungen.Am(d, offen.Datum);
        var paare = Vertretungen.ZusammengefassteZeilen(tag);
        var tausch = tag.Where(v => v.Art == VertretungsArt.Tausch).ToList();
        Assert.Equal(2, tausch.Count);
        Assert.All(tausch, t => Assert.True(paare.TryGetValue(t.Id, out var q) && q.NachDatum.HasValue && q.Stunde == t.Stunde));
        // die offene Stunde der Absenz bleibt sichtbar
        Assert.DoesNotContain(paare.Values, v => v.Id == offen.Id);
    }
}
