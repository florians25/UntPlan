using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record Raumverlegung(int Unterricht, int Tag, int Stunde, int AlterRaum, int NeuerRaum);

/// <summary>
/// Übersichtspläne wie im Desktop („Klassen/Lehrer/Räume Übersicht quer“): alle Elemente einer Art als Zeilen, Tage × Stunden als
/// Spalten. Die Antwort ist kompakt (eine Liste je Element mit Text-Arrays) und wird in einem Durchlauf über den Stundenplan
/// berechnet – auch bei großen Schulen schnell. Im Raum-Übersichtsplan: Raumverlegung/-tausch per Drag &amp; Drop.
/// </summary>
public sealed class UebersichtApi : IStudioModul
{
    public string Name => "uebersicht";

    /// <summary>Was in einer Zelle steht: Standard wie im Desktop (Lehrerplan: Klassen, Klassenplan: Lehrkräfte, Raumplan: Klassen) oder ein bestimmtes Element.</summary>
    public enum Inhalt { Standard, Fach, Lehrer, Klasse, Raum }

    /// <summary>Eine belegte Zelle eines Übersichtsplans.</summary>
    public sealed record Eintrag(int Tag, int Stunde, int U, string Text, string Fach, string Lehrer, string Klassen, string Raum, bool Fix, int Farbe);

    /// <summary>Belegung aller Elemente einer Art in einem Durchlauf über den Stundenplan der aktiven Periode.</summary>
    public static Dictionary<int, List<Eintrag>> Belegung(Schuldatei d, DatenIndex ix, ElementTyp typ, Inhalt inhalt)
    {
        var res = new Dictionary<int, List<Eintrag>>();
        foreach (var p in d.Stundenplan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            // Element-Ids mit den Kopplungszeilen, die sie betreffen
            var jeElement = new Dictionary<int, List<int>>();
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                IEnumerable<int> ids = typ switch
                {
                    ElementTyp.Klasse => z.KlassenIds,
                    ElementTyp.Lehrer => z.LehrerId is int l ? new[] { l } : Array.Empty<int>(),
                    ElementTyp.Fach => z.FachId is int f ? new[] { f } : Array.Empty<int>(),
                    ElementTyp.Raum => p.RaumFuerZeile(i) is int r ? new[] { r } : Array.Empty<int>(),
                    _ => Array.Empty<int>(),
                };
                foreach (var id in ids)
                {
                    if (!jeElement.TryGetValue(id, out var l2)) jeElement[id] = l2 = new List<int>();
                    l2.Add(i);
                }
            }
            foreach (var (id, zeilen) in jeElement)
            {
                var fach = string.Join(",", zeilen.Select(i => ix.KFach(u.Zeilen[i].FachId)).Distinct());
                var lehrer = string.Join(",", zeilen.Select(i => ix.KLehrer(u.Zeilen[i].LehrerId)).Where(x => x.Length > 0).Distinct());
                var klassen = string.Join(",", zeilen.SelectMany(i => u.Zeilen[i].KlassenIds).Distinct().Select(k => ix.Klassen.GetValueOrDefault(k)?.Kurzname ?? "?"));
                var raum = string.Join(",", zeilen.Select(i => p.RaumFuerZeile(i) is int r ? ix.KRaum(r) : "").Where(x => x.Length > 0).Distinct());
                var text = (inhalt, typ) switch
                {
                    (Inhalt.Fach, _) => fach,
                    (Inhalt.Lehrer, _) => lehrer,
                    (Inhalt.Klasse, _) => klassen,
                    (Inhalt.Raum, _) => raum,
                    (_, ElementTyp.Klasse) => lehrer,
                    (_, ElementTyp.Fach) => klassen,
                    _ => klassen,
                };
                var fa = u.HauptfachId is int hf ? ix.Faecher.GetValueOrDefault(hf) : null;
                if (!res.TryGetValue(id, out var liste)) res[id] = liste = new List<Eintrag>();
                liste.Add(new Eintrag(p.Tag, p.Stunde, u.Id, text, fach, lehrer, klassen, raum, p.Fixiert || u.Fix, fa?.Hintergrund ?? 0));
            }
        }
        return res;
    }

    /// <summary>Zellentexte je Element für den Druck (mehrere Unterrichte in einer Stunde durch „/“ getrennt, fixierte mit „*“).</summary>
    public static Dictionary<(int Tag, int Stunde), string> Zellen(List<Eintrag>? liste) =>
        (liste ?? new()).GroupBy(e => (e.Tag, e.Stunde)).ToDictionary(g => g.Key, g => string.Join("/", g.Select(e => (e.Fix ? "*" : "") + e.Text)));

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/uebersicht");

        g.MapGet("/{typ}", (Arbeitsdokument a, string typ, string? inhalt) => a.LesenAsync(doc =>
        {
            var t = Typnamen.Typ(typ);
            if (t is not (ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach)) throw StudioFehler.Ungueltig("Übersichtspläne gibt es für Klassen, Lehrkräfte, Räume und Fächer.");
            var art = Enum.TryParse<Inhalt>(inhalt, true, out var i) ? i : Inhalt.Standard;
            var d = doc.Daten;
            var ix = doc.Index;
            var elemente = Stammdaten.Sortiert(d, t).ToList();
            var belegung = Belegung(d, ix, t, art);
            return new
            {
                typ = Typnamen.Schluessel(t),
                titel = $"{ListenPdf.TypText(t)} Übersicht",
                inhalt = art.ToString(),
                zeitraster = DokumenteApi.Zeitraster(d),
                periode = d.AktivePeriode.Name,
                elemente = elemente.Select(e => new { id = e.Id, k = e.Kurzname, l = e is Lehrer l ? l.VollerName : e.Langname }),
                // je Element: [tag, stunde, u, text, titel, farbe, fix] – kompakt für große Schulen
                zellen = elemente.Select(e => belegung.TryGetValue(e.Id, out var l)
                    ? l.Select(x => new object[] { x.Tag, x.Stunde, x.U, x.Text, $"U-Nr. {x.U}: {x.Fach} · {x.Lehrer} · {x.Klassen}{(x.Raum.Length > 0 ? " · " + x.Raum : "")}", StammdatenApi.Farbe(x.Farbe) ?? "", x.Fix ? 1 : 0 })
                    : Enumerable.Empty<object[]>()),
            };
        }));

        // Raumverlegung bzw. -tausch im Raum-Übersichtsplan (Stunde in eine andere Raumzeile gezogen)
        g.MapPost("/raum", (Arbeitsdokument a, Raumverlegung n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Raumverlegung", AenderungsArt.Stundenplan, d =>
            {
                var ps = d.Stundenplan.FirstOrDefault(x => x.UnterrichtId == n.Unterricht && x.Tag == n.Tag && x.Stunde == n.Stunde)
                    ?? throw StudioFehler.NichtGefunden($"Die Stunde von U-Nr. {n.Unterricht}");
                if (d.Raeume.All(x => x.Id != n.NeuerRaum)) throw StudioFehler.NichtGefunden($"Der Raum Nr. {n.NeuerRaum}");
                var z = ps.Raeume.IndexOf(n.AlterRaum);
                Planungsdienst.RaumZuordnen(d, ps, Math.Max(0, z), n.NeuerRaum, Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen);
            }, _ => new { ok = true }));
    }
}
