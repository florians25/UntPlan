using UntPlan.App.Druck;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Element-Rollup wie im Vorbild: Fenstertitel = Elementart (z. B. „Raum“), links eine senkrechte Leiste mit dem Filter und den
/// Elementarten, darüber die Filterzeile (z. B. „R“ für alle Räume, die mit R beginnen), Spalten je Art (Lehrer: Name, Nachname,
/// Text, Beschr.; Raum: Name, Langname, Ausweichraum, Raumgew.), weitere Spalten über das Kontextmenü der rechten Maustaste.
/// Mehrere Elemente mit Umschalt- bzw. Strg-Klick markieren und mit gedrückter linker Maustaste in ein Feld ziehen (z. B. Klassen in
/// die Spalte „Klasse“ der Veranstaltungen). Gezogen wird der Text „elemente:Typ:Kurz1,Kurz2,…“.
/// Handbuch 7.5.1: Eintragen auch per Doppelklick (in das markierte Feld des zuletzt aktiven Fensters, Einstellungen | Anpassen
/// „Doppelklick aktivieren“) und Andocken an eine der vier Kanten des Hauptfensters: beim Verschieben erscheinen an den Rändern und in
/// der Mitte Symbole – wird das Rollup darüber losgelassen, dockt es an der Kante an (Doppelklick auf die Titelleiste löst es wieder).
/// </summary>
public sealed class ElementRollup : Form
{
    /// <summary>Andockbereich im Hauptfenster (Leiste mit Titel und dem Rollup-Inhalt), null = frei schwebend.</summary>
    private static Panel? _dock;
    private Andockhilfe? _hilfe;
    private static ElementRollup? _instanz;
    private static readonly (string Text, ElementTyp Typ, string Symbol)[] Typen =
    {
        ("Klasse", ElementTyp.Klasse, "klassen"), ("Lehrer", ElementTyp.Lehrer, "lehrer"), ("Raum", ElementTyp.Raum, "raeume"), ("Fach", ElementTyp.Fach, "faecher"),
        ("Raumgruppe", ElementTyp.Raumgruppe, "raeume"),
    };
    /// <summary>Alle möglichen Spalten: Schlüssel, Überschrift, Breite, Standard je Art.</summary>
    private static readonly (string Name, string Text, int Breite)[] Spalten =
    {
        ("N", "Name", 50), ("L", "Langname", 120), ("T", "Text", 80), ("B", "Beschr.", 70), ("R", "Raum", 50), ("A", "Ausweichraum", 80), ("G", "Raumgew.", 60), ("K", "Kap.", 40),
    };
    private static readonly Dictionary<ElementTyp, HashSet<string>> Sichtbar = new()
    {
        [ElementTyp.Klasse] = new() { "N", "L", "R" },
        [ElementTyp.Lehrer] = new() { "N", "L", "T", "B" },
        [ElementTyp.Raum] = new() { "N", "L", "A", "G" },
        [ElementTyp.Fach] = new() { "N", "L", "R" },
        [ElementTyp.Raumgruppe] = new() { "N", "L" },
    };
    private readonly ToolStrip _arten = new() { Dock = DockStyle.Left, LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow, GripStyle = ToolStripGripStyle.Hidden, AutoSize = false, Width = 26, Padding = new Padding(1) };
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, BackColor = Color.FromArgb(255, 255, 200), Visible = false, PlaceholderText = "Filter" };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private int _typIndex;
    private Point _start;

    public static void Zeigen() => Zeigen(null);

    /// <summary>Rollup zeigen und auf den Elementtyp stellen.</summary>
    public static void Zeigen(ElementTyp? typ)
    {
        if (_instanz is null || _instanz.IsDisposed)
        {
            _instanz = new ElementRollup();
            if (Enum.TryParse<DockStyle>(AppZustand.Einstellungen.RollupAngedockt, out var kante) && kante is DockStyle.Left or DockStyle.Right or DockStyle.Top or DockStyle.Bottom)
                _instanz.Andocken(kante);
            else _instanz.Show(AppZustand.Haupt);
        }
        if (typ is ElementTyp t && Array.FindIndex(Typen, x => x.Typ == t) is var i and >= 0) _instanz.TypWaehlen(i);
        if (_instanz.TopLevel) _instanz.Activate();
    }

    /// <summary>Selbsttest: ist das Rollup angedockt (Kante) bzw. frei schwebend (None)?</summary>
    internal static DockStyle Kante => _dock is { IsDisposed: false } p ? p.Dock : DockStyle.None;

    /// <summary>Selbsttest/Hauptfenster: Rollup an einer Kante andocken bzw. lösen (<see cref="DockStyle.None"/>).</summary>
    internal static void KanteSetzen(DockStyle kante)
    {
        Zeigen(null);
        if (kante == DockStyle.None) _instanz!.Loesen();
        else _instanz!.Andocken(kante);
    }

    public static string? Text2Typ(string s, out ElementTyp typ, out List<string> namen)
    {
        typ = ElementTyp.Klasse;
        namen = new();
        if (!s.StartsWith("elemente:")) return null;
        var teile = s.Split(':', 3);
        if (teile.Length < 3 || !Enum.TryParse(teile[1], out typ)) return null;
        namen = teile[2].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        return teile[2];
    }

    private ElementRollup()
    {
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Size = new Size(320, 380);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Math.Max(0, AppZustand.Haupt.Right - 380), AppZustand.Haupt.Top + 160);
        var filterKnopf = new ToolStripButton(Symbole.Get("filter", 16)) { ToolTipText = "Filter", CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
        filterKnopf.CheckedChanged += (_, _) => { _filter.Visible = filterKnopf.Checked; if (!filterKnopf.Checked) _filter.Text = ""; else _filter.Focus(); };
        _arten.Items.Add(filterKnopf);
        for (var i = 0; i < Typen.Length; i++)
        {
            var n = i;
            _arten.Items.Add(new ToolStripButton(Symbole.Get(Typen[i].Symbol, 16)) { ToolTipText = Typen[i].Text, DisplayStyle = ToolStripItemDisplayStyle.Image, Tag = i }.Mit(b => b.Click += (_, _) => TypWaehlen(n)));
        }
        _filter.TextChanged += (_, _) => Fuellen();
        Ui.RasterGestalten(_g, false);
        _g.ReadOnly = true;
        _g.RowHeadersVisible = false;
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = true;
        foreach (var (n, t, w) in Spalten) _g.Columns[_g.Columns.Add(n, t)].Width = w;
        _g.MouseDown += (_, e) => _start = e.Location;
        // Eintragen per Doppelklick (Einstellungen | Anpassen | Element-Rollup „Doppelklick aktivieren“)
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && AppZustand.Einstellungen.RollupDoppelklick) Eintragen(); };
        // „Elemente aktualisieren“: Liste nach Änderungen der Stammdaten neu füllen
        void Geaendert(AenderungsArt a) { if (AppZustand.Einstellungen.RollupAktualisieren && a is AenderungsArt.Alles or AenderungsArt.Stammdaten && !IsDisposed) Fuellen(); }
        void Gewechselt() { AppZustand.Doc.Geaendert += Geaendert; if (!IsDisposed) Fuellen(); }
        AppZustand.Doc.Geaendert += Geaendert;
        AppZustand.DokumentGewechselt += Gewechselt;
        FormClosed += (_, _) => { AppZustand.Doc.Geaendert -= Geaendert; AppZustand.DokumentGewechselt -= Gewechselt; _hilfe?.Dispose(); };
        // Andocken: beim Verschieben die Andocksymbole zeigen, beim Loslassen über einem Symbol andocken
        Move += (_, _) => { if (TopLevel && Visible && MouseButtons == MouseButtons.Left) (_hilfe ??= new Andockhilfe()).Zeigen(Cursor.Position); };
        ResizeEnd += (_, _) =>
        {
            if (_hilfe is null) return;
            var kante = _hilfe.Treffer(Cursor.Position);
            _hilfe.Verbergen();
            if (kante != DockStyle.None) Andocken(kante);
        };
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (Math.Abs(e.X - _start.X) < 6 && Math.Abs(e.Y - _start.Y) < 6)) return;
            var namen = _g.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Cells[0].Value?.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (namen.Count == 0) return;
            _g.DoDragDrop($"elemente:{Typen[_typIndex].Typ}:{string.Join(",", namen)}", DragDropEffects.Copy);
        };
        // Vorbild: weitere Spalten über das Kontextmenü der rechten Maustaste
        _g.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) SpaltenMenue(e.Location); };
        Controls.Add(_g);
        Controls.Add(_filter);
        Controls.Add(_arten);
        TypWaehlen(0);
    }

    /// <summary>
    /// Doppelklick: die markierten Elemente in das markierte Feld des zuletzt aktiven Fensters eintragen (Tabellenzelle bzw. Textfeld;
    /// mehrere Elemente durch Komma getrennt). Fenster mit eigener Logik implementieren <see cref="IElementEintrag"/>.
    /// </summary>
    internal bool Eintragen()
    {
        var typ = Typen[_typIndex].Typ;
        var namen = _g.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Cells[0].Value?.ToString()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        if (namen.Count == 0) return false;
        var ziel = AppZustand.Haupt?.ActiveMdiChild;
        if (ziel is IElementEintrag ee && ee.ElementeEintragen(typ, namen)) return true;
        Control? c = ziel;
        while (c is ContainerControl { ActiveControl: { } a }) c = a;
        var text = string.Join(",", namen);
        switch (c)
        {
            case TextBox { ReadOnly: false } tb:
                tb.SelectedText = tb.Text.Length > 0 && tb.SelectionStart > 0 && tb.Text[tb.SelectionStart - 1] != ',' ? "," + text : text;
                return true;
            case DataGridView { CurrentCell: { ReadOnly: false } zelle } g when !g.ReadOnly && zelle is not DataGridViewCheckBoxCell:
                g.Focus();
                if (!g.BeginEdit(true)) return false;
                if (g.EditingControl is { } ec) ec.Text = text; else zelle.Value = text;
                g.EndEdit();
                return true;
        }
        Ui.Info("Markieren Sie zuerst im Zielfenster das Feld, in das die Elemente eingetragen werden sollen.", "Element-Rollup");
        return false;
    }

    /// <summary>An einer Kante des Hauptfensters andocken (Leiste mit Titel „Element-Rollup“, Doppelklick darauf löst das Rollup).</summary>
    private void Andocken(DockStyle kante)
    {
        var haupt = AppZustand.Haupt;
        if (haupt is null) return;
        var mdi = haupt.Controls.OfType<MdiClient>().FirstOrDefault();
        if (_dock is null || _dock.IsDisposed)
        {
            _dock = new Panel { BackColor = Ui.BlauSehrHell, Padding = new Padding(1), Name = "ElementRollupDock" };
            var titel = new Label { Dock = DockStyle.Top, Height = 20, Text = "  Element-Rollup", BackColor = Ui.Titel, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.SchriftFett };
            var zu = new Label { Dock = DockStyle.Right, Width = 20, Text = "×", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            titel.Controls.Add(zu);
            new ToolTip().SetToolTip(titel, "Doppelklick: Element-Rollup vom Rand lösen");
            titel.DoubleClick += (_, _) => Loesen();
            zu.Click += (_, _) => { Loesen(); Close(); };
            _dock.Controls.Add(titel);
        }
        if (TopLevel)
        {
            Hide();
            Owner = null;
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            _dock.Controls.Add(this);
            BringToFront();
        }
        _dock.Dock = kante;
        if (kante is DockStyle.Left or DockStyle.Right) _dock.Width = Math.Max(220, Width); else _dock.Height = Math.Max(160, Math.Min(260, Height));
        if (!haupt.Controls.Contains(_dock))
        {
            haupt.Controls.Add(_dock);
            // Andocken geschieht in umgekehrter Reihenfolge der Steuerelemente: direkt vor dem Arbeitsbereich (höherer Index) einreihen,
            // damit Menüband und Statuszeile ihren Platz behalten und der Arbeitsbereich den Rest füllt
            if (mdi != null) haupt.Controls.SetChildIndex(_dock, haupt.Controls.GetChildIndex(mdi) + 1);
        }
        Show();
        AppZustand.Einstellungen.RollupAngedockt = kante.ToString();
        AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Angedocktes Rollup wieder als frei schwebendes Fenster zeigen.</summary>
    private void Loesen()
    {
        if (TopLevel) return;
        var haupt = AppZustand.Haupt;
        var lage = _dock is { IsDisposed: false } p ? p.RectangleToScreen(p.ClientRectangle) : Bounds;
        _dock?.Controls.Remove(this);
        if (_dock != null) { haupt?.Controls.Remove(_dock); _dock.Dispose(); _dock = null; }
        Dock = DockStyle.None;
        TopLevel = true;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Size = new Size(320, 380);
        Location = new Point(Math.Max(0, lage.Left + 30), Math.Max(0, lage.Top + 30));
        if (haupt != null) Show(haupt); else Show();
        AppZustand.Einstellungen.RollupAngedockt = "";
        AppZustand.Einstellungen.Speichern();
    }

    /// <summary>
    /// Andocksymbole beim Verschieben des Rollups: an den vier Rändern des Hauptfensters und in der Mitte (Kreuz mit vier Pfeilen).
    /// </summary>
    private sealed class Andockhilfe : IDisposable
    {
        private readonly List<(Form Symbol, DockStyle Kante)> _symbole = new();

        public void Zeigen(Point maus)
        {
            var haupt = AppZustand.Haupt;
            if (haupt is null) return;
            var mdi = haupt.Controls.OfType<MdiClient>().FirstOrDefault();
            var r = mdi is null ? haupt.RectangleToScreen(haupt.ClientRectangle) : mdi.RectangleToScreen(mdi.ClientRectangle);
            if (_symbole.Count == 0)
            {
                const int s = 32;
                var mx = r.Left + r.Width / 2 - s / 2;
                var my = r.Top + r.Height / 2 - s / 2;
                foreach (var (kante, x, y) in new[]
                         {
                             (DockStyle.Left, r.Left + 8, my), (DockStyle.Right, r.Right - s - 8, my), (DockStyle.Top, mx, r.Top + 8), (DockStyle.Bottom, mx, r.Bottom - s - 8),
                             (DockStyle.Left, mx - s, my), (DockStyle.Right, mx + s, my), (DockStyle.Top, mx, my - s), (DockStyle.Bottom, mx, my + s),
                         })
                    _symbole.Add((Symbol(kante, new Rectangle(x, y, s, s)), kante));
            }
            foreach (var (f, _) in _symbole) if (!f.Visible) f.Show(haupt);
            foreach (var (f, _) in _symbole) f.BackColor = f.Bounds.Contains(maus) ? Color.FromArgb(255, 220, 120) : Color.White;
        }

        public DockStyle Treffer(Point maus) => _symbole.FirstOrDefault(x => x.Symbol.Visible && x.Symbol.Bounds.Contains(maus)).Kante;

        public void Verbergen()
        {
            foreach (var (f, _) in _symbole) f.Hide();
        }

        public void Dispose()
        {
            foreach (var (f, _) in _symbole) f.Dispose();
            _symbole.Clear();
        }

        private static Form Symbol(DockStyle kante, Rectangle lage)
        {
            var f = new SymbolForm { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = lage, TopMost = true, BackColor = Color.White };
            f.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var rahmen = new Pen(Ui.Blau, 2);
                g.DrawRectangle(rahmen, 1, 1, lage.Width - 3, lage.Height - 3);
                var m = lage.Width / 2f;
                PointF[] pfeil = kante switch
                {
                    DockStyle.Left => new[] { new PointF(8, m), new PointF(20, m - 7), new PointF(20, m + 7) },
                    DockStyle.Right => new[] { new PointF(lage.Width - 8, m), new PointF(lage.Width - 20, m - 7), new PointF(lage.Width - 20, m + 7) },
                    DockStyle.Top => new[] { new PointF(m, 8), new PointF(m - 7, 20), new PointF(m + 7, 20) },
                    _ => new[] { new PointF(m, lage.Height - 8), new PointF(m - 7, lage.Height - 20), new PointF(m + 7, lage.Height - 20) },
                };
                using var b = new SolidBrush(Ui.Blau);
                g.FillPolygon(b, pfeil);
            };
            return f;
        }

        /// <summary>Symbolfenster ohne Aktivierung (das gezogene Rollup bleibt aktiv).</summary>
        private sealed class SymbolForm : Form
        {
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                    return cp;
                }
            }
        }
    }

    private void TypWaehlen(int i)
    {
        _typIndex = Math.Clamp(i, 0, Typen.Length - 1);
        Text = Typen[_typIndex].Text;
        foreach (ToolStripItem it in _arten.Items) if (it is ToolStripButton b && b.Tag is int n) b.Checked = n == _typIndex;
        Fuellen();
    }

    private void SpaltenMenue(Point pt)
    {
        var typ = Typen[_typIndex].Typ;
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        foreach (var (n, t, _) in Spalten.Skip(1))
        {
            var name = n;
            var it = new ToolStripMenuItem(Ueberschrift(name, typ, t)) { Checked = Sichtbar[typ].Contains(n), CheckOnClick = true };
            it.Click += (_, _) => { if (it.Checked) Sichtbar[typ].Add(name); else Sichtbar[typ].Remove(name); Fuellen(); };
            cm.Items.Add(it);
        }
        cm.Show(_g, pt);
    }

    private static string Ueberschrift(string spalte, ElementTyp typ, string text) => spalte == "L" && typ == ElementTyp.Lehrer ? "Nachname" : text;

    private void Fuellen()
    {
        var typ = Typen[_typIndex].Typ;
        var ix = AppZustand.Doc.Index;
        _g.Rows.Clear();
        foreach (DataGridViewColumn c in _g.Columns)
        {
            c.Visible = c.Name == "N" || Sichtbar[typ].Contains(c.Name);
            c.HeaderText = Ueberschrift(c.Name, typ, Spalten.First(x => x.Name == c.Name).Text);
        }
        var filter = _filter.Text.Trim();
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ).Where(e => !e.Ignorieren))
        {
            if (filter.Length > 0 && !e.Kurzname.StartsWith(filter, StringComparison.CurrentCultureIgnoreCase)) continue;
            var raum = e switch { Klasse k => ix.KRaum(k.StammraumId), Lehrer l => ix.KRaum(l.StammraumId), Fach f => ix.KRaum(f.FachraumId), _ => "" };
            var r = e as Raum;
            _g.Rows.Add(e.Kurzname, e is Lehrer le ? le.Langname : e.Langname, e.Text, e.Beschreibung, raum,
                r is null ? "" : ix.KRaum(r.AusweichraumId), r is null ? "" : r.Raumgewicht, r is null || r.Kapazitaet == 0 ? "" : r.Kapazitaet);
        }
    }
}

/// <summary>Fenster, die Elemente aus dem Element-Rollup selbst eintragen (Doppelklick im Rollup).</summary>
public interface IElementEintrag
{
    /// <summary>Elemente eintragen; false = nicht möglich (dann wird in das markierte Feld eingetragen).</summary>
    bool ElementeEintragen(ElementTyp typ, List<string> namen);
}
