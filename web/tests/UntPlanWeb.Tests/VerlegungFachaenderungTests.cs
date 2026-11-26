using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fall aus echten Daten: Die Doppelstunde DEU von OU (1.–2. Stunde) wird in die 4.–5. Stunde verlegt; am frei gewordenen Ursprung wird
/// in UntPlan das Fach geändert (GID, ohne Lehrkraft bzw. noch offen). Die neue Stunde am Ursprung ist eine Zusatzstunde: Symbole „+“ und
/// Änderung (grünes Quadrat) – nicht das rote Verlegungssymbol, denn verlegt wurde die ursprüngliche Stunde. Daneben bleibt die
/// ursprüngliche Stunde als Entfall (rotes Dreieck), die verlegte Stunde am Ziel behält ihren grünen Verlegungspfeil. Ende zu Ende
/// (Core → echter Export → UntPlanWeb → /api/app/plan).
/// </summary>
public class VerlegungFachaenderungTests
{
    private readonly ITestOutputHelper _aus;
    public VerlegungFachaenderungTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private const string Iso = "2026-11-06";

    private static List<string> Marker(JsonNode k) => k["marker"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();

    /// <summary>
    /// Wie im Klassenplan auch im Raum- und Lehrerplan (iOS liest dieselben Marker): die neue Stunde am Ursprung ist eine Zusatzstunde ohne
    /// Verlegung, ein Block über die 1.–2. Stunde. Mit <paramref name="neuerRaum"/> findet sie in einem anderen Raum statt („Online“) – im
    /// Raumplan dieses Raums stand die ursprüngliche Stunde nie (keine Entfallkarte), die neue Stunde ist trotzdem Zusatzstunde.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Fachaenderung_am_Ursprung_einer_Verlegung_ist_Zusatzstunde_mit_Plus_und_Aenderung(bool offen, bool neuerRaum)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.NeueDatei("Testschule", "GY");
        var deu = new Fach { Id = d.NeueId(), Kurzname = "DEU" };
        var gid = new Fach { Id = d.NeueId(), Kurzname = "GID" };
        d.Faecher.AddRange(new[] { deu, gid });
        var klasse = new Klasse { Id = d.NeueId(), Kurzname = "K1" };
        d.Klassen.Add(klasse);
        var ou = new Lehrer { Id = d.NeueId(), Kurzname = "OU" };
        d.Lehrer.Add(ou);
        var raum = new Raum { Id = d.NeueId(), Kurzname = "D004" };
        var online = new Raum { Id = d.NeueId(), Kurzname = "Online" };
        d.Raeume.AddRange(new[] { raum, online });
        var tag = (int)Freitag.DayOfWeek - 1;
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 2, Zeilen = { new Kopplungszeile { LehrerId = ou.Id, FachId = deu.Id, KlassenIds = { klasse.Id }, FachraumId = raum.Id } } };
        d.Unterrichte.Add(u);
        foreach (var st in new[] { 1, 2 }) d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = st, Raeume = { raum.Id } });
        Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Freitag, 1, u.Id, null, 2, Freitag, 4), VerlegeModus.Verlegen);
        // am Ursprung (offene Vertretung „???“): Fach geändert (Spalte „Fach“ im Vertretungsfenster), ggf. ohne Lehrkraft („+++“) und in
        // einem anderen Raum
        foreach (var q in d.Vertretungen.Where(Vertretungen.IstOffenerUrsprung).ToList())
        {
            if (!offen) Vertretungen.OhneLehrer(q);
            q.VertretungsfachId = gid.Id;
            if (neuerRaum) q.VertretungsraumId = online.Id;
            q.Bearbeitet = true;
        }
        foreach (var v in d.Vertretungen) _aus.WriteLine($"Zeile: {v.Art} U{v.UnterrichtId} {v.Stunde} Fach {v.FachId}/{v.VertretungsfachId} Raum {v.RaumId}/{v.VertretungsraumId} nach {v.NachDatum}/{v.NachStunde}");
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);

        var c = await s.AlsAsync("admin");
        async Task<List<JsonNode>> Karten(string typ, string element)
        {
            var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={element}&datum={Iso}");
            var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == Iso).ToList();
            _aus.WriteLine($"{typ} {element}:");
            foreach (var k in karten) _aus.WriteLine("  " + k.ToJsonString());
            return karten;
        }
        // neue Stunde am Ursprung: GID, Zusatzstunde, keine Verlegung, ein Block 1.–2. Stunde, „+“ und Änderung (offene Vertretung –
        // Lehrkraft noch offen: Änderung); ohne Lehrkraft steht die bisherige (OU) nicht durchgestrichen darauf – sie gehört zur Entfallkarte
        void NeueStunde(List<JsonNode> karten, string plan)
        {
            var neu = karten.Where(k => k["fach"]!.GetValue<string>() == "GID").ToList();
            Assert.True(neu.Count == 1, $"{plan}: genau eine Karte GID erwartet, gefunden {neu.Count}");
            var k = neu[0];
            Assert.Equal(1, k["von"]!.GetValue<int>());
            Assert.Equal(2, k["bis"]!.GetValue<int>());
            Assert.Equal("DEU", k["fachAlt"]!.GetValue<string>());
            Assert.True(k["zusaetzlich"]!.GetValue<bool>(), plan);
            Assert.Null(k["verlegung"]?.GetValue<string>());
            Assert.Null(k["verlegungVon"]);
            Assert.Null(k["verlegungNach"]);
            Assert.Equal(offen ? new[] { "aenderung" } : new[] { "plus", "aenderung" }, Marker(k));
            Assert.DoesNotContain(k["lehrer"]!.AsArray(), w => w!["alt"]?.GetValue<string>() == "OU");
            if (neuerRaum) Assert.Contains(k["raeume"]!.AsArray(), w => w!["k"]!.GetValue<string>() == "Online");
        }
        // ursprüngliche Stunde: Entfall (rotes Dreieck)
        void Entfall(List<JsonNode> karten)
        {
            var entfall = karten.Where(k => k["status"]!.GetValue<string>() == "entfall").ToList();
            Assert.NotEmpty(entfall);
            Assert.All(entfall, k => { Assert.Equal("DEU", k["fach"]!.GetValue<string>()); Assert.Equal(new[] { "entfall" }, Marker(k)); });
        }
        // verlegte Stunde am Ziel: grüner Verlegungspfeil
        void Ziel(List<JsonNode> karten)
        {
            var ziel = karten.Where(k => k["von"]!.GetValue<int>() >= 4).ToList();
            Assert.NotEmpty(ziel);
            Assert.All(ziel, k => { Assert.Equal("nach", k["verlegung"]!.GetValue<string>()); Assert.Equal(new[] { "verlegungNach" }, Marker(k)); });
        }

        var klassenplan = await Karten("klasse", "K1");
        NeueStunde(klassenplan, "Klassenplan");
        Entfall(klassenplan);
        Ziel(klassenplan);

        // Raumplan: im neuen Raum nur die neue Stunde; im bisherigen Raum die Entfallkarte und die verlegte Stunde
        var raumplan = await Karten("raum", neuerRaum ? "Online" : "D004");
        NeueStunde(raumplan, "Raumplan");
        if (neuerRaum)
        {
            Assert.Single(raumplan);
            // bisheriger Raum: die verlegte Stunde am Ziel, am Ursprung kein Verlegungssymbol (die aus dem Raum genommene Stunde
            // erscheint dort nicht mehr – siehe AusRaumGenommen)
            var bisher = await Karten("raum", "D004");
            Assert.DoesNotContain(bisher, k => k["von"]!.GetValue<int>() <= 2 && Marker(k).Contains("verlegungVon"));
            // die neue Stunde (GID, im neuen Raum) steht nicht im bisherigen Raum – auch nicht bei offener Vertretung („???“); dort bleibt
            // nur die ursprüngliche Stunde als Entfall
            Assert.DoesNotContain(bisher, k => k["fach"]!.GetValue<string>() == "GID");
            Assert.Equal(new[] { "entfall" }, bisher.Where(k => k["von"]!.GetValue<int>() <= 2).Select(k => k["status"]!.GetValue<string>()).ToArray());
            Entfall(bisher);
            Ziel(bisher);
        }
        else
        {
            Entfall(raumplan);
            Ziel(raumplan);
        }

        // Lehrerplan OU: die eigene Stunde entfällt, die verlegte Stunde am Ziel – keine Karte mit Verlegungssymbol am Ursprung
        var lehrerplan = await Karten("lehrer", "OU");
        Assert.DoesNotContain(lehrerplan, k => k["von"]!.GetValue<int>() <= 2 && Marker(k).Contains("verlegungVon"));
        Assert.DoesNotContain(lehrerplan, k => k["von"]!.GetValue<int>() <= 2 && k["fach"]!.GetValue<string>() == "GID" && !offen);
        Entfall(lehrerplan);
        Ziel(lehrerplan);
    }
}
