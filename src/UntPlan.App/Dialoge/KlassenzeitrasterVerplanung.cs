using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Registerkarte „Verplanung“ des Klassenzeitrasters (Handbuch Stundenplan 2.4.3.4): Eingabeblock „Letzte Stunde“ (&lt;Nein&gt;, &lt;Ja&gt;),
/// Eingabeblock „Verplanung“ (&lt;Doppelstunden&gt;, &lt;Einzelstunden&gt;, &lt;Egal&gt;), &lt;Kopieren&gt; und das Raster Stunden × Tage
/// („2“ = Doppelstunden, „1“ = Einzelstunden, „X“ = Unterricht darf nach dieser Stunde nicht enden, „*“ = keine Doppelstunde über
/// die nachfolgende Pause). Die Optimierung berücksichtigt diese Vorgaben.
/// </summary>
public sealed class KlassenzeitrasterVerplanung
{
    private static readonly Color Doppel = Color.FromArgb(200, 220, 255);
    private static readonly Color Einzel = Color.FromArgb(200, 255, 200);
    private static readonly Color Nein = Color.FromArgb(255, 160, 150);

    private readonly Schuldatei _d;
    private readonly Dictionary<int, Dictionary<int, KlassenzeitrasterArt>> _werte;
    private readonly DataGridView _raster = new() { Name = "Verplanungsraster" };
    private Klasse? _aktuell;

    public TabPage Seite { get; } = new("Verplanung") { BackColor = Ui.Kopf };

    public KlassenzeitrasterVerplanung(Schuldatei d, IWin32Window owner)
    {
        _d = d;
        _werte = d.Klassen.ToDictionary(k => k.Id, k => new Dictionary<int, KlassenzeitrasterArt>(k.Klassenzeitraster));
        var tage = Math.Max(1, d.Zeitraster.TageProWoche);
        var stunden = Math.Max(1, d.Zeitraster.StundenProTag);

        var letzte = new GroupBox { Text = "Letzte Stunde", Left = 10, Top = 6, Width = 110, Height = 84 };
        var verpl = new GroupBox { Text = "Verplanung", Left = 128, Top = 6, Width = 120, Height = 112 };
        Button Knopf(GroupBox g, string text, int y, Action klick)
        {
            var b = new Button { Text = text, Left = 10, Top = y, Width = g.Width - 20, Height = 24 };
            b.Click += (_, _) => klick();
            g.Controls.Add(b);
            return b;
        }
        Knopf(letzte, "Nein", 22, () => Setzen(a => a | KlassenzeitrasterArt.NichtLetzte));
        Knopf(letzte, "Ja", 50, () => Setzen(a => a & ~KlassenzeitrasterArt.NichtLetzte));
        Knopf(verpl, "Doppelstunden", 22, () => Setzen(a => (a & KlassenzeitrasterArt.NichtLetzte) | KlassenzeitrasterArt.Doppelstunden));
        Knopf(verpl, "Einzelstunden", 50, () => Setzen(a => (a & KlassenzeitrasterArt.NichtLetzte) | KlassenzeitrasterArt.Einzelstunden));
        Knopf(verpl, "Egal", 78, () => Setzen(a => a & KlassenzeitrasterArt.NichtLetzte));
        var kopieren = new Button { Text = "Kopieren", Left = 262, Top = 28, Width = 100, Height = 24 };
        kopieren.Click += (_, _) =>
        {
            if (_aktuell is null) return;
            var ziel = AuswahlDialog.Mehrere(owner, ElementTyp.Klasse, $"Klassenzeitraster von {_aktuell.Kurzname} kopieren nach", Array.Empty<int>());
            if (ziel is null) return;
            foreach (var k in ziel.Where(k => k.Id != _aktuell.Id && _werte.ContainsKey(k.Id)))
                _werte[k.Id] = new Dictionary<int, KlassenzeitrasterArt>(_werte[_aktuell.Id]);
        };
        Seite.Controls.Add(letzte);
        Seite.Controls.Add(verpl);
        Seite.Controls.Add(kopieren);
        Seite.Controls.Add(new Label { Text = "* = keine Doppelstunde über nachfolgende Pause", Left = 10, Top = 124, AutoSize = true });

        Ui.RasterGestalten(_raster, false);
        _raster.SetBounds(6, 144, 380, 160);
        _raster.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _raster.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _raster.MultiSelect = true;
        _raster.ReadOnly = true;
        _raster.RowHeadersWidth = 80;
        _raster.ColumnHeadersHeight = 22;
        for (var s = 1; s <= stunden; s++)
        {
            var c = _raster.Columns[_raster.Columns.Add("S" + s, s.ToString())];
            c.Width = 42;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        string Zeit(int s, bool ende) => d.Zeitraster.Zeit(s) is { } z ? (ende ? z.Ende : z.Beginn).ToString("H:mm") : "";
        _raster.Rows.Add(Enumerable.Range(1, stunden).Select(s => (object)Zeit(s, false)).ToArray());
        _raster.Rows.Add(Enumerable.Range(1, stunden).Select(s => (object)Zeit(s, true)).ToArray());
        for (var i = 0; i < 2; i++)
        {
            _raster.Rows[i].DefaultCellStyle.BackColor = _raster.Rows[i].DefaultCellStyle.SelectionBackColor = Color.White;
            _raster.Rows[i].DefaultCellStyle.SelectionForeColor = Color.Black;
        }
        for (var t = 0; t < tage; t++)
        {
            var r = _raster.Rows.Add();
            _raster.Rows[r].HeaderCell.Value = Planungsdienst.Tagname(d, t, true);
            _raster.Rows[r].Tag = t;
        }
        _raster.SelectionChanged += (_, _) => { foreach (DataGridViewCell c in _raster.SelectedCells) if (c.RowIndex < 2) c.Selected = false; };
        Seite.Controls.Add(_raster);
        Seite.Resize += (_, _) => _raster.SetBounds(6, 144, Math.Max(100, Seite.ClientSize.Width - 12), Math.Max(60, Seite.ClientSize.Height - 150));
    }

    /// <summary>Angezeigte Klasse wechseln.</summary>
    public void Klasse(Klasse? k)
    {
        _aktuell = k;
        Fuellen();
    }

    private void Setzen(Func<KlassenzeitrasterArt, KlassenzeitrasterArt> aendern)
    {
        if (_aktuell is null) return;
        var w = _werte[_aktuell.Id];
        foreach (DataGridViewCell c in _raster.SelectedCells)
        {
            if (c.OwningRow.Tag is not int t) continue;
            var key = t * 100 + c.ColumnIndex + 1;
            var neu = aendern(w.GetValueOrDefault(key));
            if (neu == KlassenzeitrasterArt.Keine) w.Remove(key); else w[key] = neu;
        }
        Fuellen();
    }

    private void Fuellen()
    {
        var stern = _d.Zeitraster.Pausen.Where(p => p.Stern).Select(p => p.NachStunde).ToHashSet();
        foreach (DataGridViewRow r in _raster.Rows)
        {
            if (r.Tag is not int t) continue;
            for (var s = 1; s <= _raster.Columns.Count; s++)
            {
                var a = _aktuell is null ? KlassenzeitrasterArt.Keine : _werte[_aktuell.Id].GetValueOrDefault(t * 100 + s);
                var zelle = r.Cells[s - 1];
                var text = a.HasFlag(KlassenzeitrasterArt.Doppelstunden) ? "2" : a.HasFlag(KlassenzeitrasterArt.Einzelstunden) ? "1" : "";
                if (text.Length > 0 && stern.Contains(s)) text = "*" + text;
                if (a.HasFlag(KlassenzeitrasterArt.NichtLetzte)) text = (text + " X").Trim();
                zelle.Value = text;
                zelle.Style.BackColor = a.HasFlag(KlassenzeitrasterArt.NichtLetzte) ? Nein
                    : a.HasFlag(KlassenzeitrasterArt.Doppelstunden) ? Doppel : a.HasFlag(KlassenzeitrasterArt.Einzelstunden) ? Einzel : Color.White;
                zelle.Style.SelectionBackColor = ControlPaint.Dark(zelle.Style.BackColor, 0.05f);
            }
        }
    }

    /// <summary>Eingaben in die Schuldatei übernehmen (innerhalb einer Änderung).</summary>
    public void Uebernehmen(Schuldatei x)
    {
        foreach (var k in x.Klassen)
            if (_werte.TryGetValue(k.Id, out var w)) k.Klassenzeitraster = new Dictionary<int, KlassenzeitrasterArt>(w);
    }
}
