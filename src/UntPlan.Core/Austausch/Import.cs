using UntPlan.Core.Model;

namespace UntPlan.Core.Austausch;

public sealed class ImportBericht
{
    public int Neu { get; set; }
    public int Aktualisiert { get; set; }
    public List<string> Hinweise { get; } = new();
    public override string ToString() => $"{Neu} neu, {Aktualisiert} aktualisiert" + (Hinweise.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, Hinweise) : "");
}

/// <summary>Import aus Schild-NRW (CSV) und dem GPU-Austauschformat (Umstieg von anderen Stundenplanprogrammen).</summary>
public static class Import
{
    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static int Spalte(string[] kopf, params string[] namen)
    {
        var h = kopf.Select(Norm).ToArray();
        foreach (var n in namen) { var i = Array.IndexOf(h, Norm(n)); if (i >= 0) return i; }
        foreach (var n in namen) { var i = Array.FindIndex(h, x => x.Contains(Norm(n))); if (i >= 0) return i; }
        return -1;
    }

    private static string Feld(string[] r, int i) => i >= 0 && i < r.Length ? r[i] : "";

    public static ImportBericht LehrerCsv(Schuldatei d, string text)
    {
        var b = new ImportBericht();
        var rows = Csv.Lesen(text);
        if (rows.Count < 2) { b.Hinweise.Add("Keine Datenzeilen gefunden."); return b; }
        var k = rows[0];
        int cK = Spalte(k, "Kürzel", "Kuerzel", "Lehrerkürzel", "Kurzname", "Name"), cN = Spalte(k, "Nachname", "Langname"), cV = Spalte(k, "Vorname"),
            cE = Spalte(k, "E-Mail", "Email"), cS = Spalte(k, "Pflichtstunden", "Soll", "Deputat", "Wochensoll"), cF = Spalte(k, "Fächer", "Faecher", "Lehrbefähigung"),
            cP = Spalte(k, "Personalnummer", "PersNr"), cT = Spalte(k, "Titel"), cG = Spalte(k, "Geschlecht");
        if (cK < 0) { b.Hinweise.Add("Spalte \"Kürzel\" nicht gefunden."); return b; }
        foreach (var r in rows.Skip(1))
        {
            var kz = Feld(r, cK);
            if (kz.Length == 0) continue;
            var l = d.Lehrer.FirstOrDefault(x => x.Kurzname.Equals(kz, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                l = new Lehrer { Id = d.NeueId(), Kurzname = kz, Sortierung = d.Lehrer.Count, Wochensoll = Nrw.Pflichtstunden.GetValueOrDefault(d.Einstellungen.Schulform, 25.5m) };
                d.Lehrer.Add(l);
                b.Neu++;
            }
            else b.Aktualisiert++;
            if (Feld(r, cN) is { Length: > 0 } n) l.Langname = n;
            if (Feld(r, cV) is { Length: > 0 } v) l.Vorname = v;
            if (Feld(r, cE) is { Length: > 0 } e) l.Email = e;
            if (Feld(r, cP) is { Length: > 0 } p) l.Personalnummer = p;
            if (Feld(r, cT) is { Length: > 0 } t) l.Titel = t;
            if (Feld(r, cG) is { Length: > 0 } g) l.Geschlecht = g;
            if (decimal.TryParse(Feld(r, cS).Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var soll)) l.Wochensoll = soll;
            var faecher = Feld(r, cF).Split(new[] { ' ', ',', '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => d.Faecher.FirstOrDefault(x => x.Kurzname.Equals(f, StringComparison.OrdinalIgnoreCase))?.Id).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            if (faecher.Count > 0) l.FaecherIds = faecher;
        }
        return b;
    }

    public static ImportBericht KlassenCsv(Schuldatei d, string text)
    {
        var b = new ImportBericht();
        var rows = Csv.Lesen(text);
        if (rows.Count < 2) { b.Hinweise.Add("Keine Datenzeilen gefunden."); return b; }
        var k = rows[0];
        int cN = Spalte(k, "Klasse", "Klassenbezeichnung", "Kurzname", "Name"), cJ = Spalte(k, "Jahrgang", "Jahrgangsstufe", "Stufe"),
            cL = Spalte(k, "Klassenlehrer", "Klassenleitung"), cS = Spalte(k, "Schüler", "Schueler", "Anzahl"), cR = Spalte(k, "Raum", "Klassenraum");
        if (cN < 0) { b.Hinweise.Add("Spalte \"Klasse\" nicht gefunden."); return b; }
        foreach (var r in rows.Skip(1))
        {
            var name = Feld(r, cN);
            if (name.Length == 0) continue;
            var kl = d.Klassen.FirstOrDefault(x => x.Kurzname.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (kl is null)
            {
                kl = new Klasse { Id = d.NeueId(), Kurzname = name, Langname = $"Klasse {name}", Sortierung = d.Klassen.Count, Jahrgang = new string(name.TakeWhile(char.IsDigit).ToArray()) };
                d.Klassen.Add(kl);
                b.Neu++;
            }
            else b.Aktualisiert++;
            if (Feld(r, cJ) is { Length: > 0 } j) kl.Jahrgang = j.TrimStart('0');
            if (int.TryParse(Feld(r, cS), out var anz)) kl.SchuelerMaennlich = anz;
            var lehrer = Feld(r, cL).Split(new[] { ' ', ',', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => d.Lehrer.FirstOrDefault(l => l.Kurzname.Equals(x, StringComparison.OrdinalIgnoreCase))?.Id).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            if (lehrer.Count > 0) kl.KlassenlehrerIds = lehrer;
            if (d.Raeume.FirstOrDefault(x => x.Kurzname.Equals(Feld(r, cR), StringComparison.OrdinalIgnoreCase)) is { } raum) kl.StammraumId = raum.Id;
        }
        return b;
    }

    public static ImportBericht SchuelerCsv(Schuldatei d, string text)
    {
        var b = new ImportBericht();
        var rows = Csv.Lesen(text);
        if (rows.Count < 2) return b;
        var k = rows[0];
        int cN = Spalte(k, "Nachname", "Name"), cV = Spalte(k, "Vorname"), cK = Spalte(k, "Klasse"), cW = Spalte(k, "Kurse", "Kurswahlen", "Fächer");
        foreach (var r in rows.Skip(1))
        {
            var s = new Schueler { Id = d.NeueId(), Kurzname = Feld(r, cN), Vorname = Feld(r, cV), Sortierung = d.Schueler.Count };
            if (s.Kurzname.Length == 0) continue;
            s.KlasseId = d.Klassen.FirstOrDefault(x => x.Kurzname.Equals(Feld(r, cK), StringComparison.OrdinalIgnoreCase))?.Id;
            foreach (var kurs in Feld(r, cW).Split(new[] { ' ', ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
                if (d.Unterrichte.FirstOrDefault(u => u.Kursname.Equals(kurs, StringComparison.OrdinalIgnoreCase)) is { } u) s.KurswahlIds.Add(u.Id);
            d.Schueler.Add(s);
            b.Neu++;
        }
        return b;
    }

    /// <summary>
    /// GPU-Austauschformat: GPU001 Stundenplan, GPU002 Unterricht, GPU003 Klassen, GPU004 Lehrer, GPU005 Räume, GPU006 Fächer.
    /// Mehrere GPU002-Zeilen mit derselben Nummer bilden Kopplungszeilen.
    /// </summary>
    public static ImportBericht Gpu(Schuldatei d, IReadOnlyDictionary<string, string> dateien)
    {
        var b = new ImportBericht();
        List<string[]> Rows(string nr)
        {
            var kv = dateien.FirstOrDefault(x => x.Key.ToUpperInvariant().Contains("GPU" + nr));
            return kv.Value is null ? new() : Csv.Lesen(kv.Value, kv.Value.Contains(';') ? ';' : ',');
        }
        T? ByK<T>(IEnumerable<T> l, string k) where T : Element => l.FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase));

        foreach (var r in Rows("006").Where(r => r.Length > 0 && r[0].Length > 0))
            if (ByK(d.Faecher, r[0]) is { } f) { if (r.Length > 1 && r[1].Length > 0) f.Langname = r[1]; b.Aktualisiert++; }
            else { d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = r[0], Langname = r.ElementAtOrDefault(1) ?? r[0], Sortierung = d.Faecher.Count }); b.Neu++; }
        foreach (var r in Rows("005").Where(r => r.Length > 0 && r[0].Length > 0))
            if (ByK(d.Raeume, r[0]) is { } ra) { if (r.Length > 1 && r[1].Length > 0) ra.Langname = r[1]; b.Aktualisiert++; }
            else
            {
                d.Raeume.Add(new Raum { Id = d.NeueId(), Kurzname = r[0], Langname = r.ElementAtOrDefault(1) ?? r[0], Kapazitaet = int.TryParse(r.ElementAtOrDefault(3), out var kap) ? kap : 0, Sortierung = d.Raeume.Count });
                b.Neu++;
            }
        foreach (var r in Rows("005").Where(r => r.Length > 2 && r[2].Length > 0))
            if (ByK(d.Raeume, r[0]) is { } ra && ByK(d.Raeume, r[2]) is { } aus) ra.AusweichraumId = aus.Id;
        foreach (var r in Rows("004").Where(r => r.Length > 0 && r[0].Length > 0))
            if (ByK(d.Lehrer, r[0]) is { } l) { if (r.Length > 1 && r[1].Length > 0) l.Langname = r[1]; b.Aktualisiert++; }
            else { d.Lehrer.Add(new Lehrer { Id = d.NeueId(), Kurzname = r[0], Langname = r.ElementAtOrDefault(1) ?? r[0], Sortierung = d.Lehrer.Count }); b.Neu++; }
        foreach (var r in Rows("003").Where(r => r.Length > 0 && r[0].Length > 0))
            if (ByK(d.Klassen, r[0]) is { } k) { if (r.Length > 1 && r[1].Length > 0) k.Langname = r[1]; b.Aktualisiert++; }
            else { d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = r[0], Langname = r.ElementAtOrDefault(1) ?? r[0], Sortierung = d.Klassen.Count, Jahrgang = new string(r[0].TakeWhile(char.IsDigit).ToArray()) }); b.Neu++; }

        // Unterricht
        var unterrichte = new Dictionary<string, Unterricht>();
        foreach (var r in Rows("002").Where(r => r.Length > 0 && r[0].Length > 0))
        {
            if (!unterrichte.TryGetValue(r[0], out var u))
            {
                u = new Unterricht { Id = d.NeueId(), Wochenstunden = (int)Math.Round(decimal.TryParse(r.ElementAtOrDefault(1)?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 0) };
                unterrichte[r[0]] = u;
            }
            var lehrer = ByK(d.Lehrer, r.ElementAtOrDefault(5) ?? "");
            var fach = ByK(d.Faecher, r.ElementAtOrDefault(6) ?? "");
            var raum = ByK(d.Raeume, r.ElementAtOrDefault(7) ?? "");
            var klasse = ByK(d.Klassen, r.ElementAtOrDefault(4) ?? "");
            var zeile = u.Zeilen.FirstOrDefault(z => z.LehrerId == lehrer?.Id && z.FachId == fach?.Id);
            if (zeile is null)
            {
                zeile = new Kopplungszeile { LehrerId = lehrer?.Id, FachId = fach?.Id, FachraumId = raum?.Id };
                u.Zeilen.Add(zeile);
            }
            if (klasse != null && !zeile.KlassenIds.Contains(klasse.Id))
            {
                zeile.KlassenIds.Add(klasse.Id);
                zeile.StammraumId ??= klasse.StammraumId;
            }
        }
        var nr = new Dictionary<string, int>();
        foreach (var (key, u) in unterrichte)
        {
            if (u.Zeilen.All(z => z.FachId is null)) { b.Hinweise.Add($"Unterricht {key}: kein Fach – übersprungen"); continue; }
            d.Unterrichte.Add(u);
            nr[key] = u.Id;
            b.Neu++;
        }
        // Stundenplan
        var gesehen = new HashSet<(int, int, int)>();
        foreach (var r in Rows("001").Where(r => r.Length >= 7))
        {
            if (!nr.TryGetValue(r[0], out var uid)) continue;
            if (!int.TryParse(r[5], out var tag) || !int.TryParse(r[6], out var stunde)) continue;
            if (!gesehen.Add((uid, tag - 1, stunde))) continue;
            var u = d.Unterrichte.First(x => x.Id == uid);
            var raum = ByK(d.Raeume, r.ElementAtOrDefault(4) ?? "");
            d.Stundenplan.Add(new Planstunde { UnterrichtId = uid, Tag = tag - 1, Stunde = stunde, Raeume = u.Zeilen.Select(_ => raum?.Id).ToList() });
        }
        return b;
    }
}
