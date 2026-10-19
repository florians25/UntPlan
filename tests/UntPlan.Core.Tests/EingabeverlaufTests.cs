using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Zuletzt verwendete Eingaben je Feldart (Autovervollständigung in Textfeldern).</summary>
public class EingabeverlaufTests
{
    [Fact]
    public void NeuesteZuerstOhneDoppelte()
    {
        var v = new Dictionary<string, List<string>>();
        Assert.True(Eingabeverlauf.Merken(v, "Text", "Aufgaben im Buch"));
        Assert.True(Eingabeverlauf.Merken(v, "Text", "Raumwechsel"));
        Assert.True(Eingabeverlauf.Merken(v, "Text", "  aufgaben im buch "));
        Assert.Equal(new[] { "aufgaben im buch", "Raumwechsel" }, v["Text"]);
        Assert.False(Eingabeverlauf.Merken(v, "Text", "aufgaben im buch"));
        Assert.False(Eingabeverlauf.Merken(v, "Text", "   "));
        Assert.False(Eingabeverlauf.Merken(v, "Text", null));
    }

    [Fact]
    public void HoechstensDreissigEintraegeJeFeld()
    {
        var v = new Dictionary<string, List<string>>();
        for (var i = 0; i < 40; i++) Eingabeverlauf.Merken(v, "Bemerkung", "Eintrag " + i);
        Eingabeverlauf.Merken(v, "Text", "anderes Feld");
        Assert.Equal(Eingabeverlauf.Maximum, v["Bemerkung"].Count);
        Assert.Equal("Eintrag 39", v["Bemerkung"][0]);
        Assert.DoesNotContain("Eintrag 9", v["Bemerkung"]);
        Assert.Single(v["Text"]);
    }

    [Fact]
    public void ZuLangeTexteWerdenNichtGemerkt()
    {
        var v = new Dictionary<string, List<string>>();
        Assert.False(Eingabeverlauf.Merken(v, "Text", new string('x', Eingabeverlauf.MaxLaenge + 1)));
        Assert.False(v.ContainsKey("Text"));
    }

    [Fact]
    public void VorschlaegeAnfangVorEnthaltenOhneGrossKlein()
    {
        var liste = new List<string> { "Klassenfahrt 7b", "Fahrt zum Museum", "Raumwechsel", "fahrt abgesagt", "Fahrt" };
        Assert.Equal(new[] { "Fahrt zum Museum", "fahrt abgesagt", "Klassenfahrt 7b" }, Eingabeverlauf.Vorschlaege(liste, "FAHRT"));
        Assert.Equal(new[] { "Raumwechsel" }, Eingabeverlauf.Vorschlaege(liste, "wechs"));
        Assert.Empty(Eingabeverlauf.Vorschlaege(liste, "xyz"));
        Assert.Equal(liste.Take(3), Eingabeverlauf.Vorschlaege(liste, "", 3));
        Assert.Empty(Eingabeverlauf.Vorschlaege((IEnumerable<string>?)null, "a"));
    }

    [Fact]
    public void EntfernenUndFeldzugriff()
    {
        var v = new Dictionary<string, List<string>>();
        Eingabeverlauf.Merken(v, "Text", "A1");
        Eingabeverlauf.Merken(v, "Text", "B2");
        Assert.Equal(new[] { "A1" }, Eingabeverlauf.Vorschlaege(v, "Text", "a"));
        Assert.Empty(Eingabeverlauf.Vorschlaege(v, "Unbekannt", "a"));
        Assert.True(Eingabeverlauf.Entfernen(v, "Text", "a1"));
        Assert.Equal(new[] { "B2" }, v["Text"]);
    }
}
