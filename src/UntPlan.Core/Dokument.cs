using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Geöffnete Schuldatei: Laden/Speichern (*.up, JSON), Änderungen mit Rückgängig/Wiederholen,
/// Änderungsbenachrichtigung für alle offenen Fenster.
/// </summary>
public sealed class Dokument
{
    public const string Dateiendung = ".up";
    /// <summary>Frühere Dateiendung (wird weiterhin gelesen).</summary>
    public const string DateiendungAlt = ".untplan";
    public const string DateiFilter = "UntPlan-Schuldatei (*.up)|*.up|Ältere Schuldatei (*.untplan)|*.untplan|Alle Dateien|*.*";
    public const string SpeicherFilter = "UntPlan-Schuldatei (*.up)|*.up";
    private const int MaxUndo = 50;

    private readonly LinkedList<(string Text, byte[] Stand)> _undo = new();
    private readonly Stack<(string Text, byte[] Stand)> _redo = new();
    private DatenIndex? _index;

    public Schuldatei Daten { get; private set; }
    public string? Pfad { get; private set; }
    public bool IstGeaendert { get; private set; }

    public event Action<AenderungsArt>? Geaendert;
    /// <summary>Nur das Änderungskennzeichen wurde gesetzt (z. B. geänderte Ansichtseinstellungen) – keine Datenänderung.</summary>
    public event Action? Markiert;

    /// <summary>Exklusive Sperre der geöffneten Datei (null = ohne Sperre, z. B. neue Datei oder MultiUser).</summary>
    public Dateisperre? Sperre { get; set; }
    /// <summary>Die Datei wird bereits von jemand anderem verwendet und ist nur zum Lesen geöffnet: Speichern in dieselbe Datei ist gesperrt.</summary>
    public bool Schreibgeschuetzt { get; set; }
    /// <summary>Liefert beim Speichern die aktuellen Ansichtseinstellungen der Oberfläche (null = Datei-Inhalt unverändert lassen).</summary>
    public Func<JsonObject?>? AnsichtLiefern { get; set; }

    /// <summary>Beginn der Arbeitssitzung (Öffnen der Datei) – Kennzeichen „Neue Vertretung“.</summary>
    public DateTime Sitzungsbeginn { get; } = DateTime.Now;

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
        if (Schreibgeschuetzt && Dateisperre.PfadGleich(pfad, Pfad))
            throw new InvalidOperationException("Die Datei ist schreibgeschützt geöffnet, weil sie von einem anderen Benutzer verwendet wird. Speichern Sie die Änderungen unter einem anderen Namen.");
        if (Sperre != null && Dateisperre.PfadGleich(pfad, Sperre.Datei) && !Sperre.IstGueltig())
            throw new InvalidOperationException("Die Sperre der Datei besteht nicht mehr (Sperrdatei gelöscht oder von jemand anderem übernommen). Damit keine fremden Änderungen überschrieben werden, wurde nicht gespeichert – speichern Sie unter einem anderen Namen.");
        if (AnsichtLiefern?.Invoke() is { } ansicht) Daten.Ansichtseinstellungen = ansicht;
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

    private static readonly System.Reflection.PropertyInfo[] SchuldateiEigenschaften = typeof(Schuldatei).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0).ToArray();

    private static T JsonKopie<T>(T wert) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(wert, JsonOptionen), JsonOptionen)!;

    /// <summary>
    /// Leichte Kopie für eine Probeberechnung der Vertretungen im Zeitraum (z. B. beim Tageswechsel im Vertretungsfenster: „ändert
    /// die Neuberechnung etwas?“). Sie teilt alle Daten mit dem Original – kopiert werden nur die Vertretungen des Zeitraums und die
    /// Veranstaltungen, die ihn berühren, also genau das, was <see cref="Vertretungsplanung.Vertretungen.Aktualisieren(Schuldatei, DateOnly, DateOnly)"/>
    /// verändert. Statt einer vollständigen Kopie der Schuldatei (bei großen Dateien mehrere hundert Millisekunden) nur wenige Millisekunden.
    /// </summary>
    public static Schuldatei VertretungsProbe(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var p = new Schuldatei();
        foreach (var e in SchuldateiEigenschaften) e.SetValue(p, e.GetValue(d));
        p.Vertretungen = d.Vertretungen.Select(v => v.Datum >= von && v.Datum <= bis ? v.Kopie() : v).ToList();
        p.Veranstaltungen = d.Veranstaltungen.Select(va => va.Bis >= von && va.Von <= bis ? JsonKopie(va) : va).ToList();
        return p;
    }

    /// <summary>Nach Änderungen an Unterricht/Stundenplan die Vertretungsplanung ab heute neu abgleichen (Standard: ja).</summary>
    public bool PlanaenderungenAbgleichen { get; set; } = true;

    /// <summary>Führt eine Änderung mit Rückgängig-Möglichkeit aus.</summary>
    public void Aendern(string beschreibung, AenderungsArt art, Action<Schuldatei> aktion)
    {
        if (art is AenderungsArt.Unterricht or AenderungsArt.Stundenplan && Daten.AktivePeriode.Fixiert)
            throw new InvalidOperationException($"Die Periode {Daten.AktivePeriode.Name} ist fixiert – Unterricht und Stundenplan können nicht geändert werden.");
        var vorher = JsonSerializer.SerializeToUtf8Bytes(Daten, JsonOptionen);
        var unterrichtVorher = art == AenderungsArt.Unterricht && Daten.Einstellungen.UnterrichtInFolgeperioden && Daten.Perioden.Count > 1
            ? Module.Perioden.Momentaufnahme(Daten) : null;
        try
        {
            aktion(Daten);
            if (unterrichtVorher != null) Module.Perioden.Weitergeben(Daten, unterrichtVorher);
            // geänderter Unterricht/Stundenplan: bestehende Absenzen und Vertretungen auf den neuen Plan anwenden (gleiche Rückgängig-Stufe)
            if (art is AenderungsArt.Unterricht or AenderungsArt.Stundenplan && PlanaenderungenAbgleichen)
                Vertretungsplanung.Vertretungen.NachPlanaenderung(Daten, DateOnly.FromDateTime(DateTime.Today));
            // geänderte Vertretungen (z. B. Sondereinsatz, Stunde ändern, Verlegung): Unterricht einer abwesenden Klasse entfällt
            if (art is AenderungsArt.Vertretung or AenderungsArt.Alles) Vertretungsplanung.Vertretungen.KlassenabwesenheitAnwenden(Daten);
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

    /// <summary>Kennzeichnet das Dokument als geändert, ohne die Fenster neu aufzubauen (z. B. geänderte Ansichtseinstellungen).</summary>
    public void AlsGeaendertMarkieren()
    {
        if (IstGeaendert) return;
        IstGeaendert = true;
        Markiert?.Invoke();
    }

    /// <summary>Meldet eine Änderung, die ohne Rückgängig-Stufe direkt an den Daten erfolgt ist.</summary>
    public void Melden(AenderungsArt art)
    {
        _index = null;
        Kalender.CacheLeeren();
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
        // Umstellung älterer Dateien: Unterricht und Pausenaufsichten gehören zur Periode
        var erste = true;
        foreach (var p in d.Perioden.OrderBy(p => p.Von))
        {
            if (p.Unterrichte is null)
                p.Unterrichte = d.UnterrichteAlt is null ? new() : erste ? d.UnterrichteAlt : d.UnterrichteAlt.Select(u => u.Kopie(u.Id)).ToList();
            if (p.Pausenaufsichten is null)
                p.Pausenaufsichten = d.PausenaufsichtenAlt is null ? new() : d.PausenaufsichtenAlt.Select(a => new Pausenaufsicht { GangId = a.GangId, Tag = a.Tag, NachStunde = a.NachStunde, LehrerId = a.LehrerId, Teil = a.Teil }).ToList();
            if (string.IsNullOrEmpty(p.Langname)) p.Langname = p.Name;
            erste = false;
        }
        d.UnterrichteAlt = null;
        d.PausenaufsichtenAlt = null;
        foreach (var t in d.Tagestexte.Where(t => t.NurLehrer && t.Art == TagestextArt.Alle)) t.Art = TagestextArt.NurLehrer;
        foreach (var v in d.Vertretungen.Where(v => v.Quelle == VertretungsQuelle.Absenz && v.AbwesenheitId is null))
        {
            // Zeilen aus älteren Dateien: Herkunft bestimmen
            if (v.UnterrichtId is null && v.Art != VertretungsArt.Pausenaufsicht) v.Quelle = VertretungsQuelle.Manuell;
            else if (v.Art is VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch) v.Quelle = VertretungsQuelle.Verlegung;
            if (v.VertreterId.HasValue && v.Art != VertretungsArt.Raumvertretung) v.Bearbeitet = true;
        }
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
            d.Perioden.SelectMany(p => p.Unterrichte ?? new()).Select(x => x.Id), d.Veranstaltungen.Select(x => x.Id), d.Vormerkungen.Select(x => x.Id),
            d.Klausuren.Select(x => x.Id), d.UGruppen.Select(x => x.Id), d.Vertretungen.Select(x => x.VerlegungsId ?? 0),
        }.SelectMany(x => x).DefaultIfEmpty(0).Max();
        if (d.NaechsteId <= maxId) d.NaechsteId = maxId + 1;
        foreach (var per in d.Perioden)
            foreach (var u in per.Unterrichte!)
                foreach (var p in per.Plan.Where(p => p.UnterrichtId == u.Id))
                    while (p.Raeume.Count < u.Zeilen.Count) p.Raeume.Add(null);
    }
}
