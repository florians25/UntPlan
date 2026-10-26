namespace UntPlan.App.Steuerelemente;

/// <summary>Darstellung eines Eintrags in einer Auswahlliste (Hintergrund z. B. rot = nicht verfügbar, Zusatztext = Langname).</summary>
public readonly record struct ListenStil(Color Hintergrund, Color Text, string Zusatz);

/// <summary>
/// Auswahllisten in Tabellen mit Direktsuche (siehe <see cref="ListenSuche"/>): In Auswahlspalten kann getippt werden, die Liste
/// öffnet sich und markiert den passenden Eintrag. Optional farbige Einträge mit Zusatztext (z. B. Lehrer rot, wenn belegt).
/// </summary>
public static class SuchKombi
{
    private static readonly HashSet<DataGridView> Aktiv = new();

    /// <summary>
    /// Laufende Bearbeitung einer Tabelle: die Zelle, in der die Bearbeitung begann, und ihr Ausgangswert. Die Auswahlliste wird
    /// von der Tabelle für jede Zelle wiederverwendet – Prüfung und Übernahme gelten ausschließlich dieser Zelle, nie der Zelle,
    /// in die der Benutzer inzwischen geklickt hat (sonst landete der Wert der zuletzt bearbeiteten Zeile in der neuen Zeile).
    /// </summary>
    private sealed class Bearbeitung
    {
        public DataGridViewCell? Zelle;
        public string Ausgang = "";
        /// <summary>Die Liste wird gerade auf den Wert der Zelle gesetzt (keine sofortige Übernahme auslösen).</summary>
        public bool Abgleich;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, Bearbeitung> Laufend = new();

    public static bool IstAktiv(DataGridView g) => Aktiv.Contains(g);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> PfeilTabelle = new();

    private static class Pfeile
    {
        public static bool Add(ComboBox cb)
        {
            if (PfeilTabelle.TryGetValue(cb, out _)) return false;
            PfeilTabelle.Add(cb, new object());
            return true;
        }
    }

    /// <summary>
    /// Pfeil auf/ab in einer geschlossenen Auswahlliste einer Tabelle (ohne Suchtext): Eingabe übernehmen und in die Zeile
    /// darüber bzw. darunter wechseln – wie in jeder anderen Zelle, statt still den nächsten Listeneintrag zu wählen.
    /// Bei offener Liste bzw. mit Alt blättern die Pfeile wie gewohnt in der Liste.
    /// </summary>
    private static void ZeileWechseln(ComboBox cb, KeyEventArgs k)
    {
        if (k.KeyCode is not (Keys.Up or Keys.Down) || k.Alt || k.Control || cb.DroppedDown || ListenSuche.HatSuchtext(cb)) return;
        if (cb is not IDataGridViewEditingControl { EditingControlDataGridView: { } g } || g.CurrentCell is not { } c) return;
        k.Handled = k.SuppressKeyPress = true;
        var spalte = c.ColumnIndex;
        var zeile = c.RowIndex;
        if (!g.EndEdit()) return;
        var richtung = k.KeyCode == Keys.Down ? 1 : -1;
        for (var z = zeile + richtung; z >= 0 && z < g.Rows.Count; z += richtung)
        {
            if (!g.Rows[z].Visible) continue;
            try { g.CurrentCell = g.Rows[z].Cells[spalte]; } catch (InvalidOperationException) { }
            if (g.SelectionMode == DataGridViewSelectionMode.FullRowSelect && g.CurrentRow is { } r) { g.ClearSelection(); r.Selected = true; }
            return;
        }
    }

    /// <summary>Zelle, deren Bearbeitung gerade läuft (null ohne Bearbeitung bzw. ohne Direktsuche).</summary>
    public static DataGridViewCell? BearbeiteteZelle(DataGridView g) =>
        Laufend.TryGetValue(g, out var b) && g.IsCurrentCellInEditMode && b.Zelle is { DataGridView: not null } z && z == g.CurrentCell ? z : null;

    /// <summary>Gehört die laufende Bearbeitung zu genau dieser Zelle (Zeile und Spalte)?</summary>
    private static bool IstZiel(DataGridView g, Bearbeitung b, int zeile, int spalte) =>
        b.Zelle is { } z && z.DataGridView == g && z.RowIndex == zeile && z.ColumnIndex == spalte;

    /// <summary>
    /// Auswahlliste auf den Wert der Zelle setzen, deren Bearbeitung gerade beginnt. Die wiederverwendete Liste behielt sonst
    /// manchmal Auswahl bzw. Text der zuvor bearbeiteten Zeile (z. B. wenn der Wert der neuen Zelle nicht in der Liste steht).
    /// </summary>
    internal static void Abgleichen(ComboBox cb, string wert)
    {
        var namen = cb.Items.Cast<object>().Select(o => cb.GetItemText(o) ?? "").ToList();
        var i = namen.FindIndex(x => string.Equals(x, wert, StringComparison.Ordinal));
        if (i < 0) i = namen.FindIndex(x => string.Equals(x, wert, StringComparison.CurrentCultureIgnoreCase));
        if (i >= 0)
        {
            if (cb.SelectedIndex != i) cb.SelectedIndex = i;
            if (cb.Text != namen[i]) cb.Text = namen[i];
        }
        else
        {
            if (cb.SelectedIndex != -1) cb.SelectedIndex = -1;
            if (cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Text != wert) cb.Text = wert;
        }
        ListenSuche.AusgangswertMerken(cb);
    }

    /// <summary>
    /// Änderung in Kontrollkästchen- bzw. Auswahlzellen sofort übernehmen (für CurrentCellDirtyStateChanged) – nur während einer
    /// Bearbeitung und nur in der Zelle, in der sie begann (nie mit einem veralteten Listenwert in einer anderen Zeile).
    /// </summary>
    public static void SofortUebernehmen(DataGridView g, bool kontrollkaestchen = true, bool auswahl = true)
    {
        if (!g.IsCurrentCellDirty || g.CurrentCell is not { } c) return;
        if (c is DataGridViewCheckBoxCell ? !kontrollkaestchen : c is DataGridViewComboBoxCell ? !auswahl : true) return;
        if (c is DataGridViewComboBoxCell && (!g.IsCurrentCellInEditMode || (IstAktiv(g) && Laufend.TryGetValue(g, out var b) && (b.Abgleich || b.Zelle != c)))) return;
        g.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    public static void Aktivieren(DataGridView g, Func<DataGridViewCell, string, ListenStil?>? stil = null)
    {
        if (!Aktiv.Add(g)) return;
        var b = Laufend.GetValue(g, _ => new Bearbeitung());
        g.Disposed += (_, _) => Aktiv.Remove(g);
        g.EditingControlShowing += (_, e) =>
        {
            // Beginn einer Bearbeitung: Zielzelle und Ausgangswert festhalten (gilt auch für Textzellen)
            b.Zelle = g.CurrentCell;
            b.Ausgang = g.CurrentCell?.FormattedValue?.ToString() ?? "";
            if (e.Control is not ComboBox cb || g.CurrentCell is not DataGridViewComboBoxCell zelle) return;
            cb.MaxDropDownItems = 18;
            ListenSuche.Einrichten(cb, stil is null ? null : text => stil(zelle, text));
            if (Pfeile.Add(cb)) cb.KeyDown += (_, k) => ZeileWechseln(cb, k);
            // die Liste zeigt genau den Wert dieser Zelle (nie einen Rest der vorher bearbeiteten Zeile)
            if (cb.Text != b.Ausgang || (cb.SelectedIndex >= 0 && cb.GetItemText(cb.SelectedItem) != b.Ausgang))
            {
                b.Abgleich = true;
                try { Abgleichen(cb, b.Ausgang); }
                finally { b.Abgleich = false; }
            }
            // die Zelle setzt die Listenbreite bei jeder Bearbeitung auf die Spaltenbreite – gleich wieder verbreitern
            ListenSuche.BreiteAktualisieren(cb);
            g.BeginInvoke(() => { if (!cb.IsDisposed) ListenSuche.BreiteAktualisieren(cb); });
        };
        g.CellEndEdit += (_, _) => b.Zelle = null;
        g.CellValidating += (_, e) =>
        {
            // eine vorher registrierte Prüfung hat die Eingabe abgelehnt (Hinweis statt stiller Ersetzung durch „leer“)
            if (e.Cancel) return;
            if (g.Columns[e.ColumnIndex] is not DataGridViewComboBoxColumn || g.EditingControl is not ComboBox cb || cb.Items.Count == 0) return;
            // nur die Zelle prüfen, deren Bearbeitung läuft – nie eine andere Zeile mit dem Inhalt der Liste beschreiben
            if (!g.IsCurrentCellInEditMode || !IstZiel(g, b, e.RowIndex, e.ColumnIndex)) return;
            var text = cb.Text.Trim();
            // unverändert (kein Suchtext, derselbe Wert wie zu Beginn): nichts ersetzen – auch Werte außerhalb der Liste bleiben
            if (!ListenSuche.HatSuchtext(cb) && string.Equals(text, b.Ausgang.Trim(), StringComparison.Ordinal)) return;
            var namen = cb.Items.Cast<object>().Select(o => cb.GetItemText(o) ?? "").ToList();
            var i = text.Length == 0 ? namen.FindIndex(x => x.Length == 0) : ListenSuche.Treffer(cb, text);
            // ungültige Eingabe: vorherigen Wert der Zelle wiederherstellen statt etwas anderes (z. B. „leer“) zu übernehmen
            if (i < 0 && ListenSuche.VorherText(cb) is { } vorher) i = namen.FindIndex(x => string.Equals(x, vorher, StringComparison.CurrentCulture));
            if (i < 0) i = namen.FindIndex(x => x == b.Ausgang);
            if (i < 0)
            {
                // nichts Passendes: Ausgangswert der Zelle behalten
                if (cb.DropDownStyle != ComboBoxStyle.DropDownList) cb.Text = b.Ausgang;
                return;
            }
            if (cb.SelectedIndex != i) cb.SelectedIndex = i;
            cb.Text = namen[i];
            if (g.CurrentCell is { } c && c.RowIndex == e.RowIndex && c.ColumnIndex == e.ColumnIndex) g.NotifyCurrentCellDirty(true);
        };
        g.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Auswahl mit der Maus sofort übernehmen – nur in der Zelle, deren Bearbeitung läuft
            if (b.Abgleich || !g.IsCurrentCellInEditMode || g.CurrentCell is not { } cur || cur != b.Zelle) return;
            if (g.IsCurrentCellDirty && cur is DataGridViewComboBoxCell && g.EditingControl is ComboBox { DroppedDown: false } cb && cb.SelectedIndex >= 0 &&
                string.Equals(cb.GetItemText(cb.SelectedItem), cb.Text, StringComparison.Ordinal))
                g.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        g.DataError += (_, e) => e.ThrowException = false;
    }

    /// <summary>Auswahlspalte mit Direktsuche (Werte: Kurznamen; leerer Eintrag zuerst).</summary>
    public static DataGridViewComboBoxColumn Spalte(string name, string kopf, int breite, IEnumerable<string> werte, bool leer = true)
    {
        var c = new DataGridViewComboBoxColumn
        {
            Name = name, HeaderText = kopf, Width = breite, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DisplayStyleForCurrentCellOnly = true, AutoComplete = false, SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        if (leer) c.Items.Add("");
        foreach (var w in werte.Distinct()) c.Items.Add(w);
        return c;
    }
}
