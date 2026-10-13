using System.Text.Json;
using System.Text.Json.Serialization;
using UntPlan.Core.Model;

namespace UntPlan.Core;

public enum AenderungsArt
{
    Alles,
    Stammdaten,
    Unterricht,
    Stundenplan,
    Vertretung,
    Einstellungen,
}

/// <summary>
/// Geöffnete Schuldatei: Laden/Speichern (*.untplan, JSON), Änderungen mit Rückgängig/Wiederholen,
/// Änderungsbenachrichtigung für alle offenen Fenster.
/// </summary>
public sealed class Dokument
{
    public const string Dateiendung = ".untplan";
    private const int MaxUndo = 50;

    private readonly LinkedList<(string Text, byte[] Stand)> _undo = new();
    private readonly Stack<(string Text, byte[] Stand)> _redo = new();
    private DatenIndex? _index;

    public Schuldatei Daten { get; private set; }
    public string? Pfad { get; private set; }
    public bool IstGeaendert { get; private set; }

    public event Action<AenderungsArt>? Geaendert;

    public Dokument(Schuldatei daten, string? pfad = null)
    {
        Daten = daten;
        Pfad = pfad;
        Normalisieren(Daten);
    }

    public DatenIndex Index => _index ??= new DatenIndex(Daten);

    public bool KannRueckgaengig => _undo.Count > 0;
    public bool KannWiederholen => _redo.Count > 0;
    public string? RueckgaengigText => _undo.Last?.Value.Text;

    public static readonly JsonSerializerOptions JsonOptionen = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static Dokument Laden(string pfad)
    {
        var json = File.ReadAllText(pfad);
        var daten = JsonSerializer.Deserialize<Schuldatei>(json, JsonOptionen) ?? throw new InvalidDataException("Leere Datei");
        if (daten.Version > Schuldatei.AktuelleVersion)
            throw new InvalidDataException("Die Datei wurde mit einer neueren UntPlan-Version erstellt.");
        return new Dokument(daten, pfad);
    }

    public void Speichern(string? pfad = null)
    {
        pfad ??= Pfad ?? throw new InvalidOperationException("Kein Dateiname");
        var tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Daten, JsonOptionen));
        if (File.Exists(pfad)) File.Copy(pfad, pfad + ".bak", true);
        File.Move(tmp, pfad, true);
        Pfad = pfad;
        IstGeaendert = false;
        Geaendert?.Invoke(AenderungsArt.Einstellungen);
    }

    public static Schuldatei Klonen(Schuldatei d) =>
        JsonSerializer.Deserialize<Schuldatei>(JsonSerializer.SerializeToUtf8Bytes(d, JsonOptionen), JsonOptionen)!;

    /// <summary>Führt eine Änderung mit Rückgängig-Möglichkeit aus.</summary>
    public void Aendern(string beschreibung, AenderungsArt art, Action<Schuldatei> aktion)
    {
        var vorher = JsonSerializer.SerializeToUtf8Bytes(Daten, JsonOptionen);
        try
        {
            aktion(Daten);
        }
        catch
        {
            Daten = JsonSerializer.Deserialize<Schuldatei>(vorher, JsonOptionen)!;
            _index = null;
            throw;
        }
        _undo.AddLast((beschreibung, vorher));
        if (_undo.Count > MaxUndo) _undo.RemoveFirst();
        _redo.Clear();
        Melden(art);
    }

    /// <summary>Meldet eine Änderung, die ohne Rückgängig-Stufe direkt an den Daten erfolgt ist.</summary>
    public void Melden(AenderungsArt art)
    {
        _index = null;
        IstGeaendert = true;
        Geaendert?.Invoke(art);
    }

    public void Rueckgaengig()
    {
        if (_undo.Last is null) return;
        var (text, stand) = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push((text, JsonSerializer.SerializeToUtf8Bytes(Daten, JsonOptionen)));
        Daten = JsonSerializer.Deserialize<Schuldatei>(stand, JsonOptionen)!;
        Melden(AenderungsArt.Alles);
    }

    public void Wiederholen()
    {
        if (_redo.Count == 0) return;
        var (text, stand) = _redo.Pop();
        _undo.AddLast((text, JsonSerializer.SerializeToUtf8Bytes(Daten, JsonOptionen)));
        Daten = JsonSerializer.Deserialize<Schuldatei>(stand, JsonOptionen)!;
        Melden(AenderungsArt.Alles);
    }

    /// <summary>Ergänzt fehlende Standardwerte (ältere oder unvollständige Dateien).</summary>
    public static void Normalisieren(Schuldatei d)
    {
        var z = d.Zeitraster;
        if (z.StundenProTag <= 0) z.StundenProTag = 8;
        if (z.TageProWoche <= 0) z.TageProWoche = 5;
        for (var s = 1; s <= z.StundenProTag; s++)
        {
            if (z.Zeiten.All(t => t.Nr != s))
            {
                var beginn = new TimeOnly(8, 0).AddMinutes((s - 1) * 55);
                z.Zeiten.Add(new Stundenzeit { Nr = s, Beginn = beginn, Ende = beginn.AddMinutes(45) });
            }
        }
        z.Zeiten.Sort((a, b) => a.Nr.CompareTo(b.Nr));
        if (d.Perioden.Count == 0) d.Perioden.Add(new Periode { Id = d.NeueId(), Name = "Periode 1", Von = d.Einstellungen.SchuljahrVon });
        if (d.Perioden.All(p => p.Id != d.AktivePeriodeId)) d.AktivePeriodeId = d.Perioden[0].Id;
        if (d.KommentarKategorien.Count == 0)
        {
            int[] farben = { unchecked((int)0xFFE53935), unchecked((int)0xFFFB8C00), unchecked((int)0xFFFDD835), unchecked((int)0xFF43A047),
                unchecked((int)0xFF29B6F6), unchecked((int)0xFF3949AB), unchecked((int)0xFFAB47BC), unchecked((int)0xFFEC407A) };
            for (var i = 0; i < farben.Length; i++)
                d.KommentarKategorien.Add(new KommentarKategorie { Nr = i + 1, Name = i == 0 ? "Sehr wichtig" : $"Kategorie {i + 1}", Farbe = farben[i] });
        }
        foreach (var (key, wert) in Gewichtung.Standard)
            d.Gewichtung.TryAdd(key, wert);
        foreach (var (key, wert) in Vertretungsplanung.VertretungsVorschlag.StandardGewichtung)
            d.Einstellungen.Vertretungsgewichtung.TryAdd(key, wert);
        var maxId = new[]
        {
            d.Klassen.Select(x => x.Id), d.Lehrer.Select(x => x.Id), d.Raeume.Select(x => x.Id), d.Faecher.Select(x => x.Id),
            d.Raumgruppen.Select(x => x.Id), d.Gaenge.Select(x => x.Id), d.Abteilungen.Select(x => x.Id), d.Abwesenheitsgruende.Select(x => x.Id),
            d.Schueler.Select(x => x.Id), d.Unterrichte.Select(x => x.Id), d.Perioden.Select(x => x.Id), d.Abwesenheiten.Select(x => x.Id),
            d.Vertretungen.Select(x => x.Id), d.Kommentare.Select(x => x.Id), d.Ferien.Select(x => x.Id),
        }.SelectMany(x => x).DefaultIfEmpty(0).Max();
        if (d.NaechsteId <= maxId) d.NaechsteId = maxId + 1;
        foreach (var u in d.Unterrichte)
            foreach (var p in d.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id))
                while (p.Raeume.Count < u.Zeilen.Count) p.Raeume.Add(null);
    }
}
