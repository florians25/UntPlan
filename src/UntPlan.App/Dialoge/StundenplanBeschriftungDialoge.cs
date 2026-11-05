using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>Einstellungen der Spaltenbeschriftung (Vorbild „Spaltenbeschriftung Wochentage - Überschrift“).</summary>
public sealed record SpaltenBeschriftungWerte(bool Fett, bool Kursiv, bool Unterstrichen, int Prozent, bool TagUndDatum, bool MitJahr, int Stellen)
{
    public static SpaltenBeschriftungWerte Aus(StundenplanFormat f) =>
        new(f.SpaltenFett, f.SpaltenKursiv, f.SpaltenUnterstrichen, f.SpaltenSchriftProzent, f.SpaltenTagUndDatum, f.SpaltenDatumMitJahr, f.SpaltenStellen);

    public void In(StundenplanFormat f) =>
        (f.SpaltenFett, f.SpaltenKursiv, f.SpaltenUnterstrichen, f.SpaltenSchriftProzent, f.SpaltenTagUndDatum, f.SpaltenDatumMitJahr, f.SpaltenStellen) =
        (Fett, Kursiv, Unterstrichen, Prozent, TagUndDatum, MitJahr, Stellen);
}

/// <summary>Einstellungen der Zeilenbeschriftung (Vorbild „Zeilenbeschriftung Stunden - Überschrift“).</summary>
public sealed record ZeilenBeschriftungWerte(ZeilenBeschriftung Art, bool NurBeginn, bool Fett, bool Kursiv, bool Unterstrichen, int Prozent)
{
    public static ZeilenBeschriftungWerte Aus(StundenplanFormat f) =>
        new(f.ZeilenArtWirksam, f.ZeilenNurBeginn, f.ZeilenFett, f.ZeilenKursiv, f.ZeilenUnterstrichen, f.ZeilenSchriftProzent);

    public void In(StundenplanFormat f)
    {
        f.ZeitenAnzeigen = Art != ZeilenBeschriftung.Nummer;
        if (Art != ZeilenBeschriftung.Nummer) f.ZeilenArt = Art;
        (f.ZeilenNurBeginn, f.ZeilenFett, f.ZeilenKursiv, f.ZeilenUnterstrichen, f.ZeilenSchriftProzent) = (NurBeginn, Fett, Kursiv, Unterstrichen, Prozent);
    }
}

/// <summary>
/// Editoren der Spalten- und Zeilenbeschriftung wie im Vorbild: Schaltflächen B / K / U, Schieberegler „Schriftgröße in %“,
/// Optionen der Beschriftung und eine Vorschau des Überschriftsfeldes; Ok, Abbrechen, Übernahme.
/// </summary>
public static class BeschriftungDialoge
{
    private static (CheckBox B, CheckBox K, CheckBox U) Stilknoepfe(Control d, int x, int y, bool b, bool k, bool u)
    {
        CheckBox Knopf(string t, FontStyle s, int i, bool wert) => new()
        {
            Text = t, Appearance = Appearance.Button, Checked = wert, Left = x + i * 26, Top = y, Width = 24, Height = 22,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Ui.Schrift.FontFamily, 8.5f, s),
        };
        var kb = Knopf("B", FontStyle.Bold, 0, b);
        var kk = Knopf("K", FontStyle.Italic, 1, k);
        var ku = Knopf("U", FontStyle.Underline, 2, u);
        d.Controls.AddRange(new Control[] { kb, kk, ku });
        return (kb, kk, ku);
    }

    private static TrackBar Regler(Control d, string text, int x, int y, int min, int max, int wert, out Label zahl)
    {
        zahl = new Label { Left = x, Top = y, Width = 40, Text = wert.ToString() };
        d.Controls.Add(zahl);
        d.Controls.Add(new Label { Text = text, Left = x + 90, Top = y, AutoSize = true });
        var t = new TrackBar { Left = x, Top = y + 16, Width = 230, Minimum = min, Maximum = max, Value = Math.Clamp(wert, min, max), TickStyle = TickStyle.None, AutoSize = false, Height = 26 };
        var z = zahl;
        t.ValueChanged += (_, _) => z.Text = t.Value.ToString();
        d.Controls.Add(t);
        return t;
    }

    private static Panel Vorschaufeld(Dialog d, int top)
    {
        // Rasterfläche wie im Vorbild-Editor
        var flaeche = new Panel { Left = 8, Top = top, Width = d.ClientSize.Width - 16, Height = d.ClientSize.Height - top - 44, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(236, 242, 248) };
        flaeche.Paint += (_, e) =>
        {
            using var p = new Pen(Color.FromArgb(210, 220, 232));
            for (var x = 0; x < flaeche.Width; x += 8) e.Graphics.DrawLine(p, x, 0, x, flaeche.Height);
            for (var y = 0; y < flaeche.Height; y += 8) e.Graphics.DrawLine(p, 0, y, flaeche.Width, y);
        };
        d.Controls.Add(flaeche);
        return flaeche;
    }

    private static void Uebernahme(Dialog d, Action uebernehmen)
    {
        d.Ok.Text = "Ok";
        var ueb = new Button { Text = "Übernahme", Width = 88, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Location = new Point(d.ClientSize.Width - 96, d.ClientSize.Height - 36) };
        d.Ok.Left -= 92;
        d.Abbrechen.Left -= 92;
        ueb.Click += (_, _) => uebernehmen();
        d.Controls.Add(ueb);
    }

    /// <summary>Spaltenbeschriftung bearbeiten; <paramref name="uebernahme"/> wird bei „Übernahme“ mit dem aktuellen Stand aufgerufen.</summary>
    public static SpaltenBeschriftungWerte? Spalten(IWin32Window owner, SpaltenBeschriftungWerte w, Action<SpaltenBeschriftungWerte>? uebernahme = null)
    {
        using var d = SpaltenErstellen(w, out var lesen);
        Uebernahme(d, () => uebernahme?.Invoke(lesen()));
        return d.ShowDialog(owner) == DialogResult.OK ? lesen() : null;
    }

    internal static Dialog SpaltenErstellen(SpaltenBeschriftungWerte w, out Func<SpaltenBeschriftungWerte> lesen)
    {
        var d = new Dialog("Spaltenbeschriftung Wochentage - Überschrift", 560, 330);
        var (b, k, u) = Stilknoepfe(d, 10, 10, w.Fett, w.Kursiv, w.Unterstrichen);
        var datum = new CheckBox { Text = "Tag + Datum für Wochenplan", Left = 10, Top = 40, Width = 220, Checked = w.TagUndDatum };
        var jahr = new CheckBox { Text = "Datum mit Jahreszahl", Left = 10, Top = 62, Width = 220, Checked = w.MitJahr, Enabled = w.TagUndDatum };
        datum.CheckedChanged += (_, _) => jahr.Enabled = datum.Checked;
        d.Controls.AddRange(new Control[] { datum, jahr });
        var prozent = Regler(d, "Schriftgröße in %", 250, 8, 50, 300, w.Prozent, out _);
        var stellen = Regler(d, "Stellen je Wochentag maximal (1-20)", 250, 54, 0, 20, w.Stellen, out var stellenZahl);
        void StellenText() => stellenZahl.Text = stellen.Value == 0 ? "auto" : stellen.Value.ToString();
        stellen.ValueChanged += (_, _) => StellenText();
        StellenText();
        var flaeche = Vorschaufeld(d, 104);
        lesen = () => new SpaltenBeschriftungWerte(b.Checked, k.Checked, u.Checked, prozent.Value, datum.Checked, jahr.Checked, stellen.Value);
        var l = lesen;
        flaeche.Paint += (_, e) =>
        {
            var x = l();
            var f = new StundenplanFormat { SpaltenStellen = x.Stellen, SpaltenTagUndDatum = x.TagUndDatum, SpaltenDatumMitJahr = x.MitJahr };
            var text = f.SpaltenText("Montag", DateOnly.FromDateTime(DateTime.Today));
            var stil = (x.Fett ? FontStyle.Bold : 0) | (x.Kursiv ? FontStyle.Italic : 0) | (x.Unterstrichen ? FontStyle.Underline : 0);
            using var schrift = new Font(Ui.Schrift.FontFamily, 14f * x.Prozent / 100f, stil);
            var r = new Rectangle(24, 24, 200, 64);
            e.Graphics.FillRectangle(Brushes.White, r);
            e.Graphics.DrawRectangle(Pens.Black, r);
            TextRenderer.DrawText(e.Graphics, text, schrift, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        foreach (var c in new[] { b, k, u, datum, jahr }) c.CheckedChanged += (_, _) => flaeche.Invalidate();
        foreach (var t in new[] { prozent, stellen }) t.ValueChanged += (_, _) => flaeche.Invalidate();
        return d;
    }

    private static readonly string[] Arten = { "Stunden-Nummer", "Zeit und Nr.", "Nur Zeit", "Stundenbezeichnung" };

    /// <summary>Zeilenbeschriftung bearbeiten; <paramref name="uebernahme"/> wird bei „Übernahme“ mit dem aktuellen Stand aufgerufen.</summary>
    public static ZeilenBeschriftungWerte? Zeilen(IWin32Window owner, ZeilenBeschriftungWerte w, Action<ZeilenBeschriftungWerte>? uebernahme = null)
    {
        using var d = ZeilenErstellen(w, out var lesen);
        Uebernahme(d, () => uebernahme?.Invoke(lesen()));
        return d.ShowDialog(owner) == DialogResult.OK ? lesen() : null;
    }

    internal static Dialog ZeilenErstellen(ZeilenBeschriftungWerte w, out Func<ZeilenBeschriftungWerte> lesen)
    {
        var d = new Dialog("Zeilenbeschriftung Stunden - Überschrift", 560, 340);
        var gArt = new GroupBox { Text = "Art der Überschrift", Left = 8, Top = 4, Width = 150, Height = 50 };
        var art = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 20, Width = 132 };
        art.Items.AddRange(Arten);
        art.SelectedIndex = (int)w.Art;
        gArt.Controls.Add(art);
        d.Controls.Add(gArt);
        var (b, k, u) = Stilknoepfe(d, 176, 10, w.Fett, w.Kursiv, w.Unterstrichen);
        var prozent = Regler(d, "Schriftgröße in %", 176, 40, 50, 300, w.Prozent, out _);
        var gZeit = new GroupBox { Text = "Stunden-Zeit", Left = 420, Top = 4, Width = 132, Height = 62 };
        var nurBeginn = new RadioButton { Text = "Nur Beginn-Zeit", Left = 8, Top = 18, Width = 120, Checked = w.NurBeginn };
        var beginnEnde = new RadioButton { Text = "Beginn- + End-Zeit", Left = 8, Top = 38, Width = 120, Checked = !w.NurBeginn };
        gZeit.Controls.AddRange(new Control[] { nurBeginn, beginnEnde });
        d.Controls.Add(gZeit);
        void Zeitwahl() => gZeit.Enabled = art.SelectedIndex is 1 or 2;
        art.SelectedIndexChanged += (_, _) => Zeitwahl();
        Zeitwahl();
        var flaeche = Vorschaufeld(d, 96);
        lesen = () => new ZeilenBeschriftungWerte((ZeilenBeschriftung)Math.Max(0, art.SelectedIndex), nurBeginn.Checked, b.Checked, k.Checked, u.Checked, prozent.Value);
        var l = lesen;
        flaeche.Paint += (_, e) =>
        {
            var x = l();
            var stil = (x.Fett ? FontStyle.Bold : 0) | (x.Kursiv ? FontStyle.Italic : 0) | (x.Unterstrichen ? FontStyle.Underline : 0);
            using var gross = new Font(Ui.Schrift.FontFamily, 16f * x.Prozent / 100f, stil);
            using var klein = new Font(Ui.Schrift.FontFamily, 12f, FontStyle.Regular);
            var rahmen = new Rectangle(24, 16, 200, 128);
            e.Graphics.FillRectangle(Brushes.White, rahmen);
            e.Graphics.DrawRectangle(Pens.Black, rahmen);
            var fl = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            var zeit = x.NurBeginn ? "10:30" : "10:30 - 11:20";
            switch (x.Art)
            {
                case ZeilenBeschriftung.Nummer: TextRenderer.DrawText(e.Graphics, "10", gross, rahmen, Color.Black, fl); break;
                case ZeilenBeschriftung.Zeit: TextRenderer.DrawText(e.Graphics, zeit, gross, rahmen, Color.Black, fl); break;
                case ZeilenBeschriftung.Bezeichnung: TextRenderer.DrawText(e.Graphics, "Bezeichnung", gross, rahmen, Color.Black, fl); break;
                default:
                    var oben = new Rectangle(rahmen.X + 30, rahmen.Y + 10, 140, 48);
                    var unten = new Rectangle(rahmen.X + 10, rahmen.Y + 70, 180, 40);
                    e.Graphics.DrawRectangle(Pens.Gray, oben);
                    e.Graphics.DrawRectangle(Pens.Gray, unten);
                    TextRenderer.DrawText(e.Graphics, "10", gross, oben, Color.Black, fl);
                    TextRenderer.DrawText(e.Graphics, zeit, klein, unten, Color.Black, fl);
                    break;
            }
        };
        foreach (var c in new[] { b, k, u }) c.CheckedChanged += (_, _) => flaeche.Invalidate();
        foreach (var r in new[] { nurBeginn, beginnEnde }) r.CheckedChanged += (_, _) => flaeche.Invalidate();
        art.SelectedIndexChanged += (_, _) => flaeche.Invalidate();
        prozent.ValueChanged += (_, _) => flaeche.Invalidate();
        return d;
    }
}
