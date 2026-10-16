using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>
/// Druck von Stundenplänen als sauberes Schuldokument: Kopf mit Schule und Planart, Akzentlinie, großer Elementname mit
/// Zusatzangaben (Klassenleitung, Stammraum, Kapazität …), der Plan in voller Breite mit hochwertiger Kantenglättung und eine
/// Fußzeile mit Druckdatum und „Seite x von y“. Im kleinen Format stehen zwei Pläne nebeneinander auf einer Seite.
/// </summary>
public static class StundenplanDruck
{
    /// <summary>Seitenlayout der Stundenplandrucke (Menü „»“ → Seitenlayout), Standard Querformat mit schmalen Rändern.</summary>
    public static PageSettings Seite { get; } = new() { Landscape = true, Margins = new Margins(35, 35, 40, 40) };

    private static readonly Color Akzent = Color.FromArgb(47, 111, 192);
    private static readonly Color Dunkel = Color.FromArgb(37, 55, 70);

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
        doc.PrintPage += (_, e) =>
        {
            seite++;
            i = SeiteZeichnen(e.Graphics!, e.MarginBounds, typ, ids, i, proSeite, format, woche, seite, seiten);
            e.HasMorePages = i < ids.Count;
        };
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>Eine Seite zeichnen (ab Plan <paramref name="i"/>); Ergebnis: Index des nächsten Plans.</summary>
    private static int SeiteZeichnen(Graphics g, Rectangle r, ElementTyp typ, List<int> ids, int i, int proSeite, StundenplanFormat format, DateOnly? woche, int seite, int seiten)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var inhalt = Kopf(g, r, typ, woche);
        inhalt = new Rectangle(inhalt.Left, inhalt.Top, inhalt.Width, inhalt.Height - 26);
        const int abstand = 24;
        var breite = (inhalt.Width - (proSeite - 1) * abstand) / proSeite;
        for (var n = 0; n < proSeite && i < ids.Count; n++, i++)
            Plan(g, new Rectangle(inhalt.Left + n * (breite + abstand), inhalt.Top, breite, inhalt.Height), typ, ids[i], format, woche, proSeite > 1);
        Fuss(g, r, seite, seiten);
        return i;
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

    /// <summary>Seitenkopf: Schule links, Planart und Gültigkeit rechts, darunter eine Akzentlinie. Ergebnis: verbleibender Bereich.</summary>
    private static Rectangle Kopf(Graphics g, Rectangle r, ElementTyp typ, DateOnly? woche)
    {
        var d = AppZustand.D;
        using var fSchule = new Font("Segoe UI Semibold", 9.5f);
        using var fKlein = new Font("Segoe UI", 8f);
        using var grau = new SolidBrush(Color.FromArgb(105, 115, 125));
        using var dunkel = new SolidBrush(Dunkel);
        g.DrawString(d.Einstellungen.Schulname, fSchule, dunkel, r.Left, r.Top);
        var rechts = $"Stundenplan {Ui.TypName(typ)}  ·  Schuljahr {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrVon.Year + 1}";
        var unten = $"gültig ab {Format.Datum(d.AktivePeriode.Von)}{(woche.HasValue ? $"  ·  Woche ab {Format.Datum(woche.Value)}" : "")}";
        using var rechtsbuendig = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString(rechts, fKlein, grau, new RectangleF(r.Left, r.Top + 1, r.Width, 16), rechtsbuendig);
        g.DrawString(unten, fKlein, grau, new RectangleF(r.Left, r.Top + 15, r.Width, 16), rechtsbuendig);
        using var linie = new SolidBrush(Akzent);
        g.FillRectangle(linie, r.Left, r.Top + 33, r.Width, 2);
        return new Rectangle(r.Left, r.Top + 44, r.Width, r.Height - 44);
    }

    /// <summary>Ein Plan: Name groß, Zusatzangaben klein, darunter das Raster.</summary>
    private static void Plan(Graphics g, Rectangle r, ElementTyp typ, int id, StundenplanFormat format, DateOnly? woche, bool halb)
    {
        var d = AppZustand.D;
        var el = AppZustand.Doc.Index.Element(typ, id);
        using var fName = new Font("Segoe UI Semibold", halb ? 14f : 17f);
        using var fLang = new Font("Segoe UI", halb ? 10f : 11.5f);
        using var fInfo = new Font("Segoe UI", 8.5f);
        using var dunkel = new SolidBrush(Dunkel);
        using var grau = new SolidBrush(Color.FromArgb(95, 105, 115));
        var kurz = el?.Kurzname ?? "";
        var lang = el is Lehrer l ? l.VollerName : el?.Langname ?? "";
        g.DrawString(kurz, fName, dunkel, r.Left - 2, r.Top - 2);
        var kb = g.MeasureString(kurz, fName);
        if (lang.Length > 0 && lang != kurz) g.DrawString(lang, fLang, grau, r.Left + kb.Width + 2, r.Top + kb.Height - g.MeasureString(lang, fLang).Height - 3);
        var info = Zusatz(d, el);
        var oben = r.Top + (int)kb.Height + 1;
        if (info.Length > 0) { g.DrawString(info, fInfo, grau, r.Left, oben); oben += 18; }
        oben += 6;
        using var plan = new PlanAnsicht { Typ = typ, ElementId = id, Format = format, Woche = woche };
        plan.Zeichnen(g, new Rectangle(r.Left, oben, r.Width, r.Bottom - oben), true);
    }

    /// <summary>Zusatzangaben je Elementart (Klassenleitung und Stammraum, Stammraum der Lehrkraft, Raumart und Plätze).</summary>
    private static string Zusatz(Schuldatei d, Element? el)
    {
        string Kuerzel(IEnumerable<int> lehrerIds) => string.Join(", ", lehrerIds.Select(x => d.Lehrer.FirstOrDefault(l => l.Id == x)).Where(l => l != null).Select(l => l!.Kurzname));
        string Raum(int? rid) => rid is int x ? d.Raeume.FirstOrDefault(r => r.Id == x)?.Kurzname ?? "" : "";
        var teile = el switch
        {
            Klasse k => new[] { k.KlassenlehrerIds.Count > 0 ? $"Klassenleitung: {Kuerzel(k.KlassenlehrerIds)}" : "", Raum(k.StammraumId) is { Length: > 0 } sr ? $"Stammraum: {sr}" : "",
                k.Schuelerzahl > 0 ? $"{k.Schuelerzahl} Schüler*innen" : "" },
            Lehrer l => new[] { Raum(l.StammraumId) is { Length: > 0 } sr ? $"Stammraum: {sr}" : "", l.Email },
            Raum ra => new[] { ra.Raumart, ra.Kapazitaet > 0 ? $"{ra.Kapazitaet} Plätze" : "", ra.Stockwerk.Length > 0 ? $"Stockwerk {ra.Stockwerk}" : "" },
            _ => Array.Empty<string>(),
        };
        return string.Join("   ·   ", teile.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static void Fuss(Graphics g, Rectangle r, int seite, int seiten)
    {
        using var f = new Font("Segoe UI", 7.5f);
        using var grau = new SolidBrush(Color.FromArgb(125, 133, 140));
        using var stift = new Pen(Color.FromArgb(205, 210, 215), 1f);
        var y = r.Bottom - 16;
        g.DrawLine(stift, r.Left, y - 3, r.Right, y - 3);
        g.DrawString($"Erstellt mit UntPlan  ·  gedruckt am {DateTime.Now:dd.MM.yyyy} um {DateTime.Now:HH:mm} Uhr", f, grau, r.Left, y);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"Seite {seite} von {seiten}", f, grau, new RectangleF(r.Left, y, r.Width, 14), rechts);
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
