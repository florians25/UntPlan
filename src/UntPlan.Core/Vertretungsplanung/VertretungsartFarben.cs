using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Einstellungen › Vertretungsplanung › Farben und Bezeichnungen: Zeilen der Tabelle (Art, Vordergrund, Hintergrund, Bezeichnung) in der
/// Reihenfolge des Vorbilds, die Standardfarben je Art und die wirksamen Farben (gespeicherter Wert, sonst Standard). Schlüssel sind die
/// Namen der <see cref="VertretungsArt"/>; Arten ohne eigenen Eintrag in der Aufzählung („Neue Vertretung“, „Unterrichtsfrei“, …) haben
/// eigene Schlüssel.
/// </summary>
public static class VertretungsartFarben
{
    /// <summary>Zeile der Tabelle: Schlüssel, Anzeige in der Spalte „Art“, Standardvordergrund, Standardhintergrund (RGB, null = keiner).</summary>
    public sealed record Zeile(string Schluessel, string Art, int Vordergrund, int? Hintergrund);

    private const int Schwarz = 0x000000;

    /// <summary>Zeilen in der Reihenfolge des Vorbilds; zusätzliche Arten dieses Programms stehen am Ende.</summary>
    public static readonly IReadOnlyList<Zeile> Zeilen = new Zeile[]
    {
        new("NeueVertretung", "Neue Vertretung", 0xFF0000, null),
        new(nameof(VertretungsArt.Verlegt), "verlegt", Schwarz, null),
        new(nameof(VertretungsArt.Verlegung), "Verlegung", Schwarz, null),
        new(nameof(VertretungsArt.Tausch), "Tausch", 0x0080FF, null),
        new(nameof(VertretungsArt.Betreuung), "Betreuung", Schwarz, null),
        new(nameof(VertretungsArt.Sondereinsatz), "Sondereins.", 0x008080, 0xDDFFDD),
        new(nameof(VertretungsArt.Entfall), "Entfall", 0xFF0000, null),
        new(nameof(VertretungsArt.Freisetzung), "Freisetzung", Schwarz, null),
        new(nameof(VertretungsArt.Teilvertretung), "Teil-Vtr.", Schwarz, null),
        new(nameof(VertretungsArt.Raumvertretung), "Raum-Vtr.", 0xFF0080, null),
        new(nameof(VertretungsArt.Pausenaufsicht), "Pausenaufsicht", Schwarz, 0x80FFFF),
        new("Unterrichtsfrei", "Unterrichtsfrei", 0xFF0000, null),
        new(nameof(VertretungsArt.Lehrertausch), "Lehrertausch", Schwarz, null),
        new(nameof(VertretungsArt.Klausur), "Klausur", Schwarz, null),
        new(nameof(VertretungsArt.EigenverantwortlichesArbeiten), "eigenverantwortliches Arbeiten", 0x400040, null),
        new(nameof(VertretungsArt.BereitschaftEntfaellt), "Bereitschaft entfällt", Schwarz, null),
        new(nameof(VertretungsArt.TrotzAbsenz), "Trotz Absenz", 0xFF0080, null),
        new(nameof(VertretungsArt.Vormerkung), "Vormerkung", 0x8000FF, null),
        new(nameof(VertretungsArt.Veranstaltung), "Veranstaltung", 0x0000FF, null),
        new(nameof(VertretungsArt.SondereinsatzVormerkung), "Sondereins. zu Vormerkung", Schwarz, null),
        new("Bereitschaft", "Bereitschaft", Schwarz, null),
        new(nameof(VertretungsArt.Vertretung), "Vertretung", Schwarz, null),
        new(nameof(VertretungsArt.Unterricht), "Unterricht", Schwarz, null),
        new("PausenaufsichtStpl", "Pausenaufsicht (Stpl)", Schwarz, 0xC0C0C0),
        new(nameof(VertretungsArt.StattVertretung), "Statt-Vertretung", 0x8080FF, null),
        new(nameof(VertretungsArt.VertretungOhneLehrer), "Vtr. ohne Lehrer", Schwarz, null),
        new(nameof(VertretungsArt.UnterrichtGeaendert), "Unterricht geändert", Schwarz, null),
        new("EntfallFuerLehrer", "Entfall für Lehrer", Schwarz, null),
        new(nameof(VertretungsArt.Bereitschaftsvertretung), "Bereitschaftsvertretung", Schwarz, null),
        new(nameof(VertretungsArt.Raumbuchung), "Raumbuchung", Schwarz, null),
    };

    private static Zeile? Finden(string schluessel) => Zeilen.FirstOrDefault(z => z.Schluessel == schluessel);

    /// <summary>Wirksame Vordergrundfarbe (gespeichert, sonst Standard; unbekannte Art: schwarz).</summary>
    public static int Vordergrund(VertretungsEinstellungen e, string schluessel) =>
        e.Vordergrundfarben.TryGetValue(schluessel, out var f) ? f : Finden(schluessel)?.Vordergrund ?? Schwarz;

    /// <summary>Wirksame Hintergrundfarbe (gespeichert, sonst Standard; null = kein Hintergrund).</summary>
    public static int? Hintergrund(VertretungsEinstellungen e, string schluessel) =>
        e.Farben.TryGetValue(schluessel, out var f) ? f : Finden(schluessel)?.Hintergrund;

    /// <summary>Vordergrund setzen; der Standardwert wird nicht gespeichert.</summary>
    public static void VordergrundSetzen(VertretungsEinstellungen e, string schluessel, int? farbe)
    {
        if (farbe is not int f || f == (Finden(schluessel)?.Vordergrund ?? Schwarz)) e.Vordergrundfarben.Remove(schluessel);
        else e.Vordergrundfarben[schluessel] = f & 0xFFFFFF;
    }

    /// <summary>Hintergrund setzen (null = Standard); der Standardwert wird nicht gespeichert.</summary>
    public static void HintergrundSetzen(VertretungsEinstellungen e, string schluessel, int? farbe)
    {
        if (farbe is not int f || f == Finden(schluessel)?.Hintergrund) e.Farben.Remove(schluessel);
        else e.Farben[schluessel] = f & 0xFFFFFF;
    }
}
