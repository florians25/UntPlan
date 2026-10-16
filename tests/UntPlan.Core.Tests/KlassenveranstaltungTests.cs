using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Veranstaltung einer Klasse: Auch Unterricht, der nachträglich hinzukam (Sondereinsatz, Stunde ändern, zusätzliche Lehrkraft
/// einer Kopplung), entfällt im Veranstaltungszeitraum und wird so an UntPlanWeb übertragen.
/// </summary>
public class KlassenveranstaltungTests
{
    private static (Schuldatei d, DateOnly tag, RegelStunde r) Daten()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.KlassenIds.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId)
            && x.Unterricht.Zeilen.Count == 1);
        return (d, tag, r);
    }

    private static Veranstaltung Veranstaltung(Schuldatei d, DateOnly tag, int klasse, int von, int bis) => new()
    {
        Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = von, StundeBis = bis, KlassenIds = new() { klasse }, Text = "Bewerbungstraining",
    };

    /// <summary>Freie Stunde der Klasse an diesem Tag (für einen Sondereinsatz).</summary>
    private static int FreieStunde(Schuldatei d, DateOnly tag, int klasse)
    {
        var tp = Tagesplan.Berechnen(d, tag);
        return Enumerable.Range(1, d.Zeitraster.StundenProTag).First(s => !tp.VonKlasse(klasse).Any(e => e.Stunde == s));
    }

    [Fact]
    public void SondereinsatzImVeranstaltungszeitraumEntfaellt()
    {
        var (d, tag, r) = Daten();
        var klasse = r.KlassenIds[0];
        var s = FreieStunde(d, tag, klasse);
        var lehrer = d.Lehrer.First(l => l.Id != r.LehrerId).Id;
        var neu = Vertretungen.Sondereinsatz(d, tag, s, s, new[] { klasse }, new[] { lehrer }, r.FachId, null, "Zusatz").Single();
        var va = Veranstaltung(d, tag, klasse, 1, d.Zeitraster.StundenProTag);
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);

        Assert.Equal(VertretungsArt.Freisetzung, neu.Art);
        Assert.Equal(VertretungsArt.Sondereinsatz, neu.ArtVorKlassenabwesenheit);
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.Contains(tp.Stunde(s), e => e.Vertretung == neu && e.Status == TagesStatus.Entfall);
        Assert.DoesNotContain(tp.VonKlasse(klasse), e => e.Vertretung?.VeranstaltungId is null && e.Stunde == s);

        // Export: die Stunde erscheint als Entfall
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        var webTag = paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd"));
        Assert.Contains(webTag.Stunden, x => x.Vertretung == neu.Id && x.Status == "entfall");

        // Veranstaltung gelöscht: der Sondereinsatz findet wieder statt
        d.Veranstaltungen.Remove(va);
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(VertretungsArt.Sondereinsatz, neu.Art);
        Assert.Null(neu.ArtVorKlassenabwesenheit);
    }

    [Fact]
    public void GeaenderteStundeEntfaelltBeiVeranstaltungDerKlasse()
    {
        var (d, tag, r) = Daten();
        var andere = d.Lehrer.First(l => l.Id != r.LehrerId).Id;
        Vertretungen.StundeAendern(d, r.Unterricht.Id, r.ZeilenIndex, tag, r.Stunde, andere, r.FachId, r.KlassenIds.ToList(), r.RaumId, "");
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.UnterrichtId == r.Unterricht.Id && x.Stunde == r.Stunde);
        Assert.False(v.IstEntfall);

        d.Veranstaltungen.Add(Veranstaltung(d, tag, r.KlassenIds[0], r.Stunde, r.Stunde));
        Vertretungen.Aktualisieren(d, tag);
        Assert.True(v.IstEntfall);
        var e = Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Single(x => x.Regel?.Unterricht.Id == r.Unterricht.Id && x.Regel.ZeilenIndex == r.ZeilenIndex);
        Assert.Equal(TagesStatus.Entfall, e.Status);
        Assert.False(Tagesplan.Berechnen(d, tag).VonLehrer(andere).Any(x => x.Stunde == r.Stunde && x.Vertretung == v));
    }

    [Fact]
    public void HinzugefuegteLehrkraftOhneKlasseWirdFreigesetzt()
    {
        var (d, tag, r) = Daten();
        // zweite Kopplungszeile ohne eigene Klassen (z. B. „BZO bei BO hinzugefügt“)
        var zweite = d.Lehrer.First(l => l.Id != r.LehrerId).Id;
        var u = d.PeriodeAm(tag).Unterrichte?.First(x => x.Id == r.Unterricht.Id) ?? d.Unterrichte.First(x => x.Id == r.Unterricht.Id);
        u.Zeilen.Add(new Kopplungszeile { LehrerId = zweite, FachId = r.FachId });
        d.Veranstaltungen.Add(Veranstaltung(d, tag, r.KlassenIds[0], r.Stunde, r.Stunde));
        Vertretungen.NachPlanaenderung(d, tag);

        var am = d.Vertretungen.Where(v => v.Datum == tag && v.Stunde == r.Stunde && v.UnterrichtId == u.Id).ToList();
        Assert.Contains(am, v => v.ZeilenIndex == r.ZeilenIndex && v.Art == VertretungsArt.Freisetzung);
        Assert.Contains(am, v => v.ZeilenIndex == u.Zeilen.Count - 1 && v.LehrerId == zweite && v.Art == VertretungsArt.Freisetzung);
        Assert.DoesNotContain(Tagesplan.Berechnen(d, tag).Stunde(r.Stunde), e => e.Regel?.Unterricht.Id == u.Id && e.Findetstatt);
    }

    [Fact]
    public void AenderungImDokumentErkenntVeranstaltungSofort()
    {
        var (d, tag, r) = Daten();
        var klasse = r.KlassenIds[0];
        var s = FreieStunde(d, tag, klasse);
        d.Veranstaltungen.Add(Veranstaltung(d, tag, klasse, 1, d.Zeitraster.StundenProTag));
        Vertretungen.Aktualisieren(d, tag);
        var doc = new Dokument(d);
        doc.Aendern("Sondereinsatz", AenderungsArt.Vertretung, x => Vertretungen.Sondereinsatz(x, tag, s, s, new[] { klasse }, new[] { r.LehrerId!.Value }, r.FachId, null, "Zusatz"));
        var v = doc.Daten.Vertretungen.Single(x => x.Datum == tag && x.Stunde == s && x.ArtVorKlassenabwesenheit == VertretungsArt.Sondereinsatz);
        Assert.True(v.IstEntfall);
    }

    [Fact]
    public void BereitschaftAbwesenderLehrkraftWirdAlsEntfallUebertragen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        RegelStunde? ber = null;
        for (var i = 0; i < 10 && ber is null; i++)
        {
            ber = Tagesplan.Regulaer(d, tag).FirstOrDefault(x => x.LehrerId.HasValue && Vertretungen.IstBereitschaftsfach(d, x.FachId));
            if (ber is null) tag = Kalender.NaechsterUnterrichtstag(d, tag);
        }
        if (ber is null)
        {
            // Testdaten ohne Bereitschaft: eine Bereitschaft anlegen
            var f = d.Faecher.First();
            f.Bereitschaft = true;
            ber = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.FachId == f.Id);
        }
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = ber.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.Single(x => x.Datum == tag && x.UnterrichtId == ber.Unterricht.Id && x.Stunde == ber.Stunde && x.ZeilenIndex == ber.ZeilenIndex);
        Assert.Equal(VertretungsArt.BereitschaftEntfaellt, v.Art);
        var paket = UntPlanWebExport.PaketErstellen(d, tag);
        var st = paket.Tage.Single(t => t.Datum == tag.ToString("yyyy-MM-dd")).Stunden.Single(x => x.Vertretung == v.Id);
        Assert.Equal("entfall", st.Status);
        Assert.True(st.Bereitschaft);
    }
}
