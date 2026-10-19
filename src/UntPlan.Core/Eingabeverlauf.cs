namespace UntPlan.Core;

/// <summary>
/// Zuletzt verwendete Eingaben je Feldart (z. B. Vertretungstext, Bemerkung, Absenztext) für die Autovervollständigung:
/// neueste zuerst, ohne Doppelte (Groß-/Kleinschreibung egal), höchstens <see cref="Maximum"/> Einträge je Feld.
/// </summary>
public static class Eingabeverlauf
{
    /// <summary>Höchstzahl gemerkter Einträge je Feldart.</summary>
    public const int Maximum = 30;

    /// <summary>Höchstlänge eines gemerkten Eintrags (längere Texte werden nicht gemerkt).</summary>
    public const int MaxLaenge = 400;

    /// <summary>Eingabe merken (leere und sehr lange Texte werden ignoriert); liefert true, wenn sich die Liste geändert hat.</summary>
    public static bool Merken(IDictionary<string, List<string>> verlauf, string feld, string? text, int maximum = Maximum)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > MaxLaenge || string.IsNullOrWhiteSpace(feld)) return false;
        if (!verlauf.TryGetValue(feld, out var liste)) verlauf[feld] = liste = new List<string>();
        if (liste.Count > 0 && liste[0] == t) return false;
        liste.RemoveAll(x => string.Equals(x, t, StringComparison.CurrentCultureIgnoreCase));
        liste.Insert(0, t);
        if (liste.Count > maximum) liste.RemoveRange(maximum, liste.Count - maximum);
        return true;
    }

    /// <summary>Eintrag aus dem Verlauf entfernen (z. B. Entf in der Vorschlagsliste).</summary>
    public static bool Entfernen(IDictionary<string, List<string>> verlauf, string feld, string text) =>
        verlauf.TryGetValue(feld, out var liste) && liste.RemoveAll(x => string.Equals(x, text, StringComparison.CurrentCultureIgnoreCase)) > 0;

    /// <summary>
    /// Vorschläge zur Eingabe: zuerst Einträge, die mit der Eingabe beginnen, dann solche, die sie enthalten (jeweils neueste
    /// zuerst, Groß-/Kleinschreibung egal). Eine leere Eingabe liefert den ganzen Verlauf; ein genau gleicher Eintrag fehlt.
    /// </summary>
    public static List<string> Vorschlaege(IEnumerable<string>? verlauf, string? eingabe, int maximum = 12)
    {
        var liste = verlauf?.ToList() ?? new List<string>();
        var e = (eingabe ?? "").Trim();
        const StringComparison ci = StringComparison.CurrentCultureIgnoreCase;
        if (e.Length == 0) return liste.Take(maximum).ToList();
        var anfang = liste.Where(x => x.StartsWith(e, ci) && !x.Equals(e, ci));
        var enthalten = liste.Where(x => !x.StartsWith(e, ci) && x.Contains(e, ci));
        return anfang.Concat(enthalten).Take(maximum).ToList();
    }

    /// <summary>Vorschläge für ein Feld aus dem Verlauf aller Felder.</summary>
    public static List<string> Vorschlaege(IReadOnlyDictionary<string, List<string>> verlauf, string feld, string? eingabe, int maximum = 12) =>
        Vorschlaege(verlauf.TryGetValue(feld, out var l) ? l : null, eingabe, maximum);
}
