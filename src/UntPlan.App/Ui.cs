using System.Drawing.Drawing2D;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>Gemeinsame Gestaltung (angelehnt an klassische Windows-/Office-Oberflächen) und Hilfsfunktionen.</summary>
public static class Ui
{
    public static readonly Font Schrift = new("Segoe UI", 9f);
    public static readonly Font SchriftFett = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font SchriftKlein = new("Segoe UI", 8f);
    public static readonly Font SchriftGross = new("Segoe UI", 11f, FontStyle.Bold);

    /// <summary>Vertretungsmodus aktiv: Oberfläche in Grün (wie ein eigenes Programm innerhalb von UntPlan).</summary>
    public static bool Vertretungsmodus { get; set; }

    // Eigenes Farbschema "Petrol" (Stundenplanmodus) bzw. "Grün" (Vertretungsmodus)
    public static Color Blau => Vertretungsmodus ? Color.FromArgb(46, 112, 64) : Color.FromArgb(18, 94, 122);
    public static Color BlauHell => Vertretungsmodus ? Color.FromArgb(206, 234, 208) : Color.FromArgb(201, 228, 236);
    public static Color BlauSehrHell => Vertretungsmodus ? Color.FromArgb(234, 246, 235) : Color.FromArgb(232, 243, 247);
    public static Color Titel => Vertretungsmodus ? Color.FromArgb(78, 140, 92) : Color.FromArgb(52, 122, 148);
    public static Color MdiHintergrund => Vertretungsmodus ? Color.FromArgb(150, 186, 156) : Color.FromArgb(176, 190, 197);
    public static Color Kopf => Vertretungsmodus ? Color.FromArgb(238, 246, 238) : Color.FromArgb(240, 245, 247);
    public static Color RibbonTab => Vertretungsmodus ? Color.FromArgb(232, 242, 233) : Color.FromArgb(236, 241, 243);
    public static Color Hover => Vertretungsmodus ? Color.FromArgb(214, 238, 216) : Color.FromArgb(214, 234, 240);
    public static Color HoverRand => Vertretungsmodus ? Color.FromArgb(120, 190, 130) : Color.FromArgb(110, 178, 200);
    public static readonly Color Rasterlinie = Color.FromArgb(208, 215, 229);
    public static readonly Color ZeileAktiv = Color.FromArgb(253, 222, 176);
    public static readonly Color Erste = Color.FromArgb(252, 228, 188);
    public static readonly Color Gruen = Color.FromArgb(120, 220, 120);
    public static readonly Color Rot = Color.FromArgb(240, 110, 110);
    public static readonly Color Violett = Color.FromArgb(220, 150, 235);
    public static readonly Color Vormittag = Color.FromArgb(255, 153, 51);
    public static readonly Color Nachmittag = Color.FromArgb(102, 230, 240);

    public static Color Farbe(int argb, Color standard) => argb == 0 ? standard : Color.FromArgb(argb | unchecked((int)0xFF000000));

    public static Color Textfarbe(Color hintergrund) =>
        hintergrund.R * 0.299 + hintergrund.G * 0.587 + hintergrund.B * 0.114 > 150 ? Color.Black : Color.White;

    /// <summary>Farbe eines Zeitwunsches (-3 … +3).</summary>
    public static Color ZeitwunschFarbe(int w) => w switch
    {
        3 => Color.FromArgb(0, 176, 80),
        2 => Color.FromArgb(102, 204, 102),
        1 => Color.FromArgb(198, 239, 206),
        -1 => Color.FromArgb(255, 199, 206),
        -2 => Color.FromArgb(255, 124, 128),
        -3 => Color.FromArgb(230, 40, 40),
        _ => Color.White,
    };

    public static void RasterGestalten(DataGridView g, bool editierbar = true)
    {
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.GridColor = Rasterlinie;
        g.Font = Schrift;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Kopf;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
        g.ColumnHeadersDefaultCellStyle.Font = Schrift;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Kopf;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 22;
        g.RowHeadersDefaultCellStyle.BackColor = Erste;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = ZeileAktiv;
        g.RowHeadersWidth = 22;
        g.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        g.RowTemplate.Height = 20;
        g.DefaultCellStyle.SelectionBackColor = BlauHell;
        g.DefaultCellStyle.SelectionForeColor = Color.Black;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
        g.AllowUserToResizeRows = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = !editierbar;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);
    }

    /// <summary>Im Selbsttest werden Fehler protokolliert statt angezeigt (keine blockierenden Meldungsfenster).</summary>
    public static List<string>? FehlerProtokoll { get; set; }

    public static void Fehler(Exception ex)
    {
        if (FehlerProtokoll != null) { FehlerProtokoll.Add(ex.ToString()); return; }
        var text = ex is InvalidOperationException or FormatException or InvalidDataException ? ex.Message : ex.ToString();
        MessageBox.Show(text, "UntPlan", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static void Info(string text, string titel = "UntPlan") => MessageBox.Show(text, titel, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Frage(string text, string titel = "UntPlan") =>
        MessageBox.Show(text, titel, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static string? Eingabe(string text, string titel, string vorgabe = "")
    {
        using var f = new Form { Text = titel, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 120), Font = Schrift, ShowInTaskbar = false };
        var l = new Label { Text = text, Left = 12, Top = 12, Width = 356, AutoSize = false, Height = 20 };
        var t = new TextBox { Text = vorgabe, Left = 12, Top = 36, Width = 356 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 200, Top = 80, Width = 80 };
        var ab = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Left = 288, Top = 80, Width = 80 };
        f.Controls.AddRange(new Control[] { l, t, ok, ab });
        f.AcceptButton = ok;
        f.CancelButton = ab;
        return f.ShowDialog() == DialogResult.OK ? t.Text : null;
    }

    public static string ElementName(Element e) => e switch
    {
        Lehrer l => $"{l.Kurzname} – {l.Langname}{(l.Vorname.Length > 0 ? ", " + l.Vorname : "")}",
        _ => string.IsNullOrEmpty(e.Langname) ? e.Kurzname : $"{e.Kurzname} – {e.Langname}",
    };

    public static string TypName(ElementTyp t, bool mehrzahl = false) => t switch
    {
        ElementTyp.Klasse => mehrzahl ? "Klassen" : "Klasse",
        ElementTyp.Lehrer => "Lehrer",
        ElementTyp.Raum => mehrzahl ? "Räume" : "Raum",
        ElementTyp.Fach => mehrzahl ? "Fächer" : "Fach",
        ElementTyp.Raumgruppe => mehrzahl ? "Raumgruppen" : "Raumgruppe",
        ElementTyp.Gang => mehrzahl ? "Gänge" : "Gang",
        ElementTyp.Abteilung => mehrzahl ? "Abteilungen" : "Abteilung",
        ElementTyp.Abwesenheitsgrund => mehrzahl ? "Abwesenheitsgründe" : "Abwesenheitsgrund",
        ElementTyp.Schueler => "Schüler",
        _ => "Unterricht",
    };

    public static GraphicsPath AbgerundetesRechteck(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Einfache Formularzeile: Beschriftung + Steuerelement.</summary>
    public static T Zeile<T>(Control container, string text, T control, ref int y, int labelBreite = 170, int breite = 220) where T : Control
    {
        container.Controls.Add(new Label { Text = text, Left = 12, Top = y + 3, Width = labelBreite, AutoSize = false, Height = 18 });
        control.Left = 12 + labelBreite;
        control.Top = y;
        if (control is not CheckBox) control.Width = breite;
        container.Controls.Add(control);
        y += Math.Max(26, control.Height + 6);
        return control;
    }
}
