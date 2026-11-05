namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Art einer Vertretungsansicht (Spalte „Art“ im Fenster „Formate / Vertretung“): Liste für Klassen bzw. für Lehrer.</summary>
public enum VertretungsformatArt
{
    Klasse,
    Lehrer,
}

/// <summary>
/// Vertretungsformat (Vertretungsansicht) wie im Handbuch, Kap. 13.1: Name, Langname, Standard, Art, Tagesliste. Der Langname ist
/// zugleich der Schlüssel der gespeicherten Ansicht („Felder der Ansicht“, angezeigte Vertretungsarten).
/// </summary>
public sealed class Vertretungsformat
{
    /// <summary>Kurzname („Ver-Kla“, „Leli“).</summary>
    public string Name { get; set; } = "";
    /// <summary>Langname („Klasse - Standard“, „Vertretungen Lehrende“) – Schlüssel der Ansicht.</summary>
    public string Langname { get; set; } = "";
    public VertretungsformatArt Art { get; set; }
    /// <summary>Druckansicht: Vertretungen mit „N. Druck“ werden nicht gezeigt.</summary>
    public bool Druck { get; set; }
    /// <summary>Nur Pausenaufsichten zeigen.</summary>
    public bool NurPausenaufsichten { get; set; }
    /// <summary>Standardformat der Art.</summary>
    public bool Standard { get; set; }
    /// <summary>Schnelldruck „Ausgabe ▸ Tageslisten“.</summary>
    public bool Tagesliste { get; set; }
    /// <summary>Vom Benutzer angelegt (kann gelöscht werden); die mitgelieferten Formate bleiben immer erhalten.</summary>
    public bool Eigen { get; set; }

    public Vertretungsformat Kopie() => (Vertretungsformat)MemberwiseClone();

    public override string ToString() => Langname;
}

/// <summary>Verwaltung der Vertretungsformate: mitgelieferte Formate, Kopieren („Format speichern als…“), Standard, Tageslisten.</summary>
public static class Vertretungsformate
{
    /// <summary>Langnamen der mitgelieferten Formate (ältere Einstellungen kennen nur diese).</summary>
    public static readonly string[] Mitgeliefert = { "Klasse - Standard", "Lehrer - Standard", "Druck - Klasse", "Druck - Lehrer", "Pausenaufsichten" };

    /// <summary>Die mitgelieferten Formate.</summary>
    public static List<Vertretungsformat> Standardliste() => new()
    {
        new() { Name = "Ver-Kla", Langname = Mitgeliefert[0], Art = VertretungsformatArt.Klasse, Standard = true },
        new() { Name = "Ver-Leh", Langname = Mitgeliefert[1], Art = VertretungsformatArt.Lehrer, Standard = true },
        new() { Name = "Druck-Kla", Langname = Mitgeliefert[2], Art = VertretungsformatArt.Klasse, Druck = true },
        new() { Name = "Druck-Leh", Langname = Mitgeliefert[3], Art = VertretungsformatArt.Lehrer, Druck = true },
        new() { Name = "Pausen", Langname = Mitgeliefert[4], Art = VertretungsformatArt.Lehrer, NurPausenaufsichten = true },
    };

    /// <summary>
    /// Gespeicherte Liste vervollständigen: fehlende mitgelieferte Formate kommen (an ihrer Stelle) hinzu, doppelte und leere Einträge
    /// fallen weg, je Art gibt es genau ein Standardformat. Die übergebene Liste wird angepasst und zurückgegeben.
    /// </summary>
    public static List<Vertretungsformat> Ergaenzen(List<Vertretungsformat>? liste)
    {
        liste ??= new List<Vertretungsformat>();
        liste.RemoveAll(f => string.IsNullOrWhiteSpace(f.Langname));
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        liste.RemoveAll(f => !gesehen.Add(f.Langname.Trim()));
        var standard = Standardliste();
        for (var i = 0; i < standard.Count; i++)
        {
            var vorhanden = liste.FirstOrDefault(f => f.Langname == standard[i].Langname);
            if (vorhanden is null) liste.Insert(Math.Min(i, liste.Count), standard[i]);
            else
            {
                // die Eigenschaften der mitgelieferten Formate (Art, Druck, Pausen) sind fest
                vorhanden.Eigen = false;
                vorhanden.Art = standard[i].Art;
                vorhanden.Druck = standard[i].Druck;
                vorhanden.NurPausenaufsichten = standard[i].NurPausenaufsichten;
                if (string.IsNullOrWhiteSpace(vorhanden.Name)) vorhanden.Name = standard[i].Name;
            }
        }
        foreach (var art in Enum.GetValues<VertretungsformatArt>())
        {
            var std = liste.Where(f => f.Art == art && f.Standard).ToList();
            if (std.Count == 1) continue;
            foreach (var f in std.Skip(1)) f.Standard = false;
            if (std.Count == 0 && liste.FirstOrDefault(f => f.Art == art && !f.Druck && !f.NurPausenaufsichten) is { } erstes) erstes.Standard = true;
        }
        return liste;
    }

    /// <summary>Format über seinen Langnamen (unbekannt: das Standardformat der Klassen bzw. das erste).</summary>
    public static Vertretungsformat Finden(IReadOnlyList<Vertretungsformat> liste, string? langname) =>
        liste.FirstOrDefault(f => f.Langname == langname) ?? liste.FirstOrDefault(f => f.Standard && f.Art == VertretungsformatArt.Klasse) ?? liste[0];

    /// <summary>Prüft Name und Langname eines neuen Formats; Rückgabe: Fehlermeldung oder null.</summary>
    public static string? NamePruefen(IEnumerable<Vertretungsformat> liste, string name, string langname)
    {
        if (string.IsNullOrWhiteSpace(langname) && string.IsNullOrWhiteSpace(name)) return "Bitte einen Namen eingeben.";
        var lang = string.IsNullOrWhiteSpace(langname) ? name.Trim() : langname.Trim();
        if (liste.Any(f => f.Langname.Equals(lang, StringComparison.OrdinalIgnoreCase))) return $"Das Format \"{lang}\" gibt es bereits.";
        if (name.Trim().Length > 0 && liste.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) return $"Der Name \"{name.Trim()}\" ist bereits vergeben.";
        return null;
    }

    /// <summary>
    /// „Format speichern als…“: Kopie der Vorlage unter neuem Namen (Art, Druck und Pausen wie die Vorlage, kein Standard, keine Tagesliste);
    /// das neue Format wird hinter der Vorlage eingefügt.
    /// </summary>
    public static Vertretungsformat Kopieren(List<Vertretungsformat> liste, Vertretungsformat vorlage, string name, string langname)
    {
        if (NamePruefen(liste, name, langname) is { } fehler) throw new InvalidOperationException(fehler);
        var lang = string.IsNullOrWhiteSpace(langname) ? name.Trim() : langname.Trim();
        var neu = vorlage.Kopie();
        neu.Name = name.Trim().Length > 0 ? name.Trim() : lang.Length > 8 ? lang[..8] : lang;
        neu.Langname = lang;
        neu.Standard = false;
        neu.Tagesliste = false;
        neu.Eigen = true;
        var i = liste.IndexOf(vorlage);
        liste.Insert(i < 0 ? liste.Count : i + 1, neu);
        return neu;
    }

    /// <summary>
    /// Übernahme einer eigenen Ansicht aus älteren Einstellungen (Kurzname, Langname, Basis = Langname des mitgelieferten Formats,
    /// dessen Art sie hatte) als eigenes Format. Rückgabe: das neue Format bzw. das bereits vorhandene gleichen Langnamens;
    /// null bei leerem Namen. Die Liste wird dabei vervollständigt (siehe <see cref="Ergaenzen"/>).
    /// </summary>
    public static Vertretungsformat? AlteAnsichtUebernehmen(List<Vertretungsformat> liste, string name, string langname, string? basis)
    {
        Ergaenzen(liste);
        name = (name ?? "").Trim();
        var lang = string.IsNullOrWhiteSpace(langname) ? name : langname.Trim();
        if (lang.Length == 0) return null;
        if (liste.FirstOrDefault(f => f.Langname.Equals(lang, StringComparison.OrdinalIgnoreCase)) is { } vorhanden) return vorhanden;
        var neu = Finden(liste, basis).Kopie();
        // Kurzname bleibt, solange er frei ist (sonst mit angehängter Nummer)
        var basisName = name.Length > 0 ? name : lang.Length > 8 ? lang[..8] : lang;
        var kurz = basisName;
        for (var n = 2; liste.Any(f => f.Name.Equals(kurz, StringComparison.OrdinalIgnoreCase)); n++) kurz = basisName + n;
        neu.Name = kurz;
        neu.Langname = lang;
        neu.Standard = false;
        neu.Tagesliste = false;
        neu.Eigen = true;
        liste.Add(neu);
        return neu;
    }

    /// <summary>Format löschen (nur eigene Formate). Rückgabe: gelöscht.</summary>
    public static bool Loeschen(List<Vertretungsformat> liste, Vertretungsformat f)
    {
        if (!f.Eigen) return false;
        var weg = liste.Remove(f);
        if (weg && f.Standard) Ergaenzen(liste);
        return weg;
    }

    /// <summary>Standardformat der Art setzen (die übrigen Formate dieser Art verlieren das Kennzeichen).</summary>
    public static void StandardSetzen(IEnumerable<Vertretungsformat> liste, Vertretungsformat f)
    {
        foreach (var x in liste.Where(x => x.Art == f.Art)) x.Standard = ReferenceEquals(x, f);
    }

    /// <summary>Formate mit dem Kennzeichen „Tagesliste“ in Listenreihenfolge.</summary>
    public static List<Vertretungsformat> Tageslisten(IEnumerable<Vertretungsformat> liste) => liste.Where(f => f.Tagesliste).ToList();

    /// <summary>Dateiname einer als PDF ausgegebenen Tagesliste („Druck-Kla_2026-11-05.pdf“, unzulässige Zeichen ersetzt).</summary>
    public static string PdfDateiname(Vertretungsformat f, DateOnly datum, string? eigenerName = null)
    {
        var basis = string.IsNullOrWhiteSpace(eigenerName) ? $"{(f.Name.Length > 0 ? f.Name : f.Langname)}_{datum:yyyy-MM-dd}" : eigenerName.Trim();
        var ungueltig = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var name = new string(basis.Select(c => ungueltig.Contains(c) ? '_' : c).ToArray());
        return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
    }
}
