namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Tabellenspalte mit Datumsauswahl: Beim Bearbeiten erscheint ein Datumsfeld mit Kalender (Alt+↓ bzw. Klick öffnet ihn).
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
        ed.Leer = string.IsNullOrWhiteSpace(text);
        ed.Value = Lesen(text) ?? AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        ed.Leer = false;
    }

    internal static DateTime? Lesen(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return Ui.DatumLesen(text, AppZustand.Datum).ToDateTime(TimeOnly.MinValue); }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException) { return null; }
    }
}

/// <summary>Datumsfeld als Bearbeitungselement einer Tabellenzelle.</summary>
public sealed class DatumEditor : DateTimePicker, IDataGridViewEditingControl
{
    private bool _geaendert;
    internal bool Leer;

    public DatumEditor()
    {
        Format = DateTimePickerFormat.Custom;
        CustomFormat = "ddd dd.MM.yyyy";
    }

    public DataGridView? EditingControlDataGridView { get; set; }
    public object EditingControlFormattedValue
    {
        get => Value.ToString("dd.MM.yyyy");
        set { if (value is string s && DatumZelle.Lesen(s) is DateTime d) Value = d; }
    }
    public int EditingControlRowIndex { get; set; }
    public bool EditingControlValueChanged { get => _geaendert; set => _geaendert = value; }
    public Cursor EditingPanelCursor => Cursors.Default;
    public bool RepositionEditingControlOnValueChange => false;

    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => EditingControlFormattedValue;

    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
    {
        Font = style.Font;
        CalendarForeColor = style.ForeColor;
        CalendarMonthBackground = Color.White;
    }

    public bool EditingControlWantsInputKey(Keys key, bool dataGridViewWantsInputKey) => (key & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageDown or Keys.PageUp => true,
        _ => !dataGridViewWantsInputKey,
    };

    public void PrepareEditingControlForEdit(bool selectAll) { }

    protected override void OnValueChanged(EventArgs eventargs)
    {
        if (!Leer)
        {
            _geaendert = true;
            EditingControlDataGridView?.NotifyCurrentCellDirty(true);
        }
        base.OnValueChanged(eventargs);
    }

    protected override void OnCloseUp(EventArgs eventargs)
    {
        base.OnCloseUp(eventargs);
        // Auswahl im Kalender sofort übernehmen
        if (EditingControlDataGridView is { } g && _geaendert) g.EndEdit();
    }
}
