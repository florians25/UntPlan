namespace UntPlan.Core.Model;

/// <summary>
/// Felder der Stundenplan-Stunde wie im Vorbild-Editor „Inhalt (Felder) einer Unterrichtsstunde“. Neue Arten werden nur angehängt
/// (Dateien speichern die Namen).
/// </summary>
public enum StundenFeldArt
{
    Fach, Lehrer, Klasse, Raum, UNr, Text, Gruppe, Kurs, Zeit,
    Schuelergruppe, StudZl, UnterrichtsGruppe, Fussnote, Sondertext, ZeilenText, ZeilenText2, Stundentext, Beschreibung, Zeitwunsch, Band,
}

/// <summary>Räumliche Darstellung der Felder (Vorbild „Layout 1“).</summary>
public enum Raeumlich { Erhaben, Versenkt, Flach }

/// <summary>Grundformen der Stundenpläne (Vorbild „Stundenplanformate“).</summary>
public enum Grundform
{
    /// <summary>Format 01 – Einzelstundenplan, Tage waagrecht, Stunden senkrecht.</summary>
    Format01 = 1,
    /// <summary>Format 10 – Stunden waagrecht, Tage senkrecht.</summary>
    Format10 = 10,
    /// <summary>Format 11 – Unterrichtswochen waagrecht, Stunden der Woche senkrecht.</summary>
    Format11 = 11,
    /// <summary>Format 20 – Übersicht: Stunden der Woche senkrecht, Elemente waagrecht.</summary>
    Format20 = 20,
    /// <summary>Format 21 – wie 20, mehrere Wochen nebeneinander.</summary>
    Format21 = 21,
    /// <summary>Format 30 – Übersicht: Stunden der Woche waagrecht, Elemente senkrecht.</summary>
    Format30 = 30,
    /// <summary>Format 40 – Einzelstundenplan mit mehreren Elementen.</summary>
    Format40 = 40,
}

/// <summary>Zeitraum der Formatauswahl (Vorbild „Auswahl (Bereich)“).</summary>
public enum FormatZeitraum { Kalenderwoche, GanzesSchuljahr, EinTag }

/// <summary>Art des Namens in der Stundenplan-Stunde.</summary>
public enum NamensArt { Kurzname, Langname, Druckname }

/// <summary>Gestaltung eines Feldbereichs (Überschrift, besetzte bzw. freie Stunden).</summary>
public sealed class FeldGestaltung
{
    public Raeumlich Raeumlich { get; set; } = Raeumlich.Flach;
    /// <summary>Eben: grau (true) bzw. weiß (false).</summary>
    public bool Grau { get; set; }
}

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
    public bool Kursiv { get; set; }
    public bool Unterstrichen { get; set; }
    /// <summary>Ausrichtung im Feld: 0 = links, 1 = mittig, 2 = rechts.</summary>
    public int Ausrichtung { get; set; } = 1;

    /// <summary>Beschriftung im Editor wie im Vorbild („1Lehrer“).</summary>
    public string Beschriftung => Zeile + Art switch
    {
        StundenFeldArt.UNr => "Unterrichts-Nummer",
        StundenFeldArt.Schuelergruppe => "Schülergruppe",
        StundenFeldArt.StudZl => "Stud-Zl",
        StundenFeldArt.UnterrichtsGruppe => "Unt.Gru",
        StundenFeldArt.Fussnote => "Referenz zu Fußnote",
        StundenFeldArt.ZeilenText => "ZeilenText",
        StundenFeldArt.ZeilenText2 => "ZeilenText-2",
        _ => Art.ToString(),
    };
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
    /// <summary>Einzelstunden: Doppelstunden und Blöcke nicht zu einem Feld zusammenfassen, jede Stunde einzeln (wie im Vorbild).</summary>
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

    // ---- Vorbild-Formatdialog: Allgemein ----
    public Grundform Grundform { get; set; } = Grundform.Format01;

    // ---- Auswahl (Bereich) ----
    /// <summary>Erste angezeigte Stunde (Stunden-Nummer).</summary>
    public int VonStunde { get; set; } = 1;
    /// <summary>Letzte angezeigte Stunde (0 = bis zur letzten Stunde des Zeitrasters).</summary>
    public int BisStunde { get; set; }
    /// <summary>Erster angezeigter Wochentag (0 = Montag).</summary>
    public int VonTag { get; set; }
    /// <summary>Letzter angezeigter Wochentag (-1 = bis zum letzten Schultag).</summary>
    public int BisTag { get; set; } = -1;
    public FormatZeitraum Zeitraum { get; set; } = FormatZeitraum.Kalenderwoche;
    public DateOnly? DatumVon { get; set; }
    public DateOnly? DatumBis { get; set; }
    public bool MinutModus { get; set; }
    public bool GanzenTagZeigen { get; set; }

    // ---- Layout 1 ----
    public FeldGestaltung Ueberschrift { get; set; } = new() { Raeumlich = Raeumlich.Flach, Grau = true };
    public FeldGestaltung BesetzteStunden { get; set; } = new() { Raeumlich = Raeumlich.Flach, Grau = true };
    public FeldGestaltung FreieStunden { get; set; } = new() { Raeumlich = Raeumlich.Flach, Grau = false };
    /// <summary>Linie vor folgenden Stunden (z. B. „3,5“).</summary>
    public string LinieVor { get; set; } = "";
    public string DoppelstrichVor { get; set; } = "";
    public string DickerStrichVor { get; set; } = "";
    public bool TrennstrichVormNachm { get; set; }
    public bool TrennstrichTage { get; set; }

    // ---- Layout 2 ----
    public bool KombinierteKlassen { get; set; }
    public bool AlleKlassenEinFeld { get; set; }
    public bool KopplungenMitPunkt { get; set; } = true;
    public bool FixierungMitStern { get; set; } = true;
    public bool KollisionTrennen { get; set; } = true;
    public bool StundenfelderSortieren { get; set; }
    public bool ElementeVerbinden { get; set; } = true;
    public bool AbsenzenAnzeigen { get; set; } = true;
    public bool KeinAbsenzgrund { get; set; }
    public bool NurTagesabsenzen { get; set; }
    public bool NurVertretungsstunden { get; set; }
    public bool EntfaelleDurchstreichen { get; set; } = true;
    public bool DragDropMehrereUnterrichte { get; set; }
    public bool DragDropTauschketten { get; set; } = true;
    public bool LeereZeilenNichtDrucken { get; set; }
    public bool LeereSpalteNichtDrucken { get; set; }
    public bool SchwarzWeissDruck { get; set; }
    public bool EineUeberschriftJeBlatt { get; set; }
    public bool AutosizeLupe { get; set; } = true;
    public bool HtmlIndexLangnamen { get; set; }
    public bool HauptklassenGetrennt { get; set; }
    public bool Pausenbezeichnung { get; set; } = true;

    // ---- HTML ----
    public bool HtmlMitFarben { get; set; } = true;
    public bool HtmlIndexseite { get; set; } = true;
    public string HtmlTitel { get; set; } = "";

    // ---- Stundenplan-Stunde (Editor) ----
    public bool FelderHorizontalZentrieren { get; set; } = true;
    public bool FelderVertikalZentrieren { get; set; } = true;
    public bool BeginnEndzeit { get; set; }
    public NamensArt NamensArt { get; set; } = NamensArt.Kurzname;
    public bool AliasVerwenden { get; set; } = true;
    public bool LehrerUndVertreter { get; set; }

    /// <summary>Stunden-Nummern aus einer Liste wie „3,5“ bzw. „3-5“.</summary>
    public static HashSet<int> StundenListe(string? text)
    {
        var erg = new HashSet<int>();
        foreach (var teil in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bis = teil.Split('-');
            if (bis.Length == 2 && int.TryParse(bis[0], out var a) && int.TryParse(bis[1], out var b))
                for (var i = Math.Min(a, b); i <= Math.Max(a, b) && i - Math.Min(a, b) < 50; i++) erg.Add(i);
            else if (int.TryParse(teil, out var n)) erg.Add(n);
        }
        return erg;
    }

    /// <summary>Titel des Formatdialogs wie im Vorbild („Format 01 - Einzelstundenplan Tage waagrecht, Stunden senkrecht“).</summary>
    public static string GrundformText(Grundform g) => g switch
    {
        Grundform.Format10 => "Format 10 - Einzelstundenplan Stunden waagrecht, Tage senkrecht",
        Grundform.Format11 => "Format 11 - Unterrichtswochen waagrecht, Stunden der Woche senkrecht",
        Grundform.Format20 => "Format 20 - Stunden der Woche senkrecht, Elemente waagrecht",
        Grundform.Format21 => "Format 21 - Wie Format 20, jedoch mehrere Wochen nebeneinander",
        Grundform.Format30 => "Format 30 - Stunden der Woche waagrecht, Elemente senkrecht",
        Grundform.Format40 => "Format 40 - Einzelstundenplan mit mehreren Elementen",
        _ => "Format 01 - Einzelstundenplan Tage waagrecht, Stunden senkrecht",
    };

    /// <summary>Standardformat eines kleinen Stundenplans (Vertretungsplanung) für eine Planart: wie im Vorbild mit zusammengefassten Doppelstunden, Pausenzeilen und Unterrichtsliste.</summary>
    public static StundenplanFormat KleinesFormat(ElementTyp typ, string name) => new()
    {
        Name = name, Typ = typ, Klein = true, Einzelstunden = false, PausenAnzeigen = true, ZeitenAnzeigen = false, Schriftgroesse = 9,
        DragDropZeitwunschFarben = false, StundenplanlupeAnzeigen = true,
    };
}

/// <summary>Kurzbezeichnungen der Pausen für Stundenpläne (Vorbild: „FA“ Frühaufsicht, „1. P“, „2. P“, „MP“ Mittagspause).</summary>
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
