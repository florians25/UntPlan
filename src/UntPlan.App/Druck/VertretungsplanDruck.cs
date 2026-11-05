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
    internal static List<Bitmap> Bilder(DateOnly von, DateOnly bis, bool lehrerVersion, out List<(string Schluessel, RectangleF Kopf)> spalten) =>
        Bilder(von, bis, lehrerVersion, out spalten, out _);

    /// <summary>Seiten als Bilder; zusätzlich die Lage der Listenüberschrift der ersten Seite (Stift „Listenüberschrift“).</summary>
    internal static List<Bitmap> Bilder(DateOnly von, DateOnly bis, bool lehrerVersion, out List<(string Schluessel, RectangleF Kopf)> spalten, out RectangleF titel)
    {
        var e = AppZustand.Einstellungen.Vertretungsdruck;
        var tage = Tage(von, bis, lehrerVersion, e);
        var seiten = new Seiten(tage, lehrerVersion, e);
        var res = new List<Bitmap>();
        spalten = new();
        titel = RectangleF.Empty;
        var mehr = tage.Count > 0;
        while (mehr && res.Count < 50)
        {
            var bmp = e.Querformat ? new Bitmap(1169, 827) : new Bitmap(827, 1169);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Display;
            mehr = seiten.Zeichnen(g, new Rectangle(55, 45, bmp.Width - 100, bmp.Height - 90));
            if (res.Count == 0) { spalten = seiten.Spaltenkoepfe.ToList(); titel = seiten.Titelbereich; }
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
        private readonly Dictionary<string, string> _langLehrer, _langKlassen, _langFaecher, _langRaeume;

        /// <summary>Lage der Spaltenköpfe der zuletzt gezeichneten Seite.</summary>
        public List<(string Schluessel, RectangleF Kopf)> Spaltenkoepfe { get; } = new();

        /// <summary>Lage der Listenüberschrift der zuletzt gezeichneten Seite.</summary>
        public RectangleF Titelbereich { get; private set; }

        public Seiten(List<Tag> tage, bool lehrer, Vertretungsdruck e)
        {
            _tage = tage; _lehrer = lehrer; _e = e;
            var d = AppZustand.D;
            static Dictionary<string, string> Lang(IEnumerable<Element> liste)
            {
                var res = new Dictionary<string, string>();
                foreach (var x in liste) if (x.Kurzname.Length > 0 && x.Langname.Length > 0) res.TryAdd(x.Kurzname, x.Langname);
                return res;
            }
            _langLehrer = Lang(d.Lehrer);
            _langKlassen = Lang(d.Klassen);
            _langFaecher = Lang(d.Faecher);
            _langRaeume = Lang(d.Raeume);
        }

        public void Zuruecksetzen() { _tagNr = 0; _zeileNr = 0; _tagAnfang = true; }

        /// <summary>Kurznamen einer Aufzählung („1a,1b“) durch Langnamen ersetzen (unbekannte bleiben).</summary>
        private static string Langnamen(string text, Dictionary<string, string> lang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var teile = text.Split(',').Select(t => t.Trim()).ToList();
            if (teile.Count == 1)
            {
                // „Ma G1“ (Fach mit Schülergruppe): nur das erste Wort ersetzen
                var i = text.IndexOf(' ');
                var kopf = i > 0 ? text[..i] : text;
                return lang.TryGetValue(kopf, out var l) ? l + (i > 0 ? text[i..] : "") : text;
            }
            return string.Join(", ", teile.Select(t => lang.TryGetValue(t, out var l) ? l : t));
        }

        private Dictionary<string, string>? LangFuer(string schluessel) => schluessel switch
        {
            "Element" => _lehrer ? _langLehrer : _langKlassen,
            "Partner" => _lehrer ? _langKlassen : _langLehrer,
            "Abwesend" => _langLehrer,
            "Fach" or "StattFach" => _langFaecher,
            "Raum" or "StattRaum" => _langRaeume,
            _ => null,
        };

        /// <summary>Gedruckter Wert eines Feldes (Symbolleiste „Wechsel Kurzname / Langname“).</summary>
        private string Anzeige(Zeile z, string schluessel)
        {
            var w = Wert(z, schluessel);
            return _e.Langname && LangFuer(schluessel) is { } lang ? Langnamen(w, lang) : w;
        }

        /// <summary>Langname zum Gruppenwert (Elementüberschrift).</summary>
        private string GruppeLang(string gruppe)
        {
            var schluessel = _e.Gruppierung switch { "Vertreter" => "Partner", "Stunde" or "Fach" or "Raum" or "Art" or "Abwesend" or "VtrNr" => _e.Gruppierung, _ => "Element" };
            return LangFuer(schluessel) is { } lang ? Langnamen(gruppe, lang) : gruppe;
        }

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
            using var normal = new Font(schrift, e.Schriftgroesse, e.TabelleFett ? FontStyle.Bold : FontStyle.Regular);
            using var fett = new Font(schrift, e.Schriftgroesse, FontStyle.Bold);
            using var durch = new Font(schrift, e.Schriftgroesse, (e.TabelleFett ? FontStyle.Bold : FontStyle.Regular) | FontStyle.Strikeout | FontStyle.Italic);
            using var kopfFett = new Font(schrift, e.Schriftgroesse + 2, FontStyle.Bold);
            using var spaltenKopf = new Font(schrift, e.Schriftgroesse);
            using var unterstrichen = new Font(schrift, e.Schriftgroesse, FontStyle.Bold | FontStyle.Underline);
            using var kopfNormal = new Font(schrift, e.Schriftgroesse + 1);
            using var titel = new Font(schrift, e.Schriftgroesse + 8, FontStyle.Bold);
            using var absenzSchrift = new Font(schrift, Math.Max(5f, (e.Schriftgroesse + 1) * Math.Clamp(e.AbsenzkopfGroesse, 25, 400) / 100f));
            using var absenzFett = new Font(absenzSchrift, FontStyle.Bold);
            using var tagestextSchrift = new Font(schrift, Math.Max(5f, (e.Schriftgroesse + 1) * Math.Clamp(e.TagestexteGroesse, 25, 400) / 100f));
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

            // Titel (Listenüberschrift)
            var wochentag = Planungsdienst.TageLang[((int)tag.Datum.DayOfWeek + 6) % 7];
            var titelText = e.Ueberschrift.Length > 0 ? e.Ueberschrift : $"{d.Einstellungen.Vertretungsplantitel} {(lehrerVersion ? "Lehrer" : "Klassen")}";
            var titelZeile = $"{titelText} am:  {tag.Datum:d.M.} / {wochentag}{(_tagAnfang ? "" : "  (Fortsetzung)")}";
            g.DrawString(titelZeile, titel, Brushes.Black, r.Left, y);
            Titelbereich = new RectangleF(r.Left, y, g.MeasureString(titelZeile, titel).Width, titel.GetHeight(g));
            y += titel.GetHeight(g) + 14;

            var zh = normal.GetHeight(g) + 5;
            if (_tagAnfang)
            {
                // Tagestexte (Global: Schriftgröße in %, Ausdruck mit Rahmen)
                foreach (var n in tag.Nachrichten)
                {
                    var h = g.MeasureString(n, tagestextSchrift, r.Width - 8).Height + 8;
                    using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, h);
                    if (e.TagestexteRahmen) g.DrawRectangle(Pens.Black, r.Left, y, r.Width, h);
                    g.DrawString(n, tagestextSchrift, Brushes.Black, new RectangleF(r.Left + 4, y + 4, r.Width - 8, h));
                    y += h + 6;
                }
                // Absenzkopf: „Abwesende Lehrer:  …“ je Zeile, Beschriftungen in einer Spalte, optional mit Rahmen
                if (tag.Kaesten.Count > 0)
                {
                    var labelBreite = tag.Kaesten.Max(k => g.MeasureString(k.Titel + ":", absenzFett).Width) + 10;
                    var textBreite = Math.Max(40, r.Width - 16 - labelBreite);
                    var oben = y;
                    y += 5;
                    foreach (var (kTitel, kText) in tag.Kaesten)
                    {
                        var h = g.MeasureString(kText, absenzSchrift, (int)textBreite).Height;
                        g.DrawString(kTitel + ":", absenzFett, Brushes.Black, r.Left + 8, y);
                        g.DrawString(kText, absenzSchrift, Brushes.Black, new RectangleF(r.Left + 8 + labelBreite, y, textBreite, h + 2));
                        y += h + 2;
                    }
                    y += 4;
                    if (e.AbsenzkopfRahmen) g.DrawRectangle(Pens.Black, r.Left, oben, r.Width, y - oben);
                    y += 6;
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
            float kopfH;
            if (e.UeberschriftSchnitt == 0)
                kopfH = Math.Max(zh, koepfe.Select((k, i) => g.MeasureString(k, spaltenKopf, (int)Math.Max(10, xs[i + 1] - xs[i])).Height).DefaultIfEmpty(zh).Max()) + 6;
            else
            {
                // gedrehte Überschriften: Höhe nach der längsten Beschriftung
                var laengste = koepfe.Select(k => g.MeasureString(k.Replace('\n', ' '), spaltenKopf).Width).DefaultIfEmpty(0).Max();
                kopfH = e.UeberschriftSchnitt == 2 ? laengste + 10 : laengste * 0.72f + zh;
            }
            var tabelleOben = y;
            using (var grau = new SolidBrush(Color.FromArgb(236, 236, 236))) g.FillRectangle(grau, r.Left, y, r.Width, kopfH);
            using var unten = new StringFormat { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };
            Spaltenkoepfe.Clear();
            for (var i = 0; i < koepfe.Length; i++)
            {
                var kopf = new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, kopfH - 2);
                var kf = i == 0 ? unterstrichen : spaltenKopf;
                if (e.UeberschriftSchnitt == 0) g.DrawString(koepfe[i], kf, Brushes.Black, kopf, unten);
                else
                {
                    var zustand = g.Save();
                    var text = koepfe[i].Replace('\n', ' ');
                    var th = kf.GetHeight(g);
                    if (e.UeberschriftSchnitt == 2)
                    {
                        g.TranslateTransform(kopf.Left + (kopf.Width - th) / 2, kopf.Bottom - 3);
                        g.RotateTransform(-90);
                    }
                    else
                    {
                        g.TranslateTransform(kopf.Left + 2, kopf.Bottom - th * 0.8f);
                        g.RotateTransform(-45);
                    }
                    g.DrawString(text, kf, Brushes.Black, 0, 0);
                    g.Restore(zustand);
                }
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
            using var umbruch = new StringFormat { Trimming = StringTrimming.EllipsisWord, LineAlignment = StringAlignment.Near };
            using var linieDuenn = new Pen(Color.FromArgb(205, 205, 205));
            var ersteAufSeite = true;
            while (_zeileNr < tag.Zeilen.Count && y + zh <= r.Bottom)
            {
                var z = tag.Zeilen[_zeileNr];
                var gruppenAnfang = _zeileNr == 0 || Gruppe(tag.Zeilen[_zeileNr - 1]) != Gruppe(z);
                // Gliederung: eine Seite je Element (neue Seite am Gruppenanfang, außer ganz oben)
                if (e.EineSeiteJeElement && gruppenAnfang && !ersteAufSeite) break;
                var werte = felder.Select(f => Anzeige(z, f.Schluessel)).ToArray();
                // Zeilenumbruch: Zeile so hoch wie der längste umbrochene Text
                var hoehe = zh;
                if (e.Zeilenumbruch)
                    for (var i = 0; i < felder.Count; i++)
                        hoehe = Math.Max(hoehe, g.MeasureString(werte[i], normal, (int)Math.Max(10, xs[i + 1] - xs[i] - 2)).Height + 4);
                if (e.UeberschriftJeElement && gruppenAnfang)
                {
                    var uh = UeberschriftZeichnen(g, r, y, Gruppe(z), fett, true);
                    if (y + uh + hoehe > r.Bottom && !ersteAufSeite) break;
                    UeberschriftZeichnen(g, r, y, Gruppe(z), fett, false);
                    y += uh;
                    g.DrawLine(Pens.Black, r.Left, y, r.Right, y);
                }
                if (y + hoehe > r.Bottom && !ersteAufSeite) break;
                for (var i = 0; i < felder.Count; i++)
                {
                    var key = felder[i].Schluessel;
                    var wert = werte[i];
                    // Elementspalte nur in der ersten Zeile der Gruppe (und oben auf jeder Seite, wenn eingestellt)
                    if (key == "Element" && !gruppenAnfang && !(ersteAufSeite && e.ErsteBeschriftungJedeSeite)) wert = "";
                    var gestrichen = key == "Abwesend" && z.Durchgestrichen;
                    var f = key == "Element" ? fett : gestrichen ? durch : normal;
                    g.DrawString(wert, f, Brushes.Black, new RectangleF(xs[i] + 1, y + (e.Zeilenumbruch ? 2 : 0), xs[i + 1] - xs[i] - 2, hoehe), e.Zeilenumbruch ? umbruch : keinUmbruch);
                    if (gestrichen && wert.Length > 0)
                    {
                        // Durchstreichen selbst zeichnen (nicht jede Schriftart unterstützt es)
                        var w = Math.Min(g.MeasureString(wert, f).Width, xs[i + 1] - xs[i] - 2);
                        var my = e.Zeilenumbruch ? y + zh / 2 : y + hoehe / 2;
                        using var strich = new Pen(Color.Black, 1.4f);
                        g.DrawLine(strich, xs[i] + 3, my + 1, xs[i] + w - 1, my - 1);
                    }
                }
                y += hoehe;
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
                "Vertreter" => x.Partner,
                "Stunde" or "Fach" or "Raum" or "Art" or "Abwesend" or "VtrNr" => Wert(x, e.Gruppierung),
                _ => x.Element,
            };
        }

        /// <summary>
        /// Elementüberschrift aus den Überschriftsfeldern (Feldart und Größe in %) – komprimiert einzeilig in Tabellenschrift.
        /// Liefert die Höhe; mit <paramref name="nurMessen"/> wird nichts gezeichnet.
        /// </summary>
        private float UeberschriftZeichnen(Graphics g, Rectangle r, float y, string gruppe, Font fett, bool nurMessen)
        {
            var e = _e;
            var teile = (e.UeberschriftsFelder.Count == 0 ? new List<UeberschriftsFeld> { new() { Feld = "Kurzname", Groesse = 200 } } : e.UeberschriftsFelder)
                .Where(f => f.Feld is "Kurzname" or "Langname")
                .Select(f => (Text: f.Feld == "Langname" ? GruppeLang(gruppe) : gruppe, f.Groesse)).ToList();
            if (teile.Count == 0) teile.Add((gruppe, 100));
            if (e.ElementueberschriftKomprimiert)
            {
                var zh = fett.GetHeight(g) + 5;
                if (!nurMessen)
                {
                    using var grau = new SolidBrush(Color.FromArgb(244, 244, 244));
                    g.FillRectangle(grau, r.Left, y, r.Width, zh);
                    g.DrawString(string.Join("  ", teile.Select(t => t.Text).Distinct()), fett, Brushes.Black, r.Left + 2, y + 2);
                }
                return zh;
            }
            var schriften = teile.Select(t => new Font(fett.FontFamily, Math.Max(5f, fett.Size * Math.Clamp(t.Groesse, 25, 400) / 100f))).ToList();
            try
            {
                var hoehe = schriften.Max(s => s.GetHeight(g));
                if (!nurMessen)
                {
                    var x = (float)r.Left + 2;
                    for (var i = 0; i < teile.Count; i++)
                    {
                        if (i > 0 && teile[i].Text == teile[i - 1].Text) continue;
                        var sh = schriften[i].GetHeight(g);
                        g.DrawString(teile[i].Text, schriften[i], Brushes.Black, x, y + 4 + hoehe - sh);
                        x += g.MeasureString(teile[i].Text, schriften[i]).Width + 8;
                    }
                }
                return hoehe + 10;
            }
            finally { foreach (var s in schriften) s.Dispose(); }
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

    /// <summary>Sortierschlüssel des Seitenlayouts (Gruppierung bzw. Sortierkriterien) mit Anzeigenamen je Version.</summary>
    public static (string Schluessel, string Text)[] Sortierfelder(bool lehrer) => new[]
    {
        ("Element", lehrer ? "Lehrer und Vertreter" : "Klasse(n)"), ("Stunde", "Std."), ("Vertreter", lehrer ? "Klasse(n)" : "Vertreter"),
        ("Fach", "Fach"), ("Raum", "Raum"), ("Art", "Art"), ("Abwesend", "(Lehrer)"), ("VtrNr", "Vtr-Nr."),
    };

    private static IOrderedEnumerable<Zeile> Sortieren(IEnumerable<Zeile> zeilen, string schluessel, bool absteigend, IOrderedEnumerable<Zeile>? bisher)
    {
        var cmp = StringComparer.Create(Format.De, true);
        if (schluessel is "Stunde" or "VtrNr")
        {
            // Pausenaufsicht „3/4“ steht zwischen der 3. und 4. Stunde
            Func<Zeile, double> zahl = schluessel == "Stunde"
                ? z => z.StundeZahl ?? (int.TryParse(z.Stunde.Split('/')[0], out var p) && z.Stunde.Contains('/') ? p + 0.5 : double.MaxValue)
                : z => int.TryParse(z.VtrNr, out var n) ? n : double.MaxValue;
            return bisher is null ? (absteigend ? zeilen.OrderByDescending(zahl) : zeilen.OrderBy(zahl)) : absteigend ? bisher.ThenByDescending(zahl) : bisher.ThenBy(zahl);
        }
        Func<Zeile, string> text = schluessel == "Vertreter" ? z => z.Partner : z => Wert(z, schluessel);
        return bisher is null ? (absteigend ? zeilen.OrderByDescending(text, cmp) : zeilen.OrderBy(text, cmp)) : absteigend ? bisher.ThenByDescending(text, cmp) : bisher.ThenBy(text, cmp);
    }

    internal static string SchriftartPruefen(string name) =>
        FontFamily.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? name : "Segoe UI";

    private static Tag TagDaten(Schuldatei d, DateOnly datum, bool lehrer, Vertretungsdruck e)
    {
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
        // Sortierkriterien: 1 = Gruppierung, 2 = „dann nach“ (je auf- oder absteigend), danach Element und Stunde
        var sortiert = Sortieren(zeilen, e.Gruppierung, e.SortierungAbsteigend, null);
        if (e.Sortierung2.Length > 0) sortiert = Sortieren(zeilen, e.Sortierung2, e.Sortierung2Absteigend, sortiert);
        sortiert = Sortieren(zeilen, "Element", false, sortiert);
        sortiert = Sortieren(zeilen, "Stunde", false, sortiert);
        var liste = sortiert.ToList();
        if (e.BlockvertretungenZusammenfassen)
            liste = Druckaufbereitung.Zusammenfassen(liste, z => z.StundeZahl,
                z => string.Join("|", z.Element, z.Partner, z.Abwesend, z.Fach, z.StattFach, z.Raum, z.StattRaum, z.Bemerkung, z.Art),
                (z, s) => z with { Stunde = s });

        var nachrichten = e.Nachrichten
            ? d.Tagestexte.Where(t => t.GiltAm(datum) && (lehrer || !t.FuerLehrer)).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
            : new List<string>();
        // Absenzkopf: Spalte „Absenz“ (abwesende Lehrer, Klassen, blockierte Räume) und Spalte „Vertretung“ (betroffene Elemente)
        var kaesten = new List<(string, string)>();
        string Stunden(int von, int bis) => $"({von}-{Math.Min(bis, d.Zeitraster.StundenProTag)})";
        string Grund(Abwesenheit a) => e.LehrerMitAbsenzgrund && a.GrundId is int gid && d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == gid) is { } gr ? $" {gr.Kurzname}" : "";
        string Name(Element? x, bool lang) => x is null ? "" : lang && x.Langname.Length > 0 ? x.Langname : x.Kurzname;
        // Absenzgründe mit „N. in Abs.kopf“ werden nicht gedruckt
        var abw = d.Abwesenheiten.Where(a => datum >= a.Von && datum <= a.Bis
            && !(a.GrundId is int g && d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == g)?.NichtImAbsenzkopf == true)).ToList();
        var veranst = e.AbsenzkopfNichtVeranstaltungen ? new List<Veranstaltung>() : d.Veranstaltungen.Where(v => datum >= v.Von && datum <= v.Bis).ToList();
        if (e.BlockierteRaeume && abw.Where(a => a.Art == AbwesenheitsArt.Raum).ToList() is { Count: > 0 } raeume)
            kaesten.Add(("Blockierte Räume", string.Join(", ", raeume.Select(a => $"{Name(d.Raeume.FirstOrDefault(x => x.Id == a.ElementId), e.AbsenzkopfLangRaeume)} {Stunden(a.StundeVon, a.StundeBis)}"))));
        var klassenTexte = abw.Where(a => a.Art == AbwesenheitsArt.Klasse).Select(a => $"{Name(d.Klassen.FirstOrDefault(x => x.Id == a.ElementId), e.AbsenzkopfLangKlassen)} {Stunden(a.StundeVon, a.StundeBis)}")
            .Concat(veranst.SelectMany(v => v.KlassenIds.Select(k => $"{Name(d.Klassen.FirstOrDefault(x => x.Id == k), e.AbsenzkopfLangKlassen)} {Stunden(v.StundeVon, v.StundeBis)}"))).Distinct().ToList();
        if (e.AbwesendeKlassen && klassenTexte.Count > 0) kaesten.Add(("Abwesende Klassen", string.Join(", ", klassenTexte)));
        var lehrerTexte = abw.Where(a => a.Art == AbwesenheitsArt.Lehrer).Select(a => $"{Name(d.Lehrer.FirstOrDefault(x => x.Id == a.ElementId), e.AbsenzkopfLangLehrer)} {Stunden(a.StundeVon, a.StundeBis)}{Grund(a)}")
            .Concat(veranst.SelectMany(v => v.LehrerIds.Select(l => $"{Name(d.Lehrer.FirstOrDefault(x => x.Id == l), e.AbsenzkopfLangLehrer)} {Stunden(v.StundeVon, v.StundeBis)}"))).Distinct().ToList();
        if (lehrer && e.AbwesendeLehrer && lehrerTexte.Count > 0) kaesten.Add(("Abwesende Lehrer", string.Join(", ", lehrerTexte)));
        // Spalte „Vertretung“: wer bzw. was von Vertretungen betroffen ist
        static IEnumerable<string> Teile(string s) => s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x is not ("---" or "+" or "+++" or "???" or "~"));
        string Lang(IEnumerable<string> namen, IEnumerable<Element> stamm, bool lang) =>
            string.Join(", ", namen.Distinct().Select(n => lang && stamm.FirstOrDefault(x => x.Kurzname == n) is { Langname.Length: > 0 } el ? el.Langname : n));
        if (e.VertretungLehrer && plan.SelectMany(z => Teile(z.Vertreter)).ToList() is { Count: > 0 } betrL)
            kaesten.Add(("Betroffene Lehrer", Lang(betrL, d.Lehrer, e.AbsenzkopfLangLehrer)));
        if (e.VertretungKlassen && plan.SelectMany(z => Teile(z.Klassen)).ToList() is { Count: > 0 } betrK)
            kaesten.Add(("Betroffene Klassen", Lang(betrK, d.Klassen, e.AbsenzkopfLangKlassen)));
        if (e.VertretungRaeume && plan.SelectMany(z => Teile(z.NeuerRaum.Length > 0 ? z.NeuerRaum : z.Raum)).ToList() is { Count: > 0 } betrR)
            kaesten.Add(("Betroffene Räume", Lang(betrR, d.Raeume, e.AbsenzkopfLangRaeume)));
        return new Tag(datum, nachrichten, kaesten, liste);
    }
}
