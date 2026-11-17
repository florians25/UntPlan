using System.Net;
using System.Net.Mail;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>E-Mail im Postausgang.</summary>
public sealed record AusgangsMail(int Id, string An, string Betreff, string Text, string Erstellt, string Status, string Fehler);

/// <summary>
/// Versand von E-Mails (Kennwort vergessen, Selbstregistrierung). Jede E-Mail landet im Postausgang (Tabelle mail_ausgang, für die
/// Administration einsehbar). Ist ein Mailserver eingerichtet (Umgebungsvariablen UNTPLANWEB_SMTP_HOST, _PORT, _BENUTZER, _PASSWORT,
/// _ABSENDER, _SSL), wird sie im Hintergrund versendet; sonst bleibt sie mit dem Status „kein Mailserver“ im Postausgang.
/// </summary>
public sealed class Maildienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly ILogger<Maildienst> _log;
    private readonly string? _host;
    private readonly int _port;
    private readonly string? _benutzer, _passwort;
    private readonly string _absender;
    private readonly bool _ssl;

    public Maildienst(Db db, Uhr uhr, IConfiguration c, ILogger<Maildienst> log)
    {
        _db = db;
        _uhr = uhr;
        _log = log;
        _host = Leer(c["UNTPLANWEB_SMTP_HOST"]);
        _port = int.TryParse(c["UNTPLANWEB_SMTP_PORT"], out var p) ? p : 587;
        _benutzer = Leer(c["UNTPLANWEB_SMTP_BENUTZER"]);
        _passwort = Leer(c["UNTPLANWEB_SMTP_PASSWORT"]);
        _absender = Leer(c["UNTPLANWEB_SMTP_ABSENDER"]) ?? _benutzer ?? "untplanweb@localhost";
        _ssl = (c["UNTPLANWEB_SMTP_SSL"] ?? "1") is not ("0" or "false" or "nein");
    }

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Ist ein Mailserver eingerichtet?</summary>
    public bool Eingerichtet => _host != null;

    /// <summary>Gültige E-Mail-Adresse (einfache Prüfung)?</summary>
    public static bool Gueltig(string? adresse) => !string.IsNullOrWhiteSpace(adresse) && MailAddress.TryCreate(adresse.Trim(), out _);

    /// <summary>E-Mail in den Postausgang legen und (falls eingerichtet) im Hintergrund versenden.</summary>
    public int Senden(string an, string betreff, string text)
    {
        var id = (int)_db.Einfuegen("INSERT INTO mail_ausgang (an, betreff, text, erstellt, status) VALUES (@a, @b, @t, @e, @s)",
            Db.P("@a", an.Trim()), Db.P("@b", betreff), Db.P("@t", text), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")),
            Db.P("@s", Eingerichtet ? "offen" : "kein Mailserver"));
        if (Eingerichtet) _ = Task.Run(() => Versenden(id, an.Trim(), betreff, text));
        return id;
    }

    private void Versenden(int id, string an, string betreff, string text)
    {
        try
        {
            using var smtp = new SmtpClient(_host!, _port) { EnableSsl = _ssl };
            if (_benutzer != null) smtp.Credentials = new NetworkCredential(_benutzer, _passwort ?? "");
            using var m = new MailMessage(_absender, an, betreff, text) { BodyEncoding = System.Text.Encoding.UTF8, SubjectEncoding = System.Text.Encoding.UTF8 };
            smtp.Send(m);
            _db.Ausfuehren("UPDATE mail_ausgang SET status = 'gesendet' WHERE id = @id", Db.P("@id", id));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "E-Mail {Id} konnte nicht versendet werden", id);
            _db.Ausfuehren("UPDATE mail_ausgang SET status = 'fehler', fehler = @f WHERE id = @id", Db.P("@f", ex.Message.Length > 500 ? ex.Message[..500] : ex.Message), Db.P("@id", id));
        }
    }

    /// <summary>Postausgang (neueste zuerst).</summary>
    public List<AusgangsMail> Ausgang(int anzahl = 200) => _db.Liste("SELECT * FROM mail_ausgang ORDER BY id DESC LIMIT @n",
        r => new AusgangsMail(r.Zahl("id"), r.Text("an"), r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Text("status"), r.Text("fehler")), Db.P("@n", anzahl));
}
