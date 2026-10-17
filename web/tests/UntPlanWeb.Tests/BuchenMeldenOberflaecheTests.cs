using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprint 27: Raumwahl als eine Komponente (Portal-Liste am Feld mit Kollisionserkennung, eingebettete Liste mit Filtern Gebäude,
/// Abteilung, Kapazität, nur freie), Prüfung editieren mit Löschen, Melden im Portal-Design (Kacheln, einheitliche Formulare,
/// Meine Meldungen mit Status-Pills und Filtern) und das überarbeitete Buchungsmodul (drei Schritte, Schnellbuchung, Storno-Dialog).
/// </summary>
public class BuchenMeldenOberflaecheTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    // ------------------------------------------------------------------ API

    [Fact]
    public async Task Raumstatus_liefert_Gebaeude_und_Abteilung_fuer_die_Filter()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var liste = (await Testserver.HolenAsync(lan, "/api/app/buchen/raumstatus?datum=2026-10-15&von=3&bis=4")).AsArray();
        Assert.NotEmpty(liste);
        Assert.All(liste, r =>
        {
            Assert.NotNull(r!["gebaeude"]);
            Assert.NotNull(r["abteilung"]);
            Assert.NotNull(r["kapazitaet"]);
        });
    }

    [Fact]
    public async Task Buchungsoptionen_nennen_den_naechsten_Unterrichtstag()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // Mittwoch der Beispielwoche: heute
        Assert.Equal("2026-10-14", (await Testserver.HolenAsync(lan, "/api/app/buchen/optionen"))["naechsterTag"]!.GetValue<string>());
        // Samstag: der nächste Tag mit Unterricht (nicht Wochenende, nicht unterrichtsfrei)
        s.Dienst<Uhr>().Fest = new DateTime(2026, 10, 17, 9, 0, 0);
        var tag = DateOnly.Parse((await Testserver.HolenAsync(lan, "/api/app/buchen/optionen"))["naechsterTag"]!.GetValue<string>());
        Assert.True(tag > new DateOnly(2026, 10, 17));
        Assert.NotEqual(DayOfWeek.Saturday, tag.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, tag.DayOfWeek);
    }

    [Fact]
    public async Task Pruefung_editieren_aendert_Titel_und_Loeschen_zieht_zurueck()
    {
        await using var s = await Testserver.StartenAsync();
        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var frei = (await Testserver.HolenAsync(c, "/api/app/meldungen/freieraeume?datum=2026-10-16&von=3&bis=4")).AsArray()[0]!["kurz"]!.GetValue<string>();
        var alt = new { datum = "2026-10-16", stundeVon = 3, stundeBis = 4, klassen = new[] { "EF" }, titel = "Deutsch GK" };
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new { alt.datum, alt.stundeVon, alt.stundeBis, alt.klassen, raeume = new[] { frei }, aufsicht = new[] { "MUE" }, alt.titel }));
        // Editieren (gleiche Zeit, neuer Name, andere Stunde): alte Prüfung weg, neue mit neuem Titel
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new
        {
            alt, neu = new { datum = "2026-10-16", stundeVon = 3, stundeBis = 3, klassen = new[] { "EF" }, raeume = new[] { frei }, aufsicht = new[] { "MUE" }, titel = "Deutsch GK Klausur 1", text = "" },
        }));
        var offen = (await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray();
        var k = Assert.Single(offen, x => x!["typ"]!.GetValue<string>() == "klausur")!;
        Assert.Equal("Deutsch GK Klausur 1", k["titel"]!.GetValue<string>());
        // Löschen aus dem Editier-Dialog nutzt dieselbe Löschlogik
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", new { datum = "2026-10-16", stundeVon = 3, stundeBis = 3, klassen = new[] { "EF" }, titel = "Deutsch GK Klausur 1" }));
        offen = (await Testserver.HolenAsync(s.SyncClient(), $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray();
        Assert.DoesNotContain(offen, x => x!["typ"]!.GetValue<string>().StartsWith("klausur"));
    }

    // ------------------------------------------------------------------ Raumwahl-Komponente

    [Fact]
    public void Raumwahl_oeffnet_als_Portal_am_Feld_mit_Kollisionserkennung()
    {
        var rw = Datei("js", "raumwahl.js");
        // Portal im obersten Behälter (Dialog, Bootstrap-Fenster, Seite), fest positioniert, nicht vom Dialoginhalt abgeschnitten
        Assert.Contains("const behaelterVon = el => el.closest('.kbd-overlay') ?? el.closest('.modal') ?? el.closest('dialog') ?? document.body;", rw);
        Assert.Contains("behaelterVon(huelle).append(pop);", rw);
        Assert.Contains("const nachOben = unten < Math.min(gewuenscht, 240) && oben > unten;", rw);
        Assert.Contains("window.addEventListener('scroll', beimScrollen, true);", rw);
        Assert.Contains("scroller = scrollVorfahre(huelle);", rw);
        var css = Datei("css", "raumwahl.css");
        Assert.Contains(".rw-pop { position: fixed;", css);
        Assert.DoesNotContain(".rw-pop { position: absolute", css);
        // Zeile: Kürzel Medium, Langname grau, Kapazität als Symbol, Status-Pill
        Assert.Contains("<i class=\"bi bi-people\" aria-hidden=\"true\"></i>${r.kapazitaet}", rw);
        Assert.Contains(".rw-kurz { font-weight: 500;", css);
        Assert.Contains(".rw-status.frei {", css);
        Assert.Contains(".rw-status { justify-self: end;", css);
    }

    [Fact]
    public void Raumbuchung_nutzt_die_gemeinsame_Raumliste_mit_Filtern()
    {
        var rw = Datei("js", "raumwahl.js");
        Assert.Contains("export function raumListe(ziel, optionen = {})", rw);
        Assert.Contains("pille('gebaeude', 'Gebäude'", rw);
        Assert.Contains("pille('abteilung', 'Abteilung'", rw);
        Assert.Contains("pille('kapazitaet', 'Kapazität'", rw);
        Assert.Contains("data-rwf=\"nurFrei\"", rw);
        // Auswahl-Pillen sind native Selects in .nl-pille – die einheitliche Auswahl-Komponente legt sich darüber
        Assert.Contains("<label class=\"nl-pille rw-pille", rw);
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("import { raumwahl, raumListe } from './raumwahl.js';", sd);
        Assert.Contains("const liste = raumListe(ziel, {", sd);
        Assert.Contains("/api/app/buchen/raumstatus?datum=${k.datum}&von=${k.von}&bis=${k.bis}", sd);
        Assert.DoesNotContain("sd-filterpille wahl", sd);
        Assert.DoesNotContain("data-abteilung aria-label=\"Abteilung\"", sd);
        var bu = Datei("js", "ansichten", "buchen.js");
        Assert.Contains("import { raumListe } from '../raumwahl.js';", bu);
        Assert.Contains("raumListe(inhalt.querySelector('[data-raumliste]'), {", bu);
    }

    [Fact]
    public void Pruefung_editieren_bietet_Loeschen_mit_Rueckfrage()
    {
        var sd = Datei("js", "stundendetails.js");
        Assert.Contains("data-pruefung-editieren>Prüfung editieren</button>", sd);
        Assert.Contains("altDialog('Prüfung editieren',", sd);
        Assert.Contains("data-pe-loeschen", sd);
        Assert.Contains("o.querySelector('[data-pe-loeschen]').onclick = async () => { if (await pruefungLoeschen()) zu(); };", sd);
        Assert.Contains("if (!await bestaetigen('Prüfung löschen',", sd);
        Assert.Contains("await senden('/api/app/meldungen/klausur/loeschen', pruefungAlt());", sd);
        Assert.Contains("raumwahl(o.querySelector('[name=raeume]'), { wurzel: o, unterricht: k.unterricht ?? null });", sd);
        Assert.DoesNotContain("data-pruefung-verlegen", sd);
    }

    // ------------------------------------------------------------------ Melden

    [Fact]
    public void Melden_im_Portal_Design_mit_Kacheln_Formularen_und_Status()
    {
        var js = Datei("js", "ansichten", "melden.js");
        Assert.Contains("<div class=\"mf-kacheln\">", js);
        Assert.Contains("kachel('#/melden/abwesenheit'", js);
        Assert.Contains("kachel('#/melden/klausur'", js);
        Assert.Contains("kachel('#/buchen/raum'", js);
        Assert.Contains("kachel('#/melden/meine'", js);
        Assert.Contains("<form class=\"mf-karte\" novalidate>", js);
        Assert.Contains("const statusPill = s =>", js);
        Assert.Contains("suchfeldMitFilter({ platzhalter: 'Meldungen durchsuchen'", js);
        Assert.Contains("Keine Meldungen für diesen Filter", js);
        Assert.Contains("Noch keine Meldungen", js);
        Assert.Contains("if (bereichParam === 'raum') { kontext.navigieren('#/buchen/raum', true); return; }", js);
        Assert.DoesNotContain("class=\"card\"", js);
        var app = Datei("js", "app.js");
        Assert.Contains("{ nav: 'melden-uebersicht', href: '#/melden', text: 'Übersicht', icon: 'ruckmeldung' },", app);
        Assert.Contains("case 'melden': return 'melden-' + (a || 'uebersicht');", app);
        var css = Datei("css", "melden.css");
        Assert.Contains(".mp-status.offen", css);
        Assert.Contains(".mf-kachel:hover", css);
    }

    // ------------------------------------------------------------------ Buchen

    [Fact]
    public void Buchen_Assistent_in_drei_Schritten_mit_Schnellbuchung_und_Storno_Dialog()
    {
        var js = Datei("js", "ansichten", "buchen.js");
        Assert.Contains("raum: { titel: 'Raum buchen', schritte: ['Termin', 'Raum', 'Abschluss'] },", js);
        Assert.Contains("ressource: { titel: 'Ressource buchen', schritte: ['Termin', 'Ressourcen', 'Abschluss'] },", js);
        Assert.DoesNotContain("'Kriterien'", js);
        Assert.Contains("kriterien: { maxBelegung: 100 }", js);
        Assert.Contains("opt.naechsterTag", js);
        Assert.Contains("export function stornoDialog(b, serienAnzahl = 0)", js);
        Assert.Contains("value=\"serie\"", js);
        Assert.Contains("function schnellbuchung(kontext, opt, klassen, raum, datum, stunde, raster, belegungVon)", js);
        Assert.Contains("data-buchung=\"${b.buchung}|", js);
        Assert.Contains("const ANSICHTEN = [['kommend', 'Kommend'], ['vergangen', 'Vergangen'], ['storniert', 'Storniert'], ['alle', 'Alle']];", js);
        Assert.Contains("data-serie-auf", js);
        Assert.Contains("Keine Buchungen für diesen Filter", js);
    }

    [Fact]
    public void Neue_und_geaenderte_Dateien_nur_Regular_Medium_ohne_Fremdnamen()
    {
        var verboten = new string(new[] { 'u', 'n', 't', 'i', 's' });
        foreach (var (inhalt, name) in new[]
        {
            (Datei("js", "raumwahl.js"), "raumwahl.js"), (Datei("css", "raumwahl.css"), "raumwahl.css"),
            (Datei("js", "ansichten", "melden.js"), "melden.js"), (Datei("css", "melden.css"), "melden.css"),
            (Datei("js", "ansichten", "buchen.js"), "buchen.js"), (Datei("css", "buchen.css"), "buchen.css"),
        })
        {
            Assert.False(inhalt.Contains(verboten, StringComparison.OrdinalIgnoreCase), name);
            foreach (var w in new[] { "font-weight: 600", "font-weight: 700", "font-weight: bold", "fw-semibold", "fw-bold" })
                Assert.False(inhalt.Contains(w), $"{name}: {w}");
        }
    }
}
