using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Lehrstoff je Block: Hat ein Unterricht an einem Tag mehrere getrennte Stunden bzw. Blöcke, erhält jeder seinen eigenen Lehrstoff.
/// Nichts wird aus einer früheren Stunde übernommen – weder beim Anzeigen noch beim Speichern anderer Felder (Notizen, Online).
/// In Blöcken zählt Lehrstoff, der für jede Stunde einzeln eingetragen wurde, als vorhanden.
/// </summary>
public class LehrstoffJeBlockTests
{
    private sealed record Paar(string Datum, string Lehrer, int Unterricht, int ErsteVon, int ZweiteVon);

    /// <summary>Unterricht mit zwei getrennten Stunden bzw. Blöcken am selben Tag (Lücke dazwischen) – Beispielwoche vor „heute“.</summary>
    private static Paar? PaarSuchen(Testserver s)
    {
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        foreach (var datum in new[] { new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13) })
            foreach (var l in stand.Paket.Elemente.Lehrer.Select(x => x.Kurz))
            {
                var karten = plan.Karten(stand, datum, Elementtyp.Lehrer, l).Where(k => k.Unterricht.HasValue && k.Status == "regulaer" && k.Lehrer.Count(w => w.K.Length > 0) == 1).ToList();
                foreach (var g in karten.GroupBy(k => k.Unterricht!.Value))
                {
                    var liste = g.OrderBy(k => k.Von).ToList();
                    for (var i = 1; i < liste.Count; i++)
                        if (liste[i].Von > liste[i - 1].Bis + 1)
                            return new Paar(Plandienst.Iso(datum), l, g.Key, liste[i - 1].Von, liste[i].Von);
                }
            }
        return null;
    }

    [Fact]
    public async Task Spaetere_Stunde_uebernimmt_keinen_Lehrstoff_der_frueheren()
    {
        await using var s = await Testserver.StartenAsync();
        var p = PaarSuchen(s);
        Assert.NotNull(p);
        var lk = await s.AlsAsync("lk-paar", "lehrer", p!.Lehrer);

        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = p.Datum, stunde = p.ErsteVon, unterricht = p.Unterricht, thema = "Thema der ersten Stunde" }));

        // die spätere Stunde zeigt keinen Lehrstoff (vorher: Eintrag der früheren Stunde)
        var zweite = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?datum={p.Datum}&stunde={p.ZweiteVon}&unterricht={p.Unterricht}");
        Assert.Null(zweite["eintrag"]);
        Assert.Null(zweite["blockEintrag"]);

        // nur eine Notiz speichern (wie die Apps: Lehrstoff nicht mitgeschickt) – kein Lehrstoff wird übernommen
        var gespeichert = await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = p.Datum, stunde = p.ZweiteVon, unterricht = p.Unterricht, notizSchueler = "Atlas mitbringen" }));
        Assert.Equal("", gespeichert["thema"]!.GetValue<string>());
        Assert.Equal(p.ZweiteVon, gespeichert["stunde"]!.GetValue<int>());

        // „Findet online statt“ der späteren Stunde ändert nicht den Eintrag der früheren
        Assert.Equal(HttpStatusCode.OK, (await lk.PutAsJsonAsync("/api/app/klassenbuch/online", new { datum = p.Datum, stunde = p.ZweiteVon, unterricht = p.Unterricht, online = true })).StatusCode);
        var erste = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?datum={p.Datum}&stunde={p.ErsteVon}&unterricht={p.Unterricht}");
        Assert.Equal("Thema der ersten Stunde", erste["eintrag"]!["thema"]!.GetValue<string>());
        Assert.False(erste["eintrag"]!["online"]!.GetValue<bool>());
        zweite = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?datum={p.Datum}&stunde={p.ZweiteVon}&unterricht={p.Unterricht}");
        Assert.Equal("", zweite["eintrag"]!["thema"]!.GetValue<string>());
        Assert.True(zweite["eintrag"]!["online"]!.GetValue<bool>());

        // Datenbank: jede Stunde hat ihren eigenen Eintrag, der Lehrstoff steht nur bei der ersten
        var tag = DateOnly.Parse(p.Datum);
        var eintraege = s.Dienst<Klassenbuchdienst>().Zeitraum(tag, tag).Where(e => e.Unterricht == p.Unterricht).ToList();
        Assert.Equal("Thema der ersten Stunde", eintraege.Single(e => e.Stunde == p.ErsteVon).Thema);
        Assert.Equal("", eintraege.Single(e => e.Stunde == p.ZweiteVon).Thema);

        // offene Stunden: die spätere Stunde ist weiter ohne Lehrstoff
        var offen = s.Dienst<Klassenbuchdienst>().OffeneStunden(s.Dienst<Paketdienst>().Aktuell!, p.Lehrer, 30);
        Assert.Contains(offen, o => System.Text.Json.JsonSerializer.SerializeToNode(o)!["Von"]!.GetValue<int>() == p.ZweiteVon
                                  && System.Text.Json.JsonSerializer.SerializeToNode(o)!["Datum"]!.GetValue<string>() == p.Datum);
    }

    [Fact]
    public async Task Lehrstoff_jeder_Einzelstunde_zaehlt_in_den_offenen_Stunden()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var plan = s.Dienst<Plandienst>();
        var tag = new DateOnly(2026, 10, 12);
        var (lehrer, block) = stand.Paket.Elemente.Lehrer.Select(x => x.Kurz)
            .Select(l => (l, k: plan.Karten(stand, tag, Elementtyp.Lehrer, l).FirstOrDefault(k => k.Unterricht.HasValue && k.Status == "regulaer" && k.Bis > k.Von && k.Lehrer.Count(w => w.K.Length > 0) == 1)))
            .First(x => x.k is not null);
        var lk = await s.AlsAsync("lk-einzeln", "lehrer", lehrer);
        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/offene-stunden/zeitbereich", new { bereich = "individuell", von = Plandienst.Iso(tag), bis = Plandienst.Iso(tag) }));
        async Task<bool> Lehrstoff() => (await Testserver.HolenAsync(lk, "/api/app/klassenbuch/offene-stunden?art=alle"))["zeilen"]!.AsArray()
            .Single(z => z!["datum"]!.GetValue<string>() == Plandienst.Iso(tag) && z["von"]!.GetValue<int>() == block!.Von && z["unterricht"]!.GetValue<int>() == block.Unterricht)!["lehrstoff"]!.GetValue<bool>();
        Assert.False(await Lehrstoff());

        // nur die erste Stunde des Blocks: noch offen
        await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = Plandienst.Iso(tag), stunde = block!.Von, unterricht = block.Unterricht, thema = "Teil 1", einzelstunde = block.Von }));
        Assert.False(await Lehrstoff());
        // jede Stunde einzeln eingetragen: vorhanden
        for (var n = block.Von + 1; n <= block.Bis; n++)
            await Testserver.JsonAsync(await lk.PutAsJsonAsync("/api/app/klassenbuch/stunde", new { datum = Plandienst.Iso(tag), stunde = block.Von, unterricht = block.Unterricht, thema = $"Teil {n}", einzelstunde = n }));
        Assert.True(await Lehrstoff());

        // die Einzelstunden behalten ihren eigenen Lehrstoff, der Block hat keinen
        var blockDaten = await Testserver.HolenAsync(lk, $"/api/app/klassenbuch/stunde?datum={Plandienst.Iso(tag)}&stunde={block.Von}&unterricht={block.Unterricht}");
        Assert.Null(blockDaten["eintrag"]);
        Assert.Equal(block.Bis - block.Von + 1, blockDaten["einzelEintraege"]!.AsArray().Count);
        Assert.Equal("Teil 1", blockDaten["einzelEintraege"]!.AsArray()[0]!["thema"]!.GetValue<string>());
    }
}
