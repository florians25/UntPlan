using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Absenzeingabe im Schuljahreskalender wie im Handbuch (Kap. 5.3): Fenster „Absenzen - &lt;Element&gt;“ mit Datum und Kalenderwoche,
/// Legende (Ferien, Feiertag, Wochenende; Schuljahr, Absenzen, Freie Tage) und dem Jahreskalender (Monate des Schuljahres als Zeilen,
/// Mo–So über sechs Wochen als Spalten). Tage mit gedrückter linker Maustaste überstreichen markiert sie als Absenz; nochmaliges
/// Überstreichen einer Absenz löscht sie wieder. Mit &lt;Übernahme&gt; bzw. &lt;Ok&gt; wird je zusammenhängendem Bereich nach Stunden,
/// Absenzgrund und Absenz-Text gefragt. Die Absenzen erscheinen anschließend im Absenzfenster.
/// </summary>
public sealed class AbsenzKalenderDialog : Dialog
{
    private static readonly Color FerienOrange = Color.FromArgb(255, 153, 51);
    private static readonly Color FeiertagRot = Color.FromArgb(240, 40, 40);
    private static readonly Color SchuljahrGrau = Color.FromArgb(200, 200, 200);
    private static readonly Color AbsenzWeiss = Color.White;
    private static readonly Color NeuBlau = Color.FromArgb(150, 200, 255);

    private readonly AbwesenheitsArt _art;
    private readonly int _elementId;
    private readonly Jahresraster _raster;
    private readonly Label _datum = new() { AutoSize = true, Left = 90, Top = 10 };
    private readonly Label _kw = new() { AutoSize = true, Left = 90, Top = 28 };
    /// <summary>Noch nicht übernommene Bereiche: hinzufügen (true) bzw. Absenz entfernen (false).</summary>
    private readonly List<(DateOnly Von, DateOnly Bis, bool Neu)> _offen = new();

    public static void Zeigen(IWin32Window owner, ElementTyp typ, int elementId)
    {
        var art = typ switch { ElementTyp.Lehrer => AbwesenheitsArt.Lehrer, ElementTyp.Klasse => AbwesenheitsArt.Klasse, ElementTyp.Raum => AbwesenheitsArt.Raum, _ => (AbwesenheitsArt?)null };
        if (art is null) return;
        using var f = new AbsenzKalenderDialog(art.Value, typ, elementId);
        f.ShowDialog(owner);
    }

    private AbsenzKalenderDialog(AbwesenheitsArt art, ElementTyp typ, int elementId) : base("Absenzen - " + AppZustand.Doc.Index.K(typ, elementId), 1000, 380)
    {
        _art = art;
        _elementId = elementId;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(760, 330);
        Ok.Text = "Ok";
        var uebernahme = new Button { Text = "Übernahme", Width = 88, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        uebernahme.Location = new Point(ClientSize.Width - 96, ClientSize.Height - 36);
        Ok.Left -= 96;
        Abbrechen.Left -= 96;
        uebernahme.Click += (_, _) => Uebernehmen();
        Ok.Click += (_, _) => Uebernehmen();
        Controls.Add(uebernahme);

        Controls.Add(new Label { Text = "Datum:", AutoSize = true, Left = 40, Top = 10 });
        Controls.Add(new Label { Text = "Kalenderwoche:", AutoSize = true, Left = 4, Top = 28 });
        _kw.Left = 96;
        _datum.Left = 96;
        Controls.Add(_datum);
        Controls.Add(_kw);
        Controls.Add(new Legende { Left = 300, Top = 6, Width = 520, Height = 40 });
        _raster = new Jahresraster(this)
        {
            Left = 6, Top = 54, Width = ClientSize.Width - 12, Height = ClientSize.Height - 100,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Controls.Add(_raster);
        DatumZeigen(AppZustand.Datum);
    }

    private Schuldatei D => AppZustand.D;

    private void DatumZeigen(DateOnly t)
    {
        _datum.Text = $"{Core.Planung.Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {t:dd.MM.yyyy}";
        _kw.Text = System.Globalization.ISOWeek.GetWeekOfYear(t.ToDateTime(TimeOnly.MinValue)).ToString();
    }

    /// <summary>Ist das Element an diesem Tag (laut gespeicherter Absenzen und noch nicht übernommener Bereiche) absent?</summary>
    private (bool Absent, bool Offen) Status(DateOnly t)
    {
        foreach (var o in Enumerable.Reverse(_offen))
            if (t >= o.Von && t <= o.Bis) return (o.Neu, true);
        return (D.Abwesenheiten.Any(a => a.Art == _art && a.ElementId == _elementId && t >= a.Von && t <= a.Bis), false);
    }

    /// <summary>Überstrichener Bereich: ist er schon ganz absent, wird die Absenz gelöscht, sonst als neue Absenz markiert.</summary>
    private void Ueberstrichen(DateOnly von, DateOnly bis)
    {
        var alleAbsent = Kalender.Tage(von, bis).All(t => Status(t).Absent);
        _offen.Add((von, bis, !alleAbsent));
        DatumZeigen(bis);
        _raster.Invalidate();
    }

    private void Uebernehmen()
    {
        if (_offen.Count == 0) return;
        var d = D;
        // Tage mit endgültigem Zustand bestimmen, dann zusammenhängende Bereiche bilden (Wochenenden/Ferien verbinden Bereiche)
        var tage = _offen.SelectMany(o => Kalender.Tage(o.Von, o.Bis)).Distinct().OrderBy(t => t).ToList();
        var neuTage = tage.Where(t => Status(t).Absent && !d.Abwesenheiten.Any(a => a.Art == _art && a.ElementId == _elementId && t >= a.Von && t <= a.Bis)).ToList();
        var wegTage = tage.Where(t => !Status(t).Absent).ToHashSet();
        var neu = new List<Abwesenheit>();
        foreach (var (von, bis) in Bereiche(neuTage))
        {
            var a = new Abwesenheit { Art = _art, ElementId = _elementId, Von = von, Bis = bis, StundeVon = 1, StundeBis = 99, Benutzer = AppZustand.Benutzer };
            if (!AbsenzFrage(a)) continue;
            neu.Add(a);
        }
        // gelöschte Tage: bestehende Absenzen kürzen bzw. teilen
        var alt = new List<Abwesenheit>();
        foreach (var a in d.Abwesenheiten.Where(a => a.Art == _art && a.ElementId == _elementId && wegTage.Any(t => t >= a.Von && t <= a.Bis)).ToList())
        {
            alt.Add(a);
            foreach (var (von, bis) in Bereiche(Kalender.Tage(a.Von, a.Bis).Where(t => !wegTage.Contains(t)).ToList(), luecken: false))
                neu.Add(new Abwesenheit
                {
                    Id = 0, Art = a.Art, ElementId = a.ElementId, Von = von, Bis = bis, StundeVon = von == a.Von ? a.StundeVon : 1, StundeBis = bis == a.Bis ? a.StundeBis : 99,
                    GrundId = a.GrundId, Text = a.Text, BegleitLehrerIds = a.BegleitLehrerIds.ToList(), Benutzer = a.Benutzer,
                });
        }
        _offen.Clear();
        if (neu.Count > 0 || alt.Count > 0)
        {
            List<Abwesenheit> gespeichert = new();
            if (Aendern("Absenzen im Schuljahreskalender", x => gespeichert = AbsenzVerwaltung.Speichern(x, neu, alt)))
                AbwesenheitenFenster.VeranstaltungenPruefen(this, gespeichert, (t, aktion) => Aendern(t, aktion));
        }
        _raster.Invalidate();
    }

    private static bool Aendern(string text, Action<Schuldatei> aktion)
    {
        try
        {
            AppZustand.Doc.Aendern(text, AenderungsArt.Vertretung, aktion);
            return true;
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            return false;
        }
    }

    /// <summary>
    /// Zusammenhängende Bereiche aus einzelnen Tagen. Mit <paramref name="luecken"/> verbinden unterrichtsfreie Tage dazwischen
    /// (Wochenende, Ferien) zwei Bereiche zu einem.
    /// </summary>
    private List<(DateOnly Von, DateOnly Bis)> Bereiche(List<DateOnly> tage, bool luecken = true)
    {
        var res = new List<(DateOnly, DateOnly)>();
        foreach (var t in tage.OrderBy(x => x))
        {
            if (res.Count > 0)
            {
                var (v, b) = res[^1];
                var anschluss = b.AddDays(1) == t || (luecken && Kalender.Tage(b.AddDays(1), t.AddDays(-1)).All(x => !Kalender.IstUnterrichtstag(D, x)));
                if (anschluss) { res[^1] = (v, t); continue; }
            }
            res.Add((t, t));
        }
        return res;
    }

    /// <summary>Kleiner Dialog „Absenz“ wie im Vorbild: Element, Zeitraum, ab / bis St., Absenzgrund, Absenz-Text.</summary>
    private bool AbsenzFrage(Abwesenheit a)
    {
        var d = D;
        using var f = new Dialog("Absenz", 270, 290);
        f.Ok.Text = "OK";
        f.Abbrechen.Text = "Cancel";
        var typ = _art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        var tag = Core.Planung.Planungsdienst.TageKurz[((int)a.Von.DayOfWeek + 6) % 7];
        f.Controls.Add(new Label { Text = AppZustand.Doc.Index.K(typ, a.ElementId), Left = 10, Top = 12, AutoSize = true });
        f.Controls.Add(new Label { Text = a.Von == a.Bis ? $"{a.Von:d.M.} / {tag}" : $"{a.Von:d.M.} - {a.Bis:d.M.}", Left = 10, Top = 34, AutoSize = true });
        f.Controls.Add(new Label { Text = "ab", Left = 10, Top = 54, AutoSize = true });
        f.Controls.Add(new Label { Text = "bis St.", Left = 140, Top = 54, AutoSize = true });
        var ab = new TextBox { Left = 10, Top = 72, Width = 70 };
        var bis = new TextBox { Left = 140, Top = 72, Width = 70 };
        f.Controls.Add(ab);
        f.Controls.Add(bis);
        f.Controls.Add(new Label { Text = "Absenzgrund", Left = 10, Top = 108, AutoSize = true });
        var grund = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 126, Width = 250 };
        grund.Items.Add("");
        foreach (var g in d.Abwesenheitsgruende) grund.Items.Add(g.Langname.Length > 0 ? $"{g.Kurzname} // {g.Langname}" : g.Kurzname);
        grund.SelectedIndex = 0;
        f.Controls.Add(grund);
        f.Controls.Add(new Label { Text = "Absenz-Text", Left = 10, Top = 162, AutoSize = true });
        var text = new TextBox { Left = 10, Top = 180, Width = 250 };
        f.Controls.Add(text);
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        var letzte = Math.Max(1, d.Zeitraster.StundenProTag);
        if (int.TryParse(ab.Text.Trim(), out var sv)) a.StundeVon = Math.Clamp(sv, 1, letzte);
        if (int.TryParse(bis.Text.Trim(), out var sb)) a.StundeBis = sb >= letzte ? 99 : Math.Max(a.StundeVon, sb);
        a.GrundId = grund.SelectedIndex > 0 ? d.Abwesenheitsgruende[grund.SelectedIndex - 1].Id : null;
        a.Text = text.Text.Trim();
        return true;
    }

    /// <summary>Legende: Ferien, Feiertag, Wochenende; Schuljahr, Absenzen, Freie Tage.</summary>
    private sealed class Legende : Control
    {
        public Legende() => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);
            void Feld(int x, int y, Color farbe, string text, bool fett = false, bool rand = false)
            {
                var r = new Rectangle(x, y, 90, 16);
                if (farbe != Color.Empty) using (var b = new SolidBrush(farbe)) g.FillRectangle(b, r);
                if (rand) g.DrawRectangle(Pens.Black, r);
                using var f = new Font(Ui.Schrift, fett ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, text, f, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "Legende", Ui.Schrift, new Point(0, 2), Color.Black);
            Feld(110, 1, FerienOrange, "Ferien");
            Feld(230, 1, FeiertagRot, "Feiertag");
            Feld(350, 1, Color.Empty, "Wochenende", true);
            TextRenderer.DrawText(g, "Schuljahr", Ui.Schrift, new Point(0, 22), Color.Black);
            Feld(110, 21, AbsenzWeiss, "Absenzen", rand: true);
            Feld(230, 21, Color.Empty, "Freie Tage");
        }
    }

    /// <summary>Jahreskalender: Zeilen = Monate des Schuljahres (links das Jahr), Spalten = Mo–So über sechs Wochen.</summary>
    private sealed class Jahresraster : Control
    {
        private const int Spalten = 37;
        private const int JahrBreite = 40;
        private const int MonatBreite = 70;
        private const int KopfHoehe = 18;
        private static readonly string[] Monate = { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" };
        private static readonly string[] Wochentage = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
        private readonly AbsenzKalenderDialog _f;
        private DateOnly? _start, _ende;

        public Jahresraster(AbsenzKalenderDialog f)
        {
            _f = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        private static List<DateOnly> MonatsListe()
        {
            var e = AppZustand.D.Einstellungen;
            var res = new List<DateOnly>();
            for (var m = new DateOnly(e.SchuljahrVon.Year, e.SchuljahrVon.Month, 1); m <= e.SchuljahrBis && res.Count < 14; m = m.AddMonths(1)) res.Add(m);
            return res;
        }

        private float ZellBreite => Math.Max(8f, (Width - JahrBreite - MonatBreite - 2) / (float)Spalten);
        private float ZeilenHoehe(int monate) => Math.Max(12f, Math.Min(20f, (Height - KopfHoehe - 2) / (float)Math.Max(1, monate)));
        private static int Versatz(DateOnly ersterTag) => ((int)ersterTag.DayOfWeek + 6) % 7;

        private RectangleF Zelle(int zeile, int spalte, int monate) =>
            new((int)(JahrBreite + MonatBreite + spalte * ZellBreite), (int)(KopfHoehe + zeile * ZeilenHoehe(monate)), (int)ZellBreite, (int)ZeilenHoehe(monate));

        private DateOnly? TagBei(Point p)
        {
            var monate = MonatsListe();
            var zeile = (int)((p.Y - KopfHoehe) / ZeilenHoehe(monate.Count));
            var spalte = (int)((p.X - JahrBreite - MonatBreite) / ZellBreite);
            if (p.Y < KopfHoehe || p.X < JahrBreite + MonatBreite || zeile < 0 || zeile >= monate.Count || spalte < 0 || spalte >= Spalten) return null;
            var m = monate[zeile];
            var tag = spalte - Versatz(m) + 1;
            return tag >= 1 && tag <= DateTime.DaysInMonth(m.Year, m.Month) ? new DateOnly(m.Year, m.Month, tag) : null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            var d = AppZustand.D;
            var monate = MonatsListe();
            var von = d.Einstellungen.SchuljahrVon;
            var bis = d.Einstellungen.SchuljahrBis;
            using var raster = new Pen(Color.FromArgb(170, 170, 170));
            using var fettRand = new Pen(Color.Black, 2);
            using var fett = new Font(Ui.SchriftKlein, FontStyle.Bold);
            using (var kopf = new SolidBrush(Color.FromArgb(232, 236, 242))) g.FillRectangle(kopf, 0, 0, Width, KopfHoehe);
            for (var s = 0; s < Spalten; s++)
            {
                var r = Zelle(0, s, monate.Count);
                TextRenderer.DrawText(g, Wochentage[s % 7], Ui.SchriftKlein, Rectangle.Round(new RectangleF(r.X, 1, r.Width, KopfHoehe - 2)), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var (sa, se) = _start is { } a && _ende is { } b ? (a < b ? (a, b) : (b, a)) : (DateOnly.MaxValue, DateOnly.MinValue);
            for (var z = 0; z < monate.Count; z++)
            {
                var m = monate[z];
                var zr = Zelle(z, 0, monate.Count);
                var monatGewaehlt = AppZustand.Datum.Year == m.Year && AppZustand.Datum.Month == m.Month;
                if (monatGewaehlt) using (var hb = new SolidBrush(Color.FromArgb(255, 236, 200))) g.FillRectangle(hb, JahrBreite, zr.Y, MonatBreite, zr.Height);
                TextRenderer.DrawText(g, Monate[m.Month - 1], Ui.SchriftKlein, new Rectangle(JahrBreite + 2, (int)zr.Y, MonatBreite - 2, (int)zr.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                if (z == 0 || m.Month == 1)
                    TextRenderer.DrawText(g, m.Year.ToString(), Ui.Schrift, new Rectangle(0, (int)zr.Y, JahrBreite, (int)zr.Height * 2), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                var vs = Versatz(m);
                var tage = DateTime.DaysInMonth(m.Year, m.Month);
                for (var s = 0; s < Spalten; s++)
                {
                    var r = Zelle(z, s, monate.Count);
                    var tagNr = s - vs + 1;
                    if (tagNr < 1 || tagNr > tage)
                    {
                        using var leer = new SolidBrush(Color.FromArgb(225, 225, 225));
                        g.FillRectangle(leer, r);
                        g.DrawRectangle(raster, r.X, r.Y, r.Width, r.Height);
                        continue;
                    }
                    var t = new DateOnly(m.Year, m.Month, tagNr);
                    var ferien = Kalender.FerienAm(d, t);
                    var kt = Kalender.KalendertagAm(d, t);
                    var wochenende = s % 7 >= 5;
                    var (absent, offen) = _f.Status(t);
                    Color farbe = ferien is { Feiertag: true } || kt is { Feiertag: true } ? FeiertagRot
                        : ferien != null || kt is { Unterrichtsfrei: true } ? FerienOrange
                        : t < von || t > bis ? Color.White
                        : absent ? (offen ? NeuBlau : AbsenzWeiss)
                        : SchuljahrGrau;
                    if (t >= sa && t <= se) farbe = NeuBlau;
                    using (var pinsel = new SolidBrush(farbe)) g.FillRectangle(pinsel, r);
                    g.DrawRectangle(raster, r.X, r.Y, r.Width, r.Height);
                    if (absent && !wochenende && ferien is null) g.DrawRectangle(Pens.Black, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                    TextRenderer.DrawText(g, tagNr.ToString(), wochenende ? fett : Ui.SchriftKlein, Rectangle.Round(r), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (wochenende) g.DrawRectangle(fettRand, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || TagBei(e.Location) is not { } t) return;
            _start = _ende = t;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_start is null || TagBei(e.Location) is not { } t || t == _ende) return;
            _ende = t;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_start is not { } a || _ende is not { } b) return;
            _start = _ende = null;
            Capture = false;
            _f.Ueberstrichen(a < b ? a : b, a < b ? b : a);
        }
    }
}
