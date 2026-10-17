using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Bewertung eines Vertretungsereignisses aus Sicht eines Lehrers (Vertretungsabrechnung).</summary>
public sealed record ZaehlZeile(Vertretung? Vertretung, DateOnly Datum, int Stunde, string Code, string Fach, string Klassen, string Grund, string Text,
    decimal Wert, bool Zaehlt, bool Bezahlt, bool Mehrarbeit, string Art);

public sealed class ZaehlSumme
{
    public decimal Summe { get; set; }
    public int Vertretungen { get; set; }
    public int VertretungenNichtZuZaehlen { get; set; }
    public int Entfaelle { get; set; }
    public int EntfaelleNichtZuZaehlen { get; set; }
    public int Freisetzungen { get; set; }
    public int Betreuungen { get; set; }
    public int Sondereinsaetze { get; set; }
    public int Bezahlt { get; set; }
    public int Pausenaufsichten { get; set; }
    public int Mehrarbeit { get; set; }
    public int BereitschaftenGenutzt { get; set; }
    public List<ZaehlZeile> Zeilen { get; } = new();
}

/// <summary>
/// Vertretungszähler: Vertretungen und Sondereinsätze erhöhen, Entfälle und Freisetzungen vermindern den Zähler –
/// gesteuert über Absenzgründe ("Entfall zählen", "Freisetzung nicht zählen"), Fächer und die Zählereinstellungen.
/// </summary>
public static class Zaehlung
{
    /// <summary>Durchrechnungszeitraum laut Einstellungen um ein Datum.</summary>
    public static (DateOnly Von, DateOnly Bis) Zeitraum(Schuldatei d, DateOnly datum)
    {
        var z = d.Vertretungseinstellungen.Zaehler;
        switch (z.Zeitraum)
        {
            case ZaehlZeitraum.Woche:
                var m = Kalender.Montag(datum);
                return (m, m.AddDays(6));
            case ZaehlZeitraum.Monat:
                var a = new DateOnly(datum.Year, datum.Month, 1);
                return (a, a.AddMonths(1).AddDays(-1));
            case ZaehlZeitraum.Halbjahr:
                var sv = d.Einstellungen.SchuljahrVon;
                var mitte = new DateOnly(sv.Year + 1, 2, 1);
                return datum < mitte ? (sv, mitte.AddDays(-1)) : (mitte, d.Einstellungen.SchuljahrBis);
            case ZaehlZeitraum.Benutzerdefiniert when z.ZeitraumVon.HasValue && z.ZeitraumBis.HasValue:
                return (z.ZeitraumVon.Value, z.ZeitraumBis.Value);
            default:
                return (d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis);
        }
    }

    private static decimal StundenWert(Schuldatei d, Vertretung v)
    {
        if (v.Wert.HasValue) return v.Wert.Value;
        if (d.Vertretungseinstellungen.Zaehler.WieZaehlen != ZaehlArt.Werte) return 1;
        var u = v.UnterrichtId is int uid ? d.PeriodeAm(v.Datum).Unterrichte?.FirstOrDefault(x => x.Id == uid) : null;
        if (u?.WerteFaktor is decimal wf) return wf;
        return v.FachId is int f && d.Faecher.FirstOrDefault(x => x.Id == f) is { } fach ? fach.WerteFaktor : 1;
    }

    /// <summary>Alle Zählzeilen eines Lehrers im Zeitraum (inkl. nicht zählender Ereignisse und Wertkorrekturen).</summary>
    public static ZaehlSumme Lehrer(Schuldatei d, Lehrer l, DateOnly von, DateOnly bis)
    {
        var res = new ZaehlSumme();
        var e = d.Vertretungseinstellungen.Zaehler;
        var ix = new DatenIndex(d);
        var gruende = d.Abwesenheitsgruende.ToDictionary(g => g.Id);
        var relevant = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis && (v.VertreterId == l.Id || v.LehrerId == l.Id)).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
        var tagesplaene = new Dictionary<DateOnly, Tagesplan>();
        Tagesplan Tp(DateOnly t) => tagesplaene.TryGetValue(t, out var x) ? x : tagesplaene[t] = Tagesplan.Berechnen(d, t);
        foreach (var v in relevant)
        {
            var nichtZaehlen = !string.IsNullOrEmpty(v.Statistik) && (v.Statistik == e.NichtZaehlenKennzeichen || v.Statistik == e.BezahltKennzeichen);
            var bezahlt = !string.IsNullOrEmpty(v.Statistik) && v.Statistik == e.BezahltKennzeichen;
            var grund = v.GrundId is int g ? gruende.GetValueOrDefault(g) : null;
            var fach = v.FachId is int fid ? ix.Faecher.GetValueOrDefault(fid) : null;
            var wert = StundenWert(d, v);
            var fachText = v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KFach(v.VertretungsfachId ?? v.FachId);
            void Zeile(string code, decimal w, bool zaehlt, bool mehrarbeit = false) =>
                res.Zeilen.Add(new ZaehlZeile(v, v.Datum, v.Stunde, code, fachText, ix.KKlassen(v.NeueKlassenIds), v.Grund, v.Text, zaehlt ? w : 0, zaehlt, bezahlt, mehrarbeit, Vertretungsplan.ArtText(v.Art)));

            if (v.VertreterId == l.Id && v.LehrerId != l.Id || (v.VertreterId == l.Id && v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.Klausur or VertretungsArt.Veranstaltung or VertretungsArt.SondereinsatzVormerkung))
            {
                switch (v.Art)
                {
                    case VertretungsArt.Verlegung or VertretungsArt.Tausch or VertretungsArt.Raumvertretung or VertretungsArt.UnterrichtGeaendert:
                        break;
                    case VertretungsArt.Betreuung:
                        res.Betreuungen++;
                        if (e.BetreuungenHalb) Zeile("+V", wert / 2, !nichtZaehlen);
                        break;
                    case VertretungsArt.Pausenaufsicht:
                        res.Pausenaufsichten++;
                        Zeile("P", 1, e.PausenaufsichtenZaehlen && !nichtZaehlen);
                        break;
                    case VertretungsArt.StattVertretung:
                        res.Vertretungen++;
                        Zeile("V", wert, !e.StattVertretungNeutral && !nichtZaehlen && e.VertretungenZaehlen, v.Mehrarbeit);
                        break;
                    case VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung:
                        res.Sondereinsaetze++;
                        Zeile("+Y", wert, e.SondereinsaetzeZaehlen && !nichtZaehlen, v.Mehrarbeit);
                        break;
                    case VertretungsArt.Klausur when v.LehrerId == l.Id:
                        // Prüfung im eigenen Unterricht (ersetzt ihn): weder Mehrleistung noch Entfall
                        Zeile("+K", wert, false);
                        break;
                    case VertretungsArt.Klausur:
                        var neutral = e.KlausurenNeutral && d.Vertretungen.Any(x => x.KlausurId == v.KlausurId && x.LehrerId == l.Id && x.Datum == v.Datum && x.Stunde == v.Stunde && x.IstEntfall);
                        Zeile("+K", wert, !neutral && !nichtZaehlen, v.Mehrarbeit);
                        break;
                    case VertretungsArt.Veranstaltung:
                        var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == v.VeranstaltungId);
                        Zeile("VA", v.Wert ?? 1, va?.Zaehlen == true && !nichtZaehlen);
                        break;
                    default:
                        var bereit = Vertretungen.HatBereitschaft(d, Tp(v.Datum), l.Id, v.Stunde) ||
                                     Tp(v.Datum).Regel.Any(r => r.Stunde == v.Stunde && r.LehrerId == l.Id && Vertretungen.IstBereitschaftsfach(d, r.FachId));
                        if (bereit)
                        {
                            res.BereitschaftenGenutzt++;
                            var imDeputat = Tp(v.Datum).Regel.Any(r => r.Stunde == v.Stunde && r.LehrerId == l.Id && r.FachId is int bf && ix.Faecher.TryGetValue(bf, out var bfa) && bfa.BereitschaftImDeputat);
                            Zeile("B", wert, !imDeputat && !nichtZaehlen && e.VertretungenZaehlen);
                        }
                        else Zeile("+V", wert, !nichtZaehlen && e.VertretungenZaehlen, v.Mehrarbeit);
                        res.Vertretungen++;
                        break;
                }
            }
            else if (v.LehrerId == l.Id && v.IstEntfall)
            {
                if (v.NachDatum.HasValue)
                {
                    Zeile("-E", wert, false); // verlegte Stunde zählt nicht
                    continue;
                }
                if (v.Art == VertretungsArt.Freisetzung)
                {
                    res.Freisetzungen++;
                    var statt = e.StattVertretungNeutral && d.Vertretungen.Any(x => x.Datum == v.Datum && x.Stunde == v.Stunde && x.VertreterId == l.Id && x.Art == VertretungsArt.StattVertretung);
                    // selbst abwesend: die Freisetzung ist keine Minderleistung
                    var selbstAbwesend = Absenzen.LehrerAbwesend(d, l.Id, v.Datum, v.Stunde);
                    Zeile("F", -wert, e.FreisetzungenZaehlen && grund?.FreisetzungNichtZaehlen != true && !statt && !selbstAbwesend && fach?.EntfallNichtZaehlen != true);
                    continue;
                }
                if (v.Art == VertretungsArt.BereitschaftEntfaellt) continue;
                var eigeneAbsenz = v.AbwesenheitId.HasValue || (v.Quelle == VertretungsQuelle.Veranstaltung && v.VeranstaltungId is int vid && d.Veranstaltungen.FirstOrDefault(x => x.Id == vid) is { } vx
                    // auch an diesem Tag zusätzlich eingetragene Lehrkräfte nehmen teil (kein gewöhnlicher Entfall)
                    && (vx.LehrerIds.Contains(l.Id) || vx.NimmtTeil(l.Id, v.Datum)));
                bool zaehlt;
                if (fach?.EntfallNichtZaehlen == true) zaehlt = false;
                else if (eigeneAbsenz) zaehlt = grund?.EntfallZaehlen == true || (grund is null && !e.EntfallNurMitGrund);
                else if (v.Quelle == VertretungsQuelle.Klausur) zaehlt = !e.KlausurenNeutral;
                else zaehlt = true;
                if (zaehlt) res.Entfaelle++; else res.EntfaelleNichtZuZaehlen++;
                Zeile("-E", -wert, zaehlt && e.EntfaelleZaehlen);
            }
        }
        foreach (var wk in l.Wertkorrekturen.Where(w => w.Datum >= von && w.Datum <= bis && w.Art is "V" or "Y" or "E"))
            res.Zeilen.Add(new ZaehlZeile(null, wk.Datum, 0, "WK", wk.Art, "", "", wk.Text, wk.Art == "E" ? -Math.Abs(wk.Wert) : Math.Abs(wk.Wert), true, false, false, "Wertkorrektur"));
        res.Zeilen.Sort((a, b) => a.Datum != b.Datum ? a.Datum.CompareTo(b.Datum) : a.Stunde.CompareTo(b.Stunde));
        res.Summe = res.Zeilen.Where(z => z.Zaehlt).Sum(z => z.Wert);
        res.VertretungenNichtZuZaehlen = res.Zeilen.Count(z => z.Wert == 0 && !z.Zaehlt && z.Code is "+V" or "V" or "B" or "+Y" or "+K" or "P" or "VA");
        res.Bezahlt = res.Zeilen.Count(z => z.Bezahlt);
        res.Mehrarbeit = res.Zeilen.Count(z => z.Mehrarbeit && !z.Bezahlt);
        return res;
    }
}

/// <summary>Vertretungsstatistik und Berichte (Vertretungsabrechnung, Entfalltage, Vertretungs-Übersicht, Mehrarbeit NRW, Monatsabrechnung).</summary>
public static class Statistik
{
    public sealed record LehrerZeile(Lehrer Lehrer, int Vertretungen, int Mehrarbeit, int Betreuungen, int Sondereinsaetze, int Aufsichten, int Entfaelle, int Freisetzungen, decimal Saldo, int Verguetungsfaehig);

    public static decimal Saldo(Schuldatei d, Lehrer l, DateOnly von, DateOnly bis) => Zaehlung.Lehrer(d, l, von, bis).Summe;

    /// <summary>Teilzeitquote (Wochensoll / Pflichtstunden der Schulform).</summary>
    public static decimal Teilzeitquote(Schuldatei d, Lehrer l)
    {
        var voll = Nrw.Pflichtstunden.GetValueOrDefault(d.Einstellungen.Schulform, 25.5m);
        if (voll <= 0 || l.Wochensoll <= 0) return 1;
        return Math.Clamp(l.Wochensoll / voll, 0.1m, 1m);
    }

    /// <summary>
    /// NRW (§ 61 LBG / MAVO): Mehrarbeit wird vergütet, wenn im Kalendermonat mehr als 3 Unterrichtsstunden (bei Teilzeit anteilig)
    /// geleistet wurden – dann ab der ersten Stunde. Ausgefallener Unterricht ohne eigene Absenz (Minderleistung) wird verrechnet.
    /// </summary>
    public sealed record MehrarbeitMonat(int Jahr, int Monat, int Geleistet, int Minder, int Saldo, decimal Grenze, int Verguetungsfaehig);

    public static List<MehrarbeitMonat> MehrarbeitNrw(Schuldatei d, Lehrer l, DateOnly von, DateOnly bis) => MehrarbeitNrw(d, l, Zaehlung.Lehrer(d, l, von, bis));

    /// <summary>Mehrarbeit NRW aus einer bereits berechneten Zählung (Abrechnung: nicht zweimal zählen).</summary>
    public static List<MehrarbeitMonat> MehrarbeitNrw(Schuldatei d, Lehrer l, ZaehlSumme z)
    {
        var grenze = Math.Round(d.Einstellungen.Mehrarbeitsgrenze * Teilzeitquote(d, l), 1);
        var res = new List<MehrarbeitMonat>();
        foreach (var m in z.Zeilen.GroupBy(x => (x.Datum.Year, x.Datum.Month)).OrderBy(g => g.Key))
        {
            var geleistet = m.Count(x => x.Mehrarbeit && !x.Bezahlt);
            var minder = m.Count(x => x.Code is "F" || (x.Code == "-E" && x.Zaehlt && x.Vertretung?.AbwesenheitId is null && x.Vertretung?.NachDatum is null));
            var saldo = geleistet - minder;
            res.Add(new MehrarbeitMonat(m.Key.Year, m.Key.Month, geleistet, minder, saldo, grenze, saldo > grenze ? saldo : 0));
        }
        return res;
    }

    public static List<LehrerZeile> Abrechnung(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var res = new List<LehrerZeile>();
        foreach (var l in d.Lehrer)
        {
            var z = Zaehlung.Lehrer(d, l, von, bis);
            if (z.Zeilen.Count == 0) continue;
            var verg = MehrarbeitNrw(d, l, z).Sum(m => m.Verguetungsfaehig);
            res.Add(new LehrerZeile(l, z.Vertretungen, z.Mehrarbeit, z.Betreuungen, z.Sondereinsaetze, z.Pausenaufsichten, z.Entfaelle, z.Freisetzungen, z.Summe, verg));
        }
        return res.OrderBy(r => r.Lehrer.Kurzname, StringComparer.Create(Format.De, true)).ToList();
    }

    public sealed record Ausfall(int Gesamt, int Vertreten, int Entfall, int Freisetzung, int Raum, int Verlegung, int Offen);

    public static Ausfall Zusammenfassung(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var l = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).ToList();
        return new Ausfall(l.Count,
            l.Count(v => v.VertreterId.HasValue && v.Art is VertretungsArt.Vertretung or VertretungsArt.Betreuung or VertretungsArt.StattVertretung or VertretungsArt.Teilvertretung or VertretungsArt.Lehrertausch or VertretungsArt.Bereitschaftsvertretung),
            l.Count(v => v.Art == VertretungsArt.Entfall && v.NachDatum is null), l.Count(v => v.Art == VertretungsArt.Freisetzung),
            l.Count(v => v.Art == VertretungsArt.Raumvertretung), l.Count(v => v.Art is VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch), l.Count(v => v.Offen));
    }

    /// <summary>Entfalltage: Tage, an denen aufgrund einer Absenz alle Stunden des Lehrers entfallen sind.</summary>
    public sealed record Entfalltag(Lehrer Lehrer, DateOnly Datum, int Stunden, string Grund);

    public static List<Entfalltag> Entfalltage(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var res = new List<Entfalltag>();
        foreach (var t in Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var regel = Tagesplan.Regulaer(d, t);
            foreach (var g in regel.Where(r => r.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId)).GroupBy(r => r.LehrerId!.Value))
            {
                var stunden = g.Select(r => r.Stunde).Distinct().ToList();
                if (!stunden.All(s => Absenzen.LehrerAbwesend(d, g.Key, t, s))) continue;
                var abw = Absenzen.Lehrer(d, g.Key, t, stunden[0]);
                var l = d.Lehrer.FirstOrDefault(x => x.Id == g.Key);
                if (l != null) res.Add(new Entfalltag(l, t, stunden.Count, Vertretungen.GrundText(d, abw?.GrundId)));
            }
        }
        return res;
    }

    /// <summary>Vertretungs-Übersicht: geplanter und gehaltener Unterricht der ganzen Schule.</summary>
    public sealed record Uebersicht(int Geplant, int GemaessPlan, int NichtErteilt, int Sondereinsaetze, Dictionary<string, int> NachGrund, Dictionary<string, int> NachArt, Dictionary<string, int> EntfallNachGrund);

    public static Uebersicht VertretungsUebersicht(Schuldatei d, DateOnly von, DateOnly bis)
    {
        int geplant = 0, gehalten = 0, nicht = 0;
        var nachGrund = new Dictionary<string, int>();
        var nachArt = new Dictionary<string, int>();
        var entfall = new Dictionary<string, int>();
        foreach (var t in Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var tp = Tagesplan.Berechnen(d, t);
            foreach (var e in tp.Eintraege.Where(e => e.Regel != null && e.Status != TagesStatus.Zusaetzlich && !Vertretungen.IstBereitschaftsfach(d, e.FachId)))
            {
                geplant++;
                var v = e.Vertretung;
                if (v is null || v.Art == VertretungsArt.Raumvertretung) { gehalten++; continue; }
                if (v.VertreterId == e.Regel!.LehrerId && !v.IstEntfall) { gehalten++; continue; }
                nicht++;
                var g = d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == v.GrundId);
                var gk = g is null ? "ohne Absenzgrund" : string.IsNullOrEmpty(g.Statistik) ? g.Langname is { Length: > 0 } ln ? ln : g.Kurzname : g.Statistik;
                nachGrund[gk] = nachGrund.GetValueOrDefault(gk) + 1;
                var a = Vertretungsplan.ArtText(v.Art) + (v.Mehrarbeit ? " (Mehrarbeit)" : "");
                nachArt[a] = nachArt.GetValueOrDefault(a) + 1;
                if (v.IstEntfall) entfall[gk] = entfall.GetValueOrDefault(gk) + 1;
            }
        }
        var sonder = d.Vertretungen.Count(v => v.Datum >= von && v.Datum <= bis && v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung);
        return new Uebersicht(geplant, gehalten, nicht, sonder, nachGrund, nachArt, entfall);
    }

    /// <summary>Mehrarbeit: tägliche Bilanz eines Lehrers in einem Monat (Soll, Ist, Vertretungen, Entfälle).</summary>
    public sealed record Tagesbilanz(DateOnly Datum, int Soll, int Ist, int Vertretungen, int Entfaelle);

    public static List<Tagesbilanz> Mehrarbeit(Schuldatei d, Lehrer l, int jahr, int monat)
    {
        var res = new List<Tagesbilanz>();
        var von = new DateOnly(jahr, monat, 1);
        foreach (var t in Kalender.Tage(von, von.AddMonths(1).AddDays(-1)).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var tp = Tagesplan.Berechnen(d, t);
            var soll = tp.Regel.Where(r => r.LehrerId == l.Id && !Vertretungen.IstBereitschaftsfach(d, r.FachId)).Select(r => r.Stunde).Distinct().Count();
            var ist = tp.VonLehrer(l.Id).Where(e => !Vertretungen.IstBereitschaftsfach(d, e.FachId)).Select(e => e.Stunde).Distinct().Count();
            var vtr = d.Vertretungen.Count(v => v.Datum == t && v.VertreterId == l.Id && v.LehrerId != l.Id && !v.IstEntfall);
            var entf = d.Vertretungen.Count(v => v.Datum == t && v.LehrerId == l.Id && v.IstEntfall);
            if (soll + ist + vtr + entf > 0) res.Add(new Tagesbilanz(t, soll, ist, vtr, entf));
        }
        return res;
    }

    /// <summary>Wochenwerte: geplante und gehaltene Unterrichte inkl. Vertretungen pro Kalenderwoche.</summary>
    public sealed record Wochenwert(DateOnly Montag, string Periode, decimal Soll, int Unterricht, int Vertretungen, int Entfaelle, decimal Korrektur, decimal Ist);

    public static List<Wochenwert> Wochenwerte(Schuldatei d, Lehrer l)
    {
        var res = new List<Wochenwert>();
        for (var m = Kalender.Montag(d.Einstellungen.SchuljahrVon); m <= d.Einstellungen.SchuljahrBis; m = m.AddDays(7))
        {
            var tage = Kalender.Tage(m, m.AddDays(6)).Where(t => Kalender.IstUnterrichtstag(d, t)).ToList();
            if (tage.Count == 0) continue;
            var unterricht = tage.Sum(t => Tagesplan.Regulaer(d, t).Where(r => r.LehrerId == l.Id && !Vertretungen.IstBereitschaftsfach(d, r.FachId)).Select(r => r.Stunde).Distinct().Count());
            var vtr = d.Vertretungen.Count(v => tage.Contains(v.Datum) && v.VertreterId == l.Id && v.LehrerId != l.Id && !v.IstEntfall && v.Art != VertretungsArt.Pausenaufsicht);
            var entf = d.Vertretungen.Count(v => tage.Contains(v.Datum) && v.LehrerId == l.Id && v.IstEntfall);
            var korr = l.Wertkorrekturen.Where(w => w.Datum >= m && w.Datum <= m.AddDays(6) && w.Art is "+" or "-").Sum(w => w.Art == "-" ? -w.Wert : w.Wert);
            res.Add(new Wochenwert(m, d.PeriodeAm(m).Name, l.Soll, unterricht, vtr, entf, korr, unterricht + vtr - entf + korr));
        }
        return res;
    }
}
