namespace UntPlan.Core;

/// <summary>
/// Gewichtung (Steuerdaten der Optimierung): Gewichtungsziffern von 0 (unwichtig) bis 5 (extrem wichtig).
/// Zwischen 4 und 5 liegt ein sehr großer Unterschied – 4 und 5 sparsam verwenden.
/// </summary>
public static class Gewichtung
{
    public sealed record Regler(string Schluessel, string Karteikarte, string Text, int Standard);

    public static readonly IReadOnlyList<Regler> Alle = new List<Regler>
    {
        // Abschnitte und Texte wie im Handbuch (Gewichtungsparameter)
        new("L.Hohlstunden", "Lehrer (1/2)", "Optimierung der Lehrerhohlstunden", 3),
        new("L.Mittagspause", "Lehrer (1/2)", "Mittagspause für Lehrer einhalten", 2),
        new("L.StdTag", "Lehrer (2/2)", "Maximale bzw. minimale Stundenzahl je Tag für Lehrer einhalten", 2),
        new("L.Nachmittage", "Lehrer (2/2)", "Max. Anzahl Nachmittage für Lehrer", 2),
        new("L.Unbestimmt", "Lehrer (2/2)", "Unbestimmte Zeitwünsche (freie Tage / Halbtage)", 3),
        new("L.Tagesbelastung", "Lehrer (2/2)", "Gleichmäßige Tagesbelastung der Lehrer", 1),
        new("K.Hohlstunden", "Klassen", "Klassenhohlstunden vermeiden", 4),
        new("K.Kernzeit", "Klassen", "Kernzeiten (+3) der Klassen einhalten", 4),
        new("K.StdTag", "Klassen", "Maximale bzw. minimale Stundenzahl je Tag für Klassen einhalten", 2),
        new("K.Mittagspause", "Klassen", "Mittagspause für Klassen einhalten", 3),
        new("K.Nachmittage", "Klassen", "Max. Anzahl Nachmittage für Klassen", 2),
        new("F.Randstunde", "Fächer", "Freifach und Randstundenfach", 1),
        new("F.KeinNachmittag", "Fächer", "Fächer ohne Nachmittagsunterricht (N)", 2),
        new("H.ProTag", "Hauptfächer", "Maximalzahl Hauptfächer je Tag für Klassen einhalten", 2),
        new("H.Folge", "Hauptfächer", "Maximalzahl aufeinanderfolgender Hauptfächer für Klassen einhalten", 2),
        new("H.Spaet", "Hauptfächer", "Hauptfächer max. 1 mal nach Grenzstunde", 1),
        new("R.Raumoptimierung", "Räume", "Optimierung der Raumbelegung", 3),
        new("F.ZweimalTag", "Stundenverteilung", "Ein Fach nicht mehrmals am selben Tag", 3),
        new("F.Doppelstunden", "Stundenverteilung", "Doppelstundenfehler vermeiden", 3),
        new("S.Verteilung", "Stundenverteilung", "Unterricht gleichmäßig über die Woche verteilen", 2),
        new("S.Spaet", "Stundenverteilung", "Späte Stunden / Nachmittag nur wenn nötig", 1),
        new("Z.Lehrer", "Zeitwünsche", "Zeitwünsche für Lehrer", 3),
        new("Z.Klassen", "Zeitwünsche", "Zeitwünsche für Klassen", 3),
        new("Z.Faecher", "Zeitwünsche", "Zeitwünsche für Lehrfächer", 2),
        new("Z.Raeume", "Zeitwünsche", "Zeitwünsche für Räume", 2),
        new("Z.Unterricht", "Zeitwünsche", "Zeitwünsche für Unterrichtsstunden", 2),
    };

    public static readonly Dictionary<string, int> Standard = Alle.ToDictionary(r => r.Schluessel, r => r.Standard);

    public static readonly string[] Karteikarten = Alle.Select(r => r.Karteikarte).Distinct().ToArray();

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

    public static string ZifferText(int z) => z switch
    {
        0 => "unwichtig",
        1 => "wenig wichtig",
        2 => "mäßig wichtig",
        3 => "wichtig",
        4 => "sehr wichtig",
        _ => "extrem wichtig",
    };
}
