using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>„Neues Format“ wie im Handbuch (Kap. 13.1.1): Name (kurz) und Langname, Ok / Abbrechen.</summary>
public static class NeuesFormatDialog
{
    internal static Dialog Erzeugen(string name, string langname, out TextBox n, out TextBox l)
    {
        var d = new Dialog("Neues Format", 360, 128);
        n = new TextBox { Text = name, Left = 12, Top = 16, Width = 92 };
        l = new TextBox { Text = langname, Left = 12, Top = 50, Width = 204 };
        d.Controls.AddRange(new Control[] { n, new Label { Text = "Name", Left = 112, Top = 19, AutoSize = true }, l, new Label { Text = "Langname", Left = 224, Top = 53, AutoSize = true } });
        d.Ok.Text = "Ok";
        return d;
    }

    public static (string Name, string Langname)? Zeigen(IWin32Window owner, string name, string langname)
    {
        using var d = Erzeugen(name, langname, out var n, out var l);
        return d.ShowDialog(owner) == DialogResult.OK ? (n.Text.Trim(), l.Text.Trim()) : null;
    }
}

/// <summary>Vertretungsnachrichten an die Lehrkräfte: Liste der Nachrichten (je Lehrkraft), Vorschau, per E-Mail-Programm senden.</summary>
public static class VertretungsnachrichtenDialog
{
    public static void Zeigen(IWin32Window owner, DateOnly von, DateOnly bis)
    {
        var d = AppZustand.D;
        var einst = AppZustand.Einstellungen;
        using var f = new Dialog($"Vertretungsnachrichten {Format.Datum(von)}{(bis > von ? " – " + Format.Datum(bis) : "")}", 720, 470);
        f.Ok.Text = "Senden";
        var nurGeaendert = new CheckBox { Text = "Nur seit der letzten Nachricht geänderte Vertretungen" + (einst.NachrichtenZuletzt is DateTime z ? $" ({z:dd.MM. HH:mm})" : ""), Left = 10, Top = 8, AutoSize = true, Checked = einst.NachrichtenNurGeaenderte };
        nurGeaendert.Font = new Font(Ui.Schrift, FontStyle.Italic); // lokal gespeichert
        var liste = new DataGridView { Left = 10, Top = 34, Width = 250, Height = 380 };
        Ui.RasterGestalten(liste, false);
        liste.RowHeadersVisible = false;
        liste.Columns.Add(new DataGridViewCheckBoxColumn { Name = "S", HeaderText = "senden", Width = 50 });
        liste.Columns.Add(new DataGridViewTextBoxColumn { Name = "L", HeaderText = "Lehrer", Width = 70, ReadOnly = true });
        liste.Columns.Add(new DataGridViewTextBoxColumn { Name = "N", HeaderText = "Anzahl", Width = 50, ReadOnly = true });
        liste.Columns.Add(new DataGridViewTextBoxColumn { Name = "E", HeaderText = "E-Mail", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        var text = new TextBox { Left = 270, Top = 34, Width = 440, Height = 380, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
        f.Controls.AddRange(new Control[] { nurGeaendert, liste, text });
        List<Vertretungsnachricht> nachrichten = new();
        void Fuellen()
        {
            nachrichten = Vertretungsnachrichten.Erzeugen(d, von, bis, nurGeaendert.Checked ? einst.NachrichtenZuletzt : null);
            liste.Rows.Clear();
            foreach (var n in nachrichten)
            {
                var i = liste.Rows.Add(n.Email.Length > 0, n.Kurzname, n.Anzahl, n.Email.Length > 0 ? n.Email : "(keine E-Mail-Adresse)");
                liste.Rows[i].Tag = n;
                if (n.Email.Length == 0) { liste.Rows[i].Cells["S"].ReadOnly = true; liste.Rows[i].DefaultCellStyle.ForeColor = Color.Gray; }
            }
            text.Text = nachrichten.Count == 0 ? "Keine Vertretungen im Zeitraum." : "";
        }
        liste.SelectionChanged += (_, _) => { if (liste.CurrentRow?.Tag is Vertretungsnachricht n) text.Text = $"An: {n.Email}\r\nBetreff: {n.Betreff}\r\n\r\n{n.Text.Replace("\n", "\r\n")}"; };
        nurGeaendert.CheckedChanged += (_, _) => { einst.NachrichtenNurGeaenderte = nurGeaendert.Checked; einst.Speichern(); Fuellen(); };
        Fuellen();
        if (f.ShowDialog(owner) != DialogResult.OK) return;
        liste.EndEdit();
        var senden = liste.Rows.Cast<DataGridViewRow>().Where(r => r.Cells["S"].Value is true).Select(r => (Vertretungsnachricht)r.Tag!).ToList();
        if (senden.Count == 0) return;
        if (senden.Count > 5 && !Ui.Frage($"{senden.Count} Nachrichten im E-Mail-Programm öffnen?")) return;
        foreach (var n in senden) HauptFenster.Starten(Vertretungsnachrichten.MailTo(n));
        einst.NachrichtenZuletzt = DateTime.Now;
        einst.Speichern();
    }
}

/// <summary>
/// „Import Vertretungsplanung“ wie im Handbuch (Kap. 16.7): Datei wählen, „Additiver Vertretungsimport“ (vorhandene Vertretungen
/// beibehalten) und die Elementzuordnung (Elemente der Quelldatei → Elemente der aktuellen Datei, vorbelegt über den Kurznamen).
/// </summary>
public static class VertretungsimportDialog
{
    public static void Zeigen(IWin32Window owner)
    {
        using var ofd = new OpenFileDialog { Filter = "UntPlan-Schuldatei (*.up)|*.up|Alle Dateien|*.*", Title = "Import Vertretungsplanung – Datei wählen" };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        Schuldatei quelle;
        try
        {
            if (Dateisperre.PfadGleich(ofd.FileName, AppZustand.Doc.Pfad)) { Ui.Info("Das ist die aktuell geöffnete Datei."); return; }
            quelle = Dokument.Laden(ofd.FileName).Daten;
            Dokument.Normalisieren(quelle);
        }
        catch (Exception ex) { Ui.Fehler(ex); return; }
        var ziel = AppZustand.D;
        var zuordnung = Vertretungsimport.Zuordnung(ziel, quelle);

        using var f = new Dialog("Import Vertretungsplanung", 620, 520);
        f.Ok.Text = "Importieren";
        f.Controls.Add(new Label { Text = "Datei:", Left = 10, Top = 12, AutoSize = true });
        f.Controls.Add(new TextBox { Text = ofd.FileName, Left = 60, Top = 9, Width = 550, ReadOnly = true });
        var additiv = new CheckBox { Text = "Additiver Vertretungsimport (vorhandene Vertretungsdaten beibehalten)", Left = 10, Top = 40, AutoSize = true, Checked = true };
        f.Controls.Add(additiv);
        f.Controls.Add(new Label
        {
            Text = $"Übernommen werden alle Daten zur Vertretungsplanung: {quelle.Abwesenheiten.Count} Absenzen, {quelle.Veranstaltungen.Count} Veranstaltungen, {quelle.Klausuren.Count} Klausuren, " +
                   $"{quelle.Vertretungen.Count} Vertretungen (auch Verlegungen und Sondereinsätze), {quelle.Tagestexte.Count} Tagestexte, {quelle.Vormerkungen.Count} Vormerkungen.",
            Left = 10, Top = 64, Width = 600, Height = 34, ForeColor = Color.DimGray,
        });
        f.Controls.Add(new Label { Text = "Elementzuordnung (Quelldatei → aktuelle Datei):", Left = 10, Top = 102, AutoSize = true, Font = Ui.SchriftFett });
        var nurOffen = new CheckBox { Text = "Nur nicht zugeordnete", Left = 440, Top = 100, AutoSize = true };
        f.Controls.Add(nurOffen);
        var g = new DataGridView { Left = 10, Top = 124, Width = 600, Height = 346 };
        Ui.RasterGestalten(g, false);
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Art", HeaderText = "Art", Width = 90, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Q", HeaderText = "Quelldatei", Width = 90, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "QL", HeaderText = "Langname", Width = 160, ReadOnly = true });
        var zielSpalte = new DataGridViewComboBoxColumn { Name = "Z", HeaderText = "aktuelle Datei", Width = 200, FlatStyle = FlatStyle.Flat, DisplayStyleForCurrentCellOnly = true };
        g.Columns.Add(zielSpalte);
        g.DataError += (_, e) => e.ThrowException = false;
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        const string Keine = "(nicht übernehmen)";
        string ArtText(ElementTyp t) => t switch { ElementTyp.Abwesenheitsgrund => "Absenzgrund", _ => Ui.TypName(t, false) };
        void Fuellen()
        {
            g.Rows.Clear();
            foreach (var z in zuordnung.Where(z => !nurOffen.Checked || z.ZielId is null))
            {
                var namen = new List<string> { Keine };
                namen.AddRange(Stammdaten.Sortiert(ziel, z.Typ).Select(e => e.Kurzname));
                var i = g.Rows.Add(ArtText(z.Typ), z.Kurzname, z.Langname, null);
                var zelle = (DataGridViewComboBoxCell)g.Rows[i].Cells["Z"];
                zelle.Items.AddRange(namen.ToArray());
                zelle.Value = z.ZielId is int id ? ziel.AlleElemente(z.Typ).FirstOrDefault(e => e.Id == id)?.Kurzname ?? Keine : Keine;
                g.Rows[i].Tag = z;
                if (z.ZielId is null) g.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 236, 225);
            }
        }
        g.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || g.Rows[e.RowIndex].Tag is not ElementZuordnung z) return;
            var name = g.Rows[e.RowIndex].Cells["Z"].Value as string;
            z.ZielId = name is null || name == Keine ? null : ziel.AlleElemente(z.Typ).FirstOrDefault(x => x.Kurzname == name)?.Id;
            g.Rows[e.RowIndex].DefaultCellStyle.BackColor = z.ZielId is null ? Color.FromArgb(255, 236, 225) : Color.White;
        };
        nurOffen.CheckedChanged += (_, _) => Fuellen();
        f.Controls.Add(g);
        Fuellen();
        if (f.ShowDialog(owner) != DialogResult.OK) return;
        if (!additiv.Checked && !Ui.Frage("Ohne „Additiver Vertretungsimport“ werden alle vorhandenen Vertretungsdaten dieser Datei ersetzt. Fortfahren?")) return;
        try
        {
            VertretungsimportErgebnis? erg = null;
            AppZustand.Doc.Aendern("Import Vertretungsplanung", AenderungsArt.Alles, d => erg = Vertretungsimport.Importieren(d, quelle, additiv.Checked, zuordnung));
            if (erg is null) return;
            var text = $"Importiert: {erg.Absenzen} Absenzen, {erg.Veranstaltungen} Veranstaltungen, {erg.Klausuren} Klausuren, {erg.Vertretungen} Vertretungen, " +
                       $"{erg.Tagestexte} Tagestexte, {erg.Vormerkungen} Vormerkungen." + (erg.Uebersprungen > 0 ? $"\n{erg.Uebersprungen} bereits vorhandene Einträge übersprungen." : "") +
                       (erg.Hinweise.Count > 0 ? "\n\nHinweise:\n" + string.Join("\n", erg.Hinweise.Take(20)) + (erg.Hinweise.Count > 20 ? "\n…" : "") : "");
            Ui.Info(text, "Import Vertretungsplanung");
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }
}

/// <summary>
/// „Berichts-Einstellungen“ wie im Handbuch (Kap. 13.2.2): Reiter „Überschriften“ (Überschrift mit zwei Zeilen, Datum, Uhrzeit,
/// Stundenplan-Version, waagrechte Linie; Fußzeile mit Text, Dateiname, Datum, Schulwoche, Kalenderwoche, Wochennummer, Periode,
/// waagrechte Linie; Seiten-Nummer und Anzahl Seiten) und „Drucknamen“. Gilt für alle Berichte.
/// </summary>
public static class BerichtseinstellungenDialog
{
    public static bool Zeigen(IWin32Window owner)
    {
        var b = AppZustand.Einstellungen.Berichte;
        var d = AppZustand.D;
        using var f = new Dialog("Berichts-Einstellungen", 500, 360);
        f.Ok.Text = "Ok";
        var tabs = new TabControl { Left = 8, Top = 8, Width = 484, Height = 306 };
        var tu = new TabPage("Überschriften") { BackColor = Ui.BlauSehrHell };
        var td = new TabPage("Drucknamen") { BackColor = Ui.BlauSehrHell };
        tabs.TabPages.AddRange(new[] { tu, td });
        f.Controls.Add(tabs);

        var gU = new GroupBox { Text = "Überschrift", Left = 8, Top = 6, Width = 226, Height = 220 };
        var u1 = new TextBox { Text = b.Ueberschrift1.Length > 0 ? b.Ueberschrift1 : d.Einstellungen.Schulname, Left = 8, Top = 20, Width = 208 };
        var u2 = new TextBox { Text = b.Ueberschrift2.Length > 0 ? b.Ueberschrift2 : d.Einstellungen.Adresse, Left = 8, Top = 50, Width = 208 };
        CheckBox H(Control p, string t, bool an, int y, int x = 8) { var c = new CheckBox { Text = t, Checked = an, Left = x, Top = y, AutoSize = true }; p.Controls.Add(c); return c; }
        gU.Controls.AddRange(new Control[] { u1, u2 });
        var cDatum = H(gU, "Datum drucken", b.DatumDrucken, 80);
        var cZeit = H(gU, "Uhrzeit drucken", b.UhrzeitDrucken, 100);
        var cVers = H(gU, "Stundenplan-Version drucken", b.VersionDrucken, 120);
        var cLinieK = H(gU, "Waagrechte Linie drucken", b.LinieKopf, 140);
        var version = new NumericUpDown { Minimum = 0, Maximum = 9999, Value = Math.Clamp(b.Version, 0, 9999), Left = 8, Top = 176, Width = 50 };
        gU.Controls.AddRange(new Control[] { version, new Label { Text = "Stundenplan-Version", Left = 66, Top = 179, AutoSize = true } });
        tu.Controls.Add(gU);
        var cSeite = H(tu, "Seiten-Nummer drucken", b.SeitenNummer, 232, 10);
        var cAnzahl = H(tu, "Anzahl Seiten drucken", b.AnzahlSeiten, 252, 10);
        cAnzahl.Enabled = cSeite.Checked;
        cSeite.CheckedChanged += (_, _) => cAnzahl.Enabled = cSeite.Checked;

        var gF = new GroupBox { Text = "Fußzeile", Left = 242, Top = 6, Width = 226, Height = 196 };
        var fz = new TextBox { Text = b.Fusszeile.Length > 0 ? b.Fusszeile : "UntPlan " + DateTime.Now.Year, Left = 8, Top = 20, Width = 208 };
        gF.Controls.Add(fz);
        var cFDatei = H(gF, "Dateiname", b.FussDateiname, 48);
        var cFDatum = H(gF, "Datum", b.FussDatum, 68);
        var cFSw = H(gF, "Schulwoche", b.FussSchulwoche, 88);
        var cFKw = H(gF, "Kalenderwoche", b.FussKalenderwoche, 108);
        var cFWn = H(gF, "Wochennummer (A, B, ...)", b.FussWochennummer, 128);
        var cFPer = H(gF, "Periode", b.FussPeriode, 148);
        var cLinieF = H(gF, "Waagrechte Linie drucken", b.LinieFuss, 168);
        tu.Controls.Add(gF);

        td.Controls.Add(new Label { Text = "Drucknamen der Lehrer (statt Langname in Berichten):", Left = 10, Top = 14, AutoSize = true });
        var dn = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 36, Width = 240 };
        foreach (var a in Berichtseinstellungen.DrucknamenArten) dn.Items.Add(a.Length == 0 ? "Langname" : a);
        dn.SelectedIndex = Math.Max(0, Array.IndexOf(Berichtseinstellungen.DrucknamenArten, b.DrucknameLehrer));
        var beispiel = new Label { Left = 10, Top = 68, Width = 440, Height = 20, ForeColor = Color.DimGray };
        void Beispiel()
        {
            var probe = new Berichtseinstellungen { DrucknameLehrer = Berichtseinstellungen.DrucknamenArten[Math.Max(0, dn.SelectedIndex)] };
            beispiel.Text = d.Lehrer.FirstOrDefault() is { } l ? "Beispiel: " + probe.Druckname(l) : "";
        }
        dn.SelectedIndexChanged += (_, _) => Beispiel();
        Beispiel();
        td.Controls.AddRange(new Control[] { dn, beispiel });
        f.Controls.Add(new Label { Text = "Diese Einstellungen gelten für alle Berichte.", Left = 10, Top = 330, AutoSize = true, ForeColor = Color.DimGray });

        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        b.Ueberschrift1 = u1.Text.Trim() == d.Einstellungen.Schulname ? "" : u1.Text.Trim();
        b.Ueberschrift2 = u2.Text.Trim() == d.Einstellungen.Adresse ? "" : u2.Text.Trim();
        b.DatumDrucken = cDatum.Checked;
        b.UhrzeitDrucken = cZeit.Checked;
        b.VersionDrucken = cVers.Checked;
        b.LinieKopf = cLinieK.Checked;
        b.Version = (int)version.Value;
        b.SeitenNummer = cSeite.Checked;
        b.AnzahlSeiten = cAnzahl.Checked;
        b.Fusszeile = fz.Text.Trim() == "UntPlan " + DateTime.Now.Year ? "" : fz.Text.Trim();
        b.FussDateiname = cFDatei.Checked;
        b.FussDatum = cFDatum.Checked;
        b.FussSchulwoche = cFSw.Checked;
        b.FussKalenderwoche = cFKw.Checked;
        b.FussWochennummer = cFWn.Checked;
        b.FussPeriode = cFPer.Checked;
        b.LinieFuss = cLinieF.Checked;
        b.DrucknameLehrer = Berichtseinstellungen.DrucknamenArten[Math.Max(0, dn.SelectedIndex)];
        AppZustand.Einstellungen.Speichern();
        return true;
    }
}
