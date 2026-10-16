using System.Globalization;
using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>Ergebnis der Prüfung einer Eingabe: gültig mit (normalisiertem) Wert oder ungültig mit verständlichem Hinweis.</summary>
public sealed record Eingabepruefung(bool Gueltig, string Wert, string Hinweis)
{
    public static Eingabepruefung Ok(string wert, string hinweis = "") => new(true, wert, hinweis);
    public static Eingabepruefung Fehler(string hinweis) => new(false, "", hinweis);
}

/// <summary>
/// Sofortige Prüfung der Unterrichtseingabe (Unterrichtsfenster der Stundenplanung): Kürzel von Fach, Lehrer, Klassen und Räumen
/// werden gegen die Stammdaten geprüft (Groß-/Kleinschreibung egal, eindeutige Anfänge werden ergänzt), Zahlen und Bereiche
/// auf ihr Format. Ungültige Eingaben liefern einen Hinweis mit Vorschlägen statt einer Fehlermeldung.
/// </summary>
public static class Unterrichtseingabe
{
    private const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    /// <summary>Prüft den Text für eine Spalte des Unterrichtsfensters (Schlüssel wie Fach, Lehrer, Klassen, Fachraum, Stammraum, Wst, Dopp, Block …).</summary>
    public static Eingabepruefung Pruefen(Schuldatei d, string feld, string text)
    {
        var t = (text ?? "").Trim();
        switch (feld)
        {
            case "Fach": return t.Length == 0 ? Eingabepruefung.Ok("") : Element(d.Faecher, t, "Fach");
            case "Lehrer": return t.Length == 0 ? Eingabepruefung.Ok("") : Element(d.Lehrer, t, "Lehrer");
            case "Klassen": return Klassen(d, t);
            case "Fachraum" or "Stammraum":
            {
                if (t.Length == 0) return Eingabepruefung.Ok("");
                var raeume = d.Raeume.Cast<Element>().Concat(d.Raumgruppen).ToList();
                return Element(raeume, t, "Raum oder Raumgruppe");
            }
            case "Wst":
                if (!int.TryParse(t, out var wst) || wst < 0 || wst > 60) return Eingabepruefung.Fehler($"Wochenstunden: bitte eine ganze Zahl von 0 bis 60 eingeben (statt „{t}“).");
                return Eingabepruefung.Ok(wst.ToString());
            case "Dopp":
                if (t.Length == 0) return Eingabepruefung.Ok("");
                return Bereich.TryParse(t, out var b) ? Eingabepruefung.Ok(b.ToString()) : Eingabepruefung.Fehler($"Doppelstunden: Bereich wie „1-1“ (genau eine) oder „0-2“ eingeben (statt „{t}“).");
            case "Block":
            {
                if (t.Length == 0) return Eingabepruefung.Ok("");
                var teile = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (teile.Any(x => !int.TryParse(x, out var n) || n < 2 || n > 10))
                    return Eingabepruefung.Fehler($"Block: Blockgrößen von 2 bis 10, mehrere durch Komma getrennt (z. B. „3“ oder „3,3“) – „{t}“ ist ungültig.");
                return Eingabepruefung.Ok(string.Join(",", teile));
            }
            case "Von" or "Bis":
                if (t.Length == 0) return Eingabepruefung.Ok("");
                return DateOnly.TryParse(t, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var dt)
                    ? Eingabepruefung.Ok(dt.ToString("dd.MM.yyyy")) : Eingabepruefung.Fehler($"Datum bitte als TT.MM.JJJJ eingeben (statt „{t}“).");
            case "Wert" or "Faktor":
                if (t.Length == 0) return Eingabepruefung.Ok("");
                return decimal.TryParse(t.Replace('.', ','), NumberStyles.Number, CultureInfo.GetCultureInfo("de-DE"), out _)
                    ? Eingabepruefung.Ok(t) : Eingabepruefung.Fehler($"Bitte eine Zahl eingeben, z. B. 0,5 (statt „{t}“).");
            case "UGruppe" or "ZUGruppe":
                if (t.Length == 0) return Eingabepruefung.Ok("");
                return Element(d.UGruppen.Select(g => new Hilfselement(g.Kurzname, g.Langname)).ToList(), t, "Unterrichtsgruppe");
            default:
                return Eingabepruefung.Ok(t);
        }
    }

    /// <summary>Hinweise zu fehlenden Pflichtangaben eines Unterrichts (Fach, Klasse, Wochenstunden; Lehrer als Hinweis).</summary>
    private static readonly System.Text.RegularExpressions.Regex OhneKlasseFach = new(
        @"aufsicht|bereitschaft|sprechstunde|konferenz|verwaltung|ermäßig|ermaessig|entlastung|koordination|beratung|teamsitzung|dienstbesprechung",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Braucht dieser Unterricht eine Klasse? Nein, wenn schon eine Kopplungszeile Klassen hat oder es ein Dienst ohne Klasse ist:
    /// Bereitschaft, Fach ohne Vertretung (Aufsicht, Sprechstunde, Konferenz …) bzw. ein Fach mit entsprechendem Namen.
    /// </summary>
    public static bool KlasseNoetig(Schuldatei? d, Unterricht u)
    {
        if (u.Zeilen.Any(z => z.KlassenIds.Count > 0)) return false;
        if (d is null) return true;
        foreach (var z in u.Zeilen)
        {
            if (z.FachId is not int fid || d.Faecher.FirstOrDefault(f => f.Id == fid) is not { } fach) continue;
            if (fach.Bereitschaft || fach.KeineVertretung || OhneKlasseFach.IsMatch(fach.Kurzname) || OhneKlasseFach.IsMatch(fach.Langname)) return false;
        }
        return true;
    }

    public static List<string> Pflichthinweise(Unterricht u, Schuldatei? d = null)
    {
        var res = new List<string>();
        if (u.Wochenstunden <= 0) res.Add("Wochenstunden fehlen (Wst = 0) – der Unterricht kann nicht verplant werden.");
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            var wo = u.Zeilen.Count > 1 ? $" (Kopplungszeile {i + 1})" : "";
            if (z.FachId is null) res.Add($"Fach fehlt{wo}.");
            if (i == 0 && KlasseNoetig(d, u)) res.Add($"Klasse fehlt{wo}.");
            if (z.LehrerId is null) res.Add($"Noch kein Lehrer{wo} – kann später ergänzt werden.");
        }
        return res;
    }

    /// <summary>Fehlt eine Pflichtangabe (Fach bzw. Klasse) in dieser Kopplungszeile?</summary>
    public static bool PflichtFehlt(Unterricht u, int zeile, string feld, Schuldatei? d = null)
    {
        if (zeile < 0 || zeile >= u.Zeilen.Count) return false;
        var z = u.Zeilen[zeile];
        return feld switch
        {
            "Fach" => z.FachId is null,
            "Klassen" => zeile == 0 && KlasseNoetig(d, u),
            "Wst" => zeile == 0 && u.Wochenstunden <= 0,
            _ => false,
        };
    }

    private static Eingabepruefung Klassen(Schuldatei d, string t)
    {
        if (t.Length == 0) return Eingabepruefung.Ok("");
        var teile = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var werte = new List<string>();
        var ergaenzt = new List<string>();
        foreach (var k in teile)
        {
            var p = Element(d.Klassen, k, "Klasse");
            if (!p.Gueltig) return p;
            if (!werte.Contains(p.Wert)) werte.Add(p.Wert);
            if (p.Hinweis.Length > 0) ergaenzt.Add(p.Hinweis);
        }
        return Eingabepruefung.Ok(string.Join(",", werte), string.Join(" ", ergaenzt));
    }

    private sealed class Hilfselement : Element
    {
        public Hilfselement(string kurz, string lang)
        {
            Kurzname = kurz;
            Langname = lang;
        }

        public override ElementTyp Typ => ElementTyp.Unterricht;
    }

    private static string Lang(Element e) => e is Lehrer l ? l.VollerName : e.Langname;

    private static Eingabepruefung Element<T>(IReadOnlyCollection<T> liste, string t, string art) where T : Element
    {
        var exakt = liste.FirstOrDefault(x => x.Kurzname.Equals(t, Ci));
        if (exakt != null) return Eingabepruefung.Ok(exakt.Kurzname);
        // eindeutiger Anfang des Kurznamens bzw. Langnamens wird ergänzt
        var anfang = liste.Where(x => x.Kurzname.StartsWith(t, Ci)).ToList();
        if (anfang.Count == 0) anfang = liste.Where(x => Lang(x).Length > 0 && Lang(x).StartsWith(t, Ci)).ToList();
        if (anfang.Count == 1)
            return Eingabepruefung.Ok(anfang[0].Kurzname, $"„{t}“ wurde zu {anfang[0].Kurzname}{(Lang(anfang[0]).Length > 0 ? $" ({Lang(anfang[0])})" : "")} ergänzt.");
        var vorschlaege = (anfang.Count > 1 ? anfang : liste.Where(x => x.Kurzname.Contains(t, Ci) || Lang(x).Contains(t, Ci)).ToList())
            .Take(5).Select(x => x.Kurzname + (Lang(x).Length > 0 ? $" ({Lang(x)})" : "")).ToList();
        if (anfang.Count > 1)
            return Eingabepruefung.Fehler($"{art} „{t}“ ist nicht eindeutig: {string.Join(", ", vorschlaege)}{(anfang.Count > 5 ? " …" : "")}. Bitte genauer eingeben oder aus der Liste wählen.");
        return Eingabepruefung.Fehler($"{art} „{t}“ ist nicht vorhanden." + (vorschlaege.Count > 0 ? $" Meinten Sie: {string.Join(", ", vorschlaege)}?" : " Bitte aus der Liste wählen (Alt+↓) oder zuerst in den Stammdaten anlegen."));
    }
}
