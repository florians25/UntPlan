using System.Globalization;
using System.Text;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Ergebnis eines Erinnerungslaufs: benachrichtigte Lehrkräfte, offene Stunden insgesamt, Lehrkräfte ohne Konto.</summary>
public sealed record Erinnerungslauf(int Lehrkraefte, int Stunden, List<string> OhneKonto);

/// <summary>
/// Hintergrunddienst des Klassenbuchs (Klassenbuch-Einstellungen):
/// <list type="bullet">
/// <item>Automatische Erinnerung an offene Stunden: am eingestellten Wochentag (ab 7 Uhr, einmal je Tag) erhält jede verantwortliche
///   Lehrkraft eine Mitteilung mit ihren offenen Stunden des aktuellen Schuljahres – offen nach dem Kriterium „Offene Stunden anzeigen für“
///   (fehlender Lehrstoff, fehlende Abwesenheitskontrolle oder beides), je Stunde mit Link direkt zur Stunde.</item>
/// <item>Automatische Entschuldigung: offene Abwesenheiten, die länger als die eingestellte Zahl an Tagen zurückliegen, erhalten den
///   eingestellten Status (einmal je Tag).</item>
/// </list>
/// </summary>
public sealed class Klassenbucherinnerung : BackgroundService
{
    private const string ErinnertAm = "klassenbuch.erinnerung.zuletzt", EntschuldigtAm = "klassenbuch.autoentschuldigung.zuletzt";
    /// <summary>Höchstens so viele Stunden je Mitteilung (die übrigen werden gezählt).</summary>
    public const int StundenJeMitteilung = 40;

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Einstellungen _einstellungen;
    private readonly Paketdienst _paket;
    private readonly Klassenbuchverwaltung _kv;
    private readonly Mitteilungsdienst _mitteilungen;
    private readonly Benutzerdienst _benutzer;
    private readonly ILogger<Klassenbucherinnerung> _log;

    public Klassenbucherinnerung(Db db, Uhr uhr, Einstellungen einstellungen, Paketdienst paket, Klassenbuchverwaltung kv, Mitteilungsdienst mitteilungen,
        Benutzerdienst benutzer, ILogger<Klassenbucherinnerung> log)
    {
        _db = db; _uhr = uhr; _einstellungen = einstellungen; _paket = paket; _kv = kv; _mitteilungen = mitteilungen; _benutzer = benutzer; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { Faellige(); }
            catch (Exception ex) { _log.LogWarning(ex, "Klassenbuch: Erinnerung bzw. automatische Entschuldigung fehlgeschlagen"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Wochentag nach ISO (1 = Montag … 7 = Sonntag).</summary>
    public static int Wochentag(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    /// <summary>Fällige Aufgaben ausführen (vom Hintergrunddienst halbstündlich aufgerufen; je Aufgabe höchstens einmal am Tag).</summary>
    public void Faellige()
    {
        var einst = Klassenbucheinstellungen.Laden(_db);
        var heute = Plandienst.Iso(_uhr.Heute);
        if (einst.Erinnerung && Wochentag(_uhr.Heute) == einst.ErinnerungTag && _uhr.Jetzt.Hour >= 7 && _einstellungen.Lesen(ErinnertAm) != heute)
        {
            _einstellungen.Schreiben(ErinnertAm, heute);
            var r = Senden();
            _log.LogInformation("Klassenbuch: {Lehrkraefte} Lehrkräfte an {Stunden} offene Stunden erinnert", r.Lehrkraefte, r.Stunden);
        }
        if (einst.AutoTage is int && _einstellungen.Lesen(EntschuldigtAm) != heute)
        {
            _einstellungen.Schreiben(EntschuldigtAm, heute);
            AutomatischEntschuldigen();
        }
    }

    /// <summary>Absender der Systemmitteilungen: das erste aktive Administrationskonto.</summary>
    private Benutzer? Absender() => _benutzer.Alle().Where(b => b.IstAdmin && !b.Gesperrt).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>
    /// Erinnerung jetzt senden: offene Stunden des aktuellen Schuljahres bis heute (nach den Klassenbuch-Einstellungen) je Lehrkraft;
    /// jede Lehrkraft mit Konto erhält eine Mitteilung mit Links zu ihren Stunden.
    /// </summary>
    public Erinnerungslauf Senden()
    {
        var s = _paket.Aktuell;
        var absender = Absender();
        if (s is null || absender is null) return new Erinnerungslauf(0, 0, new());
        var einst = Klassenbucheinstellungen.Laden(_db);
        var heute = _uhr.Heute;
        var von = s.SchuljahrVon != DateOnly.MinValue && s.SchuljahrVon <= heute ? s.SchuljahrVon : heute.AddDays(-180);
        var offen = _kv.Stunden(s, absender, von, heute, Array.Empty<string>(), null)
            .Where(z => einst.Offen(z.Geprueft || !einst.Abwesenheitskontrolle, z.Lehrstoff)).ToList();
        var konten = _benutzer.Alle().Where(b => !b.Gesperrt && b.LehrerKuerzel != null).ToList();
        int lehrkraefte = 0, stunden = 0;
        var ohneKonto = new List<string>();
        foreach (var g in offen.GroupBy(z => z.Lehrer, StringComparer.OrdinalIgnoreCase))
        {
            var ids = konten.Where(b => string.Equals(b.LehrerKuerzel, g.Key, StringComparison.OrdinalIgnoreCase)).Select(b => b.Id).Where(id => id != absender.Id).ToList();
            if (ids.Count == 0) { ohneKonto.Add(g.Key); continue; }
            var liste = g.ToList();
            try
            {
                _mitteilungen.Benachrichtigen(absender, ids, liste.Count == 1 ? "Erinnerung: eine offene Stunde im Klassenbuch" : $"Erinnerung: {liste.Count} offene Stunden im Klassenbuch",
                    Text(g.Key, liste, einst));
                lehrkraefte++;
                stunden += liste.Count;
            }
            catch (ArgumentException ex) { _log.LogWarning("Klassenbuch: Erinnerung an {Lehrer} nicht gesendet: {Grund}", g.Key, ex.Message); }
        }
        return new Erinnerungslauf(lehrkraefte, stunden, ohneKonto);
    }

    /// <summary>Text der Erinnerung (Markdown wie in Mitteilungen): je Stunde ein Link direkt zur Stunde.</summary>
    public static string Text(string lehrer, List<Stundenzeile> liste, KlassenbuchEinstellungen einst)
    {
        var fuer = einst.Abwesenheitskontrolle ? einst.OffenFuer : "lehrstoff";
        var kriterium = fuer switch
        {
            "kontrolle" => "die Abwesenheitskontrolle",
            "lehrstoff" => "der Lehrstoff",
            _ => "der Lehrstoff bzw. die Abwesenheitskontrolle",
        };
        var t = new StringBuilder();
        t.Append($"In folgenden Stunden des aktuellen Schuljahres fehlt noch {kriterium}. Bitte ergänzen Sie die Einträge im Klassenbuch.\n\n");
        foreach (var z in liste.Take(StundenJeMitteilung))
        {
            var datum = DateOnly.ParseExact(z.Datum, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var fehlt = new List<string>();
            if (!z.Lehrstoff && fuer != "kontrolle") fehlt.Add("Lehrstoff");
            if (!z.Geprueft && fuer != "lehrstoff") fehlt.Add("Abwesenheitskontrolle");
            var stunde = z.Von == z.Bis ? $"{z.Von}. Std." : $"{z.Von}.–{z.Bis}. Std.";
            t.Append($"- [{datum:dd.MM.yyyy} {stunde} {z.Fach} {string.Join(", ", z.Klassen)}](#/plan/lehrer/{Uri.EscapeDataString(lehrer)}/{z.Datum}/{z.Von}) – fehlt: {string.Join(", ", fehlt)}\n");
        }
        if (liste.Count > StundenJeMitteilung) t.Append($"- … und {liste.Count - StundenJeMitteilung} weitere (Klassenbuch → Offene Stunden)\n");
        t.Append("\nDer Link öffnet die Stunde direkt im Browser; in der mobilen App lässt er sich nicht direkt öffnen.");
        return t.ToString();
    }

    /// <summary>Offene Abwesenheiten, die länger als die eingestellte Zahl an Tagen zurückliegen, automatisch entschuldigen.</summary>
    public int AutomatischEntschuldigen()
    {
        var einst = Klassenbucheinstellungen.Laden(_db);
        if (einst.AutoTage is not int tage) return 0;
        var grenze = _uhr.Heute.AddDays(-tage);
        return _db.Ausfuehren("UPDATE fehlzeiten SET status = @s WHERE status = 'offen' AND datum < @g", Db.P("@s", einst.AutoStatus), Db.P("@g", grenze));
    }
}
