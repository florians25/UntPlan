using UntPlan.Web.Contracts;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Gekoppelter Unterricht im Klassenbuch: eine Stunde, an der mehrere Klassen gemeinsam teilnehmen – ob als ein Unterricht mit mehreren
/// Klassen bzw. Kopplungszeilen (gleiches Fach, gleicher Raum) oder als getrennte Unterrichte derselben Lehrkraft zur selben Zeit im selben
/// Fach und Raum (Mitklassen, z. B. INF12 + INF13). Anwesenheitskontrolle, Abwesenheiten und Klassenbuch gelten dann für alle Klassen –
/// gleich, ob die Stunde aus dem Lehrerplan, aus einem der Klassenpläne oder aus „Heute“ geöffnet wird.
/// </summary>
public sealed partial class Klassenbuchdienst
{
    private static readonly string[] Platzhalter = { "???", "---", "+", "+++" };

    private static bool Laeuft(Karte k) => k.Status is not ("entfall" or "veranstaltung" or "vorgemerkt" or "buchung");

    private static bool Deckt(Karte k, int stunde) => stunde >= k.Von && stunde <= k.Bis;

    /// <summary>
    /// Karte um die Klassen der Kopplung ergänzen: Mitklassen (getrennte Unterrichte, die laut Plan gemeinsam stattfinden) und die Klassen
    /// der gemeinsamen Karte im Plan der Lehrkräfte (gleiche Stunde, gleiches Fach, derselbe Unterricht bzw. eine gemeinsame Klasse).
    /// Ergänzte Klassen stehen danach in <see cref="Karte.Klassen"/> (nicht mehr in den Mitklassen).
    /// </summary>
    internal Karte MitKopplung(Stand s, DateOnly d, Karte k)
    {
        if (k.Unterricht is null || k.Fach.Length == 0 || !Laeuft(k)) return k;
        var klassen = k.Klassen.Select(w => w.K).Where(x => x.Length > 0).ToList();
        var neu = new List<string>();
        void Hinzu(string c) { if (c.Length > 0 && !klassen.Contains(c, Ci) && !neu.Contains(c, Ci)) neu.Add(c); }
        foreach (var w in k.Mitklassen) Hinzu(w.K);
        foreach (var l in k.Lehrer.Select(w => w.K).Where(x => x.Length > 0 && !Platzhalter.Contains(x)).Distinct(Ci))
            foreach (var lk in _plan.Karten(s, d, Elementtyp.Lehrer, l).Where(x => x.Unterricht.HasValue && Deckt(x, k.Von) && Laeuft(x) && Ci.Equals(x.Fach, k.Fach)
                         && (x.Unterricht == k.Unterricht || x.Klassen.Any(w => klassen.Contains(w.K, Ci)))))
                foreach (var w in lk.Klassen) Hinzu(w.K);
        if (neu.Count == 0) return k;
        return k with
        {
            Klassen = k.Klassen.Concat(neu.Select(c => new Wert(c))).ToList(),
            Mitklassen = k.Mitklassen.Where(w => !neu.Contains(w.K, Ci)).ToList(),
        };
    }

    /// <summary>
    /// Unterricht je Klasse der (gekoppelten) Karte: Klassen eines anderen Unterrichts (getrennt gekoppelt) gehören zu dessen Unterricht –
    /// für dessen Schülergruppen und für die Zuordnung der Fehlzeiten. Ohne eigenen Unterricht in dieser Stunde: der Unterricht der Karte.
    /// </summary>
    internal Dictionary<string, int?> UnterrichtJeKlasse(Stand s, DateOnly d, Karte k)
    {
        var res = new Dictionary<string, int?>(Ci);
        var klassen = k.Klassen.Select(w => w.K).Where(x => x.Length > 0).Distinct(Ci).ToList();
        foreach (var c in klassen) res[c] = k.Unterricht;
        if (k.Unterricht is not int u || klassen.Count <= 1) return res;
        var eigene = _plan.Rohstunden(s, d).Where(r => r.Unterricht == u && r.Stunde == k.Von).SelectMany(r => r.Klassen).ToHashSet(Ci);
        foreach (var c in klassen.Where(c => !eigene.Contains(c)))
        {
            var kk = _plan.Karten(s, d, Elementtyp.Klasse, c)
                .Where(x => x.Unterricht.HasValue && Deckt(x, k.Von) && Laeuft(x) && Ci.Equals(x.Fach, k.Fach))
                .OrderByDescending(x => x.Unterricht == u).FirstOrDefault();
            if (kk?.Unterricht is int andere) res[c] = andere;
        }
        return res;
    }

    /// <summary>
    /// Unterrichte, die in dieser Stunde gekoppelt mit <paramref name="unterricht"/> stattfinden (getrennte Unterrichte mit gemeinsamer Lehrkraft,
    /// gleichem Fach und gleichem Raum, z. B. INF 5a + INF 5b) – der Unterricht selbst zuerst. Klassenbucheinträge (Lehrstoff, Notizen,
    /// Hausaufgaben) einer gekoppelten Stunde gelten für alle diese Unterrichte, gleich unter welchem sie gespeichert wurden.
    /// </summary>
    public List<int> KoppelUnterrichte(Stand s, DateOnly d, int stunde, int unterricht)
    {
        var res = new List<int> { unterricht };
        var roh = _plan.Rohstunden(s, d).Where(r => r.Stunde == stunde && r.Unterricht.HasValue && r.Fach.Length > 0 && r.Status is not ("entfall" or "vorgemerkt")).ToList();
        var eigene = roh.Where(r => r.Unterricht == unterricht).ToList();
        if (eigene.Count == 0) return res;
        static HashSet<string> Personen(Rohstunde x) => new(x.Lehrer.Concat(x.LehrerAlt ?? Array.Empty<string>()).Where(l => l.Length > 0 && !Platzhalter.Contains(l)), Ci);
        static HashSet<string> Raeume(Rohstunde x) => x.Raeume.SelectMany(Plandienst.RaumListe).ToHashSet(Ci);
        foreach (var x in roh)
            if (x.Unterricht is int o && !res.Contains(o)
                && eigene.Any(e => Ci.Equals(e.Fach, x.Fach) && Personen(e).Overlaps(Personen(x)) && Raeume(e).Count > 0 && Raeume(e).SetEquals(Raeume(x))))
                res.Add(o);
        return res;
    }

    /// <summary>
    /// Unterricht, unter dem Lehrstoff, Notizen und Hausaufgaben einer gekoppelten Stunde gespeichert werden – damit es je Stunde genau einen
    /// Eintrag gibt, gleich aus welchem Plan sie bearbeitet wird: der Unterricht, unter dem schon Inhalt steht, sonst der kleinste der
    /// Kopplung (sofern der Benutzer dort schreiben darf; sonst der eigene).
    /// </summary>
    public int KoppelZiel(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht)
    {
        var us = KoppelUnterrichte(s, d, stunde, unterricht);
        if (us.Count == 1) return unterricht;
        int? vorhanden = Zeitraum(d, d, true).Where(e => us.Contains(e.Unterricht) && e.Stunde == stunde && MitInhalt(e)).Select(e => (int?)e.Unterricht).FirstOrDefault();
        vorhanden ??= us.Where(u => HausaufgabenMitEinzelstunden(d, stunde, stunde, u).Count > 0).Select(u => (int?)u).FirstOrDefault();
        var ziel = vorhanden ?? us.Min();
        return ziel == unterricht || DarfSchreiben(b, s, d, stunde, ziel) ? ziel : unterricht;
    }

    /// <summary>Eintrag mit Inhalt (Lehrstoff, Hausaufgabe, Notizen, online) – nicht nur die Abwesenheitskontrolle.</summary>
    public static bool MitInhalt(Klassenbucheintrag e) =>
        e.Thema.Length > 0 || e.Hausaufgabe.Length > 0 || e.Notiz.Length > 0 || e.NotizSchueler.Length > 0 || e.Online || e.Lehrform.Length > 0 || e.Anmerkung.Length > 0;

    /// <summary>
    /// Eintrag einer (gekoppelten) Stunde aus mehreren Kandidaten: der mit Inhalt, dann der des eigenen Unterrichts; die Abwesenheitskontrolle
    /// gilt, wenn sie unter einem der gekoppelten Unterrichte erfasst wurde.
    /// </summary>
    public static Klassenbucheintrag? Waehlen(IEnumerable<Klassenbucheintrag> kandidaten, int unterricht)
    {
        var liste = kandidaten.ToList();
        var e = liste.OrderByDescending(MitInhalt).ThenByDescending(x => x.Unterricht == unterricht).FirstOrDefault();
        if (e is null || e.Anwesenheit != null) return e;
        var kontrolle = liste.Select(x => x.Anwesenheit).Where(x => x != null).Max(StringComparer.Ordinal);
        return kontrolle is null ? e : e with { Anwesenheit = kontrolle };
    }

    /// <summary>Alle Unterrichte der (gekoppelten) Karte, der Unterricht der Karte zuerst.</summary>
    internal List<int> UnterrichteDerKarte(Stand s, DateOnly d, Karte k, int unterricht) =>
        new[] { unterricht }.Concat(UnterrichtJeKlasse(s, d, k).Values.OfType<int>()).Distinct().ToList();

    /// <summary>
    /// Teilnehmer*innen einer (gekoppelten) Stunde am Tag: je beteiligtem Unterricht die Schüler*innen seiner Klassen samt Schülergruppen
    /// (Abmeldungen, Gäste) – dazu der Unterricht, unter dem ihre Fehlzeiten stehen.
    /// </summary>
    internal (List<WebSchueler> Schueler, Dictionary<int, int?> Unterricht, HashSet<int> Gaeste) TeilnehmerDerStunde(Stand s, Karte karte, DateOnly d)
    {
        var jeKlasse = UnterrichtJeKlasse(s, d, karte);
        var schueler = new Dictionary<int, WebSchueler>();
        var unterricht = new Dictionary<int, int?>();
        var gaeste = new HashSet<int>();
        // der Unterricht der Karte zuerst (Gäste dieses Unterrichts gehören zu ihm)
        foreach (var g in jeKlasse.GroupBy(x => x.Value).OrderByDescending(g => g.Key == karte.Unterricht))
        {
            foreach (var x in Klassenzugehoerigkeit.DerStunde(s, g.Key, g.Select(x => x.Key), d))
                if (schueler.TryAdd(x.Id, x)) unterricht[x.Id] = g.Key;
            gaeste.UnionWith(Klassenzugehoerigkeit.Gaeste(s, g.Key, d));
        }
        var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
        var liste = schueler.Values.OrderBy(x => x.Klasse, de).ThenBy(x => x.Name, de).ThenBy(x => x.Vorname, de).ToList();
        return (liste, unterricht, gaeste);
    }

    /// <summary>Teilnehmer*innen einer (gekoppelten) Stunde am Tag (alle beteiligten Klassen, Schülergruppen der Unterrichte).</summary>
    public List<WebSchueler> SchuelerDerStunde(Stand s, Karte karte, DateOnly d) => TeilnehmerDerStunde(s, karte, d).Schueler;

    /// <summary>
    /// Darf die Lehrkraft in einem gekoppelten Unterricht schreiben, der in ihrem Plan unter einem anderen Unterricht zusammengefasst ist
    /// (getrennte Unterrichte zur selben Zeit, gleiches Fach und gleicher Raum – Stunde aus dem Plan der Mitklasse geöffnet)?
    /// </summary>
    private bool GekoppeltBeiLehrkraft(Stand s, DateOnly d, int stunde, int unterricht, string lehrer)
    {
        // nur Zeilen, die die Lehrkraft selbst hält (nie die Stunde einer Kollegin mit gleichem Fach und gleicher Klasse)
        var zeilen = _plan.Rohstunden(s, d).Where(r => r.Unterricht == unterricht && r.Stunde == stunde && r.Status is not ("entfall" or "vorgemerkt") && r.Klassen.Count > 0
                                                        && r.Lehrer.Contains(lehrer, Ci)).ToList();
        if (zeilen.Count == 0) return false;
        var klassen = zeilen.SelectMany(r => r.Klassen).ToHashSet(Ci);
        var faecher = zeilen.Select(r => r.Fach).ToHashSet(Ci);
        return _plan.Karten(s, d, Elementtyp.Lehrer, lehrer).Any(k => k.Unterricht.HasValue && k.Unterricht != unterricht && Deckt(k, stunde)
            && k.Status is not ("entfall" or "abwesend" or "veranstaltung" or "vorgemerkt") && faecher.Contains(k.Fach) && k.Klassen.Any(w => klassen.Contains(w.K)));
    }
}
