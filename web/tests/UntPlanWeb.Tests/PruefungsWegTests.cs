using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Gesamter Weg einer Prüfung: in UntPlanWeb anlegen, verlegen bzw. löschen → Rückmeldung → Import in UntPlan
/// (Stundenplanänderungen, Klausuren) → Export-Paket → Anzeige im Web; auch mehrfacher Import/Export, Raumänderung + Prüfung,
/// mehrstündige Prüfungen und Prüfungen über mehrere Klassen. Erwartet: keine Dubletten, keine verwaisten Entfälle, richtiger Raum,
/// keine fehlenden Karten; ein verlegter Termin verschwindet am alten Platz.
/// </summary>
public class PruefungsWegTests
{
    private static readonly DateOnly Tag = new(2026, 10, 15);
    private static readonly DateOnly Tag2 = new(2026, 10, 16);

    /// <summary>Testumgebung: UntPlanWeb-Server und die Schuldatei von UntPlan mit Import/Export wie im Desktop.</summary>
    private sealed class Ablauf : IAsyncDisposable
    {
        public Testserver S = null!;
        public Schuldatei D = null!;
        public PortalEinstellungen P = null!;
        private int _revision = 1;
        private readonly WebExportOptionen _o = new() { TageVoraus = 7 };

        public static async Task<Ablauf> StartenAsync()
        {
            var a = new Ablauf { S = await Testserver.StartenAsync(mitPaket: false), D = Demo.Beispielschule() };
            a.D.AktivePeriode.Plan = Optimierer.Optimieren(a.D, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
            a.P = new PortalEinstellungen { Adresse = a.S.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
            Assert.True((await UntPlanWebExport.HochladenAsync(a.P, UntPlanWebExport.PaketErstellen(a.D, new DateOnly(2026, 10, 12), a._o) with { Revision = 1 })).Ok);
            return a;
        }

        public Stand Stand => S.Dienst<Paketdienst>().Aktuell!;
        public Plandienst Plan => S.Dienst<Plandienst>();
        public Rueckmeldungsdienst Rd => S.Dienst<Rueckmeldungsdienst>();

        /// <summary>Import im Dialog „Stundenplanänderungen“ (alle Prüfungen markiert), optional zusätzlich der automatische Abgleich; danach quittieren.</summary>
        public async Task<(List<string> Ok, List<(string Id, string Grund)> Nein)> ImportierenAsync(bool automatisch = false)
        {
            var liste = await UntPlanWebExport.RueckmeldungenAsync(P);
            var r = StundenplanAenderungen.Uebernehmen(D, StundenplanAenderungen.Analysieren(D, liste, Tag.AddDays(-10), Tag.AddDays(20), false), "test");
            if (automatisch) UntPlanWebExport.Uebernehmen(D, liste);
            await UntPlanWebExport.QuittierenAsync(P, r.Uebernommen);
            return r;
        }

        public async Task ExportierenAsync() =>
            Assert.True((await UntPlanWebExport.HochladenAsync(P, UntPlanWebExport.PaketErstellen(D, new DateOnly(2026, 10, 12), _o) with { Revision = ++_revision })).Ok);

        public async Task RundeAsync(bool automatisch = false) { await ImportierenAsync(automatisch); await ExportierenAsync(); }

        public List<Karte> Karten(DateOnly tag, string typ, string kurz, int von, int bis) =>
            Plan.Karten(Stand, tag, typ, kurz, Rd.Vorgemerkt(tag, Stand)).Where(x => x.Von <= bis && x.Bis >= von).ToList();

        public ValueTask DisposeAsync() => S.DisposeAsync();
    }

    /// <summary>Doppelstunde (zwei aufeinanderfolgende Stunden) einer Klasse mit genau einer Lehrkraft und einem Raum.</summary>
    private static (string Klasse, string Lehrer, int Von, string Raum, string Fach, int Unterricht) Doppelstunde(Ablauf a, DateOnly tag, string? ohneKlasse = null)
    {
        foreach (var kl in a.Stand.Paket.Elemente.Klassen.Select(k => k.Kurz).Where(k => k != ohneKlasse))
        {
            var karten = a.Plan.Karten(a.Stand, tag, Elementtyp.Klasse, kl);
            var k = karten.FirstOrDefault(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1 && x.Raeume.Count == 1 && x.Bis > x.Von);
            if (k != null) return (kl, k.Lehrer[0].K, k.Von, k.Raeume[0].K, k.Fach, k.Unterricht!.Value);
        }
        throw new InvalidOperationException("keine Doppelstunde");
    }

    private static async Task<string> FreierRaum(HttpClient c, DateOnly tag, int von, int bis, params string[] ohne) =>
        (await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum={tag:yyyy-MM-dd}&von={von}&bis={bis}")).AsArray()
            .Select(x => x!["kurz"]!.GetValue<string>()).First(x => !ohne.Contains(x));

    private static object Neu(DateOnly tag, int von, int bis, string[] klassen, string[] raeume, string[] aufsicht, string titel, string text = "") =>
        new { datum = tag.ToString("yyyy-MM-dd"), stundeVon = von, stundeBis = bis, klassen, raeume, aufsicht, titel, text };

    private static object Alt(DateOnly tag, int von, int bis, string[] klassen, string titel) =>
        new { datum = tag.ToString("yyyy-MM-dd"), stundeVon = von, stundeBis = bis, klassen, titel };

    /// <summary>
    /// Im Plan der Klasse: in jeder Stunde des Zeitraums genau eine Prüfungskarte (bei verschiedenen Unterrichten je Stunde die
    /// Stundenkarte als Prüfung), kein Entfall, Raum wie erwartet.
    /// </summary>
    private static Karte EinePruefung(Ablauf a, DateOnly tag, string klasse, int von, int bis, string raum)
    {
        var karten = a.Karten(tag, Elementtyp.Klasse, klasse, von, bis);
        var info = string.Join("\n", karten.Select(x => System.Text.Json.JsonSerializer.Serialize(x)));
        Assert.False(karten.Any(x => x.Status == "entfall"), info);
        for (var h = von; h <= bis; h++)
            Assert.True(karten.Count(x => x.Status == "klausur" && x.Von <= h && x.Bis >= h) == 1, $"Stunde {h}:\n{info}");
        var pruefungen = karten.Where(x => x.Status == "klausur").ToList();
        Assert.All(pruefungen, k => Assert.Contains(k.Raeume, w => w.K == raum));
        return pruefungen[0];
    }

    /// <summary>Keine Prüfung und kein Entfall mehr: der Unterricht findet regulär statt.</summary>
    private static void KeinePruefung(Ablauf a, DateOnly tag, string klasse, int von, int bis)
    {
        var karten = a.Karten(tag, Elementtyp.Klasse, klasse, von, bis);
        Assert.DoesNotContain(karten, x => x.Status is "klausur" or "entfall" || x.Art.StartsWith("Klausur"));
        Assert.NotEmpty(karten);
    }

    private static void KeineOffenenKlausurmeldungen(Ablauf a) =>
        Assert.DoesNotContain(a.Rd.Offene(), m => m.Typ is WebRueckmeldungsTypen.Klausur or WebRueckmeldungsTypen.KlausurLoeschen);

    [Fact]
    public async Task Mehrstuendige_Pruefung_ueber_zwei_Klassen_mehrfach_importiert_bleibt_eine_Karte_je_Klasse()
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var y = Doppelstunde(a, Tag, x.Klasse);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var raum = await FreierRaum(c, Tag, x.Von, x.Von + 1, x.Raum, y.Raum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von + 1, new[] { x.Klasse, y.Klasse }, new[] { raum }, new[] { x.Lehrer }, "Mathe Klausur", "Taschenrechner")));
        // vorgemerkt: schon eine Karte je Klasse
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1, raum);

        for (var i = 0; i < 3; i++) await a.RundeAsync(automatisch: i == 1);
        var k = Assert.Single(a.D.Klausuren);
        Assert.Equal((x.Von, x.Von + 1), (k.StundeVon, k.StundeBis));
        Assert.Equal(2, k.KlassenIds.Count);
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1, raum);
        EinePruefung(a, Tag, y.Klasse, x.Von, x.Von + 1, raum);
        // Raumplan: eine Prüfung im Prüfungsraum
        Assert.Single(a.Karten(Tag, Elementtyp.Raum, raum, x.Von, x.Von), z => z.Status == "klausur");
        KeineOffenenKlausurmeldungen(a);
    }

    [Fact]
    public async Task Geloeschte_Pruefung_mit_Text_verschwindet_ohne_verwaiste_Entfaelle()
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von + 1, new[] { x.Klasse }, Array.Empty<string>(), new[] { x.Lehrer }, "Englisch Test", "Vokabeln Unit 3")));
        await a.RundeAsync();
        var karte = EinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1, x.Raum);

        // Löschen mit den Werten der Karte (Titel = Kartentext)
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", Alt(Tag, karte.Von, karte.Bis, karte.Klassen.Select(w => w.K).ToArray(), karte.Text)));
        // bis zum Import: der Unterricht erscheint wieder regulär (kein Entfall ohne Prüfung)
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1);

        var (_, nein) = await a.ImportierenAsync();
        Assert.Empty(nein);
        Assert.Empty(a.D.Klausuren);
        Assert.DoesNotContain(a.D.Vertretungen, v => v.KlausurId.HasValue);
        await a.ExportierenAsync();
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1);
        KeineOffenenKlausurmeldungen(a);
        // erneuter Import/Export ändert nichts
        await a.RundeAsync(automatisch: true);
        Assert.Empty(a.D.Klausuren);
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von + 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verlegte_Pruefung_alter_Termin_verschwindet(bool automatisch)
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von, new[] { x.Klasse }, Array.Empty<string>(), new[] { x.Lehrer }, "Bio Test")));
        await a.RundeAsync();
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von, x.Raum);

        // auf den nächsten Tag in eine Stunde der Klasse verlegen
        var ziel = a.Karten(Tag2, Elementtyp.Klasse, x.Klasse, 1, 12).First(k => k.Unterricht.HasValue && k.Status == "regulaer" && k.Raeume.Count == 1);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new
        {
            alt = Alt(Tag, x.Von, x.Von, new[] { x.Klasse }, "Bio Test"),
            neu = Neu(Tag2, ziel.Von, ziel.Von, new[] { x.Klasse }, Array.Empty<string>(), new[] { x.Lehrer }, "Bio Test"),
        }));
        // vor dem Import: alter Termin weg, neuer vorgemerkt
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von);
        Assert.Single(a.Karten(Tag2, Elementtyp.Klasse, x.Klasse, ziel.Von, ziel.Von), k => k.Status == "klausur");

        for (var i = 0; i < 2; i++) await a.RundeAsync(automatisch);
        var k = Assert.Single(a.D.Klausuren);
        Assert.Equal(Tag2, k.Datum);
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von);
        EinePruefung(a, Tag2, x.Klasse, ziel.Von, ziel.Von, ziel.Raeume[0].K);
        Assert.DoesNotContain(a.D.Vertretungen, v => v.Datum == Tag && v.KlausurId.HasValue);
        KeineOffenenKlausurmeldungen(a);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verlegen_in_dieselbe_Zeit_mit_anderem_Raum_bzw_laenger(bool laenger)
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var raum1 = await FreierRaum(c, Tag, x.Von, x.Von + 1, x.Raum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von, new[] { x.Klasse }, new[] { raum1 }, new[] { x.Lehrer }, "Physik Klausur")));
        await a.RundeAsync();
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von, raum1);

        var raum2 = laenger ? raum1 : await FreierRaum(c, Tag, x.Von, x.Von + 1, x.Raum, raum1);
        var bis = laenger ? x.Von + 1 : x.Von;
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new
        {
            alt = Alt(Tag, x.Von, x.Von, new[] { x.Klasse }, "Physik Klausur"),
            neu = Neu(Tag, x.Von, bis, new[] { x.Klasse }, new[] { raum2 }, new[] { x.Lehrer }, "Physik Klausur"),
        }));
        // vorgemerkt: genau eine Prüfung mit dem neuen Raum bzw. über beide Stunden
        EinePruefung(a, Tag, x.Klasse, x.Von, bis, raum2);

        for (var i = 0; i < 2; i++) await a.RundeAsync(automatisch: i == 1);
        var k = Assert.Single(a.D.Klausuren);
        Assert.Equal((x.Von, bis), (k.StundeVon, k.StundeBis));
        Assert.Equal(raum2, a.D.Raeume.First(r => r.Id == k.RaumIds.Single()).Kurzname);
        EinePruefung(a, Tag, x.Klasse, x.Von, bis, raum2);
        KeineOffenenKlausurmeldungen(a);
    }

    [Fact]
    public async Task Verlegen_in_belegten_Raum_laesst_die_Pruefung_unveraendert()
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var y = Doppelstunde(a, Tag2);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von, new[] { x.Klasse }, Array.Empty<string>(), new[] { x.Lehrer }, "Chemie Test")));
        await a.RundeAsync();
        // Ziel: Raum einer anderen Klasse, der zur neuen Zeit belegt ist
        var belegt = a.Karten(Tag2, Elementtyp.Klasse, y.Klasse, y.Von, y.Von).First(k => k.Raeume.Count > 0).Raeume[0].K;
        var r = await c.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new
        {
            alt = Alt(Tag, x.Von, x.Von, new[] { x.Klasse }, "Chemie Test"),
            neu = Neu(Tag2, y.Von, y.Von, new[] { x.Klasse }, new[] { belegt }, new[] { x.Lehrer }, "Chemie Test"),
        });
        if (x.Klasse != y.Klasse) Assert.False(r.IsSuccessStatusCode);
        if (r.IsSuccessStatusCode) return;
        // nichts gelöscht: keine Löschmeldung, die Prüfung steht weiter am alten Termin
        Assert.DoesNotContain(a.Rd.Offene(), m => m.Typ == WebRueckmeldungsTypen.KlausurLoeschen);
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von, x.Raum);
        await a.RundeAsync();
        Assert.Single(a.D.Klausuren);
    }

    [Fact]
    public async Task Raumaenderung_und_Pruefung_dann_Pruefung_loeschen_behaelt_die_Raumaenderung()
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var raum = await FreierRaum(c, Tag, x.Von, x.Von, x.Raum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung", new { datum = "2026-10-15", stundeVon = x.Von, stundeBis = x.Von, unterricht = x.Unterricht, raeume = new[] { raum } }));
        await a.RundeAsync();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von, new[] { x.Klasse }, new[] { raum }, new[] { x.Lehrer }, "Deutsch Klassenarbeit")));
        for (var i = 0; i < 2; i++) await a.RundeAsync();
        var k = EinePruefung(a, Tag, x.Klasse, x.Von, x.Von, raum);
        Assert.Equal(x.Fach, k.Fach);
        Assert.Single(a.D.Klausuren);

        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", Alt(Tag, k.Von, k.Bis, new[] { x.Klasse }, k.Text)));
        await a.RundeAsync();
        Assert.Empty(a.D.Klausuren);
        var karte = Assert.Single(a.Karten(Tag, Elementtyp.Klasse, x.Klasse, x.Von, x.Von), z => z.Unterricht == x.Unterricht);
        Assert.NotEqual("entfall", karte.Status);
        Assert.Contains(karte.Raeume, w => w.K == raum);
        KeineOffenenKlausurmeldungen(a);
    }

    /// <summary>Reine Raumänderung (Stundendetails bzw. Raumbuchung) ist nie eine Prüfung – weder vorgemerkt noch nach der Übernahme in UntPlan.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raumaenderung_ist_nie_eine_Pruefung(bool automatisch)
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var raum = await FreierRaum(c, Tag, x.Von, x.Von, x.Raum);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung", new { datum = "2026-10-15", stundeVon = x.Von, stundeBis = x.Von, unterricht = x.Unterricht, raeume = new[] { raum } }));
        void Pruefen()
        {
            foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, x.Klasse), (Elementtyp.Lehrer, x.Lehrer), (Elementtyp.Raum, raum), (Elementtyp.Raum, x.Raum) })
                Assert.All(a.Karten(Tag, typ, kurz, x.Von, x.Von), k => { Assert.NotEqual("klausur", k.Status); Assert.False(Plandienst.IstKlausur(k.Art), k.Art); });
            var karte = Assert.Single(a.Karten(Tag, Elementtyp.Klasse, x.Klasse, x.Von, x.Von), z => z.Unterricht == x.Unterricht);
            Assert.Contains(karte.Raeume, w => w.K == raum);
        }
        Pruefen();
        for (var i = 0; i < 2; i++) { await a.RundeAsync(automatisch); Pruefen(); }
        Assert.Empty(a.D.Klausuren);
    }

    [Fact]
    public async Task Pruefungsliste_zeigt_keine_Dubletten_und_keine_geloeschten_Pruefungen()
    {
        await using var a = await Ablauf.StartenAsync();
        var x = Doppelstunde(a, Tag);
        var c = await a.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", Neu(Tag, x.Von, x.Von, new[] { x.Klasse }, Array.Empty<string>(), new[] { x.Lehrer }, "Kunst Test")));
        // importiert, aber (noch) nicht quittiert – Paket und offene Meldung zugleich
        var liste = await UntPlanWebExport.RueckmeldungenAsync(a.P);
        StundenplanAenderungen.Uebernehmen(a.D, StundenplanAenderungen.Analysieren(a.D, liste, Tag.AddDays(-10), Tag.AddDays(20), false), "test");
        await a.ExportierenAsync();
        var l = a.S.Dienst<Listendienst>();
        Assert.Single(l.Pruefungen(a.Stand, a.Rd.OffeneKlausuren(a.Stand), Tag, Tag, x.Klasse, null));
        EinePruefung(a, Tag, x.Klasse, x.Von, x.Von, x.Raum);

        // löschen: die Prüfung ist schon im Paket – es entsteht eine Löschmeldung, die Liste ist sofort leer
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", Alt(Tag, x.Von, x.Von, new[] { x.Klasse }, "Kunst Test")));
        Assert.Contains(a.Rd.Offene(), m => m.Typ == WebRueckmeldungsTypen.KlausurLoeschen);
        Assert.Empty(l.Pruefungen(a.Stand, a.Rd.OffeneKlausuren(a.Stand), Tag, Tag, x.Klasse, null, a.Rd.OffeneLoeschungen()));
        await a.RundeAsync();
        Assert.Empty(a.D.Klausuren);
        KeinePruefung(a, Tag, x.Klasse, x.Von, x.Von);
    }
}
