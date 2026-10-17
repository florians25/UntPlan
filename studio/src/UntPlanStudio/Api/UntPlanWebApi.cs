using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>Verbindung und Exportoptionen (Desktop: „UntPlanWeb – Einstellungen“). Leerer API-Schlüssel = unverändert.</summary>
public sealed record UpwEinstellungen(string? SchulId, string? Server, int? Port, bool? Https, string? ApiSchluessel, int? TageVoraus, bool? Unbegrenzt,
    bool? IgnoriertenUnterricht, bool? FerienNichtExportieren, bool? LehrerEmail, bool? LehrerAbsenzen, bool? Absenzgruende, bool? Schueler, string? Pfad = null);
public sealed record UpwAnfang(bool Aktiv, DateOnly? Datum);
public sealed record UpwIds(List<string> Ids);
public sealed record UpwStplEinstellungen(bool VorPruefungIgnorieren, bool NachPruefungIgnorieren, int Tage);
public sealed record UpwKlausurDaten(string? Name, DateOnly? Datum, int? Von, int? Bis, string? Klassen, string? Lehrer, string? Raeume, bool? UnterrichtEntfaellt, bool? Aktiv, string? Text);

/// <summary>
/// Modul UntPlanWeb (Desktop: Fenster „UntPlanWeb“, „UntPlanWeb – Einstellungen“, „Stundenplanänderungen aus UntPlanWeb“, Klausur-Dialog):
/// Status und Zeilen (Export Stammdaten/Stundenplan*/Vertretungen*, Import Stpl.-Änderungen*/Absenzen*/Studenten*/Kurse) mit
/// Fortschritt, Verbindungseinstellungen und -prüfung, Anfangsdatum, Stundenplanänderungen (Übernehmen, Ignorieren, Stornos,
/// Einstellungen), Klausuren, importierte Schülerabwesenheiten/Klassenbuch und der optionale automatische Abgleich.
/// </summary>
public sealed class UntPlanWebApi : IStudioModul
{
    public string Name => "untplanweb";

    public void DiensteRegistrieren(IServiceCollection d)
    {
        d.AddSingleton<UntPlanWebDienst>();
        d.AddHostedService<UpwAbgleichDienst>();
    }

    /// <summary>Adresse wie im Desktop aus Server, Port, https und optionalem Pfad.</summary>
    public static string Adresse(string? server, int port, bool https, string? pfad)
    {
        if (string.IsNullOrWhiteSpace(server)) return "";
        var host = server.Trim().Replace("https://", "").Replace("http://", "").TrimEnd('/');
        var p = (pfad ?? "").Trim('/');
        if (host.Contains('/'))
        {
            p = host[(host.IndexOf('/') + 1)..].Trim('/');
            host = host[..host.IndexOf('/')];
        }
        return $"{(https ? "https" : "http")}://{host}" + ((https && port == 443) || (!https && port == 80) ? "" : $":{port}") + (p.Length > 0 ? "/" + p : "");
    }

    private static object EinstellungenDto(PortalEinstellungen p)
    {
        Uri.TryCreate(p.Adresse, UriKind.Absolute, out var uri);
        return new
        {
            adresse = p.Adresse, schulId = p.SchulId, server = uri?.Host ?? "", port = uri?.Port is int pt and > 0 ? pt : 443, https = uri is null || uri.Scheme == Uri.UriSchemeHttps,
            pfad = uri?.AbsolutePath.Trim('/') ?? "", apiSchluesselGesetzt = p.ApiSchluessel.Length > 0, tageVoraus = Math.Clamp(p.TageVoraus, 1, 366), unbegrenzt = p.OhneTagesbegrenzung,
            ignoriertenUnterricht = p.IgnoriertenUnterrichtUebertragen, ferienNichtExportieren = p.FerienNichtExportieren, lehrerEmail = p.LehrerEmail,
            lehrerAbsenzen = p.LehrerAbsenzen, absenzgruende = p.Absenzgruende, schueler = p.SchuelerExportieren,
        };
    }

    private static object Status(Arbeitsdokument a, Dokument doc, UntPlanWebDienst upw)
    {
        var d = doc.Daten;
        var p = d.Einstellungen.Portal;
        var heute = UntPlanWebDienst.Heute;
        var eingerichtet = !string.IsNullOrWhiteSpace(p.Adresse);
        Uri.TryCreate(p.Adresse, UriKind.Absolute, out var uri);
        var bisMontag = ((int)DayOfWeek.Monday - (int)heute.DayOfWeek + 7) % 7;
        var vorgaben = new List<object> { new { text = "Tagesdatum", datum = heute }, new { text = "Nächster Montag", datum = heute.AddDays(bisMontag == 0 ? 7 : bisMontag) },
            new { text = "Schuljahresbeginn", datum = d.Einstellungen.SchuljahrVon } };
        vorgaben.AddRange(d.Perioden.OrderBy(x => x.Von).Select(x => (object)new { text = $"Periode {x.Name}", datum = x.Von }));
        object Zeile(string titel, string teil, bool export, string hinweis)
        {
            var art = export ? "exportiert" : "importiert";
            return new
            {
                titel, teil, export, hinweis,
                datum = teil == UntPlanWebDienst.Stpl ? "Import aller Buchungen, Prüfungen, ..." : $"{(export ? "Export" : "Import")} ab {UntPlanWebDienst.AbDatum(d, teil):d.M.yyyy}",
                zuletzt = p.LetzteUebertragung.TryGetValue(teil, out var zl) ? $"Zuletzt {art}: {zl:dd.MM.yyyy HH:mm}" : $"Noch nicht {art}",
            };
        }
        var abgleich = upw.Abgleich(a.Id);
        return new
        {
            eingerichtet,
            exportStatus = eingerichtet ? "Unterrichtsexport aktiv." : "Keine Verbindung eingerichtet.",
            exportHinweis = eingerichtet
                ? $"Revision {p.Revision}" + (p.LetzteSynchronisation is DateTime t ? $", zuletzt übertragen am {t:dd.MM.yyyy} um {t:HH:mm}" : ", noch nicht übertragen")
                : "Bitte unter „Einstellungen“ Server, Port, Schul-ID und API-Schlüssel eintragen.",
            schulname = d.Einstellungen.Schulname.Length > 0 ? d.Einstellungen.Schulname : "UntPlanWeb",
            server = eingerichtet && uri != null ? $"{uri.Host}:{uri.Port}" : p.Adresse,
            adresse = p.Adresse,
            schulId = eingerichtet ? p.SchulId : "",
            revision = p.Revision,
            anfangsdatumAktiv = p.AnfangsdatumAktiv,
            anfangsdatum = p.Anfangsdatum ?? heute,
            datumVorgaben = vorgaben,
            zeilen = new[]
            {
                Zeile("Stammdaten", WebTeile.Stammdaten, true, "Schule, Zeitraster, Klassen, Lehrkräfte, Räume, Fächer, Gänge, Ferien (und Schüler, falls freigegeben)"),
                Zeile("Stundenplan*", WebTeile.Unterricht, true, "Perioden mit Unterricht und Stundenplan sowie Pausenaufsichten – Stundenplanänderungen gehen nur hiermit an UntPlanWeb"),
                Zeile("Vertretungen*", WebTeile.Vertretungen, true, "Vertretungen, Absenzen, Termine und Tagestexte – nur die geänderten Stunden, ohne Stundenplanänderungen"),
                Zeile("Stpl.-Änderungen*", UntPlanWebDienst.Stpl, false, "Raumänderungen, Buchungen (zusätzliche Unterrichte) und Prüfungen aus UntPlanWeb"),
                Zeile("Absenzen*", UntPlanWebDienst.Absenzen, false, "Krankmeldungen bzw. Abwesenheitsmeldungen der Lehrkräfte und Nachrichten aus UntPlanWeb"),
                Zeile("Studenten*", UntPlanWebDienst.Studenten, false, "Im Klassenbuch von UntPlanWeb erfasste Fehlzeiten der Schülerinnen und Schüler als Schülerabwesenheiten übernehmen"),
                Zeile("Kurse", UntPlanWebDienst.Kurse, false, "Klassenbuch je Unterricht (Lehrstoff, Hausaufgaben) aus UntPlanWeb übernehmen"),
            },
            laufend = upw.Laufend(a.Id),
            abgleich,
            schuelerAbwesenheiten = d.SchuelerAbwesenheiten.Count,
            klassenbuch = d.Klassenbuch.Count,
        };
    }

    private static object StplZeile(StplAenderung a) => new
    {
        id = a.Id, datum = a.Datum, datumText = Format.Datum(a.Datum), zeit = a.Zeit, stunde = a.Stunde, art = a.Art.ToString(), artText = a.ArtText,
        unterricht = a.UnterrichtId?.ToString() ?? "", klassenFach = a.KlassenFach, lehrer = a.Lehrer, raeume = a.Raeume, wert = a.Wert, statistik = a.Statistik,
        text = a.Fehler is null ? a.Text : $"{a.Text}  ({a.Fehler})".Trim(), ersteller = a.Ersteller, uebernommen = a.Uebernommen, erledigt = a.Erledigt, fehler = a.Fehler,
        storno = a.Meldung.Typ == WebRueckmeldungsTypen.BuchungStorno,
    };

    private static object KlausurDto(Klausur k, DatenIndex ix) => new
    {
        id = k.Id, name = k.Name, datum = k.Datum, datumText = Format.Datum(k.Datum), von = k.StundeVon, bis = k.StundeBis,
        stunde = k.StundeVon == k.StundeBis ? k.StundeVon.ToString() : $"{k.StundeVon}-{k.StundeBis}",
        klassen = ix.KKlassen(k.KlassenIds), lehrer = string.Join(",", k.LehrerIds.Select(ix.KLehrer)), raeume = string.Join(",", k.RaumIds.Select(r => ix.KRaum(r))),
        unterrichtEntfaellt = k.UnterrichtEntfaellt, aktiv = k.Aktiv, text = k.Text, importiert = k.Importiert, webId = k.WebId, buchungen = k.Buchungen.Count,
    };

    /// <summary>Kurznamenliste („5a,5b“) in Ids; unbekannte Namen werden wie im Desktop gemeldet.</summary>
    public static List<int> Ids(Schuldatei d, ElementTyp typ, string? text)
    {
        var res = new List<int>();
        foreach (var n in (text ?? "").Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var e = d.AlleElemente(typ).FirstOrDefault(x => x.Kurzname.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw StudioFehler.Ungueltig($"{Typnamen.Name(typ)} „{n}“ ist nicht bekannt.");
            if (!res.Contains(e.Id)) res.Add(e.Id);
        }
        return res;
    }

    private static void KlausurSetzen(Schuldatei d, Klausur k, UpwKlausurDaten e)
    {
        if (e.Lehrer != null) k.LehrerIds = Ids(d, ElementTyp.Lehrer, e.Lehrer);
        if (e.Raeume != null) k.RaumIds = Ids(d, ElementTyp.Raum, e.Raeume);
        if (e.Aktiv is bool akt) k.Aktiv = akt;
        if (k.Importiert) return; // aus UntPlanWeb übernommene Prüfung: nur Lehrkraft, Raum und Aktiv änderbar (keine Verlegung)
        if (e.Name != null) k.Name = e.Name.Trim();
        if (e.Datum is DateOnly dt) k.Datum = dt;
        var spt = Math.Max(1, d.Zeitraster.StundenProTag);
        if (e.Von is int v) k.StundeVon = Math.Clamp(v, 1, spt);
        if (e.Bis is int b) k.StundeBis = Math.Clamp(b, 1, spt);
        if (k.StundeBis < k.StundeVon) k.StundeBis = k.StundeVon;
        if (e.Klassen != null) k.KlassenIds = Ids(d, ElementTyp.Klasse, e.Klassen);
        if (e.UnterrichtEntfaellt is bool ue) k.UnterrichtEntfaellt = ue;
        if (e.Text != null) k.Text = e.Text;
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/untplanweb");

        g.MapGet("", (Arbeitsdokument a, UntPlanWebDienst upw) => a.LesenAsync(doc => Status(a, doc, upw)));

        g.MapGet("/schulname", async (Arbeitsdokument a) =>
        {
            var p = await a.LesenAsync(doc => UntPlanWebDienst.Kopie(doc.Daten.Einstellungen.Portal));
            return new { schulname = await UntPlanWebExport.SchulnameAsync(p) };
        });

        // ---- Einstellungen ----
        g.MapGet("/einstellungen", (Arbeitsdokument a) => a.LesenAsync(doc => EinstellungenDto(doc.Daten.Einstellungen.Portal)));

        g.MapPut("/einstellungen", (Arbeitsdokument a, UpwEinstellungen e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "UntPlanWeb-Einstellungen", AenderungsArt.Einstellungen, d =>
            {
                var x = d.Einstellungen.Portal;
                Uri.TryCreate(x.Adresse, UriKind.Absolute, out var alt);
                var https = e.Https ?? (alt is null || alt.Scheme == Uri.UriSchemeHttps);
                var port = e.Port ?? (alt?.Port is int pt and > 0 ? pt : https ? 443 : 80);
                if (port is < 1 or > 65535) throw StudioFehler.Ungueltig("Der Port muss zwischen 1 und 65535 liegen.");
                if (e.Server != null) x.Adresse = Adresse(e.Server, port, https, e.Pfad ?? alt?.AbsolutePath);
                if (e.SchulId != null) x.SchulId = e.SchulId.Trim();
                if (!string.IsNullOrWhiteSpace(e.ApiSchluessel)) x.ApiSchluessel = e.ApiSchluessel.Trim();
                if (e.TageVoraus is int t) x.TageVoraus = Math.Clamp(t, 1, 366);
                if (e.Unbegrenzt is bool u) x.OhneTagesbegrenzung = u;
                if (e.IgnoriertenUnterricht is bool iu) x.IgnoriertenUnterrichtUebertragen = iu;
                if (e.FerienNichtExportieren is bool fe) x.FerienNichtExportieren = fe;
                if (e.LehrerEmail is bool le) x.LehrerEmail = le;
                if (e.LehrerAbsenzen is bool la) x.LehrerAbsenzen = la;
                if (e.Absenzgruende is bool ag) x.Absenzgruende = ag;
                if (e.Schueler is bool sc) x.SchuelerExportieren = sc;
            }, doc => EinstellungenDto(doc.Daten.Einstellungen.Portal)));

        g.MapPost("/pruefen", async (Arbeitsdokument a, UpwEinstellungen e) =>
        {
            var alt = await a.LesenAsync(doc => UntPlanWebDienst.Kopie(doc.Daten.Einstellungen.Portal));
            var https = e.Https ?? true;
            var test = new PortalEinstellungen
            {
                Adresse = e.Server is null ? alt.Adresse : Adresse(e.Server, e.Port ?? (https ? 443 : 80), https, e.Pfad),
                SchulId = e.SchulId?.Trim() ?? alt.SchulId,
                ApiSchluessel = string.IsNullOrWhiteSpace(e.ApiSchluessel) ? alt.ApiSchluessel : e.ApiSchluessel.Trim(),
            };
            try { return new { ok = true, text = await UntPlanWebExport.VerbindungPruefenAsync(test) }; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new { ok = false, text = "Keine Verbindung zum Server." }; }
            catch (InvalidOperationException ex) { return new { ok = false, text = ex.Message }; }
        });

        g.MapPut("/anfangsdatum", (Arbeitsdokument a, UpwAnfang e, ClaimsPrincipal p, UntPlanWebDienst upw) =>
            a.AendernAsync(p.Benutzername(), "UntPlanWeb-Anfangsdatum", AenderungsArt.Einstellungen, d =>
            {
                d.Einstellungen.Portal.AnfangsdatumAktiv = e.Aktiv;
                d.Einstellungen.Portal.Anfangsdatum = e.Datum ?? UntPlanWebDienst.Heute;
            }, doc => Status(a, doc, upw)));

        // ---- Export / Import mit Fortschritt ----
        g.MapPost("/export/{teil}", (Arbeitsdokument a, string teil, ClaimsPrincipal p, UntPlanWebDienst upw) =>
            upw.Starten(a, teil == "alles" ? "" : teil, true, p.Benutzername()));
        g.MapPost("/import/{teil}", (Arbeitsdokument a, string teil, ClaimsPrincipal p, UntPlanWebDienst upw) =>
            upw.Starten(a, teil, false, p.Benutzername()));
        g.MapGet("/auftrag/{id}", (Arbeitsdokument a, string id, UntPlanWebDienst upw) =>
            upw.Auftrag(id) is { } x && x.DokumentId == a.Id ? Results.Ok(x) : throw StudioFehler.NichtGefunden("Der Auftrag"));

        // ---- Stundenplanänderungen aus UntPlanWeb ----
        g.MapGet("/stpl", async (Arbeitsdokument a, UntPlanWebDienst upw, DateOnly? von, DateOnly? bis, bool? erledigte, bool? neu) =>
        {
            var (liste, status) = await upw.StplLadenAsync(a, neu ?? false);
            return await a.LesenAsync(doc =>
            {
                var p = doc.Daten.Einstellungen.Portal;
                var start = von ?? UntPlanWebDienst.Heute;
                var ende = bis ?? start.AddDays(Math.Max(1, p.StplAenderungenTage));
                var zeilen = StundenplanAenderungen.Analysieren(doc.Daten, liste, start, ende, erledigte ?? false);
                return new
                {
                    status, von = start, bis = ende, eingerichtet = !string.IsNullOrWhiteSpace(p.Adresse),
                    einstellungen = new UpwStplEinstellungen(p.PausenaufsichtVorPruefungIgnorieren, p.PausenaufsichtNachPruefungIgnorieren, p.StplAenderungenTage),
                    zeilen = zeilen.Select(StplZeile),
                };
            });
        });

        g.MapPost("/stpl/uebernehmen", async (Arbeitsdokument a, UpwIds e, ClaimsPrincipal p, UntPlanWebDienst upw) =>
        {
            var ids = e.Ids.ToHashSet();
            var meldungen = upw.StplGemerkt(a.Id).Where(m => ids.Contains(m.Id)).ToList();
            if (meldungen.Count == 0) throw StudioFehler.Ungueltig("Bitte zuerst Änderungen markieren (z. B. „Buchungen markieren“).");
            List<string> ok = new();
            List<(string Id, string Grund)> nein = new();
            var portal = await a.AendernAsync(p.Benutzername(), "Stundenplanänderungen aus UntPlanWeb", AenderungsArt.Vertretung, d =>
            {
                var liste = meldungen.Select(m => StundenplanAenderungen.Zeile(d, m)).Where(z => !z.Erledigt).ToList();
                (ok, nein) = StundenplanAenderungen.Uebernehmen(d, liste, p.Benutzername());
            }, doc => UntPlanWebDienst.Kopie(doc.Daten.Einstellungen.Portal));
            await UntPlanWebDienst.QuittierenAsync(portal, ok);
            return new
            {
                uebernommen = ok, abgelehnt = nein.Select(n => new { id = n.Id, grund = n.Grund }),
                text = $"{ok.Count} Änderung(en) übernommen." + (nein.Count > 0 ? $" Nicht übernommen: {string.Join("; ", nein.Select(n => $"{n.Id} ({n.Grund})"))}" : ""),
            };
        });

        g.MapPost("/stpl/ignorieren", async (Arbeitsdokument a, UpwIds e, ClaimsPrincipal p, UntPlanWebDienst upw) =>
        {
            var ids = e.Ids.ToHashSet();
            var meldungen = upw.StplGemerkt(a.Id).Where(m => ids.Contains(m.Id)).ToList();
            if (meldungen.Count == 0) throw StudioFehler.Ungueltig("Bitte zuerst Änderungen markieren.");
            var portal = await a.AendernAsync(p.Benutzername(), "Stundenplanänderungen ignorieren", AenderungsArt.Einstellungen, d =>
                StundenplanAenderungen.Ignorieren(d, meldungen.Select(m => StundenplanAenderungen.Zeile(d, m)).ToList(), p.Benutzername()),
                doc => UntPlanWebDienst.Kopie(doc.Daten.Einstellungen.Portal));
            await UntPlanWebDienst.QuittierenAsync(portal, meldungen.Select(m => m.Id));
            return new { ignoriert = meldungen.Count, text = $"{meldungen.Count} Änderung(en) ignoriert." };
        });

        g.MapPut("/stpl/einstellungen", (Arbeitsdokument a, UpwStplEinstellungen e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Einstellungen Stundenplanänderungen", AenderungsArt.Einstellungen, d =>
            {
                var x = d.Einstellungen.Portal;
                x.PausenaufsichtVorPruefungIgnorieren = e.VorPruefungIgnorieren;
                x.PausenaufsichtNachPruefungIgnorieren = e.NachPruefungIgnorieren;
                x.StplAenderungenTage = Math.Clamp(e.Tage, 1, 365);
            }, doc => new UpwStplEinstellungen(doc.Daten.Einstellungen.Portal.PausenaufsichtVorPruefungIgnorieren, doc.Daten.Einstellungen.Portal.PausenaufsichtNachPruefungIgnorieren, doc.Daten.Einstellungen.Portal.StplAenderungenTage)));

        // ---- Klausuren (Desktop: Klausur-Dialog; aus UntPlanWeb übernommene nur Lehrkraft/Raum/Aktiv) ----
        g.MapGet("/klausuren", (Arbeitsdokument a, DateOnly? von, DateOnly? bis) => a.LesenAsync(doc =>
            doc.Daten.Klausuren.Where(k => (von is null || k.Datum >= von) && (bis is null || k.Datum <= bis))
                .OrderBy(k => k.Datum).ThenBy(k => k.StundeVon).Select(k => KlausurDto(k, doc.Index)).ToList()));

        g.MapPost("/klausuren", (Arbeitsdokument a, UpwKlausurDaten e, ClaimsPrincipal p) =>
        {
            var id = 0;
            return a.AendernAsync(p.Benutzername(), "Klausur anlegen", AenderungsArt.Vertretung, d =>
            {
                var k = new Klausur { Name = "Klausur", Datum = e.Datum ?? UntPlanWebDienst.Heute };
                KlausurSetzen(d, k, e);
                k.Id = id = d.NeueId();
                d.Klausuren.Add(k);
                if (Kalender.IstUnterrichtstag(d, k.Datum)) Vertretungen.Aktualisieren(d, k.Datum);
            }, doc => KlausurDto(doc.Daten.Klausuren.First(k => k.Id == id), doc.Index));
        });

        g.MapPut("/klausuren/{id:int}", (Arbeitsdokument a, int id, UpwKlausurDaten e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Klausur ändern", AenderungsArt.Vertretung, d =>
            {
                var k = d.Klausuren.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Klausur Nr. {id}");
                var alt = k.Datum;
                KlausurSetzen(d, k, e);
                foreach (var t in new[] { alt, k.Datum }.Distinct().Where(t => Kalender.IstUnterrichtstag(d, t))) Vertretungen.Aktualisieren(d, t);
            }, doc => KlausurDto(doc.Daten.Klausuren.First(k => k.Id == id), doc.Index)));

        g.MapDelete("/klausuren/{id:int}", (Arbeitsdokument a, int id, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Klausur löschen", AenderungsArt.Vertretung, d =>
            {
                var k = d.Klausuren.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Klausur Nr. {id}");
                // zur Prüfung gewordene Buchungen kehren zurück (wie beim Löschen über UntPlanWeb)
                StundenplanAenderungen.KlausurZeilenEntfernen(d, k);
                d.Klausuren.Remove(k);
                if (Kalender.IstUnterrichtstag(d, k.Datum)) Vertretungen.Aktualisieren(d, k.Datum);
            }, _ => new { ok = true }));

        // ---- importierte Daten (nur lesen) ----
        g.MapGet("/schuelerabwesenheiten", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var schueler = doc.Daten.Schueler.ToDictionary(s => s.Id);
            return doc.Daten.SchuelerAbwesenheiten.OrderBy(x => x.Datum).ThenBy(x => x.Stunde).Select(x => new
            {
                datum = Format.Datum(x.Datum), stunde = x.Stunde, klasse = x.Klasse,
                name = schueler.TryGetValue(x.SchuelerId, out var s) ? $"{(s.Langname.Length > 0 ? s.Langname : s.Kurzname)}, {s.Vorname}".Trim(' ', ',') : x.Name,
                art = x.Verspaetet ? $"verspätet ({x.Minuten} Min.)" : "fehlt", status = x.Status, fach = x.Fach, lehrer = x.Lehrer, bemerkung = x.Bemerkung,
            }).ToList();
        }));

        g.MapGet("/klassenbuch", (Arbeitsdokument a) => a.LesenAsync(doc =>
            doc.Daten.Klassenbuch.OrderBy(x => x.Datum).ThenBy(x => x.Stunde).Select(x => new
            {
                datum = Format.Datum(x.Datum), stunde = x.Stunde, klassen = string.Join(",", x.Klassen), fach = x.Fach, lehrer = x.Lehrer, lehrstoff = x.Lehrstoff,
                hausaufgabe = x.Hausaufgabe, faellig = x.Faellig is DateOnly f ? Format.Datum(f) : "", anwesenheit = x.AnwesenheitGeprueft ? "geprüft" : "",
            }).ToList()));

        // ---- automatischer Abgleich ----
        g.MapGet("/abgleich", (Arbeitsdokument a, UntPlanWebDienst upw) => upw.Abgleich(a.Id));
        g.MapPut("/abgleich", (Arbeitsdokument a, UpwAbgleich e, UntPlanWebDienst upw) =>
        {
            var alt = upw.Abgleich(a.Id);
            e.Zuletzt = alt.Zuletzt;
            e.Ergebnis = alt.Ergebnis;
            upw.AbgleichSpeichern(a.Id, e);
            return upw.Abgleich(a.Id);
        });
        g.MapPost("/abgleich/jetzt", async (Arbeitsdokument a, ClaimsPrincipal p, UntPlanWebDienst upw) =>
        {
            var e = upw.Abgleich(a.Id);
            try { e.Ergebnis = await upw.AbgleichenAsync(a, new UpwAbgleich { Vertretungen = e.Vertretungen, Rueckmeldungen = e.Rueckmeldungen }, p.Benutzername()); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { e.Ergebnis = "Fehler: " + UntPlanWebDienst.Verbindungsfehler(ex); }
            e.Zuletzt = DateTime.Now;
            upw.AbgleichSpeichern(a.Id, e);
            return e;
        });
    }
}
