using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>Auswahl beim Ablegen auf eine belegte Stunde: Tausch/behindernden Unterricht entplanen oder Kollision erzeugen.</summary>
public static class PlanungsKonfliktDialog
{
    public static int? Fragen(IWin32Window owner, string titel, string details, string option1, string option2)
    {
        using var d = new Dialog(titel, 460, 250);
        d.Controls.Add(new Label { Text = details, Left = 12, Top = 10, Width = 436, Height = 110, AutoSize = false });
        var grp = new GroupBox { Text = "Optionen", Left = 12, Top = 124, Width = 436, Height = 76 };
        var r1 = new RadioButton { Text = option1, Left = 12, Top = 20, Width = 410, Checked = true };
        var r2 = new RadioButton { Text = option2, Left = 12, Top = 44, Width = 410 };
        grp.Controls.Add(r1);
        grp.Controls.Add(r2);
        d.Controls.Add(grp);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return r1.Checked ? 0 : 1;
    }
}

/// <summary>Frage mit Meldungstext und mehreren Optionen (Optionsfelder), z. B. "Kollision in Zielstunde".</summary>
public static class OptionenDialog
{
    public static int? Fragen(IWin32Window owner, string titel, string meldung, IReadOnlyList<string> optionen, int vorgabe = 0)
    {
        var hoehe = 140 + optionen.Count * 24;
        using var d = new Dialog(titel, 520, Math.Max(220, hoehe + 60));
        var text = new TextBox { Text = meldung.Replace("\n", Environment.NewLine), Left = 12, Top = 10, Width = 496, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = d.BackColor };
        d.Controls.Add(text);
        var grp = new GroupBox { Text = "Optionen", Left = 12, Top = 126, Width = 496, Height = 28 + optionen.Count * 24 };
        var knoepfe = new List<RadioButton>();
        for (var i = 0; i < optionen.Count; i++)
        {
            var r = new RadioButton { Text = optionen[i], Left = 12, Top = 20 + i * 24, Width = 470, Checked = i == vorgabe };
            grp.Controls.Add(r);
            knoepfe.Add(r);
        }
        d.Controls.Add(grp);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return knoepfe.FindIndex(k => k.Checked);
    }
}

/// <summary>
/// Raum zuordnen / löschen.
/// <list type="bullet">
/// <item>Links: die Kopplungszeilen des Unterrichts.</item>
/// <item>Rechts: alle möglichen Räume mit Kapazität, Ausweich- und Stammraum, Belegung, Raumgruppe, Gang, Wochenbelegung, freien Stunden und Kapazitätsdifferenz.</item>
/// <item>Anwenden auf die Stunde, den Block oder alle Stunden des Unterrichts.</item>
/// <item>Filter: nur freie Räume, nur Wunschräume, ausreichende Kapazität und Suche.</item>
/// </list>
/// Doppelklick oder ← ordnet den Raum zu; bei belegten Räumen wird nach Verdrängen, Tauschen oder Kollision gefragt.
/// </summary>
public static class RaumZuordnenDialog
{
    private static int _umfang = 1;
    private static bool _nurFrei;

    public static bool Zeigen(IWin32Window owner, Planstunde p, bool modal = true)
    {
        Dokument doc = AppZustand.Doc;
        DatenIndex ix = doc.Index;
        Unterricht u = ix.Unterrichte[p.UnterrichtId];
        var idx = doc.Daten.Stundenplan.IndexOf(p);
        var d = new Dialog("Raum zuordnen / löschen", 1220, 640);
        d.Ok.Text = "Schließen";
        d.Abbrechen.Visible = false;
        d.MinimumSize = new Size(900, 480);
        d.FormBorderStyle = FormBorderStyle.Sizable;
        var geaendert = false;

        var info = new Label { Left = 10, Top = 10, Width = 500, Height = 60, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4), BackColor = Color.White };
        var umfang = new GroupBox { Text = "Anwenden auf", Left = 556, Top = 4, Width = 230, Height = 88 };
        RadioButton[] rbs = new[] { "Unterrichtsstunde", "Stundenblock", "Alle Stunden eines Unterr." }
            .Select((t, i) => new RadioButton { Text = t, Left = 10, Top = 18 + i * 22, Width = 210, Checked = i == _umfang }).ToArray();
        umfang.Controls.AddRange(rbs);
        CheckBox lang = new CheckBox { Text = "Langname anzeigen", Left = 800, Top = 12, AutoSize = true };
        var nurFrei = new CheckBox { Text = "Nur freie Räume", Left = 800, Top = 34, AutoSize = true, Checked = _nurFrei };
        var nurWunsch = new CheckBox { Text = "Nur Wunschräume / Raumgruppe", Left = 800, Top = 56, AutoSize = true };
        var nurKap = new CheckBox { Text = "Nur ausreichende Kapazität", Left = 800, Top = 78, AutoSize = true };
        var suche = new TextBox { Left = 700, Top = 100, Width = 180, PlaceholderText = "Raum suchen…" };

        d.Controls.Add(new Label { Text = "Auswahl derzeitiger Unterricht:", Left = 10, Top = 104, AutoSize = true });
        DataGridView links = new DataGridView { Left = 10, Top = 128, Width = 500, Height = 420, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
        Ui.RasterGestalten(links, false);
        links.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        links.MultiSelect = false;
        links.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[] { ("U", "Unt.", 40), ("L", "Lehrer", 60), ("F", "Fach", 50), ("R", "Raum", 64), ("K", "Klasse(n)", 110), ("W", "Wunschraum", 84), ("G", "U-Gruppe", 76) })
            links.Columns[links.Columns.Add(n, k)].Width = w;

        var pfeil = new Button { Text = "←", Left = 516, Top = 220, Width = 34, Height = 34, Font = new Font(Ui.Schrift.FontFamily, 13, FontStyle.Bold) };
        var loeschen = new Button { Image = Symbole.Get("loeschen", 16), Left = 516, Top = 260, Width = 34, Height = 34 };
        var tip = new ToolTip();
        tip.SetToolTip(pfeil, "Markierten Raum zuordnen (Doppelklick)");
        tip.SetToolTip(loeschen, "Raum der markierten Zeile löschen (Entf)");

        d.Controls.Add(new Label { Text = "Mögliche Räume:", Left = 556, Top = 104, AutoSize = true });
        var rechts = new DataGridView { Left = 556, Top = 128, Width = 654, Height = 420, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(rechts, false);
        rechts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rechts.MultiSelect = false;
        rechts.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[] { ("Rm", "Rm.", 64), ("Kap", "Kap.", 38), ("AR", "Ausw.-Rm.", 62), ("SR", "Stammr.", 52), ("B", "Besetzt", 52), ("BD", "Besetzt durch", 118), ("RG", "Raumgruppe", 80), ("Ga", "Gang", 50), ("St", "Wo.-Bel.", 52), ("SF", "Std.frei", 52), ("KD", "Kap.Diff.", 56) })
        {
            var c = rechts.Columns[rechts.Columns.Add(n, k)];
            c.Width = w;
            if (n is "Kap" or "St" or "SF" or "KD") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (n is "AR" or "SR" or "B") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        var legende = new Label
        {
            Text = "grün = Wunschraum bzw. Raumgruppe · rot = besetzt (in mindestens einer Stunde) · grau = gesperrt · Std.frei = freie Stunden von den betroffenen Stunden",
            Left = 10, Top = 556, Width = 1000, Height = 18, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        d.Controls.AddRange(new Control[] { info, umfang, lang, nurFrei, nurWunsch, nurKap, suche, links, pfeil, loeschen, rechts, legende });
        // Häkchen selbst zeichnen (unabhängig von der Schriftart)
        rechts.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
            e.PaintBackground(e.CellBounds, true);
            var r = e.CellBounds;
            var m = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            using var pen = new Pen(rechts.Columns[e.ColumnIndex].Name == "B" ? Color.ForestGreen : Color.SteelBlue, 2.2f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[] { new Point(m.X - 5, m.Y), new Point(m.X - 1, m.Y + 4), new Point(m.X + 6, m.Y - 4) });
            e.Handled = true;
        };

        int Umfang() => Array.FindIndex(rbs, r => r.Checked);
        Planstunde Aktuell() => doc.Daten.Stundenplan[idx];
        List<Planstunde> Stunden() => Planungsdienst.RaumStunden(doc.Daten, Aktuell(), Umfang());
        int Zeile() => Math.Max(0, links.CurrentRow?.Index ?? 0);
        string K(ElementTyp t, int? id)
        {
            if (id is null) return "";
            var e = ix.Element(t, id.Value);
            return e is null ? "" : lang.Checked && e.Langname.Length > 0 ? e.Langname : e.Kurzname;
        }

        void Info()
        {
            var st = Stunden();
            info.Text = $"Unterricht: {u.Id}: {ix.UnterrichtText(u)}\nStunden: {st.Count}  " +
                        string.Join(", ", st.Select(x => $"{Planungsdienst.Tagname(doc.Daten, x.Tag)}-{x.Stunde}")) + $"\nRaum: {string.Join(", ", st.Select(x => K(ElementTyp.Raum, x.RaumFuerZeile(Zeile()))).Where(x => x.Length > 0).Distinct())}";
        }

        void Links()
        {
            var alt = Zeile();
            links.Rows.Clear();
            var akt = Aktuell();
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var gruppe = (z.UGruppeId ?? u.UGruppeId) is int g ? doc.Daten.UGruppen.FirstOrDefault(x => x.Id == g)?.Kurzname ?? "" : "";
                var r = links.Rows.Add(i == 0 ? u.Id.ToString() : "", K(ElementTyp.Lehrer, z.LehrerId), K(ElementTyp.Fach, z.FachId), K(ElementTyp.Raum, akt.RaumFuerZeile(i)),
                    string.Join(",", z.KlassenIds.Select(k => K(ElementTyp.Klasse, k))), z.Wunschraum.HasValue ? K(ElementTyp.Raum, z.Wunschraum) : K(ElementTyp.Raumgruppe, z.Wunschraumgruppe), gruppe);
                links.Rows[r].Cells[0].Style.BackColor = Color.FromArgb(240, 222, 200);
            }
            if (links.Rows.Count > 0) links.CurrentCell = links.Rows[Math.Min(alt, links.Rows.Count - 1)].Cells[1];
        }

        void Rechts()
        {
            var altRaum = (rechts.CurrentRow?.Tag as Planungsdienst.RaumZeile)?.Raum.Id;
            rechts.Rows.Clear();
            var f = suche.Text.Trim();
            foreach (var z in Planungsdienst.RaumUebersicht(doc.Daten, ix, Stunden(), Zeile()))
            {
                if (nurFrei.Checked && !z.GanzFrei) continue;
                if (nurWunsch.Checked && !z.Wunsch) continue;
                if (nurKap.Checked && z.KapazitaetDiff < 0) continue;
                if (f.Length > 0 && !z.Raum.Kurzname.Contains(f, StringComparison.CurrentCultureIgnoreCase) && !z.Raum.Langname.Contains(f, StringComparison.CurrentCultureIgnoreCase) &&
                    !z.Raumgruppen.Contains(f, StringComparison.CurrentCultureIgnoreCase)) continue;
                var durch = string.Join("; ", z.Belegt.Select(b => ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? $"{ix.UnterrichtText(o)}" : "").Distinct());
                var r = rechts.Rows.Add(lang.Checked && z.Raum.Langname.Length > 0 ? z.Raum.Langname : z.Raum.Kurzname, z.Raum.Kapazitaet > 0 ? z.Raum.Kapazitaet : "",
                    z.Ausweichraum ? "✔" : "", z.Stammraum ? "✔" : "", z.Belegt.Count > 0 ? "✔" : "", durch, z.Raumgruppen, z.Gang, z.Wochenbelegung,
                    z.StundenFrei, z.KapazitaetDiff?.ToString("+0;-0;0") ?? "");
                var row = rechts.Rows[r];
                row.Tag = z;
                if (z.Gesperrt > 0 && z.StundenFrei == 0) row.DefaultCellStyle.BackColor = Color.FromArgb(232, 232, 232);
                else if (z.Belegt.Count > 0) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 238, 238);
                else if (z.Wunsch) row.DefaultCellStyle.BackColor = Color.FromArgb(226, 246, 226);
                row.Cells["B"].Style.ForeColor = Color.ForestGreen;
                if (z.KapazitaetDiff < 0) row.Cells["KD"].Style.ForeColor = Color.Firebrick;
                if (z.StundenFrei < z.StundenGesamt) row.Cells["SF"].Style.ForeColor = Color.Firebrick;
                if (z.Raum.Id == altRaum) rechts.CurrentCell = row.Cells[0];
            }
        }

        void Alles()
        {
            ix = doc.Index;
            Info();
            Links();
            Rechts();
        }

        void Zuordnen()
        {
            if (rechts.CurrentRow?.Tag is not Planungsdienst.RaumZeile z) return;
            var zeile = Zeile();
            var loesung = Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen;
            if (z.Belegt.Count > 0)
            {
                var text = string.Join("\n", z.Belegt.Select(b => $"{Planungsdienst.Tagname(doc.Daten, b.Tag)}-{b.Stunde}: {(ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? ix.UnterrichtText(o) : "")}").Distinct());
                var w = OptionenDialog.Fragen(d, "Raum nicht frei", $"Raum {z.Raum.Kurzname} ist belegt:\n{text}",
                    new[] { "Behindernden Unterricht aus dem Raum verdrängen", "Räume tauschen", "Kollision zulassen" });
                if (w is null) return;
                loesung = w switch { 0 => Planungsdienst.RaumKonfliktLoesung.Verdraengen, 1 => Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen, _ => Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen };
            }
            else if (z.Gesperrt > 0 && !Ui.Frage($"Raum {z.Raum.Kurzname} ist in {z.Gesperrt} der Stunden gesperrt. Trotzdem zuordnen?")) return;
            var indizes = Stunden().Select(x => doc.Daten.Stundenplan.IndexOf(x)).Where(i => i >= 0).ToList();
            doc.Aendern($"Raum {z.Raum.Kurzname} zuordnen", AenderungsArt.Stundenplan,
                dd => Planungsdienst.RaumZuordnen(dd, indizes.Select(i => dd.Stundenplan[i]), zeile, z.Raum.Id, loesung));
            geaendert = true;
            Alles();
        }

        void Loeschen()
        {
            var zeile = Zeile();
            var indizes = Stunden().Select(x => doc.Daten.Stundenplan.IndexOf(x)).Where(i => i >= 0).ToList();
            doc.Aendern("Raum löschen", AenderungsArt.Stundenplan,
                dd => Planungsdienst.RaumZuordnen(dd, indizes.Select(i => dd.Stundenplan[i]), zeile, null, Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen));
            geaendert = true;
            Alles();
        }

        links.SelectionChanged += (_, _) => { Info(); Rechts(); };
        foreach (var rb in rbs) rb.CheckedChanged += (_, _) => { if (rb.Checked) { _umfang = Umfang(); Info(); Rechts(); } };
        lang.CheckedChanged += (_, _) => Alles();
        nurFrei.CheckedChanged += (_, _) => { _nurFrei = nurFrei.Checked; Rechts(); };
        nurWunsch.CheckedChanged += (_, _) => Rechts();
        nurKap.CheckedChanged += (_, _) => Rechts();
        suche.TextChanged += (_, _) => Rechts();
        rechts.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Zuordnen(); };
        rechts.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Zuordnen(); e.Handled = true; } };
        links.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { Loeschen(); e.Handled = true; } };
        pfeil.Click += (_, _) => Zuordnen();
        loeschen.Click += (_, _) => Loeschen();
        Alles();
        if (!modal)
        {
            d.Show(owner);
            return false;
        }
        using (d) d.ShowDialog(owner);
        return geaendert;
    }
}

/// <summary>Tauschvorschläge mit Gewinn (Verbesserung bzw. Verschlechterung der Bewertung).</summary>
public static class TauschvorschlaegeDialog
{
    public static Tauschvorschlag? Zeigen(IWin32Window owner, Planstunde p)
    {
        List<Tauschvorschlag> liste;
        using (new WarteCursor()) liste = Planungsdienst.Tauschvorschlaege(AppZustand.D, p);
        using var d = new Dialog("Tauschvorschläge", 700, 360);
        d.Ok.Text = "Tauschen";
        var g = new DataGridView { Left = 10, Top = 10, Width = 680, Height = 300 };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.RowHeadersVisible = false;
        g.Columns.Add("G", "Gewinn");
        g.Columns.Add("T", "Tausch");
        g.Columns[1].Width = 590;
        foreach (var v in liste)
        {
            var r = g.Rows.Add(v.Gewinn.ToString("+0;-0;0"), v.Beschreibung);
            g.Rows[r].Tag = v;
            g.Rows[r].Cells[0].Style.ForeColor = v.Gewinn > 0 ? Color.DarkGreen : v.Gewinn < 0 ? Color.DarkRed : Color.Black;
        }
        d.Controls.Add(g);
        if (liste.Count == 0) Ui.Info("Für diese Stunde wurden keine Tauschmöglichkeiten gefunden.");
        return d.ShowDialog(owner) == DialogResult.OK ? g.CurrentRow?.Tag as Tauschvorschlag : null;
    }
}

public sealed class WarteCursor : IDisposable
{
    private readonly Cursor _alt = Cursor.Current ?? Cursors.Default;
    public WarteCursor() => Cursor.Current = Cursors.WaitCursor;
    public void Dispose() => Cursor.Current = _alt;
}

/// <summary>Stundenplan-Einstellungen (Layout, Farben, Standardformat) und Grafik-Editor "Stundenplan-Stunde".</summary>
public static class StundenplanEinstellungenDialog
{
    public static StundenplanFormat? Zeigen(IWin32Window owner, StundenplanFormat f)
    {
        using var d = new Dialog($"Stundenplan-Einstellungen – {f.Name}", 640, 470);
        var tabs = new TabControl { Left = 8, Top = 8, Width = 624, Height = 410 };
        var layout = new TabPage("Layout");
        var y = 12;
        var name = Ui.Zeile(layout, "Formatname", new TextBox { Text = f.Name }, ref y);
        var schrift = Ui.Zeile(layout, "Schriftgröße", new NumericUpDown { Minimum = 6, Maximum = 20, Value = f.Schriftgroesse }, ref y, 170, 70);
        var farbe = Ui.Zeile(layout, "Farbe nach", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y);
        farbe.Items.AddRange(new object[] { "Fach", "Lehrer", "Klasse", "Raum", "keine" });
        farbe.SelectedIndex = (int)f.Farbe;
        var lang = Ui.Zeile(layout, "Langnamen anzeigen", new CheckBox { Checked = f.Langname }, ref y);
        var zeiten = Ui.Zeile(layout, "Uhrzeiten anzeigen", new CheckBox { Checked = f.ZeitenAnzeigen }, ref y);
        var dd = Ui.Zeile(layout, "DragDrop: Farben laut Zeitwünsche", new CheckBox { Checked = f.DragDropZeitwunschFarben }, ref y, 220);
        var std = Ui.Zeile(layout, "Stpl-Stunde: Standardformat", new CheckBox { Checked = f.Standardformat }, ref y, 220);
        layout.Controls.Add(new Label { Text = "Tipp: Für jedes Element zwei Formate verwenden – ein kleines zum Arbeiten am Stundenplan und \"… groß\" mit allen Informationen für die Ausgabe.", Left = 12, Top = y + 10, Width = 590, Height = 40, ForeColor = Color.DimGray });
        tabs.TabPages.Add(layout);

        var stunde = new TabPage("Stundenplan-Stunde");
        var editor = new StundenEditor(f.Felder.Select(x => new StundenFeld { Art = x.Art, Zeile = x.Zeile, X = x.X, Y = x.Y, B = x.B, H = x.H, SchriftProzent = x.SchriftProzent, Fett = x.Fett }).ToList())
        { Left = 170, Top = 12, Width = 430, Height = 280 };
        var liste = new ListBox { Left = 12, Top = 12, Width = 150, Height = 280 };
        foreach (var a in Enum.GetValues<StundenFeldArt>()) liste.Items.Add(a);
        liste.MouseDown += (_, e) => { if (liste.SelectedItem is StundenFeldArt a) liste.DoDragDrop("feld:" + a, DragDropEffects.Copy); };
        var prozent = new NumericUpDown { Left = 170, Top = 300, Width = 70, Minimum = 40, Maximum = 300, Value = 100 };
        var fett = new CheckBox { Text = "Fett", Left = 250, Top = 300, Width = 60 };
        var zeileNr = new NumericUpDown { Left = 380, Top = 300, Width = 50, Minimum = 1, Maximum = 6, Value = 1 };
        var entfernen = new Button { Text = "Feld entfernen", Left = 440, Top = 298, Width = 110 };
        stunde.Controls.Add(new Label { Text = "Schriftgrad %", Left = 170, Top = 328, Width = 100 });
        stunde.Controls.Add(new Label { Text = "Kopplungszeile", Left = 380, Top = 328, Width = 100 });
        stunde.Controls.Add(new Label { Text = "Felder per Drag & Drop in die Stunde ziehen. Das Hintergrundfeld symbolisiert die Stundenplan-Zelle; Hilfslinien unterstützen beim Ausrichten. Für Kopplungen ein Feld mehrfach einfügen (Kopplungszeile 1, 2, …).", Left = 12, Top = 350, Width = 590, Height = 32, ForeColor = Color.DimGray });
        editor.AuswahlGeaendert += sf =>
        {
            prozent.Enabled = fett.Enabled = zeileNr.Enabled = entfernen.Enabled = sf != null;
            if (sf is null) return;
            prozent.Value = Math.Clamp(sf.SchriftProzent, 40, 300);
            fett.Checked = sf.Fett;
            zeileNr.Value = Math.Clamp(sf.Zeile, 1, 6);
        };
        prozent.ValueChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.SchriftProzent = (int)prozent.Value; editor.Invalidate(); } };
        fett.CheckedChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.Fett = fett.Checked; editor.Invalidate(); } };
        zeileNr.ValueChanged += (_, _) => { if (editor.Ausgewaehlt != null) { editor.Ausgewaehlt.Zeile = (int)zeileNr.Value; editor.Invalidate(); } };
        entfernen.Click += (_, _) => editor.Entfernen();
        stunde.Controls.AddRange(new Control[] { liste, editor, prozent, fett, zeileNr, entfernen });
        tabs.TabPages.Add(stunde);
        d.Controls.Add(tabs);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        f.Name = name.Text;
        f.Schriftgroesse = (int)schrift.Value;
        f.Farbe = (FarbeNach)farbe.SelectedIndex;
        f.Langname = lang.Checked;
        f.ZeitenAnzeigen = zeiten.Checked;
        f.DragDropZeitwunschFarben = dd.Checked;
        f.Felder = editor.Felder;
        f.Standardformat = std.Checked || f.Felder.Count == 0;
        return f;
    }

    /// <summary>Grafik-Editor der Stundenplan-Zelle mit Snap-Lines.</summary>
    private sealed class StundenEditor : Control
    {
        public List<StundenFeld> Felder { get; }
        public StundenFeld? Ausgewaehlt { get; private set; }
        public event Action<StundenFeld?>? AuswahlGeaendert;
        private PointF _start;
        private StundenFeld? _ziehen;
        private bool _groesse;
        private float? _snapX, _snapY;

        public StundenEditor(List<StundenFeld> felder)
        {
            Felder = felder;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private RectangleF Rect(StundenFeld f) => new(f.X * Width, f.Y * Height, f.B * Width, f.H * Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(235, 240, 248));
            using (var p = new Pen(Color.FromArgb(215, 222, 232))) for (var x = 0; x < Width; x += 10) g.DrawLine(p, x, 0, x, Height);
            using (var p = new Pen(Color.FromArgb(215, 222, 232))) for (var y = 0; y < Height; y += 10) g.DrawLine(p, 0, y, Width, y);
            foreach (var f in Felder)
            {
                var r = Rect(f);
                g.FillRectangle(Brushes.White, r);
                g.DrawRectangle(f == Ausgewaehlt ? Pens.OrangeRed : Pens.Gray, r.X, r.Y, r.Width, r.Height);
                using var font = new Font("Segoe UI", Math.Max(6f, 12f * f.SchriftProzent / 100f), f.Fett ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, $"{f.Zeile}{f.Art}", font, Rectangle.Round(r), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                g.FillRectangle(Brushes.Black, r.Right - 4, r.Bottom - 4, 4, 4);
            }
            using var snap = new Pen(Color.Red) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            if (_snapX.HasValue) g.DrawLine(snap, _snapX.Value, 0, _snapX.Value, Height);
            if (_snapY.HasValue) g.DrawLine(snap, 0, _snapY.Value, Width, _snapY.Value);
        }

        protected override void OnDragEnter(DragEventArgs e) => e.Effect = (e.Data?.GetData(DataFormats.Text) as string)?.StartsWith("feld:") == true ? DragDropEffects.Copy : DragDropEffects.None;

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.Text) is not string s || !Enum.TryParse<StundenFeldArt>(s[5..], out var art)) return;
            var p = PointToClient(new Point(e.X, e.Y));
            var f = new StundenFeld { Art = art, X = Math.Clamp(p.X / (float)Width - 0.2f, 0, 0.6f), Y = Math.Clamp(p.Y / (float)Height - 0.1f, 0, 0.8f), B = 0.4f, H = 0.2f, Zeile = Felder.Count(x => x.Art == art) + 1, Fett = art == StundenFeldArt.Fach };
            Felder.Add(f);
            Ausgewaehlt = f;
            AuswahlGeaendert?.Invoke(f);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Ausgewaehlt = Felder.LastOrDefault(f => Rect(f).Contains(e.Location));
            AuswahlGeaendert?.Invoke(Ausgewaehlt);
            _ziehen = Ausgewaehlt;
            if (_ziehen != null)
            {
                var r = Rect(_ziehen);
                _groesse = e.X > r.Right - 8 && e.Y > r.Bottom - 8;
                _start = new PointF(e.X - r.X, e.Y - r.Y);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_ziehen is null || e.Button != MouseButtons.Left) return;
            _snapX = _snapY = null;
            if (_groesse)
            {
                _ziehen.B = Math.Clamp((e.X - _ziehen.X * Width) / Width, 0.08f, 1 - _ziehen.X);
                _ziehen.H = Math.Clamp((e.Y - _ziehen.Y * Height) / Height, 0.08f, 1 - _ziehen.Y);
            }
            else
            {
                var x = (e.X - _start.X) / Width;
                var y = (e.Y - _start.Y) / Height;
                foreach (var o in Felder.Where(o => o != _ziehen))
                {
                    if (Math.Abs(o.X - x) < 0.02f) { x = o.X; _snapX = o.X * Width; }
                    if (Math.Abs(o.Y - y) < 0.03f) { y = o.Y; _snapY = o.Y * Height; }
                }
                _ziehen.X = Math.Clamp(x, 0, 1 - _ziehen.B);
                _ziehen.Y = Math.Clamp(y, 0, 1 - _ziehen.H);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _ziehen = null;
            _snapX = _snapY = null;
            Invalidate();
        }

        public void Entfernen()
        {
            if (Ausgewaehlt is null) return;
            Felder.Remove(Ausgewaehlt);
            Ausgewaehlt = null;
            AuswahlGeaendert?.Invoke(null);
            Invalidate();
        }
    }
}
