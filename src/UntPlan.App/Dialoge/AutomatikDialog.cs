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
    // Maße wie im Handbuch-Bild (96 dpi): Fenster 348 × 266 ohne Schaltflächen unten, Rahmen 329 breit, Zeilen im Abstand von 19 Pixeln
    private readonly Label _vertretungen = new() { Left = 12, Top = 16, Width = 300, Height = 17 };
    private readonly Label _raum = new() { Left = 12, Top = 35, Width = 300, Height = 17 };
    private readonly Label _pause = new() { Left = 12, Top = 54, Width = 300, Height = 17 };
    private readonly ComboBox _methode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 10, Top = 111, Width = 329 };
    private readonly Button _start = new() { Left = 10, Top = 149, Width = 329, Height = 50 };
    private readonly Label _schritt = new() { Left = 10, Top = 206, Width = 329, Height = 18, Text = "Vertreter statt: Lehrer (Fach / Klasse(n))" };
    private readonly ProgressBar _fortschritt = new() { Left = 10, Top = 232, Width = 329, Height = 22, Style = ProgressBarStyle.Continuous };

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

    private VertretungsautomatikDialog(DateOnly von, DateOnly bis) : base("Vertretungsautomatik", 348, 266)
    {
        _von = von;
        _bis = bis;
        // wie im Vorbild ohne Schaltflächen unten: Schließen über das Kreuz der Titelleiste bzw. Esc
        Ok.Visible = false;
        Abbrechen.Visible = false;
        Abbrechen.Text = "Schließen";
        CancelButton = null;
        AcceptButton = null;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        var rahmen = new GroupBox { Text = "offene Vertretungen", Left = 10, Top = 10, Width = 329, Height = 75 };
        rahmen.Controls.AddRange(new Control[] { _vertretungen, _raum, _pause });
        Controls.Add(rahmen);
        Controls.Add(new Label { Text = "Auswahl der Methode", Left = 10, Top = 92, AutoSize = true });
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
        _schritt.Text = rest.Summe == 0 ? $"Fertig: {n} bearbeitet. Alle offenen Vertretungen sind erledigt." : $"Fertig: {n} bearbeitet, {rest.Summe} weiterhin offen.";
    }
}
