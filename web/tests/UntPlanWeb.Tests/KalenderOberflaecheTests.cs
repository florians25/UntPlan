using System.Net;
using System.Net.Http.Json;

namespace UntPlanWeb.Tests;

/// <summary>
/// Eigener Kalender (Datumswähler) für alle Datumsfelder, Unterstützung kleinerer Notebook-Bildschirme, überarbeitete Anmeldeseite,
/// Abstand in der Navigation und rahmenlose Auswahl im Kopf des Stundenplans.
/// </summary>
public class KalenderOberflaecheTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    // ------------------------------------------------------------------ Schnittstelle für den Kalender

    [Fact]
    public async Task Kalender_liefert_Ferien_Feiertage_und_eigene_freie_Tage()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/freietage", new { von = "2026-11-20", bis = "2026-11-20", art = "frei", name = "Pädagogischer Tag" }));

        var c = await s.AlsAsync("mue", "lehrer", "MUE");
        var k = await Testserver.HolenAsync(c, "/api/app/kalender");
        var tage = k["freieTage"]!.AsArray().Select(t => t!).ToList();
        Assert.Contains(tage, t => t["name"]!.GetValue<string>() == "Herbstferien" && t["art"]!.GetValue<string>() == "ferien" && t["von"]!.GetValue<string>() == "2026-10-17");
        Assert.Contains(tage, t => t["art"]!.GetValue<string>() == "feiertag" && t["von"]!.GetValue<string>() == "2026-10-03");
        Assert.Contains(tage, t => t["name"]!.GetValue<string>() == "Pädagogischer Tag" && t["art"]!.GetValue<string>() == "frei");
        // nach Beginn sortiert, jeweils ISO-Daten
        var von = tage.Select(t => t["von"]!.GetValue<string>()).ToList();
        Assert.Equal(von.OrderBy(x => x, StringComparer.Ordinal), von);
        Assert.All(tage, t => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", t["bis"]!.GetValue<string>()));
        Assert.NotNull(k["schuljahr"]?["von"]);
    }

    [Fact]
    public async Task Kalender_nur_fuer_angemeldete_Benutzer()
    {
        await using var s = await Testserver.StartenAsync();
        var r = await s.Client().GetAsync("/api/app/kalender");
        Assert.False(r.IsSuccessStatusCode);
        Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    // ------------------------------------------------------------------ Kalender-Komponente

    [Fact]
    public void Kalender_ersetzt_alle_Datumsfelder_automatisch()
    {
        var js = Datei("js", "kalender.js");
        var app = Datei("js", "app.js");
        var index = Datei("index.html");
        // überall eingesetzt: MutationObserver stellt jedes <input type="date"> / "datetime-local" um, Start in app.js
        Assert.Contains("import { beobachten as kalenderBeobachten } from './kalender.js';", app);
        Assert.Contains("kalenderBeobachten(document.body);", app);
        Assert.Contains("input[type=date]:not([data-kal])", js);
        Assert.Contains("input[type=datetime-local]:not([data-kal])", js);
        Assert.Contains("new MutationObserver(", js);
        Assert.Contains("<link rel=\"stylesheet\" href=\"css/kalender.css\">", index);
        // .value bleibt ISO, Anzeige deutsch, Ereignisse wie bisher
        Assert.Contains("Object.defineProperty(input, 'value'", js);
        Assert.Contains("input.type = 'text';", js);
        Assert.Contains("new Event('change', { bubbles: true })", js);
        Assert.Contains("export function datumLesen(", js);
        // kein Fremd-Picker und kein nativer Picker mehr
        Assert.DoesNotContain("showPicker()", Datei("js", "ansichten", "klassenbuch.js"));
        Assert.DoesNotContain("os-kalender", Datei("js", "ansichten", "offen.js"));
        Assert.DoesNotContain("flatpickr", js, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Kalender_hat_Monatsansicht_KW_heute_Ferien_Tastatur_Woche_und_Bereich()
    {
        var js = Datei("js", "kalender.js");
        var css = Datei("css", "kalender.css");
        Assert.Contains("export function kalenderwoche(", js);
        Assert.Contains("<span class=\"kal-kw\">KW</span>", js);
        Assert.Contains("kl.push('k-heute')", js);
        Assert.Contains("aria-current=\"date\"", js);
        Assert.Contains("fetch('/api/app/kalender'", js);
        Assert.Contains("kl.push('k-frei', `k-frei-${frei.art}`)", js);
        foreach (var taste in new[] { "'ArrowLeft'", "'ArrowRight'", "'ArrowUp'", "'ArrowDown'", "'PageUp'", "'PageDown'", "'Home'", "'End'", "'Escape'" })
            Assert.Contains(taste, js);
        // Modi: Tag, Woche (Stundenplan-Kopf), Bereich (Von/Bis-Paare), Uhrzeit (datetime-local)
        Assert.Contains("this.modus === 'woche'", js);
        Assert.Contains("modus: 'bereich'", js);
        Assert.Contains("function partner(input)", js);
        Assert.Contains("data-zeit", js);
        // Monats- und Jahresübersicht, Telefon als Blatt am unteren Rand
        Assert.Contains("data-ansicht=\"monate\"", js);
        Assert.Contains("data-ansicht=\"jahre\"", js);
        Assert.Contains(".kal-pop.kal-blatt", css);
        // Ferien dezent, Feiertage mit Punkt, heute als Ring
        Assert.Contains(".kal-tag.k-frei-ferien::before", css);
        Assert.Contains(".kal-tag.k-frei-feiertag::after", css);
        Assert.Contains(".kal-tag.k-heute::before { box-shadow: inset 0 0 0 1.5px var(--kal-akzent); }", css);
    }

    [Fact]
    public void Plan_und_Tagesansicht_nutzen_den_eigenen_Kalender()
    {
        var plan = Datei("js", "ansichten", "plan.js");
        Assert.Contains("export { kalenderwoche, kalenderBinden } from '../kalender.js';", plan);
        Assert.DoesNotContain("export function kalenderHtml(", plan);
        Assert.Contains("kalenderBinden(seite.querySelector('[data-kalender]'), mo, zuDatum);", plan);
        Assert.Contains("import { kalenderBinden } from './plan.js';", Datei("js", "ansichten", "tag.js"));
        Assert.DoesNotContain(".u-kalender {", Datei("css", "app.css"));
    }

    // ------------------------------------------------------------------ Kleinere Notebooks

    [Fact]
    public void Kleinere_Notebooks_schmalere_Navigation_und_skalierendes_Raster()
    {
        var css = Datei("css", "kompakt.css");
        Assert.Contains("<link rel=\"stylesheet\" href=\"css/kompakt.css\">", Datei("index.html"));
        Assert.Contains("@media (min-width: 992px) and (max-width: 1440px) {\n  :root { --u-leiste: 248px; }", css);
        Assert.Contains("@media (min-width: 992px) and (max-width: 1300px) {\n  :root { --u-leiste: 232px; }", css);
        Assert.Contains("@media (min-width: 992px) and (max-height: 860px)", css);
        Assert.Contains(".tt { container: plan-raster / inline-size; }", css);
        Assert.Contains("@container plan-raster (max-width: 1000px)", css);
        Assert.Contains("@container plan-raster (max-width: 820px)", css);
        Assert.Contains("--tt-spalt: clamp(8px, .9vw, 14px)", css);
        Assert.Contains(".tagplan-gitter { min-width: min(900px, 100%); }", css);
    }

    // ------------------------------------------------------------------ Anmeldung, Navigation, Kopf des Stundenplans

    [Fact]
    public void Anmeldeseite_ist_zweigeteilt_mit_Formularkarte()
    {
        var app = Datei("js", "app.js");
        var css = Datei("css", "anmeldung.css");
        Assert.Contains("<main class=\"anmeldung an-seite\">", app);
        Assert.Contains("<section class=\"an-buehne\"", app);
        Assert.Contains("<div class=\"an-karte\">", app);
        Assert.Contains("function anmeldeBild()", app);
        Assert.DoesNotContain("lg-leiste", app);
        Assert.DoesNotContain(".lg-leiste", Datei("css", "seiten.css"));
        Assert.Contains("<link rel=\"stylesheet\" href=\"css/anmeldung.css\">", Datei("index.html"));
        Assert.Contains("grid-template-columns: minmax(0, 46fr) minmax(420px, 54fr)", css);
        Assert.Contains(".an-seite .lg-login { height: 48px; margin-top: 8px; border-radius: 999px;", css);
        Assert.Contains("var(--u-akzent, #f56b2a)", css);
    }

    [Fact]
    public void Navigation_hat_mehr_Abstand_zum_Schuljahr()
    {
        var css = Datei("css", "app.css");
        Assert.Contains(".u-jahrzeile { padding: 15px 0 22px 71px; min-height: 59px; }", css);
        Assert.Contains(".mit-unter .u-jahrzeile { padding-left: 76px; padding-top: 19px; }", css);
    }

    [Fact]
    public void Auswahl_im_Plankopf_zeigt_den_Rahmen_nur_beim_Ueberfahren()
    {
        var css = Datei("css", "app.css");
        Assert.Contains(".element-pill .wahl-knopf { background: transparent !important; border: 1.5px solid transparent !important;", css);
        Assert.Contains(".element-pill .wahl-knopf:is(:hover, :focus-visible, [aria-expanded=\"true\"]) { border-color: var(--u-text) !important;", css);
        Assert.Contains("border: 1px solid transparent; border-radius: 999px;", css);
        Assert.Contains(".zeitraum.kal-pille:hover, .zeitraum.kal-pille[aria-expanded=\"true\"] { border-color: #67747e;", css);
    }

    [Fact]
    public void Neue_Dateien_ohne_Fremdnamen_und_nur_Regular_Medium()
    {
        var fremd = new string(new[] { 'u', 'n', 't', 'i', 's' });
        foreach (var inhalt in new[] { Datei("js", "kalender.js"), Datei("css", "kalender.css"), Datei("css", "kompakt.css"), Datei("css", "anmeldung.css") })
        {
            Assert.DoesNotContain(fremd, inhalt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("font-weight: 600", inhalt);
            Assert.DoesNotContain("font-weight: 700", inhalt);
            Assert.DoesNotContain("font-weight: bold", inhalt);
        }
    }
}
