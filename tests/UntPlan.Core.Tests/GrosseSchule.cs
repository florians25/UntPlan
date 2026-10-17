using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.Core.Tests;

/// <summary>
/// Generierte große Schule für Tests mit vielen Elementen und Messungen: 120 Lehrkräfte, 60 Klassen, 60 Räume,
/// je Klasse 30 Wochenstunden (Mo–Fr, 1.–6. Stunde) ohne Kollisionen.
/// </summary>
internal static class GrosseSchule
{
    public static Schuldatei Erzeugen(int lehrer = 120, int klassen = 60)
    {
        var d = Demo.NeueDatei("Große Testschule", "GY");
        for (var i = 0; i < lehrer; i++)
            d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = $"L{i:000}", Langname = $"Lehrkraft {i:000}", Vorname = "Vorname" });
        for (var i = 0; i < klassen; i++)
        {
            var raum = new Raum { Id = d.NeueId(), Kurzname = $"R{i:000}", Langname = $"Raum {i:000}" };
            d.Raeume.Add(raum);
            d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = $"{5 + i / 6}{(char)('a' + i % 6)}", Jahrgang = $"{5 + i / 6}", StammraumId = raum.Id });
        }
        var faecher = d.Faecher.Where(f => !f.Bereitschaft).Take(10).ToList();
        var p = d.AktivePeriode;
        // je Klasse 10 Unterrichte à 3 Stunden; in jedem Zeitfeld unterrichten alle Klassen denselben Unterricht j = Feld % 10
        // mit verschiedenen Lehrkräften (Klasse c, j gerade: Lehrkraft c, j ungerade: Lehrkraft c + Klassenzahl)
        var u = new Unterricht[klassen, 10];
        for (var c = 0; c < klassen; c++)
            for (var j = 0; j < 10; j++)
            {
                var l = d.Lehrer[(c + (j % 2) * klassen) % lehrer];
                u[c, j] = new Unterricht
                {
                    Id = d.NeueId(), Wochenstunden = 3,
                    Zeilen = { new Kopplungszeile { LehrerId = l.Id, FachId = faecher[j].Id, KlassenIds = { d.Klassen[c].Id } } },
                };
                d.Unterrichte.Add(u[c, j]);
            }
        var feld = 0;
        for (var t = 0; t < 5; t++)
            for (var s = 1; s <= 6; s++, feld++)
                for (var c = 0; c < klassen; c++)
                    p.Plan.Add(new Planstunde { UnterrichtId = u[c, feld % 10].Id, Tag = t, Stunde = s, Raeume = { d.Klassen[c].StammraumId } });
        Dokument.Normalisieren(d);
        return d;
    }

    public static DateOnly Tag(Schuldatei d) => Kalender.NaechsterUnterrichtstag(d, d.Einstellungen.SchuljahrVon.AddDays(20));

    /// <summary>Lehrkräftekonferenz bzw. pädagogischer Tag: alle Lehrkräfte, alle Klassen.</summary>
    public static Veranstaltung AlleVeranstaltung(Schuldatei d, DateOnly tag, int von = 1, int bis = 99, string text = "Pädagogischer Tag")
    {
        var va = new Veranstaltung
        {
            Id = d.NeueId(), Text = text, Von = tag, Bis = tag, StundeVon = von, StundeBis = bis,
            KlassenIds = d.Klassen.Select(k => k.Id).ToList(), LehrerIds = d.Lehrer.Select(l => l.Id).ToList(),
        };
        d.Veranstaltungen.Add(va);
        return va;
    }
}
