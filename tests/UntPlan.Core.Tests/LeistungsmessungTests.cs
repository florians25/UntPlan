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
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < wiederholungen; i++) a();
        sw.Stop();
        var ms = sw.ElapsedMilliseconds / Math.Max(1, wiederholungen);
        _aus.WriteLine($"{name,-55} {ms,6} ms");
        Messwerte.Add($"{name}\t{ms}");
        return ms;
    }

    private static readonly List<string> Messwerte = new();

    private static Schuldatei Szenario(out DateOnly tag)
    {
        var d = GrosseSchule.Erzeugen();
        tag = GrosseSchule.Tag(d);
        GrosseSchule.AlleVeranstaltung(d, tag, text: "Pädagogischer Tag");
        // Lehrkräftekonferenz am Folgetag ab der 5. Stunde
        GrosseSchule.AlleVeranstaltung(d, Kalender.NaechsterUnterrichtstag(d, tag), 5, 99, "Lehrkräftekonferenz");
        // einige Absenzen in der Woche
        for (var i = 0; i < 10; i++)
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = d.Lehrer[i * 7].Id, Von = tag, Bis = tag.AddDays(6) });
        return d;
    }

    [Fact]
    public void TypischeAbläufeMitGroßenDaten()
    {
        var d = Szenario(out var tag);
        var bis = tag.AddDays(6);
        var gesamt = new Dictionary<string, long>
        {
            ["Vertretungen einer Woche berechnen"] = Messen("Vertretungen einer Woche berechnen (Aktualisieren)", () => Vertretungen.Aktualisieren(d, tag, bis), 3),
            ["Tag neu berechnen"] = Messen("Tag mit pädagogischem Tag neu berechnen", () => Vertretungen.Aktualisieren(d, tag), 5),
            ["Tagesplan"] = Messen("Tagesplan.Berechnen (Tag mit Veranstaltung)", () => Tagesplan.Berechnen(d, tag), 10),
            ["Vertretungsplan"] = Messen("Vertretungsplan.Zeilen (Aushang des Tages)", () => Vertretungsplan.Zeilen(d, tag), 5),
            ["Klonen"] = Messen("Dokument.Klonen (Tageswechsel im Vertretungsfenster)", () => Dokument.Klonen(d), 3),
            ["Speichern"] = Messen("Datei speichern (JSON)", () => System.Text.Json.JsonSerializer.Serialize(d, Dokument.JsonOptionen), 3),
            ["Export"] = Messen("Export-Paket (14 Tage)", () => UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }), 1),
            ["Abrechnung"] = Messen("Vertretungsabrechnung (Woche, alle Lehrkräfte)", () => Statistik.Abrechnung(d, tag, bis), 1),
        };
        var json = UntPlanWebExport.Json(UntPlanWebExport.PaketErstellen(d, tag, new WebExportOptionen { TageVoraus = 14, TageZurueck = 0 }));
        _aus.WriteLine($"Paketgröße (14 Tage): {json.Length / 1024} KB, Vertretungen am Tag: {d.Vertretungen.Count(v => v.Datum == tag)}");
        Messwerte.Add($"Paketgröße KB\t{json.Length / 1024}");
        try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "untplan_messung.txt"), Messwerte); } catch (IOException) { }
        // grobe Obergrenzen (Release und Debug, langsame CI-Rechner)
        Assert.True(gesamt["Tag neu berechnen"] < 5000, $"Tag neu berechnen: {gesamt["Tag neu berechnen"]} ms");
        Assert.True(gesamt["Tagesplan"] < 2000, $"Tagesplan: {gesamt["Tagesplan"]} ms");
    }
}
