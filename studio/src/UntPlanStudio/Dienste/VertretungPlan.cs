using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Api;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Dienste;

/// <summary>
/// Tagesaktuelle Daten der Vertretungsplanung für die Oberfläche: Kalenderwoche (Fenster „Kalender“), tagesaktueller Stundenplan
/// einer Woche (Stundenpläne im Vertretungsmodus) mit Zielstatus für Drag &amp; Drop, Bereitschaften des Tages.
/// </summary>
public static class VertretungPlan
{
    public static string TagKurz(DateOnly d) => Planungsdienst.TageKurz[((int)d.DayOfWeek + 6) % 7];

    /// <summary>Nächster (bzw. vorheriger) Unterrichtstag; mit <paramref name="woche"/> eine Woche weiter.</summary>
    public static DateOnly Blaettern(Schuldatei d, DateOnly datum, int richtung, bool woche)
    {
        richtung = richtung < 0 ? -1 : 1;
        if (!woche) return Kalender.NaechsterUnterrichtstag(d, datum, richtung);
        var ziel = datum.AddDays(7 * richtung);
        return Kalender.IstUnterrichtstag(d, ziel) ? ziel : Kalender.NaechsterUnterrichtstag(d, ziel, richtung);
    }

    /// <summary>Startdatum der Vertretungsplanung: heute bzw. der nächste Unterrichtstag.</summary>
    public static DateOnly Start(Schuldatei d)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        return Kalender.IstUnterrichtstag(d, heute) ? heute : Kalender.NaechsterUnterrichtstag(d, heute);
    }

    private static readonly string[] TagestextArten = { "Alle", "Klassen", "Lehrer", "Nur Lehrer" };

    /// <summary>Fenster „Kalender“: Woche mit Kennzahlen, Tagestexte und Kalendertag des Datums.</summary>
    public static object Kalenderdaten(Dokument doc, DateOnly datum)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var montag = Kalender.Montag(datum);
        var woche = new List<object>();
        for (var i = 0; i < 7; i++)
        {
            var t = montag.AddDays(i);
            var liste = d.Vertretungen.Where(v => v.Datum == t).ToList();
            int kollL = 0, kollR = 0;
            if (Kalender.IstUnterrichtstag(d, t) && liste.Count > 0)
            {
                var tp = Tagesplan.Berechnen(d, t);
                kollL = liste.Count(v => v.VertreterId is int l && !v.IstEntfall && !v.Mitbetreuung && v.Art != VertretungsArt.TrotzAbsenz && Vertretungen.LehrerBelegt(d, tp, l, v.Stunde, v) != null);
                kollR = tp.Eintraege.Where(e => e.Findetstatt).SelectMany(e => e.RaumIds.Select(r => (e.Stunde, r))).GroupBy(x => x).Count(g => g.Count() > 1);
            }
            woche.Add(new
            {
                datum = t, text = $"{t.Day}.{t.Month}.", tag = TagKurz(t), unterrichtstag = Kalender.IstUnterrichtstag(d, t),
                frei = Kalender.GanzerTagFrei(d, t) ? Kalender.FreiBezeichnung(d, t) : "",
                offen = liste.Count(v => v.Offen),
                abwKl = d.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && t >= a.Von && t <= a.Bis),
                freis = liste.Count(v => v.Art == VertretungsArt.Freisetzung),
                blRaeume = d.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && t >= a.Von && t <= a.Bis),
                raumV = liste.Count(v => v.Art == VertretungsArt.Raumvertretung),
                tagestexte = d.Tagestexte.Count(x => x.GiltAm(t)),
                kollL, kollR,
            });
        }
        var k = Kalender.KalendertagAm(d, datum);
        var art = k is null ? "normal" : k.Feiertag ? "feiertag" : k.Unterrichtsfrei ? "frei" : k.FreieStunden.Length > 0 ? "stunden" : "normal";
        return new
        {
            datum,
            titel = $"{Format.De.DateTimeFormat.GetDayName(datum.DayOfWeek)} {datum.Day}.{datum.Month}.{datum.Year}",
            kw = System.Globalization.ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue)),
            wochentyp = Kalender.WochenTyp(d, datum),
            unterrichtstag = Kalender.IstUnterrichtstag(d, datum),
            vorher = Kalender.NaechsterUnterrichtstag(d, datum, -1),
            naechster = Kalender.NaechsterUnterrichtstag(d, datum),
            woche,
            tagestexte = d.Tagestexte.Where(x => x.GiltAm(datum)).Select(x =>
            {
                var a = x.NurLehrer ? TagestextArt.NurLehrer : x.Art;
                var typ = a == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
                return new
                {
                    start = x.Datum, bis = x.Datum.AddDays(Math.Max(1, x.GueltigTage) - 1), text = x.Text, portal = x.Portal, art = TagestextArten[(int)a],
                    elemente = string.Join(",", x.ElementIds.Select(id => ix.K(typ, id))), formatiert = x.TextHtml.Length > 0,
                };
            }).ToList(),
            kalendertag = new { art, grund = k?.GrundId is int g ? ix.K(ElementTyp.Abwesenheitsgrund, g) : "", stunden = k?.FreieStunden ?? "", notiz = k?.Notiz ?? "" },
            arten = TagestextArten,
        };
    }

    public static TagestextArt TagestextArtVon(string? text) => (TagestextArt)Math.Max(0, Array.IndexOf(TagestextArten, text ?? "Alle"));

    // ---------------------------------------------------------------------------------------------
    // Tagesaktueller Stundenplan

    /// <summary>Gehört der Tageseintrag zum Plan des Elements? „durch“: das Element nimmt nicht (mehr) teil, Stunde wird durchgestrichen gezeigt.</summary>
    private static (bool Gehoert, bool Durch) Gehoert(TagesEintrag e, ElementTyp typ, int id)
    {
        var findet = e.Findetstatt;
        switch (typ)
        {
            case ElementTyp.Klasse:
                if (e.KlassenIds.Contains(id)) return (true, !findet);
                return (e.Regel?.KlassenIds.Contains(id) == true || e.Vertretung?.KlassenIds.Contains(id) == true, true);
            case ElementTyp.Lehrer:
                if (e.LehrerIds.Contains(id)) return (true, !findet);
                return (e.Regel?.LehrerId == id || e.Vertretung?.LehrerId == id, true);
            case ElementTyp.Raum:
                if (e.RaumIds.Contains(id)) return (true, !findet);
                return (e.Regel?.RaumId == id || e.Vertretung?.RaumId == id, true);
            case ElementTyp.Fach:
                return (e.FachId == id || e.Regel?.FachId == id, !findet || e.FachId != id);
            default:
                return (false, false);
        }
    }

    /// <summary>Tagesaktueller Plan der Woche des Datums für Klasse, Lehrkraft, Raum oder Fach.</summary>
    public static object Woche(Dokument doc, ElementTyp typ, int id, DateOnly datum)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var e0 = d.AlleElemente(typ).FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(typ)} Nr. {id}");
        var z = d.Zeitraster;
        var montag = Kalender.Montag(datum);
        var tage = new List<object>();
        var zellen = new List<object>();
        for (var t = 0; t < z.TageProWoche; t++)
        {
            var tagDatum = montag.AddDays((z.ErsterSchultag + t) % 7);
            var unterricht = Kalender.IstUnterrichtstag(d, tagDatum);
            tage.Add(new
            {
                nr = t, datum = tagDatum, kurz = Planungsdienst.TageKurz[(z.ErsterSchultag + t) % 7], text = $"{tagDatum.Day}.{tagDatum.Month}.",
                unterrichtstag = unterricht, frei = unterricht ? "" : Kalender.FreiBezeichnung(d, tagDatum), heute = tagDatum == datum,
            });
            if (!unterricht && !d.Vertretungen.Any(v => v.Datum == tagDatum)) continue;
            var tp = Tagesplan.Berechnen(d, tagDatum);
            foreach (var g in tp.Eintraege.GroupBy(e => e.Stunde).OrderBy(g => g.Key))
            {
                var liste = new List<object>();
                foreach (var e in g)
                {
                    var (gehoert, durch) = Gehoert(e, typ, id);
                    if (!gehoert) continue;
                    liste.Add(Eintrag(d, ix, typ, tp, e, durch));
                }
                if (liste.Count > 0) zellen.Add(new { tag = t, stunde = g.Key, datum = tagDatum, eintraege = liste });
            }
        }
        return new
        {
            element = new { id = e0.Id, typ = Typnamen.Schluessel(typ), kurz = e0.Kurzname, lang = e0 is Lehrer l ? l.VollerName : e0.Langname },
            montag, datum,
            woche = $"{montag:dd.MM.} – {montag.AddDays(z.TageProWoche - 1):dd.MM.yyyy} ({Kalender.WochenTyp(d, datum)}-Woche, {d.PeriodeAm(datum).Name})",
            zeitraster = DokumenteApi.Zeitraster(d),
            tage,
            zellen,
            vormerkungen = d.Vormerkungen.Count(v => v.Offen > 0 && (typ switch
            {
                ElementTyp.Klasse => v.KlassenIds.Contains(id),
                ElementTyp.Lehrer => v.LehrerIds.Contains(id),
                ElementTyp.Raum => v.RaumId == id,
                _ => v.FachId == id,
            })),
        };
    }

    private static object Eintrag(Schuldatei d, DatenIndex ix, ElementTyp typ, Tagesplan tp, TagesEintrag e, bool durch)
    {
        var v = e.Vertretung;
        var r = e.Regel;
        var uid = e.Unterricht?.Id ?? v?.UnterrichtId;
        var zeile = r?.ZeilenIndex ?? v?.ZeilenIndex ?? 0;
        var fach = e.FachId is int f ? ix.Faecher.GetValueOrDefault(f) : null;
        var lehrer = string.Join(",", e.LehrerIds.Select(ix.KLehrer));
        var raum = string.Join(",", e.RaumIds.Select(ix.KRaum));
        var klassen = ix.KKlassen(e.KlassenIds);
        // geänderte Felder (rot) gegenüber dem regulären Unterricht
        var lehrerAlt = r?.LehrerId is int rl && !e.LehrerIds.Contains(rl) ? ix.KLehrer(rl) : v?.LehrerId is int vl && !e.LehrerIds.Contains(vl) ? ix.KLehrer(vl) : "";
        var raumAlt = r?.RaumId is int rr && !e.RaumIds.Contains(rr) ? ix.KRaum(rr) : "";
        var fachAlt = r?.FachId is int rf && rf != e.FachId ? ix.KFach(rf) : "";
        var aufhebbar = new List<object>();
        if (uid is int u && e.Status == TagesStatus.Entfall)
        {
            int? zi = typ == ElementTyp.Lehrer ? zeile : null;
            if (Entfallaufhebung.Moeglich(d, u, zi, tp.Datum, e.Stunde))
            {
                var zeilen = Entfallaufhebung.Zeilen(d, u, zi, tp.Datum, e.Stunde);
                var offen = zeilen.Count(x => Entfallaufhebung.WirkungVon(d, x) == Entfallaufhebung.Wirkung.Offen);
                aufhebbar.Add(new
                {
                    u, zeile = zi, block = Entfallaufhebung.Block(d, u, zi, tp.Datum, e.Stunde),
                    hinweis = offen == 0 ? "Die Stunde findet wieder regulär statt." : "Die Lehrkraft ist abwesend – die Stunde wird wieder zur offenen Vertretung.",
                });
            }
        }
        var block = 1;
        if (uid is int ub)
        {
            // Länge des Blocks (gleicher Unterricht in den Folgestunden) für Drag & Drop
            while (tp.Stunde(e.Stunde + block).Any(x => x.Findetstatt && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == ub && (typ != ElementTyp.Lehrer || (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex ?? 0) == zeile))) block++;
        }
        return new
        {
            u = uid, zeile, status = e.Status.ToString(), durch,
            fach = ix.KFach(e.FachId), lehrer, klassen, raum, lehrerAlt, raumAlt, fachAlt,
            vtr = v?.Id, art = v is null ? "" : Vertretungsplan.ArtText(d, v.Art), text = v?.Text ?? e.Unterricht?.Text ?? "",
            offen = v?.OffenOderRaumOffen == true || e.Status == TagesStatus.Offen,
            farbe = StammdatenApi.Farbe(fach?.Hintergrund ?? 0), schrift = StammdatenApi.Farbe(fach?.Vordergrund ?? 0),
            fix = r?.Planstunde.Fixiert == true || v?.Fixiert == true,
            gekoppelt = e.Unterricht is { Zeilen.Count: > 1 },
            aufhebbar,
            block,
        };
    }

    /// <summary>Zielstatus beim Ziehen einer Stunde im tagesaktuellen Plan für alle Felder der Woche ([tag][stunde-1]).</summary>
    public static string[][] Zielstatus(Schuldatei d, DateOnly von, int vonStunde, int unterricht, int? zeile, int laenge, DateOnly woche)
    {
        var z = d.Zeitraster;
        var montag = Kalender.Montag(woche);
        var tpVon = Tagesplan.Berechnen(d, von);
        var res = new string[z.TageProWoche][];
        for (var t = 0; t < z.TageProWoche; t++)
        {
            var datum = montag.AddDays((z.ErsterSchultag + t) % 7);
            var tp = datum == von ? tpVon : Tagesplan.Berechnen(d, datum);
            res[t] = Enumerable.Range(1, z.StundenProTag).Select(s =>
                s + Math.Max(1, laenge) - 1 > z.StundenProTag ? "Kollision" : Tagesverlegung.Zielstatus(d, tpVon, vonStunde, unterricht, zeile, Math.Max(1, laenge), tp, s).ToString()).ToArray();
        }
        return res;
    }

    // ---------------------------------------------------------------------------------------------
    // Bereitschaften

    public static object Bereitschaften(Dokument doc, DateOnly datum)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var tp = Tagesplan.Berechnen(d, datum);
        var (zv, zb) = Zaehlung.Zeitraum(d, datum);
        var zeilen = tp.Regel.Where(r => Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.LehrerId.HasValue).OrderBy(r => r.Stunde).ThenBy(r => ix.KLehrer(r.LehrerId))
            .Select(e =>
            {
                var l = e.LehrerId!.Value;
                var einsatz = d.Vertretungen.Where(v => v.Datum == datum && v.Stunde == e.Stunde && v.VertreterId == l && v.LehrerId != l).ToList();
                var abwesend = Absenzen.LehrerAbwesend(d, l, datum, e.Stunde);
                return new
                {
                    stunde = e.Stunde, lehrer = ix.KLehrer(l), lehrerId = l, fach = ix.KFach(e.FachId),
                    eingesetzt = abwesend ? "(abwesend)" : string.Join("; ", einsatz.Select(v => $"{ix.KKlassen(v.NeueKlassenIds)} {ix.KFach(v.FachId)} ({ix.KLehrer(v.LehrerId)})")),
                    vertretungen = einsatz.Select(v => v.Id).ToList(),
                    zaehler = ix.Lehrer.TryGetValue(l, out var le) ? Zaehlung.Lehrer(d, le, zv, zb).Summe.ToString("0.#", Format.De) : "",
                    status = abwesend ? "abwesend" : einsatz.Count > 0 ? "eingesetzt" : "frei",
                };
            }).ToList();
        return new { datum, titel = $"Bereitschaften – {Format.Datum(datum)}", zeilen };
    }
}
