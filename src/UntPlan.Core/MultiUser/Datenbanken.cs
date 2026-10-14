using System.Data.Common;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace UntPlan.Core.MultiUser;

/// <summary>Unterstützte Datenbanksysteme für den MultiUser-Betrieb.</summary>
public enum DatenbankArt
{
    SQLite,
    SqlServer,
    MySql,
    Access,
}

/// <summary>Verbindungsangaben zur MultiUser-Datenbank (Server, Datenbank, Anmeldung bzw. Datei).</summary>
public sealed class Verbindungsdaten
{
    public DatenbankArt Art { get; set; } = DatenbankArt.SQLite;
    public string Server { get; set; } = "";
    public int Port { get; set; }
    public string Datenbank { get; set; } = "UntPlan";
    public string Benutzer { get; set; } = "";
    /// <summary>Datenbank-Passwort (wird nicht im Klartext gespeichert, siehe <see cref="MultiUserEinstellungen"/>).</summary>
    [JsonIgnore]
    public string Passwort { get; set; } = "";
    /// <summary>SQL Server: Windows-Authentifizierung verwenden.</summary>
    public bool IntegrierteSicherheit { get; set; } = true;
    /// <summary>Datenbankdatei (SQLite, Access).</summary>
    public string Datei { get; set; } = "";

    public bool IstDateiDatenbank => Art is DatenbankArt.SQLite or DatenbankArt.Access;

    public override string ToString() => Art switch
    {
        DatenbankArt.SQLite => $"SQLite: {Datei}",
        DatenbankArt.Access => $"Access: {Datei}",
        DatenbankArt.SqlServer => $"SQL Server: {Server}/{Datenbank}",
        _ => $"MySQL/MariaDB: {Server}/{Datenbank}",
    };

    public static string ArtText(DatenbankArt a) => a switch
    {
        DatenbankArt.SQLite => "SQLite (Datei, z. B. auf einem Netzlaufwerk)",
        DatenbankArt.SqlServer => "Microsoft SQL Server / SQL Server Express",
        DatenbankArt.MySql => "MySQL / MariaDB",
        _ => "Microsoft Access (nur Windows)",
    };
}

/// <summary>SQL-Unterschiede der Datenbanksysteme (Datentypen, Autowert, Parameter).</summary>
public abstract class SqlDialekt
{
    /// <summary>Spaltendefinition eines automatisch hochgezählten Primärschlüssels.</summary>
    public abstract string AutoId { get; }
    public abstract string LangerText { get; }
    public virtual string Text(int laenge) => $"VARCHAR({laenge})";
    public virtual string Ganzzahl => "INT";
    /// <summary>Abfrage der zuletzt vergebenen Autowert-Nummer.</summary>
    public abstract string LetzteId { get; }
    /// <summary>Darf die Abfrage der Autowert-Nummer im selben Befehl wie das INSERT stehen?</summary>
    public virtual bool LetzteIdImSelbenBefehl => true;
    /// <summary>Parameter werden positionsbezogen mit "?" übergeben (OLE DB).</summary>
    public virtual bool PositionsParameter => false;
}

public sealed class SqliteDialekt : SqlDialekt
{
    public override string AutoId => "INTEGER PRIMARY KEY AUTOINCREMENT";
    public override string LangerText => "TEXT";
    public override string Ganzzahl => "INTEGER";
    public override string LetzteId => "SELECT last_insert_rowid()";
    public override bool LetzteIdImSelbenBefehl => false;
}

public sealed class SqlServerDialekt : SqlDialekt
{
    public override string AutoId => "INT IDENTITY(1,1) PRIMARY KEY";
    public override string LangerText => "NVARCHAR(MAX)";
    public override string Text(int laenge) => $"NVARCHAR({laenge})";
    public override string LetzteId => "SELECT CAST(SCOPE_IDENTITY() AS INT)";
}

public sealed class MySqlDialekt : SqlDialekt
{
    public override string AutoId => "INT AUTO_INCREMENT PRIMARY KEY";
    public override string LangerText => "LONGTEXT";
    public override string LetzteId => "SELECT LAST_INSERT_ID()";
    public override bool LetzteIdImSelbenBefehl => false;
}

public sealed class AccessDialekt : SqlDialekt
{
    public override string AutoId => "COUNTER PRIMARY KEY";
    public override string LangerText => "LONGTEXT";
    public override string Text(int laenge) => $"TEXT({Math.Min(laenge, 255)})";
    public override string Ganzzahl => "INTEGER";
    public override string LetzteId => "SELECT @@IDENTITY";
    public override bool LetzteIdImSelbenBefehl => false;
    public override bool PositionsParameter => true;
}

/// <summary>Anbieter einer MultiUser-Datenbank: liefert geöffnete Verbindungen und den SQL-Dialekt.</summary>
public interface IMultiUserDatenbank
{
    DatenbankArt Art { get; }
    SqlDialekt Dialekt { get; }
    Verbindungsdaten Daten { get; }
    /// <summary>Öffnet eine neue Verbindung (Verbindungspooling übernimmt der Treiber).</summary>
    DbConnection Verbinden();
    /// <summary>Legt die (leere) Datenbank bzw. Datei an, falls sie noch nicht existiert.</summary>
    void DatenbankAnlegen();
}

/// <summary>Erzeugt den passenden Datenbankanbieter.</summary>
public static class MultiUserAnbieter
{
    public static IMultiUserDatenbank Erzeugen(Verbindungsdaten v) => v.Art switch
    {
        DatenbankArt.SQLite => new SqliteDatenbank(v),
        DatenbankArt.SqlServer => new SqlServerDatenbank(v),
        DatenbankArt.MySql => new MySqlDatenbank(v),
        _ => new AccessDatenbank(v),
    };
}

public sealed class SqliteDatenbank : IMultiUserDatenbank
{
    public SqliteDatenbank(Verbindungsdaten v) => Daten = v;
    public DatenbankArt Art => DatenbankArt.SQLite;
    public SqlDialekt Dialekt { get; } = new SqliteDialekt();
    public Verbindungsdaten Daten { get; }

    private string Verbindung => new SqliteConnectionStringBuilder
    {
        DataSource = Daten.Datei,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
        DefaultTimeout = 15,
    }.ToString();

    public DbConnection Verbinden()
    {
        if (string.IsNullOrWhiteSpace(Daten.Datei)) throw new MultiUserException("Es ist keine Datenbankdatei angegeben.");
        var c = new SqliteConnection(Verbindung);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 15000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void DatenbankAnlegen()
    {
        var ordner = Path.GetDirectoryName(Path.GetFullPath(Daten.Datei));
        if (!string.IsNullOrEmpty(ordner)) Directory.CreateDirectory(ordner);
        using var c = Verbinden();
    }
}

public sealed class SqlServerDatenbank : IMultiUserDatenbank
{
    public SqlServerDatenbank(Verbindungsdaten v) => Daten = v;
    public DatenbankArt Art => DatenbankArt.SqlServer;
    public SqlDialekt Dialekt { get; } = new SqlServerDialekt();
    public Verbindungsdaten Daten { get; }

    private string Verbindung(string datenbank)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Daten.Port > 0 ? $"{Daten.Server},{Daten.Port}" : Daten.Server,
            InitialCatalog = datenbank,
            IntegratedSecurity = Daten.IntegrierteSicherheit,
            TrustServerCertificate = true,
            ConnectTimeout = 10,
        };
        if (!Daten.IntegrierteSicherheit)
        {
            b.UserID = Daten.Benutzer;
            b.Password = Daten.Passwort;
        }
        return b.ToString();
    }

    public DbConnection Verbinden()
    {
        var c = new SqlConnection(Verbindung(Daten.Datenbank));
        c.Open();
        return c;
    }

    public void DatenbankAnlegen()
    {
        using var c = new SqlConnection(Verbindung("master"));
        c.Open();
        using var cmd = c.CreateCommand();
        var name = Daten.Datenbank.Replace("]", "]]");
        cmd.CommandText = $"IF DB_ID(@n) IS NULL CREATE DATABASE [{name}]";
        cmd.Parameters.AddWithValue("@n", Daten.Datenbank);
        cmd.ExecuteNonQuery();
    }
}

public sealed class MySqlDatenbank : IMultiUserDatenbank
{
    public MySqlDatenbank(Verbindungsdaten v) => Daten = v;
    public DatenbankArt Art => DatenbankArt.MySql;
    public SqlDialekt Dialekt { get; } = new MySqlDialekt();
    public Verbindungsdaten Daten { get; }

    private string Verbindung(bool mitDatenbank)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = Daten.Server,
            UserID = Daten.Benutzer,
            Password = Daten.Passwort,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 10,
            AllowUserVariables = true,
        };
        if (Daten.Port > 0) b.Port = (uint)Daten.Port;
        if (mitDatenbank) b.Database = Daten.Datenbank;
        return b.ToString();
    }

    public DbConnection Verbinden()
    {
        var c = new MySqlConnection(Verbindung(true));
        c.Open();
        return c;
    }

    public void DatenbankAnlegen()
    {
        using var c = new MySqlConnection(Verbindung(false));
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{Daten.Datenbank.Replace("`", "``")}` CHARACTER SET utf8mb4";
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Microsoft Access über OLE DB (ACE-Treiber). Nur unter Windows verfügbar.</summary>
public sealed class AccessDatenbank : IMultiUserDatenbank
{
    public AccessDatenbank(Verbindungsdaten v) => Daten = v;
    public DatenbankArt Art => DatenbankArt.Access;
    public SqlDialekt Dialekt { get; } = new AccessDialekt();
    public Verbindungsdaten Daten { get; }

    private string Verbindung => $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={Daten.Datei};" +
        (string.IsNullOrEmpty(Daten.Passwort) ? "" : $"Jet OLEDB:Database Password={Daten.Passwort};");

    public DbConnection Verbinden()
    {
        if (!OperatingSystem.IsWindows()) throw new MultiUserException("Microsoft Access steht nur unter Windows zur Verfügung.");
        var c = new System.Data.OleDb.OleDbConnection(Verbindung);
        c.Open();
        return c;
    }

    public void DatenbankAnlegen()
    {
        if (!OperatingSystem.IsWindows()) throw new MultiUserException("Microsoft Access steht nur unter Windows zur Verfügung.");
        if (File.Exists(Daten.Datei)) return;
        // Neue Datei über ADOX (COM) anlegen
        var typ = Type.GetTypeFromProgID("ADOX.Catalog")
                  ?? throw new MultiUserException("ADOX ist nicht installiert. Bitte legen Sie die Access-Datei mit Microsoft Access an.");
        var katalog = Activator.CreateInstance(typ)!;
        typ.InvokeMember("Create", BindingFlags.InvokeMethod, null, katalog, new object[] { Verbindung });
        var verbindung = typ.InvokeMember("ActiveConnection", BindingFlags.GetProperty, null, katalog, null);
        verbindung?.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, verbindung, null);
    }
}
