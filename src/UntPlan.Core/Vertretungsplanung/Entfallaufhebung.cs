using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// „Entfall rückgängig“ im tagesaktuellen Stundenplan (Vertretungsmodus): Eine entfallene Stunde wird wieder zum regulären
/// Unterricht, wenn ihre Lehrkraft anwesend ist; ist die Lehrkraft abwesend, wird sie zur offenen Vertretung („???“).
/// Gilt je Kopplungszeile: Im Klassen-, Raum- und Fachplan werden alle entfallenen Zeilen der Stunde betrachtet, im Lehrerplan
/// nur die Zeile der Lehrkraft. Verlegte Stunden (Entfall mit „nach …“) bleiben unberührt – dort hebt man die Verlegung auf.
/// </summary>
public static class Entfallaufhebung
{
    /// <summary>Was mit einer Entfall-Zeile beim Aufheben geschieht.</summary>
    public enum Wirkung
    {
        /// <summary>Lehrkraft anwesend: die Stunde findet wieder regulär statt.</summary>
        Regulaer,
        /// <summary>Lehrkraft abwesend: offene Vertretung („???“).</summary>
        Offen,
    }

    /// <summary>Entfall-Zeilen einer Stunde, die aufgehoben werden können (wie <see cref="Vertretungen.EntfallZeilen"/>).</summary>
    public static List<Vertretung> Zeilen(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde) =>
        Vertretungen.EntfallZeilen(d, unterrichtId, zeilenIndex, datum, stunde);

    /// <summary>Kann die Stunde (bzw. Zeile) aufgehoben werden?</summary>
    public static bool Moeglich(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde) =>
        Zeilen(d, unterrichtId, zeilenIndex, datum, stunde).Count > 0;

    /// <summary>Lehrkraft der Zeile zu dieser Stunde abwesend (Absenz oder Veranstaltung)?</summary>
    public static bool LehrkraftAbwesend(Schuldatei d, Vertretung v) =>
        v.LehrerId is int l && Absenzen.LehrerAbwesend(d, l, v.Datum, v.Stunde);

    /// <summary>Vorschau: Wirkung des Aufhebens je Zeile.</summary>
    public static Wirkung WirkungVon(Schuldatei d, Vertretung v) => LehrkraftAbwesend(d, v) ? Wirkung.Offen : Wirkung.Regulaer;

    /// <summary>
    /// Zusammenhängende Stunden (Doppelstunde/Block) um <paramref name="stunde"/>, in denen derselbe Unterricht (bzw. dieselbe Zeile)
    /// entfällt und aufgehoben werden kann – aufsteigend sortiert; leer, wenn die Stunde selbst nicht entfallen ist.
    /// </summary>
    public static List<int> Block(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde)
    {
        var res = new List<int>();
        if (!Moeglich(d, unterrichtId, zeilenIndex, datum, stunde)) return res;
        var von = stunde;
        while (von > 1 && Moeglich(d, unterrichtId, zeilenIndex, datum, von - 1)) von--;
        var bis = stunde;
        var max = Math.Max(stunde, d.Zeitraster.StundenProTag);
        while (bis < max && Moeglich(d, unterrichtId, zeilenIndex, datum, bis + 1)) bis++;
        for (var s = von; s <= bis; s++) res.Add(s);
        return res;
    }

    /// <summary>
    /// Entfall der Stunde aufheben. Manuelle Entfälle (z. B. „Stunde entfallen lassen“) werden gelöscht – der Unterricht findet
    /// wieder regulär statt; ist die Lehrkraft abwesend, entsteht aus der Absenz die offene Vertretung. Aus einer Absenz bzw.
    /// Veranstaltung entstandene Entfälle werden zur offenen Vertretung; bei anwesender Lehrkraft (z. B. Entfall wegen einer
    /// Klausur der Klasse) hält die Lehrkraft ihren Unterricht wieder selbst. Ergebnis: Anzahl aufgehobener Zeilen.
    /// </summary>
    public static int Aufheben(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde, string benutzer = "")
    {
        var liste = Zeilen(d, unterrichtId, zeilenIndex, datum, stunde);
        var neuBerechnen = false;
        foreach (var v in liste)
        {
            if (!Vertretungen.IstAbgeleitet(v))
            {
                if (LehrkraftAbwesend(d, v))
                {
                    // die Absenz übernimmt die Zeile wieder (Grund, Absenz-Nr. beim Neuberechnen) – als offene Vertretung
                    var l = v.LehrerId!.Value;
                    var abw = Absenzen.Lehrer(d, l, datum, stunde);
                    v.Quelle = abw is null ? VertretungsQuelle.Veranstaltung : VertretungsQuelle.Absenz;
                    v.AbwesenheitId = abw?.Id;
                    v.VeranstaltungId = abw is null ? Absenzen.VeranstaltungLehrer(d, l, datum, stunde)?.Id : null;
                    Offen(d, v, benutzer);
                }
                else d.Vertretungen.Remove(v);
                neuBerechnen = true;
                continue;
            }
            if (LehrkraftAbwesend(d, v)) Offen(d, v, benutzer);
            else
            {
                // abgeleitete Zeile (z. B. Klausur der Klasse), Lehrkraft anwesend: sie hält ihren Unterricht selbst
                v.Art = VertretungsArt.UnterrichtGeaendert;
                v.VertreterId = v.LehrerId;
                v.VertretungsraumId = v.RaumId;
                v.Mehrarbeit = false;
                v.Bearbeitet = true;
                Vertretungen.AlsGeaendertMarkieren(v, benutzer);
            }
        }
        if (neuBerechnen) Vertretungen.Aktualisieren(d, datum);
        return liste.Count;
    }

    /// <summary>Alle Stunden eines Blocks aufheben (siehe <see cref="Block"/>). Ergebnis: Anzahl aufgehobener Zeilen.</summary>
    public static int BlockAufheben(Schuldatei d, int unterrichtId, int? zeilenIndex, DateOnly datum, int stunde, string benutzer = "")
    {
        var n = 0;
        foreach (var s in Block(d, unterrichtId, zeilenIndex, datum, stunde)) n += Aufheben(d, unterrichtId, zeilenIndex, datum, s, benutzer);
        return n;
    }

    private static void Offen(Schuldatei d, Vertretung v, string benutzer)
    {
        // wie „Entfall rückgängig machen“ in der Vertretungsliste: wieder offene Vertretung
        if (v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt) Vertretungen.EntfallUmschalten(d, v, benutzer);
    }
}
