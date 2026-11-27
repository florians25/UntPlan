using System.Globalization;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Stand des Nachpflegens der Vergangenheit (Hintergrundauftrag).</summary>
public sealed record Nachpflegestand(bool Laeuft, string Von, string Bis, int Tage, int Fertig, int Eintraege, int Uebersprungen, string Beginn, string Ende, string Fehler);

public sealed partial class Schulsimulation
{
    private Nachpflegestand _nachpflegen = new(false, "", "", 0, 0, 0, 0, "", "", "");

    public Nachpflegestand Nachpflegestatus => _nachpflegen;

    /// <summary>Vergangenheit nachpflegen als Hintergrundauftrag starten (Standard: Schuljahresbeginn bis gestern).</summary>
    public Nachpflegestand NachpflegenStarten(Benutzer ich, DateOnly? von, DateOnly? bis)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        var s = _paket.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
        lock (_sperre)
        {
            if (_nachpflegen.Laeuft) throw new ArgumentException("Das Nachpflegen läuft bereits.");
            var (v, b) = Zeitraum(s, von, bis);
            var e = MitStartwert();
            _nachpflegen = new(true, Plandienst.Iso(v), Plandienst.Iso(b), 0, 0, 0, 0, Zeit(_uhr.Jetzt), "", "");
            Task.Run(() =>
            {
                try { Nachpflegen(s, e, v, b); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Schulsimulation: Nachpflegen fehlgeschlagen");
                    _nachpflegen = _nachpflegen with { Laeuft = false, Fehler = ex.Message, Ende = Zeit(_uhr.Jetzt) };
                }
            });
            return _nachpflegen;
        }
    }

    private (DateOnly Von, DateOnly Bis) Zeitraum(Stand s, DateOnly? von, DateOnly? bis)
    {
        var gestern = _uhr.Heute.AddDays(-1);
        var v = von ?? Halbjahre(s)[0].Von;
        var b = bis ?? gestern;
        if (b > gestern) b = gestern;
        if (v < Halbjahre(s)[0].Von) v = Halbjahre(s)[0].Von;
        if (v > b) throw new ArgumentException("Bitte einen Zeitraum in der Vergangenheit wählen (Beginn vor Ende, Ende spätestens gestern).");
        return (v, b);
    }

    /// <summary>Einstellungen mit Startwert (das Nachpflegen braucht ihn auch, wenn die Simulation nie eingeschaltet war).</summary>
    private Simulationseinstellungen MitStartwert()
    {
        var e = Laden();
        if (e.Seed != 0) return e;
        e = e with { Seed = Random.Shared.NextInt64(1, long.MaxValue) };
        _einstellungen.Schreiben("simulation.einstellungen", System.Text.Json.JsonSerializer.Serialize(e, Json));
        return e;
    }

    /// <summary>
    /// Vergangenheit nachpflegen: Für jeden Schultag im Zeitraum tragen die Lehrkräfte fehlende Abwesenheitskontrollen, Lehrstoff und Klassenbucheinträge
    /// nach, die Klassenleitungen entschuldigen. Realistisch gesammelt einige Tage später (Freitagnachmittag, Wochenende), immer vor jetzt. Gleiche Ereignisse
    /// wie im laufenden Betrieb (gleiche Schlüssel): Was schon ausgeführt ist, bleibt; erneutes Nachpflegen ändert nichts. Ausgenommene und abwesende
    /// Lehrkräfte tragen nie ein, Einträge anderer Benutzer werden nie überschrieben. Je Tag ein Stapel (Ereignisvermerke in einer Transaktion).
    /// </summary>
    public Nachpflegestand Nachpflegen(Stand s, Simulationseinstellungen e, DateOnly von, DateOnly bis)
    {
        var jetzt = _uhr.Jetzt;
        var tage = Schuljahrestage(s).Where(d => d >= von && d <= bis).ToList();
        _nachpflegen = _nachpflegen with { Laeuft = true, Von = Plandienst.Iso(von), Bis = Plandienst.Iso(bis), Tage = tage.Count, Fertig = 0, Eintraege = 0, Uebersprungen = 0 };
        var simStart = DateTime.TryParse(e.Start, Inv, DateTimeStyles.None, out var st) ? DateOnly.FromDateTime(st) : DateOnly.MaxValue;
        // vor dem Start der Simulation eine eigene Grundlage (Ausfälle der Lehrkräfte und Schüler*innen), ab dem Start die des laufenden Betriebs
        Weltstand vorher;
        Weltstand? betrieb;
        lock (_sperre)
        {
            vorher = Vergangenheitswelt(s, e, von, simStart == DateOnly.MaxValue ? bis : simStart.AddDays(-1));
            betrieb = simStart <= bis ? Welt(s, e, st, jetzt) : null;
        }
        var erledigt = _db.Liste("SELECT schluessel FROM simulation_ereignisse", r => r.Text("schluessel")).ToHashSet(StringComparer.Ordinal);
        int ok = 0, weg = 0;
        foreach (var d in tage)
        {
            var w = d >= simStart && betrieb is not null ? betrieb : vorher;
            var liste = new List<Ereignis>();
            lock (_sperre)
            {
                liste.AddRange(Tagesereignisse(s, e, w, d, true).Where(x => x.Schluessel[..3] is "an|" or "kb|" or "vm|"));
                // Verspätungen: Kontrolle zur Stundenzeit, dann Eintreffen; sonst (gesammelt nachgetragen) gleich als Verspätung
                liste.AddRange(Verspaetungsereignisse(s, e, w, d));
                if (e.Entschuldigungen)
                {
                    liste.AddRange(w.Schuelerausfaelle.Where(a => a.Bis == d).Select(a => Entschuldigung(s, e, w, a)).OfType<Ereignis>());
                    liste.AddRange(w.Schuelerausfaelle.Where(a => a.Von == d).Select(a => Schuelerkrankmeldung(s, e, w, a)).OfType<Ereignis>());
                }
                var vermerke = new List<(string, string, string)>();
                var z = Zufall.Fuer(w.Seed, "np", d);
                // gesammelt: nach dem Tag, bevorzugt Freitagnachmittag bzw. Wochenende; Entschuldigungen zu ihrer Zeit (spätestens jetzt)
                var sammel = Sammelzeit(d, z);
                foreach (var x in liste.Where(x => !erledigt.Contains(x.Schluessel)).OrderBy(x => x.Schluessel[..3] == "en|" ? 1 : 0).ThenBy(x => x.Zeit))
                {
                    var zeit = x.Schluessel[..3] is "en|" or "ms|" or "sp|" ? x.Zeit : x.Zeit < Um(d, 23 * 60) && z.Chance(0.25) ? x.Zeit : sammel.AddMinutes(z.Zahl(0, 90));
                    if (zeit >= jetzt) zeit = jetzt.AddMinutes(-1);
                    string ergebnis;
                    using (_uhr.Simuliert(zeit))
                    {
                        try { x.Aktion(); ergebnis = "ok"; ok++; }
                        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException)
                        { ergebnis = ex.Message.Length > 290 ? ex.Message[..290] : ex.Message; weg++; }
                    }
                    vermerke.Add((x.Schluessel.Length > 190 ? x.Schluessel[..190] : x.Schluessel, Zeit(zeit), ergebnis));
                    erledigt.Add(x.Schluessel);
                }
                if (vermerke.Count > 0)
                    _db.Transaktion(c =>
                    {
                        foreach (var (k, z2, erg) in vermerke)
                            Db.Ausfuehren(c, "INSERT INTO simulation_ereignisse (schluessel, zeit, ausgefuehrt, ergebnis) VALUES (@s, @z, @a, @e) ON CONFLICT(schluessel) DO NOTHING",
                                Db.P("@s", k), Db.P("@z", z2), Db.P("@a", Zeit(jetzt)), Db.P("@e", "nachgepflegt: " + erg));
                    });
            }
            _nachpflegen = _nachpflegen with { Fertig = _nachpflegen.Fertig + 1, Eintraege = ok, Uebersprungen = weg };
        }
        _nachpflegen = _nachpflegen with { Laeuft = false, Ende = Zeit(_uhr.Jetzt) };
        return _nachpflegen;
    }

    /// <summary>Freitagnachmittag bzw. Wochenende nach dem Tag (selten erst eine Woche später).</summary>
    private static DateTime Sammelzeit(DateOnly d, Zufall z)
    {
        var freitag = d.AddDays(((int)DayOfWeek.Friday - (int)d.DayOfWeek + 7) % 7);
        if (freitag == d && z.Chance(0.5)) freitag = freitag.AddDays(7);
        if (z.Chance(0.15)) freitag = freitag.AddDays(7);
        return z.Gewichtet(new[] { (0, 45.0), (1, 30.0), (2, 25.0) }, x => x.Item2).Item1 switch
        {
            0 => Um(freitag, z.Zahl(13 * 60 + 30, 17 * 60 + 30)),
            1 => Um(freitag.AddDays(1), z.Zahl(9 * 60, 16 * 60)),
            _ => Um(freitag.AddDays(2), z.Zahl(16 * 60, 21 * 60 + 30)),
        };
    }

    /// <summary>Grundlage für die Zeit vor dem Start: Ausfälle der Lehrkräfte und Schüler*innen im Zeitraum (gleicher Startwert, eigener Zeitraum).</summary>
    private Weltstand Vergangenheitswelt(Stand s, Simulationseinstellungen e, DateOnly von, DateOnly bis)
    {
        var ausgenommen = Ausgenommen(e).ToHashSet(Ci);
        var tage = Schuljahrestage(s);
        var klassenDerLehrkraft = new Dictionary<string, List<string>>(Ci);
        foreach (var u in s.Unterricht.Values)
            foreach (var z in u.Zeilen.Where(z => z.Lehrer.Length > 0))
            {
                if (!klassenDerLehrkraft.TryGetValue(z.Lehrer, out var l)) klassenDerLehrkraft[z.Lehrer] = l = new();
                foreach (var k in z.Klassen) if (!l.Contains(k, Ci)) l.Add(k);
            }
        var w = new Weltstand
        {
            Seed = e.Seed, Start = von, Bis = bis, Schultage = tage.ToHashSet(), Tagesliste = tage, Ausgenommen = ausgenommen,
            Ausfaelle = AusfaelleBerechnen(s, e.Seed, von.AddDays(-1), bis, tage, ausgenommen, klassenDerLehrkraft, e.I),
            Schuelerausfaelle = SchuelerausfaelleBerechnen(s, e.Seed, von, bis, tage, e.I), KlassenDerLehrkraft = klassenDerLehrkraft, Intensitaet = e.I,
        };
        foreach (var a in w.Schuelerausfaelle)
            foreach (var d in a.Tage) w.SchuelerAm.TryAdd((a.SchuelerId, d), a);
        return w;
    }
}
