using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Handbuch Kap. 6/7: Tageszeitraster, Text zu Stundenplanstunde, Zeilentext-2, Sicherungsgenerationen, Absturzsicherung, Formataustausch, Wochenanzeige.</summary>
public class StundenplanAnwendungTests
{
    private static string NeuerOrdner()
    {
        var o = Path.Combine(Path.GetTempPath(), "UntPlanTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(o);
        return o;
    }

    [Fact]
    public void Tageszeitraster_liefert_Zeit_des_Tages_nur_wenn_aktiv()
    {
        var d = new Schuldatei();
        Dokument.Normalisieren(d);
        var z = d.Zeitraster;
        var allgemein = z.Zeit(1)!.Beginn;
        z.TagesZeiten[4] = new List<Stundenzeit> { new() { Nr = 1, Beginn = new TimeOnly(7, 30), Ende = new TimeOnly(8, 15) } };
        Assert.Equal(allgemein, z.Zeit(4, 1)!.Beginn);
        Assert.False(z.TagWeichtAb(4));
        z.TageszeitrasterAktiv = true;
        Assert.Equal(new TimeOnly(7, 30), z.Zeit(4, 1)!.Beginn);
        Assert.Equal(allgemein, z.Zeit(0, 1)!.Beginn);
        // fehlende Stunden des Tages kommen aus dem allgemeinen Raster
        Assert.Equal(z.Zeit(2)!.Beginn, z.Zeit(4, 2)!.Beginn);
        Assert.True(z.TagWeichtAb(4));
        Assert.False(z.TagWeichtAb(0));
        var tag = z.ZeitenDesTages(4);
        Assert.Equal(z.StundenProTag, tag.Count);
        Assert.Equal(new TimeOnly(7, 30), tag[0].Beginn);
        // überlebt Speichern/Laden
        var json = JsonSerializer.Serialize(d, Dokument.JsonOptionen);
        var d2 = JsonSerializer.Deserialize<Schuldatei>(json, Dokument.JsonOptionen)!;
        Assert.Equal(new TimeOnly(7, 30), d2.Zeitraster.Zeit(4, 1)!.Beginn);
    }

    [Fact]
    public void Stundentext_und_Zeilentext2_werden_kopiert()
    {
        var p = new Planstunde { UnterrichtId = 1, Tag = 2, Stunde = 3, Text = "Probe", Raeume = { 5 } };
        Assert.Equal("Probe", p.Kopie().Text);
        var z = new Kopplungszeile { Zeilentext = "A", Zeilentext2 = "B" };
        Assert.Equal("B", z.Kopie().Zeilentext2);
        var u = new Unterricht { Id = 1, Zeilen = { z } };
        Assert.Equal("B", u.Kopie(2).Zeilen[0].Zeilentext2);
    }

    [Fact]
    public void Sicherungsgenerationen_rotieren_und_ueberspringen_unveraenderte_Daten()
    {
        var o = NeuerOrdner();
        try
        {
            Assert.NotNull(Sicherungsgenerationen.Sichern(o, "eins", 3, true));
            Assert.Null(Sicherungsgenerationen.Sichern(o, "eins", 3, true));
            Assert.NotNull(Sicherungsgenerationen.Sichern(o, "eins", 3, false));
            Sicherungsgenerationen.Sichern(o, "zwei", 3, true);
            Sicherungsgenerationen.Sichern(o, "drei", 3, true);
            Sicherungsgenerationen.Sichern(o, "vier", 3, true);
            Assert.Equal("vier", File.ReadAllText(Path.Combine(o, Sicherungsgenerationen.Dateiname(1))));
            Assert.Equal("drei", File.ReadAllText(Path.Combine(o, Sicherungsgenerationen.Dateiname(2))));
            Assert.Equal("zwei", File.ReadAllText(Path.Combine(o, Sicherungsgenerationen.Dateiname(3))));
            Assert.False(File.Exists(Path.Combine(o, Sicherungsgenerationen.Dateiname(4))));
            // weniger Generationen: überzählige verschwinden
            Sicherungsgenerationen.Sichern(o, "fünf", 2, true);
            Assert.Equal(new[] { 1, 2 }, Sicherungsgenerationen.Vorhandene(o).Select(v => v.Nr));
            Assert.Equal("vier", File.ReadAllText(Path.Combine(o, Sicherungsgenerationen.Dateiname(2))));
        }
        finally { Directory.Delete(o, true); }
    }

    [Fact]
    public void Absturzsicherung_erkennt_ungewolltes_Beenden_und_schreibt_Bericht()
    {
        var o = NeuerOrdner();
        try
        {
            var a = new Absturzsicherung(o);
            a.SitzungBeginnen("1.0", "schule.up");
            Assert.Null(a.VorherigeSitzung);
            // Programm „stürzt ab“: Marke bleibt liegen
            var b = new Absturzsicherung(o);
            b.SitzungBeginnen("1.0", null);
            Assert.Contains("schule.up", b.VorherigeSitzung);
            var pfad = b.BerichtSchreiben(new InvalidOperationException("Testfehler"), "1.0", "rettung.up");
            Assert.NotNull(pfad);
            var text = File.ReadAllText(pfad!);
            Assert.Contains("Testfehler", text);
            Assert.Contains("Systemarchitektur", text);
            Assert.Contains("rettung.up", text);
            b.SitzungBeenden();
            var c = new Absturzsicherung(o);
            c.SitzungBeginnen("1.0", null);
            Assert.Null(c.VorherigeSitzung);
            Assert.Single(c.Berichte());
            c.Aufraeumen(0);
            Assert.Empty(c.Berichte());
        }
        finally { Directory.Delete(o, true); }
    }

    [Fact]
    public void Formate_lassen_sich_exportieren_und_in_andere_Datei_importieren()
    {
        var quelle = new Schuldatei();
        quelle.Formate.Add(new StundenplanFormat { Name = "Klassen Bildschirm", Kurzname = "KBA", Typ = ElementTyp.Klasse, Standard = true, Schriftgroesse = 11 });
        quelle.Formate.Add(new StundenplanFormat { Name = "LehrerDruck", Typ = ElementTyp.Lehrer });
        quelle.Ansichten.Add(new Ansicht { Fenster = "Klassen", Name = "Meine", Spalten = { "Name", "Langname" } });
        var fg = new JsonObject { ["Planung"] = new JsonObject { ["Name"] = "Planung" } };
        var json = FormatAustausch.AlsJson(FormatAustausch.Erstellen(quelle, quelle.Formate.Where(f => f.Typ == ElementTyp.Klasse), true, fg));
        var paket = FormatAustausch.Lesen(json);
        Assert.Single(paket.Stundenplanformate);
        Assert.NotNull(paket.Fenstergruppen?["Planung"]);

        var ziel = new Schuldatei();
        ziel.Formate.Add(new StundenplanFormat { Name = "Klassen Bildschirm", Typ = ElementTyp.Klasse, Standard = false, ImMenue = true, Schriftgroesse = 8 });
        var (neu, ersetzt) = FormatAustausch.Einspielen(ziel, paket, null, true);
        Assert.Equal((1, 1), (neu, ersetzt));
        var f = ziel.Formate.Single(x => x.Name == "Klassen Bildschirm");
        Assert.Equal(11, f.Schriftgroesse);
        Assert.Equal("KBA", f.Kurzname);
        // Standard/Menü der Zieldatei bleiben erhalten
        Assert.False(f.Standard);
        Assert.True(f.ImMenue);
        Assert.Contains(ziel.Ansichten, a => a.Name == "Meine" && a.Spalten.Count == 2);

        // direkt aus einer Schuldatei importieren
        var ausDatei = FormatAustausch.Lesen(JsonSerializer.Serialize(quelle, Dokument.JsonOptionen));
        Assert.Equal(2, ausDatei.Stundenplanformate.Count);
        var ziel2 = new Schuldatei();
        Assert.Equal((1, 0), FormatAustausch.Einspielen(ziel2, ausDatei, new[] { "LehrerDruck" }, false));
        Assert.Throws<InvalidDataException>(() => FormatAustausch.Lesen("{\"x\":1}"));
    }

    [Fact]
    public void Wochenanzeige_Schulwochen_oder_Kalenderwochen()
    {
        var d = new Schuldatei();
        d.Einstellungen.SchuljahrVon = new DateOnly(2026, 8, 3);
        var datum = new DateOnly(2026, 11, 16);
        Assert.Equal("16. Schulwoche", Kalender.Wochenbezeichnung(d, datum, false));
        Assert.Equal("KW 47", Kalender.Wochenbezeichnung(d, datum, true));
        Assert.Equal("KW 32 - KW 47", Kalender.Wochenbereich(d, d.Einstellungen.SchuljahrVon, datum, true));
    }
    [Fact]
    public void Uebersichtsplan_Filter_und_Zeilenbeschriftung()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 1;
        d.Zeitraster.StundenProTag = 4;
        Dokument.Normalisieren(d);
        var r = new Raum { Id = d.NeueId(), Kurzname = "R1a" };
        d.Raeume.Add(r);
        var k1 = new Klasse { Id = d.NeueId(), Kurzname = "1a", StammraumId = r.Id };
        var k2 = new Klasse { Id = d.NeueId(), Kurzname = "1b" };
        d.Klassen.AddRange(new[] { k1, k2 });
        var a = new Lehrer { Id = d.NeueId(), Kurzname = "Arist" };
        var b = new Lehrer { Id = d.NeueId(), Kurzname = "Callas" };
        d.Lehrer.AddRange(new[] { a, b });
        var m = new Fach { Id = d.NeueId(), Kurzname = "M" };
        var e = new Fach { Id = d.NeueId(), Kurzname = "E" };
        d.Faecher.AddRange(new[] { m, e });
        d.Unterrichte.Add(new Unterricht { Id = 1, Wochenstunden = 3, Zeilen = { new Kopplungszeile { LehrerId = a.Id, FachId = m.Id, KlassenIds = { k1.Id } } } });
        d.Unterrichte.Add(new Unterricht { Id = 2, Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = b.Id, FachId = e.Id, KlassenIds = { k2.Id } } } });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 1, Raeume = { null } });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 2, Raeume = { null } });
        Assert.Equal(new[] { a.Id }, Uebersichtsplan.LehrerDerKlasse(d, k1.Id));
        Assert.Equal(new[] { b.Id }, Uebersichtsplan.LehrerDesFaches(d, e.Id));
        var ix = new DatenIndex(d);
        Assert.Equal((2, 1), Uebersichtsplan.Lehrerstunden(ix, a.Id));
        Assert.Equal("Arist 2/1", Uebersichtsplan.Zeilenbeschriftung(ix, ElementTyp.Lehrer, a, true, true));
        Assert.Equal("1a R1a", Uebersichtsplan.Zeilenbeschriftung(ix, ElementTyp.Klasse, k1, true, true));
        Assert.Equal("1b", Uebersichtsplan.Zeilenbeschriftung(ix, ElementTyp.Klasse, k2, false, true));
        Assert.Equal(new[] { b.Id }, Uebersichtsplan.FilterAufloesen(d.Lehrer, "callas, Unbekannt"));
    }

    [Fact]
    public void Stundenliste_nach_Klassen_und_Lehrern()
    {
        var d = new Schuldatei();
        d.Zeitraster.TageProWoche = 2;
        d.Zeitraster.StundenProTag = 3;
        Dokument.Normalisieren(d);
        var k = new Klasse { Id = d.NeueId(), Kurzname = "1a" };
        var k2 = new Klasse { Id = d.NeueId(), Kurzname = "1b" };
        d.Klassen.AddRange(new[] { k, k2 });
        var a = new Lehrer { Id = d.NeueId(), Kurzname = "Arist" };
        var b = new Lehrer { Id = d.NeueId(), Kurzname = "Rub" };
        d.Lehrer.AddRange(new[] { a, b });
        var sp = new Fach { Id = d.NeueId(), Kurzname = "SP" };
        d.Faecher.Add(sp);
        // Kopplung: zwei Lehrkräfte, je eine Klasse
        d.Unterrichte.Add(new Unterricht
        {
            Id = 1, Wochenstunden = 2,
            Zeilen = { new Kopplungszeile { LehrerId = a.Id, FachId = sp.Id, KlassenIds = { k.Id } }, new Kopplungszeile { LehrerId = b.Id, FachId = sp.Id, KlassenIds = { k2.Id } } },
        });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 1, Stunde = 2, Raeume = { null, null }, Text = "Halle" });
        d.Stundenplan.Add(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 3, Raeume = { null, null } });
        var klassen = Stundenliste.Zeilen(d, false);
        Assert.Equal(new[] { "1a", "1a", "1b", "1b" }, klassen.Select(z => z.Element));
        Assert.Equal((0, 3), (klassen[0].Tag, klassen[0].Stunde));
        Assert.Equal("Arist", klassen[0].Lehrer);
        Assert.Equal("Rub", klassen[2].Lehrer);
        Assert.Equal("Halle", klassen[1].Text);
        var lehrer = Stundenliste.Zeilen(d, true);
        Assert.Equal(4, lehrer.Count);
        Assert.All(lehrer.Where(z => z.Element == "Rub"), z => Assert.Equal("1b", z.Klassen));
    }
}
