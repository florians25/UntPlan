using System.Net;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenlehrkräfte sehen auf klassenbezogenen Verwaltungsseiten nur ihre eigenen Klassen (serverseitig erzwungen, fremde Klasse ergibt 403).
/// Klassenbuch, Einträge, Gruppen und Sitzplan einer Klasse sehen zusätzlich die Lehrkräfte, die in der Klasse unterrichten.
/// Schulleitung und Administration sehen alle Klassen. Beispieldaten: FIS leitet 5a, unterrichtet 5a, 7a und EF, nicht 7b.
/// </summary>
public class KlassenleitungZugriffTests
{
    /// <summary>Seiten der Klassenleitung: nur die eigene Klasse.</summary>
    private static readonly string[] Verwalten =
    {
        "/api/app/klassenbuch/fehlzeiten?klasse={K}",
        "/api/app/abwesenheiten?klasse={K}",
        "/api/app/abwesenheiten/zeitraeume?klasse={K}",
        "/api/app/abwesenheiten/fehlzeiten?klasse={K}",
        "/api/app/berichte/fehlzeiten?klasse={K}",
        "/api/app/entschuldigungen/abwesenheiten?klasse={K}",
        "/api/app/klassendienste?klasse={K}",
        "/api/app/befreiungen?klasse={K}",
        "/api/app/kennwortblatt?klasse={K}",
        "/api/app/schueler?klasse={K}",
        "/api/app/schueler/{S}/kontaktdaten",
        "/api/app/berichte/erstellen/fehlzeiten_klasse?klasse={K}",
        "/api/app/berichte/erstellen/klassenbucheintraege?klasse={K}",
        "/api/app/berichte/erstellen/befreiungen?klasse={K}",
    };

    /// <summary>Klassenbuch einer Klasse: Klassenleitung und Lehrkräfte der Klasse.</summary>
    private static readonly string[] Einsehen =
    {
        "/api/app/klassenbuch/klasse?klasse={K}",
        "/api/app/klassenbuch/hausaufgaben?klasse={K}",
        "/api/app/klassenbuch/woche?klasse={K}",
        "/api/app/klassenbuch/vermerke?klasse={K}",
        "/api/app/klassenbuch/schueler?klasse={K}",
        "/api/app/klassenbucheintraege?klasse={K}",
        "/api/app/berichte/lehrstoff?klasse={K}",
        "/api/app/entschuldigungen/fehlzeiten?klasse={K}",
        "/api/app/klassenbuch/uebersicht?klasse={K}",
        "/api/app/schuelergruppen?klasse={K}",
        "/api/app/schuelergruppen/matrix?klassen={K}",
        "/api/app/sitzplan/{K}",
        "/api/app/unterrichtklassen?klasse={K}",
        "/api/app/unterrichtswechsel/unterricht?klasse={K}",
        "/api/app/berichte/erstellen/wochenuebersicht?klasse={K}",
    };

    private static string Pfad(Testserver s, string muster, string klasse)
    {
        var sch = s.Dienst<Paketdienst>().Aktuell!.Schueler.First(x => x.Klasse == klasse).Id;
        return muster.Replace("{K}", klasse).Replace("{S}", sch.ToString());
    }

    private static async Task Erwarten(HttpClient c, Testserver s, string muster, string klasse, HttpStatusCode code)
    {
        var pfad = Pfad(s, muster, klasse);
        var r = await c.GetAsync(pfad);
        Assert.True(r.StatusCode == code, $"{pfad}: erwartet {(int)code}, erhalten {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Klassenleitung_sieht_auf_Verwaltungsseiten_nur_die_eigene_Klasse()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        Assert.Contains("FIS", st.Klassen["5a"].Klassenleitung);
        Assert.True(Klassenbuchdienst.UnterrichtetKlasse(st, "FIS", "7a"));
        Assert.False(Klassenbuchdienst.UnterrichtetKlasse(st, "FIS", "7b"));
        var lk = await s.AlsAsync("fis", "lehrer", "FIS");
        foreach (var p in Verwalten)
        {
            await Erwarten(lk, s, p, "5a", HttpStatusCode.OK);
            await Erwarten(lk, s, p, "7b", HttpStatusCode.Forbidden);
            // Unterricht in der Klasse genügt für die Verwaltungsseiten nicht
            await Erwarten(lk, s, p, "7a", HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Klassenbuch_sehen_Klassenleitung_und_Lehrkraefte_der_Klasse()
    {
        await using var s = await Testserver.StartenAsync();
        var lk = await s.AlsAsync("fis", "lehrer", "FIS");
        foreach (var p in Einsehen)
        {
            await Erwarten(lk, s, p, "5a", HttpStatusCode.OK);
            await Erwarten(lk, s, p, "7a", HttpStatusCode.OK);
            await Erwarten(lk, s, p, "7b", HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Schulleitung_und_Administration_sehen_alle_Klassen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var leitung = await s.AlsAsync("sl", "leitung", "");
        foreach (var c in new[] { admin, leitung })
            // Kennwort-Blatt und Fachlehrer-Sicht sind eigene Funktionen der Klassenleitung bzw. der Lehrkräfte
            foreach (var p in Verwalten.Concat(Einsehen).Where(p => !p.Contains("kennwortblatt") && !p.Contains("entschuldigungen/fehlzeiten")))
                await Erwarten(c, s, p, "7b", HttpStatusCode.OK);
        // Berichte ohne Klasse: alle Klassen
        var r = await admin.GetAsync("/api/app/berichte/erstellen/fehlzeiten_klasse");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Listen_ohne_Klasse_enthalten_nur_eigene_Klassen()
    {
        await using var s = await Testserver.StartenAsync();
        var lk = await s.AlsAsync("fis", "lehrer", "FIS");
        // Sitzung: Klassen der Klassenleitung und des Unterrichts für die Auswahllisten
        var ich = (await Testserver.HolenAsync(lk, "/api/app/sitzung"))["benutzer"]!;
        Assert.False(ich["alleKlassen"]!.GetValue<bool>());
        Assert.Equal(new[] { "5a" }, ich["klassenleitung"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("7a", ich["unterrichtsklassen"]!.AsArray().Select(x => x!.GetValue<string>()));
        var adminIch = (await Testserver.HolenAsync(await s.AlsAsync("admin"), "/api/app/sitzung"))["benutzer"]!;
        Assert.True(adminIch["alleKlassen"]!.GetValue<bool>());

        var schueler = await Testserver.HolenAsync(lk, "/api/app/schueler");
        Assert.All(schueler["schueler"]!.AsArray(), x => Assert.Equal("5a", x!["klasse"]!.GetValue<string>()));
        Assert.Equal(new[] { "5a" }, schueler["klassen"]!.AsArray().Select(x => x!["kurz"]!.GetValue<string>()));
        Assert.NotEmpty(schueler["schueler"]!.AsArray());

        // Klassenberichte verlangen eine eigene Klasse
        var r = await lk.GetAsync("/api/app/berichte/erstellen/fehlzeiten_klasse");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        // Prüfungen bleiben für alle Lehrkräfte sichtbar
        Assert.Equal(HttpStatusCode.OK, (await lk.GetAsync("/api/app/berichte/erstellen/pruefungen")).StatusCode);
    }

    [Fact]
    public async Task Eigener_Unterricht_bleibt_unberuehrt()
    {
        await using var s = await Testserver.StartenAsync();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var heute = DateOnly.FromDateTime(Testserver.Jetzt);
        // eine Stunde der Lehrkraft in einer Klasse, die sie nicht leitet
        var karte = Enumerable.Range(0, 14).Select(i => heute.AddDays(-i))
            .SelectMany(d => s.Dienst<Plandienst>().Karten(st, d, Elementtyp.Lehrer, "FIS").Select(k => (d, k)))
            .First(x => x.k.Unterricht.HasValue && x.k.Klassen.Any(w => w.K == "7a") && x.k.Status is not ("entfall" or "abwesend"));
        var lk = await s.AlsAsync("fis", "lehrer", "FIS");
        var anw = await lk.GetAsync($"/api/app/klassenbuch/anwesenheit?datum={Plandienst.Iso(karte.d)}&stunde={karte.k.Von}&unterricht={karte.k.Unterricht}");
        Assert.Equal(HttpStatusCode.OK, anw.StatusCode);
    }
}
