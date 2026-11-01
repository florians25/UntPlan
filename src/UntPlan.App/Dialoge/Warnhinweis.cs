namespace UntPlan.App.Dialoge;

/// <summary>
/// Kleiner Warnhinweis wie im Vorbild beim Planen per Maus: Fenster „Warnung“ direkt neben der Maus (nicht in der Bildschirmmitte),
/// kurzer Text, Ja/Nein bzw. OK und ein Kästchen, mit dem gleichartige Meldungen (für diese Sitzung) bzw. diese Meldung (dauerhaft)
/// ausgeblendet werden. Ausgeblendete Meldungen gelten als bestätigt (Ja/OK).
/// </summary>
public static class Warnhinweis
{
    /// <summary>Für diese Sitzung ausgeblendete Meldungen („Weitere gleichartige Meldungen nicht mehr anzeigen“).</summary>
    private static readonly HashSet<string> Sitzung = new();

    /// <summary>Frage mit Ja/Nein; true = Ja (auch wenn die Meldung ausgeblendet ist).</summary>
    public static bool Frage(IWin32Window? owner, string schluessel, string text, bool dauerhaft = false) =>
        Zeigen(owner, schluessel, text, true, dauerhaft);

    /// <summary>Reiner Hinweis mit OK.</summary>
    public static void Hinweis(IWin32Window? owner, string schluessel, string text, bool dauerhaft = false) =>
        Zeigen(owner, schluessel, text, false, dauerhaft);

    /// <summary>Fenster wie im Vorbild neben der Maus öffnen (ganz im Arbeitsbereich des Bildschirms).</summary>
    public static void NebenDerMaus(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        var pos = Cursor.Position;
        var bereich = Screen.FromPoint(pos).WorkingArea;
        var x = Math.Min(pos.X + 12, bereich.Right - f.Width);
        var y = Math.Min(pos.Y + 12, bereich.Bottom - f.Height);
        f.Location = new Point(Math.Max(bereich.Left, x), Math.Max(bereich.Top, y));
    }

    /// <summary>Ist die Meldung ausgeblendet (Sitzung bzw. dauerhaft)?</summary>
    public static bool Ausgeblendet(string schluessel) =>
        Sitzung.Contains(schluessel) || AppZustand.Einstellungen.AusgeblendeteMeldungen.Contains(schluessel);

    /// <summary>Selbsttest: zuletzt gezeigter Text.</summary>
    internal static string? Zuletzt { get; private set; }

    private static bool Zeigen(IWin32Window? owner, string schluessel, string text, bool jaNein, bool dauerhaft)
    {
        if (Ausgeblendet(schluessel)) return true;
        Zuletzt = text;
        // Selbsttest: die Hooks beantworten ohne modales Fenster
        if (jaNein && Ui.FrageAntwort is { } frage) return frage(text, "Warnung");
        if (!jaNein && Ui.InfoAntwort is { } info) { info(text, "Warnung"); return true; }
        using var f = new Form
        {
            Text = "Warnung", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, BackColor = Ui.BlauSehrHell, Font = Ui.Schrift, AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(340, 150), KeyPreview = true,
        };
        var lbl = new Label { Text = text.Replace("\n", Environment.NewLine), Left = 10, Top = 10, Width = 320, Height = 66, AutoEllipsis = true };
        var kb = new CheckBox
        {
            Text = dauerhaft ? "Diese Meldung dauerhaft nicht mehr anzeigen." : "Weitere gleichartige Meldungen nicht mehr anzeigen.",
            Left = 10, Top = 84, Width = 320, Height = 22,
        };
        f.Controls.Add(lbl);
        f.Controls.Add(kb);
        if (jaNein)
        {
            var ja = new Button { Text = "Ja", DialogResult = DialogResult.Yes, Left = 60, Top = 114, Width = 90, Height = 26 };
            var nein = new Button { Text = "Nein", DialogResult = DialogResult.No, Left = 190, Top = 114, Width = 90, Height = 26 };
            f.Controls.Add(ja);
            f.Controls.Add(nein);
            f.AcceptButton = ja;
            f.CancelButton = nein;
        }
        else
        {
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 125, Top = 114, Width = 90, Height = 26 };
            f.Controls.Add(ok);
            f.AcceptButton = ok;
            f.CancelButton = ok;
        }
        NebenDerMaus(f);
        var antwort = owner is null ? f.ShowDialog() : f.ShowDialog(owner);
        var ergebnis = antwort is DialogResult.Yes or DialogResult.OK;
        if (kb.Checked && ergebnis)
        {
            if (dauerhaft)
            {
                if (!AppZustand.Einstellungen.AusgeblendeteMeldungen.Contains(schluessel)) AppZustand.Einstellungen.AusgeblendeteMeldungen.Add(schluessel);
                try { AppZustand.Einstellungen.Speichern(); } catch (IOException) { }
            }
            else Sitzung.Add(schluessel);
        }
        return ergebnis;
    }
}
