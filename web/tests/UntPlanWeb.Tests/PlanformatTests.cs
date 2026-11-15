using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Stundenplanformate (Administration → Stundenplaneinstellungen): Formate anlegen und prüfen, Plänen zuordnen; der Plan liefert das Format
/// mit Langnamen aus und wendet Tage, entfallenen Unterricht und Hervorhebungen an.
/// </summary>
public class PlanformatTests
{
    private static object Format(string name, bool entfall = true, int[]? tage = null) => new
    {
        name, details = false, farben = true, zeiten = true, stundennummer = false,
        elemente = new[] { new { art = "fach", name = "lang" }, new { art = "lehrer", name = "kurz" }, new { art = "raum", name = "kurz" } },
        tage = tage ?? new[] { 0, 1, 2, 3, 4 }, zeitVon = "", zeitBis = "",
        entfall, bereitschaften = true, sprechstunden = true, buchungen = true,
        aenderungen = true, pruefungen = true, entfaelle = true, symbole = true,
    };

    [Fact]
    public async Task Format_anlegen_zuordnen_und_im_Klassenplan_anwenden()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var f = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("Schüler*innen", entfall: false, tage: new[] { 0, 1, 2 })));
        var id = f["id"]!.GetValue<int>();
        Assert.True(id > 0);
        // gleicher Name: abgelehnt; ohne Element: abgelehnt
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/planformate", Format("schüler*innen"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/planformate", new { name = "Leer", elemente = Array.Empty<object>() })).StatusCode);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate-zuordnung", new { klasse = id, kollegium = false }));

        // Kollegium: Standarddarstellung (kein Format)
        var lehrkraft = await s.AlsAsync("mue", "lehrer", "MUE");
        var planL = await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13");
        Assert.Null(planL["format"]);
        // Schüler*in: Format, nur Mo–Mi, kein entfallener Unterricht
        var schueler = await s.AlsAsync("schueler7a", "schueler", "7a");
        var plan = await Testserver.HolenAsync(schueler, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13");
        Assert.Equal("Schüler*innen", plan["format"]!["name"]!.GetValue<string>());
        Assert.False(plan["format"]!["details"]!.GetValue<bool>());
        Assert.Equal(3, plan["tage"]!.AsArray().Count);
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["status"]!.GetValue<string>() == "entfall");
        Assert.NotNull(plan["langnamen"]);
        Assert.Equal(id, plan["formatId"]!.GetValue<int>());
        Assert.Contains(plan["formate"]!.AsArray(), x => x!["id"]!.GetValue<int>() == id && x["name"]!.GetValue<string>() == "Schüler*innen");
        // Auswahl im Menü „⋮“: Standard (0) bzw. ein bestimmtes Format, unbekannte Id = Vorgabe
        var standard = await Testserver.HolenAsync(schueler, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format=0");
        Assert.Null(standard["format"]);
        Assert.Equal(0, standard["formatId"]!.GetValue<int>());
        Assert.True(standard["tage"]!.AsArray().Count >= 5);
        var gewaehlt = await Testserver.HolenAsync(lehrkraft, $"/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format={id}");
        Assert.Equal(id, gewaehlt["formatId"]!.GetValue<int>());
        Assert.Null((await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13&format=999"))["format"]);

        // Kollegium einschließen
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/planformate-zuordnung", new { klasse = id, kollegium = true }));
        Assert.NotNull((await Testserver.HolenAsync(lehrkraft, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-13"))["format"]);
        // Löschen hebt die Zuordnung auf
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/admin/planformate/{id}"));
        var liste = await Testserver.HolenAsync(admin, "/api/app/admin/planformate");
        Assert.Empty(liste["formate"]!.AsArray());
        Assert.Null(liste["zuordnung"]!["klasse"]);
        // nur Administration
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrkraft.GetAsync("/api/app/admin/planformate")).StatusCode);
    }

    [Fact]
    public void Hervorhebungen_aus_machen_geaenderte_Stunden_schlicht()
    {
        var k = new UntPlanWeb.Dienste.Karte("2026-10-13", 1, 1, "08:00", "08:45", "geaendert", "M", null, "Mathe", "#ff0000",
            new() { new("7a") }, new() { new("BEC", "MUE", true) }, new() { new("A1") }, "Vertretung", "", 1, 1, "");
        var w = new UntPlanWeb.Dienste.Wochenplan("klasse", "7a", "7a", "2026-10-12", "", new() { new(1, "1", "08:00", "08:45") },
            new() { new UntPlanWeb.Dienste.Tageskopf("2026-10-13", "Dienstag", "", false, null, false, new(), new()) }, new() { k }, new());
        var f = new UntPlanWeb.Dienste.Planformat { Id = 1, Name = "x", Elemente = new() { new("fach") }, Aenderungen = false, Farben = false, Symbole = false };
        var stand = new UntPlanWeb.Dienste.Stand(new UntPlan.Web.Contracts.WebPaket(), "2026-10-12 07:00:00");
        var r = UntPlanWeb.Dienste.Planformatdienst.Anwenden(w, f, stand);
        var x = Assert.Single(r.Karten);
        Assert.Equal("regulaer", x.Status);
        Assert.Equal("", x.Farbe);
        Assert.Null(x.Lehrer[0].Alt);
        Assert.Empty(x.Marker);
    }
}
