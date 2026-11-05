using System.Globalization;
using UntPlan.App.Dialoge;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Unterrichtsfenster aus Sicht der Klassen, Lehrer, Fächer oder aller Unterrichte. Jede Kopplungszeile ist eine
/// eigene Zeile (über "+" in der Spalte Kl,Le auf- und zuklappbar). Mehrere Klassen durch Komma getrennt.
/// <para>
/// Eingabe (UP5): deutlich markierte Neuanlage-Zeile „+ neu“, Auswahllisten mit Langnamen (Klassen als Mehrfachauswahl über den
/// Pfeil bzw. Alt+↓), Enter/Tab springen zum nächsten Feld derselben Zeile, jede Eingabe wird sofort geprüft (unbekannte Kürzel,
/// fehlende Pflichtfelder) und in der Hinweisleiste verständlich erklärt; Esc verwirft die Eingabe, „Rückgängig“ (Strg+Z) die
/// letzte Änderung. Nach dem Speichern bleibt die Eingabeposition erhalten (keine Sprünge).
/// </para>
/// </summary>
public sealed class UnterrichtFenster : MdiFenster, IDruckbar
{
    private sealed record Spalte(string Key, string Titel, int Breite, bool Zeile, bool Standard, Func<Unterricht, Kopplungszeile, string> Get, Action<Schuldatei, Unterricht, Kopplungszeile, string>? Set, string Karte = "Unterricht", bool Bool = false);
    private sealed record ZeilenTag(Unterricht U, int Index);

    private readonly ElementTyp _sicht;
    private int? _elementId;
    private readonly EingabeRaster _grid = new() { Dock = DockStyle.Fill };
    /// <summary>Hinweisleiste unter der Tabelle: Prüfergebnis, gespeicherte Änderung, fehlende Pflichtfelder.</summary>
    private readonly Panel _hinweisLeiste = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 4, 4, 0) };
    private readonly Label _hinweis = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly ToolStripButton _rueckgaengig;
    /// <summary>Gerade über die Neuanlage-Zeile angelegter Unterricht (Eingabeposition nach dem Neuaufbau).</summary>
    private int? _neuId;
    /// <summary>Hinweis nach einer Änderung bleibt stehen, solange dieser Unterricht gewählt ist (U-Nr., -1 = Neuanlage-Zeile).</summary>
    private int? _hinweisFestUid;

    /// <summary>Tabelle, in der Enter (wie Tab) zum nächsten Eingabefeld derselben Zeile springt.</summary>
    private sealed class EingabeRaster : DataGridView
    {
        public Action<bool>? Weiter { get; set; }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter && (keyData & (Keys.Control | Keys.Alt)) == 0 && Weiter != null)
            {
                Weiter((keyData & Keys.Shift) != 0);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt && Weiter != null)
            {
                Weiter(e.Shift);
                return true;
            }
            return base.ProcessDataGridViewKey(e);
        }
    }

    private enum HinweisArt { Info, Ok, Warnung }

    /// <summary>Hinweis in der Leiste unter der Tabelle (grün = gespeichert, gelb = bitte prüfen, grau = Tipp).</summary>
    private void Hinweis(string text, HinweisArt art, bool fest = false, int? uid = null)
    {
        _hinweis.Text = (art == HinweisArt.Warnung ? "Bitte prüfen: " : "") + text;
        _hinweis.ForeColor = art == HinweisArt.Warnung ? Color.FromArgb(120, 70, 0) : art == HinweisArt.Ok ? Color.FromArgb(20, 100, 30) : Color.FromArgb(60, 60, 60);
        _hinweisLeiste.BackColor = art switch { HinweisArt.Ok => Color.FromArgb(226, 244, 226), HinweisArt.Warnung => Color.FromArgb(255, 243, 205), _ => Color.FromArgb(240, 244, 248) };
        _hinweisFestUid = fest ? uid ?? Aktuell()?.U.Id ?? -1 : null;
    }

    /// <summary>Selbsttest: zuletzt geprüfte Eingabe.</summary>
    internal string LetztePruefung { get; private set; } = "";

    /// <summary>Selbsttest: aktueller Text der Hinweisleiste.</summary>
    internal string HinweisText => _hinweis.Text;

    /// <summary>Selbsttest: Neuanlage-Zeile bzw. erste Zeile eines Unterrichts wählen (Spalte).</summary>
    internal void SelbsttestWaehlen(int? unterrichtId, string spalte, int zeile = 0)
    {
        if (!_grid.Columns.Contains(spalte)) throw new InvalidOperationException($"Spalte {spalte} ist nicht sichtbar.");
        var r = unterrichtId is int uid
            ? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag is ZeilenTag zt && zt.U.Id == uid && zt.Index == zeile)
            : _grid.Rows.Cast<DataGridViewRow>().LastOrDefault(x => x.Tag is null);
        if (r is null) throw new InvalidOperationException($"Zeile für U-Nr. {unterrichtId} nicht gefunden.");
        _grid.Focus();
        _grid.ClearSelection();
        _grid.CurrentCell = r.Cells[spalte];
    }

    /// <summary>
    /// Selbsttest: Text in die aktuelle Zelle tippen und Enter drücken (Prüfung, Übernahme, Sprung zum nächsten Feld).
    /// Liefert true, wenn die Eingabe übernommen wurde; bei abgelehnter Eingabe bleibt die Zelle in Bearbeitung.
    /// </summary>
    internal bool SelbsttestEingabe(string text)
    {
        if (_grid.CurrentCell is null) throw new InvalidOperationException("Keine Zelle gewählt.");
        _grid.BeginEdit(true);
        switch (_grid.EditingControl)
        {
            case ComboBox cb:
                cb.Text = text;
                _grid.NotifyCurrentCellDirty(true);
                break;
            case TextBox tb:
                tb.Text = text;
                _grid.NotifyCurrentCellDirty(true);
                break;
            default: throw new InvalidOperationException("Zelle lässt sich nicht bearbeiten.");
        }
        Weiter(false);
        return !_grid.IsCurrentCellInEditMode;
    }

    /// <summary>Selbsttest: Eingabe verwerfen (Esc).</summary>
    internal void SelbsttestVerwerfen()
    {
        _grid.CancelEdit();
        _grid.EndEdit();
    }

    /// <summary>Selbsttest: aktuelle Position (U-Nr. bzw. null = Neuanlage-Zeile, Spalte, Kopplungszeile).</summary>
    internal (int? Unterricht, string Spalte, int Zeile) SelbsttestPosition =>
        (Aktuell()?.U.Id, _grid.CurrentCell is { } c ? _grid.Columns[c.ColumnIndex].Name : "", Aktuell()?.Index ?? -1);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    /// <summary>Selbsttest: Mausklick (gedrückt/losgelassen) in eine Zelle wie vom Benutzer – über Fenstermeldungen an die Tabelle.</summary>
    private void SelbsttestKlick(DataGridViewCell zelle, Action warten)
    {
        var r = _grid.GetCellDisplayRectangle(zelle.ColumnIndex, zelle.RowIndex, false);
        if (r.Width <= 0) { _grid.FirstDisplayedScrollingRowIndex = zelle.RowIndex; warten(); r = _grid.GetCellDisplayRectangle(zelle.ColumnIndex, zelle.RowIndex, false); }
        var lp = (IntPtr)(((r.Y + r.Height / 2) << 16) | ((r.X + r.Width / 2) & 0xFFFF));
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, MK_LBUTTON = 1;
        PostMessage(_grid.Handle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
        PostMessage(_grid.Handle, WM_LBUTTONUP, IntPtr.Zero, lp);
        warten();
    }

    /// <summary>
    /// Selbsttest „Zeilenwechsel“: Auswahlliste (Lehrer) in Zeile A ändern, dann per Mausklick bzw. Pfeiltaste in Zeile B wechseln –
    /// Zeile A hat den gewählten Wert, Zeile B bleibt unverändert (auch mit getipptem Suchtext und offener Liste).
    /// </summary>
    internal string SelbsttestZeilenwechsel(Action warten)
    {
        if (!_grid.Columns.Contains("Lehrer")) throw new InvalidOperationException("Spalte Lehrer fehlt.");
        var spalte = _grid.Columns["Lehrer"]!.Index;
        var berichte = new List<string>();
        int? LehrerVon(int uid) => D.Unterrichte.First(u => u.Id == uid).Zeilen[0].LehrerId;
        DataGridViewRow? Zeile(int uid) => _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ZeilenTag zt && zt.U.Id == uid && zt.Index == 0);
        (int A, int B)? Paar()
        {
            var zeilen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is ZeilenTag { Index: 0 } zt && zt.U.Zeilen.Count == 1 && !r.Cells[spalte].ReadOnly && zt.U.LehrerIds.Any()).ToList();
            for (var i = 0; i + 1 < zeilen.Count; i++)
                if (((ZeilenTag)zeilen[i].Tag!).U.Zeilen[0].LehrerId != ((ZeilenTag)zeilen[i + 1].Tag!).U.Zeilen[0].LehrerId)
                    return (((ZeilenTag)zeilen[i].Tag!).U.Id, ((ZeilenTag)zeilen[i + 1].Tag!).U.Id);
            return null;
        }
        // freie Lehrkraft (nicht A, nicht B), damit keine Behinderungsabfrage die Eingabe aufhält
        Lehrer Andere(params int?[] nicht) => D.Lehrer.Where(l => !nicht.Contains(l.Id) && !l.Ignorieren).OrderBy(l => D.Unterrichte.Count(u => u.LehrerIds.Contains(l.Id))).First();
        void Abbrechen()
        {
            if (_grid.EditingControl is ComboBox { DroppedDown: true } offen) offen.DroppedDown = false;
            if (_grid.IsCurrentCellInEditMode) { _grid.CancelEdit(); _grid.EndEdit(); }
            warten();
        }
        foreach (var art in new[] { "Maus", "Maus mit Suchtext", "Maus bei offener Liste", "Pfeiltaste" })
        {
            Abbrechen();
            if (Paar() is not { } p) throw new InvalidOperationException("Keine zwei Unterrichte mit verschiedenen Lehrkräften.");
            var (lehrerA, lehrerB) = (LehrerVon(p.A), LehrerVon(p.B));
            var neu = Andere(lehrerA, lehrerB);
            var zeileA = Zeile(p.A)!;
            _grid.Focus();
            _grid.ClearSelection();
            _grid.CurrentCell = zeileA.Cells[spalte];
            zeileA.Cells[spalte].Selected = true;
            _grid.BeginEdit(false);
            warten();
            if (_grid.EditingControl is not ComboBox cb) throw new InvalidOperationException("Keine Auswahlliste in der Spalte Lehrer.");
            if (cb.Text != Ix.KLehrer(lehrerA)) throw new InvalidOperationException($"{art}: Auswahlliste zeigt „{cb.Text}“ statt „{Ix.KLehrer(lehrerA)}“ (Wert einer anderen Zeile).");
            cb.Focus();
            var ziel = neu.Kurzname;
            switch (art)
            {
                case "Maus mit Suchtext":
                    // Tippen wie der Benutzer: Kurzname ohne letzten Buchstaben (Treffer wird beim Verlassen übernommen)
                    foreach (var ch in neu.Kurzname) SendMessage(cb.Handle, 0x0102, (IntPtr)ch, IntPtr.Zero);
                    warten();
                    break;
                case "Maus bei offener Liste":
                    cb.DroppedDown = true;
                    warten();
                    cb.SelectedIndex = cb.Items.IndexOf(neu.Kurzname);
                    cb.DroppedDown = false;
                    break;
                default:
                    cb.SelectedIndex = cb.Items.IndexOf(neu.Kurzname);
                    break;
            }
            warten();
            var zeileB = Zeile(p.B) ?? throw new InvalidOperationException("Zeile B verschwunden.");
            if (art == "Pfeiltaste")
            {
                // Pfeiltaste nach unten: Wechsel in die nächste Zeile (B steht direkt unter A)
                if (zeileB.Index != Zeile(p.A)!.Index + 1) { Abbrechen(); continue; }
                // Taste an das Steuerelement mit dem Fokus (die Auswahlliste in Bearbeitung) wie vom Benutzer
                var fokus = GetFocus();
                PostMessage(fokus == IntPtr.Zero ? _grid.Handle : fokus, 0x0100, (IntPtr)0x28, IntPtr.Zero);
                PostMessage(fokus == IntPtr.Zero ? _grid.Handle : fokus, 0x0101, (IntPtr)0x28, IntPtr.Zero);
                warten();
                if (_grid.CurrentRow?.Tag is not ZeilenTag { } nachTaste || nachTaste.U.Id != p.B)
                    throw new InvalidOperationException($"Pfeiltaste: aktuelle Zeile ist U{(_grid.CurrentRow?.Tag as ZeilenTag)?.U.Id} statt U{p.B}.");
            }
            else SelbsttestKlick(zeileB.Cells[spalte], warten);
            warten();
            var nachA = LehrerVon(p.A);
            var nachB = LehrerVon(p.B);
            var anzeigeB = Zeile(p.B)?.Cells[spalte].Value?.ToString() ?? "";
            var editorB = _grid.IsCurrentCellInEditMode && _grid.CurrentRow?.Tag is ZeilenTag { } aktB && aktB.U.Id == p.B && _grid.EditingControl is ComboBox eb ? eb.Text : null;
            Abbrechen();
            if (nachB != lehrerB) throw new InvalidOperationException($"{art}: Zeile B (U{p.B}) hat jetzt {Ix.KLehrer(nachB)} statt {Ix.KLehrer(lehrerB)} – Wert der vorherigen Zeile übernommen!");
            if (anzeigeB != Ix.KLehrer(lehrerB)) throw new InvalidOperationException($"{art}: Zeile B zeigt „{anzeigeB}“ statt „{Ix.KLehrer(lehrerB)}“.");
            if (editorB != null && editorB != Ix.KLehrer(lehrerB)) throw new InvalidOperationException($"{art}: Auswahlliste in Zeile B zeigt „{editorB}“ statt „{Ix.KLehrer(lehrerB)}“.");
            if (nachA != neu.Id && nachA != lehrerA) throw new InvalidOperationException($"{art}: Zeile A (U{p.A}) hat {Ix.KLehrer(nachA)} (weder {ziel} noch {Ix.KLehrer(lehrerA)}).");
            if (art is "Maus" or "Pfeiltaste" && nachA != neu.Id) throw new InvalidOperationException($"{art}: Zeile A (U{p.A}) hat {Ix.KLehrer(nachA)} statt {ziel}.");
            berichte.Add($"{art}: A U{p.A} {Ix.KLehrer(lehrerA)}→{Ix.KLehrer(nachA)}, B U{p.B} bleibt {Ix.KLehrer(nachB)}");
        }
        return "Zeilenwechsel: " + string.Join("; ", berichte);
    }

    /// <summary>
    /// Selbsttest: Wert in einer Zeile in der Mitte ändern – die Liste wird nicht neu geladen, die Zeile bleibt an ihrem Platz,
    /// oberste sichtbare Zeile und Reihenfolge bleiben.
    /// </summary>
    internal string SelbsttestStabil(Action warten)
    {
        var zeilen = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is ZeilenTag { Index: 0 }).ToList();
        if (zeilen.Count < 3) return $"zu wenige Zeilen ({zeilen.Count})";
        if (!_grid.Columns.Contains("Text")) throw new InvalidOperationException("Spalte Text fehlt.");
        var zeile = zeilen[zeilen.Count / 2];
        var uid = ((ZeilenTag)zeile.Tag!).U.Id;
        _grid.Focus();
        _grid.ClearSelection();
        _grid.CurrentCell = zeile.Cells["Text"];
        try { if (_grid.Rows.Count > 6) _grid.FirstDisplayedScrollingRowIndex = 1; } catch (InvalidOperationException) { }
        warten();
        var index = zeile.Index;
        var erste = _grid.FirstDisplayedScrollingRowIndex;
        var ersteSchluessel = erste >= 0 ? Schluessel(_grid.Rows[erste]) : null;
        var reihenfolge = _grid.Rows.Cast<DataGridViewRow>().Select(r => Schluessel(r)).ToList();
        var neuaufbauten = Steuerelemente.TabellenAbgleich.Neuaufbauten;
        // Enter übernimmt und springt zum nächsten Feld (das ggf. gleich in Bearbeitung geht) – dort nichts eingeben
        SelbsttestEingabe("Selbsttest stabil");
        warten();
        if (_grid.IsCurrentCellInEditMode) SelbsttestVerwerfen();
        warten();
        if (D.Unterrichte.First(u => u.Id == uid).Text != "Selbsttest stabil") throw new InvalidOperationException("Text nicht gespeichert.");
        if (Steuerelemente.TabellenAbgleich.Neuaufbauten != neuaufbauten) throw new InvalidOperationException("Liste wurde nach dem Ändern ganz neu aufgebaut.");
        var jetzt = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ZeilenTag zt && zt.U.Id == uid && zt.Index == 0);
        if (jetzt is null || jetzt.Index != index) throw new InvalidOperationException($"Zeile ist verrutscht ({index} → {jetzt?.Index}).");
        if (Aktuell()?.U.Id != uid) throw new InvalidOperationException("Aktuelle Zeile verloren.");
        var e2 = _grid.FirstDisplayedScrollingRowIndex;
        if (ersteSchluessel != null && (e2 < 0 || Schluessel(_grid.Rows[e2]) != ersteSchluessel)) throw new InvalidOperationException("Bildlauf verändert.");
        if (!_grid.Rows.Cast<DataGridViewRow>().Select(r => Schluessel(r)).SequenceEqual(reihenfolge)) throw new InvalidOperationException("Reihenfolge verändert.");
        return $"U{uid}: Text geändert – Zeile {index}, Bildlauf und Reihenfolge blieben (kein Neuaufbau)";
    }

    /// <summary>Selbsttest: Pflichtfeld-Hinweis (Fehlersymbol) einer Zelle der ersten Zeile eines Unterrichts.</summary>
    internal string SelbsttestFehlertext(int unterrichtId, string spalte) =>
        _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ZeilenTag zt && zt.U.Id == unterrichtId && zt.Index == 0)?.Cells[spalte].ErrorText ?? "(keine Zeile)";

    /// <summary>Selbsttest: Kopplungszeile zum aktuellen Unterricht hinzufügen.</summary>
    internal void SelbsttestKopplungszeile() => KopplungszeileNeu();
    private readonly ToolStripComboBox _auswahl = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ToolStripLabel _summe = new() { Alignment = ToolStripItemAlignment.Right };
    private readonly NumericUpDown _unrFeld = new() { Width = 70, Minimum = 0, Maximum = 999999 };
    private bool _unrLaden;
    private readonly HashSet<int> _aufgeklappt = new();
    private bool _alleAuf;
    private bool _laden;
    private List<Spalte> _spalten = new();
    private List<string> _sichtbar = new();
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, Panel2Collapsed = true };
    private readonly TabControl _formular = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, TextBox> _formFelder = new();
    /// <summary>Einstellungen: „Eine Woche“ (nur Unterrichte dieser Woche) und „Vererbte Kennzeichen anzeigen“.</summary>
    private bool _eineWoche;
    private bool _vererbte = true;
    private readonly DateTimePicker _woche = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.", Width = 70, Visible = false, Name = "EineWoche" };
    private readonly NumericUpDown _wocheDreh = new() { Width = 18, Minimum = -1, Maximum = 1, Visible = false };

    /// <summary>Doppelklick auf eine Unterrichtsnummer (1. Spalte): Eingabe in geöffnete Dialoge (Koppeln, Unterrichtsfolgen).</summary>
    public static event Action<int>? UnterrichtsnummerGewaehlt;

    /// <summary>Gibt es Empfänger für den Doppelklick auf die Unterrichtsnummer?</summary>
    private static bool NummerWirdErwartet => UnterrichtsnummerGewaehlt != null;

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
        // Symbolleiste wie im Handbuch (3.5): Elementauswahl | Normalform anzeigen, Felder der Ansicht, Neu, Löschen |
        // Koppeln, Erweitertes Entkoppeln, Lehrer-Vorschlag | Zeitwünsche, Schuljahreskalender | Seitenlayout, Einstellungen, Aktualisieren
        ts.Items.Add(Knopf("Normalform anzeigen", "normalform", Normalform));
        ts.Items.Add(Knopf("Felder der Ansicht", "uebersicht", FelderDialog));
        ts.Items.Add(Knopf("Neu", "neu", NeuerUnterricht));
        ts.Items.Add(Knopf("Löschen", "loeschen", Loeschen));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Koppeln", "plus", Koppeln));
        ts.Items.Add(Knopf("Erweitertes Entkoppeln", "minus", ErweitertesEntkoppeln));
        ts.Items.Add(Knopf("Lehrer-Vorschlag", "lehrer", Lehrervorschlag));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Zeitwünsche", "zeitwunsch", () => { if (Aktuell() is { } u) ZeitwuenscheFenster.Zeigen(ElementTyp.Unterricht, u.U.Id); }));
        ts.Items.Add(Knopf("Schuljahreskalender", "kalender", () => HauptFenster.Oeffnen<FerienFenster>("ferien", () => new FerienFenster())));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Unterrichtsvergleich", "perioden", Unterrichtsvergleich));
        ts.Items.Add(Knopf("Unterricht als Kurs übernehmen", "kurs", AlsKursUebernehmen));
        ts.Items.Add(new ToolStripSeparator());
        var layout = new ToolStripSplitButton("Seitenlayout", Symbole.Get("vorschau", 16)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Seitenlayout" };
        layout.ButtonClick += (_, _) => Drucken(true);
        layout.DropDownItems.Add("Seitenlayout", Symbole.Get("vorschau", 16), (_, _) => Drucken(true));
        layout.DropDownItems.Add("Drucken", Symbole.Get("drucken", 16), (_, _) => Drucken(false));
        ts.Items.Add(layout);
        ts.Items.Add(Knopf("Einstellungen", "einstellungen", EinstellungenDialog));
        ts.Items.Add(Knopf("Aktualisieren (F5)", "aktualisieren", Neuaufbau));
        // weitere Funktionen dieses Programms
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(VerknuepfenKnopf());
        var kopplung = Knopf("Kopplungszeile", "plus", KopplungszeileNeu, false);
        kopplung.ToolTipText = "Kopplungszeile zum gewählten Unterricht hinzufügen (weiterer Lehrer bzw. weitere Gruppe) – Strg+R";
        ts.Items.Add(kopplung);
        ts.Items.Add(Knopf("Kopplungszeile löschen", "minus", KopplungszeileLoeschen));
        _rueckgaengig = Knopf("Rückgängig", "rueckgaengig", () =>
        {
            if (_grid.IsCurrentCellInEditMode) { _grid.CancelEdit(); _grid.EndEdit(); Hinweis("Eingabe verworfen.", HinweisArt.Info, true); return; }
            if (!Doc.KannRueckgaengig) { Hinweis("Es gibt nichts rückgängig zu machen.", HinweisArt.Info, true); return; }
            var text = Doc.RueckgaengigText;
            Doc.Rueckgaengig();
            Hinweis($"Rückgängig gemacht: {text}", HinweisArt.Ok, true);
        }, false);
        _rueckgaengig.ToolTipText = "Letzte Änderung rückgängig machen (Strg+Z); während der Eingabe: Eingabe verwerfen (Esc)";
        ts.Items.Add(_rueckgaengig);
        ts.Items.Add(Knopf("Unterricht kopieren", "unterricht", Kopieren));
        ts.Items.Add(Knopf("Alle Kopplungszeilen ein-/ausblenden (Strg+Umschalt+R)", "uebersicht", AlleKopplungszeilen));
        ts.Items.Add(Knopf("Stundenplan", "stundenplan", () => { if (_elementId.HasValue) StundenplanFenster.Neu(_sicht, _elementId, false); }));
        ts.Items.Add(Knopf("Automatische Unterrichtszuordnung (Lehrer für Zeilen ohne Lehrer)", "optimierung", AutomatischZuordnen));
        ts.Items.Add(_summe);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { Neuaufbau(); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.R) { AlleKopplungszeilen(); e.Handled = true; }
            else if (e.Control && !e.Shift && e.KeyCode == Keys.R) { KopplungszeileNeu(); e.Handled = true; }
        };

        Ui.RasterGestalten(_grid);
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.ShowCellErrors = true;
        _grid.StandardTab = false;
        _grid.Weiter = Weiter;
        // sofortige Prüfung (vor der Direktsuche der Auswahllisten registriert, damit unbekannte Kürzel nicht still „leer“ werden)
        _grid.CellValidating += ZellePruefen;
        // Ziel der Eingabe beim Beginn der Bearbeitung festhalten: die bearbeitete Zelle und die dabei markierten Zellen derselben Spalte
        _grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            _eingabe = (_grid[e.ColumnIndex, e.RowIndex], MarkierteZiele(e.ColumnIndex, e.RowIndex));
        };
        _grid.CellEndEdit += CellEndEdit;
        _grid.CellClick += CellClick;
        _grid.CellFormatting += CellFormatting;
        _grid.EditingControlShowing += EditingControlShowing;
        _grid.CurrentCellChanged += (_, _) =>
        {
            FormularFuellen();
            if (Aktuell() is { } akt && akt.U.Id <= _unrFeld.Maximum) { _unrLaden = true; _unrFeld.Value = akt.U.Id; _unrLaden = false; }
            if (!_laden) ZeilenHinweis();
            // gewählter Unterricht wirkt auf die geöffneten Stundenpläne (Synchronisation)
            if (!_laden && Aktuell() is { } zt) AppZustand.StundeWaehlen(zt.U, null, this);
        };
        _grid.CellMouseDown += CellMouseDown;
        _grid.DataError += (_, e) => e.ThrowException = false;
        Steuerelemente.SuchKombi.Aktivieren(_grid, (zelle, text) =>
        {
            // Langname als Zusatz in der Auswahlliste (Räume und Raumgruppen)
            var typ = zelle.OwningColumn.Name switch { "Fach" => ElementTyp.Fach, "Lehrer" => ElementTyp.Lehrer, _ => ElementTyp.Raum };
            var el = D.AlleElemente(typ).FirstOrDefault(x => x.Kurzname == text) ?? (typ == ElementTyp.Raum ? D.Raumgruppen.FirstOrDefault(x => x.Kurzname == text) : null);
            return el is null ? null : new Steuerelemente.ListenStil(Color.White, Color.Black, el is Lehrer l ? l.VollerName : el is Raumgruppe ? "Raumgruppe " + el.Langname : el.Langname);
        });
        // Klassen: Mehrfachauswahl mit Langnamen über den Pfeil in der aktuellen Zelle bzw. Alt+↓
        _grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Klassen" || _grid.CurrentCell is not { } c ||
                c.RowIndex != e.RowIndex || c.ColumnIndex != e.ColumnIndex || _grid.IsCurrentCellInEditMode || _grid[e.ColumnIndex, e.RowIndex].ReadOnly) return;
            MehrfachAuswahl.PfeilZeichnen(e);
        };
        _grid.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Left || _grid.Columns[e.ColumnIndex].Name != "Klassen") return;
            if (MehrfachAuswahl.AufPfeil(_grid, e)) KlassenAuswahl(e.RowIndex);
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Klassen" || _grid.Rows[e.RowIndex].Tag is not ZeilenTag zt) return;
            var z = zt.U.Zeilen.ElementAtOrDefault(zt.Index);
            var w = AuswahlDialog.Mehrere(this, ElementTyp.Klasse, "Klassen des Unterrichts", z?.KlassenIds ?? new List<int>());
            if (w is null) return;
            _grid[e.ColumnIndex, e.RowIndex].Value = string.Join(",", w.Select(k => k.Kurzname));
            CellEndEdit(this, new DataGridViewCellEventArgs(e.ColumnIndex, e.RowIndex));
        };
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) _grid.EndEdit(); };
        // Doppelklick auf die Unterrichtsnummer: in den Dialog „Koppeln“ bzw. das Fenster „Unterrichtsfolgen“ übernehmen
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "UNr" || _grid.Rows[e.RowIndex].Tag is not ZeilenTag zt || !NummerWirdErwartet) return;
            UnterrichtsnummerGewaehlt?.Invoke(zt.U.Id);
        };
        // Koppeln per Drag&Drop: Unterricht in der Spalte „Kl,Le“ anfassen und über einen anderen Unterricht ziehen;
        // eine Kopplungszeile aus der Kopplung herausziehen = entkoppeln
        _grid.AllowDrop = true;
        _grid.DragOver += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.Text) is not string t || !t.StartsWith("koppeln:")) return;
            e.Effect = DragDropEffects.Move;
        };
        _grid.DragDrop += KoppelnDrop;
        _grid.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) Kontextmenue(e); };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode) { ZelleLeeren(); e.Handled = true; }
            // Zwischenablage (Handbuch 3.3.7): Strg+C / Strg+X / Strg+V kopieren, ausschneiden bzw. fügen markierte Unterrichte ein
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode is Keys.C or Keys.X && !_grid.IsCurrentCellInEditMode) { UnterrichteKopieren(e.KeyCode == Keys.X); e.Handled = true; }
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.V && !_grid.IsCurrentCellInEditMode && _zwischenablage.Count > 0) { UnterrichteEinfuegen(); e.Handled = true; e.SuppressKeyPress = true; }
            // Erweitertes Einfügen (mit Stundenplan): Strg+Umschalt+V
            else if (e.Control && e.Shift && !e.Alt && e.KeyCode == Keys.V && !_grid.IsCurrentCellInEditMode && _zwischenablage.Count > 0) { UnterrichteEinfuegen(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Alt && e.KeyCode == Keys.Down && _grid.CurrentCell is { } c && _grid.Columns[c.ColumnIndex].Name == "Klassen" && !c.ReadOnly) { e.Handled = true; KlassenAuswahl(c.RowIndex); }
        };

        var unten = new Fussleiste { BackColor = Color.FromArgb(246, 248, 251) };
        var klappen = new Button { Text = "▾", Width = 22, Height = 22, Left = 4, Top = (Fussleiste.Hoehe - 22) / 2, FlatStyle = FlatStyle.Flat };
        klappen.Click += (_, _) => { _split.Panel2Collapsed = !_split.Panel2Collapsed; klappen.Text = _split.Panel2Collapsed ? "▾" : "▴"; };
        unten.Controls.Add(klappen);
        // wie im Handbuch: U-Nr des aktuellen Unterrichts in der Fußleiste – Eingabe einer Nummer springt zu diesem Unterricht
        unten.Controls.Add(new Label { Text = "U-Nr", Left = 32, Top = (Fussleiste.Hoehe - 15) / 2, AutoSize = true });
        _unrFeld.Left = 66;
        _unrFeld.Top = (Fussleiste.Hoehe - _unrFeld.Height) / 2;
        _unrFeld.ValueChanged += (_, _) => { if (!_unrLaden) UnterrichtMarkieren((int)_unrFeld.Value); };
        unten.Controls.Add(_unrFeld);
        // Einstellung „Eine Woche“: Datum der angezeigten Woche neben der U-Nr (Drehknopf blättert wochenweise)
        _woche.Left = _unrFeld.Right + 16;
        _woche.Top = (Fussleiste.Hoehe - _woche.Height) / 2;
        _woche.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _woche.ValueChanged += (_, _) => { if (_eineWoche) Neuaufbau(); };
        _wocheDreh.Left = _woche.Right + 1;
        _wocheDreh.Top = _woche.Top;
        _wocheDreh.ValueChanged += (_, _) =>
        {
            if (_wocheDreh.Value == 0) return;
            var r = (int)_wocheDreh.Value;
            _wocheDreh.Value = 0;
            _woche.Value = _woche.Value.AddDays(7 * r);
        };
        unten.Controls.Add(_woche);
        unten.Controls.Add(_wocheDreh);
        _hinweisLeiste.Controls.Add(_hinweis);
        _split.Panel1.Controls.Add(_grid);
        _split.Panel1.Controls.Add(_hinweisLeiste);
        _split.Panel2.Controls.Add(_formular);
        Controls.Add(_split);
        Controls.Add(unten);
        Controls.Add(ts);
        Hinweis("Neuer Unterricht: in der Zeile „+ neu“ Fach, Lehrer und – falls nötig – Klasse eingeben – Enter/Tab springt zum nächsten Feld, Esc verwirft die Eingabe.", HinweisArt.Info);
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
        AppZustand.StundeGewaehlt += StundeGewaehlt;
        FormClosed += (_, _) =>
        {
            AppZustand.ElementGewaehlt -= ElementGewaehlt;
            AppZustand.StundeGewaehlt -= StundeGewaehlt;
        };
    }

    private sealed record ElementEintrag(Element E)
    {
        public override string ToString() => E.Kurzname;
    }

    private void ElementGewaehlt(ElementTyp typ, int id, object quelle)
    {
        if (quelle != this && typ == _sicht && _sicht != ElementTyp.Unterricht && Verknuepft && !IsDisposed && id != _elementId) ElementSetzen(id);
    }

    /// <summary>
    /// Fensterverknüpfung: Stunde im Stundenplan gewählt – das Fenster zeigt das passende Element (Klasse bzw. Lehrkraft der Stunde)
    /// und markiert den Unterricht. Die Markierung meldet nichts zurück (keine Rückkopplung zwischen den Fenstern).
    /// </summary>
    private void StundeGewaehlt(Unterricht u, Planstunde? p, object quelle)
    {
        if (quelle == this || !Verknuepft || IsDisposed) return;
        if (_sicht != ElementTyp.Unterricht && AppZustand.ElementDerStunde(_sicht, u, p, _elementId) is int id && id != _elementId) ElementSetzen(id);
        UnterrichtMarkieren(u.Id);
    }

    /// <summary>Zeile eines Unterrichts zur aktuellen Zeile machen, ohne die Auswahl an andere Fenster zu melden.</summary>
    private void UnterrichtMarkieren(int unterrichtId)
    {
        if (Aktuell() is { } akt && akt.U.Id == unterrichtId) return;
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.Tag is not ZeilenTag zt || zt.U.Id != unterrichtId || zt.Index != 0) continue;
            var spalte = Math.Clamp(_grid.CurrentCell?.ColumnIndex ?? 0, 0, Math.Max(0, _grid.Columns.Count - 1));
            if (_grid.Columns.Count == 0 || !_grid.Columns[spalte].Visible) spalte = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible)?.Index ?? 0;
            _laden = true;
            try
            {
                _grid.ClearSelection();
                _grid.CurrentCell = r.Cells[spalte];
                r.Selected = true;
            }
            catch (InvalidOperationException) { }
            finally { _laden = false; }
            FormularFuellen();
            return;
        }
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

    /// <summary>Fachfolge beim Unterricht: 1–9 (positiv) bzw. A–E (negativ).</summary>
    private static string Fachfolge(string t)
    {
        var x = t.Trim().ToUpperInvariant();
        if (x.Length == 0) return "";
        return x.Length == 1 && (x[0] is >= '1' and <= '9' or >= 'A' and <= 'E') ? x : throw new FormatException("Erlaubte Eingaben sind 1 - 9 für die positive, A - E für die negative Fachfolge.");
    }

    /// <summary>Klassenkollisions-Kennzeichen: 1–9 bzw. A–H.</summary>
    private static string Kkk(string t)
    {
        var x = t.Trim().ToUpperInvariant();
        if (x.Length == 0) return "";
        return x.Length == 1 && (x[0] is >= '1' and <= '9' or >= 'A' and <= 'H') ? x : throw new FormatException("Die gültigen Eingaben sind 1 - 9 und A - H.");
    }

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
            new("Jst", "Jst", 44, false, true, (u, _) => (u.Wochenstunden * Kalender.Unterrichtswochen(AppZustand.D)).ToString(), null),
            new("Lehrer", "Lehrer", 56, true, true, (_, z) => ix().KLehrer(z.LehrerId), (d, u, z, t) =>
            {
                var l = t.Trim().Length == 0 ? null : ElementPflicht(d.Lehrer, t, "Lehrer");
                z.LehrerId = l?.Id;
                if (l?.StammraumId != null && z.StammraumId is null && z.KlassenIds.Count == 0) z.StammraumId = l.StammraumId;
            }),
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
            new("Dopp", "Dopp.Std.", 58, false, true, (u, _) => u.Doppelstunden.ToString(), (d, u, z, t) => u.Doppelstunden = Bereich.Parse(t), "Stundenplan"),
            new("Block", "Block", 42, false, true, (u, _) => u.Block, (d, u, z, t) => u.Block = t.Trim(), "Stundenplan"),
            new("Text", "Text", 80, false, true, (u, _) => u.Text, (d, u, z, t) => u.Text = t),
            new("Fix", "Fix (X)", 44, false, true, (u, _) => B(u.Fix), (d, u, z, t) => u.Fix = Wahr(t), "Kennzeichen", Bool: true),
            new("Ignore", "Ignore (i)", 50, false, true, (u, _) => B(u.Ignorieren), (d, u, z, t) => u.Ignorieren = Wahr(t), "Kennzeichen", Bool: true),
            // Karteikarte „Kennzeichen“ Teil 1 (Handbuch 3.4.4)
            new("Markiert", "(m) Markiert", 34, false, false, (u, _) => B(u.Markiert), (d, u, z, t) => u.Markiert = Wahr(t), "Kennzeichen", true),
            new("DoppPause", "(E) Doppelstd. über *-Pausen", 34, false, false, (u, _) => B(u.DoppelstundeUeberPause), (d, u, z, t) => u.DoppelstundeUeberPause = Wahr(t), "Kennzeichen", true),
            new("Freifach", "(F) Freifach", 34, false, false, (u, _) => B(u.Freifach), (d, u, z, t) => u.Freifach = Wahr(t), "Kennzeichen", true),
            new("NichtRand", "(G) Nicht in Randstunde", 34, false, false, (u, _) => B(u.NichtInRandstunde), (d, u, z, t) => u.NichtInRandstunde = Wahr(t), "Kennzeichen", true),
            new("KeinAusweich", "(K) Kein Ausweichraum", 34, false, false, (u, _) => B(u.KeinAusweichraum), (d, u, z, t) => u.KeinAusweichraum = Wahr(t), "Kennzeichen", true),
            new("KeineAnalyse", "(k) Keine Datenanalyse", 34, false, false, (u, _) => B(u.KeineDatenanalyse), (d, u, z, t) => u.KeineDatenanalyse = Wahr(t), "Kennzeichen", true),
            new("SelberRaum", "(r) Stunden im selben Raum", 34, false, false, (u, _) => B(u.SelberRaum), (d, u, z, t) => u.SelberRaum = Wahr(t), "Kennzeichen", true),
            new("LehrerFix", "Lehrerzuordnung fix", 34, true, false, (_, z) => B(z.LehrerFix), (d, u, z, t) => z.LehrerFix = Wahr(t), "Kennzeichen", true),
            new("ZwVorhanden", "Zeitwünsche vorhanden", 34, false, false, (u, _) => B(!u.Zeitwuensche.Leer), null, "Kennzeichen", true),
            // Karteikarte „Kennzeichen“ Teil 2 (Handbuch 3.4.5); (D), (2) und (C) schließen einander aus
            new("BedingtFix", "(B) Bedingt fixieren", 34, false, false, (u, _) => B(u.BedingtFixieren), (d, u, z, t) => u.BedingtFixieren = Wahr(t), "Kennzeichen 2", true),
            new("DoppEinhalten", "(D) Doppelstunden einhalten", 34, false, false, (u, _) => B(u.DoppelstundenEinhalten),
                (d, u, z, t) => { u.DoppelstundenEinhalten = Wahr(t); if (u.DoppelstundenEinhalten) { u.MehrmalsProTag = false; u.KeineEinzelstunden = false; } }, "Kennzeichen 2", true),
            new("KeineEinzel", "(C) Keine Einzelstunden", 34, false, false, (u, _) => B(u.KeineEinzelstunden),
                (d, u, z, t) => { u.KeineEinzelstunden = Wahr(t); if (u.KeineEinzelstunden) { u.MehrmalsProTag = false; u.DoppelstundenEinhalten = false; } }, "Kennzeichen 2", true),
            new("RandstundeU", "(R) Randstunde", 34, false, false, (u, _) => B(u.Randstunde), (d, u, z, t) => u.Randstunde = Wahr(t), "Kennzeichen 2", true),
            new("KlGrSpaeter", "(S) Klassengruppe später planen", 34, false, false, (u, _) => B(u.KlassengruppeSpaeter), (d, u, z, t) => u.KlassengruppeSpaeter = Wahr(t), "Kennzeichen 2", true),
            new("Mehrmals", "(2) Fach auch 2x / Tag", 34, false, false, (u, _) => B(u.MehrmalsProTag),
                (d, u, z, t) => { u.MehrmalsProTag = Wahr(t); if (u.MehrmalsProTag) { u.DoppelstundenEinhalten = false; u.KeineEinzelstunden = false; } }, "Kennzeichen 2", true),
            new("VarLehrer", "(V) Variabler Lehrer", 34, false, false, (u, _) => B(u.VariablerLehrer), (d, u, z, t) => u.VariablerLehrer = Wahr(t), "Kennzeichen 2", true),
            new("NichtLegende", "(L) Nicht in Legende", 34, false, false, (u, _) => B(u.NichtInLegende), (d, u, z, t) => u.NichtInLegende = Wahr(t), "Kennzeichen 2", true),
            new("NachmDoppel", "(U) Am Nachmittag nur Doppelstunden", 34, false, false, (u, _) => B(u.NachmittagNurDoppel), (d, u, z, t) => u.NachmittagNurDoppel = Wahr(t), "Kennzeichen 2", true),
            new("Manuell", "(M) Manuell planen", 34, false, false, (u, _) => B(u.ManuellPlanen), (d, u, z, t) => u.ManuellPlanen = Wahr(t), "Kennzeichen 2", true),
            new("SelbeZeit", "(s) Immer selbe Zeit", 34, false, false, (u, _) => B(u.ImmerSelbeZeit), (d, u, z, t) => u.ImmerSelbeZeit = Wahr(t), "Kennzeichen 2", true),
            // Spalte „Kennzeichen“ (Handbuch 3.4.7): alle gesetzten Kennzeichen, vererbte in Klammern (Einstellungen)
            new("Kennzeichen", "Kennzeichen", 70, false, false, (u, _) => Unterrichtswerkzeuge.KennzeichenAnzeige(ix(), u, _vererbte), null, "Kennzeichen"),
            // Karteikarte „Stundenplan“ (Handbuch 3.4.3)
            new("StdImRaum", "Stunden im Raum", 50, false, false, (u, _) => u.StundenImRaum > 0 ? u.StundenImRaum.ToString() : "",
                (d, u, z, t) => u.StundenImRaum = t.Trim().Length == 0 ? 0 : Math.Max(0, int.Parse(t)), "Stundenplan"),
            new("FfKlassen", "Fachfolge Klassen", 50, false, false, (u, _) => u.FachfolgeKlassen, (d, u, z, t) => u.FachfolgeKlassen = Fachfolge(t), "Stundenplan"),
            new("FfLehrer", "Fachfolge Lehrer", 50, false, false, (u, _) => u.FachfolgeLehrer, (d, u, z, t) => u.FachfolgeLehrer = Fachfolge(t), "Stundenplan"),
            new("KKK", "KlassenKoll. Kz.", 50, false, false, (u, _) => u.Klassenkollision, (d, u, z, t) => u.Klassenkollision = Kkk(t), "Stundenplan"),
            new("KeineVtr", "keine Vertr.", 70, false, false, (u, _) => B(u.KeineVertretung), (d, u, z, t) => u.KeineVertretung = Wahr(t), "Kennzeichen", true),
            new("Band", "Band", 50, false, true, (u, _) => u.Band, (d, u, z, t) => u.Band = t.Trim(), "Kursplanung"),
            new("Kurs", "Kursname", 70, false, false, (u, _) => u.Kursname, (d, u, z, t) => u.Kursname = t.Trim(), "Kursplanung"),
            new("Gruppe", "Schülergruppe", 80, false, false, (u, _) => u.Schuelergruppe, (d, u, z, t) => u.Schuelergruppe = t, "Unterricht"),
            new("Zeilentext", "ZeilenText", 80, true, false, (_, z) => z.Zeilentext, (d, u, z, t) => z.Zeilentext = t, "Unterricht"),
            new("Wert", "Wert", 48, true, false, (u, z) => Format.Zahl(Core.Module.Wertrechnung.Wert(AppZustand.D, ix(), u, z)),
                (d, u, z, t) => z.Wert = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Faktor", "Werte-Faktor", 70, false, false, (u, _) => u.WerteFaktor.HasValue ? Format.Zahl(u.WerteFaktor.Value) : "",
                (d, u, z, t) => u.WerteFaktor = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Periode", "Woche (A/B)", 70, false, false, (u, _) => u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => "" },
                (d, u, z, t) => u.Periodizitaet = t.Trim().ToUpperInvariant() switch { "A" => Periodizitaet.AWoche, "B" => Periodizitaet.BWoche, _ => Periodizitaet.JedeWoche }, "Mehrwochen"),
            new("UGruppe", "U-Gruppen", 66, false, false, (u, _) => AppZustand.D.UGruppen.FirstOrDefault(g => g.Id == u.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => u.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Unterricht"),
            new("ZUGruppe", "Zeilen-U-Gr.", 74, true, false, (_, z) => AppZustand.D.UGruppen.FirstOrDefault(g => g.Id == z.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => z.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Mehrwochen"),
            new("Von", "Von", 76, false, false, (u, _) => u.VonDatum.HasValue ? Format.Datum(u.VonDatum.Value) : "",
                (d, u, z, t) => u.VonDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Stundenplan"),
            new("Bis", "Bis", 76, false, false, (u, _) => u.BisDatum.HasValue ? Format.Datum(u.BisDatum.Value) : "",
                (d, u, z, t) => u.BisDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Stundenplan"),
            new("Statistik", "Statistik-Kennz.", 60, false, false, (u, _) => u.Statistik, (d, u, z, t) => u.Statistik = t, "Unterricht"),
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
        var liste = _sicht == ElementTyp.Unterricht ? D.Unterrichte.OrderBy(u => u.Id)
            : _elementId is null ? Enumerable.Empty<Unterricht>() : Ix.UnterrichteVon(_sicht, _elementId.Value).OrderBy(u => u.Id);
        // Einstellung „Eine Woche“: nur Unterrichte, die in der gewählten Woche stattfinden
        if (_eineWoche)
        {
            var montag = Kalender.Montag(DateOnly.FromDateTime(_woche.Value));
            return liste.Where(u => Unterrichtswerkzeuge.FindetInWocheStatt(D, u, montag));
        }
        return liste;
    }

    /// <summary>Platzhalter der Neuanlage-Zeile („+ neu“) am Ende der Liste.</summary>
    private static readonly object NeuanlageZeile = new();

    private static string Schluessel(object o) => o is ZeilenTag zt ? $"U{zt.U.Id}/{zt.Index}" : "neu";
    private static string? Schluessel(DataGridViewRow r) => r.Tag is ZeilenTag zt ? $"U{zt.U.Id}/{zt.Index}" : "neu";

    /// <summary>Stand, für den Spalten und Zeilen aufgebaut sind (Ansicht/Element, sichtbare Spalten und Auswahllisten).</summary>
    private string _aufgebautFuer = "";
    private string _spaltenStand = "";

    /// <summary>
    /// Liste aktualisieren, ohne sie ganz neu zu laden: geänderte Unterrichte werden an Ort und Stelle erneuert, neue (bzw. aufgeklappte
    /// Kopplungszeilen) an ihrem Platz eingefügt, gelöschte entfernt – aktuelle Zeile, Spalte und Bildlauf bleiben, nichts verrutscht.
    /// Neu aufgebaut wird nur bei anderem Element bzw. geänderten Spalten oder Auswahllisten.
    /// </summary>
    private void Neuaufbau()
    {
        // laufende Eingabe nicht in eine (geänderte) Zeile schreiben: verwerfen, bevor die Zeilen erneuert werden
        if (_grid.IsCurrentCellInEditMode && !_laden)
        {
            _laden = true;
            try { _grid.CancelEdit(); _grid.EndEdit(); }
            catch (InvalidOperationException) { }
            finally { _laden = false; }
        }
        _eingabe = null;
        _laden = true;
        var neueZeile = _grid.CurrentRow is { } cr && cr.Tag is null && cr.Index == _grid.Rows.Count - 1;
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
        var spalten = _spalten.Where(s => _sichtbar.Contains(s.Key)).OrderBy(s => _sichtbar.IndexOf(s.Key)).ToList();
        var listen = spalten.ToDictionary(s => s.Key, s => s.Bool ? null : AuswahlListe(s.Key));
        var spaltenStand = string.Join("|", spalten.Select(s => s.Key + "=" + (listen[s.Key] is { } li ? string.Join(",", li) : "")));
        var element = $"{_sicht}/{_elementId}";
        if (spaltenStand != _spaltenStand || _grid.Columns.Count == 0)
        {
            _spaltenStand = spaltenStand;
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            foreach (var s in spalten)
            {
                DataGridViewColumn c = s.Bool ? new DataGridViewCheckBoxColumn() : listen[s.Key] is { } liste ? KombiSpalte(liste) : new DataGridViewTextBoxColumn();
                c.Name = s.Key;
                c.HeaderText = s.Titel;
                c.Width = s.Breite;
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
                c.ReadOnly = s.Set is null;
                c.Tag = s;
                if (s.Key == "KlLe") c.HeaderText = "⊞ Kl,Le";
                _grid.Columns.Add(c);
            }
        }
        // anderes Element der Ansicht: andere Zeilen – neu aufbauen (oben beginnen)
        if (element != _aufgebautFuer) _grid.Rows.Clear();
        _aufgebautFuer = element;
        var summeWst = 0;
        var summeNv = 0;
        var eintraege = new List<object>();
        foreach (var u in Unterrichte())
        {
            summeWst += u.Wochenstunden;
            summeNv += Ix.NichtVerplant(u);
            var zeigen = (_alleAuf || _aufgeklappt.Contains(u.Id)) ? u.Zeilen.Count : 1;
            for (var i = 0; i < Math.Max(1, zeigen); i++) eintraege.Add(new ZeilenTag(u, i));
        }
        // Neuanlage-Zeile: deutlich markiert, das Element der Ansicht ist schon eingetragen (grau)
        eintraege.Add(NeuanlageZeile);
        // gerade über die Neuanlage-Zeile angelegter Unterricht: dessen Zeile wählen (Spalte bleibt)
        var aktuell = neueZeile && _neuId is int nid ? $"U{nid}/0" : null;
        _neuId = null;
        try { Steuerelemente.TabellenAbgleich.Anwenden(_grid, eintraege, Schluessel, Schluessel, ZeileSetzen, aktuell: aktuell); }
        catch (InvalidOperationException) { }
        finally
        {
            _grid.ResumeLayout();
            _laden = false;
        }
        _summe.Text = $"Wst: {summeWst}   Nvpl.: {summeNv}";
        FormularFuellen();
        ZeilenHinweis();
    }

    /// <summary>Hinweis zur aktuellen Zeile: fehlende Pflichtangaben bzw. Zusammenfassung des Unterrichts.</summary>
    private void ZeilenHinweis()
    {
        if (_hinweisFestUid is int fest && fest == (Aktuell()?.U.Id ?? -1)) return;
        _hinweisFestUid = null;
        if (_grid.CurrentRow is { Tag: null })
        {
            Hinweis("Neuer Unterricht: Fach, Lehrer und – falls nötig – Klasse eingeben (Alt+↓ öffnet die Auswahl mit Langnamen) – Enter/Tab springt zum nächsten Feld.", HinweisArt.Info);
            return;
        }
        if (Aktuell() is not { } zt) return;
        var u = Ix.Unterrichte.GetValueOrDefault(zt.U.Id) ?? zt.U;
        var fehlt = Unterrichtseingabe.Pflichthinweise(u, D);
        var pflicht = fehlt.Where(x => !x.StartsWith("Noch kein Lehrer")).ToList();
        if (pflicht.Count > 0) Hinweis($"U-Nr. {u.Id}: {string.Join(" ", fehlt)}", HinweisArt.Warnung);
        else
            Hinweis($"U-Nr. {u.Id}: {Ix.UnterrichtText(u)}, {u.Wochenstunden} Wst" + (Ix.NichtVerplant(u) > 0 ? $", {Ix.NichtVerplant(u)} nicht verplant" : ", vollständig verplant") +
                    (fehlt.Count > 0 ? " – " + string.Join(" ", fehlt) : ""), HinweisArt.Info);
    }

    /// <summary>Enter/Tab: Eingabe übernehmen (nur wenn gültig) und zum nächsten bearbeitbaren Feld (Umschalt: zurück).</summary>
    private void Weiter(bool zurueck)
    {
        if (_grid.CurrentCell is not { } c) return;
        if (_grid.IsCurrentCellInEditMode)
        {
            // getippten Suchtext der Auswahlliste zuerst auflösen (Treffer übernehmen), dann prüfen
            if (_grid.EditingControl is ComboBox kombi)
            {
                ListenSuche.SuchtextUebernehmen(kombi);
                if (kombi.DroppedDown) kombi.DroppedDown = false;
            }
            // EndEdit löst keine Zellprüfung aus: vorher prüfen, ungültige Eingabe bleibt in der Zelle (Hinweis in der Leiste)
            if (!EingabeGueltig(c.ColumnIndex, c.RowIndex, _grid.EditingControl?.Text ?? "") || !_grid.EndEdit()) return;
        }
        var spalten = _grid.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible && !x.ReadOnly).OrderBy(x => x.DisplayIndex).ToList();
        if (spalten.Count == 0) return;
        var zeile = c.RowIndex;
        var pos = spalten.FindIndex(x => x.Index == c.ColumnIndex);
        for (var schritte = 0; schritte < spalten.Count * 3; schritte++)
        {
            pos += zurueck ? -1 : 1;
            if (pos >= spalten.Count) { zeile++; pos = 0; }
            else if (pos < 0) { zeile--; pos = spalten.Count - 1; }
            if (zeile < 0 || zeile >= _grid.Rows.Count) return;
            var zelle = _grid.Rows[zeile].Cells[spalten[pos].Index];
            if (zelle.ReadOnly) continue;
            try { _grid.CurrentCell = zelle; } catch (InvalidOperationException) { }
            return;
        }
    }

    /// <summary>Klassen als Mehrfachauswahl mit Langnamen (Häkchen) wählen.</summary>
    private void KlassenAuswahl(int zeile)
    {
        if (!_grid.Columns.Contains("Klassen") || zeile < 0 || zeile >= _grid.Rows.Count) return;
        var spalte = _grid.Columns["Klassen"]!.Index;
        if (_grid.IsCurrentCellInEditMode && !_grid.EndEdit()) return;
        var eintraege = Stammdaten.Sortiert(D, ElementTyp.Klasse).Where(x => !x.Ignorieren).Select(x => new MehrfachAuswahl.Eintrag(x.Kurzname, x.Langname)).ToList();
        var aktuell = (_grid[spalte, zeile].Value?.ToString() ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        MehrfachAuswahl.Zeigen(_grid, zeile, spalte, eintraege, aktuell, true, gewaehlt =>
        {
            if (IsDisposed || zeile >= _grid.Rows.Count) return;
            _grid[spalte, zeile].Value = string.Join(",", gewaehlt);
            CellEndEdit(this, new DataGridViewCellEventArgs(spalte, zeile));
        });
    }

    /// <summary>Sofortige Prüfung der Eingabe: unbekannte Kürzel bzw. falsche Formate werden nicht übernommen, sondern erklärt.</summary>
    private void ZellePruefen(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_laden || e.RowIndex < 0 || e.ColumnIndex < 0 || !_grid.IsCurrentCellInEditMode) return;
        var text = _grid.EditingControl is ComboBox cb ? cb.Text : e.FormattedValue?.ToString() ?? "";
        if (!EingabeGueltig(e.ColumnIndex, e.RowIndex, text)) e.Cancel = true;
    }

    /// <summary>Eingabe einer Zelle prüfen: ungültig → Fehlersymbol und Hinweis in der Leiste; gültig mit Ergänzung → Hinweis.</summary>
    private bool EingabeGueltig(int spalte, int zeile, string text)
    {
        if (_grid.Columns[spalte].Tag is not Spalte s || s.Set is null || s.Bool) return true;
        LetztePruefung = $"{s.Key}=„{text}“";
        var zelle = _grid[spalte, zeile];
        var p = Unterrichtseingabe.Pruefen(D, s.Key, text);
        if (!p.Gueltig)
        {
            zelle.ErrorText = p.Hinweis;
            Hinweis(p.Hinweis + "  (Esc = Eingabe verwerfen)", HinweisArt.Warnung, true);
            return false;
        }
        zelle.ErrorText = "";
        if (p.Hinweis.Length > 0) Hinweis(p.Hinweis, HinweisArt.Info, true);
        return true;
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

    /// <summary>Eine Zeile vollständig füllen (Werte, Sperren, Pflichtfeld-Hinweise, Format) – für bestehende und neue Zeilen.</summary>
    private void ZeileSetzen(DataGridViewRow row, object eintrag)
    {
        if (eintrag is not ZeilenTag zt)
        {
            NeuanlageSetzen(row);
            return;
        }
        var (u, i) = (zt.U, zt.Index);
        var z = i < u.Zeilen.Count ? u.Zeilen[i] : new Kopplungszeile();
        row.Tag = zt;
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            var s = (Spalte)c.Tag!;
            var zelle = row.Cells[c.Index];
            object wert;
            var sperren = i > 0 && !s.Zeile;
            if (sperren) wert = s.Bool ? false : "";
            else
            {
                var v = s.Get(u, z);
                wert = s.Bool ? v == "X" : v;
            }
            if (!Equals(zelle.Value, wert) && (zelle.Value?.ToString() ?? "") != wert.ToString()) zelle.Value = wert;
            if (!c.ReadOnly && zelle.ReadOnly != sperren) zelle.ReadOnly = sperren;
            // fehlende Pflichtangabe: Fehlersymbol mit Erklärung in der Zelle
            var fehler = !sperren && Unterrichtseingabe.PflichtFehlt(u, i, s.Key, D)
                ? s.Key switch { "Fach" => "Pflichtfeld: Fach fehlt", "Klassen" => "Pflichtfeld: Klasse fehlt", _ => "Pflichtfeld: Wochenstunden fehlen" } : "";
            if (zelle.ErrorText != fehler) zelle.ErrorText = fehler;
            if (zelle.ToolTipText.Length > 0) zelle.ToolTipText = "";
            if (!zelle.Style.ForeColor.IsEmpty) zelle.Style.ForeColor = Color.Empty;
        }
        var hinter = i > 0 ? Ui.BlauSehrHell : Color.Empty;
        if (row.DefaultCellStyle.BackColor != hinter) row.DefaultCellStyle.BackColor = hinter;
        if (!row.DefaultCellStyle.SelectionBackColor.IsEmpty) row.DefaultCellStyle.SelectionBackColor = Color.Empty;
        var vorder = u.Ignorieren ? Color.Gray : Color.Empty;
        if (row.DefaultCellStyle.ForeColor != vorder) row.DefaultCellStyle.ForeColor = vorder;
    }

    /// <summary>Neuanlage-Zeile („+ neu“): deutlich markiert, das Element der Ansicht ist schon eingetragen (grau).</summary>
    private void NeuanlageSetzen(DataGridViewRow neuZeile)
    {
        neuZeile.Tag = null;
        foreach (DataGridViewCell c in neuZeile.Cells) c.Value = c is DataGridViewCheckBoxCell ? false : null;
        neuZeile.DefaultCellStyle.BackColor = Color.FromArgb(236, 248, 236);
        neuZeile.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 232, 200);
        if (_grid.Columns.Contains("UNr"))
        {
            neuZeile.Cells["UNr"].Value = "+ neu";
            neuZeile.Cells["UNr"].ToolTipText = "Neuer Unterricht: Fach, Lehrer und ggf. Klasse in dieser Zeile eingeben (Unterricht ohne Klasse ist möglich). Der Unterricht wird mit dem ersten Wert angelegt.";
        }
        var vorgabe = _elementId is int eid ? D.AlleElemente(_sicht).FirstOrDefault(e => e.Id == eid)?.Kurzname : null;
        var vorgabeSpalte = _sicht switch { ElementTyp.Klasse => "Klassen", ElementTyp.Lehrer => "Lehrer", ElementTyp.Fach => "Fach", _ => null };
        if (vorgabe != null && vorgabeSpalte != null && _grid.Columns.Contains(vorgabeSpalte))
        {
            neuZeile.Cells[vorgabeSpalte].Style.ForeColor = Color.Gray;
            neuZeile.Cells[vorgabeSpalte].ToolTipText = $"{vorgabe} wird beim Anlegen automatisch eingetragen.";
            if (_grid.Columns[vorgabeSpalte] is not DataGridViewComboBoxColumn) neuZeile.Cells[vorgabeSpalte].Value = vorgabe;
        }
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
        if (Unterrichtseingabe.PflichtFehlt(zt.U, zt.Index, key, D)) e.CellStyle!.BackColor = Color.FromArgb(255, 232, 232);
    }

    private void CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "KlLe")
        {
            AlleKopplungszeilen();
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
        if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "KlLe" &&
            _grid.Rows[e.RowIndex].Tag is ZeilenTag kz && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
        {
            _ziehen = (e.RowIndex, Cursor.Position, $"koppeln:{kz.U.Id}:{kz.Index}");
            return;
        }
        // Verplanung aus dem Unterrichtsfenster: Spalte "Nvpl. Std." per Drag & Drop in den Stundenplan ziehen
        if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Nvpl" &&
            _grid.Rows[e.RowIndex].Tag is ZeilenTag zt && Ix.NichtVerplant(zt.U) > 0)
            _grid.DoDragDrop("unterricht:" + zt.U.Id, DragDropEffects.Move);
    }

    /// <summary>Kopierte Unterrichte (U-Nr.) und das Element der Ansicht, aus der kopiert wurde.</summary>
    private static List<int> _zwischenablage = new();
    private static (ElementTyp Typ, int? Id) _zwischenablageQuelle;

    private void UnterrichteKopieren(bool ausschneiden)
    {
        var ids = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<ZeilenTag>().Select(t => t.U.Id).Distinct().ToList();
        if (ids.Count == 0) return;
        // für andere Programme: die markierten Zellen als Text
        try { if (_grid.GetClipboardContent() is { } inhalt) Clipboard.SetDataObject(inhalt); }
        catch (System.Runtime.InteropServices.ExternalException) { }
        _zwischenablage = ids;
        _zwischenablageQuelle = (_sicht, _elementId);
        if (ausschneiden && Ui.Frage($"{ids.Count} Unterricht(e) ausschneiden? Sie werden hier gelöscht und können mit Strg+V eingefügt werden."))
        {
            if (Aendern("Unterricht ausschneiden", AenderungsArt.Unterricht, d =>
            {
                // Kopien für das Einfügen behalten (die Originale werden gelöscht)
                foreach (var id in ids) _ausgeschnitten[id] = d.Unterrichte.First(u => u.Id == id).Kopie(id);
                ids.ForEach(id => Stammdaten.UnterrichtLoeschen(d, id));
            })) Neuaufbau();
        }
        else Hinweis($"{ids.Count} Unterricht(e) kopiert – mit Strg+V einfügen (z. B. nach Wechsel zu einer anderen Klasse).", HinweisArt.Ok, true);
    }

    private static readonly Dictionary<int, Unterricht> _ausgeschnitten = new();

    /// <summary>
    /// Einfügen: neue Unterrichte als Kopie; beim Einfügen in eine andere Klasse (Lehrkraft) wird die Ausgangsklasse (-lehrkraft) ersetzt
    /// und der Stammraum der neuen Klasse übernommen.
    /// </summary>
    /// <param name="erweitert">Erweitertes Einfügen: auch den Stundenplan der kopierten Unterrichte übernehmen (Handbuch 3.3.7).</param>
    private void UnterrichteEinfuegen(bool erweitert = false)
    {
        var quelle = _zwischenablageQuelle;
        var ziel = _elementId;
        var neue = new List<int>();
        var gleicheSicht = quelle.Typ == _sicht;
        if (!Aendern(erweitert ? "Erweitertes Einfügen" : "Unterricht einfügen", AenderungsArt.Unterricht, d =>
            neue = Unterrichtswerkzeuge.ErweitertesEinfuegen(d, _zwischenablage, _sicht, gleicheSicht ? quelle.Id : null, gleicheSicht ? ziel : null, erweitert, _ausgeschnitten))) return;
        Neuaufbau();
        var verplant = erweitert ? neue.Sum(id => D.Stundenplan.Count(p => p.UnterrichtId == id)) : 0;
        Hinweis(neue.Count == 0 ? "Nichts eingefügt." : $"Eingefügt: Unterricht {string.Join(", ", neue)}" + (erweitert ? $" mit {verplant} verplanten Stunden." : " (noch nicht verplant)."),
            neue.Count == 0 ? HinweisArt.Warnung : HinweisArt.Ok, true);
    }

    private void AlleKopplungszeilen()
    {
        _alleAuf = !_alleAuf;
        _aufgeklappt.Clear();
        Neuaufbau();
    }

    /// <summary>Normalform anzeigen: Fensterrahmen an die Größe der Tabelle anpassen.</summary>
    private void Normalform()
    {
        _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        var breite = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width) + 40;
        var hoehe = Math.Min(_grid.Rows.Count * _grid.RowTemplate.Height + 150 + (_split.Panel2Collapsed ? 0 : 160), (AppZustand.Haupt?.ClientSize.Height ?? 800) - 200);
        Size = new Size(Math.Min(Math.Max(480, breite), (AppZustand.Haupt?.ClientSize.Width ?? 1200) - 40), Math.Max(260, hoehe));
    }

    /// <summary>
    /// Koppeln (Handbuch 3.3.4): die markierten Unterrichte zu einem Unterricht zusammenfassen – deren Kopplungszeilen kommen zum ersten
    /// Unterricht. Ist nur ein Unterricht markiert, werden die Nummern der dazuzukoppelnden Unterrichte abgefragt.
    /// </summary>
    private void Koppeln()
    {
        var ids = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<ZeilenTag>().Select(t => t.U.Id).Distinct().ToList();
        if (ids.Count == 0 && Aktuell() is { } a) ids.Add(a.U.Id);
        if (ids.Count == 0) return;
        if (ids.Count == 1)
        {
            // Dialog „Koppeln“: der aktivierte Unterricht ist eingetragen; weitere per Doppelklick im Unterrichtsfenster,
            // über <Dazu> (aktivierter Unterricht bzw. eingetragene Nummer)
            KoppelnDialog.Zeigen(this, ids[0], () => Aktuell()?.U.Id, liste => KoppelnAusfuehren(liste, false));
            return;
        }
        KoppelnAusfuehren(ids, true);
    }

    /// <summary>Koppeln der Unterrichte (erster = Ziel) nach Rückfrage.</summary>
    private void KoppelnAusfuehren(List<int> ids, bool fragen)
    {
        if (ids.Count < 2) return;
        var ziel = ids[0];
        var weitere = ids.Skip(1).ToList();
        var wst = D.Unterrichte.Where(u => ids.Contains(u.Id)).Select(u => u.Wochenstunden).Distinct().ToList();
        var text = $"Sollen die Unterrichte {string.Join(", ", ids)} gekoppelt werden?\n\nDie Kopplungszeilen kommen zum Unterricht {ziel}; verplante Stunden der übrigen Unterrichte werden entplant.";
        if (wst.Count > 1) text += $"\n\nAchtung: unterschiedliche Wochenstunden – die Kopplung übernimmt {D.Unterrichte.First(u => u.Id == ziel).Wochenstunden} Wst.";
        if (fragen && !Ui.Frage(text)) return;
        if (Aendern("Koppeln", AenderungsArt.Unterricht, d => Unterrichtswerkzeuge.Koppeln(d, ziel, weitere)))
        {
            _aufgeklappt.Add(ziel);
            Neuaufbau();
            Hinweis($"Unterrichte gekoppelt: U-Nr. {ziel} hat jetzt {D.Unterrichte.First(u => u.Id == ziel).Zeilen.Count} Kopplungszeilen.", HinweisArt.Ok, true, ziel);
        }
    }

    /// <summary>
    /// Erweitertes Entkoppeln (Handbuch 3.3.5): gewählte Kopplungszeilen eines gekoppelten Unterrichts zu selbständigen Unterrichten
    /// (mit eigener Unterrichtsnummer) machen.
    /// </summary>
    private void ErweitertesEntkoppeln()
    {
        if (Aktuell() is not { } t || t.U.Zeilen.Count < 2) { Hinweis("Bitte einen gekoppelten Unterricht (mehrere Kopplungszeilen) wählen.", HinweisArt.Warnung, true); return; }
        var u0 = t.U;
        using var d = new Dialog("Erweitertes Entkoppeln", 420, 260);
        d.Controls.Add(new Label { Text = $"Unterricht {u0.Id}: Kopplungszeilen, die aus der Kopplung genommen werden sollen", Left = 10, Top = 8, Width = 400 });
        var liste = new CheckedListBox { Left = 10, Top = 30, Width = 400, Height = 180, CheckOnClick = true };
        for (var i = 0; i < u0.Zeilen.Count; i++)
        {
            var z = u0.Zeilen[i];
            liste.Items.Add($"{i + 1}:  {Ix.KLehrer(z.LehrerId),-8} {Ix.KFach(z.FachId),-6} {Ix.KKlassen(z.KlassenIds)}", i == t.Index && i > 0);
        }
        d.Controls.Add(liste);
        d.Ok.Text = "Ok";
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var auswahl = liste.CheckedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        if (auswahl.Count == 0) return;
        if (auswahl.Count == u0.Zeilen.Count) auswahl.RemoveAt(auswahl.Count - 1);
        var neue = new List<int>();
        if (Aendern("Erweitertes Entkoppeln", AenderungsArt.Unterricht, dd =>
        {
            var u = dd.Unterrichte.First(x => x.Id == u0.Id);
            foreach (var i in auswahl)
            {
                var neu = u.Kopie(dd.NeueId());
                neu.Zeilen = new List<Kopplungszeile> { u.Zeilen[i].Kopie() };
                dd.Unterrichte.Add(neu);
                neue.Add(neu.Id);
                foreach (var p in dd.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id && p.Raeume.Count > i && !(p.Raeume.Count == 1 && u.Zeilen.Count > 1))) p.Raeume.RemoveAt(i);
                u.Zeilen.RemoveAt(i);
            }
        }))
        {
            Neuaufbau();
            Hinweis($"Entkoppelt: neue Unterrichte {string.Join(", ", neue.OrderBy(x => x))} (noch nicht verplant).", HinweisArt.Ok, true);
        }
    }

    /// <summary>Einstellungen (Handbuch 3.5): Summenzeile anzeigen, Schrift der Rasteransicht.</summary>
    private void EinstellungenDialog()
    {
        using var d = new Dialog("Einstellungen", 280, 140);
        // wie im Handbuch: Eine Woche, Vererbte Kennzeichen anzeigen, Summenzeile anzeigen; Schrift / Ok / Abbrechen
        var woche = new CheckBox { Text = "Eine Woche", Left = 12, Top = 12, AutoSize = true, Checked = _eineWoche, Name = "EineWoche" };
        var vererbt = new CheckBox { Text = "Vererbte Kennzeichen anzeigen", Left = 12, Top = 36, AutoSize = true, Checked = _vererbte, Name = "Vererbte" };
        var summe = new CheckBox { Text = "Summenzeile anzeigen", Left = 12, Top = 60, AutoSize = true, Checked = _summe.Visible, Name = "Summenzeile" };
        d.Controls.Add(woche);
        d.Controls.Add(vererbt);
        d.Controls.Add(summe);
        var schrift = new Button { Text = "Schrift", Left = 12, Top = 104, Width = 72, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        Font? neueSchrift = null;
        schrift.Click += (_, _) =>
        {
            using var fd = new FontDialog { Font = _grid.DefaultCellStyle.Font ?? _grid.Font, ShowEffects = false };
            if (fd.ShowDialog(d) == DialogResult.OK) neueSchrift = fd.Font;
        };
        d.Controls.Add(schrift);
        d.Ok.Text = "Ok";
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _summe.Visible = summe.Checked;
        var neuAufbauen = _eineWoche != woche.Checked || _vererbte != vererbt.Checked;
        _eineWoche = woche.Checked;
        _vererbte = vererbt.Checked;
        _woche.Visible = _wocheDreh.Visible = _eineWoche;
        if (neuAufbauen) { _spaltenStand = ""; Neuaufbau(); }
        if (neueSchrift != null)
        {
            _grid.DefaultCellStyle.Font = neueSchrift;
            _grid.RowTemplate.Height = Math.Max(18, neueSchrift.Height + 6);
            foreach (DataGridViewRow r in _grid.Rows) r.Height = _grid.RowTemplate.Height;
        }
    }

    private (int Zeile, Point Start, string Daten)? _ziehen;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Ziehen erst nach einer kleinen Mausbewegung beginnen (ein Klick auf „Kl,Le“ klappt weiterhin auf/zu)
        _grid.MouseMove += (_, m) =>
        {
            if (_ziehen is not { } z || (m.Button & MouseButtons.Left) == 0) { _ziehen = null; return; }
            var p = Cursor.Position;
            if (Math.Abs(p.X - z.Start.X) < SystemInformation.DragSize.Width && Math.Abs(p.Y - z.Start.Y) < SystemInformation.DragSize.Height) return;
            _ziehen = null;
            _grid.DoDragDrop(z.Daten, DragDropEffects.Move);
        };
        _grid.MouseUp += (_, _) => _ziehen = null;
    }

    /// <summary>
    /// Drag&amp;Drop in der Spalte „Kl,Le“: Unterricht über einen anderen Unterricht fallen lassen = koppeln (nach Rückfrage);
    /// eine weitere Kopplungszeile außerhalb ihres Unterrichts fallen lassen = aus der Kopplung lösen (eigener Unterricht).
    /// </summary>
    private void KoppelnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.Text) is not string t || !t.StartsWith("koppeln:")) return;
        var teile = t.Split(':');
        if (teile.Length < 3 || !int.TryParse(teile[1], out var quelle) || !int.TryParse(teile[2], out var zeile)) return;
        var pt = _grid.PointToClient(new Point(e.X, e.Y));
        var hit = _grid.HitTest(pt.X, pt.Y);
        var ziel = hit.RowIndex >= 0 && _grid.Rows[hit.RowIndex].Tag is ZeilenTag zt ? zt.U.Id : (int?)null;
        var qu = D.Unterrichte.FirstOrDefault(u => u.Id == quelle);
        if (qu is null || ziel == quelle) return;
        if (zeile > 0 && qu.Zeilen.Count > 1)
        {
            // Kopplungszeile aus der Kopplung herausziehen
            int? neu = null;
            if (Aendern("Entkoppeln", AenderungsArt.Unterricht, dd =>
            {
                var u = dd.Unterrichte.First(x => x.Id == quelle);
                var n = u.Kopie(dd.NeueId());
                n.Zeilen = new List<Kopplungszeile> { u.Zeilen[zeile].Kopie() };
                dd.Unterrichte.Add(n);
                neu = n.Id;
                foreach (var p in dd.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id && p.Raeume.Count > zeile && !(p.Raeume.Count == 1 && u.Zeilen.Count > 1))) p.Raeume.RemoveAt(zeile);
                u.Zeilen.RemoveAt(zeile);
            }))
            {
                Neuaufbau();
                Hinweis($"Entkoppelt: Kopplungszeile {zeile + 1} von U-Nr. {quelle} ist jetzt Unterricht {neu} (noch nicht verplant).", HinweisArt.Ok, true);
            }
            return;
        }
        if (ziel is not int zid) return;
        var zu = D.Unterrichte.First(u => u.Id == zid);
        if (!Ui.Frage($"Wollen Sie folgende Unterrichte koppeln?\n\n{zid}, {Ix.UnterrichtText(zu)}\n{quelle}, {Ix.UnterrichtText(qu)}", "Warnung")) return;
        if (Aendern("Koppeln", AenderungsArt.Unterricht, dd => Unterrichtswerkzeuge.Koppeln(dd, zid, new[] { quelle })))
        {
            _aufgeklappt.Add(zid);
            Neuaufbau();
            Hinweis($"Unterrichte gekoppelt: U-Nr. {zid} hat jetzt {D.Unterrichte.First(u => u.Id == zid).Zeilen.Count} Kopplungszeilen.", HinweisArt.Ok, true, zid);
        }
    }

    private void Kontextmenue(DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex >= 0 && !_grid[e.ColumnIndex, e.RowIndex].Selected) _grid.CurrentCell = _grid[Math.Max(0, e.ColumnIndex), e.RowIndex];
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Ausschneiden (Strg+X)", null, (_, _) => UnterrichteKopieren(true));
        cm.Items.Add("Kopieren (Strg+C)", null, (_, _) => UnterrichteKopieren(false));
        var einf = cm.Items.Add("Einfügen (Strg+V)", null, (_, _) => UnterrichteEinfuegen());
        var erw = cm.Items.Add("Erweitertes Einfügen (Strg+Umschalt+V)", null, (_, _) => UnterrichteEinfuegen(true));
        einf.Enabled = erw.Enabled = _zwischenablage.Count > 0;
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add("Koppeln…", Symbole.Get("plus"), (_, _) => Koppeln());
        cm.Items.Add("Erweitertes Entkoppeln…", Symbole.Get("minus"), (_, _) => ErweitertesEntkoppeln());
        cm.Items.Add("Unterricht als Kurs übernehmen", null, (_, _) => AlsKursUebernehmen());
        cm.Items.Add("Unterrichtsfolgen…", null, (_, _) => HauptFenster.Oeffnen<UnterrichtsfolgenFenster>("ufolgen", () => new UnterrichtsfolgenFenster()));
        cm.Show(_grid, _grid.PointToClient(Cursor.Position));
    }

    /// <summary>Unterrichtsvergleich: Unterrichte in mehreren Perioden vergleichen (Handbuch 3.5).</summary>
    private void Unterrichtsvergleich()
    {
        if (D.Perioden.Count < 2) { Hinweis("Für den Unterrichtsvergleich werden mindestens zwei Perioden benötigt (Modul Mehrwochenstundenplan / Perioden).", HinweisArt.Warnung, true); return; }
        UnterrichtsvergleichDialog.Zeigen(this, Unterrichte().Select(u => u.Id).ToHashSet());
    }

    /// <summary>Unterricht als Kurs übernehmen (Kursplanung): Kursname vergeben und Schüler der Klassen eintragen.</summary>
    private void AlsKursUebernehmen()
    {
        var ids = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow.Tag).OfType<ZeilenTag>().Select(t => t.U.Id).Distinct().ToList();
        if (ids.Count == 0 && Aktuell() is { } a) ids.Add(a.U.Id);
        if (ids.Count == 0) return;
        var schueler = 0;
        if (Aendern("Unterricht als Kurs übernehmen", AenderungsArt.Unterricht, d =>
        {
            foreach (var id in ids) if (d.Unterrichte.FirstOrDefault(u => u.Id == id) is { } u) schueler += Unterrichtswerkzeuge.AlsKursUebernehmen(d, u);
        }))
        {
            Neuaufbau();
            var namen = string.Join(", ", ids.Select(id => D.Unterrichte.FirstOrDefault(u => u.Id == id)?.Kursname).Where(n => !string.IsNullOrEmpty(n)));
            Hinweis($"Als Kurs übernommen: {namen}" + (schueler > 0 ? $" – {schueler} Kurswahl(en) eingetragen." : "."), HinweisArt.Ok, true);
        }
    }

    private ZeilenTag? Aktuell() => _grid.CurrentRow?.Tag as ZeilenTag;

    /// <summary>Laufende Eingabe: Zelle und Zielzeilen, festgehalten beim Beginn der Bearbeitung.</summary>
    private (DataGridViewCell Zelle, List<ZeilenTag> Ziele)? _eingabe;

    /// <summary>
    /// Zeilen, auf die eine Eingabe in der Zelle (spalte, zeile) wirkt: die Zeile selbst und – nur wenn diese Zelle markiert ist –
    /// alle weiteren markierten Zellen derselben Spalte (Mehrfachänderung). Eine Markierung, die nicht die bearbeitete Zelle
    /// enthält (z. B. nach einem Klick in eine andere Zeile), wirkt nie mit.
    /// </summary>
    private List<ZeilenTag> MarkierteZiele(int spalte, int zeile)
    {
        var res = new List<ZeilenTag>();
        if (zeile < 0 || zeile >= _grid.Rows.Count || _grid.Rows[zeile].Tag is not ZeilenTag eigen) return res;
        res.Add(eigen);
        if (!_grid[spalte, zeile].Selected) return res;
        foreach (var c in _grid.SelectedCells.Cast<DataGridViewCell>().Where(c => c.ColumnIndex == spalte && c.RowIndex != zeile).OrderBy(c => c.RowIndex))
            if (c.OwningRow.Tag is ZeilenTag zt && !res.Contains(zt)) res.Add(zt);
        return res;
    }

    private void CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_laden || e.RowIndex < 0) return;
        var s = (Spalte)_grid.Columns[e.ColumnIndex].Tag!;
        if (s.Set is null) return;
        var v = _grid[e.ColumnIndex, e.RowIndex].Value;
        var text = v switch { bool b => b ? "X" : "", null => "", _ => v.ToString()! };
        // geprüfter, normalisierter Wert (Groß-/Kleinschreibung, ergänzte Kürzel)
        if (!s.Bool)
        {
            var pr = Unterrichtseingabe.Pruefen(D, s.Key, text);
            if (!pr.Gueltig)
            {
                // ungültige Eingabe nie speichern (z. B. über eine Auswahlliste übernommen): erklären und alten Wert zeigen
                Hinweis(pr.Hinweis, HinweisArt.Warnung, true);
                NachZellwechsel(Neuaufbau);
                return;
            }
            text = pr.Wert;
        }
        var tag = _grid.Rows[e.RowIndex].Tag as ZeilenTag;
        if (tag is null)
        {
            if (text.Trim().Length == 0) return;
            // vorgegebenes Element der Ansicht (grau) ist kein eigener Wert
            if (_elementId is int eid && D.AlleElemente(_sicht).FirstOrDefault(x => x.Id == eid)?.Kurzname == text.Trim() &&
                s.Key == (_sicht switch { ElementTyp.Klasse => "Klassen", ElementTyp.Lehrer => "Lehrer", _ => "" })) return;
            NeuMitWert(s, text);
            return;
        }
        // Zielzeilen: beim Beginn der Bearbeitung festgehalten (nie die Zeile, in die inzwischen geklickt wurde)
        var eingabe = _eingabe;
        _eingabe = null;
        var ziele = eingabe is { } ein && ein.Zelle.RowIndex == e.RowIndex && ein.Zelle.ColumnIndex == e.ColumnIndex && ein.Zelle.DataGridView == _grid && ein.Ziele.Contains(tag)
            ? ein.Ziele.ToList() : MarkierteZiele(e.ColumnIndex, e.RowIndex);
        if (!ziele.Contains(tag)) ziele.Insert(0, tag);
        // unverändert: nichts speichern (keine leere Rückgängig-Stufe)
        if (ziele.Count == 1 && s.Get(tag.U, tag.U.Zeilen.ElementAtOrDefault(tag.Index) ?? new Kopplungszeile()) == text) return;
        // „Soll behindernder Unterricht entplant werden?“: zusätzliche Klasse bzw. Lehrkraft kollidiert mit den verplanten Stunden
        var weg = new List<int>();
        if (s.Key is "Klassen" or "Lehrer")
        {
            var behindernd = new List<Hindernis>();
            foreach (var z in ziele.Where(z => Ix.Verplant(z.U) > 0))
            {
                var alt = Ix.Unterrichte.GetValueOrDefault(z.U.Id) ?? z.U;
                var neu = alt.Kopie(alt.Id);
                while (neu.Zeilen.Count <= z.Index) neu.Zeilen.Add(new Kopplungszeile());
                try { s.Set(D, neu, neu.Zeilen[z.Index], text); } catch (FormatException) { continue; }
                behindernd.AddRange(Behinderung.DurchAenderung(D, Ix, alt, neu).Where(h => behindernd.All(x => x.Planstunde != h.Planstunde)));
            }
            if (behindernd.Count > 0)
            {
                var antwort = BehinderungsAbfrage.Fragen(this, D, Ix, behindernd,
                    $"U-Nr. {tag.U.Id}: {s.Titel} „{text}“ – der Unterricht ist bereits verplant; zu den verplanten Zeiten hat {(s.Key == "Klassen" ? "die Klasse" : "die Lehrkraft")} schon Unterricht.");
                if (antwort == BehinderungsAntwort.Abbrechen)
                {
                    Hinweis($"Abgebrochen – {s.Titel} von U-Nr. {tag.U.Id} bleibt unverändert.", HinweisArt.Info, true);
                    NachZellwechsel(Neuaufbau);
                    return;
                }
                if (antwort == BehinderungsAntwort.Entplanen) weg = behindernd.Select(h => D.Stundenplan.IndexOf(h.Planstunde)).Where(i => i >= 0).ToList();
            }
        }
        if (Aendern($"Unterricht: {s.Titel}", AenderungsArt.Unterricht, d =>
        {
            var entfernen = weg.Select(i => d.Stundenplan[i]).ToList();
            foreach (var x in entfernen) d.Stundenplan.Remove(x);
            foreach (var z in ziele)
            {
                var u = d.Unterrichte.First(x => x.Id == z.U.Id);
                while (u.Zeilen.Count <= z.Index) u.Zeilen.Add(new Kopplungszeile());
                s.Set(d, u, u.Zeilen[z.Index], text);
            }
        }))
            Hinweis($"Gespeichert: {s.Titel} = „{text}“ (U-Nr. {string.Join(", ", ziele.Select(z => z.U.Id).Distinct())})" +
                    (weg.Count > 0 ? $", {weg.Count} behindernde Stunde(n) entplant" : "") + " – Strg+Z macht die Änderung rückgängig.", HinweisArt.Ok, true, tag.U.Id);
        NachZellwechsel(Neuaufbau);
    }

    private void NeuMitWert(Spalte s, string text)
    {
        Unterricht? neu = null;
        if (Aendern("Neuer Unterricht", AenderungsArt.Unterricht, d =>
        {
            neu = NeuerUnterrichtAnlegen(d);
            if (s.Set != null) s.Set(d, neu, neu.Zeilen[0], text);
        }))
        {
            _neuId = neu?.Id;
            Hinweis($"Neuer Unterricht U-Nr. {neu?.Id} angelegt ({s.Titel} = „{text}“) – weitere Felder eintragen; Strg+Z macht die Anlage rückgängig.", HinweisArt.Ok, true, neu?.Id);
            NachZellwechsel(Neuaufbau);
        }
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
        Unterricht? neu = null;
        if (Aendern("Neuer Unterricht", AenderungsArt.Unterricht, d => neu = NeuerUnterrichtAnlegen(d)))
        {
            Neuaufbau();
            var zeile = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ZeilenTag zt && zt.U.Id == neu?.Id);
            var spalte = new[] { "Fach", "Lehrer", "Klassen", "Wst" }.FirstOrDefault(n => _grid.Columns.Contains(n) && _grid.Columns[n]!.Visible);
            if (zeile != null && spalte != null) _grid.CurrentCell = zeile.Cells[spalte];
            Hinweis($"Neuer Unterricht U-Nr. {neu?.Id} angelegt – bitte Fach, Lehrer und ggf. Klasse eintragen (Enter springt weiter).", HinweisArt.Ok, true, neu?.Id);
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
        if (Aktuell() is not { } t)
        {
            Hinweis("Bitte zuerst den Unterricht wählen, zu dem eine Kopplungszeile (weiterer Lehrer bzw. weitere Gruppe) hinzukommen soll.", HinweisArt.Warnung, true);
            return;
        }
        var nr = t.U.Zeilen.Count + 1;
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
            // neue Kopplungszeile wählen: Lehrer eintragen
            var zeile = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ZeilenTag zt && zt.U.Id == t.U.Id && zt.Index == nr - 1);
            var spalte = new[] { "Lehrer", "Fach", "Klassen" }.FirstOrDefault(n => _grid.Columns.Contains(n) && _grid.Columns[n]!.Visible);
            if (zeile != null && spalte != null) { _grid.ClearSelection(); _grid.CurrentCell = zeile.Cells[spalte]; }
            Hinweis($"Kopplungszeile {nr} zu U-Nr. {t.U.Id} angelegt (Klassen und Fach übernommen) – jetzt den Lehrer der Zeile eintragen.", HinweisArt.Ok, true);
        }
    }

    private void KopplungszeileLoeschen()
    {
        if (Aktuell() is not { } t || t.U.Zeilen.Count <= 1) return;
        var idx = t.Index;
        if (!Ui.Frage($"Kopplungszeile {idx + 1} des Unterrichts {t.U.Id} löschen?\n\nLehrkraft, Fach, Klassen und Räume dieser Zeile werden entfernt (rückgängig mit Strg+Z).", "UntPlan - Frage")) return;
        if (Aendern("Kopplungszeile löschen", AenderungsArt.Unterricht, d =>
        {
            var u = d.Unterrichte.First(x => x.Id == t.U.Id);
            // gemeinsamer Raum der Kopplung (ein Eintrag für alle Zeilen) bleibt für die übrigen Zeilen
            foreach (var p in d.Perioden.SelectMany(p => p.Plan).Where(p => p.UnterrichtId == u.Id && p.Raeume.Count > idx && !(p.Raeume.Count == 1 && u.Zeilen.Count > 1))) p.Raeume.RemoveAt(idx);
            u.Zeilen.RemoveAt(idx);
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
        // Karteikarten in der Reihenfolge des Handbuchs: Unterricht, Stundenplan, Kennzeichen, Werte, Kopplungszeile (danach weitere)
        var reihenfolge = new[] { "Unterricht", "Stundenplan", "Kennzeichen", "Kennzeichen 2", "Werte", "Kopplungszeile" };
        foreach (var gruppe in _spalten.Where(s => s.Set != null).GroupBy(s => s.Zeile ? "Kopplungszeile" : s.Karte)
                     .OrderBy(g => Array.IndexOf(reihenfolge, g.Key) is var i && i >= 0 ? i : reihenfolge.Length))
        {
            var page = new TabPage(gruppe.Key) { BackColor = Color.White };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true, AutoScroll = true };
            foreach (var s in gruppe)
            {
                // wie im Handbuch: Eingabefeld links, Beschriftung rechts daneben
                var p = new Panel { Width = 260, Height = 26 };
                p.Controls.Add(new Label { Text = s.Titel, Left = 108, Top = 4, Width = 148, AutoEllipsis = true });
                var tb = new TextBox { Left = 2, Top = 1, Width = 100 };
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
