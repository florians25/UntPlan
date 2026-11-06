using System.Runtime.CompilerServices;
using UntPlan.Core;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Autovervollständigung aus den zuletzt verwendeten Eingaben (je Feldart, in den Benutzereinstellungen gespeichert, höchstens
/// 30 Einträge): Beim Tippen erscheint der Rest des zuletzt verwendeten passenden Textes blau markiert direkt hinter der Eingabe
/// (Groß-/Kleinschreibung egal). Tab bestätigt den Vorschlag, Weitertippen ersetzt ihn, Entf/Rücktaste/Esc entfernen ihn;
/// Enter übernimmt nur den getippten Text. Gilt für eigenständige Textfelder und für Textspalten in Tabellen (Text, Bemerkung …).
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
        "Text", "Tagestext", "Text 2", "Text2", "Vertr.-Text-2", "Bemerkung", "Bemerkungen", "Beschr.", "Beschreibung", "Notiz", "Kommentar", "Vertretungs-Text", "Vertretungstext",
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

    /// <summary>Gerade angezeigter Vorschlag (leer, wenn keiner angezeigt wird) – für den Selbsttest.</summary>
    internal static IReadOnlyList<string> SichtbareVorschlaege() =>
        Inline.Aktiv is { } t && Inline.HatVorschlag(t) ? new[] { t.Text } : Array.Empty<string>();

    /// <summary>Zeigt dieses Textfeld gerade einen (noch nicht bestätigten) blauen Vorschlag?</summary>
    internal static bool VorschlagMarkiert(TextBox t) => Inline.HatVorschlag(t);

    /// <summary>Vorschlag dieses Textfelds verwerfen (nur der getippte Text bleibt); true, wenn einer angezeigt wurde.</summary>
    internal static bool ListeSchliessen(TextBox t) => Inline.Verwerfen(t);

    /// <summary>Selbsttest: angezeigten Vorschlag wie mit Tab bestätigen.</summary>
    internal static bool VorschlagBestaetigen(TextBox t) => Inline.Bestaetigen(t);

    // ------------------------------------------------------------------------------------------
    // Vorschlag im Text (wie im Vorbild): der Rest des zuletzt verwendeten passenden Textes erscheint markiert (blau) hinter der
    // Eingabe; Tab bestätigt ihn, Weitertippen ersetzt ihn, Entf/Rücktaste/Esc entfernen ihn. Keine Liste.
    // ------------------------------------------------------------------------------------------

    private static void Einrichten(TextBox t, Bindung b)
    {
        t.AutoCompleteMode = AutoCompleteMode.None;
        var vorher = t.Text;
        t.Enter += (_, _) => vorher = t.Text;
        t.TextChanged += (_, _) =>
        {
            var alt = vorher;
            vorher = t.Text;
            if (!b.Aktiv || t.ReadOnly || !t.Focused || Inline.Unterdrueckt) return;
            // nur beim Weiterschreiben am Ende vorschlagen (nicht beim Löschen oder Einfügen in der Mitte)
            if (t.TextLength <= alt.Length || t.SelectionStart != t.TextLength || !t.Text.StartsWith(alt, StringComparison.CurrentCultureIgnoreCase)) { Inline.Vergessen(t); return; }
            if (!AppZustand.Einstellungen.Autovervollstaendigen) return;
            // Vergleichsbasis ist der getippte Teil: das nächste Zeichen ersetzt den markierten Vorschlag und schlägt neu vor
            vorher = Inline.Vorschlagen(t, b.Feld()) ?? t.Text;
        };
        t.KeyDown += (_, e) =>
        {
            if (!Inline.HatVorschlag(t)) return;
            if (e.KeyCode is Keys.Delete or Keys.Back) { Inline.Verwerfen(t); vorher = t.Text; e.Handled = e.SuppressKeyPress = true; }
        };
        t.Leave += (_, _) => { Inline.Verwerfen(t); vorher = t.Text; };
        t.Disposed += (_, _) => Inline.Vergessen(t);
    }

    private static class Inline
    {
        public static bool Unterdrueckt;
        public static TextBox? Aktiv;
        /// <summary>Länge des getippten Teils (davor), wenn ein Vorschlag angezeigt wird.</summary>
        private static int _getippt = -1;
        /// <summary>Getippter Text in der eigenen Schreibweise (Esc/Entf/Enter stellen ihn wieder her).</summary>
        private static string _eingabe = "";
        private static readonly Filter TastenFilter = new();
        private static bool _filterAn;

        public static bool HatVorschlag(TextBox t) =>
            ReferenceEquals(Aktiv, t) && !t.IsDisposed && _getippt >= 0 && t.SelectionStart == _getippt && t.SelectionStart + t.SelectionLength == t.TextLength && t.SelectionLength > 0;

        /// <summary>Vorschlag zeigen; liefert den getippten Teil (in der Schreibweise des Vorschlags) oder null ohne Vorschlag.</summary>
        public static string? Vorschlagen(TextBox t, string feldart)
        {
            var eingabe = t.Text;
            if (eingabe.Trim().Length == 0) { Vergessen(t); return null; }
            var treffer = Eingabeverlauf.Vorschlaege(Verlauf, feldart, eingabe)
                .FirstOrDefault(v => v.StartsWith(eingabe, StringComparison.CurrentCultureIgnoreCase) && v.Length > eingabe.Length);
            if (treffer is null) { Vergessen(t); return null; }
            Unterdrueckt = true;
            try
            {
                // Schreibweise des gemerkten Textes übernehmen („a“ → „Aufgaben im Buch“)
                t.Text = treffer;
                t.Select(eingabe.Length, t.TextLength - eingabe.Length);
            }
            finally { Unterdrueckt = false; }
            Aktiv = t;
            _getippt = eingabe.Length;
            _eingabe = eingabe;
            if (!_filterAn) { Application.AddMessageFilter(TastenFilter); _filterAn = true; }
            return treffer[..eingabe.Length];
        }

        public static bool Bestaetigen(TextBox t)
        {
            if (!HatVorschlag(t)) return false;
            t.Select(t.TextLength, 0);
            Vergessen(t);
            return true;
        }

        public static bool Verwerfen(TextBox t)
        {
            if (!HatVorschlag(t)) { Vergessen(t); return false; }
            Unterdrueckt = true;
            try
            {
                var n = _getippt;
                t.Text = _eingabe.Length == n ? _eingabe : t.Text[..n];
                t.Select(n, 0);
            }
            finally { Unterdrueckt = false; }
            Vergessen(t);
            return true;
        }

        public static void Vergessen(TextBox t)
        {
            if (Aktiv != null && !ReferenceEquals(Aktiv, t)) return;
            Aktiv = null;
            _getippt = -1;
            if (_filterAn) { Application.RemoveMessageFilter(TastenFilter); _filterAn = false; }
        }

        /// <summary>Tab bestätigt, Esc verwirft – vor der Tabelle bzw. dem Dialog (sonst würde Tab die Zelle wechseln).</summary>
        private sealed class Filter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                const int WM_KEYDOWN = 0x100;
                if (m.Msg != WM_KEYDOWN || Aktiv is not { } t || t.IsDisposed || m.HWnd != t.Handle) return false;
                var taste = (Keys)(int)m.WParam & Keys.KeyCode;
                if (taste == Keys.Tab && Control.ModifierKeys == Keys.None) return Bestaetigen(t);
                if (taste == Keys.Escape) return Verwerfen(t);
                if (taste == Keys.Enter) Verwerfen(t);
                return false;
            }
        }
    }
}
