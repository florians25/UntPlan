using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>Auswahl beim Ablegen auf eine belegte Stunde: Tausch/behindernden Unterricht entplanen oder Kollision erzeugen.</summary>
public static class PlanungsKonfliktDialog
{
    public static int? Fragen(IWin32Window owner, string titel, string details, string option1, string option2)
    {
        using var d = new Dialog(titel, 460, 250);
        d.Controls.Add(new Label { Text = details, Left = 12, Top = 10, Width = 436, Height = 110, AutoSize = false });
        var grp = new GroupBox { Text = "Optionen", Left = 12, Top = 124, Width = 436, Height = 76 };
        var r1 = new RadioButton { Text = option1, Left = 12, Top = 20, Width = 410, Checked = true };
        var r2 = new RadioButton { Text = option2, Left = 12, Top = 44, Width = 410 };
        grp.Controls.Add(r1);
        grp.Controls.Add(r2);
        d.Controls.Add(grp);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return r1.Checked ? 0 : 1;
    }
}

/// <summary>Raumzuordnungsdialog: links die Räume laut Unterricht, rechts mögliche (freie bzw. auch besetzte) Räume.</summary>
public static class RaumZuordnenDialog
{
    public static bool Zeigen(IWin32Window owner, Planstunde p)
    {
        var doc = AppZustand.Doc;
        var ix = doc.Index;
        var u = ix.Unterrichte[p.UnterrichtId];
        using var d = new Dialog($"Raum zuordnen / löschen – Unterricht {u.Id} {Planungsdienst.Tagname(doc.Daten, p.Tag)}-{p.Stunde}", 820, 420);
        d.Ok.Text = "Schließen";
        d.Abbrechen.Visible = false;
        var links = new DataGridView { Left = 10, Top = 30, Width = 360, Height = 300 };
        var rechts = new DataGridView { Left = 380, Top = 30, Width = 430, Height = 300 };
        Ui.RasterGestalten(links, false);
        Ui.RasterGestalten(rechts, false);
        links.SelectionMode = rechts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        links.RowHeadersVisible = rechts.RowHeadersVisible = false;
        links.MultiSelect = rechts.MultiSelect = false;
        foreach (var (n, w) in new[] { ("Zeile", 40), ("Lehrer", 60), ("Raum", 60), ("Klasse(n)", 90), ("Wunschraum", 90) }) links.Columns.Add(n, n);
        foreach (var (n, w) in new[] { ("Rm", 60), ("Kap", 40), ("Ausw.-Rm", 70), ("Besetzt", 150), ("Raumgruppe", 80), ("Wunsch", 50) }) rechts.Columns.Add(n, n);
        var besetzte = new CheckBox { Text = "Auch besetzte Räume anzeigen", Left = 380, Top = 6, Width = 240 };
        d.Controls.Add(new Label { Text = "Auswahl des Unterrichts", Left = 10, Top = 10, Width = 300 });
        var zuordnen = new Button { Text = "✓ Raum zuordnen", Left = 380, Top = 340, Width = 130, Height = 26 };
        var loeschen = new Button { Text = "✕ Raum löschen", Left = 516, Top = 340, Width = 130, Height = 26 };
        d.Controls.AddRange(new Control[] { links, rechts, besetzte, zuordnen, loeschen });
        var geaendert = false;

        void Links()
        {
            links.Rows.Clear();
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                links.Rows.Add(i + 1, ix.KLehrer(z.LehrerId), ix.KRaum(p.RaumFuerZeile(i)), ix.KKlassen(z.KlassenIds), z.Wunschraum.HasValue ? ix.KRaum(z.Wunschraum) : ix.K(ElementTyp.Raumgruppe, z.Wunschraumgruppe));
            }
        }
        void Rechts()
        {
            rechts.Rows.Clear();
            var zeile = Math.Max(0, links.CurrentRow?.Index ?? 0);
            foreach (var (raum, frei, belegt, wunsch) in Planungsdienst.Raumliste(doc.Daten, ix, p, zeile))
            {
                if (!frei && !besetzte.Checked) continue;
                var gruppe = string.Join(",", doc.Daten.Raumgruppen.Where(g => g.RaumIds.Contains(raum.Id)).Select(g => g.Kurzname));
                var r = rechts.Rows.Add(raum.Kurzname, raum.Kapazitaet == 0 ? "" : raum.Kapazitaet.ToString(), ix.KRaum(raum.AusweichraumId),
                    belegt is null ? (raum.Zeitwuensche.Gesperrt(p.Tag, p.Stunde) ? "gesperrt" : "") : ix.UnterrichtText(ix.Unterrichte[belegt.UnterrichtId]), gruppe, wunsch ? "✓" : "");
                rechts.Rows[r].Tag = (raum, belegt);
                if (!frei) rechts.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                else if (wunsch) rechts.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(225, 248, 225);
            }
        }
        links.SelectionChanged += (_, _) => Rechts();
        besetzte.CheckedChanged += (_, _) => Rechts();
        zuordnen.Click += (_, _) =>
        {
            if (rechts.CurrentRow?.Tag is not ValueTuple<Raum, Planstunde> t) return;
            var (raum, belegt) = t;
            var zeile = Math.Max(0, links.CurrentRow?.Index ?? 0);
            var loesung = Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen;
            if (belegt != null)
            {
                var w = PlanungsKonfliktDialog.Fragen(d, "Raum nicht frei", $"Raum {raum.Kurzname} ist durch Unterricht {belegt.UnterrichtId} belegt.",
                    "Behindernden Unterricht aus dem Raum verdrängen", "Räume tauschen");
                if (w is null) return;
                loesung = w == 0 ? Planungsdienst.RaumKonfliktLoesung.Verdraengen : Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen;
            }
            var idx = doc.Daten.Stundenplan.IndexOf(p);
            doc.Aendern("Raum zuordnen", AenderungsArt.Stundenplan, dd => Planungsdienst.RaumZuordnen(dd, dd.Stundenplan[idx], zeile, raum.Id, loesung));
            p = doc.Daten.Stundenplan[idx];
            ix = doc.Index;
            geaendert = true;
            Links();
            Rechts();
        };
        loeschen.Click += (_, _) =>
        {
            var zeile = Math.Max(0, links.CurrentRow?.Index ?? 0);
            var idx = doc.Daten.Stundenplan.IndexOf(p);
            doc.Aendern("Raum löschen", AenderungsArt.Stundenplan, dd => Planungsdienst.RaumZuordnen(dd, dd.Stundenplan[idx], zeile, null, Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen));
            p = doc.Daten.Stundenplan[idx];
            ix = doc.Index;
            geaendert = true;
            Links();
            Rechts();
        };
        Links();
        Rechts();
        d.ShowDialog(owner);
        return geaendert;
    }
}

/// <summary>Tauschvorschläge mit Gewinn (Verbesserung bzw. Verschlechterung der Bewertung).</summary>
public static class TauschvorschlaegeDialog
{
    public static Tauschvorschlag? Zeigen(IWin32Window owner, Planstunde p)
    {
        List<Tauschvorschlag> liste;
        using (new WarteCursor()) liste = Planungsdienst.Tauschvorschlaege(AppZustand.D, p);
        using var d = new Dialog("Tauschvorschläge", 700, 360);
        d.Ok.Text = "Tauschen";
        var g = new DataGridView { Left = 10, Top = 10, Width = 680, Height = 300 };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.RowHeadersVisible = false;
        g.Columns.Add("G", "Gewinn");
        g.Columns.Add("T", "Tausch");
        g.Columns[1].Width = 590;
        foreach (var v in liste)
        {
            var r = g.Rows.Add(v.Gewinn.ToString("+0;-0;0"), v.Beschreibung);
            g.Rows[r].Tag = v;
            g.Rows[r].Cells[0].Style.ForeColor = v.Gewinn > 0 ? Color.DarkGreen : v.Gewinn < 0 ? Color.DarkRed : Color.Black;
        }
        d.Controls.Add(g);
        if (liste.Count == 0) Ui.Info("Für diese Stunde wurden keine Tauschmöglichkeiten gefunden.");
        return d.ShowDialog(owner) == DialogResult.OK ? g.CurrentRow?.Tag as Tauschvorschlag : null;
    }
}

public sealed class WarteCursor : IDisposable
{
    private readonly Cursor _alt = Cursor.Current ?? Cursors.Default;
    public WarteCursor() => Cursor.Current = Cursors.WaitCursor;
    public void Dispose() => Cursor.Current = _alt;
}

/// <summary>Stundenplan-Einstellungen (Layout, Farben, Standardformat) und Grafik-Editor "Stundenplan-Stunde".</summary>
public static class StundenplanEinstellungenDialog
{
    public static StundenplanFormat? Zeigen(IWin32Window owner, StundenplanFormat f)
    {
        using var d = new Dialog($"Stundenplan-Einstellungen – {f.Name}", 640, 470);
        var tabs = new TabControl { Left = 8, Top = 8, Width = 624, Height = 410 };
        var layout = new TabPage("Layout");
        var y = 12;
        var name = Ui.Zeile(layout, "Formatname", new TextBox { Text = f.Name }, ref y);
        var schrift = Ui.Zeile(layout, "Schriftgröße", new NumericUpDown { Minimum = 6, Maximum = 20, Value = f.Schriftgroesse }, ref y, 170, 70);
        var farbe = Ui.Zeile(layout, "Farbe nach", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y);
        farbe.Items.AddRange(new object[] { "Fach", "Lehrer", "Klasse", "Raum", "keine" });
        farbe.SelectedIndex = (int)f.Farbe;
        var lang = Ui.Zeile(layout, "Langnamen anzeigen", new CheckBox { Checked = f.Langname }, ref y);
        var zeiten = Ui.Zeile(layout, "Uhrzeiten anzeigen", new CheckBox { Checked = f.ZeitenAnzeigen }, ref y);
        var dd = Ui.Zeile(layout, "DragDrop: Farben laut Zeitwünsche", new CheckBox { Checked = f.DragDropZeitwunschFarben }, ref y, 220);
        var std = Ui.Zeile(layout, "Stpl-Stunde: Standardformat", new CheckBox { Checked = f.Standardformat }, ref y, 220);
        layout.Controls.Add(new Label { Text = "Tipp: Für jedes Element zwei Formate verwenden – ein kleines zum Arbeiten am Stundenplan und \"… groß\" mit allen Informationen für die Ausgabe.", Left = 12, Top = y + 10, Width = 590, Height = 40, ForeColor = Color.DimGray });
        tabs.TabPages.Add(layout);

        var stunde = new TabPage("Stundenplan-Stunde");
        var editor = new StundenEditor(f.Felder.Select(x => new StundenFeld { Art = x.Art, Zeile = x.Zeile, X = x.X, Y = x.Y, B = x.B, H = x.H, SchriftProzent = x.SchriftProzent, Fett = x.Fett }).ToList())
        { Left = 170, Top = 12, Width = 430, Height = 280 };
        var liste = new ListBox { Left = 12, Top = 12, Width = 150, Height = 280 };
        foreach (var a in Enum.GetValues<StundenFeldArt>()) liste.Items.Add(a);
        liste.MouseDown += (_, e) => { if (liste.SelectedItem is StundenFeldArt a) liste.DoDragDrop("feld:" + a, DragDropEffects.Copy); };
        var prozent = new NumericUpDown { Left = 170, Top = 300, Width = 70, Minimum = 40, Maximum = 300, Value = 100 };
        var fett = new CheckBox { Text = "Fett", Left = 250, Top = 300, Width = 60 };
        var zeileNr = new NumericUpDown { Left = 380, Top = 300, Width = 50, Minimum = 1, Maximum = 6, Value = 1 };
        var entfernen = new Button { Text = "Feld entfernen", Left = 440, Top = 298, Width = 110 };
        stunde.Controls.Add(new Label { Text = "Schriftgrad %", Left = 170, Top = 328, Width = 100 });
        stunde.Controls.Add(new Label { Text = "Kopplungszeile", Left = 380, Top = 328, Width = 100 });
        stunde.Controls.Add(new Label { Text = "Felder per Drag & Drop in die Stunde ziehen. Das Hintergrundfeld symbolisiert die Stundenplan-Zelle; Hilfslinien unterstützen beim Ausrichten. Für Kopplungen ein Feld mehrfach einfügen (Kopplungszeile 1, 2, …).", Left = 12, Top = 350, Width = 590, Height = 32, ForeColor = Color.DimGray });
        editor.AuswahlGeaendert += sf =>
        {
            prozent.Enabled = fett.Enabled = zeileNr.Enabled = entfernen.Enabled = sf != null;
            if (sf is null) return;
            prozent.Value = Math.Clamp(sf.SchriftProzent, 40, 300);
            fett.Checked = sf.Fett;
            zeileNr.Value = Math.Clamp(sf.Zeile, 1, 6);
        };
        prozent.ValueChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.SchriftProzent = (int)prozent.Value; editor.Invalidate(); } };
        fett.CheckedChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.Fett = fett.Checked; editor.Invalidate(); } };
        zeileNr.ValueChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.Zeile = (int)zeileNr.Value; editor.Invalidate(); } };
        entfernen.Click += (_, _) => editor.Entfernen();
        stunde.Controls.AddRange(new Control[] { liste, editor, prozent, fett, zeileNr, entfernen });
        tabs.TabPages.Add(stunde);
        d.Controls.Add(tabs);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        f.Name = name.Text;
        f.Schriftgroesse = (int)schrift.Value;
        f.Farbe = (FarbeNach)farbe.SelectedIndex;
        f.Langname = lang.Checked;
        f.ZeitenAnzeigen = zeiten.Checked;
        f.DragDropZeitwunschFarben = dd.Checked;
        f.Felder = editor.Felder;
        f.Standardformat = std.Checked || f.Felder.Count == 0;
        return f;
    }

    /// <summary>Grafik-Editor der Stundenplan-Zelle mit Snap-Lines.</summary>
    private sealed class StundenEditor : Control
    {
        public List<StundenFeld> Felder { get; }
        public StundenFeld? Ausgewaehlt { get; private set; }
        public event Action<StundenFeld?>? AuswahlGeaendert;
        private PointF _start;
        private StundenFeld? _ziehen;
        private bool _groesse;
        private float? _snapX, _snapY;

        public StundenEditor(List<StundenFeld> felder)
        {
            Felder = felder;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private RectangleF Rect(StundenFeld f) => new(f.X * Width, f.Y * Height, f.B * Width, f.H * Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(235, 240, 248));
            using (var p = new Pen(Color.FromArgb(215, 222, 232))) for (var x = 0; x < Width; x += 10) g.DrawLine(p, x, 0, x, Height);
            using (var p = new Pen(Color.FromArgb(215, 222, 232))) for (var y = 0; y < Height; y += 10) g.DrawLine(p, 0, y, Width, y);
            foreach (var f in Felder)
            {
                var r = Rect(f);
                g.FillRectangle(Brushes.White, r);
                g.DrawRectangle(f == Ausgewaehlt ? Pens.OrangeRed : Pens.Gray, r.X, r.Y, r.Width, r.Height);
                using var font = new Font("Segoe UI", Math.Max(6f, 12f * f.SchriftProzent / 100f), f.Fett ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, $"{f.Zeile}{f.Art}", font, Rectangle.Round(r), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                g.FillRectangle(Brushes.Black, r.Right - 4, r.Bottom - 4, 4, 4);
            }
            using var snap = new Pen(Color.Red) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            if (_snapX.HasValue) g.DrawLine(snap, _snapX.Value, 0, _snapX.Value, Height);
            if (_snapY.HasValue) g.DrawLine(snap, 0, _snapY.Value, Width, _snapY.Value);
        }

        protected override void OnDragEnter(DragEventArgs e) => e.Effect = (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("feld:") == true ? DragDropEffects.Copy : DragDropEffects.None;

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.Text) is not string s || !Enum.TryParse<StundenFeldArt>(s[5..], out var art)) return;
            var p = PointToClient(new Point(e.X, e.Y));
            var f = new StundenFeld { Art = art, X = Math.Clamp(p.X / (float)Width - 0.2f, 0, 0.6f), Y = Math.Clamp(p.Y / (float)Height - 0.1f, 0, 0.8f), B = 0.4f, H = 0.2f, Zeile = Felder.Count(x => x.Art == art) + 1, Fett = art == StundenFeldArt.Fach };
            Felder.Add(f);
            Ausgewaehlt = f;
            AuswahlGeaendert?.Invoke(f);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Ausgewaehlt = Felder.LastOrDefault(f => Rect(f).Contains(e.Location));
            AuswahlGeaendert?.Invoke(Ausgewaehlt);
            _ziehen = Ausgewaehlt;
            if (_ziehen != null)
            {
                var r = Rect(_ziehen);
                _groesse = e.X > r.Right - 8 && e.Y > r.Bottom - 8;
                _start = new PointF(e.X - r.X, e.Y - r.Y);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_ziehen is null || e.Button != MouseButtons.Left) return;
            _snapX = _snapY = null;
            if (_groesse)
            {
                _ziehen.B = Math.Clamp((e.X - _ziehen.X * Width) / Width, 0.08f, 1 - _ziehen.X);
                _ziehen.H = Math.Clamp((e.Y - _ziehen.Y * Height) / Height, 0.08f, 1 - _ziehen.Y);
            }
            else
            {
                var x = (e.X - _start.X) / Width;
                var y = (e.Y - _start.Y) / Height;
                foreach (var o in Felder.Where(o => o != _ziehen))
                {
                    if (Math.Abs(o.X - x) < 0.02f) { x = o.X; _snapX = o.X * Width; }
                    if (Math.Abs(o.Y - y) < 0.03f) { y = o.Y; _snapY = o.Y * Height; }
                }
                _ziehen.X = Math.Clamp(x, 0, 1 - _ziehen.B);
                _ziehen.Y = Math.Clamp(y, 0, 1 - _ziehen.H);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _ziehen = null;
            _snapX = _snapY = null;
            Invalidate();
        }

        public void Entfernen()
        {
            if (Ausgewaehlt is null) return;
            Felder.Remove(Ausgewaehlt);
            Ausgewaehlt = null;
            AuswahlGeaendert?.Invoke(null);
            Invalidate();
        }
    }
}
