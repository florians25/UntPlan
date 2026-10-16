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
        SchnellanordnungenAufbauen();
        // vor dem Speichern der Ansicht in die Schuldatei: aktuelle Fensteranordnung des Modus aufnehmen
        AppZustand.AnsichtVorSpeichern = () =>
        {
            if (!IsDisposed) AppZustand.Einstellungen.Fenstergruppen[AppZustand.Vertretungsmodus ? GruppeVertretung : GruppeStundenplan] = LayoutAufnehmen();
        };
        FormClosed += (_, _) =>
        {
            _autoSpeichern.Stop();
            AppZustand.AnsichtVorSpeichern = null;
            // exklusiven Zugriff auf die Schuldatei freigeben
            Dateisperre.AlleFreigeben();
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
        if (!doc.IstGeaendert || doc.Schreibgeschuetzt || MultiUser.MultiUserSitzung.Aktiv || OwnedForms.Any(f => f.Modal) || ActiveForm is Form { Modal: true }) return;
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

    /// <summary>Kurze Rückmeldung links in der Statusleiste.</summary>
    internal void StatusMelden(string text) => _statusLinks.Text = text;

    private readonly ToolStripButton[] _schnellAnordnung = new ToolStripButton[8];

    /// <summary>
    /// Fenster-Schnellknöpfe 1–8 rechts in der obersten Leiste (wie Fenstergruppen): Klick stellt die gespeicherte
    /// Anordnung her, Rechtsklick speichert die aktuelle Anordnung unter dieser Nummer.
    /// </summary>
    private void SchnellanordnungenAufbauen()
    {
        // wie im Vorbild 2026: kleine Fenstersymbole rechts in der Registerleiste
        for (var n = 1; n <= 8; n++)
        {
            var nr = n;
            var b = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Image, AutoSize = false, Width = 20, Height = 20, Margin = new Padding(0, 1, 0, 1) };
            b.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right) SchnellanordnungSpeichern(nr);
                else if (e.Button == MouseButtons.Left) SchnellanordnungOeffnen(nr);
            };
            _schnellAnordnung[nr - 1] = b;
            _ribbon.Rechts.Items.Add(b);
        }
        SchnellanordnungenAnzeigen();
    }

    private void SchnellanordnungenAnzeigen()
    {
        var e = AppZustand.Einstellungen;
        for (var n = 1; n <= 8; n++)
        {
            var b = _schnellAnordnung[n - 1];
            if (b is null) continue;
            var belegt = e.Schnellanordnungen.TryGetValue(n, out var a);
            b.Image = SchnellBild(n, belegt);
            b.ToolTipText = belegt
                ? $"{n}: {(a!.Name.Length > 0 ? a.Name : "Fensteranordnung")} ({(a.Vertretungsmodus ? "Vertretungsplanung" : "Stundenplanung")}, {a.Fenster.Count} Fenster)\nKlick: öffnen · Rechtsklick: aktuelle Anordnung hier speichern"
                : $"{n}: frei\nRechtsklick: aktuelle Fensteranordnung hier speichern";
        }
    }

    /// <summary>Kleines Fenstersymbol mit Nummer (hell = frei).</summary>
    /// <summary>Schnellknopf als schlichte Ziffer ohne Rahmen (belegt: weiß, sonst gedämpft).</summary>
    private static Bitmap SchnellBild(int n, bool belegt)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font("Segoe UI", 11f, belegt ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var t = new SolidBrush(belegt ? Color.White : Color.FromArgb(150, 255, 255, 255));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(n.ToString(), f, t, new RectangleF(0, 0, 16, 16), sf);
        return bmp;
    }

    private void SchnellanordnungSpeichern(int n)
    {
        var e = AppZustand.Einstellungen;
        var vorschlag = e.Schnellanordnungen.TryGetValue(n, out var alt) && alt.Name.Length > 0 ? alt.Name : AppZustand.Vertretungsmodus ? "Vertretungsplanung" : "Stundenplanung";
        var name = Ui.Eingabe($"Aktuelle Fensteranordnung unter Nummer {n} speichern – Bezeichnung:", "Fenster-Schnellknopf", vorschlag);
        if (name is null) return;
        e.Schnellanordnungen[n] = new Fensteranordnung { Name = name.Trim(), Vertretungsmodus = AppZustand.Vertretungsmodus, Fenster = LayoutAufnehmen() };
        e.Speichern();
        SchnellanordnungenAnzeigen();
        _statusLinks.Text = $"Fensteranordnung {n} gespeichert.";
    }

    private void SchnellanordnungOeffnen(int n)
    {
        if (!AppZustand.Einstellungen.Schnellanordnungen.TryGetValue(n, out var a))
        {
            _statusLinks.Text = $"Schnellknopf {n} ist noch frei – Rechtsklick speichert die aktuelle Fensteranordnung.";
            return;
        }
        if (a.Vertretungsmodus != AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        AlleSchliessen();
        LayoutOeffnen(a.Fenster);
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
