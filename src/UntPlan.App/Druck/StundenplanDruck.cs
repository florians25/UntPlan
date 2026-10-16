using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>Druck von Stundenplänen (je Element eine Seite).</summary>
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
        var i = 0;
        doc.BeginPrint += (_, _) => i = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            var r = e.MarginBounds;
            var d = AppZustand.D;
            var el = AppZustand.Doc.Index.Element(typ, ids[i]);
            using var ft = new Font("Segoe UI", 15f, FontStyle.Bold);
            using var fs = new Font("Segoe UI", 9f);
            var titel = el is null ? "" : $"{el.Kurzname} – {(el is Lehrer l ? l.VollerName : el.Langname)}";
            g.DrawString(titel, ft, Brushes.Black, r.Left, r.Top);
            g.DrawString($"{d.Einstellungen.Schulname}   {Ui.TypName(typ)}   gültig ab {Format.Datum(d.AktivePeriode.Von)}{(woche.HasValue ? $"   Woche ab {Format.Datum(woche.Value)}" : "")}", fs, Brushes.DimGray, r.Left, r.Top + 28);
            var plan = new PlanAnsicht { Typ = typ, ElementId = ids[i], Format = format, Woche = woche };
            plan.Zeichnen(g, new Rectangle(r.Left, r.Top + 50, r.Width, r.Height - 70), true);
            g.DrawString($"UntPlan · gedruckt am {DateTime.Now:dd.MM.yyyy HH:mm}", fs, Brushes.Gray, r.Left, r.Bottom - 14);
            plan.Dispose();
            i++;
            e.HasMorePages = i < ids.Count;
        };
        ListenDruck.Ausgeben(owner, doc, vorschau);
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
