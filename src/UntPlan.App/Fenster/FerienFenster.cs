using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>Ferien und Feiertage: Liste und Jahreskalender. Ferien durch Überstreichen im Kalender eingeben.</summary>
public sealed class FerienFenster : MdiFenster
{
    private readonly DataGridView _liste = new() { Dock = DockStyle.Fill };
    private readonly Kalenderansicht _kalender;

    public FerienFenster()
    {
        Text = "Ferien";
        HilfeThema = "Ferien";
        Icon = SymbolIcon("ferien");
        Size = new Size(1050, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("NRW-Ferien und Feiertage eintragen", "ferien", () => { Aendern("NRW-Ferien eintragen", AenderungsArt.Einstellungen, Nrw.FerienEintragen); Neuaufbau(); }, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_liste);
        _liste.Columns.Add("K", "Name");
        _liste.Columns.Add("L", "Langname");
        _liste.Columns.Add("V", "von");
        _liste.Columns.Add("B", "bis");
        _liste.Columns.Add(new DataGridViewCheckBoxColumn { Name = "F", HeaderText = "Feiertag", Width = 55 });
        _liste.Columns[1].Width = 150;
        _liste.CellEndEdit += CellEndEdit;
        _liste.CellContentClick += (_, e) => { if (e.ColumnIndex == 4) _liste.EndEdit(); };
        _kalender = new Kalenderansicht(this) { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_liste);
        split.Panel2.Controls.Add(_kalender);
        split.Panel2.Controls.Add(new Label { Text = "Ferien eingeben: im Kalender mit gedrückter Maustaste überstreichen. Ein freier Tag in der Woche ist für die Wertrechnung relevant.", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
        Controls.Add(split);
        Controls.Add(ts);
        Load += (_, _) => split.SplitterDistance = 420;
        Neuaufbau();
    }

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art is AenderungsArt.Einstellungen or AenderungsArt.Alles) Neuaufbau();
    }

    private void Neuaufbau()
    {
        _liste.Rows.Clear();
        foreach (var f in D.Ferien.OrderBy(f => f.Von))
        {
            var r = _liste.Rows.Add(f.Kurzname, f.Langname, Format.Datum(f.Von), Format.Datum(f.Bis), f.Feiertag);
            _liste.Rows[r].Tag = f;
        }
        _kalender.Invalidate();
    }

    private void CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _liste.Rows[e.RowIndex].Tag is not Ferien f) return;
        var row = _liste.Rows[e.RowIndex];
        var id = f.Id;
        try
        {
            var von = DateOnly.Parse(row.Cells[2].Value?.ToString() ?? "", Format.De);
            var bis = DateOnly.Parse(row.Cells[3].Value?.ToString() ?? "", Format.De);
            Aendern("Ferien geändert", AenderungsArt.Einstellungen, d =>
            {
                var x = d.Ferien.First(y => y.Id == id);
                x.Kurzname = row.Cells[0].Value?.ToString() ?? "";
                x.Langname = row.Cells[1].Value?.ToString() ?? "";
                x.Von = von;
                x.Bis = bis < von ? von : bis;
                x.Feiertag = row.Cells[4].Value is true;
            });
        }
        catch (FormatException) { Ui.Info("Bitte Datum als TT.MM.JJJJ eingeben."); }
        NachZellwechsel(Neuaufbau);
    }

    private void Loeschen()
    {
        var ids = _liste.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Ferien>().Select(f => f.Id).Distinct().ToList();
        if (ids.Count == 0) return;
        Aendern("Ferien löschen", AenderungsArt.Einstellungen, d => d.Ferien.RemoveAll(f => ids.Contains(f.Id)));
        Neuaufbau();
    }

    internal void Neu(DateOnly von, DateOnly bis)
    {
        var name = Ui.Eingabe($"Bezeichnung für {Format.Datum(von)} – {Format.Datum(bis)}:", "Ferien eingeben", von == bis ? "Beweglicher Ferientag" : "Ferien");
        if (string.IsNullOrWhiteSpace(name)) return;
        Aendern("Ferien eingeben", AenderungsArt.Einstellungen, d =>
            d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = name.Length > 6 ? name[..6] : name, Langname = name, Von = von, Bis = bis, Feiertag = von == bis && name.Contains("Feiertag") }));
        Neuaufbau();
    }

    private sealed class Kalenderansicht : Control
    {
        private readonly FerienFenster _f;
        private DateOnly? _start, _ende;
        private static readonly string[] Monate = { "Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez" };

        public Kalenderansicht(FerienFenster f)
        {
            _f = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private DateOnly Beginn => new(AppZustand.D.Einstellungen.SchuljahrVon.Year, AppZustand.D.Einstellungen.SchuljahrVon.Month, 1);

        private RectangleF Zelle(int monat, int tag) => new(60 + (tag - 1) * (Width - 64) / 31f, 22 + monat * (Height - 26) / 12f, (Width - 64) / 31f, (Height - 26) / 12f);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var d = AppZustand.D;
            g.Clear(Color.White);
            for (var t = 1; t <= 31; t++) TextRenderer.DrawText(g, t.ToString(), Ui.SchriftKlein, Rectangle.Round(new RectangleF(Zelle(0, t).X, 4, Zelle(0, t).Width, 16)), Color.DimGray, TextFormatFlags.HorizontalCenter);
            for (var m = 0; m < 12; m++)
            {
                var monat = Beginn.AddMonths(m);
                TextRenderer.DrawText(g, $"{Monate[monat.Month - 1]} {monat.Year % 100:00}", Ui.SchriftKlein, new Rectangle(2, (int)Zelle(m, 1).Y, 56, (int)Zelle(m, 1).Height), Color.Black, TextFormatFlags.VerticalCenter);
                for (var t = 1; t <= DateTime.DaysInMonth(monat.Year, monat.Month); t++)
                {
                    var datum = new DateOnly(monat.Year, monat.Month, t);
                    var r = Zelle(m, t);
                    var ferien = Kalender.FerienAm(d, datum);
                    var we = Kalender.TagIndex(d, datum) < 0;
                    var markiert = _start.HasValue && _ende.HasValue && datum >= Min(_start.Value, _ende.Value) && datum <= Max(_start.Value, _ende.Value);
                    var farbe = markiert ? Color.FromArgb(120, 180, 255) : ferien is { Feiertag: true } ? Color.FromArgb(255, 120, 120) : ferien != null ? Color.FromArgb(120, 220, 120) : we ? Color.FromArgb(225, 225, 225) : Color.White;
                    using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
                    g.DrawRectangle(Pens.Gainsboro, r.X, r.Y, r.Width, r.Height);
                    if (r.Width > 18) TextRenderer.DrawText(g, Core.Planung.Planungsdienst.TageKurz[((int)datum.DayOfWeek + 6) % 7][..1], Ui.SchriftKlein, Rectangle.Round(r), Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
        private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

        private DateOnly? Datum(Point p)
        {
            for (var m = 0; m < 12; m++)
            {
                var monat = Beginn.AddMonths(m);
                for (var t = 1; t <= DateTime.DaysInMonth(monat.Year, monat.Month); t++)
                    if (Zelle(m, t).Contains(p)) return new DateOnly(monat.Year, monat.Month, t);
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e) { _start = _ende = Datum(e.Location); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left && _start.HasValue && Datum(e.Location) is { } d) { _ende = d; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_start.HasValue && _ende.HasValue) _f.Neu(Min(_start.Value, _ende.Value), Max(_start.Value, _ende.Value));
            _start = _ende = null;
            Invalidate();
        }
    }
}
