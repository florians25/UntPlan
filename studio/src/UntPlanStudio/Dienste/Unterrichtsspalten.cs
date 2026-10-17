using System.Globalization;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlanStudio.Dienste;

/// <summary>
/// Spalte der Unterrichtstabelle. <see cref="Zeile"/> = Wert je Kopplungszeile (Fach, Lehrer, Klassen, Räume …),
/// sonst Wert des Unterrichts (nur in der ersten Zeile angezeigt).
/// </summary>
public sealed record Spalte(string Key, string Titel, int Breite, bool Zeile, bool Standard, Func<Unterricht, Kopplungszeile, string> Get,
    Action<Schuldatei, Unterricht, Kopplungszeile, string>? Set, string Karte = "Unterricht", bool Bool = false)
{
    /// <summary>Elementtyp für Auswahllisten (Kurz-/Langname) oder null.</summary>
    public ElementTyp? RefTyp => Key switch
    {
        "Fach" => ElementTyp.Fach,
        "Lehrer" => ElementTyp.Lehrer,
        "Klassen" => ElementTyp.Klasse,
        "Fachraum" or "Stammraum" => ElementTyp.Raum,
        _ => null,
    };

    public bool Liste => Key == "Klassen";
}

/// <summary>Spaltendefinitionen der Unterrichtsfenster, übernommen aus der Desktop-Anwendung (Fenster/UnterrichtFenster.cs).</summary>
public static class Unterrichtsspalten
{
    private static int? Raum(Schuldatei d, string t) =>
        t.Trim().Length == 0 ? null : d.Raeume.FirstOrDefault(r => r.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private static int? Gruppe(Schuldatei d, string t) =>
        t.Trim().Length == 0 ? null : d.Raumgruppen.FirstOrDefault(r => r.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    private static void RaumSetzen(Schuldatei d, string t, Action<int?> raum, Action<int?> gruppe)
    {
        if (t.Trim().Length == 0) { raum(null); gruppe(null); return; }
        if (Raum(d, t) is int r) { raum(r); gruppe(null); return; }
        if (Gruppe(d, t) is int g) { raum(null); gruppe(g); return; }
        throw new FormatException($"Raum oder Raumgruppe \"{t}\" ist nicht vorhanden.");
    }

    private static T ElementPflicht<T>(IEnumerable<T> liste, string t, string art) where T : Element =>
        liste.FirstOrDefault(x => x.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new FormatException($"{art} \"{t.Trim()}\" ist nicht vorhanden.");

    /// <summary>Spalten der Unterrichtstabelle (wie Desktop); berechnete Werte beziehen sich auf das übergebene Dokument.</summary>
    public static List<Spalte> Fuer(Dokument doc)
    {
        var ix = () => doc.Index;
        var wochen = new Lazy<int>(() => Kalender.Unterrichtswochen(doc.Daten)); // einmal je Abruf statt je Zeile
        static string B(bool b) => b ? "X" : "";
        static bool Wahr(string s) => s.Equals("X", StringComparison.OrdinalIgnoreCase) || s == "True" || s == "1";
        return new List<Spalte>
        {
            new("UNr", "U-Nr", 48, false, true, (u, _) => u.Id.ToString(), null),
            new("KlLe", "Kl,Le", 56, false, true, (u, _) => u.Zeilen.Count > 1 ? $"⊞ {u.KlassenIds.Count()}, {u.LehrerIds.Count()}" : "", null),
            new("Nvpl", "Nvpl.Std.", 58, false, true, (u, _) => ix().NichtVerplant(u) is var n && n > 0 ? n.ToString() : "", null),
            new("Wst", "Wst", 38, false, true, (u, _) => u.Wochenstunden.ToString(), (d, u, z, t) => u.Wochenstunden = Math.Max(0, int.Parse(t))),
            new("Jst", "Jst", 44, false, false, (u, _) => (u.Wochenstunden * wochen.Value).ToString(), null),
            new("Fach", "Fach", 52, true, true, (_, z) => ix().KFach(z.FachId), (d, u, z, t) =>
            {
                var f = t.Trim().Length == 0 ? null : ElementPflicht(d.Faecher, t, "Fach");
                z.FachId = f?.Id;
                if (f != null)
                {
                    // Fachraum laut Fächer-Stammdaten automatisch eintragen
                    if (z.FachraumId is null && z.FachraumgruppeId is null) { z.FachraumId = f.FachraumId; z.FachraumgruppeId = f.FachraumgruppeId; }
                    if (u.Doppelstunden.IstLeer && !f.Doppelstunden.IstLeer) u.Doppelstunden = f.Doppelstunden;
                }
            }),
            new("Lehrer", "Lehrer", 56, true, true, (_, z) => ix().KLehrer(z.LehrerId), (d, u, z, t) =>
            {
                var l = t.Trim().Length == 0 ? null : ElementPflicht(d.Lehrer, t, "Lehrer");
                z.LehrerId = l?.Id;
                if (l?.StammraumId != null && z.StammraumId is null && z.KlassenIds.Count == 0) z.StammraumId = l.StammraumId;
            }),
            new("Klassen", "Klasse(n)", 80, true, true, (_, z) => ix().KKlassen(z.KlassenIds), (d, u, z, t) =>
            {
                z.KlassenIds = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(k => ElementPflicht(d.Klassen, k, "Klasse").Id).Distinct().ToList();
                // Stammraum aus den Stammdaten der Klasse übernehmen
                if (z.StammraumId is null && z.StammraumgruppeId is null && z.KlassenIds.Count > 0)
                    z.StammraumId = d.Klassen.First(k => k.Id == z.KlassenIds[0]).StammraumId;
            }),
            new("Fachraum", "Fachraum", 62, true, true, (_, z) => z.FachraumId.HasValue ? ix().KRaum(z.FachraumId) : ix().K(ElementTyp.Raumgruppe, z.FachraumgruppeId),
                (d, u, z, t) => RaumSetzen(d, t, r => z.FachraumId = r, g => z.FachraumgruppeId = g)),
            new("Stammraum", "Stammraum", 66, true, true, (_, z) => z.StammraumId.HasValue ? ix().KRaum(z.StammraumId) : ix().K(ElementTyp.Raumgruppe, z.StammraumgruppeId),
                (d, u, z, t) => RaumSetzen(d, t, r => z.StammraumId = r, g => z.StammraumgruppeId = g)),
            new("Dopp", "Dopp.Std.", 58, false, true, (u, _) => u.Doppelstunden.ToString(), (d, u, z, t) => u.Doppelstunden = Bereich.Parse(t)),
            new("Block", "Block", 42, false, true, (u, _) => u.Block, (d, u, z, t) => u.Block = t.Trim()),
            new("Text", "Text", 80, false, true, (u, _) => u.Text, (d, u, z, t) => u.Text = t),
            new("Fix", "Fix (X)", 44, false, true, (u, _) => B(u.Fix), (d, u, z, t) => u.Fix = Wahr(t), Bool: true),
            new("Ignore", "Ignore (i)", 50, false, true, (u, _) => B(u.Ignorieren), (d, u, z, t) => u.Ignorieren = Wahr(t), Bool: true),
            new("Mehrmals", "(M)", 34, false, false, (u, _) => B(u.MehrmalsProTag), (d, u, z, t) => u.MehrmalsProTag = Wahr(t), "Kennzeichen", true),
            new("KeineVtr", "keine Vertr.", 70, false, false, (u, _) => B(u.KeineVertretung), (d, u, z, t) => u.KeineVertretung = Wahr(t), "Kennzeichen", true),
            new("Band", "Band", 50, false, true, (u, _) => u.Band, (d, u, z, t) => u.Band = t.Trim(), "Kursplanung"),
            new("Kurs", "Kursname", 70, false, false, (u, _) => u.Kursname, (d, u, z, t) => u.Kursname = t.Trim(), "Kursplanung"),
            new("Gruppe", "Schülergruppe", 80, false, false, (u, _) => u.Schuelergruppe, (d, u, z, t) => u.Schuelergruppe = t, "Unterricht"),
            new("Zeilentext", "Zeilentext", 80, true, false, (_, z) => z.Zeilentext, (d, u, z, t) => z.Zeilentext = t, "Kopplungszeile"),
            new("Wert", "Wert", 48, true, false, (u, z) => Format.Zahl(UntPlan.Core.Module.Wertrechnung.Wert(doc.Daten, ix(), u, z)),
                (d, u, z, t) => z.Wert = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Faktor", "Werte-Faktor", 70, false, false, (u, _) => u.WerteFaktor.HasValue ? Format.Zahl(u.WerteFaktor.Value) : "",
                (d, u, z, t) => u.WerteFaktor = t.Trim().Length == 0 ? null : decimal.Parse(t.Replace('.', ','), CultureInfo.GetCultureInfo("de-DE")), "Werte"),
            new("Periode", "Woche (A/B)", 70, false, false, (u, _) => u.Periodizitaet switch { Periodizitaet.AWoche => "A", Periodizitaet.BWoche => "B", _ => "" },
                (d, u, z, t) => u.Periodizitaet = t.Trim().ToUpperInvariant() switch { "A" => Periodizitaet.AWoche, "B" => Periodizitaet.BWoche, _ => Periodizitaet.JedeWoche }, "Mehrwochen"),
            new("UGruppe", "U-Gruppe", 66, false, false, (u, _) => doc.Daten.UGruppen.FirstOrDefault(g => g.Id == u.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => u.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Mehrwochen"),
            new("ZUGruppe", "Zeilen-U-Gr.", 74, true, false, (_, z) => doc.Daten.UGruppen.FirstOrDefault(g => g.Id == z.UGruppeId)?.Kurzname ?? "",
                (d, u, z, t) => z.UGruppeId = t.Trim().Length == 0 ? null : d.UGruppen.FirstOrDefault(g => g.Kurzname.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? throw new FormatException($"Unterrichtsgruppe \"{t}\" unbekannt"), "Mehrwochen"),
            new("Von", "von", 76, false, false, (u, _) => u.VonDatum.HasValue ? Format.Datum(u.VonDatum.Value) : "",
                (d, u, z, t) => u.VonDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Mehrwochen"),
            new("Bis", "bis", 76, false, false, (u, _) => u.BisDatum.HasValue ? Format.Datum(u.BisDatum.Value) : "",
                (d, u, z, t) => u.BisDatum = t.Trim().Length == 0 ? null : DateOnly.Parse(t, CultureInfo.GetCultureInfo("de-DE")), "Mehrwochen"),
            new("Statistik", "Statistik", 60, false, false, (u, _) => u.Statistik, (d, u, z, t) => u.Statistik = t, "Diverses"),
        };
    }

}
