using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

public enum FeldArt { Text, Ganzzahl, Dezimal, Bool, Bereich, Element, ElementListe, Farbe, Auswahl, Datum, NurLesen }

/// <summary>
/// Beschreibung eines Eingabefeldes der Stammdaten (Raster- und Formularansicht). Übernommen aus der Desktop-Anwendung
/// (UntPlan.App, Fenster/Felder.cs) – gleiche Schlüssel, Titel, Karteikarten und Breiten, damit Ansichten austauschbar bleiben.
/// </summary>
public sealed class Feld
{
    public string Schluessel { get; init; } = "";
    public string Titel { get; init; } = "";
    public string Karteikarte { get; init; } = "Allgemein";
    public FeldArt Art { get; init; }
    public ElementTyp RefTyp { get; init; }
    public string[] Auswahl { get; init; } = Array.Empty<string>();
    public Func<Element, object?> Get { get; init; } = _ => null;
    public Action<Element, object?>? Set { get; init; }
    public bool Standard { get; init; }
    public int Breite { get; init; } = 70;
    public string Hinweis { get; init; } = "";

    public bool Editierbar => Set != null && Art != FeldArt.NurLesen;

    public string Anzeige(Element e, DatenIndex ix)
    {
        var v = Get(e);
        return v switch
        {
            null => "",
            bool b => b ? "X" : "",
            Bereich r => r.ToString(),
            decimal m => m == 0 ? "" : Format.Zahl(m),
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
            case FeldArt.Ganzzahl: return text.Length == 0 ? 0 : int.Parse(text, CultureInfo.GetCultureInfo("de-DE"));
            case FeldArt.Dezimal: return text.Length == 0 ? 0m : decimal.Parse(text.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE"));
            case FeldArt.Bool: return text.Equals("X", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
            case FeldArt.Bereich: return Bereich.Parse(text);
            case FeldArt.Datum: return text.Length == 0 ? null : DateOnly.Parse(text, CultureInfo.GetCultureInfo("de-DE"));
            case FeldArt.Auswahl:
                if (text.Length == 0) return 0;
                var idx = Array.FindIndex(Auswahl, a => a.Equals(text, StringComparison.OrdinalIgnoreCase) || a.StartsWith(text + " ", StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
                return int.Parse(text.Split(' ')[0]);
            case FeldArt.Element:
                if (text.Length == 0) return null;
                return Suchen(d, text).Id;
            case FeldArt.ElementListe:
                return text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => Suchen(d, t).Id).Distinct().ToList();
            case FeldArt.Farbe:
                if (text.Length == 0) return 0;
                return unchecked((int)(0xFF000000 | Convert.ToUInt32(text.TrimStart('#'), 16)));
            default: return null;
        }
    }

    private Element Suchen(Schuldatei d, string text) =>
        d.AlleElemente(RefTyp).FirstOrDefault(x => x.Kurzname.Equals(text, StringComparison.OrdinalIgnoreCase))
        ?? throw new FormatException($"{Typnamen.Name(RefTyp)} \"{text}\" ist nicht vorhanden.");
}

public static class Felder
{
    private static Feld T(string k, string titel, Func<Element, object?> get, Action<Element, object?>? set, string karte = "Allgemein", bool std = false, int breite = 70) =>
        new() { Schluessel = k, Titel = titel, Art = FeldArt.Text, Get = get, Set = set, Karteikarte = karte, Standard = std, Breite = breite };

    private static Feld F(string k, string titel, FeldArt art, Func<Element, object?> get, Action<Element, object?>? set, string karte = "Allgemein", bool std = false, int breite = 60, ElementTyp refTyp = ElementTyp.Raum, string[]? auswahl = null) =>
        new() { Schluessel = k, Titel = titel, Art = art, Get = get, Set = set, Karteikarte = karte, Standard = std, Breite = breite, RefTyp = refTyp, Auswahl = auswahl ?? Array.Empty<string>() };

    private static IEnumerable<Feld> Gemeinsam(string langname = "Langname") => new[]
    {
        T("Kurzname", "Name", e => e.Kurzname, null, std: true, breite: 60),
        T("Langname", langname, e => e.Langname, (e, v) => e.Langname = (string)v!, std: true, breite: 140),
        T("Text", "Text", e => e.Text, (e, v) => e.Text = (string)v!, std: true, breite: 60),
        T("Beschreibung", "Beschreibung", e => e.Beschreibung, (e, v) => e.Beschreibung = (string)v!),
        T("Alias", "Aliasname", e => e.Alias, (e, v) => e.Alias = (string)v!, "Diverses"),
        T("Statistik", "Statistik-Kennz.", e => e.Statistik, (e, v) => e.Statistik = (string)v!, "Diverses"),
        F("Abteilung", "Abteilung", FeldArt.Element, e => e.AbteilungId, (e, v) => e.AbteilungId = (int?)v, "Diverses", refTyp: ElementTyp.Abteilung),
        F("Fix", "Fix (X)", FeldArt.Bool, e => e.Fix, (e, v) => e.Fix = (bool)v!, "Diverses", true, 45),
        F("Ignorieren", "Ignore (i)", FeldArt.Bool, e => e.Ignorieren, (e, v) => e.Ignorieren = (bool)v!, "Diverses", true, 50),
        F("Hintergrund", "Farbe", FeldArt.Farbe, e => e.Hintergrund, (e, v) => e.Hintergrund = (int)v!, "Diverses", false, 50),
    };

    /// <summary>Felder eines Elementtyps; berechnete Felder (Ist-Wert, Kurswahlen) beziehen sich auf das übergebene Dokument.</summary>
    public static List<Feld> Fuer(ElementTyp typ, Dokument doc)
    {
        var l = new List<Feld>();
        switch (typ)
        {
            case ElementTyp.Raum:
                l.AddRange(Gemeinsam());
                l.Add(F("Ausweichraum", "Ausweichraum", FeldArt.Element, e => ((Raum)e).AusweichraumId, (e, v) => ((Raum)e).AusweichraumId = (int?)v, "Unterricht", true, 80, ElementTyp.Raum));
                l.Add(F("Raumgewicht", "Raumgew.", FeldArt.Auswahl, e => ((Raum)e).Raumgewicht, (e, v) => ((Raum)e).Raumgewicht = (int)v!, "Unterricht", true, 70,
                    auswahl: Enumerable.Range(0, 5).Select(i => $"{i} {Raum.GewichtText(i)}").ToArray()));
                l.Add(F("Kapazitaet", "Kapaz.", FeldArt.Ganzzahl, e => ((Raum)e).Kapazitaet, (e, v) => ((Raum)e).Kapazitaet = (int)v!, "Allgemein", true, 50));
                l.Add(F("Gang", "Gänge", FeldArt.Element, e => ((Raum)e).GangId, (e, v) => ((Raum)e).GangId = (int?)v, "Pausenaufsicht", true, 55, ElementTyp.Gang));
                l.Add(T("Stockwerk", "Stockwerk", e => ((Raum)e).Stockwerk, (e, v) => ((Raum)e).Stockwerk = (string)v!));
                l.Add(T("Raumart", "Raumart", e => ((Raum)e).Raumart, (e, v) => ((Raum)e).Raumart = (string)v!));
                l.Add(F("Buchbar", "buchbar (Web)", FeldArt.Bool, e => ((Raum)e).Buchbar, (e, v) => ((Raum)e).Buchbar = (bool)v!, "Allgemein", true, 70));
                break;
            case ElementTyp.Raumgruppe:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Raeume", "Raum", FeldArt.ElementListe, e => ((Raumgruppe)e).RaumIds, (e, v) => ((Raumgruppe)e).RaumIds = (List<int>)v!, "Allgemein", true, 200, ElementTyp.Raum));
                break;
            case ElementTyp.Klasse:
                l.AddRange(Gemeinsam());
                l.Add(F("Raum", "Raum", FeldArt.Element, e => ((Klasse)e).StammraumId, (e, v) => ((Klasse)e).StammraumId = (int?)v, "Allgemein", true, 50, ElementTyp.Raum));
                l.Add(F("HauptfaecherProTag", "Hauptf./Tag", FeldArt.Ganzzahl, e => ((Klasse)e).HauptfaecherProTag, (e, v) => ((Klasse)e).HauptfaecherProTag = (int)v!, "Unterricht", true, 70));
                l.Add(F("HauptfaecherFolge", "Hauptf.Folge", FeldArt.Ganzzahl, e => ((Klasse)e).HauptfaecherFolge, (e, v) => ((Klasse)e).HauptfaecherFolge = (int)v!, "Unterricht", true, 75));
                l.Add(F("Mittagspause", "Mitt-pause", FeldArt.Bereich, e => ((Klasse)e).Mittagspause, (e, v) => ((Klasse)e).Mittagspause = (Bereich)v!, "Unterricht", true, 65));
                l.Add(F("StundenProTag", "Std./Tag", FeldArt.Bereich, e => ((Klasse)e).StundenProTag, (e, v) => ((Klasse)e).StundenProTag = (Bereich)v!, "Unterricht", true, 60));
                l.Add(F("MaxNachmittage", "max. Nachm.", FeldArt.Ganzzahl, e => ((Klasse)e).MaxNachmittage, (e, v) => ((Klasse)e).MaxNachmittage = (int)v!, "Unterricht", false, 70));
                l.Add(F("Klassenlehrer", "Klassenlehrer", FeldArt.ElementListe, e => ((Klasse)e).KlassenlehrerIds, (e, v) => ((Klasse)e).KlassenlehrerIds = (List<int>)v!, "Allgemein", true, 90, ElementTyp.Lehrer));
                l.Add(T("Jahrgang", "Jahrgang", e => ((Klasse)e).Jahrgang, (e, v) => ((Klasse)e).Jahrgang = (string)v!));
                l.Add(T("Stufe", "Stufe", e => ((Klasse)e).Stufe, (e, v) => ((Klasse)e).Stufe = (string)v!));
                l.Add(F("SchuelerM", "Schüler m", FeldArt.Ganzzahl, e => ((Klasse)e).SchuelerMaennlich, (e, v) => ((Klasse)e).SchuelerMaennlich = (int)v!, "Werte"));
                l.Add(F("SchuelerW", "Schüler w", FeldArt.Ganzzahl, e => ((Klasse)e).SchuelerWeiblich, (e, v) => ((Klasse)e).SchuelerWeiblich = (int)v!, "Werte"));
                l.Add(T("Klassengruppe", "Klassengruppe", e => ((Klasse)e).Klassengruppe, (e, v) => ((Klasse)e).Klassengruppe = (string)v!, "Diverses"));
                l.Add(F("VonDatum", "von", FeldArt.Datum, e => ((Klasse)e).VonDatum, (e, v) => ((Klasse)e).VonDatum = (DateOnly?)v, "Diverses"));
                l.Add(F("BisDatum", "bis", FeldArt.Datum, e => ((Klasse)e).BisDatum, (e, v) => ((Klasse)e).BisDatum = (DateOnly?)v, "Diverses"));
                break;
            case ElementTyp.Lehrer:
                l.AddRange(Gemeinsam("Nachname"));
                l.Insert(2, T("Vorname", "Vorname", e => ((Lehrer)e).Vorname, (e, v) => ((Lehrer)e).Vorname = (string)v!, std: false, breite: 90));
                l.Add(T("Titel", "Titel", e => ((Lehrer)e).Titel, (e, v) => ((Lehrer)e).Titel = (string)v!));
                l.Add(F("Raum", "Raum", FeldArt.Element, e => ((Lehrer)e).StammraumId, (e, v) => ((Lehrer)e).StammraumId = (int?)v, "Allgemein", true, 50, ElementTyp.Raum));
                l.Add(F("Hohlstunden", "HohlStd. soll", FeldArt.Bereich, e => ((Lehrer)e).Hohlstunden, (e, v) => ((Lehrer)e).Hohlstunden = (Bereich)v!, "Unterricht", true, 75));
                l.Add(F("StundenProTag", "Std./Tag", FeldArt.Bereich, e => ((Lehrer)e).StundenProTag, (e, v) => ((Lehrer)e).StundenProTag = (Bereich)v!, "Unterricht", true, 60));
                l.Add(F("Mittagspause", "Mitt-pause", FeldArt.Bereich, e => ((Lehrer)e).Mittagspause, (e, v) => ((Lehrer)e).Mittagspause = (Bereich)v!, "Unterricht", true, 65));
                l.Add(F("MaxNachmittage", "max. Nachm.", FeldArt.Ganzzahl, e => ((Lehrer)e).MaxNachmittage, (e, v) => ((Lehrer)e).MaxNachmittage = (int)v!, "Unterricht", false, 70));
                l.Add(F("Faecher", "Fächer", FeldArt.ElementListe, e => ((Lehrer)e).FaecherIds, (e, v) => ((Lehrer)e).FaecherIds = (List<int>)v!, "Unterricht", true, 90, ElementTyp.Fach));
                l.Add(F("Wochensoll", "Wochensoll", FeldArt.Dezimal, e => ((Lehrer)e).Wochensoll, (e, v) => ((Lehrer)e).Wochensoll = (decimal)v!, "Werte", true, 70));
                l.Add(T("Ermaessigungen", "Ermäßigungen", e => string.Join("; ", ((Lehrer)e).Ermaessigungen.Select(x => $"{x.Grund}:{Format.Zahl(x.Wert)}")),
                    (e, v) => ((Lehrer)e).Ermaessigungen = ErmaessigungenParsen((string)v!), "Werte", false, 140));
                l.Add(F("Soll", "Soll (abzgl. Erm.)", FeldArt.NurLesen, e => ((Lehrer)e).Soll, null, "Werte", false, 70));
                l.Add(F("Ist", "Ist (Wert)", FeldArt.NurLesen, e => UntPlan.Core.Module.Wertrechnung.Ist(doc.Daten, doc.Index, (Lehrer)e), null, "Werte", false, 60));
                l.Add(F("Wertkorrektur", "Wertkorrektur", FeldArt.Dezimal, e => ((Lehrer)e).Wertkorrektur, (e, v) => ((Lehrer)e).Wertkorrektur = (decimal)v!, "Werte"));
                l.Add(T("Email", "E-Mail", e => ((Lehrer)e).Email, (e, v) => ((Lehrer)e).Email = (string)v!, "Allgemeine Daten", false, 150));
                l.Add(T("Telefon", "Telefon", e => ((Lehrer)e).Telefon, (e, v) => ((Lehrer)e).Telefon = (string)v!, "Allgemeine Daten"));
                l.Add(T("Personalnummer", "Pers.-Nr.", e => ((Lehrer)e).Personalnummer, (e, v) => ((Lehrer)e).Personalnummer = (string)v!, "Allgemeine Daten"));
                l.Add(F("Geburtsdatum", "Geburtsdatum", FeldArt.Datum, e => ((Lehrer)e).Geburtsdatum, (e, v) => ((Lehrer)e).Geburtsdatum = (DateOnly?)v, "Allgemeine Daten", false, 80));
                l.Add(T("Geschlecht", "Geschlecht", e => ((Lehrer)e).Geschlecht, (e, v) => ((Lehrer)e).Geschlecht = (string)v!, "Allgemeine Daten"));
                l.Add(T("Status", "Status", e => ((Lehrer)e).Status, (e, v) => ((Lehrer)e).Status = (string)v!, "Allgemeine Daten"));
                l.Add(F("VertrMaxWoche", "Vtr. max/Woche", FeldArt.Ganzzahl, e => ((Lehrer)e).VertretungenMaxWoche, (e, v) => ((Lehrer)e).VertretungenMaxWoche = (int)v!, "Vertretungsplanung"));
                l.Add(F("VertrMaxMonat", "Vtr. max/Monat", FeldArt.Ganzzahl, e => ((Lehrer)e).VertretungenMaxMonat, (e, v) => ((Lehrer)e).VertretungenMaxMonat = (int)v!, "Vertretungsplanung"));
                l.Add(F("KeineVertretung", "Keine Vertretung", FeldArt.Bool, e => ((Lehrer)e).KeineVertretung, (e, v) => ((Lehrer)e).KeineVertretung = (bool)v!, "Vertretungsplanung"));
                l.Add(F("AufsichtenMin", "Aufsichten min", FeldArt.Ganzzahl, e => ((Lehrer)e).AufsichtenMin, (e, v) => ((Lehrer)e).AufsichtenMin = (int)v!, "Pausenaufsicht"));
                l.Add(F("AufsichtenMax", "Aufsichten max", FeldArt.Ganzzahl, e => ((Lehrer)e).AufsichtenMax, (e, v) => ((Lehrer)e).AufsichtenMax = (int)v!, "Pausenaufsicht"));
                l.Add(F("KeineAufsicht", "Keine Aufsicht", FeldArt.Bool, e => ((Lehrer)e).KeineAufsicht, (e, v) => ((Lehrer)e).KeineAufsicht = (bool)v!, "Pausenaufsicht"));
                l.Add(F("PaMaxMinuten", "Aufs. max. Minuten", FeldArt.Ganzzahl, e => ((Lehrer)e).PausenaufsichtMaxMinuten, (e, v) => ((Lehrer)e).PausenaufsichtMaxMinuten = (int)v!, "Pausenaufsicht"));
                l.Add(F("Sperrvermerk", "Sperrvermerk (0-9)", FeldArt.Ganzzahl, e => ((Lehrer)e).Sperrvermerk, (e, v) => ((Lehrer)e).Sperrvermerk = Math.Clamp((int)v!, 0, 9), "Vertretungsplanung"));
                l.Add(F("BereitschaftenMax", "Bereitschaften max", FeldArt.Ganzzahl, e => ((Lehrer)e).BereitschaftenMax, (e, v) => ((Lehrer)e).BereitschaftenMax = (int)v!, "Vertretungsplanung"));
                break;
            case ElementTyp.Fach:
                l.AddRange(Gemeinsam());
                l.Add(F("Fachraum", "Fachraum", FeldArt.Element, e => ((Fach)e).FachraumId, (e, v) => ((Fach)e).FachraumId = (int?)v, "Unterricht", true, 70, ElementTyp.Raum));
                l.Add(F("Fachraumgruppe", "Raumgruppe", FeldArt.Element, e => ((Fach)e).FachraumgruppeId, (e, v) => ((Fach)e).FachraumgruppeId = (int?)v, "Unterricht", false, 70, ElementTyp.Raumgruppe));
                l.Add(F("Hauptfach", "Hauptfach", FeldArt.Bool, e => ((Fach)e).Hauptfach, (e, v) => ((Fach)e).Hauptfach = (bool)v!, "Unterricht", true, 60));
                l.Add(F("Doppelstunden", "Dopp.Std.", FeldArt.Bereich, e => ((Fach)e).Doppelstunden, (e, v) => ((Fach)e).Doppelstunden = (Bereich)v!, "Unterricht", true, 60));
                l.Add(F("Mehrmals", "(M) mehrmals/Tag", FeldArt.Bool, e => ((Fach)e).MehrmalsProTag, (e, v) => ((Fach)e).MehrmalsProTag = (bool)v!, "Unterricht"));
                l.Add(F("Randstunde", "(R) Randstunde", FeldArt.Bool, e => ((Fach)e).Randstunde, (e, v) => ((Fach)e).Randstunde = (bool)v!, "Unterricht"));
                l.Add(F("KeinNachmittag", "(N) kein Nachm.", FeldArt.Bool, e => ((Fach)e).KeinNachmittag, (e, v) => ((Fach)e).KeinNachmittag = (bool)v!, "Unterricht"));
                l.Add(F("Bereitschaft", "(B) Bereitschaft", FeldArt.Bool, e => ((Fach)e).Bereitschaft, (e, v) => ((Fach)e).Bereitschaft = (bool)v!, "Vertretungsplanung"));
                l.Add(F("BereitschaftImDeputat", "Bereitsch. im Deputat", FeldArt.Bool, e => ((Fach)e).BereitschaftImDeputat, (e, v) => ((Fach)e).BereitschaftImDeputat = (bool)v!, "Vertretungsplanung"));
                l.Add(F("EntfallNichtZaehlen", "Entfall nicht zählen", FeldArt.Bool, e => ((Fach)e).EntfallNichtZaehlen, (e, v) => ((Fach)e).EntfallNichtZaehlen = (bool)v!, "Vertretungsplanung"));
                l.Add(F("KeineVertretung", "Keine Vertretung", FeldArt.Bool, e => ((Fach)e).KeineVertretung, (e, v) => ((Fach)e).KeineVertretung = (bool)v!, "Vertretungsplanung"));
                l.Add(F("NichtDrucken", "Nicht drucken", FeldArt.Bool, e => ((Fach)e).NichtDrucken, (e, v) => ((Fach)e).NichtDrucken = (bool)v!, "Vertretungsplanung"));
                l.Add(F("KeinePausenaufsicht", "Keine Pausenaufsicht", FeldArt.Bool, e => ((Fach)e).KeinePausenaufsicht, (e, v) => ((Fach)e).KeinePausenaufsicht = (bool)v!, "Pausenaufsicht"));
                l.Add(T("Fachgruppe", "Fachgruppe", e => ((Fach)e).Fachgruppe, (e, v) => ((Fach)e).Fachgruppe = (string)v!));
                l.Add(F("WerteFaktor", "Werte-Faktor", FeldArt.Dezimal, e => ((Fach)e).WerteFaktor, (e, v) => ((Fach)e).WerteFaktor = (decimal)v!, "Werte"));
                break;
            case ElementTyp.Gang:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Bedarf", "Aufsichten je Pause", FeldArt.Ganzzahl, e => ((Gang)e).Bedarf, (e, v) => ((Gang)e).Bedarf = (int)v!, "Allgemein", true, 110));
                l.Add(F("NurMaennlich", "nur männlich", FeldArt.Bool, e => ((Gang)e).NurMaennlich, (e, v) => ((Gang)e).NurMaennlich = (bool)v!, "Allgemein"));
                l.Add(F("NurWeiblich", "nur weiblich", FeldArt.Bool, e => ((Gang)e).NurWeiblich, (e, v) => ((Gang)e).NurWeiblich = (bool)v!, "Allgemein"));
                break;
            case ElementTyp.Abteilung:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Leiter", "Abteilungsleiter", FeldArt.Element, e => ((Abteilung)e).LeiterId, (e, v) => ((Abteilung)e).LeiterId = (int?)v, "Allgemein", true, 90, ElementTyp.Lehrer));
                break;
            case ElementTyp.Abwesenheitsgrund:
                l.AddRange(Gemeinsam().Take(3));
                l.Add(F("Statistikrelevant", "Statistik", FeldArt.Bool, e => ((Abwesenheitsgrund)e).Statistikrelevant, (e, v) => ((Abwesenheitsgrund)e).Statistikrelevant = (bool)v!, "Allgemein", true));
                l.Add(F("Vertraulich", "Vertraulich", FeldArt.Bool, e => ((Abwesenheitsgrund)e).Vertraulich, (e, v) => ((Abwesenheitsgrund)e).Vertraulich = (bool)v!, "Allgemein", true));
                l.Add(F("EntfallZaehlen", "Entfall zählen", FeldArt.Bool, e => ((Abwesenheitsgrund)e).EntfallZaehlen, (e, v) => ((Abwesenheitsgrund)e).EntfallZaehlen = (bool)v!, "Allgemein", true));
                l.Add(F("FreisetzungNichtZaehlen", "Freisetzung nicht zählen", FeldArt.Bool, e => ((Abwesenheitsgrund)e).FreisetzungNichtZaehlen, (e, v) => ((Abwesenheitsgrund)e).FreisetzungNichtZaehlen = (bool)v!, "Allgemein", true));
                l.Add(F("NichtImAbsenzkopf", "nicht im Kopf", FeldArt.Bool, e => ((Abwesenheitsgrund)e).NichtImAbsenzkopf, (e, v) => ((Abwesenheitsgrund)e).NichtImAbsenzkopf = (bool)v!, "Allgemein"));
                l.Add(F("Hintergrund", "Farbe", FeldArt.Farbe, e => e.Hintergrund, (e, v) => e.Hintergrund = (int)v!, "Allgemein", false, 50));
                break;
            case ElementTyp.Schueler:
                l.Add(T("Kurzname", "Nachname", e => e.Kurzname, null, std: true, breite: 90));
                l.Add(T("Vorname", "Vorname", e => ((Schueler)e).Vorname, (e, v) => ((Schueler)e).Vorname = (string)v!, std: true, breite: 90));
                l.Add(F("Klasse", "Klasse", FeldArt.Element, e => ((Schueler)e).KlasseId, (e, v) => ((Schueler)e).KlasseId = (int?)v, "Allgemein", true, 60, ElementTyp.Klasse));
                l.Add(T("Geschlecht", "Geschlecht", e => ((Schueler)e).Geschlecht, (e, v) => ((Schueler)e).Geschlecht = (string)v!));
                l.Add(T("Kurswahl", "Kurswahlen", e => string.Join(",", ((Schueler)e).KurswahlIds.Select(id => doc.Index.Unterrichte.GetValueOrDefault(id)?.Kursname ?? "?")),
                    (e, v) => ((Schueler)e).KurswahlIds = ((string)v!).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => doc.Daten.Unterrichte.FirstOrDefault(u => u.Kursname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Kurs \"{k}\" unbekannt")).ToList(),
                    "Allgemein", true, 260));
                break;
        }
        return l;
    }

    private static List<Ermaessigung> ErmaessigungenParsen(string s) =>
        s.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t =>
        {
            var p = t.Split(':');
            return new Ermaessigung { Grund = p[0].Trim(), Wert = p.Length > 1 ? decimal.Parse(p[1].Trim().Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")) : 0 };
        }).ToList();
}
