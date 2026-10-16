using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>
/// Druck von Stundenplänen als Schwarz-Weiß-Formular (siehe <see cref="Druck.StundenplanFormular"/>): je Element eine Seite mit
/// Raster, Unterrichtsliste und Legenden; im kleinen Format zwei kompakte Pläne nebeneinander auf einer Seite.
/// </summary>
public static class StundenplanDruck
{
    /// <summary>Seitenlayout der Stundenplandrucke (Menü „»“ → Seitenlayout), Standard Querformat mit schmalen Rändern.</summary>
    public static PageSettings Seite { get; } = new() { Landscape = true, Margins = new Margins(35, 35, 40, 40) };

    public static void Drucken(IWin32Window owner, ElementTyp typ, List<int> ids, StundenplanFormat format, bool vorschau, DateOnly? woche = null)
    {
        if (ids.Count == 0) return;
        var doc = new PrintDocument { DocumentName = $"Stundenpläne {Ui.TypName(typ, true)}" };
        doc.DefaultPageSettings.Landscape = Seite.Landscape;
        doc.DefaultPageSettings.Margins = Seite.Margins;
        try { if (Seite.PaperSize is { } papier) doc.DefaultPageSettings.PaperSize = papier; } catch (InvalidPrinterException) { }
        // kleines Format im Querformat: zwei Pläne je Seite
        var proSeite = !format.Gross && Seite.Landscape && ids.Count > 1 ? 2 : 1;
        var seiten = (ids.Count + proSeite - 1) / proSeite;
        var i = 0;
        var seite = 0;
        doc.BeginPrint += (_, _) => { i = 0; seite = 0; };
        doc.PrintPage += (sender, e) =>
        {
            seite++;
            i = SeiteZeichnen(e.Graphics!, ListenDruck.Seitenbereich(sender, e), typ, ids, i, proSeite, format, woche, seite, seiten);
            e.HasMorePages = i < ids.Count;
        };
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>Eine Seite zeichnen (ab Plan <paramref name="i"/>) als Schwarz-Weiß-Formular; Ergebnis: Index des nächsten Plans.</summary>
    private static int SeiteZeichnen(Graphics g, Rectangle r, ElementTyp typ, List<int> ids, int i, int proSeite, StundenplanFormat format, DateOnly? woche, int seite, int seiten)
    {
        var teil = ids.Skip(i).Take(proSeite).ToList();
        Druck.StundenplanFormular.Seite(g, r, typ, teil, !format.Gross, seite, seiten);
        return i + teil.Count;
    }

    /// <summary>Für Selbsttest und Vorschau: Seiten als Bilder (A4 quer, 100 dpi).</summary>
    public static List<Bitmap> Bilder(ElementTyp typ, List<int> ids, StundenplanFormat format, DateOnly? woche = null)
    {
        var proSeite = !format.Gross && ids.Count > 1 ? 2 : 1;
        var seiten = (ids.Count + proSeite - 1) / proSeite;
        var res = new List<Bitmap>();
        for (int i = 0, seite = 1; i < ids.Count && res.Count < 20; seite++)
        {
            var bmp = new Bitmap(1169, 827);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Display;
            i = SeiteZeichnen(g, new Rectangle(35, 40, bmp.Width - 70, bmp.Height - 80), typ, ids, i, proSeite, format, woche, seite, seiten);
            res.Add(bmp);
        }
        return res;
    }
}

/// <summary>Druckauswahl: Elemente auswählen, Druck / Seitenansicht / PDF / HTML-Ausgabe.</summary>
public static class DruckauswahlDialog
{
    public static void Stundenplaene(IWin32Window owner, ElementTyp typ)
    {
        using var d = new Dialog("Druckauswahl", 420, 250);
        var y = 14;
        var typBox = Ui.Zeile(d, "Pläne", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 100, 280);
        typBox.Items.AddRange(new object[] { "Klassen", "Lehrer", "Räume", "Fächer" });
        typBox.SelectedIndex = typ switch { ElementTyp.Lehrer => 1, ElementTyp.Raum => 2, ElementTyp.Fach => 3, _ => 0 };
        var gross = Ui.Zeile(d, "Format groß", new CheckBox { Checked = true }, ref y, 100);
        var auswahl = new List<int>();
        var lbl = new Label { Left = 112, Top = y + 4, Width = 200 };
        var btn = new Button { Text = "Auswahl…", Left = 12, Top = y, Width = 90 };
        d.Controls.Add(btn);
        d.Controls.Add(lbl);
        ElementTyp Typ() => typBox.SelectedIndex switch { 1 => ElementTyp.Lehrer, 2 => ElementTyp.Raum, 3 => ElementTyp.Fach, _ => ElementTyp.Klasse };
        void Alle() { auswahl = Stammdaten.Sortiert(AppZustand.D, Typ()).Select(e => e.Id).ToList(); lbl.Text = $"{Ui.TypName(Typ(), true)}: {auswahl.Count}/{auswahl.Count}"; }
        typBox.SelectedIndexChanged += (_, _) => Alle();
        btn.Click += (_, _) =>
        {
            var l = AuswahlDialog.Mehrere(d, Typ(), "Elemente auswählen", auswahl);
            if (l != null) { auswahl = l.Select(e => e.Id).ToList(); lbl.Text = $"{Ui.TypName(Typ(), true)}: {auswahl.Count}"; }
        };
        y += 40;
        var vorschau = new Button { Text = "Seitenansicht", Left = 12, Top = y, Width = 100 };
        var html = new Button { Text = "HTML-Ausg.", Left = 118, Top = y, Width = 100 };
        var pdf = new Button { Text = "PDF", Left = 224, Top = y, Width = 80 };
        d.Controls.AddRange(new Control[] { vorschau, html, pdf });
        Alle();
        StundenplanFormat Fmt() => AppZustand.D.Formate.FirstOrDefault(f => f.Typ == Typ() && f.Gross == gross.Checked) ?? new StundenplanFormat { Typ = Typ(), Gross = gross.Checked, Schriftgroesse = 10 };
        vorschau.Click += (_, _) => StundenplanDruck.Drucken(d, Typ(), auswahl, Fmt(), true);
        pdf.Click += (_, _) =>
        {
            Ui.Info("Wählen Sie im folgenden Druckdialog den Drucker \"Microsoft Print to PDF\".");
            StundenplanDruck.Drucken(d, Typ(), auswahl, Fmt(), false);
        };
        html.Click += (_, _) =>
        {
            var pfad = Path.Combine(Path.GetTempPath(), "UntPlan_Stundenplaene.html");
            File.WriteAllText(pfad, Export.Seite("Stundenpläne", string.Concat(auswahl.Select(id => Export.StundenplanHtml(AppZustand.D, Typ(), id)))));
            HauptFenster.Starten(pfad);
        };
        if (d.ShowDialog(owner) == DialogResult.OK) StundenplanDruck.Drucken(owner, Typ(), auswahl, Fmt(), false);
    }
}
