using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Leitfaden Klassenleitung (Kapitel 2–6):
/// <list type="bullet">
/// <item><c>GET /api/app/schuelergruppen?klasse=&amp;schueler=&amp;fach=</c> – Unterricht → Schülergruppen; <c>POST /api/app/unterrichtklassen/{u}/gruppen</c> – neue Schülergruppe.</item>
/// <item><c>GET|PUT /api/app/schuelergruppen/matrix</c> – Schüler-Gruppen-Zuordnung (Auswahlkriterien Klassen, Schüler*innen, Fächer; Zellen mit Zeiträumen).</item>
/// <item><c>GET /api/app/schueler/{id}/kontaktdaten</c>, <c>PUT …/adresse</c>, <c>POST|PUT|DELETE …/kontakte</c> – Kontaktdaten.</item>
/// <item><c>GET|POST|DELETE /api/app/schueler/{id}/bild</c>, <c>GET /api/app/schueler/bilder</c>, <c>POST /api/app/schueler/bildimport?klasse=</c> – Bilder.</item>
/// <item><c>GET|POST /api/app/kennwortblatt</c> – Kennwort-Blatt (PDF) einer Klasse.</item>
/// <item><c>POST /api/app/kennwort-vergessen</c>, <c>POST /api/app/kennwort-zuruecksetzen</c> – Kennwort vergessen (ohne Anmeldung).</item>
/// <item><c>POST /api/app/registrieren</c>, <c>GET|POST /api/app/registrieren/{token}</c> – Selbstregistrierung der Eltern und Betriebe (ohne Anmeldung).</item>
/// <item><c>GET /api/app/admin/postausgang</c> – versendete E-Mails (Administration).</item>
/// </list>
/// </summary>
public static class KlassenleitungApi
{
    public sealed record MatrixDaten(List<MatrixAenderung>? Aenderungen);
    public sealed record AdresseDaten(string? Email, string? Telefon, string? Mobil);
    public sealed record KennwortblattDaten(string? Klasse, List<int>? SchuelerIds);
    public sealed record VergessenDaten(string? Login, string? Email);
    public sealed record TokenDaten(string? Token);
    public sealed record RegistrierungDaten(string? Email);
    public sealed record KontoDaten(string? Login, string? Passwort);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    private static string Adresse(HttpContext c) => $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";

    private static List<string> Liste(string? t) => (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task<(byte[] Daten, string Name)> Datei(HttpContext c, int max, string fehlt)
    {
        if (!c.Request.HasFormContentType) throw new ArgumentException(fehlt);
        var form = await c.Request.ReadFormAsync();
        var datei = form.Files.FirstOrDefault() ?? throw new ArgumentException(fehlt);
        if (datei.Length > max) throw new ArgumentException($"Die Datei ist zu groß (höchstens {max / 1024 / 1024} MB).");
        using var ms = new MemoryStream();
        await datei.CopyToAsync(ms);
        return (ms.ToArray(), datei.FileName);
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        // ---------------------------------------------------------------- Schülergruppen und Matrix
        api.MapGet("/schuelergruppen", (string klasse, int? schueler, string? fach, HttpContext c, Paketdienst p, Unterrichtsgruppen ug) =>
            Results.Json(ug.Gruppenliste(Ich(c), Paket(p), klasse, schueler, fach)));
        api.MapPost("/unterrichtklassen/{unterricht:int}/gruppen", (int unterricht, UnterrichtKlassenApi.GruppeDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = ug.GruppeAnlegen(b, Paket(p), unterricht, d.Name, d.Von, d.Bis);
            prot.Schreiben(b.Login, "Schülergruppe angelegt", $"Unterricht {unterricht}: {d.Name}");
            return Results.Json(res);
        });
        api.MapGet("/schuelergruppen/matrix", (string? klassen, string? schueler, string? faecher, HttpContext c, Paketdienst p, Unterrichtsgruppen ug) =>
            Results.Json(ug.Matrix(Ich(c), Paket(p), Liste(klassen), Liste(schueler).Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0), Liste(faecher))));
        api.MapPut("/schuelergruppen/matrix", (MatrixDaten d, HttpContext c, Paketdienst p, Unterrichtsgruppen ug, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = ug.MatrixSpeichern(b, Paket(p), d.Aenderungen ?? new());
            prot.Schreiben(b.Login, "Schüler-Gruppen-Zuordnung gespeichert", $"{d.Aenderungen?.Count ?? 0} Änderungen");
            return Results.Json(res);
        });

        // ---------------------------------------------------------------- Kontaktdaten
        api.MapGet("/schueler/{id:int}/kontaktdaten", (int id, HttpContext c, Paketdienst p, Schuelerkontakte k) => Results.Json(k.Kontaktdaten(Ich(c), Paket(p), id)));
        api.MapPut("/schueler/{id:int}/adresse", (int id, AdresseDaten d, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            k.AdresseSpeichern(b, Paket(p), id, d.Email, d.Telefon, d.Mobil);
            prot.Schreiben(b.Login, "Adresse geändert", $"Schüler*in {id}");
            return Results.Json(k.Kontaktdaten(b, Paket(p), id));
        });
        api.MapPost("/schueler/{id:int}/kontakte", (int id, KontaktEingabe d, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = k.KontaktSpeichern(b, Paket(p), id, d with { Id = null });
            prot.Schreiben(b.Login, "Kontakt angelegt", $"Schüler*in {id}");
            return Results.Json(res);
        });
        api.MapPut("/schueler/{id:int}/kontakte/{kontakt:int}", (int id, int kontakt, KontaktEingabe d, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            var res = k.KontaktSpeichern(b, Paket(p), id, d with { Id = kontakt });
            prot.Schreiben(b.Login, "Kontakt geändert", $"Schüler*in {id}");
            return Results.Json(res);
        });
        api.MapDelete("/schueler/{id:int}/kontakte/{kontakt:int}", (int id, int kontakt, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            k.KontaktLoeschen(b, Paket(p), id, kontakt);
            prot.Schreiben(b.Login, "Kontakt gelöscht", $"Schüler*in {id}");
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Bilder
        api.MapGet("/schueler/bilder", (HttpContext c, Schuelerkontakte k) =>
        {
            var b = Ich(c);
            return Results.Json(k.MitBild().Where(id => Schuelerkontakte.DarfSehen(b, id)).OrderBy(x => x).ToList());
        });
        api.MapGet("/schueler/{id:int}/bild", (int id, HttpContext c, Schuelerkontakte k) =>
        {
            var bild = k.Bild(Ich(c), id);
            if (bild is null) return Results.NotFound();
            c.Response.Headers.CacheControl = "private, no-cache";
            return Results.File(bild.Value.Daten, bild.Value.Typ);
        });
        api.MapPost("/schueler/{id:int}/bild", async (int id, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            var (daten, name) = await Datei(c, Schuelerkontakte.MaxBild, "Bitte eine Bilddatei wählen.");
            k.BildHochladen(b, Paket(p), id, daten, name);
            prot.Schreiben(b.Login, "Bild hochgeladen", $"Schüler*in {id}");
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/schueler/{id:int}/bild", (int id, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            k.BildLoeschen(b, Paket(p), id);
            prot.Schreiben(b.Login, "Bild gelöscht", $"Schüler*in {id}");
            return Results.Json(new { ok = true });
        });
        api.MapPost("/schueler/bildimport", async (string klasse, HttpContext c, Paketdienst p, Schuelerkontakte k, Protokoll prot) =>
        {
            var b = Ich(c);
            var (daten, _) = await Datei(c, 50 * 1024 * 1024, "Bitte ein ZIP-Archiv wählen.");
            var res = k.Bildimport(b, Paket(p), klasse, daten);
            prot.Schreiben(b.Login, "Bildimport", $"Klasse {klasse}");
            return Results.Json(res);
        });

        // ---------------------------------------------------------------- Kennwort-Blatt
        api.MapGet("/kennwortblatt", (string klasse, HttpContext c, Paketdienst p, Zugangsdienst z) => Results.Json(z.Kontenstand(Ich(c), Paket(p), klasse)));
        api.MapPost("/kennwortblatt", (KennwortblattDaten d, HttpContext c, Paketdienst p, Zugangsdienst z, Einstellungen e, Protokoll prot) =>
        {
            var b = Ich(c);
            var s = Paket(p);
            var klasse = (d.Klasse ?? "").Trim();
            var karten = z.Kennwortblatt(b, s, klasse, d.SchuelerIds);
            if (karten.Count == 0) throw new ArgumentException("Keine Schüler*innen ausgewählt.");
            prot.Schreiben(b.Login, "Kennwort-Blatt erstellt", $"Klasse {klasse}: {karten.Count} Konten");
            var schuljahr = s.SchuljahrVon == DateOnly.MinValue ? "" : $"{s.SchuljahrVon.Year}/{s.SchuljahrVon.Year + 1}";
            var pdf = PdfBericht.Kennwortblatt(e.Schulname, schuljahr, Adresse(c), karten);
            return Results.File(pdf, "application/pdf", $"kennwortblatt-{Sicherheit.Slug(klasse)}.pdf");
        });

        // ---------------------------------------------------------------- Kennwort vergessen, Selbstregistrierung (ohne Anmeldung)
        api.MapPost("/kennwort-vergessen", async (VergessenDaten d, HttpContext c, Zugangsdienst z) =>
        {
            z.KennwortVergessen(d.Login, d.Email, Adresse(c));
            await Task.Delay(300);
            return Results.Json(new { ok = true });
        });
        api.MapPost("/kennwort-zuruecksetzen", (TokenDaten d, HttpContext c, Zugangsdienst z, Protokoll prot) =>
        {
            var login = z.KennwortZuruecksetzen(d.Token, Adresse(c));
            prot.Schreiben(login, "Kennwort über „Kennwort vergessen“ zurückgesetzt");
            return Results.Json(new { ok = true });
        });
        api.MapPost("/registrieren", async (RegistrierungDaten d, HttpContext c, Zugangsdienst z) =>
        {
            z.RegistrierungAnfordern(d.Email, Adresse(c));
            await Task.Delay(300);
            return Results.Json(new { ok = true });
        });
        api.MapGet("/registrieren/{token}", (string token, Zugangsdienst z) => Results.Json(z.Registrierung(token)));
        api.MapPost("/registrieren/{token}", (string token, KontoDaten d, Zugangsdienst z, Protokoll prot) =>
        {
            var b = z.RegistrierungAbschliessen(token, d.Login, d.Passwort);
            prot.Schreiben(b.Login, "Selbstregistrierung", b.Email);
            return Results.Json(new { ok = true, login = b.Login });
        });
        api.MapGet("/admin/postausgang", (HttpContext c, Maildienst m) =>
        {
            var b = Ich(c);
            if (!b.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
            return Results.Json(new { eingerichtet = m.Eingerichtet, mails = m.Ausgang() });
        });
    }
}
