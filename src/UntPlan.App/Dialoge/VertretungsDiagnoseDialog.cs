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
    private readonly DataGridView _g = new() { Left = 8, Top = 44, Width = 300, Height = 300, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly DateOnly _von;
    private readonly DateOnly _bis;

    private VertretungsDiagnoseDialog(DateOnly von, DateOnly bis)
    {
        _von = von;
        _bis = bis;
        Text = "Diagnose";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(316, 352);
        var pruefen = new Button { Text = "Vertretungen prüfen", Left = 20, Top = 10, Width = 130, Height = 26 };
        pruefen.Click += (_, _) => Pruefen();
        Ui.RasterGestalten(_g, false);
        _g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _g.MultiSelect = false;
        _g.RowHeadersVisible = false;
        foreach (var (n, k, b) in new[] { ("N", "Vtr-Nr.", 50), ("V", "Vertreter", 70), ("A", "", 150) }) _g.Columns[_g.Columns.Add(n, k)].Width = b;
        _g.Columns["A"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _g.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _g.Rows[e.RowIndex].Tag is Vertretung v) AppZustand.VertretungWaehlen(v); };
        Controls.Add(pruefen);
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
