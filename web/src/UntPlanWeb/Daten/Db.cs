using Microsoft.Data.Sqlite;

namespace UntPlanWeb.Daten;

/// <summary>
/// SQLite-Datenbank im Datenordner (Docker: Volume /data). Keine Einrichtung nötig: Die Datei wird beim ersten Start
/// angelegt, das Schema über nummerierte Migrationen aktuell gehalten (Tabelle schema_version).
/// </summary>
public sealed class Db
{
    private readonly string _verbindung;

    public string Datei { get; }

    public Db(string ordner)
    {
        Directory.CreateDirectory(ordner);
        Datei = Path.Combine(ordner, "untplanweb.db");
        _verbindung = new SqliteConnectionStringBuilder { DataSource = Datei, Cache = SqliteCacheMode.Shared, Pooling = true }.ToString();
        using var c = Oeffnen();
        Ausfuehren(c, "PRAGMA journal_mode=WAL;");
        Migrieren(c);
    }

    public SqliteConnection Oeffnen()
    {
        var c = new SqliteConnection(_verbindung);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public static (string, object?) P(string name, object? wert) => (name, wert);

    private static SqliteCommand Befehl(SqliteConnection c, string sql, (string, object?)[] p)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, w) in p) cmd.Parameters.AddWithValue(n, w switch
        {
            null => DBNull.Value,
            bool b => b ? 1 : 0,
            DateTime dt => dt.ToString("O"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            _ => w,
        });
        return cmd;
    }

    public static int Ausfuehren(SqliteConnection c, string sql, params (string, object?)[] p)
    {
        using var cmd = Befehl(c, sql, p);
        return cmd.ExecuteNonQuery();
    }

    public int Ausfuehren(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        return Ausfuehren(c, sql, p);
    }

    public long Einfuegen(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        using var cmd = Befehl(c, sql + "; SELECT last_insert_rowid();", p);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public List<T> Liste<T>(string sql, Func<SqliteDataReader, T> lesen, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        using var cmd = Befehl(c, sql, p);
        using var r = cmd.ExecuteReader();
        var res = new List<T>();
        while (r.Read()) res.Add(lesen(r));
        return res;
    }

    public T? Einzel<T>(string sql, Func<SqliteDataReader, T> lesen, params (string, object?)[] p) where T : class =>
        Liste(sql, lesen, p).FirstOrDefault();

    public object? Wert(string sql, params (string, object?)[] p)
    {
        using var c = Oeffnen();
        using var cmd = Befehl(c, sql, p);
        var w = cmd.ExecuteScalar();
        return w is DBNull ? null : w;
    }

    /// <summary>Mehrere Befehle in einer Transaktion.</summary>
    public void Transaktion(Action<SqliteConnection> aktion)
    {
        using var c = Oeffnen();
        using var t = c.BeginTransaction();
        aktion(c);
        t.Commit();
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
    };

    private static void Migrieren(SqliteConnection c)
    {
        Ausfuehren(c, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        var stand = Convert.ToInt32(cmd.ExecuteScalar());
        for (var i = stand; i < Migrationen.Length; i++)
        {
            using var t = c.BeginTransaction();
            Ausfuehren(c, Migrationen[i]);
            Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", P("@v", i + 1));
            t.Commit();
        }
    }
}

public static class LeserErweiterungen
{
    public static string Text(this SqliteDataReader r, string spalte) => r[spalte] is DBNull or null ? "" : Convert.ToString(r[spalte]) ?? "";
    public static string? TextOderNull(this SqliteDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToString(r[spalte]);
    public static int Zahl(this SqliteDataReader r, string spalte) => r[spalte] is DBNull or null ? 0 : Convert.ToInt32(r[spalte]);
    public static int? ZahlOderNull(this SqliteDataReader r, string spalte) => r[spalte] is DBNull or null ? null : Convert.ToInt32(r[spalte]);
    public static bool Wahr(this SqliteDataReader r, string spalte) => r.Zahl(spalte) != 0;
}
