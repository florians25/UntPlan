using UntPlan.Core.Model;

namespace UntPlan.Core.MultiUser;

/// <summary>
/// Angemeldeter Arbeitsplatz im MultiUser-Betrieb: Benutzer, Rechte, gewählte Version, Sitzung und zuletzt gelesene Revision.
/// Änderungen werden einzeln sofort geschrieben; <see cref="PruefeAenderungen"/> erkennt fremde Änderungen.
/// </summary>
public sealed class MultiUserKlient
{
    private MultiUserKlient(MultiUserDb db, BenutzerInfo benutzer, Gruppe gruppe, Rechte rechte, SchuleInfo schule, SchuljahrInfo schuljahr, VersionInfo version, int sitzung)
    {
        Db = db;
        Benutzer = benutzer;
        Gruppe = gruppe;
        Rechte = rechte;
        Schule = schule;
        Schuljahr = schuljahr;
        Version = version;
        SitzungId = sitzung;
    }

    public MultiUserDb Db { get; }
    public BenutzerInfo Benutzer { get; }
    public Gruppe Gruppe { get; }
    public Rechte Rechte { get; }
    public SchuleInfo Schule { get; }
    public SchuljahrInfo Schuljahr { get; }
    public VersionInfo Version { get; }
    public int SitzungId { get; }
    /// <summary>Zuletzt gelesene bzw. geschriebene Revision der Daten.</summary>
    public int Revision { get; private set; }
    /// <summary>Der Benutzer hat (noch) kein Passwort – die Oberfläche fordert zur Vergabe auf.</summary>
    public bool PasswortFehlt => !Benutzer.HatPasswort;
    public bool Exklusiv { get; private set; }
    public bool Abgemeldet { get; private set; }

    /// <summary>Kurzbeschreibung für Titelleiste/Statuszeile, z. B. "123456 ~ 2024/2025 ~ 1 (Administrator)".</summary>
    public string Titel => $"{Schule.Nr} ~ {Schuljahr.Nr} ~ {Version.Nr} ({Benutzer.Name})";

    /// <summary>Meldet einen Benutzer an einer Version an (Passwort, Einschränkungen, Exklusivbetrieb werden geprüft).</summary>
    public static MultiUserKlient Anmelden(MultiUserDb db, string benutzer, string passwort, int versionId, string? rechner = null)
    {
        var b = db.BenutzerSuchen(benutzer);
        if (b is null || !db.PasswortPruefen(b.Name, passwort ?? ""))
            throw new MultiUserException("Benutzername oder Passwort ist falsch.");
        var gruppe = db.Gruppe(b.GruppeId) ?? throw new MultiUserException($"Dem Benutzer {b.Name} ist keine gültige Gruppe zugeordnet.");
        var (schule, schuljahr, version) = db.VersionPfad(versionId);
        var rechte = Rechte.Aus(gruppe);
        if (!rechte.IstAdministrator)
        {
            var zeilen = db.Einschraenkungen(gruppe.Id);
            if (zeilen.Count > 0)
            {
                var e = zeilen.FirstOrDefault(z => z.Passt(schule.Nr, schuljahr.Nr, version.Nr))
                        ?? throw new MultiUserException($"Die Gruppe {gruppe.Name} hat keine Berechtigung für {schule.Nr} ~ {schuljahr.Nr} ~ Version {version.Nr}.");
                rechte.NurLesen = e.NurLesen;
                rechte.Periode = e.Periode;
                rechte.Abteilung = e.Abteilung;
            }
        }
        if (b.Abteilung.Length > 0) rechte.Abteilung = b.Abteilung;
        var sperre = db.Exklusivsperre(versionId);
        if (sperre != null)
            throw new MultiUserException($"Anmeldung nicht möglich: {sperre.Benutzer} arbeitet im Exklusivbetrieb.");
        var sitzung = db.SitzungStarten(b.Name, rechner ?? Environment.MachineName, versionId);
        db.LoginMerken(b.Id);
        return new MultiUserKlient(db, b, gruppe, rechte, schule, schuljahr, version, sitzung);
    }

    /// <summary>Lädt die aktuellen Daten der Version.</summary>
    public Schuldatei Laden()
    {
        var (daten, rev) = Db.Laden(Version.Id);
        Revision = rev;
        return daten;
    }

    /// <summary>Neu laden ohne Zusammenführen (lokale, nicht geschriebene Änderungen gehen verloren).</summary>
    public Schuldatei Neuladen() => Laden();

    /// <summary>Gibt es eine neuere Revision als die zuletzt gelesene? (Abfrage etwa alle 10 Sekunden.)</summary>
    public bool PruefeAenderungen() => !Abgemeldet && Db.Revision(Version.Id) != Revision;

    /// <summary>Schreibt den aktuellen Stand. Wirft <see cref="MultiUserKonfliktException"/>, wenn inzwischen ein anderer Benutzer geschrieben hat.</summary>
    public void Speichern(Schuldatei daten, string beschreibung, AenderungsArt art = AenderungsArt.Alles)
    {
        if (Abgemeldet) throw new MultiUserException("Die Sitzung wurde beendet.");
        if (!Rechte.DarfAendern(art))
            throw new MultiUserException("Sie haben keine Berechtigung, diese Daten zu ändern.");
        Revision = Db.Speichern(Version.Id, Revision, daten, Benutzer.Name, beschreibung, SitzungId);
    }

    /// <summary>Lebenszeichen senden. false = die Sitzung wurde von außen beendet.</summary>
    public bool Lebenszeichen(string? taetigkeit = null)
    {
        if (Abgemeldet) return false;
        if (Db.Lebenszeichen(SitzungId, taetigkeit)) return true;
        Abgemeldet = true;
        return false;
    }

    public List<Nachricht> NachrichtenAbholen() => Db.NachrichtenAbholen(Benutzer.Name);

    public int NachrichtSenden(string? empfaenger, string text) => Db.NachrichtSenden(Benutzer.Name, empfaenger, text);

    public bool ExklusivAnfordern(out string grund)
    {
        Exklusiv = Db.ExklusivAnfordern(SitzungId, out grund);
        return Exklusiv;
    }

    public void ExklusivFreigeben()
    {
        Db.ExklusivFreigeben(SitzungId);
        Exklusiv = false;
    }

    /// <summary>Ändert das eigene Passwort (altes Passwort muss stimmen).</summary>
    public void PasswortAendern(string alt, string neu)
    {
        if (!Db.PasswortPruefen(Benutzer.Name, alt)) throw new MultiUserException("Das bisherige Passwort ist falsch.");
        Db.PasswortSetzen(Benutzer.Id, neu);
        Benutzer.HatPasswort = neu.Length > 0;
    }

    public void Abmelden()
    {
        if (Abgemeldet) return;
        Abgemeldet = true;
        try { Db.SitzungBeenden(SitzungId); } catch { /* Datenbank nicht erreichbar */ }
    }
}
