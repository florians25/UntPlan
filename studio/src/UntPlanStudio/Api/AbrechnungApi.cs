using System.Globalization;
using System.Security.Claims;
using System.Text;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record AbrWert(string Feld, string Wert);
public sealed record AbrTafelWert(string Jahrgang, int Fach, string Wert);
public sealed record AbrJahrgang(string Jahrgang);
/// <summary>Bericht der Vertretungsstatistik: Kopf, Breiten, Zeilen (Werte + optionale Lehrer-Id), Summentext.</summary>
public sealed record AbrBericht(int Nr, string Titel, string[] Kopf, int[] Breiten, List<AbrBerichtZeile> Zeilen, string Summe, bool LehrerNoetig);
public sealed record AbrBerichtZeile(string[] Werte, int? Lehrer, string? Markierung);

/// <summary>
/// Modul Unterrichtsplanung/Wertrechnung und Vertretungsstatistik (Desktop: „Unterrichtsplanung / Wertrechnung“ mit Lehrer Soll/Ist,
/// Unterrichts-Matrix und Stundentafel; „Vertretungsstatistik“ mit Abrechnung, Einzelnachweis (Zählung), Mehrarbeit NRW,
/// Monatsabrechnung, Entfalltage, Vertretungs-Übersicht und Wochenwerte als Tabelle, HTML-Dokument (auch für alle Lehrkräfte) und CSV).
/// </summary>
public sealed class AbrechnungApi : IStudioModul
{
    public string Name => "abrechnung";

    public static readonly string[] Berichte =
    {
        "Vertretungsabrechnung (alle Lehrer)", "Vertretungszähler – Einzelnachweis", "Mehrarbeit NRW (Monate)", "Monatsabrechnung (Tagesbilanz)",
        "Entfalltage", "Vertretungs-Übersicht", "Wochenwerte",
    };

    private static bool MitLehrer(int bericht) => bericht is 1 or 2 or 3 or 6;
    private static string S(decimal x) => x.ToString("+0.##;-0.##;0", Format.De);
    private static string Tag(DateOnly d) => $"{Planungsdienst.TageKurz[((int)d.DayOfWeek + 6) % 7]} {Format.Datum(d)}";

    private static decimal Dezimal(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return 0;
        if (!decimal.TryParse(text, NumberStyles.Number, Format.De, out var w)) throw StudioFehler.Ungueltig($"„{text}“ ist keine gültige Zahl.");
        return w;
    }

    // ------------------------------------------------------------------------------------------ Wertrechnung

    public static object Werte(Dokument doc)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var liste = Wertrechnung.Uebersicht(d);
        return new
        {
            spalten = new[] { "Lehrer", "Name", "Wochensoll", "Ermäßigung", "Soll", "Ist", "Wertkorrektur", "Differenz", "Wst.", "verplant", "Fächer" },
            zeilen = liste.OrderBy(w => w.Lehrer.Kurzname, StringComparer.Create(Format.De, true)).Select(w => new
            {
                id = w.Lehrer.Id,
                werte = new[]
                {
                    w.Lehrer.Kurzname, w.Lehrer.VollerName, Format.Zahl(w.Wochensoll), Format.Zahl(w.Ermaessigung), Format.Zahl(w.Soll), Format.Zahl(w.Ist),
                    Format.Zahl(w.Lehrer.Wertkorrektur), S(w.Differenz), w.Wochenstunden.ToString(), w.Verplant.ToString(), string.Join(", ", w.Lehrer.FaecherIds.Select(f => ix.KFach(f))),
                },
                differenz = w.Differenz,
            }),
            summe = $"Summe Soll: {Format.Zahl(liste.Sum(w => w.Soll))}   Ist: {Format.Zahl(liste.Sum(w => w.Ist))}   Differenz: {Format.Zahl(liste.Sum(w => w.Differenz))}   " +
                    "(Ist = Wochenstunden × Werte-Faktor, anteilig bei A/B-Wochen und befristetem Unterricht)",
        };
    }

    public static object Matrix(Dokument doc, int art)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var faecher = Stammdaten.Sortiert(d.Faecher).Where(f => d.Unterrichte.Any(u => u.FachIds.Contains(f.Id))).ToList();
        var m = Wertrechnung.Matrix(d);
        var zeilen = new List<object>();
        foreach (var k in Stammdaten.Sortiert(d.Klassen))
        {
            var werte = new string[faecher.Count + 2];
            werte[0] = k.Kurzname;
            var summe = 0;
            for (var i = 0; i < faecher.Count; i++)
            {
                if (!m.TryGetValue((k.Id, faecher[i].Id), out var l)) { werte[i + 1] = ""; continue; }
                var std = l.Select(x => x.U).Distinct().Sum(u => u.Wochenstunden);
                summe += l.Select(x => x.U).Distinct().Where(u => u.HauptfachId == faecher[i].Id).Sum(u => u.Wochenstunden);
                var lehrer = string.Join(",", l.Select(x => ix.KLehrer(x.Lehrer)).Where(s => s.Length > 0).Distinct());
                werte[i + 1] = art switch { 0 => lehrer, 1 => std.ToString(), _ => $"{std} {lehrer}" };
            }
            werte[^1] = summe.ToString();
            zeilen.Add(new { id = k.Id, werte });
        }
        return new { spalten = new[] { "Klasse" }.Concat(faecher.Select(f => f.Kurzname)).Append("Summe"), faecher = faecher.Select(f => f.Id), zeilen };
    }

    public static object Stundentafel(Dokument doc)
    {
        var d = doc.Daten;
        var faecher = Stammdaten.Sortiert(d.Faecher).Where(f => !f.Bereitschaft).ToList();
        var zeilen = d.Stundentafel.Select(z => z.Jahrgang).Distinct().OrderBy(Nrw.JahrgangRang).Select(j => new
        {
            jahrgang = j,
            werte = faecher.Select(f => d.Stundentafel.FirstOrDefault(x => x.Jahrgang == j && x.FachId == f.Id) is { Stunden: > 0 } z ? Format.Zahl(z.Stunden) : "").ToArray(),
            summe = Format.Zahl(d.Stundentafel.Where(x => x.Jahrgang == j).Sum(x => x.Stunden)),
        }).ToList();
        var abgleich = Wertrechnung.Stundentafel(d).Select(a => new
        {
            klasse = a.Klasse.Kurzname, fach = a.Fach.Kurzname, soll = Format.Zahl(a.Soll), ist = Format.Zahl(a.Ist), differenz = S(a.Ist - a.Soll), zuWenig = a.Ist < a.Soll,
        }).ToList();
        return new { faecher = faecher.Select(f => new { id = f.Id, kurz = f.Kurzname }), zeilen, abgleich };
    }

    /// <summary>Legt einen Jahrgang an und übernimmt als Vorschlag die Stunden aus dem vorhandenen Unterricht (wie im Desktop).</summary>
    private static void JahrgangSicherstellen(Schuldatei d, string jahrgang)
    {
        if (d.Stundentafel.Any(z => z.Jahrgang == jahrgang)) return;
        var klasse = d.Klassen.FirstOrDefault(k => k.Jahrgang == jahrgang);
        var ix = new DatenIndex(d);
        var vorschlag = klasse is null ? new Dictionary<int, int>() : ix.UnterrichteVon(ElementTyp.Klasse, klasse.Id)
            .Where(u => u.HauptfachId.HasValue && !(ix.Faecher.TryGetValue(u.HauptfachId.Value, out var f) && f.Bereitschaft))
            .GroupBy(u => u.HauptfachId!.Value).ToDictionary(g => g.Key, g => g.Max(u => u.Wochenstunden));
        if (vorschlag.Count == 0 && d.Faecher.FirstOrDefault(f => !f.Bereitschaft) is { } erstes) vorschlag[erstes.Id] = 0;
        foreach (var (fach, std) in vorschlag) d.Stundentafel.Add(new StundentafelZeile { Jahrgang = jahrgang, FachId = fach, Stunden = std });
    }

    // ------------------------------------------------------------------------------------------ Vertretungsstatistik

    public static AbrBericht Bericht(Schuldatei d, int bericht, DateOnly von, DateOnly bis, Lehrer? l)
    {
        if (bericht < 0 || bericht >= Berichte.Length) throw StudioFehler.Ungueltig("Unbekannter Bericht.");
        var zeilen = new List<AbrBerichtZeile>();
        var titel = $"{Berichte[bericht]}{(MitLehrer(bericht) && l != null ? $" – {l.Kurzname} {l.VollerName}" : "")} – {Format.Datum(von)} bis {Format.Datum(bis)}";
        AbrBericht Fertig(string[] kopf, int[] breiten, string summe) => new(bericht, titel, kopf, breiten, zeilen, summe, MitLehrer(bericht));
        switch (bericht)
        {
            case 0:
            {
                var liste = Statistik.Abrechnung(d, von, bis);
                foreach (var z in liste)
                    zeilen.Add(new(new[] { z.Lehrer.Kurzname, z.Lehrer.VollerName, z.Vertretungen.ToString(), z.Betreuungen.ToString(), z.Sondereinsaetze.ToString(), z.Aufsichten.ToString(),
                        z.Mehrarbeit.ToString(), z.Entfaelle.ToString(), z.Freisetzungen.ToString(), S(z.Saldo), z.Verguetungsfaehig.ToString() }, z.Lehrer.Id, z.Verguetungsfaehig != 0 ? "verguetung" : null));
                var s = Statistik.Zusammenfassung(d, von, bis);
                return Fertig(new[] { "Lehrer", "Name", "Vertretungen", "Betreuungen", "Sondereins.", "Pausenaufs.", "Mehrarbeit", "Entfall", "Freisetzung", "Zähler", "vergütungsfähig (NRW)" },
                    new[] { 60, 150, 80, 75, 75, 75, 70, 55, 70, 55, 130 },
                    $"Gesamt: {liste.Sum(z => z.Vertretungen)} Vertretungen, {liste.Sum(z => z.Mehrarbeit)} Mehrarbeitsstunden, davon {liste.Sum(z => z.Verguetungsfaehig)} vergütungsfähig. " +
                    $"Ausfall: {s.Gesamt} Einträge – vertreten {s.Vertreten}, Entfall {s.Entfall}, Freisetzung {s.Freisetzung}, Raum {s.Raum}, Verlegung {s.Verlegung}, offen {s.Offen}.\nDoppelklick auf einen Lehrer zeigt den Einzelnachweis.");
            }
            case 1 when l != null:
            {
                var z = Zaehlung.Lehrer(d, l, von, bis);
                foreach (var x in z.Zeilen.OrderBy(x => x.Datum).ThenBy(x => x.Stunde))
                    zeilen.Add(new(new[] { Tag(x.Datum), x.Stunde == 0 ? "" : x.Stunde.ToString(), x.Code, x.Art, x.Fach, x.Klassen, x.Grund, x.Zaehlt ? S(x.Wert) : "0", x.Bezahlt ? "b" : "", x.Mehrarbeit ? "x" : "", x.Text },
                        null, x.Code.StartsWith('-') || x.Code == "F" ? "minus" : null));
                return Fertig(new[] { "Datum", "Std.", "Code", "Art", "Fach", "Klasse(n)", "Grund", "Wert", "bez.", "MA", "Text" }, new[] { 95, 40, 45, 120, 55, 80, 90, 50, 40, 35, 200 },
                    $"{l.Kurzname} – {l.VollerName}: Zähler {S(z.Summe)} · Vertretungen {z.Vertretungen} (nicht gezählt {z.VertretungenNichtZuZaehlen}) · Entfälle {z.Entfaelle} (nicht gezählt {z.EntfaelleNichtZuZaehlen}) · " +
                    $"Freisetzungen {z.Freisetzungen} · Betreuungen {z.Betreuungen} · Sondereinsätze {z.Sondereinsaetze} · bezahlt {z.Bezahlt} · Mehrarbeit {z.Mehrarbeit}\n" +
                    "Codes: +V Vertretung, V Statt-Vertretung, B Betreuung, P Pausenaufsicht, +Y Sondereinsatz, +K Klausuraufsicht, VA Veranstaltung, -E Entfall, F Freisetzung, WK Wertkorrektur");
            }
            case 2 when l != null:
            {
                var monate = Statistik.MehrarbeitNrw(d, l, von, bis);
                foreach (var m in monate)
                    zeilen.Add(new(new[] { new DateTime(m.Jahr, m.Monat, 1).ToString("MMMM yyyy", Format.De), m.Geleistet.ToString(), m.Minder.ToString(), m.Saldo.ToString("+0;-0;0"), m.Grenze.ToString("0.#", Format.De), m.Verguetungsfaehig.ToString() },
                        null, m.Verguetungsfaehig != 0 ? "verguetung" : null));
                return Fertig(new[] { "Monat", "geleistete MA", "Minderleistung", "Saldo", "Grenze", "vergütungsfähig" }, new[] { 130, 100, 100, 70, 70, 110 },
                    $"{l.Kurzname}: Teilzeitquote {Statistik.Teilzeitquote(d, l).ToString("0.00", Format.De)} · Grenze {d.Einstellungen.Mehrarbeitsgrenze} Std. × Quote. Übersteigt der Saldo eines Kalendermonats die Grenze, sind alle Stunden vergütungsfähig.\n" +
                    $"Summe vergütungsfähig: {monate.Sum(m => m.Verguetungsfaehig)} Stunden.");
            }
            case 3 when l != null:
            {
                var tage = Statistik.Mehrarbeit(d, l, von.Year, von.Month);
                foreach (var t in tage)
                    zeilen.Add(new(new[] { Tag(t.Datum), t.Soll.ToString(), t.Ist.ToString(), t.Vertretungen.ToString(), t.Entfaelle.ToString(), (t.Ist - t.Soll).ToString("+0;-0;0") }, null, null));
                return Fertig(new[] { "Tag", "Soll", "Ist", "Vertretungen", "Entfälle", "Differenz" }, new[] { 110, 50, 50, 85, 70, 70 },
                    $"{l.Kurzname} – Monatsabrechnung {new DateTime(von.Year, von.Month, 1).ToString("MMMM yyyy", Format.De)}: Soll {tage.Sum(t => t.Soll)}, Ist {tage.Sum(t => t.Ist)}, Vertretungen {tage.Sum(t => t.Vertretungen)}, Entfälle {tage.Sum(t => t.Entfaelle)}.");
            }
            case 4:
            {
                var liste = Statistik.Entfalltage(d, von, bis);
                foreach (var e in liste.OrderBy(e => e.Lehrer.Kurzname).ThenBy(e => e.Datum))
                    zeilen.Add(new(new[] { e.Lehrer.Kurzname, e.Lehrer.VollerName, Tag(e.Datum), e.Stunden.ToString(), e.Grund }, e.Lehrer.Id, null));
                return Fertig(new[] { "Lehrer", "Name", "Datum", "Stunden", "Grund" }, new[] { 60, 160, 100, 60, 150 },
                    $"{liste.Count} Entfalltage (Tage, an denen wegen einer Absenz der gesamte Unterricht des Lehrers entfiel), {liste.Select(e => e.Lehrer.Id).Distinct().Count()} Lehrer.");
            }
            case 5:
            {
                var u = Statistik.VertretungsUebersicht(d, von, bis);
                string P(int x) => u.Geplant == 0 ? "" : $"{(100.0 * x / u.Geplant).ToString("0.0", Format.De)} %";
                void Z(string a, string b, string c, string? m = null) => zeilen.Add(new(new[] { a, b, c }, null, m));
                Z("Geplanter Unterricht", u.Geplant.ToString(), "100 %");
                Z("gemäß Plan erteilt", u.GemaessPlan.ToString(), P(u.GemaessPlan));
                Z("nicht gemäß Plan erteilt", u.NichtErteilt.ToString(), P(u.NichtErteilt));
                Z("Sondereinsätze", u.Sondereinsaetze.ToString(), "");
                Z("— nach Grund —", "", "", "gruppe");
                foreach (var (k, v) in u.NachGrund.OrderByDescending(x => x.Value)) Z("  " + k, v.ToString(), P(v));
                Z("— nach Art —", "", "", "gruppe");
                foreach (var (k, v) in u.NachArt.OrderByDescending(x => x.Value)) Z("  " + k, v.ToString(), P(v));
                Z("— ersatzlos entfallen, nach Grund —", "", "", "gruppe");
                foreach (var (k, v) in u.EntfallNachGrund.OrderByDescending(x => x.Value)) Z("  " + k, v.ToString(), P(v));
                return Fertig(new[] { "Kennzahl", "Stunden", "Anteil" }, new[] { 300, 80, 80 }, "Vertretungs-Übersicht der Schule (Bereitschaften nicht mitgezählt).");
            }
            case 6 when l != null:
            {
                var ww = Statistik.Wochenwerte(d, l).Where(w => w.Montag.AddDays(6) >= von && w.Montag <= bis).ToList();
                foreach (var w in ww)
                    zeilen.Add(new(new[] { $"KW {ISOWeek.GetWeekOfYear(w.Montag.ToDateTime(TimeOnly.MinValue))}", Format.Datum(w.Montag), w.Periode, w.Soll.ToString("0.##", Format.De), w.Unterricht.ToString(),
                        w.Vertretungen.ToString(), w.Entfaelle.ToString(), w.Korrektur.ToString("+0.##;-0.##;", Format.De), w.Ist.ToString("0.##", Format.De), S(w.Ist - w.Soll) }, null, null));
                return Fertig(new[] { "Woche", "Montag", "Periode", "Soll", "Unterricht", "Vertr.", "Entfall", "Korrektur", "Ist", "Differenz" }, new[] { 60, 85, 90, 50, 70, 50, 55, 70, 50, 70 },
                    $"{l.Kurzname}: Summe Differenz {S(ww.Sum(w => w.Ist - w.Soll))} Stunden in {ww.Count} Wochen.");
            }
        }
        return Fertig(new[] { "" }, new[] { 200 }, "Bitte einen Lehrer wählen.");
    }

    private static string Html(AbrBericht b, Schuldatei d, string benutzer)
    {
        static string H(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append($"<div class=\"pg\"><h1>{H(b.Titel)}</h1><div class=\"sub\">{H(d.Einstellungen.Schulname)} · erstellt am {DateTime.Now:dd.MM.yyyy HH:mm} von {H(benutzer)}</div><table><tr>");
        foreach (var k in b.Kopf) sb.Append($"<th>{H(k)}</th>");
        sb.Append("</tr>");
        foreach (var z in b.Zeilen)
        {
            sb.Append("<tr>");
            foreach (var x in z.Werte) sb.Append($"<td>{H(x)}</td>");
            sb.Append("</tr>");
        }
        sb.Append($"</table><p>{H(b.Summe).Replace("\n", "<br>")}</p>");
        if (b.Nr is 1 or 2 or 3)
            sb.Append("<p style=\"margin-top:40px\">_______________________________ &nbsp; &nbsp; _______________________________<br>Lehrkraft &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; Schulleitung</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static (DateOnly Von, DateOnly Bis) Zeitraum(DateOnly? von, DateOnly? bis)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var v = von ?? new DateOnly(heute.Year, heute.Month, 1);
        var b = bis ?? v.AddMonths(1).AddDays(-1);
        if (b < v) throw StudioFehler.Ungueltig("Das Ende liegt vor dem Anfang.");
        return (v, b);
    }

    private static Lehrer? LehrerVon(Schuldatei d, int? id) => id is int i ? d.Lehrer.FirstOrDefault(x => x.Id == i) ?? throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {i}") : null;

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/abrechnung");

        // ---- Wertrechnung: Lehrer Soll/Ist ----
        g.MapGet("/werte", (Arbeitsdokument a) => a.LesenAsync(Werte));
        g.MapPatch("/werte/{lehrer:int}", (Arbeitsdokument a, int lehrer, AbrWert w, ClaimsPrincipal p) =>
        {
            var wert = Dezimal(w.Wert);
            var feld = w.Feld.ToLowerInvariant();
            if (feld is not ("wochensoll" or "wertkorrektur")) throw StudioFehler.Ungueltig("Änderbar sind Wochensoll und Wertkorrektur.");
            return a.AendernAsync(p.Benutzername(), feld == "wochensoll" ? "Wochensoll" : "Wertkorrektur", AenderungsArt.Stammdaten, d =>
            {
                var l = d.Lehrer.FirstOrDefault(x => x.Id == lehrer) ?? throw StudioFehler.NichtGefunden($"Die Lehrkraft Nr. {lehrer}");
                if (feld == "wochensoll") l.Wochensoll = wert; else l.Wertkorrektur = wert;
            }, Werte);
        });

        // ---- Unterrichts-Matrix (0 Lehrer, 1 Stunden, 2 Lehrer + Stunden) ----
        g.MapGet("/matrix", (Arbeitsdokument a, int? art) => a.LesenAsync(doc => Matrix(doc, Math.Clamp(art ?? 2, 0, 2))));

        // ---- Stundentafel ----
        g.MapGet("/stundentafel", (Arbeitsdokument a) => a.LesenAsync(Stundentafel));
        g.MapPut("/stundentafel", (Arbeitsdokument a, AbrTafelWert w, ClaimsPrincipal p) =>
        {
            var std = Dezimal(w.Wert);
            if (std < 0) throw StudioFehler.Ungueltig("Die Stundenzahl darf nicht negativ sein.");
            return a.AendernAsync(p.Benutzername(), "Stundentafel", AenderungsArt.Einstellungen, d =>
            {
                if (d.Faecher.All(f => f.Id != w.Fach)) throw StudioFehler.NichtGefunden($"Das Fach Nr. {w.Fach}");
                d.Stundentafel.RemoveAll(z => z.Jahrgang == w.Jahrgang && z.FachId == w.Fach);
                if (std > 0) d.Stundentafel.Add(new StundentafelZeile { Jahrgang = w.Jahrgang, FachId = w.Fach, Stunden = std });
            }, Stundentafel);
        });
        g.MapPost("/stundentafel/jahrgang", (Arbeitsdokument a, AbrJahrgang j, ClaimsPrincipal p) =>
        {
            var jahrgang = (j.Jahrgang ?? "").Trim();
            if (jahrgang.Length == 0) throw StudioFehler.Ungueltig("Bitte einen Jahrgang angeben (z. B. 5, 6, EF, Q1).");
            return a.AendernAsync(p.Benutzername(), $"Stundentafel Jahrgang {jahrgang}", AenderungsArt.Einstellungen, d => JahrgangSicherstellen(d, jahrgang), Stundentafel);
        });
        g.MapPost("/stundentafel/nrw", (Arbeitsdokument a, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "NRW-Jahrgänge eintragen", AenderungsArt.Einstellungen, d =>
            {
                foreach (var j in d.Klassen.Select(k => k.Jahrgang).Where(j => j.Length > 0).Distinct().ToList()) JahrgangSicherstellen(d, j);
            }, Stundentafel));

        // ---- Vertretungsstatistik / Abrechnung NRW ----
        g.MapGet("/berichte", () => Berichte.Select((t, i) => new { nr = i, text = t, lehrer = MitLehrer(i) }));
        g.MapGet("/statistik", (Arbeitsdokument a, int? bericht, DateOnly? von, DateOnly? bis, int? lehrer) => a.LesenAsync(doc =>
        {
            var (v, b) = Zeitraum(von, bis);
            return Bericht(doc.Daten, bericht ?? 0, v, b, LehrerVon(doc.Daten, lehrer));
        }));
        g.MapGet("/statistik.html", (Arbeitsdokument a, int? bericht, DateOnly? von, DateOnly? bis, int? lehrer, bool? alle, ClaimsPrincipal p) => a.LesenAsync(doc =>
        {
            var (v, b) = Zeitraum(von, bis);
            var nr = bericht ?? 0;
            var sb = new StringBuilder();
            if (alle == true && MitLehrer(nr))
                foreach (var l in Statistik.Abrechnung(doc.Daten, v, b).Select(z => z.Lehrer)) sb.Append(Html(Bericht(doc.Daten, nr, v, b, l), doc.Daten, p.Benutzername()));
            else sb.Append(Html(Bericht(doc.Daten, nr, v, b, LehrerVon(doc.Daten, lehrer)), doc.Daten, p.Benutzername()));
            return Results.Content(Export.Seite(Berichte[Math.Clamp(nr, 0, Berichte.Length - 1)], sb.ToString()), "text/html; charset=utf-8");
        }));
        g.MapGet("/statistik.csv", (Arbeitsdokument a, int? bericht, DateOnly? von, DateOnly? bis, int? lehrer) => a.LesenAsync(doc =>
        {
            var (v, b) = Zeitraum(von, bis);
            var x = Bericht(doc.Daten, bericht ?? 0, v, b, LehrerVon(doc.Daten, lehrer));
            var liste = new List<string[]> { x.Kopf };
            liste.AddRange(x.Zeilen.Select(z => z.Werte));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv.Schreiben(liste, bom: false))).ToArray(), "text/csv; charset=utf-8",
                $"Statistik_{x.Nr}_{v:yyyyMMdd}-{b:yyyyMMdd}.csv");
        }));
        // Zählerstand einer Lehrkraft im Durchrechnungszeitraum (wie im Vertretungsvorschlag)
        g.MapGet("/zaehler/{lehrer:int}", (Arbeitsdokument a, int lehrer, DateOnly? datum) => a.LesenAsync(doc =>
        {
            var l = LehrerVon(doc.Daten, lehrer)!;
            var (v, b) = Zaehlung.Zeitraum(doc.Daten, datum ?? DateOnly.FromDateTime(DateTime.Today));
            var z = Zaehlung.Lehrer(doc.Daten, l, v, b);
            return new { lehrer = l.Kurzname, von = v, bis = b, summe = z.Summe, vertretungen = z.Vertretungen, entfaelle = z.Entfaelle, mehrarbeit = z.Mehrarbeit, freisetzungen = z.Freisetzungen };
        }));
    }
}
