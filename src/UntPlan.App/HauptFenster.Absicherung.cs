using System.Text.Json;
using UntPlan.Core;

namespace UntPlan.App;

/// <summary>
/// Absturzsicherung (Vorbild „Crash Dumps und Qualitätsmonitoring“): Absturzbericht und Wiederherstellungskopie bei einem
/// Programmfehler, Hinweis beim nächsten Start nach ungewolltem Beenden. Es wird nichts übertragen – alles bleibt lokal.
/// </summary>
internal static class Absturzschutz
{
    private static Absturzsicherung? _sicherung;
    private static bool _aktiv;

    public static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static string Ordner => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan");

    public static Absturzsicherung Sicherung => _sicherung ??= new Absturzsicherung(Ordner);

    /// <summary>Sitzung beginnen (nicht im Selbsttest).</summary>
    public static void Starten()
    {
        try
        {
            Sicherung.SitzungBeginnen(Version, null);
            _aktiv = true;
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void Beenden()
    {
        if (_aktiv) Sicherung.SitzungBeenden();
    }

    /// <summary>
    /// Programmfehler: Absturzbericht schreiben und – wenn Daten geändert sind – eine Wiederherstellungskopie anlegen.
    /// Liefert einen Hinweistext für die Fehlermeldung (leer, wenn nichts geschrieben wurde).
    /// </summary>
    public static string Fehler(Exception ex)
    {
        if (!_aktiv) return "";
        var e = AppZustand.Einstellungen;
        string? kopie = null;
        try
        {
            if (e.AbsturzWiederherstellungskopie && AppZustand.Doc.IstGeaendert)
            {
                var ordner = Dialoge.Einstellungsseiten.Pfad("Sicherungsdateien");
                Directory.CreateDirectory(ordner);
                kopie = Path.Combine(ordner, $"Wiederherstellung_{DateTime.Now:yyyyMMdd_HHmmss}{Dokument.Dateiendung}");
                File.WriteAllText(kopie, JsonSerializer.Serialize(AppZustand.D, Dokument.JsonOptionen));
            }
        }
        catch (Exception) { kopie = null; } // im Fehlerfall nie einen zweiten Fehler auslösen
        string? bericht = null;
        if (e.AbsturzberichteSpeichern)
        {
            bericht = Sicherung.BerichtSchreiben(ex, Version, kopie);
            try { Sicherung.Aufraeumen(Math.Max(1, e.AbsturzberichteBehalten)); } catch (Exception) { }
        }
        return (kopie != null ? $"\n\nIhre Daten wurden als Wiederherstellungskopie gesichert:\n{kopie}" : "") + (bericht != null ? $"\n\nAbsturzbericht: {bericht}" : "");
    }
}

public sealed partial class HauptFenster
{
    /// <summary>Absturzsicherung, Startdatum und Hinweis nach ungewolltem Beenden (aus <see cref="KomfortVorbereiten"/>).</summary>
    private void AbsicherungVorbereiten()
    {
        if (Selbsttest) return;
        Absturzschutz.Starten();
        var e = AppZustand.Einstellungen;
        // „Mit dem aktuellen Datum starten“ aus: mit dem zuletzt verwendeten Datum beginnen
        if (!e.MitAktuellemDatumStarten && e.LetztesDatum is DateOnly letztes) AppZustand.DatumSetzen(letztes);
        Shown += (_, _) => BeginInvoke(AbsturzHinweis);
        FormClosed += (_, _) =>
        {
            AppZustand.Einstellungen.LetztesDatum = AppZustand.Datum;
            AppZustand.Einstellungen.Speichern();
            Absturzschutz.Beenden();
        };
    }

    /// <summary>Nach ungewolltem Beenden: Hinweis mit den vorhandenen Sicherungen und Angebot, eine davon zu öffnen.</summary>
    private void AbsturzHinweis()
    {
        var vorher = Absturzschutz.Sicherung.VorherigeSitzung;
        if (vorher is null || !AppZustand.Einstellungen.AbsturzHinweisBeimStart) return;
        var ordner = Dialoge.Einstellungsseiten.Pfad("Sicherungsdateien");
        var sicherungen = Directory.Exists(ordner)
            ? Directory.GetFiles(ordner, "*" + Dokument.Dateiendung).Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).Take(5).ToList()
            : new List<FileInfo>();
        var text = "UntPlan wurde beim letzten Mal nicht ordnungsgemäß beendet.\n\n" + vorher.Trim();
        if (sicherungen.Count == 0)
        {
            Ui.Warnung(text + "\n\nEs liegen keine Sicherungen vor.", "UntPlan - Absturzsicherung", this);
            return;
        }
        text += "\n\nVorhandene Sicherungen:\n" + string.Join("\n", sicherungen.Select(f => $"{f.Name}  ({f.LastWriteTime:dd.MM.yyyy HH:mm})")) + "\n\nEine Sicherung öffnen?";
        if (!Ui.Frage(text, "UntPlan - Absturzsicherung")) return;
        using var ofd = new OpenFileDialog { InitialDirectory = ordner, Filter = $"UntPlan-Dateien (*{Dokument.Dateiendung})|*{Dokument.Dateiendung}", FileName = sicherungen[0].Name, Title = "Sicherung öffnen" };
        if (ofd.ShowDialog(this) == DialogResult.OK) DateiLaden(ofd.FileName);
    }
}
