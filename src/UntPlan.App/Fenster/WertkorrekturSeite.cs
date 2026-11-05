using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Karteikarte „WertKorrektur“ der Lehrer-Stammdaten wie im Handbuch: Datum, Art, Wert =, Text und rechts die Auswahl „Monat“.
/// Art V (Vertretungen) und Y (Sondereinsätze) erhöhen, E (Entfälle) vermindert den Vertretungszähler; „+“ bzw. „-“ verändern
/// statt des Vertretungszählers den Unterrichtswert.
/// </summary>
public sealed class WertkorrekturSeite : TabPage
{
    private static readonly string[] Arten = { "V", "Y", "E", "+", "-" };
    private readonly DataGridView _g = new() { Left = 6, Top = 6, Width = 330, Height = 120, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
    private readonly ComboBox _monat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 350, Top = 26, Width = 110 };
    private readonly Func<Lehrer?> _lehrer;
    private readonly Action<string, Action<Schuldatei>> _aendern;
    private bool _laden;

    public WertkorrekturSeite(Func<Lehrer?> lehrer, Action<string, Action<Schuldatei>> aendern) : base("WertKorrektur")
    {
        _lehrer = lehrer;
        _aendern = aendern;
        BackColor = Color.White;
        Ui.RasterGestalten(_g, true);
        _g.AllowUserToAddRows = true;
        _g.AllowUserToDeleteRows = true;
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Datum", HeaderText = "Datum", Width = 80 });
        var art = new DataGridViewComboBoxColumn { Name = "Art", HeaderText = "Art", Width = 50, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing, FlatStyle = FlatStyle.Flat };
        art.Items.AddRange(Arten);
        _g.Columns.Add(art);
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Wert", HeaderText = "Wert =", Width = 60, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _g.Columns["Art"]!.ToolTipText = "V = Vertretungen, Y = Sondereinsätze (erhöhen den Vertretungszähler), E = Entfälle (vermindert ihn); + / - = Unterrichtswert erhöhen / vermindern";
        _g.DataError += (_, e) => e.ThrowException = false;
        _g.CellEndEdit += (_, _) => Speichern();
        _g.UserDeletedRow += (_, _) => Speichern();
        _monat.Items.Add("Alle");
        Controls.Add(_g);
        Controls.Add(new Label { Text = "Monat", Left = 350, Top = 8, AutoSize = true });
        Controls.Add(_monat);
        _monat.SelectedIndexChanged += (_, _) => { if (!_laden) Fuellen(); };
        _monat.Format += MonatText;
    }

    private (int Jahr, int Monat)? Filter => _monat.SelectedItem is DateTime m ? (m.Year, m.Month) : null;

    /// <summary>Zeilen des gewählten Lehrers anzeigen (nach Wechsel der Zeile oder einer Änderung).</summary>
    public void Fuellen()
    {
        _laden = true;
        var d = AppZustand.D;
        var gewaehlt = _monat.SelectedItem;
        _monat.Items.Clear();
        _monat.Items.Add("Alle");
        for (var m = new DateTime(d.Einstellungen.SchuljahrVon.Year, d.Einstellungen.SchuljahrVon.Month, 1); m <= d.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue); m = m.AddMonths(1))
            _monat.Items.Add(m);

        _monat.SelectedItem = gewaehlt is DateTime g && _monat.Items.Contains(g) ? g : "Alle";
        if (_monat.SelectedIndex < 0) _monat.SelectedIndex = 0;
        _g.Rows.Clear();
        var l = _lehrer();
        _g.Enabled = l != null;
        if (l != null)
            foreach (var w in l.Wertkorrekturen.Where(Sichtbar).OrderBy(w => w.Datum))
                _g.Rows.Add(Format.Datum(w.Datum), Arten.Contains(w.Art) ? w.Art : "V", w.Wert.ToString("0.000", CultureInfo.InvariantCulture), w.Text);
        _laden = false;
    }

    private static void MonatText(object? sender, ListControlConvertEventArgs e)
    {
        if (e.ListItem is DateTime m) e.Value = m.ToString("MMMM yyyy", Format.De);
    }

    private bool Sichtbar(Wertkorrektur w) => Filter is not { } f || w.Datum.Year == f.Jahr && w.Datum.Month == f.Monat;

    private void Speichern()
    {
        if (_laden || _lehrer() is not { } l) return;
        var neu = new List<Wertkorrektur>();
        foreach (DataGridViewRow r in _g.Rows)
        {
            if (r.IsNewRow) continue;
            var datumText = r.Cells["Datum"].Value?.ToString()?.Trim() ?? "";
            var wertText = r.Cells["Wert"].Value?.ToString()?.Trim() ?? "";
            if (datumText.Length == 0 && wertText.Length == 0) continue;
            DateOnly datum;
            try { datum = Ui.DatumLesen(datumText.Length == 0 ? Format.Datum(AppZustand.Datum) : datumText, AppZustand.Datum); }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { Ui.Info($"Das Datum „{datumText}“ ist ungültig."); return; }
            if (!decimal.TryParse(wertText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var wert)) wert = 0;
            neu.Add(new Wertkorrektur { Datum = datum, Art = r.Cells["Art"].Value?.ToString() is { Length: > 0 } a ? a : "V", Wert = wert, Text = r.Cells["Text"].Value?.ToString() ?? "" });
        }
        var behalten = l.Wertkorrekturen.Where(w => !Sichtbar(w)).ToList();
        var id = l.Id;
        _aendern("Wertkorrektur", x =>
        {
            if (x.Lehrer.FirstOrDefault(y => y.Id == id) is { } ll) ll.Wertkorrekturen = behalten.Concat(neu).OrderBy(w => w.Datum).ToList();
        });
    }
}
