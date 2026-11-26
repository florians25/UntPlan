using System.Runtime.CompilerServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Einheitliches Erscheinungsbild aller Fenster und Dialoge (beide Modi, Farben aus <see cref="Ui"/>), abgeleitet aus den
/// Vorbild-Bildschirmfotos der Vertretungsplanung und dem Meldungsfenster:
/// <list type="bullet">
/// <item>Dialoge: Fläche in der Listenfarbe des Modus (statt Windows-Grau), helle Titelleiste in der Modusfarbe mit schwarzer Schrift.</item>
/// <item>Knöpfe: flach, weiß, 1 Pixel grauer Rand (173,173,173), Überfahren in der Hover-Farbe, gedrückt hell in der Modusfarbe;
/// der Standardknopf (Enter) mit dunklem Rand in der Modusfarbe, nie fett.</item>
/// <item>Registerkarten in Dialogen: Seiten in der Dialogfläche.</item>
/// <item>Kontext- und Aufklappmenüs: weiß, Symbolspalte in der Kopffarbe, Markierung in der Hover-Farbe mit Rand, Trennlinien in der Rasterfarbe.</item>
/// </list>
/// Wird über <see cref="Auswahlbreite"/> für jedes Fenster und jedes später hinzugefügte Steuerelement angewandt; Fenster mit
/// eigener Gestaltung (Hauptfenster, Arbeitsfenster-Rahmen, Meldungsfenster, Element-Rollup, Monitor, Hilfe) bleiben unberührt.
/// </summary>
public static class Erscheinungsbild
{
    /// <summary>Rand der flachen Knöpfe (wie im Meldungsfenster, gemessen (171,171,171)).</summary>
    public static readonly Color KnopfRand = Color.FromArgb(171, 171, 171);

    private static readonly ConditionalWeakTable<Control, object> Gestaltet = new();

    /// <summary>Menüs und Aufklapplisten aller Werkzeugleisten ohne eigene Darstellung.</summary>
    public static void Einschalten()
    {
        ToolStripManager.Renderer = new MenueDarstellung();
        StandardSymbolSetzen();
    }

    /// <summary>Hat das Fenster eine eigene, vollständige Gestaltung (dann keine Dialogfarben)?</summary>
    private static bool EigeneGestaltung(Form f) =>
        f.IsMdiContainer || f.IsMdiChild || f is Fenster.MdiFenster || f.FormBorderStyle == FormBorderStyle.None ||
        f.GetType().Name is "HauptFenster" or "MeldungsForm" or "ElementRollup" or "SymbolForm" or "MonitorFenster" or "HilfeFenster" or "EingabeHinweisFenster";

    /// <summary>
    /// Fenstersymbol: jedes Fenster und jeder Dialog zeigt oben links das UntPlan-Logo (auch Dialoge mit festem Rahmen, die Windows
    /// sonst ohne Symbol zeigt). Werkzeugfenster haben unter Windows keinen Platz für ein Symbol; Fenster mit eigener Titelleiste
    /// zeichnen das Logo selbst.
    /// </summary>
    public static void Fenstersymbol(Form f)
    {
        if (f.IsDisposed) return;
        try
        {
            if (!ReferenceEquals(f.Icon, Symbole.Logo)) f.Icon = Symbole.Logo;
            if (f.FormBorderStyle is FormBorderStyle.None or FormBorderStyle.FixedToolWindow or FormBorderStyle.SizableToolWindow) return;
            if (!f.ShowIcon) f.ShowIcon = true;
            if (!OperatingSystem.IsWindows() || SymbolAngemeldet.TryGetValue(f, out _)) return;
            SymbolAngemeldet.Add(f, new object());
            if (f.IsHandleCreated) { SymbolWaechter.Anhaengen(f); SymbolErzwingen(f); }
            f.HandleCreated += (_, _) => { SymbolWaechter.Anhaengen(f); SymbolErzwingen(f); };
            f.Shown += (_, _) => SymbolErzwingen(f);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException) { }
    }

    private static readonly ConditionalWeakTable<Form, object> SymbolAngemeldet = new();

    /// <summary>
    /// Dialoge mit festem Rahmen (und Fenster ohne Symbol) erhalten von Windows Forms den Stil WS_EX_DLGMODALFRAME, mit dem Windows
    /// kein Symbol in der Titelleiste zeigt: Stil entfernen, Rahmen neu berechnen und das Logo ausdrücklich setzen.
    /// </summary>
    private static void SymbolErzwingen(Form f)
    {
        if (f.IsDisposed || !f.IsHandleCreated || f.FormBorderStyle is FormBorderStyle.None or FormBorderStyle.FixedToolWindow or FormBorderStyle.SizableToolWindow) return;
        try
        {
            const int GWL_EXSTYLE = -20, WS_EX_DLGMODALFRAME = 0x1, WM_SETICON = 0x80;
            var ex = GetWindowLong(f.Handle, GWL_EXSTYLE);
            if ((ex & WS_EX_DLGMODALFRAME) != 0)
            {
                SetWindowLong(f.Handle, GWL_EXSTYLE, ex & ~WS_EX_DLGMODALFRAME);
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); // NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED
            }
            SendMessage(f.Handle, WM_SETICON, (IntPtr)0, Symbole.LogoKlein.Handle);
            SendMessage(f.Handle, WM_SETICON, (IntPtr)1, Symbole.Logo.Handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Windows Forms setzt den Dialograhmenstil bei jeder Stilaktualisierung (Anzeigen, Text, Knöpfe) neu: die Änderung abfangen
    /// (WM_STYLECHANGING) und WS_EX_DLGMODALFRAME herausnehmen, damit das Logo in der Titelleiste bleibt.
    /// </summary>
    private sealed class SymbolWaechter : NativeWindow
    {
        private static readonly ConditionalWeakTable<Form, SymbolWaechter> Aktiv = new();

        public static void Anhaengen(Form f)
        {
            if (!f.IsHandleCreated) return;
            if (Aktiv.TryGetValue(f, out var alt))
            {
                if (alt.Handle == f.Handle) return;
                Aktiv.Remove(f);
            }
            var w = new SymbolWaechter();
            w.AssignHandle(f.Handle);
            f.HandleDestroyed += (_, _) => w.ReleaseHandle();
            Aktiv.Add(f, w);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct StilWechsel { public int Alt, Neu; }

        protected override void WndProc(ref Message m)
        {
            const int WM_STYLECHANGING = 0x7C, GWL_EXSTYLE = -20;
            if (m.Msg == WM_STYLECHANGING && (int)(long)m.WParam == GWL_EXSTYLE && m.LParam != IntPtr.Zero)
            {
                var s = System.Runtime.InteropServices.Marshal.PtrToStructure<StilWechsel>(m.LParam);
                if ((s.Neu & 0x1) != 0)
                {
                    s.Neu &= ~0x1;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(s, m.LParam, false);
                }
            }
            base.WndProc(ref m);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int wert);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr nach, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Voreingestelltes Symbol aller Fenster (statt des Windows-Forms-Symbols) auf das UntPlan-Logo setzen.</summary>
    public static void StandardSymbolSetzen()
    {
        foreach (var name in new[] { "s_defaultIcon", "defaultIcon" })
            if (typeof(Form).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is { } feld && feld.FieldType == typeof(Icon))
            {
                try { feld.SetValue(null, Symbole.Logo); } catch (Exception ex) when (ex is FieldAccessException or ArgumentException) { }
                return;
            }
    }

    /// <summary>Dialogfläche und Titelleiste eines frei schwebenden Fensters (Dialog, Werkzeugfenster) setzen.</summary>
    public static void Dialog(Form f)
    {
        Fenstersymbol(f);
        if (f.IsDisposed || EigeneGestaltung(f)) return;
        if (Gestaltet.TryGetValue(f, out _)) return;
        Gestaltet.Add(f, new object());
        if (f.BackColor == SystemColors.Control) f.BackColor = Ui.DialogFlaeche;
        if (f.IsHandleCreated) Titelleiste(f);
        f.HandleCreated += (_, _) => Titelleiste(f);
        // Knöpfe und Registerkarten schon vor dem ersten Zeichnen (nicht erst im nächsten Leerlauf: kein Aufblitzen grauer Knöpfe)
        f.Load += (_, _) => Unterelemente(f);
    }

    private static void Unterelemente(Control c)
    {
        foreach (Control k in c.Controls)
        {
            Anwenden(k);
            Unterelemente(k);
        }
    }

    private static void Titelleiste(Form f) => Ui.TitelleisteFaerben(f, false);

    /// <summary>Gehört das Steuerelement zu einem Fenster mit eigener Gestaltung?</summary>
    private static bool InEigenemFenster(Control c) => c.FindForm() is { } f && f.GetType().Name is "MeldungsForm" or "MonitorFenster" or "HilfeFenster";

    /// <summary>Flacher Knopf wie im Meldungsfenster (nur unveränderte Windows-Knöpfe).</summary>
    public static void Knopf(Button b)
    {
        if (b.IsDisposed || b.GetType() != typeof(Button) || b.FlatStyle != FlatStyle.Standard) return;
        // eigene Farbe gesetzt (Farbknöpfe, Signalknöpfe)? Eine nur von der Dialogfläche geerbte Farbe zählt nicht
        if (!b.UseVisualStyleBackColor && b.BackColor != SystemColors.Control && b.BackColor != Color.Empty && b.BackColor != b.Parent?.BackColor) return;
        if (Gestaltet.TryGetValue(b, out _) || InEigenemFenster(b)) return;
        Gestaltet.Add(b, new object());
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.BackColor = Color.White;
        if (b.ForeColor == SystemColors.ControlText) b.ForeColor = Color.Black;
        b.FlatAppearance.BorderSize = 1;
        RandSetzen(b);
        b.FlatAppearance.MouseOverBackColor = Ui.Hover;
        b.FlatAppearance.MouseDownBackColor = Ui.BlauHell;
        if (b.Font.Bold && b.Font.Name == Ui.Schrift.Name) b.Font = Ui.Schrift;
        // Standardknopf erst nach dem Aufbau des Fensters bekannt; Modusfarben beim Anzeigen erneuern
        b.VisibleChanged += (_, _) => { if (b.Visible) RandSetzen(b); };
        b.ParentChanged += (_, _) => RandSetzen(b);
    }

    private static void RandSetzen(Button b)
    {
        if (b.IsDisposed) return;
        var standard = b.FindForm() is { } f && ReferenceEquals(f.AcceptButton, b);
        // Standardknopf wie im Vorbild (Meldungsfenster): hell gefüllt in der Modusfarbe, 1 Pixel dunkler Rand
        if (standard) Ui.StandardKnopf(b);
        else
        {
            if (b.BackColor == Ui.StandardKnopfFlaeche) b.BackColor = Color.White;
            b.FlatAppearance.BorderColor = KnopfRand;
        }
        b.FlatAppearance.MouseOverBackColor = Ui.Hover;
        b.FlatAppearance.MouseDownBackColor = Ui.BlauHell;
    }

    /// <summary>Registerkarte eines Dialogs in der Dialogfläche.</summary>
    public static void Karte(TabPage p)
    {
        if (p.IsDisposed || Gestaltet.TryGetValue(p, out _)) return;
        if (p.FindForm() is not { } f || EigeneGestaltung(f) || f.IsMdiChild) return;
        if (!p.UseVisualStyleBackColor && p.BackColor != SystemColors.Control && p.BackColor != Color.Transparent) return;
        Gestaltet.Add(p, new object());
        p.UseVisualStyleBackColor = false;
        p.BackColor = f.BackColor == SystemColors.Control ? Ui.DialogFlaeche : f.BackColor;
    }

    /// <summary>Gemeinsame Gestaltung für ein neu erfasstes Steuerelement.</summary>
    internal static void Anwenden(Control c)
    {
        try
        {
            switch (c)
            {
                case Form f: Fenstersymbol(f); Dialog(f); break;
                case Button b: Knopf(b); break;
                case TabPage p: Karte(p); break;
                case DataGridView g when g.BorderStyle == BorderStyle.Fixed3D: KlassischVertieft.Anmelden(g); break;
                case GroupBox gb when gb.FlatStyle == FlatStyle.Standard: Gruppenrahmen(gb); break;
                case CheckBox cb when cb.Appearance == Appearance.Normal && cb.FlatStyle == FlatStyle.Standard: Kontrollkaestchen(cb); break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException) { }
    }

    /// <summary>
    /// Kontrollkästchen wie im Vorbild (gemessen, „Offene Vertretungen“): flaches Quadrat 11 × 11 Pixel, Rand (171,171,171), weiß
    /// gefüllt – statt des größeren, abgerundeten Windows-Kästchens.
    /// </summary>
    private static void Kontrollkaestchen(CheckBox cb)
    {
        if (Gestaltet.TryGetValue(cb, out _) || InEigenemFenster(cb)) return;
        Gestaltet.Add(cb, new object());
        cb.FlatStyle = FlatStyle.Flat;
        cb.FlatAppearance.BorderColor = Color.FromArgb(171, 171, 171);
        // Windows zeichnet das flache Kästchen schwarz umrandet und getönt: wie im Vorbild weiß mit grauem Rand, Text 14 Pixel
        // nach dem Kästchenrand (gemessen in Bild 308)
        if (cb.CheckAlign != ContentAlignment.MiddleLeft || cb.RightToLeft == RightToLeft.Yes) return;
        cb.Padding = new Padding(cb.Padding.Left, cb.Padding.Top, Math.Max(cb.Padding.Right, 4), cb.Padding.Bottom);
        cb.Paint += (_, e) =>
        {
            var g = e.Graphics;
            var grund = cb.BackColor.A == 255 ? cb.BackColor : cb.Parent?.BackColor ?? SystemColors.Control;
            using (var b = new SolidBrush(grund)) g.FillRectangle(b, cb.ClientRectangle);
            var box = new Rectangle(0, (cb.ClientSize.Height - 11) / 2, 11, 11);
            using (var weiss = new SolidBrush(cb.Enabled ? Color.White : Color.FromArgb(244, 244, 244))) g.FillRectangle(weiss, box);
            using (var p = new Pen(Color.FromArgb(171, 171, 171))) g.DrawRectangle(p, box.X, box.Y, 10, 10);
            if (cb.CheckState != CheckState.Unchecked)
            {
                var farbe = cb.Enabled ? Color.Black : Color.FromArgb(160, 160, 160);
                if (cb.CheckState == CheckState.Indeterminate) using (var b = new SolidBrush(farbe)) g.FillRectangle(b, box.X + 3, box.Y + 3, 5, 5);
                else
                    using (var b = new SolidBrush(farbe))
                        for (var i = 0; i < 7; i++)
                        {
                            // klassisches Häkchen 7 × 7, drei Pixel hoch
                            var y = i < 3 ? box.Y + 4 + i : box.Y + 6 - (i - 2);
                            g.FillRectangle(b, box.X + 2 + i, y, 1, 3);
                        }
            }
            var textFarbe = cb.Enabled ? cb.ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(g, cb.Text, cb.Font, new Rectangle(14, 0, Math.Max(0, cb.ClientSize.Width - 14), cb.ClientSize.Height), textFarbe,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | (cb.UseMnemonic ? 0 : TextFormatFlags.NoPrefix));
        };
    }

    /// <summary>
    /// Rahmen der Gruppenfelder wie im Vorbild (gemessen, „Von-Bis“): Linie (213,213,213) mit abgerundeten Ecken (2 Pixel) statt der
    /// eckigen hellgrauen Linie von Windows; Beschriftung unverändert. Windows zeichnet zuerst, danach wird nur die Linie ersetzt.
    /// </summary>
    private static void Gruppenrahmen(GroupBox gb)
    {
        if (Gestaltet.TryGetValue(gb, out _) || InEigenemFenster(gb)) return;
        Gestaltet.Add(gb, new object());
        gb.Paint += (_, e) =>
        {
            if (gb.FlatStyle != FlatStyle.Standard || gb.Width < 8 || gb.Height < 8) return;
            var g = e.Graphics;
            var oben = gb.Font.Height / 2;
            // Windows zeichnet die Unterkante eine Zeile über dem unteren Rand: dort liegt auch die neue Linie
            int r = gb.Width - 1, u = gb.Height - 2;
            var text = string.IsNullOrEmpty(gb.Text) ? 0 : TextRenderer.MeasureText(g, gb.Text, gb.Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            int textVon = 7, textBis = text == 0 ? 7 : 8 + text + 1;
            var grund = gb.BackColor.A == 255 ? gb.BackColor : Ui.Kopf;
            using (var weg = new Pen(grund))
            {
                g.DrawLine(weg, 0, oben, textVon - 1, oben);
                g.DrawLine(weg, textBis, oben, r, oben);
                g.DrawLine(weg, 0, oben, 0, u + 1); g.DrawLine(weg, r, oben, r, u + 1); g.DrawLine(weg, 0, u, r, u); g.DrawLine(weg, 0, u + 1, r, u + 1);
            }
            using var p = new Pen(Color.FromArgb(213, 213, 213));
            g.DrawLine(p, 2, oben, textVon - 2, oben);
            if (textBis + 1 <= r - 2) g.DrawLine(p, textBis + 1, oben, r - 2, oben);
            g.DrawLine(p, 0, oben + 2, 0, u - 2); g.DrawLine(p, r, oben + 2, r, u - 2);
            g.DrawLine(p, 2, u, r - 2, u);
            using var punkt = new SolidBrush(p.Color);
            foreach (var (x, y) in new[] { (1, oben + 1), (r - 1, oben + 1), (1, u - 1), (r - 1, u - 1) }) g.FillRectangle(punkt, x, y, 1, 1);
        };
        gb.Invalidate();
    }

    /// <summary>
    /// Vertiefter Tabellenrahmen wie im Vorbild (gemessen): klassisch zweifarbig – oben/links außen (160,160,160), innen (105,105,105),
    /// unten/rechts außen (255,255,255), innen (227,227,227) – statt der einfarbigen Linie, die Windows mit Designs zeichnet.
    /// </summary>
    internal sealed class KlassischVertieft : NativeWindow
    {
        private static readonly ConditionalWeakTable<Control, KlassischVertieft?> Angemeldet = new();
        private readonly Control _c;

        private KlassischVertieft(Control c)
        {
            _c = c;
            if (c.IsHandleCreated) { AssignHandle(c.Handle); Neuzeichnen(); }
            c.HandleCreated += (_, _) => { if (Handle == IntPtr.Zero) AssignHandle(c.Handle); };
            c.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        public static void Anmelden(Control c)
        {
            if (!OperatingSystem.IsWindows() || Angemeldet.TryGetValue(c, out _)) return;
            if (c is DataGridView g)
            {
                // die Tabelle zeichnet ihren Rahmen mit Designs selbst (1 Pixel) in der Clientfläche: danach klassisch (2 Pixel) übermalen;
                // die überdeckte oberste Zeile der Überschrift kommt als zusätzliches Pixel dazu (Trennlinien wie im Vorbild)
                Angemeldet.Add(c, null!);
                g.Paint += (_, e) => { if (g.BorderStyle == BorderStyle.Fixed3D) Zeichnen(e.Graphics, g.Width, g.Height); };
                if (Application.RenderWithVisualStyles)
                {
                    var gesetzt = -1;
                    void Kopf()
                    {
                        if (g.BorderStyle != BorderStyle.Fixed3D || g.ColumnHeadersHeight == gesetzt || g.ColumnHeadersHeightSizeMode == DataGridViewColumnHeadersHeightSizeMode.AutoSize) return;
                        gesetzt = g.ColumnHeadersHeight + 1;
                        g.ColumnHeadersHeight = gesetzt;
                    }
                    Kopf();
                    g.ColumnHeadersHeightChanged += (_, _) => Kopf();
                }
                g.Invalidate();
                return;
            }
            Angemeldet.Add(c, new KlassischVertieft(c));
        }

        private static void Zeichnen(Graphics g, int w, int h)
        {
            void Linie(Color c, int x1, int y1, int x2, int y2) { using var p = new Pen(c); g.DrawLine(p, x1, y1, x2, y2); }
            Linie(Color.FromArgb(160, 160, 160), 0, 0, w - 2, 0); Linie(Color.FromArgb(160, 160, 160), 0, 0, 0, h - 2);
            Linie(Color.White, 0, h - 1, w - 1, h - 1); Linie(Color.White, w - 1, 0, w - 1, h - 1);
            Linie(Color.FromArgb(105, 105, 105), 1, 1, w - 3, 1); Linie(Color.FromArgb(105, 105, 105), 1, 1, 1, h - 3);
            Linie(Color.FromArgb(227, 227, 227), 1, h - 2, w - 2, h - 2); Linie(Color.FromArgb(227, 227, 227), w - 2, 1, w - 2, h - 2);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hwnd, IntPtr r, IntPtr rgn, uint flags);

        private void Neuzeichnen()
        {
            try { RedrawWindow(_c.Handle, IntPtr.Zero, IntPtr.Zero, 0x0400 | 0x0001 /* RDW_FRAME | RDW_INVALIDATE */); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x85 /* WM_NCPAINT */ || _c is DataGridView { BorderStyle: not BorderStyle.Fixed3D }) return;
            try
            {
                var dc = GetWindowDC(m.HWnd);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using var g = Graphics.FromHdc(dc);
                    Zeichnen(g, _c.Width, _c.Height);
                }
                finally { ReleaseDC(m.HWnd, dc); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }

    /// <summary>Kontext- und Aufklappmenüs in den Modusfarben (Farben werden bei jedem Zeichnen gelesen: Moduswechsel wirkt sofort).</summary>
    private sealed class MenueDarstellung : ToolStripProfessionalRenderer
    {
        public MenueDarstellung() : base(new MenueFarben()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Enabled && e.TextColor != Color.White) e.TextColor = Color.Black;
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenueFarben : ProfessionalColorTable
    {
        public MenueFarben() => UseSystemColors = false;
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Ui.Kopf;
        public override Color ImageMarginGradientMiddle => Ui.Kopf;
        public override Color ImageMarginGradientEnd => Ui.Kopf;
        public override Color MenuBorder => Erscheinungsbild.KnopfRand;
        public override Color MenuItemBorder => Ui.HoverRand;
        public override Color MenuItemSelected => Ui.Hover;
        public override Color MenuItemSelectedGradientBegin => Ui.Hover;
        public override Color MenuItemSelectedGradientEnd => Ui.Hover;
        public override Color MenuItemPressedGradientBegin => Ui.BlauHell;
        public override Color MenuItemPressedGradientMiddle => Ui.BlauHell;
        public override Color MenuItemPressedGradientEnd => Ui.BlauHell;
        public override Color ButtonSelectedHighlight => Ui.Hover;
        public override Color ButtonSelectedHighlightBorder => Ui.HoverRand;
        public override Color ButtonSelectedBorder => Ui.HoverRand;
        public override Color ButtonSelectedGradientBegin => Ui.Hover;
        public override Color ButtonSelectedGradientMiddle => Ui.Hover;
        public override Color ButtonSelectedGradientEnd => Ui.Hover;
        public override Color ButtonPressedGradientBegin => Ui.BlauHell;
        public override Color ButtonPressedGradientMiddle => Ui.BlauHell;
        public override Color ButtonPressedGradientEnd => Ui.BlauHell;
        public override Color ButtonPressedBorder => Ui.HoverRand;
        public override Color ButtonCheckedGradientBegin => Ui.BlauHell;
        public override Color ButtonCheckedGradientMiddle => Ui.BlauHell;
        public override Color ButtonCheckedGradientEnd => Ui.BlauHell;
        public override Color CheckBackground => Ui.BlauHell;
        public override Color CheckSelectedBackground => Ui.Hover;
        public override Color CheckPressedBackground => Ui.BlauHell;
        public override Color SeparatorDark => Ui.Rasterlinie;
        public override Color SeparatorLight => Color.White;
        public override Color ToolStripBorder => Ui.Rasterlinie;
        public override Color ToolStripGradientBegin => Ui.Leiste;
        public override Color ToolStripGradientMiddle => Ui.Leiste;
        public override Color ToolStripGradientEnd => Ui.Leiste;
        public override Color MenuStripGradientBegin => Ui.Leiste;
        public override Color MenuStripGradientEnd => Ui.Leiste;
        public override Color StatusStripGradientBegin => Ui.Kopf;
        public override Color StatusStripGradientEnd => Ui.Kopf;
    }
}
