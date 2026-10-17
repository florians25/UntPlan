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
    private readonly ToolStripButton _kleinKnopf;
    private readonly ToolStripButton _lupeKnopf;
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Short, Width = 105 };
    private readonly ToolStrip _leiste;
    /// <summary>Zweite Zeile wie im Vorbild: Zeitbereich (▼), Datum mit Hoch/Runter und Ende der Woche („- 16.10.2026“).</summary>
    private readonly Panel _zeitZeile = new() { Dock = DockStyle.Top, Height = 27 };
    private readonly ToolStrip _zeitLeiste;
    private readonly ToolStripLabel _bisText = new() { Margin = new Padding(2, 1, 0, 2) };
    /// <summary>Fußzeile mit der Formatauswahl unten rechts (wie im Vorbild „Rau1A - Raumplan …“).</summary>
    private readonly Fussleiste _fuss = new();
    /// <summary>Hellblau des Vorbild-Stundenplanmodus für die Flächen um den Plan (Zeitzeile, Fuß, Ablage).</summary>
    internal static readonly Color PlanFlaeche = Color.FromArgb(220, 240, 255);
    private readonly ToolStripDropDownButton _typKnopf = new() { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Planart: Klasse, Lehrer oder Raum" };
    private readonly Panel _planRahmen = new() { Dock = DockStyle.Fill };
    private bool _laden;
    private bool _klein;
    private Action? _planGroesse;
    /// <summary>Werkzeugleisten-Einträge, die auch im kleinen Stundenplan sichtbar bleiben.</summary>
    private readonly List<ToolStripItem> _kleinSichtbar = new();

    public static StundenplanFenster Neu(ElementTyp typ, int? id, bool gross, bool klein = false)
    {
        var f = new StundenplanFenster(typ, id ?? Stammdaten.Sortiert(AppZustand.D, typ).FirstOrDefault()?.Id ?? 0, gross, klein);
        f.Show();
        return f;
    }

    public static StundenplanFenster? AktivesOderNeu() =>
        AppZustand.Haupt.ActiveMdiChild as StundenplanFenster ?? AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().FirstOrDefault() ?? Neu(ElementTyp.Klasse, null, false);

    /// <summary>Kleiner Stundenplan (schmale Zellen nur mit Fachkürzel, Pausenzeilen), z. B. mehrere nebeneinander in der Vertretungsplanung.</summary>
    public bool Klein => _klein;
    public ElementTyp Typ => _typ;
    public int ElementId => _id;
    /// <summary>Gezeichneter Plan (für Selbsttest und Synchronisation).</summary>
    internal PlanAnsicht Plan => _plan;
    /// <summary>Selbsttest: Der Plan ist höher als der sichtbare Bereich und kann gescrollt werden (Rollbalken statt Quetschen).</summary>
    internal bool PlanScrollbar => _planRahmen.AutoScroll && _plan.Height > _planRahmen.ClientSize.Height;
    /// <summary>Selbsttest: Plan mit dem Mausrad nach unten scrollen.</summary>
    internal void SelbsttestScrollen(int y) => _planRahmen.AutoScrollPosition = new Point(0, y);

    public StundenplanFenster(ElementTyp typ, int id, bool gross, bool klein = false)
    {
        _typ = typ;
        _id = id;
        _gross = gross;
        _klein = klein;
        HilfeThema = "Stundenpläne";
        Icon = SymbolIcon("stundenplan");
        Size = klein ? new Size(196, 330) : gross ? new Size(820, 640) : new Size(560, 520);
        Positionieren();

        var ts = Werkzeugleiste();
        _leiste = ts;
        ts.Items.Add(_auswahl);
        _kleinSichtbar.Add(_auswahl);
        _auswahl.SelectedIndexChanged += (_, _) =>
        {
            if (_laden || _auswahl.SelectedItem is not Eintrag e) return;
            _id = e.E.Id;
            Neuaufbau();
            // Fensterverknüpfung: andere Fenster (Stammdaten, Unterricht, Zeitwünsche, Pläne desselben Typs) folgen
            if (Verknuepft) AppZustand.Waehlen(_typ, _id, this);
        };
        // Hoch/Runter neben der Elementauswahl und Planart-Symbol mit Auswahl (Klasse, Lehrer, Raum) wie im Vorbild
        var dreh = new Steuerelemente.Drehknopf("Nächstes / vorheriges Element");
        dreh.Gedreht += r => Blaettern(r);
        ts.Items.Add(new ToolStripControlHost(dreh) { Margin = new Padding(1, 1, 1, 0), AutoSize = false, Size = dreh.Size });
        _kleinSichtbar.Add(ts.Items[^1]);
        foreach (var (t, name, symbol) in new[] { (ElementTyp.Klasse, "Klasse", "klassen"), (ElementTyp.Lehrer, "Lehrer", "lehrer"), (ElementTyp.Raum, "Raum", "raeume"), (ElementTyp.Fach, "Fach", "faecher") })
            _typKnopf.DropDownItems.Add(name, Symbole.Get(symbol, 16), (_, _) => TypSetzen(t));
        ts.Items.Add(_typKnopf);
        _kleinSichtbar.Add(_typKnopf);
        // blaue Durchschaltknöpfe im Fenstertitel
        Titelleiste.Durchschalten = Blaettern;
        // Fensterverknüpfung (Vorbild-Kettensymbol): Plan folgt dem gewählten Element bzw. der gewählten Stunde anderer Fenster
        Verknuepft = AppZustand.Einstellungen.StundenplanSynchronisieren;
        _sync = Verknuepfung.Knopf(Verknuepft, v => Verknuepft = v);
        ts.Items.Add(_sync);
        _kleinSichtbar.Add(_sync);
        _kleinKnopf = new ToolStripButton("klein") { CheckOnClick = true, Checked = klein, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Kleiner Stundenplan (nur Fachkürzel, Pausenzeilen) – z. B. mehrere nebeneinander in der Vertretungsplanung" };
        _kleinKnopf.CheckedChanged += (_, _) => { if (!_laden) KleinSetzen(_kleinKnopf.Checked); };
        ts.Items.Add(_kleinKnopf);
        _lupeKnopf = new ToolStripButton("Lupe") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Stundenplanlupe unter dem Plan ein-/ausblenden" };
        _lupeKnopf.CheckedChanged += (_, _) => { if (!_laden) LupeUmschalten(_lupeKnopf.Checked); };
        ts.Items.Add(_lupeKnopf);
        ts.Items.Add(new ToolStripSeparator());
        _format.SelectedIndexChanged += (_, _) => FormatAuswahlGeaendert();
        _alleElemente = new ToolStripButton(Symbole.Get("uebersicht")) { ToolTipText = "Alle Elemente im Unterricht", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        _alleElemente.CheckedChanged += (_, _) => { _reiter.Visible = _alleElemente.Checked; ReiterAufbauen(); };
        ts.Items.Add(_alleElemente);
        ts.Items.Add(Knopf("Unterr.-Std. fixieren / lösen", "fixieren", Fixieren));
        ts.Items.Add(Knopf("Raum zuordnen", "raumzuordnen", RaumZuordnen));
        ts.Items.Add(Knopf("Tauschvorschläge", "tausch", TauschvorschlaegeZeigen));
        ts.Items.Add(Knopf("Stunde entplanen", "loeschen", Entplanen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        ts.Items.Add(Knopf("HTML-Ausgabe", "html", HtmlAusgabe));
        ts.Items.Add(new ToolStripSeparator());
        _wochenansicht = new ToolStripButton("Woche", Symbole.Get("kalender")) { ToolTipText = "Wochenansicht mit Vertretungen", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        _wochenansicht.CheckedChanged += (_, _) => WocheSetzen();
        ts.Items.Add(_wochenansicht);
        ts.Items.Add(Knopf("Änderungen rückgängig (gewählte Stunde)", "rueckgaengig", AenderungenRueckgaengig));
        ts.Items.Add(MehrMenueAufbauen(ts));

        // zweite Zeile: Zeitbereich, Datum, Hoch/Runter, Ende der Woche
        _zeitLeiste = Werkzeugleiste();
        _zeitLeiste.Dock = DockStyle.Fill;
        _zeitLeiste.CanOverflow = false;
        var zeitbereich = new ToolStripDropDownButton { DisplayStyle = ToolStripItemDisplayStyle.None, ShowDropDownArrow = true, ToolTipText = "Zeitbereich: Tag, Woche oder Periode", AutoSize = false, Size = new Size(18, 20), Margin = new Padding(2, 1, 6, 2) };
        zeitbereich.DropDownItems.Add("…");
        zeitbereich.DropDownOpening += (_, _) =>
        {
            var m = ZeitbereichMenue();
            var eintraege = m.DropDownItems.Cast<ToolStripItem>().ToArray();
            zeitbereich.DropDownItems.Clear();
            zeitbereich.DropDownItems.AddRange(eintraege);
        };
        _zeitLeiste.Items.Add(zeitbereich);
        _zeitLeiste.Items.Add(new ToolStripControlHost(_datum) { Margin = new Padding(0, 1, 2, 1) });
        var datumDreh = new Steuerelemente.Drehknopf("Nächster / vorheriger Zeitraum");
        datumDreh.Gedreht += r => { if (_plan.Woche.HasValue && _nurTag) TagBlaettern(r); else WocheBlaettern(7 * r); };
        _zeitLeiste.Items.Add(new ToolStripControlHost(datumDreh) { AutoSize = false, Size = datumDreh.Size, Margin = new Padding(0, 1, 2, 0) });
        _zeitLeiste.Items.Add(_bisText);
        _zeitZeile.Controls.Add(_zeitLeiste);
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _datum.ValueChanged += (_, _) => { if (!_laden) { AppZustand.DatumSetzen(DateOnly.FromDateTime(_datum.Value)); WocheSetzen(); } };

        // Fußzeile: Formatauswahl unten rechts mit Abstand zum Fensterrand, Größengriff frei (wie im Vorbild „Kla1 - Klasse 1“)
        _fuss.Rechts = _format;
        _format.DrawMode = DrawMode.OwnerDrawFixed;
        _format.ItemHeight = 18;
        _format.DrawItem += FormatEintragZeichnen;
        _format.DropDown += (_, _) => _format.DropDownWidth = Math.Max(_format.Width, FormatListenbreite());

        _plan.Format = FormatHolen();
        FormateFuellen();
        _plan.AuswahlGeaendert += AuswahlGeaendert;
        _plan.LeereZelleGewaehlt += LeereZelleGewaehlt;
        _plan.Abgelegt += Abgelegt;
        _plan.Doppelklick += p => { var u = Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId); if (u != null) UnterrichtFenster.Zeigen(_typ is ElementTyp.Raum ? ElementTyp.Unterricht : _typ, _typ is ElementTyp.Raum ? null : _id); };
        _plan.Kontext += Kontextmenue;
        _plan.TagesKontext += TagesKontextmenue;
        _plan.TagesDoppelklick += (datum, e) => EntfallDirektAufheben(datum, e);
        _plan.TagesAuswahlGeaendert += (datum, e) =>
        {
            TagesLupe(e);
            if (e?.Vertretung is { } v) AppZustand.VertretungWaehlen(v);
            else if (e?.Unterricht is { } u)
            {
                // regulärer Unterricht im tagesaktuellen Plan: die anderen Pläne zeigen Klasse, Lehrkraft und Raum dieser Stunde
                var tag = Kalender.TagIndex(D, datum);
                var p = D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == u.Id && x.Tag == tag && x.Stunde == e.Stunde) ?? new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = e.Stunde };
                AppZustand.StundeWaehlen(u, p, this, e.KlassenIds, e.LehrerIds, e.RaumIds, datum);
            }
        };

        Ui.RasterGestalten(_lupe, false);
        _lupe.RowHeadersVisible = false;
        LupeSpalten(false);

        _pool.DragEnter += (_, e) => e.Effect = (e.Data?.GetData(DataFormats.Text) as string) is string x && (x.StartsWith("plan:") || (_plan.Tagesmodus && x.StartsWith("tag:"))) ? DragDropEffects.Move : DragDropEffects.None;
        _pool.DragDrop += (_, e) =>
        {
            // Vertretungsmodus: Stunde neben den Stundenplan legen = Vormerkung (später, auch in einer anderen Woche, wieder einplanen)
            if (_plan.Tagesmodus && e.Data?.GetData(DataFormats.Text) is string tx && tx.StartsWith("tag:"))
            {
                var t = tx.Split(':');
                var datum = DateOnly.ParseExact(t[1], "yyyyMMdd");
                var st = int.Parse(t[2]);
                var uid = int.Parse(t[3]);
                int? zeile = int.Parse(t[4]) is var zz && zz >= 0 ? zz : null;
                var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
                Aendern(laenge > 1 ? $"Vormerkung ({laenge} Stunden)" : "Vormerkung", AenderungsArt.Vertretung, d =>
                {
                    for (var i = 0; i < laenge; i++) Tagesverlegung.Vormerken(d, datum, st + i, uid, zeile, VormerkungsQuelle.Stundenplan, AppZustand.Benutzer);
                });
                Neuaufbau(false);
                return;
            }
            if (e.Data?.GetData(DataFormats.Text) is string s && s.StartsWith("plan:") && !RegelplanNurAnzeige)
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
            if (x.StartsWith("plan:") && RegelplanNurAnzeige) return;
            if (x.StartsWith("plan:")) { var (_, alle) = PlanAnsicht.PlanDaten(x); var idxs = alle.Select(a => D.Stundenplan.IndexOf(a)).Where(i => i >= 0).OrderByDescending(i => i).ToList(); Aendern("Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var i in idxs) d.Stundenplan.RemoveAt(i); }); Neuaufbau(); return; }
            var t = x.Split(':');
            var datum = DateOnly.ParseExact(t[1], "yyyyMMdd");
            var stunde = int.Parse(t[2]);
            var uid = int.Parse(t[3]);
            var zeile = int.Parse(t[4]);
            var laenge = t.Length > 5 && int.TryParse(t[5], out var l5) ? Math.Max(1, l5) : 1;
            // verlegte Stunde: erst fragen, ob die Verlegung rückgängig gemacht werden soll (dann kein Entfall)
            if (VerlegungsAbfrage.VorEntfall(datum, stunde, uid, Aendern)) { Neuaufbau(false); return; }
            var seLoeschen = SondereinsatzLoeschenFragen(Enumerable.Range(stunde, laenge).SelectMany(s => Vertretungen.SondereinsaetzeBeiEntfall(D, uid, zeile < 0 ? null : zeile, datum, s)));
            Aendern(laenge > 1 ? $"Entfall ({laenge} Stunden)" : "Entfall", AenderungsArt.Vertretung, d =>
            {
                for (var i = 0; i < laenge; i++) Vertretungen.Entfallen(d, uid, zeile < 0 ? null : zeile, datum, stunde + i, AppZustand.Benutzer, seLoeschen);
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
            _formatName = null;
            _plan.Format = FormatHolen();
            FormateFuellen();
            _planGroesse?.Invoke();
            Neuaufbau(false);
        };

        var mitte = new Panel { Dock = DockStyle.Fill };
        // Plan scrollt senkrecht, wenn die Zellen für alle Werte untereinander höher sein müssen als das Fenster
        var planRahmen = _planRahmen;
        _plan.Dock = DockStyle.None;
        planRahmen.Controls.Add(_plan);
        void PlanGroesse()
        {
            // nur blättern, wenn die Zellen tatsächlich mehr Platz brauchen (sonst füllt der Plan das Fenster ohne Rollbalken)
            // kleiner Plan: immer an das Fenster angepasst; sonst Mindestgrößen laut Format (Zellengröße) bzw. Schriftgröße
            // Vertretungsmodus: schmaler Rand um den Plan
            var rand = AppZustand.Vertretungsmodus ? (_klein ? 3 : 6) : 0;
            var innen = planRahmen.Height - 2 - 2 * rand;
            // kleiner Plan (Miniplan): Mindestzeilenhöhe statt Quetschen – bei vielen Stunden (bis 14) scrollt er senkrecht
            var noetig = _klein ? _plan.MindestHoehe : _plan.BenoetigteHoehe;
            var blaettern = noetig > innen;
            var quer = !_klein && _plan.BenoetigteBreite > planRahmen.Width - (blaettern ? SystemInformation.VerticalScrollBarWidth : 0) - 2 - 2 * rand;
            if (planRahmen.AutoScroll != (blaettern || quer))
            {
                planRahmen.AutoScroll = blaettern || quer;
                if (!blaettern && !quer) planRahmen.AutoScrollPosition = Point.Empty;
            }
            var breite = quer ? _plan.BenoetigteBreite : (blaettern ? planRahmen.Width - SystemInformation.VerticalScrollBarWidth - 2 : planRahmen.Width) - 2 * rand;
            var hoehe = blaettern ? noetig : planRahmen.Height - (quer ? SystemInformation.HorizontalScrollBarHeight + 2 : 0) - 2 * rand;
            _plan.SetBounds(rand + (quer ? planRahmen.AutoScrollPosition.X : 0), rand + (blaettern ? planRahmen.AutoScrollPosition.Y : 0), Math.Max(_klein ? 60 : 100, breite), Math.Max(60, hoehe));
        }
        planRahmen.Resize += (_, _) => PlanGroesse();
        _planGroesse = PlanGroesse;
        mitte.Controls.Add(planRahmen);
        mitte.Controls.Add(_pool);
        Controls.Add(mitte);
        Controls.Add(_reiter);
        Controls.Add(_lupe);
        Controls.Add(_fuss);
        Controls.Add(_detail);
        Controls.Add(_zeitZeile);
        Controls.Add(ts);
        KleinAnwenden();
        if (klein) Height = KleineHoehe();
        ModusAnwenden();
        Neuaufbau();
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        AppZustand.ZelleGewaehlt += ZelleGewaehlt;
        AppZustand.DatumGewechselt += DatumGewechselt;
        AppZustand.ModusGewechselt += ModusAnwenden;
        AppZustand.VertretungGewaehlt += VertretungGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.VertretungGewaehlt -= VertretungGewaehlt;
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
            AppZustand.ZelleGewaehlt -= ZelleGewaehlt;
            AppZustand.DatumGewechselt -= DatumGewechselt;
            AppZustand.ModusGewechselt -= ModusAnwenden;
        };
    }

    /// <summary>Im Vertretungsmodus zeigt der Stundenplan immer den tagesaktuellen Plan der gewählten Woche.</summary>
    private void ModusAnwenden()
    {
        // Vorbild-Farben des Vertretungsmodus für die Zeilen um den Plan
        _zeitLeiste.BackColor = AppZustand.Vertretungsmodus ? Ui.Leiste : PlanFlaeche;
        _zeitZeile.BackColor = _fuss.BackColor = _planRahmen.BackColor = AppZustand.Vertretungsmodus ? Ui.BlauSehrHell : PlanFlaeche;
        _pool.BackColor = AppZustand.Vertretungsmodus ? Color.FromArgb(244, 246, 250) : Color.FromArgb(232, 244, 255);
        _detail.BackColor = AppZustand.Vertretungsmodus ? Color.FromArgb(248, 249, 252) : PlanFlaeche;
        _detail.Visible = !_klein && !AppZustand.Vertretungsmodus;
        _laden = true;
        _wochenansicht.Checked = AppZustand.Vertretungsmodus || _wochenansicht.Checked && !AppZustand.Vertretungsmodus;
        _wochenansicht.Enabled = true;
        if (!AppZustand.Vertretungsmodus) _wochenansicht.Checked = false;
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _laden = false;
        WocheSetzen();
        // im Vertretungsmodus kompakter, damit der Plan ohne Blättern auf die Seite passt
        _planGroesse?.Invoke();
    }

    private void DatumGewechselt(DateOnly datum)
    {
        if (IsDisposed || !Verknuepft) return;
        _laden = true;
        _datum.Value = datum.ToDateTime(TimeOnly.MinValue);
        _laden = false;
        if (_plan.Woche.HasValue && (Kalender.Montag(datum) != _plan.Woche || _nurTag)) WocheSetzen();
        else { _plan.Anzeigewoche = Kalender.Montag(datum); BisTextSetzen(); _plan.Invalidate(); }
    }

    /// <summary>Ende des angezeigten Zeitraums neben dem Datum („- 16.10.2026“ bzw. Ende der Periode).</summary>
    private void BisTextSetzen()
    {
        if (_plan.Woche is DateOnly w) _bisText.Text = _nurTag ? "" : $"- {w.AddDays(D.Zeitraster.TageProWoche - 1):d.M.yyyy}";
        else _bisText.Text = $"- {D.AktivePeriode.Bis ?? D.Einstellungen.SchuljahrBis:d.M.yyyy}";
    }

    /// <summary>Formatauswahl in der Fußzeile rechts ausrichten (im kleinen Plan über fast die ganze Breite).</summary>
    private void FussAnordnen()
    {
        _fuss.Wunschbreite = _klein ? 150 : 190;
        _fuss.Anordnen();
    }

    /// <summary>Anzeige eines Formats wie im Vorbild: „Kla1 - Klassenplan klein“, große Pläne „Kla1A - Klassenplan groß“.</summary>
    private string FormatAnzeige(string name)
    {
        var i = Math.Max(0, _format.Items.IndexOf(name));
        var kurz = _typ switch { ElementTyp.Klasse => "Kla", ElementTyp.Lehrer => "Leh", ElementTyp.Raum => "Rau", ElementTyp.Fach => "Fa", _ => "Stp" };
        return $"{kurz}{i + 1}{(_gross && !_klein ? "A" : "")} - {name}";
    }

    /// <summary>Selbsttest: Formatauswahl liegt mit Abstand zum unteren und rechten Rand in der Fußleiste.</summary>
    internal bool FormatauswahlFrei => _fuss.AuswahlFrei && _fuss.Visible;

    /// <summary>Planart wechseln (Symbol neben der Elementauswahl): Klasse, Lehrer, Raum oder Fach.</summary>
    /// <summary>„Art fixieren“: die Planart (Klasse/Lehrer/Raum) lässt sich nicht mehr umschalten.</summary>
    internal bool ArtFixiert { get; private set; }

    /// <summary>„Ansicht fixiert“: der Plan folgt nicht mehr der Auswahl in anderen Fenstern.</summary>
    internal bool AnsichtFixiert { get; private set; }

    /// <summary>Menü „»“ am Ende der Werkzeugleiste wie im Vorbild (für den Selbsttest erreichbar).</summary>
    internal ToolStripDropDownButton MehrMenue { get; private set; } = null!;

    /// <summary>
    /// Menü „»“ wie im Vorbild: Unterrichts-Farben anzeigen, Art fixieren, Ansicht fixiert, Seitenlayout ▸ (Seitenansicht, Seitenlayout),
    /// Einstellungen, Alle Elemente im Unterricht, Schaltflächen hinzufügen oder entfernen ▸.
    /// </summary>
    private ToolStripDropDownButton MehrMenueAufbauen(ToolStrip ts)
    {
        var mehr = new ToolStripDropDownButton("»") { Alignment = ToolStripItemAlignment.Right, Overflow = ToolStripItemOverflow.Never, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Weitere Optionen", ShowDropDownArrow = true, Font = new Font(Ui.Schrift, FontStyle.Bold) };
        var farben = new ToolStripMenuItem("Unterrichts-Farben anzeigen", Symbole.Get("farbe", 16)) { CheckOnClick = true };
        farben.Click += (_, _) =>
        {
            var f = Kopie(_plan.Format);
            f.Farbe = farben.Checked ? (f.Farbe == FarbeNach.Keine ? FarbeNach.Fach : f.Farbe) : FarbeNach.Keine;
            FormatSpeichern(f, f.Name);
        };
        var art = new ToolStripMenuItem("Art fixieren", Symbole.Get("fixieren", 16)) { CheckOnClick = true };
        art.Click += (_, _) => { ArtFixiert = art.Checked; _typKnopf.Enabled = !ArtFixiert; };
        var ansicht = new ToolStripMenuItem("Ansicht fixiert", Symbole.Get("schloss", 16)) { CheckOnClick = true };
        ansicht.Click += (_, _) =>
        {
            AnsichtFixiert = ansicht.Checked;
            if (AnsichtFixiert) Verknuepft = false;
            _sync.Enabled = !AnsichtFixiert;
            _sync.Checked = Verknuepft;
        };
        var layout = new ToolStripMenuItem("Seitenlayout", Symbole.Get("einstellungen", 16));
        layout.DropDownItems.Add("Seitenansicht", Symbole.Get("vorschau", 16), (_, _) => Drucken(true));
        layout.DropDownItems.Add("Seitenlayout", Symbole.Get("einstellungen", 16), (_, _) => Seitenlayout());
        var einstellungen = new ToolStripMenuItem("Einstellungen", Symbole.Get("einstellungen", 16), (_, _) => Einstellungen());
        var alle = new ToolStripMenuItem("Alle Elemente im Unterricht", Symbole.Get("uebersicht", 16)) { CheckOnClick = true };
        alle.Click += (_, _) => _alleElemente.Checked = alle.Checked;
        var knoepfe = new ToolStripMenuItem("Schaltflächen hinzufügen oder entfernen");
        mehr.DropDownItems.AddRange(new ToolStripItem[] { farben, art, ansicht, layout, einstellungen, alle, knoepfe });
        mehr.DropDownOpening += (_, _) =>
        {
            farben.Checked = _plan.Format.Farbe != FarbeNach.Keine;
            art.Checked = ArtFixiert;
            ansicht.Checked = AnsichtFixiert;
            alle.Checked = _alleElemente.Checked;
            // alle Schaltflächen der Leiste ein-/ausblendbar (Auswahlfeld und das Menü selbst bleiben immer sichtbar)
            knoepfe.DropDownItems.Clear();
            foreach (ToolStripItem it in ts.Items)
            {
                if (it == mehr || it == _auswahl || it is ToolStripSeparator) continue;
                var titel = !string.IsNullOrEmpty(it.ToolTipText) ? it.ToolTipText.Split('\n')[0] : it.Text;
                if (string.IsNullOrWhiteSpace(titel)) continue;
                var eintrag = new ToolStripMenuItem(titel, it.Image) { Checked = it.Available, CheckOnClick = true };
                var ziel = it;
                eintrag.Click += (_, _) => ziel.Available = eintrag.Checked;
                knoepfe.DropDownItems.Add(eintrag);
            }
        };
        MehrMenue = mehr;
        return mehr;
    }

    /// <summary>Seitenlayout des Stundenplans: Papierformat, Ausrichtung und Ränder für Druck und Seitenansicht.</summary>
    private void Seitenlayout()
    {
        using var dlg = new PageSetupDialog { PageSettings = StundenplanDruck.Seite, EnableMetric = true, AllowPrinter = true };
        try { dlg.ShowDialog(this); }
        catch (System.Drawing.Printing.InvalidPrinterException ex) { Ui.Info("Kein Drucker eingerichtet – das Seitenlayout kann erst mit einem Drucker gewählt werden.\n\n" + ex.Message, "Seitenlayout"); }
    }

    private void TypSetzen(ElementTyp typ)
    {
        if (typ == _typ) return;
        _typ = typ;
        _id = Stammdaten.Sortiert(D, typ).FirstOrDefault()?.Id ?? 0;
        _formatName = null;
        _plan.Format = FormatHolen();
        _plan.AuswahlLeeren();
        FormateFuellen();
        TypSymbol();
        _planGroesse?.Invoke();
        Neuaufbau();
    }

    private void TypSymbol() => _typKnopf.Image = Symbole.Get(_typ switch { ElementTyp.Lehrer => "lehrer", ElementTyp.Raum => "raeume", ElementTyp.Fach => "faecher", _ => "klassen" }, 16);

    private sealed record Eintrag(Element E)
    {
        public override string ToString() => E.Kurzname;
    }

    private string FormatSchluessel => _typ + (_klein ? " klein" : _gross ? " groß" : "");

    private string StandardName => (_typ switch { ElementTyp.Klasse => "Klassenplan", ElementTyp.Lehrer => "Lehrerplan", ElementTyp.Raum => "Raumplan", ElementTyp.Fach => "Fachplan", _ => "Stundenplan" }) + (_klein ? " klein" : _gross ? " groß" : "");

    /// <summary>Gewähltes Format (eigenes Format nach Name, sonst das erste passende bzw. ein Standardformat); kleine Pläne nur mit kleinen Formaten.</summary>
    private StundenplanFormat FormatHolen()
    {
        AppZustand.Einstellungen.Stundenplanformate.TryGetValue(FormatSchluessel, out var gewaehlt);
        var name = _formatName ?? gewaehlt;
        var f = (name is null ? null : D.Formate.FirstOrDefault(x => x.Typ == _typ && x.Name == name && x.Klein == _klein)) ??
                D.Formate.FirstOrDefault(x => x.Typ == _typ && x.Klein == _klein && (_klein || x.Gross == _gross));
        return f ?? (_klein ? StundenplanFormat.KleinesFormat(_typ, StandardName) : new StundenplanFormat { Name = StandardName, Typ = _typ, Gross = _gross, Schriftgroesse = _gross ? 10 : 9 });
    }

    /// <summary>Zwischen normalem und kleinem Stundenplan umschalten (Fenstergröße, Werkzeugleiste, Format).</summary>
    public void KleinSetzen(bool klein)
    {
        if (_klein == klein) return;
        _klein = klein;
        _formatName = null;
        _plan.Format = FormatHolen();
        FormateFuellen();
        if (WindowState == FormWindowState.Normal)
            Size = klein ? new Size(196, KleineHoehe()) : _gross ? new Size(820, 640) : new Size(560, 520);
        ModusAnwenden();
        KleinAnwenden();
        Neuaufbau(false);
    }

    /// <summary>Passende Fensterhöhe des kleinen Plans (Plan ohne Blättern, Werkzeugleiste, Titel, ggf. Lupe).</summary>
    private int KleineHoehe() => _plan.BenoetigteHoehe + FensterTitel.Hoehe + 27 + _zeitZeile.Height + _fuss.Height + (_lupeSichtbar ? (_klein ? 80 : _lupe.Height) : 0) + 14;

    /// <summary>Sichtbarkeit der Bereiche: Der kleine Plan zeigt nur Werkzeugleiste (Element, Blättern, Verknüpfung) und Plan, optional die Lupe.</summary>
    private void KleinAnwenden()
    {
        _laden = true;
        _kleinKnopf.Checked = _klein;
        _laden = false;
        // kleiner Plan wie im Vorbild: Elementauswahl, Hoch/Runter, Planart und Verknüpfung sichtbar, alles Weitere hinter „»“
        foreach (ToolStripItem it in _leiste.Items)
        {
            it.Visible = true;
            it.Overflow = _klein && !_kleinSichtbar.Contains(it) ? ToolStripItemOverflow.Always : ToolStripItemOverflow.AsNeeded;
        }
        _auswahl.Width = _klein ? 70 : 100;
        Titelleiste.DurchschalterLinks = _klein;
        Titelleiste.Invalidate();
        TypSymbol();
        FussAnordnen();
        _detail.Visible = !_klein && !AppZustand.Vertretungsmodus;
        _pool.Visible = !_klein;
        if (_klein) _reiter.Visible = false;
        else _reiter.Visible = _alleElemente.Checked;
        _lupe.Height = _klein ? 80 : 74;
        LupeUmschalten(_klein ? _plan.Format.StundenplanlupeAnzeigen : true);
        _planGroesse?.Invoke();
    }

    private void LupeUmschalten(bool sichtbar)
    {
        _laden = true;
        _lupeKnopf.Checked = sichtbar;
        _laden = false;
        // eigener Zustand: Visible meldet vor dem Anzeigen des Fensters immer false
        if (_lupeSichtbar == sichtbar) return;
        _lupeSichtbar = sichtbar;
        _lupe.Visible = sichtbar;
        _lupe.Height = _klein ? 80 : 74;
        if (_klein && WindowState == FormWindowState.Normal) Height = Math.Max(200, Height + (sichtbar ? _lupe.Height : -_lupe.Height));
        _planGroesse?.Invoke();
    }

    private bool _lupeSichtbar = true;
    private string? _formatName;
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };

    /// <summary>Auswahl der Formate dieser Planart füllen.</summary>
    private void FormateFuellen()
    {
        _laden = true;
        _format.Items.Clear();
        var namen = D.Formate.Where(x => x.Typ == _typ && x.Klein == _klein).Select(x => x.Name).Where(n => n.Length > 0).Distinct().ToList();
        if (!namen.Contains(_plan.Format.Name)) namen.Insert(0, _plan.Format.Name);
        foreach (var n in namen) _format.Items.Add(n);
        _format.Items.Add(FormatTrenner);
        _format.Items.Add(new FormatBefehl("Format speichern", FormatAktuellSpeichern));
        _format.Items.Add(new FormatBefehl("Format speichern als…", FormatSpeichernAls));
        _format.Items.Add(new FormatBefehl("Neues Format…", FormatNeu));
        _format.Items.Add(new FormatBefehl("Bearbeiten", Einstellungen));
        _format.Items.Add(new FormatBefehl("Löschen", FormatLoeschen));
        _format.SelectedItem = _plan.Format.Name;
        _laden = false;
    }

    // ------------------------------------------------------------------ Formatauswahl unten: Formate und Befehle

    /// <summary>Befehl in der Formatauswahl (unter den Formaten, fett wie im Vorbild).</summary>
    private sealed record FormatBefehl(string Text, Action Aktion)
    {
        public override string ToString() => Text;
    }

    private static readonly object FormatTrenner = new();

    private void FormatAuswahlGeaendert()
    {
        if (_laden) return;
        switch (_format.SelectedItem)
        {
            case string n: FormatWaehlen(n); break;
            case FormatBefehl b:
                // zurück auf das aktuelle Format, dann den Befehl ausführen
                _laden = true;
                _format.SelectedItem = _plan.Format.Name;
                _laden = false;
                BeginInvoke(b.Aktion);
                break;
            default:
                _laden = true;
                _format.SelectedItem = _plan.Format.Name;
                _laden = false;
                break;
        }
    }

    private int FormatListenbreite()
    {
        using var g = _format.CreateGraphics();
        using var fett = new Font(_format.Font, FontStyle.Bold);
        var w = 0;
        foreach (var o in _format.Items)
            w = Math.Max(w, TextRenderer.MeasureText(g, o is string n ? FormatAnzeige(n) : o.ToString(), o is FormatBefehl ? fett : _format.Font).Width);
        return w + 16 + (_format.Items.Count > _format.MaxDropDownItems ? SystemInformation.VerticalScrollBarWidth : 0);
    }

    private void FormatEintragZeichnen(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var o = _format.Items[e.Index];
        var imFeld = (e.State & DrawItemState.ComboBoxEdit) != 0;
        if (o == FormatTrenner)
        {
            using var hg = new SolidBrush(_format.BackColor);
            e.Graphics.FillRectangle(hg, e.Bounds);
            var y = e.Bounds.Top + e.Bounds.Height / 2;
            e.Graphics.DrawLine(SystemPens.ControlDark, e.Bounds.Left + 2, y, e.Bounds.Right - 2, y);
            return;
        }
        e.DrawBackground();
        using var fett = new Font(e.Font ?? _format.Font, FontStyle.Bold);
        var text = o is string n ? FormatAnzeige(n) : o?.ToString() ?? "";
        Ui.ListenText(e.Graphics, text, o is FormatBefehl && !imFeld ? fett : e.Font ?? _format.Font, new Rectangle(e.Bounds.X + 1, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height),
            (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : _format.ForeColor);
        e.DrawFocusRectangle();
    }

    /// <summary>„Format speichern“: das aktuelle Format (auch ein noch nicht gespeichertes Standardformat) in der Datei ablegen.</summary>
    private void FormatAktuellSpeichern()
    {
        var f = Kopie(_plan.Format);
        if (f.Name.Length == 0) f.Name = FormatAnzeige(_plan.Format.Name);
        f.Typ = _typ;
        FormatSpeichern(f, _plan.Format.Name);
        AppZustand.Haupt.StatusMelden($"Stundenplanformat „{f.Name}“ gespeichert.");
    }

    /// <summary>„Format speichern als…“: Kopie des aktuellen Formats unter neuem Namen.</summary>
    private void FormatSpeichernAls()
    {
        var name = Ui.Eingabe("Name des Stundenplanformats:", "Format speichern als", _plan.Format.Name + " 2");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (D.Formate.Any(x => x.Typ == _typ && x.Name == name) && !Ui.Frage($"Das Format \"{name}\" gibt es bereits. Überschreiben?")) return;
        var f = Kopie(_plan.Format);
        f.Name = name;
        f.Typ = _typ;
        FormatSpeichern(f, name);
    }

    private void FormatWaehlen(string name)
    {
        _formatName = name;
        AppZustand.Einstellungen.Stundenplanformate[FormatSchluessel] = name;
        AppZustand.Einstellungen.Speichern();
        _plan.Format = FormatHolen();
        _planGroesse?.Invoke();
        Neuaufbau(false);
    }

    private void FormatNeu()
    {
        // wie im Vorbild: zuerst die Grundform wählen (Format 01, 10, 11, 20, 21, 30, 40)
        if (StundenplanGrundformDialog.Zeigen(this, _plan.Format.Grundform) is not { } grundform) return;
        var name = Ui.Eingabe("Name des neuen Stundenplanformats (Kopie des aktuellen Formats):", "Neues Stundenplanformat", _plan.Format.Name + " 2");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (D.Formate.Any(x => x.Typ == _typ && x.Name == name)) { Ui.Info($"Das Format \"{name}\" gibt es bereits."); return; }
        var f = Kopie(_plan.Format);
        f.Name = name;
        f.Typ = _typ;
        f.Grundform = grundform;
        Aendern("Neues Stundenplanformat", AenderungsArt.Einstellungen, d => d.Formate.Add(f));
        FormatWaehlen(name);
        FormateFuellen();
        Einstellungen();
    }

    private void FormatUmbenennen()
    {
        var alt = _plan.Format.Name;
        var name = Ui.Eingabe("Neuer Name des Stundenplanformats:", "Format umbenennen", alt);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == alt) return;
        var f = Kopie(_plan.Format);
        f.Name = name.Trim();
        FormatSpeichern(f, alt);
    }

    private void FormatLoeschen()
    {
        var name = _plan.Format.Name;
        if (!D.Formate.Any(x => x.Typ == _typ && x.Name == name)) { Ui.Info("Das Standardformat ist nicht gespeichert und kann nicht gelöscht werden."); return; }
        if (!Ui.Frage($"Stundenplanformat \"{name}\" löschen?")) return;
        Aendern("Stundenplanformat löschen", AenderungsArt.Einstellungen, d => d.Formate.RemoveAll(x => x.Typ == _typ && x.Name == name));
        _formatName = null;
        AppZustand.Einstellungen.Stundenplanformate.Remove(FormatSchluessel);
        _plan.Format = FormatHolen();
        FormateFuellen();
        _planGroesse?.Invoke();
        Neuaufbau(false);
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || typ != _typ || _reiter.Visible || !Verknuepft || IsDisposed || id == _id) return;
        _id = id;
        _plan.MarkierterUnterricht = null;
        Neuaufbau();
    }

    /// <summary>Stundenplan-Synchronisation: andere Pläne zeigen die Elemente der aktivierten Stunde.</summary>
    /// <summary>Position dieses Fensters unter den geöffneten Plänen desselben Typs (1. Lehrerplan, 2. Lehrerplan …).</summary>
    private int FensterIndex(object? quelle) =>
        Math.Max(0, AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().Where(f => f != quelle && f._typ == _typ && !f._reiter.Visible).ToList().IndexOf(this));

    private static int? Wahl(IReadOnlyList<int> ids, int index) => ids.Count == 0 ? null : ids[index % ids.Count];

    /// <summary>Stundenplan-Synchronisation: andere Pläne zeigen die Elemente der aktivierten Stunde (bei mehreren Plänen desselben Typs der Reihe nach).</summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Verknuepft || _reiter.Visible || IsDisposed) return;
        var k = FensterIndex(quelle);
        // Elemente der angeklickten Zeile haben Vorrang (z. B. Lehrerplan → Klasse dieser Lehrkraft)
        int? neu = _typ switch
        {
            ElementTyp.Lehrer => Wahl((AppZustand.AuswahlLehrer is { Count: > 0 } al ? al : u.LehrerIds).ToList(), k),
            ElementTyp.Klasse => Wahl((AppZustand.AuswahlKlassen is { Count: > 0 } ak ? ak : u.KlassenIds).ToList(), k),
            ElementTyp.Raum => Wahl(AppZustand.AuswahlRaeume is { Count: > 0 } ar ? ar.ToList() :
                p?.Raeume.Where(r => r.HasValue).Select(r => r!.Value).ToList() ?? u.Zeilen.Where(z => z.FachraumId.HasValue).Select(z => z.FachraumId!.Value).ToList(), k),
            ElementTyp.Fach => u.HauptfachId,
            _ => null,
        };
        var datum = AppZustand.AuswahlDatum;
        if (neu is not null && neu != _id)
        {
            _id = neu.Value;
            Neuaufbau();
        }
        // dieselbe Stunde markieren; ohne Stunde (Auswahl im Unterrichtsfenster) alle Stunden des Unterrichts gestrichelt
        _plan.MarkierterUnterricht = u.Id;
        if (_plan.Tagesmodus && datum is DateOnly dt && p != null) _plan.TagesAuswaehlen(dt, p.Stunde, u.Id);
        else if (_plan.Tagesmodus && p != null) _plan.TagesAuswaehlen(AppZustand.Datum, p.Stunde, u.Id);
        else if (p != null)
        {
            _plan.Mehrfach.Clear();
            _plan.Ausgewaehlt = D.Stundenplan.Contains(p) ? p : D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == p.UnterrichtId && x.Tag == p.Tag && x.Stunde == p.Stunde);
        }
        else _plan.Ausgewaehlt = null;
        _plan.Invalidate();
        if (neu is not null) LupeFuellen(p);
    }

    /// <summary>Nicht verplante Stunde angeklickt: verknüpfte Pläne auf denselben Tag und dieselbe Stunde setzen.</summary>
    private void LeereZelleGewaehlt(int spalte, int stunde)
    {
        _plan.MarkierterUnterricht = null;
        if (_plan.Tagesmodus) TagesLupe(null);
        else LupeFuellen(null);
        if (Core.Planung.PlanSynchronisation.AusSpalte(D, spalte, stunde, _plan.Woche) is { } z) AppZustand.ZelleWaehlen(z, _typ, _id, this);
    }

    /// <summary>
    /// Stundenplan-Synchronisation für leere Zellen: das angezeigte Element bleibt, die bisherige Auswahl wird aufgehoben und dieselbe
    /// Stunde am selben (Wochen-)Tag markiert – auch wenn das Element dort keinen Unterricht hat.
    /// </summary>
    private void ZelleGewaehlt(Core.Planung.PlanZelle z, ElementTyp typ, int id, object quelle)
    {
        if (quelle == this || !Verknuepft || _reiter.Visible || IsDisposed) return;
        var spalte = Core.Planung.PlanSynchronisation.ZielSpalte(D, z, _plan.Woche, _plan.Spalten);
        if (spalte is int s) _plan.ZelleMarkieren(s, z.Stunde);
        else _plan.AuswahlLeeren();
        _plan.MarkierterUnterricht = null;
        if (_plan.Tagesmodus) TagesLupe(null);
        else LupeFuellen(null);
    }

    /// <summary>Selbsttest: leere Zelle (Spalte, Stunde) anklicken wie mit der Maus.</summary>
    internal void LeereZelleAnklicken(int spalte, int stunde)
    {
        _plan.ZelleMarkieren(spalte, stunde);
        AuswahlGeaendert(null);
        LeereZelleGewaehlt(spalte, stunde);
    }

    /// <summary>Gewählte Vertretung: Klassenplan der Klasse, Lehrerpläne von (Lehrer) und Vertreter, Raumplan – tagesaktuell zur Woche der Vertretung.</summary>
    private void VertretungGewaehlt(Vertretung? v)
    {
        if (v is null || IsDisposed || !Verknuepft || _reiter.Visible || ContainsFocus) return;
        var k = FensterIndex(null);
        int? neu = _typ switch
        {
            ElementTyp.Klasse => Wahl(v.NeueKlassenIds.ToList(), k),
            ElementTyp.Lehrer => Wahl(new[] { v.LehrerId, v.VertreterId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList(), k),
            ElementTyp.Raum => Wahl(new[] { v.VertretungsraumId, v.RaumId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList(), k),
            ElementTyp.Fach => v.VertretungsfachId ?? v.FachId,
            _ => null,
        };
        var wocheAnders = _plan.Woche.HasValue && Kalender.Montag(v.Datum) != _plan.Woche;
        if (wocheAnders)
        {
            _laden = true;
            _datum.Value = v.Datum.ToDateTime(TimeOnly.MinValue);
            _laden = false;
        }
        if (neu is int n && n != _id)
        {
            _id = n;
            Neuaufbau();
        }
        if (wocheAnders) WocheSetzen();
        // gewählte Vertretung im Plan markieren (dieselbe Stunde wie im Vertretungsfenster)
        _plan.MarkierterUnterricht = v.UnterrichtId;
        if (_plan.Tagesmodus) _plan.TagesAuswaehlen(v.Datum, v.Stunde, v.UnterrichtId ?? 0, v.Id);
        else _plan.Invalidate();
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

    /// <summary>Vertretungsmodus, Zeitbereich „Periode“: Regelplan nur zur Ansicht (Stundenplanänderungen gehören in den Stundenplanmodus).</summary>
    private bool RegelplanNurAnzeige => AppZustand.Vertretungsmodus && !_plan.Tagesmodus;

    /// <summary>
    /// Kontextmenü „Zeitbereich“ wie im Vorbild: Woche (tagesaktueller Plan der Woche mit allen Vertretungen) oder Periode
    /// (Regelplan der aktiven Periode). Im Vertretungsmodus ist „Woche“ der Standard, „Periode“ dient zum Nachsehen.
    /// </summary>
    private ToolStripMenuItem ZeitbereichMenue()
    {
        var woche = _plan.Woche.HasValue;
        var tag = woche && _nurTag;
        var m = new ToolStripMenuItem("Zeitbereich: " + (tag ? "Tag" : woche ? "Woche" : "Periode"));
        var t = new ToolStripMenuItem("Tag – nur der gewählte Tag, tagesaktuell") { Checked = tag };
        t.Click += (_, _) => ZeitbereichSetzen(true, true);
        var w = new ToolStripMenuItem("Woche – tagesaktuell mit Vertretungen") { Checked = woche && !tag };
        w.Click += (_, _) => ZeitbereichSetzen(true, false);
        var p = new ToolStripMenuItem($"Periode – Regelplan ({D.AktivePeriode.Name})") { Checked = !woche };
        p.Click += (_, _) => ZeitbereichSetzen(false, false);
        m.DropDownItems.Add(t);
        m.DropDownItems.Add(w);
        m.DropDownItems.Add(p);
        if (woche)
        {
            m.DropDownItems.Add(new ToolStripSeparator());
            if (tag)
            {
                m.DropDownItems.Add("Vorheriger Tag", null, (_, _) => TagBlaettern(-1));
                m.DropDownItems.Add("Nächster Tag", null, (_, _) => TagBlaettern(1));
            }
            m.DropDownItems.Add("Vorherige Woche", null, (_, _) => WocheBlaettern(-7));
            m.DropDownItems.Add("Nächste Woche", null, (_, _) => WocheBlaettern(7));
        }
        return m;
    }

    /// <summary>Zeitbereich „Tag“: nur der gewählte Tag über die ganze Fensterbreite.</summary>
    private bool _nurTag;

    private void ZeitbereichSetzen(bool woche, bool tag)
    {
        _nurTag = tag;
        if (_wochenansicht.Checked != woche) _wochenansicht.Checked = woche; // ruft WocheSetzen auf
        else WocheSetzen();
    }

    /// <summary>Zum vorherigen bzw. nächsten Schultag der Woche blättern.</summary>
    private void TagBlaettern(int richtung)
    {
        var ziel = DateOnly.FromDateTime(_datum.Value);
        for (var i = 0; i < 14; i++)
        {
            ziel = ziel.AddDays(richtung);
            if (ziel < D.Einstellungen.SchuljahrVon || ziel > D.Einstellungen.SchuljahrBis) return;
            if (Kalender.TagIndex(D, ziel) >= 0) break; // Wochenende überspringen, Ferien werden angezeigt
        }
        _datum.Value = ziel.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Selbsttest: Zeitbereich umschalten (Woche bzw. Periode) und prüfen.</summary>
    internal void SelbsttestZeitbereich(bool woche, bool tag = false)
    {
        ZeitbereichSetzen(woche, tag);
        if (tag && _plan.NurTag is null) throw new InvalidOperationException("Zeitbereich „Tag“ zeigt nicht nur einen Tag.");
        if (!tag && _plan.NurTag is not null) throw new InvalidOperationException("Zeitbereich zeigt noch nur einen Tag.");
        if (tag) return;
        if (_plan.Woche.HasValue != woche) throw new InvalidOperationException("Zeitbereich wurde nicht umgeschaltet.");
        if (ZeitbereichMenue().Text != "Zeitbereich: " + (woche ? "Woche" : "Periode")) throw new InvalidOperationException("Kontextmenü Zeitbereich zeigt den falschen Zustand.");
    }

    private void WocheBlaettern(int tage)
    {
        var ziel = DateOnly.FromDateTime(_datum.Value).AddDays(tage);
        if (ziel < D.Einstellungen.SchuljahrVon || ziel > D.Einstellungen.SchuljahrBis) return;
        _datum.Value = ziel.ToDateTime(TimeOnly.MinValue);
    }

    private void WocheSetzen()
    {
        _plan.Woche = _wochenansicht.Checked ? Kalender.Montag(DateOnly.FromDateTime(_datum.Value)) : null;
        _plan.Anzeigewoche = Kalender.Montag(DateOnly.FromDateTime(_datum.Value));
        _plan.NurTag = _nurTag && _wochenansicht.Checked ? Kalender.TagIndex(D, DateOnly.FromDateTime(_datum.Value)) : null;
        _plan.AuswahlLeeren();
        LupeSpalten(_plan.Tagesmodus);
        _lupe.Rows.Clear();
        BisTextSetzen();
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
        Text = el is null ? $"Stundenplan ({fmtName})" : _klein ? $"{el.Kurzname} – {(el is Lehrer lk ? lk.VollerName : el.Langname)}" : $"{el.Kurzname} – {(el is Lehrer l ? l.VollerName : el.Langname)} ({fmtName})";
        Tag = $"sp:{_typ}:{_id}:{(_gross ? 1 : 0)}" + (_klein ? ":k" : "");
        _plan.Typ = _typ;
        _plan.ElementId = _id;
        _plan.AuswahlAktualisieren();
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
        _plan.Ecke = $"Nvpl Std.\n{nv}/{wst}";
        if (_plan.Woche is DateOnly w)
        {
            // nicht verplant: Stunden neben dem Plan (Vormerkungen) / Stunden der Woche
            if (_plan.Tagesmodus)
            {
                var neben = D.Vormerkungen.Where(v => v.Offen > 0 && (_typ == ElementTyp.Klasse ? v.KlassenIds.Contains(_id) : _typ == ElementTyp.Lehrer && v.LehrerIds.Contains(_id))).Sum(v => v.Offen);
                _plan.Ecke = $"Nvpl Std.\n{neben}/{_plan.StundenDerWoche()}";
            }
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
            _pool.Controls.Add(new Label
            {
                Text = "Ablage", Image = Symbole.Get("vormerkung", 32), ImageAlign = ContentAlignment.TopCenter, TextAlign = ContentAlignment.BottomCenter, Width = 64, Height = 54,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Ui.BlauSehrHell, Margin = new Padding(3, 3, 3, 6),
            });
            new ToolTip().SetToolTip(_pool.Controls[^1], "Stunde hierher ziehen = neben den Stundenplan legen (Vormerkung). Von hier in eine andere Position oder Woche ziehen.");
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
        var offenGesamt = _typ == ElementTyp.Raum ? 0 : Ix.UnterrichteVon(_typ, _id).Where(u => !u.Ignorieren).Sum(u => Ix.NichtVerplant(u));
        var titel = new Label { Text = $"Nicht verplant ({offenGesamt})", Font = Ui.SchriftFett, AutoSize = true, Margin = new Padding(2, 2, 2, 6) };
        _pool.Controls.Add(titel);
        _pool.SetFlowBreak(titel, true);
        if (_typ != ElementTyp.Raum)
        {
            // offene Stunden direkt als Blöcke in ihrer Blockgröße (Doppelstunde doppelt hoch), jeder Block einzeln ziehbar
            foreach (var u in Ix.UnterrichteVon(_typ, _id).Where(u => !u.Ignorieren && Ix.NichtVerplant(u) > 0).OrderBy(u => u.Id))
            {
                var fach = string.Join(",", u.FachIds.Select(f => Ix.KFach(f)).Distinct());
                var info = _typ == ElementTyp.Lehrer ? Ix.KKlassen(u.KlassenIds) : string.Join(",", u.LehrerIds.Select(Ix.KLehrer));
                foreach (var b in Planungsdienst.OffeneBloecke(D, Ix, u))
                {
                    var block = new PoolBlock(u, b, fach.Length > 0 ? fach : "?", info, (u.Zeilen.Count > 1 ? "." : ""));
                    block.MouseDown += (_, e) =>
                    {
                        if (e.Button != MouseButtons.Left) return;
                        // Strg+Klick: mehrere Unterrichte markieren und gemeinsam verplanen
                        if ((ModifierKeys & Keys.Control) != 0)
                        {
                            var neu = !block.Markiert;
                            foreach (var x in _pool.Controls.OfType<PoolBlock>().Where(x => x.Unterricht == u)) { x.Markiert = neu; x.Invalidate(); }
                            return;
                        }
                        LupeFuellen(null, u);
                        var markiert = _pool.Controls.OfType<PoolBlock>().Where(x => x.Markiert).Select(x => x.Unterricht.Id).Distinct().ToList();
                        var daten = markiert.Count > 1 && markiert.Contains(u.Id) ? "unterricht:" + string.Join(",", markiert) : $"unterricht:{u.Id}|{block.Laenge}";
                        block.DoDragDrop(daten, DragDropEffects.Move);
                    };
                    _pool.Controls.Add(block);
                }
            }
        }
        _pool.ResumeLayout();
    }

    /// <summary>Selbsttest: Blöcke unter „nicht verplant“ (U-Nr., Blocklänge, Höhe).</summary>
    internal List<(int Unterricht, int Laenge, int Hoehe)> PoolBloecke => _pool.Controls.OfType<PoolBlock>().Select(b => (b.Unterricht.Id, b.Laenge, b.Height)).ToList();

    /// <summary>
    /// Nicht verplanter Block eines Unterrichts in seiner Blockgröße wie im Vorbild: eine Einzelstunde als Kästchen, eine Doppelstunde
    /// doppelt so hoch usw. – mit Fach, Klasse bzw. Lehrer und Blocklänge; zum Verplanen in den Stundenplan ziehen.
    /// </summary>
    private sealed class PoolBlock : Control
    {
        /// <summary>Höhe einer Stunde im Block.</summary>
        public const int Einheit = 32;
        public Unterricht Unterricht { get; }
        public int Laenge { get; }
        public bool Markiert { get; set; }
        private readonly string _fach;
        private readonly string _info;
        private readonly string _praefix;

        public PoolBlock(Unterricht u, int laenge, string fach, string info, string praefix)
        {
            Unterricht = u;
            Laenge = Math.Max(1, laenge);
            _fach = fach;
            _info = info;
            _praefix = praefix;
            Size = new Size(64, Laenge * Einheit + 2);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            new ToolTip().SetToolTip(this, $"U-Nr. {u.Id}: {(Laenge > 1 ? $"{Laenge}er-Block (Doppelstunde = 2)" : "Einzelstunde")} nicht verplant.\nIn den Stundenplan ziehen zum Verplanen. Strg+Klick markiert mehrere Unterrichte.");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(PlanAnsicht.Grund)) g.FillRectangle(b, r);
            // Stundenlinien im Block (gestrichelt) wie die Zeilen im Plan
            using (var p = new Pen(Color.FromArgb(150, 170, 190)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                for (var i = 1; i < Laenge; i++) g.DrawLine(p, 2, i * Einheit + 1, Width - 3, i * Einheit + 1);
            g.DrawRectangle(Pens.Black, r);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var mitte = Math.Max(1, (Height - 30) / 2);
            TextRenderer.DrawText(g, _praefix + _fach, Ui.SchriftFett, new Rectangle(2, mitte, Width - 4, 15), Color.Black, flags);
            TextRenderer.DrawText(g, _info, Ui.SchriftKlein, new Rectangle(2, mitte + 15, Width - 4, 14), Color.Black, flags);
            if (Laenge > 1) TextRenderer.DrawText(g, $"{Laenge} Std.", Ui.SchriftKlein, new Rectangle(2, Height - 15, Width - 5, 13), Color.FromArgb(40, 70, 120), TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            if (Markiert) using (var p = new Pen(Color.FromArgb(255, 140, 0), 2.5f)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
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

    /// <summary>Stunde wählen wie per Mausklick (markiert sie und meldet sie an die verknüpften Fenster) – für Selbsttest und Tastatur.</summary>
    internal void StundeAnklicken(Planstunde p)
    {
        _plan.Mehrfach.Clear();
        _plan.Ausgewaehlt = p;
        _plan.Invalidate();
        AuswahlGeaendert(p);
    }

    /// <summary>Selbsttest: Ablegen wie per Drag &amp; Drop (z. B. „unterricht:12|2“ aus „nicht verplant“) an (tag, stunde).</summary>
    internal void SelbsttestAblegen(string daten, int tag, int stunde) => Abgelegt(daten, tag, stunde);

    /// <summary>Format nur für die Anzeige setzen (ohne Speichern), z. B. Einzelstunden mit Pausen im Selbsttest.</summary>
    internal void FormatAnzeigen(StundenplanFormat f)
    {
        _plan.Format = f;
        _planGroesse?.Invoke();
        _plan.Invalidate();
    }

    private void AuswahlGeaendert(Planstunde? p)
    {
        _plan.MarkierterUnterricht = p?.UnterrichtId;
        LupeFuellen(p);
        ReiterAufbauen();
        if (p == null || !Ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return;
        // nur die Zeilen des angezeigten Elements weitergeben (Kopplungen: Klassen dieses Lehrers, Lehrer dieser Klasse)
        var zeilen = Enumerable.Range(0, u.Zeilen.Count).Where(i => _typ switch
        {
            ElementTyp.Lehrer => u.Zeilen[i].LehrerId == _id,
            ElementTyp.Klasse => u.Zeilen[i].KlassenIds.Contains(_id),
            ElementTyp.Raum => p.RaumFuerZeile(i) == _id,
            _ => true,
        }).ToList();
        if (zeilen.Count == 0) zeilen = Enumerable.Range(0, u.Zeilen.Count).ToList();
        AppZustand.StundeWaehlen(u, p, this,
            zeilen.SelectMany(i => u.Zeilen[i].KlassenIds).Distinct().ToList(),
            zeilen.Select(i => u.Zeilen[i].LehrerId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList(),
            zeilen.Select(i => p.RaumFuerZeile(i)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList());
    }

    private bool? _lupeTages;

    /// <summary>Spalten der Stundenplanlupe: Regelplan (U-Nr, Lehr., Fa., Rm., Kla., Wst …) bzw. tagesaktuell (U-Nr, Lehr./Fa./Rm., Kla., Zeit, Schulwoche, Studt, Sondertext, Band).</summary>
    private void LupeSpalten(bool tages)
    {
        if (_lupeTages == tages) return;
        _lupeTages = tages;
        _lupe.Rows.Clear();
        _lupe.Columns.Clear();
        // Vorbild: dieselbe Unterrichtsliste im Stunden- und im Vertretungsmodus (U-Nr, „Lehr., Fa., Rm.“, Kla., Zeit, Schulwoche …)
        var spalten = new[] { ("U", "U-Nr", 48), ("LFR", "Lehr., Fa., Rm.", 130), ("K", "Kla.", 50), ("Z", "Zeit", 40), ("W", "Schulwoche", 76), ("S", "Studt", 44), ("T", "Sondertext", 80), ("B", "Band", 40), ("Z2", "ZeilenText-2", 84), ("G", "Schülergruppe", 90) };
        foreach (var (n, t, w) in spalten)
            _lupe.Columns.Add(new DataGridViewTextBoxColumn { Name = n, HeaderText = t, Width = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        _lupe.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        // Vorbild: Kopf „Lehr., Fa., Rm.“ orange hinterlegt
        _lupe.Columns["LFR"]!.HeaderCell.Style.BackColor = Ui.Erste;
    }

    private void LupeFuellen(Planstunde? p, Unterricht? unv = null)
    {
        if (_plan.Tagesmodus) return;
        LupeSpalten(false);
        _lupe.Rows.Clear();
        var u = unv ?? (p != null ? Ix.Unterrichte.GetValueOrDefault(p.UnterrichtId) : null);
        if (u != null)
        {
            var zeit = p != null && D.Zeitraster.Zeit(p.Stunde) is { } zt ? $"{zt.Beginn:H\\:mm}-{zt.Ende:H\\:mm}" : "";
            var wochen = $"{Kalender.Schulwoche(D, u.VonDatum ?? D.Einstellungen.SchuljahrVon)}-{Kalender.Schulwoche(D, u.BisDatum ?? D.Einstellungen.SchuljahrBis)}";
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var raum = p is null ? Ix.KRaum(z.Wunschraum) : Ix.KRaum(p.RaumFuerZeile(i));
                var lfr = string.Join(", ", new[] { Ix.KLehrer(z.LehrerId), Ix.KFach(z.FachId), raum }.Where(x => x.Length > 0));
                var studt = D.Klassen.Where(k => z.KlassenIds.Contains(k.Id)).Sum(k => k.Schuelerzahl);
                var r = _lupe.Rows.Add(i == 0 ? u.Id + (p?.Fixiert == true ? "*" : "") : "", lfr, Ix.KKlassen(z.KlassenIds), zeit, wochen, studt > 0 ? studt.ToString() : "",
                    i == 0 ? u.Text : "", u.Band, z.Zeilentext, u.Schuelergruppe);
                _lupe.Rows[r].Tag = u;
            }
        }
        // Vorbild: letzte Zeile mit der Zahl der noch nicht verplanten Stunden – „+3“ für den gewählten Unterricht, „-3“ für das Element
        var offen = u != null ? Ix.NichtVerplant(u) : _typ == ElementTyp.Raum ? 0 : Ix.UnterrichteVon(_typ, _id).Where(x => !x.Ignorieren).Sum(x => Ix.NichtVerplant(x));
        var summe = _lupe.Rows.Add((u != null ? "+" : "-") + offen, "", "", "", "", "", "", "", "", "");
        var zeile = _lupe.Rows[summe];
        zeile.Cells[0].ToolTipText = u != null ? $"{offen} nicht verplante Stunde(n) von U-Nr. {u.Id}" : $"{offen} nicht verplante Stunde(n) von {Ix.Element(_typ, _id)?.Kurzname}";
        if (u is null)
            foreach (var c in new[] { 0, 1 }) { zeile.Cells[c].Style.BackColor = Ui.ZelleAktivVertretung; zeile.Cells[c].Style.SelectionBackColor = Ui.ZelleAktivVertretung; }
        _lupe.ClearSelection();
    }

    /// <summary>Selbsttest: Text der Zeile mit den nicht verplanten Stunden („+3“ bzw. „-3“).</summary>
    internal string LupeOffenText => _lupe.Rows.Count > 0 ? _lupe.Rows[^1].Cells[0].Value?.ToString() ?? "" : "";

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
        if (RegelplanNurAnzeige) { Ui.Info("Im Vertretungsmodus zeigt der Zeitbereich „Periode“ den Regelplan nur an. Zum Verlegen bitte den Zeitbereich „Woche“ wählen (rechte Maustaste › Zeitbereich)."); return; }
        if (daten.StartsWith("plan:"))
        {
            var (prim, alle) = PlanAnsicht.PlanDaten(daten);
            if (prim is null) return;
            var dt = tag - prim.Tag;
            var ds = stunde - prim.Stunde;
            if (dt == 0 && ds == 0) return;
            var idx = D.Stundenplan.IndexOf(prim);
            // eine Stunde bzw. ein ganzer Block eines Unterrichts: geprüfter Tausch mit dem Unterricht am Ziel (wie im Vorbild)
            var erzwingen = false;
            var tauschGrund = "";
            if (alle.All(x => x.UnterrichtId == prim.UnterrichtId && x.Tag == prim.Tag))
            {
                var start = alle.Min(x => x.Stunde) + ds;
                var tausch = Planungsdienst.TauschPruefen(D, Ix, alle, tag, start, out tauschGrund);
                if (tausch != null)
                {
                    var wahl = PlanungsKonfliktDialog.Fragen(this, $"Stunden tauschen – Unterricht {prim.UnterrichtId} {Ix.UnterrichtText(Ix.Unterrichte[prim.UnterrichtId])}",
                        tausch.Beschreibung + (tausch.Partner.Count > tausch.Laenge ? $"\n(mit {tausch.Partner.Count} parallelen Stunden des Bandes)" : ""), "Stunden tauschen", "Kollision erzeugen");
                    if (wahl is null) return;
                    if (wahl == 0)
                    {
                        var idxs = alle.Select(x => D.Stundenplan.IndexOf(x)).Where(i => i >= 0).ToList();
                        Aendern(tausch.Laenge > 1 ? $"Block tauschen ({tausch.Laenge} Stunden)" : "Stunden tauschen", AenderungsArt.Stundenplan, d =>
                        {
                            var t = Planungsdienst.TauschPruefen(d, new DatenIndex(d), idxs.Select(i => d.Stundenplan[i]).ToList(), tag, start, out var g);
                            if (t is null) throw new InvalidOperationException(g);
                            Planungsdienst.Tauschen(d, t);
                        });
                        _plan.Ausgewaehlt = D.Stundenplan.ElementAtOrDefault(idx);
                        Neuaufbau(false);
                        return;
                    }
                    erzwingen = true;
                }
            }
            if (alle.Count > 1) { MehrereVerschieben(alle, dt, ds, erzwingen); return; }
            var p = prim;
            var u = Ix.Unterrichte[p.UnterrichtId];
            if (p.Fixiert && !Ui.Frage("Die Stunde ist fixiert. Trotzdem verschieben?")) return;
            // „Soll behindernder Unterricht entplant werden?“ (Lehrer, Klasse bzw. fester Raum belegt)
            var behindernd = erzwingen ? new List<Hindernis>() : Behinderung.AnStellen(D, Ix, u, new[] { (tag, stunde) }, new[] { p });
            var weg = new List<int>();
            if (behindernd.Count > 0)
            {
                var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, behindernd, $"Stunde verschieben – U-Nr. {u.Id} {Ix.UnterrichtText(u)} nach {Planungsdienst.Tagname(D, tag)}-{stunde}" +
                    (tauschGrund.Length > 0 ? $"\n(kein Tausch möglich: {tauschGrund})" : ""));
                if (antwort == BehinderungsAntwort.Abbrechen) return;
                if (antwort == BehinderungsAntwort.Entplanen) weg = behindernd.Select(h => D.Stundenplan.IndexOf(h.Planstunde)).Where(i => i >= 0).ToList();
            }
            Aendern(weg.Count > 0 ? $"Stunde verschieben ({weg.Count} behindernde entplant)" : "Stunde verschieben", AenderungsArt.Stundenplan, d =>
            {
                var stundeRef = d.Stundenplan[idx];
                var entfernen = weg.Select(i => d.Stundenplan[i]).ToList();
                foreach (var x in entfernen) d.Stundenplan.Remove(x);
                Planungsdienst.Verschieben(d, stundeRef, tag, stunde, false, false);
            });
            _plan.Ausgewaehlt = D.Stundenplan.FirstOrDefault(x => x.UnterrichtId == u.Id && x.Tag == tag && x.Stunde == stunde);
            Neuaufbau(false);
        }
        else if (daten.StartsWith("unterricht:"))
        {
            var (uids, gewuenscht) = UnterrichtDaten(daten);
            uids = uids.Where(i => Ix.Unterrichte.ContainsKey(i)).ToList();
            if (uids.Count == 0) return;
            var unts = uids.Select(i => Ix.Unterrichte[i]).Where(u => Ix.NichtVerplant(u) > 0).ToList();
            if (unts.Count == 0) { Ui.Info("Alle Stunden dieses Unterrichts sind bereits verplant."); return; }
            // Einzelner Unterricht: ganzen Block (Doppelstunde, 3er-, 4er-Block) verplanen – bzw. den gezogenen Block aus „nicht verplant“
            var laenge = unts.Count == 1 ? Math.Min(Ix.NichtVerplant(unts[0]), Math.Max(1, gewuenscht ?? NaechsterBlock(unts[0]))) : 1;
            if (stunde + laenge - 1 > D.Zeitraster.StundenProTag) laenge = 1;
            var stellen = Enumerable.Range(stunde, laenge).Select(s => (tag, s)).ToList();
            var behindernd = unts.SelectMany(u => Behinderung.AnStellen(D, Ix, u, stellen)).Where(h => !uids.Contains(h.Unterricht.Id))
                .DistinctBy(h => D.Stundenplan.IndexOf(h.Planstunde)).ToList();
            var weg = new List<int>();
            if (behindernd.Count > 0)
            {
                var anlass = unts.Count > 1 ? $"{unts.Count} Unterrichte gleichzeitig in {Planungsdienst.Tagname(D, tag)}-{stunde} verplanen"
                    : $"{(laenge > 1 ? $"{laenge}er-Block" : "Stunde")} verplanen – U-Nr. {unts[0].Id} {Ix.UnterrichtText(unts[0])} in {Planungsdienst.Tagname(D, tag)}-{stunde}";
                var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, behindernd, anlass);
                if (antwort == BehinderungsAntwort.Abbrechen) return;
                if (antwort == BehinderungsAntwort.Entplanen) weg = behindernd.Select(h => D.Stundenplan.IndexOf(h.Planstunde)).Where(i => i >= 0).ToList();
            }
            Planstunde? neu = null;
            Aendern(laenge > 1 ? $"{laenge}er-Block verplanen" : unts.Count > 1 ? "Stunden gleichzeitig verplanen" : "Stunde verplanen", AenderungsArt.Stundenplan, d =>
            {
                var entfernen = weg.Select(i => d.Stundenplan[i]).ToList();
                foreach (var x in entfernen) d.Stundenplan.Remove(x);
                foreach (var u in unts.Select(x => d.Unterrichte.First(y => y.Id == x.Id)))
                    for (var k = 0; k < laenge; k++)
                    {
                        // nicht „neu ??= Verplanen(…)“: das würde nach der ersten Stunde nichts mehr verplanen
                        var p = Planungsdienst.Verplanen(d, u, tag, stunde + k, false);
                        neu ??= p;
                    }
            });
            _plan.Ausgewaehlt = neu;
            Neuaufbau(false);
        }
    }

    /// <summary>Länge des nächsten noch nicht verplanten Blocks (Blöcke und Doppelstunden laut Unterricht).</summary>
    private int NaechsterBlock(Unterricht u) => Planungsdienst.OffeneBloecke(D, Ix, u).FirstOrDefault(1);

    /// <summary>Gezogene Unterrichte („unterricht:12,13“) und ggf. die Blocklänge des gezogenen Blocks („unterricht:12|2“).</summary>
    internal static (List<int> Ids, int? Laenge) UnterrichtDaten(string daten)
    {
        var teile = daten[11..].Split('|');
        var ids = teile[0].Split(',').Select(x => int.TryParse(x, out var i) ? i : -1).Where(i => i >= 0).ToList();
        int? laenge = teile.Length > 1 && int.TryParse(teile[1], out var l) && l > 0 ? l : null;
        return (ids, laenge);
    }

    /// <summary>Mehrere markierte Stunden (oder einen Block) gemeinsam um denselben Abstand verschieben.</summary>
    private void MehrereVerschieben(List<Planstunde> alle, int dt, int ds, bool kollisionErzeugen = false)
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
        if (konflikte.Count > 0 && !kollisionErzeugen)
        {
            var hind = alle.SelectMany(p => Planungsdienst.Hindernisse(D, Ix, Ix.Unterrichte[p.UnterrichtId], p.Tag + dt, p.Stunde + ds, alle)).DistinctBy(h => D.Stundenplan.IndexOf(h.Planstunde)).ToList();
            var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, hind, $"{alle.Count} Stunden verschieben");
            if (antwort == BehinderungsAntwort.Abbrechen) return;
            entplanen = antwort == BehinderungsAntwort.Entplanen;
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

    /// <summary>
    /// Tagesaktueller Plan: Verlegung, Tausch oder Vormerkung verplanen. Auf einer Stunde mit Tauschmöglichkeit fragt
    /// „Was wollen Sie … machen?“ (tauschen / nur dorthin verlegen), bei Kollision „Kollision in Zielstunde“
    /// (nicht verplanen / verdrängen (vormerken) / Kollision erzeugen).
    /// </summary>
    private void TagesAbgelegt(string daten, int tag, int stunde)
    {
        if (_plan.DatumVon(tag) is not DateOnly ziel) return;
        if (daten.StartsWith("vormerkung:") && int.TryParse(daten[11..], out var vid))
        {
            Aendern("Vormerkung verplanen", AenderungsArt.Vertretung, d => Tagesverlegung.VormerkungVerplanen(d, d.Vormerkungen.First(v => v.Id == vid), ziel, stunde, AppZustand.Benutzer));
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
        var a = Tagesverlegung.Analysieren(D, von, vonStunde, uid, zeile, laenge, ziel, stunde);
        if (a.Schritte.Count == 0) return;
        var tpVon = Tagesplan.Berechnen(D, von);
        if (a.Schritte.Any(x => tpVon.Stunde(x.VonStunde).Any(e => (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == uid &&
                (e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true))) && !Ui.Frage("Die Stunde ist fixiert. Trotzdem verlegen?")) return;
        var modus = VerlegeModus.Verlegen;
        if (a.TauschMoeglich)
        {
            var w = OptionenDialog.Fragen(this, "Frage", Tagesverlegung.TauschFrage(D, a), new[] { "mit dortiger Unterrichtsstunde tauschen", "nur dorthin verlegen" });
            if (w is null) return;
            if (w == 0) modus = VerlegeModus.Tauschen;
            // „nur dorthin verlegen“: einfache Verlegung, die dortige Stunde bleibt stehen (Kollisionsfrage nur bei weiterem Unterricht)
            else a = a.NurDorthin();
        }
        if (modus != VerlegeModus.Tauschen && a.Kollision)
        {
            var w = OptionenDialog.Fragen(this, "Frage", Tagesverlegung.KollisionsFrage(D, a), new[] { "Unterricht nicht verplanen.", "Behindernden Unterricht verdrängen (vormerken)", "Kollision erzeugen" }, 1);
            if (w is null or 0) return;
            modus = w == 1 ? VerlegeModus.Verdraengen : VerlegeModus.Kollision;
        }
        if (modus != VerlegeModus.Tauschen && a.LehrerAbwesend && !Ui.Frage("Ein beteiligter Lehrer ist zu diesem Zeitpunkt abwesend. Trotzdem verlegen? (Es entsteht eine offene Vertretung.)")) return;
        Aendern(modus switch
        {
            VerlegeModus.Tauschen => "Stundentausch",
            VerlegeModus.Verdraengen => "Verlegung mit Verdrängung",
            _ => laenge > 1 ? $"Block verlegen ({laenge} Stunden)" : "Stundenverlegung",
        }, AenderungsArt.Vertretung, d => Tagesverlegung.Ausfuehren(d, a, modus, AppZustand.Benutzer));
        Neuaufbau(false);
    }

    /// <summary>Stundenplanlupe im tagesaktuellen Plan (Stunde hierher ziehen = Entfall).</summary>
    private void TagesLupe(TagesEintrag? e)
    {
        LupeSpalten(true);
        _lupe.Rows.Clear();
        if (e is null) return;
        var v = e.Vertretung;
        var u = e.Unterricht;
        var lehrer = string.Join(",", e.LehrerIds.Select(Ix.KLehrer)) + (v?.LehrerId is int l && !e.LehrerIds.Contains(l) ? $" ({Ix.KLehrer(l)})" : "");
        var lfr = string.Join(", ", new[] { lehrer, Ix.KFach(e.FachId), string.Join(",", e.RaumIds.Select(Ix.KRaum)) }.Where(x => x.Length > 0));
        var zeit = D.Zeitraster.Zeit(e.Stunde) is { } z ? $"{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}" : "";
        var von = u?.VonDatum ?? D.Einstellungen.SchuljahrVon;
        var bis = u?.BisDatum ?? D.Einstellungen.SchuljahrBis;
        var wochen = $"{Kalender.Schulwoche(D, von)}-{Kalender.Schulwoche(D, bis)}";
        var studt = D.Klassen.Where(k => e.KlassenIds.Contains(k.Id)).Sum(k => k.Schuelerzahl);
        var sondertext = v is null ? u?.Text ?? "" : $"{Core.Vertretungsplanung.Vertretungsplan.ArtText(v.Art)} {v.Text}".Trim();
        var zeile = e.Regel?.ZeilenIndex ?? v?.ZeilenIndex ?? 0;
        var zeilenText = u?.Zeilen.ElementAtOrDefault(zeile)?.Zeilentext ?? "";
        // Vertretung/Änderung wie im Vorbild mit „*“ hinter der Nummer
        var nr = u is null ? (v != null ? "V" + v.Id : "") : u.Id + (v != null ? "*" : "");
        var r = _lupe.Rows.Add(nr, lfr, Ix.KKlassen(e.KlassenIds), zeit, wochen, studt > 0 ? studt.ToString() : "", sondertext, u?.Band ?? "", zeilenText, u?.Schuelergruppe ?? "");
        // gewählte Stunde orange wie im Vorbild
        _lupe.Rows[r].DefaultCellStyle.BackColor = Ui.ZelleAktivVertretung;
        _lupe.Rows[r].DefaultCellStyle.SelectionBackColor = Ui.ZelleAktivVertretung;
    }

    /// <summary>Entfall eines Sondereinsatzes: Rückfrage, ob er gelöscht werden soll (null = kein Sondereinsatz betroffen).</summary>
    internal static bool? SondereinsatzLoeschenFragen(IEnumerable<Vertretung> sondereinsaetze) =>
        sondereinsaetze.Any() ? Ui.Frage(Vertretungen.SondereinsatzLoeschenFrage, "UntPlan - Frage") : null;

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

    /// <summary>Einträge der Stunde für „Stunde ändern“: im Klassenplan alle Kopplungszeilen, im Lehrerplan nur die des Lehrers.</summary>
    private List<TagesEintrag> StundenEintraege(DateOnly datum, int stunde, TagesEintrag e)
    {
        var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (uid is null || _typ == ElementTyp.Lehrer) return new() { e };
        return Tagesplan.Berechnen(D, datum).Stunde(stunde).Where(x => x.Findetstatt && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == uid)
            .GroupBy(x => x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex ?? 0).Select(g => g.First()).ToList();
    }

    /// <summary>Kontextmenü des tagesaktuellen Stundenplans (wie im Original).</summary>
    private void TagesKontextmenue(Point pt, DateOnly datum, int stunde, TagesEintrag? e) => TagesMenue(datum, stunde, e).Show(_plan, pt);

    /// <summary>Selbsttest: Kontextmenü des tagesaktuellen Plans für eine Stunde aufbauen (ohne es anzuzeigen).</summary>
    internal ContextMenuStrip SelbsttestTagesMenue(DateOnly datum, int stunde, TagesEintrag? e) => TagesMenue(datum, stunde, e);

    private ContextMenuStrip TagesMenue(DateOnly datum, int stunde, TagesEintrag? e)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var uid = e?.Unterricht?.Id ?? e?.Vertretung?.UnterrichtId;
        var zeile = e?.Regel?.ZeilenIndex ?? e?.Vertretung?.ZeilenIndex ?? 0;
        var findet = e?.Findetstatt == true;
        cm.Items.Add("Einstellungen", null, (_, _) => Einstellungen());
        var stpl = cm.Items.Add("Stundenplan-Stunde", null, (_, _) =>
        {
            if (uid is int u && Ix.Unterrichte.ContainsKey(u)) UnterrichtFenster.Zeigen(_typ is ElementTyp.Raum or ElementTyp.Fach ? ElementTyp.Unterricht : _typ, _typ is ElementTyp.Raum or ElementTyp.Fach ? null : _id);
        });
        stpl.Enabled = uid is int;
        var raum = cm.Items.Add("Raum zuordnen / löschen", Symbole.Get("raumzuordnen"), (_, _) =>
        {
            if (RaumZuordnenVertretungDialog.Zeigen(this, datum, stunde, uid, uid.HasValue ? zeile : null, e?.Vertretung)) Neuaufbau(false);
        });
        raum.Enabled = findet;
        cm.Items.Add("Sondereinsatz / Stunde ändern", Symbole.Get("sondereinsatz"), (_, _) =>
        {
            if (findet && e != null && uid is int) { if (StundeAendernDialog.Zeigen(this, datum, stunde, StundenEintraege(datum, stunde, e))) Neuaufbau(false); }
            else SondereinsatzDialog.Neu(this, datum, stunde, _typ == ElementTyp.Klasse ? new[] { _id } : Array.Empty<int>(), _typ == ElementTyp.Lehrer ? new[] { _id } : Array.Empty<int>());
        });
        var entfall = cm.Items.Add("Stunde entfallen lassen", Symbole.Get("entfall"), (_, _) =>
        {
            if (uid is int uu) StundeEntfallenLassen(datum, stunde, uu, zeile);
        });
        entfall.Enabled = findet && uid is int;
        // Entfall rückgängig: angeklickte entfallene Stunde bzw. alle entfallenen Stunden der Zelle (auch Kopplungszeilen und
        // Entfälle neben einer stattfindenden Stunde); bei Doppelstunden/Blöcken wahlweise der ganze Block
        var kandidaten = EntfallKandidaten(datum, stunde, e);
        foreach (var k in kandidaten)
        {
            var name = kandidaten.Count > 1 ? $"Entfall rückgängig: {EntfallBeschreibung(k.Eintrag)}" : "Entfall rückgängig";
            var hinweis = EntfallWirkungText(datum, stunde, k.Unterricht, k.Zeile);
            var block = Entfallaufhebung.Block(D, k.Unterricht, k.Zeile, datum, stunde);
            if (block.Count > 1)
            {
                var b = (ToolStripMenuItem)cm.Items.Add($"{name} (Block {block[0]}.–{block[^1]}. Stunde)", Symbole.Get("rueckgaengig"), (_, _) => EntfallAufheben(datum, stunde, k.Unterricht, k.Zeile, true));
                b.ToolTipText = hinweis;
                var einzeln = (ToolStripMenuItem)cm.Items.Add($"{name} (nur {stunde}. Stunde)", null, (_, _) => EntfallAufheben(datum, stunde, k.Unterricht, k.Zeile, false));
                einzeln.ToolTipText = hinweis;
            }
            else
            {
                var i = (ToolStripMenuItem)cm.Items.Add(name, Symbole.Get("rueckgaengig"), (_, _) => EntfallAufheben(datum, stunde, k.Unterricht, k.Zeile, false));
                i.ToolTipText = hinweis;
            }
        }
        cm.Items.Add("Klausur erstellen", Symbole.Get("klausur"), (_, _) => KlausurDialog.Neu(this, datum, stunde, e));
        var std = new ToolStripMenuItem("Stpl-Stunde: Standardformat") { Checked = _plan.Format.Standardformat };
        std.Click += (_, _) => { var f = Kopie(_plan.Format); f.Standardformat = !f.Standardformat; FormatSpeichern(f, f.Name); };
        cm.Items.Add(std);
        var text = cm.Items.Add("Text", null, (_, _) => TextZurVertretung(datum, stunde, e!));
        text.Enabled = findet && uid is int;
        var schueler = uid is int us ? D.Schueler.Where(x => x.KurswahlIds.Contains(us)).ToList() : new List<Schueler>();
        var stud = cm.Items.Add("Studenten", null, (_, _) => Ui.Info(string.Join("\n", schueler.Select(x => $"{x.Langname}, {x.Vorname}")), $"Studenten – U-Nr. {uid}"));
        stud.Enabled = schueler.Count > 0;
        cm.Items.Add(ZeitbereichMenue());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Im HTML-Format kopieren", Symbole.Get("html"), (_, _) => HtmlKopieren());
        // weitere Funktionen des Vertretungsmodus
        cm.Items.Add(new ToolStripSeparator());
        if (findet && uid is int uv)
            cm.Items.Add("Neben den Plan legen (Vormerkung)", Symbole.Get("vormerkung"), (_, _) =>
            {
                Aendern("Vormerkung", AenderungsArt.Vertretung, d => Tagesverlegung.Vormerken(d, datum, stunde, uv, _typ == ElementTyp.Lehrer ? zeile : null, VormerkungsQuelle.Stundenplan, AppZustand.Benutzer));
                Neuaufbau(false);
            });
        if (e?.Vertretung != null)
        {
            cm.Items.Add("Vertretungsvorschlag", Symbole.Get("vertretung"), (_, _) => { AppZustand.VertretungWaehlen(e.Vertretung); HauptFenster.Oeffnen("vorschlag", () => new VertretungsvorschlagFenster()); });
            cm.Items.Add("Änderungen rückgängig", Symbole.Get("rueckgaengig"), (_, _) => AenderungenRueckgaengig());
        }
        if (cm.Items[^1] is ToolStripSeparator) cm.Items.RemoveAt(cm.Items.Count - 1);
        return cm;
    }

    /// <summary>„Stunde entfallen lassen“ (Kontextmenü, Taste „-“).</summary>
    private void StundeEntfallenLassen(DateOnly datum, int stunde, int uid, int zeile)
    {
        if (VerlegungsAbfrage.VorEntfall(datum, stunde, uid, Aendern)) { Neuaufbau(false); return; }
        var zi = _typ == ElementTyp.Lehrer ? zeile : (int?)null;
        var seLoeschen = SondereinsatzLoeschenFragen(Vertretungen.SondereinsaetzeBeiEntfall(D, uid, zi, datum, stunde));
        Aendern("Entfall", AenderungsArt.Vertretung, d => Vertretungen.Entfallen(d, uid, zi, datum, stunde, AppZustand.Benutzer, seLoeschen));
        Neuaufbau(false);
    }

    /// <summary>
    /// Entfallene Stunden einer Zelle, die sich wiederherstellen lassen: zuerst die angeklickte, dann weitere Entfälle der Zelle.
    /// Im Lehrerplan je Kopplungszeile der Lehrkraft, sonst je Unterricht (alle entfallenen Kopplungszeilen).
    /// </summary>
    internal List<(int Unterricht, int? Zeile, TagesEintrag Eintrag)> EntfallKandidaten(DateOnly datum, int stunde, TagesEintrag? e)
    {
        var res = new List<(int Unterricht, int? Zeile, TagesEintrag Eintrag)>();
        void Pruefen(TagesEintrag x)
        {
            if ((x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) is not int u) return;
            int? zi = _typ == ElementTyp.Lehrer ? x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex ?? 0 : null;
            if (res.Any(r => r.Unterricht == u && r.Zeile == zi)) return;
            if (Entfallaufhebung.Moeglich(D, u, zi, datum, stunde)) res.Add((u, zi, x));
        }
        if (e != null) Pruefen(e);
        foreach (var x in _plan.EintraegeAmDatum(datum, stunde).Where(x => x.Status == TagesStatus.Entfall)) Pruefen(x);
        return res;
    }

    private string EntfallBeschreibung(TagesEintrag e) =>
        string.Join(" ", new[] { Ix.KFach(e.FachId), string.Join(",", (e.Regel?.LehrerId is int l ? new[] { l } : e.LehrerIds.ToArray()).Select(Ix.KLehrer)), Ix.KKlassen(e.KlassenIds) }.Where(t => t.Length > 0));

    private string EntfallWirkungText(DateOnly datum, int stunde, int uid, int? zeile)
    {
        var zeilen = Entfallaufhebung.Zeilen(D, uid, zeile, datum, stunde);
        var offen = zeilen.Count(v => Entfallaufhebung.WirkungVon(D, v) == Entfallaufhebung.Wirkung.Offen);
        return offen == 0 ? "Die Stunde findet wieder regulär statt."
            : offen == zeilen.Count ? "Die Lehrkraft ist abwesend: Die Stunde wird zur offenen Vertretung (???)."
            : "Teilweise: Stunden anwesender Lehrkräfte finden wieder statt, die übrigen werden offene Vertretungen (???).";
    }

    /// <summary>Entfall aufheben (Kontextmenü, Doppelklick, Taste „-“): wieder regulärer Unterricht bzw. offene Vertretung.</summary>
    internal int EntfallAufheben(DateOnly datum, int stunde, int uid, int? zeile, bool block)
    {
        var stunden = block ? Entfallaufhebung.Block(D, uid, zeile, datum, stunde) : new List<int> { stunde };
        if (stunden.Count == 0) return 0;
        var hinweis = EntfallWirkungText(datum, stunde, uid, zeile);
        var n = 0;
        if (!Aendern(stunden.Count > 1 ? $"Entfall aufgehoben ({stunden.Count} Stunden)" : "Entfall aufgehoben", AenderungsArt.Vertretung,
                d => { n = 0; foreach (var s in stunden) n += Entfallaufhebung.Aufheben(d, uid, zeile, datum, s, AppZustand.Benutzer); })) return 0;
        Neuaufbau(false);
        _plan.TagesAuswaehlen(datum, stunde, uid);
        AppZustand.Haupt?.StatusMelden($"Entfall aufgehoben ({Format.Datum(datum)}, {stunden[0]}.{(stunden.Count > 1 ? $"–{stunden[^1]}." : "")} Stunde): {hinweis}");
        return n;
    }

    /// <summary>Doppelklick bzw. Taste „-“ auf eine entfallene Stunde: Entfall des ganzen Blocks aufheben.</summary>
    private bool EntfallDirektAufheben(DateOnly datum, TagesEintrag e)
    {
        if (e.Status != TagesStatus.Entfall) return false;
        var k = EntfallKandidaten(datum, e.Stunde, e).FirstOrDefault(x => x.Eintrag == e);
        if (k.Eintrag is null) return false;
        EntfallAufheben(datum, e.Stunde, k.Unterricht, k.Zeile, true);
        return true;
    }

    private void TextZurVertretung(DateOnly datum, int stunde, TagesEintrag e)
    {
        var uu = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
        if (uu is null) return;
        var zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
        var text = Ui.Eingabe("Text zur Stunde (wird im Stundenplan und im Vertretungsplan ausgegeben):", "Text", e.Vertretung?.Text ?? "");
        if (text is null) return;
        Aendern("Text zur Vertretung", AenderungsArt.Vertretung, d =>
        {
            var v = e.Vertretung is { } x ? d.Vertretungen.First(y => y.Id == x.Id) : null;
            if (v is null)
            {
                v = new Vertretung
                {
                    Id = d.NeueId(), Datum = datum, Stunde = stunde, UnterrichtId = uu, ZeilenIndex = zeile, LehrerId = e.Regel?.LehrerId, VertreterId = e.Regel?.LehrerId,
                    FachId = e.FachId, KlassenIds = e.KlassenIds.ToList(), RaumId = e.Regel?.RaumId, VertretungsraumId = e.Regel?.RaumId, Art = VertretungsArt.UnterrichtGeaendert,
                    Quelle = VertretungsQuelle.Manuell, Bearbeitet = true,
                };
                d.Vertretungen.Add(v);
            }
            v.Text = text;
        });
        Neuaufbau(false);
    }

    /// <summary>Stundenplan im HTML-Format in die Zwischenablage (CF_HTML) kopieren.</summary>
    private void HtmlKopieren()
    {
        var html = Export.StundenplanHtml(D, _typ, _id);
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.Html, ClipboardHtml(html));
            data.SetData(DataFormats.Text, html);
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    private static string ClipboardHtml(string fragment)
    {
        const string kopf = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var vor = "<html><body><!--StartFragment-->";
        var nach = "<!--EndFragment--></body></html>";
        var laenge = string.Format(kopf, 0, 0, 0, 0).Length;
        var enc = System.Text.Encoding.UTF8;
        var startHtml = laenge;
        var startFrag = startHtml + enc.GetByteCount(vor);
        var endFrag = startFrag + enc.GetByteCount(fragment);
        var endHtml = endFrag + enc.GetByteCount(nach);
        return string.Format(kopf, startHtml, endHtml, startFrag, endFrag) + vor + fragment + nach;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // tagesaktueller Plan: „-“ wie in der Vertretungsliste – Stunde entfallen lassen bzw. Entfall wieder aufheben
        if (_plan.Tagesmodus && ActiveControl is not (TextBoxBase or ComboBox or ToolStrip or DataGridView) && e.Modifiers == Keys.None &&
            e.KeyCode is Keys.OemMinus or Keys.Subtract && _plan.TagesAuswahl is { } ta)
        {
            if (!EntfallDirektAufheben(ta.Datum, ta.Eintrag) && ta.Eintrag.Findetstatt && (ta.Eintrag.Unterricht?.Id ?? ta.Eintrag.Vertretung?.UnterrichtId) is int u)
                StundeEntfallenLassen(ta.Datum, ta.Eintrag.Stunde, u, ta.Eintrag.Regel?.ZeilenIndex ?? ta.Eintrag.Vertretung?.ZeilenIndex ?? 0);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        // Entf = entplanen, F = fixieren (nicht während einer Texteingabe)
        if (ActiveControl is not (TextBoxBase or ComboBox or ToolStrip or DataGridView) && e.Modifiers == Keys.None && !RegelplanNurAnzeige)
        {
            if (e.KeyCode == Keys.Delete) { Entplanen(); e.Handled = true; }
            else if (e.KeyCode == Keys.F) { Fixieren(); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    private List<Planstunde> Markierte() => _plan.Mehrfach.Count > 0 ? _plan.Mehrfach.ToList() : _plan.Ausgewaehlt is { } p ? new() { p } : new();

    private void Fixieren()
    {
        if (RegelplanNurAnzeige) return;
        var m = Markierte();
        if (m.Count == 0) return;
        var idxs = m.Select(p => D.Stundenplan.IndexOf(p)).Where(i => i >= 0).ToList();
        var ziel = !m[0].Fixiert;
        Aendern(ziel ? "Stunden fixieren" : "Fixierung lösen", AenderungsArt.Stundenplan, d => { foreach (var i in idxs) d.Stundenplan[i].Fixiert = ziel; });
        Neuaufbau(false);
    }

    private void Entplanen()
    {
        if (RegelplanNurAnzeige) return;
        var m = Markierte();
        if (m.Count == 0) return;
        if (m.Any(p => p.Fixiert) && !Ui.Frage("Fixierte Stunden entplanen?")) return;
        Aendern(m.Count > 1 ? "Stunden entplanen" : "Stunde entplanen", AenderungsArt.Stundenplan, d => { foreach (var p in m) Planungsdienst.Entplanen(d, p); });
        _plan.AuswahlLeeren();
        Neuaufbau(false);
    }

    public void RaumZuordnen()
    {
        if (_plan.Tagesmodus)
        {
            // Vertretungsmodus: Raum der gewählten Stunde an diesem Tag ändern (Raumvertretung)
            if (_plan.TagesAuswahl is not { } ta || !ta.Eintrag.Findetstatt) { Ui.Info("Bitte zuerst eine Stunde im Stundenplan anklicken."); return; }
            var e = ta.Eintrag;
            var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
            if (RaumZuordnenVertretungDialog.Zeigen(this, ta.Datum, e.Stunde, uid, uid.HasValue ? e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex : null, e.Vertretung)) Neuaufbau(false);
            return;
        }
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
        std.Click += (_, _) => { var f = Kopie(_plan.Format); f.Standardformat = !f.Standardformat; FormatSpeichern(f, f.Name); };
        cm.Items.Add(std);
        cm.Items.Add(ZeitbereichMenue());
        cm.Items.Add(new ToolStripSeparator());
        if (_plan.Tagesmodus) return;
        // Vertretungsmodus mit Zeitbereich „Periode“: der Regelplan wird nur angezeigt, nicht verändert
        if (RegelplanNurAnzeige)
        {
            cm.Items.RemoveAt(cm.Items.Count - 1);
            cm.Show(_plan, pt);
            return;
        }
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
        var alt = _plan.Format.Name;
        var f = StundenplanEinstellungenDialog.Zeigen(this, Kopie(_plan.Format), _typ, _id, sofort: neu => { FormatSpeichern(Kopie(neu), alt); alt = neu.Name; });
        if (f != null) FormatSpeichern(f, alt);
    }

    /// <summary>Format speichern; ersetzt das Format gleichen (bzw. bisherigen) Namens dieser Planart.</summary>
    private void FormatSpeichern(StundenplanFormat f, string alterName)
    {
        Aendern("Stundenplan-Format", AenderungsArt.Einstellungen, d =>
        {
            d.Formate.RemoveAll(x => x.Typ == f.Typ && (x.Name == alterName || x.Name == f.Name || (x.Name.Length == 0 && x.Gross == f.Gross)));
            d.Formate.Add(f);
        });
        _formatName = f.Name;
        AppZustand.Einstellungen.Stundenplanformate[FormatSchluessel] = f.Name;
        _plan.Format = f;
        if (f.Klein != _klein)
        {
            // „Kleiner Stundenplan“ im Format umgeschaltet: Fenster passt sich an
            _klein = f.Klein;
            AppZustand.Einstellungen.Stundenplanformate[FormatSchluessel] = f.Name;
            KleinAnwenden();
        }
        FormateFuellen();
        _planGroesse?.Invoke();
        Neuaufbau(false);
        foreach (var w in AppZustand.Haupt.MdiChildren.OfType<StundenplanFenster>().Where(w => w != this && w._typ == f.Typ && (w._plan.Format.Name == alterName || w._plan.Format.Name == f.Name)))
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
