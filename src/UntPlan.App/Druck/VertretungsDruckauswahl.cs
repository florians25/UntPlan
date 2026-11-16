using UntPlan.App.Dialoge;

namespace UntPlan.App.Druck;

/// <summary>
/// „Druckauswahl“ der Vertretungsliste wie im Handbuch (Kap. 13.2.3 und 13.6.1): Rahmen „Lehrer: 1/10“ bzw. „Klasse(n): …“ mit
/// „Auswahl“, „Ausdruck bearbeiten“ (Seitenlayout), „Druck nur, wenn geändert nach ..“ (Datum, Uhrzeit, „Heute“; 01.01.1990 = alle),
/// „PDF“ sowie Ok (Drucken), „HTML-Ausg.“ und Abbrechen. Lage und Größe nach der Abbildung (96 dpi).
/// </summary>
public static class VertretungsDruckauswahl
{
    /// <summary>Für den Selbsttest: Dialog erzeugen, ohne ihn anzuzeigen.</summary>
    internal static Dialog Erzeugen(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrer)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        var d = new Dialog("Druckauswahl", 356, 290);
        d.Ok.Text = "Ok";
        d.Ok.Size = new Size(78, 23);
        d.Ok.Location = new Point(98, 260);
        d.Abbrechen.Size = new Size(77, 23);
        d.Abbrechen.Location = new Point(270, 260);
        var html = new Button { Text = "HTML-Ausg.", Left = 183, Top = 260, Width = 79, Height = 23, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        d.Controls.Add(html);

        var gAuswahl = new GroupBox { Text = SeitenlayoutFenster.AuswahlText(lehrer), Left = 12, Top = 12, Width = 183, Height = 42 };
        var auswahl = new Button { Text = "Auswahl", Left = 26, Top = 11, Width = 138, Height = 23 };
        auswahl.Click += (_, _) => { if (SeitenlayoutFenster.ElementAuswahl(d, lehrer)) gAuswahl.Text = SeitenlayoutFenster.AuswahlText(lehrer); };
        gAuswahl.Controls.Add(auswahl);
        var bearbeiten = new Button { Text = "Ausdruck bearbeiten", Left = 225, Top = 75, Width = 115, Height = 23 };
        bearbeiten.Click += (_, _) => { d.DialogResult = DialogResult.Cancel; SeitenlayoutFenster.Zeigen(owner, von, bis, lehrer); };

        var gGeaendert = new GroupBox { Text = "Druck nur, wenn geändert nach ..", Left = 9, Top = 160, Width = 324, Height = 49 };
        var ab = e.NurGeaendertNach ?? SeitenlayoutFenster.OhneAenderungsdatum;
        var datum = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = ab.Date, Left = 11, Top = 17, Width = 101 };
        var zeit = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Value = ab, Left = 131, Top = 17, Width = 80 };
        var heute = new Button { Text = "Heute", Left = 237, Top = 17, Width = 75, Height = 23 };
        heute.Click += (_, _) => datum.Value = DateTime.Today;
        gGeaendert.Controls.AddRange(new Control[] { datum, zeit, heute });
        new ToolTip().SetToolTip(datum, "01.01.1990 = alle Vertretungen drucken");

        var gPdf = new GroupBox { Text = "PDF", Left = 11, Top = 210, Width = 322, Height = 46 };
        var pdf = new Button { Text = "PDF", Left = 9, Top = 15, Width = 78, Height = 23 };
        gPdf.Controls.Add(pdf);
        d.Controls.AddRange(new Control[] { gAuswahl, bearbeiten, gGeaendert, gPdf });

        void Uebernehmen()
        {
            e.NurGeaendertNach = datum.Value.Date <= SeitenlayoutFenster.OhneAenderungsdatum.Date ? null : datum.Value.Date + zeit.Value.TimeOfDay;
            AppZustand.Einstellungen.Speichern();
        }
        d.Ok.Click += (_, _) => Uebernehmen();
        html.Click += (_, _) => { Uebernehmen(); VertretungsplanDruck.HtmlAusgeben(d, von, bis, lehrer); };
        pdf.Click += (_, _) => { Uebernehmen(); VertretungsplanDruck.PdfSpeichern(d, von, bis, lehrer); };
        return d;
    }

    /// <summary>Druckauswahl zeigen; Ok druckt die Vertretungsliste.</summary>
    public static void Zeigen(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrer)
    {
        using var d = Erzeugen(owner, von, bis, lehrer);
        if (d.ShowDialog(owner) == DialogResult.OK) VertretungsplanDruck.Ausgeben(owner, von, bis, lehrer, false);
    }
}
