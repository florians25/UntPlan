using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>NRW-Vorgaben (Schulformen, Pflichtstunden, Jahrgänge, Ferien, Feiertage, Fächerkatalog). Alle Werte sind änderbare Voreinstellungen.</summary>
public static class Nrw
{
    public static readonly IReadOnlyDictionary<string, string> Schulformen = new Dictionary<string, string>
    {
        ["GS"] = "Grundschule", ["HS"] = "Hauptschule", ["RS"] = "Realschule", ["SK"] = "Sekundarschule", ["GE"] = "Gesamtschule",
        ["GY"] = "Gymnasium", ["BK"] = "Berufskolleg", ["FS"] = "Förderschule", ["PS"] = "PRIMUS-Schule", ["WB"] = "Weiterbildungskolleg",
    };

    /// <summary>Wöchentliche Pflichtstunden einer Vollzeitlehrkraft (VO zu § 93 Abs. 2 SchulG).</summary>
    public static readonly IReadOnlyDictionary<string, decimal> Pflichtstunden = new Dictionary<string, decimal>
    {
        ["GS"] = 28, ["HS"] = 28, ["RS"] = 28, ["SK"] = 25.5m, ["GE"] = 25.5m, ["GY"] = 25.5m, ["BK"] = 25.5m, ["FS"] = 27.5m, ["PS"] = 25.5m, ["WB"] = 25,
    };

    public static readonly IReadOnlyDictionary<string, string[]> Jahrgaenge = new Dictionary<string, string[]>
    {
        ["GS"] = new[] { "1", "2", "3", "4" },
        ["HS"] = new[] { "5", "6", "7", "8", "9", "10" },
        ["RS"] = new[] { "5", "6", "7", "8", "9", "10" },
        ["SK"] = new[] { "5", "6", "7", "8", "9", "10" },
        ["GE"] = new[] { "5", "6", "7", "8", "9", "10", "EF", "Q1", "Q2" },
        ["GY"] = new[] { "5", "6", "7", "8", "9", "10", "EF", "Q1", "Q2" },
        ["BK"] = new[] { "Unterstufe", "Mittelstufe", "Oberstufe", "FOS 11", "FOS 12", "BG 11", "BG 12", "BG 13" },
        ["FS"] = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" },
        ["PS"] = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" },
        ["WB"] = new[] { "E", "Q1", "Q2" },
    };

    public static int JahrgangRang(string j) => int.TryParse(j, out var n) ? n : j switch { "EF" or "E" => 11, "Q1" => 12, "Q2" => 13, _ => 99 };

    /// <summary>Kursarten der gymnasialen Oberstufe (APO-GOSt) mit Regel-Wochenstunden.</summary>
    public static readonly IReadOnlyDictionary<string, (string Name, int Stunden)> Kursarten = new Dictionary<string, (string, int)>
    {
        ["GK"] = ("Grundkurs", 3), ["LK"] = ("Leistungskurs", 5), ["ZK"] = ("Zusatzkurs", 3), ["VTF"] = ("Vertiefungskurs", 2), ["PJK"] = ("Projektkurs", 2),
    };

    public static readonly (string K, string Name, uint Farbe, bool Haupt, string? Raumart)[] Faecherkatalog =
    {
        ("D", "Deutsch", 0xFFF28B82, true, null), ("M", "Mathematik", 0xFF7BAAF7, true, null), ("E", "Englisch", 0xFF81C995, true, null),
        ("F", "Französisch", 0xFF4DB6AC, true, null), ("L", "Latein", 0xFFBCAAA4, true, null), ("S", "Spanisch", 0xFFFFB74D, true, null),
        ("BI", "Biologie", 0xFFAED581, false, "Fachraum"), ("CH", "Chemie", 0xFFCE93D8, false, "Fachraum"), ("PH", "Physik", 0xFF9FA8DA, false, "Fachraum"),
        ("IF", "Informatik", 0xFF4FC3F7, false, "EDV"), ("EK", "Erdkunde", 0xFFDCE775, false, null), ("GE", "Geschichte", 0xFFD7CCC8, false, null),
        ("PK", "Politik", 0xFFFFD54F, false, null), ("WI", "Wirtschaft-Politik", 0xFFFFCA28, false, null), ("SW", "Sozialwissenschaften", 0xFFFFE082, false, null),
        ("PL", "Philosophie", 0xFFB0BEC5, false, null), ("PPL", "Praktische Philosophie", 0xFFCFD8DC, false, null), ("KR", "Kath. Religionslehre", 0xFFE1BEE7, false, null),
        ("ER", "Ev. Religionslehre", 0xFFD1C4E9, false, null), ("KU", "Kunst", 0xFFF48FB1, false, "Kunstraum"), ("MU", "Musik", 0xFFF06292, false, "Musikraum"),
        ("SP", "Sport", 0xFF90A4AE, false, "Sporthalle"), ("NW", "Naturwissenschaften", 0xFFA5D6A7, false, "Fachraum"), ("GL", "Gesellschaftslehre", 0xFFE6EE9C, false, null),
        ("SU", "Sachunterricht", 0xFFC5E1A5, false, null), ("KL", "Klassenleitungsstunde", 0xFFEEEEEE, false, null), ("FÖ", "Förderunterricht", 0xFFE0E0E0, false, null),
        ("BER", "Bereitschaft", 0xFFFFF59D, false, null),
    };

    /// <summary>Ferienordnung NRW (Schuljahre 2025/26 und 2026/27, Sommer 2027).</summary>
    public static readonly (string Name, DateOnly Von, DateOnly Bis)[] FerienNrw =
    {
        ("Herbstferien", new(2025, 10, 13), new(2025, 10, 25)), ("Weihnachtsferien", new(2025, 12, 22), new(2026, 1, 6)),
        ("Osterferien", new(2026, 3, 30), new(2026, 4, 11)), ("Pfingstferien", new(2026, 5, 26), new(2026, 5, 26)),
        ("Sommerferien", new(2026, 7, 20), new(2026, 9, 1)), ("Herbstferien", new(2026, 10, 17), new(2026, 10, 31)),
        ("Weihnachtsferien", new(2026, 12, 23), new(2027, 1, 6)), ("Osterferien", new(2027, 3, 22), new(2027, 4, 3)),
        ("Pfingstferien", new(2027, 5, 18), new(2027, 5, 18)), ("Sommerferien", new(2027, 7, 19), new(2027, 8, 31)),
    };

    public static DateOnly Ostersonntag(int jahr)
    {
        var a = jahr % 19;
        var b = jahr / 100;
        var c = jahr % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var monat = (h + l - 7 * m + 114) / 31;
        var tag = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(jahr, monat, tag);
    }

    /// <summary>Gesetzliche Feiertage in Nordrhein-Westfalen.</summary>
    public static IEnumerable<(string Name, DateOnly Datum)> Feiertage(int jahr)
    {
        var o = Ostersonntag(jahr);
        yield return ("Neujahr", new DateOnly(jahr, 1, 1));
        yield return ("Karfreitag", o.AddDays(-2));
        yield return ("Ostermontag", o.AddDays(1));
        yield return ("Tag der Arbeit", new DateOnly(jahr, 5, 1));
        yield return ("Christi Himmelfahrt", o.AddDays(39));
        yield return ("Pfingstmontag", o.AddDays(50));
        yield return ("Fronleichnam", o.AddDays(60));
        yield return ("Tag der Deutschen Einheit", new DateOnly(jahr, 10, 3));
        yield return ("Allerheiligen", new DateOnly(jahr, 11, 1));
        yield return ("1. Weihnachtstag", new DateOnly(jahr, 12, 25));
        yield return ("2. Weihnachtstag", new DateOnly(jahr, 12, 26));
    }

    /// <summary>Trägt Ferien und Feiertage NRW für das Schuljahr ein (ohne Duplikate).</summary>
    public static void FerienEintragen(Schuldatei d)
    {
        var von = d.Einstellungen.SchuljahrVon.AddMonths(-2);
        var bis = d.Einstellungen.SchuljahrBis.AddMonths(2);
        foreach (var (name, v, b) in FerienNrw.Where(f => f.Bis >= von && f.Von <= bis))
            if (!d.Ferien.Any(f => f.Von == v && f.Langname == name))
                d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = name[..Math.Min(4, name.Length)], Langname = name, Von = v, Bis = b });
        for (var j = von.Year; j <= bis.Year; j++)
            foreach (var (name, dt) in Feiertage(j).Where(f => f.Datum >= von && f.Datum <= bis))
                if (!d.Ferien.Any(f => f.Von == dt))
                    d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = name.Length > 6 ? name[..6] : name, Langname = name, Von = dt, Bis = dt, Feiertag = true });
        d.Ferien.Sort((a, b) => a.Von.CompareTo(b.Von));
    }
}

/// <summary>Kalenderfunktionen: Unterrichtstage, Wochentag-Index, Schulwochen (A/B).</summary>
public static class Kalender
{
    public static Ferien? FerienAm(Schuldatei d, DateOnly datum) => d.Ferien.FirstOrDefault(f => datum >= f.Von && datum <= f.Bis);

    /// <summary>Tagesindex im Zeitraster (0 = erster Schultag) oder -1.</summary>
    public static int TagIndex(Schuldatei d, DateOnly datum)
    {
        var wd = ((int)datum.DayOfWeek + 6) % 7; // Montag = 0
        var idx = (wd - d.Zeitraster.ErsterSchultag + 7) % 7;
        return idx < d.Zeitraster.TageProWoche ? idx : -1;
    }

    public static bool IstUnterrichtstag(Schuldatei d, DateOnly datum) =>
        TagIndex(d, datum) >= 0 && FerienAm(d, datum) is null && datum >= d.Einstellungen.SchuljahrVon.AddDays(-7) && datum <= d.Einstellungen.SchuljahrBis.AddDays(7);

    public static DateOnly Montag(DateOnly datum) => datum.AddDays(-(((int)datum.DayOfWeek + 6) % 7));

    /// <summary>Schulwoche seit Schuljahresbeginn (1-basiert; ungerade = A-Woche).</summary>
    public static int Schulwoche(Schuldatei d, DateOnly datum)
    {
        var start = Montag(d.Einstellungen.SchuljahrVon);
        var w = (Montag(datum).DayNumber - start.DayNumber) / 7 + 1;
        return Math.Max(1, w);
    }

    public static string WochenTyp(Schuldatei d, DateOnly datum) => Schulwoche(d, datum) % 2 == 1 ? "A" : "B";

    public static DateOnly NaechsterUnterrichtstag(Schuldatei d, DateOnly datum, int richtung = 1)
    {
        var x = datum.AddDays(richtung);
        for (var i = 0; i < 60 && !IstUnterrichtstag(d, x); i++) x = x.AddDays(richtung);
        return x;
    }

    public static IEnumerable<DateOnly> Tage(DateOnly von, DateOnly bis)
    {
        for (var x = von; x <= bis; x = x.AddDays(1)) yield return x;
    }

    /// <summary>Anzahl Unterrichtswochen im Schuljahr (für die Wertrechnung).</summary>
    public static int Unterrichtswochen(Schuldatei d) =>
        Tage(d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis).Count(t => IstUnterrichtstag(d, t)) / Math.Max(1, d.Zeitraster.TageProWoche);
}
