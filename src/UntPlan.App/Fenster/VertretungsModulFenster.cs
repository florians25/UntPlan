using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>Kalender der Vertretungsplanung: Planungsdatum wählen, Tage mit Absenzen hervorgehoben, Kalendereintrag (unterrichtsfrei, freie Stunden, Notiz).</summary>
public sealed class KalenderFenster : MdiFenster
{
    private readonly MonthCalendar _kal = new() { MaxSelectionCount = 1, Dock = DockStyle.Top, ShowWeekNumbers = true };
    private readonly Label _info = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(6), BackColor = Ui.Kopf };
    private readonly CheckBox _frei = new() { Text = "unterrichtsfrei", AutoSize = true };
    private readonly CheckBox _feiertag = new() { Text = "Feiertag", AutoSize = true };
    private readonly TextBox _stunden = new() { Width = 70 };
    private readonly TextBox _notiz = new() { Width = 200 };
    private bool _laedt;

    public KalenderFenster()
    {
        Text = "Kalender";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("kalender");
        Size = new Size(260, 420);
        Positionieren();
        _kal.SetDate(AppZustand.Datum.ToDateTime(TimeOnly.MinValue));
        _kal.DateSelected += (_, e) => { if (!_laedt) AppZustand.DatumSetzen(DateOnly.FromDateTime(e.Start)); };
        var eingabe = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6), WrapContents = false, AutoScroll = true };
        var uebernehmen = new Button { Text = "Kalendereintrag übernehmen", Width = 200, Height = 26 };
        uebernehmen.Click += (_, _) => Speichern();
        eingabe.Controls.Add(_frei);
        eingabe.Controls.Add(_feiertag);
        eingabe.Controls.Add(new Label { Text = "nur diese Stunden frei (z. B. 6-8):", AutoSize = true });
        eingabe.Controls.Add(_stunden);
        eingabe.Controls.Add(new Label { Text = "Notiz (wird nicht gedruckt):", AutoSize = true });
        eingabe.Controls.Add(_notiz);
        eingabe.Controls.Add(uebernehmen);
        Controls.Add(eingabe);
        Controls.Add(_info);
        Controls.Add(_kal);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void DatumGewechselt(DateOnly d)
    {
        if (IsDisposed) return;
        _laedt = true;
        _kal.SetDate(d.ToDateTime(TimeOnly.MinValue));
        _laedt = false;
        Neuaufbau();
    }

    private void Neuaufbau()
    {
        var datum = AppZustand.Datum;
        var fett = D.Abwesenheiten.SelectMany(a => Kalender.Tage(a.Von, a.Bis.DayNumber - a.Von.DayNumber > 120 ? a.Von : a.Bis))
            .Concat(D.Veranstaltungen.SelectMany(v => Kalender.Tage(v.Von, v.Bis.DayNumber - v.Von.DayNumber > 60 ? v.Von : v.Bis)))
            .Distinct().Select(t => t.ToDateTime(TimeOnly.MinValue)).ToArray();
        _kal.BoldedDates = fett;
        var ferien = D.Ferien.FirstOrDefault(f => datum >= f.Von && datum <= f.Bis);
        var lehrer = D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Lehrer && datum >= a.Von && datum <= a.Bis);
        var klassen = D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && datum >= a.Von && datum <= a.Bis);
        var offen = D.Vertretungen.Count(v => v.Datum == datum && v.Offen);
        var woche = Kalender.WochenTyp(D, datum);
        _info.Text = $"{Planungsdienst.TageLang[((int)datum.DayOfWeek + 6) % 7]}, {Format.Datum(datum)}  (KW {System.Globalization.ISOWeek.GetWeekOfYear(datum.ToDateTime(TimeOnly.MinValue))}{(string.IsNullOrEmpty(woche) ? "" : ", Woche " + woche)})\n" +
                     (ferien != null ? $"Ferien: {(ferien.Langname.Length > 0 ? ferien.Langname : ferien.Kurzname)}\n" : Kalender.IstUnterrichtstag(D, datum) ? $"Periode: {D.PeriodeAm(datum).Name}\n" : "kein Unterrichtstag\n") +
                     $"Absent: {lehrer} Lehrer, {klassen} Klassen · offen: {offen}";
        _info.ForeColor = offen > 0 ? Color.Firebrick : Color.Black;
        var k = Kalender.KalendertagAm(D, datum);
        _frei.Checked = k?.Unterrichtsfrei == true;
        _feiertag.Checked = k?.Feiertag == true;
        _stunden.Text = k?.FreieStunden ?? "";
        _notiz.Text = k?.Notiz ?? "";
    }

    private void Speichern()
    {
        var datum = AppZustand.Datum;
        var frei = _frei.Checked;
        var feiertag = _feiertag.Checked;
        var stunden = _stunden.Text.Trim();
        var notiz = _notiz.Text.Trim();
        Aendern("Kalendereintrag", AenderungsArt.Vertretung, d =>
        {
            d.Kalender.RemoveAll(x => x.Datum == datum);
            if (frei || feiertag || stunden.Length > 0 || notiz.Length > 0)
                d.Kalender.Add(new Kalendertag { Datum = datum, Unterrichtsfrei = frei, Feiertag = feiertag, FreieStunden = stunden, Notiz = notiz });
            Kalender.CacheLeeren();
            Vertretungen.Aktualisieren(d, datum);
        });
    }
}

/// <summary>Absenzen von Lehrern, Klassen und Räumen (Reiter), Zeitraum um das Planungsdatum, Teilen/Kopieren.</summary>
public sealed class AbwesenheitenFenster : MdiFenster
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _zeitraum = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };

    public AbwesenheitenFenster()
    {
        Text = "Absenzen";
        HilfeThema = "Abwesenheiten";
        Icon = SymbolIcon("abwesenheit");
        Size = new Size(760, 300);
        Positionieren();
        foreach (var t in new[] { "Lehrer", "Klassen", "Räume" }) _tabs.TabPages.Add(t);
        _tabs.SelectedIndexChanged += (_, _) => Neuaufbau();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Absenz", "neu", Neu, false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Absenz teilen", "tausch", Teilen));
        ts.Items.Add(Knopf("Absenz kopieren (andere Elemente)", "kopieren", Kopieren));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        _zeitraum.Items.AddRange(new object[] { "Planungsdatum", "Woche", "ab Planungsdatum", "gesamtes Schuljahr" });
        _zeitraum.SelectedIndex = 0;
        _zeitraum.SelectedIndexChanged += (_, _) => Neuaufbau();
        ts.Items.Add(new ToolStripLabel("Zeitraum:"));
        ts.Items.Add(_zeitraum);
        ts.Items.Add(Knopf("Veranstaltungen", "klassen", () => HauptFenster.Oeffnen("veranst", () => new VeranstaltungenFenster()), false));
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Abs-Nr.", 55), ("E", "Name", 70), ("V", "Von", 82), ("SV", "Std.", 38), ("B", "Bis", 82), ("SB", "Std.", 38), ("G", "Absenzgrund", 100), ("Beg", "Begleitlehrer", 100), ("O", "offen", 45), ("T", "Text", 200) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); else if (e.RowIndex < 0) Neu(); };
        _g.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) Loeschen(); if (e.KeyCode == Keys.Insert) Neu(); };
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Neue Absenz…", Symbole.Get("neu"), (_, _) => Neu());
        cm.Items.Add("Bearbeiten…", Symbole.Get("einstellungen"), (_, _) => Bearbeiten());
        cm.Items.Add("Absenz teilen…", Symbole.Get("tausch"), (_, _) => Teilen());
        cm.Items.Add("Absenz kopieren…", null, (_, _) => Kopieren());
        cm.Items.Add("Absenzen zusammenfassen", null, (_, _) => Zusammenfassen());
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Löschen", Symbole.Get("loeschen"), (_, _) => Loeschen());
        _g.ContextMenuStrip = cm;
        Controls.Add(_g);
        Controls.Add(_tabs);
        Controls.Add(ts);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();
    private void DatumGewechselt(DateOnly _) { if (!IsDisposed) Neuaufbau(); }

    private AbwesenheitsArt Art => (AbwesenheitsArt)Math.Max(0, _tabs.SelectedIndex);

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        var heute = AppZustand.Datum;
        var (von, bis) = _zeitraum.SelectedIndex switch
        {
            0 => (heute, heute),
            1 => (Kalender.Montag(heute), Kalender.Montag(heute).AddDays(6)),
            2 => (heute, DateOnly.MaxValue),
            _ => (DateOnly.MinValue, DateOnly.MaxValue),
        };
        var typ = Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        _g.Columns["Beg"]!.Visible = Art == AbwesenheitsArt.Klasse;
        foreach (var a in D.Abwesenheiten.Where(a => a.Art == Art && a.Bis >= von && a.Von <= bis).OrderBy(a => Ix.K(typ, a.ElementId), StringComparer.Create(Format.De, true)).ThenBy(a => a.Von))
        {
            var offen = D.Vertretungen.Count(v => v.AbwesenheitId == a.Id && v.Offen);
            var r = _g.Rows.Add(a.Id, Ix.K(typ, a.ElementId), $"{Planungsdienst.TageKurz[((int)a.Von.DayOfWeek + 6) % 7]} {a.Von:dd.MM.yy}", a.StundeVon,
                $"{Planungsdienst.TageKurz[((int)a.Bis.DayOfWeek + 6) % 7]} {a.Bis:dd.MM.yy}", a.StundeBis >= 99 ? "" : a.StundeBis.ToString(), Vertretungen.GrundText(D, Ix, a),
                string.Join(", ", a.BegleitLehrerIds.Select(Ix.KLehrer)), offen == 0 ? "" : offen.ToString(), a.Text);
            _g.Rows[r].Tag = a;
            if (offen > 0) _g.Rows[r].Cells["O"].Style.ForeColor = Color.Firebrick;
        }
        _tabs.TabPages[0].Text = $"Lehrer ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Lehrer && a.Bis >= von && a.Von <= bis)})";
        _tabs.TabPages[1].Text = $"Klassen ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && a.Bis >= von && a.Von <= bis)})";
        _tabs.TabPages[2].Text = $"Räume ({D.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && a.Bis >= von && a.Von <= bis)})";
    }

    private List<Abwesenheit> Markierte() => _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Abwesenheit>().ToList();

    private static Abwesenheit Kopie(Abwesenheit a) => new()
    {
        Id = a.Id, Art = a.Art, ElementId = a.ElementId, Von = a.Von, Bis = a.Bis, StundeVon = a.StundeVon, StundeBis = a.StundeBis,
        GrundId = a.GrundId, Text = a.Text, BegleitLehrerIds = new List<int>(a.BegleitLehrerIds), Erstellt = a.Erstellt, Benutzer = a.Benutzer,
    };

    private void Neu()
    {
        var a = new Abwesenheit { Art = Art, Von = AppZustand.Datum, Bis = AppZustand.Datum };
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, Array.Empty<Abwesenheit>(), "Absenz eingeben");
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var a = Kopie(alt);
        if (AbwesenheitDialog.Zeigen(this, a)) Speichern(new[] { a }, new[] { alt }, "Absenz geändert");
    }

    private void Teilen()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        if (alt.Von == alt.Bis) { Ui.Info("Eine eintägige Absenz kann nicht geteilt werden."); return; }
        var text = Ui.Eingabe($"Absenz {Format.Datum(alt.Von)} – {Format.Datum(alt.Bis)} teilen: zweiter Teil beginnt am (TT.MM.JJJJ):", "Absenz teilen", Format.Datum(alt.Von.AddDays(1)));
        if (text is null || !DateOnly.TryParse(text, Format.De, out var ab) || ab <= alt.Von || ab > alt.Bis) return;
        var a = Kopie(alt);
        a.Bis = ab.AddDays(-1);
        a.StundeBis = 99;
        var b = Kopie(alt);
        b.Id = 0;
        b.Von = ab;
        b.StundeVon = 1;
        Speichern(new[] { a, b }, new[] { alt }, "Absenz teilen");
    }

    private void Kopieren()
    {
        if (_g.CurrentRow?.Tag is not Abwesenheit alt) return;
        var typ = alt.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };
        var w = AuswahlDialog.Mehrere(this, typ, "Absenz für weitere Elemente übernehmen", Array.Empty<int>());
        if (w is null || w.Count == 0) return;
        var neu = w.Where(e => e.Id != alt.ElementId).Select(e => { var a = Kopie(alt); a.Id = 0; a.ElementId = e.Id; a.Erstellt = DateTime.Now; return a; }).ToArray();
        Speichern(neu, Array.Empty<Abwesenheit>(), "Absenz kopieren");
    }

    private void Zusammenfassen()
    {
        var m = Markierte().OrderBy(a => a.Von).ToList();
        if (m.Count < 2 || m.Select(a => (a.Art, a.ElementId)).Distinct().Count() > 1) { Ui.Info("Bitte mindestens zwei Absenzen desselben Elements markieren."); return; }
        var a = Kopie(m[0]);
        a.Bis = m.Max(x => x.Bis);
        a.StundeBis = m.First(x => x.Bis == a.Bis).StundeBis;
        Speichern(new[] { a }, m.ToArray(), "Absenzen zusammenfassen");
    }

    private void Speichern(IReadOnlyList<Abwesenheit> neu, IReadOnlyList<Abwesenheit> alt, string text)
    {
        var altIds = alt.Select(a => a.Id).ToHashSet();
        Aendern(text, AenderungsArt.Vertretung, d =>
        {
            d.Abwesenheiten.RemoveAll(x => altIds.Contains(x.Id));
            foreach (var a in neu)
            {
                if (a.Id == 0 || !altIds.Contains(a.Id) && d.Abwesenheiten.Any(x => x.Id == a.Id)) a.Id = d.NeueId();
                d.Abwesenheiten.Add(a);
            }
            var alle = neu.Concat(alt).ToList();
            if (alle.Count > 0) Vertretungen.Aktualisieren(d, alle.Min(a => a.Von), alle.Max(a => a.Bis));
        });
    }

    private void Loeschen()
    {
        var liste = Markierte();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Absenz(en) löschen? Daraus entstandene Vertretungen werden entfernt.")) return;
        Speichern(Array.Empty<Abwesenheit>(), liste, "Absenz löschen");
    }
}

/// <summary>Veranstaltungen: Exkursionen, Klassenfahrten, Schulveranstaltungen mit Auswirkung auf Stunden- und Vertretungsplan.</summary>
public sealed class VeranstaltungenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public VeranstaltungenFenster()
    {
        Text = "Veranstaltungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("klassen");
        Size = new Size(900, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Veranstaltung", "neu", Neu, false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Nr.", 45), ("T", "Veranstaltung", 160), ("V", "Von", 80), ("SV", "Std.", 38), ("B", "Bis", 80), ("SB", "Std.", 38), ("K", "Klassen", 100), ("L", "Lehrer", 100), ("R", "Räume", 80), ("G", "Grund", 80), ("Z", "zählt", 45) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var v in D.Veranstaltungen.OrderBy(v => v.Von))
        {
            var r = _g.Rows.Add(v.Id, v.Text, Format.Datum(v.Von), v.StundeVon, Format.Datum(v.Bis), v.StundeBis >= 99 ? "" : v.StundeBis.ToString(), Ix.KKlassen(v.KlassenIds),
                string.Join(",", v.LehrerIds.Select(Ix.KLehrer)), string.Join(",", v.RaumIds.Select(Ix.KRaum)), Vertretungen.GrundText(D, v.GrundId), v.Zaehlen ? "ja" : "");
            _g.Rows[r].Tag = v;
            if (v.Bis < AppZustand.Datum) _g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private static Veranstaltung Kopie(Veranstaltung v) => System.Text.Json.JsonSerializer.Deserialize<Veranstaltung>(System.Text.Json.JsonSerializer.Serialize(v))!;

    private void Neu()
    {
        var v = new Veranstaltung { Von = AppZustand.Datum, Bis = AppZustand.Datum, Text = "Exkursion" };
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Aendern("Veranstaltung", AenderungsArt.Vertretung, d =>
        {
            v.Id = d.NeueId();
            d.Veranstaltungen.Add(v);
            Vertretungen.Aktualisieren(d, v.Von, v.Bis);
        });
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Veranstaltung alt) return;
        var v = Kopie(alt);
        if (!VeranstaltungDialog.Zeigen(this, v)) return;
        Aendern("Veranstaltung geändert", AenderungsArt.Vertretung, d =>
        {
            d.Veranstaltungen.RemoveAll(x => x.Id == alt.Id);
            d.Veranstaltungen.Add(v);
            Vertretungen.Aktualisieren(d, alt.Von < v.Von ? alt.Von : v.Von, alt.Bis > v.Bis ? alt.Bis : v.Bis);
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Veranstaltung>().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Veranstaltung(en) löschen?")) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Veranstaltung löschen", AenderungsArt.Vertretung, d =>
        {
            d.Veranstaltungen.RemoveAll(v => ids.Contains(v.Id));
            Vertretungen.Aktualisieren(d, liste.Min(v => v.Von), liste.Max(v => v.Bis));
        });
    }
}

/// <summary>Klausuren (Prüfungen) mit Aufsichten; kollidierender Unterricht der Klassen entfällt.</summary>
public sealed class KlausurenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public KlausurenFenster()
    {
        Text = "Klausuren";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("kommentar");
        Size = new Size(860, 320);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Klausur", "neu", () => KlausurDialog.Neu(this, AppZustand.Datum, 1, null), false));
        ts.Items.Add(Knopf("Bearbeiten", "einstellungen", Bearbeiten));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("N", "Name", 150), ("D", "Datum", 90), ("S", "Stunden", 60), ("K", "Klassen", 110), ("L", "Aufsicht", 110), ("R", "Räume", 90), ("E", "Unterr. entfällt", 95), ("A", "aktiv", 45), ("T", "Text", 160) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var k in D.Klausuren.OrderBy(k => k.Datum).ThenBy(k => k.StundeVon))
        {
            var r = _g.Rows.Add(k.Name, $"{Planungsdienst.TageKurz[((int)k.Datum.DayOfWeek + 6) % 7]} {Format.Datum(k.Datum)}", k.StundeVon == k.StundeBis ? k.StundeVon.ToString() : $"{k.StundeVon}-{k.StundeBis}",
                Ix.KKlassen(k.KlassenIds), string.Join(",", k.LehrerIds.Select(Ix.KLehrer)), string.Join(",", k.RaumIds.Select(Ix.KRaum)), k.UnterrichtEntfaellt ? "ja" : "", k.Aktiv ? "ja" : "", k.Text);
            _g.Rows[r].Tag = k;
        }
    }

    private void Bearbeiten()
    {
        if (_g.CurrentRow?.Tag is not Klausur alt) return;
        var k = System.Text.Json.JsonSerializer.Deserialize<Klausur>(System.Text.Json.JsonSerializer.Serialize(alt))!;
        if (!KlausurDialog.Zeigen(this, k)) return;
        Aendern("Klausur geändert", AenderungsArt.Vertretung, d =>
        {
            d.Klausuren.RemoveAll(x => x.Id == alt.Id);
            d.Klausuren.Add(k);
            Vertretungen.Aktualisieren(d, alt.Datum);
            if (k.Datum != alt.Datum) Vertretungen.Aktualisieren(d, k.Datum);
        });
    }

    private void Loeschen()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Klausur>().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Klausur(en) löschen?")) return;
        var ids = liste.Select(k => k.Id).ToHashSet();
        Aendern("Klausur löschen", AenderungsArt.Vertretung, d =>
        {
            d.Klausuren.RemoveAll(k => ids.Contains(k.Id));
            foreach (var t in liste.Select(k => k.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
        });
    }
}

/// <summary>Vormerkungen: beiseitegelegte bzw. noch zu verplanende Stunden. Ziehen in den tagesaktuellen Stundenplan verplant die Vormerkung.</summary>
public sealed class VormerkungenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };
    private readonly ToolStripButton _alle = new("Auch verplante") { CheckOnClick = true };

    public VormerkungenFenster()
    {
        Text = "Vormerkungen";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("zurueck");
        Size = new Size(900, 300);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Vormerkung", "neu", Neu, false));
        ts.Items.Add(Knopf("Am Planungsdatum verplanen…", "planen", Verplanen, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(_alle);
        ts.Items.Add(new ToolStripLabel("   Vormerkung mit der Maus in einen tagesaktuellen Stundenplan ziehen, um sie zu verplanen.") { ForeColor = Color.DimGray });
        _alle.CheckedChanged += (_, _) => Neuaufbau();
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (n, k, b) in new[] { ("Nr", "Nr.", 45), ("Q", "Herkunft", 85), ("S", "Std.", 38), ("O", "offen", 42), ("F", "Fach", 55), ("L", "Lehrer", 75), ("K", "Klassen", 85), ("R", "Raum", 55), ("U", "ursprünglich", 110), ("G", "gültig", 140), ("T", "Text", 200), ("V", "verplant am", 120) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["T"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Point start = Point.Empty;
        _g.MouseDown += (_, e) => start = e.Location;
        _g.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 6) return;
            var hit = _g.HitTest(start.X, start.Y);
            if (hit.RowIndex >= 0 && _g.Rows[hit.RowIndex].Tag is Vormerkung vm && vm.Offen > 0) _g.DoDragDrop("vormerkung:" + vm.Id, DragDropEffects.Move);
        };
        Controls.Add(_g);
        Controls.Add(ts);
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        foreach (var v in D.Vormerkungen.Where(v => _alle.Checked || v.Offen > 0).OrderBy(v => v.Datum ?? DateOnly.MaxValue))
        {
            var r = _g.Rows.Add(v.Id, v.Quelle switch { VormerkungsQuelle.Vertretung => "Vertretung", VormerkungsQuelle.Verdraengung => "Verdrängung", VormerkungsQuelle.Stundenplan => "Stundenplan", VormerkungsQuelle.Unterricht => "Unterricht", _ => "konkret" },
                v.Stunden, v.Offen, Ix.KFach(v.FachId), string.Join(",", v.LehrerIds.Select(Ix.KLehrer)), Ix.KKlassen(v.KlassenIds), Ix.KRaum(v.RaumId),
                v.Datum is DateOnly dd ? $"{Format.Datum(dd)} / {v.Stunde}" : "", v.GueltigVon.HasValue || v.GueltigBis.HasValue ? $"{(v.GueltigVon is DateOnly a ? Format.Datum(a) : "")} – {(v.GueltigBis is DateOnly b ? Format.Datum(b) : "")}" : "",
                v.Text, string.Join(", ", v.VerplantAm.Select(Format.Datum)));
            _g.Rows[r].Tag = v;
            if (v.Offen == 0) _g.Rows[r].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private void Neu()
    {
        using var f = new Dialog("Vormerkung", 460, 250);
        var y = 14;
        var fa = Ui.Zeile(f, "Fach:", ElementListe.Combo(ElementTyp.Fach, null), ref y, 120, 300);
        var le = Ui.Zeile(f, "Lehrer:", new ElementWahl(ElementTyp.Lehrer), ref y, 120, 300);
        var kl = Ui.Zeile(f, "Klassen:", new ElementWahl(ElementTyp.Klasse), ref y, 120, 300);
        var ra = Ui.Zeile(f, "Raum:", ElementListe.Combo(ElementTyp.Raum, null), ref y, 120, 300);
        var st = Ui.Zeile(f, "Stunden:", new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1 }, ref y, 120, 60);
        var tx = Ui.Zeile(f, "Text:", new TextBox(), ref y, 120, 300);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var vm = new Vormerkung { Quelle = VormerkungsQuelle.Konkret, FachId = ElementListe.Id(fa), LehrerIds = le.Ids, KlassenIds = kl.Ids, RaumId = ElementListe.Id(ra), Stunden = (int)st.Value, Text = tx.Text };
        Aendern("Vormerkung", AenderungsArt.Vertretung, d => { vm.Id = d.NeueId(); d.Vormerkungen.Add(vm); });
    }

    private void Verplanen()
    {
        if (_g.CurrentRow?.Tag is not Vormerkung vm) return;
        var s = Ui.Eingabe($"Vormerkung am {Format.Datum(AppZustand.Datum)} verplanen – Stunde:", "Vormerkung verplanen", "1");
        if (s is null || !int.TryParse(s, out var stunde)) return;
        Aendern("Vormerkung verplanen", AenderungsArt.Vertretung, d => Vertretungen.VormerkungVerplanen(d, d.Vormerkungen.First(x => x.Id == vm.Id), AppZustand.Datum, stunde, AppZustand.Benutzer));
    }

    private void Loeschen()
    {
        var liste = _g.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Vormerkung>().ToList();
        if (liste.Count == 0 || !Ui.Frage($"{liste.Count} Vormerkung(en) löschen?")) return;
        var ids = liste.Select(v => v.Id).ToHashSet();
        Aendern("Vormerkung löschen", AenderungsArt.Vertretung, d => d.Vormerkungen.RemoveAll(v => ids.Contains(v.Id)));
    }
}

/// <summary>Bereitschaften am Planungsdatum: wer hat zu welcher Stunde Bereitschaft, und ist sie bereits verplant?</summary>
public sealed class BereitschaftenFenster : MdiFenster
{
    private readonly DataGridView _g = new() { Dock = DockStyle.Fill };

    public BereitschaftenFenster()
    {
        Text = "Bereitschaften";
        HilfeThema = "Vertretungsplanung";
        Icon = SymbolIcon("lehrer");
        Size = new Size(520, 300);
        Positionieren();
        Ui.RasterGestalten(_g, false);
        foreach (var (n, k, b) in new[] { ("S", "Std.", 40), ("L", "Lehrer", 80), ("F", "Bereitschaft", 90), ("V", "eingesetzt für", 180), ("Z", "Zähler", 55) })
            _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["V"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Controls.Add(_g);
        AppZustand.DatumGewechselt += DatumGewechselt;
        FormClosed += (_, _) => AppZustand.DatumGewechselt -= DatumGewechselt;
        Neuaufbau();
    }

    protected override bool EigeneAenderungenAnzeigen => true;
    protected override void Aktualisieren(AenderungsArt art) => Neuaufbau();
    private void DatumGewechselt(DateOnly _) { if (!IsDisposed) Neuaufbau(); }

    private void Neuaufbau()
    {
        _g.Rows.Clear();
        var datum = AppZustand.Datum;
        Text = $"Bereitschaften – {Format.Datum(datum)}";
        var tp = Tagesplan.Berechnen(D, datum);
        var (zv, zb) = Zaehlung.Zeitraum(D, datum);
        foreach (var e in tp.Regel.Where(r => Vertretungen.IstBereitschaftsfach(D, r.FachId) && r.LehrerId.HasValue).OrderBy(r => r.Stunde).ThenBy(r => Ix.KLehrer(r.LehrerId)))
        {
            var l = e.LehrerId!.Value;
            var einsatz = D.Vertretungen.Where(v => v.Datum == datum && v.Stunde == e.Stunde && v.VertreterId == l && v.LehrerId != l).ToList();
            var abwesend = Absenzen.LehrerAbwesend(D, l, datum, e.Stunde);
            var r = _g.Rows.Add(e.Stunde, Ix.KLehrer(l), Ix.KFach(e.FachId),
                abwesend ? "(abwesend)" : string.Join("; ", einsatz.Select(v => $"{Ix.KKlassen(v.NeueKlassenIds)} {Ix.KFach(v.FachId)} ({Ix.KLehrer(v.LehrerId)})")),
                Ix.Lehrer.TryGetValue(l, out var le) ? Zaehlung.Lehrer(D, le, zv, zb).Summe.ToString("0.#") : "");
            _g.Rows[r].DefaultCellStyle.BackColor = abwesend ? Color.FromArgb(240, 240, 240) : einsatz.Count > 0 ? Color.FromArgb(255, 240, 210) : Color.FromArgb(220, 242, 220);
        }
        if (_g.Rows.Count == 0) _g.Rows.Add("", "", "", "Keine Bereitschaften an diesem Tag (Fach mit Kennzeichen \"Bereitschaft\" im Stundenplan).");
    }
}
