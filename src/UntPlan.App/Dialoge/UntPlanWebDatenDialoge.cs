using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Gemeinsame Grundlage der Anzeigefenster für aus UntPlanWeb importierte Daten: Tabelle (nur lesend), Filter nach Klasse,
/// Suche und Summenzeile.
/// </summary>
public abstract class WebListenDialog : Dialog
{
    protected readonly DataGridView Raster = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _klasse = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, ToolTipText = "Nur Einträge dieser Klasse anzeigen" };
    private readonly ToolStripTextBox _suche = new() { Width = 160, ToolTipText = "Suche in allen Spalten" };
    private readonly Label _summe = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 5, 0, 0), BackColor = Ui.Kopf };

    protected WebListenDialog(string titel, int breite, int hoehe) : base(titel, breite, hoehe)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(640, 300);
        Ok.Text = "Schließen";
        Abbrechen.Visible = false;
        CancelButton = Ok;

        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = Ui.Kopf, Renderer = new Steuerelemente.LeistenRenderer() };
        ts.Items.Add(new ToolStripLabel("Klasse:"));
        ts.Items.Add(_klasse);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripLabel("Suche:"));
        ts.Items.Add(_suche);
        ts.Items.Add(new ToolStripLabel("Importiert aus UntPlanWeb – Änderungen bitte dort vornehmen.") { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.DimGray });

        Ui.RasterGestalten(Raster, false);
        Raster.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Raster.RowHeadersVisible = false;
        Raster.AllowUserToOrderColumns = true;

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 44, Width = breite };
        Controls.Remove(Ok);
        Ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Ok.Location = new Point(breite - 100, 9);
        unten.Controls.Add(Ok);
        Controls.Add(Raster);
        Controls.Add(_summe);
        Controls.Add(unten);
        Controls.Add(ts);

        _klasse.SelectedIndexChanged += (_, _) => Fuellen();
        _suche.TextChanged += (_, _) => Fuellen();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _klasse.Items.Add("Alle");
        foreach (var k in Klassen().Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)) _klasse.Items.Add(k);
        _klasse.SelectedIndex = 0; // füllt die Tabelle
    }

    protected void Spalte(string name, string titel, int breite, bool fuellen = false)
    {
        var c = Raster.Columns[Raster.Columns.Add(name, titel)];
        c.Width = breite;
        if (fuellen) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = breite; }
    }

    /// <summary>Alle vorkommenden Klassen (für den Filter).</summary>
    protected abstract IEnumerable<string> Klassen();

    /// <summary>Zeilen für die gewählte Klasse (null = alle); liefert die Summenzeile.</summary>
    protected abstract string Zeilen(string? klasse, Func<string[], bool> passt);

    private void Fuellen()
    {
        var klasse = _klasse.SelectedIndex > 0 ? _klasse.SelectedItem as string : null;
        var suche = _suche.Text.Trim();
        bool Passt(string[] werte) => suche.Length == 0 || werte.Any(w => w.Contains(suche, StringComparison.CurrentCultureIgnoreCase));
        Raster.SuspendLayout();
        Raster.Rows.Clear();
        _summe.Text = Zeilen(klasse, Passt);
        Raster.ResumeLayout();
    }

    protected static string TagText(DateOnly d) => d.ToString("ddd dd.MM.yyyy");
}

/// <summary>
/// „Schülerabwesenheiten“: im Klassenbuch von UntPlanWeb erfasste Fehlzeiten (Import „Studenten*“), je Stunde mit
/// Art (fehlt/verspätet), Entschuldigungsstatus, Bemerkung und erfassender Lehrkraft.
/// </summary>
public sealed class SchuelerAbwesenheitenDialog : WebListenDialog
{
    private readonly List<SchuelerAbwesenheit> _liste;

    /// <param name="vorgabe">Einträge ohne Schuldatei (Selbsttest), sonst null.</param>
    public SchuelerAbwesenheitenDialog(IEnumerable<SchuelerAbwesenheit>? vorgabe = null) : base("Schülerabwesenheiten (UntPlanWeb)", 980, 520)
    {
        _liste = (vorgabe ?? AppZustand.D.SchuelerAbwesenheiten).OrderByDescending(a => a.Datum).ThenBy(a => a.Klasse).ThenBy(a => a.Name).ThenBy(a => a.Stunde).ToList();
        Spalte("Datum", "Datum", 100);
        Spalte("Stunde", "Std.", 40);
        Spalte("Klasse", "Klasse", 60);
        Spalte("Schueler", "Schüler/in", 170);
        Spalte("Fach", "Fach", 60);
        Spalte("Lehrer", "Lehrer", 60);
        Spalte("Art", "Art", 110);
        Spalte("Status", "Status", 100);
        Spalte("Bemerkung", "Bemerkung", 160, true);
        Raster.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || Raster.Columns[e.ColumnIndex].Name != "Status" || e.CellStyle is null) return;
            e.CellStyle.ForeColor = (e.Value as string) switch
            {
                "entschuldigt" => Color.DarkGreen, "unentschuldigt" => Color.Firebrick, _ => Color.DimGray,
            };
        };
    }

    protected override IEnumerable<string> Klassen() => _liste.Select(a => a.Klasse);

    protected override string Zeilen(string? klasse, Func<string[], bool> passt)
    {
        var namen = AppZustand.D.Schueler.ToDictionary(s => s.Id, s => $"{(s.Langname.Length > 0 ? s.Langname : s.Kurzname)}, {s.Vorname}");
        var gezeigt = new List<SchuelerAbwesenheit>();
        foreach (var a in _liste.Where(a => klasse is null || a.Klasse.Equals(klasse, StringComparison.OrdinalIgnoreCase)))
        {
            var werte = new[]
            {
                TagText(a.Datum), a.Stunde.ToString(), a.Klasse, namen.GetValueOrDefault(a.SchuelerId, a.Name), a.Fach, a.Lehrer,
                a.Verspaetet ? $"verspätet ({a.Minuten} Min.)" : "fehlt", a.Status, a.Bemerkung,
            };
            if (!passt(werte)) continue;
            Raster.Rows.Add(werte);
            gezeigt.Add(a);
        }
        var fehlt = gezeigt.Where(a => !a.Verspaetet).ToList();
        var versp = gezeigt.Where(a => a.Verspaetet).ToList();
        return $"{fehlt.Count} Fehlstunden ({fehlt.Count(a => a.Status == "entschuldigt")} entschuldigt, {fehlt.Count(a => a.Status == "unentschuldigt")} unentschuldigt, " +
               $"{fehlt.Count(a => a.Status == "offen")} offen) · {versp.Count} Verspätungen ({versp.Sum(a => a.Minuten)} Min.) · {gezeigt.Select(a => a.SchuelerId).Distinct().Count()} Schüler/innen";
    }
}

/// <summary>
/// „Klassenbuch (UntPlanWeb)“: Lehrstoff und Hausaufgaben je gehaltener Stunde (Import „Kurse“) mit Datum, Stunde,
/// Klasse(n), Fach, Lehrkraft, Fälligkeit und Anwesenheitsprüfung – nur lesend.
/// </summary>
public sealed class KlassenbuchDialog : WebListenDialog
{
    private readonly List<KlassenbuchEintrag> _liste;

    /// <param name="vorgabe">Einträge ohne Schuldatei (Selbsttest), sonst null.</param>
    public KlassenbuchDialog(IEnumerable<KlassenbuchEintrag>? vorgabe = null) : base("Klassenbuch (UntPlanWeb)", 1040, 520)
    {
        _liste = (vorgabe ?? AppZustand.D.Klassenbuch).OrderByDescending(e => e.Datum).ThenBy(e => e.Stunde).ToList();
        Spalte("Datum", "Datum", 100);
        Spalte("Stunde", "Std.", 40);
        Spalte("Klassen", "Klasse(n)", 80);
        Spalte("Fach", "Fach", 60);
        Spalte("Lehrer", "Lehrer", 60);
        Spalte("Lehrstoff", "Lehrstoff", 260, true);
        Spalte("Hausaufgabe", "Hausaufgabe", 200);
        Spalte("Faellig", "fällig", 80);
        Spalte("Anw", "Anwesenheit", 80);
    }

    protected override IEnumerable<string> Klassen() => _liste.SelectMany(e => e.Klassen);

    protected override string Zeilen(string? klasse, Func<string[], bool> passt)
    {
        int n = 0, ha = 0;
        foreach (var e in _liste.Where(e => klasse is null || e.Klassen.Contains(klasse, StringComparer.OrdinalIgnoreCase)))
        {
            var werte = new[]
            {
                TagText(e.Datum), e.Stunde.ToString(), string.Join(", ", e.Klassen), e.Fach, e.Lehrer, e.Lehrstoff, e.Hausaufgabe,
                e.Faellig is DateOnly f ? f.ToString("dd.MM.yyyy") : "", e.AnwesenheitGeprueft ? "geprüft" : "",
            };
            if (!passt(werte)) continue;
            Raster.Rows.Add(werte);
            n++;
            if (e.Hausaufgabe.Length > 0) ha++;
        }
        return $"{n} Klassenbucheinträge, davon {ha} mit Hausaufgabe";
    }
}
