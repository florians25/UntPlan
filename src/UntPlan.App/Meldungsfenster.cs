using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace UntPlan.App;

/// <summary>Art eines Meldungsfensters: bestimmt Symbol und Farbe.</summary>
public enum MeldungsArt { Info, Frage, Warnung, Fehler }

/// <summary>
/// Hinweis-, Frage- und Fehlerfenster im Design der Anwendung (statt der Windows-Meldungsfenster): Akzentleiste und Titelleiste in
/// der Modusfarbe, rundes Symbol (i, ?, !, ×), gut lesbarer Text und eine Knopfleiste wie in den Fenstern (Standardknopf farbig).
/// Alle Meldungen laufen über <see cref="Ui.Meldung"/> – auch Raumfragen (Raum belegt, Räume mitnehmen), Löschrückfragen und Fehler.
/// </summary>
public static partial class Ui
{
    /// <summary>Farbe des Symbols je Art.</summary>
    internal static Color MeldungsFarbe(MeldungsArt art) => art switch
    {
        MeldungsArt.Fehler => Color.FromArgb(201, 55, 44),
        MeldungsArt.Warnung => Color.FromArgb(229, 131, 42),
        MeldungsArt.Frage => Titel,
        _ => Blau,
    };

    /// <summary>
    /// Meldung mit eigenen Knöpfen anzeigen. Liefert den Index des gewählten Knopfes; null bei „Abbrechen“ bzw. Escape (mit
    /// <paramref name="abbrechen"/>). Ohne „Abbrechen“ wählt Escape den letzten Knopf (z. B. „Nein“ bzw. „OK“).
    /// </summary>
    public static int? Meldung(string text, string titel, MeldungsArt art, IReadOnlyList<string> knoepfe, int vorgabe = 0, bool abbrechen = false,
        IWin32Window? owner = null, IReadOnlyList<bool>? aktiv = null)
    {
        var ergebnis = new StrongBox<int?>();
        using var f = MeldungsFenster(text, titel, art, knoepfe, vorgabe, abbrechen, aktiv, ergebnis);
        owner ??= Form.ActiveForm is { IsDisposed: false, Visible: true } aktivesFenster ? aktivesFenster : null;
        if (owner is null) f.StartPosition = FormStartPosition.CenterScreen;
        var r = owner is null ? f.ShowDialog() : f.ShowDialog(owner);
        if (r == DialogResult.OK) return ergebnis.Value;
        return abbrechen ? null : knoepfe.Count - 1;
    }

    /// <summary>Meldungsfenster erzeugen (auch nicht modal für Bildschirmfotos im Selbsttest); das Ergebnis steht nach dem Schließen in <paramref name="ergebnis"/>.</summary>
    internal static Form MeldungsFenster(string text, string titel, MeldungsArt art, IReadOnlyList<string> knoepfe, int vorgabe, bool abbrechen,
        IReadOnlyList<bool>? aktiv, StrongBox<int?> ergebnis)
    {
        var f = new MeldungsForm { Text = titel, Art = art };
        var liste = knoepfe.Where(k => !abbrechen || k != "Abbrechen").ToList();
        var alle = abbrechen ? liste.Append("Abbrechen").ToList() : liste;
        int KnopfBreite(string k) => Math.Max(96, TextRenderer.MeasureText(k, SchriftFett).Width + 28);
        var breite = Math.Clamp(alle.Sum(k => KnopfBreite(k) + 8) + 40, 440, 900);
        var textBreite = breite - 92;
        var normal = text.Replace("\r\n", "\n").Trim('\n');
        var groesse = TextRenderer.MeasureText(normal, Schrift, new Size(textBreite, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var hoehe = Math.Clamp(groesse.Height + 6, 40, 440);
        const int oben = 22, leiste = 52;
        var label = new TextBox
        {
            Text = normal.Replace("\n", Environment.NewLine), Left = 76, Top = oben, Width = textBreite, Height = hoehe, Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Color.FromArgb(33, 37, 43), TabStop = false, Font = Schrift,
            ScrollBars = groesse.Height + 6 > 440 ? ScrollBars.Vertical : ScrollBars.None, Name = "Meldungstext",
        };
        f.ClientSize = new Size(breite, oben + hoehe + 22 + leiste);
        f.Controls.Add(label);
        var unten = new Panel { Dock = DockStyle.Bottom, Height = leiste, BackColor = Kopf, Name = "Knopfleiste" };
        unten.Paint += (_, e) => { using var p = new Pen(Rasterlinie); e.Graphics.DrawLine(p, 0, 0, unten.Width, 0); };
        f.Controls.Add(unten);
        // von rechts nach links: Abbrechen ganz rechts, davor die Knöpfe in ihrer Reihenfolge
        var x = breite - 14;
        var indizes = Enumerable.Range(0, liste.Count).Select(i => knoepfe.ToList().IndexOf(liste[i]) is var k && k >= 0 ? k : i).ToList();
        var reihenfolge = (abbrechen ? new[] { -1 } : Array.Empty<int>()).Concat(Enumerable.Range(0, liste.Count).Reverse());
        Button? vorgabeKnopf = null;
        foreach (var pos in reihenfolge)
        {
            var istAbbrechen = pos < 0;
            var t = istAbbrechen ? "Abbrechen" : liste[pos];
            var index = istAbbrechen ? -1 : indizes[pos];
            var w = KnopfBreite(t);
            x -= w;
            var b = new Button
            {
                Text = t, Left = x, Top = (leiste - 30) / 2, Width = w, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(33, 37, 43),
                Font = Schrift, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                Enabled = istAbbrechen || aktiv is null || index >= aktiv.Count || aktiv[index],
            };
            b.FlatAppearance.BorderColor = HoverRand;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.Click += (_, _) => { ergebnis.Value = istAbbrechen ? null : index; f.DialogResult = istAbbrechen ? DialogResult.Cancel : DialogResult.OK; };
            unten.Controls.Add(b);
            if (istAbbrechen) f.CancelButton = b;
            else if (!abbrechen && pos == liste.Count - 1) f.CancelButton = b;
            if (!istAbbrechen && index == vorgabe && b.Enabled) vorgabeKnopf = b;
            x -= 8;
        }
        vorgabeKnopf ??= unten.Controls.OfType<Button>().Where(b => b.Enabled && b != f.CancelButton).LastOrDefault() ?? unten.Controls.OfType<Button>().FirstOrDefault();
        if (vorgabeKnopf != null)
        {
            // Standardknopf farbig in der Modusfarbe
            vorgabeKnopf.BackColor = Titel;
            vorgabeKnopf.ForeColor = Color.White;
            vorgabeKnopf.Font = SchriftFett;
            vorgabeKnopf.FlatAppearance.BorderColor = Blau;
            vorgabeKnopf.FlatAppearance.MouseOverBackColor = Blau;
            f.AcceptButton = vorgabeKnopf;
            f.ActiveControl = vorgabeKnopf;
        }
        // Strg+C kopiert den Meldungstext (wie bei Windows-Meldungen)
        f.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && !label.Focused) { try { Clipboard.SetText(titel + Environment.NewLine + normal); } catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException) { } e.Handled = true; }
        };
        return f;
    }

    /// <summary>Formular des Meldungsfensters: weiß mit Akzentleiste oben und rundem Symbol links.</summary>
    private sealed class MeldungsForm : Form
    {
        public MeldungsArt Art { get; init; }

        public MeldungsForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Schrift;
            BackColor = Color.White;
            KeyPreview = true;
            DoubleBuffered = true;
            ShowIcon = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TitelleisteFaerben(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var akzent = new SolidBrush(Titel)) g.FillRectangle(akzent, 0, 0, ClientSize.Width, 4);
            SymbolZeichnen(g, new Rectangle(22, 20, 38, 38), Art);
        }
    }

    /// <summary>Rundes Symbol der Meldung (i, ?, !, ×) in der Farbe der Art.</summary>
    internal static void SymbolZeichnen(Graphics g, Rectangle r, MeldungsArt art)
    {
        var zustand = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var farbe = MeldungsFarbe(art);
            using (var hell = new SolidBrush(Color.FromArgb(40, farbe))) g.FillEllipse(hell, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
            using (var b = new SolidBrush(farbe)) g.FillEllipse(b, r);
            var zeichen = art switch { MeldungsArt.Fehler => "×", MeldungsArt.Warnung => "!", MeldungsArt.Frage => "?", _ => "i" };
            using var schrift = new Font("Segoe UI", art == MeldungsArt.Fehler ? 20f : 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, zeichen, schrift, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        finally { g.Restore(zustand); }
    }
}
