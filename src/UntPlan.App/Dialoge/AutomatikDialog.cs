using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vertretungsautomatik: Rahmen „offene Vertretungen“ mit Zählern, „Auswahl der Methode“, Schaltfläche
/// „Start Vertretungsautomatik“ und Fortschrittsanzeige „Vertreter statt: Lehrer (Fach / Klasse(n))“.
/// </summary>
public sealed class VertretungsautomatikDialog : Dialog
{
    private readonly DateOnly _von;
    private readonly DateOnly _bis;
    private readonly Label _vertretungen = new() { Left = 12, Top = 20, Width = 300, Height = 18 };
    private readonly Label _raum = new() { Left = 12, Top = 40, Width = 300, Height = 18 };
    private readonly Label _pause = new() { Left = 12, Top = 60, Width = 300, Height = 18 };
    private readonly ComboBox _methode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 128, Width = 326 };
    private readonly Button _start = new() { Left = 12, Top = 166, Width = 326, Height = 40 };
    private readonly Label _schritt = new() { Left = 12, Top = 216, Width = 326, Height = 18, Text = "Vertreter statt: Lehrer (Fach / Klasse(n))" };
    private readonly ProgressBar _fortschritt = new() { Left = 12, Top = 238, Width = 326, Height = 18, Style = ProgressBarStyle.Continuous };

    public static void Zeigen(IWin32Window owner, DateOnly von, DateOnly bis)
    {
        using var f = new VertretungsautomatikDialog(von, bis);
        f.ShowDialog(owner);
    }

    /// <summary>Nicht modal anzeigen (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, DateOnly von, DateOnly bis)
    {
        var f = new VertretungsautomatikDialog(von, bis);
        f.Show(owner);
        return f;
    }

    private VertretungsautomatikDialog(DateOnly von, DateOnly bis) : base("Vertretungsautomatik", 350, 300)
    {
        _von = von;
        _bis = bis;
        Ok.Visible = false;
        Abbrechen.Text = "Schließen";
        var rahmen = new GroupBox { Text = "offene Vertretungen", Left = 8, Top = 4, Width = 334, Height = 86 };
        rahmen.Controls.AddRange(new Control[] { _vertretungen, _raum, _pause });
        Controls.Add(rahmen);
        Controls.Add(new Label { Text = "Auswahl der Methode", Left = 12, Top = 106, AutoSize = true });
        foreach (var (_, text) in Vertretungsautomatik.Methoden) _methode.Items.Add(text);
        _methode.SelectedIndex = 0;
        _methode.SelectedIndexChanged += (_, _) => StartText();
        StartText();
        _start.Click += (_, _) => Starten();
        Controls.AddRange(new Control[] { _methode, _start, _schritt, _fortschritt });
        Zaehlen();
    }

    private AutomatikMethode Methode => Vertretungsautomatik.Methoden[Math.Max(0, _methode.SelectedIndex)].Methode;

    private void StartText() => _start.Text = "Start Vertretungsautomatik\n" + _methode.SelectedItem;

    private void Zaehlen()
    {
        var z = Vertretungsautomatik.Zaehlen(AppZustand.D, _von, _bis);
        _vertretungen.Text = $"{z.Vertretungen} Vertretungen";
        _raum.Text = $"{z.Raumvertretungen} Raumvertretungen";
        _pause.Text = $"{z.Pausenaufsichten} Pausenaufs.-Vertretungen";
    }

    private void Starten()
    {
        var methode = Methode;
        var n = 0;
        _start.Enabled = false;
        try
        {
            using (new WarteCursor())
                AppZustand.Doc.Aendern("Vertretungsautomatik", AenderungsArt.Vertretung, d =>
                    n = Vertretungsautomatik.Ausfuehren(d, _von, _bis, methode, (v, i, gesamt) =>
                    {
                        _schritt.Text = Vertretungsautomatik.Beschreibung(d, v);
                        _fortschritt.Maximum = Math.Max(1, gesamt);
                        _fortschritt.Value = Math.Min(gesamt, i);
                        _schritt.Refresh();
                        _fortschritt.Refresh();
                    }, AppZustand.Benutzer));
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        finally { _start.Enabled = true; }
        _fortschritt.Value = _fortschritt.Maximum;
        Zaehlen();
        var rest = Vertretungsautomatik.Zaehlen(AppZustand.D, _von, _bis);
        _schritt.Text = rest.Summe == 0 ? $"Fertig! {n} bearbeitet – alle offenen Vertretungen sind erledigt." : $"Fertig: {n} bearbeitet, {rest.Summe} weiterhin offen.";
    }
}
