using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Raum zuordnen / löschen im Vertretungsmodus (Vertretungsfenster und tagesaktueller Stundenplan).
/// <list type="bullet">
/// <item>Links: Unterricht, benötigte Kapazität, ursprünglicher Raum, Stammraum und „Auswahl derzeitiger Unterricht“ (Kopplungszeilen).</item>
/// <item>Rechts: Anwenden auf Unterrichtsstunde / Stundenblock, Langname, Auto-resize, Zusätzlichen Raum zuordnen und „Mögliche Räume“ mit Filterzeile.</item>
/// <item>Ist der Raum besetzt, folgt die Frage „Kollision! …“: nicht verplanen, mit Kollision, ohne Kollision (Raumvertretung) oder Räume tauschen.</item>
/// </list>
/// </summary>
public sealed class RaumZuordnenVertretungDialog : Dialog
{
    private sealed record Ziel(int? UnterrichtId, int? Zeile, int? VertretungId);

    private readonly DateOnly _datum;
    private readonly int _stunde;
    private readonly List<Ziel> _ziele = new();
    private readonly Label _info = new() { Left = 10, Top = 8, Width = 420, Height = 42, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(3), BackColor = Color.White };
    private readonly Label _details = new() { Left = 10, Top = 58, Width = 420, Height = 50 };
    private readonly DataGridView _links = new() { Left = 10, Top = 132, Width = 420, Height = 300, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
    private readonly RadioButton _stundeRb = new() { Text = "Unterrichtsstunde", Left = 10, Top = 18, Width = 160, Checked = true };
    private readonly RadioButton _blockRb = new() { Text = "Stundenblock", Left = 10, Top = 40, Width = 160 };
    private readonly CheckBox _lang = new() { Text = "Langname anzeigen", Left = 680, Top = 10, AutoSize = true };
    private readonly CheckBox _resize = new() { Text = "Auto-resize beim Öffnen dieses\nFensters deaktivieren", Left = 680, Top = 32, Width = 260, Height = 34 };
    private readonly CheckBox _zusatz = new() { Text = "Zusätzlichen Raum zuordnen", Left = 470, Top = 86, AutoSize = true };
    private readonly RaumListe _raeume = new("raumzuordnen") { Left = 470, Top = 132, Width = 520, Height = 300, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private bool _laedt;

    /// <summary>Anzahl der Änderungen (für den Aufrufer).</summary>
    public int Aenderungen { get; private set; }

    private static Schuldatei D => AppZustand.D;

    public static bool Zeigen(IWin32Window owner, DateOnly datum, int stunde, int? unterrichtId, int? zeile, Vertretung? v = null)
    {
        using var f = new RaumZuordnenVertretungDialog(datum, stunde, unterrichtId, zeile, v);
        if (f._ziele.Count == 0) { Ui.Info("Die Stunde findet an diesem Tag nicht statt."); return false; }
        f.ShowDialog(owner);
        return f.Aenderungen > 0;
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, DateOnly datum, int stunde, int? unterrichtId, int? zeile)
    {
        var f = new RaumZuordnenVertretungDialog(datum, stunde, unterrichtId, zeile, null);
        f.Show(owner);
        return f;
    }

    /// <summary>Für eine Zeile des Vertretungsfensters.</summary>
    public static bool Zeigen(IWin32Window owner, Vertretung v) =>
        Zeigen(owner, v.Datum, v.Stunde, v.UnterrichtId, v.UnterrichtId.HasValue ? v.ZeilenIndex : null, v);

    private RaumZuordnenVertretungDialog(DateOnly datum, int stunde, int? unterrichtId, int? zeile, Vertretung? v) : base("Raum zuordnen / löschen", 1000, 480)
    {
        _datum = datum;
        _stunde = stunde;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(820, 420);
        Abbrechen.Text = "Schließen";
        Abbrechen.Width = 110;
        Ok.Text = "Raum zuordnen";
        Ok.Image = Symbole.Get("planen", 16);
        Ok.ImageAlign = ContentAlignment.MiddleLeft;
        Ok.TextImageRelation = TextImageRelation.ImageBeforeText;
        Ok.Width = 130;
        Ok.DialogResult = DialogResult.None;
        Ok.Location = new Point(560, 444);
        Ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Abbrechen.Location = new Point(880, 444);
        AcceptButton = null;

        var tp = Tagesplan.Berechnen(D, datum);
        if (unterrichtId is int uid)
        {
            var eintraege = tp.Stunde(stunde).Where(e => e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == uid).ToList();
            foreach (var e in eintraege.OrderBy(e => (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeile ? 0 : 1))
            {
                var z = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex ?? 0;
                if (_ziele.All(x => x.Zeile != z)) _ziele.Add(new Ziel(uid, z, e.Vertretung?.Id));
            }
            // Stunde findet nicht statt (z. B. offene Raumvertretung): die Zeile selbst
            if (_ziele.Count == 0 && v != null) _ziele.Add(new Ziel(null, null, v.Id));
        }
        else if (v != null) _ziele.Add(new Ziel(null, null, v.Id));

        var anwenden = new GroupBox { Text = "Anwenden auf", Left = 470, Top = 4, Width = 190, Height = 68 };
        anwenden.Controls.AddRange(new Control[] { _stundeRb, _blockRb });
        Controls.Add(new Label { Text = "Auswahl derzeitiger Unterricht:", Left = 10, Top = 112, AutoSize = true });
        Controls.Add(new Label { Text = "Mögliche Räume:", Left = 470, Top = 112, AutoSize = true });
        var pfeil = new Button { Text = "←", Left = 436, Top = 240, Width = 28, Height = 28, Font = new Font(Ui.Schrift.FontFamily, 12, FontStyle.Bold) };
        var loeschen = new Button { Image = Symbole.Get("loeschen", 16), Left = 436, Top = 274, Width = 28, Height = 28 };
        var tip = new ToolTip();
        tip.SetToolTip(pfeil, "Markierten Raum zuordnen (Doppelklick)");
        tip.SetToolTip(loeschen, "Raum der markierten Zeile löschen (Entf)");
        tip.SetToolTip(_raeume.Raster, "Rechtsklick auf die Überschrift: Filterzeile und Spalten ein-/ausblenden");

        Ui.RasterGestalten(_links, false);
        _links.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _links.MultiSelect = false;
        _links.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[] { ("U", "Unt.", 40), ("L", "Lehrer", 56), ("R", "Raum", 52), ("K", "Klasse(n)", 80), ("G", "Gang", 50), ("S", "Statistik", 56), ("E", "Eff. U-Gruppe", 84) })
            _links.Columns[_links.Columns.Add(n, k)].Width = w;
        Controls.AddRange(new Control[] { _info, _details, _links, pfeil, loeschen, anwenden, _lang, _resize, _zusatz, _raeume });

        _resize.Checked = AppZustand.Einstellungen.RaumDialogAutoResizeAus;
        if (_resize.Checked && AppZustand.Einstellungen.RaumDialogGroesse is { Width: > 0 } gr) ClientSize = gr;
        _resize.CheckedChanged += (_, _) => { AppZustand.Einstellungen.RaumDialogAutoResizeAus = _resize.Checked; AppZustand.Einstellungen.Speichern(); };
        FormClosed += (_, _) => { if (_resize.Checked) { AppZustand.Einstellungen.RaumDialogGroesse = ClientSize; AppZustand.Einstellungen.Speichern(); } };

        _links.SelectionChanged += (_, _) => { if (!_laedt) { Info(); Rechts(); } };
        _stundeRb.CheckedChanged += (_, _) => { if (_stundeRb.Checked) { Info(); Rechts(); } };
        _blockRb.CheckedChanged += (_, _) => { if (_blockRb.Checked) { Info(); Rechts(); } };
        _lang.CheckedChanged += (_, _) => { _raeume.Langname = _lang.Checked; Alles(); };
        _raeume.Gewaehlt += _ => Zuordnen();
        pfeil.Click += (_, _) => Zuordnen();
        Ok.Click += (_, _) => Zuordnen();
        loeschen.Click += (_, _) => Loeschen();
        _links.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { Loeschen(); e.Handled = true; } };
        Alles();
    }

    private Ziel? AktuellesZiel => _ziele.ElementAtOrDefault(Math.Max(0, _links.CurrentRow?.Index ?? 0));

    private Vertretung? ZeileVon(Ziel z) => z.VertretungId is int id ? D.Vertretungen.FirstOrDefault(x => x.Id == id) : null;

    /// <summary>Tageseintrag des Ziels zur Stunde.</summary>
    private TagesEintrag? Eintrag(Ziel z, Tagesplan tp, int stunde) => tp.Stunde(stunde).FirstOrDefault(e => e.Findetstatt && Gehoert(e, z));

    private static bool Gehoert(TagesEintrag e, Ziel z) =>
        z.UnterrichtId is int u ? (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == u && (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == z.Zeile : e.Vertretung?.Id == z.VertretungId;

    private List<int> Stunden(Ziel z) => _blockRb.Checked ? Raumvertretungen.Block(D, _datum, _stunde, z.UnterrichtId, z.Zeile) : new List<int> { _stunde };

    private string K(ElementTyp t, int? id)
    {
        if (id is null) return "";
        var e = AppZustand.Doc.Index.Element(t, id.Value);
        return e is null ? "" : _lang.Checked && e.Langname.Length > 0 ? e.Langname : e.Kurzname;
    }

    private void Alles()
    {
        Links();
        Info();
        Rechts();
    }

    private void Links()
    {
        _laedt = true;
        var alt = Math.Max(0, _links.CurrentRow?.Index ?? 0);
        _links.Rows.Clear();
        var tp = Tagesplan.Berechnen(D, _datum);
        var ix = AppZustand.Doc.Index;
        foreach (var z in _ziele)
        {
            var e = Eintrag(z, tp, _stunde);
            var v = ZeileVon(z) ?? e?.Vertretung;
            var u = z.UnterrichtId is int uid ? ix.Unterrichte.GetValueOrDefault(uid) : null;
            var raum = e?.RaumIds.FirstOrDefault() is int r && r != 0 ? r : v?.VertretungsraumId;
            var gang = raum is int rr && ix.Raeume.TryGetValue(rr, out var ro) && ro.GangId is int g ? K(ElementTyp.Gang, g) : "";
            var zeile = u?.Zeilen.ElementAtOrDefault(z.Zeile ?? 0);
            var ug = (zeile?.UGruppeId ?? u?.UGruppeId) is int ugid ? D.UGruppen.FirstOrDefault(x => x.Id == ugid)?.Kurzname ?? "" : "";
            var lehrer = e?.LehrerIds ?? (v?.VertreterId is int vt ? new List<int> { vt } : new List<int>());
            var klassen = e?.KlassenIds ?? v?.NeueKlassenIds.ToList() ?? new List<int>();
            var i = _links.Rows.Add(u?.Id.ToString() ?? (v != null ? "V" + v.Id : ""), string.Join(",", lehrer.Select(l => K(ElementTyp.Lehrer, l))),
                raum is null ? "" : $"({K(ElementTyp.Raum, raum)})", string.Join(",", klassen.Select(k => K(ElementTyp.Klasse, k))), gang, u?.Statistik ?? v?.Statistik ?? "", ug);
            _links.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(240, 214, 200);
        }
        if (_links.Rows.Count > 0) _links.CurrentCell = _links.Rows[Math.Min(alt, _links.Rows.Count - 1)].Cells[0];
        _laedt = false;
    }

    private (int? Ursprung, List<int> Klassen) Basis(Ziel z)
    {
        var tp = Tagesplan.Berechnen(D, _datum);
        var e = Eintrag(z, tp, _stunde);
        var v = ZeileVon(z) ?? e?.Vertretung;
        var ursprung = v?.RaumId ?? e?.Regel?.RaumId ?? e?.RaumIds.Cast<int?>().FirstOrDefault();
        return (ursprung, e?.KlassenIds ?? v?.NeueKlassenIds.ToList() ?? new List<int>());
    }

    private void Info()
    {
        if (AktuellesZiel is not { } z) return;
        var (ursprung, klassen) = Basis(z);
        var tag = Core.Planung.Planungsdienst.TageKurz[((int)_datum.DayOfWeek + 6) % 7];
        var st = Stunden(z);
        _info.Text = $"Unterricht: {(z.UnterrichtId?.ToString() ?? "V" + z.VertretungId)}: {string.Join(", ", st.Select(s => $"{tag}-{s}"))}";
        var raum = ursprung is int r ? AppZustand.Doc.Index.Raeume.GetValueOrDefault(r) : null;
        var sr = Raumvertretungen.Stammraum(D, klassen);
        _details.Text = $"Benöt. Kapazität: {Raumvertretungen.BenoetigteKapazitaet(D, klassen, ursprung)}\n" +
                        (raum is null ? "" : $"{raum.Kurzname}. {raum.Langname}") + $"\nStammraum: {K(ElementTyp.Raum, sr)}";
    }

    private void Rechts()
    {
        if (AktuellesZiel is not { } z) return;
        var (ursprung, klassen) = Basis(z);
        _raeume.Setzen(Raumvertretungen.MoeglicheRaeume(D, _datum, Stunden(z), e => Gehoert(e, z), klassen, ursprung, true));
    }

    private void Ausfuehren(string text, Ziel z, int? raum, bool zusatz, RaumKollisionsLoesung loesung, List<int> stunden)
    {
        try
        {
            AppZustand.Doc.Aendern(text, AenderungsArt.Vertretung, d =>
            {
                if (z.UnterrichtId is int uid) Raumvertretungen.Zuordnen(d, uid, z.Zeile ?? 0, _datum, stunden, raum, zusatz, loesung, AppZustand.Benutzer);
                else if (d.Vertretungen.FirstOrDefault(x => x.Id == z.VertretungId) is { } v) Raumvertretungen.Zuordnen(d, v, raum, zusatz, loesung, AppZustand.Benutzer);
            });
            Aenderungen++;
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        Alles();
    }

    private void Zuordnen()
    {
        if (AktuellesZiel is not { } z || _raeume.Aktuell is not { } r) return;
        var stunden = Stunden(z);
        var loesung = RaumKollisionsLoesung.MitKollision;
        var belegt = r.Belegung.FirstOrDefault();
        if (belegt.Eintrag != null)
        {
            var text = Raumvertretungen.KollisionsText(D, r.Raum, _datum, belegt.Stunde, r.Belegung.Where(b => b.Stunde == belegt.Stunde).Select(b => b.Eintrag));
            var w = KollisionFragen(this, text);
            if (w is null or RaumKollisionsLoesung.NichtVerplanen) return;
            loesung = w.Value;
        }
        else if (r.Gesperrt && !Ui.Frage($"Der Raum {r.Raum.Kurzname} ist zu dieser Zeit gesperrt. Trotzdem zuordnen?")) return;
        Ausfuehren($"Raum {r.Raum.Kurzname} zuordnen", z, r.Raum.Id, _zusatz.Checked, loesung, stunden);
    }

    private void Loeschen()
    {
        if (AktuellesZiel is not { } z) return;
        Ausfuehren("Raum löschen", z, null, false, RaumKollisionsLoesung.MitKollision, Stunden(z));
    }

    /// <summary>Frage bei besetztem Raum (Optionen wie im Original).</summary>
    public static RaumKollisionsLoesung? KollisionFragen(IWin32Window owner, string text)
    {
        var w = OptionenDialog.Fragen(owner, "Frage", text, new[]
        {
            "Raum nicht verplanen", "Raum mit Kollision verplanen", "Raum ohne Kollision verplanen (erzeugt Raumvertretung)", "Räume tauschen (erzeugt zwei Raumvertretungen)",
        });
        return w is null ? null : (RaumKollisionsLoesung)w.Value;
    }
}
