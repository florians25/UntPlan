using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using Xunit.Abstractions;

namespace UntPlanStudio.Tests;

/// <summary>Module Einstellungen (Schuldaten, Zeitraster, Ferien, Perioden, Unterrichtsgruppen), Druck/PDF/HTML/iCal, Übersicht, Formate.</summary>
public sealed class DruckEinstellungenTests : IClassFixture<StudioFabrik>
{
    private readonly StudioFabrik _f;
    private readonly ITestOutputHelper _aus;

    public DruckEinstellungenTests(StudioFabrik f, ITestOutputHelper aus)
    {
        _f = f;
        _aus = aus;
    }

    /// <summary>Beispielschule mit fertigem Stundenplan (schnelle Verplanung wie in der Beispieldatei des Studios).</summary>
    private static Schuldatei? _mitPlan;

    /// <summary>Beispielschule mit verplantem Stundenplan (wie „Beispielschule“ im Studio: Optimierung mit festem Startwert).</summary>
    private static Schuldatei MitPlan()
    {
        lock (typeof(DruckEinstellungenTests))
        {
            if (_mitPlan is null)
            {
                var d = Demo.Beispielschule();
                var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { AnzahlPlaene = 1, Schritte = 1, Seed = 1 });
                if (erg.Count > 0) d.AktivePeriode.Plan = erg[0].Plan;
                _mitPlan = d;
            }
            return Dokument.Klonen(_mitPlan);
        }
    }

    private static void IstPdf(byte[] b, int minSeiten = 1)
    {
        Assert.True(b.Length > 400, "PDF zu klein");
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(b, 0, 8));
        var text = Encoding.Latin1.GetString(b);
        Assert.EndsWith("%%EOF\n", text);
        var seiten = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"/Type /Pages /Count (\d+)").Groups[1].Value);
        Assert.True(seiten >= minSeiten, $"{seiten} Seiten statt mindestens {minSeiten}");
        // Querverweistabelle zeigt auf die Objekte
        var start = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[start..]);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text[start..], @"(\d{10}) 00000 n"))
            Assert.Matches(@"^\d+ 0 obj", text.Substring(int.Parse(m.Groups[1].Value), 12));
    }

    [Fact]
    public async Task SchuldatenLesenUndAendern()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var basis = $"/api/dokumente/{id}/schule";
        var s = await c.GetJsonAsync($"{basis}/schuldaten");
        Assert.False(string.IsNullOrEmpty(s["schulname"]!.GetValue<string>()));
        Assert.NotEmpty(s["kriterien"]!.AsArray());
        Assert.Contains(s["schulformen"]!.AsArray(), x => x!["wert"]!.GetValue<string>() == "GY");

        var neu = await (await c.PutAsJsonAsync($"{basis}/schuldaten", new { schulname = "Testschule Nord", adresse = "Hauptstr. 1", vertretungsplantitel = "Änderungen", mehrarbeitsgrenze = 4, vertretungsgewichtung = new Dictionary<string, int> { ["V.Bereitschaft"] = 1 } })).JsonAsync();
        Assert.Equal("Testschule Nord", neu["schulname"]!.GetValue<string>());
        Assert.Equal("Änderungen", neu["vertretungsplantitel"]!.GetValue<string>());
        Assert.Equal(4, neu["mehrarbeitsgrenze"]!.GetValue<int>());
        Assert.Equal(1, neu["vertretungsgewichtung"]!["V.Bereitschaft"]!.GetValue<int>());
        // Grunddaten sehen den neuen Namen, Rückgängig stellt ihn zurück
        Assert.Equal("Testschule Nord", (await c.GetJsonAsync($"/api/dokumente/{id}/grunddaten"))["schulname"]!.GetValue<string>());
        await c.PostJsonAsync($"/api/dokumente/{id}/rueckgaengig");
        Assert.NotEqual("Testschule Nord", (await c.GetJsonAsync($"{basis}/schuldaten"))["schulname"]!.GetValue<string>());

        // ungültig wie im Desktop
        var r = await c.PutAsJsonAsync($"{basis}/schuldaten", new { schuljahrVon = "2026-09-01", schuljahrBis = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Schuljahresende", await r.Content.ReadAsStringAsync());

        // Leser dürfen nicht ändern
        await c.PostJsonAsync("/api/benutzer", new { name = "leser1", anzeigename = "Leser", passwort = "leser-passwort", rolle = "Leser" });
        var leser = await _f.AngemeldetAsync("leser1", "leser-passwort");
        Assert.Equal(HttpStatusCode.Forbidden, (await leser.PutAsJsonAsync($"{basis}/schuldaten", new { schulname = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leser.GetAsync($"{basis}/schuldaten")).StatusCode);
    }

    [Fact]
    public async Task ZeitrasterAendern()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var basis = $"/api/dokumente/{id}/schule/zeitraster";
        var z = await c.GetJsonAsync(basis);
        var stunden = z["stundenProTag"]!.GetValue<int>();
        var zeiten = z["zeiten"]!.AsArray().Select(x => new { nr = x!["nr"]!.GetValue<int>(), beginn = x["beginn"]!.GetValue<string>(), ende = x["ende"]!.GetValue<string>() }).ToList();
        zeiten[0] = new { nr = 1, beginn = "7:50", ende = "08:35" };
        var belegung = z["belegung"]!.AsArray().Select(t => t!.AsArray().Select(x => x!.GetValue<string>()).ToList()).ToList();
        belegung[4][stunden - 1] = ""; // Freitag letzte Stunde leer
        var neu = await (await c.PutAsJsonAsync(basis, new
        {
            tageProWoche = 5, stundenProTag = stunden, ersterSchultag = 0, ersteStundennummer = 1, letzteVormittagsstunde = 6, maxNachmittagsstunden = 4,
            normalstundeMinuten = 45, zeiten, belegung, pausen = new[] { new { nachStunde = 2, bezeichnung = "1. Pause", dauerMinuten = 20, aufsicht = true }, new { nachStunde = 6, bezeichnung = "Mittagspause", dauerMinuten = 45, aufsicht = false } },
            mittagspauseVon = 6, mittagspauseBis = 7, maxKlassenGleicheMittagspause = 0,
        })).JsonAsync();
        Assert.Equal("07:50", neu["zeiten"]![0]!["beginn"]!.GetValue<string>());
        Assert.Equal("", neu["belegung"]![4]![stunden - 1]!.GetValue<string>());
        Assert.Equal(2, neu["pausen"]!.AsArray().Count);
        Assert.Equal("MP", neu["pausen"]![1]!["kurz"]!.GetValue<string>());
        // Grunddaten (Raster der Stundenpläne) folgen
        var g = await c.GetJsonAsync($"/api/dokumente/{id}/grunddaten");
        Assert.Equal("07:50", g["zeitraster"]!["stunden"]![0]!["beginn"]!.GetValue<string>());

        var falsch = await c.PutAsJsonAsync(basis, new { tageProWoche = 9, stundenProTag = 8, zeiten = Array.Empty<object>(), belegung = Array.Empty<object>(), pausen = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, falsch.StatusCode);
        var zeitFalsch = await c.PutAsJsonAsync(basis, new { tageProWoche = 5, stundenProTag = 8, zeiten = new[] { new { nr = 1, beginn = "25:99", ende = "" } }, belegung = Array.Empty<object>(), pausen = Array.Empty<object>() });
        Assert.Contains("Uhrzeit", await zeitFalsch.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FerienPeriodenUndUnterrichtsgruppen()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync();
        var basis = $"/api/dokumente/{id}/schule";

        // Ferien: NRW-Vorschlag, eigener Eintrag, Feld ändern, Tag herausnehmen, löschen
        var vorher = (await c.GetJsonAsync($"{basis}/ferien"))["ferien"]!.AsArray().Count;
        var nrw = await c.PostJsonAsync($"{basis}/ferien/nrw");
        Assert.True(nrw["ferien"]!.AsArray().Count >= vorher);
        Assert.True(nrw["jahresdaten"]!["unterrichtTage"]!.GetValue<int>() > 100);
        var sj = DateOnly.Parse(nrw["schuljahrVon"]!.GetValue<string>());
        var tag = sj.AddDays(70);
        while (tag.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) tag = tag.AddDays(1);
        var mit = await c.PostJsonAsync($"{basis}/ferien", new { kurzname = "Proj", langname = "Projektwoche", von = tag, bis = tag.AddDays(4) });
        var proj = mit["ferien"]!.AsArray().First(f => f!["kurzname"]!.GetValue<string>() == "Proj")!;
        var fid = proj["id"]!.GetValue<int>();
        Assert.Equal(5, proj["tage"]!.GetValue<int>());
        var geaendert = await c.PatchJsonAsync($"{basis}/ferien/{fid}", new { feld = "langname", wert = "Projekttage" });
        Assert.Contains(geaendert["ferien"]!.AsArray(), f => f!["langname"]!.GetValue<string>() == "Projekttage");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PatchAsJsonAsync($"{basis}/ferien/{fid}", new { feld = "von", wert = "99.99." })).StatusCode);
        var geteilt = await c.PostJsonAsync($"{basis}/ferien/tag", new { datum = tag.AddDays(2), unterrichtsfrei = false, feiertag = false });
        Assert.Equal(2, geteilt["ferien"]!.AsArray().Count(f => f!["langname"]!.GetValue<string>() == "Projekttage"));
        var ids = geteilt["ferien"]!.AsArray().Where(f => f!["langname"]!.GetValue<string>() == "Projekttage").Select(f => f!["id"]!.GetValue<int>()).ToArray();
        var weg = await c.PostJsonAsync($"{basis}/ferien/loeschen", new { ids });
        Assert.DoesNotContain(weg["ferien"]!.AsArray(), f => f!["langname"]!.GetValue<string>() == "Projekttage");

        // Perioden: neue Tochterperiode, aktiv setzen, ändern, löschen
        var per = await c.GetJsonAsync($"{basis}/perioden");
        var erste = per["perioden"]![0]!["id"]!.GetValue<int>();
        var mitNeu = await c.PostJsonAsync($"{basis}/perioden", new { mutterId = erste, name = "Halbjahr 2", von = sj.AddDays(150) });
        var neu = mitNeu["perioden"]!.AsArray().First(p => p!["name"]!.GetValue<string>() == "Halbjahr 2")!;
        var pid = neu["id"]!.GetValue<int>();
        Assert.Equal(erste, neu["mutterId"]!.GetValue<int>());
        Assert.True(neu["unterrichte"]!.GetValue<int>() > 0);
        var aktiv = await c.PostJsonAsync($"{basis}/perioden/{pid}/aktiv");
        Assert.Equal(pid, aktiv["aktiv"]!.GetValue<int>());
        Assert.Contains((await c.GetJsonAsync($"/api/dokumente/{id}/grunddaten"))["perioden"]!.AsArray(), p => p!["aktiv"]!.GetValue<bool>() && p["id"]!.GetValue<int>() == pid);
        var fixiert = await (await c.PutAsJsonAsync($"{basis}/perioden/{pid}", new { name = "HJ2", langname = "Zweites Halbjahr", von = sj.AddDays(150), fixiert = true })).JsonAsync();
        Assert.True(fixiert["perioden"]!.AsArray().First(p => p!["id"]!.GetValue<int>() == pid)!["fixiert"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"{basis}/perioden/{pid}")).StatusCode); // fixiert
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"{basis}/perioden", new { mutterId = erste, name = "X", von = sj.AddYears(3) })).StatusCode);
        await c.PutAsJsonAsync($"{basis}/perioden/{pid}", new { name = "HJ2", von = sj.AddDays(150), fixiert = false });
        var nachLoeschen = await (await c.DeleteAsync($"{basis}/perioden/{pid}")).JsonAsync();
        Assert.Equal(erste, nachLoeschen["aktiv"]!.GetValue<int>());

        // Unterrichtsgruppen und Mehrwochen
        var ug = await (await c.PutAsJsonAsync($"{basis}/wochenperiodizitaet", new { wert = 2 })).JsonAsync();
        Assert.Equal(2, ug["wochen"]!.AsArray().Count);
        var mitUg = await c.PostJsonAsync($"{basis}/ugruppen", new { kurzname = "A-Wo", langname = "A-Woche", wochen = "A", unterbrechungen = "01.12.-05.12." });
        var g = mitUg["gruppen"]!.AsArray().Single()!;
        Assert.Equal("A", g["wochen"]!.GetValue<string>());
        Assert.Equal("01.12.-05.12.", g["unterbrechungen"]!.GetValue<string>());
        var faktor = double.Parse(g["faktor"]!.GetValue<string>().Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(faktor, 0.3, 0.7);
        var gid = g["id"]!.GetValue<int>();
        var ae = await (await c.PutAsJsonAsync($"{basis}/ugruppen/{gid}", new { kurzname = "AB", wochen = "AB", fix = true })).JsonAsync();
        Assert.Equal("", ae["gruppen"]![0]!["wochen"]!.GetValue<string>()); // alle Wochen = leer
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"{basis}/ugruppen", new { kurzname = "X", unterbrechungen = "kein datum" })).StatusCode);
        var leer = await c.PostJsonAsync($"{basis}/ugruppen/loeschen", new { ids = new[] { gid } });
        Assert.Empty(leer["gruppen"]!.AsArray());
    }

    [Fact]
    public async Task StundenplaenePdfHtmlUndICal()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync(MitPlan());
        var basis = $"/api/dokumente/{id}/druck";
        var auswahl = await c.GetJsonAsync($"{basis}/auswahl");
        var klassen = auswahl["klasse"]!.AsArray().Select(k => k!["id"]!.GetValue<int>()).ToList();
        Assert.NotEmpty(auswahl["berichte"]!.AsArray());

        var r = await c.GetAsync($"{basis}/stundenplaene?typ=klasse&ids={string.Join(",", klassen.Take(3))}");
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("SAMEORIGIN", r.Headers.GetValues("X-Frame-Options").Single());
        var pdf = await r.Content.ReadAsByteArrayAsync();
        IstPdf(pdf, 3);
        File.WriteAllBytes(Path.Combine(_f.Datenordner, "klassen.pdf"), pdf);

        // kompakt: zwei Pläne je Seite
        var kompakt = await c.GetByteArrayAsync($"{basis}/stundenplaene?typ=lehrer&ids=alle&kompakt=true");
        var lehrer = auswahl["lehrer"]!.AsArray().Count;
        IstPdf(kompakt, (lehrer + 1) / 2);
        IstPdf(await c.GetByteArrayAsync($"{basis}/stundenplaene?typ=raum&farbig=true"));

        var html = await c.GetStringAsync($"{basis}/stundenplaene?typ=klasse&ids={klassen[0]}&ausgabe=html");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<table class=\"tt\">", html);

        // Info-Stundenplan: Seiten einzeln und als ZIP
        var index = await c.GetStringAsync($"{basis}/info/index.html");
        var link = System.Text.RegularExpressions.Regex.Match(index, "href=\"(kl_[^\"]+)\"").Groups[1].Value;
        Assert.Contains("<table class=\"tt\">", await c.GetStringAsync($"{basis}/info/{link}"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{basis}/info/gibtsnicht.html")).StatusCode);
        using (var zip = new ZipArchive(new MemoryStream(await c.GetByteArrayAsync($"{basis}/infostundenplan.zip"))))
        {
            Assert.Contains(zip.Entries, e => e.Name == "index.html");
            Assert.Contains(zip.Entries, e => e.Name == link);
        }

        // iCalendar einer Lehrkraft und aller als ZIP
        var lid = auswahl["lehrer"]![0]!["id"]!.GetValue<int>();
        var sj = DateOnly.Parse(auswahl["schuljahrVon"]!.GetValue<string>());
        var ics = await c.GetAsync($"{basis}/ical?lehrer={lid}&von={sj.AddDays(14):yyyy-MM-dd}&bis={sj.AddDays(27):yyyy-MM-dd}");
        Assert.Equal("text/calendar", ics.Content.Headers.ContentType!.MediaType);
        var ical = await ics.Content.ReadAsStringAsync();
        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0", ical);
        Assert.EndsWith("END:VCALENDAR\r\n", ical);
        Assert.Contains("BEGIN:VEVENT", ical);
        Assert.Equal(ical.Split("BEGIN:VEVENT").Length, ical.Split("END:VEVENT").Length);
        using (var zip = new ZipArchive(new MemoryStream(await c.GetByteArrayAsync($"{basis}/ical?von={sj:yyyy-MM-dd}&bis={sj.AddDays(6):yyyy-MM-dd}"))))
            Assert.Equal(lehrer, zip.Entries.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"{basis}/ical?lehrer={lid}&von=2026-10-10&bis=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{basis}/stundenplaene?typ=klasse&format=Gibtsnicht")).StatusCode);
    }

    [Fact]
    public async Task VertretungsplanAushangUndBerichte()
    {
        var c = await _f.AngemeldetAsync();
        var d = MitPlan();
        var id = await c.BeispielHochladenAsync(d);
        var basis = $"/api/dokumente/{id}/druck";

        // Seitenlayout lesen, ändern (bleibt in der Schuldatei, Desktop-kompatibel)
        var aushang = await c.GetJsonAsync($"{basis}/aushang");
        var layout = aushang["layout"]!.AsObject();
        Assert.Equal(11, layout["felder"]!.AsArray().Count);
        layout["raster"] = true;
        layout["querformat"] = true;
        layout["gruppierung"] = "Stunde";
        layout["ueberschrift"] = "Aushang";
        var neu = await (await c.PutAsJsonAsync($"{basis}/aushang", layout)).JsonAsync();
        Assert.True(neu["layout"]!["raster"]!.GetValue<bool>());
        var datei = await c.GetByteArrayAsync($"/api/dokumente/{id}/herunterladen");
        Assert.Contains("\"Vertretungsdruck\"", Encoding.UTF8.GetString(datei));

        var tag = d.Einstellungen.SchuljahrVon.AddDays(21);
        while (!Kalender.IstUnterrichtstag(d, tag)) tag = tag.AddDays(1);
        // eine Vertretung am Tag (manuell, wie im Vertretungsfenster angelegt)
        var u = d.Unterrichte.First(x => x.Zeilen[0].LehrerId.HasValue && x.Zeilen[0].KlassenIds.Count > 0);
        var vertreter = d.Lehrer.First(l => l.Id != u.Zeilen[0].LehrerId);
        var r0 = await c.GetAsync($"{basis}/vertretungsplan?von={tag:yyyy-MM-dd}&version=lehrer");
        Assert.Equal("application/pdf", r0.Content.Headers.ContentType!.MediaType);
        Assert.All(r0.Content.Headers.GetValues("Content-Disposition").Single().Split("filename*")[0], ch => Assert.True(ch < 128, "Dateiname im Kopf nur ASCII"));
        var mitVertretung = Dokument.Klonen(d);
        mitVertretung.Vertretungen.Add(new Vertretung { Id = mitVertretung.NeueId(), Datum = tag, Stunde = 2, Art = VertretungsArt.Vertretung, Quelle = VertretungsQuelle.Manuell,
            UnterrichtId = u.Id, LehrerId = u.Zeilen[0].LehrerId, VertreterId = vertreter.Id, FachId = u.Zeilen[0].FachId, KlassenIds = u.Zeilen[0].KlassenIds.ToList(), Text = "Smoketext" });
        var id2 = await c.BeispielHochladenAsync(mitVertretung);
        var html2 = await c.GetStringAsync($"/api/dokumente/{id2}/druck/vertretungsplan?von={tag:yyyy-MM-dd}&ausgabe=html&version=lehrer");
        Assert.Contains("Smoketext", html2);
        Assert.Contains(vertreter.Kurzname, html2);
        IstPdf(await c.GetByteArrayAsync($"/api/dokumente/{id2}/druck/vertretungsplan?von={tag:yyyy-MM-dd}&version=lehrer"));
        var vt = await c.GetJsonAsync($"/api/dokumente/{id2}/druck/bericht/vertretungen?von={tag:yyyy-MM-dd}&bis={tag:yyyy-MM-dd}&ausgabe=json");
        Assert.Contains("Smoketext", vt["zeilen"]!.ToJsonString());
        var pdf = await c.GetByteArrayAsync($"{basis}/vertretungsplan?von={tag:yyyy-MM-dd}&bis={tag.AddDays(6):yyyy-MM-dd}&version=lehrer");
        IstPdf(pdf, 3);
        var html = await c.GetStringAsync($"{basis}/vertretungsplan?von={tag:yyyy-MM-dd}&ausgabe=html");
        Assert.Contains("<table>", html);

        // Übersichtsplan
        IstPdf(await c.GetByteArrayAsync($"{basis}/uebersicht?typ=lehrer"));

        // alle Berichte als PDF, CSV und JSON
        var berichte = await c.GetJsonAsync($"{basis}/berichte");
        foreach (var b in berichte.AsArray())
        {
            var bid = b!["id"]!.GetValue<string>();
            var zeitraum = $"von={tag:yyyy-MM-dd}&bis={tag.AddDays(13):yyyy-MM-dd}";
            IstPdf(await c.GetByteArrayAsync($"{basis}/bericht/{bid}?{zeitraum}"));
            var json = await c.GetJsonAsync($"{basis}/bericht/{bid}?{zeitraum}&ausgabe=json");
            Assert.NotEmpty(json["spalten"]!.AsArray());
            var csv = await c.GetByteArrayAsync($"{basis}/bericht/{bid}?{zeitraum}&ausgabe=csv");
            Assert.True(csv.Length > 10);
        }
        var soll = await c.GetJsonAsync($"{basis}/bericht/lehrer-sollist?ausgabe=json");
        Assert.Equal(d.Lehrer.Count + 1, soll["zeilen"]!.AsArray().Count);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{basis}/bericht/gibtsnicht")).StatusCode);
    }

    [Fact]
    public async Task UebersichtUndRaumverlegung()
    {
        var c = await _f.AngemeldetAsync();
        var d = MitPlan();
        var id = await c.BeispielHochladenAsync(d);
        var u = await c.GetJsonAsync($"/api/dokumente/{id}/uebersicht/lehrer");
        Assert.Equal(d.Lehrer.Count, u["elemente"]!.AsArray().Count);
        Assert.Equal(d.Lehrer.Count, u["zellen"]!.AsArray().Count);
        var summe = u["zellen"]!.AsArray().Sum(z => z!.AsArray().Count);
        Assert.True(summe > 0);
        var mitFach = await c.GetJsonAsync($"/api/dokumente/{id}/uebersicht/klasse?inhalt=fach");
        Assert.Equal("Fach", mitFach["inhalt"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/dokumente/{id}/uebersicht/gang")).StatusCode);

        // Raumverlegung: erste Stunde mit Raum in einen freien Raum
        var raeume = await c.GetJsonAsync($"/api/dokumente/{id}/uebersicht/raum");
        var el = raeume["elemente"]!.AsArray();
        var zellen = raeume["zellen"]!.AsArray();
        var i = Enumerable.Range(0, el.Count).First(k => zellen[k]!.AsArray().Count > 0);
        var zelle = zellen[i]![0]!.AsArray();
        var (tag, stunde, uid) = (zelle[0]!.GetValue<int>(), zelle[1]!.GetValue<int>(), zelle[2]!.GetValue<int>());
        var ziel = Enumerable.Range(0, el.Count).First(k => k != i && !zellen[k]!.AsArray().Any(z => z![0]!.GetValue<int>() == tag && z[1]!.GetValue<int>() == stunde));
        await c.PostJsonAsync($"/api/dokumente/{id}/uebersicht/raum", new { unterricht = uid, tag, stunde, alterRaum = el[i]!["id"]!.GetValue<int>(), neuerRaum = el[ziel]!["id"]!.GetValue<int>() });
        var nach = await c.GetJsonAsync($"/api/dokumente/{id}/uebersicht/raum");
        Assert.Contains(nach["zellen"]![ziel]!.AsArray(), z => z![2]!.GetValue<int>() == uid && z[0]!.GetValue<int>() == tag && z[1]!.GetValue<int>() == stunde);
    }

    [Fact]
    public async Task FormateAnlegenUndDrucken()
    {
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync(MitPlan());
        var basis = $"/api/dokumente/{id}/formate";
        var liste = await c.GetJsonAsync(basis);
        Assert.Empty(liste["formate"]!.AsArray());
        Assert.NotEmpty(liste["vorlagen"]!.AsArray());
        Assert.Contains(liste["grundformen"]!.AsArray(), g => g!["wert"]!.GetValue<string>() == "Format30");

        var kopie = await c.PostJsonAsync($"{basis}/kopieren", new { name = "Klassenplan", neu = "Klasse Vormittag" });
        var f = kopie["formate"]![0]!.AsObject();
        f["bisStunde"] = 6;
        f["einzelstunden"] = true;
        f["felder"] = new JsonArray(new JsonObject { ["art"] = "Fach", ["zeile"] = 1, ["x"] = 0, ["y"] = 0, ["b"] = 0.5, ["h"] = 0.5, ["fett"] = true });
        var gespeichert = await (await c.PutAsJsonAsync($"{basis}/{Uri.EscapeDataString("Klasse Vormittag")}", f)).JsonAsync();
        var g2 = gespeichert["formate"]![0]!;
        Assert.Equal(6, g2["bisStunde"]!.GetValue<int>());
        Assert.Equal("Fach", g2["felder"]![0]!["art"]!.GetValue<string>());
        f["bisStunde"] = 1; f["vonStunde"] = 3;
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"{basis}/neu", f)).StatusCode);

        // Druck mit dem Format
        var pdf = await c.GetByteArrayAsync($"/api/dokumente/{id}/druck/stundenplaene?typ=klasse&format={Uri.EscapeDataString("Klasse Vormittag")}");
        IstPdf(pdf);
        Assert.Contains("Klasse Vormittag", (await c.GetJsonAsync($"/api/dokumente/{id}/druck/auswahl"))["formate"]!.ToJsonString());

        var leer = await (await c.DeleteAsync($"{basis}/{Uri.EscapeDataString("Klasse Vormittag")}")).JsonAsync();
        Assert.Empty(leer["formate"]!.AsArray());
    }

    [Fact]
    public async Task GrosseSchuleDruckUndUebersichtBleibenSchnell()
    {
        var d = Demo.NeueDatei("Große Schule", "GY");
        for (var i = 0; i < 150; i++) d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = $"L{i:000}", Langname = $"Lehrkraft {i:000}", Vorname = "V" });
        for (var i = 0; i < 80; i++)
        {
            var raum = new Raum { Id = d.NeueId(), Kurzname = $"R{i:000}" };
            d.Raeume.Add(raum);
            d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = $"K{i:00}", StammraumId = raum.Id });
        }
        var faecher = d.Faecher.Where(f => !f.Bereitschaft).Take(10).ToList();
        for (var k = 0; k < 80; k++)
            for (var j = 0; j < 40; j++)
            {
                var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = d.Lehrer[(k + j) % 150].Id, FachId = faecher[j % 10].Id, KlassenIds = { d.Klassen[k].Id } } } };
                d.Unterrichte.Add(u);
                d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = j % 5, Stunde = 1 + (j / 5) % 8, Raeume = { d.Klassen[k].StammraumId } });
            }
        var c = await _f.AngemeldetAsync();
        var id = await c.BeispielHochladenAsync(d);
        await c.GetAsync($"/api/dokumente/{id}/uebersicht/lehrer"); // aufwärmen
        var sw = Stopwatch.StartNew();
        var u1 = await c.GetJsonAsync($"/api/dokumente/{id}/uebersicht/lehrer");
        var uebersicht = sw.ElapsedMilliseconds;
        Assert.Equal(3200, u1["zellen"]!.AsArray().Sum(z => z!.AsArray().Count));
        sw.Restart();
        var pdf = await c.GetByteArrayAsync($"/api/dokumente/{id}/druck/stundenplaene?typ=klasse");
        var druck = sw.ElapsedMilliseconds;
        sw.Restart();
        var updf = await c.GetByteArrayAsync($"/api/dokumente/{id}/druck/uebersicht?typ=lehrer");
        var uebersichtPdf = sw.ElapsedMilliseconds;
        _aus.WriteLine($"Übersicht {uebersicht} ms, 80 Klassenpläne PDF {druck} ms ({pdf.Length / 1024} KB), Übersicht PDF {uebersichtPdf} ms");
        IstPdf(pdf, 80);
        IstPdf(updf, 2);
        Assert.True(uebersicht < 1500, $"Übersicht {uebersicht} ms");
        Assert.True(druck < 15000, $"Druck {druck} ms");
    }
}
