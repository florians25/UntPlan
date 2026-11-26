using Krypton.Docking;
using Krypton.Navigator;
using Krypton.Toolkit;
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
        haupt.FormClosed += (_, _) => { _manager?.Dispose(); _manager = null; _haupt = null; Seiten.Clear(); };
        return _manager;
    }

    private static bool _ordnet;

    private static bool IstKrypton(Control? c) => c?.GetType().Namespace?.StartsWith("Krypton", StringComparison.Ordinal) == true;

    /// <summary>
    /// Krypton-Randleisten direkt vor den MDI-Arbeitsbereich einreihen (angedockt wird in umgekehrter Reihenfolge der Steuerelemente:
    /// so bleiben Kopf, Menüband und Statuszeile außen und behalten ihren Platz). Die Reihenfolge der Krypton-Leisten untereinander bleibt.
    /// </summary>
    private static void Einordnen(Form haupt)
    {
        if (_ordnet || haupt.IsDisposed || haupt.Controls.OfType<MdiClient>().FirstOrDefault() is not { } mdi) return;
        var kr = haupt.Controls.Cast<Control>().Where(IstKrypton).OrderByDescending(c => haupt.Controls.GetChildIndex(c)).ToList();
        if (kr.Count == 0) return;
        // schon richtig: alle Krypton-Leisten unmittelbar über dem Arbeitsbereich
        var mi = haupt.Controls.GetChildIndex(mdi);
        var soll = Enumerable.Range(mi + 1, kr.Count).ToHashSet();
        if (kr.All(c => soll.Contains(haupt.Controls.GetChildIndex(c)))) return;
        _ordnet = true;
        haupt.SuspendLayout();
        try
        {
            foreach (var c in kr) haupt.Controls.SetChildIndex(c, haupt.Controls.GetChildIndex(mdi) + 1);
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
        // Breite bzw. Höhe der Leiste wie das schwebende Werkzeugfenster
        if (m.FindDockingEdgeDocked(Pfad + "|" + rand) is { } kante)
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
    }

    /// <summary>Seite entfernen; das Werkzeugfenster erscheint wieder als klassisches Fenster.</summary>
    public static void Loesen(string name, bool wiederherstellen = true)
    {
        if (!Seiten.Remove(name, out var s)) return;
        foreach (Control c in s.Seite.Controls.Cast<Control>().ToList()) s.Seite.Controls.Remove(c);
        _manager?.RemovePage(s.Seite, true);
        if (wiederherstellen) s.Zurueck();
    }
}
