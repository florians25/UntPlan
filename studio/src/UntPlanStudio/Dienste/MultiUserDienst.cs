using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using UntPlan.Core;
using UntPlan.Core.MultiUser;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>Zuordnung eines Studio-Benutzers zu einer MultiUser-Gruppe (mit Einschränkungen wie im Desktop).</summary>
public sealed class MuZuordnung
{
    public int GruppeId { get; set; }
    public bool NurLesen { get; set; }
    /// <summary>Abteilung (Kurzname), auf die der Benutzer beschränkt ist; leer = alle (wird angezeigt, Prüfung je Modul).</summary>
    public string Abteilung { get; set; } = "";
}

/// <summary>Gespeicherte MultiUser-Konfiguration des Studios (Datei <c>multiuser.json</c> im Datenordner).</summary>
public sealed class MuKonfiguration
{
    public List<Gruppe> Gruppen { get; set; } = new();
    public Dictionary<string, MuZuordnung> Zuordnung { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sperren und Exklusivbetrieb verfallen, wenn ihr Inhaber so lange nicht mehr aktiv war (Desktop: Sitzungs-Timeout).</summary>
    public int SperrTimeoutMinuten { get; set; } = 10;
}

/// <summary>Ein Benutzer, der gerade an einem Dokument arbeitet (Desktop: „Angemeldete Benutzer“ mit Tätigkeit).</summary>
public sealed record MuSitzung(string Benutzer, string Anzeigename, DateTime Seit, DateTime Aktivitaet, string Taetigkeit, bool Aktiv);
public sealed record MuSperre(string Bereich, string BereichText, string Benutzer, string Anzeigename, DateTime Seit);
public sealed record MuNachricht(int Id, string Von, string? An, DateTime Zeit, string Text);

/// <summary>
/// MultiUser im Studio: verbindet <c>UntPlan.Core.MultiUser</c> (Gruppen, <see cref="Rechte"/>, <see cref="Rechtebereich"/>,
/// <see cref="Stufe"/>, Exklusivbetrieb) mit dem Benutzerdienst. Alle Benutzer bearbeiten dasselbe Arbeitsdokument im Speicher
/// (Dokumentdienst), daher entfällt das Laden/Speichern über eine Datenbank; geblieben sind die Rechte je Bereich, Sperren
/// einzelner Bereiche, der Exklusivbetrieb, die Anzeige der aktiven Benutzer mit Tätigkeit und Nachrichten.
/// </summary>
public sealed class MultiUserDienst
{
    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    /// <summary>Wie lange ein Benutzer ohne Lebenszeichen als aktiv gilt (Core: <see cref="MultiUserDb.SitzungsTimeout"/>).</summary>
    public static readonly TimeSpan AktivTimeout = TimeSpan.FromMinutes(2);

    private readonly string _datei;
    private readonly Benutzerdienst _benutzer;
    private readonly object _sperre = new();
    private MuKonfiguration _k;
    private readonly ConcurrentDictionary<(string Dok, string Benutzer), MuSitzung> _sitzungen = new();
    private readonly ConcurrentDictionary<(string Dok, Rechtebereich Bereich), (string Benutzer, DateTime Seit)> _sperren = new();
    private readonly ConcurrentDictionary<string, (string Benutzer, DateTime Seit)> _exklusiv = new();
    private readonly ConcurrentDictionary<string, List<MuNachricht>> _nachrichten = new();
    private int _nachrichtId;

    public MultiUserDienst(StudioKonfiguration k, Benutzerdienst benutzer)
    {
        _datei = Path.Combine(k.Datenordner, "multiuser.json");
        _benutzer = benutzer;
        _k = Laden();
    }

    // ---------------------------------------------------------------------------------------------- Konfiguration

    private MuKonfiguration Laden()
    {
        try
        {
            if (File.Exists(_datei))
            {
                var k = JsonSerializer.Deserialize<MuKonfiguration>(File.ReadAllText(_datei), JsonOpt);
                if (k is { Gruppen.Count: > 0 })
                {
                    k.Zuordnung = new Dictionary<string, MuZuordnung>(k.Zuordnung, StringComparer.OrdinalIgnoreCase);
                    return k;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return new MuKonfiguration { Gruppen = StandardGruppen() };
    }

    /// <summary>Standardgruppen wie beim Einrichten der Desktop-Datenbank (<see cref="MultiUserDb.Initialisieren"/>).</summary>
    public static List<Gruppe> StandardGruppen()
    {
        var admin = new Gruppe { Id = 1, Name = MultiUserDb.AdminGruppe, Rechte = Enum.GetValues<Recht>().ToHashSet() };
        foreach (var b in Enum.GetValues<Rechtebereich>()) admin.Stufen[b] = Stufe.Bearbeiten;
        var sp = new Gruppe { Id = 2, Name = "Stundenplaner", Rechte = { Recht.VersionAnlegen, Recht.Optimierung, Recht.Drucken, Recht.WebExport, Recht.DatenImportExport, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) sp.Stufen[b] = b is Rechtebereich.Vertretungsplanung or Rechtebereich.Absenzen ? Stufe.NurLesen : Stufe.Bearbeiten;
        var vp = new Gruppe { Id = 3, Name = "Vertretungsplaner", Rechte = { Recht.Drucken, Recht.Vertretungsstatistik, Recht.WebExport, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) vp.Stufen[b] = b is Rechtebereich.Vertretungsplanung or Rechtebereich.Absenzen ? Stufe.Bearbeiten : Stufe.AnsichtenBearbeiten;
        var info = new Gruppe { Id = 4, Name = "Info", Rechte = { Recht.Drucken, Recht.InfoStundenplan } };
        foreach (var b in Enum.GetValues<Rechtebereich>()) info.Stufen[b] = Stufe.NurLesen;
        return new() { admin, sp, vp, info };
    }

    private void Schreiben()
    {
        File.WriteAllText(_datei + ".tmp", JsonSerializer.Serialize(_k, JsonOpt));
        File.Move(_datei + ".tmp", _datei, true);
    }

    public MuKonfiguration Konfiguration()
    {
        lock (_sperre) return JsonSerializer.Deserialize<MuKonfiguration>(JsonSerializer.Serialize(_k, JsonOpt), JsonOpt)!;
    }

    public Gruppe GruppeSpeichern(Gruppe g)
    {
        if (string.IsNullOrWhiteSpace(g.Name)) throw StudioFehler.Ungueltig("Bitte einen Gruppennamen eingeben.");
        lock (_sperre)
        {
            if (_k.Gruppen.Any(x => x.Id != g.Id && x.Name.Equals(g.Name.Trim(), StringComparison.OrdinalIgnoreCase))) throw StudioFehler.Ungueltig($"Die Gruppe „{g.Name}“ gibt es bereits.");
            var alt = _k.Gruppen.FirstOrDefault(x => x.Id == g.Id && g.Id > 0);
            if (alt is null)
            {
                g.Id = _k.Gruppen.Count == 0 ? 1 : _k.Gruppen.Max(x => x.Id) + 1;
                _k.Gruppen.Add(g);
            }
            else
            {
                if (alt.Name == MultiUserDb.AdminGruppe && !g.Rechte.Contains(Recht.Administrator)) throw StudioFehler.Ungueltig("Die Gruppe Administrator behält das Recht Administrator.");
                _k.Gruppen[_k.Gruppen.IndexOf(alt)] = g;
            }
            g.Name = g.Name.Trim();
            Schreiben();
            return g;
        }
    }

    public void GruppeLoeschen(int id)
    {
        lock (_sperre)
        {
            var g = _k.Gruppen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Gruppe Nr. {id}");
            if (g.Name == MultiUserDb.AdminGruppe) throw StudioFehler.Ungueltig("Die Gruppe Administrator kann nicht gelöscht werden.");
            if (_k.Zuordnung.Values.Any(z => z.GruppeId == id)) throw StudioFehler.Ungueltig("Der Gruppe sind noch Benutzer zugeordnet.");
            _k.Gruppen.Remove(g);
            Schreiben();
        }
    }

    public void Zuordnen(string benutzer, MuZuordnung? z)
    {
        if (_benutzer.Holen(benutzer) is null) throw StudioFehler.NichtGefunden("Der Benutzer");
        lock (_sperre)
        {
            if (z is null || z.GruppeId == 0) _k.Zuordnung.Remove(benutzer);
            else
            {
                if (_k.Gruppen.All(g => g.Id != z.GruppeId)) throw StudioFehler.NichtGefunden($"Die Gruppe Nr. {z.GruppeId}");
                _k.Zuordnung[benutzer] = z;
            }
            Schreiben();
        }
    }

    /// <summary>
    /// Wirksame Rechte eines Studio-Benutzers: Rolle Admin = alle Rechte, Rolle Leser = alles nur lesen; sonst die zugeordnete Gruppe
    /// (mit „Nur lesen“) bzw. ohne Zuordnung alle Bereiche bearbeiten (Einzelplatzbetrieb wie im Desktop ohne Datenbank).
    /// </summary>
    public (Rechte Rechte, string Gruppe) RechteVon(string benutzer)
    {
        var b = _benutzer.Holen(benutzer);
        if (b?.Rolle == Rollen.Admin) return (Rechte.Alle(), MultiUserDb.AdminGruppe);
        if (b is null || b.Rolle == Rollen.Leser)
        {
            var r = new Rechte { NurLesen = true };
            foreach (var x in Enum.GetValues<Rechtebereich>()) r.Stufen[x] = Stufe.NurLesen;
            r.Erlaubt.Add(Recht.Drucken);
            return (r, "Leser");
        }
        lock (_sperre)
        {
            if (_k.Zuordnung.TryGetValue(benutzer, out var z) && _k.Gruppen.FirstOrDefault(g => g.Id == z.GruppeId) is { } gr)
            {
                var r = Rechte.Aus(gr);
                r.NurLesen = z.NurLesen;
                r.Abteilung = z.Abteilung;
                return (r, gr.Name);
            }
        }
        var alle = Rechte.Alle();
        alle.Erlaubt.Remove(Recht.Administrator);
        alle.Erlaubt.Remove(Recht.Benutzerverwaltung);
        return (alle, "Planer");
    }

    // ---------------------------------------------------------------------------------------------- Sitzungen

    private string Anzeigename(string benutzer) => _benutzer.Holen(benutzer)?.Anzeigename is { Length: > 0 } a ? a : benutzer;

    /// <summary>Lebenszeichen eines Benutzers an einem Dokument (jede Anfrage; Tätigkeit optional, z. B. aktives Fenster).</summary>
    public void Lebenszeichen(string dok, string benutzer, string? taetigkeit = null)
    {
        var jetzt = DateTime.Now;
        _sitzungen.AddOrUpdate((dok, benutzer),
            _ => new MuSitzung(benutzer, Anzeigename(benutzer), jetzt, jetzt, taetigkeit ?? "", true),
            (_, s) => s with { Aktivitaet = jetzt, Taetigkeit = taetigkeit ?? s.Taetigkeit, Seit = jetzt - s.Aktivitaet > AktivTimeout ? jetzt : s.Seit });
    }

    public void Abmelden(string dok, string benutzer)
    {
        _sitzungen.TryRemove((dok, benutzer), out _);
        foreach (var k in _sperren.Keys.Where(k => k.Dok == dok && _sperren.TryGetValue(k, out var s) && s.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase)).ToList()) _sperren.TryRemove(k, out _);
        if (_exklusiv.TryGetValue(dok, out var e) && e.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase)) _exklusiv.TryRemove(dok, out _);
    }

    private bool IstAktiv(string dok, string benutzer, TimeSpan timeout) =>
        _sitzungen.TryGetValue((dok, benutzer), out var s) && DateTime.Now - s.Aktivitaet <= timeout;

    private TimeSpan SperrTimeout
    {
        get { lock (_sperre) return TimeSpan.FromMinutes(Math.Max(1, _k.SperrTimeoutMinuten)); }
    }

    public List<MuSitzung> Sitzungen(string dok) =>
        _sitzungen.Where(kv => kv.Key.Dok == dok).Select(kv => kv.Value with { Aktiv = DateTime.Now - kv.Value.Aktivitaet <= AktivTimeout })
            .Where(s => DateTime.Now - s.Aktivitaet <= TimeSpan.FromHours(1)).OrderByDescending(s => s.Aktiv).ThenBy(s => s.Anzeigename).ToList();

    // ---------------------------------------------------------------------------------------------- Sperren

    private void Aufraeumen(string dok)
    {
        var timeout = SperrTimeout;
        foreach (var (k, s) in _sperren.Where(kv => kv.Key.Dok == dok).ToList())
            if (!IstAktiv(dok, s.Benutzer, timeout)) _sperren.TryRemove(k, out _);
        if (_exklusiv.TryGetValue(dok, out var e) && !IstAktiv(dok, e.Benutzer, timeout)) _exklusiv.TryRemove(dok, out _);
    }

    public List<MuSperre> Sperren(string dok)
    {
        Aufraeumen(dok);
        return _sperren.Where(kv => kv.Key.Dok == dok).OrderBy(kv => kv.Key.Bereich)
            .Select(kv => new MuSperre(kv.Key.Bereich.ToString(), Rechte.Text(kv.Key.Bereich), kv.Value.Benutzer, Anzeigename(kv.Value.Benutzer), kv.Value.Seit)).ToList();
    }

    public (string Benutzer, string Anzeigename, DateTime Seit)? Exklusiv(string dok)
    {
        Aufraeumen(dok);
        return _exklusiv.TryGetValue(dok, out var e) ? (e.Benutzer, Anzeigename(e.Benutzer), e.Seit) : null;
    }

    /// <summary>Bereich zur alleinigen Bearbeitung sperren (andere Benutzer erhalten beim Ändern einen Sperrhinweis).</summary>
    public void Sperren(string dok, string benutzer, Rechtebereich bereich)
    {
        var (r, _) = RechteVon(benutzer);
        if (!r.DarfBearbeiten(bereich)) throw StudioFehler.Verboten($"Sie dürfen den Bereich {Rechte.Text(bereich)} nicht bearbeiten.");
        Aufraeumen(dok);
        Lebenszeichen(dok, benutzer);
        var neu = _sperren.GetOrAdd((dok, bereich), _ => (benutzer, DateTime.Now));
        if (!neu.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase))
            throw new StudioFehler(StatusCodes.Status423Locked, $"Der Bereich {Rechte.Text(bereich)} ist bereits von {Anzeigename(neu.Benutzer)} gesperrt (seit {neu.Seit:HH:mm}).");
    }

    public void Freigeben(string dok, string benutzer, Rechtebereich bereich, bool admin)
    {
        if (_sperren.TryGetValue((dok, bereich), out var s) && !s.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase) && !admin)
            throw StudioFehler.Verboten($"Die Sperre gehört {Anzeigename(s.Benutzer)}.");
        _sperren.TryRemove((dok, bereich), out _);
    }

    /// <summary>Exklusivbetrieb (Desktop: „Exklusivbetrieb ein/aus“) – nur möglich, wenn kein anderer Benutzer aktiv ist.</summary>
    public void ExklusivSetzen(string dok, string benutzer, bool ein, bool admin)
    {
        Aufraeumen(dok);
        if (!ein)
        {
            if (_exklusiv.TryGetValue(dok, out var e) && !e.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase) && !admin)
                throw StudioFehler.Verboten($"Der Exklusivbetrieb gehört {Anzeigename(e.Benutzer)}.");
            _exklusiv.TryRemove(dok, out _);
            return;
        }
        var (r, _) = RechteVon(benutzer);
        if (!r.DarfAendern(AenderungsArt.Alles)) throw StudioFehler.Verboten("Für den Exklusivbetrieb fehlt die Berechtigung zum Bearbeiten.");
        var andere = Sitzungen(dok).Where(s => s.Aktiv && !s.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase)).ToList();
        if (andere.Count > 0)
            throw new StudioFehler(StatusCodes.Status423Locked, $"Exklusivbetrieb nicht möglich: Es arbeiten noch andere Benutzer an der Datei ({string.Join(", ", andere.Select(a => a.Anzeigename))}).");
        Lebenszeichen(dok, benutzer);
        var x = _exklusiv.GetOrAdd(dok, _ => (benutzer, DateTime.Now));
        if (!x.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase))
            throw new StudioFehler(StatusCodes.Status423Locked, $"{Anzeigename(x.Benutzer)} arbeitet bereits exklusiv.");
    }

    // ---------------------------------------------------------------------------------------------- Prüfung ändernder Anfragen

    /// <summary>Rechtebereich eines Moduls (erstes Pfadsegment unter <c>/api/dokumente/{dok}/</c>); null = mehrere Bereiche/„Alles“.</summary>
    public static Rechtebereich? BereichVon(string segment) => segment.ToLowerInvariant() switch
    {
        "stammdaten" or "kommentare" => Rechtebereich.Stammdaten,
        "unterricht" => Rechtebereich.Unterricht,
        "stundenplan" or "planung" or "optimierung" or "pausenaufsicht" or "zeitwuensche" or "uebersicht" => Rechtebereich.Stundenplan,
        "vertretung" or "veranstaltungen" or "untplanweb" or "bereitschaft" or "formate" => Rechtebereich.Vertretungsplanung,
        "absenzen" => Rechtebereich.Absenzen,
        "kursplanung" => Rechtebereich.Kursplanung,
        "perioden" or "ugruppen" => Rechtebereich.Perioden,
        "einstellungen" or "schule" or "ferien" or "zeitraster" => Rechtebereich.BesondereDaten,
        "abrechnung" => Rechtebereich.Anrechnungen,
        _ => null,
    };

    /// <summary>Anfragen, die keine Daten ändern (bzw. nur den eigenen Zustand) und daher nie gesperrt werden.</summary>
    public static bool Frei(string segment) => segment is "" or "multiuser" or "speichern" or "schliessen" or "ereignisse" or "herunterladen" or "druck";

    /// <summary>
    /// Prüft eine ändernde Anfrage gegen Exklusivbetrieb, Bereichssperren anderer Benutzer und die Rechte (Gruppe). Liefert
    /// (Status, Meldung) oder null, wenn die Anfrage erlaubt ist.
    /// </summary>
    public (int Status, string Meldung)? Pruefen(string dok, string benutzer, string segment, string unter = "")
    {
        if (Frei(segment)) return null;
        Aufraeumen(dok);
        if (_exklusiv.TryGetValue(dok, out var e) && !e.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status423Locked, $"{Anzeigename(e.Benutzer)} arbeitet exklusiv an dieser Datei (seit {e.Seit:HH:mm}). Änderungen sind erst nach dem Ende des Exklusivbetriebs möglich.");
        var bereich = BereichVon(segment);
        var (r, gruppe) = RechteVon(benutzer);
        if (segment == "austausch" && !r.Hat(Recht.DatenImportExport)) return (403, $"Ihrer Gruppe {gruppe} fehlt das Recht „{Rechte.Text(Recht.DatenImportExport)}“.");
        if (segment == "untplanweb" && !r.Hat(Recht.WebExport) && !r.DarfBearbeiten(Rechtebereich.Vertretungsplanung)) return (403, $"Ihrer Gruppe {gruppe} fehlt das Recht „{Rechte.Text(Recht.WebExport)}“.");
        // Export an UntPlanWeb und Verbindungsprüfung ändern keine Plandaten (nur die Revision): keine Bereichssperre
        if (segment == "untplanweb" && unter is "export" or "pruefen" or "einstellungen" or "anfangsdatum" or "abgleich") return null;
        if (bereich is Rechtebereich b)
        {
            if (!(segment == "untplanweb" && r.Hat(Recht.WebExport)) && !r.DarfBearbeiten(b))
                return (403, $"Ihre Gruppe {gruppe} darf den Bereich {Rechte.Text(b)} nur {Rechte.Text(r.StufeVon(b)).ToLowerInvariant()}.");
            if (_sperren.TryGetValue((dok, b), out var s) && !s.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase))
                return (StatusCodes.Status423Locked, $"Der Bereich {Rechte.Text(b)} wird gerade von {Anzeigename(s.Benutzer)} bearbeitet und ist gesperrt (seit {s.Seit:HH:mm}).");
            return null;
        }
        // mehrere Bereiche (Import, Rückgängig …): mindestens ein Bereich bearbeitbar, keine Sperre eines anderen Benutzers
        if (!r.DarfAendern(AenderungsArt.Alles)) return (403, $"Ihre Gruppe {gruppe} darf diese Datei nicht bearbeiten.");
        var fremd = _sperren.Where(kv => kv.Key.Dok == dok && !kv.Value.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fremd.Count > 0 && segment is "rueckgaengig" or "wiederholen" or "austausch" or "bal-import")
            return (StatusCodes.Status423Locked, $"Nicht möglich, solange {string.Join(", ", fremd.Select(f => $"{Anzeigename(f.Value.Benutzer)} ({Rechte.Text(f.Key.Bereich)})"))} Bereiche gesperrt hat.");
        return null;
    }

    // ---------------------------------------------------------------------------------------------- Nachrichten

    public MuNachricht NachrichtSenden(string dok, string von, string? an, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw StudioFehler.Ungueltig("Bitte einen Text eingeben.");
        var n = new MuNachricht(Interlocked.Increment(ref _nachrichtId), von, string.IsNullOrWhiteSpace(an) ? null : an.Trim(), DateTime.Now, text.Trim());
        var liste = _nachrichten.GetOrAdd(dok, _ => new());
        lock (liste)
        {
            liste.Add(n);
            if (liste.Count > 200) liste.RemoveRange(0, liste.Count - 200);
        }
        return n;
    }

    public List<MuNachricht> Nachrichten(string dok, string benutzer, int nach)
    {
        if (!_nachrichten.TryGetValue(dok, out var liste)) return new();
        lock (liste)
            return liste.Where(n => n.Id > nach && (n.An is null || n.An.Equals(benutzer, StringComparison.OrdinalIgnoreCase) || n.Von.Equals(benutzer, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
