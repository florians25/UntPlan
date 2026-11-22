using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Schwere eines Prüfbefunds: Fehler verhindern das Speichern, Hinweise werden nur angezeigt.</summary>
public enum Befundart { Fehler, Hinweis }

/// <summary>Ein Prüfbefund zu einer Absenz: betroffenes Feld (Spaltenname der Absenzliste) und Meldungstext.</summary>
public sealed record Absenzbefund(Befundart Art, string Feld, string Text);

/// <summary>
/// Prüfung einer Absenz vor dem Speichern (Absenzliste, Dialog, Einfügen): unbekanntes Element, Bis vor Von, Stunden außerhalb des
/// Zeitrasters bzw. bis Stunde vor ab Stunde am selben Tag, Zeitraum außerhalb des Schuljahres (Fehler) sowie Zeiträume ohne
/// Unterrichtstag (Hinweis). Dazu Überschneidungen mit vorhandenen Absenzen desselben Elements und deren Zusammenfassung.
/// </summary>
public static class Absenzpruefung
{
    /// <summary>Feldnamen (Spalten der Absenzliste).</summary>
    public const string Element = "E", Von = "V", StundeVon = "SV", Bis = "B", StundeBis = "SB";

    /// <summary>Gespeicherter Wert für „bis zur letzten Stunde“.</summary>
    public const int GanzerTag = 99;

    public static int LetzteStunde(Schuldatei d) => Math.Max(1, d.Zeitraster.StundenProTag);

    public static ElementTyp TypVon(AbwesenheitsArt art) => art switch
    {
        AbwesenheitsArt.Lehrer => ElementTyp.Lehrer,
        AbwesenheitsArt.Klasse => ElementTyp.Klasse,
        _ => ElementTyp.Raum,
    };

    private static string TypText(AbwesenheitsArt art) => art switch
    {
        AbwesenheitsArt.Lehrer => "Lehrkraft",
        AbwesenheitsArt.Klasse => "Klasse",
        _ => "Raum",
    };

    /// <summary>
    /// Alle Befunde einer Absenz. Ohne <paramref name="elementPflicht"/> wird ein fehlendes Element nicht gemeldet (Eingabezeile,
    /// in der noch kein Element eingetragen ist).
    /// </summary>
    public static List<Absenzbefund> Pruefen(Schuldatei d, Abwesenheit a, bool elementPflicht = true)
    {
        var liste = new List<Absenzbefund>();
        var letzte = LetzteStunde(d);
        if (a.ElementId == 0)
        {
            if (elementPflicht) liste.Add(new(Befundart.Fehler, Element, $"Bitte zuerst {(a.Art == AbwesenheitsArt.Raum ? "den Raum" : a.Art == AbwesenheitsArt.Klasse ? "die Klasse" : "die Lehrkraft")} eintragen."));
        }
        else if (!d.AlleElemente(TypVon(a.Art)).Any(e => e.Id == a.ElementId))
            liste.Add(new(Befundart.Fehler, Element, $"{TypText(a.Art)} ist nicht (mehr) vorhanden."));
        if (a.Bis < a.Von)
            liste.Add(new(Befundart.Fehler, Bis, $"Der letzte Tag ({a.Bis:dd.MM.}) liegt vor dem ersten Tag ({a.Von:dd.MM.})."));
        if (a.StundeVon < 1 || a.StundeVon > letzte)
            liste.Add(new(Befundart.Fehler, StundeVon, $"Ab Stunde muss zwischen 1 und {letzte} liegen."));
        if (a.StundeBis < 1 || (a.StundeBis > letzte && a.StundeBis != GanzerTag))
            liste.Add(new(Befundart.Fehler, StundeBis, $"Bis Stunde muss zwischen 1 und {letzte} liegen."));
        else if (a.Von == a.Bis && a.StundeBis < a.StundeVon)
            liste.Add(new(Befundart.Fehler, StundeBis, $"Bis Stunde ({a.StundeBis}.) liegt am selben Tag vor ab Stunde ({a.StundeVon}.)."));
        var sv = d.Einstellungen.SchuljahrVon;
        var sb = d.Einstellungen.SchuljahrBis;
        var schuljahr = $"Das Schuljahr dauert vom {sv:dd.MM.yyyy} bis {sb:dd.MM.yyyy}.";
        if (a.Von < sv || a.Von > sb) liste.Add(new(Befundart.Fehler, Von, $"Der erste Tag ({a.Von:dd.MM.yyyy}) liegt außerhalb des Schuljahres. {schuljahr}"));
        if ((a.Bis < sv || a.Bis > sb) && a.Bis >= a.Von) liste.Add(new(Befundart.Fehler, Bis, $"Der letzte Tag ({a.Bis:dd.MM.yyyy}) liegt außerhalb des Schuljahres. {schuljahr}"));
        if (liste.Count == 0)
        {
            var tage = Math.Min(a.Tage, 400);
            var unterricht = Enumerable.Range(0, tage).Any(i => Kalender.IstUnterrichtstag(d, a.Von.AddDays(i)));
            if (!unterricht)
                liste.Add(new(Befundart.Hinweis, Von, a.Von == a.Bis
                    ? $"Am {a.Von:dd.MM.} findet kein Unterricht statt ({FreiGrund(d, a.Von)}). Es entstehen keine Vertretungen."
                    : $"Vom {a.Von:dd.MM.} bis {a.Bis:dd.MM.} findet kein Unterricht statt (Wochenende, Ferien bzw. Feiertage). Es entstehen keine Vertretungen."));
            else if (!Kalender.IstUnterrichtstag(d, a.Von))
                liste.Add(new(Befundart.Hinweis, Von, $"Der erste Tag ({a.Von:dd.MM.}) ist unterrichtsfrei ({FreiGrund(d, a.Von)})."));
        }
        return liste;
    }

    /// <summary>Nur die Fehler (Befunde, die das Speichern verhindern).</summary>
    public static List<Absenzbefund> Fehler(Schuldatei d, Abwesenheit a, bool elementPflicht = true) =>
        Pruefen(d, a, elementPflicht).Where(b => b.Art == Befundart.Fehler).ToList();

    private static string FreiGrund(Schuldatei d, DateOnly tag)
    {
        if (Kalender.TagIndex(d, tag) < 0) return "Wochenende";
        var b = Kalender.FreiBezeichnung(d, tag);
        return b.Length > 0 ? b : "unterrichtsfrei";
    }

    /// <summary>
    /// Stundeneingabe lesen: leer ergibt die Vorgabe (ab: 1, bis: letzte Stunde). Liefert false mit Meldung bei Text, der keine
    /// Zahl ist, bzw. bei einer Stunde außerhalb des Zeitrasters.
    /// </summary>
    public static bool StundeLesen(string text, int letzteStunde, bool bisStunde, out int stunde, out string? fehler)
    {
        letzteStunde = Math.Max(1, letzteStunde);
        text = (text ?? "").Trim().TrimEnd('.');
        fehler = null;
        if (text.Length == 0)
        {
            stunde = bisStunde ? letzteStunde : 1;
            return true;
        }
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out stunde))
        {
            fehler = $"„{text}“ ist keine Stunde. Bitte eine Zahl von 1 bis {letzteStunde} eingeben.";
            return false;
        }
        if (stunde < 1 || stunde > letzteStunde)
        {
            fehler = $"Stunde {stunde} gibt es nicht. Bitte eine Zahl von 1 bis {letzteStunde} eingeben.";
            return false;
        }
        return true;
    }

    private static (int Tag, int Stunde) Anfang(Abwesenheit a) => (a.Von.DayNumber, a.StundeVon);
    private static (int Tag, int Stunde) Ende(Abwesenheit a) => (a.Bis.DayNumber, Math.Min(a.StundeBis, GanzerTag));

    private static int Vergleich((int Tag, int Stunde) x, (int Tag, int Stunde) y) => x.Tag != y.Tag ? x.Tag.CompareTo(y.Tag) : x.Stunde.CompareTo(y.Stunde);

    /// <summary>Überschneiden sich zwei Absenzen zeitlich (mindestens eine gemeinsame Stunde)?</summary>
    public static bool Ueberschneiden(Abwesenheit x, Abwesenheit y) =>
        Vergleich(Anfang(x), Ende(y)) <= 0 && Vergleich(Anfang(y), Ende(x)) <= 0;

    /// <summary>Liegt <paramref name="innen"/> vollständig in <paramref name="aussen"/>?</summary>
    public static bool Enthalten(Abwesenheit innen, Abwesenheit aussen) =>
        Vergleich(Anfang(aussen), Anfang(innen)) <= 0 && Vergleich(Ende(innen), Ende(aussen)) <= 0;

    /// <summary>
    /// Vorhandene Absenzen desselben Elements (gleiche Art), die sich mit <paramref name="a"/> überschneiden – ohne die Absenz selbst
    /// und ohne die Absenzen mit den Nummern in <paramref name="ausgenommen"/> (z. B. die gerade ersetzten). Sortiert nach Beginn.
    /// </summary>
    public static List<Abwesenheit> Ueberschneidungen(Schuldatei d, Abwesenheit a, IEnumerable<int>? ausgenommen = null)
    {
        if (a.ElementId == 0) return new();
        var ohne = (ausgenommen ?? Array.Empty<int>()).ToHashSet();
        return d.Abwesenheiten
            .Where(b => b.Art == a.Art && b.ElementId == a.ElementId && (a.Id == 0 || b.Id != a.Id) && !ohne.Contains(b.Id) && Ueberschneiden(a, b))
            .OrderBy(b => b.Von).ThenBy(b => b.StundeVon).ToList();
    }

    /// <summary>
    /// Fasst Absenzen desselben Elements zu einer zusammen: Nummer, Grund und Text der <paramref name="basis"/> (fehlender Grund bzw.
    /// Text aus den weiteren), Zeitraum vom frühesten Beginn bis zum spätesten Ende.
    /// </summary>
    public static Abwesenheit Zusammenfassen(Abwesenheit basis, IEnumerable<Abwesenheit> weitere)
    {
        var z = basis.Kopie();
        foreach (var b in weitere)
        {
            if (Vergleich(Anfang(b), Anfang(z)) < 0) { z.Von = b.Von; z.StundeVon = b.StundeVon; }
            if (Vergleich(Ende(b), Ende(z)) > 0) { z.Bis = b.Bis; z.StundeBis = b.StundeBis; }
            z.GrundId ??= b.GrundId;
            if (z.Text.Length == 0) z.Text = b.Text;
            foreach (var l in b.BegleitLehrerIds) if (!z.BegleitLehrerIds.Contains(l)) z.BegleitLehrerIds.Add(l);
        }
        return z;
    }

    /// <summary>Zeitraum einer Absenz als kurzer Text („12.11. 3. Std. bis 14.11.“).</summary>
    public static string Zeitraum(Abwesenheit a, int letzteStunde)
    {
        var ab = a.StundeVon > 1 ? $" ab {a.StundeVon}. Std." : "";
        var bis = a.StundeBis < Math.Max(1, letzteStunde) ? $" bis {a.StundeBis}. Std." : "";
        return a.Von == a.Bis ? $"{a.Von:dd.MM.}{ab}{bis}" : $"{a.Von:dd.MM.}{ab} bis {a.Bis:dd.MM.}{bis}";
    }
}
