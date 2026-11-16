using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Handbuch 7.4.1.1 Tageszeitraster: Karteikarte „Tage“ des Zeitrasters (nur bei „Tageszeitraster aktivieren“). Links der Tag, rechts
/// Beginn und Ende jeder Stunde dieses Tages. Tage ohne eigene Zeiten verwenden das allgemeine Raster.
/// </summary>
public sealed partial class ZeitrasterDialog
{
    private TabPage TageSeite()
    {
        var seite = new TabPage("Tage");
        var tage = new ListBox { Left = 10, Top = 10, Width = 140, Height = 300, IntegralHeight = false };
        var raster = new DataGridView { Left = 164, Top = 10, Width = 330, Height = 300 };
        var standard = new Button { Text = "Wie allgemeines Raster", Left = 510, Top = 10, Width = 170, Height = 26 };
        var kopieren = new Button { Text = "Auf alle Tage kopieren", Left = 510, Top = 42, Width = 170, Height = 26 };
        var hinweis = new Label { Left = 510, Top = 80, Width = 250, Height = 120, ForeColor = Color.DimGray };
        Ui.RasterGestalten(raster);
        raster.AllowUserToAddRows = false;
        raster.RowHeadersVisible = false;
        raster.Columns.Add(new DataGridViewTextBoxColumn { Name = "S", HeaderText = "Stunde", Width = 60, ReadOnly = true });
        raster.Columns.Add(new DataGridViewTextBoxColumn { Name = "B", HeaderText = "Beginn", Width = 80 });
        raster.Columns.Add(new DataGridViewTextBoxColumn { Name = "E", HeaderText = "Ende", Width = 80 });
        raster.Columns.Add(new DataGridViewTextBoxColumn { Name = "A", HeaderText = "allgemein", Width = 90, ReadOnly = true });
        var laedt = false;

        void Tagliste()
        {
            var alt = Math.Max(0, tage.SelectedIndex);
            tage.Items.Clear();
            for (var t = 0; t < _z.TageProWoche; t++)
                tage.Items.Add(Planungsdienst.TageLang[(_z.ErsterSchultag + t) % 7] + (_z.TagWeichtAb(t) ? "  *" : ""));
            if (tage.Items.Count > 0) tage.SelectedIndex = Math.Min(alt, tage.Items.Count - 1);
        }

        void Fuellen()
        {
            laedt = true;
            raster.Rows.Clear();
            var tag = tage.SelectedIndex;
            if (tag >= 0)
                foreach (var z in _z.ZeitenDesTages(tag))
                {
                    var a = _z.Zeit(z.Nr);
                    raster.Rows.Add(_z.StundenLabel(z.Nr), z.Beginn.ToString("HH:mm"), z.Ende.ToString("HH:mm"), a is null ? "" : $"{a.Beginn:HH:mm}-{a.Ende:HH:mm}");
                }
            laedt = false;
            hinweis.Text = tag >= 0 && _z.TagWeichtAb(tag)
                ? "Dieser Tag hat eigene Zeiten (*). Sichtbar im Stundenplan über die Beginn- und Endzeiten der Stundenplan-Stunde, in der Lupe und im Minut-Modus."
                : "Dieser Tag verwendet die Zeiten des allgemeinen Zeitrasters. Zeiten ändern = eigene Zeiten für diesen Tag.";
        }

        raster.CellEndEdit += (_, e) =>
        {
            if (laedt || tage.SelectedIndex < 0 || e.RowIndex < 0) return;
            var tag = tage.SelectedIndex;
            var liste = _z.ZeitenDesTages(tag);
            var z = liste[e.RowIndex];
            if (TimeOnly.TryParse(raster[e.ColumnIndex, e.RowIndex].Value?.ToString(), out var t))
            {
                if (e.ColumnIndex == 1) { var dauer = z.Ende - z.Beginn; z.Beginn = t; if (dauer > TimeSpan.Zero) z.Ende = t.Add(dauer); }
                else if (e.ColumnIndex == 2) z.Ende = t;
                _z.TagesZeiten[tag] = liste;
            }
            BeginInvoke(() => { Tagliste(); Fuellen(); });
        };
        tage.SelectedIndexChanged += (_, _) => Fuellen();
        standard.Click += (_, _) => { if (tage.SelectedIndex >= 0) { _z.TagesZeiten.Remove(tage.SelectedIndex); Tagliste(); Fuellen(); } };
        kopieren.Click += (_, _) =>
        {
            if (tage.SelectedIndex < 0) return;
            var quelle = _z.ZeitenDesTages(tage.SelectedIndex);
            for (var t = 0; t < _z.TageProWoche; t++)
                _z.TagesZeiten[t] = quelle.Select(z => new Stundenzeit { Nr = z.Nr, Bezeichnung = z.Bezeichnung, Beginn = z.Beginn, Ende = z.Ende }).ToList();
            Tagliste();
            Fuellen();
        };
        seite.Controls.AddRange(new Control[] { tage, raster, standard, kopieren, hinweis });
        seite.Enter += (_, _) => { Tagliste(); Fuellen(); };
        Tagliste();
        return seite;
    }
}
