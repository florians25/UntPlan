using UntPlan.App.Fenster;
using UntPlan.Core;

namespace UntPlan.App;

/// <summary>
/// Arbeitsfenster bleiben im sichtbaren MDI-Arbeitsbereich (unter Programmkopf und Menüband): beim Öffnen, Verschieben und
/// Anordnen (siehe <see cref="MdiFenster"/>), beim Wiederherstellen gespeicherter Fensteranordnungen und wenn der Arbeitsbereich
/// kleiner wird (Hauptfenster verkleinert, Menüband höher, anderer Bildschirm bzw. andere DPI-Skalierung).
/// </summary>
public sealed partial class HauptFenster
{
    private MdiClient? MdiArbeitsbereich => Controls.OfType<MdiClient>().FirstOrDefault();

    private void MdiLageVorbereiten()
    {
        if (MdiArbeitsbereich is not { } mdi) return;
        // erst nach dem Neuanordnen der Leisten klemmen (Größe steht dann fest)
        mdi.Resize += (_, _) => { if (IsHandleCreated && WindowState != FormWindowState.Minimized) BeginInvoke(MdiFensterKlemmen); };
    }

    /// <summary>Alle Arbeitsfenster (im Fenstermodus) vollständig in den MDI-Arbeitsbereich holen.</summary>
    internal void MdiFensterKlemmen()
    {
        if (IsDisposed || WindowState == FormWindowState.Minimized) return;
        foreach (var f in MdiChildren)
        {
            if (f is MdiFenster m) m.InArbeitsbereichKlemmen();
            else if (f.WindowState == FormWindowState.Normal && MdiArbeitsbereich is { } mdi && Fensterlage.BereichGueltig(new Rectangle(Point.Empty, mdi.ClientSize)))
            {
                var r = Fensterlage.MdiFenster(f.Bounds, mdi.ClientSize, true);
                if (r != f.Bounds) f.Bounds = r;
            }
        }
    }

    /// <summary>Gespeicherte Lage eines Arbeitsfensters an den aktuellen MDI-Arbeitsbereich anpassen.</summary>
    private Rectangle GespeicherteLage(FensterPosition p)
    {
        var r = new Rectangle(p.X, p.Y, Math.Max(200, p.B), Math.Max(120, p.H));
        return MdiArbeitsbereich is { } mdi && Fensterlage.BereichGueltig(new Rectangle(Point.Empty, mdi.ClientSize)) ? Fensterlage.MdiFenster(r, mdi.ClientSize, true) : r;
    }
}
