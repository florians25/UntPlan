using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>Übersetzung der UntPlanWeb-Karten in die Kacheln der Oberfläche.</summary>
public class KachelbauTests
{
    private static readonly List<PlanZeit> Zeiten = new()
    {
        new(1, "1", "07:55", "08:40"), new(2, "2", "08:45", "09:30"), new(3, "3", "09:50", "10:35"),
        new(4, "4", "10:40", "11:25"), new(5, "5", "11:45", "12:30"), new(6, "6", "12:35", "13:20"),
    };

    private static PlanKarte Karte(string status, int von, int bis = 0, string fach = "M", string text = "", string art = "", int? unterricht = 106,
        List<PlanWert>? lehrer = null, List<PlanWert>? raeume = null, List<PlanWert>? klassen = null, string? fachAlt = null) =>
        new("2026-10-14", von, bis == 0 ? von : bis, "", "", status, fach, fachAlt, fach == "M" ? "Mathematik" : fach, "#7BAAF7",
            klassen ?? new() { new("5a") }, lehrer ?? new() { new("WEB") }, raeume ?? new() { new("R101") }, art, text, null, unterricht);

    private static PlanTag Tag(string datum = "2026-10-14", bool frei = false, string? grund = null, string? freiArt = null, List<PlanEreignis>? ereignisse = null) =>
        new(datum, "Mittwoch", "A", frei, grund, true, new(), ereignisse ?? new(), freiArt);

    private static Wochenplan Plan(string typ, List<PlanKarte> karten, List<PlanTag>? tage = null, List<PlanAufsicht>? aufsichten = null) =>
        new(typ, typ == "lehrer" ? "WEB" : "5a", "", "2026-10-12", "A", Zeiten, tage ?? new() { Tag() }, karten, aufsichten ?? new());

    [Fact]
    public void Regulaere_Stunde_mit_Fachfarbe_und_Unterricht()
    {
        var k = Kachelbau.AusKarte(Karte("regulaer", 3, 4), "class");
        Assert.Equal("normal", k.Art);
        Assert.Equal(3, k.Stunde);
        Assert.Equal(2, k.Dauer);
        Assert.Equal("5a", k.Klasse);
        Assert.Equal("WEB", k.Lehrkraft);
        Assert.Equal("M", k.Fach);
        Assert.Equal("Mathematik", k.FachLang);
        Assert.Equal("#7BAAF7", k.Farbe);
        Assert.Equal(106, k.Unterricht);
        Assert.Equal(106, k.Eintrag);
        Assert.Empty(k.Geaendert);
    }

    [Fact]
    public void Entfall_wird_cancelled_und_behaelt_Text()
    {
        var k = Kachelbau.AusKarte(Karte("entfall", 2, text: "Projekttag Medien", art: "Freisetzung"), "class");
        Assert.Equal("cancelled", k.Art);
        Assert.Equal("Projekttag Medien", k.Info);
        Assert.Equal("Freisetzung", k.Typ);
        Assert.Equal("#7BAAF7", k.Farbe);
    }

    [Fact]
    public void Vertretung_mit_geaendertem_Lehrer_und_Raum_markiert_changed_und_Orig()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 3, art: "Vertretung",
            lehrer: new() { new("BEC", "HOF", true) }, raeume: new() { new("R105", "R101", true) }, fach: "KU", fachAlt: "M"), "class");
        Assert.Equal("change", k.Art);
        Assert.Equal("BEC", k.Lehrkraft);
        Assert.Equal("HOF", k.LehrkraftAlt);
        Assert.Equal("R105", k.Raum);
        Assert.Equal("R101", k.RaumAlt);
        Assert.Equal("M", k.FachAlt);
        Assert.Equal(new[] { "teacher", "room", "subject" }, k.Geaendert);
        Assert.False(k.Offen);
    }

    [Fact]
    public void Offene_Vertretung_hat_leeren_Lehrer_und_den_alten_als_Orig()
    {
        var k = Kachelbau.AusKarte(Karte("offen", 1, lehrer: new() { new("", "WEB", true) }), "class");
        Assert.Equal("change", k.Art);
        Assert.True(k.Offen);
        Assert.Equal("", k.Lehrkraft);
        Assert.Equal("WEB", k.LehrkraftAlt);
        Assert.Contains("teacher", k.Geaendert);
    }

    [Fact]
    public void Abgegebene_Stunde_im_Lehrerplan_zeigt_Vertreter_und_eigenes_Kuerzel_als_Orig()
    {
        var k = Kachelbau.AusKarte(Karte("abwesend", 1, lehrer: new() { new("HAR", "HOF", true) }), "teacher");
        Assert.Equal("change", k.Art);
        Assert.Equal("HAR", k.Lehrkraft);
        Assert.Equal("HOF", k.LehrkraftAlt);
        Assert.False(k.Offen);
    }

    [Fact]
    public void Klausur_ohne_Fach_traegt_den_Titel()
    {
        var k = Kachelbau.AusKarte(Karte("klausur", 3, 4, fach: "", text: "Mathematik-Klausur", art: "Klausur", unterricht: null, klassen: new() { new("EF") }), "teacher");
        Assert.Equal("klausur", k.Art);
        Assert.Equal("Mathematik-Klausur", k.Fach);
        Assert.Equal(2, k.Dauer);
        Assert.Null(k.Unterricht);
    }

    [Fact]
    public void Vorgemerkte_Klausur_nennt_die_Vormerkung_im_Text()
    {
        var k = Kachelbau.AusKarte(Karte("klausur", 3, text: "Vokabeltest", art: "Klausur (vorgemerkt)"), "class");
        Assert.Equal("klausur", k.Art);
        Assert.Equal("Klausur (vorgemerkt) · Vokabeltest", k.Info);
    }

    [Fact]
    public void Veranstaltung_wird_gruen_gerahmtes_Feld()
    {
        var k = Kachelbau.AusKarte(Karte("veranstaltung", 7, 8, fach: "", text: "Lehrerkonferenz", art: "Veranst.", unterricht: null, klassen: new()), "teacher");
        Assert.Equal("absence", k.Art);
        Assert.True(k.Feld);
        Assert.Equal("Lehrerkonferenz", k.Fach);
        Assert.Equal(2, k.Dauer);
        // Veranstaltung als Zusatzkarte (geändert, ohne Fach und Unterricht) ebenso
        var p = Kachelbau.AusKarte(Karte("geaendert", 1, 10, fach: "", text: "Projekttag Medien", unterricht: null, lehrer: new(), raeume: new(),
            klassen: new() { new("5a"), new("5b") }), "class");
        Assert.Equal("absence", p.Art);
        Assert.Equal("5a,5b", p.Klasse);
        Assert.Equal(10, p.Dauer);
    }

    [Fact]
    public void Bereitschaft_wird_standby_und_entfaellt_in_der_Stunde_einer_Vertretung()
    {
        var plan = Plan("lehrer", new()
        {
            Karte("bereitschaft", 3, 5, fach: "BER", unterricht: 183, klassen: new(), raeume: new()),
            Karte("geaendert", 4, art: "Vertretung", lehrer: new() { new("WEB", "WAG", true) }),
        });
        var kacheln = Kachelbau.Tag(plan, plan.Tage[0], Array.Empty<Ferienzeit>());
        var bereitschaft = kacheln.Where(k => k.Art == "standby").OrderBy(k => k.Stunde).ToList();
        Assert.Equal(new[] { (3, 1), (5, 1) }, bereitschaft.Select(k => (k.Stunde, k.Dauer)));
        Assert.Single(kacheln, k => k.Art == "change");
    }

    [Fact]
    public void Ferien_werden_ganztaegiges_Feld_mit_Name_und_Zeitraum()
    {
        var ferien = new List<Ferienzeit> { new("Herbstferien", "2026-10-17", "2026-10-31", false), new("Tag der Deutschen Einheit", "2026-10-03", "2026-10-03", true) };
        var plan = Plan("klasse", new(), new() { Tag("2026-10-19", true, "Herbstferien", "ferien") });
        var k = Assert.Single(Kachelbau.Tag(plan, plan.Tage[0], ferien));
        Assert.Equal("holiday", k.Art);
        Assert.True(k.Ferien);
        Assert.Equal("Ferien", k.Fach);
        Assert.Equal("Herbst­ferien", k.FerienName);
        Assert.Equal("17.10. bis 31.10.", k.FerienZeitraum);
        Assert.Equal(1, k.Stunde);
        Assert.Equal(6, k.Dauer);

        var feiertag = Plan("klasse", new(), new() { Tag("2026-10-03", true, "Tag der Deutschen Einheit", "feiertag") });
        var f = Assert.Single(Kachelbau.Tag(feiertag, feiertag.Tage[0], ferien));
        Assert.Equal("Feiertag", f.Fach);
        Assert.Equal("03.10.", f.FerienZeitraum);
    }

    [Fact]
    public void Wochenende_bleibt_leer()
    {
        var plan = Plan("klasse", new(), new() { Tag("2026-10-17", true, null, "wochenende") });
        Assert.Empty(Kachelbau.Tag(plan, plan.Tage[0], Array.Empty<Ferienzeit>()));
    }

    [Fact]
    public void Pausenaufsicht_wird_Pille_und_teilt_Bloecke_ueber_der_Pause()
    {
        var plan = Plan("lehrer", new() { Karte("regulaer", 4, 5) }, aufsichten: new()
        {
            new("2026-10-14", 4, "11:25", "11:45", "Hof", "Schulhof", "vertreten", "???", "WEB"),
            new("2026-10-14", 0, "", "07:55", "Bus", "Bushaltestelle", "vertretung", "WEB", "KOC"),
        });
        var kacheln = Kachelbau.Tag(plan, plan.Tage[0], Array.Empty<Ferienzeit>());
        Assert.Equal(new[] { (4, 1), (5, 1) }, kacheln.Where(k => k.Art == "normal").OrderBy(k => k.Stunde).Select(k => (k.Stunde, k.Dauer)));
        var hof = kacheln.Single(k => k.Fach == "Schulhof");
        Assert.Equal("supervision", hof.Art);
        Assert.True(hof.Aufsicht);
        Assert.True(hof.AufsichtAbgegeben);
        Assert.Equal(("11:25", "11:45"), (hof.Von, hof.Bis));
        Assert.Equal("Vertretung offen", hof.Info);
        var bus = kacheln.Single(k => k.Fach == "Bushaltestelle");
        Assert.True(bus.VorErsterStunde);
        Assert.True(bus.AufsichtUebernommen);
        Assert.Equal(("07:40", "07:55"), (bus.Von, bus.Bis));
    }

    [Fact]
    public void Ganztaegiger_Termin_steht_als_Pille_ueber_dem_Unterricht_oder_als_Flaeche()
    {
        var termin = new List<PlanEreignis> { new("Elternsprechtag", "veranstaltung", 1, 10, true, new(), new(), new()) };
        var mitUnterricht = Plan("klasse", new() { Karte("regulaer", 1) }, new() { Tag(ereignisse: termin) });
        var pille = Kachelbau.Tag(mitUnterricht, mitUnterricht.Tage[0], Array.Empty<Ferienzeit>()).Single(k => k.Art == "event");
        Assert.True(pille.VorErsterStunde);
        Assert.Equal("Elternsprechtag", pille.Fach);

        var ohne = Plan("klasse", new() { Karte("entfall", 1) }, new() { Tag(ereignisse: termin) });
        var flaeche = Kachelbau.Tag(ohne, ohne.Tage[0], Array.Empty<Ferienzeit>()).Single(k => k.Art == "event");
        Assert.Null(flaeche.VorErsterStunde);
        Assert.Equal(6, flaeche.Dauer);

        // Steht die Veranstaltung schon als Karte im Plan, kommt keine zweite dazu
        var doppelt = Plan("klasse", new() { Karte("veranstaltung", 1, 10, fach: "", text: "Elternsprechtag", unterricht: null) }, new() { Tag(ereignisse: termin) });
        Assert.Single(Kachelbau.Tag(doppelt, doppelt.Tage[0], Array.Empty<Ferienzeit>()));
    }

    [Fact]
    public void Kopplung_mit_zwei_Lehrkraeften_liefert_teacherList()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, lehrer: new() { new("BIE"), new("", "BLU", true), new("ILK", "KRU", true) }), "class");
        Assert.NotNull(k.Kopplung);
        Assert.Equal(new[] { ("BIE", "ok"), ("BLU", "absent"), ("ILK", "sub") }, k.Kopplung!.Select(t => (t.Code, t.Zustand)));
        Assert.Equal("KRU", k.Kopplung![2].Fuer);
    }

    [Fact]
    public void Woche_liefert_fuenf_Tage_und_Stundenzeiten()
    {
        var plan = Plan("klasse", new() { Karte("regulaer", 1) });
        var woche = Kachelbau.Woche(plan);
        Assert.Equal(new[] { "2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-16" }, woche.Keys);
        Assert.Single(woche["2026-10-14"]);
        var zeiten = Kachelbau.Stundenzeiten(Zeiten);
        Assert.Equal(6, zeiten.Count);
        Assert.Contains("1", zeiten.Keys);
    }
}
