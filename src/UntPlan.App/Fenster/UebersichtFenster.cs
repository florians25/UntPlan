using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Übersichtsplan (z. B. "Lehrer Übersicht quer"): Elemente als Zeilen, Tage × Stunden als Spalten.
/// Im Raum-Übersichtsplan können Raumverlegungen bzw. -täusche per Drag &amp; Drop durchgeführt werden.
/// </summary>
public sealed partial class UebersichtFenster : MdiFenster, IDruckbar
{
    private readonly ElementTyp _typ;
    private readonly Raster _raster;

    public UebersichtFenster(ElementTyp typ)
    {
        _typ = typ;
        Text = $"{Ui.TypName(typ, true)} Übersicht quer";
        HilfeThema = "Stundenpläne";
        Size = new Size(1100, 520);
        Positionieren();
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        // Druck-Details der Übersichtspläne (Anzahl Zeilen / Spalten pro Blatt, Zwischenüberschriften)
        ts.Items.Add(Knopf("Einstellungen (Druck)", "einstellungen", () => { using var f = Druck.UebersichtDruck.DetailsForm(Druck.UebersichtDruck.Details(_typ)); f.ShowDialog(this); }));
        FilterLeisteAufbauen(ts);
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

    /// <summary>Druck mit Seitenaufteilung laut Druck-Details (Zeilen / Spalten pro Blatt, Stundenplan-Blöcke, Zwischenüberschriften).</summary>
    public void Drucken(bool vorschau) => Druck.UebersichtDruck.Drucken(this, _typ, Text, vorschau, _auswahl is null ? null : Angezeigt(), Beschriftung);

    private sealed class Raster : Control
    {
        private readonly UebersichtFenster _f;
        private (int Zeile, Planstunde P)? _drag;
        private int NameBreite => _f.ZusatzBeschriftung ? 96 : 60;
        // Vorbild „Leh20“ (Handbuch-Bild Übersichtsplan im Querformat): Tageszeile 19 px, Stundenzeile 21 px, Zeilen 21 px, Stunden 22 px breit
        private const int TagZeile = 19;
        private const int Kopf = 40;
        private const int Zeile = 21;
        private const int MindestSpalte = 22;

        public Raster(UebersichtFenster f)
        {
            _f = f;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private List<Element> Elemente => _f.Angezeigt();
        private int Spalten => AppZustand.D.Zeitraster.TageProWoche * AppZustand.D.Zeitraster.StundenProTag;

        public void GroesseAnpassen(Size client)
        {
            var hoehe = Kopf + Elemente.Count * Zeile + 2;
            Size = new Size(Math.Max(client.Width - 2, NameBreite + Spalten * MindestSpalte + 2), Math.Max(client.Height - 2, hoehe));
            Invalidate();
        }

        private float ZellBreite(Rectangle b) => (b.Width - NameBreite - 1) / (float)Spalten;
        private float ZeilenHoehe(Rectangle b) => Math.Max(14, Math.Min(Zeile, (b.Height - Kopf - 1) / (float)Math.Max(1, Elemente.Count)));

        protected override void OnPaint(PaintEventArgs e) => Zeichnen(e.Graphics, ClientRectangle);

        /// <summary>
        /// Übersichtsplan wie im Vorbild: Kopf und Namensspalte hellblau, schwarze 1-px-Linien, 2 px zwischen den Tagen, Namen fett,
        /// Stunden in der Farbe des Elements (Lehrer-/Raumübersicht: Klasse, Klassenübersicht: Fach), Kopplungen mit „.“, Fixierung mit „*“.
        /// </summary>
        public void Zeichnen(Graphics g, Rectangle b)
        {
            var d = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            var zr = d.Zeitraster;
            var cw = ZellBreite(b);
            var rh = ZeilenHoehe(b);
            var elemente = Elemente;
            var unten = b.Y + Kopf + (int)Math.Round(rh * elemente.Count);
            var familie = (AppZustand.D.Formate.FirstOrDefault(x => x.Typ == _f._typ) ?? new StundenplanFormat()).SchriftartWirksam;
            using var normal = new Font(familie, 9f);
            using var fett = new Font(familie, 9f, FontStyle.Bold);
            using var hell = new SolidBrush(Steuerelemente.PlanAnsicht.Hellblau);
            g.FillRectangle(Brushes.White, b);
            g.FillRectangle(hell, b.X, b.Y, b.Width, Kopf);
            g.FillRectangle(hell, b.X, b.Y, NameBreite, unten - b.Y);
            int X(int spalte) => b.X + NameBreite + (int)Math.Round(spalte * cw);
            int Y(int zeile) => b.Y + Kopf + (int)Math.Round(zeile * rh);
            const TextFormatFlags mitte = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            for (var t = 0; t < zr.TageProWoche; t++)
            {
                var x0 = X(t * zr.StundenProTag);
                var x1 = X((t + 1) * zr.StundenProTag);
                TextRenderer.DrawText(g, Planungsdienst.Tagname(d, t, true), normal, new Rectangle(x0, b.Y, x1 - x0, TagZeile), Color.Black, mitte);
                for (var s = 1; s <= zr.StundenProTag; s++)
                {
                    var sx = X(t * zr.StundenProTag + s - 1);
                    TextRenderer.DrawText(g, zr.StundenLabel(s), normal, new Rectangle(sx, b.Y + TagZeile, X(t * zr.StundenProTag + s) - sx, Kopf - TagZeile), Color.Black, mitte);
                }
            }
            for (var i = 0; i < elemente.Count; i++)
            {
                var y0 = Y(i);
                var y1 = Y(i + 1);
                TextRenderer.DrawText(g, _f.Beschriftung(elemente[i]), fett, new Rectangle(b.X + 3, y0, NameBreite - 5, y1 - y0), Color.Black,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                foreach (var p in ix.PlanVon(_f._typ, elemente[i].Id))
                {
                    if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
                    var spalte = p.Tag * zr.StundenProTag + p.Stunde - 1;
                    var r = new Rectangle(X(spalte) + 1, y0 + 1, X(spalte + 1) - X(spalte) - 1, y1 - y0 - 1);
                    Element? farbElement = _f._typ == ElementTyp.Klasse
                        ? (u.HauptfachId is int fid ? ix.Faecher.GetValueOrDefault(fid) : null)
                        : u.KlassenIds.FirstOrDefault() is int kid and > 0 ? ix.Klassen.GetValueOrDefault(kid) : null;
                    if (farbElement is null or { Hintergrund: 0 } && u.HauptfachId is int fid2) farbElement = ix.Faecher.GetValueOrDefault(fid2);
                    var farbe = Ui.Farbe(farbElement?.Hintergrund ?? 0, Steuerelemente.PlanAnsicht.Hellblau);
                    var schrift = farbElement is { Vordergrund: not 0 } ? Ui.Farbe(farbElement.Vordergrund, Color.Black) : Ui.Textfarbe(farbe);
                    using (var br = new SolidBrush(farbe)) g.FillRectangle(br, r);
                    var text = _f._typ switch
                    {
                        ElementTyp.Lehrer => ix.KKlassen(u.KlassenIds),
                        ElementTyp.Klasse => string.Join(",", u.LehrerIds.Select(ix.KLehrer)),
                        _ => ix.KKlassen(u.KlassenIds),
                    };
                    TextRenderer.DrawText(g, (p.Fixiert ? "*" : "") + text + (u.Zeilen.Count > 1 ? "." : ""), normal, r, schrift, mitte);
                }
            }
            // Raster: schwarze Linien, 2 px zwischen den Tagen und unter dem Kopf
            using var linie = new Pen(Color.Black);
            for (var c = 0; c <= Spalten; c++)
            {
                var x = X(c);
                var tagGrenze = c % zr.StundenProTag == 0;
                g.DrawLine(linie, x, b.Y + (tagGrenze ? 0 : TagZeile), x, unten);
                if (tagGrenze && c > 0 && c < Spalten) g.DrawLine(linie, x + 1, b.Y, x + 1, unten);
            }
            g.DrawLine(linie, b.X, b.Y, b.X, unten);
            g.DrawLine(linie, b.X + NameBreite, b.Y + TagZeile, b.Right - 1, b.Y + TagZeile);
            g.DrawLine(linie, b.X, b.Y, b.Right - 1, b.Y);
            for (var i = 0; i <= elemente.Count; i++) g.DrawLine(linie, b.X, Y(i), X(Spalten), Y(i));
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
            // Ziehen erst nach echter Mausbewegung (ein Klick wählt nur die Stunde, verschiebt nie)
            _drag = null;
            _ziehStart = e.Button == MouseButtons.Left ? (h.Value.Zeile, p, Cursor.Position) : null;
        }

        private (int Zeile, Planstunde P, Point Start)? _ziehStart;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_ziehStart is not { } z) return;
            if ((e.Button & MouseButtons.Left) == 0) { _ziehStart = null; return; }
            if (!Steuerelemente.Ziehschwelle.Ueberschritten(z.Start, Cursor.Position)) return;
            _ziehStart = null;
            _drag = (z.Zeile, z.P);
            if (DoDragDrop("uebersicht", DragDropEffects.Move) == DragDropEffects.None) _drag = null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _ziehStart = null;
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
