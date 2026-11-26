using System.Runtime.CompilerServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Gruppierte Spaltenköpfe für DataGridView-Tabellen (wie die Kopfgruppen des Vorbilds, z. B. „Schritt 1“/„Schritt 2“ über den Spalten
/// der Vorziehungen oder „Stundenplan“ über den Planspalten des Vertretungsvorschlags). Zwei Arten:
/// <list type="bullet">
/// <item><b>verbunden</b> (einzeilig): der Gruppentitel steht einmal über allen Spalten der Gruppe, ohne Trennlinien dazwischen;</item>
/// <item><b>zweizeilig</b>: oben der Gruppentitel über der Gruppe (mit Linie darunter), unten die Spaltentitel; Spalten ohne Gruppe
/// tragen ihren Titel über die volle Höhe.</item>
/// </list>
/// Gezeichnet wird je Zelle (auf die Zelle beschnitten) relativ zum sichtbaren Gruppenbereich, daher bleibt der Titel beim Bildlauf
/// und beim Ändern von Spaltenbreiten durchgehend; die Kopfzeile wird dabei ganz neu gezeichnet.
/// </summary>
public static class KopfGruppen
{
    public sealed record Gruppe(string Titel, params string[] Spalten);

    private sealed class Stand
    {
        public bool Zweizeilig;
        public List<Gruppe> Gruppen = new();
    }

    private static readonly ConditionalWeakTable<DataGridView, Stand> Tabellen = new();

    /// <summary>Kopfgruppen festlegen (ersetzt frühere). Zweizeilig verdoppelt die Kopfhöhe einmalig.</summary>
    public static void Einrichten(DataGridView g, bool zweizeilig, params Gruppe[] gruppen)
    {
        var neu = !Tabellen.TryGetValue(g, out var st);
        st ??= new Stand();
        if (zweizeilig && !st.Zweizeilig) g.ColumnHeadersHeight *= 2;
        st.Zweizeilig = zweizeilig;
        st.Gruppen = gruppen.ToList();
        Tabellen.AddOrUpdate(g, st);
        if (!neu) return;
        void KopfNeu() { if (g.IsHandleCreated) g.Invalidate(new Rectangle(0, 0, g.Width, g.ColumnHeadersHeight)); }
        g.Scroll += (_, e) => { if (e.ScrollOrientation == ScrollOrientation.HorizontalScroll) KopfNeu(); };
        g.ColumnWidthChanged += (_, _) => KopfNeu();
        g.ColumnDisplayIndexChanged += (_, _) => KopfNeu();
        g.ColumnStateChanged += (_, _) => KopfNeu();
    }

    /// <summary>Hat die Tabelle Kopfgruppen?</summary>
    public static bool Aktiv(DataGridView g) => Tabellen.TryGetValue(g, out var st) && st.Gruppen.Count > 0;

    /// <summary>Zweizeiliger Kopf?</summary>
    public static bool Zweizeilig(DataGridView g) => Tabellen.TryGetValue(g, out var st) && st.Zweizeilig;

    /// <summary>Gruppe einer Spalte (null = keine).</summary>
    public static Gruppe? GruppeVon(DataGridView g, DataGridViewColumn spalte) =>
        Tabellen.TryGetValue(g, out var st) ? st.Gruppen.FirstOrDefault(x => x.Spalten.Contains(spalte.Name)) : null;

    /// <summary>Sichtbarer Bereich der Gruppe in der Kopfzeile (Vereinigung der angezeigten Spalten), leer wenn keine angezeigt wird.</summary>
    public static Rectangle Bereich(DataGridView g, Gruppe gruppe)
    {
        var r = Rectangle.Empty;
        foreach (var n in gruppe.Spalten)
        {
            if (!g.Columns.Contains(n) || g.Columns[n] is not { Visible: true } c) continue;
            var d = g.GetColumnDisplayRectangle(c.Index, false);
            if (d.Width <= 0) continue;
            r = r.IsEmpty ? d : Rectangle.Union(r, d);
        }
        return r.IsEmpty ? r : new Rectangle(r.X, 0, r.Width, g.ColumnHeadersHeight);
    }

    /// <summary>Ist die Spalte die letzte angezeigte ihrer Gruppe (dort steht die rechte Gruppenlinie)?</summary>
    public static bool LetzteDerGruppe(DataGridView g, Gruppe gruppe, DataGridViewColumn spalte) =>
        gruppe.Spalten.Select(n => g.Columns.Contains(n) ? g.Columns[n] : null).Where(c => c is { Visible: true })
            .OrderBy(c => c!.DisplayIndex).LastOrDefault() == spalte;

    /// <summary>
    /// Kopfzelle zeichnen (Hintergrund in <paramref name="grund"/>, Rasterlinien, Gruppen- und Spaltentitel). Liefert false, wenn die
    /// Spalte zu keiner Gruppe gehört und die Tabelle einzeilig ist (dann zeichnet der Aufrufer wie bisher).
    /// </summary>
    public static bool Zeichnen(DataGridView g, DataGridViewCellPaintingEventArgs e, Color grund)
    {
        if (e.RowIndex != -1 || e.ColumnIndex < 0 || e.Graphics is null || !Tabellen.TryGetValue(g, out var st) || st.Gruppen.Count == 0) return false;
        var spalte = g.Columns[e.ColumnIndex];
        var gruppe = GruppeVon(g, spalte);
        if (gruppe is null && !st.Zweizeilig) return false;
        var r = e.CellBounds;
        var gr = e.Graphics;
        var schrift = g.ColumnHeadersDefaultCellStyle.Font ?? g.Font;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var clip = gr.Clip;
        gr.SetClip(r);
        using (var b = new SolidBrush(grund)) gr.FillRectangle(b, r);
        using var linie = new Pen(g.GridColor);
        if (gruppe is null)
        {
            // zweizeilige Tabelle, Spalte ohne Gruppe: Titel über die volle Höhe
            TextRenderer.DrawText(gr, spalte.HeaderText, schrift, new Rectangle(r.X + 3, r.Y, r.Width - 4, r.Height - 1), Color.Black, flags);
            gr.DrawLine(linie, r.Right - 1, r.Y, r.Right - 1, r.Bottom - 1);
        }
        else
        {
            var bereich = Bereich(g, gruppe);
            var letzte = LetzteDerGruppe(g, gruppe, spalte);
            if (st.Zweizeilig)
            {
                var halb = r.Height / 2;
                TextRenderer.DrawText(gr, gruppe.Titel, schrift, new Rectangle(bereich.X + 3, r.Y, Math.Max(bereich.Width, 400), halb), Color.Black, flags);
                gr.DrawLine(linie, r.X, r.Y + halb, r.Right, r.Y + halb);
                if (letzte) gr.DrawLine(linie, r.Right - 1, r.Y, r.Right - 1, r.Y + halb);
                TextRenderer.DrawText(gr, spalte.HeaderText, schrift, new Rectangle(r.X + 3, r.Y + halb, r.Width - 4, r.Height - halb - 1), Color.Black, flags);
                gr.DrawLine(linie, r.Right - 1, r.Y + halb, r.Right - 1, r.Bottom - 1);
            }
            else
            {
                // verbunden: Titel einmal über der Gruppe, Trennlinie nur am Gruppenende
                TextRenderer.DrawText(gr, gruppe.Titel, schrift, new Rectangle(bereich.X + 3, r.Y, Math.Max(bereich.Width, 400), r.Height - 1), Color.Black, flags);
                if (letzte) gr.DrawLine(linie, r.Right - 1, r.Y, r.Right - 1, r.Bottom - 1);
            }
        }
        gr.DrawLine(linie, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        gr.Clip = clip;
        e.Handled = true;
        return true;
    }
}
