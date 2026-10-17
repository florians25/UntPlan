using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record KursWert(string Feld, string? Wert);
public sealed record KursWahl(int Schueler, int Kurs, bool Gewaehlt);
public sealed record KursBandbildung(int MaxBaender);
public sealed record KursBaender(string Praefix, Dictionary<int, int> Zuordnung);

/// <summary>
/// Modul Kursplanung (Desktop: Fenster „Kursplanung“): Kurse einer Klasse/eines Jahrgangs (Kursname, Band änderbar), Kurswahlen
/// der Schüler, Kollisionsmatrix, automatische Bandbildung (Vorschlag) und Übernahme der Bänder in den Unterricht.
/// </summary>
public sealed class KursplanungApi : IStudioModul
{
    public string Name => "kursplanung";

    public static object Klasse(Dokument doc, int klasse)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var k = d.Klassen.FirstOrDefault(x => x.Id == klasse) ?? throw StudioFehler.NichtGefunden($"Die Klasse Nr. {klasse}");
        var kurse = Kursplanung.Kurse(d, klasse);
        var schueler = Stammdaten.Sortiert(d.Schueler.Where(s => s.KlasseId == klasse)).ToList();
        var m = Kursplanung.Kollisionsmatrix(d, kurse);
        var kollisionen = Kursplanung.Kollisionen(d, kurse);
        var zuordnung = kurse.Where(x => !string.IsNullOrEmpty(x.Band)).GroupBy(x => x.Band).ToList();
        return new
        {
            klasse = new { id = k.Id, kurz = k.Kurzname },
            kurse = kurse.Select(u =>
            {
                var teil = d.Schueler.Where(s => s.KurswahlIds.Contains(u.Id)).ToList();
                return new
                {
                    id = u.Id, kursname = u.Kursname, fach = ix.KFach(u.HauptfachId), lehrer = string.Join(",", u.LehrerIds.Select(ix.KLehrer)), klassen = ix.KKlassen(u.KlassenIds),
                    wst = u.Wochenstunden, band = u.Band, teilnehmer = teil.Count, mw = $"{teil.Count(s => s.Geschlecht == "m")}/{teil.Count(s => s.Geschlecht == "w")}",
                };
            }),
            schueler = schueler.Select(s => new
            {
                id = s.Id, name = $"{(s.Langname.Length > 0 ? s.Langname : s.Kurzname)}, {s.Vorname}".Trim(' ', ','),
                wahlen = kurse.Where(x => s.KurswahlIds.Contains(x.Id)).Select(x => x.Id).ToList(),
            }),
            matrix = kurse.Select(a => kurse.Select(b => a.Id == b.Id ? -1 : m.GetValueOrDefault(a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id))).ToArray()).ToArray(),
            baender = zuordnung.Select(gr => new { band = gr.Key, kurse = gr.Select(x => $"{x.Kursname} ({Kursplanung.Teilnehmer(d, x.Id)})").ToList(), schueler = gr.Sum(x => Kursplanung.Teilnehmer(d, x.Id)) }),
            kollisionen,
            status = $"{kurse.Count} Kurse, {schueler.Count} Schüler, {kollisionen} Kollisionen (Schüler mit zwei Kursen im selben Band).",
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/kursplanung");

        // Klassen mit Kursen (sonst alle), wie die Auswahlliste im Desktop
        g.MapGet("", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var mit = d.Klassen.Where(k => Kursplanung.Kurse(d, k.Id).Count > 0).ToList();
            return Stammdaten.Sortiert(mit.Count > 0 ? mit : d.Klassen).Select(k => new { id = k.Id, kurz = k.Kurzname, kurse = Kursplanung.Kurse(d, k.Id).Count }).ToList();
        }));

        g.MapGet("/{klasse:int}", (Arbeitsdokument a, int klasse) => a.LesenAsync(doc => Klasse(doc, klasse)));

        g.MapPatch("/{klasse:int}/kurs/{kurs:int}", (Arbeitsdokument a, int klasse, int kurs, KursWert w, ClaimsPrincipal p) =>
        {
            var band = w.Feld.Equals("band", StringComparison.OrdinalIgnoreCase);
            if (!band && !w.Feld.Equals("kursname", StringComparison.OrdinalIgnoreCase)) throw StudioFehler.Ungueltig("Änderbar sind Kursname und Band.");
            return a.AendernAsync(p.Benutzername(), band ? "Band" : "Kursname", AenderungsArt.Unterricht, d =>
            {
                var u = d.Unterrichte.FirstOrDefault(x => x.Id == kurs) ?? throw StudioFehler.NichtGefunden($"Der Kurs U-Nr. {kurs}");
                if (band) u.Band = (w.Wert ?? "").Trim(); else u.Kursname = (w.Wert ?? "").Trim();
            }, doc => Klasse(doc, klasse));
        });

        g.MapPut("/{klasse:int}/wahl", (Arbeitsdokument a, int klasse, KursWahl w, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kurswahl", AenderungsArt.Stammdaten, d =>
            {
                var s = d.Schueler.FirstOrDefault(x => x.Id == w.Schueler) ?? throw StudioFehler.NichtGefunden($"Der Schüler Nr. {w.Schueler}");
                if (d.Unterrichte.All(u => u.Id != w.Kurs)) throw StudioFehler.NichtGefunden($"Der Kurs U-Nr. {w.Kurs}");
                s.KurswahlIds.Remove(w.Kurs);
                if (w.Gewaehlt) s.KurswahlIds.Add(w.Kurs);
            }, doc => Klasse(doc, klasse)));

        // Testhilfe wie im Desktop: Schüler ohne Kurswahl erhalten zufällig je Fach höchstens einen Kurs
        g.MapPost("/{klasse:int}/zufall", (Arbeitsdokument a, int klasse, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kurswahlen", AenderungsArt.Stammdaten, d =>
            {
                var kurse = Kursplanung.Kurse(d, klasse);
                if (kurse.Count == 0) throw StudioFehler.Ungueltig("Die Klasse hat keine Kurse (Unterricht mit Kursname).");
                var rnd = new Random(7);
                foreach (var s in d.Schueler.Where(s => s.KlasseId == klasse && !s.KurswahlIds.Any(id => kurse.Any(k => k.Id == id))))
                    foreach (var gr in kurse.GroupBy(k => k.HauptfachId))
                        if (rnd.NextDouble() < 0.8) s.KurswahlIds.Add(gr.ElementAt(rnd.Next(gr.Count())).Id);
            }, doc => Klasse(doc, klasse)));

        // Bandbildung: nur Vorschlag (Kurs → Band), Übernahme separat
        g.MapPost("/{klasse:int}/bandbildung", (Arbeitsdokument a, int klasse, KursBandbildung b) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var kurse = Kursplanung.Kurse(d, klasse);
            if (kurse.Count == 0) throw StudioFehler.Ungueltig("Die Klasse hat keine Kurse.");
            var vorschlag = Kursplanung.Bandbildung(d, kurse, Math.Clamp(b.MaxBaender, 1, 30));
            var m = Kursplanung.Kollisionsmatrix(d, kurse);
            var koll = m.Where(kv => vorschlag[kv.Key.Item1] == vorschlag[kv.Key.Item2]).Sum(kv => kv.Value);
            return new
            {
                zuordnung = vorschlag,
                baender = vorschlag.Values.Distinct().OrderBy(x => x).Select(nr => new
                {
                    band = $"Band {nr}", nr,
                    kurse = kurse.Where(k => vorschlag[k.Id] == nr).Select(k => $"{k.Kursname} ({Kursplanung.Teilnehmer(d, k.Id)})").ToList(),
                    schueler = kurse.Where(k => vorschlag[k.Id] == nr).Sum(k => Kursplanung.Teilnehmer(d, k.Id)),
                }),
                kollisionen = koll,
                text = $"Vorschlag: {vorschlag.Values.Distinct().Count()} Bänder, {koll} Kollisionen. Mit „Bänder übernehmen“ in den Unterricht schreiben.",
            };
        }));

        g.MapPost("/{klasse:int}/baender", (Arbeitsdokument a, int klasse, KursBaender b, ClaimsPrincipal p) =>
        {
            if (string.IsNullOrWhiteSpace(b.Praefix)) throw StudioFehler.Ungueltig("Bitte ein Präfix für die Bandbezeichnung angeben.");
            if (b.Zuordnung.Count == 0) throw StudioFehler.Ungueltig("Bitte zuerst die Bandbildung starten.");
            return a.AendernAsync(p.Benutzername(), "Bänder übernehmen", AenderungsArt.Unterricht, d => Kursplanung.BaenderUebernehmen(d, b.Praefix.Trim(), b.Zuordnung), doc => Klasse(doc, klasse));
        });
    }
}
