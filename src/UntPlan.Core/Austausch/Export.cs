using System.Net;
using System.Text;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Austausch;

/// <summary>Exporte: GPU-Austauschformat, CSV-Listen, HTML (Info-Stundenplan, Vertretungsplan, Monitor), iCalendar.</summary>
public static class Export
{
    public static Dictionary<string, string> Gpu(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        string Z(IEnumerable<IEnumerable<object?>> rows) => Csv.Schreiben(rows, ';', false);
        var g002 = new List<object?[]>();
        foreach (var u in d.Unterrichte)
            foreach (var z in u.Zeilen)
                foreach (var k in z.KlassenIds.DefaultIfEmpty(0))
                    g002.Add(new object?[] { u.Id, u.Wochenstunden, "", "", k == 0 ? "" : ix.K(ElementTyp.Klasse, k), ix.KLehrer(z.LehrerId), ix.KFach(z.FachId), ix.KRaum(z.FachraumId ?? z.StammraumId) });
        var g001 = new List<object?[]>();
        foreach (var p in d.Stundenplan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
                foreach (var k in u.Zeilen[i].KlassenIds.DefaultIfEmpty(0))
                    g001.Add(new object?[] { u.Id, k == 0 ? "" : ix.K(ElementTyp.Klasse, k), ix.KLehrer(u.Zeilen[i].LehrerId), ix.KFach(u.Zeilen[i].FachId), ix.KRaum(p.RaumFuerZeile(i)), p.Tag + 1, p.Stunde, "" });
        }
        return new Dictionary<string, string>
        {
            ["GPU001.TXT"] = Z(g001),
            ["GPU002.TXT"] = Z(g002),
            ["GPU003.TXT"] = Z(d.Klassen.Select(k => new object?[] { k.Kurzname, k.Langname })),
            ["GPU004.TXT"] = Z(d.Lehrer.Select(l => new object?[] { l.Kurzname, l.Langname })),
            ["GPU005.TXT"] = Z(d.Raeume.Select(r => new object?[] { r.Kurzname, r.Langname, ix.KRaum(r.AusweichraumId), r.Kapazitaet })),
            ["GPU006.TXT"] = Z(d.Faecher.Select(f => new object?[] { f.Kurzname, f.Langname })),
        };
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Stil = "<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;margin:12px;color:#111}h1{font-size:18px;margin:4px 0}h2{font-size:15px}" +
        "table{border-collapse:collapse;margin-bottom:14px}th,td{border:1px solid #9aa4b2;padding:3px 5px;vertical-align:top}th{background:#dfe6f0}" +
        ".tt td{width:120px;height:44px}.f{font-weight:bold}.k{font-size:11px;color:#333}.e{color:#b00;font-weight:bold}.sub{color:#555}" +
        "nav a{margin-right:8px}@media print{nav{display:none}.pg{page-break-after:always}}</style>";

    public static string Seite(string titel, string body) =>
        $"<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{H(titel)}</title>{Stil}</head><body>{body}</body></html>";

    /// <summary>HTML-Stundenplan eines Elements (Klasse, Lehrer, Raum).</summary>
    public static string StundenplanHtml(Schuldatei d, ElementTyp typ, int id)
    {
        var ix = new DatenIndex(d);
        var el = ix.Element(typ, id);
        var plan = ix.PlanVon(typ, id).ToList();
        var sb = new StringBuilder();
        sb.Append($"<div class=\"pg\"><h1>{H(el?.Kurzname)} – {H(el?.Langname)}</h1><div class=\"sub\">{H(d.Einstellungen.Schulname)} · Schuljahr {Format.Datum(d.Einstellungen.SchuljahrVon)} – {Format.Datum(d.Einstellungen.SchuljahrBis)}</div>");
        sb.Append("<table class=\"tt\"><tr><th></th>");
        for (var t = 0; t < d.Zeitraster.TageProWoche; t++) sb.Append($"<th>{Planungsdienst.Tagname(d, t, true)}</th>");
        sb.Append("</tr>");
        for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
        {
            var z = d.Zeitraster.Zeit(s);
            sb.Append($"<tr><th>{s}<br><span class=\"k\">{z?.Beginn:HH\\:mm}-{z?.Ende:HH\\:mm}</span></th>");
            for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
            {
                sb.Append("<td>");
                foreach (var p in plan.Where(p => p.Tag == t && p.Stunde == s))
                {
                    var u = ix.Unterrichte[p.UnterrichtId];
                    var f = u.HauptfachId is int fid ? ix.Faecher.GetValueOrDefault(fid) : null;
                    var farbe = f is { Hintergrund: not 0 } ? $"#{f.Hintergrund & 0xFFFFFF:X6}" : "#eef";
                    var info = typ switch
                    {
                        ElementTyp.Klasse => $"{string.Join(",", u.LehrerIds.Select(ix.KLehrer))} {string.Join(",", p.Raeume.Where(r => r.HasValue).Select(r => ix.KRaum(r)))}",
                        ElementTyp.Lehrer => $"{ix.KKlassen(u.KlassenIds)} {string.Join(",", p.Raeume.Where(r => r.HasValue).Select(r => ix.KRaum(r)))}",
                        _ => $"{ix.KKlassen(u.KlassenIds)} {string.Join(",", u.LehrerIds.Select(ix.KLehrer))}",
                    };
                    sb.Append($"<div style=\"background:{farbe};padding:2px\"><span class=\"f\">{H(f?.Kurzname)}</span> <span class=\"k\">{H(info)}</span></div>");
                }
                sb.Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table></div>");
        return sb.ToString();
    }

    /// <summary>Info-Stundenplan: HTML-Dateien für alle Klassen, Lehrer und Räume samt Übersicht.</summary>
    public static Dictionary<string, string> InfoStundenplan(Schuldatei d)
    {
        var res = new Dictionary<string, string>();
        var nav = new StringBuilder("<nav><a href=\"index.html\">Übersicht</a></nav>");
        var index = new StringBuilder($"<h1>{H(d.Einstellungen.Schulname)} – Stundenpläne</h1>");
        void Gruppe(string titel, ElementTyp typ, IEnumerable<Element> el, string praefix)
        {
            index.Append($"<h2>{titel}</h2><p>");
            foreach (var e in el)
            {
                var datei = $"{praefix}_{Datei(e.Kurzname)}.html";
                res[datei] = Seite($"{titel} {e.Kurzname}", nav + StundenplanHtml(d, typ, e.Id));
                index.Append($"<a href=\"{datei}\">{H(e.Kurzname)}</a> ");
            }
            index.Append("</p>");
        }
        Gruppe("Klassen", ElementTyp.Klasse, d.Klassen, "kl");
        Gruppe("Lehrer", ElementTyp.Lehrer, d.Lehrer, "le");
        Gruppe("Räume", ElementTyp.Raum, d.Raeume, "ra");
        res["index.html"] = Seite("Stundenpläne", index.ToString());
        return res;
    }

    private static string Datei(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    public static string VertretungsplanHtml(Schuldatei d, IEnumerable<DateOnly> tage, bool lehrerversion, bool monitor = false)
    {
        var ix = new DatenIndex(d);
        var sb = new StringBuilder();
        if (monitor) sb.Append("<meta http-equiv=\"refresh\" content=\"60\"><style>body{background:#0b1a33;color:#fff;font-size:20px}th{background:#1d3a66;color:#fff}td{border-color:#2c4a7a}.e{color:#ff9a9a}</style>");
        foreach (var datum in tage)
        {
            sb.Append($"<div class=\"pg\"><h1>{H(d.Einstellungen.Vertretungsplantitel)} – {Planungsdienst.TageLang[((int)datum.DayOfWeek + 6) % 7]}, {Format.Datum(datum)}</h1>");
            sb.Append($"<div class=\"sub\">{H(d.Einstellungen.Schulname)} · Stand {DateTime.Now:dd.MM.yyyy HH:mm} · {Kalender.WochenTyp(d, datum)}-Woche</div>");
            foreach (var t in d.Tagestexte.Where(t => t.Datum == datum && (lehrerversion || !t.NurLehrer))) sb.Append($"<p><b>{H(t.Text)}</b></p>");
            if (lehrerversion)
            {
                var abw = d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && datum >= a.Von && datum <= a.Bis).Select(a => ix.KLehrer(a.ElementId)).Distinct();
                sb.Append($"<p class=\"sub\">Abwesende Lehrer: {H(string.Join(", ", abw))}</p>");
            }
            var abwK = d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Klasse && datum >= a.Von && datum <= a.Bis).Select(a => ix.K(ElementTyp.Klasse, a.ElementId)).Distinct();
            if (abwK.Any()) sb.Append($"<p class=\"sub\">Abwesende Klassen: {H(string.Join(", ", abwK))}</p>");
            sb.Append($"<table><tr><th>Klasse(n)</th><th>Std.</th>{(lehrerversion ? "<th>(Lehrer)</th>" : "")}<th>Vertreter</th><th>Fach</th><th>Raum</th><th>Art</th><th>Text</th></tr>");
            var zeilen = Vertretungsplan.Zeilen(d, datum).Where(z => z.Art != "Pausenaufsicht" || lehrerversion).ToList();
            foreach (var z in zeilen)
                sb.Append($"<tr><td>{H(z.Klassen)}</td><td>{z.Stunde}</td>{(lehrerversion ? $"<td>{H(z.Lehrer)}</td>" : "")}<td>{H(z.Vertreter)}</td><td>{H(z.NeuesFach)}</td><td>{H(z.NeuerRaum)}</td>" +
                    $"<td class=\"{(z.Art is "Entfall" or "Freisetzung" ? "e" : "")}\">{H(z.Art)}</td><td>{H(z.Text)}</td></tr>");
            if (zeilen.Count == 0) sb.Append("<tr><td colspan=\"8\">Keine Vertretungen.</td></tr>");
            sb.Append("</table></div>");
        }
        return Seite(d.Einstellungen.Vertretungsplantitel, sb.ToString());
    }

    public static string ICal(Schuldatei d, int lehrerId, DateOnly von, DateOnly bis)
    {
        var ix = new DatenIndex(d);
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//UntPlan//DE\r\n");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        foreach (var datum in Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var tp = Tagesplan.Berechnen(d, datum);
            foreach (var e in tp.VonLehrer(lehrerId).Where(e => e.Status != TagesStatus.Zusaetzlich && e.Vertretung is null))
                Ereignis(datum, e.Stunde, $"{ix.KFach(e.FachId)} {ix.KKlassen(e.KlassenIds)}", ix.KRaum(e.RaumIds.Cast<int?>().FirstOrDefault()));
            foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.VertreterId == lehrerId && !v.IstEntfall && v.Art != VertretungsArt.Pausenaufsicht))
                Ereignis(datum, v.Stunde, $"{Vertretungsplan.ArtText(v.Art)} {ix.KFach(v.VertretungsfachId ?? v.FachId)} {ix.KKlassen(v.KlassenIds)}", ix.KRaum(v.VertretungsraumId ?? v.RaumId));
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();

        void Ereignis(DateOnly datum, int stunde, string titel, string ort)
        {
            var z = d.Zeitraster.Zeit(stunde);
            if (z is null) return;
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{datum:yyyyMMdd}-{stunde}-{lehrerId}-{titel.GetHashCode():X}@untplan\r\nDTSTAMP:{stamp}\r\n");
            sb.Append($"DTSTART;TZID=Europe/Berlin:{datum:yyyyMMdd}T{z.Beginn:HHmm}00\r\nDTEND;TZID=Europe/Berlin:{datum:yyyyMMdd}T{z.Ende:HHmm}00\r\n");
            sb.Append($"SUMMARY:{titel}\r\n");
            if (ort.Length > 0) sb.Append($"LOCATION:{ort}\r\n");
            sb.Append("END:VEVENT\r\n");
        }
    }
}
