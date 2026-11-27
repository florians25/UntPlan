using System.Text.Json.Nodes;

namespace UntPlanWeb.Tests;

/// <summary>
/// Große Testschule: das Beispielpaket mehrfach (Klassen, Lehrkräfte, Räume, Schüler*innen, Unterricht, Stundenplan, Vertretungen und Tage
/// je Kopie mit eigenen Kürzeln und Kennungen). Fächer und Gänge bleiben gemeinsam. Faktor 6 ergibt 42 Klassen, 132 Lehrkräfte und gut 1100 Schüler*innen.
/// </summary>
public static class GrossesPaket
{
    private static readonly HashSet<string> Namensfelder = new()
        { "klassen", "lehrer", "raeume", "lehrerAlt", "raeumeAlt", "klassenAlt", "klassenleitung", "stammraum", "vertreter", "raum", "neuerRaum", "element", "klasse" };

    public static JsonObject Erzeugen(int faktor)
    {
        var p = Testserver.Beispielpaket();
        var e = p["elemente"]!.AsObject();
        var per = p["perioden"]![0]!.AsObject();
        var original = Testserver.Beispielpaket();
        for (var k = 1; k < faktor; k++)
        {
            var map = new Dictionary<string, string>();
            foreach (var art in new[] { "klassen", "lehrer", "raeume" })
                foreach (var x in original["elemente"]![art]!.AsArray())
                    map[x!["kurz"]!.GetValue<string>()] = x["kurz"]!.GetValue<string>() + "-" + k;
            JsonNode? Kopie(JsonNode? n) => n?.DeepClone();
            void Umbenennen(JsonNode? n)
            {
                if (n is JsonObject o)
                    foreach (var (name, wert) in o.ToList())
                    {
                        if (Namensfelder.Contains(name))
                        {
                            if (wert is JsonValue v && v.TryGetValue<string>(out var t) && map.TryGetValue(t, out var neu)) o[name] = neu;
                            else if (wert is JsonArray a)
                                for (var i = 0; i < a.Count; i++)
                                    if (a[i] is JsonValue av && av.TryGetValue<string>(out var at) && map.TryGetValue(at, out var an)) a[i] = an;
                        }
                        else Umbenennen(wert);
                    }
                else if (n is JsonArray arr) foreach (var x in arr) Umbenennen(x);
            }
            void Versatz(JsonObject o, string feld, int um) { if (o[feld] is JsonValue v && v.TryGetValue<int>(out var i)) o[feld] = i + um; }
            var um = k * 10000;
            foreach (var art in new[] { "klassen", "lehrer", "raeume" })
                foreach (var x in original["elemente"]![art]!.AsArray())
                {
                    var c = Kopie(x)!.AsObject();
                    c["kurz"] = map[c["kurz"]!.GetValue<string>()];
                    Versatz(c, "id", um);
                    Umbenennen(c);
                    e[art]!.AsArray().Add(c);
                }
            foreach (var x in original["elemente"]!["schueler"]!.AsArray())
            {
                var c = Kopie(x)!.AsObject();
                Versatz(c, "id", um);
                Umbenennen(c);
                e["schueler"]!.AsArray().Add(c);
            }
            var oper = original["perioden"]![0]!;
            foreach (var x in oper["unterricht"]!.AsArray())
            {
                var c = Kopie(x)!.AsObject();
                Versatz(c, "id", um);
                Umbenennen(c);
                per["unterricht"]!.AsArray().Add(c);
            }
            foreach (var x in oper["stundenplan"]!.AsArray())
            {
                var c = Kopie(x)!.AsObject();
                Versatz(c, "unterricht", um);
                Umbenennen(c);
                per["stundenplan"]!.AsArray().Add(c);
            }
            foreach (var x in original["vertretungen"]!.AsArray())
            {
                var c = Kopie(x)!.AsObject();
                Versatz(c, "id", um);
                Umbenennen(c);
                p["vertretungen"]!.AsArray().Add(c);
            }
            foreach (var feld in new[] { "absenzen", "pausenaufsichten" })
                foreach (var x in original[feld]!.AsArray())
                {
                    var c = Kopie(x)!.AsObject();
                    Umbenennen(c);
                    p[feld]!.AsArray().Add(c);
                }
            var tage = p["tage"]!.AsArray();
            var otage = original["tage"]!.AsArray();
            for (var t = 0; t < otage.Count; t++)
                foreach (var x in otage[t]!["stunden"]!.AsArray())
                {
                    var c = Kopie(x)!.AsObject();
                    Versatz(c, "unterricht", um);
                    Versatz(c, "vertretung", um);
                    Umbenennen(c);
                    tage[t]!["stunden"]!.AsArray().Add(c);
                }
        }
        p["revision"] = 2;
        return p;
    }
}
