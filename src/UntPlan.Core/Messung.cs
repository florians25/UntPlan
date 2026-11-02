using System.Diagnostics;

namespace UntPlan.Core;

/// <summary>
/// Leichte Zeitmessung für den Leistungslauf: aufsummierte Zeit und Anzahl je Messpunkt (inklusive verschachtelter Punkte).
/// Ausgeschaltet kostet ein Messpunkt nur eine Abfrage.
/// </summary>
public static class Messung
{
    public static bool An { get; set; }
    private static readonly Dictionary<string, (int Anzahl, long Ticks)> Werte = new();

    public static Punkt Start(string name) => An ? new Punkt(name, Stopwatch.GetTimestamp()) : default;

    public static void Leeren() { lock (Werte) Werte.Clear(); }

    public static List<string> Bericht(int max = 40)
    {
        lock (Werte)
            return Werte.OrderByDescending(w => w.Value.Ticks).Take(max)
                .Select(w => $"      {w.Value.Ticks * 1000.0 / Stopwatch.Frequency,9:0.0} ms  {w.Value.Anzahl,6}×  {w.Key}").ToList();
    }

    public readonly struct Punkt : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        internal Punkt(string name, long start) { _name = name; _start = start; }

        public void Dispose()
        {
            if (_name is null) return;
            var t = Stopwatch.GetTimestamp() - _start;
            lock (Werte)
            {
                Werte.TryGetValue(_name, out var w);
                Werte[_name] = (w.Anzahl + 1, w.Ticks + t);
            }
        }
    }
}
