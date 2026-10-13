using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Stundenplanfenster: Detailbereich (oben), Stundenplan (Mitte), Stundenlupe (unten), nicht verplante Stunden (rechts).
/// Manuelles Planen per Drag &amp; Drop, Tauschen, Fixieren, Raum zuordnen, Tauschvorschläge, Stundenplan-Synchronisation.
/// </summary>
public sealed class StundenplanFenster : MdiFenster, IDruckbar
{
    private ElementTyp _typ;
    private int _id;
    private readonly bool _gross;
    private readonly PlanAnsicht _plan = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _pool = new() { Dock = DockStyle.Right, Width = 150, AutoScroll = true, BackColor = Color.FromArgb(244, 246, 250), AllowDrop = true, Padding = new Padding(4) };
    private readonly DataGridView _lupe = new() { Dock = DockStyle.Bottom, Height = 74 };
    private readonly Label _detail = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 3, 0, 0), BackColor = Color.FromArgb(248, 249, 252) };
    private readonly TabControl _reiter = new() { Dock = DockStyle.Top, Height = 22, Visible = false };
    private readonly ToolStripComboBox _auswahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ToolStripButton _alleElemente;
    private readonly ToolStripButton _sync;
    private readonly ToolStripButton _wochenansicht;
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Short, Width = 105 };
    private bool _laden;

    public static StundenplanFenster Neu(ElementTyp typ, int? id, bool gross)
    {
        var f = new StundenplanFenster(typ, id ?? Stammdaten.Sortiert(AppZustand.D, typ).FirstOrDefault()?.Id ?? 0, gross);
        f.Show();
        return f;
    }

    public static StundenplanFenster? AktivesOderNeu() =>
        AppZustand.Haupt.ActiveMdiChild as StundenplanFenster ?? AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().FirstOrDefault() ?? Neu(ElementTyp.Klasse, null, false);

    public StundenplanFenster(ElementTyp typ, int id, bool gross)
    {
        _typ = typ;
        _id = id;
        _gross = gross;
        HilfeThema = "Stundenpläne";
        Icon = SymbolIcon("stundenplan");
        Size = gross ? new Size(820, 640) : new Size(560, 520);
        Positionieren();

        var ts = Werkzeugleiste();
        ts.Items.Add(_auswahl);
        _auswahl.SelectedIndexChanged += (_, _) => { if (!_laden && _auswahl.SelectedItem is Eintrag e) { _id = e.E.Id; Neuaufbau(); } };
        ts.Items.Add(Knopf("Vorheriges Element", "zurueck", () => Blaettern(-1)));
        ts.Items.Add(Knopf("Nächstes Element", "vor", () => Blaettern(1)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Stundenplan-Einstellungen", "einstellungen", Einstellungen));
        _alleElemente = new ToolStripButton(Symbole.Get("uebersicht")) { ToolTipText = "Alle Elemente im Unterricht", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _alleElemente.CheckedChanged += (_, _) => { _reiter.Visible = _alleElemente.Checked; ReiterAufbauen(); };
        ts.Items.Add(_alleElemente);
        ts.Items.Add(Knopf("Unterr.-Std. fixieren / lösen", "fixieren", Fixieren));
        ts.Items.Add(Knopf("Raum zuordnen", "raumzuordnen", RaumZuordnen));
        ts.Items.Add(Knopf("Tauschvorschläge", "tausch", TauschvorschlaegeZeigen));
        ts.Items.Add(Knopf("Stunde entplanen", "loeschen", Entplanen));
        _sync = new ToolStripButton(Symbole.Get("vertretung")) { ToolTipText = "Stundenplan-Synchronisation", CheckOnClick = true, Checked = AppZustand.Einstellungen.StundenplanSynchronisieren, DisplayStyle = ToolStripItemDisplayStyle.Image };
        ts.Items.Add(_sync);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        ts.Items.Add(Knopf("HTML-Ausgabe", "html", HtmlAusgabe));
        ts.Items.Add(new ToolStripSeparator());
        _wochenansicht = new ToolStripButton("Woche", Symbole.Get("kalender")) { ToolTipText = "Wochenansicht mit Vertretungen", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        _wochenansicht.CheckedChanged += (_, _) => WocheSetzen();
        ts.Items.Add(_wochenansicht);
        ts.Items.Add(new ToolStripControlHost(_datum));
        _datum.Value = DateTime.Today;
        _datum.ValueChanged += (_, _) => WocheSetzen();

        _plan.Format = FormatHolen();
        _plan.AuswahlGeaendert += AuswahlGeaendert;
        _plan.Abgelegt += Abgelegt;
        _plan.Doppelklick += p => { var u = Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId); if (u != null) UnterrichtFenster.Zeigen(_typ is ElementTyp.Raum ? ElementTyp.Unterricht : _typ, _typ is ElementTyp.Raum ? null : _id); };
        _plan.Kontext += Kontextmenue;

        Ui.RasterGestalten(_lupe, false);
        _lupe.RowHeadersVisible = false;
        foreach (var (n, t, w) in new[] { ("U", "U-Nr", 48), ("L", "Lehr.", 50), ("F", "Fa.", 46), ("R", "Rm.", 50), ("K", "Kla.", 80), ("W", "Wst", 36), ("G", "Schülergr.", 70), ("T", "Text", 120), ("X", "Fix", 30) })
            _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });

        _pool.DragEnter += (_, e) => e.Effect = (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("plan:") == true ? DragDropEffects.Move : DragDropEffects.None;
        _pool.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is string s && s.StartsWith("plan:") && int.TryParse(s[5..], out var idx) && idx < D.Stundenplan.Count)
            {
                var p = D.Stundenplan[idx];
                Aendern("Stunde entplanen", AenderungsArt.Stundenplan, d => Planungsdienst.Entplanen(d, p));
                Neuaufbau();
            }
        };
        var poolMenue = new ContextMenuStrip();
        poolMenue.Items.Add("Nicht verplante Stunden neu gruppieren", null, (_, _) => PoolAufbauen());
        _pool.ContextMenuStrip = poolMenue;

        _reiter.SelectedIndexChanged += (_, _) =>
        {
            if (_laden || _reiter.SelectedTab?.Tag is not (ElementTyp t, int i)) return;
            _typ = t;
            _id = i;
            _plan.Format = FormatHolen();
            Neuaufbau(false);
        };

        var mitte = new Panel { Dock = DockStyle.Fill };
        mitte.Controls.Add(_plan);
        mitte.Controls.Add(_pool);
        Controls.Add(mitte);
        Controls.Add(_reiter);
        Controls.Add(_lupe);
        Controls.Add(_detail);
        Controls.Add(ts);
        Neuaufbau();
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
        };
    }

    private sealed record Eintrag(Element E)
    {
        public override string ToString() => E.Kurzname;
    }

    private StundenplanFormat FormatHolen()
    {
        var basis = _typ switch { ElementTyp.Klasse => "Klassenplan", ElementTyp.Lehrer => "Lehrerplan", ElementTyp.Raum => "Raumplan", ElementTyp.Fach => "Fachplan", _ => "Stundenplan" };
        var name = basis + (_gross ? " groß" : "");
        var f = D.Formate.FirstOrDefault(x => x.Typ == _typ && x.Gross == _gross);
        return f ?? new StundenplanFormat { Name = name, Typ = _typ, Gross = _gross, Schriftgroesse = _gross ? 10 : 9 };
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || typ != _typ || _reiter.Visible) return;
        _id = id;
        Neuaufbau();
    }

    /// <summary>Stundenplan-Synchronisation: andere Pläne zeigen die Elemente der aktivierten Stunde.</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !_sync.Checked || _reiter.Visible) return;
        int? neu = _typ switch
        {
            ElementTyp.Lehrer => u.LehrerIds.Cast<int?>().FirstOrDefault(),
            ElementTyp.Klasse => u.KlassenIds.Cast<int?>().FirstOrDefault(),
            ElementTyp.Raum => p?.Raeume.FirstOrDefault(r => r.HasValue),
            ElementTyp.Fach => u.HauptfachId,
            _ => null,
        };
        if (neu is null || neu == _id) { if (p != null) { _plan.Ausgewaehlt = p; _plan.Invalidate(); } return; }
        _id = neu.Value;
        Neuaufbau();
        _plan.Ausgewaehlt = p;
        LupeFuellen(p);
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau(false);
    protected override bool EigeneAenderungenAnzeigen => false;

    private void Blaettern(int r)
    {
        if (_auswahl.Items.Count == 0) return;
        _auswahl.SelectedIndex = Math.Clamp(_auswahl.SelectedIndex + r, 0, _auswahl.Items.Count - 1);
    }

    private void WocheSetzen()
    {
        _plan.Woche = _wochenansicht.Checked ? Kalender.Montag(DateOnly.FromDateTime(_datum.Value)) : null;
        _plan.Invalidate();
        DetailAktualisieren();
    }

    public void Neuaufbau(bool auswahlListe = true)
    {
        if (IsDisposed) return;
        _laden = true;
        if (auswahlListe || _auswahl.Items.Count == 0 || ((Eintrag?)_auswahl.Items[0])?.E.Typ != _typ)
        {
            _auswahl.Items.Clear();
            foreach (var e in Stammdaten.Sortiert(D, _typ)) _auswahl.Items.Add(new Eintrag(e));
        }
        _auswahl.SelectedItem = _auswahl.Items.Cast<Eintrag>().FirstOrDefault(x => x.E.Id == _id);
        _laden = false;
        var el = Ix.Element(_typ, _id);
        var fmtName = _plan.Format.Name.Length > 0 ? _plan.Format.Name : FormatHolen().Name;
        Text = el is null ? $"Stundenplan ({fmtName})" : $"{el.Kurzname} – {(el is Lehrer l ? l.VollerName : el.Langname)} ({fmtName})";
        _plan.Typ = _typ;
        _plan.ElementId = _id;
        if (_plan.Ausgewaehlt != null && !D.Stundenplan.Contains(_plan.Ausgewaehlt)) _plan.Ausgewaehlt = null;
        _plan.Invalidate();
        DetailAktualisieren();
        PoolAufbauen();
        LupeFuellen(_plan.Ausgewaehlt);
    }

    private void DetailAktualisieren()
    {
        var unts = _typ == ElementTyp.Raum ? Enumerable.Empty<Unterricht>() : Ix.UnterrichteVon(_typ, _id).Where(u => !u.Ignorieren).ToList();
        var wst = _typ == ElementTyp.Raum ? Ix.PlanVon(ElementTyp.Raum, _id).Count() : unts.Sum(u => u.Wochenstunden);
        var nv = unts.Sum(u => Ix.NichtVerplant(u));
        var periode = D.AktivePeriode;
        var woche = _plan.Woche.HasValue ? $"   Woche ab {Format.Datum(_plan.Woche.Value)} ({Kalender.WochenTyp(D, _plan.Woche.Value)}-Woche)" : "";
        _detail.Text = $"{wst} Wochenstunden   {nv} nicht verplante Std.   Datumsbereich: {Format.Datum(periode.Von)} – {Format.Datum(D.Einstellungen.SchuljahrBis)} ({periode.Name}){woche}";
    }

    private void PoolAufbauen()
    {
        _pool.SuspendLayout();
        _pool.Controls.Clear();
        _pool.Controls.Add(new Label { Text = "Nicht verplant", Font = Ui.SchriftFett, AutoSize = true, Margin = new Padding(2, 2, 2, 6) });
        if (_typ != ElementTyp.Raum)
        {
            foreach (var u in Ix.UnterrichteVon(_typ, _id).Where(u => !u.Ignorieren && Ix.NichtVerplant(u) > 0))
            {
                var nv = Ix.NichtVerplant(u);
                var bloecke = u.Bloecke();
                var fach = u.HauptfachId is int fid ? Ix.Faecher.GetValueOrDefault(fid) : null;
                var kachel = new Stapel(u, nv, bloecke.FirstOrDefault(), Ui.Farbe(fach?.Hintergrund ?? 0, Color.FromArgb(220, 228, 240)), fach?.Kurzname ?? "?", _typ == ElementTyp.Lehrer ? Ix.KKlassen(u.KlassenIds) : string.Join(",", u.LehrerIds.Select(Ix.KLehrer)));
                kachel.MouseDown += (_, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    LupeFuellen(null, u);
                    kachel.DoDragDrop("unterricht:" + u.Id, DragDropEffects.Move);
                };
                _pool.Controls.Add(kachel);
            }
        }
        _pool.ResumeLayout();
    }

    /// <summary>Stapel nicht verplanter Stunden eines Unterrichts (bei mehr als drei Stunden mit Anzahl).</summary>
    private sealed class Stapel : Control
    {
        private readonly int _anzahl;
        private readonly Color _farbe;
        private readonly string _fach;
        private readonly string _info;
        private readonly int _block;

        public Stapel(Unterricht u, int anzahl, int block, Color farbe, string fach, string info)
        {
            _anzahl = anzahl;
            _farbe = farbe;
            _fach = fach;
            _info = info;
            _block = block;
            Size = new Size(64, 42 + Math.Min(anzahl - 1, 3) * 3);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            new ToolTip().SetToolTip(this, $"U-Nr. {u.Id}: {anzahl} nicht verplante Stunde(n){(block > 1 ? $", Blocklänge {block}" : "")}");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var tiefe = Math.Min(_anzahl - 1, 3);
            for (var i = tiefe; i >= 0; i--)
            {
                var r = new Rectangle(i * 3, (tiefe - i) * 3, Width - 1 - tiefe * 3, Height - 1 - tiefe * 3);
                using (var b = new SolidBrush(_farbe)) g.FillRectangle(b, r);
                g.DrawRectangle(Pens.DimGray, r);
            }
            var top = new Rectangle(0, tiefe * 3, Width - 1 - tiefe * 3, Height - 1 - tiefe * 3);
            var tf = Ui.Textfarbe(_farbe);
            TextRenderer.DrawText(g, _fach + (_block > 1 ? $" ({_block})" : ""), Ui.SchriftFett, new Rectangle(top.X + 2, top.Y + 2, top.Width - 4, 16), tf, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _info, Ui.SchriftKlein, new Rectangle(top.X + 2, top.Y + 18, top.Width - 4, 14), tf, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            if (_anzahl > 3) TextRenderer.DrawText(g, _anzahl.ToString(), Ui.SchriftFett, new Rectangle(top.Right - 16, top.Bottom - 15, 14, 14), Color.DarkRed, TextFormatFlags.Right);
        }
    }

    private void AuswahlGeaendert(Planstunde? p)
    {
        LupeFuellen(p);
        ReiterAufbauen();
        if (p != null && Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) AppZustand.StundeWaehlen(u, p, this);
    }

    private void LupeFuellen(Planstunde? p, Unterricht? unv = null)
    {
        _lupe.Rows.Clear();
        var u = unv ?? (p != null ? Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId) : null);
        if (u is null) return;
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var z = u.Zeilen[i];
            _lupe.Rows.Add(i == 0 ? u.Id.ToString() : "", Ix.KLehrer(z.LehrerId), Ix.KFach(z.FachId), p is null ? Ix.KRaum(z.Wunschraum) : Ix.KRaum(p.RaumFuerZeile(i)),
                Ix.KKlassen(z.KlassenIds), i == 0 ? u.Wochenstunden.ToString() : "", u.Schuelergruppe, i == 0 ? u.Text : z.Zeilentext, p?.Fixiert == true && i == 0 ? "*" : "");
        }
    }

    private void ReiterAufbauen()
    {
        if (!_alleElemente.Checked) return;
        var p = _plan.Ausgewaehlt;
        if (p is null || !Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
        _laden = true;
        _reiter.TabPages.Clear();
        void Add(ElementTyp t, int id)
        {
            var e = Ix.Element(t, id);
            if (e is null) return;
            var tp = new TabPage(e.Kurzname) { Tag = (t, id) };
            _reiter.TabPages.Add(tp);
            if (t == _typ && id == _id) _reiter.SelectedTab = tp;
        }
        foreach (var k in u.KlassenIds) Add(ElementTyp.Klasse, k);
        foreach (var l in u.LehrerIds) Add(ElementTyp.Lehrer, l);
        foreach (var r in p.Raeume.Where(r => r.HasValue)) Add(ElementTyp.Raum, r!.Value);
        _laden = false;
    }

    // ------------------------------------------------------------------------------------------
    // Planen
    // ------------------------------------------------------------------------------------------

    private void Abgelegt(string daten, int tag, int stunde)
    {
        if (daten.StartsWith("plan:") && int.TryParse(daten[5..], out var idx) && idx < D.Stundenplan.Count)
        {
            var p = D.Stundenplan[idx];
            if (p.Tag == tag && p.Stunde == stunde) return;
            var u = Ix.Unterrichte[p.UnterrichtId];
            var status = Planungsdienst.Status(D, Ix, u, tag, stunde, 1, new[] { p });
            var tauschen = false;
            var entplanen = false;
            if (status == Feldstatus.TauschMoeglich)
            {
                var h = Planungsdienst.Hindernisse(D, Ix, u, tag, stunde, new[] { p }).First();
                var wahl = PlanungsKonfliktDialog.Fragen(this, $"Stunde tauschen – Unterricht {u.Id} {Ix.UnterrichtText(u)}",
                    $"Behindernder Unterricht: {h.Unterricht.Id} ({Ix.UnterrichtText(h.Unterricht)}, {h.Grund})", "Stunden tauschen", "Kollision erzeugen");
                if (wahl is null) return;
                tauschen = wahl == 0;
            }
            else if (status == Feldstatus.Kollision)
            {
                var h = Planungsdienst.Hindernisse(D, Ix, u, tag, stunde, new[] { p });
                var wahl = PlanungsKonfliktDialog.Fragen(this, $"Block speichern – Unterricht {u.Id} {Ix.UnterrichtText(u)}",
                    $"Anzahl behindernder Unterrichte: {h.Count}\n" + string.Join("\n", h.Select(x => $"U-Nr. {x.Unterricht.Id}: {Ix.UnterrichtText(x.Unterricht)} ({x.Grund})")),
                    "Block speichern (behindernder Unterricht wird entplant)", "Kollision erzeugen");
                if (wahl is null) return;
                entplanen = wahl == 0;
            }
            var idxZiel = idx;
            Aendern(tauschen ? "Stunden tauschen" : "Stunde verschieben", AenderungsArt.Stundenplan, d => Planungsdienst.Verschieben(d, d.Stundenplan[idxZiel], tag, stunde, tauschen, entplanen));
            _plan.Ausgewaehlt = D.Stundenplan.ElementAtOrDefault(idxZiel);
            Neuaufbau(false);
        }
        else if (daten.StartsWith("unterricht:") && int.TryParse(daten[11..], out var uid) && Ix.Unterrichte.TryGetValue(uid, out var u))
        {
            if (Ix.NichtVerplant(u) <= 0) { Ui.Info("Alle Stunden dieses Unterrichts sind bereits verplant."); return; }
            var status = Planungsdienst.Status(D, Ix, u, tag, stunde);
            var entplanen = false;
            if (status is Feldstatus.Kollision or Feldstatus.TauschMoeglich)
            {
                var h = Planungsdienst.Hindernisse(D, Ix, u, tag, stunde);
                var wahl = PlanungsKonfliktDialog.Fragen(this, $"Block speichern – Unterricht {u.Id} {Ix.UnterrichtText(u)}",
                    string.Join("\n", h.Select(x => $"U-Nr. {x.Unterricht.Id}: {Ix.UnterrichtText(x.Unterricht)} ({x.Grund})")),
                    "Block speichern (behindernder Unterricht wird entplant)", "Kollision erzeugen");
                if (wahl is null) return;
                entplanen = wahl == 0;
            }
            Planstunde? neu = null;
            Aendern("Stunde verplanen", AenderungsArt.Stundenplan, d => neu = Planungsdienst.Verplanen(d, d.Unterrichte.First(x => x.Id == uid), tag, stunde, entplanen));
            _plan.Ausgewaehlt = neu;
            Neuaufbau(false);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Entf = entplanen, F = fixieren (nicht während einer Texteingabe)
        if (ActiveControl is not (TextBoxBase or ComboBox or ToolStrip or DataGridView) && e.Modifiers == Keys.None)
        {
            if (e.KeyCode == Keys.Delete) { Entplanen(); e.Handled = true; }
            else if (e.KeyCode == Keys.F) { Fixieren(); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    private void Fixieren()
    {
        if (_plan.Ausgewaehlt is not { } p) return;
        var idx = D.Stundenplan.IndexOf(p);
        Aendern(p.Fixiert ? "Fixierung lösen" : "Stunde fixieren", AenderungsArt.Stundenplan, d => d.Stundenplan[idx].Fixiert = !d.Stundenplan[idx].Fixiert);
        Neuaufbau(false);
    }

    private void Entplanen()
    {
        if (_plan.Ausgewaehlt is not { } p) return;
        Aendern("Stunde entplanen", AenderungsArt.Stundenplan, d => Planungsdienst.Entplanen(d, p));
        _plan.Ausgewaehlt = null;
        Neuaufbau(false);
    }

    public void RaumZuordnen()
    {
        if (_plan.Ausgewaehlt is not { } p) { Ui.Info("Bitte zuerst eine Stunde im Stundenplan anklicken."); return; }
        if (RaumZuordnenDialog.Zeigen(this, p)) Neuaufbau(false);
    }

    public void TauschvorschlaegeZeigen()
    {
        if (_plan.Ausgewaehlt is not { } p) { Ui.Info("Bitte zuerst eine Stunde im Stundenplan einer Klasse anklicken."); return; }
        var v = TauschvorschlaegeDialog.Zeigen(this, p);
        if (v is null) return;
        var a = D.Stundenplan.IndexOf(v.A);
        var b = D.Stundenplan.IndexOf(v.B);
        Aendern("Tauschvorschlag", AenderungsArt.Stundenplan, d =>
        {
            var pa = d.Stundenplan[a];
            var pb = d.Stundenplan[b];
            (pa.Tag, pb.Tag) = (pb.Tag, pa.Tag);
            (pa.Stunde, pb.Stunde) = (pb.Stunde, pa.Stunde);
            var ix = new DatenIndex(d);
            pa.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pa.UnterrichtId], pa.Tag, pa.Stunde, new[] { pa });
            pb.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pb.UnterrichtId], pb.Tag, pb.Stunde, new[] { pb });
        });
        Neuaufbau(false);
    }

    private void Kontextmenue(Point pt, Planstunde? p)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Stundenplan-Einstellungen…", Symbole.Get("einstellungen"), (_, _) => Einstellungen());
        var std = new ToolStripMenuItem("Stpl-Stunde: Standardformat") { Checked = _plan.Format.Standardformat };
        std.Click += (_, _) => { var f = Kopie(_plan.Format); f.Standardformat = !f.Standardformat; FormatSpeichern(f); };
        cm.Items.Add(std);
        cm.Items.Add(new ToolStripSeparator());
        if (p != null)
        {
            cm.Items.Add(p.Fixiert ? "Fixierung lösen" : "Unterr.-Std. fixieren", Symbole.Get("fixieren"), (_, _) => Fixieren());
            cm.Items.Add("Raum zuordnen / löschen…", Symbole.Get("raumzuordnen"), (_, _) => RaumZuordnen());
            cm.Items.Add("Tauschvorschläge…", Symbole.Get("tausch"), (_, _) => TauschvorschlaegeZeigen());
            cm.Items.Add("Stunde vom Unterricht entplanen", Symbole.Get("loeschen"), (_, _) => Entplanen());
            cm.Items.Add("Zeitwünsche des Unterrichts", Symbole.Get("zeitwunsch"), (_, _) => ZeitwuenscheFenster.Zeigen(ElementTyp.Unterricht, p.UnterrichtId));
            cm.Items.Add(new ToolStripSeparator());
        }
        cm.Items.Add("Im HTML-Format ausgeben", Symbole.Get("html"), (_, _) => HtmlAusgabe());
        cm.Show(_plan, pt);
    }

    private static StundenplanFormat Kopie(StundenplanFormat f) => System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.Serialize(f))!;

    private void Einstellungen()
    {
        var f = StundenplanEinstellungenDialog.Zeigen(this, Kopie(_plan.Format));
        if (f != null) FormatSpeichern(f);
    }

    private void FormatSpeichern(StundenplanFormat f)
    {
        Aendern("Stundenplan-Format", AenderungsArt.Einstellungen, d =>
        {
            d.Formate.RemoveAll(x => x.Typ == f.Typ && x.Gross == f.Gross);
            d.Formate.Add(f);
        });
        _plan.Format = f;
        Neuaufbau(false);
        foreach (var w in AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().Where(w => w != this && w._typ == f.Typ && w._gross == f.Gross))
        {
            w._plan.Format = f;
            w._plan.Invalidate();
        }
    }

    private void HtmlAusgabe()
    {
        var pfad = Path.Combine(Path.GetTempPath(), $"UntPlan_{Ix.Element(_typ, _id)?.Kurzname ?? "plan"}.html");
        File.WriteAllText(pfad, Export.Seite("Stundenplan", Export.StundenplanHtml(D, _typ, _id)));
        HauptFenster.Starten(pfad);
    }

    public void Drucken(bool vorschau) => StundenplanDruck.Drucken(this, _typ, new List<int> { _id }, _plan.Format, vorschau, _plan.Woche);
}
