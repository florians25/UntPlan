using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Prüfungen in gekoppelten Stunden gelten immer für die ganze Kopplung. Wird eine Prüfung nur für einen Teil der Klassen einer
/// Kopplung eingetragen (z. B. aus UntPlanWeb für INF13, obwohl der Unterricht WW für ET13, INF13 und ING13 gemeinsam stattfindet),
/// entfiele sonst nur die Zeile dieser Klasse, die Lehrkraft ginge in die Aufsicht und die übrigen Zeilen stünden ohne Lehrkraft da.
/// <see cref="Erweitern"/> ergänzt die fehlenden Klassen der Kopplung, so wie UntPlanWeb die Prüfung anzeigt.
/// </summary>
public static class Klausurkopplung
{
    /// <summary>
    /// Klassen der Kopplungen, die zur Prüfung gehören, aber in ihren Klassen fehlen. Eine Kopplung gehört zur Prüfung, wenn sie
    /// mindestens eine Klasse der Prüfung umfasst und entweder der Prüfung zugeordnet ist, ihre Aufsicht dort unterrichtet oder (ohne
    /// Aufsicht) für die betroffenen Klassen der einzige Unterricht zur Prüfungszeit ist.
    /// </summary>
    public static List<int> FehlendeKlassen(Schuldatei d, DateOnly datum, int stundeVon, int stundeBis, IReadOnlyCollection<int> klassen,
        IReadOnlyCollection<int> lehrer, int? unterrichtId = null, IReadOnlyList<RegelStunde>? regel = null)
    {
        if (klassen.Count == 0) return new List<int>();
        var bis = Math.Max(stundeVon, stundeBis);
        regel ??= Tagesplan.Regulaer(d, datum);
        var inZeit = regel.Where(r => r.Stunde >= stundeVon && r.Stunde <= bis).ToList();
        var fehlend = new List<int>();
        foreach (var g in inZeit.GroupBy(r => (r.Unterricht.Id, r.Stunde)))
        {
            var zeilen = g.ToList();
            var alle = zeilen.SelectMany(Vertretungen.KlassenDerZeile).Distinct().ToList();
            if (alle.Count < 2) continue;
            var betroffen = alle.Where(klassen.Contains).ToList();
            if (betroffen.Count == 0 || betroffen.Count == alle.Count) continue;
            var gehoert = unterrichtId == g.Key.Id
                || zeilen.Any(z => z.LehrerId is int l && lehrer.Contains(l))
                || (lehrer.Count == 0 && betroffen.All(kl => inZeit.Where(r => r.Stunde == g.Key.Stunde && Vertretungen.KlassenDerZeile(r).Contains(kl))
                    .Select(r => r.Unterricht.Id).Distinct().Count() == 1));
            if (!gehoert) continue;
            foreach (var kl in alle)
                if (!klassen.Contains(kl) && !fehlend.Contains(kl)) fehlend.Add(kl);
        }
        return fehlend;
    }

    public static List<int> FehlendeKlassen(Schuldatei d, Klausur k, IReadOnlyList<RegelStunde>? regel = null) =>
        FehlendeKlassen(d, k.Datum, k.StundeVon, k.StundeBis, k.KlassenIds, k.LehrerIds, k.UnterrichtId, regel);

    /// <summary>Klassen der Prüfung samt den fehlenden Klassen ihrer Kopplungen (Reihenfolge: bisherige Klassen zuerst).</summary>
    public static List<int> Klassen(Schuldatei d, DateOnly datum, int stundeVon, int stundeBis, IReadOnlyCollection<int> klassen,
        IReadOnlyCollection<int> lehrer, int? unterrichtId = null) =>
        klassen.Concat(FehlendeKlassen(d, datum, stundeVon, stundeBis, klassen, lehrer, unterrichtId)).Distinct().ToList();

    /// <summary>Ergänzt die fehlenden Klassen der Kopplung in der Prüfung. Ergebnis: geändert.</summary>
    public static bool Erweitern(Schuldatei d, Klausur k, IReadOnlyList<RegelStunde>? regel = null)
    {
        if (!k.Aktiv) return false;
        var fehlend = FehlendeKlassen(d, k, regel);
        if (fehlend.Count == 0) return false;
        k.KlassenIds = k.KlassenIds.Concat(fehlend).Distinct().ToList();
        return true;
    }

    /// <summary>
    /// Alle aktiven Prüfungen (ab <paramref name="ab"/>) auf ihre ganze Kopplung erweitern, z. B. ältere Teilprüfungen beim Laden, Import
    /// und Hochladen; die Vertretungen der betroffenen Tage werden neu abgeglichen. Ergebnis: Anzahl erweiterter Prüfungen.
    /// </summary>
    public static int AlleErweitern(Schuldatei d, DateOnly? ab = null)
    {
        var tage = new HashSet<DateOnly>();
        var anzahl = 0;
        foreach (var gruppe in d.Klausuren.Where(k => k.Aktiv && k.KlassenIds.Count > 0 && (ab is null || k.Datum >= ab)).GroupBy(k => k.Datum).ToList())
        {
            if (!Kalender.IstUnterrichtstag(d, gruppe.Key)) continue;
            var regel = Tagesplan.Regulaer(d, gruppe.Key);
            foreach (var k in gruppe)
            {
                if (!Erweitern(d, k, regel)) continue;
                anzahl++;
                tage.Add(k.Datum);
            }
        }
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return anzahl;
    }
}
