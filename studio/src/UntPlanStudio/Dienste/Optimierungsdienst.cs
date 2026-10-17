using System.Collections.Concurrent;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>
/// Ein Optimierungslauf als Hintergrundauftrag: läuft auf einer Kopie der Schuldatei (<see cref="Dokument.Klonen"/>) außerhalb der
/// Dokumentsperre, meldet Fortschritt, kann abgebrochen werden und hält die Ergebnispläne bis zur Übernahme bzw. zum nächsten Lauf.
/// </summary>
public sealed class OptimierungsAuftrag : IProgress<OptimierungsFortschritt>
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string DokumentId { get; init; }
    public required string Benutzer { get; init; }
    public required OptimierungsOptionen Optionen { get; init; }
    public DateTime Start { get; } = DateTime.Now;
    public DateTime? Ende { get; internal set; }
    /// <summary>laeuft | fertig | abgebrochen | fehler | uebernommen</summary>
    public string Status { get; internal set; } = "laeuft";
    public string? Fehler { get; internal set; }
    public double Anteil { get; private set; }
    public string Phase { get; private set; } = "Vorbereitung";
    public int Plan { get; private set; }
    public int Plaene { get; private set; }
    public double Kosten { get; private set; }
    /// <summary>Dokumentversion beim Start (Übernahme warnt, wenn das Dokument inzwischen geändert wurde).</summary>
    public long BasisVersion { get; internal set; }
    public Kennzahlen? BasisKennzahlen { get; internal set; }
    public int BasisPlanstunden { get; internal set; }
    public List<PlanErgebnis>? Ergebnisse { get; internal set; }
    /// <summary>Je Ergebnis (Nummer): Anzahl Stunden, die gegenüber dem Plan beim Start an anderer Stelle liegen bzw. neu sind.</summary>
    public Dictionary<int, int> Abweichungen { get; } = new();
    public int? Uebernommen { get; internal set; }
    /// <summary>Kopplungszeilen, denen vor dem Lauf automatisch ein Lehrer zugeordnet wurde.</summary>
    public int Zugeordnet { get; internal set; }
    internal Schuldatei? Basis { get; set; }
    internal CancellationTokenSource Abbruch { get; } = new();
    internal Task? Lauf { get; set; }
    /// <summary>Vorschau eines Ergebnisplans (zwischengespeichert, nur unter <see cref="Vorschausperre"/> verwenden).</summary>
    internal (int Nummer, Dokument Dokument)? Vorschau { get; set; }
    internal object Vorschausperre { get; } = new();

    public bool Laeuft => Status == "laeuft";

    public void Report(OptimierungsFortschritt f)
    {
        Anteil = f.Anteil;
        Phase = f.Phase;
        Plan = f.Plan;
        Plaene = f.Plaene;
        Kosten = f.Kosten;
    }

    internal void Fortschritt(double anteil, string phase)
    {
        Anteil = anteil;
        Phase = phase;
    }
}

/// <summary>Verwaltet je Dokument höchstens einen Optimierungsauftrag (der letzte bleibt mit seinen Ergebnissen erhalten).</summary>
public sealed class Optimierungsdienst
{
    private readonly ConcurrentDictionary<string, OptimierungsAuftrag> _auftraege = new();
    private readonly ILogger<Optimierungsdienst> _log;

    public Optimierungsdienst(ILogger<Optimierungsdienst> log) => _log = log;

    public OptimierungsAuftrag? Holen(string dokumentId) => _auftraege.TryGetValue(dokumentId, out var a) ? a : null;

    /// <summary>
    /// Startet einen Lauf: optional vorher automatische Lehrerzuordnung (eigene Rückgängig-Stufe), dann Kopie der Daten unter der
    /// Sperre und Optimierung im Hintergrund.
    /// </summary>
    public async Task<OptimierungsAuftrag> StartenAsync(Arbeitsdokument a, OptimierungsOptionen o, string benutzer, bool zuordnen)
    {
        if (Holen(a.Id) is { Laeuft: true } alt)
            throw StudioFehler.Ungueltig($"Für dieses Dokument läuft bereits eine Optimierung (gestartet von {alt.Benutzer} um {alt.Start:HH:mm}).");
        o.AnzahlPlaene = Math.Clamp(o.AnzahlPlaene, 1, 20);
        o.Schritte = Math.Clamp(o.Schritte, 1, 9);
        o.StartProzent = Math.Clamp(o.StartProzent, 5, 100);
        o.ErhoehungProzent = Math.Clamp(o.ErhoehungProzent, 5, 100);
        o.Aehnlichkeit = Math.Clamp(o.Aehnlichkeit, 0, 4);
        var auftrag = new OptimierungsAuftrag { DokumentId = a.Id, Benutzer = benutzer, Optionen = o };
        if (!_auftraege.TryAdd(a.Id, auftrag))
        {
            var vorher = Holen(a.Id);
            if (vorher is { Laeuft: true }) throw StudioFehler.Ungueltig("Für dieses Dokument läuft bereits eine Optimierung.");
            _auftraege[a.Id] = auftrag;
        }
        try
        {
            if (zuordnen)
            {
                var n = 0;
                await a.AendernAsync(benutzer, "Automatische Unterrichtszuordnung", AenderungsArt.Unterricht,
                    d => n = UntPlan.Core.Module.Wertrechnung.AutomatischZuordnen(d).Count, _ => 0);
                auftrag.Zugeordnet = n;
            }
            await a.LesenAsync(doc =>
            {
                if (doc.Daten.AktivePeriode.Fixiert)
                    throw StudioFehler.Ungueltig($"Die Periode {doc.Daten.AktivePeriode.Name} ist fixiert – der Stundenplan kann nicht optimiert werden.");
                auftrag.Basis = Dokument.Klonen(doc.Daten);
                auftrag.BasisVersion = a.Version;
                auftrag.BasisPlanstunden = doc.Daten.Stundenplan.Count;
                return true;
            });
        }
        catch
        {
            _auftraege.TryRemove(new KeyValuePair<string, OptimierungsAuftrag>(a.Id, auftrag));
            throw;
        }
        auftrag.Lauf = Task.Run(() => Rechnen(auftrag));
        return auftrag;
    }

    private void Rechnen(OptimierungsAuftrag auftrag)
    {
        var basis = auftrag.Basis!;
        try
        {
            auftrag.Fortschritt(0, "Bewertung des aktuellen Plans");
            auftrag.BasisKennzahlen = Bewertung.Kennzahlen(basis);
            var erg = Optimierer.Optimieren(basis, auftrag.Optionen, auftrag, auftrag.Abbruch.Token);
            var vorher = basis.Stundenplan.GroupBy(p => (p.UnterrichtId, p.Tag, p.Stunde)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var e in erg)
            {
                var rest = new Dictionary<(int, int, int), int>(vorher);
                var abw = 0;
                foreach (var p in e.Plan)
                {
                    var k = (p.UnterrichtId, p.Tag, p.Stunde);
                    if (rest.TryGetValue(k, out var n) && n > 0) rest[k] = n - 1;
                    else abw++;
                }
                auftrag.Abweichungen[e.Nummer] = abw;
            }
            auftrag.Ergebnisse = erg;
            auftrag.Fortschritt(1, "Fertig");
            auftrag.Status = "fertig";
        }
        catch (OperationCanceledException)
        {
            auftrag.Status = "abgebrochen";
            auftrag.Fortschritt(auftrag.Anteil, "Abgebrochen");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Optimierung für Dokument {Dok} fehlgeschlagen", auftrag.DokumentId);
            auftrag.Status = "fehler";
            auftrag.Fehler = ex.Message;
        }
        finally
        {
            auftrag.Ende = DateTime.Now;
        }
    }

    public OptimierungsAuftrag Abbrechen(string dokumentId)
    {
        var a = Holen(dokumentId) ?? throw StudioFehler.NichtGefunden("Ein Optimierungsauftrag");
        if (a.Laeuft) a.Abbruch.Cancel();
        return a;
    }

    /// <summary>Verwirft einen beendeten Auftrag (Ergebnisse werden freigegeben).</summary>
    public void Verwerfen(string dokumentId)
    {
        if (Holen(dokumentId) is { } a)
        {
            if (a.Laeuft) throw StudioFehler.Ungueltig("Die Optimierung läuft noch – bitte zuerst abbrechen.");
            _auftraege.TryRemove(new KeyValuePair<string, OptimierungsAuftrag>(dokumentId, a));
        }
    }

    /// <summary>Ergebnisplan als (zwischengespeichertes) Dokument für die Vorschau einzelner Pläne.</summary>
    public static T MitVorschau<T>(OptimierungsAuftrag a, int nummer, Func<Dokument, T> lesen)
    {
        var e = a.Ergebnisse?.FirstOrDefault(x => x.Nummer == nummer) ?? throw StudioFehler.NichtGefunden($"Plan {nummer}");
        lock (a.Vorschausperre)
        {
            if (a.Vorschau is not { } v || v.Nummer != nummer)
            {
                var kopie = Dokument.Klonen(a.Basis!);
                kopie.Stundenplan.Clear();
                kopie.Stundenplan.AddRange(e.Plan.Select(p => p.Kopie()));
                a.Vorschau = v = (nummer, new Dokument(kopie));
            }
            return lesen(v.Dokument);
        }
    }
}
