using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Antwort auf die Frage „Lehrer X hat am … in der N. Stunde bereits Unterricht“.</summary>
public enum LehrerKonfliktLoesung
{
    /// <summary>Lehrer nicht verplanen.</summary>
    NichtVerplanen,
    /// <summary>Lehrer hält sowohl Unterricht als auch Vertretung (Betreuung).</summary>
    Betreuung,
    /// <summary>Lehrer im regulären Unterricht vertreten lassen (legt dort eine offene Vertretung an).</summary>
    RegulaerVertreten,
}

/// <summary>Neue Werte einer Stunde im Dialog „Stunde ändern“.</summary>
public sealed record StundeAendernWerte(int? LehrerId, int? FachId, IReadOnlyList<int> KlassenIds, int? RaumId, string Schuelergruppe = "", string Text = "");

/// <summary>
/// Sondereinsatz / Stunde ändern im tagesaktuellen Stundenplan. Je nach geändertem Element:
/// <list type="bullet">
/// <item>Klasse: der Unterricht entfällt für die bisherige Klasse (und kollidierender Unterricht der neuen Klasse), zusätzlich entsteht „Unterricht geändert“.</item>
/// <item>Lehrer ohne Unterricht: Sondereinsatz (bei abwesender Lehrkraft: Vertretung).</item>
/// <item>Lehrer mit Unterricht: Rückfrage – nicht verplanen, Betreuung oder regulären Unterricht vertreten lassen.</item>
/// <item>Fach: „Unterricht geändert“.</item>
/// <item>Raum: Raumvertretung.</item>
/// </list>
/// </summary>
public static class Stundenaenderung
{
    /// <summary>Eintrag, in dem die Lehrkraft zur Stunde bereits unterrichtet (außer dem Unterricht selbst), sonst null.</summary>
    public static TagesEintrag? LehrerKonflikt(Schuldatei d, DateOnly datum, int stunde, int lehrerId, int? unterrichtId)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        return tp.Stunde(stunde).FirstOrDefault(e => e.Findetstatt && e.LehrerIds.Contains(lehrerId) && (unterrichtId is null || (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) != unterrichtId) &&
                                                     !(e.Status == TagesStatus.Regulaer && Vertretungen.IstBereitschaftsfach(d, e.FachId)) && e.Vertretung?.Mitbetreuung != true);
    }

    /// <summary>Text der Rückfrage: „Lehrer Gauss hat am 8.11. in der 2. Stunde bereits Unterricht“ und „Unt. 1: 3a, Gauss, Mat, R3a“.</summary>
    public static string KonfliktText(Schuldatei d, int lehrerId, DateOnly datum, int stunde, TagesEintrag e)
    {
        var ix = new DatenIndex(d);
        var unt = $"Unt. {e.Unterricht?.Id.ToString() ?? "V" + e.Vertretung?.Id}: " + string.Join(", ", new[]
        {
            ix.KKlassen(e.KlassenIds), string.Join(",", e.LehrerIds.Select(ix.KLehrer)), ix.KFach(e.FachId), string.Join(",", e.RaumIds.Select(ix.KRaum)),
        }.Where(x => x.Length > 0));
        return $"Lehrer {ix.KLehrer(lehrerId)} hat am {datum.Day}.{datum.Month}. in der {stunde}. Stunde bereits Unterricht\n\n{unt}";
    }

    private static void Markieren(Vertretung v, string benutzer)
    {
        v.Bearbeitet = true;
        v.Geaendert = DateTime.Now;
        if (!string.IsNullOrEmpty(benutzer)) v.Benutzer = benutzer;
    }

    /// <summary>Der bisherige Unterricht des Lehrers wird zur offenen Vertretung („im regulären Unterricht vertreten lassen“).</summary>
    private static void RegulaerOffen(Schuldatei d, DateOnly datum, int stunde, TagesEintrag konflikt, string benutzer)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        var e = tp.Stunde(stunde).FirstOrDefault(x => x.Findetstatt && (x.Unterricht?.Id ?? -1) == konflikt.Unterricht?.Id && x.Regel?.ZeilenIndex == konflikt.Regel?.ZeilenIndex) ?? konflikt;
        var v = e.Vertretung;
        if (v is null)
        {
            if (e.Regel is not { } r) return;
            v = new Vertretung
            {
                Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = r.Unterricht.Id, ZeilenIndex = r.ZeilenIndex, LehrerId = r.LehrerId, FachId = r.FachId,
                KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId, Quelle = VertretungsQuelle.Sondereinsatz, Schuelergruppe = r.Unterricht.Schuelergruppe,
            };
            d.Vertretungen.Add(v);
        }
        v.Art = v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung ? v.Art : VertretungsArt.Vertretung;
        v.VertreterId = null;
        v.Mitbetreuung = false;
        v.Mehrarbeit = false;
        if (string.IsNullOrEmpty(v.Text)) v.Text = "wegen Sondereinsatz";
        Markieren(v, benutzer);
    }

    /// <summary>
    /// Stunde(n) eines Unterrichts ändern. Liefert die neuen bzw. geänderten Vertretungszeilen. Hat die neue Lehrkraft
    /// bereits Unterricht, entscheidet <paramref name="konflikt"/>.
    /// </summary>
    public static List<Vertretung> Ausfuehren(Schuldatei d, int unterrichtId, int zeile, DateOnly datum, IEnumerable<int> stunden, StundeAendernWerte neu,
        LehrerKonfliktLoesung konflikt = LehrerKonfliktLoesung.NichtVerplanen, string benutzer = "")
    {
        var res = new List<Vertretung>();
        foreach (var s in stunden)
        {
            var tp = Tagesplan.Berechnen(d, datum);
            var e = tp.Stunde(s).FirstOrDefault(x => x.Findetstatt && Raumvertretungen.Gehoert(x, unterrichtId, zeile));
            if (e is null) continue;
            var lehrerAlt = e.LehrerIds.Count > 0 ? e.LehrerIds[0] : (int?)null;
            var raumAlt = e.RaumIds.Count > 0 ? e.RaumIds[0] : (int?)null;
            var klassenNeu = !new HashSet<int>(neu.KlassenIds).SetEquals(e.KlassenIds) && neu.KlassenIds.Count > 0;
            var fachNeu = neu.FachId != e.FachId;
            var raumNeu = neu.RaumId != raumAlt;
            var lehrerNeu = neu.LehrerId != lehrerAlt && neu.LehrerId != null;
            // Lehrkraft hat bereits Unterricht?
            var betreuung = false;
            if (lehrerNeu && LehrerKonflikt(d, datum, s, neu.LehrerId!.Value, unterrichtId) is { } k)
            {
                switch (konflikt)
                {
                    case LehrerKonfliktLoesung.NichtVerplanen: lehrerNeu = false; break;
                    case LehrerKonfliktLoesung.Betreuung: betreuung = true; break;
                    default: RegulaerOffen(d, datum, s, k, benutzer); break;
                }
                tp = Tagesplan.Berechnen(d, datum);
                e = tp.Stunde(s).First(x => x.Findetstatt && Raumvertretungen.Gehoert(x, unterrichtId, zeile));
            }
            var lehrer = lehrerNeu ? neu.LehrerId : lehrerAlt;
            if (!klassenNeu && !fachNeu && !raumNeu && !lehrerNeu)
            {
                if (neu.Text.Length == 0 && neu.Schuelergruppe == (e.Vertretung?.Schuelergruppe ?? e.Unterricht?.Schuelergruppe ?? "")) continue;
                var t = Raumvertretungen.ZeileFuer(d, tp, e, benutzer);
                if (t.Art == VertretungsArt.Raumvertretung && t.VertretungsraumId is null && t.Quelle == VertretungsQuelle.Manuell) { t.Art = VertretungsArt.UnterrichtGeaendert; t.VertretungsraumId = raumAlt; }
                t.Text = neu.Text;
                t.Schuelergruppe = neu.Schuelergruppe;
                Markieren(t, benutzer);
                res.Add(t);
                continue;
            }
            var v = Raumvertretungen.ZeileFuer(d, tp, e, benutzer);
            var lehrerAbwesend = v.Offen || (v.LehrerId is int la && Absenzen.LehrerAbwesend(d, la, datum, s));
            if (klassenNeu)
            {
                // kollidierender Unterricht der neuen Klassen entfällt
                foreach (var kid in neu.KlassenIds.Except(e.KlassenIds))
                    foreach (var x in tp.Stunde(s).Where(x => x.Findetstatt && x != e && x.KlassenIds.Contains(kid) && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) is int).ToList())
                        Vertretungen.Entfallen(d, (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId)!.Value, x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex, datum, s, benutzer);
                // für die bisherige Klasse entfällt der Unterricht ...
                v.Art = VertretungsArt.Entfall;
                v.VertreterId = null;
                v.VertretungsraumId = null;
                v.Mehrarbeit = false;
                v.Mitbetreuung = false;
                Markieren(v, benutzer);
                res.Add(v);
                // ... und es entsteht „Unterricht geändert“
                var g = new Vertretung
                {
                    Id = d.NeueId(), Datum = datum, Stunde = s, Art = VertretungsArt.UnterrichtGeaendert, Quelle = VertretungsQuelle.Manuell, UnterrichtId = unterrichtId,
                    ZeilenIndex = zeile, LehrerId = v.LehrerId, VertreterId = lehrer, FachId = v.FachId, VertretungsfachId = neu.FachId, KlassenIds = e.KlassenIds.ToList(),
                    VertretungsKlassenIds = neu.KlassenIds.ToList(), RaumId = v.RaumId, VertretungsraumId = neu.RaumId, Text = neu.Text, Schuelergruppe = neu.Schuelergruppe,
                    Mitbetreuung = betreuung, Mehrarbeit = lehrerNeu && !betreuung, Bearbeitet = true, Benutzer = benutzer,
                };
                if (neu.RaumId is null) g.RaumVerdraengt = true;
                d.Vertretungen.Add(g);
                res.Add(g);
                continue;
            }
            if (lehrerNeu)
            {
                if (lehrerAbwesend || betreuung)
                {
                    if (v.Art == VertretungsArt.Raumvertretung) v.Art = VertretungsArt.Vertretung;
                    Vertretungen.VertreterSetzen(d, v, lehrer, betreuung, benutzer);
                }
                else
                {
                    v.Art = VertretungsArt.Sondereinsatz;
                    v.VertreterId = lehrer;
                    v.Mitbetreuung = false;
                    v.Mehrarbeit = lehrer is int l && !Vertretungen.HatBereitschaft(d, tp, l, s);
                }
            }
            if (fachNeu)
            {
                v.VertretungsfachId = neu.FachId;
                if (v.Art is VertretungsArt.Raumvertretung) v.Art = VertretungsArt.UnterrichtGeaendert;
            }
            if (raumNeu)
            {
                v.VertretungsraumId = neu.RaumId;
                v.RaumVerdraengt = false;
                if (neu.RaumId is null && v.Art != VertretungsArt.Raumvertretung) v.RaumVerdraengt = true;
            }
            else if (v.Art == VertretungsArt.UnterrichtGeaendert && v.VertretungsraumId is null) v.VertretungsraumId = raumAlt;
            if (!lehrerNeu && v.VertreterId is null && !v.Offen) v.VertreterId = lehrerAlt;
            if (neu.Text.Length > 0) v.Text = neu.Text;
            v.Schuelergruppe = neu.Schuelergruppe;
            Markieren(v, benutzer);
            res.Add(v);
        }
        return res;
    }

    /// <summary>„Neuer Lehrer“: zusätzliche Lehrkraft für die Stunde(n) als Sondereinsatz (ohne den bestehenden Unterricht zu ändern).</summary>
    public static List<Vertretung> ZusaetzlicherLehrer(Schuldatei d, DateOnly datum, IEnumerable<int> stunden, int lehrerId, IReadOnlyList<int> klassenIds, int? fachId, int? raumId,
        string text, string schuelergruppe, LehrerKonfliktLoesung konflikt, string benutzer = "")
    {
        var res = new List<Vertretung>();
        foreach (var s in stunden)
        {
            var betreuung = false;
            if (LehrerKonflikt(d, datum, s, lehrerId, null) is { } k)
            {
                if (konflikt == LehrerKonfliktLoesung.NichtVerplanen) continue;
                if (konflikt == LehrerKonfliktLoesung.Betreuung) betreuung = true;
                else RegulaerOffen(d, datum, s, k, benutzer);
            }
            var tp = Tagesplan.Berechnen(d, datum);
            var v = new Vertretung
            {
                Id = d.NeueId(), Datum = datum, Stunde = s, Art = VertretungsArt.Sondereinsatz, Quelle = VertretungsQuelle.Sondereinsatz, VertreterId = lehrerId,
                FachId = fachId, VertretungsfachId = fachId, KlassenIds = klassenIds.ToList(), VertretungsraumId = raumId, Text = text, Schuelergruppe = schuelergruppe,
                Mitbetreuung = betreuung, Mehrarbeit = !betreuung && !Vertretungen.HatBereitschaft(d, tp, lehrerId, s), Bearbeitet = true, Benutzer = benutzer,
            };
            d.Vertretungen.Add(v);
            res.Add(v);
        }
        return res;
    }

    /// <summary>Lehrkräfte, die zur Stunde verfügbar sind (Option „Nur verfügbare Lehrer“).</summary>
    public static List<Lehrer> VerfuegbareLehrer(Schuldatei d, DateOnly datum, IReadOnlyList<int> stunden)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        return Stammdaten.Sortiert(d.Lehrer).Where(l => !l.Ignorieren && stunden.All(s => Vertretungen.LehrerBelegt(d, tp, l.Id, s) is null)).ToList();
    }

    /// <summary>Fächer des Lehrers (Lehrbefähigung und unterrichtete Fächer) – Option „Nur Fächer des Lehrers“.</summary>
    public static List<Fach> FaecherDesLehrers(Schuldatei d, int lehrerId)
    {
        var l = d.Lehrer.FirstOrDefault(x => x.Id == lehrerId);
        var ids = (l?.FaecherIds ?? new()).Concat(d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.LehrerId == lehrerId && z.FachId.HasValue).Select(z => z.FachId!.Value)).ToHashSet();
        return Stammdaten.Sortiert(d.Faecher).Where(f => ids.Contains(f.Id)).ToList();
    }

    /// <summary>Räume, die in allen Stunden frei und nicht gesperrt sind (Option „Nur freie Räume“).</summary>
    public static List<Raum> FreieRaeume(Schuldatei d, DateOnly datum, IReadOnlyList<int> stunden)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        return Stammdaten.Sortiert(d.Raeume).Where(r => !r.Ignorieren && stunden.All(s => !Absenzen.RaumGesperrt(d, r.Id, datum, s) && !tp.Stunde(s).Any(e => e.Findetstatt && e.RaumIds.Contains(r.Id)))).ToList();
    }
}
