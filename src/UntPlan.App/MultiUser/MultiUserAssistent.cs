using UntPlan.Core;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>
/// Einrichtungsassistent für den MultiUser-Betrieb: Datenbanksystem → Verbindung → Tabellen anlegen →
/// Administrator-Passwort → geöffnete Schuldatei als erste Version hochladen. Speichert die Verbindung lokal.
/// </summary>
public sealed class MultiUserAssistent : Form
{
    private readonly MultiUserEinstellungen _einst;
    private readonly List<Panel> _seiten = new();
    private readonly Label _kopf = new() { Dock = DockStyle.Top, Height = 46, Font = Ui.SchriftGross, Padding = new Padding(14, 12, 0, 0), BackColor = Color.White };
    private readonly Button _zurueck = new() { Text = "< Zurück", Width = 88, Height = 26 };
    private readonly Button _weiter = new() { Text = "Weiter >", Width = 88, Height = 26 };
    private readonly Button _fertig = new() { Text = "Fertig", Width = 88, Height = 26 };
    private readonly Button _abbrechen = new() { Text = "Abbrechen", Width = 88, Height = 26, DialogResult = DialogResult.Cancel };
    private int _seite;

    // Seite 1
    private readonly Dictionary<DatenbankArt, RadioButton> _arten = new();
    // Seite 2
    private readonly TextBox _datei = new();
    private readonly Button _dateiWaehlen = new() { Text = "…", Width = 30, Height = 23 };
    private readonly TextBox _server = new();
    private readonly NumericUpDown _port = new() { Maximum = 65535 };
    private readonly TextBox _datenbank = new();
    private readonly CheckBox _integriert = new() { Text = "Windows-Authentifizierung (integrierte Sicherheit)", AutoSize = true };
    private readonly TextBox _dbBenutzer = new();
    private readonly TextBox _dbPasswort = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _anlegen = new() { Text = "Datenbank anlegen, falls sie noch nicht existiert", AutoSize = true, Checked = true };
    private readonly List<Control> _nurDatei = new(), _nurServer = new();
    // Seite 3
    private readonly Label _status = new() { Left = 16, Top = 16, Width = 500, Height = 90 };
    private readonly NumericUpDown _autoLogout = new() { Maximum = 600 };
    // Seite 4
    private readonly Label _pwHinweis = new() { Left = 16, Top = 16, Width = 500, Height = 40 };
    private readonly TextBox _pw1 = new() { UseSystemPasswordChar = true };
    private readonly TextBox _pw2 = new() { UseSystemPasswordChar = true };
    // Seite 5
    private readonly CheckBox _hochladen = new() { Text = "Geöffnete Schuldatei als erste Version in die Datenbank speichern", AutoSize = true };
    private readonly TextBox _schulNr = new(), _schulName = new(), _schuljahr = new(), _bezeichnung = new() { Text = "Hauptversion" };
    private readonly NumericUpDown _versionNr = new() { Minimum = 1, Maximum = 999, Value = 1 };

    private MultiUserDb? _db;
    private bool _adminHatPasswort;

    /// <summary>Wurde die Einrichtung abgeschlossen?</summary>
    public bool Eingerichtet { get; private set; }

    public MultiUserAssistent(MultiUserEinstellungen einst)
    {
        _einst = einst;
        Text = "MultiUser-Assistent";
        Font = Ui.Schrift;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 400);
        CancelButton = _abbrechen;

        var unten = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        unten.Controls.Add(new Label { Dock = DockStyle.Top, Height = 1, BackColor = Color.Silver });
        _zurueck.Location = new Point(168, 10);
        _weiter.Location = new Point(260, 10);
        _fertig.Location = new Point(360, 10);
        _abbrechen.Location = new Point(460, 10);
        unten.Controls.AddRange(new Control[] { _zurueck, _weiter, _fertig, _abbrechen });
        _zurueck.Click += (_, _) => Zeigen(_seite - 1);
        _weiter.Click += (_, _) => Weiter();
        _fertig.Click += (_, _) => Fertig();

        SeiteArt();
        SeiteVerbindung();
        SeiteDatenbank();
        SeitePasswort();
        SeiteHochladen();
        foreach (var s in _seiten) Controls.Add(s);
        Controls.Add(unten);
        Controls.Add(_kopf);
        Zeigen(0);
    }

    private Panel NeueSeite(string titel)
    {
        var p = new Panel { Dock = DockStyle.Fill, Tag = titel, Visible = false };
        _seiten.Add(p);
        return p;
    }

    private void SeiteArt()
    {
        var p = NeueSeite("1. Datenbanksystem wählen");
        p.Controls.Add(new Label
        {
            Text = "Im MultiUser-Betrieb arbeiten mehrere Planer gleichzeitig mit denselben Daten. Alle Daten liegen in einer gemeinsamen Datenbank; " +
                   "jede Änderung wird sofort gespeichert. Wählen Sie das Datenbanksystem:",
            Left = 16, Top = 12, Width = 520, Height = 48,
        });
        var y = 68;
        foreach (var a in Enum.GetValues<DatenbankArt>())
        {
            var rb = new RadioButton { Text = Verbindungsdaten.ArtText(a), Left = 24, Top = y, Width = 480, Checked = a == _einst.Verbindung.Art };
            if (a == DatenbankArt.Access && !OperatingSystem.IsWindows()) rb.Enabled = false;
            _arten[a] = rb;
            p.Controls.Add(rb);
            y += 28;
        }
        p.Controls.Add(new Label
        {
            Text = "SQLite eignet sich für kleine Schulen (Datei auf einem gemeinsamen Netzlaufwerk). Für viele gleichzeitige Benutzer empfehlen sich SQL Server (Express) oder MySQL/MariaDB.",
            Left = 16, Top = y + 10, Width = 520, Height = 48, ForeColor = Color.DimGray,
        });
    }

    private void SeiteVerbindung()
    {
        var p = NeueSeite("2. Verbindung");
        var v = _einst.Verbindung;
        var y = 14;
        _datei.Text = v.Datei;
        _server.Text = v.Server;
        _port.Value = Math.Clamp(v.Port, 0, 65535);
        _datenbank.Text = v.Datenbank;
        _integriert.Checked = v.IntegrierteSicherheit;
        _dbBenutzer.Text = v.Benutzer;
        _dbPasswort.Text = v.Passwort;

        _nurDatei.Add(Zeile(p, "Datenbankdatei", _datei, ref y, 300));
        _dateiWaehlen.Location = new Point(_datei.Right + 4, _datei.Top - 1);
        _dateiWaehlen.Click += (_, _) => DateiWaehlen();
        p.Controls.Add(_dateiWaehlen);
        _nurDatei.Add(_dateiWaehlen);
        _nurServer.Add(Zeile(p, "Server", _server, ref y, 300));
        _nurServer.Add(Zeile(p, "Port (0 = Standard)", _port, ref y, 80));
        _nurServer.Add(Zeile(p, "Datenbank", _datenbank, ref y, 300));
        _nurServer.Add(Zeile(p, "", _integriert, ref y, 300));
        _nurServer.Add(Zeile(p, "Benutzer", _dbBenutzer, ref y, 200));
        Zeile(p, "Passwort", _dbPasswort, ref y, 200);
        Zeile(p, "", _anlegen, ref y, 300);
        _integriert.CheckedChanged += (_, _) => Sichtbarkeit();
        var test = new Button { Text = "Verbindung testen", Left = 172, Top = y + 6, Width = 140, Height = 26 };
        test.Click += (_, _) => Testen();
        p.Controls.Add(test);
    }

    private static Control Zeile(Panel p, string text, Control c, ref int y, int breite)
    {
        Ui.Zeile(p, text, c, ref y, 160, breite);
        return c;
    }

    private void SeiteDatenbank()
    {
        var p = NeueSeite("3. Datenbank einrichten");
        p.Controls.Add(_status);
        var y = 120;
        Ui.Zeile(p, "Automatisch abmelden nach (Minuten, 0 = nie)", _autoLogout, ref y, 290, 80);
        p.Controls.Add(new Label
        {
            Text = "Nach der eingestellten Zeit ohne Tastatur- oder Mausaktivität fragt UntPlan \"Sitzung beenden?\" und meldet nach 10 Sekunden ab.",
            Left = 16, Top = y + 4, Width = 520, Height = 40, ForeColor = Color.DimGray,
        });
    }

    private void SeitePasswort()
    {
        var p = NeueSeite("4. Administrator-Passwort");
        p.Controls.Add(_pwHinweis);
        var y = 64;
        Ui.Zeile(p, "Passwort", _pw1, ref y, 160, 200);
        Ui.Zeile(p, "Wiederholung", _pw2, ref y, 160, 200);
    }

    private void SeiteHochladen()
    {
        var p = NeueSeite("5. Erste Version");
        var y = 14;
        Ui.Zeile(p, "", _hochladen, ref y, 0);
        var d = AppZustand.D.Einstellungen;
        _schulNr.Text = d.Schulnummer.Length > 0 ? d.Schulnummer : "000000";
        _schulName.Text = d.Schulname;
        _schuljahr.Text = ZielVersionDialog.SchuljahrZu(d.SchuljahrVon);
        Ui.Zeile(p, "Schulnummer", _schulNr, ref y, 160, 200);
        Ui.Zeile(p, "Schulname", _schulName, ref y, 160, 300);
        Ui.Zeile(p, "Schuljahr (JJJJ/JJJJ)", _schuljahr, ref y, 160, 120);
        Ui.Zeile(p, "Versionsnummer", _versionNr, ref y, 160, 80);
        Ui.Zeile(p, "Bezeichnung", _bezeichnung, ref y, 160, 300);
        p.Controls.Add(new Label
        {
            Text = "Eine vorhandene Version gleicher Nummer wird überschrieben. Weitere Schulen, Schuljahre und Versionen legen Sie im Anmeldedialog mit <Neu> an.",
            Left = 16, Top = y + 6, Width = 520, Height = 40, ForeColor = Color.DimGray,
        });
    }

    private DatenbankArt Art => _arten.First(a => a.Value.Checked).Key;

    private void Sichtbarkeit()
    {
        var datei = Art is DatenbankArt.SQLite or DatenbankArt.Access;
        foreach (var c in _nurDatei) c.Visible = datei;
        foreach (var c in _nurServer) c.Visible = !datei;
        _integriert.Visible = Art == DatenbankArt.SqlServer;
        _dbBenutzer.Enabled = !(Art == DatenbankArt.SqlServer && _integriert.Checked);
        _dbPasswort.Enabled = Art != DatenbankArt.SQLite && _dbBenutzer.Enabled;
    }

    private Verbindungsdaten Verbindung() => new()
    {
        Art = Art,
        Datei = _datei.Text.Trim(),
        Server = _server.Text.Trim(),
        Port = (int)_port.Value,
        Datenbank = _datenbank.Text.Trim(),
        IntegrierteSicherheit = _integriert.Checked,
        Benutzer = _dbBenutzer.Text.Trim(),
        Passwort = _dbPasswort.Text,
    };

    private void DateiWaehlen()
    {
        using var d = new SaveFileDialog
        {
            Filter = Art == DatenbankArt.Access ? "Access-Datenbank (*.accdb)|*.accdb|Alle Dateien|*.*" : "SQLite-Datenbank (*.db)|*.db|Alle Dateien|*.*",
            OverwritePrompt = false,
            FileName = _datei.Text,
        };
        if (d.ShowDialog(this) == DialogResult.OK) _datei.Text = d.FileName;
    }

    private void Testen()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var v = Verbindung();
            if (v.IstDateiDatenbank && !File.Exists(v.Datei) && !_anlegen.Checked) throw new MultiUserException("Die Datenbankdatei existiert nicht.");
            var db = new MultiUserDb(v);
            if (_anlegen.Checked) db.Anbieter.DatenbankAnlegen();
            db.VerbindungTesten();
            var schema = db.SchemaVersion();
            Ui.Info("Die Verbindung wurde erfolgreich hergestellt.\n" +
                    (schema is null ? "Die Datenbank ist leer; die Tabellen werden im nächsten Schritt angelegt." : $"Die UntPlan-Tabellen sind vorhanden (Schemaversion {schema})."));
        }
        catch (Exception ex)
        {
            Ui.Info("Die Verbindung ist fehlgeschlagen:\n" + ex.Message, "Verbindung testen");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Zeigen(int seite)
    {
        _seite = Math.Clamp(seite, 0, _seiten.Count - 1);
        for (var i = 0; i < _seiten.Count; i++) _seiten[i].Visible = i == _seite;
        _kopf.Text = (string)_seiten[_seite].Tag!;
        _zurueck.Enabled = _seite > 0;
        _weiter.Enabled = _seite < _seiten.Count - 1;
        _fertig.Enabled = _seite == _seiten.Count - 1;
        AcceptButton = _seite == _seiten.Count - 1 ? _fertig : _weiter;
        if (_seite == 1) Sichtbarkeit();
    }

    private void Weiter()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            switch (_seite)
            {
                case 1:
                    Einrichten();
                    break;
                case 2:
                    _db!.AutoLogoutMinuten = (int)_autoLogout.Value;
                    break;
                case 3:
                    PasswortUebernehmen();
                    break;
            }
            Zeigen(_seite + 1);
        }
        catch (Exception ex)
        {
            Ui.Info(ex.Message, "MultiUser-Assistent");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Verbindung aufbauen, Datenbank und Tabellen anlegen.</summary>
    private void Einrichten()
    {
        var v = Verbindung();
        if (v.IstDateiDatenbank && v.Datei.Length == 0) throw new MultiUserException("Bitte geben Sie die Datenbankdatei an.");
        if (!v.IstDateiDatenbank && (v.Server.Length == 0 || v.Datenbank.Length == 0)) throw new MultiUserException("Bitte geben Sie Server und Datenbank an.");
        _db = new MultiUserDb(v);
        if (_anlegen.Checked) _db.Anbieter.DatenbankAnlegen();
        var neu = _db.Initialisieren();
        var schulen = _db.Schulen();
        var benutzer = _db.BenutzerListe();
        _status.Text = (neu ? "Die Tabellen wurden angelegt. Der Benutzer \"Administrator\" (Gruppe Administrator) wurde ohne Passwort eingerichtet.\n\n"
                            : "Es wurde eine vorhandene UntPlan-Datenbank gefunden.\n\n") +
                       $"Schemaversion: {_db.SchemaVersion()}\nSchulen: {schulen.Count}   Benutzer: {benutzer.Count}   Gruppen: {_db.Gruppen().Count}";
        _autoLogout.Value = Math.Clamp(_db.AutoLogoutMinuten, 0, 600);
        _adminHatPasswort = _db.BenutzerSuchen(MultiUserDb.AdminName)?.HatPasswort == true;
        _pwHinweis.Text = _adminHatPasswort
            ? "Der Administrator hat bereits ein Passwort. Geben Sie es zur Bestätigung ein (wird für das Hochladen der ersten Version benötigt)."
            : "Der Administrator hat noch kein Passwort. Bitte vergeben Sie jetzt ein Passwort – ohne Passwort kann sich jeder als Administrator anmelden.";
        _pw2.Visible = !_adminHatPasswort;
        _hochladen.Checked = schulen.Count == 0;
    }

    private void PasswortUebernehmen()
    {
        var admin = _db!.BenutzerSuchen(MultiUserDb.AdminName)!;
        if (_adminHatPasswort)
        {
            if (!_db.PasswortPruefen(admin.Name, _pw1.Text)) throw new MultiUserException("Das Passwort des Administrators ist falsch.");
            return;
        }
        if (_pw1.Text != _pw2.Text) throw new MultiUserException("Die Passwörter stimmen nicht überein.");
        if (_pw1.Text.Length == 0 && !Ui.Frage("Wirklich ohne Administrator-Passwort fortfahren?")) throw new MultiUserException("Bitte vergeben Sie ein Passwort.");
        if (_pw1.Text.Length > 0) _db.PasswortSetzen(admin.Id, _pw1.Text);
        _adminHatPasswort = _pw1.Text.Length > 0;
    }

    private void Fertig()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            if (_db == null) throw new MultiUserException("Die Datenbank wurde noch nicht eingerichtet.");
            if (_hochladen.Checked)
            {
                var nr = (int)_versionNr.Value;
                var sj = ZielVersionDialog.SchuljahrSichern(_db, _schulNr.Text.Trim(), _schulName.Text.Trim(), _schuljahr.Text.Trim());
                _db.DatenSpeichern(Dokument.Klonen(AppZustand.D), sj.Id, nr, _bezeichnung.Text.Trim(), MultiUserDb.AdminName, "Erste Version (MultiUser-Assistent)");
                _einst.LetzteSchule = _schulNr.Text.Trim();
                _einst.LetztesSchuljahr = sj.Nr;
                _einst.LetzteVersion = nr;
            }
            _einst.Verbindung = _db.Anbieter.Daten;
            if (_einst.LetzterBenutzer.Length == 0) _einst.LetzterBenutzer = MultiUserDb.AdminName;
            _einst.Speichern();
            Eingerichtet = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            Ui.Info(ex.Message, "MultiUser-Assistent");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
