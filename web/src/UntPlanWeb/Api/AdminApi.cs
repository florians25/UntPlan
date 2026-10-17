using System.Text;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Administration:
/// <list type="bullet">
/// <item>Übersicht und Synchronisation (API-Schlüssel, Schul-ID, Paket hochladen).</item>
/// <item>Benutzer und Rückmeldungen.</item>
/// <item>Nachrichten zum Tag (auch Schulleitung).</item>
/// <item>Monitore und Protokoll.</item>
/// </list>
/// </summary>
public static class AdminApi
{
    public sealed record SchulDaten(string Schulname, string SchulId);
    public sealed record BenutzerDaten(string? Login, string? Passwort, string Rolle, string? Element, string? Anzeigename, string? Email, bool Gesperrt, List<int>? SchuelerIds = null,
        List<string>? Zusatzrechte = null, int? PersonId = null);
    public sealed record StammdatenDaten(bool Lehrer, bool Klassen);
    public sealed record NachrichtDaten(DateOnly Von, DateOnly Bis, string Text, string? Zielgruppe, bool Startseite, bool Monitor,
        string? Betreff = null, int? Wochentag = null, int? Reihenfolge = null, bool? Aufgefaechert = null, bool? Oeffentlich = null, bool? Mobil = null);
    public sealed record MonitorDaten(string Name, string? Version, int Tage);
    public sealed record StatusDaten(string Status);

    private static Benutzer Admin(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.");
    }

    /// <summary>Benutzerverwaltung: Administration bzw. Recht „Benutzerverwaltung“.</summary>
    private static Benutzer Benutzerverwaltung(HttpContext c) => RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminBenutzer, meldung: "Nur für die Administration.");

    private static Benutzer Leitung(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return Rechtedienst.Darf(b, Recht.NachrichtenPflegen) ? b : throw new UnauthorizedAccessException("Nur für Schulleitung und Administration.");
    }

    private static object BenutzerJson(Benutzer b) => new
    {
        b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Element, b.Email, b.Gesperrt, b.PasswortAendern, b.Erstellt, b.LetzteAnmeldung,
        b.SchuelerIds, b.Klassen, b.Zusatzrechte, b.PersonId,
    };

    /// <summary>Schülerkonto: Klasse der zugeordneten Person als Element merken (Klassenkonto-Kompatibilität, z. B. iCal, ältere Apps).</summary>
    private static string ElementFuer(BenutzerDaten d, Paketdienst p) =>
        d.Rolle is Rollen.Schueler or Rollen.Eltern && d.SchuelerIds is { Count: > 0 } ids && p.Aktuell?.Schueler.FirstOrDefault(x => x.Id == ids[0]) is { } sch
            ? sch.Klasse : d.Rolle == Rollen.Eltern ? "" : d.Element ?? "";

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/admin");

        // ---------------------------------------------------------------- Übersicht und Synchronisation

        api.MapGet("/uebersicht", (HttpContext c, Einstellungen e, Paketdienst p, Benutzerdienst bd, Rueckmeldungsdienst r) =>
        {
            Admin(c);
            var s = p.Aktuell;
            var basis = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";
            return Results.Json(new
            {
                schulname = e.Schulname,
                schulId = e.SchulId,
                adresse = basis,
                apiSchluesselGesetzt = e.Lesen("api_schluessel") is { Length: > 0 },
                paket = s is null ? null : new
                {
                    s.Paket.Revision, s.Empfangen, erzeugt = s.Paket.Erzeugt, s.Paket.Programmversion, s.Paket.Von, s.Paket.Bis,
                    klassen = s.Paket.Elemente.Klassen.Count, lehrer = s.Paket.Elemente.Lehrer.Count, raeume = s.Paket.Elemente.Raeume.Count,
                    perioden = s.Paket.Perioden.Count, tage = s.Paket.Tage.Count, vertretungen = s.Paket.Vertretungen.Count,
                    schuljahr = $"{s.Paket.Schule.SchuljahrVon} – {s.Paket.Schule.SchuljahrBis}",
                },
                benutzer = bd.Alle().GroupBy(b => b.Rolle).ToDictionary(g => g.Key, g => g.Count()),
                offeneRueckmeldungen = r.Offene().Count,
                version = typeof(AdminApi).Assembly.GetName().Version?.ToString(3),
            });
        });

        api.MapPut("/schule", (SchulDaten d, HttpContext c, Einstellungen e, Protokoll prot) =>
        {
            var b = Admin(c);
            if (string.IsNullOrWhiteSpace(d.Schulname)) throw new ArgumentException("Bitte den Namen der Schule eingeben.");
            var id = Sicherheit.Slug(string.IsNullOrWhiteSpace(d.SchulId) ? d.Schulname : d.SchulId);
            e.Schreiben("schulname", d.Schulname.Trim());
            e.Schreiben("schul_id", id);
            prot.Schreiben(b.Login, "Schuldaten geändert", id);
            return Results.Json(new { ok = true, schulId = id });
        });

        api.MapPost("/apischluessel", (HttpContext c, Einstellungen e, Protokoll prot) =>
        {
            var b = Admin(c);
            var s = e.ApiSchluesselSetzen();
            prot.Schreiben(b.Login, "API-Schlüssel neu erzeugt");
            return Results.Json(new { apiSchluessel = s });
        });

        // Paket manuell hochladen (z. B. „Paket als Datei exportieren“ in UntPlan)
        api.MapPost("/paket", async (HttpContext c, Paketdienst p, Einstellungen e, Protokoll prot) =>
        {
            var b = Admin(c);
            WebPaket? paket;
            if (c.Request.HasFormContentType)
            {
                var form = await c.Request.ReadFormAsync();
                var datei = form.Files.FirstOrDefault() ?? throw new ArgumentException("Bitte eine Datei auswählen.");
                await using var st = datei.OpenReadStream();
                paket = await JsonSerializer.DeserializeAsync<WebPaket>(st, UntPlanWebSchema.Json);
            }
            else paket = await JsonSerializer.DeserializeAsync<WebPaket>(c.Request.Body, UntPlanWebSchema.Json);
            if (paket is null) throw new ArgumentException("Leere Datei.");
            var stand = p.Speichern(paket, revisionPruefen: false);
            if (e.Schulname.Length == 0) e.Schreiben("schulname", stand.Paket.Schule.Name);
            prot.Schreiben(b.Login, "Paket hochgeladen", $"Revision {paket.Revision}");
            return Results.Json(new { ok = true, revision = stand.Paket.Revision });
        });

        // ---------------------------------------------------------------- Benutzer

        api.MapGet("/benutzer", (HttpContext c, Benutzerdienst bd) => { Benutzerverwaltung(c); return Results.Json(bd.Alle().Select(BenutzerJson)); });

        api.MapPost("/benutzer", (BenutzerDaten d, HttpContext c, Benutzerdienst bd, Paketdienst p, Protokoll prot) =>
        {
            var ich = Benutzerverwaltung(c);
            RechtePruefung.AdminSchuetzen(ich, null, d.Rolle);
            var pw = string.IsNullOrEmpty(d.Passwort) ? Sicherheit.Zufall(10) : d.Passwort;
            bd.ZuordnungPruefen(null, d.Rolle, d.Element, d.SchuelerIds, d.PersonId is > 0 ? d.PersonId : null);
            var b = bd.Anlegen(d.Login ?? "", pw, d.Rolle, ElementFuer(d, p), d.Anzeigename ?? "", d.Email ?? "", string.IsNullOrEmpty(d.Passwort));
            if (d.SchuelerIds is { Count: > 0 })
            {
                try { bd.SchuelerZuordnen(b.Id, d.SchuelerIds); }
                catch (ArgumentException) { bd.Loeschen(b.Id); throw; }
                b = bd.Holen(b.Id)!;
            }
            if (d.Zusatzrechte is { Count: > 0 }) bd.ZusatzrechteSetzen(b.Id, d.Zusatzrechte);
            if (d.PersonId is > 0) bd.PersonSetzen(b.Id, d.PersonId);
            b = bd.Holen(b.Id)!;
            prot.Schreiben(ich.Login, "Benutzer angelegt", b.Login);
            return Results.Json(new { benutzer = BenutzerJson(b), passwort = string.IsNullOrEmpty(d.Passwort) ? pw : null });
        });

        api.MapPut("/benutzer/{id:int}", (int id, BenutzerDaten d, HttpContext c, Benutzerdienst bd, Paketdienst p, Protokoll prot) =>
        {
            var ich = Benutzerverwaltung(c);
            RechtePruefung.AdminSchuetzen(ich, bd.Holen(id), d.Rolle);
            if (id == ich.Id && (d.Rolle != Rollen.Admin || d.Gesperrt)) throw new ArgumentException("Sie können sich nicht selbst die Administration entziehen oder sperren.");
            bd.ZuordnungPruefen(id, d.Rolle, d.Element, d.SchuelerIds, d.PersonId is > 0 ? d.PersonId : null);
            bd.Aendern(id, d.Anzeigename ?? "", d.Rolle, ElementFuer(d, p), d.Email ?? "", d.Gesperrt);
            if (d.Zusatzrechte != null) bd.ZusatzrechteSetzen(id, d.Zusatzrechte);
            if (d.PersonId != null) bd.PersonSetzen(id, d.PersonId > 0 ? d.PersonId : null);
            // Zuordnung nur ändern, wenn mitgeschickt; bei anderen Rollen entfällt sie
            if (d.SchuelerIds != null || !(d.Rolle is Rollen.Schueler or Rollen.Eltern)) bd.SchuelerZuordnen(id, d.Rolle is Rollen.Schueler or Rollen.Eltern ? d.SchuelerIds ?? new() : new());
            prot.Schreiben(ich.Login, "Benutzer geändert", bd.Holen(id)?.Login ?? id.ToString());
            return Results.Json(new { ok = true });
        });

        api.MapPost("/benutzer/{id:int}/passwort", (int id, HttpContext c, Benutzerdienst bd, Protokoll prot) =>
        {
            var ich = Benutzerverwaltung(c);
            RechtePruefung.AdminSchuetzen(ich, bd.Holen(id));
            var pw = Sicherheit.Zufall(10);
            bd.PasswortSetzen(id, pw, mussAendern: true);
            prot.Schreiben(ich.Login, "Passwort zurückgesetzt", bd.Holen(id)?.Login ?? id.ToString());
            return Results.Json(new { passwort = pw });
        });

        api.MapDelete("/benutzer/{id:int}", (int id, HttpContext c, Benutzerdienst bd, Protokoll prot) =>
        {
            var ich = Benutzerverwaltung(c);
            RechtePruefung.AdminSchuetzen(ich, bd.Holen(id));
            if (id == ich.Id) throw new ArgumentException("Sie können sich nicht selbst löschen.");
            var login = bd.Holen(id)?.Login ?? id.ToString();
            bd.Loeschen(id);
            prot.Schreiben(ich.Login, "Benutzer gelöscht", login);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/benutzer/stammdaten", (StammdatenDaten d, HttpContext c, Benutzerdienst bd, Paketdienst p, Protokoll prot, Verwaltungsdienst vw) =>
        {
            var ich = Benutzerverwaltung(c);
            var s = p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Stammdaten aus UntPlan übertragen.");
            var neu = bd.AusStammdaten(s.Paket, d.Lehrer, d.Klassen, vw.Text("zuordnung", "leitungKuerzel").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            prot.Schreiben(ich.Login, "Konten aus Stammdaten angelegt", neu.Count.ToString());
            var csv = new StringBuilder("Benutzername;Startpasswort;Rolle;Kürzel;Name\r\n");
            foreach (var n in neu) csv.Append($"{n.Login};{n.Passwort};{Rollen.Name(n.Rolle)};{n.Element};{n.Name}\r\n");
            return Results.Json(new { anzahl = neu.Count, konten = neu.Select(n => new { login = n.Login, passwort = n.Passwort, rolle = Rollen.Name(n.Rolle), element = n.Element, name = n.Name }), csv = csv.ToString() });
        });

        // ---------------------------------------------------------------- Rückmeldungen

        api.MapGet("/rueckmeldungen", (HttpContext c, Rueckmeldungsdienst r) => { Admin(c); return Results.Json(r.Alle()); });
        api.MapPut("/rueckmeldungen/{id}", (string id, StatusDaten d, HttpContext c, Rueckmeldungsdienst r) =>
        {
            Admin(c);
            if (d.Status is not ("offen" or "quittiert" or "abgelehnt")) throw new ArgumentException("Unbekannter Status.");
            r.StatusSetzen(id, d.Status);
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/rueckmeldungen/{id}", (string id, HttpContext c, Rueckmeldungsdienst r) => { Admin(c); r.Loeschen(id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Nachrichten zum Tag

        api.MapGet("/nachrichten", (HttpContext c, Vertretungsplandienst vp) => { Leitung(c); return Results.Json(vp.Nachrichten()); });
        api.MapPost("/nachrichten", (NachrichtDaten d, HttpContext c, Vertretungsplandienst vp) =>
        {
            var b = Leitung(c);
            vp.NachrichtSpeichern(null, d.Von, d.Bis, d.Text, d.Zielgruppe ?? "alle", d.Startseite, d.Monitor, b.Id, d.Betreff, d.Wochentag ?? 0, d.Reihenfolge ?? 10, d.Aufgefaechert ?? true, d.Oeffentlich ?? false, d.Mobil ?? true);
            return Results.Json(new { ok = true });
        });
        api.MapPut("/nachrichten/{id:int}", (int id, NachrichtDaten d, HttpContext c, Vertretungsplandienst vp) =>
        {
            var b = Leitung(c);
            vp.NachrichtSpeichern(id, d.Von, d.Bis, d.Text, d.Zielgruppe ?? "alle", d.Startseite, d.Monitor, b.Id, d.Betreff, d.Wochentag ?? 0, d.Reihenfolge ?? 10, d.Aufgefaechert ?? true, d.Oeffentlich ?? false, d.Mobil ?? true);
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/nachrichten/{id:int}", (int id, HttpContext c, Vertretungsplandienst vp) => { Leitung(c); vp.NachrichtLoeschen(id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Monitore

        api.MapGet("/monitore", (HttpContext c, Monitordienst md) =>
        {
            Admin(c);
            var basis = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";
            return Results.Json(md.Alle().Select(m => new { m.Id, m.Name, m.Version, m.Tage, link = $"{basis}/monitor/{m.Token}" }));
        });
        api.MapPost("/monitore", (MonitorDaten d, HttpContext c, Monitordienst md) => { Admin(c); md.Speichern(null, d.Name, d.Version ?? "schueler", d.Tage); return Results.Json(new { ok = true }); });
        api.MapPut("/monitore/{id:int}", (int id, MonitorDaten d, HttpContext c, Monitordienst md) => { Admin(c); md.Speichern(id, d.Name, d.Version ?? "schueler", d.Tage); return Results.Json(new { ok = true }); });
        api.MapDelete("/monitore/{id:int}", (int id, HttpContext c, Monitordienst md) => { Admin(c); md.Loeschen(id); return Results.Json(new { ok = true }); });

        // ---------------------------------------------------------------- Protokoll

        api.MapGet("/protokoll", (HttpContext c, Protokoll prot) => { Admin(c); return Results.Json(prot.Letzte(300)); });
    }
}
