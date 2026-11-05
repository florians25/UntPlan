using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Druck;

/// <summary>
/// Seitenlayout der Stundenpläne wie im Handbuch (6.6.2): links die Seitenansicht (Blättern, Papierformat) mit dem Stift-Symbol für die
/// Überschrift (Einzelpläne) und dem Zahnrad für die Einstellungen (Stundenpläne pro Seite, Für jede Woche ein Stundenplan, 1 Überschrift
/// je Blatt …), rechts die Druckeinstellungen – „Auswahl“ (Alle, Markierte, Inverse, Abteilung), „Stundenplangröße an Seitengröße
/// anpassen“, „Unterricht, …“, „Kopplungslegende (Fußnoten)“, „Abkürzungen“, „Lehrer der Klasse“ bzw. „Klassen des Lehrers“,
/// „Stundenplan Klassenlehrer“ bzw. „Stundenplan Klasse des Klassenlehrers“, „Element-Text“, „Hintergrund-Bild“ und „Bild verschieben“.
/// Die Druckdetails werden je Stundenplanformat gespeichert.
/// </summary>
public sealed class StundenplanSeitenlayout : Form
{
    private readonly ElementTyp _typ;
    private readonly StundenplanFormat _format;
    private readonly DateOnly? _woche;
    private List<int> _ids;
    private readonly PictureBox _bild = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, BackColor = Color.FromArgb(160, 170, 185) };
    private readonly ToolStripLabel _seitenText = new();
    private readonly Label _anzahl = new() { Left = 10, Top = 8, Width = 200, Height = 18 };
    private readonly Button _hintergrund = new() { Left = 10, Width = 210, Height = 40, TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText };
    private readonly CheckBox _verschieben = new() { Text = "Bild verschieben", Appearance = Appearance.Button, Left = 10, Width = 130, Height = 24, TextAlign = ContentAlignment.MiddleCenter };
    private List<Bitmap> _seiten = new();
    private int _seite;

    /// <summary>Druckoptionen dieses Seitenlayouts (bleiben für die Sitzung erhalten).</summary>
    internal static StundenplanDruckOptionen Optionen => StundenplanFormular.Optionen;

    private StundenplanDruckdetails Details => _format.Druck;

    public StundenplanSeitenlayout(ElementTyp typ, IEnumerable<int> ids, StundenplanFormat format, DateOnly? woche)
    {
        _typ = typ;
        _format = format;
        _woche = woche;
        _ids = ids.ToList();
        Text = "Seitenlayout";
        Font = Ui.Schrift;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var leiste = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        leiste.Items.Add(new ToolStripButton("◀") { ToolTipText = "Vorherige Seite" }.Mit(b => b.Click += (_, _) => Blaettern(-1)));
        leiste.Items.Add(new ToolStripButton("▶") { ToolTipText = "Nächste Seite" }.Mit(b => b.Click += (_, _) => Blaettern(1)));
        leiste.Items.Add(_seitenText);
        var papier = new ToolStripDropDownButton("Papier") { ToolTipText = "Papierformat: Hochformat oder Querformat" };
        papier.DropDownItems.Add("Hochformat", null, (_, _) => { StundenplanDruck.Seite.Landscape = false; Neu(); });
        papier.DropDownItems.Add("Querformat", null, (_, _) => { StundenplanDruck.Seite.Landscape = true; Neu(); });
        papier.DropDownItems.Add(new ToolStripSeparator());
        papier.DropDownItems.Add("Seite einrichten…", null, (_, _) => SeiteEinrichten());
        leiste.Items.Add(new ToolStripSeparator());
        leiste.Items.Add(papier);
        leiste.Items.Add(new ToolStripSeparator());
        leiste.Items.Add(new ToolStripButton("Drucken", Symbole.Get("drucken", 16)).Mit(b => b.Click += (_, _) => StundenplanDruck.Drucken(this, _typ, _ids, _format, false, _woche)));

        // Stift (Überschrift für den Druck von Einzelplänen) links oben und Zahnrad (Einstellungen) rechts oben auf der Seitenansicht
        var stift = new Button { Text = "✎", Width = 24, Height = 24, Left = 6, Top = 6, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
        var zahnrad = new Button { Text = "⚙", Width = 24, Height = 24, Top = 6, Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
        var tipp = new ToolTip();
        tipp.SetToolTip(stift, "Überschrift für den Druck von Einzelplänen");
        tipp.SetToolTip(zahnrad, "Einstellungen");
        stift.Click += (_, _) => { if (UeberschriftEditor.Zeigen(this, _format)) { Speichern(); Neu(); } };
        zahnrad.Click += (_, _) => { if (DetailsDialog(zahnrad)) { Speichern(); Neu(); } };
        _bild.Controls.Add(stift);
        _bild.Controls.Add(zahnrad);
        _bild.Resize += (_, _) => zahnrad.Left = _bild.ClientSize.Width - 30;
        _bild.MouseDown += BildMaus;
        _bild.MouseMove += BildMaus;
        _bild.MouseUp += (_, e) => { if (_verschieben.Checked && e.Button == MouseButtons.Left) { Speichern(); Neu(); } };

        var rechts = new Panel { Dock = DockStyle.Right, Width = 240, BackColor = Color.FromArgb(232, 240, 250), AutoScroll = true };
        var auswahl = new Button { Text = "Auswahl", Left = 10, Top = 28, Width = 210, Height = 26 };
        auswahl.Click += (_, _) => AuswahlWaehlen();
        rechts.Controls.AddRange(new Control[] { _anzahl, auswahl });
        var y = 66;
        CheckBox Option(string text, bool wert, Action<bool>? setzen, bool speichern = false)
        {
            var c = new CheckBox { Text = text, Left = 10, Top = y, Width = 220, Height = 34, Checked = wert, Enabled = setzen != null };
            if (setzen != null) c.CheckedChanged += (_, _) => { setzen(c.Checked); if (speichern) Speichern(); Neu(); };
            rechts.Controls.Add(c);
            y += 36;
            return c;
        }
        // das Formular wird immer auf die Seite eingepasst
        Option("Stundenplangröße an Seitengröße anpassen", true, null);
        Option("Unterricht, ...", Optionen.Unterricht, v => Optionen.Unterricht = v);
        Option("Kopplungslegende (Fußnoten)", Optionen.Kopplungslegende, v => Optionen.Kopplungslegende = v);
        Option("Abkürzungen", Optionen.Abkuerzungen, v => Optionen.Abkuerzungen = v);
        if (typ is ElementTyp.Klasse or ElementTyp.Lehrer)
        {
            Option(typ == ElementTyp.Lehrer ? "Klassen des Lehrers" : "Lehrer der Klasse", Details.ZusatzKlassenLehrer, v => Details.ZusatzKlassenLehrer = v, true);
            Option(typ == ElementTyp.Lehrer ? "Stundenplan Klasse des Klassenlehrers" : "Stundenplan Klassenlehrer", Details.StundenplanKlassenlehrer, v => Details.StundenplanKlassenlehrer = v, true);
        }
        Option("Element-Text", Optionen.ElementText, v => Optionen.ElementText = v);
        rechts.Controls.Add(new Label { Text = "Hintergrund-Bild", Left = 10, Top = y + 4, AutoSize = true });
        y += 24;
        _hintergrund.Top = y;
        _hintergrund.Image = Symbole.Get("bild", 32);
        _hintergrund.Click += (_, _) => HintergrundWaehlen();
        rechts.Controls.Add(_hintergrund);
        y += 48;
        _verschieben.Top = y;
        rechts.Controls.Add(_verschieben);
        HintergrundAnzeigen();

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var schliessen = new Button { Text = "Schließen", Width = 88, Height = 26, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.Cancel };
        unten.Controls.Add(schliessen);
        unten.Resize += (_, _) => schliessen.Location = new Point(unten.Width - 96, 7);
        CancelButton = schliessen;

        Controls.Add(_bild);
        Controls.Add(rechts);
        Controls.Add(unten);
        Controls.Add(leiste);
        FormClosed += (_, _) => { foreach (var b in _seiten) b.Dispose(); };
        Neu();
    }

    /// <summary>Selbsttest: Anzahl der Seiten der Vorschau.</summary>
    internal int Seitenzahl => _seiten.Count;

    /// <summary>Seitenlayout für die angegebenen Elemente öffnen.</summary>
    public static void Zeigen(IWin32Window owner, ElementTyp typ, IEnumerable<int> ids, StundenplanFormat format, DateOnly? woche)
    {
        using var f = new StundenplanSeitenlayout(typ, ids, format, woche);
        f.ShowDialog(owner);
    }

    private void Neu()
    {
        foreach (var b in _seiten) b.Dispose();
        Optionen.UeberschriftProzent = _format.UeberschriftSchriftProzent;
        Optionen.LegendeProzent = _format.LegendeSchriftProzent;
        _seiten = _ids.Count == 0 ? new List<Bitmap>() : StundenplanDruck.Bilder(_typ, _ids, _format, _woche);
        _seite = Math.Clamp(_seite, 0, Math.Max(0, _seiten.Count - 1));
        var alle = Stammdaten.Sortiert(AppZustand.D, _typ).Count();
        _anzahl.Text = $"{Ui.TypName(_typ, true)}: {_ids.Count}/{alle}";
        Anzeigen();
    }

    private void Anzeigen()
    {
        _bild.Image = _seiten.Count > 0 ? _seiten[_seite] : null;
        _seitenText.Text = _seiten.Count > 0 ? $"{_seite + 1}-{_seite + 1} / {_seiten.Count}" : "0 / 0";
    }

    private void Blaettern(int r)
    {
        if (_seiten.Count == 0) return;
        _seite = Math.Clamp(_seite + r, 0, _seiten.Count - 1);
        Anzeigen();
    }

    /// <summary>Druckdetails in das gespeicherte Format der Schuldatei übernehmen (gleichnamiges Format derselben Planart).</summary>
    private void Speichern()
    {
        var kopie = System.Text.Json.JsonSerializer.Deserialize<StundenplanDruckdetails>(System.Text.Json.JsonSerializer.Serialize(Details))!;
        var eineUeberschrift = _format.EineUeberschriftJeBlatt;
        var alias = _format.LupeLegendeAlias;
        if (AppZustand.D.Formate.FirstOrDefault(f => f.Typ == _format.Typ && f.Name == _format.Name) is not { } ziel) return;
        AppZustand.Doc.Aendern("Seitenlayout", AenderungsArt.Einstellungen, d =>
        {
            var f = d.Formate.First(x => x.Typ == _format.Typ && x.Name == _format.Name);
            f.Druck = ReferenceEquals(ziel, _format) ? Details : kopie;
            f.EineUeberschriftJeBlatt = eineUeberschrift;
            f.LupeLegendeAlias = alias;
        });
    }

    private void SeiteEinrichten()
    {
        using var dlg = new PageSetupDialog { PageSettings = StundenplanDruck.Seite, EnableMetric = true, AllowPrinter = true };
        try { if (dlg.ShowDialog(this) == DialogResult.OK) Neu(); }
        catch (System.Drawing.Printing.InvalidPrinterException ex) { Ui.Info("Kein Drucker eingerichtet – das Seitenlayout kann erst mit einem Drucker gewählt werden.\n\n" + ex.Message, "Seitenlayout"); }
    }

    private void AuswahlWaehlen()
    {
        if (ElementAuswahlDialog.Zeigen(this, _typ, _ids) is not { } neu) return;
        _ids = neu;
        _seite = 0;
        Neu();
    }

    // ------------------------------------------------------------------------------------------ Hintergrund-Bild

    private void HintergrundAnzeigen()
    {
        var pfad = Details.Hintergrundbild;
        _hintergrund.Text = string.IsNullOrWhiteSpace(pfad) ? "<nicht definiert>" : Path.GetFileName(pfad);
        _verschieben.Enabled = !string.IsNullOrWhiteSpace(pfad);
        if (!_verschieben.Enabled) _verschieben.Checked = false;
    }

    /// <summary>„Hintergrund-Bild“: Bild (.bmp, .gif, .jpg, .png) wählen; ein weiterer Klick entfernt das Bild wieder.</summary>
    private void HintergrundWaehlen()
    {
        if (!string.IsNullOrWhiteSpace(Details.Hintergrundbild))
        {
            Details.Hintergrundbild = "";
        }
        else
        {
            using var dlg = new OpenFileDialog { Filter = "Bilder (*.bmp;*.gif;*.jpg;*.png)|*.bmp;*.gif;*.jpg;*.jpeg;*.png|Alle Dateien|*.*", Title = "Hintergrund-Bild" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Details.Hintergrundbild = dlg.FileName;
        }
        HintergrundAnzeigen();
        Speichern();
        Neu();
    }

    /// <summary>„Bild verschieben“: das Bild mit gedrückter Maustaste an die gewünschte Stelle der Seite ziehen.</summary>
    private void BildMaus(object? sender, MouseEventArgs e)
    {
        if (!_verschieben.Checked || e.Button != MouseButtons.Left || _bild.Image is not { } img) return;
        // Lage der gezoomten Seite in der PictureBox
        var cw = _bild.ClientSize.Width;
        var ch = _bild.ClientSize.Height;
        var faktor = Math.Min(cw / (float)img.Width, ch / (float)img.Height);
        var ox = (cw - img.Width * faktor) / 2;
        var oy = (ch - img.Height * faktor) / 2;
        var px = (e.X - ox) / faktor;
        var py = (e.Y - oy) / faktor;
        // Druckbereich der Vorschau wie in StundenplanDruck.Bilder
        var r = new Rectangle(35, 40, img.Width - 70, img.Height - 80);
        Details.BildX = Math.Clamp((px - r.Left) / r.Width, 0f, 1f);
        Details.BildY = Math.Clamp((py - r.Top) / r.Height, 0f, 1f);
        _bild.Invalidate();
        using var g = _bild.CreateGraphics();
        using var stift = new Pen(Color.Red, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var b = r.Width * Details.BildBreite * faktor;
        g.DrawRectangle(stift, e.X, e.Y, b, b * 0.6f);
    }

    // ------------------------------------------------------------------------------------------ Einstellungen (Zahnrad)

    /// <summary>
    /// Einstellungen des Seitenlayouts: bei Einzelplänen (Formate 1, 10) „Stundenpläne pro Seite“, „Für jede Woche ein Stundenplan“
    /// (Je Element neue Seite, Anzahl Wochen, Schulwoche), „1 Überschrift je Blatt“, „Für Lupe / Legende Alias verwenden“; bei
    /// Übersichtsplänen (Formate 11, 20, 30) „Anzahl Zeilen / Spalten pro Blatt“.
    /// </summary>
    private bool DetailsDialog(Control anker)
    {
        var uebersicht = _format.Grundform is Grundform.Format11 or Grundform.Format20 or Grundform.Format21 or Grundform.Format30;
        using var f = uebersicht ? UebersichtDruck.DetailsForm(Details) : EinzelDetailsForm(Details, _format);
        f.StartPosition = FormStartPosition.Manual;
        var p = anker.PointToScreen(new Point(anker.Width - f.Width, anker.Height));
        f.Location = new Point(Math.Max(0, p.X), p.Y);
        return f.ShowDialog(this) == DialogResult.OK;
    }

    internal static Form EinzelDetailsForm(StundenplanDruckdetails det, StundenplanFormat format)
    {
        var f = new Dialog("Einstellungen", 250, 330) { FormBorderStyle = FormBorderStyle.FixedToolWindow };
        f.Abbrechen.Visible = false;
        f.Ok.Text = "Ok";
        f.Ok.Location = new Point(10, 296);
        var g1 = new GroupBox { Text = "Stundenpläne pro Seite", Left = 6, Top = 4, Width = 238, Height = 74 };
        var neben = new NumericUpDown { Left = 8, Top = 18, Width = 40, Minimum = 1, Maximum = 6, Value = Math.Clamp(det.PlaeneNebeneinander, 1, 6) };
        var ueber = new NumericUpDown { Left = 8, Top = 44, Width = 40, Minimum = 1, Maximum = 6, Value = Math.Clamp(det.PlaeneUebereinander, 1, 6) };
        g1.Controls.AddRange(new Control[] { neben, new Label { Text = "Anzahl Pläne nebeneinander", Left = 54, Top = 21, AutoSize = true },
            ueber, new Label { Text = "Anzahl Pläne übereinander", Left = 54, Top = 47, AutoSize = true } });
        var g2 = new GroupBox { Text = "Für jede Woche ein Stundenplan", Left = 6, Top = 82, Width = 238, Height = 140 };
        var jede = new CheckBox { Text = "Für jede Woche ein Stundenplan", Left = 8, Top = 18, AutoSize = true, Checked = det.JedeWocheEinPlan };
        var neueSeite = new CheckBox { Text = "Je Element neue Seite", Left = 8, Top = 38, AutoSize = true, Checked = det.JeElementNeueSeite };
        var anzahl = new NumericUpDown { Left = 8, Top = 62, Width = 40, Minimum = 0, Maximum = 52, Value = Math.Clamp(det.AnzahlWochen, 0, 52) };
        var schulwoche = new TextBox { Left = 8, Top = 108, Width = 220, Text = det.Schulwoche };
        g2.Controls.AddRange(new Control[] { jede, neueSeite, anzahl, new Label { Text = "Anzahl Wochen", Left = 54, Top = 65, AutoSize = true },
            new Label { Text = "Schulwoche", Left = 8, Top = 90, AutoSize = true }, schulwoche });
        var eine = new CheckBox { Text = "1 Überschrift je Blatt", Left = 10, Top = 230, AutoSize = true, Checked = format.EineUeberschriftJeBlatt };
        var alias = new CheckBox { Text = "Für Lupe / Legende Alias verwenden", Left = 10, Top = 252, Width = 230, Height = 34, Checked = format.LupeLegendeAlias };
        f.Controls.AddRange(new Control[] { g1, g2, eine, alias });
        f.Ok.Click += (_, _) =>
        {
            det.PlaeneNebeneinander = (int)neben.Value;
            det.PlaeneUebereinander = (int)ueber.Value;
            det.JedeWocheEinPlan = jede.Checked;
            det.JeElementNeueSeite = neueSeite.Checked;
            det.AnzahlWochen = (int)anzahl.Value;
            det.Schulwoche = schulwoche.Text.Trim();
            format.EineUeberschriftJeBlatt = eine.Checked;
            format.LupeLegendeAlias = alias.Checked;
        };
        return f;
    }
}

/// <summary>
/// Überschrift für den Druck von Einzelplänen (Formate 01, 10, 11; Handbuch 6.6.2.2): Ok, Abbrechen, Übernahme, „Neues Feld“, „Feld ändern“,
/// „Feld löschen“; rechts die Darstellung des gewählten Feldes (Stellen maximal, Schriftgröße in Prozent (50-500%), Fett, Kursiv,
/// neue Zeile); unten die Felder der Überschrift (Standard: Kurz- und Langname). Neue Felder: Text, Beschreibung, Abteilung,
/// Klassenlehrer, Stammraum, Zeitbereich, Periode, Fixer (konstanter) Text.
/// </summary>
public static class UeberschriftEditor
{
    public static bool Zeigen(IWin32Window owner, StundenplanFormat format)
    {
        var felder = format.Druck.Ueberschrift.Count > 0
            ? format.Druck.Ueberschrift.Select(Kopie).ToList()
            : UeberschriftFeld.Standard();
        var nr = (int)format.Grundform;
        using var d = new Dialog($"Format {nr:00}  Stundenplan-Stunde  Überschrift für den Druck von Einzelplänen (Format 01, 10, 11)", 640, 260);
        d.Ok.Text = "Ok";
        d.Ok.Location = new Point(8, 8);
        d.Abbrechen.Location = new Point(8, 36);
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        var uebernahme = new Button { Text = "Übernahme", Left = 8, Top = 64, Width = 88, Height = 26 };
        var neu = new Button { Text = "Neues Feld", Left = 104, Top = 8, Width = 96, Height = 26 };
        var aendern = new Button { Text = "Feld ändern", Left = 104, Top = 36, Width = 96, Height = 26 };
        var loeschen = new Button { Text = "Feld löschen", Left = 104, Top = 64, Width = 96, Height = 26 };
        var info = new Label { Left = 230, Top = 8, Width = 250, AutoSize = false, Height = 18 };
        var stellen = new NumericUpDown { Left = 230, Top = 30, Width = 50, Minimum = 0, Maximum = 99 };
        var prozent = new NumericUpDown { Left = 230, Top = 58, Width = 50, Minimum = 50, Maximum = 500, Increment = 10 };
        var fett = new CheckBox { Text = "Fett", Left = 470, Top = 30, AutoSize = true };
        var kursiv = new CheckBox { Text = "Kursiv", Left = 470, Top = 52, AutoSize = true };
        var zeile = new CheckBox { Text = "Neue Zeile", Left = 470, Top = 74, AutoSize = true };
        var text = new TextBox { Left = 230, Top = 86, Width = 230, PlaceholderText = "Fixer (konstanter) Text" };
        d.Controls.AddRange(new Control[] { uebernahme, neu, aendern, loeschen, info, stellen, new Label { Text = "Stellen maximal", Left = 286, Top = 33, AutoSize = true },
            prozent, new Label { Text = "% Schriftgröße in Prozent (50-500%)", Left = 286, Top = 61, AutoSize = true }, fett, kursiv, zeile, text });
        var vorschau = new FlowLayoutPanel { Left = 8, Top = 120, Width = 624, Height = 96, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, WrapContents = true, AutoScroll = true };
        d.Controls.Add(vorschau);
        var gewaehlt = felder.Count > 0 ? 0 : -1;
        var laedt = false;
        void Vorschau()
        {
            vorschau.Controls.Clear();
            for (var i = 0; i < felder.Count; i++)
            {
                var f = felder[i];
                var muster = f.Art switch
                {
                    UeberschriftFeldArt.Kurzname => "Kurz.",
                    UeberschriftFeldArt.Langname => "Langname-xxxxxxxxxx",
                    UeberschriftFeldArt.FixerText => f.Text.Length > 0 ? f.Text : "Text",
                    _ => UeberschriftFeld.ArtText(f.Art),
                };
                if (f.StellenMax > 0 && muster.Length > f.StellenMax) muster = muster[..f.StellenMax];
                var lbl = new Label
                {
                    Text = muster, AutoSize = true, Margin = new Padding(2), Padding = new Padding(2), Cursor = Cursors.Hand,
                    Font = new Font("Arial", Math.Max(6f, 10f * f.SchriftProzent / 100f), (f.Fett ? FontStyle.Bold : 0) | (f.Kursiv ? FontStyle.Italic : 0)),
                    BackColor = i == gewaehlt ? Color.FromArgb(200, 240, 250) : Color.White, BorderStyle = i == gewaehlt ? BorderStyle.FixedSingle : BorderStyle.None,
                };
                vorschau.SetFlowBreak(lbl, i + 1 < felder.Count && felder[i + 1].NeueZeile);
                var idx = i;
                lbl.Click += (_, _) => { gewaehlt = idx; Anzeigen(); Vorschau(); };
                vorschau.Controls.Add(lbl);
            }
        }
        void Anzeigen()
        {
            laedt = true;
            var ok = gewaehlt >= 0 && gewaehlt < felder.Count;
            foreach (var c in new Control[] { stellen, prozent, fett, kursiv, zeile, text, aendern, loeschen }) c.Enabled = ok;
            if (ok)
            {
                var f = felder[gewaehlt];
                info.Text = $"{UeberschriftFeld.ArtText(f.Art)}: Arial {12f * f.SchriftProzent / 100f:0.#}";
                stellen.Value = Math.Clamp(f.StellenMax, 0, 99);
                prozent.Value = Math.Clamp(f.SchriftProzent, 50, 500);
                fett.Checked = f.Fett;
                kursiv.Checked = f.Kursiv;
                zeile.Checked = f.NeueZeile;
                text.Text = f.Text;
                text.Enabled = f.Art == UeberschriftFeldArt.FixerText;
            }
            else info.Text = "";
            laedt = false;
        }
        void Uebernehmen()
        {
            if (laedt || gewaehlt < 0 || gewaehlt >= felder.Count) return;
            var f = felder[gewaehlt];
            f.StellenMax = (int)stellen.Value;
            f.SchriftProzent = (int)prozent.Value;
            f.Fett = fett.Checked;
            f.Kursiv = kursiv.Checked;
            f.NeueZeile = zeile.Checked;
            f.Text = text.Text;
            Anzeigen();
            Vorschau();
        }
        foreach (var c in new Control[] { stellen, prozent }) ((NumericUpDown)c).ValueChanged += (_, _) => Uebernehmen();
        foreach (var c in new[] { fett, kursiv, zeile }) c.CheckedChanged += (_, _) => Uebernehmen();
        text.TextChanged += (_, _) => Uebernehmen();
        aendern.Click += (_, _) => { if (gewaehlt >= 0) { stellen.Focus(); stellen.Select(0, 3); } };
        loeschen.Click += (_, _) =>
        {
            if (gewaehlt < 0 || gewaehlt >= felder.Count) return;
            felder.RemoveAt(gewaehlt);
            gewaehlt = Math.Min(gewaehlt, felder.Count - 1);
            Anzeigen();
            Vorschau();
        };
        neu.Click += (_, _) =>
        {
            var cm = new ContextMenuStrip { Font = Ui.Schrift };
            foreach (var art in Enum.GetValues<UeberschriftFeldArt>())
            {
                var a = art;
                cm.Items.Add(UeberschriftFeld.ArtText(a), null, (_, _) =>
                {
                    felder.Add(new UeberschriftFeld { Art = a, StellenMax = a is UeberschriftFeldArt.Kurzname ? 8 : 20, SchriftProzent = 100 });
                    gewaehlt = felder.Count - 1;
                    Anzeigen();
                    Vorschau();
                });
            }
            cm.Show(neu, new Point(0, neu.Height));
        };
        void Speichern() => format.Druck.Ueberschrift = felder.Select(Kopie).ToList();
        var uebernommen = false;
        uebernahme.Click += (_, _) => { Speichern(); uebernommen = true; };
        Anzeigen();
        Vorschau();
        if (d.ShowDialog(owner) == DialogResult.OK) { Speichern(); return true; }
        return uebernommen;
    }

    private static UeberschriftFeld Kopie(UeberschriftFeld f) => new()
    {
        Art = f.Art, StellenMax = f.StellenMax, SchriftProzent = f.SchriftProzent, Fett = f.Fett, Kursiv = f.Kursiv, Text = f.Text, NeueZeile = f.NeueZeile,
    };
}

/// <summary>
/// Auswahl der zu druckenden Stundenpläne wie im Handbuch (6.6.2.3): Liste Name/Langname (Mehrfachauswahl mit gedrückter Maustaste bzw.
/// Strg+Klick), „Elementfilter“, Schaltflächen Alle, Markierte (Kennzeichen „Markiert“ der Stammdaten) und Inverse; bei Lehrkräften
/// zusätzlich die Einschränkung auf eine Abteilung; Ok/Abbrechen.
/// </summary>
public static class ElementAuswahlDialog
{
    public static List<int>? Zeigen(IWin32Window owner, ElementTyp typ, IEnumerable<int> vorauswahl)
    {
        using var d = new Dialog(Ui.TypName(typ, true), 300, 450);
        var liste = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, Left = 8, Top = 8, Width = 284, Height = 300, GridLines = true };
        liste.Columns.Add("Name", 90);
        liste.Columns.Add("Langname", 170);
        var vor = vorauswahl.ToHashSet();
        var filter = new TextBox { Left = 8, Top = 316, Width = 120 };
        var abteilung = new ComboBox { Left = 8, Top = 376, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, Visible = typ == ElementTyp.Lehrer };
        abteilung.Items.Add("<alle>");
        foreach (var a in AppZustand.D.Abteilungen.OrderBy(a => a.Kurzname)) abteilung.Items.Add(a.Kurzname);
        abteilung.SelectedIndex = 0;
        bool AbteilungPasst(Element e) => abteilung.SelectedIndex <= 0 || e.AbteilungId is int id && AppZustand.Doc.Index.K(ElementTyp.Abteilung, id) == (string)abteilung.SelectedItem!;
        void Fuellen()
        {
            var gewaehlt = liste.Items.Count == 0 ? vor : liste.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag!).ToHashSet();
            liste.BeginUpdate();
            liste.Items.Clear();
            foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ))
            {
                if (filter.Text.Length > 0 && !e.Kurzname.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase) && !e.Langname.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                if (!AbteilungPasst(e)) continue;
                var it = new ListViewItem(new[] { e.Kurzname, e is Lehrer l ? l.VollerName : e.Langname }) { Tag = e.Id, Selected = gewaehlt.Contains(e.Id) };
                liste.Items.Add(it);
            }
            liste.EndUpdate();
        }
        Fuellen();
        filter.TextChanged += (_, _) => Fuellen();
        abteilung.SelectedIndexChanged += (_, _) => Fuellen();
        var alle = new Button { Text = "Alle", Left = 8, Top = 344, Width = 88 };
        var markierte = new Button { Text = "Markierte", Left = 102, Top = 344, Width = 88 };
        var inverse = new Button { Text = "Inverse", Left = 196, Top = 344, Width = 88 };
        alle.Click += (_, _) => { foreach (ListViewItem i in liste.Items) i.Selected = true; liste.Focus(); };
        markierte.Click += (_, _) =>
        {
            var ix = AppZustand.Doc.Index;
            foreach (ListViewItem i in liste.Items) i.Selected = ix.Element(typ, (int)i.Tag!)?.Markiert == true;
            liste.Focus();
        };
        inverse.Click += (_, _) => { foreach (ListViewItem i in liste.Items) i.Selected = !i.Selected; liste.Focus(); };
        d.Controls.AddRange(new Control[] { liste, filter, new Label { Text = "Elementfilter", Left = 134, Top = 319, AutoSize = true }, alle, markierte, inverse, abteilung });
        if (typ == ElementTyp.Lehrer) d.Controls.Add(new Label { Text = "Abteilung", Left = 134, Top = 379, AutoSize = true });
        d.Ok.Text = "Ok";
        return d.ShowDialog(owner) == DialogResult.OK ? liste.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag!).ToList() : null;
    }
}

internal static class ToolStripErweiterung
{
    public static T Mit<T>(this T item, Action<T> a) where T : ToolStripItem
    {
        a(item);
        return item;
    }
}
