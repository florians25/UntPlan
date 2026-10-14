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
    private readonly TabControl _tabs = new() { Left = 8, Top = 8, Width = 404, Height = 382 };
    private readonly RadioButton _einzeln = new() { Text = "Unterrichtsstunde", Left = 10, Top = 18, Width = 150, Checked = true };
    private readonly RadioButton _block = new() { Text = "Stundenblock", Left = 10, Top = 38, Width = 150 };
    private readonly List<Seite> _seiten = new();

    private static Schuldatei D => AppZustand.D;

    /// <summary>Dialog zeigen und Änderungen ausführen; true, wenn etwas geändert wurde.</summary>
    public static bool Zeigen(IWin32Window owner, DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege)
    {
        using var f = new StundeAendernDialog(datum, stunde, eintraege);
        return f.ShowDialog(owner) == DialogResult.OK && f.Ausfuehren(owner);
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege)
    {
        var f = new StundeAendernDialog(datum, stunde, eintraege);
        f.Show(owner);
        return f;
    }

    private StundeAendernDialog(DateOnly datum, int stunde, IReadOnlyList<TagesEintrag> eintraege) : base($"Stunde ändern:  {datum.Day}.{datum.Month}. / {stunde}", 420, 440)
    {
        _datum = datum;
        _stunde = stunde;
        Controls.Add(_tabs);
        foreach (var e in eintraege)
        {
            var s = new Seite
            {
                UnterrichtId = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId, Zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0, Klassen = e.KlassenIds.ToList(),
                Lehrer = e.LehrerIds.Count > 0 ? e.LehrerIds[0] : null, Fach = e.FachId, Raum = e.RaumIds.Count > 0 ? e.RaumIds[0] : null,
                Gruppe = e.Vertretung?.Schuelergruppe is { Length: > 0 } g ? g : e.Unterricht?.Schuelergruppe ?? "", Text = e.Vertretung?.Text ?? "",
            };
            SeiteHinzufuegen(s);
        }
        var neu = new Button { Text = "Neuer Lehrer", Left = 140, Top = 404, Width = 96, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        neu.Click += (_, _) =>
        {
            var vorlage = _seiten.FirstOrDefault();
            SeiteHinzufuegen(new Seite { UnterrichtId = null, Klassen = vorlage?.Klassen.ToList() ?? new(), Fach = vorlage is null ? null : ElementListe.Id(vorlage.FachCb), Raum = null, Gruppe = vorlage?.GruppeCb.Text ?? "" });
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

    private void SeiteHinzufuegen(Seite s)
    {
        var ix = AppZustand.Doc.Index;
        var seite = new TabPage(s.UnterrichtId is null ? "Neuer Lehrer" : $"{ix.KLehrer(s.Lehrer)}, {ix.KFach(s.Fach)}".Trim(' ', ','));
        var anwenden = new GroupBox { Text = "Anwenden auf", Left = 8, Top = 6, Width = 170, Height = 62 };
        if (_seiten.Count == 0) anwenden.Controls.AddRange(new Control[] { _einzeln, _block });
        else anwenden.Controls.Add(new Label { Text = "(wie erster Reiter)", Left = 10, Top = 26, AutoSize = true, ForeColor = Color.DimGray });
        seite.Controls.Add(anwenden);
        var y = 76;
        seite.Controls.Add(new Label { Text = "Klasse", Left = 8, Top = y, AutoSize = true });
        s.KlassenKnopf = new Button { Left = 8, Top = y + 18, Width = 90, Height = 24, BackColor = Ui.BlauHell, FlatStyle = FlatStyle.Flat };
        s.KlassenText = new Label { Left = 108, Top = y + 22, Width = 270, AutoEllipsis = true };
        seite.Controls.AddRange(new Control[] { s.KlassenKnopf, s.KlassenText });
        y += 48;
        ComboBox Feld(string titel, string option, bool haken, out CheckBox cb)
        {
            seite.Controls.Add(new Label { Text = titel, Left = 8, Top = y, AutoSize = true });
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = y + 18, Width = 180 };
            c.Format += (_, ev) => ev.Value = ev.ListItem is Element el ? Ui.ElementName(el) : "";
            cb = new CheckBox { Text = option, Left = 200, Top = y + 19, AutoSize = true, Checked = haken };
            seite.Controls.Add(c);
            seite.Controls.Add(cb);
            y += 46;
            return c;
        }
        s.LehrerCb = Feld("Lehrer", "Nur verfügbare Lehrer", true, out s.NurVerfuegbar);
        s.FachCb = Feld("Fach", "Nur Fächer des Lehrers", true, out s.NurFaecher);
        s.RaumCb = Feld("Raum", "Nur freie Räume", true, out s.NurFrei);
        seite.Controls.Add(new Label { Text = "Schülergruppe", Left = 8, Top = y, AutoSize = true });
        s.GruppeCb = new ComboBox { Left = 8, Top = y + 18, Width = 180, Text = s.Gruppe };
        s.GruppeCb.Items.AddRange(D.Unterrichte.Select(u => u.Schuelergruppe).Where(g => g.Length > 0).Distinct().OrderBy(g => g).Cast<object>().ToArray());
        s.GruppeCb.Text = s.Gruppe;
        seite.Controls.Add(s.GruppeCb);
        y += 50;
        seite.Controls.Add(new Label { Text = "Text", Left = 8, Top = y, AutoSize = true });
        s.TextTb = new TextBox { Left = 8, Top = y + 18, Width = 370, Text = s.Text };
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
            Fuellen(s.LehrerCb, lehrer, ElementListe.Id(s.LehrerCb) ?? s.Lehrer, s.Lehrer, D.Lehrer);
            var l = ElementListe.Id(s.LehrerCb);
            var faecher = s.NurFaecher.Checked && l is int li ? Stundenaenderung.FaecherDesLehrers(D, li).Cast<Element>().ToList() : Stammdaten.Sortiert(D.Faecher).Cast<Element>().ToList();
            Fuellen(s.FachCb, faecher, ElementListe.Id(s.FachCb) ?? s.Fach, s.Fach, D.Faecher);
            var raeume = s.NurFrei.Checked ? Stundenaenderung.FreieRaeume(D, _datum, st).Cast<Element>().ToList() : Stammdaten.Sortiert(D.Raeume).Cast<Element>().ToList();
            Fuellen(s.RaumCb, raeume, ElementListe.Id(s.RaumCb) ?? s.Raum, s.Raum, D.Raeume);
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
            var lehrer = ElementListe.Id(s.LehrerCb);
            var werte = new StundeAendernWerte(lehrer, ElementListe.Id(s.FachCb), s.Klassen.ToList(), ElementListe.Id(s.RaumCb), s.GruppeCb.Text.Trim(), s.TextTb.Text.Trim());
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

/// <summary>Auswahlliste „Klasse(n)“ mit Name und Langname, Schaltflächen Alle, Markierte, Inverse, Ok und Abbrechen.</summary>
public static class KlassenAuswahlDialog
{
    public static List<int>? Waehlen(IWin32Window owner, IReadOnlyCollection<int> gewaehlt)
    {
        using var f = new Dialog("Klasse(n)", 330, 330);
        f.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        f.Ok.Text = "Ok";
        var g = new DataGridView { Left = 6, Top = 6, Width = 318, Height = 244, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = true;
        g.RowHeadersVisible = false;
        g.Columns[g.Columns.Add("N", "Name")].Width = 70;
        g.Columns[g.Columns.Add("L", "Langname")].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
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
            var b = new Button { Text = text, Left = x, Top = 256, Width = 74, Height = 24, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            b.Click += (_, _) => a();
            f.Controls.Add(b);
            return b;
        }
        Knopf("Alle", 92, () => { foreach (DataGridViewRow r in g.Rows) { r.Visible = true; r.Selected = true; } nurMarkierte = false; });
        Knopf("Markierte", 170, () =>
        {
            // nur die markierten Klassen anzeigen bzw. wieder alle
            nurMarkierte = !nurMarkierte;
            g.CurrentCell = null;
            foreach (DataGridViewRow r in g.Rows) r.Visible = !nurMarkierte || r.Selected;
        });
        Knopf("Inverse", 248, () => { foreach (DataGridViewRow r in g.Rows) if (r.Visible) r.Selected = !r.Selected; });
        f.Controls.Add(g);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return g.Rows.Cast<DataGridViewRow>().Where(r => r.Selected).Select(r => (int)r.Tag!).ToList();
    }
}
