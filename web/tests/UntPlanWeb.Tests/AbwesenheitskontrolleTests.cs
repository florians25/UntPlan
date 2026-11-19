using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Kontrolle der Abwesenheiten durch die Klassenleitung (Ende zu Ende): Entschuldigungsstatus mit Namen, nicht zählende Abwesenheitsgründe,
/// „Zusammenfassen“ aller dargestellten Abwesenheiten, Fehlzeiten pro Klasse für das Zeugnis, Fehlzeiten pro Schüler*in „pro Tag“,
/// Klassenbuch Deckblatt, Klassenbucheinträge nach Kategorie und die Berichte zum Unterricht (75 %-Regel, Fehlstundenmatrix).
/// </summary>
public class AbwesenheitskontrolleTests
{
    private static List<string> Spalte(JsonNode bericht, int i) => bericht["zeilen"]!.AsArray().Select(z => z![i]?.ToString() ?? "").ToList();

    [Fact]
    public async Task Status_Gruende_Zusammenfassen_und_Berichte()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var kinder = stand.Paket.Elemente.Schueler.Where(x => x.Klasse == "7a").OrderBy(x => x.Name).Take(3).ToList();

        // Entschuldigungsstatus der Liste – nur „Entschuldigt“ zählt als entschuldigt
        var liste = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/statusliste");
        Assert.Equal(12, liste["status"]!.AsArray().Count);
        Assert.Equal(Fehlzeitenrechnung.ZusammenfassenHinweis, liste["zusammenfassen"]!.GetValue<string>());

        // Kind 1: ganzer Tag krank, Status „u-1Mahn“ (erste Mahnung) → nicht entschuldigt, Name bleibt sichtbar
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[0].Id, von = "2026-10-12", bis = "2026-10-12", grund = "K" }));
        var zr = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-14&klasse=7a");
        var a1 = zr.AsArray().First(z => z!["schuelerId"]!.GetValue<int>() == kinder[0].Id)!;
        var ids1 = a1["ids"]!.AsArray().Select(i => i!.GetValue<int>()).ToList();
        await Testserver.JsonAsync(await leitung.PutAsJsonAsync("/api/app/abwesenheiten", new { ids = ids1, status = "u-1Mahn" }));
        zr = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-14&klasse=7a");
        a1 = zr.AsArray().First(z => z!["schuelerId"]!.GetValue<int>() == kinder[0].Id)!;
        Assert.Equal(("unentschuldigt", "u-1Mahn"), (a1["status"]!.GetValue<string>(), a1["statusName"]!.GetValue<string>()));
        Assert.True(a1["loeschbar"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await leitung.PutAsJsonAsync("/api/app/abwesenheiten", new { ids = ids1, status = "Chronische Unlust" })).StatusCode);

        // Kind 2: andere schulische Veranstaltung („Schul“) – Fehlstunden, die nicht zählen
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[1].Id, von = "2026-10-14", bis = "2026-10-14", grund = "Schul", status = "entschuldigt" }));
        var fz = await Testserver.HolenAsync(leitung, $"/api/app/abwesenheiten/fehlzeiten?von=2026-10-12&bis=2026-10-14&klasse=7a&schueler={kinder[1].Id}");
        Assert.NotEmpty(fz.AsArray());
        Assert.All(fz.AsArray(), z => Assert.False(z!["zaehlt"]!.GetValue<bool>()));
        Assert.Contains((await Testserver.HolenAsync(leitung, "/api/app/abwesenheitsgruende?art=abwesenheit")).AsArray(),
            g => g!["kurz"]!.GetValue<string>() == "Schul" && !g["zaehlt"]!.GetValue<bool>());

        // Kind 3: Mo und Mi einzeln erfasst (Di entfällt der Unterricht der 7a) → „Zusammenfassen“ ergibt eine Abwesenheit
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[2].Id, von = "2026-10-12", bis = "2026-10-12", grund = "A" }));
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kinder[2].Id, von = "2026-10-14", bis = "2026-10-14", grund = "A" }));
        int Anzahl(JsonNode z, int sid) => z.AsArray().Count(x => x!["schuelerId"]!.GetValue<int>() == sid);
        zr = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-14&klasse=7a");
        Assert.Equal(2, Anzahl(zr, kinder[2].Id));
        var zf = await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten/zusammenfassen-bereich", new { von = "2026-10-12", bis = "2026-10-14", klasse = "7a" }));
        Assert.True(zf["abwesenheiten"]!.GetValue<int>() >= 1);
        zr = await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-14&klasse=7a");
        Assert.Equal(1, Anzahl(zr, kinder[2].Id));
        // verschiedene Gründe bzw. Status bleiben getrennt: Kind 1 und Kind 2 unverändert
        Assert.Equal(1, Anzahl(zr, kinder[0].Id));
        Assert.Equal("u-1Mahn", zr.AsArray().First(z => z!["schuelerId"]!.GetValue<int>() == kinder[0].Id)!["statusName"]!.GetValue<string>());

        // Fehlzeiten pro Klasse (Gesamtübersicht, für das Zeugnis): Fehltage, davon unentschuldigt; „Schul“ zählt nicht
        // (Vorgabe „Nur zählende Abwesenheiten“: Kind 2 fehlt ganz)
        var nurZaehlend = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/fehlzeiten_klasse?klasse=7a&von=2026-10-12&bis=2026-10-14");
        Assert.DoesNotContain(Spalte(nurZaehlend, 0), x => x.StartsWith(kinder[1].Name));
        var g = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/fehlzeiten_klasse?klasse=7a&von=2026-10-12&bis=2026-10-14&trennen=0&zaehlend=0");
        var spalten = g["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Equal("Fehltage unent.", spalten[11]);
        var zeilen = g["zeilen"]!.AsArray();
        var arten = g["arten"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        JsonArray Zeile(int sid) => zeilen.Select((z, i) => (z!.AsArray(), i)).First(t => arten[t.i] == "" && t.Item1[0]!.GetValue<string>().StartsWith(kinder.First(k => k.Id == sid).Name)).Item1;
        var z1 = Zeile(kinder[0].Id);
        Assert.Equal(1, z1[9]!.GetValue<int>());   // Fehltage alle
        Assert.Equal(1, z1[11]!.GetValue<int>());  // Fehltage unent. (u-1Mahn)
        var z2 = Zeile(kinder[1].Id);
        Assert.Equal(1, z2[9]!.GetValue<int>());   // Fehltag …
        Assert.Equal("", z2[10]!.ToString());      // … der nicht zählt
        var z3 = Zeile(kinder[2].Id);
        Assert.Equal(2, z3[9]!.GetValue<int>());
        // getrennt: Stunden ganzer Fehltage nicht zusätzlich als Fehlstunden
        var gt = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/fehlzeiten_klasse?klasse=7a&von=2026-10-12&bis=2026-10-14&trennen=1&zaehlend=0");
        Assert.All(gt["zeilen"]!.AsArray().Where((z, i) => gt["arten"]![i]!.GetValue<string>() == ""), z => Assert.Equal("", z![1]!.ToString()));
        // „pro Stunde“ mit [nicht entschuldigt]: nur Kind 1 und Kind 3 (offen zählt wie nicht entschuldigt)
        var u = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/fehlzeiten_klasse?klasse=7a&von=2026-10-12&bis=2026-10-14&ausgabe=pro_stunde&status=nicht_entschuldigt");
        Assert.Contains("nicht entschuldigt", u["titel"]!.GetValue<string>());
        Assert.Contains("u-1Mahn", Spalte(u, 12));
        Assert.Equal(new[] { "Datum", "Stunde", "Schulst", "Std.", "Uhrzeit", "Art", "Fach", "Lehrkraft", "Abwesenheitsgrund", "Text", "ENr", "Erledigt", "Status" }, u["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        // je Schüler*in ein Kopfblock wie im Original (Schüler*innen, Text, Klasse)
        Assert.Contains(Spalte(u, 0), x => x.StartsWith("Schüler*innen\t") && x.Contains("\nKlasse\t7a"));
        Assert.DoesNotContain(Spalte(u, 0), x => x.Contains(kinder[1].Name));
        // Fehlzeiten pro Schüler*in „pro Tag“: ganzer Schultag = 100 %
        var t = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/fehlzeiten_schueler?klasse=7a&schueler={kinder[0].Id}&von=2026-10-12&bis=2026-10-12&ausgabe=pro_tag");
        Assert.Contains("100 %", Spalte(t, 5));
        Assert.Matches(@"^[A-Z][a-z]\.\d\d\.\d\d\.\d\d$", Spalte(t, 0).First(x => x.Length > 0 && char.IsUpper(x[0]) && x[2] == '.'));
        Assert.Contains("Summe (zählend)", Spalte(t, 0));

        // Schuljahresende: Klassenbuch Deckblatt und Lehrnachweis mit Abwesenheiten
        var d = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/klassenbuch_deckblatt?klasse=7a&von=2026-08-01&bis=2026-10-14");
        Assert.Contains("Klassenleitung", Spalte(d, 0));
        var w = await Testserver.HolenAsync(leitung, "/api/app/berichte/erstellen/wochenuebersicht?klasse=7a&von=2026-10-12&bis=2026-10-14&mitabw=1");
        Assert.Equal("Abwesende Schüler*innen", w["spalten"]!.AsArray()[^2]!.GetValue<string>());
        var pdf = await leitung.GetAsync("/api/app/berichte/erstellen/klassenbuch_deckblatt?klasse=7a&von=2026-08-01&bis=2026-10-14&format=pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);

        // Klassenbucheinträge: Kategorie „Trainingsraum“ je Schüler*in (Maßnahmen)
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-13", klasse = "7a", schuelerId = kinder[0].Id, art = "Trainingsraum", text = "Unterschrift der Eltern" }));
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/vermerke", new { datum = "2026-10-13", klasse = "7a", schuelerId = kinder[0].Id, art = "Lob", text = "Toll" }));
        var tr = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/klassenbucheintraege_schueler?klasse=7a&schueler={kinder[0].Id}&von=2026-08-01&bis=2026-10-14&kategorie=Trainingsraum");
        Assert.Equal(new[] { "Name", "Klasse", "Tag", "Datum", "Zeit", "Fach", "Benutzer", "Eintragskategorie", "Kategoriegruppe", "Text" }, tr["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal(new[] { "Trainingsraum" }, Spalte(tr, 7).ToArray());
        var mass = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/klassenbucheintraege_schueler?klasse=7a&schueler={kinder[0].Id}&von=2026-08-01&bis=2026-10-14&kategorie=gruppe:Maßnahme");
        Assert.Equal("Maßnahme", Spalte(mass, 8).Single());
    }

    [Fact]
    public async Task Berichte_zum_Unterricht_mit_75_Prozent_Regel_und_Fehlstundenmatrix()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var kind = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a");
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kind.Id, von = "2026-10-12", bis = "2026-10-12", grund = "K" }));
        var unterrichte = (await Testserver.HolenAsync(leitung, "/api/app/unterricht?klasse=7a&datum=2026-10-12")).AsArray();
        // ein Unterricht der 7a, der am Montag stattfindet
        JsonNode? matrix = null;
        int uid = 0;
        foreach (var x in unterrichte)
        {
            uid = x!["id"]!.GetValue<int>();
            matrix = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/fehlstundenmatrix?von=2026-10-12&bis=2026-10-14&unterricht={uid}");
            if (matrix["zeilen"]!.AsArray().Any(z => z!.AsArray().Any(c => c?.ToString() == "A"))) break;
        }
        Assert.Contains(matrix!["zeilen"]!.AsArray(), z => z!.AsArray().Any(c => c?.ToString() == "A"));
        // ohne Einträge: leere Matrix
        var leer = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/fehlstundenmatrix?von=2026-10-12&bis=2026-10-14&unterricht={uid}&ohne=1");
        Assert.DoesNotContain(leer["zeilen"]!.AsArray(), z => z!.AsArray().Any(c => c?.ToString() == "A"));
        var f = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/fehlzeiten_unterricht_schueler?von=2026-10-12&bis=2026-10-14&unterricht={uid}");
        Assert.Contains("Unterrichtsstunden", f["hinweis"]!.GetValue<string>());
        // wie im Original: je Fehlzeit eine Zeile, am Ende Summe | Summe (zählend) und davon unent. mit Anteil an den gehaltenen Stunden
        Assert.Equal(new[] { "Schüler*innen", "Datum", "Fehlstd.", "Zeit", "Abwesenheitsgrund", "ENr", "Erledigt", "Verspätung / vorzeitig" }, f["spalten"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        Assert.Contains(kind.Name, Spalte(f, 0).First());
        Assert.Equal("Summe", Spalte(f, 0)[^2]);
        Assert.Equal("Summe (zählend)", Spalte(f, 4)[^2]);
        Assert.Matches(@"^\d+\.\d\d %$", Spalte(f, 3)[^2]);
        Assert.Equal("davon unent.", Spalte(f, 0)[^1]);
        var ab = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/arbeitsbericht_unterricht?von=2026-10-12&bis=2026-10-14&unterricht={uid}");
        Assert.Contains(Spalte(ab, 5), x => x.Contains(kind.Name));
        Assert.NotEmpty((await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/fehlzeiten_unterricht_stunde?von=2026-10-12&bis=2026-10-14&unterricht={uid}"))["zeilen"]!.AsArray());
        // ohne Unterricht: Fehlermeldung
        Assert.Equal(HttpStatusCode.BadRequest, (await leitung.GetAsync("/api/app/berichte/erstellen/fehlstundenmatrix?von=2026-10-12&bis=2026-10-14")).StatusCode);
    }

    [Fact]
    public async Task Stift_bearbeitet_und_entschuldigt_die_Abwesenheit()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var kind = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a");
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kind.Id, von = "2026-10-12", bis = "2026-10-12", grund = "K" }));
        var z = (await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-12&klasse=7a")).AsArray().First(x => x!["schuelerId"]!.GetValue<int>() == kind.Id)!;
        var ids = z["ids"]!.AsArray().Select(i => i!.GetValue<int>()).ToList();
        // Bearbeiten-Dialog (Stift): Zeitraum, Grund, Text und Entschuldigungsstatus in einem Schritt
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new { schuelerIds = new[] { kind.Id }, von = "2026-10-12T00:00", bis = "2026-10-12T23:59",
            ersetzen = ids, grund = "A", status = "Nicht akzeptiert", text = "Arzttermin ohne Bescheinigung", entschuldigtAm = "2026-10-14" }));
        z = (await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-12&klasse=7a")).AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == kind.Id)!;
        Assert.Equal(("A", "unentschuldigt", "Nicht akzeptiert", "2026-10-14"), (z["grund"]!.GetValue<string>(), z["status"]!.GetValue<string>(), z["statusName"]!.GetValue<string>(), z["entschuldigtAm"]!.GetValue<string>()));
        // „Entschuldigen“ im selben Dialog
        ids = z["ids"]!.AsArray().Select(i => i!.GetValue<int>()).ToList();
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/klassenbuch/abwesenheit", new { schuelerIds = new[] { kind.Id }, von = "2026-10-12T00:00", bis = "2026-10-12T23:59",
            ersetzen = ids, grund = "A", status = "Entschuldigt", text = "Attest nachgereicht" }));
        z = (await Testserver.HolenAsync(leitung, "/api/app/abwesenheiten/zeitraeume?von=2026-10-12&bis=2026-10-12&klasse=7a")).AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == kind.Id)!;
        Assert.Equal(("entschuldigt", ""), (z["status"]!.GetValue<string>(), z["statusName"]!.GetValue<string>()));

        // Oberfläche: Stift in jeder Zeile sichtbar, Dialog mit Abschnitt „Entschuldigung“ und Knopf „Entschuldigen“
        var js = File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js", "ansichten", "abwesenheiten.js"));
        Assert.Contains("class=\"nl-icon sichtbar\" data-bearbeiten=", js);
        Assert.Contains("<div class=\"abw-details-titel\">Entschuldigung</div>", js);
        Assert.Contains("{ text: 'Entschuldigen', aktion:", js);
        Assert.Contains("data-entsch-status", js);
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
