using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>
/// Tauschkette beim Ziehen (Handbuch Stundenplan Kap. 5.2.5 b/c, „blauer Pfeil“): Die gezogenen Stunden kommen an die Zielposition,
/// die dort liegende Stunde (bzw. der Block) des Partners wird nicht an die Quelle getauscht, sondern an einen freien Platz verlegt
/// (<see cref="PartnerTag"/>, <see cref="PartnerStunde"/>), z. B. „Tauschkette: Di-3 -&gt; Do-7“.
/// </summary>
public sealed record Kettentausch(int UnterrichtId, int QuellTag, int QuellStunde, int Laenge, int ZielTag, int ZielStunde,
    int PartnerId, int PartnerVonStunde, int PartnerLaenge, int PartnerTag, int PartnerStunde);

/// <summary>Gewählte Option im Dialog „Block speichern“ beim Ablegen auf ein belegtes Feld.</summary>
public enum BlockSpeichernWahl
{
    /// <summary>Block speichern: die gezogene Stunde wird verplant, der behindernde Unterricht entplant.</summary>
    BlockSpeichern,
    /// <summary>Kollision erzeugen: verplanen, die Kollision bleibt bestehen.</summary>
    KollisionErzeugen,
    /// <summary>2-er-Tausch mit der Stunde am Ziel.</summary>
    Tausch,
    /// <summary>Tauschkette: die Stunde am Ziel wird an einen freien Platz verlegt.</summary>
    Tauschkette,
}

/// <summary>
/// Bewertungen beim manuellen Planen per Drag &amp; Drop (Handbuch Stundenplan Kap. 5.2): Grünschattierungen der freien Felder
/// („je dunkler, desto besser“), Tauschketten (blauer Pfeil), Vorschläge für Hohlstunden (Maustaste 2 Sekunden gedrückt) und die
/// Texte des Dialogs „Block speichern“.
/// </summary>
public static class Ziehbewertung
{
    /// <summary>Anzahl der Grünschattierungen (0 = dunkelstes Grün = am besten geeignet).</summary>
    public const int Gruenstufen = 4;

    /// <summary>Option „Block speichern (behindernder Unterricht wird entplant)“.</summary>
    public const string OptionBlockSpeichern = "Block speichern (behindernder Unterricht wird entplant)";
    /// <summary>Option „Kollision erzeugen“.</summary>
    public const string OptionKollision = "Kollision erzeugen";
    /// <summary>Häkchen unter den Optionen (nur aktiv, wenn am Ziel ein Raum belegt ist).</summary>
    public const string RaumKollision = "Speichern mit Raum-Kollision";

    /// <summary>
    /// Grünschattierung je freiem Feld: die Felder werden nach den Kosten laut Gewichtung (Zeitwünsche, Doppelstunden, Hohlstunden …)
    /// gereiht und in <see cref="Gruenstufen"/> gleich große Gruppen geteilt; 0 = am besten. Gleiche Kosten erhalten dieselbe Stufe.
    /// <paramref name="bewegt"/> = die gezogene verplante Stunde (null = nicht verplante Stunde aus der Ablage).
    /// </summary>
    public static Dictionary<(int Tag, int Stunde), int> Gruenschattierung(Schuldatei d, Unterricht u, Planstunde? bewegt, IEnumerable<(int Tag, int Stunde)> felder)
    {
        var res = new Dictionary<(int, int), int>();
        var liste = felder.Distinct().ToList();
        if (liste.Count == 0) return res;
        Platzbewerter bew;
        try { bew = new Platzbewerter(d, u, bewegt); }
        catch (Exception) { return res; }
        if (!bew.Gueltig) return res;
        var kosten = liste.Select(f => (Feld: f, K: bew.Kosten(f.Tag, f.Stunde))).Where(x => !double.IsInfinity(x.K) && !double.IsNaN(x.K)).OrderBy(x => x.K).ToList();
        if (kosten.Count == 0) return res;
        for (var i = 0; i < kosten.Count; i++)
        {
            // gleiche Kosten: Stufe des ersten Feldes mit diesen Kosten
            var erster = kosten.FindIndex(x => Math.Abs(x.K - kosten[i].K) < 1e-9);
            res[kosten[i].Feld] = Math.Min(Gruenstufen - 1, erster * Gruenstufen / kosten.Count);
        }
        return res;
    }

    /// <summary>
    /// Vorschläge für Hohlstunden (Kap. 5.2.6): verplante Stunden (ganze Blöcke) des Elements, die kollisionsfrei auf die freie
    /// Position (<paramref name="tag"/>, <paramref name="stunde"/>) verlegt werden könnten. Fixierte Stunden werden nicht angeboten.
    /// </summary>
    public static List<Planstunde> Hohlstundenvorschlaege(Schuldatei d, DatenIndex ix, ElementTyp typ, int id, int tag, int stunde)
    {
        var res = new List<Planstunde>();
        if (tag < 0 || tag >= d.Zeitraster.TageProWoche || stunde < 1 || stunde > d.Zeitraster.StundenProTag || !d.Zeitraster.Verfuegbar(tag, stunde)) return res;
        var eigene = ix.PlanVon(typ, id).ToList();
        if (eigene.Any(p => p.Tag == tag && p.Stunde == stunde)) return res;
        var gesehen = new HashSet<Planstunde>(ReferenceEqualityComparer.Instance);
        foreach (var p in eigene.OrderBy(p => p.Tag).ThenBy(p => p.Stunde))
        {
            if (gesehen.Contains(p)) continue;
            var block = Planungsdienst.BlockVon(d, p);
            foreach (var b in block) gesehen.Add(b);
            if (block.Any(b => b.Fixiert)) continue;
            for (var k = 0; k < block.Count; k++)
            {
                var start = stunde - k;
                if (start < 1) break;
                if (Planungsdienst.Zielstatus(d, ix, block, tag, start) is Feldstatus.Frei or Feldstatus.KeinRaum)
                {
                    res.AddRange(block);
                    break;
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Tauschkette prüfen: Liegt an der Zielposition genau ein (nicht fixierter, nicht in einem Band liegender) Unterricht, der die gezogenen
    /// Stunden behindert, und lässt er sich – nachdem die gezogenen Stunden dort liegen – kollisionsfrei an einen anderen freien Platz
    /// verlegen (nicht an die Quelle: das wäre der 2-er-Tausch)? <paramref name="besteSuchen"/> = false liefert den ersten passenden
    /// Platz (für die Kennzeichnung beim Ziehen), sonst den laut Gewichtung besten.
    /// </summary>
    public static Kettentausch? KettePruefen(Schuldatei d, DatenIndex ix, IReadOnlyCollection<Planstunde> gezogen, int zielTag, int zielStunde, bool besteSuchen = true)
    {
        var q = gezogen.OrderBy(p => p.Stunde).ToList();
        if (q.Count == 0 || !ix.Unterrichte.TryGetValue(q[0].UnterrichtId, out var u)) return null;
        var n = q.Count;
        int qTag = q[0].Tag, qStunde = q[0].Stunde;
        if (q.Any(p => p.UnterrichtId != u.Id || p.Tag != qTag) || q.Select((p, i) => p.Stunde - i).Distinct().Count() != 1) return null;
        if (q.Any(p => p.Fixiert) || !string.IsNullOrWhiteSpace(u.Band)) return null;
        var zr = d.Zeitraster;
        if (zielTag < 0 || zielTag >= zr.TageProWoche || zielStunde < 1 || zielStunde + n - 1 > zr.StundenProTag) return null;
        if (zielTag == qTag && zielStunde < qStunde + n && qStunde < zielStunde + n) return null;
        for (var j = 0; j < n; j++)
            if (Planungsdienst.VorgabeVerletzt(d, ix, u, zielTag, zielStunde + j)) return null;
        // Partner: alle Hindernisse gehören zu genau einem Unterricht
        var partner = new List<Planstunde>();
        for (var j = 0; j < n; j++)
            foreach (var h in Planungsdienst.Hindernisse(d, ix, u, zielTag, zielStunde + j, q))
                if (!partner.Contains(h.Planstunde)) partner.Add(h.Planstunde);
        if (partner.Count == 0 || partner.Select(p => p.UnterrichtId).Distinct().Count() != 1) return null;
        if (!ix.Unterrichte.TryGetValue(partner[0].UnterrichtId, out var pu) || !string.IsNullOrWhiteSpace(pu.Band)) return null;
        var partnerAlle = partner.SelectMany(p => Planungsdienst.BlockVon(d, p)).Distinct().OrderBy(p => p.Stunde).ToList();
        if (partnerAlle.Any(p => p.Fixiert || p.Tag != zielTag) || partnerAlle.Select((p, i) => p.Stunde - i).Distinct().Count() != 1) return null;
        var pn = partnerAlle.Count;
        var pVon = partnerAlle[0].Stunde;
        // gezogene Stunden gedanklich an das Ziel legen, dann einen Platz für den Partner suchen
        var alt = q.Select(p => (p.Tag, p.Stunde)).ToList();
        try
        {
            for (var i = 0; i < n; i++) { q[i].Tag = zielTag; q[i].Stunde = zielStunde + i; }
            Platzbewerter? bew = null;
            (int Tag, int Stunde, double Kosten)? bester = null;
            for (var t = 0; t < zr.TageProWoche; t++)
                for (var s = 1; s + pn - 1 <= zr.StundenProTag; s++)
                {
                    if (t == zielTag && s < pVon + pn && pVon < s + pn) continue;  // eigener Platz
                    if (t == qTag && s == qStunde) continue;                          // das wäre der 2-er-Tausch
                    if (d.Stundenplan.Any(p => p.UnterrichtId == pu.Id && p.Tag == t && p.Stunde >= s && p.Stunde < s + pn && !partnerAlle.Contains(p))) continue;
                    if (Planungsdienst.Status(d, ix, pu, t, s, pn, partnerAlle) != Feldstatus.Frei) continue;
                    if (!besteSuchen) return Ergebnis(t, s);
                    bew ??= new Platzbewerter(d, pu, partnerAlle[0]);
                    var k = bew.Gueltig ? bew.Kosten(t, s) : 0;
                    if (bester is null || k < bester.Value.Kosten) bester = (t, s, k);
                }
            return bester is { } b ? Ergebnis(b.Tag, b.Stunde) : null;
        }
        finally
        {
            for (var i = 0; i < n; i++) { q[i].Tag = alt[i].Tag; q[i].Stunde = alt[i].Stunde; }
        }

        Kettentausch Ergebnis(int t, int s) => new(u.Id, qTag, qStunde, n, zielTag, zielStunde, pu.Id, pVon, pn, t, s);
    }

    /// <summary>Tauschkette ausführen (die Stunden werden über U-Nr., Tag und Stunde in <paramref name="d"/> gesucht); Räume werden neu gesucht.</summary>
    public static bool KetteAusfuehren(Schuldatei d, Kettentausch k)
    {
        var gezogen = Enumerable.Range(k.QuellStunde, k.Laenge).Select(s => d.Stundenplan.FirstOrDefault(p => p.UnterrichtId == k.UnterrichtId && p.Tag == k.QuellTag && p.Stunde == s)).ToList();
        var partner = Enumerable.Range(k.PartnerVonStunde, k.PartnerLaenge).Select(s => d.Stundenplan.FirstOrDefault(p => p.UnterrichtId == k.PartnerId && p.Tag == k.ZielTag && p.Stunde == s)).ToList();
        if (gezogen.Any(p => p is null) || partner.Any(p => p is null)) return false;
        for (var i = 0; i < gezogen.Count; i++) { gezogen[i]!.Tag = k.ZielTag; gezogen[i]!.Stunde = k.ZielStunde + i; }
        for (var i = 0; i < partner.Count; i++) { partner[i]!.Tag = k.PartnerTag; partner[i]!.Stunde = k.PartnerStunde + i; }
        var ix = new DatenIndex(d);
        foreach (var p in gezogen.Concat(partner))
            if (ix.Unterrichte.TryGetValue(p!.UnterrichtId, out var u)) p.Raeume = Planungsdienst.RaeumeSuchen(d, ix, u, p.Tag, p.Stunde, new[] { p });
        return true;
    }

    /// <summary>Position wie im Vorbild: „Di-5“.</summary>
    public static string Position(Schuldatei d, int tag, int stunde) => $"{Planungsdienst.Tagname(d, tag)}-{stunde}";

    /// <summary>Kurzbezeichnung eines Unterrichts wie im Vorbild: Fach und Lehrer („Mus Callas“).</summary>
    public static string Kurz(DatenIndex ix, Unterricht u) =>
        $"{string.Join(",", u.FachIds.Distinct().Select(f => ix.KFach(f)))} {string.Join(",", u.LehrerIds.Distinct().Select(l => ix.KLehrer(l)))}".Trim();

    /// <summary>Titel des Dialogs: „Block speichern - Unterricht: 35 Mus Callas“.</summary>
    public static string BlockSpeichernTitel(DatenIndex ix, Unterricht u) => $"Block speichern - Unterricht: {u.Id} {Kurz(ix, u)}".TrimEnd();

    /// <summary>
    /// Meldung des Dialogs „Block speichern“: Unterricht mit Quelle und Ziel, Anzahl und Liste der behindernden Unterrichte und die
    /// behindernden Elemente (Lehrer, Klassen, Räume), z. B. „Behindernde Elemente: 2 Callas, 1a“.
    /// </summary>
    public static string BlockSpeichernText(Schuldatei d, DatenIndex ix, Unterricht u, (int Tag, int Stunde)? von, int zielTag, int zielStunde, IReadOnlyCollection<Hindernis> hindernisse)
    {
        var zeilen = new List<string>
        {
            $"Unterricht: {u.Id} {Kurz(ix, u)} - {(von is { } v ? Position(d, v.Tag, v.Stunde) + " --> " : "")}{Position(d, zielTag, zielStunde)}",
            "",
        };
        var unterrichte = hindernisse.Select(h => h.Unterricht).DistinctBy(x => x.Id).ToList();
        if (unterrichte.Count > 0)
        {
            zeilen.Add($"Anderer Unterricht behindert Verplanung - Anzahl hindernde Unterrichte: {unterrichte.Count}");
            zeilen.Add(string.Join(", ", unterrichte.Select((x, i) => i == 0 ? UnterrichtMitFachLehrer(ix, x) : $"Unt: {x.Id}")));
            var elemente = BehinderndeElemente(ix, u, hindernisse);
            zeilen.Add($"Behindernde Elemente: {elemente.Count} {string.Join(", ", elemente)}");
        }
        return string.Join("\n", zeilen);
    }

    /// <summary>„Unt: 34 Fach: D Lehrer: Callas“.</summary>
    public static string UnterrichtMitFachLehrer(DatenIndex ix, Unterricht x) =>
        $"Unt: {x.Id} Fach: {string.Join(",", x.FachIds.Distinct().Select(f => ix.KFach(f)))} Lehrer: {string.Join(",", x.LehrerIds.Distinct().Select(l => ix.KLehrer(l)))}";

    /// <summary>Gemeinsame Elemente, die die Verplanung behindern: Lehrer, dann Klassen, dann Räume (Kurznamen, ohne Doppelte).</summary>
    public static List<string> BehinderndeElemente(DatenIndex ix, Unterricht u, IEnumerable<Hindernis> hindernisse)
    {
        var lehrer = new List<int>();
        var klassen = new List<int>();
        var raeume = new List<string>();
        foreach (var h in hindernisse)
        {
            lehrer.AddRange(h.Unterricht.LehrerIds.Where(l => u.LehrerIds.Contains(l) && !lehrer.Contains(l)));
            klassen.AddRange(h.Unterricht.KlassenIds.Where(k => u.KlassenIds.Contains(k) && !klassen.Contains(k)));
            if (h.Grund.StartsWith("Raum ")) raeume.AddRange(h.Grund[5..].Split(',').Where(r => r.Length > 0 && !raeume.Contains(r)));
        }
        return lehrer.Select(l => ix.KLehrer(l)).Concat(klassen.Select(k => ix.KKlassen(new[] { k }))).Concat(raeume).ToList();
    }

    /// <summary>„2-er-Tausch: Di-5 (Unt.: 35 Mus Callas) &lt;--&gt; Di-3 (Unt.: 39 Fach: Ke Lehrer: Callas)“.</summary>
    public static string TauschOption(Schuldatei d, DatenIndex ix, Unterricht u, int vonTag, int vonStunde, Unterricht partner, int zielTag, int zielStunde) =>
        $"2-er-Tausch: {Position(d, vonTag, vonStunde)} (Unt.: {u.Id} {Kurz(ix, u)}) <--> {Position(d, zielTag, zielStunde)} " +
        $"(Unt.: {partner.Id} Fach: {string.Join(",", partner.FachIds.Distinct().Select(f => ix.KFach(f)))} Lehrer: {string.Join(",", partner.LehrerIds.Distinct().Select(l => ix.KLehrer(l)))})";

    /// <summary>„Tauschkette: Di-3 -&gt; Do-7“ (der verdrängte Unterricht wandert von der Ziel- an die freie Position).</summary>
    public static string KettenOption(Schuldatei d, Kettentausch k) =>
        $"Tauschkette: {Position(d, k.ZielTag, k.PartnerVonStunde)} -> {Position(d, k.PartnerTag, k.PartnerStunde)}";

    /// <summary>
    /// Würde am Ziel ein Raum doppelt belegt (Raum und Ausweichräume belegt)? Dann ist „Speichern mit Raum-Kollision“ wählbar.
    /// </summary>
    public static bool RaumBelegt(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, int laenge, ICollection<Planstunde>? ignorieren = null)
    {
        for (var j = 0; j < laenge; j++)
        {
            if (stunde + j > d.Zeitraster.StundenProTag) break;
            var raeume = Planungsdienst.RaeumeSuchen(d, ix, u, tag, stunde + j, ignorieren);
            for (var i = 0; i < u.Zeilen.Count; i++)
                if (raeume[i] is null && ix.RaumKandidaten(u, u.Zeilen[i]).Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// „Speichern mit Raum-Kollision“: Kopplungszeilen ohne freien Raum erhalten trotzdem ihren ersten Wunschraum (doppelt belegt).
    /// </summary>
    public static void RaumKollisionSetzen(DatenIndex ix, Unterricht u, Planstunde p)
    {
        p.RaeumeErweitern(u.Zeilen.Count);
        for (var i = 0; i < u.Zeilen.Count; i++)
            if (p.Raeume[i] is null && ix.RaumKandidaten(u, u.Zeilen[i]) is { Count: > 0 } k) p.Raeume[i] = k[0];
    }
}
