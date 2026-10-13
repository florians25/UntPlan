using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Portal;
using Xunit;
using PortalClient = UntPlan.Core.Austausch.Portal;

namespace UntPlan.Core.Tests;

/// <summary>Ende-zu-Ende: Desktop-Client (UntPlan.Core) gegen den Portal-Referenzserver.</summary>
public class PortalTests
{
    [Fact]
    public async Task HochladenMeldenUebernehmenQuittieren()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "untplan_portal_" + Guid.NewGuid().ToString("N"));
        var app = PortalServer.Erstellen(new[] { "--urls", "http://127.0.0.1:0" }, o =>
        {
            o.Datenordner = ordner;
            o.Schulen = new() { new SchulKonfiguration { Id = "199999", Name = "Testschule", ApiSchluessel = "geheim", MeldeSchluessel = "1234" } };
        });
        await app.StartAsync();
        try
        {
            var adresse = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var d = Testdaten.Verplant();
            var tag = Testdaten.Unterrichtstag(d);
            d.Einstellungen.Portal = new PortalEinstellungen { Adresse = adresse, SchulId = "199999", ApiSchluessel = "geheim" };

            Assert.Contains("Version", await PortalClient.StatusAsync(d.Einstellungen.Portal));

            // Falscher Schlüssel wird abgelehnt
            var falsch = new PortalEinstellungen { Adresse = adresse, SchulId = "199999", ApiSchluessel = "x" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => PortalClient.HochladenAsync(falsch, PortalClient.PaketErstellen(d, tag)));

            var a1 = await PortalClient.HochladenAsync(d.Einstellungen.Portal, PortalClient.PaketErstellen(d, tag));
            var a2 = await PortalClient.HochladenAsync(d.Einstellungen.Portal, PortalClient.PaketErstellen(d, tag));
            Assert.True(a1.Ok);
            Assert.Equal(a1.Revision + 1, a2.Revision);

            // Lehrkraft meldet sich krank
            using var http = new HttpClient { BaseAddress = new Uri(adresse) };
            var l = d.Lehrer[0];
            var r = await http.PostAsJsonAsync("/api/v1/schulen/199999/meldungen", new { pin = "1234", kuerzel = l.Kurzname, von = tag.ToString("yyyy-MM-dd"), grund = "Krankheit" });
            r.EnsureSuccessStatusCode();
            var falschePin = await http.PostAsJsonAsync("/api/v1/schulen/199999/meldungen", new { pin = "0", kuerzel = l.Kurzname, von = tag.ToString("yyyy-MM-dd") });
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, falschePin.StatusCode);

            var meldungen = await PortalClient.MeldungenAsync(d.Einstellungen.Portal);
            Assert.Single(meldungen);
            var (ok, _) = PortalClient.MeldungenUebernehmen(d, meldungen);
            Assert.Single(ok);
            Assert.Contains(d.Abwesenheiten, x => x.ElementId == l.Id && x.Von == tag);
            await PortalClient.QuittierenAsync(d.Einstellungen.Portal, ok);
            Assert.Empty(await PortalClient.MeldungenAsync(d.Einstellungen.Portal));

            // Öffentliche Seite
            var seite = await http.GetStringAsync("/199999/?klasse=5a");
            Assert.Contains("Stundenpläne", seite);
            Assert.Contains("5a", seite);
        }
        finally
        {
            await app.StopAsync();
            Directory.Delete(ordner, true);
        }
    }
}
