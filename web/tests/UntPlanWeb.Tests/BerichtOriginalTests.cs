using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Klassenbuch-Berichte als PDF im Aussehen des Originals: Serifenschrift, rechts oben Schule und „Schuljahr : 2026/2027“, Titel, Zeitraum,
/// Kopfblock je Schüler*in mit Seitenumbruch (außer „ohne Seitenumbrüche“), Spaltengruppen („Fehlstunden“ über „Schulst“/„Std.“),
/// senkrechte Spaltenköpfe in der Fehlstundenmatrix und die Einstellungen des Auswahldialogs.
/// </summary>
public class BerichtOriginalTests
{
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);
    private static int Seiten(byte[] pdf) => int.Parse(Regex.Match(Text(pdf), @"/Count (\d+)").Groups[1].Value);

    [Fact]
    public void Original_mit_Schuljahr_Spaltengruppe_Kopfblock_und_Seitenumbruch()
    {
        var zeilen = new List<IReadOnlyList<string>>
        {
            new[] { "Schüler*innen\tAnna Muster\nText\t\nKlasse\t7a", "", "" },
            new[] { "Mo. 12.10.26", "1", "0:45" },
            new[] { "Summe", "1", "0:45" },
            new[] { "Schüler*innen\tBen Beispiel\nText\t\nKlasse\t7a", "", "" },
            new[] { "Di. 13.10.26", "1", "0:45" },
        };
        var arten = new[] { "block", "", "zwischensumme", "block", "" };
        var d = new Berichtsdarstellung { Gruppen = new() { "", "Fehlstunden", "Fehlstunden" }, Seitenumbruch = true };
        var pdf = PdfBericht.Original("Schule", "2026/2027", "Fehlzeiten pro Schüler*in", "12.10.2026 - 13.10.2026", new[] { "Datum", "Schulst", "Std." }, zeilen, arten, d, true);
        var t = Text(pdf);
        Assert.Contains("/Times-Roman", t);
        Assert.Contains("(Schuljahr : 2026/2027)", t);
        Assert.Contains("(Fehlstunden)", t);
        Assert.Contains("(Anna Muster)", t);
        Assert.DoesNotContain("Dienstgebrauch", t);
        Assert.Equal(2, Seiten(pdf));
        // „ohne Seitenumbrüche“: beide Schüler*innen auf einer Seite
        Assert.Equal(1, Seiten(PdfBericht.Original("Schule", "2026/2027", "T", "", new[] { "Datum", "Schulst", "Std." }, zeilen, arten, d, true, ohneSeitenumbrueche: true)));
        // senkrechte Spaltenköpfe (Fehlstundenmatrix): gedrehte Textmatrix
        var m = PdfBericht.Original("Schule", "", "Fehlstundenmatrix", "", new[] { "Schüler*innen", "Klasse", "12.10. 07:55" },
            new List<IReadOnlyList<string>> { new[] { "Anna Muster", "7a", "A" } }, null, new Berichtsdarstellung { SenkrechtAb = 2 }, false);
        Assert.Contains("0 -1 1 0", Text(m));
        // danach wieder Helvetica für die übrigen Formulare
        Assert.Contains("/Helvetica", Text(PdfBericht.Erzeugen("Schule", "Titel", "", new[] { "A" }, new List<IReadOnlyList<string>>(), null, null, false)));
    }

    [Fact]
    public async Task Auswahldialog_hat_die_Einstellungen_des_Originals_und_PDF_im_Originalkopf()
    {
        await using var s = await Testserver.StartenAsync();
        var stand = s.Dienst<Paketdienst>().Aktuell!;
        var leitung = await s.AlsAsync("leitung", "leitung", "");
        var liste = (await Testserver.HolenAsync(leitung, "/api/app/berichte/liste")).AsArray();
        List<string> Optionen(string id) => liste.First(x => x!["id"]!.GetValue<string>() == id)!["optionen"]!.AsArray().Select(o => o!["titel"]!.GetValue<string>()).ToList();
        Assert.Contains("ohne Seitenumbrüche", Optionen("fehlzeiten_schueler"));
        Assert.Contains("Klassenfilter aktivieren", Optionen("fehlzeiten_klasse"));
        Assert.Contains("Fehltage und -zeiten trennen", Optionen("fehlzeiten_klasse"));
        Assert.Contains("Ganze Tage", Optionen("fehltage_klasse"));
        Assert.Contains("Status", Optionen("fehlzeiten_fach"));
        Assert.Equal(new[] { "Ausgabe", "Gruppierung pro Woche", "Entschuldigte Stunden anzeigen" }, Optionen("entschuldigungsschreiben"));
        Assert.Contains("ohne Seitenumbrüche", Optionen("tagesuebersicht"));
        Assert.Contains("Eintragungskategorie", Optionen("klassenbucheintraege"));
        Assert.Equal("länger als {n1} Tage", liste.First(x => x!["id"]!.GetValue<string>() == "unentschuldigt")!["parameter"]!.GetValue<string>());

        var kind = stand.Paket.Elemente.Schueler.First(x => x.Klasse == "7a");
        await Testserver.JsonAsync(await leitung.PostAsJsonAsync("/api/app/abwesenheiten", new { schuelerId = kind.Id, von = "2026-10-12", bis = "2026-10-12", grund = "K" }));
        var r = await leitung.GetAsync("/api/app/berichte/erstellen/fehlzeiten_schueler?klasse=7a&von=2026-10-12&bis=2026-10-14&format=pdf");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = Text(await r.Content.ReadAsByteArrayAsync());
        Assert.Contains("/Times-Roman", t);
        Assert.Contains("(Schuljahr : ", t);
        Assert.Contains("(12.10.2026 - 14.10.2026)", t);
        Assert.Contains("(Fehlstunden)", t);
        Assert.Contains("(Klasse)", t);
        // Entschuldigungsschreiben mit Gruppierung pro Woche: ein Schreiben je Schüler*in und Woche; mit entschuldigten Stunden
        var e = await Testserver.HolenAsync(leitung, $"/api/app/berichte/erstellen/entschuldigungsschreiben?klasse=7a&schueler={kind.Id}&von=2026-10-12&bis=2026-10-14&woche=1&ausgabe=pro_tag");
        var z = e["zeilen"]!.AsArray();
        Assert.Single(z);
        Assert.Contains("Woche 12.10.", z[0]![0]!.GetValue<string>());
        // Schüler*innen im Unterricht (Berichte zum Unterricht)
        Assert.Contains(liste, x => x!["id"]!.GetValue<string>() == "schueler_unterricht" && x["unterricht"]!.GetValue<bool>());
    }
}
