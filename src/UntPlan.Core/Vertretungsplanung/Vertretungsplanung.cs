using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core.Vertretungsplanung;

public sealed record Tagesstunde(Planstunde Planstunde, Unterricht Unterricht);

/// <summary>Grundfunktionen der Vertretungsplanung: Tagesplan, Abwesenheiten, Vertretungsbedarf.</summary>
public static class Vertretungen
{
    public static List<Tagesstunde> Tagesplan(Schuldatei d, DatenIndex ix, DateOnly datum)
    {
        if (!Kalender.IstUnterrichtstag(d, datum)) return new();
        var tag = Kalender.TagIndex(d, datum);
        var woche = Kalender.Schulwoche(d, datum);
        var periode = d.PeriodeAm(datum);
        return periode.Plan.Where(p => p.Tag == tag && ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) && !u.Ignorieren && u.GiltAm(datum, woche))
            .Select(p => new Tagesstunde(p, ix.Unterrichte[p.UnterrichtId])).ToList();
    }

    public static Abwesenheit? LehrerAbwesend(Schuldatei d, int lehrerId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.FirstOrDefault(a => a.Deckt(datum, stunde) &&
            ((a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrerId) || (a.Art == AbwesenheitsArt.Klasse && a.BegleitLehrerIds.Contains(lehrerId))));

    public static Abwesenheit? KlasseAbwesend(Schuldatei d, int klasseId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.FirstOrDefault(a => a.Art == AbwesenheitsArt.Klasse && a.ElementId == klasseId && a.Deckt(datum, stunde));

    public static Abwesenheit? RaumGesperrt(Schuldatei d, int raumId, DateOnly datum, int stunde) =>
        d.Abwesenheiten.FirstOrDefault(a => a.Art == AbwesenheitsArt.Raum && a.ElementId == raumId && a.Deckt(datum, stunde));

    public static string GrundText(Schuldatei d, DatenIndex ix, Abwesenheit? a) =>
        a?.GrundId is int g && ix.Gruende.TryGetValue(g, out var gr) ? gr.Kurzname : a is null ? "" : a.Art == AbwesenheitsArt.Klasse ? "Klasse abw." : "";

    public static bool IstBereitschaft(DatenIndex ix, Unterricht u) =>
        u.FachIds.Any(f => ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft);

    /// <summary>
    /// Erzeugt die offenen Vertretungen eines Tages aus den Abwesenheiten (Lehrer → Vertretung, Klasse → Freisetzung,
    /// Raum → Raumvertretung) und entfernt offene Einträge, deren Ursache entfallen ist. Bearbeitete Vertretungen bleiben erhalten.
    /// </summary>
    public static void Aktualisieren(Schuldatei d, DateOnly datum)
    {
        var ix = new DatenIndex(d);
        var plan = Tagesplan(d, ix, datum);
        var benoetigt = new List<Vertretung>();
        foreach (var (p, u) in plan)
        {
            var bereitschaft = IstBereitschaft(ix, u);
            var alleKlassenWeg = u.KlassenIds.Any() && u.KlassenIds.All(k => KlasseAbwesend(d, k, datum, p.Stunde) != null);
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var basis = new Vertretung
                {
                    Datum = datum, Stunde = p.Stunde, UnterrichtId = u.Id, ZeilenIndex = i, LehrerId = z.LehrerId, FachId = z.FachId,
                    KlassenIds = new List<int>(z.KlassenIds), RaumId = p.RaumFuerZeile(i),
                };
                if (alleKlassenWeg && !bereitschaft)
                {
                    var a = KlasseAbwesend(d, u.KlassenIds.First(), datum, p.Stunde);
                    basis.Art = VertretungsArt.Freisetzung;
                    basis.Grund = GrundText(d, ix, a);
                    basis.Text = a?.Text ?? "";
                    basis.VertreterId = null;
                    benoetigt.Add(basis);
                    continue;
                }
                if (z.LehrerId is int l && LehrerAbwesend(d, l, datum, p.Stunde) is { } abw)
                {
                    if (bereitschaft) continue; // Bereitschaft eines abwesenden Lehrers entfällt einfach
                    basis.Art = VertretungsArt.Vertretung;
                    basis.Grund = GrundText(d, ix, abw);
                    benoetigt.Add(basis);
                    continue;
                }
                if (p.RaumFuerZeile(i) is int r && RaumGesperrt(d, r, datum, p.Stunde) != null)
                {
                    basis.Art = VertretungsArt.Raumvertretung;
                    basis.VertreterId = z.LehrerId;
                    benoetigt.Add(basis);
                }
            }
        }
        bool Gleich(Vertretung a, Vertretung b) => a.UnterrichtId == b.UnterrichtId && a.ZeilenIndex == b.ZeilenIndex && a.Stunde == b.Stunde && a.Datum == b.Datum;
        // Unbearbeitete Einträge ohne Ursache entfernen
        d.Vertretungen.RemoveAll(v => v.Datum == datum && v.UnterrichtId.HasValue && IstAutomatisch(v) && !benoetigt.Any(b => Gleich(b, v) && b.Art == v.Art));
        foreach (var b in benoetigt)
            if (!d.Vertretungen.Any(v => Gleich(v, b)))
            {
                b.Id = d.NeueId();
                d.Vertretungen.Add(b);
            }
    }

    /// <summary>Automatisch angelegt und noch nicht bearbeitet.</summary>
    public static bool IstAutomatisch(Vertretung v) =>
        (v.Art == VertretungsArt.Vertretung && v.VertreterId is null) ||
        v.Art == VertretungsArt.Freisetzung ||
        (v.Art == VertretungsArt.Raumvertretung && v.VertretungsraumId is null);

    public static List<Vertretung> Am(Schuldatei d, DateOnly datum) =>
        d.Vertretungen.Where(v => v.Datum == datum).OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();

    /// <summary>Ist der Lehrer zu dieser Stunde gebunden? Liefert den Grund oder null.</summary>
    public static string? LehrerBelegt(Schuldatei d, DatenIndex ix, int lehrerId, DateOnly datum, int stunde, List<Tagesstunde>? plan = null, Vertretung? ausser = null)
    {
        if (LehrerAbwesend(d, lehrerId, datum, stunde) != null) return "abwesend";
        if (ix.Lehrer.TryGetValue(lehrerId, out var le))
        {
            var tag = Kalender.TagIndex(d, datum);
            if (le.KeineVertretung) return "keine Vertretung";
            if (tag >= 0 && le.Zeitwuensche.Gesperrt(tag, stunde)) return "gesperrt";
        }
        foreach (var v in d.Vertretungen)
            if (v != ausser && v.Datum == datum && v.Stunde == stunde && v.VertreterId == lehrerId &&
                v.Art is not (VertretungsArt.Entfall or VertretungsArt.Freisetzung or VertretungsArt.Raumvertretung))
                return "vertritt bereits";
        plan ??= Tagesplan(d, ix, datum);
        foreach (var (p, u) in plan)
        {
            if (p.Stunde != stunde) continue;
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                if (u.Zeilen[i].LehrerId != lehrerId) continue;
                if (IstBereitschaft(ix, u)) continue; // Bereitschaft = verfügbar
                var ersetzt = d.Vertretungen.Any(v => v.Datum == datum && v.Stunde == stunde && v.UnterrichtId == u.Id && v.ZeilenIndex == i &&
                    (v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung || (v.VertreterId.HasValue && v.VertreterId != lehrerId)));
                if (!ersetzt) return "eigener Unterricht";
            }
        }
        return null;
    }

    public static bool HatBereitschaft(Schuldatei d, DatenIndex ix, int lehrerId, DateOnly datum, int stunde, List<Tagesstunde> plan) =>
        plan.Any(x => x.Planstunde.Stunde == stunde && IstBereitschaft(ix, x.Unterricht) && x.Unterricht.LehrerIds.Contains(lehrerId));

    public static bool IstFreigesetzt(Schuldatei d, int lehrerId, DateOnly datum, int stunde) =>
        d.Vertretungen.Any(v => v.Datum == datum && v.Stunde == stunde && v.LehrerId == lehrerId && v.Art is VertretungsArt.Freisetzung or VertretungsArt.Entfall);

    public static readonly VertretungsArt[] ZaehlendeArten = { VertretungsArt.Vertretung, VertretungsArt.StattVertretung, VertretungsArt.Betreuung, VertretungsArt.Sondereinsatz, VertretungsArt.Pausenaufsicht };

    public static int Zaehler(Schuldatei d, int lehrerId, DateOnly von, DateOnly bis) =>
        d.Vertretungen.Count(v => v.VertreterId == lehrerId && v.Datum >= von && v.Datum <= bis && ZaehlendeArten.Contains(v.Art));
}

public sealed class Kandidat
{
    public Lehrer Lehrer { get; init; } = null!;
    public double Punkte { get; set; }
    public List<string> Kriterien { get; } = new();
    public bool Bereitschaft { get; set; }
    public bool Freigesetzt { get; set; }
    public bool KenntKlasse { get; set; }
    public bool Fachlehrer { get; set; }
    public int ZaehlerWoche { get; set; }
    public int ZaehlerMonat { get; set; }
    public int ZaehlerJahr { get; set; }
    public decimal Saldo { get; set; }
    public string Lage { get; set; } = "";
    public bool Mehrarbeit => !Bereitschaft && !Freigesetzt;
}

/// <summary>Vertretungsvorschlag mit einstellbarer Gewichtung der Kriterien (Vertretungsgewichtung).</summary>
public static class VertretungsVorschlag
{
    public static readonly Dictionary<string, int> StandardGewichtung = new()
    {
        ["V.Bereitschaft"] = 5, ["V.Freigesetzt"] = 5, ["V.KenntKlasse"] = 3, ["V.Fachlehrer"] = 3, ["V.Hohlstunde"] = 3, ["V.Randstunde"] = 2,
        ["V.KeinZusatztag"] = 4, ["V.Zaehler"] = 3, ["V.Saldo"] = 2, ["V.Zeitwunsch"] = 2, ["V.StdTag"] = 2,
    };

    public static readonly (string Key, string Text)[] Kriterien =
    {
        ("V.Bereitschaft", "Lehrer hat Bereitschaft"), ("V.Freigesetzt", "Lehrer ist freigesetzt (Klasse abwesend)"),
        ("V.KenntKlasse", "Lehrer kennt die Klasse"), ("V.Fachlehrer", "Fachlehrer (Lehrbefähigung)"),
        ("V.Hohlstunde", "Vertretung füllt eine Hohlstunde"), ("V.Randstunde", "Vertretung schließt an eigenen Unterricht an"),
        ("V.KeinZusatztag", "Lehrer hat an diesem Tag ohnehin Unterricht"), ("V.Zaehler", "Wenige Vertretungen (Woche/Monat)"),
        ("V.Saldo", "Minusstunden / Soll-Ist-Saldo"), ("V.Zeitwunsch", "Zeitwunsch des Lehrers beachten"), ("V.StdTag", "Max. Stunden pro Tag beachten"),
    };

    public static List<Kandidat> Berechnen(Schuldatei d, Vertretung v)
    {
        var ix = new DatenIndex(d);
        var plan = Vertretungen.Tagesplan(d, ix, v.Datum);
        var tag = Kalender.TagIndex(d, v.Datum);
        var g = d.Einstellungen.Vertretungsgewichtung;
        double W(string k) => Gewichtung.Faktor(g.GetValueOrDefault(k, StandardGewichtung[k]));
        var montag = Kalender.Montag(v.Datum);
        var monatsAnfang = new DateOnly(v.Datum.Year, v.Datum.Month, 1);
        var res = new List<Kandidat>();
        foreach (var l in d.Lehrer.Where(l => !l.Ignorieren && l.Id != v.LehrerId))
        {
            if (Vertretungen.LehrerBelegt(d, ix, l.Id, v.Datum, v.Stunde, plan, v) != null) continue;
            var k = new Kandidat { Lehrer = l };
            var eigene = plan.Where(x => x.Unterricht.LehrerIds.Contains(l.Id) && !Vertretungen.IstBereitschaft(ix, x.Unterricht)).Select(x => x.Planstunde.Stunde).Distinct().ToHashSet();
            if (Vertretungen.HatBereitschaft(d, ix, l.Id, v.Datum, v.Stunde, plan)) { k.Bereitschaft = true; k.Punkte += 12 * W("V.Bereitschaft"); k.Kriterien.Add("Bereitschaft"); }
            if (Vertretungen.IstFreigesetzt(d, l.Id, v.Datum, v.Stunde)) { k.Freigesetzt = true; k.Punkte += 12 * W("V.Freigesetzt"); k.Kriterien.Add("freigesetzt"); }
            k.KenntKlasse = v.KlassenIds.Any() && d.Unterrichte.Any(u => u.Zeilen.Any(z => z.LehrerId == l.Id && z.KlassenIds.Intersect(v.KlassenIds).Any()));
            if (k.KenntKlasse) { k.Punkte += 6 * W("V.KenntKlasse"); k.Kriterien.Add("kennt Klasse"); }
            k.Fachlehrer = v.FachId.HasValue && l.FaecherIds.Contains(v.FachId.Value);
            if (k.Fachlehrer) { k.Punkte += 6 * W("V.Fachlehrer"); k.Kriterien.Add("Fachlehrer"); }
            if (eigene.Count == 0 && !k.Bereitschaft) { k.Punkte -= 10 * W("V.KeinZusatztag"); k.Lage = "sonst frei"; }
            else if (eigene.Contains(v.Stunde - 1) || eigene.Contains(v.Stunde + 1)) { k.Punkte += 4 * W("V.Randstunde"); k.Lage = "anschließend"; }
            else if (eigene.Count > 0 && v.Stunde > eigene.Min() && v.Stunde < eigene.Max()) { k.Punkte += 5 * W("V.Hohlstunde"); k.Lage = "Hohlstunde"; }
            else if (eigene.Count > 0) { k.Punkte -= 3 * W("V.Randstunde"); k.Lage = "verlängert Tag"; }
            k.ZaehlerWoche = Vertretungen.Zaehler(d, l.Id, montag, montag.AddDays(6));
            k.ZaehlerMonat = Vertretungen.Zaehler(d, l.Id, monatsAnfang, monatsAnfang.AddMonths(1).AddDays(-1));
            k.ZaehlerJahr = Vertretungen.Zaehler(d, l.Id, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis);
            k.Punkte -= (k.ZaehlerWoche * 2 + k.ZaehlerMonat * 0.5) * W("V.Zaehler");
            if ((l.VertretungenMaxWoche > 0 && k.ZaehlerWoche >= l.VertretungenMaxWoche) || (l.VertretungenMaxMonat > 0 && k.ZaehlerMonat >= l.VertretungenMaxMonat))
            { k.Punkte -= 40 * W("V.Zaehler"); k.Kriterien.Add("Maximum erreicht"); }
            k.Saldo = Statistik.Saldo(d, l, d.Einstellungen.SchuljahrVon, v.Datum.AddDays(-1));
            k.Punkte += (double)Math.Clamp(-k.Saldo, -10, 10) * W("V.Saldo") * 0.5;
            if (tag >= 0)
            {
                var zw = l.Zeitwuensche.Get(tag, v.Stunde);
                if (zw < 0) { k.Punkte += zw * 4 * W("V.Zeitwunsch"); k.Kriterien.Add($"Zeitwunsch {zw}"); }
            }
            if (!l.StundenProTag.IstLeer && eigene.Count + 1 > l.StundenProTag.Max) { k.Punkte -= 10 * W("V.StdTag"); k.Kriterien.Add("Std./Tag überschritten"); }
            res.Add(k);
        }
        return res.OrderByDescending(x => x.Punkte).ToList();
    }

    public static bool EntfallSinnvoll(Schuldatei d, Vertretung v)
    {
        var ix = new DatenIndex(d);
        var ab = Nrw.JahrgangRang(d.Einstellungen.EntfallAbJahrgang);
        if (v.KlassenIds.Count == 0) return false;
        var plan = Vertretungen.Tagesplan(d, ix, v.Datum);
        return v.KlassenIds.All(kid =>
        {
            if (!ix.Klassen.TryGetValue(kid, out var k) || Nrw.JahrgangRang(k.Jahrgang) < ab) return false;
            var stunden = plan.Where(x => x.Unterricht.KlassenIds.Contains(kid)).Select(x => x.Planstunde.Stunde).ToList();
            return stunden.Count > 0 && (v.Stunde == stunden.Min() || v.Stunde == stunden.Max());
        });
    }

    /// <summary>Automatische Vertretungsplanung eines Tages: bester Kandidat, sonst Entfall (wenn zulässig) bzw. offen.</summary>
    public static int Automatisch(Schuldatei d, DateOnly datum)
    {
        Vertretungen.Aktualisieren(d, datum);
        var anzahl = 0;
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.Offen).OrderBy(v => v.Stunde).ToList())
        {
            var k = Berechnen(d, v).FirstOrDefault();
            if (EntfallSinnvoll(d, v) && (k is null || k.Punkte < 20))
            {
                v.Art = VertretungsArt.Entfall;
                v.Text = "Unterricht fällt aus";
            }
            else if (k != null)
            {
                v.VertreterId = k.Lehrer.Id;
                v.Mehrarbeit = k.Mehrarbeit;
                v.VertretungsfachId ??= v.FachId;
            }
            else continue;
            anzahl++;
        }
        return anzahl;
    }
}

/// <summary>Vertretungsstatistik / Lehrer-Abrechnung inkl. NRW-Mehrarbeit je Kalendermonat.</summary>
public static class Statistik
{
    public sealed record LehrerZeile(Lehrer Lehrer, int Vertretungen, int Mehrarbeit, int Betreuungen, int Sondereinsaetze, int Aufsichten, int Entfaelle, int Freisetzungen, decimal Saldo, int Verguetungsfaehig);

    public static decimal Saldo(Schuldatei d, Lehrer l, DateOnly von, DateOnly bis)
    {
        var plus = d.Vertretungen.Count(v => v.VertreterId == l.Id && v.Datum >= von && v.Datum <= bis && v.Mehrarbeit && Vertretungen.ZaehlendeArten.Contains(v.Art));
        var minus = d.Vertretungen.Count(v => v.LehrerId == l.Id && v.Datum >= von && v.Datum <= bis && v.Art is VertretungsArt.Freisetzung);
        return plus - minus;
    }

    public static List<LehrerZeile> Abrechnung(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var res = new List<LehrerZeile>();
        foreach (var l in d.Lehrer)
        {
            var als = d.Vertretungen.Where(v => v.VertreterId == l.Id && v.Datum >= von && v.Datum <= bis).ToList();
            var eigene = d.Vertretungen.Where(v => v.LehrerId == l.Id && v.Datum >= von && v.Datum <= bis).ToList();
            var vert = als.Count(v => v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung);
            var mehr = als.Count(v => v.Mehrarbeit && Vertretungen.ZaehlendeArten.Contains(v.Art));
            var betr = als.Count(v => v.Art == VertretungsArt.Betreuung);
            var sonder = als.Count(v => v.Art == VertretungsArt.Sondereinsatz);
            var aufs = als.Count(v => v.Art == VertretungsArt.Pausenaufsicht);
            var entf = eigene.Count(v => v.Art == VertretungsArt.Entfall);
            var frei = eigene.Count(v => v.Art == VertretungsArt.Freisetzung);
            // NRW: Vergütung je Kalendermonat, wenn mehr als die Grenze (Standard 3) geleistet wurde
            var verg = 0;
            foreach (var monat in als.Where(v => v.Mehrarbeit).GroupBy(v => (v.Datum.Year, v.Datum.Month)))
            {
                var minder = eigene.Count(v => v.Art == VertretungsArt.Freisetzung && v.Datum.Year == monat.Key.Year && v.Datum.Month == monat.Key.Month);
                var saldoMonat = monat.Count() - minder;
                if (saldoMonat > d.Einstellungen.Mehrarbeitsgrenze) verg += saldoMonat;
            }
            if (als.Count + eigene.Count == 0) continue;
            res.Add(new LehrerZeile(l, vert, mehr, betr, sonder, aufs, entf, frei, mehr - frei, verg));
        }
        return res.OrderBy(r => r.Lehrer.Kurzname).ToList();
    }

    public sealed record Ausfall(int Gesamt, int Vertreten, int Entfall, int Freisetzung, int Raum, int Verlegung, int Offen);

    public static Ausfall Zusammenfassung(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var l = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).ToList();
        return new Ausfall(l.Count,
            l.Count(v => v.VertreterId.HasValue && v.Art is VertretungsArt.Vertretung or VertretungsArt.Betreuung or VertretungsArt.StattVertretung),
            l.Count(v => v.Art == VertretungsArt.Entfall), l.Count(v => v.Art == VertretungsArt.Freisetzung),
            l.Count(v => v.Art == VertretungsArt.Raumvertretung), l.Count(v => v.Art == VertretungsArt.Verlegung), l.Count(v => v.Offen));
    }
}

public sealed record PlanZeile(DateOnly Datum, int Stunde, string Klassen, string Lehrer, string Vertreter, string Fach, string NeuesFach, string Raum, string NeuerRaum,
    string Art, string Text, IReadOnlyList<int> KlassenIds, int? LehrerId, int? VertreterId);

/// <summary>Ausgabe des Vertretungsplans (Aushang, Monitor, HTML, Portal).</summary>
public static class Vertretungsplan
{
    public static string ArtText(VertretungsArt a) => a switch
    {
        VertretungsArt.Vertretung => "Vertretung",
        VertretungsArt.Entfall => "Entfall",
        VertretungsArt.Raumvertretung => "Raum-Vtr.",
        VertretungsArt.Verlegung => "Verlegung",
        VertretungsArt.StattVertretung => "statt-Vertretung",
        VertretungsArt.Betreuung => "Betreuung",
        VertretungsArt.Sondereinsatz => "Sondereinsatz",
        VertretungsArt.Freisetzung => "Freisetzung",
        VertretungsArt.Tausch => "Tausch",
        VertretungsArt.Pausenaufsicht => "Pausenaufsicht",
        _ => "Klausur",
    };

    public static List<PlanZeile> Zeilen(Schuldatei d, DateOnly datum)
    {
        var ix = new DatenIndex(d);
        return Vertretungen.Am(d, datum).Select(v => new PlanZeile(
            v.Datum, v.Stunde, ix.KKlassen(v.KlassenIds), ix.KLehrer(v.LehrerId),
            v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung ? "---" : v.VertreterId.HasValue ? ix.KLehrer(v.VertreterId) : "???",
            ix.KFach(v.FachId), ix.KFach(v.VertretungsfachId ?? v.FachId), ix.KRaum(v.RaumId),
            v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung ? "" : ix.KRaum(v.VertretungsraumId ?? v.RaumId),
            ArtText(v.Art),
            v.Art == VertretungsArt.Verlegung && v.NachDatum.HasValue ? $"verlegt auf {Format.Datum(v.NachDatum.Value)} {v.NachStunde}. Std. {v.Text}".Trim() : v.Text,
            v.KlassenIds, v.LehrerId, v.VertreterId))
            .OrderBy(z => z.Klassen, StringComparer.Create(Format.De, true)).ThenBy(z => z.Stunde).ToList();
    }
}
