using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

public enum FeldArt { Text, Ganzzahl, Dezimal, Bool, Bereich, Element, ElementListe, Farbe, Auswahl, Datum, NurLesen }

/// <summary>Beschreibung eines Eingabefeldes der Stammdaten (Raster- und Formularansicht).</summary>
public sealed class Feld
{
    public string Schluessel { get; init; } = "";
    public string Titel { get; init; } = "";
    public string Karteikarte { get; init; } = "Allgemein";
    public FeldArt Art { get; init; }
    public ElementTyp RefTyp { get; init; }
    public string[] Auswahl { get; init; } = Array.Empty<string>();
    /// <summary>Zahlenfeld erlaubt negative Werte (z. B. Wertkorrektur); sonst sind nur Werte ab 0 zulässig.</summary>
    public bool Negativ { get; init; }
    public Func<Element, object?> Get { get; init; } = _ => null;
    public Action<Element, object?>? Set { get; init; }
    public bool Standard { get; init; }
    public int Breite { get; init; } = 70;
    public string Hinweis { get; init; } = "";
    /// <summary>Beschriftung in der Formularansicht und im Felderdialog (leer = Spaltenüberschrift), z. B. „Stammraum (Kurzname)“.</summary>
    public string Beschriftung { get; init; } = "";

    /// <summary>Text in Formularansicht und Felderdialog.</summary>
    public string Formulartext => Beschriftung.Length > 0 ? Beschriftung : Titel;

    public bool Editierbar => Set != null && Art != FeldArt.NurLesen;

    /// <summary>Kann wahlweise mit Kurz- oder Langname angezeigt werden („Anzeige von“ im Dialog „Felder der Ansicht“).</summary>
    public bool HatLangname => Art is FeldArt.Element or FeldArt.ElementListe;

    public string Anzeige(Element e, DatenIndex ix) => Anzeige(e, ix, false);

    /// <summary>Anzeigetext; <paramref name="langname"/>: Elementverweise mit dem Langnamen statt dem Kurznamen.</summary>
    public string Anzeige(Element e, DatenIndex ix, bool langname)
    {
        var v = Get(e);
        string L(int id) => ix.Element(RefTyp, id) is { } el && el.Langname.Length > 0 ? el.Langname : ix.K(RefTyp, id);
        return v switch
        {
            null => "",
            bool b => b ? "X" : "",
            Bereich r => r.ToString(),
            decimal m => m == 0 ? "" : Format.Zahl(m),
            int i when Art == FeldArt.Element && langname => L(i),
            IEnumerable<int> l when langname => string.Join(",", l.Select(L)),
            int i when Art == FeldArt.Element => ix.K(RefTyp, i),
            int i when Art == FeldArt.Auswahl => Auswahl.ElementAtOrDefault(i) ?? i.ToString(),
            int i when Art == FeldArt.Farbe => i == 0 ? "" : $"#{i & 0xFFFFFF:X6}",
            int i => i == 0 ? "" : i.ToString(),
            IEnumerable<int> l => string.Join(",", l.Select(x => ix.K(RefTyp, x))),
            DateOnly dt => Format.Datum(dt),
            _ => v.ToString() ?? "",
        };
    }

    /// <summary>Text der Eingabe in den Feldwert umwandeln (wirft FormatException bei ungültiger Eingabe).</summary>
    public object? Parse(string text, Schuldatei d)
    {
        text = (text ?? "").Trim();
        switch (Art)
        {
            case FeldArt.Text: return text;
            case FeldArt.Ganzzahl: return text.Length == 0 ? 0 : Ganzzahl(text);
            case FeldArt.Dezimal: return text.Length == 0 ? 0m : Dezimalzahl(text);
            case FeldArt.Bool: return text.Equals("X", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
            case FeldArt.Bereich: return Bereich.Parse(text);
            case FeldArt.Datum:
                if (text.Length == 0) return null;
                return DateOnly.TryParse(text, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var datum) ? datum
                    : throw new FormatException($"{Titel}: „{text}“ ist kein gültiges Datum (TT.MM.JJJJ).");
            case FeldArt.Auswahl:
                if (text.Length == 0) return 0;
                var idx = Array.FindIndex(Auswahl, a => a.Equals(text, StringComparison.OrdinalIgnoreCase) || a.StartsWith(text + " ", StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
                return int.TryParse(text.Split(' ')[0], out var nr) && nr >= 0 && nr < Math.Max(1, Auswahl.Length) ? nr
                    : throw new FormatException($"{Titel}: „{text}“ ist kein zulässiger Wert. Erlaubt: {string.Join(", ", Auswahl)}.");
            case FeldArt.Element:
                if (text.Length == 0) return null;
                return Suchen(d, text).Id;
            case FeldArt.ElementListe:
                return text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => Suchen(d, t).Id).Distinct().ToList();
            case FeldArt.Farbe:
                if (text.Length == 0) return 0;
                return uint.TryParse(text.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) && rgb <= 0xFFFFFF
                    ? unchecked((int)(0xFF000000 | rgb))
                    : throw new FormatException($"{Titel}: „{text}“ ist keine Farbe (Beispiel: #FFCC00).");
            default: return null;
        }
    }

    /// <summary>Größter zulässiger Betrag einer Zahleneingabe (Stunden, Kapazitäten, Faktoren): größere Werte sind Tippfehler.</summary>
    public const int GroessteZahl = 99999;

    private int Ganzzahl(string text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("de-DE"), out var n))
            throw new FormatException($"{Titel}: „{text}“ ist keine ganze Zahl.");
        if (Math.Abs(n) > GroessteZahl) throw new FormatException($"{Titel}: {text} ist zu groß (höchstens {GroessteZahl}).");
        if (n < 0 && !Negativ) throw new FormatException($"{Titel}: bitte keine negative Zahl eingeben.");
        return (int)n;
    }

    private decimal Dezimalzahl(string text)
    {
        if (!decimal.TryParse(text.Replace('.', ','), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out var n))
            throw new FormatException($"{Titel}: „{text}“ ist keine Zahl.");
        if (Math.Abs(n) > GroessteZahl) throw new FormatException($"{Titel}: {text} ist zu groß (höchstens {GroessteZahl}).");
        if (n < 0 && !Negativ) throw new FormatException($"{Titel}: bitte keine negative Zahl eingeben.");
        return n;
    }

    private Element Suchen(Schuldatei d, string text) =>
        d.AlleElemente(RefTyp).FirstOrDefault(x => x.Kurzname.Equals(text, StringComparison.OrdinalIgnoreCase))
        ?? throw new FormatException($"{Ui.TypName(RefTyp)} \"{text}\" ist nicht vorhanden.");
}

public static class Felder
{
    private static Feld T(string k, string titel, Func<Element, object?> get, Action<Element, object?>? set, string karte = "Allgemeines", bool std = false, int breite = 70, string formular = "") =>
        new() { Schluessel = k, Titel = titel, Art = FeldArt.Text, Get = get, Set = set, Karteikarte = karte, Standard = std, Breite = breite, Beschriftung = formular };

    private static Feld F(string k, string titel, FeldArt art, Func<Element, object?> get, Action<Element, object?>? set, string karte = "Allgemeines", bool std = false, int breite = 60, ElementTyp refTyp = ElementTyp.Raum, string[]? auswahl = null, string formular = "", bool negativ = false) =>
        new() { Schluessel = k, Titel = titel, Art = art, Get = get, Set = set, Karteikarte = karte, Standard = std, Breite = breite, RefTyp = refTyp, Auswahl = auswahl ?? Array.Empty<string>(), Beschriftung = formular, Negativ = negativ };

    /// <summary>
    /// Eingabefelder aller Stammdaten wie im Handbuch (Karteikarte „Allgemeines“): Name, Langname, Text, Beschreibung, Statistik-Kennzeichen,
    /// Markiert (m), Fix (X), Ignore (i), N.Druck (N) und (Z) – Zeitwünsche vorhanden (nicht editierbar).
    /// </summary>
    private static IEnumerable<Feld> Gemeinsam(string langname = "Langname") => new[]
    {
        T("Kurzname", "Name", e => e.Kurzname, null, std: true, breite: 60),
        T("Langname", langname, e => e.Langname, (e, v) => e.Langname = (string)v!, std: true, breite: 140),
        T("Text", "Text", e => e.Text, (e, v) => e.Text = (string)v!, std: true, breite: 60),
        T("Beschreibung", "Beschr.", e => e.Beschreibung, (e, v) => e.Beschreibung = (string)v!, formular: "Beschreibung"),
        T("Statistik", "Statistik", e => e.Statistik, (e, v) => e.Statistik = (string)v!, formular: "Statistik-Kennzeichen"),
        F("Markiert", "Markiert (m)", FeldArt.Bool, e => e.Markiert, (e, v) => e.Markiert = (bool)v!, breite: 70),
        F("Fix", "Fix (X)", FeldArt.Bool, e => e.Fix, (e, v) => e.Fix = (bool)v!, std: true, breite: 45),
        F("Ignorieren", "Ignore (i)", FeldArt.Bool, e => e.Ignorieren, (e, v) => e.Ignorieren = (bool)v!, std: true, breite: 55),
        F("NichtDrucken", "N.Druck. (N)", FeldArt.Bool, e => e.NichtDrucken, (e, v) => e.NichtDrucken = (bool)v!, breite: 70),
        F("Zeitwuensche", "(Z)", FeldArt.Bool, e => !e.Zeitwuensche.Leer || e.UnbestimmteZeitwuensche.Count > 0, null, breite: 30, formular: "Zeitwünsche (Z)"),
        T("Alias", "Alias-(Zweit)Name", e => e.Alias, (e, v) => e.Alias = (string)v!, breite: 90),
        F("Abteilung", "Abt.", FeldArt.Element, e => e.AbteilungId, (e, v) => e.AbteilungId = (int?)v, refTyp: ElementTyp.Abteilung, formular: "Abteilung"),
        T("ExtName", "Externer Name", e => e.ExtName, (e, v) => e.ExtName = (string)v!, breite: 90),
        F("Hintergrund", "Farbe", FeldArt.Farbe, e => e.Hintergrund, (e, v) => e.Hintergrund = (int)v!, std: false, breite: 50),
    };

    public static List<Feld> Fuer(ElementTyp typ)
    {
        var l = new List<Feld>();
        switch (typ)
        {
            case ElementTyp.Raum:
                l.AddRange(Gemeinsam());
                l.Add(F("Ausweichraum", "Ausweichraum", FeldArt.Element, e => ((Raum)e).AusweichraumId, (e, v) => ((Raum)e).AusweichraumId = (int?)v, "Raum", true, 80, ElementTyp.Raum));
                l.Add(F("Raumgewicht", "Raumgew.", FeldArt.Auswahl, e => ((Raum)e).Raumgewicht, (e, v) => ((Raum)e).Raumgewicht = (int)v!, "Raum", true, 70,
                    auswahl: Enumerable.Range(0, 5).Select(i => $"{i} {Raum.GewichtText(i)}").ToArray()));
                l.Add(T("Disloz", "Disloz.", e => ((Raum)e).Dislozierung, (e, v) => ((Raum)e).Dislozierung = DislozierungPruefen((string)v!), "Raum", breite: 50, formular: "Dislozierung"));
                l.Add(F("Kapazitaet", "Kapaz.", FeldArt.Ganzzahl, e => ((Raum)e).Kapazitaet, (e, v) => ((Raum)e).Kapazitaet = (int)v!, "Raum", true, 50, formular: "Kapazität"));
                l.Add(F("Gang", "Gänge", FeldArt.Element, e => ((Raum)e).GangId, (e, v) => ((Raum)e).GangId = (int?)v, "Raum", true, 55, ElementTyp.Gang));
                l.Add(T("Stockwerk", "Stockwerk", e => ((Raum)e).Stockwerk, (e, v) => ((Raum)e).Stockwerk = (string)v!, "Raum"));
                l.Add(T("Raumart", "Raumart", e => ((Raum)e).Raumart, (e, v) => ((Raum)e).Raumart = (string)v!, "Raum"));
                l.Add(F("Buchbar", "buchbar (Web)", FeldArt.Bool, e => ((Raum)e).Buchbar, (e, v) => ((Raum)e).Buchbar = (bool)v!, "Raum", true, 70));
                break;
            case ElementTyp.Raumgruppe:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Raeume", "Raum", FeldArt.ElementListe, e => ((Raumgruppe)e).RaumIds, (e, v) => ((Raumgruppe)e).RaumIds = (List<int>)v!, "Allgemeines", true, 200, ElementTyp.Raum));
                break;
            case ElementTyp.Klasse:
                l.AddRange(Gemeinsam());
                l.Add(F("SchuelerM", "Männl.", FeldArt.Ganzzahl, e => ((Klasse)e).SchuelerMaennlich, (e, v) => ((Klasse)e).SchuelerMaennlich = (int)v!, "Klasse", formular: "Studenten Männl."));
                l.Add(F("SchuelerW", "Weibl.", FeldArt.Ganzzahl, e => ((Klasse)e).SchuelerWeiblich, (e, v) => ((Klasse)e).SchuelerWeiblich = (int)v!, "Klasse", formular: "Studenten Weibl."));
                l.Add(F("VonDatum", "Von", FeldArt.Datum, e => ((Klasse)e).VonDatum, (e, v) => ((Klasse)e).VonDatum = (DateOnly?)v, "Klasse", formular: "DatumsBereich Von"));
                l.Add(F("BisDatum", "Bis", FeldArt.Datum, e => ((Klasse)e).BisDatum, (e, v) => ((Klasse)e).BisDatum = (DateOnly?)v, "Klasse", formular: "DatumsBereich Bis"));
                l.Add(F("Klassenlehrer", "Klassenlehrer", FeldArt.ElementListe, e => ((Klasse)e).KlassenlehrerIds, (e, v) => ((Klasse)e).KlassenlehrerIds = (List<int>)v!, "Klasse", true, 90, ElementTyp.Lehrer));
                l.Add(T("Jahrgang", "Jahrgangsstufe", e => ((Klasse)e).Jahrgang, (e, v) => ((Klasse)e).Jahrgang = (string)v!, "Klasse"));
                l.Add(T("Stufe", "Stufe", e => ((Klasse)e).Stufe, (e, v) => ((Klasse)e).Stufe = (string)v!, "Klasse"));
                l.Add(T("Stundentafel", "Stundentafel", e => ((Klasse)e).Stundentafel, (e, v) => ((Klasse)e).Stundentafel = (string)v!, "Klasse"));
                l.Add(T("Vorjahresname", "Vorjahresname", e => ((Klasse)e).Vorjahresname, (e, v) => ((Klasse)e).Vorjahresname = (string)v!, "Klasse", breite: 80));
                l.Add(F("Raum", "Raum", FeldArt.Element, e => ((Klasse)e).StammraumId, (e, v) => ((Klasse)e).StammraumId = (int?)v, "Stundenplan", true, 50, ElementTyp.Raum, formular: "Stammraum (Kurzname)"));
                l.Add(T("Klassengruppe", "Klassen-Gruppe", e => ((Klasse)e).Klassengruppe, (e, v) => ((Klasse)e).Klassengruppe = (string)v!, "Stundenplan"));
                l.Add(F("Mittagspause", "Mitt-pause", FeldArt.Bereich, e => ((Klasse)e).Mittagspause, (e, v) => ((Klasse)e).Mittagspause = (Bereich)v!, "Stundenplan", true, 65, formular: "Mittagspause min-max"));
                l.Add(F("StundenProTag", "Std./Tag", FeldArt.Bereich, e => ((Klasse)e).StundenProTag, (e, v) => ((Klasse)e).StundenProTag = (Bereich)v!, "Stundenplan", true, 60, formular: "Stunden je Tag min-max"));
                l.Add(F("HauptfaecherProTag", "Hauptf./Tag", FeldArt.Ganzzahl, e => ((Klasse)e).HauptfaecherProTag, (e, v) => ((Klasse)e).HauptfaecherProTag = (int)v!, "Stundenplan", true, 70, formular: "max.Hauptfächer/Tag"));
                l.Add(F("HauptfaecherFolge", "Hauptf.Folge", FeldArt.Ganzzahl, e => ((Klasse)e).HauptfaecherFolge, (e, v) => ((Klasse)e).HauptfaecherFolge = (int)v!, "Stundenplan", true, 75, formular: "max. Hauptfach-Folge"));
                l.Add(F("MaxNachmittage", "max. Nachm.", FeldArt.Ganzzahl, e => ((Klasse)e).MaxNachmittage, (e, v) => ((Klasse)e).MaxNachmittage = (int)v!, "Stundenplan", false, 70, formular: "max. Nachmittage"));
                l.Add(F("MaxVerschieden", "max.versch.Unt./Tag", FeldArt.Ganzzahl, e => ((Klasse)e).MaxVerschiedeneProTag, (e, v) => ((Klasse)e).MaxVerschiedeneProTag = (int)v!, "Stundenplan", false, 80, formular: "max. verschiedene Unt./Tag"));
                l.Add(F("Hauptklasse", "Hauptklasse", FeldArt.Element, e => ((Klasse)e).HauptklasseId, (e, v) => ((Klasse)e).HauptklasseId = (int?)v, "Stundenplan", false, 70, ElementTyp.Klasse));
                l.Add(F("KlHohlZul", "(P) Hohlstunden zulässig", FeldArt.Bool, e => ((Klasse)e).HohlstundenZulaessig, (e, v) => ((Klasse)e).HohlstundenZulaessig = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("KlTageNichtFolge", "(F) Unterr.Tage nicht in Folge", FeldArt.Bool, e => ((Klasse)e).TageNichtInFolge, (e, v) => ((Klasse)e).TageNichtInFolge = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("KlZweiTage", "(2) 2 Tage hintereinander", FeldArt.Bool, e => ((Klasse)e).ZweiTageHintereinander, (e, v) => ((Klasse)e).ZweiTageHintereinander = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("KlHalbtag", "(H) 1 Halbtag/Tag belegen", FeldArt.Bool, e => ((Klasse)e).EinHalbtag, (e, v) => ((Klasse)e).EinHalbtag = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("KlBelegung", "(Y) Belegung beibehalten", FeldArt.Bool, e => ((Klasse)e).BelegungBeibehalten, (e, v) => ((Klasse)e).BelegungBeibehalten = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("KlKennzeichen", "Kennzeichen", FeldArt.NurLesen, e => ((Klasse)e).Kennzeichen, null, "Kennzeichen", false, 70));
                break;
            case ElementTyp.Lehrer:
                l.AddRange(Gemeinsam("Nachname"));
                l.Add(T("Titel", "Titel", e => ((Lehrer)e).Titel, (e, v) => ((Lehrer)e).Titel = (string)v!, "Lehrer"));
                l.Add(T("Vorname", "Vorname", e => ((Lehrer)e).Vorname, (e, v) => ((Lehrer)e).Vorname = (string)v!, "Lehrer", breite: 90));
                l.Add(T("Personalnummer", "Pers.-Nr.", e => ((Lehrer)e).Personalnummer, (e, v) => ((Lehrer)e).Personalnummer = (string)v!, "Lehrer", formular: "Personal-Nummer"));
                l.Add(T("Status", "Status", e => ((Lehrer)e).Status, (e, v) => ((Lehrer)e).Status = (string)v!, "Lehrer"));
                l.Add(F("StatusNeuAb", "Datum ab Status neu", FeldArt.Datum, e => ((Lehrer)e).StatusNeuAb, (e, v) => ((Lehrer)e).StatusNeuAb = (DateOnly?)v, "Lehrer", false, 80));
                l.Add(T("StatusNeu", "Neuer Status", e => ((Lehrer)e).StatusNeu, (e, v) => ((Lehrer)e).StatusNeu = (string)v!, "Lehrer"));
                l.Add(T("Email", "E-Mail", e => ((Lehrer)e).Email, (e, v) => ((Lehrer)e).Email = (string)v!, "Lehrer", false, 150, formular: "e-mail Adresse"));
                l.Add(T("Telefon", "Telefon", e => ((Lehrer)e).Telefon, (e, v) => ((Lehrer)e).Telefon = (string)v!, "Lehrer", formular: "Telefonnummer"));
                l.Add(F("Geburtsdatum", "Geburtsdatum", FeldArt.Datum, e => ((Lehrer)e).Geburtsdatum, (e, v) => ((Lehrer)e).Geburtsdatum = (DateOnly?)v, "Lehrer", false, 80));
                l.Add(F("Eintritt", "Eintrittsdatum", FeldArt.Datum, e => ((Lehrer)e).Eintritt, (e, v) => ((Lehrer)e).Eintritt = (DateOnly?)v, "Lehrer", false, 80));
                l.Add(F("Austritt", "Austrittsdatum", FeldArt.Datum, e => ((Lehrer)e).Austritt, (e, v) => ((Lehrer)e).Austritt = (DateOnly?)v, "Lehrer", false, 80));
                l.Add(T("Geschlecht", "Geschlecht", e => ((Lehrer)e).Geschlecht, (e, v) => ((Lehrer)e).Geschlecht = (string)v!, "Lehrer"));
                l.Add(F("Stundensatz", "Stundensatz", FeldArt.Dezimal, e => ((Lehrer)e).Stundensatz, (e, v) => ((Lehrer)e).Stundensatz = (decimal)v!, "Lehrer", false, 70));
                l.Add(T("Zusatztext1", "Zusatztext 1", e => ((Lehrer)e).Zusatztext1, (e, v) => ((Lehrer)e).Zusatztext1 = (string)v!, "Lehrer", breite: 90));
                l.Add(T("Zusatztext2", "Zusatztext 2", e => ((Lehrer)e).Zusatztext2, (e, v) => ((Lehrer)e).Zusatztext2 = (string)v!, "Lehrer", breite: 90));
                l.Add(F("Raum", "Raum", FeldArt.Element, e => ((Lehrer)e).StammraumId, (e, v) => ((Lehrer)e).StammraumId = (int?)v, "Stundenplan", true, 50, ElementTyp.Raum, formular: "Stammraum (Kurzname)"));
                l.Add(F("Mittagspause", "Mitt-pause", FeldArt.Bereich, e => ((Lehrer)e).Mittagspause, (e, v) => ((Lehrer)e).Mittagspause = (Bereich)v!, "Stundenplan", true, 65, formular: "Mittagspause min-max"));
                l.Add(F("StundenProTag", "Std./Tag", FeldArt.Bereich, e => ((Lehrer)e).StundenProTag, (e, v) => ((Lehrer)e).StundenProTag = (Bereich)v!, "Stundenplan", true, 60, formular: "Stunden je Tag min-max"));
                l.Add(F("Hohlstunden", "HohlStd. soll", FeldArt.Bereich, e => ((Lehrer)e).Hohlstunden, (e, v) => ((Lehrer)e).Hohlstunden = (Bereich)v!, "Stundenplan", true, 75, formular: "Hohlstunden min-max"));
                l.Add(F("MaxNachmittage", "max. Nachm.", FeldArt.Ganzzahl, e => ((Lehrer)e).MaxNachmittage, (e, v) => ((Lehrer)e).MaxNachmittage = (int)v!, "Stundenplan", false, 70, formular: "max. Nachmittage"));
                l.Add(F("MaxFolge", "Max.Std.Folge", FeldArt.Ganzzahl, e => ((Lehrer)e).MaxStundenFolge, (e, v) => ((Lehrer)e).MaxStundenFolge = Math.Max(0, (int)v!), "Stundenplan", false, 70, formular: "Maximale Stundenzahl in Folge"));
                l.Add(F("TagesrandPause", "Tagesrand-Pause", FeldArt.Ganzzahl, e => ((Lehrer)e).TagesrandPause, (e, v) => ((Lehrer)e).TagesrandPause = Math.Max(0, (int)v!), "Stundenplan", false, 80));
                l.Add(T("OptKz", "Lehrer Opt.Kz.", e => ((Lehrer)e).OptimierungsKz, (e, v) => ((Lehrer)e).OptimierungsKz = OptimierungsKzPruefen((string)v!), "Stundenplan", breite: 60, formular: "Lehrer Optimierungs-Kz."));
                l.Add(F("Verweildauer", "Max. Verweildauer", FeldArt.Ganzzahl, e => ((Lehrer)e).MaxVerweildauer, (e, v) => ((Lehrer)e).MaxVerweildauer = Math.Max(0, (int)v!), "Stundenplan", false, 80));
                l.Add(F("LeHalbtag", "(H) 1 Halbtag/Tag belegen", FeldArt.Bool, e => ((Lehrer)e).EinHalbtag, (e, v) => ((Lehrer)e).EinHalbtag = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeBelegung", "(Y) Belegung beibehalten", FeldArt.Bool, e => ((Lehrer)e).BelegungBeibehalten, (e, v) => ((Lehrer)e).BelegungBeibehalten = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeErsteLetzte", "(R) Nicht in 1. UND letzter Std.", FeldArt.Bool, e => ((Lehrer)e).NichtErsteUndLetzte, (e, v) => ((Lehrer)e).NichtErsteUndLetzte = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeWichtigA", "keine Hohlstunden (A)", FeldArt.Bool, e => ((Lehrer)e).WichtigHohlstunden, (e, v) => ((Lehrer)e).WichtigHohlstunden = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeWichtigB", "Mittagspause (B)", FeldArt.Bool, e => ((Lehrer)e).WichtigMittagspause, (e, v) => ((Lehrer)e).WichtigMittagspause = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeWichtigC", "Max. Stunden je Tag (C)", FeldArt.Bool, e => ((Lehrer)e).WichtigStundenProTag, (e, v) => ((Lehrer)e).WichtigStundenProTag = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeWichtigD", "Max. Stunden-Folge (D)", FeldArt.Bool, e => ((Lehrer)e).WichtigStundenFolge, (e, v) => ((Lehrer)e).WichtigStundenFolge = (bool)v!, "Kennzeichen", breite: 40));
                l.Add(F("LeKennzeichen", "Kennzeichen", FeldArt.NurLesen, e => ((Lehrer)e).Kennzeichen, null, "Kennzeichen", false, 70));
                l.Add(F("Wochensoll", "Wochensoll", FeldArt.Dezimal, e => ((Lehrer)e).Wochensoll, (e, v) => ((Lehrer)e).Wochensoll = (decimal)v!, "Werte", true, 70));
                l.Add(T("Ermaessigungen", "Ermäßigungen", e => string.Join("; ", ((Lehrer)e).Ermaessigungen.Select(x => $"{x.Grund}:{Format.Zahl(x.Wert)}")),
                    (e, v) => ((Lehrer)e).Ermaessigungen = ErmaessigungenParsen((string)v!), "Werte", false, 140));
                l.Add(F("Soll", "Soll (abzgl. Erm.)", FeldArt.NurLesen, e => ((Lehrer)e).Soll, null, "Werte", false, 70));
                l.Add(F("Ist", "Ist (Wert)", FeldArt.NurLesen, e => Core.Module.Wertrechnung.Ist(AppZustand.D, AppZustand.Doc.Index, (Lehrer)e), null, "Werte", false, 60));
                l.Add(F("Faecher", "Fächer", FeldArt.ElementListe, e => ((Lehrer)e).FaecherIds, (e, v) => ((Lehrer)e).FaecherIds = (List<int>)v!, "Lehrbefähig.", true, 90, ElementTyp.Fach, formular: "Lehrbefähigung (Fächer)"));
                l.Add(F("Wertkorrektur", "Wertkorrektur", FeldArt.Dezimal, e => ((Lehrer)e).Wertkorrektur, (e, v) => ((Lehrer)e).Wertkorrektur = (decimal)v!, "Werte", negativ: true));
                l.Add(F("VertrMaxWoche", "Vtr. max/Woche", FeldArt.Ganzzahl, e => ((Lehrer)e).VertretungenMaxWoche, (e, v) => ((Lehrer)e).VertretungenMaxWoche = (int)v!, "Vertr."));
                l.Add(F("VertrMaxMonat", "Vtr. max/Monat", FeldArt.Ganzzahl, e => ((Lehrer)e).VertretungenMaxMonat, (e, v) => ((Lehrer)e).VertretungenMaxMonat = (int)v!, "Vertr."));
                l.Add(F("KeineVertretung", "Keine Vertretung", FeldArt.Bool, e => ((Lehrer)e).KeineVertretung, (e, v) => ((Lehrer)e).KeineVertretung = (bool)v!, "Vertr."));
                l.Add(F("Sperrvermerk", "Sperrvermerk (0-9)", FeldArt.Ganzzahl, e => ((Lehrer)e).Sperrvermerk, (e, v) => ((Lehrer)e).Sperrvermerk = Math.Clamp((int)v!, 0, 9), "Vertr."));
                l.Add(F("BereitschaftenMax", "Bereitschaften max", FeldArt.Ganzzahl, e => ((Lehrer)e).BereitschaftenMax, (e, v) => ((Lehrer)e).BereitschaftenMax = (int)v!, "Vertr."));
                l.Add(F("AufsichtenMin", "Aufsichten min", FeldArt.Ganzzahl, e => ((Lehrer)e).AufsichtenMin, (e, v) => ((Lehrer)e).AufsichtenMin = (int)v!, "Pausenaufsicht"));
                l.Add(F("AufsichtenMax", "Aufsichten max", FeldArt.Ganzzahl, e => ((Lehrer)e).AufsichtenMax, (e, v) => ((Lehrer)e).AufsichtenMax = (int)v!, "Pausenaufsicht"));
                l.Add(F("KeineAufsicht", "Keine Aufsicht", FeldArt.Bool, e => ((Lehrer)e).KeineAufsicht, (e, v) => ((Lehrer)e).KeineAufsicht = (bool)v!, "Pausenaufsicht"));
                l.Add(F("PaMaxMinuten", "Aufs. max. Minuten", FeldArt.Ganzzahl, e => ((Lehrer)e).PausenaufsichtMaxMinuten, (e, v) => ((Lehrer)e).PausenaufsichtMaxMinuten = (int)v!, "Pausenaufsicht"));
                // Überblick Vertretungszähler (Handbuch: Lehrer | Stammdaten): Durchrechnungszeitraum laut Einstellungen der Vertretungsplanung
                l.Add(F("PaVertr", "PA-Vertr.", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).PaVertretungen, null, "Vertr.", false, 60));
                l.Add(F("PaEntf", "PA-Entf.", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).PaEntfaelle, null, "Vertr.", false, 60));
                l.Add(F("Zaehler", "Zähler", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Summe, null, "Vertr.", false, 50));
                l.Add(F("ZaehlerEntf", "Entf.", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Entfaelle, null, "Vertr.", false, 45));
                l.Add(F("ZaehlerVertr", "Vertretungen", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Vertretungen, null, "Vertr.", false, 75));
                l.Add(F("ZaehlerWk", "davon Wertkorrektur", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Wertkorrektur, null, "Vertr.", false, 75));
                l.Add(F("ZaehlerBetr", "Betreuung", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Betreuungen, null, "Vertr.", false, 60));
                l.Add(F("ZaehlerBez", "bezahlte Vertretungen", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).Bezahlt, null, "Vertr.", false, 60));
                l.Add(F("JahresZaehler", "Jahres-Zähler", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e, true).Summe, null, "Vertr.", false, 80));
                l.Add(F("JahresPa", "Jahres-Z. Pausenaufs.", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e, true).PaVertretungen, null, "Vertr.", false, 80));
                l.Add(F("AnzBereit", "Anz. Bereit.", FeldArt.NurLesen, e => Zaehler.Bereitschaften((Lehrer)e), null, "Vertr.", false, 70));
                l.Add(F("GehBereit", "Geh. Bereit.", FeldArt.NurLesen, e => Zaehler.Fuer((Lehrer)e).BereitschaftenGenutzt, null, "Vertr.", false, 70));
                break;
            case ElementTyp.Fach:
                l.AddRange(Gemeinsam());
                l.Add(T("Fachgruppe", "Fachgruppe", e => ((Fach)e).Fachgruppe, (e, v) => ((Fach)e).Fachgruppe = (string)v!, "Fach"));
                l.Add(T("FachArt", "Art", e => ((Fach)e).Art, (e, v) => ((Fach)e).Art = (string)v!, "Fach"));
                // Karteikarte „Kennzeichen“ (Handbuch 2.4.5.2)
                l.Add(F("Hauptfach", "Hauptfach", FeldArt.Bool, e => ((Fach)e).Hauptfach, (e, v) => ((Fach)e).Hauptfach = (bool)v!, "Kennzeichen", true, 60, formular: "(H) Hauptfach"));
                l.Add(F("Randstunde", "(R) Randstunden", FeldArt.Bool, e => ((Fach)e).Randstunde, (e, v) => ((Fach)e).Randstunde = (bool)v!, "Kennzeichen", formular: "(R) Randstunden(fach)"));
                l.Add(F("Freifach", "(F) Freifach", FeldArt.Bool, e => ((Fach)e).Freifach, (e, v) => ((Fach)e).Freifach = (bool)v!, "Kennzeichen"));
                l.Add(F("Mehrmals", "(2) auch mehrmals am Tag", FeldArt.Bool, e => ((Fach)e).MehrmalsProTag, (e, v) => { ((Fach)e).MehrmalsProTag = (bool)v!; if ((bool)v!) ((Fach)e).DoppelstundenEinhalten = false; }, "Kennzeichen"));
                l.Add(F("NichtRand", "(G) nicht in Randstunden", FeldArt.Bool, e => ((Fach)e).NichtInRandstunden, (e, v) => { ((Fach)e).NichtInRandstunden = (bool)v!; if ((bool)v!) ((Fach)e).DoppelstundenEinhalten = false; }, "Kennzeichen"));
                l.Add(F("DoppEinhalten", "(D) Doppelstunden einhalten", FeldArt.Bool, e => ((Fach)e).DoppelstundenEinhalten,
                    (e, v) => { ((Fach)e).DoppelstundenEinhalten = (bool)v!; if ((bool)v!) { ((Fach)e).MehrmalsProTag = false; ((Fach)e).NichtInRandstunden = false; } }, "Kennzeichen"));
                l.Add(F("DoppPause", "(E) Doppelstunde über *-Pausen", FeldArt.Bool, e => ((Fach)e).DoppelstundeUeberPause, (e, v) => ((Fach)e).DoppelstundeUeberPause = (bool)v!, "Kennzeichen"));
                l.Add(F("KeinePausenaufsicht", "(P) keine Pausenaufs. davor/danach", FeldArt.Bool, e => ((Fach)e).KeinePausenaufsicht, (e, v) => ((Fach)e).KeinePausenaufsicht = (bool)v!, "Kennzeichen"));
                l.Add(F("Sprechstunde", "(S) Sprechstunde", FeldArt.Bool, e => ((Fach)e).Sprechstunde, (e, v) => ((Fach)e).Sprechstunde = (bool)v!, "Kennzeichen"));
                l.Add(F("FaKennzeichen", "Kennzeichen", FeldArt.NurLesen, e => ((Fach)e).Kennzeichen, null, "Kennzeichen", false, 70));
                l.Add(F("Fachraum", "Fachraum", FeldArt.Element, e => ((Fach)e).FachraumId, (e, v) => ((Fach)e).FachraumId = (int?)v, "Stundenplan", true, 70, ElementTyp.Raum, formular: "Fachraum (Kurzname)"));
                l.Add(F("Fachraumgruppe", "Raumgruppe", FeldArt.Element, e => ((Fach)e).FachraumgruppeId, (e, v) => ((Fach)e).FachraumgruppeId = (int?)v, "Stundenplan", false, 70, ElementTyp.Raumgruppe));
                l.Add(F("Doppelstunden", "Dopp.Std.", FeldArt.Bereich, e => ((Fach)e).Doppelstunden, (e, v) => ((Fach)e).Doppelstunden = (Bereich)v!, "Stundenplan", true, 60, formular: "Doppelstunden (min-max)"));
                l.Add(F("KeinNachmittag", "(N) kein Nachm.", FeldArt.Bool, e => ((Fach)e).KeinNachmittag, (e, v) => ((Fach)e).KeinNachmittag = (bool)v!, "Stundenplan"));
                l.Add(F("NachmStd", "Nachm.Std.", FeldArt.Bereich, e => ((Fach)e).Nachmittagsstunden, (e, v) => ((Fach)e).Nachmittagsstunden = (Bereich)v!, "Stundenplan", false, 65, formular: "Nachmittagsstunden (min-max)"));
                l.Add(F("WochenStd", "Wochenstd.", FeldArt.Bereich, e => ((Fach)e).WochenstundenBereich, (e, v) => ((Fach)e).WochenstundenBereich = (Bereich)v!, "Stundenplan", false, 65, formular: "Wochenstunden (min-max)"));
                l.Add(T("FachfolgeLe", "Fachfolge Lehrer", e => ((Fach)e).FachfolgeLehrer, (e, v) => ((Fach)e).FachfolgeLehrer = FachfolgePruefen((string)v!), "Stundenplan", breite: 60));
                l.Add(T("FachfolgeKl", "Fachfolge Klassen", e => ((Fach)e).FachfolgeKlassen, (e, v) => ((Fach)e).FachfolgeKlassen = FachfolgePruefen((string)v!), "Stundenplan", breite: 60));
                l.Add(F("WerteFaktor", "Werte-Faktor", FeldArt.Dezimal, e => ((Fach)e).WerteFaktor, (e, v) => ((Fach)e).WerteFaktor = (decimal)v!, "Werte"));
                l.Add(F("Bereitschaft", "(B) Bereitschaft", FeldArt.Bool, e => ((Fach)e).Bereitschaft, (e, v) => ((Fach)e).Bereitschaft = (bool)v!, "Vertr."));
                l.Add(F("BereitschaftImDeputat", "Bereitsch. im Deputat", FeldArt.Bool, e => ((Fach)e).BereitschaftImDeputat, (e, v) => ((Fach)e).BereitschaftImDeputat = (bool)v!, "Vertr."));
                l.Add(F("EntfallNichtZaehlen", "Entfall nicht zählen", FeldArt.Bool, e => ((Fach)e).EntfallNichtZaehlen, (e, v) => ((Fach)e).EntfallNichtZaehlen = (bool)v!, "Vertr."));
                l.Add(F("KeineVertretung", "Keine Vertretung", FeldArt.Bool, e => ((Fach)e).KeineVertretung, (e, v) => ((Fach)e).KeineVertretung = (bool)v!, "Vertr."));
                break;
            case ElementTyp.Gang:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Bedarf", "Aufsichten je Pause", FeldArt.Ganzzahl, e => ((Gang)e).Bedarf, (e, v) => ((Gang)e).Bedarf = (int)v!, "Allgemeines", true, 110));
                l.Add(F("NurMaennlich", "nur männlich", FeldArt.Bool, e => ((Gang)e).NurMaennlich, (e, v) => ((Gang)e).NurMaennlich = (bool)v!, "Allgemeines"));
                l.Add(F("NurWeiblich", "nur weiblich", FeldArt.Bool, e => ((Gang)e).NurWeiblich, (e, v) => ((Gang)e).NurWeiblich = (bool)v!, "Allgemeines"));
                break;
            case ElementTyp.Abteilung:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Leiter", "Abteilungsleiter", FeldArt.Element, e => ((Abteilung)e).LeiterId, (e, v) => ((Abteilung)e).LeiterId = (int?)v, "Allgemeines", true, 90, ElementTyp.Lehrer));
                break;
            case ElementTyp.Abwesenheitsgrund:
                // Spalten wie im Handbuch: Name, Langname, Beschr., Entf zählen, FS n.zählen, N. in Abs.kopf, Statistik
                l.AddRange(Gemeinsam().Take(2));
                l.Add(T("Beschreibung", "Beschr.", e => e.Beschreibung, (e, v) => e.Beschreibung = (string)v!, std: true, breite: 60));
                l.Add(F("EntfallZaehlen", "Entf zählen", FeldArt.Bool, e => ((Abwesenheitsgrund)e).EntfallZaehlen, (e, v) => ((Abwesenheitsgrund)e).EntfallZaehlen = (bool)v!, "Allgemeines", true));
                l.Add(F("FreisetzungNichtZaehlen", "FS n.zählen", FeldArt.Bool, e => ((Abwesenheitsgrund)e).FreisetzungNichtZaehlen, (e, v) => ((Abwesenheitsgrund)e).FreisetzungNichtZaehlen = (bool)v!, "Allgemeines", true));
                l.Add(F("NichtImAbsenzkopf", "N. in Abs.kopf", FeldArt.Bool, e => ((Abwesenheitsgrund)e).NichtImAbsenzkopf, (e, v) => ((Abwesenheitsgrund)e).NichtImAbsenzkopf = (bool)v!, "Allgemeines", true));
                l.Add(T("Statistik", "Statistik", e => e.Statistik, (e, v) => e.Statistik = (string)v!, std: true, breite: 60));
                l.Add(Gemeinsam().ElementAt(2));
                l.Add(F("Statistikrelevant", "Statistikrelevant", FeldArt.Bool, e => ((Abwesenheitsgrund)e).Statistikrelevant, (e, v) => ((Abwesenheitsgrund)e).Statistikrelevant = (bool)v!, "Allgemeines"));
                l.Add(F("Vertraulich", "Vertraulich", FeldArt.Bool, e => ((Abwesenheitsgrund)e).Vertraulich, (e, v) => ((Abwesenheitsgrund)e).Vertraulich = (bool)v!, "Allgemeines"));
                l.Add(F("Hintergrund", "Farbe", FeldArt.Farbe, e => e.Hintergrund, (e, v) => e.Hintergrund = (int)v!, "Allgemeines", false, 50));
                break;
            case ElementTyp.Schueler:
                l.Add(T("Kurzname", "Nachname", e => e.Kurzname, null, std: true, breite: 90));
                l.Add(T("Vorname", "Vorname", e => ((Schueler)e).Vorname, (e, v) => ((Schueler)e).Vorname = (string)v!, std: true, breite: 90));
                l.Add(F("Klasse", "Klasse", FeldArt.Element, e => ((Schueler)e).KlasseId, (e, v) => ((Schueler)e).KlasseId = (int?)v, "Allgemeines", true, 60, ElementTyp.Klasse));
                l.Add(T("Geschlecht", "Geschlecht", e => ((Schueler)e).Geschlecht, (e, v) => ((Schueler)e).Geschlecht = (string)v!));
                l.Add(T("Kurswahl", "Kurswahlen", e => string.Join(",", ((Schueler)e).KurswahlIds.Select(id => AppZustand.Doc.Index.Unterrichte.GetValueOrDefault(id)?.Kursname ?? "?")),
                    (e, v) => ((Schueler)e).KurswahlIds = ((string)v!).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => AppZustand.D.Unterrichte.FirstOrDefault(u => u.Kursname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Kurs \"{k}\" unbekannt")).ToList(),
                    "Allgemeines", true, 260));
                break;
        }
        return l;
    }

    /// <summary>Disloz.: 1–9 (mit Zeitstaffelung) bzw. A–E (ohne Zeitstaffelung).</summary>
    internal static string DislozierungPruefen(string s)
    {
        if (s.Trim().Length == 0) return "";
        var n = Raum.DislozierungNormal(s);
        return n.Length > 0 ? n : throw new FormatException("Erlaubt sind die Werte 1 bis 9 (mit Zeitstaffelung) und A bis E (ohne Zeitstaffelung).");
    }

    /// <summary>Lehrer Optimierungs-Kz.: Ziffern 1–9 oder Buchstaben A–Z.</summary>
    internal static string OptimierungsKzPruefen(string s)
    {
        var t = s.Trim().ToUpperInvariant();
        if (t.Length == 0) return "";
        return t.Length == 1 && (t[0] is >= '1' and <= '9' or >= 'A' and <= 'Z') ? t : throw new FormatException("Erlaubte Eingaben sind die Ziffern 1 bis 9 und die Buchstaben A-Z.");
    }

    /// <summary>Fachfolge: 1–9 (positive Fachfolge) bzw. A–F (negative Fachfolge).</summary>
    internal static string FachfolgePruefen(string s)
    {
        var t = s.Trim().ToUpperInvariant();
        if (t.Length == 0) return "";
        return t.Length == 1 && (t[0] is >= '1' and <= '9' or >= 'A' and <= 'F') ? t : throw new FormatException("Erlaubt sind 1-9 (Folgestunden) und A-F (keine Folgestunden).");
    }

    private static List<Ermaessigung> ErmaessigungenParsen(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t =>
        {
            var p = t.Split(':');
            return new Ermaessigung { Grund = p[0].Trim(), Wert = p.Length > 1 ? decimal.Parse(p[1].Trim().Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")) : 0 };
        }).ToList();
}

/// <summary>
/// Zählerstände je Lehrer für die Stammdatenspalten (Überblick Vertretungszähler): Durchrechnungszeitraum um das eingestellte Datum
/// bzw. das ganze Schuljahr („Jahres-Zähler“). Zwischengespeichert, bis sich die Daten oder das Datum ändern.
/// </summary>
public static class Zaehler
{
    public sealed record Stand(int Vertretungen, int Entfaelle, decimal Summe, decimal Wertkorrektur, int Betreuungen, int Bezahlt, int PaVertretungen, int PaEntfaelle, int BereitschaftenGenutzt);

    private static object? _schluessel;
    private static DateOnly _datum;
    private static readonly Dictionary<(int, bool), Stand> Cache = new();

    public static Stand Fuer(Lehrer l, bool schuljahr = false)
    {
        var d = AppZustand.D;
        var ix = AppZustand.Doc.Index;
        if (!ReferenceEquals(_schluessel, ix) || _datum != AppZustand.Datum)
        {
            Cache.Clear();
            _schluessel = ix;
            _datum = AppZustand.Datum;
        }
        if (Cache.TryGetValue((l.Id, schuljahr), out var s)) return s;
        var (von, bis) = schuljahr ? (d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis) : Core.Vertretungsplanung.Zaehlung.Zeitraum(d, AppZustand.Datum);
        var z = Core.Vertretungsplanung.Zaehlung.Lehrer(d, l, von, bis);
        var paV = z.Zeilen.Count(x => x.Code == "P");
        var paE = d.Vertretungen.Count(v => v.Datum >= von && v.Datum <= bis && v.Art == VertretungsArt.Pausenaufsicht && v.LehrerId == l.Id && v.VertreterId != l.Id);
        s = new Stand(z.Vertretungen, z.Entfaelle, z.Summe, z.Zeilen.Where(x => x.Code == "WK").Sum(x => x.Wert), z.Betreuungen, z.Bezahlt, paV, paE, z.BereitschaftenGenutzt);
        Cache[(l.Id, schuljahr)] = s;
        return s;
    }

    /// <summary>Anz. Bereit.: verplante Bereitschaften der aktiven Periode; mit Deputats-Bereitschaften als „Summe (regulär:Deputat)“.</summary>
    public static string Bereitschaften(Lehrer l)
    {
        var d = AppZustand.D;
        var ix = AppZustand.Doc.Index;
        int regulaer = 0, deputat = 0;
        foreach (var p in d.Stundenplan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            foreach (var zeile in u.Zeilen.Where(z => z.LehrerId == l.Id && z.FachId is int f && ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft))
                if (ix.Faecher[zeile.FachId!.Value].BereitschaftImDeputat) deputat++; else regulaer++;
        }
        if (regulaer + deputat == 0) return "";
        return deputat == 0 ? regulaer.ToString() : $"{regulaer + deputat} ({regulaer}:{deputat})";
    }
}
