using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Vertretungszeile(string Klassen, string Stunde, int StundeVon, string Fach, string? FachAlt, string Lehrer, string? LehrerAlt,
    string Raum, string? RaumAlt, string Art, string Text, string VertrVon, string Nach, string Status, bool OhneLehrer = false);

public sealed record Vertretungstag(string Datum, string Wochentag, string Woche, bool Frei, string? FreiGrund, List<string> Nachrichten,
    List<string> AbwesendeKlassen, List<string> AbwesendeLehrer, List<string> BlockierteRaeume, List<string> Veranstaltungen, List<Vertretungszeile> Zeilen);

/// <summary>Nachricht zum Tag (in UntPlanWeb gepflegt, ergänzt die Tagestexte aus UntPlan).</summary>
public sealed record Nachricht(int Id, string Von, string Bis, string Text, string Zielgruppe, bool Startseite, bool Monitor);

/// <summary>Vertretungsplan je Tag in Schüler- bzw. Lehrerversion (Liste, Monitor, Startseite).</summary>
public sealed class Vertretungsplandienst
{
    private readonly Plandienst _plan;
    private readonly Db _db;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    public Vertretungsplandienst(Plandienst plan, Db db) { _plan = plan; _db = db; }

    // -------------------------------------------------------------- Nachrichten zum Tag

    public List<Nachricht> Nachrichten() => _db.Liste("SELECT * FROM nachrichten ORDER BY von DESC, id DESC",
        r => new Nachricht(r.Zahl("id"), r.Text("von"), r.Text("bis"), r.Text("text"), r.Text("zielgruppe"), r.Wahr("startseite"), r.Wahr("monitor")));

    public void NachrichtSpeichern(int? id, DateOnly von, DateOnly bis, string text, string zielgruppe, bool startseite, bool monitor, int benutzer)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (bis < von) throw new ArgumentException("Das Ende liegt vor dem Beginn.");
        if (zielgruppe is not ("alle" or "lehrer" or "schueler")) throw new ArgumentException("Unbekannte Zielgruppe.");
        var p = new[] { Db.P("@v", von), Db.P("@b", bis), Db.P("@t", text.Trim()), Db.P("@z", zielgruppe), Db.P("@s", startseite), Db.P("@m", monitor), Db.P("@u", benutzer), Db.P("@id", id) };
        if (id is null) _db.Ausfuehren("INSERT INTO nachrichten (von, bis, text, zielgruppe, startseite, monitor, erstellt, benutzer_id) VALUES (@v, @b, @t, @z, @s, @m, datetime('now'), @u)", p);
        else _db.Ausfuehren("UPDATE nachrichten SET von = @v, bis = @b, text = @t, zielgruppe = @z, startseite = @s, monitor = @m WHERE id = @id", p);
    }

    public void NachrichtLoeschen(int id) => _db.Ausfuehren("DELETE FROM nachrichten WHERE id = @id", Db.P("@id", id));

    /// <summary>Nachrichten eines Tages: Tagestexte aus UntPlan und Nachrichten aus UntPlanWeb, gefiltert nach Version und Ort.</summary>
    public List<string> NachrichtenAm(Stand? s, DateOnly d, bool kollegium, string ort)
    {
        var res = new List<string>();
        if (s != null)
            foreach (var t in s.Paket.Tagestexte)
            {
                if (!DateOnly.TryParse(t.Datum, out var ab) || d < ab || d >= ab.AddDays(Math.Max(1, t.GueltigTage))) continue;
                if (!kollegium && t.Fuer is "lehrer" or "kollegium") continue;
                res.Add(t.Text);
            }
        var iso = Plandienst.Iso(d);
        foreach (var n in _db.Liste("SELECT text, zielgruppe, startseite, monitor FROM nachrichten WHERE von <= @d AND bis >= @d ORDER BY id",
                     r => new { Text = r.Text("text"), Ziel = r.Text("zielgruppe"), Start = r.Wahr("startseite"), Mon = r.Wahr("monitor") }, Db.P("@d", iso)))
        {
            if (ort == "monitor" && !n.Mon) continue;
            if (ort == "startseite" && !n.Start) continue;
            if (n.Ziel == "lehrer" && !kollegium) continue;
            res.Add(n.Text);
        }
        return res.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
    }

    // -------------------------------------------------------------- Vertretungsplan

    public Vertretungstag Tag(Stand s, DateOnly d, bool kollegium, string ort = "liste")
    {
        var (frei, grund) = _plan.Frei(s, d);
        var iso = Plandienst.Iso(d);
        string Std(WebAbsenz a) => a.StundeBis >= 20 || a.StundeBis < a.StundeVon ? "" : $" ({a.StundeVon}–{a.StundeBis})";
        var absenzen = s.Paket.Absenzen.Where(a => DateOnly.TryParse(a.Von, out var v) && DateOnly.TryParse(a.Bis, out var b) && d >= v && d <= b).ToList();
        var klassen = absenzen.Where(a => a.Art == "klasse").Select(a => a.Element + Std(a)).Distinct().ToList();
        var lehrer = kollegium ? absenzen.Where(a => a.Art == "lehrer").Select(a => a.Element + Std(a)).Distinct().ToList() : new List<string>();
        var raeume = absenzen.Where(a => a.Art == "raum").Select(a => a.Element + Std(a)).Distinct().ToList();

        // Veranstaltungen als eigene Zeile oben; Teilnahmezeilen einzelner Lehrkräfte (ohne Klassen) entfallen in der Liste
        var veranstaltungen = s.Paket.Termine.Where(t => t.Art != "klausur" && DateOnly.TryParse(t.Von, out var v) && DateOnly.TryParse(t.Bis, out var b) && d >= v && d <= b
                                                         && (kollegium || t.Klassen.Count > 0 || (t.Lehrer.Count == 0 && t.Raeume.Count == 0)))
            .Select(t =>
            {
                var von = DateOnly.Parse(t.Von) == d ? t.StundeVon : 1;
                var bis = DateOnly.Parse(t.Bis) == d ? t.StundeBis : 99;
                var std = von <= 1 && bis >= 20 ? "ganztägig" : bis >= 20 ? $"ab {von}. Std." : von == bis ? $"{von}. Std." : $"{von}.–{bis}. Std.";
                var wer = t.Klassen.Count > 0 ? string.Join(", ", t.Klassen) : t.Lehrer.Count > 0 && kollegium ? (t.Lehrer.Count > 8 ? $"{t.Lehrer.Count} Lehrkräfte" : string.Join(", ", t.Lehrer)) : "";
                return $"{t.Titel} ({std}{(wer.Length > 0 ? ", " + wer : "")}{(t.Raeume.Count > 0 ? ", " + string.Join(", ", t.Raeume) : "")})";
            }).ToList();

        // Schülerversion: nur Einträge mit Klassenbezug, keine Bereitschaften oder Aufsichten
        var einzeln = s.Paket.Vertretungen.Where(v => v.Datum == iso && !(v.Klassen.Count == 0 && v.Art.StartsWith("Veranst", StringComparison.OrdinalIgnoreCase))
                                                      && (kollegium || (!v.NurLehrer && v.Klassen.Count > 0 && !IstLehrerintern(v)))).Select(v =>
        {
            var status = v.Art switch
            {
                var a when a.StartsWith("Entfall", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Freisetzung", StringComparison.OrdinalIgnoreCase) => "entfall",
                _ when v.Vertreter is "???" => "offen",
                var a when a.StartsWith("Klausur", StringComparison.OrdinalIgnoreCase) => "klausur",
                _ => "geaendert",
            };
            // Vertretung ohne Lehrkraft („+++“): keine neue Lehrkraft, die bisherige steht durchgestrichen
            var ohneLehrer = v.Vertreter is "+" or "+++";
            var neuLehrer = v.Vertreter is "---" || ohneLehrer ? "" : v.Vertreter;
            var lehrerAlt = v.Lehrer.Length > 0 && !Ci.Equals(v.Lehrer, neuLehrer) ? v.Lehrer : null;
            var neuRaum = v.NeuerRaum;
            var raumAlt = v.Raum.Length > 0 && !Ci.Equals(v.Raum, neuRaum) ? v.Raum : null;
            var fachAlt = v.Fach.Length > 0 && v.NeuesFach.Length > 0 && !Ci.Equals(v.Fach, v.NeuesFach) ? v.Fach : null;
            // Schülerversion: keine Angaben, wer fehlt – nur, wer vertritt
            if (!kollegium && status != "entfall" && !ohneLehrer) lehrerAlt = null;
            return new Vertretungszeile(string.Join(", ", v.Klassen), v.Stunde.ToString(), v.Stunde, v.NeuesFach.Length > 0 ? v.NeuesFach : v.Fach, fachAlt,
                neuLehrer, lehrerAlt, neuRaum, raumAlt, v.Art, ohneLehrer && string.IsNullOrWhiteSpace(v.Text) ? Plandienst.OhneLehrkraftText : v.Text, v.VertrVon, v.Nach, status, ohneLehrer);
        }).ToList();

        // gleiche Zeilen in aufeinanderfolgenden Stunden zusammenfassen (z. B. „3–4“)
        var zeilen = new List<Vertretungszeile>();
        foreach (var z in einzeln.OrderBy(x => x.Klassen, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true)).ThenBy(x => x.StundeVon))
        {
            var letzte = zeilen.LastOrDefault();
            if (letzte != null && letzte with { Stunde = "", StundeVon = 0 } == z with { Stunde = "", StundeVon = 0 } && Bis(letzte) + 1 == z.StundeVon)
            {
                zeilen[^1] = letzte with { Stunde = $"{letzte.StundeVon}–{z.StundeVon}" };
                continue;
            }
            zeilen.Add(z);
        }
        return new Vertretungstag(iso, Plandienst.Wochentage[Plandienst.TagIndex(d)], _plan.Woche(s, d), frei, grund, NachrichtenAm(s, d, kollegium, ort),
            klassen, lehrer, raeume, veranstaltungen, zeilen);
    }

    private static bool IstLehrerintern(WebVertretung v) =>
        v.Fach.Equals("BER", StringComparison.OrdinalIgnoreCase) || v.Art.Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase) ||
        v.Art.Contains("Aufsicht", StringComparison.OrdinalIgnoreCase);

    private static int Bis(Vertretungszeile z) => z.Stunde.Contains('–') && int.TryParse(z.Stunde.Split('–')[1], out var b) ? b : z.StundeVon;

    /// <summary>Die nächsten n Unterrichtstage ab einem Datum (für Monitor und Liste).</summary>
    public List<DateOnly> NaechsteTage(Stand s, DateOnly ab, int anzahl)
    {
        var res = new List<DateOnly>();
        for (var d = ab; res.Count < anzahl && d < ab.AddDays(40); d = d.AddDays(1))
            if (!_plan.Frei(s, d).Frei) res.Add(d);
        return res;
    }
}
