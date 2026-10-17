using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprint 26: Fachlehrer-Entschuldigungen (Einstellungen, Rechte, Sperre, Texte derselben Abwesenheit, mehrdeutige Abwesenheiten,
/// Entschuldigung und Zurücknahme durch die Klassenleitung, Jahrgangsstufen), Prüfungen löschen (Ersteller, Lehrkraft, Administration;
/// vor und nach der Übernahme) und die Stornierung übertragener Buchungen an den Desktop (Rückmeldung „buchungstorno“).
/// Feste Uhr: Mittwoch, 14.10.2026; Klasse 7a (Jahrgang 7, Klassenleitung LAN).
/// </summary>
public class EntschuldigungTests
{
    private static readonly DateOnly Tag = new(2026, 10, 12);

    /// <summary>Zwei Fachlehrkräfte (nicht Klassenleitung) mit eigenen Stunden in 7a am Tag, eine Schülerin bzw. ein Schüler.</summary>
    private static (string A, string B, int Schueler) Vorbereiten(Testserver s)
    {
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var kl = st.Paket.Elemente.Klassen.First(k => k.Kurz == "7a").Klassenleitung;
        var karten = s.Dienst<Plandienst>().Karten(st, Tag, Elementtyp.Klasse, "7a").Where(k => k.Unterricht.HasValue && k.Status == "regulaer").ToList();
        // ohne Kopplungen (z. B. KR/ER in derselben Stunde): genau eine Lehrkraft je Stunde
        var lehrer = karten.Where(k => k.Lehrer.Count == 1 && !kl.Contains(k.Lehrer[0].K) && !karten.Any(x => x != k && x.Von <= k.Bis && x.Bis >= k.Von))
            .Select(k => k.Lehrer[0].K).Distinct().ToList();
        Assert.True(lehrer.Count >= 2, "zwei Fachlehrkräfte in 7a erwartet");
        return (lehrer[0], lehrer[1], st.Paket.Elemente.Schueler.First(x => x.Klasse == "7a").Id);
    }

    private static async Task EinstellenAsync(HttpClient admin, Action<JsonObject> aendern)
    {
        var e = (await Testserver.HolenAsync(admin, "/api/app/klassenbuch/einstellungen"))["einstellungen"]!.AsObject();
        aendern(e);
        await Testserver.JsonAsync(await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e)));
    }

    private static List<JsonNode> Zeilen(JsonNode d) => d["zeilen"]!.AsArray().Select(x => x!).ToList();
    private static List<int> Ids(IEnumerable<JsonNode> z) => z.Select(x => x["id"]!.GetValue<int>()).ToList();

    [Fact]
    public async Task Fachlehrer_entschuldigen_Stunden_Klassenleitung_ueberschreibt_und_nimmt_zurueck()
    {
        await using var s = await Testserver.StartenAsync();
        var (la, lb, sid) = Vorbereiten(s);
        var admin = await s.AlsAsync("admin");
        var a = await s.AlsAsync(la.ToLowerInvariant(), "lehrer", la);
        var b = await s.AlsAsync(lb.ToLowerInvariant(), "lehrer", lb);
        var kl = await s.AlsAsync("lan", "lehrer", "LAN");
        // ganzer Tag abwesend (Klassenleitung)
        await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/tage", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-12", status = "offen", bemerkung = "krank gemeldet" }));
        var q = "von=2026-10-12&bis=2026-10-12";

        // Modul aus: Liste lesbar, Entschuldigen verboten
        var ein = await Testserver.HolenAsync(a, "/api/app/entschuldigungen/einstellungen");
        Assert.False(ein["aktiv"]!.GetValue<bool>());
        Assert.False(ein["fachlehrer"]!.GetValue<bool>());
        var liste = await Testserver.HolenAsync(a, $"/api/app/entschuldigungen/fehlzeiten?{q}");
        var eigene = Zeilen(liste).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.NotEmpty(eigene);
        Assert.All(eigene, z => Assert.Contains(la, z["lehrer"]!.GetValue<string>()));
        Assert.All(eigene, z => Assert.False(z["darf"]!.GetValue<bool>()));
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(eigene), status = "entschuldigt" })).StatusCode);

        // Modul an (Klassenbuch-Einstellungen) – in der Sitzung sichtbar
        await EinstellenAsync(admin, e => e["fachlehrerEntschuldigungen"] = true);
        Assert.True((await Testserver.HolenAsync(a, "/api/app/sitzung"))["benutzer"]!["fachlehrerEntschuldigungen"]!.GetValue<bool>());
        liste = await Testserver.HolenAsync(a, $"/api/app/entschuldigungen/fehlzeiten?{q}&klasse=7a&nurOffene=true");
        eigene = Zeilen(liste).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.Contains("7a", liste["klassen"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.All(eigene, z => Assert.True(z["darf"]!.GetValue<bool>()));
        var r = await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(eigene), status = "entschuldigt", text = "Hat Attest vom Arzt.", datum = "2026-10-14" }));
        Assert.Equal(eigene.Count, r["geaendert"]!.GetValue<int>());

        // einmal gesetzt: gesperrt (übersprungen), Dialog zeigt die eigene Entschuldigung
        r = await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(eigene), status = "unentschuldigt" }));
        Assert.Equal(0, r["geaendert"]!.GetValue<int>());
        Assert.Equal(eigene.Count, r["gesperrt"]!.AsArray().Count);
        var nachher = Zeilen(await Testserver.HolenAsync(a, $"/api/app/entschuldigungen/fehlzeiten?{q}")).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(nachher, z => { Assert.True(z["gesperrt"]!.GetValue<bool>()); Assert.Equal("entschuldigt", z["status"]!.GetValue<string>()); Assert.Equal("Hat Attest vom Arzt.", z["entschuldigung"]!["text"]!.GetValue<string>()); });

        // andere Lehrkraft derselben Abwesenheit sieht den Text, darf die fremde Stunde aber nicht entschuldigen
        var zb = Zeilen(await Testserver.HolenAsync(b, $"/api/app/entschuldigungen/fehlzeiten?{q}")).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.NotEmpty(zb);
        Assert.Contains(zb[0]["texte"]!.AsArray(), t => t!["text"]!.GetValue<string>() == "Hat Attest vom Arzt." && t["lehrer"]!.GetValue<string>() == la);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(eigene), status = "entschuldigt" })).StatusCode);
        await Testserver.JsonAsync(await b.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(zb), status = "unentschuldigt", text = "Kein Attest gesehen." }));

        // Klassenleitung: Abwesenheit mehrdeutig (offen), x/y Lehrer bearbeitet; Fachlehrer-Sicht für die Klassenleitung gesperrt
        var abw = (await Testserver.HolenAsync(kl, $"/api/app/entschuldigungen/abwesenheiten?{q}&klasse=7a")).AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == sid)!;
        Assert.True(abw["mehrdeutig"]!.GetValue<bool>());
        Assert.Equal("offen", abw["status"]!.GetValue<string>());
        Assert.True(abw["lehrerBearbeitet"]!.GetValue<int>() >= 2);
        Assert.True(abw["lehrerGesamt"]!.GetValue<int>() >= abw["lehrerBearbeitet"]!.GetValue<int>());
        Assert.Contains(abw["stunden"]!.AsArray(), x => x!["entschuldigung"]?["text"]?.GetValue<string>() == "Kein Attest gesehen.");
        var ids = abw["ids"]!.AsArray().Select(x => x!.GetValue<int>()).ToList();
        var detail = await Testserver.HolenAsync(b, $"/api/app/entschuldigungen/abwesenheit?ids={string.Join(",", ids)}");
        Assert.False(detail["darf"]!.GetValue<bool>()); // Fachlehrkraft: nur lesen
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/app/entschuldigungen/abwesenheit", new { ids, status = "entschuldigt" })).StatusCode);

        // Klassenleitung entschuldigt die ganze Abwesenheit (überschreibt), Fachlehrkraft sieht den Text gesperrt
        r = await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/entschuldigungen/abwesenheit", new { ids, status = "entschuldigt", text = "Abwesenheit ist OK.", datum = "2026-10-14", nachweis = "attest" }));
        Assert.Equal(ids.Count, r["geaendert"]!.GetValue<int>());
        Assert.True(r["ueberschrieben"]!.GetValue<int>() >= 1);
        abw = (await Testserver.HolenAsync(kl, $"/api/app/entschuldigungen/abwesenheiten?{q}")).AsArray().Single(x => x!["schuelerId"]!.GetValue<int>() == sid)!;
        Assert.Equal("entschuldigt", abw["status"]!.GetValue<string>());
        Assert.False(abw["mehrdeutig"]!.GetValue<bool>());
        Assert.Equal("attest", abw["nachweis"]!.GetValue<string>());
        zb = Zeilen(await Testserver.HolenAsync(b, $"/api/app/entschuldigungen/fehlzeiten?{q}")).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(zb, z =>
        {
            Assert.True(z["gesperrt"]!.GetValue<bool>());
            Assert.Equal("klassenleitung", z["entschuldigung"]!["rolle"]!.GetValue<string>());
            Assert.Equal("Abwesenheit ist OK.", z["entschuldigung"]!["text"]!.GetValue<string>());
            Assert.Contains("Klassenleitung", z["hinweis"]!.GetValue<string>());
        });

        // zurücknehmen: alles offen, Fachlehrkräfte dürfen wieder
        await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/entschuldigungen/zuruecknehmen", new { ids }));
        var wieder = Zeilen(await Testserver.HolenAsync(a, $"/api/app/entschuldigungen/fehlzeiten?{q}")).Where(z => z["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(wieder, z => { Assert.Equal("offen", z["status"]!.GetValue<string>()); Assert.True(z["darf"]!.GetValue<bool>()); Assert.Null(z["entschuldigung"]); });

        // Neuspeichern der Anwesenheit behält die Entschuldigung der Stunde (Schlüssel Schüler*in, Datum, Stunde)
        await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(wieder), status = "entschuldigt", text = "Attest" }));
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var karte = s.Dienst<Plandienst>().Karten(st, Tag, Elementtyp.Lehrer, la).First(k => k.Klassen.Any(x => x.K == "7a") && k.Unterricht.HasValue);
        var aAdmin = s.Dienst<Benutzerdienst>().NachLogin("admin")!;
        s.Dienst<Klassenbuchdienst>().AnwesenheitSpeichern(aAdmin, st, Tag, karte.Von, karte.Unterricht!.Value, new() { new AnwesenheitEintrag(sid, "fehlt", 0, null) });
        var neu = Zeilen(await Testserver.HolenAsync(a, $"/api/app/entschuldigungen/fehlzeiten?{q}")).Where(z => z["schuelerId"]!.GetValue<int>() == sid && z["stunde"]!.GetValue<int>() == karte.Von).ToList();
        Assert.Equal("Attest", neu.Single()["entschuldigung"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Jahrgangsstufen_und_Klassenleitung_im_eigenen_Unterricht()
    {
        await using var s = await Testserver.StartenAsync();
        var (la, _, sid) = Vorbereiten(s);
        var admin = await s.AlsAsync("admin");
        var a = await s.AlsAsync(la.ToLowerInvariant(), "lehrer", la);
        var kl = await s.AlsAsync("lan", "lehrer", "LAN");
        await Testserver.JsonAsync(await kl.PostAsJsonAsync("/api/app/klassenbuch/fehlzeiten/tage", new { schuelerId = sid, von = "2026-10-12", bis = "2026-10-12" }));
        // Einschränkung ohne Jahrgang ist ungültig, „bis“ vor „von“ auch
        var e = (await Testserver.HolenAsync(admin, "/api/app/klassenbuch/einstellungen"))["einstellungen"]!.AsObject();
        e["fachlehrerEntschuldigungen"] = true;
        e["fachlehrerJahrgangEinschraenken"] = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e))).StatusCode);
        e["fachlehrerJahrgangVon"] = 13; e["fachlehrerJahrgangBis"] = 11;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e))).StatusCode);
        e["fachlehrerJahrgangVon"] = 11; e["fachlehrerJahrgangBis"] = 13;
        await Testserver.JsonAsync(await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e)));

        // Jahrgang 7 liegt außerhalb von 11–13
        var z = Zeilen(await Testserver.HolenAsync(a, "/api/app/entschuldigungen/fehlzeiten?von=2026-10-12&bis=2026-10-12")).Where(x => x["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(z, x => { Assert.False(x["darf"]!.GetValue<bool>()); Assert.Contains("Jahrgangsstufe", x["hinweis"]!.GetValue<string>()); });
        e["fachlehrerJahrgangVon"] = 5; e["fachlehrerJahrgangBis"] = 8;
        await Testserver.JsonAsync(await admin.PutAsync("/api/app/klassenbuch/einstellungen", JsonContent.Create(e)));
        z = Zeilen(await Testserver.HolenAsync(a, "/api/app/entschuldigungen/fehlzeiten?von=2026-10-12&bis=2026-10-12")).Where(x => x["schuelerId"]!.GetValue<int>() == sid).ToList();
        Assert.All(z, x => Assert.True(x["darf"]!.GetValue<bool>()));

        // Recht entzogen (Rechtedienst): keine Fachlehrer-Entschuldigungen
        var bd = s.Dienst<Benutzerdienst>();
        Rechtedienst.UeberschreibungenSetzen(s.Dienst<UntPlanWeb.Daten.Db>(), bd.NachLogin(la.ToLowerInvariant())!.Id, new() { [Recht.AbwesenheitFachlehrer] = 0 });
        var ohne = await s.AlsAsync(la.ToLowerInvariant(), "lehrer", la);
        Assert.False((await Testserver.HolenAsync(ohne, "/api/app/entschuldigungen/einstellungen"))["fachlehrer"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await ohne.PostAsJsonAsync("/api/app/entschuldigungen/fehlzeiten", new { ids = Ids(z), status = "entschuldigt" })).StatusCode);

        // Einheiten: Jahrgang aus den Stammdaten („Jg11“, „11“), ohne Einschränkung alle
        var einst = new KlassenbuchEinstellungen { FachlehrerJahrgangEinschraenken = true, FachlehrerJahrgangVon = 11, FachlehrerJahrgangBis = 13 };
        Assert.True(einst.FachlehrerJahrgangErlaubt("Jg11"));
        Assert.True(einst.FachlehrerJahrgangErlaubt("13"));
        Assert.False(einst.FachlehrerJahrgangErlaubt("10"));
        Assert.False(einst.FachlehrerJahrgangErlaubt(""));
        Assert.True(new KlassenbuchEinstellungen().FachlehrerJahrgangErlaubt(""));
        Assert.Contains(Recht.Katalog, r => r.Id == Recht.AbwesenheitFachlehrer && r.Standard[2] == Stufe.Ja && r.Standard[3] == Stufe.Nein);
    }

    [Fact]
    public async Task Pruefung_loeschen_nur_Ersteller_Lehrkraft_und_Administration_vor_und_nach_der_Uebernahme()
    {
        await using var s = await Testserver.StartenAsync();
        var a = await s.AlsAsync("lan", "lehrer", "LAN");
        var b = await s.AlsAsync("sne", "lehrer", "SNE");
        var admin = await s.AlsAsync("admin");
        var neu = new { datum = "2026-10-15", stundeVon = 3, stundeBis = 4, klassen = new[] { "7a" }, raeume = Array.Empty<string>(), aufsicht = Array.Empty<string>(), titel = "Mathe-Arbeit" };
        var alt = new { datum = "2026-10-15", stundeVon = 3, stundeBis = 4, klassen = new[] { "7a" }, titel = "Mathe-Arbeit" };
        await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/meldungen/klausur", neu));

        // Liste: nur die Erstellerin darf löschen
        bool Darf(JsonNode liste) => liste.AsArray().Single(p => p!["titel"]!.GetValue<string>() == "Mathe-Arbeit")!["darfLoeschen"]!.GetValue<bool>();
        Assert.True(Darf(await Testserver.HolenAsync(a, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")));
        Assert.False(Darf(await Testserver.HolenAsync(b, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")));
        Assert.True(Darf(await Testserver.HolenAsync(admin, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", alt)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new { alt, neu })).StatusCode);

        // vor der Übernahme: zurückgezogen, Plan zeigt sie nicht mehr
        var r = await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", alt));
        Assert.Null(r["meldung"]?.GetValue<string>());
        Assert.DoesNotContain((await Testserver.HolenAsync(a, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")).AsArray(), p => p!["titel"]!.GetValue<string>() == "Mathe-Arbeit");

        // nach der Übernahme (Meldung quittiert, Prüfung im Paket): Löschmeldung an den Desktop, Plan zeigt sie sofort nicht mehr
        await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/meldungen/klausur", neu));
        var sync = s.SyncClient();
        var ids = (await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray().Where(x => x!["typ"]!.GetValue<string>() == "klausur").Select(x => x!["id"]!.GetValue<string>()).ToArray();
        await Testserver.JsonAsync(await sync.PostAsJsonAsync($"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen/quittieren", ids));
        var paket = Testserver.Beispielpaket();
        paket["revision"] = 2;
        paket["termine"]!.AsArray().Add(new JsonObject { ["art"] = "klausur", ["titel"] = "Mathe-Arbeit", ["von"] = "2026-10-15", ["bis"] = "2026-10-15", ["stundeVon"] = 3, ["stundeBis"] = 4,
            ["klassen"] = new JsonArray("7a"), ["lehrer"] = new JsonArray("LAN"), ["raeume"] = new JsonArray() });
        Assert.Equal(HttpStatusCode.OK, (await s.PaketSendenAsync(paket)).StatusCode);
        Assert.True(Darf(await Testserver.HolenAsync(a, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")));
        Assert.False(Darf(await Testserver.HolenAsync(b, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", alt)).StatusCode);
        r = await Testserver.JsonAsync(await a.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", alt));
        Assert.NotNull(r["meldung"]?.GetValue<string>());
        var rm = (await Testserver.HolenAsync(sync, $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen")).AsArray();
        Assert.Contains(rm, x => x!["typ"]!.GetValue<string>() == WebRueckmeldungsTypen.KlausurLoeschen && x["titel"]!.GetValue<string>() == "Mathe-Arbeit");
        Assert.DoesNotContain((await Testserver.HolenAsync(a, "/api/app/pruefungen?von=2026-10-15&bis=2026-10-15")).AsArray(), p => p!["titel"]!.GetValue<string>() == "Mathe-Arbeit");
        var plan = await Testserver.HolenAsync(a, "/api/app/plan?typ=klasse&element=7a&datum=2026-10-15");
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["datum"]!.GetValue<string>() == "2026-10-15" && k["status"]!.GetValue<string>() == "klausur" && (k["text"]?.GetValue<string>() ?? "").Contains("Mathe-Arbeit"));
    }

    [Fact]
    public async Task Stornierte_uebertragene_Buchung_geht_als_buchungstorno_an_den_Desktop()
    {
        await using var s = await Testserver.StartenAsync();
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var sync = s.SyncClient();
        var pfad = $"/api/v1/schulen/{Testserver.SchulId}/rueckmeldungen";
        object Buchung(int von) => new { art = "raum", termine = new[] { new { datum = "2026-10-15", stundeVon = von, stundeBis = von, raum = "R108" } }, klassen = new[] { "7a" }, titel = "Projekt" };
        var b1 = (await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung(3))))["buchungen"]![0]!["id"]!.GetValue<int>();
        var b2 = (await Testserver.JsonAsync(await lan.PostAsJsonAsync("/api/app/buchen", Buchung(5))))["buchungen"]![0]!["id"]!.GetValue<int>();

        // noch nicht übertragen und storniert: verschwindet einfach (keine Stornomeldung)
        await Testserver.JsonAsync(await lan.DeleteAsync($"/api/app/buchen/{b2}"));
        var liste = (await Testserver.HolenAsync(sync, pfad)).AsArray();
        Assert.Contains(liste, x => x!["id"]!.GetValue<string>() == $"buchung-{b1}");
        Assert.DoesNotContain(liste, x => x!["id"]!.GetValue<string>().EndsWith($"buchung-{b2}"));

        // übertragen (quittiert), dann storniert: Stornomeldung mit Bezug auf die Web-Id
        await Testserver.JsonAsync(await sync.PostAsJsonAsync($"{pfad}/quittieren", new[] { $"buchung-{b1}" }));
        Assert.DoesNotContain((await Testserver.HolenAsync(sync, pfad)).AsArray(), x => x!["id"]!.GetValue<string>().Contains($"buchung-{b1}"));
        await Testserver.JsonAsync(await lan.DeleteAsync($"/api/app/buchen/{b1}"));
        var vertrag = System.Text.Json.JsonSerializer.Deserialize<List<WebRueckmeldung>>(await (await sync.GetAsync(pfad)).Content.ReadAsStringAsync(), UntPlanWebSchema.Json)!;
        var storno = vertrag.Single(m => m.Typ == WebRueckmeldungsTypen.BuchungStorno);
        Assert.Equal($"storno-buchung-{b1}", storno.Id);
        Assert.Equal($"buchung-{b1}", storno.BezugId);
        Assert.Equal("storniert", storno.Grund);
        Assert.Equal("2026-10-15", storno.Von);
        Assert.Equal(3, storno.StundeVon);
        Assert.Equal("R108", storno.Raum);
        Assert.Equal("7a", storno.Klassen.Single());
        // im Plan sofort weg
        var plan = await Testserver.HolenAsync(lan, "/api/app/plan?typ=raum&element=R108&datum=2026-10-15");
        Assert.DoesNotContain(plan["karten"]!.AsArray(), k => k!["text"]?.GetValue<string>() == "Projekt");

        // Desktop quittiert die Stornierung: wird nicht mehr angeboten (idempotent)
        var q = await Testserver.JsonAsync(await sync.PostAsJsonAsync($"{pfad}/quittieren", new { ids = new[] { storno.Id } }));
        Assert.Equal(1, q["quittiert"]!.GetValue<int>());
        Assert.DoesNotContain((await Testserver.HolenAsync(sync, pfad)).AsArray(), x => x!["typ"]!.GetValue<string>() == WebRueckmeldungsTypen.BuchungStorno);
        q = await Testserver.JsonAsync(await sync.PostAsJsonAsync($"{pfad}/quittieren", new[] { storno.Id }));
        Assert.Equal(0, q["quittiert"]!.GetValue<int>());

        // Unterricht löschen storniert übertragene zusätzliche Stunden ebenfalls mit Stornomeldung
        var bu = s.Dienst<Buchungsdienst>();
        var ich = s.Dienst<Benutzerdienst>().NachLogin("lan")!;
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var eu = bu.UnterrichtAnlegen(st, ich, "LAN", st.Paket.Elemente.Faecher.First().Kurz, new[] { "7a" }, "AG", "", 1);
        var (stunden, _) = bu.Speichern(st, ich, new BuchungDaten("unterricht", new() { new TerminWahl(new DateOnly(2026, 10, 15), 9, 9, null) }, UnterrichtId: eu.Id));
        Assert.Equal(1, bu.Quittieren(new[] { $"buchung-{stunden[0].Id}" }));
        bu.UnterrichtLoeschen(ich, eu.Id);
        Assert.Contains(bu.Rueckmeldungen(), m => m.Typ == WebRueckmeldungsTypen.BuchungStorno && m.BezugId == $"buchung-{stunden[0].Id}");
    }

    [Fact]
    public void Klassenbuch_Dialoge_nutzen_die_gemeinsame_Modal_Komponente()
    {
        var js = Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "js");
        var komponente = File.ReadAllText(Path.Combine(js, "kbdialog.js"));
        foreach (var teil in new[] { "kbd-s", "kbd-m", "kbd-l", "kbd-xl", "Escape", "kbd-zu", "kbd-fuss" }) Assert.Contains(teil, komponente);
        // alle Klassenbuch-Seiten holen Dialog und Rückfrage aus kbdialog.js (nicht mehr aus ui.js)
        foreach (var datei in new[] { "ansichten/klassenbuch.js", "ansichten/abwesenheiten.js", "ansichten/dienste.js", "ansichten/befreiungen.js", "ansichten/eintraege.js",
                     "ansichten/offen.js", "ansichten/berichte.js", "ansichten/entschuldigungen.js", "hausaufgabe.js", "stundendetails.js" })
        {
            var text = File.ReadAllText(Path.Combine(js, datei));
            Assert.Contains("kbdialog.js", text);
            Assert.DoesNotMatch(@"import \{[^}]*\bdialog\b[^}]*\} from '\.\.?/ui\.js'", text);
            Assert.DoesNotContain("sd-altdialog", text);
        }
        Assert.Contains(".kbd-overlay", File.ReadAllText(Path.Combine(Wurzel(), "src", "UntPlanWeb", "wwwroot", "css", "klassenbuch.css")));
    }

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
