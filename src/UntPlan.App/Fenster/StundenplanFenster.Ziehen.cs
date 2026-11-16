using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Manuelles Planen per Drag &amp; Drop im Stundenplan (Handbuch Stundenplan Kap. 5.2): Ablegen einer Stunde bzw. eines Blocks mit der
/// Rückfrage „Block speichern“ (Block speichern / Kollision erzeugen / 2-er-Tausch / Tauschkette, „Speichern mit Raum-Kollision“).
/// </summary>
public sealed partial class StundenplanFenster
{
    /// <summary>
    /// Stunde oder ganzen Block eines Unterrichts an (<paramref name="zielTag"/>, <paramref name="zielStart"/>) ablegen. Ist das Ziel frei,
    /// wird verschoben; behindert ein anderer Unterricht, fragt „Block speichern - Unterricht: …“ nach der gewünschten Aktion.
    /// </summary>
    private void BlockAblegen(List<Planstunde> alle, int zielTag, int zielStart)
    {
        var q = alle.OrderBy(x => x.Stunde).ToList();
        var u = Ix.Unterrichte[q[0].UnterrichtId];
        var n = q.Count;
        var (vonTag, vonStunde) = (q[0].Tag, q[0].Stunde);
        if (zielStart < 1 || zielStart + n - 1 > D.Zeitraster.StundenProTag) { Ui.Info("Die Stunden passen an dieser Position nicht in das Zeitraster."); return; }
        // Rückfragen wie im Vorbild: klein neben der Maus, mit „nicht mehr anzeigen“
        if (q.Any(x => x.Fixiert) && !Warnhinweis.Frage(this, "fixiert", $"Unterricht ist fixiert. Unterricht: {u.Id}\n{Planungsdienst.Tagname(D, vonTag)}-{vonStunde}\nUnterricht trotzdem entplanen ?")) return;
        if (n == 1 && FachZweimalAmTag(u, zielTag, zielStart, q[0]) && !Warnhinweis.Frage(this, "fach2x", $"Fach 2x / Tag\nTrotzdem verplanen ? (Unterricht: {u.Id})", dauerhaft: true)) return;
        var stellen = Enumerable.Range(zielStart, n).Select(s => (zielTag, s)).ToList();
        var behindernd = Behinderung.AnStellen(D, Ix, u, stellen, q);
        var tausch = Planungsdienst.TauschPruefen(D, Ix, q, zielTag, zielStart, out _);
        var kette = behindernd.Count > 0 && _plan.Format.DragDropTauschketten ? Ziehbewertung.KettePruefen(D, Ix, q, zielTag, zielStart) : null;
        var wahl = BlockSpeichernWahl.BlockSpeichern;
        var raumKollision = false;
        if (behindernd.Count > 0 || tausch != null)
        {
            var text = Ziehbewertung.BlockSpeichernText(D, Ix, u, (vonTag, vonStunde), zielTag, zielStart, behindernd);
            var tauschText = tausch is { Partner.Count: > 0 } && Ix.Unterrichte.TryGetValue(tausch.Partner[0].UnterrichtId, out var pu)
                ? Ziehbewertung.TauschOption(D, Ix, u, vonTag, vonStunde, pu, zielTag, zielStart) : null;
            var antwort = BlockSpeichernDialog.Fragen(this, Ziehbewertung.BlockSpeichernTitel(Ix, u), text, tauschText,
                kette is null ? null : Ziehbewertung.KettenOption(D, kette), Ziehbewertung.RaumBelegt(D, Ix, u, zielTag, zielStart, n, q));
            if (antwort is null) return;
            wahl = antwort.Wahl;
            raumKollision = antwort.RaumKollision;
        }
        var idxs = q.Select(x => D.Stundenplan.IndexOf(x)).ToList();
        if (idxs.Any(i => i < 0)) return;
        var raeumeVorher = q[0].Raeume.Where(r => r.HasValue).Select(r => r!.Value).Distinct().ToList();
        switch (wahl)
        {
            case BlockSpeichernWahl.Tausch when tausch != null:
                Aendern(n > 1 ? $"Block tauschen ({n} Stunden)" : "Stunden tauschen", AenderungsArt.Stundenplan, d =>
                {
                    var t = Planungsdienst.TauschPruefen(d, new DatenIndex(d), idxs.Select(i => d.Stundenplan[i]).ToList(), zielTag, zielStart, out var g);
                    if (t is null) throw new InvalidOperationException(g);
                    Planungsdienst.Tauschen(d, t);
                });
                break;
            case BlockSpeichernWahl.Tauschkette when kette != null:
                Aendern($"Tauschkette: {Ziehbewertung.Position(D, vonTag, vonStunde)} -> {Ziehbewertung.Position(D, zielTag, zielStart)}, " +
                        $"{Ziehbewertung.Position(D, zielTag, kette.PartnerVonStunde)} -> {Ziehbewertung.Position(D, kette.PartnerTag, kette.PartnerStunde)}", AenderungsArt.Stundenplan, d =>
                {
                    if (!Ziehbewertung.KetteAusfuehren(d, kette)) throw new InvalidOperationException("Die Tauschkette ist nicht mehr möglich.");
                });
                break;
            default:
                var weg = wahl == BlockSpeichernWahl.BlockSpeichern ? behindernd.Select(h => D.Stundenplan.IndexOf(h.Planstunde)).Where(i => i >= 0).ToList() : new List<int>();
                Aendern(weg.Count > 0 ? $"Stunde verschieben ({weg.Count} behindernde entplant)" : n > 1 ? $"{n} Stunden verschieben" : "Stunde verschieben", AenderungsArt.Stundenplan, d =>
                {
                    var stunden = idxs.Select(i => d.Stundenplan[i]).ToList();
                    var entfernen = weg.Select(i => d.Stundenplan[i]).ToList();
                    foreach (var x in entfernen) d.Stundenplan.Remove(x);
                    for (var i = 0; i < stunden.Count; i++) Planungsdienst.Verschieben(d, stunden[i], zielTag, zielStart + i, false, false);
                    if (raumKollision)
                    {
                        var ix = new DatenIndex(d);
                        foreach (var p in stunden) Ziehbewertung.RaumKollisionSetzen(ix, ix.Unterrichte[p.UnterrichtId], p);
                    }
                });
                break;
        }
        _plan.Ausgewaehlt = D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == u.Id && x.Tag == zielTag && x.Stunde == zielStart);
        Neuaufbau(false);
        if (wahl is not (BlockSpeichernWahl.Tausch or BlockSpeichernWahl.Tauschkette)) RaeumeNichtVerplantMelden(raeumeVorher, _plan.Ausgewaehlt);
    }

    /// <summary>
    /// Nicht verplanten Unterricht aus der Ablage auf ein belegtes Feld legen: „Block speichern“ (behindernde entplanen) oder
    /// „Kollision erzeugen“. Liefert null bei Abbrechen, sonst (behindernde entplanen, Raum-Kollision).
    /// </summary>
    private (bool Entplanen, bool RaumKollision)? NeuAblegenFragen(Unterricht u, int tag, int stunde, int laenge, IReadOnlyCollection<Hindernis> behindernd)
    {
        var antwort = BlockSpeichernDialog.Fragen(this, Ziehbewertung.BlockSpeichernTitel(Ix, u), Ziehbewertung.BlockSpeichernText(D, Ix, u, null, tag, stunde, behindernd),
            raumBelegt: Ziehbewertung.RaumBelegt(D, Ix, u, tag, stunde, laenge));
        return antwort is null ? null : (antwort.Wahl == BlockSpeichernWahl.BlockSpeichern, antwort.RaumKollision);
    }

    // ------------------------------------------------------------------------------------------
    // Ablage „nicht verplant“, Farblegende, Stundenlupe als Ziel
    // ------------------------------------------------------------------------------------------

    /// <summary>Strg+Klick in die Ablage: Doppelstunden und Blöcke als Einzelstunden anzeigen (und verplanen).</summary>
    private bool _poolEinzeln;
    private readonly Steuerelemente.ZiehLegende _legende = new();

    /// <summary>Für den Selbsttest: Ablage zeigt Doppelstunden als Einzelstunden.</summary>
    internal bool PoolEinzeln => _poolEinzeln;

    /// <summary>Selbsttest/Tastatur: wie Strg+Klick in die Ablage.</summary>
    internal void PoolTeilen()
    {
        _poolEinzeln = !_poolEinzeln;
        PoolAufbauen();
    }

    /// <summary>Stapel der Ablage für einen Unterricht: (Blocklänge, Anzahl gleich langer Blöcke) – geteilt nur Einzelstunden.</summary>
    private List<(int Laenge, int Anzahl)> PoolStapel(Unterricht u)
    {
        var bloecke = Planungsdienst.OffeneBloecke(D, Ix, u);
        if (_poolEinzeln) return new() { (1, Math.Max(1, bloecke.Sum())) };
        return bloecke.GroupBy(b => b).OrderByDescending(g => g.Key).Select(g => (g.Key, g.Count())).ToList();
    }

    /// <summary>Farblegende, X-Mauszeiger über der Lupe und Strg+Klick in die freie Ablage einrichten (aus dem Konstruktor).</summary>
    private void ZiehenEinrichten(Panel mitte)
    {
        _legende.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        mitte.Controls.Add(_legende);
        _plan.ZiehenAktivGeaendert += aktiv =>
        {
            if (aktiv)
            {
                _legende.Modus(_plan.Tagesmodus);
                _legende.Location = new Point(Math.Max(0, mitte.ClientSize.Width - _legende.Width - (_pool.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 2),
                    Math.Max(0, mitte.ClientSize.Height - _legende.Height - 2));
                _legende.BringToFront();
            }
            _legende.Visible = aktiv && !_klein && _pool.Visible;
        };
        _lupe.DragEnter += (_, _) => _plan.EntplanenZiel = true;
        _lupe.DragLeave += (_, _) => _plan.EntplanenZiel = false;
        _lupe.DragDrop += (_, _) => _plan.EntplanenZiel = false;
        _pool.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0 && !_plan.Tagesmodus) PoolTeilen();
        };
    }

    /// <summary>Selbsttest: Texte der Farblegende beim Ziehen.</summary>
    internal IReadOnlyList<string> LegendenTexte => _legende.Texte;
}
