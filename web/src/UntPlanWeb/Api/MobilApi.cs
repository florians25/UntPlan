using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Endpunkte für UntPlanMobile, die über die Oberfläche von UntPlanWeb hinausgehen.
/// <list type="bullet">
/// <item><c>POST /api/app/mobil/raum</c>: Raum einer eigenen Stunde ersetzen, zusätzlichen Raum hinzufügen oder einen Raum löschen
/// (Raum-Chip in den Stundendetails). Gespeichert wird – wie jede Raumbuchung aus UntPlanWeb – als Rückmeldung „raumbuchung“
/// mit der vollständigen Raumliste der Stunde; bis UntPlan sie übernimmt, steht sie als „vorgemerkt“ auf der Stundenkarte.
/// Je Lehrkraft und Stunde gibt es höchstens eine offene Raumänderung: Eine neue ersetzt die vorige. Ergibt sich wieder die
/// ursprüngliche Raumliste, wird nur zurückgezogen.</item>
/// </list>
/// </summary>
public static class MobilApi
{
    /// <summary>Aktion: ersetzen (alt → raum), hinzufuegen (raum) oder loeschen (raum).</summary>
    public sealed record RaumAenderung(DateOnly Datum, int StundeVon, int? StundeBis, int? Unterricht, List<string>? Klassen, string Aktion, string Raum, string? Alt, string? Titel);

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/mobil");

        api.MapPost("/raum", (RaumAenderung d, HttpContext c, Paketdienst p, Plandienst plan, Rueckmeldungsdienst r, Db db, Uhr uhr, Protokoll prot) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Räume ändern können nur Lehrkräfte.");
            var s = Paket(p);
            var ergebnis = RaumAendern(s, b, d, plan, r, db, uhr);
            prot.Schreiben(b.Login, "Raum geändert (mobil)", $"{Plandienst.Iso(d.Datum)} {d.StundeVon}. Std.: {d.Aktion} {d.Raum} → {string.Join(",", ergebnis.Raeume)}");
            return Results.Json(new { ok = true, raeume = ergebnis.Raeume, zurueckgesetzt = ergebnis.Zurueckgesetzt });
        });
    }

    /// <summary>Raumänderung ausführen; liefert die neue Raumliste der Stunde.</summary>
    public static (List<string> Raeume, bool Zurueckgesetzt) RaumAendern(Stand s, Benutzer b, RaumAenderung d, Plandienst plan, Rueckmeldungsdienst r, Db db, Uhr uhr)
    {
        if (d.Datum < uhr.Heute) throw new ArgumentException("Räume können nicht rückwirkend geändert werden.");
        if (plan.Frei(s, d.Datum).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var von = Math.Max(1, d.StundeVon);
        var bis = Math.Max(von, d.StundeBis ?? von);
        var klassen = (d.Klassen ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (klassen.Count == 0) throw new ArgumentException("Zur Stunde gehört keine Klasse.");
        if (klassen.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } unbekannt) throw new ArgumentException($"Unbekannte Klasse {unbekannt}.");
        var raum = (d.Raum ?? "").Trim();
        if (raum.Length == 0) throw new ArgumentException("Bitte einen Raum wählen.");

        // Ursprüngliche Räume der Stunde (Plan ohne Vormerkungen)
        var stunde = plan.Rohstunden(s, d.Datum).FirstOrDefault(x => x.Stunde == von && x.Status != "entfall" &&
            (d.Unterricht is int u ? x.Unterricht == u : x.Klassen.Any(k => klassen.Contains(k, Ci))))
            ?? throw new ArgumentException("Die Stunde wurde im Plan nicht gefunden.");
        var original = stunde.Raeume.Where(x => x.Length > 0).ToList();

        // Eigene offene Raumänderungen dieser Stunde – die neue ersetzt sie
        var offen = r.VonBenutzer(b.Id).Where(m => m.Typ == WebRueckmeldungsTypen.Raumbuchung && m.Status == "offen" && m.Von == Plandienst.Iso(d.Datum) &&
                                                   (m.StundeVon ?? 1) <= bis && (m.StundeBis ?? m.StundeVon ?? 1) >= von && m.Klassen.Any(k => klassen.Contains(k, Ci))).ToList();
        var aktuell = offen.LastOrDefault() is { } letzte && (letzte.Raeume.Count > 0 || letzte.Raum != null)
            ? (letzte.Raeume.Count > 0 ? letzte.Raeume : new List<string> { letzte.Raum! })
            : original;

        bool Enthaelt(IEnumerable<string> liste, string x) => liste.Any(y => Ci.Equals(y, x));
        List<string> neu;
        switch (d.Aktion)
        {
            case "ersetzen":
            {
                var alt = (d.Alt ?? "").Trim();
                if (!Enthaelt(aktuell, alt)) throw new ArgumentException($"Raum {alt} gehört nicht zur Stunde.");
                if (Ci.Equals(alt, raum)) return (aktuell, false);
                if (Enthaelt(aktuell, raum)) throw new ArgumentException($"Raum {raum} gehört schon zur Stunde.");
                // Der neue Raum steht vorn: UntPlan übernimmt den ersten als Vertretungsraum, die übrigen als Zusatzräume
                neu = new List<string> { raum }.Concat(aktuell.Where(x => !Ci.Equals(x, alt))).ToList();
                break;
            }
            case "hinzufuegen":
                if (Enthaelt(aktuell, raum)) throw new ArgumentException($"Raum {raum} gehört schon zur Stunde.");
                // zusätzlicher Raum hinten: der ursprüngliche Raum bleibt Hauptraum
                neu = aktuell.Append(raum).ToList();
                break;
            case "loeschen":
                if (!Enthaelt(aktuell, raum)) throw new ArgumentException($"Raum {raum} gehört nicht zur Stunde.");
                neu = aktuell.Where(x => !Ci.Equals(x, raum)).ToList();
                if (neu.Count == 0) throw new ArgumentException("Die Stunde braucht mindestens einen Raum – ersetzen Sie ihn stattdessen.");
                break;
            default:
                throw new ArgumentException("Unbekannte Aktion.");
        }

        // Neu hinzukommende Räume müssen buchbar und frei sein (die eigenen Räume der Stunde dürfen immer zurück)
        foreach (var x in neu.Where(x => !Enthaelt(aktuell, x) && !Enthaelt(original, x)))
        {
            if (!s.Raeume.ContainsKey(x)) throw new ArgumentException($"Unbekannter Raum {x}.");
            if (!Rueckmeldungsdienst.Buchbar(s).Any(w => Ci.Equals(w.Kurz, x))) throw new ArgumentException($"Raum {x} ist nicht buchbar.");
            if (!r.RaumFrei(s, d.Datum, x, von, bis)) throw new ArgumentException($"Raum {x} ist in dieser Zeit nicht frei.");
        }
        neu = neu.Select(x => s.Raeume.TryGetValue(x, out var w) ? w.Kurz : x).ToList();

        foreach (var m in offen) r.Zurueckziehen(m.Id, b.Id);
        if (neu.Count == original.Count && neu.All(x => Enthaelt(original, x))) return (original, true);

        db.Ausfuehren("""
            INSERT INTO rueckmeldungen (id, typ, erstellt, benutzer_id, absender, lehrer, von, bis, stunde_von, stunde_bis, raum, raeume, klassen, weitere_lehrer, titel, grund, text, status)
            VALUES (@id, @typ, @e, @b, @a, @l, @von, NULL, @sv, @sb, @r, @rs, @k, '[]', @t, NULL, '', 'offen')
            """, Db.P("@id", Guid.NewGuid().ToString("N")), Db.P("@typ", WebRueckmeldungsTypen.Raumbuchung), Db.P("@e", uhr.Jetzt.ToString("yyyy-MM-ddTHH:mm:ss")),
            Db.P("@b", b.Id), Db.P("@a", b.LehrerKuerzel ?? b.Login), Db.P("@l", b.LehrerKuerzel), Db.P("@von", Plandienst.Iso(d.Datum)), Db.P("@sv", von), Db.P("@sb", bis),
            Db.P("@r", neu[0]), Db.P("@rs", JsonSerializer.Serialize(neu)), Db.P("@k", JsonSerializer.Serialize(klassen)),
            Db.P("@t", string.IsNullOrWhiteSpace(d.Titel) ? "Raumänderung" : d.Titel.Trim()));
        return (neu, false);
    }
}
