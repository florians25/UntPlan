namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Scrollender Rahmen um einen Stundenplan: wie im Vorbild wird zeilen- bzw. spaltenweise gescrollt (Pausenzeilen zählen als
/// Zeilen). Unter dem stehenden Tageskopf beginnt daher immer eine ganze Zeile, rechts der Stundenspalte immer ein ganzer Tag –
/// egal ob mit Mausrad, Rollbalken oder Tastatur gescrollt wird.
/// </summary>
internal sealed class PlanRahmen : Panel
{
    private const int WmHScroll = 0x114, WmVScroll = 0x115;
    private const int SbLineUp = 0, SbLineDown = 1, SbPageUp = 2, SbPageDown = 3, SbThumbPosition = 4, SbThumbTrack = 5, SbEndScroll = 8;

    /// <summary>Plan im Rahmen (liefert die Rasterpositionen).</summary>
    public PlanAnsicht? Plan { get; set; }

    private bool _rastet;

    private int Pos(bool senkrecht) => senkrecht ? -AutoScrollPosition.Y : -AutoScrollPosition.X;

    private void Setzen(bool senkrecht, int wert) =>
        AutoScrollPosition = senkrecht ? new Point(Pos(false), wert) : new Point(wert, Pos(true));

    /// <summary>Größte erreichbare Scrollposition.</summary>
    private int Hoechste(bool senkrecht) =>
        Math.Max(0, senkrecht ? DisplayRectangle.Height - ClientSize.Height : DisplayRectangle.Width - ClientSize.Width);

    /// <summary>Alle Rasterpositionen des Plans (ungefiltert).</summary>
    private List<int> RasterAlle(bool senkrecht) =>
        Plan is null || !AutoScroll ? new() : senkrecht ? Plan.ZeilenRaster() : Plan.SpaltenRaster();

    /// <summary>Erreichbare Rasterpositionen.</summary>
    private List<int> Raster(bool senkrecht)
    {
        var max = Hoechste(senkrecht);
        var r = RasterAlle(senkrecht).Where(v => v <= max).ToList();
        if (r.Count == 0 && Plan != null && AutoScroll) r.Add(0);
        return r;
    }

    /// <summary>Selbsttest: die aktuelle Position liegt in beiden Richtungen auf dem Raster.</summary>
    public bool Eingerastet => Plan is null || ((Raster(true) is var s && (s.Count == 0 || s.Contains(Pos(true)))) &&
                                                (Raster(false) is var w && (w.Count == 0 || w.Contains(Pos(false)))));

    /// <summary>
    /// Nach einer Bewegung von <paramref name="alt"/> auf die nächste Rasterposition einrasten; hat sich die Position bewegt,
    /// geht es mindestens eine Zeile weiter (sonst bliebe ein kurzer Ruck wirkungslos).
    /// </summary>
    private void Einrasten(bool senkrecht, int alt)
    {
        var r = Raster(senkrecht);
        if (r.Count == 0) return;
        var neu = Pos(senkrecht);
        var ziel = r.MinBy(v => Math.Abs(v - neu));
        if (ziel == alt && neu != alt)
            ziel = neu > alt ? r.Where(v => v > alt).DefaultIfEmpty(alt).Min() : r.Where(v => v < alt).DefaultIfEmpty(alt).Max();
        if (ziel != neu) Setzen(senkrecht, ziel);
    }

    /// <summary>Auf die nächstgelegenen Rasterpositionen (beide Richtungen) setzen.</summary>
    public void Einrasten()
    {
        if (_rastet) return;
        _rastet = true;
        try
        {
            Einrasten(true, Pos(true));
            Einrasten(false, Pos(false));
        }
        finally { _rastet = false; }
    }

    /// <summary>Um <paramref name="anzahl"/> ganze Zeilen (senkrecht) bzw. Tage (waagrecht) scrollen, negativ = zurück.</summary>
    public void Schritte(bool senkrecht, int anzahl)
    {
        var r = Raster(senkrecht);
        if (r.Count == 0 || anzahl == 0) return;
        var pos = Pos(senkrecht);
        var i = anzahl > 0 ? r.FindLastIndex(v => v <= pos) + anzahl : (r.FindIndex(v => v >= pos) is var j && j < 0 ? r.Count : j) + anzahl;
        Setzen(senkrecht, r[Math.Clamp(i, 0, r.Count - 1)]);
    }

    /// <summary>Um eine Seite scrollen: die erste nicht mehr ganz sichtbare Zeile wird die oberste (bzw. umgekehrt).</summary>
    public void Seite(bool senkrecht, int richtung)
    {
        var r = Raster(senkrecht);
        if (r.Count == 0 || Plan is null) return;
        var pos = Pos(senkrecht);
        var platz = (senkrecht ? ClientSize.Height - Plan.FixHoehe : ClientSize.Width - Plan.FixBreite);
        int ziel;
        if (richtung > 0)
        {
            // die unterste, nicht mehr ganz sichtbare Zeile wird die oberste
            ziel = r.Where(v => v > pos && v <= pos + platz).DefaultIfEmpty(-1).Max();
            if (ziel < 0) ziel = r.Where(v => v > pos).DefaultIfEmpty(r[^1]).Min();
        }
        else
        {
            ziel = r.Where(v => v < pos && v >= pos - platz).DefaultIfEmpty(-1).Min();
            if (ziel < 0) ziel = r.Where(v => v < pos).DefaultIfEmpty(0).Max();
        }
        Setzen(senkrecht, ziel);
    }

    protected override void WndProc(ref Message m)
    {
        if ((m.Msg == WmVScroll || m.Msg == WmHScroll) && Plan != null && AutoScroll && !_rastet && m.LParam == IntPtr.Zero)
        {
            var senkrecht = m.Msg == WmVScroll;
            var code = (int)(m.WParam.ToInt64() & 0xFFFF);
            switch (code)
            {
                case SbLineUp: Schritte(senkrecht, -1); return;
                case SbLineDown: Schritte(senkrecht, 1); return;
                case SbPageUp: Seite(senkrecht, -1); return;
                case SbPageDown: Seite(senkrecht, 1); return;
                case SbEndScroll: base.WndProc(ref m); return;
            }
            var alt = Pos(senkrecht);
            base.WndProc(ref m);
            _rastet = true;
            // Ziehen am Rollbalken: einfach die nächstgelegene Zeile (sonst spränge jedes Pixel eine Zeile weiter)
            try { Einrasten(senkrecht, code is SbThumbTrack or SbThumbPosition ? Pos(senkrecht) : alt); }
            finally { _rastet = false; }
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Ein Klick in den Plan setzt den Fokus; dabei nicht automatisch scrollen (sonst verschiebt sich der Plan unter der Maus
    /// und ein einfacher Klick würde wie Ziehen wirken).</summary>
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Plan != null && AutoScroll && (VerticalScroll.Visible || HorizontalScroll.Visible))
        {
            Rad(e.Delta, ModifierKeys.HasFlag(Keys.Shift) || !VerticalScroll.Visible);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    /// <summary>Mausrad: je Raste eine ganze Zeile (mit Umschalt bzw. ohne senkrechten Rollbalken ein Tag waagrecht).</summary>
    public void Rad(int delta, bool waagrecht) => Schritte(!waagrecht, -Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 120));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_rastet || Plan is null) return;
        _rastet = true;
        try
        {
            // Rand unter bzw. rechts des Plans, sodass auch die letzte Scrollposition auf einer Zeilen- bzw. Tagesgrenze liegt
            for (var i = 0; i < 2; i++)
            {
                var rand = new Size(Rand(false), Rand(true));
                if (rand == AutoScrollMargin) break;
                AutoScrollMargin = rand;
            }
            Einrasten(true, Pos(true));
            Einrasten(false, Pos(false));
        }
        finally { _rastet = false; }
    }

    private int Rand(bool senkrecht)
    {
        if (Plan is null || !AutoScroll) return 0;
        var max = senkrecht ? Plan.Height - ClientSize.Height : Plan.Width - ClientSize.Width;
        if (max <= 0) return 0;
        var grenze = RasterAlle(senkrecht).Where(v => v >= max).DefaultIfEmpty(-1).Min();
        return grenze < 0 ? 0 : grenze - max;
    }
}
