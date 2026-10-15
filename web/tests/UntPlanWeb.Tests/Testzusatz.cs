using System.Collections.Concurrent;
using MySqlConnector;

namespace UntPlanWeb.Tests;

/// <summary>
/// Zusätzliche Konfiguration für <see cref="Testserver"/>:
/// <list type="bullet">
/// <item>MySQL/MariaDB: Ist <c>UNTPLANWEB_TEST_MYSQL</c> gesetzt (Verbindung mit Rechten zum Anlegen von Datenbanken, z. B.
///   <c>Server=127.0.0.1;User ID=root;Password=…</c>), laufen die MySQL-Tests; mit <c>UNTPLANWEB_TEST_ALLE_MIT_MYSQL=1</c> zusätzlich
///   alle Testserver gegen eine frische MySQL-Datenbank. Ohne Variable werden die MySQL-Tests übersprungen.</item>
/// <item>Weitere Einstellungen für einen Test (z. B. Update-Quelle) über <see cref="Mit"/>.</item>
/// </list>
/// </summary>
public static class Testzusatz
{
    public static string? MySqlAdmin => Environment.GetEnvironmentVariable("UNTPLANWEB_TEST_MYSQL") is { Length: > 0 } v ? v : null;
    private static bool AlleMitMySql => Environment.GetEnvironmentVariable("UNTPLANWEB_TEST_ALLE_MIT_MYSQL") is "1" or "true";

    private static readonly AsyncLocal<Dictionary<string, string?>?> Zusatz = new();
    private static readonly AsyncLocal<bool?> MySqlErzwingen = new();
    private static readonly ConcurrentDictionary<string, string> Datenbanken = new();

    /// <summary>Für den folgenden Testserver zusätzliche Einstellungen setzen (gilt im aktuellen asynchronen Ablauf).</summary>
    public static void Mit(Dictionary<string, string?> werte) => Zusatz.Value = werte;

    /// <summary>Folgende Testserver mit MySQL/MariaDB starten.</summary>
    public static void MitMySql() => MySqlErzwingen.Value = true;

    /// <summary>Folgende Testserver immer mit SQLite starten (auch mit UNTPLANWEB_TEST_ALLE_MIT_MYSQL).</summary>
    public static void OhneMySql() => MySqlErzwingen.Value = false;

    public static IEnumerable<KeyValuePair<string, string?>> Konfiguration(string ordner)
    {
        var res = new Dictionary<string, string?>(Zusatz.Value ?? new());
        if (MySqlAdmin != null && (MySqlErzwingen.Value ?? AlleMitMySql))
        {
            var (name, verbindung) = NeueDatenbank();
            Datenbanken[ordner] = name;
            res["UNTPLANWEB_DB"] = "mysql";
            res["UNTPLANWEB_DB_CONNECTION"] = verbindung;
        }
        return res;
    }

    public static void Aufraeumen(string ordner)
    {
        if (Datenbanken.TryRemove(ordner, out var name)) DatenbankLoeschen(name);
    }

    /// <summary>Frische, leere Datenbank anlegen; liefert Name und Verbindung.</summary>
    public static (string Name, string Verbindung) NeueDatenbank()
    {
        var name = "untplanweb_test_" + Guid.NewGuid().ToString("N")[..12];
        using (var c = new MySqlConnection(MySqlAdmin))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {name} CHARACTER SET utf8mb4 COLLATE utf8mb4_bin";
            cmd.ExecuteNonQuery();
        }
        var b = new MySqlConnectionStringBuilder(MySqlAdmin) { Database = name };
        return (name, b.ConnectionString);
    }

    public static void DatenbankLoeschen(string name)
    {
        MySqlConnection.ClearAllPools();
        using var c = new MySqlConnection(MySqlAdmin);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {name}";
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Test nur mit verfügbarer MySQL/MariaDB (UNTPLANWEB_TEST_MYSQL), sonst übersprungen.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (Testzusatz.MySqlAdmin is null) Skip = "Keine MySQL/MariaDB konfiguriert (UNTPLANWEB_TEST_MYSQL).";
    }
}
