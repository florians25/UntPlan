using UntPlan.App.Fenster;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Dialog „Felder der Ansicht“ wie im Handbuch: oben Ok / Übernahme / Abbrechen, darunter die Tabelle mit den Spalten
/// „Feld“, „aktiv“, „drucken“ und „Anzeige von“ (Kurz- oder Langname bei Elementfeldern); die Felder sind nach Karteikarten
/// gruppiert (Gruppen über ⊞/⊟ auf- und zuklappbar).
/// </summary>
public static class FelderDerAnsichtRaster
{
    /// <summary>Ergebnis: aktive Felder in Anzeigereihenfolge, Felder, die nicht gedruckt werden, und Felder mit Langnamen-Anzeige.</summary>
    public sealed record Auswahl(List<string> Aktiv, HashSet<string> NichtDrucken, HashSet<string>? Langnamen = null);

    public const string Kurzname = "Kurzname";
    public const string Langname = "Langname";

    public static Auswahl? Zeigen(IWin32Window owner, List<Feld> felder, List<string> sichtbar, ISet<string>? nichtDrucken = null, Action<Auswahl>? uebernahme = null,
        ISet<string>? langnamen = null)
    {
        using var d = Erzeugen(felder, sichtbar, nichtDrucken, uebernahme, out var lesen, langnamen);
        return d.ShowDialog(owner) == DialogResult.OK ? lesen() : null;
    }

    internal static Dialog Erzeugen(List<Feld> felder, List<string> sichtbar, ISet<string>? nichtDrucken, Action<Auswahl>? uebernahme, out Func<Auswahl> lesen,
        ISet<string>? langnamen = null)
    {
        var mitAnzeige = felder.Any(f => f.HatLangname);
        var d = new Dialog("Felder der Ansicht", mitAnzeige ? 460 : 380, 470) { FormBorderStyle = FormBorderStyle.Sizable, MinimumSize = new Size(300, 300) };
        d.Ok.Text = "Ok";
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        d.Ok.Location = new Point(10, 8);
        var ueb = new Button { Text = "Übernahme", Width = 88, Height = 26, Location = new Point(104, 8), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        d.Abbrechen.Location = new Point(198, 8);
        d.Controls.Add(ueb);

        var g = new DataGridView { Left = 0, Top = 42, Width = mitAnzeige ? 460 : 380, Height = 428, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Name = "Felder" };
        Ui.RasterGestalten(g, true);
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feld", HeaderText = "⊞ Feld", Width = 210, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Aktiv", HeaderText = "aktiv", Width = 50, SortMode = DataGridViewColumnSortMode.NotSortable });
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Drucken", HeaderText = "drucken", Width = 60, SortMode = DataGridViewColumnSortMode.NotSortable });
        // „Anzeige von“: Kurz- oder Langname des Elementes (nur bei Feldern, die auf andere Elemente verweisen)
        if (mitAnzeige) g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Anzeige", HeaderText = "Anzeige von", Width = 80, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        d.Controls.Add(g);

        // Zeilen: Felder der ersten Karteikarte offen, weitere Karteikarten als zuklappbare Gruppen
        var aktiv = felder.ToDictionary(f => f.Schluessel, f => sichtbar.Contains(f.Schluessel));
        var drucken = felder.ToDictionary(f => f.Schluessel, f => nichtDrucken?.Contains(f.Schluessel) != true);
        var lang = felder.Where(f => f.HatLangname && langnamen?.Contains(f.Schluessel) == true).Select(f => f.Schluessel).ToHashSet();
        var offen = new HashSet<string> { felder.FirstOrDefault()?.Karteikarte ?? "" };
        void Aufbauen()
        {
            g.Rows.Clear();
            foreach (var gruppe in felder.GroupBy(f => f.Karteikarte))
            {
                var erste = gruppe.Key == felder[0].Karteikarte;
                if (!erste)
                {
                    var i = g.Rows.Add((offen.Contains(gruppe.Key) ? "⊟ " : "⊞ ") + gruppe.Key, false, false);
                    var r = g.Rows[i];
                    r.Tag = "gruppe:" + gruppe.Key;
                    r.Cells[1] = new DataGridViewTextBoxCell { Value = "" };
                    r.Cells[2] = new DataGridViewTextBoxCell { Value = "" };
                    if (mitAnzeige) r.Cells[3].Value = "";
                    r.ReadOnly = true;
                    r.DefaultCellStyle.BackColor = Color.FromArgb(240, 244, 248);
                    if (!offen.Contains(gruppe.Key)) continue;
                }
                foreach (var f in gruppe)
                {
                    var i = g.Rows.Add((erste ? "" : "    ") + f.Formulartext, aktiv[f.Schluessel], drucken[f.Schluessel]);
                    g.Rows[i].Tag = f;
                    if (f.Schluessel == "Kurzname") g.Rows[i].Cells[1].ReadOnly = true;
                    if (mitAnzeige && f.HatLangname)
                    {
                        var c = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
                        c.Items.AddRange(Kurzname, Langname);
                        g.Rows[i].Cells[3] = c;
                        c.Value = lang.Contains(f.Schluessel) ? Langname : Kurzname;
                        c.ReadOnly = false;
                    }
                    if (aktiv[f.Schluessel]) g.Rows[i].Cells[0].Style.Font = Ui.SchriftFett;
                }
            }
        }
        Aufbauen();
        g.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex is 1 or 2) g.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.CurrentCell?.ColumnIndex == 3) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        g.DataError += (_, e) => e.ThrowException = false;
        g.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || g.Rows[e.RowIndex].Tag is not Feld f) return;
            if (e.ColumnIndex == 1) { aktiv[f.Schluessel] = g[1, e.RowIndex].Value is true; g[0, e.RowIndex].Style.Font = aktiv[f.Schluessel] ? Ui.SchriftFett : null; }
            if (e.ColumnIndex == 2) drucken[f.Schluessel] = g[2, e.RowIndex].Value is true;
            if (e.ColumnIndex == 3 && f.HatLangname) { if (g[3, e.RowIndex].Value as string == Langname) lang.Add(f.Schluessel); else lang.Remove(f.Schluessel); }
        };
        g.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || g.Rows[e.RowIndex].Tag is not string s || !s.StartsWith("gruppe:")) return;
            var k = s[7..];
            if (!offen.Remove(k)) offen.Add(k);
            Aufbauen();
        };

        Auswahl Lesen()
        {
            g.EndEdit();
            var res = sichtbar.Where(s => aktiv.TryGetValue(s, out var a) && a).ToList();
            foreach (var f in felder) if (aktiv[f.Schluessel] && !res.Contains(f.Schluessel)) res.Add(f.Schluessel);
            if (!res.Contains("Kurzname") && felder.Any(f => f.Schluessel == "Kurzname")) res.Insert(0, "Kurzname");
            return new Auswahl(res, drucken.Where(x => !x.Value).Select(x => x.Key).ToHashSet(), lang.ToHashSet());
        }
        lesen = Lesen;
        ueb.Click += (_, _) => uebernahme?.Invoke(Lesen());
        ueb.Enabled = uebernahme != null;
        return d;
    }
}

/// <summary>Dialog „Serien-Änderung“ wie im Handbuch: Alter Wert, Neuer Wert, Ganze Datei, Nur Markierte; Ok / Abbrechen / Hilfe.</summary>
public static class SerienaenderungDialog
{
    public sealed record Eingabe(string AlterWert, string NeuerWert, bool GanzeDatei, bool NurMarkierte);

    public static Eingabe? Zeigen(IWin32Window owner, string feld, string alterWert)
    {
        using var d = new Dialog("Serien-Änderung", 300, 150);
        var alt = new TextBox { Left = 14, Top = 16, Width = 140, Text = alterWert };
        var neu = new TextBox { Left = 14, Top = 46, Width = 140 };
        d.Controls.Add(alt);
        d.Controls.Add(new Label { Text = "Alter Wert", Left = 162, Top = 19, AutoSize = true });
        d.Controls.Add(neu);
        d.Controls.Add(new Label { Text = "Neuer Wert", Left = 162, Top = 49, AutoSize = true });
        var ganz = new CheckBox { Text = "Ganze Datei", Left = 14, Top = 78, AutoSize = true };
        var mark = new CheckBox { Text = "Nur Markierte", Left = 150, Top = 78, AutoSize = true };
        d.Controls.Add(ganz);
        d.Controls.Add(mark);
        d.Controls.Add(new Label { Text = feld, Left = 162, Top = 0, AutoSize = true, ForeColor = Color.Gray, Font = Ui.SchriftKlein });
        d.Ok.Text = "Ok";
        d.Ok.Width = d.Abbrechen.Width = 82;
        d.Ok.Location = new Point(14, 112);
        d.Abbrechen.Location = new Point(102, 112);
        var hilfe = new Button { Text = "Hilfe", Width = 82, Height = 26, Location = new Point(204, 112), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        hilfe.Click += (_, _) => HilfeFenster.Zeigen("Stammdaten");
        d.Controls.Add(hilfe);
        d.ActiveControl = neu;
        return d.ShowDialog(owner) == DialogResult.OK ? new Eingabe(alt.Text.Trim(), neu.Text.Trim(), ganz.Checked, mark.Checked) : null;
    }
}

/// <summary>Dialog „Farbe“: Vorder- und Hintergrundfarbe für die markierten Elemente (Vorschau mit dem Kurznamen).</summary>
public static class ElementFarbeDialog
{
    /// <summary>Liefert (Vordergrund, Hintergrund) als ARGB (0 = keine Farbe) oder null bei Abbruch.</summary>
    public static (int Vorder, int Hinter)? Zeigen(IWin32Window owner, string beispiel, int vorder, int hinter) => Zeigen(owner, beispiel, vorder, hinter, null)?.Farbe;

    /// <summary>
    /// Wie oben, zusätzlich mit „Neuen Elementen automatisch eine Farbe zuweisen“ (<paramref name="automatisch"/> = bisheriger Zustand,
    /// null = Option nicht anbieten). Ergebnis: Farben und neuer Zustand der Option.
    /// </summary>
    public static ((int Vorder, int Hinter) Farbe, bool Automatisch)? Zeigen(IWin32Window owner, string beispiel, int vorder, int hinter, bool? automatisch)
    {
        using var d = new Dialog("Farbe", 300, automatisch.HasValue ? 176 : 150);
        var auto = new CheckBox { Text = "Neuen Elementen automatisch eine Farbe zuweisen", Left = 14, Top = 100, AutoSize = true, Checked = automatisch == true, Visible = automatisch.HasValue, Name = "Automatisch" };
        d.Controls.Add(auto);
        var muster = new Label { Text = beispiel, Left = 14, Top = 16, Width = 110, Height = 74, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle, Font = Ui.SchriftGross };
        void Zeigen()
        {
            muster.BackColor = Ui.Farbe(hinter, Color.White);
            muster.ForeColor = Ui.Farbe(vorder, Color.Black);
        }
        Zeigen();
        Button Knopf(string text, int top, Action klick)
        {
            var b = new Button { Text = text, Left = 136, Top = top, Width = 150, Height = 24 };
            b.Click += (_, _) => klick();
            d.Controls.Add(b);
            return b;
        }
        int Waehlen(int alt)
        {
            using var cd = new ColorDialog { Color = Ui.Farbe(alt, Color.White), FullOpen = true };
            return cd.ShowDialog(d) == DialogResult.OK ? cd.Color.ToArgb() : alt;
        }
        Knopf("Vordergrund…", 14, () => { vorder = Waehlen(vorder); Zeigen(); });
        Knopf("Hintergrund…", 42, () => { hinter = Waehlen(hinter); Zeigen(); });
        Knopf("Farben löschen", 70, () => { vorder = hinter = 0; Zeigen(); });
        d.Controls.Add(muster);
        d.Ok.Text = "Ok";
        return d.ShowDialog(owner) == DialogResult.OK ? ((vorder, hinter), auto.Checked) : null;
    }
}

/// <summary>Dialog „Ansicht umbenennen“ (Formate verwalten → Bearbeiten): Name und Langname des Formats.</summary>
public static class AnsichtUmbenennenDialog
{
    public sealed record Ergebnis(string Name, string Langname, bool ImMenu, bool Standard);

    /// <summary>Bearbeiten mit Langname, „im Menu“ und „Standard“ (Formate verwalten, Handbuch 2.3.4).</summary>
    public static Ergebnis? Bearbeiten(IWin32Window owner, string name, string langname, bool imMenu, bool standard, bool umbenennbar)
    {
        using var d = new Dialog("Format bearbeiten", 300, 150);
        var t = new TextBox { Left = 12, Top = 14, Width = 140, Text = name, Enabled = umbenennbar };
        var l = new TextBox { Left = 12, Top = 42, Width = 140, Text = langname };
        d.Controls.Add(t);
        d.Controls.Add(new Label { Text = "Name", Left = 160, Top = 17, AutoSize = true });
        d.Controls.Add(l);
        d.Controls.Add(new Label { Text = "Langname", Left = 160, Top = 45, AutoSize = true });
        var menu = new CheckBox { Text = "im Menu", Left = 12, Top = 72, AutoSize = true, Checked = imMenu };
        var std = new CheckBox { Text = "Standard", Left = 110, Top = 72, AutoSize = true, Checked = standard };
        d.Controls.Add(menu);
        d.Controls.Add(std);
        d.Ok.Text = "Ok";
        d.Ok.Location = new Point(12, 112);
        d.Abbrechen.Location = new Point(106, 112);
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        return d.ShowDialog(owner) == DialogResult.OK && t.Text.Trim().Length > 0 ? new Ergebnis(t.Text.Trim(), l.Text.Trim(), menu.Checked, std.Checked) : null;
    }

    public static string? Zeigen(IWin32Window owner, string name)
    {
        using var d = new Dialog("Ansicht umbenennen", 270, 84);
        var t = new TextBox { Left = 12, Top = 14, Width = 140, Text = name };
        d.Controls.Add(t);
        d.Controls.Add(new Label { Text = "Name", Left = 160, Top = 17, AutoSize = true });
        d.Ok.Text = "Ok";
        d.Ok.Location = new Point(12, 48);
        d.Abbrechen.Location = new Point(106, 48);
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        return d.ShowDialog(owner) == DialogResult.OK && t.Text.Trim().Length > 0 ? t.Text.Trim() : null;
    }
}
