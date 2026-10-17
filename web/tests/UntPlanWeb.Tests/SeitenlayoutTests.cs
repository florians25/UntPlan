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
    public void Offene_Stunden_Art_als_Segment_statt_Auswahlliste()
    {
        var js = Datei("js", "ansichten", "offen.js");
        Assert.Contains("segment(ART_WAHL", js);
        Assert.DoesNotContain("<select data-artwahl", js);
        Assert.DoesNotContain("os-artwahl", js);
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
