using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UntPlanMobile.Dienste;

/// <summary>Push-Abonnement eines Browsers (PushSubscription: endpoint, keys.p256dh, keys.auth – Base64url).</summary>
public sealed record PushAbo(string Endpoint, string P256dh, string Auth);

/// <summary>Ergebnis eines Versands: zugestellt, abgelaufen (Abo löschen) oder Fehler.</summary>
public enum PushErgebnis { Zugestellt, Abgelaufen, Fehler }

/// <summary>
/// Web-Push ohne Fremdbibliothek, nur mit .NET-Kryptografie:
/// <list type="bullet">
/// <item>Verschlüsselung der Nachricht nach RFC 8291 (ECDH P-256, HKDF-SHA-256, AES-128-GCM, Inhaltskodierung „aes128gcm“ nach RFC 8188).</item>
/// <item>Anmeldung beim Push-Dienst mit VAPID nach RFC 8292 (JWT mit ES256, Kopf <c>Authorization: vapid t=…, k=…</c>).</item>
/// </list>
/// Funktioniert mit den Push-Diensten von Chrome/Edge, Firefox und Safari (auch der Homescreen-App auf iPhone und iPad ab iOS 16.4).
/// </summary>
public static class WebPush
{
    public static string B64(ReadOnlySpan<byte> d) => Convert.ToBase64String(d).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] AusB64(string s)
    {
        s = (s ?? "").Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>Öffentlicher Schlüssel als unkomprimierter Punkt (65 Byte: 0x04 || X || Y).</summary>
    public static byte[] Punkt(ECParameters p)
    {
        var r = new byte[65];
        r[0] = 4;
        p.Q.X!.CopyTo(r, 1);
        p.Q.Y!.CopyTo(r, 33);
        return r;
    }

    public static ECParameters AusPunkt(byte[] punkt, byte[]? privat = null)
    {
        if (punkt.Length != 65 || punkt[0] != 4) throw new FormatException("Ungültiger öffentlicher Schlüssel.");
        return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = punkt[1..33], Y = punkt[33..] }, D = privat };
    }

    /// <summary>
    /// Nachricht für ein Abo verschlüsseln (RFC 8291, ein Datensatz). <paramref name="sender"/> und <paramref name="salz"/> nur für Tests
    /// vorgeben – sonst werden für jede Nachricht ein neues Schlüsselpaar und ein neues Salz erzeugt.
    /// </summary>
    public static byte[] Verschluesseln(byte[] klartext, byte[] uaPublic, byte[] authSecret, ECDiffieHellman? sender = null, byte[]? salz = null, int datensatz = 4096)
    {
        if (authSecret.Length != 16) throw new FormatException("Ungültiges Auth-Geheimnis.");
        using var eigen = sender is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var asKey = sender ?? eigen!;
        var asPublic = Punkt(asKey.ExportParameters(false));
        using var ua = ECDiffieHellman.Create(AusPunkt(uaPublic));
        var ecdh = asKey.DeriveRawSecretAgreement(ua.PublicKey);

        // IKM = HKDF(auth_secret, ecdh_secret, "WebPush: info" || 0x00 || ua_public || as_public, 32)
        var info = Verbinden(Encoding.ASCII.GetBytes("WebPush: info\0"), uaPublic, asPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, authSecret, info);
        salz ??= RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salz, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salz, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // ein einziger (letzter) Datensatz: Klartext || 0x02, ohne Auffüllung
        var block = new byte[klartext.Length + 1];
        klartext.CopyTo(block, 0);
        block[^1] = 2;
        if (block.Length + 16 > datensatz) throw new ArgumentException("Nachricht zu lang für einen Datensatz.");
        var geheim = new byte[block.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, block, geheim, tag);

        // Kopf: salt (16) || rs (4, big endian) || idlen (1) || keyid (as_public)
        var kopf = new byte[16 + 4 + 1 + asPublic.Length];
        salz.CopyTo(kopf, 0);
        BinaryPrimitives.WriteUInt32BigEndian(kopf.AsSpan(16), (uint)datensatz);
        kopf[20] = (byte)asPublic.Length;
        asPublic.CopyTo(kopf, 21);
        return Verbinden(kopf, geheim, tag);
    }

    /// <summary>Gegenstück zu <see cref="Verschluesseln"/> (so entschlüsselt der Browser) – für Tests.</summary>
    public static byte[] Entschluesseln(byte[] nachricht, ECDiffieHellman ua, byte[] authSecret)
    {
        var salz = nachricht[..16];
        var idlen = nachricht[20];
        var asPublic = nachricht[21..(21 + idlen)];
        var geheim = nachricht[(21 + idlen)..];
        using var sender = ECDiffieHellman.Create(AusPunkt(asPublic));
        var ecdh = ua.DeriveRawSecretAgreement(sender.PublicKey);
        var uaPublic = Punkt(ua.ExportParameters(false));
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, authSecret, Verbinden(Encoding.ASCII.GetBytes("WebPush: info\0"), uaPublic, asPublic));
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salz, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salz, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var klar = new byte[geheim.Length - 16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Decrypt(nonce, geheim[..^16], geheim[^16..], klar);
        var ende = Array.LastIndexOf(klar, (byte)2);
        if (ende < 0) throw new CryptographicException("Kein Datensatzende.");
        return klar[..ende];
    }

    /// <summary>VAPID-JWT (ES256) für den Push-Dienst: aud = Herkunft des Endpunkts, exp höchstens 24 Stunden, sub = Kontakt.</summary>
    public static string VapidJwt(ECDsa schluessel, string endpoint, string kontakt, DateTimeOffset jetzt)
    {
        var aud = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var kopf = B64(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var inhalt = B64(JsonSerializer.SerializeToUtf8Bytes(new { aud, exp = jetzt.AddHours(12).ToUnixTimeSeconds(), sub = kontakt }));
        var signatur = schluessel.SignData(Encoding.ASCII.GetBytes($"{kopf}.{inhalt}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{kopf}.{inhalt}.{B64(signatur)}";
    }

    /// <summary>Anfrage an den Push-Dienst (verschlüsselter Inhalt, VAPID, TTL und Dringlichkeit).</summary>
    public static HttpRequestMessage Anfrage(PushAbo abo, byte[] klartext, ECDsa vapid, string kontakt, DateTimeOffset jetzt, int ttlSekunden = 86400)
    {
        var inhalt = Verschluesseln(klartext, AusB64(abo.P256dh), AusB64(abo.Auth));
        var a = new HttpRequestMessage(HttpMethod.Post, abo.Endpoint) { Content = new ByteArrayContent(inhalt) };
        a.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        a.Content.Headers.ContentEncoding.Add("aes128gcm");
        a.Headers.TryAddWithoutValidation("TTL", ttlSekunden.ToString(System.Globalization.CultureInfo.InvariantCulture));
        a.Headers.TryAddWithoutValidation("Urgency", "high");
        a.Headers.TryAddWithoutValidation("Topic", "planaenderung");
        a.Headers.TryAddWithoutValidation("Authorization", $"vapid t={VapidJwt(vapid, abo.Endpoint, kontakt, jetzt)}, k={B64(Punkt(vapid.ExportParameters(false)))}");
        return a;
    }

    public static async Task<PushErgebnis> SendenAsync(HttpClient http, PushAbo abo, byte[] klartext, ECDsa vapid, string kontakt, CancellationToken ct)
    {
        try
        {
            using var a = Anfrage(abo, klartext, vapid, kontakt, DateTimeOffset.UtcNow);
            using var r = await http.SendAsync(a, ct);
            if (r.IsSuccessStatusCode) return PushErgebnis.Zugestellt;
            return r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ? PushErgebnis.Abgelaufen : PushErgebnis.Fehler;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or CryptographicException or UriFormatException)
        {
            return PushErgebnis.Fehler;
        }
    }

    private static byte[] Verbinden(params byte[][] teile)
    {
        var r = new byte[teile.Sum(t => t.Length)];
        var i = 0;
        foreach (var t in teile) { t.CopyTo(r, i); i += t.Length; }
        return r;
    }
}
