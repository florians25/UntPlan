using System.Security;
using System.Text;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Endpunkte der Portal-Oberfläche, die über Plan, Vertretungen und Klassenbuch hinausgehen:
/// <list type="bullet">
/// <item>Startseite „Heute“ (letzte Anmeldung, Planaktualisierung, Nachrichten, Stunden des Tages).</item>
/// <item>Klassenbuch: Offene Stunden, Klassendienste, Befreiungen, Abwesenheiten bearbeiten, Sitzplan, Berichte.</item>
/// <item>Mitteilungen: Entwürfe, Listen, Anhänge. Profil und Konto wechseln.</item>
/// <item>Administration: Stammdaten, freie Tage, Einstellungen, Benutzer-Massenaktionen, CSV-Import, aktive Benutzer.</item>
/// <item>RSS-Feed der öffentlichen Nachrichten zum Tag.</item>
/// </list>
/// </summary>
public static class ErweiterungApi
{
    /// <summary>„Findet online statt“ einer Stunde; Link (optional) = Videokonferenz-Adresse (http/https), null = unverändert, "" = entfernen.</summary>
    public sealed record OnlineDaten(DateOnly Datum, int Stunde, int Unterricht, bool Online, string? Link = null, string? OnlineLink = null, int? Einzelstunde = null);
    public sealed record AbwesenheitNeuDaten(int SchuelerId, DateOnly Von, DateOnly Bis, int? StundeVon, int? StundeBis, string? Grund, string? Status, string? Bemerkung,
        string? Nachweis = null, string? EntschuldigtAm = null, bool? ElternInformiert = null, string? NotizIntern = null);
    public sealed record AbwesenheitAenderDaten(List<int> Ids, string? Grund, string? Bemerkung, string? Status,
        string? Nachweis = null, string? EntschuldigtAm = null, bool? ElternInformiert = null, string? NotizIntern = null);
    public sealed record IdsDaten(List<int> Ids);
    public sealed record DienstDaten(int DienstId, int SchuelerId, DateOnly Von, DateOnly Bis, string? Text);
    public sealed record DienstWocheDaten(int DienstId, int SchuelerId, DateOnly Datum, bool An);
    public sealed record DienstAutoDaten(string Klasse, int DienstId, DateOnly Von, DateOnly Bis, int? ProWoche);
    public sealed record BefreiungDaten(int SchuelerId, DateOnly Von, DateOnly Bis, int? Wochentag, string? Fach, string? Beginn, string? Ende, string? Grund, string? Text);
    public sealed record EntwurfDaten(int? Id, List<string>? Empfaenger, string? Betreff, string? Text, bool Wichtig, bool Lesebestaetigung = false, int? AntwortAuf = null,
        bool AntwortErlaubt = true);
    public sealed record ListeDaten(string Name, List<string>? Mitglieder);
    public sealed record ProfilDaten(string? Email, string? Sprache, bool Benachrichtigung);
    public sealed record GrundDaten(string Kurz, string Name, string? Art, bool Entschuldigt, int? Reihenfolge, bool? Aktiv);
    public sealed record DienstartDaten(string? Kurz, string Name, bool? Aktiv, int? Reihenfolge);
    public sealed record FreierTagDaten(DateOnly Von, DateOnly Bis, string? Art, string Name);
    public sealed record MassenDaten(string Aktion, List<int>? Ids);
    public sealed record ImportDaten(List<Verwaltungsdienst.Importzeile> Zeilen, bool Ersetzen);

    private const string Geraetecookie = "untplan-geraet";

    private static Benutzer Ich(HttpContext c) => c.Ich() ?? throw new NichtAngemeldetException();
    private static Stand Paket(Paketdienst p) => p.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
    private static Benutzer Kollegium(HttpContext c) { var b = Ich(c); return b.Kollegium ? b : throw new UnauthorizedAccessException("Nur für das Kollegium."); }
    private static Benutzer Admin(HttpContext c) { var b = Ich(c); return b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration."); }
    private static DateOnly D(string? s, DateOnly vorgabe) => DateOnly.TryParse(s, out var d) ? d : vorgabe;
    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Gerätekennung (HttpOnly-Cookie) für „Konto wechseln“; wird bei Bedarf angelegt.</summary>
    public static string Geraet(HttpContext c, bool anlegen)
    {
        if (c.Request.Cookies.TryGetValue(Geraetecookie, out var g) && g.Length >= 20) return g;
        if (!anlegen) return "";
        g = Sicherheit.Zufall(40);
        c.Response.Cookies.Append(Geraetecookie, g, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = c.Request.IsHttps, MaxAge = TimeSpan.FromDays(365), IsEssential = true, Path = "/",
        });
        return g;
    }

    /// <summary>Prüfungssperren (Administration): höchstens n Prüfungen je Klasse und Tag bzw. Woche.</summary>
    public static void PruefungssperrePruefen(Verwaltungsdienst vw, Listendienst l, Rueckmeldungsdienst r, Stand s, Benutzer b, DateOnly d, IEnumerable<string> klassen,
        (DateOnly Datum, int Von, int Bis)? ohne = null)
    {
        if (!vw.Wahr("pruefungssperren", "aktiv")) return;
        if (Rechtedienst.Darf(b, Recht.PruefungSperrenUmgehen) && vw.Wahr("pruefungssperren", "leitungAusnehmen")) return;
        var maxTag = vw.Zahl("pruefungssperren", "maxProTag");
        var maxWoche = vw.Zahl("pruefungssperren", "maxProWoche");
        var mo = Plandienst.Montag(d);
        // ohne Dubletten (übernommen, noch nicht quittiert) und ohne zum Löschen vorgemerkte bzw. die gerade verlegte Prüfung
        var alle = l.Pruefungen(s, r.OffeneKlausuren(s), mo, mo.AddDays(6), null, null, r.OffeneLoeschungen())
            .Where(p => ohne is not { } o || p.Datum != Plandienst.Iso(o.Datum) || p.StundeVon > o.Bis || p.StundeBis < o.Von).ToList();
        foreach (var k in klassen.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var eigene = alle.Where(p => p.Klassen.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (maxTag > 0 && eigene.Count(p => p.Datum == Plandienst.Iso(d)) >= maxTag)
                throw new ArgumentException($"Prüfungssperre: Die Klasse {k} hat an diesem Tag bereits {maxTag} Prüfung(en).");
            if (maxWoche > 0 && eigene.Count >= maxWoche)
                throw new ArgumentException($"Prüfungssperre: Die Klasse {k} hat in dieser Woche bereits {maxWoche} Prüfung(en).");
        }
    }

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app");

        // ---------------------------------------------------------------- Startseite „Heute“

        api.MapGet("/start", (HttpContext c, Paketdienst p, Plandienst plan, Vertretungsplandienst vp, Klassenbuchdienst kb, Mitteilungsdienst m, Verwaltungsdienst vw, Rueckmeldungsdienst r, Uhr uhr) =>
        {
            var b = Ich(c);
            var s = p.Aktuell;
            var heute = uhr.Heute;
            var typ = b.KlassenKuerzel != null ? Elementtyp.Klasse : Elementtyp.Lehrer;
            var element = b.KlassenKuerzel ?? b.LehrerKuerzel;
            var stunden = new List<object>();
            // „Mein nächster Unterricht“: heute bzw. der nächste Unterrichtstag mit Stunden (höchstens zwei Wochen voraus)
            var tag = heute;
            List<Karte> karten = new();
            if (s != null && element != null)
                for (var d = heute; d <= heute.AddDays(14); d = d.AddDays(1))
                {
                    if (!Rechtedienst.PlanSichtbar(b, d, heute)) break;   // Planfenster der Rolle
                    if (plan.Frei(s, d).Frei) continue;
                    karten = kb.Anreichern(plan.Karten(s, d, typ, element, r.Vorgemerkt(d, s)), b.Kollegium);
                    tag = d;
                    if (karten.Count > 0) break;
                }
            if (s != null && karten.Count > 0)
            {
                var eintraege = kb.Zeitraum(tag, tag);
                foreach (var k in karten.OrderBy(k => k.Von).ThenBy(k => k.Status == "entfall" ? 0 : 1))
                    for (var n = k.Von; n <= k.Bis; n++)
                    {
                        var z = s.Paket.Zeitraster.Stunden.FirstOrDefault(x => x.Nr == n);
                        var e = k.Unterricht is int u ? eintraege.FirstOrDefault(x => x.Unterricht == u && x.Stunde == k.Von) : null;
                        var hw = k.Unterricht is int u2 ? kb.HausaufgabenDerStunde(tag, k.Von, k.Bis, u2) : new List<Hausaufgabe>();
                        stunden.Add(new
                        {
                            stunde = n, beginn = z?.Beginn ?? k.Beginn, ende = z?.Ende ?? k.Ende, k.Status, k.Art, k.Text, k.Fach, k.FachName, k.FachAlt,
                            klassen = k.Klassen, lehrer = k.Lehrer, raeume = k.Raeume, k.Unterricht, von = k.Von, bis = k.Bis, k.Farbe, k.Gruppe, k.Zusaetzlich,
                            k.NotizSchueler, notizLehrer = b.Kollegium ? k.NotizLehrer : "", k.HausaufgabeFaellig, k.LehrerAbwesend, k.VertretungOffen,
                            lehrstoff = e?.Thema ?? "", hausaufgabe = string.Join(" · ", hw.Select(h => h.Text).DefaultIfEmpty(e?.Hausaufgabe ?? "").Where(x => x.Length > 0)),
                            geprueft = e?.Anwesenheit != null, online = e?.Online == true, onlineLink = e?.Online == true ? e.OnlineLink : "",
                        });
                    }
            }
            var ansicht = vw.Einstellung("ansicht");
            return Results.Json(new
            {
                datum = Plandienst.Iso(heute),
                wochentag = Plandienst.Wochentage[Plandienst.TagIndex(heute)],
                tag = Plandienst.Iso(tag),
                letzteAnmeldung = b.VorherigeAnmeldung,
                planAktualisiert = s?.Empfangen,
                nachrichten = ansicht["nachrichtenAnzeigen"].ValueKind == JsonValueKind.False ? new List<Tagesnachricht>() : vp.NachrichtenDetails(s, heute, b.Kollegium, "startseite"),
                rss = ansicht["rss"].ValueKind == JsonValueKind.True,
                frei = s != null && plan.Frei(s, heute).Frei ? plan.Frei(s, heute).Grund ?? "unterrichtsfrei" : null,
                element, typ, stunden,
                keinPlan = element is null,
                offeneStunden = b.LehrerKuerzel is string l && s != null ? kb.OffeneStunden(s, l).Count : 0,
                ungelesen = m.Ungelesen(b.Id),
            });
        });

        // ---------------------------------------------------------------- Klassenbuch: Offene Stunden, online

        api.MapGet("/klassenbuch/stunden", (string? von, string? bis, string? lehrer, string? klasse, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Uhr uhr) =>
        {
            var b = Kollegium(c);
            var v = D(von, Plandienst.Montag(uhr.Heute));
            var bi = D(bis, uhr.Heute);
            var wahl = (lehrer ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var alle = kv.Stunden(Paket(p), b, v, bi, wahl, Leer(klasse));
            return Results.Json(new
            {
                von = Plandienst.Iso(v), bis = Plandienst.Iso(bi > uhr.Heute ? uhr.Heute : bi), gesamt = alle.Count,
                offen = alle.Count(z => !z.Geprueft || !z.Lehrstoff), zeilen = alle,
                lehrer = OffeneStunden.DarfAlle(b) ? wahl.ToList() : b.LehrerKuerzel is string l ? new List<string> { l } : new List<string>(),
            });
        });

        api.MapPut("/klassenbuch/online", (OnlineDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            kv.OnlineSetzen(Kollegium(c), Paket(p), d.Datum, d.Stunde, d.Unterricht, d.Online, d.OnlineLink ?? d.Link, d.Einzelstunde);
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Abwesenheiten bearbeiten

        api.MapGet("/abwesenheitsgruende", (string? art, HttpContext c, Klassenbuchverwaltung kv) => { Kollegium(c); return Results.Json(kv.Gruende(Leer(art))); });
        api.MapGet("/abwesenheiten/texte", (HttpContext c, Klassenbuchverwaltung kv) => { Kollegium(c); return Results.Json(kv.LetzteTexte()); });
        api.MapPost("/abwesenheiten", (AbwesenheitNeuDaten d, HttpContext c, Paketdienst p, Klassenbuchdienst kb) =>
            Results.Json(new { stunden = kb.FehlenEintragen(Kollegium(c), Paket(p), d.SchuelerId, d.Von, d.Bis, d.Status ?? "offen", d.Bemerkung ?? "", d.Grund ?? "",
                Math.Max(1, d.StundeVon ?? 1), Math.Max(1, d.StundeBis ?? 99), d.Nachweis, d.EntschuldigtAm, d.ElternInformiert ?? false, d.NotizIntern) }));
        api.MapPut("/abwesenheiten", (AbwesenheitAenderDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(new { geaendert = kv.AbwesenheitAendern(Kollegium(c), Paket(p), d.Ids ?? new(), d.Grund, d.Bemerkung, d.Status,
                d.Nachweis, d.EntschuldigtAm, d.ElternInformiert, d.NotizIntern) }));
        api.MapPost("/abwesenheiten/loeschen", (IdsDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(new { geloescht = kv.AbwesenheitenLoeschen(Kollegium(c), Paket(p), d.Ids ?? new()) }));
        api.MapPost("/abwesenheiten/zusammenfassen", (IdsDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(new { zusammengefasst = kv.Zusammenfassen(Kollegium(c), Paket(p), d.Ids ?? new()) }));

        // ---------------------------------------------------------------- Klassendienste

        api.MapGet("/klassendienste", (string? klasse, string? von, string? bis, int? dienst, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Uhr uhr) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var v = D(von, Plandienst.Montag(uhr.Heute));
            var bi = D(bis, s.SchuljahrBis == DateOnly.MaxValue ? uhr.Heute.AddDays(90) : s.SchuljahrBis);
            var k = Leer(klasse);
            return Results.Json(new
            {
                arten = kv.Dienstarten().Where(a => a.Aktiv), zuordnungen = kv.Dienste(s, k, v, bi, dienst), von = Plandienst.Iso(v), bis = Plandienst.Iso(bi),
                schueler = k is null ? new List<WebSchueler>() : Klassenbuchdienst.Schueler(s, new[] { k }),
                verwalten = k != null && Klassenbuchdienst.IstKlassenleitung(b, s, k),
            });
        });
        api.MapPost("/klassendienste", (DienstDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(kv.DienstSpeichern(Kollegium(c), Paket(p), null, d.DienstId, d.SchuelerId, d.Von, d.Bis, d.Text)));
        api.MapPut("/klassendienste/{id:int}", (int id, DienstDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(kv.DienstSpeichern(Kollegium(c), Paket(p), id, d.DienstId, d.SchuelerId, d.Von, d.Bis, d.Text)));
        api.MapDelete("/klassendienste/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            kv.DienstLoeschen(Kollegium(c), Paket(p), id);
            return Results.Json(new { ok = true });
        });
        api.MapPost("/klassendienste/woche", (DienstWocheDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            kv.DienstWoche(Kollegium(c), Paket(p), d.DienstId, d.SchuelerId, d.Datum, d.An);
            return Results.Json(new { ok = true });
        });
        api.MapPost("/klassendienste/automatisch", (DienstAutoDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(new { eingeteilt = kv.DienstAutomatisch(Kollegium(c), Paket(p), d.Klasse ?? "", d.DienstId, d.Von, d.Bis, d.ProWoche ?? 2) }));

        // ---------------------------------------------------------------- Befreiungen

        api.MapGet("/befreiungen", (string? klasse, int? schueler, string? von, string? bis, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Uhr uhr) =>
        {
            var b = Kollegium(c);
            var s = Paket(p);
            var k = Leer(klasse);
            return Results.Json(new
            {
                befreiungen = kv.Befreiungen(s, k, schueler, D(von, uhr.Heute.AddDays(-30)), D(bis, uhr.Heute.AddDays(180))),
                gruende = kv.Gruende("befreiung").Where(g => g.Aktiv),
                verwalten = k is null ? s.Paket.Elemente.Klassen.Any(x => Klassenbuchdienst.IstKlassenleitung(b, s, x.Kurz)) : Klassenbuchdienst.IstKlassenleitung(b, s, k),
            });
        });
        api.MapPost("/befreiungen", (BefreiungDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(kv.BefreiungSpeichern(Kollegium(c), Paket(p), null, d.SchuelerId, d.Von, d.Bis, d.Wochentag ?? 0, d.Fach, d.Beginn, d.Ende, d.Grund, d.Text)));
        api.MapPut("/befreiungen/{id:int}", (int id, BefreiungDaten d, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
            Results.Json(kv.BefreiungSpeichern(Kollegium(c), Paket(p), id, d.SchuelerId, d.Von, d.Bis, d.Wochentag ?? 0, d.Fach, d.Beginn, d.Ende, d.Grund, d.Text)));
        api.MapDelete("/befreiungen/{id:int}", (int id, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            kv.BefreiungLoeschen(Kollegium(c), Paket(p), id);
            return Results.Json(new { ok = true });
        });
        api.MapGet("/klassenbuch/befreit", (DateOnly datum, string klassen, string? fach, string? beginn, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            Kollegium(c);
            return Results.Json(kv.BefreitAm(Paket(p), klassen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), datum, fach ?? "", beginn ?? ""));
        });

        // ---------------------------------------------------------------- Sitzplan

        SitzplanApi.Abbilden(api);

        // ---------------------------------------------------------------- Berichte

        api.MapGet("/berichte/liste", (HttpContext c) => { RechtePruefung.Verlangen(Kollegium(c), Recht.BerichteSehen); return Results.Json(Klassenbuchverwaltung.Berichtsarten); });
        api.MapGet("/berichte/erstellen/{art}", (string art, string? von, string? bis, string? klasse, int? schueler, int? n1, int? n2, string? format, string? ausrichtung,
            HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Uhr uhr, Einstellungen es) =>
        {
            var b = RechtePruefung.Verlangen(Kollegium(c), Recht.BerichteSehen);
            var v = D(von, uhr.Heute.AddDays(-30));
            var bi = D(bis, uhr.Heute);
            var bericht = kv.BerichtErstellen(Paket(p), b, art, v, bi, Leer(klasse), schueler, n1 ?? 0, n2 ?? 0);
            var dateiname = $"{art}-{Leer(klasse) ?? "alle"}-{v:yyyy-MM-dd}-{bi:yyyy-MM-dd}";
            if (format == "pdf")
            {
                var schule = es.Schulname.Length > 0 ? es.Schulname : Paket(p).Paket.Schule.Name;
                var unter = $"{(Leer(klasse) is { } kl ? $"Klasse {kl} · " : "")}Zeitraum {v:dd.MM.yyyy} – {bi:dd.MM.yyyy}";
                var pdf = PdfBericht.Erzeugen(schule, bericht.Titel, unter, bericht.Spalten,
                    bericht.Zeilen.Select(z => (IReadOnlyList<string>)z.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.GetCultureInfo("de-DE")) ?? "").ToList()).ToList(), bericht.Hinweis,
                    b.Anzeigename, true, PdfBericht.Hochformat(ausrichtung, false), bericht.Arten);
                return Results.File(pdf, "application/pdf", dateiname + ".pdf");
            }
            return format == "csv"
                ? Results.File(Klassenbuchverwaltung.BerichtCsv(bericht), "text/csv; charset=utf-8", dateiname + ".csv")
                : Results.Json(new { bericht.Id, bericht.Titel, bericht.Spalten, bericht.Zeilen, bericht.Hinweis, bericht.Arten, von = Plandienst.Iso(v), bis = Plandienst.Iso(bi) });
        });

        // ---------------------------------------------------------------- Mitteilungen: Entwürfe, Listen, Anhänge

        api.MapPost("/mitteilungen/entwurf", (EntwurfDaten d, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(new { id = m.EntwurfSpeichern(Ich(c), p.Aktuell, d.Id, d.Empfaenger ?? new(), d.Betreff ?? "", d.Text ?? "", d.Wichtig, d.Lesebestaetigung, d.AntwortAuf, d.AntwortErlaubt) }));
        api.MapPost("/mitteilungen/{id:int}/senden", (int id, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(new { id = m.EntwurfSenden(Ich(c), p.Aktuell, id) }));
        api.MapGet("/mitteilungen/listen", (HttpContext c, Mitteilungsdienst m, Paketdienst p) => Results.Json(m.Listen(Ich(c), p.Aktuell)));
        api.MapPost("/mitteilungen/listen", (ListeDaten d, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(m.ListeSpeichern(Ich(c), p.Aktuell, null, d.Name ?? "", d.Mitglieder ?? new())));
        api.MapPut("/mitteilungen/listen/{id:int}", (int id, ListeDaten d, HttpContext c, Mitteilungsdienst m, Paketdienst p) =>
            Results.Json(m.ListeSpeichern(Ich(c), p.Aktuell, id, d.Name ?? "", d.Mitglieder ?? new())));
        api.MapDelete("/mitteilungen/listen/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) => { m.ListeLoeschen(Ich(c), id); return Results.Json(new { ok = true }); });
        api.MapPost("/mitteilungen/{id:int}/anhaenge", async (int id, HttpContext c, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            if (!c.Request.HasFormContentType) throw new ArgumentException("Bitte eine Datei auswählen.");
            var form = await c.Request.ReadFormAsync();
            var datei = form.Files.FirstOrDefault() ?? throw new ArgumentException("Bitte eine Datei auswählen.");
            if (datei.Length > m.MaxAnhang) throw new ArgumentException($"Ein Anhang darf höchstens {m.Einstellungen().MaxAnhangMb} MB groß sein.");
            using var ms = new MemoryStream();
            await datei.CopyToAsync(ms);
            return Results.Json(m.AnhangHinzufuegen(b, id, datei.FileName, datei.ContentType, ms.ToArray()));
        });
        api.MapDelete("/mitteilungen/anhaenge/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) => { m.AnhangLoeschen(Ich(c), id); return Results.Json(new { ok = true }); });
        api.MapGet("/mitteilungen/anhang/{id:int}", (int id, HttpContext c, Mitteilungsdienst m) =>
            m.AnhangLaden(Ich(c), id) is { } a ? Results.File(a.Daten, "application/octet-stream", a.Name) : Results.NotFound());

        // ---------------------------------------------------------------- Profil und Konto wechseln

        api.MapPut("/profil", (ProfilDaten d, HttpContext c, Benutzerdienst bd) =>
        {
            var b = Ich(c);
            bd.ProfilSpeichern(b.Id, d.Email ?? "", d.Sprache ?? "Deutsch", d.Benachrichtigung);
            return Results.Json(new { ok = true });
        });
        api.MapGet("/konten", (HttpContext c, Verwaltungsdienst vw) =>
        {
            var b = Ich(c);
            return Results.Json(vw.Konten(Geraet(c, false)).Select(k => new { k.Id, k.Login, k.Anzeigename, rolle = Rollen.Name(k.Rolle), aktuell = k.BenutzerId == b.Id }));
        });
        api.MapPost("/konten/{id:int}/wechseln", async (int id, HttpContext c, Verwaltungsdienst vw, Protokoll prot) =>
        {
            Ich(c);
            var geraet = Geraet(c, false);
            var ziel = vw.KontoFuerWechsel(geraet, id) ?? throw new ArgumentException("Dieses Konto ist auf diesem Gerät nicht mehr gespeichert. Bitte neu anmelden.");
            await c.AnmeldenAsync(ziel);
            vw.KontoMerken(geraet, ziel.Id);
            prot.Schreiben(ziel.Login, "Konto gewechselt");
            return Results.Json(new { ok = true });
        });
        api.MapDelete("/konten/{id:int}", (int id, HttpContext c, Verwaltungsdienst vw) =>
        {
            Ich(c);
            vw.KontoVergessen(Geraet(c, false), id);
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Stammdaten (Administration)

        api.MapGet("/stammdaten/{art}", (string art, HttpContext c, Paketdienst p, Klassenbuchverwaltung kv, Verwaltungsdienst vw) =>
        {
            Admin(c);
            var s = p.Aktuell;
            var de = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);
            object Leer2() => Array.Empty<object>();
            return Results.Json(art switch
            {
                "klassen" => s is null ? Leer2() : s.Paket.Elemente.Klassen.OrderBy(k => k.Kurz, de).Select(k => new { k.Kurz, k.Name, k.Jahrgang, leitung = string.Join(", ", k.Klassenleitung),
                    schueler = s.Schueler.Count(x => x.Klasse.Equals(k.Kurz, StringComparison.OrdinalIgnoreCase)) }),
                "schueler" => s is null ? Leer2() : s.Schueler.OrderBy(x => x.Klasse, de).ThenBy(x => x.Name, de).ThenBy(x => x.Vorname, de).Select(x => new { x.Id, x.Name, x.Vorname, x.Klasse }),
                "lehrer" => s is null ? Leer2() : s.Paket.Elemente.Lehrer.OrderBy(l => l.Kurz, de).Select(l => new { l.Kurz, l.Name, l.Vorname, l.Email }),
                "faecher" => s is null ? Leer2() : s.Paket.Elemente.Faecher.OrderBy(f => f.Kurz, de).Select(f => new { f.Kurz, f.Name, f.Farbe }),
                "raeume" => s is null ? Leer2() : s.Paket.Elemente.Raeume.OrderBy(r => r.Kurz, de).Select(r => new { r.Kurz, r.Name, r.Buchbar }),
                "versionen" => vw.Verlauf(),
                "gruende" => kv.Gruende(),
                "dienste" => kv.Dienstarten(),
                _ => throw new ArgumentException("Unbekannte Stammdaten."),
            });
        });

        // ---------------------------------------------------------------- Administration

        var admin = app.MapGroup("/api/app/admin");
        admin.MapPost("/gruende", (GrundDaten d, HttpContext c, Klassenbuchverwaltung kv) =>
        {
            Admin(c);
            return Results.Json(kv.GrundSpeichern(null, d.Kurz, d.Name, d.Art ?? "abwesenheit", d.Entschuldigt, d.Reihenfolge ?? 0, d.Aktiv ?? true));
        });
        admin.MapPut("/gruende/{id:int}", (int id, GrundDaten d, HttpContext c, Klassenbuchverwaltung kv) =>
        {
            Admin(c);
            return Results.Json(kv.GrundSpeichern(id, d.Kurz, d.Name, d.Art ?? "abwesenheit", d.Entschuldigt, d.Reihenfolge ?? 0, d.Aktiv ?? true));
        });
        admin.MapDelete("/gruende/{id:int}", (int id, HttpContext c, Klassenbuchverwaltung kv) => { Admin(c); kv.GrundLoeschen(id); return Results.Json(new { ok = true }); });
        admin.MapPost("/dienstarten", (DienstartDaten d, HttpContext c, Klassenbuchverwaltung kv) =>
        {
            Admin(c);
            return Results.Json(kv.DienstartSpeichern(null, d.Kurz ?? "", d.Name, d.Aktiv ?? true, d.Reihenfolge ?? 0));
        });
        admin.MapPut("/dienstarten/{id:int}", (int id, DienstartDaten d, HttpContext c, Klassenbuchverwaltung kv) =>
        {
            Admin(c);
            return Results.Json(kv.DienstartSpeichern(id, d.Kurz ?? "", d.Name, d.Aktiv ?? true, d.Reihenfolge ?? 0));
        });
        admin.MapDelete("/dienstarten/{id:int}", (int id, HttpContext c, Klassenbuchverwaltung kv) => { Admin(c); kv.DienstartLoeschen(id); return Results.Json(new { ok = true }); });

        admin.MapGet("/freietage", (HttpContext c, Verwaltungsdienst vw, Paketdienst p) =>
        {
            Admin(c);
            return Results.Json(new
            {
                eigene = vw.FreieTage(),
                ausUntPlan = p.Aktuell?.Paket.Ferien.Select(f => new { f.Name, f.Von, f.Bis, f.Feiertag }) ?? Enumerable.Empty<object>(),
            });
        });
        admin.MapPost("/freietage", (FreierTagDaten d, HttpContext c, Verwaltungsdienst vw, Protokoll prot) =>
        {
            var b = Admin(c);
            var f = vw.FreienTagSpeichern(null, d.Von, d.Bis, d.Art ?? "frei", d.Name ?? "");
            prot.Schreiben(b.Login, "Freien Tag eingetragen", $"{f.Name} {f.Von}–{f.Bis}");
            return Results.Json(f);
        });
        admin.MapPut("/freietage/{id:int}", (int id, FreierTagDaten d, HttpContext c, Verwaltungsdienst vw) =>
        {
            Admin(c);
            return Results.Json(vw.FreienTagSpeichern(id, d.Von, d.Bis, d.Art ?? "frei", d.Name ?? ""));
        });
        admin.MapDelete("/freietage/{id:int}", (int id, HttpContext c, Verwaltungsdienst vw) => { Admin(c); vw.FreienTagLoeschen(id); return Results.Json(new { ok = true }); });

        admin.MapGet("/einstellungen/{bereich}", (string bereich, HttpContext c, Verwaltungsdienst vw) => { Admin(c); return Results.Json(vw.Einstellung(bereich)); });
        admin.MapPut("/einstellungen/{bereich}", (string bereich, Dictionary<string, JsonElement> werte, HttpContext c, Verwaltungsdienst vw, Protokoll prot) =>
        {
            var b = Admin(c);
            vw.EinstellungSpeichern(bereich, werte);
            prot.Schreiben(b.Login, "Einstellungen geändert", bereich);
            return Results.Json(vw.Einstellung(bereich));
        });

        admin.MapPost("/benutzer/aktion", (MassenDaten d, HttpContext c, Verwaltungsdienst vw, Protokoll prot) =>
        {
            var b = Admin(c);
            var n = vw.Massenaktion(b, d.Aktion ?? "", d.Ids ?? new());
            prot.Schreiben(b.Login, "Benutzer: " + d.Aktion, n.ToString());
            return Results.Json(new { anzahl = n });
        });
        admin.MapPost("/import/pruefen", async (HttpContext c, Verwaltungsdienst vw) =>
        {
            RechtePruefung.Verlangen(Ich(c), Recht.AdminImport, meldung: "Nur für die Administration.");
            if (!c.Request.HasFormContentType) throw new ArgumentException("Bitte eine CSV-Datei auswählen.");
            var form = await c.Request.ReadFormAsync();
            var datei = form.Files.FirstOrDefault() ?? throw new ArgumentException("Bitte eine CSV-Datei auswählen.");
            using var ms = new MemoryStream();
            await datei.CopyToAsync(ms);
            var zeilen = vw.ImportPruefen(ms.ToArray());
            return Results.Json(new { zeilen, fehler = zeilen.Count(z => z.Fehler != null), neu = zeilen.Count(z => z.Fehler == null && !z.Vorhanden), vorhanden = zeilen.Count(z => z.Vorhanden) });
        });
        admin.MapPost("/import/uebernehmen", (ImportDaten d, HttpContext c, Verwaltungsdienst vw, Benutzerdienst bd, Protokoll prot) =>
        {
            var b = RechtePruefung.Verlangen(Ich(c), Recht.AdminImport, meldung: "Nur für die Administration.");
            // ohne Administration keine Administrationskonten anlegen oder überschreiben (Name, E-Mail, Rolle, Sperre, Passwort)
            foreach (var z in d.Zeilen ?? new())
                RechtePruefung.AdminSchuetzen(b, d.Ersetzen ? bd.NachLogin(z.Login ?? "") : null, z.Rolle);
            var (neu, akt, ueber) = vw.ImportUebernehmen(d.Zeilen ?? new(), d.Ersetzen);
            prot.Schreiben(b.Login, "Benutzer importiert", $"{neu.Count} neu, {akt} aktualisiert");
            var csv = new StringBuilder("Benutzername;Startpasswort\r\n");
            foreach (var (l, pw) in neu) csv.Append($"{l};{pw}\r\n");
            return Results.Json(new { neu = neu.Select(x => new { login = x.Login, passwort = x.Passwort }), aktualisiert = akt, uebersprungen = ueber, csv = csv.ToString() });
        });

        // ---------------------------------------------------------------- RSS der öffentlichen Nachrichten zum Tag

        app.MapGet("/rss/nachrichten.xml", (HttpContext c, Verwaltungsdienst vw, Vertretungsplandienst vp, Paketdienst p, Einstellungen e, Uhr uhr) =>
        {
            if (!vw.Wahr("ansicht", "rss")) return Results.NotFound();
            var sb = new StringBuilder();
            var basis = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";
            static string X(string s) => SecurityElement.Escape(s) ?? "";
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<rss version=\"2.0\"><channel>");
            sb.Append($"<title>{X("Nachrichten zum Tag – " + e.Schulname)}</title><link>{X(basis)}/</link><description>Öffentliche Nachrichten zum Tag (UntPlanWeb)</description><language>de-de</language>");
            for (var d = uhr.Heute; d <= uhr.Heute.AddDays(6); d = d.AddDays(1))
                foreach (var n in vp.NachrichtenDetails(p.Aktuell, d, false, "oeffentlich"))
                    sb.Append($"<item><title>{X(n.Betreff.Length > 0 ? n.Betreff : n.Text.Length > 60 ? n.Text[..60] + " …" : n.Text)}</title><description>{X(n.Text)}</description>")
                      .Append($"<pubDate>{d.ToDateTime(TimeOnly.MinValue):R}</pubDate><guid isPermaLink=\"false\">{d:yyyyMMdd}-{Sicherheit.Hash(n.Betreff + n.Text)[..12]}</guid></item>");
            sb.Append("</channel></rss>");
            return Results.Text(sb.ToString(), "application/rss+xml; charset=utf-8");
        });
    }
}
