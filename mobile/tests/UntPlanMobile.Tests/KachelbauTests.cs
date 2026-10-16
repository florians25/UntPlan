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
        var k = Kachelbau.AusKarte(Karte("entfall", 2, text: "Projekttag Medien", art: "Entfall"), "class");
        Assert.Equal("cancelled", k.Art);
        Assert.Equal("Projekttag Medien", k.Info);
        Assert.Equal("Entfall", k.Typ);
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
    public void Bereitschaft_einer_abwesenden_Lehrkraft_ist_kein_Entfall()
    {
        var karte = Karte("abwesend", 3, fach: "BER", unterricht: 300, klassen: new(), raeume: new(), lehrer: new() { new("", "SNE") })
            with { LehrerAbwesend = true, Bereitschaft = true };
        foreach (var art in new[] { "teacher", "room", "class" })
        {
            var k = Kachelbau.AusKarte(karte, art);
            Assert.Equal("standby", k.Art);
            Assert.True(k.BereitschaftAbwesend);
            Assert.True(k.LehrerAbwesend);
            Assert.Equal("", k.Lehrkraft);
            Assert.Equal("SNE", k.LehrkraftAlt);
        }
        // anderes Kürzel: erkannt am Kennzeichen „bereitschaft“
        var vb = Kachelbau.AusKarte(Karte("abwesend", 3, fach: "VB", lehrer: new() { new("", "SNE") }) with { Bereitschaft = true, LehrerAbwesend = true }, "room");
        Assert.Equal("standby", vb.Art);
        // normale abgegebene Stunde bleibt eine Änderung, mit Kennzeichen „teacherAbsent“
        var weg = Kachelbau.AusKarte(Karte("abwesend", 1, lehrer: new() { new("HAR", "HOF", true) }) with { LehrerAbwesend = true }, "teacher");
        Assert.Equal("change", weg.Art);
        Assert.True(weg.LehrerAbwesend);
        Assert.Null(weg.BereitschaftAbwesend);
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
    public void Veranstaltung_bleibt_ueber_der_Aufsichtspause_ein_Block()
    {
        var plan = Plan("lehrer", new() { Karte("veranstaltung", 3, 5, fach: "", text: "Projekttag", unterricht: null) }, aufsichten: new()
        {
            new("2026-10-14", 4, "11:25", "11:45", "Hof", "Schulhof", "regulaer", "WEB", null),
        });
        var kacheln = Kachelbau.Tag(plan, plan.Tage[0], Array.Empty<Ferienzeit>());
        var feld = kacheln.Single(k => k.Art == "absence");
        Assert.Equal((3, 3), (feld.Stunde, feld.Dauer));
        Assert.True(feld.Feld);
        // die Aufsicht bleibt erhalten (Tagesansicht: halbe Kachel daneben, Wochenansicht: hinter dem Feld)
        Assert.Single(kacheln, k => k.Art == "supervision");
    }

    [Fact]
    public void Abwesende_Klasse_ist_Entfall_mit_Hinweis()
    {
        var k = Kachelbau.AusKarte(Karte("entfall", 1, art: "Freisetzung", text: "Exkursion Zoo"), "teacher");
        Assert.Equal("cancelled", k.Art);
        Assert.Null(k.KlasseAbwesend);
        Assert.Equal("5a", k.Klasse);
        Assert.Equal("Klasse abwesend – Exkursion Zoo", k.Info);
        // echter Entfall bleibt Entfall
        var entfall = Kachelbau.AusKarte(Karte("entfall", 1, art: "Entfall"), "teacher");
        Assert.Equal("cancelled", entfall.Art);
        Assert.Null(entfall.KlasseAbwesend);
        Assert.Equal("5a", entfall.Klasse);
    }

    [Fact]
    public void Offene_Raumvertretung_wie_offene_Lehrervertretung()
    {
        // Raumänderung ohne neuen Raum: bisheriger Raum durchgestrichen in der Plakette
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Raumänderung", raeume: new() { new("R101") }), "class");
        Assert.True(k.RaumOffen);
        Assert.Equal("", k.Raum);
        Assert.Equal("R101", k.RaumAlt);
        Assert.Contains("room", k.Geaendert);
        // Platzhalter „???“ mit bisherigem Raum
        var p = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Vertretung", raeume: new() { new("???", "R101", true) }), "class");
        Assert.True(p.RaumOffen);
        Assert.Equal("", p.Raum);
        Assert.Equal("R101", p.RaumAlt);
        // Raumänderung mit neuem Raum ist nicht offen
        var neu = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Raumänderung", raeume: new() { new("R109", "R101", true) }), "class");
        Assert.Null(neu.RaumOffen);
        Assert.Equal("R109", neu.Raum);
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

    [Fact]
    public void Klausur_geht_auf_die_Unterrichtskachel_ueber_statt_neuer_Kachel()
    {
        var plan = Plan("klasse", new()
        {
            Karte("regulaer", 3, 4),
            Karte("klausur", 3, 4, fach: "", text: "Mathe-Arbeit", art: "Klausur", unterricht: null, raeume: new() { new("R106") }),
        });
        var kacheln = Kachelbau.Tag(plan, plan.Tage[0], new List<Ferienzeit>());
        var k = Assert.Single(kacheln);
        Assert.Equal("klausur", k.Art);
        Assert.Equal("M", k.Fach);
        Assert.Contains("Mathe-Arbeit", k.Info);
        Assert.Equal("R106", k.Raum);
        Assert.Equal("R101", k.RaumAlt);
        Assert.Contains("room", k.Geaendert);
    }

    [Fact]
    public void Hinzugefuegter_Raum_steht_in_der_Plakette()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, raeume: new() { new("BI1", "R101", true) }), "class");
        Assert.Contains("room", k.Geaendert);
        var b = Kachelbau.AusKarte(Karte("buchung", 2, fach: "", text: "Raumbuchung", unterricht: null, raeume: new() { new("EDV1") }), "teacher");
        Assert.Contains("room", b.Geaendert);
    }

    [Fact]
    public void Raumbuchung_ist_keine_Vertretung()
    {
        // zusätzlicher Raum zu einem Unterricht gebucht: Kachel bleibt regulär, ohne Änderungstext, nur der Raum ist hervorgehoben
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Raumänderung (vorgemerkt)",
            raeume: new() { new("R101"), new("EDV1", "R101", true) }), "teacher");
        Assert.Equal("normal", k.Art);
        Assert.Equal("", k.Info);
        Assert.Equal("R101,EDV1", k.Raum);
        Assert.Contains("room", k.Geaendert);
        var b = Kachelbau.AusKarte(Karte("buchung", 2, fach: "", text: "Raumbuchung", unterricht: null, raeume: new() { new("EDV1") }), "room");
        Assert.Equal("normal", b.Art);
        Assert.Equal("", b.Info);
        // Vertretung mit zusätzlich gebuchtem Raum bleibt eine Vertretung
        var v = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Raumänderung (vorgemerkt)", lehrer: new() { new("BEC", "WEB", true) },
            raeume: new() { new("EDV1", "R101", true) }), "class");
        Assert.Equal("change", v.Art);
    }

    [Fact]
    public void Veranstaltung_zeigt_alle_teilnehmenden_Lehrkraefte()
    {
        var ereignis = new PlanEreignis("Lehrerkonferenz", "veranstaltung", 7, 8, false, new(), new() { "WEB", "BEC", "HOF" }, new() { "EDV2" });
        var plan = Plan("lehrer", new() { Karte("veranstaltung", 7, 8, fach: "", text: "Lehrerkonferenz", unterricht: null, klassen: new(), raeume: new()) },
            new() { Tag(ereignisse: new() { ereignis }) });
        var k = Assert.Single(Kachelbau.Tag(plan, plan.Tage[0], new List<Ferienzeit>()));
        Assert.Equal("absence", k.Art);
        Assert.Equal("WEB,BEC,HOF", k.Lehrkraft);
        Assert.Equal("EDV2", k.Raum);
    }

    [Fact]
    public void Vertretung_ohne_Lehrkraft_streicht_bisherige_Lehrkraft_und_zeigt_Hinweis()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 1, art: "Vtr. ohne Lehrer", lehrer: new() { new("", "WAG", true) }), "class");
        Assert.Equal("change", k.Art);
        Assert.Equal("", k.Lehrkraft);
        Assert.Equal("WAG", k.LehrkraftAlt);
        Assert.Contains("teacher", k.Geaendert);
        Assert.Equal(Kachelbau.OhneLehrkraftText, k.Info);
        Assert.False(k.Offen);
        // eigener Vertretungstext bleibt
        var mitText = Kachelbau.AusKarte(Karte("geaendert", 1, text: "Aufgaben im Raum", lehrer: new() { new("", "WAG", true) }), "class");
        Assert.Equal("Aufgaben im Raum", mitText.Info);
    }

    [Fact]
    public void Zusatzstunde_ohne_Lehrkraft_zeigt_drei_Striche()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 5, fach: "WUB", art: "Sondereinsatz", unterricht: null, lehrer: new()), "class");
        Assert.Equal("change", k.Art);
        Assert.True(k.Zusatz);
        Assert.Equal(Kachelbau.KeineLehrkraft, k.Lehrkraft);
        Assert.Equal("---", k.Lehrkraft);
        Assert.Equal("", k.LehrkraftAlt);
        // auch eine Zusatzstunde aus „Zusatzunterricht“ mit Unterricht, aber ohne Lehrkraft
        var z = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Zusatzunterricht", lehrer: new() { new("") }), "class");
        Assert.True(z.Zusatz);
        Assert.Equal("---", z.Lehrkraft);
    }

    [Fact]
    public void Drei_Striche_nur_bei_Zusatzstunden()
    {
        // Zusatzstunde mit Lehrkraft: Lehrkraft bleibt, Kennzeichen für die Plakette in den Details
        var mit = Kachelbau.AusKarte(Karte("geaendert", 2, fach: "SPO", art: "Sondereinsatz", unterricht: null, lehrer: new() { new("SNE") }), "class");
        Assert.True(mit.Zusatz);
        Assert.Equal("SNE", mit.Lehrkraft);
        // Vertretung ohne Lehrkraft („+++“): bisherige Lehrkraft durchgestrichen, kein „---“
        var plus = Kachelbau.AusKarte(Karte("geaendert", 1, art: "Vertretung", lehrer: new() { new("", "WAG", true) }), "class");
        Assert.Null(plus.Zusatz);
        Assert.Equal("", plus.Lehrkraft);
        Assert.Equal("WAG", plus.LehrkraftAlt);
        // offene Vertretung, Entfall, reguläre Stunde ohne Lehrkraft: kein „---“
        Assert.Equal("", Kachelbau.AusKarte(Karte("offen", 1, lehrer: new() { new("", "WAG", true) }), "class").Lehrkraft);
        Assert.Equal("", Kachelbau.AusKarte(Karte("entfall", 1, lehrer: new()), "class").Lehrkraft);
        Assert.Equal("", Kachelbau.AusKarte(Karte("regulaer", 1, lehrer: new()), "class").Lehrkraft);
        var vertretung = Kachelbau.AusKarte(Karte("geaendert", 1, art: "Vertretung", lehrer: new() { new("BEC", "WAG", true) }), "class");
        Assert.Null(vertretung.Zusatz);
        Assert.Equal("BEC", vertretung.Lehrkraft);
    }

    [Fact]
    public void Hinzugefuegter_zweiter_Raum_nennt_den_alten_Raum_nur_einmal()
    {
        // Raumänderung mit zwei neuen Räumen (UntPlanWeb setzt bei beiden den bisherigen Raum als „alt“)
        var k = Kachelbau.AusKarte(Karte("geaendert", 1, raeume: new() { new("R108", "R101", true), new("BI1", "R101", true) }), "class");
        Assert.Equal("R108,BI1", k.Raum);
        Assert.Equal("R101", k.RaumAlt);
        Assert.Contains("room", k.Geaendert);
    }

    [Fact]
    public void Kopplung_gleiches_Fach_und_Raum_ist_eine_Kachel_mit_allen_Lehrkraeften_und_Klassen()
    {
        // UntPlanWeb fasst nur bei gleichem Fach und gleichem Raum zusammen – dann eine Kachel mit allen Beteiligten
        var karte = Karte("regulaer", 5, fach: "KR", unterricht: 167, lehrer: new() { new("BEC"), new("ZIM") }, raeume: new() { new("R101") }, klassen: new() { new("5a"), new("5b") });
        var kacheln = Kachelbau.Tag(Plan("klasse", new() { karte }), Tag(), Array.Empty<Ferienzeit>());
        var k = Assert.Single(kacheln);
        Assert.Equal("KR", k.Fach);
        Assert.Equal("BEC,ZIM", k.Lehrkraft);
        Assert.Equal("R101", k.Raum);
        Assert.Equal(new[] { "BEC", "ZIM" }, k.Kopplung!.Select(t => t.Code));
        Assert.Equal(new[] { "5a", "5b" }, k.Klassenliste!.Select(t => t.Name));
    }

    [Fact]
    public void Kopplung_mit_unterschiedlichen_Faechern_bleibt_einzelne_Kacheln()
    {
        var kr = Karte("regulaer", 5, fach: "KR", unterricht: 167, lehrer: new() { new("BEC") }, raeume: new() { new("R101") }, klassen: new() { new("5a"), new("5b") });
        var er = Karte("regulaer", 5, fach: "ER", unterricht: 167, lehrer: new() { new("ZIM") }, raeume: new() { new("R102") }, klassen: new() { new("5a"), new("5b") });
        var kacheln = Kachelbau.Tag(Plan("klasse", new() { kr, er }), Tag(), Array.Empty<Ferienzeit>());
        Assert.Equal(2, kacheln.Count);
        Assert.Equal(new[] { "ER", "KR" }, kacheln.Select(x => x.Fach).OrderBy(x => x));
        Assert.All(kacheln, x => Assert.Null(x.Kopplung));
    }

    [Fact]
    public void Notizen_und_faellige_Hausaufgabe_setzen_die_Zeichen_auf_der_Kachel()
    {
        var mitNotiz = Karte("regulaer", 2) with { NotizSchueler = "Bibel mitbringen", NotizLehrer = "Beamer defekt", HausaufgabeFaellig = true };
        var k = Kachelbau.AusKarte(mitNotiz, "class");
        Assert.True(k.Notiz);
        Assert.Equal("Bibel mitbringen", k.NotizSchueler);
        Assert.Equal("Beamer defekt", k.NotizLehrer);
        Assert.True(k.Hausaufgabe);
        var ohne = Kachelbau.AusKarte(Karte("regulaer", 2), "class");
        Assert.Null(ohne.Notiz);
        Assert.Null(ohne.Hausaufgabe);
    }

    [Fact]
    public void Fachwechsel_alte_Stunde_als_Entfall_neue_als_Aenderung()
    {
        var neu = Karte("geaendert", 1, fach: "M", fachAlt: "E", art: "Vertretung", lehrer: new() { new("KRU", "WAG", true) });
        var alt = Karte("entfall", 1, fach: "E", art: "Entfall", unterricht: null, lehrer: new() { new("WAG") });
        var kacheln = Kachelbau.Tag(Plan("klasse", new() { neu, alt }), Tag(), Array.Empty<Ferienzeit>());
        Assert.Equal(2, kacheln.Count);
        var aenderung = kacheln.Single(x => x.Art == "change");
        Assert.Equal("M", aenderung.Fach);
        Assert.Contains("subject", aenderung.Geaendert);
        var entfall = kacheln.Single(x => x.Art == "cancelled");
        Assert.Equal("E", entfall.Fach);
        Assert.Equal("WAG", entfall.Lehrkraft);
    }
}
