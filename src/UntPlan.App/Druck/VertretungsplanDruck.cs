using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Druck;

/// <summary>
/// Aushang des Vertretungsplans (Klassen- oder Lehrerversion), je Tag eine neue Seite.
/// <list type="bullet">
/// <item>Kopf: Schule, Schuljahr, Programm, Adresse und Druckzeitpunkt.</item>
/// <item>Titel: „… Klassen am: 24.9. / Donnerstag“.</item>
/// <item>Nachrichten zum Tag in einem grauen Kasten.</item>
/// <item>Blockierte Räume sowie abwesende Klassen bzw. Lehrer im Rahmen.</item>
/// <item>Tabelle: Klasse(n), Stunde, Vertreter, Abwesend (durchgestrichen), Fach, Raum, Bemerkungen und Hinweise.</item>
/// </list>
/// Gruppen (Klassen bzw. Lehrer) werden durch eine Linie getrennt.
/// </summary>
public static class VertretungsplanDruck
{
    private sealed record Zeile(string Element, string Stunde, string Vertreter, string Abwesend, bool Durchgestrichen, string Fach, string Raum, string Bemerkung);

    private sealed record Tag(DateOnly Datum, List<string> Nachrichten, List<(string Titel, string Text)> Kaesten, List<Zeile> Zeilen);

    public static void Drucken(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrerVersion, bool vorschau)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        if (vorschau && !Optionen(owner, e, ref lehrerVersion)) return;
        AppZustand.Einstellungen.Speichern();
        var d = AppZustand.D;
        var tage = Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)).Select(t => TagDaten(d, t, lehrerVersion, e)).ToList();
        if (tage.Count == 0) { Ui.Info("Im gewählten Zeitraum gibt es keinen Unterrichtstag."); return; }

        var doc = new PrintDocument { DocumentName = $"{d.Einstellungen.Vertretungsplantitel} {Format.Datum(von)}" };
        doc.DefaultPageSettings.Landscape = e.Querformat;
        doc.DefaultPageSettings.Margins = new Margins(55, 45, 45, 45);
        var seiten = new Seiten(tage, lehrerVersion, e);
        doc.BeginPrint += (_, _) => seiten.Zuruecksetzen();
        doc.PrintPage += (_, pe) => pe.HasMorePages = seiten.Zeichnen(pe.Graphics!, pe.MarginBounds);
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    /// <summary>Seiten als Bilder (A4, 100 dpi) – für Vorschau und Selbsttest ohne Drucker.</summary>
    public static List<Bitmap> Bilder(DateOnly von, DateOnly bis, bool lehrerVersion)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        var d = AppZustand.D;
        var tage = Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)).Select(t => TagDaten(d, t, lehrerVersion, e)).ToList();
        var seiten = new Seiten(tage, lehrerVersion, e);
        var res = new List<Bitmap>();
        var mehr = tage.Count > 0;
        while (mehr && res.Count < 50)
        {
            var bmp = e.Querformat ? new Bitmap(1169, 827) : new Bitmap(827, 1169);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Display;
            mehr = seiten.Zeichnen(g, new Rectangle(55, 45, bmp.Width - 100, bmp.Height - 90));
            res.Add(bmp);
        }
        return res;
    }

    /// <summary>Seitenweises Zeichnen mit Fortsetzung über Seitengrenzen.</summary>
    private sealed class Seiten
    {
        private readonly List<Tag> _tage;
        private readonly bool _lehrer;
        private readonly Vertretungsdruck _e;
        private int _tagNr, _zeileNr;
        private bool _tagAnfang = true;

        public Seiten(List<Tag> tage, bool lehrer, Vertretungsdruck e) { _tage = tage; _lehrer = lehrer; _e = e; }

        public void Zuruecksetzen() { _tagNr = 0; _zeileNr = 0; _tagAnfang = true; }

        /// <summary>Zeichnet die nächste Seite; liefert true, wenn weitere Seiten folgen.</summary>
        public bool Zeichnen(Graphics g, Rectangle r)
        {
            var d = AppZustand.D;
            var e = _e;
            var tage = _tage;
            var lehrerVersion = _lehrer;
            ref var tagNr = ref _tagNr;
            ref var zeileNr = ref _zeileNr;
            ref var tagAnfang = ref _tagAnfang;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var schrift = SchriftartPruefen(e.Schriftart);
            using var normal = new Font(schrift, e.Schriftgroesse);
            using var fett = new Font(schrift, e.Schriftgroesse, FontStyle.Bold);
            using var durch = new Font(schrift, e.Schriftgroesse, FontStyle.Strikeout | FontStyle.Italic);
            using var kopfFett = new Font(schrift, e.Schriftgroesse + 2, FontStyle.Bold);
            using var unterstrichen = new Font(schrift, e.Schriftgroesse, FontStyle.Bold | FontStyle.Underline);
            using var kopfNormal = new Font(schrift, e.Schriftgroesse + 1);
            using var titel = new Font(schrift, e.Schriftgroesse + 8, FontStyle.Bold);
            var tag = tage[tagNr];
            var y = (float)r.Top;

            // Kopf: Schule | Schuljahr | Programm – Adresse | Druckzeitpunkt
            var schule = d.Einstellungen.Schulname.ToUpperInvariant();
            var hk = kopfFett.GetHeight(g);
            g.DrawString(schule, kopfFett, Brushes.Black, r.Left, y);
            // Schuljahr mittig, bei langem Schulnamen rechts daneben
            var sj = $"Schuljahr {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrBis.Year}";
            var sjBreite = g.MeasureString(sj, kopfNormal).Width;
            var sjX = Math.Max(r.Left + r.Width / 2f - sjBreite / 2, r.Left + g.MeasureString(schule, kopfFett).Width + 24);
            g.DrawString(sj, kopfNormal, Brushes.Black, sjX, y + 1);
            using (var rechts = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString($"UntPlan {DateTime.Now.Year}", kopfFett, Brushes.Black, new RectangleF(r.Left, y, r.Width, hk), rechts);
                y += hk + 2;
                g.DrawString(d.Einstellungen.Adresse, kopfNormal, Brushes.Black, r.Left, y);
                g.DrawString(DateTime.Now.ToString("d.M.yyyy  HH:mm"), kopfNormal, Brushes.Black, new RectangleF(r.Left, y, r.Width, hk), rechts);
            }
            y += kopfNormal.GetHeight(g) + 1;
            g.DrawLine(Pens.Black, r.Left, y, r.Right, y);
            y += 16;

            // Titel
            var wochentag = Planungsdienst.TageLang[((int)tag.Datum.DayOfWeek + 6) % 7];
            g.DrawString($"{d.Einstellungen.Vertretungsplantitel} {(lehrerVersion ? "Lehrer" : "Klassen")} am:  {tag.Datum:d.M.} / {wochentag}{(tagAnfang ? "" : "  (Fortsetzung)")}",
                titel, Brushes.Black, r.Left, y);
            y += titel.GetHeight(g) + 14;

            var zh = normal.GetHeight(g) + 5;
            if (tagAnfang)
            {
                // Nachrichten zum Tag (grauer Kasten)
                foreach (var n in tag.Nachrichten)
                {
                    var h = g.MeasureString(n, kopfNormal, r.Width - 8).Height + 8;
                    using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, h);
                    g.DrawString(n, kopfNormal, Brushes.Black, new RectangleF(r.Left + 4, y + 4, r.Width - 8, h));
                    y += h + 6;
                }
                // Blockierte Räume, abwesende Klassen/Lehrer (Rahmen)
                foreach (var (kTitel, kText) in tag.Kaesten)
                {
                    var text = $"{kTitel}:  {kText}";
                    var h = g.MeasureString(text, kopfNormal, r.Width - 16).Height + 10;
                    g.DrawRectangle(Pens.Black, r.Left, y, r.Width, h);
                    g.DrawString(text, kopfNormal, Brushes.Black, new RectangleF(r.Left + 8, y + 6, r.Width - 16, h));
                    y += h + 6;
                }
                y += 14;
            }

            // Spalten: Anteile der Breite
            var anteile = new[] { 0.13f, 0.08f, 0.11f, 0.12f, 0.11f, 0.10f, 0.35f };
            var xs = new float[anteile.Length + 1];
            xs[0] = r.Left;
            for (var i = 0; i < anteile.Length; i++) xs[i + 1] = xs[i] + anteile[i] * r.Width;
            var koepfe = new[] { lehrerVersion ? "Vertreter" : "Klasse(n)", "Stunde", lehrerVersion ? "Klasse(n)" : "Vertreter", "Abwesend", "Fach", "Raum", "Bemerkungen\nund Hinweise" };
            var kopfH = Math.Max(zh, g.MeasureString(koepfe[^1], normal, (int)(xs[7] - xs[6])).Height) + 6;
            using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, kopfH);
            using var unten = new StringFormat { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };
            for (var i = 0; i < koepfe.Length; i++)
                g.DrawString(koepfe[i], i == 0 ? unterstrichen : normal, Brushes.Black, new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, kopfH - 2), unten);
            y += kopfH;
            g.DrawLine(Pens.Black, r.Left, y, r.Right, y);

            if (tag.Zeilen.Count == 0)
            {
                g.DrawString("Keine Vertretungen.", normal, Brushes.DimGray, r.Left + 2, y + 6);
                zeileNr = 0;
            }
            using var keinUmbruch = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var linieDuenn = new Pen(Color.FromArgb(205, 205, 205));
            while (zeileNr < tag.Zeilen.Count && y + zh <= r.Bottom)
            {
                var z = tag.Zeilen[zeileNr];
                var werte = new[] { z.Element, z.Stunde, z.Vertreter, z.Abwesend, z.Fach, z.Raum, z.Bemerkung };
                for (var i = 0; i < werte.Length; i++)
                {
                    var f = i == 0 ? fett : i == 3 && z.Durchgestrichen ? durch : normal;
                    g.DrawString(werte[i], f, Brushes.Black, new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, zh), keinUmbruch);
                    if (i == 3 && z.Durchgestrichen && werte[i].Length > 0)
                    {
                        // Durchstreichen selbst zeichnen (nicht jede Schriftart unterstützt es)
                        var w = Math.Min(g.MeasureString(werte[i], f).Width, xs[i + 1] - xs[i] - 2);
                        using var strich = new Pen(Color.Black, 1.4f);
                        g.DrawLine(strich, xs[i] + 3, y + zh / 2 + 1, xs[i] + w - 1, y + zh / 2 - 1);
                    }
                }
                y += zh;
                var gruppenEnde = zeileNr + 1 >= tag.Zeilen.Count || tag.Zeilen[zeileNr + 1].Element != z.Element;
                g.DrawLine(gruppenEnde ? Pens.Black : linieDuenn, r.Left, y, r.Right, y);
                zeileNr++;
            }
            using (var fuss = new Font(schrift, Math.Max(7f, e.Schriftgroesse - 3)))
            using (var rechts = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString($"Seite {tagNr + 1}/{tage.Count}", fuss, Brushes.DimGray, new RectangleF(r.Left, r.Bottom + 8, r.Width, 20), rechts);

            if (zeileNr >= tag.Zeilen.Count)
            {
                tagNr++;
                zeileNr = 0;
                tagAnfang = true;
            }
            else tagAnfang = false;
            return tagNr < tage.Count;
        }
    }

    private static string SchriftartPruefen(string name) =>
        FontFamily.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? name : "Segoe UI";

    private static Tag TagDaten(Schuldatei d, DateOnly datum, bool lehrer, Vertretungsdruck e)
    {
        var ix = new DatenIndex(d);
        var plan = Vertretungsplan.Zeilen(d, datum);
        if (!lehrer) plan = plan.Where(z => z.ArtWert != VertretungsArt.Pausenaufsicht).ToList();
        var zeilen = plan.Select(z =>
        {
            var vertreter = z.Vertreter;
            var abwesend = z.Lehrer.Length > 0 && z.Lehrer != z.Vertreter ? z.Lehrer : "";
            var fach = string.IsNullOrWhiteSpace(z.Gruppe) ? z.NeuesFach : $"{z.NeuesFach} {z.Gruppe}";
            var raum = z.NeuerRaum.Length > 0 ? z.NeuerRaum : z.Raum;
            var stunde = z.ArtWert == VertretungsArt.Pausenaufsicht ? $"{z.Stunde}/{z.Stunde + 1}" : z.Stunde.ToString();
            if (!lehrer) return new Zeile(z.Klassen, stunde, vertreter, abwesend, abwesend.Length > 0, fach, raum, z.Bemerkung);
            // Lehrerversion: Gruppe = tatsächlich eingesetzte Lehrkraft (bei Entfall die eigentliche Lehrkraft)
            var person = vertreter is "---" or "+" or "???" || vertreter.Length == 0 ? z.Lehrer : vertreter;
            var weg = z.Lehrer.Length > 0 && z.Lehrer != person ? z.Lehrer : "";
            return new Zeile(person, stunde, z.Klassen, weg, weg.Length > 0, fach, raum, z.Bemerkung);
        });
        var sortiert = zeilen.OrderBy(z => z.Element, StringComparer.Create(Format.De, true)).ThenBy(z => int.TryParse(z.Stunde.Split('/')[0], out var s) ? s : 0).ToList();

        var nachrichten = e.Nachrichten
            ? d.Tagestexte.Where(t => t.GiltAm(datum) && (lehrer || !t.FuerLehrer)).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
            : new List<string>();
        var kaesten = new List<(string, string)>();
        string Stunden(Abwesenheit a) => $"({a.StundeVon}-{Math.Min(a.StundeBis, d.Zeitraster.StundenProTag)})";
        var abw = d.Abwesenheiten.Where(a => datum >= a.Von && datum <= a.Bis).ToList();
        if (e.BlockierteRaeume && abw.Where(a => a.Art == AbwesenheitsArt.Raum).ToList() is { Count: > 0 } raeume)
            kaesten.Add(("Blockierte Räume", string.Join(", ", raeume.Select(a => $"{ix.KRaum(a.ElementId)} {Stunden(a)}"))));
        if (e.AbwesendeKlassen && abw.Where(a => a.Art == AbwesenheitsArt.Klasse).ToList() is { Count: > 0 } klassen)
            kaesten.Add(("Abwesende Klassen", string.Join(", ", klassen.Select(a => $"{ix.KKlassen(new[] { a.ElementId })} {Stunden(a)}"))));
        if (lehrer && e.AbwesendeLehrer && abw.Where(a => a.Art == AbwesenheitsArt.Lehrer).ToList() is { Count: > 0 } lehrerAbw)
            kaesten.Add(("Abwesende Lehrer", string.Join(", ", lehrerAbw.Select(a => $"{ix.KLehrer(a.ElementId)} {Stunden(a)}"))));
        return new Tag(datum, nachrichten, kaesten, sortiert);
    }

    /// <summary>Optionen vor der Seitenansicht: Version, Schrift, Inhalte, Format.</summary>
    private static bool Optionen(IWin32Window owner, Vertretungsdruck e, ref bool lehrer)
    {
        using var d = new Dialog("Vertretungsplan drucken", 420, 330);
        var y = 14;
        var version = Ui.Zeile(d, "Version", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 130, 200);
        version.Items.AddRange(new object[] { "Klassen", "Lehrer" });
        version.SelectedIndex = lehrer ? 1 : 0;
        var schrift = Ui.Zeile(d, "Schriftart", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 130, 200);
        schrift.Items.AddRange(FontFamily.Families.Select(f => (object)f.Name).ToArray());
        schrift.SelectedItem = SchriftartPruefen(e.Schriftart);
        var groesse = Ui.Zeile(d, "Schriftgröße", new NumericUpDown { Minimum = 7, Maximum = 18, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Clamp(e.Schriftgroesse, 7, 18) }, ref y, 130, 70);
        var quer = Ui.Zeile(d, "Querformat", new CheckBox { Checked = e.Querformat }, ref y, 130);
        var nachrichten = Ui.Zeile(d, "Nachrichten zum Tag", new CheckBox { Checked = e.Nachrichten }, ref y, 130);
        var raeume = Ui.Zeile(d, "Blockierte Räume", new CheckBox { Checked = e.BlockierteRaeume }, ref y, 130);
        var klassen = Ui.Zeile(d, "Abwesende Klassen", new CheckBox { Checked = e.AbwesendeKlassen }, ref y, 130);
        var lehrerAbw = Ui.Zeile(d, "Abwesende Lehrer", new CheckBox { Checked = e.AbwesendeLehrer }, ref y, 130);
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        lehrer = version.SelectedIndex == 1;
        e.Schriftart = schrift.SelectedItem?.ToString() ?? e.Schriftart;
        e.Schriftgroesse = (float)groesse.Value;
        e.Querformat = quer.Checked;
        e.Nachrichten = nachrichten.Checked;
        e.BlockierteRaeume = raeume.Checked;
        e.AbwesendeKlassen = klassen.Checked;
        e.AbwesendeLehrer = lehrerAbw.Checked;
        return true;
    }
}
