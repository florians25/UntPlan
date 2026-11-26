using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace UntPlan.App;

/// <summary>Art eines Meldungsfensters: bestimmt Symbol und Farbe.</summary>
public enum MeldungsArt { Info, Frage, Warnung, Fehler }

/// <summary>
/// Hinweis-, Frage- und Fehlerfenster im Stil des Vorbilds (statt der Windows-Meldungsfenster): kleines Fenster mit heller
/// Titelleiste in der Modusfarbe (Titel „… - Hinweis“ und ×), Fläche in der Listenfarbe mit FatCow-Symbol (gelbes Warndreieck,
/// rotes ×, blaues ? bzw. i) und Text, darunter durch eine Linie abgesetzt die Knopfleiste mit flachen Knöpfen rechts.
/// Alle Meldungen laufen über <see cref="Ui.Meldung"/> – auch Raumfragen (Raum belegt, Räume mitnehmen), Löschrückfragen und Fehler.
/// </summary>
public static partial class Ui
{
    /// <summary>Helle Titelleiste der Meldungsfenster (Vorbild: Mintgrün im Vertretungsmodus).</summary>
    public static Color MeldungTitel => Vertretungsmodus ? Color.FromArgb(149, 208, 185) : Color.FromArgb(158, 202, 218);
    /// <summary>Fläche der Meldungsfenster (wie der Listenhintergrund).</summary>
    public static Color MeldungFlaeche => Listenhintergrund;
    /// <summary>Knopfleiste der Meldungsfenster (etwas dunkler als die Fläche).</summary>
    public static Color MeldungLeiste => Vertretungsmodus ? Color.FromArgb(223, 235, 230) : Color.FromArgb(228, 236, 240);
    /// <summary>Rand des Standardknopfes (dunkle Modusfarbe wie im Vorbild).</summary>
    public static Color MeldungKnopfRand => Vertretungsmodus ? Color.FromArgb(81, 132, 111) : Color.FromArgb(52, 106, 128);
    /// <summary>Fensterrahmen der Meldungsfenster (inaktiv).</summary>
    internal static readonly Color MeldungRahmen = Color.FromArgb(131, 131, 131);
    /// <summary>Titelleiste des aktiven Meldungsfensters (Vorbild: dunkle Modusfarbe mit weißer Schrift wie aktive Arbeitsfenster).</summary>
    public static Color MeldungTitelAktiv => Vertretungsmodus ? Color.FromArgb(88, 150, 125) : Titel;
    /// <summary>Standardknopf (Enter) wie im Vorbild: gefüllt in heller Modusfarbe, 1 Pixel Rand in der dunklen Modusfarbe.</summary>
    public static Color StandardKnopfFlaeche => Vertretungsmodus ? Color.FromArgb(144, 205, 181) : Color.FromArgb(158, 202, 218);

    /// <summary>
    /// Knopf als Standardknopf darstellen: Fläche <see cref="StandardKnopfFlaeche"/>, genau 1 Pixel Rand <see cref="MeldungKnopfRand"/>
    /// (Windows zeichnet beim flachen Standardknopf sonst einen doppelten Rand).
    /// </summary>
    internal static void StandardKnopf(Button b)
    {
        b.BackColor = StandardKnopfFlaeche;
        b.FlatAppearance.BorderColor = MeldungKnopfRand;
        if (b.Tag as string == "StandardRand") return;
        b.Tag ??= "StandardRand";
        b.Paint += (_, e) =>
        {
            if (b.BackColor != StandardKnopfFlaeche) return;
            using var r = new Pen(MeldungKnopfRand);
            using var i = new Pen(b.ClientRectangle.Contains(b.PointToClient(Cursor.Position)) ? b.FlatAppearance.MouseOverBackColor : b.BackColor);
            e.Graphics.DrawRectangle(r, 0, 0, b.Width - 1, b.Height - 1);
            e.Graphics.DrawRectangle(i, 1, 1, b.Width - 3, b.Height - 3);
        };
    }

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
        var artTitel = art switch { MeldungsArt.Fehler => "Fehler", MeldungsArt.Frage => "Frage", _ => "Hinweis" };
        if (string.IsNullOrWhiteSpace(titel) || titel.Trim() == "UntPlan") titel = "UntPlan - " + artTitel;
        // Titel passend zur Art: eine Meldung ohne Frage (z. B. „Datenübertragung erfolgreich“) heißt nie „Frage“
        else if (art != MeldungsArt.Frage && titel.Trim() == "UntPlan - Frage") titel = "UntPlan - " + artTitel;
        var f = new MeldungsForm { Text = titel, Art = art };
        var liste = knoepfe.Where(k => !abbrechen || k != "Abbrechen").ToList();
        var alle = abbrechen ? liste.Append("Abbrechen").ToList() : liste;
        // Maße wie im Vorbild (gemessen): Titelleiste 30, Knopfleiste 54 samt Linie, Knöpfe 88 × 26 im Abstand von 7 Pixeln, 13 Pixel
        // unter der Linie und 14 vor dem rechten Rand; Symbol (32) 26 Pixel unter der Titelleiste, Text ab 64 Pixeln
        const int rand = 1, titelHoehe = MeldungsForm.TitelHoehe, leiste = 54, textLinks = 64, knopfHoehe = 26, knopfAbstand = 7;
        int KnopfBreite(string k) => Math.Max(88, TextRenderer.MeasureText(k, Schrift).Width + 22);
        var knopfSumme = alle.Sum(k => KnopfBreite(k) + knopfAbstand) - knopfAbstand + 28;
        var normal = text.Replace("\r\n", "\n").Trim('\n');
        var einzeilig = TextRenderer.MeasureText(normal, Schrift, new Size(int.MaxValue, 0), TextFormatFlags.TextBoxControl);
        var maxText = 460;
        var textBreite = Math.Min(maxText, einzeilig.Width + 8);
        // Vorbild: rechts neben der längsten Zeile 21 Pixel bis zum Rand
        var breite = Math.Clamp(Math.Max(textLinks + textBreite + 3, knopfSumme), 206, 900);
        textBreite = breite - textLinks - 3;
        var groesse = TextRenderer.MeasureText(normal, Schrift, new Size(textBreite, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var hoehe = Math.Clamp(groesse.Height + 4, 16, 440);
        // kurze Meldungen mittig in der Mindestfläche; längere wie im Vorbild 45 Pixel unter der Titelleiste und 27 über der Knopfleiste
        var flaeche = Math.Max(84, hoehe + 63);
        var oben = flaeche == 84 ? (flaeche - hoehe) / 2 : 41;
        var label = new TextBox
        {
            Text = normal.Replace("\n", Environment.NewLine), Left = rand + textLinks - 4, Top = rand + titelHoehe + oben, Width = textBreite, Height = hoehe,
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = MeldungFlaeche, ForeColor = Color.Black, TabStop = false, Font = Schrift,
            ScrollBars = groesse.Height + 4 > 440 ? ScrollBars.Vertical : ScrollBars.None, Name = "Meldungstext",
        };
        f.Padding = new Padding(rand);
        f.ClientSize = new Size(breite + 2 * rand, titelHoehe + flaeche + leiste + 2 * rand);
        f.SymbolBereich = new Rectangle(rand + 19, rand + titelHoehe + 26, 32, 32);
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
                Text = t, Left = x, Top = 13, Width = w, Height = knopfHoehe, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.Black,
                Font = Schrift, UseVisualStyleBackColor = false,
                Enabled = istAbbrechen || aktiv is null || index >= aktiv.Count || aktiv[index],
            };
            b.FlatAppearance.BorderColor = Steuerelemente.Erscheinungsbild.KnopfRand;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = BlauHell;
            b.Click += (_, _) => { ergebnis.Value = istAbbrechen ? null : index; f.DialogResult = istAbbrechen ? DialogResult.Cancel : DialogResult.OK; };
            unten.Controls.Add(b);
            if (istAbbrechen) f.CancelButton = b;
            else if (!abbrechen && pos == liste.Count - 1) f.CancelButton = b;
            if (!istAbbrechen && index == vorgabe && b.Enabled) vorgabeKnopf = b;
            x -= knopfAbstand;
        }
        vorgabeKnopf ??= unten.Controls.OfType<Button>().Where(b => b.Enabled && b != f.CancelButton).LastOrDefault() ?? unten.Controls.OfType<Button>().FirstOrDefault();
        if (vorgabeKnopf != null)
        {
            // Standardknopf wie im Vorbild: hell gefüllt mit dunklem Rand in der Modusfarbe (keine fette Schrift)
            StandardKnopf(vorgabeKnopf);
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
        /// <summary>Aktives Fenster (dunkle Titelleiste wie im Vorbild); inaktiv hell.</summary>
        public bool Aktiv { get; private set; }
        /// <summary>Farbe der eigenen Titelleiste: aktiv dunkel, inaktiv hell in der Modusfarbe (wie im Vorbild).</summary>
        public Color TitelFarbe => Aktiv ? MeldungTitelAktiv : MeldungTitel;
        private static readonly Font TitelSchrift = new("Segoe UI", 8.25f);

        /// <summary>Bereich des Schließen-Knopfes (×) in der Titelleiste.</summary>
        internal Rectangle SchliessenBereich => new(ClientSize.Width - 1 - SchliessenRechts - SchliessenBreite / 2, 1, SchliessenBreite, TitelHoehe);
        /// <summary>Mitte des × wie bei den Fenstern des Vorbilds (Bild 344): 30 Pixel vor der rechten und 14 unter der oberen Randlinie.</summary>
        private const int SchliessenRechts = 30, SchliessenOben = 14;

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); Aktiv = true; RandfarbeSetzen(); Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Aktiv = false; RandfarbeSetzen(); Invalidate(); }

        /// <summary>Windows 11 zeichnet um das Fenster eine eigene Randlinie: in der Farbe des Vorbilds (aktiv dunkelgrün, sonst grau).</summary>
        private void RandfarbeSetzen()
        {
            if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            try
            {
                var c = Aktiv ? MeldungKnopfRand : MeldungRahmen;
                var farbe = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref farbe, 4);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Raender { public int L, R, O, U; }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Raender r);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int wert, int groesse);

        /// <summary>
        /// Weicher Schatten rundum wie im Vorbild (Fensterschatten des Systems auch ohne Rahmen). Kein zusätzlicher Klassenschatten
        /// (CS_DROPSHADOW): der läge als harter grauer Streifen rechts und unten neben dem weichen Schatten (Bild 345, Ecke neben OK).
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return;
            try
            {
                var an = 2; // DWMNCRP_ENABLED
                DwmSetWindowAttribute(Handle, 2, ref an, 4);
                var r = new Raender { L = 1, R = 1, O = 1, U = 1 };
                DwmExtendFrameIntoClientArea(Handle, ref r);
                RandfarbeSetzen();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }

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
            using (var b = new SolidBrush(TitelFarbe)) g.FillRectangle(b, titel);
            if (_schliessenHover)
                using (var rot = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillRectangle(rot, SchliessenBereich);
            // UntPlan-Logo oben links wie in allen Fenstern
            g.DrawIconUnstretched(Symbole.LogoKlein, new Rectangle(9, 1 + (TitelHoehe - 16) / 2, 16, 16));
            TextRenderer.DrawText(g, Text, TitelSchrift, new Rectangle(31, 5, Math.Max(0, SchliessenBereich.X - 31), TitelHoehe - 2), Aktiv ? Color.White : Color.Black,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            // × wie bei den Fenstern des Vorbilds (9 × 9 mit weichen Kanten), Mitte 30 Pixel vor der rechten und 14 unter der oberen Randlinie
            var grund = _schliessenHover ? Color.FromArgb(232, 17, 35) : TitelFarbe;
            Steuerelemente.FensterTitel.GlypheZeichnen(g, 2, new Point(w - 1 - SchliessenRechts, SchliessenOben), _schliessenHover || Aktiv ? Color.White : Color.FromArgb(72, 72, 72), grund);
            using (var r = new Pen(Aktiv ? MeldungKnopfRand : MeldungRahmen)) g.DrawRectangle(r, 0, 0, w - 1, ClientSize.Height - 1);
            if (!SymbolBereich.IsEmpty) SymbolZeichnen(g, SymbolBereich, Art);
        }
    }

    /// <summary>
    /// Symbol der Meldung (FatCow, 32 px): Warnung „error“ (gelbes Warndreieck), Fehler „cancel“ (rotes ×), Frage „help“ (blaues ?),
    /// Info „information“ (blaues i).
    /// </summary>
    internal static void SymbolZeichnen(Graphics g, Rectangle r, MeldungsArt art)
    {
        var name = art switch { MeldungsArt.Warnung => "warnung", MeldungsArt.Fehler => "fehler", MeldungsArt.Frage => "frage", _ => "info" };
        g.DrawImage(Symbole.Get(name, r.Width), r.X, r.Y, r.Width, r.Height);
    }
}
