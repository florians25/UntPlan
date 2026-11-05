using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

/// <summary>Ein Lehrertausch der Optimierung: in der Kopplungszeile <paramref name="Zeile"/> des Unterrichts unterrichtet nun <paramref name="NeuLehrerId"/>.</summary>
public sealed record Lehrertausch(int UnterrichtId, int Zeile, int? VorherLehrerId, int? NeuLehrerId);

/// <summary>
/// Lehrer-Zuordnung bei der Optimierung (Handbuch 4.3.1.13): Werden Engpässe erkannt, tauscht die Optimierung Lehrkräfte von
/// Unterrichten mit dem Kennzeichen „(V) Variabler Lehrer“, wenn der Plan dadurch besser bewertet wird. Zeilen mit „Lehrerzuordnung fix“
/// (<see cref="Kopplungszeile.LehrerFix"/>) bleiben unverändert, das Lehrer-Optimierungskennzeichen (<see cref="Lehrer.OptimierungsKz"/>) begrenzt die Tauschpartner; die Optionen „Kein Lehrertausch mit anderem Fach“, „Tausch nur für gleichstündige Unt.“ und
/// „Tausch nur innerhalb Jahrgangsstufe“ schränken die Tausche ein. Die ursprüngliche Lehrkraft wird gemerkt
/// (<see cref="Kopplungszeile.UrsprLehrerId"/>), „Lehrer laut Unterricht“ stellt sie wieder her.
/// </summary>
public static class Lehrerzuordnung
{
    /// <summary>Gibt es Unterrichte mit variablem Lehrer (Voraussetzung für die automatische Lehrerzuordnung)?</summary>
    public static bool Moeglich(Schuldatei d) => d.Unterrichte.Any(u => u.VariablerLehrer && !u.Ignorieren && u.Zeilen.Any(z => z.LehrerId.HasValue && !z.LehrerFix));

    /// <summary>Tauschbare Kopplungszeilen: Unterricht mit (V), Zeile mit Lehrkraft und ohne „Lehrerzuordnung fix“.</summary>
    public static List<(Unterricht U, int Zeile)> Tauschbar(Schuldatei d) =>
        d.Unterrichte.Where(u => u.VariablerLehrer && !u.Ignorieren)
            .SelectMany(u => u.Zeilen.Select((z, i) => (u, z, i)))
            .Where(x => x.z.LehrerId.HasValue && !x.z.LehrerFix)
            .Select(x => (x.u, x.i)).ToList();

    /// <summary>Darf die Lehrkraft der Zeile a mit der Lehrkraft der Zeile b getauscht werden (laut Optionen und Lehrbefähigung)?</summary>
    public static bool TauschErlaubt(DatenIndex ix, Unterricht ua, int za, Unterricht ub, int zb, OptimierungsOptionen o)
    {
        var a = ua.Zeilen[za];
        var b = ub.Zeilen[zb];
        if (ua.Id == ub.Id || a.LehrerId is not int la || b.LehrerId is not int lb || la == lb) return false;
        if (!ua.VariablerLehrer || !ub.VariablerLehrer || a.LehrerFix || b.LehrerFix) return false;
        // dieselbe Lehrkraft darf nicht zweimal im selben Unterricht stehen
        if (ua.Zeilen.Where((_, i) => i != za).Any(z => z.LehrerId == lb) || ub.Zeilen.Where((_, i) => i != zb).Any(z => z.LehrerId == la)) return false;
        // Lehrer-Optimierungskennzeichen (Stammdaten Lehrer „Opt.Kz.“): Ziffer nur mit gleichem, Buchstabe nur mit anderem Kennzeichen
        if (ix.Lehrer.TryGetValue(la, out var lehrerA) && ix.Lehrer.TryGetValue(lb, out var lehrerB) && !lehrerA.TauschErlaubt(lehrerB)) return false;
        if (o.KeinTauschAnderesFach && a.FachId != b.FachId) return false;
        if (o.TauschNurGleichstuendig && ua.Wochenstunden != ub.Wochenstunden) return false;
        if (o.TauschNurJahrgang)
        {
            var ja = Jahrgaenge(ix, ua);
            var jb = Jahrgaenge(ix, ub);
            if (ja.Count != 1 || jb.Count != 1 || !ja.SetEquals(jb)) return false;
        }
        // bei verschiedenen Fächern: neue Lehrkraft braucht die Lehrbefähigung (sofern Fächer eingetragen sind)
        if (a.FachId != b.FachId && (!Befaehigt(ix, lb, a.FachId) || !Befaehigt(ix, la, b.FachId))) return false;
        return true;
    }

    private static HashSet<string> Jahrgaenge(DatenIndex ix, Unterricht u) =>
        u.KlassenIds.Select(k => ix.Klassen.TryGetValue(k, out var kl) ? kl.Jahrgang : "").Where(j => j.Length > 0).ToHashSet();

    private static bool Befaehigt(DatenIndex ix, int lehrerId, int? fachId) =>
        fachId is not int f || !ix.Lehrer.TryGetValue(lehrerId, out var l) || l.FaecherIds.Count == 0 || l.FaecherIds.Contains(f);

    /// <summary>
    /// Tausche suchen, die die Bewertung des Plans senken (zufällige Reihenfolge, je Paar ein Versuch, höchstens
    /// <paramref name="maxVersuche"/> Bewertungen). Die Tausche werden in <paramref name="d"/> (einer Kopie) ausgeführt.
    /// </summary>
    public static List<Lehrertausch> Optimieren(Schuldatei d, IReadOnlyList<Planstunde> plan, OptimierungsOptionen o, Random rnd, CancellationToken ct = default, int maxVersuche = 400)
    {
        var tausche = new List<Lehrertausch>();
        var zeilen = Tauschbar(d);
        if (zeilen.Count < 2) return tausche;
        var ix = new DatenIndex(d);
        var paare = new List<(int A, int B)>();
        for (var i = 0; i < zeilen.Count; i++)
            for (var j = i + 1; j < zeilen.Count; j++)
                if (TauschErlaubt(ix, zeilen[i].U, zeilen[i].Zeile, zeilen[j].U, zeilen[j].Zeile, o)) paare.Add((i, j));
        if (paare.Count == 0) return tausche;
        // zufällige Reihenfolge
        for (var i = paare.Count - 1; i > 0; i--)
        {
            var k = rnd.Next(i + 1);
            (paare[i], paare[k]) = (paare[k], paare[i]);
        }
        var aktuell = Bewertung.Kennzahlen(d, plan).Bewertung;
        var versuche = 0;
        foreach (var (ia, ib) in paare)
        {
            if (versuche++ >= maxVersuche || aktuell == 0) break;
            ct.ThrowIfCancellationRequested();
            var (ua, za) = zeilen[ia];
            var (ub, zb) = zeilen[ib];
            // nach früheren Tauschen erneut prüfen
            if (!TauschErlaubt(ix, ua, za, ub, zb, o)) continue;
            Tauschen(ua.Zeilen[za], ub.Zeilen[zb]);
            var neu = Bewertung.Kennzahlen(d, plan).Bewertung;
            if (neu < aktuell)
            {
                aktuell = neu;
                tausche.Add(new Lehrertausch(ua.Id, za, ub.Zeilen[zb].LehrerId, ua.Zeilen[za].LehrerId));
                tausche.Add(new Lehrertausch(ub.Id, zb, ua.Zeilen[za].LehrerId, ub.Zeilen[zb].LehrerId));
            }
            else Tauschen(ua.Zeilen[za], ub.Zeilen[zb]);
        }
        return Zusammenfassen(tausche);
    }

    private static void Tauschen(Kopplungszeile a, Kopplungszeile b) => (a.LehrerId, b.LehrerId) = (b.LehrerId, a.LehrerId);

    /// <summary>Mehrfache Tausche derselben Zeile zu einem Eintrag zusammenfassen (erste Vorher-, letzte Neu-Lehrkraft).</summary>
    private static List<Lehrertausch> Zusammenfassen(List<Lehrertausch> liste) =>
        liste.GroupBy(t => (t.UnterrichtId, t.Zeile))
            .Select(g => new Lehrertausch(g.Key.UnterrichtId, g.Key.Zeile, g.First().VorherLehrerId, g.Last().NeuLehrerId))
            .Where(t => t.VorherLehrerId != t.NeuLehrerId).ToList();

    /// <summary>Tausche in die Schuldatei übernehmen; die Lehrkraft laut Unterricht wird beim ersten Tausch gemerkt.</summary>
    public static int Anwenden(Schuldatei d, IEnumerable<Lehrertausch> tausche)
    {
        var n = 0;
        foreach (var t in tausche)
        {
            var u = d.Unterrichte.FirstOrDefault(x => x.Id == t.UnterrichtId);
            if (u is null || t.Zeile < 0 || t.Zeile >= u.Zeilen.Count) continue;
            var z = u.Zeilen[t.Zeile];
            if (z.LehrerId != t.VorherLehrerId) continue;
            z.UrsprLehrerId ??= z.LehrerId;
            z.LehrerId = t.NeuLehrerId;
            if (z.UrsprLehrerId == z.LehrerId) z.UrsprLehrerId = null;
            n++;
        }
        return n;
    }

    /// <summary>„Lehrer laut Unterricht“: alle Lehrertausche früherer Optimierungen rückgängig machen. Ergebnis: Anzahl der Zeilen.</summary>
    public static int LautUnterricht(Schuldatei d)
    {
        var n = 0;
        foreach (var z in d.AlleUnterrichte.SelectMany(u => u.Zeilen).Where(z => z.UrsprLehrerId.HasValue))
        {
            z.LehrerId = z.UrsprLehrerId;
            z.UrsprLehrerId = null;
            n++;
        }
        return n;
    }

    /// <summary>Gibt es getauschte Lehrkräfte (für die Schaltfläche „Lehrer laut Unterricht“)?</summary>
    public static bool HatTausche(Schuldatei d) => d.AlleUnterrichte.SelectMany(u => u.Zeilen).Any(z => z.UrsprLehrerId.HasValue);
}
