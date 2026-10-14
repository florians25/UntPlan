using System.Text.RegularExpressions;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>Einstiegspunkte des MultiUser-Moduls für Menüband und Hauptfenster.</summary>
public static class MultiUserMenue
{
    private static BenutzerverwaltungFenster? _verwaltung;

    /// <summary>Menüeinträge (Text, Symbolname aus <see cref="Symbole"/>, Aktion).</summary>
    public static List<(string Text, string Symbol, Action Klick)> Eintraege(IWin32Window owner) => new()
    {
        ("MultiUser-Assistent…", "assistent", () => Assistent(owner)),
        ("Anmelden…", "lehrer", () => Anmelden(owner)),
        ("Abmelden", "zurueck", Abmelden),
        ("Neuanzeige", "wiederholen", Neuanzeige),
        ("Benutzerverwaltung", "einstellungen", Benutzerverwaltung),
        ("Angemeldete Benutzer…", "info", () => AngemeldeteBenutzer(owner)),
        ("Datei in Datenbank speichern…", "speichern", () => InDatenbankSpeichern(owner)),
        ("Version als Datei exportieren…", "export", () => AlsDateiExportieren(owner)),
        ("Exklusivbetrieb ein/aus", "fixieren", () => Exklusiv(owner)),
    };

    /// <summary>Einrichtungsassistent (Datenbank wählen, Tabellen anlegen, Administrator-Passwort, erste Version).</summary>
    public static void Assistent(IWin32Window owner)
    {
        Sicher(() =>
        {
            var einst = MultiUserEinstellungen.Laden();
            using var a = new MultiUserAssistent(einst);
            if (a.ShowDialog(owner) == DialogResult.OK && a.Eingerichtet && Ui.Frage("Die MultiUser-Datenbank ist eingerichtet. Jetzt anmelden?"))
                Anmelden(owner);
        });
    }

    /// <summary>Anmeldedialog (Schule, Schuljahr, Version, Benutzer, Passwort); lädt danach die Daten aus der Datenbank.</summary>
    public static void Anmelden(IWin32Window owner)
    {
        Sicher(() =>
        {
            var einst = MultiUserEinstellungen.Laden();
            if (!einst.Eingerichtet)
            {
                if (Ui.Frage("Es ist noch keine MultiUser-Datenbank eingerichtet. MultiUser-Assistent starten?")) Assistent(owner);
                return;
            }
            if (!VorWechselFragen()) return;
            var db = new MultiUserDb(einst.Verbindung);
            db.Initialisieren();
            using var d = new AnmeldeDialog(db, einst);
            if (d.ShowDialog(owner) != DialogResult.OK || d.Klient == null) return;
            einst.Speichern();
            Starten(owner, d.Klient);
        });
    }

    /// <summary>
    /// Anmeldung über die Befehlszeile: <c>DB~Schulnummer~2024-2025~1 /user=Name /pw=Passwort</c>.
    /// Liefert true, wenn eine Anmeldung erfolgt ist.
    /// </summary>
    public static bool Befehlszeile(IWin32Window owner, string[] args)
    {
        var ziel = args.Select(a => Regex.Match(a, @"^DB~([^~]+)~([^~]+)~(\d+)$", RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success);
        if (ziel == null) return false;
        string Wert(string name) => args.Select(a => Regex.Match(a, $@"^[/-]{name}[=:](.*)$", RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "";
        var ok = false;
        Sicher(() =>
        {
            var einst = MultiUserEinstellungen.Laden();
            if (!einst.Eingerichtet) throw new MultiUserException("Es ist keine MultiUser-Datenbank eingerichtet.");
            var db = new MultiUserDb(einst.Verbindung);
            db.Initialisieren();
            var schule = db.Schulen().FirstOrDefault(s => string.Equals(s.Nr, ziel.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                         ?? throw new MultiUserException($"Die Schule {ziel.Groups[1].Value} existiert nicht.");
            var jahrNr = ziel.Groups[2].Value.Replace('-', '/');
            var jahr = db.Schuljahre(schule.Id).FirstOrDefault(j => j.Nr == jahrNr) ?? throw new MultiUserException($"Das Schuljahr {jahrNr} existiert nicht.");
            var version = db.Versionen(jahr.Id).FirstOrDefault(v => v.Nr == int.Parse(ziel.Groups[3].Value)) ?? throw new MultiUserException($"Die Version {ziel.Groups[3].Value} existiert nicht.");
            var benutzer = Wert("user");
            var klient = MultiUserKlient.Anmelden(db, benutzer.Length > 0 ? benutzer : einst.LetzterBenutzer, Wert("pw"), version.Id);
            Starten(owner, klient);
            ok = true;
        });
        return ok;
    }

    private static void Starten(IWin32Window owner, MultiUserKlient klient)
    {
        if (klient.PasswortFehlt)
        {
            var hinweis = klient.Benutzer.IstAdministrator
                ? "Der Administrator hat noch kein Passwort. Bitte vergeben Sie jetzt ein Passwort."
                : "Sie haben noch kein Passwort. Möchten Sie jetzt eines vergeben?";
            var pw = PasswortDialog.Zeigen(owner, klient.Benutzer.Name, false, hinweis);
            if (pw is { Neu.Length: > 0 }) klient.PasswortAendern("", pw.Value.Neu);
        }
        MultiUserSitzung.Starten(klient);
    }

    /// <summary>Beendet die Datenbanksitzung (die Daten bleiben als ungespeicherte Schuldatei geöffnet).</summary>
    public static void Abmelden()
    {
        if (!MultiUserSitzung.Aktiv) { Ui.Info("Sie sind nicht an einer MultiUser-Datenbank angemeldet."); return; }
        _verwaltung?.Close();
        MultiUserSitzung.Beenden();
    }

    /// <summary>Lädt die Daten sofort neu aus der Datenbank.</summary>
    public static void Neuanzeige()
    {
        if (!Angemeldet()) return;
        MultiUserSitzung.Neuladen();
    }

    /// <summary>Öffnet die Benutzerverwaltung (Recht "Benutzerverwaltung").</summary>
    public static void Benutzerverwaltung()
    {
        if (!Angemeldet() || !Darf(Recht.Benutzerverwaltung)) return;
        Sicher(() =>
        {
            if (_verwaltung is { IsDisposed: false })
            {
                _verwaltung.Activate();
                return;
            }
            _verwaltung = new BenutzerverwaltungFenster(MultiUserSitzung.Klient!.Db);
            _verwaltung.FormClosed += (_, _) => _verwaltung = null;
            _verwaltung.Show();
        });
    }

    /// <summary>Liste der angemeldeten Benutzer mit Nachrichten und Abmelden.</summary>
    public static void AngemeldeteBenutzer(IWin32Window owner)
    {
        if (!Angemeldet()) return;
        Sicher(() =>
        {
            using var d = new AngemeldeteBenutzerDialog(MultiUserSitzung.Klient!);
            d.ShowDialog(owner);
        });
    }

    /// <summary>"Datei in Datenbank speichern": geöffnete Schuldatei oder eine Datei als (neue oder überschriebene) Version speichern.</summary>
    public static void InDatenbankSpeichern(IWin32Window owner)
    {
        if (!Angemeldet() || !Darf(Recht.DatenImportExport)) return;
        Sicher(() =>
        {
            var k = MultiUserSitzung.Klient!;
            Schuldatei daten;
            string quelle;
            if (Ui.Frage("Eine Schuldatei (*.up) auswählen?\n(Nein = die aktuell geöffneten Daten speichern)"))
            {
                using var ofd = new OpenFileDialog { Filter = Dokument.DateiFilter };
                if (ofd.ShowDialog(owner) != DialogResult.OK) return;
                daten = Dokument.Laden(ofd.FileName).Daten;
                quelle = $"Datei {Path.GetFileName(ofd.FileName)} in Datenbank gespeichert";
            }
            else
            {
                daten = Dokument.Klonen(AppZustand.D);
                quelle = "Daten in Datenbank gespeichert";
            }
            var ziel = ZielVersionDialog.Zeigen(owner, k.Db, "Datei in Datenbank speichern", k.Schule.Nr, k.Schule.Bezeichnung, k.Schuljahr.Nr, k.Version.Nr + 1);
            if (ziel == null) return;
            var sj = ZielVersionDialog.SchuljahrSichern(k.Db, ziel.Schule, ziel.Schulname, ziel.Schuljahr);
            var vorhanden = k.Db.Versionen(sj.Id).FirstOrDefault(v => v.Nr == ziel.Version);
            if (vorhanden == null && !k.Rechte.Hat(Recht.VersionAnlegen)) throw new MultiUserException("Sie haben nicht das Recht \"Version anlegen\".");
            if (vorhanden != null)
            {
                if (!Ui.Frage($"Version {vorhanden} existiert bereits und wird überschrieben. Fortfahren?")) return;
                var andere = k.Db.Sitzungen(vorhanden.Id).Where(s => s.Id != k.SitzungId).ToList();
                if (andere.Count > 0)
                    throw new MultiUserException("Zum Überschreiben ist Exklusivbetrieb nötig. Angemeldet sind: " + string.Join(", ", andere.Select(s => s.Benutzer)));
            }
            var v = k.Db.DatenSpeichern(daten, sj.Id, ziel.Version, ziel.Bezeichnung, k.Benutzer.Name, quelle);
            if (v.Id == k.Version.Id) MultiUserSitzung.Neuladen();
            Ui.Info($"Die Daten wurden als Version {v.Nr} ({ziel.Schule} ~ {sj.Nr}) gespeichert.");
        });
    }

    /// <summary>Exportiert die angemeldete Version als Schuldatei (*.up).</summary>
    public static void AlsDateiExportieren(IWin32Window owner)
    {
        if (!Angemeldet() || !Darf(Recht.DatenImportExport)) return;
        Sicher(() =>
        {
            var k = MultiUserSitzung.Klient!;
            using var sfd = new SaveFileDialog { Filter = Dokument.SpeicherFilter, FileName = $"{k.Schule.Nr}_{k.Schuljahr.Nr.Replace('/', '-')}_V{k.Version.Nr}{Dokument.Dateiendung}" };
            if (sfd.ShowDialog(owner) != DialogResult.OK) return;
            k.Db.DateiExportieren(k.Version.Id, sfd.FileName);
            Ui.Info($"Die Version wurde nach {sfd.FileName} exportiert.");
        });
    }

    /// <summary>Exklusivbetrieb anfordern bzw. beenden (nötig für Import, Schuldaten/Zeitraster, Perioden, Ferien, neues Schuljahr).</summary>
    public static void Exklusiv(IWin32Window owner)
    {
        if (!Angemeldet()) return;
        Sicher(() =>
        {
            MultiUserSitzung.ExklusivUmschalten(out var meldung);
            Ui.Info(meldung, "Exklusivbetrieb");
        });
    }

    private static bool Angemeldet()
    {
        if (MultiUserSitzung.Aktiv) return true;
        Ui.Info("Sie sind nicht an einer MultiUser-Datenbank angemeldet.");
        return false;
    }

    private static bool Darf(Recht recht)
    {
        if (MultiUserSitzung.Rechte.Hat(recht)) return true;
        Ui.Info($"Sie haben nicht das Recht \"{Rechte.Text(recht)}\".");
        return false;
    }

    /// <summary>Vor dem Laden aus der Datenbank: ungespeicherte lokale Änderungen?</summary>
    private static bool VorWechselFragen() =>
        MultiUserSitzung.Aktiv || !AppZustand.Doc.IstGeaendert || AppZustand.Doc.Pfad == null && !AppZustand.Doc.KannRueckgaengig
        || Ui.Frage("Die geöffnete Schuldatei enthält ungespeicherte Änderungen, die beim Anmelden verworfen werden. Fortfahren?");

    private static void Sicher(Action a)
    {
        try
        {
            Cursor.Current = Cursors.WaitCursor;
            a();
        }
        catch (Exception ex)
        {
            if (ex is System.Data.Common.DbException) Ui.Info("Datenbankfehler:\n" + ex.Message, "MultiUser");
            else Ui.Fehler(ex);
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }
    }
}
