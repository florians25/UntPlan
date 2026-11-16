using System.IO.Compression;
using System.Text;
using UntPlan.Core.Austausch;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Export nach Excel: echte .xlsx-Arbeitsmappe und Zeilen aus HTML-Berichten.</summary>
public class ExcelExportTests
{
    private static Dictionary<string, string> Teile(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open(), Encoding.UTF8); return r.ReadToEnd(); });
    }

    [Fact]
    public void ArbeitsmappeIstGueltigesXlsxMitZahlenUndText()
    {
        var bytes = Excel.Arbeitsmappe("Abrechnung", new[] { new[] { "Datum", "Wert", "Fach" }, new[] { "2.3.", "-1,5", "Deutsch & Kunst" }, new[] { "1a", "0.000", "<x>" } });
        Assert.Equal((byte)'P', bytes[0]); // ZIP
        var teile = Teile(bytes);
        Assert.Contains("[Content_Types].xml", teile.Keys);
        Assert.Contains("xl/workbook.xml", teile.Keys);
        Assert.Contains("xl/styles.xml", teile.Keys);
        Assert.Contains("name=\"Abrechnung\"", teile["xl/workbook.xml"]);
        var blatt = teile["xl/worksheets/sheet1.xml"];
        Assert.Contains("<c r=\"B2\"><v>-1.5</v></c>", blatt);
        Assert.Contains("<c r=\"B3\"><v>0.000</v></c>", blatt);
        Assert.Contains("Deutsch &amp; Kunst", blatt);
        Assert.Contains("&lt;x&gt;", blatt);
        Assert.Contains("<c r=\"A1\" s=\"1\" t=\"inlineStr\">", blatt); // Kopfzeile fett
        Assert.Contains(">1a<", blatt); // Kürzel bleibt Text
        // wohlgeformtes XML
        foreach (var t in teile.Values) System.Xml.Linq.XDocument.Parse(t);
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("+3", 3)]
    [InlineData("-1,5", -1.5)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("2.5", 2.5)]
    [InlineData("0", 0)]
    public void ZahlenWerdenErkannt(string text, double erwartet) => Assert.Equal((decimal)erwartet, Excel.Zahl(text));

    [Theory]
    [InlineData("1a")]
    [InlineData("05")]
    [InlineData("2.3.")]
    [InlineData("Mo/1")]
    [InlineData("")]
    [InlineData("12:30")]
    public void TexteBleibenText(string text) => Assert.Null(Excel.Zahl(text));

    [Fact]
    public void SpaltenbuchstabenWieInExcel()
    {
        Assert.Equal("A", Excel.Spalte(0));
        Assert.Equal("Z", Excel.Spalte(25));
        Assert.Equal("AA", Excel.Spalte(26));
        Assert.Equal("AZ", Excel.Spalte(51));
    }

    [Fact]
    public void BlattnamenWerdenBereinigtUndEindeutig()
    {
        var bytes = Excel.Arbeitsmappe(new[] { new Excel.Blatt("A/B", Array.Empty<Excel.Zeile>()), new Excel.Blatt("AB", Array.Empty<Excel.Zeile>()) });
        var wb = Teile(bytes)["xl/workbook.xml"];
        Assert.Contains("name=\"AB\"", wb);
        Assert.Contains("name=\"AB 2\"", wb);
    }

    [Fact]
    public void HtmlBerichtWirdZuZeilen()
    {
        const string html = "<html><head><title>X</title><style>td{}</style></head><body>" +
            "<div><span class=\"kn\">Arist</span><span class=\"ln\">Aristoteles</span></div>" +
            "<div class=\"b\">Vertretungen / Entfälle : 1.3. - 31.3.</div>" +
            "<table class=\"r\"><tr><th>Datum</th><th>Wert</th><th>Fach</th></tr>" +
            "<tr><td>2.3.</td><td class=\"z\">-1</td><td>Deutsch&nbsp;LK</td></tr>" +
            "<tr class=\"s\"><td colspan=\"2\">Summe</td><td>3</td></tr></table>" +
            "<div class=\"bil\">Vertretungen: 4<br>Entfälle: 1</div><div>normal</div></body></html>";
        var z = Excel.AusHtml(html);
        Assert.Equal(new[] { "Arist Aristoteles" }, z[0].Zellen);
        Assert.True(z[0].Fett);
        Assert.Equal("Vertretungen / Entfälle : 1.3. - 31.3.", z[1].Zellen[0]);
        Assert.True(z[1].Fett);
        Assert.Equal(new[] { "Datum", "Wert", "Fach" }, z[2].Zellen);
        Assert.True(z[2].Fett);
        Assert.Equal(new[] { "2.3.", "-1", "Deutsch LK" }, z[3].Zellen);
        Assert.False(z[3].Fett);
        Assert.Equal(new[] { "Summe", "", "3" }, z[4].Zellen);
        Assert.True(z[4].Fett);
        Assert.Equal("Vertretungen: 4", z[5].Zellen[0]);
        Assert.True(z[5].Fett);
        Assert.Equal("Entfälle: 1", z[6].Zellen[0]);
        Assert.True(z[6].Fett);
        Assert.Equal("normal", z[7].Zellen[0]);
        Assert.False(z[7].Fett);
        Assert.Equal(8, z.Count);
    }
}
