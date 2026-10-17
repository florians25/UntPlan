using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record FormatKopie(string Name, string Neu);

/// <summary>
/// Eigene Stundenplanformate wie im Desktop (Formatdialog: Allgemein, Auswahl (Bereich), Layout 1/2, HTML, Grundform,
/// Stundenplan-Stunde mit Feldern). Die Formate liegen in der Schuldatei (<c>Formate</c>) und werden beim Drucken gewählt.
/// </summary>
public sealed class FormateApi : IStudioModul
{
    public string Name => "formate";

    /// <summary>Standardformate je Planart (Vorschläge, wenn die Datei noch keine eigenen Formate hat).</summary>
    public static IEnumerable<StundenplanFormat> Vorlagen() => new[]
    {
        new StundenplanFormat { Name = "Klassenplan", Typ = ElementTyp.Klasse, Gross = true, Schriftgroesse = 10, PausenAnzeigen = true },
        new StundenplanFormat { Name = "Klassenplan kompakt", Typ = ElementTyp.Klasse, Gross = false, Schriftgroesse = 9 },
        new StundenplanFormat { Name = "Lehrerplan", Typ = ElementTyp.Lehrer, Gross = true, Schriftgroesse = 10, PausenAnzeigen = true },
        new StundenplanFormat { Name = "Lehrerplan kompakt", Typ = ElementTyp.Lehrer, Gross = false, Schriftgroesse = 9 },
        new StundenplanFormat { Name = "Raumplan", Typ = ElementTyp.Raum, Gross = true, Schriftgroesse = 10, PausenAnzeigen = true },
        new StundenplanFormat { Name = "Lehrer Übersicht", Typ = ElementTyp.Lehrer, Grundform = Grundform.Format30, Schriftgroesse = 8 },
    };

    private static object Liste(Schuldatei d) => new
    {
        // Kopien – Core-Objekte nicht außerhalb der Dokumentsperre serialisieren
        formate = d.Formate.Select(Kopie).ToList(),
        vorlagen = Vorlagen().Where(v => d.Formate.All(f => !f.Name.Equals(v.Name, StringComparison.CurrentCultureIgnoreCase))),
        grundformen = Enum.GetValues<Grundform>().Select(g => new { wert = g.ToString(), text = StundenplanFormat.GrundformText(g) }),
        feldarten = Enum.GetValues<StundenFeldArt>().Select(a => a.ToString()),
    };

    private static StundenplanFormat Kopie(StundenplanFormat f) =>
        System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(f, Dokument.JsonOptionen), Dokument.JsonOptionen)!;

    private static void Pruefen(StundenplanFormat f)
    {
        if (string.IsNullOrWhiteSpace(f.Name)) throw StudioFehler.Ungueltig("Bitte einen Formatnamen angeben.");
        if (f.Typ is not (ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach)) throw StudioFehler.Ungueltig("Formate gibt es für Klassen, Lehrkräfte, Räume und Fächer.");
        f.Name = f.Name.Trim();
        f.Schriftgroesse = Math.Clamp(f.Schriftgroesse, 6, 20);
        f.VonStunde = Math.Clamp(f.VonStunde, 1, 16);
        f.BisStunde = Math.Clamp(f.BisStunde, 0, 16);
        if (f.BisStunde != 0 && f.BisStunde < f.VonStunde) throw StudioFehler.Ungueltig("Die letzte Stunde liegt vor der ersten.");
        f.VonTag = Math.Clamp(f.VonTag, 0, 6);
        f.BisTag = Math.Clamp(f.BisTag, -1, 6);
        if (f.BisTag >= 0 && f.BisTag < f.VonTag) throw StudioFehler.Ungueltig("Der letzte Tag liegt vor dem ersten.");
        f.ZellenBreite = Math.Clamp(f.ZellenBreite, 0, 400);
        f.ZellenHoehe = Math.Clamp(f.ZellenHoehe, 0, 200);
        foreach (var fe in f.Felder)
        {
            fe.X = Math.Clamp(fe.X, 0, 1); fe.Y = Math.Clamp(fe.Y, 0, 1);
            fe.B = Math.Clamp(fe.B, 0.05f, 1 - fe.X); fe.H = Math.Clamp(fe.H, 0.05f, 1 - fe.Y);
            fe.SchriftProzent = Math.Clamp(fe.SchriftProzent, 30, 300);
            fe.Zeile = Math.Clamp(fe.Zeile, 1, 20);
            fe.Ausrichtung = Math.Clamp(fe.Ausrichtung, 0, 2);
        }
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/formate");
        g.MapGet("", (Arbeitsdokument a) => a.LesenAsync(doc => Liste(doc.Daten)));

        // Format speichern (neu oder ersetzen); {name} = bisheriger Name (Umbenennen über f.Name)
        g.MapPut("/{name}", (Arbeitsdokument a, string name, StundenplanFormat f, ClaimsPrincipal p) =>
        {
            Pruefen(f);
            return a.AendernAsync(p.Benutzername(), $"Stundenplanformat „{f.Name}“", AenderungsArt.Einstellungen, d =>
            {
                var i = d.Formate.FindIndex(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                if (d.Formate.Where((x, k) => k != i).Any(x => x.Name.Equals(f.Name, StringComparison.CurrentCultureIgnoreCase)))
                    throw StudioFehler.Ungueltig($"Ein Format „{f.Name}“ gibt es bereits.");
                if (i >= 0) d.Formate[i] = f; else d.Formate.Add(f);
            }, doc => Liste(doc.Daten));
        });

        g.MapPost("/kopieren", (Arbeitsdokument a, FormatKopie k, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"Stundenplanformat „{k.Neu}“ kopiert", AenderungsArt.Einstellungen, d =>
            {
                var quelle = d.Formate.FirstOrDefault(x => x.Name.Equals(k.Name, StringComparison.CurrentCultureIgnoreCase))
                    ?? Vorlagen().FirstOrDefault(x => x.Name.Equals(k.Name, StringComparison.CurrentCultureIgnoreCase))
                    ?? throw StudioFehler.NichtGefunden($"Das Format „{k.Name}“");
                var neu = Kopie(quelle);
                neu.Name = string.IsNullOrWhiteSpace(k.Neu) ? quelle.Name + " (Kopie)" : k.Neu.Trim();
                if (d.Formate.Any(x => x.Name.Equals(neu.Name, StringComparison.CurrentCultureIgnoreCase))) throw StudioFehler.Ungueltig($"Ein Format „{neu.Name}“ gibt es bereits.");
                d.Formate.Add(neu);
            }, doc => Liste(doc.Daten)));

        g.MapDelete("/{name}", (Arbeitsdokument a, string name, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"Stundenplanformat „{name}“ gelöscht", AenderungsArt.Einstellungen, d =>
            {
                if (d.Formate.RemoveAll(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)) == 0) throw StudioFehler.NichtGefunden($"Das Format „{name}“");
            }, doc => Liste(doc.Daten)));
    }
}
