namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Eigene Tabellenformate für jede Tabelle: Rechtsklick auf die Spaltenköpfe blendet Spalten ein/aus, speichert die
/// aktuelle Anordnung (Sichtbarkeit, Reihenfolge, Breite) als benanntes Format, wechselt zwischen Formaten oder setzt zurück.
/// Spalten lassen sich mit der Maus verschieben. Das zuletzt gewählte Format wird beim Öffnen wieder angewendet.
/// </summary>
public static class TabellenFormate
{
    private sealed class Zustand
    {
        public string Schluessel = "";
        public List<Tabellenspalte> Standard = new();
        public bool Anwenden;
    }

    public static void Aktivieren(DataGridView g, string schluessel)
    {
        var z = new Zustand { Schluessel = schluessel };
        g.AllowUserToOrderColumns = true;
        void Merken()
        {
            if (g.Columns.Count == 0) return;
            z.Standard = Aufnehmen(g);
            Anwenden(g, z);
        }
        if (g.IsHandleCreated) Merken();
        else g.HandleCreated += (_, _) => Merken();
        g.ColumnAdded += (_, _) =>
        {
            // Spalten werden manchmal neu aufgebaut: Format nach dem Aufbau erneut anwenden
            if (z.Anwenden || !g.IsHandleCreated) return;
            z.Anwenden = true;
            g.BeginInvoke(() => { z.Anwenden = false; Merken(); });
        };
        g.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            Menue(g, z).Show(g, g.PointToClient(Cursor.Position));
        };
    }

    private static List<Tabellenspalte> Aufnehmen(DataGridView g) =>
        g.Columns.Cast<DataGridViewColumn>().Select(c => new Tabellenspalte { Name = c.Name, Sichtbar = c.Visible, Breite = c.Width, Position = c.DisplayIndex }).ToList();

    private static List<Tabellenformat> Formate(string schluessel)
    {
        var e = AppZustand.Einstellungen;
        if (!e.Tabellenformate.TryGetValue(schluessel, out var l)) e.Tabellenformate[schluessel] = l = new List<Tabellenformat>();
        return l;
    }

    private static void Anwenden(DataGridView g, Zustand z)
    {
        var e = AppZustand.Einstellungen;
        if (!e.AktiveTabellenformate.TryGetValue(z.Schluessel, out var name)) return;
        var f = Formate(z.Schluessel).FirstOrDefault(x => x.Name == name);
        if (f != null) Setzen(g, f.Spalten);
    }

    private static void Setzen(DataGridView g, List<Tabellenspalte> spalten)
    {
        foreach (var s in spalten.OrderBy(x => x.Position))
        {
            if (!g.Columns.Contains(s.Name)) continue;
            var c = g.Columns[s.Name]!;
            c.Visible = s.Sichtbar;
            if (s.Breite > 10 && c.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) c.Width = s.Breite;
            if (s.Position >= 0 && s.Position < g.Columns.Count) c.DisplayIndex = s.Position;
        }
    }

    private static ContextMenuStrip Menue(DataGridView g, Zustand z)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        var spalten = new ToolStripMenuItem("Spalten ein-/ausblenden", Symbole.Get("rollup"));
        foreach (var c in g.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
        {
            var col = c;
            var it = new ToolStripMenuItem(string.IsNullOrWhiteSpace(c.HeaderText) ? c.Name : c.HeaderText) { Checked = c.Visible, CheckOnClick = true };
            it.CheckedChanged += (_, _) =>
            {
                if (!it.Checked && g.Columns.Cast<DataGridViewColumn>().Count(x => x.Visible) <= 1) { it.Checked = true; return; }
                col.Visible = it.Checked;
            };
            spalten.DropDownItems.Add(it);
        }
        spalten.DropDown.Closing += (_, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
        cm.Items.Add(spalten);
        cm.Items.Add(new ToolStripSeparator());
        var e = AppZustand.Einstellungen;
        e.AktiveTabellenformate.TryGetValue(z.Schluessel, out var aktiv);
        var formate = Formate(z.Schluessel);
        var standard = new ToolStripMenuItem("Standardformat") { Checked = aktiv is null };
        standard.Click += (_, _) =>
        {
            e.AktiveTabellenformate.Remove(z.Schluessel);
            e.Speichern();
            Setzen(g, z.Standard);
        };
        cm.Items.Add(standard);
        foreach (var f in formate)
        {
            var fmt = f;
            var it = new ToolStripMenuItem(f.Name) { Checked = f.Name == aktiv };
            it.Click += (_, _) =>
            {
                e.AktiveTabellenformate[z.Schluessel] = fmt.Name;
                e.Speichern();
                Setzen(g, z.Standard);
                Setzen(g, fmt.Spalten);
            };
            cm.Items.Add(it);
        }
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Aktuelle Anordnung als Format speichern…", Symbole.Get("speichern"), (_, _) =>
        {
            var name = Ui.Eingabe("Name des Tabellenformats:", "Tabellenformat speichern", aktiv ?? "Mein Format");
            if (string.IsNullOrWhiteSpace(name)) return;
            formate.RemoveAll(x => x.Name == name);
            formate.Add(new Tabellenformat { Name = name.Trim(), Spalten = Aufnehmen(g) });
            e.AktiveTabellenformate[z.Schluessel] = name.Trim();
            e.Speichern();
        });
        if (aktiv != null)
            cm.Items.Add($"Format \"{aktiv}\" löschen", Symbole.Get("loeschen"), (_, _) =>
            {
                formate.RemoveAll(x => x.Name == aktiv);
                e.AktiveTabellenformate.Remove(z.Schluessel);
                e.Speichern();
                Setzen(g, z.Standard);
            });
        cm.Items.Add("Alle Spalten einblenden", null, (_, _) => { foreach (DataGridViewColumn c in g.Columns) c.Visible = true; });
        return cm;
    }
}
