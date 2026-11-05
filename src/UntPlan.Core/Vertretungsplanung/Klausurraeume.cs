using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Belegung eines Klausurraums durch anderen Unterricht.</summary>
public sealed record KlausurraumBelegung(int RaumId, int Stunde, TagesEintrag Eintrag);

/// <summary>
/// Räume einer Klausur (Handbuch Kap. 11.2): Ist ein gewählter Raum zur Klausurzeit durch anderen Unterricht belegt, wird gefragt –
/// Raum nicht verplanen, mit Kollision verplanen oder „Raum ohne Kollision verplanen (erzeugt Raumvertretung)“
/// (<see cref="Klausur.RaumVerdraengen"/>; die Raumvertretung entsteht beim Abgleich in <see cref="Vertretungen.Aktualisieren(Schuldatei, DateOnly)"/>).
/// </summary>
public static class Klausurraeume
{
    /// <summary>
    /// Unterricht, der zur Klausurzeit in einem der Klausurräume stattfindet – ohne den Unterricht, den die Klausur selbst ersetzt
    /// (Unterricht der Klausur, bei „Kollidierender Unterricht entfällt“ der Unterricht der Klassen) und ohne die Klausurzeilen.
    /// </summary>
    public static List<KlausurraumBelegung> Belegungen(Schuldatei d, Klausur k)
    {
        var res = new List<KlausurraumBelegung>();
        if (k.RaumIds.Count == 0) return res;
        var tp = Tagesplan.Berechnen(d, k.Datum);
        for (var s = k.StundeVon; s <= Math.Max(k.StundeVon, k.StundeBis); s++)
            foreach (var e in tp.Stunde(s).Where(e => e.Findetstatt))
            {
                if (e.Vertretung?.KlausurId == k.Id && e.Vertretung.Art == VertretungsArt.Klausur) continue;
                var uid = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId;
                if (k.UnterrichtId is int ku && uid == ku) continue;
                if (k.UnterrichtEntfaellt && e.KlassenIds.Intersect(k.KlassenIds).Any()) continue;
                foreach (var r in e.RaumIds.Where(k.RaumIds.Contains).Distinct()) res.Add(new KlausurraumBelegung(r, s, e));
            }
        return res;
    }
}
