using System.Text.Json;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Klassenbuch → Offene Stunden: Rechte, vordefinierte Zeitbereiche und der pro Benutzer gespeicherte Zeitbereich.
/// <list type="bullet">
/// <item>Jede Lehrkraft sieht ihre eigenen gehaltenen Stunden (Recht „Klassenbuch“ genügt).</item>
/// <item>Klassenleitungen sehen zusätzlich alle Stunden ihrer Klassen (Filter „Meine Klassen“).</item>
/// <item>Administration, Schulleitung und Konten mit den Zusatzrechten „Klassenbuch – alle“ oder „Offene Stunden“ sehen alle Lehrkräfte;
///   bei ihnen beginnt die Seite immer mit der aktuellen Woche.</item>
/// <item>Für alle übrigen Lehrkräfte bleibt der gewählte Zeitbereich gespeichert (auch nach erneuter Anmeldung).</item>
/// </list>
/// </summary>
public static class OffeneStunden
{
    public const string Vorgabe = "woche";

    /// <summary>Vordefinierte Zeitbereiche in der Reihenfolge der Auswahlbox.</summary>
    public static readonly (string Id, string Text)[] Bereiche =
    {
        ("heute", "Heute"),
        ("woche", "Beginn der Woche"),
        ("letzte", "Letzte Woche"),
        ("tage14", "Letzte 14 Tage"),
        ("monat", "Beginn des Monats"),
        ("schuljahr", "Beginn des Schuljahres"),
        ("individuell", "Individuell"),
    };

    public static bool Gueltig(string? bereich) => bereich != null && Bereiche.Any(b => b.Id == bereich);

    /// <summary>Darf die offenen Stunden aller Lehrkräfte sehen.</summary>
    public static bool DarfAlle(Benutzer b) => Rechtedienst.Darf(b, Recht.OffeneStundenAlle);

    /// <summary>Zeitraum eines Bereichs bis heute; „individuell“ verwendet von/bis (ohne Angabe: aktuelle Woche).</summary>
    public static (DateOnly Von, DateOnly Bis) Zeitraum(string bereich, DateOnly heute, DateOnly schuljahrVon, DateOnly? von = null, DateOnly? bis = null)
    {
        var montag = Plandienst.Montag(heute);
        return bereich switch
        {
            "heute" => (heute, heute),
            "letzte" => (montag.AddDays(-7), montag.AddDays(-1)),
            "tage14" => (heute.AddDays(-13), heute),
            "monat" => (new DateOnly(heute.Year, heute.Month, 1), heute),
            "schuljahr" => (schuljahrVon != DateOnly.MinValue && schuljahrVon <= heute ? schuljahrVon : heute.AddDays(-180), heute),
            "individuell" when von is { } v && bis is { } b => v <= b ? (v, b) : (b, v),
            _ => (montag, heute),
        };
    }

    private static string Schluessel(int benutzerId) => $"offene_stunden.zeitbereich.{benutzerId}";

    /// <summary>Gespeicherter Zeitbereich des Benutzers (Bereich und bei „individuell“ von/bis).</summary>
    public static (string Bereich, DateOnly? Von, DateOnly? Bis) Gespeichert(Einstellungen e, int benutzerId)
    {
        try
        {
            if (e.Lesen(Schluessel(benutzerId)) is not { Length: > 0 } json) return (Vorgabe, null, null);
            var d = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
            var bereich = d.GetValueOrDefault("bereich");
            if (!Gueltig(bereich)) return (Vorgabe, null, null);
            DateOnly? Datum(string k) => DateOnly.TryParse(d.GetValueOrDefault(k), out var x) ? x : null;
            return (bereich!, Datum("von"), Datum("bis"));
        }
        catch (JsonException) { return (Vorgabe, null, null); }
    }

    public static void Speichern(Einstellungen e, int benutzerId, string bereich, DateOnly? von, DateOnly? bis)
    {
        if (!Gueltig(bereich)) throw new ArgumentException("Unbekannter Zeitbereich.");
        if (bereich == "individuell" && (von is null || bis is null)) throw new ArgumentException("Bitte Beginn und Ende des Zeitraums angeben.");
        e.Schreiben(Schluessel(benutzerId), JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["bereich"] = bereich,
            ["von"] = bereich == "individuell" ? von!.Value.ToString("yyyy-MM-dd") : null,
            ["bis"] = bereich == "individuell" ? bis!.Value.ToString("yyyy-MM-dd") : null,
        }));
    }
}
