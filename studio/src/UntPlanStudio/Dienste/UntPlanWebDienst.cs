using System.Collections.Concurrent;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>Laufender bzw. beendeter Export/Import mit UntPlanWeb (Fortschritt wie im Desktop-Fenster „UntPlanWeb“).</summary>
public sealed class UpwAuftrag
{
    public string Id { get; init; } = "";
    public string DokumentId { get; init; } = "";
    public string Teil { get; init; } = "";
    public bool Export { get; init; }
    public string Benutzer { get; init; } = "";
    public DateTime Gestartet { get; init; } = DateTime.Now;
    public int Wert { get; set; }
    public string Text { get; set; } = "";
    public bool Fertig { get; set; }
    public string? FehlerTitel { get; set; }
    public string? Fehler { get; set; }
    /// <summary>Meldung nach erfolgreichem Ende (Desktop: Info-Fenster).</summary>
    public string? Ergebnis { get; set; }
    /// <summary>Anzeigefenster, das die Oberfläche danach öffnen kann (schuelerabwesenheiten, klassenbuch).</summary>
    public string? Danach { get; set; }
}

/// <summary>Einstellungen für den automatischen Abgleich eines Dokuments mit UntPlanWeb (Studio-Datenordner, nicht in der Schuldatei).</summary>
public sealed class UpwAbgleich
{
    public bool Aktiv { get; set; }
    /// <summary>Takt in Minuten (mindestens 5).</summary>
    public int Minuten { get; set; } = 15;
    /// <summary>Vertretungen* exportieren.</summary>
    public bool Vertretungen { get; set; } = true;
    /// <summary>Rückmeldungen (Absenzen, Nachrichten, Buchungen, Prüfungen, Stornos) übernehmen.</summary>
    public bool Rueckmeldungen { get; set; } = true;
    public DateTime? Zuletzt { get; set; }
    public string Ergebnis { get; set; } = "";
}

/// <summary>
/// Datenaustausch mit UntPlanWeb für das Studio: Aufträge mit Fortschritt (Export Stammdaten/Unterricht/Vertretungen, Import
/// Absenzen/Studenten/Kurse) mit genau derselben Core-Logik wie das Desktop-Fenster, zwischengespeicherte Rückmeldungen für den
/// Dialog „Stundenplanänderungen“ und der optionale automatische Abgleich (<see cref="UpwAbgleichDienst"/>).
/// </summary>
public sealed class UntPlanWebDienst
{
    public const string Stpl = "stpl-aenderungen";
    public const string Absenzen = "absenzen";
    public const string Studenten = "studenten";
    public const string Kurse = "kurse";

    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, UpwAuftrag> _auftraege = new();
    private readonly ConcurrentDictionary<string, List<WebRueckmeldung>> _rueckmeldungen = new();
    private readonly string _ordner;
    private readonly ILogger<UntPlanWebDienst> _log;
    private readonly object _sperre = new();

    public UntPlanWebDienst(StudioKonfiguration k, ILogger<UntPlanWebDienst> log)
    {
        _ordner = Path.Combine(k.Datenordner, "untplanweb");
        _log = log;
    }

    // ---------------------------------------------------------------------------------------------- Datumsangaben (wie Desktop)

    public static DateOnly Heute => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Anfangsdatum der mit * markierten Daten: Tagesdatum bzw. das gewählte Anfangsdatum, wenn aktiviert.</summary>
    public static DateOnly Start(PortalEinstellungen p) => p.AnfangsdatumAktiv && p.Anfangsdatum is DateOnly a ? a : Heute;

    /// <summary>„ab“-Datum einer Zeile: Stammdaten und Kurse ab Schuljahresbeginn, Studenten* ab Schuljahresbeginn bzw. Anfangsdatum, sonst Start.</summary>
    public static DateOnly AbDatum(Schuldatei d, string teil) => teil switch
    {
        WebTeile.Stammdaten or Kurse => d.Einstellungen.SchuljahrVon,
        Studenten => d.Einstellungen.Portal.AnfangsdatumAktiv && d.Einstellungen.Portal.Anfangsdatum is DateOnly a ? a : d.Einstellungen.SchuljahrVon,
        _ => Start(d.Einstellungen.Portal),
    };

    public static string TeilName(string teil) => teil switch
    {
        WebTeile.Stammdaten => "Stammdaten", WebTeile.Unterricht => "Unterricht/Stundenplan", WebTeile.Vertretungen => "Vertretungen", "" => "Alle Daten",
        Absenzen => "Absenzen", Studenten => "Studenten", Kurse => "Kurse", _ => teil,
    };

    /// <summary>Kopie der Verbindungsdaten (für Arbeiten außerhalb der Dokumentsperre).</summary>
    public static PortalEinstellungen Kopie(PortalEinstellungen p) => JsonSerializer.Deserialize<PortalEinstellungen>(JsonSerializer.Serialize(p))!;

    public static string Verbindungsfehler(Exception ex) => ex is HttpRequestException or TaskCanceledException
        ? "Es konnte keine Verbindung zum Server hergestellt werden. Überprüfen Sie Ihre Internetverbindung, die Verbindungseinstellungen und gegebenenfalls den Status des UntPlanWeb-Servers."
        : ex.Message;

    // ---------------------------------------------------------------------------------------------- Aufträge

    public UpwAuftrag? Auftrag(string id) => _auftraege.TryGetValue(id, out var a) ? a : null;

    public UpwAuftrag? Laufend(string dokId) => _auftraege.Values.FirstOrDefault(a => a.DokumentId == dokId && !a.Fertig);

    /// <summary>Startet Export bzw. Import eines Teils im Hintergrund; liefert den Auftrag (Fortschritt über <see cref="Auftrag"/>).</summary>
    public UpwAuftrag Starten(Arbeitsdokument a, string teil, bool export, string benutzer)
    {
        if (!export && teil is not (Absenzen or Studenten or Kurse)) throw StudioFehler.Ungueltig($"Unbekannter Import „{teil}“.");
        if (export && teil.Length > 0 && !WebTeile.Felder.ContainsKey(teil)) throw StudioFehler.Ungueltig($"Unbekannter Export „{teil}“.");
        UpwAuftrag auftrag;
        lock (_sperre)
        {
            if (Laufend(a.Id) is { } l) throw StudioFehler.Ungueltig($"Es läuft bereits eine Übertragung ({TeilName(l.Teil)}, gestartet von {l.Benutzer}).");
            auftrag = new UpwAuftrag { Id = Guid.NewGuid().ToString("N")[..12], DokumentId = a.Id, Teil = teil, Export = export, Benutzer = benutzer, Text = "Wird gestartet …" };
            _auftraege[auftrag.Id] = auftrag;
        }
        foreach (var alt in _auftraege.Values.Where(x => x.Fertig && DateTime.Now - x.Gestartet > TimeSpan.FromHours(2)).ToList()) _auftraege.TryRemove(alt.Id, out _);
        _ = Task.Run(async () =>
        {
            try
            {
                if (export) await ExportierenAsync(a, teil, benutzer, auftrag);
                else if (teil == Absenzen) await AbsenzenImportierenAsync(a, benutzer, auftrag);
                else if (teil == Studenten) await StudentenImportierenAsync(a, benutzer, auftrag);
                else await KurseImportierenAsync(a, benutzer, auftrag);
            }
            catch (Exception ex)
            {
                auftrag.FehlerTitel = "UntPlanWeb-Übertragungsfehler!";
                auftrag.Fehler = Verbindungsfehler(ex);
                _log.LogWarning("UntPlanWeb {Teil}: {Fehler}", teil, ex.Message);
            }
            finally { auftrag.Fertig = true; }
        });
        return auftrag;
    }

    private static void Fortschritt(UpwAuftrag a, int wert, string text)
    {
        a.Wert = Math.Clamp(wert, 0, 100);
        a.Text = text;
    }

    private static async Task<PortalEinstellungen> VerbindungAsync(Arbeitsdokument a)
    {
        var p = await a.LesenAsync(doc => Kopie(doc.Daten.Einstellungen.Portal));
        if (string.IsNullOrWhiteSpace(p.Adresse)) throw new InvalidOperationException("Bitte zuerst unter „Einstellungen“ Server, Port, Schul-ID und API-Schlüssel eintragen.");
        return p;
    }

    /// <summary>Export wie im Desktop: Stammdaten immer mitsenden, Paket außerhalb der Sperre aus einer Kopie erzeugen, übertragen, Revision vermerken.</summary>
    public async Task ExportierenAsync(Arbeitsdokument a, string teil, string benutzer, UpwAuftrag auftrag)
    {
        var p = await VerbindungAsync(a);
        var teile = teil.Length == 0 ? Array.Empty<string>() : new[] { WebTeile.Stammdaten, teil }.Distinct().ToArray();
        Fortschritt(auftrag, 10, $"{TeilName(teil)}: Daten werden zusammengestellt …");
        var (kopie, start) = await a.LesenAsync(doc => (Dokument.Klonen(doc.Daten), Start(doc.Daten.Einstellungen.Portal)));
        var optionen = WebExportOptionen.Aus(p, teile);
        var paket = await Task.Run(() => UntPlanWebExport.PaketErstellen(kopie, start, optionen,
            (i, n) => Fortschritt(auftrag, 10 + 35 * i / Math.Max(1, n), $"Unterrichtsbestandteile werden konvertiert ({i} von {n})")));
        Fortschritt(auftrag, 45, $"{TeilName(teil)}: Übertragung an {p.Adresse} … ({paket.Tage.Count} Tage, {paket.Vertretungen.Count} Vertretungen)");
        var antwort = await UntPlanWebExport.HochladenAsync(p, paket);
        var jetzt = DateTime.Now;
        await a.AendernAsync(benutzer, "UntPlanWeb-Übertragung", AenderungsArt.Einstellungen, d =>
        {
            var e = d.Einstellungen.Portal;
            e.Revision = antwort.Revision;
            e.LetzteSynchronisation = jetzt;
            foreach (var t in teile.Length == 0 ? WebTeile.Felder.Keys.ToArray() : teile) e.LetzteUebertragung[t] = jetzt;
        });
        Fortschritt(auftrag, 100, $"{TeilName(teil)}: an UntPlanWeb übertragen (Revision {antwort.Revision}) um {jetzt:HH:mm}.");
        auftrag.Ergebnis = "Die Datenübertragung nach UntPlanWeb war erfolgreich!";
    }

    /// <summary>Absenzen*: Krankmeldungen und Nachrichten über die Rückmeldungen übernehmen und quittieren.</summary>
    public async Task AbsenzenImportierenAsync(Arbeitsdokument a, string benutzer, UpwAuftrag auftrag)
    {
        var p = await VerbindungAsync(a);
        var start = await a.LesenAsync(doc => Start(doc.Daten.Einstellungen.Portal));
        var typen = new[] { WebRueckmeldungsTypen.Absenz, WebRueckmeldungsTypen.Nachricht };
        Fortschritt(auftrag, 20, "Absenzen: Rückmeldungen werden abgeholt …");
        var liste = (await UntPlanWebExport.RueckmeldungenAsync(p))
            .Where(m => typen.Contains(m.Typ) && (!DateOnly.TryParse(m.Bis ?? m.Von, out var bis) || bis >= start)).ToList();
        var jetzt = DateTime.Now;
        if (liste.Count == 0)
        {
            await a.AendernAsync(benutzer, "UntPlanWeb-Absenzen", AenderungsArt.Einstellungen, d => d.Einstellungen.Portal.LetzteUebertragung[Absenzen] = jetzt);
            Fortschritt(auftrag, 100, "Absenzen: keine neuen Absenzen oder Nachrichten.");
            return;
        }
        Fortschritt(auftrag, 60, $"Absenzen: {liste.Count} Meldungen werden übernommen …");
        List<string> ok = new(), nein = new();
        await a.AendernAsync(benutzer, "UntPlanWeb-Rückmeldungen", AenderungsArt.Vertretung, d =>
        {
            (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
            var tage = d.Abwesenheiten.Where(x => x.Text.Contains("Portal-Meldung")).SelectMany(x => Kalender.Tage(x.Von, x.Bis)).Distinct().ToList();
            foreach (var t in tage.Where(t => Kalender.IstUnterrichtstag(d, t))) Vertretungen.Aktualisieren(d, t);
            d.Einstellungen.Portal.LetzteUebertragung[Absenzen] = jetzt;
        });
        await UntPlanWebExport.QuittierenAsync(p, ok);
        var text = $"{ok.Count} Absenzen oder Nachrichten übernommen{(nein.Count > 0 ? $", {nein.Count} abgelehnt (unbekannte Elemente oder kein Unterrichtstag)" : "")}.";
        Fortschritt(auftrag, 100, "Absenzen: " + text);
        auftrag.Ergebnis = text;
    }

    /// <summary>Studenten*: Schüler-Fehlzeiten aus dem Klassenbuch als Schülerabwesenheiten übernehmen (ohne Doppelimport).</summary>
    public async Task StudentenImportierenAsync(Arbeitsdokument a, string benutzer, UpwAuftrag auftrag)
    {
        var p = await VerbindungAsync(a);
        var ab = await a.LesenAsync(doc => AbDatum(doc.Daten, Studenten));
        Fortschritt(auftrag, 20, $"Studenten: Fehlzeiten ab {ab:d.M.yyyy} werden abgeholt …");
        var liste = await UntPlanWebExport.FehlzeitenAsync(p, ab);
        Fortschritt(auftrag, 60, $"Studenten: {liste.Count} Fehlzeiten werden abgeglichen …");
        WebImportErgebnis? e = null;
        var jetzt = DateTime.Now;
        var anzahl = await a.AendernAsync(benutzer, "UntPlanWeb-Fehlzeiten", AenderungsArt.Vertretung, d =>
        {
            e = UntPlanWebExport.FehlzeitenUebernehmen(d, liste, ab);
            d.Einstellungen.Portal.LetzteUebertragung[Studenten] = jetzt;
        }, doc => doc.Daten.SchuelerAbwesenheiten.Count);
        Fortschritt(auftrag, 100, $"Studenten: Schülerabwesenheiten übernommen – {e}.");
        if (anzahl > 0) auftrag.Danach = "schuelerabwesenheiten";
    }

    /// <summary>Kurse: Klassenbuch je Unterricht (Lehrstoff, Hausaufgaben) übernehmen (ohne Doppelimport).</summary>
    public async Task KurseImportierenAsync(Arbeitsdokument a, string benutzer, UpwAuftrag auftrag)
    {
        var p = await VerbindungAsync(a);
        var ab = await a.LesenAsync(doc => AbDatum(doc.Daten, Kurse));
        Fortschritt(auftrag, 20, $"Kurse: Klassenbuch ab {ab:d.M.yyyy} wird abgeholt …");
        var liste = await UntPlanWebExport.KlassenbuchAsync(p, ab);
        Fortschritt(auftrag, 60, $"Kurse: {liste.Count} Klassenbucheinträge werden abgeglichen …");
        WebImportErgebnis? e = null;
        var jetzt = DateTime.Now;
        var anzahl = await a.AendernAsync(benutzer, "UntPlanWeb-Klassenbuch", AenderungsArt.Vertretung, d =>
        {
            e = UntPlanWebExport.KlassenbuchUebernehmen(d, liste, ab);
            d.Einstellungen.Portal.LetzteUebertragung[Kurse] = jetzt;
        }, doc => doc.Daten.Klassenbuch.Count);
        Fortschritt(auftrag, 100, $"Kurse: Klassenbuch übernommen – {e}.");
        if (anzahl > 0) auftrag.Danach = "klassenbuch";
    }

    // ---------------------------------------------------------------------------------------------- Stundenplanänderungen

    /// <summary>Rückmeldungen (nur Buchungen, Prüfungen, Stornos) vom Server holen und für Übernehmen/Ignorieren merken.</summary>
    public async Task<(List<WebRueckmeldung> Liste, string Status)> StplLadenAsync(Arbeitsdokument a, bool neu)
    {
        var p = await a.LesenAsync(doc => Kopie(doc.Daten.Einstellungen.Portal));
        if (!neu && _rueckmeldungen.TryGetValue(a.Id, out var gemerkt)) return (gemerkt, $"{gemerkt.Count} Änderungen vom Server geladen.");
        if (string.IsNullOrWhiteSpace(p.Adresse))
            return (_rueckmeldungen.GetValueOrDefault(a.Id) ?? new(), "Keine Verbindung zu UntPlanWeb eingerichtet – es werden nur bereits bearbeitete Änderungen angezeigt.");
        try
        {
            var liste = (await UntPlanWebExport.RueckmeldungenAsync(p)).Where(m => StundenplanAenderungen.Typen.Contains(m.Typ)).ToList();
            _rueckmeldungen[a.Id] = liste;
            return (liste, $"{liste.Count} Änderungen vom Server geladen ({DateTime.Now:HH:mm}).");
        }
        catch (Exception ex)
        {
            return (_rueckmeldungen.GetValueOrDefault(a.Id) ?? new(), ex is HttpRequestException or TaskCanceledException ? "Keine Verbindung zum UntPlanWeb-Server." : ex.Message);
        }
    }

    public List<WebRueckmeldung> StplGemerkt(string dokId) => _rueckmeldungen.GetValueOrDefault(dokId) ?? new();

    /// <summary>Quittieren beim Server (Fehler werden ignoriert: die Meldung ist in der Datei als erledigt vermerkt und wird nicht doppelt übernommen).</summary>
    public static async Task QuittierenAsync(PortalEinstellungen p, IEnumerable<string> ids)
    {
        var liste = ids.ToList();
        if (liste.Count == 0 || string.IsNullOrWhiteSpace(p.Adresse)) return;
        try { await UntPlanWebExport.QuittierenAsync(p, liste); }
        catch (Exception) { /* bleibt auf dem Server; wird als übernommen erkannt */ }
    }

    // ---------------------------------------------------------------------------------------------- automatischer Abgleich

    private string AbgleichPfad(string dokId) => Path.Combine(_ordner, dokId + ".json");

    public UpwAbgleich Abgleich(string dokId)
    {
        if (!Dokumentdienst.IdGueltig(dokId)) return new();
        try { return File.Exists(AbgleichPfad(dokId)) ? JsonSerializer.Deserialize<UpwAbgleich>(File.ReadAllText(AbgleichPfad(dokId))) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    public void AbgleichSpeichern(string dokId, UpwAbgleich a)
    {
        if (!Dokumentdienst.IdGueltig(dokId)) throw StudioFehler.Ungueltig("Ungültige Dokument-Id.");
        a.Minuten = Math.Clamp(a.Minuten, 5, 24 * 60);
        lock (_sperre)
        {
            Directory.CreateDirectory(_ordner);
            File.WriteAllText(AbgleichPfad(dokId) + ".tmp", JsonSerializer.Serialize(a, JsonOpt));
            File.Move(AbgleichPfad(dokId) + ".tmp", AbgleichPfad(dokId), true);
        }
    }

    public IEnumerable<string> AbgleichDokumente() =>
        Directory.Exists(_ordner) ? Directory.EnumerateFiles(_ordner, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(Dokumentdienst.IdGueltig) : Array.Empty<string>();

    /// <summary>
    /// Ein Abgleich: Rückmeldungen übernehmen (gleiche Core-Logik wie der Absenzen-Import, inkl. Buchungen/Prüfungen/Stornos, die
    /// über den Dialog bereits bearbeiteten werden übersprungen) und quittieren, danach Vertretungen exportieren.
    /// </summary>
    public async Task<string> AbgleichenAsync(Arbeitsdokument a, UpwAbgleich e, string benutzer = "Automatischer Abgleich")
    {
        var p = await VerbindungAsync(a);
        var teile = new List<string>();
        if (e.Rueckmeldungen)
        {
            var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
            if (liste.Count > 0)
            {
                List<string> ok = new(), nein = new();
                await a.AendernAsync(benutzer, "UntPlanWeb-Abgleich", AenderungsArt.Vertretung, d =>
                {
                    (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
                    d.Einstellungen.Portal.LetzteUebertragung[Absenzen] = DateTime.Now;
                });
                await UntPlanWebExport.QuittierenAsync(p, ok);
                teile.Add($"{ok.Count} Rückmeldungen übernommen" + (nein.Count > 0 ? $", {nein.Count} abgelehnt" : ""));
            }
            else teile.Add("keine neuen Rückmeldungen");
        }
        if (e.Vertretungen)
        {
            var auftrag = new UpwAuftrag { DokumentId = a.Id, Teil = WebTeile.Vertretungen, Export = true, Benutzer = benutzer };
            await ExportierenAsync(a, WebTeile.Vertretungen, benutzer, auftrag);
            teile.Add("Vertretungen exportiert");
        }
        return string.Join(", ", teile);
    }
}

/// <summary>Hintergrunddienst: führt den automatischen Abgleich der Dokumente aus, für die er eingeschaltet ist.</summary>
public sealed class UpwAbgleichDienst : BackgroundService
{
    private readonly UntPlanWebDienst _upw;
    private readonly Dokumentdienst _dok;
    private readonly ILogger<UpwAbgleichDienst> _log;

    public UpwAbgleichDienst(UntPlanWebDienst upw, Dokumentdienst dok, ILogger<UpwAbgleichDienst> log)
    {
        _upw = upw;
        _dok = dok;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            foreach (var id in _upw.AbgleichDokumente().ToList())
            {
                var e = _upw.Abgleich(id);
                if (!e.Aktiv || e.Zuletzt is DateTime z && DateTime.Now - z < TimeSpan.FromMinutes(e.Minuten) || !_dok.Existiert(id) || _upw.Laufend(id) != null) continue;
                try
                {
                    e.Ergebnis = await _upw.AbgleichenAsync(_dok.Holen(id), e);
                }
                catch (Exception ex)
                {
                    e.Ergebnis = "Fehler: " + UntPlanWebDienst.Verbindungsfehler(ex);
                    _log.LogWarning("Automatischer UntPlanWeb-Abgleich {Id}: {Fehler}", id, ex.Message);
                }
                e.Zuletzt = DateTime.Now;
                try { _upw.AbgleichSpeichern(id, e); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
