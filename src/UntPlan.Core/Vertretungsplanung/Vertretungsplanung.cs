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

    /// <summary>
    /// Erscheint nicht in der Vertretungsliste: Bereitschaften (werden nie vertreten) und Unterricht, der nicht vertreten wird
    /// (Unterricht/Fach „keine Vertretung“ → automatischer Entfall, solange nicht von Hand bearbeitet).
    /// </summary>
    public static bool NichtInListe(Schuldatei d, Vertretung v)
    {
        if (v.Art == VertretungsArt.BereitschaftEntfaellt) return true;
        if (v.Art is VertretungsArt.Unterricht or VertretungsArt.Entfall && IstBereitschaftsfach(d, v.FachId)) return true;
        if (v.Art != VertretungsArt.Entfall || v.Bearbeitet || v.Quelle == VertretungsQuelle.Manuell || v.KlausurId.HasValue) return false;
        var fach = v.FachId is int f ? d.Faecher.FirstOrDefault(x => x.Id == f) : null;
        var u = v.UnterrichtId is int uid ? d.Unterrichte.FirstOrDefault(x => x.Id == uid) : null;
        return fach?.KeineVertretung == true || u?.KeineVertretung == true;
    }

    /// <summary>Vertretungszeitraster: Regel für eine Stunde (Klassenzeitraster vor globalem Zeitraster).</summary>
    public static VertretungsRegel Regel(Schuldatei d, IEnumerable<int> klassenIds, int tag, int stunde)
    {
        foreach (var k in klassenIds)
            if (d.Klassen.FirstOrDefault(x => x.Id == k) is { } kl && kl.VertretungsZeitraster.TryGetValue(tag * 100 + stunde, out var r))
                return r;
        return d.Vertretungseinstellungen.Regel(tag, stunde);
    }

    /// <summary>
    /// Nach einer Änderung von Unterricht oder Stundenplan: alle Tage ab <paramref name="ab"/> neu abgleichen, an denen Absenzen,
    /// Veranstaltungen, Klausuren oder Vertretungen bestehen – so wirkt z. B. eine geplante Klassenabwesenheit auch auf eine neu
    /// eingeplante Stunde (Freisetzung). Zeilen verschobener Stunden bleiben für die Nacherfassung (Umplanungsabgleich) stehen.
    /// </summary>
    public static int NachPlanaenderung(Schuldatei d, DateOnly ab)
    {
        var grenze = ab.AddDays(370);
        IEnumerable<DateOnly> Bereich(DateOnly von, DateOnly bis)
        {
            var v = von < ab ? ab : von;
            var b = bis > grenze ? grenze : bis;
            return v > b ? Enumerable.Empty<DateOnly>() : Kalender.Tage(v, b);
        }
        var tage = new SortedSet<DateOnly>();
        foreach (var a in d.Abwesenheiten) tage.UnionWith(Bereich(a.Von, a.Bis));
        foreach (var va in d.Veranstaltungen) tage.UnionWith(Bereich(va.Von, va.Bis));
        foreach (var k in d.Klausuren.Where(k => k.Aktiv && k.Datum >= ab && k.Datum <= grenze)) tage.Add(k.Datum);
        foreach (var v in d.Vertretungen.Where(v => v.Datum >= ab && v.Datum <= grenze)) tage.Add(v.Datum);
        var n = 0;
        foreach (var t in tage)
        {
            if (!Kalender.IstUnterrichtstag(d, t) && !d.Vertretungen.Any(v => v.Datum == t)) continue;
            Aktualisieren(d, t, entfernen: false);
            n++;
        }
        return n;
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
    public static void Aktualisieren(Schuldatei d, DateOnly datum) => Aktualisieren(d, datum, true);

    /// <param name="entfernen">false: Zeilen, deren Ursache nicht mehr besteht (z. B. verschobene Stunde), bleiben stehen – die Nacherfassung
    /// beim Wechsel in den Vertretungsmodus überträgt bzw. entfernt sie; neue Zeilen (z. B. Freisetzung einer neu eingeplanten Stunde) kommen hinzu</param>
    public static void Aktualisieren(Schuldatei d, DateOnly datum, bool entfernen)
    {
        // ältere Dateien: offene „Bereitschaftsvertretungen“ gibt es nicht mehr – Bereitschaften entfallen
        foreach (var alt in d.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Bereitschaftsvertretung && v.VertreterId is null))
            alt.Art = VertretungsArt.BereitschaftEntfaellt;
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
            {
                VeranstaltungsLogik.Bereinigen(d, va, datum);
                // je Stunde eine Zeile je teilnehmender Lehrkraft; entfernte Lehrkräfte erscheinen als Platz „?“ (offen) bzw. mit Ersatzlehrkraft
                var plaetze = va.LehrerIds.Select(l => va.Ersatz.FirstOrDefault(e => e.Datum == datum && e.LehrerId == l) is { } e ? (Lehrer: e.ErsatzId, Fuer: (int?)l) : (Lehrer: (int?)l, Fuer: null))
                    .Concat(va.Zusatz.Where(z => z.Datum == datum).Select(z => (Lehrer: (int?)z.LehrerId, Fuer: (int?)null)))
                    .DistinctBy(p => p.Fuer.HasValue ? $"E{p.Fuer}" : $"L{p.Lehrer}").ToList();
                if (plaetze.Count == 0) plaetze.Add((null, null));
                // je Veranstaltung einmal bestimmen (bei allen Lehrkräften und Klassen Tausende Zeilen am Tag)
                var vaKlassen = va.AlleKlassenIds.ToArray();
                var vaRaum = va.RaumIds.Cast<int?>().FirstOrDefault();
                var vaZusatzraeume = va.RaumIds.Skip(1).ToArray();
                var vaGrund = GrundText(d, va.GrundId);
                for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
                {
                    if (!va.Deckt(datum, s) || !d.Zeitraster.Verfuegbar(tag, s)) continue;
                    foreach (var (l, fuer) in plaetze)
                        benoetigt.Add(new Vertretung
                        {
                            Datum = datum, Stunde = s, Art = VertretungsArt.Veranstaltung, Quelle = VertretungsQuelle.Veranstaltung, VeranstaltungId = va.Id,
                            VertreterId = l, ErsatzFuerLehrerId = fuer, LehrerId = fuer ?? l, KlassenIds = vaKlassen.ToList(),
                            VertretungsraumId = vaRaum, ZusatzraumIds = vaZusatzraeume.ToList(),
                            GrundId = va.GrundId, Grund = vaGrund, Text = va.Text, Schuelergruppe = va.Schuelergruppe, Wert = va.Wert,
                        });
                }
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
                // Die Lehrkraft des Unterrichts führt selbst Aufsicht: die Prüfung ersetzt den Unterricht – genau eine Zeile (Art Klausur)
                // mit bisherigem Lehrer, Fach und Raum statt Prüfungszeile und zusätzlichem Entfall
                if (r.LehrerId is int kl && klausur.LehrerIds.Contains(kl) && benoetigt.FirstOrDefault(b => b.Art == VertretungsArt.Klausur && b.KlausurId == klausur.Id
                        && b.Stunde == r.Stunde && b.VertreterId == kl && b.UnterrichtId is null) is { } kz)
                {
                    kz.UnterrichtId = u.Id;
                    kz.ZeilenIndex = r.ZeilenIndex;
                    kz.LehrerId = kl;
                    kz.FachId = r.FachId;
                    kz.RaumId = r.RaumId;
                    kz.Schuelergruppe = u.Schuelergruppe;
                    continue;
                }
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
            // Klassen abwesend → Freisetzung (bzw. Unterricht findet als Veranstaltung statt, wenn die Lehrkraft an derselben Veranstaltung teilnimmt)
            // Kopplungszeile ohne eigene Klassen (z. B. hinzugefügte zweite Lehrkraft): es zählen die Klassen der ganzen Kopplung
            var klassenDerStunde = KlassenDerStunde(r);
            if (klassenDerStunde.Count > 0 && klassenDerStunde.All(k => Absenzen.KlasseAbwesend(d, k, datum, r.Stunde)))
            {
                var abw = Absenzen.Klasse(d, klassenDerStunde[0], datum, r.Stunde);
                var va = abw is null ? Absenzen.VeranstaltungKlasse(d, klassenDerStunde[0], datum, r.Stunde) : null;
                if (bereit) continue;
                basis.Quelle = va != null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                basis.AbwesenheitId = abw?.Id;
                basis.VeranstaltungId = va?.Id;
                basis.GrundId = abw?.GrundId ?? va?.GrundId;
                basis.Grund = GrundText(d, basis.GrundId) is { Length: > 0 } g ? g : "Klasse abw.";
                basis.Text = abw?.Text ?? va?.Text ?? "";
                basis.InVeranstaltung = va != null && r.LehrerId is int vl && va.NimmtTeil(vl, datum) && Absenzen.Lehrer(d, vl, datum, r.Stunde) is null;
                basis.Art = basis.InVeranstaltung ? VertretungsArt.Entfall : VertretungsArt.Freisetzung;
                benoetigt.Add(basis);
                continue;
            }
            // Lehrer abwesend
            if (r.LehrerId is int l && Absenzen.LehrerAbwesend(d, l, datum, r.Stunde))
            {
                var abw = Absenzen.Lehrer(d, l, datum, r.Stunde);
                var va = abw is null ? Absenzen.VeranstaltungLehrer(d, l, datum, r.Stunde) : null;
                // Kl-Teil bzw. Schülergruppe: der Unterricht der Lehrkraft mit (einem Teil) dieser Klassen findet als Veranstaltung statt
                if (va != null && VeranstaltungsLogik.UnterrichtInVeranstaltung(va, r))
                {
                    basis.Quelle = VertretungsQuelle.Veranstaltung;
                    basis.VeranstaltungId = va.Id;
                    basis.GrundId = va.GrundId;
                    basis.Grund = GrundText(d, va.GrundId);
                    basis.Text = va.Text;
                    basis.Art = bereit ? VertretungsArt.BereitschaftEntfaellt : VertretungsArt.Entfall;
                    basis.InVeranstaltung = !bereit;
                    benoetigt.Add(basis);
                    continue;
                }
                basis.Quelle = va != null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                basis.AbwesenheitId = abw?.Id;
                basis.VeranstaltungId = va?.Id;
                basis.GrundId = abw?.GrundId ?? va?.GrundId;
                basis.Grund = GrundText(d, basis.GrundId);
                // Bereitschaften werden nie vertreten: fehlt die Lehrkraft, entfällt die Bereitschaft
                if (bereit) basis.Art = VertretungsArt.BereitschaftEntfaellt;
                else if (fach?.KeineVertretung == true || u.KeineVertretung) basis.Art = VertretungsArt.Entfall;
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
        // nur ergänzen: Unterrichte mit verwaister Zeile (Stunde verschoben) überlässt man der Nacherfassung – sonst doppelte Zeilen
        var verwaist = new HashSet<int>();
        // Zuordnung über den Schlüssel (je Schlüssel in der Reihenfolge der Berechnung) – bei Veranstaltungen mit allen Lehrkräften
        // und Klassen sind das Tausende Zeilen am Tag, die lineare Suche war quadratisch
        var offen = new Dictionary<string, Queue<Vertretung>>();
        foreach (var b in benoetigt)
            if (Schluessel(b) is { } bk)
            {
                if (!offen.TryGetValue(bk, out var q)) offen[bk] = q = new Queue<Vertretung>();
                q.Enqueue(b);
            }
        var zugeordnet = new HashSet<Vertretung>(ReferenceEqualityComparer.Instance);
        var weg = new HashSet<Vertretung>(ReferenceEqualityComparer.Instance);
        foreach (var v in vorhanden)
        {
            var key = Schluessel(v);
            var b = key is not null && offen.TryGetValue(key, out var q) && q.Count > 0 ? q.Dequeue() : null;
            if (b is null)
            {
                if (v.VerlegungsId.HasValue) v.Quelle = VertretungsQuelle.Verlegung;
                else if (entfernen) weg.Add(v);
                else if (v.UnterrichtId is int vu) verwaist.Add(vu);
                continue;
            }
            zugeordnet.Add(b);
            var vorher = Inhalt.Von(v);
            Abgleichen(v, b);
            // Kennzeichen „Neue Vertretung“: auch eine durch die Neuberechnung geänderte Zeile gilt in dieser Sitzung als geändert
            if (!vorher.Gleich(v)) v.Geaendert = DateTime.Now;
        }
        if (weg.Count > 0) d.Vertretungen.RemoveAll(weg.Contains);
        foreach (var b in benoetigt)
        {
            if (zugeordnet.Contains(b)) continue;
            if (!entfernen && b.UnterrichtId is int bu && verwaist.Contains(bu)) continue;
            b.Id = d.NeueId();
            d.Vertretungen.Add(b);
        }
        OffenWennVertreterAbwesend(d, datum);
        KlassenabwesenheitAnwenden(d, datum);
    }

    /// <summary>Klassen einer Stunde; eine Kopplungszeile ohne eigene Klassen gehört zu den Klassen der ganzen Kopplung.</summary>
    private static IReadOnlyList<int> KlassenDerStunde(RegelStunde r) =>
        r.KlassenIds.Count > 0 ? r.KlassenIds : r.Unterricht.Zeilen.SelectMany(z => z.KlassenIds).Distinct().ToList();

    /// <summary>Arten, die nicht als Unterricht der Klasse zählen (bleiben bei abwesender Klasse unverändert).</summary>
    private static bool KeinKlassenunterricht(VertretungsArt a) => a is VertretungsArt.Veranstaltung or VertretungsArt.Klausur or VertretungsArt.Raumbuchung
        or VertretungsArt.Pausenaufsicht or VertretungsArt.Vormerkung or VertretungsArt.SondereinsatzVormerkung or VertretungsArt.Unterricht;

    /// <summary>
    /// Ist die Klasse abwesend (Veranstaltung der Klasse bzw. Klassenabsenz), entfällt auch Unterricht, der erst nachträglich hinzukam
    /// oder von Hand geändert wurde (Sondereinsatz, Stunde ändern, Verlegung, Raumänderung, bearbeitete Vertretung): Die Zeile wird
    /// zur Freisetzung (bzw. zum Entfall, wenn die Lehrkraft an derselben Veranstaltung teilnimmt), die bisherige Art bleibt in
    /// <see cref="Vertretung.ArtVorKlassenabwesenheit"/> erhalten und kehrt zurück, sobald die Klasse wieder anwesend ist.
    /// Liefert die Anzahl geänderter Zeilen.
    /// </summary>
    public static int KlassenabwesenheitAnwenden(Schuldatei d, DateOnly datum)
    {
        var n = 0;
        Tagesplan? tp = null;
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum).ToList())
        {
            // abgeleitete, nicht bearbeitete Zeilen folgen ohnehin der Neuberechnung
            if (IstAbgeleitet(v) && !v.Bearbeitet)
            {
                v.ArtVorKlassenabwesenheit = null;
                continue;
            }
            if (KeinKlassenunterricht(v.Art) || IstBereitschaftsfach(d, v.VertretungsfachId ?? v.FachId)) continue;
            IReadOnlyList<int> klassen = v.NeueKlassenIds;
            if (klassen.Count == 0 && v.UnterrichtId is int uid)
            {
                tp ??= Tagesplan.Berechnen(d, datum);
                klassen = tp.Unterrichte.TryGetValue(uid, out var u) ? u.Zeilen.SelectMany(z => z.KlassenIds).Distinct().ToList() : klassen;
            }
            var abwesend = klassen.Count > 0 && klassen.All(k => Absenzen.KlasseAbwesend(d, k, datum, v.Stunde));
            if (abwesend && !v.IstEntfall)
            {
                var va = Absenzen.Klasse(d, klassen[0], datum, v.Stunde) is null ? Absenzen.VeranstaltungKlasse(d, klassen[0], datum, v.Stunde) : null;
                var lehrer = v.VertreterId ?? v.LehrerId;
                v.ArtVorKlassenabwesenheit = v.Art;
                v.Art = va != null && lehrer is int l && va.NimmtTeil(l, datum) ? VertretungsArt.Entfall : VertretungsArt.Freisetzung;
                v.Geaendert = DateTime.Now;
                n++;
            }
            else if (!abwesend && v.ArtVorKlassenabwesenheit is VertretungsArt alt)
            {
                if (v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung) v.Art = alt;
                v.ArtVorKlassenabwesenheit = null;
                v.Geaendert = DateTime.Now;
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// <see cref="KlassenabwesenheitAnwenden(Schuldatei, DateOnly)"/> für alle Tage, an denen eine Klasse abwesend ist (Veranstaltung mit
    /// Klassen, Klassenabsenz) oder eine Zeile wegen einer früheren Klassenabwesenheit entfallen ist – z. B. nach einer Änderung an Vertretungen.
    /// </summary>
    public static int KlassenabwesenheitAnwenden(Schuldatei d)
    {
        var tage = new HashSet<DateOnly>();
        foreach (var v in d.Vertretungen)
        {
            if (v.ArtVorKlassenabwesenheit.HasValue) { tage.Add(v.Datum); continue; }
            if (IstAbgeleitet(v) && !v.Bearbeitet) continue;
            if (d.Veranstaltungen.Any(va => va.KlassenIds.Count > 0 && va.Deckt(v.Datum, v.Stunde)) ||
                d.Abwesenheiten.Any(a => a.Art == AbwesenheitsArt.Klasse && a.Deckt(v.Datum, v.Stunde)))
                tage.Add(v.Datum);
        }
        return tage.Sum(t => KlassenabwesenheitAnwenden(d, t));
    }

    /// <summary>Inhalt einer Zeile, der für das Kennzeichen „Neue Vertretung“ zählt.</summary>
    /// <remarks>Ohne Zeichenketten (früher je Zeile zweimal alle Klassen als Text – bei Veranstaltungen mit allen Klassen spürbar).</remarks>
    private readonly record struct Inhalt(VertretungsArt Art, int? VertreterId, int? LehrerId, int? VertretungsraumId, int? RaumId, int? FachId, string Text,
        int[] KlassenIds, int[] ZusatzraumIds, bool Mitbetreuung)
    {
        public static Inhalt Von(Vertretung v) => new(v.Art, v.VertreterId, v.LehrerId, v.VertretungsraumId, v.RaumId, v.FachId, v.Text, v.KlassenIds.ToArray(), v.ZusatzraumIds.ToArray(), v.Mitbetreuung);

        public bool Gleich(Vertretung v) => Art == v.Art && VertreterId == v.VertreterId && LehrerId == v.LehrerId && VertretungsraumId == v.VertretungsraumId &&
            RaumId == v.RaumId && FachId == v.FachId && Text == v.Text && Mitbetreuung == v.Mitbetreuung &&
            KlassenIds.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(v.KlassenIds)) &&
            ZusatzraumIds.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(v.ZusatzraumIds));
    }

    /// <summary>Vorhandene abgeleitete Zeile an die neu berechnete angleichen (bearbeitete Zeilen behalten Vertreter, Raum und Text).</summary>
    private static void Abgleichen(Vertretung v, Vertretung b)
    {
        {
            v.GrundId = b.GrundId;
            v.Grund = b.Grund;
            v.AbwesenheitId = b.AbwesenheitId;
            v.VeranstaltungId = b.VeranstaltungId;
            v.KlausurId = b.KlausurId;
            v.Quelle = b.Quelle;
            v.InVeranstaltung = b.InVeranstaltung;
            v.ErsatzFuerLehrerId = b.ErsatzFuerLehrerId;
            // Zeilen der Veranstaltung selbst folgen immer der Veranstaltung (Lehrkräfte, Klassen, Räume)
            if (v.Art == VertretungsArt.Veranstaltung && b.Art == VertretungsArt.Veranstaltung)
            {
                v.VertreterId = b.VertreterId;
                v.LehrerId = b.LehrerId;
                v.KlassenIds = b.KlassenIds;
                v.VertretungsraumId = b.VertretungsraumId;
                v.ZusatzraumIds = b.ZusatzraumIds;
                v.Schuelergruppe = b.Schuelergruppe;
                v.Wert = b.Wert;
                if (!v.Bearbeitet) v.Text = b.Text;
                return;
            }
            // Unterricht wird durch die Prüfung ersetzt (ältere Dateien: Entfall-Zeile neben der Prüfungszeile): folgt der Prüfung
            if (!v.Bearbeitet || (b.Art == VertretungsArt.Klausur && b.UnterrichtId.HasValue && v.Art != VertretungsArt.Klausur))
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
    }

    /// <summary>Eingesetzte Vertreter, die inzwischen selbst abwesend sind → wieder offen.</summary>
    private static void OffenWennVertreterAbwesend(Schuldatei d, DateOnly datum)
    {
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.VertreterId.HasValue && v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung
                     or VertretungsArt.Betreuung or VertretungsArt.Teilvertretung or VertretungsArt.Lehrertausch or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Pausenaufsicht))
        {
            var stunde = v.Art == VertretungsArt.Pausenaufsicht ? v.Stunde + 1 : v.Stunde;
            if (v.VertreterId == v.LehrerId || v.Fixiert) continue;
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
        v.VeranstaltungId is int va ? (v.ErsatzFuerLehrerId is int f ? $"V{va}/{v.Stunde}/E{f}" : $"V{va}/{v.Stunde}/{v.VertreterId}") : null;

    public static List<Vertretung> Am(Schuldatei d, DateOnly datum) =>
        d.Vertretungen.Where(v => v.Datum == datum).OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();

    public static List<Vertretung> ImZeitraum(Schuldatei d, DateOnly von, DateOnly bis) =>
        d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ThenBy(v => v.Id).ToList();

    /// <summary>Ist der Lehrer zu dieser Stunde gebunden? Liefert den Grund oder null.</summary>
    public static string? LehrerBelegt(Schuldatei d, Tagesplan tp, int lehrerId, int stunde, Vertretung? ausser = null)
    {
        // Zeile einer Veranstaltung: die Teilnahme an genau dieser Veranstaltung ist keine Abwesenheit
        if (ausser is { Art: VertretungsArt.Veranstaltung, VeranstaltungId: int vaId })
        {
            if (Absenzen.Lehrer(d, lehrerId, tp.Datum, stunde) != null) return "abwesend";
            if (d.Veranstaltungen.Any(x => x.Id != vaId && x.Deckt(tp.Datum, stunde) && x.NimmtTeil(lehrerId, tp.Datum))) return "andere Veranstaltung";
            foreach (var e in tp.Stunde(stunde))
            {
                if (!e.Findetstatt || !e.LehrerIds.Contains(lehrerId) || e.Vertretung?.VeranstaltungId == vaId) continue;
                if (e.Status == TagesStatus.Regulaer && IstBereitschaftsfach(d, e.FachId)) continue;
                if (e.Vertretung?.Mitbetreuung == true) continue;
                return e.Status == TagesStatus.Regulaer ? "eigener Unterricht" : "vertritt bereits";
            }
            return null;
        }
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

    /// <summary>Vertretungstext, der bei „+++“ (Vertretung ohne Lehrkraft) eingesetzt wird, wenn noch kein anderer Text da ist.</summary>
    public const string OhneLehrerText = "Vertr. o. Lehrkr.";

    /// <summary>Der automatisch eingesetzte Text gehört nur zu „+++“: wird daraus etwas anderes, verschwindet er wieder.</summary>
    private static void OhneLehrerTextEntfernen(Vertretung v)
    {
        if (v.Art == VertretungsArt.VertretungOhneLehrer && v.Text is OhneLehrerText or "Vertr. o. Lehrkraft") v.Text = "";
    }

    private static void Markieren(Vertretung v, string benutzer)
    {
        v.Bearbeitet = true;
        AlsGeaendertMarkieren(v, benutzer);
    }

    /// <summary>Kennzeichen „Neue Vertretung“ setzen: die Zeile wurde in dieser Arbeitssitzung geändert (auch Text, Fixierung …).</summary>
    public static void AlsGeaendertMarkieren(Vertretung v, string benutzer = "")
    {
        v.Geaendert = DateTime.Now;
        if (!string.IsNullOrEmpty(benutzer)) v.Benutzer = benutzer;
    }

    /// <summary>
    /// Kennzeichen „Neue Vertretung“ (wie im Vorbild): in der aktuellen Arbeitssitzung – seit dem Öffnen der Datei – neu angelegt
    /// oder geändert. Reine Unterrichtszeilen (Nr. 0) sind nie neu.
    /// </summary>
    public static bool IstNeu(Vertretung v, DateTime sitzungsbeginn) => v.Id != 0 && (v.Erstellt >= sitzungsbeginn || v.Geaendert >= sitzungsbeginn);

    /// <summary>
    /// Vertreter einsetzen. "+" = Vertretung ohne Lehrkraft, "-" = Entfall, null = wieder offen.
    /// Statt-Vertretung, Betreuung (Lehrer hat selbst Unterricht) und Bereitschaft werden erkannt.
    /// </summary>
    public static void VertreterSetzen(Schuldatei d, Vertretung v, int? lehrerId, bool mitbetreuung = false, string benutzer = "")
    {
        OhneLehrerTextEntfernen(v);
        Markieren(v, benutzer);
        if (lehrerId is null)
        {
            v.VertreterId = null;
            v.Mitbetreuung = false;
            if (v.Art is not (VertretungsArt.Pausenaufsicht or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Raumvertretung or VertretungsArt.Verlegt)) v.Art = VertretungsArt.Vertretung;
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
            or VertretungsArt.Veranstaltung or VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch or VertretungsArt.UnterrichtGeaendert or VertretungsArt.SondereinsatzVormerkung) { }
        // Lehrkraft ist selbst absent und wird ausnahmsweise trotzdem eingesetzt („Trotzdem verplanen“)
        else if (Absenzen.Lehrer(d, l, v.Datum, stunde) != null || (v.LehrerId == l && Absenzen.LehrerAbwesend(d, l, v.Datum, stunde))) v.Art = VertretungsArt.TrotzAbsenz;
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

    /// <summary>Rückfrage beim Entfall eines Sondereinsatzes (Vertretungsliste, Vorschlag, Stundenplan).</summary>
    public const string SondereinsatzLoeschenFrage = "Soll der Sondereinsatz gelöscht werden?";

    /// <summary>Zeile, bei deren Entfall gefragt wird, ob der Sondereinsatz gelöscht werden soll.</summary>
    public static bool IstSondereinsatz(Vertretung v) => v.Art == VertretungsArt.Sondereinsatz && v.GangId is null;

    /// <summary>Sondereinsätze, die „Stunde entfallen lassen“ im Stundenplan beträfe (für die Rückfrage).</summary>
    public static List<Vertretung> SondereinsaetzeBeiEntfall(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde) =>
        Tagesplan.Berechnen(d, datum).Stunde(stunde).Where(e => e.Findetstatt && e.Vertretung is { } v && IstSondereinsatz(v) && (e.Regel?.Unterricht.Id ?? v.UnterrichtId) == unterrichtId &&
            (zeilenIndex is null || (e.Regel?.ZeilenIndex ?? v.ZeilenIndex) == zeilenIndex)).Select(e => e.Vertretung!).Distinct().ToList();

    /// <summary>
    /// Entfall-Zeilen einer Stunde im Stundenplan (Unterricht, im Lehrerplan nur die Zeile der Lehrkraft) rückgängig machen: Ist die
    /// Lehrkraft anwesend, findet die Stunde wieder regulär statt, sonst wird sie zur offenen Vertretung (<see cref="Entfallaufhebung"/>).
    /// Verlegte Stunden bleiben unberührt. Ergebnis: Anzahl.
    /// </summary>
    public static int EntfallRueckgaengig(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde, string benutzer = "") =>
        Entfallaufhebung.Aufheben(d, unterrichtId, zeilenIndex, datum, stunde, benutzer);

    /// <summary>Zeilen, die <see cref="EntfallRueckgaengig"/> zurücknehmen würde.</summary>
    public static List<Vertretung> EntfallZeilen(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde) =>
        d.Vertretungen.Where(v => v.Datum == datum && v.Stunde == stunde && v.UnterrichtId == unterrichtId && (zeilenIndex is null || v.ZeilenIndex == zeilenIndex)
                                  && v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt && v.NachDatum is null && v.VerlegungsId is null
                                  && !IstBereitschaftsfach(d, v.FachId)).ToList();

    /// <summary>
    /// Entfall mit Option: Ist die Zeile ein Sondereinsatz und <paramref name="sondereinsatzLoeschen"/> gesetzt, wird der Sondereinsatz
    /// komplett gelöscht statt zur Entfall-Zeile zu werden. Ergebnis: true, wenn gelöscht.
    /// </summary>
    public static bool EntfallUmschalten(Schuldatei d, Vertretung v, string benutzer, bool sondereinsatzLoeschen)
    {
        if (sondereinsatzLoeschen && IstSondereinsatz(v))
        {
            d.Vertretungen.Remove(v);
            return true;
        }
        EntfallUmschalten(d, v, benutzer);
        return false;
    }

    /// <summary>Schaltfläche Entfall: offene Vertretung → Entfall, Entfall → wieder offene Vertretung.</summary>
    public static void EntfallUmschalten(Schuldatei d, Vertretung v, string benutzer = "")
    {
        OhneLehrerTextEntfernen(v);
        Markieren(v, benutzer);
        if (v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt)
        {
            if (IstBereitschaftsfach(d, v.FachId)) return; // Bereitschaften werden nie vertreten
            v.Art = v.GangId.HasValue ? VertretungsArt.Pausenaufsicht : VertretungsArt.Vertretung;
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
        if (string.IsNullOrWhiteSpace(v.Text)) v.Text = OhneLehrerText;
    }

    /// <summary>
    /// Blockvertretung (Option „Stundenblock“): Vertreter für alle offenen Stunden des Blocks (Doppelstunde) eintragen.
    /// Liefert die bearbeiteten Vertretungen.
    /// </summary>
    public static List<Vertretung> Blockvertretung(Schuldatei d, Vertretung v, int lehrerId, string benutzer = "")
    {
        var res = new List<Vertretung>();
        foreach (var x in VertretungsVorschlag.Block(d, v).Where(x => x == v || x.Offen))
        {
            VertreterSetzen(d, x, lehrerId, false, benutzer);
            res.Add(x);
        }
        return res;
    }

    /// <summary>
    /// Dauervertretung: dieselbe Stunde (gleicher Wochentag, gleiche Stunde – mit <paramref name="block"/> der ganze Stundenblock)
    /// dieses Unterrichts wird in allen Folgewochen der Absenz mit derselben Lehrkraft vertreten. Vertretungen der Folgewochen werden
    /// dazu erzeugt. Wochen, in denen die Lehrkraft nicht frei ist, werden übersprungen.
    /// </summary>
    public static (List<Vertretung> Gesetzt, List<Vertretung> Uebersprungen) Dauervertretung(Schuldatei d, Vertretung v, int lehrerId, bool block = false, string benutzer = "")
    {
        var gesetzt = new List<Vertretung>();
        var weg = new List<Vertretung>();
        var stunden = (block ? VertretungsVorschlag.Block(d, v) : new List<Vertretung> { v }).Select(x => x.Stunde).ToHashSet();
        var abw = v.AbwesenheitId is int aid ? d.Abwesenheiten.FirstOrDefault(a => a.Id == aid) : null;
        var ende = abw?.Bis ?? (v.LehrerId is int ol
            ? d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == ol && a.Deckt(v.Datum, v.Stunde)).Select(a => a.Bis).DefaultIfEmpty(v.Datum).Max()
            : v.Datum);
        if (ende > v.Datum) Aktualisieren(d, v.Datum.AddDays(1), ende);
        var kandidaten = d.Vertretungen.Where(x => x.UnterrichtId == v.UnterrichtId && v.UnterrichtId.HasValue && x.ZeilenIndex == v.ZeilenIndex &&
                x.Datum >= v.Datum && x.Datum <= ende && x.Datum.DayOfWeek == v.Datum.DayOfWeek && stunden.Contains(x.Stunde) &&
                (x == v || x.Offen) && (abw is null ? x.LehrerId == v.LehrerId : x.AbwesenheitId == abw.Id))
            .OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList();
        if (!kandidaten.Contains(v)) kandidaten.Insert(0, v);
        foreach (var x in kandidaten)
        {
            if (x != v && LehrerBelegt(d, Tagesplan.Berechnen(d, x.Datum), lehrerId, x.Stunde, x) != null) { weg.Add(x); continue; }
            VertreterSetzen(d, x, lehrerId, false, benutzer);
            gesetzt.Add(x);
        }
        return (gesetzt, weg);
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
            if (e.Status == TagesStatus.Zusaetzlich && e.Vertretung is { Art: VertretungsArt.Verlegung or VertretungsArt.Verlegt or VertretungsArt.Tausch } ziel)
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
                if (ziel.Art is VertretungsArt.Verlegung or VertretungsArt.Verlegt) ziel.Art = ziel.VertreterId is null ? VertretungsArt.Verlegt : VertretungsArt.Verlegung;
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
            // Lehrkraft ist an der Zielposition nicht verfügbar: „verlegt“ – offene Vertretung
            if (z.VertreterId is null && zielArt == VertretungsArt.Verlegung) z.Art = VertretungsArt.Verlegt;
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
    /// <param name="sondereinsatzLoeschen">Sondereinsätze der Stunde: true = löschen, false = Entfall-Zeile, null = wie bisher
    /// (zusätzlicher Sondereinsatz wird gelöscht, Sondereinsatz zu einem Unterricht wird zum Entfall)</param>
    public static void Entfallen(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde, string benutzer = "", bool? sondereinsatzLoeschen = null)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        foreach (var e in tp.Stunde(stunde).Where(e => e.Findetstatt && (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == unterrichtId &&
                     (zeilenIndex is null || (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeilenIndex)).ToList())
        {
            if (e.Vertretung is { } se && IstSondereinsatz(se) && sondereinsatzLoeschen is bool loeschen && (loeschen || e.Status == TagesStatus.Zusaetzlich))
            {
                if (loeschen) d.Vertretungen.Remove(se);
                else
                {
                    se.Art = VertretungsArt.Entfall;
                    se.VertreterId = null;
                    se.VertretungsraumId = null;
                    se.Mehrarbeit = false;
                    Markieren(se, benutzer);
                }
                continue;
            }
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
    /// <summary>Spalte „Stundenplan“: drei Stunden um die Vertretungsstunde (die Vertretungsstunde ist markiert).</summary>
    public List<StundenplanZelle> Zellen { get; set; } = new();
    /// <summary>Spalte „Vertr.“: gewertete Vertretungen im Zählzeitraum (Stundenwert).</summary>
    public decimal VertretungenWert { get; set; }
    /// <summary>Spalte „Entfall“: gewertete Entfälle und Freisetzungen im Zählzeitraum (Stundenwert, positiv).</summary>
    public decimal EntfaelleWert { get; set; }
    /// <summary>Spalte „Kennz.“: Lehrkraft ist am Tag bereits von Vertretungen betroffen.</summary>
    public bool Kennzeichen { get; set; }
    public string KennzeichenText { get; set; } = "";
    /// <summary>Spalte „reg. Bereitschaft“: regelmäßige Bereitschaftsstunden pro Woche.</summary>
    public int RegelBereitschaft { get; set; }
    /// <summary>Spalte „Dep.“: Deputatsdifferenz Ist − Soll (null = kein Soll erfasst).</summary>
    public decimal? Deputat { get; set; }
    /// <summary>Unmittelbar angrenzender Unterricht liegt in einem anderen Gebäude (Abteilung des Raumes).</summary>
    public bool Disloziert { get; set; }
    public bool Mehrarbeit => !Bereitschaft && !Freigesetzt;
}

/// <summary>Lehrkraft, die die Klasse neben ihrem eigenen Unterricht mitbetreuen kann.</summary>
/// <param name="Naehe">0 = gekoppelt, 1 = gleicher Gang, 2 = gleiches Stockwerk, 3 = sonst.</param>
public sealed record Betreuungskandidat(Lehrer Lehrer, string Klassen, string Fach, string Raum, string Gang, bool Kopplung, decimal Zaehler,
    int Naehe = 3, string NaeheText = "", int BetreuungenHeute = 0, bool KenntKlasse = false)
{
    /// <summary>Spalte „Stundenplan“ wie im Vertretungsvorschlag.</summary>
    public List<StundenplanZelle> Zellen { get; init; } = new();
    public int Zeitwunsch { get; init; }
    public decimal Jahreszaehler { get; init; }
    public bool Kennzeichen { get; init; }
    public string KennzeichenText { get; init; } = "";
}

/// <summary>Eine Stunde der Spalte „Stundenplan“ im Vertretungsvorschlag.</summary>
/// <param name="Text">Fach (bzw. Klasse) des tagesaktuellen Unterrichts, leer = frei.</param>
/// <param name="Entfallen">Der Unterricht der Lehrkraft entfällt bzw. ist offen (rot durchgestrichen).</param>
/// <param name="Ziel">Die Stunde der Vertretung (gestrichelt markiert).</param>
/// <param name="Hintergrund">Fachfarbe (ARGB, 0 = keine).</param>
public sealed record StundenplanZelle(int Stunde, string Text, bool Entfallen, bool Ziel, int Hintergrund = 0, int Vordergrund = 0);

public sealed record Raumkandidat(Raum Raum, bool Ausweichraum, bool AusweichStammraum, string? BesetztDurch, string Raumgruppen, string Gang, int KapazitaetDifferenz);

public sealed record Vorziehung(TagesEintrag Eintrag, DateOnly Datum, int Stunde, string Beschreibung)
{
    /// <summary>Vorgezogen wird eine Randstunde der Klasse (es entsteht keine neue Lücke).</summary>
    public bool Randstunde { get; init; }
    public string Lehrer { get; init; } = "";
    public string Klassen { get; init; } = "";
    public string Fach { get; init; } = "";
    public string Raum { get; init; } = "";
}

public enum AutomatikMethode
{
    BesterVorschlag,
    BereitschaftUndFreigesetzteZuerst,
    VorziehungenZuerst,
    Pausenaufsichten,
    /// <summary>Nur Bereitschaftslehrer und freigesetzte Lehrer einteilen (ohne Rückfall auf andere Lehrer).</summary>
    Freigesetzte,
    /// <summary>Lehrer einer Kopplung desselben Unterrichts betreuen mit (Betreuung).</summary>
    Kopplungslehrer,
    /// <summary>Offene Raumvertretungen, nur mit Ausweichräumen.</summary>
    Raumvertretungen,
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

    /// <summary>
    /// Vertretungsvorschlag: alle verfügbaren Lehrer, gereiht nach Schlechtpunkten (Vorschlagsgüte wie im Vorbild:
    /// freigesetzte und Bereitschaftslehrer zuerst, dann nach Merker zur Stunde, Zähler und Zeitwunsch;
    /// Lehrkräfte mit Zeitwunsch −3 stehen am Ende).
    /// </summary>
    /// <param name="dislozierungenIgnorieren">
    /// Lehrkräfte auch vorschlagen, deren unmittelbar angrenzender Unterricht in einem anderen Gebäude (Abteilung des Raumes) liegt.
    /// </param>
    public static List<Kandidat> Berechnen(Schuldatei d, Vertretung v, bool stundenblock = false, bool alleAnzeigen = false, bool dislozierungenIgnorieren = false)
    {
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var ix = new DatenIndex(d);
        var e = d.Vertretungseinstellungen.Vorschlag;
        var tag = Kalender.TagIndex(d, v.Datum);
        var stunden = new List<int> { v.Stunde };
        if (stundenblock) stunden = Block(d, v).Select(x => x.Stunde).ToList();
        var (zVon, zBis) = Zaehlung.Zeitraum(d, v.Datum);
        var res = new List<Kandidat>();
        var klassen = v.NeueKlassenIds;
        var montag = Kalender.Montag(v.Datum);
        var monat = new DateOnly(v.Datum.Year, v.Datum.Month, 1);
        var kennzeichen = Kennzeichen(d, ix, v);
        var bereitschaften = RegelBereitschaften(d, ix);
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
            k.Stundenplan = StundenplanText(d, ix, tp, l.Id, v.Stunde);
            k.Zellen = StundenplanZellen(d, ix, tp, l.Id, v.Stunde);
            k.KenntKlasse = klassen.Count > 0 && d.Unterrichte.Any(u => u.Zeilen.Any(z => z.LehrerId == l.Id &&
                (e.KlasseAuchFuerKlassenteil ? z.KlassenIds.Intersect(klassen).Any() : klassen.All(z.KlassenIds.Contains))));
            k.Klassenlehrer = d.Klassen.Any(c => klassen.Contains(c.Id) && c.KlassenlehrerIds.Contains(l.Id));
            k.Fachlehrer = v.FachId is int f && (l.FaecherIds.Contains(f) || d.Unterrichte.Any(u => u.Zeilen.Any(z => z.LehrerId == l.Id && z.FachId == f)));
            k.Vorwoche = d.Vertretungen.Any(x => x.VertreterId == l.Id && x.Datum >= v.Datum.AddDays(-7) && x.Datum < v.Datum && x.KlassenIds.Intersect(klassen).Any());
            var z = Zaehlung.Lehrer(d, l, zVon, zBis);
            k.Zaehler = z.Summe;
            k.Vertretungen = z.Vertretungen;
            k.Entfaelle = z.Entfaelle;
            (k.VertretungenWert, k.EntfaelleWert) = Werte(z);
            k.Bezahlt = z.Bezahlt;
            k.Betreuungen = z.Betreuungen;
            k.Jahreszaehler = Zaehlung.Lehrer(d, l, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis).Summe;
            k.ZaehlerWoche = Vertretungen.Zaehler(d, l.Id, montag, montag.AddDays(6));
            k.ZaehlerMonat = Vertretungen.Zaehler(d, l.Id, monat, monat.AddMonths(1).AddDays(-1));
            k.ZaehlerJahr = Vertretungen.Zaehler(d, l.Id, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis);
            k.Saldo = k.Jahreszaehler;
            if (kennzeichen.TryGetValue(l.Id, out var kz)) { k.Kennzeichen = true; k.KennzeichenText = kz; }
            k.RegelBereitschaft = bereitschaften.GetValueOrDefault(l.Id);
            k.Deputat = l.Soll > 0 ? Module.Wertrechnung.Ist(d, ix, l) - l.Soll : null;
            k.Disloziert = Disloziert(ix, tp, v, l.Id, stunden);
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
            // Zeitwunsch −3 (gesperrt) wie im Vorbild ans Ende der Liste
            if (zw <= -3) k.Punkte += 1000;
            if (!l.StundenProTag.IstLeer && k.UnterrichtAmTag + 1 > l.StundenProTag.Max) { k.Punkte += 25; k.Kriterien.Add("Std./Tag überschritten"); }
            if (l.Sperrvermerk > 0) k.Kriterien.Add($"Sperrvermerk {l.Sperrvermerk}");
            if (k.Disloziert)
            {
                if (!dislozierungenIgnorieren && !alleAnzeigen) continue;
                k.Kriterien.Add("Dislozierung");
                if (!dislozierungenIgnorieren) k.Punkte += 30;
            }
            if (merker > e.NichtAnzeigenWennMerkerGroesser && !alleAnzeigen) continue;
            k.Rot = merker > e.RotWennMerkerGroesser || k.Zaehler > e.RotWennZaehlerGroesser || l.Sperrvermerk >= 9;
            res.Add(k);
        }
        return res.OrderBy(x => x.Punkte).ThenBy(x => x.Lehrer.Kurzname).ToList();
    }

    /// <summary>Gewertete Vertretungen (positiv) und Entfälle/Freisetzungen (als positiver Betrag) einer Zählung – Spalten „Vertr.“ und „Entfall“.</summary>
    public static (decimal Vertretungen, decimal Entfaelle) Werte(ZaehlSumme z) =>
        (z.Zeilen.Where(x => x.Zaehlt && x.Wert > 0).Sum(x => x.Wert), -z.Zeilen.Where(x => x.Zaehlt && x.Wert < 0).Sum(x => x.Wert));

    /// <summary>
    /// Spalte „Kennz.“: Lehrkräfte, die am Tag der Vertretung bereits von Vertretungen betroffen sind
    /// (eigener Unterricht entfällt/wird vertreten oder sie vertreten selbst) – mit Liste der Stunden als Text.
    /// </summary>
    public static Dictionary<int, string> Kennzeichen(Schuldatei d, DatenIndex ix, Vertretung v)
    {
        var res = new Dictionary<int, List<string>>();
        void Add(int l, string t)
        {
            if (!res.TryGetValue(l, out var x)) res[l] = x = new();
            if (!x.Contains(t)) x.Add(t);
        }
        foreach (var x in d.Vertretungen.Where(x => x.Datum == v.Datum && x != v && x.Art != VertretungsArt.Pausenaufsicht).OrderBy(x => x.Stunde))
        {
            var text = $"{x.Stunde}. Std. {Vertretungsplan.ArtText(x.Art)} {ix.KKlassen(x.NeueKlassenIds)} {ix.KFach(x.VertretungsfachId ?? x.FachId)}".TrimEnd();
            if (x.LehrerId is int l && x.VertreterId != l) Add(l, text);
            if (x.VertreterId is int vt) Add(vt, text);
        }
        return res.ToDictionary(p => p.Key, p => string.Join(Environment.NewLine, p.Value));
    }

    /// <summary>Spalte „reg. Bereitschaft“: Bereitschaftsstunden je Lehrkraft und Woche laut Unterricht.</summary>
    public static Dictionary<int, int> RegelBereitschaften(Schuldatei d, DatenIndex ix)
    {
        var res = new Dictionary<int, int>();
        foreach (var u in d.Unterrichte.Where(u => !u.Ignorieren))
            foreach (var z in u.Zeilen.Where(z => z.LehrerId.HasValue && z.FachId is int f && ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft))
                res[z.LehrerId!.Value] = res.GetValueOrDefault(z.LehrerId!.Value) + u.Wochenstunden;
        return res;
    }

    /// <summary>Sind Gebäude (Abteilungen der Räume) erfasst? Nur dann ist „Dislozierungen ignorieren“ wählbar.</summary>
    public static bool DislozierungenDefiniert(Schuldatei d) => d.Raeume.Any(r => !r.Ignorieren && r.AbteilungId.HasValue);

    /// <summary>
    /// Dislozierung: der Unterricht der Lehrkraft unmittelbar vor oder nach der Vertretung liegt in einem anderen Gebäude
    /// (andere Abteilung des Raumes) als der Raum der Vertretung – ohne Pause dazwischen nicht erreichbar.
    /// </summary>
    public static bool Disloziert(DatenIndex ix, Tagesplan tp, Vertretung v, int lehrerId, IReadOnlyCollection<int>? stunden = null)
    {
        var raum = v.VertretungsraumId ?? v.RaumId;
        if (raum is not int rid || !ix.Raeume.TryGetValue(rid, out var r) || r.AbteilungId is not int geb) return false;
        stunden ??= new[] { v.Stunde };
        var nachbarn = new[] { stunden.Min() - 1, stunden.Max() + 1 };
        return tp.VonLehrer(lehrerId).Where(e => nachbarn.Contains(e.Stunde)).SelectMany(e => e.RaumIds)
            .Any(x => ix.Raeume.TryGetValue(x, out var nr) && nr.AbteilungId is int g && g != geb);
    }

    /// <summary>
    /// Spalte „Stundenplan“ wie im Vorbild: drei Stunden (die Vertretungsstunde in der Mitte, am Tagesrand verschoben) mit dem
    /// Fach des tagesaktuellen Unterrichts der Lehrkraft. Entfallener bzw. offener eigener Unterricht erscheint durchgestrichen.
    /// </summary>
    public static List<StundenplanZelle> StundenplanZellen(Schuldatei d, DatenIndex ix, Tagesplan tp, int lehrerId, int stunde, int anzahl = 3)
    {
        var max = Math.Max(stunde, d.Zeitraster.StundenProTag);
        var start = Math.Clamp(stunde - (anzahl - 1) / 2, 1, Math.Max(1, max - anzahl + 1));
        var res = new List<StundenplanZelle>();
        for (var s = start; s < start + anzahl; s++)
        {
            var aktiv = tp.Stunde(s).Where(e => e.Findetstatt && e.LehrerIds.Contains(lehrerId)).ToList();
            var entfallen = aktiv.Count == 0
                ? tp.Stunde(s).Where(e => (e.Status == TagesStatus.Offen && e.Regel?.LehrerId == lehrerId) || (e.Status == TagesStatus.Entfall && (e.LehrerIds.Contains(lehrerId) || e.Regel?.LehrerId == lehrerId))).ToList()
                : new List<TagesEintrag>();
            var liste = aktiv.Count > 0 ? aktiv : entfallen;
            var text = string.Join(" ", liste.Select(e => ix.KFach(e.FachId) is { Length: > 0 } f ? f : ix.KKlassen(e.KlassenIds)).Where(t => t.Length > 0).Distinct());
            var fach = liste.Select(e => e.FachId).FirstOrDefault(f => f.HasValue) is int fid ? ix.Faecher.GetValueOrDefault(fid) : null;
            res.Add(new StundenplanZelle(s, text, aktiv.Count == 0 && entfallen.Count > 0, s == stunde, fach?.Hintergrund ?? 0, fach?.Vordergrund ?? 0));
        }
        return res;
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
    public static string StundenplanText(Schuldatei d, Tagesplan tp, int lehrerId, int stunde, int umfang = 2) =>
        StundenplanText(d, new DatenIndex(d), tp, lehrerId, stunde, umfang);

    public static string StundenplanText(Schuldatei d, DatenIndex ix, Tagesplan tp, int lehrerId, int stunde, int umfang = 2)
    {
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
    /// <summary>
    /// Betreuungsvorschlag: Lehrkräfte mit eigenem Unterricht zur selben Stunde.
    /// Sortiert nach Nähe (gekoppelt, gleicher Gang, gleiches Stockwerk), dann danach, ob sie die Klasse kennen,
    /// nach Betreuungen am selben Tag und nach dem Vertretungszähler.
    /// </summary>
    public static List<Betreuungskandidat> Betreuungen(Schuldatei d, Vertretung v)
    {
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var ix = new DatenIndex(d);
        var (zVon, zBis) = Zaehlung.Zeitraum(d, v.Datum);
        var raumId = v.VertretungsraumId ?? v.RaumId;
        var eigenerRaum = raumId is int rid ? ix.Raeume.GetValueOrDefault(rid) : null;
        var klassen = v.NeueKlassenIds.ToHashSet();
        var heute = d.Vertretungen.Where(x => x.Datum == v.Datum && x.Mitbetreuung && x.VertreterId.HasValue).GroupBy(x => x.VertreterId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var kennt = d.Unterrichte.Where(u => u.KlassenIds.Any(klassen.Contains)).SelectMany(u => u.Zeilen.Select(z => z.LehrerId)).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var res = new List<Betreuungskandidat>();
        var tag = Kalender.TagIndex(d, v.Datum);
        var kennzeichen = Kennzeichen(d, ix, v);
        var raumReihe = d.Raeume.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var e in tp.Stunde(v.Stunde).Where(e => e.Findetstatt && e.LehrerIds.Count > 0 && e.Vertretung != v && !Vertretungen.IstBereitschaftsfach(d, e.FachId)))
            foreach (var l in e.LehrerIds.Where(l => l != v.LehrerId && !Absenzen.LehrerAbwesend(d, l, v.Datum, v.Stunde)))
            {
                if (res.Any(r => r.Lehrer.Id == l) || !ix.Lehrer.TryGetValue(l, out var le)) continue;
                var raum = e.RaumIds.FirstOrDefault();
                var r = raum != 0 ? ix.Raeume.GetValueOrDefault(raum) : null;
                var gang = r?.GangId is int g ? ix.K(ElementTyp.Gang, g) : "";
                var kopplung = e.Unterricht?.Id == v.UnterrichtId && v.UnterrichtId.HasValue;
                var (naehe, text) =
                    kopplung ? (0, "gekoppelt") :
                    r != null && eigenerRaum != null && r.GangId.HasValue && r.GangId == eigenerRaum.GangId ? (1, "gleicher Gang") :
                    r != null && eigenerRaum != null && r.Stockwerk.Length > 0 && r.Stockwerk == eigenerRaum.Stockwerk ? (2, "gleiches Stockwerk") :
                    (3, "");
                res.Add(new Betreuungskandidat(le, ix.KKlassen(e.KlassenIds), ix.KFach(e.FachId), raum == 0 ? "" : ix.KRaum(raum), gang, kopplung,
                    Zaehlung.Lehrer(d, le, zVon, zBis).Summe, naehe, text, heute.GetValueOrDefault(l), kennt.Contains(l))
                {
                    Zellen = StundenplanZellen(d, ix, tp, l, v.Stunde),
                    Zeitwunsch = tag >= 0 ? le.Zeitwuensche.Get(tag, v.Stunde) : 0,
                    Jahreszaehler = Zaehlung.Lehrer(d, le, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrBis).Summe,
                    Kennzeichen = kennzeichen.ContainsKey(l),
                    KennzeichenText = kennzeichen.GetValueOrDefault(l) ?? "",
                });
            }
        // wie im Vorbild: nach Nähe zum Raum der Vertretung, dann nach der Reihenfolge der Räume
        return res.OrderBy(r => r.Naehe).ThenByDescending(r => r.KenntKlasse).ThenBy(r => r.BetreuungenHeute).ThenBy(r => r.Zaehler)
            .ThenBy(r => d.Raeume.FirstOrDefault(x => x.Kurzname == r.Raum) is { } x ? raumReihe[x.Id] : int.MaxValue).ThenBy(r => r.Lehrer.Kurzname).ToList();
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
                    (rand ? " (Randstunde)" : " (erzeugt neue Lücke)"))
                {
                    Randstunde = rand,
                    Lehrer = string.Join(",", zeilen.SelectMany(z => z.LehrerIds).Distinct().Select(ix.KLehrer)),
                    Klassen = ix.KKlassen(zeilen.SelectMany(z => z.KlassenIds).Distinct()),
                    Fach = string.Join(",", zeilen.Select(z => ix.KFach(z.FachId)).Where(f => f.Length > 0).Distinct()),
                    Raum = string.Join(",", zeilen.SelectMany(z => z.RaumIds).Distinct().Select(ix.KRaum)),
                });
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
        foreach (var v in d.Vertretungen.Where(v => v.Datum == datum && v.Offen && !v.Fixiert && !v.VeranstaltungOffen).OrderBy(v => v.Stunde).ToList())
        {
            if (!v.Offen) continue;
            if (v.Art == VertretungsArt.Pausenaufsicht)
            {
                var pk = Module.Pausenaufsichten.Kandidaten(d, datum, new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 }).FirstOrDefault();
                if (pk != null) { Vertretungen.VertreterSetzen(d, v, pk.Id, false, benutzer); anzahl++; }
                continue;
            }
            if (methode == AutomatikMethode.Pausenaufsichten) continue;
            if (methode == AutomatikMethode.VorziehungenZuerst && Vorziehungen(d, v).FirstOrDefault(x => x.Randstunde) is { } vz)
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
    string Art, string Text, IReadOnlyList<int> KlassenIds, int? LehrerId, int? VertreterId, string VertrVon = "", string Nach = "", string Grund = "", int Id = 0,
    string Gruppe = "", string Bemerkung = "", VertretungsArt ArtWert = VertretungsArt.Vertretung)
{
    /// <summary>Zeile einer Veranstaltung (zusammengefasst je Stunde, nicht je Lehrkraft): deren Nummer.</summary>
    public int? VeranstaltungId { get; init; }
    /// <summary>Veranstaltung mit allen (wählbaren) Klassen – <see cref="Klassen"/> lautet dann „Alle Klassen“.</summary>
    public bool AlleKlassen { get; init; }
    /// <summary>Veranstaltung mit allen (wählbaren) Lehrkräften – <see cref="Vertreter"/> lautet dann „Alle Lehrkräfte“.</summary>
    public bool AlleLehrer { get; init; }
}

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
        VertretungsArt.VertretungOhneLehrer => "Vertr. o. Lehrkr.",
        VertretungsArt.Bereitschaftsvertretung => "Bereitschaftsvertretung",
        VertretungsArt.BereitschaftEntfaellt => "Bereitschaft entfällt",
        VertretungsArt.EigenverantwortlichesArbeiten => "eigenverantwortliches Arbeiten",
        VertretungsArt.Lehrertausch => "Lehrertausch",
        VertretungsArt.Teilvertretung => "Teil-Vtr.",
        VertretungsArt.TrotzAbsenz => "Trotz Absenz",
        VertretungsArt.UnterrichtGeaendert => "Unterricht geändert",
        VertretungsArt.Veranstaltung => "Veranst.",
        VertretungsArt.SondereinsatzVormerkung => "Sondereins. zu Vormerkung",
        VertretungsArt.Vormerkung => "Vormerkung",
        VertretungsArt.Raumbuchung => "Raumbuchung",
        VertretungsArt.Verlegt => "verlegt",
        VertretungsArt.Unterricht => "Unterricht",
        _ => a.ToString(),
    };

    /// <summary>Bezeichnung laut Einstellungen ("Farben und Bezeichnungen"), sonst Standard.</summary>
    public static string ArtText(Schuldatei d, VertretungsArt a) =>
        d.Vertretungseinstellungen.Bezeichnungen.TryGetValue(a.ToString(), out var t) && !string.IsNullOrWhiteSpace(t) ? t : ArtText(a);

    /// <summary>Bemerkung für den Aushang: Art der Änderung in Worten (z. B. Bereitschaft, Raumänderung, verlegt von …) und Vertretungstext.</summary>
    public static string Bemerkung(Schuldatei d, Vertretung v, ISet<(int Lehrer, int Stunde)>? bereitschaften = null)
    {
        string Tag(DateOnly t, int? s) => $"{Planung.Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {t:d.M.}{(s is int x ? $" {x}. Std." : "")}";
        var art = v.Art switch
        {
            VertretungsArt.Bereitschaftsvertretung => "Bereitschaft",
            VertretungsArt.Entfall => "Entfall",
            VertretungsArt.Freisetzung => "Freisetzung",
            VertretungsArt.BereitschaftEntfaellt => "",
            VertretungsArt.Raumvertretung => "Raumänderung",
            VertretungsArt.Verlegung when v.VonDatum is DateOnly von => $"verlegt von {Tag(von, v.VonStunde)}",
            VertretungsArt.Verlegung => "Verlegung",
            VertretungsArt.Verlegt when v.VonDatum is DateOnly von2 => $"verlegt von {Tag(von2, v.VonStunde)}",
            VertretungsArt.Betreuung => "Betreuung",
            VertretungsArt.StattVertretung => "statt-Vertretung",
            VertretungsArt.Sondereinsatz => "Sondereinsatz",
            VertretungsArt.Klausur => "Klausur",
            VertretungsArt.Pausenaufsicht => "Pausenaufsicht",
            VertretungsArt.VertretungOhneLehrer => "ohne Lehrkraft",
            VertretungsArt.EigenverantwortlichesArbeiten => "EVA",
            VertretungsArt.Tausch or VertretungsArt.Lehrertausch => "Tausch",
            VertretungsArt.Veranstaltung => "Veranstaltung",
            VertretungsArt.Raumbuchung => "Raumbuchung",
            _ => "",
        };
        if (v.NachDatum is DateOnly nach && v.IstEntfall) art = $"verlegt auf {Tag(nach, v.NachStunde)}";
        // Vertreter hatte zu der Stunde Bereitschaft
        if (art.Length == 0 && v.VertreterId is int vl && bereitschaften?.Contains((vl, v.Stunde)) == true && v.Art is VertretungsArt.Vertretung or VertretungsArt.StattVertretung)
            art = "Bereitschaft";
        // ein freier Text ersetzt die Standardbemerkung nicht, sondern ergänzt sie (ohne den automatischen Hinweis älterer Versionen
        // „(Benutzer) Raumänderung“ – eine Raumänderung aus einer Buchung hat keinen Vertretungstext)
        var text = Austausch.StundenplanAenderungen.ExportText(v.Text);
        if (string.IsNullOrWhiteSpace(text)) return art;
        if (art.Length == 0 || text.Contains(art, StringComparison.CurrentCultureIgnoreCase)) return text;
        return $"{art} – {text}";
    }

    public static string StundeText(Vertretung v) => v.Art == VertretungsArt.Pausenaufsicht ? $"{v.Stunde}/{v.Stunde + 1}" : v.Stunde.ToString();

    public static List<PlanZeile> Zeilen(Schuldatei d, DateOnly datum, bool mitNichtDrucken = false)
    {
        var ix = new DatenIndex(d);
        var nichtDruckFaecher = d.Faecher.Where(f => f.NichtDrucken).Select(f => f.Id).ToHashSet();
        // Bereitschaftsstunden des Tages (für die Bemerkung „Bereitschaft“)
        var periode = d.PeriodeAm(datum);
        var tagIndex = Kalender.TagIndex(d, datum);
        var bereitUnterricht = (periode.Unterrichte ?? d.Unterrichte).Where(u => u.Zeilen.Any(z => Vertretungen.IstBereitschaftsfach(d, z.FachId))).ToDictionary(u => u.Id);
        var bereitschaften = periode.Plan.Where(p => p.Tag == tagIndex && bereitUnterricht.ContainsKey(p.UnterrichtId))
            .SelectMany(p => bereitUnterricht[p.UnterrichtId].Zeilen.Where(z => z.LehrerId.HasValue).Select(z => (z.LehrerId!.Value, p.Stunde))).ToHashSet();
        var gruende = d.Abwesenheitsgruende.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        var am = Vertretungen.Am(d, datum);
        // Veranstaltung: je Veranstaltung und Stunde eine Zeile statt einer je teilnehmender Lehrkraft (Lehrkräftekonferenz,
        // pädagogischer Tag: sonst Hunderte gleicher Zeilen). Nummer der Zeile ist die kleinste Vtr-Nr. der Stunde (auch im Export).
        var vaNummer = am.Where(v => v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId.HasValue)
            .GroupBy(v => (Va: v.VeranstaltungId!.Value, v.Stunde)).ToDictionary(g => g.Key, g => g.Min(v => v.Id));
        var veranstaltungen = d.Veranstaltungen.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var gefiltert = am
            .Where(v => mitNichtDrucken || (!v.NichtDrucken && !(v.FachId is int f && nichtDruckFaecher.Contains(f))))
            .Where(v => v.Art is not (VertretungsArt.Veranstaltung or VertretungsArt.Vormerkung) || mitNichtDrucken || v.VertreterId.HasValue)
            .ToList();
        var vaZeilen = gefiltert.Where(v => v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int va && veranstaltungen.ContainsKey(va))
            .GroupBy(v => (Va: v.VeranstaltungId!.Value, v.Stunde)).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList());
        var res = new List<PlanZeile>();
        foreach (var v in gefiltert)
        {
            if (v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int vaId && vaZeilen.TryGetValue((vaId, v.Stunde), out var gruppe))
            {
                if (!ReferenceEquals(gruppe[0], v)) continue;
                res.Add(VeranstaltungsZeile(d, ix, veranstaltungen[vaId], gruppe, vaNummer[(vaId, v.Stunde)], gruende));
                continue;
            }
            res.Add(new PlanZeile(
                v.Datum, v.Stunde, ix.KKlassen(v.NeueKlassenIds), ix.KLehrer(v.LehrerId),
                v.IstEntfall || v.Art == VertretungsArt.Vormerkung ? "---" : v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten ? "+++" :
                v.VertreterId.HasValue ? ix.KLehrer(v.VertreterId) : "???",
                ix.KFach(v.FachId), ix.KFach(v.VertretungsfachId ?? v.FachId),
                v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KRaum(v.RaumId),
                v.IstEntfall ? "" : v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) :
                string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => ix.KRaum(x))),
                ArtText(d, v.Art), v.Text, v.NeueKlassenIds, v.LehrerId, v.VertreterId,
                v.VonDatum.HasValue ? $"{Planung.Planungsdienst.TageKurz[((int)v.VonDatum.Value.DayOfWeek + 6) % 7]}-{v.VonDatum:dd.MM.} / {v.VonStunde}" : "",
                v.NachDatum.HasValue ? $"{Planung.Planungsdienst.TageKurz[((int)v.NachDatum.Value.DayOfWeek + 6) % 7]}-{v.NachDatum:dd.MM.} / {v.NachStunde}" : "",
                v.GrundId is int gid && gruende.TryGetValue(gid, out var gr) && gr.Vertraulich ? "" : v.Grund, v.Id,
                v.Schuelergruppe, Bemerkung(d, v, bereitschaften), v.Art) { VeranstaltungId = v.Art == VertretungsArt.Veranstaltung ? v.VeranstaltungId : null });
        }
        return res.OrderBy(z => z.Klassen, StringComparer.Create(Format.De, true)).ThenBy(z => z.Stunde).ToList();
    }

    /// <summary>
    /// Eine Zeile für alle Teilnahmen an einer Veranstaltung in einer Stunde: Vertreter „Alle Lehrkräfte“ bzw. die Liste der
    /// Lehrkräfte (offene Plätze „???“), Klassen „Alle Klassen“ bzw. die Liste; die Klassen-Ids bleiben vollständig (Filter, Export).
    /// </summary>
    private static PlanZeile VeranstaltungsZeile(Schuldatei d, DatenIndex ix, Veranstaltung va, List<Vertretung> gruppe, int nummer, Dictionary<int, Abwesenheitsgrund> gruende)
    {
        var v = gruppe[0];
        var lehrer = gruppe.Where(x => x.VertreterId.HasValue).Select(x => x.VertreterId!.Value).Distinct().ToList();
        var offen = gruppe.Count(x => x.VertreterId is null);
        var alleLehrer = VeranstaltungsAuswahl.UmfasstAlle(d, ElementTyp.Lehrer, lehrer, va.Von, va.Bis);
        var vertreter = string.Join(",", new[] { VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, lehrer, va.Von, va.Bis) }.Where(t => t.Length > 0)
            .Concat(Enumerable.Repeat("???", offen)));
        var klassen = v.NeueKlassenIds;
        var alleKlassen = VeranstaltungsAuswahl.UmfasstAlle(d, ElementTyp.Klasse, klassen, va.Von, va.Bis);
        var klassenText = klassen.Count >= 3 ? VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Klasse, klassen, va.Von, va.Bis) : ix.KKlassen(klassen);
        var raum = string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => ix.KRaum(x)));
        return new PlanZeile(v.Datum, v.Stunde, klassenText, "", vertreter, ix.KFach(v.FachId), ix.KFach(v.VertretungsfachId ?? v.FachId), ix.KRaum(v.RaumId), raum,
            ArtText(d, v.Art), v.Text, klassen, null, null, "", "", v.GrundId is int gid && gruende.TryGetValue(gid, out var gr) && gr.Vertraulich ? "" : v.Grund, nummer,
            v.Schuelergruppe, Bemerkung(d, v), v.Art) { VeranstaltungId = va.Id, AlleKlassen = alleKlassen, AlleLehrer = alleLehrer };
    }
}
