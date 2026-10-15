using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace UntPlanWeb.Daten;

/// <summary>
/// Datenbankwahl über Umgebungsvariablen:
/// <list type="bullet">
/// <item><c>UNTPLANWEB_DB</c> = <c>sqlite</c> (Standard) oder <c>mysql</c>/<c>mariadb</c>.</item>
/// <item><c>UNTPLANWEB_DB_CONNECTION</c> = Verbindung zu MySQL/MariaDB, z. B. <c>Server=mariadb;Database=untplanweb;User ID=untplanweb;Password=…</c>
///   oder <c>mysql://untplanweb:passwort@mariadb:3306/untplanweb</c>.</item>
/// <item><c>UNTPLANWEB_DB_WARTEN</c> = Sekunden, die beim Start auf den Datenbankserver gewartet wird (Standard 60).</item>
/// </list>
/// </summary>
public sealed class Datenbankkonfiguration
{
    public Datenbankart Art { get; init; } = Datenbankart.Sqlite;
    public string? Verbindung { get; init; }
    public TimeSpan Warten { get; init; } = TimeSpan.FromSeconds(60);

    public static Datenbankkonfiguration Laden(IConfiguration c) => new()
    {
        Art = Art_(c["UNTPLANWEB_DB"]),
        Verbindung = string.IsNullOrWhiteSpace(c["UNTPLANWEB_DB_CONNECTION"]) ? null : c["UNTPLANWEB_DB_CONNECTION"]!.Trim(),
        Warten = TimeSpan.FromSeconds(int.TryParse(c["UNTPLANWEB_DB_WARTEN"], out var s) && s >= 0 ? s : 60),
    };

    public static Datenbankart Art_(string? wert) => (wert ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "sqlite" or "sqlite3" => Datenbankart.Sqlite,
        "mysql" or "mariadb" => Datenbankart.MySql,
        var x => throw new InvalidOperationException($"Unbekannte Datenbank UNTPLANWEB_DB={x} (erlaubt: sqlite, mysql, mariadb)."),
    };

    public Db Oeffnen(string datenordner) => new(Art, datenordner, Verbindung, Warten);
}

/// <summary>
/// Übernahme aller Daten einer Datenbank in eine andere (SQLite → MySQL/MariaDB). Beide Datenbanken werden vorher auf den
/// aktuellen Schemastand migriert; danach werden alle Tabellen in einer Transaktion zeilenweise kopiert (IDs bleiben erhalten).
/// Aufruf: <c>dotnet UntPlanWeb.dll --migrieren-nach-mysql [--quelle /data/untplanweb.db] [--ueberschreiben]</c>.
/// </summary>
public static class Datenmigration
{
    public const string Befehlsname = "--migrieren-nach-mysql";

    /// <summary>Kopiert alle Tabellen; liefert die Anzahl der Zeilen je Tabelle.</summary>
    public static Dictionary<string, int> Kopieren(Db quelle, Db ziel, bool ueberschreiben = false, Action<string>? log = null)
    {
        if (quelle.Version() != ziel.Version())
            throw new InvalidOperationException($"Unterschiedlicher Schemastand (Quelle {quelle.Version()}, Ziel {ziel.Version()}).");
        if (!ueberschreiben && !Leer(ziel))
            throw new InvalidOperationException("Die Zieldatenbank enthält bereits Daten (Benutzer, Einstellungen oder Paket). Zum Ersetzen --ueberschreiben angeben.");

        var zielTabellen = new HashSet<string>(ziel.Tabellen(), StringComparer.OrdinalIgnoreCase);
        var tabellen = quelle.Tabellen().Where(t => !t.Equals("schema_version", StringComparison.OrdinalIgnoreCase) && zielTabellen.Contains(t)).ToList();
        var res = new Dictionary<string, int>();

        using var qc = quelle.Oeffnen();
        using var zc = ziel.Oeffnen();
        FremdschluesselPruefen(zc, false);
        try
        {
            using var t = zc.BeginTransaction();
            // vorgegebene Zeilen der Migrationen (z. B. Abwesenheitsgründe) bzw. alte Daten entfernen
            foreach (var tab in tabellen) Db.Ausfuehren(zc, $"DELETE FROM {tab}");
            foreach (var tab in tabellen)
            {
                var zielSpalten = Spalten(zc, tab);
                using var lese = qc.CreateCommand();
                lese.CommandText = $"SELECT * FROM {tab}";
                using var r = lese.ExecuteReader();
                var spalten = Enumerable.Range(0, r.FieldCount).Select(r.GetName).Where(zielSpalten.Contains).ToList();
                var sql = $"INSERT INTO {tab} ({string.Join(", ", spalten)}) VALUES ({string.Join(", ", spalten.Select((_, i) => "@p" + i))})";
                var n = 0;
                while (r.Read())
                {
                    var werte = spalten.Select((s, i) => Db.P("@p" + i, r[s] is DBNull ? null : r[s])).ToArray();
                    Db.Ausfuehren(zc, sql, werte);
                    n++;
                }
                res[tab] = n;
                log?.Invoke($"  {tab}: {n} Zeilen");
            }
            t.Commit();
        }
        finally { FremdschluesselPruefen(zc, true); }
        return res;
    }

    /// <summary>Ziel gilt als leer, wenn noch keine Benutzer, Einstellungen oder Pakete vorhanden sind.</summary>
    public static bool Leer(Db db) =>
        new[] { "benutzer", "einstellungen", "paket" }.All(t => Convert.ToInt64(db.Wert($"SELECT COUNT(*) FROM {t}")) == 0);

    private static HashSet<string> Spalten(DbConnection c, string tabelle)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {tabelle} WHERE 1 = 0";
        using var r = cmd.ExecuteReader();
        return new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName), StringComparer.OrdinalIgnoreCase);
    }

    private static void FremdschluesselPruefen(DbConnection c, bool an)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = c is MySqlConnection ? $"SET FOREIGN_KEY_CHECKS = {(an ? 1 : 0)}" : $"PRAGMA foreign_keys = {(an ? "ON" : "OFF")}";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Kommandozeile: SQLite-Daten aus dem Datenordner nach MySQL/MariaDB (UNTPLANWEB_DB_CONNECTION) übernehmen.</summary>
    public static int Befehl(string[] args, IConfiguration konfig)
    {
        var ordner = konfig["UNTPLANWEB_DATEN"] ?? konfig["Datenordner"] ?? (Directory.Exists("/data") ? "/data" : "daten");
        var i = Array.IndexOf(args, "--quelle");
        var datei = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(ordner, "untplanweb.db");
        var verbindung = konfig["UNTPLANWEB_DB_CONNECTION"];
        try
        {
            if (!File.Exists(datei)) throw new InvalidOperationException($"SQLite-Datenbank {datei} nicht gefunden.");
            if (string.IsNullOrWhiteSpace(verbindung)) throw new InvalidOperationException("Bitte UNTPLANWEB_DB_CONNECTION (Ziel MySQL/MariaDB) setzen.");
            Console.WriteLine($"UntPlanWeb: Datenmigration {datei} → MySQL/MariaDB");
            var quelle = new Db(Path.GetDirectoryName(Path.GetFullPath(datei))!, Path.GetFileName(datei));
            var ziel = new Db(Datenbankart.MySql, ordner, verbindung,
                TimeSpan.FromSeconds(int.TryParse(konfig["UNTPLANWEB_DB_WARTEN"], out var s) ? s : 60));
            var ergebnis = Kopieren(quelle, ziel, args.Contains("--ueberschreiben"), Console.WriteLine);
            SqliteConnection.ClearAllPools();
            Console.WriteLine($"Fertig: {ergebnis.Values.Sum()} Zeilen in {ergebnis.Count} Tabellen übernommen (Schemastand {ziel.Version()}).");
            Console.WriteLine("Jetzt UNTPLANWEB_DB=mysql setzen und UntPlanWeb neu starten. Die SQLite-Datei bleibt als Sicherung erhalten.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MySqlException or SqliteException)
        {
            Console.Error.WriteLine("Fehler: " + ex.Message);
            return 1;
        }
    }
}

/// <summary>
/// Sicherung über die Kommandozeile (z. B. vor einem Update): <c>dotnet UntPlanWeb.dll --sichern</c> legt bei SQLite eine
/// konsistente Kopie unter &lt;Datenordner&gt;/sicherungen an. Für MySQL/MariaDB bitte <c>mariadb-dump</c> verwenden (update.sh).
/// </summary>
public static class Sicherung
{
    public const string Befehlsname = "--sichern";

    public static int Befehl(IConfiguration konfig)
    {
        var ordner = konfig["UNTPLANWEB_DATEN"] ?? konfig["Datenordner"] ?? (Directory.Exists("/data") ? "/data" : "daten");
        try
        {
            if (Datenbankkonfiguration.Laden(konfig).Art == Datenbankart.MySql)
            {
                Console.Error.WriteLine("MySQL/MariaDB: bitte mit mariadb-dump bzw. mysqldump sichern (siehe README, Abschnitt „Update“).");
                return 2;
            }
            var ziel = new Db(ordner).Sichern(Path.Combine(ordner, "sicherungen"));
            SqliteConnection.ClearAllPools();
            Console.WriteLine($"Sicherung erstellt: {ziel}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqliteException or IOException)
        {
            Console.Error.WriteLine("Fehler: " + ex.Message);
            return 1;
        }
    }
}
