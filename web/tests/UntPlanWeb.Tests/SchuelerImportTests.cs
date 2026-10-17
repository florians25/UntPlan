using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Schüler-Import: CSV-Parser (Kodierung, Trennzeichen, Anführungszeichen), Werte, Zuordnung, Abgleich, Trockenlauf, Übernahme und Quelle der Schülerdaten.</summary>
public class SchuelerImportTests
{
    [Fact]
    public void Kodierung_und_Trennzeichen_werden_erkannt()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(1252).GetBytes("Nachname;Vorname\nMüller;Jürgen\n");
        var t = SchuelerImport.Lesen(ansi);
        Assert.Equal("Windows-1252", t.Kodierung);
        Assert.Equal(";", t.Trennzeichen);
        Assert.Equal("Müller", t.Zeilen[1][0]);

        var utf8 = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Name,Vorname,Klasse\r\n\"Groß, Anna\",\"Lisa \"\"Lilly\"\"\",5a\r\n")).ToArray();
        t = SchuelerImport.Lesen(utf8);
        Assert.Equal("UTF-8", t.Kodierung);
        Assert.Equal(",", t.Trennzeichen);
        Assert.Equal("Groß, Anna", t.Zeilen[1][0]);
        Assert.Equal("Lisa \"Lilly\"", t.Zeilen[1][1]);

        t = SchuelerImport.Lesen(Encoding.UTF8.GetBytes("Name\tVorname\tBemerkung\nMeier\tTom\t\"zwei\nZeilen\"\n\nKern\tIda\t\n"));
        Assert.Equal("tab", t.Trennzeichen);
        Assert.Equal(3, t.Zeilen.Count);
        Assert.Equal("zwei\nZeilen", t.Zeilen[1][2]);
        // vorgegebenes Trennzeichen
        Assert.Equal(new[] { "a;b", "c" }, SchuelerImport.Lesen(Encoding.UTF8.GetBytes("a;b,c\n"), trennzeichen: ",").Zeilen[0]);
        Assert.Throws<ArgumentException>(() => SchuelerImport.Lesen(Array.Empty<byte>()));
    }

    [Fact]
    public void Werte_und_Zuordnungsvorschlag()
    {
        Assert.Equal("2012-03-05", SchuelerImport.Datum("5.3.2012"));
        Assert.Equal("2012-03-05", SchuelerImport.Datum("2012-03-05"));
        Assert.Equal("2012-03-05", SchuelerImport.Datum("05.03.2012 00:00"));
        Assert.Equal("", SchuelerImport.Datum(" "));
        Assert.Null(SchuelerImport.Datum("31.02.2012"));
        Assert.Equal("w", SchuelerImport.Geschlecht("weiblich"));
        Assert.Equal("m", SchuelerImport.Geschlecht("M"));
        Assert.Equal("d", SchuelerImport.Geschlecht("divers"));
        Assert.Null(SchuelerImport.Geschlecht("?"));
        Assert.Equal(("Maria", "Muster"), SchuelerImport.Personenname("Muster, Maria", ""));
        Assert.Equal(("Maria Anna", "Muster"), SchuelerImport.Personenname("Maria Anna Muster", ""));
        Assert.Equal(("Eva", "Kern"), SchuelerImport.Personenname("Kern", "Eva"));
        var v = SchuelerImport.Vorschlag(new[] { "Familienname", "Rufname", "Geb.-Datum", "Klasse", "E-Mail", "Schülernummer", "EB1 Name", "EB1 E-Mail", "Unbekannt" });
        Assert.Equal("nachname", v["0"]);
        Assert.Equal("vorname", v["1"]);
        Assert.Equal("klasse", v["3"]);
        Assert.Equal("email", v["4"]);
        Assert.Equal("extern", v["5"]);
        Assert.Equal("eb1_name", v["6"]);
        Assert.Equal("eb1_email", v["7"]);
        Assert.False(v.ContainsKey("8"));
        Assert.True(SchuelerImport.KopfzeileVermuten(new() { new() { "Nachname", "Vorname" }, new() { "Meier", "Tom" } }));
    }

    private static async Task<JsonNode> LesenAsync(HttpClient c, string csv)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "datei", "schueler.csv" } };
        return await Testserver.JsonAsync(await c.PostAsync("/api/app/admin/schuelerimport/lesen", form));
    }

    private static object Anfrage(JsonNode gelesen, object optionen) => new
    {
        zeilen = gelesen["zeilen"], kopfzeile = gelesen["kopfzeile"]!.GetValue<bool>(), zuordnung = gelesen["vorschlag"], optionen,
    };

    [Fact]
    public async Task Trockenlauf_Uebernahme_Abgleich_und_Austritt()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/schuelerimport/grundlagen")).StatusCode);

        var p = s.Dienst<Paketdienst>();
        var vorher = p.Aktuell!.Schueler.ToList();
        var bekannt = vorher[0];
        var klasse = bekannt.Klasse;
        var csv = $"""
            Nachname;Vorname;Geburtsdatum;Geschlecht;Klasse;Schülernummer;E-Mail;EB1 Name;EB1 E-Mail;EB1 Telefon
            {bekannt.Name};{bekannt.Vorname};01.02.2014;w;{klasse};S-1;;;;
            Neumann;Nora;3.4.2014;weiblich;{klasse};S-2;nora@example.org;"Neumann, Petra";petra@example.org;0123 456
            Fehler;Fritz;31.02.2014;m;{klasse};S-3;;;;
            Klassenlos;Karl;;m;ZZ9;S-4;;;;
            """;
        var g = await LesenAsync(admin, csv);
        Assert.True(g["kopfzeile"]!.GetValue<bool>());
        Assert.Equal(";", g["trennzeichen"]!.GetValue<string>());
        Assert.Equal(4, g["anzahl"]!.GetValue<int>());
        Assert.Equal("eb1_telefon", g["vorschlag"]!["9"]!.GetValue<string>());

        // Trockenlauf speichert nichts
        var t = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/pruefen", Anfrage(g, new { modus = "aktualisieren", schluessel = "auto" })));
        Assert.Equal(1, t["neu"]!.GetValue<int>());
        Assert.Equal(1, t["geaendert"]!.GetValue<int>());
        Assert.Equal(2, t["fehler"]!.GetValue<int>());
        var zeilen = t["zeilen"]!.AsArray();
        Assert.Contains("Ungültiges Datum", zeilen.First(z => z!["zeile"]!.GetValue<int>() == 4)!["fehler"]!.GetValue<string>());
        Assert.Contains("Unbekannte Klasse", zeilen.First(z => z!["zeile"]!.GetValue<int>() == 5)!["fehler"]!.GetValue<string>());
        Assert.Equal(bekannt.Id, zeilen.First(z => z!["status"]!.GetValue<string>() == "geaendert")!["id"]!.GetValue<int>());
        Assert.Equal(vorher.Count, p.Aktuell!.Schueler.Count);

        // Übernehmen mit Konten für Schüler*innen und Eltern
        var u = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/uebernehmen",
            Anfrage(g, new { modus = "aktualisieren", schluessel = "auto", kontenSchueler = true, kontenEltern = true })));
        Assert.Equal(vorher.Count + 1, p.Aktuell!.Schueler.Count);
        var nora = p.Aktuell!.Schueler.Single(x => x.Name == "Neumann");
        Assert.True(nora.Id >= Schuelerverwaltung.ErsteEigeneId);
        var konten = u["konten"]!.AsArray();
        Assert.Contains(konten, k => k!["rolle"]!.GetValue<string>() == "eltern");
        Assert.Contains(konten, k => k!["rolle"]!.GetValue<string>() == "schueler" && k["klasse"]!.GetValue<string>() == klasse);
        Assert.Contains("Startpasswort", u["csv"]!.GetValue<string>());
        var petra = s.Dienst<Personendienst>().Liste(Personendienst.Eltern).Single(x => x.Name == "Neumann");
        Assert.Equal(new[] { nora.Id }, petra.Kinder);
        Assert.NotNull(petra.Konto);
        Assert.Contains(nora.Id, s.Dienst<Benutzerdienst>().Holen(petra.Konto!.Id)!.SchuelerIds);
        var details = s.Dienst<Schuelerverwaltung>().Liste(s.Dienst<Benutzerdienst>().NachLogin("admin")!, p.Aktuell!, klasse).Single(x => x.Id == bekannt.Id);
        Assert.Equal("2014-02-01", details.Geburtsdatum);
        Assert.Equal("w", details.Geschlecht);

        // zweiter Lauf: unverändert (Abgleich über die externe Nummer), Modus „nur neue“ überspringt Vorhandene
        t = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/pruefen", Anfrage(g, new { modus = "aktualisieren" })));
        Assert.Equal(2, t["unveraendert"]!.GetValue<int>());
        Assert.Equal(0, t["neu"]!.GetValue<int>());
        t = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/pruefen", Anfrage(g, new { modus = "neu" })));
        Assert.Equal(2, t["uebersprungen"]!.GetValue<int>());

        // Abgleich über Name + Geburtsdatum: Klassenwechsel, nicht enthaltene als ausgetreten markieren
        var andere = p.Aktuell!.Klassen.Keys.First(k => k != klasse);
        var g2 = await LesenAsync(admin, $"Nachname,Vorname,Geburtsdatum,Klasse\nNeumann,Nora,2014-04-03,{andere}\n");
        t = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/pruefen", Anfrage(g2, new { modus = "aktualisieren", schluessel = "name_geburt", austrittMarkieren = true })));
        Assert.Equal(1, t["geaendert"]!.GetValue<int>());
        Assert.Equal(vorher.Count, t["ausgetreten"]!.GetValue<int>());
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/uebernehmen", Anfrage(g2, new { modus = "aktualisieren", schluessel = "name_geburt", austrittMarkieren = true })));
        Assert.Equal(nora.Id, Assert.Single(p.Aktuell!.Schueler).Id);
        Assert.Equal(andere, p.Aktuell!.Schueler[0].Klasse);
    }

    [Fact]
    public async Task Quelle_Web_Verwaltung_schuetzt_vor_dem_Export()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var p = s.Dienst<Paketdienst>();
        var vorher = p.Aktuell!.Schueler.Select(x => x.Id).ToList();
        Assert.Equal("export", (await Testserver.HolenAsync(admin, "/api/app/admin/schuelerimport/quelle"))["quelle"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/admin/schuelerimport/quelle", new { quelle = "irgendwas" })).StatusCode);

        var r = await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/schuelerimport/quelle", new { quelle = "web" }));
        Assert.Equal("web", r["quelle"]!.GetValue<string>());
        Assert.Equal(vorher.Count, r["uebernommen"]!.GetValue<int>());

        // neuer Export ohne Schüler*innen: in der Web-Verwaltung bleiben sie erhalten
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 99;
        paket["elemente"]!["schueler"] = new JsonArray();
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        Assert.Equal(vorher, p.Aktuell!.Schueler.Select(x => x.Id).ToList());

        // zurück zum Export: die Liste aus dem Export gilt wieder (dort sind jetzt keine Schüler*innen)
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/schuelerimport/quelle", new { quelle = "export" }));
        Assert.Empty(p.Aktuell!.Schueler);
        Assert.False(SchuelerImport.WebVerwaltung(s.Dienst<UntPlanWeb.Daten.Db>()));
    }

    [Fact]
    public async Task Vorlagen_speichern_und_loeschen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/schuelerimport/vorlagen", new
        {
            name = "Schulverwaltung", kopfzeile = true, ueberschriften = new Dictionary<string, string> { ["Familienname"] = "nachname", ["X"] = "kein-feld" },
            spalten = new Dictionary<string, string> { ["0"] = "nachname" },
        }));
        var g = await Testserver.HolenAsync(admin, "/api/app/admin/schuelerimport/grundlagen");
        var v = Assert.Single(g["vorlagen"]!.AsArray())!;
        Assert.Equal("Schulverwaltung", v["name"]!.GetValue<string>());
        Assert.Single(v["ueberschriften"]!.AsObject());
        await Testserver.JsonAsync(await admin.DeleteAsync("/api/app/admin/schuelerimport/vorlagen/Schulverwaltung"));
        Assert.Empty((await Testserver.HolenAsync(admin, "/api/app/admin/schuelerimport/grundlagen"))["vorlagen"]!.AsArray());
    }
}
