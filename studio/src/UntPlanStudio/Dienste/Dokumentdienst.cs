using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>Eintrag der Dokumentliste (Datei → Öffnen).</summary>
public sealed record DokumentEintrag(string Id, string Name, string Besitzer, DateTime Erstellt, DateTime Geaendert, string Herkunft, long Groesse, bool Offen, bool Ungespeichert);

/// <summary>
/// Verwaltet die Arbeitsdokumente des Servers: Ablage als <c>dokumente/&lt;id&gt;.up</c> (+ <c>.json</c> mit Name/Besitzer),
/// geöffnete Dokumente im Speicher, Neu/Hochladen/BAL-Import/Löschen. Gespeichert wird automatisch
/// (<see cref="Autospeicherung"/>) bzw. über <see cref="Arbeitsdokument.SpeichernAsync"/>.
/// </summary>
public sealed partial class Dokumentdienst
{
    private static readonly JsonSerializerOptions InfoOptionen = new() { WriteIndented = true };
    private readonly StudioKonfiguration _k;
    private readonly ILogger<Dokumentdienst> _log;
    private readonly ConcurrentDictionary<string, Lazy<Arbeitsdokument>> _offen = new();

    public Dokumentdienst(StudioKonfiguration k, ILogger<Dokumentdienst> log)
    {
        _k = k;
        _log = log;
    }

    [GeneratedRegex("^[a-f0-9]{16}$")]
    private static partial Regex IdMuster();

    public static bool IdGueltig(string? id) => id is not null && IdMuster().IsMatch(id);

    private string UpPfad(string id) => Path.Combine(_k.Dokumentordner, id + Dokument.Dateiendung);
    private string InfoPfad(string id) => Path.Combine(_k.Dokumentordner, id + ".json");

    public IEnumerable<Arbeitsdokument> Offene => _offen.Values.Where(l => l.IsValueCreated).Select(l => l.Value);

    /// <summary>Alle Dokumente im Datenordner (neueste zuerst).</summary>
    public List<DokumentEintrag> Liste()
    {
        var res = new List<DokumentEintrag>();
        foreach (var datei in Directory.EnumerateFiles(_k.Dokumentordner, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(datei);
            if (!IdGueltig(id) || !File.Exists(UpPfad(id))) continue;
            var info = InfoLesen(id);
            if (info is null) continue;
            var offen = _offen.TryGetValue(id, out var l) && l.IsValueCreated ? l.Value : null;
            res.Add(new DokumentEintrag(id, offen?.Info.Name ?? info.Name, info.Besitzer, info.Erstellt, offen?.Info.Geaendert ?? info.Geaendert, info.Herkunft,
                new FileInfo(UpPfad(id)).Length, offen != null, offen?.IstGeaendert ?? false));
        }
        return res.OrderByDescending(e => e.Geaendert).ToList();
    }

    public bool Existiert(string id) => IdGueltig(id) && File.Exists(UpPfad(id));

    /// <summary>Geöffnetes Dokument holen bzw. von der Platte laden.</summary>
    public Arbeitsdokument Holen(string? id)
    {
        if (!IdGueltig(id) || !File.Exists(UpPfad(id!)) && !_offen.ContainsKey(id!)) throw StudioFehler.NichtGefunden("Das Dokument");
        var lazy = _offen.GetOrAdd(id!, i => new Lazy<Arbeitsdokument>(() => Laden(i), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var dok = lazy.Value;
            dok.Beruehrt();
            return dok;
        }
        catch
        {
            _offen.TryRemove(id!, out _);
            throw;
        }
    }

    private Arbeitsdokument Laden(string id)
    {
        var info = InfoLesen(id) ?? new DokumentInfo { Id = id, Name = id };
        var dok = Dokument.Laden(UpPfad(id));
        _log.LogInformation("Dokument {Id} ({Name}) geöffnet", id, info.Name);
        return new Arbeitsdokument(info, UpPfad(id), dok, InfoSchreiben);
    }

    private DokumentInfo? InfoLesen(string id)
    {
        try { return JsonSerializer.Deserialize<DokumentInfo>(File.ReadAllText(InfoPfad(id))); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private void InfoSchreiben(Arbeitsdokument a) => InfoSchreiben(a.Info);

    private void InfoSchreiben(DokumentInfo info)
    {
        var pfad = InfoPfad(info.Id);
        File.WriteAllText(pfad + ".tmp", JsonSerializer.Serialize(info, InfoOptionen));
        File.Move(pfad + ".tmp", pfad, true);
    }

    private static string NeueId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>Legt ein neues Arbeitsdokument aus einer Schuldatei an (sofort gespeichert und geöffnet).</summary>
    public Arbeitsdokument Anlegen(Schuldatei daten, string name, string besitzer, string herkunft)
    {
        var id = NeueId();
        var info = new DokumentInfo { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "Neue Schuldatei" : name.Trim(), Besitzer = besitzer, Herkunft = herkunft };
        var dok = new Dokument(daten, UpPfad(id));
        dok.Speichern(UpPfad(id));
        InfoSchreiben(info);
        var a = new Arbeitsdokument(info, UpPfad(id), dok, InfoSchreiben);
        _offen[id] = new Lazy<Arbeitsdokument>(a);
        _log.LogInformation("Dokument {Id} ({Name}) angelegt: {Herkunft}", id, info.Name, herkunft);
        return a;
    }

    /// <summary>Neue leere Schuldatei (wie Desktop „Neu…“).</summary>
    public Arbeitsdokument Neu(string schulname, string schulform, string besitzer) =>
        Anlegen(Demo.NeueDatei(string.IsNullOrWhiteSpace(schulname) ? "Neue Schule" : schulname.Trim(), string.IsNullOrWhiteSpace(schulform) ? "GY" : schulform.Trim()),
            string.IsNullOrWhiteSpace(schulname) ? "Neue Schuldatei" : schulname, besitzer, "neu");

    /// <summary>Beispielschule mit verplantem Stundenplan (Optimierung mit festem Startwert).</summary>
    public Arbeitsdokument Beispiel(string besitzer)
    {
        var d = Demo.Beispielschule();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { AnzahlPlaene = 1, Schritte = 1, Seed = 1 });
        if (erg.Count > 0) d.AktivePeriode.Plan = erg[0].Plan;
        return Anlegen(d, d.Einstellungen.Schulname, besitzer, "beispiel");
    }

    /// <summary>Hochgeladene Datei übernehmen: Schuldatei (*.up, *.untplan) oder BAL-Datei (wird in eine neue Schuldatei importiert).</summary>
    public (Arbeitsdokument Dokument, ImportBericht? Bericht) Hochladen(byte[] inhalt, string dateiname, string besitzer)
    {
        if (inhalt.Length == 0) throw StudioFehler.Ungueltig("Die Datei ist leer.");
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName(dateiname ?? "Datei"));
        var text = Text(inhalt);
        if (text.TrimStart().StartsWith('{'))
        {
            Schuldatei daten;
            try { daten = JsonSerializer.Deserialize<Schuldatei>(text, Dokument.JsonOptionen) ?? throw new InvalidDataException("Leere Datei"); }
            catch (JsonException ex) { throw StudioFehler.Ungueltig("Die Datei ist keine gültige UntPlan-Schuldatei: " + ex.Message); }
            if (daten.Version > Schuldatei.AktuelleVersion) throw StudioFehler.Ungueltig("Die Datei wurde mit einer neueren UntPlan-Version erstellt.");
            return (Anlegen(daten, name, besitzer, "hochgeladen: " + Path.GetFileName(dateiname)), null);
        }
        if (BalImport.IstBal(text))
        {
            var d = Demo.NeueDatei(name, "GY");
            // wie im Desktop „Neu“ + „BAL-Import“ (das Normalisieren übernimmt das neue Dokument)
            var bericht = BalImport.Importieren(d, text);
            var schulname = string.IsNullOrWhiteSpace(d.Einstellungen.Schulname) ? name : d.Einstellungen.Schulname;
            return (Anlegen(d, schulname, besitzer, "BAL: " + Path.GetFileName(dateiname)), bericht);
        }
        throw StudioFehler.Ungueltig("Unbekanntes Dateiformat. Erwartet wird eine UntPlan-Schuldatei (*.up) oder eine BAL-Datei.");
    }

    /// <summary>Text einer hochgeladenen Datei: UTF-8, sonst Latin-1 (wie <see cref="BalImport.DateiLesen"/>).</summary>
    public static string Text(byte[] inhalt)
    {
        try { return new UTF8Encoding(false, true).GetString(inhalt).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(inhalt); }
    }

    public void Umbenennen(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw StudioFehler.Ungueltig("Bitte einen Namen eingeben.");
        var dok = Holen(id);
        dok.Info.Name = name.Trim();
        InfoSchreiben(dok.Info);
    }

    /// <summary>Speichert (falls geändert) und entfernt das Dokument aus dem Speicher.</summary>
    public async Task SchliessenAsync(string id, string benutzer)
    {
        if (!_offen.TryGetValue(id, out var l) || !l.IsValueCreated) return;
        await l.Value.SpeichernAsync(benutzer);
        l.Value.Senden(new StudioEreignis("geschlossen", l.Value.Version, "", "Dokument geschlossen", benutzer, DateTime.Now));
        _offen.TryRemove(id, out _);
    }

    public void Loeschen(string id)
    {
        if (!Existiert(id)) throw StudioFehler.NichtGefunden("Das Dokument");
        if (_offen.TryRemove(id, out var l) && l.IsValueCreated)
            l.Value.Senden(new StudioEreignis("geschlossen", l.Value.Version, "", "Dokument gelöscht", "", DateTime.Now));
        foreach (var pfad in new[] { UpPfad(id), UpPfad(id) + ".bak", InfoPfad(id) })
            if (File.Exists(pfad)) File.Delete(pfad);
    }

    /// <summary>Speichert alle geänderten Dokumente (Autospeicherung, Herunterfahren).</summary>
    public async Task AlleSpeichernAsync(TimeSpan ruhezeit, CancellationToken ct = default)
    {
        foreach (var dok in Offene.ToList())
        {
            if (!dok.IstGeaendert || DateTime.Now - dok.LetzteAenderung < ruhezeit) continue;
            try { await dok.SpeichernAsync("Autospeicherung", false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Autospeicherung von {Id} fehlgeschlagen", dok.Id); }
        }
    }

    /// <summary>Lange nicht benutzte Dokumente ohne Abonnenten speichern und aus dem Speicher entfernen.</summary>
    public async Task AufraeumenAsync(TimeSpan leerlauf)
    {
        foreach (var dok in Offene.ToList())
            if (DateTime.Now - dok.LetzterZugriff > leerlauf && dok.Abonnenten == 0)
                await SchliessenAsync(dok.Id, "Autospeicherung");
    }
}
