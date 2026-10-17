using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Model;
using Xunit.Abstractions;

namespace UntPlanStudio.Tests;

/// <summary>Vertretungsmodus über die API: Absenz → Vertretungen, Vorschlag übernehmen, Entfall/Rückgängig, Veranstaltung mit allen Lehrkräften, Tageswechsel.</summary>
public sealed class VertretungTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;
    private readonly ITestOutputHelper _aus;

    public VertretungTests(StudioFabrik f, ITestOutputHelper aus)
    {
        _f = f;
        _aus = aus;
    }

    private static string S(JsonNode? n) => n?.GetValue<string>() ?? "";

    /// <summary>Beispielschule mit verplantem Stundenplan, Startdatum der Vertretungsplanung und eine Lehrkraft mit mehreren Stunden an diesem Tag.</summary>
    private async Task<(HttpClient C, string Basis, string Datum, int LehrerId, string Lehrer)> VorbereitenAsync()
    {
        var c = await _f.AngemeldetAsync();
        var id = (await c.PostJsonAsync("/api/dokumente/beispiel"))["id"]!.GetValue<string>();
        var basis = $"/api/dokumente/{id}/vertretung";
        var datum = S((await c.GetJsonAsync($"{basis}/start"))["datum"]);
        var lehrer = (await c.GetJsonAsync($"/api/dokumente/{id}/elemente?typen=lehrer"))["lehrer"]!.AsArray();
        foreach (var l in lehrer)
        {
            var lid = l!["id"]!.GetValue<int>();
            var plan = await c.GetJsonAsync($"{basis}/plan/lehrer/{lid}?datum={datum}");
            var stunden = plan["zellen"]!.AsArray().Count(z => S(z!["datum"]) == datum);
            if (stunden >= 3) return (c, basis, datum, lid, S(l["k"]));
        }
        throw new InvalidOperationException("Keine Lehrkraft mit Unterricht am Starttag gefunden.");
    }

    private static List<JsonNode> Zeilen(JsonNode liste) => liste["zeilen"]!.AsArray().Select(z => z!).ToList();

    private static int Vid(JsonNode zeile) => zeile["m"]!["v"]!.GetValue<int>();

    [Fact]
    public async Task AbsenzErzeugtVertretungenUndVorschlagWirdUebernommen()
    {
        var (c, basis, datum, lid, lehrer) = await VorbereitenAsync();
        _aus.WriteLine($"Datum {datum}, Lehrkraft {lehrer}");

        // Absenz anlegen (Standard 1.–letzte Stunde)
        var erg = await c.PostJsonAsync($"{basis}/absenzen/speichern", new { art = "lehrer", neu = new[] { new { id = 0, element = lehrer, von = datum } } });
        Assert.Single(erg["gespeichert"]!.AsArray());
        var absenzen = await c.GetJsonAsync($"{basis}/absenzen?art=lehrer&von={datum}");
        Assert.Equal(1, absenzen["anzahl"]!["lehrer"]!.GetValue<int>());
        var zeile = absenzen["zeilen"]![0]!;
        Assert.Equal(lehrer, S(zeile["element"]));
        Assert.True(zeile["offen"]!.GetValue<int>() > 0);

        // gleiche Absenz erneut: wird nicht doppelt angelegt
        var doppelt = await c.PostJsonAsync($"{basis}/absenzen/speichern", new { art = "lehrer", neu = new[] { new { id = 0, element = lehrer, von = datum } } });
        Assert.True(doppelt["doppelt"]!.GetValue<bool>());

        // Vertretungen sind entstanden (offen, „???“ beim Vertreter)
        var liste = await c.GetJsonAsync($"{basis}/liste?von={datum}&offen=true");
        var offen = Zeilen(liste).Where(z => S(z["Lehrer"]).TrimStart('*') == lehrer).ToList();
        Assert.NotEmpty(offen);
        Assert.Contains(offen, z => S(z["Vertreter"]) == "???");
        Assert.True(liste["spalten"]!.AsArray().Count >= 30);

        // Vertretungsvorschlag: Kandidaten mit Bewertung, Übernehmen
        var vid = Vid(offen.First(z => S(z["Vertreter"]) == "???"));
        var vorschlag = await c.GetJsonAsync($"{basis}/vorschlag/{vid}");
        Assert.StartsWith(datum[8..].TrimStart('0'), S(vorschlag["kopf"]));
        var kandidaten = vorschlag["kandidaten"]!.AsArray();
        Assert.NotEmpty(kandidaten);
        Assert.Matches(@"^-?\d+\.\d{3}$", S(kandidaten[0]!["vertr"]));
        Assert.Equal(3, kandidaten[0]!["s"]!.AsArray().Count);
        var vertreter = kandidaten.FirstOrDefault(k => !k!["rot"]!.GetValue<bool>()) ?? kandidaten[0]!;
        await c.PostJsonAsync($"{basis}/vorschlag/{vid}/uebernehmen", new { lehrer = vertreter!["lehrer"]!.GetValue<int>(), ids = new[] { vid } });
        var nachher = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(z => Vid(z) == vid);
        Assert.Equal(S(vertreter["name"]), S(nachher["Vertreter"]));
        Assert.False(nachher["m"]!["offen"]!.GetValue<bool>());

        // Rückgängig über den Core (eine Stufe)
        var stand = await c.GetJsonAsync(basis.Replace("/vertretung", ""));
        Assert.StartsWith("Vertreter", S(stand["rueckgaengigText"]));

        // Absenz löschen: Vertretungen verschwinden
        await c.PostJsonAsync($"{basis}/absenzen/loeschen", new { ids = new[] { zeile["nr"]!.GetValue<int>() } });
        Assert.DoesNotContain(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")), z => S(z["Lehrer"]).TrimStart('*') == lehrer && z["m"]!["abgeleitet"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EntfallUndRueckgaengigSowieRueckfragen()
    {
        var (c, basis, datum, lid, lehrer) = await VorbereitenAsync();
        await c.PostJsonAsync($"{basis}/absenzen/speichern", new { art = "lehrer", neu = new[] { new { id = 0, element = lehrer, von = datum } } });
        var offen = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}&offen=true")).Where(z => S(z["Vertreter"]) == "???").ToList();
        Assert.True(offen.Count >= 2);
        var a = Vid(offen[0]);
        var b = Vid(offen[1]);

        // Entfall über die Spalte Vertreter („-“) und über den Knopf
        await c.PostJsonAsync($"{basis}/aendern", new { ids = new[] { a }, spalte = "Vertreter", wert = "-" });
        await c.PostJsonAsync($"{basis}/entfall", new { ids = new[] { b } });
        var zeilen = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}"));
        foreach (var x in new[] { a, b })
        {
            var z = zeilen.Single(y => Vid(y) == x);
            Assert.Equal("---", S(z["Vertreter"]));
            Assert.Equal("X", S(z["Entf"]));
        }
        // Filter „offene Vertretungen“ zeigt sie nicht mehr – außer sie werden „behalten“ (bis zum Zeilenwechsel)
        Assert.DoesNotContain(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}&offen=true")), z => Vid(z) == a);
        Assert.Contains(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}&offen=true&behalten={a}")), z => Vid(z) == a);

        // Entfall rückgängig: erneuter Entfall auf lauter Entfällen macht wieder offene Vertretungen daraus
        await c.PostJsonAsync($"{basis}/entfall", new { ids = new[] { a, b } });
        zeilen = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}"));
        Assert.All(new[] { a, b }, x => Assert.Equal("???", S(zeilen.Single(y => Vid(y) == x)["Vertreter"])));

        // „+“ = Vertretung ohne Lehrkraft, Zurücksetzen macht sie wieder offen
        await c.PostJsonAsync($"{basis}/aendern", new { ids = new[] { a }, spalte = "Vertreter", wert = "+" });
        Assert.Equal("+++", S(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(y => Vid(y) == a)["Vertreter"]));
        await c.PostJsonAsync($"{basis}/zuruecksetzen", new { ids = new[] { a } });
        Assert.Equal("???", S(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(y => Vid(y) == a)["Vertreter"]));

        // Rückfrage: abwesende Lehrkraft als Vertreter („Trotzdem verplanen?“) – ohne Antwort keine Änderung
        var frage = await c.PostJsonAsync($"{basis}/aendern", new { ids = new[] { a }, spalte = "Vertreter", wert = lehrer });
        Assert.Equal("absent", S(frage["frage"]!["schluessel"]));
        Assert.Equal("???", S(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(y => Vid(y) == a)["Vertreter"]));
        await c.PostJsonAsync($"{basis}/aendern", new { ids = new[] { a }, spalte = "Vertreter", wert = lehrer, antworten = new Dictionary<string, int> { ["absent"] = 0, ["belegt"] = 0 } });
        Assert.Equal(lehrer, S(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(y => Vid(y) == a)["Vertreter"]));

        // Text, Raum (Raumvertretung), Fixieren schützt vor Änderungen
        await c.PostJsonAsync($"{basis}/aendern", new { ids = new[] { b }, spalte = "Text", wert = "Aufgaben im Ordner" });
        await c.PostJsonAsync($"{basis}/kennzeichen", new { ids = new[] { b }, art = "fix", wert = true });
        var fix = await c.PostJsonAsync($"{basis}/entfall", new { ids = new[] { b } });
        Assert.Equal(0, fix["anzahl"]!.GetValue<int>());
        var zb = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")).Single(y => Vid(y) == b);
        Assert.Equal("Aufgaben im Ordner", S(zb["Text"]));
        Assert.Equal("X", S(zb["Fix"]));

        // Tagesaktueller Plan: Entfall und Entfall rückgängig im Kontextmenü
        var plan = await c.GetJsonAsync($"{basis}/plan/lehrer/{lid}?datum={datum}");
        var stunde = plan["zellen"]!.AsArray().Select(z => z!).First(z => S(z["datum"]) == datum && z["eintraege"]!.AsArray().Any(e => S(e!["status"]) == "Offen"));
        var e = stunde["eintraege"]!.AsArray().First(x => S(x!["status"]) == "Offen")!;
        var pos = new { datum, stunde = stunde["stunde"]!.GetValue<int>(), unterricht = e["u"]!.GetValue<int>(), zeile = e["zeile"]!.GetValue<int>() };
        await c.PostJsonAsync($"{basis}/plan/entfall", pos);
        plan = await c.GetJsonAsync($"{basis}/plan/lehrer/{lid}?datum={datum}");
        var nachEntfall = plan["zellen"]!.AsArray().Select(z => z!).First(z => S(z["datum"]) == datum && z["stunde"]!.GetValue<int>() == pos.stunde)["eintraege"]!.AsArray()
            .First(x => x!["u"]!.GetValue<int>() == pos.unterricht)!;
        Assert.Equal("Entfall", S(nachEntfall["status"]));
        Assert.NotEmpty(nachEntfall["aufhebbar"]!.AsArray());
        await c.PostJsonAsync($"{basis}/plan/entfallrueckgaengig", pos);
        plan = await c.GetJsonAsync($"{basis}/plan/lehrer/{lid}?datum={datum}");
        Assert.Equal("Offen", S(plan["zellen"]!.AsArray().Select(z => z!).First(z => S(z["datum"]) == datum && z["stunde"]!.GetValue<int>() == pos.stunde)["eintraege"]!.AsArray()
            .First(x => x!["u"]!.GetValue<int>() == pos.unterricht)!["status"]));
    }

    [Fact]
    public async Task VeranstaltungMitAllenLehrkraeftenWirdZusammengefasst()
    {
        var (c, basis, datum, _, lehrer) = await VorbereitenAsync();
        var vbasis = basis.Replace("/vertretung", "/veranstaltungen");
        var neu = await c.PostJsonAsync(vbasis, new { feld = "L", wert = "Alle", datum });
        var vaId = neu["id"]!.GetValue<int>();
        await c.PostJsonAsync($"{vbasis}/{vaId}", new { feld = "K", wert = "Alle Klassen" });
        await c.PostJsonAsync($"{vbasis}/{vaId}", new { feld = "T", wert = "Pädagogischer Tag" });
        var liste = await c.GetJsonAsync($"{vbasis}?von={datum}");
        var va = liste["zeilen"]!.AsArray().Single(z => z!["id"]!.GetValue<int>() == vaId)!;
        Assert.Equal("Alle Lehrkräfte", S(va["l"]));
        Assert.Equal("Alle Klassen", S(va["k"]));

        // Vertretungsliste: Entfälle/Freisetzungen der Veranstaltung je Tag in Sammelzeilen
        var gesammelt = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}&sammeln=true"));
        var sammel = gesammelt.Where(z => z["m"]!["sammel"]!.GetValue<bool>()).ToList();
        Assert.NotEmpty(sammel);
        Assert.Contains("-", S(sammel[0]["Std"]));
        Assert.StartsWith("Alle Klassen", S(sammel[0]["AKlassen"]));
        var einzeln = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}&sammeln=false"));
        Assert.True(einzeln.Count > gesammelt.Count, $"{einzeln.Count} einzeln, {gesammelt.Count} gesammelt");
        _aus.WriteLine($"Veranstaltung alle: {einzeln.Count} Zeilen → {gesammelt.Count} ({sammel.Count} zusammengefasst, Std. {S(sammel[0]["Std"])})");

        // Schnellauswahl: Gruppen, Lehrkraft fällt aus (Absenz) → Nachfrage „aus der Veranstaltung entfernen?“
        var gruppen = await c.GetJsonAsync($"{vbasis}/gruppen?feld=K&va={vaId}");
        Assert.Contains(gruppen.AsArray(), x => S(x!["name"]) == "Alle Klassen");
        var abs = await c.PostJsonAsync($"{basis}/absenzen/speichern", new { art = "lehrer", neu = new[] { new { id = 0, element = lehrer, von = datum } } });
        var nf = abs["nachfragen"]!.AsArray().Single(x => S(x!["art"]) == "veranstaltung")!;
        await c.PostJsonAsync($"{basis}/absenzen/nachfrage", new { art = "veranstaltung", va = nf["va"]!.GetValue<int>(), lehrer = nf["lehrer"]!.GetValue<int>(), tage = nf["tage"], absenz = nf["absenz"]!.GetValue<int>() });
        va = (await c.GetJsonAsync($"{vbasis}?von={datum}"))["zeilen"]!.AsArray().Single(z => z!["id"]!.GetValue<int>() == vaId)!;
        Assert.Contains("???", S(va["l"]));
        Assert.NotNull(va["offen"]);

        // Löschen der Veranstaltung entfernt die Sammelzeilen
        await c.PostJsonAsync($"{vbasis}/loeschen", new { ids = new[] { vaId } });
        Assert.DoesNotContain(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}")), z => z["m"]!["sammel"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TageswechselErzeugtVertretungenNurBeiBedarf()
    {
        var c = await _f.AngemeldetAsync();
        // planende Beispielschule herunterladen und eine Absenz ohne berechnete Vertretungen eintragen (wie eine ältere Desktop-Datei)
        var id0 = (await c.PostJsonAsync("/api/dokumente/beispiel"))["id"]!.GetValue<string>();
        var basis0 = $"/api/dokumente/{id0}/vertretung";
        var datum = DateOnly.Parse(S((await c.GetJsonAsync($"{basis0}/start"))["datum"]));
        var up = await c.GetByteArrayAsync($"/api/dokumente/{id0}/herunterladen");
        var d = JsonSerializer.Deserialize<Schuldatei>(up, Dokument.JsonOptionen)!;
        var tag = Kalender.TagIndex(d, datum);
        var lehrerId = d.AktivePeriode.Plan.Where(p => p.Tag == tag).Select(p => d.Unterrichte.First(u => u.Id == p.UnterrichtId).Zeilen[0].LehrerId).OfType<int>().First();
        var naechster = Kalender.NaechsterUnterrichtstag(d, datum);
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrerId, Von = naechster, Bis = naechster });
        var id = await c.BeispielHochladenAsync(d);
        var basis = $"/api/dokumente/{id}/vertretung";

        // Blättern: nächster Unterrichtstag, eine Woche weiter
        Assert.Equal(naechster.ToString("yyyy-MM-dd"), S((await c.GetJsonAsync($"{basis}/blaettern?datum={datum:yyyy-MM-dd}&richtung=1"))["datum"]));
        var woche = DateOnly.Parse(S((await c.GetJsonAsync($"{basis}/blaettern?datum={datum:yyyy-MM-dd}&richtung=1&woche=true"))["datum"]));
        Assert.True(woche >= datum.AddDays(7));
        Assert.Equal(datum.ToString("yyyy-MM-dd"), S((await c.GetJsonAsync($"{basis}/blaettern?datum={naechster:yyyy-MM-dd}&richtung=-1"))["datum"]));

        // vor dem Tageswechsel keine Vertretungen; Tageswechsel berechnet sie (eine Rückgängig-Stufe), ein zweiter Wechsel ändert nichts
        Assert.Empty(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={naechster:yyyy-MM-dd}")));
        var erst = await c.PostJsonAsync($"{basis}/aktualisieren", new { von = naechster });
        Assert.True(erst["geaendert"]!.GetValue<bool>());
        Assert.NotEmpty(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={naechster:yyyy-MM-dd}&offen=true")));
        var zweit = await c.PostJsonAsync($"{basis}/aktualisieren", new { von = naechster });
        Assert.False(zweit["geaendert"]!.GetValue<bool>());

        // Kalender: Woche mit offenen Vertretungen, Kalendertag „unterrichtsfrei“ lässt sie verschwinden
        var kal = await c.GetJsonAsync($"{basis}/kalender?datum={naechster:yyyy-MM-dd}");
        Assert.Contains(kal["woche"]!.AsArray(), w => S(w!["datum"]) == naechster.ToString("yyyy-MM-dd") && w["offen"]!.GetValue<int>() > 0);
        await c.PostJsonAsync($"{basis}/kalendertag", new { datum = naechster, art = "frei" });
        Assert.Empty(Zeilen(await c.GetJsonAsync($"{basis}/liste?von={naechster:yyyy-MM-dd}&offen=true")));
        await c.PostJsonAsync($"/api/dokumente/{id}/rueckgaengig");

        // Tagestexte
        var texte = await (await c.PutAsJsonAsync($"{basis}/tagestexte", new { datum = naechster, texte = new[] { new { text = "Lehrerkonferenz 14 Uhr", portal = true, art = "Nur Lehrer" } } })).JsonAsync();
        Assert.Equal("Lehrerkonferenz 14 Uhr", S(texte["tagestexte"]![0]!["text"]));
        Assert.Equal("Nur Lehrer", S(texte["tagestexte"]![0]!["art"]));
    }

    [Fact]
    public async Task SondereinsatzVormerkungAutomatikUndEinstellungen()
    {
        var (c, basis, datum, lid, lehrer) = await VorbereitenAsync();
        var klasse = S((await c.GetJsonAsync(basis.Replace("/vertretung", "/elemente?typen=klasse")))["klasse"]![0]!["k"]);
        var se = await c.PostJsonAsync($"{basis}/sondereinsaetze", new { zeilen = new[] { new { datum, sv = 1, sb = 2, lehrer, klassen = klasse, text = "Projekt" } } });
        Assert.True(se["anzahl"]!.GetValue<int>() >= 2);
        var zeilen = Zeilen(await c.GetJsonAsync($"{basis}/liste?von={datum}"));
        Assert.Contains(zeilen, z => z["m"]!["art"]!.GetValue<string>() == "Sondereinsatz" && S(z["Vertreter"]) == lehrer);

        // Absenz einer weiteren Lehrkraft, Vormerkung beiseitelegen und wieder verplanen
        await c.PostJsonAsync($"{basis}/absenzen/speichern", new { art = "klasse", neu = new[] { new { id = 0, element = klasse, von = datum } } });
        var kk = await c.GetJsonAsync($"{basis}/klassenkonferenz?klassen={klasse}");
        Assert.NotEmpty(S(kk["lehrer"]));

        var auto = await c.GetJsonAsync($"{basis}/automatik?von={datum}");
        Assert.Equal(6, auto["methoden"]!.AsArray().Count);
        var erg = await c.PostJsonAsync($"{basis}/automatik", new { von = datum, methode = "BesterVorschlag" });
        Assert.True(erg["bearbeitet"]!.GetValue<int>() >= 0);

        var bereit = await c.GetJsonAsync($"{basis}/bereitschaften?datum={datum}");
        Assert.NotNull(bereit["zeilen"]);
        var bp = await c.GetJsonAsync($"{basis}/bereitschaftsplanung");
        Assert.NotEmpty(bp["zellen"]!.AsArray());

        var vm = await c.PostJsonAsync($"{basis}/vormerkungen", new { fach = "", lehrer, klassen = klasse, stunden = 2, text = "Nachholen" });
        var vormerkungen = (await c.GetJsonAsync($"{basis}/vormerkungen")).AsArray();
        var vmId = vormerkungen.Single(v => S(v!["text"]) == "Nachholen")!["id"]!.GetValue<int>();
        await c.PostJsonAsync($"{basis}/vormerkungen/{vmId}/verplanen", new { datum, stunde = 8 });
        Assert.Equal(1, (await c.GetJsonAsync($"{basis}/vormerkungen?alle=true")).AsArray().Single(v => v!["id"]!.GetValue<int>() == vmId)!["offen"]!.GetValue<int>());

        var ein = await c.GetJsonAsync($"{basis}/einstellungen");
        var e = ein["einstellungen"]!.AsObject();
        e["vorschlag"]!["hatKlasse"] = 42;
        await (await c.PutAsJsonAsync($"{basis}/einstellungen", new { einstellungen = e })).JsonAsync();
        Assert.Equal(42, (await c.GetJsonAsync($"{basis}/einstellungen"))["einstellungen"]!["vorschlag"]!["hatKlasse"]!.GetValue<int>());

        // ohne Schutzkopfzeile keine Änderung
        Assert.Contains((await _f.Client(false).PostAsJsonAsync($"{basis}/entfall", new { ids = new[] { 1 } })).StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });
    }
}
