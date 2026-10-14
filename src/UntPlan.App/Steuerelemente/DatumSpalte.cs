namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Tabellenspalte mit Datumsauswahl: Beim Bearbeiten erscheint ein Textfeld mit schmalem Kalenderknopf (Alt+↓ öffnet den Kalender).
/// Der Wert bleibt Text im Format TT.MM.JJJJ, sodass vorhandene Auswertungen (Ui.DatumLesen) unverändert funktionieren.
/// Eintippen ist weiterhin möglich.
/// </summary>
public sealed class DatumSpalte : DataGridViewColumn
{
    public DatumSpalte() : base(new DatumZelle()) { }

    public static DatumSpalte Neu(string name, string kopf, int breite) =>
        new() { Name = name, HeaderText = kopf, Width = breite, SortMode = DataGridViewColumnSortMode.NotSortable };

    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DatumZelle) throw new InvalidCastException("DatumZelle erwartet");
            base.CellTemplate = value;
        }
    }
}

public sealed class DatumZelle : DataGridViewTextBoxCell
{
    public override Type EditType => typeof(DatumEditor);
    public override Type ValueType => typeof(string);
    public override object DefaultNewRowValue => "";

    public override void InitializeEditingControl(int rowIndex, object? initialFormattedValue, DataGridViewCellStyle dataGridViewCellStyle)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, dataGridViewCellStyle);
        if (DataGridView?.EditingControl is not DatumEditor ed) return;
        var text = initialFormattedValue?.ToString() ?? "";
        ed.Leer = true;
        ed.Value = Lesen(text) ?? AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        ed.Leer = false;
        ed.EditingControlValueChanged = string.IsNullOrWhiteSpace(text);
    }

    internal static DateTime? Lesen(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return Ui.DatumLesen(text, AppZustand.Datum).ToDateTime(TimeOnly.MinValue); }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { return null; }
    }
}

/// <summary>
/// Datumsfeld als Bearbeitungselement einer Tabellenzelle: Textfeld zum Eintippen mit schmalem Kalenderknopf rechts.
/// Klick auf den Knopf bzw. Alt+↓ / F4 öffnet einen Kalender; die Auswahl wird sofort übernommen.
/// </summary>
public sealed class DatumEditor : TextBox, IDataGridViewEditingControl
{
    private bool _geaendert;
    internal bool Leer;
    private readonly Button _knopf;
    private ToolStripDropDown? _popup;

    public DatumEditor()
    {
        BorderStyle = BorderStyle.None;
        _knopf = new Button
        {
            Dock = DockStyle.Right, Width = 16, FlatStyle = FlatStyle.Flat, TabStop = false, Cursor = Cursors.Default,
            BackColor = Color.FromArgb(236, 240, 243), Text = "", Margin = Padding.Empty, Padding = Padding.Empty,
        };
        _knopf.FlatAppearance.BorderSize = 0;
        _knopf.Paint += (_, e) =>
        {
            // kleines Dreieck statt breitem Systemknopf
            var r = _knopf.ClientRectangle;
            var m = new Point(r.Width / 2, r.Height / 2 + 1);
            using var b = new SolidBrush(Color.FromArgb(70, 82, 92));
            e.Graphics.FillPolygon(b, new[] { new Point(m.X - 4, m.Y - 2), new Point(m.X + 4, m.Y - 2), new Point(m.X, m.Y + 2) });
        };
        _knopf.Click += (_, _) => KalenderOeffnen();
        Controls.Add(_knopf);
    }

    /// <summary>Datum als Wert (für die Zelle und den Kalender).</summary>
    public DateTime Value
    {
        get => DatumZelle.Lesen(Text) ?? AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        set => Text = value.ToString("dd.MM.yyyy");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Text nicht unter den Knopf laufen lassen (EM_SETMARGINS, rechter Rand)
        if (OperatingSystem.IsWindows()) SendMessage(Handle, 0xD3, (IntPtr)2, (IntPtr)((_knopf.Width + 2) << 16));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    private void KalenderOeffnen()
    {
        _popup?.Close();
        var kal = new MonthCalendar { MaxSelectionCount = 1, ShowToday = true, ShowWeekNumbers = true, FirstDayOfWeek = Day.Monday };
        kal.SetDate(Value);
        var host = new ToolStripControlHost(kal) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = kal.Size };
        _popup = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true };
        _popup.Items.Add(host);
        kal.DateSelected += (_, ev) =>
        {
            Text = ev.Start.ToString("dd.MM.yyyy");
            _geaendert = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
            _popup?.Close();
            EditingControlDataGridView?.EndEdit();
        };
        _popup.Show(this, new Point(0, Height));
        kal.Focus();
    }

    public DataGridView? EditingControlDataGridView { get; set; }
    public object EditingControlFormattedValue
    {
        get => DatumZelle.Lesen(Text) is DateTime d ? d.ToString("dd.MM.yyyy") : Text.Trim();
        set { if (value is string s) Text = DatumZelle.Lesen(s) is DateTime d ? d.ToString("dd.MM.yyyy") : s; }
    }
    public int EditingControlRowIndex { get; set; }
    public bool EditingControlValueChanged { get => _geaendert; set => _geaendert = value; }
    public Cursor EditingPanelCursor => Cursors.IBeam;
    public bool RepositionEditingControlOnValueChange => false;

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => EditingControlFormattedValue;

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
    {
        Font = style.Font;
        ForeColor = style.ForeColor;
        BackColor = Color.White;
    }

    public bool EditingControlWantsInputKey(Keys key, bool dataGridViewWantsInputKey) => (key & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Home or Keys.End => true,
        Keys.Down when (key & Keys.Alt) != 0 => true,
        Keys.F4 => true,
        _ => !dataGridViewWantsInputKey,
    };

    public void PrepareEditingControlForEdit(bool selectAll) { if (selectAll) SelectAll(); else SelectionStart = Text.Length; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.Down && e.Alt) || e.KeyCode == Keys.F4) { KalenderOeffnen(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!Leer)
        {
            _geaendert = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
        }
        base.OnTextChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _popup?.Dispose();
        base.Dispose(disposing);
    }
}
