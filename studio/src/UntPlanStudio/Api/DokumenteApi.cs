using System.Security.Claims;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record NeuesDokument(string? Schulname, string? Schulform);
public sealed record DokumentName(string Name);

/// <summary>Stand eines geöffneten Dokuments (Titelleiste, Statuszeile, Schnellzugriff).</summary>
public sealed record DokumentStand(string Id, string Name, long Version, bool Geaendert, DateTime? Gespeichert, bool KannRueckgaengig, string? RueckgaengigText,
    bool KannWiederholen, string Schulname, string Schuljahr, string Periode, bool PeriodeFixiert, string Besitzer, string Herkunft, int Abonnenten, Dictionary<string, int> Zaehler);

public sealed record ImportErgebnis(int Neu, int Aktualisiert, List<string> Hinweise);

/// <summary>
/// Datei-Funktionen: Dokumentliste, Neu, Beispielschule, Hochladen (*.up oder BAL), Speichern, Herunterladen, Schließen,
/// Löschen, Umbenennen, Rückgängig/Wiederholen, BAL-Import in ein geöffnetes Dokument, Grunddaten (Zeitraster) und
/// Ereignisse (Server-Sent Events).
/// </summary>
public sealed class DokumenteApi : IStudioModul
{
    public string Name => "dokumente";

    public static DokumentStand Stand(Arbeitsdokument a, Dokument d)
    {
        var e = d.Daten.Einstellungen;
        var p = d.Daten.AktivePeriode;
        return new DokumentStand(a.Id, a.Info.Name, a.Version, d.IstGeaendert, a.Gespeichert, d.KannRueckgaengig, d.RueckgaengigText, d.KannWiederholen,
            e.Schulname, $"{Format.Datum(e.SchuljahrVon)} – {Format.Datum(e.SchuljahrBis)}", p.Name, p.Fixiert, a.Info.Besitzer, a.Info.Herkunft, a.Abonnenten,
            new Dictionary<string, int>
            {
                ["klassen"] = d.Daten.Klassen.Count, ["lehrer"] = d.Daten.Lehrer.Count, ["raeume"] = d.Daten.Raeume.Count, ["faecher"] = d.Daten.Faecher.Count,
                ["unterrichte"] = d.Daten.Unterrichte.Count, ["planstunden"] = d.Daten.Stundenplan.Count,
                ["nichtVerplant"] = d.Daten.Unterrichte.Sum(u => d.Index.NichtVerplant(u)),
            });
    }

    /// <summary>Zeitraster für Stundenplan-Raster und Zeitwunsch-Fenster.</summary>
    public static object Zeitraster(Schuldatei d)
    {
        var z = d.Zeitraster;
        return new
        {
            tage = Enumerable.Range(0, z.TageProWoche).Select(t => new
            {
                nr = t,
                kurz = Planungsdienst.TageKurz[(z.ErsterSchultag + t) % 7],
                lang = Planungsdienst.TageLang[(z.ErsterSchultag + t) % 7],
            }),
            stunden = Enumerable.Range(1, z.StundenProTag).Select(s => new
            {
                nr = s,
                label = z.StundenLabel(s),
                beginn = z.Zeit(s)?.Beginn.ToString("HH:mm") ?? "",
                ende = z.Zeit(s)?.Ende.ToString("HH:mm") ?? "",
                pauseDanach = z.Pausen.FirstOrDefault(p => p.NachStunde == s) is { } pa ? (pa.Bezeichnung.Length > 0 ? pa.Bezeichnung : $"{pa.DauerMinuten} Min.") : null,
            }),
            // je Tag und Stunde: "V" Vormittag, "N" Nachmittag, "" nicht verfügbar
            belegung = Enumerable.Range(0, z.TageProWoche).Select(t => Enumerable.Range(1, z.StundenProTag)
                .Select(s => z.Art(t, s) switch { Tageszeit.Vormittag => "V", Tageszeit.Nachmittag => "N", _ => "" })),
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        r.Api.MapGet("/dokumente", (ClaimsPrincipal p, Dokumentdienst dienst, Benutzereinstellungen e) =>
        {
            var liste = dienst.Liste();
            var zuletzt = e.Zuletzt(p.Benutzername()).Where(id => liste.Any(x => x.Id == id)).ToList();
            return new { dokumente = liste, zuletzt };
        });

        r.Api.MapPost("/dokumente/neu", async (NeuesDokument n, ClaimsPrincipal p, Dokumentdienst dienst, Benutzereinstellungen e) =>
        {
            var a = dienst.Neu(n.Schulname ?? "", n.Schulform ?? "GY", p.Benutzername());
            e.ZuletztMerken(p.Benutzername(), a.Id);
            return await a.LesenAsync(d => Stand(a, d));
        });

        r.Api.MapPost("/dokumente/beispiel", async (ClaimsPrincipal p, Dokumentdienst dienst, Benutzereinstellungen e) =>
        {
            var a = await Task.Run(() => dienst.Beispiel(p.Benutzername()));
            e.ZuletztMerken(p.Benutzername(), a.Id);
            return await a.LesenAsync(d => Stand(a, d));
        });

        r.Api.MapPost("/dokumente/hochladen", async (HttpRequest req, ClaimsPrincipal p, Dokumentdienst dienst, Benutzereinstellungen e) =>
        {
            var datei = await EineDatei(req);
            var (a, bericht) = dienst.Hochladen(datei.Inhalt, datei.Name, p.Benutzername());
            e.ZuletztMerken(p.Benutzername(), a.Id);
            return new { dokument = await a.LesenAsync(d => Stand(a, d)), bericht = bericht is null ? null : Ergebnis(bericht) };
        }).DisableAntiforgery();

        var d = r.Dokument;
        d.MapGet("", async (Arbeitsdokument a, ClaimsPrincipal p, Benutzereinstellungen e) =>
        {
            e.ZuletztMerken(p.Benutzername(), a.Id);
            return await a.LesenAsync(doc => Stand(a, doc));
        });

        d.MapGet("/grunddaten", (Arbeitsdokument a) => a.LesenAsync(doc => new
        {
            schulname = doc.Daten.Einstellungen.Schulname,
            zeitraster = Zeitraster(doc.Daten),
            perioden = doc.Daten.Perioden.Select(x => new { x.Id, x.Name, x.Langname, x.Von, x.Bis, x.Fixiert, aktiv = x.Id == doc.Daten.AktivePeriode.Id }),
            wochenperiodizitaet = doc.Daten.Einstellungen.Wochenperiodizitaet,
        }));

        d.MapPost("/speichern", async (Arbeitsdokument a, ClaimsPrincipal p) =>
        {
            await a.SpeichernAsync(p.Benutzername(), immer: true);
            return await a.LesenAsync(doc => Stand(a, doc));
        });

        d.MapGet("/herunterladen", async (Arbeitsdokument a) =>
        {
            var inhalt = await a.DateiinhaltAsync();
            var name = string.Concat(a.Info.Name.Split(Path.GetInvalidFileNameChars())).Trim();
            return Results.File(inhalt, "application/octet-stream", (name.Length == 0 ? "Schuldatei" : name) + Dokument.Dateiendung);
        });

        d.MapPost("/schliessen", async (Arbeitsdokument a, ClaimsPrincipal p, Dokumentdienst dienst) =>
        {
            await dienst.SchliessenAsync(a.Id, p.Benutzername());
            return Results.Ok(new { ok = true });
        });

        d.MapDelete("", (Arbeitsdokument a, Dokumentdienst dienst) =>
        {
            dienst.Loeschen(a.Id);
            return Results.Ok(new { ok = true });
        });

        d.MapPut("/name", async (Arbeitsdokument a, DokumentName n, Dokumentdienst dienst) =>
        {
            dienst.Umbenennen(a.Id, n.Name);
            return await a.LesenAsync(doc => Stand(a, doc));
        });

        d.MapPost("/rueckgaengig", (Arbeitsdokument a, ClaimsPrincipal p) => a.AusfuehrenAsync(p.Benutzername(), doc =>
        {
            if (!doc.KannRueckgaengig) return (Stand(a, doc), (AenderungsArt?)null, "");
            var text = doc.RueckgaengigText ?? "";
            doc.Rueckgaengig();
            return (Stand(a, doc), AenderungsArt.Alles, "Rückgängig: " + text);
        }));

        d.MapPost("/wiederholen", (Arbeitsdokument a, ClaimsPrincipal p) => a.AusfuehrenAsync(p.Benutzername(), doc =>
        {
            if (!doc.KannWiederholen) return (Stand(a, doc), (AenderungsArt?)null, "");
            doc.Wiederholen();
            return (Stand(a, doc), AenderungsArt.Alles, "Wiederholen");
        }));

        d.MapPost("/bal-import", async (Arbeitsdokument a, HttpRequest req, ClaimsPrincipal p) =>
        {
            var datei = await EineDatei(req);
            var text = Dokumentdienst.Text(datei.Inhalt);
            if (!BalImport.IstBal(text)) throw StudioFehler.Ungueltig("Die Datei ist keine BAL-Datei (WILLI2-Stundenplan).");
            var f = req.Form;
            bool Option(string name, bool standard) => f.TryGetValue(name, out var v) ? v.ToString() is "true" or "1" or "on" : standard;
            var o = new BalOptionen
            {
                Zeitraster = Option("zeitraster", true), Stammdaten = Option("stammdaten", true), UnterrichtErsetzen = Option("unterrichtErsetzen", true),
                Stundenplan = Option("stundenplan", true), Pausenaufsichten = Option("pausenaufsichten", true), Zeitwuensche = Option("zeitwuensche", true),
            };
            ImportBericht? bericht = null;
            // wie im Desktop: eine Rückgängig-Stufe „BAL-Import“
            await a.AendernAsync(p.Benutzername(), "BAL-Import", AenderungsArt.Alles, s => bericht = BalImport.Importieren(s, text, o));
            return Ergebnis(bericht!);
        }).DisableAntiforgery();

        d.MapGet("/ereignisse", Ereignisse);
    }

    private static ImportErgebnis Ergebnis(ImportBericht b) => new(b.Neu, b.Aktualisiert, b.Hinweise.ToList());

    private sealed record HochgeladeneDatei(string Name, byte[] Inhalt);

    private static async Task<HochgeladeneDatei> EineDatei(HttpRequest req)
    {
        if (!req.HasFormContentType) throw StudioFehler.Ungueltig("Bitte eine Datei hochladen (multipart/form-data, Feld \"datei\").");
        var form = await req.ReadFormAsync();
        var datei = form.Files["datei"] ?? form.Files.FirstOrDefault() ?? throw StudioFehler.Ungueltig("Es wurde keine Datei übertragen.");
        using var ms = new MemoryStream();
        await datei.CopyToAsync(ms);
        return new HochgeladeneDatei(datei.FileName, ms.ToArray());
    }

    /// <summary>
    /// Server-Sent Events eines Dokuments: <c>event: aenderung|gespeichert|geschlossen</c>, Daten = <see cref="StudioEreignis"/>.
    /// Erstes Ereignis <c>hallo</c> mit dem aktuellen Stand; alle 25 Sekunden ein Kommentar gegen Verbindungsabbrüche.
    /// </summary>
    private static async Task Ereignisse(Arbeitsdokument a, HttpContext ctx, CancellationToken ct)
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        var leser = a.Abonnieren(out var abmelden);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task Senden(string art, object daten)
        {
            await ctx.Response.WriteAsync($"event: {art}\ndata: {JsonSerializer.Serialize(daten, json)}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
        try
        {
            await Senden("hallo", new StudioEreignis("hallo", a.Version, "", "", ctx.User.Benutzername(), DateTime.Now));
            while (!ct.IsCancellationRequested)
            {
                var warten = leser.WaitToReadAsync(ct).AsTask();
                if (await Task.WhenAny(warten, Task.Delay(TimeSpan.FromSeconds(25), ct)) == warten)
                {
                    if (!await warten) break;
                    while (leser.TryRead(out var e)) await Senden(e.Art, e);
                }
                else
                {
                    await ctx.Response.WriteAsync(": ping\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { abmelden(); }
    }
}
