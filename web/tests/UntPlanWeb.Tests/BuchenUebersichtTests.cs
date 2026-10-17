using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Buchen: Ressourcen mit Standort und Symbol je Art, Beispiel-Ressourcen, Ressourcenübersicht für einen Tag mit gebuchter Menge je Stunde
/// (Verfügbarkeit, Zeitstrahl), Rechte in den Optionen sowie die überarbeiteten Übersichten (Belegungsraster mit Filtern, Zoom, Hover,
/// Liste je Raum auf dem Telefon; Ressourcen als Karten bzw. Liste mit Buchen direkt aus der Liste). Feste Uhr: Mittwoch, 14.10.2026.
/// </summary>
public class BuchenUebersichtTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public async Task Ressource_mit_Standort_und_Art_mit_Symbol()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var art = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcenarten", new { name = "Beamer", reihenfolge = 10, symbol = "projector" }));
        Assert.Equal("projector", art["symbol"]!.GetValue<string>());
        var artId = art["id"]!.GetValue<int>();
        // unbekanntes Symbol wird abgelehnt
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/buchen/ressourcenarten", new { name = "X", symbol = "gibtsnicht" })).StatusCode);
        // Umbenennen ohne Symbol behält das Symbol
        var umbenannt = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/buchen/ressourcenarten/{artId}", new { name = "Projektor", reihenfolge = 10 }));
        Assert.Equal("projector", umbenannt["symbol"]!.GetValue<string>());

        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Beamer mobil 1", artId, anzahl = 2, standort = "Medienraum" }));
        Assert.Equal("Medienraum", r["standort"]!.GetValue<string>());
        Assert.Equal("projector", r["symbol"]!.GetValue<string>());
        // Ändern ohne Standort behält ihn; feste Ressourcen haben keinen Standort
        var id = r["id"]!.GetValue<int>();
        r = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/buchen/ressourcen/{id}", new { name = "Beamer mobil 1", artId, anzahl = 3 }));
        Assert.Equal("Medienraum", r["standort"]!.GetValue<string>());
        var fest = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Tafel", artId, raum = "PH1", standort = "egal" }));
        Assert.Equal("", fest["standort"]!.GetValue<string>());

        // Suche liefert Standort und Symbol
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var suche = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/suche/ressourcen",
            new { zeit = new { wiederholung = "einmalig", datum = "2026-10-15", stundeVon = 3, stundeBis = 3 } }));
        var treffer = suche["ressourcen"]!.AsArray().Single()!;
        Assert.Equal("Medienraum", treffer["standort"]!.GetValue<string>());
        Assert.Equal("projector", treffer["symbol"]!.GetValue<string>());
        // Optionen nennen die Buchungsrechte
        var opt = await Testserver.HolenAsync(lan, "/api/app/buchen/optionen");
        Assert.True(opt["raumBuchen"]!.GetValue<bool>());
        Assert.True(opt["ressourcenBuchen"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Beispiel_Ressourcen_nur_fuer_die_Administration_und_nur_einmal()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        Assert.NotEqual(HttpStatusCode.OK, (await lan.PostAsJsonAsync("/api/app/buchen/ressourcen/beispiele", new { })).StatusCode);
        var admin = await s.AlsAsync("admin");
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen/beispiele", new { }));
        Assert.True(r["angelegt"]!.GetValue<int>() >= 9);
        var liste = (await Testserver.HolenAsync(admin, "/api/app/buchen/ressourcen")).AsArray();
        Assert.Contains(liste, x => x!["name"]!.GetValue<string>() == "iPad-Koffer" && x["anzahl"]!.GetValue<int>() == 4 && x["standort"]!.GetValue<string>() == "Sekretariat");
        Assert.Contains(liste, x => x!["name"]!.GetValue<string>() == "Interaktive Tafel PH1" && x["raum"]!.GetValue<string>() == "PH1");
        Assert.All(liste, x => Assert.NotEqual("", x!["symbol"]!.GetValue<string>()));
        // zweites Mal: schon Ressourcen vorhanden
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/buchen/ressourcen/beispiele", new { })).StatusCode);
        Assert.NotEmpty((await Testserver.HolenAsync(admin, "/api/app/buchen/symbole")).AsArray());
    }

    [Fact]
    public async Task Ressourcenuebersicht_fuer_einen_Tag_mit_gebuchter_Menge_je_Stunde()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "iPad-Koffer", anzahl = 4, standort = "Sekretariat" })))["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen/ressourcen", new { name = "Tafel PH1", raum = "PH1" }));
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "ressource", termine = new[] { new { datum = "2026-10-15", stundeVon = 2, stundeBis = 3, raum = (string?)null } }, ressourcen = new[] { new { id, menge = 3 } }, titel = "Recherche",
        }));
        var u = await Testserver.HolenAsync(lan, "/api/app/buchen/ressourcenuebersicht?datum=2026-10-15&tage=1");
        Assert.Equal("2026-10-15", u["tage"]!.AsArray().Single()!["datum"]!.GetValue<string>());
        var koffer = u["ressourcen"]!.AsArray().Single(r => r!["id"]!.GetValue<int>() == id)!;
        Assert.Equal("Sekretariat", koffer["standort"]!.GetValue<string>());
        var g = koffer["gebucht"]!["2026-10-15"]!;
        Assert.Equal(3, g["2"]!.GetValue<int>());
        Assert.Equal(3, g["3"]!.GetValue<int>());
        Assert.Null(g["4"]);
        Assert.Equal("Recherche", koffer["belegung"]!["2026-10-15"]!.AsArray().Single()!["text"]!.GetValue<string>());
        // feste Ressource: Stunden, in denen ihr Raum belegt ist, mit voller Anzahl
        var tafel = u["ressourcen"]!.AsArray().Single(r => r!["raum"]!.GetValue<string>() == "PH1")!;
        Assert.All(tafel["gebucht"]!["2026-10-15"]!.AsObject(), x => Assert.Equal(1, x.Value!.GetValue<int>()));
        // ohne tage: ganze Woche (wie bisher)
        var w = await Testserver.HolenAsync(lan, "/api/app/buchen/ressourcenuebersicht?datum=2026-10-15");
        Assert.True(w["tage"]!.AsArray().Count >= 5);
    }

    [Fact]
    public void Raumuebersicht_Raster_mit_Filtern_Zoom_Hover_und_Telefonliste()
    {
        var js = Datei("js", "ansichten", "buchen.js");
        var css = Datei("css", "buchen.css");
        // Raster: feste Kopfzeile und Raumspalte, Blöcke zusammenhängender Belegung, Gruppen je Gebäude, aktuelle Stunde
        Assert.Contains("const laeufe = (r, t) =>", js);
        Assert.Contains("class=\"bu-gruppe\"", js);
        Assert.Contains(".bu-uebersicht thead th { position: sticky; top: 0;", css);
        Assert.Contains(".bu-raumname { position: sticky; left: 0;", css);
        // Farben je Art
        foreach (var art in new[] { "unterricht", "buchung", "klausur", "termin", "vorgemerkt", "gesperrt" })
            Assert.Contains($".bu-zelle.{art} .bu-block", css);
        // Filter: Verfügbarkeit (frei in Stunde), Zoom, Hover-Details, Telefon: Liste je Raum, Schnellbuchung bleibt
        Assert.Contains("{ name: 'frei', label: 'Verfügbarkeit'", js);
        Assert.Contains("const ZOOM = [['kompakt', 'Kompakt'", js);
        Assert.Contains(".bu-uebersicht.zoom-kompakt", css);
        Assert.Contains("function hoverVerbinden(wurzel, inhalt)", js);
        Assert.Contains("const SCHMAL = '(max-width: 767.98px)';", js);
        Assert.Contains("class=\"bu-ru-liste", js);
        Assert.Contains("schnellbuchung(kontext, opt, el.klassen,", js);
        // Ressourcen: Symbol je Art, Karten/Liste, Zeitstrahl, Buchen direkt aus der Liste
        Assert.Contains("export const ressourcenSymbol = r =>", js);
        Assert.Contains("[['karten', 'Karten'", js);
        Assert.Contains("class=\"bu-strahl", js);
        Assert.Contains("function ressourceBuchen(kontext, opt, r, datum, von, bis, raster)", js);
        Assert.Contains("#/buchen/ressource/${w('datum').value}", js);
        // Verwaltung: Standort, Symbolwahl, Beispiele
        var vw = Datei("js", "ansichten", "buchungsverwaltung.js");
        Assert.Contains("name=\"standort\"", vw);
        Assert.Contains("bu-symbolwahl", vw);
        Assert.Contains("/api/app/buchen/ressourcen/beispiele", vw);
        // gemeinsame Suchfeld-mit-Filter-Komponente statt eigener Suchfelder
        Assert.Contains("<div class=\"u-sf-leiste bu-leiste\">", js);
        Assert.Contains("<div class=\"u-sf-leiste bu-ru-leiste\">", js);
        Assert.Contains("suchfeldMitFilterBinden(", vw);
        Assert.DoesNotContain("nl-suche", js);
        Assert.DoesNotContain("nl-suche", vw);
        // Browser-Speicher nur mit try/catch
        Assert.Contains("try { localStorage.setItem(`untplan:buchen:", js);
    }

    [Fact]
    public void Buchen_Dateien_nur_Regular_Medium_ohne_Fremdnamen()
    {
        var verboten = new string(new[] { 'u', 'n', 't', 'i', 's' });
        foreach (var (inhalt, name) in new[]
        {
            (Datei("js", "ansichten", "buchen.js"), "buchen.js"), (Datei("css", "buchen.css"), "buchen.css"),
            (Datei("js", "ansichten", "buchungsverwaltung.js"), "buchungsverwaltung.js"),
        })
        {
            Assert.False(inhalt.Contains(verboten, StringComparison.OrdinalIgnoreCase), name);
            foreach (var w in new[] { "font-weight: 600", "font-weight: 700", "font-weight: bold", "fw-semibold", "fw-bold" })
                Assert.False(inhalt.Contains(w), $"{name}: {w}");
        }
    }
}
