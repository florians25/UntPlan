using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

public class Dialog : Form
{
    public readonly Button Ok = new() { Text = "OK", DialogResult = DialogResult.OK, Width = 88, Height = 26 };
    public readonly Button Abbrechen = new() { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Width = 88, Height = 26 };

    public Dialog(string titel, int breite, int hoehe)
    {
        Text = titel;
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(breite, hoehe);
        AcceptButton = Ok;
        CancelButton = Abbrechen;
        Ok.Anchor = Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        Ok.Location = new Point(breite - 188, hoehe - 36);
        Abbrechen.Location = new Point(breite - 96, hoehe - 36);
        Controls.Add(Ok);
        Controls.Add(Abbrechen);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Ui.SucheAktivieren(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.TitelleisteFaerben(this);
    }
}

/// <summary>
/// Dialog „Kurzname“ wie im Vorbild (Neuanlegen und Umbenennen von Stammdaten): Eingabefelder links, Beschriftung „Name“ bzw.
/// „Langname“ rechts daneben, Ok/Abbrechen unten links.
/// </summary>
public static class KurznameDialog
{
    /// <summary>Für den Selbsttest: zuletzt geöffneter Dialog (nicht modal angezeigt).</summary>
    internal static Form Erzeugen(string kurz, string lang, out TextBox k, out TextBox l)
    {
        var d = new Dialog("Kurzname", 238, 112);
        k = new TextBox { Text = kurz, Left = 12, Top = 14, Width = 120 };
        l = new TextBox { Text = lang, Left = 12, Top = 44, Width = 120 };
        d.Controls.Add(k);
        d.Controls.Add(new Label { Text = "Name", Left = 140, Top = 17, AutoSize = true });
        d.Controls.Add(l);
        d.Controls.Add(new Label { Text = "Langname", Left = 140, Top = 47, AutoSize = true });
        d.Ok.Text = "Ok";
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        d.Ok.Location = new Point(12, 76);
        d.Abbrechen.Location = new Point(106, 76);
        d.Ok.Width = d.Abbrechen.Width = 86;
        d.Ok.Height = d.Abbrechen.Height = 24;
        return d;
    }

    public static (string Kurz, string Lang)? Zeigen(IWin32Window owner, string kurz, string lang)
    {
        using var d = Erzeugen(kurz, lang, out var k, out var l);
        return d.ShowDialog(owner) == DialogResult.OK ? (k.Text.Trim(), l.Text.Trim()) : null;
    }

    /// <summary>Vorschlag für den Namen eines neuen Elements: nächste freie Nummer nach dem Muster des letzten Namens (S67 → S68).</summary>
    public static string Vorschlag(IEnumerable<string> vorhandene)
    {
        var liste = vorhandene.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var letzter = liste.LastOrDefault(n => char.IsDigit(n[^1]));
        if (letzter is null) return "";
        var i = letzter.Length;
        while (i > 0 && char.IsDigit(letzter[i - 1])) i--;
        var basis = letzter[..i];
        var nr = int.TryParse(letzter[i..], out var x) ? x : 0;
        var namen = liste.ToHashSet(StringComparer.OrdinalIgnoreCase);
        do nr++; while (namen.Contains(basis + nr));
        return basis + nr;
    }
}

public static class KommentarDialog
{
    public static (int Kategorie, string Text)? Neu(IWin32Window owner, string titel)
    {
        using var d = new Dialog(titel, 480, 260);
        var t = new TextBox { Multiline = true, Left = 10, Top = 10, Width = 460, Height = 170, ScrollBars = ScrollBars.Vertical };
        var kat = new ComboBox { Left = 10, Top = 192, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, Font = Ui.ListenSchrift, ItemHeight = 18 };
        foreach (var k in AppZustand.D.KommentarKategorien) kat.Items.Add(k);
        kat.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var k = (KommentarKategorie)kat.Items[e.Index]!;
            using var b = new SolidBrush(Ui.Farbe(k.Farbe, Color.Red));
            e.Graphics.FillRectangle(b, e.Bounds.X + 2, e.Bounds.Y + 2, 14, e.Bounds.Height - 4);
            Ui.ListenText(e.Graphics, k.Name, Ui.ListenSchrift, new Rectangle(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 21, e.Bounds.Height), e.ForeColor);
        };
        kat.SelectedIndex = 0;
        d.Controls.Add(t);
        d.Controls.Add(kat);
        return d.ShowDialog(owner) == DialogResult.OK && t.Text.Trim().Length > 0 ? (((KommentarKategorie)kat.SelectedItem!).Nr, t.Text.Trim()) : null;
    }
}

public static class SortierDialog
{
    public static List<Sortierkriterium>? Zeigen(IWin32Window owner, List<Feld> felder, List<Sortierkriterium> bisher, out bool dauerhaft)
    {
        using var d = new Dialog("Sortierkriterien", 420, 330);
        var boxen = new List<(ComboBox Feld, RadioButton Ab)>();
        var y = 10;
        for (var i = 0; i < 5; i++)
        {
            d.Controls.Add(new Label { Text = i == 0 ? "Sortieren nach" : "dann nach", Left = 12, Top = y, Width = 200 });
            var cb = new ComboBox { Left = 12, Top = y + 18, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.Add("--Keine--");
            foreach (var f in felder) cb.Items.Add(f.Titel);
            var auf = new RadioButton { Text = "Aufsteigend", Left = 270, Top = y + 18, Width = 95, Checked = true };
            var ab = new RadioButton { Text = "Absteigend", Left = 270, Top = y + 34, Width = 95 };
            var grp = new Panel { Left = 0, Top = 0, Width = 0, Height = 0 };
            var p = new Panel { Left = 266, Top = y + 14, Width = 140, Height = 40 };
            auf.Left = 4; auf.Top = 2; ab.Left = 4; ab.Top = 20;
            p.Controls.Add(auf);
            p.Controls.Add(ab);
            if (i < bisher.Count)
            {
                var idx = felder.FindIndex(f => f.Schluessel == bisher[i].Feld);
                cb.SelectedIndex = idx + 1;
                ab.Checked = bisher[i].Absteigend;
                auf.Checked = !ab.Checked;
            }
            else cb.SelectedIndex = i == 0 && bisher.Count == 0 ? 1 : 0;
            d.Controls.Add(cb);
            d.Controls.Add(p);
            boxen.Add((cb, ab));
            y += 50;
        }
        var dauer = new CheckBox { Text = "Reihenfolge dauerhaft übernehmen (alle Auswahllisten)", Left = 12, Top = y + 2, Width = 380 };
        d.Controls.Add(dauer);
        var ok = d.ShowDialog(owner) == DialogResult.OK;
        dauerhaft = dauer.Checked;
        if (!ok) return null;
        return boxen.Where(b => b.Feld.SelectedIndex > 0).Select(b => new Sortierkriterium { Feld = felder[b.Feld.SelectedIndex - 1].Schluessel, Absteigend = b.Ab.Checked }).ToList();
    }
}

public static class FelderAuswahlDialog
{
    public static List<string>? Zeigen(IWin32Window owner, List<Feld> felder, List<string> sichtbar)
    {
        using var d = new Dialog("Felder der Ansicht", 340, 420);
        var lb = new CheckedListBox { Left = 10, Top = 10, Width = 320, Height = 360, CheckOnClick = true };
        foreach (var f in felder) lb.Items.Add($"{f.Titel}  ({f.Karteikarte})", sichtbar.Contains(f.Schluessel));
        d.Controls.Add(lb);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        var res = sichtbar.Where(s => lb.CheckedIndices.Cast<int>().Any(i => felder[i].Schluessel == s)).ToList();
        foreach (int i in lb.CheckedIndices) if (!res.Contains(felder[i].Schluessel)) res.Add(felder[i].Schluessel);
        return res;
    }
}

public static class AuswahlDialog
{
    public static Element? Element(IWin32Window owner, ElementTyp typ, string titel)
    {
        using var d = new Dialog(titel, 320, 340);
        var lb = new ListBox { Left = 10, Top = 10, Width = 300, Height = 280 };
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ)) lb.Items.Add(new Eintrag(e));
        if (lb.Items.Count > 0) lb.SelectedIndex = 0;
        lb.DoubleClick += (_, _) => d.DialogResult = DialogResult.OK;
        d.Controls.Add(lb);
        return d.ShowDialog(owner) == DialogResult.OK ? (lb.SelectedItem as Eintrag)?.E : null;
    }

    public static List<Element>? Mehrere(IWin32Window owner, ElementTyp typ, string titel, IEnumerable<int>? vorauswahl = null)
    {
        using var d = new Dialog(titel, 320, 400);
        var lb = new CheckedListBox { Left = 10, Top = 10, Width = 300, Height = 310, CheckOnClick = true };
        var vor = vorauswahl?.ToHashSet() ?? new HashSet<int>();
        foreach (var e in Stammdaten.Sortiert(AppZustand.D, typ)) lb.Items.Add(new Eintrag(e), vorauswahl is null || vor.Contains(e.Id));
        var alle = new Button { Text = "Alle", Left = 10, Top = 330, Width = 70 };
        var keine = new Button { Text = "Keine", Left = 86, Top = 330, Width = 70 };
        alle.Click += (_, _) => { for (var i = 0; i < lb.Items.Count; i++) lb.SetItemChecked(i, true); };
        keine.Click += (_, _) => { for (var i = 0; i < lb.Items.Count; i++) lb.SetItemChecked(i, false); };
        d.Controls.AddRange(new Control[] { lb, alle, keine });
        return d.ShowDialog(owner) == DialogResult.OK ? lb.CheckedItems.Cast<Eintrag>().Select(x => x.E).ToList() : null;
    }

    private sealed record Eintrag(Element E)
    {
        public override string ToString() => Ui.ElementName(E);
    }
}

/// <summary>Neue Schuldatei: Schulname, Schulform (NRW), Schuljahr – oder Beispielschule.</summary>
public sealed class NeueDateiDialog : Dialog
{
    private readonly TextBox _name = new() { Text = "Neue Schule" };
    private readonly ComboBox _form = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _leer = new() { Text = "Leere Schuldatei (NRW-Vorgaben werden eingetragen)", Checked = true, Width = 380 };
    private readonly RadioButton _demo = new() { Text = "Beispielschule (Gymnasium mit Daten)", Width = 380 };
    private readonly CheckBox _assistent = new() { Text = "Anschließend Schuldaten eingeben", Checked = true, Width = 300 };

    public Schuldatei? Ergebnis { get; private set; }
    public bool AssistentStarten => _assistent.Checked && _leer.Checked;

    public NeueDateiDialog() : base("Neue Schuldatei", 430, 250)
    {
        var y = 12;
        _leer.Left = 12; _leer.Top = y; Controls.Add(_leer); y += 26;
        _demo.Left = 12; _demo.Top = y; Controls.Add(_demo); y += 34;
        Ui.Zeile(this, "Schulbezeichnung", _name, ref y, 130, 270);
        foreach (var (k, v) in Nrw.Schulformen) _form.Items.Add($"{k} – {v}");
        _form.SelectedIndex = Nrw.Schulformen.Keys.ToList().IndexOf("GY");
        Ui.Zeile(this, "Schulform (NRW)", _form, ref y, 130, 270);
        _assistent.Left = 12; _assistent.Top = y + 4; Controls.Add(_assistent);
        _demo.CheckedChanged += (_, _) => { _name.Enabled = _form.Enabled = !_demo.Checked; };
        Ok.Click += (_, _) =>
        {
            Ergebnis = _demo.Checked ? Demo.Beispielschule() : Demo.NeueDatei(_name.Text.Trim(), Nrw.Schulformen.Keys.ElementAt(_form.SelectedIndex));
        };
    }
}
