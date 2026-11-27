using System.Text.Json;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Hintergrundaufträge der Schulsimulation (Vergangenheit nachpflegen, Simulationsdaten nachbessern): laufen nacheinander (der zweite wartet sichtbar
/// auf den ersten), arbeiten Tag für Tag und geben die Sperre der Simulation nach jedem Tag frei (der Minutenlauf kommt dazwischen), lassen sich nach dem
/// aktuellen Tag abbrechen und speichern ihren Stand: Nach einem Neustart des Servers steht ein unterbrochener Auftrag als abgebrochen da, nie weiter als „läuft“.
/// </summary>
public sealed partial class Schulsimulation
{
    private const string AuftragsSchluessel = "simulation.auftraege";
    private readonly SemaphoreSlim _auftrag = new(1, 1);
    private string _auftragName = "";
    private CancellationTokenSource? _abbruchNachpflegen;
    private CancellationTokenSource? _abbruchNachbessern;
    private bool _auftraegeGeladen;

    private sealed record Auftragsstaende(Nachpflegestand? Nachpflegen, Nachbesserungsstand? Nachbessern);

    /// <summary>Gespeicherte Stände nach einem Neustart lesen: ein laufender Auftrag gilt als unterbrochen.</summary>
    /// <param name="erneut">Erneut lesen (Tests: Zustand wie nach einem Neustart).</param>
    public void AuftraegeLaden(bool erneut = false)
    {
        if (_auftraegeGeladen && !erneut) return;
        _auftraegeGeladen = true;
        try
        {
            if (_einstellungen.Lesen(AuftragsSchluessel) is not { Length: > 0 } json || JsonSerializer.Deserialize<Auftragsstaende>(json, Json) is not { } a) return;
            static string Grund(int fertig, int tage) => $"Durch einen Neustart des Servers unterbrochen nach {fertig} von {tage} Tagen. Bereits bearbeitete Tage bleiben erhalten.";
            if (a.Nachpflegen is { } np)
                _nachpflegen = np.Laeuft ? np with { Laeuft = false, Wartet = "", Abgebrochen = true, Fehler = Grund(np.Fertig, np.Tage), Ende = np.Ende.Length > 0 ? np.Ende : np.Beginn } : np;
            if (a.Nachbessern is { } nb)
                _nachbessern = nb.Laeuft ? nb with { Laeuft = false, Wartet = "", Abgebrochen = true, Fehler = Grund(nb.Fertig, nb.Tage), Ende = nb.Ende.Length > 0 ? nb.Ende : nb.Beginn } : nb;
            AuftraegeSpeichern();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { }
    }

    private void AuftraegeSpeichern()
    {
        try { _einstellungen.Schreiben(AuftragsSchluessel, JsonSerializer.Serialize(new Auftragsstaende(_nachpflegen, _nachbessern), Json)); }
        catch (Exception ex) { _log.LogWarning(ex, "Schulsimulation: Stand der Aufträge nicht gespeichert"); }
    }

    /// <summary>Auftrag im Hintergrund: wartet auf einen laufenden anderen Auftrag, meldet Fehler im Stand statt endlos „läuft“.</summary>
    private void AuftragStarten(string name, CancellationTokenSource abbruch, Action<string> wartet, Action<CancellationToken> arbeit, Action<string> fehler, Action abgebrochen)
    {
        if (_auftrag.CurrentCount == 0) wartet(_auftragName);
        Task.Run(async () =>
        {
            try { await _auftrag.WaitAsync(abbruch.Token); }
            catch (OperationCanceledException) { abgebrochen(); return; }
            try
            {
                _auftragName = name;
                wartet("");
                arbeit(abbruch.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Schulsimulation: {Auftrag} fehlgeschlagen", name);
                fehler(ex.Message);
            }
            finally
            {
                _auftragName = "";
                _auftrag.Release();
                AuftraegeSpeichern();
            }
        });
    }

    /// <summary>Laufendes bzw. wartendes Nachpflegen nach dem aktuellen Tag abbrechen.</summary>
    public Nachpflegestand NachpflegenAbbrechen(Benutzer ich)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        _abbruchNachpflegen?.Cancel();
        return _nachpflegen;
    }

    /// <summary>Laufendes bzw. wartendes Nachbessern nach dem aktuellen Tag abbrechen.</summary>
    public Nachbesserungsstand NachbessernAbbrechen(Benutzer ich)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        _abbruchNachbessern?.Cancel();
        return _nachbessern;
    }
}
