using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core;

/// <summary>Zeile der Stundenliste (Stundenplan in Listenform wie ein Vorlesungsverzeichnis).</summary>
public sealed record StundenlistenZeile(string Element, int Tag, string Tagname, int Stunde, string Zeit, string Fach, string Lehrer, string Klassen, string Raum, int UnterrichtId, string Text);

/// <summary>
/// Handbuch 6.4.4 Stundenlisten: der Stundenplan nicht als Matrix, sondern als Liste – sortiert nach Klassen oder nach Lehrkräften,
/// dann nach Tag und Stunde. Je Element und Stunde eine Zeile (bei Kopplungen je beteiligter Kopplungszeile).
/// </summary>
public static class Stundenliste
{
    public static List<StundenlistenZeile> Zeilen(Schuldatei d, bool nachLehrern)
    {
        var ix = new DatenIndex(d);
        var liste = new List<StundenlistenZeile>();
        var typ = nachLehrern ? ElementTyp.Lehrer : ElementTyp.Klasse;
        foreach (var e in Stammdaten.Sortiert(d, typ))
        {
            foreach (var p in ix.PlanVon(typ, e.Id).OrderBy(p => p.Tag).ThenBy(p => p.Stunde))
            {
                if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
                var zeit = d.Zeitraster.Zeit(p.Tag, p.Stunde) is { } z ? $"{z.Beginn:H\\:mm}-{z.Ende:H\\:mm}" : "";
                for (var i = 0; i < u.Zeilen.Count; i++)
                {
                    var zl = u.Zeilen[i];
                    if (nachLehrern ? zl.LehrerId != e.Id : !zl.KlassenIds.Contains(e.Id)) continue;
                    liste.Add(new StundenlistenZeile(e.Kurzname, p.Tag, Planungsdienst.Tagname(d, p.Tag), p.Stunde, zeit, ix.KFach(zl.FachId), ix.KLehrer(zl.LehrerId),
                        ix.KKlassen(zl.KlassenIds), ix.KRaum(p.RaumFuerZeile(i)), u.Id, string.Join(" ", new[] { u.Text, p.Text }.Where(t => t.Length > 0))));
                }
            }
        }
        return liste;
    }
}
