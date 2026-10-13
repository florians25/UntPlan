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
    }

    private static readonly Layout Letztes = new();

    public static void Drucken(IWin32Window owner, string titel, string[] kopf, int[] breiten, List<string[]> zeilen, bool vorschau)
    {
        Letztes.Ueberschrift = titel;
        if (vorschau && !LayoutDialog(owner, Letztes)) return;
        var doc = new PrintDocument { DocumentName = titel };
        doc.DefaultPageSettings.Landscape = Letztes.Querformat;
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 50, 50);
        var zeile = 0;
        var seite = 0;
        var summen = Letztes.Summenzeile
            ? kopf.Select((_, i) => zeilen.Sum(z => decimal.TryParse(z.ElementAtOrDefault(i), out var v) ? v : 0)).ToArray()
            : Array.Empty<decimal>();
        doc.BeginPrint += (_, _) => { zeile = 0; seite = 0; };
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            var r = e.MarginBounds;
            using var f = new Font("Segoe UI", Letztes.Schriftgroesse);
            using var fb = new Font("Segoe UI", Letztes.Schriftgroesse, FontStyle.Bold);
            using var ft = new Font("Segoe UI", 12f, FontStyle.Bold);
            seite++;
            var y = (float)r.Top;
            g.DrawString(Letztes.Ueberschrift, ft, Brushes.Black, r.Left, y);
            g.DrawString($"{AppZustand.D.Einstellungen.Schulname}   {DateTime.Now:dd.MM.yyyy HH:mm}   Seite {seite}", f, Brushes.DimGray, r.Left, y + 22);
            y += 46;
            var gesamt = Math.Max(1, breiten.Sum());
            var faktor = Math.Min(1f, r.Width / (float)gesamt);
            var hz = f.GetHeight(g) + 4;
            float x = r.Left;
            g.FillRectangle(Brushes.Gainsboro, r.Left, y, gesamt * faktor, hz);
            for (var i = 0; i < kopf.Length; i++)
            {
                g.DrawString(kopf[i], fb, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                x += breiten[i] * faktor;
            }
            y += hz;
            while (zeile < zeilen.Count && y + hz < r.Bottom)
            {
                x = r.Left;
                for (var i = 0; i < kopf.Length; i++)
                {
                    g.DrawString(zeilen[zeile].ElementAtOrDefault(i) ?? "", f, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    x += breiten[i] * faktor;
                }
                g.DrawLine(Pens.LightGray, r.Left, y + hz, r.Left + gesamt * faktor, y + hz);
                y += hz;
                zeile++;
            }
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

    private static bool LayoutDialog(IWin32Window owner, Layout l)
    {
        using var d = new Dialog("Seitenlayout", 360, 200);
        var y = 12;
        var t = Ui.Zeile(d, "Überschrift", new TextBox { Text = l.Ueberschrift }, ref y, 110, 220);
        var q = Ui.Zeile(d, "Querformat", new CheckBox { Checked = l.Querformat }, ref y, 110);
        var s = Ui.Zeile(d, "Schriftgröße", new NumericUpDown { Minimum = 6, Maximum = 16, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)l.Schriftgroesse }, ref y, 110, 70);
        var su = Ui.Zeile(d, "Summenzeile", new CheckBox { Checked = l.Summenzeile }, ref y, 110);
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        l.Ueberschrift = t.Text;
        l.Querformat = q.Checked;
        l.Schriftgroesse = (float)s.Value;
        l.Summenzeile = su.Checked;
        return true;
    }
}
