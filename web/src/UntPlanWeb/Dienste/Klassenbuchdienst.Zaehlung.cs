using System.Globalization;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Zählung der Fehlzeiten (siehe <see cref="Fehlzeitenrechnung"/>): Abwesenheitsgründe, die nicht zählen, Befreiungen, Stundenplan je Tag
/// und Zugehörigkeit zu einer Abwesenheit.
/// </summary>
public sealed partial class Klassenbuchdienst
{
    /// <summary>Kürzel der Abwesenheitsgründe, die nicht als Fehlzeit zählen (z. B. „Schul“, „Klab“, „OM“).</summary>
    public HashSet<string> NichtZaehlendeGruende() =>
        _db.Liste("SELECT kurz FROM abwesenheitsgruende WHERE zaehlt = 0", r => r.Text("kurz")).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Liegt die Fehlzeit in einer Befreiung (Zeitraum, Wochentag, Fach und Uhrzeit wie in Klassenbuch → Befreiungen)? Während einer Befreiung
    /// werden keine Fehlzeiten gezählt.
    /// </summary>
    public Func<Fehlzeit, bool> BefreitFinder(Stand s, IEnumerable<int> schuelerIds)
    {
        var ids = schuelerIds.Distinct().ToList();
        var befreiungen = ids.Count == 0 ? new List<(int Sid, string Von, string Bis, int Tag, string Fach, string Beginn, string Ende)>()
            : _db.Liste("SELECT schueler_id, von, bis, wochentag, fach, beginn, ende FROM befreiungen",
                r => (Sid: r.Zahl("schueler_id"), Von: r.Text("von"), Bis: r.Text("bis"), Tag: r.Zahl("wochentag"), Fach: r.Text("fach"), Beginn: r.Text("beginn"), Ende: r.Text("ende")))
                .Where(x => ids.Contains(x.Sid)).ToList();
        if (befreiungen.Count == 0) return _ => false;
        return f =>
        {
            if (!DateOnly.TryParse(f.Datum, CultureInfo.InvariantCulture, out var d)) return false;
            var beginn = Hhmm(StundenZeit(s, f.Stunde).Beginn);
            return befreiungen.Any(x => x.Sid == f.SchuelerId && string.CompareOrdinal(x.Von, f.Datum) <= 0 && string.CompareOrdinal(x.Bis, f.Datum) >= 0
                && (x.Tag == 0 || x.Tag == Plandienst.TagIndex(d) + 1)
                && (x.Fach.Length == 0 || Ci.Equals(x.Fach, f.Fach))
                && (x.Beginn.Length == 0 || x.Ende.Length == 0 || x.Beginn == "00:00" || (string.CompareOrdinal(beginn, x.Beginn) >= 0 && string.CompareOrdinal(beginn, x.Ende) < 0)));
        };
    }

    /// <summary>Unterrichtsstunden einer Klasse an einem Tag laut Plan mit ihrer Dauer (ohne Entfall und Veranstaltungen).</summary>
    public Func<string, string, IReadOnlyList<Planeinheit>> PlanFinder(Stand s)
    {
        var stunden = StundenFinder(s);
        return (klasse, datum) => stunden(klasse, datum).OrderBy(n => n).Select(n =>
        {
            var (b, e) = StundenZeit(s, n);
            return new Planeinheit(n, (int)Math.Round((e - b).TotalMinutes));
        }).ToList();
    }

    /// <summary>Schlüssel der Abwesenheit einer Fehlzeit: Abwesenheitsgruppe (Zeitraum, auch über mehrere Tage), sonst die Fehlzeit selbst.</summary>
    public static string AbwesenheitSchluessel(Fehlzeit f) => Zeitraumgruppe(f.Gruppe) ? f.Gruppe : $"#{f.Id}";

    /// <summary>Fehlzeiten als Posten für die Zählung (Fehlminuten minutengenau, Grund zählt, Befreiung).</summary>
    public List<Fehlposten> Fehlposten(Stand s, IEnumerable<Fehlzeit> liste)
    {
        var alle = liste.ToList();
        var nicht = NichtZaehlendeGruende();
        var befreit = BefreitFinder(s, alle.Select(f => f.SchuelerId));
        return alle.Select(f =>
        {
            var (ab, bi) = Fenster(s, f);
            return new Fehlposten(f.Id, f.SchuelerId, f.Klasse, f.Datum, f.Stunde, f.Art, f.Status, f.Grund, AbwesenheitSchluessel(f),
                (int)Math.Round((bi - ab).TotalMinutes), !nicht.Contains(f.Grund), befreit(f));
        }).ToList();
    }
}
