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

    /// <summary>Anzahl der bekannten Migrationen (= Schemastand nach dem Start).</summary>
    public static int Schemastand => Migrationen.Length;

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
    // Migrationen: nur anhängen, nie ändern
    // ------------------------------------------------------------------------------------------

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

        // 4: Funktionen der WebUntis-Oberfläche – vorherige Anmeldung, Nachrichten mit Betreff, Klassendienste, Befreiungen,
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
        var stand = Convert.ToInt32(Wert(c, "SELECT COALESCE(MAX(version), 0) FROM schema_version"));
        for (var i = stand; i < Migrationen.Length; i++)
        {
            using var t = c.BeginTransaction();
            Ausfuehren(c, Migrationen[i]);
            Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", P("@v", i + 1));
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
        var stand = Convert.ToInt32(Wert(c, "SELECT COALESCE(MAX(version), 0) FROM schema_version"));
        var dialekt = (MySqlDialekt)Dialekt.MySql;
        for (var i = stand; i < Migrationen.Length; i++)
        {
            var befehle = dialekt.MigrationUebersetzen(Migrationen[i],
                (tabelle, spalte) => Wert(c, "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @s",
                    P("@t", tabelle), P("@s", spalte)) as string,
                index => Wert(c, "SELECT TABLE_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = @i LIMIT 1", P("@i", index)) as string);
            foreach (var b in befehle)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = b;
                try { cmd.ExecuteNonQuery(); }
                catch (MySqlException ex) { throw new InvalidOperationException($"Migration {i + 1} fehlgeschlagen ({ex.Message}): {b}", ex); }
            }
            Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", P("@v", i + 1));
        }
    }

    /// <summary>Für Tests und die Dokumentation: die Migrationen im SQLite-Dialekt.</summary>
    public static IReadOnlyList<string> MigrationsSkripte => Migrationen;
}

public static class LeserErweiterungen
{
    public static string Text(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? "" : Convert.ToString(r[spalte]) ?? "";
    public static string? TextOderNull(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToString(r[spalte]);
    public static int Zahl(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? 0 : Convert.ToInt32(r[spalte]);
    public static int? ZahlOderNull(this DbDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToInt32(r[spalte]);
    public static bool Wahr(this DbDataReader r, string spalte) => r.Zahl(spalte) != 0;
}
