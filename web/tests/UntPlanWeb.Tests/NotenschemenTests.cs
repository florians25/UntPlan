using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Stammdaten | Notenschemen und Prüfungsarten: Rechte, Pflege, Reihenfolge, Gesamtnotenschema, Durchschnitt nur mit Werten.</summary>
public class NotenschemenTests
{
    [Fact]
    public async Task Notenschemen_anlegen_sortieren_Gesamtschema_und_loeschen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsJsonAsync("/api/app/notenschemen", new { name = "X", noten = new[] { new { text = "1" } } })).StatusCode);

        var mitWerten = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/notenschemen", new
        {
            name = "Prüfung mündlich", aktiv = true, noten = new object[] { new { text = "Bestanden", wert = 1 }, new { text = "Nicht Bestanden", wert = 5 }, new { text = " " } },
        }));
        Assert.True(mitWerten["mitDurchschnitt"]!.GetValue<bool>());
        Assert.Equal(2, mitWerten["noten"]!.AsArray().Count);
        var ohneWerte = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/notenschemen", new { name = "Mitarbeit", aktiv = true, noten = new[] { new { text = "+" }, new { text = "~" }, new { text = "-" } } }));
        Assert.False(ohneWerte["mitDurchschnitt"]!.GetValue<bool>());
        var id1 = mitWerten["id"]!.GetValue<int>();
        var id2 = ohneWerte["id"]!.GetValue<int>();

        // Prüfungen der Eingaben
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/notenschemen", new { name = "Mitarbeit", noten = new[] { new { text = "1" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/notenschemen", new { name = "Leer", noten = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/notenschemen", new { name = "Doppelt", noten = new[] { new { text = "1" }, new { text = "1" } } })).StatusCode);

        // Durchschnitt nur mit Werten
        var n = s.Dienst<Notendienst>();
        Assert.Equal(3.0, Notendienst.Durchschnitt(n.Schema(id1)!, new[] { "Bestanden", "Nicht Bestanden" }));
        Assert.Null(Notendienst.Durchschnitt(n.Schema(id2)!, new[] { "+", "-" }));

        // Reihenfolge per Pfeil bzw. alphabetisch, Gesamtnotenschema
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/notenschemen/reihenfolge", new { ids = new[] { id2, id1 } }));
        var liste = await Testserver.HolenAsync(admin, "/api/app/notenschemen");
        Assert.Equal(new[] { id2, id1 }, liste["schemen"]!.AsArray().Select(x => x!["id"]!.GetValue<int>()));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/notenschemen/reihenfolge", new { ids = (int[]?)null }));
        Assert.Equal(new[] { id2, id1 }, (await Testserver.HolenAsync(admin, "/api/app/notenschemen"))["schemen"]!.AsArray().Select(x => x!["id"]!.GetValue<int>()));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/notenschemen/gesamt", new { id = id1 }));
        Assert.Equal(id1, (await Testserver.HolenAsync(lehrer, "/api/app/notenschemen"))["gesamt"]!.GetValue<int>());

        // Bearbeiten: inaktiv → für Lehrkräfte nicht mehr sichtbar
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/notenschemen/{id2}", new { name = "Mitarbeit", aktiv = false, noten = new[] { new { text = "+" }, new { text = "-" } } }));
        Assert.DoesNotContain((await Testserver.HolenAsync(lehrer, "/api/app/notenschemen"))["schemen"]!.AsArray(), x => x!["id"]!.GetValue<int>() == id2);

        // Löschen: Gesamtnotenschema und Zuordnung zur Prüfungsart entfallen
        var art = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "ZwP", name = "Zwischenprüfung", muendlich = true, notenschemaId = id1 }));
        Assert.Equal("Prüfung mündlich", art["notenschema"]!.GetValue<string>());
        Assert.Equal(1, (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/notenschemen/loeschen", new { ids = new[] { id1 } })))["anzahl"]!.GetValue<int>());
        Assert.Null((await Testserver.HolenAsync(admin, "/api/app/notenschemen"))["gesamt"]);
        var zwp = (await Testserver.HolenAsync(admin, "/api/app/pruefungsarten?alle=true"))["arten"]!.AsArray().Single(x => x!["kurz"]!.GetValue<string>() == "ZwP")!;
        Assert.Null(zwp["notenschemaId"]);
        Assert.Equal("", zwp["notenschema"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pruefungsarten_Grundbestand_Pflege_und_Rechte()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("sne", "lehrer", "SNE");

        // bisher fest hinterlegte Prüfungsarten sind als Grundbestand vorhanden (erweitern statt doppeln)
        var arten = (await Testserver.HolenAsync(lehrer, "/api/app/pruefungsarten"))["arten"]!.AsArray();
        Assert.Equal(Listendienst.Pruefungsarten.OrderBy(x => x), arten.Select(x => x!["name"]!.GetValue<string>()).OrderBy(x => x));

        var neu = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/pruefungsarten", new
        {
            kurz = "SHW", name = "Schriftliche Wiederholung", aktiv = true, angekuendigt = false, uebergeben = true, vordergrund = "#FFFFFF", hintergrund = "#1f9d55",
            maxTag = 1, maxWoche = 2, maxZeitraum = 3, zeitraumTage = 14, gruppierung = 0, freieTage = 2, mitWochenende = true, gewichtung = 0.5,
        }));
        Assert.Equal(("#ffffff", 0.5, 3, 14), (neu["vordergrund"]!.GetValue<string>(), neu["gewichtung"]!.GetValue<double>(), neu["maxZeitraum"]!.GetValue<int>(), neu["zeitraumTage"]!.GetValue<int>()));
        var id = neu["id"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "shw" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "X", vordergrund = "rot" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "Y", maxTag = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "Z", notenschemaId = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsJsonAsync("/api/app/pruefungsarten", new { kurz = "L" })).StatusCode);

        // inaktiv: nur in der Verwaltung sichtbar
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/pruefungsarten/{id}", new { kurz = "SHW", name = "Schriftliche Wiederholung", aktiv = false }));
        Assert.DoesNotContain((await Testserver.HolenAsync(lehrer, "/api/app/pruefungsarten?alle=true"))["arten"]!.AsArray(), x => x!["id"]!.GetValue<int>() == id);
        Assert.Contains((await Testserver.HolenAsync(admin, "/api/app/pruefungsarten?alle=true"))["arten"]!.AsArray(), x => x!["id"]!.GetValue<int>() == id);

        // alphabetisch sortieren, löschen
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/pruefungsarten/reihenfolge", new { ids = (int[]?)null }));
        var kurz = (await Testserver.HolenAsync(admin, "/api/app/pruefungsarten?alle=true"))["arten"]!.AsArray().Select(x => x!["kurz"]!.GetValue<string>()).ToList();
        Assert.Equal(kurz.OrderBy(x => x, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true)), kurz);
        Assert.Equal(1, (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/pruefungsarten/loeschen", new { ids = new[] { id } })))["anzahl"]!.GetValue<int>());
    }
}
