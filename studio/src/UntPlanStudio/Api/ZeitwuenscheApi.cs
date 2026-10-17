using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record ZeitwunschZelle(int Tag, int Stunde, int Wert);
/// <summary>Geänderte Felder (Raster-Editor: überstrichene Stunden) bzw. alle löschen.</summary>
public sealed record ZeitwunschAenderung(List<ZeitwunschZelle>? Aenderungen, bool AlleLoeschen = false);
public sealed record UnbestimmterWunsch(string Bereich, int Anzahl, int Wunsch);
public sealed record UnbestimmteWuensche(List<UnbestimmterWunsch> Liste);

/// <summary>
/// Zeitwünsche (−3 keinesfalls … +3 auf jeden Fall Unterricht) für Klassen, Lehrkräfte, Räume, Fächer und Unterrichte sowie die
/// unbestimmten Zeitwünsche (freie Tage/Halbtage) – wie das Zeitwünsche-Fenster des Desktops.
/// </summary>
public sealed class ZeitwuenscheApi : IStudioModul
{
    public string Name => "zeitwuensche";

    private static readonly ElementTyp[] Erlaubt = { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum, ElementTyp.Fach, ElementTyp.Unterricht };

    private static ElementTyp Typ(string typ)
    {
        var t = Typnamen.Typ(typ, unterrichtErlaubt: true);
        if (!Erlaubt.Contains(t)) throw StudioFehler.NichtGefunden($"Zeitwünsche für \"{typ}\"");
        return t;
    }

    private static Zeitwuensche Wuensche(Schuldatei d, ElementTyp t, int id) => t == ElementTyp.Unterricht
        ? (d.Unterrichte.FirstOrDefault(u => u.Id == id) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {id}")).Zeitwuensche
        : (d.AlleElemente(t).FirstOrDefault(e => e.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)} Nr. {id}")).Zeitwuensche;

    private static void Setzen(Schuldatei d, ElementTyp t, int id, Zeitwuensche z)
    {
        if (t == ElementTyp.Unterricht) d.Unterrichte.First(u => u.Id == id).Zeitwuensche = z;
        else d.AlleElemente(t).First(e => e.Id == id).Zeitwuensche = z;
    }

    private static string BereichText(ZeitbereichArt b) => b switch { ZeitbereichArt.Vormittage => "Vormittage", ZeitbereichArt.Nachmittage => "Nachmittage", _ => "Tage" };

    public static object Dto(Dokument doc, ElementTyp t, int id)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var z = d.Zeitraster;
        var zw = Wuensche(d, t, id);
        object element;
        List<UnbestimmterZeitwunsch>? unbestimmt = null;
        if (t == ElementTyp.Unterricht)
        {
            var u = ix.Unterrichte[id];
            element = new { id, typ = "unterricht", kurz = $"U-Nr. {id}", lang = ix.UnterrichtText(u) };
        }
        else
        {
            var e = d.AlleElemente(t).First(x => x.Id == id);
            element = new { id, typ = Typnamen.Schluessel(t), kurz = e.Kurzname, lang = e is Lehrer l ? l.VollerName : e.Langname };
            unbestimmt = e.UnbestimmteZeitwuensche;
        }
        return new
        {
            element,
            tage = Enumerable.Range(0, z.TageProWoche).Select(x => new { nr = x, kurz = Planungsdienst.Tagname(d, x), lang = Planungsdienst.Tagname(d, x, true) }),
            stunden = Enumerable.Range(1, z.StundenProTag).Select(s => new { nr = s, label = z.StundenLabel(s), vormittag = s <= z.LetzteVormittagsstunde }),
            werte = Enumerable.Range(0, z.TageProWoche).Select(x => Enumerable.Range(1, z.StundenProTag).Select(s => zw.Get(x, s)).ToArray()).ToArray(),
            verfuegbar = Enumerable.Range(0, z.TageProWoche).Select(x => Enumerable.Range(1, z.StundenProTag).Select(s => z.Verfuegbar(x, s)).ToArray()).ToArray(),
            unbestimmte = unbestimmt?.Select(u => new { bereich = BereichText(u.Bereich), anzahl = u.Anzahl, wunsch = u.Wunsch, text = UnbestimmterZeitwunsch.WunschText(u.Wunsch) }),
            anzahl = zw.Werte.Count,
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/zeitwuensche");

        // Übersicht: Elemente eines Typs mit Anzahl gesetzter Zeitwünsche (Unterricht: mit Text für die Auswahl)
        g.MapGet("/{typ}", (Arbeitsdokument a, string typ) => a.LesenAsync(doc =>
        {
            var t = Typ(typ);
            var ix = doc.Index;
            if (t == ElementTyp.Unterricht)
                return doc.Daten.Unterrichte.OrderBy(u => u.Id).Select(u => new { id = u.Id, k = u.Id.ToString(), l = ix.UnterrichtText(u), n = u.Zeitwuensche.Werte.Count, f = 0 }).ToList();
            return Stammdaten.Sortiert(doc.Daten, t).Select(e => new { id = e.Id, k = e.Kurzname, l = e is Lehrer le ? le.VollerName : e.Langname, n = e.Zeitwuensche.Werte.Count, f = e.UnbestimmteZeitwuensche.Count }).ToList();
        }));

        g.MapGet("/{typ}/{id:int}", (Arbeitsdokument a, string typ, int id) => a.LesenAsync(doc =>
        {
            var t = Typ(typ);
            Wuensche(doc.Daten, t, id);
            return Dto(doc, t, id);
        }));

        g.MapPut("/{typ}/{id:int}", (Arbeitsdokument a, string typ, int id, ZeitwunschAenderung aen, ClaimsPrincipal p) =>
        {
            var t = Typ(typ);
            return a.AendernAsync(p.Benutzername(), aen.AlleLoeschen ? "Zeitwünsche gelöscht" : "Zeitwünsche geändert", AenderungsArt.Stammdaten, d =>
            {
                var neu = aen.AlleLoeschen ? new Zeitwuensche() : Wuensche(d, t, id).Kopie();
                foreach (var z in aen.Aenderungen ?? new())
                {
                    if (z.Tag < 0 || z.Tag >= d.Zeitraster.TageProWoche || z.Stunde < 1 || z.Stunde > d.Zeitraster.StundenProTag) throw StudioFehler.Ungueltig("Ungültige Stunde.");
                    if (z.Wert is < -3 or > 3) throw StudioFehler.Ungueltig("Zeitwünsche liegen zwischen -3 und +3.");
                    neu.Set(z.Tag, z.Stunde, z.Wert);
                }
                Setzen(d, t, id, neu);
            }, doc => Dto(doc, t, id));
        });

        g.MapPut("/{typ}/{id:int}/unbestimmte", (Arbeitsdokument a, string typ, int id, UnbestimmteWuensche w, ClaimsPrincipal p) =>
        {
            var t = Typ(typ);
            if (t == ElementTyp.Unterricht) throw StudioFehler.Ungueltig("Unterrichte haben keine unbestimmten Zeitwünsche.");
            var liste = (w.Liste ?? new()).Select(x => new UnbestimmterZeitwunsch
            {
                Bereich = (x.Bereich ?? "").ToLowerInvariant() switch
                {
                    "vormittage" or "vormittag" => ZeitbereichArt.Vormittage,
                    "nachmittage" or "nachmittag" => ZeitbereichArt.Nachmittage,
                    "tage" or "tag" => ZeitbereichArt.Tage,
                    _ => throw StudioFehler.Ungueltig($"Unbekannter Bereich \"{x.Bereich}\" (Tage, Vormittage, Nachmittage)."),
                },
                Anzahl = Math.Clamp(x.Anzahl, 1, 7),
                Wunsch = Math.Clamp(x.Wunsch, -3, -1),
            }).ToList();
            return a.AendernAsync(p.Benutzername(), "Unbestimmte Zeitwünsche", AenderungsArt.Stammdaten,
                d => (d.AlleElemente(t).FirstOrDefault(e => e.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)} Nr. {id}")).UnbestimmteZeitwuensche = liste,
                doc => Dto(doc, t, id));
        });
    }
}
