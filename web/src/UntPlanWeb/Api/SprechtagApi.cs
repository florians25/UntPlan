using System.Globalization;
using UntPlan.Web.Contracts;
using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Sprechtag (Elternsprechtag) – alle Endpunkte unter <c>/api/app/sprechtag</c>:
/// <list type="bullet">
/// <item><c>GET /start</c>: Hinweiskarten für die Startseite (Lehrkräfte während Einladung/Anmeldung, Eltern während der Anmeldung bzw. mit Terminen).</item>
/// <item><c>GET /</c>: Sprechtage (Administration alle, sonst nur sichtbare); <c>GET /optionen</c>: Lehrkräfte und Räume (Administration).</item>
/// <item>Administration: <c>POST /</c>, <c>PUT /{id}</c>, <c>DELETE /{id}</c>, <c>GET /{id}</c> (mit Übersicht je Lehrkraft).</item>
/// <item>Lehrkraft (Administration mit <c>?lehrer=</c> für jede Lehrkraft): <c>GET /{id}/lehrkraft</c> (Zeitraster, Einladungen),
/// <c>PUT /{id}/raum</c>, <c>PUT /{id}/sperren</c>, <c>GET /{id}/schueler</c>, <c>POST /{id}/einladungen</c>, <c>DELETE /{id}/einladungen/{eid}</c>,
/// <c>POST /{id}/termine</c> (manuell), <c>PUT /{id}/termine/{tid}</c> (verschieben), <c>GET /{id}/plan.pdf</c>.</item>
/// <item>Eltern: <c>GET /{id}/eltern</c> (Kinder, Lehrkräfte, freie Zeitfenster, Einladungen, Termine), <c>POST /{id}/buchen</c>, <c>GET /{id}/elternplan.pdf</c>.</item>
/// <item><c>DELETE /{id}/termine/{tid}</c>: Storno (Administration, Lehrkraft des Termins, Eltern des Kindes im Anmeldezeitraum).</item>
/// <item>Absagen: <c>POST /{id}/termine/{tid}/absagen</c> (Termin mit Grund – Eltern bis zum Sprechtag, Lehrkraft, Administration; die andere Seite
/// erhält eine Mitteilung), <c>POST /{id}/teilnahme/absagen</c> bzw. <c>/teilnahme/wiederaufnehmen</c> (Lehrkraft; Administration mit <c>lehrer</c>),
/// <c>POST /{id}/absagen</c> (Administration: ganzer Sprechtag, alle Beteiligten werden benachrichtigt).</item>
/// <item>PDF-Formulare mit <c>?ausrichtung=hoch|quer</c> (Vorgabe: Terminplan einer Lehrkraft und Elternplan hoch, Gesamtplan quer).</item>
/// <item>Status „angekündigt“ (<c>status</c>): freigegeben, Anmeldung beginnt erst – Startseite, Liste und Familienansicht zeigen Datum, Zeit, Ort
/// und Anmeldebeginn. Erziehungsberechtigte und Schüler*innen sehen die Ankündigung standardmäßig (Optionen <c>ankuendigen</c>, <c>sichtbarEltern</c>,
/// <c>sichtbarSchueler</c> sind an, fehlende gespeicherte Optionen gelten als an); Schüler*innen nur lesend.</item>
/// <item>Warteliste: <c>POST /{id}/warteliste</c>, <c>DELETE /{id}/warteliste/{wid}</c>.</item>
/// </list>
/// </summary>
public static class SprechtagApi
{
    public sealed record RaumDaten(string? Lehrer, string? Raum, bool? Online = null, string? Link = null);
    public sealed record WarteDaten(string? Lehrer, int SchuelerId);
    public sealed record SperrDaten(string? Lehrer, List<string>? Beginne, bool Gesperrt);
    public sealed record EinladungDaten(string? Lehrer, List<int>? SchuelerIds, string? Text);
    public sealed record TerminDaten(string? Lehrer, int SchuelerId, string? Beginn, string? Bemerkung);
    public sealed record VerschiebeDaten(string? Beginn, string? Bemerkung);
    public sealed record BuchDaten(string? Lehrer, int SchuelerId, string? Beginn);
    public sealed record AbsageDaten(string? Lehrer, string? Grund);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static Benutzer Ich(HttpContext c, bool schuelerErlaubt = false)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!b.Kollegium && b.Rolle != Rollen.Eltern && !(schuelerErlaubt && b.Rolle == Rollen.Schueler))
            throw new UnauthorizedAccessException("Der Sprechtag steht Lehrkräften, Schulleitung, Administration und Erziehungsberechtigten zur Verfügung.");
        return b;
    }

    private static Benutzer Admin(HttpContext c)
    {
        var b = Ich(c);
        if (!Rechtedienst.Darf(b, Recht.SprechtagVerwalten)) throw new UnauthorizedAccessException("Nur für die Administration.");
        return b;
    }

    private static Benutzer Eltern(HttpContext c)
    {
        var b = Ich(c);
        if (b.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Diese Seite steht Erziehungsberechtigten zur Verfügung.");
        return b;
    }

    /// <summary>Sprechtag, den das Konto sehen darf (nicht sichtbare nur für die Administration).</summary>
    private static Sprechtag Sichtbar(Sprechtagdienst d, Benutzer b, int id)
    {
        var t = d.Pflicht(id);
        if (!Sprechtagdienst.SichtbarFuer(t, b)) throw new ArgumentException("Unbekannter Sprechtag.");
        return t;
    }

    /// <summary>Lehrkraft, für die gehandelt wird: eigene (beteiligte) Lehrkraft bzw. bei der Administration die angegebene.</summary>
    private static string Lehrkraft(Benutzer b, Sprechtag t, string? lehrer)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
        var gewuenscht = string.IsNullOrWhiteSpace(lehrer) ? b.LehrerKuerzel : lehrer.Trim();
        if (gewuenscht is null) throw new ArgumentException("Bitte eine Lehrkraft wählen.");
        if (!Rechtedienst.Darf(b, Recht.SprechtagVerwalten) && !Ci.Equals(gewuenscht, b.LehrerKuerzel)) throw new UnauthorizedAccessException("Sie können nur Ihren eigenen Terminplan bearbeiten.");
        return Sprechtagdienst.Beteiligt(t, gewuenscht)?.Lehrer ?? throw new UnauthorizedAccessException("Sie sind an diesem Sprechtag nicht beteiligt.");
    }

    private static string Schule(Einstellungen e, Paketdienst p) => e.Schulname.Length > 0 ? e.Schulname : p.Aktuell?.Paket.Schule.Name ?? "";

    private static string DatumText(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? $"{Plandienst.Wochentage[Plandienst.TagIndex(d)]}, {d:dd.MM.yyyy}" : iso;
    private static string ZeitText(string iso) => DateTime.TryParse(iso, Inv, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy HH:mm", Inv) : iso;

    /// <summary>Statuszeile für die Anzeige (z. B. „Angekündigt – Anmeldung ab …“).</summary>
    public static string StatusText(Sprechtagdienst d, Sprechtag t) => d.Status(t) switch
    {
        Sprechtagdienst.Angekuendigt => $"Angekündigt – Anmeldung ab {ZeitText(t.AnmeldungVon)}",
        Sprechtagdienst.Anmeldung => $"Anmeldung bis {ZeitText(t.AnmeldungBis)}",
        Sprechtagdienst.Geschlossen => "Anmeldung geschlossen",
        Sprechtagdienst.Vorbei => "vorbei",
        Sprechtagdienst.Abgesagt => "abgesagt",
        _ => "in Vorbereitung (nicht sichtbar)",
    };

    /// <summary>Sprechtag als JSON; der Online-Link einer Lehrkraft nur für die Verwaltung und die Lehrkraft selbst.</summary>
    private static object Json(Sprechtagdienst d, Sprechtag t, Stand? s, Benutzer? b = null)
    {
        var verwaltung = b != null && Rechtedienst.Darf(b, Recht.SprechtagVerwalten);
        return new
        {
            t.Id, t.Name, t.Datum, datumText = DatumText(t.Datum), t.Beginn, t.Ende, t.Dauer, t.Pausen, t.EinladungBis, t.AnmeldungVon, t.AnmeldungBis,
            einladungEnde = Sprechtagdienst.EinladungEnde(t).ToString("yyyy-MM-dd'T'HH:mm", Inv), t.Sichtbar, t.Raumwahl, t.Hinweis, phase = d.Phase(t), t.Abgesagt, t.AbsageGrund,
            status = d.Status(t), statusText = StatusText(d, t), t.Ort, anmeldungVonText = ZeitText(t.AnmeldungVon), anmeldungBisText = ZeitText(t.AnmeldungBis),
            buchbar = d.Phase(t) == Sprechtagdienst.Anmeldung, zeitbloecke = Sprechtagdienst.Zeitbloecke(t),
            optionen = new
            {
                t.Optionen.Puffer, t.Optionen.MaxTermineFamilie, t.Optionen.Warteliste, t.Optionen.ErinnerungStunden, t.Optionen.SichtbarEltern,
                t.Optionen.SichtbarSchueler, t.Optionen.Ankuendigen,
            },
            reihe = d.Reihe(t).Select(x => new { x.Id, x.Datum, datumText = DatumText(x.Datum) }),
            lehrkraefte = t.Lehrkraefte.Select(l => new
            {
                l.Lehrer, l.Raum, name = Sprechtagdienst.LehrerName(s, l.Lehrer), l.Abgesagt, l.AbsageGrund, l.Dauer, dauerEffektiv = Sprechtagdienst.DauerVon(t, l.Lehrer),
                l.Online, link = verwaltung || (b?.LehrerKuerzel is string k && Ci.Equals(k, l.Lehrer)) ? l.Link : "",
            }),
            zeitfenster = Sprechtagdienst.Zeitfenster(t).Count,
        };
    }

    private static string Termine(int n) => $"{n} {(n == 1 ? "Termin" : "Termine")}";

    /// <summary>Ankündigungstext: Datum, Uhrzeit, Ort und Anmeldebeginn.</summary>
    private static string Ankuendigung(Sprechtag t) =>
        $"Angekündigt: {DatumText(t.Datum)}, {t.Beginn}–{t.Ende} Uhr{(t.Ort.Length > 0 ? $" · {t.Ort}" : "")}. Anmeldung ab {ZeitText(t.AnmeldungVon)}.";

    /// <summary>
    /// Hinweiskarten „Sprechtag“ für die Startseite (Web und Apps, auch in <c>/api/app/heute</c>): beteiligte Lehrkräfte und Verwaltung,
    /// Erziehungsberechtigte (angekündigt, Anmeldung, eigene Termine bzw. Einladungen) und Schüler*innen (Option „sichtbar für Schüler*innen“).
    /// </summary>
    public static List<object> StartKarten(Sprechtagdienst d, Benutzer b)
    {
        var karten = new List<object>();
        if (!b.Kollegium && b.Rolle is not (Rollen.Eltern or Rollen.Schueler)) return karten;
        foreach (var t in d.Alle().Where(t => t.Sichtbar && Sprechtagdienst.SichtbarFuer(t, b)).OrderBy(t => t.Datum, StringComparer.Ordinal))
        {
            var phase = d.Phase(t);
            if (phase is Sprechtagdienst.Vorbei or Sprechtagdienst.Vorbereitung) continue;
            var termine = d.TermineVon(t);
            int eigene, einladungen = 0;
            string text;
            if (b.Kollegium)
            {
                var l = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel);
                var verwaltung = Rechtedienst.Darf(b, Recht.SprechtagVerwalten);
                // nicht beteiligte Lehrkräfte: nur die Ankündigung (Option „ankündigen“) bzw. der Hinweis auf die laufende Anmeldung
                if (l is null && !verwaltung && !(t.Optionen.Ankuendigen && phase is Sprechtagdienst.Einladung or Sprechtagdienst.Anmeldung)) continue;
                eigene = l is null ? (verwaltung ? termine.Count : 0) : termine.Count(x => Ci.Equals(x.Lehrer, l.Lehrer));
                text = phase switch
                {
                    Sprechtagdienst.Einladung when l is null && !verwaltung => Ankuendigung(t) + " Sie sind an diesem Sprechtag nicht beteiligt.",
                    Sprechtagdienst.Anmeldung when l is null && !verwaltung => $"{DatumText(t.Datum)}, {t.Beginn}–{t.Ende} Uhr{(t.Ort.Length > 0 ? $" · {t.Ort}" : "")}. Anmeldung für Erziehungsberechtigte bis {ZeitText(t.AnmeldungBis)}; Sie sind nicht beteiligt.",
                    Sprechtagdienst.Abgesagt => "Der Sprechtag wurde abgesagt." + (t.AbsageGrund.Length > 0 ? $" Grund: {t.AbsageGrund}" : ""),
                    _ when l is { Abgesagt.Length: > 0 } => "Sie haben Ihre Teilnahme abgesagt.",
                    Sprechtagdienst.Einladung when l != null => $"Hier können Sie bis {ZeitText(Sprechtagdienst.EinladungEnde(t).ToString("yyyy-MM-dd'T'HH:mm", Inv))} Erziehungsberechtigte zum Gespräch einladen.",
                    Sprechtagdienst.Einladung => $"Einladungsphase der Lehrkräfte; Anmeldung für Erziehungsberechtigte ab {ZeitText(t.AnmeldungVon)}.",
                    Sprechtagdienst.Anmeldung => $"Anmeldung für Erziehungsberechtigte bis {ZeitText(t.AnmeldungBis)} · {Termine(eigene)} gebucht.",
                    _ => $"Anmeldung geschlossen · {Termine(eigene)}.",
                };
            }
            else if (b.Rolle == Rollen.Eltern)
            {
                eigene = termine.Count(x => b.SchuelerIds.Contains(x.SchuelerId));
                einladungen = d.Einladungen(t.Id).Count(e => b.SchuelerIds.Contains(e.SchuelerId));
                // angekündigt: allen Erziehungsberechtigten (Option „ankündigen“), sonst nur mit Einladung bzw. Termin
                if (phase == Sprechtagdienst.Einladung && !t.Optionen.Ankuendigen && eigene == 0 && einladungen == 0) continue;
                if (phase is Sprechtagdienst.Geschlossen or Sprechtagdienst.Abgesagt && eigene == 0 && einladungen == 0) continue;
                text = phase switch
                {
                    Sprechtagdienst.Abgesagt => "Der Sprechtag wurde abgesagt." + (t.AbsageGrund.Length > 0 ? $" Grund: {t.AbsageGrund}" : ""),
                    Sprechtagdienst.Anmeldung => $"Hier können Sie bis {ZeitText(t.AnmeldungBis)} Gesprächstermine buchen" + (eigene > 0 ? $" · {Termine(eigene)} gebucht." : "."),
                    Sprechtagdienst.Einladung when einladungen > 0 => $"Sie haben {einladungen} {(einladungen == 1 ? "Einladung" : "Einladungen")} erhalten. Die Anmeldung beginnt am {ZeitText(t.AnmeldungVon)}.",
                    Sprechtagdienst.Einladung => Ankuendigung(t),
                    _ => $"{Termine(eigene)} gebucht.",
                };
            }
            else
            {
                // Schüler*innen: nur Information und eigene Termine (Buchung durch die Erziehungsberechtigten)
                eigene = termine.Count(x => b.SchuelerIds.Contains(x.SchuelerId));
                if (phase == Sprechtagdienst.Einladung && !t.Optionen.Ankuendigen && eigene == 0) continue;
                text = phase switch
                {
                    Sprechtagdienst.Abgesagt => "Der Sprechtag wurde abgesagt.",
                    Sprechtagdienst.Einladung => Ankuendigung(t),
                    Sprechtagdienst.Anmeldung => $"Deine Erziehungsberechtigten können bis {ZeitText(t.AnmeldungBis)} Termine buchen" + (eigene > 0 ? $" · {Termine(eigene)} gebucht." : "."),
                    _ => eigene > 0 ? $"{Termine(eigene)} gebucht." : "Anmeldung geschlossen.",
                };
            }
            karten.Add(new
            {
                t.Id, t.Name, t.Datum, datumText = DatumText(t.Datum), t.Beginn, t.Ende, t.Ort, phase, status = d.Status(t), text, termine = eigene, einladungen,
                t.AnmeldungVon, anmeldungVonText = ZeitText(t.AnmeldungVon), t.AnmeldungBis, anmeldungBisText = ZeitText(t.AnmeldungBis),
                buchbar = phase == Sprechtagdienst.Anmeldung && b.Rolle == Rollen.Eltern,
                online = b.Kollegium ? Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel)?.Online == true
                    : termine.Any(x => b.SchuelerIds.Contains(x.SchuelerId) && Sprechtagdienst.Beteiligt(t, x.Lehrer)?.Online == true),
            });
        }
        return karten;
    }

    private static object? SchuelerJson(Stand? s, int id) => s?.Schueler.FirstOrDefault(x => x.Id == id) is { } x ? new { x.Id, x.Vorname, x.Name, x.Klasse } : new { Id = id, Vorname = "", Name = $"Nr. {id}", Klasse = "" };

    private static object TerminJson(Stand? s, Benutzerdienst bd, SprechtagTermin x) => new
    {
        x.Id, x.Lehrer, lehrerName = Sprechtagdienst.LehrerName(s, x.Lehrer), x.Beginn, x.Ende, schueler = SchuelerJson(s, x.SchuelerId), x.Bemerkung, x.Quelle, x.Erstellt,
        gebuchtVon = x.BenutzerId is int u && bd.Holen(u) is { } b ? (b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login) : null,
    };

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/sprechtag");

        // ---------------------------------------------------------------- Startseite

        api.MapGet("/start", (HttpContext c, Sprechtagdienst d) => Results.Json(StartKarten(d, c.Ich() ?? throw new NichtAngemeldetException())));

        // ---------------------------------------------------------------- Liste, Verwaltung

        api.MapGet("/", (HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c, schuelerErlaubt: true);
            var s = p.Aktuell;
            var liste = d.Alle().Where(t => Sprechtagdienst.SichtbarFuer(t, b)).ToList();
            return Results.Json(new
            {
                admin = Rechtedienst.Darf(b, Recht.SprechtagVerwalten), ich = b.LehrerKuerzel ?? "", eltern = b.Rolle == Rollen.Eltern, schueler = b.Rolle == Rollen.Schueler,
                sprechtage = liste.Select(t =>
                {
                    var termine = d.Termine(t.Id);
                    return new
                    {
                        sprechtag = Json(d, t, s, b),
                        beteiligt = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel) != null,
                        termine = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) ? termine.Count : b.Familie ? termine.Count(x => b.SchuelerIds.Contains(x.SchuelerId)) : termine.Count(x => Ci.Equals(x.Lehrer, b.LehrerKuerzel)),
                    };
                }),
            });
        });

        api.MapGet("/optionen", (HttpContext c, Paketdienst p) =>
        {
            Admin(c);
            var s = p.Aktuell;
            var de = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true);
            return Results.Json(new
            {
                lehrer = (s?.Paket.Elemente.Lehrer ?? new()).OrderBy(l => l.Kurz, de).Select(l => new { l.Kurz, name = Sprechtagdienst.LehrerName(s, l.Kurz) }),
                raeume = (s?.Paket.Elemente.Raeume ?? new()).OrderBy(r => r.Kurz, de).Select(r => new { r.Kurz, r.Name }),
            });
        });

        api.MapPost("/", (SprechtagDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Speichern(p.Aktuell, null, daten);
            prot.Schreiben(b.Login, "Sprechtag angelegt", $"{t.Name} {t.Datum}");
            return Results.Json(Json(d, t, p.Aktuell, b));
        });

        api.MapPut("/{id:int}", (int id, SprechtagDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Speichern(p.Aktuell, id, daten);
            prot.Schreiben(b.Login, "Sprechtag geändert", $"{t.Name} {t.Datum}");
            return Results.Json(Json(d, t, p.Aktuell, b));
        });

        api.MapDelete("/{id:int}", (int id, HttpContext c, Sprechtagdienst d, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Pflicht(id);
            d.Loeschen(id);
            prot.Schreiben(b.Login, "Sprechtag gelöscht", $"{t.Name} {t.Datum}");
            return Results.Json(new { ok = true });
        });

        api.MapGet("/{id:int}", (int id, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c, schuelerErlaubt: true);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var termine = d.TermineVon(t);
            var einladungen = d.Einladungen(id);
            var warteliste = d.Warteliste(id);
            return Results.Json(new
            {
                sprechtag = Json(d, t, s, b), schueler = b.Rolle == Rollen.Schueler, admin = Rechtedienst.Darf(b, Recht.SprechtagVerwalten), ich = b.LehrerKuerzel ?? "", beteiligt = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel) != null,
                eltern = b.Rolle == Rollen.Eltern,
                // Übersicht je Lehrkraft (nur Administration)
                uebersicht = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) ? t.Lehrkraefte.Select(l =>
                {
                    var raster = d.Raster(t, l.Lehrer);
                    return new
                    {
                        l.Lehrer, name = Sprechtagdienst.LehrerName(s, l.Lehrer), l.Raum, gebucht = raster.Count(x => x.Status == "gebucht"), gesperrt = raster.Count(x => x.Status == "gesperrt"),
                        frei = raster.Count(x => x.Status == "frei"), einladungen = einladungen.Count(e => Ci.Equals(e.Lehrer, l.Lehrer)),
                        dauer = Sprechtagdienst.DauerVon(t, l.Lehrer), l.Online, warteliste = warteliste.Count(w => Ci.Equals(w.Lehrer, l.Lehrer)),
                    };
                }) : null,
                termine = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) ? termine.Count : 0,
            });
        });

        // ---------------------------------------------------------------- Lehrkraft

        api.MapGet("/{id:int}/lehrkraft", (int id, string? lehrer, HttpContext c, Sprechtagdienst d, Paketdienst p, Benutzerdienst bd, Uhr uhr) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, lehrer);
            var s = p.Aktuell;
            var l = Sprechtagdienst.Beteiligt(t, kurz)!;
            var termine = d.TermineVon(t);
            var warteliste = d.Warteliste(id).Where(w => Ci.Equals(w.Lehrer, kurz)).ToList();
            var einladungen = d.Einladungen(id).Where(e => Ci.Equals(e.Lehrer, kurz)).ToList();
            return Results.Json(new
            {
                sprechtag = Json(d, t, s, b), admin = Rechtedienst.Darf(b, Recht.SprechtagVerwalten),
                lehrer = new { kurz, name = Sprechtagdienst.LehrerName(s, kurz), l.Raum, l.Abgesagt, l.AbsageGrund, dauer = Sprechtagdienst.DauerVon(t, kurz), l.Online, l.Link },
                warteliste = warteliste.Select(w => new { w.Id, schueler = SchuelerJson(s, w.SchuelerId), w.Erstellt, w.Benachrichtigt }),
                darfRaum = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) || t.Raumwahl,
                raeume = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) || t.Raumwahl ? (s?.Paket.Elemente.Raeume ?? new()).OrderBy(r => r.Kurz, Ci).Select(r => new { r.Kurz, r.Name }).ToList<object>() : new List<object>(),
                einladenMoeglich = t.Sichtbar && uhr.Jetzt <= Sprechtagdienst.EinladungEnde(t),
                zeitfenster = d.Raster(t, kurz).Select(x => new { x.Beginn, x.Ende, x.Status, termin = x.Termin is null ? null : TerminJson(s, bd, x.Termin) }),
                einladungen = einladungen.Select(e => new
                {
                    e.Id, schueler = SchuelerJson(s, e.SchuelerId), e.Erstellt, e.Benachrichtigt,
                    termin = termine.FirstOrDefault(x => Ci.Equals(x.Lehrer, kurz) && x.SchuelerId == e.SchuelerId)?.Beginn,
                }),
            });
        });

        api.MapPut("/{id:int}/raum", (int id, RaumDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            if (!Rechtedienst.Darf(b, Recht.SprechtagVerwalten) && !t.Raumwahl) throw new UnauthorizedAccessException("Den Raum legt die Administration fest.");
            d.RaumSetzen(p.Aktuell, t, kurz, daten.Raum, daten.Online, daten.Link);
            return Results.Json(new { ok = true });
        });

        api.MapPut("/{id:int}/sperren", (int id, SperrDaten daten, HttpContext c, Sprechtagdienst d) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            return Results.Json(new { geaendert = d.SperrenSetzen(t, kurz, daten.Beginne ?? new(), daten.Gesperrt) });
        });

        api.MapGet("/{id:int}/schueler", (int id, string? lehrer, HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, lehrer);
            var s = p.Aktuell;
            var de = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true);
            var eltern = new HashSet<int>(c.RequestServices.GetRequiredService<Benutzerdienst>().Alle().Where(x => x.Rolle == Rollen.Eltern && !x.Gesperrt).SelectMany(x => x.SchuelerIds));
            return Results.Json(new
            {
                eigeneKlassen = Sprechtagdienst.KlassenVon(s, kurz),
                klassen = (s?.Paket.Elemente.Klassen ?? new()).Select(k => k.Kurz).OrderBy(k => k, de),
                schueler = (s?.Schueler ?? new()).OrderBy(x => x.Name, de).ThenBy(x => x.Vorname, de)
                    .Select(x => new { x.Id, x.Vorname, x.Name, x.Klasse, elternkonto = eltern.Contains(x.Id) }),
            });
        });

        api.MapPost("/{id:int}/einladungen", (int id, EinladungDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            var (neu, benachrichtigt, ohne) = d.Einladen(p.Aktuell, t, b, kurz, daten.SchuelerIds ?? new(), daten.Text);
            prot.Schreiben(b.Login, "Sprechtag: Einladungen", $"{t.Name} {kurz}: {neu} neu, {benachrichtigt} Konten benachrichtigt");
            return Results.Json(new { neu, benachrichtigt, ohneKonto = ohne });
        });

        api.MapDelete("/{id:int}/einladungen/{eid:int}", (int id, int eid, HttpContext c, Sprechtagdienst d) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
            d.EinladungLoeschen(Sichtbar(d, b, id), b, eid);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/termine", (int id, TerminDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Benutzerdienst bd, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            var x = d.Eintragen(p.Aktuell, t, b, kurz, daten.SchuelerId, (daten.Beginn ?? "").Trim(), daten.Bemerkung);
            prot.Schreiben(b.Login, "Sprechtag: Termin eingetragen", $"{t.Name} {kurz} {x.Beginn}");
            return Results.Json(TerminJson(p.Aktuell, bd, x));
        });

        api.MapPut("/{id:int}/termine/{tid:int}", (int id, int tid, VerschiebeDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Benutzerdienst bd, Mitteilungsdienst m) =>
        {
            var b = Ich(c);
            if (!b.Kollegium) throw new UnauthorizedAccessException("Termine verschieben Lehrkräfte und die Administration.");
            var t = Sichtbar(d, b, id);
            var x = d.Termin(t, tid) ?? throw new ArgumentException("Unbekannter Termin.");
            if (!Sprechtagdienst.DarfTermin(t, b, x)) throw new UnauthorizedAccessException("Diesen Termin dürfen Sie nicht ändern.");
            var neu = d.Verschieben(p.Aktuell, t, x, daten.Beginn, daten.Bemerkung);
            if (neu.Beginn != x.Beginn && x.BenutzerId is int konto)
                Benachrichtigen(m, b, p.Aktuell, konto, $"Sprechtag: Termin verschoben",
                    $"Ihr Termin bei {Sprechtagdienst.LehrerName(p.Aktuell, x.Lehrer)} am Sprechtag „{t.Name}“ ({DatumText(t.Datum)}) wurde von {x.Beginn} auf {neu.Beginn} Uhr verschoben.");
            return Results.Json(TerminJson(p.Aktuell, bd, neu));
        });

        api.MapDelete("/{id:int}/termine/{tid:int}", (int id, int tid, HttpContext c, Sprechtagdienst d, Paketdienst p, Mitteilungsdienst m, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var x = d.Termin(t, tid) ?? throw new ArgumentException("Unbekannter Termin.");
            d.Stornieren(t, b, x);
            prot.Schreiben(b.Login, "Sprechtag: Termin storniert", $"{t.Name} {x.Lehrer} {x.Beginn}");
            if (b.Kollegium && x.BenutzerId is int konto)
                Benachrichtigen(m, b, p.Aktuell, konto, "Sprechtag: Termin storniert",
                    $"Ihr Termin bei {Sprechtagdienst.LehrerName(p.Aktuell, x.Lehrer)} am Sprechtag „{t.Name}“ ({DatumText(t.Datum)}, {x.Beginn} Uhr) wurde storniert.");
            return Results.Json(new { ok = true });
        });

        // ---------------------------------------------------------------- Absagen

        api.MapPost("/{id:int}/termine/{tid:int}/absagen", (int id, int tid, AbsageDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var x = d.Termin(t, tid) ?? throw new ArgumentException("Unbekannter Termin.");
            var n = d.TerminAbsagen(p.Aktuell, t, b, x, daten.Grund);
            prot.Schreiben(b.Login, "Sprechtag: Termin abgesagt", $"{t.Name} {x.Lehrer} {x.Beginn}");
            return Results.Json(new { ok = true, benachrichtigt = n });
        });

        api.MapPost("/{id:int}/teilnahme/absagen", (int id, AbsageDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            var (termine, n) = d.TeilnahmeAbsagen(p.Aktuell, t, b, kurz, daten.Grund);
            prot.Schreiben(b.Login, "Sprechtag: Teilnahme abgesagt", $"{t.Name} {kurz}: {termine} Termine");
            return Results.Json(new { termine, benachrichtigt = n });
        });

        api.MapPost("/{id:int}/teilnahme/wiederaufnehmen", (int id, AbsageDaten daten, HttpContext c, Sprechtagdienst d, Protokoll prot) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var kurz = Lehrkraft(b, t, daten.Lehrer);
            d.TeilnahmeWiederaufnehmen(t, kurz);
            prot.Schreiben(b.Login, "Sprechtag: Teilnahme wieder aufgenommen", $"{t.Name} {kurz}");
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/absagen", (int id, AbsageDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Pflicht(id);
            var (termine, n) = d.SprechtagAbsagen(p.Aktuell, t, b, daten.Grund);
            prot.Schreiben(b.Login, "Sprechtag abgesagt", $"{t.Name}: {termine} Termine, {n} Konten benachrichtigt");
            return Results.Json(new { termine, benachrichtigt = n });
        });

        api.MapGet("/{id:int}/plan.pdf", (int id, string? lehrer, bool? leer, string? ausrichtung, HttpContext c, Sprechtagdienst d, Paketdienst p, Benutzerdienst bd, Einstellungen e) =>
        {
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var alle = Rechtedienst.Darf(b, Recht.SprechtagVerwalten) && string.IsNullOrWhiteSpace(lehrer);
            var liste = alle ? t.Lehrkraefte.Select(l => l.Lehrer).ToList() : new List<string> { Lehrkraft(b, t, lehrer) };
            var zeilen = new List<IReadOnlyList<string>>();
            foreach (var kurz in liste)
            {
                var raum = Sprechtagdienst.Beteiligt(t, kurz)?.Raum ?? "";
                foreach (var x in d.Raster(t, kurz).Where(x => leer == true || x.Status == "gebucht"))
                {
                    var k = x.Termin is null ? null : s?.Schueler.FirstOrDefault(y => y.Id == x.Termin.SchuelerId);
                    var von = x.Termin?.BenutzerId is int u && bd.Holen(u) is { } eb ? (eb.Anzeigename.Length > 0 ? eb.Anzeigename : eb.Login) : x.Termin is null ? "" : "eingetragen";
                    var zeile = new List<string> { $"{x.Beginn}–{x.Ende}", x.Termin is null ? (x.Status == "gesperrt" ? "– gesperrt –" : "") : k is null ? $"Nr. {x.Termin.SchuelerId}" : $"{k.Name}, {k.Vorname}",
                        k?.Klasse ?? "", von, x.Termin?.Bemerkung ?? "", raum };
                    if (alle) zeile.Insert(0, Sprechtagdienst.LehrerName(s, kurz));
                    zeilen.Add(zeile);
                }
            }
            var spalten = new List<string> { "Zeit", "Schüler*in", "Klasse", "Gebucht von", "Bemerkung", "Raum" };
            if (alle) spalten.Insert(0, "Lehrkraft");
            var titel = alle ? $"Terminplan {t.Name}" : $"Terminplan {Sprechtagdienst.LehrerName(s, liste[0])}";
            var pdf = PdfBericht.Erzeugen(Schule(e, p), titel, $"{t.Name} · {DatumText(t.Datum)} · {t.Beginn}–{t.Ende} Uhr", spalten, zeilen,
                leer == true ? "Freie Zeilen: Erziehungsberechtigte ohne Termin können sich hier spontan eintragen." : null, b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login, false,
                PdfBericht.Hochformat(ausrichtung, !alle));
            return Results.File(pdf, "application/pdf", $"sprechtag-{t.Datum}{(alle ? "" : "-" + liste[0].ToLowerInvariant())}.pdf");
        });

        // ---------------------------------------------------------------- Eltern

        api.MapGet("/{id:int}/eltern", (int id, HttpContext c, Sprechtagdienst d, Paketdienst p, Benutzerdienst bd) =>
        {
            // Erziehungsberechtigte; Schüler*innen nur lesend (Option „sichtbar für Schüler*innen“)
            var b = Ich(c, schuelerErlaubt: true);
            if (!b.Familie) throw new UnauthorizedAccessException("Diese Seite steht Erziehungsberechtigten zur Verfügung.");
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var termine = d.TermineVon(t);
            var einladungen = d.Einladungen(id);
            var warteliste = d.Warteliste(id);
            var phase = d.Phase(t);
            var eltern = b.Rolle == Rollen.Eltern;
            // eigene Familie: Termine aller eigenen Kinder und eigene Buchungen (für Überschneidungen)
            var meine = termine.Where(x => b.SchuelerIds.Contains(x.SchuelerId) || x.BenutzerId == b.Id).ToList();
            bool Konflikt(string beginn, string ende) => meine.Any(x => Sprechtagdienst.Minuten(x.Beginn) < Sprechtagdienst.Minuten(ende) && Sprechtagdienst.Minuten(beginn) < Sprechtagdienst.Minuten(x.Ende));
            var kinder = b.SchuelerIds.Select(i => s?.Schueler.FirstOrDefault(x => x.Id == i)).Where(x => x != null).Select(x => x!).ToList();
            var gebuchtFamilie = kinder.Count == 0 ? 0 : d.FamilienTermine(t, kinder[0].Id, b.Id);
            return Results.Json(new
            {
                sprechtag = Json(d, t, s, b), buchbar = phase == Sprechtagdienst.Anmeldung && eltern, nurLesen = !eltern,
                maxTermine = t.Optionen.MaxTermineFamilie, gebuchtFamilie, warteliste = t.Optionen.Warteliste,
                kinder = kinder.Select(k => new
                {
                    k.Id, k.Vorname, k.Name, k.Klasse,
                    lehrkraefte = d.LehrkraefteDesKindes(s, t, k).Select(l =>
                    {
                        var termin = termine.FirstOrDefault(x => Ci.Equals(x.Lehrer, l.Kurz) && x.SchuelerId == k.Id);
                        var einladung = einladungen.FirstOrDefault(e => Ci.Equals(e.Lehrer, l.Kurz) && e.SchuelerId == k.Id);
                        var raster = d.Raster(t, l.Kurz);
                        var platz = warteliste.FirstOrDefault(w => Ci.Equals(w.Lehrer, l.Kurz) && w.SchuelerId == k.Id);
                        var lk = Sprechtagdienst.Beteiligt(t, l.Kurz);
                        return new
                        {
                            l.Kurz, l.Name, l.Faecher, l.Klassenleitung, l.Raum, abgesagt = lk?.Abgesagt.Length > 0,
                            eingeladen = einladung != null, einladungText = einladung?.Text ?? "",
                            termin = termin is null ? null : new { termin.Id, termin.Beginn, termin.Ende },
                            dauer = Sprechtagdienst.DauerVon(t, l.Kurz), online = lk?.Online == true,
                            ausgebucht = termin is null && lk?.Abgesagt.Length == 0 && !raster.Any(x => x.Status == "frei"),
                            warteliste = platz is null ? null : new { platz.Id, position = Sprechtagdienst.Position(warteliste, platz) },
                            zeitfenster = termin != null || !eltern ? null : raster.Where(x => x.Status == "frei").Select(x => new { x.Beginn, x.Ende, konflikt = Konflikt(x.Beginn, x.Ende) }),
                        };
                    }),
                }),
                termine = meine.Where(x => b.SchuelerIds.Contains(x.SchuelerId)).Select(x =>
                {
                    var lk = Sprechtagdienst.Beteiligt(t, x.Lehrer);
                    return new
                    {
                        x.Id, x.Beginn, x.Ende, x.Lehrer, lehrerName = Sprechtagdienst.LehrerName(s, x.Lehrer), raum = lk?.Raum ?? "",
                        schueler = SchuelerJson(s, x.SchuelerId), x.Bemerkung, eigene = x.BenutzerId == b.Id,
                        online = lk?.Online == true, link = lk?.Online == true ? lk.Link : "",
                    };
                }),
            });
        });

        api.MapPost("/{id:int}/warteliste", (int id, WarteDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Eltern(c);
            var t = Sichtbar(d, b, id);
            var w = d.WartelisteEintragen(p.Aktuell, t, b, (daten.Lehrer ?? "").Trim(), daten.SchuelerId);
            prot.Schreiben(b.Login, "Sprechtag: Warteliste", $"{t.Name} {w.Lehrer}");
            return Results.Json(new { w.Id, w.Lehrer, w.SchuelerId, position = Sprechtagdienst.Position(d.Warteliste(id), w) });
        });

        api.MapDelete("/{id:int}/warteliste/{wid:int}", (int id, int wid, HttpContext c, Sprechtagdienst d) =>
        {
            var b = Ich(c);
            d.WartelisteLoeschen(Sichtbar(d, b, id), b, wid);
            return Results.Json(new { ok = true });
        });

        api.MapPost("/{id:int}/buchen", (int id, BuchDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Eltern(c);
            var t = Sichtbar(d, b, id);
            var x = d.Buchen(p.Aktuell, t, b, (daten.Lehrer ?? "").Trim(), daten.SchuelerId, (daten.Beginn ?? "").Trim());
            prot.Schreiben(b.Login, "Sprechtag: Termin gebucht", $"{t.Name} {x.Lehrer} {x.Beginn}");
            return Results.Json(new { x.Id, x.Lehrer, x.Beginn, x.Ende, x.SchuelerId });
        });

        api.MapGet("/{id:int}/elternplan.pdf", (int id, string? ausrichtung, HttpContext c, Sprechtagdienst d, Paketdienst p, Einstellungen e) =>
        {
            var b = Eltern(c);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var zeilen = d.TermineVon(t).Where(x => b.SchuelerIds.Contains(x.SchuelerId)).OrderBy(x => x.Beginn, StringComparer.Ordinal).Select(x =>
            {
                var k = s?.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
                var lk = Sprechtagdienst.Beteiligt(t, x.Lehrer);
                return (IReadOnlyList<string>)new List<string> { $"{x.Beginn}–{x.Ende}", Sprechtagdienst.LehrerName(s, x.Lehrer), lk is { Online: true } ? (lk.Link.Length > 0 ? $"online: {lk.Link}" : "online") : lk?.Raum ?? "",
                    k is null ? "" : $"{k.Vorname} {k.Name}", k?.Klasse ?? "", x.Bemerkung };
            }).ToList();
            var pdf = PdfBericht.Erzeugen(Schule(e, p), "Meine Termine am Sprechtag", $"{t.Name} · {DatumText(t.Datum)}{(t.Ort.Length > 0 ? $" · {t.Ort}" : "")}",
                new[] { "Zeit", "Lehrkraft", "Raum", "Kind", "Klasse", "Bemerkung" }, zeilen, null, b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login, false, PdfBericht.Hochformat(ausrichtung, true));
            return Results.File(pdf, "application/pdf", $"sprechtag-{t.Datum}-termine.pdf");
        });
    }

    /// <summary>Mitteilung an ein Elternkonto (Fehler beim Versand blockieren die Änderung nicht).</summary>
    private static void Benachrichtigen(Mitteilungsdienst m, Benutzer ich, Stand? s, int konto, string betreff, string text)
    {
        try { m.Benachrichtigen(ich, new[] { konto }, betreff, text); }
        catch (ArgumentException) { /* Konto gelöscht bzw. gesperrt */ }
    }
}
