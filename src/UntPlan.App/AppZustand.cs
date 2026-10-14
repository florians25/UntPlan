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

    /// <summary>Vertretungsmodus: eigene Fenster, eigenes Menüband, grüne Oberfläche.</summary>
    public static bool Vertretungsmodus { get; private set; }
    public static event Action? ModusGewechselt;

    /// <summary>Planungsdatum der Vertretungsplanung (alle Fenster werden auf diesen Tag synchronisiert).</summary>
    public static DateOnly Datum { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    public static event Action<DateOnly>? DatumGewechselt;

    /// <summary>Aktive Vertretungszeile (Vertretungsvorschlag folgt der Auswahl).</summary>
    public static Vertretung? AktiveVertretung { get; private set; }
    public static event Action<Vertretung?>? VertretungGewaehlt;

    /// <summary>Angemeldeter Benutzer (MultiUser) bzw. Windows-Benutzer.</summary>
    public static string Benutzer { get; set; } = Environment.UserName;

    public static void ModusSetzen(bool vertretung)
    {
        if (Vertretungsmodus == vertretung) return;
        Vertretungsmodus = vertretung;
        Ui.Vertretungsmodus = vertretung;
        ModusGewechselt?.Invoke();
    }

    public static void DatumSetzen(DateOnly datum)
    {
        if (Datum == datum) return;
        Datum = datum;
        DatumGewechselt?.Invoke(datum);
    }

    /// <summary>Im Vertretungsfenster markierte Zeilen (gleichzeitige Bearbeitung mehrerer Stunden).</summary>
    public static List<int> MarkierteVertretungen { get; } = new();

    public static void VertretungWaehlen(Vertretung? v, IEnumerable<int>? markiert = null)
    {
        AktiveVertretung = v;
        MarkierteVertretungen.Clear();
        if (markiert != null) MarkierteVertretungen.AddRange(markiert);
        else if (v != null) MarkierteVertretungen.Add(v.Id);
        VertretungGewaehlt?.Invoke(v);
    }

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
    /// <summary>Automatisch speichern alle n Minuten (0 = aus).</summary>
    public int AutoSpeichernMinuten { get; set; } = 10;
    /// <summary>Beim letzten Beenden war der Vertretungsmodus aktiv.</summary>
    public bool LetzterModusVertretung { get; set; }
    public bool ModusMerken { get; set; } = true;
    /// <summary>Zuletzt gewähltes Stundenplanformat je Planart ("Klasse", "Klasse groß" …).</summary>
    public Dictionary<string, string> Stundenplanformate { get; set; } = new();
    /// <summary>Benannte, vom Benutzer gespeicherte Fensteranordnungen.</summary>
    public Dictionary<string, Fensteranordnung> Fensteranordnungen { get; set; } = new();
    public bool AssistentZeigen { get; set; } = true;
    public bool StundenplanSynchronisieren { get; set; } = true;
    public int[] Zeitwunschfarben { get; set; } = Array.Empty<int>();
    /// <summary>Fensteranordnung je Modus (Stundenplan / Vertretung): Schlüssel und Position.</summary>
    public Dictionary<string, List<FensterPosition>> Fenstergruppen { get; set; } = new();
    public string Vertretungsansicht { get; set; } = "Klasse - Standard";
    /// <summary>Druckformulare je Liste.</summary>
    public Dictionary<string, ListenDruck.Layout> Drucklayouts { get; set; } = new();
    /// <summary>Eigene Tabellenformate je Tabelle (Schlüssel = Fenster/Tabelle).</summary>
    public Dictionary<string, List<Tabellenformat>> Tabellenformate { get; set; } = new();
    /// <summary>Zuletzt gewähltes Tabellenformat je Tabelle.</summary>
    public Dictionary<string, string> AktiveTabellenformate { get; set; } = new();
    /// <summary>Aktuelle (automatisch gemerkte) Spaltenanordnung je Tabelle.</summary>
    public Dictionary<string, List<Tabellenspalte>> Tabellenlayouts { get; set; } = new();
    /// <summary>Gewählte Zeitraumart (Tag, Woche, Von-Bis) je Fenster.</summary>
    public Dictionary<string, string> Zeitraumarten { get; set; } = new();
    /// <summary>Einstellungen des Vertretungsplan-Drucks (Aushang).</summary>
    public Vertretungsdruck Vertretungsdruck { get; set; } = new();

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

public sealed class Vertretungsdruck
{
    public string Schriftart { get; set; } = "Comic Sans MS";
    public float Schriftgroesse { get; set; } = 11f;
    public bool Nachrichten { get; set; } = true;
    public bool BlockierteRaeume { get; set; } = true;
    public bool AbwesendeKlassen { get; set; } = true;
    public bool AbwesendeLehrer { get; set; } = true;
    public bool Querformat { get; set; }
}

public sealed class Tabellenformat
{
    public string Name { get; set; } = "";
    public List<Tabellenspalte> Spalten { get; set; } = new();
}

public sealed class Tabellenspalte
{
    public string Name { get; set; } = "";
    public bool Sichtbar { get; set; } = true;
    public int Breite { get; set; }
    public int Position { get; set; }
    /// <summary>Eigene Spaltenüberschrift (leer = Standard).</summary>
    public string Ueberschrift { get; set; } = "";
    /// <summary>0 = Standard, 1 = links, 2 = zentriert, 3 = rechts.</summary>
    public int Ausrichtung { get; set; }
    /// <summary>Spalte bleibt beim waagerechten Blättern stehen.</summary>
    public bool Fixiert { get; set; }
}

public sealed class Fensteranordnung
{
    public bool Vertretungsmodus { get; set; }
    public List<FensterPosition> Fenster { get; set; } = new();
}

public sealed class FensterPosition
{
    public string Schluessel { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int B { get; set; }
    public int H { get; set; }
    public bool Maximiert { get; set; }
}
