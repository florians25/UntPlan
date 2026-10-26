using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace UntPlan.App;

/// <summary>Art eines Meldungsfensters: bestimmt Symbol und Farbe.</summary>
public enum MeldungsArt { Info, Frage, Warnung, Fehler }

/// <summary>
/// Hinweis-, Frage- und Fehlerfenster im Stil des Vorbilds (statt der Windows-Meldungsfenster): kleines Fenster mit heller
/// Titelleiste in der Modusfarbe (Titel „… - Hinweis“ und ×), Fläche in der Listenfarbe mit Windows-Symbol (gelbes Warndreieck,
/// rotes ×, blaues ? bzw. i) und Text, darunter durch eine Linie abgesetzt die Knopfleiste mit flachen Knöpfen rechts.
/// Alle Meldungen laufen über <see cref="Ui.Meldung"/> – auch Raumfragen (Raum belegt, Räume mitnehmen), Löschrückfragen und Fehler.
/// </summary>
public static partial class Ui
{
    /// <summary>Farbe des Symbols je Art (Windows-Symbole: Warndreieck gelb, Fehler rot, Frage/Info blau).</summary>
    internal static Color MeldungsFarbe(MeldungsArt art) => art switch
    {
        MeldungsArt.Fehler => Color.FromArgb(232, 17, 35),
        MeldungsArt.Warnung => Color.FromArgb(252, 202, 1),
        _ => Color.FromArgb(0, 120, 215),
    };

    /// <summary>Helle Titelleiste der Meldungsfenster (Vorbild: Mintgrün im Vertretungsmodus).</summary>
    public static Color MeldungTitel => Vertretungsmodus ? Color.FromArgb(149, 208, 185) : Color.FromArgb(158, 202, 218);
    /// <summary>Fläche der Meldungsfenster (wie der Listenhintergrund).</summary>
    public static Color MeldungFlaeche => Listenhintergrund;
    /// <summary>Knopfleiste der Meldungsfenster (etwas dunkler als die Fläche).</summary>
    public static Color MeldungLeiste => Vertretungsmodus ? Color.FromArgb(223, 235, 230) : Color.FromArgb(228, 236, 240);
    /// <summary>Rand des Standardknopfes (dunkle Modusfarbe wie im Vorbild).</summary>
    public static Color MeldungKnopfRand => Vertretungsmodus ? Color.FromArgb(81, 132, 111) : Color.FromArgb(52, 106, 128);
    /// <summary>Fensterrahmen der Meldungsfenster.</summary>
    internal static readonly Color MeldungRahmen = Color.FromArgb(131, 131, 131);

    /// <summary>
    /// Meldung mit eigenen Knöpfen anzeigen. Liefert den Index des gewählten Knopfes; null bei „Abbrechen“ bzw. Escape (mit
    /// <paramref name="abbrechen"/>). Ohne „Abbrechen“ wählt Escape den letzten Knopf (z. B. „Nein“ bzw. „OK“).
    /// </summary>
    public static int? Meldung(string text, string titel, MeldungsArt art, IReadOnlyList<string> knoepfe, int vorgabe = 0, bool abbrechen = false,
        IWin32Window? owner = null, IReadOnlyList<bool>? aktiv = null)
    {
        var ergebnis = new StrongBox<int?>();
        using var f = MeldungsFenster(text, titel, art, knoepfe, vorgabe, abbrechen, aktiv, ergebnis);
        owner ??= Form.ActiveForm is { IsDisposed: false, Visible: true } aktivesFenster ? aktivesFenster : null;
        if (owner is null) f.StartPosition = FormStartPosition.CenterScreen;
        var r = owner is null ? f.ShowDialog() : f.ShowDialog(owner);
        if (r == DialogResult.OK) return ergebnis.Value;
        return abbrechen ? null : knoepfe.Count - 1;
    }

    /// <summary>Meldungsfenster erzeugen (auch nicht modal für Bildschirmfotos im Selbsttest); das Ergebnis steht nach dem Schließen in <paramref name="ergebnis"/>.</summary>
    internal static Form MeldungsFenster(string text, string titel, MeldungsArt art, IReadOnlyList<string> knoepfe, int vorgabe, bool abbrechen,
        IReadOnlyList<bool>? aktiv, StrongBox<int?> ergebnis)
    {
        // ohne eigenen Titel wie im Vorbild „UntPlan - Hinweis“ (bzw. Frage/Fehler)
        if (string.IsNullOrWhiteSpace(titel) || titel.Trim() == "UntPlan")
            titel = "UntPlan - " + art switch { MeldungsArt.Fehler => "Fehler", MeldungsArt.Frage => "Frage", _ => "Hinweis" };
        var f = new MeldungsForm { Text = titel, Art = art };
        var liste = knoepfe.Where(k => !abbrechen || k != "Abbrechen").ToList();
        var alle = abbrechen ? liste.Append("Abbrechen").ToList() : liste;
        // Maße wie im Vorbild: Titelleiste 30, Knopfleiste 53, Knöpfe 88 × 26, Symbol links (32), Text ab 64
        const int rand = 1, titelHoehe = MeldungsForm.TitelHoehe, leiste = 53, textLinks = 64, knopfHoehe = 26;
        int KnopfBreite(string k) => Math.Max(88, TextRenderer.MeasureText(k, Schrift).Width + 24);
        var knopfSumme = alle.Sum(k => KnopfBreite(k) + 8) - 8 + 28;
        var normal = text.Replace("\r\n", "\n").Trim('\n');
        var einzeilig = TextRenderer.MeasureText(normal, Schrift, new Size(int.MaxValue, 0), TextFormatFlags.TextBoxControl);
        var maxText = 460;
        var textBreite = Math.Min(maxText, einzeilig.Width + 8);
        var breite = Math.Clamp(Math.Max(textLinks + textBreite + 30, knopfSumme), 206, 900);
        textBreite = breite - textLinks - 30;
        var groesse = TextRenderer.MeasureText(normal, Schrift, new Size(textBreite, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var hoehe = Math.Clamp(groesse.Height + 4, 16, 440);
        var flaeche = Math.Max(84, hoehe + 48);
        var label = new TextBox
        {
            Text = normal.Replace("\n", Environment.NewLine), Left = rand + textLinks, Top = rand + titelHoehe + (flaeche - hoehe) / 2, Width = textBreite, Height = hoehe,
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = MeldungFlaeche, ForeColor = Color.Black, TabStop = false, Font = Schrift,
            ScrollBars = groesse.Height + 4 > 440 ? ScrollBars.Vertical : ScrollBars.None, Name = "Meldungstext",
        };
        f.Padding = new Padding(rand);
        f.ClientSize = new Size(breite + 2 * rand, titelHoehe + flaeche + leiste + 2 * rand);
        f.SymbolBereich = new Rectangle(rand + 21, rand + titelHoehe + (flaeche - 32) / 2, 32, 32);
        f.Controls.Add(label);
        var unten = new Panel { Dock = DockStyle.Bottom, Height = leiste, BackColor = MeldungLeiste, Name = "Knopfleiste" };
        unten.Paint += (_, e) => { using var p = new Pen(Rasterlinie); e.Graphics.DrawLine(p, 0, 0, unten.Width, 0); };
        f.Controls.Add(unten);
        // von rechts nach links: Abbrechen ganz rechts, davor die Knöpfe in ihrer Reihenfolge
        var x = breite - 14;
        var indizes = Enumerable.Range(0, liste.Count).Select(i => knoepfe.ToList().IndexOf(liste[i]) is var k && k >= 0 ? k : i).ToList();
        var reihenfolge = (abbrechen ? new[] { -1 } : Array.Empty<int>()).Concat(Enumerable.Range(0, liste.Count).Reverse());
        Button? vorgabeKnopf = null;
        foreach (var pos in reihenfolge)
        {
            var istAbbrechen = pos < 0;
            var t = istAbbrechen ? "Abbrechen" : liste[pos];
            var index = istAbbrechen ? -1 : indizes[pos];
            var w = KnopfBreite(t);
            x -= w;
            var b = new Button
            {
                Text = t, Left = x, Top = 12, Width = w, Height = knopfHoehe, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.Black,
                Font = Schrift, UseVisualStyleBackColor = false,
                Enabled = istAbbrechen || aktiv is null || index >= aktiv.Count || aktiv[index],
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(173, 173, 173);
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = BlauHell;
            b.Click += (_, _) => { ergebnis.Value = istAbbrechen ? null : index; f.DialogResult = istAbbrechen ? DialogResult.Cancel : DialogResult.OK; };
            unten.Controls.Add(b);
            if (istAbbrechen) f.CancelButton = b;
            else if (!abbrechen && pos == liste.Count - 1) f.CancelButton = b;
            if (!istAbbrechen && index == vorgabe && b.Enabled) vorgabeKnopf = b;
            x -= 8;
        }
        vorgabeKnopf ??= unten.Controls.OfType<Button>().Where(b => b.Enabled && b != f.CancelButton).LastOrDefault() ?? unten.Controls.OfType<Button>().FirstOrDefault();
        if (vorgabeKnopf != null)
        {
            // Standardknopf wie im Vorbild: weiß mit dunklem Rand in der Modusfarbe (keine fette Schrift)
            vorgabeKnopf.FlatAppearance.BorderColor = MeldungKnopfRand;
            f.AcceptButton = vorgabeKnopf;
            f.ActiveControl = vorgabeKnopf;
        }
        // Strg+C kopiert den Meldungstext (wie bei Windows-Meldungen)
        f.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && !label.Focused) { try { Clipboard.SetText(titel + Environment.NewLine + normal); } catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException) { } e.Handled = true; }
        };
        return f;
    }

    /// <summary>
    /// Formular des Meldungsfensters: ohne Systemrahmen, mit eigener heller Titelleiste (Titel links, × rechts, zum Verschieben
    /// ziehen), 1-Pixel-Rahmen und Windows-Symbol links neben dem Text.
    /// </summary>
    internal sealed class MeldungsForm : Form
    {
        public const int TitelHoehe = 30;
        private const int SchliessenBreite = 46;
        private bool _schliessenHover;

        public MeldungsArt Art { get; init; }
        public Rectangle SymbolBereich { get; set; }

        /// <summary>Bereich des Schließen-Knopfes (×) in der Titelleiste.</summary>
        internal Rectangle SchliessenBereich => new(ClientSize.Width - 1 - SchliessenBreite, 1, SchliessenBreite, TitelHoehe);

        public MeldungsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Schrift;
            BackColor = MeldungFlaeche;
            KeyPreview = true;
            DoubleBuffered = true;
            ShowIcon = false;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate(new Rectangle(0, 0, ClientSize.Width, TitelHoehe + 1));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = SchliessenBereich.Contains(e.Location);
            if (h == _schliessenHover) return;
            _schliessenHover = h;
            Invalidate(SchliessenBereich);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_schliessenHover) return;
            _schliessenHover = false;
            Invalidate(SchliessenBereich);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && SchliessenBereich.Contains(e.Location))
            {
                // × wie Escape: Abbrechen bzw. letzter Knopf
                if (CancelButton is Button b) b.PerformClick();
                else DialogResult = DialogResult.Cancel;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Titelleiste (außer ×) zum Verschieben des Fensters
            if (m.Msg != 0x84 || (int)m.Result != 1) return; // WM_NCHITTEST, HTCLIENT
            var p = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
            if (p.Y >= 1 && p.Y < TitelHoehe + 1 && !SchliessenBereich.Contains(p)) m.Result = (IntPtr)2; // HTCAPTION
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var w = ClientSize.Width;
            var titel = new Rectangle(1, 1, w - 2, TitelHoehe);
            using (var b = new SolidBrush(MeldungTitel)) g.FillRectangle(b, titel);
            if (_schliessenHover)
                using (var rot = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillRectangle(rot, SchliessenBereich);
            TextRenderer.DrawText(g, Text, Schrift, new Rectangle(13, 1, w - 13 - SchliessenBreite, TitelHoehe), Color.Black,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            // × als zwei dünne Linien (9 × 9 wie im Vorbild)
            var s = SchliessenBereich;
            var m = new Point(s.X + s.Width / 2, s.Y + s.Height / 2);
            var zustand = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(_schliessenHover ? Color.White : Color.FromArgb(48, 48, 48), 1.1f))
            {
                g.DrawLine(p, m.X - 4.5f, m.Y - 4.5f, m.X + 4.5f, m.Y + 4.5f);
                g.DrawLine(p, m.X + 4.5f, m.Y - 4.5f, m.X - 4.5f, m.Y + 4.5f);
            }
            g.Restore(zustand);
            using (var r = new Pen(MeldungRahmen)) g.DrawRectangle(r, 0, 0, w - 1, ClientSize.Height - 1);
            if (!SymbolBereich.IsEmpty) SymbolZeichnen(g, SymbolBereich, Art);
        }
    }

    /// <summary>
    /// Symbol der Meldung wie unter Windows: Warnung als gelbes Dreieck mit schwarzem „!“, Fehler als roter Kreis mit weißem ×,
    /// Frage bzw. Info als blauer Kreis mit weißem „?“ bzw. „i“.
    /// </summary>
    internal static void SymbolZeichnen(Graphics g, Rectangle r, MeldungsArt art)
    {
        var zustand = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var farbe = MeldungsFarbe(art);
            if (art == MeldungsArt.Warnung)
            {
                // Dreieck mit abgerundeten Ecken (28 hoch), Rand etwas dunkler
                var oben = new PointF(r.X + r.Width / 2f, r.Y + 2.5f);
                var links = new PointF(r.X + 1f, r.Bottom - 3.5f);
                var rechts = new PointF(r.Right - 1f, r.Bottom - 3.5f);
                using var pfad = new GraphicsPath();
                pfad.AddPolygon(new[] { oben, rechts, links });
                using (var b = new SolidBrush(farbe)) g.FillPath(b, pfad);
                using (var p = new Pen(Color.FromArgb(214, 160, 0), 2f) { LineJoin = LineJoin.Round }) g.DrawPath(p, pfad);
                // Ausrufezeichen: Strich und Punkt
                var mx = r.X + r.Width / 2f;
                using var schwarz = new SolidBrush(Color.FromArgb(20, 20, 20));
                using (var strich = AbgerundetesRechteck(new RectangleF(mx - 1.6f, r.Y + 11f, 3.2f, 10f), 1.4f)) g.FillPath(schwarz, strich);
                g.FillEllipse(schwarz, mx - 1.9f, r.Y + 22.6f, 3.8f, 3.8f);
                return;
            }
            using (var b = new SolidBrush(farbe)) g.FillEllipse(b, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            if (art == MeldungsArt.Fehler)
            {
                var m = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                using var p = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(p, m.X - 6, m.Y - 6, m.X + 6, m.Y + 6);
                g.DrawLine(p, m.X + 6, m.Y - 6, m.X - 6, m.Y + 6);
                return;
            }
            var zeichen = art == MeldungsArt.Frage ? "?" : "i";
            using var schrift = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var weiss = new SolidBrush(Color.White);
            g.DrawString(zeichen, schrift, weiss, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height), sf);
        }
        finally { g.Restore(zustand); }
    }
}
