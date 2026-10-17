using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Schüler-Import (Administration → Import → Schüler*innen; Recht „Import“): CSV lesen (Kodierung und Trennzeichen automatisch),
/// Spaltenzuordnung mit Vorlagen, Trockenlauf und Übernahme. Quelle der Schülerdaten (Export bzw. Web-Verwaltung; Recht „Einstellungen“).
/// </summary>
public static class SchuelerImportApi
{
    public sealed record QuelleDaten(string? Quelle);

    private static Benutzer Import(HttpContext c) =>
        RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminImport, meldung: "Nur für die Administration bzw. mit dem Recht „Import“.");

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/admin/schuelerimport");

        api.MapGet("/grundlagen", (HttpContext c, SchuelerImport si, Paketdienst p, Klassenbuchverwaltung kv) =>
        {
            var b = Import(c);
            return Results.Json(new
            {
                quelle = si.Quelle,
                darfQuelle = Rechtedienst.Darf(b, Recht.AdminEinstellungen),
                darfKonten = Rechtedienst.Darf(b, Recht.AdminBenutzer),
                felder = SchuelerImport.Felder.Select(f => new { f.Id, f.Name, f.Gruppe }),
                vorlagen = si.Vorlagen(),
                schemata = Personendienst.Schemata,
                klassen = p.Aktuell?.Paket.Elemente.Klassen.Select(k => k.Kurz) ?? Enumerable.Empty<string>(),
                schueler = p.Aktuell?.Schueler.Count ?? 0,
                dienste = kv.Dienstarten().Where(d => d.Aktiv).Select(d => new { d.Kurz, d.Name }),
            });
        });

        // Datei lesen: Kodierung, Trennzeichen, Vorschau, Zeilen und Zuordnungsvorschlag
        api.MapPost("/lesen", async (HttpContext c) =>
        {
            Import(c);
            if (!c.Request.HasFormContentType) throw new ArgumentException("Bitte eine CSV- oder TXT-Datei auswählen.");
            var form = await c.Request.ReadFormAsync();
            var datei = form.Files.FirstOrDefault() ?? throw new ArgumentException("Bitte eine CSV- oder TXT-Datei auswählen.");
            if (datei.Length > SchuelerImport.MaxBytes) throw new ArgumentException("Die Datei ist größer als 5 MB.");
            using var ms = new MemoryStream();
            await datei.CopyToAsync(ms);
            var t = SchuelerImport.Lesen(ms.ToArray(), form["kodierung"].FirstOrDefault(), form["trennzeichen"].FirstOrDefault());
            var kopf = SchuelerImport.KopfzeileVermuten(t.Zeilen);
            var spalten = t.Zeilen.Max(z => z.Count);
            return Results.Json(new
            {
                datei = datei.FileName, t.Kodierung, t.Trennzeichen, kopfzeile = kopf, spalten,
                ueberschriften = Enumerable.Range(0, spalten).Select(i => kopf && i < t.Zeilen[0].Count && t.Zeilen[0][i].Length > 0 ? t.Zeilen[0][i] : $"Spalte {i + 1}"),
                vorschlag = kopf ? SchuelerImport.Vorschlag(t.Zeilen[0]) : new Dictionary<string, string>(),
                anzahl = t.Zeilen.Count - (kopf ? 1 : 0),
                vorschau = t.Zeilen.Take(11),
                zeilen = t.Zeilen,
            });
        });

        api.MapPost("/pruefen", (ImportAnfrage a, HttpContext c, SchuelerImport si) =>
        {
            Import(c);
            return Results.Json(si.Pruefen(a));
        });

        api.MapPost("/uebernehmen", (ImportAnfrage a, HttpContext c, SchuelerImport si, Protokoll prot) =>
        {
            var b = Import(c);
            if (a.Optionen is { } o && (o.KontenSchueler || o.KontenEltern)) RechtePruefung.Verlangen(b, Recht.AdminBenutzer, meldung: "Konten anlegen darf nur die Benutzerverwaltung.");
            var e = si.Uebernehmen(b, a);
            prot.Schreiben(b.Login, "Schüler*innen importiert", $"{a.Zeilen?.Count ?? 0} Zeilen, {a.Optionen?.Modus}");
            return Results.Json(e);
        });

        api.MapPost("/vorlagen", (ImportVorlage v, HttpContext c, SchuelerImport si) => { Import(c); return Results.Json(si.VorlageSpeichern(v)); });
        api.MapDelete("/vorlagen/{name}", (string name, HttpContext c, SchuelerImport si) => { Import(c); si.VorlageLoeschen(name); return Results.Json(new { ok = true }); });

        // Quelle der Schülerdaten (Administration → Einstellungen)
        api.MapGet("/quelle", (HttpContext c, SchuelerImport si) =>
        {
            RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminEinstellungen, meldung: "Nur für die Administration.");
            return Results.Json(new { quelle = si.Quelle });
        });
        api.MapPut("/quelle", (QuelleDaten d, HttpContext c, SchuelerImport si, Protokoll prot) =>
        {
            var b = RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminEinstellungen, meldung: "Nur für die Administration.");
            var n = si.QuelleSetzen(d.Quelle ?? "", b.Id);
            prot.Schreiben(b.Login, "Quelle der Schülerdaten", d.Quelle ?? "");
            return Results.Json(new { quelle = si.Quelle, uebernommen = n });
        });
    }
}
