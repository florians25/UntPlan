using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Rechte-Modell: Standardprofile der Basisrollen (bisheriges Verhalten), Zusatzrechte (abwärtskompatibel), eigene Rollen als Profile,
/// Überschreibungen je Konto, Vorschau der effektiven Rechte und die Umstellung der Endpunkte auf <see cref="Rechtedienst.Darf"/>.
/// </summary>
public class RechteModellTests
{
    private static Benutzer Konto(string rolle, params string[] zusatz) =>
        new(1, "x", "X", rolle, rolle is "lehrer" or "leitung" ? "LAN" : "", "", false, false, "", "", null) { Zusatzrechte = zusatz.ToList() };

    [Fact]
    public void Standardprofile_entsprechen_den_bisherigen_Rollen()
    {
        var admin = Konto(Rollen.Admin);
        Assert.All(Recht.Katalog, r => Assert.True(Rechtedienst.Darf(admin, r.Id)));
        var lehrer = Konto(Rollen.Lehrer);
        Assert.True(Rechtedienst.Darf(lehrer, Recht.KlassenbuchEigene));
        Assert.True(Rechtedienst.Darf(lehrer, Recht.PruefungAnlegen));
        Assert.False(Rechtedienst.DarfLesen(lehrer, Recht.KlassenbuchAlle));
        Assert.False(Rechtedienst.Darf(lehrer, Recht.MitteilungAlle));
        Assert.False(Rechtedienst.Darf(lehrer, Recht.AdminBenutzer));
        var leitung = Konto(Rollen.Leitung);
        Assert.True(Rechtedienst.Darf(leitung, Recht.KlassenbuchAlle));
        Assert.True(Rechtedienst.Darf(leitung, Recht.NachrichtenPflegen));
        Assert.False(Rechtedienst.Darf(leitung, Recht.AdminImport));
        Assert.False(Rechtedienst.Darf(leitung, Recht.SprechtagVerwalten));
        var eltern = Konto(Rollen.Eltern);
        Assert.True(Rechtedienst.Darf(eltern, Recht.MitteilungSenden));
        Assert.False(Rechtedienst.Darf(eltern, Recht.PlanLehrer));
        // bisherige Hilfsfunktionen bleiben gleich
        Assert.True(leitung.DarfNachrichten);
        Assert.False(lehrer.DarfNachrichten);
        Assert.True(Schuelerverwaltung.Verwalten(leitung));
        Assert.False(OffeneStunden.DarfAlle(lehrer));
    }

    [Fact]
    public void Zusatzrechte_wirken_im_neuen_Modell_weiter()
    {
        var l = Konto(Rollen.Lehrer, Zusatzrechte.Nachrichten, Zusatzrechte.KlassenbuchAlle);
        Assert.True(Rechtedienst.Darf(l, Recht.MitteilungAlle));
        Assert.True(Rechtedienst.Darf(l, Recht.NachrichtenPflegen));
        Assert.True(l.DarfNachrichten);
        Assert.True(Rechtedienst.DarfLesen(l, Recht.KlassenbuchAlle));
        Assert.False(Rechtedienst.Darf(l, Recht.KlassenbuchAlle)); // nur Lesen
        Assert.True(OffeneStunden.DarfAlle(l));
        // Zusatzrechte gelten nur für das Kollegium
        Assert.False(Rechtedienst.Darf(Konto(Rollen.Eltern, Zusatzrechte.Nachrichten), Recht.MitteilungAlle));
    }

    [Fact]
    public void Ueberschreibungen_gewaehren_und_entziehen_Kollegiumsrechte_nur_im_Kollegium()
    {
        var l = Konto(Rollen.Lehrer);
        var v = Rechtedienst.Vorschau(l, null, new Dictionary<string, int> { [Recht.AdminImport] = Stufe.Ja, [Recht.PruefungAnlegen] = Stufe.Nein });
        Assert.Equal(Stufe.Ja, v.First(x => x.Recht == Recht.AdminImport).Effektiv);
        Assert.Equal("gewährt", v.First(x => x.Recht == Recht.AdminImport).Quelle);
        Assert.Equal(Stufe.Nein, v.First(x => x.Recht == Recht.PruefungAnlegen).Effektiv);
        Assert.Equal("entzogen", v.First(x => x.Recht == Recht.PruefungAnlegen).Quelle);
        var e = Rechtedienst.Vorschau(Konto(Rollen.Eltern), null, new Dictionary<string, int> { [Recht.KlassenbuchAlle] = Stufe.Ja });
        Assert.Equal(Stufe.Nein, e.First(x => x.Recht == Recht.KlassenbuchAlle).Effektiv);
        // Ja/Nein-Rechte kennen kein „Lesen“
        Assert.Equal(Stufe.Ja, Recht.Begrenzen(Recht.AdminImport, Stufe.Lesen));
        Assert.Equal(Stufe.Lesen, Recht.Begrenzen(Recht.KlassenbuchAlle, Stufe.Lesen));
    }

    [Fact]
    public async Task Rollen_anlegen_kopieren_zuweisen_und_Endpunkte_pruefen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lehrer = await s.AlsAsync("lan", "lehrer", "LAN");
        var id = s.Dienst<Benutzerdienst>().NachLogin("lan")!.Id;
        var klasse = s.Dienst<Paketdienst>().Aktuell!.Klassen.Keys.First();
        object Klausur() => new { datum = "2026-10-15", stundeVon = 3, stundeBis = 4, klassen = new[] { klasse }, raeume = Array.Empty<string>(), aufsicht = Array.Empty<string>(), titel = "Test" };

        // Rechte und Rollen: nur Administration
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/rechte/katalog")).StatusCode);
        var katalog = await Testserver.HolenAsync(admin, "/api/app/admin/rechte/katalog");
        Assert.Contains(katalog["profile"]!.AsArray(), p => p!["id"]!.GetValue<string>() == "lehrer" && p["system"]!.GetValue<bool>());
        Assert.True(katalog["rechte"]!.AsArray().Count >= 30);

        // Kopie der Lehrkraft ohne „Prüfungen anlegen“
        var kopie = await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/admin/rechte/profile/lehrer/kopieren", new { name = "Referendar*in" }));
        var pid = kopie["id"]!.GetValue<string>();
        Assert.Equal("lehrer", kopie["basis"]!.GetValue<string>());
        var rechte = kopie["rechte"]!.AsObject().ToDictionary(x => x.Key, x => x.Value!.GetValue<int>());
        rechte[Recht.PruefungAnlegen] = 0;
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/admin/rechte/profile/{pid}", new { name = "Referendar*in", beschreibung = "ohne Prüfungen", rechte }));
        // Zuweisung an ein Konto einer anderen Basisrolle: abgelehnt
        var leitungId = s.Dienst<Benutzerdienst>().Anlegen("leit", "start-passwort", Rollen.Leitung, "MUE").Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/app/admin/rechte/benutzer/{leitungId}", new { profil = pid })).StatusCode);

        await Testserver.JsonAsync(await lehrer.PostAsJsonAsync("/api/app/meldungen/klausur", Klausur()));
        var v = await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/admin/rechte/benutzer/{id}", new { profil = pid }));
        Assert.Equal(pid, v["profil"]!.GetValue<string>());
        Assert.Equal(0, v["rechte"]!.AsArray().First(x => x!["recht"]!.GetValue<string>() == Recht.PruefungAnlegen)!["effektiv"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsJsonAsync("/api/app/meldungen/klausur", Klausur())).StatusCode);
        // eigene Rechte für die Oberfläche
        var ich = await Testserver.HolenAsync(lehrer, "/api/app/rechte");
        Assert.Null(ich["rechte"]![Recht.PruefungAnlegen]);
        Assert.Equal(2, ich["rechte"]![Recht.KlassenbuchEigene]!.GetValue<int>());

        // Überschreibung: Prüfungen wieder erlauben, Benutzerverwaltung und Import gewähren
        await Testserver.JsonAsync(await admin.PutAsJsonAsync($"/api/app/admin/rechte/benutzer/{id}", new
        {
            profil = pid, ueberschreibungen = new Dictionary<string, int?> { [Recht.PruefungAnlegen] = 2, [Recht.AdminBenutzer] = 2, [Recht.AdminImport] = 2 },
        }));
        Assert.Equal(HttpStatusCode.OK, (await lehrer.PostAsJsonAsync("/api/app/meldungen/klausur", new { datum = "2026-10-16", stundeVon = 5, stundeBis = 6, klassen = new[] { klasse }, raeume = Array.Empty<string>(), aufsicht = Array.Empty<string>(), titel = "Test 2" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lehrer.GetAsync("/api/app/admin/personen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lehrer.GetAsync("/api/app/admin/schuelerimport/grundlagen")).StatusCode);
        // … aber keine Administrationskonten anlegen oder ändern, keine Rechte und Rollen
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsJsonAsync("/api/app/admin/benutzer", new { login = "boss", passwort = "start-passwort", rolle = "admin", gesperrt = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.PostAsync("/api/app/admin/benutzer/1/passwort", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lehrer.PostAsJsonAsync("/api/app/admin/benutzer", new { login = "neu1", passwort = "start-passwort", rolle = "lehrer", gesperrt = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lehrer.GetAsync("/api/app/admin/rechte/katalog")).StatusCode);

        // Vorschau ohne Speichern
        var vorschau = await Testserver.JsonAsync(await admin.PostAsJsonAsync($"/api/app/admin/rechte/benutzer/{id}/vorschau", new { profil = "", ueberschreibungen = new Dictionary<string, int?>() }));
        Assert.Equal(2, vorschau["rechte"]!.AsArray().First(x => x!["recht"]!.GetValue<string>() == Recht.PruefungAnlegen)!["effektiv"]!.GetValue<int>());
        Assert.Equal(0, vorschau["rechte"]!.AsArray().First(x => x!["recht"]!.GetValue<string>() == Recht.AdminBenutzer)!["effektiv"]!.GetValue<int>());

        // Rolle löschen: Konto erhält wieder das Standardprofil der Basisrolle
        await Testserver.JsonAsync(await admin.DeleteAsync($"/api/app/admin/rechte/profile/{pid}"));
        Assert.Equal("", Rechtedienst.ProfilVon(s.Dienst<Db>(), id));
        // Systemrollen: Administration nicht änderbar, andere zurücksetzbar
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/admin", new { rechte = new Dictionary<string, int> { [Recht.AdminBenutzer] = 0 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/app/admin/rechte/profile/lehrer")).StatusCode);
    }

    [Fact]
    public async Task Systemrolle_aendern_wirkt_auf_Klassenbuch_Abwesenheiten_und_Mitteilungen()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var db = s.Dienst<Db>();
        var bd = s.Dienst<Benutzerdienst>();
        var st = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = bd.Anlegen("leit", "start-passwort", Rollen.Leitung, "MUE");
        var klasse = st.Klassen.Keys.First();
        Assert.True(Klassenbuchdienst.IstKlassenleitung(bd.Holen(leitung.Id)!, st, klasse));
        Assert.True(Klassenbuchdienst.DarfEntschuldigen(bd.Holen(leitung.Id)!, st, klasse));

        // Schulleitung ohne „Klassenbuch aller Klassen“, „entschuldigen“ und „Mitteilungen an alle“
        var profil = Rechtedienst.Profil(db, Rollen.Leitung)!;
        var rechte = new Dictionary<string, int>(profil.Rechte) { [Recht.KlassenbuchAlle] = 0, [Recht.AbwesenheitEntschuldigen] = 0, [Recht.MitteilungAlle] = 0, [Recht.AbwesenheitRueckwirkend] = 0 };
        await Testserver.JsonAsync(await admin.PutAsJsonAsync("/api/app/admin/rechte/profile/leitung", new { rechte }));
        var b = bd.Holen(leitung.Id)!;
        Assert.False(Klassenbuchdienst.IstKlassenleitung(b, st, klasse));
        Assert.False(Klassenbuchdienst.DarfEntschuldigen(b, st, klasse));
        Assert.True(Klassenbuchdienst.FehlzeitenVerwalten(b, st)); // „Abwesenheiten aller Klassen“ bleibt
        Assert.Throws<UnauthorizedAccessException>(() => Klassenbuchdienst.RueckwirkendPruefen(b, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14)));
        Klassenbuchdienst.RueckwirkendPruefen(b, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 14));
        var empfaenger = s.Dienst<Mitteilungsdienst>().Empfaengerliste(b, st);
        Assert.DoesNotContain(empfaenger, x => x.Id == "gruppe:alle");

        // Zurücksetzen stellt die Standardrechte wieder her
        await Testserver.JsonAsync(await admin.PostAsync("/api/app/admin/rechte/profile/leitung/zuruecksetzen", null));
        b = bd.Holen(leitung.Id)!;
        Assert.True(Klassenbuchdienst.IstKlassenleitung(b, st, klasse));
        Assert.Contains(s.Dienst<Mitteilungsdienst>().Empfaengerliste(b, st), x => x.Id == "gruppe:alle");
    }

    [Fact]
    public async Task Buchungen_anderer_stornieren_nur_mit_Recht()
    {
        await using var s = await Testserver.StartenAsync();
        var bd = s.Dienst<Benutzerdienst>();
        var lehrer = bd.Holen(bd.Anlegen("lan", "start-passwort", Rollen.Lehrer, "LAN").Id)!;
        Assert.False(Rechtedienst.Darf(lehrer, Recht.BuchungAndere));
        Rechtedienst.UeberschreibungenSetzen(s.Dienst<Db>(), lehrer.Id, new() { [Recht.BuchungAndere] = 2, [Recht.BuchungRaum] = 0 });
        lehrer = bd.Holen(lehrer.Id)!;
        Assert.True(Rechtedienst.Darf(lehrer, Recht.BuchungAndere));
        Assert.Throws<UnauthorizedAccessException>(() => s.Dienst<Buchungsdienst>().RaumSuche(s.Dienst<Paketdienst>().Aktuell!, lehrer,
            new Zeitangabe(null, new DateOnly(2026, 10, 15), null, null, null, 1, 1), null));
    }
}
