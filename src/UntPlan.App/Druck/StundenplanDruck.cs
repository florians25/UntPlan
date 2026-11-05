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
        var (seiten, spalten) = Seitenaufteilung(ids, format, Seite.Landscape, woche);
        var seite = 0;
        doc.BeginPrint += (_, _) => seite = 0;
        doc.PrintPage += (sender, e) =>
        {
            SeiteZeichnen(e.Graphics!, ListenDruck.Seitenbereich(sender, e), typ, seiten[seite], spalten, format, seite + 1, seiten.Count);
            seite++;
            e.HasMorePages = seite < seiten.Count;
        };
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>
    /// Seiten des Drucks laut Druckdetails des Formats (Pläne nebeneinander × übereinander, „Für jede Woche ein Stundenplan“,
    /// „Je Element neue Seite“). Ohne eigene Einstellung bleibt es wie bisher: großes Format ein Plan je Seite, kleines Format im
    /// Querformat zwei kompakte Pläne nebeneinander (<c>spalten</c> = 0).
    /// </summary>
    internal static (List<List<Druck.DruckPlan>> Seiten, int Spalten) Seitenaufteilung(List<int> ids, StundenplanFormat format, bool quer, DateOnly? woche)
    {
        var det = format.Druck;
        var ersteWoche = woche is DateOnly w ? Kalender.Schulwoche(AppZustand.D, w) : Kalender.Schulwoche(AppZustand.D, AppZustand.Datum);
        var wochen = det.JedeWocheEinPlan ? Druckseiten.Wochen(det, Math.Max(1, ersteWoche)) : null;
        if (Druckseiten.PlaeneJeSeite(det) > 1 || det.JedeWocheEinPlan || det.JeElementNeueSeite)
            return (Druckseiten.Einzelplaene(ids, det, wochen).Select(s => s.Select(p => new Druck.DruckPlan(p.ElementId, p.Woche)).ToList()).ToList(), 1);
        var proSeite = !format.Gross && quer && ids.Count > 1 ? 2 : 1;
        return (ids.Chunk(proSeite).Select(c => c.Select(i => new Druck.DruckPlan(i, null)).ToList()).ToList(), proSeite > 1 ? 0 : 1);
    }

    /// <summary>Eine Seite als Schwarz-Weiß-Formular zeichnen.</summary>
    private static void SeiteZeichnen(Graphics g, Rectangle r, ElementTyp typ, List<Druck.DruckPlan> plaene, int spalten, StundenplanFormat format, int seite, int seiten)
    {
        // Schriftgrößen in % aus den Stundenplan-Einstellungen (Allgemein): Überschrift und Kopp.-Legende; Druckdetails des Formats
        var o = Druck.StundenplanFormular.Optionen;
        o.UeberschriftProzent = format.UeberschriftSchriftProzent;
        o.LegendeProzent = format.LegendeSchriftProzent;
        o.Details = format.Druck;
        o.EineUeberschriftJeBlatt = format.EineUeberschriftJeBlatt;
        Druck.StundenplanFormular.Seite(g, r, typ, plaene, spalten, seite, seiten);
    }

    /// <summary>Für Selbsttest und Vorschau: Seiten als Bilder (A4 quer, 100 dpi).</summary>
    public static List<Bitmap> Bilder(ElementTyp typ, List<int> ids, StundenplanFormat format, DateOnly? woche = null)
    {
        var (seiten, spalten) = Seitenaufteilung(ids, format, true, woche);
        var res = new List<Bitmap>();
        for (var i = 0; i < seiten.Count && res.Count < 20; i++)
        {
            var bmp = new Bitmap(1169, 827);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Display;
            SeiteZeichnen(g, new Rectangle(35, 40, bmp.Width - 70, bmp.Height - 80), typ, seiten[i], spalten, format, i + 1, seiten.Count);
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
        void Alle() { auswahl = Stammdaten.Sortiert(AppZustand.D, Typ()).Where(e => !e.NichtDrucken).Select(e => e.Id).ToList(); lbl.Text = $"{Ui.TypName(Typ(), true)}: {auswahl.Count}/{auswahl.Count}"; }
        // „Markierte“: alle Elemente, in deren Stammdaten das Kennzeichen „Markiert“ gesetzt ist
        var markierte = new Button { Text = "Markierte", Left = 312, Top = y, Width = 90 };
        markierte.Click += (_, _) =>
        {
            var alle = Stammdaten.Sortiert(AppZustand.D, Typ()).Count();
            auswahl = Stammdaten.Sortiert(AppZustand.D, Typ()).Where(e => e.Markiert).Select(e => e.Id).ToList();
            lbl.Text = $"{Ui.TypName(Typ(), true)}: {auswahl.Count}/{alle}";
        };
        d.Controls.Add(markierte);
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
            var ordner = Dialoge.Einstellungsseiten.Eingetragen("HTML-Dateien") ?? Path.GetTempPath();
            var pfad = Path.Combine(ordner, "UntPlan_Stundenplaene.html");
            Export.HtmlSchreiben(AppZustand.D, pfad, Export.Seite("Stundenpläne", string.Concat(auswahl.Select(id => Export.StundenplanHtml(AppZustand.D, Typ(), id))), AppZustand.D.Einstellungen.Html));
            HauptFenster.Starten(pfad);
        };
        if (d.ShowDialog(owner) == DialogResult.OK) StundenplanDruck.Drucken(owner, Typ(), auswahl, Fmt(), false);
    }
}
