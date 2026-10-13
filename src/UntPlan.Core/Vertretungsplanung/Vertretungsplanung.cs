using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Grundfunktionen der Vertretungsplanung: offene Vertretungen aus Absenzen, Veranstaltungen und Klausuren erzeugen,
/// Verfügbarkeit, Merker, Bearbeitung (Vertreter, Entfall, Verlegung, Tausch, Sondereinsatz, Vormerkung, Raum).
/// </summary>
public static class Vertretungen
{
    private static readonly VertretungsQuelle[] Abgeleitet = { VertretungsQuelle.Absenz, VertretungsQuelle.Veranstaltung, VertretungsQuelle.Klausur };

    public static bool IstAbgeleitet(Vertretung v) => Abgeleitet.Contains(v.Quelle);

    public static string GrundText(Schuldatei d, int? grundId) =>
        grundId is int g ? d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == g)?.Kurzname ?? "" : "";

    public static string GrundText(Schuldatei d, DatenIndex ix, Abwesenheit? a) => a is null ? "" : GrundText(d, a.GrundId) is { Length: > 0 } t ? t : a.Art == AbwesenheitsArt.Klasse ? "Klasse abw." : "";

    public static bool IstBereitschaft(DatenIndex ix, Unterricht u) => u.FachIds.Any(f => ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft);

    public static bool IstBereitschaftsfach(Schuldatei d, int? fachId) => fachId is int f && d.Faecher.FirstOrDefault(x => x.Id == f) is { Bereitschaft: true };

    /// <summary>Vertretungszeitraster: Regel für eine Stunde (Klassenzeitraster vor globalem Zeitraster).</summary>
    public static VertretungsRegel Regel(Schuldatei d, IEnumerable<int> klassenIds, int tag, int stunde)
    {
        foreach (var k in klassenIds)
            if (d.Klassen.FirstOrDefault(x => x.Id == k) is { } kl && kl.VertretungsZeitraster.TryGetValue(tag * 100 + stunde, out var r))
                return r;
        return d.Vertretungseinstellungen.Regel(tag, stunde);
    }

    public static void Aktualisieren(Schuldatei d, DateOnly von, DateOnly bis)
    {
        foreach (var t in Kalender.Tage(von, bis)) Aktualisieren(d, t);
    }

    /// <summary>
    /// Erzeugt die Vertretungszeilen eines Tages aus Absenzen, Veranstaltungen und Klausuren und gleicht sie mit den
    /// vorhandenen ab. Bearbeitete Zeilen bleiben erhalten; entfällt die Ursache (z. B. Absenz gelöscht), werden die
    /// daraus entstandenen Zeilen entfernt. Manuelle Änderungen (Verlegung, Sondereinsatz) haben Vorrang.
    /// </summary>
    public static void Aktualisieren(Schuldatei d, DateOnly datum)
    {
        var tag = Kalender.TagIndex(d, datum);
        var regel = Tagesplan.Regulaer(d, datum);
        var manuell = d.Vertretungen.Where(v => v.Datum == datum && !IstAbgeleitet(v) && v.UnterrichtId.HasValue)
            .Select(v => (v.UnterrichtId!.Value, v.ZeilenIndex, v.Stunde)).ToHashSet();
        var benoetigt = new List<Vertretung>();
        var fachIx = d.Faecher.ToDictionary(f => f.Id);
        var klausuren = d.Klausuren.Where(k => k.Aktiv && k.Datum == datum).ToList();

        if (Kalender.IstUnterrichtstag(d, datum))
        {
            foreach (var k in klausuren)
                for (var s = k.StundeVon; s <= Math.Max(k.StundeVon, k.StundeBis); s++)
                    foreach (var l in k.LehrerIds.DefaultIfEmpty(-1))
                        benoetigt.Add(new Vertretung
                        {
                            Datum = datum, Stunde = s, Art = VertretungsArt.Klausur, Quelle = VertretungsQuelle.Klausur, KlausurId = k.Id,
                            VertreterId = l < 0 ? null : l, KlassenIds = k.KlassenIds.ToList(), VertretungsraumId = k.RaumIds.Cast<int?>().FirstOrDefault(),
                            ZusatzraumIds = k.RaumIds.Skip(1).ToList(), Text = string.IsNullOrEmpty(k.Text) ? k.Name : $"{k.Name} {k.Text}".Trim(),
                        });
            foreach (var va in d.Veranstaltungen.Where(v => datum >= v.Von && datum <= v.Bis))
                for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
                {
                    if (!va.Deckt(datum, s) || !d.Zeitraster.Verfuegbar(tag, s)) continue;
                    foreach (var l in va.LehrerIds.DefaultIfEmpty(-1))
                        benoetigt.Add(new Vertretung
                        {
                            Datum = datum, Stunde = s, Art = VertretungsArt.Veranstaltung, Quelle = VertretungsQuelle.Veranstaltung, VeranstaltungId = va.Id,
                            VertreterId = l < 0 ? null : l, KlassenIds = va.KlassenIds.ToList(), VertretungsraumId = va.RaumIds.Cast<int?>().FirstOrDefault(),
                            GrundId = va.GrundId, Grund = GrundText(d, va.GrundId), Text = va.Text, Schuelergruppe = va.Schuelergruppe, Wert = va.Wert,
                        });
                }
        }

        foreach (var r in regel)
        {
            if (manuell.Contains((r.Unterricht.Id, r.ZeilenIndex, r.Stunde))) continue;
            var u = r.Unterricht;
            var fach = r.FachId is int fid ? fachIx.GetValueOrDefault(fid) : null;
            var bereit = fach?.Bereitschaft == true;
            var basis = new Vertretung
            {
                Datum = datum, Stunde = r.Stunde, UnterrichtId = u.Id, ZeilenIndex = r.ZeilenIndex, LehrerId = r.LehrerId, FachId = r.FachId,
                KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId, Schuelergruppe = u.Schuelergruppe,
            };
            // Klausur: Unterricht der Klasse entfällt, Aufsicht hat eigenen Unterricht → offen
            var klausur = klausuren.FirstOrDefault(k => r.Stunde >= k.StundeVon && r.Stunde <= Math.Max(k.StundeVon, k.StundeBis) &&
                ((k.UnterrichtEntfaellt && k.KlassenIds.Intersect(r.KlassenIds).Any()) || k.UnterrichtId == u.Id));
            if (klausur != null)
            {
                basis.Art = VertretungsArt.Entfall;
                basis.Quelle = VertretungsQuelle.Klausur;
                basis.KlausurId = klausur.Id;
                basis.Text = "Klausur " + klausur.Name;
                benoetigt.Add(basis);
                continue;
            }
            if (r.LehrerId is int kl0 && klausuren.FirstOrDefault(k => k.LehrerIds.Contains(kl0) && r.Stunde >= k.StundeVon && r.Stunde <= Math.Max(k.StundeVon, k.StundeBis)) is { } aufsicht)
            {
                basis.Quelle = VertretungsQuelle.Klausur;
                basis.KlausurId = aufsicht.Id;
                basis.Art = bereit ? VertretungsArt.BereitschaftEntfaellt : NachZeitraster(d, r, tag);
                basis.Text = "Klausuraufsicht";
                benoetigt.Add(basis);
                continue;
            }
            // Klassen abwesend → Freisetzung (bzw. Entfall, wenn der Lehrer an derselben Veranstaltung teilnimmt)
            if (r.KlassenIds.Count > 0 && r.KlassenIds.All(k => Absenzen.KlasseAbwesend(d, k, datum, r.Stunde)))
            {
                var abw = Absenzen.Klasse(d, r.KlassenIds[0], datum, r.Stunde);
                var va = abw is null ? Absenzen.VeranstaltungKlasse(d, r.KlassenIds[0], datum, r.Stunde) : null;
                if (bereit) continue;
                basis.Quelle = va != null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                basis.AbwesenheitId = abw?.Id;
                basis.VeranstaltungId = va?.Id;
                basis.GrundId = abw?.GrundId ?? va?.GrundId;
                basis.Grund = GrundText(d, basis.GrundId) is { Length: > 0 } g ? g : "Klasse abw.";
                basis.Text = abw?.Text ?? va?.Text ?? "";
                basis.Art = va != null && r.LehrerId is int vl && va.LehrerIds.Contains(vl) ? VertretungsArt.Entfall : VertretungsArt.Freisetzung;
                benoetigt.Add(basis);
                continue;
            }
            // Lehrer abwesend
            if (r.LehrerId is int l && Absenzen.LehrerAbwesend(d, l, datum, r.Stunde))
            {
                var abw = Absenzen.Lehrer(d, l, datum, r.Stunde);
                var va = abw is null ? Absenzen.VeranstaltungLehrer(d, l, datum, r.Stunde) : null;
                basis.Quelle = va != null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                basis.AbwesenheitId = abw?.Id;
                basis.VeranstaltungId = va?.Id;
                basis.GrundId = abw?.GrundId ?? va?.GrundId;
                basis.Grund = GrundText(d, basis.GrundId);
                if (bereit) basis.Art = fach!.KeineVertretung ? VertretungsArt.BereitschaftEntfaellt : VertretungsArt.Bereitschaftsvertretung;
                else if (fach?.KeineVertretung == true) basis.Art = VertretungsArt.Entfall;
                else if (u.BetreuungLehrerId is int b && b != l && !Absenzen.LehrerAbwesend(d, b, datum, r.Stunde))
                {
                    basis.Art = VertretungsArt.Betreuung;
                    basis.VertreterId = b;
                    basis.Mitbetreuung = true;
                }
                else
                {
                    basis.Art = NachZeitraster(d, r, tag);
                    if (basis.Art == VertretungsArt.EigenverantwortlichesArbeiten) basis.VertretungsraumId = d.Vertretungseinstellungen.EvaRaumId ?? r.RaumId;
                }
                benoetigt.Add(basis);
                continue;
            }
            // Raum gesperrt → Raumvertretung
            if (r.RaumId is int raum && Absenzen.RaumGesperrt(d, raum, datum, r.Stunde))
            {
                var abw = Absenzen.Raum(d, raum, datum, r.Stunde);
                basis.Art = VertretungsArt.Raumvertretung;
                basis.Quelle = abw is null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                basis.AbwesenheitId = abw?.Id;
                basis.GrundId = abw?.GrundId;
                basis.VertreterId = r.LehrerId;
                benoetigt.Add(basis);
            }
        }

        // Pausenaufsichten abwesender Lehrer
        if (tag >= 0 && Kalender.IstUnterrichtstag(d, datum))
        {
            var periode = d.PeriodeAm(datum);
            foreach (var a in (periode.Pausenaufsichten ?? new()).Where(a => a.Tag == tag && (a.Datum is null || a.Datum == datum)))
            {
                if (a.UGruppeId is int ug && d.UGruppen.FirstOrDefault(x => x.Id == ug) is { } g && !Kalender.UGruppeAktiv(d, g, datum)) continue;
                var abwesend = Absenzen.LehrerAbwesend(d, a.LehrerId, datum, a.NachStunde + 1) || Absenzen.LehrerAbwesend(d, a.LehrerId, datum, a.NachStunde);
                if (!abwesend) continue;
                var abw = Absenzen.Lehrer(d, a.LehrerId, datum, a.NachStunde + 1) ?? Absenzen.Lehrer(d, a.LehrerId, datum, a.NachStunde);
                benoetigt.Add(new Vertretung
                {
                    Datum = datum, Stunde = a.NachStunde, Art = VertretungsArt.Pausenaufsicht, Quelle = VertretungsQuelle.Absenz, GangId = a.GangId,
                    LehrerId = a.LehrerId, AbwesenheitId = abw?.Id, GrundId = abw?.GrundId, Grund = GrundText(d, abw?.GrundId),
                });
            }
        }

        // Abgleich
        var vorhanden = d.Vertretungen.Where(v => v.Datum == datum && IstAbgeleitet(v)).ToList();
        foreach (var v in vorhanden)
        {
            var key = Schluessel(v);
            var b = key is null ? null : benoetigt.FirstOrDefault(x => Schluessel(x) == key);
            if (b is null)
            {
                if (v.VerlegungsId.HasValue) v.Quelle = VertretungsQuelle.Verlegung;
                else d.Vertretungen.Remove(v);
                continue;
            }
            benoetigt.Remove(b);
            v.GrundId = b.GrundId;
            v.Grund = b.Grund;
            v.AbwesenheitId = b.AbwesenheitId;
            v.VeranstaltungId = b.VeranstaltungId;
            v.KlausurId = b.KlausurId;
            v.Quelle = b.Quelle;
            if (!v.Bearbeitet)
            {
                v.Art = b.Art;
                v.VertreterId = b.VertreterId;
                v.Mitbetreuung = b.Mitbetreuung;
                v.VertretungsraumId = b.VertretungsraumId;
                v.ZusatzraumIds = b.ZusatzraumIds;
                v.KlassenIds = b.KlassenIds;
                v.Text = b.Text;
                v.RaumId = b.RaumId;
                v.LehrerId = b.LehrerId;
                v.FachId = b.FachId;
            }
        }
        foreach (var b in benoetigt)
        {
            b.Id = d.NeueId();
            d.Vertretungen.Add(b);
        }

        // Eingesetzte Vertreter, die inzwischen selbst abwesend sind → wieder offen
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.VertreterId.HasValue && v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung
                     or VertretungsArt.Betreuung or VertretungsArt.Teilvertretung or VertretungsArt.Lehrertausch or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Pausenaufsicht))
        {
            var stunde = v.Art == VertretungsArt.Pausenaufsicht ? v.Stunde + 1 : v.Stunde;
            if (v.VertreterId == v.LehrerId) continue;
            if (Absenzen.LehrerAbwesend(d, v.VertreterId!.Value, datum, stunde))
            {
                v.VertreterId = null;
                v.Mitbetreuung = false;
                if (v.Art is VertretungsArt.StattVertretung or VertretungsArt.Betreuung or VertretungsArt.Teilvertretung or VertretungsArt.Lehrertausch) v.Art = VertretungsArt.Vertretung;
            }
        }
    }

    private static VertretungsArt NachZeitraster(Schuldatei d, RegelStunde r, int tag) => Regel(d, r.KlassenIds, tag, r.Stunde) switch
    {
        VertretungsRegel.Entfall => VertretungsArt.Entfall,
        VertretungsRegel.Eigenverantwortlich => VertretungsArt.EigenverantwortlichesArbeiten,
        _ => VertretungsArt.Vertretung,
    };

    private static string? Schluessel(Vertretung v) =>
        v.GangId.HasValue ? $"P{v.GangId}/{v.Stunde}/{v.LehrerId}" :
        v.UnterrichtId is int u ? $"U{u}/{v.ZeilenIndex}/{v.Stunde}" :
        v.KlausurId is int k ? $"K{k}/{v.Stunde}/{v.VertreterId}" :
        v.VeranstaltungId is int va ? $"V{va}/{v.Stunde}/{v.VertreterId}" : null;

    public static List<Vertretung> Am(Schuldatei d, DateOnly datum) =>
        d.Vertretungen.Where(v => v.Datum == datum).OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();

    public static List<Vertretung> ImZeitraum(Schuldatei d, DateOnly von, DateOnly bis) =>
        d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ThenBy(v => v.Id).ToList();

    /// <summary>Ist der Lehrer zu dieser Stunde gebunden? Liefert den Grund oder null.</summary>
    public static string? LehrerBelegt(Schuldatei d, Tagesplan tp, int lehrerId, int stunde, Vertretung? ausser = null)
    {
        if (Absenzen.LehrerAbwesend(d, lehrerId, tp.Datum, stunde)) return "abwesend";
        foreach (var e in tp.Stunde(stunde))
        {
            if (!e.Findetstatt || !e.LehrerIds.Contains(lehrerId) || (ausser != null && e.Vertretung == ausser)) continue;
            if (e.Status == TagesStatus.Regulaer && IstBereitschaftsfach(d, e.FachId)) continue; // Bereitschaft = verfügbar
            if (e.Vertretung?.Mitbetreuung == true) continue;
            return e.Status == TagesStatus.Regulaer ? "eigener Unterricht" : "vertritt bereits";
        }
        return null;
    }

    public static string? LehrerBelegt(Schuldatei d, DatenIndex ix, int lehrerId, DateOnly datum, int stunde, Tagesplan? plan = null, Vertretung? ausser = null) =>
        LehrerBelegt(d, plan ?? Tagesplan.Berechnen(d, datum), lehrerId, stunde, ausser);

    /// <summary>Lehrer hat in dieser Stunde eine (noch ungenutzte) Bereitschaft.</summary>
    public static Fach? Bereitschaft(Schuldatei d, Tagesplan tp, int lehrerId, int stunde)
    {
        var r = tp.Regel.FirstOrDefault(x => x.Stunde == stunde && x.LehrerId == lehrerId && IstBereitschaftsfach(d, x.FachId));
        if (r is null) return null;
        var e = tp.Eintraege.FirstOrDefault(x => x.Regel == r);
        if (e?.Status == TagesStatus.Entfall) return null;
        return d.Faecher.First(f => f.Id == r.FachId);
    }

    public static bool HatBereitschaft(Schuldatei d, Tagesplan tp, int lehrerId, int stunde) => Bereitschaft(d, tp, lehrerId, stunde) != null;

    /// <summary>Lehrer ist freigesetzt: eigener Unterricht dieser Stunde entfällt, obwohl er anwesend ist.</summary>
    public static bool IstFreigesetzt(Schuldatei d, Tagesplan tp, int lehrerId, int stunde) =>
        !Absenzen.LehrerAbwesend(d, lehrerId, tp.Datum, stunde) &&
        tp.Eintraege.Any(e => e.Stunde == stunde && e.Regel?.LehrerId == lehrerId && e.Status == TagesStatus.Entfall && !IstBereitschaftsfach(d, e.FachId)) &&
        !tp.VonLehrer(lehrerId).Any(e => e.Stunde == stunde && !IstBereitschaftsfach(d, e.FachId));

    public static bool IstFreigesetzt(Schuldatei d, int lehrerId, DateOnly datum, int stunde) => IstFreigesetzt(d, Tagesplan.Berechnen(d, datum), lehrerId, stunde);

    /// <summary>
    /// Merker zur Stunde: "x" = freigesetzt, Fachkürzel = Bereitschaft, "0" = Hohlstunde, "1".."8" = Abstand zur nächsten
    /// eigenen Unterrichtsstunde, "9" = an diesem Tag kein Unterricht.
    /// </summary>
    public static string Merker(Schuldatei d, Tagesplan tp, int lehrerId, int stunde)
    {
        if (IstFreigesetzt(d, tp, lehrerId, stunde)) return "x";
        if (Bereitschaft(d, tp, lehrerId, stunde) is { } f) return f.Kurzname;
        var eigene = tp.VonLehrer(lehrerId).Where(e => !IstBereitschaftsfach(d, e.FachId)).Select(e => e.Stunde).Distinct().ToList();
        if (eigene.Count == 0) return "9";
        if (stunde > eigene.Min() && stunde < eigene.Max()) return "0";
        return Math.Min(8, eigene.Min(s => Math.Abs(s - stunde))).ToString();
    }

    public static int MerkerWert(string merker) => int.TryParse(merker, out var n) ? n : 0;

    /// <summary>Anzahl Unterrichtsstunden des Lehrers an diesem Tag (tagesaktuell).</summary>
    public static int UnterrichtAmTag(Schuldatei d, Tagesplan tp, int lehrerId) =>
        tp.VonLehrer(lehrerId).Where(e => !IstBereitschaftsfach(d, e.FachId)).Select(e => e.Stunde).Distinct().Count();

    // ----------------------------------------------------------------------------------------------
    // Bearbeitung
    // ----------------------------------------------------------------------------------------------

    private static void Markieren(Vertretung v, string benutzer)
    {
        v.Bearbeitet = true;
        v.Geaendert = DateTime.Now;
        if (!string.IsNullOrEmpty(benutzer)) v.Benutzer = benutzer;
    }

    /// <summary>
    /// Vertreter einsetzen. "+" = Vertretung ohne Lehrkraft, "-" = Entfall, null = wieder offen.
    /// Statt-Vertretung, Betreuung (Lehrer hat selbst Unterricht) und Bereitschaft werden erkannt.
    /// </summary>
    public static void VertreterSetzen(Schuldatei d, Vertretung v, int? lehrerId, bool mitbetreuung = false, string benutzer = "")
    {
        Markieren(v, benutzer);
        if (lehrerId is null)
        {
            v.VertreterId = null;
            v.Mitbetreuung = false;
            if (v.Art is not (VertretungsArt.Pausenaufsicht or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Raumvertretung)) v.Art = VertretungsArt.Vertretung;
            v.Mehrarbeit = false;
            return;
        }
        var l = lehrerId.Value;
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var stunde = v.Art == VertretungsArt.Pausenaufsicht ? v.Stunde : v.Stunde;
        v.VertreterId = l;
        v.Mitbetreuung = mitbetreuung;
        v.VertretungsfachId ??= v.Art == VertretungsArt.Pausenaufsicht ? null : v.FachId;
        var bereit = HatBereitschaft(d, tp, l, stunde);
        var frei = IstFreigesetzt(d, tp, l, stunde);
        if (v.Art is VertretungsArt.Pausenaufsicht or VertretungsArt.Raumvertretung or VertretungsArt.Sondereinsatz or VertretungsArt.Klausur
            or VertretungsArt.Veranstaltung or VertretungsArt.Verlegung or VertretungsArt.Tausch or VertretungsArt.UnterrichtGeaendert or VertretungsArt.SondereinsatzVormerkung) { }
        else if (v.LehrerId == l && Absenzen.LehrerAbwesend(d, l, v.Datum, stunde)) v.Art = VertretungsArt.TrotzAbsenz;
        else if (mitbetreuung) v.Art = VertretungsArt.Betreuung;
        else if (frei) v.Art = VertretungsArt.StattVertretung;
        else if (v.Art == VertretungsArt.Bereitschaftsvertretung) { }
        else if (tp.VonLehrer(l).Any(e => e.Stunde == stunde && e.Vertretung != v && !IstBereitschaftsfach(d, e.FachId))) v.Art = VertretungsArt.Lehrertausch;
        else v.Art = VertretungsArt.Vertretung;
        v.Mehrarbeit = !bereit && !frei && !mitbetreuung && v.Art is VertretungsArt.Vertretung or VertretungsArt.Lehrertausch or VertretungsArt.Sondereinsatz or VertretungsArt.Klausur;
        // Lehrer-Raum-Prinzip: Raum des Vertreters
        if (d.Vertretungseinstellungen.LehrerRaumPrinzip && d.Lehrer.FirstOrDefault(x => x.Id == l)?.StammraumId is int sr &&
            !tp.Stunde(stunde).Any(e => e.Findetstatt && e.Vertretung != v && e.RaumIds.Contains(sr)))
            v.VertretungsraumId = sr;
        // Vertretungskopplung: gleicher Vertreter in gekoppelten Zeilen
        if (v.Kopplung > 0)
            foreach (var w in d.Vertretungen.Where(x => x != v && x.Datum == v.Datum && x.Stunde == v.Stunde && x.Kopplung == v.Kopplung))
            {
                w.VertreterId = l;
                w.VertretungsraumId = v.VertretungsraumId;
                w.Art = v.Art;
                Markieren(w, benutzer);
            }
    }

    /// <summary>Schaltfläche Entfall: offene Vertretung → Entfall, Entfall → wieder offene Vertretung.</summary>
    public static void EntfallUmschalten(Schuldatei d, Vertretung v, string benutzer = "")
    {
        Markieren(v, benutzer);
        if (v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt)
        {
            v.Art = v.GangId.HasValue ? VertretungsArt.Pausenaufsicht : IstBereitschaftsfach(d, v.FachId) ? VertretungsArt.Bereitschaftsvertretung : VertretungsArt.Vertretung;
            v.VertreterId = null;
            return;
        }
        if (v.Art is VertretungsArt.Freisetzung) return;
        v.Art = IstBereitschaftsfach(d, v.FachId) ? VertretungsArt.BereitschaftEntfaellt : VertretungsArt.Entfall;
        v.VertreterId = null;
        v.VertretungsraumId = null;
        v.Mehrarbeit = false;
    }

    /// <summary>Vertretung ohne Lehrkraft ("+" beim Vertreter).</summary>
    public static void OhneLehrer(Vertretung v, string benutzer = "")
    {
        Markieren(v, benutzer);
        v.Art = VertretungsArt.VertretungOhneLehrer;
        v.VertreterId = null;
        v.Mehrarbeit = false;
    }

    /// <summary>Vertretung teilen: weitere Zeile für dieselbe Stunde (z. B. unterschiedliche Klassen).</summary>
    public static Vertretung Teilen(Schuldatei d, Vertretung v, string benutzer = "")
    {
        var n = new Vertretung
        {
            Id = d.NeueId(), Datum = v.Datum, Stunde = v.Stunde, Art = VertretungsArt.Teilvertretung, Quelle = VertretungsQuelle.Manuell,
            UnterrichtId = v.UnterrichtId, ZeilenIndex = v.ZeilenIndex, LehrerId = v.LehrerId, FachId = v.FachId, KlassenIds = v.KlassenIds.ToList(),
            VertretungsKlassenIds = v.NeueKlassenIds.ToList(), RaumId = v.RaumId, GrundId = v.GrundId, Grund = v.Grund, Bearbeitet = true,
        };
        if (v.Art == VertretungsArt.Vertretung) v.Art = VertretungsArt.Teilvertretung;
        Markieren(v, benutzer);
        d.Vertretungen.Add(n);
        return n;
    }

    /// <summary>
    /// Verlegt den Unterricht (alle aktiven Kopplungszeilen oder eine Zeile) von einer Position auf eine andere.
    /// An der Ursprungsposition entsteht ein Entfall ("(Le.) nach"), an der Zielposition eine Verlegung ("Vertr. von").
    /// Wird eine verlegte Stunde zurück an ihren Ursprung gezogen, wird die Verlegung aufgehoben.
    /// </summary>
    public static void Verlegen(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly vonDatum, int vonStunde, DateOnly nachDatum, int nachStunde,
        VertretungsArt zielArt = VertretungsArt.Verlegung, string benutzer = "")
    {
        if (vonDatum == nachDatum && vonStunde == nachStunde) return;
        var tpVon = Tagesplan.Berechnen(d, vonDatum);
        var eintraege = tpVon.Stunde(vonStunde).Where(e => e.Findetstatt && (e.Regel?.Unterricht.Id == unterrichtId || e.Vertretung?.UnterrichtId == unterrichtId) &&
            (zeilenIndex is null || (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeilenIndex)).ToList();
        if (eintraege.Count == 0) throw new InvalidOperationException("An dieser Position findet der Unterricht nicht statt.");
        var tpNach = Tagesplan.Berechnen(d, nachDatum);
        foreach (var e in eintraege)
        {
            var zeile = e.Regel?.ZeilenIndex ?? e.Vertretung!.ZeilenIndex;
            // bereits verlegte Stunde erneut verschieben
            if (e.Status == TagesStatus.Zusaetzlich && e.Vertretung is { Art: VertretungsArt.Verlegung or VertretungsArt.Tausch } ziel)
            {
                var quelle = ziel.VerlegungsId is int vid ? d.Vertretungen.FirstOrDefault(x => x != ziel && x.VerlegungsId == vid) : null;
                if (quelle != null && quelle.Datum == nachDatum && quelle.Stunde == nachStunde)
                {
                    d.Vertretungen.Remove(ziel);
                    d.Vertretungen.Remove(quelle);
                    continue;
                }
                ziel.Datum = nachDatum;
                ziel.Stunde = nachStunde;
                ziel.VertreterId = ZielLehrer(d, tpNach, ziel.LehrerId, nachStunde);
                ziel.VertretungsraumId = ZielRaum(tpNach, nachStunde, ziel.VertretungsraumId ?? ziel.RaumId, e.Unterricht, zeile, d);
                if (quelle != null) { quelle.NachDatum = nachDatum; quelle.NachStunde = nachStunde; }
                Markieren(ziel, benutzer);
                continue;
            }
            var lehrer = e.Regel?.LehrerId ?? e.Vertretung?.LehrerId;
            var vid2 = d.NeueId();
            // vorhandene Zeile an der Quelle (z. B. offene Vertretung) wird zum Entfall mit Wegverlegung
            var q = e.Vertretung ?? new Vertretung
            {
                Id = d.NeueId(), Datum = vonDatum, Stunde = vonStunde, UnterrichtId = unterrichtId, ZeilenIndex = zeile, LehrerId = lehrer,
                FachId = e.Regel?.FachId, KlassenIds = e.Regel?.KlassenIds.ToList() ?? e.KlassenIds.ToList(), RaumId = e.Regel?.RaumId,
            };
            if (e.Vertretung is null) d.Vertretungen.Add(q);
            var vertreterVorher = q.VertreterId;
            q.Art = VertretungsArt.Entfall;
            q.Quelle = IstAbgeleitet(q) ? q.Quelle : VertretungsQuelle.Verlegung;
            if (q.Quelle == VertretungsQuelle.Absenz && q.AbwesenheitId is null) q.Quelle = VertretungsQuelle.Verlegung;
            q.VertreterId = null;
            q.NachDatum = nachDatum;
            q.NachStunde = nachStunde;
            q.VerlegungsId = vid2;
            Markieren(q, benutzer);
            var z = new Vertretung
            {
                Id = d.NeueId(), Datum = nachDatum, Stunde = nachStunde, Art = zielArt, Quelle = VertretungsQuelle.Verlegung, UnterrichtId = unterrichtId,
                ZeilenIndex = zeile, LehrerId = lehrer, FachId = q.FachId, VertretungsfachId = q.VertretungsfachId, KlassenIds = q.KlassenIds.ToList(),
                RaumId = q.RaumId, VonDatum = vonDatum, VonStunde = vonStunde, VerlegungsId = vid2, Bearbeitet = true, Benutzer = benutzer,
                Schuelergruppe = q.Schuelergruppe,
            };
            z.VertreterId = ZielLehrer(d, tpNach, lehrer, nachStunde) ?? (vertreterVorher is int vv && LehrerBelegt(d, tpNach, vv, nachStunde) is null ? vv : null);
            z.VertretungsraumId = ZielRaum(tpNach, nachStunde, q.VertretungsraumId ?? q.RaumId, e.Unterricht, zeile, d);
            d.Vertretungen.Add(z);
        }
    }

    private static int? ZielLehrer(Schuldatei d, Tagesplan tp, int? lehrer, int stunde) =>
        lehrer is int l && !Absenzen.LehrerAbwesend(d, l, tp.Datum, stunde) ? l : null;

    private static int? ZielRaum(Tagesplan tp, int stunde, int? wunsch, Unterricht? u, int zeile, Schuldatei d)
    {
        var belegt = tp.Stunde(stunde).Where(e => e.Findetstatt).SelectMany(e => e.RaumIds).ToHashSet();
        if (wunsch is int w && !belegt.Contains(w) && !Absenzen.RaumGesperrt(d, w, tp.Datum, stunde)) return w;
        if (u != null && zeile < u.Zeilen.Count)
        {
            var ix = new DatenIndex(d);
            foreach (var r in ix.RaumKandidaten(u.Zeilen[zeile]))
                if (!belegt.Contains(r) && !Absenzen.RaumGesperrt(d, r, tp.Datum, stunde)) return r;
        }
        return wunsch;
    }

    /// <summary>Stundentausch zweier Unterrichtsstunden (auch über Tage hinweg).</summary>
    public static void Tauschen(Schuldatei d, int uA, DateOnly datumA, int stundeA, int uB, DateOnly datumB, int stundeB, string benutzer = "")
    {
        Verlegen(d, uA, null, datumA, stundeA, datumB, stundeB, VertretungsArt.Tausch, benutzer);
        // B nach A: der Ursprung von B liegt an der alten Position (nach der ersten Verlegung unverändert)
        Verlegen(d, uB, null, datumB, stundeB, datumA, stundeA, VertretungsArt.Tausch, benutzer);
    }

    /// <summary>Stunde entfallen lassen (Stunde in die Lupe ziehen bzw. Entfall im Planungsdialog).</summary>
    public static void Entfallen(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde, string benutzer = "")
    {
        var tp = Tagesplan.Berechnen(d, datum);
        foreach (var e in tp.Stunde(stunde).Where(e => e.Findetstatt && (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == unterrichtId &&
                     (zeilenIndex is null || (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeilenIndex)).ToList())
        {
            if (e.Status == TagesStatus.Zusaetzlich && e.Vertretung != null)
            {
                if (e.Vertretung.VerlegungsId is int vid)
                    foreach (var q in d.Vertretungen.Where(x => x.VerlegungsId == vid && x != e.Vertretung).ToList())
                    {
                        q.NachDatum = null;
                        q.NachStunde = null;
                        q.VerlegungsId = null;
                    }
                d.Vertretungen.Remove(e.Vertretung);
                continue;
            }
            var v = e.Vertretung;
            if (v is null)
            {
                v = new Vertretung
                {
                    Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = unterrichtId, ZeilenIndex = e.Regel!.ZeilenIndex, LehrerId = e.Regel.LehrerId,
                    FachId = e.Regel.FachId, KlassenIds = e.Regel.KlassenIds.ToList(), RaumId = e.Regel.RaumId, Quelle = VertretungsQuelle.Manuell,
                };
                d.Vertretungen.Add(v);
            }
            v.Art = VertretungsArt.Entfall;
            v.VertreterId = null;
            Markieren(v, benutzer);
        }
    }

    /// <summary>Raum einer Stunde ändern (erzeugt eine Raumvertretung). Optional zusätzlicher Raum.</summary>
    public static void RaumAendern(Schuldatei d, int unterrichtId, int zeilenIndex, DateOnly datum, int stunde, int? raumId, bool zusaetzlich, string benutzer = "")
    {
        var tp = Tagesplan.Berechnen(d, datum);
        var e = tp.Stunde(stunde).FirstOrDefault(x => x.Findetstatt && (x.Regel?.Unterricht.Id ?? x.Vertretung?.UnterrichtId) == unterrichtId &&
            (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex) == zeilenIndex) ?? throw new InvalidOperationException("Stunde nicht gefunden.");
        var v = e.Vertretung;
        if (v is null)
        {
            v = new Vertretung
            {
                Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = unterrichtId, ZeilenIndex = zeilenIndex, LehrerId = e.Regel!.LehrerId,
                VertreterId = e.Regel.LehrerId, FachId = e.Regel.FachId, KlassenIds = e.Regel.KlassenIds.ToList(), RaumId = e.Regel.RaumId,
                Art = VertretungsArt.Raumvertretung, Quelle = VertretungsQuelle.Manuell,
            };
            d.Vertretungen.Add(v);
        }
        if (zusaetzlich && raumId is int z) { if (!v.ZusatzraumIds.Contains(z)) v.ZusatzraumIds.Add(z); }
        else v.VertretungsraumId = raumId;
        Markieren(v, benutzer);
    }

    /// <summary>
    /// Sondereinsatz (zusätzlicher Unterricht) für einen Stundenbereich. Kollidierender Unterricht der Lehrer wird
    /// laut Vertretungszeitraster zur offenen Vertretung bzw. zum Entfall, kollidierender Unterricht der Klassen entfällt.
    /// </summary>
    public static List<Vertretung> Sondereinsatz(Schuldatei d, DateOnly datum, int stundeVon, int stundeBis, IReadOnlyList<int> klassenIds, IReadOnlyList<int> lehrerIds,
        int? fachId, int? raumId, string text, string schuelergruppe = "", int? vormerkungId = null, string benutzer = "")
    {
        var neu = new List<Vertretung>();
        var tag = Kalender.TagIndex(d, datum);
        for (var s = stundeVon; s <= Math.Max(stundeVon, stundeBis); s++)
        {
            var tp = Tagesplan.Berechnen(d, datum);
            foreach (var e in tp.Stunde(s).Where(e => e.Findetstatt && e.Regel != null && e.Status != TagesStatus.Zusaetzlich).ToList())
            {
                var lehrerBetroffen = e.LehrerIds.Intersect(lehrerIds).Any();
                var klasseBetroffen = e.KlassenIds.Intersect(klassenIds).Any();
                if (!lehrerBetroffen && !klasseBetroffen) continue;
                var v = e.Vertretung;
                if (v is null)
                {
                    v = new Vertretung
                    {
                        Id = d.NeueId(), Datum = datum, Stunde = s, UnterrichtId = e.Regel!.Unterricht.Id, ZeilenIndex = e.Regel.ZeilenIndex, LehrerId = e.Regel.LehrerId,
                        FachId = e.Regel.FachId, KlassenIds = e.Regel.KlassenIds.ToList(), RaumId = e.Regel.RaumId, Quelle = VertretungsQuelle.Sondereinsatz,
                    };
                    d.Vertretungen.Add(v);
                }
                v.Art = klasseBetroffen ? VertretungsArt.Entfall : Regel(d, e.KlassenIds, tag, s) == VertretungsRegel.Entfall ? VertretungsArt.Entfall : VertretungsArt.Vertretung;
                v.VertreterId = null;
                v.Text = "wegen Sondereinsatz";
                Markieren(v, benutzer);
            }
            var lehrer = lehrerIds.Count == 0 ? new List<int?> { null } : lehrerIds.Select(x => (int?)x).ToList();
            for (var i = 0; i < lehrer.Count; i++)
            {
                var v = new Vertretung
                {
                    Id = d.NeueId(), Datum = datum, Stunde = s, Art = vormerkungId.HasValue ? VertretungsArt.SondereinsatzVormerkung : VertretungsArt.Sondereinsatz,
                    Quelle = VertretungsQuelle.Sondereinsatz, VertreterId = lehrer[i], FachId = fachId, VertretungsfachId = fachId, KlassenIds = klassenIds.ToList(),
                    VertretungsraumId = raumId, Text = text, Schuelergruppe = schuelergruppe, VormerkungId = vormerkungId, Bearbeitet = true, Benutzer = benutzer,
                    Mehrarbeit = lehrer[i] is int l && !HatBereitschaft(d, tp, l, s),
                };
                d.Vertretungen.Add(v);
                neu.Add(v);
            }
        }
        return neu;
    }

    /// <summary>Unterricht einer Stunde ändern (Fach, Lehrer, Klassen, Raum) – Vertretungsart "Unterricht geändert" bzw. Sondereinsatz.</summary>
    public static void StundeAendern(Schuldatei d, int unterrichtId, int zeilenIndex, DateOnly datum, int stunde, int? lehrerId, int? fachId,
        IReadOnlyList<int> klassenIds, int? raumId, string text, string benutzer = "")
    {
        var tp = Tagesplan.Berechnen(d, datum);
        var e = tp.Stunde(stunde).FirstOrDefault(x => x.Findetstatt && (x.Regel?.Unterricht.Id ?? x.Vertretung?.UnterrichtId) == unterrichtId &&
            (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex) == zeilenIndex) ?? throw new InvalidOperationException("Stunde nicht gefunden.");
        var v = e.Vertretung;
        if (v is null)
        {
            v = new Vertretung
            {
                Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = unterrichtId, ZeilenIndex = zeilenIndex, LehrerId = e.Regel!.LehrerId,
                FachId = e.Regel.FachId, KlassenIds = e.Regel.KlassenIds.ToList(), RaumId = e.Regel.RaumId, Quelle = VertretungsQuelle.Manuell,
            };
            d.Vertretungen.Add(v);
        }
        var lehrerNeu = lehrerId != v.LehrerId;
        // neue Klassen verlieren ihren Unterricht dieser Stunde
        foreach (var k in klassenIds.Except(v.KlassenIds))
            foreach (var x in tp.Stunde(stunde).Where(x => x.Findetstatt && x.Regel != null && x.Vertretung != v && x.KlassenIds.Contains(k)).ToList())
                Entfallen(d, x.Regel!.Unterricht.Id, x.Regel.ZeilenIndex, datum, stunde, benutzer);
        v.VertreterId = lehrerId;
        v.VertretungsfachId = fachId;
        v.VertretungsKlassenIds = klassenIds.SequenceEqual(v.KlassenIds) ? null : klassenIds.ToList();
        v.VertretungsraumId = raumId;
        v.Text = text;
        v.Art = lehrerNeu ? VertretungsArt.Vertretung : raumId != v.RaumId && fachId == v.FachId && v.VertretungsKlassenIds is null ? VertretungsArt.Raumvertretung : VertretungsArt.UnterrichtGeaendert;
        if (lehrerNeu && lehrerId is int l) VertreterSetzen(d, v, l, false, benutzer);
        Markieren(v, benutzer);
    }

    /// <summary>Vertretung bzw. Entfall als Vormerkung übernehmen (Stunde wird später nachgeholt).</summary>
    public static Vormerkung AlsVormerkung(Schuldatei d, Vertretung v, string benutzer = "")
    {
        var vm = new Vormerkung
        {
            Id = d.NeueId(), Quelle = VormerkungsQuelle.Vertretung, Stunden = 1, FachId = v.FachId, LehrerIds = v.LehrerId is int l ? new() { l } : new(),
            KlassenIds = v.KlassenIds.ToList(), RaumId = v.RaumId, UnterrichtId = v.UnterrichtId, Datum = v.Datum, Stunde = v.Stunde,
            Text = $"aus Vertretung {v.Id} ({Format.Datum(v.Datum)}, {v.Stunde}. Std.)",
        };
        d.Vormerkungen.Add(vm);
        v.Art = VertretungsArt.Vormerkung;
        v.VertreterId = null;
        v.VormerkungId = vm.Id;
        Markieren(v, benutzer);
        return vm;
    }

    /// <summary>Vormerkung an einer Position verplanen (Sondereinsatz zu Vormerkung).</summary>
    public static Vertretung VormerkungVerplanen(Schuldatei d, Vormerkung vm, DateOnly datum, int stunde, string benutzer = "")
    {
        if (vm.Offen <= 0) throw new InvalidOperationException("Alle Stunden der Vormerkung sind bereits verplant.");
        var v = Sondereinsatz(d, datum, stunde, stunde, vm.KlassenIds, vm.LehrerIds, vm.FachId, vm.RaumId, vm.Text, "", vm.Id, benutzer).First();
        vm.VerplantAm.Add(datum);
        return v;
    }

    /// <summary>Vertretungsfähigkeit für Drag &amp; Drop im tagesaktuellen Stundenplan.</summary>
    public static Planung.Feldstatus Feldstatus(Schuldatei d, Tagesplan ziel, IReadOnlyList<int> lehrerIds, IReadOnlyList<int> klassenIds, int stunde, int unterrichtId, int? raumId)
    {
        if (!d.Zeitraster.Verfuegbar(ziel.Tag, stunde) || !Kalender.IstUnterrichtstag(d, ziel.Datum)) return Planung.Feldstatus.Kollision;
        var hindernisse = ziel.Stunde(stunde).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) != unterrichtId &&
            (e.LehrerIds.Intersect(lehrerIds).Any() || e.KlassenIds.Intersect(klassenIds).Any())).ToList();
        if (hindernisse.Count > 0)
            return hindernisse.Select(h => h.Unterricht?.Id).Distinct().Count() == 1 && hindernisse[0].Unterricht != null ? Planung.Feldstatus.TauschMoeglich : Planung.Feldstatus.Kollision;
        if (lehrerIds.Any(l => Absenzen.LehrerAbwesend(d, l, ziel.Datum, stunde))) return Planung.Feldstatus.VorgabeVerletzt;
        if (raumId is int r && (ziel.Stunde(stunde).Any(e => e.Findetstatt && e.RaumIds.Contains(r) && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) != unterrichtId) || Absenzen.RaumGesperrt(d, r, ziel.Datum, stunde)))
            return Planung.Feldstatus.KeinRaum;
        return Planung.Feldstatus.Frei;
    }

    // ----------------------------------------------------------------------------------------------
    // Zähler (Kurzform; Details siehe Zaehlung)
    // ----------------------------------------------------------------------------------------------

    public static readonly VertretungsArt[] ZaehlendeArten =
    {
        VertretungsArt.Vertretung, VertretungsArt.StattVertretung, VertretungsArt.Betreuung, VertretungsArt.Sondereinsatz, VertretungsArt.Pausenaufsicht,
        VertretungsArt.Teilvertretung, VertretungsArt.Lehrertausch, VertretungsArt.Bereitschaftsvertretung, VertretungsArt.SondereinsatzVormerkung, VertretungsArt.Klausur,
    };

    /// <summary>
    /// Vertretungen, deren eingeplanter Vertreter durch die Absenz abwesend wird (Lehrer-Absenz bzw. Begleitlehrer einer
    /// Klassen-Absenz). Diese werden bei der Neuberechnung wieder offen – der Planer muss informiert werden.
    /// </summary>
    public static List<Vertretung> BetroffeneVertreter(Schuldatei d, Abwesenheit a)
    {
        var lehrer = a.Art switch
        {
            AbwesenheitsArt.Lehrer => new HashSet<int> { a.ElementId },
            AbwesenheitsArt.Klasse => a.BegleitLehrerIds.ToHashSet(),
            _ => new HashSet<int>(),
        };
        return d.Vertretungen.Where(v => v.VertreterId is int l && lehrer.Contains(l) && v.LehrerId != l && !v.IstEntfall && a.Deckt(v.Datum, v.Stunde))
            .OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
    }

    /// <summary>Anzahl gehaltener Vertretungen im Zeitraum (für Höchstwerte pro Woche/Monat).</summary>
    public static int Zaehler(Schuldatei d, int lehrerId, DateOnly von, DateOnly bis) =>
        d.Vertretungen.Count(v => v.VertreterId == lehrerId && v.Datum >= von && v.Datum <= bis && v.VertreterId != v.LehrerId &&
                                  v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung or VertretungsArt.Teilvertretung or VertretungsArt.Lehrertausch
                                      or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Sondereinsatz);
}

/// <summary>Ein Kandidat im Vertretungsvorschlag.</summary>
public sealed class Kandidat
{
    public Lehrer Lehrer { get; init; } = null!;
    /// <summary>Schlechtpunkte (je niedriger, desto geeigneter).</summary>
    public double Punkte { get; set; }
    public string Merker { get; set; } = "";
    public string Stundenplan { get; set; } = "";
    public int UnterrichtAmTag { get; set; }
    public int Zeitwunsch { get; set; }
    public bool Bereitschaft { get; set; }
    public bool Freigesetzt { get; set; }
    public bool KenntKlasse { get; set; }
    public bool Klassenlehrer { get; set; }
    public bool Fachlehrer { get; set; }
    public bool Vorwoche { get; set; }
    public decimal Zaehler { get; set; }
    public int Vertretungen { get; set; }
    public int Entfaelle { get; set; }
    public decimal Jahreszaehler { get; set; }
    public int Bezahlt { get; set; }
    public int Betreuungen { get; set; }
    public int ZaehlerWoche { get; set; }
    public int ZaehlerMonat { get; set; }
    public int ZaehlerJahr { get; set; }
    public decimal Saldo { get; set; }
    public bool Benachrichtigen { get; set; }
    public bool Rot { get; set; }
    public List<string> Kriterien { get; } = new();
    public string Lage { get; set; } = "";
    public bool Mehrarbeit => !Bereitschaft && !Freigesetzt;
}

public sealed record Betreuungskandidat(Lehrer Lehrer, string Klassen, string Fach, string Raum, string Gang, bool Kopplung, decimal Zaehler);

public sealed record Raumkandidat(Raum Raum, bool Ausweichraum, bool AusweichStammraum, string? BesetztDurch, string Raumgruppen, string Gang, int KapazitaetDifferenz);

public sealed record Vorziehung(TagesEintrag Eintrag, DateOnly Datum, int Stunde, string Beschreibung);

public enum AutomatikMethode
{
    BesterVorschlag,
    BereitschaftUndFreigesetzteZuerst,
    VorziehungenZuerst,
    Pausenaufsichten,
}

/// <summary>Vertretungsvorschlag, Betreuungs-, Raum- und Vorziehungsvorschlag sowie Vertretungsautomatik.</summary>
public static class VertretungsVorschlag
{
    // Ältere Gewichtung (Schlüssel V.*) – wird auf die neuen Einstellungen abgebildet
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

    /// <summary>Vertretungsvorschlag: alle verfügbaren Lehrer, gereiht nach Schlechtpunkten.</summary>
    public static List<Kandidat> Berechnen(Schuldatei d, Vertretung v, bool stundenblock = false, bool alleAnzeigen = false)
    {
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var e = d.Vertretungseinstellungen.Vorschlag;
        var tag = Kalender.TagIndex(d, v.Datum);
        var stunden = new List<int> { v.Stunde };
        if (stundenblock) stunden = Block(d, v).Select(x => x.Stunde).ToList();
        var (zVon, zBis) = Zaehlung.Zeitraum(d, v.Datum);
        var res = new List<Kandidat>();
        var klassen = v.NeueKlassenIds;
        var montag = Kalender.Montag(v.Datum);
        var monat = new DateOnly(v.Datum.Year, v.Datum.Month, 1);
        foreach (var l in d.Lehrer.Where(l => !l.Ignorieren && l.Id != v.LehrerId))
        {
            if (stunden.Any(s => Vertretungen.LehrerBelegt(d, tp, l.Id, s, v) != null)) continue;
            if (l.Sperrvermerk >= 9 || l.KeineVertretung) { if (!alleAnzeigen) continue; }
            var zw = tag >= 0 ? stunden.Min(s => l.Zeitwuensche.Get(tag, s)) : 0;
            if (zw <= -3 && e.GesperrteNichtAnzeigen && !alleAnzeigen) continue;
            var k = new Kandidat { Lehrer = l, Zeitwunsch = zw };
            k.Merker = Vertretungen.Merker(d, tp, l.Id, v.Stunde);
            k.Freigesetzt = k.Merker == "x";
            k.Bereitschaft = Vertretungen.HatBereitschaft(d, tp, l.Id, v.Stunde);
            k.UnterrichtAmTag = Vertretungen.UnterrichtAmTag(d, tp, l.Id);
            k.Stundenplan = StundenplanText(d, tp, l.Id, v.Stunde);
            k.KenntKlasse = klassen.Count > 0 && d.Unterrichte.Any(u => u.Zeilen.Any(z => z.LehrerId == l.Id &&
                (e.KlasseAuchFuerKlassenteil ? z.KlassenIds.Intersect(klassen).Any() : klassen.All(z.KlassenIds.Contains))));
            k.Klassenlehrer = d.Klassen.Any(c => klassen.Contains(c.Id) && c.KlassenlehrerIds.Contains(l.Id));
            k.Fachlehrer = v.FachId is int f && (l.FaecherIds.Contains(f) || d.Unterrichte.Any(u => u.Zeilen.Any(z => z.LehrerId == l.Id && z.FachId == f)));
            k.Vorwoche = d.Vertretungen.Any(x => x.VertreterId == l.Id && x.Datum >= v.Datum.AddDays(-7) && x.Datum < v.Datum && x.KlassenIds.Intersect(klassen).Any());
            var z = Zaehlung.Lehrer(d, l, zVon, zBis);
            k.Zaehler = z.Summe;
            k.Vertretungen = z.Vertretungen;
            k.Entfaelle = z.Entfaelle;
            k.Bezahlt = z.Bezahlt;
            k.Betreuungen = z.Betreuungen;
            k.Jahreszaehler = Zaehlung.Lehrer(d, l, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis).Summe;
            k.ZaehlerWoche = Vertretungen.Zaehler(d, l.Id, montag, montag.AddDays(6));
            k.ZaehlerMonat = Vertretungen.Zaehler(d, l.Id, monat, monat.AddMonths(1).AddDays(-1));
            k.ZaehlerJahr = Vertretungen.Zaehler(d, l.Id, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis);
            k.Saldo = k.Jahreszaehler;
            var vorher = tp.VonLehrer(l.Id).Where(x => x.Stunde < v.Stunde).Select(x => x.Stunde).DefaultIfEmpty(0).Max();
            k.Benachrichtigen = vorher == 0 && k.UnterrichtAmTag > 0 && v.Datum == DateOnly.FromDateTime(DateTime.Today);
            var merker = Vertretungen.MerkerWert(k.Merker);
            k.Lage = k.Freigesetzt ? "freigesetzt" : k.Bereitschaft ? "Bereitschaft" : merker == 0 ? "Hohlstunde" : merker == 9 ? "sonst frei" : merker == 1 ? "anschließend" : $"Abstand {merker}";
            if ((l.VertretungenMaxWoche > 0 && k.ZaehlerWoche >= l.VertretungenMaxWoche) || (l.VertretungenMaxMonat > 0 && k.ZaehlerMonat >= l.VertretungenMaxMonat))
            {
                k.Kriterien.Add("Maximum erreicht");
                k.Punkte += 200;
            }
            k.Punkte += merker * e.MerkerFaktor + l.Sperrvermerk * e.SperreFaktor + (double)k.Zaehler * e.ZaehlerFaktor + k.UnterrichtAmTag * e.UnterrichtsstundenAmTag;
            if (!string.IsNullOrEmpty(l.Statistik) && e.StatistikPunkte.TryGetValue(l.Statistik, out var sp)) k.Punkte += sp;
            if (k.Freigesetzt) { k.Punkte -= e.Freisetzung; k.Kriterien.Add("freigesetzt"); }
            if (k.Bereitschaft) { k.Punkte -= e.Bereitschaft; k.Kriterien.Add("Bereitschaft"); }
            if (k.KenntKlasse) { k.Punkte -= e.HatKlasse; k.Kriterien.Add(k.Klassenlehrer ? "Klassenlehrer" : "kennt Klasse"); }
            if (k.Fachlehrer) { k.Punkte -= e.HatFach; k.Kriterien.Add("Fach"); }
            if (zw < 0) { k.Punkte += -zw * 15; k.Kriterien.Add($"Zeitwunsch {zw}"); }
            if (!l.StundenProTag.IstLeer && k.UnterrichtAmTag + 1 > l.StundenProTag.Max) { k.Punkte += 25; k.Kriterien.Add("Std./Tag überschritten"); }
            if (l.Sperrvermerk > 0) k.Kriterien.Add($"Sperrvermerk {l.Sperrvermerk}");
            if (merker > e.NichtAnzeigenWennMerkerGroesser && !alleAnzeigen) continue;
            k.Rot = merker > e.RotWennMerkerGroesser || k.Zaehler > e.RotWennZaehlerGroesser || l.Sperrvermerk >= 9;
            res.Add(k);
        }
        return res.OrderBy(x => x.Punkte).ThenBy(x => x.Lehrer.Kurzname).ToList();
    }

    /// <summary>Stunden des Stundenblocks (Doppelstunde/Block) der Vertretung am selben Tag.</summary>
    public static List<Vertretung> Block(Schuldatei d, Vertretung v)
    {
        var res = new List<Vertretung> { v };
        if (v.UnterrichtId is not int u) return res;
        var tag = d.Vertretungen.Where(x => x.Datum == v.Datum && x.UnterrichtId == u && x.ZeilenIndex == v.ZeilenIndex && x.Art == v.Art).ToList();
        for (var s = v.Stunde - 1; tag.FirstOrDefault(x => x.Stunde == s) is { } x; s--) res.Insert(0, x);
        for (var s = v.Stunde + 1; tag.FirstOrDefault(x => x.Stunde == s) is { } x; s++) res.Add(x);
        return res;
    }

    /// <summary>Benachbarte Stunden des Lehrers (Spalte "Stundenplan").</summary>
    public static string StundenplanText(Schuldatei d, Tagesplan tp, int lehrerId, int stunde, int umfang = 2)
    {
        var ix = new DatenIndex(d);
        var teile = new List<string>();
        for (var s = Math.Max(1, stunde - umfang); s <= Math.Min(d.Zeitraster.StundenProTag, stunde + umfang); s++)
        {
            var e = tp.VonLehrer(lehrerId).FirstOrDefault(x => x.Stunde == s);
            var t = e is null ? "·" : (ix.KFach(e.FachId) is { Length: > 0 } f ? f : ix.KKlassen(e.KlassenIds));
            teile.Add(s == stunde ? $"[{(e is null ? " " : t)}]" : t);
        }
        return string.Join(" ", teile);
    }

    /// <summary>Betreuungsvorschlag: Lehrer, die zur Stunde selbst unterrichten.</summary>
    public static List<Betreuungskandidat> Betreuungen(Schuldatei d, Vertretung v)
    {
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var ix = new DatenIndex(d);
        var (zVon, zBis) = Zaehlung.Zeitraum(d, v.Datum);
        var res = new List<Betreuungskandidat>();
        foreach (var e in tp.Stunde(v.Stunde).Where(e => e.Findetstatt && e.LehrerIds.Count > 0 && e.Vertretung != v && !Vertretungen.IstBereitschaftsfach(d, e.FachId)))
            foreach (var l in e.LehrerIds.Where(l => l != v.LehrerId && !Absenzen.LehrerAbwesend(d, l, v.Datum, v.Stunde)))
            {
                if (res.Any(r => r.Lehrer.Id == l) || !ix.Lehrer.TryGetValue(l, out var le)) continue;
                var raum = e.RaumIds.FirstOrDefault();
                var gang = raum != 0 && ix.Raeume.TryGetValue(raum, out var r) && r.GangId is int g ? ix.K(ElementTyp.Gang, g) : "";
                var kopplung = e.Unterricht?.Id == v.UnterrichtId;
                res.Add(new Betreuungskandidat(le, ix.KKlassen(e.KlassenIds), ix.KFach(e.FachId), raum == 0 ? "" : ix.KRaum(raum), gang, kopplung,
                    Zaehlung.Lehrer(d, le, zVon, zBis).Summe));
            }
        return res.OrderByDescending(r => r.Kopplung).ThenBy(r => r.Zaehler).ToList();
    }

    /// <summary>Raumvorschlag: freie Räume (optional auch besetzte) für die Stunde(n).</summary>
    public static List<Raumkandidat> Raeume(Schuldatei d, Vertretung v, bool auchBesetzte = false, bool stundenblock = false)
    {
        var ix = new DatenIndex(d);
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var stunden = stundenblock ? Block(d, v).Select(x => x.Stunde).ToList() : new List<int> { v.Stunde };
        var wunsch = v.RaumId;
        var ring = wunsch is int w ? Ring(ix, w) : new HashSet<int>();
        var stammraum = v.KlassenIds.Select(k => ix.Klassen.GetValueOrDefault(k)?.StammraumId).FirstOrDefault(x => x.HasValue);
        var ringSr = stammraum is int sr ? Ring(ix, sr) : new HashSet<int>();
        var kap = wunsch is int ww && ix.Raeume.TryGetValue(ww, out var wr) ? wr.Kapazitaet : 0;
        var res = new List<Raumkandidat>();
        foreach (var r in d.Raeume.Where(r => !r.Ignorieren))
        {
            if (stunden.Any(s => Absenzen.RaumGesperrt(d, r.Id, v.Datum, s))) continue;
            var besetzt = stunden.SelectMany(s => tp.Stunde(s)).FirstOrDefault(e => e.Findetstatt && e.Vertretung != v && e.RaumIds.Contains(r.Id) &&
                !(e.Regel?.Unterricht.Id == v.UnterrichtId && e.Regel?.ZeilenIndex == v.ZeilenIndex));
            if (besetzt != null && !auchBesetzte) continue;
            var gruppen = string.Join(",", d.Raumgruppen.Where(g => g.RaumIds.Contains(r.Id)).Select(g => g.Kurzname));
            res.Add(new Raumkandidat(r, ring.Contains(r.Id), ringSr.Contains(r.Id),
                besetzt is null ? null : $"{ix.KKlassen(besetzt.KlassenIds)} {ix.KFach(besetzt.FachId)}".Trim(), gruppen,
                r.GangId is int g ? ix.K(ElementTyp.Gang, g) : "", kap > 0 && r.Kapazitaet > 0 ? r.Kapazitaet - kap : 0));
        }
        return res.OrderBy(r => r.BesetztDurch != null).ThenByDescending(r => r.Ausweichraum).ThenByDescending(r => r.AusweichStammraum).ThenBy(r => r.Raum.Kurzname).ToList();
    }

    private static HashSet<int> Ring(DatenIndex ix, int raum)
    {
        var res = new HashSet<int>();
        var x = raum;
        for (var i = 0; i < 50 && ix.Raeume.TryGetValue(x, out var r) && r.AusweichraumId is int a && res.Add(a); i++) x = a;
        return res;
    }

    /// <summary>Vorziehungsvorschlag: Unterricht der Klasse aus späteren Stunden (dieselbe und nächste Woche), der in die offene Stunde gezogen werden kann.</summary>
    public static List<Vorziehung> Vorziehungen(Schuldatei d, Vertretung v, VorziehungsModus? modus = null)
    {
        var res = new List<Vorziehung>();
        if (v.KlassenIds.Count == 0) return res;
        var e = d.Vertretungseinstellungen.Vorschlag;
        var m = modus ?? e.Vorziehungen;
        var ix = new DatenIndex(d);
        var tpZiel = Tagesplan.Berechnen(d, v.Datum);
        // Klasse muss in der Stunde sonst frei sein
        if (tpZiel.Stunde(v.Stunde).Any(x => x.Findetstatt && x.Vertretung != v && x.KlassenIds.Intersect(v.KlassenIds).Any())) return res;
        var ende = e.NurDieselbeWoche ? Kalender.Montag(v.Datum).AddDays(6) : Kalender.Montag(v.Datum).AddDays(13);
        var start = e.NurSpaeterenUnterricht ? v.Datum : Kalender.Montag(v.Datum);
        foreach (var t in Kalender.Tage(start, ende).Where(t => Kalender.IstUnterrichtstag(d, t)))
        {
            var tp = t == v.Datum ? tpZiel : Tagesplan.Berechnen(d, t);
            var tag = Kalender.TagIndex(d, t);
            foreach (var c in tp.Eintraege.Where(x => x.Status == TagesStatus.Regulaer && x.Regel != null && x.KlassenIds.Count > 0 && v.KlassenIds.All(x.KlassenIds.Contains)))
            {
                if (t == v.Datum && c.Stunde == v.Stunde) continue;
                if ((t < v.Datum || (t == v.Datum && c.Stunde < v.Stunde)) && e.NurSpaeterenUnterricht) continue;
                if (c.Regel!.Planstunde.Fixiert) continue;
                if (Vertretungen.IstBereitschaftsfach(d, c.FachId)) continue;
                if (c.Unterricht!.Id == v.UnterrichtId) continue;
                // Lehrer des Unterrichts muss zur offenen Stunde frei sein
                if (c.LehrerIds.Any(l => Vertretungen.LehrerBelegt(d, tpZiel, l, v.Stunde) != null)) continue;
                // alle Zeilen des Unterrichts müssen verlegt werden können
                var zeilen = tp.Stunde(c.Stunde).Where(x => x.Regel?.Unterricht.Id == c.Unterricht.Id).ToList();
                if (zeilen.Any(z => z.Status != TagesStatus.Regulaer)) continue;
                if (zeilen.SelectMany(z => z.LehrerIds).Any(l => Vertretungen.LehrerBelegt(d, tpZiel, l, v.Stunde) != null)) continue;
                if (zeilen.SelectMany(z => z.KlassenIds).Except(v.KlassenIds).Any(k => tpZiel.Stunde(v.Stunde).Any(x => x.Findetstatt && x.Vertretung != v && x.KlassenIds.Contains(k)))) continue;
                if (e.Raumgewicht4Beachten && c.RaumIds.FirstOrDefault() is int rr && rr != 0 && ix.Raeume.TryGetValue(rr, out var raum) && raum.Raumgewicht >= 4 &&
                    tpZiel.Stunde(v.Stunde).Any(x => x.Findetstatt && x.Vertretung != v && x.RaumIds.Contains(rr))) continue;
                var klassenStunden = tp.Eintraege.Where(x => x.Findetstatt && x.KlassenIds.Intersect(v.KlassenIds).Any()).Select(x => x.Stunde).Distinct().ToList();
                var rand = klassenStunden.Count > 0 && (c.Stunde == klassenStunden.Max() || c.Stunde == klassenStunden.Min());
                var ok = m switch
                {
                    VorziehungsModus.GemaessVertretungszeitraster => rand && Vertretungen.Regel(d, c.KlassenIds, tag, c.Stunde) == VertretungsRegel.Entfall,
                    VorziehungsModus.AusRandstunden => rand,
                    _ => true,
                };
                if (!ok) continue;
                res.Add(new Vorziehung(c, t, c.Stunde,
                    $"{Planung.Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {Format.Datum(t)} {c.Stunde}. Std.: {ix.KFach(c.FachId)} {string.Join(",", c.LehrerIds.Select(ix.KLehrer))} {ix.KKlassen(c.KlassenIds)}" +
                    (rand ? " (Randstunde)" : " (erzeugt neue Lücke)")));
            }
        }
        return res.OrderBy(x => x.Datum == v.Datum ? 0 : 1).ThenBy(x => x.Datum).ThenByDescending(x => x.Stunde).ToList();
    }

    /// <summary>Vorziehung durchführen: die offene Stunde entfällt, der vorgezogene Unterricht wird verlegt.</summary>
    public static void VorziehungAusfuehren(Schuldatei d, Vertretung offen, Vorziehung vz, string benutzer = "")
    {
        var uid = vz.Eintrag.Unterricht!.Id;
        offen.Art = VertretungsArt.Entfall;
        offen.VertreterId = null;
        offen.Bearbeitet = true;
        offen.Text = "Vorziehung";
        Vertretungen.Verlegen(d, uid, null, vz.Datum, vz.Stunde, offen.Datum, offen.Stunde, VertretungsArt.Verlegung, benutzer);
    }

    public static bool EntfallSinnvoll(Schuldatei d, Vertretung v)
    {
        var ab = Nrw.JahrgangRang(d.Einstellungen.EntfallAbJahrgang);
        if (v.KlassenIds.Count == 0) return false;
        var tp = Tagesplan.Berechnen(d, v.Datum);
        return v.KlassenIds.All(kid =>
        {
            var k = d.Klassen.FirstOrDefault(x => x.Id == kid);
            if (k is null || Nrw.JahrgangRang(k.Jahrgang) < ab) return false;
            var stunden = tp.Eintraege.Where(x => x.KlassenIds.Contains(kid) && (x.Findetstatt || x.Vertretung == v)).Select(x => x.Stunde).ToList();
            return stunden.Count > 0 && (v.Stunde == stunden.Min() || v.Stunde == stunden.Max());
        }) || Vertretungen.Regel(d, v.KlassenIds, Kalender.TagIndex(d, v.Datum), v.Stunde) == VertretungsRegel.Entfall;
    }

    /// <summary>Vertretungsautomatik: offene Vertretungen eines Tages nach der gewählten Methode bearbeiten.</summary>
    public static int Automatisch(Schuldatei d, DateOnly datum, AutomatikMethode methode = AutomatikMethode.BesterVorschlag, string benutzer = "")
    {
        Vertretungen.Aktualisieren(d, datum);
        var anzahl = 0;
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.Offen).OrderBy(v => v.Stunde).ToList())
        {
            if (!v.Offen) continue;
            if (v.Art == VertretungsArt.Pausenaufsicht)
            {
                var pk = Module.Pausenaufsichten.Kandidaten(d, datum, new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 }).FirstOrDefault();
                if (pk != null) { Vertretungen.VertreterSetzen(d, v, pk.Id, false, benutzer); anzahl++; }
                continue;
            }
            if (methode == AutomatikMethode.Pausenaufsichten) continue;
            if (methode == AutomatikMethode.VorziehungenZuerst && Vorziehungen(d, v).FirstOrDefault(x => x.Beschreibung.EndsWith("(Randstunde)")) is { } vz)
            {
                VorziehungAusfuehren(d, v, vz, benutzer);
                anzahl++;
                continue;
            }
            var k = Berechnen(d, v).FirstOrDefault(x => !x.Rot && (methode != AutomatikMethode.BereitschaftUndFreigesetzteZuerst || x.Bereitschaft || x.Freigesetzt))
                    ?? (methode == AutomatikMethode.BereitschaftUndFreigesetzteZuerst ? Berechnen(d, v).FirstOrDefault(x => !x.Rot) : null);
            if (k is null && EntfallSinnvoll(d, v))
            {
                Vertretungen.EntfallUmschalten(d, v, benutzer);
                anzahl++;
                continue;
            }
            if (k is null) continue;
            Vertretungen.VertreterSetzen(d, v, k.Lehrer.Id, false, benutzer);
            anzahl++;
        }
        return anzahl;
    }
}

public sealed record PlanZeile(DateOnly Datum, int Stunde, string Klassen, string Lehrer, string Vertreter, string Fach, string NeuesFach, string Raum, string NeuerRaum,
    string Art, string Text, IReadOnlyList<int> KlassenIds, int? LehrerId, int? VertreterId, string VertrVon = "", string Nach = "", string Grund = "", int Id = 0);

/// <summary>Ausgabe des Vertretungsplans (Aushang, Monitor, HTML, Portal).</summary>
public static class Vertretungsplan
{
    public static string ArtText(VertretungsArt a) => a switch
    {
        VertretungsArt.Vertretung => "Vertretung",
        VertretungsArt.Entfall => "Entfall",
        VertretungsArt.Raumvertretung => "Raum-Vtr.",
        VertretungsArt.Verlegung => "Verlegung",
        VertretungsArt.StattVertretung => "Statt-Vertretung",
        VertretungsArt.Betreuung => "Betreuung",
        VertretungsArt.Sondereinsatz => "Sondereinsatz",
        VertretungsArt.Freisetzung => "Freisetzung",
        VertretungsArt.Tausch => "Tausch",
        VertretungsArt.Pausenaufsicht => "Pausenaufsicht",
        VertretungsArt.Klausur => "Klausur",
        VertretungsArt.VertretungOhneLehrer => "Vertr. o. Lehrkraft",
        VertretungsArt.Bereitschaftsvertretung => "Bereitschaftsvtr.",
        VertretungsArt.BereitschaftEntfaellt => "Bereitschaft entfällt",
        VertretungsArt.EigenverantwortlichesArbeiten => "eigenverantw. Arbeiten",
        VertretungsArt.Lehrertausch => "Lehrertausch",
        VertretungsArt.Teilvertretung => "Teil-Vtr.",
        VertretungsArt.TrotzAbsenz => "Trotz Absenz",
        VertretungsArt.UnterrichtGeaendert => "Unterricht geändert",
        VertretungsArt.Veranstaltung => "Veranst.",
        VertretungsArt.SondereinsatzVormerkung => "Sondereins. zu Vormerkung",
        VertretungsArt.Vormerkung => "Vormerkung",
        _ => a.ToString(),
    };

    /// <summary>Bezeichnung laut Einstellungen ("Farben und Bezeichnungen"), sonst Standard.</summary>
    public static string ArtText(Schuldatei d, VertretungsArt a) =>
        d.Vertretungseinstellungen.Bezeichnungen.TryGetValue(a.ToString(), out var t) && !string.IsNullOrWhiteSpace(t) ? t : ArtText(a);

    public static string StundeText(Vertretung v) => v.Art == VertretungsArt.Pausenaufsicht ? $"{v.Stunde}/{v.Stunde + 1}" : v.Stunde.ToString();

    public static List<PlanZeile> Zeilen(Schuldatei d, DateOnly datum, bool mitNichtDrucken = false)
    {
        var ix = new DatenIndex(d);
        var nichtDruckFaecher = d.Faecher.Where(f => f.NichtDrucken).Select(f => f.Id).ToHashSet();
        return Vertretungen.Am(d, datum)
            .Where(v => mitNichtDrucken || (!v.NichtDrucken && !(v.FachId is int f && nichtDruckFaecher.Contains(f))))
            .Where(v => v.Art is not (VertretungsArt.Veranstaltung or VertretungsArt.Vormerkung) || mitNichtDrucken || v.VertreterId.HasValue)
            .Select(v => new PlanZeile(
                v.Datum, v.Stunde, ix.KKlassen(v.NeueKlassenIds), ix.KLehrer(v.LehrerId),
                v.IstEntfall || v.Art == VertretungsArt.Vormerkung ? "---" : v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten ? "+" :
                v.VertreterId.HasValue ? ix.KLehrer(v.VertreterId) : "???",
                ix.KFach(v.FachId), ix.KFach(v.VertretungsfachId ?? v.FachId),
                v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KRaum(v.RaumId),
                v.IstEntfall ? "" : v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) :
                string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => ix.KRaum(x))),
                ArtText(d, v.Art), v.Text, v.NeueKlassenIds, v.LehrerId, v.VertreterId,
                v.VonDatum.HasValue ? $"{Planung.Planungsdienst.TageKurz[((int)v.VonDatum.Value.DayOfWeek + 6) % 7]}-{v.VonDatum:dd.MM.} / {v.VonStunde}" : "",
                v.NachDatum.HasValue ? $"{Planung.Planungsdienst.TageKurz[((int)v.NachDatum.Value.DayOfWeek + 6) % 7]}-{v.NachDatum:dd.MM.} / {v.NachStunde}" : "",
                d.Abwesenheitsgruende.FirstOrDefault(g => g.Id == v.GrundId) is { Vertraulich: true } ? "" : v.Grund, v.Id))
            .OrderBy(z => z.Klassen, StringComparer.Create(Format.De, true)).ThenBy(z => z.Stunde).ToList();
    }
}
