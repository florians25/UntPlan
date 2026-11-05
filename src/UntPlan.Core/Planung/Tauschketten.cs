using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.Core.Planung;

/// <summary>
/// Möglicher Tauschplatz einer Tauschkette. <see cref="Rang"/>: je niedriger, desto besser bewertet. Wird eine verplante Stunde
/// verdrängt (<see cref="Verdraengt"/>), zeigt der Planungsdialog „2!!“, sonst „!1!“ (die Kette wäre damit abgeschlossen).
/// </summary>
public sealed record Tauschplatz(int Tag, int Stunde, int Rang, Planstunde? Verdraengt, double Kosten)
{
    public bool Abschluss => Verdraengt is null;
    public string Symbol => Abschluss ? $"!{Rang}!" : $"{Rang}!!";
}

/// <summary>Ein ausgeführter Schritt einer Tauschkette.</summary>
public sealed record Kettenglied(int UnterrichtId, int VonTag, int VonStunde, int NachTag, int NachStunde, int? VerdraengtUnterricht);

/// <summary>
/// Tauschkette (Handbuch Stundenplan Kap. 5.5.2): Eine verplante Stunde wird an einen anderen Platz getauscht; verdrängt sie dort eine
/// Stunde, wird diese zur offenen Stunde der Kette (sie ist bis zum nächsten Schritt nicht verplant) und muss ihrerseits einen Platz
/// finden – bis eine Stunde auf einen freien Platz kommt und die Kette damit abgeschlossen ist. Angeboten werden nur Plätze, die den
/// Stundenplan gemäß Gewichtung nicht wesentlich verschlechtern (keine harten Verstöße wie Sperrungen oder Kollisionen).
/// </summary>
public sealed class Tauschkette
{
    /// <summary>Unterricht der offenen (bzw. zu Beginn: der gewählten) Stunde.</summary>
    public int UnterrichtId { get; private set; }
    /// <summary>Bisherige Position der offenen Stunde.</summary>
    public int Tag { get; private set; }
    public int Stunde { get; private set; }
    /// <summary>Die offene Stunde steht noch im Stundenplan (nur vor dem ersten Schritt).</summary>
    public bool ImPlan { get; private set; }
    public bool Abgeschlossen { get; private set; }
    public List<Kettenglied> Glieder { get; } = new();

    private Tauschkette(int unterrichtId, int tag, int stunde, bool imPlan)
    {
        UnterrichtId = unterrichtId;
        Tag = tag;
        Stunde = stunde;
        ImPlan = imPlan;
    }

    /// <summary>Kette mit einer verplanten Stunde beginnen („Beginn“).</summary>
    public static Tauschkette Beginnen(Planstunde p) => new(p.UnterrichtId, p.Tag, p.Stunde, true);

    /// <summary>Kopie des Zustands (für „Rückgängig 1 Schritt“).</summary>
    public Tauschkette Kopie()
    {
        var k = new Tauschkette(UnterrichtId, Tag, Stunde, ImPlan) { Abgeschlossen = Abgeschlossen };
        k.Glieder.AddRange(Glieder);
        return k;
    }

    /// <summary>Text wie im Vorbild: „Unt. 93: Di4 -&gt; ?“ (abgeschlossen: „Tauschkette abgeschlossen“).</summary>
    public string Text(Schuldatei d) => Abgeschlossen ? "Tauschkette abgeschlossen" : $"Unt. {UnterrichtId}: {Planungsdienst.Tagname(d, Tag)}{Stunde} -> ?";

    /// <summary>Die offene Stunde, solange sie noch im Plan steht.</summary>
    public Planstunde? StundeImPlan(Schuldatei d) =>
        ImPlan ? d.Stundenplan.FirstOrDefault(p => p.UnterrichtId == UnterrichtId && p.Tag == Tag && p.Stunde == Stunde) : null;

    /// <summary>Mögliche Tauschplätze der offenen Stunde, nach Bewertung gereiht (Rang 1 = am besten).</summary>
    public List<Tauschplatz> Plaetze(Schuldatei d)
    {
        var res = new List<(int Tag, int Stunde, Planstunde? Weg, double Kosten)>();
        if (Abgeschlossen) return new();
        var ix = new DatenIndex(d);
        if (!ix.Unterrichte.TryGetValue(UnterrichtId, out var u)) return new();
        var eigene = StundeImPlan(d);
        if (ImPlan && eigene is null) return new();
        var bew = new Platzbewerter(d, u, eigene);
        if (!bew.Gueltig) return new();
        var ignorieren = eigene is null ? Array.Empty<Planstunde>() : new[] { eigene };
        for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
            for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
            {
                if (t == Tag && s == Stunde) continue;
                if (!d.Zeitraster.Verfuegbar(t, s) || Planungsdienst.VorgabeVerletzt(d, ix, u, t, s)) continue;
                if (d.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Tag == t && p.Stunde == s)) continue;
                var h = Planungsdienst.Hindernisse(d, ix, u, t, s, ignorieren);
                if (h.Select(x => x.Planstunde).Distinct().Count() > 1) continue;
                Planstunde? weg = null;
                if (h.Count == 1)
                {
                    weg = h[0].Planstunde;
                    if (weg.Fixiert || weg.UnterrichtId == u.Id) continue;
                    // die verdrängte Stunde muss selbst wieder einen Platz finden können: nicht als Block an die Stelle gebunden
                    if (ix.Unterrichte.TryGetValue(weg.UnterrichtId, out var wu) && !string.IsNullOrWhiteSpace(wu.Band)) continue;
                }
                var k = bew.Kosten(t, s, weg is null ? null : new[] { weg });
                res.Add((t, s, weg, k));
            }
        if (res.Count == 0) return new();
        // „nicht wesentlich verschlechtern“: keine harten Verstöße gegenüber der bisherigen (bzw. besten) Lage
        var basis = eigene != null ? bew.KostenJetzt : res.Min(x => x.Kosten);
        var rang = 1;
        return res.Where(x => x.Kosten - basis < PlanModell.Hart / 2)
            .OrderBy(x => x.Kosten).ThenBy(x => x.Weg is null ? 0 : 1).ThenBy(x => x.Tag).ThenBy(x => x.Stunde)
            .Select(x => new Tauschplatz(x.Tag, x.Stunde, rang++, x.Weg, x.Kosten)).ToList();
    }

    /// <summary>
    /// „Tausch“: die offene Stunde an (<paramref name="tag"/>, <paramref name="stunde"/>) setzen. Liegt dort eine Stunde, wird sie
    /// verdrängt (entplant) und zur neuen offenen Stunde; sonst ist die Kette abgeschlossen. Nur angebotene Plätze sind erlaubt.
    /// </summary>
    public Kettenglied Tauschen(Schuldatei d, int tag, int stunde)
    {
        if (Abgeschlossen) throw new InvalidOperationException("Die Tauschkette ist bereits abgeschlossen.");
        var platz = Plaetze(d).FirstOrDefault(p => p.Tag == tag && p.Stunde == stunde)
                    ?? throw new InvalidOperationException($"{Planungsdienst.Tagname(d, tag)}-{stunde} ist kein möglicher Tauschplatz.");
        var ix = new DatenIndex(d);
        var u = ix.Unterrichte[UnterrichtId];
        var p = StundeImPlan(d);
        if (p is null)
        {
            p = new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde };
            d.Stundenplan.Add(p);
        }
        int? verdraengt = null;
        int wegTag = 0, wegStunde = 0;
        if (platz.Verdraengt is { } w)
        {
            verdraengt = w.UnterrichtId;
            wegTag = w.Tag;
            wegStunde = w.Stunde;
            d.Stundenplan.Remove(w);
        }
        p.Tag = tag;
        p.Stunde = stunde;
        p.Raeume = Planungsdienst.RaeumeSuchen(d, ix, u, tag, stunde, new[] { p });
        var glied = new Kettenglied(u.Id, Tag, Stunde, tag, stunde, verdraengt);
        Glieder.Add(glied);
        if (verdraengt is int vu)
        {
            UnterrichtId = vu;
            Tag = wegTag;
            Stunde = wegStunde;
            ImPlan = false;
        }
        else Abgeschlossen = true;
        return glied;
    }

    /// <summary>„-&gt; x &lt;-“: die offene Stunde bleibt unverplant (erscheint unter „Nicht verplant“), die Kette endet.</summary>
    public void Beenden() => Abgeschlossen = true;
}
