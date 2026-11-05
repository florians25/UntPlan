using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Druck;

/// <summary>
/// Druck der Übersichtspläne (Formate 11, 20, 30; Handbuch 6.6.2.11): Anzahl Zeilen (Klassen, Le., …) und Spalten (Wochenstunden) pro Blatt
/// bzw. automatisch („Nur ganze Tage auf ein Blatt“, „Nur Einzeltage auf ein Blatt“), „Mehrere Stundenplan-Blöcke pro Blatt“,
/// Zwischenüberschriften nach jeder X. Zeile und Seitenüberschrift.
/// </summary>
public static class UebersichtDruck
{
    private static readonly Dictionary<ElementTyp, StundenplanDruckdetails> Sitzung = new();

    /// <summary>Druckdetails des Übersichtsplans: aus einem gespeicherten Übersichtsformat (Format 20/30) der Planart, sonst für die Sitzung.</summary>
    public static StundenplanDruckdetails Details(ElementTyp typ) =>
        AppZustand.D.Formate.FirstOrDefault(f => f.Typ == typ && f.Grundform is Grundform.Format20 or Grundform.Format30 or Grundform.Format11)?.Druck
        ?? (Sitzung.TryGetValue(typ, out var d) ? d : Sitzung[typ] = new StundenplanDruckdetails());

    private const int NameBreite = 60, KopfHoehe = 36, MinZeile = 18, MinSpalte = 18;

    /// <summary>Blätter für einen Druckbereich der Größe <paramref name="r"/>.</summary>
    public static List<UebersichtBlatt> Blaetter(ElementTyp typ, Rectangle r, StundenplanDruckdetails det)
    {
        var d = AppZustand.D;
        var anzahl = Stammdaten.Sortiert(d, typ).Count();
        var hoehe = r.Height - 30 - (det.Seitenueberschrift.Length > 0 ? 20 : 0);
        var maxZeilen = Math.Max(1, (hoehe - KopfHoehe) / MinZeile);
        var maxSpalten = Math.Max(1, (r.Width - NameBreite) / MinSpalte);
        return Druckseiten.Uebersicht(anzahl, d.Zeitraster.TageProWoche, d.Zeitraster.StundenProTag, det, maxZeilen, maxSpalten);
    }

    public static void Drucken(IWin32Window owner, ElementTyp typ, string titel, bool vorschau)
    {
        var det = Details(typ);
        var doc = new PrintDocument { DocumentName = titel };
        doc.DefaultPageSettings.Landscape = true;
        List<UebersichtBlatt>? blaetter = null;
        var seite = 0;
        doc.BeginPrint += (_, _) => { seite = 0; blaetter = null; };
        doc.PrintPage += (sender, e) =>
        {
            var r = ListenDruck.Seitenbereich(sender, e);
            blaetter ??= Blaetter(typ, r, det);
            Seite(e.Graphics!, r, typ, titel, det, blaetter[seite], seite + 1, blaetter.Count);
            seite++;
            e.HasMorePages = seite < blaetter.Count;
        };
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>Ein Blatt: Titel, Seitenüberschrift, je Tagesblock Kopf (Tage, Stunden) und die Elementzeilen (mit Zwischenüberschriften).</summary>
    public static void Seite(Graphics g, Rectangle r, ElementTyp typ, string titel, StundenplanDruckdetails det, UebersichtBlatt blatt, int seite, int seiten)
    {
        var d = AppZustand.D;
        var ix = new DatenIndex(d);
        var zr = d.Zeitraster;
        using var fTitel = new Font("Arial", 11f, FontStyle.Bold);
        using var fKopf = new Font("Arial", 8f, FontStyle.Bold);
        using var fKlein = new Font("Arial", 6.5f);
        using var fName = new Font("Arial", 7.5f, FontStyle.Bold);
        g.DrawString($"{titel} – {d.Einstellungen.Schulname}", fTitel, Brushes.Black, r.Left, r.Top);
        using var rechts = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"Seite {seite} / {seiten}", fKlein, Brushes.Black, new RectangleF(r.Left, r.Top + 2, r.Width, 14), rechts);
        var y = r.Top + 22f;
        if (det.Seitenueberschrift.Length > 0) { g.DrawString(det.Seitenueberschrift, fKopf, Brushes.Black, r.Left, y); y += 18; }
        var elemente = Stammdaten.Sortiert(d, typ).Skip(blatt.ErstesElement).Take(blatt.AnzahlElemente).ToList();
        var blockH = (r.Bottom - y) / Math.Max(1, blatt.Bloecke.Count);
        using var mitte = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        foreach (var (von, bis) in blatt.Bloecke)
        {
            var tage = bis - von + 1;
            var spalten = tage * zr.StundenProTag;
            var zwischen = det.ZwischenueberschriftNach > 0 ? (elemente.Count - 1) / det.ZwischenueberschriftNach : 0;
            var zeileH = Math.Clamp((blockH - 8 - KopfHoehe * (1 + zwischen)) / Math.Max(1, elemente.Count), 10f, 26f);
            var cw = (r.Width - NameBreite) / (float)Math.Max(1, spalten);
            void Kopf(float yy)
            {
                g.FillRectangle(Brushes.Gainsboro, r.Left, yy, r.Width, KopfHoehe);
                for (var t = von; t <= bis; t++)
                {
                    var x = r.Left + NameBreite + (t - von) * zr.StundenProTag * cw;
                    g.DrawString(Planungsdienst.Tagname(d, t, true), fKopf, Brushes.Black, new RectangleF(x, yy, zr.StundenProTag * cw, 18), mitte);
                    for (var s = 1; s <= zr.StundenProTag; s++)
                        g.DrawString(zr.StundenLabel(s), fKlein, Brushes.Black, new RectangleF(x + (s - 1) * cw, yy + 18, cw, 16), mitte);
                    g.DrawLine(Pens.Black, x, yy, x, yy + KopfHoehe);
                }
                g.DrawRectangle(Pens.Black, r.Left, yy, r.Width, KopfHoehe);
            }
            Kopf(y);
            y += KopfHoehe;
            for (var i = 0; i < elemente.Count; i++)
            {
                if (det.ZwischenueberschriftNach > 0 && i > 0 && i % det.ZwischenueberschriftNach == 0) { Kopf(y); y += KopfHoehe; }
                var el = elemente[i];
                g.DrawString(el.Kurzname, fName, Brushes.Black, new RectangleF(r.Left + 2, y, NameBreite - 4, zeileH), new StringFormat { LineAlignment = StringAlignment.Center });
                foreach (var p in ix.PlanVon(typ, el.Id).Where(p => p.Tag >= von && p.Tag <= bis))
                {
                    if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
                    var x = r.Left + NameBreite + ((p.Tag - von) * zr.StundenProTag + p.Stunde - 1) * cw;
                    var text = typ switch
                    {
                        ElementTyp.Lehrer => ix.KKlassen(u.KlassenIds),
                        ElementTyp.Klasse => ix.KFach(u.HauptfachId),
                        _ => ix.KKlassen(u.KlassenIds),
                    };
                    g.FillRectangle(Brushes.WhiteSmoke, x + 1, y + 1, cw - 2, zeileH - 2);
                    g.DrawString((p.Fixiert ? "*" : "") + text, fKlein, Brushes.Black, new RectangleF(x, y, cw, zeileH), mitte);
                }
                g.DrawLine(Pens.Silver, r.Left, y + zeileH, r.Right, y + zeileH);
                y += zeileH;
            }
            for (var t = von; t <= bis; t++)
            {
                var x = r.Left + NameBreite + (t - von) * zr.StundenProTag * cw;
                g.DrawLine(Pens.Black, x, y - zeileH * elemente.Count, x, y);
            }
            y += 8;
        }
    }

    /// <summary>Einstellungen der Übersichtspläne: „Anzahl Zeilen / Spalten pro Blatt“ (wie im Handbuch, Abbildung S. 189/190).</summary>
    public static Form DetailsForm(StundenplanDruckdetails det)
    {
        var f = new Dialog("Einstellungen", 300, 250) { FormBorderStyle = FormBorderStyle.FixedToolWindow };
        f.Abbrechen.Visible = false;
        f.Ok.Text = "Ok";
        f.Ok.Location = new Point(10, 216);
        var g = new GroupBox { Text = "Anzahl Zeilen / Spalten pro Blatt", Left = 6, Top = 4, Width = 288, Height = 150 };
        var zeilen = new NumericUpDown { Left = 8, Top = 18, Width = 50, Minimum = 1, Maximum = 200, Value = Math.Clamp(det.ZeilenProBlatt, 1, 200) };
        var spalten = new NumericUpDown { Left = 8, Top = 44, Width = 50, Minimum = 1, Maximum = 200, Value = Math.Clamp(det.SpaltenProBlatt, 1, 200) };
        var auto = new CheckBox { Text = "Zeilen/Spalten Automatisch", Left = 8, Top = 72, AutoSize = true, Checked = det.ZeilenSpaltenAutomatisch };
        var ganz = new CheckBox { Text = "Nur ganze Tage auf ein Blatt", Left = 24, Top = 92, AutoSize = true, Checked = det.NurGanzeTage };
        var einzel = new CheckBox { Text = "Nur Einzeltage auf ein Blatt", Left = 24, Top = 110, AutoSize = true, Checked = det.NurEinzeltage };
        var bloecke = new CheckBox { Text = "Mehrere Stundenplan-Blöcke pro Blatt", Left = 8, Top = 128, AutoSize = true, Checked = det.MehrereBloecke };
        g.Controls.AddRange(new Control[] { zeilen, new Label { Text = "Zeilen (Klassen, Le., ..) pro Blatt", Left = 64, Top = 21, AutoSize = true },
            spalten, new Label { Text = "Spalten (Wochenstunden) pro Blatt", Left = 64, Top = 47, AutoSize = true }, auto, ganz, einzel, bloecke });
        var zw = new NumericUpDown { Left = 8, Top = 160, Width = 50, Minimum = 0, Maximum = 99, Value = Math.Clamp(det.ZwischenueberschriftNach, 0, 99) };
        var su = new TextBox { Left = 8, Top = 188, Width = 284, Text = det.Seitenueberschrift, PlaceholderText = "Seitenüberschrift" };
        f.Controls.AddRange(new Control[] { g, zw, new Label { Text = "Zwischenüberschriften nach jeder X. Zeile", Left = 64, Top = 163, AutoSize = true }, su });
        void Umschalten()
        {
            zeilen.Enabled = spalten.Enabled = !auto.Checked;
            ganz.Enabled = einzel.Enabled = auto.Checked;
        }
        auto.CheckedChanged += (_, _) => Umschalten();
        Umschalten();
        f.Ok.Click += (_, _) =>
        {
            det.ZeilenProBlatt = (int)zeilen.Value;
            det.SpaltenProBlatt = (int)spalten.Value;
            det.ZeilenSpaltenAutomatisch = auto.Checked;
            det.NurGanzeTage = ganz.Checked;
            det.NurEinzeltage = einzel.Checked;
            det.MehrereBloecke = bloecke.Checked;
            det.ZwischenueberschriftNach = (int)zw.Value;
            det.Seitenueberschrift = su.Text.Trim();
        };
        return f;
    }
}
