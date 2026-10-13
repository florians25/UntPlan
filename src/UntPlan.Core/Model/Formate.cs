namespace UntPlan.Core.Model;

public enum StundenFeldArt { Fach, Lehrer, Klasse, Raum, UNr, Text, Gruppe, Kurs, Zeit }

/// <summary>Feld im Grafik-Editor der Stundenplan-Stunde (Position relativ zur Zelle, 0..1).</summary>
public sealed class StundenFeld
{
    public StundenFeldArt Art { get; set; }
    /// <summary>Bei Kopplungen: Nummer der Kopplungszeile (1 = erste).</summary>
    public int Zeile { get; set; } = 1;
    public float X { get; set; }
    public float Y { get; set; }
    public float B { get; set; } = 0.5f;
    public float H { get; set; } = 0.5f;
    public int SchriftProzent { get; set; } = 100;
    public bool Fett { get; set; }
}

public enum FarbeNach { Fach, Lehrer, Klasse, Raum, Keine }

/// <summary>Stundenplan-Format (z. B. "Klassenplan", "Klassenplan groß").</summary>
public sealed class StundenplanFormat
{
    public string Name { get; set; } = "";
    public ElementTyp Typ { get; set; }
    public bool Gross { get; set; }
    /// <summary>"Stpl-Stunde: Standardformat" – alle beteiligten Elemente automatisch anzeigen.</summary>
    public bool Standardformat { get; set; } = true;
    public bool Langname { get; set; }
    public FarbeNach Farbe { get; set; } = FarbeNach.Fach;
    /// <summary>"DragDrop: Farben laut Zeitwünsche".</summary>
    public bool DragDropZeitwunschFarben { get; set; } = true;
    public int Schriftgroesse { get; set; } = 9;
    public bool ZeitenAnzeigen { get; set; } = true;
    public bool MitVertretungen { get; set; } = true;
    public List<StundenFeld> Felder { get; set; } = new();
}
