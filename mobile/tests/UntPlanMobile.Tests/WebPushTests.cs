using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>Web-Push: Verschlüsselung nach RFC 8291 (aes128gcm), VAPID-JWT nach RFC 8292, Planvergleich für die Benachrichtigung.</summary>
public class WebPushTests
{
    // Beispiel aus RFC 8291, Abschnitt 5 / Anhang A
    private const string Klartext = "When I grow up, I want to be a watermelon";
    private const string AsPrivat = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string AsOeffentlich = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string UaPrivat = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string UaOeffentlich = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Salz = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string Ergebnis = "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    private static ECDiffieHellman Schluessel(string oeffentlich, string privat) =>
        ECDiffieHellman.Create(WebPush.AusPunkt(WebPush.AusB64(oeffentlich), WebPush.AusB64(privat)));

    [Fact]
    public void Verschluesselung_entspricht_dem_Beispiel_aus_RFC_8291()
    {
        using var sender = Schluessel(AsOeffentlich, AsPrivat);
        var nachricht = WebPush.Verschluesseln(Encoding.UTF8.GetBytes(Klartext), WebPush.AusB64(UaOeffentlich), WebPush.AusB64(Auth), sender, WebPush.AusB64(Salz));
        Assert.Equal(Ergebnis, WebPush.B64(nachricht));
        // Kopf: Salz, Datensatzgröße 4096, Schlüssellänge 65, Schlüssel des Absenders
        Assert.Equal(WebPush.AusB64(Salz), nachricht[..16]);
        Assert.Equal(new byte[] { 0, 0, 0x10, 0 }, nachricht[16..20]);
        Assert.Equal(65, nachricht[20]);
        Assert.Equal(WebPush.AusB64(AsOeffentlich), nachricht[21..86]);
    }

    [Fact]
    public void Verschluesselung_laesst_sich_vom_Browser_entschluesseln()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        var text = "{\"title\":\"Änderung im Stundenplan\",\"body\":\"Mi. 14.10. 3. Std.: Vertretung E 7b R101\"}";
        var a = WebPush.Verschluesseln(Encoding.UTF8.GetBytes(text), WebPush.Punkt(ua.ExportParameters(false)), auth);
        var b = WebPush.Verschluesseln(Encoding.UTF8.GetBytes(text), WebPush.Punkt(ua.ExportParameters(false)), auth);
        // jede Nachricht mit neuem Salz und neuem Absenderschlüssel
        Assert.NotEqual(a, b);
        Assert.Equal(text, Encoding.UTF8.GetString(WebPush.Entschluesseln(a, ua, auth)));
        Assert.Equal(text, Encoding.UTF8.GetString(WebPush.Entschluesseln(b, ua, auth)));
        // RFC-Beispiel rückwärts
        using var uaRfc = Schluessel(UaOeffentlich, UaPrivat);
        Assert.Equal(Klartext, Encoding.UTF8.GetString(WebPush.Entschluesseln(WebPush.AusB64(Ergebnis), uaRfc, WebPush.AusB64(Auth))));
        // falsches Auth-Geheimnis: Entschlüsselung scheitert
        Assert.ThrowsAny<CryptographicException>(() => WebPush.Entschluesseln(a, ua, RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public void Vapid_JWT_und_Anfrage()
    {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jetzt = new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
        var jwt = WebPush.VapidJwt(vapid, "https://web.push.apple.com/QGuQyavXutnMH8Ix", "mailto:schule@example.org", jetzt);
        var teile = jwt.Split('.');
        Assert.Equal(3, teile.Length);
        Assert.Equal("ES256", JsonNode.Parse(WebPush.AusB64(teile[0]))!["alg"]!.GetValue<string>());
        var inhalt = JsonNode.Parse(WebPush.AusB64(teile[1]))!;
        Assert.Equal("https://web.push.apple.com", inhalt["aud"]!.GetValue<string>());
        Assert.Equal("mailto:schule@example.org", inhalt["sub"]!.GetValue<string>());
        Assert.Equal(jetzt.AddHours(12).ToUnixTimeSeconds(), inhalt["exp"]!.GetValue<long>());
        var signatur = WebPush.AusB64(teile[2]);
        Assert.Equal(64, signatur.Length);
        Assert.True(vapid.VerifyData(Encoding.ASCII.GetBytes(teile[0] + "." + teile[1]), signatur, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var abo = new PushAbo("https://fcm.googleapis.com/fcm/send/abc", WebPush.B64(WebPush.Punkt(ua.ExportParameters(false))), WebPush.B64(RandomNumberGenerator.GetBytes(16)));
        using var anfrage = WebPush.Anfrage(abo, Encoding.UTF8.GetBytes("{}"), vapid, "mailto:x@example.org", jetzt);
        Assert.Equal("aes128gcm", Assert.Single(anfrage.Content!.Headers.ContentEncoding));
        var kopf = anfrage.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("vapid t=", kopf);
        Assert.EndsWith(", k=" + WebPush.B64(WebPush.Punkt(vapid.ExportParameters(false))), kopf);
        Assert.Equal("86400", anfrage.Headers.GetValues("TTL").Single());
    }

    private static Kachel K(int stunde, string art, string fach, string klasse, string raum, string datum = "2026-10-15") =>
        new() { Stunde = stunde, Art = art, Fach = fach, Klasse = klasse, Raum = raum, Datum = datum, Lehrkraft = "SNE" };

    [Fact]
    public void Planvergleich_meldet_nur_relevante_Aenderungen_ab_heute()
    {
        var heute = new DateOnly(2026, 10, 14);
        var alt = new Dictionary<string, List<Kachel>>
        {
            ["2026-10-13"] = new() { K(1, "normal", "E", "5a", "R101", "2026-10-13") },
            ["2026-10-15"] = new() { K(1, "normal", "E", "5a", "R101"), K(3, "normal", "M", "7b", "R102") },
        };
        var neu = new Dictionary<string, List<Kachel>>
        {
            ["2026-10-13"] = new() { K(1, "cancelled", "E", "5a", "R101", "2026-10-13") },
            ["2026-10-15"] = new() { K(1, "normal", "E", "5a", "R101"), K(3, "change", "M", "7b", "R205") },
        };
        neu["2026-10-15"][0].Hausaufgabe = true; // Hausaufgaben lösen nichts aus
        var zeilen = Planvergleich.Vergleich(Planvergleich.Stand(alt, heute), Planvergleich.Stand(neu, heute), heute);
        Assert.Equal(new[] { "Do. 15.10. 3. Std.: Vertretung M 7b R205" }, zeilen);
        // Stunde fällt ganz weg
        neu["2026-10-15"].RemoveAt(1);
        zeilen = Planvergleich.Vergleich(Planvergleich.Stand(alt, heute), Planvergleich.Stand(neu, heute), heute);
        Assert.Equal(new[] { "Do. 15.10. 3. Std.: M 7b R102 nicht mehr im Plan" }, zeilen);
        // gleicher Stand: nichts
        Assert.Empty(Planvergleich.Vergleich(Planvergleich.Stand(alt, heute), Planvergleich.Stand(alt, heute), heute));
        // Nachricht für den Service Worker
        var j = JsonNode.Parse(Planvergleich.Nachricht(new() { "a", "b", "c", "d", "e" }, "Julia Schneider"))!;
        Assert.Equal("5 Änderungen im Stundenplan · Julia Schneider", j["title"]!.GetValue<string>());
        Assert.EndsWith("… und 1 weitere", j["body"]!.GetValue<string>());
        // am Wochenende zählen die beiden kommenden Wochen
        Assert.Equal(new[] { new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26) }, Planvergleich.Wochen(new DateOnly(2026, 10, 17)));
        Assert.Equal(new[] { new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19) }, Planvergleich.Wochen(heute));
    }
}
