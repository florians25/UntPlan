using System.Globalization;
using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>Wert einer Auswahl in den Berichtseinstellungen.</summary>
public sealed record Berichtswahl(string Wert, string Text);

/// <summary>Einstellung eines Berichts: Auswahl (z. B. „pro Stunde“ | „pro Tag“) oder Haken (z. B. „Fehltage und -zeiten trennen“).</summary>
public sealed record Berichtsoption(string Name, string Titel, string Typ, string Vorgabe, List<Berichtswahl>? Werte = null);

/// <summary>
/// Abwesenheitsberichte und Fehlzeitenstatistik der Klassenleitung (Klassenbuch → Berichte) mit den Einstellungen des Originals,
/// das Deckblatt des Klassenbuchs (Schuljahresende) und die Berichte zum Unterricht (Arbeitsbericht, Fehlzeiten pro Unterricht pro
/// Schüler*in mit der 75 %-Regel, Fehlzeiten pro Unterricht pro Stunde, Fehlstundenmatrix). Gezählt wird mit <see cref="Fehlzeitenrechnung"/>.
/// </summary>
public sealed partial class Klassenbuchverwaltung
{
    private static Berichtsoption Haken(string name, string titel, bool vorgabe = false) => new(name, titel, "haken", vorgabe ? "1" : "0");
    private static Berichtsoption Auswahl(string name, string titel, string vorgabe, params (string Wert, string Text)[] werte) =>
        new(name, titel, "auswahl", vorgabe, werte.Select(w => new Berichtswahl(w.Wert, w.Text)).ToList());

    // Eigenschaften statt Felder: die Liste der Berichtsarten (anderer Teil der Klasse) wird ggf. vorher initialisiert
    private static Berichtsoption StatusFilter => Auswahl("status", "Status", "alle", ("alle", "- Alle -"), ("entschuldigt", "[entschuldigt]"), ("nicht_entschuldigt", "[nicht entschuldigt]"));

    private static List<Berichtsoption> OptionenFehlzeitenSchueler => new()
    {
        Auswahl("ausgabe", "Ausgabe", "pro_stunde", ("pro_stunde", "pro Stunde"), ("pro_tag", "pro Tag"), ("gesamt", "Gesamtübersicht")),
        StatusFilter,
        Haken("fehlzeiten", "Fehlzeiten", true), Haken("verspaetungen", "Verspätungen", true), Haken("zaehlend", "Nur zählende Abwesenheiten"),
        Haken("alle", "Alle Schüler*innen"), Haken("ohneumbruch", "ohne Seitenumbrüche"),
    };
    private static List<Berichtsoption> OptionenFehlzeitenKlasse => new()
    {
        Auswahl("ausgabe", "Ausgabe", "gesamt", ("gesamt", "Gesamtübersicht"), ("pro_stunde", "pro Stunde"), ("klassen", "Summen je Klasse")),
        StatusFilter,
        Haken("fehlzeiten", "Fehlzeiten", true), Haken("verspaetungen", "Verspätungen", true), Haken("zaehlend", "Nur zählende Abwesenheiten", true),
        Haken("alle", "Alle Schüler*innen"), Haken("sortiert", "sortiert nach Schülern/Schülerinnen", true), Haken("trennen", "Fehltage und -zeiten trennen", true),
        Haken("klassenfilter", "Klassenfilter aktivieren"),
    };
    private static List<Berichtsoption> OptionenFehlzeitenFach => new() { Auswahl("ausgabe", "Ausgabe", "gesamt", ("gesamt", "Gesamtübersicht")), StatusFilter };
    private static List<Berichtsoption> OptionenFehltage => new() { Haken("ganze", "Ganze Tage") };
    private static List<Berichtsoption> OptionenEntschuldigung => new()
    {
        Auswahl("ausgabe", "Ausgabe", "pro_stunde", ("pro_stunde", "pro Stunde"), ("pro_tag", "pro Tag")),
        Haken("woche", "Gruppierung pro Woche"), Haken("entschuldigte", "Entschuldigte Stunden anzeigen"),
    };
    private static List<Berichtsoption> OptionenTag => new() { Haken("ohneumbruch", "ohne Seitenumbrüche") };
    private static Berichtsoption KategorieFilter => new("kategorie", "Eintragungskategorie", "auswahl", "",
        new[] { new Berichtswahl("", "- Alle -") }.Concat(Klassenbuchdienst.VermerkGruppen.Keys.Select(g => new Berichtswahl("gruppe:" + g, $"Kategoriegruppe {g}")))
            .Concat(Klassenbuchdienst.VermerkArten.Select(a => new Berichtswahl(a, a))).ToList());
    private static List<Berichtsoption> OptionenEintraegeKlasse => new() { KategorieFilter, Haken("sortiert", "sortiert nach Schülern/Schülerinnen") };
    private static List<Berichtsoption> OptionenEintraegeSchueler => new() { KategorieFilter };
    private static List<Berichtsoption> OptionenWoche => new() { Haken("mitabw", "mit Abwesenheiten") };
    private static List<Berichtsoption> OptionenMatrix => new() { Haken("ohne", "ohne Einträge") };

    private static bool An(IReadOnlyDictionary<string, string> o, Berichtsart a, string name)
    {
        var v = o.TryGetValue(name, out var x) ? x : a.Optionen?.FirstOrDefault(y => y.Name == name)?.Vorgabe ?? "0";
        return v is "1" or "true" or "on" or "ja";
    }
    private static string Wert(IReadOnlyDictionary<string, string> o, Berichtsart a, string name)
    {
        var opt = a.Optionen?.FirstOrDefault(y => y.Name == name);
        var v = o.TryGetValue(name, out var x) ? x : opt?.Vorgabe ?? "";
        return opt?.Werte is { } w && w.All(y => y.Wert != v) ? opt.Vorgabe : v;
    }

    private static readonly CultureInfo DeKultur = CultureInfo.GetCultureInfo("de-DE");
    private static string TagDatum(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? d.ToString("ddd dd.MM.yy", DeKultur) : iso;
    private static string KurzDatum(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? d.ToString("dd.MM.yy", DeKultur) : "";
    private static string StatusKurz(string status, string name) => name.Length > 0 ? name : status switch
    {
        "entschuldigt" => "entsch.",
        "unentschuldigt" => "nicht entsch.",
        _ => "[offen]",
    };
    private static object? Leer0(int n) => n == 0 ? "" : n;
    private static string Zeit0(int min) => min == 0 ? "" : Fehlzeitenrechnung.Hhmm(min);

    /// <summary>Berichte mit Einstellungen bzw. neue Berichte; null = der bisherige Bericht ohne Einstellungen.</summary>
    private Bericht? Zusatzbericht(Stand s, Benutzer b, Berichtsart info, DateOnly von, DateOnly bis, string? klasse, int? schueler, IReadOnlyDictionary<string, string> o)
    {
        var art = info.Id;
        var ausgabe = Wert(o, info, "ausgabe");
        switch (art)
        {
            case "fehlzeiten_schueler" when ausgabe is "pro_stunde" or "pro_tag":
            case "fehlzeiten_klasse" when ausgabe is "pro_stunde" or "gesamt":
                return Fehlzeitenbericht(s, info, von, bis, klasse, schueler, o, ausgabe);
            case "klassenbuch_deckblatt":
                return Deckblatt(s, info, von, bis, klasse!);
            case "wochenuebersicht":
                return Wochenuebersicht(s, info, von, bis, klasse!, An(o, info, "mitabw"));
            case "klassenbucheintraege" or "klassenbucheintraege_schueler":
                return Eintragsbericht(s, info, von, bis, klasse, schueler, o);
            case "entschuldigungsschreiben":
                return Entschuldigungsschreiben(s, b, info, von, bis, klasse, schueler, o);
            case "arbeitsbericht_unterricht" or "fehlzeiten_unterricht_schueler" or "fehlzeiten_unterricht_stunde" or "fehlstundenmatrix" or "schueler_unterricht":
                return Unterrichtsbericht(s, b, info, von, bis, o);
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------ Fehlzeiten pro Schüler*in / pro Klasse

    private static string TagPunkt(string iso, string trenner) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d)
        ? $"{d.ToString("ddd", DeKultur).TrimEnd('.')}.{trenner}{d:dd.MM.yy}" : iso;
    /// <summary>Prozent wie im Original („43.9 %“, ganzer Tag „100 %“).</summary>
    private static string ProzentOriginal(double p, int stellen = 1) => p % 1 == 0 && stellen == 1
        ? $"{p.ToString("0", CultureInfo.InvariantCulture)} %" : $"{p.ToString(stellen == 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture)} %";
    /// <summary>Kopfblock je Schüler*in wie im Original: „Schüler*innen“, „Text“, „Klasse“.</summary>
    private static string Schuelerblock(string name, string klasse, string text = "") => $"Schüler*innen\t{name}\nText\t{text}\nKlasse\t{klasse}";
    /// <summary>ENr (Nummer der Entschuldigung): Nummer der Abwesenheit, sobald sie erledigt ist.</summary>
    private static Dictionary<int, string> Entschuldigungsnummern(List<Fehlzeit> fz) => fz.GroupBy(Klassenbuchdienst.AbwesenheitSchluessel)
        .SelectMany(g => { var nr = g.Min(f => f.Id); return g.Select(f => (f.Id, Nr: f.EntschuldigtAm.Length > 0 ? nr.ToString(CultureInfo.InvariantCulture) : "")); })
        .ToDictionary(x => x.Id, x => x.Nr);

    private Bericht Fehlzeitenbericht(Stand s, Berichtsart info, DateOnly von, DateOnly bis, string? klasse, int? schueler, IReadOnlyDictionary<string, string> o, string ausgabe)
    {
        var namen = Schueler(s);
        var klassen = string.IsNullOrEmpty(klasse) ? s.Paket.Elemente.Klassen.Select(k => k.Kurz).ToList() : new List<string> { klasse };
        var kinder = Klassenzugehoerigkeit.ImZeitraum(s, klassen, von, bis).Where(x => schueler is null || x.Id == schueler).ToList();
        // „Klassenfilter aktivieren“: nur Fehlzeiten, die in der gewählten Klasse erfasst wurden; sonst alle Fehlzeiten ihrer Schüler*innen im Zeitraum
        var klassenfilter = info.Optionen?.Any(x => x.Name == "klassenfilter") != true || An(o, info, "klassenfilter");
        var ids0 = kinder.Select(x => x.Id).ToHashSet();
        var alleFz = (klassenfilter || string.IsNullOrEmpty(klasse) ? _kb.FehlzeitenZeitraum(von, bis, string.IsNullOrEmpty(klasse) ? null : klasse)
                : _kb.FehlzeitenZeitraum(von, bis).Where(f => ids0.Contains(f.SchuelerId)).ToList())
            .Where(f => schueler is null || f.SchuelerId == schueler).ToList();
        // Status je Abwesenheit über alle ihre Fehlzeiten (auch Verspätungen), danach Filter der Einstellungen
        var alleP = _kb.Fehlposten(s, alleFz);
        var wirksam = Fehlzeitenrechnung.WirksamerStatus(alleP);
        var fehl = An(o, info, "fehlzeiten");
        var spaet = An(o, info, "verspaetungen");
        var zaehlend = An(o, info, "zaehlend");
        var statusFilter = Wert(o, info, "status");
        bool Passt(Fehlposten p) => (p.Fehlstunde ? fehl : spaet) && (!zaehlend || p.Zaehlt)
            && (statusFilter == "alle" || (statusFilter == "entschuldigt") == !Fehlzeitenrechnung.Unentschuldigt(wirksam[p.Id]));
        var posten = alleP.Where(Passt).ToList();
        var ids = posten.Select(p => p.Id).ToHashSet();
        var zeilenDetail = _kb.Fehlzeilen(s, alleFz.Where(f => ids.Contains(f.Id))).ToDictionary(z => z.Id);
        var fzNach = alleFz.ToDictionary(f => f.Id);
        var enr = Entschuldigungsnummern(alleFz);
        var gruende = Gruende().GroupBy(g => g.Kurz, Ci).ToDictionary(g => g.Key, g => g.First().Name, Ci);
        string Grund(string g) => gruende.TryGetValue(g, out var n) ? n : g;
        var plan = _kb.PlanFinder(s);
        var mitAllen = An(o, info, "alle");
        var titel = info.Titel + (statusFilter == "entschuldigt" ? " – entschuldigt" : statusFilter == "nicht_entschuldigt" ? " – nicht entschuldigt" : "");
        var hinweisBasis = "Es erscheinen nur die Fehlzeiten, die als Abwesenheiten in UntPlanWeb bzw. UntPlanMobile dokumentiert wurden. Nur „entschuldigt“ zählt als entschuldigt; [offen] und alle anderen Status zählen als nicht entschuldigt.";
        var zeilen = new List<(string Art, object?[] Zeile)>();

        if (ausgabe == "gesamt")
        {
            // Bericht für das Zeugnis: je Schüler*in Fehlschulstunden, Fehlstunden (Zeitstunden) und Fehltage – je alle, zählend, unentschuldigt
            var trennen = An(o, info, "trennen");
            var sortiert = An(o, info, "sortiert");
            var spalten = new List<string> { "Schüler*in", "Fehlschulst. alle", "Fehlschulst. zählend", "Fehlschulst. unent.", "Fehlschulst. zählend unent.",
                "Fehlstd. alle", "Fehlstd. zählend", "Fehlstd. unent.", "Fehlstd. zählend unent.", "Fehltage alle", "Fehltage zählend", "Fehltage unent.", "Fehltage zählend unent." };
            foreach (var k in klassen)
            {
                var liste = kinder.Where(x => Ci.Equals(x.Klasse, k)).Select(x => (x, a: Fehlzeitenrechnung.Auswerten(x.Id, posten, plan, trennen)))
                    .Where(t => mitAllen || posten.Any(p => p.SchuelerId == t.x.Id)).ToList();
                liste = (sortiert ? liste.OrderBy(t => Name(namen, t.x.Id), De) : liste.OrderBy(t => t.x.Klasse, De).ThenBy(t => Name(namen, t.x.Id), De)).ToList();
                if (liste.Count == 0) continue;
                zeilen.Add(("gruppe", new object?[] { $"Klasse: {k}" }.Concat(Enumerable.Repeat<object?>("", spalten.Count - 1)).ToArray()));
                foreach (var (x, a) in liste)
                    zeilen.Add(("", new object?[] { Name(namen, x.Id), Leer0(a.Schulstunden), Leer0(a.SchulstundenZaehlend), Leer0(a.SchulstundenUnent), Leer0(a.SchulstundenZaehlendUnent),
                        Zeit0(a.Minuten), Zeit0(a.MinutenZaehlend), Zeit0(a.MinutenUnent), Zeit0(a.MinutenZaehlendUnent),
                        Leer0(a.Fehltage), Leer0(a.FehltageZaehlend), Leer0(a.FehltageUnent), Leer0(a.FehltageZaehlendUnent) }));
                var sum = liste.Select(t => t.a).ToList();
                zeilen.Add(("summe", new object?[] { $"Summe {k}", sum.Sum(a => a.Schulstunden), sum.Sum(a => a.SchulstundenZaehlend), sum.Sum(a => a.SchulstundenUnent), sum.Sum(a => a.SchulstundenZaehlendUnent),
                    Fehlzeitenrechnung.Hhmm(sum.Sum(a => a.Minuten)), Fehlzeitenrechnung.Hhmm(sum.Sum(a => a.MinutenZaehlend)), Fehlzeitenrechnung.Hhmm(sum.Sum(a => a.MinutenUnent)),
                    Fehlzeitenrechnung.Hhmm(sum.Sum(a => a.MinutenZaehlendUnent)), sum.Sum(a => a.Fehltage), sum.Sum(a => a.FehltageZaehlend), sum.Sum(a => a.FehltageUnent), sum.Sum(a => a.FehltageZaehlendUnent) }));
            }
            // wie im Original: Spaltengruppen „Fehlschulstunde“, „Fehlstunden“, „Fehltage“ mit Alle, zählend, unent., zählend; die zählenden Spalten grau
            var vier = new[] { "Alle", "zählend", "unent.", "zählend" };
            return new Bericht(info.Id, titel, spalten, zeilen.Select(z => z.Zeile.ToList()).ToList(),
                $"Für das Zeugnis: Fehlstunden, davon unentschuldigt, und Fehltage, davon unentschuldigt. {(trennen ? "Fehltage und -zeiten getrennt: Stunden ganzer Fehltage zählen nicht zusätzlich als Fehlstunden. " : "")}{hinweisBasis}",
                zeilen.Select(z => z.Art).ToList())
            {
                Darstellung = new Berichtsdarstellung
                {
                    Koepfe = new[] { "Schüler*innen" }.Concat(vier).Concat(vier).Concat(vier).ToList(),
                    Gruppen = new[] { "" }.Concat(Enumerable.Repeat("Fehlschulstunde", 4)).Concat(Enumerable.Repeat("Fehlstunden", 4)).Concat(Enumerable.Repeat("Fehltage", 4)).ToList(),
                    Grau = new() { 2, 4, 6, 8, 10, 12 },
                },
            };
        }

        // pro Stunde bzw. pro Tag: je Schüler*in ein Kopfblock (Schüler*innen, Text, Klasse), die Fehlzeiten und die Summen
        var proTag = ausgabe == "pro_tag";
        var spaltenD = proTag
            ? new List<string> { "Datum", "Schulstd. Fehlst", "Schulstd. Unt.", "hh:mm Fehlst", "hh:mm Unt.", "%", "Abwesenheitsgrund", "Erledigt", "Status" }
            : new List<string> { "Datum", "Schulst", "Std.", "Fach", "Lehrkraft", "Abwesenheitsgrund", "ENr", "Erledigt", "Status" };
        var reihen = kinder.Where(x => mitAllen || posten.Any(p => p.SchuelerId == x.Id))
            .OrderBy(x => string.IsNullOrEmpty(klasse) ? x.Klasse : "", De).ThenBy(x => Name(namen, x.Id), De).ToList();
        object?[] Leerzeile(object? erstes) => new[] { erstes }.Concat(Enumerable.Repeat<object?>("", spaltenD.Count - 1)).ToArray();
        foreach (var x in reihen)
        {
            var eigene = posten.Where(p => p.SchuelerId == x.Id).OrderBy(p => p.Datum, StringComparer.Ordinal).ThenBy(p => p.Stunde).ToList();
            zeilen.Add(("block", Leerzeile(Schuelerblock(Name(namen, x.Id), x.Klasse))));
            bool U(Fehlposten p) => Fehlzeitenrechnung.Unentschuldigt(wirksam[p.Id]);
            if (!proTag)
            {
                foreach (var p in eigene)
                {
                    var f = fzNach[p.Id];
                    var d = zeilenDetail.GetValueOrDefault(p.Id);
                    zeilen.Add(("", new object?[] { TagPunkt(p.Datum, "   "), p.Fehlstunde ? 1 : 0, Fehlzeitenrechnung.Hhmm(p.Minuten), f.Fach, d?.Lehrer ?? f.Lehrer, Grund(p.Grund),
                        enr.GetValueOrDefault(p.Id, ""), KurzDatum(f.EntschuldigtAm), StatusKurz(wirksam[p.Id], wirksam[p.Id] == f.Status ? f.StatusName : "") }));
                }
                void Summe(string text, IEnumerable<Fehlposten> menge)
                {
                    var l = menge.ToList();
                    zeilen.Add(("zwischensumme", new object?[] { text, l.Count(p => p.Fehlstunde), Fehlzeitenrechnung.Hhmm(l.Sum(p => p.Minuten)), "", "", "", "", "", "" }));
                }
                Summe("Summe", eigene);
                Summe("davon unent.", eigene.Where(U));
                Summe("Summe (zählend)", eigene.Where(p => p.Zaehlt));
                Summe("davon unent.", eigene.Where(p => p.Zaehlt && U(p)));
            }
            else
            {
                var tage = Fehlzeitenrechnung.Tage(eigene, plan);
                foreach (var t in tage)
                {
                    var erste = eigene.First(p => p.Datum == t.Datum);
                    var f = fzNach[erste.Id];
                    zeilen.Add(("", new object?[] { TagPunkt(t.Datum, ""), t.Fehlstunden, t.Unterrichtsstunden, Fehlzeitenrechnung.Hhmm(t.Fehlminuten), Fehlzeitenrechnung.Hhmm(t.Unterrichtsminuten),
                        ProzentOriginal(t.Prozent), Grund(t.Grund), KurzDatum(f.EntschuldigtAm), t.Unentschuldigt ? "nicht entsch." : "entsch." }));
                }
                // Unterricht im Zeitraum (bis heute) als Bezugsgröße der Summen
                int unt = 0, untMin = 0;
                for (var d = von; d <= bis && d <= _uhr.Heute; d = d.AddDays(1))
                {
                    var pl = plan(x.Klasse, Plandienst.Iso(d));
                    unt += pl.Count;
                    untMin += pl.Sum(n => n.Minuten);
                }
                void Summe(string text, IEnumerable<Fehlposten> menge)
                {
                    var l = menge.ToList();
                    var min = l.Sum(p => p.Minuten);
                    zeilen.Add(("zwischensumme", new object?[] { text, l.Count(p => p.Fehlstunde), unt, Fehlzeitenrechnung.Hhmm(min), Fehlzeitenrechnung.Hhmm(untMin),
                        ProzentOriginal(Fehlzeitenrechnung.Anteil(min, untMin)), "", "", "" }));
                }
                Summe("Summe", eigene);
                Summe("davon unent.", eigene.Where(U));
                Summe("Summe (zählend)", eigene.Where(p => p.Zaehlt));
                Summe("davon unent.", eigene.Where(p => p.Zaehlt && U(p)));
            }
        }
        return new Bericht(info.Id, titel, spaltenD, zeilen.Select(z => z.Zeile.ToList()).ToList(),
            (proTag ? "Pro Tag: An der Prozentangabe ist zu erkennen, ob es sich um einen ganzen Schultag handelt (100 %). " : "Pro Stunde: Schulstunden und Zeitstunden je Fehlzeit mit der unterrichtenden Lehrkraft. ") + hinweisBasis,
            zeilen.Select(z => z.Art).ToList())
        {
            // wie im Original: „Fehlstunden“ über Schulst/Std. bzw. „Schulstd.“ und „hh:mm“ über Fehlst/Unt.; je Schüler*in eine Seite (außer „ohne Seitenumbrüche“)
            Darstellung = new Berichtsdarstellung
            {
                Koepfe = proTag ? new() { "Datum", "Fehlst", "Unt.", "Fehlst", "Unt.", "%", "Abwesenheitsgrund", "Erledigt", "Status" } : null,
                Gruppen = proTag ? new() { "", "Schulstd.", "Schulstd.", "hh:mm", "hh:mm", "", "", "", "" } : new() { "", "Fehlstunden", "Fehlstunden", "", "", "", "", "", "" },
                Seitenumbruch = info.Id == "fehlzeiten_schueler",
            },
        };
    }

    // ------------------------------------------------------------------------------------------ Klassenbuch: Deckblatt, Wochenübersicht, Einträge

    private Bericht Deckblatt(Stand s, Berichtsart info, DateOnly von, DateOnly bis, string klasse)
    {
        var kl = s.Paket.Elemente.Klassen.FirstOrDefault(k => Ci.Equals(k.Kurz, klasse)) ?? throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        var lehrer = s.Paket.Elemente.Lehrer.ToDictionary(l => l.Kurz, l => $"{l.Vorname} {l.Name}".Trim(), Ci);
        string L(string k) => lehrer.TryGetValue(k, out var n) && n.Length > 0 ? $"{n} ({k})" : k;
        var kinder = Klassenzugehoerigkeit.ImZeitraum(s, new[] { kl.Kurz }, von, bis).OrderBy(x => x.Name, De).ThenBy(x => x.Vorname, De).ToList();
        var schule = s.Paket.Schule;
        var faecher = _listen.Unterrichte(s, von < _uhr.Heute ? (bis < _uhr.Heute ? bis : _uhr.Heute) : von, null, kl.Kurz)
            .Select(u => $"{u.Fach} – {string.Join(", ", u.Lehrer.Select(L))}").Distinct().ToList();
        var zeilen = new List<(string, object?[])>
        {
            ("block", new object?[] { "Klassenbuch", "" }),
            ("", new object?[] { "Schule", schule.Name }),
            ("", new object?[] { "Klasse", kl.Name.Length > 0 && kl.Name != kl.Kurz ? $"{kl.Kurz} ({kl.Name})" : kl.Kurz }),
            ("", new object?[] { "Schuljahr", schule.SchuljahrVon.Length > 0 ? $"{KurzDatum(schule.SchuljahrVon)} – {KurzDatum(schule.SchuljahrBis)}" : "" }),
            ("", new object?[] { "Zeitraum", $"{von:dd.MM.yyyy} – {bis:dd.MM.yyyy}" }),
            ("", new object?[] { "Klassenleitung", string.Join(", ", kl.Klassenleitung.Select(L)) }),
            ("", new object?[] { "Stammraum", kl.Stammraum }),
            ("", new object?[] { "Schüler*innen", kinder.Count }),
            ("block", new object?[] { "Unterricht", "" }),
        };
        zeilen.AddRange(faecher.Select(f => ("", new object?[] { "Fach – Lehrkraft", f })));
        zeilen.Add(("block", new object?[] { "Schüler*innen", "" }));
        zeilen.AddRange(kinder.Select((x, i) => ("", new object?[] { $"{i + 1}.", $"{x.Name}, {x.Vorname}" })));
        return new Bericht(info.Id, info.Titel, new() { "Angabe", "Wert" }, zeilen.Select(z => z.Item2.ToList()).ToList(), null, zeilen.Select(z => z.Item1).ToList())
        { Darstellung = new Berichtsdarstellung { Kontext = new(), Gitter = false, OhneKopf = true, Breit = new() { 1 } } };
    }

    private Bericht Wochenuebersicht(Stand s, Berichtsart info, DateOnly von, DateOnly bis, string klasse, bool mitAbwesenheiten)
    {
        var tage = Math.Max(5, s.Paket.Zeitraster.TageProWoche);
        var start = Plandienst.Montag(von);
        var ende = bis < start.AddDays(6) ? start.AddDays(tage - 1) : bis;
        var namen = Schueler(s);
        var fz = _kb.FehlzeitenZeitraum(start, ende, klasse);
        var eintraege = _kb.Klasse(klasse, start, ende).OrderBy(e => e.Datum, StringComparer.Ordinal).ThenBy(e => e.Stunde).ToList();
        var zeilen = new List<(string, object?[])>();
        var mehrere = ende.DayNumber - start.DayNumber > 6;
        foreach (var woche in eintraege.GroupBy(e => Plandienst.Montag(DateOnly.Parse(e.Datum, CultureInfo.InvariantCulture))))
        {
            if (mehrere) zeilen.Add(("gruppe", new object?[] { $"Woche {woche.Key:dd.MM.} – {woche.Key.AddDays(tage - 1):dd.MM.yyyy}", "", "", "", "", "", "" }));
            foreach (var e in woche)
            {
                var fehlend = fz.Where(f => f.Datum == e.Datum && f.Stunde == e.Stunde && f.Art == "fehlt").Select(f => f.SchuelerId).Distinct().ToList();
                zeilen.Add(("", new object?[] { DateOnly.TryParse(e.Datum, out var d) ? d.ToString("dd.MM.yyyy") : e.Datum, $"{e.Stunde}.", e.Fach, e.Lehrer, e.Thema, e.Hausaufgabe,
                    mitAbwesenheiten ? string.Join(", ", fehlend.Select(id => Name(namen, id)).OrderBy(n => n, De)) : fehlend.Count }));
            }
        }
        return new Bericht(info.Id, info.Titel, new() { "Datum", "Std.", "Fach", "Lehrkraft", "Lehrstoff", "Hausaufgabe", mitAbwesenheiten ? "Abwesende Schüler*innen" : "Fehlende" },
            zeilen.Select(z => z.Item2.ToList()).ToList(), mehrere ? $"Lehrnachweis {start:dd.MM.yyyy} – {ende:dd.MM.yyyy}" : $"Woche {start:dd.MM.} – {ende:dd.MM.yyyy}",
            mehrere ? zeilen.Select(z => z.Item1).ToList() : null);
    }

    private Bericht Eintragsbericht(Stand s, Berichtsart info, DateOnly von, DateOnly bis, string? klasse, int? schueler, IReadOnlyDictionary<string, string> o)
    {
        var kat = Wert(o, info, "kategorie");
        var arten = kat.StartsWith("gruppe:", StringComparison.Ordinal) && Klassenbuchdienst.VermerkGruppen.TryGetValue(kat[7..], out var g) ? g
            : kat.Length > 0 ? new[] { kat } : null;
        var liste = _listen.Vermerke(s, von, bis, string.IsNullOrEmpty(klasse) ? null : klasse, null)
            .Where(v => (schueler is null || v.SchuelerId == schueler) && (arten is null || arten.Contains(v.Art, Ci)));
        // pro Schüler*in bzw. „sortiert nach Schülern/Schülerinnen“: nach Name, sonst nach Datum (wie die Liste der Klassenbucheinträge)
        liste = info.Id == "klassenbucheintraege_schueler" ? liste.Where(v => v.SchuelerId != null).OrderBy(v => v.Schueler, De).ThenBy(v => v.Datum, StringComparer.Ordinal).ThenBy(v => v.Zeit, StringComparer.Ordinal)
            : An(o, info, "sortiert") ? liste.OrderBy(v => v.Klasse, De).ThenBy(v => v.Schueler, De).ThenBy(v => v.Datum, StringComparer.Ordinal).ThenBy(v => v.Zeit, StringComparer.Ordinal)
            : liste.OrderBy(v => v.Klasse, De).ThenBy(v => v.Datum, StringComparer.Ordinal).ThenBy(v => v.Zeit, StringComparer.Ordinal);
        var kategorien = Klassenbuchdienst.VermerkGruppen.ToDictionary(x => x.Key, x => x.Value);
        string Gruppe(string a) => kategorien.FirstOrDefault(x => x.Value.Contains(a, Ci)).Key ?? "";
        // Fach der Stunde, in der der Eintrag erfasst wurde (laut Plan der Klasse)
        var faecher = new Dictionary<(string, string), List<Karte>>();
        string Fach(Vermerkzeile v)
        {
            if (v.Stunde <= 0 || !DateOnly.TryParse(v.Datum, CultureInfo.InvariantCulture, out var d)) return "";
            if (!faecher.TryGetValue((v.Klasse, v.Datum), out var karten)) faecher[(v.Klasse, v.Datum)] = karten = _plan.Karten(s, d, Elementtyp.Klasse, v.Klasse);
            return karten.FirstOrDefault(k => k.Von <= v.Stunde && v.Stunde <= k.Bis && k.Unterricht.HasValue)?.Fach ?? "";
        }
        // Spalten wie die Liste „Klassenbucheinträge“ des Originals (Bericht-Knopf): Name, Klasse, Tag, Datum, Zeit, Fach, Benutzer, Eintragskategorie, Kategoriegruppe, Text
        return new Bericht(info.Id, info.Titel + (kat.Length > 0 ? $" – {(arten is { Length: > 1 } ? kat[7..] : kat)}" : ""),
            new() { "Name", "Klasse", "Tag", "Datum", "Zeit", "Fach", "Benutzer", "Eintragskategorie", "Kategoriegruppe", "Text" },
            liste.Select(v => new List<object?> { v.Schueler, v.Klasse,
                DateOnly.TryParse(v.Datum, CultureInfo.InvariantCulture, out var d) ? d.ToString("ddd", DeKultur).TrimEnd('.') + "." : "", DateOnly.TryParse(v.Datum, CultureInfo.InvariantCulture, out var d2) ? d2.ToString("dd.MM.yyyy") : v.Datum,
                v.Zeit, Fach(v), v.Lehrer, v.Art, Gruppe(v.Art), v.Text }).ToList(),
            "Maßnahmen erscheinen nur, wenn sie als Eintrag ins Klassenbuch vermerkt wurden und nicht als Abwesenheit.")
        { Darstellung = new Berichtsdarstellung { Kontext = string.IsNullOrEmpty(klasse) ? null : new() { $"Klasse: {klasse}" } } };
    }

    // ------------------------------------------------------------------------------------------ Entschuldigungsschreiben

    /// <summary>
    /// Entschuldigungsschreiben je Schüler*in (Seite mit Unterschriftsfeld): „pro Stunde“ (je Fehlstunde mit Fach) oder „pro Tag“, „Gruppierung pro Woche“
    /// (je Woche ein eigenes Schreiben), „Entschuldigte Stunden anzeigen“ (sonst nur die noch nicht entschuldigten).
    /// </summary>
    private Bericht Entschuldigungsschreiben(Stand s, Benutzer b, Berichtsart info, DateOnly von, DateOnly bis, string? klasse, int? schueler, IReadOnlyDictionary<string, string> o)
    {
        var namen = Schueler(s);
        var mitEntschuldigten = An(o, info, "entschuldigte");
        var woche = An(o, info, "woche");
        var proTag = Wert(o, info, "ausgabe") == "pro_tag";
        var gruende = Gruende().GroupBy(g => g.Kurz, Ci).ToDictionary(g => g.Key, g => g.First().Name, Ci);
        string Grund(string g) => gruende.TryGetValue(g, out var n) ? n : g;
        var fz = _kb.FehlzeitenZeitraum(von, bis, string.IsNullOrEmpty(klasse) ? null : klasse)
            .Where(f => f.Art == "fehlt" && (schueler is null || f.SchuelerId == schueler) && (mitEntschuldigten || f.Status != "entschuldigt")).ToList();
        string Wer(Fehlzeit f)
        {
            var name = Name(namen, f.SchuelerId);
            if (!woche || !DateOnly.TryParse(f.Datum, CultureInfo.InvariantCulture, out var d)) return name;
            var mo = Plandienst.Montag(d);
            return $"{name} – Woche {mo:dd.MM.} – {mo.AddDays(6):dd.MM.yyyy}";
        }
        string Datum(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? d.ToString("dd.MM.yyyy") : iso;
        string Status(IEnumerable<Fehlzeit> l) => l.All(f => f.Status == "entschuldigt") ? "entschuldigt" : l.Any(f => f.Status == "unentschuldigt") ? "unentschuldigt" : "offen";
        var zeilen = proTag
            ? fz.GroupBy(f => (f.SchuelerId, f.Datum)).Select(g => (Sort: Wer(g.First()), g.Key.Datum, Stunde: g.Min(f => f.Stunde), Z: new List<object?> { Wer(g.First()), g.First().Klasse, Datum(g.Key.Datum),
                    g.Min(f => f.Stunde) == g.Max(f => f.Stunde) ? $"{g.Min(f => f.Stunde)}." : $"{g.Min(f => f.Stunde)}.–{g.Max(f => f.Stunde)}.",
                    string.Join(", ", g.Select(f => f.Fach).Where(x => x.Length > 0).Distinct(Ci)), string.Join(", ", g.Select(f => Grund(f.Grund)).Where(x => x.Length > 0).Distinct(Ci)), Status(g) }))
            : fz.Select(f => (Sort: Wer(f), f.Datum, f.Stunde, Z: new List<object?> { Wer(f), f.Klasse, Datum(f.Datum), $"{f.Stunde}.", f.Fach, Grund(f.Grund), f.Status }));
        return new Bericht(info.Id, info.Titel, new() { "Schüler*in", "Klasse", "Datum", "Stunden", "Fächer", "Grund", "Status" },
            zeilen.OrderBy(z => z.Sort, De).ThenBy(z => z.Datum, StringComparer.Ordinal).ThenBy(z => z.Stunde).Select(z => z.Z).ToList(),
            "Je Schüler*in eine Seite mit den Fehlstunden zum Unterschreiben durch die Erziehungsberechtigten.");
    }

    // ------------------------------------------------------------------------------------------ Berichte zum Unterricht

    /// <summary>Gehaltene Stunde eines Unterrichts (ohne Entfall) mit Datum, Stunden von–bis und Lehrkräften.</summary>
    private sealed record GehalteneStunde(DateOnly Datum, int Von, int Bis, string Beginn, string Ende, List<string> Lehrer, Karte Karte);

    private Bericht Unterrichtsbericht(Stand s, Benutzer b, Berichtsart info, DateOnly von, DateOnly bis, IReadOnlyDictionary<string, string> o)
    {
        if (!o.TryGetValue("unterricht", out var uw) || !int.TryParse(uw, out var uid)) throw new ArgumentException("Bitte einen Unterricht wählen.");
        var u = _listen.Unterrichte(s, von <= _uhr.Heute ? (bis < _uhr.Heute ? bis : _uhr.Heute) : von, null, null).FirstOrDefault(x => x.Id == uid)
                ?? _listen.Unterrichte(s, von, null, null).FirstOrDefault(x => x.Id == uid) ?? throw new ArgumentException("Unbekannter Unterricht.");
        var darf = Rechtedienst.Darf(b, Recht.KlassenbuchAlle) || (b.LehrerKuerzel is string lk && u.Lehrer.Contains(lk, Ci)) || u.Klassen.Any(k => Klassenbuchdienst.IstKlassenleitung(b, s, k));
        if (!darf) throw new UnauthorizedAccessException("Berichte zum Unterricht stehen den Lehrkräften des Unterrichts und der Klassenleitung zur Verfügung.");
        // gehaltene Stunden im Datumsbereich (bis heute)
        var stunden = new List<GehalteneStunde>();
        for (var d = von; d <= bis && d <= _uhr.Heute; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            var gesehen = new HashSet<(int, int)>();
            foreach (var k in u.Klassen)
                foreach (var karte in _plan.Karten(s, d, Elementtyp.Klasse, k).Where(x => x.Unterricht == uid && x.Status is not ("entfall" or "veranstaltung")))
                    if (gesehen.Add((karte.Von, karte.Bis)))
                        stunden.Add(new GehalteneStunde(d, karte.Von, karte.Bis, Hm(Klassenbuchdienst.StundenZeit(s, karte.Von).Beginn), Hm(Klassenbuchdienst.StundenZeit(s, karte.Bis).Ende),
                            karte.Lehrer.Select(l => l.K).Where(l => l.Length > 0).ToList(), karte));
        }
        stunden = stunden.OrderBy(x => x.Datum).ThenBy(x => x.Von).ToList();
        int Minuten(int n) { var (sb, se) = Klassenbuchdienst.StundenZeit(s, n); return (int)Math.Round((se - sb).TotalMinutes); }
        var anzahl = stunden.Sum(x => x.Bis - x.Von + 1);
        var gesamtMin = stunden.Sum(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1).Sum(Minuten));
        var namen = Schueler(s);
        var schuelerIds = new HashSet<int>();
        foreach (var st in stunden) foreach (var x in _kb.SchuelerDerStunde(s, st.Karte, st.Datum)) schuelerIds.Add(x.Id);
        if (stunden.Count == 0) foreach (var x in Klassenbuchdienst.Schueler(s, u.Klassen)) schuelerIds.Add(x.Id);
        var gehalten = stunden.SelectMany(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1).Select(n => (Plandienst.Iso(x.Datum), n))).ToHashSet();
        var fz = u.Klassen.SelectMany(k => _kb.FehlzeitenZeitraum(von, bis, k)).Where(f => gehalten.Contains((f.Datum, f.Stunde)) && (f.Unterricht == uid || f.Unterricht == 0)).ToList();
        var gruende = Gruende().GroupBy(g => g.Kurz, Ci).ToDictionary(g => g.Key, g => g.First().Name, Ci);
        string Grund(string g) => gruende.TryGetValue(g, out var n) ? n : g;
        var kopf = $"Klasse: {string.Join(", ", u.Klassen)}, Fach: {u.FachName}{(u.Kurs.Length > 0 ? $", Schülergruppe: {u.Kurs}" : "")}, Lehrkraft: {string.Join(", ", u.Lehrer)} · Unterrichtsstunden {anzahl} ({Fehlzeitenrechnung.Hhmm(gesamtMin)}) · {von:dd.MM.yyyy} – {bis:dd.MM.yyyy}";
        // Kopfzeilen im PDF wie im Original
        var kontextFach = $"Klasse: {string.Join(", ", u.Klassen)}, Fach: {u.Fach}{(u.Kurs.Length > 0 ? $", Schülergruppe: {u.Kurs}" : "")}, Lehrkraft: {string.Join(", ", u.Lehrer)}";
        var stundenText = $"Unterrichtsstunden\t{anzahl}\t{Fehlzeitenrechnung.Hhmm(gesamtMin)}";
        string Std(GehalteneStunde x) => x.Von == x.Bis ? $"{x.Von}" : $"{x.Von} - {x.Bis}";
        var enr = Entschuldigungsnummern(fz);

        switch (info.Id)
        {
            case "arbeitsbericht_unterricht":
            {
                var eintraege = u.Klassen.SelectMany(k => _kb.Klasse(k, von, bis)).Where(e => e.Unterricht == uid).ToList();
                return new Bericht(info.Id, "Arbeitsbericht zum Unterricht", new() { "Datum", "Std.", "Lehrkraft", "Thema", "Hausaufgaben", "Abw. Schüler*innen" },
                    stunden.Select(x =>
                    {
                        var e = eintraege.Where(e => e.Datum == Plandienst.Iso(x.Datum) && e.Stunde >= x.Von && e.Stunde <= x.Bis).ToList();
                        var abw = fz.Where(f => f.Datum == Plandienst.Iso(x.Datum) && f.Stunde >= x.Von && f.Stunde <= x.Bis).Select(f => f.SchuelerId).Distinct()
                            .Select(id => Name(namen, id)).OrderBy(n => n, De);
                        return new List<object?> { x.Datum.ToString("dd.MM.yyyy"), Std(x), string.Join(", ", x.Lehrer), string.Join(" / ", e.Select(y => y.Thema).Where(t => t.Length > 0)),
                            string.Join(" / ", e.Select(y => y.Hausaufgabe).Where(t => t.Length > 0)), string.Join("\n", abw) };
                    }).ToList(), kopf)
                { Darstellung = new Berichtsdarstellung { Kontext = new() { $"Klasse: {string.Join(", ", u.Klassen)}, Fach: {u.FachName}", stundenText }, KontextFett = true, KopfFett = true, Breit = new() { 3, 4, 5 } } };
            }
            case "fehlzeiten_unterricht_stunde":
                return new Bericht(info.Id, "Fehlzeiten pro Unterricht pro Stunde", new() { "Datum", "Std.", "Zeit", "Lehrkraft", "Schüler*innen", "Fehlstd.", "Abwesenheitsgrund", "ENr", "Status" },
                    stunden.SelectMany(x => fz.Where(f => f.Datum == Plandienst.Iso(x.Datum) && f.Stunde >= x.Von && f.Stunde <= x.Bis)
                        .OrderBy(f => f.Stunde).ThenBy(f => Name(namen, f.SchuelerId), De)
                        .Select(f => new List<object?> { x.Datum.ToString("dd.MM.yyyy"), $"{f.Stunde}", $"{x.Beginn}-{x.Ende}", string.Join(", ", x.Lehrer), Name(namen, f.SchuelerId),
                            f.Art == "fehlt" ? 1 : 0, Grund(f.Grund), enr.GetValueOrDefault(f.Id, ""), StatusKurz(f.Status, f.StatusName) })).ToList(), kopf)
                { Darstellung = new Berichtsdarstellung { Kontext = new() { $"{kontextFach}\t{stundenText}" }, Gitter = false, Breit = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8 } } };
            case "schueler_unterricht":
            {
                var klassen = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
                return new Bericht(info.Id, "Schüler*innen im Unterricht", new() { "Nr.", "Schüler*innen", "Klasse" },
                    schuelerIds.OrderBy(id => Name(namen, id), De).Select((id, i) => new List<object?> { i + 1, Name(namen, id), klassen.TryGetValue(id, out var k) ? k.Klasse : "" }).ToList(), kopf)
                { Darstellung = new Berichtsdarstellung { Kontext = new() { kontextFach } } };
            }
            case "fehlstundenmatrix":
            {
                var ohne = An(o, info, "ohne");
                var spalten = new List<string> { "Schüler*innen", "Klasse" };
                var einzel = stunden.SelectMany(x => Enumerable.Range(x.Von, x.Bis - x.Von + 1).Select(n => (x.Datum, n))).ToList();
                spalten.AddRange(einzel.Select(e => $"{e.Datum.Day}.{e.Datum.Month}. {Hm(Klassenbuchdienst.StundenZeit(s, e.n).Beginn)}"));
                var fehlt = fz.Select(f => (f.SchuelerId, f.Datum, f.Stunde)).ToHashSet();
                var klassen = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Klasse);
                // Kopf wie im Original: „Fach M, Lehrkraft BEC,MUE, Klasse 7a, Schülergruppe“, Datumsspalten senkrecht
                return new Bericht(info.Id, "Fehlstundenmatrix", spalten,
                    schuelerIds.OrderBy(id => Name(namen, id), De).Select(id => new List<object?> { Name(namen, id), klassen.GetValueOrDefault(id, "") }
                        .Concat(einzel.Select(e => (object?)(!ohne && fehlt.Contains((id, Plandienst.Iso(e.Datum), e.n)) ? "A" : ""))).ToList()).ToList(),
                    $"Fach {u.Fach}, Lehrkraft {string.Join(",", u.Lehrer)}, Klasse {string.Join(", ", u.Klassen)}{(u.Kurs.Length > 0 ? $", Schülergruppe {u.Kurs}" : "")}. A = abwesend (ohne Entschuldigungsstatus).")
                {
                    Darstellung = new Berichtsdarstellung
                    {
                        Kontext = new() { $"Fach {u.Fach}, Lehrkraft {string.Join(",", u.Lehrer)}, Klasse {string.Join(", ", u.Klassen)}, Schülergruppe{(u.Kurs.Length > 0 ? $" {u.Kurs}" : "")}" },
                        Koepfe = new[] { "Schüler*innen", "" }.Concat(spalten.Skip(2)).ToList(), SenkrechtAb = 2,
                    },
                };
            }
            default:
            {
                // Fehlzeiten pro Unterricht pro Schüler*in (75 %-Regel vor der abschließenden Leistungsfeststellung) wie im Original: je Fehlzeit eine Zeile
                // mit Schüler*in, Datum, Fehlstd., Zeit, Abwesenheitsgrund, ENr, Erledigt; am Ende Summe und Summe (zählend), je davon unent., bezogen auf
                // die gehaltenen Unterrichtsstunden
                var posten = _kb.Fehlposten(s, fz);
                var wirksam = Fehlzeitenrechnung.WirksamerStatus(posten);
                bool U(Fehlposten p) => Fehlzeitenrechnung.Unentschuldigt(wirksam[p.Id]);
                var fzNach = fz.ToDictionary(f => f.Id);
                var zeilen = new List<(string, object?[])>();
                string DM(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? d.ToString("d.M.", DeKultur) : "";
                foreach (var p in posten.Where(p => schuelerIds.Contains(p.SchuelerId)).OrderBy(p => Name(namen, p.SchuelerId), De).ThenBy(p => p.Datum, StringComparer.Ordinal).ThenBy(p => p.Stunde))
                {
                    var f = fzNach[p.Id];
                    var (ab, bi) = Klassenbuchdienst.Fenster(s, f);
                    zeilen.Add(("", new object?[] { Name(namen, p.SchuelerId), DM(p.Datum), p.Fehlstunde ? 1 : 0, $"{Hm(ab)}-{Hm(bi)}", Grund(p.Grund),
                        enr.GetValueOrDefault(p.Id, ""), DM(f.EntschuldigtAm) }));
                }
                var inListe = posten.Where(p => schuelerIds.Contains(p.SchuelerId)).ToList();
                (int N, string Zeit, string Anteil) Werte(List<Fehlposten> l)
                {
                    var min = l.Sum(p => p.Minuten);
                    return (l.Count(p => p.Fehlstunde), Fehlzeitenrechnung.Hhmm(min), ProzentOriginal(Fehlzeitenrechnung.Anteil(min, gesamtMin), 2));
                }
                var zaehl = inListe.Where(p => p.Zaehlt).ToList();
                var (a1, a2, a3) = Werte(inListe);
                var (b1, b2, b3) = Werte(zaehl);
                zeilen.Add(("summe", new object?[] { "Summe", a1, a2, a3, "Summe (zählend)", b1, $"{b2}   {b3}" }));
                (a1, a2, a3) = Werte(inListe.Where(U).ToList());
                (b1, b2, b3) = Werte(zaehl.Where(U).ToList());
                zeilen.Add(("zwischensumme", new object?[] { "davon unent.", a1, a2, a3, "davon unent.", b1, $"{b2}   {b3}" }));
                return new Bericht(info.Id, "Fehlzeiten pro Unterricht", new() { "Schüler*innen", "Datum", "Fehlstd.", "Zeit", "Abwesenheitsgrund", "ENr", "Erledigt" },
                    zeilen.Select(z => z.Item2.ToList()).ToList(),
                    $"{kopf}. Anteil = Fehlzeit bezogen auf die gehaltenen Unterrichtsstunden. An der abschließenden Leistungsfeststellung darf nur teilnehmen, wer 75 % des Unterrichts besucht hat. Ist der Unterricht auf mehrere Zeilen der Unterrichtsliste verteilt, die Zahlen der Listen addieren.",
                    zeilen.Select(z => z.Item1).ToList())
                { Darstellung = new Berichtsdarstellung { Kontext = new() { $"{kontextFach}\t{stundenText}" }, Gitter = false, Breit = new() { 0, 1, 2, 3, 4, 5, 6 } } };
            }
        }
    }

    private static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
