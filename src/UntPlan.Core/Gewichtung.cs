using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.Core;

/// <summary>Art eines Eintrags im Gewichtungsdialog.</summary>
public enum ReglerArt
{
    /// <summary>Schieberegler 0 (unwichtig) … 5 (extrem wichtig).</summary>
    Gewicht,
    /// <summary>Zahleneingabe (z. B. „Maximal“ bei „Stunden in letzter Vormittagsstunde“, „Grenzstunde“).</summary>
    Zahl,
    /// <summary>Häkchen (0/1), z. B. „In erster Unterrichtsstunde“ bei Frei- und Randstundenfächern.</summary>
    Haken,
}

/// <summary>Hinweis des Abschnitts „Analyse“ im Gewichtungsdialog.</summary>
public sealed record GewichtungsHinweis(string Text, bool Warnung, string? Schluessel = null);

/// <summary>
/// Gewichtung (Steuerdaten der Optimierung): Gewichtungsziffern von 0 (unwichtig) bis 5 (extrem wichtig) in den Abschnitten
/// Lehrer (1), Lehrer (2), Klassen, Fächer, Hauptfächer, Räume, Stundenverteilung und Zeitwünsche, dazu „Gewichtungs-Verteilung“
/// und „Analyse“. Zwischen 4 und 5 liegt ein sehr großer Unterschied – 4 und 5 sparsam verwenden.
/// </summary>
public static class Gewichtung
{
    /// <param name="Gruppe">Eingabeblock, zu dem der Eintrag gehört (z. B. „Stunden in letzter Vormittagsstunde“), sonst null.</param>
    /// <param name="Max">Höchstwert einer Zahleneingabe.</param>
    public sealed record Regler(string Schluessel, string Karteikarte, string Text, int Standard, ReglerArt Art = ReglerArt.Gewicht, string? Gruppe = null, int Max = 5);

    public const string Lehrer1 = "Lehrer (1)";
    public const string Lehrer2 = "Lehrer (2)";

    private const string LetzteVorm = "Stunden in letzter Vormittagsstunde";
    private const string Freifach = "Freifach";
    private const string Randstundenfach = "Randstundenfach";
    private const string Grenzstunde = "Gewichtung zur Grenzstunde";

    // Neue Parameter ohne frühere Entsprechung stehen in der Voreinstellung auf 0 (unwichtig), damit sich gerechnete Pläne
    // bestehender Dateien nicht ändern; der Anwender stellt sie bei Bedarf höher.
    public static readonly IReadOnlyList<Regler> Alle = new List<Regler>
    {
        // Abschnitt Lehrer (1)
        new("L.Einzelstunden", Lehrer1, "Einzelstunden an einem Halbtag für Lehrer vermeiden", 0),
        new("L.Hohlstunden", Lehrer1, "Optimierung der Lehrerhohlstunden", 3),
        new("L.Doppelhohl", Lehrer1, "Doppelhohlstunden für Lehrer vermeiden", 0),
        new("L.Mittagspause", Lehrer1, "Mittagspause für Lehrer einhalten", 2),
        new("L.Fachfolge", Lehrer1, "Fachfolge Lehrer", 1),
        new("L.Tagesrand", Lehrer1, "Tagesrand-Pausen einhalten", 2),
        // Abschnitt Lehrer (2)
        new("L.StdTag", Lehrer2, "Maximale bzw. minimale Stundenzahl je Tag für Lehrer einhalten", 2),
        new("L.StdFolge", Lehrer2, "Maximale Stundenzahl je Tag in unmittelbarer Folge einhalten", 2),
        new("L.Verweildauer", Lehrer2, "Max. Verweildauer des Lehrers pro Tag", 2),
        new("L.LetzteVormMax", Lehrer2, "Maximal", 0, ReglerArt.Zahl, LetzteVorm, 7),
        new("L.LetzteVorm", Lehrer2, "Gewicht", 0, ReglerArt.Gewicht, LetzteVorm),
        new("L.Nachmittage", Lehrer2, "Max. Anzahl Nachmittage für Lehrer", 2),
        new("L.Unbestimmt", Lehrer2, "Unbestimmte Zeitwünsche (freie Tage / Halbtage)", 3),
        new("L.Tagesbelastung", Lehrer2, "Gleichmäßige Tagesbelastung der Lehrer", 1),
        // Abschnitt Klassen
        new("K.Hohlstunden", "Klassen", "Klassenhohlstunden vermeiden", 4),
        new("K.StdTag", "Klassen", "Maximale bzw. minimale Stundenzahl je Tag für Klassen einhalten", 2),
        new("K.Mittagspause", "Klassen", "Mittagspause für Klassen einhalten", 3),
        new("K.Fachfolge", "Klassen", "Fachfolge Klassen", 1),
        new("K.Verschiedene", "Klassen", "Maximalzahl verschiedene Unterrichte je Tag für Klassen einhalten", 2),
        new("K.Klassenlehrer", "Klassen", "Klassenlehrer mindestens einmal pro Tag", 0),
        new("K.GleichMittag", "Klassen", "Einhaltung der Maximalzahl von Klassen mit gleichzeitiger Mittagspause", 0),
        new("K.Kernzeit", "Klassen", "Kernzeiten (+3) der Klassen einhalten", 4),
        new("K.Nachmittage", "Klassen", "Max. Anzahl Nachmittage für Klassen", 2),
        // Abschnitt Fächer
        new("F.Freifach", "Fächer", "Gewicht", 1, ReglerArt.Gewicht, Freifach),
        new("F.Freifach.Erste", "Fächer", "In erster Unterrichtsstunde", 1, ReglerArt.Haken, Freifach, 1),
        new("F.Freifach.Letzte", "Fächer", "In letzter Unterrichtsstunde", 1, ReglerArt.Haken, Freifach, 1),
        new("F.Freifach.Mittag", "Fächer", "Zwischen Vormittag und Nachmittag", 1, ReglerArt.Haken, Freifach, 1),
        new("F.Randstunde", "Fächer", "Gewicht", 1, ReglerArt.Gewicht, Randstundenfach),
        new("F.Randstunde.Erste", "Fächer", "In erster Unterrichtsstunde", 1, ReglerArt.Haken, Randstundenfach, 1),
        new("F.Randstunde.Letzte", "Fächer", "In letzter Unterrichtsstunde", 1, ReglerArt.Haken, Randstundenfach, 1),
        new("F.Randstunde.Mittag", "Fächer", "Zwischen Vormittag und Nachmittag", 1, ReglerArt.Haken, Randstundenfach, 1),
        new("F.NichtRandG", "Fächer", "Unterricht nicht in Randstunde, wenn allg. Kennz. = G", 1),
        new("F.KeinNachmittag", "Fächer", "Fächer ohne Nachmittagsunterricht (N)", 2),
        // Abschnitt Hauptfächer
        new("H.ProTag", "Hauptfächer", "Maximalzahl Hauptfächer je Tag für Klassen einhalten", 2),
        new("H.Folge", "Hauptfächer", "Maximalzahl aufeinanderfolgender Hauptfächer für Klassen einhalten", 2),
        new("H.Grenzstunde", "Hauptfächer", "Grenzstunde für folgende Gesichtspunkte (0 = letzte Vormittagsstunde)", 0, ReglerArt.Zahl, Grenzstunde, 16),
        new("H.Spaet", "Hauptfächer", "Hauptfächer max. 1 mal nach Grenzstunde", 1, ReglerArt.Gewicht, Grenzstunde),
        new("H.BisGrenz", "Hauptfächer", "Hauptfach mindestens 1 mal bis Grenzstunde (incl.)", 0, ReglerArt.Gewicht, Grenzstunde),
        // Abschnitt Räume
        new("R.Raumoptimierung", "Räume", "Optimierung der Raumbelegung", 3),
        new("R.Dislozierung", "Räume", "Optimierung der dislozierten Räume", 3),
        new("R.Kapazitaet", "Räume", "Berücksichtigung der Raumkapazität", 0),
        // Abschnitt Stundenverteilung
        new("F.ZweimalTag", "Stundenverteilung", "Ein Fach nicht mehrmals am selben Tag", 3),
        new("F.Doppelstunden", "Stundenverteilung", "Doppelstundenfehler vermeiden", 3),
        new("S.Zwei", "Stundenverteilung", "2-Stunden-Fächer nicht an aufeinanderfolgenden Tagen", 0),
        new("S.Drei", "Stundenverteilung", "3-Stunden-Fächer nicht an aufeinanderfolgenden Tagen", 0),
        new("S.Tagesstunden", "Stundenverteilung", "Gute Verteilung der Stunden eines Faches auf verschiedene Tagesstunden", 0),
        new("S.SelbeZeit", "Stundenverteilung", "Selber Unterricht an verschiedenen Tagen zur selben Zeit", 0),
        new("S.Grossbloecke", "Stundenverteilung", "Großblöcke am Rand des Halbtages", 0),
        new("S.Verteilung", "Stundenverteilung", "Unterricht gleichmäßig über die Woche verteilen", 2),
        new("S.Spaet", "Stundenverteilung", "Späte Stunden / Nachmittag nur wenn nötig", 1),
        // Abschnitt Zeitwünsche
        new("Z.Lehrer", "Zeitwünsche", "Zeitwünsche für Lehrer", 3),
        new("Z.Klassen", "Zeitwünsche", "Zeitwünsche für Klassen", 3),
        new("Z.Faecher", "Zeitwünsche", "Zeitwünsche für Lehrfächer", 2),
        new("Z.Raeume", "Zeitwünsche", "Zeitwünsche für Räume", 2),
        new("Z.Unterricht", "Zeitwünsche", "Zeitwünsche für Unterrichtsstunden", 2),
    };

    /// <summary>Nur die Schieberegler (Grundlage für „Gewichtungs-Verteilung“ und „Analyse“).</summary>
    public static IEnumerable<Regler> Gewichte => Alle.Where(r => r.Art == ReglerArt.Gewicht);

    public static readonly Dictionary<string, int> Standard = Alle.ToDictionary(r => r.Schluessel, r => r.Standard);

    /// <summary>Abschnitte in der Reihenfolge des Gewichtungsdialogs.</summary>
    public static readonly string[] Karteikarten = Alle.Select(r => r.Karteikarte).Distinct().ToArray();

    /// <summary>Wert eines Eintrags (gespeicherter Wert oder Voreinstellung).</summary>
    public static int Wert(IReadOnlyDictionary<string, int> g, string schluessel) =>
        g.TryGetValue(schluessel, out var v) ? v : Standard.GetValueOrDefault(schluessel);

    /// <summary>Multiplikator einer Gewichtungsziffer (exponentiell: 4 → 5 ist ein großer Sprung).</summary>
    public static double Faktor(int ziffer) => ziffer switch
    {
        <= 0 => 0,
        1 => 1,
        2 => 3,
        3 => 8,
        4 => 20,
        _ => 60,
    };

    /// <summary>Bedeutung der Reglerpositionen 0–5 wie im Handbuch.</summary>
    public static string ZifferText(int z) => z switch
    {
        <= 0 => "unwichtig",
        1 => "wenig wichtig",
        2 => "berücksichtigenswert",
        3 => "wichtig",
        4 => "sehr wichtig",
        _ => "extrem wichtig",
    };

    /// <summary>Häufigkeit der Gewichte 0–5 über alle Schieberegler (Abschnitt „Gewichtungs-Verteilung“).</summary>
    public static int[] Verteilung(IReadOnlyDictionary<string, int> g)
    {
        var n = new int[6];
        foreach (var r in Gewichte) n[Math.Clamp(Wert(g, r.Schluessel), 0, 5)]++;
        return n;
    }

    /// <summary>Gewichtungsparameter, dessen Regler einen Verstoß der Bewertung steuert; null = nicht gewichtbar (Diagnose: „*“).</summary>
    public static string? SchluesselFuer(VerstossArt a) => a switch
    {
        VerstossArt.EinzelstundeHalbtag => "L.Einzelstunden",
        VerstossArt.HohlstundenLehrer => "L.Hohlstunden",
        VerstossArt.DoppelhohlstundeLehrer => "L.Doppelhohl",
        VerstossArt.MittagspauseLehrer => "L.Mittagspause",
        VerstossArt.Tagesrandpause => "L.Tagesrand",
        VerstossArt.StdTagLehrer or VerstossArt.HalbtagLehrer => "L.StdTag",
        VerstossArt.StundenFolgeLehrer => "L.StdFolge",
        VerstossArt.Verweildauer => "L.Verweildauer",
        VerstossArt.LetzteVormittagsstunde => "L.LetzteVorm",
        VerstossArt.NachmittageLehrer => "L.Nachmittage",
        VerstossArt.UnbestimmterZeitwunsch => "L.Unbestimmt",
        VerstossArt.Tagesbelastung => "L.Tagesbelastung",
        VerstossArt.HohlstundeKlasse => "K.Hohlstunden",
        VerstossArt.StdTagKlasse or VerstossArt.HalbtagKlasse or VerstossArt.Unterrichtstage => "K.StdTag",
        VerstossArt.MittagspauseKlasse => "K.Mittagspause",
        VerstossArt.VerschiedeneUnterrichte => "K.Verschiedene",
        VerstossArt.KlassenlehrerTag => "K.Klassenlehrer",
        VerstossArt.GleichzeitigeMittagspause => "K.GleichMittag",
        VerstossArt.Kernzeitverletzung => "K.Kernzeit",
        VerstossArt.NachmittageKlasse => "K.Nachmittage",
        VerstossArt.Freifach => "F.Freifach",
        VerstossArt.Randstunde => "F.Randstunde",
        VerstossArt.NichtInRandstunde => "F.NichtRandG",
        VerstossArt.KeinNachmittag or VerstossArt.Nachmittagsstunden => "F.KeinNachmittag",
        VerstossArt.HauptfaecherProTag => "H.ProTag",
        VerstossArt.HauptfaecherFolge => "H.Folge",
        VerstossArt.HauptfachSpaet => "H.Spaet",
        VerstossArt.HauptfachBisGrenzstunde => "H.BisGrenz",
        VerstossArt.KeinRaum or VerstossArt.SelberRaum => "R.Raumoptimierung",
        VerstossArt.Dislozierung => "R.Dislozierung",
        VerstossArt.Raumkapazitaet => "R.Kapazitaet",
        VerstossArt.FachMehrmalsProTag => "F.ZweimalTag",
        VerstossArt.DoppelstundenFehler or VerstossArt.BlockFehler or VerstossArt.Klassenzeitraster or VerstossArt.NachmittagEinzelstunde => "F.Doppelstunden",
        VerstossArt.ZweiStundenFolgetage => "S.Zwei",
        VerstossArt.DreiStundenFolgetage => "S.Drei",
        VerstossArt.FachGleicheTagesstunde => "S.Tagesstunden",
        VerstossArt.SelbeZeitVerschiedeneTage => "S.SelbeZeit",
        VerstossArt.GrossblockRand => "S.Grossbloecke",
        VerstossArt.Verteilung or VerstossArt.ImmerSelbeZeit => "S.Verteilung",
        VerstossArt.SpaeteStunde => "S.Spaet",
        VerstossArt.ZeitwunschLehrer or VerstossArt.ErsteUndLetzteStunde => "Z.Lehrer",
        VerstossArt.ZeitwunschKlasse => "Z.Klassen",
        VerstossArt.ZeitwunschFach => "Z.Faecher",
        VerstossArt.ZeitwunschRaum => "Z.Raeume",
        VerstossArt.ZeitwunschUnterricht => "Z.Unterricht",
        _ => null,
    };

    /// <summary>
    /// Abschnitt „Analyse“: Hinweise auf Probleme der aktuellen Verteilung der Gewichtungsparameter – zu viele Regler auf 5, eine mit
    /// steigendem Gewicht zunehmende Vergabehäufigkeit, fast alles unwichtig bzw. extrem wichtig – sowie abhängige Gewichte, die
    /// mangels Eingaben in den Stammdaten bzw. im Unterricht keine Wirkung haben, und häufiger Gebrauch des Kennzeichens (D).
    /// </summary>
    public static List<GewichtungsHinweis> Analyse(Schuldatei? d, IReadOnlyDictionary<string, int> g)
    {
        var h = new List<GewichtungsHinweis>();
        var n = Verteilung(g);
        var gesamt = n.Sum();
        if (gesamt == 0) return h;
        var fuenf = Gewichte.Where(r => Wert(g, r.Schluessel) >= 5).ToList();
        if (fuenf.Count > Math.Max(2, gesamt / 10))
            h.Add(new($"{fuenf.Count} Parameter stehen auf „extrem wichtig“ (5). Der Unterschied zwischen 4 und 5 ist sehr groß – zu viele 5er schränken die Optimierung so stark ein, dass oft nur ein Bruchteil der Stunden verplant werden kann.", true));
        foreach (var r in fuenf)
            h.Add(new($"„{Titel(r)}“ ist extrem wichtig (5) – prüfen Sie, ob „sehr wichtig“ (4) genügt.", false, r.Schluessel));
        if (n[0] + n[1] > gesamt * 0.7)
            h.Add(new("Ein Großteil der Gewichte ist „unwichtig“ bzw. „wenig wichtig“ – die Optimierung kann so kaum zwischen den Kriterien unterscheiden.", true));
        if (n[5] > gesamt * 0.4)
            h.Add(new("Ein Großteil der Gewichte ist „extrem wichtig“ – bitte die Gewichte abstufen.", true));
        if (n[4] + n[5] > n[1] + n[2] && n[4] + n[5] > 0)
            h.Add(new("Die Vergabehäufigkeit nimmt mit steigendem Gewicht zu. Empfohlen ist eine Verteilung, bei der hohe Gewichte seltener vergeben werden als niedrige.", true));
        if (d != null) AbhaengigeGewichte(d, g, h);
        return h;
    }

    private static string Titel(Regler r) => r.Gruppe is { } gr && r.Text is "Gewicht" ? gr : r.Text;

    private static void AbhaengigeGewichte(Schuldatei d, IReadOnlyDictionary<string, int> g, List<GewichtungsHinweis> h)
    {
        var lehrer = d.Lehrer.Where(l => !l.Ignorieren).ToList();
        var klassen = d.Klassen.Where(k => !k.Ignorieren).ToList();
        var faecher = d.Faecher.ToList();
        var unt = d.Unterrichte.Where(u => !u.Ignorieren).ToList();
        var fachfolge = unt.Any(u => !string.IsNullOrWhiteSpace(u.FachfolgeLehrer) || !string.IsNullOrWhiteSpace(u.FachfolgeKlassen)) ||
                        faecher.Any(f => !string.IsNullOrWhiteSpace(f.FachfolgeLehrer) || !string.IsNullOrWhiteSpace(f.FachfolgeKlassen));
        var pruefungen = new (string Key, bool Vorhanden, string Wo)[]
        {
            ("L.StdTag", lehrer.Any(l => !l.StundenProTag.IstLeer), "„Stunden je Tag min-max“ bei den Lehrern"),
            ("L.StdFolge", lehrer.Any(l => l.MaxStundenFolge > 0), "„Max. Stunden in Folge“ bei den Lehrern"),
            ("L.Verweildauer", lehrer.Any(l => l.MaxVerweildauer > 0), "„Max. Verweildauer“ bei den Lehrern"),
            ("L.Tagesrand", lehrer.Any(l => l.TagesrandPause > 0), "„Tagesrand-Pause“ bei den Lehrern"),
            ("L.Nachmittage", lehrer.Any(l => l.MaxNachmittage > 0), "„Max. Nachmittage“ bei den Lehrern"),
            ("L.Fachfolge", fachfolge, "Fachfolge-Kennzeichen bei Fächern bzw. Unterricht"),
            ("K.Fachfolge", fachfolge, "Fachfolge-Kennzeichen bei Fächern bzw. Unterricht"),
            ("K.StdTag", klassen.Any(k => !k.StundenProTag.IstLeer), "„Stunden je Tag min-max“ bei den Klassen"),
            ("K.Verschiedene", klassen.Any(k => k.MaxVerschiedeneProTag > 0), "„Max. verschiedene Unterrichte je Tag“ bei den Klassen"),
            ("K.Klassenlehrer", klassen.Any(k => k.KlassenlehrerIds.Count > 0), "Klassenlehrer bei den Klassen"),
            ("K.GleichMittag", d.Zeitraster.MaxKlassenGleicheMittagspause > 0, "die Maximalzahl gleichzeitiger Mittagspausen im Zeitraster"),
            ("K.Nachmittage", klassen.Any(k => k.MaxNachmittage > 0), "„Max. Nachmittage“ bei den Klassen"),
            ("F.Freifach", faecher.Any(f => f.Freifach) || unt.Any(u => u.Freifach), "Kennzeichen (F) Freifach"),
            ("F.Randstunde", faecher.Any(f => f.Randstunde) || unt.Any(u => u.Randstunde), "Kennzeichen (R) Randstundenfach"),
            ("F.NichtRandG", faecher.Any(f => f.NichtInRandstunden) || unt.Any(u => u.NichtInRandstunde), "Kennzeichen (G) nicht in Randstunden"),
            ("H.ProTag", klassen.Any(k => k.HauptfaecherProTag > 0) && faecher.Any(f => f.Hauptfach), "„Hauptfächer je Tag“ bei den Klassen und Hauptfächer"),
            ("H.Folge", klassen.Any(k => k.HauptfaecherFolge > 0) && faecher.Any(f => f.Hauptfach), "„Hauptfächer in Folge“ bei den Klassen und Hauptfächer"),
            ("H.Spaet", faecher.Any(f => f.Hauptfach), "Hauptfächer bei den Fächern"),
            ("H.BisGrenz", faecher.Any(f => f.Hauptfach), "Hauptfächer bei den Fächern"),
            ("R.Dislozierung", d.Raeume.Any(r => Raum.DislozierungNormal(r.Dislozierung).Length > 0), "Dislozierungs-Kennzeichen bei den Räumen"),
            ("R.Kapazitaet", d.Raeume.Any(r => r.Kapazitaet > 0), "Kapazitäten bei den Räumen"),
        };
        foreach (var (key, vorhanden, wo) in pruefungen)
        {
            if (vorhanden || Wert(g, key) <= 0) continue;
            var r = Alle.First(x => x.Schluessel == key);
            h.Add(new($"„{Titel(r)}“ ist gewichtet ({Wert(g, key)}), es gibt aber keine Eingaben für {wo} – das Gewicht spielt bei der Optimierung keine Rolle.", false, key));
        }
        if (Wert(g, "L.LetzteVorm") > 0 && Wert(g, "L.LetzteVormMax") <= 0)
            h.Add(new("„Stunden in letzter Vormittagsstunde“: Ohne Eingabe bei „Maximal“ ist das Gewicht wirkungslos.", false, "L.LetzteVorm"));
        var mitD = unt.Count(u => u.DoppelstundenEinhalten) + faecher.Count(f => f.DoppelstundenEinhalten);
        if (unt.Count > 0 && mitD > Math.Max(3, unt.Count / 5))
            h.Add(new($"Das Kennzeichen (D) Doppelstunden einhalten ist {mitD}-mal gesetzt. Exzessiver Gebrauch verschlechtert die Optimierungsresultate.", true));
    }
}
