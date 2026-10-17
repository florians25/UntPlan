using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>Ergebnis eines Imports (wie <see cref="ImportBericht"/> im Desktop: „3 neu, 5 aktualisiert“ + Hinweise).</summary>
public sealed record AustauschBericht(string Art, int Neu, int Aktualisiert, List<string> Hinweise, string Text);

/// <summary>Hochgeladene Dateien einer multipart-Anfrage (Feld „datei“, auch mehrfach).</summary>
public static class AustauschUpload
{
    public sealed record Datei(string Name, byte[] Inhalt)
    {
        /// <summary>Text wie im Desktop (<see cref="Csv.DateiLesen"/>): UTF-8, sonst Windows-1252/Latin-1.</summary>
        public string Text => Dokumentdienst.Text(Inhalt);
    }

    public static async Task<List<Datei>> LesenAsync(HttpRequest req)
    {
        if (!req.HasFormContentType) throw StudioFehler.Ungueltig("Bitte eine Datei hochladen (multipart/form-data, Feld \"datei\").");
        var form = await req.ReadFormAsync();
        var liste = new List<Datei>();
        foreach (var f in form.Files)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            liste.Add(new Datei(Path.GetFileName(f.FileName), ms.ToArray()));
        }
        if (liste.Count == 0) throw StudioFehler.Ungueltig("Es wurde keine Datei übertragen.");
        return liste;
    }

    public static async Task<Datei> EineAsync(HttpRequest req) => (await LesenAsync(req))[0];

    /// <summary>Dateiname ohne unzulässige Zeichen (für Content-Disposition).</summary>
    public static string Dateiname(string name, string standard) =>
        string.Concat((name ?? "").Split(Path.GetInvalidFileNameChars())).Trim() is { Length: > 0 } n ? n : standard;
}

/// <summary>
/// Modul Import/Export (Desktop: Datei → Import/Export, Module → Abteilungsplanung): Schild-NRW-CSV (Lehrer, Klassen),
/// Schüler mit Kurswahlen (CSV), GPU-Dateien (Import und Export), Unterrichtsverteilung (CSV), Info-Stundenplan (HTML, ZIP),
/// Lehrer-Kalender (iCal), UntPlanWeb-Paket (JSON), Abteilungsdatei exportieren/zusammenführen. Schuldatei (.up) und BAL:
/// Hoch-/Herunterladen und BAL-Import stellt das Modul <c>dokumente</c> bereit; die Oberfläche fasst alles in einem Fenster zusammen.
/// </summary>
public sealed class AustauschApi : IStudioModul
{
    public string Name => "austausch";

    private static AustauschBericht Bericht(string art, ImportBericht b) => new(art, b.Neu, b.Aktualisiert, b.Hinweise.ToList(), $"{art}: {b.Neu} neu, {b.Aktualisiert} aktualisiert");

    private static IResult Zip(IEnumerable<(string Name, byte[] Inhalt)> dateien, string name)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, inhalt) in dateien)
            {
                var e = zip.CreateEntry(n, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(inhalt);
            }
        return Results.File(ms.ToArray(), "application/zip", name);
    }

    private static string Schulname(Schuldatei d) => AustauschUpload.Dateiname(d.Einstellungen.Schulname, "Schule");

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/austausch");

        // Übersicht für das Fenster (Auswahllisten, Zähler)
        g.MapGet("", (Arbeitsdokument a) => a.LesenAsync(doc => new
        {
            schulname = doc.Daten.Einstellungen.Schulname,
            abteilungen = Stammdaten.Sortiert(doc.Daten.Abteilungen).Select(x => new { x.Id, kurz = x.Kurzname, lang = x.Langname, unterrichte = doc.Daten.Unterrichte.Count(u => Abteilungen.GehoertZu(doc.Daten, u, x.Id)) }),
            lehrer = Stammdaten.Sortiert(doc.Daten.Lehrer).Select(x => new { x.Id, kurz = x.Kurzname, lang = x.VollerName }),
            zaehler = new { lehrer = doc.Daten.Lehrer.Count, klassen = doc.Daten.Klassen.Count, schueler = doc.Daten.Schueler.Count, unterrichte = doc.Daten.Unterrichte.Count, stunden = doc.Daten.Stundenplan.Count },
        }));

        // ---- Importe (je eine Rückgängig-Stufe wie im Desktop) ----
        g.MapPost("/csv/{art}", async (Arbeitsdokument a, string art, HttpRequest req, ClaimsPrincipal p) =>
        {
            var datei = await AustauschUpload.EineAsync(req);
            var text = datei.Text;
            ImportBericht? b = null;
            switch (art.ToLowerInvariant())
            {
                case "lehrer":
                    await a.AendernAsync(p.Benutzername(), "Import Lehrer", AenderungsArt.Stammdaten, d => b = Import.LehrerCsv(d, text));
                    return Bericht("Import Lehrer", b!);
                case "klassen":
                    await a.AendernAsync(p.Benutzername(), "Import Klassen", AenderungsArt.Stammdaten, d => b = Import.KlassenCsv(d, text));
                    return Bericht("Import Klassen", b!);
                case "schueler":
                    await a.AendernAsync(p.Benutzername(), "Import Schüler", AenderungsArt.Stammdaten, d => b = Import.SchuelerCsv(d, text));
                    return Bericht("Import Schüler", b!);
                default:
                    throw StudioFehler.Ungueltig($"Unbekannte Importart „{art}“ (lehrer, klassen, schueler).");
            }
        }).DisableAntiforgery();

        g.MapPost("/gpu", async (Arbeitsdokument a, HttpRequest req, ClaimsPrincipal p) =>
        {
            var dateien = await AustauschUpload.LesenAsync(req);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in dateien)
            {
                if (f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = new ZipArchive(new MemoryStream(f.Inhalt), ZipArchiveMode.Read);
                    foreach (var e in zip.Entries.Where(e => e.Length > 0))
                    {
                        using var s = e.Open();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        dict[Path.GetFileName(e.FullName)] = Dokumentdienst.Text(ms.ToArray());
                    }
                }
                else dict[f.Name] = f.Text;
            }
            if (!dict.Keys.Any(k => k.Contains("GPU", StringComparison.OrdinalIgnoreCase)))
                throw StudioFehler.Ungueltig("Es wurden keine GPU-Dateien (GPU001.TXT … GPU006.TXT) übertragen.");
            ImportBericht? b = null;
            await a.AendernAsync(p.Benutzername(), "GPU-Import", AenderungsArt.Alles, d => b = Import.Gpu(d, dict));
            return Bericht("GPU-Import", b!);
        }).DisableAntiforgery();

        g.MapPost("/abteilung/{id:int}", async (Arbeitsdokument a, int id, HttpRequest req, ClaimsPrincipal p) =>
        {
            var datei = await AustauschUpload.EineAsync(req);
            Schuldatei teil;
            try { teil = JsonSerializer.Deserialize<Schuldatei>(datei.Text, Dokument.JsonOptionen) ?? throw new InvalidDataException(); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw StudioFehler.Ungueltig("Die Datei ist keine gültige UntPlan-Schuldatei (.up)."); }
            var name = await a.LesenAsync(doc => doc.Daten.Abteilungen.FirstOrDefault(x => x.Id == id)?.Kurzname) ?? throw StudioFehler.NichtGefunden($"Die Abteilung Nr. {id}");
            var n = 0;
            await a.AendernAsync(p.Benutzername(), "Abteilung zusammenführen", AenderungsArt.Alles, d => n = Abteilungen.Zusammenfuehren(d, teil, id));
            return new AustauschBericht("Abteilung zusammenführen", 0, n, new(), $"{n} Unterrichte der Abteilung {name} übernommen.");
        }).DisableAntiforgery();

        // ---- Exporte (Download) ----
        g.MapGet("/gpu", (Arbeitsdokument a) => a.LesenAsync(doc =>
            Zip(Export.Gpu(doc.Daten).Select(kv => (kv.Key, Encoding.Latin1.GetBytes(kv.Value))), $"GPU_{Schulname(doc.Daten)}.zip")));

        g.MapGet("/unterricht.csv", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var rows = new List<object?[]> { new object?[] { "U-Nr", "Wst", "Fach", "Lehrer", "Klassen", "Fachraum", "Stammraum", "Dopp.Std.", "Block", "Band", "Kurs" } };
            foreach (var u in d.Unterrichte)
                foreach (var z in u.Zeilen)
                    rows.Add(new object?[] { u.Id, u.Wochenstunden, ix.KFach(z.FachId), ix.KLehrer(z.LehrerId), ix.KKlassen(z.KlassenIds), ix.KRaum(z.FachraumId), ix.KRaum(z.StammraumId), u.Doppelstunden.ToString(), u.Block, u.Band, u.Kursname });
            return Results.File(Encoding.UTF8.GetBytes(Csv.Schreiben(rows)), "text/csv; charset=utf-8", "Unterrichtsverteilung.csv");
        }));

        g.MapGet("/infostundenplan.zip", (Arbeitsdokument a) => a.LesenAsync(doc =>
            Zip(Export.InfoStundenplan(doc.Daten).Select(kv => (kv.Key, Encoding.UTF8.GetBytes(kv.Value))), $"Info-Stundenplan_{Schulname(doc.Daten)}.zip")));

        g.MapGet("/ical/{lehrer:int}", (Arbeitsdokument a, int lehrer, DateOnly? von, DateOnly? bis) => a.LesenAsync(doc =>
        {
            var l = doc.Daten.Lehrer.FirstOrDefault(x => x.Id == lehrer) ?? throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {lehrer}");
            var start = von ?? DateOnly.FromDateTime(DateTime.Today);
            var ende = bis ?? start.AddDays(27);
            if (ende < start) throw StudioFehler.Ungueltig("Das Ende liegt vor dem Anfang.");
            return Results.File(Encoding.UTF8.GetBytes(Export.ICal(doc.Daten, l.Id, start, ende)), "text/calendar; charset=utf-8", $"Stundenplan_{AustauschUpload.Dateiname(l.Kurzname, "Lehrer")}.ics");
        }));

        g.MapGet("/untplanweb.json", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var paket = UntPlanWebExport.PaketErstellen(doc.Daten, DateOnly.FromDateTime(DateTime.Today), WebExportOptionen.Aus(doc.Daten.Einstellungen.Portal));
            return Results.File(Encoding.UTF8.GetBytes(UntPlanWebExport.Json(paket)), "application/json", $"untplanweb_{DateTime.Now:yyyyMMdd_HHmm}.json");
        }));

        g.MapGet("/abteilung/{id:int}", (Arbeitsdokument a, int id) => a.LesenAsync(doc =>
        {
            var ab = doc.Daten.Abteilungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Abteilung Nr. {id}");
            var teil = Abteilungen.Exportieren(doc.Daten, id);
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(teil, Dokument.JsonOptionen), "application/octet-stream",
                $"Abteilung_{AustauschUpload.Dateiname(ab.Kurzname, "Abteilung")}{Dokument.Dateiendung}");
        }));
    }
}
