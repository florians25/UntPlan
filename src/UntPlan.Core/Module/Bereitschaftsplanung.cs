using UntPlan.Core.Model;

namespace UntPlan.Core.Module;

/// <summary>Bereitschaftsstunde eines Lehrers im Regelstundenplan.</summary>
public sealed record Bereitschaftsstunde(Lehrer Lehrer, int Tag, int Stunde, Unterricht Unterricht, Planstunde Planstunde);

/// <summary>Bewertung eines Lehrers für eine Bereitschaft (Schlechtpunkte, je niedriger desto besser).</summary>
public sealed record BereitschaftsKandidat(Lehrer Lehrer, double Punkte, bool Hohlstunde, bool Randstunde, int BereitschaftenWoche, decimal FreiesDeputat, string Hinweis);

/// <summary>
/// Bereitschaftsplanung: Bedarf an Bereitschaften je Tag/Stunde, Abgleich mit dem Regelstundenplan (Unterricht mit
/// Bereitschaftsfach), Lehrervorschlag (frei, nicht gesperrt, Hohlstunde bevorzugt, Höchstzahl je Woche, freies Deputat)
/// und automatische Verteilung. Bereitschaften werden als fixierte Stunden eines Bereitschafts-Unterrichts gesetzt.
/// </summary>
public static class Bereitschaftsplanung
{
    public static Fach? Bereitschaftsfach(Schuldatei d) => d.Faecher.FirstOrDefault(f => f.Bereitschaft);

    /// <summary>Bereitschaftsfach anlegen, falls es noch keines gibt.</summary>
    public static Fach FachSicherstellen(Schuldatei d)
    {
        if (Bereitschaftsfach(d) is { } f) return f;
        f = new Fach { Id = d.NeueId(), Kurzname = "BER", Langname = "Bereitschaft", Bereitschaft = true, Sortierung = d.Faecher.Count, Hintergrund = 0xE0E0E0 };
        d.Faecher.Add(f);
        return f;
    }

    public static List<Bereitschaftsstunde> Stunden(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var res = new List<Bereitschaftsstunde>();
        foreach (var p in d.Stundenplan)
        {
            if (!ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)) continue;
            foreach (var z in u.Zeilen)
                if (z.FachId is int f && ix.Faecher.TryGetValue(f, out var fa) && fa.Bereitschaft && z.LehrerId is int l && ix.Lehrer.TryGetValue(l, out var le))
                    res.Add(new Bereitschaftsstunde(le, p.Tag, p.Stunde, u, p));
        }
        return res;
    }

    public static int Bedarf(Schuldatei d, int tag, int stunde) => d.Bereitschaftsbedarf.FirstOrDefault(b => b.Tag == tag && b.Stunde == stunde)?.Anzahl ?? 0;

    public static void BedarfSetzen(Schuldatei d, int tag, int stunde, int anzahl)
    {
        d.Bereitschaftsbedarf.RemoveAll(b => b.Tag == tag && b.Stunde == stunde);
        if (anzahl > 0) d.Bereitschaftsbedarf.Add(new Bereitschaftsbedarf { Tag = tag, Stunde = stunde, Anzahl = anzahl });
    }

    /// <summary>Lehrervorschlag für eine Bereitschaft an (tag, stunde).</summary>
    public static List<BereitschaftsKandidat> Vorschlag(Schuldatei d, int tag, int stunde)
    {
        var ix = new DatenIndex(d);
        var bereit = Stunden(d);
        var res = new List<BereitschaftsKandidat>();
        foreach (var l in d.Lehrer.Where(l => !l.Ignorieren && !l.KeineVertretung))
        {
            var plan = ix.PlanVon(ElementTyp.Lehrer, l.Id).Where(p => p.Tag == tag).Select(p => p.Stunde).ToHashSet();
            if (plan.Contains(stunde)) continue; // belegt (Unterricht oder schon Bereitschaft)
            var zw = l.Zeitwuensche.Get(tag, stunde);
            if (zw <= -3 || !d.Zeitraster.Verfuegbar(tag, stunde)) continue;
            var woche = bereit.Count(b => b.Lehrer.Id == l.Id);
            if (l.BereitschaftenMax > 0 && woche >= l.BereitschaftenMax) continue;
            var unterrichtTag = plan.Count;
            var hohl = plan.Any(s => s < stunde) && plan.Any(s => s > stunde);
            var rand = plan.Contains(stunde - 1) || plan.Contains(stunde + 1);
            var frei = l.Soll - Wertrechnung.Ist(d, ix, l);
            double punkte = woche * 15 + (zw < 0 ? -zw * 10 : 0);
            if (unterrichtTag == 0) punkte += 60;      // zusätzlicher Anwesenheitstag
            else if (hohl) punkte -= 25;               // füllt eine Hohlstunde
            else if (rand) punkte -= 10;               // schließt an
            else punkte += 15;
            if (frei > 0) punkte -= (double)Math.Min(frei, 5) * 3;
            var hinweis = unterrichtTag == 0 ? "kein Unterricht an diesem Tag" : hohl ? "Hohlstunde" : rand ? "anschließend" : "Lücke";
            res.Add(new BereitschaftsKandidat(l, punkte, hohl, rand, woche, frei, hinweis));
        }
        return res.OrderBy(k => k.Punkte).ThenBy(k => k.Lehrer.Kurzname).ToList();
    }

    /// <summary>Bereitschaft eintragen: Bereitschafts-Unterricht des Lehrers (wird bei Bedarf angelegt) um eine fixierte Stunde ergänzen.</summary>
    public static void Setzen(Schuldatei d, int lehrerId, int tag, int stunde)
    {
        var fach = FachSicherstellen(d);
        var u = d.Unterrichte.FirstOrDefault(x => x.Zeilen.Count == 1 && x.Zeilen[0].FachId == fach.Id && x.Zeilen[0].LehrerId == lehrerId);
        if (u is null)
        {
            u = new Unterricht { Id = d.NeueId(), Wochenstunden = 0, Zeilen = { new Kopplungszeile { FachId = fach.Id, LehrerId = lehrerId } }, Text = "Bereitschaft" };
            d.Unterrichte.Add(u);
        }
        if (d.Stundenplan.Any(p => p.UnterrichtId == u.Id && p.Tag == tag && p.Stunde == stunde)) return;
        u.Wochenstunden++;
        d.Stundenplan.Add(new Planstunde { UnterrichtId = u.Id, Tag = tag, Stunde = stunde, Raeume = { null }, Fixiert = true });
    }

    public static void Entfernen(Schuldatei d, int lehrerId, int tag, int stunde)
    {
        foreach (var b in Stunden(d).Where(b => b.Lehrer.Id == lehrerId && b.Tag == tag && b.Stunde == stunde).ToList())
        {
            d.Stundenplan.Remove(b.Planstunde);
            b.Unterricht.Wochenstunden = Math.Max(0, b.Unterricht.Wochenstunden - 1);
            if (b.Unterricht.Wochenstunden == 0) d.Unterrichte.Remove(b.Unterricht);
        }
    }

    /// <summary>Fehlende Bereitschaften laut Bedarf automatisch besetzen (beste Kandidaten zuerst). Liefert die Anzahl gesetzter Stunden.</summary>
    public static int Automatisch(Schuldatei d)
    {
        if (d.Einstellungen.BereitschaftNurDoppelstunden) return AutomatischDoppelt(d);
        var n = 0;
        // knappe Stunden (wenige Kandidaten) zuerst
        var offen = d.Bereitschaftsbedarf.Select(b => (b.Tag, b.Stunde, Fehlt: b.Anzahl - Stunden(d).Count(x => x.Tag == b.Tag && x.Stunde == b.Stunde)))
            .Where(x => x.Fehlt > 0).OrderBy(x => Vorschlag(d, x.Tag, x.Stunde).Count).ToList();
        foreach (var (tag, stunde, fehlt) in offen)
            for (var i = 0; i < fehlt; i++)
            {
                var k = Vorschlag(d, tag, stunde).FirstOrDefault();
                if (k is null) break;
                Setzen(d, k.Lehrer.Id, tag, stunde);
                n++;
            }
        return n;
    }

    private static int Fehlt(Schuldatei d, int tag, int stunde) => Bedarf(d, tag, stunde) - Stunden(d).Count(x => x.Tag == tag && x.Stunde == stunde);

    /// <summary>
    /// Bereitschaften nur als Doppelstunden: Jede offene Stunde wird mit einer Nachbarstunde desselben Tages zusammen besetzt.
    /// Bevorzugt wird eine Nachbarstunde, in der ebenfalls Bedarf fehlt; der Lehrer muss in beiden Stunden frei sein.
    /// Gibt es keine Nachbarstunde mit Bedarf, wird die angrenzende verfügbare Stunde (gleiche Tageshälfte) genommen.
    /// </summary>
    private static int AutomatischDoppelt(Schuldatei d)
    {
        var n = 0;
        var offen = d.Bereitschaftsbedarf.Where(b => Fehlt(d, b.Tag, b.Stunde) > 0)
            .OrderBy(b => Vorschlag(d, b.Tag, b.Stunde).Count).ThenBy(b => b.Tag).ThenBy(b => b.Stunde).Select(b => (b.Tag, b.Stunde)).ToList();
        foreach (var (tag, stunde) in offen)
        {
            var schutz = 0;
            while (Fehlt(d, tag, stunde) > 0 && schutz++ < 20)
            {
                var vormittag = stunde <= d.Zeitraster.LetzteVormittagsstunde;
                var partner = new[] { stunde + 1, stunde - 1 }
                    .Where(s => s >= 1 && s <= d.Zeitraster.StundenProTag && d.Zeitraster.Verfuegbar(tag, s) && (s <= d.Zeitraster.LetzteVormittagsstunde) == vormittag)
                    .OrderByDescending(s => Fehlt(d, tag, s) > 0).ToList();
                (BereitschaftsKandidat K, int Partner, double Punkte)? bester = null;
                var hier = Vorschlag(d, tag, stunde);
                foreach (var s in partner)
                {
                    var dort = Vorschlag(d, tag, s).ToDictionary(k => k.Lehrer.Id);
                    foreach (var k in hier)
                    {
                        if (!dort.TryGetValue(k.Lehrer.Id, out var k2)) continue;
                        // höchstens so viele Bereitschaften wie erlaubt (zwei auf einmal)
                        if (k.Lehrer.BereitschaftenMax > 0 && k.BereitschaftenWoche + 2 > k.Lehrer.BereitschaftenMax) continue;
                        var punkte = k.Punkte + k2.Punkte + (Fehlt(d, tag, s) > 0 ? 0 : 40);
                        if (bester is null || punkte < bester.Value.Punkte) bester = (k, s, punkte);
                    }
                    if (bester != null && Fehlt(d, tag, s) > 0) break; // Partner mit Bedarf gefunden
                }
                if (bester is null) break;
                Setzen(d, bester.Value.K.Lehrer.Id, tag, stunde);
                Setzen(d, bester.Value.K.Lehrer.Id, tag, bester.Value.Partner);
                n += 2;
            }
        }
        return n;
    }
}
