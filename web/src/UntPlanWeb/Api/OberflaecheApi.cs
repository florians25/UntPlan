using System.Text;
using System.Text.RegularExpressions;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Endpunkte der überarbeiteten Oberfläche:
/// <list type="bullet">
/// <item>Schrift: siehe <see cref="SchriftApi"/> (Dateien im Datenordner).</item>
/// <item>Schülerverwaltung (Rechte siehe <see cref="Schuelerverwaltung"/>).</item>
/// <item>Raumänderung aus den Stundendetails (Ersetzen, Löschen, zusätzlicher Raum).</item>
/// <item>Klassenbuch: Übersicht der letzten Tage, Vorstunden für den Lehrstoff-Dialog.</item>
/// </list>
/// </summary>
public static partial class OberflaecheApi
{
    public sealed record RaumaenderungDaten(DateOnly Datum, int StundeVon, int? StundeBis, int Unterricht, List<string>? Raeume);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();
    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
    private static Benutzer Kollegium(HttpContext c) { var b = Ich(c); return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium."); }

    [GeneratedRegex(@"^AvenirNextPro[\w\-. ]*\.(woff2|woff|otf|ttf)$", RegexOptions.IgnoreCase)]
    private static partial Regex Schriftdatei();

    /// <summary>Gewicht und Stil aus dem Dateinamen (z. B. AvenirNextPro-DemiIt.woff2 → 600 italic).</summary>
    internal static (int Gewicht, string Stil) Schnitt(string datei)
    {
        var n = Path.GetFileNameWithoutExtension(datei).ToLowerInvariant().Replace("avenirnextpro", "");
        var stil = Regex.IsMatch(n, "(it|italic|oblique)$") ? "italic" : "normal";
        var gewicht = n switch
        {
            _ when n.Contains("ultralight") || n.Contains("thin") => 200,
            _ when n.Contains("light") => 300,
            _ when n.Contains("demi") || n.Contains("semibold") => 600,
            _ when n.Contains("medium") => 500,
            _ when n.Contains("heavy") || n.Contains("black") => 800,
            _ when n.Contains("bold") => 700,
            _ => 400,
        };
        return (gewicht, stil);
    }

    /// <summary>@font-face-Regeln für die vorhandenen AvenirNextPro-Dateien (leer, wenn keine vorhanden sind – dann gilt Nunito Sans).</summary>
    public static string SchriftCss(string? webroot)
    {
        var ordner = webroot is null ? null : Path.Combine(webroot, "fonts");
        var dateien = ordner is not null && Directory.Exists(ordner)
            ? Directory.GetFiles(ordner).Select(Path.GetFileName).Where(f => f is not null && Schriftdatei().IsMatch(f)).Cast<string>().ToList()
            : new List<string>();
        var sb = new StringBuilder("/* UntPlanWeb: Schriftfamilie AvenirNextPro aus wwwroot/fonts (lizenzierte Dateien der Schule). */\n");
        if (dateien.Count == 0) return sb.Append("/* Keine AvenirNextPro-Dateien gefunden – es gilt Nunito Sans. */\n").ToString();
        static int Rang(string f) => Path.GetExtension(f).ToLowerInvariant() switch { ".woff2" => 0, ".woff" => 1, ".otf" => 2, _ => 3 };
        static string Format(string f) => Path.GetExtension(f).ToLowerInvariant() switch { ".woff2" => "woff2", ".woff" => "woff", ".otf" => "opentype", _ => "truetype" };
        // gleiche Zuordnung wie bei hochgeladenen Schriften (SchriftApi): CSS 100–400 Regular, 500–900 Medium, jedes Gewicht fest belegt
        foreach (var stil in dateien.GroupBy(f => Schnitt(f).Stil).OrderBy(g => g.Key == "italic"))
        {
            var jeSchnitt = stil.GroupBy(f => Schnitt(f).Gewicht).ToDictionary(g => g.Key,
                g => string.Join(", ", g.OrderBy(Rang).Select(f => $"url(\"../fonts/{Uri.EscapeDataString(f)}\") format(\"{Format(f)}\")")));
            foreach (var (css, schnitt) in SchriftApi.Zuordnung(jeSchnitt.Keys))
                sb.Append($"@font-face {{ font-family: \"AvenirNextPro\"; src: {jeSchnitt[schnitt]}; font-weight: {css}; font-style: {stil.Key}; font-display: swap; }}\n");
        }
        sb.Append(":root { --u-schrift: " + SchriftApi.Stapel + "; }\n").Append(SchriftApi.Darstellung);
        return sb.ToString();
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        // ---------------------------------------------------------------- Schülerverwaltung

        api.MapGet("/schueler", (string? klasse, HttpContext c, Paketdienst p, Schuelerverwaltung sv) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var k = string.IsNullOrWhiteSpace(klasse) ? null : klasse.Trim();
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            return Results.Json(new
            {
                schueler = sv.Liste(b, s, k),
                klassen = s.Paket.Elemente.Klassen.OrderBy(x => x.Kurz, de).Select(x => new { kurz = x.Kurz, bearbeiten = Schuelerverwaltung.DarfBearbeiten(b, s, x.Kurz) }),
                anlegen = k is null ? s.Paket.Elemente.Klassen.Any(x => Schuelerverwaltung.DarfBearbeiten(b, s, x.Kurz)) : Schuelerverwaltung.DarfBearbeiten(b, s, k),
                loeschen = Schuelerverwaltung.Verwalten(b),
                klassenwechsel = Schuelerverwaltung.Verwalten(b),
            });
        });
        api.MapPost("/schueler", (SchuelerEingabe d, HttpContext c, Paketdienst p, Schuelerverwaltung sv, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var neu = sv.Anlegen(b, Paket(p), d);
            prot.Schreiben(b.Login, "Schüler*in angelegt", $"{neu.Name}, {neu.Vorname} ({neu.Klasse})");
            return Results.Json(neu);
        });
        api.MapPut("/schueler/{id:int}", (int id, SchuelerEingabe d, HttpContext c, Paketdienst p, Schuelerverwaltung sv, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var x = sv.Aendern(b, Paket(p), id, d);
            prot.Schreiben(b.Login, "Schüler*in geändert", $"{x.Name}, {x.Vorname} ({x.Klasse})");
            return Results.Json(x);
        });
        api.MapDelete("/schueler/{id:int}", (int id, HttpContext c, Paketdienst p, Schuelerverwaltung sv, Protokoll prot) =>
        {
            var b = Kollegium(c);
            sv.Loeschen(b, Paket(p), id);
            prot.Schreiben(b.Login, "Schüler*in gelöscht", id.ToString());
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Raumänderung einer Stunde (Stundendetails)

        api.MapPost("/meldungen/raumaenderung", (RaumaenderungDaten d, HttpContext c, Paketdienst p, Rueckmeldungsdienst r, Protokoll prot) =>
        {
            var b = Kollegium(c);
            var m = r.Raumaenderung(Paket(p), b, d.Datum, d.StundeVon, d.StundeBis ?? d.StundeVon, d.Unterricht, d.Raeume ?? new());
            prot.Schreiben(b.Login, "Raumänderung", $"{d.Datum:yyyy-MM-dd} {d.StundeVon}. Std.: {string.Join(", ", d.Raeume ?? new())}");
            return Results.Json(new { ok = true, meldung = m });
        });

        // ---------------------------------------------------------------- Klassenbuch: Übersicht und Vorstunden

        // Übersicht wie im Original: je Tag die Stunden (eigene, einer Lehrkraft bzw. Klasse) mit Lehrstoff, Abwesenheiten und Hausaufgaben.
        // Zeitraum von–bis (höchstens 31 Tage; ohne von: bis und tage), mehrere Kriterien kombinierbar: klasse und lehrer als Komma-Liste,
        // fach und unterricht schränken weiter ein. Grenzen des Schuljahres (Endlos-Scroll der Klassenbuch-Übersicht) liefert die Antwort mit.
        api.MapGet("/klassenbuch/uebersicht", (string? von, string? bis, int? tage, string? klasse, string? lehrer, string? fach, int? unterricht,
            HttpContext c, Paketdienst p, Plandienst plan, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            static List<string> Liste(string? x) => (x ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ende = DateOnly.TryParse(bis, out var x) ? x : uhr.Heute;
            var start = DateOnly.TryParse(von, out var v) ? v : ende.AddDays(-Math.Clamp(tage ?? 7, 1, 31) + 1);
            if (start > ende) (start, ende) = (ende, start);
            if (ende.DayNumber - start.DayNumber > 30) ende = start.AddDays(30);
            var klassen = Liste(klasse);
            var lehrerListe = Liste(lehrer);
            var faecher = Liste(fach);
            if (klassen.Count == 0 && lehrerListe.Count == 0 && b.LehrerKuerzel is { Length: > 0 } ich) lehrerListe.Add(ich);
            var eintraege = kb.Zeitraum(start, ende);
            var fehl = kb.FehlzeitenZeitraum(start, ende);
            // Klassenbucheinträge (Vermerke) je Klasse im Zeitraum – je Stunde gezählt über die Uhrzeit des Eintrags
            var vermerke = new Dictionary<string, List<Vermerk>>(StringComparer.OrdinalIgnoreCase);
            List<Vermerk> VermerkeVon(string kl) => vermerke.TryGetValue(kl, out var w) ? w : vermerke[kl] = kb.Vermerke(kl, start, ende);
            static bool Hat(IEnumerable<Wert> werte, List<string> liste) => werte.Any(w => liste.Contains(w.K, StringComparer.OrdinalIgnoreCase));
            var tageListe = new List<object>();
            for (var d = start; d <= ende; d = d.AddDays(1))
            {
                if (plan.Frei(s, d).Frei || (klassen.Count == 0 && lehrerListe.Count == 0)) continue;
                var iso = Plandienst.Iso(d);
                // Karten aus den Klassen (bzw. Lehrkräften), doppelte (gleicher Unterricht, gleiche Stunde) nur einmal
                var quelle = klassen.Count > 0
                    ? klassen.SelectMany(k => plan.Karten(s, d, Elementtyp.Klasse, k))
                    : lehrerListe.SelectMany(l => plan.Karten(s, d, Elementtyp.Lehrer, l));
                var karten = quelle
                    .Where(z => z.Unterricht.HasValue && z.Klassen.Count > 0 && z.Status is not ("vorgemerkt" or "buchung" or "veranstaltung"))
                    .Where(z => klassen.Count == 0 || lehrerListe.Count == 0 || Hat(z.Lehrer, lehrerListe))
                    .Where(z => faecher.Count == 0 || faecher.Contains(z.Fach, StringComparer.OrdinalIgnoreCase))
                    .Where(z => unterricht is null || z.Unterricht == unterricht)
                    .GroupBy(z => (z.Unterricht, z.Von, z.Bis, z.Fach)).Select(g => g.First())
                    .OrderBy(z => z.Von).ThenBy(z => z.Fach, StringComparer.OrdinalIgnoreCase).ToList();
                if (karten.Count == 0) continue;
                var tagVermerke = karten.SelectMany(z => z.Klassen.Select(w => w.K)).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                    .SelectMany(VermerkeVon).Where(w => w.Datum == iso).ToList();
                tageListe.Add(new
                {
                    datum = iso,
                    eintraege = tagVermerke.Count,
                    kw = System.Globalization.ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)),
                    stunden = karten.Select(z =>
                    {
                        var e = eintraege.FirstOrDefault(y => y.Datum == iso && y.Unterricht == z.Unterricht && y.Stunde >= z.Von && y.Stunde <= z.Bis);
                        var f = fehl.Where(y => y.Datum == iso && y.Unterricht == z.Unterricht && y.Stunde >= z.Von && y.Stunde <= z.Bis && y.Art == "fehlt")
                            .GroupBy(y => y.SchuelerId).Select(g => g.First()).ToList();
                        var hw = kb.HausaufgabenDerStunde(DateOnly.Parse(iso), z.Von, z.Bis, z.Unterricht!.Value).ToList();
                        return new
                        {
                            von = z.Von, bis = z.Bis, beginn = z.Beginn, ende = z.Ende, fach = z.Fach, fachName = z.FachName, farbe = z.Farbe, art = z.Art, klassen = z.Klassen.Select(w => w.K).Where(w => w.Length > 0),
                            lehrer = z.Lehrer.Select(w => w.K).Where(w => w.Length > 0), raeume = z.Raeume.Select(w => w.K).Where(w => w.Length > 0),
                            status = z.Status, unterricht = z.Unterricht, lehrstoff = e?.Thema ?? "", geprueft = e?.Anwesenheit != null,
                            abwesend = f.Count, entschuldigt = f.Count(y => y.Status == "entschuldigt"), unentschuldigt = f.Count(y => y.Status == "unentschuldigt"),
                            hausaufgaben = string.Join(" · ", hw.Select(h => h.Text)),
                            verspaetet = fehl.Count(y => y.Datum == iso && y.Unterricht == z.Unterricht && y.Stunde >= z.Von && y.Stunde <= z.Bis && y.Art != "fehlt"),
                            eintraege = tagVermerke.Count(w => w.Zeit.Length == 5 && z.Klassen.Any(k => string.Equals(k.K, w.Klasse, StringComparison.OrdinalIgnoreCase))
                                && string.CompareOrdinal(w.Zeit, z.Beginn) >= 0 && string.CompareOrdinal(w.Zeit, z.Ende) < 0),
                        };
                    }).ToList(),
                });
            }
            return Results.Json(new
            {
                von = Plandienst.Iso(start), bis = Plandienst.Iso(ende), klasse = klassen.FirstOrDefault(), lehrer = lehrerListe.FirstOrDefault(), klassen, lehrerListe, faecher,
                schuljahrVon = s.SchuljahrVon == DateOnly.MinValue ? null : Plandienst.Iso(s.SchuljahrVon),
                schuljahrBis = s.SchuljahrBis == DateOnly.MaxValue ? null : Plandienst.Iso(s.SchuljahrBis),
                heute = Plandienst.Iso(uhr.Heute), tage = tageListe,
            });
        });

        // Lehrstoff aus den Vorwochen desselben Unterrichts (Dialog „Lehrstoff“: Übernehmen bzw. Anhängen per Klick) – nur Einträge mit
        // Lehrstoff, die letzten 8; mit lehrer nur die Einträge dieser Lehrkraft (gekoppelter Unterricht mit mehreren Lehrkräften)
        api.MapGet("/klassenbuch/lehrstoff/vorstunden", (DateOnly datum, int stunde, int unterricht, string? lehrer, HttpContext c, Klassenbuchdienst kb) =>
        {
            Kollegium(c);
            var liste = kb.Zeitraum(datum.AddDays(-120), datum)
                .Where(e => e.Unterricht == unterricht && (string.CompareOrdinal(e.Datum, Plandienst.Iso(datum)) < 0 || e.Stunde < stunde))
                .Where(e => !string.IsNullOrWhiteSpace(e.Thema))
                .Where(e => string.IsNullOrEmpty(lehrer) || e.Lehrer.Length == 0 || string.Equals(e.Lehrer, lehrer, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Datum).ThenByDescending(e => e.Stunde).Take(8)
                .Select(e => new { e.Datum, e.Stunde, e.Thema, e.Lehrer, e.Lehrform });
            return Results.Json(liste);
        });
    }
}
