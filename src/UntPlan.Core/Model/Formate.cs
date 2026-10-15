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
    /// <summary>Einzelstunden: Doppelstunden und Blöcke nicht zu einem Feld zusammenfassen, jede Stunde einzeln (wie in Untis).</summary>
    public bool Einzelstunden { get; set; }
    /// <summary>Pausen als eigene schmale Zeilen mit Bezeichnung über die ganze Breite anzeigen (z. B. „FA“, „1. P“, „MP“).</summary>
    public bool PausenAnzeigen { get; set; }
    /// <summary>Kleiner Stundenplan (Vertretungsplanung): schmale Zellen nur mit Fachkürzel und Präfixen („.“ Kopplung, „*“ fixiert).</summary>
    public bool Klein { get; set; }
    /// <summary>Mindestbreite einer Tagesspalte in Pixeln (0 = automatisch, an die Fensterbreite angepasst).</summary>
    public int ZellenBreite { get; set; }
    /// <summary>Mindesthöhe einer Stunde in Pixeln (0 = automatisch nach Schriftgröße).</summary>
    public int ZellenHoehe { get; set; }
    /// <summary>Stundenplanlupe (Details der gewählten Stunde) unter dem Plan anzeigen.</summary>
    public bool StundenplanlupeAnzeigen { get; set; } = true;

    /// <summary>Standardformat eines kleinen Stundenplans (Vertretungsplanung) für eine Planart: wie Untis mit zusammengefassten Doppelstunden, Pausenzeilen und Unterrichtsliste.</summary>
    public static StundenplanFormat KleinesFormat(ElementTyp typ, string name) => new()
    {
        Name = name, Typ = typ, Klein = true, Einzelstunden = false, PausenAnzeigen = true, ZeitenAnzeigen = false, Schriftgroesse = 9,
        DragDropZeitwunschFarben = false, StundenplanlupeAnzeigen = true,
    };
}

/// <summary>Kurzbezeichnungen der Pausen für Stundenpläne (Untis: „FA“ Frühaufsicht, „1. P“, „2. P“, „MP“ Mittagspause).</summary>
public static class Pausenbezeichnung
{
    /// <summary>Kurzbezeichnung einer Pause: kurze eigene Bezeichnung (bis 5 Zeichen) bleibt, sonst FA, MP bzw. „n. P“.</summary>
    public static string Kurz(Zeitraster z, Pause p)
    {
        var b = p.Bezeichnung.Trim();
        if (b.Length is > 0 and <= 5) return b;
        if (p.NachStunde <= 0) return "FA";
        if (b.Contains("Mittag", StringComparison.OrdinalIgnoreCase)) return "MP";
        var n = z.Pausen.Where(x => x.NachStunde > 0 && !x.Bezeichnung.Contains("Mittag", StringComparison.OrdinalIgnoreCase) && !(x.Bezeichnung.Trim().Length is > 0 and <= 5))
            .OrderBy(x => x.NachStunde).ToList().IndexOf(p) + 1;
        return n > 0 ? $"{n}. P" : "P";
    }
}
