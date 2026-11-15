using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Sprechtag: Anlegen je Schuljahr, Konfiguration in drei Schritten (laufend gespeichert), Prüfung und Veröffentlichen, die acht Phasen,
/// Sichtbarkeit je Rolle, Räume je Abschnitt, Abwesenheiten (mit automatischer Absage), Einladungen und Terminanfragen, Buchung durch
/// Erziehungsberechtigte (1. Phase nur mit Einladung, 2. Phase frei), Buchungsregeln, Rechte der Lehrkräfte, Startkarten und Benachrichtigungen.
/// </summary>
public class SprechtagTests
{
    // Testuhr: Mittwoch, 14.10.2026 10:00 (Phase „Verwaltung LK“ nach dem Veröffentlichen)
    private static object Konfig(string name = "Sprechtag Herbst", object? rechte = null, object? regeln = null, bool online = false) => new
    {
        name, dauer = 10, online,
        abschnitte = new[]
        {
            new { name = "1. Sprechtagsabschnitt", datum = "2026-10-30", von = "15:00", bis = "16:00" },
            new { name = "2. Sprechtagsabschnitt", datum = "2026-10-30", von = "16:10", bis = "17:00" },
        },
        phase1Von = "2026-10-15T08:00", phase1Bis = "2026-10-20T18:00", phase2Von = "2026-10-21T08:00", phase2Bis = "2026-10-28T18:00",
        rechte = rechte ?? new { raeumeWaehlen = true, abwesenheitenEintragen = true, termineBearbeiten = true, termineBuchen = true },
        regeln = regeln ?? new { },
    };

    private static async Task<(HttpClient C, int Id)> ElternAsync(Testserver s, HttpClient admin, string login, params int[] kinder)
    {
        var r = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/benutzer", new { login, passwort = "eltern-passwort", rolle = "eltern", anzeigename = login, gesperrt = false, schuelerIds = kinder }));
        var c = s.Client();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/app/anmelden", new { login, passwort = "eltern-passwort" })).StatusCode);
        return (c, r["benutzer"]!["id"]!.GetValue<int>());
    }

    private static async Task<string> FehlerAsync(HttpResponseMessage r, HttpStatusCode erwartet = HttpStatusCode.BadRequest)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(erwartet == r.StatusCode, $"erwartet {erwartet}, erhalten {(int)r.StatusCode}: {text}");
        return text;
    }

    /// <summary>Angelegter und veröffentlichter Sprechtag (Phase „Verwaltung LK“).</summary>
    private static async Task<int> VeroeffentlichtAsync(HttpClient admin, object? konfig = null)
    {
        var sj = (await Testserver.HolenAsync(admin, "/api/app/sprechtag"))["schuljahre"]!.AsArray()[0]!["name"]!.GetValue<string>();
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { schuljahr = sj })))["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", konfig ?? Konfig()));
        await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { }));
        return id;
    }

    [Fact]
    public async Task Anlegen_Konfigurieren_Pruefen_Veroeffentlichen_Duplizieren_Loeschen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var liste = await Testserver.HolenAsync(admin, "/api/app/sprechtag");
        Assert.True(liste["admin"]!.GetValue<bool>());
        Assert.Equal(2, liste["schuljahre"]!.AsArray().Count);
        Assert.Equal(8, liste["phasen"]!.AsArray().Count);
        var sj = liste["schuljahre"]!.AsArray()[0]!["name"]!.GetValue<string>();

        // nur die Administration legt an; unbekanntes Schuljahr wird abgelehnt
        await FehlerAsync(await lan.PostAsJsonAsync("/api/app/sprechtag", new { schuljahr = sj }), HttpStatusCode.Forbidden);
        await FehlerAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { schuljahr = "1999/2000" }));
        var t = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new { schuljahr = sj }));
        var id = t["id"]!.GetValue<int>();
        Assert.Equal("entwurf", t["phase"]!.GetValue<string>());
        Assert.Equal("Entwurf", t["phaseText"]!.GetValue<string>());
        Assert.Contains($"{sj} | ", t["schuljahrText"]!.GetValue<string>());

        // Entwurf: unvollständig speicherbar, aber nicht veröffentlichbar; für Lehrkräfte unsichtbar
        var teil = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", new { name = "Halb fertig", dauer = 10, abschnitte = Array.Empty<object>() }));
        Assert.NotEmpty(teil["fehler"]!.AsArray());
        Assert.Contains("Sprechtagsabschnitt", await FehlerAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { })));
        Assert.Empty((await Testserver.HolenAsync(lan, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        await FehlerAsync(await lan.GetAsync($"/api/app/sprechtag/{id}"));
        // Grenzen der Konfiguration
        await FehlerAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", new { name = new string('x', 81), dauer = 10 }));
        await FehlerAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", new { name = "x", dauer = 0 }));
        // Abschnitte überschneiden sich, Anmeldephasen nach dem ersten Abschnitt bzw. überlappend
        var falsch = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", new
        {
            name = "x", dauer = 10,
            abschnitte = new[] { new { name = "A", datum = "2026-10-30", von = "15:00", bis = "16:00" }, new { name = "B", datum = "2026-10-30", von = "15:30", bis = "17:00" } },
            phase1Von = "2026-10-15T08:00", phase1Bis = "2026-10-22T18:00", phase2Von = "2026-10-21T08:00", phase2Bis = "2026-10-31T18:00",
        }));
        var fehler = string.Join(" ", falsch["fehler"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.Contains("überschneiden sich", fehler);
        Assert.Contains("dürfen sich nicht überschneiden", fehler);
        Assert.Contains("vor dem ersten Sprechtagsabschnitt", fehler);

        // vollständig: veröffentlichen → Verwaltung LK, Lehrkräfte sehen ihn, Eltern nicht
        Assert.Empty((await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Konfig())))["fehler"]!.AsArray());
        var v = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { }));
        Assert.Equal("verwaltung", v["phase"]!.GetValue<string>());
        Assert.Equal("30.10.2026", v["datumText"]!.GetValue<string>());
        Assert.Equal("15.10.2026 – 28.10.2026", v["anmeldungText"]!.GetValue<string>());
        Assert.True(v["bearbeitbar"]!.GetValue<bool>());
        await FehlerAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { }));
        Assert.Single((await Testserver.HolenAsync(lan, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        var (eltern, _) = await ElternAsync(s, admin, "eltern-a", s.Dienst<Paketdienst>().Aktuell!.Schueler[0].Id);
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        // Schüler*innen haben keinen Zugriff
        var schueler = await s.AlsAsync("klasse-7a", "schueler", "7a");
        await FehlerAsync(await schueler.GetAsync("/api/app/sprechtag"), HttpStatusCode.Forbidden);

        // Bearbeiten ohne Termine: wird wieder Entwurf
        var neu = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/sprechtag/{id}", Konfig("Sprechtag Herbst 2026")));
        Assert.Equal("entwurf", neu["phase"]!.GetValue<string>());
        Assert.False(neu["veroeffentlicht"]!.GetValue<bool>());
        await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/veroeffentlichen", new { }));

        // Duplizieren: Entwurf mit gleicher Konfiguration; Löschen
        var kopie = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/sprechtag/{id}/duplizieren", new { }));
        Assert.Equal("Sprechtag Herbst 2026 (Kopie)", kopie["name"]!.GetValue<string>());
        Assert.Equal("entwurf", kopie["phase"]!.GetValue<string>());
        Assert.Equal(2, kopie["abschnitte"]!.AsArray().Count);
        await FehlerAsync(await lan.DeleteAsync($"/api/app/sprechtag/{kopie["id"]}"), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/app/sprechtag/{kopie["id"]}")).StatusCode);
        Assert.Single((await Testserver.HolenAsync(admin, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
    }

    [Fact]
    public void Phasen_folgen_den_Zeitpunkten()
    {
        var k = new StKonfig
        {
            Name = "x", Dauer = 10, Abschnitte = new() { new StAbschnitt("A", "2026-10-30", "15:00", "17:00") },
            Phase1Von = "2026-10-15T08:00", Phase1Bis = "2026-10-20T18:00", Phase2Von = "2026-10-21T08:00", Phase2Bis = "2026-10-28T18:00",
        };
        var entwurf = new Sprechtag(1, "2026/2027", "2026-08-01", "2027-07-31", k, "", "");
        var t = entwurf with { Veroeffentlicht = "2026-10-01 10:00" };
        Assert.Equal(Sprechtagdienst.Entwurf, Sprechtagdienst.Phase(entwurf, new DateTime(2026, 10, 25)));
        Assert.Equal(Sprechtagdienst.Verwaltung, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 14, 10, 0, 0)));
        Assert.Equal(Sprechtagdienst.Phase1, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 15, 8, 0, 0)));
        Assert.Equal(Sprechtagdienst.Phase1Beendet, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 20, 20, 0, 0)));
        Assert.Equal(Sprechtagdienst.Phase2, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 22)));
        Assert.Equal(Sprechtagdienst.Geschlossen, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 29)));
        Assert.Equal(Sprechtagdienst.Durchfuehrung, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 30, 16, 0, 0)));
        Assert.Equal(Sprechtagdienst.Abgeschlossen, Sprechtagdienst.Phase(t, new DateTime(2026, 10, 30, 17, 30, 0)));
        Assert.Equal(new[] { "15:00", "15:10", "15:20" }, Sprechtagdienst.Bloecke(t, k.Abschnitte[0]).Take(3).Select(z => z.Von));
        Assert.Equal(12, Sprechtagdienst.Bloecke(t, k.Abschnitte[0]).Count);
    }

    [Fact]
    public async Task Verwaltung_Raeume_Abwesenheiten_Einladungen_und_Buchung_der_Eltern()
    {
        await using var s = await Testserver.StartenAsync();
        var uhr = s.Dienst<Uhr>();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var id = await VeroeffentlichtAsync(admin);
        string Url(string pfad = "") => $"/api/app/sprechtag/{id}{pfad}";

        // Lehrkraft: nur eigene Daten; Schüler*innen ihres Unterrichts
        var meine = await Testserver.HolenAsync(lan, Url());
        Assert.Equal("lehrer", meine["rolle"]!.GetValue<string>());
        Assert.Single(meine["lehrkraefte"]!.AsArray());
        Assert.True(meine["lehrkraefte"]![0]!["keinRaum"]!.GetValue<bool>());
        await FehlerAsync(await lan.PutAsJsonAsync(Url("/raum"), new { lehrer = "FIS", abschnitt = 0, raum = "R101" }), HttpStatusCode.Forbidden);
        var raum = s.Dienst<Paketdienst>().Aktuell!.Paket.Elemente.Raeume[0].Kurz;
        await Testserver.JsonAsync(await lan.PutAsJsonAsync(Url("/raum"), new { abschnitt = 0, raum }));
        await Testserver.JsonAsync(await lan.PutAsJsonAsync(Url("/raum"), new { abschnitt = 1, raum }));
        Assert.False((await Testserver.HolenAsync(lan, Url()))["lehrkraefte"]![0]!["keinRaum"]!.GetValue<bool>());
        var kinder = (await Testserver.HolenAsync(lan, Url("/schueler"))).AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        Assert.True(kinder.Count >= 3);
        var fremd = s.Dienst<Paketdienst>().Aktuell!.Schueler.Select(x => x.Id).First(x => !kinder.Contains(x) && !Sprechtagdienst.SchuelerDerLehrkraft(s.Dienst<Paketdienst>().Aktuell, "LAN").Any(y => y.Id == x));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { fremd } }));

        // Verwaltung LK: Einladung und Terminanfrage („Terminiert“), Eltern sehen noch nichts
        Assert.Equal(1, (await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { kinder[0] }, verpflichtend = true })))["anzahl"]!.GetValue<int>());
        Assert.Equal(0, (await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { kinder[0] } })))["anzahl"]!.GetValue<int>());
        await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/termin"), new { abschnitt = 0, beginn = "15:30", ende = "15:40", schuelerId = kinder[1] }));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/termin"), new { abschnitt = 0, beginn = "15:35", ende = "15:45", schuelerId = kinder[2] }));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/termin"), new { abschnitt = 0, beginn = "15:30", ende = "15:40", schuelerId = kinder[2] }));
        var termine = (await Testserver.HolenAsync(lan, Url()))["lehrkraefte"]![0]!["termine"]!.AsArray();
        Assert.Contains(termine, x => x!["statusText"]!.GetValue<string>() == "Terminiert");
        Assert.Contains(termine, x => x!["statusText"]!.GetValue<string>() == "Eingeladen" && x["verpflichtend"]!.GetValue<bool>());
        // mit Terminen nicht mehr bearbeitbar
        Assert.Contains("nicht mehr bearbeitet", await FehlerAsync(await admin.PutAsJsonAsync(Url(), Konfig())));
        var (eltern, _) = await ElternAsync(s, admin, "eltern-a", kinder[0], kinder[1]);
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        var (andere, _) = await ElternAsync(s, admin, "eltern-b", kinder[2]);

        // Abwesenheit: Überschneidung abgelehnt, überschneidende Termine abgesagt
        var abw = await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/abwesenheit"), new { abschnitt = 0, beginn = "15:00", ende = "15:20", anmerkung = "Besprechung" }));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/abwesenheit"), new { abschnitt = 0, beginn = "15:10", ende = "15:30" }));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/abwesenheit"), new { abschnitt = 0, beginn = "14:00", ende = "14:30" }));
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/abwesenheit"), new { abschnitt = 0, beginn = "15:40", ende = "15:50", anmerkung = new string('x', 256) }));
        var frei = (await Testserver.HolenAsync(lan, Url("/frei?abschnitt=0"))).AsArray().Select(x => x!["von"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain("15:00", frei);
        Assert.DoesNotContain("15:30", frei);
        Assert.Contains("15:20", frei);

        // 1. Anmeldephase: Eltern mit Einladung bzw. Anfrage sehen den Sprechtag, andere nicht; Buchung nur über die Einladung
        uhr.Fest = new DateTime(2026, 10, 16, 10, 0, 0);
        Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        Assert.Empty((await Testserver.HolenAsync(andere, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
        var sicht = await Testserver.HolenAsync(eltern, Url());
        Assert.Equal("Buchung nur mit Einladung möglich", sicht["hinweis"]!.GetValue<string>());
        Assert.Single(sicht["einladungen"]!.AsArray());
        var anfrage = sicht["anfragen"]!.AsArray().Single()!;
        Assert.Equal("Versendet", anfrage["statusText"]!.GetValue<string>());
        Assert.Equal(raum, anfrage["raum"]!.GetValue<string>());
        var einladung = sicht["einladungen"]![0]!["id"]!.GetValue<int>();
        Assert.Contains("nur mit einer Einladung", await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:50", schuelerId = kinder[0] })));
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:50", schuelerId = kinder[2], einladungId = einladung }), HttpStatusCode.Forbidden);
        // belegte Zeit (Abwesenheit) bzw. Überschneidung mit dem Termin des Geschwisterkinds abgelehnt
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:00", schuelerId = kinder[0], einladungId = einladung }));
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:50", schuelerId = kinder[0], einladungId = einladung }));
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:20", schuelerId = kinder[0], einladungId = einladung }));
        // Terminanfrage annehmen
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url($"/termin/{anfrage["id"]}/annehmen"), new { }));
        await FehlerAsync(await eltern.PostAsJsonAsync(Url($"/termin/{anfrage["id"]}/ablehnen"), new { }));
        sicht = await Testserver.HolenAsync(eltern, Url());
        Assert.Empty(sicht["anfragen"]!.AsArray());
        Assert.Equal(new[] { "15:30", "15:50" }, sicht["termine"]!.AsArray().Select(x => x!["beginn"]!.GetValue<string>()));
        Assert.All(sicht["termine"]!.AsArray(), x => Assert.Equal("Akzeptiert", x!["statusText"]!.GetValue<string>()));
        // Eltern verschieben in der 1. Phase (Dauer bleibt), fremde Termine nicht
        var t50 = sicht["termine"]![1]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await eltern.PutAsJsonAsync(Url($"/termin/{t50}"), new { abschnitt = 1, beginn = "16:20" }));
        await FehlerAsync(await andere.PutAsJsonAsync(Url($"/termin/{t50}"), new { abschnitt = 1, beginn = "16:30" }), HttpStatusCode.BadRequest);

        // 1. Phase beendet: keine neuen Einladungen
        uhr.Fest = new DateTime(2026, 10, 20, 20, 0, 0);
        Assert.Contains("2. Anmeldephase", await FehlerAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { kinder[2] } })));
        // 2. Phase: alle Eltern sehen ihn und buchen frei bei Lehrkräften des Kindes
        uhr.Fest = new DateTime(2026, 10, 22, 10, 0, 0);
        var b = await Testserver.HolenAsync(andere, Url());
        Assert.True(b["freiBuchbar"]!.GetValue<bool>());
        Assert.Contains(b["kinder"]![0]!["lehrkraefte"]!.AsArray(), l => l!["kurz"]!.GetValue<string>() == "LAN");
        await Testserver.JsonAsync(await andere.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 1, beginn = "16:10", schuelerId = kinder[2] }));
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 1, beginn = "16:10", schuelerId = kinder[1] }));

        // Abwesenheit sagt den Termin 16:10 ab
        await Testserver.JsonAsync(await admin.PostAsJsonAsync(Url("/abwesenheit"), new { lehrer = "LAN", abschnitt = 1, beginn = "16:10", ende = "16:30" }));
        Assert.Empty((await Testserver.HolenAsync(andere, Url()))["termine"]!.AsArray());
        Assert.Single((await Testserver.HolenAsync(eltern, Url()))["termine"]!.AsArray());
        // Abwesenheit löschen
        Assert.Equal(HttpStatusCode.OK, (await lan.DeleteAsync(Url($"/abwesenheit/{abw["id"]}"))).StatusCode);

        // Absage durch die Eltern; die Lehrkraft erhält eine Mitteilung
        var rest = (await Testserver.HolenAsync(eltern, Url()))["termine"]![0]!["id"]!.GetValue<int>();
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url($"/termin/{rest}/absagen"), new { grund = "krank" }));
        Assert.Empty((await Testserver.HolenAsync(eltern, Url()))["termine"]!.AsArray());
        Assert.Contains("Termin abgesagt", await PostAsync(lan));

        // abgeschlossen: Eltern sehen ihn nicht mehr
        uhr.Fest = new DateTime(2026, 10, 31, 10, 0, 0);
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag"))["sprechtage"]!.AsArray());
    }

    [Fact]
    public async Task Rechte_der_Lehrkraefte_und_Buchungsregeln()
    {
        await using var s = await Testserver.StartenAsync();
        var uhr = s.Dienst<Uhr>();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var id = await VeroeffentlichtAsync(admin, Konfig(
            rechte: new { raeumeWaehlen = false, abwesenheitenEintragen = false, termineBearbeiten = false, termineBuchen = false },
            regeln: new { maxBloeckeProSchueler = 1, gesperrteBloecke = 1, bloeckeBisPause = 2 }));
        string Url(string pfad = "") => $"/api/app/sprechtag/{id}{pfad}";
        var rechte = (await Testserver.HolenAsync(lan, Url()))["rechte"]!;
        Assert.False(rechte["raeume"]!.GetValue<bool>());
        Assert.True(rechte["einladen"]!.GetValue<bool>());
        var kinder = (await Testserver.HolenAsync(lan, Url("/schueler"))).AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        await FehlerAsync(await lan.PutAsJsonAsync(Url("/raum"), new { abschnitt = 0, raum = "R1" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/abwesenheit"), new { abschnitt = 0, beginn = "15:00", ende = "15:10" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await lan.PostAsJsonAsync(Url("/termin"), new { abschnitt = 0, beginn = "15:00", ende = "15:10", schuelerId = kinder[0] }), HttpStatusCode.Forbidden);
        // Einladen dürfen Lehrkräfte immer; die Administration bucht und bearbeitet für sie
        await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { kinder[0] } }));
        var termin = (await Testserver.JsonAsync(await admin.PostAsJsonAsync(Url("/termin"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:00", ende = "15:10", schuelerId = kinder[1] })))["id"]!.GetValue<int>();
        await FehlerAsync(await lan.PutAsJsonAsync(Url($"/termin/{termin}"), new { beginn = "15:20", ende = "15:30" }), HttpStatusCode.Forbidden);
        await FehlerAsync(await lan.PostAsJsonAsync(Url($"/termin/{termin}/absagen"), new { }), HttpStatusCode.Forbidden);
        await FehlerAsync(await admin.PutAsJsonAsync(Url($"/termin/{termin}"), new { beginn = "15:05", ende = "15:15" }));
        await Testserver.JsonAsync(await admin.PutAsJsonAsync(Url($"/termin/{termin}"), new { beginn = "15:10", ende = "15:20" }));

        uhr.Fest = new DateTime(2026, 10, 22, 10, 0, 0);
        var (eltern, _) = await ElternAsync(s, admin, "eltern-a", kinder[0]);
        string[] Frei(JsonNode n) => n.AsArray().Select(x => x!["von"]!.GetValue<string>()).ToArray();
        var frei = Frei(await Testserver.HolenAsync(eltern, Url($"/frei?lehrer=LAN&abschnitt=0&schuelerId={kinder[0]}")));
        // 15:10 belegt
        Assert.DoesNotContain("15:10", frei);
        Assert.Contains("15:00", frei);
        // höchstens zwei Blöcke am Stück: 15:20 schlösse an 15:10 an (2 Blöcke) – erlaubt; nach Buchung 15:20 wäre 15:30 der dritte
        await Testserver.JsonAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 0, beginn = "15:20", schuelerId = kinder[0] }));
        var (eltern2, _) = await ElternAsync(s, admin, "eltern-b", kinder[2]);
        var frei2 = Frei(await Testserver.HolenAsync(eltern2, Url($"/frei?lehrer=LAN&abschnitt=0&schuelerId={kinder[2]}")));
        Assert.DoesNotContain("15:30", frei2);
        Assert.DoesNotContain("15:00", frei2);
        Assert.Contains("15:40", frei2);
        // höchstens ein Block je Kind und Lehrkraft
        Assert.Empty(Frei(await Testserver.HolenAsync(eltern, Url($"/frei?lehrer=LAN&abschnitt=1&schuelerId={kinder[0]}"))));
        await FehlerAsync(await eltern.PostAsJsonAsync(Url("/buchen"), new { lehrer = "LAN", abschnitt = 1, beginn = "16:30", schuelerId = kinder[0] }));
    }

    [Fact]
    public async Task Startkarten_und_Benachrichtigungen_bei_Phasenbeginn()
    {
        await using var s = await Testserver.StartenAsync();
        var uhr = s.Dienst<Uhr>();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var id = await VeroeffentlichtAsync(admin);
        string Url(string pfad = "") => $"/api/app/sprechtag/{id}{pfad}";
        // Veröffentlichen benachrichtigt die Lehrkräfte
        Assert.Contains("veröffentlicht", await PostAsync(lan));
        var karte = (await Testserver.HolenAsync(lan, "/api/app/sprechtag/start")).AsArray().Single()!;
        Assert.Equal("Verwaltung LK", karte["phaseText"]!.GetValue<string>());
        Assert.Equal("angekuendigt", karte["status"]!.GetValue<string>());
        Assert.Equal("2026-10-30", karte["datum"]!.GetValue<string>());

        var kinder = (await Testserver.HolenAsync(lan, Url("/schueler"))).AsArray().Select(x => x!["id"]!.GetValue<int>()).ToList();
        await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/einladung"), new { schuelerIds = new[] { kinder[0] } }));
        var (eltern, _) = await ElternAsync(s, admin, "eltern-a", kinder[0]);
        Assert.Empty((await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray());
        var d = s.Dienst<Sprechtagdienst>();
        var stand = s.Dienst<Paketdienst>().Aktuell;
        // Raumerinnerung: 1 Tag vor der 1. Anmeldephase, nur einmal
        Assert.True(d.Aufgaben(stand) > 0);
        Assert.Equal(0, d.Aufgaben(stand));
        // 1. Anmeldephase: Einladung wird jetzt versendet (einmal)
        uhr.Fest = new DateTime(2026, 10, 15, 9, 0, 0);
        Assert.Equal(1, d.Aufgaben(stand));
        Assert.Equal(0, d.Aufgaben(stand));
        Assert.Contains("Einladung zum Sprechtag", await PostAsync(eltern));
        var ek = (await Testserver.HolenAsync(eltern, "/api/app/sprechtag/start")).AsArray().Single()!;
        Assert.True(ek["buchbar"]!.GetValue<bool>());
        Assert.Equal(1, ek["einladungen"]!.GetValue<int>());
        Assert.Single((await Testserver.HolenAsync(eltern, "/api/app/heute"))["sprechtage"]!.AsArray());
        // 2. Phase: Hinweis auf die ungenutzte Einladung
        uhr.Fest = new DateTime(2026, 10, 22, 9, 0, 0);
        Assert.True(d.Aufgaben(stand) > 0);
        // Abschluss: unbeantwortete Terminanfragen werden abgesagt
        uhr.Fest = new DateTime(2026, 10, 22, 10, 0, 0);
        var tid = (await Testserver.JsonAsync(await lan.PostAsJsonAsync(Url("/termin"), new { abschnitt = 0, beginn = "15:00", ende = "15:10", schuelerId = kinder[1] })))["id"]!.GetValue<int>();
        uhr.Fest = new DateTime(2026, 10, 31, 10, 0, 0);
        d.Aufgaben(stand);
        Assert.Equal("abgesagt", d.Termin(tid)!.Status);
    }

    [Fact]
    public void Oberflaeche_nach_Vorlage()
    {
        string Datei(params string[] teile) => File.ReadAllText(Path.Combine(new[] { Wurzel(), "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
        var js = Datei("js", "ansichten", "sprechtag.js");
        var css = Datei("css", "sprechtag.css");
        foreach (var text in new[] { "Neuer Sprechtag", "Schuljahr auswählen", "Statusinformationen", "1. Allgemeine Informationen", "2. Zeiträume festlegen",
            "3. Buchungsregeln und Rechte", "Sprechtag in Abschnitte gliedern", "Anmeldephasen festlegen", "Berechtigungen für Lehrkräfte", "Buchungsregeln",
            "Veröffentlichen", "Lehrkräfte suchen", "Kein Raum", "Keine Termine", "Einladung senden", "Termin buchen", "Abwesenheit eintragen", "Meine Termine",
            "Terminanfragen", "Ihre bestätigten Gesprächstermine.", "Unbekannter Raum", "Als Entwurf gespeichert", "Gebäude | Raum" })
            Assert.Contains(text, js);
        // Schüler*innen haben keinen Zugriff (Menü nur für Kollegium und Eltern)
        Assert.Contains("if (k || b.rolle === 'eltern') m.push({ id: 'sprechtag'", Datei("js", "app.js"));
        // Phasenpillen und Grunddesign (Schrift nur Regular/Medium)
        Assert.Contains(".st-pille.entwurf { background: #e8efff; color: #2f5bea; }", css);
        Assert.Contains(".st-pille.durchfuehrung, .st-pille.gruen { background: #e2f6ec; color: #16794a; }", css);
        Assert.DoesNotMatch(@"font-weight:\s*[6-9]00", css);
    }

    /// <summary>Betreffzeilen des Posteingangs.</summary>
    private static async Task<string> PostAsync(HttpClient c) =>
        string.Join(" | ", (await Testserver.HolenAsync(c, "/api/app/mitteilungen")).AsArray().Select(x => x!["betreff"]?.GetValue<string>() ?? ""));

    private static string Wurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
