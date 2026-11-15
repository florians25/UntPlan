using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

// ================================================================================================================================
// Sprechtag (neu): Die Administration legt einen Sprechtag für ein Schuljahr an und konfiguriert ihn in drei Schritten
// (1. Allgemeine Informationen: Name, Dauer der Zeitblöcke, Online-Sprechtag; 2. Zeiträume: Abschnitte und zwei Anmeldephasen;
// 3. Buchungsregeln und Rechte). Nach dem Veröffentlichen durchläuft er automatisch die Phasen
// Entwurf → Verwaltung LK → 1. Anmeldephase → 1. Anmeldephase beendet → 2. Anmeldephase → Anmeldung geschlossen → In Durchführung →
// Abgeschlossen. Lehrkräfte (bzw. die Administration in ihrem Namen) wählen Räume je Abschnitt, tragen Abwesenheiten ein, senden
// Einladungen (Erziehungsberechtigte wählen die Uhrzeit selbst) und legen Terminanfragen mit fester Uhrzeit an (Annehmen/Ablehnen).
// Erziehungsberechtigte buchen in der 1. Anmeldephase nur über Einladungen bzw. Terminanfragen, ab der 2. Anmeldephase frei.
// ================================================================================================================================

/// <summary>Abschnitt eines Sprechtags (z. B. Nachmittag): Name, Datum (yyyy-MM-dd), Beginn und Ende (HH:mm).</summary>
public sealed record StAbschnitt(string Name, string Datum, string Von, string Bis);

/// <summary>Berechtigungen der Lehrkräfte (alle standardmäßig an).</summary>
public sealed record StRechte(bool RaeumeWaehlen = true, bool AbwesenheitenEintragen = true, bool TermineBearbeiten = true, bool TermineBuchen = true);

/// <summary>Buchungsregeln (optional): Zeitblöcke am Stück bis zur Pause, höchstens Zeitblöcke je Schüler*in und Lehrkraft, gesperrte Blöcke zwischen zwei Terminen.</summary>
public sealed record StRegeln(int? BloeckeBisPause = null, int? MaxBloeckeProSchueler = null, int? GesperrteBloecke = null);

/// <summary>Konfiguration eines Sprechtags (wird laufend gespeichert; Anmeldephasen als yyyy-MM-ddTHH:mm).</summary>
public sealed record StKonfig
{
    public string Name { get; init; } = "";
    public int Dauer { get; init; } = 10;
    public bool Online { get; init; }
    public List<StAbschnitt> Abschnitte { get; init; } = new();
    public string Phase1Von { get; init; } = "";
    public string Phase1Bis { get; init; } = "";
    public string Phase2Von { get; init; } = "";
    public string Phase2Bis { get; init; } = "";
    public StRechte Rechte { get; init; } = new();
    public StRegeln Regeln { get; init; } = new();
}

public sealed record Sprechtag(int Id, string Schuljahr, string SchuljahrVon, string SchuljahrBis, StKonfig K, string Veroeffentlicht, string Erstellt)
{
    public bool IstVeroeffentlicht => Veroeffentlicht.Length > 0;
}

/// <summary>
/// Termin, Einladung bzw. Terminanfrage. Art „einladung“ (ohne Uhrzeit, Erziehungsberechtigte wählen selbst) bzw. „termin“ (feste Uhrzeit).
/// Status: einladung (offen), angefragt (Terminanfrage wartet auf Annahme), bestaetigt, abgelehnt, abgesagt.
/// </summary>
public sealed record StTermin(int Id, int SprechtagId, string Lehrer, int Abschnitt, string Beginn, string Ende, int SchuelerId, string Art, string Status,
    bool Verpflichtend, string Link, string Anmerkung, string ErstelltVon, int? BenutzerId, string Erstellt, string Benachrichtigt, string Grund)
{
    public bool Aktiv => Status is "angefragt" or "bestaetigt";
    public bool HatZeit => Beginn.Length > 0 && Abschnitt >= 0;
}

public sealed record StAbwesenheit(int Id, int SprechtagId, string Lehrer, int Abschnitt, string Beginn, string Ende, string Anmerkung);

public sealed record StZeit(string Von, string Bis);

public sealed class Sprechtagdienst
{
    public const string Entwurf = "entwurf", Verwaltung = "verwaltung", Phase1 = "phase1", Phase1Beendet = "phase1_beendet", Phase2 = "phase2",
        Geschlossen = "geschlossen", Durchfuehrung = "durchfuehrung", Abgeschlossen = "abgeschlossen";

    /// <summary>Anzeigetexte der Phasen (wie in der Vorlage).</summary>
    public static readonly IReadOnlyDictionary<string, string> PhasenText = new Dictionary<string, string>
    {
        [Entwurf] = "Entwurf", [Verwaltung] = "Verwaltung LK", [Phase1] = "1. Anmeldephase", [Phase1Beendet] = "1. Anmeldephase beendet",
        [Phase2] = "2. Anmeldephase", [Geschlossen] = "Anmeldung geschlossen", [Durchfuehrung] = "In Durchführung", [Abgeschlossen] = "Abgeschlossen",
    };

    private static readonly string[] Reihenfolge = { Entwurf, Verwaltung, Phase1, Phase1Beendet, Phase2, Geschlossen, Durchfuehrung, Abgeschlossen };
    public static int Rang(string phase) => Array.IndexOf(Reihenfolge, phase);

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Uhrzeit = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Benutzerdienst _benutzer;
    private readonly Mitteilungsdienst _mitteilungen;
    private readonly Paketdienst? _paket;
    /// <summary>Prüfen und Schreiben von Terminen nacheinander (gegen doppelte Buchungen bei gleichzeitigen Anfragen).</summary>
    private readonly object _sperre = new();

    public Sprechtagdienst(Db db, Uhr uhr, Benutzerdienst benutzer, Mitteilungsdienst mitteilungen, Paketdienst? paket = null)
    {
        _db = db; _uhr = uhr; _benutzer = benutzer; _mitteilungen = mitteilungen; _paket = paket;
    }

    // ------------------------------------------------------------------------------------------ Zeit

    public static int Minuten(string hhmm) => int.Parse(hhmm[..2], Inv) * 60 + int.Parse(hhmm[3..5], Inv);
    public static string Text(int minuten) => $"{minuten / 60:00}:{minuten % 60:00}";
    private static DateTime? Zeitpunkt(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : DateTime.TryParse(s.Replace(' ', 'T'), Inv, DateTimeStyles.None, out var d) ? d : null;
    private static DateTime Beginn(StAbschnitt a) => DateOnly.Parse(a.Datum, Inv).ToDateTime(TimeOnly.MinValue).AddMinutes(Minuten(a.Von));
    private static DateTime Ende(StAbschnitt a) => DateOnly.Parse(a.Datum, Inv).ToDateTime(TimeOnly.MinValue).AddMinutes(Minuten(a.Bis));
    private static bool Vollstaendig(StAbschnitt a) => DateOnly.TryParse(a.Datum, Inv, out _) && Uhrzeit.IsMatch(a.Von) && Uhrzeit.IsMatch(a.Bis);
    private static bool Ueberschneidung(int von1, int bis1, int von2, int bis2) => von1 < bis2 && von2 < bis1;

    // ------------------------------------------------------------------------------------------ Laden und Speichern

    private Sprechtag Lesen(DbDataReader r) => new(r.Zahl("id"), r.Text("schuljahr"), r.Text("sj_von"), r.Text("sj_bis"),
        JsonSerializer.Deserialize<StKonfig>(r.Text("konfig"), Json) ?? new StKonfig(), r.Text("veroeffentlicht"), r.Text("erstellt"));

    public List<Sprechtag> Alle() => _db.Liste("SELECT * FROM st_sprechtage ORDER BY id", Lesen);
    public Sprechtag? Holen(int id) => _db.Einzel("SELECT * FROM st_sprechtage WHERE id = @id", Lesen, Db.P("@id", id));
    public Sprechtag HolenOderFehler(int id) => Holen(id) ?? throw new ArgumentException("Unbekannter Sprechtag.");

    private static StTermin TerminLesen(DbDataReader r) => new(r.Zahl("id"), r.Zahl("sprechtag_id"), r.Text("lehrer"), r.Zahl("abschnitt"), r.Text("beginn"), r.Text("ende"),
        r.Zahl("schueler_id"), r.Text("art"), r.Text("status"), r.Zahl("verpflichtend") == 1, r.Text("link"), r.Text("anmerkung"), r.Text("erstellt_von"),
        r["benutzer_id"] is DBNull or null ? null : Convert.ToInt32(r["benutzer_id"]), r.Text("erstellt"), r.Text("benachrichtigt"), r.Text("grund"));

    public List<StTermin> Termine(int sprechtagId) => _db.Liste("SELECT * FROM st_termine WHERE sprechtag_id = @s ORDER BY abschnitt, beginn, id", TerminLesen, Db.P("@s", sprechtagId));
    public StTermin? Termin(int id) => _db.Einzel("SELECT * FROM st_termine WHERE id = @id", TerminLesen, Db.P("@id", id));

    public List<StAbwesenheit> Abwesenheiten(int sprechtagId) => _db.Liste("SELECT * FROM st_abwesenheiten WHERE sprechtag_id = @s ORDER BY abschnitt, beginn", r =>
        new StAbwesenheit(r.Zahl("id"), r.Zahl("sprechtag_id"), r.Text("lehrer"), r.Zahl("abschnitt"), r.Text("beginn"), r.Text("ende"), r.Text("anmerkung")), Db.P("@s", sprechtagId));

    /// <summary>Räume je Lehrkraft und Abschnitt.</summary>
    public Dictionary<(string Lehrer, int Abschnitt), string> Raeume(int sprechtagId) =>
        _db.Liste("SELECT lehrer, abschnitt, raum FROM st_raeume WHERE sprechtag_id = @s", r => (r.Text("lehrer"), r.Zahl("abschnitt"), r.Text("raum")), Db.P("@s", sprechtagId))
            .Where(x => x.Item3.Length > 0).GroupBy(x => (x.Item1.ToUpperInvariant(), x.Item2)).ToDictionary(g => g.Key, g => g.First().Item3);

    public string Raum(Dictionary<(string, int), string> raeume, string lehrer, int abschnitt) => raeume.TryGetValue((lehrer.ToUpperInvariant(), abschnitt), out var r) ? r : "";

    // ------------------------------------------------------------------------------------------ Phase

    public string Phase(Sprechtag t) => Phase(t, _uhr.Jetzt);

    public static string Phase(Sprechtag t, DateTime jetzt)
    {
        if (!t.IstVeroeffentlicht) return Entwurf;
        var k = t.K;
        if (Zeitpunkt(k.Phase1Von) is { } p1v && jetzt < p1v) return Verwaltung;
        if (Zeitpunkt(k.Phase1Bis) is { } p1b && jetzt < p1b) return Phase1;
        if (Zeitpunkt(k.Phase2Von) is { } p2v && jetzt < p2v) return Phase1Beendet;
        if (Zeitpunkt(k.Phase2Bis) is { } p2b && jetzt < p2b) return Phase2;
        var abschnitte = k.Abschnitte.Where(Vollstaendig).ToList();
        if (abschnitte.Count == 0) return Abgeschlossen;
        if (jetzt < abschnitte.Min(Beginn)) return Geschlossen;
        if (jetzt <= abschnitte.Max(Ende)) return Durchfuehrung;
        return Abgeschlossen;
    }

    /// <summary>Datum des Sprechtags („22.01.2027“ bzw. „04.11.2026 – 05.11.2026“), leer ohne Abschnitte.</summary>
    public static string DatumText(Sprechtag t)
    {
        var tage = t.K.Abschnitte.Where(a => DateOnly.TryParse(a.Datum, Inv, out _)).Select(a => DateOnly.Parse(a.Datum, Inv)).Distinct().Order().ToList();
        if (tage.Count == 0) return "";
        return tage.Count == 1 ? $"{tage[0]:dd.MM.yyyy}" : $"{tage[0]:dd.MM.yyyy} – {tage[^1]:dd.MM.yyyy}";
    }

    /// <summary>Zeitraum der Anmeldung (Beginn 1. bis Ende 2. Anmeldephase).</summary>
    public static string AnmeldungText(Sprechtag t)
    {
        var von = Zeitpunkt(t.K.Phase1Von) ?? Zeitpunkt(t.K.Phase2Von);
        var bis = Zeitpunkt(t.K.Phase2Bis) ?? Zeitpunkt(t.K.Phase1Bis);
        return von is null || bis is null ? "" : $"{von:dd.MM.yyyy} – {bis:dd.MM.yyyy}";
    }

    /// <summary>
    /// Konfiguration noch änderbar? Im Entwurf immer; veröffentlicht nur, solange noch keine Termine angelegt wurden und die 1. Anmeldephase
    /// noch nicht begonnen hat (der Sprechtag wird dann wieder zum Entwurf).
    /// </summary>
    public bool Bearbeitbar(Sprechtag t) => !t.IstVeroeffentlicht || (Phase(t) == Verwaltung && Termine(t.Id).Count == 0);

    /// <summary>Zeitblöcke eines Abschnitts (einheitliche Taktung).</summary>
    public static List<StZeit> Bloecke(Sprechtag t, StAbschnitt a)
    {
        var res = new List<StZeit>();
        if (!Vollstaendig(a) || t.K.Dauer <= 0) return res;
        for (var m = Minuten(a.Von); m + t.K.Dauer <= Minuten(a.Bis); m += t.K.Dauer) res.Add(new StZeit(Text(m), Text(m + t.K.Dauer)));
        return res;
    }

    // ------------------------------------------------------------------------------------------ Anlegen, Konfiguration, Veröffentlichen

    /// <summary>Neuer Sprechtag (Entwurf) für ein Schuljahr – das Schuljahr kann danach nicht mehr geändert werden.</summary>
    public Sprechtag Anlegen(string schuljahr, string sjVon, string sjBis, StKonfig? k = null)
    {
        k ??= new StKonfig { Name = "Neuer Sprechtag", Abschnitte = new() { new StAbschnitt("1. Sprechtagsabschnitt", "", "13:00", "18:00") } };
        var id = (int)_db.Einfuegen("INSERT INTO st_sprechtage (schuljahr, sj_von, sj_bis, konfig, veroeffentlicht, erstellt) VALUES (@j, @v, @b, @k, '', @z)",
            Db.P("@j", schuljahr), Db.P("@v", sjVon), Db.P("@b", sjBis), Db.P("@k", JsonSerializer.Serialize(k, Json)), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)));
        return Holen(id)!;
    }

    /// <summary>Kopie eines Sprechtags als Entwurf (gleiches Schuljahr, gleiche Konfiguration, ohne Räume und Termine).</summary>
    public Sprechtag Duplizieren(Sprechtag t)
    {
        var name = t.K.Name.Length > 73 ? t.K.Name[..73] : t.K.Name;
        return Anlegen(t.Schuljahr, t.SchuljahrVon, t.SchuljahrBis, t.K with { Name = $"{name} (Kopie)" });
    }

    /// <summary>
    /// Konfiguration speichern (laufend, auch unvollständig). Ein veröffentlichter Sprechtag wird dabei wieder zum Entwurf – nur solange
    /// noch keine Termine angelegt wurden und die 1. Anmeldephase nicht begonnen hat.
    /// </summary>
    public Sprechtag KonfigSpeichern(Sprechtag t, StKonfig k)
    {
        if (!Bearbeitbar(t))
            throw new ArgumentException("Der Sprechtag kann nicht mehr bearbeitet werden: Es wurden bereits Termine angelegt bzw. die Anmeldung hat begonnen. Sie können ihn löschen, neu anlegen oder duplizieren.");
        var name = (k.Name ?? "").Trim();
        if (name.Length > 80) throw new ArgumentException("Der Sprechtagsname darf höchstens 80 Zeichen lang sein.");
        if (k.Dauer is < 3 or > 120) throw new ArgumentException("Die Dauer der Zeitblöcke muss zwischen 3 und 120 Minuten liegen.");
        foreach (var a in k.Abschnitte)
        {
            if (a.Von.Length > 0 && !Uhrzeit.IsMatch(a.Von) || a.Bis.Length > 0 && !Uhrzeit.IsMatch(a.Bis)) throw new ArgumentException("Bitte Uhrzeiten im Format HH:MM angeben.");
            if ((a.Name ?? "").Length > 80) throw new ArgumentException("Der Abschnittsname darf höchstens 80 Zeichen lang sein.");
        }
        var neu = k with
        {
            Name = name, Abschnitte = k.Abschnitte.Select(a => a with { Name = (a.Name ?? "").Trim() }).ToList(),
            Rechte = k.Rechte ?? new StRechte(), Regeln = Regeln(k.Regeln),
        };
        _db.Ausfuehren("UPDATE st_sprechtage SET konfig = @k, veroeffentlicht = '' WHERE id = @id", Db.P("@k", JsonSerializer.Serialize(neu, Json)), Db.P("@id", t.Id));
        return Holen(t.Id)!;
    }

    private static StRegeln Regeln(StRegeln? r)
    {
        int? Wert(int? x, string was) => x is null or 0 ? null : x is < 1 or > 50 ? throw new ArgumentException($"{was}: bitte eine Zahl zwischen 1 und 50 angeben.") : x;
        return new StRegeln(Wert(r?.BloeckeBisPause, "Zeitblöcke am Stück bis zur Pause"), Wert(r?.MaxBloeckeProSchueler, "Maximale Zeitblöcke pro Schüler*in"),
            Wert(r?.GesperrteBloecke, "Gesperrte Zeitblöcke zwischen zwei Terminen"));
    }

    /// <summary>Fehler der Konfiguration, die das Veröffentlichen verhindern (leer = vollständig).</summary>
    public static List<string> Pruefen(Sprechtag t)
    {
        var k = t.K;
        var f = new List<string>();
        if (k.Name.Trim().Length == 0) f.Add("Bitte einen Sprechtagsnamen eingeben.");
        if (k.Abschnitte.Count == 0) f.Add("Bitte mindestens einen Sprechtagsabschnitt anlegen.");
        DateOnly.TryParse(t.SchuljahrVon, Inv, out var sjVon);
        var sjBis = DateOnly.TryParse(t.SchuljahrBis, Inv, out var b) ? b : DateOnly.MaxValue;
        for (var i = 0; i < k.Abschnitte.Count; i++)
        {
            var a = k.Abschnitte[i];
            if (!Vollstaendig(a)) { f.Add($"{i + 1}. Abschnitt: Bitte Datum, Start- und Endzeit angeben."); continue; }
            if (Minuten(a.Bis) - Minuten(a.Von) < k.Dauer) f.Add($"{i + 1}. Abschnitt: Die Endzeit muss mindestens einen Zeitblock nach der Startzeit liegen.");
            var d = DateOnly.Parse(a.Datum, Inv);
            if (d < sjVon || d > sjBis) f.Add($"{i + 1}. Abschnitt: Das Datum liegt nicht im Schuljahr {t.Schuljahr}.");
            for (var j = 0; j < i; j++)
                if (Vollstaendig(k.Abschnitte[j]) && Beginn(a) < Ende(k.Abschnitte[j]) && Beginn(k.Abschnitte[j]) < Ende(a))
                    f.Add($"{i + 1}. und {j + 1}. Abschnitt überschneiden sich.");
        }
        var p1v = Zeitpunkt(k.Phase1Von); var p1b = Zeitpunkt(k.Phase1Bis); var p2v = Zeitpunkt(k.Phase2Von); var p2b = Zeitpunkt(k.Phase2Bis);
        if (p1v is null || p1b is null) f.Add("Bitte Beginn und Ende der 1. Anmeldephase angeben.");
        if (p2v is null || p2b is null) f.Add("Bitte Beginn und Ende der 2. Anmeldephase angeben.");
        if (p1v >= p1b) f.Add("Die 1. Anmeldephase muss nach ihrem Beginn enden.");
        if (p2v >= p2b) f.Add("Die 2. Anmeldephase muss nach ihrem Beginn enden.");
        if (p1b > p2v) f.Add("Die Anmeldephasen dürfen sich nicht überschneiden (die 2. beginnt nach dem Ende der 1.).");
        var erster = k.Abschnitte.Where(Vollstaendig).Select(Beginn).DefaultIfEmpty(DateTime.MaxValue).Min();
        if (p2b > erster || p1b > erster) f.Add("Beide Anmeldephasen müssen vor dem ersten Sprechtagsabschnitt enden.");
        return f.Distinct().ToList();
    }

    /// <summary>Veröffentlichen: Phase „Verwaltung LK“ beginnt; alle Lehrkräfte werden benachrichtigt.</summary>
    public Sprechtag Veroeffentlichen(Sprechtag t, Benutzer ich)
    {
        if (t.IstVeroeffentlicht) throw new ArgumentException("Der Sprechtag ist bereits veröffentlicht.");
        var fehler = Pruefen(t);
        if (fehler.Count > 0) throw new ArgumentException(string.Join(" ", fehler));
        if (Zeitpunkt(t.K.Phase1Von) <= _uhr.Jetzt) throw new ArgumentException("Die 1. Anmeldephase muss nach dem Veröffentlichen beginnen (Phase „Verwaltung LK“ für die Lehrkräfte).");
        _db.Ausfuehren("UPDATE st_sprechtage SET veroeffentlicht = @z WHERE id = @id", Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@id", t.Id));
        var lehrkraefte = _benutzer.Alle().Where(b => !b.Gesperrt && b.Kollegium && !string.IsNullOrEmpty(b.LehrerKuerzel)).Select(b => b.Id);
        Melden(ich, lehrkraefte, $"Sprechtag „{t.K.Name}“ veröffentlicht",
            $"Der Sprechtag „{t.K.Name}“ ({DatumText(t)}) wurde veröffentlicht. Bitte wählen Sie bis zum Beginn der 1. Anmeldephase am {Zeitpunkt(t.K.Phase1Von):dd.MM.yyyy HH:mm} Uhr " +
            "Ihren Raum, tragen Sie Abwesenheiten ein und laden Sie Erziehungsberechtigte ein.");
        return Holen(t.Id)!;
    }

    /// <summary>Löschen (unwiderruflich): Betroffene mit aktiven Terminen bzw. Einladungen werden über die Absage benachrichtigt.</summary>
    public void Loeschen(Stand? s, Sprechtag t, Benutzer ich)
    {
        if (t.IstVeroeffentlicht && Rang(Phase(t)) >= Rang(Phase1))
            foreach (var x in Termine(t.Id).Where(x => x.Aktiv || x.Status == "einladung"))
                Melden(ich, ElternVon(x.SchuelerId).Select(b => b.Id), "Sprechtag abgesagt",
                    $"Der Sprechtag „{t.K.Name}“ wurde abgesagt. Ihr Termin bzw. Ihre Einladung bei {LehrerName(s, x.Lehrer)} für {Name(s, x.SchuelerId)} entfällt.");
        _db.Transaktion(c =>
        {
            foreach (var tab in new[] { "st_hinweise", "st_abwesenheiten", "st_termine", "st_raeume" })
                Db.Ausfuehren(c, $"DELETE FROM {tab} WHERE sprechtag_id = @id", Db.P("@id", t.Id));
            Db.Ausfuehren(c, "DELETE FROM st_sprechtage WHERE id = @id", Db.P("@id", t.Id));
        });
    }

    // ------------------------------------------------------------------------------------------ Rechte

    public static bool IstVerwaltung(Benutzer b) => Rechtedienst.Darf(b, Recht.SprechtagVerwalten);

    /// <summary>Darf die Person für die Lehrkraft handeln (Administration immer; Lehrkraft nur für sich und nur mit dem Recht des Sprechtags)?</summary>
    public static bool Darf(Sprechtag t, Benutzer b, string lehrer, Func<StRechte, bool>? recht = null) =>
        IstVerwaltung(b) || (b.Kollegium && Ci.Equals(b.LehrerKuerzel, lehrer) && (recht is null || recht(t.K.Rechte)));

    private static void Erlaubt(Sprechtag t, Benutzer b, string lehrer, Func<StRechte, bool>? recht, string was)
    {
        if (!Darf(t, b, lehrer, recht)) throw new UnauthorizedAccessException($"{was} ist für diesen Sprechtag nicht freigegeben.");
    }

    private void NichtAbgeschlossen(Sprechtag t)
    {
        var p = Phase(t);
        if (p == Entwurf) throw new ArgumentException("Der Sprechtag ist noch nicht veröffentlicht.");
        if (p == Abgeschlossen) throw new ArgumentException("Der Sprechtag ist abgeschlossen.");
    }

    // ------------------------------------------------------------------------------------------ Räume und Abwesenheiten

    public void RaumSetzen(Sprechtag t, Benutzer ich, string lehrer, int abschnitt, string? raum)
    {
        Erlaubt(t, ich, lehrer, r => r.RaeumeWaehlen, "Die Raumwahl");
        if (t.K.Online) throw new ArgumentException("Bei einem Online-Sprechtag werden keine Räume zugewiesen.");
        if (abschnitt < 0 || abschnitt >= t.K.Abschnitte.Count) throw new ArgumentException("Unbekannter Abschnitt.");
        if (Phase(t) == Abgeschlossen) throw new ArgumentException("Der Sprechtag ist abgeschlossen.");
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "DELETE FROM st_raeume WHERE sprechtag_id = @s AND lehrer = @l AND abschnitt = @a", Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@a", abschnitt));
            if (!string.IsNullOrWhiteSpace(raum))
                Db.Ausfuehren(c, "INSERT INTO st_raeume (sprechtag_id, lehrer, abschnitt, raum) VALUES (@s, @l, @a, @r)", Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@a", abschnitt), Db.P("@r", raum.Trim()));
        });
    }

    /// <summary>
    /// Abwesenheit eintragen bzw. ändern (Administration immer, Lehrkraft mit Recht; in jeder Phase außer „Abgeschlossen“). Überschneidet sie
    /// sich mit einer bestehenden Abwesenheit, wird sie nicht gespeichert; Termine der Lehrkraft in diesem Zeitraum werden abgesagt.
    /// </summary>
    public StAbwesenheit AbwesenheitSpeichern(Stand? s, Sprechtag t, Benutzer ich, int? id, string lehrer, int abschnitt, string beginn, string ende, string? anmerkung)
    {
        Erlaubt(t, ich, lehrer, r => r.AbwesenheitenEintragen, "Das Eintragen von Abwesenheiten");
        NichtAbgeschlossen(t);
        var a = Abschnitt(t, abschnitt);
        if (!Uhrzeit.IsMatch(beginn) || !Uhrzeit.IsMatch(ende) || Minuten(beginn) >= Minuten(ende)) throw new ArgumentException("Bitte Start- und Endzeit angeben (Start vor Ende).");
        if (Minuten(beginn) < Minuten(a.Von) || Minuten(ende) > Minuten(a.Bis)) throw new ArgumentException($"Die Abwesenheit muss im Abschnitt ({a.Von}–{a.Bis} Uhr) liegen.");
        var text = (anmerkung ?? "").Trim();
        if (text.Length > 255) throw new ArgumentException("Die Anmerkung darf höchstens 255 Zeichen lang sein.");
        lock (_sperre)
        {
            if (Abwesenheiten(t.Id).Any(x => x.Id != id && Ci.Equals(x.Lehrer, lehrer) && x.Abschnitt == abschnitt && Ueberschneidung(Minuten(x.Beginn), Minuten(x.Ende), Minuten(beginn), Minuten(ende))))
                throw new ArgumentException("Die Abwesenheit überschneidet sich mit einer bestehenden Abwesenheit der Lehrkraft.");
            int neueId;
            if (id is int vorhanden)
            {
                var alt = Abwesenheiten(t.Id).FirstOrDefault(x => x.Id == vorhanden && Ci.Equals(x.Lehrer, lehrer)) ?? throw new ArgumentException("Unbekannte Abwesenheit.");
                _db.Ausfuehren("UPDATE st_abwesenheiten SET abschnitt = @a, beginn = @b, ende = @e, anmerkung = @m WHERE id = @id",
                    Db.P("@a", abschnitt), Db.P("@b", beginn), Db.P("@e", ende), Db.P("@m", text), Db.P("@id", alt.Id));
                neueId = alt.Id;
            }
            else
                neueId = (int)_db.Einfuegen("INSERT INTO st_abwesenheiten (sprechtag_id, lehrer, abschnitt, beginn, ende, anmerkung) VALUES (@s, @l, @a, @b, @e, @m)",
                    Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@a", abschnitt), Db.P("@b", beginn), Db.P("@e", ende), Db.P("@m", text));
            // Termine der Lehrkraft in diesem Zeitraum werden abgesagt und die Teilnehmenden benachrichtigt
            foreach (var x in Termine(t.Id).Where(x => x.Aktiv && x.HatZeit && Ci.Equals(x.Lehrer, lehrer) && x.Abschnitt == abschnitt
                                                        && Ueberschneidung(Minuten(x.Beginn), Minuten(x.Ende), Minuten(beginn), Minuten(ende))))
                AbsageSchreiben(s, t, ich, x, "Die Lehrkraft ist zu dieser Zeit abwesend.");
            return Abwesenheiten(t.Id).First(x => x.Id == neueId);
        }
    }

    public void AbwesenheitLoeschen(Sprechtag t, Benutzer ich, int id)
    {
        var a = Abwesenheiten(t.Id).FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Abwesenheit.");
        Erlaubt(t, ich, a.Lehrer, r => r.AbwesenheitenEintragen, "Das Bearbeiten von Abwesenheiten");
        NichtAbgeschlossen(t);
        _db.Ausfuehren("DELETE FROM st_abwesenheiten WHERE id = @id", Db.P("@id", id));
    }

    private static StAbschnitt Abschnitt(Sprechtag t, int i) =>
        i >= 0 && i < t.K.Abschnitte.Count && Vollstaendig(t.K.Abschnitte[i]) ? t.K.Abschnitte[i] : throw new ArgumentException("Unbekannter Sprechtagsabschnitt.");

    // ------------------------------------------------------------------------------------------ Schüler*innen und Erziehungsberechtigte

    /// <summary>Schüler*innen, die die Lehrkraft unterrichtet (Klassen ihres Unterrichts und Klassenleitung) – nur mit ihnen gibt es Termine.</summary>
    public static List<WebSchueler> SchuelerDerLehrkraft(Stand? s, string lehrer)
    {
        if (s == null) return new();
        var klassen = KlassenVon(s, lehrer).ToHashSet(Ci);
        var de = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true);
        return s.Schueler.Where(x => klassen.Contains(x.Klasse)).OrderBy(x => x.Klasse, de).ThenBy(x => x.Name, de).ThenBy(x => x.Vorname, de).ToList();
    }

    public static List<string> KlassenVon(Stand? s, string lehrer)
    {
        if (s == null) return new();
        var res = s.Unterricht.Values.SelectMany(u => u.Zeilen).Where(z => Ci.Equals(z.Lehrer, lehrer)).SelectMany(z => z.Klassen).ToHashSet(Ci);
        var leitung = s.Klassen.Values.Where(k => k.Klassenleitung.Contains(lehrer, Ci)).Select(k => k.Kurz).ToHashSet(Ci);
        res.UnionWith(leitung);
        return res.OrderByDescending(leitung.Contains).ThenBy(x => x, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true)).ToList();
    }

    /// <summary>Lehrkräfte eines Kindes (Unterricht der Klasse und Klassenleitung) mit Fächern.</summary>
    public sealed record KindLehrkraft(string Kurz, string Name, List<string> Faecher, bool Klassenleitung);

    public static List<KindLehrkraft> LehrkraefteDesKindes(Stand? s, WebSchueler kind)
    {
        if (s == null) return new();
        var faecher = new Dictionary<string, SortedSet<string>>(Ci);
        void Dazu(string lehrer, string? fach)
        {
            if (string.IsNullOrWhiteSpace(lehrer) || lehrer is "---" or "???") return;
            if (!faecher.TryGetValue(lehrer, out var f)) faecher[lehrer] = f = new SortedSet<string>(Ci);
            if (!string.IsNullOrWhiteSpace(fach)) f.Add(fach);
        }
        foreach (var u in s.Unterricht.Values)
            foreach (var z in u.Zeilen)
                if (z.Klassen.Contains(kind.Klasse, Ci)) Dazu(z.Lehrer, z.Fach);
        var leitung = s.Klassen.TryGetValue(kind.Klasse, out var kl) ? kl.Klassenleitung : new List<string>();
        foreach (var l in leitung) Dazu(l, null);
        var de = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true);
        return faecher.Select(x => new KindLehrkraft(x.Key, LehrerName(s, x.Key), x.Value.ToList(), leitung.Contains(x.Key, Ci)))
            .OrderByDescending(x => x.Klassenleitung).ThenBy(x => x.Name, de).ToList();
    }

    private bool Unterrichtet(Stand? s, string lehrer, int schuelerId) => s == null || SchuelerDerLehrkraft(s, lehrer).Any(x => x.Id == schuelerId);

    public List<Benutzer> ElternVon(int schuelerId) => _benutzer.Alle().Where(b => !b.Gesperrt && b.Rolle == Rollen.Eltern && b.SchuelerIds.Contains(schuelerId)).ToList();
    public List<Benutzer> KontenDerLehrkraft(string lehrer) => _benutzer.Alle().Where(b => !b.Gesperrt && b.Kollegium && Ci.Equals(b.LehrerKuerzel, lehrer)).ToList();

    /// <summary>Kinder aller Elternkonten der Familie (Geschwister über gemeinsame Konten).</summary>
    public HashSet<int> Familie(int schuelerId)
    {
        var konten = ElternVon(schuelerId);
        var kinder = konten.SelectMany(b => b.SchuelerIds).ToHashSet();
        kinder.Add(schuelerId);
        return kinder;
    }

    public static string Name(Stand? s, int schuelerId) => s?.Schueler.FirstOrDefault(x => x.Id == schuelerId) is { } x ? $"{x.Name} {x.Vorname}".Trim() : "Unbekannt";
    public static string LehrerName(Stand? s, string kurz) =>
        s != null && s.Lehrer.TryGetValue(kurz, out var l) && l.Name.Length > 0 ? $"{l.Name}{(l.Vorname.Length > 0 ? " " + l.Vorname : "")}" : kurz;
    /// <summary>„(Kürzel) Name Vorname“ wie in der Vorlage.</summary>
    public static string LehrerTitel(Stand? s, string kurz) => $"({kurz}) {LehrerName(s, kurz)}";

    // ------------------------------------------------------------------------------------------ Freie Zeiten und Buchungsregeln

    /// <summary>Belegte Zeiten der Lehrkraft im Abschnitt: aktive Termine, Abwesenheiten (Sprechtag und Lehrerabwesenheiten des Plans).</summary>
    private List<(int Von, int Bis)> Belegt(Stand? s, Sprechtag t, string lehrer, int abschnitt, int? ausser)
    {
        var a = Abschnitt(t, abschnitt);
        var res = Termine(t.Id).Where(x => x.Id != ausser && x.Aktiv && x.HatZeit && x.Abschnitt == abschnitt && Ci.Equals(x.Lehrer, lehrer))
            .Select(x => (Minuten(x.Beginn), Minuten(x.Ende))).ToList();
        res.AddRange(Abwesenheiten(t.Id).Where(x => x.Abschnitt == abschnitt && Ci.Equals(x.Lehrer, lehrer)).Select(x => (Minuten(x.Beginn), Minuten(x.Ende))));
        // Abwesenheiten aus dem Plan (z. B. Krankmeldung) werden übernommen: ganztägig sperrt den Abschnitt
        if (s != null && LehrerAbwesend(s, lehrer, DateOnly.Parse(a.Datum, Inv))) res.Add((Minuten(a.Von), Minuten(a.Bis)));
        return res;
    }

    private static bool LehrerAbwesend(Stand s, string lehrer, DateOnly tag) =>
        s.Paket.Absenzen.Any(x => Ci.Equals(x.Art, "lehrer") && Ci.Equals(x.Element, lehrer)
            && DateOnly.TryParse(x.Von, Inv, out var v) && tag >= v && tag <= (DateOnly.TryParse(x.Bis, Inv, out var b) ? b : v));

    /// <summary>
    /// Freie Zeitblöcke der Lehrkraft im Abschnitt. Für Erziehungsberechtigte (<paramref name="schuelerId"/> gesetzt) gelten zusätzlich die
    /// Buchungsregeln: Pause nach N Blöcken am Stück, höchstens M Blöcke je Kind und Lehrkraft, G gesperrte Blöcke zwischen zwei Terminen der
    /// Familie (Raumwechsel) und keine Überschneidung mit Terminen der Familie.
    /// </summary>
    public List<StZeit> Frei(Stand? s, Sprechtag t, string lehrer, int abschnitt, int? schuelerId, int? ausser = null)
    {
        var a = Abschnitt(t, abschnitt);
        var belegt = Belegt(s, t, lehrer, abschnitt, ausser);
        var frei = Bloecke(t, a).Where(z => !belegt.Any(b => Ueberschneidung(b.Von, b.Bis, Minuten(z.Von), Minuten(z.Bis)))).ToList();
        if (schuelerId is not int kind) return frei;
        var r = t.K.Regeln;
        var termine = Termine(t.Id).Where(x => x.Id != ausser && x.Aktiv && x.HatZeit).ToList();
        if (r.MaxBloeckeProSchueler is int max)
        {
            var bisher = termine.Where(x => x.SchuelerId == kind && Ci.Equals(x.Lehrer, lehrer)).Sum(x => (Minuten(x.Ende) - Minuten(x.Beginn)) / Math.Max(1, t.K.Dauer));
            if (bisher >= max) return new();
        }
        if (r.BloeckeBisPause is int n)
        {
            var eigene = termine.Where(x => x.Abschnitt == abschnitt && Ci.Equals(x.Lehrer, lehrer)).Select(x => (Minuten(x.Beginn), Minuten(x.Ende))).ToList();
            bool Gebucht(int m) => eigene.Any(e => e.Item1 <= m && m < e.Item2);
            frei = frei.Where(z =>
            {
                var m = Minuten(z.Von);
                var vor = 0; for (var x = m - t.K.Dauer; x >= Minuten(a.Von) && Gebucht(x); x -= t.K.Dauer) vor++;
                var nach = 0; for (var x = m + t.K.Dauer; x < Minuten(a.Bis) && Gebucht(x); x += t.K.Dauer) nach++;
                return vor + 1 + nach <= n;
            }).ToList();
        }
        var familie = Familie(kind);
        var abstand = (r.GesperrteBloecke ?? 0) * t.K.Dauer;
        var datum = a.Datum;
        var fam = termine.Where(x => familie.Contains(x.SchuelerId) && x.Abschnitt >= 0 && x.Abschnitt < t.K.Abschnitte.Count && t.K.Abschnitte[x.Abschnitt].Datum == datum)
            .Select(x => (Minuten(x.Beginn), Minuten(x.Ende), x.Lehrer)).ToList();
        return frei.Where(z => !fam.Any(f =>
        {
            // eine Lehrkraft, ein Raum: kein Abstand nötig, aber keine Überschneidung
            var luecke = Ci.Equals(f.Lehrer, lehrer) ? 0 : abstand;
            return Ueberschneidung(f.Item1 - luecke, f.Item2 + luecke, Minuten(z.Von), Minuten(z.Bis));
        })).ToList();
    }

    // ------------------------------------------------------------------------------------------ Einladungen und Termine (Lehrkräfte, Administration)

    /// <summary>
    /// Einladungen senden (ohne Uhrzeit): in „Verwaltung LK“, in der 1. und der 2. Anmeldephase. Erziehungsberechtigte werden ab Beginn der
    /// 1. Anmeldephase benachrichtigt (vorher sehen sie den Sprechtag nicht).
    /// </summary>
    public int Einladen(Stand? s, Sprechtag t, Benutzer ich, string lehrer, IEnumerable<int> schuelerIds, string? text, bool verpflichtend)
    {
        Erlaubt(t, ich, lehrer, null, "Das Einladen");
        var p = Phase(t);
        if (p is not (Verwaltung or Phase1 or Phase2))
            throw new ArgumentException(p == Phase1Beendet ? "Bis zum Start der 2. Anmeldephase können keine Einladungen verschickt werden." : "In dieser Phase können keine Einladungen verschickt werden.");
        var vorhanden = Termine(t.Id).Where(x => Ci.Equals(x.Lehrer, lehrer) && (x.Status == "einladung" || x.Aktiv)).Select(x => x.SchuelerId).ToHashSet();
        var n = 0;
        foreach (var id in schuelerIds.Distinct())
        {
            if (vorhanden.Contains(id)) continue;
            if (!Unterrichtet(s, lehrer, id)) throw new ArgumentException($"{Name(s, id)} wird von dieser Lehrkraft nicht unterrichtet.");
            var tid = (int)_db.Einfuegen("""
                INSERT INTO st_termine (sprechtag_id, lehrer, abschnitt, schueler_id, art, status, verpflichtend, anmerkung, erstellt_von, erstellt)
                VALUES (@s, @l, -1, @k, 'einladung', 'einladung', @v, @m, @u, @z)
                """, Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@k", id), Db.P("@v", verpflichtend ? 1 : 0), Db.P("@m", (text ?? "").Trim()),
                Db.P("@u", ich.Login), Db.P("@z", Jetzt()));
            n++;
            if (Rang(p) >= Rang(Phase1)) AnfrageMelden(s, t, ich, Termin(tid)!);
        }
        return n;
    }

    /// <summary>
    /// Terminanfrage mit fester Uhrzeit (Recht „Termine für Erziehungsberechtigte buchen“; Administration immer). Die Erziehungsberechtigten
    /// nehmen sie an bzw. lehnen sie ab; in „Verwaltung LK“ heißt sie „Terminiert“ und wird erst mit der 1. Anmeldephase versendet.
    /// </summary>
    public StTermin TerminAnlegen(Stand? s, Sprechtag t, Benutzer ich, string lehrer, int abschnitt, string beginn, string ende, int schuelerId,
        string? anmerkung, string? link, bool verpflichtend)
    {
        Erlaubt(t, ich, lehrer, r => r.TermineBuchen, "Das Buchen von Terminen für Erziehungsberechtigte");
        var p = Phase(t);
        if (p is Entwurf or Abgeschlossen or Durchfuehrung) throw new ArgumentException("In dieser Phase können keine Termine angelegt werden.");
        if (!Unterrichtet(s, lehrer, schuelerId)) throw new ArgumentException($"{Name(s, schuelerId)} wird von dieser Lehrkraft nicht unterrichtet.");
        var linkText = LinkPruefen(link);
        lock (_sperre)
        {
            ZeitPruefen(s, t, lehrer, abschnitt, beginn, ende, null);
            var id = (int)_db.Einfuegen("""
                INSERT INTO st_termine (sprechtag_id, lehrer, abschnitt, beginn, ende, schueler_id, art, status, verpflichtend, link, anmerkung, erstellt_von, erstellt)
                VALUES (@s, @l, @a, @b, @e, @k, 'termin', 'angefragt', @v, @li, @m, @u, @z)
                """, Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@a", abschnitt), Db.P("@b", beginn), Db.P("@e", ende), Db.P("@k", schuelerId),
                Db.P("@v", verpflichtend ? 1 : 0), Db.P("@li", linkText), Db.P("@m", (anmerkung ?? "").Trim()), Db.P("@u", ich.Login), Db.P("@z", Jetzt()));
            var x = Termin(id)!;
            if (Rang(p) >= Rang(Phase1)) AnfrageMelden(s, t, ich, x);
            return x;
        }
    }

    /// <summary>Prüft Beginn/Ende eines Termins der Lehrkraft: im Abschnitt, auf Zeitblöcke ausgerichtet, frei.</summary>
    private void ZeitPruefen(Stand? s, Sprechtag t, string lehrer, int abschnitt, string beginn, string ende, int? ausser)
    {
        var a = Abschnitt(t, abschnitt);
        if (!Uhrzeit.IsMatch(beginn) || !Uhrzeit.IsMatch(ende) || Minuten(beginn) >= Minuten(ende)) throw new ArgumentException("Bitte Start- und Endzeit angeben (Start vor Ende).");
        var b = Minuten(beginn); var e = Minuten(ende);
        if (b < Minuten(a.Von) || e > Minuten(a.Bis)) throw new ArgumentException($"Der Termin muss im Abschnitt ({a.Von}–{a.Bis} Uhr) liegen.");
        if ((b - Minuten(a.Von)) % t.K.Dauer != 0 || (e - b) % t.K.Dauer != 0)
            throw new ArgumentException($"Termine beginnen und enden im Takt der Zeitblöcke ({t.K.Dauer} Minuten).");
        if (Belegt(s, t, lehrer, abschnitt, ausser).Any(x => Ueberschneidung(x.Von, x.Bis, b, e)))
            throw new ArgumentException("Die Lehrkraft hat zu dieser Zeit bereits einen Termin bzw. ist abwesend.");
    }

    public static string LinkPruefen(string? link)
    {
        var l = (link ?? "").Trim();
        if (l.Length == 0) return "";
        if (l.Length > 500 || !Uri.TryCreate(l, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http"))
            throw new ArgumentException("Bitte einen gültigen Meeting-Link (https://…) angeben.");
        return l;
    }

    /// <summary>
    /// Termin bearbeiten (Uhrzeit, Anmerkung, Meeting-Link): Administration immer, Lehrkraft mit Recht „Bestehende Termine bearbeiten &amp; absagen“,
    /// Erziehungsberechtigte nur die Uhrzeit eigener Termine in der 1. und 2. Anmeldephase. Die Betroffenen werden benachrichtigt.
    /// </summary>
    public StTermin TerminAendern(Stand? s, Sprechtag t, Benutzer ich, StTermin x, int? abschnitt, string? beginn, string? ende, string? anmerkung, string? link)
    {
        var eltern = ich.Rolle == Rollen.Eltern;
        if (eltern)
        {
            if (!ich.SchuelerIds.Contains(x.SchuelerId)) throw new UnauthorizedAccessException("Diesen Termin dürfen Sie nicht ändern.");
            if (Phase(t) is not (Phase1 or Phase2)) throw new ArgumentException("Termine können nur in der 1. und 2. Anmeldephase geändert werden.");
            if (x.Status != "bestaetigt") throw new ArgumentException("Nur bestätigte Termine können verschoben werden.");
        }
        else Erlaubt(t, ich, x.Lehrer, r => r.TermineBearbeiten, "Das Bearbeiten von Terminen");
        NichtAbgeschlossen(t);
        if (!x.Aktiv) throw new ArgumentException("Der Termin ist nicht mehr aktiv.");
        lock (_sperre)
        {
            var a = abschnitt ?? x.Abschnitt;
            var b = beginn ?? x.Beginn; var e = ende ?? x.Ende;
            if (eltern) e = Text(Minuten(b) + (Minuten(x.Ende) - Minuten(x.Beginn)));
            var zeitNeu = a != x.Abschnitt || b != x.Beginn || e != x.Ende;
            if (zeitNeu)
            {
                ZeitPruefen(s, t, x.Lehrer, a, b, e, x.Id);
                if (eltern && !Frei(s, t, x.Lehrer, a, x.SchuelerId, x.Id).Any(z => z.Von == b))
                    throw new ArgumentException("Diese Zeit ist nicht mehr frei bzw. nach den Buchungsregeln nicht möglich.");
            }
            var linkNeu = eltern ? x.Link : link is null ? x.Link : LinkPruefen(link);
            var text = eltern ? x.Anmerkung : (anmerkung ?? x.Anmerkung).Trim();
            _db.Ausfuehren("UPDATE st_termine SET abschnitt = @a, beginn = @b, ende = @e, link = @li, anmerkung = @m WHERE id = @id",
                Db.P("@a", a), Db.P("@b", b), Db.P("@e", e), Db.P("@li", linkNeu), Db.P("@m", text), Db.P("@id", x.Id));
            var neu = Termin(x.Id)!;
            if ((zeitNeu || linkNeu != x.Link) && Sichtbar(t, neu))
            {
                var was = zeitNeu ? $"neue Zeit: {ZeitText(t, neu)}" : $"neuer Meeting-Link: {linkNeu}";
                var an = eltern ? KontenDerLehrkraft(x.Lehrer).Select(k => k.Id) : ElternVon(x.SchuelerId).Select(k => k.Id);
                Melden(ich, an, "Sprechtag: Termin geändert", $"Ihr Termin am Sprechtag „{t.K.Name}“ bei {LehrerName(s, x.Lehrer)} für {Name(s, x.SchuelerId)} wurde geändert – {was}.");
            }
            return neu;
        }
    }

    /// <summary>Termin absagen bzw. Einladung zurückziehen; die Betroffenen werden benachrichtigt.</summary>
    public void Absagen(Stand? s, Sprechtag t, Benutzer ich, StTermin x, string? grund)
    {
        if (ich.Rolle == Rollen.Eltern)
        {
            if (!ich.SchuelerIds.Contains(x.SchuelerId)) throw new UnauthorizedAccessException("Diesen Termin dürfen Sie nicht absagen.");
            if (x.HatZeit && Zeitpunkt($"{t.K.Abschnitte[x.Abschnitt].Datum}T{x.Beginn}") <= _uhr.Jetzt) throw new ArgumentException("Der Termin hat bereits begonnen.");
        }
        else Erlaubt(t, ich, x.Lehrer, x.Status == "einladung" ? null : r => r.TermineBearbeiten, "Das Absagen von Terminen");
        if (x.Status is "abgesagt" or "abgelehnt") return;
        AbsageSchreiben(s, t, ich, x, grund);
    }

    private void AbsageSchreiben(Stand? s, Sprechtag t, Benutzer ich, StTermin x, string? grund)
    {
        _db.Ausfuehren("UPDATE st_termine SET status = 'abgesagt', grund = @g WHERE id = @id", Db.P("@g", (grund ?? "").Trim()), Db.P("@id", x.Id));
        if (!Sichtbar(t, x)) return;
        var g = string.IsNullOrWhiteSpace(grund) ? "" : $" Grund: {grund.Trim()}";
        var was = x.Status == "einladung" ? "Die Einladung" : $"Der Termin ({ZeitText(t, x)})";
        var an = ich.Rolle == Rollen.Eltern ? KontenDerLehrkraft(x.Lehrer).Select(k => k.Id) : ElternVon(x.SchuelerId).Select(k => k.Id);
        Melden(ich, an, "Sprechtag: Termin abgesagt", $"{was} am Sprechtag „{t.K.Name}“ bei {LehrerName(s, x.Lehrer)} für {Name(s, x.SchuelerId)} wurde abgesagt.{g}");
    }

    // ------------------------------------------------------------------------------------------ Erziehungsberechtigte

    /// <summary>
    /// Sieht das Elternkonto den Sprechtag? Im Entwurf und in „Verwaltung LK“ nie; in der 1. Anmeldephase und der Pause danach nur mit
    /// Einladung bzw. Terminanfrage; ab der 2. Anmeldephase alle, bis der Sprechtag abgeschlossen ist.
    /// </summary>
    public bool SichtbarFuerEltern(Sprechtag t, Benutzer b)
    {
        var p = Phase(t);
        if (Rang(p) < Rang(Phase1) || p == Abgeschlossen) return false;
        if (Rang(p) >= Rang(Phase2)) return true;
        return Termine(t.Id).Any(x => b.SchuelerIds.Contains(x.SchuelerId) && x.Status is "einladung" or "angefragt" or "bestaetigt");
    }

    /// <summary>Sieht die Familie den Termin bzw. die Einladung (ab der 1. Anmeldephase)?</summary>
    public bool Sichtbar(Sprechtag t, StTermin x) => Rang(Phase(t)) >= Rang(Phase1);

    /// <summary>
    /// Buchung durch Erziehungsberechtigte: über eine Einladung (1. Anmeldephase, Pause und 2. Anmeldephase) bzw. frei in der 2. Anmeldephase –
    /// nur eigene Kinder, nur Lehrkräfte des Kindes, nach den Buchungsregeln. Der Termin ist sofort bestätigt; die Lehrkraft wird benachrichtigt.
    /// </summary>
    public StTermin Buchen(Stand? s, Sprechtag t, Benutzer eltern, string lehrer, int abschnitt, string beginn, int schuelerId, int? einladungId)
    {
        if (!eltern.SchuelerIds.Contains(schuelerId)) throw new UnauthorizedAccessException("Termine können nur für eigene Kinder gebucht werden.");
        var p = Phase(t);
        StTermin? einladung = null;
        if (einladungId is int eid)
        {
            einladung = Termin(eid);
            if (einladung is null || einladung.SprechtagId != t.Id || einladung.Status != "einladung" || einladung.SchuelerId != schuelerId || !Ci.Equals(einladung.Lehrer, lehrer))
                throw new ArgumentException("Die Einladung ist nicht mehr gültig.");
            if (p is not (Phase1 or Phase1Beendet or Phase2)) throw new ArgumentException("Die Anmeldung ist geschlossen.");
        }
        else if (p != Phase2)
            throw new ArgumentException(p is Phase1 or Phase1Beendet ? "Derzeit können Termine nur mit einer Einladung gebucht werden. Die freie Buchung ist ab der 2. Anmeldephase möglich."
                : "Die Anmeldung ist geschlossen.");
        if (!LehrkraefteDesKindes(s, Schueler(s, schuelerId)).Any(l => Ci.Equals(l.Kurz, lehrer)) && einladung is null)
            throw new ArgumentException("Termine sind nur bei Lehrkräften des Kindes möglich.");
        lock (_sperre)
        {
            if (!Frei(s, t, lehrer, abschnitt, schuelerId).Any(z => z.Von == beginn))
                throw new ArgumentException("Diese Zeit ist nicht mehr frei bzw. nach den Buchungsregeln nicht möglich. Bitte die Ansicht aktualisieren.");
            var ende = Text(Minuten(beginn) + t.K.Dauer);
            int id;
            if (einladung != null)
            {
                _db.Ausfuehren("UPDATE st_termine SET abschnitt = @a, beginn = @b, ende = @e, status = 'bestaetigt', benutzer_id = @u WHERE id = @id",
                    Db.P("@a", abschnitt), Db.P("@b", beginn), Db.P("@e", ende), Db.P("@u", eltern.Id), Db.P("@id", einladung.Id));
                id = einladung.Id;
            }
            else
                id = (int)_db.Einfuegen("""
                    INSERT INTO st_termine (sprechtag_id, lehrer, abschnitt, beginn, ende, schueler_id, art, status, erstellt_von, benutzer_id, erstellt)
                    VALUES (@s, @l, @a, @b, @e, @k, 'termin', 'bestaetigt', @u, @bid, @z)
                    """, Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@a", abschnitt), Db.P("@b", beginn), Db.P("@e", ende), Db.P("@k", schuelerId),
                    Db.P("@u", eltern.Login), Db.P("@bid", eltern.Id), Db.P("@z", Jetzt()));
            var x = Termin(id)!;
            Melden(eltern, KontenDerLehrkraft(lehrer).Select(k => k.Id), "Sprechtag: Termin gebucht",
                $"Für {Name(s, schuelerId)} wurde am Sprechtag „{t.K.Name}“ ein Termin gebucht: {ZeitText(t, x)}.");
            return x;
        }
    }

    /// <summary>Terminanfrage annehmen bzw. ablehnen (Erziehungsberechtigte, ab der 1. Anmeldephase bis zum Ende der Anmeldung).</summary>
    public StTermin Beantworten(Stand? s, Sprechtag t, Benutzer eltern, StTermin x, bool annehmen)
    {
        if (!eltern.SchuelerIds.Contains(x.SchuelerId)) throw new UnauthorizedAccessException("Diese Anfrage gilt nicht für Ihre Kinder.");
        if (x.Status != "angefragt") throw new ArgumentException("Die Terminanfrage ist nicht mehr offen.");
        var p = Phase(t);
        if (Rang(p) < Rang(Phase1) || Rang(p) >= Rang(Durchfuehrung)) throw new ArgumentException("Die Anfrage kann derzeit nicht beantwortet werden.");
        _db.Ausfuehren("UPDATE st_termine SET status = @st, benutzer_id = @u WHERE id = @id", Db.P("@st", annehmen ? "bestaetigt" : "abgelehnt"), Db.P("@u", eltern.Id), Db.P("@id", x.Id));
        Melden(eltern, KontenDerLehrkraft(x.Lehrer).Select(k => k.Id), annehmen ? "Sprechtag: Termin angenommen" : "Sprechtag: Termin abgelehnt",
            $"Der Termin am Sprechtag „{t.K.Name}“ für {Name(s, x.SchuelerId)} ({ZeitText(t, x)}) wurde {(annehmen ? "angenommen" : "abgelehnt")}.");
        return Termin(x.Id)!;
    }

    private static WebSchueler Schueler(Stand? s, int id) =>
        s?.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin bzw. unbekannter Schüler.");

    // ------------------------------------------------------------------------------------------ Benachrichtigungen

    private string Jetzt() => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv);

    public static string ZeitText(Sprechtag t, StTermin x)
    {
        if (!x.HatZeit || x.Abschnitt >= t.K.Abschnitte.Count) return "ohne feste Uhrzeit";
        var d = DateOnly.Parse(t.K.Abschnitte[x.Abschnitt].Datum, Inv);
        return $"{Plandienst.Wochentage[Plandienst.TagIndex(d)]}, {d:dd.MM.yyyy}, {x.Beginn}–{x.Ende} Uhr";
    }

    private int Melden(Benutzer absender, IEnumerable<int> konten, string betreff, string text)
    {
        var liste = konten.Distinct().Where(k => k != absender.Id).ToList();
        if (liste.Count == 0) return 0;
        try { return _mitteilungen.Benachrichtigen(absender, liste, betreff, text); }
        catch (Exception) { return 0; }
    }

    private void AnfrageMelden(Stand? s, Sprechtag t, Benutzer absender, StTermin x)
    {
        var text = x.Art == "einladung"
            ? $"{LehrerName(s, x.Lehrer)} lädt Sie zum Sprechtag „{t.K.Name}“ ({DatumText(t)}) zu einem Gespräch über {Name(s, x.SchuelerId)} ein{(x.Verpflichtend ? " (verpflichtend)" : "")}. Bitte buchen Sie unter „Sprechtag“ einen Termin."
            : $"{LehrerName(s, x.Lehrer)} schlägt Ihnen am Sprechtag „{t.K.Name}“ einen Termin für {Name(s, x.SchuelerId)} vor: {ZeitText(t, x)}. Bitte nehmen Sie ihn unter „Sprechtag“ an oder lehnen Sie ihn ab.";
        Melden(absender, ElternVon(x.SchuelerId).Select(b => b.Id), x.Art == "einladung" ? "Einladung zum Sprechtag" : "Terminanfrage zum Sprechtag", text);
        _db.Ausfuehren("UPDATE st_termine SET benachrichtigt = @z WHERE id = @id", Db.P("@z", Jetzt()), Db.P("@id", x.Id));
    }

    private Benutzer? Systemabsender() => _benutzer.Alle().Where(b => b.IstAdmin && !b.Gesperrt).OrderBy(b => b.Id).FirstOrDefault();

    private bool EinmalMerken(int sprechtagId, string schluessel)
    {
        if (_db.Wert("SELECT 1 FROM st_hinweise WHERE sprechtag_id = @s AND schluessel = @k", Db.P("@s", sprechtagId), Db.P("@k", schluessel)) != null) return false;
        _db.Ausfuehren("INSERT INTO st_hinweise (sprechtag_id, schluessel) VALUES (@s, @k)", Db.P("@s", sprechtagId), Db.P("@k", schluessel));
        return true;
    }

    /// <summary>
    /// Regelmäßige Aufgaben (alle 15 Minuten): Einladungen und Terminanfragen aus „Verwaltung LK“ mit Beginn der 1. Anmeldephase versenden,
    /// Familien mit ungenutzter Einladung zu Beginn der 2. Anmeldephase erinnern, an fehlende Räume erinnern (Lehrkräfte 28, 21, 14, 7, 2 und 1 Tag
    /// vor der 1. Anmeldephase, Administration 7, 2 und 1 Tag vorher) und unbeantwortete Terminanfragen beim Abschluss absagen.
    /// </summary>
    public int Aufgaben(Stand? s)
    {
        var absender = Systemabsender();
        if (absender is null) return 0;
        var n = 0;
        var jetzt = _uhr.Jetzt;
        foreach (var t in Alle().Where(t => t.IstVeroeffentlicht))
        {
            var p = Phase(t);
            var termine = Termine(t.Id);
            if (Rang(p) >= Rang(Phase1) && p != Abgeschlossen)
                foreach (var x in termine.Where(x => x.Benachrichtigt.Length == 0 && x.Status is "einladung" or "angefragt"))
                { AnfrageMelden(s, t, absender, x); n++; }
            if (p == Phase2 && EinmalMerken(t.Id, "phase2"))
                foreach (var x in termine.Where(x => x.Status == "einladung"))
                {
                    n += Melden(absender, ElternVon(x.SchuelerId).Select(b => b.Id), "Sprechtag: Anmeldung geöffnet",
                        $"Die allgemeine Anmeldephase des Sprechtags „{t.K.Name}“ hat begonnen. Sie haben noch eine offene Einladung von {LehrerName(s, x.Lehrer)} für {Name(s, x.SchuelerId)}.");
                }
            if (p == Verwaltung && !t.K.Online && Zeitpunkt(t.K.Phase1Von) is { } p1)
            {
                var tage = (int)Math.Ceiling((p1 - jetzt).TotalDays);
                var raeume = Raeume(t.Id);
                var ohne = s?.Lehrer.Keys.Where(l => Enumerable.Range(0, t.K.Abschnitte.Count).Any(a => Raum(raeume, l, a).Length == 0)).ToList() ?? new();
                if (ohne.Count > 0 && new[] { 28, 21, 14, 7, 2, 1 }.Contains(tage) && EinmalMerken(t.Id, $"raum-lk-{tage}"))
                    foreach (var l in ohne)
                        n += Melden(absender, KontenDerLehrkraft(l).Select(b => b.Id), "Sprechtag: Raum fehlt",
                            $"Für den Sprechtag „{t.K.Name}“ ist Ihnen noch kein Raum zugeordnet. Die 1. Anmeldephase beginnt in {tage} {(tage == 1 ? "Tag" : "Tagen")}.");
                if (ohne.Count > 0 && new[] { 7, 2, 1 }.Contains(tage) && EinmalMerken(t.Id, $"raum-admin-{tage}"))
                    n += Melden(absender, _benutzer.Alle().Where(b => !b.Gesperrt && IstVerwaltung(b)).Select(b => b.Id), "Sprechtag: Räume fehlen",
                        $"Für den Sprechtag „{t.K.Name}“ fehlt noch {ohne.Count} {(ohne.Count == 1 ? "Lehrkraft" : "Lehrkräften")} ein Raum. Die 1. Anmeldephase beginnt in {tage} {(tage == 1 ? "Tag" : "Tagen")}.");
            }
            if (p == Abgeschlossen && EinmalMerken(t.Id, "abgeschlossen"))
                _db.Ausfuehren("UPDATE st_termine SET status = 'abgesagt', grund = 'Nicht beantwortet' WHERE sprechtag_id = @s AND status = 'angefragt'", Db.P("@s", t.Id));
        }
        return n;
    }
}

/// <summary>Hintergrunddienst: Phasenwechsel des Sprechtags (Benachrichtigungen, Erinnerungen, Abschluss), alle 15 Minuten.</summary>
public sealed class Sprechtagerinnerung : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly Sprechtagdienst _dienst;
    private readonly Paketdienst _paket;
    private readonly ILogger<Sprechtagerinnerung> _log;

    public Sprechtagerinnerung(Sprechtagdienst dienst, Paketdienst paket, ILogger<Sprechtagerinnerung> log) { _dienst = dienst; _paket = paket; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var n = _dienst.Aufgaben(_paket.Aktuell);
                if (n > 0) _log.LogInformation("Sprechtag: {Anzahl} Benachrichtigungen versendet", n);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Sprechtag: Aufgaben fehlgeschlagen"); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
