using System.Runtime.CompilerServices;
using UntPlan.Core;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Autovervollständigung aus den zuletzt verwendeten Eingaben (je Feldart, in den Benutzereinstellungen gespeichert, höchstens
/// 30 Einträge): Beim Tippen erscheint unter dem Textfeld eine Vorschlagsliste (Anfang vor „enthält“, Groß-/Kleinschreibung
/// egal). Pfeil ab/auf wählt, Enter bzw. Tab übernimmt, Escape schließt die Liste, Entf entfernt den markierten Vorschlag aus
/// dem Verlauf. Gilt für eigenständige Textfelder und für Textspalten in Tabellen (Text, Bemerkung …).
/// </summary>
public static class Textverlauf
{
    private sealed class Bindung
    {
        public Func<string> Feld = () => "";
        public bool Aktiv = true;
    }

    private static readonly ConditionalWeakTable<TextBox, Bindung> Bindungen = new();

    /// <summary>Spaltenüberschriften von Freitextspalten mit Verlauf.</summary>
    private static readonly HashSet<string> TextSpalten = new(StringComparer.CurrentCultureIgnoreCase)
    {
        "Text", "Text 2", "Text2", "Bemerkung", "Bemerkungen", "Beschr.", "Beschreibung", "Notiz", "Kommentar", "Vertretungs-Text", "Vertretungstext",
    };

    /// <summary>Beschriftungen eigenständiger Textfelder mit Verlauf (Dialogzeilen „Text:“, „Bemerkung:“ …).</summary>
    private static readonly HashSet<string> TextBeschriftungen = new(StringComparer.CurrentCultureIgnoreCase)
    {
        "Text", "Text:", "Bemerkung", "Bemerkung:", "Bezeichnung:", "Beschreibung:", "Notiz:", "Kommentar:", "Text zur Stunde",
    };

    public static bool IstTextBeschriftung(string beschriftung) => TextBeschriftungen.Contains(beschriftung.Trim());

    private static Dictionary<string, List<string>> Verlauf => AppZustand.Einstellungen.Eingabeverlauf;

    /// <summary>Eingabe für eine Feldart merken (und speichern).</summary>
    public static void Merken(string feld, string? text)
    {
        if (Eingabeverlauf.Merken(Verlauf, feld, text)) AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Eigenständiges Textfeld anbinden; gemerkt wird beim Verlassen, wenn sich der Text geändert hat.</summary>
    public static void Anbinden(TextBox t, string feld) => Anbinden(t, () => feld);

    public static void Anbinden(TextBox t, Func<string> feld)
    {
        if (Bindungen.TryGetValue(t, out var b)) { b.Feld = feld; return; }
        b = new Bindung { Feld = feld };
        Bindungen.Add(t, b);
        Einrichten(t, b);
        var vorher = t.Text;
        t.Enter += (_, _) => vorher = t.Text;
        t.Validated += (_, _) => { if (t.Text != vorher && !t.ReadOnly) Merken(b.Feld(), t.Text); vorher = t.Text; };
    }

    /// <summary>Tabelle: Freitextspalten (Text, Bemerkung …) bekommen beim Bearbeiten den Verlauf; gemerkt wird beim Verlassen der Zelle.</summary>
    public static void Tabelle(DataGridView g)
    {
        string? Feld(DataGridViewColumn? c)
        {
            if (c is not DataGridViewTextBoxColumn || c.ReadOnly || !TextSpalten.Contains(c.HeaderText.Trim())) return null;
            return (g.FindForm()?.GetType().Name ?? "Tabelle") + ":" + c.Name;
        }
        g.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox t) return;
            var feld = Feld(g.CurrentCell?.OwningColumn);
            if (Bindungen.TryGetValue(t, out var b))
            {
                // derselbe Editor wird für alle Spalten wiederverwendet
                b.Aktiv = feld != null;
                if (feld != null) b.Feld = () => feld;
                return;
            }
            if (feld is null) return;
            b = new Bindung { Feld = () => feld };
            Bindungen.Add(t, b);
            Einrichten(t, b);
        };
        g.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= g.RowCount || Feld(g.Columns[e.ColumnIndex]) is not { } feld) return;
            Merken(feld, g.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString());
        };
    }

    /// <summary>Gerade angezeigte Vorschläge (leer, wenn die Liste geschlossen ist) – für den Selbsttest.</summary>
    internal static IReadOnlyList<string> SichtbareVorschlaege() => Liste.Sichtbar();

    // ------------------------------------------------------------------------------------------
    // Vorschlagsliste
    // ------------------------------------------------------------------------------------------

    private static void Einrichten(TextBox t, Bindung b)
    {
        t.AutoCompleteMode = AutoCompleteMode.None;
        t.TextChanged += (_, _) =>
        {
            if (!b.Aktiv || t.ReadOnly || !t.Focused || Liste.Unterdrueckt) return;
            Liste.Zeigen(t, b.Feld());
        };
        t.KeyDown += (_, e) =>
        {
            // Alt+Pfeil ab bzw. Strg+Leertaste: ganzen Verlauf zeigen
            if (b.Aktiv && !t.ReadOnly && ((e.Alt && e.KeyCode == Keys.Down) || (e.Control && e.KeyCode == Keys.Space)))
            {
                Liste.Zeigen(t, b.Feld(), alle: true);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        t.Leave += (_, _) => Liste.Schliessen(t);
        t.Disposed += (_, _) => Liste.Schliessen(t);
    }

    /// <summary>Vorschlagsfenster (eines für das ganze Programm): erscheint ohne Aktivierung unter dem Textfeld.</summary>
    private sealed class Liste : Form, IMessageFilter
    {
        private static Liste? _fenster;
        public static bool Unterdrueckt;
        private readonly ListBox _box = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Ui.Schrift };
        private TextBox? _feld;
        private string _feldart = "";
        private bool _filter;

        private Liste()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = SystemColors.ControlDark;
            Padding = new Padding(1);
            Controls.Add(_box);
            _box.MouseMove += (_, e) => { var i = _box.IndexFromPoint(e.Location); if (i >= 0 && i != _box.SelectedIndex) _box.SelectedIndex = i; };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public static List<string> Sichtbar() => _fenster is { IsDisposed: false, Visible: true } f ? f._box.Items.Cast<string>().ToList() : new();

        public static void Zeigen(TextBox t, string feldart, bool alle = false)
        {
            var vorschlaege = Eingabeverlauf.Vorschlaege(Verlauf, feldart, alle ? "" : t.Text);
            if (vorschlaege.Count == 0 || (!alle && t.Text.Trim().Length == 0)) { Schliessen(t); return; }
            if (_fenster is null || _fenster.IsDisposed) _fenster = new Liste();
            _fenster.Anzeigen(t, feldart, vorschlaege);
        }

        public static void Schliessen(TextBox? t)
        {
            if (_fenster is null || _fenster.IsDisposed || (t != null && _fenster._feld != t)) return;
            _fenster.Verbergen();
        }

        private void Anzeigen(TextBox t, string feldart, List<string> vorschlaege)
        {
            _feld = t;
            _feldart = feldart;
            _box.BeginUpdate();
            _box.Items.Clear();
            foreach (var v in vorschlaege) _box.Items.Add(v);
            _box.EndUpdate();
            _box.SelectedIndex = -1;
            var unten = t.PointToScreen(new Point(0, t.Height));
            var bildschirm = Screen.FromControl(t).WorkingArea;
            var breite = Math.Max(t.Width, Math.Min(520, vorschlaege.Max(v => TextRenderer.MeasureText(v, _box.Font).Width) + 24));
            var hoehe = Math.Min(vorschlaege.Count, 10) * _box.ItemHeight + Padding.Vertical + 4;
            var y = unten.Y + hoehe > bildschirm.Bottom ? t.PointToScreen(Point.Empty).Y - hoehe : unten.Y;
            var x = Math.Max(bildschirm.Left, Math.Min(unten.X, bildschirm.Right - breite));
            Bounds = new Rectangle(x, y, breite, hoehe);
            if (!Visible)
            {
                if (t.FindForm() is { } owner && !owner.IsDisposed) Owner = owner.MdiParent ?? owner;
                Show();
            }
            if (!_filter) { Application.AddMessageFilter(this); _filter = true; }
        }

        private void Verbergen()
        {
            if (_filter) { Application.RemoveMessageFilter(this); _filter = false; }
            _feld = null;
            if (Visible) Hide();
        }

        private void Uebernehmen()
        {
            if (_feld is not { } t || _box.SelectedItem is not string text) { Verbergen(); return; }
            Unterdrueckt = true;
            try
            {
                t.Text = text;
                t.SelectionStart = t.TextLength;
                t.SelectionLength = 0;
            }
            finally { Unterdrueckt = false; }
            Verbergen();
        }

        /// <summary>Tasten für die Liste abfangen, bevor das Textfeld bzw. die Tabelle sie verarbeitet (Pfeile, Enter, Tab, Escape, Entf).</summary>
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x100, WM_LBUTTONDOWN = 0x201, WM_NCLBUTTONDOWN = 0x00A1, WM_RBUTTONDOWN = 0x204;
            if (_feld is not { } t || t.IsDisposed || !Visible) return false;
            if (m.Msg == WM_LBUTTONDOWN && m.HWnd == _box.Handle)
            {
                // Klick in die Liste: sofort übernehmen, ohne dass die Liste den Fokus bekommt (das Textfeld bleibt aktiv)
                var lp = (int)(long)m.LParam;
                var i = _box.IndexFromPoint(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (i >= 0) { _box.SelectedIndex = i; Uebernehmen(); }
                return true;
            }
            if (m.Msg is WM_LBUTTONDOWN or WM_NCLBUTTONDOWN or WM_RBUTTONDOWN)
            {
                // Klick außerhalb von Textfeld und Liste schließt die Liste (ohne etwas zu übernehmen)
                if (m.HWnd != t.Handle && m.HWnd != _box.Handle && m.HWnd != Handle) Verbergen();
                return false;
            }
            if (m.Msg != WM_KEYDOWN || m.HWnd != t.Handle) return false;
            var taste = (Keys)(int)m.WParam & Keys.KeyCode;
            switch (taste)
            {
                case Keys.Down:
                    _box.SelectedIndex = Math.Min(_box.Items.Count - 1, _box.SelectedIndex + 1);
                    return true;
                case Keys.Up:
                    if (_box.SelectedIndex <= 0) { _box.SelectedIndex = -1; return true; }
                    _box.SelectedIndex--;
                    return true;
                case Keys.PageDown:
                    _box.SelectedIndex = Math.Min(_box.Items.Count - 1, _box.SelectedIndex + 9);
                    return true;
                case Keys.PageUp:
                    _box.SelectedIndex = Math.Max(0, _box.SelectedIndex - 9);
                    return true;
                case Keys.Enter or Keys.Tab:
                    // nur mit markiertem Vorschlag abfangen; sonst wirken Enter/Tab wie gewohnt
                    if (_box.SelectedIndex < 0) { Verbergen(); return false; }
                    Uebernehmen();
                    return taste == Keys.Enter;
                case Keys.Escape:
                    Verbergen();
                    return true;
                case Keys.Delete when _box.SelectedItem is string weg && Control.ModifierKeys == Keys.Shift:
                    // Umschalt+Entf: markierten Vorschlag aus dem Verlauf entfernen
                    if (Eingabeverlauf.Entfernen(Verlauf, _feldart, weg)) AppZustand.Einstellungen.Speichern();
                    var i = _box.SelectedIndex;
                    _box.Items.RemoveAt(i);
                    if (_box.Items.Count == 0) Verbergen();
                    else _box.SelectedIndex = Math.Min(i, _box.Items.Count - 1);
                    return true;
            }
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (_filter) { Application.RemoveMessageFilter(this); _filter = false; }
            base.Dispose(disposing);
        }
    }
}
