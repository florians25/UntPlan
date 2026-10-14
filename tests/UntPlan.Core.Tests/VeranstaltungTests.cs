using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Veranstaltungen (Klasse, Kl-Teil, Schülergruppe), Lehrkraft fällt für eine Veranstaltung aus, Absenzen anlegen/ändern/löschen.</summary>
public class VeranstaltungTests
{
    private static (Schuldatei d, DateOnly tag) Daten()
    {
        var d = Testdaten.Verplant();
        return (d, Testdaten.Unterrichtstag(d));
    }

    /// <summary>Regulärer gekoppelter Unterricht mit zwei Lehrkräften und denselben Klassen (z. B. Sport getrennt).</summary>
    private static List<RegelStunde>? Kopplung(Schuldatei d, DateOnly tag)
    {
        foreach (var g in Tagesplan.Regulaer(d, tag).Where(r => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId))
                     .GroupBy(r => (r.Unterricht.Id, r.Stunde)))
        {
            var l = g.ToList();
            if (l.Count >= 2 && l.Select(x => x.LehrerId).Distinct().Count() >= 2 && l.All(x => x.KlassenIds.Count > 0)) return l;
        }
        return null;
    }

    private static List<RegelStunde> Kopplung(Schuldatei d, ref DateOnly tag)
    {
        for (var i = 0; i < 10; i++)
        {
            if (Kopplung(d, tag) is { } k) return k;
            tag = Kalender.NaechsterUnterrichtstag(d, tag);
        }
        throw new InvalidOperationException("Keine Kopplung in den Testdaten.");
    }

    [Fact]
    public void KlasseMitSchuelergruppeSetztAnderenLehrerFrei()
    {
        var (d, tag) = Daten();
        var k = Kopplung(d, ref tag);
        var a = k[0];
        var b = k.First(x => x.LehrerId != a.LehrerId);
        a.Unterricht.Zeilen[a.ZeilenIndex].Schuelergruppe = "SportM";
        b.Unterricht.Zeilen[b.ZeilenIndex].Schuelergruppe = "SportK";
        var klassen = a.KlassenIds.Union(b.KlassenIds).ToList();
        d.Veranstaltungen.Add(new Veranstaltung
        {
            Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = a.Stunde, StundeBis = a.Stunde, KlassenIds = klassen,
            LehrerIds = new() { b.LehrerId!.Value }, Schuelergruppe = "SportK", Text = "Tennis",
        });
        Vertretungen.Aktualisieren(d, tag);
        var am = d.Vertretungen.Where(v => v.Datum == tag && v.Stunde == a.Stunde).ToList();
        Assert.Contains(am, v => v.Art == VertretungsArt.Veranstaltung && v.VertreterId == b.LehrerId);
        // der andere Lehrer wird freigesetzt
        Assert.Contains(am, v => v.Art == VertretungsArt.Freisetzung && v.LehrerId == a.LehrerId && v.UnterrichtId == a.Unterricht.Id);
        // der Unterricht des Veranstaltungslehrers findet als Veranstaltung statt (keine offene Vertretung, nicht in der Liste)
        var eigen = am.Single(v => v.LehrerId == b.LehrerId && v.UnterrichtId == b.Unterricht.Id && v.ZeilenIndex == b.ZeilenIndex);
        Assert.True(eigen.InVeranstaltung);
        Assert.False(eigen.Offen);
    }

    [Fact]
    public void KlTeilLaesstRegulaerenUnterrichtStattfinden()
    {
        var (d, tag) = Daten();
        var k = Kopplung(d, ref tag);
        var a = k[0];
        var b = k.First(x => x.LehrerId != a.LehrerId);
        a.Unterricht.Zeilen[a.ZeilenIndex].Schuelergruppe = "SportM";
        b.Unterricht.Zeilen[b.ZeilenIndex].Schuelergruppe = "SportK";
        d.Veranstaltungen.Add(new Veranstaltung
        {
            Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = a.Stunde, StundeBis = a.Stunde, KlassenteilIds = a.KlassenIds.ToList(),
            LehrerIds = new() { a.LehrerId!.Value }, Schuelergruppe = "SportM", Text = "Fußball",
        });
        Vertretungen.Aktualisieren(d, tag);
        var am = d.Vertretungen.Where(v => v.Datum == tag && v.Stunde == a.Stunde).ToList();
        // Klassen nicht abwesend: keine Freisetzung, der Unterricht der anderen Lehrkraft findet statt
        Assert.DoesNotContain(am, v => v.Art == VertretungsArt.Freisetzung);
        Assert.DoesNotContain(am, v => v.LehrerId == b.LehrerId && v.UnterrichtId == b.Unterricht.Id);
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.Contains(tp.Stunde(a.Stunde), e => e.Status == TagesStatus.Regulaer && e.LehrerIds.Contains(b.LehrerId!.Value));
        // Teil der Veranstaltungslehrkraft wird zur Veranstaltung, nicht zur offenen Vertretung
        Assert.Contains(am, v => v.Art == VertretungsArt.Veranstaltung && v.VertreterId == a.LehrerId && v.KlassenIds.SequenceEqual(a.KlassenIds));
        Assert.DoesNotContain(am, v => v.Offen);
    }

    [Fact]
    public void KlTeilMitFremderSchuelergruppeMachtAnderenUnterrichtDerLehrkraftOffen()
    {
        var (d, tag) = Daten();
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.KlassenIds.Count > 0 && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        var andereKlasse = d.Klassen.First(c => !r.KlassenIds.Contains(c.Id));
        d.Veranstaltungen.Add(new Veranstaltung
        {
            Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = r.Stunde, StundeBis = r.Stunde, KlassenteilIds = new() { andereKlasse.Id },
            LehrerIds = new() { r.LehrerId!.Value }, Schuelergruppe = "Projekt_neu", Text = "Wirtschaftsprojekt",
        });
        Vertretungen.Aktualisieren(d, tag);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.UnterrichtId == r.Unterricht.Id && v.Offen);
    }

    [Fact]
    public void AbsenzUeberlapptMitVeranstaltungUndLehrerWirdEntfernt()
    {
        var (d, tag) = Daten();
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren).Take(3).Select(l => l.Id).ToList();
        var klasse = d.Klassen.First();
        var bis = Kalender.NaechsterUnterrichtstag(d, Kalender.NaechsterUnterrichtstag(d, tag));
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = bis, KlassenIds = new() { klasse.Id }, LehrerIds = lehrer.ToList(), Text = "Sportwoche" };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag, bis);
        var curie = lehrer[0];
        // Absenz nur am ersten Tag
        var abs = AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = curie, Von = tag, Bis = tag } }, Array.Empty<Abwesenheit>()).Single();
        var ue = VeranstaltungsLogik.Ueberlappungen(d, abs);
        var u = Assert.Single(ue);
        Assert.Equal(va.Id, u.Veranstaltung.Id);
        Assert.Equal(new[] { tag }, u.Tage);
        var frage = VeranstaltungsLogik.FrageText(d, abs, u);
        Assert.Contains($"Absenz {abs.Id}:", frage);
        Assert.Contains("überlappt mit", frage);
        Assert.Contains($"Veranstaltung {va.Id}:", frage);
        Assert.EndsWith($"Soll Lehrer {d.Lehrer.First(l => l.Id == curie).Kurzname} aus der Veranstaltung entfernt werden?", frage);
        VeranstaltungsLogik.LehrerEntfernen(d, va.Id, curie, u.Tage, abs.Id);
        Assert.Contains($"am {tag:dd.MM.yyyy} entfernt.", VeranstaltungsLogik.HinweisText(d, u));
        // Vertretungsliste: Veranstaltung mit „?“ und offen
        Assert.EndsWith(",?", VeranstaltungsLogik.LehrerText(d, va, tag));
        Assert.DoesNotContain("?", VeranstaltungsLogik.LehrerText(d, va, bis));
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.VeranstaltungId == va.Id && v.VeranstaltungOffen && v.ErsatzFuerLehrerId == curie);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.VeranstaltungId == va.Id && v.VertreterId == curie);
        Assert.True(VeranstaltungsLogik.HatOffenePlaetze(va, tag, tag));
        Assert.False(VeranstaltungsLogik.HatOffenePlaetze(va, bis, bis));
        // an den anderen Tagen bleibt sie in der Veranstaltung
        Assert.Contains(d.Vertretungen, v => v.Datum == bis && v.VeranstaltungId == va.Id && v.VertreterId == curie);
        // erneute Prüfung: keine Überlappung mehr
        Assert.Empty(VeranstaltungsLogik.Ueberlappungen(d, abs));
        // Ersatzlehrkraft eintragen → nicht mehr offen
        var ersatz = d.Lehrer.First(l => !lehrer.Contains(l.Id) && !l.Ignorieren).Id;
        Assert.True(VeranstaltungsLogik.ErsatzSetzen(d, va.Id, tag, null, ersatz));
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.VeranstaltungOffen);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.VeranstaltungId == va.Id && v.VertreterId == ersatz);
        Assert.True(Absenzen.LehrerAbwesend(d, ersatz, tag, va.StundeVon));
        // Absenz gelöscht → Lehrkraft nimmt wieder teil
        AbsenzVerwaltung.Loeschen(d, new[] { abs });
        Assert.Empty(va.Ersatz);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.VeranstaltungId == va.Id && v.VertreterId == curie);
    }

    [Fact]
    public void VeranstaltungslehrerIstKeineKollision()
    {
        var (d, tag) = Daten();
        var l = d.Lehrer.First(x => !x.Ignorieren).Id;
        d.Veranstaltungen.Add(new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, LehrerIds = new() { l }, Text = "Fortbildung" });
        Vertretungen.Aktualisieren(d, tag);
        var tp = Tagesplan.Berechnen(d, tag);
        foreach (var v in d.Vertretungen.Where(v => v.Datum == tag && v.Art == VertretungsArt.Veranstaltung))
            Assert.Null(Vertretungen.LehrerBelegt(d, tp, l, v.Stunde, v));
    }

    [Fact]
    public void VeranstaltungKopieren()
    {
        var va = new Veranstaltung { Id = 5, Von = new DateOnly(2025, 1, 10), Bis = new DateOnly(2025, 1, 11), StundeVon = 3, KlassenIds = new() { 1 }, LehrerIds = new() { 2 }, Text = "X" };
        va.Ersatz.Add(new VeranstaltungsErsatz { Datum = va.Von, LehrerId = 2 });
        var k = VeranstaltungsLogik.Kopieren(va, new DateOnly(2025, 2, 3));
        Assert.Equal(0, k.Id);
        Assert.Equal(new DateOnly(2025, 2, 3), k.Von);
        Assert.Equal(new DateOnly(2025, 2, 4), k.Bis);
        Assert.Equal(3, k.StundeVon);
        Assert.Empty(k.Ersatz);
        Assert.Equal(new[] { 2 }, k.LehrerIds);
    }

    [Fact]
    public void AbsenzAnlegenAendernLoeschen()
    {
        var (d, tag) = Daten();
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        var l = r.LehrerId!.Value;
        // stundenweise Absenz, ab St. > bis St. wird getauscht, letzte Stunde = ganzer Tag
        var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = l, Von = tag, Bis = tag, StundeVon = r.Stunde, StundeBis = r.Stunde };
        var gespeichert = AbsenzVerwaltung.Speichern(d, new[] { a }, Array.Empty<Abwesenheit>());
        Assert.Single(gespeichert);
        Assert.NotEqual(0, a.Id);
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.LehrerId == l && v.AbwesenheitId == a.Id);
        // gleiche Absenz noch einmal → nicht doppelt
        Assert.Empty(AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = l, Von = tag, Bis = tag, StundeVon = r.Stunde, StundeBis = r.Stunde } }, Array.Empty<Abwesenheit>()));
        Assert.Single(d.Abwesenheiten, x => x.ElementId == l);
        // ändern: anderer Tag → alte Vertretungen weg, neue da
        var morgen = Kalender.NaechsterUnterrichtstag(d, tag);
        var neu = new Abwesenheit { Id = a.Id, Art = a.Art, ElementId = l, Von = morgen, Bis = morgen, StundeVon = 1, StundeBis = d.Zeitraster.StundenProTag };
        AbsenzVerwaltung.Speichern(d, new[] { neu }, new[] { a });
        Assert.Equal(a.Id, neu.Id);
        Assert.Equal(99, neu.StundeBis);
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag && v.LehrerId == l);
        var offenMorgen = d.Vertretungen.Count(v => v.Datum == morgen && v.LehrerId == l && v.AbwesenheitId == neu.Id);
        Assert.Equal(Tagesplan.Regulaer(d, morgen).Count(x => x.LehrerId == l && !Vertretungen.IstBereitschaftsfach(d, x.FachId)) > 0, offenMorgen > 0);
        // löschen
        AbsenzVerwaltung.Loeschen(d, new[] { neu });
        Assert.DoesNotContain(d.Vertretungen, v => v.LehrerId == l && v.AbwesenheitId == neu.Id);
        Assert.DoesNotContain(d.Abwesenheiten, x => x.Id == neu.Id);
    }

    [Fact]
    public void NormalisierenTauschtStundenUndDaten()
    {
        var a = new Abwesenheit { Von = new DateOnly(2025, 3, 5), Bis = new DateOnly(2025, 3, 5), StundeVon = 5, StundeBis = 2 };
        AbsenzVerwaltung.Normalisieren(a, 8);
        Assert.Equal((2, 5), (a.StundeVon, a.StundeBis));
        var b = new Abwesenheit { Von = new DateOnly(2025, 3, 6), Bis = new DateOnly(2025, 3, 5), StundeVon = 0, StundeBis = 8 };
        AbsenzVerwaltung.Normalisieren(b, 8);
        Assert.True(b.Von < b.Bis);
        Assert.Equal((1, 99), (b.StundeVon, b.StundeBis));
    }

    [Fact]
    public void KlassenUndRaumAbsenz()
    {
        var (d, tag) = Daten();
        var r = Tagesplan.Regulaer(d, tag).First(x => x.KlassenIds.Count == 1 && x.RaumId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        var ka = AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Klasse, ElementId = r.KlassenIds[0], Von = tag, Bis = tag } }, Array.Empty<Abwesenheit>()).Single();
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.Art == VertretungsArt.Freisetzung && v.AbwesenheitId == ka.Id);
        AbsenzVerwaltung.Loeschen(d, new[] { ka });
        var ra = AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Raum, ElementId = r.RaumId!.Value, Von = tag, Bis = tag } }, Array.Empty<Abwesenheit>()).Single();
        Assert.Contains(d.Vertretungen, v => v.Datum == tag && v.Stunde == r.Stunde && v.Art == VertretungsArt.Raumvertretung && v.RaumOffen && v.AbwesenheitId == ra.Id);
        AbsenzVerwaltung.Loeschen(d, new[] { ra });
        Assert.DoesNotContain(d.Vertretungen, v => v.Datum == tag);
    }

    [Fact]
    public void TrotzAbsenzUndDauervertretung()
    {
        var (d, tag) = Daten();
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.Unterricht.Zeilen.Count == 1 && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        var l = r.LehrerId!.Value;
        var ende = tag.AddDays(21);
        AbsenzVerwaltung.Speichern(d, new[] { new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = l, Von = tag, Bis = ende } }, Array.Empty<Abwesenheit>());
        var v = d.Vertretungen.First(x => x.Datum == tag && x.Stunde == r.Stunde && x.LehrerId == l && x.Offen);
        // abwesende Lehrkraft trotzdem einsetzen → „Trotz Absenz“
        Vertretungen.VertreterSetzen(d, v, l);
        Assert.Equal(VertretungsArt.TrotzAbsenz, v.Art);
        Vertretungen.VertreterSetzen(d, v, null);
        Assert.True(v.Offen);
        // Dauervertretung: freier Lehrer für alle Folgewochen
        var tp = Tagesplan.Berechnen(d, tag);
        var frei = d.Lehrer.First(x => x.Id != l && !x.Ignorieren && Vertretungen.LehrerBelegt(d, tp, x.Id, r.Stunde, v) is null);
        var (gesetzt, _) = Vertretungen.Dauervertretung(d, v, frei.Id);
        Assert.Contains(v, gesetzt);
        Assert.True(gesetzt.Count >= 2);
        Assert.All(gesetzt, x => Assert.Equal(tag.DayOfWeek, x.Datum.DayOfWeek));
        Assert.All(gesetzt, x => Assert.Equal(frei.Id, x.VertreterId));
    }
}
