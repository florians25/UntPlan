using System.Diagnostics;
using System.Text.Json;

namespace UntPlan.Core;

/// <summary>Inhalt einer Sperrdatei: wer die Schuldatei seit wann geöffnet hat.</summary>
public sealed class SperrInfo
{
    public string Benutzer { get; set; } = "";
    public string Rechner { get; set; } = "";
    public int ProzessId { get; set; }
    public DateTime Seit { get; set; }
    /// <summary>Eindeutige Kennung dieser Sperre (erkennt, ob die Sperrdatei noch die eigene ist).</summary>
    public string Kennung { get; set; } = "";
    public string Programm { get; set; } = "UntPlan";

    /// <summary>Sperrangaben für den laufenden Prozess.</summary>
    public static SperrInfo Aktuell(string? benutzer = null) => new()
    {
        Benutzer = string.IsNullOrWhiteSpace(benutzer) ? Environment.UserName : benutzer,
        Rechner = Environment.MachineName,
        ProzessId = Environment.ProcessId,
        Seit = DateTime.Now,
        Kennung = Guid.NewGuid().ToString("N"),
    };

    /// <summary>„&lt;Benutzer&gt; an &lt;Rechner&gt; seit &lt;Zeit&gt;“.</summary>
    public string Beschreibung =>
        $"{(Benutzer.Length > 0 ? Benutzer : "unbekannt")} an {(Rechner.Length > 0 ? Rechner : "unbekanntem Rechner")} seit {Seit.ToLocalTime():dd.MM.yyyy HH:mm} Uhr";
}

public enum SperrStatus
{
    /// <summary>Sperre angelegt – exklusiver Schreibzugriff.</summary>
    Erhalten,
    /// <summary>Die Datei wird nachweislich von einem anderen Prozess (oder in diesem Programm) verwendet.</summary>
    Belegt,
    /// <summary>
    /// Gesperrt von einem anderen Rechner, ob das Programm dort noch läuft, ist nicht feststellbar
    /// (Sperre kann nur nach Bestätigung aufgehoben werden).
    /// </summary>
    AndererRechner,
    /// <summary>Sperrdatei kann nicht angelegt werden (z. B. keine Schreibrechte im Ordner).</summary>
    NichtMoeglich,
}

public sealed class SperrErgebnis
{
    public SperrStatus Status { get; init; }
    public Dateisperre? Sperre { get; init; }
    /// <summary>Inhaber der vorgefundenen Sperre (Belegt, AndererRechner bzw. die übernommene verwaiste Sperre).</summary>
    public SperrInfo? Inhaber { get; init; }
    /// <summary>Eine verwaiste Sperre dieses Rechners (Prozess beendet) wurde übernommen.</summary>
    public bool VerwaisteUebernommen { get; init; }
    public string Fehler { get; init; } = "";

    /// <summary>Meldung für den Benutzer.</summary>
    public string Meldung => Status switch
    {
        SperrStatus.Erhalten => "",
        SperrStatus.NichtMoeglich => "Die Datei kann nicht exklusiv geöffnet werden (Sperrdatei nicht anlegbar): " + Fehler,
        _ => "Die Datei wird gerade von " + (Inhaber?.Beschreibung ?? "einem anderen Benutzer") + " verwendet.",
    };
}

/// <summary>
/// Exklusiver Zugriff auf eine Schuldatei: Beim Öffnen wird neben der Datei eine Sperrdatei „&lt;datei&gt;.lock“ mit Benutzer,
/// Rechner, Prozess-ID und Zeitpunkt angelegt und bis zur Freigabe offen gehalten (andere Prozesse können sie lesen, aber
/// weder löschen noch exklusiv öffnen). Ein zweiter Versuch erkennt die Sperre. Verwaiste Sperren (Programm abgestürzt)
/// desselben Rechners werden übernommen, die eines anderen Rechners nur nach Bestätigung (<see cref="Aufheben"/>).
/// </summary>
public sealed class Dateisperre : IDisposable
{
    public const string Endung = ".lock";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Dateisperre> Gehalten = new(StringComparer.OrdinalIgnoreCase);
    private static bool _endeAngemeldet;
    private FileStream? _strom;

    /// <summary>Vollständiger Pfad der gesperrten Schuldatei.</summary>
    public string Datei { get; }
    public string SperrDatei { get; }
    public SperrInfo Info { get; }
    /// <summary>Die Sperre wird von diesem Objekt gehalten (nicht freigegeben).</summary>
    public bool Aktiv => _strom != null;

    private Dateisperre(string datei, SperrInfo info, FileStream strom)
    {
        Datei = datei;
        SperrDatei = datei + Endung;
        Info = info;
        _strom = strom;
    }

    public static string SperrPfad(string datei) => Path.GetFullPath(datei) + Endung;

    /// <summary>Gleiche Datei (Groß-/Kleinschreibung wie unter Windows unerheblich)?</summary>
    public static bool PfadGleich(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sperre für eine Schuldatei anfordern. <paramref name="ich"/> und <paramref name="prozessLebt"/> sind für Tests
    /// austauschbar (Standard: aktueller Prozess bzw. Prüfung über die Prozessliste).
    /// </summary>
    public static SperrErgebnis Anfordern(string datei, SperrInfo? ich = null, Func<SperrInfo, bool>? prozessLebt = null)
    {
        var voll = Path.GetFullPath(datei);
        var sperrDatei = voll + Endung;
        ich ??= SperrInfo.Aktuell();
        prozessLebt ??= ProzessLebt;
        lock (Sync)
        {
            EndeAnmelden();
            if (Gehalten.TryGetValue(voll, out var eigene) && eigene.Aktiv)
                return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = eigene.Info };
            var uebernommen = false;
            SperrInfo? alt = null;
            for (var versuch = 0; versuch < 4; versuch++)
            {
                try
                {
                    var strom = new FileStream(sperrDatei, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    try
                    {
                        JsonSerializer.Serialize(strom, ich, Dokument.JsonOptionen);
                        strom.Flush(true);
                    }
                    catch
                    {
                        strom.Dispose();
                        LoeschenVersuchen(sperrDatei);
                        throw;
                    }
                    var sperre = new Dateisperre(voll, ich, strom);
                    Gehalten[voll] = sperre;
                    return new SperrErgebnis { Status = SperrStatus.Erhalten, Sperre = sperre, Inhaber = alt, VerwaisteUebernommen = uebernommen };
                }
                catch (IOException) when (File.Exists(sperrDatei))
                {
                    // Sperrdatei vorhanden – prüfen, wem sie gehört
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    return new SperrErgebnis { Status = SperrStatus.NichtMoeglich, Fehler = ex.Message };
                }

                var inhaber = Lesen(voll);
                var gehalten = WirdGehalten(sperrDatei);
                if (inhaber is null)
                {
                    // leer oder unlesbar: evtl. gerade im Entstehen, sonst Überrest – entfernen und neu versuchen
                    if (gehalten)
                    {
                        if (versuch < 3) { Thread.Sleep(100); continue; }
                        return new SperrErgebnis { Status = SperrStatus.Belegt };
                    }
                    LoeschenVersuchen(sperrDatei);
                    continue;
                }
                if (gehalten) return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = inhaber };
                if (string.Equals(inhaber.Rechner, ich.Rechner, StringComparison.OrdinalIgnoreCase))
                {
                    // gleicher Rechner: läuft der sperrende Prozess noch? (eigene Prozess-ID ohne gehaltene Sperre = Überrest)
                    var lebt = inhaber.ProzessId != ich.ProzessId && prozessLebt(inhaber);
                    if (lebt) return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = inhaber };
                    LoeschenVersuchen(sperrDatei);
                    uebernommen = true;
                    alt = inhaber;
                    continue;
                }
                return new SperrErgebnis { Status = SperrStatus.AndererRechner, Inhaber = inhaber };
            }
            return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = Lesen(voll) };
        }
    }

    /// <summary>
    /// Fremde (vermutlich verwaiste) Sperre nach Bestätigung durch den Benutzer aufheben und selbst sperren.
    /// Eine nachweislich noch gehaltene Sperre wird nie aufgehoben.
    /// </summary>
    public static SperrErgebnis Aufheben(string datei, SperrInfo? ich = null)
    {
        var voll = Path.GetFullPath(datei);
        lock (Sync)
        {
            if (Gehalten.TryGetValue(voll, out var eigene) && eigene.Aktiv)
                return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = eigene.Info };
            var sperrDatei = voll + Endung;
            if (WirdGehalten(sperrDatei)) return new SperrErgebnis { Status = SperrStatus.Belegt, Inhaber = Lesen(voll) };
            var alt = Lesen(voll);
            LoeschenVersuchen(sperrDatei);
            var erg = Anfordern(voll, ich);
            return erg.Status == SperrStatus.Erhalten ? new SperrErgebnis { Status = erg.Status, Sperre = erg.Sperre, Inhaber = alt, VerwaisteUebernommen = true } : erg;
        }
    }

    /// <summary>Inhalt der Sperrdatei einer Schuldatei (null = keine oder unlesbare Sperre).</summary>
    public static SperrInfo? Lesen(string datei)
    {
        try
        {
            using var fs = new FileStream(SperrPfad(datei), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length == 0) return null;
            var info = JsonSerializer.Deserialize<SperrInfo>(fs, Dokument.JsonOptionen);
            return info is { Kennung.Length: > 0 } ? info : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Hält dieses Objekt die Sperre noch (Sperrdatei vorhanden und unverändert die eigene)?</summary>
    public bool IstGueltig() => _strom != null && File.Exists(SperrDatei) && Lesen(Datei)?.Kennung == Info.Kennung;

    /// <summary>Sperre freigeben und Sperrdatei entfernen (sofern sie noch die eigene ist).</summary>
    public void Freigeben()
    {
        lock (Sync)
        {
            if (_strom is null) return;
            var eigene = IstGueltig();
            _strom.Dispose();
            _strom = null;
            if (Gehalten.TryGetValue(Datei, out var g) && ReferenceEquals(g, this)) Gehalten.Remove(Datei);
            if (eigene) LoeschenVersuchen(SperrDatei);
        }
    }

    public void Dispose() => Freigeben();

    /// <summary>Alle von diesem Prozess gehaltenen Sperren freigeben (Programmende).</summary>
    public static void AlleFreigeben()
    {
        List<Dateisperre> alle;
        lock (Sync) alle = Gehalten.Values.ToList();
        foreach (var s in alle) s.Freigeben();
    }

    private static void EndeAnmelden()
    {
        if (_endeAngemeldet) return;
        _endeAngemeldet = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AlleFreigeben();
    }

    /// <summary>Hält ein (anderer) Prozess die Sperrdatei offen? Exklusives Öffnen schlägt dann fehl.</summary>
    private static bool WirdGehalten(string sperrDatei)
    {
        try
        {
            using var fs = new FileStream(sperrDatei, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool ProzessLebt(SperrInfo info)
    {
        try
        {
            using var p = Process.GetProcessById(info.ProzessId);
            if (p.HasExited) return false;
            try
            {
                // Prozess-ID inzwischen neu vergeben?
                if (p.StartTime.ToUniversalTime() > info.Seit.ToUniversalTime().AddMinutes(1)) return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Startzeit nicht ermittelbar: als laufend werten
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void LoeschenVersuchen(string pfad)
    {
        try { File.Delete(pfad); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* wird beim nächsten Versuch erkannt */ }
    }
}
