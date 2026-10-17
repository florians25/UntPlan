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
    /// Clientbereich des Hauptfensters mit eigener Titelleiste (Antwort auf WM_NCCALCSIZE, Bildschirmkoordinaten).
    /// <paramref name="fenster"/> ist das vorgeschlagene Fensterrechteck, <paramref name="systemClient"/> der vom System berechnete
    /// Clientbereich (Systemrahmen links, rechts und unten samt Titelleiste oben).
    /// <list type="bullet">
    /// <item>Fenstermodus: Rahmen links, rechts und unten wie vom System (Schatten, Greifränder, Andocken), oben kein Rahmen – der
    /// Programmkopf beginnt an der Oberkante des Fensters. Ein oben stehen gelassener Rahmen erschiene als weißer Streifen.</item>
    /// <item>Maximiert: Windows legt das Fenster um die Rahmendicke über den Bildschirm hinaus. Der Clientbereich ist dann genau der
    /// Arbeitsbereich des Bildschirms (ohne Taskleiste) – unabhängig von DPI-Skalierung und Systemmetriken; ist die Taskleiste
    /// automatisch ausgeblendet (<paramref name="autoTaskleiste"/>: 0 links, 1 oben, 2 rechts, 3 unten), bleibt an ihrer Kante
    /// ein Pixel frei, damit sie sich weiterhin einblenden lässt.</item>
    /// </list>
    /// </summary>
    public static Rectangle ClientBereich(Rectangle fenster, Rectangle systemClient, bool maximiert, Rectangle arbeitsbereich, int? autoTaskleiste = null)
    {
        if (!maximiert)
            return Rectangle.FromLTRB(systemClient.Left, fenster.Top, Math.Max(systemClient.Left, systemClient.Right), Math.Max(fenster.Top, systemClient.Bottom));
        // ohne Arbeitsbereich: oben dieselbe Rahmendicke wie links (vom System für die DPI des Fensters berechnet)
        var rahmen = Math.Max(0, systemClient.Left - fenster.Left);
        var r = Rectangle.FromLTRB(systemClient.Left, fenster.Top + rahmen, systemClient.Right, systemClient.Bottom);
        if (BereichGueltig(arbeitsbereich))
        {
            var s = Rectangle.Intersect(fenster, arbeitsbereich);
            if (s.Width > 0 && s.Height > 0) r = s;
        }
        if (autoTaskleiste is int k && r.Width > 2 && r.Height > 2)
            r = k switch
            {
                0 => Rectangle.FromLTRB(r.Left + 1, r.Top, r.Right, r.Bottom),
                1 => Rectangle.FromLTRB(r.Left, r.Top + 1, r.Right, r.Bottom),
                2 => Rectangle.FromLTRB(r.Left, r.Top, r.Right - 1, r.Bottom),
                3 => Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom - 1),
                _ => r,
            };
        return r;
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
