using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Termin-Modul (Menü „Buchen“): Neue Aktivität mit Zusatzangaben, mehreren Lehrkräften und Räumen, Reservierung und Bestätigung durch die
/// Raumadministration, Buchung bearbeiten (Details, Schülergruppe), zusätzliche Stunden zu Unterricht aus UntPlan bzw. zu Buchungen mit
/// Lehrkraft-/Klassenkonflikten, Terminsuche (Verfügbarkeit, Feiertage) und Buchungsbeschränkungen. Feste Uhr: Mittwoch, 14.10.2026.
/// </summary>
public class TerminModulTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    private static object Aktivitaet(string datum, int von, int bis, string? raum, object? extra = null)
    {
        var d = new Dictionary<string, object?> { ["art"] = "aktivitaet", ["termine"] = new[] { new { datum, stundeVon = von, stundeBis = bis, raum } } };
        if (extra != null) foreach (var p in extra.GetType().GetProperties()) d[p.Name] = p.GetValue(extra);
        return d;
    }

    [Fact]
    public async Task Neue_Aktivitaet_mit_Zusatzangaben_weiteren_Lehrkraeften_und_Raeumen_erscheint_in_allen_Plaenen()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 3, 3, "R108", new
        {
            klassen = new[] { "7a" }, lehrer = "LAN", weitereLehrer = new[] { "SNE" }, weitereRaeume = new[] { "R109" }, fach = "D", titel = "Projektarbeit", text = "Projektarbeit",
            zusatz = new { abteilung = "Abteilung 1", verantwortlich = "LAN", aktivitaetsart = "Unterricht", teilnehmer = 24, kostenstelle = "K1", menge = 2, email = "a@b.de", emailBenachrichtigung = true, anmerkung = "intern" },
        })));
        var b = antwort["buchungen"]!.AsArray().Single()!;
        Assert.Equal("Bestätigt", b["statusText"]!.GetValue<string>());
        Assert.Equal(new[] { "LAN", "SNE" }, b["alleLehrer"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(new[] { "R108", "R109" }, b["alleRaeume"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(24, b["zusatz"]!["teilnehmer"]!.GetValue<int>());
        Assert.Equal("intern", b["zusatz"]!["anmerkung"]!.GetValue<string>());

        // Lehrer- und Raumpläne aller Beteiligten
        foreach (var (typ, element) in new[] { ("lehrer", "SNE"), ("raum", "R109"), ("raum", "R108"), ("klasse", "7a") })
        {
            var plan = await Testserver.HolenAsync(lan, $"/api/app/plan?typ={typ}&element={element}&datum=2026-10-15");
            Assert.Contains(plan["karten"]!.AsArray(), k => k!["datum"]!.GetValue<string>() == "2026-10-15" && k["text"]?.GetValue<string>() == "Projektarbeit");
        }
        // weiterer Raum ist belegt; „Meine Buchungen“ der weiteren Lehrkraft enthält die Buchung
        var zweite = await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 3, 3, "R109"));
        Assert.Equal(HttpStatusCode.BadRequest, zweite.StatusCode);
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        var liste = await Testserver.HolenAsync(sne, "/api/app/buchen/liste?von=2026-10-12&bis=2026-10-18");
        Assert.Contains(liste.AsArray(), x => x!["id"]!.GetValue<int>() == b["id"]!.GetValue<int>());
        // ungültige E-Mail-Adresse
        var r = await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 5, 5, null, new { zusatz = new { email = "keine mail" } }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Raum_nur_reservieren_braucht_Bestaetigung_der_Raumadministration_vor_der_Uebertragung()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/buchen/raumdaten/R108", new { kapazitaet = 30, gebaeude = "", abteilung = "", raumgruppe = "", nurReservieren = true }));
        var opt = await Testserver.HolenAsync(lan, "/api/app/buchen/optionen");
        Assert.True(opt["raeume"]!.AsArray().Single(x => x!["kurz"]!.GetValue<string>() == "R108")!["nurReservieren"]!.GetValue<bool>());
        // Kapazität ändern lässt „nur reservieren“ unverändert
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/buchen/raumdaten/R108", new { kapazitaet = 31, gebaeude = "", abteilung = "", raumgruppe = "" }));

        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 3, 3, "R108", new { titel = "Reserviert", zusatz = new { emailBenachrichtigung = true } })));
        var b = antwort["buchungen"]!.AsArray().Single()!;
        var id = b["id"]!.GetValue<int>();
        Assert.Equal("Nicht bestätigt", b["statusText"]!.GetValue<string>());
        Assert.Contains(antwort["hinweise"]!.AsArray(), h => h!.GetValue<string>().Contains("Raumadministration"));
        Assert.DoesNotContain(s.Dienst<Buchungsdienst>().Rueckmeldungen(), x => x.Id == $"buchung-{id}");
        // der Raum ist trotzdem belegt
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 3, 3, "R108"))).StatusCode);

        // nur die Raumadministration bestätigt; danach Übertragung an UntPlan und Mitteilung an die buchende Person
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PostAsJsonAsync($"/api/app/buchen/{id}/bestaetigen", new { })).StatusCode);
        var x = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/buchen/{id}/bestaetigen?serie=true", new { }));
        Assert.Equal(1, x["bestaetigt"]!.GetValue<int>());
        var d = await Testserver.HolenAsync(lan, $"/api/app/buchen/{id}/details");
        Assert.Equal("Bestätigt", d["buchung"]!["statusText"]!.GetValue<string>());
        Assert.Contains(s.Dienst<Buchungsdienst>().Rueckmeldungen(), y => y.Id == $"buchung-{id}");

        // Administration bucht sofort; „Buchung sofort bestätigen“ abgewählt → Reservierung
        var frei = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-15", 5, 5, "R108")));
        Assert.Equal("Bestätigt", frei["buchungen"]!.AsArray().Single()!["statusText"]!.GetValue<string>());
        var res = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-16", 6, 6, null, new { sofortBestaetigen = false })));
        Assert.Equal("Nicht bestätigt", res["buchungen"]!.AsArray().Single()!["statusText"]!.GetValue<string>());
    }

    [Fact]
    public async Task Buchung_bearbeiten_aendert_Details_der_Serie_und_die_Schuelergruppe()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var d = new Dictionary<string, object?>
        {
            ["art"] = "aktivitaet", ["klassen"] = new[] { "7a" }, ["titel"] = "AG",
            ["termine"] = new[] { new { datum = "2026-10-15", stundeVon = 7, stundeBis = 7, raum = (string?)null }, new { datum = "2026-11-05", stundeVon = 7, stundeBis = 7, raum = (string?)null } },
        };
        var antwort = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", d));
        var id = antwort["buchungen"]!.AsArray()[0]!["id"]!.GetValue<int>();
        var geaendert = await Testserver.JsonAsync(await lan.PutAsJsonAsync($"/api/app/buchen/{id}", new { titel = "Theater-AG", fach = "D", klassen = new[] { "7a", "7b" }, zusatz = new { teilnehmer = 12 }, serie = true }));
        Assert.Equal(2, geaendert.AsArray().Count);
        Assert.All(geaendert.AsArray(), x => { Assert.Equal("Theater-AG", x!["titel"]!.GetValue<string>()); Assert.Equal("LAN", x["geaendertVon"]!.GetValue<string>()); });

        var details = await Testserver.HolenAsync(lan, $"/api/app/buchen/{id}/details");
        Assert.Equal(2, details["termine"]!.AsArray().Count);
        var schueler = details["schueler"]!.AsArray();
        Assert.NotEmpty(schueler);
        Assert.All(schueler, x => Assert.Contains(x!["klasse"]!.GetValue<string>(), new[] { "7a", "7b" }));
        var ids = schueler.Take(2).Select(x => x!["id"]!.GetValue<int>()).ToArray();
        var gesetzt = await Testserver.JsonAsync(await lan.PutAsJsonAsync($"/api/app/buchen/{id}/schueler", new { ids = ids.Append(999999).ToArray() }));
        Assert.Equal(ids, gesetzt["schueler"]!.AsArray().Select(x => x!.GetValue<int>()));
        var liste = await Testserver.HolenAsync(lan, "/api/app/buchen/liste?von=2026-10-01&bis=2026-11-30");
        Assert.All(liste.AsArray(), x => Assert.Equal(2, x!["schueler"]!.GetValue<int>()));

        // fremde Buchung: nicht bearbeiten
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.NotEqual(HttpStatusCode.OK, (await sne.PutAsJsonAsync($"/api/app/buchen/{id}", new { titel = "X" })).StatusCode);
    }

    [Fact]
    public async Task Zusaetzliche_Stunden_zu_Unterricht_und_Buchung_mit_Lehrkraft_und_Klassenkonflikten()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        // eine Stunde, in der LAN laut Plan unterrichtet
        var plan = await Testserver.HolenAsync(lan, "/api/app/plan?typ=lehrer&element=LAN&datum=2026-10-15");
        var k = plan["karten"]!.AsArray().First(x => x!["datum"]!.GetValue<string>() == "2026-10-15" && x["unterricht"] is not null && x["status"]!.GetValue<string>() == "regulaer")!;
        var nr = k["unterricht"]!.GetValue<int>();
        var stunde = k["von"]!.GetValue<int>();
        var quelle = await Testserver.HolenAsync(lan, $"/api/app/buchen/stundenquelle?u={nr}");
        Assert.Contains("LAN", quelle["lehrer"]!.AsArray().Select(x => x!.GetValue<string>()));

        object Zusatz(bool zulassen) => new
        {
            art = "unterricht", planUnterricht = nr, termine = new[] { new { datum = "2026-10-15", stundeVon = stunde, stundeBis = stunde, raum = (string?)null } },
            lehrerKonflikteZulassen = zulassen, klassenKonflikteZulassen = zulassen,
        };
        var r = await lan.PostAsJsonAsync("/api/app/buchen", Zusatz(false));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Konflikt nicht zugelassen", await r.Content.ReadAsStringAsync());
        var ok = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Zusatz(true)));
        var b = ok["buchungen"]!.AsArray().Single()!;
        Assert.Equal("unterricht", b["art"]!.GetValue<string>());
        Assert.Equal(nr, b["planUnterricht"]!.GetValue<int>());
        Assert.Equal("LAN", b["lehrer"]!.GetValue<string>());

        // zusätzliche Stunde zu einer Aktivität übernimmt deren Angaben
        var akt = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Aktivitaet("2026-10-16", 8, 8, null, new { klassen = new[] { "7b" }, titel = "Förderkurs", fach = "M" })));
        var aid = akt["buchungen"]!.AsArray().Single()!["id"]!.GetValue<int>();
        var neu = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "aktivitaet", quellBuchung = aid, termine = new[] { new { datum = "2026-11-06", stundeVon = 8, stundeBis = 8, raum = (string?)null } },
            lehrerKonflikteZulassen = true, klassenKonflikteZulassen = true,
        }));
        var x = neu["buchungen"]!.AsArray().Single()!;
        Assert.Equal("Förderkurs", x["titel"]!.GetValue<string>());
        Assert.Equal("M", x["fach"]!.GetValue<string>());
        Assert.Equal("7b", x["klassen"]!.AsArray().Single()!.GetValue<string>());

        // fremde Buchung: keine zusätzlichen Stunden
        var sne = await s.AlsAsync("sne", "lehrer", "SNE");
        Assert.NotEqual(HttpStatusCode.OK, (await sne.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "aktivitaet", quellBuchung = aid, termine = new[] { new { datum = "2026-11-06", stundeVon = 9, stundeBis = 9, raum = (string?)null } },
        })).StatusCode);
        Assert.Contains(Recht.Katalog, y => y.Id == Recht.Stundenplanaenderung && y.Name == "Stundenplanänderung");
    }

    [Fact]
    public async Task Terminsuche_liefert_belegte_Zeiten_Feiertage_und_Buchungsbeschraenkungen()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var admin = await s.AlsAsync("admin");
        var v = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/verfuegbarkeit", new { von = "2026-10-12", bis = "2026-10-18", lehrer = new[] { "LAN" }, klassen = new[] { "7a" }, raeume = new[] { "R108" } }));
        var tage = v["tage"]!.AsArray();
        Assert.Equal(7, tage.Count);
        Assert.True(tage[5]!["frei"]!.GetValue<bool>());
        Assert.NotEmpty(tage[3]!["belegt"]!["lehrer:LAN"]!.AsArray());
        Assert.True(tage[3]!["belegt"]!["lehrer:LAN"]!.AsArray().All(x => x!["bis"]!.GetValue<int>() > x["von"]!.GetValue<int>()));
        // Herbstferien
        var ferien = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/verfuegbarkeit", new { von = "2026-10-19", lehrer = new[] { "LAN" } }));
        Assert.True(ferien["tage"]!.AsArray().Single()!["frei"]!.GetValue<bool>());

        // Verabredung mit Uhrzeit und zwei Lehrkräften: danach belegt
        var tt = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/termine", new { wiederholung = "einmalig", datum = "2026-10-15", beginn = "15:00", ende = "15:30" }));
        var t = tt["termine"]!.AsArray().Single()!;
        var g = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", new
        {
            art = "aktivitaet", termine = new[] { new { datum = "2026-10-15", stundeVon = t["stundeVon"]!.GetValue<int>(), stundeBis = t["stundeBis"]!.GetValue<int>(), raum = (string?)null } },
            beginn = "15:00", ende = "15:30", lehrer = "LAN", weitereLehrer = new[] { "SNE" }, titel = "Verabredung",
        }));
        Assert.Equal("15:00", g["buchungen"]!.AsArray().Single()!["beginn"]!.GetValue<string>());
        v = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/verfuegbarkeit", new { von = "2026-10-15", lehrer = new[] { "SNE" } }));
        Assert.Contains(v["tage"]!.AsArray().Single()!["belegt"]!["lehrer:SNE"]!.AsArray(), x => x!["von"]!.GetValue<int>() == 15 * 60 && x["bis"]!.GetValue<int>() == 15 * 60 + 30);

        // Buchungsbeschränkungen: nur die Administration ändert sie
        Assert.Equal(45, (await Testserver.HolenAsync(lan, "/api/app/buchen/beschraenkungen"))["dauer"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await lan.PutAsJsonAsync("/api/app/buchen/beschraenkungen", new { dauer = 30, beginn = "08:00", ende = "17:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/buchen/beschraenkungen", new { dauer = 30, beginn = "17:00", ende = "08:00" })).StatusCode);
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/buchen/beschraenkungen", new { dauer = 30, beginn = "8:00", ende = "17:00" }));
        var opt = await Testserver.HolenAsync(lan, "/api/app/buchen/optionen");
        Assert.Equal(30, opt["beschraenkungen"]!["dauer"]!.GetValue<int>());
        Assert.Equal("08:00", opt["beschraenkungen"]!["beginn"]!.GetValue<string>());
        Assert.True(opt["stundenplanaenderung"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Neuer_Unterricht_mit_Zeitraum_und_Zusatzangaben()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var u = await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen/unterricht", new
        {
            fach = "D", klassen = new[] { "7a" }, wochenstunden = 2, text = "Unterricht für Projektarbeit", benachrichtigen = true,
            zusatz = new { von = "2026-10-15", bis = "2027-06-15", jahresstunden = 40, aktivitaetsart = "Unterricht", verantwortlich = "LAN" },
        }));
        Assert.Equal("2027-06-15", u["zusatz"]!["bis"]!.GetValue<string>());
        Assert.Equal(40, u["zusatz"]!["jahresstunden"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await lan.PostAsJsonAsync("/api/app/buchen/unterricht", new { fach = "D", zusatz = new { von = "2026-12-01", bis = "2026-11-01" } })).StatusCode);
        var q = await Testserver.HolenAsync(lan, $"/api/app/buchen/stundenquelle?e={u["id"]!.GetValue<int>()}");
        Assert.Equal("D", q["fach"]!.GetValue<string>());
    }

    [Fact]
    public void Oberflaeche_des_Termin_Moduls_wie_in_den_Anforderungen()
    {
        var js = Datei("js", "ansichten", "termin.js");
        foreach (var text in new[]
        {
            "'Neue Aktivität'", "<h3 class=\"rb-abschnitt\">Zeitbereich</h3>", ">ganztägig</button>", "data-weiter>Weiter</button>", "data-abbrechen>Abbrechen</button>",
            "'Lehrkraft der Aktivität benachrichtigen'", "'Buchung sofort bestätigen'", "'E-Mail-Benachrichtigung bei Statusänderung'",
            "Farbig gekennzeichnete Elemente haben zur gleichen Zeit schon eine andere Aktivität", "Farbig gekennzeichnete Räume dürfen nur reserviert werden",
            "'Bemerkung (für alle Benutzer sichtbar)'", "'Anmerkung (dieses Feld wird in zukünftigen Versionen entfernt)'", "'E-Mail Adresse'", "'Kostenstelle'", "'Teilnehmer'",
            "'Zusätzliche Stunde: Zeitbereich'", "'Erweiterte Suche >>'", "<h3 class=\"rb-abschnitt\">Uhrzeit und Dauer</h3>", "'Lehrkraftkonflikte zulassen'", "'Klassenkonflikte zulassen'",
            "data-buchen>Buchen</button>", "Recht „Stundenplanänderung“",
            "'Suche nach freien Terminen'", "'Dauer [Min.]'", "'Lehrkraft der Klasse bei Klassenselektion selektieren'", "'Lehrkräfteliste'", "titel: `Veranstaltung am ${datumDe(datum)}`",
            "Administration | Buchungsbeschränkungen", "`Buchung ${b.id} - ${b.artName}`", "Zusätzliche Stunden</a>", "Buchung stornieren", "Schüler*innen speichern", "Buchungsbeleg (PDF)",
            "'Raum- / Ressourcenliste'", "'Raumwechsel'", "sofort buchen", "reservieren",
            "'Jahresstunden'", "'Wochenstunden'",
        })
            Assert.Contains(text, js);
        var app = Datei("js", "app.js");
        Assert.Contains("{ nav: 'buchen-terminsuche', href: '#/buchen/terminsuche', text: 'Terminsuche', icon: 'pruefung' },", app);
        Assert.Contains("{ nav: 'buchen-beschraenkungen', href: '#/buchen/beschraenkungen', text: 'Buchungsbeschränkungen', icon: 'sperre' },", app);
        Assert.Contains("text: 'Buchungen', icon: 'protokoll'", app);
        // Buchen-Menü: kein Punkt „Übersicht“, jeder Punkt genau einmal; alte Adresse …/uebersicht führt zum ersten echten Punkt
        var start = app.IndexOf("m.push({ id: 'buchen'", StringComparison.Ordinal);
        var unter = app.IndexOf("unter: [", start, StringComparison.Ordinal);
        var menue = app[unter..app.IndexOf("] });", unter, StringComparison.Ordinal)];
        Assert.DoesNotContain("'Übersicht'", menue);
        Assert.DoesNotContain("buchen-uebersicht", app);
        var texte = System.Text.RegularExpressions.Regex.Matches(menue, "text: '([^']+)'").Select(m => m.Groups[1].Value).ToList();
        var ziele = System.Text.RegularExpressions.Regex.Matches(menue, "href: '([^']+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(texte.Count, texte.Distinct().Count());
        Assert.Equal(ziele.Count, ziele.Distinct().Count());
        Assert.Equal("Raum buchen", texte[0]);
        Assert.Contains("case 'uebersicht': kontext.navigieren('#/buchen/raum', true); return;", Datei("js", "ansichten", "buchen.js"));
        Assert.DoesNotContain("#/buchen/uebersicht", Datei("js", "ansichten", "buchen.js").Replace("…/uebersicht", ""));
        Assert.Contains("href=\"#/buchen/zusatz/u${k.unterricht}\" data-zusatz-link>Zusätzliche Stunden</a>", Datei("js", "stundendetails.js"));
        Assert.Contains("knopf(`#/buchen/zusatz/u${u.id}`, 'calendar-plus', 'Zusätzliche Stunden')", Datei("js", "ansichten", "unterricht.js"));
        var bu = Datei("js", "ansichten", "buchen.js");
        Assert.Contains("href=\"#/buchen/buchung/${b.id}\"><i class=\"bi bi-pencil\"></i>Bearbeiten</a>", bu);
        Assert.Contains("const von = iso.test(vonParam ?? '') ? vonParam : montag(heuteIso());", bu);
        Assert.Contains("name=\"nurReservieren\"", Datei("js", "ansichten", "buchungsverwaltung.js"));

        var verboten = new string(new[] { 'u', 'n', 't', 'i', 's' });
        foreach (var (inhalt, name) in new[] { (js, "termin.js"), (Datei("css", "termin.css"), "termin.css") })
        {
            Assert.False(inhalt.Contains(verboten, StringComparison.OrdinalIgnoreCase), name);
            foreach (var w in new[] { "font-weight: 600", "font-weight: 700", "font-weight: bold", "fw-semibold", "fw-bold" })
                Assert.False(inhalt.Contains(w), $"{name}: {w}");
        }
    }
}
