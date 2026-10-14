using System.Globalization;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Unterrichtsfenster aus Sicht der Klassen, Lehrer, Fächer oder aller Unterrichte. Jede Kopplungszeile ist eine
/// eigene Zeile (über "+" in der Spalte Kl,Le auf- und zuklappbar). Mehrere Klassen durch Komma getrennt.
/// </summary>
public sealed class UnterrichtFenster : MdiFenster, IDruckbar
{
    private sealed record Spalte(string Key, string Titel, int Breite, bool Zeile, bool Standard, Func<Unterricht, Kopplungszeile, string> Get, Action<Schuldatei, Unterricht, Kopplungszeile, string>? Set, string Karte = "Unterricht", bool Bool = false);
    private sealed record ZeilenTag(Unterricht U, int Index);

    private readonly ElementTyp _sicht;
    private int? _elementId;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly ToolStripComboBox _auswahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ToolStripLabel _summe = new() { Alignment = ToolStripItemAlignment.Right };
    private readonly HashSet<int> _aufgeklappt = new();
    private bool _alleAuf;
    private bool _laden;
    private List<Spalte> _spalten = new();
    private List<string> _sichtbar = new();
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, Panel2Collapsed = true };
    private readonly TabControl _formular = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, TextBox> _formFelder = new();

    public static void Zeigen(ElementTyp sicht, int? id)
    {
        var f = HauptFenster.Oeffnen($"u:{sicht}", () => new UnterrichtFenster(sicht));
        if (id.HasValue) f.ElementSetzen(id.Value);
    }

    public UnterrichtFenster(ElementTyp sicht)
    {
        _sicht = sicht;
        HilfeThema = "Unterricht";
        Icon = SymbolIcon("unterricht");
        Size = new Size(900, 380);
        Positionieren();
        SpaltenDefinieren();
        _sichtbar = _spalten.Where(s => s.Standard).Select(s => s.Key).ToList();
        var gespeichert = D.Ansichten.FirstOrDefault(a => a.Fenster == $"Unterricht.{sicht}");
        if (gespeichert != null) _sichtbar = gespeichert.Spalten.Where(k => _spalten.Any(s => s.Key == k)).ToList();

        var ts = Werkzeugleiste();
        if (sicht != ElementTyp.Unterricht)
        {
            ts.Items.Add(_auswahl);
            _auswahl.SelectedIndexChanged += (_, _) => { if (!_laden && _auswahl.SelectedItem is ElementEintrag e) { _elementId = e.E.Id; Neuaufbau(); AppZustand.Waehlen(_sicht, e.E.Id, this); } };
            ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(false), (_, _) => Blaettern(-1)) { ToolTipText = "Vorheriges Element", DisplayStyle = ToolStripItemDisplayStyle.Image });
            ts.Items.Add(new ToolStripButton(null, Steuerelemente.PfeilKnopf.Bild(true), (_, _) => Blaettern(1)) { ToolTipText = "Nächstes Element", DisplayStyle = ToolStripItemDisplayStyle.Image });
            ts.Items.Add(new ToolStripSeparator());
        }
        ts.Items.Add(Knopf("Neuer Unterricht", "neu", NeuerUnterricht));
        ts.Items.Add(Knopf("Unterricht löschen", "loeschen", Loeschen));
        ts.Items.Add(Knopf("Kopplungszeile hinzufügen", "plus", KopplungszeileNeu));
        ts.Items.Add(Knopf("Kopplungszeile löschen", "minus", KopplungszeileLoeschen));
        ts.Items.Add(Knopf("Unterricht kopieren", "unterricht", Kopieren));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Alle Kopplungszeilen ein-/ausblenden", "uebersicht", () => { _alleAuf = !_alleAuf; _aufgeklappt.Clear(); Neuaufbau(); }));
        ts.Items.Add(Knopf("Felder der Ansicht", "rollup", FelderDialog));
        ts.Items.Add(Knopf("Zeitwünsche des Unterrichts", "zeitwunsch", () => { if (Aktuell() is { } u) ZeitwuenscheFenster.Zeigen(ElementTyp.Unterricht, u.U.Id); }));
        ts.Items.Add(Knopf("Stundenplan", "stundenplan", () => { if (_elementId.HasValue) StundenplanFenster.Neu(_sicht, _elementId, false); }));
        ts.Items.Add(Knopf("Lehrervorschlag", "lehrer", Lehrervorschlag));
        ts.Items.Add(Knopf("Automatische Unterrichtszuordnung (Lehrer für Zeilen ohne Lehrer)", "optimierung", AutomatischZuordnen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Drucken", "drucken", () => Drucken(false)));
        ts.Items.Add(Knopf("Seitenansicht", "vorschau", () => Drucken(true)));
        ts.Items.Add(_summe);

        Ui.RasterGestalten(_grid);
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.CellEndEdit += CellEndEdit;
        _grid.CellClick += CellClick;
        _grid.CellFormatting += CellFormatting;
        _grid.EditingControlShowing += EditingControlShowing;
        _grid.CurrentCellChanged += (_, _) =>
        {
            FormularFuellen();
            // gewählter Unterricht wirkt auf die geöffneten Stundenpläne (Synchronisation)
            if (!_laden && Aktuell() is { } zt) AppZustand.StundeWaehlen(zt.U, null, this);
        };
        _grid.CellMouseDown += CellMouseDown;
        _grid.DataError += (_, e) => e.ThrowException = false;
        Steuerelemente.SuchKombi.Aktivieren(_grid, (zelle, text) =>
        {
            // Langname als Zusatz in der Auswahlliste
            var typ = zelle.OwningColumn.Name switch { "Fach" => ElementTyp.Fach, "Lehrer" => ElementTyp.Lehrer, _ => ElementTyp.Raum };
            var el = D.AlleElemente(typ).FirstOrDefault(x => x.Kurzname == text);
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el is Lehrer l ? l.VollerName : el.Langname);
        });
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Klassen" || _grid.Rows[e.RowIndex].Tag is not ZeilenTag zt) return;
            var z = zt.U.Zeilen.ElementAtOrDefault(zt.Index);
            var w = AuswahlDialog.Mehrere(this, ElementTyp.Klasse, "Klassen des Unterrichts", z?.KlassenIds ?? new List<int>());
            if (w is null) return;
            _grid[e.ColumnIndex, e.RowIndex].Value = string.Join(",", w.Select(k => k.Kurzname));
            CellEndEdit(this, new DataGridViewCellEventArgs(e.ColumnIndex, e.RowIndex));
        };
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _grid.EndEdit(); };
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode) { ZelleLeeren(); e.Handled = true; } };

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = Color.FromArgb(246, 248, 251) };
        var klappen = new Button { Text = "▾", Width = 22, Height = 20, Left = 2, Top = 2, FlatStyle = FlatStyle.Flat };
        klappen.Click += (_, _) => { _split.Panel2Collapsed = !_split.Panel2Collapsed; klappen.Text = _split.Panel2Collapsed ? "▾" : "▴"; };
        unten.Controls.Add(klappen);
        unten.Controls.Add(new Label { Text = "Formularansicht", Left = 28, Top = 5, AutoSize = true, ForeColor = Color.DimGray });
        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(_formular);
        Controls.Add(_split);
        Controls.Add(unten);
        Controls.Add(ts);
        Load += (_, _) => _split.SplitterDistance = Math.Max(80, _split.Height - 150);
        FormularAufbauen();

        _elementId = sicht switch
        {
            ElementTyp.Klasse => Stammdaten.Sortiert(D, ElementTyp.Klasse).FirstOrDefault()?.Id,
            ElementTyp.Lehrer => Stammdaten.Sortiert(D, ElementTyp.Lehrer).FirstOrDefault()?.Id,
            ElementTyp.Fach => Stammdaten.Sortiert(D, ElementTyp.Fach).FirstOrDefault()?.Id,
            _ => null,
        };
        Neuaufbau();
        AppZustand.ElementGewaehlt += ElementGewaehlt;
        FormClosed += (_, _) => AppZustand.ElementGewaehlt -= ElementGewaehlt;
    }

    private sealed record ElementEintrag(Element E)
    {
        public override string ToString() => E.Kurzname;
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle != this && typ == _sicht && _sicht != ElementTyp.Unterricht) ElementSetzen(id);
    }

    public void ElementSetzen(int id)
    {
        _elementId = id;
        Neuaufbau();
    }

    private void Blaettern(int r)
    {
        if (_auswahl.Items.Count == 0) return;
        var i = Math.Clamp(_auswahl.SelectedIndex + r, 0, _auswahl.Items.Count - 1);
        _auswahl.SelectedIndex = i;
    }

    // ------------------------------------------------------------------------------------------
    // Spalten
    // ------------------------------------------------------------------------------------------

    private static int? Raum(Schuldatei d, string t) =>
        t.Trim().Length == 0 ? null : d.Raeume.FirstOrDefault(r => r.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private static int? Gruppe(Schuldatei d, string t) =>
        t.Trim().Length == 0 ? null : d.Raumgruppen.FirstOrDefault(r => r.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private static void RaumSetzen(Schuldatei d, string t, Action<int?> raum, Action<int?> gruppe)
    {
        if (t.Trim().Length == 0) { raum(null); gruppe(null); return; }
        if (Raum(d, t) is int r) { raum(r); gruppe(null); return; }
        if (Gruppe(d, t) is int g) { raum(null); gruppe(g); return; }
        throw new FormatException($"Raum oder Raumgruppe \"{t}\" ist nicht vorhanden.");
    }

    private static T ElementPflicht<T>(IEnumerable<T> liste, string t, string art) where T : Element =>
        liste.FirstOrDefault(x => x.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new FormatException($"{art} \"{t.Trim()}\" ist nicht vorhanden.");

    private void SpaltenDefinieren()
    {
        var ix = () => Ix;
        static string B(bool b) => b ? "X" : "";
        static bool Wahr(string s) => s.Equals("X", StringComparison.OrdinalIgnoreCase) || s == "True" || s == "1";
        _spalten = new List<Spalte>
        {
            new("UNr", "U-Nr", 48, false, true, (u, _) => u.Id.ToString(), null),
            new("KlLe", "Kl,Le", 56, false, true, (u, _) => u.Zeilen.Count > 1 ? $"⊞ {u.KlassenIds.Count()}, {u.LehrerIds.Count()}" : "", null),
            new("Nvpl", "Nvpl.Std.", 58, false, true, (u, _) => ix().NichtVerplant(u) is var n && n > 0 ? n.ToString() : "", null),
            new("Wst", "Wst", 38, false, true, (u, _) => u.Wochenstunden.ToString(), (d, u, z, t) => u.Wochenstunden = Math.Max(0, int.Parse(t))),
            new("Jst", "Jst", 44, false, false, (u, _) => (u.Wochenstunden * Kalender.Unterrichtswochen(AppZustand.D)).ToString(), null),
            new("Fach", "Fach", 52, true, true, (_, z) => ix().KFach(z.FachId), (d, u, z, t) =>
            {
                var f = t.Trim().Length == 0 ? null : ElementPflicht(d.Faecher, t, "Fach");
                z.FachId = f?.Id;
                if (f != null)
                {
                    // Fachraum laut Fächer-Stammdaten automatisch eintragen
                    if (z.FachraumId is null && z.FachraumgruppeId is null) { z.FachraumId = f.FachraumId; z.FachraumgruppeId = f.FachraumgruppeId; }
                    if (u.Doppelstunden.IstLeer && !f.Doppelstunden.IstLeer) u.Doppelstunden = f.Doppelstunden;
                }
            }),
            new("Lehrer", "Lehrer", 56, true, true, (_, z) => ix().KLehrer(z.LehrerId), (d, u, z, t) =>
            {
                var l = t.Trim().Length == 0 ? null : ElementPflicht(d.Lehrer, t, "Lehrer");
                z.LehrerId = l?.Id;
                if (l?.StammraumId != null && z.StammraumId is null && z.KlassenIds.Count == 0) z.StammraumId = l.StammraumId;
            }),
            new("Klassen", "Klasse(n)", 80, true, true, (_, z) => ix().KKlassen(z.KlassenIds), (d, u, z, t) =>
            {
                z.KlassenIds = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(k => ElementPflicht(d.Klassen, k, "Klasse").Id).Distinct().ToList();
                // Stammraum aus den Stammdaten der Klasse übernehmen
                if (z.StammraumId is null && z.StammraumgruppeId is null && z.KlassenIds.Count > 0)
                    z.StammraumId = d.Klassen.First(k => k.Id == z.KlassenIds[0]).StammraumId;
            }),
            new("Fachraum", "Fachraum", 62, true, true, (_, z) => z.FachraumId.HasValue ? ix().KRaum(z.FachraumId) : ix().K(ElementTyp.Raumgruppe, z.FachraumgruppeId),
                (d, u, z, t) => RaumSetzen(d, t, r => z.FachraumId = r, g => z.FachraumgruppeId = g)),
            new("Stammraum", "Stammraum", 66, true, true, (_, z) => z.StammraumId.HasValue ? ix().KRaum(z.StammraumId) : ix().K(ElementTyp.Raumgruppe, z.StammraumgruppeId),
                (d, u, z, t) => RaumSetzen(d, t, r => z.StammraumId = r, g => z.StammraumgruppeId = g)),
            new("Dopp", "Dopp.Std.", 58, false, true, (u, _) => u.Doppelstunden.ToString(), (d, u, z, t) => u.Doppelstunden = Bereich.Parse(t)),
            new("Block", "Block", 42, false, true, (u, _) => u.Block, (d, u, z, t) => u.Block = t.Trim()),
            new("Text", "Text", 80, false, true, (u, _) => u.Text, (d, u, z, t) => u.Text = t),
            new("Fix", "Fix (X)", 44, false, true, (u, _) => B(u.Fix), (d, u, z, t) => u.Fix = Wahr(t), Bool: true),
            new("Ignore", "Ignore (i)", 50, false, true, (u, _) => B(u.Ignorieren), (d, u, z, t) => u.Ignorieren = Wahr(t), Bool: true),
            new("Mehrmals", "(M)", 34, false, false, (u, _) => B(u.MehrmalsProTag), (d, u, z, t) => u.MehrmalsProTag = Wahr(t), "Kennzeichen", true),
            new("Band", "Band", 50, false, true, (u, _) => u.Band, (d, u, z, t) => u.Band = t.Trim(), "Kursplanung"),
            new("Kurs", "Kursname", 70, false, false, (u, _) => u.Kursname, (d, u, z, t) => u.Kursname = t.Trim(), "Kursplanung"),
            new("Gruppe", "Schülergruppe", 80, false, false, (u, _) => u.Schuelergruppe, (d, u, z, t) => u.Schuelergruppe = t, "Unterricht"),
            new("Zeilentext", "Zeilentext", 80, true, false, (_, z) => z.Zeilentext, (d, u, z, t) => z.Zeilentext = t, "Kopplungszeile"),
            new("Wert", "Wert", 48, true, false, (u, z) => Format.Zahl(Core.Module.Wertrechnung.Wert(AppZustand.D, ix(), u, z)),
                (d, u, z, t) => z.Wert = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Faktor", "Werte-Faktor", 70, false, false, (u, _) => u.WerteFaktor.HasValue ? Format.Zahl(u.WerteFaktor.Value) : "",
                (d, u, z, t) => u.WerteFaktor = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Periode", "Woche (A/B)", 70, false, false, (u, _) => u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => "" },
                (d, u, z, t) => u.Periodizitaet = t.Trim().ToUpperInvariant() switch { "A" => Periodizitaet.AWoche, "B" => Periodizitaet.BWoche, _ => Periodizitaet.JedeWoche }, "Mehrwochen"),
            new("UGruppe", "U-Gruppe", 66, false, false, (u, _) => AppZustand.D.UGruppen.FirstOrDefault(g => g.Id == u.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => u.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Mehrwochen"),
            new("ZUGruppe", "Zeilen-U-Gr.", 74, true, false, (_, z) => AppZustand.D.UGruppen.FirstOrDefault(g => g.Id == z.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => z.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Mehrwochen"),
            new("Von", "von", 76, false, false, (u, _) => u.VonDatum.HasValue ? Format.Datum(u.VonDatum.Value) : "",
                (d, u, z, t) => u.VonDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Mehrwochen"),
            new("Bis", "bis", 76, false, false, (u, _) => u.BisDatum.HasValue ? Format.Datum(u.BisDatum.Value) : "",
                (d, u, z, t) => u.BisDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Mehrwochen"),
            new("Statistik", "Statistik", 60, false, false, (u, _) => u.Statistik, (d, u, z, t) => u.Statistik = t, "Diverses"),
        };
    }

    // ------------------------------------------------------------------------------------------

    protected override void Aktualisieren(AenderungsArt art)
    {
        if (art == AenderungsArt.Vertretung) return;
        Neuaufbau();
    }

    private IEnumerable<Unterricht> Unterrichte()
    {
        if (_sicht == ElementTyp.Unterricht) return D.Unterrichte.OrderBy(u => u.Id);
        if (_elementId is null) return Enumerable.Empty<Unterricht>();
        return Ix.UnterrichteVon(_sicht, _elementId.Value).OrderBy(u => u.Id);
    }

    private void Neuaufbau()
    {
        _laden = true;
        var tag = _grid.CurrentRow?.Tag as ZeilenTag;
        var spalte = _grid.CurrentCell?.ColumnIndex ?? 3;
        if (_sicht != ElementTyp.Unterricht)
        {
            _auswahl.Items.Clear();
            foreach (var e in Stammdaten.Sortiert(D, _sicht)) _auswahl.Items.Add(new ElementEintrag(e));
            _auswahl.SelectedItem = _auswahl.Items.Cast<ElementEintrag>().FirstOrDefault(x => x.E.Id == _elementId);
            var el = _elementId.HasValue ? D.AlleElemente(_sicht).FirstOrDefault(e => e.Id == _elementId) : null;
            Text = el is null ? $"Unterricht / {Ui.TypName(_sicht)}" : $"{Ui.TypName(_sicht)} {el.Kurzname} ({(el is Lehrer l ? l.Langname : el.Langname)}) / {Ui.TypName(_sicht)}";
        }
        else Text = "Alle Unterrichte";
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        _grid.Columns.Clear();
        foreach (var s in _spalten.Where(s => _sichtbar.Contains(s.Key)).OrderBy(s => _sichtbar.IndexOf(s.Key)))
        {
            DataGridViewColumn c = s.Bool ? new DataGridViewCheckBoxColumn() : AuswahlListe(s.Key) is { } liste ? KombiSpalte(liste) : new DataGridViewTextBoxColumn();
            c.Name = s.Key;
            c.HeaderText = s.Titel;
            c.Width = s.Breite;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.ReadOnly = s.Set is null;
            c.Tag = s;
            if (s.Key == "KlLe") c.HeaderText = "⊞ Kl,Le";
            _grid.Columns.Add(c);
        }
        var summeWst = 0;
        var summeNv = 0;
        foreach (var u in Unterrichte())
        {
            summeWst += u.Wochenstunden;
            summeNv += Ix.NichtVerplant(u);
            var zeigen = (_alleAuf || _aufgeklappt.Contains(u.Id)) ? u.Zeilen.Count : 1;
            for (var i = 0; i < Math.Max(1, zeigen); i++) ZeileAnlegen(u, i);
        }
        // Eingabezeile
        var leer = _grid.Rows.Add();
        _grid.Rows[leer].Tag = null;
        _grid.Rows[leer].DefaultCellStyle.BackColor = Color.FromArgb(252, 252, 240);
        _grid.ResumeLayout();
        _summe.Text = $"Wst: {summeWst}   Nvpl.: {summeNv}";
        _laden = false;
        if (tag != null)
            foreach (DataGridViewRow r in _grid.Rows)
                if (r.Tag is ZeilenTag zt && zt.U.Id == tag.U.Id && zt.Index == tag.Index && spalte < _grid.Columns.Count)
                {
                    _grid.CurrentCell = r.Cells[spalte];
                    break;
                }
        FormularFuellen();
    }

    /// <summary>Auswahllisten (Dropdowns) für Fach, Lehrer und Räume.</summary>
    private List<string>? AuswahlListe(string key)
    {
        IEnumerable<string>? q = key switch
        {
            "Fach" => Stammdaten.Sortiert(D.Faecher).Select(x => x.Kurzname),
            "Lehrer" => Stammdaten.Sortiert(D.Lehrer).Select(x => x.Kurzname),
            "Fachraum" or "Stammraum" => Stammdaten.Sortiert(D.Raeume).Select(x => x.Kurzname).Concat(Stammdaten.Sortiert(D.Raumgruppen).Select(x => x.Kurzname)),
            "UGruppe" or "ZUGruppe" => D.UGruppen.Select(x => x.Kurzname),
            _ => null,
        };
        return q is null ? null : new[] { "" }.Concat(q).Distinct(StringComparer.Ordinal).ToList();
    }

    private static DataGridViewComboBoxColumn KombiSpalte(List<string> werte)
    {
        var c = new DataGridViewComboBoxColumn
        {
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, DisplayStyleForCurrentCellOnly = true, FlatStyle = FlatStyle.Flat,
            MaxDropDownItems = 20, AutoComplete = false,
        };
        c.Items.AddRange(werte.Cast<object>().ToArray());
        return c;
    }

    private void ZeileAnlegen(Unterricht u, int i)
    {
        var z = i < u.Zeilen.Count ? u.Zeilen[i] : new Kopplungszeile();
        var r = _grid.Rows.Add();
        var row = _grid.Rows[r];
        row.Tag = new ZeilenTag(u, i);
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            var s = (Spalte)c.Tag!;
            if (i > 0 && !s.Zeile) { row.Cells[c.Index].Value = s.Bool ? false : ""; row.Cells[c.Index].ReadOnly = true; continue; }
            var v = s.Get(u, z);
            row.Cells[c.Index].Value = s.Bool ? v == "X" : v;
        }
        if (i > 0) row.DefaultCellStyle.BackColor = Ui.BlauSehrHell;
        if (u.Ignorieren) row.DefaultCellStyle.ForeColor = Color.Gray;
    }

    private void CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ZeilenTag zt) return;
        var key = _grid.Columns[e.ColumnIndex].Name;
        if (key == "Fach" && zt.Index < zt.U.Zeilen.Count && zt.U.Zeilen[zt.Index].FachId is int fid && Ix.Faecher.TryGetValue(fid, out var f) && f.Hintergrund != 0)
        {
            var c = Ui.Farbe(f.Hintergrund, Color.White);
            e.CellStyle!.BackColor = c;
            e.CellStyle.ForeColor = Ui.Textfarbe(c);
        }
        if (key == "Nvpl" && e.Value is string s && s.Length > 0)
        {
            e.CellStyle!.BackColor = Color.FromArgb(255, 240, 200);
            e.CellStyle.Font = Ui.SchriftFett;
        }
        if (key == "UNr") e.CellStyle!.BackColor = zt.Index == 0 ? Ui.Erste : Ui.BlauSehrHell;
    }

    private void CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "KlLe")
        {
            _alleAuf = !_alleAuf;
            _aufgeklappt.Clear();
            Neuaufbau();
            return;
        }
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ZeilenTag zt) return;
        if (_grid.Columns[e.ColumnIndex].Name == "KlLe" && zt.U.Zeilen.Count > 1)
        {
            if (!_aufgeklappt.Remove(zt.U.Id)) _aufgeklappt.Add(zt.U.Id);
            Neuaufbau();
        }
    }

    private void CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        // Verplanung aus dem Unterrichtsfenster: Spalte "Nvpl. Std." per Drag & Drop in den Stundenplan ziehen
        if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Nvpl" &&
            _grid.Rows[e.RowIndex].Tag is ZeilenTag zt && Ix.NichtVerplant(zt.U) > 0)
            _grid.DoDragDrop("unterricht:" + zt.U.Id, DragDropEffects.Move);
    }

    private ZeilenTag? Aktuell() => _grid.CurrentRow?.Tag as ZeilenTag;

    private void CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laden || e.RowIndex < 0) return;
        var s = (Spalte)_grid.Columns[e.ColumnIndex].Tag!;
        if (s.Set is null) return;
        var v = _grid[e.ColumnIndex, e.RowIndex].Value;
        var text = v switch { bool b => b ? "X" : "", null => "", _ => v.ToString()! };
        var tag = _grid.Rows[e.RowIndex].Tag as ZeilenTag;
        if (tag is null)
        {
            if (text.Trim().Length == 0) return;
            NeuMitWert(s, text);
            return;
        }
        var ziele = _grid.SelectedCells.Cast<DataGridViewCell>().Where(c => c.ColumnIndex == e.ColumnIndex && c.OwningRow.Tag is ZeilenTag).Select(c => (ZeilenTag)c.OwningRow.Tag!).ToList();
        if (!ziele.Contains(tag)) ziele.Add(tag);
        Aendern($"Unterricht: {s.Titel}", AenderungsArt.Unterricht, d =>
        {
            foreach (var z in ziele)
            {
                var u = d.Unterrichte.First(x => x.Id == z.U.Id);
                while (u.Zeilen.Count <= z.Index) u.Zeilen.Add(new Kopplungszeile());
                s.Set(d, u, u.Zeilen[z.Index], text);
            }
        });
        NachZellwechsel(Neuaufbau);
    }

    private void NeuMitWert(Spalte s, string text)
    {
        Unterricht? neu = null;
        if (Aendern("Neuer Unterricht", AenderungsArt.Unterricht, d =>
        {
            neu = NeuerUnterrichtAnlegen(d);
            if (s.Set != null) s.Set(d, neu, neu.Zeilen[0], text);
        })) NachZellwechsel(Neuaufbau);
    }

    private Unterricht NeuerUnterrichtAnlegen(Schuldatei d)
    {
        var z = new Kopplungszeile();
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { z } };
        if (_elementId is int id)
        {
            switch (_sicht)
            {
                case ElementTyp.Klasse:
                    z.KlassenIds.Add(id);
                    z.StammraumId = d.Klassen.FirstOrDefault(k => k.Id == id)?.StammraumId;
                    break;
                case ElementTyp.Lehrer:
                    z.LehrerId = id;
                    z.StammraumId = d.Lehrer.FirstOrDefault(l => l.Id == id)?.StammraumId;
                    break;
                case ElementTyp.Fach:
                    var f = d.Faecher.FirstOrDefault(x => x.Id == id);
                    z.FachId = id;
                    z.FachraumId = f?.FachraumId;
                    z.FachraumgruppeId = f?.FachraumgruppeId;
                    if (f != null) u.Doppelstunden = f.Doppelstunden;
                    break;
            }
        }
        d.Unterrichte.Add(u);
        return u;
    }

    private void NeuerUnterricht()
    {
        if (Aendern("Neuer Unterricht", AenderungsArt.Unterricht, d => NeuerUnterrichtAnlegen(d)))
        {
            Neuaufbau();
            var letzte = _grid.Rows.Cast<DataGridViewRow>().LastOrDefault(r => r.Tag is ZeilenTag);
            if (letzte != null && _grid.Columns.Contains("Wst")) _grid.CurrentCell = letzte.Cells["Wst"];
        }
    }

    private void Loeschen()
    {
        var ids = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<ZeilenTag>().Select(t => t.U.Id).Distinct().ToList();
        if (ids.Count == 0) return;
        if (!Ui.Frage($"{ids.Count} Unterricht(e) samt verplanter Stunden löschen?")) return;
        if (Aendern("Unterricht löschen", AenderungsArt.Unterricht, d => ids.ForEach(id => Stammdaten.UnterrichtLoeschen(d, id)))) Neuaufbau();
    }

    private void KopplungszeileNeu()
    {
        if (Aktuell() is not { } t) return;
        if (Aendern("Kopplungszeile hinzufügen", AenderungsArt.Unterricht, d =>
        {
            var u = d.Unterrichte.First(x => x.Id == t.U.Id);
            var vorlage = u.Zeilen.FirstOrDefault();
            u.Zeilen.Add(new Kopplungszeile { KlassenIds = vorlage?.KlassenIds.ToList() ?? new(), FachId = vorlage?.FachId });
            foreach (var p in d.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id)) p.Raeume.Add(null);
        }))
        {
            _aufgeklappt.Add(t.U.Id);
            Neuaufbau();
        }
    }

    private void KopplungszeileLoeschen()
    {
        if (Aktuell() is not { } t || t.U.Zeilen.Count <= 1) return;
        var idx = t.Index;
        if (Aendern("Kopplungszeile löschen", AenderungsArt.Unterricht, d =>
        {
            var u = d.Unterrichte.First(x => x.Id == t.U.Id);
            u.Zeilen.RemoveAt(idx);
            foreach (var p in d.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id && p.Raeume.Count > idx)) p.Raeume.RemoveAt(idx);
        })) Neuaufbau();
    }

    private void Kopieren()
    {
        if (Aktuell() is not { } t) return;
        if (Aendern("Unterricht kopieren", AenderungsArt.Unterricht, d => d.Unterrichte.Add(d.Unterrichte.First(x => x.Id == t.U.Id).Kopie(d.NeueId())))) Neuaufbau();
    }

    private void ZelleLeeren()
    {
        if (_grid.CurrentCell is null || _grid.CurrentRow?.Tag is not ZeilenTag) return;
        var s = (Spalte)_grid.CurrentCell.OwningColumn.Tag!;
        if (s.Set is null || s.Key == "Wst") return;
        _grid.CurrentCell.Value = s.Bool ? false : "";
        CellEndEdit(this, new DataGridViewCellEventArgs(_grid.CurrentCell.ColumnIndex, _grid.CurrentCell.RowIndex));
    }

    private void EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox tb) return;
        var key = _grid.CurrentCell!.OwningColumn.Name;
        var quelle = key switch
        {
            "Fach" => D.Faecher.Select(x => x.Kurzname),
            "Lehrer" => D.Lehrer.Select(x => x.Kurzname),
            "Fachraum" or "Stammraum" => D.Raeume.Select(x => x.Kurzname).Concat(D.Raumgruppen.Select(x => x.Kurzname)),
            _ => null,
        };
        if (quelle is null) { tb.AutoCompleteMode = AutoCompleteMode.None; return; }
        var src = new AutoCompleteStringCollection();
        src.AddRange(quelle.ToArray());
        tb.AutoCompleteCustomSource = src;
        tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
        tb.AutoCompleteMode = AutoCompleteMode.Append;
    }

    private void FelderDialog()
    {
        var felder = _spalten.Select(s => new Feld { Schluessel = s.Key, Titel = s.Titel, Karteikarte = s.Karte }).ToList();
        var neu = FelderAuswahlDialog.Zeigen(this, felder, _sichtbar);
        if (neu is null) return;
        _sichtbar = neu;
        var sicht = _sicht;
        Aendern("Ansicht Unterricht", AenderungsArt.Einstellungen, d =>
        {
            var a = d.Ansichten.FirstOrDefault(x => x.Fenster == $"Unterricht.{sicht}");
            if (a is null) d.Ansichten.Add(a = new Ansicht { Fenster = $"Unterricht.{sicht}", Name = "Standard" });
            a.Spalten = neu;
        });
        Neuaufbau();
    }

    private void AutomatischZuordnen()
    {
        var nurAnzeige = _sicht != ElementTyp.Unterricht && Ui.Frage("Nur die Unterrichte dieser Ansicht zuordnen?\n(Nein = alle Unterrichte der Schule)");
        var ids = nurAnzeige ? Unterrichte().Select(u => u.Id).ToHashSet() : null;
        var offen = D.Unterrichte.Where(u => ids is null || ids.Contains(u.Id)).Sum(u => u.Zeilen.Count(z => z.LehrerId is null && z.FachId.HasValue));
        if (offen == 0) { Ui.Info("Alle Kopplungszeilen haben bereits einen Lehrer."); return; }
        var ueber = Ui.Frage($"{offen} Kopplungszeilen ohne Lehrer.\n\nDarf das Deputat (Soll) eines Lehrers dabei überschritten werden?");
        List<(Unterricht U, int Zeile, Lehrer Lehrer)> erg = new();
        if (!Aendern("Automatische Unterrichtszuordnung", AenderungsArt.Unterricht, d =>
            erg = Core.Module.Wertrechnung.AutomatischZuordnen(d, ids is null ? null : d.Unterrichte.Where(u => ids.Contains(u.Id)), ueber))) return;
        Neuaufbau();
        var liste = string.Join("\n", erg.Take(25).Select(x => $"U{x.U.Id} {Ix.UnterrichtText(x.U)} → {x.Lehrer.Kurzname}"));
        Ui.Info($"{erg.Count} von {offen} Zeilen wurde ein Lehrer zugeordnet (Lehrbefähigung, freies Deputat, Klassenlehrer/kennt Klasse).\n\n{liste}{(erg.Count > 25 ? "\n…" : "")}", "Automatische Unterrichtszuordnung");
    }

    private void Lehrervorschlag()
    {
        if (Aktuell() is not { } t) return;
        var z = t.U.Zeilen.ElementAtOrDefault(t.Index) ?? t.U.Zeilen[0];
        var liste = Core.Module.Wertrechnung.Lehrervorschlag(D, t.U, z);
        using var dlg = new Dialog($"Lehrervorschlag für U-Nr. {t.U.Id} ({Ix.UnterrichtText(t.U)})", 520, 380);
        var g = new DataGridView { Left = 10, Top = 10, Width = 500, Height = 320 };
        Ui.RasterGestalten(g, false);
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.RowHeadersVisible = false;
        g.Columns.Add("L", "Lehrer");
        g.Columns.Add("N", "Name");
        g.Columns.Add("F", "Fachlehrer");
        g.Columns.Add("K", "Kennt Klasse");
        g.Columns.Add("R", "Freies Deputat");
        foreach (var x in liste) { var r = g.Rows.Add(x.Lehrer.Kurzname, x.Lehrer.Langname, x.Fach ? "x" : "", x.KenntKlasse ? "x" : "", Format.Zahl(x.Frei)); g.Rows[r].Tag = x.Lehrer; }
        dlg.Controls.Add(g);
        if (dlg.ShowDialog(this) != DialogResult.OK || g.CurrentRow?.Tag is not Lehrer l) return;
        var idx = t.Index;
        if (Aendern("Lehrervorschlag übernehmen", AenderungsArt.Unterricht, d => d.Unterrichte.First(u => u.Id == t.U.Id).Zeilen[idx].LehrerId = l.Id)) Neuaufbau();
    }

    // ------------------------------------------------------------------------------------------
    // Formularansicht
    // ------------------------------------------------------------------------------------------

    private void FormularAufbauen()
    {
        foreach (var gruppe in _spalten.Where(s => s.Set != null).GroupBy(s => s.Zeile ? "Kopplungszeile" : s.Karte))
        {
            var page = new TabPage(gruppe.Key) { BackColor = Color.White };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true, AutoScroll = true };
            foreach (var s in gruppe)
            {
                var p = new Panel { Width = 240, Height = 26 };
                p.Controls.Add(new Label { Text = s.Titel, Left = 0, Top = 4, Width = 90 });
                var tb = new TextBox { Left = 92, Top = 1, Width = 140 };
                var spalte = s;
                tb.Leave += (_, _) => FormularWert(spalte, tb.Text);
                tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { FormularWert(spalte, tb.Text); e.SuppressKeyPress = true; } };
                p.Controls.Add(tb);
                _formFelder[s.Key] = tb;
                flow.Controls.Add(p);
            }
            page.Controls.Add(flow);
            _formular.TabPages.Add(page);
        }
    }

    private bool _formLaden;

    private void FormularFuellen()
    {
        _formLaden = true;
        var t = Aktuell();
        foreach (var s in _spalten)
        {
            if (!_formFelder.TryGetValue(s.Key, out var tb)) continue;
            tb.Text = t is null ? "" : s.Get(t.U, t.U.Zeilen.ElementAtOrDefault(t.Index) ?? new Kopplungszeile());
        }
        _formLaden = false;
    }

    private void FormularWert(Spalte s, string text)
    {
        if (_formLaden || Aktuell() is not { } t || s.Set is null) return;
        var z = t.U.Zeilen.ElementAtOrDefault(t.Index) ?? new Kopplungszeile();
        if (s.Get(t.U, z) == text) return;
        var idx = t.Index;
        if (Aendern($"Unterricht: {s.Titel}", AenderungsArt.Unterricht, d =>
        {
            var u = d.Unterrichte.First(x => x.Id == t.U.Id);
            while (u.Zeilen.Count <= idx) u.Zeilen.Add(new Kopplungszeile());
            s.Set(d, u, u.Zeilen[idx], text);
        })) Neuaufbau();
    }

    public void Drucken(bool vorschau)
    {
        var spalten = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var zeilen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is ZeilenTag)
            .Select(r => spalten.Select(c => r.Cells[c.Index].Value switch { bool b => b ? "X" : "", var v => v?.ToString() ?? "" }).ToArray()).ToList();
        ListenDruck.Drucken(this, Text, spalten.Select(c => c.HeaderText).ToArray(), spalten.Select(c => c.Width).ToArray(), zeilen, vorschau);
    }
}
