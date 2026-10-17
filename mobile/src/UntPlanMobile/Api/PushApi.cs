using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Api;

/// <summary>
/// Web-Push für Änderungen im eigenen Plan (<c>/api/push</c>):
/// <list type="bullet">
/// <item><c>GET ?action=status</c>: öffentlicher VAPID-Schlüssel, ob das angemeldete Profil Benachrichtigungen bekommt und ob das Gerät ein Abo hat.</item>
/// <item><c>POST action=subscribe, subscription=&lt;PushSubscription als JSON&gt;</c>: Abo des Geräts hinterlegen und Benachrichtigungen für das
/// angemeldete Profil einschalten. <c>action=off</c>: für das Profil ausschalten. <c>action=unsubscribe, endpoint=…</c>: Abo löschen.</item>
/// <item><c>POST action=test</c>: Probenachricht an alle Abos des Geräts.</item>
/// </list>
/// </summary>
public static class PushApi
{
    private static IResult Fehler(string meldung, int status = 400) => Results.Json(new { ok = false, error = meldung }, statusCode: status);

    public static void Abbilden(WebApplication app)
    {
        app.MapMethods("/api/push", new[] { "GET", "POST" }, async (HttpContext c, UntPlanWebVerbindung web, Kontodienst konten, Kontenspeicher speicher, Vapid vapid, Pushwache wache) =>
        {
            var p = await Parameter.LesenAsync(c);
            var aktion = p.Text("action", "status");
            if (aktion != "status" && HttpMethods.IsGet(c.Request.Method)) return Fehler("post_required", 405);
            var k = await konten.LadenAsync(c);
            var sitzung = UntPlanWebVerbindung.Sitzung(c);
            var geraet = Kontenspeicher.GeraetVon(c, true)!;
            var ich = speicher.MitSitzung(geraet, sitzung);
            if (ich is null)
            {
                await MobilApi.ProfilMerkenAsync(c, web, speicher);
                ich = speicher.MitSitzung(geraet, UntPlanWebVerbindung.Sitzung(c));
            }
            switch (aktion)
            {
                case "status":
                    return Results.Json(new { ok = true, key = vapid.Oeffentlich, on = ich?.Push == true, subscribed = speicher.Abos(geraet).Count > 0, ownPlan = k.Kuerzel.Length > 0 });
                case "subscribe":
                {
                    PushAbo abo;
                    try
                    {
                        var j = JsonNode.Parse(p["subscription"]) as JsonObject ?? throw new FormatException();
                        abo = new PushAbo(j["endpoint"]!.GetValue<string>(), j["keys"]!["p256dh"]!.GetValue<string>(), j["keys"]!["auth"]!.GetValue<string>());
                        if (!Uri.TryCreate(abo.Endpoint, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) throw new FormatException();
                        if (WebPush.AusB64(abo.P256dh).Length != 65 || WebPush.AusB64(abo.Auth).Length != 16) throw new FormatException();
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException or NullReferenceException or InvalidOperationException)
                    {
                        return Fehler("Ungültiges Push-Abonnement.");
                    }
                    if (ich is null) return Fehler("Profil nicht gefunden – bitte neu anmelden.", 409);
                    speicher.AboSetzen(geraet, abo);
                    speicher.PushSetzen(geraet, ich.Id, true);
                    return Results.Json(new { ok = true, on = true });
                }
                case "off":
                    if (ich != null) speicher.PushSetzen(geraet, ich.Id, false);
                    // Bekommt kein Profil des Geräts mehr Nachrichten, kann der Browser sein Abo kündigen.
                    return Results.Json(new { ok = true, on = false, others = speicher.Konten(geraet).Any(x => x.Push) });
                case "unsubscribe":
                    if (p["endpoint"].Length > 0 && speicher.Abos(geraet).Any(a => a.Endpoint == p["endpoint"])) speicher.AboEntfernen(p["endpoint"]);
                    return Results.Json(new { ok = true });
                case "test":
                {
                    var abos = speicher.Abos(geraet);
                    if (abos.Count == 0) return Fehler("Auf diesem Gerät sind keine Benachrichtigungen eingerichtet.");
                    var n = await wache.SendenAsync(abos, JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        title = "Benachrichtigungen eingeschaltet", body = "Bei Änderungen in Ihrem Stundenplan erhalten Sie hier eine Nachricht.", tag = "planwacht-test", url = "./",
                    }), c.RequestAborted);
                    return Results.Json(new { ok = n > 0, sent = n, error = n > 0 ? null : "Die Nachricht konnte nicht zugestellt werden." });
                }
                default:
                    return Fehler("unknown_action");
            }
        });
    }
}
