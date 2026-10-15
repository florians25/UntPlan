using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Versionsanzeige und Updateprüfung von UntPlanMobile (UM-01) – dieselbe Logik wie in UntPlanWeb (Updatepruefung.cs).
/// <c>GET /api/version</c> liefert die laufende Version und – falls aktiviert – die neueste veröffentlichte Version.
/// UntPlanMobile speichert keine Daten (reiner Proxy zu UntPlanWeb); ein Update ist daher nur ein neues Image:
/// <c>docker compose pull &amp;&amp; docker compose up -d</c>, angestoßen auch aus der Administration von UntPlanWeb (Watchtower).
/// Umgebungsvariablen: <c>UNTPLANMOBILE_UPDATE_PRUEFEN</c> (0 = aus), <c>UNTPLANMOBILE_UPDATE_URL</c>, <c>UNTPLANMOBILE_UPDATE_TAG_PRAEFIX</c>.
/// </summary>
public static class VersionApi
{
    public static Updatepruefung PruefungErstellen(IServiceProvider sp, IConfiguration c) => new(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("update"),
        typeof(VersionApi).Assembly,
        c["UNTPLANMOBILE_UPDATE_URL"],
        Updatepruefung.Schalter(c["UNTPLANMOBILE_UPDATE_PRUEFEN"], true),
        c["UNTPLANMOBILE_UPDATE_TAG_PRAEFIX"]);

    public static void Abbilden(WebApplication app)
    {
        app.MapGet("/api/version", async (Updatepruefung p, CancellationToken ct) =>
        {
            var s = await p.StandAsync(false, ct);
            return Results.Json(new
            {
                produkt = "UntPlanMobile",
                version = s.Version,
                pruefungAktiv = s.PruefungAktiv,
                neueste = s.Neueste,
                updateVerfuegbar = s.UpdateVerfuegbar,
                hinweisurl = s.Hinweisurl,
                befehl = "docker compose pull && docker compose up -d",
            });
        });
    }
}
