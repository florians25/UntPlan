using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

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
    private Action? _planGroesse;

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
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _datum.ValueChanged += (_, _) => { if (!_laden) { AppZustand.DatumSetzen(DateOnly.FromDateTime(_datum.Value)); WocheSetzen(); } };
        ts.Items.Add(Knopf("Änderungen rückgängig (gewählte Stunde)", "rueckgaengig", AenderungenRueckgaengig));

        _plan.Format = FormatHolen();
        _plan.AuswahlGeaendert += AuswahlGeaendert;
        _plan.Abgelegt += Abgelegt;
        _plan.Doppelklick += p => { var u = Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId); if (u != null) UnterrichtFenster.Zeigen(_typ is ElementTyp.Raum ? ElementTyp.Unterricht : _typ, _typ is ElementTyp.Raum ? null : _id); };
        _plan.Kontext += Kontextmenue;
        _plan.TagesKontext += TagesKontextmenue;
        _plan.TagesAuswahlGeaendert += (datum, e) => { TagesLupe(e); if (e?.Vertretung is { } v) AppZustand.VertretungWaehlen(v); };

        Ui.RasterGestalten(_lupe, false);
        _lupe.RowHeadersVisible = false;
        foreach (var (n, t, w) in new[] { ("U", "U-Nr", 48), ("L", "Lehr.", 50), ("F", "Fa.", 46), ("R", "Rm.", 50), ("K", "Kla.", 80), ("W", "Wst", 36), ("G", "Schülergr.", 70), ("T", "Text", 120), ("X", "Fix", 30) })
            _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });

        _pool.DragEnter += (_, e) => e.Effect = (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("plan:") == true ? DragDropEffects.Move : DragDropEffects.None;
        _pool.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is string s && s.StartsWith("plan:"))
            {
                var (_, alle) = PlanAnsicht.PlanDaten(s);
                var idxs = alle.Select(x => D.Stundenplan.IndexOf(x)).Where(i => i >= 0).OrderByDescending(i => i).ToList();
                Aendern(idxs.Count > 1 ? "Stunden entplanen" : "Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var i in idxs) d.Stundenplan.RemoveAt(i); });
                _plan.AuswahlLeeren();
                Neuaufbau();
            }
        };
        // Stunde in die Lupe ziehen = Entfall (tagesaktueller Plan) bzw. Entplanen (Regelplan)
        _lupe.AllowDrop = true;
        _lupe.DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.Text) is string x && (x.StartsWith("tag:") || x.StartsWith("plan:")) ? DragDropEffects.Move : DragDropEffects.None;
        _lupe.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is not string x) return;
            if (x.StartsWith("plan:")) { var (_, alle) = PlanAnsicht.PlanDaten(x); var idxs = alle.Select(a => D.Stundenplan.IndexOf(a)).Where(i => i >= 0).OrderByDescending(i => i).ToList(); Aendern("Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var i in idxs) d.Stundenplan.RemoveAt(i); }); Neuaufbau(); return; }
            var t = x.Split(':');
            var datum = DateOnly.ParseExact(t[1], "yyyyMMdd");
            var stunde = int.Parse(t[2]);
            var uid = int.Parse(t[3]);
            var zeile = int.Parse(t[4]);
            var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
            Aendern(laenge > 1 ? $"Entfall ({laenge} Stunden)" : "Entfall", AenderungsArt.Vertretung, d =>
            {
                for (var i = 0; i < laenge; i++) Vertretungen.Entfallen(d, uid, zeile < 0 ? null : zeile, datum, stunde + i, AppZustand.Benutzer);
            });
            Neuaufbau(false);
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
            _planGroesse?.Invoke();
            Neuaufbau(false);
        };

        var mitte = new Panel { Dock = DockStyle.Fill };
        // Plan scrollt senkrecht, wenn die Zellen für alle Werte untereinander höher sein müssen als das Fenster
        var planRahmen = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _plan.Dock = DockStyle.None;
        planRahmen.Controls.Add(_plan);
        void PlanGroesse()
        {
            var breite = planRahmen.ClientSize.Width;
            var hoehe = Math.Max(planRahmen.ClientSize.Height, _plan.BenoetigteHoehe);
            if (hoehe > planRahmen.ClientSize.Height) breite = planRahmen.Width - SystemInformation.VerticalScrollBarWidth - 2;
            _plan.SetBounds(0, planRahmen.AutoScrollPosition.Y, Math.Max(100, breite), hoehe);
        }
        planRahmen.Resize += (_, _) => PlanGroesse();
        _planGroesse = PlanGroesse;
        mitte.Controls.Add(planRahmen);
        mitte.Controls.Add(_pool);
        Controls.Add(mitte);
        Controls.Add(_reiter);
        Controls.Add(_lupe);
        Controls.Add(_detail);
        Controls.Add(ts);
        ModusAnwenden();
        Neuaufbau();
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.ModusGewechselt += ModusAnwenden;
        FormClosed += (_, _) =>
        {
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.ModusGewechselt -= ModusAnwenden;
        };
    }

    /// <summary>Im Vertretungsmodus zeigt der Stundenplan immer den tagesaktuellen Plan der gewählten Woche.</summary>
    private void ModusAnwenden()
    {
        _laden = true;
        _wochenansicht.Checked = AppZustand.Vertretungsmodus || _wochenansicht.Checked && !AppZustand.Vertretungsmodus;
        _wochenansicht.Enabled = !AppZustand.Vertretungsmodus;
        if (!AppZustand.Vertretungsmodus) _wochenansicht.Checked = false;
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _laden = false;
        WocheSetzen();
    }

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed) return;
        _laden = true;
        _datum.Value = datum.ToDateTime(TimeOnly.MinValue);
        _laden = false;
        if (_plan.Woche.HasValue && Kalender.Montag(datum) != _plan.Woche) WocheSetzen();
        else _plan.Invalidate();
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

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (_plan.Tagesmodus) _plan.TageNeu();
        Neuaufbau(false);
    }
    protected override bool EigeneAenderungenAnzeigen => false;

    private void Blaettern(int r)
    {
        if (_auswahl.Items.Count == 0) return;
        _auswahl.SelectedIndex = Math.Clamp(_auswahl.SelectedIndex + r, 0, _auswahl.Items.Count - 1);
    }

    private void WocheSetzen()
    {
        _plan.Woche = _wochenansicht.Checked ? Kalender.Montag(DateOnly.FromDateTime(_datum.Value)) : null;
        _plan.AuswahlLeeren();
        DetailAktualisieren();
        PoolAufbauen();
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
        Tag = $"sp:{_typ}:{_id}:{(_gross ? 1 : 0)}";
        _plan.Typ = _typ;
        _plan.ElementId = _id;
        if (_plan.Ausgewaehlt != null && !D.Stundenplan.Contains(_plan.Ausgewaehlt)) _plan.Ausgewaehlt = null;
        _plan.Mehrfach.RemoveAll(x => !D.Stundenplan.Contains(x));
        if (_plan.Tagesmodus) _plan.TageNeu();
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
        if (_plan.Woche is DateOnly w)
        {
            var p = D.PeriodeAm(w);
            _detail.Text = $"Woche {Format.Datum(w)} – {Format.Datum(w.AddDays(D.Zeitraster.TageProWoche - 1))} ({Kalender.WochenTyp(D, w)}-Woche, {p.Name})   Tagesaktueller Stundenplan: Änderungen rot, Entfälle durchgestrichen.";
            return;
        }
        _detail.Text = $"{wst} Wochenstunden   {nv} nicht verplante Std.   Datumsbereich: {Format.Datum(periode.Von)} – {Format.Datum(periode.Bis ?? D.Einstellungen.SchuljahrBis)} ({periode.Name})";
    }

    private void PoolAufbauen()
    {
        _pool.SuspendLayout();
        _pool.Controls.Clear();
        if (_plan.Tagesmodus)
        {
            _pool.Controls.Add(new Label { Text = "Vormerkungen", Font = Ui.SchriftFett, AutoSize = true, Margin = new Padding(2, 2, 2, 6) });
            foreach (var vm in D.Vormerkungen.Where(v => v.Offen > 0 && (_typ switch
                     {
                         ElementTyp.Klasse => v.KlassenIds.Contains(_id),
                         ElementTyp.Lehrer => v.LehrerIds.Contains(_id),
                         _ => false,
                     })))
            {
                var fach = vm.FachId is int fid ? Ix.Faecher.GetValueOrDefault(fid) : null;
                var kachel = new Stapel(null, vm.Offen, 1, Ui.Farbe(fach?.Hintergrund ?? 0, Color.FromArgb(255, 236, 200)), fach?.Kurzname ?? "?", _typ == ElementTyp.Lehrer ? Ix.KKlassen(vm.KlassenIds) : string.Join(",", vm.LehrerIds.Select(Ix.KLehrer)), $"Vormerkung {vm.Id}: {vm.Text}");
                var id = vm.Id;
                kachel.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) kachel.DoDragDrop("vormerkung:" + id, DragDropEffects.Move); };
                _pool.Controls.Add(kachel);
            }
            _pool.ResumeLayout();
            return;
        }
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
                    // Strg+Klick: mehrere Unterrichte markieren und gemeinsam verplanen
                    if ((ModifierKeys & Keys.Control) != 0) { kachel.Markiert = !kachel.Markiert; kachel.Invalidate(); return; }
                    LupeFuellen(null, u);
                    var markiert = _pool.Controls.OfType<Stapel>().Where(x => x.Markiert && x.Unterricht != null).Select(x => x.Unterricht!.Id).ToList();
                    if (!markiert.Contains(u.Id)) markiert = new() { u.Id };
                    kachel.DoDragDrop("unterricht:" + string.Join(",", markiert), DragDropEffects.Move);
                };
                _pool.Controls.Add(kachel);
            }
        }
        _pool.ResumeLayout();
    }

    /// <summary>Stapel nicht verplanter Stunden eines Unterrichts (bei mehr als drei Stunden mit Anzahl).</summary>
    private sealed class Stapel : Control
    {
        public Unterricht? Unterricht { get; }
        public bool Markiert { get; set; }
        private readonly int _anzahl;
        private readonly Color _farbe;
        private readonly string _fach;
        private readonly string _info;
        private readonly int _block;

        public Stapel(Unterricht? u, int anzahl, int block, Color farbe, string fach, string info, string? tip = null)
        {
            Unterricht = u;
            _anzahl = anzahl;
            _farbe = farbe;
            _fach = fach;
            _info = info;
            _block = block;
            Size = new Size(64, 42 + Math.Min(anzahl - 1, 3) * 3);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            new ToolTip().SetToolTip(this, tip ?? $"U-Nr. {u?.Id}: {anzahl} nicht verplante Stunde(n){(block > 1 ? $", Blocklänge {block}" : "")}. Strg+Klick markiert mehrere Unterrichte.");
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
            if (Markiert) using (var p = new Pen(Color.FromArgb(255, 140, 0), 2.5f)) g.DrawRectangle(p, top.X + 1, top.Y + 1, top.Width - 2, top.Height - 2);
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
        if (daten.StartsWith("tag:") || daten.StartsWith("vormerkung:")) { TagesAbgelegt(daten, tag, stunde); return; }
        if (_plan.Tagesmodus) return;
        if (daten.StartsWith("plan:"))
        {
            var (prim, alle) = PlanAnsicht.PlanDaten(daten);
            if (prim is null) return;
            var dt = tag - prim.Tag;
            var ds = stunde - prim.Stunde;
            if (dt == 0 && ds == 0) return;
            if (alle.Count > 1) { MehrereVerschieben(alle, dt, ds); return; }
            var p = prim;
            var idx = D.Stundenplan.IndexOf(p);
            var u = Ix.Unterrichte[p.UnterrichtId];
            var status = Planungsdienst.Status(D, Ix, u, tag, stunde, 1, new[] { p });
            var tauschen = false;
            var entplanen = false;
            if (p.Fixiert && !Ui.Frage("Die Stunde ist fixiert. Trotzdem verschieben?")) return;
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
            Aendern(tauschen ? "Stunden tauschen" : "Stunde verschieben", AenderungsArt.Stundenplan, d => Planungsdienst.Verschieben(d, d.Stundenplan[idx], tag, stunde, tauschen, entplanen));
            _plan.Ausgewaehlt = D.Stundenplan.ElementAtOrDefault(idx);
            Neuaufbau(false);
        }
        else if (daten.StartsWith("unterricht:"))
        {
            var uids = daten[11..].Split(',').Select(x => int.TryParse(x, out var i) ? i : -1).Where(i => Ix.Unterrichte.ContainsKey(i)).ToList();
            if (uids.Count == 0) return;
            var unts = uids.Select(i => Ix.Unterrichte[i]).Where(u => Ix.NichtVerplant(u) > 0).ToList();
            if (unts.Count == 0) { Ui.Info("Alle Stunden dieses Unterrichts sind bereits verplant."); return; }
            // Einzelner Unterricht: ganzen Block (Doppelstunde, 3er-, 4er-Block) verplanen
            var laenge = unts.Count == 1 ? Math.Min(Ix.NichtVerplant(unts[0]), Math.Max(1, NaechsterBlock(unts[0]))) : 1;
            if (stunde + laenge - 1 > D.Zeitraster.StundenProTag) laenge = 1;
            var konflikte = new List<string>();
            foreach (var u in unts)
                for (var k = 0; k < laenge; k++)
                    konflikte.AddRange(Planungsdienst.Hindernisse(D, Ix, u, tag, stunde + k).Where(h => !uids.Contains(h.Unterricht.Id)).Select(x => $"U-Nr. {x.Unterricht.Id}: {Ix.UnterrichtText(x.Unterricht)} ({x.Grund})"));
            var entplanen = false;
            if (konflikte.Count > 0)
            {
                var wahl = PlanungsKonfliktDialog.Fragen(this, unts.Count > 1 ? $"{unts.Count} Unterrichte gleichzeitig verplanen" : $"Block speichern – Unterricht {unts[0].Id} {Ix.UnterrichtText(unts[0])}",
                    string.Join("\n", konflikte.Distinct()), "Block speichern (behindernder Unterricht wird entplant)", "Kollision erzeugen");
                if (wahl is null) return;
                entplanen = wahl == 0;
            }
            Planstunde? neu = null;
            Aendern(laenge > 1 ? $"{laenge}er-Block verplanen" : unts.Count > 1 ? "Stunden gleichzeitig verplanen" : "Stunde verplanen", AenderungsArt.Stundenplan, d =>
            {
                foreach (var u in unts.Select(x => d.Unterrichte.First(y => y.Id == x.Id)))
                    for (var k = 0; k < laenge; k++)
                        neu ??= Planungsdienst.Verplanen(d, u, tag, stunde + k, entplanen);
            });
            _plan.Ausgewaehlt = neu;
            Neuaufbau(false);
        }
    }

    /// <summary>Länge des nächsten noch nicht verplanten Blocks (Blöcke und Doppelstunden laut Unterricht).</summary>
    private int NaechsterBlock(Unterricht u)
    {
        var verplant = Ix.PlanJeUnterricht.TryGetValue(u.Id, out var l) ? l : new List<Planstunde>();
        var vorhanden = verplant.GroupBy(p => p.Tag).SelectMany(g =>
        {
            var st = g.Select(p => p.Stunde).OrderBy(x => x).ToList();
            var res = new List<int>();
            var lauf = 1;
            for (var i = 1; i <= st.Count; i++)
            {
                if (i < st.Count && st[i] == st[i - 1] + 1) lauf++;
                else { res.Add(lauf); lauf = 1; }
            }
            return res;
        }).ToList();
        foreach (var b in u.Bloecke())
            if (!vorhanden.Remove(b)) return b;
        return 1;
    }

    /// <summary>Mehrere markierte Stunden (oder einen Block) gemeinsam um denselben Abstand verschieben.</summary>
    private void MehrereVerschieben(List<Planstunde> alle, int dt, int ds)
    {
        var konflikte = new List<string>();
        foreach (var p in alle)
        {
            var u = Ix.Unterrichte[p.UnterrichtId];
            int t = p.Tag + dt, st = p.Stunde + ds;
            if (t < 0 || t >= D.Zeitraster.TageProWoche || st < 1 || st > D.Zeitraster.StundenProTag) { Ui.Info("Die Stunden passen an dieser Position nicht in das Zeitraster."); return; }
            konflikte.AddRange(Planungsdienst.Hindernisse(D, Ix, u, t, st, alle).Select(h => $"{Planungsdienst.Tagname(D, t)} {st}.: U-Nr. {h.Unterricht.Id} {Ix.UnterrichtText(h.Unterricht)} ({h.Grund})"));
        }
        if (alle.Any(p => p.Fixiert) && !Ui.Frage("Unter den Stunden sind fixierte Stunden. Trotzdem verschieben?")) return;
        var entplanen = false;
        if (konflikte.Count > 0)
        {
            var wahl = PlanungsKonfliktDialog.Fragen(this, $"{alle.Count} Stunden verschieben", string.Join("\n", konflikte.Distinct()), "Verschieben (behindernder Unterricht wird entplant)", "Kollision erzeugen");
            if (wahl is null) return;
            entplanen = wahl == 0;
        }
        var idxs = alle.Select(p => D.Stundenplan.IndexOf(p)).ToList();
        Aendern($"{alle.Count} Stunden verschieben", AenderungsArt.Stundenplan, d =>
        {
            var stunden = idxs.Select(i => d.Stundenplan[i]).ToList();
            var ix = new DatenIndex(d);
            if (entplanen)
                foreach (var p in stunden)
                    foreach (var h in Planungsdienst.Hindernisse(d, ix, ix.Unterrichte[p.UnterrichtId], p.Tag + dt, p.Stunde + ds, stunden)) d.Stundenplan.Remove(h.Planstunde);
            foreach (var p in stunden) { p.Tag += dt; p.Stunde += ds; }
            foreach (var p in stunden) p.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[p.UnterrichtId], p.Tag, p.Stunde, stunden.Where(x => x != p).Concat(new[] { p }).ToList());
        });
        Neuaufbau(false);
    }

    /// <summary>Tagesaktueller Plan: Verlegung, Tausch oder Vormerkung verplanen.</summary>
    private void TagesAbgelegt(string daten, int tag, int stunde)
    {
        if (_plan.DatumVon(tag) is not DateOnly ziel) return;
        if (daten.StartsWith("vormerkung:") && int.TryParse(daten[11..], out var vid))
        {
            Aendern("Vormerkung verplanen", AenderungsArt.Vertretung, d => Vertretungen.VormerkungVerplanen(d, d.Vormerkungen.First(v => v.Id == vid), ziel, stunde, AppZustand.Benutzer));
            Neuaufbau(false);
            return;
        }
        var t = daten.Split(':');
        var von = DateOnly.ParseExact(t[1], "yyyyMMdd");
        var vonStunde = int.Parse(t[2]);
        var uid = int.Parse(t[3]);
        int? zeile = int.Parse(t[4]) is var z && z >= 0 ? z : null;
        var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
        if (von == ziel && vonStunde == stunde) return;
        if (stunde + laenge - 1 > D.Zeitraster.StundenProTag) { Ui.Info("Der Block passt an dieser Position nicht mehr in den Tag."); return; }
        var tpVon = Tagesplan.Berechnen(D, von);
        var tp = Tagesplan.Berechnen(D, ziel);
        // Quelle und Ziel je Blockstunde
        var schritte = new List<(int Von, int Nach, List<TagesEintrag> Quelle, Feldstatus Status, List<TagesEintrag> Hindernisse)>();
        for (var i = 0; i < laenge; i++)
        {
            var q = tpVon.Stunde(vonStunde + i).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == uid && (zeile is null || (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeile)).ToList();
            if (q.Count == 0) continue;
            var lehrer = q.SelectMany(e => e.Regel?.LehrerId is int l ? new[] { l } : e.LehrerIds.ToArray()).Distinct().ToList();
            var klassen = q.SelectMany(e => e.KlassenIds).Distinct().ToList();
            var st = Vertretungen.Feldstatus(D, tp, lehrer, klassen, stunde + i, uid, null);
            var hindernisse = tp.Stunde(stunde + i).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) != uid && (e.LehrerIds.Intersect(lehrer).Any() || e.KlassenIds.Intersect(klassen).Any())).ToList();
            schritte.Add((vonStunde + i, stunde + i, q, st, hindernisse));
        }
        if (schritte.Count == 0) return;
        if (schritte.Any(x => x.Quelle.Any(e => e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true)) && !Ui.Frage("Die Stunde ist fixiert. Trotzdem verlegen?")) return;
        var konflikte = schritte.Where(x => x.Hindernisse.Count > 0).ToList();
        var tauschen = false;
        var verdraengen = false;
        if (konflikte.Count > 0)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine, konflikte.Select(k =>
                $"Am {ziel:d.M.} in der {k.Nach}. Stunde: Kollision in Zielstunde{Environment.NewLine}" +
                string.Join(Environment.NewLine, k.Hindernisse.Select(h => $"Unt. {h.Unterricht?.Id.ToString() ?? "V" + h.Vertretung?.Id}: {string.Join(",", h.LehrerIds.Select(Ix.KLehrer))}, {Ix.KKlassen(h.KlassenIds)} {Ix.KFach(h.FachId)}"))));
            var tauschMoeglich = laenge == 1 && konflikte.Count == 1 && konflikte[0].Status == Feldstatus.TauschMoeglich && konflikte[0].Hindernisse.All(h => h.Unterricht != null) && konflikte[0].Hindernisse.Select(h => h.Unterricht!.Id).Distinct().Count() == 1;
            var optionen = new List<string> { "Unterricht nicht verplanen.", "Behindernden Unterricht verdrängen (vormerken)", "Kollision erzeugen" };
            if (tauschMoeglich) optionen.Add("Stunden tauschen");
            var w = OptionenDialog.Fragen(this, "Frage", text, optionen, tauschMoeglich ? 3 : 0);
            if (w is null or 0) return;
            verdraengen = w == 1;
            tauschen = w == 3;
        }
        if (schritte.Any(x => x.Status == Feldstatus.VorgabeVerletzt) && !Ui.Frage("Ein beteiligter Lehrer ist zu diesem Zeitpunkt abwesend. Trotzdem verlegen? (Es entsteht eine offene Vertretung.)")) return;
        Aendern(tauschen ? "Stundentausch" : laenge > 1 ? $"Block verlegen ({laenge} Stunden)" : verdraengen ? "Verlegung mit Verdrängung" : "Stundenverlegung", AenderungsArt.Vertretung, d =>
        {
            if (tauschen)
            {
                Vertretungen.Tauschen(d, uid, von, vonStunde, konflikte[0].Hindernisse[0].Unterricht!.Id, ziel, stunde, AppZustand.Benutzer);
                return;
            }
            if (verdraengen)
                foreach (var k in konflikte)
                    foreach (var h in k.Hindernisse.Where(h => h.Unterricht != null).Select(h => h.Unterricht!.Id).Distinct())
                    {
                        Vertretungen.Entfallen(d, h, null, ziel, k.Nach, AppZustand.Benutzer);
                        var v = d.Vertretungen.LastOrDefault(x => x.Datum == ziel && x.Stunde == k.Nach && x.UnterrichtId == h);
                        if (v != null) Vertretungen.AlsVormerkung(d, v, AppZustand.Benutzer).Quelle = VormerkungsQuelle.Verdraengung;
                    }
            // bei Verschiebung innerhalb desselben Tages in Richtung der eigenen Stunden: Reihenfolge so wählen, dass nichts überschrieben wird
            var reihe = von == ziel && stunde > vonStunde ? schritte.AsEnumerable().Reverse() : schritte;
            foreach (var sch in reihe) Vertretungen.Verlegen(d, uid, zeile, von, sch.Von, ziel, sch.Nach, VertretungsArt.Verlegung, AppZustand.Benutzer);
        });
        Neuaufbau(false);
    }

    private void TagesLupe(TagesEintrag? e)
    {
        _lupe.Rows.Clear();
        if (e is null) return;
        var v = e.Vertretung;
        _lupe.Rows.Add(e.Unterricht?.Id.ToString() ?? (v != null ? "V" + v.Id : ""), string.Join(",", e.LehrerIds.Select(Ix.KLehrer)) + (v?.LehrerId is int l && !e.LehrerIds.Contains(l) ? $" ({Ix.KLehrer(l)})" : ""),
            Ix.KFach(e.FachId), string.Join(",", e.RaumIds.Select(Ix.KRaum)), Ix.KKlassen(e.KlassenIds), "", e.Unterricht?.Schuelergruppe ?? "",
            v is null ? e.Unterricht?.Text ?? "" : $"{Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art)} {v.Text}".Trim(), v?.Fixiert == true ? "*" : "");
    }

    /// <summary>Alle Änderungen der gewählten Stunde zurücknehmen (manuelle Vertretungszeilen löschen).</summary>
    private void AenderungenRueckgaengig()
    {
        if (_plan.TagesAuswahl is not { } ta || ta.Eintrag.Vertretung is not { } v) { Ui.Info("Bitte im tagesaktuellen Stundenplan eine geänderte Stunde anklicken."); return; }
        Aendern("Änderungen rückgängig", AenderungsArt.Vertretung, d =>
        {
            var weg = d.Vertretungen.Where(x => x.Id == v.Id || (v.VerlegungsId.HasValue && x.VerlegungsId == v.VerlegungsId)).ToList();
            foreach (var x in weg) d.Vertretungen.Remove(x);
            foreach (var t in weg.Select(x => x.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        });
        Neuaufbau(false);
    }

    private void TagesKontextmenue(Point pt, DateOnly datum, int stunde, TagesEintrag? e)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var uid = e?.Unterricht?.Id ?? e?.Vertretung?.UnterrichtId;
        var zeile = e?.Regel?.ZeilenIndex ?? e?.Vertretung?.ZeilenIndex ?? 0;
        cm.Items.Add("Sondereinsatz / Stunde ändern…", Symbole.Get("neu"), (_, _) =>
        {
            if (e?.Findetstatt == true && uid is int u) Dialoge.SondereinsatzDialog.StundeAendern(this, u, zeile, datum, stunde);
            else Dialoge.SondereinsatzDialog.Neu(this, datum, stunde, _typ == ElementTyp.Klasse ? new[] { _id } : Array.Empty<int>(), _typ == ElementTyp.Lehrer ? new[] { _id } : Array.Empty<int>());
        });
        cm.Items.Add("Klausur erstellen…", Symbole.Get("kommentar"), (_, _) => Dialoge.KlausurDialog.Neu(this, datum, stunde, e));
        if (e?.Findetstatt == true && uid is int uu)
        {
            cm.Items.Add("Raum zuordnen / löschen…", Symbole.Get("raumzuordnen"), (_, _) => Dialoge.SondereinsatzDialog.RaumZuordnen(this, uu, zeile, datum, stunde));
            cm.Items.Add("Entfall", Symbole.Get("entfall"), (_, _) => { Aendern("Entfall", AenderungsArt.Vertretung, d => Vertretungen.Entfallen(d, uu, _typ == ElementTyp.Lehrer ? zeile : null, datum, stunde, AppZustand.Benutzer)); Neuaufbau(false); });
            cm.Items.Add("Text zur Vertretung…", Symbole.Get("kommentar"), (_, _) =>
            {
                var text = Ui.Eingabe("Text zur Vertretung (wird im Stundenplan und im Vertretungsplan ausgegeben):", "Text zur Vertretung", e.Vertretung?.Text ?? "");
                if (text is null) return;
                Aendern("Text zur Vertretung", AenderungsArt.Vertretung, d =>
                {
                    var v = e.Vertretung is { } x ? d.Vertretungen.First(y => y.Id == x.Id) : null;
                    if (v is null)
                    {
                        v = new Vertretung
                        {
                            Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = uu, ZeilenIndex = zeile, LehrerId = e.Regel?.LehrerId, VertreterId = e.Regel?.LehrerId,
                            FachId = e.FachId, KlassenIds = e.KlassenIds.ToList(), RaumId = e.Regel?.RaumId, Art = VertretungsArt.UnterrichtGeaendert, Quelle = VertretungsQuelle.Manuell, Bearbeitet = true,
                        };
                        d.Vertretungen.Add(v);
                    }
                    v.Text = text;
                });
                Neuaufbau(false);
            });
            cm.Items.Add("Als Vormerkung beiseitelegen", Symbole.Get("zurueck"), (_, _) =>
            {
                Aendern("Vormerkung", AenderungsArt.Vertretung, d =>
                {
                    Vertretungen.Entfallen(d, uu, null, datum, stunde, AppZustand.Benutzer);
                    var v = d.Vertretungen.Last(x => x.Datum == datum && x.Stunde == stunde && x.UnterrichtId == uu);
                    Vertretungen.AlsVormerkung(d, v, AppZustand.Benutzer).Quelle = VormerkungsQuelle.Stundenplan;
                });
                Neuaufbau(false);
            });
        }
        if (e?.Vertretung != null)
        {
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Vertretungsvorschlag", Symbole.Get("vertretung"), (_, _) => { AppZustand.VertretungWaehlen(e.Vertretung); HauptFenster.Oeffnen("vorschlag", () => new VertretungsvorschlagFenster()); });
            cm.Items.Add("Änderungen rückgängig", Symbole.Get("rueckgaengig"), (_, _) => AenderungenRueckgaengig());
        }
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Stundenplan-Einstellungen…", Symbole.Get("einstellungen"), (_, _) => Einstellungen());
        cm.Show(_plan, pt);
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

    private List<Planstunde> Markierte() => _plan.Mehrfach.Count > 0 ? _plan.Mehrfach.ToList() : _plan.Ausgewaehlt is { } p ? new() { p } : new();

    private void Fixieren()
    {
        var m = Markierte();
        if (m.Count == 0) return;
        var idxs = m.Select(p => D.Stundenplan.IndexOf(p)).Where(i => i >= 0).ToList();
        var ziel = !m[0].Fixiert;
        Aendern(ziel ? "Stunden fixieren" : "Fixierung lösen", AenderungsArt.Stundenplan, d => { foreach (var i in idxs) d.Stundenplan[i].Fixiert = ziel; });
        Neuaufbau(false);
    }

    private void Entplanen()
    {
        var m = Markierte();
        if (m.Count == 0) return;
        if (m.Any(p => p.Fixiert) && !Ui.Frage("Fixierte Stunden entplanen?")) return;
        Aendern(m.Count > 1 ? "Stunden entplanen" : "Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var p in m) Planungsdienst.Entplanen(d, p); });
        _plan.AuswahlLeeren();
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
        if (_plan.Tagesmodus) return;
        if (_plan.Mehrfach.Count > 1)
        {
            cm.Items.Add($"{_plan.Mehrfach.Count} markierte Stunden entplanen", Symbole.Get("loeschen"), (_, _) => Entplanen());
            cm.Items.Add("Markierte Stunden fixieren / lösen", Symbole.Get("fixieren"), (_, _) => Fixieren());
            cm.Items.Add("Markierung aufheben", null, (_, _) => { _plan.Mehrfach.Clear(); _plan.Invalidate(); });
            cm.Items.Add(new ToolStripSeparator());
        }
        if (p != null)
        {
            cm.Items.Add("Unterricht bearbeiten…", Symbole.Get("unterricht"), (_, _) => UnterrichtFenster.Zeigen(_typ is ElementTyp.Raum or ElementTyp.Fach ? ElementTyp.Unterricht : _typ, _typ is ElementTyp.Raum or ElementTyp.Fach ? null : _id));
            cm.Items.Add("Ganzen Block markieren", null, (_, _) => { _plan.Mehrfach.Clear(); _plan.Mehrfach.AddRange(PlanAnsicht.Block(p)); _plan.Invalidate(); });
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
        _planGroesse?.Invoke();
        Neuaufbau(false);
        foreach (var w in AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().Where(w => w != this && w._typ == f.Typ && w._gross == f.Gross))
        {
            w._plan.Format = f;
            w._planGroesse?.Invoke();
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
