using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>
/// Bucht eine Lehrkraft für ihre Stunde einen anderen Raum (vorgemerkte Raumbuchung zum Unterricht), wird der ursprüngliche Raum frei
/// und steht in der Buchung wieder zur Verfügung.
/// </summary>
public class RaumFreiNachBuchungTests
{
    private static Rohstunde Plan(int stunde, int unterricht, params string[] raeume) =>
        new(stunde, unterricht, "M", new[] { "7a" }, new[] { "KLE" }, raeume, "regulaer", null, "", "");

    private static Rohstunde Buchung(int stunde, int unterricht, params string[] raeume) =>
        new(stunde, unterricht, "", new[] { "7a" }, new[] { "KLE" }, raeume, "vorgemerkt", null, "", "Raumbuchung");

    private static bool Belegt(List<Rohstunde> l, int stunde, string raum) =>
        l.Any(r => r.Stunde == stunde && r.Status != "entfall" && r.Raeume.SelectMany(x => x.Split(',')).Contains(raum, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Ursprungsraum_wird_frei_neuer_Raum_belegt()
    {
        var l = Plandienst.MitRaumaenderungen(new[] { Plan(3, 7, "R101"), Plan(4, 7, "R101") }, new[] { Buchung(3, 7, "R205") });
        Assert.False(Belegt(l, 3, "R101"));
        Assert.True(Belegt(l, 3, "R205"));
        // andere Stunde unverändert
        Assert.True(Belegt(l, 4, "R101"));
    }

    [Fact]
    public void Zusaetzlicher_Raum_gibt_nichts_frei()
    {
        var l = Plandienst.MitRaumaenderungen(new[] { Plan(3, 7, "R101") }, new[] { Buchung(3, 7, "R101", "R205") });
        Assert.True(Belegt(l, 3, "R101"));
        Assert.True(Belegt(l, 3, "R205"));
    }

    [Fact]
    public void Anderer_Unterricht_im_selben_Raum_bleibt_belegt()
    {
        var l = Plandienst.MitRaumaenderungen(new[] { Plan(3, 7, "R101"), Plan(3, 8, "R101") }, new[] { Buchung(3, 7, "R205") });
        Assert.True(Belegt(l, 3, "R101"));
    }
}
