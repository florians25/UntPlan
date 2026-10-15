using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace UntPlanWeb.Daten;

/// <summary>Unterstützte Datenbanken: SQLite (Standard, Tests) und MySQL/MariaDB (produktive Installationen).</summary>
public enum Datenbankart { Sqlite, MySql }

/// <summary>
/// Schlanke Provider-Abstraktion: Die Dienste und Migrationen schreiben SQL im SQLite-Dialekt; für MySQL/MariaDB übersetzt
/// <see cref="MySqlDialekt"/> die Unterschiede zentral an dieser einen Stelle:
/// <list type="bullet">
/// <item><c>INTEGER PRIMARY KEY AUTOINCREMENT</c> → <c>BIGINT AUTO_INCREMENT</c>, <c>TEXT</c> → <c>VARCHAR(255)</c> (Schlüssel/Index) bzw. <c>LONGTEXT</c>.</item>
/// <item><c>INSERT OR REPLACE/IGNORE</c>, <c>ON CONFLICT(..) DO UPDATE SET x = excluded.x</c> → <c>REPLACE</c>, <c>INSERT IGNORE</c>, <c>ON DUPLICATE KEY UPDATE x = VALUES(x)</c>.</item>
/// <item><c>datetime('now')</c>, <c>date('now')</c>, <c>last_insert_rowid()</c>, <c>COLLATE NOCASE</c>, <c>random()</c>, <c>CAST(.. AS TEXT/INTEGER)</c>.</item>
/// <item>Booleans werden als Zahl 0/1 gespeichert (Parameter in <see cref="Db"/>), <c>LIMIT</c> ist in beiden Dialekten gleich, <c>||</c> verkettet Texte (sql_mode PIPES_AS_CONCAT).</item>
/// </list>
/// Hinweis für neue SQL-Befehle: <c>LIKE</c> unterscheidet unter MySQL Groß-/Kleinschreibung (Kollation utf8mb4_bin) – bei Bedarf <c>COLLATE NOCASE</c> anhängen.
/// </summary>
public abstract class Dialekt
{
    public static readonly Dialekt Sqlite = new SqliteDialekt();
    public static readonly Dialekt MySql = new MySqlDialekt();

    public abstract Datenbankart Art { get; }

    /// <summary>SQL im SQLite-Dialekt für diese Datenbank umschreiben (zwischengespeichert).</summary>
    public abstract string Uebersetzen(string sql);

    /// <summary>Abfrage der zuletzt vergebenen ID in derselben Verbindung.</summary>
    public abstract string LetzteIdSql { get; }

    public static Dialekt Fuer(DbConnection c) => c is MySqlConnection ? MySql : Sqlite;
    public static Dialekt Fuer(Datenbankart art) => art == Datenbankart.MySql ? MySql : Sqlite;

    /// <summary>Skript in einzelne Befehle zerlegen (Semikolon außerhalb von Zeichenketten).</summary>
    public static List<string> Befehle(string skript)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var inText = false;
        foreach (var z in skript)
        {
            if (z == '\'') inText = !inText;
            if (z == ';' && !inText)
            {
                if (sb.ToString().Trim() is { Length: > 0 } b) res.Add(b);
                sb.Clear();
                continue;
            }
            sb.Append(z);
        }
        if (sb.ToString().Trim() is { Length: > 0 } rest) res.Add(rest);
        return res;
    }
}

public sealed class SqliteDialekt : Dialekt
{
    public override Datenbankart Art => Datenbankart.Sqlite;
    public override string Uebersetzen(string sql) => sql;
    public override string LetzteIdSql => "SELECT last_insert_rowid()";
}

public sealed class MySqlDialekt : Dialekt
{
    /// <summary>Kollation für Spalten und Vergleiche mit <c>COLLATE NOCASE</c>; sonst binär wie SQLite.</summary>
    public const string OhneGrossKlein = "utf8mb4_unicode_ci";
    public const string Tabellenoptionen = "ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin";
    /// <summary>Sitzungsmodus: streng, ohne ONLY_FULL_GROUP_BY (wie SQLite), <c>||</c> als Textverkettung.</summary>
    public const string SqlModus = "STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,PIPES_AS_CONCAT";
    private const int SchluesselLaenge = 255;
    private const int Praefix = 191;

    private static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly Regex InsertOrReplace = new(@"\bINSERT\s+OR\s+REPLACE\b", O);
    private static readonly Regex InsertOrIgnore = new(@"\bINSERT\s+OR\s+IGNORE\b", O);
    private static readonly Regex KonfliktNichts = new(@"\s*\bON\s+CONFLICT\s*(\([^)]*\))?\s*DO\s+NOTHING\b", O);
    private static readonly Regex KonfliktUpdate = new(@"\bON\s+CONFLICT\s*\([^)]*\)\s*DO\s+UPDATE\s+SET\b", O);
    private static readonly Regex Excluded = new(@"\bexcluded\.(\w+)", O);
    private static readonly Regex Nocase = new(@"\bCOLLATE\s+NOCASE\b", O);
    private static readonly Regex JetztLokal = new(@"\bdatetime\s*\(\s*'now'\s*,\s*'localtime'\s*\)", O);
    private static readonly Regex Jetzt = new(@"\bdatetime\s*\(\s*'now'\s*\)", O);
    private static readonly Regex HeuteLokal = new(@"\bdate\s*\(\s*'now'\s*,\s*'localtime'\s*\)", O);
    private static readonly Regex Heute = new(@"\bdate\s*\(\s*'now'\s*\)", O);
    private static readonly Regex LetzteId = new(@"\blast_insert_rowid\s*\(\s*\)", O);
    private static readonly Regex Zufall = new(@"\brandom\s*\(\s*\)", O);
    private static readonly Regex AlsText = new(@"\bAS\s+TEXT\s*\)", O);
    private static readonly Regex AlsZahl = new(@"\bAS\s+INTEGER\s*\)", O);
    private static readonly Regex Insert = new(@"^\s*INSERT\b", O);

    private readonly ConcurrentDictionary<string, string> _cache = new();

    public override Datenbankart Art => Datenbankart.MySql;
    public override string LetzteIdSql => "SELECT LAST_INSERT_ID()";

    public override string Uebersetzen(string sql) => _cache.GetOrAdd(sql, UebersetzenOhneCache);

    private static string UebersetzenOhneCache(string sql)
    {
        var s = InsertOrReplace.Replace(sql, "REPLACE");
        s = InsertOrIgnore.Replace(s, "INSERT IGNORE");
        if (KonfliktNichts.IsMatch(s)) s = Insert.Replace(KonfliktNichts.Replace(s, ""), "INSERT IGNORE", 1);
        s = KonfliktUpdate.Replace(s, "ON DUPLICATE KEY UPDATE");
        s = Excluded.Replace(s, "VALUES($1)");
        s = Nocase.Replace(s, "COLLATE " + OhneGrossKlein);
        s = JetztLokal.Replace(s, "DATE_FORMAT(NOW(), '%Y-%m-%d %H:%i:%s')");
        s = Jetzt.Replace(s, "DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:%i:%s')");
        s = HeuteLokal.Replace(s, "DATE_FORMAT(CURDATE(), '%Y-%m-%d')");
        s = Heute.Replace(s, "DATE_FORMAT(UTC_DATE(), '%Y-%m-%d')");
        s = LetzteId.Replace(s, "LAST_INSERT_ID()");
        s = Zufall.Replace(s, "RAND()");
        s = AlsText.Replace(s, "AS CHAR)");
        s = AlsZahl.Replace(s, "AS SIGNED)");
        return s;
    }

    // ------------------------------------------------------------------------------------------ Migrationen (DDL)

    private static readonly Regex CreateTable = new(@"^\s*CREATE\s+TABLE\s+(IF\s+NOT\s+EXISTS\s+)?[`""]?(\w+)[`""]?\s*\((.*)\)\s*(WITHOUT\s+ROWID|STRICT|\s)*$", O | RegexOptions.Singleline);
    private static readonly Regex AlterAdd = new(@"^\s*ALTER\s+TABLE\s+(\w+)\s+ADD\s+(COLUMN\s+)?(.*)$", O | RegexOptions.Singleline);
    private static readonly Regex CreateIndex = new(@"^\s*CREATE\s+(UNIQUE\s+)?INDEX\s+(IF\s+NOT\s+EXISTS\s+)?(\w+)\s+ON\s+(\w+)\s*\((.*)\)\s*$", O | RegexOptions.Singleline);
    private static readonly Regex DropIndex = new(@"^\s*DROP\s+INDEX\s+(IF\s+EXISTS\s+)?(\w+)\s*$", O);
    private static readonly Regex Pragma = new(@"^\s*PRAGMA\b", O);
    private static readonly Regex Referenz = new(@"\s*\bREFERENCES\s+(\w+)\s*\(([^)]*)\)((?:\s+ON\s+(?:DELETE|UPDATE)\s+(?:CASCADE|SET\s+NULL|SET\s+DEFAULT|RESTRICT|NO\s+ACTION))*)", O);
    private static readonly Regex Autoincrement = new(@"\bAUTOINCREMENT\b", O);
    private static readonly Regex Vorgabe = new(@"\bDEFAULT\s+('(?:[^']|'')*'|-?\d+(?:\.\d+)?)", O);
    private static readonly Regex SchluesselInline = new(@"\b(PRIMARY\s+KEY|UNIQUE)\b", O);
    private static readonly Regex Tabellenregel = new(@"^\s*(PRIMARY\s+KEY|UNIQUE|CHECK|FOREIGN\s+KEY|CONSTRAINT)\b", O);
    private static readonly Regex SpaltenListe = new(@"^\s*(?:PRIMARY\s+KEY|UNIQUE)\s*\(([^)]*)\)", O);

    /// <summary>
    /// Migrationsskript (SQLite-Dialekt) in MySQL-Befehle übersetzen. <paramref name="spaltentyp"/> liefert für bereits vorhandene
    /// Spalten den MySQL-Datentyp (information_schema), damit Indizes auf Textspalten eine Präfixlänge bekommen.
    /// <paramref name="indexVorhanden"/> beantwortet (Tabelle, Index) bzw. (null, Index) für DROP INDEX mit der Tabelle oder null.
    /// </summary>
    public List<string> MigrationUebersetzen(string skript, Func<string, string, string?> spaltentyp, Func<string, string?> indexTabelle)
    {
        var befehle = Befehle(skript);
        // Spalten, die in diesem Skript einen Index bekommen (→ VARCHAR statt LONGTEXT)
        var indiziert = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in befehle)
            if (CreateIndex.Match(b) is { Success: true } m)
                foreach (var sp in Spalten(m.Groups[5].Value)) indiziert.Add(m.Groups[4].Value + "." + sp);
        var neueTypen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var res = new List<string>();
        foreach (var b in befehle)
        {
            if (Pragma.IsMatch(b)) continue;
            if (CreateTable.Match(b) is { Success: true } ct)
            {
                res.Add(TabelleUebersetzen(ct.Groups[2].Value, ct.Groups[1].Success, ct.Groups[3].Value, indiziert, neueTypen));
                continue;
            }
            if (AlterAdd.Match(b) is { Success: true } aa)
            {
                var regeln = new List<string>();
                var spalte = SpalteUebersetzen(aa.Groups[1].Value, aa.Groups[3].Value.Trim(), indiziert, new HashSet<string>(), regeln, neueTypen);
                res.Add($"ALTER TABLE {aa.Groups[1].Value} ADD COLUMN {spalte}");
                res.AddRange(regeln.Select(r => $"ALTER TABLE {aa.Groups[1].Value} ADD {r}"));
                continue;
            }
            if (CreateIndex.Match(b) is { Success: true } ci)
            {
                var tabelle = ci.Groups[4].Value;
                if (ci.Groups[2].Success && indexTabelle(ci.Groups[3].Value) != null) continue;
                var spalten = string.Join(", ", TeileAufTiefe0(ci.Groups[5].Value).Select(s =>
                {
                    var t = s.Trim();
                    var name = Regex.Match(t, @"^[`""]?(\w+)").Groups[1].Value;
                    var rest = Nocase.Replace(t[Math.Min(t.Length, Regex.Match(t, @"^[`""]?\w+[`""]?").Length)..], "").Trim();
                    var typ = neueTypen.TryGetValue(tabelle + "." + name, out var nt) ? nt : spaltentyp(tabelle, name) ?? "";
                    var text = Regex.IsMatch(typ, "text|blob", RegexOptions.IgnoreCase);
                    return name + (text ? $"({Praefix})" : "") + (rest.Length > 0 ? " " + rest : "");
                }));
                res.Add($"CREATE {(ci.Groups[1].Success ? "UNIQUE " : "")}INDEX {ci.Groups[3].Value} ON {tabelle} ({spalten})");
                continue;
            }
            if (DropIndex.Match(b) is { Success: true } di)
            {
                var tabelle = indexTabelle(di.Groups[2].Value);
                if (tabelle != null) res.Add($"DROP INDEX {di.Groups[2].Value} ON {tabelle}");
                else if (!di.Groups[1].Success) throw new InvalidOperationException($"Index {di.Groups[2].Value} nicht gefunden.");
                continue;
            }
            res.Add(Uebersetzen(b));
        }
        return res;
    }

    private static string TabelleUebersetzen(string name, bool wennFehlt, string koerper, HashSet<string> indiziert, Dictionary<string, string> neueTypen)
    {
        var teile = TeileAufTiefe0(koerper).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        // Schlüsselspalten aus Tabellenregeln (PRIMARY KEY (..), UNIQUE (..))
        var schluessel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in teile)
            if (SpaltenListe.Match(t) is { Success: true } m)
                foreach (var s in Spalten(m.Groups[1].Value)) schluessel.Add(s);
        var regeln = new List<string>();
        var zeilen = new List<string>();
        foreach (var t in teile)
        {
            if (Tabellenregel.IsMatch(t)) { regeln.Add(Nocase.Replace(t, "")); continue; }
            zeilen.Add(SpalteUebersetzen(name, t, indiziert, schluessel, regeln, neueTypen));
        }
        return $"CREATE TABLE {(wennFehlt ? "IF NOT EXISTS " : "")}{name} (\n    {string.Join(",\n    ", zeilen.Concat(regeln))}\n) {Tabellenoptionen}";
    }

    private static string SpalteUebersetzen(string tabelle, string def, HashSet<string> indiziert, HashSet<string> schluessel, List<string> regeln, Dictionary<string, string> neueTypen)
    {
        var m = Regex.Match(def, @"^[`""]?(\w+)[`""]?\s*(.*)$", RegexOptions.Singleline);
        var name = m.Groups[1].Value;
        var rest = m.Groups[2].Value;
        var typ = "";
        var tm = Regex.Match(rest, @"^(?:(INTEGER|INT|BIGINT|BOOLEAN|BOOL|TEXT|BLOB|REAL|DOUBLE|FLOAT|NUMERIC)\b|((?:VAR)?CHAR\s*\(\s*\d+\s*\)))", RegexOptions.IgnoreCase);
        if (tm.Success) { typ = Regex.Replace(tm.Value.ToUpperInvariant(), @"\s+", ""); rest = rest[tm.Length..]; }

        var nocase = Nocase.IsMatch(rest);
        rest = Nocase.Replace(rest, "");
        if (Referenz.Match(rest) is { Success: true } r)
        {
            regeln.Add($"FOREIGN KEY ({name}) REFERENCES {r.Groups[1].Value}({r.Groups[2].Value.Trim()}){Regex.Replace(r.Groups[3].Value, @"\s+", " ")}");
            rest = Referenz.Replace(rest, "");
        }
        var auto = Autoincrement.IsMatch(rest);
        rest = Autoincrement.Replace(rest, "AUTO_INCREMENT");
        var istSchluessel = SchluesselInline.IsMatch(rest) || schluessel.Contains(name) || indiziert.Contains(tabelle + "." + name);

        string mysqlTyp;
        var langerText = false;
        switch (typ)
        {
            case "INTEGER" or "INT" or "BIGINT" or "BOOLEAN" or "BOOL": mysqlTyp = "BIGINT"; break;
            case "REAL" or "DOUBLE" or "FLOAT": mysqlTyp = "DOUBLE"; break;
            case "NUMERIC": mysqlTyp = "DECIMAL(20,6)"; break;
            case "BLOB": mysqlTyp = "LONGBLOB"; langerText = true; break;
            case var v when v.StartsWith("VARCHAR") || v.StartsWith("CHAR"): mysqlTyp = v; break;
            default:
                if (istSchluessel) mysqlTyp = $"VARCHAR({SchluesselLaenge})";
                else { mysqlTyp = "LONGTEXT"; langerText = true; }
                break;
        }
        if (auto && mysqlTyp != "BIGINT") mysqlTyp = "BIGINT";
        // MySQL 8 erlaubt Vorgaben für TEXT/BLOB nur als Ausdruck in Klammern (MariaDB versteht beides)
        if (langerText) rest = Vorgabe.Replace(rest, "DEFAULT ($1)");
        neueTypen[tabelle + "." + name] = mysqlTyp;
        var kollation = nocase && (mysqlTyp.Contains("CHAR") || mysqlTyp.Contains("TEXT")) ? $" COLLATE {OhneGrossKlein}" : "";
        return $"{name} {mysqlTyp}{kollation}{(rest.Trim().Length > 0 ? " " + Regex.Replace(rest.Trim(), @"\s+", " ") : "")}";
    }

    private static IEnumerable<string> Spalten(string liste) =>
        TeileAufTiefe0(liste).Select(s => Regex.Match(s.Trim(), @"^[`""]?(\w+)").Groups[1].Value).Where(s => s.Length > 0);

    /// <summary>Kommagetrennte Liste auf oberster Klammerebene zerlegen (Zeichenketten beachtet).</summary>
    private static List<string> TeileAufTiefe0(string s)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        int tiefe = 0;
        var inText = false;
        foreach (var z in s)
        {
            if (z == '\'') inText = !inText;
            if (!inText)
            {
                if (z == '(') tiefe++;
                else if (z == ')') tiefe--;
                else if (z == ',' && tiefe == 0) { res.Add(sb.ToString()); sb.Clear(); continue; }
            }
            sb.Append(z);
        }
        if (sb.Length > 0) res.Add(sb.ToString());
        return res;
    }
}
