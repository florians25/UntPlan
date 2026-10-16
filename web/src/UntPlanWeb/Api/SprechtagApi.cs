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
/// </list>
/// </summary>
public static class SprechtagApi
{
    public sealed record RaumDaten(string? Lehrer, string? Raum);
    public sealed record SperrDaten(string? Lehrer, List<string>? Beginne, bool Gesperrt);
    public sealed record EinladungDaten(string? Lehrer, List<int>? SchuelerIds, string? Text);
    public sealed record TerminDaten(string? Lehrer, int SchuelerId, string? Beginn, string? Bemerkung);
    public sealed record VerschiebeDaten(string? Beginn, string? Bemerkung);
    public sealed record BuchDaten(string? Lehrer, int SchuelerId, string? Beginn);
    public sealed record AbsageDaten(string? Lehrer, string? Grund);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static Benutzer Ich(HttpContext c)
    {
        var b = c.Ich() ?? throw new NichtAngemeldetException();
        if (!b.Kollegium && b.Rolle != Rollen.Eltern) throw new UnauthorizedAccessException("Der Sprechtag steht Lehrkräften, Schulleitung, Administration und Erziehungsberechtigten zur Verfügung.");
        return b;
    }

    private static Benutzer Admin(HttpContext c)
    {
        var b = Ich(c);
        if (!b.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
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
        if (!t.Sichtbar && !b.IstAdmin) throw new ArgumentException("Unbekannter Sprechtag.");
        return t;
    }

    /// <summary>Lehrkraft, für die gehandelt wird: eigene (beteiligte) Lehrkraft bzw. bei der Administration die angegebene.</summary>
    private static string Lehrkraft(Benutzer b, Sprechtag t, string? lehrer)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für Lehrkräfte.");
        var gewuenscht = string.IsNullOrWhiteSpace(lehrer) ? b.LehrerKuerzel : lehrer.Trim();
        if (gewuenscht is null) throw new ArgumentException("Bitte eine Lehrkraft wählen.");
        if (!b.IstAdmin && !Ci.Equals(gewuenscht, b.LehrerKuerzel)) throw new UnauthorizedAccessException("Sie können nur Ihren eigenen Terminplan bearbeiten.");
        return Sprechtagdienst.Beteiligt(t, gewuenscht)?.Lehrer ?? throw new UnauthorizedAccessException("Sie sind an diesem Sprechtag nicht beteiligt.");
    }

    private static string Schule(Einstellungen e, Paketdienst p) => e.Schulname.Length > 0 ? e.Schulname : p.Aktuell?.Paket.Schule.Name ?? "";

    private static string DatumText(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? $"{Plandienst.Wochentage[Plandienst.TagIndex(d)]}, {d:dd.MM.yyyy}" : iso;
    private static string ZeitText(string iso) => DateTime.TryParse(iso, Inv, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy HH:mm", Inv) : iso;

    private static object Json(Sprechtagdienst d, Sprechtag t, Stand? s) => new
    {
        t.Id, t.Name, t.Datum, datumText = DatumText(t.Datum), t.Beginn, t.Ende, t.Dauer, t.Pausen, t.EinladungBis, t.AnmeldungVon, t.AnmeldungBis,
        einladungEnde = Sprechtagdienst.EinladungEnde(t).ToString("yyyy-MM-dd'T'HH:mm", Inv), t.Sichtbar, t.Raumwahl, t.Hinweis, phase = d.Phase(t), t.Abgesagt, t.AbsageGrund,
        lehrkraefte = t.Lehrkraefte.Select(l => new { l.Lehrer, l.Raum, name = Sprechtagdienst.LehrerName(s, l.Lehrer), l.Abgesagt, l.AbsageGrund }),
        zeitfenster = Sprechtagdienst.Zeitfenster(t).Count,
    };

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

        api.MapGet("/start", (HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = c.Ich() ?? throw new NichtAngemeldetException();
            var s = p.Aktuell;
            var karten = new List<object>();
            if (!b.Kollegium && b.Rolle != Rollen.Eltern) return Results.Json(karten);
            foreach (var t in d.Alle().Where(t => t.Sichtbar).OrderBy(t => t.Datum, StringComparer.Ordinal))
            {
                var phase = d.Phase(t);
                if (phase is Sprechtagdienst.Vorbei or Sprechtagdienst.Vorbereitung) continue;
                var termine = d.Termine(t.Id);
                if (b.Kollegium)
                {
                    var l = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel);
                    if (l is null && !b.IstAdmin) continue;
                    var eigene = l is null ? termine.Count : termine.Count(x => Ci.Equals(x.Lehrer, l.Lehrer));
                    var text = phase switch
                    {
                        Sprechtagdienst.Abgesagt => "Der Sprechtag wurde abgesagt." + (t.AbsageGrund.Length > 0 ? $" Grund: {t.AbsageGrund}" : ""),
                        _ when l is { Abgesagt.Length: > 0 } => "Sie haben Ihre Teilnahme abgesagt.",
                        Sprechtagdienst.Einladung when l != null => $"Hier können Sie bis {ZeitText(Sprechtagdienst.EinladungEnde(t).ToString("yyyy-MM-dd'T'HH:mm", Inv))} Erziehungsberechtigte zum Gespräch einladen.",
                        Sprechtagdienst.Einladung => $"Einladungsphase der Lehrkräfte; Anmeldung für Erziehungsberechtigte ab {ZeitText(t.AnmeldungVon)}.",
                        Sprechtagdienst.Anmeldung => $"Anmeldung für Erziehungsberechtigte bis {ZeitText(t.AnmeldungBis)} · {eigene} {(eigene == 1 ? "Termin" : "Termine")} gebucht.",
                        _ => $"Anmeldung geschlossen · {eigene} {(eigene == 1 ? "Termin" : "Termine")}.",
                    };
                    karten.Add(new { t.Id, t.Name, t.Datum, datumText = DatumText(t.Datum), phase, text, termine = eigene });
                }
                else
                {
                    var eigene = termine.Count(x => b.SchuelerIds.Contains(x.SchuelerId));
                    var einladungen = d.Einladungen(t.Id).Count(e => b.SchuelerIds.Contains(e.SchuelerId));
                    if (phase != Sprechtagdienst.Anmeldung && eigene == 0 && einladungen == 0) continue;
                    var text = phase switch
                    {
                        Sprechtagdienst.Abgesagt => "Der Sprechtag wurde abgesagt." + (t.AbsageGrund.Length > 0 ? $" Grund: {t.AbsageGrund}" : ""),
                        Sprechtagdienst.Anmeldung => $"Hier können Sie bis {ZeitText(t.AnmeldungBis)} Gesprächstermine buchen" + (eigene > 0 ? $" · {eigene} {(eigene == 1 ? "Termin" : "Termine")} gebucht." : "."),
                        Sprechtagdienst.Einladung => $"Sie haben {einladungen} {(einladungen == 1 ? "Einladung" : "Einladungen")} erhalten. Die Anmeldung beginnt am {ZeitText(t.AnmeldungVon)}.",
                        _ => $"{eigene} {(eigene == 1 ? "Termin" : "Termine")} gebucht.",
                    };
                    karten.Add(new { t.Id, t.Name, t.Datum, datumText = DatumText(t.Datum), phase, text, termine = eigene, einladungen });
                }
            }
            return Results.Json(karten);
        });

        // ---------------------------------------------------------------- Liste, Verwaltung

        api.MapGet("/", (HttpContext c, Sprechtagdienst d, Paketdienst p) =>
        {
            var b = Ich(c);
            var s = p.Aktuell;
            var liste = d.Alle().Where(t => b.IstAdmin || t.Sichtbar).ToList();
            return Results.Json(new
            {
                admin = b.IstAdmin, ich = b.LehrerKuerzel ?? "", eltern = b.Rolle == Rollen.Eltern,
                sprechtage = liste.Select(t =>
                {
                    var termine = d.Termine(t.Id);
                    return new
                    {
                        sprechtag = Json(d, t, s),
                        beteiligt = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel) != null,
                        termine = b.IstAdmin ? termine.Count : b.Rolle == Rollen.Eltern ? termine.Count(x => b.SchuelerIds.Contains(x.SchuelerId)) : termine.Count(x => Ci.Equals(x.Lehrer, b.LehrerKuerzel)),
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
            return Results.Json(Json(d, t, p.Aktuell));
        });

        api.MapPut("/{id:int}", (int id, SprechtagDaten daten, HttpContext c, Sprechtagdienst d, Paketdienst p, Protokoll prot) =>
        {
            var b = Admin(c);
            var t = d.Speichern(p.Aktuell, id, daten);
            prot.Schreiben(b.Login, "Sprechtag geändert", $"{t.Name} {t.Datum}");
            return Results.Json(Json(d, t, p.Aktuell));
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
            var b = Ich(c);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var termine = d.Termine(id);
            var einladungen = d.Einladungen(id);
            return Results.Json(new
            {
                sprechtag = Json(d, t, s), admin = b.IstAdmin, ich = b.LehrerKuerzel ?? "", beteiligt = Sprechtagdienst.Beteiligt(t, b.LehrerKuerzel) != null,
                eltern = b.Rolle == Rollen.Eltern,
                // Übersicht je Lehrkraft (nur Administration)
                uebersicht = b.IstAdmin ? t.Lehrkraefte.Select(l =>
                {
                    var raster = d.Raster(t, l.Lehrer);
                    return new
                    {
                        l.Lehrer, name = Sprechtagdienst.LehrerName(s, l.Lehrer), l.Raum, gebucht = raster.Count(x => x.Status == "gebucht"), gesperrt = raster.Count(x => x.Status == "gesperrt"),
                        frei = raster.Count(x => x.Status == "frei"), einladungen = einladungen.Count(e => Ci.Equals(e.Lehrer, l.Lehrer)),
                    };
                }) : null,
                termine = b.IstAdmin ? termine.Count : 0,
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
            var termine = d.Termine(id);
            var einladungen = d.Einladungen(id).Where(e => Ci.Equals(e.Lehrer, kurz)).ToList();
            return Results.Json(new
            {
                sprechtag = Json(d, t, s), admin = b.IstAdmin,
                lehrer = new { kurz, name = Sprechtagdienst.LehrerName(s, kurz), l.Raum, l.Abgesagt, l.AbsageGrund },
                darfRaum = b.IstAdmin || t.Raumwahl,
                raeume = b.IstAdmin || t.Raumwahl ? (s?.Paket.Elemente.Raeume ?? new()).OrderBy(r => r.Kurz, Ci).Select(r => new { r.Kurz, r.Name }).ToList<object>() : new List<object>(),
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
            if (!b.IstAdmin && !t.Raumwahl) throw new UnauthorizedAccessException("Den Raum legt die Administration fest.");
            d.RaumSetzen(p.Aktuell, t, kurz, daten.Raum);
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
            var alle = b.IstAdmin && string.IsNullOrWhiteSpace(lehrer);
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
            var b = Eltern(c);
            var t = Sichtbar(d, b, id);
            var s = p.Aktuell;
            var termine = d.Termine(id);
            var einladungen = d.Einladungen(id);
            var phase = d.Phase(t);
            // eigene Familie: Termine aller eigenen Kinder und eigene Buchungen (für Überschneidungen)
            var meine = termine.Where(x => b.SchuelerIds.Contains(x.SchuelerId) || x.BenutzerId == b.Id).ToList();
            bool Konflikt(string beginn) => meine.Any(x => Sprechtagdienst.Minuten(x.Beginn) < Sprechtagdienst.Minuten(beginn) + t.Dauer && Sprechtagdienst.Minuten(beginn) < Sprechtagdienst.Minuten(x.Beginn) + t.Dauer);
            var kinder = b.SchuelerIds.Select(i => s?.Schueler.FirstOrDefault(x => x.Id == i)).Where(x => x != null).Select(x => x!).ToList();
            return Results.Json(new
            {
                sprechtag = Json(d, t, s), buchbar = phase == Sprechtagdienst.Anmeldung,
                kinder = kinder.Select(k => new
                {
                    k.Id, k.Vorname, k.Name, k.Klasse,
                    lehrkraefte = d.LehrkraefteDesKindes(s, t, k).Select(l =>
                    {
                        var termin = termine.FirstOrDefault(x => Ci.Equals(x.Lehrer, l.Kurz) && x.SchuelerId == k.Id);
                        var einladung = einladungen.FirstOrDefault(e => Ci.Equals(e.Lehrer, l.Kurz) && e.SchuelerId == k.Id);
                        return new
                        {
                            l.Kurz, l.Name, l.Faecher, l.Klassenleitung, l.Raum, abgesagt = Sprechtagdienst.Beteiligt(t, l.Kurz)?.Abgesagt.Length > 0,
                            eingeladen = einladung != null, einladungText = einladung?.Text ?? "",
                            termin = termin is null ? null : new { termin.Id, termin.Beginn, termin.Ende },
                            zeitfenster = termin != null ? null : d.Raster(t, l.Kurz).Where(x => x.Status == "frei").Select(x => new { x.Beginn, x.Ende, konflikt = Konflikt(x.Beginn) }),
                        };
                    }),
                }),
                termine = meine.Where(x => b.SchuelerIds.Contains(x.SchuelerId)).Select(x => new
                {
                    x.Id, x.Beginn, x.Ende, x.Lehrer, lehrerName = Sprechtagdienst.LehrerName(s, x.Lehrer), raum = Sprechtagdienst.Beteiligt(t, x.Lehrer)?.Raum ?? "",
                    schueler = SchuelerJson(s, x.SchuelerId), x.Bemerkung, eigene = x.BenutzerId == b.Id,
                }),
            });
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
            var zeilen = d.Termine(id).Where(x => b.SchuelerIds.Contains(x.SchuelerId)).OrderBy(x => x.Beginn, StringComparer.Ordinal).Select(x =>
            {
                var k = s?.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
                return (IReadOnlyList<string>)new List<string> { $"{x.Beginn}–{x.Ende}", Sprechtagdienst.LehrerName(s, x.Lehrer), Sprechtagdienst.Beteiligt(t, x.Lehrer)?.Raum ?? "",
                    k is null ? "" : $"{k.Vorname} {k.Name}", k?.Klasse ?? "", x.Bemerkung };
            }).ToList();
            var pdf = PdfBericht.Erzeugen(Schule(e, p), "Meine Termine am Sprechtag", $"{t.Name} · {DatumText(t.Datum)}",
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
