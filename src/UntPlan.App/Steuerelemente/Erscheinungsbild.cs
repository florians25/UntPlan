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
    /// <summary>Rand der flachen Knöpfe (wie im Meldungsfenster).</summary>
    public static readonly Color KnopfRand = Color.FromArgb(173, 173, 173);

    private static readonly ConditionalWeakTable<Control, object> Gestaltet = new();

    /// <summary>Menüs und Aufklapplisten aller Werkzeugleisten ohne eigene Darstellung.</summary>
    public static void Einschalten() => ToolStripManager.Renderer = new MenueDarstellung();

    /// <summary>Hat das Fenster eine eigene, vollständige Gestaltung (dann keine Dialogfarben)?</summary>
    private static bool EigeneGestaltung(Form f) =>
        f.IsMdiContainer || f.IsMdiChild || f is Fenster.MdiFenster || f.FormBorderStyle == FormBorderStyle.None ||
        f.GetType().Name is "HauptFenster" or "MeldungsForm" or "ElementRollup" or "SymbolForm" or "MonitorFenster" or "HilfeFenster" or "EingabeHinweisFenster";

    /// <summary>Dialogfläche und Titelleiste eines frei schwebenden Fensters (Dialog, Werkzeugfenster) setzen.</summary>
    public static void Dialog(Form f)
    {
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
        if (!b.UseVisualStyleBackColor && b.BackColor != SystemColors.Control && b.BackColor != Color.Empty) return;
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
        b.FlatAppearance.BorderColor = standard ? Ui.MeldungKnopfRand : KnopfRand;
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
                case Form f: Dialog(f); break;
                case Button b: Knopf(b); break;
                case TabPage p: Karte(p); break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException) { }
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
