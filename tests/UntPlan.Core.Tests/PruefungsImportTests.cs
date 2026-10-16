using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Import von Prüfungen aus UntPlanWeb (Stundenplanänderungen, Klausuren): Löschen, Verlegen (Löschen + neu im selben Import, in
/// beliebiger Reihenfolge), Titel mit Text, mehrfacher Import über Dialog und automatischen Abgleich, verwaiste Vertretungszeilen.
/// </summary>
public class PruefungsImportTests
{
    private sealed record Umgebung(Schuldatei D, DateOnly Tag, TagesEintrag Stunde, Klasse Klasse, Lehrer Lehrer, Raum Raum1, Raum Raum2)
    {
        public string Datum => Tag.ToString("yyyy-MM-dd");
    }

    private static Umgebung Aufbauen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Tagesplan.Berechnen(d, tag);
        var stunde = tp.Eintraege.First(e => e.Unterricht != null && e.KlassenIds.Count == 1 && e.RaumIds.Count == 1 && e.LehrerIds.Count == 1);
        var frei = d.Raeume.Where(r => !tp.Stunde(stunde.Stunde).Any(e => e.RaumIds.Contains(r.Id))).Take(2).ToList();
        return new Umgebung(d, tag, stunde, d.Klassen.First(k => k.Id == stunde.KlassenIds[0]), d.Lehrer.First(l => l.Id == stunde.LehrerIds[0]), frei[0], frei[1]);
    }

    private static WebRueckmeldung Pruefung(Umgebung u, string id, Raum raum, string titel = "Mathe Klausur", string text = "", int? bis = null) => new()
    {
        Id = id, Typ = WebRueckmeldungsTypen.Klausur, Lehrer = u.Lehrer.Kurzname, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = bis ?? u.Stunde.Stunde,
        Klassen = { u.Klasse.Kurzname }, Raeume = { raum.Kurzname }, Titel = titel, Text = text, Erstellt = new DateTime(2026, 10, 14, 10, 0, 0),
    };

    private static WebRueckmeldung Loeschen(Umgebung u, string id, string titel, Raum? raum = null, bool exakt = false) => new()
    {
        Id = id, Typ = WebRueckmeldungsTypen.KlausurLoeschen, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde, Klassen = { u.Klasse.Kurzname },
        Raeume = raum is null ? new() : new() { raum.Kurzname }, Titel = titel, Grund = exakt ? StundenplanAenderungen.Exakt : null, Erstellt = new DateTime(2026, 10, 14, 10, 0, 0),
    };

    private static (List<string> Ok, List<(string Id, string Grund)> Nein) Dialog(Umgebung u, params WebRueckmeldung[] liste) =>
        StundenplanAenderungen.Uebernehmen(u.D, StundenplanAenderungen.Analysieren(u.D, liste, u.Tag.AddDays(-1), u.Tag.AddDays(1), false), "test");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verlegen_in_dieselbe_Zeit_mit_anderem_Raum_loescht_nur_den_alten_Termin(bool neuZuerst)
    {
        var u = Aufbauen();
        Dialog(u, Pruefung(u, "alt", u.Raum1));
        var alt = Assert.Single(u.D.Klausuren);
        // Verlegen = Löschung (genau beschrieben) + neue Prüfung im selben Import, Reihenfolge der Meldungen beliebig
        var loeschen = Loeschen(u, "weg", "Mathe Klausur", u.Raum1, exakt: true);
        var neu = Pruefung(u, "neu", u.Raum2);
        var (ok, nein) = neuZuerst ? Dialog(u, neu, loeschen) : Dialog(u, loeschen, neu);
        Assert.Empty(nein);
        Assert.Equal(2, ok.Count);
        var k = Assert.Single(u.D.Klausuren);
        Assert.Equal("neu", k.WebId);
        Assert.Equal(new[] { u.Raum2.Id }, k.RaumIds);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId == alt.Id);
        // der automatische Abgleich danach ändert nichts (schon übernommen) – auch nicht in umgekehrter Reihenfolge
        UntPlanWebExport.Uebernehmen(u.D, new[] { neu, loeschen });
        UntPlanWebExport.Uebernehmen(u.D, new[] { loeschen, neu });
        Assert.Equal("neu", Assert.Single(u.D.Klausuren).WebId);
        // nur automatischer Abgleich (ohne Dialog): ebenso
        var u2 = Aufbauen();
        UntPlanWebExport.Uebernehmen(u2.D, new[] { Pruefung(u2, "alt", u2.Raum1) });
        UntPlanWebExport.Uebernehmen(u2.D, neuZuerst ? new[] { Pruefung(u2, "neu", u2.Raum2), Loeschen(u2, "weg", "Mathe Klausur", u2.Raum1, true) }
                                                    : new[] { Loeschen(u2, "weg", "Mathe Klausur", u2.Raum1, true), Pruefung(u2, "neu", u2.Raum2) });
        Assert.Equal("neu", Assert.Single(u2.D.Klausuren).WebId);
    }

    [Fact]
    public void Verlegen_auf_laengere_Dauer_ersetzt_die_Pruefung()
    {
        var u = Aufbauen();
        Dialog(u, Pruefung(u, "alt", u.Raum1));
        Dialog(u, Pruefung(u, "neu", u.Raum1, bis: u.Stunde.Stunde + 1), Loeschen(u, "weg", "Mathe Klausur", u.Raum1, exakt: true));
        var k = Assert.Single(u.D.Klausuren);
        Assert.Equal((u.Stunde.Stunde, u.Stunde.Stunde + 1), (k.StundeVon, k.StundeBis));
    }

    [Fact]
    public void Loeschen_mit_Kartentext_Name_und_Text_findet_die_Pruefung()
    {
        var u = Aufbauen();
        Dialog(u, Pruefung(u, "p1", u.Raum1, "Englisch Test", "Vokabeln Unit 3"));
        // im Web steht auf der Karte „Name Text“ bzw. „Name · Text“
        var (ok, nein) = Dialog(u, Loeschen(u, "l1", "Englisch Test Vokabeln Unit 3"));
        Assert.Empty(nein);
        Assert.Single(ok);
        Assert.Empty(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId.HasValue);
        // fremder Titel löscht nichts
        Dialog(u, Pruefung(u, "p2", u.Raum1, "Physik Klausur"));
        Assert.Single(Dialog(u, Loeschen(u, "l2", "Chemie Test")).Nein);
        Assert.Single(u.D.Klausuren);
    }

    [Fact]
    public void Geloeschte_Pruefung_hinterlaesst_keine_Pausenaufsichtsvertretung()
    {
        var u = Aufbauen();
        u.D.Einstellungen.Portal.PausenaufsichtNachPruefungIgnorieren = false;
        var gang = u.D.Gaenge.FirstOrDefault() ?? new Gang { Id = u.D.NeueId(), Kurzname = "Hof" };
        if (!u.D.Gaenge.Contains(gang)) u.D.Gaenge.Add(gang);
        var periode = u.D.PeriodeAm(u.Tag);
        periode.Pausenaufsichten ??= new();
        periode.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang.Id, Tag = Kalender.TagIndex(u.D, u.Tag), NachStunde = u.Stunde.Stunde, LehrerId = u.Lehrer.Id });
        Dialog(u, Pruefung(u, "p1", u.Raum1));
        var k = Assert.Single(u.D.Klausuren);
        Assert.Contains(u.D.Vertretungen, v => v.KlausurId == k.Id && v.Art == VertretungsArt.Pausenaufsicht);
        Dialog(u, Loeschen(u, "l1", "Mathe Klausur"));
        Assert.Empty(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId == k.Id);
    }

    [Fact]
    public void Mehrfacher_Import_und_Export_erzeugt_keine_Dubletten()
    {
        var u = Aufbauen();
        var m = Pruefung(u, "p1", u.Raum1, bis: u.Stunde.Stunde + 1);
        for (var i = 0; i < 3; i++)
        {
            Dialog(u, m);
            UntPlanWebExport.Uebernehmen(u.D, new[] { m });
            UntPlanWebExport.PaketErstellen(u.D, u.Tag);
        }
        var k = Assert.Single(u.D.Klausuren);
        var paket = UntPlanWebExport.PaketErstellen(u.D, u.Tag);
        Assert.Single(paket.Termine, t => t.Art == "klausur");
        // je Stunde genau eine Prüfungszeile je Aufsicht, der Unterricht der Klasse entfällt mit Klausur-Id
        var stunden = paket.Tage.Single(t => t.Datum == u.Datum).Stunden.Where(x => x.Klassen.Contains(u.Klasse.Kurzname) && x.Stunde == u.Stunde.Stunde).ToList();
        Assert.Single(stunden, x => x.Status == "zusaetzlich" && x.Klausur == k.Id);
        Assert.All(stunden.Where(x => x.Status == "entfall"), x => Assert.Equal(k.Id, x.Klausur));
        // die Meldung steht im Protokoll und wird im Dialog nicht noch einmal angeboten
        Assert.Empty(StundenplanAenderungen.Analysieren(u.D, new[] { m }, u.Tag.AddDays(-1), u.Tag.AddDays(1), false));
    }
}
