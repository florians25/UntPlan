using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>Eine Kopplungszeile einer verplanten Stunde im Stundenplan-Raster.</summary>
public sealed record PlanEintrag(int U, int Zeile, string Fach, string Lehrer, string Klassen, string Raum, bool Fix, bool Gekoppelt, string? Farbe, string? Schrift, string Text, string? Woche = null);
public sealed record PlanZelle(int Tag, int Stunde, List<PlanEintrag> Eintraege);
public sealed record Stundenbewegung(int Unterricht, int Tag, int Stunde, int ZielTag, int ZielStunde, bool Tauschen = true);
public sealed record Stundenposition(int Unterricht, int Tag, int Stunde, bool Fixieren = false);

/// <summary>
/// Stundenplan der aktiven Periode je Klasse/Lehrkraft/Raum/Fach (Wochenraster). Dazu – für das manuelle Planen per Drag &amp; Drop –
/// Zielstatus aller Felder, Verplanen, Verschieben/Tauschen, Entplanen und Fixieren über <see cref="Planungsdienst"/>.
/// </summary>
public sealed class StundenplanApi : IStudioModul
{
    public string Name => "stundenplan";

    /// <summary>Kopplungszeilen eines Unterrichts, die für das Element des Plans gelten (Raum: tatsächlich verplanter Raum).</summary>
    private static IEnumerable<int> RelevanteZeilen(ElementTyp typ, int id, Unterricht u, Planstunde p)
    {
        var alle = Enumerable.Range(0, u.Zeilen.Count);
        var passend = typ switch
        {
            ElementTyp.Klasse => alle.Where(i => u.Zeilen[i].KlassenIds.Contains(id)),
            ElementTyp.Lehrer => alle.Where(i => u.Zeilen[i].LehrerId == id),
            ElementTyp.Fach => alle.Where(i => u.Zeilen[i].FachId == id),
            ElementTyp.Raum => alle.Where(i => p.RaumFuerZeile(i) == id),
            _ => alle,
        };
        var l = passend.ToList();
        return l.Count > 0 ? l : alle;
    }

    /// <summary>Wochenkennung eines Unterrichts (A-/B-Woche bzw. Wochen der Unterrichtsgruppe), null = jede Woche.</summary>
    public static string? Woche(Schuldatei d, Unterricht u)
    {
        if (u.Periodizitaet == Periodizitaet.AWoche) return "A";
        if (u.Periodizitaet == Periodizitaet.BWoche) return "B";
        if (u.UGruppeId is int g && d.UGruppen.FirstOrDefault(x => x.Id == g) is { } ug && !string.IsNullOrWhiteSpace(ug.Wochen)) return ug.Wochen.Trim().ToUpperInvariant();
        return null;
    }

    public static List<PlanZelle> Zellen(Dokument doc, ElementTyp typ, int id)
    {
        var ix = doc.Index;
        var zellen = new Dictionary<(int, int), PlanZelle>();
        foreach (var p in ix.PlanVon(typ, id))
        {
            var u = ix.Unterrichte[p.UnterrichtId];
            if (!zellen.TryGetValue((p.Tag, p.Stunde), out var zelle)) zellen[(p.Tag, p.Stunde)] = zelle = new PlanZelle(p.Tag, p.Stunde, new());
            foreach (var i in RelevanteZeilen(typ, id, u, p))
            {
                var z = u.Zeilen[i];
                var fach = z.FachId is int f ? ix.Faecher.GetValueOrDefault(f) : null;
                var raum = p.RaumFuerZeile(i);
                zelle.Eintraege.Add(new PlanEintrag(u.Id, i, ix.KFach(z.FachId), ix.KLehrer(z.LehrerId), ix.KKlassen(z.KlassenIds),
                    raum is int r ? ix.KRaum(r) : "", p.Fixiert || u.Fix, u.Zeilen.Count > 1,
                    StammdatenApi.Farbe(fach?.Hintergrund ?? 0), StammdatenApi.Farbe(fach?.Vordergrund ?? 0), u.Text, Woche(doc.Daten, u)));
            }
        }
        return zellen.Values.OrderBy(z => z.Tag).ThenBy(z => z.Stunde).ToList();
    }

    private static Planstunde Stunde(Schuldatei d, int unterricht, int tag, int stunde) =>
        d.Stundenplan.FirstOrDefault(p => p.UnterrichtId == unterricht && p.Tag == tag && p.Stunde == stunde)
        ?? throw StudioFehler.NichtGefunden($"Die Stunde von U-Nr. {unterricht} am Tag {tag + 1}, Stunde {stunde}");

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/stundenplan");

        g.MapGet("/{typ}/{id:int}", (Arbeitsdokument a, string typ, int id) => a.LesenAsync(doc =>
        {
            var t = Typnamen.Typ(typ);
            var e = doc.Daten.AlleElemente(t).FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)} Nr. {id}");
            var ix = doc.Index;
            var nicht = ix.UnterrichteVon(t, id).Where(u => ix.NichtVerplant(u) > 0)
                .Select(u => new { u = u.Id, text = ix.UnterrichtText(u), anzahl = ix.NichtVerplant(u), bloecke = Planungsdienst.OffeneBloecke(doc.Daten, ix, u) });
            return new
            {
                element = new { id = e.Id, typ = Typnamen.Schluessel(t), kurz = e.Kurzname, lang = e is Lehrer l ? l.VollerName : e.Langname },
                zeitraster = DokumenteApi.Zeitraster(doc.Daten),
                zellen = Zellen(doc, t, id),
                nichtVerplant = nicht,
                periode = doc.Daten.AktivePeriode.Name,
            };
        }));

        // Zielstatus aller Felder beim Ziehen einer Stunde: Frei, VorgabeVerletzt, KeinRaum, TauschMoeglich, Kollision
        g.MapGet("/zielstatus", (Arbeitsdokument a, int unterricht, int? tag, int? stunde) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var u = ix.Unterrichte.GetValueOrDefault(unterricht) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {unterricht}");
            var gezogen = tag is int tg && stunde is int st ? Planungsdienst.BlockVon(d, Stunde(d, unterricht, tg, st)) : null;
            var z = d.Zeitraster;
            return Enumerable.Range(0, z.TageProWoche).Select(t => Enumerable.Range(1, z.StundenProTag).Select(s =>
                (gezogen is null ? Planungsdienst.Status(d, ix, u, t, s) : Planungsdienst.Zielstatus(d, ix, gezogen, t, s)).ToString()).ToArray()).ToArray();
        }));

        g.MapPost("/verplanen", (Arbeitsdokument a, Stundenposition s, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"U-Nr. {s.Unterricht} verplant", AenderungsArt.Stundenplan, d =>
            {
                var u = d.Unterrichte.FirstOrDefault(x => x.Id == s.Unterricht) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {s.Unterricht}");
                if (s.Tag < 0 || s.Tag >= d.Zeitraster.TageProWoche || s.Stunde < 1 || s.Stunde > d.Zeitraster.StundenProTag) throw StudioFehler.Ungueltig("Ungültige Position.");
                if (d.Stundenplan.Count(x => x.UnterrichtId == u.Id) >= u.Wochenstunden) throw StudioFehler.Ungueltig($"U-Nr. {u.Id} ist bereits vollständig verplant.");
                Planungsdienst.Verplanen(d, u, s.Tag, s.Stunde, behinderndeEntplanen: true, s.Fixieren);
            }, _ => new { ok = true }));

        g.MapPost("/verschieben", (Arbeitsdokument a, Stundenbewegung b, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"U-Nr. {b.Unterricht} verschoben", AenderungsArt.Stundenplan, d =>
            {
                if (b.ZielTag < 0 || b.ZielTag >= d.Zeitraster.TageProWoche || b.ZielStunde < 1 || b.ZielStunde > d.Zeitraster.StundenProTag) throw StudioFehler.Ungueltig("Ungültige Zielposition.");
                var pl = Stunde(d, b.Unterricht, b.Tag, b.Stunde);
                if (pl.Fixiert) throw StudioFehler.Ungueltig("Die Stunde ist fixiert.");
                Planungsdienst.Verschieben(d, pl, b.ZielTag, b.ZielStunde, b.Tauschen, behinderndeEntplanen: false);
            }, _ => new { ok = true }));

        g.MapPost("/entplanen", (Arbeitsdokument a, Stundenposition s, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"U-Nr. {s.Unterricht} entplant", AenderungsArt.Stundenplan,
                d => Planungsdienst.Entplanen(d, Stunde(d, s.Unterricht, s.Tag, s.Stunde)), _ => new { ok = true }));

        g.MapPost("/fixieren", (Arbeitsdokument a, Stundenposition s, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), s.Fixieren ? "Stunde fixiert" : "Fixierung aufgehoben", AenderungsArt.Stundenplan,
                d => Stunde(d, s.Unterricht, s.Tag, s.Stunde).Fixiert = s.Fixieren, _ => new { ok = true }));
    }
}
