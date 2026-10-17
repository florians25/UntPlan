using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Pause am Sprechtag (keine Termine), Uhrzeiten „HH:mm“.</summary>
public sealed record SprechtagPause(string Von, string Bis);

/// <summary>Beteiligte Lehrkraft mit Raum.</summary>
public sealed record SprechtagLehrkraft(string Lehrer, string Raum)
{
    /// <summary>Teilnahme abgesagt („yyyy-MM-dd HH:mm“; leer = nimmt teil).</summary>
    public string Abgesagt { get; init; } = "";
    public string AbsageGrund { get; init; } = "";
    /// <summary>Eigene Termindauer in Minuten (0 = Termindauer des Sprechtags).</summary>
    public int Dauer { get; init; }
    /// <summary>Gespräche finden online statt (Link für die Familien mit Termin).</summary>
    public bool Online { get; init; }
    /// <summary>Link zur Videokonferenz (nur bei <see cref="Online"/>; nur für Familien mit Termin, Lehrkraft und Verwaltung sichtbar).</summary>
    public string Link { get; init; } = "";
}

/// <summary>Zeitblock eines Sprechtags (mehrere Blöcke am Tag; Lücken zwischen den Blöcken werden zu Pausen).</summary>
public sealed record SprechtagZeitblock(string Von, string Bis);

/// <summary>
/// Weitere Optionen eines Sprechtags (als JSON gespeichert, neue Optionen erhalten so ihren Standard).
/// </summary>
public sealed record SprechtagOptionen
{
    /// <summary>Puffer in Minuten zwischen zwei Terminen (0–60).</summary>
    public int Puffer { get; init; }
    /// <summary>Höchstzahl der Termine je Familie an diesem Tag (0 = unbegrenzt).</summary>
    public int MaxTermineFamilie { get; init; }
    /// <summary>Warteliste für ausgebuchte Lehrkräfte.</summary>
    public bool Warteliste { get; init; }
    /// <summary>Erinnerung an Termine so viele Stunden vor Beginn (0 = keine).</summary>
    public int ErinnerungStunden { get; init; }
    /// <summary>Für Erziehungsberechtigte sichtbar (Ankündigung, Buchung).</summary>
    public bool SichtbarEltern { get; init; } = true;
    /// <summary>
    /// Für Schüler*innen sichtbar (nur Ankündigung und eigene Termine). Standard an – auch für gespeicherte Sprechtage ohne diese Option,
    /// damit die Ankündigung Eltern und Schüler*innen gleichermaßen erreicht.
    /// </summary>
    public bool SichtbarSchueler { get; init; } = true;
    /// <summary>Schon vor dem Anmeldezeitraum allen Beteiligten ankündigen (Status „angekündigt“).</summary>
    public bool Ankuendigen { get; init; } = true;
    /// <summary>Reihe mehrtägiger Sprechtage (Id des ersten Tages; 0 = keine Reihe).</summary>
    public int Reihe { get; init; }
}

/// <summary>
/// Elternsprechtag: ein Tag mit Zeitfenster (von–bis), gleich langen Terminen, Pausen, Einladungsphase der Lehrkräfte, Anmeldezeitraum
/// für Erziehungsberechtigte, beteiligten Lehrkräften (je mit Raum) und Sichtbarkeit.
/// </summary>
/// <param name="EinladungBis">Ende der Einladungsphase („yyyy-MM-ddTHH:mm“); leer = bis zum Ende des Anmeldezeitraums.</param>
/// <param name="AnmeldungVon">Beginn des Anmeldezeitraums für Erziehungsberechtigte („yyyy-MM-ddTHH:mm“).</param>
/// <param name="Sichtbar">Für Lehrkräfte und Erziehungsberechtigte sichtbar (sonst nur Administration).</param>
/// <param name="Raumwahl">Lehrkräfte dürfen ihren Raum selbst wählen.</param>
public sealed record Sprechtag(int Id, string Name, string Datum, string Beginn, string Ende, int Dauer, List<SprechtagPause> Pausen, string EinladungBis,
    string AnmeldungVon, string AnmeldungBis, bool Sichtbar, bool Raumwahl, string Hinweis, string Erstellt)
{
    public List<SprechtagLehrkraft> Lehrkraefte { get; init; } = new();
    /// <summary>Ganzer Sprechtag abgesagt („yyyy-MM-dd HH:mm“; leer = findet statt).</summary>
    public string Abgesagt { get; init; } = "";
    public string AbsageGrund { get; init; } = "";
    /// <summary>Ort des Sprechtags (frei, z. B. Gebäude).</summary>
    public string Ort { get; init; } = "";
    public SprechtagOptionen Optionen { get; init; } = new();
}

/// <summary>
/// Eingabe zum Anlegen bzw. Ändern eines Sprechtags. <paramref name="Zeitbloecke"/> ersetzt Beginn/Ende (Lücken werden Pausen);
/// <paramref name="WeitereTage"/> legt beim Anlegen je Datum einen gleichen Sprechtag derselben Reihe an.
/// </summary>
public sealed record SprechtagDaten(string? Name, string? Datum, string? Beginn, string? Ende, int? Dauer, List<SprechtagPause>? Pausen, string? EinladungBis,
    string? AnmeldungVon, string? AnmeldungBis, bool? Sichtbar, bool? Raumwahl, string? Hinweis, List<SprechtagLehrkraft>? Lehrkraefte,
    string? Ort = null, SprechtagOptionen? Optionen = null, List<SprechtagZeitblock>? Zeitbloecke = null, List<string>? WeitereTage = null);

/// <summary>Eintrag auf der Warteliste einer ausgebuchten Lehrkraft.</summary>
public sealed record SprechtagWarteplatz(int Id, int SprechtagId, string Lehrer, int SchuelerId, int? BenutzerId, string Erstellt, string Benachrichtigt);

/// <summary>Gebuchter bzw. von der Lehrkraft eingetragener Termin.</summary>
/// <param name="BenutzerId">Buchendes Elternkonto (leer, wenn die Lehrkraft bzw. Administration eingetragen hat).</param>
/// <param name="Quelle"><c>eltern</c>, <c>lehrkraft</c> oder <c>verwaltung</c>.</param>
public sealed record SprechtagTermin(int Id, int SprechtagId, string Lehrer, string Beginn, string Ende, int SchuelerId, int? BenutzerId, string Bemerkung,
    string Quelle, string Erstellt);

/// <summary>Einladung einer Lehrkraft an die Erziehungsberechtigten einer Schülerin bzw. eines Schülers.</summary>
public sealed record SprechtagEinladung(int Id, int SprechtagId, string Lehrer, int SchuelerId, string Text, string Erstellt, int Benachrichtigt);

/// <summary>Zeitfenster einer Lehrkraft: <c>frei</c>, <c>gebucht</c> oder <c>gesperrt</c>.</summary>
public sealed record SprechtagSlot(string Beginn, string Ende, string Status, SprechtagTermin? Termin);

/// <summary>
/// Sprechtag (Elternsprechtag): Verwaltung durch die Administration, Zeitraster und Sperren der Lehrkräfte, Einladungen über den
/// Mitteilungsdienst, Buchungen der Erziehungsberechtigten mit Konfliktprüfung:
/// <list type="bullet">
/// <item>ein Termin je Lehrkraft und Kind,</item>
/// <item>kein Zeitfenster doppelt (auch nicht gesperrte),</item>
/// <item>keine Überschneidung bei der Familie: Termine aller Kinder der Elternkonten eines Kindes und alle Buchungen dieser Konten.</item>
/// </list>
/// Phasen: <c>vorbereitung</c> (nicht sichtbar), <c>einladung</c> (vor dem Anmeldezeitraum), <c>anmeldung</c>, <c>geschlossen</c> (bis zum Tag), <c>vorbei</c>.
/// </summary>
public sealed class Sprechtagdienst
{
    public const string Vorbereitung = "vorbereitung", Einladung = "einladung", Anmeldung = "anmeldung", Geschlossen = "geschlossen", Vorbei = "vorbei", Abgesagt = "abgesagt";
    /// <summary>Status für die Anzeige: wie die Phase, aber „angekündigt“ statt „einladung“ (freigegeben, Anmeldung beginnt noch).</summary>
    public const string Angekuendigt = "angekuendigt";
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex Uhrzeit = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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

    // ------------------------------------------------------------------------------------------ Hilfen: Zeit und Phase

    public static int Minuten(string hhmm) => int.Parse(hhmm[..2], Inv) * 60 + int.Parse(hhmm[3..5], Inv);
    public static string Text(int minuten) => $"{minuten / 60:00}:{minuten % 60:00}";

    private static DateTime? Zeitpunkt(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : DateTime.TryParse(s.Replace(' ', 'T'), Inv, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Ende der Einladungsphase (ohne Angabe: Ende des Anmeldezeitraums).</summary>
    public static DateTime EinladungEnde(Sprechtag t) => Zeitpunkt(t.EinladungBis) ?? Zeitpunkt(t.AnmeldungBis) ?? DateTime.MaxValue;

    public string Phase(Sprechtag t)
    {
        var jetzt = _uhr.Jetzt;
        if (t.Abgesagt.Length > 0) return Abgesagt;
        if (!t.Sichtbar) return Vorbereitung;
        var tagEnde = DateOnly.Parse(t.Datum, Inv).ToDateTime(TimeOnly.MinValue).AddMinutes(Minuten(t.Ende));
        if (jetzt > tagEnde) return Vorbei;
        if (Zeitpunkt(t.AnmeldungVon) is { } von && jetzt < von) return Einladung;
        if (Zeitpunkt(t.AnmeldungBis) is { } bis && jetzt <= bis) return Anmeldung;
        return Geschlossen;
    }

    /// <summary>Anzeigestatus: <c>vorbereitung</c>, <c>angekuendigt</c>, <c>anmeldung</c>, <c>geschlossen</c>, <c>vorbei</c>, <c>abgesagt</c>.</summary>
    public string Status(Sprechtag t) => Phase(t) is var p && p == Einladung ? Angekuendigt : p;

    /// <summary>Darf das Konto den Sprechtag sehen? Verwaltung immer; sonst nur freigegebene – Kollegium, Eltern bzw. Schüler*innen nach den Optionen.</summary>
    public static bool SichtbarFuer(Sprechtag t, Benutzer b) =>
        Rechtedienst.Darf(b, Recht.SprechtagVerwalten) || (t.Sichtbar && (b.Kollegium || (b.Rolle == Rollen.Eltern && t.Optionen.SichtbarEltern) ||
            (b.Rolle == Rollen.Schueler && t.Optionen.SichtbarSchueler)));

    /// <summary>Termindauer einer Lehrkraft (eigene Dauer bzw. die des Sprechtags).</summary>
    public static int DauerVon(Sprechtag t, string? lehrer) => Beteiligt(t, lehrer) is { Dauer: > 0 } l ? l.Dauer : t.Dauer;

    /// <summary>Zeitblöcke des Tages (Beginn bis Ende ohne Pausen).</summary>
    public static List<SprechtagZeitblock> Zeitbloecke(Sprechtag t)
    {
        var res = new List<SprechtagZeitblock>();
        var z = Minuten(t.Beginn);
        foreach (var p in t.Pausen.OrderBy(p => p.Von, StringComparer.Ordinal))
        {
            if (Minuten(p.Von) > z) res.Add(new SprechtagZeitblock(Text(z), p.Von));
            z = Math.Max(z, Minuten(p.Bis));
        }
        if (Minuten(t.Ende) > z) res.Add(new SprechtagZeitblock(Text(z), t.Ende));
        return res;
    }

    /// <summary>Ist der Sprechtag (Tag und Uhrzeit des Endes) schon vorbei? Dann sind keine Änderungen mehr möglich.</summary>
    private bool IstVorbei(Sprechtag t) => _uhr.Jetzt > DateOnly.Parse(t.Datum, Inv).ToDateTime(TimeOnly.MinValue).AddMinutes(Minuten(t.Ende));

    /// <summary>
    /// Zeitfenster des Tages (für eine Lehrkraft mit ihrer Termindauer): ab Beginn im Takt Termindauer + Puffer; ein Fenster, das in eine Pause
    /// fiele, beginnt nach der Pause neu.
    /// </summary>
    public static List<(int Von, int Bis)> Zeitfenster(Sprechtag t, string? lehrer = null)
    {
        var res = new List<(int, int)>();
        var dauer = DauerVon(t, lehrer);
        var schritt = dauer + Math.Max(0, t.Optionen.Puffer);
        var pausen = t.Pausen.Select(p => (Von: Minuten(p.Von), Bis: Minuten(p.Bis))).OrderBy(p => p.Von).ToList();
        var ende = Minuten(t.Ende);
        var z = Minuten(t.Beginn);
        while (z + dauer <= ende && res.Count < 500)
        {
            var p = pausen.FirstOrDefault(p => z < p.Bis && z + dauer > p.Von);
            if (p != default) { z = Math.Max(z + 1, p.Bis); continue; }
            res.Add((z, z + dauer));
            z += schritt;
        }
        return res;
    }

    private static bool Ueberschneidung(string vonA, string bisA, string vonB, string bisB) => Minuten(vonA) < Minuten(bisB) && Minuten(vonB) < Minuten(bisA);

    // ------------------------------------------------------------------------------------------ Sprechtage lesen und speichern

    private static Sprechtag Lesen(DbDataReader r) => new(r.Zahl("id"), r.Text("name"), r.Text("datum"), r.Text("beginn"), r.Text("ende"), r.Zahl("dauer"),
        JsonSerializer.Deserialize<List<SprechtagPause>>(r.Text("pausen") is { Length: > 0 } p ? p : "[]", JsonOpt) ?? new(), r.Text("einladung_bis"),
        r.Text("anmeldung_von"), r.Text("anmeldung_bis"), r.Wahr("sichtbar"), r.Wahr("raumwahl"), r.Text("hinweis"), r.Text("erstellt"))
        { Abgesagt = r.Text("abgesagt"), AbsageGrund = r.Text("absage_grund"), Ort = r.Text("ort"), Optionen = OptionenLesen(r.Text("optionen")) };

    private static SprechtagOptionen OptionenLesen(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<SprechtagOptionen>(json, JsonOpt) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private List<SprechtagLehrkraft> LehrkraefteVon(int id) => _db.Liste("SELECT * FROM sprechtag_lehrer WHERE sprechtag_id = @id ORDER BY lehrer",
        r => new SprechtagLehrkraft(r.Text("lehrer"), r.Text("raum"))
        {
            Abgesagt = r.Text("abgesagt"), AbsageGrund = r.Text("absage_grund"), Dauer = r.Zahl("dauer"), Online = r.Wahr("online"), Link = r.Text("link"),
        }, Db.P("@id", id));

    /// <summary>Alle Tage derselben Reihe (mehrtägiger Sprechtag), aufsteigend nach Datum; ohne Reihe leer.</summary>
    public List<Sprechtag> Reihe(Sprechtag t) => t.Optionen.Reihe == 0 ? new()
        : _db.Liste("SELECT * FROM sprechtage ORDER BY datum, id", Lesen).Where(x => x.Optionen.Reihe == t.Optionen.Reihe).ToList();

    public List<Sprechtag> Alle() => _db.Liste("SELECT * FROM sprechtage ORDER BY datum DESC, id DESC", Lesen).Select(t => t with { Lehrkraefte = LehrkraefteVon(t.Id) }).ToList();

    public Sprechtag? Holen(int id) => _db.Einzel("SELECT * FROM sprechtage WHERE id = @id", Lesen, Db.P("@id", id)) is { } t ? t with { Lehrkraefte = LehrkraefteVon(id) } : null;

    public Sprechtag Pflicht(int id) => Holen(id) ?? throw new ArgumentException("Unbekannter Sprechtag.");

    /// <summary>
    /// Sprechtag anlegen (id = null) bzw. ändern. Gebuchte Termine müssen weiterhin ins Zeitraster passen. Beim Anlegen legt
    /// <see cref="SprechtagDaten.WeitereTage"/> je Datum einen gleichen Sprechtag derselben Reihe an.
    /// </summary>
    public Sprechtag Speichern(Stand? s, int? id, SprechtagDaten d)
    {
        var tage = id is null ? (d.WeitereTage ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList() : new List<string>();
        if (tage.Count > 10) throw new ArgumentException("Bitte höchstens 10 weitere Tage angeben.");
        // weitere Tage vorab prüfen (Datum, Anmeldezeitraum), damit nicht nur ein Teil der Reihe entsteht
        foreach (var tag in tage) Aufbauen(s, d with { Datum = tag, WeitereTage = null }, null);
        if (tage.Count > 0 && tage.Contains((d.Datum ?? "").Trim())) throw new ArgumentException("Ein weiterer Tag entspricht dem Datum des Sprechtags.");
        var erster = SpeichernEinzeln(s, id, d, 0);
        if (tage.Count == 0) return erster;
        erster = SpeichernEinzeln(s, erster.Id, d with { WeitereTage = null }, erster.Id);
        foreach (var tag in tage) SpeichernEinzeln(s, null, d with { Datum = tag, WeitereTage = null }, erster.Id);
        return erster;
    }

    /// <summary>Eingaben prüfen und den Sprechtag (ohne Id) aufbauen.</summary>
    private Sprechtag Aufbauen(Stand? s, SprechtagDaten d, int? id)
    {
        var name = (d.Name ?? "").Trim();
        if (name.Length is 0 or > 200) throw new ArgumentException("Bitte einen Namen (höchstens 200 Zeichen) eingeben.");
        if (!DateOnly.TryParse(d.Datum, Inv, out var datum)) throw new ArgumentException("Bitte ein gültiges Datum angeben.");
        string Zeit(string? x, string feld) => x is not null && Uhrzeit.IsMatch(x.Trim()) ? x.Trim() : throw new ArgumentException($"Bitte eine gültige Uhrzeit für „{feld}“ angeben (HH:MM).");
        var eingabePausen = (d.Pausen ?? new()).ToList();
        string beginn, ende;
        var bloecke = (d.Zeitbloecke ?? new()).Where(b => !string.IsNullOrWhiteSpace(b.Von) || !string.IsNullOrWhiteSpace(b.Bis)).ToList();
        if (bloecke.Count > 0)
        {
            // mehrere Zeitblöcke: Beginn des ersten bis Ende des letzten, Lücken werden Pausen
            var liste = bloecke.Select(b => (Von: Zeit(b.Von, "Zeitblock von"), Bis: Zeit(b.Bis, "Zeitblock bis"))).OrderBy(b => b.Von, StringComparer.Ordinal).ToList();
            if (liste.Count > 6) throw new ArgumentException("Bitte höchstens 6 Zeitblöcke angeben.");
            for (var i = 0; i < liste.Count; i++)
            {
                if (Minuten(liste[i].Bis) <= Minuten(liste[i].Von)) throw new ArgumentException("Ein Zeitblock muss nach seinem Beginn enden.");
                if (i > 0 && Minuten(liste[i].Von) < Minuten(liste[i - 1].Bis)) throw new ArgumentException("Zeitblöcke dürfen sich nicht überschneiden.");
                if (i > 0 && Minuten(liste[i].Von) > Minuten(liste[i - 1].Bis)) eingabePausen.Add(new SprechtagPause(liste[i - 1].Bis, liste[i].Von));
            }
            beginn = liste[0].Von;
            ende = liste[^1].Bis;
        }
        else
        {
            beginn = Zeit(d.Beginn, "von");
            ende = Zeit(d.Ende, "bis");
        }
        if (Minuten(ende) <= Minuten(beginn)) throw new ArgumentException("Das Ende muss nach dem Beginn liegen.");
        var dauer = d.Dauer ?? 10;
        if (dauer is < 3 or > 120) throw new ArgumentException("Die Termindauer muss zwischen 3 und 120 Minuten liegen.");
        var pausen = new List<SprechtagPause>();
        foreach (var p in eingabePausen)
        {
            if (string.IsNullOrWhiteSpace(p.Von) && string.IsNullOrWhiteSpace(p.Bis)) continue;
            var pv = Zeit(p.Von, "Pause von");
            var pb = Zeit(p.Bis, "Pause bis");
            if (Minuten(pb) <= Minuten(pv)) throw new ArgumentException("Eine Pause muss nach ihrem Beginn enden.");
            if (Minuten(pv) < Minuten(beginn) || Minuten(pb) > Minuten(ende)) throw new ArgumentException("Pausen müssen innerhalb des Zeitfensters liegen.");
            pausen.Add(new SprechtagPause(pv, pb));
        }
        pausen = pausen.OrderBy(p => p.Von, StringComparer.Ordinal).ToList();
        for (var i = 1; i < pausen.Count; i++)
            if (Minuten(pausen[i].Von) < Minuten(pausen[i - 1].Bis)) throw new ArgumentException("Pausen dürfen sich nicht überschneiden.");
        string Zp(string? x, string feld, bool pflicht)
        {
            if (string.IsNullOrWhiteSpace(x)) return pflicht ? throw new ArgumentException($"Bitte „{feld}“ angeben.") : "";
            return Zeitpunkt(x) is { } z ? z.ToString("yyyy-MM-dd'T'HH:mm", Inv) : throw new ArgumentException($"Ungültige Angabe bei „{feld}“.");
        }
        var anmeldungVon = Zp(d.AnmeldungVon, "Anmeldung ab", true);
        var anmeldungBis = Zp(d.AnmeldungBis, "Anmeldung bis", true);
        var einladungBis = Zp(d.EinladungBis, "Einladungen bis", false);
        if (Zeitpunkt(anmeldungBis) <= Zeitpunkt(anmeldungVon)) throw new ArgumentException("Der Anmeldezeitraum muss nach seinem Beginn enden.");
        var tagEnde = datum.ToDateTime(TimeOnly.MinValue).AddMinutes(Minuten(ende));
        if (Zeitpunkt(anmeldungBis) > tagEnde) throw new ArgumentException($"Der Anmeldezeitraum muss spätestens mit dem Sprechtag ({datum:dd.MM.yyyy}) enden.");
        if (einladungBis.Length > 0 && Zeitpunkt(einladungBis) > Zeitpunkt(anmeldungBis)) throw new ArgumentException("Die Einladungsphase muss spätestens mit dem Anmeldezeitraum enden.");
        var hinweis = (d.Hinweis ?? "").Trim();
        if (hinweis.Length > 2000) throw new ArgumentException("Der Hinweis ist zu lang (höchstens 2000 Zeichen).");
        var ort = (d.Ort ?? "").Trim();
        if (ort.Length > 300) throw new ArgumentException("Der Ort ist zu lang (höchstens 300 Zeichen).");
        var o = d.Optionen ?? new SprechtagOptionen();
        if (o.Puffer is < 0 or > 60) throw new ArgumentException("Der Puffer zwischen zwei Terminen muss zwischen 0 und 60 Minuten liegen.");
        if (o.MaxTermineFamilie is < 0 or > 50) throw new ArgumentException("Die Höchstzahl der Termine je Familie muss zwischen 0 (unbegrenzt) und 50 liegen.");
        if (o.ErinnerungStunden is < 0 or > 168) throw new ArgumentException("Die Erinnerung muss zwischen 0 (keine) und 168 Stunden vor dem Termin liegen.");

        var lehrkraefte = new List<SprechtagLehrkraft>();
        foreach (var l in d.Lehrkraefte ?? new())
        {
            var kurz = (l.Lehrer ?? "").Trim();
            if (kurz.Length == 0 || lehrkraefte.Any(x => Ci.Equals(x.Lehrer, kurz))) continue;
            if (s != null)
            {
                kurz = s.Lehrer.TryGetValue(kurz, out var le) ? le.Kurz : throw new ArgumentException($"Unbekannte Lehrkraft {kurz}.");
            }
            var raum = (l.Raum ?? "").Trim();
            if (raum.Length > 0 && s != null) raum = s.Raeume.TryGetValue(raum, out var ra) ? ra.Kurz : throw new ArgumentException($"Unbekannter Raum {raum}.");
            if (l.Dauer != 0 && l.Dauer is < 3 or > 120) throw new ArgumentException($"Die Termindauer von {kurz} muss zwischen 3 und 120 Minuten liegen (0 = Standard).");
            lehrkraefte.Add(new SprechtagLehrkraft(kurz, raum) { Dauer = l.Dauer, Online = l.Online, Link = l.Online ? LinkPruefen(l.Link) : "" });
        }
        if (lehrkraefte.Count == 0) throw new ArgumentException("Bitte mindestens eine beteiligte Lehrkraft wählen.");
        return new Sprechtag(id ?? 0, name, Plandienst.Iso(datum), beginn, ende, dauer, pausen, einladungBis, anmeldungVon, anmeldungBis, d.Sichtbar ?? false,
            d.Raumwahl ?? false, hinweis, "") { Lehrkraefte = lehrkraefte, Ort = ort, Optionen = o };
    }

    /// <summary>Link zur Videokonferenz: leer oder http(s)-Adresse, höchstens 500 Zeichen.</summary>
    public static string LinkPruefen(string? link)
    {
        var l = (link ?? "").Trim();
        if (l.Length == 0) return "";
        if (l.Length > 500) throw new ArgumentException("Der Link ist zu lang (höchstens 500 Zeichen).");
        if (!Uri.TryCreate(l, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Bitte einen gültigen Link (https://…) angeben.");
        return l;
    }

    private Sprechtag SpeichernEinzeln(Stand? s, int? id, SprechtagDaten d, int reihe)
    {
        var neu = Aufbauen(s, d, id);
        lock (_sperre)
        {
            var alt = id is int a ? Pflicht(a) : null;
            // Reihe bleibt beim Ändern erhalten
            neu = neu with { Optionen = neu.Optionen with { Reihe = reihe != 0 ? reihe : alt?.Optionen.Reihe ?? 0 } };
            if (alt != null)
            {
                if (alt.Abgesagt.Length > 0) throw new ArgumentException("Der Sprechtag wurde abgesagt und kann nicht mehr geändert werden.");
                // gebuchte Termine: Lehrkraft muss beteiligt bleiben und das Zeitfenster muss weiter bestehen
                foreach (var t in TermineVon(alt))
                {
                    if (!neu.Lehrkraefte.Any(l => Ci.Equals(l.Lehrer, t.Lehrer)))
                        throw new ArgumentException($"Die Lehrkraft {t.Lehrer} hat bereits gebuchte Termine und kann nicht entfernt werden.");
                    if (!Zeitfenster(neu, t.Lehrer).Any(f => Text(f.Von) == t.Beginn))
                        throw new ArgumentException($"Der gebuchte Termin um {t.Beginn} Uhr ({t.Lehrer}) passt nicht mehr ins Zeitraster. Bitte zuerst verschieben oder stornieren.");
                }
            }
            var pausenJson = JsonSerializer.Serialize(neu.Pausen, JsonOpt);
            var optionenJson = JsonSerializer.Serialize(neu.Optionen, JsonOpt);
            var neueId = id ?? 0;
            // abgesagte Teilnahmen bleiben beim Speichern erhalten
            var absagen = alt != null ? alt.Lehrkraefte.ToDictionary(l => l.Lehrer, Ci) : new Dictionary<string, SprechtagLehrkraft>(Ci);
            _db.Transaktion(c =>
            {
                var p = new[]
                {
                    Db.P("@n", neu.Name), Db.P("@d", neu.Datum), Db.P("@b", neu.Beginn), Db.P("@e", neu.Ende), Db.P("@du", neu.Dauer), Db.P("@p", pausenJson),
                    Db.P("@eb", neu.EinladungBis), Db.P("@av", neu.AnmeldungVon), Db.P("@ab", neu.AnmeldungBis), Db.P("@s", neu.Sichtbar), Db.P("@r", neu.Raumwahl),
                    Db.P("@h", neu.Hinweis), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@id", neueId), Db.P("@o", neu.Ort), Db.P("@op", optionenJson),
                };
                if (id is null)
                {
                    Db.Ausfuehren(c, """
                        INSERT INTO sprechtage (name, datum, beginn, ende, dauer, pausen, einladung_bis, anmeldung_von, anmeldung_bis, sichtbar, raumwahl, hinweis, erstellt, geaendert, ort, optionen)
                        VALUES (@n, @d, @b, @e, @du, @p, @eb, @av, @ab, @s, @r, @h, @z, @z, @o, @op)
                        """, p);
                    neueId = (int)Db.LetzteId(c);
                }
                else
                    Db.Ausfuehren(c, """
                        UPDATE sprechtage SET name = @n, datum = @d, beginn = @b, ende = @e, dauer = @du, pausen = @p, einladung_bis = @eb, anmeldung_von = @av,
                            anmeldung_bis = @ab, sichtbar = @s, raumwahl = @r, hinweis = @h, geaendert = @z, ort = @o, optionen = @op WHERE id = @id
                        """, p);
                Db.Ausfuehren(c, "DELETE FROM sprechtag_lehrer WHERE sprechtag_id = @id", Db.P("@id", neueId));
                foreach (var l in neu.Lehrkraefte)
                {
                    var a = absagen.GetValueOrDefault(l.Lehrer);
                    Db.Ausfuehren(c, "INSERT INTO sprechtag_lehrer (sprechtag_id, lehrer, raum, abgesagt, absage_grund, dauer, online, link) VALUES (@id, @l, @r, @ab, @ag, @du, @on, @li)",
                        Db.P("@id", neueId), Db.P("@l", l.Lehrer), Db.P("@r", l.Raum), Db.P("@ab", a?.Abgesagt ?? ""), Db.P("@ag", a?.AbsageGrund ?? ""),
                        Db.P("@du", l.Dauer), Db.P("@on", l.Online), Db.P("@li", l.Link));
                }
                // Sperren außerhalb des neuen Rasters bzw. von nicht mehr beteiligten Lehrkräften entfernen
                foreach (var (sid, lehrer, b) in Db.Liste(c, "SELECT id, lehrer, beginn FROM sprechtag_sperren WHERE sprechtag_id = @id", r => (r.Zahl("id"), r.Text("lehrer"), r.Text("beginn")), Db.P("@id", neueId)))
                    if (!neu.Lehrkraefte.Any(l => Ci.Equals(l.Lehrer, lehrer)) || !Zeitfenster(neu, lehrer).Any(f => Text(f.Von) == b))
                        Db.Ausfuehren(c, "DELETE FROM sprechtag_sperren WHERE id = @s", Db.P("@s", sid));
            });
            return Pflicht(neueId);
        }
    }

    public void Loeschen(int id)
    {
        Pflicht(id);
        _db.Transaktion(c =>
        {
            foreach (var t in new[] { "sprechtag_termine", "sprechtag_einladungen", "sprechtag_sperren", "sprechtag_lehrer", "sprechtag_warteliste" })
                Db.Ausfuehren(c, $"DELETE FROM {t} WHERE sprechtag_id = @id", Db.P("@id", id));
            Db.Ausfuehren(c, "DELETE FROM sprechtage WHERE id = @id", Db.P("@id", id));
        });
    }

    // ------------------------------------------------------------------------------------------ Lehrkräfte: Raum, Sperren, Zeitraster

    public static SprechtagLehrkraft? Beteiligt(Sprechtag t, string? lehrer) =>
        lehrer is null ? null : t.Lehrkraefte.FirstOrDefault(l => Ci.Equals(l.Lehrer, lehrer));

    /// <summary>Raum setzen; optional Online-Gespräche mit Link (<paramref name="online"/> null = unverändert).</summary>
    public void RaumSetzen(Stand? s, Sprechtag t, string lehrer, string? raum, bool? online = null, string? link = null)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        var r = raum is null ? l.Raum : raum.Trim();
        if (r.Length > 0 && s != null && raum is not null) r = s.Raeume.TryGetValue(r, out var ra) ? ra.Kurz : throw new ArgumentException($"Unbekannter Raum {r}.");
        var on = online ?? l.Online;
        var li = on ? (online is null && link is null ? l.Link : LinkPruefen(link)) : "";
        _db.Ausfuehren("UPDATE sprechtag_lehrer SET raum = @r, online = @on, link = @li WHERE sprechtag_id = @id AND lehrer = @l",
            Db.P("@r", r), Db.P("@on", on), Db.P("@li", li), Db.P("@id", t.Id), Db.P("@l", l.Lehrer));
    }

    public HashSet<string> Sperren(int id, string lehrer) =>
        _db.Liste("SELECT beginn FROM sprechtag_sperren WHERE sprechtag_id = @id AND lehrer = @l", r => r.Text("beginn"), Db.P("@id", id), Db.P("@l", lehrer)).ToHashSet();

    /// <summary>Zeitfenster sperren bzw. freigeben (Abwesenheit der Lehrkraft). Gebuchte Fenster lassen sich nicht sperren.</summary>
    public int SperrenSetzen(Sprechtag t, string lehrer, IEnumerable<string> beginne, bool gesperrt)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        var fenster = Zeitfenster(t, l.Lehrer).Select(f => Text(f.Von)).ToHashSet();
        var liste = beginne.Select(b => b.Trim()).Distinct().ToList();
        if (liste.Count == 0) throw new ArgumentException("Bitte mindestens ein Zeitfenster wählen.");
        if (liste.FirstOrDefault(b => !fenster.Contains(b)) is { } falsch) throw new ArgumentException($"Kein Zeitfenster um {falsch} Uhr.");
        lock (_sperre)
        {
            var vorhanden = Sperren(t.Id, l.Lehrer);
            if (gesperrt)
            {
                var gebucht = TermineVon(t).Where(x => Ci.Equals(x.Lehrer, l.Lehrer)).Select(x => x.Beginn).ToHashSet();
                if (liste.FirstOrDefault(gebucht.Contains) is { } b) throw new ArgumentException($"Das Zeitfenster um {b} Uhr ist bereits gebucht und kann nicht gesperrt werden.");
            }
            var n = 0;
            _db.Transaktion(c =>
            {
                foreach (var b in liste)
                {
                    if (gesperrt && !vorhanden.Contains(b))
                        n += Db.Ausfuehren(c, "INSERT INTO sprechtag_sperren (sprechtag_id, lehrer, beginn) VALUES (@id, @l, @b)", Db.P("@id", t.Id), Db.P("@l", l.Lehrer), Db.P("@b", b));
                    else if (!gesperrt && vorhanden.Contains(b))
                        n += Db.Ausfuehren(c, "DELETE FROM sprechtag_sperren WHERE sprechtag_id = @id AND lehrer = @l AND beginn = @b", Db.P("@id", t.Id), Db.P("@l", l.Lehrer), Db.P("@b", b));
                }
            });
            if (!gesperrt && n > 0) WartendeBenachrichtigen(t, l.Lehrer);
            return n;
        }
    }

    /// <summary>Zeitraster einer Lehrkraft mit Status je Fenster.</summary>
    public List<SprechtagSlot> Raster(Sprechtag t, string lehrer)
    {
        var sperren = Sperren(t.Id, lehrer);
        // abgesagte Teilnahme bzw. abgesagter Sprechtag: kein Fenster mehr frei
        if (t.Abgesagt.Length > 0 || Beteiligt(t, lehrer)?.Abgesagt.Length > 0) sperren = Zeitfenster(t, lehrer).Select(f => Text(f.Von)).ToHashSet();
        var termine = TermineVon(t).Where(x => Ci.Equals(x.Lehrer, lehrer)).ToDictionary(x => x.Beginn);
        return Zeitfenster(t, lehrer).Select(f =>
        {
            var b = Text(f.Von);
            return termine.TryGetValue(b, out var x) ? new SprechtagSlot(b, Text(f.Bis), "gebucht", x)
                : new SprechtagSlot(b, Text(f.Bis), sperren.Contains(b) ? "gesperrt" : "frei", null);
        }).ToList();
    }

    // ------------------------------------------------------------------------------------------ Termine

    private static SprechtagTermin LesenTermin(DbDataReader r, Sprechtag t) => new(r.Zahl("id"), r.Zahl("sprechtag_id"), r.Text("lehrer"), r.Text("beginn"),
        Text(Minuten(r.Text("beginn")) + DauerVon(t, r.Text("lehrer"))), r.Zahl("schueler_id"), r.ZahlOderNull("benutzer_id"), r.Text("bemerkung"), r.Text("quelle"), r.Text("erstellt"));

    public List<SprechtagTermin> Termine(int id) => Holen(id) is { } t ? TermineVon(t) : new();

    /// <summary>Termine eines Sprechtags (Ende nach der Termindauer der jeweiligen Lehrkraft).</summary>
    public List<SprechtagTermin> TermineVon(Sprechtag t) =>
        _db.Liste("SELECT * FROM sprechtag_termine WHERE sprechtag_id = @id ORDER BY beginn, lehrer", r => LesenTermin(r, t), Db.P("@id", t.Id));

    public SprechtagTermin? Termin(Sprechtag t, int terminId) =>
        _db.Einzel("SELECT * FROM sprechtag_termine WHERE id = @id AND sprechtag_id = @s", r => LesenTermin(r, t), Db.P("@id", terminId), Db.P("@s", t.Id));

    /// <summary>Elternkonten (nicht gesperrt) einer Schülerin bzw. eines Schülers.</summary>
    public List<Benutzer> ElternVon(int schuelerId) => _benutzer.Alle().Where(b => b.Rolle == Rollen.Eltern && !b.Gesperrt && b.SchuelerIds.Contains(schuelerId)).ToList();

    /// <summary>Familie eines Kindes: alle Elternkonten des Kindes (plus buchendes Konto) und alle Kinder dieser Konten.</summary>
    public (HashSet<int> Konten, HashSet<int> Kinder) Familie(int schuelerId, int? konto)
    {
        var alle = _benutzer.Alle().Where(b => b.Rolle == Rollen.Eltern).ToList();
        var konten = alle.Where(b => b.SchuelerIds.Contains(schuelerId)).Select(b => b.Id).ToHashSet();
        if (konto is int k) konten.Add(k);
        var kinder = alle.Where(b => konten.Contains(b.Id)).SelectMany(b => b.SchuelerIds).ToHashSet();
        kinder.Add(schuelerId);
        return (konten, kinder);
    }

    /// <summary>Prüft ein Zeitfenster: gültig, nicht gesperrt, frei, kein zweiter Termin bei der Lehrkraft für das Kind, keine Überschneidung bei der Familie.</summary>
    private void Pruefen(Stand? s, Sprechtag t, string lehrer, int schuelerId, string beginn, int? konto, int? ausser, bool gesperrteErlaubt = false)
    {
        if (t.Abgesagt.Length > 0) throw new ArgumentException("Der Sprechtag wurde abgesagt.");
        if (Beteiligt(t, lehrer)?.Abgesagt.Length > 0) throw new ArgumentException($"{LehrerName(s, lehrer)} hat die Teilnahme am Sprechtag abgesagt.");
        if (!Zeitfenster(t, lehrer).Any(f => Text(f.Von) == beginn)) throw new ArgumentException($"Kein Zeitfenster um {beginn} Uhr.");
        var ende = Text(Minuten(beginn) + DauerVon(t, lehrer));
        var termine = TermineVon(t).Where(x => x.Id != ausser).ToList();
        if (termine.Any(x => Ci.Equals(x.Lehrer, lehrer) && x.Beginn == beginn)) throw new ArgumentException($"Das Zeitfenster um {beginn} Uhr ist bereits vergeben.");
        if (!gesperrteErlaubt && Sperren(t.Id, lehrer).Contains(beginn)) throw new ArgumentException($"Das Zeitfenster um {beginn} Uhr ist gesperrt.");
        if (termine.Any(x => Ci.Equals(x.Lehrer, lehrer) && x.SchuelerId == schuelerId))
            throw new ArgumentException($"Für {Name(s, schuelerId)} gibt es bei dieser Lehrkraft bereits einen Termin (ein Termin je Lehrkraft und Kind).");
        var (konten, kinder) = Familie(schuelerId, konto);
        var konflikt = termine.FirstOrDefault(x => Ueberschneidung(x.Beginn, x.Ende, beginn, ende) && (kinder.Contains(x.SchuelerId) || (x.BenutzerId is int b && konten.Contains(b))));
        if (konflikt != null)
            throw new ArgumentException($"Überschneidung: Um {konflikt.Beginn} Uhr ist bereits ein Termin bei {LehrerName(s, konflikt.Lehrer)} für {Name(s, konflikt.SchuelerId)} eingetragen.");
    }

    private static string Name(Stand? s, int schuelerId) => s?.Schueler.FirstOrDefault(x => x.Id == schuelerId) is { } x ? $"{x.Vorname} {x.Name}".Trim() : $"Schüler*in {schuelerId}";

    public static string LehrerName(Stand? s, string kurz) =>
        s != null && s.Lehrer.TryGetValue(kurz, out var l) && l.Name.Length > 0 ? $"{(l.Vorname.Length > 0 ? l.Vorname + " " : "")}{l.Name}" : kurz;

    private static WebSchueler Schueler(Stand? s, int id) =>
        s?.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin bzw. unbekannter Schüler.");

    private SprechtagTermin Einfuegen(Sprechtag t, string lehrer, int schuelerId, string beginn, int? konto, string bemerkung, string quelle)
    {
        var id = 0;
        try
        {
            _db.Transaktion(c =>
            {
                Db.Ausfuehren(c, """
                    INSERT INTO sprechtag_termine (sprechtag_id, lehrer, beginn, schueler_id, benutzer_id, bemerkung, quelle, erstellt)
                    VALUES (@s, @l, @b, @k, @u, @m, @q, @z)
                    """, Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@b", beginn), Db.P("@k", schuelerId), Db.P("@u", konto), Db.P("@m", bemerkung),
                    Db.P("@q", quelle), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)));
                id = (int)Db.LetzteId(c);
                // Termin erhalten: Platz auf der Warteliste dieser Lehrkraft entfällt
                Db.Ausfuehren(c, "DELETE FROM sprechtag_warteliste WHERE sprechtag_id = @s AND lehrer = @l AND schueler_id = @k", Db.P("@s", t.Id), Db.P("@l", lehrer), Db.P("@k", schuelerId));
            });
        }
        catch (DbException) { throw new ArgumentException("Das Zeitfenster wurde gerade vergeben. Bitte die Ansicht aktualisieren."); }
        return Termin(t, id)!;
    }

    /// <summary>Buchung durch ein Elternkonto: nur im Anmeldezeitraum, nur eigene Kinder, nur Lehrkräfte des Kindes.</summary>
    public SprechtagTermin Buchen(Stand? s, Sprechtag t, Benutzer eltern, string lehrer, int schuelerId, string beginn)
    {
        if (eltern.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Termine buchen Erziehungsberechtigte.");
        if (!eltern.SchuelerIds.Contains(schuelerId)) throw new UnauthorizedAccessException("Diese Schülerin bzw. dieser Schüler ist Ihrem Konto nicht zugeordnet.");
        if (Phase(t) != Anmeldung) throw new ArgumentException("Termine können nur im Anmeldezeitraum gebucht werden.");
        var kind = Schueler(s, schuelerId);
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (!LehrkraefteDesKindes(s, t, kind).Any(x => Ci.Equals(x.Kurz, l.Lehrer))) throw new UnauthorizedAccessException("Diese Lehrkraft unterrichtet Ihr Kind nicht.");
        lock (_sperre)
        {
            Pruefen(s, t, l.Lehrer, schuelerId, beginn, eltern.Id, null);
            if (t.Optionen.MaxTermineFamilie > 0 && FamilienTermine(t, schuelerId, eltern.Id) >= t.Optionen.MaxTermineFamilie)
                throw new ArgumentException($"Je Familie {(t.Optionen.MaxTermineFamilie == 1 ? "ist höchstens ein Termin" : $"sind höchstens {t.Optionen.MaxTermineFamilie} Termine")} möglich.");
            return Einfuegen(t, l.Lehrer, schuelerId, beginn, eltern.Id, "", "eltern");
        }
    }

    /// <summary>Zahl der Termine der Familie eines Kindes (alle Kinder der Elternkonten und Buchungen dieser Konten).</summary>
    public int FamilienTermine(Sprechtag t, int schuelerId, int? konto)
    {
        var (konten, kinder) = Familie(schuelerId, konto);
        return TermineVon(t).Count(x => kinder.Contains(x.SchuelerId) || (x.BenutzerId is int b && konten.Contains(b)));
    }

    /// <summary>Termin durch die Lehrkraft bzw. Administration eintragen (auch für Familien ohne Konto, auch nach dem Anmeldezeitraum).</summary>
    public SprechtagTermin Eintragen(Stand? s, Sprechtag t, Benutzer ich, string lehrer, int schuelerId, string beginn, string? bemerkung)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        Schueler(s, schuelerId);
        var text = (bemerkung ?? "").Trim();
        if (text.Length > 500) throw new ArgumentException("Die Bemerkung ist zu lang (höchstens 500 Zeichen).");
        lock (_sperre)
        {
            Pruefen(s, t, l.Lehrer, schuelerId, beginn, null, null, gesperrteErlaubt: true);
            // ein manuell eingetragener Termin in einem gesperrten Fenster hebt die Sperre auf
            _db.Ausfuehren("DELETE FROM sprechtag_sperren WHERE sprechtag_id = @id AND lehrer = @l AND beginn = @b", Db.P("@id", t.Id), Db.P("@l", l.Lehrer), Db.P("@b", beginn));
            return Einfuegen(t, l.Lehrer, schuelerId, beginn, null, text, Rechtedienst.Darf(ich, Recht.SprechtagVerwalten) && !Ci.Equals(ich.LehrerKuerzel, l.Lehrer) ? "verwaltung" : "lehrkraft");
        }
    }

    /// <summary>Termin verschieben (neue Uhrzeit) bzw. Bemerkung ändern – Lehrkraft des Termins oder Administration.</summary>
    public SprechtagTermin Verschieben(Stand? s, Sprechtag t, SprechtagTermin x, string? beginn, string? bemerkung)
    {
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        var neu = string.IsNullOrWhiteSpace(beginn) ? x.Beginn : beginn.Trim();
        var text = bemerkung is null ? x.Bemerkung : bemerkung.Trim();
        if (text.Length > 500) throw new ArgumentException("Die Bemerkung ist zu lang (höchstens 500 Zeichen).");
        lock (_sperre)
        {
            if (neu != x.Beginn) Pruefen(s, t, x.Lehrer, x.SchuelerId, neu, x.BenutzerId, x.Id, gesperrteErlaubt: true);
            try
            {
                _db.Transaktion(c =>
                {
                    Db.Ausfuehren(c, "UPDATE sprechtag_termine SET beginn = @b, bemerkung = @m WHERE id = @id", Db.P("@b", neu), Db.P("@m", text), Db.P("@id", x.Id));
                    Db.Ausfuehren(c, "DELETE FROM sprechtag_sperren WHERE sprechtag_id = @id AND lehrer = @l AND beginn = @b", Db.P("@id", t.Id), Db.P("@l", x.Lehrer), Db.P("@b", neu));
                });
            }
            catch (DbException) { throw new ArgumentException("Das Zeitfenster wurde gerade vergeben. Bitte die Ansicht aktualisieren."); }
            return Termin(t, x.Id)!;
        }
    }

    // ------------------------------------------------------------------------------------------ Warteliste

    public List<SprechtagWarteplatz> Warteliste(int id) => _db.Liste("SELECT * FROM sprechtag_warteliste WHERE sprechtag_id = @id ORDER BY id",
        r => new SprechtagWarteplatz(r.Zahl("id"), r.Zahl("sprechtag_id"), r.Text("lehrer"), r.Zahl("schueler_id"), r.ZahlOderNull("benutzer_id"), r.Text("erstellt"),
            r.Text("benachrichtigt")), Db.P("@id", id));

    /// <summary>Position (ab 1) eines Eintrags auf der Warteliste seiner Lehrkraft.</summary>
    public static int Position(List<SprechtagWarteplatz> liste, SprechtagWarteplatz w) =>
        liste.Where(x => Ci.Equals(x.Lehrer, w.Lehrer)).ToList().FindIndex(x => x.Id == w.Id) + 1;

    /// <summary>Auf die Warteliste einer ausgebuchten Lehrkraft (Option „Warteliste“, nur im Anmeldezeitraum, nur eigene Kinder).</summary>
    public SprechtagWarteplatz WartelisteEintragen(Stand? s, Sprechtag t, Benutzer eltern, string lehrer, int schuelerId)
    {
        if (eltern.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Auf die Warteliste tragen sich Erziehungsberechtigte ein.");
        if (!eltern.SchuelerIds.Contains(schuelerId)) throw new UnauthorizedAccessException("Diese Schülerin bzw. dieser Schüler ist Ihrem Konto nicht zugeordnet.");
        if (!t.Optionen.Warteliste) throw new ArgumentException("Für diesen Sprechtag gibt es keine Warteliste.");
        if (Phase(t) != Anmeldung) throw new ArgumentException("Die Warteliste steht nur im Anmeldezeitraum zur Verfügung.");
        var kind = Schueler(s, schuelerId);
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (l.Abgesagt.Length > 0) throw new ArgumentException($"{LehrerName(s, l.Lehrer)} hat die Teilnahme am Sprechtag abgesagt.");
        if (!LehrkraefteDesKindes(s, t, kind).Any(x => Ci.Equals(x.Kurz, l.Lehrer))) throw new UnauthorizedAccessException("Diese Lehrkraft unterrichtet Ihr Kind nicht.");
        lock (_sperre)
        {
            if (TermineVon(t).Any(x => Ci.Equals(x.Lehrer, l.Lehrer) && x.SchuelerId == schuelerId)) throw new ArgumentException("Für Ihr Kind gibt es bei dieser Lehrkraft bereits einen Termin.");
            if (Raster(t, l.Lehrer).Any(x => x.Status == "frei")) throw new ArgumentException("Bei dieser Lehrkraft sind noch Termine frei – bitte direkt buchen.");
            var vorhanden = Warteliste(t.Id).FirstOrDefault(x => Ci.Equals(x.Lehrer, l.Lehrer) && x.SchuelerId == schuelerId);
            if (vorhanden != null) return vorhanden;
            try
            {
                _db.Ausfuehren("INSERT INTO sprechtag_warteliste (sprechtag_id, lehrer, schueler_id, benutzer_id, erstellt) VALUES (@s, @l, @k, @b, @z)",
                    Db.P("@s", t.Id), Db.P("@l", l.Lehrer), Db.P("@k", schuelerId), Db.P("@b", eltern.Id), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)));
            }
            catch (DbException) { /* gleichzeitig eingetragen */ }
            return Warteliste(t.Id).First(x => Ci.Equals(x.Lehrer, l.Lehrer) && x.SchuelerId == schuelerId);
        }
    }

    /// <summary>Eintrag von der Warteliste entfernen: Eltern des Kindes, die Lehrkraft bzw. die Verwaltung.</summary>
    public void WartelisteLoeschen(Sprechtag t, Benutzer ich, int wid)
    {
        var w = Warteliste(t.Id).FirstOrDefault(x => x.Id == wid) ?? throw new ArgumentException("Unbekannter Eintrag auf der Warteliste.");
        var darf = Rechtedienst.Darf(ich, Recht.SprechtagVerwalten) || (ich.Kollegium && Ci.Equals(ich.LehrerKuerzel, w.Lehrer)) ||
            (ich.Rolle == Rollen.Eltern && ich.SchuelerIds.Contains(w.SchuelerId));
        if (!darf) throw new UnauthorizedAccessException("Diesen Eintrag dürfen Sie nicht entfernen.");
        _db.Ausfuehren("DELETE FROM sprechtag_warteliste WHERE id = @id", Db.P("@id", wid));
    }

    /// <summary>Absender der Systemmitteilungen (Warteliste, Erinnerungen): das erste aktive Administrationskonto.</summary>
    private Benutzer? Systemabsender() => _benutzer.Alle().Where(b => b.IstAdmin && !b.Gesperrt).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>
    /// Ein Zeitfenster der Lehrkraft ist frei geworden: alle noch nicht benachrichtigten Familien auf ihrer Warteliste erhalten eine Mitteilung
    /// (Absender: Administration, damit keine Namen anderer Familien erscheinen). Liefert die Zahl benachrichtigter Konten.
    /// </summary>
    public int WartendeBenachrichtigen(Sprechtag t, string lehrer)
    {
        if (!t.Optionen.Warteliste || Phase(t) != Anmeldung) return 0;
        var wartend = Warteliste(t.Id).Where(x => Ci.Equals(x.Lehrer, lehrer) && x.Benachrichtigt.Length == 0).ToList();
        if (wartend.Count == 0 || Systemabsender() is not { } absender) return 0;
        var n = 0;
        var datum = DateOnly.Parse(t.Datum, Inv);
        foreach (var w in wartend)
        {
            var konten = ElternVon(w.SchuelerId).Select(b => b.Id).ToHashSet();
            if (w.BenutzerId is int k) konten.Add(k);
            n += Melden(absender, konten, "Sprechtag: Termin frei geworden",
                $"Bei {LehrerName(_paket?.Aktuell, lehrer)} ist am Sprechtag „{t.Name}“ ({datum:dd.MM.yyyy}) ein Termin frei geworden. Sie stehen auf der Warteliste – " +
                "bitte buchen Sie unter „Sprechtag“, solange der Termin frei ist.");
            _db.Ausfuehren("UPDATE sprechtag_warteliste SET benachrichtigt = @z WHERE id = @id", Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@id", w.Id));
        }
        return n;
    }

    // ------------------------------------------------------------------------------------------ Erinnerungen

    /// <summary>
    /// Erinnerungen versenden (Option „Erinnerung … Stunden vorher“): je fälligem Termin eine Mitteilung an die Familie und eine je Lehrkraft
    /// mit ihren Terminen. Jeder Termin wird nur einmal erinnert. Liefert die Zahl erinnerter Termine.
    /// </summary>
    public int ErinnerungenSenden(Stand? s)
    {
        var jetzt = _uhr.Jetzt;
        var absender = Systemabsender();
        if (absender is null) return 0;
        var n = 0;
        foreach (var t in Alle().Where(t => t.Sichtbar && t.Abgesagt.Length == 0 && t.Optionen.ErinnerungStunden > 0))
        {
            var tag = DateOnly.Parse(t.Datum, Inv).ToDateTime(TimeOnly.MinValue);
            var offen = _db.Liste("SELECT id FROM sprechtag_termine WHERE sprechtag_id = @id AND erinnert = ''", r => r.Zahl("id"), Db.P("@id", t.Id)).ToHashSet();
            var faellig = TermineVon(t).Where(x => offen.Contains(x.Id)).Where(x =>
            {
                var beginn = tag.AddMinutes(Minuten(x.Beginn));
                return jetzt < beginn && jetzt >= beginn.AddHours(-t.Optionen.ErinnerungStunden);
            }).ToList();
            if (faellig.Count == 0) continue;
            var datum = $"{Plandienst.Wochentage[Plandienst.TagIndex(DateOnly.FromDateTime(tag))]}, {tag:dd.MM.yyyy}";
            foreach (var x in faellig)
            {
                var l = Beteiligt(t, x.Lehrer);
                var wo = l is { Online: true } ? $"Online-Gespräch{(l.Link.Length > 0 ? $": {l.Link}" : "")}" : l?.Raum is { Length: > 0 } r ? $"Raum {r}" : t.Ort;
                Melden(absender, ElternDesTermins(x), "Erinnerung: Termin am Sprechtag",
                    $"Erinnerung: {datum}, {x.Beginn}–{x.Ende} Uhr Gespräch mit {LehrerName(s, x.Lehrer)} über {Name(s, x.SchuelerId)} (Sprechtag „{t.Name}“)." +
                    (wo.Length > 0 ? $"\nOrt: {wo}" : ""));
            }
            foreach (var g in faellig.GroupBy(x => x.Lehrer, Ci))
                Melden(absender, KontenDerLehrkraft(g.Key).Select(b => b.Id), "Erinnerung: Ihre Termine am Sprechtag",
                    $"Erinnerung an Ihre Termine am Sprechtag „{t.Name}“ ({datum}):\n" + string.Join("\n", g.Select(x => $"{x.Beginn}–{x.Ende} Uhr: {Name(s, x.SchuelerId)}")));
            _db.Transaktion(c =>
            {
                foreach (var x in faellig) Db.Ausfuehren(c, "UPDATE sprechtag_termine SET erinnert = @z WHERE id = @id", Db.P("@z", jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@id", x.Id));
            });
            n += faellig.Count;
        }
        return n;
    }

    /// <summary>Termin stornieren: Administration, Lehrkraft des Termins oder Elternkonto des Kindes (nur im Anmeldezeitraum).</summary>
    public void Stornieren(Sprechtag t, Benutzer ich, SprechtagTermin x)
    {
        if (!DarfTermin(t, ich, x)) throw new UnauthorizedAccessException("Diesen Termin dürfen Sie nicht stornieren.");
        if (ich.Rolle == Rollen.Eltern && Phase(t) != Anmeldung) throw new ArgumentException("Termine können nur im Anmeldezeitraum storniert werden. Bitte wenden Sie sich an die Lehrkraft.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        _db.Ausfuehren("DELETE FROM sprechtag_termine WHERE id = @id", Db.P("@id", x.Id));
        WartendeBenachrichtigen(t, x.Lehrer);
    }

    /// <summary>Darf das Konto diesen Termin ändern bzw. stornieren?</summary>
    public static bool DarfTermin(Sprechtag t, Benutzer ich, SprechtagTermin x) =>
        Rechtedienst.Darf(ich, Recht.SprechtagVerwalten) || (ich.Kollegium && Ci.Equals(ich.LehrerKuerzel, x.Lehrer)) || (ich.Rolle == Rollen.Eltern && ich.SchuelerIds.Contains(x.SchuelerId));

    // ------------------------------------------------------------------------------------------ Absagen

    /// <summary>Konten einer Lehrkraft (Kollegium mit diesem Kürzel, nicht gesperrt).</summary>
    public List<Benutzer> KontenDerLehrkraft(string lehrer) => _benutzer.Alle().Where(b => !b.Gesperrt && b.Kollegium && Ci.Equals(b.LehrerKuerzel, lehrer)).ToList();

    /// <summary>Betroffene Elternkonten eines Termins: alle Elternkonten des Kindes und das buchende Konto.</summary>
    private HashSet<int> ElternDesTermins(SprechtagTermin x)
    {
        var res = ElternVon(x.SchuelerId).Select(b => b.Id).ToHashSet();
        if (x.BenutzerId is int k) res.Add(k);
        return res;
    }

    private static string Grund(string? grund)
    {
        var g = (grund ?? "").Trim();
        if (g.Length > 1000) throw new ArgumentException("Der Grund ist zu lang (höchstens 1000 Zeichen).");
        return g;
    }

    private string Zeile(Stand? s, Sprechtag t) => $"am Sprechtag „{t.Name}“ ({Plandienst.Wochentage[Plandienst.TagIndex(DateOnly.Parse(t.Datum, Inv))]}, {DateOnly.Parse(t.Datum, Inv):dd.MM.yyyy})";

    /// <summary>Mitteilung an Konten (Fehler beim Versand blockieren die Absage nicht). Liefert die Zahl benachrichtigter Konten.</summary>
    private int Melden(Benutzer ich, IEnumerable<int> konten, string betreff, string text)
    {
        var ids = konten.Where(k => k != ich.Id).ToHashSet();
        if (ids.Count == 0) return 0;
        try { _mitteilungen.Benachrichtigen(ich, ids, betreff, text); return ids.Count; }
        catch (ArgumentException) { return 0; }
    }

    /// <summary>
    /// Einzelnen Termin absagen (optional mit Grund): Erziehungsberechtigte des Kindes (bis zum Ende des Sprechtags), Lehrkraft des Termins bzw.
    /// Administration. Die jeweils andere Seite erhält eine Mitteilung. Liefert die Zahl benachrichtigter Konten.
    /// </summary>
    public int TerminAbsagen(Stand? s, Sprechtag t, Benutzer ich, SprechtagTermin x, string? grund)
    {
        if (!DarfTermin(t, ich, x)) throw new UnauthorizedAccessException("Diesen Termin dürfen Sie nicht absagen.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        var g = Grund(grund);
        _db.Ausfuehren("DELETE FROM sprechtag_termine WHERE id = @id", Db.P("@id", x.Id));
        WartendeBenachrichtigen(t, x.Lehrer);
        var kind = Name(s, x.SchuelerId);
        var wer = ich.Anzeigename.Length > 0 ? ich.Anzeigename : ich.Login;
        var zusatz = g.Length > 0 ? $"\n\nGrund: {g}" : "";
        if (ich.Rolle == Rollen.Eltern)
        {
            // Lehrkraft und die übrigen Elternkonten des Kindes
            var konten = KontenDerLehrkraft(x.Lehrer).Select(b => b.Id).Concat(ElternDesTermins(x));
            return Melden(ich, konten, $"Sprechtag: Termin abgesagt ({kind})",
                $"{wer} hat den Termin um {x.Beginn} Uhr für {kind} {Zeile(s, t)} abgesagt.{zusatz}");
        }
        return Melden(ich, ElternDesTermins(x), "Sprechtag: Termin abgesagt",
            $"Ihr Termin bei {LehrerName(s, x.Lehrer)} {Zeile(s, t)} um {x.Beginn} Uhr für {kind} wurde abgesagt.{zusatz}\n\nBitte buchen Sie bei Bedarf einen neuen Termin.");
    }

    /// <summary>
    /// Gesamte Teilnahme einer Lehrkraft absagen: alle Termine der Lehrkraft entfallen, die betroffenen Erziehungsberechtigten erhalten eine Mitteilung,
    /// die Lehrkraft ist nicht mehr buchbar. Liefert (abgesagte Termine, benachrichtigte Konten).
    /// </summary>
    public (int Termine, int Benachrichtigt) TeilnahmeAbsagen(Stand? s, Sprechtag t, Benutzer ich, string lehrer, string? grund)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (t.Abgesagt.Length > 0) throw new ArgumentException("Der Sprechtag wurde bereits abgesagt.");
        if (l.Abgesagt.Length > 0) throw new ArgumentException("Die Teilnahme wurde bereits abgesagt.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        var g = Grund(grund);
        List<SprechtagTermin> termine;
        lock (_sperre)
        {
            termine = TermineVon(t).Where(x => Ci.Equals(x.Lehrer, l.Lehrer)).ToList();
            _db.Transaktion(c =>
            {
                Db.Ausfuehren(c, "UPDATE sprechtag_lehrer SET abgesagt = @z, absage_grund = @g WHERE sprechtag_id = @id AND lehrer = @l",
                    Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@g", g), Db.P("@id", t.Id), Db.P("@l", l.Lehrer));
                Db.Ausfuehren(c, "DELETE FROM sprechtag_termine WHERE sprechtag_id = @id AND lehrer = @l", Db.P("@id", t.Id), Db.P("@l", l.Lehrer));
            });
        }
        var n = 0;
        var zusatz = g.Length > 0 ? $"\n\nGrund: {g}" : "";
        foreach (var x in termine)
            n += Melden(ich, ElternDesTermins(x), "Sprechtag: Termin abgesagt",
                $"{LehrerName(s, l.Lehrer)} kann {Zeile(s, t)} leider nicht teilnehmen. Ihr Termin um {x.Beginn} Uhr für {Name(s, x.SchuelerId)} entfällt.{zusatz}");
        // Administration informieren, wenn die Lehrkraft selbst absagt
        if (!ich.IstAdmin)
            Melden(ich, _benutzer.Alle().Where(b => b.IstAdmin && !b.Gesperrt).Select(b => b.Id), $"Sprechtag: Teilnahme abgesagt ({l.Lehrer})",
                $"{LehrerName(s, l.Lehrer)} hat die Teilnahme {Zeile(s, t)} abgesagt; {termine.Count} Termin(e) entfallen.{zusatz}");
        return (termine.Count, n);
    }

    /// <summary>Abgesagte Teilnahme wieder aufnehmen (Lehrkraft bzw. Administration) – die Zeitfenster sind danach wieder frei.</summary>
    public void TeilnahmeWiederaufnehmen(Sprechtag t, string lehrer)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (t.Abgesagt.Length > 0) throw new ArgumentException("Der Sprechtag wurde abgesagt.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        _db.Ausfuehren("UPDATE sprechtag_lehrer SET abgesagt = '', absage_grund = '' WHERE sprechtag_id = @id AND lehrer = @l", Db.P("@id", t.Id), Db.P("@l", l.Lehrer));
        WartendeBenachrichtigen(Pflicht(t.Id), l.Lehrer);
    }

    /// <summary>
    /// Ganzen Sprechtag absagen (Administration): alle Termine entfallen; alle beteiligten Lehrkräfte, alle Erziehungsberechtigten mit Termin
    /// bzw. Einladung werden benachrichtigt. Liefert (abgesagte Termine, benachrichtigte Konten).
    /// </summary>
    public (int Termine, int Benachrichtigt) SprechtagAbsagen(Stand? s, Sprechtag t, Benutzer ich, string? grund)
    {
        if (!Rechtedienst.Darf(ich, Recht.SprechtagVerwalten)) throw new UnauthorizedAccessException("Nur die Administration kann den Sprechtag absagen.");
        if (t.Abgesagt.Length > 0) throw new ArgumentException("Der Sprechtag wurde bereits abgesagt.");
        if (IstVorbei(t)) throw new ArgumentException("Der Sprechtag ist bereits vorbei.");
        var g = Grund(grund);
        List<SprechtagTermin> termine;
        lock (_sperre)
        {
            termine = TermineVon(t);
            _db.Transaktion(c =>
            {
                Db.Ausfuehren(c, "UPDATE sprechtage SET abgesagt = @z, absage_grund = @g, geaendert = @z WHERE id = @id",
                    Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@g", g), Db.P("@id", t.Id));
                Db.Ausfuehren(c, "DELETE FROM sprechtag_termine WHERE sprechtag_id = @id", Db.P("@id", t.Id));
            });
        }
        if (!t.Sichtbar) return (termine.Count, 0);
        var zusatz = g.Length > 0 ? $"\n\nGrund: {g}" : "";
        var eltern = termine.SelectMany(ElternDesTermins).Concat(Einladungen(t.Id).SelectMany(e => ElternVon(e.SchuelerId).Select(b => b.Id))).ToHashSet();
        var lehrer = t.Lehrkraefte.SelectMany(l => KontenDerLehrkraft(l.Lehrer)).Select(b => b.Id).ToHashSet();
        var n = Melden(ich, eltern, $"Sprechtag abgesagt: {t.Name}",
            $"Der Sprechtag „{t.Name}“ am {DateOnly.Parse(t.Datum, Inv):dd.MM.yyyy} wurde abgesagt. Alle gebuchten Termine entfallen.{zusatz}");
        n += Melden(ich, lehrer, $"Sprechtag abgesagt: {t.Name}",
            $"Der Sprechtag „{t.Name}“ am {DateOnly.Parse(t.Datum, Inv):dd.MM.yyyy} wurde abgesagt; {termine.Count} gebuchte(r) Termin(e) entfallen.{zusatz}");
        return (termine.Count, n);
    }

    // ------------------------------------------------------------------------------------------ Einladungen

    public List<SprechtagEinladung> Einladungen(int id) => _db.Liste("SELECT * FROM sprechtag_einladungen WHERE sprechtag_id = @id ORDER BY id",
        r => new SprechtagEinladung(r.Zahl("id"), r.Zahl("sprechtag_id"), r.Text("lehrer"), r.Zahl("schueler_id"), r.Text("text"), r.Text("erstellt"), r.Zahl("benachrichtigt")),
        Db.P("@id", id));

    /// <summary>
    /// Einladungen versenden: je Schüler*in eine Einladung und eine Mitteilung an alle Elternkonten des Kindes (bereits eingeladene werden
    /// übersprungen). Liefert die Zahl neuer Einladungen, benachrichtigter Konten und der Kinder ohne Elternkonto.
    /// </summary>
    public (int Neu, int Benachrichtigt, List<string> OhneKonto) Einladen(Stand? s, Sprechtag t, Benutzer ich, string lehrer, IEnumerable<int> schuelerIds, string? text)
    {
        var l = Beteiligt(t, lehrer) ?? throw new ArgumentException("Die Lehrkraft ist an diesem Sprechtag nicht beteiligt.");
        if (!t.Sichtbar) throw new ArgumentException("Der Sprechtag ist noch nicht freigegeben.");
        if (t.Abgesagt.Length > 0 || l.Abgesagt.Length > 0) throw new ArgumentException("Die Teilnahme bzw. der Sprechtag wurde abgesagt.");
        if (_uhr.Jetzt > EinladungEnde(t)) throw new ArgumentException("Die Einladungsphase ist beendet.");
        var zusatz = (text ?? "").Trim();
        if (zusatz.Length > 1000) throw new ArgumentException("Der Text ist zu lang (höchstens 1000 Zeichen).");
        var ids = schuelerIds.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens eine Schülerin bzw. einen Schüler wählen.");
        if (ids.Count > 500) throw new ArgumentException("Bitte höchstens 500 Einladungen auf einmal versenden.");
        var kinder = ids.Select(i => Schueler(s, i)).ToList();
        var vorhanden = Einladungen(t.Id).Where(e => Ci.Equals(e.Lehrer, l.Lehrer)).Select(e => e.SchuelerId).ToHashSet();
        var lehrerName = LehrerName(s, l.Lehrer);
        var datum = DateOnly.Parse(t.Datum, Inv);
        var neu = 0;
        var benachrichtigt = 0;
        var ohne = new List<string>();
        foreach (var k in kinder.Where(k => !vorhanden.Contains(k.Id)))
        {
            var eltern = ElternVon(k.Id);
            var gesendet = 0;
            if (eltern.Count > 0)
            {
                var von = Zeitpunkt(t.AnmeldungVon);
                var bis = Zeitpunkt(t.AnmeldungBis);
                var nachricht = $"{lehrerName} lädt Sie zum Sprechtag „{t.Name}“ am {Plandienst.Wochentage[Plandienst.TagIndex(datum)]}, {datum:dd.MM.yyyy} zu einem Gespräch über " +
                    $"{k.Vorname} {k.Name} (Klasse {k.Klasse}) ein.\n\nBitte buchen Sie im Anmeldezeitraum vom {von:dd.MM.yyyy HH:mm} bis {bis:dd.MM.yyyy HH:mm} Uhr " +
                    "unter „Sprechtag“ einen freien Termin." + (Beteiligt(t, l.Lehrer)?.Raum is { Length: > 0 } raum ? $"\nRaum: {raum}" : "") + (zusatz.Length > 0 ? $"\n\n{zusatz}" : "");
                try
                {
                    _mitteilungen.Benachrichtigen(ich, eltern.Select(e => e.Id), $"Einladung zum Sprechtag: {k.Vorname} {k.Name}", nachricht);
                    gesendet = eltern.Count;
                }
                catch (ArgumentException) { gesendet = 0; }
            }
            if (gesendet == 0) ohne.Add($"{k.Vorname} {k.Name} ({k.Klasse})");
            try
            {
                _db.Ausfuehren("INSERT INTO sprechtag_einladungen (sprechtag_id, lehrer, schueler_id, text, erstellt, benachrichtigt) VALUES (@s, @l, @k, @t, @z, @b)",
                    Db.P("@s", t.Id), Db.P("@l", l.Lehrer), Db.P("@k", k.Id), Db.P("@t", zusatz), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)), Db.P("@b", gesendet));
                neu++;
                benachrichtigt += gesendet;
            }
            catch (DbException) { /* gleichzeitig eingeladen */ }
        }
        return (neu, benachrichtigt, ohne);
    }

    public void EinladungLoeschen(Sprechtag t, Benutzer ich, int einladungId)
    {
        var e = Einladungen(t.Id).FirstOrDefault(x => x.Id == einladungId) ?? throw new ArgumentException("Unbekannte Einladung.");
        if (!Rechtedienst.Darf(ich, Recht.SprechtagVerwalten) && !Ci.Equals(ich.LehrerKuerzel, e.Lehrer)) throw new UnauthorizedAccessException("Diese Einladung dürfen Sie nicht löschen.");
        _db.Ausfuehren("DELETE FROM sprechtag_einladungen WHERE id = @id", Db.P("@id", einladungId));
    }

    // ------------------------------------------------------------------------------------------ Lehrkräfte eines Kindes

    /// <summary>Lehrkraft eines Kindes am Sprechtag mit Fächern (aus dem Unterricht der Klasse, Klassenleitung, Einladungen).</summary>
    public sealed record KindLehrkraft(string Kurz, string Name, List<string> Faecher, bool Klassenleitung, string Raum);

    public List<KindLehrkraft> LehrkraefteDesKindes(Stand? s, Sprechtag t, WebSchueler kind)
    {
        var faecher = new Dictionary<string, SortedSet<string>>(Ci);
        void Dazu(string lehrer, string? fach)
        {
            if (string.IsNullOrWhiteSpace(lehrer)) return;
            if (!faecher.TryGetValue(lehrer, out var f)) faecher[lehrer] = f = new SortedSet<string>(Ci);
            if (!string.IsNullOrWhiteSpace(fach)) f.Add(fach);
        }
        var leitung = new List<string>();
        if (s != null)
        {
            foreach (var u in s.Unterricht.Values)
                foreach (var z in u.Zeilen)
                    if (z.Klassen.Contains(kind.Klasse, Ci)) Dazu(z.Lehrer, z.Fach);
            if (s.Klassen.TryGetValue(kind.Klasse, out var kl)) { leitung = kl.Klassenleitung; foreach (var l in leitung) Dazu(l, null); }
        }
        foreach (var e in Einladungen(t.Id).Where(e => e.SchuelerId == kind.Id)) Dazu(e.Lehrer, null);
        return t.Lehrkraefte.Where(l => faecher.ContainsKey(l.Lehrer))
            .Select(l => new KindLehrkraft(l.Lehrer, LehrerName(s, l.Lehrer), faecher[l.Lehrer].ToList(), leitung.Contains(l.Lehrer, Ci), l.Raum))
            .OrderByDescending(x => x.Klassenleitung).ThenBy(x => x.Name, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true)).ToList();
    }

    /// <summary>Unterrichtete Klassen einer Lehrkraft (für den Klassenfilter beim Einladen).</summary>
    public static List<string> KlassenVon(Stand? s, string lehrer)
    {
        if (s == null) return new();
        var res = s.Unterricht.Values.SelectMany(u => u.Zeilen).Where(z => Ci.Equals(z.Lehrer, lehrer)).SelectMany(z => z.Klassen).ToHashSet(Ci);
        var leitung = s.Klassen.Values.Where(k => k.Klassenleitung.Contains(lehrer, Ci)).Select(k => k.Kurz).ToHashSet(Ci);
        res.UnionWith(leitung);
        // eigene Klasse (Klassenleitung) zuerst – sie ist beim Einladen vorgewählt
        return res.OrderByDescending(leitung.Contains).ThenBy(x => x, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true)).ToList();
    }
}

/// <summary>Hintergrunddienst: Erinnerungen an Sprechtag-Termine (Option „Erinnerung … Stunden vorher“), alle 15 Minuten.</summary>
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
                var n = _dienst.ErinnerungenSenden(_paket.Aktuell);
                if (n > 0) _log.LogInformation("Sprechtag: an {Termine} Termine erinnert", n);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Sprechtag: Erinnerung fehlgeschlagen"); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
