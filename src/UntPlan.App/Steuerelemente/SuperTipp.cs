namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Erweiterte Kurzinfo wie in der Vorlage (Menüband und Schnellzugriffsleiste): weiße Fläche mit grauem Rand, oben Symbol und
/// fette Überschrift, darunter die Beschreibung in Grau, eine Trennlinie und „Mit F1 erhalten Sie Hilfe.“ mit dem Hilfesymbol.
/// </summary>
public sealed class SuperTipp : IDisposable
{
    private const int Rand = 7;
    private const int MaxTextBreite = 260;
    private static readonly Color Linie = Color.FromArgb(118, 118, 118);
    private static readonly Color Trenner = Color.FromArgb(214, 214, 214);
    private static readonly Color TextGrau = Color.FromArgb(87, 87, 87);
    private const string Fusszeile = "Mit F1 erhalten Sie Hilfe.";

    private readonly ToolTip _tip = new() { OwnerDraw = true, InitialDelay = 600, ReshowDelay = 100, AutoPopDelay = 12000, UseAnimation = false, UseFading = false };
    private string _titel = "";
    private string _text = "";
    private Image? _bild;
    private bool _mitHilfe = true;

    public SuperTipp()
    {
        _tip.Popup += (_, e) => e.ToolTipSize = Groesse();
        _tip.Draw += (_, e) => Zeichnen(e.Graphics, e.Bounds);
    }

    /// <summary>Inhalt für das Steuerelement setzen (leerer Titel blendet die Kurzinfo aus).</summary>
    public void Setzen(Control c, string titel, string text = "", Image? bild = null, bool mitHilfe = true)
    {
        if (titel == _titel && text == _text && ReferenceEquals(bild, _bild) && mitHilfe == _mitHilfe && _tip.GetToolTip(c) == titel) return;
        _titel = titel;
        _text = text;
        _bild = bild;
        _mitHilfe = mitHilfe;
        _tip.SetToolTip(c, titel);
    }

    /// <summary>Kurzinfo einer Leiste (Schnellzugriff, Fensteranordnungen) statt der einfachen Windows-Kurzinfo: erste Zeile
    /// des Hinweistexts als Überschrift, der Rest als Beschreibung. Rechtsbündige Einträge (Fensterknöpfe) ohne Symbol und Hilfezeile.</summary>
    public void FuerLeiste(ToolStrip leiste)
    {
        leiste.ShowItemToolTips = false;
        ToolStripItem? letztes = null;
        leiste.MouseMove += (_, e) =>
        {
            var it = leiste.GetItemAt(e.Location);
            if (it == letztes) return;
            letztes = it;
            var hinweis = it?.ToolTipText ?? "";
            if (string.IsNullOrEmpty(hinweis) && it is { DisplayStyle: not ToolStripItemDisplayStyle.Image }) hinweis = it.Text ?? "";
            if (it is null || string.IsNullOrEmpty(hinweis)) { Setzen(leiste, ""); return; }
            var teile = hinweis.Split('\n', 2);
            var links = it.Alignment == ToolStripItemAlignment.Left;
            Setzen(leiste, teile[0].Trim(), teile.Length > 1 ? teile[1].Trim() : "", links ? it.Image : null, links);
        };
        leiste.MouseLeave += (_, _) => { letztes = null; Setzen(leiste, ""); };
    }

    private (Size Titel, Size Text, Size Fuss) Masse()
    {
        var titel = TextRenderer.MeasureText(_titel, Ui.SchriftFett, new Size(MaxTextBreite, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var text = _text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(_text, Ui.Schrift, new Size(MaxTextBreite, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var fuss = _mitHilfe ? TextRenderer.MeasureText(Fusszeile, Ui.Schrift) : Size.Empty;
        return (titel, text, fuss);
    }

    private Size Groesse()
    {
        var (titel, text, fuss) = Masse();
        var symbol = _bild != null ? 20 : 0;
        var w = Math.Max(symbol + titel.Width, Math.Max(text.Width + 10, _mitHilfe ? 20 + fuss.Width : 0));
        var h = Math.Max(_bild != null ? 16 : 0, titel.Height);
        if (text.Height > 0) h += 6 + text.Height;
        if (_mitHilfe) h += 13 + Math.Max(16, fuss.Height);
        return new Size(Math.Max(_mitHilfe ? 190 : 0, w) + 2 * Rand, h + 2 * Rand);
    }

    private void Zeichnen(Graphics g, Rectangle r)
    {
        g.Clear(Color.White);
        using (var p = new Pen(Linie)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        var (titel, text, fuss) = Masse();
        var x = r.X + Rand;
        var y = r.Y + Rand;
        var zeile = Math.Max(_bild != null ? 16 : 0, titel.Height);
        if (_bild != null) g.DrawImage(_bild, x, y + (zeile - 16) / 2, 16, 16);
        var tx = x + (_bild != null ? 20 : 0);
        TextRenderer.DrawText(g, _titel, Ui.SchriftFett, new Rectangle(tx, y + (zeile - titel.Height) / 2, r.Right - Rand - tx, titel.Height), Color.FromArgb(30, 30, 30),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        y += zeile;
        if (text.Height > 0)
        {
            y += 6;
            TextRenderer.DrawText(g, _text, Ui.Schrift, new Rectangle(x + 10, y, r.Right - Rand - x - 10, text.Height), TextGrau,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            y += text.Height;
        }
        if (!_mitHilfe) return;
        y += 6;
        using (var p = new Pen(Trenner)) g.DrawLine(p, r.X + Rand, y, r.Right - Rand - 1, y);
        y += 7;
        var fz = Math.Max(16, fuss.Height);
        g.DrawImage(Symbole.Get("hilfe", 16), x, y + (fz - 16) / 2, 16, 16);
        TextRenderer.DrawText(g, Fusszeile, Ui.Schrift, new Rectangle(x + 20, y, r.Right - Rand - x - 20, fz), Color.FromArgb(30, 30, 30),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    public void Dispose() => _tip.Dispose();
}
