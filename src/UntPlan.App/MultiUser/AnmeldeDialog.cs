using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>
/// Anmeldung an der MultiUser-Datenbank: Schulnummer, Schuljahr und Version (jeweils mit &lt;Neu&gt;),
/// Benutzer und Passwort, Schaltfläche "Passwort ändern".
/// </summary>
public sealed class AnmeldeDialog : Dialog
{
    private const string Neu = "<Neu>";
    private readonly MultiUserDb _db;
    private readonly MultiUserEinstellungen _einst;
    private readonly ComboBox _schule = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _jahr = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _version = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _benutzer = new();
    private readonly TextBox _passwort = new() { UseSystemPasswordChar = true };
    private bool _fuellt;

    /// <summary>Ergebnis einer erfolgreichen Anmeldung.</summary>
    public MultiUserKlient? Klient { get; private set; }

    public AnmeldeDialog(MultiUserDb db, MultiUserEinstellungen einst) : base("Anmeldung – MultiUser", 440, 250)
    {
        _db = db;
        _einst = einst;
        var y = 14;
        Ui.Zeile(this, "Schulnummer", _schule, ref y, 110, 300);
        Ui.Zeile(this, "Schuljahr", _jahr, ref y, 110, 300);
        Ui.Zeile(this, "Version", _version, ref y, 110, 300);
        y += 8;
        Ui.Zeile(this, "Benutzer", _benutzer, ref y, 110, 200);
        Ui.Zeile(this, "Passwort", _passwort, ref y, 110, 200);
        var pw = new Button { Text = "Passwort ändern", Left = 12, Top = 250 - 36, Width = 120, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        pw.Click += (_, _) => PasswortAendern();
        Controls.Add(pw);
        Controls.Add(new Label { Text = einst.Verbindung.ToString(), Left = 12, Top = y + 2, Width = 416, Height = 18, ForeColor = Color.DimGray, AutoEllipsis = true });

        _benutzer.Text = einst.LetzterBenutzer.Length > 0 ? einst.LetzterBenutzer : MultiUserDb.AdminName;
        _schule.SelectedIndexChanged += (_, _) => Auswahl(_schule);
        _jahr.SelectedIndexChanged += (_, _) => Auswahl(_jahr);
        _version.SelectedIndexChanged += (_, _) => Auswahl(_version);
        SchulenLaden(einst.LetzteSchule);
        Shown += (_, _) => (_benutzer.Text.Length > 0 ? _passwort : _benutzer).Focus();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && !Anmelden()) e.Cancel = true;
        };
    }

    private void SchulenLaden(string waehlen)
    {
        Fuellen(_schule, _db.Schulen(), s => s.Nr == waehlen);
        JahreLaden(_einst.LetztesSchuljahr);
    }

    private void JahreLaden(string waehlen)
    {
        var s = _schule.SelectedItem as SchuleInfo;
        Fuellen(_jahr, s == null ? new List<SchuljahrInfo>() : _db.Schuljahre(s.Id), j => j.Nr == waehlen);
        VersionenLaden(_einst.LetzteVersion);
    }

    private void VersionenLaden(int waehlen)
    {
        var j = _jahr.SelectedItem as SchuljahrInfo;
        Fuellen(_version, j == null ? new List<VersionInfo>() : _db.Versionen(j.Id), v => v.Nr == waehlen);
    }

    private void Fuellen<T>(ComboBox cb, List<T> liste, Func<T, bool> vorauswahl) where T : class
    {
        _fuellt = true;
        cb.Items.Clear();
        foreach (var x in liste) cb.Items.Add(x);
        cb.Items.Add(Neu);
        var idx = liste.FindIndex(x => vorauswahl(x));
        cb.SelectedIndex = idx >= 0 ? idx : liste.Count > 0 ? liste.Count - 1 : -1;
        _fuellt = false;
    }

    private void Auswahl(ComboBox cb)
    {
        if (_fuellt) return;
        if (cb.SelectedItem is string)
        {
            try { NeuAnlegen(cb); }
            catch (Exception ex) { Ui.Fehler(ex); }
            if (cb.SelectedItem is string) cb.SelectedIndex = cb.Items.Count > 1 ? 0 : -1;
            return;
        }
        if (cb == _schule) JahreLaden("");
        else if (cb == _jahr) VersionenLaden(0);
    }

    /// <summary>Prüft Benutzer/Passwort und das Recht zum Anlegen.</summary>
    private bool DarfAnlegen(Recht recht)
    {
        var b = _db.BenutzerSuchen(_benutzer.Text);
        if (b == null || !_db.PasswortPruefen(b.Name, _passwort.Text))
        {
            Ui.Info("Bitte geben Sie zuerst Benutzer und Passwort ein.");
            return false;
        }
        var g = _db.Gruppe(b.GruppeId);
        if (g != null && Rechte.Aus(g).Hat(recht)) return true;
        Ui.Info($"Sie haben nicht das Recht \"{Rechte.Text(recht)}\".");
        return false;
    }

    private void NeuAnlegen(ComboBox cb)
    {
        if (cb == _schule)
        {
            if (!DarfAnlegen(Recht.SchuleAnlegen)) return;
            var e = ZweiFelderDialog.Zeigen(this, "Neue Schule", "Schulnummer", AppZustand.D.Einstellungen.Schulnummer, "Bezeichnung", AppZustand.D.Einstellungen.Schulname);
            if (e == null) return;
            var s = _db.SchuleAnlegen(e.Value.A, e.Value.B);
            SchulenLaden(s.Nr);
        }
        else if (cb == _jahr)
        {
            if (_schule.SelectedItem is not SchuleInfo s || !DarfAnlegen(Recht.SchuljahrAnlegen)) return;
            var e = ZweiFelderDialog.Zeigen(this, "Neues Schuljahr", "Schuljahr", ZielVersionDialog.SchuljahrZu(DateOnly.FromDateTime(DateTime.Today)), "Bezeichnung", "");
            if (e == null) return;
            var j = _db.SchuljahrAnlegen(s.Id, e.Value.A, e.Value.B);
            JahreLaden(j.Nr);
        }
        else
        {
            if (_jahr.SelectedItem is not SchuljahrInfo j || !DarfAnlegen(Recht.VersionAnlegen)) return;
            var vorhanden = _db.Versionen(j.Id);
            var nr = vorhanden.Count == 0 ? 1 : vorhanden.Max(v => v.Nr) + 1;
            var e = ZweiFelderDialog.Zeigen(this, "Neue Version", "Versionsnummer", nr.ToString(), "Bezeichnung", vorhanden.Count == 0 ? "Hauptversion" : "");
            if (e == null) return;
            if (!int.TryParse(e.Value.A, out nr)) throw new MultiUserException("Die Versionsnummer muss eine Zahl sein.");
            // Neue Version: Kopie der neuesten Version des Schuljahres bzw. der geöffneten Schuldatei
            Schuldatei daten;
            if (vorhanden.Count > 0 && Ui.Frage($"Die neue Version als Kopie von Version {vorhanden[^1].Nr} anlegen?\n(Nein = Daten der geöffneten Schuldatei übernehmen)"))
                daten = _db.Laden(vorhanden[^1].Id).Daten;
            else
                daten = Dokument.Klonen(AppZustand.D);
            var v = _db.VersionAnlegen(j.Id, nr, e.Value.B, daten, _benutzer.Text.Trim());
            VersionenLaden(v.Nr);
        }
    }

    private void PasswortAendern()
    {
        var name = _benutzer.Text.Trim();
        var b = _db.BenutzerSuchen(name);
        if (b == null) { Ui.Info("Der Benutzer existiert nicht."); return; }
        var e = PasswortDialog.Zeigen(this, b.Name, true);
        if (e == null) return;
        try
        {
            if (!_db.PasswortPruefen(b.Name, e.Value.Alt)) throw new MultiUserException("Das bisherige Passwort ist falsch.");
            _db.PasswortSetzen(b.Id, e.Value.Neu);
            _passwort.Text = e.Value.Neu;
            Ui.Info("Das Passwort wurde geändert.");
        }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    private bool Anmelden()
    {
        if (_version.SelectedItem is not VersionInfo v)
        {
            Ui.Info("Bitte wählen Sie Schule, Schuljahr und Version (oder legen Sie sie mit <Neu> an).");
            return false;
        }
        try
        {
            Klient = MultiUserKlient.Anmelden(_db, _benutzer.Text, _passwort.Text, v.Id);
            _einst.LetzteSchule = Klient.Schule.Nr;
            _einst.LetztesSchuljahr = Klient.Schuljahr.Nr;
            _einst.LetzteVersion = Klient.Version.Nr;
            _einst.LetzterBenutzer = Klient.Benutzer.Name;
            return true;
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            return false;
        }
    }
}
