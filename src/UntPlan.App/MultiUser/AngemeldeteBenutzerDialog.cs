using UntPlan.App.Dialoge;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>Angemeldete Benutzer mit Tätigkeit; Nachricht an alle bzw. an einen Benutzer; Benutzer abmelden (Administrator).</summary>
public sealed class AngemeldeteBenutzerDialog : Dialog
{
    private readonly MultiUserKlient _klient;
    private readonly DataGridView _raster = new() { Left = 10, Top = 10, Width = 700, Height = 290, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
    private readonly Dictionary<int, string> _versionen = new();

    public AngemeldeteBenutzerDialog(MultiUserKlient klient) : base("Angemeldete Benutzer", 720, 350)
    {
        _klient = klient;
        Abbrechen.Visible = false;
        Ok.Text = "Schließen";
        Ok.Location = Abbrechen.Location;
        CancelButton = Ok;
        Ui.RasterGestalten(_raster, false);
        _raster.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (titel, breite) in new[] { ("Benutzer", 110), ("Rechner", 100), ("Version", 150), ("Angemeldet", 110), ("Letzte Aktivität", 110), ("Tätigkeit", 160) })
            _raster.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = titel, Width = breite });
        Controls.Add(_raster);

        var x = 10;
        Button Knopf(string text, Action klick, bool aktiv = true)
        {
            var b = new Button { Text = text, Left = x, Top = 350 - 36, Width = 128, Height = 26, Enabled = aktiv, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            b.Click += (_, _) => { try { klick(); } catch (Exception ex) { Ui.Fehler(ex); } };
            Controls.Add(b);
            x += 134;
            return b;
        }
        Knopf("Aktualisieren", Laden);
        Knopf("Nachricht an alle…", () => Senden(null));
        Knopf("Nachricht an…", () => { if (Gewaehlt is { } s) Senden(s.Benutzer); });
        Knopf("Abmelden", Abmelden, klient.Rechte.Hat(Recht.Benutzerverwaltung));
        Laden();
    }

    private SitzungInfo? Gewaehlt => _raster.CurrentRow?.Tag as SitzungInfo;

    private string VersionText(int id)
    {
        if (_versionen.TryGetValue(id, out var t)) return t;
        try
        {
            var (s, j, v) = _klient.Db.VersionPfad(id);
            t = $"{s.Nr} ~ {j.Nr} ~ {v.Nr}";
        }
        catch { t = "?"; }
        return _versionen[id] = t;
    }

    private void Laden()
    {
        _raster.Rows.Clear();
        foreach (var s in _klient.Db.Sitzungen())
        {
            var i = _raster.Rows.Add(s.Benutzer + (s.Id == _klient.SitzungId ? " (Sie)" : ""), s.Rechner, VersionText(s.VersionId), s.Angemeldet.ToString("g"), s.Aktivitaet.ToString("T"), s.Taetigkeit);
            _raster.Rows[i].Tag = s;
        }
    }

    private void Senden(string? empfaenger)
    {
        var text = Ui.Eingabe(empfaenger is null ? "Nachricht an alle angemeldeten Benutzer:" : $"Nachricht an {empfaenger}:", "Benutzernachricht");
        if (string.IsNullOrWhiteSpace(text)) return;
        var n = _klient.NachrichtSenden(empfaenger, text.Trim());
        Ui.Info(n == 0 ? "Es ist kein weiterer Benutzer angemeldet." : $"Die Nachricht wurde an {n} Benutzer gesendet.");
    }

    private void Abmelden()
    {
        if (Gewaehlt is not { } s) return;
        if (s.Id == _klient.SitzungId) { Ui.Info("Die eigene Sitzung beenden Sie über \"Abmelden\" im Menü."); return; }
        if (!Ui.Frage($"{s.Benutzer} ({s.Rechner}) abmelden? Nicht gespeicherte Eingaben dieses Benutzers gehen verloren.")) return;
        _klient.Db.SitzungBeenden(s.Id);
        Laden();
    }
}
