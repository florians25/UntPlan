using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Austausch;

/// <summary>Zuordnung eines Elements der Quelldatei zu einem Element der aktuellen Datei (Elementzuordnung beim Vertretungsimport).</summary>
public sealed class ElementZuordnung
{
    public ElementTyp Typ { get; init; }
    public int QuellId { get; init; }
    public string Kurzname { get; init; } = "";
    public string Langname { get; init; } = "";
    /// <summary>Element der aktuellen Datei; null = nicht zugeordnet (Daten mit diesem Element werden nicht übernommen bzw. ohne es).</summary>
    public int? ZielId { get; set; }
}

/// <summary>Ergebnis des Vertretungsimports.</summary>
public sealed class VertretungsimportErgebnis
{
    public int Absenzen { get; set; }
    public int Veranstaltungen { get; set; }
    public int Klausuren { get; set; }
    public int Vertretungen { get; set; }
    public int Tagestexte { get; set; }
    public int Vormerkungen { get; set; }
    /// <summary>Bereits vorhandene (gleiche) Einträge, die beim additiven Import übersprungen wurden.</summary>
    public int Uebersprungen { get; set; }
    public List<string> Hinweise { get; } = new();
    /// <summary>Tage mit importierten Vertretungen.</summary>
    public SortedSet<DateOnly> Tage { get; } = new();
}

/// <summary>
/// „Import Vertretungsplanung“ wie im Handbuch (Kap. 16.7): Alle Daten zur Vertretungsplanung (Absenzen, Veranstaltungen, Klausuren,
/// Vertretungen, Verlegungen, Sondereinsätze, Tagestexte, Vormerkungen) aus einer zweiten Datei übernehmen. Die Elemente werden über
/// ihren Kurznamen zugeordnet (abweichende Zuordnung möglich), Unterrichte über Nummer bzw. Fach, Lehrer und Klassen. Ohne „additiv“ ersetzen
/// die importierten Daten die vorhandenen, „additiv“ behält die vorhandenen Vertretungsdaten und ergänzt sie (Kap. 16.7.1).
/// </summary>
public static class Vertretungsimport
{
    /// <summary>Elementarten, die zugeordnet werden.</summary>
    public static readonly ElementTyp[] Arten = { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum, ElementTyp.Fach, ElementTyp.Abwesenheitsgrund, ElementTyp.Gang };

    /// <summary>Vorschlag der Elementzuordnung: gleicher Kurzname (Groß-/Kleinschreibung egal).</summary>
    public static List<ElementZuordnung> Zuordnung(Schuldatei ziel, Schuldatei quelle)
    {
        var res = new List<ElementZuordnung>();
        foreach (var typ in Arten)
        {
            var zielNamen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ziel.AlleElemente(typ)) zielNamen.TryAdd(e.Kurzname.Trim(), e.Id);
            foreach (var e in quelle.AlleElemente(typ))
                res.Add(new ElementZuordnung
                {
                    Typ = typ, QuellId = e.Id, Kurzname = e.Kurzname, Langname = e.Langname,
                    ZielId = zielNamen.TryGetValue(e.Kurzname.Trim(), out var id) ? id : null,
                });
        }
        return res;
    }

    /// <summary>Importiert die Vertretungsdaten der Quelle in das Ziel (siehe Klassenbeschreibung).</summary>
    public static VertretungsimportErgebnis Importieren(Schuldatei ziel, Schuldatei quelle, bool additiv, IReadOnlyList<ElementZuordnung>? zuordnung = null)
    {
        zuordnung ??= Zuordnung(ziel, quelle);
        var erg = new VertretungsimportErgebnis();
        var karte = zuordnung.Where(z => z.ZielId.HasValue).ToDictionary(z => (z.Typ, z.QuellId), z => z.ZielId!.Value);
        int? M(ElementTyp t, int? id) => id is int i && karte.TryGetValue((t, i), out var z) ? z : null;
        List<int> ML(ElementTyp t, IEnumerable<int> ids) => ids.Select(i => M(t, i)).OfType<int>().Distinct().ToList();
        var fehlend = new HashSet<string>();
        void Fehlt(ElementTyp t, int id)
        {
            var z = zuordnung.FirstOrDefault(x => x.Typ == t && x.QuellId == id);
            fehlend.Add($"{t}: {z?.Kurzname ?? id.ToString()}");
        }
        bool Vollstaendig(ElementTyp t, IEnumerable<int> ids)
        {
            var ok = true;
            foreach (var i in ids) if (M(t, i) is null) { Fehlt(t, i); ok = false; }
            return ok;
        }

        if (!additiv)
        {
            ziel.Abwesenheiten.Clear();
            ziel.Veranstaltungen.Clear();
            ziel.Klausuren.Clear();
            ziel.Vertretungen.Clear();
            ziel.Tagestexte.Clear();
            ziel.Vormerkungen.Clear();
        }

        // ---- Absenzen ----
        var absenzKarte = new Dictionary<int, int>();
        foreach (var a in quelle.Abwesenheiten)
        {
            var typ = a.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
            if (M(typ, a.ElementId) is not int el) { Fehlt(typ, a.ElementId); continue; }
            var grund = M(ElementTyp.Abwesenheitsgrund, a.GrundId);
            var gleich = ziel.Abwesenheiten.FirstOrDefault(x => x.Art == a.Art && x.ElementId == el && x.Von == a.Von && x.Bis == a.Bis && x.StundeVon == a.StundeVon && x.StundeBis == a.StundeBis);
            if (gleich != null) { absenzKarte[a.Id] = gleich.Id; erg.Uebersprungen++; continue; }
            var neu = new Abwesenheit
            {
                Id = ziel.NeueId(), Art = a.Art, ElementId = el, Von = a.Von, Bis = a.Bis, StundeVon = a.StundeVon, StundeBis = a.StundeBis, GrundId = grund,
                Text = a.Text, BegleitLehrerIds = ML(ElementTyp.Lehrer, a.BegleitLehrerIds), Erstellt = a.Erstellt, Benutzer = a.Benutzer,
            };
            absenzKarte[a.Id] = neu.Id;
            ziel.Abwesenheiten.Add(neu);
            erg.Absenzen++;
        }

        // ---- Veranstaltungen ----
        var vaKarte = new Dictionary<int, int>();
        foreach (var va in quelle.Veranstaltungen)
        {
            Vollstaendig(ElementTyp.Klasse, va.KlassenIds.Concat(va.KlassenteilIds));
            Vollstaendig(ElementTyp.Lehrer, va.LehrerIds);
            var klassen = ML(ElementTyp.Klasse, va.KlassenIds);
            var lehrer = ML(ElementTyp.Lehrer, va.LehrerIds);
            var gleich = ziel.Veranstaltungen.FirstOrDefault(x => x.Von == va.Von && x.Bis == va.Bis && x.StundeVon == va.StundeVon && x.StundeBis == va.StundeBis
                                                                  && x.Text == va.Text && x.KlassenIds.SequenceEqual(klassen) && x.LehrerIds.SequenceEqual(lehrer));
            if (gleich != null) { vaKarte[va.Id] = gleich.Id; erg.Uebersprungen++; continue; }
            var neu = new Veranstaltung
            {
                Id = ziel.NeueId(), Text = va.Text, Von = va.Von, Bis = va.Bis, StundeVon = va.StundeVon, StundeBis = va.StundeBis, KlassenIds = klassen, LehrerIds = lehrer,
                RaumIds = ML(ElementTyp.Raum, va.RaumIds), GrundId = M(ElementTyp.Abwesenheitsgrund, va.GrundId), Schuelergruppe = va.Schuelergruppe, Zaehlen = va.Zaehlen,
                Wert = va.Wert, KlassenteilIds = ML(ElementTyp.Klasse, va.KlassenteilIds),
                Zusatz = va.Zusatz.Where(z => M(ElementTyp.Lehrer, z.LehrerId).HasValue).Select(z => new VeranstaltungsZusatz { Datum = z.Datum, LehrerId = M(ElementTyp.Lehrer, z.LehrerId)!.Value }).ToList(),
            };
            vaKarte[va.Id] = neu.Id;
            foreach (var e in va.Ersatz)
                if (M(ElementTyp.Lehrer, e.LehrerId) is int l)
                    neu.Ersatz.Add(new VeranstaltungsErsatz { Datum = e.Datum, LehrerId = l, ErsatzId = M(ElementTyp.Lehrer, e.ErsatzId), AbwesenheitId = e.AbwesenheitId is int ab && absenzKarte.TryGetValue(ab, out var na) ? na : null });
            ziel.Veranstaltungen.Add(neu);
            erg.Veranstaltungen++;
        }

        // ---- Unterrichte: gleiche Nummer mit gleichem Inhalt, sonst gleicher Inhalt (Fächer, Lehrer, Klassen) ----
        var uKarte = new Dictionary<(int Id, int PeriodeQuelle), int?>();
        int? U(int? uid, DateOnly datum)
        {
            if (uid is not int id) return null;
            var pq = quelle.PeriodeAm(datum);
            if (uKarte.TryGetValue((id, pq.Id), out var vorhanden)) return vorhanden;
            int? res = null;
            var qu = (pq.Unterrichte ?? new()).FirstOrDefault(x => x.Id == id);
            if (qu != null)
            {
                var sig = Signatur(qu, (t, i) => M(t, i));
                var zielU = ziel.PeriodeAm(datum).Unterrichte ?? new();
                var gleicheNr = zielU.FirstOrDefault(x => x.Id == id);
                if (gleicheNr != null && Signatur(gleicheNr, (_, i) => i) == sig) res = gleicheNr.Id;
                else res = zielU.FirstOrDefault(x => Signatur(x, (_, i) => i) == sig)?.Id;
            }
            uKarte[(id, pq.Id)] = res;
            return res;
        }

        // ---- Klausuren ----
        var klausurKarte = new Dictionary<int, int>();
        foreach (var k in quelle.Klausuren)
        {
            if (!Vollstaendig(ElementTyp.Klasse, k.KlassenIds) && k.KlassenIds.Count > 0 && ML(ElementTyp.Klasse, k.KlassenIds).Count == 0) continue;
            var klassen = ML(ElementTyp.Klasse, k.KlassenIds);
            var gleich = ziel.Klausuren.FirstOrDefault(x => x.Datum == k.Datum && x.StundeVon == k.StundeVon && x.StundeBis == k.StundeBis && x.Name == k.Name && x.KlassenIds.SequenceEqual(klassen));
            if (gleich != null) { klausurKarte[k.Id] = gleich.Id; erg.Uebersprungen++; continue; }
            var neu = new Klausur
            {
                Id = ziel.NeueId(), Name = k.Name, Text = k.Text, Datum = k.Datum, StundeVon = k.StundeVon, StundeBis = k.StundeBis, KlassenIds = klassen,
                LehrerIds = ML(ElementTyp.Lehrer, k.LehrerIds), RaumIds = ML(ElementTyp.Raum, k.RaumIds), UnterrichtId = U(k.UnterrichtId, k.Datum),
                UnterrichtEntfaellt = k.UnterrichtEntfaellt, Aktiv = k.Aktiv, Statistik = k.Statistik, RaumVerdraengen = k.RaumVerdraengen,
            };
            klausurKarte[k.Id] = neu.Id;
            ziel.Klausuren.Add(neu);
            erg.Klausuren++;
        }

        // ---- Vormerkungen ----
        var vmKarte = new Dictionary<int, int>();
        foreach (var vm in quelle.Vormerkungen)
        {
            var neu = new Vormerkung
            {
                Id = ziel.NeueId(), Quelle = vm.Quelle, Stunden = vm.Stunden, FachId = M(ElementTyp.Fach, vm.FachId), LehrerIds = ML(ElementTyp.Lehrer, vm.LehrerIds),
                KlassenIds = ML(ElementTyp.Klasse, vm.KlassenIds), RaumId = M(ElementTyp.Raum, vm.RaumId), UnterrichtId = vm.Datum is DateOnly vd ? U(vm.UnterrichtId, vd) : null,
                Datum = vm.Datum, Stunde = vm.Stunde, GueltigVon = vm.GueltigVon, GueltigBis = vm.GueltigBis, Text = vm.Text, VerplantAm = vm.VerplantAm.ToList(),
            };
            vmKarte[vm.Id] = neu.Id;
            ziel.Vormerkungen.Add(neu);
            erg.Vormerkungen++;
        }

        // ---- Tagestexte ----
        foreach (var t in quelle.Tagestexte)
        {
            if (ziel.Tagestexte.Any(x => x.Datum == t.Datum && x.Text == t.Text)) { erg.Uebersprungen++; continue; }
            var typ = t.Art == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
            ziel.Tagestexte.Add(new Tagestext
            {
                Datum = t.Datum, Text = t.Text, TextRtf = t.TextRtf, TextHtml = t.TextHtml, NurLehrer = t.NurLehrer, Art = t.Art,
                ElementIds = t.Art is TagestextArt.Klassen or TagestextArt.Lehrer ? ML(typ, t.ElementIds) : t.ElementIds.ToList(), GueltigTage = t.GueltigTage, Portal = t.Portal,
            });
            erg.Tagestexte++;
        }

        // ---- Vertretungen (auch Verlegungen und Sondereinsätze) ----
        var verlegungKarte = new Dictionary<int, int>();
        var kopplungKarte = new Dictionary<int, int>();
        var naechsteKopplung = ziel.Vertretungen.Select(v => v.Kopplung).DefaultIfEmpty(0).Max() + 1;
        var vorhandeneSchluessel = additiv ? ziel.Vertretungen.Select(VSchluessel).ToHashSet() : new HashSet<string>();
        foreach (var v in quelle.Vertretungen)
        {
            var uid = U(v.UnterrichtId, v.Datum);
            if (v.UnterrichtId.HasValue && uid is null)
            {
                erg.Hinweise.Add($"{Format.Datum(v.Datum)}, {v.Stunde}. Std.: Vertretung {v.Id} – Unterricht {v.UnterrichtId} nicht in der aktuellen Datei, nicht übernommen.");
                continue;
            }
            var w = v.Kopie();
            w.UnterrichtId = uid;
            w.LehrerId = M(ElementTyp.Lehrer, v.LehrerId);
            w.VertreterId = M(ElementTyp.Lehrer, v.VertreterId);
            w.ErsatzFuerLehrerId = M(ElementTyp.Lehrer, v.ErsatzFuerLehrerId);
            w.FachId = M(ElementTyp.Fach, v.FachId);
            w.VertretungsfachId = M(ElementTyp.Fach, v.VertretungsfachId);
            w.KlassenIds = ML(ElementTyp.Klasse, v.KlassenIds);
            w.VertretungsKlassenIds = v.VertretungsKlassenIds is null ? null : ML(ElementTyp.Klasse, v.VertretungsKlassenIds);
            w.RaumId = M(ElementTyp.Raum, v.RaumId);
            w.VertretungsraumId = M(ElementTyp.Raum, v.VertretungsraumId);
            w.ZusatzraumIds = ML(ElementTyp.Raum, v.ZusatzraumIds);
            w.GrundId = M(ElementTyp.Abwesenheitsgrund, v.GrundId);
            w.GangId = M(ElementTyp.Gang, v.GangId);
            if (v.GangId.HasValue && w.GangId is null) { Fehlt(ElementTyp.Gang, v.GangId.Value); continue; }
            if (v.VertreterId.HasValue && w.VertreterId is null) Fehlt(ElementTyp.Lehrer, v.VertreterId.Value);
            w.AbwesenheitId = v.AbwesenheitId is int a && absenzKarte.TryGetValue(a, out var na) ? na : null;
            w.VeranstaltungId = v.VeranstaltungId is int va && vaKarte.TryGetValue(va, out var nva) ? nva : null;
            w.KlausurId = v.KlausurId is int k && klausurKarte.TryGetValue(k, out var nk) ? nk : null;
            w.VormerkungId = v.VormerkungId is int vm && vmKarte.TryGetValue(vm, out var nvm) ? nvm : null;
            if (v.VerlegungsId is int vl)
            {
                if (!verlegungKarte.TryGetValue(vl, out var nvl)) verlegungKarte[vl] = nvl = ziel.NeueId();
                w.VerlegungsId = nvl;
            }
            if (v.Kopplung > 0)
            {
                if (!kopplungKarte.TryGetValue(v.Kopplung, out var nkp)) kopplungKarte[v.Kopplung] = nkp = naechsteKopplung++;
                w.Kopplung = nkp;
            }
            if (additiv && vorhandeneSchluessel.Contains(VSchluessel(w))) { erg.Uebersprungen++; continue; }
            w.Id = ziel.NeueId();
            ziel.Vertretungen.Add(w);
            vorhandeneSchluessel.Add(VSchluessel(w));
            erg.Tage.Add(w.Datum);
            erg.Vertretungen++;
        }
        foreach (var f in fehlend.OrderBy(x => x)) erg.Hinweise.Insert(0, $"Nicht zugeordnet: {f}");
        return erg;
    }

    /// <summary>Gleiche Vertretung (für den additiven Import): Tag, Stunde, Art, Unterricht/Zeile, Lehrer, Vertreter, Klassen, Gang.</summary>
    private static string VSchluessel(Vertretung v) =>
        $"{v.Datum:yyyyMMdd}/{v.Stunde}/{v.Art}/{v.UnterrichtId}/{v.ZeilenIndex}/{v.LehrerId}/{v.VertreterId}/{v.GangId}/{string.Join(",", v.KlassenIds.OrderBy(x => x))}";

    /// <summary>Inhalt eines Unterrichts (Fächer, Lehrer, Klassen je Zeile, nach Zuordnung) für den Vergleich zwischen den Dateien.</summary>
    private static string Signatur(Unterricht u, Func<ElementTyp, int, int?> m) =>
        string.Join("|", u.Zeilen.Select(z => $"{(z.FachId is int f ? m(ElementTyp.Fach, f) : null)};{(z.LehrerId is int l ? m(ElementTyp.Lehrer, l) : null)};" +
                                              string.Join(",", z.KlassenIds.Select(k => m(ElementTyp.Klasse, k)).OrderBy(x => x)))
            .OrderBy(x => x, StringComparer.Ordinal));
}
