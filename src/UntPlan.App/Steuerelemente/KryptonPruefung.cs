using Krypton.Ribbon;
using Krypton.Toolkit;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Abgrenzung der Krypton-Bibliothek zur eigenen Gestaltung und Vergleichsmuster für den Selbsttest.
/// Krypton setzt sonst beim ersten Laden seine Palette als globalen Zeichner aller ToolStrips (Menüs, Kontextmenüs, Leisten);
/// das würde die eigene Darstellung (Erscheinungsbild, LeistenRenderer) überschreiben. <see cref="Abgrenzen"/> verhindert das.
/// </summary>
public static class KryptonPruefung
{
    private static KryptonManager? _manager;

    /// <summary>Krypton darf den globalen ToolStrip-Zeichner nicht ersetzen (vor dem ersten Krypton-Steuerelement aufrufen).</summary>
    public static void Abgrenzen()
    {
        if (_manager != null) return;
        var eigener = ToolStripManager.Renderer;
        _manager = new KryptonManager { GlobalApplyToolstrips = false, ShowAdministratorSuffix = false };
        // falls Krypton beim Laden dennoch umgestellt hat: eigene Darstellung zurück
        if (!ReferenceEquals(ToolStripManager.Renderer, eigener)) ToolStripManager.Renderer = eigener;
    }

    /// <summary>
    /// Vergleichsmuster: das Menüband „Vertretungsplanung“ als KryptonRibbon mit einer an das Vorbild angepassten Palette
    /// (Grundpalette Office 2013 Weiß, Farben aus Bild 308). Dient nur der Entscheidung, ob das eigene Menüband ersetzt werden kann.
    /// </summary>
    public static Form MenuebandMuster()
    {
        Abgrenzen();
        var f = new Form { Text = "Muster Menüband (Krypton)", ClientSize = new Size(1300, 160), StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), ShowInTaskbar = false };
        var r = new KryptonRibbon { PaletteMode = PaletteMode.Office2013White, QATLocation = QATLocation.Hidden };
        var c = r.StateCommon;
        c.RibbonGeneral.TextFont = Ui.Schrift;
        c.RibbonGroupButtonText.TextColor = Color.Black;
        c.RibbonGroupNormalBorder.BackColor1 = Ui.Menueband;
        c.RibbonGroupNormalBorder.BackColor2 = Ui.Menueband;
        c.RibbonGroupNormalTitle.BackColor1 = Ui.Menueband;
        c.RibbonGroupNormalTitle.BackColor2 = Ui.Menueband;
        c.RibbonTab.BackColor1 = Ui.Menueband;
        c.RibbonTab.BackColor2 = Ui.Menueband;
        var tab = new KryptonRibbonTab { Text = "Vertretungsplanung" };
        r.RibbonTabs.Add(tab);
        KryptonRibbonGroupButton Gross(string text, string symbol, bool pfeil = true) => new()
        {
            TextLine1 = text, ImageLarge = Symbole.Get(symbol, 32), ImageSmall = Symbole.Get(symbol, 16),
            ButtonType = pfeil ? GroupButtonType.Split : GroupButtonType.Push, KeyTip = text[..1].ToUpperInvariant(),
        };
        KryptonRibbonGroup Gruppe(string titel, bool starter, params KryptonRibbonGroupButton[] knoepfe)
        {
            var g = new KryptonRibbonGroup { TextLine1 = titel, DialogBoxLauncher = starter };
            var t = new KryptonRibbonGroupTriple();
            foreach (var k in knoepfe) t.Items.Add(k);
            g.Items.Add(t);
            tab.Groups.Add(g);
            return g;
        }
        Gruppe("Kalender", false, Gross("Kalender", "kalender"));
        Gruppe("Vertretungsplanung", false, Gross("Absenzen", "absenz"), Gross("Vertretungen", "vertretung"), Gross("Ausgabe", "drucken"));
        Gruppe("", false, Gross("Vertretungsstatistik", "statistik", false));
        var ereignisse = new KryptonRibbonGroup { TextLine1 = "Ereignisse" };
        var lines = new KryptonRibbonGroupLines();
        foreach (var (t, s) in new[] { ("Veranstaltungen", "veranstaltung"), ("Sondereinsätze", "sondereinsatz"), ("Bereitschaften", "bereitschaft") })
            lines.Items.Add(new KryptonRibbonGroupButton { TextLine1 = t, ImageSmall = Symbole.Get(s, 16), ImageLarge = Symbole.Get(s, 32) });
        ereignisse.Items.Add(lines);
        tab.Groups.Add(ereignisse);
        Gruppe("Planung", false, Gross("Planungsdialog", "planungsdialog"), Gross("Vormerkungen", "vormerkung"), Gross("Pausenaufsichten", "pausenaufsicht"));
        Gruppe("Modus", false, Gross("Vertretungsplanung", "vertretungsmodus", false));
        f.Controls.Add(r);
        return f;
    }

    /// <summary>Vergleichsmuster: KryptonDataGridView mit Spalten und Zeilen wie die Vertretungsliste, Kopf in den Modusfarben.</summary>
    public static Form TabellenMuster()
    {
        Abgrenzen();
        var f = new Form { Text = "Muster Tabelle (Krypton)", ClientSize = new Size(700, 220), StartPosition = FormStartPosition.Manual, Location = new Point(0, 180), ShowInTaskbar = false };
        var g = new KryptonDataGridView { Dock = DockStyle.Fill, PaletteMode = PaletteMode.Office2013White, RowHeadersVisible = false, AllowUserToAddRows = false };
        g.StateCommon.HeaderColumn.Back.Color1 = Ui.Kopf;
        g.StateCommon.HeaderColumn.Content.Font = Ui.Schrift;
        g.StateCommon.DataCell.Content.Font = Ui.Schrift;
        foreach (var s in new[] { "Vtr-Nr.", "Art", "Stunde", "Klasse", "(Lehrer)", "Vertreter", "(Raum)", "Raum", "(Fach)", "Fach", "Text" })
            g.Columns.Add(s, s);
        g.Rows.Add("280", "Pausenaufsicht", "2/3", "", "KRU", "???", "1.OG", "1.OG", "", "", "");
        g.Rows.Add("277", "Vertretung", "1", "6a", "SCH", "???", "R103", "R103", "M", "", "");
        g.Rows.Add("268", "Freisetzung", "1", "(7a)", "SWA", "SWA", "R105", "---", "F", "---", "Exkursion");
        f.Controls.Add(g);
        return f;
    }
}
