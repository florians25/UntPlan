namespace UntPlan.Core.Model;

/// <summary>Druckname für Lehrer (Einstellungen | Berichte | Drucknamen).</summary>
public enum LehrerDruckname
{
    Kurzname,
    Langname,
    /// <summary>„Lehrer Langn.+Vor.“ – Langname, Vorname.</summary>
    LangnameVorname,
    /// <summary>„Lehrer Vor.+Langn.“ – Vorname Langname.</summary>
    VornameLangname,
    /// <summary>„Lehrer Tit.+Langn.,Vor.“ – Titel Langname Vorname.</summary>
    TitelLangnameVorname,
    /// <summary>„Lehrer Tit.+Vor.+Langn.“.</summary>
    TitelVornameLangname,
}

/// <summary>Druckname für Studenten.</summary>
public enum SchuelerDruckname { Kurzname, LangnameVorname, VornameLangname }

/// <summary>Einstellungen | Berichte | Überschriften: Kopf- und Fußzeilen der Ausdrucke.</summary>
public sealed class BerichtEinstellungen
{
    /// <summary>Überschrift (zwei Zeilen; leer = Schulname bzw. „Stundenplan Schuljahr“).</summary>
    public string Ueberschrift1 { get; set; } = "";
    public string Ueberschrift2 { get; set; } = "";
    public bool DatumDrucken { get; set; } = true;
    public bool UhrzeitDrucken { get; set; } = true;
    public bool VersionDrucken { get; set; }
    public bool WaagrechteLinie { get; set; } = true;
    public bool SeitenNummer { get; set; } = true;
    public bool AnzahlSeiten { get; set; } = true;
    /// <summary>Fußzeile: freier Text (leer = Programmname).</summary>
    public string Fusszeile { get; set; } = "";
    public bool FussDateiname { get; set; }
    public bool FussDatum { get; set; }
    public bool FussSchulwoche { get; set; }
    public bool FussKalenderwoche { get; set; }
    public bool FussWochennummer { get; set; }
    public bool FussPeriode { get; set; } = true;
    public bool FussLinie { get; set; } = true;
    public LehrerDruckname LehrerDruckname { get; set; } = LehrerDruckname.Kurzname;
    public SchuelerDruckname SchuelerDruckname { get; set; } = SchuelerDruckname.Kurzname;
}

/// <summary>Einstellungen | Diverses | HTML: Navigation, Fußzeile und Tag-Eigenschaften der HTML-Stundenpläne.</summary>
public sealed class HtmlEinstellungen
{
    public bool SchaltflaechenOben { get; set; }
    public bool SchaltflaechenUnten { get; set; } = true;
    /// <summary>Encoding UTF-8 (sonst ISO-8859-1).</summary>
    public bool Utf8 { get; set; }
    /// <summary>Fußzeile: 0 = keine, 1 = unter Plan/Tabelle, 2 = am Seitenende.</summary>
    public int Fusszeile { get; set; } = 2;
    /// <summary>Eigenschaften für Tag &lt;Body&gt; bzw. &lt;Table&gt; (z. B. bgcolor="#ffffff").</summary>
    public string BodyEigenschaften { get; set; } = "";
    public string TableEigenschaften { get; set; } = "";
}

/// <summary>Feld der Überschrift eines Einzelplans im Druck (Überschrift-Editor im Seitenlayout).</summary>
public enum UeberschriftFeldArt
{
    Kurzname, Langname, Text, Beschreibung, Abteilung, Klassenlehrer, Stammraum, Zeitbereich, PeriodeKurz, PeriodeLang, FixerText,
}

public sealed class UeberschriftFeld
{
    public UeberschriftFeldArt Art { get; set; }
    /// <summary>Stellen maximal (0 = unbegrenzt).</summary>
    public int StellenMax { get; set; } = 20;
    /// <summary>Schriftgröße in Prozent (50–500 %).</summary>
    public int SchriftProzent { get; set; } = 100;
    public bool Fett { get; set; }
    public bool Kursiv { get; set; }
    /// <summary>Text des Feldes „Fixer (konstanter) Text“.</summary>
    public string Text { get; set; } = "";
    /// <summary>Neue Zeile beginnen (sonst rechts neben dem vorigen Feld).</summary>
    public bool NeueZeile { get; set; }

    public static string ArtText(UeberschriftFeldArt a) => a switch
    {
        UeberschriftFeldArt.Kurzname => "Kurzname",
        UeberschriftFeldArt.Langname => "Langname",
        UeberschriftFeldArt.Text => "Text",
        UeberschriftFeldArt.Beschreibung => "Beschreibung",
        UeberschriftFeldArt.Abteilung => "Abteilung",
        UeberschriftFeldArt.Klassenlehrer => "Klassenlehrer",
        UeberschriftFeldArt.Stammraum => "Stammraum",
        UeberschriftFeldArt.Zeitbereich => "Zeitbereich",
        UeberschriftFeldArt.PeriodeKurz => "Periode (Kurzname)",
        UeberschriftFeldArt.PeriodeLang => "Periode (Langname)",
        _ => "Fixer (konstanter) Text",
    };

    public static List<UeberschriftFeld> Standard() => new()
    {
        new UeberschriftFeld { Art = UeberschriftFeldArt.Kurzname, StellenMax = 8, SchriftProzent = 150 },
        new UeberschriftFeld { Art = UeberschriftFeldArt.Langname, StellenMax = 30, SchriftProzent = 100 },
    };
}

/// <summary>Druckdetails eines Stundenplanformats (Seitenlayout: Einstellungen, Hintergrund, Zusatzinformationen).</summary>
public sealed class StundenplanDruckdetails
{
    // Einzelpläne (Formate 1 und 10)
    public int PlaeneNebeneinander { get; set; } = 1;
    public int PlaeneUebereinander { get; set; } = 1;
    public bool JedeWocheEinPlan { get; set; }
    public bool JeElementNeueSeite { get; set; }
    public int AnzahlWochen { get; set; }
    /// <summary>Schulwochen, z. B. „1-4,7“ (leer = Wochen laut Stundenplan-Einstellungen).</summary>
    public string Schulwoche { get; set; } = "";
    // Zusatzinformationen
    /// <summary>Klassenplan: Lehrer der Klasse; Lehrerplan: Klassen des Lehrers (jeweils mit Fächern).</summary>
    public bool ZusatzKlassenLehrer { get; set; }
    /// <summary>Lehrerplan: Stundenplan der Klasse des Klassenlehrers; Klassenplan: Stundenplan des Klassenlehrers.</summary>
    public bool StundenplanKlassenlehrer { get; set; }
    // Hintergrund-Bild
    public string Hintergrundbild { get; set; } = "";
    /// <summary>Lage des Bildes relativ zur Seite (0..1).</summary>
    public float BildX { get; set; } = 0.82f;
    public float BildY { get; set; } = 0.02f;
    public float BildBreite { get; set; } = 0.12f;
    // Überschrift für den Druck von Einzelplänen
    public List<UeberschriftFeld> Ueberschrift { get; set; } = new();
    // Übersichtspläne (Formate 11, 20, 30)
    public int ZeilenProBlatt { get; set; } = 8;
    public int SpaltenProBlatt { get; set; } = 24;
    public bool ZeilenSpaltenAutomatisch { get; set; } = true;
    public bool NurGanzeTage { get; set; } = true;
    public bool NurEinzeltage { get; set; }
    public bool MehrereBloecke { get; set; }
    /// <summary>Zwischenüberschriften nach jeder X. Zeile (0 = keine).</summary>
    public int ZwischenueberschriftNach { get; set; }
    public string Seitenueberschrift { get; set; } = "";
}

/// <summary>Drucknamen (Einstellungen | Berichte | Drucknamen): Kurznamen in Berichten und Stundenplänen ersetzen.</summary>
public static class Drucknamen
{
    public static string Text(LehrerDruckname a) => a switch
    {
        LehrerDruckname.Langname => "Lehrer Langname",
        LehrerDruckname.LangnameVorname => "Lehrer Langn.+Vor.",
        LehrerDruckname.VornameLangname => "Lehrer Vor.+Langn.",
        LehrerDruckname.TitelLangnameVorname => "Lehrer Tit.+Langn.,Vor.",
        LehrerDruckname.TitelVornameLangname => "Lehrer Tit.+Vor.+Langn.",
        _ => "Lehrer Kurzname",
    };

    public static string Text(SchuelerDruckname a) => a switch
    {
        SchuelerDruckname.LangnameVorname => "Student Langn.+Vor.",
        SchuelerDruckname.VornameLangname => "Student Vor.+Langn.",
        _ => "Student Kurzname",
    };

    public static string Lehrer(Lehrer? l, LehrerDruckname art)
    {
        if (l is null) return "";
        static string Zus(params string[] t) => string.Join(' ', t.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        var name = art switch
        {
            LehrerDruckname.Langname => l.Langname,
            LehrerDruckname.LangnameVorname => l.Vorname.Length > 0 ? $"{l.Langname}, {l.Vorname}" : l.Langname,
            LehrerDruckname.VornameLangname => Zus(l.Vorname, l.Langname),
            LehrerDruckname.TitelLangnameVorname => Zus(l.Titel, l.Langname, l.Vorname),
            LehrerDruckname.TitelVornameLangname => Zus(l.Titel, l.Vorname, l.Langname),
            _ => l.Kurzname,
        };
        return string.IsNullOrWhiteSpace(name) ? l.Kurzname : name;
    }

    public static string Schueler(Schueler? s, SchuelerDruckname art)
    {
        if (s is null) return "";
        var name = art switch
        {
            SchuelerDruckname.LangnameVorname => s.Vorname.Length > 0 ? $"{s.Langname}, {s.Vorname}" : s.Langname,
            SchuelerDruckname.VornameLangname => $"{s.Vorname} {s.Langname}".Trim(),
            _ => s.Kurzname,
        };
        return string.IsNullOrWhiteSpace(name) ? s.Kurzname : name;
    }
}

/// <summary>Ein Blatt eines Übersichtsplans: Elemente (Zeilen) und Tagesblöcke (je Block aufeinanderfolgende Tage nebeneinander).</summary>
public sealed record UebersichtBlatt(int ErstesElement, int AnzahlElemente, IReadOnlyList<(int VonTag, int BisTag)> Bloecke);

/// <summary>Seitenaufteilung der Stundenplandrucke (Mehrere Pläne pro Seite, Details Übersichtspläne).</summary>
public static class Druckseiten
{
    /// <summary>Pläne je Seite (nebeneinander × übereinander, je 1–6).</summary>
    public static int PlaeneJeSeite(StundenplanDruckdetails d) => Math.Clamp(d.PlaeneNebeneinander, 1, 6) * Math.Clamp(d.PlaeneUebereinander, 1, 6);

    /// <summary>
    /// Druckaufträge der Einzelpläne: je Element ein Plan bzw. bei „Für jede Woche ein Stundenplan“ je Element und Woche einer.
    /// Ergebnis: Seiten mit (Element, Woche) – bei „Je Element neue Seite“ beginnt jedes Element auf einer neuen Seite.
    /// </summary>
    public static List<List<(int ElementId, int? Woche)>> Einzelplaene(IReadOnlyList<int> ids, StundenplanDruckdetails d, IReadOnlyList<int>? wochen = null)
    {
        var jeSeite = PlaeneJeSeite(d);
        var seiten = new List<List<(int, int?)>>();
        var aktuell = new List<(int, int?)>();
        foreach (var id in ids)
        {
            var plaene = d.JedeWocheEinPlan && wochen is { Count: > 0 } ? wochen.Select(w => (id, (int?)w)).ToList() : new List<(int, int?)> { (id, null) };
            if (d.JeElementNeueSeite && aktuell.Count > 0) { seiten.Add(aktuell); aktuell = new(); }
            foreach (var p in plaene)
            {
                aktuell.Add(p);
                if (aktuell.Count == jeSeite) { seiten.Add(aktuell); aktuell = new(); }
            }
        }
        if (aktuell.Count > 0) seiten.Add(aktuell);
        return seiten;
    }

    /// <summary>
    /// Wochen für „Für jede Woche ein Stundenplan“: aus dem Feld „Schulwoche“ (z. B. „1-4,7“), sonst die <paramref name="anzahlWochen"/>
    /// Wochen ab <paramref name="ersteWoche"/> (mindestens eine).
    /// </summary>
    public static List<int> Wochen(StundenplanDruckdetails d, int ersteWoche)
    {
        var liste = StundenplanFormat.StundenListe(d.Schulwoche).Where(w => w > 0).OrderBy(w => w).ToList();
        if (liste.Count > 0) return liste;
        return Enumerable.Range(ersteWoche, Math.Max(1, d.AnzahlWochen)).ToList();
    }

    /// <summary>
    /// Blätter eines Übersichtsplans: Zeilen (Elemente) und Spalten (Stunden) pro Blatt laut Druckdetails. Bei „Zeilen/Spalten
    /// automatisch“ wird aus dem Platz berechnet (<paramref name="maxZeilen"/>, <paramref name="maxSpalten"/>); „Nur ganze Tage auf ein
    /// Blatt“ teilt keinen Tag, „Nur Einzeltage auf ein Blatt“ druckt je Blatt einen Tag; „Mehrere Stundenplan-Blöcke pro Blatt“
    /// druckt weitere Tagesblöcke untereinander auf dasselbe Blatt, solange die Zeilen reichen.
    /// </summary>
    public static List<UebersichtBlatt> Uebersicht(int elemente, int tage, int stundenProTag, StundenplanDruckdetails d, int maxZeilen, int maxSpalten)
    {
        var zeilen = Math.Max(1, d.ZeilenSpaltenAutomatisch ? maxZeilen : Math.Min(d.ZeilenProBlatt, Math.Max(1, maxZeilen)));
        var spalten = Math.Max(1, d.ZeilenSpaltenAutomatisch ? maxSpalten : Math.Min(d.SpaltenProBlatt, Math.Max(1, maxSpalten)));
        tage = Math.Max(1, tage);
        stundenProTag = Math.Max(1, stundenProTag);
        // Tagesblöcke: so viele Tage wie Spalten passen (mindestens ein Tag)
        var tageJeBlock = d.NurEinzeltage ? 1 : Math.Max(1, spalten / stundenProTag);
        if (!d.NurGanzeTage && !d.NurEinzeltage) tageJeBlock = Math.Max(1, (int)Math.Ceiling(spalten / (double)stundenProTag));
        var bloecke = new List<(int, int)>();
        for (var t = 0; t < tage; t += tageJeBlock) bloecke.Add((t, Math.Min(tage, t + tageJeBlock) - 1));
        var blaetter = new List<UebersichtBlatt>();
        var elementeJeBlatt = Math.Max(1, zeilen);
        for (var e = 0; e < Math.Max(1, elemente); e += elementeJeBlatt)
        {
            var anz = Math.Min(elementeJeBlatt, Math.Max(0, elemente - e));
            // mehrere Blöcke untereinander: so viele, wie die Zeilen (inkl. je einer Kopfzeile) Platz bieten
            var bloeckeJeBlatt = d.MehrereBloecke && anz > 0 ? Math.Max(1, maxZeilen / (anz + 2)) : 1;
            for (var b = 0; b < bloecke.Count; b += bloeckeJeBlatt)
                blaetter.Add(new UebersichtBlatt(e, anz, bloecke.Skip(b).Take(bloeckeJeBlatt).ToList()));
        }
        return blaetter;
    }
}
