using System.Drawing.Drawing2D;

namespace UntPlan.App;

/// <summary>Programmatisch gezeichnete, farbige Symbole (skalierbar, keine externen Grafikdateien).</summary>
public static class Symbole
{
    private static readonly Dictionary<(string, int), Bitmap> Cache = new();

    public static Bitmap Get(string name, int groesse = 16)
    {
        if (Cache.TryGetValue((name, groesse), out var b)) return b;
        b = new Bitmap(groesse, groesse);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(groesse / 32f, groesse / 32f);
            Zeichnen(g, name);
        }
        Cache[(name, groesse)] = b;
        return b;
    }

    private static readonly Pen Rand = new(Color.FromArgb(70, 70, 80), 1.6f);

    private static void Person(Graphics g, float x, float y, float s, Color c)
    {
        using var br = new SolidBrush(c);
        g.FillEllipse(br, x + s * 0.3f, y, s * 0.4f, s * 0.4f);
        g.FillPie(br, x, y + s * 0.42f, s, s * 0.9f, 180, 180);
        g.DrawEllipse(Rand, x + s * 0.3f, y, s * 0.4f, s * 0.4f);
    }

    private static void Seite(Graphics g, float x, float y, float w, float h)
    {
        g.FillRectangle(Brushes.White, x, y, w, h);
        g.DrawRectangle(Rand, x, y, w, h);
        using var p = new Pen(Color.FromArgb(150, 160, 175), 1.2f);
        for (var i = 1; i < 4; i++) g.DrawLine(p, x + 3, y + i * h / 4.5f + 2, x + w - 3, y + i * h / 4.5f + 2);
    }

    private static void Uhr(Graphics g, float x, float y, float s, Color c)
    {
        using var br = new SolidBrush(c);
        g.FillEllipse(br, x, y, s, s);
        g.FillEllipse(Brushes.White, x + s * 0.15f, y + s * 0.15f, s * 0.7f, s * 0.7f);
        g.DrawEllipse(Rand, x, y, s, s);
        using var p = new Pen(Color.FromArgb(40, 40, 40), 2f);
        g.DrawLine(p, x + s / 2, y + s / 2, x + s / 2, y + s * 0.25f);
        g.DrawLine(p, x + s / 2, y + s / 2, x + s * 0.72f, y + s / 2);
    }

    private static void Raster(Graphics g, float x, float y, float w, float h, bool bunt)
    {
        g.FillRectangle(Brushes.White, x, y, w, h);
        Color[] f = { Color.FromArgb(242, 139, 130), Color.FromArgb(123, 170, 247), Color.FromArgb(129, 201, 149), Color.FromArgb(255, 213, 79) };
        var cw = w / 4;
        var ch = h / 4;
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
                if (bunt && (i + j) % 2 == 0)
                    using (var b = new SolidBrush(f[(i * 3 + j) % 4])) g.FillRectangle(b, x + i * cw, y + j * ch, cw, ch);
        using var p = new Pen(Color.FromArgb(90, 100, 120), 1f);
        for (var i = 0; i <= 4; i++)
        {
            g.DrawLine(p, x + i * cw, y, x + i * cw, y + h);
            g.DrawLine(p, x, y + i * ch, x + w, y + i * ch);
        }
    }

    private static void Zahnrad(Graphics g, float cx, float cy, float r, Color c)
    {
        using var br = new SolidBrush(c);
        var pts = new List<PointF>();
        for (var i = 0; i < 16; i++)
        {
            var a = i * Math.PI / 8;
            var rr = i % 2 == 0 ? r : r * 0.75f;
            pts.Add(new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr)));
        }
        g.FillPolygon(br, pts.ToArray());
        g.DrawPolygon(Rand, pts.ToArray());
        g.FillEllipse(Brushes.White, cx - r * 0.3f, cy - r * 0.3f, r * 0.6f, r * 0.6f);
    }

    private static void Pfeil(Graphics g, Color c, params PointF[] pts)
    {
        using var p = new Pen(c, 3f) { EndCap = LineCap.ArrowAnchor };
        g.DrawLines(p, pts);
    }

    private static void Zeichnen(Graphics g, string name)
    {
        var blau = Color.FromArgb(61, 118, 204);
        var orange = Color.FromArgb(242, 140, 40);
        var gruen = Color.FromArgb(67, 160, 71);
        var rot = Color.FromArgb(220, 50, 50);
        var gelb = Color.FromArgb(250, 200, 40);
        var braun = Color.FromArgb(160, 105, 60);
        switch (name)
        {
            case "klassen":
                Person(g, 1, 6, 16, orange);
                Person(g, 15, 4, 16, blau);
                Person(g, 8, 12, 17, gruen);
                break;
            case "lehrer":
                g.FillPolygon(Brushes.Black, new PointF[] { new(16, 5), new(31, 12), new(16, 19), new(1, 12) });
                g.FillRectangle(Brushes.Black, 8, 14, 16, 8);
                g.FillEllipse(Brushes.Black, 8, 18, 16, 8);
                using (var p = new Pen(gelb, 2f)) g.DrawLine(p, 27, 13, 27, 25);
                g.FillEllipse(new SolidBrush(gelb), 25, 24, 5, 5);
                break;
            case "raeume":
                g.FillRectangle(new SolidBrush(braun), 7, 3, 18, 27);
                g.DrawRectangle(Rand, 7, 3, 18, 27);
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 150, 95)), 10, 6, 12, 10);
                g.FillEllipse(new SolidBrush(gelb), 20, 17, 4, 4);
                break;
            case "faecher":
                g.FillRectangle(new SolidBrush(blau), 6, 3, 20, 26);
                g.FillRectangle(Brushes.White, 9, 26, 17, 3);
                g.DrawRectangle(Rand, 6, 3, 20, 26);
                g.FillRectangle(new SolidBrush(Color.FromArgb(150, 190, 240)), 10, 7, 12, 5);
                break;
            case "zeitwunsch":
                Uhr(g, 4, 4, 24, orange);
                break;
            case "neu":
                Seite(g, 7, 4, 18, 24);
                g.FillEllipse(new SolidBrush(gelb), 18, 2, 11, 11);
                break;
            case "loeschen":
                using (var p = new Pen(rot, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, 7, 7, 25, 25);
                    g.DrawLine(p, 25, 7, 7, 25);
                }
                break;
            case "speichern":
                g.FillRectangle(new SolidBrush(Color.FromArgb(70, 100, 170)), 4, 4, 24, 24);
                g.FillRectangle(Brushes.White, 9, 4, 14, 9);
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 210, 230)), 8, 17, 16, 11);
                g.DrawRectangle(Rand, 4, 4, 24, 24);
                break;
            case "oeffnen":
                g.FillPolygon(new SolidBrush(gelb), new PointF[] { new(3, 9), new(12, 9), new(14, 12), new(29, 12), new(27, 27), new(3, 27) });
                g.DrawPolygon(Rand, new PointF[] { new(3, 9), new(12, 9), new(14, 12), new(29, 12), new(27, 27), new(3, 27) });
                break;
            case "drucken":
                g.FillRectangle(Brushes.White, 9, 3, 14, 10);
                g.DrawRectangle(Rand, 9, 3, 14, 10);
                g.FillRectangle(new SolidBrush(Color.FromArgb(120, 130, 145)), 3, 12, 26, 11);
                g.DrawRectangle(Rand, 3, 12, 26, 11);
                g.FillRectangle(Brushes.White, 9, 19, 14, 10);
                g.DrawRectangle(Rand, 9, 19, 14, 10);
                break;
            case "vorschau":
                Seite(g, 4, 3, 18, 24);
                using (var p = new Pen(Color.FromArgb(60, 60, 60), 3f)) { g.DrawEllipse(p, 14, 13, 10, 10); g.DrawLine(p, 23, 22, 29, 28); }
                break;
            case "rueckgaengig":
                Pfeil(g, blau, new PointF(26, 24), new PointF(26, 13), new PointF(18, 9), new PointF(8, 12));
                break;
            case "wiederholen":
                Pfeil(g, blau, new PointF(6, 24), new PointF(6, 13), new PointF(14, 9), new PointF(24, 12));
                break;
            case "sortieren":
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) { g.DrawString("A", f, Brushes.Black, 1, 0); g.DrawString("Z", f, Brushes.Black, 1, 14); }
                Pfeil(g, blau, new PointF(24, 4), new PointF(24, 28));
                break;
            case "filter":
                g.FillPolygon(new SolidBrush(Color.FromArgb(120, 140, 170)), new PointF[] { new(3, 4), new(29, 4), new(19, 16), new(19, 28), new(13, 24), new(13, 16) });
                g.DrawPolygon(Rand, new PointF[] { new(3, 4), new(29, 4), new(19, 16), new(19, 28), new(13, 24), new(13, 16) });
                break;
            case "diagnose":
                g.FillEllipse(new SolidBrush(Color.FromArgb(230, 240, 255)), 2, 2, 28, 28);
                using (var p = new Pen(rot, 2.5f)) g.DrawLines(p, new PointF[] { new(3, 17), new(10, 17), new(13, 8), new(18, 25), new(21, 14), new(29, 14) });
                break;
            case "gewichtung":
                using (var p = new Pen(Color.FromArgb(90, 90, 100), 2.5f)) { g.DrawLine(p, 16, 5, 16, 27); g.DrawLine(p, 5, 9, 27, 9); g.DrawLine(p, 9, 28, 23, 28); }
                g.FillPie(new SolidBrush(gelb), 1, 9, 10, 10, 0, 180);
                g.FillPie(new SolidBrush(gelb), 21, 9, 10, 10, 0, 180);
                break;
            case "optimierung":
                Zahnrad(g, 12, 13, 10, Color.FromArgb(120, 130, 150));
                Zahnrad(g, 23, 22, 7, orange);
                break;
            case "planen":
                Raster(g, 3, 6, 26, 22, false);
                g.FillRectangle(new SolidBrush(blau), 3, 3, 26, 5);
                using (var p = new Pen(gruen, 3.5f)) g.DrawLines(p, new PointF[] { new(9, 18), new(14, 23), new(24, 11) });
                break;
            case "perioden":
                Seite(g, 9, 3, 18, 20);
                Seite(g, 5, 9, 18, 20);
                g.FillRectangle(new SolidBrush(rot), 5, 9, 18, 4);
                break;
            case "abteilungen":
                g.FillPolygon(new SolidBrush(rot), new PointF[] { new(16, 3), new(30, 15), new(2, 15) });
                g.FillRectangle(new SolidBrush(Color.FromArgb(235, 225, 200)), 6, 15, 20, 14);
                g.DrawRectangle(Rand, 6, 15, 20, 14);
                g.FillRectangle(new SolidBrush(braun), 13, 20, 6, 9);
                break;
            case "einstellungen":
                Zahnrad(g, 16, 16, 13, gelb);
                break;
            case "hilfe":
                g.FillEllipse(new SolidBrush(gelb), 7, 2, 18, 18);
                g.DrawEllipse(Rand, 7, 2, 18, 18);
                g.FillRectangle(new SolidBrush(Color.FromArgb(150, 150, 160)), 11, 20, 10, 7);
                g.FillRectangle(new SolidBrush(Color.FromArgb(110, 110, 120)), 12, 27, 8, 3);
                break;
            case "vertretung":
                Person(g, 1, 8, 15, blau);
                Person(g, 16, 8, 15, orange);
                Pfeil(g, gruen, new PointF(10, 4), new PointF(22, 4));
                break;
            case "stundenplan":
                Raster(g, 2, 3, 28, 26, true);
                break;
            case "uebersicht":
                Raster(g, 2, 3, 28, 26, true);
                g.FillRectangle(new SolidBrush(blau), 2, 3, 7, 26);
                break;
            case "fixieren":
                using (var p = new Pen(Color.FromArgb(120, 120, 130), 3f)) g.DrawArc(p, 9, 3, 14, 16, 180, 180);
                g.FillRectangle(new SolidBrush(gelb), 6, 13, 20, 16);
                g.DrawRectangle(Rand, 6, 13, 20, 16);
                g.FillEllipse(Brushes.Black, 14, 18, 4, 4);
                break;
            case "raumzuordnen":
                g.FillRectangle(new SolidBrush(braun), 3, 5, 14, 23);
                g.FillEllipse(new SolidBrush(gelb), 13, 16, 3, 3);
                Pfeil(g, gruen, new PointF(18, 16), new PointF(30, 16));
                break;
            case "tausch":
                Pfeil(g, blau, new PointF(4, 10), new PointF(28, 10));
                Pfeil(g, orange, new PointF(28, 22), new PointF(4, 22));
                break;
            case "assistent":
                using (var p = new Pen(Color.FromArgb(90, 60, 30), 3f)) g.DrawLine(p, 6, 27, 22, 11);
                g.FillPolygon(new SolidBrush(gelb), new PointF[] { new(24, 2), new(26, 8), new(31, 9), new(27, 13), new(28, 19), new(24, 16), new(19, 19), new(20, 13), new(16, 9), new(22, 8) });
                break;
            case "modul":
                g.FillRectangle(new SolidBrush(gruen), 4, 10, 20, 18);
                g.FillEllipse(new SolidBrush(gruen), 10, 4, 8, 8);
                g.FillEllipse(new SolidBrush(gruen), 21, 15, 8, 8);
                break;
            case "pausenaufsicht":
                g.FillPolygon(new SolidBrush(blau), new PointF[] { new(16, 2), new(28, 7), new(26, 20), new(16, 30), new(6, 20), new(4, 7) });
                g.DrawPolygon(Rand, new PointF[] { new(16, 2), new(28, 7), new(26, 20), new(16, 30), new(6, 20), new(4, 7) });
                using (var p = new Pen(Color.White, 3f)) g.DrawLines(p, new PointF[] { new(10, 16), new(15, 21), new(23, 11) });
                break;
            case "kurs":
                g.FillPolygon(new SolidBrush(Color.FromArgb(40, 40, 60)), new PointF[] { new(16, 6), new(31, 13), new(16, 20), new(1, 13) });
                Raster(g, 6, 20, 20, 10, true);
                break;
            case "wertrechnung":
                g.FillRectangle(new SolidBrush(Color.FromArgb(90, 100, 120)), 6, 3, 20, 26);
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 230, 200)), 9, 6, 14, 6);
                for (var i = 0; i < 3; i++) for (var j = 0; j < 3; j++) g.FillRectangle(Brushes.White, 9 + i * 5, 15 + j * 4.5f, 3.5f, 3);
                break;
            case "web":
                g.FillEllipse(new SolidBrush(Color.FromArgb(80, 160, 230)), 3, 3, 26, 26);
                using (var p = new Pen(Color.White, 1.6f)) { g.DrawEllipse(p, 10, 3, 12, 26); g.DrawLine(p, 3, 16, 29, 16); g.DrawLine(p, 6, 9, 26, 9); g.DrawLine(p, 6, 23, 26, 23); }
                break;
            case "import":
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 210, 225)), 4, 16, 24, 13);
                g.DrawRectangle(Rand, 4, 16, 24, 13);
                Pfeil(g, gruen, new PointF(16, 2), new PointF(16, 22));
                break;
            case "export":
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 210, 225)), 4, 16, 24, 13);
                g.DrawRectangle(Rand, 4, 16, 24, 13);
                Pfeil(g, orange, new PointF(16, 22), new PointF(16, 2));
                break;
            case "kommentar":
                g.FillPolygon(new SolidBrush(gelb), new PointF[] { new(3, 4), new(29, 4), new(29, 21), new(14, 21), new(7, 28), new(8, 21), new(3, 21) });
                g.DrawPolygon(Rand, new PointF[] { new(3, 4), new(29, 4), new(29, 21), new(14, 21), new(7, 28), new(8, 21), new(3, 21) });
                break;
            case "rollup":
                g.FillRectangle(Brushes.White, 5, 3, 22, 26);
                g.DrawRectangle(Rand, 5, 3, 22, 26);
                g.FillRectangle(new SolidBrush(blau), 5, 3, 22, 5);
                for (var i = 0; i < 4; i++) g.FillRectangle(new SolidBrush(Color.FromArgb(150, 170, 200)), 8, 11 + i * 4.5f, 16, 2.5f);
                break;
            case "normalform":
                Pfeil(g, blau, new PointF(16, 16), new PointF(16, 2));
                Pfeil(g, blau, new PointF(16, 16), new PointF(16, 30));
                Pfeil(g, blau, new PointF(16, 16), new PointF(2, 16));
                Pfeil(g, blau, new PointF(16, 16), new PointF(30, 16));
                break;
            case "unterricht":
                Seite(g, 4, 3, 22, 26);
                g.FillRectangle(new SolidBrush(gruen), 4, 3, 7, 26);
                break;
            case "info":
                g.FillEllipse(new SolidBrush(blau), 3, 3, 26, 26);
                using (var f = new Font("Georgia", 15f, FontStyle.Bold)) g.DrawString("i", f, Brushes.White, 10, 3);
                break;
            case "plus":
                using (var p = new Pen(gruen, 5f)) { g.DrawLine(p, 16, 5, 16, 27); g.DrawLine(p, 5, 16, 27, 16); }
                break;
            case "minus":
                using (var p = new Pen(rot, 5f)) g.DrawLine(p, 5, 16, 27, 16);
                break;
            case "vor":
                g.FillPolygon(new SolidBrush(blau), new PointF[] { new(10, 5), new(25, 16), new(10, 27) });
                break;
            case "zurueck":
                g.FillPolygon(new SolidBrush(blau), new PointF[] { new(22, 5), new(7, 16), new(22, 27) });
                break;
            case "monitor":
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 50, 70)), 2, 4, 28, 19);
                g.FillRectangle(new SolidBrush(Color.FromArgb(90, 160, 230)), 4, 6, 24, 15);
                g.FillRectangle(new SolidBrush(Color.FromArgb(90, 90, 100)), 12, 23, 8, 5);
                break;
            case "abwesenheit":
                Person(g, 4, 4, 22, Color.FromArgb(160, 160, 170));
                using (var p = new Pen(rot, 4f)) g.DrawLine(p, 4, 28, 28, 4);
                break;
            case "entfall":
                Seite(g, 6, 3, 20, 26);
                using (var p = new Pen(rot, 4f)) { g.DrawLine(p, 8, 8, 24, 24); g.DrawLine(p, 24, 8, 8, 24); }
                break;
            case "schueler":
                Person(g, 3, 3, 26, Color.FromArgb(120, 180, 90));
                break;
            case "html":
                g.FillRectangle(Brushes.White, 3, 5, 26, 22);
                g.DrawRectangle(Rand, 3, 5, 26, 22);
                using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold)) g.DrawString("</>", f, new SolidBrush(orange), 4, 10);
                break;
            case "suchen":
                using (var p = new Pen(Color.FromArgb(60, 60, 60), 3.5f)) { g.DrawEllipse(p, 4, 4, 16, 16); g.DrawLine(p, 18, 18, 28, 28); }
                break;
            case "kalender":
                Raster(g, 3, 8, 26, 21, false);
                g.FillRectangle(new SolidBrush(rot), 3, 3, 26, 6);
                break;
            case "zeitraster":
                Uhr(g, 2, 2, 17, blau);
                Raster(g, 13, 13, 17, 16, true);
                break;
            case "ferien":
                g.FillEllipse(new SolidBrush(gelb), 18, 2, 12, 12);
                g.FillRectangle(new SolidBrush(Color.FromArgb(80, 170, 230)), 2, 22, 28, 8);
                using (var p = new Pen(braun, 2.5f)) g.DrawLine(p, 10, 22, 13, 8);
                g.FillPie(new SolidBrush(gruen), 4, 4, 18, 12, 180, 180);
                break;
            case "statistik":
                g.FillRectangle(new SolidBrush(blau), 4, 16, 6, 13);
                g.FillRectangle(new SolidBrush(orange), 13, 8, 6, 21);
                g.FillRectangle(new SolidBrush(gruen), 22, 12, 6, 17);
                break;
            default:
                using (var f = new Font("Segoe UI", 14f, FontStyle.Bold))
                    g.DrawString(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", f, new SolidBrush(blau), 6, 2);
                break;
        }
    }
}
