using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Ferien und Feiertage nach Vorbild: oben die Tabelle (Name, Langname, Von, Bis, Woche danach, Wochennummerierung),
/// darunter Werkzeugknöpfe, Datum/Kalenderwoche des gewählten Tages mit „Unterrichtsfrei“/„Feiertag“ und Legende,
/// der Jahreskalender (Monate August–Juli als Zeilen, Mo–So über sechs Wochen als Spalten: Ferien orange, Feiertage rot,
/// Schultage grün, Wochenenden fett umrandet), der Reiter „Allgemeine Daten“ und Ok/Abbrechen/Übernahme.
/// Änderungen werden in einem Entwurf gesammelt und erst mit „Übernahme“ bzw. „Ok“ in die Schuldatei geschrieben.
/// Ferien eingeben: im Kalender mit gedrückter Maustaste überstreichen.
/// </summary>
public sealed class FerienFenster : MdiFenster
{
    /// <summary>Farben der Legende (wie im Vorbild).</summary>
    public static readonly Color FerienOrange = Color.FromArgb(255, 153, 51);
    public static readonly Color FeiertagRot = Color.FromArgb(240, 40, 40);
    public static readonly Color SchultagGruen = Color.FromArgb(102, 240, 102);

    private readonly DataGridView _liste = new() { Dock = DockStyle.Fill, Name = "Ferien" };
    private readonly Kalenderansicht _kalender;
    private readonly Label _datumText = new() { AutoSize = true, Location = new Point(70, 6) };
    private readonly Label _kwText = new() { AutoSize = true, Location = new Point(70, 24) };
    private readonly CheckBox _unterrichtsfrei = new() { Text = "Unterrichtsfrei", AutoSize = true, Location = new Point(250, 4) };
    private readonly CheckBox _feiertag = new() { Text = "Feiertag", AutoSize = true, Location = new Point(250, 22) };
    private readonly Label[] _daten = Enumerable.Range(0, 8).Select(_ => new Label { AutoSize = true }).ToArray();
    private readonly Button _uebernahme = new() { Text = "Übernahme", Width = 80, Height = 24 };
    private List<Ferien> _entwurf = new();
    private bool _geaendert;
    private bool _laedt;
    private DateOnly _gewaehlt;

    public FerienFenster()
    {
        Text = "Ferien";
        HilfeThema = "Ferien";
        Icon = SymbolIcon("ferien");
        Size = new Size(1050, 640);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Ferien", "neu", () => NeuDialog(_gewaehlt, _gewaehlt)));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("NRW-Ferien und Feiertage eintragen", "ferien", NrwEintragen, false));

        // Tabelle wie im Vorbild
        Ui.RasterGestalten(_liste);
        _liste.AllowUserToAddRows = false;
        _liste.Columns[_liste.Columns.Add("K", "Name")].Width = 60;
        _liste.Columns[_liste.Columns.Add("L", "Langname")].Width = 130;
        _liste.Columns.Add(Steuerelemente.DatumSpalte.Neu("V", "Von", 80));
        _liste.Columns.Add(Steuerelemente.DatumSpalte.Neu("B", "Bis", 80));
        _liste.Columns[_liste.Columns.Add("W", "Woche danach (A,B..)")].Width = 135;
        _liste.Columns.Add(new DataGridViewCheckBoxColumn { Name = "N", HeaderText = "Wochennummerierung", Width = 130 });
        _liste.Columns.Add(new DataGridViewCheckBoxColumn { Name = "F", HeaderText = "Feiertag", Width = 55 });
        _liste.CellEndEdit += CellEndEdit;
        _liste.CellContentClick += (_, e) => { if (e.ColumnIndex >= 0 && _liste.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _liste.EndEdit(); };
        _liste.CellClick += (_, e) => { if (e.RowIndex >= 0 && _liste.Rows[e.RowIndex].Tag is Ferien f) DatumWaehlen(f.Von, false); };

        // Zeile mit Werkzeugknöpfen, Datum/Kalenderwoche, Häkchen und Legende
        var info = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.FromArgb(240, 244, 248) };
        var bearbeiten = new Button { Image = Symbole.Get("einstellungen", 16), Location = new Point(6, 10), Size = new Size(26, 26) };
        var neu = new Button { Image = Symbole.Get("neu", 16), Location = new Point(36, 10), Size = new Size(26, 26) };
        new ToolTip().SetToolTip(bearbeiten, "Ferien des gewählten Tages bearbeiten (Name, Langname)");
        new ToolTip().SetToolTip(neu, "Neue Ferien am gewählten Tag");
        bearbeiten.Click += (_, _) => FerienBearbeiten();
        neu.Click += (_, _) => NeuDialog(_gewaehlt, _gewaehlt);
        info.Controls.AddRange(new Control[] { bearbeiten, neu, _datumText, _kwText, _unterrichtsfrei, _feiertag });
        _unterrichtsfrei.CheckedChanged += (_, _) => TagUmschalten();
        _feiertag.CheckedChanged += (_, _) => TagUmschalten();
        var legende = new Legende { Location = new Point(400, 2), Size = new Size(460, 42) };
        info.Controls.Add(legende);

        _kalender = new Kalenderansicht(this) { Dock = DockStyle.Fill };

        // Reiter „Allgemeine Daten“
        var reiter = new TabControl { Dock = DockStyle.Bottom, Height = 78 };
        var allgemein = new TabPage("Allgemeine Daten") { BackColor = Color.White };
        void Platz(Control c, int x, int y) { c.Location = new Point(x, y); allgemein.Controls.Add(c); }
        Platz(new Label { Text = "Schuljahr", AutoSize = true }, 8, 8);
        Platz(new Label { Text = "Unterricht", AutoSize = true }, 8, 28);
        Platz(_daten[0], 110, 8); Platz(_daten[1], 270, 8); Platz(_daten[2], 420, 8); Platz(_daten[3], 600, 8);
        Platz(_daten[4], 110, 28); Platz(_daten[5], 270, 28); Platz(_daten[6], 420, 28);
        reiter.TabPages.Add(allgemein);

        var fuss = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        var ok = new Button { Text = "Ok", Width = 80, Height = 24, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var abbrechen = new Button { Text = "Abbrechen", Width = 80, Height = 24, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _uebernahme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        fuss.Controls.AddRange(new Control[] { ok, abbrechen, _uebernahme });
        fuss.Resize += (_, _) =>
        {
            _uebernahme.Location = new Point(fuss.ClientSize.Width - 88, 5);
            abbrechen.Location = new Point(fuss.ClientSize.Width - 172, 5);
            ok.Location = new Point(fuss.ClientSize.Width - 256, 5);
        };
        ok.Click += (_, _) => { if (Uebernehmen()) Close(); };
        abbrechen.Click += (_, _) => { _geaendert = false; Close(); };
        _uebernahme.Click += (_, _) => Uebernehmen();

        var oben = new Panel { Dock = DockStyle.Top, Height = 170 };
        oben.Controls.Add(_liste);
        Controls.Add(_kalender);
        Controls.Add(info);
        Controls.Add(oben);
        Controls.Add(reiter);
        Controls.Add(fuss);
        Controls.Add(ts);
        _gewaehlt = AppZustand.Datum;
        EntwurfLaden();
    }

    /// <summary>Gewählter Tag (für den Selbsttest).</summary>
    internal DateOnly Gewaehlt => _gewaehlt;
    /// <summary>Entwurf der Ferienliste (für den Selbsttest).</summary>
    internal IReadOnlyList<Ferien> Entwurf => _entwurf;

    protected override void Aktualisieren(AenderungsArt art)
    {
        // Änderungen aus anderen Fenstern: nur übernehmen, solange kein eigener Entwurf offen ist
        if (art is AenderungsArt.Einstellungen or AenderungsArt.Alles && !_geaendert) EntwurfLaden();
    }

    private static Ferien Kopie(Ferien f) => new()
    {
        Id = f.Id, Kurzname = f.Kurzname, Langname = f.Langname, Von = f.Von, Bis = f.Bis, Feiertag = f.Feiertag, WocheDanach = f.WocheDanach,
        Wochennummerierung = f.Wochennummerierung,
    };

    private void EntwurfLaden()
    {
        _entwurf = D.Ferien.Select(Kopie).ToList();
        _geaendert = false;
        Neuaufbau();
    }

    /// <summary>Schuldatei mit den Ferien des Entwurfs (für Kalenderfunktionen und Kennzahlen).</summary>
    private Schuldatei EntwurfsDatei() => new() { Einstellungen = D.Einstellungen, Zeitraster = D.Zeitraster, Ferien = _entwurf, Kalender = D.Kalender };

    private void Geaendert()
    {
        _geaendert = true;
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        _laedt = true;
        _entwurf.Sort((a, b) => a.Von.CompareTo(b.Von));
        var pos = _liste.CurrentCell is { } c ? (c.RowIndex, c.ColumnIndex) : (-1, 0);
        _liste.Rows.Clear();
        foreach (var f in _entwurf)
        {
            var r = _liste.Rows.Add(f.Kurzname, f.Langname, Format.Datum(f.Von), Format.Datum(f.Bis), f.WocheDanach, f.Wochennummerierung, f.Feiertag);
            _liste.Rows[r].Tag = f;
        }
        if (pos.Item1 >= 0 && pos.Item1 < _liste.Rows.Count) _liste.CurrentCell = _liste.Rows[pos.Item1].Cells[pos.Item2];
        var j = Kalender.JahresdatenBerechnen(EntwurfsDatei());
        _daten[0].Text = $"Anzahl Tage: {j.SchuljahrTage}";
        _daten[1].Text = $"Anzahl Wochen: {j.SchuljahrWochen}";
        _daten[2].Text = $"Anzahl Ferien: {j.AnzahlFerien}";
        _daten[3].Text = $"Anzahl Feiertage: {j.AnzahlFeiertage}";
        _daten[4].Text = $"Anzahl Tage: {j.UnterrichtTage}";
        _daten[5].Text = $"Anzahl Wochen: {j.UnterrichtWochen}";
        _daten[6].Text = $"Anzahl Ferientage: {j.Ferientage}";
        _uebernahme.Enabled = _geaendert;
        _laedt = false;
        DatumWaehlen(_gewaehlt, false);
        _kalender.Invalidate();
    }

    /// <summary>Tag im Kalender wählen: Datum, Kalenderwoche und Häkchen „Unterrichtsfrei“/„Feiertag“ anzeigen.</summary>
    internal void DatumWaehlen(DateOnly datum, bool zeile = true)
    {
        _gewaehlt = datum;
        _laedt = true;
        var tag = Core.Planung.Planungsdienst.TageKurz[((int)datum.DayOfWeek + 6) % 7];
        _datumText.Text = $"Datum: {tag} {datum.Day}.{datum.Month}.{datum.Year}";
        _kwText.Text = $"Kalenderwoche: {System.Globalization.ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue))}";
        var f = FerienAm(datum);
        _unterrichtsfrei.Checked = f != null;
        _feiertag.Checked = f?.Feiertag == true;
        _laedt = false;
        if (zeile && f != null)
            foreach (DataGridViewRow r in _liste.Rows)
                if (r.Tag == f) { _liste.CurrentCell = r.Cells[0]; break; }
        _kalender.Invalidate();
    }

    private Ferien? FerienAm(DateOnly datum) => _entwurf.Where(f => datum >= f.Von && datum <= f.Bis).OrderBy(f => f.Bis.DayNumber - f.Von.DayNumber).FirstOrDefault();

    /// <summary>
    /// Häkchen für den gewählten Tag: „Unterrichtsfrei“ legt einen eintägigen Eintrag an (Name „3.9“, Langname „3.9.2021“ wie im Vorbild)
    /// bzw. entfernt ihn, „Feiertag“ kennzeichnet den Eintrag als Feiertag.
    /// </summary>
    private void TagUmschalten()
    {
        if (_laedt) return;
        var d = _gewaehlt;
        var f = FerienAm(d);
        if (_unterrichtsfrei.Checked || _feiertag.Checked)
        {
            if (f is null)
            {
                f = new Ferien { Id = 0, Kurzname = $"{d.Day}.{d.Month}", Langname = $"{d.Day}.{d.Month}.{d.Year}", Von = d, Bis = d };
                _entwurf.Add(f);
            }
            f.Feiertag = _feiertag.Checked;
        }
        else if (f != null)
        {
            if (f.Von == f.Bis) _entwurf.Remove(f);
            else if (f.Von == d) f.Von = d.AddDays(1);
            else if (f.Bis == d) f.Bis = d.AddDays(-1);
            else
            {
                // Tag aus mehrtägigen Ferien herausnehmen: Ferien werden geteilt
                var rest = Kopie(f);
                rest.Id = 0;
                rest.Von = d.AddDays(1);
                f.Bis = d.AddDays(-1);
                _entwurf.Add(rest);
            }
        }
        Geaendert();
    }

    private void CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laedt || e.RowIndex < 0 || _liste.Rows[e.RowIndex].Tag is not Ferien f) return;
        var row = _liste.Rows[e.RowIndex];
        try
        {
            var von = DateOnly.Parse(row.Cells["V"].Value?.ToString() ?? "", Format.De);
            var bis = DateOnly.Parse(row.Cells["B"].Value?.ToString() ?? "", Format.De);
            f.Kurzname = row.Cells["K"].Value?.ToString()?.Trim() ?? "";
            f.Langname = row.Cells["L"].Value?.ToString()?.Trim() ?? "";
            f.Von = von;
            f.Bis = bis < von ? von : bis;
            var w = (row.Cells["W"].Value?.ToString() ?? "").Trim().ToUpperInvariant();
            f.WocheDanach = w.Length > 0 && w[0] is >= 'A' and <= 'P' ? w[..1] : "";
            f.Wochennummerierung = row.Cells["N"].Value is true;
            f.Feiertag = row.Cells["F"].Value is true;
        }
        catch (FormatException) { Ui.Info("Bitte Datum als TT.MM.JJJJ eingeben."); }
        NachZellwechsel(Geaendert);
    }

    private void Loeschen()
    {
        var weg = _liste.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<Ferien>().Distinct().ToList();
        if (weg.Count == 0 && FerienAm(_gewaehlt) is { } f) weg.Add(f);
        if (weg.Count == 0) return;
        _entwurf.RemoveAll(weg.Contains);
        Geaendert();
    }

    private void NrwEintragen()
    {
        var d = EntwurfsDatei();
        var vorher = _entwurf.ToHashSet();
        Nrw.FerienEintragen(d);
        // Nummern vergibt erst die Übernahme (die Entwurfsdatei kennt die übrigen Nummern der Schuldatei nicht)
        foreach (var f in d.Ferien.Where(f => !vorher.Contains(f))) f.Id = 0;
        _entwurf = d.Ferien;
        Geaendert();
    }

    /// <summary>Ferien des gewählten Tages im Dialog „Kurzname“ umbenennen.</summary>
    private void FerienBearbeiten()
    {
        if (FerienAm(_gewaehlt) is not { } f) { NeuDialog(_gewaehlt, _gewaehlt); return; }
        var e = KurznameDialog.Zeigen(this, f.Kurzname, f.Langname);
        if (e is not { } x) return;
        f.Kurzname = x.Kurz;
        f.Langname = x.Lang;
        Geaendert();
    }

    /// <summary>Neue Ferien für einen Zeitraum: Name und Langname im Dialog „Kurzname“ (Vorschlag wie im Vorbild: Datum).</summary>
    internal void NeuDialog(DateOnly von, DateOnly bis)
    {
        var kurz = von == bis ? $"{von.Day}.{von.Month}" : "Ferien";
        var lang = von == bis ? $"{von.Day}.{von.Month}.{von.Year}" : $"Ferien {Format.Datum(von)} – {Format.Datum(bis)}";
        var e = KurznameDialog.Zeigen(this, kurz, lang);
        if (e is not { } x || x.Kurz.Length == 0) return;
        Neu(von, bis, x.Kurz, x.Lang);
    }

    /// <summary>Neuen Ferieneintrag in den Entwurf aufnehmen.</summary>
    internal void Neu(DateOnly von, DateOnly bis, string kurz, string lang, bool feiertag = false)
    {
        _entwurf.Add(new Ferien { Id = 0, Kurzname = kurz, Langname = lang, Von = von, Bis = bis, Feiertag = feiertag || (von == bis && lang.Contains("Feiertag")) });
        _gewaehlt = von;
        Geaendert();
    }

    /// <summary>Entwurf in die Schuldatei übernehmen („Übernahme“, „Ok“).</summary>
    internal bool Uebernehmen()
    {
        if (_liste.IsCurrentCellInEditMode) _liste.EndEdit();
        if (!_geaendert) return true;
        var liste = _entwurf.Select(Kopie).ToList();
        var ok = Aendern("Ferien geändert", AenderungsArt.Einstellungen, d =>
        {
            foreach (var f in liste.Where(f => f.Id == 0)) f.Id = d.NeueId();
            d.Ferien = liste.OrderBy(f => f.Von).ToList();
            Kalender.CacheLeeren();
        });
        if (ok) EntwurfLaden();
        return ok;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_geaendert && e.CloseReason == CloseReason.UserClosing && !Ui.Frage("Die Änderungen an den Ferien übernehmen?"))
            _geaendert = false;
        else if (_geaendert) Uebernehmen();
        base.OnFormClosing(e);
    }

    /// <summary>Legende: Ferien orange, Feiertag rot, Wochenende fett, Schuljahr grün.</summary>
    private sealed class Legende : Control
    {
        public Legende()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(240, 244, 248);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            void Feld(int x, int y, Color farbe, string text)
            {
                var r = new Rectangle(x, y, 56, 14);
                using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
                g.DrawRectangle(Pens.DimGray, r);
                TextRenderer.DrawText(g, text, Ui.SchriftKlein, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Legende", Ui.SchriftKlein, new Point(0, 4), Color.Black);
            Feld(70, 4, FerienOrange, "Ferien");
            Feld(160, 4, FeiertagRot, "Feiertag");
            using (var fett = new Font(Ui.SchriftKlein, FontStyle.Bold))
            {
                var r = new Rectangle(250, 3, 84, 16);
                using var p = new Pen(Color.Black, 2);
                g.DrawRectangle(p, r);
                TextRenderer.DrawText(g, "Wochenende", fett, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Schuljahr", Ui.SchriftKlein, new Point(0, 24), Color.Black);
            Feld(70, 24, SchultagGruen, "Schultag");
        }
    }

    /// <summary>
    /// Jahreskalender wie im Vorbild: Zeilen = Monate des Schuljahres (August bis Juli, links das Jahr), Spalten = Mo–So über sechs Wochen.
    /// Ferien orange, Feiertage rot, Schultage grün, Wochenenden fett umrandet, gewählter Tag blau. Überstreichen legt Ferien an.
    /// </summary>
    private sealed class Kalenderansicht : Control
    {
        private const int Spalten = 42;
        private const int JahrBreite = 34;
        private const int MonatBreite = 72;
        private const int KopfHoehe = 18;
        private readonly FerienFenster _f;
        private DateOnly? _start, _ende;
        private static readonly string[] Monate = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
        private static readonly string[] Wochentage = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

        public Kalenderansicht(FerienFenster f)
        {
            _f = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        /// <summary>Erster Monat: August des Schuljahres (Schuljahresbeginn vor August: August des Vorjahres).</summary>
        private static DateOnly Beginn
        {
            get
            {
                var v = AppZustand.D.Einstellungen.SchuljahrVon;
                return new DateOnly(v.Month >= 8 ? v.Year : v.Year - 1, 8, 1);
            }
        }

        private float ZellBreite => Math.Max(8f, (Width - JahrBreite - MonatBreite - 2) / (float)Spalten);
        private float ZeilenHoehe => Math.Max(12f, (Height - KopfHoehe - 2) / 12f);

        private static int Versatz(DateOnly ersterTag) => ((int)ersterTag.DayOfWeek + 6) % 7;

        private RectangleF Zelle(int monat, int spalte) =>
            new((int)(JahrBreite + MonatBreite + spalte * ZellBreite), (int)(KopfHoehe + monat * ZeilenHoehe), (int)ZellBreite, (int)ZeilenHoehe);

        private RectangleF TagZelle(int monat, DateOnly datum) => Zelle(monat, Versatz(new DateOnly(datum.Year, datum.Month, 1)) + datum.Day - 1);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            var d = _f.EntwurfsDatei();
            var von = d.Einstellungen.SchuljahrVon;
            var bis = d.Einstellungen.SchuljahrBis;
            using var raster = new Pen(Color.FromArgb(200, 200, 200));
            using var fettRand = new Pen(Color.Black, 2);
            using var fett = new Font(Ui.SchriftKlein, FontStyle.Bold);
            using var kopf = new SolidBrush(Color.FromArgb(232, 236, 242));
            g.FillRectangle(kopf, 0, 0, Width, KopfHoehe);
            for (var s = 0; s < Spalten; s++)
            {
                var r = Zelle(0, s);
                TextRenderer.DrawText(g, Wochentage[s % 7], Ui.SchriftKlein, Rectangle.Round(new RectangleF(r.X, 1, r.Width, KopfHoehe - 2)), Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                g.DrawLine(raster, r.X, 0, r.X, KopfHoehe);
            }
            // Jahre links (über die Monate des Jahres zusammengefasst), Monatsnamen
            using (var jahrHg = new SolidBrush(Color.FromArgb(255, 240, 210)))
                foreach (var gruppe in Enumerable.Range(0, 12).GroupBy(m => Beginn.AddMonths(m).Year))
                {
                    var oben = Zelle(gruppe.First(), 0).Y;
                    var unten = Zelle(gruppe.Last(), 0).Bottom;
                    var r = new Rectangle(0, (int)oben, JahrBreite, (int)(unten - oben));
                    g.FillRectangle(jahrHg, r);
                    g.DrawRectangle(raster, r);
                    TextRenderer.DrawText(g, gruppe.Key.ToString(), Ui.SchriftKlein, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            for (var m = 0; m < 12; m++)
            {
                var monat = Beginn.AddMonths(m);
                var zeile = Zelle(m, 0);
                var mr = new Rectangle(JahrBreite, (int)zeile.Y, MonatBreite, (int)zeile.Height);
                g.FillRectangle(kopf, mr);
                g.DrawRectangle(raster, mr);
                TextRenderer.DrawText(g, Monate[monat.Month - 1], Ui.SchriftKlein, new Rectangle(mr.X + 3, mr.Y, mr.Width - 3, mr.Height), Color.Black, TextFormatFlags.VerticalCenter);
                var versatz = Versatz(monat);
                var tage = DateTime.DaysInMonth(monat.Year, monat.Month);
                for (var s = 0; s < Spalten; s++)
                {
                    var r = Zelle(m, s);
                    var tag = s - versatz + 1;
                    if (tag < 1 || tag > tage)
                    {
                        using var leer = new SolidBrush(Color.FromArgb(236, 236, 236));
                        g.FillRectangle(leer, r);
                        g.DrawRectangle(raster, r.X, r.Y, r.Width, r.Height);
                        continue;
                    }
                    var datum = new DateOnly(monat.Year, monat.Month, tag);
                    var ferien = _f.FerienAm(datum);
                    var wochenende = s % 7 >= 5;
                    var imJahr = datum >= von && datum <= bis;
                    var markiert = _start.HasValue && _ende.HasValue && datum >= Min(_start.Value, _ende.Value) && datum <= Max(_start.Value, _ende.Value);
                    var farbe = markiert ? Color.FromArgb(120, 170, 255)
                        : ferien is { Feiertag: true } ? FeiertagRot
                        : ferien != null ? FerienOrange
                        : imJahr && !wochenende && Kalender.TagIndex(d, datum) >= 0 ? SchultagGruen
                        : Color.White;
                    using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
                    g.DrawRectangle(raster, r.X, r.Y, r.Width, r.Height);
                    var gewaehlt = datum == _f._gewaehlt;
                    if (gewaehlt)
                    {
                        using var auswahl = new SolidBrush(Color.FromArgb(150, 60, 110, 230));
                        g.FillRectangle(auswahl, r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1);
                    }
                    TextRenderer.DrawText(g, tag.ToString(), wochenende ? fett : Ui.SchriftKlein, Rectangle.Round(r), gewaehlt ? Color.White : Color.Black,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                // Wochenenden (Sa+So) fett umrandet
                for (var w = 0; w < Spalten / 7; w++)
                {
                    var sa = 7 * w + 5;
                    var tSa = sa - versatz + 1;
                    var tSo = tSa + 1;
                    if (tSo < 1 || tSa > tage) continue;
                    var s0 = Math.Max(sa, versatz);
                    var s1 = Math.Min(sa + 1, versatz + tage - 1);
                    var a = Zelle(m, s0);
                    var z = Zelle(m, s1);
                    g.DrawRectangle(fettRand, a.X + 1, a.Y + 1, z.Right - a.X - 2, a.Height - 2);
                }
            }
            g.DrawRectangle(raster, 0, 0, Width - 1, Height - 1);
        }

        private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
        private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

        private DateOnly? Datum(Point p)
        {
            if (p.X < JahrBreite + MonatBreite || p.Y < KopfHoehe) return null;
            var m = (int)((p.Y - KopfHoehe) / ZeilenHoehe);
            var s = (int)((p.X - JahrBreite - MonatBreite) / ZellBreite);
            if (m < 0 || m >= 12 || s < 0 || s >= Spalten) return null;
            var monat = Beginn.AddMonths(m);
            var tag = s - Versatz(monat) + 1;
            return tag >= 1 && tag <= DateTime.DaysInMonth(monat.Year, monat.Month) ? new DateOnly(monat.Year, monat.Month, tag) : null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _start = _ende = Datum(e.Location);
            if (_start is DateOnly d) _f.DatumWaehlen(d);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _start.HasValue && Datum(e.Location) is { } d && d != _ende) { _ende = d; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            // überstrichener Zeitraum (mehr als ein Tag): neue Ferien; ein Klick wählt nur den Tag
            if (_start is DateOnly a && _ende is DateOnly b && a != b)
            {
                _start = _ende = null;
                _f.NeuDialog(Min(a, b), Max(a, b));
            }
            _start = _ende = null;
            Invalidate();
        }
    }
}
