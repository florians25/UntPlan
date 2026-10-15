using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using UntPlanWeb.Daten;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Datenbankschicht: Dialektübersetzung für MySQL/MariaDB, Update bestehender Datenbanken (Migrationen), Datenmigration
/// SQLite → SQLite (immer) und SQLite → MySQL/MariaDB (wenn UNTPLANWEB_TEST_MYSQL gesetzt ist).
/// </summary>
public class DatenbankTests
{
    private static string NeuerOrdner()
    {
        var o = Path.Combine(Path.GetTempPath(), "untplanweb-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(o);
        return o;
    }

    // ------------------------------------------------------------------------------ Dialekt

    [Fact]
    public void MySqlDialekt_uebersetzt_Upsert_Zeit_und_Kollation()
    {
        var d = Dialekt.MySql;
        var sql = d.Uebersetzen("INSERT INTO einstellungen (schluessel, wert) VALUES (@s, @w) ON CONFLICT(schluessel) DO UPDATE SET wert = excluded.wert");
        Assert.Equal("INSERT INTO einstellungen (schluessel, wert) VALUES (@s, @w) ON DUPLICATE KEY UPDATE wert = VALUES(wert)", sql);
        Assert.Contains("UTC_TIMESTAMP()", d.Uebersetzen("INSERT INTO monitore (erstellt) VALUES (datetime('now'))"));
        Assert.Equal("SELECT * FROM x WHERE k = @k COLLATE utf8mb4_unicode_ci", d.Uebersetzen("SELECT * FROM x WHERE k = @k COLLATE NOCASE"));
        Assert.Equal("INSERT IGNORE INTO t (a) VALUES (1)", d.Uebersetzen("INSERT OR IGNORE INTO t (a) VALUES (1)"));
        Assert.Equal("REPLACE INTO t (a) VALUES (1)", d.Uebersetzen("INSERT OR REPLACE INTO t (a) VALUES (1)"));
        Assert.Equal("INSERT IGNORE INTO t (a) VALUES (1)", d.Uebersetzen("INSERT INTO t (a) VALUES (1) ON CONFLICT DO NOTHING"));
        Assert.Equal("SELECT LAST_INSERT_ID()", d.Uebersetzen("SELECT last_insert_rowid()"));
        // SQLite bleibt unverändert
        Assert.Equal("SELECT datetime('now')", Dialekt.Sqlite.Uebersetzen("SELECT datetime('now')"));
    }

    [Fact]
    public void MySqlDialekt_uebersetzt_alle_Migrationen()
    {
        var d = (MySqlDialekt)Dialekt.MySql;
        var alle = Db.MigrationsSkripte.SelectMany(m => d.MigrationUebersetzen(m, (_, _) => null, _ => null)).ToList();
        var text = string.Join(";\n", alle);
        Assert.DoesNotContain("AUTOINCREMENT", text);
        Assert.DoesNotContain("NOCASE", text);
        Assert.Contains("id BIGINT PRIMARY KEY AUTO_INCREMENT", text);
        Assert.Contains("login VARCHAR(255) COLLATE utf8mb4_unicode_ci NOT NULL UNIQUE", text);
        Assert.Contains("FOREIGN KEY (absender_id) REFERENCES benutzer(id) ON DELETE CASCADE", text);
        Assert.Contains("json LONGTEXT NOT NULL", text);
        Assert.Contains("daten LONGBLOB NOT NULL", text);
        Assert.Contains("raeume LONGTEXT NOT NULL DEFAULT ('[]')", text);
        Assert.Contains("ENGINE=InnoDB", text);
        // Index auf einer bereits vorhandenen Textspalte (spätere Migration) bekommt eine Präfixlänge
        var idx = d.MigrationUebersetzen("CREATE INDEX ix_test ON mitteilungen (betreff, absender_id)", (t, s) => s == "betreff" ? "longtext" : "bigint", _ => null);
        Assert.Equal("CREATE INDEX ix_test ON mitteilungen (betreff(191), absender_id)", Assert.Single(idx));
        var drop = d.MigrationUebersetzen("DROP INDEX IF EXISTS ix_alt", (_, _) => null, i => i == "ix_alt" ? "vermerke" : null);
        Assert.Equal("DROP INDEX ix_alt ON vermerke", Assert.Single(drop));
    }

    [Fact]
    public void Konfiguration_waehlt_Datenbank()
    {
        Assert.Equal(Datenbankart.Sqlite, Datenbankkonfiguration.Art_(null));
        Assert.Equal(Datenbankart.MySql, Datenbankkonfiguration.Art_("MariaDB"));
        Assert.Throws<InvalidOperationException>(() => Datenbankkonfiguration.Art_("oracle"));
        var v = Db.MySqlVerbindung("mysql://untplanweb:geheim%21@mariadb:3307/untplanweb");
        Assert.Contains("Server=mariadb", v);
        Assert.Contains("Port=3307", v);
        Assert.Contains("Password=geheim!", v);
        Assert.Contains("Database=untplanweb", v);
        Assert.Throws<InvalidOperationException>(() => new Db(Datenbankart.MySql, NeuerOrdner(), null));
    }

    // ------------------------------------------------------------------------------ Update (Migrationen auf Bestandsdaten)

    [Fact]
    public void Update_einer_alten_SQLite_Datenbank_uebernimmt_Daten()
    {
        var ordner = NeuerOrdner();
        var datei = Path.Combine(ordner, "untplanweb.db");
        // Stand einer älteren Version: nur die ersten vier Migrationen, mit Hausaufgabe im Klassenbuch
        using (var c = new SqliteConnection($"Data Source={datei}"))
        {
            c.Open();
            Db.Ausfuehren(c, "CREATE TABLE schema_version (version INTEGER NOT NULL)");
            for (var i = 0; i < 4; i++)
            {
                Db.Ausfuehren(c, Db.MigrationsSkripte[i]);
                Db.Ausfuehren(c, "INSERT INTO schema_version (version) VALUES (@v)", Db.P("@v", i + 1));
            }
            Db.Ausfuehren(c, "INSERT INTO klassenbuch (datum, stunde, klassen, fach, lehrer, hausaufgabe, geaendert) VALUES ('2026-10-12', 1, '[\"5a\"]', 'M', 'MUE', 'S. 12 Nr. 3', '2026-10-12 08:00')");
        }
        SqliteConnection.ClearAllPools();

        var db = new Db(ordner);
        Assert.Equal(Db.Schemastand, db.Version());
        Assert.Equal("S. 12 Nr. 3", db.Wert("SELECT text FROM hausaufgaben_eintraege WHERE lehrer = 'MUE'"));
        // erneuter Start: nichts mehr zu tun
        Assert.Equal(Db.Schemastand, new Db(ordner).Version());
        SqliteConnection.ClearAllPools();
    }

    // ------------------------------------------------------------------------------ Datenmigration

    private static async Task<string> BestandAnlegenAsync(Testserver s)
    {
        var admin = await s.AlsAsync("admin");
        await s.AlsAsync("mue", "lehrer", "MUE");
        await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/mitteilungen", new { empfaenger = new[] { "gruppe:kollegium" }, betreff = "Grüße – äöüß", text = "Text mit Umlauten: Größe", wichtig = true }));
        s.Dienst<Monitordienst>().Speichern(null, "Foyer", "schueler", 2);
        return s.Dienst<Db>().Datei;
    }

    [Fact]
    public async Task Datenmigration_kopiert_alle_Tabellen()
    {
        await using var s = await Testserver.StartenAsync();
        await BestandAnlegenAsync(s);
        var quelle = s.Dienst<Db>();
        var ziel = new Db(NeuerOrdner());
        var ergebnis = Datenmigration.Kopieren(quelle, ziel);
        foreach (var t in quelle.Tabellen().Where(t => t != "schema_version"))
            Assert.Equal(Convert.ToInt64(quelle.Wert($"SELECT COUNT(*) FROM {t}")), Convert.ToInt64(ziel.Wert($"SELECT COUNT(*) FROM {t}")));
        Assert.True(ergebnis["benutzer"] >= 2);
        Assert.Equal("Grüße – äöüß", ziel.Wert("SELECT betreff FROM mitteilungen"));
        // zweiter Lauf ohne --ueberschreiben wird abgelehnt
        Assert.Throws<InvalidOperationException>(() => Datenmigration.Kopieren(quelle, ziel));
        Datenmigration.Kopieren(quelle, ziel, ueberschreiben: true);
        SqliteConnection.ClearAllPools();
    }

    [MySqlFact]
    public async Task MySql_Testserver_laeuft_mit_MariaDB()
    {
        Testzusatz.MitMySql();
        await using var s = await Testserver.StartenAsync();
        var db = s.Dienst<Db>();
        Assert.Equal(Datenbankart.MySql, db.Art);
        Assert.Equal(Db.Schemastand, db.Version());
        await BestandAnlegenAsync(s);
        var admin = await s.AlsAsync("admin");
        var eingang = await Testserver.HolenAsync(await s.AlsAsync("mue", "lehrer", "MUE"), "/api/app/mitteilungen");
        Assert.Contains("Grüße – äöüß", eingang.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        // Upsert (ON DUPLICATE KEY) über Einstellungen
        s.Dienst<Einstellungen>().Schreiben("test", "1");
        s.Dienst<Einstellungen>().Schreiben("test", "2");
        Assert.Equal("2", s.Dienst<Einstellungen>().Lesen("test"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/app/admin/update")).StatusCode);
    }

    [MySqlFact]
    public async Task Datenmigration_SQLite_nach_MySql_per_Befehl()
    {
        Testzusatz.OhneMySql();
        await using var s = await Testserver.StartenAsync();
        await BestandAnlegenAsync(s);
        var quelle = s.Dienst<Db>();
        var (name, verbindung) = Testzusatz.NeueDatenbank();
        try
        {
            var konfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UNTPLANWEB_DATEN"] = Path.GetDirectoryName(quelle.Datei),
                ["UNTPLANWEB_DB_CONNECTION"] = verbindung,
            }).Build();
            Assert.Equal(0, Datenmigration.Befehl(new[] { Datenmigration.Befehlsname }, konfig));
            var ziel = new Db(Datenbankart.MySql, NeuerOrdner(), verbindung);
            foreach (var t in quelle.Tabellen().Where(t => t != "schema_version"))
                Assert.Equal(Convert.ToInt64(quelle.Wert($"SELECT COUNT(*) FROM {t}")), Convert.ToInt64(ziel.Wert($"SELECT COUNT(*) FROM {t}")));
            Assert.Equal("Grüße – äöüß", ziel.Wert("SELECT betreff FROM mitteilungen"));
            // Anmeldung mit übernommenem Passwort-Hash, neue IDs setzen nach den übernommenen fort
            var bd = new Benutzerdienst(ziel, s.Dienst<Uhr>());
            Assert.NotNull(bd.NachLogin("mue"));
            var maxId = Convert.ToInt64(ziel.Wert("SELECT MAX(id) FROM benutzer"));
            Assert.True(bd.Anlegen("neu", "neues-passwort", "lehrer", "NEU", "Neu").Id > maxId);
            // zweiter Lauf: Ziel nicht leer → Fehler
            Assert.Equal(1, Datenmigration.Befehl(new[] { Datenmigration.Befehlsname }, konfig));
        }
        finally { Testzusatz.DatenbankLoeschen(name); }
    }
}
