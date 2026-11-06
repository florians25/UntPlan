using System.Runtime.CompilerServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Einheitliches, verlässliches Eingabeverhalten aller Tabellen (Stammdaten, Unterricht, Absenzen, Vertretungen, Veranstaltungen,
/// Vormerkungen, Klausuren, Zeitwünsche, Ferien …) – programmweit an einer Stelle statt je Fenster:
/// <list type="bullet">
/// <item>Enter bzw. Tab bestätigt die Eingabe und springt zum nächsten änderbaren Feld (Umschalt: zurück); am Zeilenende in die nächste Zeile.
/// Die Eingabe wird beim Verlassen der Zelle geprüft und übernommen.</item>
/// <item>Esc verwirft die laufende Eingabe (auch bei geöffneter Auswahlliste mit einem Tastendruck), F2 bearbeitet das Feld.</item>
/// <item>Entf leert die markierten Felder (bei ganz markierten Zeilen: Zeilen löschen), Strg+Entf löscht die markierten Zeilen
/// über den Knopf „Löschen“ des Fensters – immer mit derselben Rückfrage (<see cref="Ui.LoeschenBestaetigen"/>).</item>
/// <item>Strg+C kopiert, Strg+X schneidet aus, Strg+V fügt (auch Bereiche aus Tabellenkalkulationen) ein – als eine Rückgängig-Stufe.</item>
/// <item>Strg+Z / Strg+Y wirken in jeder Maske (Dokument-Historie) mit Hinweis in der Statuszeile.</item>
/// <item>Ungültige Eingaben: Hinweis in Statuszeile und Tooltip, die Eingabe bleibt stehen – keine Ausnahme, kein stiller Verlust.</item>
/// </list>
/// Fenstereigene Tastenbelegungen (KeyDown mit Handled) haben Vorrang; Tabellen mit eigener Enter-Logik (Unterricht: nächstes
/// Eingabefeld) behalten diese.
/// </summary>
public static class Tabellenbedienung
{
    private static readonly ConditionalWeakTable<DataGridView, object> Eingerichtet = new();
    private static readonly ConditionalWeakTable<Type, object> EigeneEnterLogik = new();
    private static readonly ConditionalWeakTable<DataGridView, StrongBox<bool>> LoeschAntwort = new();
    private static bool _aktiv;

    /// <summary>Programmweit einschalten (einmal beim Start): Tasten während einer Zelleingabe und Strg+Z/Strg+Y.</summary>
    public static void Einschalten()
    {
        if (_aktiv) return;
        _aktiv = true;
        Application.AddMessageFilter(new Filter());
    }

    /// <summary>Tabelle anbinden (über <see cref="Auswahlbreite"/> für jede Tabelle jedes Fensters, mehrfacher Aufruf unschädlich).</summary>
    public static void Einrichten(DataGridView g)
    {
        if (Eingerichtet.TryGetValue(g, out _)) return;
        Eingerichtet.Add(g, new object());
        g.KeyDown += (_, e) => Taste(g, e);
        g.DataError += (_, e) => DatenFehler(g, e);
        // eingebautes Zeilenlöschen (Entf auf markierten Zeilen): dieselbe Rückfrage wie überall, einmal je Tastendruck
        g.UserDeletingRow += (_, e) =>
        {
            if (e.Cancel || e.Row is null || e.Row.IsNewRow) return;
            if (!LoeschAntwort.TryGetValue(g, out var antwort))
            {
                antwort = new StrongBox<bool>(Ui.LoeschenBestaetigen(Math.Max(1, g.SelectedRows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow)), "Zeile", "Zeilen"));
                LoeschAntwort.AddOrUpdate(g, antwort);
                if (g.IsHandleCreated) g.BeginInvoke(() => LoeschAntwort.Remove(g)); else LoeschAntwort.Remove(g);
            }
            if (!antwort.Value) e.Cancel = true;
        };
        // F2 bearbeitet in jeder Tabelle (auch wenn das Fenster nur programmgesteuert bearbeiten lässt)
        if (g.EditMode == DataGridViewEditMode.EditProgrammatically && !g.ReadOnly) g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
    }

    // -------------------------------------------------------------------------------------------------------------------------
    // Tasten ohne laufende Eingabe (KeyDown der Tabelle – nach den fenstereigenen Belegungen)
    // -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Taste auf der Tabelle verarbeiten (ohne laufende Eingabe). Liefert true, wenn die Taste verarbeitet wurde.</summary>
    internal static bool Taste(DataGridView g, KeyEventArgs e)
    {
        if (e.Handled || g.IsCurrentCellInEditMode) return false;
        var erledigt = true;
        switch (e.KeyData)
        {
            case Keys.Enter when Aenderbar(g) && !HatEigeneEnterLogik(g):
            case Keys.Shift | Keys.Enter when Aenderbar(g) && !HatEigeneEnterLogik(g):
                // keine änderbare Zelle mehr (z. B. Auswahltabelle): Standardverhalten der Tabelle
                erledigt = Weiter(g, e.Shift ? -1 : 1);
                break;
            case Keys.F2 when g.CurrentCell is { } c && ZelleAenderbar(c):
                try { g.BeginEdit(true); } catch (InvalidOperationException) { }
                break;
            case Keys.Delete:
                var zeilen = g.SelectionMode == DataGridViewSelectionMode.FullRowSelect || GanzeZeilenMarkiert(g);
                if (zeilen && LoeschKnopf(g) is { } k) k.PerformClick();
                // Tabellen mit eingebautem Zeilenlöschen: die Tabelle löscht, die Rückfrage stellt UserDeletingRow
                else if (zeilen && g.AllowUserToDeleteRows && !g.ReadOnly) erledigt = false;
                else if (Aenderbar(g)) ZellenLeeren(g);
                else erledigt = false;
                break;
            case Keys.Insert when NeuKnopf(g) is { } neu:
                // Einfg: neuer Datensatz wie über den Knopf „Neu“ des Fensters
                neu.PerformClick();
                break;
            case Keys.Control | Keys.Delete:
                if (LoeschKnopf(g) is { } knopf) knopf.PerformClick();
                else erledigt = false;
                break;
            case Keys.Control | Keys.V when Aenderbar(g):
            case Keys.Shift | Keys.Insert when Aenderbar(g):
                Einfuegen(g);
                break;
            case Keys.Control | Keys.X when Aenderbar(g):
            case Keys.Shift | Keys.Delete when Aenderbar(g):
                NurText.Kopieren(NurText.TabellenText(g));
                ZellenLeeren(g, "Ausschneiden");
                break;
            default:
                erledigt = false;
                break;
        }
        if (erledigt) e.Handled = e.SuppressKeyPress = true;
        return erledigt;
    }

    /// <summary>Lässt die Tabelle Eingaben zu (nicht schreibgeschützt, sichtbar, bedienbar)?</summary>
    private static bool Aenderbar(DataGridView g) => !g.ReadOnly && g.Enabled && g.Visible;

    /// <summary>Kann diese Zelle über die Tastatur bearbeitet werden (Text- bzw. Auswahlzelle, nicht schreibgeschützt)?</summary>
    public static bool ZelleAenderbar(DataGridViewCell c) =>
        c.RowIndex >= 0 && c.ColumnIndex >= 0 && c.DataGridView is { ReadOnly: false } && !c.ReadOnly && c.Visible &&
        c is not (DataGridViewButtonCell or DataGridViewImageCell or DataGridViewLinkCell or DataGridViewCheckBoxCell);

    /// <summary>Sind nur ganze Zeilen markiert (Zeilenmarkierung bzw. Zeilenkopf)? Dann löscht Entf die Zeilen statt die Felder zu leeren.</summary>
    public static bool GanzeZeilenMarkiert(DataGridView g) =>
        g.SelectionMode is DataGridViewSelectionMode.RowHeaderSelect or DataGridViewSelectionMode.CellSelect && g.SelectedRows.Count > 0 &&
        g.SelectedCells.Count == g.SelectedRows.Count * g.Columns.GetColumnCount(DataGridViewElementStates.Visible);

    /// <summary>Knopf „Löschen“ der Werkzeugleiste des Fensters, zu dem die Tabelle gehört (null = keiner bzw. nicht bedienbar).</summary>
    internal static ToolStripItem? LoeschKnopf(DataGridView g)
    {
        for (Control? c = g.Parent; c != null; c = c.Parent)
        {
            foreach (var ts in c.Controls.OfType<ToolStrip>())
                foreach (ToolStripItem i in ts.Items)
                    if (i.Available && i.Enabled && i is ToolStripButton or ToolStripMenuItem && IstLoeschen(i)) return i;
            if (c is Form) break;
        }
        return null;
    }

    /// <summary>Knopf „Neu“ (bzw. „Neue …“) der Werkzeugleiste des Fensters, zu dem die Tabelle gehört.</summary>
    internal static ToolStripItem? NeuKnopf(DataGridView g)
    {
        static bool N(string? t) => t is not null && (t == "Neu" || t.StartsWith("Neu\n", StringComparison.Ordinal) ||
            ((t.StartsWith("Neue ", StringComparison.Ordinal) || t.StartsWith("Neuer ", StringComparison.Ordinal)) && !t.StartsWith("Neuer aktueller", StringComparison.Ordinal)));
        for (Control? c = g.Parent; c != null; c = c.Parent)
        {
            foreach (var ts in c.Controls.OfType<ToolStrip>())
                foreach (ToolStripItem i in ts.Items)
                    if (i.Available && i.Enabled && i is ToolStripButton && (N(i.Text) || N(i.ToolTipText))) return i;
            if (c is Form) break;
        }
        return null;
    }

    private static bool IstLoeschen(ToolStripItem i)
    {
        static bool L(string? t) => t is not null && (t == "Löschen" || t.StartsWith("Löschen\n", StringComparison.Ordinal) || t.StartsWith("Löschen (", StringComparison.Ordinal));
        return L(i.Text) || (string.IsNullOrEmpty(i.Text) && L(i.ToolTipText)) || (i.DisplayStyle == ToolStripItemDisplayStyle.Image && L(i.ToolTipText));
    }

    /// <summary>
    /// Enter/Tab-Navigation: nächstes (bzw. vorheriges) änderbares Feld der Zeile in Anzeigereihenfolge, am Zeilenende das erste
    /// änderbare Feld der nächsten Zeile. Am Tabellenende bleibt die Zelle stehen.
    /// </summary>
    public static bool Weiter(DataGridView g, int richtung)
    {
        if (g.CurrentCell is not { } cur || g.IsDisposed) return false;
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        if (spalten.Count == 0) return false;
        var zeile = cur.RowIndex;
        var i = spalten.IndexOf(cur.OwningColumn);
        for (var schritt = 0; schritt < spalten.Count * 3 + 2; schritt++)
        {
            i += richtung;
            if (i < 0 || i >= spalten.Count)
            {
                var naechste = zeile + richtung;
                while (naechste >= 0 && naechste < g.Rows.Count && !g.Rows[naechste].Visible) naechste += richtung;
                if (naechste < 0 || naechste >= g.Rows.Count) return false;
                zeile = naechste;
                i = richtung > 0 ? 0 : spalten.Count - 1;
            }
            var zelle = g.Rows[zeile].Cells[spalten[i].Index];
            if (!ZelleAenderbar(zelle)) continue;
            try
            {
                if (g.SelectionMode != DataGridViewSelectionMode.FullRowSelect) g.ClearSelection();
                g.CurrentCell = zelle;
                if (g.SelectionMode != DataGridViewSelectionMode.FullRowSelect) zelle.Selected = true;
            }
            catch (InvalidOperationException) { return false; }
            return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------------------------------------------------------
    // Felder leeren und einfügen – immer über den normalen Eingabeweg (Prüfung und Übernahme wie beim Tippen)
    // -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Wert in eine Zelle eintragen wie per Tastatur (Bearbeiten, Text setzen, Übernehmen). false = ungültig bzw. nicht änderbar.</summary>
    public static bool Setzen(DataGridView g, DataGridViewCell zelle, string text)
    {
        if (!ZelleAenderbar(zelle)) return false;
        try
        {
            if (g.CurrentCell != zelle) g.CurrentCell = zelle;
            if (g.CurrentCell != zelle || !g.BeginEdit(true)) return false;
            switch (g.EditingControl)
            {
                case TextBox tb:
                    tb.Text = text;
                    break;
                case ComboBox cb when text.Length == 0:
                    if (cb.DropDownStyle == ComboBoxStyle.DropDownList)
                    {
                        var leer = -1;
                        for (var n = 0; n < cb.Items.Count && leer < 0; n++)
                            if ((cb.GetItemText(cb.Items[n]) ?? "").Trim().Length == 0) leer = n;
                        cb.SelectedIndex = leer;
                    }
                    else cb.Text = "";
                    break;
                case ComboBox cb:
                    var treffer = ListenSuche.Treffer(cb, text.Trim());
                    if (treffer >= 0) cb.SelectedIndex = treffer;
                    else if (cb.DropDownStyle != ComboBoxStyle.DropDownList) cb.Text = text;
                    else { g.CancelEdit(); g.EndEdit(); return false; }
                    break;
                default:
                    g.CancelEdit();
                    g.EndEdit();
                    return false;
            }
            g.NotifyCurrentCellDirty(true);
            if (g.EndEdit()) return true;
            g.CancelEdit();
            g.EndEdit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            try { g.CancelEdit(); g.EndEdit(); } catch (InvalidOperationException) { }
        }
        return false;
    }

    /// <summary>Markierte änderbare Felder leeren (Entf) – eine Rückgängig-Stufe; nicht leerbare Felder (Pflichtfelder) bleiben mit Hinweis stehen.</summary>
    public static void ZellenLeeren(DataGridView g, string beschreibung = "Felder leeren")
    {
        // ganze Zeile markiert (Zeilenmarkierung): nur das aktuelle Feld leeren, nie still eine ganze Zeile
        var ziele = g.SelectionMode == DataGridViewSelectionMode.FullRowSelect ? new List<(int RowIndex, int ColumnIndex)>()
            : g.SelectedCells.Cast<DataGridViewCell>().Where(ZelleAenderbar).Select(c => (c.RowIndex, c.ColumnIndex)).ToList();
        if (ziele.Count == 0 && g.CurrentCell is { } cur && ZelleAenderbar(cur)) ziele.Add((cur.RowIndex, cur.ColumnIndex));
        if (ziele.Count == 0) return;
        ziele = ziele.OrderBy(z => z.RowIndex).ThenBy(z => g.Columns[z.ColumnIndex].DisplayIndex).ToList();
        Eintragen(g, ziele.Select(z => (z.RowIndex, z.ColumnIndex, "")).ToList(), beschreibung);
    }

    /// <summary>
    /// Strg+V: Text der Zwischenablage ab der aktuellen Zelle eintragen (Tabulator trennt Spalten, Zeilenumbruch Zeilen). Ein einzelner
    /// Wert wird in alle markierten Felder eingetragen. Alles zusammen ist eine Rückgängig-Stufe.
    /// </summary>
    public static void Einfuegen(DataGridView g, string? text = null)
    {
        text ??= NurText.Einfuegetext();
        if (string.IsNullOrEmpty(text) || g.CurrentCell is not { } start) return;
        var zeilen = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (zeilen.Count > 1 && zeilen[^1].Length == 0) zeilen.RemoveAt(zeilen.Count - 1);
        var werte = zeilen.Select(z => z.Split('\t')).ToList();
        var ziele = new List<(int Zeile, int Spalte, string Text)>();
        if (werte.Count == 1 && werte[0].Length == 1 && g.SelectedCells.Count > 1)
        {
            foreach (var c in g.SelectedCells.Cast<DataGridViewCell>().OrderBy(c => c.RowIndex).ThenBy(c => c.OwningColumn.DisplayIndex))
                ziele.Add((c.RowIndex, c.ColumnIndex, werte[0][0]));
        }
        else
        {
            var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            var s0 = spalten.IndexOf(start.OwningColumn);
            var zeile = start.RowIndex;
            foreach (var w in werte)
            {
                while (zeile < g.Rows.Count && !g.Rows[zeile].Visible) zeile++;
                if (zeile >= g.Rows.Count) break;
                for (var k = 0; k < w.Length && s0 + k < spalten.Count; k++) ziele.Add((zeile, spalten[s0 + k].Index, w[k].Trim()));
                zeile++;
            }
        }
        Eintragen(g, ziele, "Einfügen");
    }

    /// <summary>
    /// Masken, die mit Strg+C ganze Datensätze merken (Absenzen, Veranstaltungen – Handbuch): die markierten Zellen kommen
    /// zusätzlich wie überall als reiner Text in die Zwischenablage. Liefert diesen Text als Kennung für <see cref="DatensatzEinfuegen"/>.
    /// </summary>
    public static string DatensaetzeKopieren(DataGridView g)
    {
        var text = NurText.TabellenText(g);
        NurText.Kopieren(text);
        return text;
    }

    /// <summary>
    /// Strg+V fügt die gemerkten Datensätze nur ein, solange seit dem Kopieren nichts anderes in die Zwischenablage kam; sonst
    /// fügt Strg+V wie in jeder Tabelle den Text der Zwischenablage in die Felder ein.
    /// </summary>
    public static bool DatensatzEinfuegen(string? gemerkt) =>
        !string.IsNullOrEmpty(gemerkt) && (NurText.Einfuegetext() is not { } jetzt || jetzt == gemerkt);

    private static void Eintragen(DataGridView g, List<(int Zeile, int Spalte, string Text)> ziele, string beschreibung)
    {
        var start = g.CurrentCell is { } c ? (c.RowIndex, c.ColumnIndex) : (-1, -1);
        var zeilen = g.Rows.Count;
        int ok = 0, falsch = 0;
        using (AppZustand.Doc.Sammeln(beschreibung))
        {
            foreach (var (z, s, t) in ziele)
            {
                // die Tabelle wurde während der Übernahme neu aufgebaut (andere Zeilenzahl): nicht in fremde Zeilen schreiben
                if (g.IsDisposed || g.Rows.Count != zeilen || z >= g.Rows.Count || s >= g.Columns.Count) break;
                var zelle = g.Rows[z].Cells[s];
                if (!ZelleAenderbar(zelle)) continue;
                if ((NurText.ZellText(zelle)) == t) { ok++; continue; }
                if (Setzen(g, zelle, t)) ok++; else falsch++;
            }
        }
        try
        {
            if (!g.IsDisposed && start.Item1 >= 0 && start.Item1 < g.Rows.Count && start.Item2 < g.Columns.Count && g.Rows[start.Item1].Cells[start.Item2] is { Visible: true } zurueck)
                g.CurrentCell = zurueck;
        }
        catch (InvalidOperationException) { }
        if (falsch > 0) Ui.EingabeHinweis(g, $"{falsch} Feld(er) konnten nicht übernommen werden (ungültiger Wert bzw. Pflichtfeld) – die übrigen {ok} wurden übernommen.");
        else if (ok > 0) Ui.Statushinweis($"{beschreibung}: {ok} Feld(er) übernommen (rückgängig mit Strg+Z).");
    }

    // -------------------------------------------------------------------------------------------------------------------------
    // Fokus nach Neuaufbau: Zeile und Spalte bleiben erhalten
    // -------------------------------------------------------------------------------------------------------------------------

    /// <summary>Fachlicher Schlüssel einer Zeile für das Wiederfinden nach einem Neuaufbau (Id des Datensatzes, sonst Inhalt).</summary>
    public static string? ZeilenSchluessel(DataGridViewRow r)
    {
        var t = r.Tag;
        if (t is null) return null;
        if (t is string or ValueType) return t.ToString();
        var typ = t.GetType();
        if (typ.GetProperty("Id")?.GetValue(t) is { } id) return typ.Name + ":" + id;
        foreach (var p in typ.GetProperties())
            if (p.GetIndexParameters().Length == 0 && p.PropertyType.IsClass && p.PropertyType != typeof(string) && p.GetValue(t) is { } inner &&
                inner.GetType().GetProperty("Id")?.GetValue(inner) is { } innerId)
                return typ.Name + ":" + p.Name + ":" + innerId;
        return t.ToString();
    }

    public sealed record Stand(DataGridView Tabelle, TabellenStand Daten, int Zeile, int Spalte);

    /// <summary>Stand aller sichtbaren Tabellen eines Fensters vor dem Neuaufbau.</summary>
    public static List<Stand> StandMerken(Control fenster)
    {
        var liste = new List<Stand>();
        foreach (var g in Ui.Tabellen(fenster))
            if (g.Visible && g.Rows.Count > 1 && g.CurrentCell is { } c && !g.IsCurrentCellInEditMode)
                liste.Add(new Stand(g, TabellenStand.Merken(g, ZeilenSchluessel), c.RowIndex, c.ColumnIndex));
        return liste;
    }

    /// <summary>
    /// Nach dem Neuaufbau: Ist eine Tabelle an den Anfang gesprungen (bzw. ohne aktuelle Zelle), obwohl vorher eine andere Zelle
    /// aktuell war, wird der vorherige Datensatz (Schlüssel, sonst Position) samt Spalte und Bildlauf wieder aktuell.
    /// Fenster, die ihre Position selbst erhalten oder bewusst eine andere Zeile wählen, bleiben unberührt.
    /// </summary>
    public static void StandWiederherstellen(List<Stand> staende)
    {
        foreach (var s in staende)
        {
            var g = s.Tabelle;
            if (g.IsDisposed || !g.Visible || g.Rows.Count == 0 || g.IsCurrentCellInEditMode) continue;
            var erste = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).Select(c => c.Index).DefaultIfEmpty(0).First();
            var amAnfang = g.CurrentCell is null || (g.CurrentCell.RowIndex == 0 && g.CurrentCell.ColumnIndex == erste);
            if (!amAnfang || (s.Zeile == 0 && s.Spalte == erste)) continue;
            s.Daten.Wiederherstellen(g, ZeilenSchluessel);
        }
    }

    // -------------------------------------------------------------------------------------------------------------------------
    // Ungültige Eingaben
    // -------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Eine Eingabe wurde nach dem Übernehmen als ungültig erkannt: Hinweis (Statuszeile, Tooltip) und die Eingabe steht – nach
    /// einem Neuaufbau – wieder zur Korrektur in der Zelle (Esc verwirft sie). Kein Meldungsfenster, kein stiller Verlust.
    /// </summary>
    public static void Ungueltig(DataGridView g, int zeile, int spalte, string eingabe, string hinweis)
    {
        Ui.EingabeHinweis(g, hinweis);
        if (g.IsDisposed || !g.IsHandleCreated) return;
        g.BeginInvoke(() =>
        {
            if (g.IsDisposed || zeile < 0 || spalte < 0 || zeile >= g.Rows.Count || spalte >= g.Columns.Count || g.IsCurrentCellInEditMode) return;
            var zelle = g.Rows[zeile].Cells[spalte];
            if (!ZelleAenderbar(zelle)) return;
            try
            {
                g.CurrentCell = zelle;
                if (g.BeginEdit(false) && g.EditingControl is TextBox tb)
                {
                    tb.Text = eingabe;
                    tb.SelectAll();
                }
            }
            catch (InvalidOperationException) { }
            Ui.EingabeHinweis(g, hinweis);
        });
    }

    /// <summary>Wie <see cref="Ungueltig(DataGridView, int, int, string, string)"/> für die aktuelle Zelle.</summary>
    public static void Ungueltig(DataGridView g, string eingabe, string hinweis) =>
        Ungueltig(g, g.CurrentCell?.RowIndex ?? -1, g.CurrentCell?.ColumnIndex ?? -1, eingabe, hinweis);

    private static void DatenFehler(DataGridView g, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        // beim Übernehmen/Prüfen einer Eingabe: Eingabe bleibt stehen, Hinweis wie im Vorbild (Statuszeile, Tooltip)
        if ((e.Context & (DataGridViewDataErrorContexts.Commit | DataGridViewDataErrorContexts.Parsing)) != 0 && g.IsCurrentCellInEditMode)
        {
            e.Cancel = true;
            var spalte = e.ColumnIndex >= 0 && e.ColumnIndex < g.Columns.Count ? g.Columns[e.ColumnIndex].HeaderText.Replace("\n", " ") : "";
            Ui.EingabeHinweis(g, $"Ungültige Eingabe{(spalte.Length > 0 ? $" im Feld „{spalte}“" : "")} – bitte korrigieren oder mit Esc verwerfen.");
        }
    }

    // -------------------------------------------------------------------------------------------------------------------------
    // Tasten während einer Zelleingabe (Nachrichtenfilter: die Eingabe-Steuerelemente verarbeiten Enter/Esc sonst selbst)
    // -------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Taste während einer Zelleingabe: Enter übernimmt (geprüft) und springt weiter, Esc verwirft. Liefert true, wenn verarbeitet.
    /// Auch von <see cref="BleibendeTabelle"/> verwendet, damit alle Tabellen gleich reagieren.
    /// </summary>
    public static bool Bearbeitungstaste(DataGridView g, Keys keyData)
    {
        if (!g.IsCurrentCellInEditMode) return false;
        var taste = keyData & Keys.KeyCode;
        var mod = keyData & Keys.Modifiers;
        if (taste == Keys.Enter && (mod & (Keys.Control | Keys.Alt)) == 0)
        {
            // nicht bestätigter Vorschlag (Textverlauf): Enter übernimmt nur den getippten Text
            if (g.EditingControl is TextBox t2) Textverlauf.ListeSchliessen(t2);
            if (g.EditingControl is ComboBox cb)
            {
                ListenSuche.SuchtextUebernehmen(cb);
                if (cb.DroppedDown) cb.DroppedDown = false;
            }
            bool ok;
            try { ok = g.EndEdit(); }
            catch (InvalidOperationException) { ok = false; }
            // ungültig: Eingabe bleibt stehen (Hinweis durch Prüfung bzw. DataError)
            if (!ok || g.IsCurrentCellInEditMode) return true;
            var richtung = (mod & Keys.Shift) != 0 ? -1 : 1;
            // erst nach einem durch die Übernahme ausgelösten Neuaufbau weiterspringen (Zeile/Spalte bleiben dabei erhalten)
            if (g.IsHandleCreated) g.BeginInvoke(() => { if (!g.IsDisposed && !g.IsCurrentCellInEditMode) Weiter(g, richtung); });
            else Weiter(g, richtung);
            return true;
        }
        if (taste == Keys.Escape && mod == Keys.None)
        {
            if (g.EditingControl is TextBox tb && Textverlauf.ListeSchliessen(tb)) return true;
            if (g.EditingControl is ComboBox { DroppedDown: true } cb) cb.DroppedDown = false;
            try
            {
                g.CancelEdit();
                g.EndEdit();
            }
            catch (InvalidOperationException) { }
            Ui.Statushinweis("Eingabe verworfen.");
            return true;
        }
        return false;
    }

    /// <summary>Hat der Tabellentyp eine eigene Enter-Logik (überschriebenes ProcessDialogKey, z. B. Unterricht: nächstes Eingabefeld)?</summary>
    private static bool HatEigeneEnterLogik(DataGridView g)
    {
        var typ = g.GetType();
        if (typ == typeof(DataGridView) || typ == typeof(BleibendeTabelle)) return false;
        if (EigeneEnterLogik.TryGetValue(typ, out var v)) return (bool)v;
        var m = typ.GetMethod("ProcessDialogKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(Keys) }, null);
        var eigen = m != null && m.DeclaringType != typeof(DataGridView) && m.DeclaringType != typeof(BleibendeTabelle);
        EigeneEnterLogik.AddOrUpdate(typ, eigen);
        return eigen;
    }

    private static DataGridView? BearbeiteteTabelle(Control c)
    {
        for (var p = c; p != null; p = p.Parent)
            if (p is DataGridView g)
                return g.IsCurrentCellInEditMode && g.EditingControl is { } ec && (ec == c || ec.Contains(c)) ? g : null;
        return null;
    }

    /// <summary>Taste wie vom Benutzer gedrückt verarbeiten (Selbsttest; Modifizierer aus <paramref name="keyData"/>).</summary>
    internal static bool Senden(Control ziel, Keys keyData)
    {
        if (Verarbeiten(ziel, keyData)) return true;
        if (ziel is DataGridView g && !g.IsCurrentCellInEditMode)
        {
            var e = new KeyEventArgs(keyData);
            var vorher = g.CurrentCell;
            typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(g, new object[] { e });
            return e.Handled || g.CurrentCell != vorher;
        }
        return false;
    }

    private static bool Verarbeiten(Control c, Keys keyData)
    {
        var taste = keyData & Keys.KeyCode;
        if ((keyData & Keys.Modifiers) == Keys.Control && taste is Keys.Z or Keys.Y)
        {
            // Textfelder (auch die Eingabe einer Tabellenzelle) behalten ihr eigenes Rückgängig
            if (c is TextBoxBase) return false;
            var form = c.FindForm();
            if (form is null || form.Modal || c is ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }) return false;
            if (BearbeiteteTabelle(c) is { } eg) { try { eg.CancelEdit(); eg.EndEdit(); } catch (InvalidOperationException) { } }
            Ui.Rueckgaengig(taste == Keys.Y);
            return true;
        }
        if (taste is not (Keys.Enter or Keys.Escape)) return false;
        var g = BearbeiteteTabelle(c);
        if (g is null || HatEigeneEnterLogik(g)) return false;
        return Bearbeitungstaste(g, keyData);
    }

    private sealed class Filter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x100;
            if (m.Msg != WM_KEYDOWN) return false;
            var taste = (Keys)(int)(long)m.WParam & Keys.KeyCode;
            if (taste is not (Keys.Enter or Keys.Escape or Keys.Z or Keys.Y)) return false;
            if (Control.FromChildHandle(m.HWnd) is not { } c) return false;
            try { return Verarbeiten(c, taste | Control.ModifierKeys); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException)
            {
                Ui.Fehler(ex);
                return true;
            }
        }
    }
}
