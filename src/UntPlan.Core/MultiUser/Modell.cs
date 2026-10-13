namespace UntPlan.Core.MultiUser;

/// <summary>Allgemeiner Fehler im MultiUser-Betrieb (Meldung wird dem Benutzer angezeigt).</summary>
public class MultiUserException : InvalidOperationException
{
    public MultiUserException(string text) : base(text) { }
}

/// <summary>Ein anderer Benutzer hat die Daten seit dem letzten Laden geändert (erster Schreiber gewinnt).</summary>
public sealed class MultiUserKonfliktException : MultiUserException
{
    public const string Meldung = "Die Änderung konnte nicht durchgeführt werden, da ein anderer Benutzer die Daten inzwischen geändert hat.";
    public MultiUserKonfliktException() : base(Meldung) { }
}

public sealed record SchuleInfo(int Id, string Nr, string Bezeichnung)
{
    public override string ToString() => string.IsNullOrEmpty(Bezeichnung) ? Nr : $"{Nr} – {Bezeichnung}";
}

public sealed record SchuljahrInfo(int Id, int SchuleId, string Nr, string Bezeichnung)
{
    public override string ToString() => string.IsNullOrEmpty(Bezeichnung) ? Nr : $"{Nr} – {Bezeichnung}";
}

public sealed record VersionInfo(int Id, int SchuljahrId, int Nr, string Bezeichnung, int Revision, DateTime? Geaendert, string GeaendertVon)
{
    public override string ToString() => string.IsNullOrEmpty(Bezeichnung) ? Nr.ToString() : $"{Nr} – {Bezeichnung}";
}

public sealed record AenderungInfo(int Revision, string Benutzer, DateTime Zeitpunkt, string Beschreibung);

/// <summary>Benutzer der MultiUser-Datenbank.</summary>
public sealed class BenutzerInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int GruppeId { get; set; }
    public bool HatPasswort { get; set; }
    public DateTime? LetztesLogin { get; set; }
    /// <summary>Abteilung (Kurzname), auf die der Benutzer beschränkt ist; leer = alle.</summary>
    public string Abteilung { get; set; } = "";

    public bool IstAdministrator => string.Equals(Name, MultiUserDb.AdminName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Benutzergruppe mit Ja/Nein-Rechten und Rechtestufen je Rechtebereich.</summary>
public sealed class Gruppe
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public HashSet<Recht> Rechte { get; set; } = new();
    public Dictionary<Rechtebereich, Stufe> Stufen { get; set; } = new();

    public override string ToString() => Name;
}

/// <summary>Zeile der Einschränkungstabelle einer Gruppe (leere Felder = alle).</summary>
public sealed class Einschraenkung
{
    public int Id { get; set; }
    public int GruppeId { get; set; }
    public string Schule { get; set; } = "";
    public string Schuljahr { get; set; } = "";
    /// <summary>Versionsnummer, 0 = alle.</summary>
    public int Version { get; set; }
    public string Periode { get; set; } = "";
    public string Abteilung { get; set; } = "";
    public bool NurLesen { get; set; }

    public bool Passt(string schule, string schuljahr, int version) =>
        (Schule.Length == 0 || string.Equals(Schule, schule, StringComparison.OrdinalIgnoreCase))
        && (Schuljahr.Length == 0 || string.Equals(Schuljahr, schuljahr, StringComparison.OrdinalIgnoreCase))
        && (Version == 0 || Version == version);
}

/// <summary>Angemeldeter Benutzer (Sitzung) mit aktueller Tätigkeit.</summary>
public sealed record SitzungInfo(int Id, string Benutzer, string Rechner, int VersionId, DateTime Angemeldet, DateTime Aktivitaet, string Taetigkeit);

public sealed record Nachricht(int Id, string Absender, string Empfaenger, DateTime Zeitpunkt, string Text);

public sealed record SperreInfo(int Id, string Art, int VersionId, int SitzungId, string Benutzer, DateTime Zeitpunkt);
