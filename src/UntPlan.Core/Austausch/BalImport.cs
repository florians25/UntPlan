using System.Globalization;
using System.Text;
using UntPlan.Core.Model;

namespace UntPlan.Core.Austausch;

/// <summary>Optionen für den Import einer BAL-Datei (WILLI2-Stundenplan).</summary>
public sealed class BalOptionen
{
    public bool Zeitraster { get; set; } = true;
    public bool Stammdaten { get; set; } = true;
    /// <summary>Unterricht und Stundenplan der aktiven Periode vorher löschen.</summary>
    public bool UnterrichtErsetzen { get; set; } = true;
    public bool Stundenplan { get; set; } = true;
    public bool Pausenaufsichten { get; set; } = true;
    public bool Zeitwuensche { get; set; } = true;
}

/// <summary>
/// Import von BAL-Dateien (WILLI2-Stundenplan, Latin-1, CSV mit Anführungszeichen). Satzarten:
/// W (Schule), T (Tage, Belegung V/N/X), S (Stundenzeiten), L/LQ (Lehrer, Verfügbarkeiten), R (Räume), F (Fächer), K (Klassen),
/// O (Aufsichtsorte), Z (Pausen), A (Aufsichten), J (Kopplungsgruppen), U (Unterricht), PL (Stundenplan), MK (zusammengefasste Klassen).
/// </summary>
public static class BalImport
{
    public static bool IstBal(string text) => text.Contains("WILLI2", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith("\"W\"", StringComparison.Ordinal);

    public static string DateiLesen(string pfad)
    {
        var bytes = File.ReadAllBytes(pfad);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static readonly string[] TagKurz = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

    private static string Satzart(string feld)
    {
        var i = feld.Length;
        while (i > 0 && char.IsDigit(feld[i - 1])) i--;
        return feld[..i];
    }

    private static string F(string[] r, int i) => i < r.Length ? r[i].Trim() : "";

    private static decimal Zahl(string s) =>
        decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int Ganz(string s) => int.TryParse(s, out var v) ? v : 0;

    private static TimeOnly Zeit(string s) => TimeOnly.TryParse(s, CultureInfo.InvariantCulture, out var t) ? t : default;

    /// <summary>"Mo 3" → (0, 3).</summary>
    private static (int Tag, int Stunde)? Position(string s)
    {
        var t = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != 2) return null;
        var tag = Array.FindIndex(TagKurz, x => x.Equals(t[0], StringComparison.OrdinalIgnoreCase));
        return tag >= 0 && int.TryParse(t[1], out var st) ? (tag, st) : null;
    }

    /// <summary>Platzhalter wie "?", "???", "NN", "---" werden nicht übernommen.</summary>
    private static bool Platzhalter(string k) => k.Length == 0 || k.All(c => c is '?' or '-') || k is "NN";

    public static ImportBericht Importieren(Schuldatei d, string text, BalOptionen? o = null)
    {
        o ??= new BalOptionen();
        var b = new ImportBericht();
        var zeilen = Csv.Lesen(text, ',').Where(r => r.Length > 0 && r[0].Length > 0 && !r[0].StartsWith("---")).ToList();
        var nach = zeilen.GroupBy(r => Satzart(r[0])).ToDictionary(g => g.Key, g => g.ToList());
        List<string[]> S(string art) => nach.TryGetValue(art, out var l) ? l : new();
        T? K<T>(IEnumerable<T> l, string k) where T : Element => k.Length == 0 ? null : l.FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase));

        // --- Schule ---------------------------------------------------------------------------------
        if (S("W").FirstOrDefault() is { } w && F(w, 2).Length > 0 && (string.IsNullOrWhiteSpace(d.Einstellungen.Schulname) || o.UnterrichtErsetzen))
            d.Einstellungen.Schulname = F(w, 2);

        // --- Zeitraster -----------------------------------------------------------------------------
        if (o.Zeitraster)
        {
            var tage = S("T");
            var stunden = S("S");
            if (tage.Count > 0) d.Zeitraster.TageProWoche = Math.Clamp(tage.Count, 1, 7);
            if (stunden.Count > 0)
            {
                d.Zeitraster.StundenProTag = stunden.Count;
                d.Zeitraster.Zeiten = stunden.Select(r => new Stundenzeit { Nr = Ganz(F(r, 1)), Bezeichnung = F(r, 2), Beginn = Zeit(F(r, 3)), Ende = Zeit(F(r, 4)) }).Where(z => z.Nr > 0).ToList();
            }
            d.Zeitraster.Belegung.Clear();
            for (var t = 0; t < tage.Count && t < 7; t++)
            {
                var bel = F(tage[t], 3);
                for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
                {
                    var c = s - 1 < bel.Length ? bel[s - 1] : 'X';
                    d.Zeitraster.Belegung[t * 100 + s] = c switch { 'V' => Tageszeit.Vormittag, 'N' => Tageszeit.Nachmittag, _ => Tageszeit.Leer };
                }
            }
            var letzteV = Enumerable.Range(1, d.Zeitraster.StundenProTag).LastOrDefault(s => d.Zeitraster.Art(0, s) == Tageszeit.Vormittag);
            if (letzteV > 0) d.Zeitraster.LetzteVormittagsstunde = letzteV;
            // Pausen: "Vor" Stunde n → nach Stunde n-1; "In" (z. B. Prüfungsaufsicht) wird nicht als Pause übernommen
            var pausen = new List<Pause>();
            foreach (var r in S("Z").Where(r => F(r, 2).Equals("Vor", StringComparison.OrdinalIgnoreCase)))
            {
                var nachStunde = Ganz(F(r, 3)) - 1;
                var dauer = (int)(Zeit(F(r, 5)) - Zeit(F(r, 4))).TotalMinutes;
                pausen.Add(new Pause { NachStunde = nachStunde, Bezeichnung = F(r, 1).TrimStart('_'), DauerMinuten = Math.Max(0, dauer), Aufsicht = true });
            }
            if (pausen.Count > 0) d.Zeitraster.Pausen = pausen.OrderBy(p => p.NachStunde).ToList();
        }

        // --- Stammdaten -----------------------------------------------------------------------------
        if (o.Stammdaten)
        {
            foreach (var r in S("R"))
            {
                var k = F(r, 1);
                if (Platzhalter(k.Replace("?", "")) && k.Contains('?')) continue;
                if (K(d.Raeume, k) is { } x) { if (F(r, 3).Length > 0) x.Langname = F(r, 3); b.Aktualisiert++; }
                else { d.Raeume.Add(new Raum { Id = d.NeueId(), Kurzname = k, Langname = F(r, 3), Sortierung = d.Raeume.Count }); b.Neu++; }
            }
            foreach (var r in S("F"))
            {
                var k = F(r, 1);
                if (Platzhalter(k)) continue;
                if (K(d.Faecher, k) is { } x) { if (F(r, 3).Length > 0) x.Langname = F(r, 3); b.Aktualisiert++; }
                else { d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = k, Langname = F(r, 3), Sortierung = d.Faecher.Count }); b.Neu++; }
            }
            foreach (var r in S("K"))
            {
                var k = F(r, 1);
                if (Platzhalter(k)) continue;
                if (K(d.Klassen, k) is { } x) { if (F(r, 3).Length > 0) x.Langname = F(r, 3); b.Aktualisiert++; }
                else
                {
                    d.Klassen.Add(new Klasse { Id = d.NeueId(), Kurzname = k, Langname = F(r, 3), Sortierung = d.Klassen.Count, Jahrgang = new string(k.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()) });
                    b.Neu++;
                }
            }
            foreach (var r in S("L"))
            {
                var k = F(r, 1);
                if (Platzhalter(k)) continue;
                var l = K(d.Lehrer, k);
                if (l is null)
                {
                    l = new Lehrer { Id = d.NeueId(), Kurzname = k, Sortierung = d.Lehrer.Count };
                    d.Lehrer.Add(l);
                    b.Neu++;
                }
                else b.Aktualisiert++;
                if (F(r, 3).Length > 0) l.Langname = F(r, 3);
                if (F(r, 4).Length > 0) l.Vorname = F(r, 4);
                var soll = new[] { 6, 7, 8, 9, 10 }.Select(i => Zahl(F(r, i))).FirstOrDefault(x => x > 0);
                if (soll > 0) l.Wochensoll = soll;
                // Stammraum (Lehrer-Raum-Prinzip) steht als Raum-Kurzname gegen Ende des Satzes
                for (var i = r.Length - 1; i > 20; i--)
                    if (K(d.Raeume, F(r, i)) is { } raum) { l.StammraumId = raum.Id; break; }
                // gewünschter freier Tag ("Di,")
                if (o.Zeitwuensche)
                    foreach (var f in r.Skip(20).Where(x => x.EndsWith(',')))
                        foreach (var t in f.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = Array.FindIndex(TagKurz, x => x.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (tag < 0 || tag >= d.Zeitraster.TageProWoche) continue;
                            for (var s = 1; s <= d.Zeitraster.StundenProTag; s++) if (l.Zeitwuensche.Get(tag, s) == 0) l.Zeitwuensche.Set(tag, s, -2);
                        }
            }
            if (o.Zeitwuensche)
                foreach (var r in S("LQ"))
                    if (K(d.Lehrer, F(r, 1)) is { } l)
                        for (var t = 0; t < d.Zeitraster.TageProWoche && t + 2 < r.Length; t++)
                        {
                            var z = F(r, t + 2);
                            for (var s = 1; s <= Math.Min(z.Length, d.Zeitraster.StundenProTag); s++)
                                if (z[s - 1] == '0') l.Zeitwuensche.Set(t, s, -3);
                        }
            foreach (var r in S("O"))
            {
                var k = F(r, 1);
                if (k.Length == 0) continue;
                if (K(d.Gaenge, k) is null) { d.Gaenge.Add(new Gang { Id = d.NeueId(), Kurzname = k, Langname = k, Sortierung = d.Gaenge.Count }); b.Neu++; }
            }
        }

        // --- zusammengefasste Klassen (MK) -----------------------------------------------------------
        var mk = S("MK").Where(r => r.Length > 2).ToDictionary(r => F(r, 1), r => r.Skip(2).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), StringComparer.OrdinalIgnoreCase);
        List<int> Klassen(string k)
        {
            if (Platzhalter(k)) return new();
            if (K(d.Klassen, k) is { } x) return new() { x.Id };
            if (mk.TryGetValue(k, out var teile)) return teile.SelectMany(Klassen).Distinct().ToList();
            var neu = new Klasse { Id = d.NeueId(), Kurzname = k, Sortierung = d.Klassen.Count };
            d.Klassen.Add(neu);
            b.Hinweise.Add($"Klasse {k} war nicht in den Stammdaten und wurde angelegt.");
            return new() { neu.Id };
        }

        // --- Unterricht ----------------------------------------------------------------------------
        var periode = d.AktivePeriode;
        periode.Unterrichte ??= new();
        if (o.UnterrichtErsetzen)
        {
            periode.Unterrichte.Clear();
            periode.Plan.Clear();
        }
        var gruppen = new Dictionary<string, Unterricht>(StringComparer.OrdinalIgnoreCase);
        var unterrichte = new List<Unterricht>();
        foreach (var r in S("U"))
        {
            var lehrer = K(d.Lehrer, F(r, 1));
            var fach = K(d.Faecher, F(r, 2));
            if (fach is null && !Platzhalter(F(r, 2)))
            {
                fach = new Fach { Id = d.NeueId(), Kurzname = F(r, 2), Sortierung = d.Faecher.Count };
                d.Faecher.Add(fach);
            }
            var klassen = Klassen(F(r, 3));
            var gruppe = F(r, 4);
            var wst = (int)Math.Round(Zahl(F(r, 5)));
            var raum = K(d.Raeume, F(r, 15));
            var zeile = new Kopplungszeile { LehrerId = lehrer?.Id, FachId = fach?.Id, KlassenIds = klassen, FachraumId = raum?.Id };
            zeile.StammraumId = klassen.Select(k => d.Klassen.First(x => x.Id == k).StammraumId).FirstOrDefault(x => x.HasValue);
            Unterricht u;
            if (gruppe.Length > 0 && gruppen.TryGetValue(gruppe, out var vorhanden))
            {
                u = vorhanden;
                u.Wochenstunden = Math.Max(u.Wochenstunden, wst);
                u.Zeilen.Add(zeile);
                continue;
            }
            u = new Unterricht { Id = d.NeueId(), Wochenstunden = wst, Zeilen = new() { zeile }, Text = gruppe };
            // Blockung: Anzahl Blöcke (16) × Länge (18) – Länge 2 = Doppelstunden, ab 3 = Block
            var anzahl = Ganz(F(r, 16));
            var laenge = Ganz(F(r, 18));
            if (laenge == 2 && anzahl > 0 && anzahl * 2 <= wst) u.Doppelstunden = new Bereich(anzahl, anzahl);
            else if (laenge >= 3 && anzahl > 0) u.Block = string.Join(",", Enumerable.Repeat(laenge.ToString(), anzahl));
            if (gruppe.Length > 0) gruppen[gruppe] = u;
            unterrichte.Add(u);
        }
        foreach (var u in unterrichte)
        {
            if (u.Zeilen.All(z => z.FachId is null && z.LehrerId is null)) continue;
            periode.Unterrichte.Add(u);
            b.Neu++;
        }

        // --- Stundenplan ---------------------------------------------------------------------------
        if (o.Stundenplan)
        {
            var plan = new Dictionary<(int Uid, int Tag, int Stunde), Planstunde>();
            var ohne = 0;
            foreach (var r in S("PL"))
            {
                if (Position(F(r, 1)) is not { } pos || pos.Tag >= d.Zeitraster.TageProWoche) continue;
                var lehrer = K(d.Lehrer, F(r, 2));
                var klassen = Klassen(F(r, 3));
                var fach = K(d.Faecher, F(r, 4));
                var raum = K(d.Raeume, F(r, 5));
                // passende Kopplungszeile suchen: Lehrer + Fach + Klassen, sonst Lehrer + Fach, sonst Fach + Klassen
                (Unterricht U, int Z)? treffer = null;
                foreach (var u in periode.Unterrichte)
                    for (var zi = 0; zi < u.Zeilen.Count; zi++)
                    {
                        var z = u.Zeilen[zi];
                        if (z.FachId != fach?.Id || z.LehrerId != lehrer?.Id) continue;
                        if (klassen.Count == 0 || klassen.All(z.KlassenIds.Contains) || z.KlassenIds.All(klassen.Contains)) { treffer = (u, zi); break; }
                        treffer ??= (u, zi);
                    }
                if (treffer is null)
                {
                    // Stunde ohne Unterrichtssatz: Unterricht anlegen
                    var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 0, Zeilen = new() { new Kopplungszeile { LehrerId = lehrer?.Id, FachId = fach?.Id, KlassenIds = klassen } } };
                    periode.Unterrichte.Add(u);
                    treffer = (u, 0);
                    ohne++;
                }
                var (uu, zz) = treffer.Value;
                var key = (uu.Id, pos.Tag, pos.Stunde);
                if (!plan.TryGetValue(key, out var p))
                {
                    p = new Planstunde { UnterrichtId = uu.Id, Tag = pos.Tag, Stunde = pos.Stunde, Raeume = uu.Zeilen.Select(_ => (int?)null).ToList() };
                    plan[key] = p;
                }
                while (p.Raeume.Count < uu.Zeilen.Count) p.Raeume.Add(null);
                if (raum != null) p.Raeume[zz] = raum.Id;
            }
            // Unterrichte ohne Unterrichtssatz: Wochenstunden aus dem Plan
            foreach (var u in periode.Unterrichte.Where(u => u.Wochenstunden == 0))
                u.Wochenstunden = plan.Keys.Count(k => k.Uid == u.Id);
            periode.Plan.AddRange(plan.Values);
            b.Hinweise.Add($"{plan.Count} Unterrichtsstunden in den Stundenplan übernommen.");
            if (ohne > 0) b.Hinweise.Add($"{ohne} Unterrichte wurden aus dem Stundenplan ergänzt (kein U-Satz vorhanden).");
        }

        // --- Pausenaufsichten ------------------------------------------------------------------------
        if (o.Pausenaufsichten)
        {
            periode.Pausenaufsichten ??= new();
            if (o.UnterrichtErsetzen) periode.Pausenaufsichten.Clear();
            var n = 0;
            foreach (var r in S("A"))
            {
                var gang = K(d.Gaenge, F(r, 1));
                var lehrer = K(d.Lehrer, F(r, 3));
                if (gang is null || lehrer is null || Position(F(r, 6)) is not { } pos) continue;
                if (!F(r, 5).Equals("Vor", StringComparison.OrdinalIgnoreCase)) continue;
                periode.Pausenaufsichten.Add(new Pausenaufsicht { GangId = gang.Id, Tag = pos.Tag, NachStunde = pos.Stunde - 1, LehrerId = lehrer.Id, Teil = Math.Max(1, Ganz(F(r, 4))) });
                n++;
            }
            if (n > 0) b.Hinweise.Add($"{n} Pausenaufsichten übernommen.");
        }
        return b;
    }
}
