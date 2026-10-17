using System.Diagnostics;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;
using Xunit.Abstractions;

namespace UntPlan.Core.Tests;

/// <summary>
/// Messung typischer Abläufe mit großen Daten (120 Lehrkräfte, 60 Klassen, pädagogischer Tag mit allen Lehrkräften und Klassen,
/// Absenzen in der Woche). Die Zeiten stehen in der Testausgabe; die Grenzen sind großzügig (Schutz vor groben Rückschritten).
/// </summary>
public class LeistungsmessungTests
{
    private readonly ITestOutputHelper _aus;
    public LeistungsmessungTests(ITestOutputHelper aus) => _aus = aus;

    private long Messen(string name, Action a, int wiederholungen = 1)
    {
        a(); // Aufwärmen (JIT)
        // schnellster von mehreren Läufen (unempfindlich gegen GC und parallele Last auf dem Rechner)
        var zeiten = new List<double>();
        for (var i = 0; i < Math.Max(1, wiederholungen); i++)
        {
            var sw = Stopwatch.StartNew();
            a();
            zeiten.Add(sw.Elapsed.TotalMilliseconds);
        }
        zeiten.Sort();
        var ms = (long)Math.Round(zeiten[0]);
        _aus.WriteLine($"{name,-55} {ms,6} ms");
        Messwerte.Add($"{name}\t{ms}");
        return ms;
    }

    private static readonly List<string> Messwerte = new();

    private static Schuldatei Szenario(out DateOnly tag)
    {
        var d = GrosseSchule.Erzeugen();
        // einige Monate nach Schuljahresbeginn (Verlauf mit Vertretungen davor)
        tag = Kalender.NaechsterUnterrichtstag(d, d.Einstellungen.SchuljahrVon.AddDays(120));
        GrosseSchule.AlleVeranstaltung(d, tag, text: "Pädagogischer Tag");
        // Lehrkräftekonferenz am Folgetag ab der 5. Stunde
        GrosseSchule.AlleVeranstaltung(d, Kalender.NaechsterUnterrichtstag(d, tag), 5, 99, "Lehrkräftekonferenz");
        // einige Absenzen in der Woche
        for (var i = 0; i < 10; i++)
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = d.Lehrer[i * 7].Id, Von = tag, Bis = tag.AddDays(6) });
        // Verlauf wie in einer Datei nach einigen Monaten: täglich wechselnde Absenzen in den 12 Wochen davor
        var start = tag.AddDays(-84);
        for (var t = start; t < tag; t = t.AddDays(1))
            for (var i = 0; i < 6; i++)
                d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = d.Lehrer[(t.DayNumber * 7 + i * 13) % d.Lehrer.Count].Id, Von = t, Bis = t });
        Vertretungen.Aktualisieren(d, start, tag.AddDays(6));
        return d;
    }

    /// <summary>
    /// Messung mit einer echten Datei (Umgebungsvariable UNTPLAN_MESSDATEI: .bal oder .up); ohne Variable ohne Wirkung.
    /// Pädagogischer Tag mit allen Lehrkräften und Klassen am ersten Unterrichtstag nach vier Wochen.
    /// </summary>
    [Fact]
    public void EchteDatei_FallsAngegeben()
    {
        var pfad = Environment.GetEnvironmentVariable("UNTPLAN_MESSDATEI");
        if (string.IsNullOrEmpty(pfad) || !File.Exists(pfad)) return;
        Schuldatei d = null!;
        Messen("Datei laden/importieren", () =>
        {
            if (pfad.EndsWith(".bal", StringComparison.OrdinalIgnoreCase))
            {
                d = Demo.NeueDatei("Messung", "GY");
                d.Lehrer.Clear(); d.Klassen.Clear(); d.Faecher.Clear(); d.Raeume.Clear();
                BalImport.Importieren(d, BalImport.DateiLesen(pfad));
            }
            else d = Dokument.Laden(pfad).Daten;
        }, 3);
        var tag = Kalender.NaechsterUnterrichtstag(d, d.Einstellungen.SchuljahrVon.AddDays(28));
        _aus.WriteLine($"{d.Lehrer.Count} Lehrkräfte, {d.Klassen.Count} Klassen, {d.Stundenplan.Count} Planstunden, Tag {tag:dd.MM.yyyy}");
        GrosseSchule.AlleVeranstaltung(d, tag, text: "Pädagogischer Tag");
        Messen("Tag mit pädagogischem Tag berechnen", () => Vertretungen.Aktualisieren(d, tag), 5);
        Messen("Woche berechnen", () => Vertretungen.Aktualisieren(d, tag, tag.AddDays(6)), 3);
        Messen("Tagesplan.Berechnen", () => Tagesplan.Berechnen(d, tag), 15);
        Messen("Vertretungsplan.Zeilen", () => Vertretungsplan.Zeilen(d, tag), 15);
        Messen("Tageswechsel: Probe per Klonen (alt)", () => Vertretungen.Aktualisieren(Dokument.Klonen(d), tag, tag), 5);
        Messen("Tageswechsel: VertretungsProbe (neu)", () => Vertretungen.Aktualisieren(Dokument.VertretungsProbe(d, tag, tag), tag, tag), 5);
        Messen("Export-Paket (14 Tage)", () => UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }), 3);
        var json = UntPlanWebExport.Json(UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }));
        _aus.WriteLine($"Paketgröße (14 Tage): {json.Length / 1024} KB; Zeilen am Tag: {d.Vertretungen.Count(v => v.Datum == tag)}, Aushang: {Vertretungsplan.Zeilen(d, tag).Count}");
    }

    [Fact]
    public void TypischeAbläufeMitGroßenDaten()
    {
        var d = Szenario(out var tag);
        var bis = tag.AddDays(6);
        var gesamt = new Dictionary<string, long>
        {
            ["Vertretungen einer Woche berechnen"] = Messen("Vertretungen einer Woche berechnen (Aktualisieren)", () => Vertretungen.Aktualisieren(d, tag, bis), 7),
            ["Tag neu berechnen"] = Messen("Tag mit pädagogischem Tag neu berechnen", () => Vertretungen.Aktualisieren(d, tag), 15),
            ["Tagesplan"] = Messen("Tagesplan.Berechnen (Tag mit Veranstaltung)", () => Tagesplan.Berechnen(d, tag), 30),
            ["Vertretungsplan"] = Messen("Vertretungsplan.Zeilen (Aushang des Tages)", () => Vertretungsplan.Zeilen(d, tag), 15),
            ["Klonen"] = Messen("Dokument.Klonen (vollständige Kopie)", () => Dokument.Klonen(d), 7),
            ["Tageswechsel alt"] = Messen("Tageswechsel Vertretungsfenster: Probe per Klonen (alt)", () => Vertretungen.Aktualisieren(Dokument.Klonen(d), tag, tag), 7),
            ["Tageswechsel neu"] = Messen("Tageswechsel Vertretungsfenster: VertretungsProbe (neu)", () => Vertretungen.Aktualisieren(Dokument.VertretungsProbe(d, tag, tag), tag, tag), 7),
            ["Speichern"] = Messen("Datei speichern (JSON)", () => System.Text.Json.JsonSerializer.Serialize(d, Dokument.JsonOptionen), 7),
            ["Export"] = Messen("Export-Paket (14 Tage)", () => UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }), 7),
            ["Abrechnung"] = Messen("Vertretungsabrechnung (Woche, alle Lehrkräfte)", () => Statistik.Abrechnung(d, tag, bis), 7),
        };
        var json = UntPlanWebExport.Json(UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }));
        _aus.WriteLine($"Paketgröße (14 Tage): {json.Length / 1024} KB, Vertretungen am Tag: {d.Vertretungen.Count(v => v.Datum == tag)}, insgesamt: {d.Vertretungen.Count}");
        Messwerte.Add($"Paketgröße KB\t{json.Length / 1024}");
        // grobe Obergrenzen (Release und Debug, langsame CI-Rechner)
        Assert.True(gesamt["Tag neu berechnen"] < 5000, $"Tag neu berechnen: {gesamt["Tag neu berechnen"]} ms");
        Assert.True(gesamt["Tagesplan"] < 2000, $"Tagesplan: {gesamt["Tagesplan"]} ms");
        Assert.True(gesamt["Tageswechsel neu"] <= gesamt["Tageswechsel alt"] + 20, $"Tageswechsel: neu {gesamt["Tageswechsel neu"]} ms, alt {gesamt["Tageswechsel alt"]} ms");
    }
}
