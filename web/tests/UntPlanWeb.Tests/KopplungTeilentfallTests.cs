using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Kopplung (mehrere Lehrkräfte in einer Stunde, z. B. „SN, CS, SZ“), nur ein Element entfällt: Die Stunde findet für die übrigen statt –
/// eine grüne Änderungskarte, das entfallene Element durchgestrichen im Label ({ k: "", alt: "SN", neu: true }), kein roter Entfall
/// daneben. Erst wenn alle Elemente entfallen, bleibt es die rote Entfall-Karte. Im eigenen Plan der entfallenen Lehrkraft: Entfall.
/// </summary>
public class KopplungTeilentfallTests
{
    private static readonly DateOnly Tag = new(2026, 10, 14);

    private static Stand StandMit(IEnumerable<WebStunde> stunden, IEnumerable<WebVertretung>? vertretungen = null) => new(new WebPaket
    {
        Zeitraster = new WebZeitraster { TageProWoche = 5, Stunden = { new WebStundenzeit(1, "1.", "08:00", "08:45"), new WebStundenzeit(2, "2.", "08:50", "09:35") } },
        Tage = { new WebTag { Datum = "2026-10-14", Unterrichtstag = true, Stunden = stunden.ToList() } },
        Vertretungen = vertretungen?.ToList() ?? new(),
    }, "2026-10-14 07:00:00");

    private static WebStunde Zeile(string lehrer, string status = "regulaer", int? vertretung = null, string klasse = "5a") => new()
    {
        Stunde = 1, Unterricht = 7, Fach = "SPO", Klassen = { klasse }, Raeume = { "TH1" }, Status = status, Vertretung = vertretung,
        Lehrer = { lehrer },
    };

    private static WebVertretung Entfall(int id, string lehrer) => new()
    {
        Id = id, Datum = "2026-10-14", Stunde = 1, Art = "Entfall", Lehrer = lehrer, Vertreter = "---", Fach = "SPO", Raum = "TH1", Klassen = { "5a" },
    };

    [Fact]
    public void Ein_Element_entfaellt_gruene_Aenderung_statt_Entfall()
    {
        var s = StandMit(new[] { Zeile("SN", "entfall", 1), Zeile("CS"), Zeile("SZ") }, new[] { Entfall(1, "SN") });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, "5a"), (Elementtyp.Raum, "TH1"), (Elementtyp.Lehrer, "CS"), (Elementtyp.Lehrer, "SZ") })
        {
            var karten = plan.Karten(s, Tag, typ, kurz);
            var k = Assert.Single(karten);
            Assert.Equal("geaendert", k.Status);
            Assert.DoesNotContain("entfall", k.Marker);
            Assert.Contains("aenderung", k.Marker);
            Assert.Contains(new Wert("", "SN", true), k.Lehrer);
            Assert.Contains(new Wert("CS"), k.Lehrer);
            Assert.Contains(new Wert("SZ"), k.Lehrer);
            Assert.NotEqual("Entfall", k.Art);
        }
        // Plan von SN: die eigene Stunde entfällt
        var eigen = Assert.Single(plan.Karten(s, Tag, Elementtyp.Lehrer, "SN"));
        Assert.Equal("entfall", eigen.Status);
    }

    [Fact]
    public void Alle_Elemente_entfallen_bleibt_Entfall()
    {
        var s = StandMit(new[] { Zeile("SN", "entfall", 1), Zeile("CS", "entfall", 2), Zeile("SZ", "entfall", 3) },
            new[] { Entfall(1, "SN"), Entfall(2, "CS"), Entfall(3, "SZ") });
        var plan = new Plandienst();
        foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, "5a"), (Elementtyp.Raum, "TH1"), (Elementtyp.Lehrer, "CS") })
        {
            var k = Assert.Single(plan.Karten(s, Tag, typ, kurz));
            Assert.Equal("entfall", k.Status);
            Assert.Contains("entfall", k.Marker);
        }
    }
}
