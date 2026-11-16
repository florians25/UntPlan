using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Handbuch 6.7 / 7.3.2: Datei | Import/Export | Formate/Fenstergruppen. Karteikarte „Formate Export“: Stundenplanformate (Auswahl),
/// Stammdatenformate und Fenstergruppen in eine Formatdatei (.upf) schreiben. Karteikarte „Formate Import“: Formate und Fenstergruppen
/// aus einer Formatdatei oder direkt aus einer anderen Schuldatei übernehmen.
/// </summary>
public sealed class FormatAustauschDialog : Dialog
{
    private Schuldatei D => AppZustand.D;
    private readonly TabControl _tabs = new() { Left = 8, Top = 8, Width = 524, Height = 372 };

    public FormatAustauschDialog(bool import = false) : base("Formate/Fenstergruppen", 540, 424)
    {
        Ok.Visible = false;
        Abbrechen.Text = "Schließen";
        _tabs.TabPages.Add(ExportSeite());
        _tabs.TabPages.Add(ImportSeite());
        if (import) _tabs.SelectedIndex = 1;
        Controls.Add(_tabs);
    }

    public static void Zeigen(IWin32Window owner, bool import = false)
    {
        using var d = new FormatAustauschDialog(import);
        d.ShowDialog(owner);
    }

    private static string Beschriftung(StundenplanFormat f) => $"{(f.Kurzname.Length > 0 ? f.Kurzname + " – " : "")}{f.Name} ({f.Typ})";

    private TabPage ExportSeite()
    {
        var s = new TabPage("Formate Export");
        s.Controls.Add(new Label { Text = "Stundenplanformate", Left = 8, Top = 8, AutoSize = true });
        var liste = new CheckedListBox { Left = 8, Top = 26, Width = 300, Height = 220, CheckOnClick = true, IntegralHeight = false };
        foreach (var f in D.Formate.OrderBy(f => f.Typ).ThenBy(f => f.Name)) liste.Items.Add(new Eintrag(f), true);
        var alle = new Button { Text = "Alle", Left = 316, Top = 26, Width = 90, Height = 24 };
        var keine = new Button { Text = "Keine", Left = 316, Top = 54, Width = 90, Height = 24 };
        alle.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, true); };
        keine.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, false); };
        var stamm = new CheckBox { Text = "Stammdatenformate", Left = 8, Top = 254, AutoSize = true, Checked = true };
        var fg = new CheckBox { Text = "Fenstergruppen (Fensteranordnungen)", Left = 8, Top = 276, AutoSize = true, Checked = true };
        var export = new Button { Text = "Export…", Left = 8, Top = 306, Width = 110, Height = 26 };
        export.Click += (_, _) =>
        {
            var auswahl = liste.CheckedItems.Cast<Eintrag>().Select(e => e.Format).ToList();
            if (auswahl.Count == 0 && !stamm.Checked && !fg.Checked) { Ui.Info("Bitte zuerst auswählen, was exportiert werden soll."); return; }
            using var sfd = new SaveFileDialog
            {
                Filter = $"UntPlan-Formate (*{FormatAustausch.Dateiendung})|*{FormatAustausch.Dateiendung}", FileName = "Neu" + FormatAustausch.Dateiendung,
                InitialDirectory = Einstellungsseiten.Eingetragen("Import/Export") ?? "", Title = "Formate exportieren",
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;
            var gruppen = fg.Checked ? JsonSerializer.SerializeToNode(AppZustand.Einstellungen.Fensteranordnungen) as JsonObject : null;
            var paket = FormatAustausch.Erstellen(D, auswahl, stamm.Checked, gruppen);
            File.WriteAllText(sfd.FileName, FormatAustausch.AlsJson(paket));
            Ui.Info($"Exportiert: {paket.Stundenplanformate.Count} Stundenplanformat(e), {paket.Stammdatenformate.Count} Stammdatenformat(e), " +
                    $"{paket.Fenstergruppen?.Count ?? 0} Fenstergruppe(n).\n\n{sfd.FileName}", "Formate exportieren");
        };
        s.Controls.AddRange(new Control[] { liste, alle, keine, stamm, fg, export });
        s.Controls.Add(new Label { Text = "Die Formatdatei enthält keine Schuldaten und kann an andere Benutzer weitergegeben werden.", Left = 130, Top = 306, Width = 370, Height = 34, ForeColor = Color.DimGray });
        return s;
    }

    private TabPage ImportSeite()
    {
        var s = new TabPage("Formate Import");
        var datei = new TextBox { Left = 8, Top = 10, Width = 390, ReadOnly = true };
        var waehlen = new Button { Text = "Datei…", Left = 406, Top = 8, Width = 96, Height = 25 };
        s.Controls.Add(new Label { Text = "Stundenplanformate der Datei", Left = 8, Top = 40, AutoSize = true });
        var liste = new CheckedListBox { Left = 8, Top = 58, Width = 300, Height = 190, CheckOnClick = true, IntegralHeight = false };
        var stamm = new CheckBox { Text = "Stammdatenformate", Left = 8, Top = 254, AutoSize = true, Checked = true, Enabled = false };
        var fg = new CheckBox { Text = "Fenstergruppen (Fensteranordnungen)", Left = 8, Top = 276, AutoSize = true, Checked = true, Enabled = false };
        var import = new Button { Text = "Import", Left = 8, Top = 306, Width = 110, Height = 26, Enabled = false };
        var info = new Label { Left = 316, Top = 58, Width = 190, Height = 190, ForeColor = Color.DimGray };
        Formatpaket? paket = null;
        waehlen.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = $"Formate oder Schuldatei (*{FormatAustausch.Dateiendung};*{Dokument.Dateiendung})|*{FormatAustausch.Dateiendung};*{Dokument.Dateiendung}|Alle Dateien|*.*",
                InitialDirectory = Einstellungsseiten.Eingetragen("Import/Export") ?? "", Title = "Formate importieren",
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            try { paket = FormatAustausch.Lesen(File.ReadAllText(ofd.FileName)); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { Ui.Fehler("Die Datei kann nicht gelesen werden: " + ex.Message); return; }
            datei.Text = ofd.FileName;
            liste.Items.Clear();
            foreach (var f in paket.Stundenplanformate.OrderBy(f => f.Typ).ThenBy(f => f.Name)) liste.Items.Add(new Eintrag(f), true);
            stamm.Enabled = paket.Stammdatenformate.Count > 0;
            fg.Enabled = paket.Fenstergruppen is { Count: > 0 };
            import.Enabled = true;
            var gleich = paket.Stundenplanformate.Count(f => D.Formate.Any(x => x.Typ == f.Typ && x.Name == f.Name));
            info.Text = $"{paket.Stundenplanformate.Count} Stundenplanformat(e)\n{paket.Stammdatenformate.Count} Stammdatenformat(e)\n{paket.Fenstergruppen?.Count ?? 0} Fenstergruppe(n)" +
                        (gleich > 0 ? $"\n\n{gleich} gleichnamige(s) Format(e) werden ersetzt." : "");
        };
        import.Click += (_, _) =>
        {
            if (paket is null) return;
            var namen = liste.CheckedItems.Cast<Eintrag>().Select(e => e.Format.Name).ToList();
            var p = paket;
            var ergebnis = (Neu: 0, Ersetzt: 0);
            AppZustand.Doc.Aendern("Formate importieren", AenderungsArt.Einstellungen, d => ergebnis = FormatAustausch.Einspielen(d, p, namen, stamm.Enabled && stamm.Checked));
            var gruppen = 0;
            if (fg.Enabled && fg.Checked && p.Fenstergruppen is { } json
                && json.Deserialize<Dictionary<string, Fensteranordnung>>() is { } neu)
            {
                foreach (var (k, v) in neu) AppZustand.Einstellungen.Fensteranordnungen[k] = v;
                AppZustand.Einstellungen.Speichern();
                gruppen = neu.Count;
            }
            Ui.Info($"Importiert: {ergebnis.Neu} neu, {ergebnis.Ersetzt} ersetzt" + (gruppen > 0 ? $", {gruppen} Fenstergruppe(n)" : "") + ".", "Formate importieren");
        };
        s.Controls.AddRange(new Control[] { datei, waehlen, liste, stamm, fg, import, info });
        s.Controls.Add(new Label { Text = "Import aus einer Formatdatei (.upf) oder direkt aus einer anderen UntPlan-Datei.", Left = 130, Top = 306, Width = 370, Height = 34, ForeColor = Color.DimGray });
        return s;
    }

    private sealed record Eintrag(StundenplanFormat Format)
    {
        public override string ToString() => Beschriftung(Format);
    }
}
