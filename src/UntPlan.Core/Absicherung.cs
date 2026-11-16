using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UntPlan.Core;

/// <summary>
/// Einstellungen | Diverses | Sichern: automatische Sicherung in Generationen. Die neueste Sicherung liegt immer in Save1,
/// die älteste in SaveN; vor jeder neuen Sicherung rücken die vorhandenen um eins nach hinten (Save1 → Save2 …), SaveN fällt weg.
/// </summary>
public static class Sicherungsgenerationen
{
    /// <summary>Dateiname der Generation <paramref name="nr"/> (1 = neueste).</summary>
    public static string Dateiname(int nr) => $"Save{nr}{Dokument.Dateiendung}";

    /// <summary>Prüfsumme eines Datenstands – damit „nur bei geänderten Daten“ keine identischen Sicherungen anlegt.</summary>
    public static string Pruefsumme(string inhalt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inhalt)));

    /// <summary>
    /// Schreibt <paramref name="inhalt"/> als neue Sicherung Save1 in <paramref name="ordner"/> und schiebt die älteren Generationen nach.
    /// Liefert den Pfad der neuen Sicherung bzw. null, wenn <paramref name="nurBeiAenderung"/> gesetzt ist und Save1 bereits denselben Stand enthält.
    /// </summary>
    public static string? Sichern(string ordner, string inhalt, int generationen, bool nurBeiAenderung)
    {
        generationen = Math.Clamp(generationen, 1, 99);
        Directory.CreateDirectory(ordner);
        var neueste = Path.Combine(ordner, Dateiname(1));
        if (nurBeiAenderung && File.Exists(neueste) && Pruefsumme(File.ReadAllText(neueste)) == Pruefsumme(inhalt)) return null;
        // überzählige Generationen (z. B. nach Verringern der Anzahl) entfernen
        for (var n = generationen; n <= 99; n++)
        {
            var alt = Path.Combine(ordner, Dateiname(n));
            if (File.Exists(alt)) File.Delete(alt);
        }
        for (var n = generationen - 1; n >= 1; n--)
        {
            var von = Path.Combine(ordner, Dateiname(n));
            if (File.Exists(von)) File.Move(von, Path.Combine(ordner, Dateiname(n + 1)), true);
        }
        var tmp = neueste + ".tmp";
        File.WriteAllText(tmp, inhalt);
        File.Move(tmp, neueste, true);
        return neueste;
    }

    /// <summary>Vorhandene Sicherungen (neueste zuerst) mit Zeitpunkt.</summary>
    public static List<(int Nr, string Pfad, DateTime Zeit)> Vorhandene(string ordner)
    {
        var liste = new List<(int, string, DateTime)>();
        if (!Directory.Exists(ordner)) return liste;
        for (var n = 1; n <= 99; n++)
        {
            var p = Path.Combine(ordner, Dateiname(n));
            if (File.Exists(p)) liste.Add((n, p, File.GetLastWriteTime(p)));
        }
        return liste;
    }
}

/// <summary>
/// Absturzsicherung (Vorbild „Crash Dumps und Qualitätsmonitoring“, Einstellungen | Logging und Crash Reporting). UntPlan überträgt
/// nichts ins Internet: Absturzberichte werden lokal abgelegt, beim Absturz wird eine Wiederherstellungskopie der Daten geschrieben,
/// und eine Sitzungsmarke erkennt beim nächsten Start, ob das Programm ungewollt beendet wurde.
/// </summary>
public sealed class Absturzsicherung
{
    private readonly string _ordner;
    private DateTime _start;

    public Absturzsicherung(string ordner) => _ordner = ordner;

    /// <summary>Ordner der Absturzberichte.</summary>
    public string Berichtsordner => Path.Combine(_ordner, "Absturzberichte");
    private string Marke => Path.Combine(_ordner, "sitzung.laeuft");

    /// <summary>Angaben der letzten, ungewollt beendeten Sitzung (null = sauber beendet bzw. erster Start).</summary>
    public string? VorherigeSitzung { get; private set; }

    /// <summary>
    /// Sitzung beginnen: Liegt noch die Marke der letzten Sitzung vor, wurde diese nicht ordnungsgemäß beendet (<see cref="VorherigeSitzung"/>).
    /// </summary>
    public void SitzungBeginnen(string version, string? datei)
    {
        Directory.CreateDirectory(_ordner);
        VorherigeSitzung = File.Exists(Marke) ? SicherLesen(Marke) : null;
        _start = DateTime.Now;
        File.WriteAllText(Marke, $"Beginn: {_start:dd.MM.yyyy HH:mm:ss}\nVersion: {version}\nDatei: {datei ?? "(keine)"}\n");
    }

    /// <summary>Gewolltes Beenden: Marke entfernen.</summary>
    public void SitzungBeenden()
    {
        try { if (File.Exists(Marke)) File.Delete(Marke); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Datei der Sitzungsmarke für die laufende Sitzung aktualisieren (z. B. nach dem Öffnen einer anderen Datei).</summary>
    public void DateiMerken(string version, string? datei)
    {
        try { File.WriteAllText(Marke, $"Beginn: {_start:dd.MM.yyyy HH:mm:ss}\nVersion: {version}\nDatei: {datei ?? "(keine)"}\n"); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Text eines Absturzberichts (Fehler, Version, Architektur, Sprache, Sitzungsdauer) – ohne personenbezogene Daten.</summary>
    public static string BerichtText(Exception ex, string version, TimeSpan dauer, string? wiederherstellung) =>
        $"""
        UntPlan – Absturzbericht
        Zeitpunkt: {DateTime.Now:dd.MM.yyyy HH:mm:ss}
        Version: {version}
        Systemarchitektur: {RuntimeInformation.ProcessArchitecture}
        Betriebssystem: {RuntimeInformation.OSDescription}
        Sprache: {System.Globalization.CultureInfo.CurrentUICulture.Name}
        Sitzungsdauer: {(int)dauer.TotalSeconds} s
        Wiederherstellungskopie: {wiederherstellung ?? "(keine)"}

        {ex}
        """;

    /// <summary>Absturzbericht ablegen; liefert den Pfad (null, wenn nicht geschrieben werden konnte).</summary>
    public string? BerichtSchreiben(Exception ex, string version, string? wiederherstellung)
    {
        try
        {
            Directory.CreateDirectory(Berichtsordner);
            var pfad = Path.Combine(Berichtsordner, $"Absturz_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            File.WriteAllText(pfad, BerichtText(ex, version, DateTime.Now - _start, wiederherstellung));
            return pfad;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Vorhandene Absturzberichte (neueste zuerst).</summary>
    public List<string> Berichte() =>
        Directory.Exists(Berichtsordner) ? Directory.GetFiles(Berichtsordner, "Absturz_*.txt").OrderByDescending(f => f, StringComparer.Ordinal).ToList() : new();

    /// <summary>Nur die neuesten <paramref name="behalten"/> Berichte aufbewahren.</summary>
    public void Aufraeumen(int behalten)
    {
        foreach (var f in Berichte().Skip(Math.Max(0, behalten)))
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string? SicherLesen(string pfad)
    {
        try { return File.ReadAllText(pfad); } catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; }
    }
}
