using System.Globalization;
using System.Text;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>Unterricht einer Lehrkraft bzw. Klasse in der gültigen Periode („Meine Unterrichte“).</summary>
public sealed record Unterrichtszeile(int Id, string Fach, string FachName, string Farbe, List<string> Klassen, List<string> Lehrer, int Wochenstunden,
    string Kurs, string Text, string Wochen, List<string> Termine, List<string> Raeume);

/// <summary>Prüfung (Klausur aus UntPlan oder in UntPlanWeb vorgemerkt).</summary>
public sealed record Pruefung(string Datum, int StundeVon, int StundeBis, string Beginn, string Ende, string Titel, List<string> Klassen, List<string> Lehrer,
    List<string> Raeume, string Status, string Text)
{
    /// <summary>Prüfungsart (Klassenarbeit, Klausur, Test …) – aus dem Text „Art: …“ bzw. dem Titel.</summary>
    public string Art { get; init; } = "";
    /// <summary>Fach (Kürzel), wenn der Titel ein Fach ist.</summary>
    public string Fach { get; init; } = "";
    /// <summary>Eingetragen am (yyyy-MM-dd), bei in UntPlanWeb vorgemerkten Prüfungen.</summary>
    public string Aufgegeben { get; init; } = "";
    /// <summary>Darf das angemeldete Konto die Prüfung löschen bzw. verlegen (Ersteller, Lehrkraft der Prüfung, Administration)?</summary>
    public bool DarfLoeschen { get; init; }
}

/// <summary>Abwesenheit einer Schülerin/eines Schülers an einem Tag (aufeinanderfolgende Stunden zusammengefasst).</summary>
public sealed record Abwesenheitszeile(int SchuelerId, string Schueler, string Klasse, string Datum, int StundeVon, int StundeBis, string Beginn, string Ende,
    string Art, int Minuten, string Status, string Bemerkung, List<string> Faecher, List<string> Lehrer, List<int> Ids, bool Verwalten, string Grund = "", string Gruppe = "")
{
    public string Nachweis { get; init; } = "";
    public string EntschuldigtAm { get; init; } = "";
    public bool ElternInformiert { get; init; }
    /// <summary>Interne Notiz – nur für die Klassenleitung (Verwalten) befüllt.</summary>
    public string NotizIntern { get; init; } = "";
    public string Erstellt { get; init; } = "";
}

/// <summary>Klassenbucheintrag (Vermerk) mit Klasse und Name für Listen über mehrere Klassen.</summary>
public sealed record Vermerkzeile(int Id, string Datum, string Klasse, int? SchuelerId, string Schueler, string Art, string Text, string Lehrer)
{
    /// <summary>Uhrzeit des Eintrags (leer = keine Angabe), Stunde laut Zeitraster (0 = außerhalb), Zeitstempel der Erfassung.</summary>
    public string Zeit { get; init; } = "";
    public int Stunde { get; init; }
    public string Erstellt { get; init; } = "";
    /// <summary>Detaildialog: Bearbeiten bzw. Löschen erlaubt (Verfasser, Klassenleitung, Löschsperre).</summary>
    public bool Bearbeiten { get; init; }
    public bool Loeschen { get; init; }
}

/// <summary>Zeile des Fehlzeitenberichts (Summen je Schülerin/Schüler).</summary>
public sealed record Fehlzeitensumme(string Klasse, int SchuelerId, string Name, string Vorname, int Stunden, int Tage, int Entschuldigt, int Unentschuldigt,
    int Offen, int Verspaetungen, int Minuten)
{
    /// <summary>Wie oft vorzeitig gegangen.</summary>
    public int Vorzeitig { get; init; }
    /// <summary>Minuten durch vorzeitiges Gehen.</summary>
    public int VorzeitigMinuten { get; init; }
    /// <summary>Fehlstunden aus Verspätungs- und Gehminuten (laut Verspätungsregeln).</summary>
    public int StundenAusMinuten { get; init; }
    /// <summary>Fehlstunden einschließlich der umgerechneten Minuten.</summary>
    public int StundenGesamt => Stunden + StundenAusMinuten;
    /// <summary>Hinweis für die Klassenleitung (z. B. „4× verspätet“).</summary>
    public string Hinweis { get; init; } = "";
}

/// <summary>
/// Listen wie im Vorbild: Meine Unterrichte, Prüfungen, Hausaufgaben, Klassenbucheinträge, Abwesenheiten und Berichte
/// (Fehlzeiten- und Lehrstoffbericht, auch als CSV).
/// </summary>
public sealed class Listendienst
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new CultureInfo("de-DE"), true);
    private static readonly string[] TageKurz = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
    private readonly Klassenbuchdienst _kb;

    public Listendienst(Klassenbuchdienst kb) => _kb = kb;

    private static Dictionary<int, WebStundenzeit> Zeiten(Stand s) => s.Paket.Zeitraster.Stunden.GroupBy(z => z.Nr).ToDictionary(g => g.Key, g => g.First());

    /// <summary>Gültige Periode an einem Tag (sonst die zuletzt begonnene).</summary>
    public static WebPeriode? Periode(Stand s, DateOnly d) =>
        s.Paket.Perioden.Where(p => DateOnly.TryParse(p.Von, out var v) && v <= d && (!DateOnly.TryParse(p.Bis, out var b) || d <= b))
            .OrderByDescending(p => DateOnly.Parse(p.Von)).FirstOrDefault()
        ?? s.Paket.Perioden.Where(p => DateOnly.TryParse(p.Von, out _)).OrderByDescending(p => DateOnly.Parse(p.Von)).FirstOrDefault();

    /// <summary>Unterrichte der Periode, gefiltert nach Lehrkraft und/oder Klasse.</summary>
    public List<Unterrichtszeile> Unterrichte(Stand s, DateOnly d, string? lehrer, string? klasse)
    {
        var p = Periode(s, d);
        if (p is null) return new();
        var res = new List<Unterrichtszeile>();
        foreach (var u in p.Unterricht)
        {
            var lehrerListe = u.Zeilen.Select(z => z.Lehrer).Where(x => x.Length > 0).Distinct(Ci).ToList();
            var klassen = u.Zeilen.SelectMany(z => z.Klassen).Distinct(Ci).OrderBy(x => x, De).ToList();
            if (!string.IsNullOrEmpty(lehrer) && !lehrerListe.Contains(lehrer, Ci)) continue;
            if (!string.IsNullOrEmpty(klasse) && !klassen.Contains(klasse, Ci)) continue;
            var faecher = u.Zeilen.Select(z => z.Fach).Where(x => x.Length > 0).Distinct(Ci).ToList();
            var fach = string.Join(", ", faecher);
            s.Faecher.TryGetValue(faecher.FirstOrDefault() ?? "", out var fe);
            var plan = p.Stundenplan.Where(x => x.Unterricht == u.Id).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).ToList();
            // Doppelstunden zusammenfassen („Mo 3.–4.“)
            var termine = new List<string>();
            foreach (var g in plan.GroupBy(x => x.Tag))
            {
                var stunden = g.Select(x => x.Stunde).Distinct().OrderBy(x => x).ToList();
                for (var i = 0; i < stunden.Count; i++)
                {
                    var von = stunden[i];
                    while (i + 1 < stunden.Count && stunden[i + 1] == stunden[i] + 1) i++;
                    termine.Add(von == stunden[i] ? $"{TageKurz[Math.Clamp(g.Key, 0, 6)]} {von}." : $"{TageKurz[Math.Clamp(g.Key, 0, 6)]} {von}.–{stunden[i]}.");
                }
            }
            res.Add(new Unterrichtszeile(u.Id, fach, fe?.Name is { Length: > 0 } n ? n : fach, fe?.Farbe ?? "", klassen, lehrerListe, u.Wochenstunden, u.Kurs, u.Text, u.Wochen,
                termine, plan.SelectMany(x => x.Raeume).Where(x => x.Length > 0).Distinct(Ci).OrderBy(x => x, De).ToList()));
        }
        return res.OrderBy(x => x.Klassen.FirstOrDefault() ?? "", De).ThenBy(x => x.Fach, De).ToList();
    }

    /// <summary>Prüfungen im Zeitraum: Klausuren aus UntPlan und noch nicht übernommene Klausuren aus UntPlanWeb („vorgemerkt“).</summary>
    public List<Pruefung> Pruefungen(Stand s, IEnumerable<Meldung> offeneKlausuren, DateOnly von, DateOnly bis, string? klasse, string? lehrer, IEnumerable<Meldung>? loeschungen = null)
    {
        var weg = (loeschungen ?? Enumerable.Empty<Meldung>()).ToList();
        var zeiten = Zeiten(s);
        string Beginn(int n) => zeiten.TryGetValue(n, out var z) ? z.Beginn : "";
        string Ende(int n) => zeiten.TryGetValue(n, out var z) ? z.Ende : "";
        bool Passt(IReadOnlyCollection<string> klassen, IReadOnlyCollection<string> lehrerListe) =>
            (string.IsNullOrEmpty(klasse) || klassen.Contains(klasse, Ci)) && (string.IsNullOrEmpty(lehrer) || lehrerListe.Contains(lehrer, Ci));
        var res = new List<Pruefung>();
        // zum Löschen vorgemerkte Prüfungen (auch der alte Termin einer verlegten Prüfung) nicht mehr zeigen
        foreach (var t in s.Paket.Termine.Where(t => t.Art == "klausur" && DateOnly.TryParse(t.Von, out var d) && d >= von && d <= bis && Passt(t.Klassen, t.Lehrer)
                                                     && !Rueckmeldungsdienst.Geloescht(t, weg, s.Paket.Termine)))
            res.Add(new Pruefung(t.Von, t.StundeVon, Math.Max(t.StundeVon, t.StundeBis), Beginn(t.StundeVon), Ende(Math.Max(t.StundeVon, t.StundeBis)), t.Titel,
                t.Klassen, t.Lehrer, t.Raeume, "geplant", "") { Art = PruefungsArt(t.Titel, "") , Fach = FachAus(s, t.Titel) });
        foreach (var m in offeneKlausuren)
        {
            if (!DateOnly.TryParse(m.Von, out var d) || d < von || d > bis) continue;
            var lehrerListe = new List<string>();
            if (!string.IsNullOrEmpty(m.Lehrer)) lehrerListe.Add(m.Lehrer);
            lehrerListe.AddRange(m.WeitereLehrer);
            if (!Passt(m.Klassen, lehrerListe)) continue;
            var v = m.StundeVon ?? 1;
            var b = Math.Max(v, m.StundeBis ?? v);
            res.Add(new Pruefung(m.Von!, v, b, Beginn(v), Ende(b), m.Titel ?? "Klausur", m.Klassen, lehrerListe, m.Raeume, "vorgemerkt", m.Text ?? "")
            { Art = PruefungsArt(m.Titel ?? "", m.Text ?? ""), Fach = FachAus(s, m.Titel ?? ""), Aufgegeben = m.Erstellt.Length >= 10 ? m.Erstellt[..10] : "" });
        }
        return res.OrderBy(x => x.Datum).ThenBy(x => x.StundeVon).ThenBy(x => x.Klassen.FirstOrDefault() ?? "", De).ToList();
    }

    public static readonly string[] Pruefungsarten = { "Klassenarbeit", "Klausur", "Test", "Abschlussprüfung", "Präsentation", "Referat", "Vergleichsarbeit" };

    /// <summary>Prüfungsart aus „Art: Text“ bzw. dem Titel; sonst „Klausur“.</summary>
    internal static string PruefungsArt(string titel, string text)
    {
        foreach (var a in Pruefungsarten)
            if (text.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in Pruefungsarten)
            if (titel.Contains(a, StringComparison.OrdinalIgnoreCase)) return a;
        return titel.Contains("Test", StringComparison.OrdinalIgnoreCase) ? "Test" : "Klausur";
    }

    private static string FachAus(Stand s, string titel)
    {
        var w = titel.Split(new[] { ' ', '-', '–', ':' }, StringSplitOptions.RemoveEmptyEntries);
        return w.FirstOrDefault(x => s.Faecher.ContainsKey(x)) is { } f ? s.Faecher[f].Kurz : "";
    }

    /// <summary>Hausaufgaben im Zeitraum (nach Fälligkeit), gefiltert nach Klasse und/oder Lehrkraft.</summary>
    public List<Klassenbucheintrag> Hausaufgaben(DateOnly von, DateOnly bis, string? klasse, string? lehrer) =>
        _kb.Zeitraum(von, bis)
            .Where(e => e.Hausaufgabe.Length > 0 && (string.IsNullOrEmpty(klasse) || e.Klassen.Contains(klasse, Ci)) && (string.IsNullOrEmpty(lehrer) || Ci.Equals(e.Lehrer, lehrer)))
            .OrderBy(e => e.Faellig ?? e.Datum).ThenBy(e => e.Datum).ThenBy(e => e.Stunde).ToList();

    /// <summary>Klassenbucheinträge (Vermerke) einer oder aller Klassen, neueste zuerst.</summary>
    public List<Vermerkzeile> Vermerke(Stand s, DateOnly von, DateOnly bis, string? klasse, string? art, Benutzer? b = null)
    {
        var raster = s.Paket.Zeitraster.Stunden;
        int StundeZu(string zeit) => zeit.Length == 5 ? raster.FirstOrDefault(z => string.CompareOrdinal(z.Beginn, zeit) <= 0 && string.CompareOrdinal(zeit, z.Ende) < 0)?.Nr ?? 0 : 0;
        var namen = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => $"{g.First().Name}, {g.First().Vorname}");
        var klassen = string.IsNullOrEmpty(klasse) ? s.Paket.Elemente.Klassen.Select(k => k.Kurz).ToList() : new List<string> { klasse };
        return klassen.SelectMany(k => _kb.Vermerke(k, von, bis))
            .Where(v => string.IsNullOrEmpty(art) || Ci.Equals(v.Art, art))
            .Select(v =>
            {
                var (bearbeiten, loeschen) = b is null ? (false, false) : _kb.VermerkRechte(b, s, v);
                var zeit = v.Zeit.Length == 5 ? v.Zeit : "";
                return new Vermerkzeile(v.Id, v.Datum, v.Klasse, v.SchuelerId, v.SchuelerId is int i ? namen.GetValueOrDefault(i, "") : "", v.Art, v.Text, v.Lehrer)
                    { Zeit = zeit, Stunde = StundeZu(zeit), Erstellt = v.Erstellt, Bearbeiten = bearbeiten, Loeschen = loeschen };
            })
            .OrderByDescending(v => v.Datum).ThenByDescending(v => v.Id).ToList();
    }

    /// <summary>Schülerabwesenheiten im Zeitraum: je Schüler, Tag, Art und Status aufeinanderfolgende Stunden zusammengefasst.</summary>
    public List<Abwesenheitszeile> Abwesenheiten(Stand s, Benutzer b, DateOnly von, DateOnly bis, string? klasse, string? status)
    {
        var zeiten = Zeiten(s);
        var schueler = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var verwalten = new Dictionary<string, bool>(Ci);
        bool Verwalten(string k) => verwalten.TryGetValue(k, out var x) ? x : verwalten[k] = Klassenbuchdienst.IstKlassenleitung(b, s, k);
        var liste = _kb.FehlzeitenZeitraum(von, bis, string.IsNullOrEmpty(klasse) ? null : klasse)
            .Where(f => string.IsNullOrEmpty(status) || f.Status == status).ToList();
        var res = new List<Abwesenheitszeile>();
        foreach (var g in liste.GroupBy(f => (f.SchuelerId, f.Datum, f.Art, f.Status, f.Bemerkung, f.Grund, f.Gruppe, f.Nachweis, f.EntschuldigtAm, f.ElternInformiert)))
        {
            var sortiert = g.OrderBy(f => f.Stunde).ToList();
            for (var i = 0; i < sortiert.Count; i++)
            {
                var block = new List<Fehlzeit> { sortiert[i] };
                while (i + 1 < sortiert.Count && sortiert[i + 1].Stunde == block[^1].Stunde + 1) block.Add(sortiert[++i]);
                var f0 = block[0];
                var name = schueler.TryGetValue(f0.SchuelerId, out var sch) ? $"{sch.Name}, {sch.Vorname}" : $"#{f0.SchuelerId}";
                res.Add(new Abwesenheitszeile(f0.SchuelerId, name, f0.Klasse, f0.Datum, f0.Stunde, block[^1].Stunde,
                    zeiten.TryGetValue(f0.Stunde, out var zb) ? zb.Beginn : "", zeiten.TryGetValue(block[^1].Stunde, out var ze) ? ze.Ende : "",
                    f0.Art, block.Sum(x => x.Minuten), f0.Status, f0.Bemerkung, block.Select(x => x.Fach).Where(x => x.Length > 0).Distinct(Ci).ToList(),
                    block.Select(x => x.Lehrer).Where(x => x.Length > 0).Distinct(Ci).ToList(), block.Select(x => x.Id).ToList(), Verwalten(f0.Klasse), f0.Grund, f0.Gruppe)
                {
                    Nachweis = f0.Nachweis, EntschuldigtAm = f0.EntschuldigtAm, ElternInformiert = f0.ElternInformiert,
                    NotizIntern = Verwalten(f0.Klasse) ? f0.NotizIntern : "", Erstellt = f0.Erstellt,
                });
            }
        }
        return res.OrderByDescending(x => x.Datum).ThenBy(x => x.Klasse, De).ThenBy(x => x.Schueler, De).ThenBy(x => x.StundeVon).ToList();
    }

    /// <summary>Fehlzeitenbericht: Summen je Schülerin/Schüler einer oder aller Klassen.</summary>
    public List<Fehlzeitensumme> Fehlzeitenbericht(Stand s, DateOnly von, DateOnly bis, string? klasse)
    {
        var liste = _kb.FehlzeitenZeitraum(von, bis, string.IsNullOrEmpty(klasse) ? null : klasse);
        var klassen = string.IsNullOrEmpty(klasse) ? s.Paket.Elemente.Klassen.Select(k => k.Kurz) : new[] { klasse };
        var regeln = _kb.Regeln();
        return Klassenzugehoerigkeit.ImZeitraum(s, klassen, von, bis).Select(x =>
        {
            var f = liste.Where(y => y.SchuelerId == x.Id).ToList();
            return Summe(regeln, x.Klasse, x.Id, x.Name, x.Vorname, f);
        }).ToList();
    }

    /// <summary>Summen einer Schülerin/eines Schülers nach den Verspätungsregeln.</summary>
    public static Fehlzeitensumme Summe(Verspaetungsregeln regeln, string klasse, int id, string name, string vorname, IReadOnlyCollection<Fehlzeit> f)
    {
        var fehlt = f.Where(y => y.Art == "fehlt").ToList();
        var spaet = f.Where(y => y.Art == "verspaetet").ToList();
        var vor = f.Where(y => y.Art == "vorzeitig").ToList();
        var unent = fehlt.Count(y => y.Status == "unentschuldigt");
        return new Fehlzeitensumme(klasse, id, name, vorname, fehlt.Count, fehlt.Select(y => y.Datum).Distinct().Count(),
            fehlt.Count(y => y.Status == "entschuldigt"), unent, fehlt.Count(y => y.Status == "offen"), spaet.Count, spaet.Sum(y => y.Minuten))
        {
            Vorzeitig = vor.Count, VorzeitigMinuten = vor.Sum(y => y.Minuten),
            StundenAusMinuten = regeln.StundenAus(spaet.Sum(y => y.Minuten) + vor.Sum(y => y.Minuten)),
            Hinweis = regeln.Hinweis(spaet.Count, vor.Count, unent),
        };
    }

    // ------------------------------------------------------------------------------------------ CSV

    private static string Feld(object? wert)
    {
        var t = Convert.ToString(wert, CultureInfo.GetCultureInfo("de-DE")) ?? "";
        return t.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }

    /// <summary>CSV (Semikolon, UTF-8 mit BOM) für Excel.</summary>
    public static byte[] Csv(IEnumerable<string> kopf, IEnumerable<IEnumerable<object?>> zeilen)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(";", kopf.Select(Feld))).Append("\r\n");
        foreach (var z in zeilen) sb.Append(string.Join(";", z.Select(Feld))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static byte[] FehlzeitenCsv(IEnumerable<Fehlzeitensumme> liste) => Csv(
        new[] { "Klasse", "Name", "Vorname", "Fehlstunden", "Fehltage", "entschuldigt", "unentschuldigt", "offen", "Verspätungen", "Verspätung (Minuten)",
            "vorzeitig gegangen", "vorzeitig (Minuten)", "Fehlstunden aus Minuten", "Fehlstunden gesamt", "Hinweis" },
        liste.Select(x => new object?[] { x.Klasse, x.Name, x.Vorname, x.Stunden, x.Tage, x.Entschuldigt, x.Unentschuldigt, x.Offen, x.Verspaetungen, x.Minuten,
            x.Vorzeitig, x.VorzeitigMinuten, x.StundenAusMinuten, x.StundenGesamt, x.Hinweis }));

    public static byte[] LehrstoffCsv(IEnumerable<Klassenbucheintrag> liste) => Csv(
        new[] { "Datum", "Stunde", "Klassen", "Fach", "Lehrkraft", "Lehrstoff", "Hausaufgabe", "fällig" },
        liste.Select(x => new object?[] { x.Datum, x.Stunde, string.Join(", ", x.Klassen), x.Fach, x.Lehrer, x.Thema, x.Hausaufgabe, x.Faellig ?? "" }));
}
