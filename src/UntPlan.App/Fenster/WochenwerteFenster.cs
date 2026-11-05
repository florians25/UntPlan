using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Wochenwerte wie im Handbuch (Modul Wertrechnung): je Kalenderwoche geplant, verplant, gehalten, Entfälle, Vertretungen,
/// Sondereinsätze und Gesamt eines Lehrers; Einstellung „Vertretungsplan / Stunden“ bzw. „Vertretungsplan / Werte“ (Werte inkl. Wertkorrekturen).
/// </summary>
public sealed class WochenwerteFenster : MdiFenster, IDruckbar
{
    private readonly ComboBox _art = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 30, Top = 8, Width = 150, Enabled = false };
    private readonly ComboBox _modus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 200, Top = 8, Width = 190 };
    private readonly ComboBox _lehrer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 30, Top = 38, Width = 150 };
    private readonly CheckBox _komprimiert = new() { Text = "komprimiert", Left = 200, Top = 40, AutoSize = true };
    private readonly CheckBox _hhmm = new() { Text = "HH:MM", Left = 560, Top = 40, AutoSize = true };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public WochenwerteFenster()
    {
        Text = "Wochenwerte";
        HilfeThema = "Unterrichtsplanung";
        Icon = SymbolIcon("wertrechnung");
        Size = new Size(720, 460);
        Positionieren();
        var kopf = new Panel { Dock = DockStyle.Top, Height = 100 };
        _art.Items.Add("Lehrer");
        _art.SelectedIndex = 0;
        _modus.Items.AddRange(new object[] { "Vertretungsplan / Stunden", "Vertretungsplan / Werte" });
        _modus.SelectedIndex = 0;
        foreach (var l in Stammdaten.Sortiert(D.Lehrer)) _lehrer.Items.Add(l);
        _lehrer.Format += (_, e) => e.Value = e.ListItem is Lehrer l ? l.Kurzname : e.ListItem?.ToString();
        if (_lehrer.Items.Count > 0) _lehrer.SelectedIndex = 0;
        var aktualisieren = new Button { Text = "Aktualisieren", Left = 30, Top = 66, Width = 150, Height = 26 };
        aktualisieren.Click += (_, _) => Neuaufbau();
        kopf.Controls.AddRange(new Control[] { _art, _modus, _lehrer, _komprimiert, _hhmm, aktualisieren,
            new Label { Text = "Gehaltener Unterricht incl Vertretungen und zu zählenden Veranstaltungen", Left = 200, Top = 71, AutoSize = true } });
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.RowHeadersVisible = false;
        foreach (var (n, k, b) in new[] { ("W", "Woche", 55), ("VB", "Von - Bis", 90), ("G", "geplant", 60), ("Vp", "Verplant", 60), ("Ge", "Gehalten", 60), ("E", "Entf.", 50), ("V", "Vertr.", 50), ("S", "Sondereins.", 75), ("Su", "Gesamt", 60) })
        {
            var c = _g.Columns[_g.Columns.Add(n, k)];
            c.Width = b;
            if (n != "VB") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        _g.Columns["S"]!.HeaderCell.Style.BackColor = Color.FromArgb(255, 220, 170);
        _lehrer.SelectedIndexChanged += (_, _) => Neuaufbau();
        _modus.SelectedIndexChanged += (_, _) => Neuaufbau();
        _komprimiert.CheckedChanged += (_, _) => Neuaufbau();
        _hhmm.CheckedChanged += (_, _) => Neuaufbau();
        Controls.Add(_g);
        Controls.Add(kopf);
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private string Z(decimal w)
    {
        if (_hhmm.Checked)
        {
            var min = (int)Math.Round(Math.Abs(w) * D.Zeitraster.NormalstundeMinuten);
            return $"{(w < 0 ? "-" : "")}{min / 60}:{min % 60:00}";
        }
        return w == 0 ? "" : w.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        if (_lehrer.SelectedItem is not Lehrer l) return;
        var werte = _modus.SelectedIndex == 1;
        var heute = Kalender.Montag(AppZustand.Datum);
        foreach (var w in Statistik.Wochenwerte(D, l))
        {
            var sonder = D.Vertretungen.Count(v => v.Datum >= w.Montag && v.Datum <= w.Montag.AddDays(6) && v.VertreterId == l.Id && v.Art is VertretungsArt.Sondereinsatz or VertretungsArt.SondereinsatzVormerkung);
            var gehalten = w.Unterricht - w.Entfaelle + w.Vertretungen + (werte ? w.Korrektur : 0);
            if (_komprimiert.Checked && w.Entfaelle == 0 && w.Vertretungen == 0 && sonder == 0 && w.Korrektur == 0) continue;
            var nr = System.Globalization.ISOWeek.GetWeekOfYear(w.Montag.ToDateTime(TimeOnly.MinValue));
            var r = _g.Rows.Add(nr, $"{w.Montag.Day}.{w.Montag.Month}.-{w.Montag.AddDays(6).Day}.{w.Montag.AddDays(6).Month}.", Z(w.Soll), Z(w.Unterricht), Z(gehalten), Z(w.Entfaelle), Z(w.Vertretungen), Z(sonder), Z(gehalten + sonder - w.Soll));
            if (w.Montag == heute) _g.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 200);
        }
    }

    public void Drucken(bool vorschau)
    {
        ListenDruck.DruckenTabelle(this, $"Wochenwerte – {(_lehrer.SelectedItem as Lehrer)?.Kurzname}", _g, vorschau);
    }
}
