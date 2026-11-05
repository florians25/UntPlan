using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UntPlan.Core;

/// <summary>
/// Prüfhilfe für Speichern/Laden (Tests und Selbsttest): füllt jede gespeicherte Eigenschaft eines Objektgraphen per Reflexion mit
/// einem vom Standard abweichenden Wert und vergleicht zwei Objektgraphen über alle gespeicherten Eigenschaften. So werden auch
/// künftig hinzukommende Felder automatisch auf ihre Rundreise durch die Datei geprüft.
/// </summary>
public static class Rundreise
{
    private const BindingFlags Oeffentlich = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>Eigenschaften, die mit der Datei gespeichert werden (ohne [JsonIgnore], ohne Indexer).</summary>
    public static IEnumerable<PropertyInfo> Gespeichert(Type t) => t.GetProperties(Oeffentlich)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    /// <summary>Ältere Dateiformate (Eigenschaften mit eigenem JSON-Namen, die nur zum Einlesen dienen, z. B. Unterrichte vor den Perioden).</summary>
    public static bool Altformat(PropertyInfo p) => p.Name.EndsWith("Alt", StringComparison.Ordinal) && p.GetCustomAttribute<JsonPropertyNameAttribute>() != null;

    public static bool IstEinfach(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateOnly) || t == typeof(TimeOnly)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid);
    }

    public static Type? ElementTypVon(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        var ie = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? t : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return ie?.GetGenericArguments()[0];
    }

    public sealed class Fueller
    {
        private int _zaehler = 3;

        /// <summary>Ein einfacher Wert ungleich <paramref name="standard"/>.</summary>
        public object Einfach(Type t, object? standard)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            for (var versuch = 0; versuch < 5; versuch++)
            {
                var n = ++_zaehler;
                object w = u switch
                {
                    _ when u == typeof(string) => $"W{n}äß<&>\"",
                    _ when u == typeof(bool) => standard is bool b ? !b : true,
                    _ when u.IsEnum => Enum.GetValues(u).Cast<object>().Where(e => !Equals(e, standard)).Select((e, i) => (e, i)).OrderBy(x => (x.i + n) % Math.Max(1, Enum.GetValues(u).Length)).First().e,
                    _ when u == typeof(int) => n,
                    _ when u == typeof(long) => (long)n * 1000,
                    _ when u == typeof(short) => (short)n,
                    _ when u == typeof(byte) => (byte)(n % 200 + 1),
                    _ when u == typeof(double) => n + 0.25,
                    _ when u == typeof(float) => n + 0.5f,
                    _ when u == typeof(decimal) => n + 0.75m,
                    _ when u == typeof(DateOnly) => new DateOnly(2026, 9, 1).AddDays(n),
                    _ when u == typeof(TimeOnly) => new TimeOnly(7, 0).AddMinutes(n),
                    _ when u == typeof(DateTime) => new DateTime(2026, 9, 1, 8, 0, 0).AddMinutes(n),
                    _ when u == typeof(DateTimeOffset) => new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).AddMinutes(n),
                    _ when u == typeof(TimeSpan) => TimeSpan.FromMinutes(n),
                    _ when u == typeof(Guid) => new Guid(n, 1, 2, new byte[8]),
                    _ => throw new NotSupportedException(u.FullName),
                };
                if (!Equals(w, standard)) return w;
            }
            throw new InvalidOperationException("Kein abweichender Wert für " + t);
        }

        public object? Neu(Type t, int tiefe, object? standard = null)
        {
            if (IstEinfach(t)) return Einfach(t, standard);
            if (typeof(JsonNode).IsAssignableFrom(t)) return new JsonObject { ["Feld"] = ++_zaehler, ["Text"] = "Ansicht" };
            if (t.IsAbstract || t.IsInterface && ElementTypVon(t) is null) return null;
            if (tiefe > 7) return null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() is var gd && (gd == typeof(Dictionary<,>) || gd == typeof(IDictionary<,>) || gd == typeof(IReadOnlyDictionary<,>)))
            {
                var args = t.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args))!;
                var k = Neu(args[0], tiefe + 1);
                var v = Neu(args[1], tiefe + 1);
                if (k != null) dict[k] = v;
                return dict;
            }
            if (ElementTypVon(t) is { } et && t != typeof(string))
            {
                var e = Neu(et, tiefe + 1);
                if (t.IsArray)
                {
                    var a = Array.CreateInstance(et, e is null ? 0 : 1);
                    if (e != null) a.SetValue(e, 0);
                    return a;
                }
                var konkret = t.IsInterface || t.IsAbstract ? (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ISet<>) ? typeof(HashSet<>) : typeof(List<>)).MakeGenericType(et) : t;
                var liste = Activator.CreateInstance(konkret)!;
                if (e != null) konkret.GetMethod("Add", new[] { et })!.Invoke(liste, new[] { e });
                return liste;
            }
            if (!t.IsValueType && t.GetConstructor(Type.EmptyTypes) is null) return null;
            var o = Activator.CreateInstance(t)!;
            Fuellen(o, tiefe + 1);
            return o;
        }

        public void Fuellen(object o, int tiefe)
        {
            var standard = Activator.CreateInstance(o.GetType())!;
            foreach (var p in Gespeichert(o.GetType()))
            {
                if (Altformat(p)) continue;
                var std = p.GetValue(standard);
                if (p.SetMethod is { IsPublic: true })
                {
                    var w = Neu(p.PropertyType, tiefe, std);
                    if (w != null) p.SetValue(o, w);
                }
                else if (p.GetValue(o) is IList liste && ElementTypVon(p.PropertyType) is { } et && Neu(et, tiefe + 1) is { } e)
                    liste.Add(e);
                else if (p.GetValue(o) is IDictionary dict && p.PropertyType.GetGenericArguments() is { Length: 2 } args && Neu(args[0], tiefe + 1) is { } k)
                    dict[k] = Neu(args[1], tiefe + 1);
            }
        }
    }

    /// <summary>Vergleicht zwei Objektgraphen über alle gespeicherten Eigenschaften und sammelt die Abweichungen (Pfad).</summary>
    public static void Vergleichen(object? a, object? b, string pfad, List<string> abweichungen, int tiefe = 0)
    {
        if (tiefe > 12) return;
        if (a is null || b is null)
        {
            if (!(a is null && b is null)) abweichungen.Add($"{pfad}: {a ?? "null"} ≠ {b ?? "null"}");
            return;
        }
        var t = a.GetType();
        if (IstEinfach(t))
        {
            if (!Equals(a, b)) abweichungen.Add($"{pfad}: {a} ≠ {b}");
            return;
        }
        if (a is JsonNode ja)
        {
            if (b is not JsonNode jb || ja.ToJsonString() != jb.ToJsonString()) abweichungen.Add($"{pfad}: JSON abweichend");
            return;
        }
        if (a is IDictionary da && b is IDictionary db)
        {
            if (da.Count != db.Count) { abweichungen.Add($"{pfad}: {da.Count} ≠ {db.Count} Einträge"); return; }
            foreach (DictionaryEntry e in da)
            {
                if (!db.Contains(e.Key)) { abweichungen.Add($"{pfad}[{e.Key}] fehlt"); continue; }
                Vergleichen(e.Value, db[e.Key], $"{pfad}[{e.Key}]", abweichungen, tiefe + 1);
            }
            return;
        }
        if (a is IEnumerable ea && b is IEnumerable eb && a is not string)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            if (la.Count != lb.Count) { abweichungen.Add($"{pfad}: {la.Count} ≠ {lb.Count} Einträge"); return; }
            for (var i = 0; i < la.Count; i++) Vergleichen(la[i], lb[i], $"{pfad}[{i}]", abweichungen, tiefe + 1);
            return;
        }
        if (b.GetType() != t) { abweichungen.Add($"{pfad}: Typ {t.Name} ≠ {b.GetType().Name}"); return; }
        foreach (var p in Gespeichert(t))
        {
            // berechnete Eigenschaften (nur Getter, keine Sammlung) werden nicht gespeichert
            if (p.SetMethod is null && !typeof(IEnumerable).IsAssignableFrom(p.PropertyType)) continue;
            if (Altformat(p)) continue;
            Vergleichen(p.GetValue(a), p.GetValue(b), $"{pfad}.{p.Name}", abweichungen, tiefe + 1);
        }
    }

    /// <summary>Alle Modelltypen (UntPlan-Klassen mit parameterlosem Konstruktor), die von <paramref name="wurzel"/> aus erreichbar sind.</summary>
    public static List<Type> Modelltypen(Type wurzel)
    {
        var res = new List<Type>();
        var offen = new Queue<Type>(new[] { wurzel });
        while (offen.Count > 0)
        {
            var t = offen.Dequeue();
            if (res.Contains(t)) continue;
            res.Add(t);
            foreach (var p in Gespeichert(t))
            {
                var pt = p.PropertyType;
                var kandidaten = new List<Type> { pt };
                if (pt.IsGenericType) kandidaten.AddRange(pt.GetGenericArguments());
                if (ElementTypVon(pt) is { } et) kandidaten.Add(et);
                foreach (var k in kandidaten.Select(k => Nullable.GetUnderlyingType(k) ?? k))
                    if (k.IsClass && !IstEinfach(k) && k.Namespace?.StartsWith("UntPlan", StringComparison.Ordinal) == true && !k.IsAbstract && k.GetConstructor(Type.EmptyTypes) != null)
                        offen.Enqueue(k);
            }
        }
        return res;
    }
}
