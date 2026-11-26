using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Zugriff auf klassenbezogene Verwaltungsseiten: Klassenlehrkräfte sehen und bearbeiten dort nur ihre eigenen Klassen.
/// Schulleitung, Administration und Konten mit „Klassenbuch aller Klassen“ bzw. „Abwesenheiten aller Klassen“ sehen alle Klassen.
/// Der eigene Unterricht (Stunden laut Plan) bleibt davon unberührt.
/// </summary>
public sealed partial class Klassenbuchdienst
{
    /// <summary>Darf das Konto die Verwaltungsseiten aller Klassen nutzen?</summary>
    public static bool AlleKlassen(Benutzer b) =>
        Rechtedienst.DarfLesen(b, Recht.KlassenbuchAlle) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle);

    /// <summary>Klassen, deren Klassenleitung das Konto ist.</summary>
    public static HashSet<string> GeleiteteKlassen(Benutzer b, Stand s) =>
        b.LehrerKuerzel is string l && Rechtedienst.Darf(b, Recht.KlassenbuchKlasse)
            ? s.Paket.Elemente.Klassen.Where(k => k.Klassenleitung.Contains(l, Ci)).Select(k => k.Kurz).ToHashSet(Ci)
            : new HashSet<string>(Ci);

    /// <summary>Unterrichtet die Lehrkraft laut Plan (irgendeine Periode) in der Klasse?</summary>
    public static bool UnterrichtetKlasse(Stand s, string lehrer, string klasse) =>
        s.Paket.Perioden.Any(p => p.Unterricht.Any(u => u.Zeilen.Any(z => Ci.Equals(z.Lehrer, lehrer) && z.Klassen.Contains(klasse, Ci))));

    /// <summary>Klassenleitungsseiten (Abwesenheiten, Fehlzeiten, Entschuldigungen, Schülerdaten, Dienste, Befreiungen, Gruppen, Sitzplan der Klasse).</summary>
    public static bool DarfKlasseVerwalten(Benutzer b, Stand s, string klasse) =>
        AlleKlassen(b) || IstKlassenleitung(b, s, klasse);

    /// <summary>Klassenbuch einer Klasse einsehen: Klassenleitung oder Lehrkraft, die in der Klasse unterrichtet.</summary>
    public static bool DarfKlasseEinsehen(Benutzer b, Stand s, string klasse) =>
        DarfKlasseVerwalten(b, s, klasse) || b.LehrerKuerzel is string l && UnterrichtetKlasse(s, l, klasse);

    /// <summary>Wirft 403, wenn eine angegebene Klasse nicht zu den eigenen Klassen gehört (leere Angabe: keine Prüfung, die Liste wird gefiltert).</summary>
    public static void KlasseVerwaltenVerlangen(Benutzer b, Stand s, string? klasse)
    {
        if (string.IsNullOrWhiteSpace(klasse)) return;
        foreach (var k in klasse.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!DarfKlasseVerwalten(b, s, k)) throw new UnauthorizedAccessException($"Die Klasse {k} gehört nicht zu Ihren Klassen.");
    }

    /// <summary>Wirft 403, wenn das Konto das Klassenbuch der Klasse nicht einsehen darf (Kollegium; Klassenkonten prüft die Planfreigabe).</summary>
    public static void KlasseEinsehenVerlangen(Benutzer b, Stand s, string? klasse)
    {
        if (!b.Kollegium || string.IsNullOrWhiteSpace(klasse)) return;
        foreach (var k in klasse.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!DarfKlasseEinsehen(b, s, k)) throw new UnauthorizedAccessException($"Das Klassenbuch der Klasse {k} steht nur der Klassenleitung und den Lehrkräften der Klasse zur Verfügung.");
    }

    /// <summary>
    /// Einsicht im Unterricht: wie <see cref="DarfKlasseEinsehen"/>, zusätzlich Lehrkräfte mit einer Stunde der Klasse an diesem Tag (Vertretung),
    /// damit die Stundendetails Klassenbucheinträge, Dienste und Schülerliste der vertretenen Klasse zeigen.
    /// </summary>
    public bool DarfKlasseImUnterricht(Benutzer b, Stand s, string klasse, DateOnly tag) =>
        DarfKlasseEinsehen(b, s, klasse)
        || b.LehrerKuerzel is string l && _plan.Karten(s, tag, Elementtyp.Lehrer, l).Any(k => k.Klassen.Any(w => Ci.Equals(w.K, klasse) || Ci.Equals(w.Alt, klasse)));
}
