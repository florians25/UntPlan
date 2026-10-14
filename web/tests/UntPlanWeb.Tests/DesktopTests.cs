using System.Net.Http.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Tests;

/// <summary>Zusammenspiel mit UntPlan (Desktop): Paket hochladen, Rückmeldungen abholen, übernehmen und quittieren.</summary>
public class DesktopTests
{
    private static PortalEinstellungen Verbindung(Testserver s) => new()
    {
        Adresse = s.Adresse.ToString(), SchulId = Testserver.SchulId, ApiSchluessel = Testserver.ApiSchluessel,
    };

    [Fact]
    public async Task Export_aus_UntPlan_und_Rueckmeldungen_zurueck()
    {
        await using var s = await Testserver.StartenAsync(mitPaket: false);
        var d = Demo.Beispielschule();
        var p = Verbindung(s);

        Assert.False(string.IsNullOrWhiteSpace(await UntPlanWebExport.VerbindungPruefenAsync(p)));
        var paket = UntPlanWebExport.PaketErstellen(d, new DateOnly(2026, 10, 12), new WebExportOptionen { TageVoraus = 7, LehrerAbsenzen = true });
        var antwort = await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 3 });
        Assert.True(antwort.Ok, antwort.Meldung);
        Assert.Equal(d.Klassen.Count, s.Dienst<Paketdienst>().Aktuell!.Paket.Elemente.Klassen.Count);

        // Lehrkraft meldet in UntPlanWeb eine Abwesenheit und bucht einen Raum
        var lehrer = d.Lehrer[0];
        var c = await s.AlsAsync(lehrer.Kurzname.ToLowerInvariant(), "lehrer", lehrer.Kurzname);
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/abwesenheit", new { von = "2026-10-15", bis = "2026-10-15", grund = "Fortbildung" }));
        var raum = d.Raeume[0].Kurzname;
        await Testserver.JsonAsync(await c.PostAsJsonAsync("/api/app/meldungen/raumbuchung", new { datum = "2026-10-16", stundeVon = 5, stundeBis = 6, raum, titel = "Projekt" }));

        // UntPlan holt ab und übernimmt
        var liste = await UntPlanWebExport.RueckmeldungenAsync(p);
        Assert.Equal(2, liste.Count);
        var vorher = d.Abwesenheiten.Count;
        var (uebernommen, abgelehnt) = UntPlanWebExport.Uebernehmen(d, liste);
        Assert.Empty(abgelehnt);
        Assert.Equal(2, uebernommen.Count);
        Assert.Contains(d.Abwesenheiten.Skip(vorher), a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == lehrer.Id && a.Von == new DateOnly(2026, 10, 15));
        await UntPlanWebExport.QuittierenAsync(p, uebernommen);

        Assert.Empty(await UntPlanWebExport.RueckmeldungenAsync(p));
        var meine = await Testserver.HolenAsync(c, "/api/app/meldungen");
        Assert.All(meine.AsArray(), m => Assert.Equal("quittiert", m!["status"]!.GetValue<string>()));

        // Export mit veralteter Revision (z. B. zweite Arbeitsstation): UntPlan sendet automatisch mit der nächsten Revision
        var erneut = await UntPlanWebExport.HochladenAsync(p, paket with { Revision = 2 });
        Assert.True(erneut.Ok);
        Assert.Equal(4, erneut.Revision);
    }
}
