using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using Xunit.Abstractions;

namespace UntPlanWeb.Tests;

/// <summary>
/// Fall aus echten Daten: SK unterrichtet FIS4H und ITS41 jeweils SPO in SPO1 – zwei getrennte Unterrichte (keine Kopplung) zu verschiedenen
/// Zeiten. Die Stunde von ITS41 wird in die Stunde von FIS4H verlegt (Tausch bzw. zwei Verlegungen). Am Ziel stehen dann zwei Stunden mit
/// gleicher Lehrkraft, gleichem Fach und gleichem Raum: die verlegte Stunde von ITS41 (grün, Verlegungspfeil) und der Ursprung der
/// verlegten Stunde von FIS4H (rot, Entfall). Sie sind keine gemeinsame Stunde: im Plan von ITS41 steht FIS4H NICHT durchgestrichen
/// (keine Mitklasse, keine abwesende Klasse), Ursprung und Ziel bleiben in allen Plänen getrennte Karten. Ende zu Ende (Core → echter
/// Export → UntPlanWeb → /api/app/plan), alles bzw. nur Vertretungen übertragen.
/// </summary>
public class VerlegungGleicheLehrkraftTests
{
    private readonly ITestOutputHelper _aus;
    public VerlegungGleicheLehrkraftTests(ITestOutputHelper aus) => _aus = aus;

    private static readonly DateOnly Freitag = new(2026, 11, 6);
    private const int StundeFis = 3, StundeIts = 5, Spaeter = 6;

    public enum Art { Tausch, ZweiVerlegungen }

    private static (Schuldatei D, Unterricht Fis, Unterricht Its) Schule()
    {
        var d = Demo.NeueDatei("Testschule", "GY");
        var spo = new Fach { Id = d.NeueId(), Kurzname = "SPO", Langname = "Sport" };
        d.Faecher.Add(spo);
        var fis = new Klasse { Id = d.NeueId(), Kurzname = "FIS4H" };
        var its = new Klasse { Id = d.NeueId(), Kurzname = "ITS41" };
        var sk = new Lehrer { Id = d.NeueId(), Kurzname = "SK" };
        var raum = new Raum { Id = d.NeueId(), Kurzname = "SPO1" };
        d.Klassen.AddRange(new[] { fis, its });
        d.Lehrer.Add(sk);
        d.Raeume.Add(raum);
        var tag = (int)Freitag.DayOfWeek - 1;
        Unterricht Neu(Klasse k, int stunde)
        {
            var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { new Kopplungszeile { LehrerId = sk.Id, FachId = spo.Id, KlassenIds = { k.Id }, FachraumId = raum.Id } } };
            d.Unterrichte.Add(u);
            d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = { raum.Id } });
            return u;
        }
        return (d, Neu(fis, StundeFis), Neu(its, StundeIts));
    }

    private async Task<List<JsonNode>> KartenAsync(Testserver s, string typ, string kurz, int stunde)
    {
        var c = await s.AlsAsync("admin");
        var iso = Freitag.ToString("yyyy-MM-dd");
        var plan = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={iso}");
        var karten = plan["karten"]!.AsArray().Select(k => k!).Where(k => k["datum"]!.GetValue<string>() == iso && k["unterricht"] is not null
            && k["von"]!.GetValue<int>() <= stunde && k["bis"]!.GetValue<int>() >= stunde).ToList();
        foreach (var k in karten) _aus.WriteLine($"{typ} {kurz} {stunde}: {k.ToJsonString()}");
        return karten;
    }

    private static List<string> Kuerzel(JsonNode? liste, bool gestrichen) => liste?.AsArray()
        .Where(w => (w!["k"]!.GetValue<string>().Length == 0) == gestrichen)
        .Select(w => gestrichen ? w!["alt"]!.GetValue<string>() : w!["k"]!.GetValue<string>()).ToList() ?? new();

    private static string Status(JsonNode k) => k["status"]!.GetValue<string>();
    private static string? Verlegung(JsonNode k) => k["verlegung"]?.GetValue<string>();
    private static List<string> Klassen(JsonNode k) => Kuerzel(k["klassen"], false).Concat(Kuerzel(k["klassen"], true)).ToList();

    /// <summary>Keine durchgestrichene (abwesende) Klasse und keine Mitklasse auf der Karte.</summary>
    private static void OhneFremdeKlassen(JsonNode k)
    {
        Assert.Empty(k["mitklassen"]?.AsArray() ?? new JsonArray());
        Assert.Empty(k["abwesendeKlassen"]?.AsArray() ?? new JsonArray());
        Assert.False(k["klasseAbwesend"]?.GetValue<bool>() ?? false);
        Assert.Single(Klassen(k));
    }

    public static IEnumerable<object[]> Varianten() =>
        from a in Enum.GetValues<Art>()
        from nur in new[] { false, true }
        select new object[] { a, nur };

    [Theory]
    [MemberData(nameof(Varianten))]
    public async Task Verlegte_Stunde_auf_den_Platz_einer_anderen_Klasse_ist_keine_gemeinsame_Stunde(Art art, bool nurVertretungen)
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var (d, fis, its) = Schule();
        var fisNach = art == Art.Tausch ? StundeIts : Spaeter;
        if (art == Art.Tausch)
            Vertretungen.Tauschen(d, its.Id, Freitag, StundeIts, fis.Id, Freitag, StundeFis);
        else
        {
            Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Freitag, StundeFis, fis.Id, null, 1, Freitag, Spaeter), VerlegeModus.Verlegen);
            Tagesverlegung.Ausfuehren(d, Tagesverlegung.Analysieren(d, Freitag, StundeIts, its.Id, null, 1, Freitag, StundeFis), VerlegeModus.Verlegen);
        }
        foreach (var v in d.Vertretungen) _aus.WriteLine($"Zeile: {v.Art} U{v.UnterrichtId} {v.Stunde} → {v.Text}");
        var p = new PortalEinstellungen { Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
        var paket = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7 });
        Assert.True((await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 1 })).Ok);
        if (nurVertretungen)
        {
            var nur = UntPlanWebExport.PaketErstellen(d, Freitag.AddDays(-4), new WebExportOptionen { TageVoraus = 7, Teile = new() { WebTeile.Vertretungen } });
            Assert.True((await UntPlanWebExport.HochladenAsync(p, nur with { Revision = 2 })).Ok);
        }

        // Ziel der Verlegung von ITS41 (= Ursprung von FIS4H): im Plan von ITS41 nur die verlegte Stunde, FIS4H nicht durchgestrichen
        var itsZiel = Assert.Single(await KartenAsync(s, "klasse", "ITS41", StundeFis));
        Assert.Equal("nach", Verlegung(itsZiel));
        Assert.Equal(new[] { "ITS41" }, Klassen(itsZiel));
        OhneFremdeKlassen(itsZiel);
        // Plan von FIS4H: der Ursprung (rot, Entfall) – nichts von ITS41
        var fisVon = Assert.Single(await KartenAsync(s, "klasse", "FIS4H", StundeFis));
        Assert.Equal("entfall", Status(fisVon));
        Assert.Equal("von", Verlegung(fisVon));
        OhneFremdeKlassen(fisVon);
        // Lehrerplan SK und Raumplan SPO1: zwei getrennte Karten (Ziel ITS41 grün, Ursprung FIS4H rot)
        foreach (var (typ, kurz) in new[] { ("lehrer", "SK"), ("raum", "SPO1") })
        {
            var karten = await KartenAsync(s, typ, kurz, StundeFis);
            Assert.Equal(2, karten.Count);
            var nach = Assert.Single(karten, k => Verlegung(k) == "nach");
            Assert.Equal(new[] { "ITS41" }, Klassen(nach));
            Assert.NotEqual("entfall", Status(nach));
            var von = Assert.Single(karten, k => Verlegung(k) == "von");
            Assert.Equal(new[] { "FIS4H" }, Klassen(von));
            Assert.Equal("entfall", Status(von));
            Assert.All(karten, OhneFremdeKlassen);
        }

        // Ziel der Verlegung von FIS4H: grün mit Pfeil, ohne ITS41
        var fisZiel = Assert.Single(await KartenAsync(s, "klasse", "FIS4H", fisNach));
        Assert.Equal("nach", Verlegung(fisZiel));
        OhneFremdeKlassen(fisZiel);
        // Ursprung von ITS41: rot, ohne FIS4H
        var itsVon = Assert.Single(await KartenAsync(s, "klasse", "ITS41", StundeIts), k => Verlegung(k) == "von");
        Assert.Equal("entfall", Status(itsVon));
        OhneFremdeKlassen(itsVon);
        foreach (var (typ, kurz) in new[] { ("lehrer", "SK"), ("raum", "SPO1"), ("klasse", "FIS4H"), ("klasse", "ITS41") })
            foreach (var st in new[] { StundeFis, StundeIts, Spaeter })
                Assert.All(await KartenAsync(s, typ, kurz, st), OhneFremdeKlassen);
    }
}
