using System.Globalization;
using System.Text.RegularExpressions;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>Öffentlicher Hinweis für die Anmeldeseite (z. B. „Elternsprechtag am …“) – nur allgemeine Informationen.</summary>
public sealed record OeffentlicherHinweis(int Id, string Titel, string Text, string Von, string Bis, bool Aktiv, int Reihenfolge, string Geaendert);

/// <summary>
/// Öffentliche Hinweise auf der Anmeldeseite (DSGVO): Die Verwaltung pflegt Titel, Text, Zeitraum und „anzeigen“; der öffentliche Endpunkt
/// liefert ohne Anmeldung nur freigegebene Hinweise im Zeitraum – reiner Text, keine Links, keine E-Mail-Adressen, keine Namen bzw. Termine
/// einzelner Personen, keine Cookies, kein Tracking, nicht zwischengespeichert.
/// <list type="bullet">
/// <item><c>GET /api/app/hinweise/oeffentlich</c> – ohne Anmeldung: <c>[{id, titel, text, von, bis}]</c>.</item>
/// <item>Recht „Infocenter verwalten“: <c>GET /api/app/hinweise</c>, <c>POST /api/app/hinweise</c>, <c>PUT /api/app/hinweise/{id}</c>, <c>DELETE /api/app/hinweise/{id}</c>.</item>
/// </list>
/// </summary>
public static class HinweisApi
{
    public sealed record HinweisDaten(string? Titel, string? Text, string? Von, string? Bis, bool? Aktiv, int? Reihenfolge);

    private static readonly Regex Link = new(@"(https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[^\s@]+@[^\s@]+\.[a-z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public const int HoechstensOeffentlich = 5;

    private static OeffentlicherHinweis Lesen(System.Data.Common.DbDataReader r) =>
        new(r.Zahl("id"), r.Text("titel"), r.Text("text"), r.Text("von"), r.Text("bis"), r.Wahr("aktiv"), r.Zahl("reihenfolge"), r.Text("geaendert"));

    public static List<OeffentlicherHinweis> Alle(Db db) =>
        db.Liste("SELECT * FROM oeffentliche_hinweise ORDER BY reihenfolge, id DESC", Lesen);

    /// <summary>Freigegebene Hinweise, die heute gelten (von/bis leer = offen), höchstens <see cref="HoechstensOeffentlich"/>.</summary>
    public static List<OeffentlicherHinweis> Oeffentlich(Db db, DateOnly heute)
    {
        var h = Plandienst.Iso(heute);
        return Alle(db).Where(x => x.Aktiv && (x.Von.Length == 0 || string.CompareOrdinal(x.Von, h) <= 0) && (x.Bis.Length == 0 || string.CompareOrdinal(x.Bis, h) >= 0))
            .Take(HoechstensOeffentlich).ToList();
    }

    /// <summary>Eingaben prüfen: Titel 1–120, Text bis 1000 Zeichen, reiner Text ohne HTML, Links und E-Mail-Adressen; Zeitraum gültig.</summary>
    public static (string Titel, string Text, string Von, string Bis) Pruefen(HinweisDaten d)
    {
        var titel = (d.Titel ?? "").Trim();
        var text = (d.Text ?? "").Trim().Replace("\r\n", "\n");
        if (titel.Length is 0 or > 120) throw new ArgumentException("Bitte einen Titel mit höchstens 120 Zeichen eingeben.");
        if (text.Length > 1000) throw new ArgumentException("Der Text ist zu lang (höchstens 1000 Zeichen).");
        foreach (var x in new[] { titel, text })
        {
            if (x.Contains('<') || x.Contains('>')) throw new ArgumentException("Bitte nur Text ohne HTML eingeben.");
            if (Link.IsMatch(x)) throw new ArgumentException("Öffentliche Hinweise dürfen keine Links enthalten.");
            if (Email.IsMatch(x)) throw new ArgumentException("Öffentliche Hinweise dürfen keine E-Mail-Adressen enthalten (keine personenbezogenen Daten).");
        }
        string Datum(string? s, string feld) => string.IsNullOrWhiteSpace(s) ? ""
            : DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? Plandienst.Iso(t)
            : throw new ArgumentException($"Ungültiges Datum bei „{feld}“.");
        var von = Datum(d.Von, "anzeigen ab");
        var bis = Datum(d.Bis, "anzeigen bis");
        if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(bis, von) < 0) throw new ArgumentException("Das Ende des Zeitraums muss nach dem Beginn liegen.");
        return (titel, text, von, bis);
    }

    private static Benutzer Verwalter(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return RechtePruefung.Verlangen(b, Recht.InfocenterVerwalten, meldung: "Öffentliche Hinweise verwalten Administration und Schulleitung.");
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/hinweise");

        // ohne Anmeldung: nur freigegebene, allgemeine Hinweise (keine Angaben zum Konto, keine Cookies nötig)
        api.MapGet("/oeffentlich", (HttpContext c, Db db, Uhr uhr) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            return Results.Json(Oeffentlich(db, uhr.Heute).Select(x => new { x.Id, x.Titel, x.Text, x.Von, x.Bis }));
        }).AllowAnonymous();

        api.MapGet("", (HttpContext c, Db db) =>
        {
            Verwalter(c);
            return Results.Json(Alle(db));
        });

        api.MapPost("", (HinweisDaten d, HttpContext c, Db db, Uhr uhr, Protokoll prot) =>
        {
            var b = Verwalter(c);
            var (titel, text, von, bis) = Pruefen(d);
            var jetzt = uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            long id = 0;
            db.Transaktion(k =>
            {
                Db.Ausfuehren(k, "INSERT INTO oeffentliche_hinweise (titel, text, von, bis, aktiv, reihenfolge, erstellt, geaendert) VALUES (@t, @x, @v, @b, @a, @r, @z, @z)",
                    Db.P("@t", titel), Db.P("@x", text), Db.P("@v", von), Db.P("@b", bis), Db.P("@a", d.Aktiv ?? true), Db.P("@r", d.Reihenfolge ?? 0), Db.P("@z", jetzt));
                id = Db.LetzteId(k);
            });
            prot.Schreiben(b.Login, "Öffentlicher Hinweis angelegt", titel);
            return Results.Json(Alle(db).First(x => x.Id == id));
        });

        api.MapPut("/{id:int}", (int id, HinweisDaten d, HttpContext c, Db db, Uhr uhr, Protokoll prot) =>
        {
            var b = Verwalter(c);
            var alt = Alle(db).FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannter Hinweis.");
            var (titel, text, von, bis) = Pruefen(d);
            db.Ausfuehren("UPDATE oeffentliche_hinweise SET titel = @t, text = @x, von = @v, bis = @b, aktiv = @a, reihenfolge = @r, geaendert = @z WHERE id = @id",
                Db.P("@t", titel), Db.P("@x", text), Db.P("@v", von), Db.P("@b", bis), Db.P("@a", d.Aktiv ?? alt.Aktiv), Db.P("@r", d.Reihenfolge ?? alt.Reihenfolge),
                Db.P("@z", uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), Db.P("@id", id));
            prot.Schreiben(b.Login, "Öffentlicher Hinweis geändert", titel);
            return Results.Json(Alle(db).First(x => x.Id == id));
        });

        api.MapDelete("/{id:int}", (int id, HttpContext c, Db db, Protokoll prot) =>
        {
            var b = Verwalter(c);
            var alt = Alle(db).FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannter Hinweis.");
            db.Ausfuehren("DELETE FROM oeffentliche_hinweise WHERE id = @id", Db.P("@id", id));
            prot.Schreiben(b.Login, "Öffentlicher Hinweis gelöscht", alt.Titel);
            return Results.Json(new { ok = true });
        });
    }
}
