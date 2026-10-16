using System.Net;
using System.Text;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Formatierter Tagestext wie im Vorbild („3.9.21 Tagestexte“): Leiste mit Fett (B), Kursiv (K), Unterstrichen (U) und Farbe,
/// darunter das Textfeld, unten Ok/Abbrechen. Gespeichert werden Rich Text, unformatierter Text und bereinigtes HTML (Export).
/// </summary>
public sealed class TagestextFormatDialog : Form
{
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DetectUrls = false, AcceptsTab = false };
    private readonly ToolStripButton _fett, _kursiv, _unter;
    private readonly ToolStripButton _farbe;
    private Color _letzteFarbe = Color.Red;

    /// <summary>Ergebnis nach Ok.</summary>
    public string Rtf { get; private set; } = "";
    public string Html { get; private set; } = "";
    public string Klartext { get; private set; } = "";

    /// <summary>Textfeld (für den Selbsttest).</summary>
    internal RichTextBox Textfeld => _text;

    public TagestextFormatDialog(DateOnly datum, Tagestext? vorlage)
    {
        Text = $"{datum.Day}.{datum.Month}.{datum.Year % 100:00} Tagestexte";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(330, 300);
        MinimumSize = new Size(260, 200);
        BackColor = Color.FromArgb(240, 240, 240);

        var leiste = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Padding = new Padding(4, 1, 0, 1), BackColor = BackColor };
        ToolStripButton Knopf(string text, FontStyle stil, string tip)
        {
            var k = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip, CheckOnClick = false, AutoSize = false, Width = 20, Height = 20 };
            k.Font = new Font("Segoe UI", 9f, stil);
            k.Click += (_, _) => StilUmschalten(stil);
            leiste.Items.Add(k);
            return k;
        }
        _fett = Knopf("B", FontStyle.Bold, "Fett (Strg+B)");
        _kursiv = Knopf("K", FontStyle.Italic, "Kursiv (Strg+K)");
        _unter = Knopf("U", FontStyle.Underline, "Unterstrichen (Strg+U)");
        _farbe = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Schriftfarbe", Image = FarbSymbol(_letzteFarbe), AutoSize = false, Width = 24, Height = 20 };
        _farbe.Click += (_, _) => FarbeWaehlen();
        leiste.Items.Add(_farbe);

        var rahmen = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Color.FromArgb(120, 120, 120) };
        var innen = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(3) };
        innen.Controls.Add(_text);
        rahmen.Controls.Add(innen);
        var mitte = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 2, 4, 4) };
        mitte.Controls.Add(rahmen);

        var fuss = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        var ok = new Button { Text = "Ok", DialogResult = DialogResult.OK, Left = 4, Top = 5, Width = 80, Height = 23 };
        var abbrechen = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Left = 92, Top = 5, Width = 84, Height = 23 };
        fuss.Controls.Add(ok);
        fuss.Controls.Add(abbrechen);
        AcceptButton = null; // Eingabetaste erzeugt im Textfeld einen Zeilenumbruch
        CancelButton = abbrechen;
        Controls.Add(mitte);
        Controls.Add(fuss);
        Controls.Add(leiste);

        _text.Font = new Font("Segoe UI", 9.75f);
        if (vorlage != null)
        {
            if (!string.IsNullOrEmpty(vorlage.TextRtf))
            {
                try { _text.Rtf = vorlage.TextRtf; }
                catch (ArgumentException) { _text.Text = vorlage.Text; }
            }
            else _text.Text = vorlage.Text;
        }
        _text.SelectionChanged += (_, _) => LeisteAktualisieren();
        _text.KeyDown += (_, e) =>
        {
            if (!e.Control) return;
            var stil = e.KeyCode switch { Keys.B => FontStyle.Bold, Keys.K or Keys.I => FontStyle.Italic, Keys.U => FontStyle.Underline, _ => FontStyle.Regular };
            if (stil == FontStyle.Regular) return;
            StilUmschalten(stil);
            e.SuppressKeyPress = true;
        };
        ok.Click += (_, _) => Uebernehmen();
        LeisteAktualisieren();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.TitelleisteFaerben(this);
    }

    /// <summary>Ergebnis aus dem Textfeld übernehmen (Ok).</summary>
    internal void Uebernehmen()
    {
        Klartext = _text.Text.Replace("\r\n", "\n").TrimEnd('\n', ' ');
        Html = HtmlAus(_text);
        // nur formatierte Texte als Rich Text/HTML speichern – unformatierte bleiben reiner Text
        var formatiert = Html.Contains("<b>") || Html.Contains("<i>") || Html.Contains("<u>") || Html.Contains("<span");
        Rtf = formatiert ? _text.Rtf ?? "" : "";
        if (!formatiert) Html = "";
    }

    /// <summary>Fett, kursiv oder unterstrichen für die Auswahl umschalten (gemischte Auswahl: einschalten).</summary>
    internal void StilUmschalten(FontStyle stil)
    {
        var schrift = _text.SelectionFont ?? _text.Font;
        var an = !schrift.Style.HasFlag(stil);
        if (_text.SelectionLength == 0)
        {
            _text.SelectionFont = new Font(schrift, an ? schrift.Style | stil : schrift.Style & ~stil);
        }
        else
        {
            // Zeichen einzeln setzen, damit andere Auszeichnungen (z. B. Fett in einem kursiven Wort) erhalten bleiben
            var (start, laenge) = (_text.SelectionStart, _text.SelectionLength);
            for (var i = start; i < start + laenge; i++)
            {
                _text.Select(i, 1);
                var f = _text.SelectionFont ?? _text.Font;
                _text.SelectionFont = new Font(f, an ? f.Style | stil : f.Style & ~stil);
            }
            _text.Select(start, laenge);
        }
        LeisteAktualisieren();
        _text.Focus();
    }

    /// <summary>Schriftfarbe für die Auswahl setzen.</summary>
    internal void FarbeSetzen(Color farbe)
    {
        _letzteFarbe = farbe;
        _farbe.Image = FarbSymbol(farbe);
        _text.SelectionColor = farbe;
        _text.Focus();
    }

    private void FarbeWaehlen()
    {
        using var cd = new ColorDialog { Color = _letzteFarbe, FullOpen = false, AnyColor = true };
        if (cd.ShowDialog(this) == DialogResult.OK) FarbeSetzen(cd.Color);
    }

    private void LeisteAktualisieren()
    {
        var f = _text.SelectionFont;
        _fett.Checked = f?.Bold == true;
        _kursiv.Checked = f?.Italic == true;
        _unter.Checked = f?.Underline == true;
    }

    private static Bitmap FarbSymbol(Color farbe)
    {
        var b = new Bitmap(16, 16);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // Farbeimer-ähnliches Symbol: Kreis mit gewählter Farbe und dunklem Rand
        using (var br = new SolidBrush(Color.FromArgb(232, 190, 60))) g.FillEllipse(br, 2, 1, 12, 10);
        using (var p = new Pen(Color.FromArgb(120, 90, 20))) g.DrawEllipse(p, 2, 1, 12, 10);
        using (var br = new SolidBrush(farbe)) g.FillRectangle(br, 1, 12, 14, 4);
        return b;
    }

    /// <summary>
    /// HTML-Subset aus dem Inhalt eines RichTextBox: je Absatz &lt;p&gt;, Läufe gleicher Auszeichnung als &lt;b&gt;, &lt;i&gt;, &lt;u&gt;
    /// und &lt;span style="color:#rrggbb"&gt;; das Ergebnis wird mit <see cref="TagestextHtml.Bereinigen"/> bereinigt.
    /// </summary>
    public static string HtmlAus(RichTextBox rtb)
    {
        var text = rtb.Text.Replace("\r\n", "\n");
        if (text.Trim().Length == 0) return "";
        var auswahl = (rtb.SelectionStart, rtb.SelectionLength);
        var sb = new StringBuilder();
        var standard = rtb.ForeColor;
        var zeilen = text.TrimEnd('\n').Split('\n');
        var pos = 0;
        foreach (var zeile in zeilen)
        {
            sb.Append("<p>");
            if (zeile.Length == 0) sb.Append("<br>");
            var i = 0;
            while (i < zeile.Length)
            {
                rtb.Select(pos + i, 1);
                var (f, c) = (rtb.SelectionFont, rtb.SelectionColor);
                var j = i + 1;
                while (j < zeile.Length)
                {
                    rtb.Select(pos + j, 1);
                    var f2 = rtb.SelectionFont;
                    if (f2?.Style != f?.Style || rtb.SelectionColor.ToArgb() != c.ToArgb()) break;
                    j++;
                }
                var lauf = WebUtility.HtmlEncode(zeile[i..j]);
                var farbig = c.ToArgb() != standard.ToArgb() && c.ToArgb() != Color.Black.ToArgb() && c.A > 0;
                if (farbig) sb.Append($"<span style=\"color:#{c.R:x2}{c.G:x2}{c.B:x2}\">");
                if (f?.Bold == true) sb.Append("<b>");
                if (f?.Italic == true) sb.Append("<i>");
                if (f?.Underline == true) sb.Append("<u>");
                sb.Append(lauf);
                if (f?.Underline == true) sb.Append("</u>");
                if (f?.Italic == true) sb.Append("</i>");
                if (f?.Bold == true) sb.Append("</b>");
                if (farbig) sb.Append("</span>");
                i = j;
            }
            sb.Append("</p>");
            pos += zeile.Length + 1;
        }
        rtb.Select(auswahl.SelectionStart, auswahl.SelectionLength);
        return TagestextHtml.Bereinigen(sb.ToString());
    }

    /// <summary>Dialog zeigen; bei Ok die formatierten Werte in den Tagestext schreiben.</summary>
    public static bool Bearbeiten(IWin32Window owner, DateOnly datum, Tagestext t)
    {
        using var d = new TagestextFormatDialog(datum, t);
        if (d.ShowDialog(owner) != DialogResult.OK) return false;
        t.Text = d.Klartext;
        t.TextRtf = d.Rtf;
        t.TextHtml = d.Html;
        return true;
    }
}
