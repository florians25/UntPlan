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

    /// <summary>Zeilen der Vertretungsliste der Klasse in der Stunde der Prüfung.</summary>
    private static List<PlanZeile> Liste(Umgebung u, int? stunde = null) =>
        Vertretungsplan.Zeilen(u.D, u.Tag, true).Where(z => z.Stunde == (stunde ?? u.Stunde.Stunde) && z.KlassenIds.Contains(u.Klasse.Id)).ToList();

    private static WebRueckmeldung Buchung(Umgebung u, string id, Raum raum, bool mitKlasse = false) => new()
    {
        Id = id, Typ = WebRueckmeldungsTypen.Raumbuchung, Lehrer = u.Lehrer.Kurzname, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde,
        Raum = raum.Kurzname, Klassen = mitKlasse ? new() { u.Klasse.Kurzname } : new(), Titel = "Klausur", Erstellt = new DateTime(2026, 10, 14, 9, 0, 0),
    };

    /// <summary>
    /// Fehlerbild aus dem Betrieb: Prüfung aus UntPlanWeb, Aufsicht ist die Lehrkraft des Unterrichts (z. B. SL, IT in B125, Prüfung in A300).
    /// Bisher standen je Stunde eine Zeile „Klausur“ und zusätzlich „Entfall … Klausur IT“ in der Liste. Jetzt: genau eine Zeile
    /// (Art Klausur, Lehrer/Fach/Raum des Unterrichts, neuer Raum der Prüfung) – auch nach mehrfachem Import über beide Wege.
    /// </summary>
    [Fact]
    public void Pruefung_im_eigenen_Unterricht_ergibt_genau_eine_Zeile_je_Stunde()
    {
        var u = Aufbauen();
        var m = Pruefung(u, "p1", u.Raum1, "IT", "Klassenarbeit: 1. KA - IT");
        for (var i = 0; i < 2; i++)
        {
            UntPlanWebExport.Uebernehmen(u.D, new[] { m });
            Dialog(u, m);
            UntPlanWebExport.PaketErstellen(u.D, u.Tag);
        }
        var k = Assert.Single(u.D.Klausuren);
        var z = Assert.Single(Liste(u));
        Assert.Equal("Klausur", z.Art);
        Assert.Equal(u.Lehrer.Kurzname, z.Lehrer);
        Assert.Equal(u.Lehrer.Kurzname, z.Vertreter);
        Assert.Equal(u.Raum1.Kurzname, z.NeuerRaum);
        Assert.Equal("IT Klassenarbeit: 1. KA - IT", z.Text);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.Datum == u.Tag && v.IstEntfall && v.KlausurId == k.Id);
        // Tagesplan: der Unterricht findet als Prüfung im Prüfungsraum statt (Raum der Prüfung belegt, bisheriger Raum frei)
        var e = Assert.Single(Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde), x => x.Findetstatt && x.LehrerIds.Contains(u.Lehrer.Id));
        Assert.Equal(new[] { u.Raum1.Id }, e.RaumIds);
        // Paket für UntPlanWeb unverändert: entfallener Unterricht + Prüfungsstunde mit Klausur-Id (eine Karte im Web)
        var stunden = UntPlanWebExport.PaketErstellen(u.D, u.Tag).Tage.Single(t => t.Datum == u.Datum).Stunden
            .Where(x => x.Stunde == u.Stunde.Stunde && x.Klassen.Contains(u.Klasse.Kurzname)).ToList();
        Assert.Equal(u.Stunde.Unterricht!.Id, Assert.Single(stunden, x => x.Status == "entfall").Unterricht);
        Assert.Equal(new[] { u.Raum1.Kurzname }, Assert.Single(stunden, x => x.Status == "zusaetzlich").Raeume);
        Assert.All(stunden, x => Assert.Equal(k.Id, x.Klausur));
        // Zählung: Prüfung im eigenen Unterricht ist weder Mehrleistung noch Entfall
        var zaehl = Zaehlung.Lehrer(u.D, u.Lehrer, u.Tag, u.Tag);
        Assert.DoesNotContain(zaehl.Zeilen, x => x.Stunde == u.Stunde.Stunde && x.Zaehlt);
    }

    [Fact]
    public void Gleiche_Pruefung_mit_neuer_Kennung_wird_nur_aktualisiert()
    {
        var u = Aufbauen();
        UntPlanWebExport.Uebernehmen(u.D, new[] { Pruefung(u, "p1", u.Raum1, "IT") });
        // dieselbe Prüfung (Klasse, Tag, Stunden, Titel) erneut gemeldet – mit anderer Kennung und anderem Raum
        Dialog(u, Pruefung(u, "p2", u.Raum2, "IT", "1. KA"));
        UntPlanWebExport.Uebernehmen(u.D, new[] { Pruefung(u, "p3", u.Raum2, "IT", "1. KA") });
        var k = Assert.Single(u.D.Klausuren);
        Assert.Equal("p1", k.WebId);
        Assert.Equal(new[] { u.Raum2.Id }, k.RaumIds);
        Assert.Equal("1. KA", k.Text);
        Assert.Equal(u.Raum2.Kurzname, Assert.Single(Liste(u)).NeuerRaum);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gleiche_Kennung_mit_neuem_Termin_verlegt_die_Pruefung_ohne_Reste(bool dialog)
    {
        var u = Aufbauen();
        var andere = Tagesplan.Regulaer(u.D, u.Tag).First(r => r.Stunde != u.Stunde.Stunde && r.KlassenIds.Contains(u.Klasse.Id)).Stunde;
        var m = Pruefung(u, "p1", u.Raum1, "IT");
        UntPlanWebExport.Uebernehmen(u.D, new[] { m });
        var verlegt = new WebRueckmeldung
        {
            Id = "p1", Typ = m.Typ, Lehrer = m.Lehrer, Von = m.Von, StundeVon = andere, StundeBis = andere, Klassen = m.Klassen, Raeume = m.Raeume, Titel = m.Titel,
        };
        if (dialog) Dialog(u, verlegt); else UntPlanWebExport.Uebernehmen(u.D, new[] { verlegt });
        var k = Assert.Single(u.D.Klausuren);
        Assert.Equal((andere, andere), (k.StundeVon, k.StundeBis));
        Assert.DoesNotContain(u.D.Vertretungen, v => v.Datum == u.Tag && v.Stunde == u.Stunde.Stunde && v.KlausurId.HasValue);
        Assert.Empty(Liste(u));
        Assert.NotEmpty(Liste(u, andere));
        // erneut gemeldet: nichts mehr zu tun
        UntPlanWebExport.Uebernehmen(u.D, new[] { verlegt });
        Assert.Empty(StundenplanAenderungen.Analysieren(u.D, new[] { verlegt }, u.Tag.AddDays(-1), u.Tag.AddDays(1), false));
        Assert.Single(u.D.Klausuren);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Bestehende_Raumbuchung_wird_zur_Pruefung(bool dialog, bool mitKlasse)
    {
        var u = Aufbauen();
        // zuerst die Buchung des Prüfungsraums (automatischer Abgleich: Raumbuchung bzw. Dialog: Raumänderung des Unterrichts)
        var b = Buchung(u, "b1", u.Raum1, mitKlasse);
        if (dialog) Dialog(u, b); else UntPlanWebExport.Uebernehmen(u.D, new[] { b });
        Assert.Contains(u.D.Vertretungen, v => v.WebId == "b1");
        // dann die Prüfung: die Buchung wird zur Prüfung, kein zusätzlicher Block
        if (dialog) Dialog(u, Pruefung(u, "p1", u.Raum1, "IT")); else UntPlanWebExport.Uebernehmen(u.D, new[] { Pruefung(u, "p1", u.Raum1, "IT") });
        Assert.Single(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == "b1");
        Assert.Equal("Klausur", Assert.Single(Liste(u)).Art);
        Assert.Single(Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde), x => x.Findetstatt && x.RaumIds.Contains(u.Raum1.Id));
        // Prüfung im Web gelöscht: keine Reste der Prüfung, die vorher bestehende Buchung (bzw. Raumänderung) gilt wieder
        if (dialog) Dialog(u, Loeschen(u, "l1", "IT")); else UntPlanWebExport.Uebernehmen(u.D, new[] { Loeschen(u, "l1", "IT") });
        Assert.Empty(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId.HasValue);
        Assert.Single(u.D.Vertretungen, v => v.WebId == "b1");
        Assert.Single(Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde), x => x.Findetstatt && x.RaumIds.Contains(u.Raum1.Id));
    }

    [Fact]
    public void Pruefung_ohne_Raum_uebernimmt_den_Raum_der_Buchung()
    {
        var u = Aufbauen();
        UntPlanWebExport.Uebernehmen(u.D, new[] { Buchung(u, "b1", u.Raum1) });
        var p = Pruefung(u, "p1", u.Raum1, "IT");
        p.Raeume.Clear();
        UntPlanWebExport.Uebernehmen(u.D, new[] { p });
        Assert.Equal(new[] { u.Raum1.Id }, Assert.Single(u.D.Klausuren).RaumIds);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == "b1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Buchung_nach_der_Pruefung_wird_nicht_zusaetzlich_angelegt(bool dialog)
    {
        var u = Aufbauen();
        UntPlanWebExport.Uebernehmen(u.D, new[] { Pruefung(u, "p1", u.Raum1, "IT") });
        var b = Buchung(u, "b1", u.Raum1);
        var (ok, nein) = dialog ? Dialog(u, b) : (UntPlanWebExport.Uebernehmen(u.D, new[] { b }).Uebernommen, new());
        Assert.Contains("b1", ok);
        Assert.Empty(nein);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == "b1");
        Assert.Single(Liste(u));
    }

    [Fact]
    public void Geloeschte_Pruefung_stellt_den_Unterricht_ohne_Reste_wieder_her()
    {
        var u = Aufbauen();
        Dialog(u, Pruefung(u, "p1", u.Raum1));
        Assert.Single(Liste(u));
        Dialog(u, Loeschen(u, "l1", "Mathe Klausur", u.Raum1, exakt: true));
        Assert.Empty(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId.HasValue || v.Quelle == VertretungsQuelle.Klausur);
        Assert.Empty(Liste(u));
        var e = Assert.Single(Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde), x => x.Unterricht?.Id == u.Stunde.Unterricht!.Id);
        Assert.Equal(TagesStatus.Regulaer, e.Status);
    }

    [Fact]
    public void Aeltere_Datei_mit_Entfall_neben_der_Pruefung_wird_bereinigt()
    {
        var u = Aufbauen();
        var k = new Klausur { Id = u.D.NeueId(), Name = "IT", Datum = u.Tag, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde, KlassenIds = { u.Klasse.Id }, LehrerIds = { u.Lehrer.Id }, RaumIds = { u.Raum1.Id }, WebId = "alt" };
        u.D.Klausuren.Add(k);
        // Stand älterer Versionen: Prüfungszeile ohne Unterricht + Entfall des Unterrichts
        u.D.Vertretungen.Add(new Vertretung { Id = u.D.NeueId(), Datum = u.Tag, Stunde = u.Stunde.Stunde, Art = VertretungsArt.Klausur, Quelle = VertretungsQuelle.Klausur, KlausurId = k.Id, VertreterId = u.Lehrer.Id, KlassenIds = { u.Klasse.Id }, VertretungsraumId = u.Raum1.Id, Text = "IT" });
        u.D.Vertretungen.Add(new Vertretung { Id = u.D.NeueId(), Datum = u.Tag, Stunde = u.Stunde.Stunde, Art = VertretungsArt.Entfall, Quelle = VertretungsQuelle.Klausur, KlausurId = k.Id, UnterrichtId = u.Stunde.Unterricht!.Id, ZeilenIndex = u.Stunde.Regel!.ZeilenIndex, LehrerId = u.Lehrer.Id, KlassenIds = { u.Klasse.Id }, Text = "Klausur IT", Bearbeitet = true });
        Vertretungen.Aktualisieren(u.D, u.Tag);
        Assert.Equal("Klausur", Assert.Single(Liste(u)).Art);
    }
}
