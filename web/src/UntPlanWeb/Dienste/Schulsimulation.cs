using System.Globalization;
using System.Text;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Einstellungen der Schulsimulation (Administration → Schulbetrieb simulieren).</summary>
public sealed record Simulationseinstellungen
{
    public bool Aktiv { get; init; }
    /// <summary>Beginn der Simulation (yyyy-MM-dd HH:mm): vorher liegende Stunden werden nicht nachgetragen.</summary>
    public string Start { get; init; } = "";
    /// <summary>Startwert des Zufalls (je Installation), damit Abläufe nach einem Neustart gleich bleiben.</summary>
    public long Seed { get; init; }
    /// <summary>Lehrkräfte (Kürzel), für die keine Automatik läuft (z. B. die Schulleitung, die selbst einträgt). null = Konten der Schulleitung bzw. Administration.</summary>
    public List<string>? Ausgenommen { get; init; }
    /// <summary>
    /// Frühere Einstellung „Fächer ohne Prüfungen“: gilt nur noch, solange keine Prüfungsfächer festgelegt sind (Vorbelegung der Listen unten).
    /// </summary>
    public List<string> OhnePruefung { get; init; } = Schulsimulation.StandardOhnePruefung.ToList();
    /// <summary>Fächer (Kürzel) mit Klassenarbeiten in der Sekundarstufe I. null = Deutsch, Mathematik, Englisch und zweite Fremdsprache.</summary>
    public List<string>? PruefungSekI { get; init; }
    /// <summary>Fächer (Kürzel) mit Prüfungen in der Oberstufe (EF, Q1, Q2) und in den Bildungsgängen des Berufskollegs. null = alle Unterrichtsfächer außer den Ausnahmen (Sport, Religion, Kunst, Musik …).</summary>
    public List<string>? PruefungSekII { get; init; }
    /// <summary>Weitere Aktionen der Lehrkräfte: Raumänderungen, Lehrstoff vorab, Hinweise an die Klasse.</summary>
    public bool Lehreraktionen { get; init; } = true;
    /// <summary>Anteil der Prüfungen (Prozent), für die die Lehrkraft einen eigenen Prüfungsraum festlegt (Oberstufe; in der Sek I seltener).</summary>
    public int Klausurraumanteil { get; init; } = 30;
    public bool Klassenbuch { get; init; } = true;
    public bool Abwesenheiten { get; init; } = true;
    public bool Entschuldigungen { get; init; } = true;
    public bool Krankmeldungen { get; init; } = true;
    public bool Pruefungen { get; init; } = true;
    public bool Sprechtag { get; init; } = true;
    /// <summary>Name der Verteilerliste im Mitteilungsmodul, an die die Lehrkräfte ihre Krankmeldungen schicken.</summary>
    public string Verteiler { get; init; } = "Vertretung";
}

/// <summary>Ergebnis eines Simulationslaufs.</summary>
public sealed record Simulationslauf(int Ausgefuehrt, int Uebersprungen, string Bis);

/// <summary>
/// Schulsimulation („Schulbetrieb simulieren“): Simulierte Lehrkräfte, Klassenleitungen und Erziehungsberechtigte arbeiten im Hintergrund wie im
/// echten Schulbetrieb, damit die Schulleitung realistisch mit UntPlanWeb arbeiten kann.
/// <list type="bullet">
/// <item>Klassenbuch: je gehaltener Stunde Abwesenheitskontrolle (kurz nach Stundenbeginn), Lehrstoff mit gelegentlicher Hausaufgabe (nach Stundenende bis
///   abends, selten erst Tage später, selten nie) und gelegentliche Klassenbucheinträge. Fehlende Schüler*innen sind über mehrere Tage krank
///   (keine zufälligen Einzelstunden), Arzttermine betreffen einzelne Stunden, Verspätungen gibt es nur in der 1. bis 3. Stunde.</item>
/// <item>Entschuldigungen: Die Klassenleitung entschuldigt unregelmäßig mit einigen Tagen Verzug, mit Begründung und Nachweis; manches bleibt offen bzw. unentschuldigt.</item>
/// <item>Krankmeldungen: Lehrkräfte melden sich bei der Verteilerliste „Vertretung“ krank (Erkältung bis längerer Ausfall, Verlängerungen, Gesundmeldungen),
///   jeden Schultag mindestens eine Meldung, meist nachmittags oder abends, manchmal nachts bzw. früh morgens. Die Absenz trägt die Schulleitung selbst
///   ein; kranke Lehrkräfte tragen aber nichts ein.</item>
/// <item>Prüfungen: je Klasse und Fach genau eine Klassenarbeit bzw. Klausur im Halbjahr (nicht in Sport, Religion, Kunst, Musik …), höchstens zwei je Woche
///   und eine je Tag, einige Wochen im Voraus eingetragen.</item>
/// <item>Sprechtag: Einladungen der Lehrkräfte und Buchungen der Erziehungsberechtigten in den Anmeldephasen.</item>
/// </list>
/// Alle Einträge laufen über die Dienste wie bei echten Benutzern (Rechte, Einstellungen, Historie) und stammen von eigenen, gesperrten Konten
/// (<see cref="KontoPraefix"/>), über die sie sich vollständig entfernen lassen. Ausgenommene Lehrkräfte (Standard: Schulleitung) bleiben unberührt.
/// Der Zufall ist je Ereignis fest (Startwert der Installation), ausgeführte Ereignisse werden vermerkt: Ein Neustart holt nach, ohne doppelt einzutragen.
/// </summary>
public sealed partial class Schulsimulation : BackgroundService
{
    public const string KontoPraefix = "sim~";
    private const string EinstellungSchluessel = "simulation.einstellungen", StandSchluessel = "simulation.stand";
    /// <summary>Höchstens so weit wird nach einer Pause nachgeholt.</summary>
    public static readonly TimeSpan Nachholen = TimeSpan.FromDays(14);
    public static readonly string[] StandardOhnePruefung = { "SP", "KR", "ER", "PPL", "KU", "MU", "FÖ", "BER", "KL", "SU", "NW", "GL" };
    /// <summary>Vorbelegung der Klassenarbeitsfächer in der Sekundarstufe I: Deutsch, Mathematik, Englisch und zweite Fremdsprache.</summary>
    public static readonly string[] StandardSekI = { "D", "M", "E", "F", "L", "S" };

    /// <summary>Wirksame Prüfungsfächer (Sek I, Sek II): festgelegte Listen bzw. die Vorbelegung aus den Fächern des Pakets (frühere Ausnahmen berücksichtigt).</summary>
    public static (HashSet<string> SekI, HashSet<string> SekII) Pruefungsfaecher(Stand? s, Simulationseinstellungen e)
    {
        var faecher = (s?.Paket.Elemente.Faecher ?? new()).Select(f => f.Kurz).Where(k => k.Length > 0).ToList();
        var ohne = (e.OhnePruefung ?? new()).Concat(StandardOhnePruefung).ToHashSet(Ci);
        var sekI = e.PruefungSekI ?? faecher.Where(f => StandardSekI.Contains(f, Ci) && !(e.OhnePruefung ?? new()).Contains(f, Ci)).ToList();
        // Sek II und Berufskolleg: alle Unterrichtsfächer außer Sport, Religion, Klassenleitung, Förderung und ähnlichen (auch allgemeine und berufliche Fächer des Berufskollegs)
        var ohneGruppe = new HashSet<string> { "sport", "religion", "klassenleitung", "koerper", "foerder", "musik", "kunst", "berufsorientierung" };
        string? Name(string k) => s?.Faecher.TryGetValue(k, out var f) == true ? f.Name : null;
        var sekII = e.PruefungSekII ?? faecher.Where(f => !ohne.Contains(f) && Zuordnung(f, Name(f)) is { } g && !ohneGruppe.Contains(g.Id)).ToList();
        return (sekI.ToHashSet(Ci), sekII.ToHashSet(Ci));
    }
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture, De = new("de-DE");
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Wochentage = { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Einstellungen _einstellungen;
    private readonly Paketdienst _paket;
    private readonly Plandienst _plan;
    private readonly Klassenbuchdienst _kb;
    private readonly Entschuldigungsdienst _ent;
    private readonly Mitteilungsdienst _mit;
    private readonly Benutzerdienst _bd;
    private readonly Rueckmeldungsdienst _rm;
    private readonly Listendienst _listen;
    private readonly Verwaltungsdienst _vw;
    private readonly Sprechtagdienst _st;
    private readonly ILogger<Schulsimulation> _log;
    private readonly object _sperre = new();

    public Schulsimulation(Db db, Uhr uhr, Einstellungen einstellungen, Paketdienst paket, Plandienst plan, Klassenbuchdienst kb, Entschuldigungsdienst ent,
        Mitteilungsdienst mit, Benutzerdienst bd, Rueckmeldungsdienst rm, Listendienst listen, Verwaltungsdienst vw, Sprechtagdienst st, ILogger<Schulsimulation> log)
    {
        _db = db; _uhr = uhr; _einstellungen = einstellungen; _paket = paket; _plan = plan; _kb = kb; _ent = ent; _mit = mit; _bd = bd; _rm = rm;
        _listen = listen; _vw = vw; _st = st; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { if (Laden().Aktiv) Lauf(); }
            catch (Exception ex) { _log.LogWarning(ex, "Schulsimulation: Lauf fehlgeschlagen"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (OperationCanceledException) { return; }
        }
    }

    // ------------------------------------------------------------------------------------------ Einstellungen

    public Simulationseinstellungen Laden()
    {
        var json = _einstellungen.Lesen(EinstellungSchluessel);
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<Simulationseinstellungen>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Ausgenommene Lehrkräfte: gespeicherte Liste bzw. die Kürzel der Konten von Schulleitung und Administration.</summary>
    public List<string> Ausgenommen(Simulationseinstellungen e) => e.Ausgenommen ?? _bd.Alle()
        .Where(b => b.Rolle is Rollen.Admin or Rollen.Leitung && b.Element.Length > 0).Select(b => b.Element).Distinct(Ci).ToList();

    /// <summary>Einstellungen speichern. Beim Einschalten beginnt die Simulation jetzt (neuer Startwert) und die Verteilerliste wird bei Bedarf angelegt.</summary>
    public Simulationseinstellungen Speichern(Benutzer ich, Simulationseinstellungen neu)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        lock (_sperre)
        {
            var alt = Laden();
            var verteiler = string.IsNullOrWhiteSpace(neu.Verteiler) ? "Vertretung" : neu.Verteiler.Trim();
            if (verteiler.Length > 100) throw new ArgumentException("Der Name der Verteilerliste ist zu lang.");
            var e = neu with
            {
                Verteiler = verteiler,
                Ausgenommen = neu.Ausgenommen?.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(Ci).ToList() ?? alt.Ausgenommen ?? Ausgenommen(alt),
                OhnePruefung = neu.OhnePruefung is null ? alt.OhnePruefung : neu.OhnePruefung.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(Ci).ToList(),
                Klausurraumanteil = Math.Clamp(neu.Klausurraumanteil, 0, 100),
                PruefungSekI = Liste(neu.PruefungSekI) ?? alt.PruefungSekI,
                PruefungSekII = Liste(neu.PruefungSekII) ?? alt.PruefungSekII,
                Start = alt.Start, Seed = alt.Seed,
            };
            if (e.Aktiv && (!alt.Aktiv || alt.Start.Length == 0))
            {
                e = e with { Start = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv), Seed = alt.Seed != 0 ? alt.Seed : Random.Shared.NextInt64(1, long.MaxValue) };
                _einstellungen.Schreiben(StandSchluessel, e.Start);
                VerteilerAnlegen(ich, verteiler);
            }
            _einstellungen.Schreiben(EinstellungSchluessel, JsonSerializer.Serialize(e, Json));
            _cache.Clear();
            _tageCache.Clear();
            return e;
        }
    }

    private static List<string>? Liste(List<string>? l) => l?.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(Ci).ToList();

    /// <summary>Verteilerliste der Krankmeldungen anlegen (Schulleitung und Administration), falls es noch keine Liste dieses Namens gibt.</summary>
    private void VerteilerAnlegen(Benutzer ich, string name)
    {
        if (Verteilerliste(name) is not null) return;
        try { _mit.ListeSpeichern(ich, _paket.Aktuell, null, name, new[] { "gruppe:leitung" }); }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { _log.LogInformation("Schulsimulation: Verteilerliste nicht angelegt ({Fehler})", ex.Message); }
    }

    private (int Besitzer, List<string> Mitglieder)? Verteilerliste(string name) => _db.Liste(
        "SELECT besitzer_id, mitglieder FROM mitteilung_listen WHERE name = @n COLLATE NOCASE ORDER BY id",
        r => ((int Besitzer, List<string> Mitglieder)?)(r.Zahl("besitzer_id"), JsonSerializer.Deserialize<List<string>>(r.Text("mitglieder")) ?? new List<string>()),
        Db.P("@n", name)).FirstOrDefault();

    /// <summary>Stand für die Verwaltungsseite: Einstellungen, Lehrkräfte und Fächer zur Auswahl, Anzahl der simulierten Einträge.</summary>
    public object Status()
    {
        var e = Laden();
        var s = _paket.Aktuell;
        int Zahl(string sql) => Convert.ToInt32(_db.Wert(sql) ?? 0);
        const string Konten = "(SELECT id FROM benutzer WHERE login LIKE 'sim~%')";
        return new
        {
            einstellungen = e with
            {
                Ausgenommen = Ausgenommen(e),
                PruefungSekI = Pruefungsfaecher(s, e).SekI.Order(StringComparer.Ordinal).ToList(),
                PruefungSekII = Pruefungsfaecher(s, e).SekII.Order(StringComparer.Ordinal).ToList(),
            },
            stand = _einstellungen.Lesen(StandSchluessel) ?? "",
            lehrer = (s?.Paket.Elemente.Lehrer ?? new()).Where(l => l.Kurz is not ("---" or "???" or "+++")).Select(l => new { l.Kurz, name = $"{l.Vorname} {l.Name}".Trim() }),
            faecher = (s?.Paket.Elemente.Faecher ?? new()).Select(f => new { f.Kurz, f.Name }),
            verteilerVorhanden = Verteilerliste(e.Verteiler) is not null,
            nachpflegen = _nachpflegen,
            schuljahrVon = s is null ? "" : Plandienst.Iso(Halbjahre(s)[0].Von),
            gestern = Plandienst.Iso(_uhr.Heute.AddDays(-1)),
            zahlen = new
            {
                lehrstoff = Zahl($"SELECT COUNT(*) FROM klassenbuch WHERE thema <> '' AND benutzer_id IN {Konten}"),
                abwesenheiten = Zahl($"SELECT COUNT(*) FROM fehlzeiten WHERE benutzer_id IN {Konten}"),
                vermerke = Zahl($"SELECT COUNT(*) FROM vermerke WHERE benutzer_id IN {Konten}"),
                krankmeldungen = Zahl($"SELECT COUNT(*) FROM mitteilungen WHERE absender_id IN {Konten} AND betreff NOT LIKE 'Sprechtag%'"),
                pruefungen = Zahl($"SELECT COUNT(*) FROM rueckmeldungen WHERE typ = 'klausur' AND benutzer_id IN {Konten}"),
                sprechtag = Zahl($"SELECT COUNT(*) FROM st_termine WHERE benutzer_id IN {Konten} OR erstellt_von LIKE 'sim~%'"),
                raumaenderungen = Zahl($"SELECT COUNT(*) FROM rueckmeldungen WHERE typ = 'raumbuchung' AND benutzer_id IN {Konten}"),
                entschuldigt = Zahl($"SELECT COUNT(*) FROM fehlzeit_entschuldigungen WHERE benutzer_id IN {Konten}"),
            },
        };
    }

    // ------------------------------------------------------------------------------------------ Entfernen

    /// <summary>Alle simulierten Datensätze und Konten entfernen (Einstellungen bleiben, die Simulation wird ausgeschaltet).</summary>
    public int Entfernen(Benutzer ich)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        if (_nachpflegen.Laeuft) throw new ArgumentException("Bitte warten, bis das Nachpflegen abgeschlossen ist.");
        lock (_sperre)
        {
            var konten = _db.Liste("SELECT id FROM benutzer WHERE login LIKE 'sim~%'", r => r.Zahl("id"));
            var n = 0;
            _db.Transaktion(c =>
            {
                const string K = "(SELECT id FROM benutzer WHERE login LIKE 'sim~%')";
                foreach (var sql in new[]
                {
                    $"DELETE FROM fehlzeit_entschuldigungen WHERE benutzer_id IN {K}",
                    $"DELETE FROM fehlzeiten WHERE benutzer_id IN {K}",
                    $"DELETE FROM klassenbuch WHERE benutzer_id IN {K}",
                    $"DELETE FROM hausaufgaben_eintraege WHERE benutzer_id IN {K}",
                    $"DELETE FROM vermerke WHERE benutzer_id IN {K}",
                    $"DELETE FROM rueckmeldungen WHERE benutzer_id IN {K}",
                    $"DELETE FROM st_termine WHERE benutzer_id IN {K} OR erstellt_von LIKE 'sim~%'",
                    $"DELETE FROM mitteilung_empfaenger WHERE mitteilung_id IN (SELECT id FROM mitteilungen WHERE absender_id IN {K})",
                    $"DELETE FROM mitteilung_anhaenge WHERE mitteilung_id IN (SELECT id FROM mitteilungen WHERE absender_id IN {K})",
                    $"DELETE FROM mitteilungen WHERE absender_id IN {K}",
                    $"DELETE FROM mitteilung_empfaenger WHERE benutzer_id IN {K}",
                    $"DELETE FROM mitteilung_listen WHERE besitzer_id IN {K}",
                    $"DELETE FROM benutzer_schueler WHERE benutzer_id IN {K}",
                    $"DELETE FROM benutzer_rechte WHERE benutzer_id IN {K}",
                    "DELETE FROM benutzer WHERE login LIKE 'sim~%'",
                    "DELETE FROM simulation_ereignisse",
                })
                    n += Db.Ausfuehren(c, sql);
            });
            var e = Laden();
            _einstellungen.Schreiben(EinstellungSchluessel, JsonSerializer.Serialize(e with { Aktiv = false, Start = "" }, Json));
            _einstellungen.Schreiben(StandSchluessel, "");
            _cache.Clear();
            _tageCache.Clear();
            _log.LogInformation("Schulsimulation: {Anzahl} Datensätze von {Konten} Konten entfernt", n, konten.Count);
            return n;
        }
    }

    // ------------------------------------------------------------------------------------------ Lauf

    /// <summary>Ereignis der Simulation: fester Schlüssel, Zeitpunkt und Aktion (läuft mit dieser Zeit als „jetzt“).</summary>
    public sealed record Ereignis(string Schluessel, DateTime Zeit, Action Aktion);

    /// <summary>Alle fälligen Ereignisse bis jetzt ausführen (Hintergrunddienst: jede Minute; Tests: mit fester Uhr).</summary>
    public Simulationslauf Lauf()
    {
        lock (_sperre)
        {
            var e = Laden();
            var jetzt = _uhr.Jetzt;
            if (!e.Aktiv || _paket.Aktuell is not { } s || !DateTime.TryParse(e.Start, Inv, DateTimeStyles.None, out var start)) return new(0, 0, "");
            var stand = DateTime.TryParse(_einstellungen.Lesen(StandSchluessel), Inv, DateTimeStyles.None, out var st) ? st : start;
            var von = new[] { stand, start, jetzt - Nachholen }.Max();
            if (von >= jetzt) return new(0, 0, Zeit(jetzt));
            var erledigt = _db.Liste("SELECT schluessel FROM simulation_ereignisse WHERE zeit >= @z", r => r.Text("schluessel"),
                Db.P("@z", Zeit(von.AddDays(-3)))).ToHashSet(StringComparer.Ordinal);
            var faellig = Ereignisse(s, e, start, von, jetzt).Where(x => x.Zeit > von.AddMinutes(-1) && x.Zeit <= jetzt && !erledigt.Contains(x.Schluessel))
                .OrderBy(x => x.Zeit).ThenBy(x => x.Schluessel, StringComparer.Ordinal).ToList();
            int ok = 0, weg = 0;
            foreach (var x in faellig)
            {
                string ergebnis;
                using (_uhr.Simuliert(x.Zeit))
                {
                    try { x.Aktion(); ergebnis = "ok"; ok++; }
                    catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException)
                    { ergebnis = ex.Message.Length > 290 ? ex.Message[..290] : ex.Message; weg++; }
                }
                _db.Ausfuehren("INSERT INTO simulation_ereignisse (schluessel, zeit, ausgefuehrt, ergebnis) VALUES (@s, @z, @a, @e) ON CONFLICT(schluessel) DO NOTHING",
                    Db.P("@s", x.Schluessel.Length > 190 ? x.Schluessel[..190] : x.Schluessel), Db.P("@z", Zeit(x.Zeit)), Db.P("@a", Zeit(jetzt)), Db.P("@e", ergebnis));
            }
            _einstellungen.Schreiben(StandSchluessel, Zeit(jetzt));
            return new(ok, weg, Zeit(jetzt));
        }
    }

    private static string Zeit(DateTime t) => t.ToString("yyyy-MM-dd HH:mm", Inv);
    private static DateTime Um(DateOnly d, int minuten) => d.ToDateTime(TimeOnly.MinValue).AddMinutes(minuten);
    private static DateTime Um(DateOnly d, string hhmm) => TimeOnly.TryParseExact(hhmm, "HH:mm", Inv, DateTimeStyles.None, out var t) ? d.ToDateTime(t) : d.ToDateTime(new TimeOnly(8, 0));

    /// <summary>Ereignisse, die im Zeitraum (von, bis] fällig werden können.</summary>
    public List<Ereignis> Ereignisse(Stand s, Simulationseinstellungen e, DateTime start, DateTime von, DateTime bis)
    {
        var res = new List<Ereignis>();
        var w = Welt(s, e, start, bis);
        // Ereignisse eines Schultages liegen zwischen dem Nachmittag davor und einigen Tagen danach (späte Einträge, Entschuldigungen)
        for (var d = DateOnly.FromDateTime(von).AddDays(-4); d <= DateOnly.FromDateTime(bis).AddDays(2); d = d.AddDays(1))
        {
            if (d < DateOnly.FromDateTime(start) || !w.Schultag(d)) continue;
            if (e.Klassenbuch || e.Abwesenheiten || e.Lehreraktionen) res.AddRange(TagesereignisseGemerkt(s, e, w, d));
        }
        if (e.Krankmeldungen) res.AddRange(w.Ausfaelle.SelectMany(a => Meldungen(s, e, w, a)));
        if (e.Entschuldigungen && e.Abwesenheiten) res.AddRange(w.Schuelerausfaelle.Select(a => Entschuldigung(s, e, w, a)).OfType<Ereignis>());
        if (e.Pruefungen) res.AddRange(Pruefungsereignisse(s, e, w, start, bis));
        if (e.Sprechtag) res.AddRange(Sprechtagereignisse(s, e, w, start));
        return res;
    }

    private readonly Dictionary<string, (DateTime Erzeugt, List<Ereignis> Liste)> _tageCache = new();

    /// <summary>Ereignisse eines Schultages, einige Minuten zwischengespeichert (jede Minute läuft die Simulation; Pläne ändern sich selten).</summary>
    private List<Ereignis> TagesereignisseGemerkt(Stand s, Simulationseinstellungen e, Weltstand w, DateOnly d)
    {
        var k = $"{d:yyyy-MM-dd}|{s.Empfangen}|{w.Seed}|{e.Start}|{e.Klassenbuch}|{e.Abwesenheiten}|{e.Lehreraktionen}|{string.Join(",", w.Ausgenommen.Order(StringComparer.Ordinal))}";
        if (_tageCache.TryGetValue(k, out var c) && DateTime.UtcNow - c.Erzeugt < TimeSpan.FromMinutes(10)) return c.Liste;
        if (_tageCache.Count > 64) _tageCache.Clear();
        var liste = Tagesereignisse(s, e, w, d).ToList();
        _tageCache[k] = (DateTime.UtcNow, liste);
        return liste;
    }

    // ------------------------------------------------------------------------------------------ Welt: Schultage, Ausfälle der Lehrkräfte und Schüler*innen

    /// <summary>Feststehende Grundlage eines Laufs (zwischengespeichert je Paket, Einstellungen und Tag).</summary>
    public sealed class Weltstand
    {
        public required long Seed { get; init; }
        public required DateOnly Start { get; init; }
        public required DateOnly Bis { get; init; }
        public required HashSet<DateOnly> Schultage { get; init; }
        public required List<DateOnly> Tagesliste { get; init; }
        public required HashSet<string> Ausgenommen { get; init; }
        public required List<Ausfall> Ausfaelle { get; init; }
        public required List<Schuelerausfall> Schuelerausfaelle { get; init; }
        public Dictionary<(int Schueler, DateOnly Tag), Schuelerausfall> SchuelerAm { get; } = new();
        public Dictionary<string, List<string>> KlassenDerLehrkraft { get; init; } = new(Ci);
        public bool Schultag(DateOnly d) => Schultage.Contains(d);
        public bool Krank(string lehrer, DateOnly d, int stunde) => Ausfaelle.Any(a => Ci.Equals(a.Lehrer, lehrer) && a.Tage.Contains(d) && (!a.Art.Teil || (stunde >= a.StundeVon && stunde <= a.StundeBis)));
        /// <summary>n-ter Schultag nach d (n = 0: d bzw. der nächste Schultag).</summary>
        public DateOnly Schultag(DateOnly d, int n)
        {
            var i = Tagesliste.BinarySearch(d);
            if (i < 0) i = ~i;
            i += n;
            return i < Tagesliste.Count ? Tagesliste[Math.Max(0, i)] : d.AddDays(n + 2);
        }
    }

    /// <summary>Ausfall einer Lehrkraft: Art, Schultage, bei einem Arzttermin die Stunden.</summary>
    public sealed record Ausfall(string Lehrer, Ausfallart Art, List<DateOnly> Tage, int StundeVon, int StundeBis)
    {
        public DateOnly Von => Tage[0];
        public DateOnly Bis => Tage[^1];
    }

    /// <summary>Abwesenheit einer Schülerin bzw. eines Schülers: krank (ganze Tage), arzt (Stunden eines Tages), verspaetet (Minuten in der ersten Stunde).</summary>
    public sealed record Schuelerausfall(int SchuelerId, string Klasse, string Art, List<DateOnly> Tage, int StundeVon, int StundeBis, int Minuten, string Grund)
    {
        public DateOnly Von => Tage[0];
        public DateOnly Bis => Tage[^1];
    }

    private readonly Dictionary<string, Weltstand> _cache = new();

    public Weltstand Welt(Stand s, Simulationseinstellungen e, DateTime start, DateTime bis)
    {
        var ausgenommen = Ausgenommen(e).ToHashSet(Ci);
        var bisTag = DateOnly.FromDateTime(bis).AddDays(21);
        var schluessel = $"{e.Seed}|{e.Start}|{s.Empfangen}|{string.Join(",", ausgenommen.Order(StringComparer.Ordinal))}|{bisTag:yyyy-MM-dd}|{s.Zusatzferien.Count}";
        if (_cache.TryGetValue(schluessel, out var w)) return w;
        if (_cache.Count > 8) _cache.Clear();
        var startTag = DateOnly.FromDateTime(start);
        var tage = new List<DateOnly>();
        for (var d = startTag.AddDays(-7); d <= bisTag.AddDays(30); d = d.AddDays(1))
            if (!_plan.Frei(s, d).Frei) tage.Add(d);
        var klassenDerLehrkraft = new Dictionary<string, List<string>>(Ci);
        foreach (var u in s.Unterricht.Values)
            foreach (var z in u.Zeilen.Where(z => z.Lehrer.Length > 0))
            {
                if (!klassenDerLehrkraft.TryGetValue(z.Lehrer, out var l)) klassenDerLehrkraft[z.Lehrer] = l = new();
                foreach (var k in z.Klassen) if (!l.Contains(k, Ci)) l.Add(k);
            }
        w = new Weltstand
        {
            Seed = e.Seed, Start = startTag, Bis = bisTag, Schultage = tage.ToHashSet(), Tagesliste = tage, Ausgenommen = ausgenommen,
            Ausfaelle = AusfaelleBerechnen(s, e.Seed, startTag, bisTag, tage, ausgenommen, klassenDerLehrkraft),
            Schuelerausfaelle = SchuelerausfaelleBerechnen(s, e.Seed, startTag, bisTag, tage), KlassenDerLehrkraft = klassenDerLehrkraft,
        };
        foreach (var a in w.Schuelerausfaelle)
            foreach (var d in a.Tage) w.SchuelerAm.TryAdd((a.SchuelerId, d), a);
        _cache[schluessel] = w;
        return w;
    }

    /// <summary>
    /// Ausfälle der Lehrkräfte ab dem Tag nach dem Start: je Lehrkraft und Schultag eine kleine Wahrscheinlichkeit; beginnt an einem Schultag kein
    /// Ausfall, meldet sich eine zufällige gesunde Lehrkraft kurz krank (jeden Schultag mindestens eine Krankmeldung).
    /// </summary>
    public static List<Ausfall> AusfaelleBerechnen(Stand s, long seed, DateOnly start, DateOnly bis, List<DateOnly> tage, HashSet<string> ausgenommen,
        Dictionary<string, List<string>> klassenDerLehrkraft)
    {
        var lehrer = s.Paket.Elemente.Lehrer.Select(l => l.Kurz).Where(k => klassenDerLehrkraft.ContainsKey(k) && !ausgenommen.Contains(k) && k is not ("---" or "???" or "+++"))
            .Order(StringComparer.Ordinal).ToList();
        var res = new List<Ausfall>();
        if (lehrer.Count == 0) return res;
        var krankBis = new Dictionary<string, DateOnly>(Ci);
        var kurz = Ausfallarten.Where(a => a.Max <= 3).ToList();
        for (var i = 0; i < tage.Count; i++)
        {
            var d = tage[i];
            if (d <= start || d > bis) continue;
            var neu = false;
            foreach (var l in lehrer)
            {
                if (krankBis.TryGetValue(l, out var kb) && kb >= d) continue;
                var z = Zufall.Fuer(seed, "lk", l, d);
                if (!z.Chance(0.0035)) continue;
                res.Add(Neu(l, z.Gewichtet(Ausfallarten, a => a.Gewicht), z, i));
                neu = true;
            }
            if (neu) continue;
            var gesund = lehrer.Where(l => !(krankBis.TryGetValue(l, out var kb) && kb >= d)).ToList();
            if (gesund.Count == 0) continue;
            var zp = Zufall.Fuer(seed, "lkp", d);
            res.Add(Neu(zp.Wahl(gesund), zp.Gewichtet(kurz, a => a.Gewicht), zp, i));
        }
        return res;

        Ausfall Neu(string l, Ausfallart art, Zufall z, int i)
        {
            var dauer = z.Zahl(art.Min, art.Max);
            var t = tage.Skip(i).Take(dauer).ToList();
            var von = art.Teil ? z.Zahl(1, 5) : 1;
            var a = new Ausfall(l, art, t, von, art.Teil ? Math.Min(10, von + z.Zahl(1, 3)) : 99);
            krankBis[l] = t[^1];
            return a;
        }
    }

    /// <summary>Abwesenheiten der Schüler*innen: Krankheiten über mehrere Tage, einzelne Arzttermine, seltene Verspätungen.</summary>
    public static List<Schuelerausfall> SchuelerausfaelleBerechnen(Stand s, long seed, DateOnly start, DateOnly bis, List<DateOnly> tage)
    {
        var res = new List<Schuelerausfall>();
        var lauf = tage.Where(d => d >= start && d <= bis).ToList();
        var bildungsgaenge = new Dictionary<string, string>(Ci);
        foreach (var sch in s.Schueler.OrderBy(x => x.Id))
        {
            var frei = DateOnly.MinValue;
            // Berufskolleg: Vollzeit-Bildungsgänge mit mehr Fehlzeiten als Teilzeit; Berufsschule mit betrieblichen Gründen und mehr Verspätungen (Pendeln)
            if (!bildungsgaenge.TryGetValue(sch.Klasse, out var bg)) bildungsgaenge[sch.Klasse] = bg = Bildungsgang(s, sch.Klasse);
            var f = Fehlfaktor(bg);
            double gKrank = 0.0085 * f, gArzt = gKrank + 0.003 * f, gSpaet = gArzt + 0.004 * (bg == "bs" ? 1.4 : f), gBetrieb = gSpaet + (bg == "bs" ? 0.0025 : 0);
            for (var i = 0; i < lauf.Count; i++)
            {
                var d = lauf[i];
                if (d <= frei) continue;
                var z = Zufall.Fuer(seed, "sch", sch.Id, d);
                var w = z.Wert();
                if (w < gKrank)
                {
                    var dauer = z.Gewichtet(new[] { (1, 35.0), (2, 25.0), (3, 20.0), (4, 9.0), (5, 6.0), (8, 4.0), (10, 1.0) }, x => x.Item2).Item1;
                    var t = lauf.Skip(i).Take(dauer).ToList();
                    res.Add(new(sch.Id, sch.Klasse, "krank", t, 1, 99, 0, z.Chance(0.9) ? "K" : "F"));
                    frei = t[^1];
                }
                else if (w < gArzt)
                {
                    // Arzttermin: einige Stunden am Vormittag bzw. ab der 4. Stunde nach Hause
                    var von = z.Chance(0.5) ? z.Zahl(1, 3) : z.Zahl(4, 6);
                    res.Add(new(sch.Id, sch.Klasse, "arzt", new() { d }, von, z.Chance(0.4) ? 99 : von + z.Zahl(0, 2), 0, "A"));
                    frei = d;
                }
                else if (w < gSpaet)
                {
                    res.Add(new(sch.Id, sch.Klasse, "verspaetet", new() { d }, 0, 0, z.Gewichtet(new[] { (3, 20.0), (5, 25.0), (8, 20.0), (10, 15.0), (15, 12.0), (20, 8.0) }, x => x.Item2).Item1,
                        z.Chance(bg == "bs" ? 0.7 : 0.5) ? "Z" : "v"));
                    frei = d;
                }
                else if (w < gBetrieb)
                {
                    // Berufsschule: Betrieb hält zurück (Montage, Kundentermin, Personalengpass) für einen Tag
                    res.Add(new(sch.Id, sch.Klasse, "betrieb", new() { d }, 1, 99, 0, "B"));
                    frei = d;
                }
            }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------ Konten

    /// <summary>Gesperrtes Konto der simulierten Lehrkraft (Rolle Lehrkraft, Kürzel); legt es beim ersten Bedarf an.</summary>
    public Benutzer LehrerKonto(Stand s, string kuerzel)
    {
        var l = s.Lehrer.TryGetValue(kuerzel, out var x) ? x : null;
        var k = l?.Kurz ?? kuerzel;
        var login = KontoPraefix + "l." + k.ToLowerInvariant();
        return _bd.NachLogin(login) ?? Anlegen(login, Rollen.Lehrer, k, l is null ? k : $"{l.Vorname} {l.Name}".Trim());
    }

    /// <summary>Gesperrtes Elternkonto zur Schülerin bzw. zum Schüler (Sprechtagsbuchungen).</summary>
    public Benutzer ElternKonto(Stand s, int schuelerId)
    {
        var login = KontoPraefix + "e." + schuelerId.ToString(Inv);
        if (_bd.NachLogin(login) is { } b) return b;
        var sch = s.Schueler.FirstOrDefault(x => x.Id == schuelerId);
        // Berufsschule: Ausbilderin bzw. Ausbilder des Betriebs; sonst die Familie
        var name = sch is null ? "Erziehungsberechtigte" : Bildungsgang(s, sch.Klasse) == "bs" ? $"Ausbildungsbetrieb ({sch.Vorname} {sch.Name})" : $"Familie {sch.Name}";
        var neu = Anlegen(login, Rollen.Eltern, "", name);
        _bd.SchuelerZuordnen(neu.Id, new[] { schuelerId });
        return _bd.Holen(neu.Id)!;
    }

    private Benutzer Anlegen(string login, string rolle, string element, string name)
    {
        var b = _bd.Anlegen(login, Sicherheit.Zufall(32), rolle, element, name);
        _bd.Sperren(b.Id, true);
        return _bd.Holen(b.Id)!;
    }
}

/// <summary>Fester Zufall (SplitMix64) aus einem Startwert und den Teilen des Ereignisses: gleiche Eingabe, gleiche Folge.</summary>
public sealed class Zufall
{
    private ulong _s;

    public Zufall(ulong startwert) => _s = startwert;

    public static Zufall Fuer(long seed, params object[] teile)
    {
        var h = 14695981039346656037UL ^ (ulong)seed;
        foreach (var t in teile)
        {
            var text = t switch { DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _ => Convert.ToString(t, CultureInfo.InvariantCulture) ?? "" };
            foreach (var c in text) { h ^= c; h *= 1099511628211UL; }
            h ^= '|'; h *= 1099511628211UL;
        }
        var z = new Zufall(h);
        z.Naechste();
        return z;
    }

    public ulong Naechste()
    {
        var z = _s += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Gleichverteilt in [0, 1).</summary>
    public double Wert() => (Naechste() >> 11) * (1.0 / (1UL << 53));
    public bool Chance(double p) => Wert() < p;
    /// <summary>Ganze Zahl von min bis max (einschließlich).</summary>
    public int Zahl(int min, int max) => max <= min ? min : min + (int)(Wert() * (max - min + 1));
    public T Wahl<T>(IReadOnlyList<T> l) => l[Math.Min(l.Count - 1, (int)(Wert() * l.Count))];

    public T Gewichtet<T>(IReadOnlyList<T> l, Func<T, double> gewicht)
    {
        var summe = l.Sum(gewicht);
        var x = Wert() * summe;
        foreach (var e in l) { x -= gewicht(e); if (x < 0) return e; }
        return l[^1];
    }

    /// <summary>Platzhalter {a}, {b}, {s} durch kleine Zahlen ersetzen (Buchseiten, Aufgaben).</summary>
    public string Zahlen(string text) => text.Replace("{s}", Zahl(12, 210).ToString(CultureInfo.InvariantCulture))
        .Replace("{a}", Zahl(1, 6).ToString(CultureInfo.InvariantCulture)).Replace("{b}", Zahl(7, 12).ToString(CultureInfo.InvariantCulture));
}
