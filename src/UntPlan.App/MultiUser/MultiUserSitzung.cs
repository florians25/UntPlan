using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.MultiUser;

namespace UntPlan.App.MultiUser;

/// <summary>
/// Laufende MultiUser-Sitzung des Arbeitsplatzes: schreibt jede Änderung sofort in die Datenbank,
/// prüft alle 10 Sekunden auf fremde Änderungen und Nachrichten, sendet Lebenszeichen und meldet bei Inaktivität ab.
/// </summary>
public static class MultiUserSitzung
{
    private const int IntervallMs = 10_000;
    private static System.Windows.Forms.Timer? _timer;
    private static Dokument? _doc;
    private static bool _laedt;
    private static bool _imTakt;
    private static int _autoLogoutMinuten;
    private static DateTime _letzteAktivitaet = DateTime.Now;
    private static AktivitaetsFilter? _filter;

    public static MultiUserKlient? Klient { get; private set; }
    public static bool Aktiv => Klient != null;
    /// <summary>Angemeldeter Benutzer (ohne MultiUser: Windows-Benutzer).</summary>
    public static string Benutzer => Klient?.Benutzer.Name ?? AppZustand.Benutzer;
    /// <summary>Rechte des angemeldeten Benutzers (ohne MultiUser: alle Rechte).</summary>
    public static Rechte Rechte => Klient?.Rechte ?? Rechte.Alle();
    /// <summary>Kurzbeschreibung "Schule ~ Schuljahr ~ Version (Benutzer)" oder leer.</summary>
    public static string Titel => Klient?.Titel ?? "";

    /// <summary>Anmeldung, Abmeldung, Exklusivbetrieb oder Neuladen (Titel, Menüs aktualisieren).</summary>
    public static event Action? ZustandGeaendert;

    public static void Starten(MultiUserKlient klient)
    {
        Beenden();
        Klient = klient;
        Uebernehmen(klient.Laden());
        AppZustand.Benutzer = klient.Benutzer.Name;
        try { _autoLogoutMinuten = klient.Db.AutoLogoutMinuten; } catch { _autoLogoutMinuten = 0; }
        _letzteAktivitaet = DateTime.Now;
        _filter = new AktivitaetsFilter();
        Application.AddMessageFilter(_filter);
        AppZustand.DokumentGewechselt += DokumentGewechselt;
        _timer = new System.Windows.Forms.Timer { Interval = IntervallMs };
        _timer.Tick += (_, _) => Takt();
        _timer.Start();
        ZustandGeaendert?.Invoke();
    }

    /// <summary>Beendet die Sitzung. Die geladenen Daten bleiben als ungespeicherte Schuldatei geöffnet.</summary>
    public static void Beenden()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        if (_filter != null) Application.RemoveMessageFilter(_filter);
        _filter = null;
        AppZustand.DokumentGewechselt -= DokumentGewechselt;
        Abhaengen();
        if (Klient == null) return;
        if (Klient.Exklusiv) try { Klient.ExklusivFreigeben(); } catch { /* egal */ }
        Klient.Abmelden();
        Klient = null;
        AppZustand.Benutzer = Environment.UserName;
        ZustandGeaendert?.Invoke();
    }

    /// <summary>Lädt den aktuellen Stand der Datenbank (ohne Zusammenführen).</summary>
    public static void Neuladen()
    {
        if (Klient == null) return;
        try
        {
            _laedt = true;
            Uebernehmen(Klient.Neuladen());
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
        }
        finally
        {
            _laedt = false;
        }
        ZustandGeaendert?.Invoke();
    }

    /// <summary>Exklusivbetrieb anfordern bzw. beenden.</summary>
    public static bool ExklusivUmschalten(out string meldung)
    {
        meldung = "";
        if (Klient == null) { meldung = "Sie sind nicht an der Datenbank angemeldet."; return false; }
        if (Klient.Exklusiv)
        {
            Klient.ExklusivFreigeben();
            meldung = "Der Exklusivbetrieb wurde beendet.";
        }
        else if (!Klient.ExklusivAnfordern(out meldung)) return false;
        else meldung = "Sie arbeiten jetzt im Exklusivbetrieb. Andere Benutzer können sich nicht anmelden und keine Änderungen schreiben.";
        ZustandGeaendert?.Invoke();
        return true;
    }

    private static void Uebernehmen(Schuldatei daten)
    {
        Abhaengen();
        var doc = new Dokument(daten);
        _doc = doc;
        doc.Geaendert += DocGeaendert;
        // im MultiUser-Betrieb gilt die Datenbank: Ansichtseinstellungen bleiben lokal (kein Zurücksetzen bei jedem Neuladen)
        AppZustand.Setzen(doc, ansichtUebernehmen: false);
    }

    private static void Abhaengen()
    {
        if (_doc != null) _doc.Geaendert -= DocGeaendert;
        _doc = null;
    }

    private static void DokumentGewechselt()
    {
        // Eine andere Datei wurde geöffnet: Datenbanksitzung beenden, damit nichts Fremdes geschrieben wird.
        if (Klient != null && _doc != null && !ReferenceEquals(AppZustand.Doc, _doc)) Beenden();
    }

    private static void DocGeaendert(AenderungsArt art)
    {
        if (Klient == null || _laedt || _doc == null) return;
        if (!_doc.IstGeaendert) return; // nur "Speichern unter" in eine Datei
        try
        {
            Klient.Speichern(_doc.Daten, _doc.RueckgaengigText ?? art.ToString(), art);
        }
        catch (MultiUserException ex)
        {
            Ui.Info(ex.Message, "MultiUser");
            NeuladenSpaeter();
        }
        catch (Exception ex)
        {
            Ui.Info("Die Änderung konnte nicht in die Datenbank geschrieben werden:\n" + ex.Message, "MultiUser");
            NeuladenSpaeter();
        }
    }

    private static void NeuladenSpaeter()
    {
        var h = AppZustand.Haupt;
        if (h != null && h.IsHandleCreated && !h.IsDisposed) h.BeginInvoke(Neuladen);
        else Neuladen();
    }

    private static void Takt()
    {
        if (Klient == null || _imTakt) return;
        _imTakt = true;
        try
        {
            var taetigkeit = AppZustand.Haupt?.ActiveMdiChild?.Text ?? "";
            if (!Klient.Lebenszeichen(taetigkeit))
            {
                Beenden();
                Ui.Info("Ihre Sitzung wurde beendet (Abmeldung durch den Administrator oder Zeitüberschreitung).", "MultiUser");
                return;
            }
            foreach (var n in Klient.NachrichtenAbholen())
                Ui.Info($"Nachricht von {n.Absender} ({n.Zeitpunkt:g}):\n\n{n.Text}", "Benutzernachricht");
            if (Klient?.PruefeAenderungen() == true) Neuladen();
            if (Klient != null && _autoLogoutMinuten > 0 && DateTime.Now - _letzteAktivitaet > TimeSpan.FromMinutes(_autoLogoutMinuten))
            {
                if (AbmeldeDialog.Zeigen()) Beenden();
                else _letzteAktivitaet = DateTime.Now;
            }
        }
        catch
        {
            // Datenbank vorübergehend nicht erreichbar: beim nächsten Takt erneut versuchen
        }
        finally
        {
            _imTakt = false;
        }
    }

    /// <summary>Merkt sich Tastatur- und Mausaktivität für die automatische Abmeldung.</summary>
    private sealed class AktivitaetsFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is 0x0100 or 0x0201 or 0x0204 or 0x020A) _letzteAktivitaet = DateTime.Now;
            return false;
        }
    }

    /// <summary>Freundliches Abmelden: "Sitzung beenden?" mit 10 Sekunden Countdown.</summary>
    private static class AbmeldeDialog
    {
        public static bool Zeigen()
        {
            using var f = new Form
            {
                Text = "MultiUser", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(360, 120), Font = Ui.Schrift, TopMost = true,
            };
            var rest = 10;
            var l = new Label { Left = 12, Top = 14, Width = 336, Height = 50 };
            void Text() => l.Text = $"Sie waren längere Zeit nicht aktiv. Sitzung beenden?\nAutomatische Abmeldung in {rest} Sekunden.";
            Text();
            var ja = new Button { Text = "Abmelden", DialogResult = DialogResult.Yes, Left = 150, Top = 80, Width = 90 };
            var nein = new Button { Text = "Angemeldet bleiben", DialogResult = DialogResult.No, Left = 246, Top = 80, Width = 104 };
            f.Controls.AddRange(new Control[] { l, ja, nein });
            f.AcceptButton = nein;
            f.CancelButton = nein;
            using var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (_, _) =>
            {
                rest--;
                Text();
                if (rest <= 0) f.DialogResult = DialogResult.Yes;
            };
            t.Start();
            return f.ShowDialog() != DialogResult.No;
        }
    }
}
