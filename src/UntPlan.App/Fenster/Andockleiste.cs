using Krypton.Docking;
using Krypton.Navigator;
using Krypton.Toolkit;
using Krypton.Workspace;
using UntPlan.App.Steuerelemente;

namespace UntPlan.App.Fenster;

/// <summary>
/// Wahlweise Andockleisten für Werkzeugfenster (derzeit das Element-Rollup) mit der Bibliothek Krypton.Docking (BSD-3-Clause):
/// Das Werkzeugfenster wird als Seite an einen Rand des Hauptfensters gedockt (mit Teiler zum Verbreitern, Anheften und automatischem
/// Ausblenden, Schweben als eigenes Fenster, Schließen). Nur auf ausdrücklichen Wunsch (Kontextmenü des Rollups); ohne das bleibt alles
/// wie bisher: frei schwebendes Rollup bzw. das eigene Andocken, und die Arbeitsfenster bleiben klassische, überlappende MDI-Fenster.
/// Krypton zeichnet nur seine eigenen Steuerelemente; Menüs, Leisten und alle übrigen Fenster behalten die eigene Darstellung
/// (<see cref="KryptonPruefung.Abgrenzen"/>).
/// </summary>
public static class Andockleiste
{
    private const string Pfad = "Haupt", PfadSchwebend = "Schwebend";
    private static KryptonDockingManager? _manager;
    private static Form? _haupt;

    /// <summary>Seiten der angedockten Werkzeugfenster (eindeutiger Name → Seite).</summary>
    private static readonly Dictionary<string, (KryptonPage Seite, Action Zurueck)> Seiten = new();

    /// <summary>Andockverwaltung für das Hauptfenster einrichten (einmalig, erst beim ersten Andocken).</summary>
    private static KryptonDockingManager Manager(Form haupt)
    {
        if (_manager != null && ReferenceEquals(_haupt, haupt)) return _manager;
        KryptonPruefung.Abgrenzen();
        _haupt = haupt;
        _manager = new KryptonDockingManager();
        // die Andockränder liegen innerhalb von Kopf, Menüband und Statuszeile: direkt um den MDI-Arbeitsbereich
        _manager.ManageControl(Pfad, haupt);
        _manager.ManageFloating(PfadSchwebend, haupt);
        // Krypton reiht seine Randleisten außen ein (neben Kopf, Menüband und Statuszeile); wie beim eigenen Andocken gehören sie
        // direkt um den Arbeitsbereich: nach jeder Änderung innen einordnen
        haupt.ControlAdded += (_, e) => { if (IstKrypton(e.Control)) Einordnen(haupt); };
        haupt.Layout += (_, _) => Einordnen(haupt);
        Einordnen(haupt);
        _manager.PageCloseRequest += (_, e) =>
        {
            // Schließen der Seite: Werkzeugfenster wieder als klassisches, frei schwebendes Fenster (wie ohne Andockleiste)
            e.CloseRequest = DockingCloseRequest.None;
            if (Seiten.TryGetValue(e.UniqueName, out var s)) haupt.BeginInvoke(() => Loesen(e.UniqueName));
        };
        // eigene Farben nur für die Steuerelemente der Andockleiste (Titel wie die Arbeitsfenster statt Kryptons Grau/Gelb)
        _manager.DockspaceAdding += (_, e) => Gestalten(e.DockspaceControl);
        _manager.DockspaceCellAdding += (_, e) => Gestalten(e.CellControl, kopf: true);
        _manager.DockspaceSeparatorAdding += (_, e) => Gestalten(e.SeparatorControl);
        _manager.FloatspaceAdding += (_, e) => Gestalten(e.FloatspaceControl);
        // schwebend: nur die Titelleiste des Fensters, kein zweiter Kopf in der Zelle
        _manager.FloatspaceCellAdding += (_, e) => Gestalten(e.CellControl, kopf: false);
        _manager.AutoHiddenGroupAdding += (_, e) => Gestalten(e.AutoHiddenGroupControl);
        _manager.FloatingWindowAdding += (_, e) => Schwebefenster(e.FloatingWindow);
        haupt.FormClosed += (_, _) => { _manager?.Dispose(); _manager = null; _haupt = null; Seiten.Clear(); };
        return _manager;
    }

    private static bool _ordnet;

    /// <summary>Titel einer Zelle (angedockt) bzw. des schwebenden Fensters wie die Titelleiste der Arbeitsfenster.</summary>
    private static void Gestalten(Control c, bool kopf = true)
    {
        switch (c)
        {
            case KryptonNavigator n:
                // Kopfstile der Andockzellen (aktiv/inaktiv) über eine eigene Palette nur dieser Zelle, zusätzlich direkt am Steuerelement
                n.LocalCustomPalette = Palette();
                var hg = n.StateCommon.HeaderGroup;
                hg.Border.Color1 = hg.Border.Color2 = Ui.Rasterlinie;
                hg.Back.Color1 = hg.Back.Color2 = Ui.Leiste;
                n.StateCommon.Page.Color1 = n.StateCommon.Page.Color2 = Ui.Leiste;
                if (!kopf) n.Header.HeaderVisiblePrimary = false;
                n.Invalidate(true);
                break;
            case KryptonSeparator s:
                s.StateCommon.Separator.Back.Color1 = s.StateCommon.Separator.Back.Color2 = Ui.MdiHintergrund;
                break;
        }
        if (c is KryptonWorkspace w) w.StateCommon.Back.Color1 = w.StateCommon.Back.Color2 = Ui.MdiHintergrund;
    }

    private static KryptonCustomPaletteBase? _palette;
    private static bool _paletteVertretung;

    /// <summary>Palette der Andockleiste: Kopf aktiv bzw. inaktiv wie die Titelleisten der Arbeitsfenster (Modusfarben).</summary>
    private static KryptonCustomPaletteBase Palette()
    {
        if (_palette != null && _paletteVertretung == Ui.Vertretungsmodus) return _palette;
        _palette ??= new KryptonCustomPaletteBase();
        _paletteVertretung = Ui.Vertretungsmodus;
        foreach (var (h, aktiv) in new[] { (_palette.HeaderStyles.HeaderDockActive, true), (_palette.HeaderStyles.HeaderDockInactive, false) })
        {
            var farbe = FensterTitel.Farbe(aktiv);
            var c = h.StateCommon;
            c.Back.Color1 = c.Back.Color2 = farbe;
            c.Border.Color1 = c.Border.Color2 = farbe;
            c.Content.ShortText.Color1 = c.Content.ShortText.Color2 = Ui.Textfarbe(farbe);
            c.Content.ShortText.Font = Ui.Schrift;
        }
        return _palette;
    }

    /// <summary>Alle Zellen und schwebenden Fenster der Andockleiste (erneut) in den eigenen Farben gestalten.</summary>
    private static void AlleGestalten()
    {
        if (_manager is null) return;
        foreach (var z in _manager.CellsDocked) Gestalten(z, kopf: true);
        foreach (var z in _manager.CellsFloating) Gestalten(z, kopf: false);
        foreach (var f in Application.OpenForms.OfType<KryptonFloatingWindow>().ToList()) Schwebefenster(f, nurFarben: true);
    }

    /// <summary>Schwebendes Fenster: Titel „Element-Rollup“ in der Modusfarbe, Lage wie zuletzt bzw. mittig über dem Hauptfenster.</summary>
    private static void Schwebefenster(KryptonFloatingWindow f, bool nurFarben = false)
    {
        f.Text = Seiten.Count == 1 ? Seiten.Values.First().Seite.Text : "Element-Rollup";
        var h = f.StateCommon.Header;
        h.Back.Color1 = h.Back.Color2 = Ui.Titel;
        h.Border.Color1 = h.Border.Color2 = Ui.Titel;
        h.Content.ShortText.Color1 = h.Content.ShortText.Color2 = Color.White;
        h.Content.ShortText.Font = Ui.Schrift;
        f.StateCommon.Border.Color1 = f.StateCommon.Border.Color2 = Ui.Titel;
        f.StateCommon.Back.Color1 = f.StateCommon.Back.Color2 = Ui.Leiste;
        f.Invalidate(true);
        if (nurFarben) return;
        f.StartPosition = FormStartPosition.Manual;
        void Platzieren()
        {
            if (f.IsDisposed || _haupt is null) return;
            f.Bounds = SchwebeLage(_haupt, f.Size);
        }
        f.Load += (_, _) => Platzieren();
        f.Shown += (_, _) => { Platzieren(); f.BeginInvoke(Platzieren); };
        f.ResizeEnd += (_, _) => LageMerken(f);
        f.Move += (_, _) => { if (f.Visible && Control.MouseButtons == MouseButtons.Left) LageMerken(f); };
    }

    private static void LageMerken(Form f)
    {
        if (f.WindowState != FormWindowState.Normal) return;
        AppZustand.Einstellungen.RollupSchwebend = $"{f.Left},{f.Top},{f.Width},{f.Height}";
    }

    /// <summary>Lage des schwebenden Fensters: zuletzt gemerkt (sofern auf einem Bildschirm sichtbar), sonst mittig über dem Hauptfenster.</summary>
    internal static Rectangle SchwebeLage(Form haupt, Size groesse)
    {
        var teile = AppZustand.Einstellungen.RollupSchwebend.Split(',');
        if (teile.Length == 4 && teile.All(t => int.TryParse(t, out _)))
        {
            var z = teile.Select(int.Parse).ToArray();
            var r = new Rectangle(z[0], z[1], Math.Max(200, z[2]), Math.Max(150, z[3]));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, 30)))) return r;
        }
        var g = new Size(Math.Max(groesse.Width, 320), Math.Max(groesse.Height, 380));
        var m = haupt.RectangleToScreen(haupt.ClientRectangle);
        return new Rectangle(m.Left + (m.Width - g.Width) / 2, m.Top + (m.Height - g.Height) / 2, g.Width, g.Height);
    }

    private static bool IstKrypton(Control? c) => c?.GetType().Namespace?.StartsWith("Krypton", StringComparison.Ordinal) == true;

    /// <summary>
    /// Krypton-Randleisten innen einreihen, direkt am MDI-Arbeitsbereich: Windows Forms dockt Steuerelemente mit höherem Index zuerst
    /// (außen), daher kommen die Krypton-Leisten an den Anfang der Liste. So bleiben Kopf, Menüband und Statuszeile außen und behalten
    /// ihren Platz. Die Reihenfolge der Krypton-Leisten untereinander bleibt erhalten.
    /// </summary>
    private static void Einordnen(Form haupt)
    {
        if (_ordnet || haupt.IsDisposed) return;
        var kr = haupt.Controls.Cast<Control>().Where(IstKrypton).OrderByDescending(c => haupt.Controls.GetChildIndex(c)).ToList();
        if (kr.Count == 0 || kr.All(c => haupt.Controls.GetChildIndex(c) < kr.Count)) return;
        _ordnet = true;
        haupt.SuspendLayout();
        try
        {
            foreach (var c in kr) haupt.Controls.SetChildIndex(c, 0);
        }
        finally
        {
            haupt.ResumeLayout(true);
            _ordnet = false;
        }
    }

    /// <summary>Werkzeugfenster ist über Krypton angedockt?</summary>
    public static bool Angedockt(string name) => Seiten.ContainsKey(name);

    /// <summary>Selbsttest: Seite des Werkzeugfensters (null = nicht angedockt).</summary>
    internal static KryptonPage? Seite(string name) => Seiten.TryGetValue(name, out var s) ? s.Seite : null;

    /// <summary>Selbsttest: Lage der Seite (angedockt, automatisch ausgeblendet, schwebend).</summary>
    internal static string Lage(string name) => _manager != null && Seiten.ContainsKey(name) ? _manager.FindPageLocation(name).ToString() : "";

    /// <summary>
    /// Inhalt eines Werkzeugfensters an einen Rand des Hauptfensters docken. <paramref name="inhalt"/> wird in die Seite verschoben;
    /// <paramref name="zurueck"/> stellt das Werkzeugfenster beim Lösen wieder her.
    /// </summary>
    public static void Andocken(Form haupt, string name, string titel, Control inhalt, DockingEdge rand, int breite, Action zurueck)
    {
        var m = Manager(haupt);
        if (Seiten.ContainsKey(name)) Loesen(name, wiederherstellen: false);
        var seite = new KryptonPage { Text = titel, TextTitle = titel, TextDescription = titel, UniqueName = name, Font = Ui.Schrift };
        seite.ClearFlags(KryptonPageFlags.DockingAllowWorkspace | KryptonPageFlags.DockingAllowNavigator);
        inhalt.Dock = DockStyle.Fill;
        seite.Controls.Add(inhalt);
        Seiten[name] = (seite, zurueck);
        m.AddDockspace(Pfad, rand, new[] { seite });
        Einordnen(haupt);
        AlleGestalten();
        // Breite bzw. Höhe der Leiste wie das schwebende Werkzeugfenster
        if (m.FindDockingEdgeDocked(name) is { } kante)
            foreach (var ds in kante.OfType<KryptonDockingDockspace>())
                if (ds.DockspaceControl is { } c)
                {
                    if (rand is DockingEdge.Left or DockingEdge.Right) c.Width = breite; else c.Height = breite;
                }
    }

    /// <summary>Angedockte Seite schwebend zeigen (eigenes Krypton-Fenster).</summary>
    public static void Schweben(string name)
    {
        if (_manager is null || !Seiten.ContainsKey(name)) return;
        _manager.MakeFloatingRequest(name);
        AlleGestalten();
        AppZustand.Einstellungen.RollupAngedockt = "Leiste:Schwebend";
        AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Seite entfernen; das Werkzeugfenster erscheint wieder als klassisches Fenster.</summary>
    public static void Loesen(string name, bool wiederherstellen = true)
    {
        if (!Seiten.Remove(name, out var s)) return;
        foreach (Control c in s.Seite.Controls.Cast<Control>().ToList()) s.Seite.Controls.Remove(c);
        _manager?.RemovePage(s.Seite, true);
        if (wiederherstellen)
        {
            AppZustand.Einstellungen.RollupAngedockt = "";
            AppZustand.Einstellungen.Speichern();
            s.Zurueck();
        }
    }
}
