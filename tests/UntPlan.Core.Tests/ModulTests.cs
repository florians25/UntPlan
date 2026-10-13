using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using Xunit;
using PortalClient = UntPlan.Core.Austausch.Portal;

namespace UntPlan.Core.Tests;

/// <summary>Gemeinsame Testdaten: Beispielschule mit fertigem Stundenplan.</summary>
internal static class Testdaten
{
    private static readonly Lazy<string> Json = new(() =>
    {
        var d = Demo.Beispielschule();
        d.AktivePeriode.Plan = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
        return System.Text.Json.JsonSerializer.Serialize(d, Dokument.JsonOptionen);
    });

    public static Schuldatei Verplant()
    {
        var d = System.Text.Json.JsonSerializer.Deserialize<Schuldatei>(Json.Value, Dokument.JsonOptionen)!;
        Dokument.Normalisieren(d);
        return d;
    }

    public static DateOnly Unterrichtstag(Schuldatei d) => Kalender.NaechsterUnterrichtstag(d, d.Einstellungen.SchuljahrVon.AddDays(20));
}

public class VertretungTests
{
    [Fact]
    public void AbwesenheitErzeugtOffeneVertretungen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var ix = new DatenIndex(d);
        var plan = Tagesplan.Regulaer(d, tag);
        var lehrer = plan.Where(x => !Vertretungen.IstBereitschaft(ix, x.Unterricht) && x.LehrerId.HasValue).GroupBy(x => x.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
        var stunden = plan.Count(x => x.LehrerId == lehrer && !Vertretungen.IstBereitschaft(ix, x.Unterricht));
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var offen = d.Vertretungen.Where(v => v.Datum == tag && v.Offen).ToList();
        Assert.True(stunden > 0);
        Assert.Equal(stunden, offen.Count(v => v.LehrerId == lehrer));

        // Erneutes Aktualisieren erzeugt keine Duplikate
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal(offen.Count, d.Vertretungen.Count(v => v.Datum == tag && v.Offen));

        // Abwesenheit entfernt -> offene Vertretungen verschwinden
        d.Abwesenheiten.Clear();
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag);
    }

    [Fact]
    public void VorschlagEnthaeltNurFreieLehrerUndBevorzugtBereitschaft()
    {
        var d = Testdaten.Verplant();
        var ix = new DatenIndex(d);
        // Einen Tag mit Bereitschaftsstunde suchen
        foreach (var tag in Kalender.Tage(d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrVon.AddDays(30)).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var plan = Tagesplan.Regulaer(d, tag);
            var ber = plan.FirstOrDefault(x => Vertretungen.IstBereitschaft(ix, x.Unterricht));
            if (ber is null) continue;
            var stunde = ber.Stunde;
            var bereit = ber.LehrerId!.Value;
            var opfer = plan.Where(x => x.Stunde == stunde && !Vertretungen.IstBereitschaft(ix, x.Unterricht) && x.Unterricht.Zeilen.Count == 1)
                .Select(x => x.LehrerId).OfType<int>().FirstOrDefault(l => l != bereit);
            if (opfer == 0) continue;
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = opfer, Von = tag, Bis = tag });
            Vertretungen.Aktualisieren(d, tag);
            var v = d.Vertretungen.First(x => x.Datum == tag && x.Stunde == stunde && x.LehrerId == opfer);
            var kand = VertretungsVorschlag.Berechnen(d, v);
            Assert.NotEmpty(kand);
            var tp = Tagesplan.Berechnen(d, tag);
            Assert.All(kand, k => Assert.Null(Vertretungen.LehrerBelegt(d, tp, k.Lehrer.Id, stunde, v)));
            Assert.Equal(bereit, kand[0].Lehrer.Id);
            Assert.False(kand[0].Mehrarbeit);

            Assert.True(VertretungsVorschlag.Automatisch(d, tag) > 0);
            Assert.DoesNotContain(d.Vertretungen, x => x.Datum == tag && x.Offen && x.Stunde == stunde);
            return;
        }
        Assert.Fail("Kein Tag mit Bereitschaft gefunden");
    }

    [Fact]
    public void KlassenabwesenheitSetztLehrerFrei()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var klasse = d.Klassen.First(k => k.Kurzname == "5a");
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = klasse.Id, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var frei = d.Vertretungen.Where(v => v.Datum == tag && v.Art == VertretungsArt.Freisetzung).ToList();
        Assert.NotEmpty(frei);
        var f = frei.First(v => v.LehrerId.HasValue);
        Assert.True(Vertretungen.IstFreigesetzt(d, f.LehrerId!.Value, tag, f.Stunde));
        Assert.True(Statistik.Saldo(d, d.Lehrer.First(l => l.Id == f.LehrerId), tag, tag) < 0);
    }

    [Fact]
    public void NrwMehrarbeitWirdErstUeberDerMonatsgrenzeVerguetet()
    {
        var d = Testdaten.Verplant();
        var l = d.Lehrer[0];
        var tag = new DateOnly(2026, 11, 2);
        for (var i = 0; i < 3; i++)
            d.Vertretungen.Add(new Vertretung { Id = d.NeueId(), Datum = tag.AddDays(i), Stunde = 1, Art = VertretungsArt.Vertretung, VertreterId = l.Id, Mehrarbeit = true });
        var z = Statistik.Abrechnung(d, tag, tag.AddDays(27)).Single(x => x.Lehrer.Id == l.Id);
        Assert.Equal(3, z.Mehrarbeit);
        Assert.Equal(0, z.Verguetungsfaehig);
        d.Vertretungen.Add(new Vertretung { Id = d.NeueId(), Datum = tag.AddDays(5), Stunde = 2, Art = VertretungsArt.Vertretung, VertreterId = l.Id, Mehrarbeit = true });
        z = Statistik.Abrechnung(d, tag, tag.AddDays(27)).Single(x => x.Lehrer.Id == l.Id);
        Assert.Equal(4, z.Verguetungsfaehig);
    }

    [Fact]
    public void VertretungsplanHtmlEnthaeltEintraege()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = d.Lehrer[0].Id, Von = tag, Bis = tag });
        VertretungsVorschlag.Automatisch(d, tag);
        var html = Export.VertretungsplanHtml(d, new[] { tag }, lehrerversion: true);
        Assert.Contains("<table", html);
        Assert.Contains(d.Lehrer[0].Kurzname, html);
    }
}

public class PlanungTests
{
    [Fact]
    public void VerplanenVerschiebenEntplanen()
    {
        var d = Testdaten.Verplant();
        var p = d.Stundenplan.First(x => !x.Fixiert);
        var u = d.Unterrichte.First(x => x.Id == p.UnterrichtId);
        Planungsdienst.Entplanen(d, p);
        Assert.DoesNotContain(p, d.Stundenplan);
        var ix = new DatenIndex(d);
        // freies Feld suchen
        var ziel = Enumerable.Range(0, d.Zeitraster.TageProWoche).SelectMany(t => Enumerable.Range(1, 6).Select(s => (t, s)))
            .First(x => Planungsdienst.Status(d, ix, u, x.t, x.s) == Feldstatus.Frei);
        var neu = Planungsdienst.Verplanen(d, u, ziel.t, ziel.s, false);
        Assert.Contains(neu, d.Stundenplan);
        Assert.DoesNotContain(Bewertung.Verstoesse(d, d.Stundenplan), v => (v.Art is VerstossArt.Lehrerkollision or VerstossArt.Klassenkollision or VerstossArt.Raumkollision));
    }

    [Fact]
    public void KollisionWirdErkannt()
    {
        var d = Testdaten.Verplant();
        var ix = new DatenIndex(d);
        var p = d.Stundenplan.First(x => string.IsNullOrEmpty(ix.Unterrichte[x.UnterrichtId].Band));
        var u = ix.Unterrichte[p.UnterrichtId];
        var andere = d.Stundenplan.First(x => x.UnterrichtId != p.UnterrichtId && ix.Unterrichte[x.UnterrichtId].KlassenIds.Intersect(u.KlassenIds).Any() && (x.Tag != p.Tag || x.Stunde != p.Stunde));
        Assert.Contains(Planungsdienst.Status(d, ix, u, andere.Tag, andere.Stunde, 1, new[] { p }), new[] { Feldstatus.Kollision, Feldstatus.TauschMoeglich });
        Assert.NotEmpty(Planungsdienst.Hindernisse(d, ix, u, andere.Tag, andere.Stunde));
    }

    [Fact]
    public void TauschvorschlaegeSindKollisionsfrei()
    {
        var d = Testdaten.Verplant();
        var p = d.Stundenplan.First(x => !x.Fixiert);
        foreach (var t in Planungsdienst.Tauschvorschlaege(d, p, 5))
        {
            var kopie = Dokument.Klonen(d);
            var a = kopie.Stundenplan.First(x => x.UnterrichtId == t.A.UnterrichtId && x.Tag == t.A.Tag && x.Stunde == t.A.Stunde);
            Planungsdienst.Verschieben(kopie, a, t.B.Tag, t.B.Stunde, true, false);
            Assert.DoesNotContain(Bewertung.Verstoesse(kopie, kopie.Stundenplan), v => (v.Art is VerstossArt.Lehrerkollision or VerstossArt.Klassenkollision or VerstossArt.Raumkollision));
        }
    }
}

public class AustauschTests
{
    [Fact]
    public void GpuExportUndImportErgebenGleicheStammdaten()
    {
        var d = Testdaten.Verplant();
        var gpu = Export.Gpu(d);
        var neu = Demo.NeueDatei("Import", "GY");
        var b = Import.Gpu(neu, gpu);
        Assert.Equal(d.Lehrer.Count, neu.Lehrer.Count);
        Assert.Equal(d.Klassen.Count, neu.Klassen.Count);
        Assert.Equal(d.Raeume.Count, neu.Raeume.Count);
        Assert.Equal(d.Unterrichte.Sum(u => u.Wochenstunden), neu.Unterrichte.Sum(u => u.Wochenstunden));
        Assert.True(b.Neu > 0);
    }

    [Fact]
    public void SchildLehrerCsvWirdImportiert()
    {
        var d = Demo.NeueDatei("Test", "GY");
        var csv = "Kürzel;Nachname;Vorname;Pflichtstunden;Fächer\nABC;Albers;Anna;25,5;D,E\nXYZ;Zimmer;Xaver;12;M\n";
        var b = Import.LehrerCsv(d, csv);
        Assert.Equal(2, b.Neu);
        var l = d.Lehrer.Single(x => x.Kurzname == "ABC");
        Assert.Equal(25.5m, l.Wochensoll);
        // erneuter Import aktualisiert
        Assert.Equal(2, Import.LehrerCsv(d, csv).Aktualisiert);
    }

    [Fact]
    public void DateiSpeichernUndLaden()
    {
        var d = Testdaten.Verplant();
        var pfad = Path.Combine(Path.GetTempPath(), $"untplan_test_{Guid.NewGuid():N}{Dokument.Dateiendung}");
        try
        {
            new Dokument(d).Speichern(pfad);
            var g = Dokument.Laden(pfad).Daten;
            Assert.Equal(d.Unterrichte.Count, g.Unterrichte.Count);
            Assert.Equal(d.Stundenplan.Count, g.Stundenplan.Count);
            Assert.Equal(d.Einstellungen.Schulname, g.Einstellungen.Schulname);
        }
        finally
        {
            File.Delete(pfad);
            File.Delete(pfad + ".bak");
        }
    }

    [Fact]
    public void PortalPaketUndMeldungen()
    {
        var d = Testdaten.Verplant();
        var paket = PortalClient.PaketErstellen(d, Testdaten.Unterrichtstag(d));
        Assert.Equal(PortalClient.Schema, paket.Schema);
        Assert.Equal(d.Stundenplan.Count, paket.Stundenplan.Length);
        var l = d.Lehrer[0];
        var (ok, nein) = PortalClient.MeldungenUebernehmen(d, new[]
        {
            new PortalClient.Meldung("1", "abwesenheit", DateTime.Now, l.Kurzname, l.Kurzname, "2026-11-03", "2026-11-04", null, null, "Krankheit", "krank"),
            new PortalClient.Meldung("2", "abwesenheit", DateTime.Now, "???", "???", "2026-11-03", null, null, null, null, null),
        });
        Assert.Equal(new[] { "1" }, ok);
        Assert.Equal(new[] { "2" }, nein);
        var a = d.Abwesenheiten.Single();
        Assert.Equal(new DateOnly(2026, 11, 3), a.Von);
        Assert.Equal(new DateOnly(2026, 11, 4), a.Bis);
    }

    [Fact]
    public void ICalEnthaeltTermine()
    {
        var d = Testdaten.Verplant();
        var l = d.Lehrer.First(x => d.Unterrichte.Any(u => u.LehrerIds.Contains(x.Id)));
        var von = Testdaten.Unterrichtstag(d);
        var ics = Export.ICal(d, l.Id, von, von.AddDays(6));
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
    }
}

public class ModulTests
{
    [Fact]
    public void BandbildungOhneKollisionen()
    {
        var d = Testdaten.Verplant();
        var ef = d.Klassen.First(k => k.Jahrgang == "EF");
        var kurse = Kursplanung.Kurse(d, ef.Id);
        Assert.NotEmpty(kurse);
        var z = Kursplanung.Bandbildung(d, kurse, 8);
        Assert.Equal(kurse.Count, z.Count);
        Kursplanung.BaenderUebernehmen(d, "T", z);
        Assert.Equal(0, Kursplanung.Kollisionen(d, Kursplanung.Kurse(d, ef.Id)));
    }

    [Fact]
    public void PausenaufsichtenDeckenBedarf()
    {
        var d = Testdaten.Verplant();
        Assert.NotEmpty(d.Gaenge);
        var n = Pausenaufsichten.Optimieren(d);
        Assert.True(n > 0);
        var ix = new DatenIndex(d);
        foreach (var a in d.Pausenaufsichten)
            Assert.True(Pausenaufsichten.Moeglich(d, ix, ix.Lehrer[a.LehrerId], a.Tag, a.NachStunde));
        // kein Lehrer doppelt in derselben Pause
        Assert.DoesNotContain(d.Pausenaufsichten.GroupBy(a => (a.Tag, a.NachStunde, a.LehrerId)), g => g.Count() > 1);
    }

    [Fact]
    public void WertrechnungSollIst()
    {
        var d = Testdaten.Verplant();
        var w = Wertrechnung.Uebersicht(d);
        Assert.Equal(d.Lehrer.Count, w.Count);
        var l = w.First(x => x.Wochenstunden > 0);
        Assert.Equal(l.Ist - l.Soll, l.Differenz);
        Assert.True(l.Ist > 0);
    }

    [Fact]
    public void NrwFerienUndFeiertage()
    {
        var d = Demo.NeueDatei("Test", "GY");
        Nrw.FerienEintragen(d);
        Assert.Contains(d.Ferien, f => f.Feiertag && f.Von.Month == 10 && f.Von.Day == 3);
        var weihnachten = new DateOnly(d.Einstellungen.SchuljahrVon.Year, 12, 24);
        Assert.False(Kalender.IstUnterrichtstag(d, weihnachten));
        Assert.Equal(new DateOnly(2027, 3, 28), Nrw.Ostersonntag(2027));
    }

    [Fact]
    public void DiagnoseLiefertPunkte()
    {
        var d = Testdaten.Verplant();
        Assert.NotEmpty(Diagnose.Eingabedaten(d));
        Assert.NotNull(Diagnose.Stundenplan(d));
    }
}
