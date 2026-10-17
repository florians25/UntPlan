using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Mitteilungen für die Apps: „Antwort nicht erlaubt“, Detail einer Mitteilung mit Download-Adressen der Anhänge,
/// Personen für die Empfängerauswahl (Kolleg*innen / Individuell) mit Rollen-Pills und Filtern.
/// </summary>
public class MitteilungenAppTests
{
    private static int Id(Testserver s, string login) => s.Dienst<Benutzerdienst>().NachLogin(login)!.Id;

    [Fact]
    public async Task Antwort_nicht_erlaubt_sperrt_Antworten()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lk = await s.AlsAsync("lk-a", "lehrer", "SNE");
        var gesperrt = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen", new
        {
            empfaenger = new[] { $"benutzer:{Id(s, "lk-a")}" }, betreff = "Info", text = "Bitte nicht antworten", wichtig = false, antwortErlaubt = false,
        })))["id"]!.GetValue<int>();
        var offen = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen", new
        {
            empfaenger = new[] { $"benutzer:{Id(s, "lk-a")}" }, betreff = "Frage", text = "Antwort erwünscht", wichtig = false,
        })))["id"]!.GetValue<int>();

        var posteingang = (await Testserver.HolenAsync(lk, "/api/app/mitteilungen")).AsArray();
        Assert.False(posteingang.Single(m => m!["id"]!.GetValue<int>() == gesperrt)!["antwortErlaubt"]!.GetValue<bool>());
        Assert.True(posteingang.Single(m => m!["id"]!.GetValue<int>() == offen)!["antwortErlaubt"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.BadRequest, (await lk.GetAsync($"/api/app/mitteilungen/{gesperrt}/antwort")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lk.PostAsJsonAsync("/api/app/mitteilungen", new
        {
            empfaenger = new[] { $"benutzer:{Id(s, "admin")}" }, betreff = "AW: Info", text = "doch", wichtig = false, antwortAuf = gesperrt,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lk.GetAsync($"/api/app/mitteilungen/{offen}/antwort")).StatusCode);

        var detail = await Testserver.HolenAsync(lk, $"/api/app/mitteilungen/{gesperrt}");
        Assert.Equal("posteingang", detail["ordner"]!.GetValue<string>());
        Assert.False(detail["darfAntworten"]!.GetValue<bool>());
        Assert.True((await Testserver.HolenAsync(lk, $"/api/app/mitteilungen/{offen}"))["darfAntworten"]!.GetValue<bool>());
        // beim Absender in „Gesendet“
        var gesendet = await Testserver.HolenAsync(admin, $"/api/app/mitteilungen/{gesperrt}");
        Assert.Equal("gesendet", gesendet["ordner"]!.GetValue<string>());
        Assert.False(gesendet["antwortErlaubt"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await lk.GetAsync("/api/app/mitteilungen/999999")).StatusCode);

        var optionen = await Testserver.HolenAsync(lk, "/api/app/mitteilungen/optionen");
        Assert.True(optionen["antwortSperre"]!.GetValue<bool>());
        Assert.Equal(2, optionen["typen"]!.AsArray().Count);
    }

    [Fact]
    public async Task Entwurf_mit_Antwortsperre_und_Anhang_im_Detail_mit_Download()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lk = await s.AlsAsync("lk-b", "lehrer", "SNE");
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen/entwurf", new
        {
            empfaenger = new[] { $"benutzer:{Id(s, "lk-b")}" }, betreff = "BSO2", text = "s. Anhang", wichtig = false, lesebestaetigung = true, antwortErlaubt = false,
        })))["id"]!.GetValue<int>();
        using var form = new MultipartFormDataContent();
        var datei = new ByteArrayContent("%PDF-1.4 test"u8.ToArray());
        datei.Headers.ContentType = new("application/pdf");
        form.Add(datei, "datei", "OpenPeriod.pdf");
        await Testserver.JsonAsync(await admin.PostAsync($"/api/app/mitteilungen/{id}/anhaenge", form));
        await Testserver.JsonAsync(await admin.PostAsync($"/api/app/mitteilungen/{id}/senden", null));

        var detail = await Testserver.HolenAsync(lk, $"/api/app/mitteilungen/{id}");
        Assert.False(detail["antwortErlaubt"]!.GetValue<bool>());
        Assert.True(detail["lesebestaetigung"]!.GetValue<bool>());
        Assert.Null(detail["gelesen"]);
        var anhang = detail["anhaenge"]!.AsArray().Single()!;
        Assert.Equal("OpenPeriod.pdf", anhang["name"]!.GetValue<string>());
        Assert.Equal("application/pdf", anhang["typ"]!.GetValue<string>());
        var r = await lk.GetAsync(anhang["url"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("%PDF-1.4 test", await r.Content.ReadAsStringAsync());
        // Detail markiert nicht als gelesen
        Assert.Null((await Testserver.HolenAsync(lk, "/api/app/mitteilungen")).AsArray().Single(m => m!["id"]!.GetValue<int>() == id)!["gelesen"]);
    }

    [Fact]
    public async Task Personen_fuer_die_Empfaengerauswahl_mit_Rollen_und_Filtern()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var elemente = await Testserver.HolenAsync(admin, "/api/app/elemente");
        var klasse = elemente["klassen"]!.AsArray().First(k => k!["leitung"]?.AsArray().Count > 0)!;
        var kl = klasse["kurz"]!.GetValue<string>();
        var leitung = klasse["leitung"]!.AsArray()[0]!.GetValue<string>();
        var andere = elemente["lehrer"]!.AsArray().Select(l => l!["kurz"]!.GetValue<string>()).First(l => l != leitung);
        await s.AlsAsync("kl-leitung", "lehrer", leitung);
        await s.AlsAsync("lk-andere", "lehrer", andere);
        await s.AlsAsync("schueler-x", "schueler", kl);
        // Abteilung über den Stammraum der Klasse
        var stammraum = s.Dienst<Paketdienst>().Aktuell!.Klassen[kl].Stammraum;
        if (stammraum.Length > 0)
            s.Dienst<UntPlanWeb.Daten.Db>().Ausfuehren("INSERT INTO raum_daten (raum, kapazitaet, gebaeude, abteilung, raumgruppe, geaendert) VALUES (@r, 0, '', 'Technik', '', '')",
                UntPlanWeb.Daten.Db.P("@r", stammraum));

        var kollegium = await Testserver.HolenAsync(admin, "/api/app/mitteilungen/personen?typ=kollegium");
        Assert.Equal("kollegium", kollegium["typ"]!.GetValue<string>());
        var personen = kollegium["personen"]!.AsArray();
        Assert.DoesNotContain(personen, p => p!["login"]!.GetValue<string>() == "schueler-x");
        var p1 = personen.Single(p => p!["login"]!.GetValue<string>() == "kl-leitung")!;
        Assert.Equal(leitung, p1["name"]!.GetValue<string>());
        Assert.Equal(leitung, p1["kuerzel"]!.GetValue<string>());
        Assert.Equal($"benutzer:{Id(s, "kl-leitung")}", p1["id"]!.GetValue<string>());
        var rollen = p1["rollen"]!.AsArray();
        Assert.Equal("lehrkraft", rollen[0]!["art"]!.GetValue<string>());
        var klPill = rollen.Single(r => r!["art"]!.GetValue<string>() == "klassenlehrkraft")!;
        Assert.StartsWith("Klassenlehrkraft | ", klPill["text"]!.GetValue<string>());
        Assert.Contains(kl, klPill["klassen"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains(kl, p1["klassen"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains(kl, kollegium["filter"]!["klassen"]!.AsArray().Select(x => x!.GetValue<string>()));
        if (stammraum.Length > 0)
        {
            Assert.Contains("Technik", p1["abteilungen"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.Contains("Technik", kollegium["filter"]!["abteilungen"]!.AsArray().Select(x => x!.GetValue<string>()));
        }
        Assert.DoesNotContain(personen.Single(p => p!["login"]!.GetValue<string>() == "lk-andere")!["rollen"]!.AsArray(),
            r => r!["art"]!.GetValue<string>() == "klassenlehrkraft" && r["klassen"]!.AsArray().Any(x => x!.GetValue<string>() == kl));
        Assert.Empty(kollegium["gruppen"]!.AsArray());

        // Individuell: auch Schüler*innen, dazu die Gruppen
        var individuell = await Testserver.HolenAsync(admin, "/api/app/mitteilungen/personen?typ=individuell");
        var sx = individuell["personen"]!.AsArray().Single(p => p!["login"]!.GetValue<string>() == "schueler-x")!;
        Assert.Equal("schueler", sx["rollen"]!.AsArray().Single()!["art"]!.GetValue<string>());
        Assert.NotEmpty(individuell["gruppen"]!.AsArray());

        // Listen als Filter
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen/listen", new { name = "Fachschaft", mitglieder = new[] { $"benutzer:{Id(s, "kl-leitung")}" } }));
        var mitListe = await Testserver.HolenAsync(admin, "/api/app/mitteilungen/personen");
        var liste = mitListe["filter"]!["listen"]!.AsArray().Single()!;
        Assert.StartsWith("liste:", liste["id"]!.GetValue<string>());
        Assert.Equal($"benutzer:{Id(s, "kl-leitung")}", liste["mitglieder"]!.AsArray().Single()!.GetValue<string>());
    }
}
