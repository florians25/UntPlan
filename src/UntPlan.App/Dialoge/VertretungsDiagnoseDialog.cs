using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vertretungs-Diagnose wie im Handbuch (Fenster „Diagnose“ aus dem Vertretungsfenster): Liste Vtr-Nr. | Vertreter | Befund
/// (Lehrerkollision, Raumkollision, Stundenfolge, Stunden pro Tag, Zeitwunsch) für den angezeigten Zeitraum; „Vertretungen prüfen“
/// baut die Vertretungsdaten des Zeitraums neu auf (Konsistenzprüfung nach Stundenplanänderungen). Doppelklick wählt die Vertretung.
/// </summary>
public sealed class VertretungsDiagnoseDialog : Form
{
    // Maße nach dem Handbuch-Bild (96 dpi): Fenster 279 × 269, Schaltfläche 142 × 28, Raster ab y = 38 mit Zeilen zu 15 px
    private readonly DataGridView _g = new() { Left = 1, Top = 38, Width = 277, Height = 230, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly DateOnly _von;
    private readonly DateOnly _bis;

    private VertretungsDiagnoseDialog(DateOnly von, DateOnly bis)
    {
        _von = von;
        _bis = bis;
        Text = "Diagnose";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(279, 269);
        var pruefen = new Button { Text = "Vertretungen prüfen", Left = 7, Top = 6, Width = 142, Height = 28 };
        var linie = new Label { Left = 0, Top = 36, Width = 279, Height = 2, BorderStyle = BorderStyle.Fixed3D, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        pruefen.Click += (_, _) => Pruefen();
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = false;
        _g.RowHeadersVisible = false;
        _g.ReadOnly = true;
        _g.AllowUserToAddRows = false;
        _g.BorderStyle = BorderStyle.None;
        _g.BackgroundColor = SystemColors.Control;
        _g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _g.ColumnHeadersHeight = 15;
        _g.RowTemplate.Height = Ui.ZeilenHoehe();
        foreach (var (n, k, b) in new[] { ("N", "Vtr-Nr.", 38), ("V", "Vertreter", 50), ("A", "", 77) }) _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is Vertretung v) AppZustand.VertretungWaehlen(v); };
        Controls.Add(pruefen);
        Controls.Add(linie);
        Controls.Add(_g);
        AppZustand.Doc.Geaendert += Geaendert;
        FormClosed += (_, _) => AppZustand.Doc.Geaendert -= Geaendert;
        Fuellen();
    }

    public static void Zeigen(IWin32Window owner, DateOnly von, DateOnly bis) => new VertretungsDiagnoseDialog(von, bis).Show(owner);

    private void Geaendert(AenderungsArt art)
    {
        if (!IsDisposed) Fuellen();
    }

    private void Fuellen()
    {
        _g.Rows.Clear();
        var ix = AppZustand.Doc.Index;
        foreach (var b in Vertretungsdiagnose.Pruefen(AppZustand.D, _von, _bis))
        {
            var r = _g.Rows.Add(b.Vertretung.Id, ix.K(ElementTyp.Lehrer, b.Vertretung.VertreterId), b.Art);
            _g.Rows[r].Tag = b.Vertretung;
        }
        if (_g.Rows.Count == 0) _g.Rows.Add("", "", "keine Abweichungen");
    }

    private void Pruefen()
    {
        var (von, bis) = (_von, _bis);
        AppZustand.Doc.Aendern("Vertretungen prüfen", AenderungsArt.Vertretung, d => Vertretungen.Aktualisieren(d, von, bis));
        Fuellen();
    }
}
