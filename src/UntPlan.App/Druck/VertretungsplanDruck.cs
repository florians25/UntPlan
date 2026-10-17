using System.Drawing.Imaging;
using System.Drawing.Printing;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Druck;

/// <summary>
/// Aushang des Vertretungsplans (Klassen- oder Lehrerversion), je Tag eine neue Seite – gestaltet über das Seitenlayout
/// (<see cref="SeitenlayoutFenster"/>) wie im Vorbild.
/// <list type="bullet">
/// <item>Kopf: Schule, Schuljahr, Programm, Adresse und Druckzeitpunkt; Titel „… Klassen am: 24.9. / Donnerstag“.</item>
/// <item>Nachrichten zum Tag (grauer Kasten); blockierte Räume, abwesende Klassen bzw. Lehrer (mit Absenzgrund) im Rahmen.</item>
/// <item>Tabelle mit den Feldern der Ansicht (Auswahl, Reihenfolge, Überschrift und Breite einstellbar), optional Raster,
/// Überschrift bzw. eigene Seite je Element, Gruppierung, kombinierte Klassen, zusammengefasste Blockvertretungen, Hintergrundbild.</item>
/// </list>
/// Vorschau und Druck verwenden denselben Zeichencode.
/// </summary>
public static class VertretungsplanDruck
{
    /// <summary>Eine Druckzeile mit allen druckbaren Feldern.</summary>
    internal sealed record Zeile(string Element, string Stunde, int? StundeZahl, string Partner, string Abwesend, bool Durchgestrichen, string Fach, string StattFach,
        string Raum, string StattRaum, string Bemerkung, string Art, string VtrNr);

    internal sealed record Tag(DateOnly Datum, List<string> Nachrichten, List<(string Titel, string Text)> Kaesten, List<Zeile> Zeilen);

    /// <summary>Alle druckbaren Felder (Schlüssel) mit Standardanteil, Standardüberschrift je Version und ob sie standardmäßig gedruckt werden.</summary>
    private static readonly (string Schluessel, float Anteil, bool Standard)[] AlleFelder =
    {
        ("Element", 0.13f, true), ("Stunde", 0.08f, true), ("Partner", 0.11f, true), ("Abwesend", 0.12f, true), ("Fach", 0.11f, true),
        ("Raum", 0.10f, true), ("Bemerkung", 0.35f, true), ("Art", 0.12f, false), ("StattFach", 0.09f, false), ("StattRaum", 0.09f, false), ("VtrNr", 0.07f, false),
    };

    /// <summary>Standardüberschrift eines Feldes.</summary>
    public static string StandardUeberschrift(string schluessel, bool lehrer) => schluessel switch
    {
        "Element" => lehrer ? "Vertreter" : "Klasse(n)",
        "Stunde" => "Stunde",
        "Partner" => lehrer ? "Klasse(n)" : "Vertreter",
        "Abwesend" => "Abwesend",
        "Fach" => "Fach",
        "Raum" => "Raum",
        "Bemerkung" => "Bemerkungen\nund Hinweise",
        "Art" => "Art",
        "StattFach" => "statt Fach",
        "StattRaum" => "statt Raum",
        "VtrNr" => "Vtr-Nr.",
        _ => schluessel,
    };

    /// <summary>Felder des Seitenlayouts in Druckreihenfolge (fehlende werden mit Standardwerten ergänzt).</summary>
    public static List<DruckFeld> Felder(Vertretungsdruck e)
    {
        if (e.Felder.Count == 0)
            e.Felder = AlleFelder.Select(f => new DruckFeld { Schluessel = f.Schluessel, Anteil = f.Anteil, Drucken = f.Standard }).ToList();
        foreach (var f in AlleFelder.Where(f => e.Felder.All(x => x.Schluessel != f.Schluessel)))
            e.Felder.Add(new DruckFeld { Schluessel = f.Schluessel, Anteil = f.Anteil, Drucken = false });
        e.Felder.RemoveAll(x => AlleFelder.All(f => f.Schluessel != x.Schluessel));
        return e.Felder;
    }

    public static string Ueberschrift(DruckFeld f, bool lehrer) => f.Ueberschrift.Length > 0 ? f.Ueberschrift : StandardUeberschrift(f.Schluessel, lehrer);

    private static string Wert(Zeile z, string schluessel) => schluessel switch
    {
        "Element" => z.Element,
        "Stunde" => z.Stunde,
        "Partner" => z.Partner,
        "Abwesend" => z.Abwesend,
        "Fach" => z.Fach,
        "Raum" => z.Raum,
        "Bemerkung" => z.Bemerkung,
        "Art" => z.Art,
        "StattFach" => z.StattFach,
        "StattRaum" => z.StattRaum,
        "VtrNr" => z.VtrNr,
        _ => "",
    };

    /// <summary>Seitenansicht: das Seitenlayout mit Vorschau öffnen; Drucken: direkt auf den Drucker.</summary>
    public static void Drucken(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrerVersion, bool vorschau)
    {
        if (vorschau) { SeitenlayoutFenster.Zeigen(owner, von, bis, lehrerVersion); return; }
        Ausgeben(owner, von, bis, lehrerVersion, false);
    }

    /// <summary>Druck bzw. Windows-Seitenansicht mit den aktuellen Einstellungen.</summary>
    public static void Ausgeben(IWin32Window owner, DateOnly von, DateOnly bis, bool lehrerVersion, bool vorschau)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        AppZustand.Einstellungen.Speichern();
        var tage = Tage(von, bis, lehrerVersion, e);
        if (tage.Count == 0) { Ui.Info("Im gewählten Zeitraum gibt es keinen Unterrichtstag."); return; }
        var d = AppZustand.D;
        var doc = new PrintDocument { DocumentName = $"{d.Einstellungen.Vertretungsplantitel} {Format.Datum(von)}" };
        doc.DefaultPageSettings.Landscape = e.Querformat;
        doc.DefaultPageSettings.Margins = new Margins(55, 45, 45, 45);
        var seiten = new Seiten(tage, lehrerVersion, e);
        doc.BeginPrint += (_, _) => seiten.Zuruecksetzen();
        doc.PrintPage += (s, pe) => pe.HasMorePages = seiten.Zeichnen(pe.Graphics!, ListenDruck.Seitenbereich(s, pe));
        ListenDruck.Ausgeben(owner, doc, vorschau);
    }

    private static List<Tag> Tage(DateOnly von, DateOnly bis, bool lehrer, Vertretungsdruck e)
    {
        var d = AppZustand.D;
        return Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)).Select(t => TagDaten(d, t, lehrer, e)).ToList();
    }

    /// <summary>Seiten als Bilder (A4, 100 dpi) – für die Vorschau im Seitenlayout und den Selbsttest ohne Drucker.</summary>
    public static List<Bitmap> Bilder(DateOnly von, DateOnly bis, bool lehrerVersion) => Bilder(von, bis, lehrerVersion, out _);

    /// <summary>Seiten als Bilder; liefert zusätzlich die Lage der Spaltenköpfe der ersten Seite (für die Stifte im Seitenlayout).</summary>
    internal static List<Bitmap> Bilder(DateOnly von, DateOnly bis, bool lehrerVersion, out List<(string Schluessel, RectangleF Kopf)> spalten)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        var tage = Tage(von, bis, lehrerVersion, e);
        var seiten = new Seiten(tage, lehrerVersion, e);
        var res = new List<Bitmap>();
        spalten = new();
        var mehr = tage.Count > 0;
        while (mehr && res.Count < 50)
        {
            var bmp = e.Querformat ? new Bitmap(1169, 827) : new Bitmap(827, 1169);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Display;
            mehr = seiten.Zeichnen(g, new Rectangle(55, 45, bmp.Width - 100, bmp.Height - 90));
            if (res.Count == 0) spalten = seiten.Spaltenkoepfe.ToList();
            res.Add(bmp);
        }
        return res;
    }

    /// <summary>Zeilen eines Tages nach dem Seitenlayout (für Selbsttest und Vorschau).</summary>
    internal static List<Zeile> ZeilenAm(DateOnly datum, bool lehrer) => TagDaten(AppZustand.D, datum, lehrer, AppZustand.Einstellungen.Vertretungsdruck).Zeilen;

    /// <summary>Seitenweises Zeichnen mit Fortsetzung über Seitengrenzen.</summary>
    private sealed class Seiten
    {
        private readonly List<Tag> _tage;
        private readonly bool _lehrer;
        private readonly Vertretungsdruck _e;
        private int _tagNr, _zeileNr;
        private bool _tagAnfang = true;

        /// <summary>Lage der Spaltenköpfe der zuletzt gezeichneten Seite.</summary>
        public List<(string Schluessel, RectangleF Kopf)> Spaltenkoepfe { get; } = new();

        public Seiten(List<Tag> tage, bool lehrer, Vertretungsdruck e) { _tage = tage; _lehrer = lehrer; _e = e; }

        public void Zuruecksetzen() { _tagNr = 0; _zeileNr = 0; _tagAnfang = true; }

        /// <summary>Zeichnet die nächste Seite; liefert true, wenn weitere Seiten folgen.</summary>
        public bool Zeichnen(Graphics g, Rectangle r)
        {
            var d = AppZustand.D;
            var e = _e;
            var tage = _tage;
            var lehrerVersion = _lehrer;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            HintergrundZeichnen(g, r, e);
            var schrift = SchriftartPruefen(e.Schriftart);
            using var normal = new Font(schrift, e.Schriftgroesse);
            using var fett = new Font(schrift, e.Schriftgroesse, FontStyle.Bold);
            using var durch = new Font(schrift, e.Schriftgroesse, FontStyle.Strikeout | FontStyle.Italic);
            using var kopfFett = new Font(schrift, e.Schriftgroesse + 2, FontStyle.Bold);
            using var unterstrichen = new Font(schrift, e.Schriftgroesse, FontStyle.Bold | FontStyle.Underline);
            using var kopfNormal = new Font(schrift, e.Schriftgroesse + 1);
            using var titel = new Font(schrift, e.Schriftgroesse + 8, FontStyle.Bold);
            var tag = tage[_tagNr];
            var y = (float)r.Top;

            // Kopf: Schule | Schuljahr | Programm – Adresse | Druckzeitpunkt
            var schule = d.Einstellungen.Schulname.ToUpperInvariant();
            var hk = kopfFett.GetHeight(g);
            g.DrawString(schule, kopfFett, Brushes.Black, r.Left, y);
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
            var titelText = e.Ueberschrift.Length > 0 ? e.Ueberschrift : $"{d.Einstellungen.Vertretungsplantitel} {(lehrerVersion ? "Lehrer" : "Klassen")}";
            g.DrawString($"{titelText} am:  {tag.Datum:d.M.} / {wochentag}{(_tagAnfang ? "" : "  (Fortsetzung)")}", titel, Brushes.Black, r.Left, y);
            y += titel.GetHeight(g) + 14;

            var zh = normal.GetHeight(g) + 5;
            if (_tagAnfang)
            {
                foreach (var n in tag.Nachrichten)
                {
                    var h = g.MeasureString(n, kopfNormal, r.Width - 8).Height + 8;
                    using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, h);
                    g.DrawString(n, kopfNormal, Brushes.Black, new RectangleF(r.Left + 4, y + 4, r.Width - 8, h));
                    y += h + 6;
                }
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

            // Spalten laut Seitenlayout (Felder der Ansicht): Anteile der Breite
            var felder = Felder(e).Where(f => f.Drucken).ToList();
            if (felder.Count == 0) felder = Felder(e).Take(1).ToList();
            var summe = felder.Sum(f => Math.Max(0.02f, f.Anteil));
            var xs = new float[felder.Count + 1];
            xs[0] = r.Left;
            for (var i = 0; i < felder.Count; i++) xs[i + 1] = xs[i] + Math.Max(0.02f, felder[i].Anteil) / summe * r.Width;
            var koepfe = felder.Select(f => Ueberschrift(f, lehrerVersion)).ToArray();
            var kopfH = Math.Max(zh, koepfe.Select((k, i) => g.MeasureString(k, normal, (int)Math.Max(10, xs[i + 1] - xs[i])).Height).DefaultIfEmpty(zh).Max()) + 6;
            var tabelleOben = y;
            using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, kopfH);
            using var unten = new StringFormat { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };
            Spaltenkoepfe.Clear();
            for (var i = 0; i < koepfe.Length; i++)
            {
                var kopf = new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, kopfH - 2);
                g.DrawString(koepfe[i], i == 0 ? unterstrichen : normal, Brushes.Black, kopf, unten);
                Spaltenkoepfe.Add((felder[i].Schluessel, kopf));
            }
            y += kopfH;
            g.DrawLine(Pens.Black, r.Left, y, r.Right, y);

            if (tag.Zeilen.Count == 0)
            {
                g.DrawString("Keine Vertretungen.", normal, Brushes.DimGray, r.Left + 2, y + 6);
                _zeileNr = 0;
            }
            using var keinUmbruch = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using var linieDuenn = new Pen(Color.FromArgb(205, 205, 205));
            var ersteAufSeite = true;
            while (_zeileNr < tag.Zeilen.Count && y + zh <= r.Bottom)
            {
                var z = tag.Zeilen[_zeileNr];
                var gruppenAnfang = _zeileNr == 0 || Gruppe(tag.Zeilen[_zeileNr - 1]) != Gruppe(z);
                // Gliederung: eine Seite je Element (neue Seite am Gruppenanfang, außer ganz oben)
                if (e.EineSeiteJeElement && gruppenAnfang && !ersteAufSeite) break;
                if (e.UeberschriftJeElement && gruppenAnfang)
                {
                    var uh = e.ElementueberschriftKomprimiert ? zh : zh + 6;
                    if (y + uh + zh > r.Bottom) break;
                    g.DrawString(Gruppe(z), kopfFett, Brushes.Black, new RectangleF(r.Left + 2, y + (uh - zh) / 2, r.Width, zh + 2), keinUmbruch);
                    y += uh;
                    g.DrawLine(Pens.Black, r.Left, y, r.Right, y);
                }
                for (var i = 0; i < felder.Count; i++)
                {
                    var key = felder[i].Schluessel;
                    var wert = Wert(z, key);
                    // Elementspalte nur in der ersten Zeile der Gruppe (und oben auf jeder Seite, wenn eingestellt)
                    if (key == "Element" && !gruppenAnfang && !(ersteAufSeite && e.ErsteBeschriftungJedeSeite)) wert = "";
                    var gestrichen = key == "Abwesend" && z.Durchgestrichen;
                    var f = key == "Element" ? fett : gestrichen ? durch : normal;
                    g.DrawString(wert, f, Brushes.Black, new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, zh), keinUmbruch);
                    if (gestrichen && wert.Length > 0)
                    {
                        // Durchstreichen selbst zeichnen (nicht jede Schriftart unterstützt es)
                        var w = Math.Min(g.MeasureString(wert, f).Width, xs[i + 1] - xs[i] - 2);
                        using var strich = new Pen(Color.Black, 1.4f);
                        g.DrawLine(strich, xs[i] + 3, y + zh / 2 + 1, xs[i] + w - 1, y + zh / 2 - 1);
                    }
                }
                y += zh;
                var gruppenEnde = _zeileNr + 1 >= tag.Zeilen.Count || Gruppe(tag.Zeilen[_zeileNr + 1]) != Gruppe(z);
                g.DrawLine(gruppenEnde ? Pens.Black : linieDuenn, r.Left, y, r.Right, y);
                _zeileNr++;
                ersteAufSeite = false;
            }
            // Raster: senkrechte Linien zwischen den Feldern und Rahmen um die Tabelle
            if (e.Raster)
            {
                for (var i = 0; i <= felder.Count; i++) g.DrawLine(Pens.Black, xs[i], tabelleOben, xs[i], y);
                g.DrawLine(Pens.Black, r.Left, tabelleOben, r.Right, tabelleOben);
            }
            using (var fuss = new Font(schrift, Math.Max(7f, e.Schriftgroesse - 3)))
            using (var rechts = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString($"Seite {_tagNr + 1}/{tage.Count}", fuss, Brushes.DimGray, new RectangleF(r.Left, r.Bottom + 8, r.Width, 20), rechts);

            if (_zeileNr >= tag.Zeilen.Count)
            {
                _tagNr++;
                _zeileNr = 0;
                _tagAnfang = true;
            }
            else _tagAnfang = false;
            return _tagNr < tage.Count;

            string Gruppe(Zeile x) => e.Gruppierung switch
            {
                "Stunde" => x.Stunde,
                "Vertreter" => x.Partner,
                "Fach" => x.Fach,
                _ => x.Element,
            };
        }
    }

    /// <summary>Hintergrundbild des Seitenlayouts, stark aufgehellt über die ganze Seite.</summary>
    private static void HintergrundZeichnen(Graphics g, Rectangle r, Vertretungsdruck e)
    {
        if (string.IsNullOrWhiteSpace(e.Hintergrundbild) || !File.Exists(e.Hintergrundbild)) return;
        try
        {
            using var bild = Image.FromFile(e.Hintergrundbild);
            var faktor = Math.Min(r.Width / (float)bild.Width, r.Height / (float)bild.Height);
            var w = bild.Width * faktor;
            var h = bild.Height * faktor;
            var ziel = Rectangle.Round(new RectangleF(r.Left + (r.Width - w) / 2, r.Top + (r.Height - h) / 2, w, h));
            using var attr = new ImageAttributes();
            attr.SetColorMatrix(new ColorMatrix { Matrix33 = 0.15f });
            g.DrawImage(bild, ziel, 0, 0, bild.Width, bild.Height, GraphicsUnit.Pixel, attr);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or IOException or ArgumentException) { }
    }

    /// <summary>
    /// Zusammengefasste Veranstaltungszeile („Alle Klassen“, „Alle Lehrkräfte außer A, B“) für eine Auswahl von Klassen bzw. Lehrkräften:
    /// gilt für jedes gewählte Element, das nicht hinter „außer“ steht.
    /// </summary>
    internal static bool AlleTrifft(string element, ICollection<string> auswahl)
    {
        if (!element.StartsWith("Alle ", StringComparison.Ordinal)) return false;
        var i = element.IndexOf(" außer ", StringComparison.Ordinal);
        if (i < 0) return true;
        var ausser = element[(i + 7)..].Split(',').Select(x => x.Trim()).ToHashSet();
        return auswahl.Any(a => !ausser.Contains(a));
    }

    internal static string SchriftartPruefen(string name) =>
        FontFamily.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? name : "Segoe UI";

    private static Tag TagDaten(Schuldatei d, DateOnly datum, bool lehrer, Vertretungsdruck e)
    {
        var ix = new DatenIndex(d);
        var plan = Vertretungsplan.Zeilen(d, datum);
        var vertretungen = d.Vertretungen.Where(v => v.Datum == datum).ToDictionary(v => v.Id);
        // Seitenlayout „Nur neue Vertretungen“: in dieser Arbeitssitzung angelegt oder geändert
        if (e.NurNeue)
            plan = plan.Where(z => vertretungen.TryGetValue(z.Id, out var v) && Vertretungen.IstNeu(v, AppZustand.Doc.Sitzungsbeginn)).ToList();
        // „Druck nur, wenn geändert nach …“
        if (e.NurGeaendertNach is DateTime ab)
            plan = plan.Where(z => vertretungen.TryGetValue(z.Id, out var v) && (v.Geaendert >= ab || v.Erstellt >= ab)).ToList();
        if (!lehrer) plan = plan.Where(z => z.ArtWert != VertretungsArt.Pausenaufsicht).ToList();
        // „Kein Entfall bei Ersatz in Stunde“: Entfall nicht drucken, wenn die Klasse in derselben Stunde Ersatz bekommt
        if (e.KeinEntfallBeiErsatz)
        {
            var ersatz = plan.Where(z => z.ArtWert != VertretungsArt.Entfall).SelectMany(z => z.KlassenIds.Select(k => (k, z.Stunde))).ToHashSet();
            plan = plan.Where(z => z.ArtWert != VertretungsArt.Entfall || !z.KlassenIds.All(k => ersatz.Contains((k, z.Stunde)))).ToList();
        }
        string Klassen(string k) => e.KombinierteKlassen ? Druckaufbereitung.KlassenKombinieren(k) : k;
        string StundeText(int s) => e.Stundenbezeichnung ? d.Zeitraster.StundenLabel(s) + "." : s.ToString();
        var zeilen = plan.Select(z =>
        {
            var vertreter = z.Vertreter;
            var abwesend = z.Lehrer.Length > 0 && z.Lehrer != z.Vertreter ? z.Lehrer : "";
            var fach = string.IsNullOrWhiteSpace(z.Gruppe) ? z.NeuesFach : $"{z.NeuesFach} {z.Gruppe}";
            var raum = z.NeuerRaum.Length > 0 ? z.NeuerRaum : z.Raum;
            var stunde = z.ArtWert == VertretungsArt.Pausenaufsicht ? $"{z.Stunde}/{z.Stunde + 1}" : StundeText(z.Stunde);
            int? zahl = z.ArtWert == VertretungsArt.Pausenaufsicht ? null : z.Stunde;
            var art = Vertretungsplan.ArtText(z.ArtWert);
            var nr = z.Id > 0 ? z.Id.ToString() : "";
            if (e.AbwesendeNichtDrucken) abwesend = "";
            var streichen = e.EntfallslehrerDurchstreichen;
            if (!lehrer)
                return new Zeile(Klassen(z.Klassen), stunde, zahl, vertreter, abwesend, streichen && abwesend.Length > 0, fach, z.Fach, raum, z.Raum, z.Bemerkung, art, nr);
            // Lehrerversion: Gruppe = tatsächlich eingesetzte Lehrkraft (bei Entfall die eigentliche Lehrkraft)
            var person = vertreter is "---" or "+" or "+++" or "???" || vertreter.Length == 0 ? z.Lehrer : vertreter;
            var weg = z.Lehrer.Length > 0 && z.Lehrer != person && !e.AbwesendeNichtDrucken ? z.Lehrer : "";
            return new Zeile(person, stunde, zahl, Klassen(z.Klassen), weg, streichen && weg.Length > 0, fach, z.Fach, raum, z.Raum, z.Bemerkung, art, nr);
        }).ToList();
        // Auswahl der Klassen bzw. Lehrer
        var auswahl = lehrer ? e.LehrerAuswahl : e.KlassenAuswahl;
        if (auswahl.Count > 0)
            zeilen = zeilen.Where(z => z.Element.Split(',').Any(x => auswahl.Contains(x.Trim())) || AlleTrifft(z.Element, auswahl)).ToList();
        // „Auch Elemente ohne Vertretung“
        if (e.AuchElementeOhneVertretung)
        {
            var alle = lehrer ? d.Lehrer.Select(x => x.Kurzname) : d.Klassen.Select(x => x.Kurzname);
            foreach (var name in alle.Where(n => (auswahl.Count == 0 || auswahl.Contains(n)) && !zeilen.Any(z => z.Element.Split(',').Contains(n) || AlleTrifft(z.Element, new[] { n }))))
                zeilen.Add(new Zeile(name, "", null, "", "", false, "", "", "", "", "keine Vertretung", "", ""));
        }
        var cmp = StringComparer.Create(Format.De, true);
        IOrderedEnumerable<Zeile> sortiert = e.Gruppierung switch
        {
            "Stunde" => zeilen.OrderBy(z => z.StundeZahl ?? int.MaxValue).ThenBy(z => z.Element, cmp),
            "Vertreter" => zeilen.OrderBy(z => z.Partner, cmp).ThenBy(z => z.StundeZahl ?? 0),
            "Fach" => zeilen.OrderBy(z => z.Fach, cmp).ThenBy(z => z.StundeZahl ?? 0),
            _ => zeilen.OrderBy(z => z.Element, cmp).ThenBy(z => z.StundeZahl ?? 0),
        };
        var liste = sortiert.ToList();
        if (e.BlockvertretungenZusammenfassen)
            liste = Druckaufbereitung.Zusammenfassen(liste, z => z.StundeZahl,
                z => string.Join("|", z.Element, z.Partner, z.Abwesend, z.Fach, z.StattFach, z.Raum, z.StattRaum, z.Bemerkung, z.Art),
                (z, s) => z with { Stunde = s });

        var nachrichten = e.Nachrichten
            ? d.Tagestexte.Where(t => t.GiltAm(datum) && (lehrer || !t.FuerLehrer)).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
            : new List<string>();
        var kaesten = new List<(string, string)>();
        string Stunden(Abwesenheit a) => $"({a.StundeVon}-{Math.Min(a.StundeBis, d.Zeitraster.StundenProTag)})";
        string Grund(Abwesenheit a) => e.LehrerMitAbsenzgrund && a.GrundId is int gid && d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == gid) is { } gr ? $" {gr.Kurzname}" : "";
        var abw = d.Abwesenheiten.Where(a => datum >= a.Von && datum <= a.Bis).ToList();
        if (e.BlockierteRaeume && abw.Where(a => a.Art == AbwesenheitsArt.Raum).ToList() is { Count: > 0 } raeume)
            kaesten.Add(("Blockierte Räume", string.Join(", ", raeume.Select(a => $"{ix.KRaum(a.ElementId)} {Stunden(a)}"))));
        if (e.AbwesendeKlassen && abw.Where(a => a.Art == AbwesenheitsArt.Klasse).ToList() is { Count: > 0 } klassen)
            kaesten.Add(("Abwesende Klassen", string.Join(", ", klassen.Select(a => $"{ix.KKlassen(new[] { a.ElementId })} {Stunden(a)}"))));
        if (lehrer && e.AbwesendeLehrer && abw.Where(a => a.Art == AbwesenheitsArt.Lehrer).ToList() is { Count: > 0 } lehrerAbw)
            kaesten.Add(("Abwesende Lehrer", string.Join(", ", lehrerAbw.Select(a => $"{ix.KLehrer(a.ElementId)} {Stunden(a)}{Grund(a)}"))));
        return new Tag(datum, nachrichten, kaesten, liste);
    }
}
