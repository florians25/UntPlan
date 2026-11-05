using UntPlan.App.Dialoge;
using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Kommentarfunktion für Listen ohne Stammdatenelement (Absenzen, Veranstaltungen) wie im Handbuch (Kap. 4.5): Rechtsklick auf ein Feld
/// | „Kommentar erstellen“ – im Dialog Text und Kategorie. Kommentierte Felder zeigen rechts oben ein Dreieck in der Farbe der Kategorie,
/// beim Überfahren mit der Maus erscheint der Kommentar. Kommentare werden weder gedruckt noch weitergegeben.
/// </summary>
public sealed class ZellKommentare
{
    private readonly DataGridView _g;
    private readonly Func<DataGridViewRow, (string Bezug, string Titel)?> _bezug;

    private ZellKommentare(DataGridView g, Func<DataGridViewRow, (string Bezug, string Titel)?> bezug)
    {
        _g = g;
        _bezug = bezug;
        g.CellPainting += Zeichnen;
        g.CellToolTipTextNeeded += Hinweis;
    }

    /// <summary>Kommentare für eine Tabelle aktivieren. <paramref name="bezug"/> liefert zu einer Zeile Schlüssel („Absenz:17“) und Titel („Absenz Nr. 17“).</summary>
    public static ZellKommentare Aktivieren(DataGridView g, Func<DataGridViewRow, (string Bezug, string Titel)?> bezug) => new(g, bezug);

    private static Schuldatei D => AppZustand.D;

    /// <summary>Anzeigetext eines Bezugs („Absenz:17“ → „Absenz Nr. 17“).</summary>
    public static string BezugText(string bezug) => bezug.Split(':') is [var art, var nr] ? $"{art} Nr. {nr}" : bezug;

    private List<Kommentar> Kommentare(int zeile, int spalte)
    {
        if (zeile < 0 || spalte < 0 || zeile >= _g.Rows.Count || _bezug(_g.Rows[zeile]) is not { } b) return new();
        var feld = _g.Columns[spalte].Name;
        return D.Kommentare.Where(k => k.Bezug == b.Bezug && k.Feld == feld).ToList();
    }

    private static Color Farbe(Kommentar k) => Ui.Farbe(D.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie)?.Farbe ?? 0, Color.Red);

    private void Zeichnen(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || D.Kommentare.Count == 0) return;
        var k = Kommentare(e.RowIndex, e.ColumnIndex).FirstOrDefault();
        if (k is null) return;
        if (!e.Handled)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            e.Handled = true;
        }
        using var b = new SolidBrush(Farbe(k));
        var r = e.CellBounds;
        e.Graphics.FillPolygon(b, new[] { new Point(r.Right - 9, r.Top + 1), new Point(r.Right - 1, r.Top + 1), new Point(r.Right - 1, r.Top + 9) });
    }

    private void Hinweis(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        var ks = Kommentare(e.RowIndex, e.ColumnIndex);
        if (ks.Count == 0) return;
        e.ToolTipText = string.Join("\n\n", ks.Select(k =>
            $"Kommentar\n{D.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie)?.Name ?? "Kategorie " + k.Kategorie}\n{k.Text}\nÄnderungsdatum: {k.Erstellt:d.M.yyyy}"));
    }

    /// <summary>Menüeinträge wie im Vorbild: Kommentar erstellen, Kommentar bearbeiten ▸, Kommentar(e) löschen, Kommentar Übersichtsfenster.</summary>
    public List<ToolStripItem> MenueEintraege(int zeile, int spalte)
    {
        var res = new List<ToolStripItem>();
        var b = zeile >= 0 && zeile < _g.Rows.Count ? _bezug(_g.Rows[zeile]) : null;
        var ks = Kommentare(zeile, spalte);
        var feld = spalte >= 0 ? _g.Columns[spalte].Name : "";
        res.Add(new ToolStripMenuItem("Kommentar erstellen", Symbole.Get("kommentar", 16), (_, _) =>
        {
            if (b is not { } bz) return;
            var k = KommentarDialog.Neu(_g.FindForm()!, $"Neuer Kommentar - Periodenübergreifend: {bz.Titel}");
            if (k is null) return;
            AppZustand.Doc.Aendern("Kommentar erstellen", AenderungsArt.Stammdaten, d => d.Kommentare.Add(new Kommentar
            {
                Id = d.NeueId(), Bezug = bz.Bezug, Feld = feld, Kategorie = k.Value.Kategorie, Text = k.Value.Text,
            }));
            _g.Invalidate();
        }) { Enabled = b != null });
        var bearbeiten = new ToolStripMenuItem("Kommentar bearbeiten") { Enabled = ks.Count > 0 };
        foreach (var k in ks)
        {
            var id = k.Id;
            var text = k.Text.Replace("\r", "").Replace("\n", " ");
            bearbeiten.DropDownItems.Add(text.Length > 40 ? text[..40] + "…" : text, null, (_, _) =>
            {
                var alt = D.Kommentare.FirstOrDefault(x => x.Id == id);
                if (alt is null || b is not { } bz) return;
                var neu = KommentarDialog.Neu(_g.FindForm()!, $"Kommentar bearbeiten - Periodenübergreifend: {bz.Titel}", alt.Text, alt.Kategorie);
                if (neu is null) return;
                AppZustand.Doc.Aendern("Kommentar bearbeiten", AenderungsArt.Stammdaten, d =>
                {
                    if (d.Kommentare.FirstOrDefault(x => x.Id == id) is not { } kk) return;
                    kk.Text = neu.Value.Text;
                    kk.Kategorie = neu.Value.Kategorie;
                    kk.Erstellt = DateTime.Now;
                });
                _g.Invalidate();
            });
        }
        res.Add(bearbeiten);
        res.Add(new ToolStripMenuItem("Kommentar(e) löschen", null, (_, _) =>
        {
            var ids = ks.Select(k => k.Id).ToHashSet();
            AppZustand.Doc.Aendern("Kommentar löschen", AenderungsArt.Stammdaten, d => d.Kommentare.RemoveAll(k => ids.Contains(k.Id)));
            _g.Invalidate();
        }) { Enabled = ks.Count > 0 });
        res.Add(new ToolStripMenuItem("Kommentar Übersichtsfenster", null, (_, _) => HauptFenster.Oeffnen<KommentarUebersicht>("komm", () => new KommentarUebersicht())));
        return res;
    }
}
