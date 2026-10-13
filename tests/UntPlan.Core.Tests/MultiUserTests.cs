using UntPlan.Core.Model;
using UntPlan.Core.MultiUser;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>MultiUser-Betrieb mit einer temporären SQLite-Datenbank.</summary>
public sealed class MultiUserTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "untplan-mu-" + Guid.NewGuid().ToString("N"));
    private readonly MultiUserDb _db;

    public MultiUserTests()
    {
        Directory.CreateDirectory(_ordner);
        var v = new Verbindungsdaten { Art = DatenbankArt.SQLite, Datei = Path.Combine(_ordner, "untplan.db") };
        _db = new MultiUserDb(v) { AenderungenBehalten = 3 };
        _db.Anbieter.DatenbankAnlegen();
        Assert.True(_db.Initialisieren());
    }

    public void Dispose()
    {
        try { Directory.Delete(_ordner, true); } catch { /* egal */ }
    }

    private VersionInfo NeueVersion(int nr = 1)
    {
        var schule = _db.Schulen().FirstOrDefault() ?? _db.SchuleAnlegen("123456", "Testgymnasium");
        var sj = _db.Schuljahre(schule.Id).FirstOrDefault() ?? _db.SchuljahrAnlegen(schule.Id, "2024-2025", "");
        return _db.VersionAnlegen(sj.Id, nr, "Hauptversion", Demo.NeueDatei("Testgymnasium", "GY"), "Administrator");
    }

    [Fact]
    public void Schema_wird_einmalig_angelegt()
    {
        Assert.Equal(MultiUserDb.AktuelleSchemaVersion, _db.SchemaVersion());
        Assert.False(_db.Initialisieren());
        Assert.Contains(_db.Gruppen(), g => g.Name == MultiUserDb.AdminGruppe);
        var admin = Assert.Single(_db.BenutzerListe());
        Assert.Equal(MultiUserDb.AdminName, admin.Name);
        Assert.False(admin.HatPasswort);
    }

    [Fact]
    public void Administrator_ohne_Passwort_und_nicht_loeschbar()
    {
        var v = NeueVersion();
        var k = MultiUserKlient.Anmelden(_db, "administrator", "", v.Id);
        Assert.True(k.PasswortFehlt);
        Assert.True(k.Rechte.IstAdministrator);
        Assert.NotNull(_db.BenutzerSuchen(MultiUserDb.AdminName)!.LetztesLogin);
        Assert.Throws<MultiUserException>(() => _db.BenutzerLoeschen(k.Benutzer.Id));
        Assert.Throws<MultiUserException>(() => MultiUserKlient.Anmelden(_db, "Administrator", "falsch", v.Id));
    }

    [Fact]
    public void Passwort_setzen_und_pruefen()
    {
        var admin = _db.BenutzerSuchen(MultiUserDb.AdminName)!;
        _db.PasswortSetzen(admin.Id, "Geheim#1");
        Assert.True(_db.PasswortPruefen("Administrator", "Geheim#1"));
        Assert.False(_db.PasswortPruefen("Administrator", "geheim#1"));
        Assert.False(_db.PasswortPruefen("Administrator", ""));
        Assert.True(_db.BenutzerSuchen("Administrator")!.HatPasswort);
        _db.PasswortSetzen(admin.Id, null);
        Assert.True(_db.PasswortPruefen("Administrator", ""));
    }

    [Fact]
    public void Schule_Schuljahr_Version_und_Laden_Speichern()
    {
        var v = NeueVersion();
        var schule = Assert.Single(_db.Schulen());
        var sj = Assert.Single(_db.Schuljahre(schule.Id));
        Assert.Equal("2024/2025", sj.Nr);
        Assert.Throws<MultiUserException>(() => _db.SchuljahrAnlegen(schule.Id, "2024", ""));
        Assert.Throws<MultiUserException>(() => _db.SchuleAnlegen("123456", "doppelt"));
        Assert.Equal(1, Assert.Single(_db.Versionen(sj.Id)).Revision);

        var k = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id);
        var d = k.Laden();
        Assert.Equal(1, k.Revision);
        d.Einstellungen.Schulname = "Gymnasium Musterstadt";
        d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = "MÜL", Langname = "Müller" });
        k.Speichern(d, "Lehrer MÜL angelegt", AenderungsArt.Stammdaten);
        Assert.Equal(2, k.Revision);

        var (geladen, rev) = _db.Laden(v.Id);
        Assert.Equal(2, rev);
        Assert.Equal("Gymnasium Musterstadt", geladen.Einstellungen.Schulname);
        Assert.Contains(geladen.Lehrer, l => l.Kurzname == "MÜL");
        Assert.Equal("Lehrer MÜL angelegt", _db.Aenderungen(v.Id)[0].Beschreibung);

        // Änderungsprotokoll wird gekürzt
        for (var i = 0; i < 5; i++) k.Speichern(d, $"Änderung {i}");
        Assert.Equal(3, _db.Aenderungen(v.Id).Count);
        Assert.NotNull(_db.Stand(v.Id, k.Revision));

        // Export und erneuter Import als neue Version
        var datei = Path.Combine(_ordner, "export.up");
        _db.DateiExportieren(v.Id, datei);
        var v2 = _db.DateiSpeichern(datei, sj.Id, 2, "Kopie", "Administrator");
        Assert.Equal(2, v2.Nr);
        Assert.Contains(_db.Laden(v2.Id).Daten.Lehrer, l => l.Kurzname == "MÜL");
        // Überschreiben einer vorhandenen Version erhöht die Revision
        var v2b = _db.DateiSpeichern(datei, sj.Id, 2, "Kopie", "Administrator");
        Assert.Equal(v2.Id, v2b.Id);
        Assert.Equal(2, v2b.Revision);
    }

    [Fact]
    public void Konflikt_erster_Schreiber_gewinnt()
    {
        var v = NeueVersion();
        var a = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-A");
        var b = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-B");
        var da = a.Laden();
        var db = b.Laden();
        Assert.False(b.PruefeAenderungen());

        da.Einstellungen.Schulname = "A";
        a.Speichern(da, "A ändert");
        Assert.True(b.PruefeAenderungen());

        db.Einstellungen.Schulname = "B";
        var ex = Assert.Throws<MultiUserKonfliktException>(() => b.Speichern(db, "B ändert"));
        Assert.Equal("Die Änderung konnte nicht durchgeführt werden, da ein anderer Benutzer die Daten inzwischen geändert hat.", ex.Message);

        db = b.Neuladen();
        Assert.Equal("A", db.Einstellungen.Schulname);
        Assert.False(b.PruefeAenderungen());
        db.Einstellungen.Schulname = "B";
        b.Speichern(db, "B ändert");
        Assert.Equal("B", _db.Laden(v.Id).Daten.Einstellungen.Schulname);
    }

    [Fact]
    public void Rechte_und_Einschraenkungen()
    {
        var v = NeueVersion();
        var vp = _db.Gruppen().Single(g => g.Name == "Vertretungsplaner");
        var nutzer = _db.BenutzerAnlegen("Meier", vp.Id);
        _db.PasswortSetzen(nutzer.Id, "pw");

        var k = MultiUserKlient.Anmelden(_db, "Meier", "pw", v.Id);
        Assert.True(k.Rechte.Darf(Rechtebereich.Vertretungsplanung, Stufe.Bearbeiten));
        Assert.True(k.Rechte.Darf(Rechtebereich.Stammdaten, Stufe.NurLesen));
        Assert.False(k.Rechte.Darf(Rechtebereich.Stammdaten, Stufe.Bearbeiten));
        Assert.True(k.Rechte.Hat(Recht.Vertretungsstatistik));
        Assert.False(k.Rechte.Hat(Recht.Benutzerverwaltung));
        var d = k.Laden();
        Assert.Throws<MultiUserException>(() => k.Speichern(d, "Lehrer ändern", AenderungsArt.Stammdaten));
        k.Speichern(d, "Vertretung", AenderungsArt.Vertretung);

        // Gruppenrechte ändern
        vp.Stufen[Rechtebereich.Stammdaten] = Stufe.Bearbeiten;
        vp.Rechte.Add(Recht.Optimierung);
        _db.GruppeSpeichern(vp);
        var neu = _db.Gruppe(vp.Id)!;
        Assert.Equal(Stufe.Bearbeiten, neu.Stufen[Rechtebereich.Stammdaten]);
        Assert.Contains(Recht.Optimierung, neu.Rechte);

        // Einschränkung: nur Version 1, nur lesen
        _db.EinschraenkungenSpeichern(vp.Id, new[] { new Einschraenkung { Schule = "123456", Version = 1, NurLesen = true, Abteilung = "SI" } });
        var k2 = MultiUserKlient.Anmelden(_db, "Meier", "pw", v.Id);
        Assert.True(k2.Rechte.NurLesen);
        Assert.Equal("SI", k2.Rechte.Abteilung);
        Assert.False(k2.Rechte.Darf(Rechtebereich.Vertretungsplanung, Stufe.AnsichtenBearbeiten));
        var v2 = NeueVersion(2);
        Assert.Throws<MultiUserException>(() => MultiUserKlient.Anmelden(_db, "Meier", "pw", v2.Id));

        Assert.Throws<MultiUserException>(() => _db.GruppeLoeschen(vp.Id));
        Assert.Throws<MultiUserException>(() => _db.GruppeLoeschen(_db.Gruppen().Single(g => g.Name == MultiUserDb.AdminGruppe).Id));
    }

    [Fact]
    public void Exklusivbetrieb_und_Sitzungen()
    {
        var v = NeueVersion();
        var a = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-A");
        var b = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-B");
        Assert.Equal(2, _db.Sitzungen(v.Id).Count);

        Assert.False(a.ExklusivAnfordern(out var grund));
        Assert.Contains("PC-B", grund);

        b.Abmelden();
        Assert.True(a.ExklusivAnfordern(out _));
        Assert.Throws<MultiUserException>(() => MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-C"));
        a.Speichern(a.Laden(), "im Exklusivbetrieb");
        a.ExklusivFreigeben();
        var c = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id, "PC-C");

        // Abmelden durch den Administrator: Lebenszeichen schlägt fehl
        _db.SitzungBeenden(c.SitzungId);
        Assert.False(c.Lebenszeichen("Stundenplan"));
        Assert.True(c.Abgemeldet);
        Assert.True(a.Lebenszeichen("Unterricht"));
        Assert.Equal("Unterricht", Assert.Single(_db.Sitzungen(v.Id)).Taetigkeit);

        // Abgelaufene Sitzungen verschwinden
        _db.Uhr = () => DateTime.UtcNow.AddMinutes(10);
        Assert.Empty(_db.Sitzungen());
        _db.AutoLogoutMinuten = 15;
        Assert.Equal(15, _db.AutoLogoutMinuten);
    }

    [Fact]
    public void Nachrichten_an_alle()
    {
        var v = NeueVersion();
        var schulleitung = _db.BenutzerAnlegen("Schulleitung", _db.Gruppen().Single(g => g.Name == "Info").Id);
        var a = MultiUserKlient.Anmelden(_db, "Administrator", "", v.Id);
        var b = MultiUserKlient.Anmelden(_db, schulleitung.Name, "", v.Id);
        Assert.Equal(1, a.NachrichtSenden(null, "Bitte in 5 Minuten abmelden."));
        var n = Assert.Single(b.NachrichtenAbholen());
        Assert.Equal("Administrator", n.Absender);
        Assert.Equal("Bitte in 5 Minuten abmelden.", n.Text);
        Assert.Empty(b.NachrichtenAbholen());
        Assert.Empty(a.NachrichtenAbholen());
    }

    [Fact]
    public void Einstellungen_mit_geschuetztem_Passwort()
    {
        var pfad = Path.Combine(_ordner, "multiuser.json");
        var e = new MultiUserEinstellungen { Verbindung = { Art = DatenbankArt.MySql, Server = "db.schule.local", Benutzer = "untplan", Passwort = "Sehr$geheim" }, LetzterBenutzer = "Meier" };
        e.Speichern(pfad);
        Assert.DoesNotContain("Sehr$geheim", File.ReadAllText(pfad));
        var g = MultiUserEinstellungen.Laden(pfad);
        Assert.Equal(DatenbankArt.MySql, g.Verbindung.Art);
        Assert.Equal("Sehr$geheim", g.Verbindung.Passwort);
        Assert.Equal("Meier", g.LetzterBenutzer);
        Assert.True(g.Eingerichtet);
    }
}
