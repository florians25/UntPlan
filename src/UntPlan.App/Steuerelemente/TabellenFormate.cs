using UntPlan.App.Dialoge;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Spaltenformatierung für jede Tabelle. Rechtsklick auf einen Spaltenkopf öffnet das Formatmenü:
/// <list type="bullet">
/// <item>Spalten ein- und ausblenden.</item>
/// <item>Spalten anpassen (Überschrift, Breite, Ausrichtung, Reihenfolge, fixieren).</item>
/// <item>Benannte Formate speichern, wechseln und löschen.</item>
/// <item>Den Standard wiederherstellen.</item>
/// </list>
/// Jede Änderung (auch Verschieben oder Verbreitern mit der Maus) wird sofort als aktuelle Anordnung gemerkt und nach jedem
/// Neuaufbau der Tabelle wieder angewendet. Vom Benutzer ausgeblendete Spalten bleiben ausgeblendet, auch wenn das Fenster
/// Spalten selbst ein- und ausblendet.
/// </summary>
public static class TabellenFormate
{
    private sealed class Zustand
    {
        public string Schluessel = "";
        public readonly Dictionary<string, Tabellenspalte> Standard = new();
        public bool Anwenden;
        public bool AnwendenGeplant;
        public bool SpeichernGeplant;
        /// <summary>Benutzer zieht/ändert gerade Spalten (zwischen MouseDown und MouseUp).</summary>
        public bool Ziehen;
        public bool Geaendert;
    }

    private static readonly Dictionary<DataGridView, Zustand> Zustaende = new();

    /// <summary>Spalten, deren Überschrift aus einem Tabellenformat stammt.</summary>
    private static readonly HashSet<DataGridViewColumn> EigeneKoepfe = new();

    public static void Aktivieren(DataGridView g, string schluessel)
    {
        if (Zustaende.ContainsKey(g)) return;
        var z = new Zustand { Schluessel = schluessel };
        Zustaende[g] = z;
        g.Disposed += (_, _) => Zustaende.Remove(g);
        g.AllowUserToOrderColumns = true;
        if (g.IsHandleCreated) Neuaufgebaut(g, z);
        else g.HandleCreated += (_, _) => Neuaufgebaut(g, z);
        // Spalten werden manchmal neu aufgebaut: Format nach dem Aufbau erneut anwenden
        g.ColumnAdded += (_, _) => AnwendenPlanen(g, z);
        // Änderungen durch den Benutzer (Spalte verschieben, Breite ziehen) erkennen und nach dem Loslassen speichern
        g.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { z.Ziehen = true; z.Geaendert = false; } };
        g.MouseUp += (_, _) =>
        {
            if (z.Ziehen && z.Geaendert) SpeichernPlanen(g, z);
            z.Ziehen = z.Geaendert = false;
        };
        g.ColumnWidthChanged += (_, e) =>
        {
            if (z.Anwenden || e.Column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) return;
            if (z.Ziehen) z.Geaendert = true;
        };
        g.ColumnDisplayIndexChanged += (_, _) =>
        {
            if (z.Anwenden) return;
            if (z.Ziehen) z.Geaendert = true;
            // Verschieben per Ziehen endet manchmal ohne MouseUp an der Tabelle
            if (Control.MouseButtons == MouseButtons.None && z.Ziehen) SpeichernPlanen(g, z);
        };
        // vom Programm wieder eingeblendete, aber vom Benutzer ausgeblendete Spalten verborgen halten
        g.ColumnStateChanged += (_, e) =>
        {
            if (z.Anwenden || e.StateChanged != DataGridViewElementStates.Visible || !e.Column.Visible) return;
            if (Layout(z.Schluessel) is { } l && l.FirstOrDefault(x => x.Name == Schluessel(e.Column)) is { Sichtbar: false } && SichtbareAndere(g, e.Column) > 0)
                g.BeginInvoke(() => { if (!g.IsDisposed) { z.Anwenden = true; e.Column.Visible = false; z.Anwenden = false; } });
        };
        g.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            Menue(g, z, e.ColumnIndex).Show(g, g.PointToClient(Cursor.Position));
        };
        // Strg+E: Spalten an die optimale Breite (Überschrift und Inhalt), Strg+F: an die Breite des Inhalts anpassen
        g.KeyDown += (_, e) =>
        {
            if (g.IsCurrentCellInEditMode || e.Modifiers != Keys.Control) return;
            if (e.KeyCode == Keys.E) { BreiteAnpassen(g, z, DataGridViewAutoSizeColumnMode.AllCells); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F) { BreiteAnpassen(g, z, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader); e.Handled = e.SuppressKeyPress = true; }
        };
    }

    /// <summary>Alle sichtbaren Spalten (bzw. eine Spalte) automatisch verbreitern bzw. verschmälern und die Anordnung merken.</summary>
    private static void BreiteAnpassen(DataGridView g, Zustand z, DataGridViewAutoSizeColumnMode modus, DataGridViewColumn? nur = null)
    {
        foreach (var c in g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill && (nur is null || c == nur)))
        {
            var breite = c.GetPreferredWidth(modus, true);
            if (modus == DataGridViewAutoSizeColumnMode.AllCellsExceptHeader && breite < 24) breite = 24;
            c.Width = Math.Max(c.MinimumWidth, breite);
        }
        Merken(g, z);
    }

    /// <summary>Spalte vom Programm aus ein- bzw. ausblenden und als Benutzereinstellung merken (z. B. Dialog „Felder der Ansicht“).</summary>
    public static void SpalteSichtbar(DataGridView g, DataGridViewColumn c, bool sichtbar)
    {
        if (!Zustaende.TryGetValue(g, out var z)) { c.Visible = sichtbar; return; }
        if (c.Visible == sichtbar && (sichtbar ? !VomBenutzerVerborgen.Contains(c) : VomBenutzerVerborgen.Contains(c))) return;
        Sichtbarkeit(g, z, c, sichtbar);
    }

    /// <summary>Menü „Spalten ein-/ausblenden, Format …“ einer Tabelle an einer Bildschirmposition zeigen.</summary>
    public static void MenueZeigen(DataGridView g, Point bildschirm)
    {
        if (!Zustaende.TryGetValue(g, out var z)) return;
        Menue(g, z).Show(bildschirm);
    }

    /// <summary>Eindeutiger Name einer Spalte (Name, sonst Überschrift, sonst Position).</summary>
    private static string Schluessel(DataGridViewColumn c) =>
        !string.IsNullOrEmpty(c.Name) ? c.Name : !string.IsNullOrWhiteSpace(c.HeaderText) ? "#" + c.HeaderText : "#" + c.Index;

    private static int SichtbareAndere(DataGridView g, DataGridViewColumn c) => g.Columns.Cast<DataGridViewColumn>().Count(x => x != c && x.Visible);

    private static List<Tabellenspalte>? Layout(string schluessel) =>
        AppZustand.Einstellungen.Tabellenlayouts.TryGetValue(schluessel, out var l) && l.Count > 0 ? l : null;

    private static void AnwendenPlanen(DataGridView g, Zustand z)
    {
        if (z.Anwenden || z.AnwendenGeplant || !g.IsHandleCreated) return;
        z.AnwendenGeplant = true;
        g.BeginInvoke(() => { z.AnwendenGeplant = false; if (!g.IsDisposed) Neuaufgebaut(g, z); });
    }

    private static void SpeichernPlanen(DataGridView g, Zustand z)
    {
        if (z.SpeichernGeplant || !g.IsHandleCreated) return;
        z.SpeichernGeplant = true;
        g.BeginInvoke(() => { z.SpeichernGeplant = false; if (!g.IsDisposed) Merken(g, z); });
    }

    /// <summary>Nach (Neu-)Aufbau: Standard der neuen Spalten erfassen, dann die gemerkte Anordnung anwenden.</summary>
    private static void Neuaufgebaut(DataGridView g, Zustand z)
    {
        if (g.Columns.Count == 0) return;
        foreach (DataGridViewColumn c in g.Columns)
        {
            // Füllspalten (z. B. Text) nie auf wenige Pixel zusammendrücken
            if (c.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill && c.MinimumWidth < 140) c.MinimumWidth = 140;
            var k = Schluessel(c);
            if (!z.Standard.ContainsKey(k))
                z.Standard[k] = new Tabellenspalte { Name = k, Sichtbar = c.Visible, Breite = c.Width, Position = c.DisplayIndex, Ueberschrift = c.HeaderText, Ausrichtung = 0, Fixiert = c.Frozen };
        }
        if (Layout(z.Schluessel) is { } l) Setzen(g, z, l);
    }

    /// <summary>Aktuelle Anordnung der Tabelle aufnehmen (eigene Überschriften nur, wenn sie vom Standard abweichen).</summary>
    private static List<Tabellenspalte> Aufnehmen(DataGridView g, Zustand z) =>
        g.Columns.Cast<DataGridViewColumn>().Select(c =>
        {
            var k = Schluessel(c);
            var std = z.Standard.GetValueOrDefault(k);
            var alt = Layout(z.Schluessel)?.FirstOrDefault(x => x.Name == k);
            return new Tabellenspalte
            {
                Name = k,
                // vom Programm ausgeblendete Spalten gelten weiter als sichtbar, nur der Benutzer blendet aus
                Sichtbar = alt?.Sichtbar ?? true,
                Breite = c.Width,
                Position = c.DisplayIndex,
                Ueberschrift = alt?.Ueberschrift ?? "",
                Ausrichtung = alt?.Ausrichtung ?? 0,
                Fixiert = alt?.Fixiert ?? false,
            };
        }).ToList();

    private static void Merken(DataGridView g, Zustand z)
    {
        AppZustand.Einstellungen.Tabellenlayouts[z.Schluessel] = Aufnehmen(g, z);
        AppZustand.Einstellungen.Speichern();
    }

    private static void LayoutSpeichern(Zustand z, List<Tabellenspalte> spalten)
    {
        AppZustand.Einstellungen.Tabellenlayouts[z.Schluessel] = spalten.Select(Kopie).ToList();
        AppZustand.Einstellungen.Speichern();
    }

    private static Tabellenspalte Kopie(Tabellenspalte s) => new()
    {
        Name = s.Name, Sichtbar = s.Sichtbar, Breite = s.Breite, Position = s.Position, Ueberschrift = s.Ueberschrift, Ausrichtung = s.Ausrichtung, Fixiert = s.Fixiert,
    };

    private static void Setzen(DataGridView g, Zustand z, List<Tabellenspalte> spalten)
    {
        z.Anwenden = true;
        try
        {
            var spaltenNachName = g.Columns.Cast<DataGridViewColumn>().GroupBy(Schluessel).ToDictionary(x => x.Key, x => x.First());
            // Reihenfolge: gemerkte Spalten in ihrer Reihenfolge, neue Spalten an ihrer Standardposition dahinter
            var i = 0;
            foreach (var s in spalten.OrderBy(x => x.Position))
            {
                if (!spaltenNachName.TryGetValue(s.Name, out var c)) continue;
                if (i < g.Columns.Count) c.DisplayIndex = i++;
            }
            foreach (var s in spalten)
            {
                if (!spaltenNachName.TryGetValue(s.Name, out var c)) continue;
                var std = z.Standard.GetValueOrDefault(s.Name);
                if (!s.Sichtbar && SichtbareAndere(g, c) > 0) c.Visible = false;
                else if (s.Sichtbar && std is { Sichtbar: true } && !c.Visible && WarVomBenutzerVerborgen(g, z, c)) c.Visible = true;
                if (s.Breite > 10 && c.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) c.Width = s.Breite;
                // nur eigene Überschriften setzen (Fenster mit wechselnden Überschriften, z. B. Datum, behalten ihre)
                if (!string.IsNullOrEmpty(s.Ueberschrift)) c.HeaderText = s.Ueberschrift;
                else if (EigeneKoepfe.Remove(c)) c.HeaderText = std?.Ueberschrift ?? c.HeaderText;
                if (!string.IsNullOrEmpty(s.Ueberschrift)) EigeneKoepfe.Add(c);
                c.DefaultCellStyle.Alignment = s.Ausrichtung switch
                {
                    1 => DataGridViewContentAlignment.MiddleLeft,
                    2 => DataGridViewContentAlignment.MiddleCenter,
                    3 => DataGridViewContentAlignment.MiddleRight,
                    _ => c.DefaultCellStyle.Alignment == DataGridViewContentAlignment.NotSet ? DataGridViewContentAlignment.NotSet : c.DefaultCellStyle.Alignment,
                };
            }
            // Fixieren: alle Spalten bis zur letzten fixierten (in Anzeigereihenfolge)
            var fixiert = spalten.Where(s => s.Fixiert && spaltenNachName.ContainsKey(s.Name)).Select(s => spaltenNachName[s.Name].DisplayIndex).DefaultIfEmpty(-1).Max();
            if (fixiert >= 0 || spalten.Any(s => s.Fixiert == false))
                foreach (DataGridViewColumn c in g.Columns)
                {
                    var std = z.Standard.GetValueOrDefault(Schluessel(c));
                    c.Frozen = fixiert >= 0 ? c.DisplayIndex <= fixiert : std?.Fixiert ?? false;
                }
        }
        finally
        {
            z.Anwenden = false;
        }
    }

    // Eine Spalte, die nur durch eine frühere Benutzereinstellung verborgen ist, beim Einblenden wieder zeigen
    // (das Programm selbst ausgeblendete Spalten bleiben unberührt).
    private static readonly HashSet<DataGridViewColumn> VomBenutzerVerborgen = new();

    private static bool WarVomBenutzerVerborgen(DataGridView g, Zustand z, DataGridViewColumn c) => VomBenutzerVerborgen.Remove(c);

    private static void Sichtbarkeit(DataGridView g, Zustand z, DataGridViewColumn c, bool sichtbar)
    {
        if (!sichtbar && SichtbareAndere(g, c) == 0) return;
        z.Anwenden = true;
        c.Visible = sichtbar;
        z.Anwenden = false;
        if (sichtbar) VomBenutzerVerborgen.Remove(c);
        else VomBenutzerVerborgen.Add(c);
        var l = Aufnehmen(g, z);
        var e = l.First(x => x.Name == Schluessel(c));
        e.Sichtbar = sichtbar;
        LayoutSpeichern(z, l);
    }

    private static List<Tabellenformat> Formate(string schluessel)
    {
        var e = AppZustand.Einstellungen;
        if (!e.Tabellenformate.TryGetValue(schluessel, out var l)) e.Tabellenformate[schluessel] = l = new List<Tabellenformat>();
        return l;
    }

    private static void StandardWiederherstellen(DataGridView g, Zustand z)
    {
        var e = AppZustand.Einstellungen;
        e.Tabellenlayouts.Remove(z.Schluessel);
        e.AktiveTabellenformate.Remove(z.Schluessel);
        e.Speichern();
        var std = z.Standard.Values.Select(Kopie).ToList();
        foreach (var c in g.Columns.Cast<DataGridViewColumn>().Where(c => VomBenutzerVerborgen.Contains(c)).ToList())
        {
            VomBenutzerVerborgen.Remove(c);
            if (z.Standard.GetValueOrDefault(Schluessel(c))?.Sichtbar == true) c.Visible = true;
        }
        Setzen(g, z, std);
    }

    private static ContextMenuStrip Menue(DataGridView g, Zustand z, int spalte = -1)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        // automatische Justierung der Spaltenbreite (wie im Vorbild: Rechtsklick auf die Titelzeile)
        if (spalte >= 0 && spalte < g.Columns.Count)
        {
            var col = g.Columns[spalte];
            cm.Items.Add("Spaltenbreite anpassen", null, (_, _) => BreiteAnpassen(g, z, DataGridViewAutoSizeColumnMode.ColumnHeader, col));
        }
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten anpassen", null, (_, _) => BreiteAnpassen(g, z, DataGridViewAutoSizeColumnMode.AllCells)) { ShortcutKeyDisplayString = "Strg+E" });
        cm.Items.Add(new ToolStripMenuItem("Alle Spalten an Inhalt anpassen", null, (_, _) => BreiteAnpassen(g, z, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader)) { ShortcutKeyDisplayString = "Strg+F" });
        cm.Items.Add(new ToolStripSeparator());
        var spalten = new ToolStripMenuItem("Spalten ein-/ausblenden", Symbole.Get("rollup"));
        foreach (var c in g.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
        {
            var col = c;
            var name = string.IsNullOrWhiteSpace(c.HeaderText) ? c.Name : c.HeaderText.Replace("\n", " ");
            var it = new ToolStripMenuItem(name) { Checked = c.Visible, CheckOnClick = true };
            it.CheckedChanged += (_, _) =>
            {
                if (!it.Checked && SichtbareAndere(g, col) == 0) { it.Checked = true; return; }
                Sichtbarkeit(g, z, col, it.Checked);
            };
            spalten.DropDownItems.Add(it);
        }
        spalten.DropDown.Closing += (_, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
        cm.Items.Add(spalten);
        cm.Items.Add("Spalten anpassen…", Symbole.Get("normalform"), (_, _) => Anpassen(g, z));
        cm.Items.Add(new ToolStripSeparator());
        var e = AppZustand.Einstellungen;
        e.AktiveTabellenformate.TryGetValue(z.Schluessel, out var aktiv);
        var formate = Formate(z.Schluessel);
        var fm = new ToolStripMenuItem("Tabellenformat", Symbole.Get("einstellungen"));
        var standard = new ToolStripMenuItem("Standardformat") { Checked = aktiv is null && Layout(z.Schluessel) is null };
        standard.Click += (_, _) => StandardWiederherstellen(g, z);
        fm.DropDownItems.Add(standard);
        foreach (var f in formate)
        {
            var fmt = f;
            var it = new ToolStripMenuItem(f.Name) { Checked = f.Name == aktiv };
            it.Click += (_, _) =>
            {
                StandardWiederherstellen(g, z);
                e.AktiveTabellenformate[z.Schluessel] = fmt.Name;
                LayoutSpeichern(z, fmt.Spalten);
                Setzen(g, z, fmt.Spalten);
            };
            fm.DropDownItems.Add(it);
        }
        cm.Items.Add(fm);
        cm.Items.Add("Aktuelle Anordnung als Format speichern…", Symbole.Get("speichern"), (_, _) =>
        {
            var name = Ui.Eingabe("Name des Tabellenformats:", "Tabellenformat speichern", aktiv ?? "Mein Format");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            formate.RemoveAll(x => x.Name == name);
            formate.Add(new Tabellenformat { Name = name, Spalten = (Layout(z.Schluessel) ?? Aufnehmen(g, z)).Select(Kopie).ToList() });
            e.AktiveTabellenformate[z.Schluessel] = name;
            e.Speichern();
        });
        if (aktiv != null)
            cm.Items.Add($"Format \"{aktiv}\" löschen", Symbole.Get("loeschen"), (_, _) =>
            {
                formate.RemoveAll(x => x.Name == aktiv);
                StandardWiederherstellen(g, z);
            });
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Alle Spalten einblenden", null, (_, _) =>
        {
            foreach (DataGridViewColumn c in g.Columns) if (!c.Visible) Sichtbarkeit(g, z, c, true);
        });
        cm.Items.Add("Standard wiederherstellen", null, (_, _) => StandardWiederherstellen(g, z));
        return cm;
    }

    /// <summary>Dialog „Spalten anpassen“: Sichtbarkeit, Reihenfolge, Überschrift, Breite, Ausrichtung, Fixieren.</summary>
    private static void Anpassen(DataGridView g, Zustand z)
    {
        var aktuell = Aufnehmen(g, z).OrderBy(x => x.Position).ToList();
        foreach (var s in aktuell)
        {
            var c = g.Columns.Cast<DataGridViewColumn>().First(x => Schluessel(x) == s.Name);
            s.Sichtbar = c.Visible || (Layout(z.Schluessel)?.FirstOrDefault(x => x.Name == s.Name)?.Sichtbar ?? true) && !VomBenutzerVerborgen.Contains(c);
        }
        using var d = new Dialog("Spalten anpassen", 620, 460);
        var t = new DataGridView { Left = 12, Top = 12, Width = 480, Height = 360, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(t);
        t.AllowUserToOrderColumns = false;
        t.Columns.Add(new DataGridViewCheckBoxColumn { Name = "S", HeaderText = "Zeigen", Width = 50 });
        t.Columns.Add(new DataGridViewTextBoxColumn { Name = "O", HeaderText = "Spalte", Width = 120, ReadOnly = true });
        t.Columns.Add(new DataGridViewTextBoxColumn { Name = "U", HeaderText = "Überschrift", Width = 130 });
        t.Columns.Add(new DataGridViewTextBoxColumn { Name = "B", HeaderText = "Breite", Width = 50 });
        t.Columns.Add(new DataGridViewComboBoxColumn { Name = "A", HeaderText = "Ausrichtung", Width = 80, DataSource = new[] { "Standard", "links", "zentriert", "rechts" }, FlatStyle = FlatStyle.Flat });
        t.Columns.Add(new DataGridViewCheckBoxColumn { Name = "F", HeaderText = "Fixiert", Width = 46 });
        t.DataError += (_, e) => e.ThrowException = false;
        t.CurrentCellDirtyStateChanged += (_, _) => { if (t.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell) t.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        void Zeile(Tabellenspalte s)
        {
            var std = z.Standard.GetValueOrDefault(s.Name);
            var orig = (std?.Ueberschrift ?? s.Name).Replace("\n", " ");
            var r = t.Rows.Add(s.Sichtbar, string.IsNullOrWhiteSpace(orig) ? s.Name : orig, s.Ueberschrift.Replace("\n", " "), s.Breite,
                new[] { "Standard", "links", "zentriert", "rechts" }[Math.Clamp(s.Ausrichtung, 0, 3)], s.Fixiert);
            t.Rows[r].Tag = s;
        }
        foreach (var s in aktuell) Zeile(s);
        void Verschieben(int richtung)
        {
            if (t.CurrentRow is not { } r) return;
            var i = r.Index;
            var j = i + richtung;
            if (j < 0 || j >= t.Rows.Count) return;
            t.EndEdit();
            var werte = r.Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray();
            var tag = r.Tag;
            t.Rows.RemoveAt(i);
            t.Rows.Insert(j, werte);
            t.Rows[j].Tag = tag;
            t.CurrentCell = t.Rows[j].Cells[1];
        }
        Button Knopf(string text, int top, Action a)
        {
            var b = new Button { Text = text, Left = 502, Top = top, Width = 104, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            b.Click += (_, _) => a();
            d.Controls.Add(b);
            return b;
        }
        Knopf("▲ Nach oben", 12, () => Verschieben(-1));
        Knopf("▼ Nach unten", 42, () => Verschieben(+1));
        Knopf("Alle zeigen", 84, () => { foreach (DataGridViewRow r in t.Rows) r.Cells["S"].Value = true; });
        Knopf("Standard", 114, () =>
        {
            t.Rows.Clear();
            foreach (var s in z.Standard.Values.OrderBy(x => x.Position)) Zeile(new Tabellenspalte { Name = s.Name, Sichtbar = s.Sichtbar, Breite = s.Breite, Position = s.Position });
        });
        d.Controls.Add(t);
        d.Controls.Add(new Label
        {
            Text = "Leere Überschrift = Standardüberschrift. Fixierte Spalten bleiben beim waagerechten Blättern stehen.",
            Left = 12, Top = 380, Width = 480, Height = 30, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        });
        if (d.ShowDialog(g.FindForm()) != DialogResult.OK) return;
        t.EndEdit();
        var neu = new List<Tabellenspalte>();
        var pos = 0;
        foreach (DataGridViewRow r in t.Rows)
        {
            if (r.Tag is not Tabellenspalte s) continue;
            neu.Add(new Tabellenspalte
            {
                Name = s.Name,
                Sichtbar = r.Cells["S"].Value is true,
                Position = pos++,
                Ueberschrift = r.Cells["U"].Value?.ToString()?.Trim() ?? "",
                Breite = int.TryParse(r.Cells["B"].Value?.ToString(), out var b) ? Math.Clamp(b, 15, 1000) : s.Breite,
                Ausrichtung = Array.IndexOf(new[] { "Standard", "links", "zentriert", "rechts" }, r.Cells["A"].Value?.ToString() ?? "Standard") is var a and >= 0 ? a : 0,
                Fixiert = r.Cells["F"].Value is true,
            });
        }
        if (neu.All(x => !x.Sichtbar)) neu[0].Sichtbar = true;
        foreach (var s in neu)
        {
            var c = g.Columns.Cast<DataGridViewColumn>().FirstOrDefault(x => Schluessel(x) == s.Name);
            if (c is null) continue;
            if (!s.Sichtbar) VomBenutzerVerborgen.Add(c);
            else if (VomBenutzerVerborgen.Remove(c)) { z.Anwenden = true; c.Visible = true; z.Anwenden = false; }
        }
        LayoutSpeichern(z, neu);
        Setzen(g, z, neu);
    }
}
