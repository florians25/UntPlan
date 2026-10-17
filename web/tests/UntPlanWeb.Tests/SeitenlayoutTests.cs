using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprint 27 – Seiten und Bausteine: Detaildialog der Klassenbucheinträge (Liste mit Stunde, Zeitstempel und Rechten, Bearbeiten),
/// einheitliches Seitenlayout und Suchfeld, Segment-Auswahl (Rechte-Stufen, Art bei Offene Stunden), Rechteverwaltung mit automatisch
/// angezeigten neuen Rechten und das Infocenter auf kleinen Geräten.
/// </summary>
public class SeitenlayoutTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    // ------------------------------------------------------------------ Klassenbucheinträge: Detaildialog

    [Fact]
    public async Task Eintraege_liefern_Stunde_Zeitstempel_und_Rechte()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var sid = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a").Id;
        var erste = stand.Paket.Zeitraster.Stunden.OrderBy(z => z.Nr).First();
        var autor = await s.AlsAsync("autor", "lehrer", "AUT");
        var v = await Testserver.JsonAsync(await autor.PostAsJsonAsync("/api/app/klassenbuch/vermerke",
            new { datum = "2026-10-14", klasse = "7a", schuelerId = sid, art = "Lob", text = "Sehr gutes Referat", zeit = erste.Beginn }));
        var id = v["id"]!.GetValue<int>();

        var liste = await Testserver.HolenAsync(autor, "/api/app/klassenbucheintraege?von=2026-10-01&bis=2026-10-20&klasse=7a");
        var x = liste["eintraege"]!.AsArray().First(e => e!["id"]!.GetValue<int>() == id)!;
        Assert.Equal(erste.Beginn, x["zeit"]!.GetValue<string>());
        Assert.Equal(erste.Nr, x["stunde"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(x["erstellt"]!.GetValue<string>()));
        Assert.True(x["bearbeiten"]!.GetValue<bool>());
        Assert.True(x["loeschen"]!.GetValue<bool>());

        // andere Lehrkraft ohne Klassenleitung: nur lesen
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        var y = (await Testserver.HolenAsync(fremd, "/api/app/klassenbucheintraege?von=2026-10-01&bis=2026-10-20&klasse=7a"))["eintraege"]!.AsArray()
            .First(e => e!["id"]!.GetValue<int>() == id)!;
        Assert.False(y["bearbeiten"]!.GetValue<bool>());
        Assert.False(y["loeschen"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PutAsJsonAsync($"/api/app/klassenbuch/vermerke/{id}",
            new { datum = "2026-10-14", klasse = "7a", art = "Lob", text = "geändert" })).StatusCode);
    }

    [Fact]
    public async Task Eintrag_bearbeiten_durch_Verfasser_und_Administration()
    {
        await using var s = await Testserver.StartenAsync();
        var autor = await s.AlsAsync("autor", "lehrer", "AUT");
        var id = (await Testserver.JsonAsync(await autor.PostAsJsonAsync("/api/app/klassenbuch/vermerke",
            new { datum = "2026-10-14", klasse = "7a", art = "Vermerk", text = "Erst", zeit = "08:00" })))["id"]!.GetValue<int>();
        var neu = await Testserver.JsonAsync(await autor.PutAsJsonAsync($"/api/app/klassenbuch/vermerke/{id}",
            new { datum = "2026-10-13", klasse = "7a", art = "Elterngespräch", text = "  Gespräch geführt  " }));
        Assert.Equal("2026-10-13", neu["datum"]!.GetValue<string>());
        Assert.Equal("Elterngespräch", neu["art"]!.GetValue<string>());
        Assert.Equal("Gespräch geführt", neu["text"]!.GetValue<string>());
        Assert.Equal("08:00", neu["zeit"]!.GetValue<string>());            // ohne Angabe bleibt die Uhrzeit
        Assert.Equal("7a", neu["klasse"]!.GetValue<string>());

        // leerer Text und Schüler einer anderen Klasse werden abgelehnt
        Assert.Equal(HttpStatusCode.BadRequest, (await autor.PutAsJsonAsync($"/api/app/klassenbuch/vermerke/{id}", new { datum = "2026-10-13", klasse = "7a", art = "Vermerk", text = " " })).StatusCode);
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var andere = stand.Paket.Elemente.Schueler.First(x => x.Klasse != "7a").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await autor.PutAsJsonAsync($"/api/app/klassenbuch/vermerke/{id}", new { datum = "2026-10-13", klasse = "7a", schuelerId = andere, art = "Vermerk", text = "x" })).StatusCode);

        var admin = await s.AlsAsync("admin");
        var a = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/klassenbuch/vermerke/{id}", new { datum = "2026-10-13", klasse = "7a", art = "Lob", text = "Admin" }));
        Assert.Equal("Lob", a["art"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/klassenbuch/vermerke/999999", new { datum = "2026-10-13", klasse = "7a", art = "Lob", text = "x" })).StatusCode);
    }

    [Fact]
    public void Eintraege_Liste_oeffnet_Detaildialog_mit_kbDialog()
    {
        var js = Datei("js", "ansichten", "eintraege.js");
        Assert.Contains("from '../kbdialog.js'", js);
        Assert.Contains("export function detail(", js);
        Assert.Contains("tr[data-eintrag]", js);
        foreach (var feld in new[] { "'Datum'", "'Stunde'", "'Klasse'", "'Schüler*in'", "'Kategorie'", "'Verfasser*in'", "'Erfasst'" }) Assert.Contains(feld, js);
        Assert.Contains("x.bearbeiten", js);
        Assert.Contains("x.loeschen", js);
        Assert.Contains("'PUT'", js);
    }

    // ------------------------------------------------------------------ Gemeinsame Bausteine

    [Fact]
    public void Seitenlayout_Stilvorlage_eingebunden_und_Heute_ausgenommen()
    {
        var index = Datei("index.html");
        Assert.Contains("<link rel=\"stylesheet\" href=\"css/seitenlayout.css\">", index);
        Assert.True(index.IndexOf("css/seitenlayout.css", StringComparison.Ordinal) > index.IndexOf("css/kompakt.css", StringComparison.Ordinal));
        var css = Datei("css", "seitenlayout.css");
        Assert.Contains("--u-rand", css);
        Assert.Contains("--u-seite-max", css);
        // Container-Regeln gelten nie für die Startseite „Heute“
        foreach (var zeile in css.Split('\n').Where(z => z.Contains(".u-inhalt") && z.Contains("padding-left: var(--u-rand)")))
            Assert.Contains(":not([data-ansicht=\"heute\"])", zeile);
        Assert.Contains("table.u-karten-tabelle", css);
    }

    [Fact]
    public void Suchfeld_und_Segment_als_gemeinsame_Komponenten()
    {
        var ui = Datei("js", "ui.js");
        Assert.Contains("export const suchfeld", ui);
        Assert.Contains("export const segment", ui);
        Assert.Contains("export function segmentSetzen", ui);
        var css = Datei("css", "seitenlayout.css");
        // alle bisherigen Suchfeld-Varianten erhalten dieselbe Darstellung (graues Feld, runde Ecken, Fokus-Ring, Löschen-X)
        foreach (var k in new[] { ".u-suchfeld", ".au-suchfeld", ".nl-suche", ".br-suche", ".sp-suche", ".mt-ew-suche" }) Assert.Contains(k, css);
        Assert.Contains("border-radius: 20px", css);
        Assert.Contains(".au-x", css);
        Assert.Contains(".u-segment", css);
        // Löschen-X ergänzt auswahl.js für jedes Suchfeld automatisch
        Assert.Contains("suchfeldAufwerten", Datei("js", "auswahl.js"));
    }

    [Fact]
    public void Offene_Stunden_Art_als_Dropdown_im_Suchfeld_im_weissen_Kopf()
    {
        var js = Datei("js", "ansichten", "offen.js");
        // Art der Anzeige als Dropdown im gemeinsamen Suchfeld (wie Hausaufgaben) statt Pillen-Gruppe
        Assert.Contains("suchfeldMitFilter({", js);
        Assert.Contains("filter: [{ name: 'art', label: 'Anzeigen'", js);
        Assert.Contains("suchfeldMitFilterBinden(w, {", js);
        Assert.DoesNotContain("segment(ART_WAHL", js);
        Assert.DoesNotContain("data-artwahl", js);
        Assert.DoesNotContain("os-artwahl", js);
        // Leiste als Teil des weißen Kopfes (u-kopfteil, app.js/seitenlayout.css), nicht mehr in der nachträglich gefüllten Werkzeugleiste
        Assert.DoesNotContain("kontext.werkzeug(", js);
        Assert.Contains("<div class=\"u-sf-leiste u-kopfteil os-filter\">", js);
    }

    [Fact]
    public void Suchfeld_mit_Filter_als_gemeinsame_Komponente()
    {
        var ui = Datei("js", "ui.js");
        Assert.Contains("export const suchfeldMitFilter = (", ui);
        Assert.Contains("export function suchfeldMitFilterBinden(", ui);
        Assert.Contains("export const zeitraumPillen = (", ui);
        Assert.Contains("export function zeilenFiltern(", ui);
        Assert.Contains("data-chip-weg", ui);
        Assert.Contains("data-sf-weg", ui);
        Assert.Contains("ev.key !== 'Backspace'", ui);
        var css = Datei("css", "seitenlayout.css");
        foreach (var k in new[] { ".u-suchfilter", ".u-sf-filter.gewaehlt", ".u-sf-chip", ".u-sf-leiste", ".u-suchfilter .u-trenn", ".u-suchfilter.au-mit-x > .au-x" }) Assert.Contains(k, css);
        Assert.DoesNotContain("font-weight: 600", css);
        Assert.DoesNotContain("font-weight: 700", css);
        // alle Listenseiten mit Suche und Filter nutzen die Komponente
        foreach (var datei in new[] { "hausaufgaben.js", "offen.js", "pruefungen.js", "abwesenheiten.js", "klassenbuch.js", "entschuldigungen.js", "eintraege.js", "dienste.js",
                     "befreiungen.js", "melden.js", "mitteilungen.js", "neuigkeiten.js", "benutzerverwaltung.js", "rechte.js", "stammdaten.js", "berichte.js" })
        {
            var js = Datei("js", "ansichten", datei);
            Assert.True(js.Contains("suchfeldMitFilter(") && js.Contains("suchfeldMitFilterBinden("), datei);
        }
        Assert.Contains("suchfeldMitFilterBinden(", Datei("js", "infocenter.js"));
        // keine weiße Werkzeugleiste mehr auf den Listenseiten
        foreach (var datei in new[] { "hausaufgaben.js", "pruefungen.js", "offen.js" }) Assert.DoesNotContain("kontext.werkzeug(", Datei("js", "ansichten", datei));
    }

    // ------------------------------------------------------------------ Sprint 28: weißer Kopf der Klassenbuch-Verwaltungsseiten

    [Fact]
    public void Klassenbuch_Verwaltungsseiten_haben_weissen_Kopf_ohne_Springen()
    {
        var app = Datei("js", "app.js");
        // Kopf steht sofort beim Seitenwechsel (vor dem Laden des Moduls): Titel aus dem Menü, Platzhalter für die Suchleiste
        Assert.Contains("const WEISSER_KOPF = {", app);
        Assert.Contains("id=\"kopfplatz\"", app);
        var route = app[app.IndexOf("async function route()", StringComparison.Ordinal)..];
        Assert.True(route.IndexOf("weisserKopf(nav)", StringComparison.Ordinal) < route.IndexOf("await ANSICHTEN[name]()", StringComparison.Ordinal));
        Assert.Contains("kopfMerken(kopf)", route);
        Assert.Contains("sessionStorage", app[app.IndexOf("function weisserKopf", StringComparison.Ordinal)..]);
        foreach (var nav in new[] { "offen", "hausaufgaben", "pruefungen", "abwesenheiten", "fehlzeiten", "eintraege", "dienste", "befreiungen", "berichte", "entschuldigen", "kbeinstellungen" })
            Assert.Matches($"[{{ ,]'?{nav}'?: {{ suche", app);

        var css = Datei("css", "seitenlayout.css");
        Assert.Contains(".u-app.u-kopf-weiss :is(#seitenkopf, #kopfplatz, #seite .u-kopfteil)::after", css);
        Assert.Contains("inset: 0 -100vmax", css);                     // volle Breite …
        Assert.Contains(".u-app.u-kopf-weiss .u-haupt { overflow-x: clip; }", css);   // … ohne waagrechtes Scrollen
        Assert.Contains(":not(:has(#seite .u-kopfteil, #seite > .alert)) #kopfplatz { display: block; height: var(--kopfplatz-h", css);
        Assert.Contains("height: 82px", css);                          // feste Höhe der Titelzeile (Aktionen kommen später)
        Assert.DoesNotContain("font-weight: 600", css);

        // jede Seite zeichnet ihre Suchleiste als Teil des Kopfes, keine nachträglich gefüllte Werkzeugleiste
        foreach (var datei in new[] { "offen.js", "hausaufgaben.js", "pruefungen.js", "abwesenheiten.js", "eintraege.js", "dienste.js", "befreiungen.js", "berichte.js", "entschuldigungen.js", "klassenbuch.js" })
        {
            var js = Datei("js", "ansichten", datei);
            Assert.True(js.Contains("u-kopfteil"), datei);
            if (datei != "klassenbuch.js") Assert.DoesNotContain("kontext.werkzeug(", js);
        }
        // Abwesenheiten: Reiter gehört zum Kopf, Platzhalter der Suchleiste während des Ladens
        var abw = Datei("js", "ansichten", "abwesenheiten.js");
        Assert.Contains("u-kopfreiter", abw);
        Assert.Contains("u-kopfskelett", abw);
    }

    // ------------------------------------------------------------------ Sprint 28: eigene Abwesenheit im Stundenplan

    [Fact]
    public async Task Eigener_Plan_liefert_eigene_Abwesenheiten_der_Lehrkraft()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var stunden = stand.Paket.Zeitraster.Stunden.Where(z => z.Beginn.Length > 0).OrderBy(z => z.Nr).ToList();
        // Beispielpaket: WAG abwesend am 14.10. in der 1.–3. Stunde
        var wag = await s.AlsAsync("wag", "lehrer", "WAG");
        var a = (await Testserver.HolenAsync(wag, "/api/app/plan?typ=lehrer&element=WAG&datum=2026-10-14"))["abwesenheiten"]!.AsArray();
        var tag = a.Single(x => x!["datum"]!.GetValue<string>() == "2026-10-14")!;
        Assert.Equal(stunden.First().Beginn, tag["von"]!.GetValue<string>());
        Assert.Equal(stunden.First(z => z.Nr == 3).Ende, tag["bis"]!.GetValue<string>());
        Assert.Equal("fehlt", tag["art"]!.GetValue<string>());
        // nur im eigenen Plan: andere Lehrkraft bzw. Administration sehen im Plan von WAG keine Abwesenheitsfläche
        Assert.Empty((await Testserver.HolenAsync(await s.AlsAsync("mue", "lehrer", "MUE"), "/api/app/plan?typ=lehrer&element=WAG&datum=2026-10-14"))["abwesenheiten"]!.AsArray());
        Assert.Empty((await Testserver.HolenAsync(await s.AlsAsync("admin"), "/api/app/plan?typ=lehrer&element=WAG&datum=2026-10-14"))["abwesenheiten"]!.AsArray());
        // ganztägig (bis zur letzten Stunde) über zwei Tage: HOF 12.–13.10.
        var hof = (await Testserver.HolenAsync(await s.AlsAsync("hof", "lehrer", "HOF"), "/api/app/plan?typ=lehrer&element=HOF&datum=2026-10-13"))["abwesenheiten"]!.AsArray();
        Assert.Equal(new[] { "2026-10-12", "2026-10-13" }, hof.Select(x => x!["datum"]!.GetValue<string>()).ToArray());
        Assert.All(hof, x => Assert.Equal(stunden.Last().Ende, x!["bis"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Eigener_Plan_liefert_eigene_Abwesenheiten_der_Schuelerin()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var kind = stand.Schueler.First(x => x.Klasse == "7a");
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login = "kind-7a", passwort = "familie-passwort", rolle = "schueler", anzeigename = "Kind", gesperrt = false, schuelerIds = new[] { kind.Id } }));
        var neu = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kind.Id, von = "2026-10-15", bis = "2026-10-15", status = "offen", grund = "K" }));
        Assert.True(neu["stunden"]!.GetValue<int>() > 0);
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login = "kind-7a", passwort = "familie-passwort" })).StatusCode);
        var a = (await Testserver.HolenAsync(c, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-15"))["abwesenheiten"]!.AsArray();
        var tag = Assert.Single(a)!;
        Assert.Equal("2026-10-15", tag["datum"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(tag["von"]!.GetValue<string>()));
        Assert.True(string.CompareOrdinal(tag["von"]!.GetValue<string>(), tag["bis"]!.GetValue<string>()) < 0);
        // Klassenkonto bzw. Lehrkraft sehen im Klassenplan keine Abwesenheit einzelner Schüler*innen
        Assert.Empty((await Testserver.HolenAsync(await s.AlsAsync("mue", "lehrer", "MUE"), "/api/app/plan?typ=klasse&element=7a&datum=2026-10-15"))["abwesenheiten"]!.AsArray());
    }

    [Fact]
    public void Abwesenheit_als_Flaeche_hinter_den_Stunden_und_im_Menue_ausblendbar()
    {
        var gitter = Datei("js", "plangitter.js");
        Assert.Contains("function abwesenheitFlaechen(", gitter);
        Assert.Contains("plan.abwesenheiten", gitter);
        // vor den Karten eingefügt (liegt hinter den Stunden), Text im freien Bereich
        Assert.Contains("html = html.slice(0, vorKarten) + abw + html.slice(vorKarten)", gitter);
        Assert.Contains("tt-abw-text", gitter);
        var plan = Datei("js", "ansichten", "plan.js");
        Assert.Contains("Abwesenheiten anzeigen</button>", plan);
        Assert.Contains("localStorage.setItem('untplan:planeinstellungen'", plan);
        var css = Datei("css", "app.css");
        Assert.Contains(".tt.ohne-abwesenheiten .tt-abw { display: none; }", css);
        var regel = css.Split('\n').First(z => z.StartsWith(".tt-abw {", StringComparison.Ordinal));
        Assert.DoesNotContain("z-index", regel);
    }

    [Fact]
    public void Rechteverwaltung_zeigt_neue_Rechte_und_Gruppen_automatisch()
    {
        var js = Datei("js", "ansichten", "rechte.js");
        // Gruppen aus dem Katalog, ergänzt um Gruppen neuer Rechte; Werte fehlender Rechte aus dem Standard der Basisrolle
        Assert.Contains("const gruppenAus = k =>", js);
        Assert.Contains("r.standard?.[p.basis]", js);
        Assert.Contains("k.rechte.filter(", js);
        // Stufen als Segment-Pillen, fixierte Spaltenköpfe, Rolle-für-Rolle-Ansicht auf kleinen Bildschirmen
        Assert.Contains("'Kein Zugriff'", js);
        Assert.Contains("'Lesen'", js);
        Assert.Contains("'Schreiben'", js);
        Assert.Contains("rm-kopfzeile", js);
        Assert.Contains("position: sticky", js);
        Assert.Contains("matchMedia('(max-width: 991.98px)')", js);
        Assert.Contains("rolleAnsicht", js);
    }

    [Fact]
    public async Task Rechtekatalog_enthaelt_Standard_je_Basisrolle_fuer_jedes_Recht()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var k = await Testserver.HolenAsync(admin, "/api/app/admin/rechte/katalog");
        var gruppen = k["gruppen"]!.AsArray().Select(g => g!.GetValue<string>()).ToHashSet();
        foreach (var r in k["rechte"]!.AsArray())
        {
            Assert.Contains(r!["gruppe"]!.GetValue<string>(), gruppen);
            foreach (var basis in new[] { "admin", "leitung", "lehrer", "schueler", "eltern" }) Assert.NotNull(r["standard"]![basis]);
        }
    }

    [Fact]
    public void Infocenter_auf_dem_Telefon_als_Bodenblatt_mit_Schleier()
    {
        Assert.Contains("data-ic-schleier", Datei("js", "infocenter.js"));
        var css = Datei("css", "seitenlayout.css");
        Assert.Contains(".ic-schleier", css);
        Assert.Contains("border-radius: 18px 18px 0 0", css);
        Assert.Contains("env(safe-area-inset-bottom)", css);
    }
}
