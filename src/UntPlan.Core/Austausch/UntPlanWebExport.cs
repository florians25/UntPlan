using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;

namespace UntPlan.Core.Austausch;

/// <summary>Datenschutz- und Umfangsoptionen für den Export an UntPlanWeb.</summary>
public sealed class WebExportOptionen
{
    public int TageVoraus { get; set; } = 14;
    public int TageZurueck { get; set; } = 7;
    /// <summary>E-Mail-Adressen der Lehrkräfte übertragen.</summary>
    public bool LehrerEmail { get; set; }
    /// <summary>Lehrer-Absenzen übertragen (nur Zeitraum, ohne Grund).</summary>
    public bool LehrerAbsenzen { get; set; } = true;
    /// <summary>Absenzgründe übertragen (vertrauliche Gründe nie).</summary>
    public bool Absenzgruende { get; set; }
    /// <summary>Zu übertragende Teile (WebTeile); leer = alles.</summary>
    public HashSet<string> Teile { get; set; } = new();
    public bool IgnoriertenUnterricht { get; set; }
    public bool Ferien { get; set; } = true;
    /// <summary>Schülerliste übertragen (Klassenbuch, Fehlzeiten).</summary>
    public bool Schueler { get; set; }

    public bool Mit(string teil) => Teile.Count == 0 || Teile.Contains(teil);

    /// <summary>Optionen aus den UntPlanWeb-Einstellungen der Schuldatei.</summary>
    public static WebExportOptionen Aus(PortalEinstellungen p, params string[] teile) => new()
    {
        TageVoraus = Math.Max(1, p.TageVoraus), LehrerEmail = p.LehrerEmail, LehrerAbsenzen = p.LehrerAbsenzen, Absenzgruende = p.Absenzgruende,
        IgnoriertenUnterricht = p.IgnoriertenUnterrichtUebertragen, Ferien = !p.FerienNichtExportieren, Schueler = p.SchuelerExportieren, Teile = teile.ToHashSet(),
    };
}

/// <summary>Ergebnis eines Klassenbuch-Imports aus UntPlanWeb (Studenten*, Kurse).</summary>
public sealed record WebImportErgebnis(int Neu, int Geaendert, int Entfernt, int Abgelehnt)
{
    public override string ToString() =>
        $"{Neu} neu, {Geaendert} geändert" + (Entfernt > 0 ? $", {Entfernt} entfernt" : "") + (Abgelehnt > 0 ? $", {Abgelehnt} abgelehnt (unbekannte Schüler oder ungültiges Datum)" : "");
}

/// <summary>
/// Export an UntPlanWeb (Schema untplanweb/1, Verträge in UntPlan.Web.Contracts): erzeugt das vollständige Paket, schreibt
/// es als Datei oder überträgt es per HTTPS; holt Rückmeldungen (Krankmeldungen, Nachrichten) und übernimmt sie.
/// </summary>
public static class UntPlanWebExport
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static string Farbe(int argb) => argb == 0 ? "" : $"#{argb & 0xFFFFFF:X6}";

    public static WebPaket PaketErstellen(Schuldatei d, DateOnly ab, WebExportOptionen? o = null)
    {
        o ??= new WebExportOptionen();
        var ix = new DatenIndex(d);
        var von = ab.AddDays(-Math.Max(0, o.TageZurueck));
        var bis = ab.AddDays(Math.Max(1, o.TageVoraus));
        string K(ElementTyp t, int? id) => id is int i ? ix.K(t, i) : "";
        List<string> Kl(IEnumerable<int> ids) => ids.Select(i => ix.K(ElementTyp.Klasse, i)).Where(x => x.Length > 0).ToList();

        var tage = new List<WebTag>();
        var vertretungen = new List<WebVertretung>();
        var mitVertretungen = o.Mit(WebTeile.Vertretungen);
        var mitUnterricht = o.Mit(WebTeile.Unterricht);
        foreach (var t in mitVertretungen ? Kalender.Tage(von, bis) : Enumerable.Empty<DateOnly>())
        {
            var unterrichtstag = Kalender.IstUnterrichtstag(d, t);
            var tag = new WebTag { Datum = D(t), Woche = Kalender.WochenTyp(d, t), Unterrichtstag = unterrichtstag };
            if (unterrichtstag)
            {
                var tp = Tagesplan.Berechnen(d, t);
                foreach (var e in tp.Eintraege.OrderBy(e => e.Stunde))
                    tag.Stunden.Add(new WebStunde
                    {
                        Stunde = e.Stunde, Unterricht = e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId, Fach = K(ElementTyp.Fach, e.FachId),
                        Klassen = Kl(e.KlassenIds), Lehrer = e.LehrerIds.Select(ix.KLehrer).ToList(), Raeume = e.RaumIds.Select(ix.KRaum).ToList(),
                        Status = e.Status switch
                        {
                            TagesStatus.Vertreten => "vertreten", TagesStatus.Offen => "offen", TagesStatus.Entfall => "entfall",
                            TagesStatus.Zusaetzlich => "zusaetzlich", TagesStatus.Frei => "frei", _ => "regulaer",
                        },
                        Vertretung = e.Vertretung?.Id, Text = e.Vertretung?.Text ?? "",
                    });
                foreach (var z in Vertretungsplan.Zeilen(d, t))
                    vertretungen.Add(new WebVertretung
                    {
                        Id = z.Id, Datum = D(t), Stunde = z.Stunde, Art = z.Art, Klassen = Kl(z.KlassenIds), Lehrer = z.Lehrer, Vertreter = z.Vertreter,
                        Fach = z.Fach, NeuesFach = z.NeuesFach, Raum = z.Raum, NeuerRaum = z.NeuerRaum, VertrVon = z.VertrVon, Nach = z.Nach, Text = z.Text,
                        NurLehrer = z.Art == Vertretungsplan.ArtText(d, VertretungsArt.Pausenaufsicht),
                    });
            }
            tage.Add(tag);
        }

        var gruende = d.Abwesenheitsgruende.ToDictionary(g => g.Id);
        string? Grund(int? id) => o.Absenzgruende && id is int g && gruende.TryGetValue(g, out var x) && !x.Vertraulich ? (x.Langname.Length > 0 ? x.Langname : x.Kurzname) : null;

        return new WebPaket
        {
            Erzeugt = DateTime.Now,
            Revision = d.Einstellungen.Portal.Revision + 1,
            Programmversion = typeof(UntPlanWebExport).Assembly.GetName().Version?.ToString() ?? "1.0",
            Teile = o.Teile.Count == 0 ? new List<string>() : o.Teile.OrderBy(x => x).ToList(),
            Von = D(von),
            Bis = D(bis),
            Schule = new WebSchule
            {
                Name = d.Einstellungen.Schulname, Schulnummer = d.Einstellungen.Schulnummer, Schulform = Nrw.Schulformen.GetValueOrDefault(d.Einstellungen.Schulform, d.Einstellungen.Schulform),
                SchuljahrVon = D(d.Einstellungen.SchuljahrVon), SchuljahrBis = D(d.Einstellungen.SchuljahrBis), Wochenperiodizitaet = Math.Max(1, d.Einstellungen.Wochenperiodizitaet),
            },
            Zeitraster = new WebZeitraster
            {
                TageProWoche = d.Zeitraster.TageProWoche,
                Stunden = d.Zeitraster.Zeiten.Select(z => new WebStundenzeit(z.Nr, z.Bezeichnung, z.Beginn.ToString("HH:mm"), z.Ende.ToString("HH:mm"))).ToList(),
                Pausen = d.Zeitraster.Pausen.Select(p => new WebPause(p.NachStunde, p.Bezeichnung, p.DauerMinuten)).ToList(),
            },
            Elemente = new WebElemente
            {
                Klassen = d.Klassen.Where(k => !k.Ignorieren).Select(k => new WebKlasse
                {
                    Id = k.Id, Kurz = k.Kurzname, Name = k.Langname, Farbe = Farbe(k.Hintergrund), Jahrgang = k.Jahrgang,
                    Klassenleitung = k.KlassenlehrerIds.Select(ix.KLehrer).ToList(), Stammraum = K(ElementTyp.Raum, k.StammraumId),
                }).ToList(),
                Lehrer = d.Lehrer.Where(l => !l.Ignorieren).Select(l => new WebLehrer
                {
                    Id = l.Id, Kurz = l.Kurzname, Name = l.Langname, Vorname = l.Vorname, Farbe = Farbe(l.Hintergrund),
                    Email = o.LehrerEmail && l.Email.Length > 0 ? l.Email : null, Faecher = l.FaecherIds.Select(f => ix.KFach(f)).ToList(),
                }).ToList(),
                Raeume = d.Raeume.Where(r => !r.Ignorieren).Select(r => new WebRaum
                {
                    Id = r.Id, Kurz = r.Kurzname, Name = r.Langname, Farbe = Farbe(r.Hintergrund), Buchbar = r.Buchbar, Raumart = r.Raumart, Kapazitaet = r.Kapazitaet,
                }).ToList(),
                Schueler = !o.Schueler ? new List<WebSchueler>() : d.Schueler.Where(x => !x.Ignorieren && x.KlasseId.HasValue)
                    .Select(x => new WebSchueler(x.Id, x.Vorname, x.Langname.Length > 0 ? x.Langname : x.Kurzname, K(ElementTyp.Klasse, x.KlasseId))).ToList(),
                Faecher = d.Faecher.Select(f => new WebElement { Id = f.Id, Kurz = f.Kurzname, Name = f.Langname, Farbe = Farbe(f.Hintergrund) }).ToList(),
                Gaenge = d.Gaenge.Select(g => new WebElement { Id = g.Id, Kurz = g.Kurzname, Name = g.Langname }).ToList(),
            },
            Perioden = !mitUnterricht ? new List<WebPeriode>() : d.Perioden.OrderBy(p => p.Von).Select(p => new WebPeriode
            {
                Id = p.Id, Name = p.Name, Von = D(p.Von), Bis = D(Module.Perioden.Ende(d, p)), MutterId = p.MutterId,
                Unterricht = (p.Unterrichte ?? new()).Where(u => o.IgnoriertenUnterricht || !u.Ignorieren).Select(u => new WebUnterricht(u.Id, u.Wochenstunden,
                    u.Zeilen.Select(z => new WebZeile(K(ElementTyp.Fach, z.FachId), K(ElementTyp.Lehrer, z.LehrerId), Kl(z.KlassenIds), z.Zeilentext)).ToList(),
                    u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => u.UGruppeId is int g ? d.UGruppen.FirstOrDefault(x => x.Id == g)?.Wochen ?? "" : "" },
                    u.Kursname, u.Text)).ToList(),
                Stundenplan = p.Plan.Select(s => new WebPlanstunde(s.UnterrichtId, s.Tag, s.Stunde, s.Raeume.Where(r => r.HasValue).Select(r => ix.KRaum(r)).ToList())).ToList(),
            }).ToList(),
            Tage = tage,
            Vertretungen = vertretungen,
            Absenzen = !mitVertretungen ? new List<WebAbsenz>() : d.Abwesenheiten.Where(a => a.Bis >= von && a.Von <= bis && (o.LehrerAbsenzen || a.Art != AbwesenheitsArt.Lehrer)).Select(a => new WebAbsenz
            {
                Art = a.Art switch { AbwesenheitsArt.Lehrer => "lehrer", AbwesenheitsArt.Klasse => "klasse", _ => "raum" },
                Element = ix.K(a.Art switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum }, a.ElementId),
                Von = D(a.Von), Bis = D(a.Bis), StundeVon = a.StundeVon, StundeBis = Math.Min(a.StundeBis, d.Zeitraster.StundenProTag), Grund = Grund(a.GrundId),
            }).ToList(),
            Termine = !mitVertretungen ? new List<WebTermin>() : d.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= bis).Select(v => new WebTermin
                {
                    Art = "veranstaltung", Titel = v.Text, Von = D(v.Von), Bis = D(v.Bis), StundeVon = v.StundeVon, StundeBis = Math.Min(v.StundeBis, d.Zeitraster.StundenProTag),
                    Klassen = Kl(v.KlassenIds), Lehrer = v.LehrerIds.Select(ix.KLehrer).ToList(), Raeume = v.RaumIds.Select(ix.KRaum).ToList(),
                })
                .Concat(d.Klausuren.Where(k => k.Aktiv && k.Datum >= von && k.Datum <= bis).Select(k => new WebTermin
                {
                    Art = "klausur", Titel = k.Name, Von = D(k.Datum), Bis = D(k.Datum), StundeVon = k.StundeVon, StundeBis = k.StundeBis,
                    Klassen = Kl(k.KlassenIds), Lehrer = k.LehrerIds.Select(ix.KLehrer).ToList(), Raeume = k.RaumIds.Select(ix.KRaum).ToList(),
                })).ToList(),
            Tagestexte = !mitVertretungen ? new List<WebTagestext>() : d.Tagestexte.Where(t => t.Portal && t.Datum.AddDays(Math.Max(1, t.GueltigTage)) > von && t.Datum <= bis).Select(t =>
            {
                var art = t.NurLehrer ? TagestextArt.NurLehrer : t.Art;
                var typ = art == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
                return new WebTagestext(D(t.Datum), Math.Max(1, t.GueltigTage), t.Text,
                    art switch { TagestextArt.Klassen => "klassen", TagestextArt.Lehrer => "lehrer", TagestextArt.NurLehrer => "kollegium", _ => "alle" },
                    t.ElementIds.Select(id => ix.K(typ, id)).ToList());
            }).ToList(),
            Ferien = !o.Ferien ? new List<WebFerien>() : d.Ferien.Select(f => new WebFerien(f.Langname.Length > 0 ? f.Langname : f.Kurzname, D(f.Von), D(f.Bis), f.Feiertag)).ToList(),
            Pausenaufsichten = !mitUnterricht ? new List<WebAufsicht>() : d.Pausenaufsichten.Where(a => a.Datum is null).Select(a => new WebAufsicht(ix.K(ElementTyp.Gang, a.GangId), a.Tag, a.NachStunde, ix.KLehrer(a.LehrerId),
                a.UGruppeId is int g ? d.UGruppen.FirstOrDefault(x => x.Id == g)?.Wochen ?? "" : "")).ToList(),
        };
    }

    public static string Json(WebPaket p) => JsonSerializer.Serialize(p, UntPlanWebSchema.Json);

    public static WebPaket? Lesen(string json) => JsonSerializer.Deserialize<WebPaket>(json, UntPlanWebSchema.Json);

    private static HttpClient Client(PortalEinstellungen p)
    {
        var c = new HttpClient { BaseAddress = new Uri(p.Adresse.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", p.ApiSchluessel);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("UntPlan-Desktop/1.0");
        return c;
    }

    /// <summary>Paket an UntPlanWeb übertragen (PUT …/untplanweb).</summary>
    public static async Task<WebAntwort> HochladenAsync(PortalEinstellungen p, WebPaket paket, CancellationToken ct = default)
    {
        using var c = Client(p);
        using var r = await c.PutAsJsonAsync(UntPlanWebSchema.Pfade.Paket(p.SchulId), paket, UntPlanWebSchema.Json, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Server kennt bereits eine neuere Revision (z. B. andere Arbeitsstation, manueller Upload): einmal mit der nächsten Revision senden
            var stand = await LesenOderNull(r, ct);
            if (stand != null && stand.Revision >= paket.Revision)
            {
                using var r2 = await c.PutAsJsonAsync(UntPlanWebSchema.Pfade.Paket(p.SchulId), paket with { Revision = stand.Revision + 1 }, UntPlanWebSchema.Json, ct);
                if (!r2.IsSuccessStatusCode) throw new InvalidOperationException($"UntPlanWeb antwortet mit HTTP {(int)r2.StatusCode}");
                return (await r2.Content.ReadFromJsonAsync<WebAntwort>(UntPlanWebSchema.Json, ct))!;
            }
        }
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"UntPlanWeb antwortet mit HTTP {(int)r.StatusCode}");
        return (await r.Content.ReadFromJsonAsync<WebAntwort>(UntPlanWebSchema.Json, ct))!;
    }

    private static async Task<WebAntwort?> LesenOderNull(HttpResponseMessage r, CancellationToken ct)
    {
        try { return await r.Content.ReadFromJsonAsync<WebAntwort>(UntPlanWebSchema.Json, ct); }
        catch (Exception) { return null; }
    }

    /// <summary>Verbindung und Berechtigung prüfen (Abruf der Rückmeldungen ohne Übernahme).</summary>
    public static async Task<string> VerbindungPruefenAsync(PortalEinstellungen p, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(p.Adresse)) throw new InvalidOperationException("Keine Serveradresse eingetragen.");
        using var c = Client(p);
        c.Timeout = TimeSpan.FromSeconds(15);
        using var r = await c.GetAsync(UntPlanWebSchema.Pfade.Rueckmeldungen(p.SchulId), ct);
        return (int)r.StatusCode switch
        {
            >= 200 and < 300 => "Verbindung erfolgreich.",
            401 or 403 => throw new InvalidOperationException("Der Server lehnt die Anmeldung ab (Schul-ID oder API-Schlüssel falsch)."),
            404 => throw new InvalidOperationException("Die Schule ist auf dem Server nicht bekannt (Schul-ID prüfen)."),
            var x => throw new InvalidOperationException($"Der Server antwortet mit HTTP {x}."),
        };
    }

    /// <summary>Schulname laut UntPlanWeb (Status-Endpunkt, ohne Anmeldung); null, wenn der Server nicht erreichbar ist.</summary>
    public static async Task<string?> SchulnameAsync(PortalEinstellungen p, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(p.Adresse)) return null;
        try
        {
            using var c = Client(p);
            c.Timeout = TimeSpan.FromSeconds(8);
            var status = await c.GetFromJsonAsync<JsonElement>(UntPlanWebSchema.Pfade.Status, ct);
            return status.TryGetProperty("schule", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } name ? name : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException) { return null; }
    }

    public static async Task<List<WebRueckmeldung>> RueckmeldungenAsync(PortalEinstellungen p, CancellationToken ct = default)
    {
        using var c = Client(p);
        return await c.GetFromJsonAsync<List<WebRueckmeldung>>(UntPlanWebSchema.Pfade.Rueckmeldungen(p.SchulId), UntPlanWebSchema.Json, ct) ?? new();
    }

    public static async Task QuittierenAsync(PortalEinstellungen p, IEnumerable<string> ids, CancellationToken ct = default)
    {
        using var c = Client(p);
        using var r = await c.PostAsJsonAsync(UntPlanWebSchema.Pfade.Quittieren(p.SchulId), ids.ToArray(), UntPlanWebSchema.Json, ct);
        r.EnsureSuccessStatusCode();
    }

    /// <summary>Im Klassenbuch erfasste Schüler-Fehlzeiten ab einem Datum abholen (Import „Studenten*“).</summary>
    public static async Task<List<WebFehlzeit>> FehlzeitenAsync(PortalEinstellungen p, DateOnly ab, DateOnly? bis = null, CancellationToken ct = default)
    {
        using var c = Client(p);
        return await c.GetFromJsonAsync<List<WebFehlzeit>>(UntPlanWebSchema.Pfade.Fehlzeiten(p.SchulId, D(ab), bis is DateOnly b ? D(b) : null), UntPlanWebSchema.Json, ct) ?? new();
    }

    /// <summary>Klassenbuch (Lehrstoff, Hausaufgaben je Unterricht) ab einem Datum abholen (Import „Kurse“).</summary>
    public static async Task<List<WebKlassenbucheintrag>> KlassenbuchAsync(PortalEinstellungen p, DateOnly ab, DateOnly? bis = null, CancellationToken ct = default)
    {
        using var c = Client(p);
        return await c.GetFromJsonAsync<List<WebKlassenbucheintrag>>(UntPlanWebSchema.Pfade.Klassenbuch(p.SchulId, D(ab), bis is DateOnly b ? D(b) : null), UntPlanWebSchema.Json, ct) ?? new();
    }

    private static DateOnly? Datum(string? text) =>
        DateOnly.TryParseExact(text ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Fehlzeiten als Schülerabwesenheiten übernehmen, ohne Doppelimport. Abgleich über Datum, Stunde und Schüler-Id (auf dem
    /// Server eindeutig; die Server-Id ändert sich, wenn eine Lehrkraft die Anwesenheit erneut speichert):
    /// <list type="bullet">
    /// <item>Neue Fehlzeiten werden angelegt, bekannte aktualisiert (z. B. entschuldigt).</item>
    /// <item>Im abgefragten Zeitraum auf dem Server gelöschte Fehlzeiten werden entfernt.</item>
    /// <item>Abgelehnt werden Fehlzeiten unbekannter Schülerinnen/Schüler oder mit ungültigem Datum.</item>
    /// </list>
    /// </summary>
    public static WebImportErgebnis FehlzeitenUebernehmen(Schuldatei d, IEnumerable<WebFehlzeit> liste, DateOnly ab, DateOnly? bis = null)
    {
        var vorhanden = d.SchuelerAbwesenheiten.GroupBy(a => (a.Datum, a.Stunde, a.SchuelerId)).ToDictionary(g => g.Key, g => g.First());
        var schueler = d.Schueler.ToDictionary(x => x.Id);
        var klassen = d.Klassen.ToDictionary(k => k.Id, k => k.Kurzname);
        var gemeldet = new HashSet<SchuelerAbwesenheit>(ReferenceEqualityComparer.Instance);
        int neu = 0, geaendert = 0, abgelehnt = 0;
        foreach (var f in liste)
        {
            if (Datum(f.Datum) is not DateOnly datum || !schueler.TryGetValue(f.SchuelerId, out var sch)) { abgelehnt++; continue; }
            var name = f.Schueler.Length > 0 ? f.Schueler : $"{(sch.Langname.Length > 0 ? sch.Langname : sch.Kurzname)}, {sch.Vorname}";
            var klasse = f.Klasse.Length > 0 ? f.Klasse : sch.KlasseId is int k ? klassen.GetValueOrDefault(k, "") : "";
            var verspaetet = f.Art == "verspaetet";
            var minuten = verspaetet ? f.Minuten : 0;
            var status = f.Status is "entschuldigt" or "unentschuldigt" ? f.Status : "offen";
            int? unterricht = f.Unterricht > 0 ? f.Unterricht : null;
            if (vorhanden.TryGetValue((datum, f.Stunde, f.SchuelerId), out var a))
            {
                if (gemeldet.Contains(a)) continue; // doppelt geliefert
                gemeldet.Add(a);
                if (a.WebId == f.Id && a.Verspaetet == verspaetet && a.Minuten == minuten && a.Status == status && a.Bemerkung == f.Bemerkung &&
                    a.Fach == f.Fach && a.Lehrer == f.Lehrer && a.Klasse == klasse && a.UnterrichtId == unterricht && a.Name == name) continue;
                geaendert++;
            }
            else
            {
                a = new SchuelerAbwesenheit { SchuelerId = f.SchuelerId, Datum = datum, Stunde = f.Stunde };
                d.SchuelerAbwesenheiten.Add(a);
                vorhanden[(datum, f.Stunde, f.SchuelerId)] = a;
                gemeldet.Add(a);
                neu++;
            }
            a.WebId = f.Id; a.Name = name; a.Klasse = klasse; a.UnterrichtId = unterricht; a.Fach = f.Fach;
            a.Verspaetet = verspaetet; a.Minuten = minuten; a.Status = status; a.Bemerkung = f.Bemerkung; a.Lehrer = f.Lehrer; a.Importiert = DateTime.Now;
        }
        var ende = bis ?? ab.AddDays(370); // Server liefert ohne „bis“ ein Jahr ab „ab“
        var entfernt = d.SchuelerAbwesenheiten.RemoveAll(a => a.Datum >= ab && a.Datum <= ende && !gemeldet.Contains(a));
        d.SchuelerAbwesenheiten.Sort((x, y) => (x.Datum, x.Stunde, x.Klasse, x.Name).CompareTo((y.Datum, y.Stunde, y.Klasse, y.Name)));
        return new WebImportErgebnis(neu, geaendert, entfernt, abgelehnt);
    }

    /// <summary>
    /// Klassenbuch (Lehrstoff, Hausaufgaben) übernehmen – Abgleich über die Server-Id wie bei den Fehlzeiten; im abgefragten
    /// Zeitraum auf dem Server gelöschte Einträge werden entfernt.
    /// </summary>
    public static WebImportErgebnis KlassenbuchUebernehmen(Schuldatei d, IEnumerable<WebKlassenbucheintrag> liste, DateOnly ab, DateOnly? bis = null)
    {
        var vorhanden = d.Klassenbuch.GroupBy(a => a.WebId).ToDictionary(g => g.Key, g => g.First());
        var gemeldet = new HashSet<int>();
        int neu = 0, geaendert = 0, abgelehnt = 0;
        foreach (var k in liste)
        {
            gemeldet.Add(k.Id);
            if (Datum(k.Datum) is not DateOnly datum) { abgelehnt++; continue; }
            var faellig = Datum(k.Faellig);
            int? unterricht = k.Unterricht > 0 ? k.Unterricht : null;
            if (vorhanden.TryGetValue(k.Id, out var e))
            {
                if (e.Datum == datum && e.Stunde == k.Stunde && e.Geaendert == k.Geaendert && e.Lehrstoff == k.Lehrstoff && e.Hausaufgabe == k.Hausaufgabe &&
                    e.Faellig == faellig && e.Lehrer == k.Lehrer && e.Fach == k.Fach && e.UnterrichtId == unterricht && e.AnwesenheitGeprueft == (k.Anwesenheit != null) &&
                    e.Klassen.SequenceEqual(k.Klassen)) continue;
                geaendert++;
            }
            else
            {
                e = new KlassenbuchEintrag { WebId = k.Id };
                d.Klassenbuch.Add(e);
                vorhanden[k.Id] = e;
                neu++;
            }
            e.Datum = datum; e.Stunde = k.Stunde; e.UnterrichtId = unterricht; e.Klassen = k.Klassen.ToList(); e.Fach = k.Fach; e.Lehrer = k.Lehrer;
            e.Lehrstoff = k.Lehrstoff; e.Hausaufgabe = k.Hausaufgabe; e.Faellig = faellig; e.AnwesenheitGeprueft = k.Anwesenheit != null; e.Geaendert = k.Geaendert;
        }
        var ende = bis ?? ab.AddDays(370); // Server liefert ohne „bis“ ein Jahr ab „ab“
        var entfernt = d.Klassenbuch.RemoveAll(e => e.Datum >= ab && e.Datum <= ende && !gemeldet.Contains(e.WebId));
        d.Klassenbuch.Sort((x, y) => (x.Datum, x.Stunde, x.Fach).CompareTo((y.Datum, y.Stunde, y.Fach)));
        return new WebImportErgebnis(neu, geaendert, entfernt, abgelehnt);
    }

    /// <summary>
    /// Rückmeldungen übernehmen:
    /// <list type="bullet">
    /// <item>Krankmeldungen als Lehrer-Absenzen.</item>
    /// <item>Nachrichten als Tagestext für das Kollegium.</item>
    /// <item>Raumbuchungen als Vertretungszeilen der Art Raumbuchung, je Stunde eine.</item>
    /// <item>Klausuren in die Klausurliste.</item>
    /// </list>
    /// Abgelehnt werden Rückmeldungen mit unbekannten Elementen, ungültigem Datum oder (bei Raumbuchungen) belegtem Raum.
    /// Betroffene Tage werden neu berechnet.
    /// </summary>
    public static (List<string> Uebernommen, List<string> Abgelehnt) Uebernehmen(Schuldatei d, IEnumerable<WebRueckmeldung> liste)
    {
        var ok = new List<string>();
        var nein = new List<string>();
        var tage = new HashSet<DateOnly>();
        var rest = new List<WebRueckmeldung>();
        var ix = new DatenIndex(d);
        int? Id(ElementTyp t, string? k) => string.IsNullOrWhiteSpace(k) ? null : d.AlleElemente(t).FirstOrDefault(e => e.Kurzname.Equals(k.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
        List<int>? Ids(ElementTyp t, IEnumerable<string> namen)
        {
            var res = new List<int>();
            foreach (var n in namen.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                if (Id(t, n) is not int i) return null;
                res.Add(i);
            }
            return res.Distinct().ToList();
        }
        foreach (var m in liste)
        {
            if (m.Typ is not (WebRueckmeldungsTypen.Raumbuchung or WebRueckmeldungsTypen.Klausur)) { rest.Add(m); continue; }
            if (!DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var datum) || !Kalender.IstUnterrichtstag(d, datum)) { nein.Add(m.Id); continue; }
            var von = Math.Clamp(m.StundeVon ?? 1, 1, d.Zeitraster.StundenProTag);
            var bis = Math.Clamp(m.StundeBis ?? von, von, d.Zeitraster.StundenProTag);
            var klassen = Ids(ElementTyp.Klasse, m.Klassen);
            var raeume = Ids(ElementTyp.Raum, m.Raeume.Append(m.Raum ?? "").Where(x => x.Length > 0));
            var lehrer = Ids(ElementTyp.Lehrer, new[] { m.Lehrer ?? "" }.Concat(m.WeitereLehrer));
            if (klassen is null || raeume is null || lehrer is null) { nein.Add(m.Id); continue; }
            if (m.Typ == WebRueckmeldungsTypen.Raumbuchung)
            {
                if (raeume.Count == 0) { nein.Add(m.Id); continue; }
                // Raum muss in allen Stunden frei sein (kein Unterricht, keine andere Buchung)
                var tp = Tagesplan.Berechnen(d, datum);
                if (Enumerable.Range(von, bis - von + 1).Any(s => tp.Stunde(s).Any(e => e.Findetstatt && e.RaumIds.Contains(raeume[0])))) { nein.Add(m.Id); continue; }
                for (var s = von; s <= bis; s++)
                    d.Vertretungen.Add(new Vertretung
                    {
                        Id = d.NeueId(), Datum = datum, Stunde = s, Art = VertretungsArt.Raumbuchung, Quelle = VertretungsQuelle.Manuell, Bearbeitet = true,
                        VertreterId = lehrer.FirstOrDefault() is int l and > 0 ? l : null, KlassenIds = klassen, VertretungsraumId = raeume[0], ZusatzraumIds = raeume.Skip(1).ToList(),
                        Text = string.Join(" – ", new[] { m.Titel, m.Text }.Where(x => !string.IsNullOrWhiteSpace(x))), Benutzer = "UntPlanWeb " + m.Absender,
                    });
            }
            else
            {
                d.Klausuren.Add(new Klausur
                {
                    Id = d.NeueId(), Name = string.IsNullOrWhiteSpace(m.Titel) ? "Klausur" : m.Titel!.Trim(), Text = m.Text ?? "", Datum = datum, StundeVon = von, StundeBis = bis,
                    KlassenIds = klassen, LehrerIds = lehrer, RaumIds = raeume, UnterrichtEntfaellt = true,
                });
                tage.Add(datum);
            }
            ok.Add(m.Id);
        }
        var meldungen = rest.Select(m => new Portal.Meldung(m.Id, m.Typ == WebRueckmeldungsTypen.Absenz ? "abwesenheit" : m.Typ, m.Erstellt, m.Absender, m.Lehrer, m.Von, m.Bis, m.StundeVon, m.StundeBis, m.Grund, m.Text));
        var (ok2, nein2) = Portal.MeldungenUebernehmen(d, meldungen);
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return (ok.Concat(ok2).ToList(), nein.Concat(nein2).ToList());
    }
}
