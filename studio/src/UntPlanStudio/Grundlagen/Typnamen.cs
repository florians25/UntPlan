using System.Security.Claims;
using UntPlan.Core.Model;

namespace UntPlanStudio.Grundlagen;

/// <summary>Anzeigenamen und URL-Schlüssel der Elementtypen (in Routen klein geschrieben: klasse, lehrer, raum, fach …).</summary>
public static class Typnamen
{
    public static string Name(ElementTyp t, bool mehrzahl = false) => t switch
    {
        ElementTyp.Klasse => mehrzahl ? "Klassen" : "Klasse",
        ElementTyp.Lehrer => mehrzahl ? "Lehrkräfte" : "Lehrkraft",
        ElementTyp.Raum => mehrzahl ? "Räume" : "Raum",
        ElementTyp.Fach => mehrzahl ? "Fächer" : "Fach",
        ElementTyp.Raumgruppe => mehrzahl ? "Raumgruppen" : "Raumgruppe",
        ElementTyp.Gang => mehrzahl ? "Gänge" : "Gang",
        ElementTyp.Abteilung => mehrzahl ? "Abteilungen" : "Abteilung",
        ElementTyp.Abwesenheitsgrund => mehrzahl ? "Abwesenheitsgründe" : "Abwesenheitsgrund",
        ElementTyp.Schueler => "Schüler",
        _ => "Unterricht",
    };

    public static string Schluessel(ElementTyp t) => t.ToString().ToLowerInvariant();

    /// <summary>Routenwert → Elementtyp (Groß-/Kleinschreibung egal); wirft 404 bei unbekanntem Typ.</summary>
    public static ElementTyp Typ(string? s, bool unterrichtErlaubt = false)
    {
        if (Enum.TryParse<ElementTyp>(s, true, out var t) && (unterrichtErlaubt || t != ElementTyp.Unterricht) && Enum.IsDefined(t)) return t;
        throw StudioFehler.NichtGefunden($"Der Elementtyp \"{s}\"");
    }
}

public static class BenutzerErweiterungen
{
    /// <summary>Anmeldename des Benutzers (für Protokoll, Ereignisse, Benutzereinstellungen).</summary>
    public static string Benutzername(this ClaimsPrincipal p) => p.Identity?.Name ?? "?";
}
