using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace UntPlanWeb.Daten;

/// <summary>
/// Datenbank von UntPlanWeb. Standard ist SQLite im Datenordner (Docker: Volume /data) – keine Einrichtung nötig: Die Datei wird
/// beim ersten Start angelegt. Für produktive Installationen kann MySQL bzw. MariaDB verwendet werden (UNTPLANWEB_DB=mysql,
/// UNTPLANWEB_DB_CONNECTION). Das Schema wird in beiden Fällen über nummerierte Migrationen aktuell gehalten (Tabelle schema_version);
/// die Migrationen und alle Befehle sind im SQLite-Dialekt geschrieben, <see cref="Dialekt"/> übersetzt für MySQL.
/// </summary>
public sealed class Db
{
    private readonly string _verbindung;

    public Datenbankart Art { get; }
    public Dialekt Dialekt { get; }

    /// <summary>SQLite: Pfad der Datenbankdatei; MySQL: leer.</summary>
    public string Datei { get; }

    /// <summary>Höchste bekannte Migrationsnummer (= Schemastand nach dem Start).</summary>
    public static int Schemastand => Nummeriert[^1].Nummer;

    /// <summary>SQLite-Datenbank im angegebenen Ordner.</summary>
    public Db(string ordner, string dateiname = "untplanweb.db") : this(Datenbankart.Sqlite, ordner, null, null, dateiname) { }

    /// <param name="art">SQLite oder MySQL/MariaDB.</param>
    /// <param name="ordner">Datenordner (SQLite-Datei; bei MySQL nur für Schlüssel und Sicherungen).</param>
    /// <param name="verbindung">MySQL: Verbindungszeichenfolge (<c>Server=…;Database=…;User ID=…;Password=…</c>) oder <c>mysql://benutzer:passwort@host:3306/datenbank</c>.</param>
    /// <param name="warten">MySQL: so lange auf den Datenbankserver warten (Containerstart).</param>
    /// <param name="dateiname">SQLite: Name der Datei im Datenordner.</param>
    public Db(Datenbankart art, string ordner, string? verbindung, TimeSpan? warten = null, string dateiname = "untplanweb.db")
    {
        Art = art;
        Dialekt = Dialekt.Fuer(art);
        Directory.CreateDirectory(ordner);
        if (art == Datenbankart.MySql)
        {
            if (string.IsNullOrWhiteSpace(verbindung)) throw new InvalidOperationException("Für UNTPLANWEB_DB=mysql bitte UNTPLANWEB_DB_CONNECTION setzen.");
            Datei = "";
            _verbindung = MySqlVerbindung(verbindung);
            Warten(warten ?? TimeSpan.FromSeconds(60));
        }
        else
        {
            Datei = Path.Combine(ordner, dateiname);
            _verbindung = new SqliteConnectionStringBuilder { DataSource = Datei, Cache = SqliteCacheMode.Shared, Pooling = true }.ToString();
        }
        using var c = Oeffnen();
        if (art == Datenbankart.Sqlite) Ausfuehren(c, "PRAGMA journal_mode=WAL;");
        Migrieren(c);
    }

    /// <summary>Verbindungszeichenfolge vervollständigen: utf8mb4, Befehle ohne ausdrückliche Transaktion zulassen (wie bei SQLite).</summary>
    public static string MySqlVerbindung(string eingabe)
    {
        var b = new MySqlConnectionStringBuilder();
        if (Uri.TryCreate(eingabe.Trim(), UriKind.Absolute, out var u) && u.Scheme is "mysql" or "mariadb")
        {
            b.Server = u.Host;
            b.Port = u.Port > 0 ? (uint)u.Port : 3306;
            b.Database = Uri.UnescapeDataString(u.AbsolutePath.Trim('/'));
            var teile = u.UserInfo.Split(':', 2);
            b.UserID = Uri.UnescapeDataString(teile[0]);
            if (teile.Length > 1) b.Password = Uri.UnescapeDataString(teile[1]);
        }
        else b.ConnectionString = eingabe;
        b.CharacterSet = "utf8mb4";
        b.IgnoreCommandTransaction = true;
        b.AllowUserVariables = false;
        return b.ConnectionString;
    }

    private void Warten(TimeSpan dauer)
    {
        var ende = DateTime.UtcNow + dauer;
        while (true)
        {
            try { using var c = Oeffnen(); return; }
            catch (MySqlException) when (DateTime.UtcNow < ende) { Thread.Sleep(2000); }
        }
    }

    public DbConnection Oeffnen()
    {
        if (Art == Datenbankart.MySql)
        {
            var m = new MySqlConnection(_verbindung);
            m.Open();
            using var mc = m.CreateCommand();
            mc.CommandText = $"SET SESSION sql_mode = '{MySqlDialekt.SqlModus}'";
            mc.ExecuteNonQuery();
            return m;
        }
        var c = new SqliteConnection(_verbindung);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public static (string, object?) P(string name, object? wert) => (name, wert);

    private static DbCommand Befehl(DbConnection c, string sql, (string, object?)[] p)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = Dialekt.Fuer(c).Uebersetzen(sql);
        foreach (var (n, w) in p)
        {
            var par = cmd.CreateParameter();
            par.ParameterName = n;
            par.Value = w switch
            {
                null => DBNull.Value,
                bool b => b ? 1 : 0,
                DateTime dt => dt.ToString("O"),
                DateOnly d => d.ToString("yyyy-MM-dd"),
                _ => w,
            };
            cmd.Parameters.Add(par);
        }
        return cmd;
    }

    public static int Ausfuehren(DbConnection c, string sql, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, sql, p);
        return cmd.ExecuteNonQuery();
    }

    public int Ausfuehren(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        return Ausfuehren(c, sql, p);
    }

    /// <summary>Zuletzt vergebene ID (AUTOINCREMENT/AUTO_INCREMENT) in dieser Verbindung.</summary>
    public static long LetzteId(DbConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = Dialekt.Fuer(c).LetzteIdSql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long Einfuegen(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        Ausfuehren(c, sql, p);
        return LetzteId(c);
    }

    public List<T> Liste<T>(string sql, Func<DbDataReader, T> lesen, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        return Liste(c, sql, lesen, p);
    }

    public static List<T> Liste<T>(DbConnection c, string sql, Func<DbDataReader, T> lesen, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, sql, p);
        using var r = cmd.ExecuteReader();
        var res = new List<T>();
        while (r.Read()) res.Add(lesen(r));
        return res;
    }

    public T? Einzel<T>(string sql, Func<DbDataReader, T> lesen, params (string, object?)[] p) where T : class =>
        Liste(sql, lesen, p).FirstOrDefault();

    public object? Wert(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        return Wert(c, sql, p);
    }

    public static object? Wert(DbConnection c, string sql, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, sql, p);
        var w = cmd.ExecuteScalar();
        return w is DBNull ? null : w;
    }

    /// <summary>Mehrere Befehle in einer Transaktion.</summary>
    public void Transaktion(Action<DbConnection> aktion)
    {
        using var c = Oeffnen();
        using var t = c.BeginTransaction();
        aktion(c);
        t.Commit();
    }

    /// <summary>Aktueller Schemastand der Datenbank.</summary>
    public int Version() => Convert.ToInt32(Wert("SELECT COALESCE(MAX(version), 0) FROM schema_version"));

    /// <summary>Alle Tabellen der Anwendung (ohne interne Tabellen der Datenbank).</summary>
    public List<string> Tabellen() => Art == Datenbankart.MySql
        ? Liste("SELECT TABLE_NAME AS n FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME", r => r.Text("n"))
        : Liste("SELECT name AS n FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name", r => r.Text("n"));

    /// <summary>
    /// Sicherung der Datenbank (SQLite: konsistente Kopie per VACUUM INTO). Für MySQL/MariaDB gibt es keine Sicherung aus der
    /// Anwendung heraus – dort <c>mariadb-dump</c> verwenden (siehe README, Abschnitt „Update“).
    /// </summary>
    public string? Sichern(string ordner)
    {
        if (Art != Datenbankart.Sqlite) return null;
        Directory.CreateDirectory(ordner);
        var ziel = Path.Combine(ordner, $"untplanweb-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        Ausfuehren("VACUUM INTO @z", P("@z", ziel));
        return ziel;
    }

    // ------------------------------------------------------------------------------------------
    // Migrationen: nur anhängen, nie ändern. Ab Nummer 19 mit fester Nummer eintragen – Nr(19, """…""") –, damit parallel
    // entstandene Migrationen unabhängig von der Reihenfolge im Quelltext aufsteigend laufen. Einträge ohne Nr(…) erhalten die
    // kleinste noch freie Nummer. Ausgeführt wird jede Nummer, die in schema_version noch fehlt (aufsteigend).
    // ------------------------------------------------------------------------------------------

    private const string NummernPraefix = "#migration ";

    /// <summary>Migration mit fester Nummer (unabhängig von der Position im Array).</summary>
    private static string Nr(int nummer, string skript) => $"{NummernPraefix}{nummer}\n{skript}";

    /// <summary>Die Migrationen aufsteigend nach Nummer, ohne Nummernvermerk.</summary>
    private static (int Nummer, string Skript)[] Nummeriert => _nummeriert ??= Nummerieren(Migrationen);
    private static (int Nummer, string Skript)[]? _nummeriert;

    public static (int Nummer, string Skript)[] Nummerieren(IReadOnlyList<string> migrationen)
    {
        var fest = new List<(int Nummer, string Skript)>();
        var offen = new List<string>();
        for (var i = 0; i < migrationen.Count; i++)
        {
            var m = migrationen[i];
            if (m.StartsWith(NummernPraefix, StringComparison.Ordinal))
            {
                var zeile = m.IndexOf('\n');
                var nummer = int.Parse(m[NummernPraefix.Length..zeile]);
                if (fest.Any(f => f.Nummer == nummer)) throw new InvalidOperationException($"Migration {nummer} doppelt vergeben.");
                fest.Add((nummer, m[(zeile + 1)..]));
            }
            else offen.Add(m);
        }
        var vergeben = fest.Select(f => f.Nummer).ToHashSet();
        var alle = fest.ToList();
        var naechste = 1;
        foreach (var skript in offen)
        {
            while (vergeben.Contains(naechste)) naechste++;
            vergeben.Add(naechste);
            alle.Add((naechste, skript));
        }
        return alle.OrderBy(a => a.Nummer).ToArray();
    }

    private static HashSet<int> Ausgefuehrt(DbConnection c)
    {
        var stand = new HashSet<int>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version";
        using var r = cmd.ExecuteReader();
        while (r.Read()) stand.Add(Convert.ToInt32(r[0]));
        return stand;
    }

    private static readonly string[] Migrationen =
    {
        // 1: Grundschema
        """
        CREATE TABLE einstellungen (schluessel TEXT PRIMARY KEY, wert TEXT NOT NULL);
        CREATE TABLE paket (id INTEGER PRIMARY KEY CHECK (id = 1), revision INTEGER NOT NULL, json TEXT NOT NULL, empfangen TEXT NOT NULL);
        CREATE TABLE benutzer (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            login TEXT NOT NULL UNIQUE COLLATE NOCASE,
            anzeigename TEXT NOT NULL DEFAULT '',
            rolle TEXT NOT NULL,
            element TEXT NOT NULL DEFAULT '',
            email TEXT NOT NULL DEFAULT '',
            passwort TEXT NOT NULL,
            gesperrt INTEGER NOT NULL DEFAULT 0,
            passwort_aendern INTEGER NOT NULL DEFAULT 0,
            ical_token TEXT NOT NULL,
            erstellt TEXT NOT NULL,
            letzte_anmeldung TEXT,
            fehlversuche INTEGER NOT NULL DEFAULT 0,
            gesperrt_bis TEXT
        );
        CREATE TABLE rueckmeldungen (
            id TEXT PRIMARY KEY, typ TEXT NOT NULL, erstellt TEXT NOT NULL, benutzer_id INTEGER, absender TEXT NOT NULL,
            lehrer TEXT, von TEXT, bis TEXT, stunde_von INTEGER, stunde_bis INTEGER, raum TEXT,
            raeume TEXT NOT NULL DEFAULT '[]', klassen TEXT NOT NULL DEFAULT '[]', weitere_lehrer TEXT NOT NULL DEFAULT '[]',
            titel TEXT, grund TEXT, text TEXT, status TEXT NOT NULL DEFAULT 'offen', quittiert TEXT
        );
        CREATE INDEX ix_rueckmeldungen_status ON rueckmeldungen (status);
        CREATE TABLE nachrichten (
            id INTEGER PRIMARY KEY AUTOINCREMENT, von TEXT NOT NULL, bis TEXT NOT NULL, text TEXT NOT NULL,
            zielgruppe TEXT NOT NULL DEFAULT 'alle', startseite INTEGER NOT NULL DEFAULT 1, monitor INTEGER NOT NULL DEFAULT 1,
            erstellt TEXT NOT NULL, benutzer_id INTEGER
        );
        CREATE TABLE mitteilungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, absender_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            betreff TEXT NOT NULL, text TEXT NOT NULL, erstellt TEXT NOT NULL, wichtig INTEGER NOT NULL DEFAULT 0,
            geloescht_absender INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE mitteilung_empfaenger (
            mitteilung_id INTEGER NOT NULL REFERENCES mitteilungen(id) ON DELETE CASCADE,
            benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            gelesen TEXT, geloescht INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (mitteilung_id, benutzer_id)
        );
        CREATE TABLE klassenbuch (
            id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, stunde INTEGER NOT NULL, unterricht INTEGER NOT NULL DEFAULT 0,
            klassen TEXT NOT NULL DEFAULT '[]', fach TEXT NOT NULL DEFAULT '', lehrer TEXT NOT NULL DEFAULT '',
            thema TEXT NOT NULL DEFAULT '', hausaufgabe TEXT NOT NULL DEFAULT '', faellig TEXT, notiz TEXT NOT NULL DEFAULT '',
            geaendert TEXT NOT NULL, benutzer_id INTEGER,
            UNIQUE (datum, stunde, unterricht, lehrer)
        );
        CREATE INDEX ix_klassenbuch_datum ON klassenbuch (datum);
        CREATE TABLE monitore (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, token TEXT NOT NULL UNIQUE,
            version TEXT NOT NULL DEFAULT 'schueler', tage INTEGER NOT NULL DEFAULT 2, erstellt TEXT NOT NULL
        );
        CREATE TABLE protokoll (id INTEGER PRIMARY KEY AUTOINCREMENT, zeit TEXT NOT NULL, benutzer TEXT NOT NULL, aktion TEXT NOT NULL, details TEXT NOT NULL DEFAULT '');
        """,

        // 2: Klassenbuch – Anwesenheit, Fehlzeiten, Vermerke
        """
        ALTER TABLE klassenbuch ADD COLUMN anwesenheit TEXT;
        CREATE TABLE fehlzeiten (
            id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, stunde INTEGER NOT NULL, unterricht INTEGER NOT NULL DEFAULT 0,
            schueler_id INTEGER NOT NULL, klasse TEXT NOT NULL, fach TEXT NOT NULL DEFAULT '',
            art TEXT NOT NULL DEFAULT 'fehlt', minuten INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'offen', bemerkung TEXT NOT NULL DEFAULT '', lehrer TEXT NOT NULL DEFAULT '',
            erstellt TEXT NOT NULL, benutzer_id INTEGER,
            UNIQUE (datum, stunde, schueler_id)
        );
        CREATE INDEX ix_fehlzeiten_klasse ON fehlzeiten (klasse, datum);
        CREATE TABLE vermerke (
            id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, klasse TEXT NOT NULL, schueler_id INTEGER,
            art TEXT NOT NULL DEFAULT 'Vermerk', text TEXT NOT NULL, lehrer TEXT NOT NULL DEFAULT '', erstellt TEXT NOT NULL, benutzer_id INTEGER
        );
        CREATE INDEX ix_vermerke_klasse ON vermerke (klasse, datum);
        """,

        // 3: Notizen für Schüler*innen je Stunde
        """
        ALTER TABLE klassenbuch ADD COLUMN notiz_schueler TEXT NOT NULL DEFAULT '';
        """,

        // 4: Funktionen der Portal-Oberfläche – vorherige Anmeldung, Nachrichten mit Betreff, Klassendienste, Befreiungen,
        //    Abwesenheitsgründe, Sitzpläne, freie Tage, Übertragungsverlauf, Entwürfe/Listen/Anhänge, Kontowechsel
        """
        ALTER TABLE benutzer ADD COLUMN vorherige_anmeldung TEXT;
        ALTER TABLE benutzer ADD COLUMN sprache TEXT NOT NULL DEFAULT 'Deutsch';
        ALTER TABLE benutzer ADD COLUMN benachrichtigung INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE nachrichten ADD COLUMN betreff TEXT NOT NULL DEFAULT '';
        ALTER TABLE nachrichten ADD COLUMN wochentag INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE nachrichten ADD COLUMN reihenfolge INTEGER NOT NULL DEFAULT 10;
        ALTER TABLE nachrichten ADD COLUMN aufgefaechert INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE nachrichten ADD COLUMN oeffentlich INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE nachrichten ADD COLUMN mobil INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE klassenbuch ADD COLUMN online INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE fehlzeiten ADD COLUMN grund TEXT NOT NULL DEFAULT '';
        ALTER TABLE fehlzeiten ADD COLUMN gruppe TEXT NOT NULL DEFAULT '';
        ALTER TABLE mitteilungen ADD COLUMN entwurf INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE mitteilungen ADD COLUMN entwurf_empfaenger TEXT NOT NULL DEFAULT '[]';
        CREATE TABLE mitteilung_anhaenge (
            id INTEGER PRIMARY KEY AUTOINCREMENT, mitteilung_id INTEGER NOT NULL REFERENCES mitteilungen(id) ON DELETE CASCADE,
            name TEXT NOT NULL, typ TEXT NOT NULL DEFAULT 'application/octet-stream', groesse INTEGER NOT NULL DEFAULT 0, daten BLOB NOT NULL
        );
        CREATE TABLE mitteilung_listen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, besitzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            name TEXT NOT NULL, mitglieder TEXT NOT NULL DEFAULT '[]', erstellt TEXT NOT NULL
        );
        CREATE TABLE klassendienste (
            id INTEGER PRIMARY KEY AUTOINCREMENT, kurz TEXT NOT NULL DEFAULT '', name TEXT NOT NULL, aktiv INTEGER NOT NULL DEFAULT 1, reihenfolge INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO klassendienste (kurz, name, reihenfolge) VALUES ('KO', 'Klassenordner', 10), ('KS', 'Klassensprecher*in', 20), ('TD', 'Tafeldienst', 30);
        CREATE TABLE klassendienst_zuordnungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, dienst_id INTEGER NOT NULL REFERENCES klassendienste(id) ON DELETE CASCADE,
            schueler_id INTEGER NOT NULL, klasse TEXT NOT NULL, von TEXT NOT NULL, bis TEXT NOT NULL, text TEXT NOT NULL DEFAULT '',
            erstellt TEXT NOT NULL, benutzer_id INTEGER
        );
        CREATE INDEX ix_klassendienst_klasse ON klassendienst_zuordnungen (klasse, von);
        CREATE TABLE befreiungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, schueler_id INTEGER NOT NULL, klasse TEXT NOT NULL, von TEXT NOT NULL, bis TEXT NOT NULL,
            wochentag INTEGER NOT NULL DEFAULT 0, fach TEXT NOT NULL DEFAULT '', beginn TEXT NOT NULL DEFAULT '', ende TEXT NOT NULL DEFAULT '',
            grund TEXT NOT NULL DEFAULT '', text TEXT NOT NULL DEFAULT '', erstellt TEXT NOT NULL, benutzer_id INTEGER
        );
        CREATE INDEX ix_befreiungen_klasse ON befreiungen (klasse, von);
        CREATE TABLE abwesenheitsgruende (
            id INTEGER PRIMARY KEY AUTOINCREMENT, kurz TEXT NOT NULL, name TEXT NOT NULL, art TEXT NOT NULL DEFAULT 'abwesenheit',
            entschuldigt INTEGER NOT NULL DEFAULT 0, reihenfolge INTEGER NOT NULL DEFAULT 0, aktiv INTEGER NOT NULL DEFAULT 1
        );
        INSERT INTO abwesenheitsgruende (kurz, name, art, entschuldigt, reihenfolge) VALUES
            ('K', 'Krankheit', 'abwesenheit', 0, 10), ('A', 'Arzttermin', 'abwesenheit', 0, 20), ('B', 'Beurlaubung', 'abwesenheit', 1, 30),
            ('F', 'Familiäre Gründe', 'abwesenheit', 0, 40), ('S', 'Schulveranstaltung', 'abwesenheit', 1, 50), ('P', 'Praktikum', 'abwesenheit', 1, 60),
            ('SPB', 'Sportbefreiung (Attest)', 'befreiung', 1, 10), ('REL', 'Befreiung vom Religionsunterricht', 'befreiung', 1, 20),
            ('SON', 'Sonstige Befreiung', 'befreiung', 1, 30);
        CREATE TABLE sitzplaene (
            klasse TEXT PRIMARY KEY COLLATE NOCASE, zeilen INTEGER NOT NULL DEFAULT 5, spalten INTEGER NOT NULL DEFAULT 6,
            belegung TEXT NOT NULL DEFAULT '{}', geaendert TEXT NOT NULL
        );
        CREATE TABLE freie_tage (
            id INTEGER PRIMARY KEY AUTOINCREMENT, von TEXT NOT NULL, bis TEXT NOT NULL, art TEXT NOT NULL DEFAULT 'frei', name TEXT NOT NULL, erstellt TEXT NOT NULL
        );
        CREATE TABLE paket_verlauf (
            id INTEGER PRIMARY KEY AUTOINCREMENT, revision INTEGER NOT NULL, empfangen TEXT NOT NULL, programmversion TEXT NOT NULL DEFAULT '',
            klassen INTEGER NOT NULL DEFAULT 0, lehrer INTEGER NOT NULL DEFAULT 0, raeume INTEGER NOT NULL DEFAULT 0, tage INTEGER NOT NULL DEFAULT 0,
            vertretungen INTEGER NOT NULL DEFAULT 0, von TEXT NOT NULL DEFAULT '', bis TEXT NOT NULL DEFAULT ''
        );
        CREATE TABLE geraete_konten (
            id INTEGER PRIMARY KEY AUTOINCREMENT, geraet TEXT NOT NULL, benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            zuletzt TEXT NOT NULL, UNIQUE (geraet, benutzer_id)
        );
        """,

        // 5: Mehrere Hausaufgaben je Stunde (Text, Fälligkeit, Bemerkung, erledigt) wie im Klassenbuch des Originals;
        //    bisherige Hausaufgaben aus dem Klassenbuch werden übernommen
        """
        CREATE TABLE hausaufgaben_eintraege (
            id INTEGER PRIMARY KEY AUTOINCREMENT, datum TEXT NOT NULL, stunde INTEGER NOT NULL, unterricht INTEGER NOT NULL DEFAULT 0,
            klassen TEXT NOT NULL DEFAULT '[]', fach TEXT NOT NULL DEFAULT '', lehrer TEXT NOT NULL DEFAULT '',
            text TEXT NOT NULL, faellig TEXT, bemerkung TEXT NOT NULL DEFAULT '', erledigt INTEGER NOT NULL DEFAULT 0,
            erstellt TEXT NOT NULL, benutzer_id INTEGER
        );
        CREATE INDEX ix_hausaufgaben_datum ON hausaufgaben_eintraege (datum);
        CREATE INDEX ix_hausaufgaben_faellig ON hausaufgaben_eintraege (faellig);
        INSERT INTO hausaufgaben_eintraege (datum, stunde, unterricht, klassen, fach, lehrer, text, faellig, erstellt, benutzer_id)
            SELECT datum, stunde, unterricht, klassen, fach, lehrer, hausaufgabe, faellig, geaendert, benutzer_id FROM klassenbuch WHERE hausaufgabe <> '';
        """,

        // 6: Raumbuchung zu einem Unterricht (reine Raumänderung), Lehrform/Anmerkung zum Lehrstoff, Uhrzeit von Klassenbucheinträgen,
        //    Schülerverwaltung in UntPlanWeb (Änderungen und neue Schüler*innen ergänzen die Liste aus UntPlan) – nur Standard-SQL
        """
        ALTER TABLE rueckmeldungen ADD COLUMN unterricht INTEGER;
        ALTER TABLE klassenbuch ADD COLUMN lehrform VARCHAR(100) NOT NULL DEFAULT '';
        ALTER TABLE klassenbuch ADD COLUMN anmerkung VARCHAR(1000) NOT NULL DEFAULT '';
        ALTER TABLE vermerke ADD COLUMN zeit VARCHAR(5) NOT NULL DEFAULT '';
        CREATE TABLE schueler_daten (
            id INTEGER NOT NULL PRIMARY KEY, vorname VARCHAR(100) NOT NULL DEFAULT '', name VARCHAR(100) NOT NULL DEFAULT '', klasse VARCHAR(50) NOT NULL DEFAULT '',
            geschlecht VARCHAR(1) NOT NULL DEFAULT '', geburtsdatum VARCHAR(10) NOT NULL DEFAULT '', email VARCHAR(200) NOT NULL DEFAULT '',
            bemerkung VARCHAR(1000) NOT NULL DEFAULT '', eigen INTEGER NOT NULL DEFAULT 0, geloescht INTEGER NOT NULL DEFAULT 0,
            geaendert VARCHAR(30) NOT NULL DEFAULT '', benutzer_id INTEGER
        );
        """,

        // 7: Details zu Abwesenheiten (Nachweis, Datum der Entschuldigung, Eltern informiert, interne Notiz der Klassenleitung) – nur Standard-SQL
        """
        ALTER TABLE fehlzeiten ADD COLUMN nachweis VARCHAR(30) NOT NULL DEFAULT '';
        ALTER TABLE fehlzeiten ADD COLUMN entschuldigt_am VARCHAR(10) NOT NULL DEFAULT '';
        ALTER TABLE fehlzeiten ADD COLUMN eltern_informiert INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE fehlzeiten ADD COLUMN notiz_intern VARCHAR(1000) NOT NULL DEFAULT '';
        """,

        // 8: Minutengenaue Abwesenheiten (abwesend von–bis innerhalb einer Stunde; leer = ganze Stunde) und Zuordnung von
        //    Schüler- bzw. Elternkonten zu Schüler*innen der Schülerverwaltung – nur Standard-SQL
        """
        ALTER TABLE fehlzeiten ADD COLUMN beginn VARCHAR(5) NOT NULL DEFAULT '';
        ALTER TABLE fehlzeiten ADD COLUMN ende VARCHAR(5) NOT NULL DEFAULT '';
        CREATE TABLE benutzer_schueler (
            benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            schueler_id INTEGER NOT NULL,
            PRIMARY KEY (benutzer_id, schueler_id)
        );
        """,

        // 9: Benutzerverwaltung – Zusatzrechte und Personenzuordnung je Konto, Personenliste (Erziehungsberechtigte, sonstiges Personal),
        //    Anmeldehistorie – nur Standard-SQL
        """
        ALTER TABLE benutzer ADD COLUMN zusatzrechte VARCHAR(500) NOT NULL DEFAULT '';
        ALTER TABLE benutzer ADD COLUMN person_id INTEGER;
        CREATE TABLE personen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, art VARCHAR(20) NOT NULL, vorname VARCHAR(100) NOT NULL DEFAULT '', name VARCHAR(100) NOT NULL,
            funktion VARCHAR(100) NOT NULL DEFAULT '', email VARCHAR(200) NOT NULL DEFAULT '', kinder VARCHAR(1000) NOT NULL DEFAULT '',
            erstellt VARCHAR(30) NOT NULL DEFAULT ''
        );
        CREATE TABLE benutzer_anmeldungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE,
            zeit VARCHAR(30) NOT NULL, ergebnis VARCHAR(20) NOT NULL DEFAULT 'ok', adresse VARCHAR(60) NOT NULL DEFAULT '', geraet VARCHAR(200) NOT NULL DEFAULT ''
        );
        CREATE INDEX ix_benutzer_anmeldungen ON benutzer_anmeldungen (benutzer_id, id);
        """,

        // 10: Buchungsmodul – Ressourcenarten, Ressourcen (fest an einem Raum oder mobil mit Anzahl), Raum-Stammdaten aus UntPlanWeb
        //    (Kapazität, Gebäude, Abteilung, Raumgruppe), Buchungen (Raum, Ressource, Aktivität, zusätzlicher Unterricht; Serie mit
        //    Serien-ID), gebuchte Ressourcen und in UntPlanWeb angelegter Unterricht – nur Standard-SQL
        """
        CREATE TABLE ressourcenarten (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name VARCHAR(100) NOT NULL, reihenfolge INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE ressourcen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name VARCHAR(100) NOT NULL, art_id INTEGER, raum VARCHAR(50) NOT NULL DEFAULT '',
            anzahl INTEGER NOT NULL DEFAULT 1, bemerkung VARCHAR(500) NOT NULL DEFAULT '', aktiv INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE raum_daten (
            raum VARCHAR(50) NOT NULL PRIMARY KEY, kapazitaet INTEGER NOT NULL DEFAULT 0, gebaeude VARCHAR(100) NOT NULL DEFAULT '',
            abteilung VARCHAR(100) NOT NULL DEFAULT '', raumgruppe VARCHAR(100) NOT NULL DEFAULT '', geaendert VARCHAR(30) NOT NULL DEFAULT ''
        );
        CREATE TABLE buchungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, serie VARCHAR(40) NOT NULL DEFAULT '', art VARCHAR(20) NOT NULL, datum VARCHAR(10) NOT NULL,
            stunde_von INTEGER NOT NULL, stunde_bis INTEGER NOT NULL, beginn VARCHAR(5) NOT NULL DEFAULT '', ende VARCHAR(5) NOT NULL DEFAULT '',
            raum VARCHAR(50) NOT NULL DEFAULT '', klassen VARCHAR(1000) NOT NULL DEFAULT '[]', lehrer VARCHAR(50) NOT NULL DEFAULT '',
            fach VARCHAR(50) NOT NULL DEFAULT '', titel VARCHAR(200) NOT NULL DEFAULT '', text VARCHAR(1000) NOT NULL DEFAULT '',
            unterricht_id INTEGER, benachrichtigen INTEGER NOT NULL DEFAULT 0, status VARCHAR(20) NOT NULL DEFAULT 'aktiv',
            uebertragen VARCHAR(30), benutzer_id INTEGER, absender VARCHAR(100) NOT NULL DEFAULT '', erstellt VARCHAR(30) NOT NULL DEFAULT '',
            storniert VARCHAR(30)
        );
        CREATE INDEX ix_buchungen_datum ON buchungen (datum, status);
        CREATE INDEX ix_buchungen_serie ON buchungen (serie);
        CREATE TABLE buchung_ressourcen (
            buchung_id INTEGER NOT NULL REFERENCES buchungen(id) ON DELETE CASCADE, ressource_id INTEGER NOT NULL,
            menge INTEGER NOT NULL DEFAULT 1, PRIMARY KEY (buchung_id, ressource_id)
        );
        CREATE TABLE eigene_unterrichte (
            id INTEGER PRIMARY KEY AUTOINCREMENT, lehrer VARCHAR(50) NOT NULL, fach VARCHAR(50) NOT NULL, klassen VARCHAR(1000) NOT NULL DEFAULT '[]',
            gruppe VARCHAR(100) NOT NULL DEFAULT '', text VARCHAR(1000) NOT NULL DEFAULT '', wochenstunden INTEGER NOT NULL DEFAULT 0,
            benutzer_id INTEGER, erstellt VARCHAR(30) NOT NULL DEFAULT '', geloescht INTEGER NOT NULL DEFAULT 0
        );
        """,

        // 11: Infocenter – Infos der Schule (Zielgruppe alle/kollegium/schueler/eltern, gültig von/bis, wichtig) und Gelesen-Vermerke
        //    je Benutzer (Infos „i:<id>“, Neuigkeiten „n:<Schlüssel>“) – nur Standard-SQL
        """
        CREATE TABLE infos (
            id INTEGER PRIMARY KEY AUTOINCREMENT, titel VARCHAR(200) NOT NULL, text TEXT NOT NULL, zielgruppe VARCHAR(20) NOT NULL DEFAULT 'alle',
            von VARCHAR(10), bis VARCHAR(10), wichtig INTEGER NOT NULL DEFAULT 0, erstellt VARCHAR(30) NOT NULL DEFAULT '',
            autor VARCHAR(200) NOT NULL DEFAULT '', geaendert VARCHAR(30) NOT NULL DEFAULT ''
        );
        CREATE TABLE infocenter_gelesen (
            benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE, schluessel VARCHAR(250) NOT NULL,
            zeit VARCHAR(30) NOT NULL DEFAULT '', PRIMARY KEY (benutzer_id, schluessel)
        );
        """,

        // 12: Sitzpläne je Wirkungsbereich (klasse = Klassenkürzel, unterricht = Unterrichtsnummer, raum = „Raum|Lehrkraft“) mit Pult-Lage
        //    und Plätzen ohne Tisch; bisherige Klassen-Sitzpläne werden übernommen – nur Standard-SQL
        """
        CREATE TABLE sitzplan_bereiche (
            id INTEGER PRIMARY KEY AUTOINCREMENT, bereich VARCHAR(20) NOT NULL, schluessel VARCHAR(200) NOT NULL,
            zeilen INTEGER NOT NULL DEFAULT 5, spalten INTEGER NOT NULL DEFAULT 6, pult VARCHAR(10) NOT NULL DEFAULT 'vorne',
            belegung TEXT NOT NULL, ohne_tisch VARCHAR(2000) NOT NULL DEFAULT '[]', lehrer VARCHAR(50) NOT NULL DEFAULT '',
            geaendert VARCHAR(30) NOT NULL DEFAULT '', benutzer_id INTEGER, UNIQUE (bereich, schluessel)
        );
        INSERT INTO sitzplan_bereiche (bereich, schluessel, zeilen, spalten, belegung, geaendert)
            SELECT 'klasse', klasse, zeilen, spalten, belegung, geaendert FROM sitzplaene;
        """,

        // 13: Sprechtag (Elternsprechtag) – Sprechtage mit Zeitfenster, Termindauer, Pausen (JSON), Einladungs- und Anmeldezeitraum,
        //    beteiligte Lehrkräfte mit Raum, gesperrte Zeitfenster, Einladungen und Termine (ein Termin je Zeitfenster und je Lehrkraft
        //    und Kind) – nur Standard-SQL
        """
        CREATE TABLE sprechtage (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name VARCHAR(200) NOT NULL, datum VARCHAR(10) NOT NULL, beginn VARCHAR(5) NOT NULL,
            ende VARCHAR(5) NOT NULL, dauer INTEGER NOT NULL DEFAULT 10, pausen VARCHAR(1000) NOT NULL DEFAULT '[]',
            einladung_bis VARCHAR(16) NOT NULL DEFAULT '', anmeldung_von VARCHAR(16) NOT NULL, anmeldung_bis VARCHAR(16) NOT NULL,
            sichtbar INTEGER NOT NULL DEFAULT 0, raumwahl INTEGER NOT NULL DEFAULT 0, hinweis VARCHAR(2000) NOT NULL DEFAULT '',
            erstellt VARCHAR(30) NOT NULL DEFAULT '', geaendert VARCHAR(30) NOT NULL DEFAULT ''
        );
        CREATE TABLE sprechtag_lehrer (
            sprechtag_id INTEGER NOT NULL REFERENCES sprechtage(id) ON DELETE CASCADE, lehrer VARCHAR(50) NOT NULL,
            raum VARCHAR(50) NOT NULL DEFAULT '', PRIMARY KEY (sprechtag_id, lehrer)
        );
        CREATE TABLE sprechtag_sperren (
            id INTEGER PRIMARY KEY AUTOINCREMENT, sprechtag_id INTEGER NOT NULL REFERENCES sprechtage(id) ON DELETE CASCADE,
            lehrer VARCHAR(50) NOT NULL, beginn VARCHAR(5) NOT NULL, UNIQUE (sprechtag_id, lehrer, beginn)
        );
        CREATE TABLE sprechtag_einladungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, sprechtag_id INTEGER NOT NULL REFERENCES sprechtage(id) ON DELETE CASCADE,
            lehrer VARCHAR(50) NOT NULL, schueler_id INTEGER NOT NULL, text VARCHAR(1000) NOT NULL DEFAULT '',
            erstellt VARCHAR(30) NOT NULL DEFAULT '', benachrichtigt INTEGER NOT NULL DEFAULT 0, UNIQUE (sprechtag_id, lehrer, schueler_id)
        );
        CREATE TABLE sprechtag_termine (
            id INTEGER PRIMARY KEY AUTOINCREMENT, sprechtag_id INTEGER NOT NULL REFERENCES sprechtage(id) ON DELETE CASCADE,
            lehrer VARCHAR(50) NOT NULL, beginn VARCHAR(5) NOT NULL, schueler_id INTEGER NOT NULL, benutzer_id INTEGER,
            bemerkung VARCHAR(500) NOT NULL DEFAULT '', quelle VARCHAR(20) NOT NULL DEFAULT 'eltern', erstellt VARCHAR(30) NOT NULL DEFAULT '',
            UNIQUE (sprechtag_id, lehrer, beginn), UNIQUE (sprechtag_id, lehrer, schueler_id)
        );
        CREATE INDEX ix_sprechtag_termine_schueler ON sprechtag_termine (sprechtag_id, schueler_id);
        """,

        // 14: Mitteilungen – Lesebestätigung (angefordert bzw. gesendet), Antwort/Weiterleitung, Empfänger in Worten; Sprechtag – Absage des
        //    ganzen Sprechtags bzw. der Teilnahme einer Lehrkraft (mit Grund) – nur Standard-SQL
        """
        ALTER TABLE mitteilungen ADD COLUMN lesebestaetigung INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE mitteilungen ADD COLUMN antwort_auf INTEGER;
        ALTER TABLE mitteilungen ADD COLUMN empfaenger_text VARCHAR(1000) NOT NULL DEFAULT '';
        ALTER TABLE mitteilung_empfaenger ADD COLUMN bestaetigt VARCHAR(30);
        CREATE INDEX ix_mitteilung_empfaenger_benutzer ON mitteilung_empfaenger (benutzer_id, geloescht);
        ALTER TABLE sprechtage ADD COLUMN abgesagt VARCHAR(30) NOT NULL DEFAULT '';
        ALTER TABLE sprechtage ADD COLUMN absage_grund VARCHAR(1000) NOT NULL DEFAULT '';
        ALTER TABLE sprechtag_lehrer ADD COLUMN abgesagt VARCHAR(30) NOT NULL DEFAULT '';
        ALTER TABLE sprechtag_lehrer ADD COLUMN absage_grund VARCHAR(1000) NOT NULL DEFAULT '';
        """,

        // 15: Rechte-Modell – Rechte-Profile (Rollen; Systemrollen nur mit geänderten Rechten), Profil und Überschreibungen je Konto;
        //    Schülerdaten für die Web-Verwaltung und den CSV-Import (externe Nummer, Eintritt/Austritt), Telefon der Erziehungsberechtigten – nur Standard-SQL
        """
        CREATE TABLE rechteprofile (
            id VARCHAR(60) NOT NULL PRIMARY KEY, name VARCHAR(100) NOT NULL, basis VARCHAR(20) NOT NULL, beschreibung VARCHAR(500) NOT NULL DEFAULT '',
            rechte TEXT NOT NULL, geaendert VARCHAR(30) NOT NULL DEFAULT ''
        );
        CREATE TABLE benutzer_rechte (
            benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE, recht VARCHAR(60) NOT NULL,
            stufe INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (benutzer_id, recht)
        );
        ALTER TABLE benutzer ADD COLUMN rechteprofil VARCHAR(60) NOT NULL DEFAULT '';
        ALTER TABLE schueler_daten ADD COLUMN extern_id VARCHAR(50) NOT NULL DEFAULT '';
        ALTER TABLE schueler_daten ADD COLUMN eintritt VARCHAR(10) NOT NULL DEFAULT '';
        ALTER TABLE schueler_daten ADD COLUMN austritt VARCHAR(10) NOT NULL DEFAULT '';
        ALTER TABLE personen ADD COLUMN telefon VARCHAR(100) NOT NULL DEFAULT '';
        """,

        // 16: „Findet online statt“ – Link zum Online-Unterricht (Videokonferenz) je Klassenbucheintrag
        """
        ALTER TABLE klassenbuch ADD COLUMN online_link VARCHAR(1000) NOT NULL DEFAULT '';
        """,

        // 17: Sofort-Benachrichtigungen (iOS) – Gerätetokens je Konto (ein Token kann mehreren Konten gehören) – nur Standard-SQL
        """
        CREATE TABLE push_geraete (
            benutzer_id INTEGER NOT NULL REFERENCES benutzer(id) ON DELETE CASCADE, token VARCHAR(200) NOT NULL,
            plattform VARCHAR(20) NOT NULL DEFAULT 'ios', umgebung VARCHAR(20) NOT NULL DEFAULT 'production', bundle VARCHAR(200) NOT NULL DEFAULT '',
            erstellt VARCHAR(30) NOT NULL DEFAULT '', aktualisiert VARCHAR(30) NOT NULL DEFAULT '', PRIMARY KEY (benutzer_id, token)
        );
        """,

        // 18: Planfenster je Rolle – wie viele Wochen zurück bzw. voraus der Stundenplan angezeigt wird (NULL = unbegrenzt) – nur Standard-SQL
        """
        ALTER TABLE rechteprofile ADD COLUMN plan_wochen_zurueck INTEGER;
        ALTER TABLE rechteprofile ADD COLUMN plan_wochen_vor INTEGER;
        """,

        // 19: Notenschemen (Noten mit optionalem Wert als JSON) und Prüfungsarten (Höchstzahlen je Schüler*in, Farben, Notenschema, Gewichtung);
        //    Grundbestand der bisher fest hinterlegten Prüfungsarten – nur Standard-SQL
        Nr(19, """
        CREATE TABLE notenschemen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name VARCHAR(100) NOT NULL, aktiv INTEGER NOT NULL DEFAULT 1, reihenfolge INTEGER NOT NULL DEFAULT 0,
            noten TEXT NOT NULL
        );
        CREATE TABLE pruefungsarten (
            id INTEGER PRIMARY KEY AUTOINCREMENT, kurz VARCHAR(20) NOT NULL, name VARCHAR(100) NOT NULL DEFAULT '', aktiv INTEGER NOT NULL DEFAULT 1,
            muendlich INTEGER NOT NULL DEFAULT 0, angekuendigt INTEGER NOT NULL DEFAULT 1, uebergeben INTEGER NOT NULL DEFAULT 1,
            vordergrund VARCHAR(7) NOT NULL DEFAULT '#000000', hintergrund VARCHAR(7) NOT NULL DEFAULT '#ffffff',
            max_tag INTEGER NOT NULL DEFAULT 0, max_woche INTEGER NOT NULL DEFAULT 0, max_zeitraum INTEGER NOT NULL DEFAULT 0, zeitraum_tage INTEGER NOT NULL DEFAULT 0,
            gruppierung INTEGER NOT NULL DEFAULT 0, freie_tage INTEGER NOT NULL DEFAULT 0, mit_wochenende INTEGER NOT NULL DEFAULT 1, notenschema_id INTEGER,
            gewichtung REAL NOT NULL DEFAULT 1, reihenfolge INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO pruefungsarten (kurz, name, reihenfolge) VALUES ('KA', 'Klassenarbeit', 10);
        INSERT INTO pruefungsarten (kurz, name, reihenfolge) VALUES ('KL', 'Klausur', 20);
        INSERT INTO pruefungsarten (kurz, name, reihenfolge) VALUES ('T', 'Test', 30);
        INSERT INTO pruefungsarten (kurz, name, reihenfolge) VALUES ('AP', 'Abschlussprüfung', 40);
        INSERT INTO pruefungsarten (kurz, name, muendlich, reihenfolge) VALUES ('PR', 'Präsentation', 1, 50);
        INSERT INTO pruefungsarten (kurz, name, muendlich, reihenfolge) VALUES ('REF', 'Referat', 1, 60);
        INSERT INTO pruefungsarten (kurz, name, reihenfolge) VALUES ('VA', 'Vergleichsarbeit', 70);
        """),

        // 20: Zeitabhängige Klassenzugehörigkeit (Klassenwechsel ab Datum), Schülergruppen je Unterricht (Standardgruppe mit Abweichungen,
        //    befristete Gast-Schülergruppen) mit Mitgliedern von/bis, Teile je Übertragung („Über UntPlan“) – nur Standard-SQL
        Nr(20, """
        CREATE TABLE klassenwechsel (
            id INTEGER PRIMARY KEY AUTOINCREMENT, schueler_id INTEGER NOT NULL, klasse VARCHAR(50) NOT NULL, klasse_vorher VARCHAR(50) NOT NULL DEFAULT '',
            von VARCHAR(10) NOT NULL, erstellt VARCHAR(30) NOT NULL DEFAULT '', benutzer_id INTEGER, UNIQUE (schueler_id, von)
        );
        CREATE TABLE schuelergruppen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, unterricht INTEGER NOT NULL, name VARCHAR(100) NOT NULL DEFAULT '', art VARCHAR(20) NOT NULL DEFAULT 'standard',
            von VARCHAR(10) NOT NULL DEFAULT '', bis VARCHAR(10) NOT NULL DEFAULT '', erstellt VARCHAR(30) NOT NULL DEFAULT '', benutzer_id INTEGER
        );
        CREATE INDEX ix_schuelergruppen_unterricht ON schuelergruppen (unterricht);
        CREATE TABLE schuelergruppe_mitglieder (
            gruppe_id INTEGER NOT NULL REFERENCES schuelergruppen(id) ON DELETE CASCADE, schueler_id INTEGER NOT NULL,
            von VARCHAR(10) NOT NULL DEFAULT '', bis VARCHAR(10) NOT NULL DEFAULT '', ausgeschlossen INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (gruppe_id, schueler_id)
        );
        ALTER TABLE paket_verlauf ADD COLUMN teile VARCHAR(200) NOT NULL DEFAULT '';
        """),

        // 21: Mitteilungen – „Antwort nicht erlaubt“ (Absender lässt keine Antwort zu) – nur Standard-SQL
        Nr(21, """
        ALTER TABLE mitteilungen ADD COLUMN keine_antwort INTEGER NOT NULL DEFAULT 0;
        """),

        // 22: Fachlehrer-Entschuldigungen – Entschuldigung je Fehlstunde (Status, Text, Lehrkraft, Rolle; Schlüssel Schüler*in, Datum, Stunde
        //    überdauert das Neuspeichern der Anwesenheit) und Stornierung übertragener Buchungen an UntPlan – nur Standard-SQL
        Nr(22, """
        CREATE TABLE fehlzeit_entschuldigungen (
            id INTEGER PRIMARY KEY AUTOINCREMENT, schueler_id INTEGER NOT NULL, datum VARCHAR(10) NOT NULL, stunde INTEGER NOT NULL,
            status VARCHAR(20) NOT NULL DEFAULT 'offen', text VARCHAR(1000) NOT NULL DEFAULT '', lehrer VARCHAR(60) NOT NULL DEFAULT '',
            rolle VARCHAR(20) NOT NULL DEFAULT 'fachlehrer', entschuldigt_am VARCHAR(10) NOT NULL DEFAULT '', erstellt VARCHAR(30) NOT NULL DEFAULT '',
            benutzer_id INTEGER, UNIQUE (schueler_id, datum, stunde)
        );
        ALTER TABLE buchungen ADD COLUMN storno_uebertragen VARCHAR(30);
        """),

        // 23: Sprechtag – Ort, Optionen (JSON: Puffer, Höchstzahl je Familie, Warteliste, Erinnerung, Sichtbarkeit, Ankündigung, Reihe),
        //    Termindauer/Online-Link je Lehrkraft, Erinnerungsvermerk je Termin, Warteliste; öffentliche Hinweise für die Anmeldeseite – nur Standard-SQL
        Nr(23, """
        ALTER TABLE sprechtage ADD COLUMN ort VARCHAR(300) NOT NULL DEFAULT '';
        ALTER TABLE sprechtage ADD COLUMN optionen VARCHAR(2000) NOT NULL DEFAULT '';
        ALTER TABLE sprechtag_lehrer ADD COLUMN dauer INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE sprechtag_lehrer ADD COLUMN online INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE sprechtag_lehrer ADD COLUMN link VARCHAR(500) NOT NULL DEFAULT '';
        ALTER TABLE sprechtag_termine ADD COLUMN erinnert VARCHAR(30) NOT NULL DEFAULT '';
        CREATE TABLE sprechtag_warteliste (
            id INTEGER PRIMARY KEY AUTOINCREMENT, sprechtag_id INTEGER NOT NULL REFERENCES sprechtage(id) ON DELETE CASCADE,
            lehrer VARCHAR(50) NOT NULL, schueler_id INTEGER NOT NULL, benutzer_id INTEGER, erstellt VARCHAR(30) NOT NULL DEFAULT '',
            benachrichtigt VARCHAR(30) NOT NULL DEFAULT '', UNIQUE (sprechtag_id, lehrer, schueler_id)
        );
        CREATE TABLE oeffentliche_hinweise (
            id INTEGER PRIMARY KEY AUTOINCREMENT, titel VARCHAR(120) NOT NULL, text VARCHAR(1000) NOT NULL DEFAULT '',
            von VARCHAR(10) NOT NULL DEFAULT '', bis VARCHAR(10) NOT NULL DEFAULT '', aktiv INTEGER NOT NULL DEFAULT 1,
            reihenfolge INTEGER NOT NULL DEFAULT 0, erstellt VARCHAR(30) NOT NULL DEFAULT '', geaendert VARCHAR(30) NOT NULL DEFAULT ''
        );
        """),

        // 28: Buchungsmodul – Standort mobiler Ressourcen (Ausgabe-/Lagerort) und Symbol je Ressourcenart – nur Standard-SQL
        Nr(28, """
        ALTER TABLE ressourcen ADD COLUMN standort VARCHAR(100) NOT NULL DEFAULT '';
        ALTER TABLE ressourcenarten ADD COLUMN symbol VARCHAR(40) NOT NULL DEFAULT '';
        """),
    };


    private static void Migrieren(DbConnection c)
    {
        if (c is MySqlConnection)
        {
            // mehrere Instanzen gleichzeitig: nur eine migriert
            Wert(c, "SELECT GET_LOCK('untplanweb_migration', 120)");
            try { MigrierenMySql(c); }
            finally { Wert(c, "SELECT RELEASE_LOCK('untplanweb_migration')"); }
            return;
        }
        Ausfuehren(c, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        var stand = Ausgefuehrt(c);
        foreach (var (nummer, skript) in Nummeriert.Where(m => !stand.Contains(m.Nummer)))
        {
            using var t = c.BeginTransaction();
            Ausfuehren(c, skript);
            Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", P("@v", nummer));
            t.Commit();
        }
    }

    /// <summary>
    /// MySQL/MariaDB: jede Migration wird übersetzt und Befehl für Befehl ausgeführt. DDL-Befehle schließen dort Transaktionen
    /// implizit ab; daher vor Updates sichern (README). Der Stand wird nach jeder vollständigen Migration vermerkt.
    /// </summary>
    private static void MigrierenMySql(DbConnection c)
    {
        Ausfuehren(c, $"CREATE TABLE IF NOT EXISTS schema_version (version BIGINT NOT NULL) {MySqlDialekt.Tabellenoptionen}");
        var stand = Ausgefuehrt(c);
        var dialekt = (MySqlDialekt)Dialekt.MySql;
        foreach (var (nummer, skript) in Nummeriert.Where(m => !stand.Contains(m.Nummer)))
        {
            var befehle = dialekt.MigrationUebersetzen(skript,
                (tabelle, spalte) => Wert(c, "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @s",
                    P("@t", tabelle), P("@s", spalte)) as string,
                index => Wert(c, "SELECT TABLE_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = @i LIMIT 1", P("@i", index)) as string);
            foreach (var b in befehle)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = b;
                try { cmd.ExecuteNonQuery(); }
                catch (MySqlException ex) { throw new InvalidOperationException($"Migration {nummer} fehlgeschlagen ({ex.Message}): {b}", ex); }
            }
            Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", P("@v", nummer));
        }
    }

    /// <summary>Für Tests und die Dokumentation: die Migrationen im SQLite-Dialekt, aufsteigend nach Nummer (Index i = Nummer i + 1, solange lückenlos).</summary>
    public static IReadOnlyList<string> MigrationsSkripte => Nummeriert.Select(m => m.Skript).ToArray();

    /// <summary>Für Tests: die Migrationsnummern aufsteigend.</summary>
    public static IReadOnlyList<int> Migrationsnummern => Nummeriert.Select(m => m.Nummer).ToArray();
}

public static class LeserErweiterungen
{
    public static string Text(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? "" : Convert.ToString(r[spalte]) ?? "";
    public static string? TextOderNull(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToString(r[spalte]);
    public static int Zahl(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? 0 : Convert.ToInt32(r[spalte]);
    public static int? ZahlOderNull(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToInt32(r[spalte]);
    public static bool Wahr(this DbDataReader r, string spalte) => r.Zahl(spalte) != 0;
}
