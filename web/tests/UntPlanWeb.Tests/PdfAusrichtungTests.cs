using System.Net.Http.Json;
using System.Text;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Druckbare Formulare im Hoch- bzw. Querformat (Parameter <c>ausrichtung</c>, Vorgabe je Formular).</summary>
public class PdfAusrichtungTests
{
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);
    private static bool Hoch(byte[] pdf) => Text(pdf).Contains("/MediaBox [0 0 595");
    private static bool Quer(byte[] pdf) => Text(pdf).Contains("/MediaBox [0 0 841");

    [Fact]
    public void Bericht_und_Sitzplan_hoch_und_quer()
    {
        var zeilen = Enumerable.Range(1, 80).Select(i => (IReadOnlyList<string>)new[] { $"Zeile {i}", "x" }).ToList();
        var quer = PdfBericht.Erzeugen("Schule", "Titel", "", new[] { "A", "B" }, zeilen, null, null, false);
        var hoch = PdfBericht.Erzeugen("Schule", "Titel", "", new[] { "A", "B" }, zeilen, null, null, false, hochformat: true);
        Assert.True(Quer(quer));
        Assert.True(Hoch(hoch));
        Assert.False(Quer(hoch));
        // im Hochformat passen mehr Zeilen auf eine Seite
        int Seiten(byte[] p) => int.Parse(System.Text.RegularExpressions.Regex.Match(Text(p), @"/Count (\d+)").Groups[1].Value);
        Assert.True(Seiten(hoch) < Seiten(quer));
        // danach wieder Querformat (Ausrichtung gilt nur für die eine Datei)
        Assert.True(Quer(PdfBericht.Erzeugen("Schule", "Titel", "", new[] { "A" }, new List<IReadOnlyList<string>>(), null, null, false)));
        var sitz = PdfBericht.Sitzplan("Schule", "Sitzplan", "", 3, 4, false, new List<(int, int, string?, string?, bool)> { (1, 1, "Anna", null, true) }, new List<string>(), null, true);
        Assert.True(Hoch(sitz));
        Assert.True(PdfBericht.Hochformat("hoch", false));
        Assert.False(PdfBericht.Hochformat("quer", true));
        Assert.True(PdfBericht.Hochformat(null, true));
    }

    [Fact]
    public async Task Endpunkte_mit_Ausrichtung_und_Vorgabe()
    {
        await using var s = await Testserver.StartenAsync();
        var admin = await s.AlsAsync("admin");
        var lan = await s.AlsAsync("lan", "lehrer", "LAN");
        var id = (await Testserver.JsonAsync(await admin.PostAsJsonAsync("/api/app/sprechtag", new
        {
            name = "Sprechtag", datum = "2026-10-21", beginn = "15:00", ende = "16:00", dauer = 10, pausen = Array.Empty<object>(),
            anmeldungVon = "2026-10-10T08:00", anmeldungBis = "2026-10-20T18:00", sichtbar = true, raumwahl = false, hinweis = "",
            lehrkraefte = new object[] { new { lehrer = "LAN", raum = "" } },
        })))["id"]!.GetValue<int>();
        // Terminplan der Lehrkraft: Vorgabe hoch; Gesamtplan der Administration: Vorgabe quer; jeweils umschaltbar
        Assert.True(Hoch(await lan.GetByteArrayAsync($"/api/app/sprechtag/{id}/plan.pdf?leer=true")));
        Assert.True(Quer(await lan.GetByteArrayAsync($"/api/app/sprechtag/{id}/plan.pdf?leer=true&ausrichtung=quer")));
        Assert.True(Quer(await admin.GetByteArrayAsync($"/api/app/sprechtag/{id}/plan.pdf")));
        Assert.True(Hoch(await admin.GetByteArrayAsync($"/api/app/sprechtag/{id}/plan.pdf?ausrichtung=hoch")));
        // Berichte: Vorgabe quer, hoch auf Wunsch
        var art = (await Testserver.HolenAsync(lan, "/api/app/berichte/liste")).AsArray().Select(x => x!["id"]!.GetValue<string>()).First(a => a != "entschuldigungsschreiben");
        var r = await lan.GetAsync($"/api/app/berichte/erstellen/{art}?klasse=7a&format=pdf&ausrichtung=hoch");
        if (r.IsSuccessStatusCode) Assert.True(Hoch(await r.Content.ReadAsByteArrayAsync()));
        // Zugangsdaten: Vorgabe hoch
        var z = await admin.PostAsJsonAsync("/api/app/admin/personen/zugangsdaten.pdf", new { konten = new[] { new { name = "A", klasse = "7a", zuordnung = "", rolle = "schueler", login = "a", passwort = "p" } }, titel = "Zugang" });
        Assert.True(Hoch(await z.Content.ReadAsByteArrayAsync()));
        z = await admin.PostAsJsonAsync("/api/app/admin/personen/zugangsdaten.pdf", new { konten = new[] { new { name = "A", klasse = "7a", zuordnung = "", rolle = "schueler", login = "a", passwort = "p" } }, titel = "Zugang", ausrichtung = "quer" });
        Assert.True(Quer(await z.Content.ReadAsByteArrayAsync()));
    }
}
