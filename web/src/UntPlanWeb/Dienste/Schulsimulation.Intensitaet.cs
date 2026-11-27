namespace UntPlanWeb.Dienste;

/// <summary>
/// Intensität der Schulsimulation (Administration → Schulbetrieb simulieren → Intensität). Faktoren in Prozent (100 = bisheriges Verhalten,
/// 0 = nichts dieser Art, 200 = etwa doppelt so viel; 0 bis 300) und Anteile in Prozent (0 bis 100). Wirkt auf neue Ereignisse und auf das
/// Nachpflegen; das Nachbessern ändert vorhandene Daten wegen der Intensität nicht.
/// </summary>
public sealed record Simulationsintensitaet
{
    /// <summary>Krankheiten und Arzttermine der Schüler*innen (Faktor auf die Fehlquoten aller Bildungsgänge).</summary>
    public int Fehlquote { get; init; } = 100;
    /// <summary>Verspätungen der Schüler*innen.</summary>
    public int Verspaetungen { get; init; } = 100;
    /// <summary>Krankheiten, die Eltern, Volljährige bzw. Betriebe vorab melden (Faktor auf 45 %).</summary>
    public int GemeldeteKrankheiten { get; init; } = 100;
    /// <summary>Entschuldigungen durch die Klassenleitung (Faktor auf den Anteil entschuldigter Fehlzeiten).</summary>
    public int Entschuldigungen { get; init; } = 100;
    /// <summary>Verzug, bis die Klassenleitung entschuldigt (Faktor auf ein bis sieben Schultage).</summary>
    public int Entschuldigungsverzug { get; init; } = 100;
    /// <summary>Krankmeldungen der Lehrkräfte (Faktor auf die Zahl der Ausfälle).</summary>
    public int Lehrerausfaelle { get; init; } = 100;
    /// <summary>Längere Ausfälle der Lehrkräfte (Unfall, Operation, Reha; Faktor auf ihren Anteil).</summary>
    public int LangeAusfaelle { get; init; } = 100;
    /// <summary>Abwesenheitskontrolle zu Stundenbeginn (Anteil der Stunden).</summary>
    public int KontrolleZuBeginn { get; init; } = 80;
    /// <summary>Lehrstoff zu Stundenbeginn (Anteil der Stunden).</summary>
    public int LehrstoffZuBeginn { get; init; } = 70;
    /// <summary>Lehrstoff im Voraus (Anteil der Stunden).</summary>
    public int LehrstoffVorab { get; init; } = 5;
    /// <summary>Vergessene Einträge (Faktor auf 3 % fehlende Kontrollen bzw. 1 % fehlenden Lehrstoff; beim Nachpflegen immer vollständig).</summary>
    public int FehlendeEintraege { get; init; } = 100;
    /// <summary>Hausaufgaben.</summary>
    public int Hausaufgaben { get; init; } = 100;
    /// <summary>Klassenbucheinträge (Bemerkungen).</summary>
    public int Klassenbucheintraege { get; init; } = 100;
    /// <summary>Raumänderungen der Lehrkräfte.</summary>
    public int Raumaenderungen { get; init; } = 100;
    /// <summary>Angekündigte Tests (Faktor auf 30 % der Fächer je Halbjahr).</summary>
    public int Tests { get; init; } = 100;
    /// <summary>Buchungen und Einladungen am Sprechtag.</summary>
    public int Sprechtagbuchungen { get; init; } = 100;
    /// <summary>Ausführlichkeit des Lehrstoffs (mehr ausführliche, weniger Stichwort-Einträge).</summary>
    public int Ausfuehrlichkeit { get; init; } = 100;
    /// <summary>Grippewellen in einzelnen Klassen im Winter.</summary>
    public bool Grippewellen { get; init; } = true;

    /// <summary>Wert als Faktor (100 % = 1).</summary>
    public static double F(int prozent) => Math.Max(0, prozent) / 100.0;

    /// <summary>Beschreibung der Regler für die Oberfläche: Schlüssel, Bezeichnung, Gruppe, Standard, Grenzen, Einheit (Faktor bzw. Anteil).</summary>
    public static readonly (string Schluessel, string Name, string Gruppe, int Standard, int Max, string Art)[] Regler =
    {
        ("fehlquote", "Fehlquote der Schüler*innen", "Schüler*innen", 100, 300, "faktor"),
        ("verspaetungen", "Verspätungen", "Schüler*innen", 100, 300, "faktor"),
        ("gemeldeteKrankheiten", "Vorab gemeldete Krankheiten", "Schüler*innen", 100, 220, "faktor"),
        ("entschuldigungen", "Entschuldigungen", "Schüler*innen", 100, 300, "faktor"),
        ("entschuldigungsverzug", "Verzug der Entschuldigungen", "Schüler*innen", 100, 300, "faktor"),
        ("lehrerausfaelle", "Krankmeldungen der Lehrkräfte", "Lehrkräfte", 100, 300, "faktor"),
        ("langeAusfaelle", "Anteil längerer Ausfälle", "Lehrkräfte", 100, 300, "faktor"),
        ("kontrolleZuBeginn", "Abwesenheitskontrolle zu Stundenbeginn", "Klassenbuch", 80, 100, "anteil"),
        ("lehrstoffZuBeginn", "Lehrstoff zu Stundenbeginn", "Klassenbuch", 70, 100, "anteil"),
        ("lehrstoffVorab", "Lehrstoff im Voraus", "Klassenbuch", 5, 100, "anteil"),
        ("fehlendeEintraege", "Vergessene Einträge", "Klassenbuch", 100, 300, "faktor"),
        ("hausaufgaben", "Hausaufgaben", "Klassenbuch", 100, 300, "faktor"),
        ("klassenbucheintraege", "Klassenbucheinträge", "Klassenbuch", 100, 300, "faktor"),
        ("ausfuehrlichkeit", "Ausführlichkeit des Lehrstoffs", "Klassenbuch", 100, 300, "faktor"),
        ("raumaenderungen", "Raumänderungen", "Weitere", 100, 300, "faktor"),
        ("tests", "Angekündigte Tests", "Weitere", 100, 300, "faktor"),
        ("sprechtagbuchungen", "Buchungen am Sprechtag", "Weitere", 100, 300, "faktor"),
    };

    /// <summary>Werte auf ihre Grenzen begrenzen.</summary>
    public Simulationsintensitaet Begrenzt()
    {
        static int B(int v, int max) => Math.Clamp(v, 0, max);
        return this with
        {
            Fehlquote = B(Fehlquote, 300), Verspaetungen = B(Verspaetungen, 300), GemeldeteKrankheiten = B(GemeldeteKrankheiten, 220),
            Entschuldigungen = B(Entschuldigungen, 300), Entschuldigungsverzug = B(Entschuldigungsverzug, 300), Lehrerausfaelle = B(Lehrerausfaelle, 300),
            LangeAusfaelle = B(LangeAusfaelle, 300), KontrolleZuBeginn = B(KontrolleZuBeginn, 100), LehrstoffZuBeginn = B(LehrstoffZuBeginn, 100),
            LehrstoffVorab = B(LehrstoffVorab, 100 - B(LehrstoffZuBeginn, 100)), FehlendeEintraege = B(FehlendeEintraege, 300), Hausaufgaben = B(Hausaufgaben, 300),
            Klassenbucheintraege = B(Klassenbucheintraege, 300), Raumaenderungen = B(Raumaenderungen, 300), Tests = B(Tests, 300),
            Sprechtagbuchungen = B(Sprechtagbuchungen, 300), Ausfuehrlichkeit = B(Ausfuehrlichkeit, 300),
        };
    }

    /// <summary>Schlüssel für Zwischenspeicher (Ergebnisse hängen von der Intensität ab).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Kennung => System.Text.Json.JsonSerializer.Serialize(this);
}
