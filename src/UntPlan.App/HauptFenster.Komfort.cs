using System.Text.Json;
using UntPlan.Core;

namespace UntPlan.App;

/// <summary>Automatisches Speichern, benannte Fensteranordnungen und Wiederherstellen des zuletzt benutzten Modus.</summary>
public sealed partial class HauptFenster
{
    private readonly System.Windows.Forms.Timer _autoSpeichern = new();

    private static bool Selbsttest => Program.Argumente.Any(a => a.StartsWith("--selbsttest", StringComparison.OrdinalIgnoreCase));

    private void KomfortVorbereiten()
    {
        _autoSpeichern.Tick += (_, _) => AutoSpeichern();
        AutoSpeichernEinstellen();
        FormClosed += (_, _) =>
        {
            _autoSpeichern.Stop();
            if (Selbsttest) return;
            AppZustand.Einstellungen.LetzterModusVertretung = AppZustand.Vertretungsmodus;
            AppZustand.Einstellungen.Speichern();
        };
    }

    /// <summary>Intervall aus den Einstellungen übernehmen (0 = aus).</summary>
    public void AutoSpeichernEinstellen()
    {
        _autoSpeichern.Stop();
        var min = AppZustand.Einstellungen.AutoSpeichernMinuten;
        if (min <= 0 || Selbsttest) return;
        _autoSpeichern.Interval = Math.Clamp(min, 1, 120) * 60_000;
        _autoSpeichern.Start();
    }

    /// <summary>Den beim letzten Beenden aktiven Modus wiederherstellen.</summary>
    private void ModusWiederherstellen()
    {
        if (!Selbsttest && AppZustand.Einstellungen.ModusMerken && AppZustand.Einstellungen.LetzterModusVertretung && !AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
    }

    /// <summary>
    /// Speichert geänderte Daten automatisch: in die geöffnete Datei bzw. – solange sie noch keinen Namen hat – als
    /// Wiederherstellungskopie im Benutzerordner. Im MultiUser-Betrieb wird ohnehin jede Änderung sofort gespeichert.
    /// </summary>
    private void AutoSpeichern()
    {
        var doc = AppZustand.Doc;
        if (!doc.IstGeaendert || MultiUser.MultiUserSitzung.Aktiv || OwnedForms.Any(f => f.Modal) || ActiveForm is Form { Modal: true }) return;
        try
        {
            if (doc.Pfad is string pfad)
            {
                doc.Speichern(pfad);
                DokGeaendert(AenderungsArt.Einstellungen);
                _statusLinks.Text = $"Automatisch gespeichert um {DateTime.Now:HH:mm}";
            }
            else
            {
                var ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan", "Wiederherstellung");
                Directory.CreateDirectory(ordner);
                var name = string.IsNullOrWhiteSpace(doc.Daten.Einstellungen.Schulname) ? "Unbenannt" : string.Concat(doc.Daten.Einstellungen.Schulname.Split(Path.GetInvalidFileNameChars()));
                var ziel = Path.Combine(ordner, name + Dokument.Dateiendung);
                File.WriteAllText(ziel, JsonSerializer.Serialize(doc.Daten, Dokument.JsonOptionen));
                _statusLinks.Text = $"Wiederherstellungskopie gespeichert um {DateTime.Now:HH:mm} ({ziel})";
            }
        }
        catch (Exception ex)
        {
            _statusLinks.Text = "Automatisches Speichern fehlgeschlagen: " + ex.Message;
        }
    }

    /// <summary>Menü der benannten Fensteranordnungen (speichern, laden, löschen).</summary>
    internal void FensteranordnungMenue()
    {
        var e = AppZustand.Einstellungen;
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        cm.Items.Add("Aktuelle Anordnung speichern…", Symbole.Get("speichern", 16), (_, _) =>
        {
            var name = Ui.Eingabe("Name der Fensteranordnung:", "Fensteranordnung speichern", AppZustand.Vertretungsmodus ? "Vertretungsplanung" : "Stundenplanung");
            if (string.IsNullOrWhiteSpace(name)) return;
            e.Fensteranordnungen[name.Trim()] = new Fensteranordnung { Vertretungsmodus = AppZustand.Vertretungsmodus, Fenster = LayoutAufnehmen() };
            e.Speichern();
        });
        if (e.Fensteranordnungen.Count > 0)
        {
            cm.Items.Add(new ToolStripSeparator());
            foreach (var (name, anordnung) in e.Fensteranordnungen.OrderBy(x => x.Key))
            {
                var a = anordnung;
                cm.Items.Add($"{name}  ({(a.Vertretungsmodus ? "Vertretungsplanung" : "Stundenplanung")}, {a.Fenster.Count} Fenster)", Symbole.Get("uebersicht", 16), (_, _) =>
                {
                    if (a.Vertretungsmodus != AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
                    AlleSchliessen();
                    LayoutOeffnen(a.Fenster);
                });
            }
            var loeschen = new ToolStripMenuItem("Anordnung löschen", Symbole.Get("loeschen", 16));
            foreach (var name in e.Fensteranordnungen.Keys.OrderBy(x => x).ToList())
                loeschen.DropDownItems.Add(name, null, (_, _) => { if (Ui.Frage($"Fensteranordnung \"{name}\" löschen?")) { e.Fensteranordnungen.Remove(name); e.Speichern(); } });
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(loeschen);
        }
        cm.Show(Cursor.Position);
    }
}
