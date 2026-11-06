using UntPlan.Core;

namespace UntPlan.App;

/// <summary>
/// Gemeinsames Bedienverhalten aller Masken (Runde 3 B): einheitliche Rückfrage vor dem Löschen, Rückgängig/Wiederherstellen mit
/// Hinweis in der Statuszeile, Hinweis auf ungültige Eingaben (Statuszeile und Hinweisfenster an der Zelle bzw. am Feld, ohne Meldungsfenster).
/// </summary>
public static partial class Ui
{
    /// <summary>Erste Zeile jeder Rückfrage vor dem Löschen – in allen Masken gleich (auch bei Mehrfachauswahl).</summary>
    public const string LoeschenFrageText = "Wollen Sie die markierten Daten wirklich löschen?";

    /// <summary>Titel der Rückfrage vor dem Löschen.</summary>
    public const string LoeschenTitel = "UntPlan - Löschen";

    /// <summary>
    /// Einheitliche Rückfrage vor dem Löschen: „Wollen Sie die markierten Daten wirklich löschen?“, darunter was gelöscht wird
    /// („1 Absenz (HUGO)“ bzw. „3 Absenzen“) und ein optionaler Zusatz (Folgen des Löschens). Nichts wird ohne diese Rückfrage gelöscht.
    /// </summary>
    /// <param name="anzahl">Anzahl der markierten Datensätze (0 = nichts zu löschen, keine Rückfrage).</param>
    /// <param name="einzahl">Bezeichnung in der Einzahl (z. B. „Absenz“).</param>
    /// <param name="mehrzahl">Bezeichnung in der Mehrzahl (z. B. „Absenzen“).</param>
    /// <param name="name">Name des einzelnen Datensatzes (nur bei einem Datensatz angezeigt).</param>
    /// <param name="zusatz">Hinweis auf Folgen (z. B. „Daraus entstandene Vertretungen werden entfernt.“).</param>
    public static bool LoeschenBestaetigen(int anzahl, string einzahl, string mehrzahl, string? name = null, string? zusatz = null, IWin32Window? owner = null)
    {
        if (anzahl <= 0) return false;
        var was = anzahl == 1 ? $"1 {einzahl}{(string.IsNullOrWhiteSpace(name) ? "" : $" ({name})")}" : $"{anzahl} {mehrzahl}";
        var text = $"{LoeschenFrageText}\n\n{was}" + (string.IsNullOrWhiteSpace(zusatz) ? "" : $"\n\n{zusatz}");
        return Frage(text, LoeschenTitel, owner);
    }

    /// <summary>Letzter Hinweis in der Statuszeile (Selbsttest).</summary>
    public static string LetzterHinweis { get; private set; } = "";

    /// <summary>Kurze Rückmeldung links in der Statuszeile des Hauptfensters.</summary>
    public static void Statushinweis(string text)
    {
        LetzterHinweis = text;
        try { AppZustand.Haupt?.StatusMelden(text); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Rückgängig (Strg+Z) bzw. Wiederherstellen (Strg+Y) für die Schuldatei – in jeder Maske gleich, mit Hinweis in der Statuszeile.
    /// Eine laufende Zelleingabe wird vorher verworfen (sie gehört nicht zur Historie).
    /// </summary>
    public static void Rueckgaengig(bool wiederherstellen = false)
    {
        var doc = AppZustand.Doc;
        try
        {
            if (!wiederherstellen)
            {
                if (!doc.KannRueckgaengig) { Statushinweis("Es gibt keine Änderung, die rückgängig gemacht werden kann."); return; }
                var text = doc.RueckgaengigText;
                doc.Rueckgaengig();
                Statushinweis($"Rückgängig: {text}");
            }
            else
            {
                if (!doc.KannWiederholen) { Statushinweis("Es gibt keine Änderung, die wiederhergestellt werden kann."); return; }
                var text = doc.WiederholenText;
                doc.Wiederholen();
                Statushinweis($"Wiederhergestellt: {text}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            Fehler(ex);
        }
    }

    /// <summary>Letzter Hinweis auf eine ungültige Eingabe (Tabellen, Felder und Auswahllisten; für den Selbsttest).</summary>
    public static string LetzterEingabeHinweis { get; private set; } = "";

    /// <summary>Zahl der bisher gemeldeten ungültigen Eingaben (der Selbsttest erkennt daran einen neuen Hinweis, auch bei gleichem Text).</summary>
    public static int EingabeHinweisZaehler { get; private set; }

    private static EingabeHinweisFenster? _hinweisFenster;

    /// <summary>
    /// Der eine gemeinsame Hinweis auf eine ungültige Eingabe – für Tabellenzellen, Felder und Auswahllisten gleich: Text in der
    /// Statuszeile und ein kleines gelbes Hinweisfenster unter der Zelle bzw. dem Feld (kein Meldungsfenster). Die Eingabe bzw. der
    /// bisherige Wert bleibt stehen und kann korrigiert oder mit Esc verworfen werden.
    /// </summary>
    public static void EingabeHinweis(Control? ort, string text)
    {
        LetzterEingabeHinweis = text;
        EingabeHinweisZaehler++;
        Statushinweis(text);
        if (ort is null || ort.IsDisposed || !ort.IsHandleCreated || !ort.Visible) return;
        try
        {
            // Anker in Bildschirmkoordinaten: aktuelle Zelle einer Tabelle, sonst das Feld selbst
            var anker = new Rectangle(ort.PointToScreen(Point.Empty), ort.Size);
            if (ort is DataGridView g && g.CurrentCell is { } c && c.RowIndex >= 0 && c.ColumnIndex >= 0)
            {
                var r = g.GetCellDisplayRectangle(c.ColumnIndex, c.RowIndex, false);
                if (!r.IsEmpty) anker = g.RectangleToScreen(r);
            }
            // eigenes kleines Fenster ohne Aktivierung statt ToolTip: ein ToolTip hält sich am Steuerelement fest und stürzt mit
            // ObjectDisposedException ab, wenn die Tabelle bzw. ihr Eingabefeld verworfen wird, bevor er sich ausblendet
            if (_hinweisFenster is { IsDisposed: false }) _hinweisFenster.Close();
            _hinweisFenster = new EingabeHinweisFenster(text);
            var bereich = Screen.FromRectangle(anker).WorkingArea;
            var x = Math.Clamp(anker.Left, bereich.Left, Math.Max(bereich.Left, bereich.Right - _hinweisFenster.Width));
            var y = anker.Bottom + 2 + _hinweisFenster.Height > bereich.Bottom ? anker.Top - _hinweisFenster.Height - 2 : anker.Bottom + 2;
            _hinweisFenster.Location = new Point(x, y);
            _hinweisFenster.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Kurzer Hinweis (gelb wie ein Tooltip), schließt sich nach 2,5 Sekunden selbst und nimmt nie den Fokus.</summary>
    private sealed class EingabeHinweisFenster : Form
    {
        private readonly System.Windows.Forms.Timer _zeit = new() { Interval = 2500 };

        public EingabeHinweisFenster(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(255, 255, 225);
            Padding = new Padding(1);
            var l = new Label
            {
                Text = "⚠ " + text, AutoSize = true, Font = ListenSchrift, ForeColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(6, 4, 6, 4), BackColor = BackColor,
            };
            Controls.Add(l);
            ClientSize = l.PreferredSize + new Size(2, 2);
            l.Location = new Point(1, 1);
            Paint += (_, e) => e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Width - 1, Height - 1);
            _zeit.Tick += (_, _) => Close();
            _zeit.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _zeit.Dispose();
            base.Dispose(disposing);
        }
    }
}
