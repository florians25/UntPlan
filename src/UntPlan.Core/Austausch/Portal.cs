using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Austausch;

/// <summary>
/// Web-Portal-Synchronisation (Schema "untplan-sync/1", siehe docs/SYNC_API.md). Die Desktop-Anwendung ist führend;
/// das Portal veröffentlicht Pläne und meldet z. B. Krankmeldungen zurück.
/// </summary>
public static class Portal
{
    public const string Schema = "untplan-sync/1";

    public sealed record SyncPlan(int Tag, int Stunde, string Fach, string[] Klassen, string[] Lehrer, string Raum, string Gruppe, string Woche);
    public sealed record SyncVertretung(string Datum, int Stunde, string Art, string[] Klassen, string Fach, string Lehrer, string Vertreter, string Raum, string Text);
    public sealed record SyncPaket(string Schema, DateTime Erzeugt, int BasisRevision, object Schule, object Zeitraster, object[] Lehrer, object[] Klassen,
        object[] Raeume, object[] Faecher, SyncPlan[] Stundenplan, SyncVertretung[] Vertretungen, object[] Nachrichten, object[] FreieTage);
    public sealed record Meldung(string Id, string Typ, DateTime Erstellt, string Absender, string? LehrerKuerzel, string? Von, string? Bis, int? StundeVon, int? StundeBis, string? Grund, string? Text);
    public sealed record Antwort(bool Ok, int Revision, string? Meldung);

    public static SyncPaket PaketErstellen(Schuldatei d, DateOnly ab)
    {
        var ix = new DatenIndex(d);
        var vertretungen = new List<SyncVertretung>();
        for (var i = 0; i < Math.Max(1, d.Einstellungen.Portal.TageVoraus); i++)
        {
            var datum = ab.AddDays(i);
            foreach (var z in Vertretungsplan.Zeilen(d, datum).Where(z => z.Art != "Pausenaufsicht"))
                vertretungen.Add(new SyncVertretung(datum.ToString("yyyy-MM-dd"), z.Stunde, z.Art, z.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)).ToArray(),
                    z.NeuesFach, z.Lehrer, z.Vertreter, z.NeuerRaum, z.Text));
        }
        return new SyncPaket(Schema, DateTime.Now, d.Einstellungen.Portal.Revision,
            new { name = d.Einstellungen.Schulname, schulnummer = d.Einstellungen.Schulnummer, schulform = Nrw.Schulformen.GetValueOrDefault(d.Einstellungen.Schulform, ""), bundesland = "NRW" },
            new { tage = d.Zeitraster.TageProWoche, stunden = d.Zeitraster.Zeiten.Select(z => new { nr = z.Nr, beginn = z.Beginn.ToString("HH:mm"), ende = z.Ende.ToString("HH:mm") }) },
            d.Lehrer.Where(l => !l.Ignorieren).Select(l => (object)new { kuerzel = l.Kurzname, name = $"{l.Vorname} {l.Langname}".Trim(), email = l.Email }).ToArray(),
            d.Klassen.Select(k => (object)new { name = k.Kurzname, jahrgang = k.Jahrgang, klassenleitung = k.KlassenlehrerIds.Select(ix.KLehrer) }).ToArray(),
            d.Raeume.Select(r => (object)new { kuerzel = r.Kurzname, name = r.Langname }).ToArray(),
            d.Faecher.Select(f => (object)new { kuerzel = f.Kurzname, name = f.Langname, farbe = $"#{f.Hintergrund & 0xFFFFFF:X6}" }).ToArray(),
            d.Stundenplan.Where(p => ix.Unterrichte.ContainsKey(p.UnterrichtId)).Select(p =>
            {
                var u = ix.Unterrichte[p.UnterrichtId];
                return new SyncPlan(p.Tag, p.Stunde, ix.KFach(u.HauptfachId), u.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)).ToArray(),
                    u.LehrerIds.Select(ix.KLehrer).ToArray(), string.Join(",", p.Raeume.Where(r => r.HasValue).Select(r => ix.KRaum(r))), u.Kursname,
                    u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => "" });
            }).ToArray(),
            vertretungen.ToArray(),
            d.Tagestexte.Where(t => t.Datum >= ab).Select(t => (object)new { datum = t.Datum.ToString("yyyy-MM-dd"), text = t.Text, intern = t.NurLehrer }).ToArray(),
            d.Ferien.Select(f => (object)new { name = f.Langname, von = f.Von.ToString("yyyy-MM-dd"), bis = f.Bis.ToString("yyyy-MM-dd"), feiertag = f.Feiertag }).ToArray());
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static HttpClient Client(PortalEinstellungen p)
    {
        var c = new HttpClient { BaseAddress = new Uri(p.Adresse.TrimEnd('/') + "/api/v1/"), Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", p.ApiSchluessel);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("UntPlan-Desktop/1.0");
        return c;
    }

    private static async Task<T> Lesen<T>(HttpResponseMessage r, CancellationToken ct)
    {
        if (!r.IsSuccessStatusCode)
        {
            string? text = null;
            try { text = (await r.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("meldung").GetString(); } catch { }
            throw new InvalidOperationException(text ?? $"Portal antwortet mit HTTP {(int)r.StatusCode}");
        }
        return (await r.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public static async Task<string> StatusAsync(PortalEinstellungen p, CancellationToken ct = default)
    {
        using var c = Client(p);
        var e = await Lesen<JsonElement>(await c.GetAsync("status", ct), ct);
        return $"{e.GetProperty("name").GetString()} (Version {e.GetProperty("version").GetString()})";
    }

    public static async Task<Antwort> HochladenAsync(PortalEinstellungen p, SyncPaket paket, CancellationToken ct = default)
    {
        using var c = Client(p);
        return await Lesen<Antwort>(await c.PutAsJsonAsync($"schulen/{Uri.EscapeDataString(p.SchulId)}/snapshot", paket, Json, ct), ct);
    }

    public static async Task<List<Meldung>> MeldungenAsync(PortalEinstellungen p, CancellationToken ct = default)
    {
        using var c = Client(p);
        var e = await Lesen<JsonElement>(await c.GetAsync($"schulen/{Uri.EscapeDataString(p.SchulId)}/meldungen?seit=0", ct), ct);
        return e.GetProperty("meldungen").Deserialize<List<Meldung>>(Json) ?? new();
    }

    public static async Task QuittierenAsync(PortalEinstellungen p, IEnumerable<string> ids, CancellationToken ct = default)
    {
        using var c = Client(p);
        await Lesen<JsonElement>(await c.PostAsJsonAsync($"schulen/{Uri.EscapeDataString(p.SchulId)}/meldungen/quittieren", new { ids }, Json, ct), ct);
    }

    /// <summary>Übernimmt Krankmeldungen aus dem Portal als Lehrer-Abwesenheiten.</summary>
    public static (List<string> Uebernommen, List<string> Abgelehnt) MeldungenUebernehmen(Schuldatei d, IEnumerable<Meldung> meldungen)
    {
        var ok = new List<string>();
        var nein = new List<string>();
        foreach (var m in meldungen)
        {
            var l = d.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(m.LehrerKuerzel ?? m.Absender, StringComparison.OrdinalIgnoreCase));
            if (m.Typ == "abwesenheit" && l != null && DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var von))
            {
                var bis = DateOnly.TryParse(m.Bis, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : von;
                var grund = d.Abwesenheitsgruende.FirstOrDefault(g => g.Langname.Equals(m.Grund ?? "Krankheit", StringComparison.OrdinalIgnoreCase) || g.Kurzname.Equals(m.Grund ?? "K", StringComparison.OrdinalIgnoreCase));
                d.Abwesenheiten.Add(new Abwesenheit
                {
                    Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = von, Bis = bis, StundeVon = m.StundeVon ?? 1, StundeBis = m.StundeBis ?? 99,
                    GrundId = grund?.Id, Text = $"{m.Text} (Portal-Meldung)".Trim(),
                });
                ok.Add(m.Id);
            }
            else if (m.Typ == "nachricht" && !string.IsNullOrWhiteSpace(m.Text))
            {
                d.Tagestexte.Add(new Tagestext { Datum = DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var dt) ? dt : DateOnly.FromDateTime(DateTime.Today), Text = $"{m.Text} – {m.Absender}", NurLehrer = true });
                ok.Add(m.Id);
            }
            else nein.Add(m.Id);
        }
        return (ok, nein);
    }
}
