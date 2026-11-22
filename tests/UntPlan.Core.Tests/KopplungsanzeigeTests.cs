using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Gekoppelte Stunde im tagesaktuellen Klassenplan: Änderungen oben, offene Änderungen darunter, unveränderte Elemente unten;
/// entfällt nur ein Element der Kopplung, ist die Stunde eine Änderung – Entfall nur, wenn alle Elemente entfallen.
/// </summary>
public class KopplungsanzeigeTests
{
    private static readonly Unterricht U = new()
    {
        Id = 1,
        Zeilen = new()
        {
            new Kopplungszeile { LehrerId = 10, FachId = 100, KlassenIds = new() { 1 } },
            new Kopplungszeile { LehrerId = 11, FachId = 101, KlassenIds = new() { 1 } },
            new Kopplungszeile { LehrerId = 12, FachId = 102, KlassenIds = new() { 1 } },
            new Kopplungszeile { LehrerId = 13, FachId = 103, KlassenIds = new() { 1 } },
        },
    };

    private static TagesEintrag E(int zeile, TagesStatus status, VertretungsArt? art = null) => new()
    {
        Stunde = 2, Status = status, FachId = U.Zeilen[zeile].FachId, KlassenIds = new() { 1 },
        LehrerIds = status == TagesStatus.Offen ? new() : new() { U.Zeilen[zeile].LehrerId!.Value },
        Regel = new RegelStunde(new Planstunde { UnterrichtId = 1, Tag = 0, Stunde = 2 }, U, zeile),
        Vertretung = art is VertretungsArt a ? new Vertretung { Art = a, UnterrichtId = 1, ZeilenIndex = zeile } : null,
    };

    [Fact]
    public void ArtJeElement()
    {
        Assert.Equal(KopplungsteilArt.Unveraendert, Kopplungsanzeige.Art(E(0, TagesStatus.Regulaer)));
        Assert.Equal(KopplungsteilArt.Geaendert, Kopplungsanzeige.Art(E(0, TagesStatus.Vertreten, VertretungsArt.Vertretung)));
        Assert.Equal(KopplungsteilArt.Geaendert, Kopplungsanzeige.Art(E(0, TagesStatus.Vertreten, VertretungsArt.Raumvertretung)));
        Assert.Equal(KopplungsteilArt.Offen, Kopplungsanzeige.Art(E(0, TagesStatus.Offen, VertretungsArt.Vertretung)));
        Assert.Equal(KopplungsteilArt.Entfallen, Kopplungsanzeige.Art(E(0, TagesStatus.Entfall, VertretungsArt.Entfall)));
    }

    [Fact]
    public void EinzelnerEntfallInDerKopplungIstAenderung()
    {
        var eintraege = new List<TagesEintrag> { E(0, TagesStatus.Regulaer), E(1, TagesStatus.Entfall, VertretungsArt.Entfall), E(2, TagesStatus.Regulaer) };
        Assert.Equal(TagesStatus.Vertreten, Kopplungsanzeige.Zellstatus(eintraege));
        Assert.True(Kopplungsanzeige.AlsElementliste(eintraege));
        var z = Kopplungsanzeige.Zusammenfassen(eintraege);
        // das Feld bleibt eine stattfindende Stunde (kein Entfall), das entfallene Element steht oben
        Assert.NotEqual(TagesStatus.Entfall, z.Status);
        Assert.True(z.Findetstatt);
        Assert.Equal(new[] { 1, 0, 2 }, z.Kopplungsteile!.Select(t => t.Eintrag.Regel!.ZeilenIndex));
        Assert.Equal(KopplungsteilArt.Entfallen, z.Kopplungsteile![0].Art);
        Assert.True(z.Kopplungsteile![0].Hervorgehoben);
        Assert.False(z.Kopplungsteile![1].Hervorgehoben);
    }

    [Fact]
    public void AlleElementeEntfallenIstEntfall()
    {
        var eintraege = new List<TagesEintrag> { E(0, TagesStatus.Entfall, VertretungsArt.Entfall), E(1, TagesStatus.Entfall, VertretungsArt.Entfall) };
        Assert.Equal(TagesStatus.Entfall, Kopplungsanzeige.Zellstatus(eintraege));
        Assert.False(Kopplungsanzeige.AlsElementliste(eintraege));
    }

    [Fact]
    public void OhneAenderungKeineElementliste()
    {
        var eintraege = new List<TagesEintrag> { E(0, TagesStatus.Regulaer), E(1, TagesStatus.Regulaer) };
        Assert.Equal(TagesStatus.Regulaer, Kopplungsanzeige.Zellstatus(eintraege));
        Assert.False(Kopplungsanzeige.AlsElementliste(eintraege));
        Assert.False(Kopplungsanzeige.AlsElementliste(new List<TagesEintrag> { E(0, TagesStatus.Vertreten, VertretungsArt.Vertretung) }));
    }

    [Fact]
    public void ReihenfolgeAenderungOffenUnveraendert()
    {
        // Zeile 0 unverändert, 1 offen, 2 vertreten, 3 einzeln entfallen
        var eintraege = new List<TagesEintrag>
        {
            E(0, TagesStatus.Regulaer), E(1, TagesStatus.Offen, VertretungsArt.Vertretung),
            E(2, TagesStatus.Vertreten, VertretungsArt.Vertretung), E(3, TagesStatus.Entfall, VertretungsArt.Entfall),
        };
        Assert.Equal(TagesStatus.Offen, Kopplungsanzeige.Zellstatus(eintraege));
        var teile = Kopplungsanzeige.Reihenfolge(eintraege);
        Assert.Equal(new[] { 2, 3, 1, 0 }, teile.Select(t => t.Eintrag.Regel!.ZeilenIndex));
        Assert.Equal(new[] { KopplungsteilArt.Geaendert, KopplungsteilArt.Entfallen, KopplungsteilArt.Offen, KopplungsteilArt.Unveraendert }, teile.Select(t => t.Art));
        // Werte des Feldes vom obersten stattfindenden Element (Vertretung), Klassen aller Elemente
        var z = Kopplungsanzeige.Zusammenfassen(eintraege);
        Assert.Equal(2, z.Regel!.ZeilenIndex);
        Assert.Equal(TagesStatus.Vertreten, z.Status);
        Assert.Equal(new[] { 1 }, z.KlassenIds);
    }

    [Fact]
    public void TagesplanEinzelnerEntfallIstAenderungAllerEntfallIstEntfall()
    {
        var d = Testdaten.Verplant();
        // erster Unterrichtstag mit gekoppeltem Unterricht (Religion: KR und ER parallel)
        var tag = Testdaten.Unterrichtstag(d);
        RegelStunde? Gekoppelt(DateOnly t) => Tagesplan.Regulaer(d, t).FirstOrDefault(x => x.Unterricht.Zeilen.Count >= 2 && x.ZeilenIndex == 0);
        for (var i = 0; i < 10 && Gekoppelt(tag) is null; i++) tag = Kalender.NaechsterUnterrichtstag(d, tag.AddDays(1));
        var r = Gekoppelt(tag) ?? throw new InvalidOperationException("Kein gekoppelter Unterricht in den ersten zwei Wochen.");
        var u = r.Unterricht;
        List<TagesEintrag> Eintraege() => Tagesplan.Berechnen(d, tag).Stunde(r.Stunde).Where(e => e.Unterricht?.Id == u.Id).ToList();
        Assert.False(Kopplungsanzeige.AlsElementliste(Eintraege()));

        Vertretungen.Entfallen(d, u.Id, 0, tag, r.Stunde);
        var teilweise = Eintraege();
        Assert.Contains(teilweise, e => e.Status == TagesStatus.Entfall);
        Assert.Equal(TagesStatus.Vertreten, Kopplungsanzeige.Zellstatus(teilweise));
        Assert.True(Kopplungsanzeige.AlsElementliste(teilweise));
        var z = Kopplungsanzeige.Zusammenfassen(teilweise);
        Assert.Equal(0, z.Kopplungsteile![0].Eintrag.Regel!.ZeilenIndex);
        Assert.Equal(KopplungsteilArt.Entfallen, z.Kopplungsteile![0].Art);
        Assert.NotEqual(TagesStatus.Entfall, z.Status);

        for (var i = 1; i < u.Zeilen.Count; i++) Vertretungen.Entfallen(d, u.Id, i, tag, r.Stunde);
        var alle = Eintraege();
        Assert.All(alle, e => Assert.Equal(TagesStatus.Entfall, e.Status));
        Assert.Equal(TagesStatus.Entfall, Kopplungsanzeige.Zellstatus(alle));
        Assert.False(Kopplungsanzeige.AlsElementliste(alle));
    }
}
