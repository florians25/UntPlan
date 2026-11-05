namespace UntPlan.App.Steuerelemente;

/// <summary>Spalte des Planungsrasters (z. B. eine Stunde eines Tages; Gruppe = Tagesname für die obere Kopfzeile).</summary>
public sealed record RasterSpalte(string Gruppe, string Text, object? Daten = null);

/// <summary>Zeile des Planungsrasters (Summenzeile, Element, Datum …). <see cref="Offen"/>: null = nicht ausklappbar.</summary>
public sealed class RasterZeile
{
    public string Text { get; init; } = "";
    public int Ebene { get; init; }
    public bool? Offen { get; set; }
    public Color? Farbe { get; init; }
    public Color? Hintergrund { get; init; }
    public bool Fett { get; init; }
    public object? Daten { get; init; }
}

/// <summary>Inhalt einer Rasterzelle.</summary>
public readonly record struct RasterZelle(string Text, Color Hintergrund, Color Vordergrund, bool Fett = false, bool Kasten = false)
{
    public static RasterZelle Leer => new("", Color.White, Color.Black);
}

/// <summary>
/// Stecktafel des Planungsdialogs (Handbuch Stundenplan Kap. 5.4): Stunden der Woche nebeneinander (Kopf mit Tag und Stunde), Elemente
/// zeilenweise untereinander, Baumspalte mit +/− vor den Summenzeilen. Feste Kopf- und Namensspalten, eigene Rollbalken, Zellcursor mit
/// Bereichsmarkierung (Umschalt), Drag &amp; Drop (auch von außen) mit Hervorhebung möglicher Ziele.
/// </summary>
public sealed class PlanungsRaster : Control
{
    private readonly HScrollBar _h = new() { Dock = DockStyle.Bottom };
    private readonly VScrollBar _v = new() { Dock = DockStyle.Right };
    private Point _down;
    private (int Z, int S)? _downZelle;
    private (int Z, int S)? _ziel;

    public List<RasterSpalte> Spalten { get; } = new();
    public List<RasterZeile> Zeilen { get; } = new();
    /// <summary>Inhalt einer Zelle (Zeile, Spalte).</summary>
    public Func<int, int, RasterZelle>? ZelleLiefern { get; set; }
    /// <summary>Hervorhebung möglicher Ziele beim Ziehen (Zeile, Spalte, Ziehdaten) → Farbe bzw. Symbol.</summary>
    public Func<int, int, string, (Color Farbe, string? Symbol)?>? ZielLiefern { get; set; }
    public int Spaltenbreite { get; set; } = 26;
    public int Zeilenhoehe { get; set; } = 18;
    public int Namensbreite { get; set; } = 110;
    /// <summary>Breite der Baumspalte (+/−); 0 = keine.</summary>
    public int Baumbreite { get; set; } = 18;
    public bool ZweiKopfzeilen { get; set; } = true;
    public string Ecke { get; set; } = "";

    /// <summary>Zellcursor (Zeile, Spalte) und Anker der Bereichsmarkierung.</summary>
    public (int Z, int S) Zellcursor { get; private set; }
    public (int Z, int S) Anker { get; private set; }
    /// <summary>Daten des laufenden Ziehvorgangs (auch von außen hereingezogen), sonst null.</summary>
    public string? Ziehdaten { get; private set; }

    public event Action? CursorGeaendert;
    public event Action<int, int>? Doppelklick;
    public event Action<int>? Umgeschaltet;
    public event Func<int, int, string?>? ZiehenBeginnt;
    public event Action<int, int, string, bool>? Abgelegt;
    public event Action<int, int>? ZielGeaendert;
    public event Action<int, int, Point>? Kontext;

    private int Kopfhoehe => ZweiKopfzeilen ? 2 * Zeilenhoehe : Zeilenhoehe;
    private int Links => Baumbreite + Namensbreite;
    private int Sx => _h.Visible ? _h.Value : 0;
    private int Sy => _v.Visible ? _v.Value : 0;

    public PlanungsRaster()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        AllowDrop = true;
        BackColor = Color.White;
        Font = Ui.Schrift;
        Controls.Add(_h);
        Controls.Add(_v);
        _h.Scroll += (_, _) => Invalidate();
        _v.Scroll += (_, _) => Invalidate();
        _h.ValueChanged += (_, _) => Invalidate();
        _v.ValueChanged += (_, _) => Invalidate();
    }

    /// <summary>Nach Änderung von Zeilen, Spalten oder Maßen: Rollbalken anpassen und neu zeichnen.</summary>
    public void Neu()
    {
        Zellcursor = (Math.Clamp(Zellcursor.Z, 0, Math.Max(0, Zeilen.Count - 1)), Math.Clamp(Zellcursor.S, 0, Math.Max(0, Spalten.Count - 1)));
        Anker = (Math.Clamp(Anker.Z, 0, Math.Max(0, Zeilen.Count - 1)), Math.Clamp(Anker.S, 0, Math.Max(0, Spalten.Count - 1)));
        Rollbalken();
        Invalidate();
    }

    /// <summary>Benötigte Größe ohne Rollbalken.</summary>
    public Size Benoetigt => new(Links + Spalten.Count * Spaltenbreite + 2, Kopfhoehe + Zeilen.Count * Zeilenhoehe + 2);

    private void Rollbalken()
    {
        var b = Benoetigt;
        var hNoetig = b.Width > ClientSize.Width;
        var vNoetig = b.Height > ClientSize.Height - (hNoetig ? _h.Height : 0);
        if (!hNoetig && vNoetig) hNoetig = b.Width > ClientSize.Width - _v.Width;
        _h.Visible = hNoetig;
        _v.Visible = vNoetig;
        var innenB = ClientSize.Width - (vNoetig ? _v.Width : 0) - Links;
        var innenH = ClientSize.Height - (hNoetig ? _h.Height : 0) - Kopfhoehe;
        if (hNoetig)
        {
            _h.Maximum = Math.Max(0, Spalten.Count * Spaltenbreite);
            _h.LargeChange = Math.Max(1, innenB);
            _h.SmallChange = Spaltenbreite;
            _h.Value = Math.Clamp(_h.Value, 0, Math.Max(0, _h.Maximum - _h.LargeChange + 1));
        }
        else _h.Value = 0;
        if (vNoetig)
        {
            _v.Maximum = Math.Max(0, Zeilen.Count * Zeilenhoehe);
            _v.LargeChange = Math.Max(1, innenH);
            _v.SmallChange = Zeilenhoehe;
            _v.Value = Math.Clamp(_v.Value, 0, Math.Max(0, _v.Maximum - _v.LargeChange + 1));
        }
        else _v.Value = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Rollbalken();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var sb = _v.Visible && (ModifierKeys & Keys.Shift) == 0 ? (ScrollBar)_v : _h.Visible ? _h : null;
        if (sb is null) return;
        sb.Value = Math.Clamp(sb.Value - Math.Sign(e.Delta) * sb.SmallChange * 3, 0, Math.Max(0, sb.Maximum - sb.LargeChange + 1));
    }

    /// <summary>Zelle unter einem Punkt (Spalte −1 = Namensbereich, Zeile −1 = Kopf).</summary>
    public (int Z, int S) Bei(Point p)
    {
        var z = p.Y < Kopfhoehe ? -1 : (p.Y - Kopfhoehe + Sy) / Math.Max(1, Zeilenhoehe);
        var s = p.X < Links ? -1 : (p.X - Links + Sx) / Math.Max(1, Spaltenbreite);
        if (z >= Zeilen.Count) z = -2;
        if (s >= Spalten.Count) s = -2;
        return (z, s);
    }

    /// <summary>Bildschirmrechteck einer Zelle (für Kontextmenüs und Hinweise).</summary>
    public Rectangle ZellRechteck(int z, int s) => new(Links + s * Spaltenbreite - Sx, Kopfhoehe + z * Zeilenhoehe - Sy, Spaltenbreite, Zeilenhoehe);

    public bool Markiert(int z, int s) =>
        z >= Math.Min(Zellcursor.Z, Anker.Z) && z <= Math.Max(Zellcursor.Z, Anker.Z) && s >= Math.Min(Zellcursor.S, Anker.S) && s <= Math.Max(Zellcursor.S, Anker.S);

    public void CursorSetzen(int z, int s, bool bereich = false)
    {
        if (Zeilen.Count == 0 || Spalten.Count == 0) return;
        z = Math.Clamp(z, 0, Zeilen.Count - 1);
        s = Math.Clamp(s, 0, Spalten.Count - 1);
        Zellcursor = (z, s);
        if (!bereich) Anker = Zellcursor;
        Sichtbar(z, s);
        Invalidate();
        CursorGeaendert?.Invoke();
    }

    private void Sichtbar(int z, int s)
    {
        var innenB = ClientSize.Width - (_v.Visible ? _v.Width : 0) - Links;
        var innenH = ClientSize.Height - (_h.Visible ? _h.Height : 0) - Kopfhoehe;
        if (_h.Visible)
        {
            var x = s * Spaltenbreite;
            if (x < _h.Value) _h.Value = x;
            else if (x + Spaltenbreite > _h.Value + innenB) _h.Value = Math.Min(Math.Max(0, _h.Maximum - _h.LargeChange + 1), x + Spaltenbreite - innenB);
        }
        if (_v.Visible)
        {
            var y = z * Zeilenhoehe;
            if (y < _v.Value) _v.Value = y;
            else if (y + Zeilenhoehe > _v.Value + innenH) _v.Value = Math.Min(Math.Max(0, _v.Maximum - _v.LargeChange + 1), y + Zeilenhoehe - innenH);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var (z, s) = Zellcursor;
        var tag = Spalten.ElementAtOrDefault(s)?.Gruppe;
        switch (e.KeyCode)
        {
            case Keys.Left when e.Alt:
            case Keys.Right when e.Alt:
                // selbe Stunde des vorigen bzw. nächsten Tages
                var schritt = Spalten.Count(x => x.Gruppe == tag);
                CursorSetzen(z, s + (e.KeyCode == Keys.Right ? schritt : -schritt), e.Shift);
                e.Handled = e.SuppressKeyPress = true;
                return;
            case Keys.Left: CursorSetzen(z, s - 1, e.Shift); break;
            case Keys.Right: CursorSetzen(z, s + 1, e.Shift); break;
            case Keys.Up: CursorSetzen(z - 1, s, e.Shift); break;
            case Keys.Down: CursorSetzen(z + 1, s, e.Shift); break;
            case Keys.Home: CursorSetzen(z, 0, e.Shift); break;   // erste Stunde der Woche
            case Keys.End: CursorSetzen(z, Spalten.Count - 1, e.Shift); break; // letzte Stunde der Woche
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        _down = e.Location;
        var (z, s) = Bei(e.Location);
        _downZelle = null;
        if (z >= 0 && s == -1 && Baumbreite > 0 && e.X < Baumbreite && Zeilen[z].Offen is bool)
        {
            Umgeschaltet?.Invoke(z);
            return;
        }
        if (z < 0) { base.OnMouseDown(e); return; }
        var spalte = Math.Max(0, s);
        if (e.Button == MouseButtons.Left)
        {
            CursorSetzen(z, spalte, (ModifierKeys & Keys.Shift) != 0);
            if (s >= 0) _downZelle = (z, s);
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (!Markiert(z, spalte)) CursorSetzen(z, spalte);
            Kontext?.Invoke(z, s, e.Location);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left || _downZelle is not { } dz) return;
        var schwelle = Math.Max(5, SystemInformation.DragSize.Width);
        if (Math.Abs(e.X - _down.X) < schwelle && Math.Abs(e.Y - _down.Y) < schwelle) return;
        _downZelle = null;
        var daten = ZiehenBeginnt?.Invoke(dz.Z, dz.S);
        if (daten is null) return;
        Ziehdaten = daten;
        try { DoDragDrop(daten, DragDropEffects.Move | DragDropEffects.Copy); }
        finally { ZiehenBeenden(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _downZelle = null;
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var (z, s) = Bei(e.Location);
        if (e.Button == MouseButtons.Left && z >= 0 && s >= 0) Doppelklick?.Invoke(z, s);
    }

    private void ZiehenBeenden()
    {
        Ziehdaten = null;
        _ziel = null;
        Invalidate();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        Ziehdaten = e.Data?.GetData(DataFormats.Text) as string;
        OnDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (Ziehdaten is null) { e.Effect = DragDropEffects.None; return; }
        var p = PointToClient(new Point(e.X, e.Y));
        var (z, s) = Bei(p);
        // am Rand automatisch weiterrollen
        if (_h.Visible && p.X > ClientSize.Width - 20 - (_v.Visible ? _v.Width : 0)) _h.Value = Math.Min(Math.Max(0, _h.Maximum - _h.LargeChange + 1), _h.Value + Spaltenbreite);
        else if (_h.Visible && p.X < Links + 10 && p.X > Links) _h.Value = Math.Max(0, _h.Value - Spaltenbreite);
        if (z < 0 || s < 0) { e.Effect = DragDropEffects.None; return; }
        var kopieren = (e.KeyState & 8) != 0 && (e.AllowedEffect & DragDropEffects.Copy) != 0;
        e.Effect = kopieren ? DragDropEffects.Copy : (e.AllowedEffect & DragDropEffects.Move) != 0 ? DragDropEffects.Move : e.AllowedEffect & DragDropEffects.Copy;
        if (_ziel != (z, s))
        {
            _ziel = (z, s);
            Invalidate();
            ZielGeaendert?.Invoke(z, s);
        }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _ziel = null;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        var daten = e.Data?.GetData(DataFormats.Text) as string ?? Ziehdaten;
        var (z, s) = Bei(PointToClient(new Point(e.X, e.Y)));
        var kopieren = (e.KeyState & 8) != 0;
        ZiehenBeenden();
        if (daten != null && z >= 0 && s >= 0) BeginInvoke(() => Abgelegt?.Invoke(z, s, daten, kopieren));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var lh = Kopfhoehe;
        var innenRechts = ClientSize.Width - (_v.Visible ? _v.Width : 0);
        var innenUnten = ClientSize.Height - (_h.Visible ? _h.Height : 0);
        using var linie = new Pen(Color.FromArgb(200, 205, 215));
        using var tagLinie = new Pen(Color.FromArgb(120, 130, 150));
        using var kopf = new SolidBrush(Color.FromArgb(245, 236, 210));
        using var kopfGrau = new SolidBrush(Color.FromArgb(236, 240, 246));
        var fmt = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        var fmtL = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var erste = Math.Max(0, Sy / Math.Max(1, Zeilenhoehe));
        var letzte = Math.Min(Zeilen.Count - 1, (Sy + innenUnten - lh) / Math.Max(1, Zeilenhoehe));
        var ersteS = Math.Max(0, Sx / Math.Max(1, Spaltenbreite));
        var letzteS = Math.Min(Spalten.Count - 1, (Sx + innenRechts - Links) / Math.Max(1, Spaltenbreite));
        using var fett = new Font(Font, FontStyle.Bold);

        // Zellen
        g.SetClip(new Rectangle(Links, lh, Math.Max(0, innenRechts - Links), Math.Max(0, innenUnten - lh)));
        for (var z = erste; z <= letzte; z++)
            for (var s = ersteS; s <= letzteS; s++)
            {
                var r = ZellRechteck(z, s);
                var c = ZelleLiefern?.Invoke(z, s) ?? RasterZelle.Leer;
                var hinter = c.Hintergrund;
                string? symbol = null;
                if (Ziehdaten != null && ZielLiefern?.Invoke(z, s, Ziehdaten) is { } zl) { hinter = zl.Farbe; symbol = zl.Symbol; }
                using (var b = new SolidBrush(hinter)) g.FillRectangle(b, r);
                if (Markiert(z, s))
                    using (var b = new SolidBrush(Color.FromArgb(70, 40, 90, 200))) g.FillRectangle(b, r);
                var text = symbol ?? c.Text;
                if (c.Kasten && text.Length > 0)
                {
                    var k = new Rectangle(r.X + (r.Width - Math.Min(r.Width - 4, 14)) / 2, r.Y + 2, Math.Min(r.Width - 4, 14), r.Height - 4);
                    using (var b = new SolidBrush(Color.FromArgb(255, 240, 0))) g.FillRectangle(b, k);
                    g.DrawRectangle(Pens.Goldenrod, k);
                    TextRenderer.DrawText(g, text, fett, k, Color.FromArgb(0, 70, 200), fmt);
                }
                else if (text.Length > 0) TextRenderer.DrawText(g, text, c.Fett || symbol != null ? fett : Font, r, c.Vordergrund, fmt);
                g.DrawLine(linie, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                var neuerTag = s + 1 < Spalten.Count && Spalten[s + 1].Gruppe != Spalten[s].Gruppe;
                g.DrawLine(neuerTag ? tagLinie : linie, r.Right - 1, r.Top, r.Right - 1, r.Bottom);
                if (_ziel == (z, s)) using (var p = new Pen(Color.DarkRed, 2)) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                if (Zellcursor == (z, s) && Focused) using (var p = new Pen(Color.Black) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) g.DrawRectangle(p, r.X, r.Y, r.Width - 2, r.Height - 2);
            }
        // Kopf
        g.SetClip(new Rectangle(Links, 0, Math.Max(0, innenRechts - Links), lh));
        for (var s = ersteS; s <= letzteS; s++)
        {
            var x = Links + s * Spaltenbreite - Sx;
            var rUnten = new Rectangle(x, lh - Zeilenhoehe, Spaltenbreite, Zeilenhoehe);
            g.FillRectangle(kopfGrau, rUnten);
            TextRenderer.DrawText(g, Spalten[s].Text, Font, rUnten, Color.Black, fmt);
            g.DrawRectangle(linie, rUnten.X, rUnten.Y, rUnten.Width - 1, rUnten.Height - 1);
        }
        if (ZweiKopfzeilen)
        {
            var s0 = ersteS;
            while (s0 <= letzteS && s0 >= 0)
            {
                var gruppe = Spalten[s0].Gruppe;
                var a = s0;
                while (a > 0 && Spalten[a - 1].Gruppe == gruppe) a--;
                var b = s0;
                while (b + 1 < Spalten.Count && Spalten[b + 1].Gruppe == gruppe) b++;
                var r = new Rectangle(Links + a * Spaltenbreite - Sx, 0, (b - a + 1) * Spaltenbreite, Zeilenhoehe);
                g.FillRectangle(kopf, r);
                g.DrawRectangle(linie, r.X, r.Y, r.Width - 1, r.Height - 1);
                var sichtbar = Rectangle.Intersect(r, new Rectangle(Links, 0, innenRechts - Links, Zeilenhoehe));
                TextRenderer.DrawText(g, gruppe, Font, sichtbar, Color.Black, fmt);
                s0 = b + 1;
            }
        }
        // Namensspalte
        g.SetClip(new Rectangle(0, lh, Links, Math.Max(0, innenUnten - lh)));
        for (var z = erste; z <= letzte; z++)
        {
            var zeile = Zeilen[z];
            var y = lh + z * Zeilenhoehe - Sy;
            var rz = new Rectangle(0, y, Links, Zeilenhoehe);
            using (var b = new SolidBrush(zeile.Hintergrund ?? (Zellcursor.Z == z ? Ui.ZeileAktiv : Color.FromArgb(250, 251, 253)))) g.FillRectangle(b, rz);
            if (Baumbreite > 0 && zeile.Offen is bool offen)
            {
                var k = new Rectangle(4 + zeile.Ebene * 6, y + (Zeilenhoehe - 9) / 2, 9, 9);
                g.FillRectangle(Brushes.White, k);
                g.DrawRectangle(Pens.Gray, k);
                g.DrawLine(Pens.Black, k.Left + 2, k.Top + 4, k.Right - 2, k.Top + 4);
                if (!offen) g.DrawLine(Pens.Black, k.Left + 4, k.Top + 2, k.Left + 4, k.Bottom - 2);
            }
            else if (Baumbreite > 0 && zeile.Ebene > 0)
            {
                using var p = new Pen(Color.Gray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                g.DrawLine(p, 8, y, 8, y + Zeilenhoehe / 2);
                g.DrawLine(p, 8, y + Zeilenhoehe / 2, Baumbreite - 2, y + Zeilenhoehe / 2);
            }
            var rn = new Rectangle(Baumbreite + 3 + zeile.Ebene * 8, y, Namensbreite - 4 - zeile.Ebene * 8, Zeilenhoehe);
            TextRenderer.DrawText(g, zeile.Text, zeile.Fett ? fett : Font, rn, zeile.Farbe ?? Color.FromArgb(0, 0, 160), fmtL);
            g.DrawLine(linie, 0, y + Zeilenhoehe - 1, Links, y + Zeilenhoehe - 1);
            if (Baumbreite > 0) g.DrawLine(linie, Baumbreite - 1, y, Baumbreite - 1, y + Zeilenhoehe);
        }
        g.ResetClip();
        // Ecke
        var ecke = new Rectangle(0, 0, Links, lh);
        g.FillRectangle(kopfGrau, ecke);
        g.DrawRectangle(linie, 0, 0, Links - 1, lh - 1);
        if (Ecke.Length > 0) TextRenderer.DrawText(g, Ecke, Font, ecke, Color.Black, fmt);
        g.DrawLine(tagLinie, Links - 1, 0, Links - 1, innenUnten);
        if (_h.Visible && _v.Visible)
            using (var b = new SolidBrush(SystemColors.Control)) g.FillRectangle(b, innenRechts, innenUnten, _v.Width, _h.Height);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
