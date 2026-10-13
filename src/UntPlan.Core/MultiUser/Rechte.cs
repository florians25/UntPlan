namespace UntPlan.Core.MultiUser;

/// <summary>Ja/Nein-Rechte einer Benutzergruppe.</summary>
public enum Recht
{
    SchuleAnlegen,
    SchuljahrAnlegen,
    VersionAnlegen,
    Administrator,
    Benutzerverwaltung,
    Optimierung,
    Drucken,
    Vertretungsstatistik,
    WebExport,
    DatenImportExport,
    InfoStundenplan,
}

/// <summary>Bereiche mit abgestuften Rechten.</summary>
public enum Rechtebereich
{
    Stundenplan,
    Stammdaten,
    BesondereDaten,
    Unterricht,
    Perioden,
    Vertretungsplanung,
    Absenzen,
    Kursplanung,
    Anrechnungen,
}

/// <summary>Rechtestufe eines Bereichs (aufsteigend).</summary>
public enum Stufe
{
    Keine = 0,
    NurLesen = 1,
    AnsichtenBearbeiten = 2,
    Bearbeiten = 3,
}

/// <summary>Rechte eines angemeldeten Benutzers (aus Gruppe und Einschränkungen).</summary>
public sealed class Rechte
{
    public HashSet<Recht> Erlaubt { get; } = new();
    public Dictionary<Rechtebereich, Stufe> Stufen { get; } = new();
    /// <summary>Einschränkung "Nur lesen": alle Bereiche höchstens lesend.</summary>
    public bool NurLesen { get; set; }
    /// <summary>Einschränkung auf eine Abteilung (Kurzname, leer = alle).</summary>
    public string Abteilung { get; set; } = "";
    /// <summary>Einschränkung auf eine Periode (Name, leer = alle).</summary>
    public string Periode { get; set; } = "";

    public bool IstAdministrator => Erlaubt.Contains(Recht.Administrator);

    /// <summary>Alle Rechte (Gruppe Administrator, Einzelplatzbetrieb).</summary>
    public static Rechte Alle()
    {
        var r = new Rechte();
        foreach (var x in Enum.GetValues<Recht>()) r.Erlaubt.Add(x);
        foreach (var b in Enum.GetValues<Rechtebereich>()) r.Stufen[b] = Stufe.Bearbeiten;
        return r;
    }

    public static Rechte Aus(Gruppe g)
    {
        var r = new Rechte();
        foreach (var x in g.Rechte) r.Erlaubt.Add(x);
        foreach (var (b, s) in g.Stufen) r.Stufen[b] = s;
        return r;
    }

    public bool Hat(Recht recht) => IstAdministrator || Erlaubt.Contains(recht);

    /// <summary>Wirksame Stufe eines Bereichs (Administrator: Bearbeiten, "Nur lesen": höchstens Lesen).</summary>
    public Stufe StufeVon(Rechtebereich b)
    {
        var s = IstAdministrator ? Stufe.Bearbeiten : Stufen.GetValueOrDefault(b, Stufe.Keine);
        return NurLesen && s > Stufe.NurLesen ? Stufe.NurLesen : s;
    }

    /// <summary>Darf der Benutzer im Rechtebereich mindestens die angegebene Stufe ausüben?</summary>
    public bool Darf(Rechtebereich b, Stufe s) => StufeVon(b) >= s;

    public bool DarfLesen(Rechtebereich b) => Darf(b, Stufe.NurLesen);
    public bool DarfBearbeiten(Rechtebereich b) => Darf(b, Stufe.Bearbeiten);

    /// <summary>Bereiche, die von einer Änderungsart betroffen sind.</summary>
    public static Rechtebereich[] Bereiche(AenderungsArt art) => art switch
    {
        AenderungsArt.Stammdaten => new[] { Rechtebereich.Stammdaten },
        AenderungsArt.Unterricht => new[] { Rechtebereich.Unterricht },
        AenderungsArt.Stundenplan => new[] { Rechtebereich.Stundenplan },
        AenderungsArt.Vertretung => new[] { Rechtebereich.Vertretungsplanung },
        AenderungsArt.Einstellungen => new[] { Rechtebereich.BesondereDaten },
        _ => Enum.GetValues<Rechtebereich>(),
    };

    /// <summary>Darf eine Änderung dieser Art geschrieben werden? Bei "Alles" genügt ein Rechtebereich mit Bearbeitungsrecht.</summary>
    public bool DarfAendern(AenderungsArt art) =>
        art == AenderungsArt.Alles ? Enum.GetValues<Rechtebereich>().Any(DarfBearbeiten) : Bereiche(art).All(DarfBearbeiten);

    public static string Text(Recht r) => r switch
    {
        Recht.SchuleAnlegen => "Schule anlegen",
        Recht.SchuljahrAnlegen => "Schuljahr anlegen",
        Recht.VersionAnlegen => "Version anlegen",
        Recht.Administrator => "Administrator",
        Recht.Benutzerverwaltung => "Benutzerverwaltung",
        Recht.Optimierung => "Optimierung",
        Recht.Drucken => "Drucken",
        Recht.Vertretungsstatistik => "Vertretungsstatistik",
        Recht.WebExport => "Web-Export",
        Recht.DatenImportExport => "Datenimport/-export",
        _ => "Info-Stundenplan",
    };

    public static string Text(Rechtebereich b) => b switch
    {
        Rechtebereich.BesondereDaten => "Besondere Daten",
        _ => b.ToString(),
    };

    public static string Text(Stufe s) => s switch
    {
        Stufe.Keine => "Keine",
        Stufe.NurLesen => "Nur lesen",
        Stufe.AnsichtenBearbeiten => "Ansichten bearbeiten",
        _ => "Bearbeiten",
    };
}
