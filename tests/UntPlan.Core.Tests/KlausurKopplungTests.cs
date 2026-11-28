using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Prüfungen in gekoppelten Stunden gelten für die ganze Kopplung: Kopplung WW (eine Lehrkraft, drei Klassen mit je eigenem Raum).
/// Eine Prüfung, die nur für eine Klasse eingetragen bzw. aus UntPlanWeb gemeldet wird, umfasst alle drei Klassen; keine Zeile der
/// Kopplung wird zur „Klausuraufsicht“ (abgegebene Stunde ohne Lehrkraft), und UntPlanWeb erhält alle Klassen.
/// </summary>
public class KlausurKopplungTests
{
    private sealed class Schule
    {
        public readonly Schuldatei D = Demo.NeueDatei("Kopplung", "BK");
        public readonly Lehrer He, Andere;
        public readonly Klasse Et, Inf, Ing, Solo;
        public readonly Unterricht Ww, Einzeln;
        public readonly Raum Pruefraum;
        public readonly DateOnly Tag;

        public Schule()
        {
            D.Lehrer.Clear(); D.Klassen.Clear(); D.Faecher.Clear(); D.Raeume.Clear(); D.Unterrichte.Clear(); D.Stundenplan.Clear();
            He = new Lehrer { Id = D.NeueId(), Kurzname = "HE", Wochensoll = 25 };
            Andere = new Lehrer { Id = D.NeueId(), Kurzname = "AN", Wochensoll = 25 };
            Et = new Klasse { Id = D.NeueId(), Kurzname = "ET13" };
            Inf = new Klasse { Id = D.NeueId(), Kurzname = "INF13" };
            Ing = new Klasse { Id = D.NeueId(), Kurzname = "ING13" };
            Solo = new Klasse { Id = D.NeueId(), Kurzname = "7a" };
            var ww = new Fach { Id = D.NeueId(), Kurzname = "WW" };
            var m = new Fach { Id = D.NeueId(), Kurzname = "M" };
            var r = Enumerable.Range(0, 5).Select(i => new Raum { Id = D.NeueId(), Kurzname = "B11" + i }).ToList();
            Pruefraum = r[4];
            D.Lehrer.AddRange(new[] { He, Andere }); D.Klassen.AddRange(new[] { Et, Inf, Ing, Solo }); D.Faecher.AddRange(new[] { ww, m }); D.Raeume.AddRange(r);
            Ww = new Unterricht { Id = D.NeueId(), Wochenstunden = 2 };
            foreach (var k in new[] { Et, Inf, Ing }) Ww.Zeilen.Add(new Kopplungszeile { LehrerId = He.Id, FachId = ww.Id, KlassenIds = { k.Id } });
            Einzeln = new Unterricht { Id = D.NeueId(), Wochenstunden = 2 };
            Einzeln.Zeilen.Add(new Kopplungszeile { LehrerId = Andere.Id, FachId = m.Id, KlassenIds = { Solo.Id } });
            D.Unterrichte.AddRange(new[] { Ww, Einzeln });
            for (var s = 5; s <= 6; s++)
            {
                D.Stundenplan.Add(new Planstunde { UnterrichtId = Ww.Id, Tag = 0, Stunde = s, Raeume = new List<int?> { r[0].Id, r[1].Id, r[2].Id } });
                D.Stundenplan.Add(new Planstunde { UnterrichtId = Einzeln.Id, Tag = 0, Stunde = s, Raeume = new List<int?> { r[3].Id } });
            }
            Tag = Kalender.Montag(Kalender.NaechsterUnterrichtstag(D, D.Einstellungen.SchuljahrVon.AddDays(21)));
            while (!Kalender.IstUnterrichtstag(D, Tag)) Tag = Tag.AddDays(7);
        }

        public WebRueckmeldung Meldung(string id, Klasse k, Lehrer l) => new()
        {
            Id = id, Typ = WebRueckmeldungsTypen.Klausur, Lehrer = l.Kurzname, Von = Tag.ToString("yyyy-MM-dd"), StundeVon = 5, StundeBis = 6,
            Klassen = { k.Kurzname }, Raeume = { Pruefraum.Kurzname }, Titel = "Klausur", Erstellt = new DateTime(2026, 10, 14, 10, 0, 0),
        };

        /// <summary>Eine Teilprüfung wie früher: nur INF13 eingetragen, ohne Abgleich.</summary>
        public Klausur Teilpruefung()
        {
            var k = new Klausur { Id = D.NeueId(), Name = "Klausur", Datum = Tag, StundeVon = 5, StundeBis = 6, KlassenIds = { Inf.Id }, LehrerIds = { He.Id }, RaumIds = { Pruefraum.Id } };
            D.Klausuren.Add(k);
            return k;
        }

        public void PruefeGanzeKopplung(Klausur k)
        {
            Assert.Equal(new[] { Et.Id, Inf.Id, Ing.Id }.OrderBy(x => x), k.KlassenIds.OrderBy(x => x));
            var zeilen = D.Vertretungen.Where(v => v.Datum == Tag && v.UnterrichtId == Ww.Id).ToList();
            // keine Zeile der Kopplung geht als „Klausuraufsicht“ ohne Lehrkraft ab: alle gehören zur Prüfung
            Assert.DoesNotContain(zeilen, v => v.Text == "Klausuraufsicht");
            Assert.All(zeilen, v => Assert.Equal(k.Id, v.KlausurId));
            Assert.Equal(6, zeilen.Count); // 3 Zeilen × 2 Stunden
            // jede Klasse hat in beiden Stunden die Prüfung im tagesaktuellen Plan
            var tp = Tagesplan.Berechnen(D, Tag);
            foreach (var kl in new[] { Et, Inf, Ing })
                for (var s = 5; s <= 6; s++)
                    Assert.Contains(tp.Stunde(s), e => e.Vertretung is { Art: VertretungsArt.Klausur, KlausurId: int id } && id == k.Id && e.KlassenIds.Contains(kl.Id));
        }
    }

    [Fact]
    public void Import_aus_UntPlanWeb_fuer_eine_Klasse_umfasst_die_ganze_Kopplung()
    {
        var s = new Schule();
        var (ok, nein) = UntPlanWebExport.Uebernehmen(s.D, new[] { s.Meldung("p1", s.Inf, s.He) });
        Assert.Empty(nein);
        Assert.Single(ok);
        s.PruefeGanzeKopplung(Assert.Single(s.D.Klausuren));
        // erneuter Abruf derselben Meldung legt keine zweite Prüfung an und verkleinert die Klassen nicht
        UntPlanWebExport.Uebernehmen(s.D, new[] { s.Meldung("p1", s.Inf, s.He) });
        s.PruefeGanzeKopplung(Assert.Single(s.D.Klausuren));
    }

    [Fact]
    public void Im_Desktop_angelegte_Teilpruefung_wird_beim_Abgleich_erweitert()
    {
        var s = new Schule();
        var k = s.Teilpruefung();
        Vertretungen.Aktualisieren(s.D, s.Tag);
        s.PruefeGanzeKopplung(k);
    }

    [Fact]
    public void Aeltere_Teilpruefung_wird_beim_Laden_auf_die_Kopplung_erweitert()
    {
        var s = new Schule();
        s.Teilpruefung();
        Vertretungen.Aktualisieren(s.D, s.Tag);
        // Zustand älterer Dateien herstellen: Klassen zurück auf INF13, Zeilen wie früher (Klausuraufsicht für ET13/ING13)
        var k = Assert.Single(s.D.Klausuren);
        k.KlassenIds = new() { s.Inf.Id };
        var pfad = Path.Combine(Path.GetTempPath(), $"klausurkopplung_{Guid.NewGuid():N}.untplan");
        try
        {
            new Dokument(s.D, null).Speichern(pfad);
            var geladen = Dokument.Laden(pfad).Daten;
            var kg = Assert.Single(geladen.Klausuren);
            Assert.Equal(3, kg.KlassenIds.Count);
            Assert.DoesNotContain(geladen.Vertretungen, v => v.Datum == s.Tag && v.UnterrichtId == s.Ww.Id && v.Text == "Klausuraufsicht");
        }
        finally { File.Delete(pfad); File.Delete(pfad + ".bak"); }
    }

    [Fact]
    public void Export_an_UntPlanWeb_uebertraegt_alle_Klassen_der_Kopplung()
    {
        var s = new Schule();
        s.Teilpruefung();
        var p = UntPlanWebExport.PaketErstellen(s.D, s.Tag, new WebExportOptionen { TageZurueck = 0, TageVoraus = 1 });
        var termin = Assert.Single(p.Termine, t => t.Art == "klausur");
        Assert.Equal(new[] { "ET13", "INF13", "ING13" }, termin.Klassen.OrderBy(x => x));
        var tag = p.Tage.Single(t => t.Datum == s.Tag.ToString("yyyy-MM-dd"));
        // jede Klasse der Kopplung hat in der Prüfungszeit eine Stunde mit der Prüfung, keine bleibt als Unterricht ohne Prüfung stehen
        foreach (var kl in new[] { "ET13", "INF13", "ING13" })
            for (var st = 5; st <= 6; st++)
            {
                var stunden = tag.Stunden.Where(x => x.Stunde == st && x.Klassen.Contains(kl)).ToList();
                Assert.NotEmpty(stunden);
                Assert.All(stunden, x => Assert.NotNull(x.Klausur));
            }
    }

    [Fact]
    public void Pruefung_ohne_Kopplung_bleibt_unveraendert()
    {
        var s = new Schule();
        var k = new Klausur { Id = s.D.NeueId(), Name = "Klausur", Datum = s.Tag, StundeVon = 5, StundeBis = 5, KlassenIds = { s.Solo.Id }, LehrerIds = { s.Andere.Id } };
        s.D.Klausuren.Add(k);
        Vertretungen.Aktualisieren(s.D, s.Tag);
        Assert.Equal(new[] { s.Solo.Id }, k.KlassenIds);
        // die Kopplung WW zur selben Zeit ist nicht betroffen
        Assert.DoesNotContain(s.D.Vertretungen, v => v.Datum == s.Tag && v.UnterrichtId == s.Ww.Id);
    }
}
