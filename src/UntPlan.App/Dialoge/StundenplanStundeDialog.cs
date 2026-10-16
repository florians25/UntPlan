using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vorbild „Stundenplan-Stunde - Inhalt (Felder) einer Unterrichtsstunde“: Felder aus der Liste links in die Stunde ziehen (Rasterfläche),
/// verschieben und an den Griffen in der Größe ändern; Fett/Kursiv/Unterstrichen, Ausrichtung, Schriftgröße in %, Felder in der Stunde
/// zentrieren, Beginn- und Endzeit, Standardstunde, Art des Stundenplanes, „Fld. ausrichten“, Art des Namens (Kurz-/Lang-/Druckname),
/// Alias verwenden, Lehrer und Vertreter, Zoom. Damit lassen sich Stundenpläne vollständig selbst gestalten.
/// </summary>
public static class StundenplanStundeDialog
{
    /// <summary>Felder in der Reihenfolge der Vorbild-Liste.</summary>
    public static readonly (StundenFeldArt Art, string Text)[] Palette =
    {
        (StundenFeldArt.Klasse, "Klasse"), (StundenFeldArt.Lehrer, "Lehrer"), (StundenFeldArt.Raum, "Raum"), (StundenFeldArt.Fach, "Fach"),
        (StundenFeldArt.Schuelergruppe, "Schülergruppe"), (StundenFeldArt.StudZl, "Stud-Zl"), (StundenFeldArt.UNr, "Unterrichts-Nummer"),
        (StundenFeldArt.UnterrichtsGruppe, "Unt.Gru"), (StundenFeldArt.Fussnote, "Referenz zu Fußnote"), (StundenFeldArt.Sondertext, "Sondertext"),
        (StundenFeldArt.ZeilenText, "ZeilenText"), (StundenFeldArt.ZeilenText2, "ZeilenText-2"), (StundenFeldArt.Stundentext, "Stundentext"),
        (StundenFeldArt.Beschreibung, "Beschreibung"), (StundenFeldArt.Zeitwunsch, "Zeitwunsch"), (StundenFeldArt.Band, "Band"),
    };

    public static StundenplanFormat? Zeigen(IWin32Window owner, StundenplanFormat f, ElementTyp typ, int elementId)
    {
        using var d = Erstellen(f, typ, elementId, out var ergebnis);
        return d.ShowDialog(owner) == DialogResult.OK ? ergebnis() : null;
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, StundenplanFormat f, ElementTyp typ, int elementId, out Func<StundenplanFormat> ergebnis)
    {
        var d = Erstellen(f, typ, elementId, out ergebnis);
        d.Show(owner);
        return d;
    }

    /// <summary>Für den Selbsttest: der Editor (Rasterfläche) im Dialog.</summary>
    internal static Editor EditorVon(Form d) => StundenplanEinstellungenDialog.AlleKontrollen(d).OfType<Editor>().First();

    private static Dialog Erstellen(StundenplanFormat f, ElementTyp typ, int elementId, out Func<StundenplanFormat> ergebnis)
    {
        var d = new Dialog("Stundenplan-Stunde - Inhalt (Felder) einer Unterrichtsstunde", 980, 640) { Font = Ui.Schrift, FormBorderStyle = FormBorderStyle.Sizable, MinimumSize = new Size(820, 520) };
        var felder = f.Felder.Select(StundenplanEinstellungenDialog.Kopie).ToList();
        var oben = new Panel { Left = 0, Top = 0, Width = 980, Height = 136, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var zentrieren = new GroupBox { Text = "Felder in Stunde zentrieren", Left = 10, Top = 4, Width = 190, Height = 62 };
        var horiz = new CheckBox { Text = "Horizontal", Left = 10, Top = 18, Width = 120, Checked = f.FelderHorizontalZentrieren };
        var vert = new CheckBox { Text = "Vertikal", Left = 10, Top = 38, Width = 120, Checked = f.FelderVertikalZentrieren };
        zentrieren.Controls.AddRange(new Control[] { horiz, vert });
        var beginnEnde = new CheckBox { Text = "Beginn- und Endzeit", Left = 12, Top = 68, Width = 180, Checked = f.BeginnEndzeit };
        var standard = new CheckBox { Text = "Standardstunde", Left = 22, Top = 88, Width = 170, Checked = f.Standardformat && felder.Count == 0 };
        var artLabel = new Label { Text = "Art des Stundenplanes", Left = 12, Top = 106, Width = 150 };
        var art = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 108, Width = 130, Visible = true };
        art.Items.AddRange(new object[] { "Klasse", "Lehrer", "Raum", "Fach" });
        art.SelectedIndex = typ switch { ElementTyp.Lehrer => 1, ElementTyp.Raum => 2, ElementTyp.Fach => 3, _ => 0 };
        artLabel.Top = 108; artLabel.Height = 14; art.Top = 123; oben.Height = 150;
        var fett = Umschalter("B", FontStyle.Bold, 210, 10);
        var kursiv = Umschalter("K", FontStyle.Italic, 240, 10);
        var unter = Umschalter("U", FontStyle.Underline, 270, 10);
        var links = Knopf("≡", "Linksbündig", 210, 40);
        var mitte = Knopf("≡", "Zentriert", 240, 40);
        var rechts = Knopf("≡", "Rechtsbündig", 270, 40);
        links.TextAlign = ContentAlignment.MiddleLeft; rechts.TextAlign = ContentAlignment.MiddleRight;
        var prozentText = new Label { Text = "100", Left = 210, Top = 72, Width = 40 };
        oben.Controls.Add(new Label { Text = "Schriftgröße in %", Left = 300, Top = 72, Width = 120 });
        var prozent = new TrackBar { Left = 206, Top = 88, Width = 220, Minimum = 40, Maximum = 300, TickFrequency = 20, SmallChange = 5, LargeChange = 20, Value = 100, AutoSize = false, Height = 30 };
        var ausrichten = new Button { Text = "Fld. ausrichten", Left = 300, Top = 118, Width = 110, Height = 24 };
        var namen = new GroupBox { Text = "Art des Namens", Left = 470, Top = 4, Width = 190, Height = 128 };
        var kurz = new RadioButton { Text = "Kurzname", Left = 10, Top = 18, Width = 120, Checked = f.NamensArt == NamensArt.Kurzname };
        var langn = new RadioButton { Text = "Langname", Left = 10, Top = 38, Width = 120, Checked = f.NamensArt == NamensArt.Langname };
        var druck = new RadioButton { Text = "Druckname", Left = 10, Top = 58, Width = 120, Checked = f.NamensArt == NamensArt.Druckname };
        var alias = new CheckBox { Text = "Alias verwenden", Left = 10, Top = 80, Width = 170, Checked = f.AliasVerwenden };
        var vertreter = new CheckBox { Text = "Lehrer und Vertreter", Left = 10, Top = 100, Width = 170, Checked = f.LehrerUndVertreter };
        namen.Controls.AddRange(new Control[] { kurz, langn, druck, alias, vertreter });
        oben.Controls.Add(new Label { Text = "Zoom", Left = 690, Top = 6, Width = 50 });
        var zoom = new TrackBar { Orientation = Orientation.Vertical, Left = 670, Top = 6, Height = 90, Width = 30, Minimum = 1, Maximum = 8, Value = 4, TickStyle = TickStyle.None, AutoSize = false };
        var zoomText = new Label { Text = "4", Left = 700, Top = 26, Width = 30 };
        oben.Controls.AddRange(new Control[] { zentrieren, beginnEnde, standard, artLabel, art, fett, kursiv, unter, links, mitte, rechts, prozentText, prozent, ausrichten, namen, zoom, zoomText });
        d.Controls.Add(oben);

        // links die Feldliste, rechts die Rasterfläche mit der Stunde
        var liste = new Panel { Left = 10, Top = 152, Width = 150, Height = 430, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, BackColor = Ui.BlauSehrHell };
        var py = 12;
        foreach (var (a, text) in Palette)
        {
            var chip = new Label { Text = text, Left = 8, Top = py, Width = 126, Height = 15, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = new Font("Segoe UI", 7.5f), Tag = a, Cursor = Cursors.Hand };
            liste.Controls.Add(chip);
            py += 18;
        }
        var flaeche = new Panel { Left = 168, Top = 152, Width = 800, Height = 430, BorderStyle = BorderStyle.FixedSingle, AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        var editor = new Editor(felder) { Left = 0, Top = 0 };
        flaeche.Controls.Add(editor);
        d.Controls.AddRange(new Control[] { liste, flaeche });
        foreach (Label chip in liste.Controls)
        {
            chip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) chip.DoDragDrop("feld:" + chip.Tag, DragDropEffects.Copy); };
            chip.DoubleClick += (_, _) => editor.Hinzufuegen((StundenFeldArt)chip.Tag!, null);
        }

        void ZoomSetzen()
        {
            zoomText.Text = zoom.Value.ToString();
            editor.Zoom = zoom.Value;
        }
        zoom.ValueChanged += (_, _) => ZoomSetzen();
        ZoomSetzen();
        var laden = false;
        editor.AuswahlGeaendert += sf =>
        {
            laden = true;
            try
            {
                foreach (var c in new Control[] { fett, kursiv, unter, links, mitte, rechts, prozent }) c.Enabled = sf != null;
                if (sf is null) return;
                fett.Checked = sf.Fett;
                kursiv.Checked = sf.Kursiv;
                unter.Checked = sf.Unterstrichen;
                prozent.Value = Math.Clamp(sf.SchriftProzent, prozent.Minimum, prozent.Maximum);
                prozentText.Text = prozent.Value.ToString();
            }
            finally { laden = false; }
        };
        void Aendern(Action<StundenFeld> a)
        {
            if (laden || editor.Ausgewaehlt is null) return;
            a(editor.Ausgewaehlt);
            editor.Invalidate();
            standard.Checked = false;
        }
        fett.CheckedChanged += (_, _) => Aendern(x => x.Fett = fett.Checked);
        kursiv.CheckedChanged += (_, _) => Aendern(x => x.Kursiv = kursiv.Checked);
        unter.CheckedChanged += (_, _) => Aendern(x => x.Unterstrichen = unter.Checked);
        links.Click += (_, _) => Aendern(x => x.Ausrichtung = 0);
        mitte.Click += (_, _) => Aendern(x => x.Ausrichtung = 1);
        rechts.Click += (_, _) => Aendern(x => x.Ausrichtung = 2);
        prozent.ValueChanged += (_, _) => { prozentText.Text = prozent.Value.ToString(); Aendern(x => x.SchriftProzent = prozent.Value); };
        ausrichten.Click += (_, _) => editor.Ausrichten();
        editor.Geaendert += () => { if (editor.Felder.Count > 0) standard.Checked = false; };
        standard.CheckedChanged += (_, _) => { if (standard.Checked && editor.Felder.Count > 0 && !laden) { editor.Felder.Clear(); editor.Auswaehlen(null); editor.Invalidate(); } };
        editor.AuswahlGeaendert?.Invoke(null);
        // vorhandene Felder: erstes auswählen, damit die Werkzeuge gleich wirken
        editor.Auswaehlen(felder.FirstOrDefault());

        ergebnis = () =>
        {
            var z = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.Serialize(f))!;
            z.Felder = editor.Felder.Select(StundenplanEinstellungenDialog.Kopie).ToList();
            z.Standardformat = standard.Checked || z.Felder.Count == 0;
            z.FelderHorizontalZentrieren = horiz.Checked;
            z.FelderVertikalZentrieren = vert.Checked;
            z.BeginnEndzeit = beginnEnde.Checked;
            z.NamensArt = langn.Checked ? NamensArt.Langname : druck.Checked ? NamensArt.Druckname : NamensArt.Kurzname;
            z.AliasVerwenden = alias.Checked;
            z.LehrerUndVertreter = vertreter.Checked;
            return z;
        };
        return d;
    }

    private static CheckBox Umschalter(string text, FontStyle stil, int x, int y) =>
        new() { Text = text, Appearance = Appearance.Button, Left = x, Top = y, Width = 26, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f, stil | FontStyle.Bold), FlatStyle = FlatStyle.Flat };

    private static Button Knopf(string text, string hinweis, int x, int y)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = 26, Height = 26, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f) };
        new ToolTip().SetToolTip(b, hinweis);
        return b;
    }

    /// <summary>Rasterfläche mit einer Unterrichtsstunde; Felder werden hineingezogen, verschoben und an den Griffen vergrößert.</summary>
    internal sealed class Editor : Control
    {
        private const int Raster = 10;
        public List<StundenFeld> Felder { get; }
        public StundenFeld? Ausgewaehlt { get; private set; }
        public event Action? Geaendert;
        public Action<StundenFeld?>? AuswahlGeaendert;
        private int _zoom = 4;
        private StundenFeld? _ziehen;
        private int _griff; // 0 = verschieben, 1 = rechts, 2 = unten, 3 = Ecke
        private PointF _start;

        public Editor(List<StundenFeld> felder)
        {
            Felder = felder;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(1000, 700);
        }

        /// <summary>Zoom 1–8: Größe der Stunde auf der Rasterfläche.</summary>
        public int Zoom
        {
            get => _zoom;
            set { _zoom = Math.Clamp(value, 1, 8); Invalidate(); }
        }

        /// <summary>Rechteck der Unterrichtsstunde auf der Fläche (Vorbild: weiße Fläche im Raster).</summary>
        public Rectangle Stunde => new(40, 30, 40 + _zoom * 36, 20 + _zoom * 18);

        public RectangleF Rechteck(StundenFeld f)
        {
            var s = Stunde;
            return new RectangleF(s.X + f.X * s.Width, s.Y + f.Y * s.Height, f.B * s.Width, f.H * s.Height);
        }

        public void Auswaehlen(StundenFeld? f)
        {
            Ausgewaehlt = f;
            AuswahlGeaendert?.Invoke(f);
            Invalidate();
        }

        public void Hinzufuegen(StundenFeldArt art, PointF? an)
        {
            var s = Stunde;
            var x = an is PointF p ? (p.X - s.X) / s.Width - 0.2f : 0.1f;
            var y = an is PointF q ? (q.Y - s.Y) / s.Height - 0.1f : Math.Min(0.75f, 0.08f + Felder.Count * 0.22f);
            var f = new StundenFeld
            {
                Art = art, B = 0.5f, H = 0.24f, Zeile = Felder.Count(o => o.Art == art) + 1, Fett = art == StundenFeldArt.Fach,
                X = Math.Clamp(x, 0, 0.5f), Y = Math.Clamp(y, 0, 0.76f),
            };
            Felder.Add(f);
            Auswaehlen(f);
            Geaendert?.Invoke();
        }

        /// <summary>„Fld. ausrichten“: Felder am Raster ausrichten und untereinander ohne Überlappung anordnen.</summary>
        public void Ausrichten()
        {
            static float Runden(float v) => MathF.Round(v * 20f) / 20f;
            var y = 0.05f;
            foreach (var f in Felder.OrderBy(f => f.Y).ThenBy(f => f.X))
            {
                f.X = Math.Clamp(Runden(f.X), 0, 1 - f.B);
                f.H = Math.Max(0.1f, Runden(f.H));
                f.B = Math.Clamp(Runden(f.B), 0.1f, 1);
                f.Y = Math.Clamp(Math.Max(Runden(f.Y), y), 0, 1 - f.H);
                y = f.Y + f.H;
            }
            Geaendert?.Invoke();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(232, 240, 250));
            using (var p = new Pen(Color.FromArgb(205, 215, 230)))
            {
                for (var x = 0; x < Width; x += Raster) g.DrawLine(p, x, 0, x, Height);
                for (var y = 0; y < Height; y += Raster) g.DrawLine(p, 0, y, Width, y);
            }
            var s = Stunde;
            g.FillRectangle(Brushes.White, s);
            g.DrawRectangle(Pens.Gray, s);
            foreach (var f in Felder)
            {
                var r = Rechteck(f);
                var stil = (f.Fett ? FontStyle.Bold : 0) | (f.Kursiv ? FontStyle.Italic : 0) | (f.Unterstrichen ? FontStyle.Underline : 0);
                using var font = new Font("Segoe UI", Math.Max(6f, 3.2f * _zoom * f.SchriftProzent / 100f), stil);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                            (f.Ausrichtung == 0 ? TextFormatFlags.Left : f.Ausrichtung == 2 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, f.Beschriftung, font, Rectangle.Round(r), Color.Black, flags);
                using var rahmen = new Pen(f == Ausgewaehlt ? Color.Black : Color.FromArgb(150, 150, 150)) { DashStyle = f == Ausgewaehlt ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dot };
                g.DrawRectangle(rahmen, r.X, r.Y, r.Width, r.Height);
                if (f == Ausgewaehlt)
                    foreach (var gr in Griffe(r)) g.FillRectangle(Brushes.Black, gr);
            }
        }

        private static RectangleF[] Griffe(RectangleF r) => new[]
        {
            new RectangleF(r.Right - 2, r.Y + r.Height / 2 - 2, 5, 5),
            new RectangleF(r.X + r.Width / 2 - 2, r.Bottom - 2, 5, 5),
            new RectangleF(r.Right - 2, r.Bottom - 2, 5, 5),
        };

        protected override void OnDragEnter(DragEventArgs e) =>
            e.Effect = (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("feld:") == true ? DragDropEffects.Copy : DragDropEffects.None;

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.Text) is not string s || !Enum.TryParse<StundenFeldArt>(s[5..], out var art)) return;
            Hinzufuegen(art, PointToClient(new Point(e.X, e.Y)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _ziehen = null;
            if (Ausgewaehlt != null)
            {
                var gr = Griffe(Rechteck(Ausgewaehlt));
                for (var i = 0; i < gr.Length; i++)
                    if (RectangleF.Inflate(gr[i], 3, 3).Contains(e.Location)) { _ziehen = Ausgewaehlt; _griff = i + 1; }
            }
            if (_ziehen is null)
            {
                var f = Felder.LastOrDefault(x => Rechteck(x).Contains(e.Location));
                Auswaehlen(f);
                _ziehen = f;
                _griff = 0;
            }
            if (_ziehen != null) { var r = Rechteck(_ziehen); _start = new PointF(e.X - r.X, e.Y - r.Y); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_ziehen is null || e.Button != MouseButtons.Left) return;
            var s = Stunde;
            float Rast(float v) => MathF.Round(v * s.Width / Raster) * Raster / s.Width;
            float RastY(float v) => MathF.Round(v * s.Height / Raster) * Raster / s.Height;
            var f = _ziehen;
            if (_griff == 0)
            {
                f.X = Math.Clamp(Rast((e.X - _start.X - s.X) / s.Width), 0, 1 - f.B);
                f.Y = Math.Clamp(RastY((e.Y - _start.Y - s.Y) / s.Height), 0, 1 - f.H);
            }
            if (_griff is 1 or 3) f.B = Math.Clamp(Rast((e.X - s.X) / (float)s.Width - f.X), 0.08f, 1 - f.X);
            if (_griff is 2 or 3) f.H = Math.Clamp(RastY((e.Y - s.Y) / (float)s.Height - f.Y), 0.08f, 1 - f.Y);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_ziehen != null) Geaendert?.Invoke();
            _ziehen = null;
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Delete or Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Ausgewaehlt is null) return;
            var s = Stunde;
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    Felder.Remove(Ausgewaehlt);
                    Auswaehlen(null);
                    Geaendert?.Invoke();
                    return;
                case Keys.Left: Ausgewaehlt.X = Math.Max(0, Ausgewaehlt.X - Raster / (float)s.Width); break;
                case Keys.Right: Ausgewaehlt.X = Math.Min(1 - Ausgewaehlt.B, Ausgewaehlt.X + Raster / (float)s.Width); break;
                case Keys.Up: Ausgewaehlt.Y = Math.Max(0, Ausgewaehlt.Y - Raster / (float)s.Height); break;
                case Keys.Down: Ausgewaehlt.Y = Math.Min(1 - Ausgewaehlt.H, Ausgewaehlt.Y + Raster / (float)s.Height); break;
                default: return;
            }
            Geaendert?.Invoke();
            Invalidate();
        }
    }
}
