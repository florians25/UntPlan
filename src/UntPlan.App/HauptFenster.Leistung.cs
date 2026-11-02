using System.Diagnostics;
using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App;

/// <summary>
/// Leistungslauf (UntPlan.exe --leistung=Ordner): große Schule (Beispielschule achtfach), Vertretungsmodus mit den üblichen
/// Fenstern; misst Tageswechsel, Zeilenwechsel in der Vertretungsliste, Vertretung ändern und Moduswechsel (je einschließlich
/// Neuzeichnen aller Fenster) und schreibt die Zeiten nach leistung.log. Für Profilaufnahmen (EventPipe) gedacht.
/// </summary>
public sealed partial class HauptFenster
{
    internal void Leistungslauf(string ordner)
    {
        Directory.CreateDirectory(ordner);
        WindowState = FormWindowState.Normal;
        Bounds = Screen.PrimaryScreen?.Bounds ?? Bounds;
        var log = new List<string>();
        var faktor = int.TryParse(Environment.GetEnvironmentVariable("UNTPLAN_FAKTOR"), out var f) ? f : 8;
        var gesamt = Stopwatch.StartNew();
        var d = Demo.Beispielschule();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 1 });
        d.AktivePeriode.Plan = erg.OrderBy(e => e.Kennzahlen.Bewertung).First().Plan;
        Demo.Vervielfachen(d, faktor);
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var tag = Kalender.IstUnterrichtstag(d, heute) ? heute : Kalender.NaechsterUnterrichtstag(d, heute);
        var tage = new List<DateOnly> { tag };
        while (tage.Count < 5) tage.Add(Kalender.NaechsterUnterrichtstag(d, tage[^1]));
        // je Tag mehrere abwesende Lehrkräfte
        var rnd = new Random(7);
        foreach (var t in tage)
        {
            foreach (var l in d.Lehrer.OrderBy(_ => rnd.Next()).Take(Math.Max(3, d.Lehrer.Count / 12)))
                d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = l.Id, Von = t, Bis = t });
            Vertretungen.Aktualisieren(d, t);
        }
        log.Add($"INFO  Schule: {d.Klassen.Count} Klassen, {d.Lehrer.Count} Lehrkräfte, {d.Raeume.Count} Räume, {d.Unterrichte.Count} Unterrichte, {d.AktivePeriode.Plan.Count} Planstunden, {d.Vertretungen.Count} Vertretungen; Aufbau {gesamt.ElapsedMilliseconds} ms");
        AppZustand.Setzen(new Dokument(d));
        AppZustand.DatumSetzen(tag);

        void Pumpen()
        {
            // nur Ausstehendes abarbeiten (Neuzeichnen ungültiger Fenster, nachgeholte Neuaufbauten) – nichts erzwingen
            for (var i = 0; i < 4; i++) Application.DoEvents();
            foreach (var m in MdiChildren) m.Update();
            Application.DoEvents();
        }
        void Mess(string name, Action a, int n = 1)
        {
            Pumpen();
            Messung.Leeren();
            Messung.An = true;
            var sw = Stopwatch.StartNew();
            var einzel = new List<long>();
            for (var i = 0; i < n; i++)
            {
                var s0 = sw.ElapsedMilliseconds;
                a();
                Pumpen();
                einzel.Add(sw.ElapsedMilliseconds - s0);
            }
            Messung.An = false;
            log.Add($"ZEIT  {name}: Ø {sw.ElapsedMilliseconds / (double)n:0} ms (min {einzel.Min()}, max {einzel.Max()}, n={n})");
            log.AddRange(Messung.Bericht(25));
        }

        try
        {
            Mess("Vertretungsmodus einschalten", VertretungsmodusUmschalten);
            Mess("Fenster öffnen (Vertretungen, Absenzen, 3 Stundenpläne)", () =>
            {
                AlleSchliessen();
                FensterOeffnen("vertr");
                FensterOeffnen("abw");
                StundenplanFenster.Neu(ElementTyp.Lehrer, d.Lehrer[0].Id, false);
                StundenplanFenster.Neu(ElementTyp.Klasse, d.Klassen[0].Id, false);
                StundenplanFenster.Neu(ElementTyp.Raum, d.Raeume[0].Id, false);
                LayoutMdi(MdiLayout.TileVertical);
            });
            Mess("Tageswechsel", () =>
            {
                var i = tage.IndexOf(AppZustand.Datum);
                AppZustand.DatumSetzen(tage[(i + 1) % tage.Count]);
            }, 10);
            AppZustand.DatumSetzen(tag);
            var liste = AppZustand.D.Vertretungen.Where(v => v.Datum == tag && v.UnterrichtId.HasValue).Take(20).ToList();
            Mess("Zeilenwechsel Vertretungsliste (Pläne folgen)", () =>
            {
                var v = liste[rnd.Next(liste.Count)];
                AppZustand.VertretungWaehlen(v);
            }, 20);
            FensterOeffnen("vorschlag");
            Mess("Zeilenwechsel mit Vertretungsvorschlag", () => AppZustand.VertretungWaehlen(liste[rnd.Next(liste.Count)]), 10);
            var offen = AppZustand.D.Vertretungen.Where(v => v.Datum == tag && v.Offen).Take(10).ToList();
            var freie = AppZustand.D.Lehrer.Select(l => l.Id).ToList();
            Mess("Vertretung ändern (Vertreter setzen)", () =>
            {
                var v = offen[rnd.Next(offen.Count)];
                var vid = v.Id;
                var neu = freie[rnd.Next(freie.Count)];
                AppZustand.Doc.Aendern("Leistung", AenderungsArt.Vertretung, dd =>
                {
                    var x = dd.Vertretungen.First(y => y.Id == vid);
                    x.VertreterId = neu;
                    x.Bearbeitet = true;
                });
            }, 10);
            Mess("Rückgängig", () => AppZustand.Doc.Rueckgaengig(), 5);
            Mess("Stundenplanmodus", VertretungsmodusUmschalten);
            Mess("Vertretungsmodus", VertretungsmodusUmschalten);
        }
        catch (Exception ex)
        {
            log.Add("FEHLER " + ex);
        }
        log.Add($"INFO  gesamt {gesamt.ElapsedMilliseconds} ms");
        log.Add("ENDE");
        File.WriteAllLines(Path.Combine(ordner, "leistung.log"), log);
        Close();
    }
}
