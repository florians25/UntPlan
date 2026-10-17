using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Sofort-Benachrichtigungen der iOS-App:
/// <list type="bullet">
/// <item><c>POST /api/app/push/geraet</c> <c>{ token, plattform: "ios", umgebung: "sandbox"|"production", bundle }</c>: Gerätetoken zum angemeldeten Konto.</item>
/// <item><c>DELETE /api/app/push/geraet?token=…</c>: Token vom angemeldeten Konto abmelden (z. B. beim Abmelden in der App).</item>
/// <item><c>GET/PUT /api/app/admin/push</c>: APNs-Zugang (Administration): .p8-Inhalt, Key-ID, Team-ID, Topic (Standard de.untplan.mobile).</item>
/// </list>
/// </summary>
public static class PushApi
{
    public sealed record GeraetDaten(string? Token, string? Plattform, string? Umgebung, string? Bundle);
    /// <summary>schluessel: Inhalt der .p8-Datei; null = unverändert, "" = entfernen.</summary>
    public sealed record PushEinstellungDaten(string? Schluessel, string? KeyId, string? TeamId, string? Topic);

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();

    private static Benutzer Admin(HttpContext c)
    {
        var b = Ich(c);
        return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.");
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        api.MapPost("/push/geraet", (GeraetDaten d, HttpContext c, Pushdienst push) =>
        {
            push.GeraetSpeichern(Ich(c).Id, d.Token ?? "", d.Plattform, d.Umgebung, d.Bundle);
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/push/geraet", (string? token, HttpContext c, Pushdienst push) =>
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Gerätetoken fehlt.");
            return Results.Json(new { ok = true, entfernt = push.GeraetLoeschen(Ich(c).Id, token) });
        });

        api.MapGet("/admin/push", (HttpContext c, Pushdienst push) =>
        {
            Admin(c);
            var k = push.Konfiguration();
            return Results.Json(new { konfiguriert = k.Vollstaendig, schluesselHinterlegt = k.Schluessel.Length > 0, keyId = k.KeyId, teamId = k.TeamId, topic = k.Topic, geraete = push.Geraete().Count });
        });
        api.MapPut("/admin/push", (PushEinstellungDaten d, HttpContext c, Pushdienst push, Protokoll prot) =>
        {
            var b = Admin(c);
            push.KonfigurationSpeichern(d.Schluessel, d.KeyId, d.TeamId, d.Topic);
            prot.Schreiben(b.Login, "Sofort-Benachrichtigungen eingerichtet", d.Schluessel is null ? "" : d.Schluessel.Length == 0 ? "Schlüssel entfernt" : "Schlüssel hinterlegt");
            return Results.Json(new { ok = true, konfiguriert = push.Konfiguration().Vollstaendig });
        });
    }
}
