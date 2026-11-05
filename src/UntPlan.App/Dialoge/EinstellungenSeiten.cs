using System.Net;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Weitere Seiten der Einstellungen wie im Handbuch (7.4.3/7.4.4): Diverses | Dateiablage, Anpassen, Warnungen und Hinweise, HTML,
/// Internet sowie Berichte | Überschriften und Drucknamen. Kursiv = lokal gespeicherte Einstellungen.
/// </summary>
public sealed partial class EinstellungenDialog
{
    private void WeitereSeiten(TreeNode diverses)
    {
        // Reihenfolge wie im Handbuch: Sichern, Dateiablage, Stundenplan, Anpassen, Warnungen und Hinweise, HTML, Internet
        diverses.Nodes.Insert(1, "Dateiablage");
        diverses.Nodes.Add("Anpassen");
        diverses.Nodes.Add("Warnungen und Hinweise");
        diverses.Nodes.Add("HTML");
        diverses.Nodes.Add("Internet");
        var berichte = _baum.Nodes.Add("Berichte");
        berichte.Nodes.Add("Überschriften");
        berichte.Nodes.Add("Drucknamen");
        _seiten["Dateiablage"] = SeiteDateiablage;
        _seiten["Anpassen"] = SeiteAnpassen;
        _seiten["Warnungen und Hinweise"] = SeiteWarnungen;
        _seiten["Warnungen"] = SeiteWarnungen;
        _seiten["HTML"] = SeiteHtml;
        _seiten["Internet"] = SeiteInternet;
        _seiten["Berichte"] = SeiteUeberschriften;
        _seiten["Überschriften"] = SeiteUeberschriften;
        _seiten["Drucknamen"] = SeiteDrucknamen;
        _seiten["Sichern"] = () => { SeiteDiverses(); ZusatzSichern(); };
        _seiten["Diverses"] = _seiten["Sichern"];
        Controls.Add(new Label { Text = "Kursiv = lokal gespeicherte Einstellungen (.ini Dateien)", Left = 8, Top = ClientSize.Height - 30, AutoSize = true, Font = new Font(Ui.Schrift, FontStyle.Italic) });
    }

    private static Font Kursiv => new(Ui.Schrift, FontStyle.Italic);

    private static void Lokal(Action a)
    {
        a();
        AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Sichern: „Optimierte Pläne zusätzlich in Arbeitsdateien speichern“ (unter den vorhandenen Optionen).</summary>
    private void ZusatzSichern()
    {
        var y = _seite.Controls.Cast<Control>().Select(c => c.Bottom).DefaultIfEmpty(0).Max() + 8;
        var c = new CheckBox { Text = "Optimierte Pläne zusätzlich in Arbeitsdateien speichern", Left = 12, Top = y, AutoSize = true, Font = Kursiv, Checked = AppZustand.Einstellungen.OptimiertePlaeneSpeichern };
        c.CheckedChanged += (_, _) => Lokal(() => AppZustand.Einstellungen.OptimiertePlaeneSpeichern = c.Checked);
        _seite.Controls.Add(c);
    }

    /// <summary>Dateiablage: Tabelle „Dateiart | Pfad“ mit „Löschen“ und „Bearbeiten“.</summary>
    private void SeiteDateiablage()
    {
        var g = new DataGridView { Left = 8, Top = 4, Width = 540, Height = 300, Name = "Dateiablage" };
        Ui.RasterGestalten(g, false);
        g.ReadOnly = true;
        g.AllowUserToAddRows = false;
        g.RowHeadersVisible = false;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
        g.Columns.Add("Art", "Dateiart");
        g.Columns.Add("Pfad", "Pfad");
        g.Columns[0].Width = 170;
        g.Columns[0].DefaultCellStyle.Font = Kursiv;
        g.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        g.Columns[1].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 235);
        void Fuellen()
        {
            g.Rows.Clear();
            foreach (var art in Einstellungsseiten.Dateiarten) g.Rows.Add(art, AppZustand.Einstellungen.Dateiablage.GetValueOrDefault(art, ""));
        }
        Fuellen();
        var loeschen = new Button { Text = "Löschen", Left = 368, Top = 312, Width = 88, Height = 26 };
        var bearbeiten = new Button { Text = "Bearbeiten", Left = 460, Top = 312, Width = 88, Height = 26 };
        string? Art() => g.CurrentRow?.Cells[0].Value as string;
        loeschen.Click += (_, _) => { if (Art() is { } a) { Lokal(() => AppZustand.Einstellungen.Dateiablage.Remove(a)); Fuellen(); } };
        void Bearbeiten()
        {
            if (Art() is not { } a) return;
            using var fbd = new FolderBrowserDialog { Description = a, UseDescriptionForTitle = true, SelectedPath = Einstellungsseiten.Pfad(a) };
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            Lokal(() => AppZustand.Einstellungen.Dateiablage[a] = fbd.SelectedPath);
            var zeile = g.CurrentRow!.Index;
            Fuellen();
            g.CurrentCell = g.Rows[zeile].Cells[0];
        }
        bearbeiten.Click += (_, _) => Bearbeiten();
        g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); };
        _seite.Controls.AddRange(new Control[] { g, loeschen, bearbeiten });
    }

    /// <summary>
    /// Anpassen wie im Handbuch: Import/Export-Menü, Unterrichtsfenster, Hintergrundfarbe Hauptfenster, Symbolleisten, Breite der
    /// Elementcombobox; rechts Eingabe, Element-Rollup, Stammdaten/Auswahllisten immer sortieren. Wirksam: Hintergrundfarben,
    /// Autovervollständigen, Tooltips, Element-Rollup (Doppelklick, Elemente aktualisieren); ohne Funktion ausgegraut.
    /// </summary>
    private void SeiteAnpassen()
    {
        var e = AppZustand.Einstellungen;
        GroupBox Gruppe(string text, int x, int y, int b, int h)
        {
            var gb = new GroupBox { Text = text, Left = x, Top = y, Width = b, Height = h, Font = Ui.Schrift };
            _seite.Controls.Add(gb);
            return gb;
        }
        CheckBox Kb(Control p, string text, int y, bool wert, Action<bool>? setzen)
        {
            var c = new CheckBox { Text = text, Left = 8, Top = y, Width = p.Width - 14, Height = 20, Font = Kursiv, Checked = wert, Enabled = setzen != null };
            if (setzen != null) c.CheckedChanged += (_, _) => Lokal(() => setzen(c.Checked));
            p.Controls.Add(c);
            return c;
        }
        var imp = Gruppe("Import/Export-Menü", 4, 0, 270, 76);
        imp.Controls.Add(new RadioButton { Text = "Alle Menüpunkte", Left = 8, Top = 16, Width = 250, Font = Kursiv, Enabled = false });
        imp.Controls.Add(new RadioButton { Text = "Untermenü pro Land", Left = 8, Top = 34, Width = 250, Font = Kursiv, Checked = true, Enabled = false });
        imp.Controls.Add(new RadioButton { Text = "Menüpunkte für Land", Left = 8, Top = 52, Width = 250, Font = Kursiv, Enabled = false });
        var uf = Gruppe("Unterrichtsfenster", 4, 80, 270, 78);
        Kb(uf, "Klassenschülerzahlen aktualisieren", 16, false, null);
        Kb(uf, "Datumssynchronisation", 36, false, null);
        Kb(uf, "Räume und Text beim Zusammenlegen von Kopplungszeilen ignorieren", 56, false, null);
        var hf = Gruppe("Hintergrundfarbe Hauptfenster", 4, 162, 270, 66);
        void Farbe(string text, int y, Func<int> lesen, Action<int> setzen, Color standard)
        {
            var knopf = new Button { Left = 8, Top = y, Width = 70, Height = 22, Text = "Stan...", FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft };
            void Zeigen() { var v = lesen(); knopf.BackColor = v == 0 ? standard : Color.FromArgb(v); knopf.Text = v == 0 ? "Stan..." : ""; }
            Zeigen();
            knopf.Click += (_, _) =>
            {
                using var cd = new ColorDialog { Color = knopf.BackColor, FullOpen = true };
                var cm = new ContextMenuStrip { Font = Ui.Schrift };
                cm.Items.Add("Standard", null, (_, _) => { Lokal(() => setzen(0)); Zeigen(); Einstellungsseiten.HintergrundAnwenden(); });
                cm.Items.Add("Farbe wählen…", null, (_, _) =>
                {
                    if (cd.ShowDialog(this) != DialogResult.OK) return;
                    Lokal(() => setzen(cd.Color.ToArgb()));
                    Zeigen();
                    Einstellungsseiten.HintergrundAnwenden();
                });
                cm.Show(knopf, new Point(0, knopf.Height));
            };
            hf.Controls.Add(knopf);
            hf.Controls.Add(new Label { Text = text, Left = 84, Top = y + 3, AutoSize = true, Font = Kursiv });
        }
        Farbe("Sp-Modus", 16, () => e.HintergrundSp, v => e.HintergrundSp = v, Color.FromArgb(221, 239, 254));
        Farbe("Vtr-Modus", 40, () => e.HintergrundVtr, v => e.HintergrundVtr = v, Color.FromArgb(179, 228, 204));
        var sl = Gruppe("Symbolleisten", 4, 232, 270, 40);
        Kb(sl, "Symbolleisten abtrennbar", 16, false, null);
        _seite.Controls.Add(new NumericUpDown { Left = 8, Top = 280, Width = 50, Value = 80, Minimum = 40, Maximum = 300, Enabled = false });
        _seite.Controls.Add(new Label { Text = "Breite der Elementcombobox (Stammdaten, Unterricht)", Left = 64, Top = 280, Width = 210, Height = 30, Font = Kursiv });
        _seite.Controls.Add(new NumericUpDown { Left = 8, Top = 314, Width = 50, Value = 120, Minimum = 40, Maximum = 300, Enabled = false });
        _seite.Controls.Add(new Label { Text = "Breite der Abteilungscombobox", Left = 64, Top = 316, Width = 210, Font = Kursiv });

        var ein = Gruppe("Eingabe", 282, 0, 270, 136);
        Kb(ein, "Autovervollständigen", 16, e.Autovervollstaendigen, v => e.Autovervollstaendigen = v);
        Kb(ein, "Per Mausklick in den Editmodus", 36, true, null);
        Kb(ein, "Auswahlfelder für Stammdaten", 56, true, null);
        Kb(ein, "Tooltips mit Spaltenbeschreibungen einblenden", 76, e.SpaltenTooltips, v => e.SpaltenTooltips = v);
        Kb(ein, "Beim Sortieren Groß-/Kleinschreibung ignorieren", 96, true, null);
        Kb(ein, "Kalender für Datumseingabe verwenden", 114, true, null);
        var er = Gruppe("Element-Rollup", 282, 140, 270, 60);
        Kb(er, "Doppelklick aktivieren", 16, e.RollupDoppelklick, v => e.RollupDoppelklick = v);
        Kb(er, "Elemente aktualisieren", 36, e.RollupAktualisieren, v => e.RollupAktualisieren = v);
        var so = Gruppe("Stammdaten/Auswahllisten immer sortieren", 282, 204, 270, 112);
        var y = 16;
        foreach (var t in new[] { "Lehrer", "Fächer", "Klassen", "Räume", "Studenten" })
        {
            Kb(so, t, y, false, null).Width = 110;
            y += 18;
        }
        so.Controls.Add(new Label { Text = "...nach", Left = 130, Top = 16, AutoSize = true, Font = Kursiv });
        so.Controls.Add(new RadioButton { Text = "Kurzname", Left = 130, Top = 34, Width = 120, Font = Kursiv, Enabled = false });
        so.Controls.Add(new RadioButton { Text = "Langname/ Nachname", Left = 130, Top = 54, Width = 130, Height = 32, Font = Kursiv, Enabled = false });
    }

    /// <summary>Warnungen und Hinweise: Tabelle „Ein/Aus | Warnung bzw. Fehlermeldung“, darunter „Warnungen in der Statuszeile“.</summary>
    private void SeiteWarnungen()
    {
        var e = AppZustand.Einstellungen;
        var g = new DataGridView { Left = 8, Top = 4, Width = 540, Height = 290, Name = "Warnungen" };
        Ui.RasterGestalten(g);
        g.AllowUserToAddRows = false;
        g.RowHeadersVisible = false;
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Ein", HeaderText = "Ein/Aus", Width = 50 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Warnung bzw. Fehlermeldung", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        g.Columns[1].DefaultCellStyle.Font = Kursiv;
        foreach (var (key, text) in Einstellungsseiten.Warnungen)
        {
            var r = g.Rows.Add(!e.AusgeblendeteMeldungen.Contains(key), text);
            g.Rows[r].Tag = key;
        }
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        g.CellValueChanged += (_, a) =>
        {
            if (a.RowIndex < 0 || a.ColumnIndex != 0 || g.Rows[a.RowIndex].Tag is not string key) return;
            var ein = g.Rows[a.RowIndex].Cells[0].Value is true;
            Lokal(() => { e.AusgeblendeteMeldungen.Remove(key); if (!ein) e.AusgeblendeteMeldungen.Add(key); });
        };
        var st = new GroupBox { Text = "Warnungen in der Statuszeile", Left = 8, Top = 300, Width = 300, Height = 44 };
        var ton = new CheckBox { Text = "Ton ausschalten", Left = 8, Top = 18, AutoSize = true, Font = Kursiv, Checked = e.WarnungTonAus };
        ton.CheckedChanged += (_, _) => Lokal(() => e.WarnungTonAus = ton.Checked);
        st.Controls.Add(ton);
        st.Controls.Add(new CheckBox { Text = "Standardmeldung", Left = 150, Top = 18, AutoSize = true, Font = Kursiv, Enabled = false });
        _seite.Controls.AddRange(new Control[] { g, st });
    }

    /// <summary>HTML: „HTML-Navigation“ (Schaltflächen oben/unten, Encoding UTF-8), „Fußzeile“ und „HTML-Stundenpläne“ (Tag-Eigenschaften).</summary>
    private void SeiteHtml()
    {
        var h = _e.Html;
        var nav = new GroupBox { Text = "HTML-Navigation", Left = 4, Top = 0, Width = 170, Height = 78 };
        var oben = new CheckBox { Text = "Schaltflächen oben", Left = 8, Top = 16, AutoSize = true, Checked = h.SchaltflaechenOben };
        var unten = new CheckBox { Text = "Schaltflächen unten", Left = 8, Top = 36, AutoSize = true, Checked = h.SchaltflaechenUnten };
        var utf = new CheckBox { Text = "Encoding: UTF-8", Left = 8, Top = 56, AutoSize = true, Checked = h.Utf8 };
        oben.CheckedChanged += (_, _) => h.SchaltflaechenOben = oben.Checked;
        unten.CheckedChanged += (_, _) => h.SchaltflaechenUnten = unten.Checked;
        utf.CheckedChanged += (_, _) => h.Utf8 = utf.Checked;
        nav.Controls.AddRange(new Control[] { oben, unten, utf });
        var fuss = new GroupBox { Text = "Fußzeile", Left = 184, Top = 0, Width = 170, Height = 78 };
        var y = 16;
        foreach (var (text, wert) in new[] { ("keine", 0), ("Unter Plan/Tabelle", 1), ("Am Seitenende", 2) })
        {
            var rb = new RadioButton { Text = text, Left = 8, Top = y, AutoSize = true, Checked = h.Fusszeile == wert };
            var w = wert;
            rb.CheckedChanged += (_, _) => { if (rb.Checked) h.Fusszeile = w; };
            fuss.Controls.Add(rb);
            y += 20;
        }
        var stpl = new GroupBox { Text = "HTML-Stundenpläne", Left = 4, Top = 84, Width = 350, Height = 76 };
        var body = new TextBox { Left = 8, Top = 18, Width = 160, Text = h.BodyEigenschaften };
        var table = new TextBox { Left = 8, Top = 46, Width = 160, Text = h.TableEigenschaften };
        body.TextChanged += (_, _) => h.BodyEigenschaften = body.Text;
        table.TextChanged += (_, _) => h.TableEigenschaften = table.Text;
        stpl.Controls.AddRange(new Control[] { body, new Label { Text = "Eigenschaften für Tag <Body>", Left = 174, Top = 21, AutoSize = true },
            table, new Label { Text = "Eigenschaften für Tag <Table>", Left = 174, Top = 49, AutoSize = true } });
        _seite.Controls.AddRange(new Control[] { nav, fuss, stpl });
    }

    /// <summary>Internet: Proxyserver (Direktverbindung, Proxy aus Systemeinstellungen, Proxy (Host[:Port])), Benutzer/Passwort, Test, Abfragen.</summary>
    private void SeiteInternet()
    {
        var e = AppZustand.Einstellungen;
        var px = new GroupBox { Text = "Proxyserver", Left = 4, Top = 0, Width = 240, Height = 186 };
        var host = new TextBox { Left = 8, Top = 82, Width = 222, Text = e.ProxyHost, Enabled = e.ProxyArt == 2 };
        var y = 18;
        foreach (var (text, wert) in new[] { ("Direktverbindung", 0), ("Proxy aus Systemeinstellungen", 1), ("Proxy (Host[:Port])", 2) })
        {
            var rb = new RadioButton { Text = text, Left = 8, Top = y, AutoSize = true, Font = Kursiv, Checked = e.ProxyArt == wert };
            var w = wert;
            rb.CheckedChanged += (_, _) => { if (!rb.Checked) return; Lokal(() => e.ProxyArt = w); host.Enabled = w == 2; };
            px.Controls.Add(rb);
            y += 20;
        }
        host.TextChanged += (_, _) => Lokal(() => e.ProxyHost = host.Text.Trim());
        var ben = new TextBox { Left = 8, Top = 120, Width = 130, Text = e.ProxyBenutzer };
        ben.TextChanged += (_, _) => Lokal(() => e.ProxyBenutzer = ben.Text);
        var pw = new TextBox { Left = 8, Top = 150, Width = 130, UseSystemPasswordChar = true, Text = Einstellungsseiten.ProxyPasswort };
        pw.TextChanged += (_, _) => Einstellungsseiten.ProxyPasswort = pw.Text;
        px.Controls.AddRange(new Control[] { host, ben, new Label { Text = "Benutzer", Left = 146, Top = 123, AutoSize = true, Font = Kursiv },
            pw, new Label { Text = "Passwort", Left = 146, Top = 153, AutoSize = true, Font = Kursiv } });
        var test = new Button { Text = "Test", Left = 262, Top = 8, Width = 88, Height = 24 };
        test.Click += async (_, _) =>
        {
            test.Enabled = false;
            var (ok, text) = await Einstellungsseiten.VerbindungTesten();
            test.Enabled = true;
            if (ok) Ui.Info(text, "Hinweis"); else Ui.Info("Die Verbindung konnte nicht hergestellt werden:\n" + text, "Hinweis");
        };
        var upd = new CheckBox { Text = "Internetabfrage für Programmupdate", Left = 8, Top = 194, AutoSize = true, Font = Kursiv, Checked = e.UpdateAbfrage };
        upd.CheckedChanged += (_, _) => Lokal(() => e.UpdateAbfrage = upd.Checked);
        var msg = new CheckBox { Text = "Internetabfrage für Benutzer-Nachrichten", Left = 8, Top = 214, AutoSize = true, Font = Kursiv, Checked = e.NachrichtenAbfrage };
        msg.CheckedChanged += (_, _) => Lokal(() => e.NachrichtenAbfrage = msg.Checked);
        var auth = new GroupBox { Text = "HTTP Authentifizierung", Left = 4, Top = 238, Width = 240, Height = 76 };
        auth.Controls.AddRange(new Control[] { new TextBox { Left = 8, Top = 18, Width = 130 }, new Label { Text = "Benutzer", Left = 146, Top = 21, AutoSize = true, Font = Kursiv },
            new TextBox { Left = 8, Top = 46, Width = 130, UseSystemPasswordChar = true }, new Label { Text = "Passwort", Left = 146, Top = 49, AutoSize = true, Font = Kursiv } });
        _seite.Controls.AddRange(new Control[] { px, test, upd, msg, auth });
    }

    /// <summary>Berichte | Überschriften: „Überschrift“ (zwei Zeilen, Datum, Uhrzeit, Version, Linie, Stundenplan-Version, Seiten-Nummer) und „Fußzeile“.</summary>
    private void SeiteUeberschriften()
    {
        var b = _e.Berichte;
        var ue = new GroupBox { Text = "Überschrift", Left = 4, Top = 0, Width = 260, Height = 250 };
        var t1 = new TextBox { Left = 8, Top = 18, Width = 240, Text = b.Ueberschrift1, PlaceholderText = $"Stundenplan {_e.SchuljahrVon.Year}/{_e.SchuljahrVon.Year + 1}" };
        var t2 = new TextBox { Left = 8, Top = 48, Width = 240, Text = b.Ueberschrift2 };
        t1.TextChanged += (_, _) => b.Ueberschrift1 = t1.Text;
        t2.TextChanged += (_, _) => b.Ueberschrift2 = t2.Text;
        ue.Controls.AddRange(new Control[] { t1, t2 });
        var y = 78;
        CheckBox K(Control p, string text, ref int yy, bool wert, Action<bool> setzen, int x = 8)
        {
            var c = new CheckBox { Text = text, Left = x, Top = yy, AutoSize = true, Checked = wert };
            c.CheckedChanged += (_, _) => setzen(c.Checked);
            p.Controls.Add(c);
            yy += 20;
            return c;
        }
        K(ue, "Datum drucken", ref y, b.DatumDrucken, v => b.DatumDrucken = v);
        K(ue, "Uhrzeit drucken", ref y, b.UhrzeitDrucken, v => b.UhrzeitDrucken = v);
        K(ue, "Stundenplan-Version drucken", ref y, b.VersionDrucken, v => b.VersionDrucken = v);
        K(ue, "Waagrechte Linie drucken", ref y, b.WaagrechteLinie, v => b.WaagrechteLinie = v);
        y += 6;
        var ver = new NumericUpDown { Left = 8, Top = y, Width = 50, Minimum = 0, Maximum = 9999, Value = Math.Clamp(_e.StundenplanVersion, 0, 9999) };
        ver.ValueChanged += (_, _) => _e.StundenplanVersion = (int)ver.Value;
        ue.Controls.Add(ver);
        ue.Controls.Add(new Label { Text = "Stundenplan-Version", Left = 66, Top = y + 3, AutoSize = true });
        y += 34;
        CheckBox? anzahl = null;
        var seiten = K(ue, "Seiten-Nummer drucken", ref y, b.SeitenNummer, v => { b.SeitenNummer = v; if (anzahl != null) anzahl.Enabled = v; });
        anzahl = K(ue, "Anzahl Seiten drucken", ref y, b.AnzahlSeiten, v => b.AnzahlSeiten = v);
        anzahl.Enabled = seiten.Checked;
        var fz = new GroupBox { Text = "Fußzeile", Left = 274, Top = 0, Width = 270, Height = 200 };
        var ft = new TextBox { Left = 8, Top = 18, Width = 250, Text = b.Fusszeile, PlaceholderText = "UntPlan" };
        ft.TextChanged += (_, _) => b.Fusszeile = ft.Text;
        fz.Controls.Add(ft);
        y = 48;
        K(fz, "Dateiname", ref y, b.FussDateiname, v => b.FussDateiname = v);
        K(fz, "Datum", ref y, b.FussDatum, v => b.FussDatum = v);
        K(fz, "Schulwoche", ref y, b.FussSchulwoche, v => b.FussSchulwoche = v);
        K(fz, "Kalenderwoche", ref y, b.FussKalenderwoche, v => b.FussKalenderwoche = v);
        K(fz, "Wochennummer (A, B, ...)", ref y, b.FussWochennummer, v => b.FussWochennummer = v);
        K(fz, "Periode", ref y, b.FussPeriode, v => b.FussPeriode = v);
        K(fz, "Waagrechte Linie drucken", ref y, b.FussLinie, v => b.FussLinie = v);
        _seite.Controls.AddRange(new Control[] { ue, fz });
    }

    /// <summary>Berichte | Drucknamen: „Druckname für Lehrer“ und „Druckname für Studenten“.</summary>
    private void SeiteDrucknamen()
    {
        var b = _e.Berichte;
        _seite.Controls.Add(new Label { Text = "Druckname für Lehrer", Left = 8, Top = 8, AutoSize = true });
        var le = new ComboBox { Left = 8, Top = 26, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var a in Enum.GetValues<LehrerDruckname>()) le.Items.Add(Drucknamen.Text(a));
        le.SelectedIndex = (int)b.LehrerDruckname;
        le.SelectedIndexChanged += (_, _) => b.LehrerDruckname = (LehrerDruckname)le.SelectedIndex;
        _seite.Controls.Add(le);
        _seite.Controls.Add(new Label { Text = "Druckname für Studenten", Left = 8, Top = 60, AutoSize = true });
        var st = new ComboBox { Left = 8, Top = 78, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var a in Enum.GetValues<SchuelerDruckname>()) st.Items.Add(Drucknamen.Text(a));
        st.SelectedIndex = (int)b.SchuelerDruckname;
        st.SelectedIndexChanged += (_, _) => b.SchuelerDruckname = (SchuelerDruckname)st.SelectedIndex;
        _seite.Controls.Add(st);
        _seite.Controls.Add(new Label { Text = "Die Drucknamen ersetzen die Kurznamen der Lehrkräfte in Stundenplanausdrucken (Unterrichtsliste, Legende, Lehrer der Klasse).", Left = 8, Top = 116, Width = 520, Height = 36, ForeColor = Color.DimGray });
    }
}

/// <summary>Hilfen für die Einstellungsseiten: Dateiablage, Warnungen, Hintergrundfarbe, Internetverbindung.</summary>
public static class Einstellungsseiten
{
    /// <summary>Dateiarten der Dateiablage in der Reihenfolge des Handbuchs.</summary>
    public static readonly string[] Dateiarten = { "Datendateien (.up)", "Sicherungsdateien", "Arbeitsdateien", "Import/Export", "HTML-Dateien" };

    /// <summary>Abschaltbare Warnhinweise (Schlüssel wie in <see cref="Warnhinweis"/>).</summary>
    public static readonly (string Schluessel, string Text)[] Warnungen =
    {
        ("fixiert", "Unterricht ist fixiert. Unterricht trotzdem entplanen ?"),
        ("fach2x", "Fach 2x / Tag – Trotzdem verplanen ?"),
        ("raumNichtVerplant", "Raum konnte nicht verplant werden."),
    };

    /// <summary>Proxy-Passwort (nur für diese Sitzung, wird nicht gespeichert).</summary>
    public static string ProxyPasswort { get; set; } = "";

    /// <summary>Pfad einer Dateiart; ohne Eintrag ein Standardordner (Dokumente\UntPlan\…).</summary>
    public static string Pfad(string art)
    {
        var key = Dateiarten.FirstOrDefault(a => a.StartsWith(art, StringComparison.OrdinalIgnoreCase)) ?? art;
        if (AppZustand.Einstellungen.Dateiablage.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p)) return p;
        var basis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UntPlan");
        return key switch
        {
            "Sicherungsdateien" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan", "Wiederherstellung"),
            "Arbeitsdateien" => Path.Combine(basis, "Arbeitsdateien"),
            "HTML-Dateien" => Path.Combine(basis, "HTML"),
            _ => basis,
        };
    }

    /// <summary>Eingetragener Pfad einer Dateiart oder null (für Startordner von Dateidialogen).</summary>
    public static string? Eingetragen(string art)
    {
        var key = Dateiarten.FirstOrDefault(a => a.StartsWith(art, StringComparison.OrdinalIgnoreCase)) ?? art;
        return AppZustand.Einstellungen.Dateiablage.TryGetValue(key, out var p) && Directory.Exists(p) ? p : null;
    }

    /// <summary>Hintergrundfarbe des Hauptfensters je Modus (Einstellungen | Anpassen), sonst <paramref name="standard"/>.</summary>
    public static Color Hintergrund(bool vertretungsmodus, Color standard)
    {
        var v = vertretungsmodus ? AppZustand.Einstellungen.HintergrundVtr : AppZustand.Einstellungen.HintergrundSp;
        return v == 0 ? standard : Color.FromArgb(255, Color.FromArgb(v));
    }

    /// <summary>Geänderte Hintergrundfarbe sofort im Arbeitsbereich anzeigen.</summary>
    public static void HintergrundAnwenden()
    {
        if (AppZustand.Haupt is null) return;
        foreach (var mdi in AppZustand.Haupt.Controls.OfType<MdiClient>()) { mdi.BackColor = Ui.MdiHintergrund; mdi.Invalidate(); }
    }

    /// <summary>HTTP-Handler laut Proxy-Einstellungen (Direktverbindung, System, Host[:Port] mit Benutzer/Passwort).</summary>
    public static HttpClientHandler Handler()
    {
        var e = AppZustand.Einstellungen;
        var h = new HttpClientHandler();
        switch (e.ProxyArt)
        {
            case 0:
                h.UseProxy = false;
                break;
            case 2 when e.ProxyHost.Length > 0:
                var adresse = e.ProxyHost.Contains("://") ? e.ProxyHost : "http://" + e.ProxyHost;
                h.Proxy = new WebProxy(adresse) { Credentials = e.ProxyBenutzer.Length > 0 ? new NetworkCredential(e.ProxyBenutzer, ProxyPasswort) : null };
                h.UseProxy = true;
                break;
        }
        return h;
    }

    /// <summary>„Test“: erreicht das Programm mit den Einstellungen den Update-Server?</summary>
    public static async Task<(bool Ok, string Text)> VerbindungTesten()
    {
        try
        {
            using var http = new HttpClient(Handler()) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("UntPlan");
            using var antwort = await http.GetAsync(Updateanzeige.Releaseseite);
            return (true, new Uri(Updateanzeige.Releaseseite).Host);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
