using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>Programmweiter Zustand: geöffnete Schuldatei, Element-Synchronisation zwischen Fenstern, Benutzereinstellungen.</summary>
public static class AppZustand
{
    public static Dokument Doc { get; private set; } = new(Demo.NeueDatei("Neue Schule", "GY"));
    public static HauptFenster Haupt { get; set; } = null!;
    public static Benutzereinstellungen Einstellungen { get; } = Benutzereinstellungen.Laden();

    public static event Action? DokumentGewechselt;
    /// <summary>Ein Element wurde in einem Fenster aktiviert (Synchronisation: Zeitwünsche, Stundenpläne).</summary>
    public static event Action<ElementTyp, int, object>? ElementGewaehlt;
    /// <summary>Eine Stunde wurde in einem Stundenplan aktiviert (Stundenplan-Synchronisation).</summary>
    public static event Action<Unterricht, Planstunde?, object>? StundeGewaehlt;

    public static Schuldatei D => Doc.Daten;

    public static void Setzen(Dokument d)
    {
        Doc = d;
        DokumentGewechselt?.Invoke();
    }

    public static void Waehlen(ElementTyp typ, int id, object quelle) => ElementGewaehlt?.Invoke(typ, id, quelle);
    public static void StundeWaehlen(Unterricht u, Planstunde? p, object quelle) => StundeGewaehlt?.Invoke(u, p, quelle);
}

public sealed class Benutzereinstellungen
{
    public List<string> ZuletztGeoeffnet { get; set; } = new();
    public bool BegruessungZeigen { get; set; } = true;
    public bool AssistentZeigen { get; set; } = true;
    public bool StundenplanSynchronisieren { get; set; } = true;
    public int[] Zeitwunschfarben { get; set; } = Array.Empty<int>();

    private static string Pfad => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan", "einstellungen.json");

    public static Benutzereinstellungen Laden()
    {
        try { return JsonSerializer.Deserialize<Benutzereinstellungen>(File.ReadAllText(Pfad)) ?? new(); }
        catch { return new(); }
    }

    public void Speichern()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
    }

    public void Merken(string datei)
    {
        ZuletztGeoeffnet.Remove(datei);
        ZuletztGeoeffnet.Insert(0, datei);
        if (ZuletztGeoeffnet.Count > 10) ZuletztGeoeffnet.RemoveAt(10);
        Speichern();
    }
}
