namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Tastaturbedienung des Menübands wie in Office und im Vorbild: Alt (oder F10) blendet Zugriffstasten (KeyTips) an „Datei“ und den
/// Registerkarten ein; die Taste einer Registerkarte wählt sie und zeigt die Zugriffstasten ihrer Befehle und Dialogstarter; die Taste eines
/// Befehls führt ihn aus (bzw. klappt sein Menü auf). Esc geht eine Stufe zurück, ein Mausklick oder erneutes Alt beendet die Anzeige.
/// Dazu die Dialogstarter in der rechten unteren Gruppenecke.
/// </summary>
public sealed partial class Ribbon
{
    /// <summary>0 = aus, 1 = Registerkarten, 2 = Befehle der gewählten Registerkarte.</summary>
    public int KeyTipStufe { get; private set; }
    private string _keyTipEingabe = "";
    private RibbonGruppe? _hoverStarter;
    private static readonly Font KeyTipSchrift = new("Segoe UI", 8.25f);

    private IEnumerable<RibbonGruppe> AktiveGruppen() => Seiten.ElementAtOrDefault(AktiveSeite)?.Gruppen ?? Enumerable.Empty<RibbonGruppe>();

    /// <summary>Dialogstarter wird gezeigt: Gruppe hat einen Starter, ist nicht zusammengeklappt und trägt ihre Beschriftung.</summary>
    private static bool StarterSichtbar(RibbonGruppe gr) => gr.DialogStarter != null && gr.Stufe < 2 && (!Ui.Vertretungsmodus || gr.Text == "Stundenpläne");

    /// <summary>Dialogstarter wie im Office-Menüband: kleines Quadrat mit Pfeil nach rechts unten.</summary>
    private void StarterZeichnen(Graphics g, RibbonGruppe gr)
    {
        var r = gr.StarterBounds;
        var alt = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        if (gr == _hoverStarter)
        {
            using var b = new SolidBrush(Ui.Hover);
            using var p = new Pen(Ui.HoverRand);
            g.FillRectangle(b, r);
            g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
        using (var p = new Pen(Color.FromArgb(110, 110, 110)))
        {
            // Winkel oben links und Pfeil zur rechten unteren Ecke
            g.DrawLine(p, r.X + 3, r.Y + 3, r.X + 7, r.Y + 3);
            g.DrawLine(p, r.X + 3, r.Y + 3, r.X + 3, r.Y + 7);
            g.DrawLine(p, r.X + 5, r.Y + 5, r.X + 9, r.Y + 9);
            g.DrawLine(p, r.X + 9, r.Y + 6, r.X + 9, r.Y + 9);
            g.DrawLine(p, r.X + 6, r.Y + 9, r.X + 9, r.Y + 9);
        }
        g.SmoothingMode = alt;
    }

    /// <summary>Ziel einer Zugriffstaste.</summary>
    internal sealed record KeyTipZiel(string Taste, Rectangle Anker, bool Mittig, Action Ausloesen, string Name);

    /// <summary>Zugriffstasten der aktuellen Stufe (eindeutig, feste Tasten zuerst, sonst Buchstaben der Beschriftung).</summary>
    internal List<KeyTipZiel> KeyTipZiele()
    {
        var res = new List<KeyTipZiel>();
        var belegt = new HashSet<string>();
        string Vergeben(string fest, string text, string reserve)
        {
            if (fest.Length > 0 && belegt.Add(fest.ToUpperInvariant())) return fest.ToUpperInvariant();
            foreach (var c in text.ToUpperInvariant().Where(char.IsLetterOrDigit))
                if (belegt.Add(c.ToString())) return c.ToString();
            foreach (var c in reserve)
                if (belegt.Add(c.ToString())) return c.ToString();
            // zweistellig: erster Buchstabe der Beschriftung und Ziffer (einstellige Tasten mit diesem Anfang gibt es dann nicht)
            for (var i = 1; i < 10; i++)
            {
                var k = "Y" + i;
                if (belegt.Add(k)) return k;
            }
            return "";
        }
        if (KeyTipStufe == 1)
        {
            belegt.Add("D");
            res.Add(new("D", _dateiBounds, true, () => { KeyTipsBeenden(); DateiMenueOeffnen().Focus(); }, "Datei"));
            for (var i = 0; i < Seiten.Count; i++)
            {
                var s = Seiten[i];
                if (!s.IstSichtbar) continue;
                var nr = i;
                res.Add(new(Vergeben(s.KeyTip, s.Text, "123456789"), s.TabBounds, true, () => { AktiveSeite = nr; KeyTipStufe = 2; _keyTipEingabe = ""; Invalidate(); }, s.Text));
            }
        }
        else if (KeyTipStufe == 2)
        {
            foreach (var gr in AktiveGruppen())
            {
                foreach (var e in gr.Angezeigt)
                {
                    if (e.Bounds.IsEmpty) continue;
                    var it = e;
                    res.Add(new(Vergeben(e.KeyTip, e.IstDatum ? "Datum" : e.Text, "0123456789"), e.Bounds, e.GrossDargestellt, () => KeyTipAusfuehren(it), e.IstDatum ? "Datum" : e.Text));
                }
                if (!gr.StarterBounds.IsEmpty && gr.DialogStarter is { } st)
                    res.Add(new(Vergeben("", "", "0123456789"), gr.StarterBounds, true, () => { KeyTipsBeenden(); st(); }, gr.Text + " (Dialogstarter)"));
            }
        }
        return res;
    }

    private void KeyTipAusfuehren(RibbonEintrag it)
    {
        KeyTipsBeenden();
        if (!(it.Aktiviert?.Invoke() ?? true)) return;
        if (it.IstDatum) { KalenderAufklappen(new Point(it.Bounds.X, it.Bounds.Bottom)); return; }
        if (it.Klick != null) { it.Klick(); return; }
        if (it.AktuellesMenue is { } menue) MenueZeigen(menue, new Point(it.Bounds.X, it.Bounds.Bottom)).Focus();
    }

    /// <summary>Zugriffstasten einblenden (Alt bzw. F10).</summary>
    public void KeyTipsStarten()
    {
        KeyTipStufe = 1;
        _keyTipEingabe = "";
        _tip.Setzen(this, "");
        Invalidate();
    }

    /// <summary>Zugriffstasten ausblenden.</summary>
    public void KeyTipsBeenden()
    {
        if (KeyTipStufe == 0) return;
        KeyTipStufe = 0;
        _keyTipEingabe = "";
        Invalidate();
    }

    /// <summary>Taste im KeyTip-Modus verarbeiten; true = verbraucht.</summary>
    public bool KeyTipTaste(Keys taste)
    {
        if (KeyTipStufe == 0) return false;
        switch (taste)
        {
            case Keys.Escape:
                if (_keyTipEingabe.Length > 0) _keyTipEingabe = "";
                else if (KeyTipStufe == 2) KeyTipStufe = 1;
                else KeyTipStufe = 0;
                Invalidate();
                return true;
            case Keys.Back:
                if (_keyTipEingabe.Length > 0) _keyTipEingabe = _keyTipEingabe[..^1];
                Invalidate();
                return true;
            case Keys.Left or Keys.Right when KeyTipStufe == 1:
                var sichtbar = Enumerable.Range(0, Seiten.Count).Where(i => Seiten[i].IstSichtbar).ToList();
                var pos = sichtbar.IndexOf(AktiveSeite);
                if (pos >= 0) AktiveSeite = sichtbar[Math.Clamp(pos + (taste == Keys.Left ? -1 : 1), 0, sichtbar.Count - 1)];
                Invalidate();
                return true;
            case Keys.Down or Keys.Return when KeyTipStufe == 1:
                KeyTipStufe = 2;
                Invalidate();
                return true;
        }
        var zeichen = TasteZeichen(taste);
        if (zeichen is null) return true;
        var eingabe = _keyTipEingabe + zeichen;
        var ziele = KeyTipZiele();
        if (ziele.FirstOrDefault(z => z.Taste == eingabe) is { } ziel)
        {
            _keyTipEingabe = "";
            ziel.Ausloesen();
            Invalidate();
        }
        else if (ziele.Any(z => z.Taste.StartsWith(eingabe, StringComparison.Ordinal))) { _keyTipEingabe = eingabe; Invalidate(); }
        return true;
    }

    private static string? TasteZeichen(Keys taste)
    {
        var k = taste & Keys.KeyCode;
        if (k is >= Keys.A and <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
        if (k is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k is >= Keys.NumPad0 and <= Keys.NumPad9) return ((char)('0' + (k - Keys.NumPad0))).ToString();
        return k switch { Keys.Oem1 => "Ü", Keys.Oem3 => "Ö", Keys.Oem7 => "Ä", _ => null };
    }

    /// <summary>Zugriffstasten wie im Office-Menüband: kleine helle Schildchen mit grauem Rand, an Registerkarten unten mittig,
    /// an großen Knöpfen unten mittig, an kleinen Knöpfen links neben dem Symbol.</summary>
    private void KeyTipsZeichnen(Graphics g)
    {
        if (KeyTipStufe == 0) return;
        var alt = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        using var rand = new Pen(Color.FromArgb(118, 118, 118));
        using var grund = new SolidBrush(Color.FromArgb(250, 250, 250));
        foreach (var z in KeyTipZiele())
        {
            if (!z.Taste.StartsWith(_keyTipEingabe, StringComparison.Ordinal) || z.Taste.Length == 0) continue;
            var groesse = TextRenderer.MeasureText(z.Taste, KeyTipSchrift, Size.Empty, TextFormatFlags.NoPadding);
            var w = Math.Max(15, groesse.Width + 7);
            const int h = 16;
            var a = z.Anker;
            var r = KeyTipStufe == 1 ? new Rectangle(a.X + (a.Width - w) / 2, a.Bottom - h / 2 - 1, w, h)
                : z.Mittig ? new Rectangle(a.X + (a.Width - w) / 2, a.Bottom - h + 2, w, h)
                : new Rectangle(a.X + 7, a.Y + 8, w, h);
            r.Y = Math.Min(r.Y, Height - h - 1);
            g.FillRectangle(grund, r);
            g.DrawRectangle(rand, r.X, r.Y, r.Width - 1, r.Height - 1);
            TextRenderer.DrawText(g, z.Taste, KeyTipSchrift, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        g.SmoothingMode = alt;
    }
}
