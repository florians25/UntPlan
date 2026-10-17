using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record PausenZelle(int Gang, int Tag, int Nach, string? Lehrer);
public sealed record PausenPosition(int Gang, int Tag, int Nach, int Lehrer = 0, int Delta = 0);
public sealed record PausenLoeschen(string Sicht, int Id, int? Tag, int? Nach);
public sealed record PausenOptimierung(bool SchlechteLoeschen, int? Gang, int KuerzestePause);
public sealed record PausenGewichtung(int MaxProTag, Dictionary<string, int> Werte);
public sealed record PausenVertreter(int Vertretung, int? Lehrer);

/// <summary>
/// Modul Pausenaufsichten (Desktop: Fenster „Pausenaufsichten“): Reiter Gang (Aufsichtsbereich) bzw. Lehrer mit der Matrix Tage ×
/// Aufsichtspausen, Lehrerliste (PA Max/Ist/Anzahl), Lehrervorschlag mit Bewertung, Aufsicht eintragen/löschen/teilen, „keine
/// Aufsicht“, Optimierung, Gewichtung – und die Vertretung der Aufsichten an einem Tag (offene Pausenaufsichtsvertretungen mit Kandidaten).
/// </summary>
public sealed class PausenaufsichtApi : IStudioModul
{
    public string Name => "pausenaufsicht";

    private static List<Pause> Pausen(Schuldatei d) => Pausenaufsichten.AufsichtsPausen(d).ToList();

    private static object Spalten(Schuldatei d) => Pausen(d).Select(p => new
    {
        nach = p.NachStunde, name = Pausenaufsichten.PausenName(d, p.NachStunde),
        zeit = $"{d.Zeitraster.Zeit(p.NachStunde)?.Ende:H\\:mm}-{d.Zeitraster.Zeit(p.NachStunde + 1)?.Beginn:H\\:mm}", minuten = Pausenaufsichten.PausenMinuten(d, p.NachStunde),
    }).ToList();

    private static List<object> Lehrerliste(Schuldatei d) => Stammdaten.Sortiert(d.Lehrer).Where(l => !l.Ignorieren).Select(l =>
    {
        var ist = Pausenaufsichten.IstMinuten(d, l.Id);
        var diff = ist - l.PausenaufsichtMaxMinuten;
        return (object)new
        {
            id = l.Id, kurz = l.Kurzname, max = l.PausenaufsichtMaxMinuten, ist, anzahl = Pausenaufsichten.Anzahl(d, l.Id),
            vertretungen = d.Vertretungen.Count(v => v.VertreterId == l.Id && v.Art == VertretungsArt.Pausenaufsicht), differenz = diff.ToString("+0;-0;0"), ueber = diff > 0, geschlecht = l.Geschlecht,
        };
    }).ToList();

    public static object Gang(Dokument doc, int gang)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var g = d.Gaenge.FirstOrDefault(x => x.Id == gang) ?? throw StudioFehler.NichtGefunden($"Der Gang Nr. {gang}");
        var pausen = Pausen(d);
        int min = 0, offen = 0;
        var tage = Enumerable.Range(0, d.Zeitraster.TageProWoche).Select(t => new
        {
            tag = t, name = Planungsdienst.Tagname(d, t),
            zellen = pausen.Select(p =>
            {
                var teile = Pausenaufsichten.Teile(d, gang, t, p.NachStunde);
                var liste = d.Pausenaufsichten.Where(a => a.GangId == gang && a.Tag == t && a.NachStunde == p.NachStunde && a.Datum is null).ToList();
                var namen = Enumerable.Range(1, Math.Max(0, teile)).Select(teil => liste.FirstOrDefault(a => a.Teil == teil) is { } a ? ix.KLehrer(a.LehrerId) : "???").ToList();
                var m = Pausenaufsichten.Minuten(d, gang, t, p.NachStunde);
                min += m * teile;
                offen += m * Enumerable.Range(1, Math.Max(0, teile)).Count(teil => liste.All(a => a.Teil != teil));
                var schlecht = liste.Where(a => ix.Lehrer.TryGetValue(a.LehrerId, out var l) && Pausenaufsichten.Bewerten(d, ix, l, gang, t, p.NachStunde, a).Schlecht).Select(a => ix.KLehrer(a.LehrerId)).ToList();
                return new { nach = p.NachStunde, teile, text = teile == 0 ? "" : string.Join(", ", namen), offen = liste.Count < teile, schlecht, keine = teile == 0 };
            }).ToList(),
        }).ToList();
        return new { sicht = "gang", id = g.Id, kurz = g.Kurzname, spalten = Spalten(d), tage, minuten = $"Minuten: {min} (offen: {offen})", lehrer = Lehrerliste(d) };
    }

    public static object LehrerSicht(Dokument doc, int lehrer)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var l = d.Lehrer.FirstOrDefault(x => x.Id == lehrer) ?? throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {lehrer}");
        var pausen = Pausen(d);
        var tage = Enumerable.Range(0, d.Zeitraster.TageProWoche).Select(t => new
        {
            tag = t, name = Planungsdienst.Tagname(d, t),
            zellen = pausen.Select(p => new
            {
                nach = p.NachStunde,
                text = string.Join(", ", d.Pausenaufsichten.Where(a => a.LehrerId == lehrer && a.Tag == t && a.NachStunde == p.NachStunde && a.Datum is null).Select(a => ix.K(ElementTyp.Gang, a.GangId))),
                moeglich = Pausenaufsichten.Moeglich(d, ix, l, t, p.NachStunde),
            }).ToList(),
        }).ToList();
        return new { sicht = "lehrer", id = l.Id, kurz = l.Kurzname, spalten = Spalten(d), tage, minuten = $"Minuten: {Pausenaufsichten.IstMinuten(d, lehrer)}", lehrer = Lehrerliste(d) };
    }

    private static void GangPruefen(Schuldatei d, int gang, int tag, int nach)
    {
        if (d.Gaenge.All(g => g.Id != gang)) throw StudioFehler.NichtGefunden($"Der Gang Nr. {gang}");
        if (tag < 0 || tag >= d.Zeitraster.TageProWoche || nach < 0 || nach > d.Zeitraster.StundenProTag) throw StudioFehler.Ungueltig("Ungültige Pause.");
    }

    private static object VertretungsTag(Schuldatei d, DateOnly datum)
    {
        var ix = new DatenIndex(d);
        var (zv, zb) = Zaehlung.Zeitraum(d, datum);
        var liste = d.Vertretungen.Where(v => v.Datum == datum && v.Art == VertretungsArt.Pausenaufsicht).OrderBy(v => v.Stunde).ThenBy(v => ix.K(ElementTyp.Gang, v.GangId)).ToList();
        return new
        {
            datum, datumText = Format.Datum(datum), unterrichtstag = Kalender.IstUnterrichtstag(d, datum),
            zeilen = liste.Select(v =>
            {
                var a = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
                return new
                {
                    id = v.Id, pause = Pausenaufsichten.PausenName(d, v.Stunde), nach = v.Stunde, gang = ix.K(ElementTyp.Gang, v.GangId), lehrer = ix.KLehrer(v.LehrerId),
                    vertreter = ix.KLehrer(v.VertreterId), vertreterId = v.VertreterId, offen = v.VertreterId is null, text = v.Text, grund = v.Grund,
                    kandidaten = Pausenaufsichten.Kandidaten(d, datum, a).Take(25)
                        .Select(l => new { id = l.Id, kurz = l.Kurzname, zaehler = Zaehlung.Lehrer(d, l, zv, zb).Summe.ToString("0.#", Format.De), aufsichten = Pausenaufsichten.Anzahl(d, l.Id) }).ToList(),
                };
            }).ToList(),
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/pausenaufsicht");

        g.MapGet("", (Arbeitsdokument a) => a.LesenAsync(doc => new
        {
            gaenge = Stammdaten.Sortiert(doc.Daten.Gaenge).Select(x => new { id = x.Id, kurz = x.Kurzname, lang = x.Langname }),
            lehrer = Stammdaten.Sortiert(doc.Daten.Lehrer).Where(l => !l.Ignorieren).Select(x => new { id = x.Id, kurz = x.Kurzname, lang = x.VollerName }),
            pausen = Spalten(doc.Daten),
        }));
        g.MapGet("/gang/{id:int}", (Arbeitsdokument a, int id) => a.LesenAsync(doc => Gang(doc, id)));
        g.MapGet("/lehrer/{id:int}", (Arbeitsdokument a, int id) => a.LesenAsync(doc => LehrerSicht(doc, id)));

        // Zelle bearbeiten: Kurznamen „AB, CD“ (geteilte Aufsicht) – wie die Eingabe im Desktop-Raster
        g.MapPut("/zelle", (Arbeitsdokument a, PausenZelle z, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Pausenaufsicht", AenderungsArt.Stundenplan, d =>
            {
                GangPruefen(d, z.Gang, z.Tag, z.Nach);
                var namen = (z.Lehrer ?? "").Split(new[] { ',', ' ', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Where(n => n != "???").ToList();
                var ids = namen.Select(n => d.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id ?? throw StudioFehler.Ungueltig($"Lehrer '{n}' ist nicht bekannt.")).ToList();
                d.Pausenaufsichten.RemoveAll(x => x.GangId == z.Gang && x.NachStunde == z.Nach && x.Tag == z.Tag && x.Datum is null);
                if (ids.Count > Pausenaufsichten.Teile(d, z.Gang, z.Tag, z.Nach)) Pausenaufsichten.TeileSetzen(d, z.Gang, z.Tag, z.Nach, ids.Count);
                for (var i = 0; i < ids.Count; i++) d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = z.Gang, NachStunde = z.Nach, Tag = z.Tag, LehrerId = ids[i], Teil = i + 1 });
            }, doc => Gang(doc, z.Gang)));

        g.MapGet("/vorschlag", (Arbeitsdokument a, int gang, int tag, int nach) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            GangPruefen(d, gang, tag, nach);
            var teile = Pausenaufsichten.Teile(d, gang, tag, nach);
            if (teile == 0) throw StudioFehler.Ungueltig("Für diese Pause ist keine Aufsicht vorgesehen.");
            var belegt = d.Pausenaufsichten.Where(x => x.GangId == gang && x.Tag == tag && x.NachStunde == nach && x.Datum is null).ToList();
            var teil = Enumerable.Range(1, teile).FirstOrDefault(t => belegt.All(x => x.Teil != t));
            var ersetzen = teil == 0 ? belegt.First() : null;
            static string J(bool b) => b ? "x" : "";
            return new
            {
                titel = $"Lehrervorschlag – {doc.Index.K(ElementTyp.Gang, gang)}, {Planungsdienst.Tagname(d, tag)}, Pause {Pausenaufsichten.PausenName(d, nach)}",
                ersetzt = ersetzen is null ? null : doc.Index.KLehrer(ersetzen.LehrerId),
                spalten = new[] { "Name", "Punkte", "Zähler", "PA/Tag", "Minuten", "Max.Min", "Unt. davor", "Unt. danach", "Gang davor", "Gang danach", "Bereit.", "vor 1.St.", "nach letzter", "Aufs.Folge", "Sperrung", "Hohlst.", "Geschl." },
                zeilen = Pausenaufsichten.Lehrervorschlag(d, gang, tag, nach, ersetzen).Select(b => new
                {
                    id = b.Lehrer.Id, schlecht = b.Schlecht, verletzt = b.Verletzt,
                    werte = new[] { b.Lehrer.Kurzname, b.Punkte.ToString("0.#", Format.De), b.Zaehler.ToString(), b.ProTag.ToString(), b.Minuten.ToString(), b.MaxMinuten.ToString(), J(b.UnterrichtDavor), J(b.UnterrichtDanach),
                        J(b.GangDavor), J(b.GangDanach), J(b.Bereitschaft), J(b.VorErsterStunde), J(b.NachLetzterStunde), J(b.Folge), J(b.VorSperrung || b.NachSperrung), J(b.HohlstundeDavor || b.HohlstundeDanach), b.Lehrer.Geschlecht },
                }).ToList(),
            };
        }));

        // Lehrer aus dem Vorschlag einteilen (freier Teil bzw. ersetzt den ersten, wenn alle Teile belegt sind)
        g.MapPost("/zuordnen", (Arbeitsdokument a, PausenPosition z, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Pausenaufsicht", AenderungsArt.Stundenplan, d =>
            {
                GangPruefen(d, z.Gang, z.Tag, z.Nach);
                var l = d.Lehrer.FirstOrDefault(x => x.Id == z.Lehrer) ?? throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {z.Lehrer}");
                var teile = Math.Max(1, Pausenaufsichten.Teile(d, z.Gang, z.Tag, z.Nach));
                var belegt = d.Pausenaufsichten.Where(x => x.GangId == z.Gang && x.Tag == z.Tag && x.NachStunde == z.Nach && x.Datum is null).ToList();
                var teil = Enumerable.Range(1, teile).FirstOrDefault(t => belegt.All(x => x.Teil != t));
                if (teil == 0)
                {
                    teil = belegt.First().Teil;
                    d.Pausenaufsichten.RemoveAll(x => x.GangId == z.Gang && x.Tag == z.Tag && x.NachStunde == z.Nach && x.Teil == teil && x.Datum is null);
                }
                d.Pausenaufsichten.Add(new Pausenaufsicht { GangId = z.Gang, Tag = z.Tag, NachStunde = z.Nach, LehrerId = l.Id, Teil = teil });
            }, doc => Gang(doc, z.Gang)));

        // Aufsicht löschen: eine Zelle (Tag + Pause) bzw. alle Aufsichten des Elements (ohne Tag) bzw. alle (Sicht „alle“)
        g.MapPost("/loeschen", (Arbeitsdokument a, PausenLoeschen z, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), z.Sicht == "alle" ? "Pausenaufsichten löschen" : z.Tag is null ? "Aufsichten löschen" : "Aufsicht löschen", AenderungsArt.Stundenplan, d =>
            {
                var gang = z.Sicht == "gang";
                d.Pausenaufsichten.RemoveAll(x => x.Datum is null && (z.Sicht == "alle" || (gang ? x.GangId == z.Id : x.LehrerId == z.Id))
                                                  && (z.Tag is null || x.Tag == z.Tag) && (z.Nach is null || x.NachStunde == z.Nach));
            }, _ => new { ok = true }));

        // Teile ändern: Delta +1 teilen, -1 Teilung entfernen, 0 = umschalten „keine Aufsicht“ ↔ „Aufsicht (???)“
        g.MapPost("/teile", (Arbeitsdokument a, PausenPosition z, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), z.Delta > 0 ? "Aufsicht teilen" : z.Delta < 0 ? "Teilung entfernen" : "Keine Aufsicht", AenderungsArt.Stundenplan, d =>
            {
                GangPruefen(d, z.Gang, z.Tag, z.Nach);
                var teile = Pausenaufsichten.Teile(d, z.Gang, z.Tag, z.Nach);
                if (z.Delta == 0) Pausenaufsichten.TeileSetzen(d, z.Gang, z.Tag, z.Nach, teile == 0 ? 1 : 0);
                else if (teile + z.Delta >= 1) Pausenaufsichten.TeileSetzen(d, z.Gang, z.Tag, z.Nach, teile + z.Delta);
                else throw StudioFehler.Ungueltig("Die Aufsicht ist nicht geteilt.");
            }, doc => Gang(doc, z.Gang)));

        g.MapPost("/optimieren", (Arbeitsdokument a, PausenOptimierung o, ClaimsPrincipal p) =>
        {
            var n = 0;
            return a.AendernAsync(p.Benutzername(), "Pausenaufsichten optimieren", AenderungsArt.Stundenplan,
                d => n = Pausenaufsichten.Optimieren(d, o.SchlechteLoeschen, o.Gang, 0, 0, Math.Max(0, o.KuerzestePause)),
                _ => new { eingeteilt = n, text = $"{n} Aufsichten wurden eingeteilt." });
        });

        g.MapGet("/gewichtung", (Arbeitsdokument a) => a.LesenAsync(doc => new
        {
            maxProTag = Pausenaufsichten.MaxProTag(doc.Daten),
            kriterien = Pausenaufsichten.Kriterien.Select(k => new { schluessel = k.Schluessel, text = k.Text, wert = Math.Clamp(Pausenaufsichten.Gewicht(doc.Daten, k.Schluessel), 0, 5), standard = k.Standard }),
        }));
        g.MapPut("/gewichtung", (Arbeitsdokument a, PausenGewichtung e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Gewichtung Pausenaufsichten", AenderungsArt.Einstellungen, d =>
            {
                foreach (var (k, v) in e.Werte)
                    if (Pausenaufsichten.Kriterien.Any(x => x.Schluessel == k)) d.Gewichtung[k] = Math.Clamp(v, 0, 5);
                d.Gewichtung["PA.MaxProTagAnzahl"] = Math.Clamp(e.MaxProTag, 1, 5);
            }, _ => new { ok = true }));

        // ---- Vertretung der Pausenaufsichten (Vertretungsmodus) ----
        g.MapGet("/vertretung/{datum}", (Arbeitsdokument a, DateOnly datum) => a.LesenAsync(doc => VertretungsTag(doc.Daten, datum)));
        g.MapPost("/vertretung/{datum}", (Arbeitsdokument a, DateOnly datum, PausenVertreter e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), e.Lehrer is null ? "Aufsichtsvertretung aufgehoben" : "Aufsichtsvertretung", AenderungsArt.Vertretung, d =>
            {
                var v = d.Vertretungen.FirstOrDefault(x => x.Id == e.Vertretung && x.Datum == datum && x.Art == VertretungsArt.Pausenaufsicht)
                        ?? throw StudioFehler.NichtGefunden($"Die Pausenaufsichtsvertretung Nr. {e.Vertretung}");
                if (e.Lehrer is int l && d.Lehrer.All(x => x.Id != l)) throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {l}");
                v.VertreterId = e.Lehrer;
                v.Bearbeitet = true;
                v.Benutzer = p.Benutzername();
            }, doc => VertretungsTag(doc.Daten, datum)));
    }
}
