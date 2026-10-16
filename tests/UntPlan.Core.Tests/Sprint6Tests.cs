using System.Text.Json;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Sprint 6: Stundenplanformat wie im Vorbild (Auswahl (Bereich), Layout 1/2, HTML, Grundformen, Stundenplan-Stunde).</summary>
public class Sprint6Tests
{
    [Theory]
    [InlineData("3,5", new[] { 3, 5 })]
    [InlineData("3-5", new[] { 3, 4, 5 })]
    [InlineData(" 1; 7 ", new[] { 1, 7 })]
    [InlineData("", new int[0])]
    [InlineData("x,2", new[] { 2 })]
    public void StundenListe_liest_Vorbild_Schreibweisen(string text, int[] erwartet) =>
        Assert.Equal(erwartet.OrderBy(x => x), StundenplanFormat.StundenListe(text).OrderBy(x => x));

    [Fact]
    public void Neue_Formatoptionen_ueberstehen_Speichern_und_Laden()
    {
        var f = new StundenplanFormat
        {
            Name = "Kla1", Grundform = Grundform.Format10, VonStunde = 2, BisStunde = 6, VonTag = 1, BisTag = 3,
            Zeitraum = FormatZeitraum.EinTag, DatumVon = new DateOnly(2021, 8, 30), DickerStrichVor = "5", TrennstrichTage = true,
            FreieStunden = new FeldGestaltung { Raeumlich = Raeumlich.Versenkt, Grau = true }, FixierungMitStern = false,
            NamensArt = NamensArt.Langname, Felder = { new StundenFeld { Art = StundenFeldArt.Lehrer, Kursiv = true, Ausrichtung = 0 } },
        };
        var json = JsonSerializer.Serialize(f, Dokument.JsonOptionen);
        var z = JsonSerializer.Deserialize<StundenplanFormat>(json, Dokument.JsonOptionen)!;
        Assert.Equal(Grundform.Format10, z.Grundform);
        Assert.Equal((2, 6, 1, 3), (z.VonStunde, z.BisStunde, z.VonTag, z.BisTag));
        Assert.Equal(FormatZeitraum.EinTag, z.Zeitraum);
        Assert.Equal(new DateOnly(2021, 8, 30), z.DatumVon);
        Assert.Equal(Raeumlich.Versenkt, z.FreieStunden.Raeumlich);
        Assert.True(z.FreieStunden.Grau && z.TrennstrichTage && !z.FixierungMitStern);
        Assert.Equal(NamensArt.Langname, z.NamensArt);
        Assert.True(z.Felder[0].Kursiv);
        Assert.Equal("1Lehrer", z.Felder[0].Beschriftung);
    }

    [Fact]
    public void Aeltere_Formate_ohne_neue_Felder_behalten_die_Vorbild_Vorgaben()
    {
        var z = JsonSerializer.Deserialize<StundenplanFormat>("""{"Name":"Alt","Schriftgroesse":9}""", Dokument.JsonOptionen)!;
        Assert.Equal(Grundform.Format01, z.Grundform);
        Assert.Equal((1, 0, 0, -1), (z.VonStunde, z.BisStunde, z.VonTag, z.BisTag));
        Assert.True(z.KopplungenMitPunkt && z.FixierungMitStern && z.EntfaelleDurchstreichen && z.Pausenbezeichnung);
        Assert.True(z.Ueberschrift.Grau && !z.FreieStunden.Grau);
        Assert.Equal(Raeumlich.Flach, z.BesetzteStunden.Raeumlich);
    }

    [Fact]
    public void Grundform_Titel_wie_im_Vorbild_Dialog()
    {
        Assert.Equal("Format 01 - Einzelstundenplan Tage waagrecht, Stunden senkrecht", StundenplanFormat.GrundformText(Grundform.Format01));
        Assert.StartsWith("Format 10 - ", StundenplanFormat.GrundformText(Grundform.Format10));
    }
}

/// <summary>Sprint 6: Aufbereitung der Druckzeilen nach dem Seitenlayout (Form).</summary>
public class DruckaufbereitungTests
{
    [Theory]
    [InlineData("5a,5b", "5ab")]
    [InlineData("5a,6a,5c", "5ac,6a")]
    [InlineData("EF,Q1", "EF,Q1")]
    [InlineData("10a", "10a")]
    [InlineData("", "")]
    public void Kombinierte_Klassen(string ein, string aus) => Assert.Equal(aus, Austausch.Druckaufbereitung.KlassenKombinieren(ein));

    [Fact]
    public void Stunden_werden_zusammengefasst() => Assert.Equal("1-3, 5", Austausch.Druckaufbereitung.StundenText(new[] { 3, 1, 2, 5 }));

    [Fact]
    public void Blockvertretungen_zusammenfassen()
    {
        var zeilen = new List<(int? S, string K, string T)> { (1, "5a", "D"), (2, "5a", "D"), (3, "5a", "M"), (4, "6a", "M"), (5, "6a", "M"), (null, "x", "y") };
        var erg = Austausch.Druckaufbereitung.Zusammenfassen(zeilen, z => z.S, z => z.K + z.T, (z, s) => (z.S, z.K, z.T + "@" + s));
        Assert.Equal(new[] { "D@1-2", "M", "M@4-5", "y" }, erg.Select(z => z.T));
    }
}
