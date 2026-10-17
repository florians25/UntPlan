using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Klassenbuch → Offene Stunden: gehaltene Stunden mit Stand von Abwesenheitskontrolle und Lehrstoff.
/// <list type="bullet">
/// <item><c>GET /api/app/klassenbuch/offene-stunden</c> – Zeitbereich (vordefiniert oder individuell), Filter Lehrkraft/Klasse, Kennzahlen, Zeilen.</item>
/// <item><c>PUT /api/app/klassenbuch/offene-stunden/zeitbereich</c> – gewählten Zeitbereich für den Benutzer speichern.</item>
/// </list>
/// Rechte siehe <see cref="OffeneStunden"/>.
/// </summary>
public static class OffeneStundenApi
{
    public sealed record ZeitbereichDaten(string? Bereich, DateOnly? Von, DateOnly? Bis);

    private static Benutzer Kollegium(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium.");
    }

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
    private static DateOnly? D(string? s) => DateOnly.TryParse(s, out var d) ? d : null;
    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/klassenbuch/offene-stunden");

        api.MapGet("", (string? bereich, string? von, string? bis, string? lehrer, string? klasse, string? art,
            HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Einstellungen e, Uhr uhr, Klassenbuchdienst kb) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var alle = OffeneStunden.DarfAlle(b);
            // Startwert: Vorgabe der Klassenbuch-Einstellungen („Offene Stunden anzeigen ab“, Standard: Beginn der Woche); Lehrkräfte ihr gespeicherter Zeitbereich
            var einst = kb.Einstellungen();
            var vorgabe = einst.OffenVorgabe(uhr.Heute);
            var (gBereich, gVon, gBis) = alle || e.Lesen($"offene_stunden.zeitbereich.{b.Id}") is not { Length: > 0 } ? vorgabe : OffeneStunden.Gespeichert(e, b.Id);
            var wahl = OffeneStunden.Gueltig(bereich) ? bereich! : gBereich;
            var (v, bi) = OffeneStunden.Zeitraum(wahl, uhr.Heute, s.SchuljahrVon, D(von) ?? gVon, D(bis) ?? gBis);
            if (wahl == "individuell" && bi > uhr.Heute) bi = uhr.Heute;

            var k = Leer(klasse);
            if (k != null && !s.Paket.Elemente.Klassen.Any(x => string.Equals(x.Kurz, k, StringComparison.OrdinalIgnoreCase))) k = null;
            var klassenleitung = b.LehrerKuerzel is string ich
                ? s.Paket.Elemente.Klassen.Where(x => x.Klassenleitung.Contains(ich, StringComparer.OrdinalIgnoreCase)).Select(x => x.Kurz).ToList()
                : new List<string>();
            var lehrerWahl = (lehrer ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var erweitert = alle || (k != null && Klassenbuchdienst.IstKlassenleitung(b, s, k));

            var zeilen = kv.Stunden(s, b, v, bi, erweitert ? lehrerWahl : Array.Empty<string>(), k);
            // ohne Abwesenheitskontrolle (Klassenbuch-Einstellungen) zählt nur der Lehrstoff
            if (!einst.Abwesenheitskontrolle) zeilen = zeilen.Select(z => z with { Geprueft = true }).ToList();
            var offen = zeilen.Count(z => einst.Offen(z.Geprueft, z.Lehrstoff));
            var ohneKontrolle = zeilen.Count(z => !z.Geprueft);
            var ohneLehrstoff = zeilen.Count(z => !z.Lehrstoff);
            var gefiltert = (art ?? "offen") switch
            {
                "kontrolle" => zeilen.Where(z => !z.Geprueft),
                "lehrstoff" => zeilen.Where(z => !z.Lehrstoff),
                "alle" => zeilen,
                _ => zeilen.Where(z => einst.Offen(z.Geprueft, z.Lehrstoff)),
            };
            return Results.Json(new
            {
                bereich = wahl, von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), heute = Plandienst.Iso(uhr.Heute),
                bereiche = OffeneStunden.Bereiche.Select(x => new { id = x.Id, text = x.Text }),
                alle, gespeichert = !alle, klassenleitung, klasse = k,
                abwesenheitskontrolle = einst.Abwesenheitskontrolle, offenFuer = einst.Abwesenheitskontrolle ? einst.OffenFuer : "lehrstoff",
                lehrer = erweitert ? lehrerWahl.Where(l => s.Lehrer.ContainsKey(l)).ToList() : b.LehrerKuerzel is string l2 ? new List<string> { l2 } : new List<string>(),
                gesamt = zeilen.Count, offen, ohneKontrolle, ohneLehrstoff,
                vollstaendig = zeilen.Count == 0 ? 100 : (int)Math.Round((zeilen.Count - offen) * 100.0 / zeilen.Count),
                zeilen = gefiltert.ToList(),
            });
        });

        api.MapPut("/zeitbereich", (ZeitbereichDaten d, HttpContext c, Einstellungen e) =>
        {
            var b = Kollegium(c);
            OffeneStunden.Speichern(e, b.Id, d.Bereich ?? "", d.Von, d.Bis);
            return Results.Json(new { ok = true });
        });
    }
}
