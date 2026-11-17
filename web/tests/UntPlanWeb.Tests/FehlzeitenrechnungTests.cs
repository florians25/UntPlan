using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Zählregeln der Kontrolle der Abwesenheiten (Klassenleitung): Abwesenheit → Fehlstunden/Fehltag, Status einer Abwesenheit mit
/// unterschiedlichen Status, Abwesenheitsgründe, die nicht zählen, Befreiungen, „Fehltage und -zeiten trennen“, Prozentangaben, 75 %-Regel
/// und „Zusammenfassen“ – genau nach den Beispielen des Leitfadens.
/// </summary>
public class FehlzeitenrechnungTests
{
    private const string Tag = "2026-10-12";
    // Stundenplan des Tages: Unterricht in der 1., 2., 4., 5. und 6. Stunde (je 45 Minuten), die 3. Stunde ist frei
    private static readonly Func<string, string, IReadOnlyList<Planeinheit>> Plan = (_, _) => new[] { 1, 2, 4, 5, 6 }.Select(n => new Planeinheit(n, 45)).ToList();

    private static Fehlposten P(int id, int stunde, string status, string abw = "abw:1", string art = "fehlt", int minuten = 45, string grund = "k", bool zaehlt = true, bool befreit = false, string datum = Tag) =>
        new(id, 7, "7a", datum, stunde, art, status, grund, abw, minuten, zaehlt, befreit);

    [Fact]
    public void Drei_Stunden_Abwesenheit_davon_zwei_im_Unterricht_ergibt_zwei_entschuldigte_Fehlstunden()
    {
        // Abwesenheit 1.–3. Stunde, Unterricht nur in der 1. und 2. → Fehlzeiten nur dort (1/E, 1/E, 0)
        var posten = new[] { P(1, 1, "entschuldigt"), P(2, 2, "entschuldigt") };
        var a = Fehlzeitenrechnung.Auswerten(7, posten, Plan);
        Assert.Equal(2, a.Schulstunden);
        Assert.Equal(0, a.SchulstundenUnent);
        Assert.Equal(0, a.Fehltage);
        Assert.Equal(90, a.Minuten);
    }

    [Fact]
    public void Neun_Stunden_Abwesenheit_mit_unterschiedlichem_Status_ergibt_einen_unentschuldigten_Fehltag()
    {
        // 9 Stunden Abwesenheit, davon 5 im Unterricht (100 %), Status 1/E 1/E 1/U 1/E 1/E → 1 Fehltag unentschuldigt
        var posten = new[] { P(1, 1, "entschuldigt"), P(2, 2, "entschuldigt"), P(3, 4, "unentschuldigt"), P(4, 5, "entschuldigt"), P(5, 6, "entschuldigt") };
        var a = Fehlzeitenrechnung.Auswerten(7, posten, Plan);
        Assert.Equal(1, a.Fehltage);
        Assert.Equal(1, a.FehltageUnent);
        // Status der Abwesenheit unbestimmt → alle Fehlzeiten unentschuldigt
        Assert.All(Fehlzeitenrechnung.WirksamerStatus(posten).Values, s => Assert.Equal("unentschuldigt", s));
        Assert.Equal(5, a.SchulstundenUnent);
        var tag = Assert.Single(Fehlzeitenrechnung.Tage(posten, Plan));
        Assert.True(tag.Fehltag);
        Assert.Equal(100, tag.Prozent);
        Assert.Equal("100 %", Fehlzeitenrechnung.Prozent(tag.Prozent));
        // „Fehltage und -zeiten trennen“: die Stunden des Fehltags zählen nicht zusätzlich als Fehlstunden
        var t = Fehlzeitenrechnung.Auswerten(7, posten, Plan, trennen: true);
        Assert.Equal((0, 1, 1), (t.Schulstunden, t.Fehltage, t.FehltageUnent));
    }

    [Fact]
    public void Getrennte_Abwesenheiten_behalten_ihren_eigenen_Status()
    {
        // zwei Abwesenheiten am selben Tag: eine entschuldigt, eine offen – nur der Tag ist unentschuldigt
        var posten = new[] { P(1, 1, "entschuldigt", "#1"), P(2, 2, "offen", "#2") };
        var w = Fehlzeitenrechnung.WirksamerStatus(posten);
        Assert.Equal("entschuldigt", w[1]);
        Assert.Equal("offen", w[2]);
        var a = Fehlzeitenrechnung.Auswerten(7, posten, Plan);
        Assert.Equal((2, 1, 0), (a.Schulstunden, a.SchulstundenUnent, a.Fehltage));
    }

    [Fact]
    public void Schulische_Veranstaltung_und_Befreiung_zaehlen_nicht()
    {
        // Schneesportwoche mit Grund „Schul“: Fehltag, zählt aber nicht
        var schul = new[] { 1, 2, 4, 5, 6 }.Select(n => P(n, n, "entschuldigt", grund: "Schul", zaehlt: false)).ToList();
        var a = Fehlzeitenrechnung.Auswerten(7, schul, Plan);
        Assert.Equal((5, 0, 1, 0), (a.Schulstunden, a.SchulstundenZaehlend, a.Fehltage, a.FehltageZaehlend));
        // Fehlzeit während einer Befreiung zählt nicht
        var befreit = P(9, 1, "offen", "#9", befreit: true);
        Assert.False(befreit.Zaehlt);
        var b = Fehlzeitenrechnung.Auswerten(7, new[] { befreit }, Plan);
        Assert.Equal((1, 0, 0), (b.Schulstunden, b.SchulstundenZaehlend, b.SchulstundenZaehlendUnent));
    }

    [Fact]
    public void Verspaetung_zaehlt_nur_mit_Minuten()
    {
        // Zug/Bus verspätet 10 Minuten: 0 Schulstunden, 0:10 Zeitstunden
        var a = Fehlzeitenrechnung.Auswerten(7, new[] { P(1, 1, "entschuldigt", "#1", art: "verspaetet", minuten: 10, grund: "Z") }, Plan);
        Assert.Equal((0, 10), (a.Schulstunden, a.Minuten));
        Assert.Equal("0:10", Fehlzeitenrechnung.Hhmm(a.Minuten));
        Assert.Equal("4:30", Fehlzeitenrechnung.Hhmm(270));
    }

    [Fact]
    public void Prozentangaben_und_75_Prozent_Regel()
    {
        // pro Tag: 4:30 von 10:15 → 43,9 %
        Assert.Equal(43.9, Fehlzeitenrechnung.Anteil(270, 615));
        Assert.Equal("43,9 %", Fehlzeitenrechnung.Prozent(Fehlzeitenrechnung.Anteil(270, 615)));
        // Fehlzeiten pro Unterricht: 5:55 von 52 Unterrichtsstunden (39:00) → 15,17 %, davon unent. 2:55 → 7,48 %
        Assert.Equal(15.17, Fehlzeitenrechnung.Anteil(355, 52 * 45));
        Assert.Equal(7.48, Fehlzeitenrechnung.Anteil(175, 52 * 45));
        var (fehl, anw, zugelassen) = Fehlzeitenrechnung.Teilnahme(355, 52 * 45);
        Assert.Equal((15.17, 84.83, true), (fehl, anw, zugelassen));
        // genau 25 % gefehlt → 75 % besucht → zugelassen; mehr gefehlt → nicht zugelassen
        Assert.True(Fehlzeitenrechnung.Teilnahme(585, 2340).Zugelassen);
        Assert.False(Fehlzeitenrechnung.Teilnahme(586, 2340).Zugelassen);
    }

    [Fact]
    public void Entschuldigungsstatus_mit_Namen_zaehlen_als_unentschuldigt()
    {
        Assert.Equal(12, Fehlzeitenrechnung.Statusliste.Count);
        Assert.Equal(("entschuldigt", ""), Fehlzeitenrechnung.StatusAus("Entschuldigt"));
        Assert.Equal(("offen", ""), Fehlzeitenrechnung.StatusAus("[offen]"));
        foreach (var n in new[] { "Nicht akzeptiert", "Zu spät", "u-BußgAnd", "u-SchAAnd", "u-1Mahn", "u-2Mahn", "u-Bußgeld", "u-SchA", "u-3...Mahn" })
            Assert.Equal(("unentschuldigt", n), Fehlzeitenrechnung.StatusAus(n));
        Assert.Equal(("unentschuldigt", ""), Fehlzeitenrechnung.StatusAus("Nicht entschuldigt"));
        Assert.Null(Fehlzeitenrechnung.StatusAus("Chronische Unlust"));
        Assert.Equal("[offen]", Fehlzeitenrechnung.StatusText("offen"));
        Assert.Equal("u-1Mahn", Fehlzeitenrechnung.StatusText("unentschuldigt", "u-1Mahn"));
    }

    [Fact]
    public void Zusammenfassen_nur_bei_gleichem_Grund_passendem_Status_und_ohne_Anwesenheit_dazwischen()
    {
        static DateTime T(int tag, string zeit) => DateTime.Parse($"2023-06-{tag:00} {zeit}");
        // Ellen fehlt 4 Tage am Stück (Grund u, ohne Status) → eine Abwesenheit
        var ellen = Enumerable.Range(19, 4).Select(t => new Zusammenfassblock($"e{t}", 2, "u", "offen", T(t, t == 19 ? "07:50" : "09:35"), T(t, "21:35"))).ToList();
        // Axel: 19. krank (entschuldigt), 21. und 22. unbekannt – am 22. war er in den ersten beiden Stunden anwesend
        var axel = new List<Zusammenfassblock>
        {
            new("a19", 1, "k", "entschuldigt", T(19, "07:50"), T(19, "21:35")),
            new("a21", 1, "u", "offen", T(21, "09:35"), T(21, "21:35")),
            new("a22", 1, "u", "offen", T(22, "09:35"), T(22, "21:35")),
        };
        bool Anwesend(int sid, DateTime ende, DateTime beginn) => sid == 1 && beginn.Day == 22;
        var gruppen = Fehlzeitenrechnung.Zusammenfassen(ellen.Concat(axel), Anwesend);
        var g = Assert.Single(gruppen);
        Assert.Equal(4, g.Bloecke.Count);
        Assert.All(g.Bloecke, b => Assert.Equal(2, b.SchuelerId));
        Assert.Equal("offen", g.Status);
        // nicht gesetzter Status stimmt mit jedem überein, verschiedene gesetzte Status nicht
        var gemischt = new[]
        {
            new Zusammenfassblock("x1", 3, "k", "offen", T(19, "07:50"), T(19, "21:35")),
            new Zusammenfassblock("x2", 3, "k", "entschuldigt", T(20, "07:50"), T(20, "21:35")),
            new Zusammenfassblock("x3", 3, "k", "unentschuldigt", T(21, "07:50"), T(21, "21:35")),
        };
        var r = Assert.Single(Fehlzeitenrechnung.Zusammenfassen(gemischt, (_, _, _) => false));
        Assert.Equal(new[] { "x1", "x2" }, r.Bloecke.Select(b => b.Schluessel));
        Assert.Equal("entschuldigt", r.Status);
        Assert.StartsWith("Nach der Bestätigung werden Abwesenheiten im gewählten Zeitbereich möglichst zusammengefasst", Fehlzeitenrechnung.ZusammenfassenHinweis);
    }
}
