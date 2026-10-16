using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Übersichtsplan (z. B. "Lehrer Übersicht quer"): Elemente als Zeilen, Tage × Stunden als Spalten.
/// Im Raum-Übersichtsplan können Raumverlegungen bzw. -täusche per Drag &amp; Drop durchgeführt werden.
/// </summary>
public sealed class UebersichtFenster : MdiFenster, IDruckbar
{
    private readonly ElementTyp _typ;
    private readonly Raster _raster;

    public UebersichtFenster(ElementTyp typ)
    {
        _typ = typ;
        Text = $"{Ui.TypName(typ, true)} Übersicht quer";
        HilfeThema = "Stundenpläne";
        Icon = SymbolIcon("uebersicht");
        Size = new Size(1100, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        ts.Items.Add(new ToolStripLabel(typ == ElementTyp.Raum ? "Raumverlegung: Stunde per Drag & Drop in eine andere Raumzeile ziehen." : "Stunde per Drag & Drop innerhalb der Zeile verschieben."));
        _raster = new Raster(this) { Dock = DockStyle.Fill };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(_raster);
        _raster.Dock = DockStyle.None;
        scroll.Resize += (_, _) => _raster.GroesseAnpassen(scroll.ClientSize);
        Controls.Add(scroll);
        Controls.Add(ts);
    }

    protected override void Aktualisieren(AenderungsArt art) => _raster.Invalidate();

    public void Drucken(bool vorschau)
    {
        var doc = new System.Drawing.Printing.PrintDocument { DocumentName = Text };
        doc.DefaultPageSettings.Landscape = true;
        doc.PrintPage += (sender, e) =>
        {
            var bereich = ListenDruck.Seitenbereich(sender, e);
            e.Graphics!.DrawString(Text + " – " + D.Einstellungen.Schulname, Ui.SchriftGross, Brushes.Black, bereich.Left, bereich.Top - 25);
            _raster.Zeichnen(e.Graphics!, bereich);
        };
        ListenDruck.Ausgeben(this, doc, vorschau);
    }

    private sealed class Raster : Control
    {
        private readonly UebersichtFenster _f;
        private (int Zeile, Planstunde P)? _drag;
        private const int NameBreite = 60;
        private const int Kopf = 36;

        public Raster(UebersichtFenster f)
        {
            _f = f;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private List<Element> Elemente => Stammdaten.Sortiert(AppZustand.D, _f._typ).ToList();
        private int Spalten => AppZustand.D.Zeitraster.TageProWoche * AppZustand.D.Zeitraster.StundenProTag;

        public void GroesseAnpassen(Size client)
        {
            var hoehe = Kopf + Elemente.Count * 22 + 2;
            Size = new Size(Math.Max(client.Width - 2, NameBreite + Spalten * 18), Math.Max(client.Height - 2, hoehe));
            Invalidate();
        }

        private float ZellBreite(Rectangle b) => (b.Width - NameBreite) / (float)Spalten;
        private float ZeilenHoehe(Rectangle b) => Math.Min(26, (b.Height - Kopf) / (float)Math.Max(1, Elemente.Count));

        protected override void OnPaint(PaintEventArgs e) => Zeichnen(e.Graphics, ClientRectangle);

        public void Zeichnen(Graphics g, Rectangle b)
        {
            var d = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            var zr = d.Zeitraster;
            var cw = ZellBreite(b);
            var rh = ZeilenHoehe(b);
            g.FillRectangle(Brushes.White, b);
            for (var t = 0; t < zr.TageProWoche; t++)
            {
                var x = b.X + NameBreite + t * zr.StundenProTag * cw;
                TextRenderer.DrawText(g, Planungsdienst.Tagname(d, t, true), Ui.SchriftFett, new Rectangle((int)x, b.Y, (int)(zr.StundenProTag * cw), 18), Color.Black, TextFormatFlags.HorizontalCenter);
                for (var s = 1; s <= zr.StundenProTag; s++)
                    TextRenderer.DrawText(g, zr.StundenLabel(s), Ui.SchriftKlein, new Rectangle((int)(x + (s - 1) * cw), b.Y + 18, (int)cw, 16), Color.DimGray, TextFormatFlags.HorizontalCenter);
                g.DrawLine(Pens.Black, x, b.Y, x, b.Y + Kopf + rh * Elemente.Count);
            }
            var elemente = Elemente;
            for (var i = 0; i < elemente.Count; i++)
            {
                var y = b.Y + Kopf + i * rh;
                g.FillRectangle(i % 2 == 0 ? Brushes.White : new SolidBrush(Color.FromArgb(247, 249, 252)), b.X, y, b.Width, rh);
                TextRenderer.DrawText(g, elemente[i].Kurzname, Ui.SchriftFett, new Rectangle(b.X + 2, (int)y, NameBreite - 4, (int)rh), Color.Black, TextFormatFlags.VerticalCenter);
                foreach (var p in ix.PlanVon(_f._typ, elemente[i].Id))
                {
                    if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
                    var x = b.X + NameBreite + (p.Tag * zr.StundenProTag + p.Stunde - 1) * cw;
                    var fach = u.HauptfachId is int fid ? ix.Faecher.GetValueOrDefault(fid) : null;
                    var farbe = Ui.Farbe(fach?.Hintergrund ?? 0, Color.FromArgb(210, 220, 240));
                    var r = new RectangleF(x + 1, y + 1, cw - 2, rh - 2);
                    using (var br = new SolidBrush(farbe)) g.FillRectangle(br, r);
                    var text = _f._typ switch
                    {
                        ElementTyp.Lehrer => ix.KKlassen(u.KlassenIds),
                        ElementTyp.Klasse => string.Join(",", u.LehrerIds.Select(ix.KLehrer)),
                        _ => ix.KKlassen(u.KlassenIds),
                    };
                    TextRenderer.DrawText(g, (p.Fixiert ? "*" : "") + text, Ui.SchriftKlein, Rectangle.Round(r), Ui.Textfarbe(farbe), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                g.DrawLine(Pens.Gainsboro, b.X, y + rh, b.Right, y + rh);
            }
        }

        private (int Zeile, int Tag, int Stunde)? Treffer(Point p)
        {
            var b = ClientRectangle;
            var cw = ZellBreite(b);
            var rh = ZeilenHoehe(b);
            if (p.X < NameBreite || p.Y < Kopf) return null;
            var col = (int)((p.X - NameBreite) / cw);
            var row = (int)((p.Y - Kopf) / rh);
            var zr = AppZustand.D.Zeitraster;
            if (row >= Elemente.Count || col >= Spalten) return null;
            return (row, col / zr.StundenProTag, col % zr.StundenProTag + 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var h = Treffer(e.Location);
            if (h is null) return;
            var el = Elemente[h.Value.Zeile];
            var p = AppZustand.Doc.Index.PlanVon(_f._typ, el.Id).FirstOrDefault(x => x.Tag == h.Value.Tag && x.Stunde == h.Value.Stunde);
            if (p is null) return;
            if (AppZustand.Doc.Index.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) AppZustand.StundeWaehlen(u, p, this);
            _drag = (h.Value.Zeile, p);
            DoDragDrop("uebersicht", DragDropEffects.Move);
        }

        protected override void OnDragOver(DragEventArgs e) => e.Effect = _drag.HasValue ? DragDropEffects.Move : DragDropEffects.None;

        protected override void OnDragDrop(DragEventArgs e)
        {
            var h = Treffer(PointToClient(new Point(e.X, e.Y)));
            if (h is null || _drag is null) return;
            var (zeile, p) = _drag.Value;
            _drag = null;
            var d = AppZustand.D;
            var idx = d.Stundenplan.IndexOf(p);
            if (idx < 0) return;
            if (_f._typ == ElementTyp.Raum && h.Value.Zeile != zeile && h.Value.Tag == p.Tag && h.Value.Stunde == p.Stunde)
            {
                // Raumverlegung bzw. -tausch
                var alt = Elemente[zeile].Id;
                var neu = Elemente[h.Value.Zeile].Id;
                AppZustand.Doc.Aendern("Raumverlegung", AenderungsArt.Stundenplan, dd =>
                {
                    var ps = dd.Stundenplan[idx];
                    var z = ps.Raeume.IndexOf(alt);
                    Planungsdienst.RaumZuordnen(dd, ps, Math.Max(0, z), neu, Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen);
                });
            }
            else if (h.Value.Zeile == zeile)
                AppZustand.Doc.Aendern("Stunde verschieben", AenderungsArt.Stundenplan, dd => Planungsdienst.Verschieben(dd, dd.Stundenplan[idx], h.Value.Tag, h.Value.Stunde, true, false));
            Invalidate();
        }
    }
}
