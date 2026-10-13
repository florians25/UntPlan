using UntPlan.App.Fenster;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>
/// Benutzerverwaltung der MultiUser-Datenbank: Benutzer (Name, Gruppe, letztes Login, Abteilung, Passwort),
/// Gruppen mit Rechten (Ja/Nein und Stufen je Bereich) und Einschränkungstabelle je Gruppe.
/// </summary>
public sealed class BenutzerverwaltungFenster : MdiFenster
{
    private readonly MultiUserDb _db;
    private readonly DataGridView _benutzer = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _gruppen = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _einschr = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _einschrGruppe = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private List<Gruppe> _gruppenListe = new();
    private bool _laedt;

    public BenutzerverwaltungFenster(MultiUserDb db)
    {
        _db = db;
        Text = "Benutzerverwaltung";
        HilfeThema = "MultiUser";
        Icon = SymbolIcon("lehrer");
        Size = new Size(900, 480);
        Positionieren();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(SeiteBenutzer());
        tabs.TabPages.Add(SeiteGruppen());
        tabs.TabPages.Add(SeiteEinschraenkungen());
        Controls.Add(tabs);
        Laden();
    }

    private TabPage SeiteBenutzer()
    {
        var seite = new TabPage("Benutzer");
        Ui.RasterGestalten(_benutzer);
        _benutzer.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _benutzer.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 160 });
        _benutzer.Columns.Add(new DataGridViewComboBoxColumn { Name = "Gruppe", HeaderText = "Gruppe", Width = 160, FlatStyle = FlatStyle.Flat });
        _benutzer.Columns.Add(new DataGridViewTextBoxColumn { Name = "Login", HeaderText = "Letztes Login", Width = 130, ReadOnly = true });
        _benutzer.Columns.Add(new DataGridViewTextBoxColumn { Name = "Abteilung", HeaderText = "Abteilung", Width = 100 });
        _benutzer.Columns.Add(new DataGridViewTextBoxColumn { Name = "Passwort", HeaderText = "Passwort", Width = 80, ReadOnly = true });
        _benutzer.CellValueChanged += (_, e) => BenutzerGeaendert(e.RowIndex);
        _benutzer.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_benutzer.CurrentCell is DataGridViewComboBoxCell) _benutzer.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _benutzer.DataError += (_, e) => e.Cancel = true;
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neuer Benutzer", "neu", BenutzerNeu, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", BenutzerLoeschen, false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Passwort setzen", "einstellungen", () => PasswortSetzen(false), false));
        ts.Items.Add(Knopf("Passwort löschen", "minus", () => PasswortSetzen(true), false));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Neu laden", "wiederholen", Laden, false));
        seite.Controls.Add(_benutzer);
        seite.Controls.Add(ts);
        return seite;
    }

    private TabPage SeiteGruppen()
    {
        var seite = new TabPage("Gruppen und Rechte");
        Ui.RasterGestalten(_gruppen);
        _gruppen.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Gruppe", Width = 140, Frozen = true });
        foreach (var r in Enum.GetValues<Recht>())
            _gruppen.Columns.Add(new DataGridViewCheckBoxColumn { Name = "R:" + r, HeaderText = Rechte.Text(r), Width = 80 });
        foreach (var b in Enum.GetValues<Rechtebereich>())
        {
            var c = new DataGridViewComboBoxColumn { Name = "B:" + b, HeaderText = Rechte.Text(b), Width = 130, FlatStyle = FlatStyle.Flat };
            foreach (var s in Enum.GetValues<Stufe>()) c.Items.Add(Rechte.Text(s));
            _gruppen.Columns.Add(c);
        }
        _gruppen.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _gruppen.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _gruppen.CellValueChanged += (_, e) => GruppeGeaendert(e.RowIndex);
        _gruppen.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_gruppen.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell) _gruppen.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _gruppen.DataError += (_, e) => e.Cancel = true;
        var ts = Werkzeugleiste();
        ts.Items.Add(Knopf("Neue Gruppe", "neu", GruppeNeu, false));
        ts.Items.Add(Knopf("Löschen", "loeschen", GruppeLoeschen, false));
        seite.Controls.Add(_gruppen);
        seite.Controls.Add(ts);
        return seite;
    }

    private TabPage SeiteEinschraenkungen()
    {
        var seite = new TabPage("Einschränkungen");
        Ui.RasterGestalten(_einschr);
        _einschr.AllowUserToAddRows = true;
        _einschr.AllowUserToDeleteRows = true;
        _einschr.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schule", HeaderText = "Schule", Width = 100 });
        _einschr.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schuljahr", HeaderText = "Schuljahr", Width = 100 });
        _einschr.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Version", Width = 70 });
        _einschr.Columns.Add(new DataGridViewTextBoxColumn { Name = "Periode", HeaderText = "Periode", Width = 120 });
        _einschr.Columns.Add(new DataGridViewTextBoxColumn { Name = "Abteilung", HeaderText = "Abteilung", Width = 100 });
        _einschr.Columns.Add(new DataGridViewCheckBoxColumn { Name = "NurLesen", HeaderText = "Nur lesen", Width = 80 });
        var ts = Werkzeugleiste();
        ts.Items.Add(new ToolStripLabel("Gruppe: "));
        ts.Items.Add(new ToolStripControlHost(_einschrGruppe));
        ts.Items.Add(Knopf("Speichern", "speichern", EinschraenkungenSpeichern, false));
        _einschrGruppe.SelectedIndexChanged += (_, _) => EinschraenkungenLaden();
        seite.Controls.Add(_einschr);
        seite.Controls.Add(ts);
        seite.Controls.Add(new Label
        {
            Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.DimGray, Padding = new Padding(4),
            Text = "Leere Felder gelten für alle. Hat eine Gruppe Einschränkungen, dürfen sich ihre Benutzer nur an passenden Versionen anmelden; \"Nur lesen\" verhindert Änderungen.",
        });
        return seite;
    }

    private void Laden()
    {
        _laedt = true;
        try
        {
            _gruppenListe = _db.Gruppen();
            var gc = (DataGridViewComboBoxColumn)_benutzer.Columns["Gruppe"]!;
            gc.Items.Clear();
            foreach (var g in _gruppenListe) gc.Items.Add(g.Name);

            _benutzer.Rows.Clear();
            foreach (var b in _db.BenutzerListe())
            {
                var i = _benutzer.Rows.Add(b.Name, _gruppenListe.FirstOrDefault(g => g.Id == b.GruppeId)?.Name, b.LetztesLogin?.ToString("g") ?? "", b.Abteilung, b.HatPasswort ? "ja" : "–");
                _benutzer.Rows[i].Tag = b;
                if (b.IstAdministrator) _benutzer.Rows[i].Cells["Gruppe"].ReadOnly = _benutzer.Rows[i].Cells["Name"].ReadOnly = true;
            }

            _gruppen.Rows.Clear();
            foreach (var g in _gruppenListe)
            {
                var i = _gruppen.Rows.Add();
                var row = _gruppen.Rows[i];
                row.Tag = g;
                row.Cells["Name"].Value = g.Name;
                foreach (var r in Enum.GetValues<Recht>()) row.Cells["R:" + r].Value = g.Rechte.Contains(r);
                foreach (var b in Enum.GetValues<Rechtebereich>()) row.Cells["B:" + b].Value = Rechte.Text(g.Stufen.GetValueOrDefault(b, Stufe.Keine));
                row.ReadOnly = g.Name == MultiUserDb.AdminGruppe;
            }

            var gewaehlt = (_einschrGruppe.SelectedItem as Gruppe)?.Id;
            _einschrGruppe.Items.Clear();
            foreach (var g in _gruppenListe.Where(g => g.Name != MultiUserDb.AdminGruppe)) _einschrGruppe.Items.Add(g);
            var idx = _gruppenListe.Where(g => g.Name != MultiUserDb.AdminGruppe).ToList().FindIndex(g => g.Id == gewaehlt);
            if (_einschrGruppe.Items.Count > 0) _einschrGruppe.SelectedIndex = Math.Max(0, idx);
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
        }
        finally
        {
            _laedt = false;
        }
        EinschraenkungenLaden();
    }

    private BenutzerInfo? AktuellerBenutzer => _benutzer.CurrentRow?.Tag as BenutzerInfo;

    private void BenutzerGeaendert(int zeile)
    {
        if (_laedt || zeile < 0 || _benutzer.Rows[zeile].Tag is not BenutzerInfo b) return;
        var row = _benutzer.Rows[zeile];
        try
        {
            b.Name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? b.Name;
            b.Abteilung = Convert.ToString(row.Cells["Abteilung"].Value)?.Trim() ?? "";
            var g = _gruppenListe.FirstOrDefault(x => x.Name == Convert.ToString(row.Cells["Gruppe"].Value));
            if (g != null) b.GruppeId = g.Id;
            _db.BenutzerAendern(b);
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            BeginInvoke(Laden);
        }
    }

    private void BenutzerNeu()
    {
        var name = Ui.Eingabe("Name des neuen Benutzers:", "Neuer Benutzer");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var gruppe = _gruppenListe.FirstOrDefault(g => g.Name == "Info") ?? _gruppenListe.Last();
            var b = _db.BenutzerAnlegen(name, gruppe.Id);
            Laden();
            if (Ui.Frage($"Für {b.Name} jetzt ein Passwort vergeben?"))
            {
                var pw = PasswortDialog.Zeigen(this, b.Name, false);
                if (pw != null) _db.PasswortSetzen(b.Id, pw.Value.Neu);
                Laden();
            }
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    private void BenutzerLoeschen()
    {
        var b = AktuellerBenutzer;
        if (b == null || !Ui.Frage($"Benutzer {b.Name} löschen?")) return;
        try { _db.BenutzerLoeschen(b.Id); }
        catch (Exception ex) { Ui.Fehler(ex); }
        Laden();
    }

    private void PasswortSetzen(bool loeschen)
    {
        var b = AktuellerBenutzer;
        if (b == null) return;
        try
        {
            if (loeschen)
            {
                if (!Ui.Frage($"Passwort von {b.Name} löschen?")) return;
                _db.PasswortSetzen(b.Id, null);
            }
            else
            {
                var pw = PasswortDialog.Zeigen(this, b.Name, false);
                if (pw == null) return;
                _db.PasswortSetzen(b.Id, pw.Value.Neu);
            }
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        Laden();
    }

    private void GruppeGeaendert(int zeile)
    {
        if (_laedt || zeile < 0 || _gruppen.Rows[zeile].Tag is not Gruppe g) return;
        var row = _gruppen.Rows[zeile];
        try
        {
            g.Name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? g.Name;
            g.Rechte.Clear();
            foreach (var r in Enum.GetValues<Recht>())
                if (row.Cells["R:" + r].Value is true) g.Rechte.Add(r);
            foreach (var b in Enum.GetValues<Rechtebereich>())
            {
                var text = Convert.ToString(row.Cells["B:" + b].Value);
                g.Stufen[b] = Enum.GetValues<Stufe>().FirstOrDefault(s => Rechte.Text(s) == text);
            }
            _db.GruppeSpeichern(g);
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            BeginInvoke(Laden);
        }
    }

    private void GruppeNeu()
    {
        var name = Ui.Eingabe("Name der neuen Gruppe:", "Neue Gruppe");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var g = new Gruppe { Name = name };
            foreach (var b in Enum.GetValues<Rechtebereich>()) g.Stufen[b] = Stufe.NurLesen;
            _db.GruppeSpeichern(g);
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        Laden();
    }

    private void GruppeLoeschen()
    {
        if (_gruppen.CurrentRow?.Tag is not Gruppe g || !Ui.Frage($"Gruppe {g.Name} löschen?")) return;
        try { _db.GruppeLoeschen(g.Id); }
        catch (Exception ex) { Ui.Fehler(ex); }
        Laden();
    }

    private void EinschraenkungenLaden()
    {
        _einschr.Rows.Clear();
        if (_einschrGruppe.SelectedItem is not Gruppe g) return;
        try
        {
            foreach (var e in _db.Einschraenkungen(g.Id))
                _einschr.Rows.Add(e.Schule, e.Schuljahr, e.Version == 0 ? "" : e.Version.ToString(), e.Periode, e.Abteilung, e.NurLesen);
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    private void EinschraenkungenSpeichern()
    {
        if (_einschrGruppe.SelectedItem is not Gruppe g) return;
        _einschr.EndEdit();
        string Wert(DataGridViewRow r, string spalte) => Convert.ToString(r.Cells[spalte].Value)?.Trim() ?? "";
        var zeilen = _einschr.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new Einschraenkung
        {
            GruppeId = g.Id,
            Schule = Wert(r, "Schule"),
            Schuljahr = Wert(r, "Schuljahr").Replace('-', '/'),
            Version = int.TryParse(Wert(r, "Version"), out var v) ? v : 0,
            Periode = Wert(r, "Periode"),
            Abteilung = Wert(r, "Abteilung"),
            NurLesen = r.Cells["NurLesen"].Value is true,
        }).ToList();
        try
        {
            _db.EinschraenkungenSpeichern(g.Id, zeilen);
            Ui.Info($"Die Einschränkungen der Gruppe {g.Name} wurden gespeichert.");
        }
        catch (Exception ex) { Ui.Fehler(ex); }
        EinschraenkungenLaden();
    }
}
