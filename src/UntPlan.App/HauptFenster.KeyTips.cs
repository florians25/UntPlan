using UntPlan.App.Steuerelemente;

namespace UntPlan.App;

/// <summary>
/// Zugriffstasten des Menübands (KeyTips): Alt allein (Drücken und Loslassen ohne weitere Taste) oder F10 blendet sie ein, solange das
/// Hauptfenster oder eines seiner Arbeitsfenster aktiv ist. Im KeyTip-Modus gehen alle Tasten an das Menüband; ein Mausklick beendet ihn.
/// </summary>
public sealed partial class HauptFenster
{
    internal sealed class KeyTipFilter(HauptFenster f) : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106;
        private const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_NCLBUTTONDOWN = 0xA1;
        private bool _altAllein;

        public bool PreFilterMessage(ref Message m)
        {
            if (f.IsDisposed || !f.IsHandleCreated) return false;
            var r = f._ribbon;
            if (m.Msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_NCLBUTTONDOWN)
            {
                _altAllein = false;
                r.KeyTipsBeenden();
                return false;
            }
            if (m.Msg is not (WM_KEYDOWN or WM_KEYUP or WM_CHAR or WM_SYSKEYDOWN or WM_SYSKEYUP or WM_SYSCHAR)) return false;
            // nur im Hauptfenster und seinen Arbeitsfenstern (Dialoge und Aufklappmenüs behalten ihre eigene Tastaturbedienung)
            if (Control.FromHandle(m.HWnd) is not { } c || c.FindForm() is not { } form || (form != f && form.MdiParent != f)) return false;
            var taste = (Keys)(int)(long)m.WParam & Keys.KeyCode;
            if (r.KeyTipStufe > 0)
            {
                if (m.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (taste == Keys.Menu) { _altAllein = true; return true; }
                    _altAllein = false;
                    return r.KeyTipTaste(taste);
                }
                if (m.Msg == WM_SYSKEYUP && taste == Keys.Menu && _altAllein)
                {
                    // erneutes Alt beendet die Anzeige
                    _altAllein = false;
                    r.KeyTipsBeenden();
                }
                return true;
            }
            if (m.Msg == WM_SYSKEYDOWN && taste == Keys.Menu)
            {
                // nur das erste Drücken zählt (Tastenwiederholung ignorieren)
                if (((long)m.LParam & (1L << 30)) == 0) _altAllein = true;
                return false;
            }
            if (m.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                _altAllein = false;
                if (taste == Keys.F10 && Control.ModifierKeys == Keys.None) { r.KeyTipsStarten(); return true; }
                return false;
            }
            if (m.Msg is WM_SYSKEYUP or WM_KEYUP && taste == Keys.Menu && _altAllein)
            {
                _altAllein = false;
                r.KeyTipsStarten();
                return true;
            }
            return false;
        }
    }
}
