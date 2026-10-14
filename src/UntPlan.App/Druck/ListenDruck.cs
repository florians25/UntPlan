using System.Drawing.Printing;
using UntPlan.App.Dialoge;

namespace UntPlan.App;

/// <summary>Druck von Listen (Stammdaten, Unterricht, Statistiken): alle in der Rasteransicht aktiven Felder werden gedruckt.</summary>
public static class ListenDruck
{
    public sealed class Layout
    {
        public bool Querformat { get; set; }
        public float Schriftgroesse { get; set; } = 8.5f;
        public bool Summenzeile { get; set; }
        public string Ueberschrift { get; set; } = "";
        public string Fusszeile { get; set; } = "";
        public bool Zebra { get; set; } = true;
        public bool Gitter { get; set; }
        /// <summary>Spaltenauswahl in Druckreihenfolge (Name, Breite); leer = alle Spalten wie in der Ansicht.</summary>
        public List<DruckSpalte> Spalten { get; set; } = new();
    }

    public sealed class DruckSpalte
    {
        public string Name { get; set; } = "";
        public bool Drucken { get; set; } = true;
        public int Breite { get; set; }
    }

    /// <summary>Gespeicherte Drucklayouts je Liste (Titel ohne Datumsangaben als Schlüssel).</summary>
    private static Layout LayoutFuer(string titel)
    {
        var key = Schluessel(titel);
        if (!AppZustand.Einstellungen.Drucklayouts.TryGetValue(key, out var l)) AppZustand.Einstellungen.Drucklayouts[key] = l = new Layout();
        l.Ueberschrift = titel;
        return l;
    }

    private static string Schluessel(string titel) => new string(titel.TakeWhile(c => !char.IsDigit(c)).ToArray()).Trim(' ', '–', '-');

    /// <summary>Tabelle drucken: sichtbare Spalten in der angezeigten Reihenfolge und Breite.</summary>
    /// <param name="leerfelder">Überschriften von Spalten, die im Ausdruck als umrandetes Leerfeld erscheinen (z. B. „Unterschrift“).</param>
    /// <param name="zeilenFilter">Nur diese Zeilen drucken (z. B. „Nur neue Vertretungen“).</param>
    public static void DruckenTabelle(IWin32Window owner, string titel, DataGridView g, bool vorschau, ISet<string>? leerfelder = null, Func<DataGridViewRow, bool>? zeilenFilter = null)
    {
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var zeilen = g.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && (zeilenFilter is null || zeilenFilter(r)))
            .Select(r => spalten.Select(c => leerfelder?.Contains(c.HeaderText) == true ? "" : r.Cells[c.Index].Value switch { bool b => b ? "X" : "", var v => v?.ToString() ?? "" }).ToArray()).ToList();
        Drucken(owner, titel, spalten.Select(c => c.HeaderText).ToArray(), spalten.Select(c => c.Width).ToArray(), zeilen, vorschau, leerfelder);
    }


    public static void Drucken(IWin32Window owner, string titel, string[] kopf, int[] breiten, List<string[]> zeilen, bool vorschau, ISet<string>? leerfelder = null)
    {
        var layout = LayoutFuer(titel);
        // Spaltenliste mit der aktuellen Tabelle abgleichen
        if (layout.Spalten.Count == 0 || layout.Spalten.Select(x => x.Name).Except(kopf).Any() || kopf.Except(layout.Spalten.Select(x => x.Name)).Any())
            layout.Spalten = kopf.Select((k, i) => new DruckSpalte { Name = k, Breite = breiten[i] }).ToList();
        if (vorschau && !LayoutDialog(owner, layout)) return;
        AppZustand.Einstellungen.Speichern();
        // Spalten laut Layout (Auswahl, Reihenfolge, Breite)
        var auswahl = layout.Spalten.Where(x => x.Drucken).Select(x => (Index: Array.IndexOf(kopf, x.Name), x.Breite)).Where(x => x.Index >= 0).ToList();
        if (auswahl.Count > 0)
        {
            var quelle = zeilen;
            zeilen = quelle.Select(z => auswahl.Select(a => z.ElementAtOrDefault(a.Index) ?? "").ToArray()).ToList();
            breiten = auswahl.Select(a => Math.Max(20, a.Breite)).ToArray();
            kopf = auswahl.Select(a => kopf[a.Index]).ToArray();
        }
        var doc = new PrintDocument { DocumentName = titel };
        doc.DefaultPageSettings.Landscape = layout.Querformat;
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 50, 50);
        var zeile = 0;
        var seite = 0;
        var summen = layout.Summenzeile
            ? kopf.Select((_, i) => zeilen.Sum(z => decimal.TryParse(z.ElementAtOrDefault(i), out var v) ? v : 0)).ToArray()
            : Array.Empty<decimal>();
        doc.BeginPrint += (_, _) => { zeile = 0; seite = 0; };
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            var r = e.MarginBounds;
            using var f = new Font("Segoe UI", layout.Schriftgroesse);
            using var fb = new Font("Segoe UI", layout.Schriftgroesse, FontStyle.Bold);
            using var ft = new Font("Segoe UI", 12f, FontStyle.Bold);
            seite++;
            var y = (float)r.Top;
            g.DrawString(layout.Ueberschrift, ft, Brushes.Black, r.Left, y);
            g.DrawString($"{AppZustand.D.Einstellungen.Schulname}   {DateTime.Now:dd.MM.yyyy HH:mm}   Seite {seite}", f, Brushes.DimGray, r.Left, y + 22);
            y += 46;
            var gesamt = Math.Max(1, breiten.Sum());
            var faktor = Math.Min(1f, r.Width / (float)gesamt);
            var hz = f.GetHeight(g) + 4;
            // Leerfelder (Unterschrift) brauchen Platz zum Unterschreiben: höhere Zeilen
            var zh = leerfelder is { Count: > 0 } && kopf.Any(leerfelder.Contains) ? hz * 2 : hz;
            float x = r.Left;
            g.FillRectangle(Brushes.Gainsboro, r.Left, y, gesamt * faktor, hz);
            for (var i = 0; i < kopf.Length; i++)
            {
                g.DrawString(kopf[i], fb, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                x += breiten[i] * faktor;
            }
            y += hz;
            var fussHoehe = string.IsNullOrWhiteSpace(layout.Fusszeile) ? 0 : hz + 4;
            var nr = 0;
            while (zeile < zeilen.Count && y + zh < r.Bottom - fussHoehe)
            {
                if (layout.Zebra && nr++ % 2 == 1) g.FillRectangle(Brushes.WhiteSmoke, r.Left, y, gesamt * faktor, zh);
                x = r.Left;
                for (var i = 0; i < kopf.Length; i++)
                {
                    if (leerfelder?.Contains(kopf[i]) == true)
                        g.DrawRectangle(Pens.Black, x + 2, y + 2, breiten[i] * faktor - 4, zh - 4);
                    else
                        g.DrawString(zeilen[zeile].ElementAtOrDefault(i) ?? "", f, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    x += breiten[i] * faktor;
                }
                g.DrawLine(Pens.LightGray, r.Left, y + zh, r.Left + gesamt * faktor, y + zh);
                if (layout.Gitter)
                {
                    var gx = (float)r.Left;
                    for (var i = 0; i <= kopf.Length; i++) { g.DrawLine(Pens.LightGray, gx, y, gx, y + zh); if (i < kopf.Length) gx += breiten[i] * faktor; }
                }
                y += zh;
                zeile++;
            }
            if (fussHoehe > 0) g.DrawString(layout.Fusszeile, f, Brushes.DimGray, r.Left, r.Bottom - hz);
            if (zeile >= zeilen.Count && summen.Length > 0 && y + hz < r.Bottom)
            {
                x = r.Left;
                g.DrawLine(Pens.Black, r.Left, y, r.Left + gesamt * faktor, y);
                for (var i = 0; i < kopf.Length; i++)
                {
                    if (summen[i] != 0) g.DrawString(summen[i].ToString("0.##"), fb, Brushes.Black, x + 2, y + 2);
                    x += breiten[i] * faktor;
                }
            }
            e.HasMorePages = zeile < zeilen.Count;
        };
        Ausgeben(owner, doc, vorschau);
    }

    public static void Ausgeben(IWin32Window owner, PrintDocument doc, bool vorschau)
    {
        try
        {
            if (vorschau)
            {
                using var p = new PrintPreviewDialog { Document = doc, Width = 1000, Height = 760, UseAntiAlias = true, Text = "Seitenansicht – " + doc.DocumentName };
                p.ShowDialog(owner);
            }
            else
            {
                using var pd = new PrintDialog { Document = doc, UseEXDialog = true };
                if (pd.ShowDialog(owner) == DialogResult.OK) doc.Print();
            }
        }
        catch (InvalidPrinterException)
        {
            Ui.Info("Es ist kein Drucker installiert. Unter Windows steht z. B. \"Microsoft Print to PDF\" zur Verfügung.");
        }
    }

    /// <summary>Druckformular bearbeiten: Überschrift, Fußzeile, Format, Schrift, Spalten (Auswahl, Reihenfolge, Breite).</summary>
    private static bool LayoutDialog(IWin32Window owner, Layout l)
    {
        using var d = new Dialog("Druckformular", 620, 420);
        var y = 12;
        var t = Ui.Zeile(d, "Überschrift", new TextBox { Text = l.Ueberschrift }, ref y, 100, 220);
        var fz = Ui.Zeile(d, "Fußzeile", new TextBox { Text = l.Fusszeile }, ref y, 100, 220);
        var q = Ui.Zeile(d, "Querformat", new CheckBox { Checked = l.Querformat }, ref y, 100);
        var s = Ui.Zeile(d, "Schriftgröße", new NumericUpDown { Minimum = 6, Maximum = 16, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)l.Schriftgroesse }, ref y, 100, 70);
        var su = Ui.Zeile(d, "Summenzeile", new CheckBox { Checked = l.Summenzeile }, ref y, 100);
        var ze = Ui.Zeile(d, "Zeilen abwechselnd", new CheckBox { Checked = l.Zebra }, ref y, 100);
        var gi = Ui.Zeile(d, "Spaltenlinien", new CheckBox { Checked = l.Gitter }, ref y, 100);
        d.Controls.Add(new Label { Text = "Spalten (Haken = drucken, Breite änderbar, Reihenfolge mit ▲ ▼):", Left = 340, Top = 12, Width = 270 });
        var g = new DataGridView { Left = 340, Top = 34, Width = 230, Height = 330 };
        Ui.RasterGestalten(g, true);
        g.RowHeadersVisible = false;
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "D", HeaderText = "", Width = 26 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "N", HeaderText = "Spalte", Width = 130, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "B", HeaderText = "Breite", Width = 60 });
        foreach (var sp in l.Spalten) g.Rows.Add(sp.Drucken, sp.Name, sp.Breite);
        var hoch = new Button { Text = "▲", Left = 576, Top = 34, Width = 32, Height = 28 };
        var runter = new Button { Text = "▼", Left = 576, Top = 66, Width = 32, Height = 28 };
        void Verschieben(int r)
        {
            if (g.CurrentRow is not { } row) return;
            var i = row.Index;
            var j = i + r;
            if (j < 0 || j >= g.Rows.Count) return;
            g.Rows.RemoveAt(i);
            g.Rows.Insert(j, row);
            g.CurrentCell = g.Rows[j].Cells[1];
        }
        hoch.Click += (_, _) => Verschieben(-1);
        runter.Click += (_, _) => Verschieben(1);
        g.CellContentClick += (_, e) => { if (e.ColumnIndex == 0) g.EndEdit(); };
        d.Controls.AddRange(new Control[] { g, hoch, runter });
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        g.EndEdit();
        l.Ueberschrift = t.Text;
        l.Fusszeile = fz.Text;
        l.Querformat = q.Checked;
        l.Schriftgroesse = (float)s.Value;
        l.Summenzeile = su.Checked;
        l.Zebra = ze.Checked;
        l.Gitter = gi.Checked;
        l.Spalten = g.Rows.Cast<DataGridViewRow>().Select(r => new DruckSpalte
        {
            Drucken = r.Cells[0].Value is true, Name = r.Cells[1].Value?.ToString() ?? "", Breite = int.TryParse(r.Cells[2].Value?.ToString(), out var b) ? b : 60,
        }).ToList();
        return true;
    }
}
