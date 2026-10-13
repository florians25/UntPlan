using System.Globalization;
using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

/// <summary>Bereichsangabe wie "0-1" (Hohlstunden), "3-7" (Std./Tag), "1-2" (Mittagspause, Doppelstunden).</summary>
public struct Bereich : IEquatable<Bereich>
{
    public int Min { get; set; }
    public int Max { get; set; }

    public Bereich(int min, int max)
    {
        Min = min;
        Max = max;
    }

    [JsonIgnore] public bool IstLeer => Min == 0 && Max == 0;

    public bool Enthaelt(int wert) => IstLeer || (wert >= Min && wert <= Max);

    /// <summary>Abstand eines Werts zum Bereich (0 = im Bereich).</summary>
    public int Abweichung(int wert) => IstLeer ? 0 : wert < Min ? Min - wert : wert > Max ? wert - Max : 0;

    public override string ToString() => IstLeer ? "" : $"{Min}-{Max}";

    public static Bereich Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var t = text.Trim().Replace(" ", "");
        var parts = t.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var a)) return new Bereich(a, a);
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            return new Bereich(Math.Min(x, y), Math.Max(x, y));
        throw new FormatException($"Ungültiger Bereich \"{text}\" (Beispiel: 1-2)");
    }

    public static bool TryParse(string? text, out Bereich b)
    {
        try { b = Parse(text); return true; }
        catch (FormatException) { b = default; return false; }
    }

    public bool Equals(Bereich other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is Bereich b && Equals(b);
    public override int GetHashCode() => HashCode.Combine(Min, Max);
    public static bool operator ==(Bereich a, Bereich b) => a.Equals(b);
    public static bool operator !=(Bereich a, Bereich b) => !a.Equals(b);
}

/// <summary>
/// Zeitwünsche eines Elements: für jeden Tag und jede Stunde ein Wunschgewicht von -3 (keinesfalls Unterricht,
/// absolute Sperrung) bis +3 (auf jeden Fall Unterricht – bei Klassen "Kernzeit").
/// </summary>
public sealed class Zeitwuensche
{
    /// <summary>Schlüssel = Tag * 100 + Stunde (Tag 0 = erster Schultag, Stunde ab 1).</summary>
    public Dictionary<int, int> Werte { get; set; } = new();

    public int Get(int tag, int stunde) => Werte.TryGetValue(tag * 100 + stunde, out var w) ? w : 0;

    public void Set(int tag, int stunde, int wert)
    {
        wert = Math.Clamp(wert, -3, 3);
        if (wert == 0) Werte.Remove(tag * 100 + stunde);
        else Werte[tag * 100 + stunde] = wert;
    }

    [JsonIgnore] public bool Leer => Werte.Count == 0;

    public bool Gesperrt(int tag, int stunde) => Get(tag, stunde) <= -3;

    public Zeitwuensche Kopie() => new() { Werte = new Dictionary<int, int>(Werte) };
}

public enum ZeitbereichArt
{
    Tage,
    Vormittage,
    Nachmittage,
}

/// <summary>Unbestimmter Zeitwunsch, z. B. "1 freier Tag, absolute Sperrung (-3)" oder "2 freie Nachmittage (-1)".</summary>
public sealed class UnbestimmterZeitwunsch
{
    public ZeitbereichArt Bereich { get; set; }
    public int Anzahl { get; set; } = 1;
    /// <summary>-3 = Freihalten, absolute Sperrung … -1 = Freihalten, schwacher Wunsch.</summary>
    public int Wunsch { get; set; } = -3;

    public override string ToString() => $"{Anzahl} {Bereich} ({Wunsch})";

    public static string WunschText(int w) => w switch
    {
        -3 => "Freihalten, absolute Sperrung (-3)",
        -2 => "Freihalten, wichtig (-2)",
        _ => "Freihalten, schwacher Wunsch (-1)",
    };
}

public static class Format
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    public static string Datum(DateOnly d) => d.ToString("dd.MM.yyyy", De);
    public static string Zahl(decimal d) => d.ToString("0.###", De);
}
