using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Core.Model;

namespace UntPlan.Core.MultiUser;

/// <summary>
/// Zugriff auf die MultiUser-Datenbank: Schema, Schulen → Schuljahre → Versionen, Daten mit Revisionsnummer
/// (optimistische Sperre: erster Schreiber gewinnt), Benutzer, Gruppen und Rechte, Sitzungen, Nachrichten, Sperren.
/// Alle Zugriffe laufen über einfaches ADO.NET, damit SQLite, SQL Server, MySQL/MariaDB und Access gleich behandelt werden.
/// </summary>
public sealed class MultiUserDb
{
    public const int AktuelleSchemaVersion = 1;
    public const string AdminName = "Administrator";
    public const string AdminGruppe = "Administrator";
    public const string ArtExklusiv = "Exklusiv";
    private const int Iterationen = 100_000;

    private static readonly JsonSerializerOptions Kompakt = new(Dokument.JsonOptionen) { WriteIndented = false };

    private readonly IMultiUserDatenbank _db;
    private SqlDialekt D => _db.Dialekt;

    public MultiUserDb(IMultiUserDatenbank db) => _db = db;
    public MultiUserDb(Verbindungsdaten v) : this(MultiUserAnbieter.Erzeugen(v)) { }

    public IMultiUserDatenbank Anbieter => _db;

    /// <summary>Uhr (UTC), austauschbar für Tests.</summary>
    public Func<DateTime> Uhr { get; set; } = () => DateTime.UtcNow;

    /// <summary>Sitzungen ohne Lebenszeichen seit dieser Zeit gelten als beendet.</summary>
    public TimeSpan SitzungsTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Anzahl der aufbewahrten Stände im Änderungsprotokoll je Version.</summary>
    public int AenderungenBehalten { get; set; } = 20;

    // ───────────────────────── Schema ─────────────────────────

    /// <summary>Schemaversion der Datenbank oder null, wenn die Tabellen noch nicht angelegt sind.</summary>
    public int? SchemaVersion()
    {
        try
        {
            using var c = _db.Verbinden();
            var w = Skalar(c, null, "SELECT Wert FROM UP_Schema WHERE Schluessel = @k", ("k", "SchemaVersion"));
            return w is null or DBNull ? null : int.Parse(Convert.ToString(w, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }
        catch (DbException)
        {
            return null;
        }
    }

    /// <summary>Prüft die Verbindung (wirft bei Fehlern).</summary>
    public void VerbindungTesten()
    {
        using var c = _db.Verbinden();
    }

    /// <summary>Legt beim ersten Verbinden alle Tabellen samt Administrator an; prüft die Schemaversion.</summary>
    /// <returns>true, wenn das Schema neu angelegt wurde.</returns>
    public bool Initialisieren()
    {
        var v = SchemaVersion();
        if (v is null)
        {
            SchemaAnlegen();
            return true;
        }
        if (v > AktuelleSchemaVersion)
            throw new MultiUserException("Die Datenbank wurde mit einer neueren UntPlan-Version angelegt. Bitte aktualisieren Sie UntPlan.");
        if (v < AktuelleSchemaVersion) Migrieren(v.Value);
        return false;
    }

    private void Migrieren(int vonVersion)
    {
        // Platz für künftige Schemaänderungen (je Version ein Schritt).
        using var c = _db.Verbinden();
        EinstellungSetzen(c, "SchemaVersion", AktuelleSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    private void SchemaAnlegen()
    {
        string T(int n) => D.Text(n);
        var i = D.Ganzzahl;
        var lang = D.LangerText;
        string[] tabellen =
        {
            $"CREATE TABLE UP_Schema (Schluessel {T(64)} NOT NULL PRIMARY KEY, Wert {T(255)})",
            $"CREATE TABLE UP_Schulen (Id {D.AutoId}, Nr {T(20)}, Bezeichnung {T(255)})",
            $"CREATE TABLE UP_Schuljahre (Id {D.AutoId}, SchuleId {i}, Nr {T(20)}, Bezeichnung {T(255)})",
            $"CREATE TABLE UP_Versionen (Id {D.AutoId}, SchuljahrId {i}, Nr {i}, Bezeichnung {T(255)}, Angelegt {T(30)}, AngelegtVon {T(100)})",
            $"CREATE TABLE UP_Daten (VersionId {i} NOT NULL PRIMARY KEY, Revision {i}, Json {lang}, Geaendert {T(30)}, GeaendertVon {T(100)})",
            $"CREATE TABLE UP_Aenderungen (Id {D.AutoId}, VersionId {i}, Revision {i}, Benutzer {T(100)}, Zeitpunkt {T(30)}, Beschreibung {T(255)}, Json {lang})",
            $"CREATE TABLE UP_Gruppen (Id {D.AutoId}, Gruppenname {T(100)})",
            $"CREATE TABLE UP_Gruppenrechte (GruppeId {i}, Recht {T(64)}, Wert {i})",
            $"CREATE TABLE UP_Benutzer (Id {D.AutoId}, Benutzername {T(100)}, GruppeId {i}, PasswortHash {T(200)}, Salt {T(100)}, LetztesLogin {T(30)}, Abteilung {T(100)})",
            $"CREATE TABLE UP_Einschraenkungen (Id {D.AutoId}, GruppeId {i}, SchuleNr {T(20)}, Schuljahr {T(20)}, VersionNr {i}, Periode {T(100)}, Abteilung {T(100)}, NurLesen {i})",
            $"CREATE TABLE UP_Sitzungen (Id {D.AutoId}, Benutzername {T(100)}, Rechner {T(100)}, VersionId {i}, Angemeldet {T(30)}, Aktivitaet {T(30)}, Taetigkeit {T(255)})",
            $"CREATE TABLE UP_Nachrichten (Id {D.AutoId}, Absender {T(100)}, Empfaenger {T(100)}, Zeitpunkt {T(30)}, Inhalt {lang}, Gelesen {i})",
            $"CREATE TABLE UP_Sperren (Id {D.AutoId}, Art {T(50)}, VersionId {i}, SitzungId {i}, Benutzername {T(100)}, Zeitpunkt {T(30)})",
        };
        using var c = _db.Verbinden();
        foreach (var sql in tabellen) Ausfuehren(c, null, sql);
        Ausfuehren(c, null, "CREATE INDEX IX_UP_Aenderungen ON UP_Aenderungen (VersionId, Revision)");

        // Standardgruppen
        var admin = new Gruppe { Name = AdminGruppe };
        foreach (var r in Enum.GetValues<Recht>()) admin.Rechte.Add(r);
        foreach (var b in Enum.GetValues<Rechtebereich>()) admin.Stufen[b] = Stufe.Bearbeiten;
        var adminId = GruppeSpeichern(c, admin);

        var sp = new Gruppe { Name = "Stundenplaner", Rechte = { Recht.VersionAnlegen, Recht.Optimierung, Recht.Drucken, Recht.WebExport, Recht.DatenImportExport, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) sp.Stufen[b] = b is Rechtebereich.Vertretungsplanung or Rechtebereich.Absenzen ? Stufe.NurLesen : Stufe.Bearbeiten;
        GruppeSpeichern(c, sp);

        var vp = new Gruppe { Name = "Vertretungsplaner", Rechte = { Recht.Drucken, Recht.Vertretungsstatistik, Recht.WebExport, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) vp.Stufen[b] = b is Rechtebereich.Vertretungsplanung or Rechtebereich.Absenzen ? Stufe.Bearbeiten : Stufe.AnsichtenBearbeiten;
        GruppeSpeichern(c, vp);

        var info = new Gruppe { Name = "Info", Rechte = { Recht.Drucken, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) info.Stufen[b] = Stufe.NurLesen;
        GruppeSpeichern(c, info);

        Ausfuehren(c, null, "INSERT INTO UP_Benutzer (Benutzername, GruppeId, PasswortHash, Salt, LetztesLogin, Abteilung) VALUES (@n, @g, @h, @s, @l, @a)",
            ("n", AdminName), ("g", adminId), ("h", ""), ("s", ""), ("l", ""), ("a", ""));
        EinstellungSetzen(c, "AutoLogoutMinuten", "0");
        EinstellungSetzen(c, "SchemaVersion", AktuelleSchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    // ───────────────────────── Einstellungen ─────────────────────────

    public string? Einstellung(string schluessel)
    {
        using var c = _db.Verbinden();
        var w = Skalar(c, null, "SELECT Wert FROM UP_Schema WHERE Schluessel = @k", ("k", schluessel));
        return w is null or DBNull ? null : Convert.ToString(w, CultureInfo.InvariantCulture);
    }

    public void EinstellungSetzen(string schluessel, string wert)
    {
        using var c = _db.Verbinden();
        EinstellungSetzen(c, schluessel, wert);
    }

    private void EinstellungSetzen(DbConnection c, string schluessel, string wert)
    {
        if (Ausfuehren(c, null, "UPDATE UP_Schema SET Wert = @w WHERE Schluessel = @k", ("w", wert), ("k", schluessel)) == 0)
            Ausfuehren(c, null, "INSERT INTO UP_Schema (Schluessel, Wert) VALUES (@k, @w)", ("k", schluessel), ("w", wert));
    }

    /// <summary>Automatische Abmeldung nach so vielen Minuten ohne Benutzeraktivität (0 = aus).</summary>
    public int AutoLogoutMinuten
    {
        get => int.TryParse(Einstellung("AutoLogoutMinuten"), out var m) ? m : 0;
        set => EinstellungSetzen("AutoLogoutMinuten", Math.Max(0, value).ToString(CultureInfo.InvariantCulture));
    }

    // ───────────────────────── Schulen, Schuljahre, Versionen ─────────────────────────

    public List<SchuleInfo> Schulen()
    {
        using var c = _db.Verbinden();
        return Lesen(c, null, "SELECT Id, Nr, Bezeichnung FROM UP_Schulen ORDER BY Nr", r => new SchuleInfo(I(r, 0), S(r, 1), S(r, 2)));
    }

    public SchuleInfo SchuleAnlegen(string nr, string bezeichnung)
    {
        nr = nr.Trim();
        if (nr.Length == 0) throw new MultiUserException("Bitte geben Sie eine Schulnummer ein.");
        if (Schulen().Any(s => string.Equals(s.Nr, nr, StringComparison.OrdinalIgnoreCase)))
            throw new MultiUserException($"Die Schule {nr} existiert bereits.");
        using var c = _db.Verbinden();
        var id = Einfuegen(c, null, "INSERT INTO UP_Schulen (Nr, Bezeichnung) VALUES (@n, @b)", ("n", nr), ("b", bezeichnung));
        return new SchuleInfo(id, nr, bezeichnung);
    }

    public List<SchuljahrInfo> Schuljahre(int schuleId)
    {
        using var c = _db.Verbinden();
        return Lesen(c, null, "SELECT Id, SchuleId, Nr, Bezeichnung FROM UP_Schuljahre WHERE SchuleId = @s ORDER BY Nr",
            r => new SchuljahrInfo(I(r, 0), I(r, 1), S(r, 2), S(r, 3)), ("s", schuleId));
    }

    /// <summary>Legt ein Schuljahr an (Bezeichnung im Format JJJJ/JJJJ, z. B. 2024/2025).</summary>
    public SchuljahrInfo SchuljahrAnlegen(int schuleId, string nr, string bezeichnung)
    {
        nr = nr.Trim().Replace('-', '/');
        if (!Regex.IsMatch(nr, @"^\d{4}/\d{4}$")) throw new MultiUserException("Das Schuljahr muss im Format JJJJ/JJJJ angegeben werden (z. B. 2024/2025).");
        if (Schuljahre(schuleId).Any(s => s.Nr == nr)) throw new MultiUserException($"Das Schuljahr {nr} existiert bereits.");
        using var c = _db.Verbinden();
        var id = Einfuegen(c, null, "INSERT INTO UP_Schuljahre (SchuleId, Nr, Bezeichnung) VALUES (@s, @n, @b)", ("s", schuleId), ("n", nr), ("b", bezeichnung));
        return new SchuljahrInfo(id, schuleId, nr, bezeichnung);
    }

    public List<VersionInfo> Versionen(int schuljahrId)
    {
        using var c = _db.Verbinden();
        return Lesen(c, null,
            "SELECT v.Id, v.SchuljahrId, v.Nr, v.Bezeichnung, d.Revision, d.Geaendert, d.GeaendertVon FROM UP_Versionen v LEFT JOIN UP_Daten d ON d.VersionId = v.Id WHERE v.SchuljahrId = @s ORDER BY v.Nr",
            LeseVersion, ("s", schuljahrId));
    }

    public VersionInfo? Version(int versionId)
    {
        using var c = _db.Verbinden();
        return Lesen(c, null,
            "SELECT v.Id, v.SchuljahrId, v.Nr, v.Bezeichnung, d.Revision, d.Geaendert, d.GeaendertVon FROM UP_Versionen v LEFT JOIN UP_Daten d ON d.VersionId = v.Id WHERE v.Id = @v",
            LeseVersion, ("v", versionId)).FirstOrDefault();
    }

    private static VersionInfo LeseVersion(DbDataReader r) => new(I(r, 0), I(r, 1), I(r, 2), S(r, 3), I(r, 4), Z(r, 5), S(r, 6));

    /// <summary>Schule, Schuljahr und Version zu einer Versions-Id.</summary>
    public (SchuleInfo Schule, SchuljahrInfo Schuljahr, VersionInfo Version) VersionPfad(int versionId)
    {
        var v = Version(versionId) ?? throw new MultiUserException("Die Version existiert nicht mehr.");
        using var c = _db.Verbinden();
        var sj = Lesen(c, null, "SELECT Id, SchuleId, Nr, Bezeichnung FROM UP_Schuljahre WHERE Id = @i", r => new SchuljahrInfo(I(r, 0), I(r, 1), S(r, 2), S(r, 3)), ("i", v.SchuljahrId)).Single();
        var s = Lesen(c, null, "SELECT Id, Nr, Bezeichnung FROM UP_Schulen WHERE Id = @i", r => new SchuleInfo(I(r, 0), S(r, 1), S(r, 2)), ("i", sj.SchuleId)).Single();
        return (s, sj, v);
    }

    /// <summary>Legt eine neue Version mit den angegebenen Daten an (Revision 1).</summary>
    public VersionInfo VersionAnlegen(int schuljahrId, int nr, string bezeichnung, Schuldatei daten, string benutzer)
    {
        if (nr <= 0) throw new MultiUserException("Die Versionsnummer muss größer als 0 sein.");
        if (Versionen(schuljahrId).Any(v => v.Nr == nr)) throw new MultiUserException($"Die Version {nr} existiert bereits.");
        var json = JsonSerializer.Serialize(daten, Kompakt);
        var jetzt = Jetzt();
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        var id = Einfuegen(c, t, "INSERT INTO UP_Versionen (SchuljahrId, Nr, Bezeichnung, Angelegt, AngelegtVon) VALUES (@s, @n, @b, @z, @u)",
            ("s", schuljahrId), ("n", nr), ("b", bezeichnung), ("z", jetzt), ("u", benutzer));
        Ausfuehren(c, t, "INSERT INTO UP_Daten (VersionId, Revision, Json, Geaendert, GeaendertVon) VALUES (@v, @r, @j, @z, @u)",
            ("v", id), ("r", 1), ("j", json), ("z", jetzt), ("u", benutzer));
        Protokollieren(c, t, id, 1, benutzer, jetzt, "Version angelegt", json);
        t.Commit();
        return new VersionInfo(id, schuljahrId, nr, bezeichnung, 1, Lokal(jetzt), benutzer);
    }

    public void VersionLoeschen(int versionId)
    {
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        foreach (var tab in new[] { "UP_Daten", "UP_Aenderungen", "UP_Sperren" })
            Ausfuehren(c, t, $"DELETE FROM {tab} WHERE VersionId = @v", ("v", versionId));
        Ausfuehren(c, t, "DELETE FROM UP_Versionen WHERE Id = @v", ("v", versionId));
        t.Commit();
    }

    // ───────────────────────── Daten ─────────────────────────

    /// <summary>Lädt die Schuldatei einer Version samt Revisionsnummer.</summary>
    public (Schuldatei Daten, int Revision) Laden(int versionId)
    {
        using var c = _db.Verbinden();
        var zeile = Lesen(c, null, "SELECT Json, Revision FROM UP_Daten WHERE VersionId = @v", r => (S(r, 0), I(r, 1)), ("v", versionId));
        if (zeile.Count == 0) throw new MultiUserException("Die Version existiert nicht mehr.");
        var daten = JsonSerializer.Deserialize<Schuldatei>(zeile[0].Item1, Dokument.JsonOptionen) ?? throw new InvalidDataException("Leere Daten");
        if (daten.Version > Schuldatei.AktuelleVersion)
            throw new MultiUserException("Die Daten wurden mit einer neueren UntPlan-Version gespeichert.");
        Dokument.Normalisieren(daten);
        return (daten, zeile[0].Item2);
    }

    /// <summary>Aktuelle Revisionsnummer einer Version (0 = Version existiert nicht).</summary>
    public int Revision(int versionId)
    {
        using var c = _db.Verbinden();
        var w = Skalar(c, null, "SELECT Revision FROM UP_Daten WHERE VersionId = @v", ("v", versionId));
        return w is null or DBNull ? 0 : Convert.ToInt32(w, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Schreibt eine Änderung. Gelingt nur, wenn die gespeicherte Revision noch der erwarteten entspricht,
    /// sonst <see cref="MultiUserKonfliktException"/>. Liefert die neue Revisionsnummer.
    /// </summary>
    public int Speichern(int versionId, int erwarteteRevision, Schuldatei daten, string benutzer, string beschreibung, int sitzungId = 0)
    {
        var json = JsonSerializer.Serialize(daten, Kompakt);
        var jetzt = Jetzt();
        var neu = erwarteteRevision + 1;
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        var sperre = Sperren(c, t, versionId).FirstOrDefault(s => s.Art == ArtExklusiv && s.SitzungId != sitzungId);
        if (sperre != null && SitzungAktiv(c, t, sperre.SitzungId))
            throw new MultiUserException($"Die Daten sind gesperrt: {sperre.Benutzer} arbeitet im Exklusivbetrieb.");
        var n = Ausfuehren(c, t, "UPDATE UP_Daten SET Json = @j, Revision = @neu, Geaendert = @z, GeaendertVon = @u WHERE VersionId = @v AND Revision = @alt",
            ("j", json), ("neu", neu), ("z", jetzt), ("u", benutzer), ("v", versionId), ("alt", erwarteteRevision));
        if (n != 1)
        {
            t.Rollback();
            throw new MultiUserKonfliktException();
        }
        Protokollieren(c, t, versionId, neu, benutzer, jetzt, beschreibung, json);
        t.Commit();
        return neu;
    }

    private void Protokollieren(DbConnection c, DbTransaction t, int versionId, int revision, string benutzer, string zeit, string beschreibung, string json)
    {
        if (beschreibung.Length > 250) beschreibung = beschreibung[..250];
        Ausfuehren(c, t, "INSERT INTO UP_Aenderungen (VersionId, Revision, Benutzer, Zeitpunkt, Beschreibung, Json) VALUES (@v, @r, @u, @z, @b, @j)",
            ("v", versionId), ("r", revision), ("u", benutzer), ("z", zeit), ("b", beschreibung), ("j", json));
        Ausfuehren(c, t, "DELETE FROM UP_Aenderungen WHERE VersionId = @v AND Revision <= @g", ("v", versionId), ("g", revision - Math.Max(1, AenderungenBehalten)));
    }

    /// <summary>Änderungsprotokoll einer Version (neueste zuerst).</summary>
    public List<AenderungInfo> Aenderungen(int versionId)
    {
        using var c = _db.Verbinden();
        return Lesen(c, null, "SELECT Revision, Benutzer, Zeitpunkt, Beschreibung FROM UP_Aenderungen WHERE VersionId = @v ORDER BY Revision DESC",
            r => new AenderungInfo(I(r, 0), S(r, 1), Z(r, 2) ?? DateTime.MinValue, S(r, 3)), ("v", versionId));
    }

    /// <summary>Gespeicherter Stand einer früheren Revision (null, wenn nicht mehr im Protokoll).</summary>
    public Schuldatei? Stand(int versionId, int revision)
    {
        using var c = _db.Verbinden();
        var json = Lesen(c, null, "SELECT Json FROM UP_Aenderungen WHERE VersionId = @v AND Revision = @r", r => S(r, 0), ("v", versionId), ("r", revision)).FirstOrDefault();
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Schuldatei>(json, Dokument.JsonOptionen);
    }

    /// <summary>"Datei in Datenbank speichern": legt die Version an oder überschreibt eine vorhandene Version gleicher Nummer.</summary>
    public VersionInfo DateiSpeichern(string pfad, int schuljahrId, int nr, string bezeichnung, string benutzer)
    {
        var daten = Dokument.Laden(pfad).Daten;
        return DatenSpeichern(daten, schuljahrId, nr, bezeichnung, benutzer, $"Datei {Path.GetFileName(pfad)} in Datenbank gespeichert");
    }

    /// <summary>Schreibt eine komplette Schuldatei in eine (neue oder vorhandene) Version.</summary>
    public VersionInfo DatenSpeichern(Schuldatei daten, int schuljahrId, int nr, string bezeichnung, string benutzer, string beschreibung = "Daten in Datenbank gespeichert")
    {
        var vorhanden = Versionen(schuljahrId).FirstOrDefault(v => v.Nr == nr);
        if (vorhanden is null) return VersionAnlegen(schuljahrId, nr, bezeichnung, daten, benutzer);
        Speichern(vorhanden.Id, Revision(vorhanden.Id), daten, benutzer, beschreibung);
        if (bezeichnung.Length > 0 && bezeichnung != vorhanden.Bezeichnung)
        {
            using var c = _db.Verbinden();
            Ausfuehren(c, null, "UPDATE UP_Versionen SET Bezeichnung = @b WHERE Id = @v", ("b", bezeichnung), ("v", vorhanden.Id));
        }
        return Version(vorhanden.Id)!;
    }

    /// <summary>Exportiert eine Version als Schuldatei (*.up).</summary>
    public void DateiExportieren(int versionId, string pfad)
    {
        var (daten, _) = Laden(versionId);
        new Dokument(daten).Speichern(pfad);
    }

    // ───────────────────────── Benutzer ─────────────────────────

    public List<BenutzerInfo> BenutzerListe()
    {
        using var c = _db.Verbinden();
        return Lesen(c, null, "SELECT Id, Benutzername, GruppeId, PasswortHash, LetztesLogin, Abteilung FROM UP_Benutzer ORDER BY Benutzername",
            r => new BenutzerInfo { Id = I(r, 0), Name = S(r, 1), GruppeId = I(r, 2), HatPasswort = S(r, 3).Length > 0, LetztesLogin = Z(r, 4), Abteilung = S(r, 5) });
    }

    public BenutzerInfo? BenutzerSuchen(string name) =>
        BenutzerListe().FirstOrDefault(b => string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public BenutzerInfo BenutzerAnlegen(string name, int gruppeId, string abteilung = "")
    {
        name = name.Trim();
        if (name.Length == 0) throw new MultiUserException("Bitte geben Sie einen Benutzernamen ein.");
        if (BenutzerSuchen(name) != null) throw new MultiUserException($"Der Benutzer {name} existiert bereits.");
        using var c = _db.Verbinden();
        var id = Einfuegen(c, null, "INSERT INTO UP_Benutzer (Benutzername, GruppeId, PasswortHash, Salt, LetztesLogin, Abteilung) VALUES (@n, @g, @h, @s, @l, @a)",
            ("n", name), ("g", gruppeId), ("h", ""), ("s", ""), ("l", ""), ("a", abteilung));
        return new BenutzerInfo { Id = id, Name = name, GruppeId = gruppeId, Abteilung = abteilung };
    }

    /// <summary>Speichert Name, Gruppe und Abteilung. Der Administrator behält Name und Gruppe.</summary>
    public void BenutzerAendern(BenutzerInfo b)
    {
        var alt = BenutzerListe().FirstOrDefault(x => x.Id == b.Id) ?? throw new MultiUserException("Der Benutzer existiert nicht mehr.");
        if (alt.IstAdministrator && (!string.Equals(b.Name, AdminName, StringComparison.OrdinalIgnoreCase) || b.GruppeId != alt.GruppeId))
            throw new MultiUserException("Name und Gruppe des Administrators können nicht geändert werden.");
        if (BenutzerListe().Any(x => x.Id != b.Id && string.Equals(x.Name, b.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new MultiUserException($"Der Benutzer {b.Name} existiert bereits.");
        using var c = _db.Verbinden();
        Ausfuehren(c, null, "UPDATE UP_Benutzer SET Benutzername = @n, GruppeId = @g, Abteilung = @a WHERE Id = @i",
            ("n", b.Name.Trim()), ("g", b.GruppeId), ("a", b.Abteilung), ("i", b.Id));
    }

    public void BenutzerLoeschen(int benutzerId)
    {
        var b = BenutzerListe().FirstOrDefault(x => x.Id == benutzerId);
        if (b is null) return;
        if (b.IstAdministrator) throw new MultiUserException("Der Benutzer Administrator kann nicht gelöscht werden.");
        using var c = _db.Verbinden();
        Ausfuehren(c, null, "DELETE FROM UP_Benutzer WHERE Id = @i", ("i", benutzerId));
    }

    /// <summary>Setzt das Passwort (leer oder null = Passwort löschen).</summary>
    public void PasswortSetzen(int benutzerId, string? passwort)
    {
        string hash = "", salt = "";
        if (!string.IsNullOrEmpty(passwort))
        {
            var s = RandomNumberGenerator.GetBytes(16);
            salt = Convert.ToBase64String(s);
            hash = $"{Iterationen}:{Convert.ToBase64String(Hash(passwort, s, Iterationen))}";
        }
        using var c = _db.Verbinden();
        Ausfuehren(c, null, "UPDATE UP_Benutzer SET PasswortHash = @h, Salt = @s WHERE Id = @i", ("h", hash), ("s", salt), ("i", benutzerId));
    }

    /// <summary>Prüft das Passwort (Benutzer ohne Passwort: nur leeres Passwort gültig).</summary>
    public bool PasswortPruefen(string name, string passwort)
    {
        var b = BenutzerSuchen(name);
        if (b is null) return false;
        using var c = _db.Verbinden();
        var (hash, salt) = Lesen(c, null, "SELECT PasswortHash, Salt FROM UP_Benutzer WHERE Id = @i", r => (S(r, 0), S(r, 1)), ("i", b.Id)).Single();
        if (hash.Length == 0) return passwort.Length == 0;
        var teile = hash.Split(':');
        if (teile.Length != 2 || !int.TryParse(teile[0], out var it)) return false;
        var soll = Convert.FromBase64String(teile[1]);
        var ist = Hash(passwort, Convert.FromBase64String(salt), it);
        return CryptographicOperations.FixedTimeEquals(soll, ist);
    }

    private static byte[] Hash(string passwort, byte[] salt, int iterationen) =>
        Rfc2898DeriveBytes.Pbkdf2(passwort, salt, iterationen, HashAlgorithmName.SHA256, 32);

    public void LoginMerken(int benutzerId)
    {
        using var c = _db.Verbinden();
        Ausfuehren(c, null, "UPDATE UP_Benutzer SET LetztesLogin = @l WHERE Id = @i", ("l", Jetzt()), ("i", benutzerId));
    }

    // ───────────────────────── Gruppen und Einschränkungen ─────────────────────────

    public List<Gruppe> Gruppen()
    {
        using var c = _db.Verbinden();
        var gruppen = Lesen(c, null, "SELECT Id, Gruppenname FROM UP_Gruppen ORDER BY Id", r => new Gruppe { Id = I(r, 0), Name = S(r, 1) });
        var rechte = Lesen(c, null, "SELECT GruppeId, Recht, Wert FROM UP_Gruppenrechte", r => (G: I(r, 0), R: S(r, 1), W: I(r, 2)));
        foreach (var g in gruppen)
        {
            foreach (var (_, r, w) in rechte.Where(x => x.G == g.Id))
            {
                if (r.StartsWith("R:") && Enum.TryParse<Recht>(r[2..], out var recht) && w != 0) g.Rechte.Add(recht);
                else if (r.StartsWith("B:") && Enum.TryParse<Rechtebereich>(r[2..], out var b)) g.Stufen[b] = (Stufe)Math.Clamp(w, 0, 3);
            }
        }
        return gruppen;
    }

    public Gruppe? Gruppe(int id) => Gruppen().FirstOrDefault(g => g.Id == id);

    /// <summary>Legt eine Gruppe an (Id 0) oder speichert Name und Rechte. Die Gruppe Administrator behält alle Rechte.</summary>
    public int GruppeSpeichern(Gruppe g)
    {
        if (string.IsNullOrWhiteSpace(g.Name)) throw new MultiUserException("Bitte geben Sie einen Gruppennamen ein.");
        var alle = Gruppen();
        if (alle.Any(x => x.Id != g.Id && string.Equals(x.Name, g.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new MultiUserException($"Die Gruppe {g.Name} existiert bereits.");
        var alt = alle.FirstOrDefault(x => x.Id == g.Id);
        if (alt != null && alt.Name == AdminGruppe)
        {
            g.Name = AdminGruppe;
            foreach (var r in Enum.GetValues<Recht>()) g.Rechte.Add(r);
            foreach (var b in Enum.GetValues<Rechtebereich>()) g.Stufen[b] = Stufe.Bearbeiten;
        }
        using var c = _db.Verbinden();
        return GruppeSpeichern(c, g);
    }

    private int GruppeSpeichern(DbConnection c, Gruppe g)
    {
        using var t = c.BeginTransaction();
        if (g.Id == 0) g.Id = Einfuegen(c, t, "INSERT INTO UP_Gruppen (Gruppenname) VALUES (@n)", ("n", g.Name.Trim()));
        else Ausfuehren(c, t, "UPDATE UP_Gruppen SET Gruppenname = @n WHERE Id = @i", ("n", g.Name.Trim()), ("i", g.Id));
        Ausfuehren(c, t, "DELETE FROM UP_Gruppenrechte WHERE GruppeId = @i", ("i", g.Id));
        foreach (var r in Enum.GetValues<Recht>())
            Ausfuehren(c, t, "INSERT INTO UP_Gruppenrechte (GruppeId, Recht, Wert) VALUES (@g, @r, @w)", ("g", g.Id), ("r", "R:" + r), ("w", g.Rechte.Contains(r) ? 1 : 0));
        foreach (var b in Enum.GetValues<Rechtebereich>())
            Ausfuehren(c, t, "INSERT INTO UP_Gruppenrechte (GruppeId, Recht, Wert) VALUES (@g, @r, @w)", ("g", g.Id), ("r", "B:" + b), ("w", (int)g.Stufen.GetValueOrDefault(b, Stufe.Keine)));
        t.Commit();
        return g.Id;
    }

    public void GruppeLoeschen(int gruppeId)
    {
        var g = Gruppe(gruppeId);
        if (g is null) return;
        if (g.Name == AdminGruppe) throw new MultiUserException("Die Gruppe Administrator kann nicht gelöscht werden.");
        if (BenutzerListe().Any(b => b.GruppeId == gruppeId)) throw new MultiUserException($"Der Gruppe {g.Name} sind noch Benutzer zugeordnet.");
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        Ausfuehren(c, t, "DELETE FROM UP_Gruppenrechte WHERE GruppeId = @i", ("i", gruppeId));
        Ausfuehren(c, t, "DELETE FROM UP_Einschraenkungen WHERE GruppeId = @i", ("i", gruppeId));
        Ausfuehren(c, t, "DELETE FROM UP_Gruppen WHERE Id = @i", ("i", gruppeId));
        t.Commit();
    }

    public List<Einschraenkung> Einschraenkungen(int gruppeId)
    {
        using var c = _db.Verbinden();
        return Lesen(c, null, "SELECT Id, GruppeId, SchuleNr, Schuljahr, VersionNr, Periode, Abteilung, NurLesen FROM UP_Einschraenkungen WHERE GruppeId = @g ORDER BY Id",
            r => new Einschraenkung { Id = I(r, 0), GruppeId = I(r, 1), Schule = S(r, 2), Schuljahr = S(r, 3), Version = I(r, 4), Periode = S(r, 5), Abteilung = S(r, 6), NurLesen = I(r, 7) != 0 },
            ("g", gruppeId));
    }

    /// <summary>Ersetzt die Einschränkungstabelle einer Gruppe.</summary>
    public void EinschraenkungenSpeichern(int gruppeId, IEnumerable<Einschraenkung> zeilen)
    {
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        Ausfuehren(c, t, "DELETE FROM UP_Einschraenkungen WHERE GruppeId = @g", ("g", gruppeId));
        foreach (var e in zeilen)
            e.Id = Einfuegen(c, t, "INSERT INTO UP_Einschraenkungen (GruppeId, SchuleNr, Schuljahr, VersionNr, Periode, Abteilung, NurLesen) VALUES (@g, @s, @j, @v, @p, @a, @l)",
                ("g", gruppeId), ("s", e.Schule.Trim()), ("j", e.Schuljahr.Trim()), ("v", e.Version), ("p", e.Periode.Trim()), ("a", e.Abteilung.Trim()), ("l", e.NurLesen ? 1 : 0));
        t.Commit();
    }

    // ───────────────────────── Sitzungen ─────────────────────────

    public int SitzungStarten(string benutzer, string rechner, int versionId)
    {
        var jetzt = Jetzt();
        using var c = _db.Verbinden();
        return Einfuegen(c, null, "INSERT INTO UP_Sitzungen (Benutzername, Rechner, VersionId, Angemeldet, Aktivitaet, Taetigkeit) VALUES (@b, @r, @v, @a, @z, @t)",
            ("b", benutzer), ("r", rechner), ("v", versionId), ("a", jetzt), ("z", jetzt), ("t", "Angemeldet"));
    }

    /// <summary>Aktualisiert das Lebenszeichen einer Sitzung. false = die Sitzung wurde beendet (z. B. vom Administrator abgemeldet).</summary>
    public bool Lebenszeichen(int sitzungId, string? taetigkeit = null)
    {
        using var c = _db.Verbinden();
        var n = taetigkeit is null
            ? Ausfuehren(c, null, "UPDATE UP_Sitzungen SET Aktivitaet = @z WHERE Id = @i", ("z", Jetzt()), ("i", sitzungId))
            : Ausfuehren(c, null, "UPDATE UP_Sitzungen SET Aktivitaet = @z, Taetigkeit = @t WHERE Id = @i", ("z", Jetzt()), ("t", taetigkeit.Length > 250 ? taetigkeit[..250] : taetigkeit), ("i", sitzungId));
        return n > 0;
    }

    public void SitzungBeenden(int sitzungId)
    {
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        Ausfuehren(c, t, "DELETE FROM UP_Sperren WHERE SitzungId = @i", ("i", sitzungId));
        Ausfuehren(c, t, "DELETE FROM UP_Sitzungen WHERE Id = @i", ("i", sitzungId));
        t.Commit();
    }

    /// <summary>Aktive Sitzungen (abgelaufene werden dabei entfernt), optional nur einer Version.</summary>
    public List<SitzungInfo> Sitzungen(int? versionId = null)
    {
        using var c = _db.Verbinden();
        var alle = Lesen(c, null, "SELECT Id, Benutzername, Rechner, VersionId, Angemeldet, Aktivitaet, Taetigkeit FROM UP_Sitzungen ORDER BY Id",
            r => new SitzungInfo(I(r, 0), S(r, 1), S(r, 2), I(r, 3), Z(r, 4) ?? DateTime.MinValue, Z(r, 5) ?? DateTime.MinValue, S(r, 6)));
        var grenze = Uhr().Add(-SitzungsTimeout).ToLocalTime();
        foreach (var s in alle.Where(s => s.Aktivitaet < grenze))
        {
            Ausfuehren(c, null, "DELETE FROM UP_Sperren WHERE SitzungId = @i", ("i", s.Id));
            Ausfuehren(c, null, "DELETE FROM UP_Sitzungen WHERE Id = @i", ("i", s.Id));
        }
        return alle.Where(s => s.Aktivitaet >= grenze && (versionId is null || s.VersionId == versionId)).ToList();
    }

    private bool SitzungAktiv(DbConnection c, DbTransaction? t, int sitzungId)
    {
        var z = Lesen(c, t, "SELECT Aktivitaet FROM UP_Sitzungen WHERE Id = @i", r => Z(r, 0), ("i", sitzungId)).FirstOrDefault();
        return z.HasValue && z.Value >= Uhr().Add(-SitzungsTimeout).ToLocalTime();
    }

    // ───────────────────────── Nachrichten ─────────────────────────

    /// <summary>Sendet eine Nachricht an einen Benutzer oder (empfänger null) an alle angemeldeten Benutzer außer dem Absender.</summary>
    public int NachrichtSenden(string absender, string? empfaenger, string text)
    {
        var ziele = empfaenger is null
            ? Sitzungen().Select(s => s.Benutzer).Where(b => !string.Equals(b, absender, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string> { empfaenger };
        using var c = _db.Verbinden();
        var jetzt = Jetzt();
        foreach (var z in ziele)
            Ausfuehren(c, null, "INSERT INTO UP_Nachrichten (Absender, Empfaenger, Zeitpunkt, Inhalt, Gelesen) VALUES (@a, @e, @z, @t, @g)",
                ("a", absender), ("e", z), ("z", jetzt), ("t", text), ("g", 0));
        return ziele.Count;
    }

    /// <summary>Liefert die ungelesenen Nachrichten eines Benutzers und markiert sie als gelesen.</summary>
    public List<Nachricht> NachrichtenAbholen(string benutzer)
    {
        using var c = _db.Verbinden();
        var liste = Lesen(c, null, "SELECT Id, Absender, Empfaenger, Zeitpunkt, Inhalt FROM UP_Nachrichten WHERE Gelesen = 0 AND Empfaenger = @e ORDER BY Id",
            r => new Nachricht(I(r, 0), S(r, 1), S(r, 2), Z(r, 3) ?? DateTime.MinValue, S(r, 4)), ("e", benutzer));
        foreach (var n in liste) Ausfuehren(c, null, "UPDATE UP_Nachrichten SET Gelesen = 1 WHERE Id = @i", ("i", n.Id));
        return liste;
    }

    // ───────────────────────── Sperren und Exklusivbetrieb ─────────────────────────

    public List<SperreInfo> Sperren(int versionId)
    {
        using var c = _db.Verbinden();
        return Sperren(c, null, versionId);
    }

    private List<SperreInfo> Sperren(DbConnection c, DbTransaction? t, int versionId) =>
        Lesen(c, t, "SELECT Id, Art, VersionId, SitzungId, Benutzername, Zeitpunkt FROM UP_Sperren WHERE VersionId = @v ORDER BY Id",
            r => new SperreInfo(I(r, 0), S(r, 1), I(r, 2), I(r, 3), S(r, 4), Z(r, 5) ?? DateTime.MinValue), ("v", versionId));

    /// <summary>Setzt eine Sperre (z. B. "Optimierung") für die Version der Sitzung. false, wenn eine andere aktive Sitzung sie hält.</summary>
    public bool SperreSetzen(int sitzungId, string art, out string grund)
    {
        grund = "";
        var sitzung = Sitzungen().FirstOrDefault(s => s.Id == sitzungId);
        if (sitzung is null) { grund = "Die Sitzung ist nicht mehr aktiv."; return false; }
        using var c = _db.Verbinden();
        using var t = c.BeginTransaction();
        var vorhanden = Sperren(c, t, sitzung.VersionId).Where(s => s.Art == art || s.Art == ArtExklusiv).ToList();
        var fremd = vorhanden.FirstOrDefault(s => s.SitzungId != sitzungId);
        if (fremd != null)
        {
            grund = fremd.Art == ArtExklusiv ? $"{fremd.Benutzer} arbeitet im Exklusivbetrieb." : $"{fremd.Benutzer} hat die Sperre \"{art}\" gesetzt.";
            return false;
        }
        if (vorhanden.All(s => s.Art != art))
            Ausfuehren(c, t, "INSERT INTO UP_Sperren (Art, VersionId, SitzungId, Benutzername, Zeitpunkt) VALUES (@a, @v, @s, @b, @z)",
                ("a", art), ("v", sitzung.VersionId), ("s", sitzungId), ("b", sitzung.Benutzer), ("z", Jetzt()));
        t.Commit();
        return true;
    }

    public void SperreAufheben(int sitzungId, string art)
    {
        using var c = _db.Verbinden();
        Ausfuehren(c, null, "DELETE FROM UP_Sperren WHERE SitzungId = @s AND Art = @a", ("s", sitzungId), ("a", art));
    }

    /// <summary>Exklusivbetrieb anfordern: nur möglich, wenn kein anderer Benutzer an derselben Version angemeldet ist.</summary>
    public bool ExklusivAnfordern(int sitzungId, out string grund)
    {
        var sitzung = Sitzungen().FirstOrDefault(s => s.Id == sitzungId);
        if (sitzung is null) { grund = "Die Sitzung ist nicht mehr aktiv."; return false; }
        var andere = Sitzungen(sitzung.VersionId).Where(s => s.Id != sitzungId).ToList();
        if (andere.Count > 0)
        {
            grund = "Exklusivbetrieb ist nicht möglich, solange weitere Benutzer angemeldet sind: " + string.Join(", ", andere.Select(s => $"{s.Benutzer} ({s.Rechner})"));
            return false;
        }
        return SperreSetzen(sitzungId, ArtExklusiv, out grund);
    }

    public void ExklusivFreigeben(int sitzungId) => SperreAufheben(sitzungId, ArtExklusiv);

    /// <summary>Exklusivsperre einer anderen, noch aktiven Sitzung auf der Version (oder null).</summary>
    public SperreInfo? Exklusivsperre(int versionId, int eigeneSitzung = 0)
    {
        var aktiv = Sitzungen().Select(s => s.Id).ToHashSet();
        return Sperren(versionId).FirstOrDefault(s => s.Art == ArtExklusiv && s.SitzungId != eigeneSitzung && aktiv.Contains(s.SitzungId));
    }

    // ───────────────────────── ADO.NET-Hilfen ─────────────────────────

    private string Jetzt() => Uhr().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTime? Lokal(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : null;

    private static string S(DbDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";
    private static int I(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    private static DateTime? Z(DbDataReader r, int i) => Lokal(S(r, i));

    private DbCommand Befehl(DbConnection c, DbTransaction? t, string sql, (string Name, object? Wert)[] p)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandTimeout = 30;
        if (D.PositionsParameter && p.Length > 0)
        {
            // OLE DB: Parameter in der Reihenfolge ihres Auftretens als "?" übergeben
            var namen = new List<string>();
            sql = Regex.Replace(sql, @"(?<!@)@(\w+)", m => { namen.Add(m.Groups[1].Value); return "?"; });
            foreach (var n in namen) Parameter(cmd, n, p.First(x => x.Name == n).Wert);
        }
        else
        {
            foreach (var (n, w) in p) Parameter(cmd, "@" + n, w);
        }
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Parameter(DbCommand cmd, string name, object? wert)
    {
        var par = cmd.CreateParameter();
        par.ParameterName = name;
        par.Value = wert ?? DBNull.Value;
        cmd.Parameters.Add(par);
    }

    private int Ausfuehren(DbConnection c, DbTransaction? t, string sql, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, t, sql, p);
        return cmd.ExecuteNonQuery();
    }

    private object? Skalar(DbConnection c, DbTransaction? t, string sql, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, t, sql, p);
        return cmd.ExecuteScalar();
    }

    private List<T> Lesen<T>(DbConnection c, DbTransaction? t, string sql, Func<DbDataReader, T> zeile, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, t, sql, p);
        using var r = cmd.ExecuteReader();
        var liste = new List<T>();
        while (r.Read()) liste.Add(zeile(r));
        return liste;
    }

    private int Einfuegen(DbConnection c, DbTransaction? t, string sql, params (string, object?)[] p)
    {
        if (D.LetzteIdImSelbenBefehl)
            return Convert.ToInt32(Skalar(c, t, sql + "; " + D.LetzteId, p), CultureInfo.InvariantCulture);
        Ausfuehren(c, t, sql, p);
        return Convert.ToInt32(Skalar(c, t, D.LetzteId), CultureInfo.InvariantCulture);
    }
}
