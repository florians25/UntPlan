using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Sondereinsatz im Planungsdialog per Drag &amp; Drop verlegen bzw. mit gedrückter Strg-Taste kopieren (Handbuch Vertretungsplanung,
/// Kap. 10.2 „Sondereinsatz verlegen und kopieren“). Am Ziel entsteht ein gleichartiger Sondereinsatz (kollidierender Unterricht der
/// Lehrkraft wird zur Vertretung bzw. zum Entfall, der Klasse zum Entfall); beim Verlegen wird der ursprüngliche Sondereinsatz samt
/// seiner Folgen („wegen Sondereinsatz“) gelöscht.
/// </summary>
public static class Sondereinsatzverlegung
{
    public static bool IstVerlegbar(Vertretung v) => v.Art is VertretungsArt.Sondereinsatz && v.UnterrichtId is null;

    /// <summary>Verlegt bzw. kopiert den Sondereinsatz <paramref name="vertretungsId"/>; liefert den neuen Sondereinsatz.</summary>
    public static Vertretung Verlegen(Schuldatei d, int vertretungsId, DateOnly ziel, int stunde, bool kopieren, string benutzer = "")
    {
        var v = d.Vertretungen.FirstOrDefault(x => x.Id == vertretungsId) ?? throw new InvalidOperationException("Sondereinsatz nicht gefunden.");
        if (!IstVerlegbar(v)) throw new InvalidOperationException("Nur Sondereinsätze ohne zugrunde liegenden Unterricht können verlegt bzw. kopiert werden.");
        if (!kopieren && v.Datum == ziel && v.Stunde == stunde) return v;
        if (stunde < 1 || stunde > d.Zeitraster.StundenProTag) throw new InvalidOperationException("Die Stunde liegt außerhalb des Zeitrasters.");
        var klassen = v.NeueKlassenIds.ToList();
        var lehrer = v.VertreterId is int l ? new List<int> { l } : new List<int>();
        var fach = v.VertretungsfachId ?? v.FachId;
        var raum = v.VertretungsraumId ?? v.RaumId;
        var (text, gruppe) = (v.Text, v.Schuelergruppe);
        if (!kopieren) Verlegungsaufhebung.SondereinsaetzeLoeschen(d, new[] { v.Id });
        var neu = Vertretungen.Sondereinsatz(d, ziel, stunde, stunde, klassen, lehrer, fach, raum, text, gruppe, null, benutzer);
        Vertretungen.Aktualisieren(d, ziel);
        return neu.First();
    }
}
