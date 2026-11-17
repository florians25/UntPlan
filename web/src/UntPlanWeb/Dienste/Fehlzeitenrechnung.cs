using System.Globalization;

namespace UntPlanWeb.Dienste;

/// <summary>Entschuldigungsstatus einer Abwesenheit mit Bedeutung; <see cref="Basis"/> ist der gezählte Status (offen | entschuldigt | unentschuldigt).</summary>
public sealed record Entschuldigungsstatus(string Name, string Bedeutung, string Basis);

/// <summary>
/// Eine Fehlzeit (Unterrichtsstunde laut Stundenplan) für die Zählung: Art (fehlt | verspaetet | vorzeitig), Status, Abwesenheitsgrund,
/// Schlüssel der Abwesenheit, zu der sie gehört, und Fehlminuten (minutengenau). <see cref="GrundZaehlt"/> = der Abwesenheitsgrund zählt
/// (z. B. „Schul“ – andere schulische Veranstaltung – zählt nicht), <see cref="Befreit"/> = die Schülerin bzw. der Schüler war in der Stunde befreit.
/// </summary>
public sealed record Fehlposten(int Id, int SchuelerId, string Klasse, string Datum, int Stunde, string Art, string Status, string Grund, string Abwesenheit,
    int Minuten, bool GrundZaehlt = true, bool Befreit = false)
{
    /// <summary>Als Fehlstunde (Schulstunde) gewertet – Verspätungen und vorzeitiges Gehen zählen nur mit ihren Minuten.</summary>
    public bool Fehlstunde => Art == "fehlt";
    /// <summary>Zählt in den Berichten und damit im Zeugnis: Grund zählt und keine Befreiung.</summary>
    public bool Zaehlt => GrundZaehlt && !Befreit;
}

/// <summary>Unterrichtsstunde laut Stundenplan an einem Tag (Stundennummer und Dauer in Minuten).</summary>
public sealed record Planeinheit(int Stunde, int Minuten);

/// <summary>Fehltag bzw. Tag mit Fehlzeiten („pro Tag“): Fehl- und Unterrichtsstunden, Minuten, Anteil am Unterricht des Tages.</summary>
public sealed record Fehltagzeile(int SchuelerId, string Datum, int Fehlstunden, int Unterrichtsstunden, int Fehlminuten, int Unterrichtsminuten,
    bool Fehltag, bool Unentschuldigt, bool Zaehlt, string Grund)
{
    /// <summary>Anteil der Fehlzeit am Unterricht des Tages (Zeitstunden) in Prozent; 100 % = ganzer Schultag.</summary>
    public double Prozent => Fehlzeitenrechnung.Anteil(Fehlminuten, Unterrichtsminuten);
}

/// <summary>
/// Summen je Schüler*in wie im Bericht „Fehlzeiten pro Klasse“ (Gesamtübersicht) bzw. am Ende von „Fehlzeiten pro Schüler*in“:
/// Fehlschulstunden, Fehlstunden in Zeitstunden (Minuten) und Fehltage – je „alle“, „unentschuldigt“, „zählend“ und „zählend, unentschuldigt“.
/// </summary>
public sealed record Fehlauswertung(int SchuelerId,
    int Schulstunden, int SchulstundenUnent, int SchulstundenZaehlend, int SchulstundenZaehlendUnent,
    int Minuten, int MinutenUnent, int MinutenZaehlend, int MinutenZaehlendUnent,
    int Fehltage, int FehltageUnent, int FehltageZaehlend, int FehltageZaehlendUnent);

/// <summary>Abwesenheit für „Zusammenfassen“: Schlüssel, Schüler*in, Grund, Status (leer bzw. „offen“ = nicht gesetzt) und Zeitraum.</summary>
public sealed record Zusammenfassblock(string Schluessel, int SchuelerId, string Grund, string Status, DateTime Von, DateTime Bis);

/// <summary>
/// Zählregeln für Abwesenheiten und Fehlzeiten (Klassenleitung → Kontrolle der Abwesenheiten, Berichte, Zeugnis) – rein rechnend, ohne Datenbank:
/// <list type="bullet">
/// <item>Aus Abwesenheiten werden über den Stundenplan Fehlzeiten. Fehlt die Schülerin bzw. der Schüler in allen Unterrichtsstunden des Tages,
/// ist das ein Fehltag, sonst sind es Fehlstunden. Beispiel: 3 Stunden Abwesenheit, davon 2 während des Unterrichts → 2 Fehlstunden.</item>
/// <item>Der Status der Abwesenheit bestimmt, ob ihre Fehlzeiten entschuldigt sind. Haben die Fehlzeiten einer Abwesenheit verschiedene Status,
/// ist der Status der Abwesenheit unbestimmt und damit alle ihre Fehlzeiten unentschuldigt. Nur „entschuldigt“ zählt als entschuldigt;
/// [offen] und alle anderen Status zählen als „nicht entschuldigt“.</item>
/// <item>Ein Fehltag ist unentschuldigt, sobald eine seiner Fehlstunden unentschuldigt ist.</item>
/// <item>Fehlzeiten während einer Befreiung und mit einem nicht zählenden Abwesenheitsgrund zählen nicht.</item>
/// <item>„Fehltage und -zeiten trennen“: Fehlstunden ganzer Fehltage werden nicht zusätzlich als Fehlstunden gerechnet.</item>
/// </list>
/// </summary>
public static class Fehlzeitenrechnung
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Die Entschuldigungsstatus mit Bedeutung (Reihenfolge der Auswahl im Dialog „Entschuldigung“).</summary>
    public static readonly IReadOnlyList<Entschuldigungsstatus> Statusliste = new List<Entschuldigungsstatus>
    {
        new("[offen]", "Die Abwesenheit wurde registriert, die Klassenleitung hat sich aber noch nicht gekümmert. Zählt wie „nicht entschuldigt.“", "offen"),
        new("Entschuldigt", "Die Entschuldigung wurde rechtzeitig abgegeben und enthält einen akzeptablen Grund. Das ist die einzige Möglichkeit für eine Abwesenheit, am Ende als „entschuldigt“ zu zählen; alle anderen Status zählen in den Berichten und damit im Zeugnis als „unentschuldigt.“", "entschuldigt"),
        new("Nicht entschuldigt", "Es wurde keine Entschuldigung abgegeben.", "unentschuldigt"),
        new("Nicht akzeptiert", "Die Entschuldigung wurde nicht akzeptiert, z. B. weil der Grund nicht akzeptabel war.", "unentschuldigt"),
        new("Zu spät", "Es wurde zwar eine Entschuldigung abgegeben, aber erst nach Ablauf der 3-Tages-Frist.", "unentschuldigt"),
        new("u-BußgAnd", "unentschuldigt, bei der Androhung des Bußgeldes verwendet", "unentschuldigt"),
        new("u-SchAAnd", "unentschuldigt, bei der Androhung des Schulausschlusses verwendet", "unentschuldigt"),
        new("u-1Mahn", "unentschuldigt, bei der ersten Mahnung gemahnt", "unentschuldigt"),
        new("u-2Mahn", "unentschuldigt, bei der zweiten Mahnung gemahnt", "unentschuldigt"),
        new("u-Bußgeld", "unentschuldigt, beim Bußgeldantrag verwendet", "unentschuldigt"),
        new("u-SchA", "unentschuldigt, beim Schulausschluss verwendet", "unentschuldigt"),
        new("u-3...Mahn", "unentschuldigt, bei weiterer Mahnung gemahnt", "unentschuldigt"),
    };

    /// <summary>
    /// Auswahl im Dialog (Name aus <see cref="Statusliste"/> oder einer der Grundstatus offen/entschuldigt/unentschuldigt) → gezählter Status und
    /// gespeicherter Statusname (leer bei den drei Grundstatus). Unbekannte Auswahl: null.
    /// </summary>
    public static (string Status, string Name)? StatusAus(string? auswahl)
    {
        var a = (auswahl ?? "").Trim();
        switch (a.ToLowerInvariant())
        {
            case "offen" or "[offen]": return ("offen", "");
            case "entschuldigt": return ("entschuldigt", "");
            case "unentschuldigt" or "nicht entschuldigt": return ("unentschuldigt", "");
        }
        var s = Statusliste.FirstOrDefault(x => string.Equals(x.Name, a, StringComparison.OrdinalIgnoreCase));
        return s is null ? null : (s.Basis, s.Name);
    }

    /// <summary>Anzeigename eines Status (gespeicherter Name, sonst „[offen]“, „entschuldigt“ bzw. „nicht entschuldigt“).</summary>
    public static string StatusText(string status, string? name = null) => !string.IsNullOrEmpty(name) ? name! : status switch
    {
        "entschuldigt" => "entschuldigt",
        "unentschuldigt" => "nicht entschuldigt",
        _ => "[offen]",
    };

    /// <summary>Zählt als „nicht entschuldigt“: alles außer „entschuldigt“.</summary>
    public static bool Unentschuldigt(string status) => status != "entschuldigt";

    /// <summary>
    /// Wirksamer Status je Fehlzeit: haben die Fehlzeiten einer Abwesenheit (gleiche Schüler*in und gleicher Abwesenheitsschlüssel)
    /// unterschiedliche Status, gelten alle als „unentschuldigt“, sonst der eigene Status.
    /// </summary>
    public static Dictionary<int, string> WirksamerStatus(IEnumerable<Fehlposten> posten)
    {
        var res = new Dictionary<int, string>();
        foreach (var g in posten.GroupBy(p => (p.SchuelerId, p.Abwesenheit)))
        {
            var gemischt = g.Select(p => p.Status).Distinct().Count() > 1;
            foreach (var p in g) res[p.Id] = gemischt ? "unentschuldigt" : p.Status;
        }
        return res;
    }

    /// <summary>
    /// Tage mit Fehlzeiten je Schüler*in: Fehlstunden und Unterrichtsstunden des Tages (laut <paramref name="plan"/>), Minuten, ob es ein
    /// Fehltag ist (in allen Unterrichtsstunden des Tages gefehlt), ob er unentschuldigt ist (eine Fehlstunde unentschuldigt) und ob er zählt
    /// (alle Fehlstunden zählen).
    /// </summary>
    public static List<Fehltagzeile> Tage(IEnumerable<Fehlposten> posten, Func<string, string, IReadOnlyList<Planeinheit>> plan)
    {
        var liste = posten.ToList();
        var wirksam = WirksamerStatus(liste);
        var res = new List<Fehltagzeile>();
        foreach (var g in liste.GroupBy(p => (p.SchuelerId, p.Datum)).OrderBy(g => g.Key.SchuelerId).ThenBy(g => g.Key.Datum, StringComparer.Ordinal))
        {
            var gewertet = g.Where(p => p.Fehlstunde).ToList();
            var stunden = plan(g.First().Klasse, g.Key.Datum);
            var voll = gewertet.Count > 0 && (stunden.Count > 0 ? stunden.All(n => gewertet.Any(p => p.Stunde == n.Stunde)) : gewertet.Count == g.Count());
            var unt = stunden.Count > 0 ? stunden.Count : g.Count();
            var untMin = stunden.Count > 0 ? stunden.Sum(n => n.Minuten) : g.Sum(p => p.Minuten);
            res.Add(new Fehltagzeile(g.Key.SchuelerId, g.Key.Datum, gewertet.Count, unt, g.Sum(p => p.Minuten), untMin, voll,
                gewertet.Any(p => Unentschuldigt(wirksam[p.Id])) || (gewertet.Count == 0 && g.Any(p => Unentschuldigt(wirksam[p.Id]))),
                g.All(p => p.Zaehlt), g.Select(p => p.Grund).FirstOrDefault(x => x.Length > 0) ?? ""));
        }
        return res;
    }

    /// <summary>
    /// Summen einer Schülerin bzw. eines Schülers. Ohne <paramref name="trennen"/> zählen alle Fehlstunden (auch die ganzer Fehltage) und
    /// die Fehltage zusätzlich; mit „Fehltage und -zeiten trennen“ werden die Stunden ganzer Fehltage nicht als Fehlstunden gerechnet.
    /// Verspätungen zählen nur mit ihren Minuten (0 Schulstunden).
    /// </summary>
    public static Fehlauswertung Auswerten(int schuelerId, IEnumerable<Fehlposten> posten, Func<string, string, IReadOnlyList<Planeinheit>> plan, bool trennen = false)
    {
        var liste = posten.Where(p => p.SchuelerId == schuelerId).ToList();
        var wirksam = WirksamerStatus(liste);
        var tage = Tage(liste, plan);
        var fehltage = tage.Where(t => t.Fehltag).Select(t => t.Datum).ToHashSet();
        var stunden = trennen ? liste.Where(p => !fehltage.Contains(p.Datum)).ToList() : liste;
        bool U(Fehlposten p) => Unentschuldigt(wirksam[p.Id]);
        var fs = stunden.Where(p => p.Fehlstunde).ToList();
        var ft = tage.Where(t => t.Fehltag).ToList();
        return new Fehlauswertung(schuelerId,
            fs.Count, fs.Count(U), fs.Count(p => p.Zaehlt), fs.Count(p => p.Zaehlt && U(p)),
            stunden.Sum(p => p.Minuten), stunden.Where(U).Sum(p => p.Minuten), stunden.Where(p => p.Zaehlt).Sum(p => p.Minuten), stunden.Where(p => p.Zaehlt && U(p)).Sum(p => p.Minuten),
            ft.Count, ft.Count(t => t.Unentschuldigt), ft.Count(t => t.Zaehlt), ft.Count(t => t.Zaehlt && t.Unentschuldigt));
    }

    /// <summary>Anteil in Prozent (zwei Nachkommastellen), 0 bei leerer Basis.</summary>
    public static double Anteil(int teil, int gesamt) => gesamt <= 0 ? 0 : Math.Round(100.0 * teil / gesamt, 2, MidpointRounding.AwayFromZero);

    /// <summary>Prozentangabe wie im Bericht: „15,17 %“ bzw. „100 %“.</summary>
    public static string Prozent(double p) => $"{p.ToString(p % 1 == 0 ? "0" : "0.##", De)} %";

    /// <summary>Zeitstunden wie im Bericht: Minuten als „h:mm“ (z. B. 355 → „5:55“).</summary>
    public static string Hhmm(int minuten) => $"{minuten / 60}:{minuten % 60:00}";

    /// <summary>
    /// 75 %-Regel vor der abschließenden Leistungsfeststellung: Fehlzeit (zählend, Zeitstunden) bezogen auf die tatsächlich gehaltenen
    /// Unterrichtsstunden im Datumsbereich. Teilnehmen darf, wer mindestens <paramref name="mindestAnwesenheit"/> % des Unterrichts besucht hat.
    /// </summary>
    public static (double Fehlanteil, double Anwesenheit, bool Zugelassen) Teilnahme(int fehlMinuten, int gehalteneMinuten, double mindestAnwesenheit = 75)
    {
        var fehl = Anteil(fehlMinuten, gehalteneMinuten);
        var anw = Math.Round(100 - fehl, 2, MidpointRounding.AwayFromZero);
        return (fehl, anw, gehalteneMinuten <= 0 || anw >= mindestAnwesenheit);
    }

    /// <summary>
    /// Abwesenheiten zusammenfassen (alle dargestellten im Zeitbereich): aufeinanderfolgende Abwesenheiten derselben Schüler*in werden
    /// zusammengefasst, wenn die Abwesenheitsgründe übereinstimmen, die Entschuldigungsstatus übereinstimmen (nicht gesetzte Status stimmen
    /// mit jedem überein) und sie zwischen beiden keinen Unterricht hatte, in dem sie anwesend war (<paramref name="anwesendDazwischen"/>).
    /// Ergebnis: Gruppen mit mindestens zwei Abwesenheiten und dem gemeinsamen Status.
    /// </summary>
    public static List<(List<Zusammenfassblock> Bloecke, string Status)> Zusammenfassen(IEnumerable<Zusammenfassblock> bloecke,
        Func<int, DateTime, DateTime, bool> anwesendDazwischen)
    {
        static bool Gesetzt(string s) => s.Length > 0 && s != "offen";
        var res = new List<(List<Zusammenfassblock>, string)>();
        foreach (var g in bloecke.GroupBy(b => b.SchuelerId))
        {
            List<Zusammenfassblock>? akt = null;
            var status = "";
            foreach (var b in g.OrderBy(x => x.Von).ThenBy(x => x.Bis))
            {
                var passt = akt != null && string.Equals(akt[^1].Grund, b.Grund, StringComparison.OrdinalIgnoreCase)
                    && (!Gesetzt(status) || !Gesetzt(b.Status) || status == b.Status)
                    && !anwesendDazwischen(b.SchuelerId, akt.Max(x => x.Bis), b.Von);
                if (passt)
                {
                    akt!.Add(b);
                    if (!Gesetzt(status) && Gesetzt(b.Status)) status = b.Status;
                    continue;
                }
                if (akt is { Count: > 1 }) res.Add((akt, Gesetzt(status) ? status : "offen"));
                akt = new List<Zusammenfassblock> { b };
                status = b.Status;
            }
            if (akt is { Count: > 1 }) res.Add((akt, Gesetzt(status) ? status : "offen"));
        }
        return res;
    }

    /// <summary>Bestätigungstext vor dem Zusammenfassen.</summary>
    public const string ZusammenfassenHinweis = "Nach der Bestätigung werden Abwesenheiten im gewählten Zeitbereich möglichst zusammengefasst, auch jene ohne Entschuldigungsstatus. Abwesenheiten mit verschiedenen Abwesenheitsgründen oder Entschuldigungsstatus werden nicht zusammengefasst.";
}
