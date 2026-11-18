using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// „Sondereinsatz / Stunde ändern“ aus dem tagesaktuellen Stundenplan: vorausgefüllt mit der gewählten Stunde, je
/// Lehrer ein Reiter. Anwenden auf Unterrichtsstunde oder Stundenblock; Klasse (Schaltfläche „Klassen: n“), Lehrer
/// (Nur verfügbare Lehrer), Fach (Nur Fächer des Lehrers), Raum (Nur freie Räume), Schülergruppe und Text.
/// „Neuer Lehrer“ fügt eine weitere Lehrkraft als Sondereinsatz hinzu.
/// </summary>
public sealed class StundeAendernDialog : Dialog
{
    private sealed class Seite
    {
        public int? UnterrichtId;
        public int Zeile;
        public List<int> Klassen = new();
        public int? Lehrer, Fach, Raum;
        public string Gruppe = "", Text = "";
        public Button KlassenKnopf = null!;
        public Label KlassenText = null!;
        public ComboBox LehrerCb = null!, FachCb = null!, RaumCb = null!, GruppeCb = null!;
        public CheckBox NurVerfuegbar = null!, NurFaecher = null!, NurFrei = null!;
        public TextBox TextTb = null!;
    }

    private readonly DateOnly _datum;
    private readonly int _stunde;
    // Maße wie im Handbuch-Screenshot (auf 96 dpi umgerechnet): Fenster 471 × 458, Reiter oben, Schaltflächen unten rechts
    private readonly TabControl _tabs = new() { Left = 3, Top = 0, Width = 466, Height = 412, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly RadioButton _einzeln = new() { Text = "Unterrichtsstunde", Left = 8, Top = 18, Width = 150, Height = 17, Checked = true };
    private readonly RadioButton _block = new() { Text = "Stundenblock", Left = 8, Top = 36, Width = 150, Height = 17 };
    private readonly List<Seite> _seiten = new();

    private static Schuldatei D => AppZustand.D;

    /// <summary>Dialog zeigen und Änderungen ausführen; true, wenn etwas geändert wurde.</summary>
    public static bool Zeigen(IWin32Window owner, DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege)
    {
        using var f = new StundeAendernDialog(datum, stunde, eintraege);
        return f.ShowDialog(owner) == DialogResult.OK && f.Ausfuehren(owner);
    }

    /// <summary>
    /// „Sondereinsatz / Stunde ändern“ in einer freien Stunde (wie im Handbuch „Sondereinsatz: 15.11. / 5“, Reiter „?.“): Klasse bzw.
    /// Lehrer laut Stundenplan vorausgewählt; legt einen Sondereinsatz an.
    /// </summary>
    public static bool Neu(IWin32Window owner, DateOnly datum, int stunde, IReadOnlyList<int> klassen, IReadOnlyList<int> lehrer)
    {
        using var f = new StundeAendernDialog(datum, stunde, Array.Empty<TagesEintrag>());
        f.Text = $"Sondereinsatz:  {datum.Day}.{datum.Month}. / {stunde}";
        f.SeiteHinzufuegen(new Seite { Klassen = klassen.ToList(), Lehrer = lehrer.Count > 0 ? lehrer[0] : null }, "?.");
        return f.ShowDialog(owner) == DialogResult.OK && f.Ausfuehren(owner);
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege)
    {
        var f = new StundeAendernDialog(datum, stunde, eintraege);
        f.Show(owner);
        return f;
    }

    private StundeAendernDialog(DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege) : base($"Stunde ändern:  {datum.Day}.{datum.Month}. / {stunde}", 471, 458)
    {
        Font = VorbildMasse.Schrift;
        Ok.Size = Abbrechen.Size = VorbildMasse.Knopf;
        Ok.Location = new Point(308, 419);
        Abbrechen.Location = new Point(389, 419);
        // Reiter in der Dialogfarbe (wie im Vorbild, nicht weiß)
        Load += (_, _) => { foreach (TabPage p in _tabs.TabPages) p.BackColor = BackColor; };
        _datum = datum;
        _stunde = stunde;
        Controls.Add(_tabs);
        foreach (var e in eintraege)
        {
            var s = new Seite
            {
                UnterrichtId = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId, Zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0, Klassen = e.KlassenIds.ToList(),
                Lehrer = e.LehrerIds.Count > 0 ? e.LehrerIds[0] : null, Fach = e.FachId, Raum = e.RaumIds.Count > 0 ? e.RaumIds[0] : null,
                Gruppe = e.Vertretung?.Schuelergruppe is { Length: > 0 } g ? g : e.Regel is { } rg ? VeranstaltungsLogik.Schuelergruppe(rg) : e.Unterricht?.Schuelergruppe ?? "", Text = e.Vertretung?.Text ?? "",
            };
            SeiteHinzufuegen(s);
        }
        var neu = new Button { Text = "Neuer Lehrer", Left = 228, Top = 419, Width = 78, Height = 23, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        neu.Click += (_, _) =>
        {
            var vorlage = _seiten.FirstOrDefault();
            SeiteHinzufuegen(new Seite { UnterrichtId = null, Klassen = vorlage?.Klassen.ToList() ?? new(), Fach = vorlage is null ? null : Wert(vorlage.FachCb, D.Faecher), Raum = null, Gruppe = vorlage?.GruppeCb.Text ?? "" });
            _tabs.SelectedIndex = _tabs.TabCount - 1;
        };
        Controls.Add(neu);
    }

    private List<int> Stunden(Seite s)
    {
        // ein neuer Lehrer gilt für dieselben Stunden wie der erste Reiter
        if (s.UnterrichtId is null && _seiten.Count > 0 && _seiten[0] != s) return Stunden(_seiten[0]);
        return _block.Checked ? Raumvertretungen.Block(D, _datum, _stunde, s.UnterrichtId, s.Zeile) : new List<int> { _stunde };
    }

    private void SeiteHinzufuegen(Seite s, string? titel = null)
    {
        var ix = AppZustand.Doc.Index;
        var seite = new TabPage(titel ?? (s.UnterrichtId is null ? "Neuer Lehrer" : $"{ix.KLehrer(s.Lehrer)}, {ix.KFach(s.Fach)}".Trim(' ', ',')));
        seite.UseVisualStyleBackColor = false;
        seite.BackColor = BackColor;
        // Positionen in Reiter-Koordinaten wie im Handbuch: Gruppe „Anwenden auf“, darunter Beschriftung über dem Feld,
        // Auswahlfelder 142 breit, Häkchen rechts daneben auf Feldmitte, „Text“ über die ganze Breite
        var anwenden = new GroupBox { Text = "Anwenden auf", Left = 10, Top = 14, Width = 240, Height = 58 };
        if (_seiten.Count == 0) anwenden.Controls.AddRange(new Control[] { _einzeln, _block });
        else anwenden.Controls.Add(new Label { Text = "(wie erster Reiter)", Left = 8, Top = 27, AutoSize = true, ForeColor = Color.DimGray });
        seite.Controls.Add(anwenden);
        seite.Controls.Add(new Label { Text = "Klasse", Left = 10, Top = 82, AutoSize = true });
        s.KlassenKnopf = new Button { Left = 10, Top = 100, Width = 101, Height = 24, UseVisualStyleBackColor = true };
        s.KlassenText = new Label { Left = 123, Top = 105, Width = 220, AutoEllipsis = true };
        seite.Controls.AddRange(new Control[] { s.KlassenKnopf, s.KlassenText });
        ComboBox Feld(string titel, int y, string option, bool haken, out CheckBox cb)
        {
            seite.Controls.Add(new Label { Text = titel, Left = 10, Top = y - 17, AutoSize = true });
            // Vorbild: editierbares Auswahlfeld (weißes Textfeld mit Pfeil), Kürzel eintippen oder aus der Liste wählen
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Left = 10, Top = y, Width = 142, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            c.Format += (_, ev) => ev.Value = ev.ListItem is Element el ? el.Kurzname : "";
            cb = new CheckBox { Text = option, Left = 170, Top = y + 2, Height = 17, AutoSize = true, Checked = haken };
            seite.Controls.Add(c);
            seite.Controls.Add(cb);
            return c;
        }
        s.LehrerCb = Feld("Lehrer", 145, "Nur verfügbare Lehrer", true, out s.NurVerfuegbar);
        s.FachCb = Feld("Fach", 191, "Nur Fächer des Lehrers", true, out s.NurFaecher);
        s.RaumCb = Feld("Raum", 236, "Nur freie Räume", true, out s.NurFrei);
        seite.Controls.Add(new Label { Text = "Schülergruppe", Left = 10, Top = 267, AutoSize = true });
        s.GruppeCb = new ComboBox { Left = 10, Top = 285, Width = 142, Text = s.Gruppe, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        // Auswahl: Schülergruppen der Unterrichte dieser Klasse(n) zuerst (passendes Fach vorn), dann alle übrigen; eine neue wird eingetippt.
        // Fach + Schülergruppe eines anderen Unterrichts der Klasse = zusätzliche Stunde dieses Unterrichts, sonst Sondereinsatz
        s.GruppeCb.Items.AddRange(Schuelergruppen.Auswahl(D, s.Klassen, s.Fach).Cast<object>().ToArray());
        s.GruppeCb.Text = s.Gruppe;
        s.FachCb.SelectedIndexChanged += (_, _) =>
        {
            var text = s.GruppeCb.Text;
            s.GruppeCb.Items.Clear();
            s.GruppeCb.Items.AddRange(Schuelergruppen.Auswahl(D, s.Klassen, Wert(s.FachCb, D.Faecher)).Cast<object>().ToArray());
            s.GruppeCb.Text = text;
        };
        seite.Controls.Add(s.GruppeCb);
        seite.Controls.Add(new Label { Text = "Text", Left = 10, Top = 332, AutoSize = true });
        s.TextTb = new TextBox { Left = 10, Top = 351, Width = 328, Text = s.Text };
        Steuerelemente.Textverlauf.Anbinden(s.TextTb, "Stunde ändern:Text");
        seite.Controls.Add(s.TextTb);
        _tabs.TabPages.Add(seite);
        _seiten.Add(s);

        void Klassen()
        {
            s.KlassenKnopf.Text = $"Klassen: {s.Klassen.Count}";
            s.KlassenText.Text = AppZustand.Doc.Index.KKlassen(s.Klassen);
        }
        var fuellt = false;
        void Listen()
        {
            // Neu füllen setzt SelectedIndex und löst sonst über LehrerCb.SelectedIndexChanged eine Endlosrekursion aus
            if (fuellt) return;
            fuellt = true;
            try { ListenFuellen(); } finally { fuellt = false; }
        }
        void ListenFuellen()
        {
            var st = Stunden(s);
            var lehrer = s.NurVerfuegbar.Checked ? Stundenaenderung.VerfuegbareLehrer(D, _datum, st).Cast<Element>().ToList() : Stammdaten.Sortiert(D.Lehrer).Cast<Element>().ToList();
            Fuellen(s.LehrerCb, lehrer, Wert(s.LehrerCb, D.Lehrer) ?? s.Lehrer, s.Lehrer, D.Lehrer);
            var l = Wert(s.LehrerCb, D.Lehrer);
            var faecher = s.NurFaecher.Checked && l is int li ? Stundenaenderung.FaecherDesLehrers(D, li).Cast<Element>().ToList() : Stammdaten.Sortiert(D.Faecher).Cast<Element>().ToList();
            Fuellen(s.FachCb, faecher, Wert(s.FachCb, D.Faecher) ?? s.Fach, s.Fach, D.Faecher);
            var raeume = s.NurFrei.Checked ? Stundenaenderung.FreieRaeume(D, _datum, st).Cast<Element>().ToList() : Stammdaten.Sortiert(D.Raeume).Cast<Element>().ToList();
            Fuellen(s.RaumCb, raeume, Wert(s.RaumCb, D.Raeume) ?? s.Raum, s.Raum, D.Raeume);
        }
        s.KlassenKnopf.Click += (_, _) =>
        {
            var w = KlassenAuswahlDialog.Waehlen(this, s.Klassen);
            if (w != null) { s.Klassen = w; Klassen(); }
        };
        s.NurVerfuegbar.CheckedChanged += (_, _) => Listen();
        s.NurFaecher.CheckedChanged += (_, _) => Listen();
        s.NurFrei.CheckedChanged += (_, _) => Listen();
        s.LehrerCb.SelectedIndexChanged += (_, _) => { if (s.NurFaecher.Checked) Listen(); };
        _block.CheckedChanged += (_, _) => Listen();
        Klassen();
        Listen();
    }

    /// <summary>Gewähltes bzw. eingetipptes Element (Kürzel, Groß-/Kleinschreibung egal) eines editierbaren Auswahlfelds.</summary>
    private static int? Wert<T>(ComboBox c, IEnumerable<T> alle) where T : Element
    {
        if (c.SelectedItem is Element e) return e.Id;
        var t = c.Text.Trim();
        if (t.Length == 0) return null;
        return alle.FirstOrDefault(x => string.Equals(x.Kurzname, t, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>Auswahlliste füllen; der ursprüngliche Wert ist immer enthalten.</summary>
    private static void Fuellen<T>(ComboBox c, List<Element> liste, int? gewaehlt, int? ursprung, IEnumerable<T> alle) where T : Element
    {
        if (ursprung is int u && liste.All(x => x.Id != u) && alle.FirstOrDefault(x => x.Id == u) is { } e) liste.Insert(0, e);
        if (gewaehlt is int g && liste.All(x => x.Id != g) && alle.FirstOrDefault(x => x.Id == g) is { } e2) liste.Insert(0, e2);
        c.BeginUpdate();
        c.Items.Clear();
        c.Items.Add("");
        foreach (var x in liste) c.Items.Add(x);
        c.SelectedItem = liste.FirstOrDefault(x => x.Id == gewaehlt);
        if (c.SelectedIndex < 0) c.SelectedIndex = 0;
        c.EndUpdate();
    }

    private static LehrerKonfliktLoesung? KonfliktFragen(IWin32Window owner, string text)
    {
        var w = OptionenDialog.Fragen(owner, "Frage", text, new[]
        {
            "Lehrer nicht verplanen", "Lehrer hält sowohl Unterricht als auch Vertretung (Betreuung)", "Lehrer im regulären Unterricht vertreten lassen (legt Vertretung an)",
        });
        return w is null ? null : (LehrerKonfliktLoesung)w.Value;
    }

    private bool Ausfuehren(IWin32Window owner)
    {
        var auftraege = new List<Action<Schuldatei>>();
        foreach (var s in _seiten)
        {
            var st = Stunden(s);
            var lehrer = Wert(s.LehrerCb, D.Lehrer);
            var werte = new StundeAendernWerte(lehrer, Wert(s.FachCb, D.Faecher), s.Klassen.ToList(), Wert(s.RaumCb, D.Raeume), s.GruppeCb.Text.Trim(), s.TextTb.Text.Trim());
            var loesung = LehrerKonfliktLoesung.NichtVerplanen;
            if (lehrer is int l && lehrer != s.Lehrer)
            {
                var konflikt = st.Select(x => (x, Stundenaenderung.LehrerKonflikt(D, _datum, x, l, s.UnterrichtId))).FirstOrDefault(k => k.Item2 != null);
                if (konflikt.Item2 != null)
                {
                    var w = KonfliktFragen(owner, Stundenaenderung.KonfliktText(D, l, _datum, konflikt.x, konflikt.Item2));
                    if (w is null) return false;
                    loesung = w.Value;
                }
            }
            if (s.UnterrichtId is int uid)
                auftraege.Add(d => Stundenaenderung.Ausfuehren(d, uid, s.Zeile, _datum, st, werte, loesung, AppZustand.Benutzer));
            else if (lehrer is int nl)
                auftraege.Add(d => Stundenaenderung.ZusaetzlicherLehrer(d, _datum, st, nl, werte.KlassenIds, werte.FachId, werte.RaumId, werte.Text, werte.Schuelergruppe, loesung, AppZustand.Benutzer));
        }
        if (auftraege.Count == 0) return false;
        try
        {
            AppZustand.Doc.Aendern("Stunde ändern", AenderungsArt.Vertretung, d => { foreach (var a in auftraege) a(d); });
            return true;
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            return false;
        }
    }
}

/// <summary>
/// Auswahlliste „Klasse(n)“ wie im Handbuch: Tabelle Name | Langname (94 | 124, Zeilenhöhe 19) über die ganze Fensterbreite,
/// darunter Alle, Markierte, Inverse (64 × 23) und Ok, Abbrechen (75 × 23); Mehrfachauswahl mit Strg.
/// </summary>
public static class KlassenAuswahlDialog
{
    public static List<int>? Waehlen(IWin32Window owner, IReadOnlyCollection<int> gewaehlt)
    {
        using var f = new Dialog("Klasse(n)", 230, 362);
        f.Font = VorbildMasse.Schrift;
        f.FormBorderStyle = FormBorderStyle.Sizable;
        f.ShowIcon = true;
        f.Icon = Application.OpenForms.Count > 0 ? Application.OpenForms[0]!.Icon : f.Icon;
        f.MinimumSize = new Size(246, 260);
        f.Ok.Text = "Ok";
        f.Ok.Size = f.Abbrechen.Size = VorbildMasse.Knopf;
        f.Ok.Anchor = f.Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        f.Ok.Location = new Point(16, 335);
        f.Abbrechen.Location = new Point(107, 335);
        var g = new DataGridView { Left = 0, Top = 0, Width = 230, Height = 296, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(g, false);
        g.BorderStyle = BorderStyle.Fixed3D;
        g.Font = g.ColumnHeadersDefaultCellStyle.Font = VorbildMasse.Schrift;
        g.ColumnHeadersHeight = 19;
        g.RowTemplate.Height = Ui.ZeilenHoehe();
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = true;
        g.RowHeadersVisible = false;
        g.Columns[g.Columns.Add("N", "Name")].Width = 94;
        g.Columns[g.Columns.Add("L", "Langname")].Width = 124;
        foreach (var k in Stammdaten.Sortiert(AppZustand.D.Klassen))
        {
            var i = g.Rows.Add(k.Kurzname, k.Langname);
            g.Rows[i].Tag = k.Id;
        }
        f.Shown += (_, _) =>
        {
            g.ClearSelection();
            foreach (DataGridViewRow r in g.Rows) r.Selected = gewaehlt.Contains((int)r.Tag!);
        };
        var nurMarkierte = false;
        Button Knopf(string text, int x, Action a)
        {
            var b = new Button { Text = text, Left = x, Top = 304, Width = 64, Height = 23, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            b.Click += (_, _) => a();
            f.Controls.Add(b);
            return b;
        }
        Knopf("Alle", 2, () => { foreach (DataGridViewRow r in g.Rows) { r.Visible = true; r.Selected = true; } nurMarkierte = false; });
        Knopf("Markierte", 70, () =>
        {
            // nur die markierten Klassen anzeigen bzw. wieder alle
            nurMarkierte = !nurMarkierte;
            g.CurrentCell = null;
            foreach (DataGridViewRow r in g.Rows) r.Visible = !nurMarkierte || r.Selected;
        });
        Knopf("Inverse", 137, () => { foreach (DataGridViewRow r in g.Rows) if (r.Visible) r.Selected = !r.Selected; });
        f.Controls.Add(g);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return g.Rows.Cast<DataGridViewRow>().Where(r => r.Selected).Select(r => (int)r.Tag!).ToList();
    }
}
