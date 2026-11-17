using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Kärtchen auf dem Kennwort-Blatt (Anmeldeblatt).</summary>
public sealed record Anmeldekarte(string Name, string Klasse, string Login, string Passwort);

/// <summary>
/// Leitfaden Klassenleitung, Kapitel 6 – Schüler*innen, Erziehungsberechtigten und Ausbildungsbetrieben Zugang geben:
/// <list type="bullet">
/// <item>Erstanmeldung über ein Kennwort-Blatt: je Klasse Schülerkonten anlegen bzw. neue Startkennwörter vergeben, sechs Kärtchen je DIN-A4-Seite.</item>
/// <item>„Kennwort vergessen“: Benutzername und hinterlegte E-Mail-Adresse → E-Mail mit Link → nach dem Anklicken E-Mail mit neuem Kennwort.</item>
/// <item>Selbstregistrierung der Erziehungsberechtigten und Ausbildungsbetriebe über die bei den Schüler*innen hinterlegte E-Mail-Adresse.</item>
/// </list>
/// </summary>
public sealed class Zugangsdienst
{
    public const string ArtPasswort = "passwort", ArtRegistrierung = "registrierung";

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Benutzerdienst _benutzer;
    private readonly Maildienst _mail;
    private readonly Einstellungen _einstellungen;
    private readonly Paketdienst _pakete;
    private readonly Schuelerkontakte _kontakte;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);

    public Zugangsdienst(Db db, Uhr uhr, Benutzerdienst benutzer, Maildienst mail, Einstellungen einstellungen, Paketdienst pakete, Schuelerkontakte kontakte)
    {
        _db = db; _uhr = uhr; _benutzer = benutzer; _mail = mail; _einstellungen = einstellungen; _pakete = pakete; _kontakte = kontakte;
    }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");

    private string Schule => _einstellungen.Schulname.Length > 0 ? _einstellungen.Schulname : "Ihre Schule";

    private string TokenAnlegen(string art, int? benutzerId, string email, TimeSpan gueltig)
    {
        var t = Sicherheit.Zufall(40);
        _db.Ausfuehren("INSERT INTO zugang_token (token, art, benutzer_id, email, erstellt, gueltig_bis) VALUES (@t, @a, @b, @e, @c, @g)",
            Db.P("@t", t), Db.P("@a", art), Db.P("@b", benutzerId), Db.P("@e", email), Db.P("@c", Jetzt), Db.P("@g", _uhr.Jetzt.Add(gueltig).ToString("yyyy-MM-dd HH:mm:ss")));
        return t;
    }

    private (int? BenutzerId, string Email)? TokenPruefen(string art, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var liste = _db.Liste("SELECT * FROM zugang_token WHERE token = @t AND art = @a", r => (B: r["benutzer_id"] is DBNull or null ? (int?)null : Convert.ToInt32(r["benutzer_id"]),
            E: r.Text("email"), G: r.Text("gueltig_bis"), U: r.Wahr("benutzt")), Db.P("@t", token.Trim()), Db.P("@a", art));
        if (liste.Count == 0 || liste[0].U || string.CompareOrdinal(liste[0].G, Jetzt) < 0) return null;
        return (liste[0].B, liste[0].E);
    }

    private void TokenVerbrauchen(string token) => _db.Ausfuehren("UPDATE zugang_token SET benutzt = 1 WHERE token = @t", Db.P("@t", token.Trim()));

    // ------------------------------------------------------------------------------------------ Kennwort vergessen

    /// <summary>E-Mail-Adressen eines Kontos: im Profil hinterlegt bzw. bei Schülerkonten die E-Mail-Adresse der Schülerin/des Schülers.</summary>
    private List<string> Adressen(Benutzer b)
    {
        var res = new List<string>();
        if (b.Email.Length > 0) res.Add(b.Email);
        if (b.Rolle == Rollen.Schueler)
            foreach (var id in b.SchuelerIds)
                if (_db.Wert("SELECT email FROM schueler_daten WHERE id = @id", Db.P("@id", id)) is string m && m.Length > 0) res.Add(m);
        return res;
    }

    /// <summary>
    /// „Kennwort vergessen“: Stimmen Benutzername und E-Mail-Adresse überein, erhält das Konto eine E-Mail mit einem Link (24 Stunden gültig).
    /// Die Antwort verrät nicht, ob es das Konto gibt.
    /// </summary>
    public void KennwortVergessen(string? login, string? email, string adresse)
    {
        var l = (login ?? "").Trim();
        var m = (email ?? "").Trim();
        if (l.Length == 0 || m.Length == 0) throw new ArgumentException("Bitte Benutzername und E-Mail-Adresse eingeben.");
        var b = _benutzer.NachLogin(l) ?? _benutzer.Alle().FirstOrDefault(x => Ci.Equals(x.Login, l));
        if (b is null || b.Gesperrt || !Adressen(b).Contains(m, Ci)) return;
        var t = TokenAnlegen(ArtPasswort, b.Id, m, TimeSpan.FromHours(24));
        _mail.Senden(m, $"{Schule}: Kennwort zurücksetzen",
            $"Hallo {(b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login)},\n\n" +
            $"für Ihr Konto „{b.Login}“ bei UntPlanWeb wurde ein neues Kennwort angefordert. Bitte öffnen Sie den folgenden Link, um das Kennwort zurückzusetzen:\n\n" +
            $"{adresse}/#/kennwort/{t}\n\nDer Link ist 24 Stunden gültig. Anschließend erhalten Sie eine weitere E-Mail mit dem neuen Kennwort.\n" +
            "Haben Sie kein neues Kennwort angefordert, können Sie diese E-Mail ignorieren.\n\n" + Schule);
    }

    /// <summary>Link aus der E-Mail angeklickt: neues Kennwort vergeben (muss bei der Anmeldung geändert werden) und per E-Mail senden.</summary>
    public string KennwortZuruecksetzen(string? token, string adresse)
    {
        var z = TokenPruefen(ArtPasswort, token) ?? throw new ArgumentException("Der Link ist ungültig oder abgelaufen. Bitte fordern Sie über „Kennwort vergessen“ einen neuen an.");
        var b = z.BenutzerId is int id ? _benutzer.Holen(id) : null;
        if (b is null || b.Gesperrt) throw new ArgumentException("Der Link ist ungültig oder abgelaufen.");
        var pw = Sicherheit.Zufall(10);
        _benutzer.PasswortSetzen(b.Id, pw, true);
        TokenVerbrauchen(token!);
        _mail.Senden(z.Email, $"{Schule}: Ihr neues Kennwort",
            $"Hallo {(b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login)},\n\nIhr Kennwort wurde zurückgesetzt. Sie können sich jetzt mit folgenden Daten anmelden:\n\n" +
            $"Benutzername: {b.Login}\nKennwort: {pw}\n\n{adresse}\n\nBitte ändern Sie das Kennwort nach der Anmeldung.\n\n" + Schule);
        return b.Login;
    }

    // ------------------------------------------------------------------------------------------ Selbstregistrierung

    /// <summary>Kontakte (Erziehungsberechtigte, Ausbildungsbeauftragte) mit dieser E-Mail-Adresse: Schüler-Id, Rolle, Name, Betrieb.</summary>
    private List<(int Schueler, string Rolle, string Name, string Betrieb)> KontakteMit(string email) =>
        _db.Liste("SELECT schueler_id, rolle, name, betrieb, email FROM schueler_kontakte WHERE rolle <> 'eigene'",
            r => (S: r.Zahl("schueler_id"), R: r.Text("rolle"), N: r.Text("name"), B: r.Text("betrieb"), E: r.Text("email")))
            .Where(x => Ci.Equals(x.E.Trim(), email.Trim())).Select(x => (x.S, x.R, x.N, x.B))
            // Erziehungsberechtigte aus „Personen & Konten“ (Schülerimport) zählen ebenso
            .Concat(_kontakte.Erziehungsberechtigte().Where(p => Ci.Equals(p.Email.Trim(), email.Trim()))
                .SelectMany(p => p.Kinder.Select(k => (k, Schuelerkontakte.Erziehungsberechtigt, p.Name, ""))))
            .ToList();

    /// <summary>Person der Personenliste mit dieser E-Mail-Adresse (für die Zuordnung des neuen Kontos).</summary>
    private int? PersonMit(string email) => _kontakte.Erziehungsberechtigte().Where(p => Ci.Equals(p.Email.Trim(), email.Trim())).Select(p => (int?)p.Id).FirstOrDefault();

    /// <summary>
    /// Selbstregistrierung: Ist die E-Mail-Adresse bei einer Schülerin/einem Schüler als Erziehungsberechtigte*r bzw. Ausbildungsbeauftragte*r
    /// hinterlegt, erhält sie einen Link zur Registrierung (48 Stunden gültig). Die Antwort verrät nicht, ob die Adresse bekannt ist.
    /// </summary>
    public void RegistrierungAnfordern(string? email, string adresse)
    {
        var m = (email ?? "").Trim();
        if (!Maildienst.Gueltig(m)) throw new ArgumentException("Bitte eine gültige E-Mail-Adresse eingeben.");
        if (KontakteMit(m).Count == 0) return;
        if (_benutzer.Alle().FirstOrDefault(b => b.Rolle == Rollen.Eltern && Ci.Equals(b.Email, m)) is { } konto)
        {
            _mail.Senden(m, $"{Schule}: Registrierung bei UntPlanWeb",
                $"Guten Tag,\n\nfür diese E-Mail-Adresse gibt es bei UntPlanWeb bereits das Konto „{konto.Login}“. Haben Sie das Kennwort vergessen, " +
                $"wählen Sie auf der Anmeldeseite „Passwort vergessen?“:\n\n{adresse}\n\n" + Schule);
            return;
        }
        var t = TokenAnlegen(ArtRegistrierung, null, m, TimeSpan.FromHours(48));
        _mail.Senden(m, $"{Schule}: Registrierung bei UntPlanWeb",
            $"Guten Tag,\n\nSie können sich bei UntPlanWeb der Schule {Schule} registrieren und dann Stundenplan, Abwesenheiten und Mitteilungen einsehen. " +
            $"Bitte öffnen Sie dazu den folgenden Link und wählen Sie einen Benutzernamen und ein Kennwort:\n\n{adresse}/#/registrieren/{t}\n\n" +
            "Der Link ist 48 Stunden gültig. Haben Sie keine Registrierung angefordert, können Sie diese E-Mail ignorieren.\n\n" + Schule);
    }

    /// <summary>Angaben zum Registrierungs-Link: E-Mail-Adresse und zugeordnete Schüler*innen.</summary>
    public object Registrierung(string? token)
    {
        var z = TokenPruefen(ArtRegistrierung, token) ?? throw new ArgumentException("Der Link ist ungültig oder abgelaufen. Bitte registrieren Sie sich erneut.");
        var s = _pakete.Aktuell;
        var kontakte = KontakteMit(z.Email);
        return new
        {
            email = z.Email,
            rolle = kontakte.Any(k => k.Rolle == Schuelerkontakte.Erziehungsberechtigt) ? "Erziehungsberechtigte*r" : "Ausbildungsbeauftragte*r",
            name = kontakte.Select(k => k.Name).FirstOrDefault(n => n.Length > 0) ?? "",
            schueler = kontakte.Select(k => s?.Schueler.FirstOrDefault(x => x.Id == k.Schueler)).Where(x => x != null).DistinctBy(x => x!.Id)
                .Select(x => $"{x!.Vorname} {x.Name} ({x.Klasse})").OrderBy(x => x, De).ToList(),
        };
    }

    /// <summary>Registrierung abschließen: Elternkonto mit den Schüler*innen anlegen, bei denen die E-Mail-Adresse hinterlegt ist.</summary>
    public Benutzer RegistrierungAbschliessen(string? token, string? login, string? passwort)
    {
        var z = TokenPruefen(ArtRegistrierung, token) ?? throw new ArgumentException("Der Link ist ungültig oder abgelaufen. Bitte registrieren Sie sich erneut.");
        var l = (login ?? "").Trim();
        var pw = passwort ?? "";
        if (l.Length < 2) throw new ArgumentException("Bitte einen Benutzernamen (mindestens 2 Zeichen) wählen.");
        _benutzer.RegelPruefen(pw);
        var kontakte = KontakteMit(z.Email);
        if (kontakte.Count == 0) throw new ArgumentException("Die E-Mail-Adresse ist bei keiner Schülerin und keinem Schüler mehr hinterlegt.");
        var ids = kontakte.Select(k => k.Schueler).Where(i => _pakete.Aktuell?.Schueler.Any(x => x.Id == i) ?? false).Distinct().ToList();
        var k0 = kontakte[0];
        var name = k0.Name.Length > 0 ? k0.Name : k0.Betrieb.Length > 0 ? k0.Betrieb : z.Email;
        if (k0.Rolle == Schuelerkontakte.Ausbildungsbeauftragt && k0.Betrieb.Length > 0 && k0.Name.Length > 0) name = $"{k0.Name} ({k0.Betrieb})";
        var b = _benutzer.Anlegen(l, pw, Rollen.Eltern, "", name, z.Email);
        if (ids.Count > 0) _benutzer.SchuelerZuordnen(b.Id, ids);
        if (PersonMit(z.Email) is int person) _benutzer.PersonSetzen(b.Id, person);
        TokenVerbrauchen(token!);
        return b;
    }

    // ------------------------------------------------------------------------------------------ Kennwort-Blatt

    /// <summary>Schülerkonten je Schüler*in (erstes Konto der Rolle Schüler*in mit dieser Zuordnung).</summary>
    private Dictionary<int, Benutzer> Schuelerkonten() => _benutzer.Alle().Where(b => b.Rolle == Rollen.Schueler)
        .SelectMany(b => b.SchuelerIds.Select(id => (Id: id, B: b))).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().B);

    private static void Pruefen(Benutzer b, Stand s, string klasse)
    {
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (!Schuelerverwaltung.DarfBearbeiten(b, s, klasse)) throw new UnauthorizedAccessException("Kennwort-Blätter erstellt die Klassenleitung bzw. die Administration.");
    }

    /// <summary>Schüler*innen der Klasse mit Konto (Benutzername, schon angemeldet?) für die Auswahl vor dem Kennwort-Blatt.</summary>
    public object Kontenstand(Benutzer b, Stand s, string klasse)
    {
        Pruefen(b, s, klasse);
        var konten = Schuelerkonten();
        return Klassenzugehoerigkeit.Am(s, new[] { klasse }, _uhr.Heute).Select(x =>
        {
            konten.TryGetValue(x.Id, out var k);
            return new { x.Id, x.Name, x.Vorname, login = k?.Login ?? "", angemeldet = k?.LetzteAnmeldung != null, gesperrt = k?.Gesperrt ?? false };
        }).ToList();
    }

    /// <summary>
    /// Kennwort-Blatt: für die gewählten Schüler*innen ein Konto anlegen (Benutzername nach dem Kurznamen, z. B. „baskhamed“) bzw. ein neues
    /// Startkennwort vergeben, das bei der ersten Anmeldung geändert werden muss.
    /// </summary>
    public List<Anmeldekarte> Kennwortblatt(Benutzer b, Stand s, string klasse, IEnumerable<int>? schuelerIds)
    {
        Pruefen(b, s, klasse);
        var schueler = Klassenzugehoerigkeit.Am(s, new[] { klasse }, _uhr.Heute);
        var wahl = schuelerIds?.ToHashSet();
        var konten = Schuelerkonten();
        var res = new List<Anmeldekarte>();
        foreach (var x in schueler.Where(x => wahl is not { Count: > 0 } || wahl.Contains(x.Id)))
        {
            var pw = Sicherheit.Zufall(10);
            var name = $"{x.Vorname} {x.Name}".Trim();
            if (konten.TryGetValue(x.Id, out var k))
            {
                _benutzer.PasswortSetzen(k.Id, pw, true);
                res.Add(new Anmeldekarte($"{x.Name} {x.Vorname}".Trim(), x.Klasse, k.Login, pw));
                continue;
            }
            var basis = Unterrichtsgruppen.Kurzname(x.Name, x.Vorname).ToLowerInvariant();
            if (basis.Length < 2) basis = "schueler" + x.Id;
            var login = basis;
            for (var i = 2; _benutzer.NachLogin(login) != null; i++) login = basis + i;
            var neu = _benutzer.Anlegen(login, pw, Rollen.Schueler, x.Klasse, name, "", true);
            _benutzer.SchuelerZuordnen(neu.Id, new[] { x.Id });
            res.Add(new Anmeldekarte($"{x.Name} {x.Vorname}".Trim(), x.Klasse, login, pw));
        }
        return res;
    }
}
