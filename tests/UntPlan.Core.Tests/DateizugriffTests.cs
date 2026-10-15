using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Ansichtseinstellungen in der .up-Datei und exklusiver Zugriff über die Sperrdatei.</summary>
public sealed class DateizugriffTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "untplan-sperre-" + Guid.NewGuid().ToString("N"));

    public DateizugriffTests() => Directory.CreateDirectory(_ordner);

    public void Dispose()
    {
        Dateisperre.AlleFreigeben();
        try { Directory.Delete(_ordner, true); } catch { /* egal */ }
    }

    private string Datei(string name = "schule")
    {
        var pfad = Path.Combine(_ordner, name + Dokument.Dateiendung);
        new Dokument(Demo.NeueDatei("Testschule", "GY")).Speichern(pfad);
        return pfad;
    }

    /// <summary>Nachbildung der Benutzereinstellungen der Oberfläche.</summary>
    private sealed class TestEinstellungen
    {
        public List<string> ZuletztGeoeffnet { get; set; } = new();
        public string Vertretungsansicht { get; set; } = "Klasse - Standard";
        public Dictionary<string, string> Stundenplanformate { get; set; } = new();
        public Dictionary<string, List<string>> VertretungsArtenAus { get; set; } = new();
        public bool UnterrichtZeigen { get; set; }
    }

    private static readonly string[] Felder = { "Vertretungsansicht", "Stundenplanformate", "VertretungsArtenAus", "UnterrichtZeigen" };

    // ------------------------------------------------------------------------------------------
    // Ansichtseinstellungen
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Ansicht_Speichern_Laden_Rundreise()
    {
        var e = new TestEinstellungen { Vertretungsansicht = "Lehrer - Standard", UnterrichtZeigen = true, ZuletztGeoeffnet = { "lokal.up" } };
        e.Stundenplanformate["Klasse"] = "Klasse groß";
        e.VertretungsArtenAus["Lehrer - Standard"] = new() { "Entfall", "Pausenaufsicht" };
        var pfad = Path.Combine(_ordner, "ansicht.up");
        var doc = new Dokument(Demo.NeueDatei("Testschule", "GY")) { AnsichtLiefern = () => Ansichtsdaten.Herausloesen(e, Felder) };
        doc.Speichern(pfad);

        var geladen = Dokument.Laden(pfad);
        Assert.NotNull(geladen.Daten.Ansichtseinstellungen);
        Assert.False(geladen.Daten.Ansichtseinstellungen!.ContainsKey("ZuletztGeoeffnet")); // gerätebezogen, bleibt lokal
        var neu = new TestEinstellungen { ZuletztGeoeffnet = { "anderer.up" } };
        Assert.Equal(4, Ansichtsdaten.Uebernehmen(neu, geladen.Daten.Ansichtseinstellungen, Felder));
        Assert.Equal("Lehrer - Standard", neu.Vertretungsansicht);
        Assert.True(neu.UnterrichtZeigen);
        Assert.Equal("Klasse groß", neu.Stundenplanformate["Klasse"]);
        Assert.Equal(new[] { "Entfall", "Pausenaufsicht" }, neu.VertretungsArtenAus["Lehrer - Standard"]);
        Assert.Equal(new[] { "anderer.up" }, neu.ZuletztGeoeffnet);
        Assert.True(Ansichtsdaten.Gleich(Ansichtsdaten.Herausloesen(e, Felder), Ansichtsdaten.Herausloesen(neu, Felder)));
        // Rückgängig/Klonen behalten die Ansicht
        Assert.True(Ansichtsdaten.Gleich(geladen.Daten.Ansichtseinstellungen, Dokument.Klonen(geladen.Daten).Ansichtseinstellungen));
    }

    [Fact]
    public void Alte_Datei_ohne_Ansicht_uebernimmt_lokale_Einstellungen()
    {
        // ältere Datei: Feld „Ansichtseinstellungen“ fehlt völlig
        var knoten = JsonSerializer.SerializeToNode(Demo.NeueDatei("Altschule", "GY"), Dokument.JsonOptionen)!.AsObject();
        knoten.Remove("Ansichtseinstellungen");
        var pfad = Path.Combine(_ordner, "alt.up");
        File.WriteAllText(pfad, knoten.ToJsonString());

        var doc = Dokument.Laden(pfad);
        Assert.Null(doc.Daten.Ansichtseinstellungen);
        Assert.Equal("Altschule", doc.Daten.Einstellungen.Schulname);

        // bisherige lokale Einstellungen bleiben erhalten …
        var lokal = new TestEinstellungen { Vertretungsansicht = "Raum - Standard" };
        lokal.Stundenplanformate["Lehrer"] = "Lehrer klein";
        Assert.Equal(0, Ansichtsdaten.Uebernehmen(lokal, doc.Daten.Ansichtseinstellungen, Felder));
        Assert.Equal("Raum - Standard", lokal.Vertretungsansicht);

        // … und landen beim nächsten Speichern in der Datei
        doc.AnsichtLiefern = () => Ansichtsdaten.Herausloesen(lokal, Felder);
        doc.Speichern();
        var wieder = Dokument.Laden(pfad);
        var ziel = new TestEinstellungen();
        Ansichtsdaten.Uebernehmen(ziel, wieder.Daten.Ansichtseinstellungen, Felder);
        Assert.Equal("Raum - Standard", ziel.Vertretungsansicht);
        Assert.Equal("Lehrer klein", ziel.Stundenplanformate["Lehrer"]);
    }

    [Fact]
    public void Teilweise_oder_beschaedigte_Ansicht_behaelt_uebrige_Werte()
    {
        var ziel = new TestEinstellungen { Vertretungsansicht = "bleibt", UnterrichtZeigen = true };
        var ansicht = new JsonObject { ["Vertretungsansicht"] = 42, ["Stundenplanformate"] = new JsonObject { ["Klasse"] = "X" } };
        Assert.Equal(1, Ansichtsdaten.Uebernehmen(ziel, ansicht, Felder));
        Assert.Equal("bleibt", ziel.Vertretungsansicht);
        Assert.True(ziel.UnterrichtZeigen);
        Assert.Equal("X", ziel.Stundenplanformate["Klasse"]);
    }

    [Fact]
    public void Markieren_setzt_Aenderungskennzeichen_ohne_Datenereignis()
    {
        var doc = new Dokument(Demo.NeueDatei("Testschule", "GY"));
        var daten = 0;
        var markiert = 0;
        doc.Geaendert += _ => daten++;
        doc.Markiert += () => markiert++;
        doc.AlsGeaendertMarkieren();
        doc.AlsGeaendertMarkieren();
        Assert.True(doc.IstGeaendert);
        Assert.Equal(0, daten);
        Assert.Equal(1, markiert);
    }

    // ------------------------------------------------------------------------------------------
    // Sperre
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Zweiter_Sperrversuch_wird_erkannt_und_Freigabe_erlaubt_neuen_Zugriff()
    {
        var pfad = Datei();
        var erste = Dateisperre.Anfordern(pfad);
        Assert.Equal(SperrStatus.Erhalten, erste.Status);
        Assert.True(File.Exists(pfad + Dateisperre.Endung));
        var info = Dateisperre.Lesen(pfad);
        Assert.NotNull(info);
        Assert.Equal(Environment.MachineName, info!.Rechner);
        Assert.Equal(Environment.ProcessId, info.ProzessId);

        var zweite = Dateisperre.Anfordern(pfad);
        Assert.Equal(SperrStatus.Belegt, zweite.Status);
        Assert.Null(zweite.Sperre);
        Assert.Contains("wird gerade von", zweite.Meldung);
        Assert.Contains(Environment.MachineName, zweite.Meldung);
        // auch eine bestätigte Aufhebung bricht eine gehaltene Sperre nicht
        Assert.Equal(SperrStatus.Belegt, Dateisperre.Aufheben(pfad).Status);

        erste.Sperre!.Freigeben();
        Assert.False(File.Exists(pfad + Dateisperre.Endung));
        var dritte = Dateisperre.Anfordern(pfad);
        Assert.Equal(SperrStatus.Erhalten, dritte.Status);
        dritte.Sperre!.Dispose();
        Assert.False(File.Exists(pfad + Dateisperre.Endung));
    }

    [Fact]
    public void Von_anderem_Prozess_offen_gehaltene_Sperre_ist_belegt()
    {
        var pfad = Datei();
        var fremd = new SperrInfo { Benutzer = "kollege", Rechner = "SEKRETARIAT", ProzessId = 4711, Seit = DateTime.Now, Kennung = "x1" };
        File.WriteAllText(pfad + Dateisperre.Endung, JsonSerializer.Serialize(fremd, Dokument.JsonOptionen));
        using (new FileStream(pfad + Dateisperre.Endung, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var erg = Dateisperre.Anfordern(pfad);
            Assert.Equal(SperrStatus.Belegt, erg.Status);
            Assert.Equal("kollege", erg.Inhaber!.Benutzer);
            Assert.Contains("kollege an SEKRETARIAT seit", erg.Meldung);
        }
    }

    [Fact]
    public void Verwaiste_Sperre_desselben_Rechners_wird_uebernommen()
    {
        var pfad = Datei();
        var tot = new SperrInfo { Benutzer = "ich", Rechner = Environment.MachineName, ProzessId = 999_999, Seit = DateTime.Now.AddHours(-3), Kennung = "alt" };
        File.WriteAllText(pfad + Dateisperre.Endung, JsonSerializer.Serialize(tot, Dokument.JsonOptionen));

        // Prozess läuft noch → belegt
        Assert.Equal(SperrStatus.Belegt, Dateisperre.Anfordern(pfad, prozessLebt: _ => true).Status);

        // Prozess existiert nicht mehr → übernehmen
        var erg = Dateisperre.Anfordern(pfad, prozessLebt: _ => false);
        Assert.Equal(SperrStatus.Erhalten, erg.Status);
        Assert.True(erg.VerwaisteUebernommen);
        Assert.Equal("alt", erg.Inhaber!.Kennung);
        Assert.Equal(erg.Sperre!.Info.Kennung, Dateisperre.Lesen(pfad)!.Kennung);
        erg.Sperre.Freigeben();
    }

    [Fact]
    public void Sperre_eines_anderen_Rechners_nur_mit_Bestaetigung_aufheben()
    {
        var pfad = Datei();
        var fremd = new SperrInfo { Benutzer = "kollege", Rechner = "ANDERER-PC", ProzessId = 1234, Seit = DateTime.Now.AddDays(-1), Kennung = "fremd" };
        File.WriteAllText(pfad + Dateisperre.Endung, JsonSerializer.Serialize(fremd, Dokument.JsonOptionen));

        var erg = Dateisperre.Anfordern(pfad, prozessLebt: _ => false);
        Assert.Equal(SperrStatus.AndererRechner, erg.Status);
        Assert.Null(erg.Sperre);
        Assert.Equal("fremd", Dateisperre.Lesen(pfad)!.Kennung); // nichts verändert

        var auf = Dateisperre.Aufheben(pfad);
        Assert.Equal(SperrStatus.Erhalten, auf.Status);
        Assert.Equal("kollege", auf.Inhaber!.Benutzer);
        Assert.Equal(auf.Sperre!.Info.Kennung, Dateisperre.Lesen(pfad)!.Kennung);
        auf.Sperre.Freigeben();
    }

    [Fact]
    public void Leere_Sperrdatei_ohne_Inhaber_wird_ersetzt()
    {
        var pfad = Datei();
        File.WriteAllText(pfad + Dateisperre.Endung, "");
        var erg = Dateisperre.Anfordern(pfad);
        Assert.Equal(SperrStatus.Erhalten, erg.Status);
        erg.Sperre!.Freigeben();
    }

    [Fact]
    public void Speichern_prueft_Sperre_und_Schreibschutz()
    {
        var pfad = Datei();
        var erg = Dateisperre.Anfordern(pfad);
        var doc = Dokument.Laden(pfad);
        doc.Sperre = erg.Sperre;
        doc.Melden(AenderungsArt.Stammdaten);
        doc.Speichern();
        Assert.False(doc.IstGeaendert);
        Assert.True(erg.Sperre!.IstGueltig());

        // jemand hat die Sperre gelöscht und selbst übernommen → nicht mehr speichern
        erg.Sperre.Freigeben();
        var fremd = Dateisperre.Anfordern(pfad);
        Assert.Equal(SperrStatus.Erhalten, fremd.Status);
        var verloren = Dokument.Laden(pfad);
        verloren.Sperre = erg.Sperre;
        Assert.False(erg.Sperre.IstGueltig());
        Assert.Throws<InvalidOperationException>(() => verloren.Speichern());

        // schreibgeschützt geöffnet: nicht in dieselbe Datei, wohl aber unter anderem Namen
        var nurLesen = Dokument.Laden(pfad);
        nurLesen.Schreibgeschuetzt = true;
        Assert.Throws<InvalidOperationException>(() => nurLesen.Speichern());
        nurLesen.Speichern(Path.Combine(_ordner, "kopie.up"));
        Assert.True(File.Exists(Path.Combine(_ordner, "kopie.up")));
        fremd.Sperre!.Freigeben();
    }
}
