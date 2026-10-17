using System.Globalization;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Seiten für Schüler*innen und Erziehungsberechtigte („Meine Daten“): Übersicht, Abwesenheiten (Zeiträume), Fehlzeiten (je Stunde),
/// Hausaufgaben, Export (CSV/PDF) und Entschuldigungsschreiben. Jeder Aufruf prüft Rolle und Zuordnung: Schülerkonten sehen nur die
/// eigene Person, Elternkonten nur ihre Kinder (Parameter <c>schueler</c>, ohne Angabe das erste Kind). Interne Notizen der
/// Klassenleitung erscheinen nie.
/// </summary>
public static class FamilieApi
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] Wt = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

    /// <summary>Angemeldetes Schüler- bzw. Elternkonto und das gewählte (zugeordnete) Kind.</summary>
    public static (Benutzer B, Stand S, WebSchueler Kind) Kind(HttpContext c, Paketdienst p, int? schueler)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!b.Familie) throw new UnauthorizedAccessException("Diese Seiten stehen Schüler*innen und Erziehungsberechtigten zur Verfügung.");
        var s = p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
        if (b.SchuelerIds.Count == 0) throw new UnauthorizedAccessException("Ihrem Konto ist keine Schülerin bzw. kein Schüler zugeordnet. Bitte wenden Sie sich an die Schule.");
        var id = schueler ?? b.SchuelerIds[0];
        if (!b.SchuelerIds.Contains(id)) throw new UnauthorizedAccessException("Diese Daten sind für Ihr Konto nicht freigegeben.");
        var kind = s.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Die zugeordnete Schülerin bzw. der zugeordnete Schüler ist nicht (mehr) in der Schülerliste.");
        return (b, s, kind);
    }

    private static DateOnly D(string? s, DateOnly vorgabe) => DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : vorgabe;
    private static string Datum(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? $"{Wt[((int)d.DayOfWeek + 6) % 7]} {d:dd.MM.yyyy}" : iso;
    private static string StatusText(string s) => s switch { "entschuldigt" => "entschuldigt", "unentschuldigt" => "nicht entschuldigt", _ => "offen" };

    /// <summary>Zeitraum: ohne Angabe Beginn des Schuljahres bis heute.</summary>
    private static (DateOnly Von, DateOnly Bis) Zeitraum(Stand s, Uhr uhr, string? von, string? bis)
    {
        var start = s.SchuljahrVon != DateOnly.MinValue && s.SchuljahrVon <= uhr.Heute ? s.SchuljahrVon : uhr.Heute.AddDays(-90);
        var v = D(von, start);
        var bi = D(bis, uhr.Heute);
        if (bi < v) (v, bi) = (bi, v);
        // Obergrenze: höchstens gut ein Jahr in die Zukunft (eingetragene Beurlaubungen); der Beginn bleibt wie gewählt – früher wurde
        // bei langen Zeiträumen (z. B. „Alles“ in der App) der Beginn verschoben und die Liste war leer.
        if (bi > uhr.Heute.AddDays(400)) bi = uhr.Heute.AddDays(400);
        if (bi < v) bi = v;
        return (v, bi);
    }

    /// <summary>
    /// Summen eines Zeitraums für die Übersicht (unabhängig vom Statusfilter): Abwesenheiten, Fehltage, Fehlstunden und -minuten, Fehlstunden
    /// je Status (entschuldigt, nicht entschuldigt, offen), Verspätungen und vorzeitiges Gehen (Anzahl, Minuten) sowie noch offene Abwesenheiten.
    /// </summary>
    public static object Summen(List<Fehlzeile> zeilen, List<Abwesenheitszeitraum> zeitraeume)
    {
        var gezaehlt = zeilen.Where(z => z.Zaehlt).ToList();
        return new
        {
            abwesenheiten = zeitraeume.Count,
            fehltage = zeilen.Sum(z => z.Fehltage),
            fehlstunden = gezaehlt.Count,
            fehlminuten = zeilen.Sum(z => z.Fehlminuten),
            entschuldigt = gezaehlt.Count(z => z.Status == "entschuldigt"),
            unentschuldigt = gezaehlt.Count(z => z.Status == "unentschuldigt"),
            offen = gezaehlt.Count(z => z.Status is not ("entschuldigt" or "unentschuldigt")),
            verspaetet = zeitraeume.Count(z => z.Art == "verspaetet"),
            verspaetetMinuten = zeitraeume.Where(z => z.Art == "verspaetet").Sum(z => z.Minuten),
            vorzeitig = zeitraeume.Count(z => z.Art == "vorzeitig"),
            offeneAbwesenheiten = zeitraeume.Count(z => z.Status == "offen"),
        };
    }

    /// <summary>Fehlzeiten und Abwesenheiten eines Kindes im Zeitraum samt Summen.</summary>
    private static (List<Fehlzeile> Zeilen, List<Abwesenheitszeitraum> Zeitraeume, object Summen) Daten(Klassenbuchdienst kb, Stand s, int kind, DateOnly v, DateOnly bi)
    {
        var roh = kb.FehlzeitenVon(kind, v, bi);
        var zeilen = kb.Fehlzeilen(s, roh);
        var zeitraeume = kb.Zeitraeume(s, roh, _ => false);
        return (zeilen, zeitraeume, Summen(zeilen, zeitraeume));
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/familie");

        api.MapGet("/kinder", (HttpContext c, Paketdienst p) =>
        {
            var b = c.Ich() ?? throw new NichtAngemeldetException();
            if (!b.Familie) throw new UnauthorizedAccessException("Diese Seiten stehen Schüler*innen und Erziehungsberechtigten zur Verfügung.");
            var s = p.Aktuell;
            return Results.Json(b.SchuelerIds.Select(i => s?.Schueler.FirstOrDefault(x => x.Id == i)).Where(x => x != null)
                .Select(x => new { x!.Id, x.Vorname, x.Name, x.Klasse }));
        });

        // Bezeichnungen der Abwesenheitsgründe (Kürzel → Name) für die Listen
        api.MapGet("/gruende", (HttpContext c, Klassenbuchverwaltung kv) =>
        {
            var b = c.Ich() ?? throw new NichtAngemeldetException();
            if (!b.Familie) throw new UnauthorizedAccessException("Diese Seiten stehen Schüler*innen und Erziehungsberechtigten zur Verfügung.");
            return Results.Json(kv.Gruende().GroupBy(g => g.Kurz, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name));
        });

        // Übersicht „Meine Daten“: offene Abwesenheiten im Schuljahr, Hausaufgaben eine Woche in die Zukunft
        api.MapGet("/uebersicht", (int? schueler, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var (_, s, kind) = Kind(c, p, schueler);
            var (v, _) = Zeitraum(s, uhr, null, null);
            var offen = kb.Zeitraeume(s, kb.FehlzeitenVon(kind.Id, v, uhr.Heute), _ => false).Where(z => z.Status == "offen").ToList();
            var hausaufgaben = kb.HausaufgabenZeitraum(uhr.Heute, uhr.Heute.AddDays(7), kind.Klasse, nachFaelligkeit: true);
            return Results.Json(new { schueler = kind, offeneAbwesenheiten = offen, hausaufgaben });
        });

        api.MapGet("/abwesenheiten", (int? schueler, string? von, string? bis, string? status, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var (_, s, kind) = Kind(c, p, schueler);
            var (v, bi) = Zeitraum(s, uhr, von, bis);
            var d = Daten(kb, s, kind.Id, v, bi);
            var liste = d.Zeitraeume.Where(z => string.IsNullOrEmpty(status) || z.Status == status).ToList();
            return Results.Json(new { schueler = kind, von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = liste, summen = d.Summen });
        });

        api.MapGet("/fehlzeiten", (int? schueler, string? von, string? bis, string? status, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var (_, s, kind) = Kind(c, p, schueler);
            var (v, bi) = Zeitraum(s, uhr, von, bis);
            var d = Daten(kb, s, kind.Id, v, bi);
            var liste = d.Zeilen.Where(z => string.IsNullOrEmpty(status) || z.Status == status).ToList();
            return Results.Json(new { schueler = kind, von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = liste, summen = d.Summen });
        });

        api.MapGet("/hausaufgaben", (int? schueler, string? von, string? bis, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Uhr uhr) =>
        {
            var (_, s, kind) = Kind(c, p, schueler);
            var v = D(von, uhr.Heute);
            var bi = D(bis, v.AddDays(30));
            if (bi < v || bi.DayNumber - v.DayNumber > 400) bi = v.AddDays(30);
            return Results.Json(new { schueler = kind, von = Plandienst.Iso(v), bis = Plandienst.Iso(bi), zeilen = kb.HausaufgabenZeitraum(v, bi, kind.Klasse) });
        });

        // Export der Listen (CSV für Excel, PDF als Schwarz-Weiß-Formular)
        api.MapGet("/export", (string liste, string? format, int? schueler, string? von, string? bis, string? ausrichtung, string? status, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Klassenbuchverwaltung kv,
            Einstellungen e, Uhr uhr) =>
        {
            var (b, s, kind) = Kind(c, p, schueler);
            var (v, bi) = Zeitraum(s, uhr, von, bis);
            var gruende = kv.Gruende().GroupBy(g => g.Kurz, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            string G(string g) => gruende.GetValueOrDefault(g, g);
            // gleicher Statusfilter wie die Liste auf der Seite
            bool Passt(string st) => string.IsNullOrEmpty(status) || st == status;
            List<string> spalten;
            List<List<string>> zeilen;
            string titel;
            if (liste == "fehlzeiten")
            {
                titel = "Fehlzeiten";
                spalten = new() { "Datum", "Zeit", "Fach", "Lehrkraft", "Fehltage", "Fehlstd.", "Fehlmin.", "zählt", "Abwesenheitsgrund", "Status", "Text" };
                zeilen = kb.Fehlzeilen(s, kb.FehlzeitenVon(kind.Id, v, bi)).Where(z => Passt(z.Status)).Select(z => new List<string> { Datum(z.Datum), $"{z.Beginn} - {z.Ende}", z.Fach, z.Lehrer,
                    z.Fehltage.ToString(De), z.Fehlstunden.ToString(De), z.Fehlminuten.ToString(De), z.Zaehlt ? "ja" : "nein", G(z.Grund), StatusText(z.Status), z.Text }).ToList();
            }
            else if (liste == "abwesenheiten")
            {
                titel = "Abwesenheiten";
                spalten = new() { "Von", "Bis", "Abwesenheitsgrund", "Status", "Fehlstunden", "Fehlminuten", "Text" };
                zeilen = kb.Zeitraeume(s, kb.FehlzeitenVon(kind.Id, v, bi), _ => false).Where(z => Passt(z.Status)).Select(z => new List<string> { $"{Datum(z.VonDatum)} {z.VonZeit}", $"{Datum(z.BisDatum)} {z.BisZeit}",
                    G(z.Grund), StatusText(z.Status), z.Fehlstunden.ToString(De), z.Fehlminuten.ToString(De), z.Text }).ToList();
            }
            else throw new ArgumentException("Unbekannte Liste.");
            var datei = $"{liste}-{kind.Name}-{v:yyyy-MM-dd}-{bi:yyyy-MM-dd}".ToLowerInvariant().Replace(' ', '-');
            if (format == "pdf")
            {
                var schule = e.Schulname.Length > 0 ? e.Schulname : s.Paket.Schule.Name;
                var pdf = PdfBericht.Erzeugen(schule, titel, $"{kind.Vorname} {kind.Name} · Klasse {kind.Klasse} · Zeitraum {v:dd.MM.yyyy} – {bi:dd.MM.yyyy}", spalten,
                    zeilen.Select(z => (IReadOnlyList<string>)z).ToList(), null, b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login, false, PdfBericht.Hochformat(ausrichtung, false));
                return Results.File(pdf, "application/pdf", datei + ".pdf");
            }
            return Results.File(Listendienst.Csv(spalten, zeilen.Select(z => z.Cast<object?>())), "text/csv; charset=utf-8", datei + ".csv");
        });

        // Entschuldigungsschreiben (PDF zum Unterschreiben): gewählte bzw. alle nicht entschuldigten Abwesenheiten im Zeitraum
        api.MapGet("/entschuldigung", (int? schueler, string? von, string? bis, string? ids, string? ausrichtung, HttpContext c, Paketdienst p, Klassenbuchdienst kb, Klassenbuchverwaltung kv,
            Einstellungen e, Uhr uhr) =>
        {
            var (b, s, kind) = Kind(c, p, schueler);
            var (v, bi) = Zeitraum(s, uhr, von, bis);
            var wahl = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : -1).Where(i => i > 0).ToHashSet();
            var gruende = kv.Gruende().GroupBy(g => g.Kurz, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var liste = kb.Zeitraeume(s, kb.FehlzeitenVon(kind.Id, v, bi), _ => false)
                .Where(z => wahl.Count > 0 ? z.Ids.Any(wahl.Contains) : z.Status != "entschuldigt").OrderBy(z => z.VonDatum, StringComparer.Ordinal).ThenBy(z => z.VonZeit, StringComparer.Ordinal).ToList();
            if (liste.Count == 0) throw new ArgumentException("Im Zeitraum gibt es keine Abwesenheiten, die entschuldigt werden müssen.");
            var schule = e.Schulname.Length > 0 ? e.Schulname : s.Paket.Schule.Name;
            var zeilen = liste.Select(z => (IReadOnlyList<string>)new List<string> { $"{Datum(z.VonDatum)} {z.VonZeit}", $"{Datum(z.BisDatum)} {z.BisZeit}",
                z.Fehlstunden.ToString(De), gruende.GetValueOrDefault(z.Grund, z.Grund), StatusText(z.Status), z.Text }).ToList();
            var hinweis = $"Hiermit entschuldige ich das Fehlen von {kind.Vorname} {kind.Name} (Klasse {kind.Klasse}) in den oben aufgeführten Zeiträumen. "
                          + "Grund des Fehlens: ____________________________________________";
            var pdf = PdfBericht.Erzeugen(schule, "Entschuldigungsschreiben", $"{kind.Vorname} {kind.Name} · Klasse {kind.Klasse}",
                new[] { "Von", "Bis", "Fehlstunden", "Abwesenheitsgrund", "Status", "Text" }, zeilen, hinweis, b.Rolle == Rollen.Eltern ? "Erziehungsberechtigte*r" : $"{kind.Vorname} {kind.Name}",
                true, PdfBericht.Hochformat(ausrichtung, true));
            return Results.File(pdf, "application/pdf", $"entschuldigung-{kind.Name.ToLowerInvariant()}-{uhr.Heute:yyyy-MM-dd}.pdf");
        });
    }
}
