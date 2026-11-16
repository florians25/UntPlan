using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>
/// Druck der Unterrichtsansichten (Handbuch Kap. 3.6) über die Druckauswahl: mehrere Elemente, „1 Seite / Element“,
/// „Unterricht des Elements“, „Unterricht zusammenfassen“, „Bilanzzeile (Ist-Soll)“, „Anrechnungen“ und als zweite Auswahl
/// die Stundenwünsche (Zeitwünsche) der Unterrichte. Spaltenauswahl, Format und Schrift kommen aus dem Seitenlayout.
/// </summary>
public static class UnterrichtDruck
{
    public sealed record Optionen(bool SeiteJeElement, bool NurElement, bool Zusammenfassen, bool Bilanzzeile, bool Anrechnungen, bool Stundenwuensche);

    /// <summary>Ein Abschnitt (ein Element): Überschrift, Tabellenzeilen und Zusatzzeilen (Bilanz, Anrechnungen).</summary>
    public sealed record Abschnitt(string Titel, List<string[]> Zeilen, List<string> Zusatz);

    /// <summary>Schlüssel des gespeicherten Seitenlayouts für die Unterrichtsansicht des Elementtyps.</summary>
    public static string LayoutTitel(ElementTyp typ) => $"Unterricht {Ui.TypName(typ, true)}";

    /// <summary>Zusatzzeilen eines Lehrers: Bilanzzeile (Ist – Soll) und Anrechnungen (Ermäßigungen).</summary>
    public static List<string> Zusatzzeilen(Schuldatei d, DatenIndex ix, Element el, Optionen o)
    {
        var res = new List<string>();
        if (el is not Lehrer l) return res;
        if (o.Bilanzzeile)
        {
            var ist = Core.Module.Wertrechnung.Ist(d, ix, l);
            res.Add($"Bilanz: Ist {Format.Zahl(ist)} – Soll {Format.Zahl(l.Soll)} = {Format.Zahl(ist - l.Soll)}");
        }
        if (o.Anrechnungen)
            foreach (var e in l.Ermaessigungen)
                res.Add($"Anrechnung: {e.Grund} {Format.Zahl(e.Wert)}{(e.Text.Length > 0 ? " – " + e.Text : "")}");
        return res;
    }

    /// <summary>Druck bzw. Seitenansicht aller Abschnitte.</summary>
    public static void Drucken(IWin32Window owner, string titel, string[] kopf, int[] breiten, List<Abschnitt> abschnitte, bool seiteJeElement, bool vorschau)
    {
        var layout = ListenDruck.LayoutFuer(titel);
        LayoutAbgleichen(layout, kopf, breiten);
        var (k, b, idx) = Auswahl(layout, kopf, breiten);
        var posten = PostenListe(abschnitte, idx, seiteJeElement);
        var doc = new PrintDocument { DocumentName = titel };
        doc.DefaultPageSettings.Landscape = layout.Querformat;
        doc.DefaultPageSettings.Margins = new Margins(40, 40, 50, 50);
        var pos = 0;
        var seite = 0;
        doc.BeginPrint += (_, _) => { pos = 0; seite = 0; };
        doc.PrintPage += (s, e) => e.HasMorePages = SeiteZeichnen(e.Graphics!, ListenDruck.Seitenbereich(s, e), layout, titel, k, b, posten, ref pos, ++seite);
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>„Ausdruck bearbeiten“: Seitenlayout (Spalten, Format, Schrift) mit der Vorschau des ersten Abschnitts.</summary>
    public static void AusdruckBearbeiten(IWin32Window owner, string titel, string[] kopf, int[] breiten, List<string[]> beispiel)
    {
        var layout = ListenDruck.LayoutFuer(titel);
        LayoutAbgleichen(layout, kopf, breiten);
        if (ListenDruck.LayoutDialog(owner, layout, kopf, breiten, beispiel, null)) AppZustand.Einstellungen.Speichern();
    }

    private static void LayoutAbgleichen(ListenDruck.Layout layout, string[] kopf, int[] breiten)
    {
        if (layout.Spalten.Count == 0 || layout.Spalten.Select(x => x.Name).Except(kopf).Any() || kopf.Except(layout.Spalten.Select(x => x.Name)).Any())
            layout.Spalten = kopf.Select((k, i) => new ListenDruck.DruckSpalte { Name = k, Breite = breiten[i] }).ToList();
    }

    private static (string[] Kopf, int[] Breiten, int[] Index) Auswahl(ListenDruck.Layout layout, string[] kopf, int[] breiten)
    {
        var a = layout.Spalten.Where(x => x.Drucken).Select(x => (I: Array.IndexOf(kopf, x.Name), x.Breite)).Where(x => x.I >= 0).ToList();
        if (a.Count == 0) return (kopf, breiten, Enumerable.Range(0, kopf.Length).ToArray());
        return (a.Select(x => kopf[x.I]).ToArray(), a.Select(x => Math.Max(20, x.Breite)).ToArray(), a.Select(x => x.I).ToArray());
    }

    private enum PostenArt { Abschnitt, Zeile, Text, Seitenwechsel }

    private sealed record Posten(PostenArt Art, string[] Zellen, string Text);

    private static List<Posten> PostenListe(List<Abschnitt> abschnitte, int[] idx, bool seiteJeElement)
    {
        var l = new List<Posten>();
        foreach (var a in abschnitte)
        {
            if (seiteJeElement && l.Count > 0) l.Add(new Posten(PostenArt.Seitenwechsel, Array.Empty<string>(), ""));
            l.Add(new Posten(PostenArt.Abschnitt, Array.Empty<string>(), a.Titel));
            foreach (var z in a.Zeilen) l.Add(new Posten(PostenArt.Zeile, idx.Select(i => z.ElementAtOrDefault(i) ?? "").ToArray(), ""));
            foreach (var t in a.Zusatz) l.Add(new Posten(PostenArt.Text, Array.Empty<string>(), t));
        }
        return l;
    }

    private static bool SeiteZeichnen(Graphics g, Rectangle r, ListenDruck.Layout layout, string titel, string[] kopf, int[] breiten, List<Posten> posten, ref int pos, int seite)
    {
        using var f = new Font("Segoe UI", layout.Schriftgroesse);
        using var fb = new Font("Segoe UI", layout.Schriftgroesse, FontStyle.Bold);
        using var fa = new Font("Segoe UI", layout.Schriftgroesse + 2, FontStyle.Bold);
        using var ft = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        var y = (float)r.Top;
        g.DrawString(string.IsNullOrWhiteSpace(layout.Ueberschrift) ? titel : layout.Ueberschrift, ft, Brushes.Black, r.Left, y);
        g.DrawString($"{AppZustand.D.Einstellungen.Schulname}   {DateTime.Now:dd.MM.yyyy HH:mm}   Seite {seite}", f, Brushes.DimGray, r.Left, y + 22);
        y += 46;
        var gesamt = Math.Max(1, breiten.Sum());
        var faktor = Math.Min(1f, r.Width / (float)gesamt);
        var hz = f.GetHeight(g) + 4;
        var fuss = string.IsNullOrWhiteSpace(layout.Fusszeile) ? 0 : hz + 4;
        var unten = r.Bottom - fuss;
        void Kopf()
        {
            float x = r.Left;
            g.FillRectangle(Brushes.Gainsboro, r.Left, y, gesamt * faktor, hz);
            for (var i = 0; i < kopf.Length; i++)
            {
                g.DrawString(kopf[i], fb, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), sf);
                x += breiten[i] * faktor;
            }
            y += hz;
        }
        // Fortsetzung eines Abschnitts auf der neuen Seite: Spaltenköpfe wiederholen
        if (pos < posten.Count && posten[pos].Art is PostenArt.Zeile or PostenArt.Text) Kopf();
        var nr = 0;
        var erster = true;
        while (pos < posten.Count)
        {
            var p = posten[pos];
            if (p.Art == PostenArt.Seitenwechsel)
            {
                pos++;
                if (!erster) break;
                continue;
            }
            if (p.Art == PostenArt.Abschnitt)
            {
                // Überschrift nicht allein unten auf der Seite
                if (!erster && y + hz * 4 > unten) break;
                if (!erster) y += hz * 0.6f;
                g.DrawString(p.Text, fa, Brushes.Black, r.Left, y);
                y += fa.GetHeight(g) + 2;
                Kopf();
                nr = 0;
            }
            else
            {
                if (y + hz > unten) break;
                if (p.Art == PostenArt.Zeile)
                {
                    if (layout.Zebra && nr++ % 2 == 1) g.FillRectangle(Brushes.WhiteSmoke, r.Left, y, gesamt * faktor, hz);
                    float x = r.Left;
                    for (var i = 0; i < kopf.Length; i++)
                    {
                        g.DrawString(p.Zellen.ElementAtOrDefault(i) ?? "", f, Brushes.Black, new RectangleF(x + 2, y + 2, breiten[i] * faktor - 4, hz), sf);
                        x += breiten[i] * faktor;
                    }
                    g.DrawLine(Pens.LightGray, r.Left, y + hz, r.Left + gesamt * faktor, y + hz);
                    if (layout.Gitter)
                    {
                        var gx = (float)r.Left;
                        for (var i = 0; i <= kopf.Length; i++) { g.DrawLine(Pens.LightGray, gx, y, gx, y + hz); if (i < kopf.Length) gx += breiten[i] * faktor; }
                    }
                }
                else g.DrawString(p.Text, fb, Brushes.Black, r.Left + 2, y + 2);
                y += hz;
            }
            erster = false;
            pos++;
        }
        if (fuss > 0) g.DrawString(layout.Fusszeile, f, Brushes.DimGray, r.Left, r.Bottom - hz);
        return pos < posten.Count;
    }
}

/// <summary>
/// Druckauswahl der Unterrichtsansichten: Elemente (Klassen bzw. Lehrer), Auswahl „Unterricht“ oder „Stundenwünsche“ und die
/// Optionen aus dem Handbuch; Schaltflächen „Ausdruck bearbeiten“, „Seitenansicht“, „Drucken“.
/// </summary>
public static class UnterrichtDruckauswahl
{
    public enum Aktion { Abbrechen, AusdruckBearbeiten, Seitenansicht, Drucken }

    public sealed record Ergebnis(Aktion Aktion, List<int> Elemente, UnterrichtDruck.Optionen Optionen);

    private static UnterrichtDruck.Optionen _zuletzt = new(false, false, false, false, false, false);

    public static Ergebnis Zeigen(IWin32Window owner, ElementTyp typ, int? aktuell)
    {
        using var d = new Dialog("Druckauswahl", 470, 400);
        d.Ok.Visible = false;
        d.Abbrechen.Text = "Abbrechen";
        var art = new ComboBox { Left = 12, Top = 12, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Name = "Auswahl" };
        art.Items.AddRange(new object[] { "Unterricht", "Stundenwünsche" });
        art.SelectedIndex = _zuletzt.Stundenwuensche ? 1 : 0;
        d.Controls.Add(art);
        d.Controls.Add(new Label { Text = Ui.TypName(typ, true), Left = 12, Top = 44, AutoSize = true });
        var liste = new CheckedListBox { Left = 12, Top = 62, Width = 200, Height = 250, CheckOnClick = true, Name = "Elemente" };
        var elemente = Stammdaten.Sortiert(AppZustand.D, typ).Where(e => !e.Ignorieren).ToList();
        foreach (var e in elemente) liste.Items.Add($"{e.Kurzname}  {e.Langname}", e.Id == aktuell);
        if (liste.CheckedItems.Count == 0 && liste.Items.Count > 0) liste.SetItemChecked(0, true);
        d.Controls.Add(liste);
        Button K(string text, int x, Action a)
        {
            var b = new Button { Text = text, Left = x, Top = 318, Width = 64, Height = 24 };
            b.Click += (_, _) => a();
            d.Controls.Add(b);
            return b;
        }
        K("Alle", 12, () => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, true); });
        K("Keine", 80, () => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, false); });
        K("Markierte", 148, () => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, elemente[i].Markiert); });
        CheckBox C(string text, int y, bool wert, bool aktiv = true)
        {
            var c = new CheckBox { Text = text, Left = 230, Top = y, AutoSize = true, Checked = wert && aktiv, Enabled = aktiv };
            d.Controls.Add(c);
            return c;
        }
        var seite = C("1 Seite / Element", 14, _zuletzt.SeiteJeElement);
        var nurEl = C("Unterricht des Elements", 40, _zuletzt.NurElement);
        var zus = C("Unterricht zusammenfassen", 66, _zuletzt.Zusammenfassen);
        var bilanz = C("Bilanzzeile (Ist-Soll)", 92, _zuletzt.Bilanzzeile, typ == ElementTyp.Lehrer);
        var anr = C("Anrechnungen", 118, _zuletzt.Anrechnungen, typ == ElementTyp.Lehrer);
        var ergebnis = Aktion.Abbrechen;
        Button A(string text, int y, Aktion a)
        {
            var b = new Button { Text = text, Left = 330, Top = y, Width = 128, Height = 26 };
            b.Click += (_, _) => { ergebnis = a; d.DialogResult = DialogResult.OK; };
            d.Controls.Add(b);
            return b;
        }
        A("Ausdruck bearbeiten", 190, Aktion.AusdruckBearbeiten);
        A("Seitenansicht", 222, Aktion.Seitenansicht);
        d.AcceptButton = A("Drucken", 254, Aktion.Drucken);
        d.Abbrechen.Location = new Point(330, 364);
        d.Abbrechen.Width = 128;
        if (d.ShowDialog(owner) != DialogResult.OK) ergebnis = Aktion.Abbrechen;
        _zuletzt = new UnterrichtDruck.Optionen(seite.Checked, nurEl.Checked, zus.Checked, bilanz.Checked, anr.Checked, art.SelectedIndex == 1);
        var ids = liste.CheckedIndices.Cast<int>().Select(i => elemente[i].Id).ToList();
        return new Ergebnis(ergebnis, ids, _zuletzt);
    }
}
