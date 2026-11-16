using UntPlan.Core.Model;

namespace UntPlan.Core.Planung;

/// <summary>Auswahl beim Ablegen auf ein violettes Feld (Raum und Ausweichräume belegt).</summary>
public enum RaumBelegtWahl
{
    /// <summary>„Behindernden Unterricht aus Raum verdrängen“: der andere Unterricht hat danach keinen Raum mehr.</summary>
    Verdraengen,
    /// <summary>„Kollision erzeugen“: der Raum wird von beiden Unterrichten belegt.</summary>
    KollisionErzeugen,
    /// <summary>„Raum nicht verplanen“: die Stunde wird ohne Raum verplant (Raum später über den Raumzuordnungsdialog).</summary>
    RaumNichtVerplanen,
}

/// <summary>
/// Verplanen auf ein violettes Feld (Handbuch, Manuelles Planen | Stunden verplanen): kollisionsfrei aus Sicht von Klassen und
/// Lehrkräften, aber weder der Raum des Unterrichts noch einer seiner Ausweichräume ist frei.
/// </summary>
public static class RaumBelegtVerplanen
{
    public static readonly string[] Optionen = { "Behindernden Unterricht aus Raum verdrängen", "Kollision erzeugen", "Raum nicht verplanen" };

    /// <summary>Kopplungszeilen der Stunde, die einen Raum wünschen, aber keinen erhalten haben.</summary>
    public static List<int> OhneRaum(DatenIndex ix, Planstunde p)
    {
        if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) return new();
        return Enumerable.Range(0, u.Zeilen.Count).Where(i => p.RaumFuerZeile(i) is null && ix.RaumKandidaten(u, u.Zeilen[i]).Count > 0).ToList();
    }

    /// <summary>Text der Rückfrage: welcher Raum durch welchen Unterricht belegt ist.</summary>
    public static string Fragetext(Schuldatei d, DatenIndex ix, Planstunde p)
    {
        var u = ix.Unterrichte[p.UnterrichtId];
        var zeilen = new List<string>();
        foreach (var i in OhneRaum(ix, p))
        {
            var r = ix.RaumKandidaten(u, u.Zeilen[i])[0];
            var belegt = d.Stundenplan.Where(q => q != p && q.Tag == p.Tag && q.Stunde == p.Stunde && q.Raeume.Contains(r))
                .Select(q => ix.Unterrichte.TryGetValue(q.UnterrichtId, out var o) ? $"U{o.Id} {ix.UnterrichtText(o)}" : "").Where(x => x.Length > 0);
            var wer = string.Join(", ", belegt);
            zeilen.Add($"Raum {ix.KRaum(r)} ist belegt durch {(wer.Length > 0 ? wer : "?")}");
        }
        return $"{Planungsdienst.Tagname(d, p.Tag)}-{p.Stunde}: Raum und Ausweichräume des Unterrichts {u.Id} sind belegt.\n" + string.Join("\n", zeilen);
    }

    /// <summary>Wahl anwenden: den gewünschten Raum (erster Raumkandidat) zuordnen und ggf. aus dem anderen Unterricht verdrängen.</summary>
    public static void Anwenden(Schuldatei d, Planstunde p, RaumBelegtWahl wahl)
    {
        if (wahl == RaumBelegtWahl.RaumNichtVerplanen) return;
        var ix = new DatenIndex(d);
        var u = ix.Unterrichte[p.UnterrichtId];
        foreach (var i in OhneRaum(ix, p))
            Planungsdienst.RaumZuordnen(d, p, i, ix.RaumKandidaten(u, u.Zeilen[i])[0],
                wahl == RaumBelegtWahl.Verdraengen ? Planungsdienst.RaumKonfliktLoesung.Verdraengen : Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen);
    }
}
