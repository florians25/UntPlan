using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Leitfaden Klassenleitung, Kapitel 2–7: Schülergruppen (Zuordnung, Gruppenwechsel mit Zuordnungshistorie, neue Gruppe), Schüler-Gruppen-
/// Zuordnung (Matrix), Klassendienst „Klassensprecher*in Stv.“, Bilder (einzeln, ZIP-Bildimport, Profil), Kennwort-Blatt, „Kennwort vergessen“,
/// Selbstregistrierung der Eltern und Betriebe, Kontaktdaten sowie die Oberfläche (Menü, Dialoge).
/// </summary>
public class KlassenleitungTests
{
    private static readonly DateOnly Heute = new(2026, 10, 14);

    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    /// <summary>Unterricht der Klasse mit Lehrkraft (Standardgruppe).</summary>
    private static (int Id, string Lehrer) Unterricht(Testserver s, string klasse)
    {
        var z = s.Dienst<Listendienst>().Unterrichte(s.Dienst<Paketdienst>().Aktuell!, Heute, null, klasse).First(x => x.Lehrer.Count > 0);
        return (z.Id, z.Lehrer[0]);
    }

    private static string Klassenleitung(Testserver s, string klasse) => s.Dienst<Paketdienst>().Aktuell!.Klassen[klasse].Klassenleitung[0];

    private static List<int> Teilnehmer(Testserver s, int unterricht, string klasse, DateOnly d) =>
        Klassenzugehoerigkeit.DerStunde(s.Dienst<Paketdienst>().Aktuell!, unterricht, new[] { klasse }, d).Select(x => x.Id).ToList();

    private static string Mail(Testserver s, string muster) =>
        Regex.Match(s.Dienst<Maildienst>().Ausgang().First().Text, muster).Groups[1].Value;

    // ------------------------------------------------------------------------------ Schülergruppen

    [Fact]
    public async Task Gruppenwechsel_mit_Zeitraum_fuehrt_Zuordnungshistorie()
    {
        await using var s = await Testserver.StartenAsync();
        var (u, lehrer) = Unterricht(s, "7a");
        // die Lehrkraft des Unterrichts ordnet zu (Weg 1: Mein Unterricht)
        var lk = await s.AlsAsync("lk", "lehrer", lehrer);
        var m = await Testserver.HolenAsync(lk, $"/api/app/unterrichtklassen/{u}/gruppen/0/schueler?datum=2026-10-14");
        Assert.True(m["schreiben"]!.GetValue<bool>());
        var ids = m["schueler"]!.AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        var (a, b) = (ids[0], ids[1]);
        Assert.False(string.IsNullOrEmpty(m["schueler"]![0]!["kurzname"]!.GetValue<string>()));

        // a wechselt zum 13.10. aus der Gruppe (Ende 12.10.), b kommt ab 13.10. hinzu – Häkchen unverändert, Zeiträume eingetragen
        var r = await Testserver.JsonAsync(await lk.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/0/schueler?datum=2026-10-14", new
        {
            schueler = new object[]
            {
                new { schuelerId = a, auswahl = true, zeitraeume = new[] { new { von = "2026-08-01", bis = "2026-10-12" } } },
                new { schuelerId = b, auswahl = true, zeitraeume = new[] { new { von = "2026-10-13", bis = "" } } },
            },
        }));
        // neu aufrufen: a ohne Häkchen, abgelaufener Zeitraum in der Zuordnungshistorie; b mit Häkchen und Von
        var za = r["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == a)!;
        Assert.False(za["auswahl"]!.GetValue<bool>());
        Assert.Equal("2026-10-12", za["historie"]![0]!["bis"]!.GetValue<string>());
        var zb = r["schueler"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == b)!;
        Assert.True(zb["auswahl"]!.GetValue<bool>());
        Assert.Equal("2026-10-13", zb["von"]!.GetValue<string>());
        Assert.Contains(a, Teilnehmer(s, u, "7a", Heute.AddDays(-2)));
        Assert.DoesNotContain(a, Teilnehmer(s, u, "7a", Heute));
        Assert.DoesNotContain(b, Teilnehmer(s, u, "7a", Heute.AddDays(-2)));
        Assert.Contains(b, Teilnehmer(s, u, "7a", Heute));

        // erneutes Speichern ohne Änderung (Häkchen weiterhin aus, Historie mitgeschickt) behält die Zugehörigkeit im alten Zeitraum
        await Testserver.JsonAsync(await lk.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/0/schueler?datum=2026-10-14", new
        {
            schueler = new object[] { new { schuelerId = a, auswahl = false, zeitraeume = new[] { new { von = "2026-08-01", bis = "2026-10-12" } } } },
        }));
        Assert.Contains(a, Teilnehmer(s, u, "7a", Heute.AddDays(-2)));

        // mehrere Zeiträume (Matrix): a zusätzlich ab 01.02.2027
        var gm = s.Dienst<Paketdienst>().Aktuell!.Zuordnungen.Gruppen[u].Single(g => g.IstStandard).Mitglieder.Single(x => x.SchuelerId == a);
        Assert.Single(gm.Zeitraeume);
        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/schuelergruppen/matrix", new
        {
            aenderungen = new[] { new { unterricht = u, gruppe = 0, schuelerId = a, auswahl = true, zeitraeume = new[] { new { von = "2026-08-01", bis = "2026-10-12" }, new { von = "2027-02-01", bis = "" } } } },
        }));
        gm = s.Dienst<Paketdienst>().Aktuell!.Zuordnungen.Gruppen[u].Single(g => g.IstStandard).Mitglieder.Single(x => x.SchuelerId == a);
        Assert.Equal(2, gm.Zeitraeume.Count);
        Assert.True(gm.Am(new DateOnly(2027, 3, 1)));
        Assert.False(gm.Am(Heute));

        // Häkchen ohne Zeitraum entfernen = gar nicht mehr in der Gruppe
        await Testserver.JsonAsync(await lk.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/0/schueler", new
        {
            schueler = new object[] { new { schuelerId = a, auswahl = false, zeitraeume = Array.Empty<object>() } },
        }));
        Assert.DoesNotContain(a, Teilnehmer(s, u, "7a", Heute.AddDays(-2)));
    }

    [Fact]
    public async Task Klassenleitung_darf_Schuelergruppen_bearbeiten_andere_Lehrkraefte_nicht()
    {
        await using var s = await Testserver.StartenAsync();
        var (u, lehrer) = Unterricht(s, "7a");
        var kl = await s.AlsAsync("kl", "lehrer", Klassenleitung(s, "7a"));
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        var leer = new { schueler = Array.Empty<object>() };
        await Testserver.JsonAsync(await kl.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/0/schueler", leer));
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/0/schueler", leer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen", new { name = "A" })).StatusCode);
        var schueler = await s.AlsAsync("kind", "schueler", "7a");
        Assert.Equal(HttpStatusCode.Forbidden, (await schueler.GetAsync("/api/app/schuelergruppen?klasse=7a")).StatusCode);
    }

    [Fact]
    public async Task Liste_Neu_und_Matrix_mit_Auswahlkriterien()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var liste = await Testserver.HolenAsync(admin, "/api/app/schuelergruppen?klasse=7a");
        var gruppen = liste["gruppen"]!.AsArray();
        Assert.NotEmpty(gruppen);
        Assert.All(gruppen, g => Assert.True(g!["standard"]!.GetValue<bool>()));
        var u = gruppen[0]!["unterricht"]!.GetValue<int>();
        var fach = gruppen[0]!["fach"]!.GetValue<string>();
        Assert.Contains(fach, liste["faecher"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.All((await Testserver.HolenAsync(admin, $"/api/app/schuelergruppen?klasse=7a&fach={Uri.EscapeDataString(fach)}"))["gruppen"]!.AsArray(),
            g => Assert.Contains(fach, g!["fach"]!.GetValue<string>()));

        // „Neu“: weitere Schülergruppe – nur die ausgewählten Schüler*innen nehmen teil
        var neu = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen", new { name = "Gruppe A", von = "", bis = "" }));
        var gid = neu["id"]!.GetValue<int>();
        var m = await Testserver.HolenAsync(admin, $"/api/app/unterrichtklassen/{u}/gruppen/{gid}/schueler");
        Assert.All(m["schueler"]!.AsArray(), x => Assert.False(x!["auswahl"]!.GetValue<bool>()));
        var erster = m["schueler"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/unterrichtklassen/{u}/gruppen/{gid}/schueler", new { schueler = new[] { new { schuelerId = erster, auswahl = true, zeitraeume = Array.Empty<object>() } } }));
        Assert.Equal(1, (await Testserver.HolenAsync(admin, $"/api/app/unterrichtklassen/{u}/gruppen"))["gruppen"]!.AsArray().Single(g => g!["id"]!.GetValue<int>() == gid)!["anzahl"]!.GetValue<int>());
        // Filter Schüler*in: nur die Gruppen der Schülerin/des Schülers
        var proSchueler = (await Testserver.HolenAsync(admin, $"/api/app/schuelergruppen?klasse=7a&schueler={erster}"))["gruppen"]!.AsArray();
        Assert.Contains(proSchueler, g => g!["id"]!.GetValue<int>() == gid);
        var anderer = m["schueler"]![1]!["id"]!.GetValue<int>();
        Assert.DoesNotContain((await Testserver.HolenAsync(admin, $"/api/app/schuelergruppen?klasse=7a&schueler={anderer}"))["gruppen"]!.AsArray(), g => g!["id"]!.GetValue<int>() == gid);

        // Matrix: Klasse bringt Schüler*innen und Gruppen, ein weiteres Fach weitere Gruppen (additiv)
        var mx = await Testserver.HolenAsync(admin, "/api/app/schuelergruppen/matrix?klassen=7a");
        var spalten = mx["spalten"]!.AsArray();
        var zeilen = mx["zeilen"]!.AsArray();
        Assert.Contains(spalten, c => c!["gruppe"]!.GetValue<int>() == gid);
        Assert.All(zeilen, z => Assert.Equal(spalten.Count, z!["zellen"]!.AsArray().Count));
        var mx2 = await Testserver.HolenAsync(admin, $"/api/app/schuelergruppen/matrix?klassen=7a&faecher={Uri.EscapeDataString(fach)}");
        Assert.True(mx2["zeilen"]!.AsArray().Count >= zeilen.Count);
        Assert.Empty((await Testserver.HolenAsync(admin, "/api/app/schuelergruppen/matrix"))["spalten"]!.AsArray());
    }

    // ------------------------------------------------------------------------------ Klassendienste, Bilder, Kontaktdaten

    [Fact]
    public void Klassendienst_Klassensprecher_Stellvertretung_ist_angelegt()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "untplanweb-kl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        try
        {
            var db = new Db(ordner);
            Assert.Equal(1L, Convert.ToInt64(db.Wert("SELECT COUNT(*) FROM klassendienste WHERE kurz = 'KS Stv.'")));
            Assert.Contains(67, Db.Migrationsnummern);
        }
        finally { try { Directory.Delete(ordner, true); } catch { /* egal */ } }
    }

    private static byte[] Jpeg() => Convert.FromBase64String("/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=");

    [Fact]
    public async Task Bilder_einzeln_als_Zip_und_im_Profil()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var kl = await s.AlsAsync("kl", "lehrer", Klassenleitung(s, "7a"));
        var schueler = Klassenzugehoerigkeit.Am(st, new[] { "7a" }, Heute);
        var (x, y) = (schueler[0], schueler[1]);

        // ZIP-Bildimport „Nachname, Vorname.jpg“
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var n in new[] { $"{x.Name}, {x.Vorname}.jpg", "Niemand, Nobody.jpg" })
                using (var w = zip.CreateEntry(n).Open()) w.Write(Jpeg());
        }
        var form = new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "datei", "bilder.zip" } };
        var r = await Testserver.JsonAsync(await kl.PostAsync("/api/app/schueler/bildimport?klasse=7a", form));
        Assert.Equal(1, r["importiert"]!.GetValue<int>());
        Assert.Single(r["fehler"]!.AsArray());
        Assert.Contains(x.Id, (await Testserver.HolenAsync(kl, "/api/app/schueler/bilder")).AsArray().Select(i => i!.GetValue<int>()));
        var bild = await kl.GetAsync($"/api/app/schueler/{x.Id}/bild");
        Assert.Equal("image/jpeg", bild.Content.Headers.ContentType!.MediaType);

        // kein Bild: abgelehnt; fremde Lehrkraft darf nicht hochladen
        var keinBild = new MultipartFormDataContent { { new ByteArrayContent("kein Bild"u8.ToArray()), "datei", "x.jpg" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await kl.PostAsync($"/api/app/schueler/{y.Id}/bild", keinBild)).StatusCode);
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsync($"/api/app/schueler/{y.Id}/bild", new MultipartFormDataContent { { new ByteArrayContent(Jpeg()), "datei", "y.jpg" } })).StatusCode);

        // Schülerkonto lädt das eigene Bild im Profil hoch, aber kein fremdes
        var bd = s.Dienst<Benutzerdienst>();
        var konto = bd.Anlegen("kind-y", "test-passwort", Rollen.Schueler, "7a", "Kind");
        bd.SchuelerZuordnen(konto.Id, new[] { y.Id });
        var kind = s.Client();
        await Testserver.JsonAsync(await kind.PostAsJsonAsync("/api/app/anmelden", new { login = "kind-y", passwort = "test-passwort" }));
        await Testserver.JsonAsync(await kind.PostAsync($"/api/app/schueler/{y.Id}/bild", new MultipartFormDataContent { { new ByteArrayContent(Jpeg()), "datei", "ich.jpg" } }));
        Assert.Equal(HttpStatusCode.Forbidden, (await kind.PostAsync($"/api/app/schueler/{x.Id}/bild", new MultipartFormDataContent { { new ByteArrayContent(Jpeg()), "datei", "x.jpg" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kind.GetAsync($"/api/app/schueler/{x.Id}/bild")).StatusCode);
    }

    [Fact]
    public async Task Kontaktdaten_nur_fuer_Klassenleitung()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var kl = await s.AlsAsync("kl", "lehrer", Klassenleitung(s, "7a"));
        var x = Klassenzugehoerigkeit.Am(st, new[] { "7a" }, Heute)[0];
        await Testserver.JsonAsync(await kl.PutAsJsonAsync($"/api/app/schueler/{x.Id}/adresse", new { email = "kind@schule.example", telefon = "", mobil = "0151 1" }));
        await Testserver.JsonAsync(await kl.PostAsJsonAsync($"/api/app/schueler/{x.Id}/kontakte", new { rolle = "erziehungsberechtigt", name = "Mutter", email = "mutter@example.org" }));
        await Testserver.JsonAsync(await kl.PostAsJsonAsync($"/api/app/schueler/{x.Id}/kontakte", new { rolle = "ausbildungsbeauftragt", name = "Ausbilderin", betrieb = "Muster GmbH", email = "ausbildung@muster.example" }));
        var k = await Testserver.HolenAsync(kl, $"/api/app/schueler/{x.Id}/kontaktdaten");
        Assert.Equal("kind@schule.example", k["schueler"]!["email"]!.GetValue<string>());
        Assert.Equal("0151 1", k["schueler"]!["mobil"]!.GetValue<string>());
        Assert.Equal(2, k["kontakte"]!.AsArray().Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await kl.PostAsJsonAsync($"/api/app/schueler/{x.Id}/kontakte", new { rolle = "erziehungsberechtigt", name = "X", email = "keine-adresse" })).StatusCode);
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.GetAsync($"/api/app/schueler/{x.Id}/kontaktdaten")).StatusCode);
    }

    // ------------------------------------------------------------------------------ Zugang: Kennwort-Blatt, Kennwort vergessen, Selbstregistrierung

    [Fact]
    public async Task Kennwortblatt_legt_Schuelerkonten_an()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var kl = await s.AlsAsync("kl", "lehrer", Klassenleitung(s, "7a"));
        var schueler = Klassenzugehoerigkeit.Am(st, new[] { "7a" }, Heute);
        var vorher = await Testserver.HolenAsync(kl, "/api/app/kennwortblatt?klasse=7a");
        Assert.All(vorher.AsArray(), x => Assert.Equal("", x!["login"]!.GetValue<string>()));
        var r = await kl.PostAsJsonAsync("/api/app/kennwortblatt", new { klasse = "7a", schuelerIds = new[] { schueler[0].Id, schueler[1].Id } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await r.Content.ReadAsByteArrayAsync())[..4]));
        var konten = s.Dienst<Benutzerdienst>().Alle().Where(b => b.Rolle == Rollen.Schueler && b.SchuelerIds.Count == 1).ToList();
        Assert.Equal(2, konten.Count);
        Assert.All(konten, b => Assert.True(b.PasswortAendern));
        Assert.Contains(konten, b => b.Login == Unterrichtsgruppen.Kurzname(schueler[0].Name, schueler[0].Vorname).ToLowerInvariant());
        var fremd = await s.AlsAsync("fremd", "lehrer", "XYZ");
        Assert.Equal(HttpStatusCode.Forbidden, (await fremd.PostAsJsonAsync("/api/app/kennwortblatt", new { klasse = "7a" })).StatusCode);
        Assert.Equal("BaskhaMad", Unterrichtsgruppen.Kurzname("Baskharon", "Madona"));
        Assert.Equal("MuelleJoe", Unterrichtsgruppen.Kurzname("Müller", "Joel"));
    }

    [Fact]
    public async Task Kennwort_vergessen_mit_Link_und_neuem_Kennwort()
    {
        await using var s = await Testserver.StartenAsync();
        var bd = s.Dienst<Benutzerdienst>();
        bd.Anlegen("muellerjo", "altes-passwort", Rollen.Lehrer, "", "Jonas Müller", "jonas@schule.example");
        var anonym = s.Client();
        // falsche Adresse: gleiche Antwort, keine E-Mail
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/kennwort-vergessen", new { login = "muellerjo", email = "falsch@example.org" }));
        Assert.Empty(s.Dienst<Maildienst>().Ausgang());
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/kennwort-vergessen", new { login = "muellerjo", email = "jonas@schule.example" }));
        var token = Mail(s, @"#/kennwort/(\S+)");
        Assert.NotEmpty(token);
        Assert.Equal("jonas@schule.example", s.Dienst<Maildienst>().Ausgang().First().An);
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/kennwort-zuruecksetzen", new { token }));
        var neu = Mail(s, @"Kennwort: (\S+)");
        Assert.True(bd.PasswortStimmt(bd.NachLogin("muellerjo")!.Id, neu));
        Assert.True(bd.NachLogin("muellerjo")!.PasswortAendern);
        // Link nur einmal gültig
        Assert.Equal(HttpStatusCode.BadRequest, (await anonym.PostAsJsonAsync("/api/app/kennwort-zuruecksetzen", new { token })).StatusCode);
    }

    [Fact]
    public async Task Selbstregistrierung_ueber_hinterlegte_Adresse()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var kl = await s.AlsAsync("kl", "lehrer", Klassenleitung(s, "7a"));
        var x = Klassenzugehoerigkeit.Am(st, new[] { "7a" }, Heute)[0];
        await Testserver.JsonAsync(await kl.PostAsJsonAsync($"/api/app/schueler/{x.Id}/kontakte", new { rolle = "erziehungsberechtigt", name = "Mutter", email = "Mutter@Example.org" }));
        var anonym = s.Client();
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/registrieren", new { email = "unbekannt@example.org" }));
        Assert.Empty(s.Dienst<Maildienst>().Ausgang());
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/registrieren", new { email = "mutter@example.org" }));
        var token = Mail(s, @"#/registrieren/(\S+)");
        var info = await Testserver.HolenAsync(anonym, $"/api/app/registrieren/{token}");
        Assert.Contains(x.Name, info["schueler"]![0]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await anonym.PostAsJsonAsync($"/api/app/registrieren/{token}", new { login = "mutter", passwort = "kurz" })).StatusCode);
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync($"/api/app/registrieren/{token}", new { login = "mutter", passwort = "Eltern-Passwort-1" }));
        var b = s.Dienst<Benutzerdienst>().Alle().Single(k => k.Login == "mutter");
        Assert.Equal(Rollen.Eltern, b.Rolle);
        Assert.Equal(new[] { x.Id }, b.SchuelerIds);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonym.PostAsJsonAsync($"/api/app/registrieren/{token}", new { login = "mutter2", passwort = "Eltern-Passwort-1" })).StatusCode);
        // zweite Anfrage: Hinweis auf das vorhandene Konto statt neuem Link
        await Testserver.JsonAsync(await anonym.PostAsJsonAsync("/api/app/registrieren", new { email = "mutter@example.org" }));
        Assert.Contains("„mutter“", s.Dienst<Maildienst>().Ausgang().First().Text);
    }

    // ------------------------------------------------------------------------------ Oberfläche

    [Fact]
    public void Oberflaeche_Menue_Dialoge_und_Anmeldeseite()
    {
        var app = Datei("js", "app.js");
        Assert.Contains("text: 'Schülergruppen'", app);
        Assert.Contains("text: 'Kontaktdaten'", app);
        // „Offene Stunden pro Klasse“ entfällt: alte Adressen führen zu „Offene Stunden“
        Assert.DoesNotContain("offenklasse.js", app);
        Assert.Contains("history.replaceState(null, '', '#/offen')", app);
        Assert.Throws<FileNotFoundException>(() => Datei("js", "ansichten", "offenklasse.js"));
        Assert.Contains("Registrieren (Eltern und Betriebe)", app);
        Assert.Contains("kennwortVergessen(anmeldeRahmen, anmeldung)", app);
        var sg = Datei("js", "ansichten", "schuelergruppen.js");
        foreach (var t in new[] { "Schüler-Gruppen-Zuordnung", "Auswahlkriterien", "Zuordnungshistorie", "Zugeordnet", "Schülerzahl", "gefunden", "Zurück", "Auswahl umkehren", "Zwischenablage", "Familienname", "Kurzname", "Externe Id" })
            Assert.Contains(t, sg);
        var sb = Datei("js", "ansichten", "stammschueler.js");
        foreach (var t in new[] { "Bild hochladen", "Bildimport", "Import", "Kennwort-Blatt" }) Assert.Contains(t, sb);
        var kd = Datei("js", "ansichten", "dienste.js");
        Assert.Contains("Zur Zeitansicht wechseln", kd);
        Assert.Contains("Anmerkung", kd);
        Assert.Contains("- Auswahl -", kd);
        Assert.Contains("css/klassenleitung.css", Datei("index.html"));
        Assert.Contains("data-adresse", Datei("js", "ansichten", "sitzplan.js"));
    }
}
