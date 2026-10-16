using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Sitzpläne im Klassenbuch (nur Kollegium; Lesen für alle Lehrkräfte, Schreiben mit Rechteprüfung):
/// <list type="bullet">
/// <item><c>GET /api/app/sitzplan/{klasse}</c>, <c>PUT /api/app/sitzplan/{klasse}</c> – Klassen-Sitzplan (Seite „Sitzplan“, Klassenleitung).</item>
/// <item><c>GET /api/app/sitzplaene/stunde?unterricht=&amp;raeume=&amp;klassen=&amp;lehrer=</c> – Schüler*innen, gültiger Plan
/// (Unterricht vor Raum vor Klasse), die Pläne der drei Wirkungsbereiche und die Rechte.</item>
/// <item><c>PUT /api/app/sitzplaene</c> – speichern (<c>bereich</c> = klasse | unterricht | raum, Kontext wie oben).</item>
/// <item><c>DELETE /api/app/sitzplaene?bereich=…</c> – Plan eines Wirkungsbereichs löschen.</item>
/// <item><c>GET /api/app/sitzplaene/pdf?…</c> (gespeichert) bzw. <c>POST /api/app/sitzplaene/pdf</c> (aktueller Stand aus dem Editor) – Druck als PDF (S/W).</item>
/// </list>
/// </summary>
public static class SitzplanApi
{
    /// <summary>Plan speichern bzw. drucken: Wirkungsbereich, Kontext der Stunde und Raster.</summary>
    public sealed record PlanDaten(string? Bereich, int? Unterricht, List<string>? Raeume, List<string>? Klassen, string? Lehrer, string? Klasse, string? Raum,
        int Zeilen, int Spalten, string? Pult, Dictionary<string, int>? Belegung, List<string>? OhneTisch,
        bool VornameZuerst = false, bool Kurz = false, bool KlasseZeigen = false, string? Titel = null, string? Ausrichtung = null);

    /// <summary>Klassen-Sitzplan (Seite „Sitzplan“).</summary>
    public sealed record KlassenplanDaten(int Zeilen, int Spalten, string? Pult, Dictionary<string, int>? Belegung, List<string>? OhneTisch);

    private static Benutzer Kollegium(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium.");
    }
    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
    private static List<string> Liste(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Plan für JSON (camelCase), auch für noch nicht gespeicherte Pläne.</summary>
    public static object Json(Sitzplan p, bool vorhanden = true) => new
    {
        bereich = p.Bereich, schluessel = p.Schluessel, zeilen = p.Zeilen, spalten = p.Spalten, pult = p.Pult, belegung = p.Belegung,
        ohneTisch = p.OhneTisch, lehrer = p.Lehrer, geaendert = p.Geaendert, vorhanden,
    };

    private static object? JsonOderNull(Sitzplan? p) => p is null ? null : Json(p);

    /// <summary>Ziel eines Bereichs im Kontext: Schlüssel, erlaubte Schüler*innen, Lehrkraft (Raum); wirft ohne Recht.</summary>
    private static (string Schluessel, HashSet<int> Schueler, string Lehrer) Ziel(Benutzer b, Stand s, string bereich, SitzplanKontext k, string? klasse, string? raum)
    {
        switch (bereich)
        {
            case Sitzplandienst.Klasse:
                var kl = klasse ?? (Sitzplandienst.Klassen(s, k) is { Count: 1 } eine ? eine[0] : throw new ArgumentException("Ein Klassen-Sitzplan ist nur bei Unterricht mit einer Klasse möglich."));
                if (!s.Klassen.ContainsKey(kl)) throw new ArgumentException($"Unbekannte Klasse {kl}.");
                if (!Sitzplandienst.DarfKlasse(b, s, kl)) throw new UnauthorizedAccessException("Einen Klassen-Sitzplan legt nur die Klassenleitung an.");
                return (Sitzplandienst.Schluessel(s, bereich, kl, null, null, null), Klassenbuchdienst.Schueler(s, new[] { kl }).Select(x => x.Id).ToHashSet(), "");
            case Sitzplandienst.Unterricht:
                if (k.Unterricht is not int u || !s.Unterricht.ContainsKey(u)) throw new ArgumentException("Unbekannter Unterricht.");
                if (!Sitzplandienst.DarfUnterricht(b, s, u)) throw new UnauthorizedAccessException("Sitzpläne legen Lehrkräfte nur für ihre eigenen Unterrichte an.");
                return (Sitzplandienst.Schluessel(s, bereich, null, u, null, null), Klassenbuchdienst.Schueler(s, Sitzplandienst.Klassen(s, k)).Select(x => x.Id).ToHashSet(), "");
            case Sitzplandienst.Raum:
                var r = raum ?? k.Raeume.FirstOrDefault(s.Raeume.ContainsKey) ?? throw new ArgumentException("Diese Stunde hat keinen Raum.");
                if (!s.Raeume.ContainsKey(r)) throw new ArgumentException($"Unbekannter Raum {r}.");
                var l = Sitzplandienst.LehrerFuerRaum(b, s, k) ?? throw new ArgumentException("Für einen Raum-Sitzplan fehlt die Lehrkraft.");
                if (!Sitzplandienst.DarfRaum(b, s, r, l, k)) throw new UnauthorizedAccessException("Raum-Sitzpläne legen Lehrkräfte nur für Räume ihrer Unterrichte an.");
                return (Sitzplandienst.Schluessel(s, bereich, null, null, r, l), Klassenbuchdienst.Schueler(s, Sitzplandienst.Klassen(s, k)).Select(x => x.Id).ToHashSet(), s.Lehrer[l].Kurz);
            default:
                throw new ArgumentException("Unbekannter Wirkungsbereich (klasse, unterricht oder raum).");
        }
    }

    private static string Schule(Einstellungen es, Stand s) => es.Schulname.Length > 0 ? es.Schulname : s.Paket.Schule.Name;

    private static string Untertitel(Stand s, SitzplanKontext k, string? bereich)
    {
        var teile = new List<string>();
        var kl = Sitzplandienst.Klassen(s, k);
        if (kl.Count > 0) teile.Add((kl.Count == 1 ? "Klasse " : "Klassen ") + string.Join(", ", kl));
        if (k.Unterricht is int u && s.Unterricht.TryGetValue(u, out var un))
        {
            var faecher = un.Zeilen.Select(z => z.Fach).Where(x => x.Length > 0).Distinct().ToList();
            if (faecher.Count > 0) teile.Add("Fach " + string.Join(", ", faecher));
            var lehrer = Sitzplandienst.LehrerVon(s, u);
            if (lehrer.Count > 0) teile.Add("Lehrkraft " + string.Join(", ", lehrer));
        }
        if (k.Raeume.Count > 0) teile.Add("Raum " + string.Join(", ", k.Raeume));
        if (bereich is not null) teile.Add("gilt für: " + (bereich switch { Sitzplandienst.Klasse => "Klasse", Sitzplandienst.Unterricht => "Unterricht", _ => "Raum" }));
        return string.Join(" · ", teile);
    }

    public static void Abbilden(RouteGroupBuilder api)
    {
        // ---------------------------------------------------------------- Klassen-Sitzplan (Seite „Sitzplan“)

        api.MapGet("/sitzplan/{klasse}", (string klasse, HttpContext c, Paketdienst p, Sitzplandienst sd) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var plan = sd.Laden(s, Sitzplandienst.Klasse, klasse, null, null, null);
            return Results.Json(new
            {
                plan = Json(plan ?? Sitzplandienst.Leer(Sitzplandienst.Klasse, s.Klassen.TryGetValue(klasse, out var k) ? k.Kurz : klasse), plan is not null),
                schueler = Klassenbuchdienst.Schueler(s, new[] { klasse }),
                verwalten = Sitzplandienst.DarfKlasse(b, s, klasse),
            });
        });
        api.MapPut("/sitzplan/{klasse}", (string klasse, KlassenplanDaten d, HttpContext c, Paketdienst p, Sitzplandienst sd) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var (schluessel, schueler, _) = Ziel(b, s, Sitzplandienst.Klasse, new SitzplanKontext(null, new(), new()), klasse, null);
            return Results.Json(Json(sd.Speichern(b, Sitzplandienst.Klasse, schluessel, new SitzplanEingabe(d.Zeilen, d.Spalten, d.Pult, d.Belegung, d.OhneTisch), schueler)));
        });

        // ---------------------------------------------------------------- Sitzpläne einer Stunde (Klassenbuch)

        api.MapGet("/sitzplaene/stunde", (int? unterricht, string? raeume, string? klassen, string? lehrer, HttpContext c, Paketdienst p, Sitzplandienst sd) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var k = new SitzplanKontext(unterricht, Liste(raeume).Where(s.Raeume.ContainsKey).Select(x => s.Raeume[x].Kurz).ToList(), Liste(klassen), lehrer);
            var kl = Sitzplandienst.Klassen(s, k);
            var schueler = Klassenbuchdienst.Schueler(s, kl);
            var ids = schueler.Select(x => x.Id).ToHashSet();
            var raum = k.Raeume.FirstOrDefault();
            var raumLehrer = Sitzplandienst.LehrerFuerRaum(b, s, k);
            var eineKlasse = kl.Count == 1 ? kl[0] : null;
            Sitzplan? Gefiltert(Sitzplan? x) => x is null ? null : x with { Belegung = x.Belegung.Where(y => ids.Contains(y.Value)).ToDictionary(y => y.Key, y => y.Value) };
            var gueltig = sd.Aufloesen(b, s, k, ids);
            return Results.Json(new
            {
                kontext = new { unterricht, raeume = k.Raeume, klassen = kl, klasse = eineKlasse, raum, lehrer = raumLehrer },
                schueler,
                gueltig = JsonOderNull(gueltig),
                plaene = new
                {
                    klasse = eineKlasse is null ? null : JsonOderNull(Gefiltert(sd.Laden(s, Sitzplandienst.Klasse, eineKlasse, null, null, null))),
                    unterricht = unterricht is int u ? JsonOderNull(Gefiltert(sd.Laden(s, Sitzplandienst.Unterricht, null, u, null, null))) : null,
                    raum = raum is not null && raumLehrer is not null ? JsonOderNull(Gefiltert(sd.Laden(s, Sitzplandienst.Raum, null, null, raum, raumLehrer))) : null,
                },
                rechte = new
                {
                    klasse = eineKlasse is not null && Sitzplandienst.DarfKlasse(b, s, eineKlasse),
                    unterricht = unterricht is int u2 && Sitzplandienst.DarfUnterricht(b, s, u2),
                    raum = raum is not null && raumLehrer is not null && Sitzplandienst.DarfRaum(b, s, raum, raumLehrer, k),
                },
            });
        });

        api.MapPut("/sitzplaene", (PlanDaten d, HttpContext c, Paketdienst p, Sitzplandienst sd) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var bereich = d.Bereich ?? "";
            if (!Sitzplandienst.IstBereich(bereich)) throw new ArgumentException("Unbekannter Wirkungsbereich (klasse, unterricht oder raum).");
            var k = new SitzplanKontext(d.Unterricht, d.Raeume ?? new(), d.Klassen ?? new(), d.Lehrer);
            var (schluessel, schueler, lehrer) = Ziel(b, s, bereich, k, d.Klasse, d.Raum);
            return Results.Json(Json(sd.Speichern(b, bereich, schluessel, new SitzplanEingabe(d.Zeilen, d.Spalten, d.Pult, d.Belegung, d.OhneTisch), schueler, lehrer)));
        });

        api.MapDelete("/sitzplaene", (string bereich, int? unterricht, string? raeume, string? klassen, string? lehrer, string? klasse, string? raum,
            HttpContext c, Paketdienst p, Sitzplandienst sd) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            if (!Sitzplandienst.IstBereich(bereich)) throw new ArgumentException("Unbekannter Wirkungsbereich (klasse, unterricht oder raum).");
            var (schluessel, _, _) = Ziel(b, s, bereich, new SitzplanKontext(unterricht, Liste(raeume), Liste(klassen), lehrer), klasse, raum);
            return Results.Json(new { ok = sd.Loeschen(bereich, schluessel) });
        });

        // ---------------------------------------------------------------- Druck (PDF, Schwarz-Weiß)

        api.MapGet("/sitzplaene/pdf", (string? bereich, int? unterricht, string? raeume, string? klassen, string? lehrer, string? klasse, string? raum,
            bool? vornameZuerst, bool? kurz, bool? klasseZeigen, string? ausrichtung, HttpContext c, Paketdienst p, Sitzplandienst sd, Einstellungen es) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var k = new SitzplanKontext(unterricht, Liste(raeume).Where(s.Raeume.ContainsKey).Select(x => s.Raeume[x].Kurz).ToList(),
                klasse is not null ? new List<string> { klasse } : Liste(klassen), lehrer);
            var schueler = Klassenbuchdienst.Schueler(s, Sitzplandienst.Klassen(s, k));
            var ids = schueler.Select(x => x.Id).ToHashSet();
            Sitzplan? plan;
            if (bereich is null) plan = sd.Aufloesen(b, s, k, ids);
            else if (!Sitzplandienst.IstBereich(bereich)) throw new ArgumentException("Unbekannter Wirkungsbereich (klasse, unterricht oder raum).");
            else plan = sd.Laden(s, bereich, klasse ?? (Sitzplandienst.Klassen(s, k) is { Count: 1 } e ? e[0] : null), unterricht,
                raum ?? k.Raeume.FirstOrDefault(), Sitzplandienst.LehrerFuerRaum(b, s, k));
            if (plan is null) return Results.NotFound(new { fehler = "Für diese Stunde ist kein Sitzplan hinterlegt." });
            plan = plan with { Belegung = plan.Belegung.Where(x => ids.Contains(x.Value)).ToDictionary(x => x.Key, x => x.Value) };
            var pdf = Sitzplandienst.Pdf(Schule(es, s), "Sitzplan", Untertitel(s, k, plan.Bereich), plan, schueler, vornameZuerst ?? false, kurz ?? false, klasseZeigen ?? false, b.Anzeigename,
                PdfBericht.Hochformat(ausrichtung, false));
            return Results.File(pdf, "application/pdf", "sitzplan.pdf");
        });

        api.MapPost("/sitzplaene/pdf", (PlanDaten d, HttpContext c, Paketdienst p, Einstellungen es) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var k = new SitzplanKontext(d.Unterricht, (d.Raeume ?? new()).Where(s.Raeume.ContainsKey).Select(x => s.Raeume[x].Kurz).ToList(),
                d.Klasse is not null ? new List<string> { d.Klasse } : d.Klassen ?? new(), d.Lehrer);
            var schueler = Klassenbuchdienst.Schueler(s, Sitzplandienst.Klassen(s, k));
            var (zeilen, spalten, pult, belegung, ohne) = Sitzplandienst.Bereinigen(new SitzplanEingabe(d.Zeilen, d.Spalten, d.Pult, d.Belegung, d.OhneTisch), schueler.Select(x => x.Id).ToHashSet());
            var plan = new Sitzplan(d.Bereich ?? "", "", zeilen, spalten, pult, belegung, ohne, "", "");
            var pdf = Sitzplandienst.Pdf(Schule(es, s), d.Titel is { Length: > 0 and <= 120 } t ? t : "Sitzplan",
                Untertitel(s, k, Sitzplandienst.IstBereich(d.Bereich) ? d.Bereich : null), plan, schueler, d.VornameZuerst, d.Kurz, d.KlasseZeigen, b.Anzeigename, PdfBericht.Hochformat(d.Ausrichtung, false));
            return Results.File(pdf, "application/pdf", "sitzplan.pdf");
        });
    }
}
