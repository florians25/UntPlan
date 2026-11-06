using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>Gemeinsame Gestaltung (angelehnt an klassische Windows-/Office-Oberflächen) und Hilfsfunktionen.</summary>
public static partial class Ui
{
    public static readonly Font Schrift = new("Segoe UI", 9f);
    public static readonly Font SchriftFett = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font SchriftKlein = new("Segoe UI", 8f);
    public static readonly Font SchriftGross = new("Segoe UI", 11f, FontStyle.Bold);

    /// <summary>
    /// Weiche Schrift aller Auswahllisten (ComboBoxen, Such- und Tabellen-Dropdowns): Segoe UI – fehlt sie (ältere Systeme, Wine),
    /// die nächste vorhandene serifenlose Schrift statt des harten Ersatzes MS Sans Serif/Tahoma.
    /// </summary>
    public static readonly Font ListenSchrift = new(ListenSchriftart(), 9f);

    /// <summary>Schrift ohne Fettdruck: Texte in Auswahllisten (auch Kürzel) sind nie fett.</summary>
    public static Font OhneFett(Font f) => !f.Bold ? f : ReferenceEquals(f, SchriftFett) ? Schrift
        : f.Name == ListenSchrift.Name && Math.Abs(f.SizeInPoints - ListenSchrift.SizeInPoints) < 0.1f ? ListenSchrift : NichtFett.GetValue(f, x => new Font(x, x.Style & ~FontStyle.Bold));

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Font, Font> NichtFett = new();

    private static string ListenSchriftart()
    {
        try
        {
            using var installiert = new System.Drawing.Text.InstalledFontCollection();
            var namen = installiert.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var n in new[] { "Segoe UI", "Segoe UI Variable Text", "Calibri", "Arial", "Liberation Sans", "DejaVu Sans" })
                if (namen.Contains(n)) return n;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException) { }
        return SystemFonts.MessageBoxFont?.Name ?? "Segoe UI";
    }

    /// <summary>
    /// Text einer Auswahlliste geglättet zeichnen (ClearType bzw. Kantenglättung über GDI+), unabhängig von der Systemeinstellung
    /// für die Schriftglättung – einheitlich in allen selbst gezeichneten Dropdowns. Der Hintergrund muss bereits gefüllt sein.
    /// </summary>
    public static void ListenText(Graphics g, string text, Font schrift, Rectangle r, Color farbe, bool auslassung = true)
    {
        if (string.IsNullOrEmpty(text) || r.Width <= 0 || r.Height <= 0) return;
        var vorher = g.TextRenderingHint;
        g.TextRenderingHint = ListenGlaettung;
        using var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near,
            Trimming = auslassung ? StringTrimming.EllipsisCharacter : StringTrimming.None,
            HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None,
        };
        using var b = new SolidBrush(farbe);
        g.DrawString(text, schrift, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
        g.TextRenderingHint = vorher;
    }

    /// <summary>ClearType, wenn das System ClearType verwendet, sonst Graustufen-Kantenglättung (z. B. unter Wine bzw. Remotedesktop).</summary>
    internal static System.Drawing.Text.TextRenderingHint ListenGlaettung
    {
        get
        {
            try { return SystemInformation.FontSmoothingType == 2 ? System.Drawing.Text.TextRenderingHint.ClearTypeGridFit : System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException) { return System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; }
        }
    }

    /// <summary>
    /// Weiche Listenschrift setzen, wenn die Liste die Standardschrift (bzw. einen harten Ersatz) verwendet – nie fett, auch wenn
    /// die Umgebung (Gruppe, Tabellenzelle) fett schreibt.
    /// </summary>
    public static void ListenSchriftSetzen(ComboBox cb)
    {
        if (cb.IsDisposed || ReferenceEquals(cb.Font, ListenSchrift)) return;
        var f = cb.Font;
        if (Math.Abs(f.SizeInPoints - 9f) > 0.6f || f.Italic || f.Underline) { if (f.Bold) cb.Font = OhneFett(f); return; }
        if (f.Name is "Segoe UI" or "Tahoma" or "Microsoft Sans Serif" or "MS Sans Serif" || f.Name == ListenSchrift.Name) cb.Font = ListenSchrift;
        else if (f.Bold) cb.Font = OhneFett(f);
    }

    /// <summary>Vertretungsmodus aktiv: Oberfläche in Grün (wie ein eigenes Programm innerhalb von UntPlan).</summary>
    public static bool Vertretungsmodus { get; set; }

    // Farbschema wie im Vorbild (Bildschirmaufnahme nachgemessen): Stundenplanmodus hellblau, Vertretungsmodus grün
    public static Color Blau => Vertretungsmodus ? Color.FromArgb(56, 112, 90) : Color.FromArgb(38, 110, 165);
    public static Color BlauHell => Vertretungsmodus ? Color.FromArgb(199, 222, 213) : Color.FromArgb(196, 224, 246);
    public static Color BlauSehrHell => Vertretungsmodus ? Color.FromArgb(229, 239, 233) : Color.FromArgb(223, 239, 248);
    public static Color Titel => Vertretungsmodus ? Color.FromArgb(83, 137, 110) : Color.FromArgb(81, 155, 205);
    public static Color MdiHintergrund => Dialoge.Einstellungsseiten.Hintergrund(Vertretungsmodus, Vertretungsmodus ? Color.FromArgb(179, 228, 204) : Color.FromArgb(221, 239, 254));
    public static Color Kopf => Vertretungsmodus ? Color.FromArgb(229, 239, 235) : Color.FromArgb(232, 243, 251);
    public static Color RibbonTab => Vertretungsmodus ? Color.FromArgb(240, 246, 243) : Color.FromArgb(236, 241, 243);
    public static Color Hover => Vertretungsmodus ? Color.FromArgb(214, 236, 226) : Color.FromArgb(212, 233, 250);
    public static Color HoverRand => Vertretungsmodus ? Color.FromArgb(110, 175, 150) : Color.FromArgb(110, 165, 215);
    /// <summary>Hintergrund der Listen (leere Fläche) und der Zellen – leicht in der Modusfarbe getönt statt Weiß.</summary>
    public static Color Listenhintergrund => Vertretungsmodus ? Color.FromArgb(229, 239, 235) : Color.FromArgb(234, 243, 251);
    public static Color Zelle => Vertretungsmodus ? Color.White : Color.FromArgb(251, 253, 254);
    public static Color ZelleWechsel => Vertretungsmodus ? Color.FromArgb(240, 247, 244) : Color.FromArgb(244, 248, 250);
    /// <summary>Wie im Vorbild: nicht bearbeitbare Felder etwas dunkler (grünlich-grau bzw. petrol-grau), nur bearbeitbare Felder hell.</summary>
    public static Color ZelleGesperrt => Vertretungsmodus ? Color.FromArgb(229, 239, 235) : Color.FromArgb(228, 238, 248);
    /// <summary>Aktuelle Zelle im Vertretungsmodus (helles Orange wie im Vorbild).</summary>
    public static readonly Color ZelleAktivVertretung = Color.FromArgb(255, 234, 198);
    /// <summary>Werkzeugleisten der Arbeitsfenster.</summary>
    public static Color Leiste => Vertretungsmodus ? Color.FromArgb(240, 246, 243) : Color.FromArgb(246, 248, 251);
    /// <summary>Markierte Zeilen im Vertretungsmodus (gedecktes Grün wie im Vorbild).</summary>
    public static readonly Color AuswahlVertretung = Color.FromArgb(199, 222, 213);
    /// <summary>Zelle unter der Maus.</summary>
    public static Color ZelleHover => Vertretungsmodus ? Color.FromArgb(214, 236, 226) : Color.FromArgb(222, 238, 246);
    /// <summary>Leere Stunde im Stundenplan.</summary>
    public static Color PlanLeer => Vertretungsmodus ? Color.FromArgb(248, 252, 250) : Color.FromArgb(248, 251, 252);
    public static Color Rasterlinie => Vertretungsmodus ? Color.FromArgb(174, 207, 194) : Color.FromArgb(208, 215, 229);
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
        RasterFarben(g);
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
        // Höhen nach der tatsächlichen Schrift (größere Windows-Schrift/Skalierung: nichts abgeschnitten), mindestens wie bisher
        var zeilenSchrift = g.DefaultCellStyle.Font ?? g.Font;
        g.ColumnHeadersHeight = Math.Max(22, zeilenSchrift.Height + 7);
        g.RowHeadersDefaultCellStyle.BackColor = Erste;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = ZeileAktiv;
        g.RowHeadersWidth = 22;
        g.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        g.RowTemplate.Height = Math.Max(20, zeilenSchrift.Height + 5);
        // Spaltenüberschriften nie abgeschnitten (größere Windows-Schrift): beim ersten Anzeigen auf Textbreite verbreitern
        g.HandleCreated += (_, _) =>
        {
            var kopfSchrift = g.ColumnHeadersDefaultCellStyle.Font ?? g.Font;
            foreach (DataGridViewColumn c in g.Columns)
            {
                if (c.Visible && c.HeaderText.Length > 0 && c.AutoSizeMode is DataGridViewAutoSizeColumnMode.None or DataGridViewAutoSizeColumnMode.NotSet)
                    c.Width = Math.Max(c.Width, TextRenderer.MeasureText(c.HeaderText, kopfSchrift).Width + 8);
                // Einstellungen | Anpassen: „Tooltips mit Spaltenbeschreibungen einblenden“
                if (AppZustand.Einstellungen.SpaltenTooltips && c.ToolTipText.Length == 0 && c.HeaderText.Length > 0) c.ToolTipText = c.HeaderText;
            }
        };
        g.DefaultCellStyle.SelectionBackColor = BlauHell;
        g.DefaultCellStyle.SelectionForeColor = Color.Black;

        g.AllowUserToResizeRows = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.ReadOnly = !editierbar;
        g.SelectionMode = DataGridViewSelectionMode.CellSelect;
        g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        // ein Klick genügt, um in ein änderbares Feld zu wechseln (Strg/Umschalt-Klick bleibt Mehrfachauswahl)
        g.CellClick += (_, e) =>
        {
            if (g.ReadOnly || e.RowIndex < 0 || e.ColumnIndex < 0 || Control.ModifierKeys != Keys.None || g.IsCurrentCellInEditMode) return;
            // erst nach Abschluss des Zellwechsels bearbeiten (kein Wiedereintritt in den Zellwechsel der Tabelle)
            var (zeile, spalte) = (e.RowIndex, e.ColumnIndex);
            g.BeginInvoke(() =>
            {
                if (g.IsDisposed || g.IsCurrentCellInEditMode || g.CurrentCell is not { } zelle || zelle.RowIndex != zeile || zelle.ColumnIndex != spalte) return;
                if (zelle.ReadOnly || zelle is DataGridViewCheckBoxCell || zelle is DataGridViewButtonCell) return;
                if (g.SelectedCells.Count > 1 && g.SelectionMode != DataGridViewSelectionMode.FullRowSelect) return;
                try
                {
                    g.BeginEdit(zelle is not DataGridViewComboBoxCell);
                    if (zelle is DataGridViewComboBoxCell && g.EditingControl is ComboBox cb) cb.DroppedDown = true;
                }
                catch (InvalidOperationException) { }
            });
        };
        g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        // wie im Vorbild: Überschriften einzeilig (zu schmale Spalten schneiden ab statt umzubrechen), Überschrift der Spalte
        // mit der aktuellen Zelle hell orange
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        DataGridViewColumn? kopfAktiv = null;
        g.CurrentCellChanged += (_, _) =>
        {
            var neu = g.CurrentCell?.OwningColumn;
            if (ReferenceEquals(neu, kopfAktiv)) return;
            if (kopfAktiv is { DataGridView: not null }) kopfAktiv.HeaderCell.Style.BackColor = Color.Empty;
            if (neu != null) neu.HeaderCell.Style.BackColor = Vertretungsmodus ? ZelleAktivVertretung : Erste;
            kopfAktiv = neu;
        };
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);
    }

    /// <summary>
    /// Tabellen ohne Zeilenköpfe (wie im Vorbild Absenzen/Vertretungen): die erste sichtbare Zelle der aktuellen Zeile ist hell
    /// orange und zeigt so die Zeile an.
    /// </summary>
    public static void ZeilenmarkeErsteSpalte(DataGridView g)
    {
        var letzte = -1;
        g.CurrentCellChanged += (_, _) =>
        {
            var z = g.CurrentCell?.RowIndex ?? -1;
            if (z == letzte) return;
            if (letzte >= 0 && letzte < g.RowCount) g.InvalidateRow(letzte);
            if (z >= 0) g.InvalidateRow(z);
            letzte = z;
        };
        g.CellFormatting += (_, e) =>
        {
            if (e.CellStyle is null || e.RowIndex < 0 || e.RowIndex != g.CurrentCell?.RowIndex) return;
            var erste = g.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
            if (erste != null && e.ColumnIndex == erste.Index && g.CurrentCell.ColumnIndex != erste.Index)
                e.CellStyle.BackColor = e.CellStyle.SelectionBackColor = Vertretungsmodus ? ZelleAktivVertretung : Erste;
        };
    }

    /// <summary>
    /// Hintergrund, Rasterlinien und Auswahl einer Tabellenzelle ohne Inhalt zeichnen (für eigene Zellinhalte in CellPainting).
    /// Anders als <see cref="DataGridViewCellPaintingEventArgs.PaintBackground"/> werden die Rasterlinien mitgezeichnet.
    /// </summary>
    public static void ZelleOhneInhalt(DataGridViewCellPaintingEventArgs e) =>
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);

    /// <summary>
    /// Häkchen in einer Tabellenzelle zeichnen. Der Zustand der Grafik (Kantenglättung) wird danach wiederhergestellt – die
    /// Grafik wird für alle Zellen eines Zeichenvorgangs und über den Doppelpuffer auch für spätere Zeichenvorgänge weiterverwendet;
    /// eine stehen gelassene Kantenglättung zeichnet Rasterlinien und Hintergründe der übrigen Zellen verschwommen bzw. versetzt.
    /// </summary>
    public static void ZellHaken(DataGridViewCellPaintingEventArgs e, Color farbe, float breite = 2.4f)
    {
        if (e.Graphics is not { } g) return;
        ZelleOhneInhalt(e);
        var r = e.CellBounds;
        var m = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var zustand = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(farbe, breite);
            g.DrawLines(pen, new[] { new Point(m.X - 5, m.Y), new Point(m.X - 1, m.Y + 4), new Point(m.X + 6, m.Y - 4) });
        }
        finally { g.Restore(zustand); }
        e.Handled = true;
    }

    /// <summary>Modusfarben einer Tabelle (beim Moduswechsel erneut aufgerufen).</summary>
    public static void RasterFarben(DataGridView g)
    {
        g.BackgroundColor = Listenhintergrund;
        g.GridColor = Rasterlinie;
        g.DefaultCellStyle.BackColor = Zelle;
        g.AlternatingRowsDefaultCellStyle.BackColor = ZelleWechsel;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = ZeileAktiv;
        g.ColumnHeadersDefaultCellStyle.BackColor = Kopf;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Kopf;
        g.DefaultCellStyle.SelectionBackColor = Vertretungsmodus ? AuswahlVertretung : BlauHell;
        g.Invalidate();
    }

    public static IEnumerable<DataGridView> Tabellen(Control c)
    {
        foreach (Control x in c.Controls)
        {
            if (x is DataGridView g) yield return g;
            foreach (var y in Tabellen(x)) yield return y;
        }
    }

    /// <summary>
    /// Direktsuche in allen Auswahllisten eines Fensters: Listen ohne freie Eingabe werden zu Listen mit Suche
    /// (tippen ergänzt den passenden Eintrag); beim Verlassen wird auf einen gültigen Eintrag zurückgesetzt.
    /// </summary>
    public static void SucheAktivieren(Control wurzel)
    {
        foreach (Control c in wurzel.Controls)
        {
            if (c is ComboBox cb) { ListenSchriftSetzen(cb); Suchliste(cb); }
            // Auswahlspalten in Tabellen ebenfalls mit Suche und Langnamen
            if (c is DataGridView g && !Steuerelemente.SuchKombi.IstAktiv(g)) Steuerelemente.SuchKombi.Aktivieren(g);
            if (c is ToolStrip ts)
                foreach (var item in ts.Items.OfType<ToolStripComboBox>()) { ListenSchriftSetzen(item.ComboBox); Suchliste(item.ComboBox); }
            if (c.HasChildren && c is not DataGridView) SucheAktivieren(c);
        }
    }

    /// <summary>Durchschaltknöpfe ◄ ► für eine Auswahlliste (vorheriges/nächstes Element, am Ende wieder von vorn).</summary>
    public static Control[] Durchschalter(ComboBox cb)
    {
        var zurueck = new Steuerelemente.PfeilKnopf(Steuerelemente.PfeilKnopf.Art.Links, "Vorheriges Element");
        var vor = new Steuerelemente.PfeilKnopf(Steuerelemente.PfeilKnopf.Art.Rechts, "Nächstes Element");
        zurueck.Click += (_, _) => Schalten(cb, -1);
        vor.Click += (_, _) => Schalten(cb, +1);
        return new Control[] { zurueck, vor };
    }

    /// <summary>Durchschaltknöpfe ‹ › für eine Auswahlliste in einer Werkzeugleiste.</summary>
    public static ToolStripItem[] Durchschalter(ToolStripComboBox cb) => new ToolStripItem[]
    {
        new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(false), (_, _) => Schalten(cb.ComboBox, -1)) { ToolTipText = "Vorheriges Element", DisplayStyle = ToolStripItemDisplayStyle.Image },
        new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(true), (_, _) => Schalten(cb.ComboBox, +1)) { ToolTipText = "Nächstes Element", DisplayStyle = ToolStripItemDisplayStyle.Image },
    };

    public static void Schalten(ComboBox cb, int richtung)
    {
        if (cb.Items.Count == 0) return;
        var i = cb.SelectedIndex < 0 ? (richtung > 0 ? 0 : cb.Items.Count - 1) : (cb.SelectedIndex + richtung + cb.Items.Count) % cb.Items.Count;
        cb.SelectedIndex = i;
    }

    public static void Suchliste(ComboBox cb)
    {
        if (cb.DropDownStyle != ComboBoxStyle.DropDownList || cb.Tag as string == "suche" || cb.DrawMode != DrawMode.Normal || Steuerelemente.ListenSuche.IstAusgenommen(cb)) return;
        cb.Tag ??= "suche";
        var letzter = cb.SelectedIndex;
        Steuerelemente.ListenSuche.Einrichten(cb);
        cb.Enter += (_, _) => letzter = cb.SelectedIndex;
        cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedIndex >= 0) letzter = cb.SelectedIndex; };
        void Pruefen()
        {
            var namen = cb.Items.Cast<object>().Select(cb.GetItemText).ToList();
            var i = Steuerelemente.ListenSuche.Treffer(cb, cb.Text);
            if (i < 0 && cb.Text.Trim().Length > 0) Steuerelemente.ListenSuche.UngueltigMelden(cb, cb.Text);
            if (i < 0) i = letzter;
            if (i >= 0 && i < cb.Items.Count) { if (cb.SelectedIndex != i) cb.SelectedIndex = i; else cb.Text = namen[i]; }
        }
        cb.Validating += (_, _) => Pruefen();
        cb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Pruefen(); e.SuppressKeyPress = true; } };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int wert, int groesse);

    /// <summary>Titelleiste eines Hauptfensters/Dialogs in der Modusfarbe (Windows 11; ältere Versionen ignorieren dies).</summary>
    public static void TitelleisteFaerben(Form f, bool dunkel = true)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !f.IsHandleCreated) return;
        try
        {
            var c = dunkel ? Titel : Kopf;
            var farbe = c.R | (c.G << 8) | (c.B << 16);
            var text = dunkel ? 0xFFFFFF : 0x000000;
            DwmSetWindowAttribute(f.Handle, 35, ref farbe, 4); // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(f.Handle, 34, ref farbe, 4); // DWMWA_BORDER_COLOR
            DwmSetWindowAttribute(f.Handle, 36, ref text, 4);  // DWMWA_TEXT_COLOR
        }
        catch { /* nicht unterstützt */ }
    }

    /// <summary>Im Selbsttest werden Fehler protokolliert statt angezeigt (keine blockierenden Meldungsfenster).</summary>
    public static List<string>? FehlerProtokoll { get; set; }

    public static void Fehler(Exception ex)
    {
        // Wiedereintritt in den Zellwechsel einer Tabelle: harmlos, der Neuaufbau wird nachgeholt – keine Meldung
        if (ex is InvalidOperationException && (ex.Message.Contains("SetCurrentCellAddressCore") || ex.StackTrace?.Contains("SetCurrentCellAddressCore") == true))
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return;
        }
        if (FehlerProtokoll != null) { FehlerProtokoll.Add(ex.ToString()); return; }
        var text = ex is InvalidOperationException or FormatException or InvalidDataException ? ex.Message : ex.ToString();
        Meldung(text, "UntPlan - Fehler", MeldungsArt.Fehler, new[] { "OK" });
    }

    /// <summary>Fehlermeldung (Text) im Design der Anwendung.</summary>
    public static void Fehler(string text, string titel = "UntPlan - Fehler", IWin32Window? owner = null)
    {
        if (FehlerProtokoll != null) { FehlerProtokoll.Add(text); return; }
        Meldung(text, titel, MeldungsArt.Fehler, new[] { "OK" }, owner: owner);
    }

    public static void Info(string text, string titel = "UntPlan", IWin32Window? owner = null)
    {
        if (InfoAntwort is { } hook) { hook(text, titel); return; }
        Meldung(text, titel, MeldungsArt.Info, new[] { "OK" }, owner: owner);
    }

    /// <summary>Warnhinweis (z. B. Raum belegt, Lehrkraft aus Veranstaltung entfernt) im Design der Anwendung.</summary>
    public static void Warnung(string text, string titel = "UntPlan - Hinweis", IWin32Window? owner = null)
    {
        if (InfoAntwort is { } hook) { hook(text, titel); return; }
        Meldung(text, titel, MeldungsArt.Warnung, new[] { "OK" }, owner: owner);
    }

    /// <summary>Hinweis-Hook (Selbsttest): nimmt Hinweise ohne modales Meldungsfenster entgegen, null = Meldungsfenster.</summary>
    public static Action<string, string>? InfoAntwort { get; set; }

    public static bool Frage(string text, string titel = "UntPlan", IWin32Window? owner = null) =>
        FrageAntwort is { } antwort ? antwort(text, titel) : Meldung(text, titel, MeldungsArt.Frage, new[] { "Ja", "Nein" }, owner: owner) == 0;

    /// <summary>
    /// Ja/Nein/Abbrechen-Frage im Design der Anwendung (z. B. „Änderungen speichern?“): Yes, No oder Cancel (auch Escape).
    /// Im Selbsttest beantwortet <see cref="AuswahlAntwort"/> bzw. <see cref="FrageAntwort"/> die Frage.
    /// </summary>
    public static DialogResult JaNeinAbbrechen(string text, string titel = "UntPlan", MeldungsArt art = MeldungsArt.Frage, int vorgabe = 0, IWin32Window? owner = null,
        string ja = "Ja", string nein = "Nein")
    {
        if (AuswahlAntwort is { } hook) return hook(text, new[] { ja, nein }) switch { 0 => DialogResult.Yes, 1 => DialogResult.No, _ => DialogResult.Cancel };
        if (FrageAntwort is { } antwort) return antwort(text, titel) ? DialogResult.Yes : DialogResult.No;
        return Meldung(text, titel, art, new[] { ja, nein }, vorgabe, true, owner) switch { 0 => DialogResult.Yes, 1 => DialogResult.No, _ => DialogResult.Cancel };
    }

    /// <summary>OK/Abbrechen-Frage im Design der Anwendung; true bei OK.</summary>
    public static bool OkAbbrechen(string text, string titel = "UntPlan", MeldungsArt art = MeldungsArt.Frage, IWin32Window? owner = null, string ok = "OK")
    {
        if (AuswahlAntwort is { } hook) return hook(text, new[] { ok }) == 0;
        if (FrageAntwort is { } antwort) return antwort(text, titel);
        return Meldung(text, titel, art, new[] { ok }, 0, true, owner) == 0;
    }

    /// <summary>Abfrage-Hook (Selbsttest): beantwortet Ja/Nein-Fragen ohne modalen Dialog, null = Meldungsfenster.</summary>
    public static Func<string, string, bool>? FrageAntwort { get; set; }

    /// <summary>Auswahl-Hook (Selbsttest): beantwortet Knopf-Fragen ohne modalen Dialog (Index des Knopfes, null = Abbrechen).</summary>
    public static Func<string, IReadOnlyList<string>, int?>? AuswahlAntwort { get; set; }

    /// <summary>
    /// Frage mit eigenen Knöpfen (z. B. „Mitnehmen / Belassen / Abbrechen“). Liefert den Index des gewählten Knopfes, null bei
    /// Abbrechen bzw. Escape. Im Selbsttest (Ja/Nein-Hook gesetzt, kein Auswahl-Hook) wird die Vorgabe gewählt.
    /// </summary>
    public static int? Auswahl(string text, string titel, IReadOnlyList<string> knoepfe, int vorgabe = 0, IReadOnlyList<bool>? aktiv = null, IWin32Window? owner = null)
    {
        if (AuswahlAntwort is { } hook) return hook(text, knoepfe);
        if (FrageAntwort != null) return vorgabe;
        return Meldung(text, titel, MeldungsArt.Frage, knoepfe, vorgabe, true, owner, aktiv);
    }

    /// <summary>Fenster der Knopf-Frage (auch für Bildschirmfotos im Selbsttest); das Ergebnis steht nach dem Schließen in <paramref name="ergebnis"/>.</summary>
    internal static Form AuswahlFenster(string text, string titel, IReadOnlyList<string> knoepfe, int vorgabe, IReadOnlyList<bool>? aktiv, System.Runtime.CompilerServices.StrongBox<int?> ergebnis) =>
        MeldungsFenster(text, titel, MeldungsArt.Frage, knoepfe, vorgabe, true, aktiv, ergebnis);

    public static string? Eingabe(string text, string titel, string vorgabe = "")
    {
        using var f = new Form { Text = titel, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 120), Font = Schrift, ShowInTaskbar = false };
        var l = new Label { Text = text, Left = 12, Top = 12, Width = 356, AutoSize = false, Height = 20 };
        var t = new TextBox { Text = vorgabe, Left = 12, Top = 36, Width = 356 };
        // Vorschläge aus den letzten Eingaben zu derselben Frage (z. B. „Text zur Stunde“)
        Steuerelemente.Textverlauf.Anbinden(t, "Eingabe:" + titel);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 200, Top = 80, Width = 80 };
        var ab = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Left = 288, Top = 80, Width = 80 };
        f.Controls.AddRange(new Control[] { l, t, ok, ab });
        f.AcceptButton = ok;
        f.CancelButton = ab;
        if (f.ShowDialog() != DialogResult.OK) return null;
        Steuerelemente.Textverlauf.Merken("Eingabe:" + titel, t.Text);
        return t.Text;
    }

    /// <summary>
    /// Datumseingabe in Listen: "30.08.", "30.8.21", "30.08.2021", "Mo 30.08.21", "+2" (Tage ab Vorgabe), "h" (heute),
    /// "" (Vorgabe). Fehlendes Jahr: das Jahr, in dem das Datum innerhalb des Schuljahres liegt.
    /// </summary>
    public static DateOnly DatumLesen(string text, DateOnly vorgabe)
    {
        var t = text.Trim();
        if (t.Length == 0) return vorgabe;
        if (t.Equals("h", StringComparison.OrdinalIgnoreCase) || t.Equals("heute", StringComparison.OrdinalIgnoreCase)) return DateOnly.FromDateTime(DateTime.Today);
        if ((t[0] is '+' or '-') && int.TryParse(t, out var tage)) return vorgabe.AddDays(tage);
        t = new string(t.SkipWhile(c => !char.IsDigit(c)).ToArray()).TrimEnd('.');
        var teile = t.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length < 2 || !int.TryParse(teile[0], out var tag) || !int.TryParse(teile[1], out var monat)) throw new FormatException();
        int jahr;
        if (teile.Length >= 3 && int.TryParse(teile[2], out var j)) jahr = j < 100 ? 2000 + j : j;
        else
        {
            var sj = AppZustand.D.Einstellungen.SchuljahrVon;
            jahr = monat >= sj.Month ? sj.Year : sj.Year + 1;
        }
        return new DateOnly(jahr, monat, tag);
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
        // Freitextfelder (Text, Bemerkung …): Vorschläge aus den zuletzt verwendeten Eingaben, je Dialog und Feld
        if (control is TextBox { ReadOnly: false, UseSystemPasswordChar: false, PasswordChar: '\0' } tb && Steuerelemente.Textverlauf.IstTextBeschriftung(text))
        {
            var feld = text.Trim().TrimEnd(':');
            Steuerelemente.Textverlauf.Anbinden(tb, () => (tb.FindForm()?.Text ?? "") + ":" + feld);
        }
        y += Math.Max(26, control.Height + 6);
        return control;
    }
}

/// <summary>
/// Zwischenspeicher für Schriften der Stundenpläne: jede Größe (auf 0,25 pt gerundet) und jeder Stil wird nur einmal angelegt.
/// Neue Schriftobjekte je Zelle und Neuzeichnen sind in GDI+ teuer; die zwischengespeicherten werden nie freigegeben.
/// </summary>
internal static class Schriftcache
{
    private static readonly Dictionary<(int, FontStyle, string), Font> Schriften = new();

    /// <summary>Schriftart des gerade gezeichneten Stundenplans (Stundenplan-Einstellungen | Allgemein | Schrift), leer = Segoe UI.</summary>
    [ThreadStatic] private static string? _familie;

    /// <summary>Schriftart für die Dauer des Zeichnens setzen (using-Block).</summary>
    public static IDisposable Familie(string? name)
    {
        var alt = _familie;
        _familie = string.IsNullOrWhiteSpace(name) ? null : name;
        return new Zuruecksetzen(() => _familie = alt);
    }

    private sealed class Zuruecksetzen(Action a) : IDisposable
    {
        public void Dispose() => a();
    }

    public static Font Holen(float groesse, FontStyle stil = FontStyle.Regular)
    {
        var key = ((int)Math.Round(Math.Max(1f, groesse) * 4), stil, _familie ?? "Segoe UI");
        lock (Schriften)
        {
            if (!Schriften.TryGetValue(key, out var f)) Schriften[key] = f = new Font(key.Item3, key.Item1 / 4f, stil);
            return f;
        }
    }
}
