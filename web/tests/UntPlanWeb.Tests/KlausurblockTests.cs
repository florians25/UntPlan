using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Ende-zu-Ende: Lehrkraft trägt in den Stundendetails eine Prüfung über ihre Doppelstunde ein (Formular mit den Vorgaben der Stunde:
/// Name = Fach, Raum und Aufsicht der Stunde), ggf. nach einer Raumbuchung → Desktop übernimmt (Dialog „Stundenplanänderungen“
/// und/oder automatischer Abgleich, auch mehrfach, auch ohne Quittung) → Export-Paket (alles bzw. nur Vertretungen) zurück ins Web.
/// Erwartet in jedem Schritt: genau EIN Prüfungsblock über beide Stunden im Klassen-, Lehrer- und Raumplan sowie in /api/app/plan,
/// kein Entfall daneben.
/// </summary>
public class KlausurblockTests
{
    private static readonly DateOnly Tag = new(2026, 10, 15);
    private static readonly DateOnly Tag2 = new(2026, 10, 16);
    private static readonly DateOnly Ab = new(2026, 10, 12);

    private sealed class Umgebung : IAsyncDisposable
    {
        public Testserver S = null!;
        public Schuldatei D = null!;
        public PortalEinstellungen P = null!;
        private int _revision = 1;

        public static async Task<Umgebung> StartenAsync()
        {
            var u = new Umgebung { S = await Testserver.StartenAsync(mitPaket: false), D = Demo.Beispielschule() };
            u.D.AktivePeriode.Plan = Optimierer.Optimieren(u.D, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 3 })[0].Plan;
            u.P = new PortalEinstellungen { Adresse = u.S.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel };
            await u.ExportierenAsync(nurVertretungen: false);
            return u;
        }

        public Stand Stand => S.Dienst<Paketdienst>().Aktuell!;
        public Plandienst Plan => S.Dienst<Plandienst>();
        public Rueckmeldungsdienst Rd => S.Dienst<Rueckmeldungsdienst>();

        /// <summary>Desktop: Rückmeldungen abholen, im Dialog „Stundenplanänderungen“ übernehmen bzw. automatisch abgleichen, quittieren.</summary>
        public async Task ImportierenAsync(bool dialog = true, bool automatisch = false, bool quittieren = true)
        {
            var liste = await UntPlanWebExport.RueckmeldungenAsync(P);
            var ok = new List<string>();
            if (dialog)
                ok.AddRange(StundenplanAenderungen.Uebernehmen(D, StundenplanAenderungen.Analysieren(D, liste, Tag.AddDays(-10), Tag.AddDays(20), false), "test").Uebernommen);
            if (automatisch) ok.AddRange(UntPlanWebExport.Uebernehmen(D, liste).Item1);
            if (quittieren) await UntPlanWebExport.QuittierenAsync(P, ok);
        }

        /// <summary>Desktop: Paket an UntPlanWeb (alles bzw. wie „Vertretungen“ nur Stammdaten + Vertretungen).</summary>
        public async Task ExportierenAsync(bool nurVertretungen)
        {
            var o = new WebExportOptionen { TageVoraus = 7, Heute = Ab };
            if (nurVertretungen) o.Teile = new HashSet<string> { WebTeile.Stammdaten, WebTeile.Vertretungen };
            var r = await UntPlanWebExport.HochladenAsync(P, UntPlanWebExport.PaketErstellen(D, Ab, o) with { Revision = ++_revision });
            Assert.True(r.Ok, r.Meldung);
        }

        public List<Karte> Karten(DateOnly tag, string typ, string kurz) => Plan.Karten(Stand, tag, typ, kurz, Rd.Vorgemerkt(tag, Stand));

        public ValueTask DisposeAsync() => S.DisposeAsync();
    }

    private sealed record Stunde(string Klasse, string Lehrer, int Von, int Bis, string Raum, string Fach, int Unterricht);

    /// <summary>Doppelstunde einer Klasse mit einer Lehrkraft und einem Raum (wie in den Stundendetails angeklickt).</summary>
    private static Stunde Doppelstunde(Umgebung u, DateOnly tag)
    {
        foreach (var kl in u.Stand.Paket.Elemente.Klassen.Select(k => k.Kurz))
        {
            var k = u.Plan.Karten(u.Stand, tag, Elementtyp.Klasse, kl)
                .FirstOrDefault(x => x.Unterricht.HasValue && x.Status == "regulaer" && x.Lehrer.Count == 1 && x.Raeume.Count == 1 && x.Bis == x.Von + 1 && x.Mitklassen.Count == 0);
            if (k != null) return new Stunde(kl, k.Lehrer[0].K, k.Von, k.Bis, k.Raeume[0].K, k.Fach, k.Unterricht!.Value);
        }
        throw new InvalidOperationException("keine Doppelstunde");
    }

    private static string Info(IEnumerable<Karte> karten) => string.Join("\n", karten.Select(x => System.Text.Json.JsonSerializer.Serialize(x)));

    /// <summary>Genau ein Prüfungsblock über von..bis, kein Entfall und keine weitere Karte derselben Stunden mit Prüfungsbezug.</summary>
    private static void EinBlock(List<Karte> alle, int von, int bis, string wo, string? raum = null)
    {
        var karten = alle.Where(x => x.Von <= bis && x.Bis >= von).ToList();
        var info = $"{wo}:\n{Info(karten)}";
        var pruefungen = karten.Where(x => x.Status is "klausur" or "vorgemerkt" || Plandienst.IstKlausur(x.Art)).ToList();
        Assert.True(pruefungen.Count == 1, $"{pruefungen.Count} Prüfungsblöcke – {info}");
        Assert.Equal("klausur", pruefungen[0].Status);
        Assert.True(pruefungen[0].Von <= von && pruefungen[0].Bis >= bis, info);
        Assert.DoesNotContain(karten, x => x.Status == "entfall");
        if (raum != null) Assert.Contains(pruefungen[0].Raeume, w => string.Equals(w.K, raum, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AppEinBlockAsync(HttpClient c, string typ, string kurz, int von, int bis)
    {
        var w = await Testserver.HolenAsync(c, $"/api/app/plan?typ={typ}&element={Uri.EscapeDataString(kurz)}&datum={Tag:yyyy-MM-dd}");
        var karten = w["karten"]!.AsArray().Select(x => x!.AsObject())
            .Where(x => x["datum"]!.GetValue<string>() == Tag.ToString("yyyy-MM-dd") && x["von"]!.GetValue<int>() <= bis && x["bis"]!.GetValue<int>() >= von).ToList();
        var info = $"/api/app/plan {typ} {kurz}:\n" + string.Join("\n", karten.Select(x => x.ToJsonString()));
        var pruefungen = karten.Where(x => x["status"]!.GetValue<string>() is "klausur" or "vorgemerkt" || Plandienst.IstKlausur(x["art"]?.GetValue<string>())).ToList();
        Assert.True(pruefungen.Count == 1, $"{pruefungen.Count} Prüfungsblöcke – {info}");
        Assert.True(pruefungen[0]["von"]!.GetValue<int>() <= von && pruefungen[0]["bis"]!.GetValue<int>() >= bis, info);
        Assert.DoesNotContain(karten, x => x["status"]!.GetValue<string>() == "entfall");
    }

    private static async Task UeberallEinBlockAsync(Umgebung u, HttpClient c, Stunde x, string raum, string schritt)
    {
        EinBlock(u.Karten(Tag, Elementtyp.Klasse, x.Klasse), x.Von, x.Bis, $"{schritt} Klasse {x.Klasse}", raum);
        EinBlock(u.Karten(Tag, Elementtyp.Lehrer, x.Lehrer), x.Von, x.Bis, $"{schritt} Lehrer {x.Lehrer}", raum);
        EinBlock(u.Karten(Tag, Elementtyp.Raum, raum), x.Von, x.Bis, $"{schritt} Raum {raum}", raum);
        await AppEinBlockAsync(c, Elementtyp.Klasse, x.Klasse, x.Von, x.Bis);
        await AppEinBlockAsync(c, Elementtyp.Lehrer, x.Lehrer, x.Von, x.Bis);
        await AppEinBlockAsync(c, Elementtyp.Raum, raum, x.Von, x.Bis);
        // Tagesübersicht (Lehrer): eine Zeile der Lehrkraft mit einem Block
        var tag = u.Plan.Tag(u.Stand, Elementtyp.Lehrer, Tag, true, d => u.Rd.Vorgemerkt(d, u.Stand));
        EinBlock(tag.Zeilen.Single(z => z.Kurz == x.Lehrer).Karten, x.Von, x.Bis, $"{schritt} Tagesübersicht {x.Lehrer}", raum);
    }

    private static async Task<string> FreierRaum(HttpClient c, int von, int bis, params string[] ohne) =>
        (await Testserver.HolenAsync(c, $"/api/app/meldungen/freieraeume?datum={Tag:yyyy-MM-dd}&von={von}&bis={bis}")).AsArray()
            .Select(x => x!["kurz"]!.GetValue<string>()).First(x => !ohne.Contains(x));

    /// <summary>Formular „Prüfung erstellen“ mit den Vorgaben der Stunde (Name = Fach, Raum und Aufsicht der Stunde).</summary>
    private static async Task PruefungEintragenAsync(HttpClient c, Stunde x, string raum, string text = "Klassenarbeit: Kapitel 3") =>
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new
        {
            datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, klassen = new[] { x.Klasse }, raeume = new[] { raum }, aufsicht = new[] { x.Lehrer },
            titel = x.Fach, text,
        }));

    public enum Vorher { Nichts, Raumaenderung, Raumbuchung }

    [Theory]
    [InlineData(Vorher.Nichts, false)]
    [InlineData(Vorher.Nichts, true)]
    [InlineData(Vorher.Raumaenderung, false)]
    [InlineData(Vorher.Raumaenderung, true)]
    [InlineData(Vorher.Raumbuchung, false)]
    [InlineData(Vorher.Raumbuchung, true)]
    public async Task Pruefung_ueber_Doppelstunde_ist_in_jedem_Schritt_genau_ein_Block(Vorher vorher, bool nurVertretungen)
    {
        await using var u = await Umgebung.StartenAsync();
        var x = Doppelstunde(u, Tag);
        var c = await u.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var raum = x.Raum;
        if (vorher == Vorher.Raumaenderung)
        {
            raum = await FreierRaum(c, x.Von, x.Bis, x.Raum);
            await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumaenderung",
                new { datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, unterricht = x.Unterricht, raeume = new[] { raum } }));
            await u.ImportierenAsync();
            await u.ExportierenAsync(nurVertretungen);
        }
        else if (vorher == Vorher.Raumbuchung)
        {
            raum = await FreierRaum(c, x.Von, x.Bis, x.Raum);
            await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung",
                new { datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, raum, klassen = new[] { x.Klasse }, titel = "Klausur", text = "" }));
            await u.ImportierenAsync();
            await u.ExportierenAsync(nurVertretungen);
        }

        await PruefungEintragenAsync(c, x, raum);
        // sofort nach dem Eintragen (noch nicht übernommen): eine gelbe Prüfungskarte an der Stelle der Stunden
        await UeberallEinBlockAsync(u, c, x, raum, "vorgemerkt");

        // übernommen, aber noch nicht quittiert und noch nicht exportiert
        await u.ImportierenAsync(quittieren: false);
        await UeberallEinBlockAsync(u, c, x, raum, "importiert ohne Quittung");
        await u.ExportierenAsync(nurVertretungen);
        await UeberallEinBlockAsync(u, c, x, raum, "exportiert ohne Quittung");

        // erneuter Import (Dialog + automatischer Abgleich) mit Quittung und Export
        await u.ImportierenAsync(automatisch: true);
        await u.ExportierenAsync(nurVertretungen);
        await UeberallEinBlockAsync(u, c, x, raum, "zweiter Import");

        // noch einmal: nichts ändert sich
        await u.ImportierenAsync(automatisch: true);
        await u.ExportierenAsync(!nurVertretungen);
        await u.ExportierenAsync(nurVertretungen);
        await UeberallEinBlockAsync(u, c, x, raum, "dritter Import");

        Assert.Single(u.D.Klausuren);
        Assert.DoesNotContain(u.Rd.Offene(), m => m.Typ is WebRueckmeldungsTypen.Klausur or WebRueckmeldungsTypen.KlausurLoeschen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nur_automatischer_Abgleich_ergibt_einen_Block(bool nurVertretungen)
    {
        await using var u = await Umgebung.StartenAsync();
        var x = Doppelstunde(u, Tag);
        var c = await u.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await PruefungEintragenAsync(c, x, x.Raum);
        await u.ImportierenAsync(dialog: false, automatisch: true);
        await u.ExportierenAsync(nurVertretungen);
        await UeberallEinBlockAsync(u, c, x, x.Raum, "automatisch");
        await u.ImportierenAsync(dialog: true, automatisch: true);
        await u.ExportierenAsync(nurVertretungen);
        await UeberallEinBlockAsync(u, c, x, x.Raum, "automatisch + Dialog");
        Assert.Single(u.D.Klausuren);
    }

    /// <summary>
    /// Parallele Kurse derselben Klasse (z. B. Oberstufe: BI und PH zur selben Zeit): die Kurslehrkraft trägt die Prüfung für ihre Stunde
    /// (Klasse der Stunde) ein. Im Klassenplan genau ein Prüfungsblock (auf ihrer Stunde), keiner auf dem parallelen Kurs, kein Entfall;
    /// der parallele Kurs findet weiter statt.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Pruefung_in_parallelem_Kurs_ist_ein_Block(bool nurVertretungen, bool altdaten)
    {
        await using var u = await Umgebung.StartenAsync();
        Stunde? x = null;
        string? anderer = null;
        foreach (var kl in u.Stand.Paket.Elemente.Klassen.Select(k => k.Kurz))
        {
            var karten = u.Plan.Karten(u.Stand, Tag, Elementtyp.Klasse, kl);
            var k = karten.FirstOrDefault(a => a.Unterricht.HasValue && a.Status == "regulaer" && a.Lehrer.Count == 1 && a.Raeume.Count == 1 && a.Bis > a.Von
                && karten.Any(b => b != a && b.Unterricht.HasValue && b.Von == a.Von && b.Bis == a.Bis && b.Status == "regulaer" && b.Lehrer.Count == 1 && b.Lehrer[0].K != a.Lehrer[0].K));
            if (k is null) continue;
            x = new Stunde(kl, k.Lehrer[0].K, k.Von, k.Bis, k.Raeume[0].K, k.Fach, k.Unterricht!.Value);
            anderer = karten.First(b => b != k && b.Unterricht.HasValue && b.Von == k.Von && b.Bis == k.Bis && b.Lehrer[0].K != k.Lehrer[0].K).Lehrer[0].K;
            break;
        }
        Assert.NotNull(x);
        var c = await u.S.AlsAsync(x!.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var klassen = u.Plan.Karten(u.Stand, Tag, Elementtyp.Lehrer, x.Lehrer).First(k => k.Von == x.Von && k.Unterricht == x.Unterricht).Klassen.Select(w => w.K).ToArray();
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new
        {
            datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, klassen, raeume = new[] { x.Raum }, aufsicht = new[] { x.Lehrer }, titel = x.Fach, text = "Test",
        }));
        async Task Pruefen(string schritt)
        {
            foreach (var kl in klassen)
            {
                EinBlock(u.Karten(Tag, Elementtyp.Klasse, kl), x.Von, x.Bis, $"{schritt} Klasse {kl}", x.Raum);
                await AppEinBlockAsync(c, Elementtyp.Klasse, kl, x.Von, x.Bis);
            }
            EinBlock(u.Karten(Tag, Elementtyp.Lehrer, x.Lehrer), x.Von, x.Bis, $"{schritt} Lehrer {x.Lehrer}", x.Raum);
            EinBlock(u.Karten(Tag, Elementtyp.Raum, x.Raum), x.Von, x.Bis, $"{schritt} Raum {x.Raum}", x.Raum);
            // der parallele Kurs findet statt: keine Prüfung, kein Entfall
            var par = u.Karten(Tag, Elementtyp.Lehrer, anderer!).Where(k => k.Von <= x.Bis && k.Bis >= x.Von).ToList();
            Assert.True(par.Count > 0 && par.All(k => k.Status is "regulaer" or "geaendert"), $"{schritt} parallel {anderer}:\n{Info(par)}");
        }
        await Pruefen("vorgemerkt");
        for (var i = 0; i < 2; i++)
        {
            await u.ImportierenAsync(automatisch: i == 1);
            if (altdaten)
            {
                // Import einer älteren Version: der Unterricht der ganzen Klasse entfiel (auch der Parallelkurs) – wird beim Export korrigiert
                var k = Assert.Single(u.D.Klausuren);
                k.UnterrichtId = null;
                k.UnterrichtEntfaellt = true;
                UntPlan.Core.Vertretungsplanung.Vertretungen.Aktualisieren(u.D, Tag);
            }
            await u.ExportierenAsync(nurVertretungen);
            await Pruefen($"Import {i + 1}");
        }
        Assert.Single(u.D.Klausuren);
    }

    /// <summary>Aufsicht durch eine weitere Lehrkraft (zusätzlich zur Lehrkraft der Stunde): ein Block, beide Lehrkräfte darauf.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pruefung_mit_zwei_Aufsichten_ist_ein_Block(bool nurVertretungen)
    {
        await using var u = await Umgebung.StartenAsync();
        var x = Doppelstunde(u, Tag);
        var c = await u.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        var zweite = u.Stand.Paket.Elemente.Lehrer.Select(l => l.Kurz)
            .First(l => l != x.Lehrer && !u.Plan.Karten(u.Stand, Tag, Elementtyp.Lehrer, l).Any(k => k.Von <= x.Bis && k.Bis >= x.Von));
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur", new
        {
            datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, klassen = new[] { x.Klasse }, raeume = new[] { x.Raum }, aufsicht = new[] { x.Lehrer, zweite },
            titel = x.Fach, text = "",
        }));
        async Task Pruefen(string schritt)
        {
            await UeberallEinBlockAsync(u, c, x, x.Raum, schritt);
            EinBlock(u.Karten(Tag, Elementtyp.Lehrer, zweite), x.Von, x.Bis, $"{schritt} Aufsicht {zweite}", x.Raum);
        }
        await Pruefen("vorgemerkt");
        for (var i = 0; i < 2; i++)
        {
            await u.ImportierenAsync(automatisch: i == 1);
            await u.ExportierenAsync(nurVertretungen);
            await Pruefen($"Import {i + 1}");
        }
        Assert.Single(u.D.Klausuren);
    }

    /// <summary>Löschen und Verlegen vor bzw. nach der Übernahme: nie zwei Blöcke, am alten Termin keiner mehr.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Loeschen_und_Verlegen_vor_und_nach_Uebernahme(bool nachUebernahme, bool nurVertretungen)
    {
        await using var u = await Umgebung.StartenAsync();
        var x = Doppelstunde(u, Tag);
        var c = await u.S.AlsAsync(x.Lehrer.ToLowerInvariant(), "lehrer", x.Lehrer);
        await PruefungEintragenAsync(c, x, x.Raum);
        if (nachUebernahme) { await u.ImportierenAsync(); await u.ExportierenAsync(nurVertretungen); }
        await UeberallEinBlockAsync(u, c, x, x.Raum, "eingetragen");

        // verlegen in einen anderen (freien) Raum zur selben Zeit
        var raum2 = await FreierRaum(c, x.Von, x.Bis, x.Raum);
        var alt = new { datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, klassen = new[] { x.Klasse }, titel = x.Fach };
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/verlegen", new
        {
            alt,
            neu = new { datum = Tag.ToString("yyyy-MM-dd"), stundeVon = x.Von, stundeBis = x.Bis, klassen = new[] { x.Klasse }, raeume = new[] { raum2 }, aufsicht = new[] { x.Lehrer }, titel = x.Fach, text = "Klassenarbeit: Kapitel 3" },
        }));
        await UeberallEinBlockAsync(u, c, x, raum2, "verlegt");
        for (var i = 0; i < 2; i++) { await u.ImportierenAsync(automatisch: i == 1); await u.ExportierenAsync(nurVertretungen); }
        await UeberallEinBlockAsync(u, c, x, raum2, "verlegt übernommen");
        Assert.Single(u.D.Klausuren);

        // löschen
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/klausur/loeschen", alt));
        void Keine(string schritt)
        {
            foreach (var (typ, kurz) in new[] { (Elementtyp.Klasse, x.Klasse), (Elementtyp.Lehrer, x.Lehrer), (Elementtyp.Raum, raum2) })
            {
                var karten = u.Karten(Tag, typ, kurz).Where(k => k.Von <= x.Bis && k.Bis >= x.Von).ToList();
                Assert.False(karten.Any(k => k.Status is "klausur" or "entfall" or "vorgemerkt" || Plandienst.IstKlausur(k.Art)), $"{schritt} {typ} {kurz}:\n{Info(karten)}");
            }
        }
        Keine("gelöscht");
        await u.ImportierenAsync();
        await u.ExportierenAsync(nurVertretungen);
        Keine("gelöscht übernommen");
        Assert.Empty(u.D.Klausuren);
    }
}
