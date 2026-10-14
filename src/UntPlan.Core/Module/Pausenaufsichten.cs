using System.Text.RegularExpressions;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Module;

/// <summary>Kriterium der Pausenaufsichts-Gewichtung.</summary>
public sealed record PaKriterium(string Schluessel, string Text, int Standard);

/// <summary>Bewertung eines Lehrers für eine Aufsicht (Lehrervorschlag).</summary>
public sealed class PaBewertung
{
    public Lehrer Lehrer { get; init; } = null!;
    public double Punkte { get; set; }
    public int Zaehler { get; set; }
    public int ProTag { get; set; }
    public int Minuten { get; set; }
    public int MaxMinuten { get; set; }
    public bool UnterrichtDavor { get; set; }
    public bool UnterrichtDanach { get; set; }
    public bool GangDavor { get; set; }
    public bool GangDanach { get; set; }
    public bool VorErsterStunde { get; set; }
    public bool NachLetzterStunde { get; set; }
    public bool Folge { get; set; }
    public bool VorSperrung { get; set; }
    public bool NachSperrung { get; set; }
    public bool HohlstundeDavor { get; set; }
    public bool HohlstundeDanach { get; set; }
    public bool Doppelstundenpause { get; set; }
    public bool Bereitschaft { get; set; }
    /// <summary>Ein "extrem wichtiges" Kriterium ist verletzt.</summary>
    public bool Schlecht { get; set; }
    public List<string> Verletzt { get; } = new();
}

/// <summary>
/// Modul Pausenaufsichten: Aufsichtsbereiche (Gänge) × Pausen × Tage, geteilte Aufsichten, Minuten, Gewichtung,
/// Lehrervorschlag, Optimierung (nur offene Aufsichten) und Pausenaufsichtsvertretung.
/// </summary>
public static class Pausenaufsichten
{
    public static readonly IReadOnlyList<PaKriterium> Kriterien = new List<PaKriterium>
    {
        new("PA.MaxMinuten", "Max. Minuten des Lehrers nicht überschreiten", 5),
        new("PA.UntVor", "Vor der Pause Unterricht", 3),
        new("PA.UntNach", "Nach der Pause Unterricht", 3),
        new("PA.GangVor", "Vor der Pause Unterricht im Gang", 2),
        new("PA.GangNach", "Nach der Pause Unterricht im Gang", 2),
        new("PA.NichtVorErster", "Nicht vor 1. Stunde am Tag", 3),
        new("PA.NichtNachLetzter", "Nicht nach letzter Stunde am Tag", 3),
        new("PA.Doppelstunde", "Keine Aufsicht in Doppelstundenpausen", 2),
        new("PA.Folge", "Keine aufeinanderfolgenden Aufsichten", 3),
        new("PA.VorSperrung", "Nicht vor Sperrung", 4),
        new("PA.NachSperrung", "Nicht nach Sperrung", 4),
        new("PA.MaxProTag", "Max. Pausenaufsichten/Tag einhalten", 4),
        new("PA.HohlVor", "Hohlstunde VOR der Aufsicht (bevorzugt)", 1),
        new("PA.HohlNach", "Hohlstunde NACH der Aufsicht (bevorzugt)", 1),
    };

    public static int Gewicht(Schuldatei d, string key) => d.Gewichtung.TryGetValue(key, out var g) ? g : Kriterien.FirstOrDefault(k => k.Schluessel == key)?.Standard ?? 0;

    public static int MaxProTag(Schuldatei d) => d.Gewichtung.TryGetValue("PA.MaxProTagAnzahl", out var m) ? m : 1;

    public static IEnumerable<Pause> AufsichtsPausen(Schuldatei d) => d.Zeitraster.Pausen.Where(p => p.Aufsicht).OrderBy(p => p.NachStunde);

    /// <summary>Bezeichnung einer Pause: Pausenbezeichnung oder "1/2" (zwischen den Stunden).</summary>
    public static string PausenName(Schuldatei d, int nachStunde)
    {
        var p = d.Zeitraster.Pausen.FirstOrDefault(x => x.NachStunde == nachStunde);
        if (p != null && !string.IsNullOrWhiteSpace(p.Bezeichnung)) return p.Bezeichnung;
        return nachStunde == 0 ? "0/1" : nachStunde >= d.Zeitraster.StundenProTag ? $"{nachStunde}/" : $"{nachStunde}/{nachStunde + 1}";
    }

    /// <summary>Dauer der Pause in Minuten (Zeitraster: Ende Stunde n bis Beginn Stunde n+1).</summary>
    public static int PausenMinuten(Schuldatei d, int nachStunde)
    {
        var p = d.Zeitraster.Pausen.FirstOrDefault(x => x.NachStunde == nachStunde);
        var a = d.Zeitraster.Zeit(nachStunde);
        var b = d.Zeitraster.Zeit(nachStunde + 1);
        if (a != null && b != null && b.Beginn > a.Ende) return (int)(b.Beginn - a.Ende).TotalMinutes;
        return p?.DauerMinuten > 0 ? p.DauerMinuten : 15;
    }

    /// <summary>Anzahl Aufsichtsteile (0 = keine Aufsicht). Ohne Eintrag gilt der Bedarf des Ganges in allen Aufsichtspausen.</summary>
    public static int Teile(Schuldatei d, int gangId, int tag, int nachStunde)
    {
        var b = d.Aufsichtsbedarf.FirstOrDefault(x => x.GangId == gangId && x.Tag == tag && x.NachStunde == nachStunde);
        if (b != null) return b.Teile;
        if (d.Aufsichtsbedarf.Any(x => x.GangId == gangId)) return 0;
        return AufsichtsPausen(d).Any(p => p.NachStunde == nachStunde) ? d.Gaenge.FirstOrDefault(g => g.Id == gangId)?.Bedarf ?? 1 : 0;
    }

    /// <summary>Anzahl Teile einer Aufsicht setzen (0 = keine Aufsicht, 2 = geteilte Aufsicht). Überzählige Einteilungen werden entfernt.</summary>
    public static void TeileSetzen(Schuldatei d, int gangId, int tag, int nachStunde, int teile)
    {
        // beim ersten individuellen Eintrag den bisherigen Standard für alle Pausen des Gangs festhalten
        if (!d.Aufsichtsbedarf.Any(x => x.GangId == gangId))
            for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
                foreach (var p in AufsichtsPausen(d))
                    d.Aufsichtsbedarf.Add(new Aufsichtsbedarf { GangId = gangId, Tag = t, NachStunde = p.NachStunde, Teile = Teile(d, gangId, t, p.NachStunde) });
        var b = d.Aufsichtsbedarf.FirstOrDefault(x => x.GangId == gangId && x.Tag == tag && x.NachStunde == nachStunde);
        if (b is null) d.Aufsichtsbedarf.Add(b = new Aufsichtsbedarf { GangId = gangId, Tag = tag, NachStunde = nachStunde });
        b.Teile = Math.Clamp(teile, 0, 9);
        d.Pausenaufsichten.RemoveAll(a => a.GangId == gangId && a.Tag == tag && a.NachStunde == nachStunde && a.Datum is null && a.Teil > b.Teile);
    }

    public static int Minuten(Schuldatei d, int gangId, int tag, int nachStunde)
    {
        var b = d.Aufsichtsbedarf.FirstOrDefault(x => x.GangId == gangId && x.Tag == tag && x.NachStunde == nachStunde);
        var min = b?.Minuten ?? PausenMinuten(d, nachStunde);
        var teile = Math.Max(1, Teile(d, gangId, tag, nachStunde));
        return min / teile;
    }

    /// <summary>Verplante Aufsichtsminuten eines Lehrers pro Woche.</summary>
    public static int IstMinuten(Schuldatei d, int lehrerId) =>
        d.Pausenaufsichten.Where(a => a.LehrerId == lehrerId && a.Datum is null).Sum(a => Minuten(d, a.GangId, a.Tag, a.NachStunde));

    public static int Anzahl(Schuldatei d, int lehrerId) => d.Pausenaufsichten.Count(a => a.LehrerId == lehrerId && a.Datum is null);

    /// <summary>Raum gehört zu einem Gang: Gang des Raumes oder Musterliste "Gänge" (Platzhalter * und ?).</summary>
    public static bool RaumImGang(Raum r, Gang g)
    {
        if (r.GangId == g.Id) return true;
        foreach (var muster in (r.Gaenge ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()))
        {
            var re = "^" + Regex.Escape(muster).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            if (Regex.IsMatch(g.Kurzname, re, RegexOptions.IgnoreCase)) return true;
        }
        return false;
    }

    private static List<(int Stunde, Unterricht U, int? Raum, int? Fach)> StundenAmTag(Schuldatei d, DatenIndex ix, int lehrerId, int tag)
    {
        var res = new List<(int, Unterricht, int?, int?)>();
        foreach (var p in d.Stundenplan.Where(p => p.Tag == tag))
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) || u.Ignorieren) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
                if (u.Zeilen[i].LehrerId == lehrerId && !(u.Zeilen[i].FachId is int f && ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft))
                    res.Add((p.Stunde, u, p.RaumFuerZeile(i), u.Zeilen[i].FachId));
        }
        return res;
    }

    /// <summary>Kann der Lehrer die Aufsicht grundsätzlich übernehmen (PA Max &gt; 0, keine Sperre, Geschlecht)?</summary>
    public static bool Moeglich(Schuldatei d, DatenIndex ix, Lehrer l, int tag, int nachStunde, Gang? gang = null)
    {
        if (l.KeineAufsicht || l.Ignorieren || l.PausenaufsichtMaxMinuten <= 0) return false;
        if (l.Zeitwuensche.Gesperrt(tag, nachStunde) && l.Zeitwuensche.Gesperrt(tag, nachStunde + 1)) return false;
        if (gang != null && gang.NurMaennlich && !gang.NurWeiblich && l.Geschlecht is "w" or "W") return false;
        if (gang != null && gang.NurWeiblich && !gang.NurMaennlich && l.Geschlecht is "m" or "M") return false;
        var std = StundenAmTag(d, ix, l.Id, tag);
        // Fächer mit Kennzeichen (P): keine Aufsicht davor/danach
        if (std.Any(s => (s.Stunde == nachStunde || s.Stunde == nachStunde + 1) && s.Fach is int f && ix.Faecher.TryGetValue(f, out var fa) && fa.KeinePausenaufsicht)) return false;
        return std.Count > 0;
    }

    /// <summary>Bewertet einen Lehrer für eine Aufsicht nach der Gewichtung (Schlechtpunkte = Summe der Gewichte verletzter Kriterien).</summary>
    public static PaBewertung Bewerten(Schuldatei d, DatenIndex ix, Lehrer l, int gangId, int tag, int nachStunde, Pausenaufsicht? ausser = null)
    {
        var b = new PaBewertung { Lehrer = l, MaxMinuten = l.PausenaufsichtMaxMinuten };
        var std = StundenAmTag(d, ix, l.Id, tag);
        var stunden = std.Select(s => s.Stunde).Distinct().ToList();
        var gang = d.Gaenge.FirstOrDefault(g => g.Id == gangId);
        var eigene = d.Pausenaufsichten.Where(a => a.LehrerId == l.Id && a.Datum is null && a != ausser).ToList();
        b.Minuten = eigene.Sum(a => Minuten(d, a.GangId, a.Tag, a.NachStunde));
        b.ProTag = eigene.Count(a => a.Tag == tag);
        b.Zaehler = d.Vertretungen.Count(v => v.VertreterId == l.Id && v.Art == VertretungsArt.Pausenaufsicht);
        b.UnterrichtDavor = stunden.Contains(nachStunde);
        b.UnterrichtDanach = stunden.Contains(nachStunde + 1);
        b.GangDavor = gang != null && std.Any(s => s.Stunde == nachStunde && s.Raum is int r && ix.Raeume.TryGetValue(r, out var ra) && RaumImGang(ra, gang));
        b.GangDanach = gang != null && std.Any(s => s.Stunde == nachStunde + 1 && s.Raum is int r && ix.Raeume.TryGetValue(r, out var ra) && RaumImGang(ra, gang));
        b.VorErsterStunde = stunden.Count > 0 && nachStunde < stunden.Min();
        b.NachLetzterStunde = stunden.Count > 0 && nachStunde >= stunden.Max();
        b.Folge = eigene.Any(a => a.Tag == tag && a.NachStunde != nachStunde && Math.Abs(AufsichtsPausen(d).Select(p => p.NachStunde).ToList().IndexOf(a.NachStunde) - AufsichtsPausen(d).Select(p => p.NachStunde).ToList().IndexOf(nachStunde)) == 1);
        b.VorSperrung = l.Zeitwuensche.Get(tag, nachStunde + 1) <= -3;
        b.NachSperrung = l.Zeitwuensche.Get(tag, nachStunde) <= -3;
        b.HohlstundeDavor = !b.UnterrichtDavor && stunden.Any(s => s < nachStunde);
        b.HohlstundeDanach = !b.UnterrichtDanach && stunden.Any(s => s > nachStunde + 1);
        b.Doppelstundenpause = std.Any(a => a.Stunde == nachStunde && std.Any(c => c.Stunde == nachStunde + 1 && c.U.Id == a.U.Id));
        var minuten = Minuten(d, gangId, tag, nachStunde);
        void Pruefen(string key, bool verletzt)
        {
            if (!verletzt) return;
            var g = Gewicht(d, key);
            if (g <= 0) return;
            b.Punkte += Gewichtung.Faktor(g);
            b.Verletzt.Add(Kriterien.First(k => k.Schluessel == key).Text);
            if (g >= 5) b.Schlecht = true;
        }
        Pruefen("PA.MaxMinuten", b.Minuten + minuten > l.PausenaufsichtMaxMinuten);
        Pruefen("PA.UntVor", !b.UnterrichtDavor);
        Pruefen("PA.UntNach", !b.UnterrichtDanach);
        Pruefen("PA.GangVor", !b.GangDavor);
        Pruefen("PA.GangNach", !b.GangDanach);
        Pruefen("PA.NichtVorErster", b.VorErsterStunde);
        Pruefen("PA.NichtNachLetzter", b.NachLetzterStunde);
        Pruefen("PA.Doppelstunde", b.Doppelstundenpause);
        Pruefen("PA.Folge", b.Folge);
        Pruefen("PA.VorSperrung", b.VorSperrung);
        Pruefen("PA.NachSperrung", b.NachSperrung);
        Pruefen("PA.MaxProTag", b.ProTag + 1 > MaxProTag(d));
        Pruefen("PA.HohlVor", !b.HohlstundeDavor);
        Pruefen("PA.HohlNach", !b.HohlstundeDanach);
        return b;
    }

    /// <summary>Lehrervorschlag für eine Aufsicht (sortiert nach Schlechtpunkten).</summary>
    public static List<PaBewertung> Lehrervorschlag(Schuldatei d, int gangId, int tag, int nachStunde, Pausenaufsicht? aktuelle = null)
    {
        var ix = new DatenIndex(d);
        var gang = d.Gaenge.FirstOrDefault(g => g.Id == gangId);
        var belegt = d.Pausenaufsichten.Where(a => a.Tag == tag && a.NachStunde == nachStunde && a.Datum is null && a != aktuelle).Select(a => a.LehrerId).ToHashSet();
        return d.Lehrer.Where(l => !belegt.Contains(l.Id) && Moeglich(d, ix, l, tag, nachStunde, gang))
            .Select(l => Bewerten(d, ix, l, gangId, tag, nachStunde, aktuelle)).OrderBy(b => b.Punkte).ThenBy(b => b.Minuten).ToList();
    }

    /// <summary>
    /// Optimierung: besetzt nur offene Aufsichten mit dem jeweils besten Lehrer (wenigste Schlechtpunkte).
    /// Optional werden zuerst schlechte Einteilungen gelöscht. Lange Pausen werden zuerst verplant.
    /// </summary>
    public static int Optimieren(Schuldatei d, bool schlechteLoeschen = false, int? gangId = null, int vonPause = 0, int bisPause = 0, int kuerzestePause = 0)
    {
        var ix = new DatenIndex(d);
        if (schlechteLoeschen)
            d.Pausenaufsichten.RemoveAll(a => a.Datum is null && (gangId is null || a.GangId == gangId) && ix.Lehrer.TryGetValue(a.LehrerId, out var l) &&
                                              Bewerten(d, ix, l, a.GangId, a.Tag, a.NachStunde, a).Schlecht);
        var neu = 0;
        var offen = new List<(int Gang, int Tag, int Nach, int Teil, int Min)>();
        foreach (var g in d.Gaenge.Where(g => gangId is null || g.Id == gangId))
            for (var tag = 0; tag < d.Zeitraster.TageProWoche; tag++)
                for (var nach = 0; nach <= d.Zeitraster.StundenProTag; nach++)
                {
                    if (vonPause > 0 && nach < vonPause || bisPause > 0 && nach > bisPause) continue;
                    var teile = Teile(d, g.Id, tag, nach);
                    if (teile <= 0 || PausenMinuten(d, nach) < kuerzestePause) continue;
                    for (var t = 1; t <= teile; t++)
                        if (!d.Pausenaufsichten.Any(a => a.GangId == g.Id && a.Tag == tag && a.NachStunde == nach && a.Teil == t && a.Datum is null))
                            offen.Add((g.Id, tag, nach, t, PausenMinuten(d, nach)));
                }
        foreach (var o in offen.OrderByDescending(o => o.Min).ThenBy(o => o.Tag).ThenBy(o => o.Nach))
        {
            var best = Lehrervorschlag(d, o.Gang, o.Tag, o.Nach).FirstOrDefault(b => !b.Schlecht);
            if (best is null) continue;
            d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = o.Gang, Tag = o.Tag, NachStunde = o.Nach, LehrerId = best.Lehrer.Id, Teil = o.Teil });
            neu++;
        }
        return neu;
    }

    /// <summary>Offene Pausenaufsichtsvertretungen eines Datums.</summary>
    public static List<Pausenaufsicht> OffeneVertretungen(Schuldatei d, DateOnly datum) =>
        d.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht && v.VertreterId is null)
            .Select(v => new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 }).ToList();

    /// <summary>Vertretungskandidaten für eine Pausenaufsicht an einem Datum (anwesend, nicht selbst eingeteilt, gereiht nach Bewertung).</summary>
    public static List<Lehrer> Kandidaten(Schuldatei d, DateOnly datum, Pausenaufsicht a)
    {
        var ix = new DatenIndex(d);
        var periode = d.PeriodeAm(datum);
        var belegt = (periode.Pausenaufsichten ?? new()).Where(x => x.Tag == a.Tag && x.NachStunde == a.NachStunde).Select(x => x.LehrerId).ToHashSet();
        belegt.UnionWith(d.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht && v.Stunde == a.NachStunde && v.VertreterId.HasValue).Select(v => v.VertreterId!.Value));
        var gang = d.Gaenge.FirstOrDefault(g => g.Id == a.GangId);
        return d.Lehrer.Where(l => !belegt.Contains(l.Id) && !Absenzen.LehrerAbwesend(d, l.Id, datum, a.NachStunde) && !Absenzen.LehrerAbwesend(d, l.Id, datum, a.NachStunde + 1)
                                   && Moeglich(d, ix, l, a.Tag, a.NachStunde, gang))
            .Select(l => (l, b: Bewerten(d, ix, l, a.GangId, a.Tag, a.NachStunde)))
            .OrderBy(x => x.b.Punkte).ThenBy(x => Vertretungen.Zaehler(d, x.l.Id, Kalender.Montag(datum), Kalender.Montag(datum).AddDays(6))).Select(x => x.l).ToList();
    }
}
