using System.Drawing;

namespace UntPlan.Core;

/// <summary>
/// Reine Klemm-Logik für Fensterlagen (ohne Windows Forms, damit testbar): Hauptfenster und Arbeitsfenster dürfen nie über den
/// oberen Rand des sichtbaren Bereichs rutschen – das Hauptfenster nicht unter die Taskleiste bzw. über den Bildschirmrand
/// (Arbeitsbereich), die Arbeitsfenster nicht unter das Menüband (MDI-Arbeitsbereich). Gespeicherte Lagen (auch mit negativen
/// Koordinaten aus älteren Versionen, von einem größeren Bildschirm, mit anderer DPI-Skalierung oder von einem nicht mehr
/// vorhandenen Monitor) werden beim Laden, Anordnen und Verschieben in den sichtbaren Bereich geholt.
/// </summary>
public static class Fensterlage
{
    /// <summary>Unter dieser Größe gilt ein Bereich als noch nicht angeordnet (z. B. minimiertes Hauptfenster) – dann wird nicht geklemmt.</summary>
    public static readonly Size MindestBereich = new(120, 80);

    /// <summary>Ist der Bereich groß genug, um Fenster darin zu klemmen?</summary>
    public static bool BereichGueltig(Rectangle bereich) => bereich.Width >= MindestBereich.Width && bereich.Height >= MindestBereich.Height;

    /// <summary>
    /// Rechteck vollständig in den Bereich holen: zu große Fenster werden (mit <paramref name="groesseAnpassen"/>) verkleinert,
    /// sonst links oben ausgerichtet. Der obere Rand des Fensters liegt danach nie über dem oberen Rand des Bereichs.
    /// </summary>
    public static Rectangle ImBereich(Rectangle fenster, Rectangle bereich, bool groesseAnpassen = true, Size? minimum = null)
    {
        if (!BereichGueltig(bereich)) return fenster;
        var min = minimum ?? Size.Empty;
        var w = Math.Max(min.Width, fenster.Width);
        var h = Math.Max(min.Height, fenster.Height);
        if (groesseAnpassen)
        {
            w = Math.Min(w, bereich.Width);
            h = Math.Min(h, bereich.Height);
        }
        var x = Math.Max(bereich.Left, Math.Min(fenster.X, bereich.Right - w));
        var y = Math.Max(bereich.Top, Math.Min(fenster.Y, bereich.Bottom - h));
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Arbeitsbereich, zu dem das Fenster gehört: größte Überschneidung, sonst der nächstgelegene (sonst der erste).</summary>
    public static Rectangle BereichFuer(Rectangle fenster, IReadOnlyList<Rectangle> bereiche)
    {
        if (bereiche.Count == 0) return Rectangle.Empty;
        var best = bereiche[0];
        var bestFlaeche = -1L;
        foreach (var b in bereiche)
        {
            var s = Rectangle.Intersect(b, fenster);
            var f = s.IsEmpty ? 0L : (long)s.Width * s.Height;
            if (f > bestFlaeche) { best = b; bestFlaeche = f; }
        }
        if (bestFlaeche > 0) return best;
        var p = new Point(fenster.X + fenster.Width / 2, fenster.Y);
        return bereiche.OrderBy(b => Abstand(b, p)).First();
    }

    /// <summary>
    /// Gespeicherte Lage des Hauptfensters beim Start: auf den passenden Bildschirm holen (nicht vorhandener Monitor → nächster),
    /// dort vollständig in den Arbeitsbereich klemmen und höchstens so groß wie dieser machen (Mindestgröße beachten).
    /// </summary>
    public static Rectangle Hauptfenster(Rectangle gespeichert, IReadOnlyList<Rectangle> arbeitsbereiche, Size minimum)
    {
        var bereich = BereichFuer(gespeichert, arbeitsbereiche);
        if (!BereichGueltig(bereich)) return gespeichert;
        var r = new Rectangle(gespeichert.X, gespeichert.Y, Math.Max(minimum.Width, gespeichert.Width), Math.Max(minimum.Height, gespeichert.Height));
        return ImBereich(r, bereich);
    }

    /// <summary>
    /// Beim Verschieben des Hauptfensters (Ziehen am Programmkopf, Andocken, Größenänderung am oberen Rand): Der Programmkopf
    /// (obere <paramref name="titelHoehe"/> Pixel) bleibt sichtbar – nie über dem oberen Rand des Arbeitsbereichs, nie unter dem
    /// unteren Rand, waagrecht mindestens <paramref name="minSichtbar"/> Pixel sichtbar. Liegt die Mitte des Programmkopfs in einem
    /// Arbeitsbereich (z. B. Wechsel auf einen darüber liegenden Monitor), bleibt die Lage unverändert, außer der Kopf ragt über dessen
    /// oberen Rand. Die Größe bleibt erhalten.
    /// </summary>
    public static Rectangle TitelSichtbar(Rectangle fenster, IReadOnlyList<Rectangle> arbeitsbereiche, int titelHoehe, int minSichtbar = 100)
    {
        if (arbeitsbereiche.Count == 0) return fenster;
        titelHoehe = Math.Max(1, titelHoehe);
        var kopf = new Point(fenster.X + fenster.Width / 2, fenster.Y + titelHoehe / 2);
        var bereich = arbeitsbereiche.FirstOrDefault(b => b.Contains(kopf));
        if (bereich.IsEmpty) bereich = arbeitsbereiche.OrderBy(b => Abstand(b, kopf)).First();
        if (!BereichGueltig(bereich)) return fenster;
        var sichtbar = Math.Min(minSichtbar, Math.Max(1, fenster.Width));
        var x = Math.Max(bereich.Left - fenster.Width + sichtbar, Math.Min(fenster.X, bereich.Right - sichtbar));
        var y = Math.Max(bereich.Top, Math.Min(fenster.Y, bereich.Bottom - titelHoehe));
        return new Rectangle(x, y, fenster.Width, fenster.Height);
    }

    /// <summary>
    /// Arbeitsfenster im MDI-Bereich (Clientkoordinaten, Ursprung oben links unter dem Menüband): vollständig im sichtbaren
    /// Bereich; bei einer Größenänderung (bzw. <paramref name="groesseAnpassen"/>) höchstens so groß wie der Bereich, beim reinen
    /// Verschieben bleibt die Größe – ein zu großes Fenster steht dann oben links.
    /// </summary>
    public static Rectangle MdiFenster(Rectangle fenster, Size mdiBereich, bool groesseAnpassen) =>
        ImBereich(fenster, new Rectangle(Point.Empty, mdiBereich), groesseAnpassen);

    /// <summary>Gespeicherte Fensteranordnung (Positionen im MDI-Bereich) in den aktuellen Bereich holen.</summary>
    public static List<Rectangle> Anordnung(IEnumerable<Rectangle> fenster, Size mdiBereich) =>
        fenster.Select(f => MdiFenster(f, mdiBereich, true)).ToList();

    private static double Abstand(Rectangle b, Point p)
    {
        var dx = p.X < b.Left ? b.Left - p.X : p.X >= b.Right ? p.X - b.Right + 1 : 0;
        var dy = p.Y < b.Top ? b.Top - p.Y : p.Y >= b.Bottom ? p.Y - b.Bottom + 1 : 0;
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
