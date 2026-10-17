using System.Reflection;
using System.Threading.Channels;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>Beschreibung eines Arbeitsdokuments auf dem Server (Datei <c>&lt;id&gt;.json</c> neben <c>&lt;id&gt;.up</c>).</summary>
public sealed class DokumentInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Besitzer { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public DateTime Geaendert { get; set; } = DateTime.Now;
    /// <summary>Herkunft: „neu“, „beispiel“, „hochgeladen: datei.up“, „BAL: datei.txt“.</summary>
    public string Herkunft { get; set; } = "";
}

/// <summary>Benachrichtigung an alle Fenster/Benutzer eines Dokuments (Server-Sent Events, siehe /ereignisse).</summary>
public sealed record StudioEreignis(string Art, long Version, string Bereich, string Beschreibung, string Benutzer, DateTime Zeit);

/// <summary>
/// Ein geöffnetes Arbeitsdokument: das Core-<see cref="Dokument"/> im Speicher. Alle Zugriffe laufen über
/// <see cref="LesenAsync{T}"/> bzw. <see cref="AendernAsync{T}"/> und sind damit je Dokument serialisiert (UntPlan.Core ist nicht
/// threadsicher). Änderungen laufen über <see cref="Dokument.Aendern"/> und sind damit rückgängig zu machen; jede Änderung erhöht
/// <see cref="Version"/> und wird als <see cref="StudioEreignis"/> an alle Abonnenten gemeldet.
/// </summary>
public sealed class Arbeitsdokument
{
    private readonly SemaphoreSlim _sperre = new(1, 1);
    private readonly object _aboSperre = new();
    private readonly List<Channel<StudioEreignis>> _abos = new();
    private readonly Action<Arbeitsdokument> _infoSpeichern;

    public string Id => Info.Id;
    public DokumentInfo Info { get; }
    /// <summary>Pfad der Arbeitsdatei (*.up) im Datenordner.</summary>
    public string Pfad { get; }
    /// <summary>Das Core-Dokument. Nur innerhalb von <see cref="LesenAsync{T}"/>/<see cref="AendernAsync{T}"/> verwenden!</summary>
    internal Dokument Dokument { get; }
    /// <summary>Änderungszähler seit dem Öffnen (für Aktualisierungen in den Fenstern).</summary>
    public long Version { get; private set; }
    public DateTime LetzterZugriff { get; private set; } = DateTime.Now;
    public DateTime LetzteAenderung { get; private set; } = DateTime.MinValue;
    public DateTime? Gespeichert { get; private set; }
    /// <summary>Bereich der letzten Änderung (<see cref="AenderungsArt"/>), z. B. „Stammdaten“ – für gezielte Aktualisierung der Fenster.</summary>
    public string LetzterBereich { get; private set; } = "";
    public bool IstGeaendert => Dokument.IstGeaendert;

    internal Arbeitsdokument(DokumentInfo info, string pfad, Dokument dokument, Action<Arbeitsdokument> infoSpeichern)
    {
        Info = info;
        Pfad = pfad;
        Dokument = dokument;
        _infoSpeichern = infoSpeichern;
        // Dokument.PlanaenderungenAbgleichen bleibt wie im Desktop eingeschaltet, damit Dateien austauschbar bleiben.
    }

    /// <summary>
    /// Bindung als Handler-Parameter in Minimal-APIs: Routenwert <c>{dok}</c> → geöffnetes Dokument (lädt es bei Bedarf).
    /// </summary>
    public static ValueTask<Arbeitsdokument?> BindAsync(HttpContext ctx, ParameterInfo _)
    {
        var id = ctx.Request.RouteValues["dok"] as string ?? throw StudioFehler.NichtGefunden("Das Dokument");
        return ValueTask.FromResult<Arbeitsdokument?>(ctx.RequestServices.GetRequiredService<Dokumentdienst>().Holen(id));
    }

    internal void Beruehrt() => LetzterZugriff = DateTime.Now;

    /// <summary>Liest unter der Dokumentsperre (Core-Daten nicht nach außen geben, sondern in DTOs umwandeln).</summary>
    public async Task<T> LesenAsync<T>(Func<Dokument, T> lesen, CancellationToken ct = default)
    {
        await _sperre.WaitAsync(ct);
        try
        {
            Beruehrt();
            return lesen(Dokument);
        }
        finally { _sperre.Release(); }
    }

    /// <summary>
    /// Ändert die Daten mit Rückgängig-Stufe (<see cref="Dokument.Aendern"/>) und liefert danach – noch unter der Sperre – ein
    /// Ergebnis (z. B. die geänderte Tabellenzeile).
    /// </summary>
    public async Task<T> AendernAsync<T>(string benutzer, string beschreibung, AenderungsArt art, Action<Schuldatei> aktion, Func<Dokument, T> danach, CancellationToken ct = default)
    {
        await _sperre.WaitAsync(ct);
        try
        {
            Beruehrt();
            Dokument.Aendern(beschreibung, art, aktion);
            var erg = danach(Dokument);
            Geaendert(benutzer, beschreibung, art);
            return erg;
        }
        finally { _sperre.Release(); }
    }

    public Task AendernAsync(string benutzer, string beschreibung, AenderungsArt art, Action<Schuldatei> aktion, CancellationToken ct = default) =>
        AendernAsync(benutzer, beschreibung, art, aktion, _ => true, ct);

    /// <summary>
    /// Freie Aktion am Dokument (z. B. Rückgängig, Import mit eigener Logik). Liefert die Aktion eine Änderungsart, wird die Änderung
    /// gemeldet (Version erhöht, Ereignis an alle Fenster).
    /// </summary>
    public async Task<T> AusfuehrenAsync<T>(string benutzer, Func<Dokument, (T Ergebnis, AenderungsArt? Art, string Beschreibung)> aktion, CancellationToken ct = default)
    {
        await _sperre.WaitAsync(ct);
        try
        {
            Beruehrt();
            var (erg, art, text) = aktion(Dokument);
            if (art is { } a) Geaendert(benutzer, text, a);
            return erg;
        }
        finally { _sperre.Release(); }
    }

    private void Geaendert(string benutzer, string beschreibung, AenderungsArt art)
    {
        Version++;
        LetzterBereich = art.ToString();
        LetzteAenderung = DateTime.Now;
        Info.Geaendert = LetzteAenderung;
        Senden(new StudioEreignis("aenderung", Version, art.ToString(), beschreibung, benutzer, LetzteAenderung));
    }

    /// <summary>Speichert die Arbeitsdatei (auch ohne Änderungen, wenn <paramref name="immer"/>).</summary>
    public async Task<bool> SpeichernAsync(string benutzer, bool immer = false, CancellationToken ct = default)
    {
        await _sperre.WaitAsync(ct);
        try
        {
            if (!immer && !Dokument.IstGeaendert) return false;
            Dokument.Speichern(Pfad);
            Gespeichert = DateTime.Now;
            _infoSpeichern(this);
            Senden(new StudioEreignis("gespeichert", Version, "", "Gespeichert", benutzer, Gespeichert.Value));
            return true;
        }
        finally { _sperre.Release(); }
    }

    /// <summary>Inhalt der Schuldatei (*.up) für den Download, mit dem aktuellen Stand (auch ungespeichert).</summary>
    public Task<byte[]> DateiinhaltAsync(CancellationToken ct = default) =>
        LesenAsync(d => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(d.Daten, Dokument.JsonOptionen), ct);

    // ---------------------------------------------------------------------------------------------
    // Ereignisse

    public ChannelReader<StudioEreignis> Abonnieren(out Action abmelden)
    {
        var ch = Channel.CreateBounded<StudioEreignis>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_aboSperre) _abos.Add(ch);
        abmelden = () =>
        {
            lock (_aboSperre) _abos.Remove(ch);
            ch.Writer.TryComplete();
        };
        return ch.Reader;
    }

    public int Abonnenten
    {
        get { lock (_aboSperre) return _abos.Count; }
    }

    internal void Senden(StudioEreignis e)
    {
        lock (_aboSperre)
            foreach (var ch in _abos) ch.Writer.TryWrite(e);
    }
}
