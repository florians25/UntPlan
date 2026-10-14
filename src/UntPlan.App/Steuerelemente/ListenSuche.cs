using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Suche direkt in der Auswahlliste, ohne eigene Vorschlagsliste: Tippen öffnet die Liste und markiert den besten Treffer.
/// Gesucht wird im Kurznamen und im Langnamen (Anfang, dann enthalten).
/// Die Liste zeigt neben dem Kurznamen grau den Langnamen und öffnet immer nach unten (die Höhe passt sich dem Platz an).
/// </summary>
public static class ListenSuche
{
    private sealed class Zustand
    {
        public Func<string, ListenStil?>? Stil;
        public Func<string, string?>? Langname;
        public int Markiert = -1;
    }

    private static readonly ConditionalWeakTable<ComboBox, Zustand> Zustaende = new();

    /// <summary>Langname eines Kurznamens (Lehrer, Klassen, Räume, Fächer, Gänge) – für die Anzeige in Auswahllisten.</summary>
    public static string? Langname(string kurz)
    {
        if (string.IsNullOrWhiteSpace(kurz) || AppZustand.Doc is null) return null;
        var d = AppZustand.D;
        foreach (var liste in new IEnumerable<Core.Model.Element>[] { d.Lehrer, d.Klassen, d.Raeume, d.Faecher, d.Gaenge })
            foreach (var e in liste)
                if (e.Kurzname == kurz) return string.IsNullOrWhiteSpace(e.Langname) || e.Langname == kurz ? null : e.Langname;
        return null;
    }

    public static void Einrichten(ComboBox cb, Func<string, ListenStil?>? stil = null, Func<string, string?>? langname = null)
    {
        langname ??= Langname;
        if (Zustaende.TryGetValue(cb, out var z))
        {
            // Tabellenzellen setzen beim Öffnen Stil und Ergänzung zurück
            z.Stil = stil;
            z.Langname = langname;
            if (cb.DropDownStyle == ComboBoxStyle.DropDownList) cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.AutoCompleteMode = AutoCompleteMode.None;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            return;
        }
        z = new Zustand { Stil = stil, Langname = langname };
        Zustaende.Add(cb, z);
        var auswahl = cb.SelectedIndex;
        if (cb.DropDownStyle == ComboBoxStyle.DropDownList) cb.DropDownStyle = ComboBoxStyle.DropDown;
        if (auswahl >= 0 && auswahl < cb.Items.Count && cb.SelectedIndex != auswahl) cb.SelectedIndex = auswahl;
        cb.AutoCompleteMode = AutoCompleteMode.None;
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = Math.Max(cb.ItemHeight, 17);
        cb.IntegralHeight = false;
        cb.DrawItem += Zeichnen;
        cb.DropDown += (_, _) =>
        {
            Breite(cb, z);
            NachUnten(cb);
        };
        cb.DropDownClosed += (_, _) => z.Markiert = -1;
        cb.TextUpdate += (_, _) => Suchen(cb, z);
        cb.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !cb.DroppedDown) return;
            var i = z.Markiert >= 0 ? z.Markiert : Treffer(cb, cb.Text, z);
            if (i >= 0)
            {
                cb.DroppedDown = false;
                cb.SelectedIndex = i;
                cb.SelectAll();
            }
        };
    }

    /// <summary>Index des besten Treffers (exakt, Kurzname beginnt, Langname beginnt, Kurzname enthält, Langname enthält).</summary>
    public static int Treffer(ComboBox cb, string text, Func<string, string?>? langname = null)
    {
        Zustaende.TryGetValue(cb, out var z);
        return Treffer(cb, text, z ?? new Zustand { Langname = langname ?? Langname });
    }

    private static int Treffer(ComboBox cb, string text, Zustand z)
    {
        text = text.Trim();
        if (text.Length == 0) return -1;
        var kurz = cb.Items.Cast<object>().Select(cb.GetItemText).ToList();
        var lang = kurz.Select(k => z.Langname?.Invoke(k) ?? "").ToList();
        const StringComparison ci = StringComparison.CurrentCultureIgnoreCase;
        var i = kurz.FindIndex(k => k.Equals(text, ci));
        if (i < 0) i = kurz.FindIndex(k => k.StartsWith(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.StartsWith(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.Split(' ', ',', '-').Any(w => w.StartsWith(text, ci)));
        if (i < 0) i = kurz.FindIndex(k => k.Contains(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.Contains(text, ci));
        return i;
    }

    private static void Suchen(ComboBox cb, Zustand z)
    {
        var text = cb.Text;
        var pos = cb.SelectionStart;
        if (!cb.DroppedDown)
        {
            cb.DroppedDown = true;
            Cursor.Current = Cursors.Default;
            // das Öffnen setzt den Text zurück bzw. markiert ihn: getippten Text wiederherstellen
            if (cb.Text != text) cb.Text = text;
            cb.SelectionStart = Math.Min(pos, text.Length);
            cb.SelectionLength = 0;
        }
        z.Markiert = Treffer(cb, text, z);
        ListeMarkieren(cb, z.Markiert);
    }

    private static void Breite(ComboBox cb, Zustand z)
    {
        var breite = cb.Width;
        using var g = cb.CreateGraphics();
        foreach (var o in cb.Items)
        {
            var t = cb.GetItemText(o);
            var zusatz = z.Stil?.Invoke(t)?.Zusatz ?? z.Langname?.Invoke(t);
            var w = TextRenderer.MeasureText(g, t, cb.Font).Width + (string.IsNullOrEmpty(zusatz) ? 0 : TextRenderer.MeasureText(g, zusatz, cb.Font).Width + 24) + 28;
            breite = Math.Max(breite, Math.Min(460, w));
        }
        cb.DropDownWidth = breite;
    }

    /// <summary>Listenhöhe so wählen, dass die Liste unter dem Feld Platz hat (Windows öffnet sonst nach oben).</summary>
    private static void NachUnten(ComboBox cb)
    {
        var unten = cb.PointToScreen(new Point(0, cb.Height)).Y;
        var platz = Screen.FromControl(cb).WorkingArea.Bottom - unten - 6;
        var gewuenscht = Math.Max(1, cb.Items.Count) * cb.ItemHeight + 4;
        var max = Math.Min(gewuenscht, 22 * cb.ItemHeight + 4);
        cb.DropDownHeight = Math.Max(cb.ItemHeight * 3 + 4, Math.Min(max, platz));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int cbSize;
        public Rectangle rcItem, rcButton;
        public int stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboBoxInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    /// <summary>Treffer in der offenen Liste markieren, ohne den getippten Text oder die Auswahl zu ändern.</summary>
    private static void ListeMarkieren(ComboBox cb, int index)
    {
        try
        {
            var info = new ComboBoxInfo { cbSize = Marshal.SizeOf<ComboBoxInfo>() };
            if (!GetComboBoxInfo(cb.Handle, ref info) || info.hwndList == IntPtr.Zero) return;
            const int LB_SETCURSEL = 0x0186, LB_SETTOPINDEX = 0x0197;
            SendMessage(info.hwndList, LB_SETCURSEL, index, IntPtr.Zero);
            if (index >= 0) SendMessage(info.hwndList, LB_SETTOPINDEX, Math.Max(0, index - 2), IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static void Zeichnen(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb || e.Index < 0 || e.Index >= cb.Items.Count) return;
        Zustaende.TryGetValue(cb, out var z);
        var text = cb.GetItemText(cb.Items[e.Index]);
        var s = z?.Stil?.Invoke(text);
        var zusatz = !string.IsNullOrEmpty(s?.Zusatz) ? s.Value.Zusatz : z?.Langname?.Invoke(text);
        var ausgewaehlt = (e.State & DrawItemState.Selected) != 0;
        var hinter = ausgewaehlt ? SystemColors.Highlight : s?.Hintergrund is Color h && h != Color.Empty ? h : cb.BackColor;
        var vorder = ausgewaehlt ? SystemColors.HighlightText : s?.Text is Color t && t != Color.Empty ? t : cb.ForeColor;
        using (var b = new SolidBrush(hinter)) e.Graphics.FillRectangle(b, e.Bounds);
        var r = e.Bounds;
        // Kurzname in fester Spalte, damit die Langnamen bündig stehen
        var kurzBreite = string.IsNullOrEmpty(zusatz) ? r.Width - 4 : Math.Min(Math.Max(56, TextRenderer.MeasureText(e.Graphics, text, cb.Font).Width + 8), r.Width / 2);
        TextRenderer.DrawText(e.Graphics, text, cb.Font, new Rectangle(r.X + 3, r.Y, kurzBreite, r.Height), vorder, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(zusatz))
            TextRenderer.DrawText(e.Graphics, zusatz, cb.Font, new Rectangle(r.X + 6 + kurzBreite, r.Y, r.Width - kurzBreite - 8, r.Height),
                ausgewaehlt ? vorder : Color.FromArgb(105, 110, 120), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
