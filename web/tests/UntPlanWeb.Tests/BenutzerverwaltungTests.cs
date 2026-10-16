using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Benutzerverwaltung: Rechteprüfung, eindeutige Zuordnung von Personen zu Konten, Personenliste mit Konto-Status, Konten nach
/// Login-Schema anlegen, Sammelaktionen, Zusatzrechte, Anmeldehistorie und Zugangsdaten als PDF.
/// </summary>
public class BenutzerverwaltungTests
{
    private static Task<HttpResponseMessage> Anlegen(HttpClient c, object daten) => c.PostAsJsonAsync("/api/app/admin/benutzer", daten);

    [Fact]
    public async Task Endpunkte_nur_fuer_die_Administration()
    {
        await using var s = await Testserver.StartenAsync();
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        var leitung = await s.AlsAsync("leitung1", "leitung", "MUE");
        foreach (var c in new[] { lehrer, leitung, s.Client() })
        {
            Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/api/app/admin/personen")).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/api/app/admin/benutzer/1")).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "lehrer" })).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "sperren", ids = new[] { 1 } })).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/admin/personen", new { art = "personal", name = "X" })).StatusCode);
        }
    }

    [Fact]
    public async Task Lehrerkuerzel_und_Schueler_sind_eindeutig_zugeordnet()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "lange", passwort = "start-passwort", rolle = "lehrer", element = "LAN", gesperrt = false })).StatusCode);
        // zweites Konto mit demselben Kürzel: abgelehnt (auch als Schulleitung, auch beim Ändern)
        var doppelt = await Anlegen(admin, new { login = "lange2", passwort = "start-passwort", rolle = "leitung", element = "lan", gesperrt = false });
        Assert.Equal(HttpStatusCode.BadRequest, doppelt.StatusCode);
        Assert.Contains("bereits", await doppelt.Content.ReadAsStringAsync());
        var anderes = await Testserver.JsonAsync(await Anlegen(admin, new { login = "meyer", passwort = "start-passwort", rolle = "lehrer", element = "MEY", gesperrt = false }));
        var id = anderes["benutzer"]!["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/app/admin/benutzer/{id}", new { rolle = "lehrer", element = "LAN", anzeigename = "", email = "", gesperrt = false })).StatusCode);
        // Klassenkonten (Rolle Schüler*in mit Klasse) sind davon nicht betroffen
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "klasse-x", passwort = "start-passwort", rolle = "schueler", element = "LAN", gesperrt = false })).StatusCode);

        // eine Schülerin bzw. ein Schüler hat höchstens ein Schülerkonto, aber beliebig viele Elternkonten
        var sch = s.Dienst<Paketdienst>().Aktuell!.Schueler[0];
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "sch1", passwort = "start-passwort", rolle = "schueler", gesperrt = false, schuelerIds = new[] { sch.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Anlegen(admin, new { login = "sch2", passwort = "start-passwort", rolle = "schueler", gesperrt = false, schuelerIds = new[] { sch.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "mutter", passwort = "start-passwort", rolle = "eltern", gesperrt = false, schuelerIds = new[] { sch.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "vater", passwort = "start-passwort", rolle = "eltern", gesperrt = false, schuelerIds = new[] { sch.Id } })).StatusCode);

        // Personenliste zeigt den Konto-Status
        var lehrer = (await Testserver.HolenAsync(admin, "/api/app/admin/personen?art=lehrer")).AsArray();
        Assert.Equal("lange", lehrer.First(p => p!["kurz"]!.GetValue<string>() == "LAN")!["konto"]!["login"]!.GetValue<string>());
        Assert.Null(lehrer.First(p => p!["kurz"]!.GetValue<string>() == "BEC")!["konto"]);
        var schueler = (await Testserver.HolenAsync(admin, "/api/app/admin/personen?art=schueler")).AsArray().First(p => p!["schluessel"]!.GetValue<string>() == sch.Id.ToString())!;
        Assert.Equal("sch1", schueler["konto"]!["login"]!.GetValue<string>());
        Assert.Equal(2, schueler["elternkonten"]!.AsArray().Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/app/admin/personen?art=unbekannt")).StatusCode);
    }

    [Fact]
    public async Task Konten_fuer_Personen_ohne_Konto_nach_Schema_anlegen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        Assert.Equal(HttpStatusCode.OK, (await Anlegen(admin, new { login = "bec", passwort = "start-passwort", rolle = "lehrer", element = "BEC", gesperrt = false })).StatusCode);

        // Lehrkräfte: Kürzel als Benutzername, vorhandene Konten werden übersprungen
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "lehrer", schema = "kuerzel" }));
        var lehrer = stand.Paket.Elemente.Lehrer.Count(l => l.Kurz.Length > 0);
        Assert.Equal(lehrer - 1, r["anzahl"]!.GetValue<int>());
        Assert.DoesNotContain(r["konten"]!.AsArray(), k => k!["zuordnung"]!.GetValue<string>() == "BEC");
        Assert.Contains("Benutzername;Startpasswort", r["csv"]!.GetValue<string>());
        var bd = s.Dienst<Benutzerdienst>();
        Assert.All(r["konten"]!.AsArray(), k => Assert.True(bd.NachLogin(k!["login"]!.GetValue<string>())!.PasswortAendern));
        Assert.Equal(0, (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "lehrer" })))["anzahl"]!.GetValue<int>());

        // Schüler*innen einer Klasse: vorname.nachname, Konto der Person zugeordnet, Login eindeutig
        var klasse = stand.Schueler[0].Klasse;
        var k = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "schueler", klasse, schema = "vorname.nachname" }));
        var inKlasse = stand.Schueler.Where(x => x.Klasse == klasse).ToList();
        Assert.Equal(inKlasse.Count, k["anzahl"]!.GetValue<int>());
        var erster = inKlasse[0];
        var konto = bd.Alle().Single(b => b.Rolle == Rollen.Schueler && b.SchuelerIds.Contains(erster.Id));
        Assert.Equal(Personendienst.Login("vorname.nachname", erster.Vorname, erster.Name, ""), konto.Login.TrimEnd('2', '3', '4'));
        Assert.Equal(klasse, konto.Klassen.Single());
        Assert.Equal(inKlasse.Count, k["konten"]!.AsArray().Select(x => x!["login"]!.GetValue<string>()).Distinct().Count());

        // Zugangsdaten als PDF
        var pdf = await admin.PostAsJsonAsync("/api/app/admin/personen/zugangsdaten.pdf", new { konten = k["konten"] });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4]);
    }

    [Theory]
    [InlineData("vorname.nachname", "Jörg", "Müller-Lüdenscheidt", "", "joerg.mueller-luedenscheidt")]
    [InlineData("v.nachname", "Anna", "Schäfer", "", "a.schaefer")]
    [InlineData("nachname.vorname", "Zoë", "Brandt", "", "brandt.zoe")]
    [InlineData("kuerzel", "Stefan", "Becker", "BEC", "bec")]
    [InlineData("kuerzel", "Ohne", "Kuerzel", "", "ohne.kuerzel")]
    public void Login_nach_Schema(string schema, string vorname, string name, string kurz, string erwartet) =>
        Assert.Equal(erwartet, Personendienst.Login(schema, vorname, name, kurz));

    [Fact]
    public async Task Personal_und_Erziehungsberechtigte_mit_Konto()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var kinder = s.Dienst<Paketdienst>().Aktuell!.Schueler.Take(2).Select(x => x.Id).ToArray();
        var p = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen", new { art = "personal", vorname = "Sabine", name = "Sekretär", funktion = "Sekretariat", email = "sek@schule.de" }));
        var e = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen", new { art = "eltern", vorname = "Eva", name = "Eltern", kinder }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/personen", new { art = "lehrer", name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/personen", new { art = "eltern", name = "X", kinder = new[] { 999999 } })).StatusCode);

        var r1 = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "personal", schluessel = new[] { p["schluessel"]!.GetValue<string>() }, schema = "v.nachname" }));
        Assert.Equal("s.sekretaer", r1["konten"]![0]!["login"]!.GetValue<string>());
        var r2 = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/personen/konten", new { art = "eltern" }));
        Assert.Equal(1, r2["anzahl"]!.GetValue<int>());
        var bd = s.Dienst<Benutzerdienst>();
        var sek = bd.NachLogin("s.sekretaer")!;
        Assert.Equal(Rollen.Lehrer, sek.Rolle);
        Assert.Equal("", sek.Element);
        Assert.Equal(int.Parse(p["schluessel"]!.GetValue<string>()), sek.PersonId);
        var eltern = bd.NachLogin(r2["konten"]![0]!["login"]!.GetValue<string>())!;
        Assert.Equal(Rollen.Eltern, eltern.Rolle);
        Assert.Equal(kinder.OrderBy(x => x), eltern.SchuelerIds.OrderBy(x => x));

        // eine Person gehört höchstens einem Konto
        Assert.Equal(HttpStatusCode.BadRequest, (await Anlegen(admin, new { login = "sek2", passwort = "start-passwort", rolle = "lehrer", gesperrt = false, personId = sek.PersonId })).StatusCode);
        var liste = (await Testserver.HolenAsync(admin, "/api/app/admin/personen?art=personal")).AsArray();
        Assert.Equal("s.sekretaer", liste.Single()!["konto"]!["login"]!.GetValue<string>());

        // Löschen der Person hebt die Zuordnung auf, das Konto bleibt
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/app/admin/personen/{sek.PersonId}")).StatusCode);
        Assert.Null(bd.NachLogin("s.sekretaer")!.PersonId);
    }

    [Fact]
    public async Task Sammelaktionen_Zusatzrechte_und_Anmeldehistorie()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var ids = new List<int>();
        foreach (var l in new[] { "a1", "a2", "a3" })
            ids.Add((await Testserver.JsonAsync(await Anlegen(admin, new { login = l, passwort = "start-passwort", rolle = "lehrer", gesperrt = false })))["benutzer"]!["id"]!.GetValue<int>());
        var bd = s.Dienst<Benutzerdienst>();

        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "passwort_zuruecksetzen", ids }));
        Assert.Equal(3, r["anzahl"]!.GetValue<int>());
        var neu = r["konten"]!["konten"]!.AsArray();
        Assert.Equal(3, neu.Count);
        Assert.True(bd.PasswortStimmt(ids[0], neu.First(k => k!["login"]!.GetValue<string>() == "a1")!["passwort"]!.GetValue<string>()));
        Assert.True(bd.Holen(ids[0])!.PasswortAendern);

        Assert.Equal(2, (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "sperren", ids = ids.Take(2) })))["anzahl"]!.GetValue<int>());
        Assert.True(bd.Holen(ids[1])!.Gesperrt);
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "rolle", rolle = "leitung", ids }));
        Assert.All(ids, i => Assert.Equal(Rollen.Leitung, bd.Holen(i)!.Rolle));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "rolle", rolle = "chef", ids })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "sperren", ids = Array.Empty<int>() })).StatusCode);
        // die eigene Rolle bleibt unverändert
        var ichId = bd.NachLogin("admin")!.Id;
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer/sammelaktion", new { aktion = "rolle", rolle = "lehrer", ids = new[] { ichId } }));
        Assert.Equal(Rollen.Admin, bd.Holen(ichId)!.Rolle);

        // Zusatzrecht „Nachrichten zum Tag“ für eine Lehrkraft
        var lehrer = await s.AlsAsync("zus", "lehrer", "SNE");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/nachrichten")).StatusCode);
        var zusId = bd.NachLogin("zus")!.Id;
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/admin/benutzer/{zusId}", new { rolle = "lehrer", element = "SNE", anzeigename = "Zus", email = "", gesperrt = false, zusatzrechte = new[] { "nachrichten", "unbekannt" } }));
        Assert.Equal(new[] { "nachrichten" }, bd.Holen(zusId)!.Zusatzrechte);
        Assert.Equal(HttpStatusCode.OK, (await lehrer.GetAsync("/api/app/admin/nachrichten")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/benutzer")).StatusCode);

        // Anmeldehistorie: erfolgreiche und fehlgeschlagene Versuche
        var c = s.Client();
        await c.PostAsJsonAsync("/api/app/anmelden", new { login = "zus", passwort = "falsch-falsch" });
        var d = await Testserver.HolenAsync(admin, $"/api/app/admin/benutzer/{zusId}");
        var verlauf = d["anmeldungen"]!.AsArray().Select(a => a!["ergebnis"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "falsch", "ok" }, verlauf);
        Assert.Equal("SNE", d["lehrkraft"]!["kurz"]!.GetValue<string>());
        Assert.Contains("nachrichten", d["benutzer"]!["zusatzrechte"]!.AsArray().Select(x => x!.GetValue<string>()));
        var grundlagen = await Testserver.HolenAsync(admin, "/api/app/admin/benutzerverwaltung/grundlagen");
        Assert.Equal(5, grundlagen["rollen"]!.AsArray().Count);
    }
}
