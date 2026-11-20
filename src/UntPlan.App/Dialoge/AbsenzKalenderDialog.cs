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
    private static readonly Color SchuljahrGrau = Color.FromArgb(190, 190, 190);
    private static readonly Color LeerGrau = Color.FromArgb(220, 220, 220);
    private static readonly Color Rasterlinie = Color.FromArgb(192, 210, 219);
    /// <summary>Schrift des Jahreskalenders und der Legende wie im Handbuch-Bild.</summary>
    private static readonly Font KalenderSchrift = new("Segoe UI", 9f);
    private static readonly Font KalenderSchriftFett = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Color AbsenzWeiss = Color.White;
    private static readonly Color NeuBlau = Color.FromArgb(150, 200, 255);

    private readonly AbwesenheitsArt _art;
    private readonly int _elementId;
    private readonly Jahresraster _raster;
    // Kopf wie im Handbuch-Bild (Kap. 5.3, 96 dpi): zwei Knöpfe links, „Datum: Mo 30.10.2023“ und „Kalenderwoche: 44“ untereinander,
    // daneben die (nur anzeigenden) Kästchen „Unterrichtsfrei“ und „Feiertag“ des gezeigten Tages, rechts die Legende
    private readonly Label _datum = new() { AutoSize = true, Left = 80, Top = 5 };
    private readonly Label _kw = new() { AutoSize = true, Left = 80, Top = 25 };
    private readonly CheckBox _unterrichtsfrei = new() { Text = "Unterrichtsfrei", AutoSize = true, Left = 239, Top = 3, Enabled = false };
    private readonly CheckBox _feiertag = new() { Text = "Feiertag", AutoSize = true, Left = 239, Top = 21, Enabled = false };
    /// <summary>Überstreichen: 0 = automatisch (eintragen bzw. bei schon absenten Tagen löschen), 1 = nur eintragen, 2 = nur löschen.</summary>
    private int _modus;
    /// <summary>Noch nicht übernommene Bereiche: hinzufügen (true) bzw. Absenz entfernen (false).</summary>
    private readonly List<(DateOnly Von, DateOnly Bis, bool Neu)> _offen = new();

    public static void Zeigen(IWin32Window owner, ElementTyp typ, int elementId)
    {
        var art = typ switch { ElementTyp.Lehrer => AbwesenheitsArt.Lehrer, ElementTyp.Klasse => AbwesenheitsArt.Klasse, ElementTyp.Raum => AbwesenheitsArt.Raum, _ => (AbwesenheitsArt?)null };
        if (art is null) return;
        using var f = new AbsenzKalenderDialog(art.Value, typ, elementId);
        f.ShowDialog(owner);
    }

    private AbsenzKalenderDialog(AbwesenheitsArt art, ElementTyp typ, int elementId) : base("Absenzen - " + AppZustand.Doc.Index.K(typ, elementId), 1070, 300)
    {
        _art = art;
        _elementId = elementId;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(760, 330);
        // Schaltflächen wie im Handbuch-Bild: Ok, Abbrechen, Übernahme (je 76 × 23) unter dem Kalender
        Ok.Text = "Ok";
        var uebernahme = new Button { Text = "Übernahme", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        Ok.Anchor = Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Ok.SetBounds(742, 243, 76, 23);
        Abbrechen.SetBounds(822, 243, 76, 23);
        uebernahme.SetBounds(902, 243, 76, 23);
        uebernahme.Click += (_, _) => Uebernehmen();
        Ok.Click += (_, _) => Uebernehmen();
        Controls.Add(uebernahme);

        // zwei Knöpfe links oben: Überstreichen trägt nur ein bzw. löscht nur (nicht gedrückt: automatisch)
        var tip = new ToolTip();
        CheckBox Modusknopf(int modus, string symbol, string hinweis, int links)
        {
            var k = new CheckBox { Appearance = Appearance.Button, Image = Symbole.Get(symbol, 16), Bounds = new Rectangle(links, 8, 25, 25), FlatStyle = FlatStyle.Standard };
            tip.SetToolTip(k, hinweis);
            k.CheckedChanged += (_, _) =>
            {
                if (k.Checked) _modus = modus;
                else if (_modus == modus) _modus = 0;
                foreach (var andere in Controls.OfType<CheckBox>().Where(c => c != k && c.Appearance == Appearance.Button)) if (k.Checked) andere.Checked = false;
            };
            Controls.Add(k);
            return k;
        }
        Modusknopf(1, "neu", "Absenz eintragen: überstrichene Tage werden absent gesetzt", 11);
        Modusknopf(2, "loeschen", "Absenz löschen: überstrichene Tage werden aus den Absenzen entfernt", 45);
        Disposed += (_, _) => tip.Dispose();

        Controls.Add(_datum);
        Controls.Add(_kw);
        Controls.Add(_unterrichtsfrei);
        Controls.Add(_feiertag);
        Controls.Add(new Legende { Left = 375, Top = 3, Width = 440, Height = 40 });
        _raster = new Jahresraster(this) { Left = 3, Top = 70, Width = 1046, Height = 170 };
        Controls.Add(_raster);
        DatumZeigen(AppZustand.Datum);
    }

    private Schuldatei D => AppZustand.D;

    private void DatumZeigen(DateOnly t)
    {
        _datum.Text = $"Datum: {Core.Planung.Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7]} {t:dd.MM.yyyy}";
        _kw.Text = "Kalenderwoche: " + System.Globalization.ISOWeek.GetWeekOfYear(t.ToDateTime(TimeOnly.MinValue));
        var kt = Kalender.KalendertagAm(D, t);
        var ferien = Kalender.FerienAm(D, t);
        _unterrichtsfrei.Checked = kt is { Unterrichtsfrei: true } || ferien is { Feiertag: false };
        _feiertag.Checked = kt is { Feiertag: true } || ferien is { Feiertag: true };
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
        _offen.Add((von, bis, _modus switch { 1 => true, 2 => false, _ => !alleAbsent }));
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
            // gelöschte Tage: Vertretungen, Entfälle und Verlegungen der frei gewordenen Zeit werden rückgängig gemacht – mit Liste nachfragen
            if (alt.Count > 0 && Absenzverkuerzung.Ermitteln(d, neu, alt) is { } rueck && rueck.Wesentlich.Any()
                && !Ui.Frage(rueck.HinweisText(d, vorschau: true) + "\n\nSollen die Absenzen trotzdem geändert werden?", "UntPlan - Frage", this))
            {
                _raster.Invalidate();
                return;
            }
            List<Abwesenheit> gespeichert = new();
            if (Aendern("Absenzen im Schuljahreskalender", x => gespeichert = Absenzverkuerzung.Speichern(x, neu, alt).Gespeichert))
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
        // Maße nach dem Handbuch-Bild (Kap. 5.3, 96 dpi): Client 276 × 236, Felder „ab“/„bis St.“ 61 breit, Absenzgrund und
        // Absenz-Text 259 breit, OK/Cancel 76 × 23 unten rechts
        using var f = new Dialog("Absenz", 276, 236);
        f.Ok.Text = "OK";
        f.Abbrechen.Text = "Cancel";
        f.Ok.SetBounds(110, 200, 76, 23);
        f.Abbrechen.SetBounds(194, 200, 76, 23);
        var typ = _art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        var tag = Core.Planung.Planungsdienst.TageKurz[((int)a.Von.DayOfWeek + 6) % 7];
        f.Controls.Add(new Label { Text = AppZustand.Doc.Index.K(typ, a.ElementId), Left = 7, Top = 12, AutoSize = true });
        f.Controls.Add(new Label { Text = a.Von == a.Bis ? $"{a.Von:d.M.} / {tag}" : $"{a.Von:d.M.} - {a.Bis:d.M.}", Left = 7, Top = 32, AutoSize = true });
        f.Controls.Add(new Label { Text = "ab", Left = 7, Top = 49, AutoSize = true });
        f.Controls.Add(new Label { Text = "bis St.", Left = 112, Top = 49, AutoSize = true });
        var ab = new TextBox { Left = 8, Top = 65, Width = 61 };
        var bis = new TextBox { Left = 113, Top = 65, Width = 61 };
        f.Controls.Add(ab);
        f.Controls.Add(bis);
        f.Controls.Add(new Label { Text = "Absenzgrund", Left = 7, Top = 99, AutoSize = true });
        var grund = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 115, Width = 259 };
        grund.Items.Add("");
        foreach (var g in d.Abwesenheitsgruende) grund.Items.Add(g.Langname.Length > 0 ? $"{g.Kurzname} // {g.Langname}" : g.Kurzname);
        grund.SelectedIndex = 0;
        f.Controls.Add(grund);
        f.Controls.Add(new Label { Text = "Absenz-Text", Left = 7, Top = 144, AutoSize = true });
        var text = new TextBox { Left = 8, Top = 161, Width = 259 };
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
            // wie im Handbuch-Bild: Farbe nur hinter dem (unterstrichenen) Text von „Ferien“ und „Feiertag“, „Wochenende“ fett;
            // Spalten im Abstand von 120 Pixeln, zweite Zeile 20 Pixel tiefer
            using var unterstrichen = new Font(KalenderSchrift, FontStyle.Underline);
            void Feld(int x, int y, Color farbe, string text, Font schrift)
            {
                var groesse = TextRenderer.MeasureText(g, text, schrift, Size.Empty, TextFormatFlags.NoPadding);
                if (farbe != Color.Empty) using (var b = new SolidBrush(farbe)) g.FillRectangle(b, x, y, groesse.Width + 2, groesse.Height + 1);
                TextRenderer.DrawText(g, text, schrift, new Point(x + 1, y + 1), Color.Black, TextFormatFlags.NoPadding);
            }
            Feld(0, 2, Color.Empty, "Legende", Ui.Schrift);
            Feld(102, 1, FerienOrange, "Ferien", unterstrichen);
            Feld(222, 1, FeiertagRot, "Feiertag", unterstrichen);
            Feld(342, 1, Color.Empty, "Wochenende", KalenderSchriftFett);
            Feld(0, 22, Color.Empty, "Schuljahr", Ui.Schrift);
            Feld(120, 22, Color.Empty, "Absenzen", Ui.Schrift);
            Feld(240, 22, Color.Empty, "Freie Tage", Ui.Schrift);
        }
    }

    /// <summary>Jahreskalender: Zeilen = Monate des Schuljahres (links das Jahr), Spalten = Mo–So über sechs Wochen.</summary>
    private sealed class Jahresraster : Control
    {
        // Maße wie im Handbuch-Bild (96 dpi): Jahr 33, Monat 60, 42 Tagesspalten (sechs Wochen) zu 21,5 Pixeln, Zeilen 15 Pixel
        private const int Spalten = 42;
        private const int JahrBreite = 33;
        private const int MonatBreite = 60;
        private const int KopfHoehe = 15;
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

        private float ZellBreite => Math.Max(8f, Math.Min(21.5f, (Width - JahrBreite - MonatBreite - 2) / (float)Spalten));
        private float ZeilenHoehe(int monate) => Math.Max(12f, Math.Min(15f, (Height - KopfHoehe - 2) / (float)Math.Max(1, monate)));
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
            if (tag < 1 || tag > DateTime.DaysInMonth(m.Year, m.Month)) return null;
            var t = new DateOnly(m.Year, m.Month, tag);
            var e = AppZustand.D.Einstellungen;
            return t >= e.SchuljahrVon && t <= e.SchuljahrBis ? t : null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            var d = AppZustand.D;
            var monate = MonatsListe();
            var von = d.Einstellungen.SchuljahrVon;
            var bis = d.Einstellungen.SchuljahrBis;
            using var raster = new Pen(Rasterlinie);
            using var fettRand = new Pen(Color.Black, 2);
            var fett = KalenderSchriftFett;
            var schrift = KalenderSchrift;
            using (var kopf = new SolidBrush(Color.FromArgb(225, 237, 243))) g.FillRectangle(kopf, 0, 0, Width, KopfHoehe);
            for (var s = 0; s < Spalten; s++)
            {
                var r = Zelle(0, s, monate.Count);
                TextRenderer.DrawText(g, Wochentage[s % 7], schrift, Rectangle.Round(new RectangleF(r.X, 1, r.Width, KopfHoehe - 2)), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var (sa, se) = _start is { } a && _ende is { } b ? (a < b ? (a, b) : (b, a)) : (DateOnly.MaxValue, DateOnly.MinValue);
            for (var z = 0; z < monate.Count; z++)
            {
                var m = monate[z];
                var zr = Zelle(z, 0, monate.Count);
                var monatGewaehlt = AppZustand.Datum.Year == m.Year && AppZustand.Datum.Month == m.Month;
                if (monatGewaehlt) using (var hb = new SolidBrush(Color.FromArgb(255, 236, 200))) g.FillRectangle(hb, JahrBreite, zr.Y, MonatBreite, zr.Height);
                TextRenderer.DrawText(g, Monate[m.Month - 1], schrift, new Rectangle(JahrBreite + 2, (int)zr.Y, MonatBreite - 2, (int)zr.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                if (z == 0 || m.Month == 1)
                    TextRenderer.DrawText(g, m.Year.ToString(), schrift, new Rectangle(0, (int)zr.Y, JahrBreite, (int)zr.Height * 2), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                var vs = Versatz(m);
                var tage = DateTime.DaysInMonth(m.Year, m.Month);
                for (var s = 0; s < Spalten; s++)
                {
                    var r = Zelle(z, s, monate.Count);
                    var tagNr = s - vs + 1;
                    // wie im Vorbild: Tage außerhalb des Schuljahres erscheinen als leere Felder
                    if (tagNr < 1 || tagNr > tage || new DateOnly(m.Year, m.Month, tagNr) is var td && (td < von || td > bis))
                    {
                        using var leer = new SolidBrush(LeerGrau);
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
                        : absent ? (offen ? NeuBlau : AbsenzWeiss)
                        : SchuljahrGrau;
                    if (t >= sa && t <= se) farbe = NeuBlau;
                    using (var pinsel = new SolidBrush(farbe)) g.FillRectangle(pinsel, r);
                    g.DrawRectangle(raster, r.X, r.Y, r.Width, r.Height);
                    TextRenderer.DrawText(g, tagNr.ToString(), wochenende ? fett : schrift, Rectangle.Round(r), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
