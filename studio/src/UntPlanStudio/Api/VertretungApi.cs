using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;
using BP = UntPlan.Core.Module.Bereitschaftsplanung;

namespace UntPlanStudio.Api;

// ---- Anfragen ----------------------------------------------------------------------------------
public sealed record IdsAnfrage(List<int> Ids, Dictionary<string, int>? Antworten = null);
public sealed record ZellAnfrage(List<int> Ids, string Spalte, string? Wert, Dictionary<string, int>? Antworten = null);
public sealed record ZeitraumAnfrage(DateOnly Von, DateOnly? Bis);
public sealed record KennzeichenAnfrage(List<int> Ids, string Art, bool Wert);
public sealed record TeilenAnfrage(int Id, string? Klassen);
public sealed record AutomatikAnfrage(DateOnly Von, DateOnly? Bis, string Methode);
public sealed record VaLehrerAnfrage(int Id, string? Wert, Dictionary<string, int>? Antworten = null);
public sealed record UebernehmenAnfrage(int Lehrer, List<int>? Ids, bool Block = false, bool Dauer = false);
public sealed record RaumAnfrage(int? Raum, bool Zusatz, List<int>? Ids, bool Block = false, Dictionary<string, int>? Antworten = null);
public sealed record VorziehenAnfrage(DateOnly Datum, int Stunde, int? Unterricht);
public sealed record AbsenzEingabe(int Id, string? Element, DateOnly Von, DateOnly? Bis, int? Sv, int? Sb, string? Grund, string? Text, string? Begleit);
public sealed record AbsenzSpeichern(string Art, List<AbsenzEingabe> Neu, List<int>? Alt, string? Text = null);
public sealed record AbsenzNachfrage(string Art, List<int>? Ids, int? Va, int? Lehrer, List<DateOnly>? Tage, int? Absenz);
public sealed record AbsenzTeilen(int Id, DateOnly Ab);
public sealed record AbsenzKopieren(int Id, string Elemente);
public sealed record KalendertagAnfrage(DateOnly Datum, string Art, string? Grund, string? Stunden, string? Notiz);
public sealed record TagestextEingabe(DateOnly? Start, DateOnly? Bis, string Text, bool Portal = true, string? Art = null, string? Elemente = null);
public sealed record TagestexteAnfrage(DateOnly Datum, List<TagestextEingabe> Texte);
public sealed record SondereinsatzZeile(DateOnly Datum, int Sv, int? Sb, string? Lehrer, string? Klassen, string? Fach, string? Raum, string? Gruppe, string? Text);
public sealed record SondereinsatzAnfrage(List<SondereinsatzZeile> Zeilen);
public sealed record VormerkungEingabe(string? Fach, string? Lehrer, string? Klassen, string? Raum, int Stunden = 1, string? Text = null);
public sealed record VormerkungVerplanen(DateOnly Datum, int Stunde);
public sealed record PlanPosition(DateOnly Datum, int Stunde, int Unterricht, int? Zeile, bool Block = false, string? Text = null, Dictionary<string, int>? Antworten = null);
public sealed record PlanVerlegen(DateOnly Von, int VonStunde, int Unterricht, int? Zeile, int Laenge, DateOnly Ziel, int ZielStunde, Dictionary<string, int>? Antworten = null);
public sealed record BereitschaftAnfrage(int Tag, int Stunde, int? Lehrer, int? Anzahl);

/// <summary>
/// Vertretungsplanung (Vertretungsmodus) wie in der Desktop-Anwendung: Kalender und Tagestexte, Absenzen, Vertretungsliste mit allen
/// Bearbeitungen (Vertreter, Entfall, „+“, Raum, Text, teilen, fixieren …), Vertretungsvorschlag (Kandidaten, Betreuungen, Räume,
/// Vorziehungen), Automatik, Sondereinsätze, Bereitschaften und Bereitschaftsplanung, Vormerkungen, tagesaktuelle Stundenpläne mit
/// Verlegen/Tauschen per Drag &amp; Drop sowie die Einstellungen der Vertretungsplanung.
/// Rückfragen wie im Desktop („Trotzdem verplanen?“) liefert der Server als <c>{ frage: { schluessel, text, optionen } }</c>; die
/// Oberfläche wiederholt die Anfrage mit <c>antworten: { schluessel: index }</c>.
/// </summary>
public sealed class VertretungApi : IStudioModul
{
    public string Name => "vertretung";

    private const AenderungsArt V = AenderungsArt.Vertretung;

    private static IResult Frage(string schluessel, string text, params string[] optionen) =>
        Results.Ok(new { frage = new { schluessel, text, optionen = optionen.Length == 0 ? new[] { "Ja", "Nein" } : optionen } });

    private static int? Antwort(Dictionary<string, int>? a, string s) => a != null && a.TryGetValue(s, out var w) ? w : null;

    private static string B(ClaimsPrincipal p) => p.Benutzername();

    private static int? ElementId(Schuldatei d, ElementTyp typ, string? kurz)
    {
        if (string.IsNullOrWhiteSpace(kurz)) return null;
        var k = kurz.Trim();
        return (d.AlleElemente(typ).FirstOrDefault(e => e.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))
                ?? (typ == ElementTyp.Lehrer ? d.Lehrer.FirstOrDefault(l => l.VollerName.Equals(k, StringComparison.OrdinalIgnoreCase)) : null))?.Id
               ?? throw StudioFehler.Ungueltig($"{Typnamen.Name(typ)} \"{k}\" ist nicht vorhanden.");
    }

    private static List<int> ElementIds(Schuldatei d, ElementTyp typ, string? text) =>
        (text ?? "").Split(new[] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(k => ElementId(d, typ, k)!.Value).Distinct().ToList();

    private static AbwesenheitsArt ArtVon(string? s) => (s ?? "").ToLowerInvariant() switch
    {
        "klasse" or "klassen" => AbwesenheitsArt.Klasse,
        "raum" or "raeume" => AbwesenheitsArt.Raum,
        _ => AbwesenheitsArt.Lehrer,
    };

    private static ElementTyp TypVon(AbwesenheitsArt a) => a switch { AbwesenheitsArt.Lehrer => ElementTyp.Lehrer, AbwesenheitsArt.Klasse => ElementTyp.Klasse, _ => ElementTyp.Raum };

    /// <summary>Markierte Vertretungen ohne fixierte Zeilen und ohne Zeilen einer Veranstaltung (für Entfall, +, Vertreter …).</summary>
    private static List<Vertretung> Bearbeitbare(Schuldatei d, IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        return d.Vertretungen.Where(v => set.Contains(v.Id) && !v.Fixiert && v.Art != VertretungsArt.Veranstaltung).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
    }

    private static int Fixierte(Schuldatei d, IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        return d.Vertretungen.Count(v => set.Contains(v.Id) && v.Fixiert);
    }

    /// <summary>Änderung auf mehrere Vertretungen (per Id) anwenden – eine Rückgängig-Stufe, Kennzeichen „neu“.</summary>
    private static Task<object> Ausfuehren(Arbeitsdokument a, ClaimsPrincipal p, string text, IReadOnlyCollection<int> ids, Action<Schuldatei, Vertretung> aktion)
    {
        if (ids.Count == 0) return Task.FromResult<object>(new { ok = true, anzahl = 0 });
        var set = ids.ToHashSet();
        return a.AendernAsync(B(p), ids.Count > 1 ? $"{text} ({ids.Count} Stunden)" : text, V, d =>
        {
            foreach (var v in d.Vertretungen.Where(x => set.Contains(x.Id)).OrderBy(x => x.Datum).ThenBy(x => x.Stunde).ToList())
            {
                aktion(d, v);
                Vertretungen.AlsGeaendertMarkieren(v, B(p));
            }
        }, _ => (object)new { ok = true, anzahl = ids.Count });
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/vertretung");
        KalenderRegistrieren(g);
        AbsenzenRegistrieren(g);
        ListeRegistrieren(g);
        VorschlagRegistrieren(g);
        EreignisseRegistrieren(g);
        PlanRegistrieren(g);
        EinstellungenRegistrieren(g);
    }

    // =============================================================================================
    // Kalender, Datum, Tagestexte

    private static void KalenderRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/start", (Arbeitsdokument a) => a.LesenAsync(doc => new { datum = VertretungPlan.Start(doc.Daten) }));

        g.MapGet("/blaettern", (Arbeitsdokument a, DateOnly datum, int? richtung, bool? woche) =>
            a.LesenAsync(doc => new { datum = VertretungPlan.Blaettern(doc.Daten, datum, richtung ?? 1, woche == true) }));

        g.MapGet("/kalender", (Arbeitsdokument a, DateOnly datum) => a.LesenAsync(doc => VertretungPlan.Kalenderdaten(doc, datum)));

        g.MapPost("/kalendertag", (Arbeitsdokument a, KalendertagAnfrage k, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Kalendertag", V, d =>
            {
                var frei = k.Art == "frei";
                var feiertag = k.Art == "feiertag";
                var stunden = k.Art == "stunden" ? (k.Stunden ?? "").Trim() : "";
                var notiz = (k.Notiz ?? "").Trim();
                int? grund = frei && !string.IsNullOrWhiteSpace(k.Grund) ? ElementId(d, ElementTyp.Abwesenheitsgrund, k.Grund) : null;
                d.Kalender.RemoveAll(x => x.Datum == k.Datum);
                if (frei || feiertag || stunden.Length > 0 || notiz.Length > 0)
                    d.Kalender.Add(new Kalendertag { Datum = k.Datum, Unterrichtsfrei = frei, Feiertag = feiertag, FreieStunden = stunden, Notiz = notiz, GrundId = grund });
                Kalender.CacheLeeren();
                Vertretungen.Aktualisieren(d, k.Datum);
            }, doc => VertretungPlan.Kalenderdaten(doc, k.Datum)));

        g.MapPut("/tagestexte", (Arbeitsdokument a, TagestexteAnfrage t, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Tagestexte", V, d =>
            {
                var alt = d.Tagestexte.Where(x => x.GiltAm(t.Datum)).ToList();
                var neu = new List<Tagestext>();
                foreach (var e in t.Texte.Where(e => !string.IsNullOrWhiteSpace(e.Text)))
                {
                    var art = VertretungPlan.TagestextArtVon(e.Art);
                    var typ = art == TagestextArt.Klassen ? ElementTyp.Klasse : ElementTyp.Lehrer;
                    var start = e.Start ?? t.Datum;
                    var bis = e.Bis is DateOnly b && b >= start ? b : start;
                    // Formatierung bleibt erhalten, solange der Text nicht geändert wurde
                    var vorher = alt.FirstOrDefault(x => x.Datum == start && x.Text == e.Text);
                    neu.Add(new Tagestext
                    {
                        Datum = start, Art = art, Text = e.Text, TextRtf = vorher?.TextRtf ?? "", TextHtml = vorher?.TextHtml ?? "",
                        ElementIds = ElementIds(d, typ, e.Elemente), GueltigTage = bis.DayNumber - start.DayNumber + 1, Portal = e.Portal,
                    });
                }
                d.Tagestexte.RemoveAll(x => x.GiltAm(t.Datum));
                d.Tagestexte.AddRange(neu);
            }, doc => VertretungPlan.Kalenderdaten(doc, t.Datum)));

        // Tageswechsel: offene Vertretungen aus Absenzen, Veranstaltungen und Klausuren erzeugen (nur wenn sich etwas ändert, wie im Desktop)
        g.MapPost("/aktualisieren", (Arbeitsdokument a, ZeitraumAnfrage z, ClaimsPrincipal p) =>
            a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var von = z.Von;
                var bis = z.Bis is DateOnly b && b >= von ? b : von;
                if (bis.DayNumber - von.DayNumber > 31) return (new { geaendert = false }, null, "");
                var probe = Dokument.VertretungsProbe(doc.Daten, von, bis);
                Vertretungen.Aktualisieren(probe, von, bis);
                if (Signatur(probe, von, bis) == Signatur(doc.Daten, von, bis)) return (new { geaendert = false }, null, "");
                var text = $"Vertretungen {Format.Datum(von)}{(von == bis ? "" : " – " + Format.Datum(bis))} aktualisiert";
                doc.Aendern(text, V, d => Vertretungen.Aktualisieren(d, von, bis));
                return (new { geaendert = true }, V, text);
            }));
    }

    private static string Signatur(Schuldatei d, DateOnly von, DateOnly bis) =>
        string.Join(";", d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).Select(v => $"{v.UnterrichtId}/{v.ZeilenIndex}/{v.Datum}/{v.Stunde}/{v.Art}/{v.VertreterId}/{v.GangId}").OrderBy(s => s, StringComparer.Ordinal));

    // =============================================================================================
    // Absenzen

    private static object AbsenzZeile(Schuldatei d, DatenIndex ix, Abwesenheit x, int letzte) => new
    {
        id = $"A{x.Id}", nr = x.Id, element = ix.K(TypVon(x.Art), x.ElementId), elementId = x.ElementId, von = x.Von, bis = x.Bis, sv = x.StundeVon,
        sb = Math.Min(x.StundeBis, letzte), grund = ix.K(ElementTyp.Abwesenheitsgrund, x.GrundId), text = x.Text,
        begleit = string.Join(", ", x.BegleitLehrerIds.Select(ix.KLehrer)),
        offen = d.Vertretungen.Count(v => v.AbwesenheitId == x.Id && v.OffenOderRaumOffen),
    };

    private static void AbsenzenRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/absenzen", (Arbeitsdokument a, string? art, DateOnly von, DateOnly? bis, bool? veranstaltungen) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var b = bis is DateOnly bb && bb >= von ? bb : von;
            var ar = ArtVon(art);
            var typ = TypVon(ar);
            var letzte = Math.Max(1, d.Zeitraster.StundenProTag);
            var vergleich = StringComparer.Create(Format.De, true);
            var zeilen = d.Abwesenheiten.Where(x => x.Art == ar && x.Bis >= von && x.Von <= b).OrderBy(x => ix.K(typ, x.ElementId), vergleich).ThenBy(x => x.Von)
                .Select(x => AbsenzZeile(d, ix, x, letzte)).ToList();
            var va = veranstaltungen == true
                ? d.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= b).OrderBy(v => v.Von).Select(v => new
                {
                    id = $"VA{v.Id}", nr = v.Id, von = v.Von, bis = v.Bis, sv = v.StundeVon, sb = v.StundeBis >= 99 ? letzte : v.StundeBis,
                    grund = ix.K(ElementTyp.Abwesenheitsgrund, v.GrundId),
                    elemente = ar switch
                    {
                        AbwesenheitsArt.Lehrer => VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Lehrer, v.LehrerIds, v.Von, v.Bis),
                        AbwesenheitsArt.Klasse => VeranstaltungsLogik.KlassenKurztext(d, v),
                        _ => VeranstaltungsAuswahl.Kurztext(d, ElementTyp.Raum, v.RaumIds, v.Von, v.Bis),
                    },
                    text = v.Text,
                }).Where(x => x.elemente.Length > 0).ToList<object>()
                : new List<object>();
            return new
            {
                art = ar.ToString(), letzteStunde = letzte,
                anzahl = new
                {
                    lehrer = d.Abwesenheiten.Count(x => x.Art == AbwesenheitsArt.Lehrer && x.Bis >= von && x.Von <= b),
                    klasse = d.Abwesenheiten.Count(x => x.Art == AbwesenheitsArt.Klasse && x.Bis >= von && x.Von <= b),
                    raum = d.Abwesenheiten.Count(x => x.Art == AbwesenheitsArt.Raum && x.Bis >= von && x.Von <= b),
                },
                zeilen, veranstaltungen = va,
            };
        }));

        g.MapPost("/absenzen/speichern", async (Arbeitsdokument a, AbsenzSpeichern s, ClaimsPrincipal p) =>
        {
            var ar = ArtVon(s.Art);
            var altIds = (s.Alt ?? new()).ToHashSet();
            List<Abwesenheit> neu = new();
            List<string> betroffen = new();
            await a.LesenAsync(doc =>
            {
                var d = doc.Daten;
                foreach (var e in s.Neu)
                {
                    var alt = d.Abwesenheiten.FirstOrDefault(x => x.Id == e.Id);
                    var x = new Abwesenheit
                    {
                        Id = e.Id, Art = ar, ElementId = ElementId(d, TypVon(ar), e.Element) ?? throw StudioFehler.Ungueltig("Für eine Absenz muss ein Element gewählt sein."),
                        Von = e.Von, Bis = e.Bis ?? e.Von, StundeVon = e.Sv ?? 1, StundeBis = e.Sb ?? 99,
                        GrundId = string.IsNullOrWhiteSpace(e.Grund) ? null : ElementId(d, ElementTyp.Abwesenheitsgrund, e.Grund),
                        Text = e.Text ?? "", BegleitLehrerIds = ElementIds(d, ElementTyp.Lehrer, e.Begleit),
                        Erstellt = alt?.Erstellt ?? DateTime.Now, Benutzer = alt?.Benutzer is { Length: > 0 } bn ? bn : B(p),
                    };
                    neu.Add(x);
                }
                var ix = doc.Index;
                betroffen = neu.SelectMany(x => Vertretungen.BetroffeneVertreter(d, x)).Distinct().Take(20)
                    .Select(v => $"Vtr-Nr. {v.Id}: {VertretungPlan.TagKurz(v.Datum)} {Format.Datum(v.Datum)}, {v.Stunde}. Std., {ix.KKlassen(v.NeueKlassenIds)} {ix.KFach(v.FachId)} – Vertreter {ix.KLehrer(v.VertreterId)}").ToList();
                return true;
            });
            var text = s.Text ?? (altIds.Count == 0 ? "Absenz eingeben" : neu.Count == 0 ? "Absenz löschen" : "Absenz geändert");
            return await a.AendernAsync(B(p), text, V, d =>
            {
                var alt = d.Abwesenheiten.Where(x => altIds.Contains(x.Id)).ToList();
                var gespeichert = AbsenzVerwaltung.Speichern(d, neu, alt);
                neu = gespeichert;
            }, doc =>
            {
                var d = doc.Daten;
                var nachfragen = new List<object>();
                foreach (var x in neu.Where(x => x.Art == AbwesenheitsArt.Lehrer))
                {
                    foreach (var u in VeranstaltungsLogik.Ueberlappungen(d, x))
                        nachfragen.Add(new
                        {
                            art = "veranstaltung", text = VeranstaltungsLogik.FrageText(d, x, u), hinweis = VeranstaltungsLogik.HinweisText(d, u),
                            va = u.Veranstaltung.Id, lehrer = u.LehrerId, tage = u.Tage, absenz = x.Id,
                        });
                    var name = doc.Index.KLehrer(x.ElementId);
                    var verl = Verlegungsaufhebung.VerlegungenImZeitraum(d, x);
                    if (verl.Count > 0)
                        nachfragen.Add(new { art = "verlegung", text = Verlegungsaufhebung.FrageText(d, $"{name} hat im Absenzzeitraum verlegte Stunden:", verl, "Soll die Verlegung rückgängig gemacht werden?"), ids = verl.Select(v => v.Id).ToList() });
                    var se = Verlegungsaufhebung.SondereinsaetzeImZeitraum(d, x);
                    if (se.Count > 0)
                        nachfragen.Add(new { art = "sondereinsatz", text = Verlegungsaufhebung.FrageText(d, $"{name} hat im Absenzzeitraum Sondereinsätze:", se, "Sollen die Sondereinsätze gelöscht werden?"), ids = se.Select(v => v.Id).ToList() });
                }
                return new { gespeichert = neu.Select(x => x.Id).ToList(), doppelt = s.Neu.Count > 0 && neu.Count == 0 && altIds.Count == 0, betroffen, nachfragen };
            });
        });

        g.MapPost("/absenzen/nachfrage", (Arbeitsdokument a, AbsenzNachfrage n, ClaimsPrincipal p) => n.Art switch
        {
            "veranstaltung" => a.AendernAsync(B(p), "Lehrer aus Veranstaltung entfernt", V,
                d => VeranstaltungsLogik.LehrerEntfernen(d, n.Va ?? 0, n.Lehrer ?? 0, n.Tage ?? new(), n.Absenz), _ => (object)new { ok = true }),
            "verlegung" => a.AendernAsync(B(p), (n.Ids?.Count ?? 0) > 1 ? $"Verlegungen aufgehoben ({n.Ids!.Count})" : "Verlegung aufgehoben", V,
                d => Verlegungsaufhebung.AlleAufheben(d, n.Ids ?? new()), _ => (object)new { ok = true }),
            "sondereinsatz" => a.AendernAsync(B(p), (n.Ids?.Count ?? 0) > 1 ? $"Sondereinsätze gelöscht ({n.Ids!.Count})" : "Sondereinsatz gelöscht", V,
                d => Verlegungsaufhebung.SondereinsaetzeLoeschen(d, n.Ids ?? new()), _ => (object)new { ok = true }),
            _ => throw StudioFehler.Ungueltig("Unbekannte Nachfrage."),
        });

        g.MapPost("/absenzen/loeschen", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), i.Ids.Count > 1 ? $"Absenzen löschen ({i.Ids.Count})" : "Absenz löschen", V, d =>
                AbsenzVerwaltung.Loeschen(d, d.Abwesenheiten.Where(x => i.Ids.Contains(x.Id)).ToList()), _ => new { ok = true }));

        g.MapPost("/absenzen/teilen", (Arbeitsdokument a, AbsenzTeilen t, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Absenz teilen", V, d =>
            {
                var alt = d.Abwesenheiten.FirstOrDefault(x => x.Id == t.Id) ?? throw StudioFehler.NichtGefunden($"Die Absenz Nr. {t.Id}");
                if (alt.Von == alt.Bis) throw StudioFehler.Ungueltig("Eine eintägige Absenz kann nicht geteilt werden.");
                if (t.Ab <= alt.Von || t.Ab > alt.Bis) throw StudioFehler.Ungueltig("Der zweite Teil muss nach dem ersten Tag und spätestens am letzten Tag der Absenz beginnen.");
                var a1 = Kopie(alt);
                a1.Bis = t.Ab.AddDays(-1);
                a1.StundeBis = 99;
                var b1 = Kopie(alt);
                b1.Id = 0;
                b1.Von = t.Ab;
                b1.StundeVon = 1;
                AbsenzVerwaltung.Speichern(d, new[] { a1, b1 }, new[] { alt });
            }, _ => new { ok = true }));

        g.MapPost("/absenzen/kopieren", (Arbeitsdokument a, AbsenzKopieren k, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Absenz kopieren", V, d =>
            {
                var alt = d.Abwesenheiten.FirstOrDefault(x => x.Id == k.Id) ?? throw StudioFehler.NichtGefunden($"Die Absenz Nr. {k.Id}");
                var neu = ElementIds(d, TypVon(alt.Art), k.Elemente).Where(e => e != alt.ElementId)
                    .Select(e => { var x = Kopie(alt); x.Id = 0; x.ElementId = e; x.Erstellt = DateTime.Now; return x; }).ToList();
                AbsenzVerwaltung.Speichern(d, neu, Array.Empty<Abwesenheit>());
            }, _ => new { ok = true }));

        g.MapPost("/absenzen/zusammenfassen", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Absenzen zusammenfassen", V, d =>
            {
                var m = d.Abwesenheiten.Where(x => i.Ids.Contains(x.Id)).OrderBy(x => x.Von).ToList();
                if (m.Count < 2 || m.Select(x => (x.Art, x.ElementId)).Distinct().Count() > 1) throw StudioFehler.Ungueltig("Bitte mindestens zwei Absenzen desselben Elements markieren.");
                var x = Kopie(m[0]);
                x.Bis = m.Max(y => y.Bis);
                x.StundeBis = m.First(y => y.Bis == x.Bis).StundeBis;
                AbsenzVerwaltung.Speichern(d, new[] { x }, m);
            }, _ => new { ok = true }));
    }

    private static Abwesenheit Kopie(Abwesenheit a) => new()
    {
        Id = a.Id, Art = a.Art, ElementId = a.ElementId, Von = a.Von, Bis = a.Bis, StundeVon = a.StundeVon, StundeBis = a.StundeBis,
        GrundId = a.GrundId, Text = a.Text, BegleitLehrerIds = new List<int>(a.BegleitLehrerIds), Erstellt = a.Erstellt, Benutzer = a.Benutzer,
    };

    // =============================================================================================
    // Vertretungsliste und Bearbeitung

    private static void ListeRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/liste", (Arbeitsdokument a, [AsParameters] ListeAnfrage l) => a.LesenAsync(doc => new VertretungListe(doc, l).Berechnen()));

        // Auswahlliste mit Status (Vertreter: belegt/abwesend rot, Bereitschaft/freigesetzt grün; Raum: gesperrt/belegt rot, frei grün)
        g.MapGet("/optionen", (Arbeitsdokument a, int id, string spalte) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
            var tp = Tagesplan.Berechnen(d, v.Datum);
            if (spalte == "NRaum")
                return Stammdaten.Sortiert(d.Raeume).Where(r => !r.Ignorieren).Select(r =>
                {
                    var gesperrt = Absenzen.RaumGesperrt(d, r.Id, v.Datum, v.Stunde);
                    var belegt = tp.Stunde(v.Stunde).Any(x => x.Findetstatt && x.RaumIds.Contains(r.Id) && x.Vertretung != v);
                    var lang = r.Langname.Length > 0 && r.Langname != r.Kurzname ? r.Langname : "";
                    return new { k = r.Kurzname, l = gesperrt ? (lang + " – gesperrt").Trim(' ', '–') : belegt ? (lang + " – belegt").Trim(' ', '–') : lang, c = gesperrt ? "rot" : belegt ? "rosa" : "gruen" };
                }).Prepend(new { k = "???", l = "offen (kein Raum)", c = "" }).ToList();
            if (spalte == "NFach")
                return Stammdaten.Sortiert(d.Faecher).Where(f => !f.Ignorieren).Select(f => new { k = f.Kurzname, l = f.Langname, c = "" }).ToList();
            return Stammdaten.Sortiert(d.Lehrer).Where(l => !l.Ignorieren).Select(l =>
            {
                var belegt = Vertretungen.LehrerBelegt(d, tp, l.Id, v.Stunde, v);
                var gruen = belegt is null && (Vertretungen.HatBereitschaft(d, tp, l.Id, v.Stunde) || Vertretungen.IstFreigesetzt(d, tp, l.Id, v.Stunde));
                return new { k = l.Kurzname, l = belegt is null ? l.VollerName : $"{l.VollerName} – {belegt}", c = belegt != null ? "rot" : gruen ? "gruen" : "" };
            }).Prepend(new { k = "---", l = "Entfall", c = "" }).Prepend(new { k = "+++", l = Vertretungen.OhneLehrerText, c = "" }).ToList();
        }));

        // Eingabe in einer Spalte (Vertreter, Fach, Raum, Text, Statistik …) – auf die bearbeitete bzw. alle markierten Zeilen
        g.MapPost("/aendern", async (Arbeitsdokument a, ZellAnfrage z, ClaimsPrincipal p) =>
        {
            var wert = (z.Wert ?? "").Trim();
            if (z.Ids.Count == 0) return Results.Ok(new { ok = true });
            switch (z.Spalte)
            {
                case "Vertreter":
                {
                    var erste = await a.LesenAsync(doc => doc.Daten.Vertretungen.FirstOrDefault(x => x.Id == z.Ids[0])?.Art);
                    if (erste == VertretungsArt.Veranstaltung) return await VeranstaltungsVertreter(a, p, z.Ids[0], wert, z.Antworten);
                    if (wert is "+" or "+++") return Results.Ok(await OhneLehrer(a, p, z.Ids));
                    if (wert is "-" or "---") return await Entfall(a, p, z.Ids, z.Antworten);
                    if (wert is "" or "???")
                    {
                        var offen = await a.LesenAsync(doc => Bearbeitbare(doc.Daten, z.Ids).Select(x => x.Id).ToList());
                        return Results.Ok(await Ausfuehren(a, p, "Vertreter entfernt", offen, (d, x) => Vertretungen.VertreterSetzen(d, x, null, false, B(p))));
                    }
                    var lehrer = await a.LesenAsync(doc => ElementId(doc.Daten, ElementTyp.Lehrer, wert)!.Value);
                    return await VertreterSetzen(a, p, z.Ids, lehrer, false, z.Antworten);
                }
                case "NFach":
                    return Results.Ok(await Ausfuehren(a, p, "Vertretungsfach", Erste(z.Ids), (d, x) => { x.VertretungsfachId = wert.Length == 0 ? null : ElementId(d, ElementTyp.Fach, wert); x.Bearbeitet = true; }));
                case "NRaum":
                    if (wert == "???") wert = "";
                    return Results.Ok(await Ausfuehren(a, p, "Vertretungsraum", Erste(z.Ids), (d, x) =>
                    {
                        x.VertretungsraumId = wert.Length == 0 ? null : ElementId(d, ElementTyp.Raum, wert);
                        x.ZusatzraumIds.Clear();
                        if (x.Art == VertretungsArt.Vertretung && x.VertreterId == x.LehrerId) x.Art = VertretungsArt.Raumvertretung;
                        x.Bearbeitet = true;
                    }));
                case "Kz": return Results.Ok(await Ausfuehren(a, p, "Statistik-Kennzeichen", Erste(z.Ids), (_, x) => x.Statistik = wert));
                case "Text": return Results.Ok(await Ausfuehren(a, p, "Vertretungstext", z.Ids, (_, x) => { x.Text = wert; x.Bearbeitet = true; }));
                case "Text2": return Results.Ok(await Ausfuehren(a, p, "Vertretungstext 2", z.Ids, (_, x) => { x.Text2 = wert; x.Bearbeitet = true; }));
                case "Beschr": return Results.Ok(await Ausfuehren(a, p, "Beschreibung", z.Ids, (_, x) => x.Beschreibung = wert));
                case "SG": return Results.Ok(await Ausfuehren(a, p, "Schülergruppe", Erste(z.Ids), (_, x) => x.Schuelergruppe = wert));
                case "Kopp":
                    if (!int.TryParse(wert.Length == 0 ? "0" : wert, out var kopp) || kopp < 0) throw StudioFehler.Ungueltig("Vertretungs-Kopplung: bitte eine Zahl eingeben.");
                    return Results.Ok(await Ausfuehren(a, p, "Vertretungs-Kopplung", Erste(z.Ids), (_, x) => x.Kopplung = kopp));
                case "Wert":
                    decimal? w = null;
                    if (wert.Length > 0)
                    {
                        if (decimal.TryParse(wert.Replace('.', ','), NumberStyles.Number, Format.De, out var wd)) w = wd;
                        else throw StudioFehler.Ungueltig("Wert: bitte eine Zahl eingeben.");
                    }
                    return Results.Ok(await Ausfuehren(a, p, "Wert", Erste(z.Ids), (_, x) => x.Wert = w));
                default:
                    throw StudioFehler.Ungueltig($"Die Spalte \"{z.Spalte}\" kann nicht bearbeitet werden.");
            }
        });

        g.MapPost("/entfall", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) => Entfall(a, p, i.Ids, i.Antworten));
        g.MapPost("/ohnelehrer", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) => OhneLehrer(a, p, i.Ids));
        g.MapPost("/veranstaltungslehrer", (Arbeitsdokument a, VaLehrerAnfrage v, ClaimsPrincipal p) => VeranstaltungsVertreter(a, p, v.Id, (v.Wert ?? "").Trim(), v.Antworten));

        g.MapPost("/teilen", (Arbeitsdokument a, TeilenAnfrage t, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vertretung teilen", V, d =>
            {
                var x = d.Vertretungen.FirstOrDefault(v => v.Id == t.Id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {t.Id}");
                var ids = ElementIds(d, ElementTyp.Klasse, t.Klassen).Where(x.NeueKlassenIds.Contains).ToList();
                var n = Vertretungen.Teilen(d, x, B(p));
                if (ids.Count > 0 && ids.Count < x.NeueKlassenIds.Count)
                {
                    n.VertretungsKlassenIds = ids;
                    x.VertretungsKlassenIds = x.NeueKlassenIds.Except(ids).ToList();
                }
                Vertretungen.AlsGeaendertMarkieren(x, B(p));
            }, _ => new { ok = true }));

        g.MapPost("/vormerken", async (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
        {
            var ids = await a.LesenAsync(doc => doc.Daten.Vertretungen.Where(v => i.Ids.Contains(v.Id) && v.UnterrichtId.HasValue && v.Art != VertretungsArt.Vormerkung).Select(v => v.Id).ToList());
            return await Ausfuehren(a, p, "Als Vormerkung beiseitelegen", ids, (d, v) => Vertretungen.AlsVormerkung(d, v, B(p)));
        });

        g.MapPost("/zuruecksetzen", async (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
        {
            var ids = await a.LesenAsync(doc => Bearbeitbare(doc.Daten, i.Ids).Select(v => v.Id).ToHashSet());
            if (ids.Count == 0) return (object)new { ok = true, anzahl = 0 };
            return await a.AendernAsync(B(p), "Vertretung zurückgesetzt", V, d =>
            {
                var tage = new HashSet<DateOnly>();
                foreach (var v in d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList())
                {
                    tage.Add(v.Datum);
                    if (Vertretungen.IstAbgeleitet(v))
                    {
                        v.Bearbeitet = false;
                        v.VertreterId = null;
                        v.VertretungsraumId = null;
                        v.VertretungsfachId = null;
                        v.Text = "";
                        v.Mehrarbeit = false;
                        Vertretungen.AlsGeaendertMarkieren(v, B(p));
                    }
                    else d.Vertretungen.RemoveAll(x => x == v || (v.VerlegungsId.HasValue && x.VerlegungsId == v.VerlegungsId));
                }
                foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
            }, _ => (object)new { ok = true, anzahl = ids.Count });
        });

        g.MapPost("/loeschen", async (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
        {
            var (ids, abgeleitet) = await a.LesenAsync(doc =>
            {
                var alle = doc.Daten.Vertretungen.Where(v => i.Ids.Contains(v.Id)).ToList();
                return (alle.Where(v => !Vertretungen.IstAbgeleitet(v)).Select(v => v.Id).ToHashSet(), alle.Count(Vertretungen.IstAbgeleitet));
            });
            if (ids.Count == 0)
                return (object)new
                {
                    ok = false, anzahl = 0,
                    hinweis = abgeleitet > 0 ? "Vertretungen, die aus einer Absenz oder Veranstaltung entstehen, können nicht gelöscht werden.\n\nMit „Entfall“ entfällt die Stunde, mit „Zurücksetzen“ wird sie wieder offen. Soll sie ganz verschwinden, die Absenz ändern." : null,
                };
            return await a.AendernAsync(B(p), "Vertretungen löschen", V, d =>
            {
                var weg = d.Vertretungen.Where(x => ids.Contains(x.Id)).ToList();
                var verlegungen = weg.Where(x => x.VerlegungsId.HasValue).Select(x => x.VerlegungsId).ToHashSet();
                d.Vertretungen.RemoveAll(x => ids.Contains(x.Id) || (x.VerlegungsId.HasValue && verlegungen.Contains(x.VerlegungsId)));
                foreach (var t in weg.Select(x => x.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
            }, _ => (object)new { ok = true, anzahl = ids.Count, behalten = abgeleitet });
        });

        g.MapPost("/kennzeichen", (Arbeitsdokument a, KennzeichenAnfrage k, ClaimsPrincipal p) => k.Art switch
        {
            "fix" => a.AendernAsync(B(p), k.Wert ? "Vertretung fixiert" : "Fixierung gelöst", V, d => { foreach (var v in d.Vertretungen.Where(x => k.Ids.Contains(x.Id))) v.Fixiert = k.Wert; }, _ => new { ok = true }),
            "nd" => a.AendernAsync(B(p), "Nicht drucken", V, d => { foreach (var v in d.Vertretungen.Where(x => k.Ids.Contains(x.Id))) v.NichtDrucken = k.Wert; }, _ => new { ok = true }),
            _ => throw StudioFehler.Ungueltig("Unbekanntes Kennzeichen."),
        });

        g.MapPost("/neuberechnen", (Arbeitsdokument a, ZeitraumAnfrage z, ClaimsPrincipal p) =>
        {
            UmplanungsErgebnis? erg = null;
            return a.AendernAsync(B(p), "Vertretungen neu berechnet", V, d =>
            {
                erg = Umplanungsabgleich.Nacherfassen(d, B(p));
                Vertretungen.Aktualisieren(d, z.Von, z.Bis is DateOnly b && b >= z.Von ? b : z.Von);
            }, _ => new
            {
                uebertragen = erg?.Uebertragen.Count ?? 0, hinweise = erg?.Hinweise.Take(15).ToList() ?? new List<string>(),
            });
        });

        g.MapGet("/automatik", (Arbeitsdokument a, DateOnly von, DateOnly? bis) => a.LesenAsync(doc =>
        {
            var o = Vertretungsautomatik.Zaehlen(doc.Daten, von, bis ?? von);
            return new
            {
                offen = new { vertretungen = o.Vertretungen, raumvertretungen = o.Raumvertretungen, pausenaufsichten = o.Pausenaufsichten, summe = o.Summe },
                methoden = Vertretungsautomatik.Methoden.Select(m => new { wert = m.Methode.ToString(), text = m.Text }),
            };
        }));

        g.MapPost("/automatik", (Arbeitsdokument a, AutomatikAnfrage z, ClaimsPrincipal p) =>
        {
            if (!Enum.TryParse<AutomatikMethode>(z.Methode, true, out var m)) throw StudioFehler.Ungueltig("Unbekannte Methode der Automatik.");
            var n = 0;
            var bis = z.Bis is DateOnly b && b >= z.Von ? b : z.Von;
            return a.AendernAsync(B(p), "Vertretungsautomatik", V, d => n = Vertretungsautomatik.Ausfuehren(d, z.Von, bis, m, null, B(p)), doc =>
            {
                var o = Vertretungsautomatik.Zaehlen(doc.Daten, z.Von, bis);
                return new { bearbeitet = n, offen = o.Summe };
            });
        });
    }

    private static List<int> Erste(List<int> ids) => ids.Take(1).ToList();

    private static Task<object> OhneLehrer(Arbeitsdokument a, ClaimsPrincipal p, List<int> ids) =>
        a.LesenAsync(doc => Bearbeitbare(doc.Daten, ids).Where(v => v.Art != VertretungsArt.Pausenaufsicht).Select(v => v.Id).ToList())
            .ContinueWith(t => Ausfuehren(a, p, "Vertr. o. Lehrkr.", t.Result, (_, v) => Vertretungen.OhneLehrer(v, B(p)))).Unwrap();

    /// <summary>Vertreter eintragen mit den Rückfragen des Desktops: abwesend („Trotzdem verplanen?“), nicht frei („Trotzdem als Vertreter eintragen?“).</summary>
    private static async Task<IResult> VertreterSetzen(Arbeitsdokument a, ClaimsPrincipal p, List<int> ids, int lehrer, bool mitbetreuung, Dictionary<string, int>? antworten)
    {
        var (liste, frage, fix) = await a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var l = Bearbeitbare(d, ids);
            (string, string)? f = null;
            var absent = l.Where(v => Absenzen.Lehrer(d, lehrer, v.Datum, v.Stunde) != null).ToList();
            if (absent.Count > 0 && Antwort(antworten, "absent") is null)
                f = ("absent", $"{ix.KLehrer(lehrer)} ist am {Format.Datum(absent[0].Datum)} absent.\n\nTrotzdem verplanen?");
            else if (!mitbetreuung && Antwort(antworten, "belegt") is null)
            {
                var probleme = l.Except(absent).Select(v => Vertretungen.LehrerBelegt(d, ix, lehrer, v.Datum, v.Stunde, null, v) is { } g ? $"{Format.Datum(v.Datum)} {v.Stunde}. Std.: {g}" : null).OfType<string>().ToList();
                if (probleme.Count > 0) f = ("belegt", $"{ix.KLehrer(lehrer)} ist nicht frei:\n{string.Join("\n", probleme)}\n\nTrotzdem als Vertreter eintragen?");
            }
            return (l.Select(v => v.Id).ToList(), f, Fixierte(d, ids));
        });
        if (frage is { } fr) return Frage(fr.Item1, fr.Item2);
        if (Antwort(antworten, "absent") == 1 || Antwort(antworten, "belegt") == 1) return Results.Ok(new { ok = false, abgebrochen = true });
        if (liste.Count == 0) return Results.Ok(new { ok = true, anzahl = 0, hinweis = fix > 0 ? $"{fix} fixierte Vertretung(en) wurden nicht geändert." : null });
        var name = await a.LesenAsync(doc => doc.Index.KLehrer(lehrer));
        await Ausfuehren(a, p, $"Vertreter {name}", liste, (d, v) => Vertretungen.VertreterSetzen(d, v, lehrer, mitbetreuung, B(p)));
        return Results.Ok(new { ok = true, anzahl = liste.Count, hinweis = fix > 0 ? $"{fix} fixierte Vertretung(en) wurden nicht geändert." : null });
    }

    /// <summary>Entfall bzw. Entfall rückgängig (alle markierten sind Entfall) mit Rückfragen Verlegung und Sondereinsatz.</summary>
    private static async Task<IResult> Entfall(Arbeitsdokument a, ClaimsPrincipal p, List<int> ids, Dictionary<string, int>? antworten)
    {
        var (liste, verlegt, frage) = await a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var l = Bearbeitbare(d, ids).Where(v => v.Art != VertretungsArt.Freisetzung).ToList();
            var verl = l.Where(v => Verlegungsaufhebung.IstVerlegung(d, v)).ToList();
            (string, string)? f = null;
            if (verl.Count > 0 && Antwort(antworten, "verlegung") is null)
                f = ("verlegung", Verlegungsaufhebung.FrageText(d, verl.Count == 1 ? "Die Stunde ist verlegt:" : "Die Stunden sind verlegt:", verl, "Soll die Verlegung rückgängig gemacht werden?"));
            var rest = Antwort(antworten, "verlegung") == 0 ? l.Except(verl).ToList() : l;
            var zurueck = rest.Count > 0 && rest.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt);
            if (f is null && !zurueck && rest.Any(Vertretungen.IstSondereinsatz) && Antwort(antworten, "sondereinsatz") is null)
                f = ("sondereinsatz", Vertretungen.SondereinsatzLoeschenFrage);
            return (l.Select(v => v.Id).ToList(), verl.Select(v => v.Id).ToList(), f);
        });
        if (frage is { } fr) return Frage(fr.Item1, fr.Item2);
        if (liste.Count == 0) return Results.Ok(new { ok = true, anzahl = 0 });
        var aufheben = Antwort(antworten, "verlegung") == 0 ? verlegt : new List<int>();
        var seLoeschen = Antwort(antworten, "sondereinsatz") == 0;
        var set = liste.Except(aufheben).ToHashSet();
        var erg = await a.AendernAsync(B(p), set.Count > 1 ? $"Entfall ({set.Count} Stunden)" : aufheben.Count > 0 && set.Count == 0 ? "Verlegung aufgehoben" : "Entfall", V, d =>
        {
            if (aufheben.Count > 0) Verlegungsaufhebung.AlleAufheben(d, aufheben);
            var zeilen = d.Vertretungen.Where(x => set.Contains(x.Id)).ToList();
            var zurueck = zeilen.Count > 0 && zeilen.All(v => v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt);
            foreach (var v in zeilen)
            {
                var istEntfall = v.Art is VertretungsArt.Entfall or VertretungsArt.BereitschaftEntfaellt;
                if (istEntfall != zurueck) continue;
                if (seLoeschen && !zurueck) { if (!Vertretungen.EntfallUmschalten(d, v, B(p), true)) Vertretungen.AlsGeaendertMarkieren(v, B(p)); }
                else { Vertretungen.EntfallUmschalten(d, v, B(p)); Vertretungen.AlsGeaendertMarkieren(v, B(p)); }
            }
        }, _ => new { ok = true, anzahl = set.Count + aufheben.Count });
        return Results.Ok(erg);
    }

    /// <summary>
    /// Vertreter einer Veranstaltung („Cer,New,?“): für einen offenen Platz („?“) eine Ersatzlehrkraft eintragen; weitere Namen sind
    /// zusätzliche Lehrkräfte (nur an diesem Tag oder für die ganze Veranstaltung); fehlt ein Ersatz/Zusatz im Text, wird er entfernt.
    /// </summary>
    private static async Task<IResult> VeranstaltungsVertreter(Arbeitsdokument a, ClaimsPrincipal p, int id, string wert, Dictionary<string, int>? antworten)
    {
        var plan = await a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
            if (v.VeranstaltungId is not int vaId || d.Veranstaltungen.FirstOrDefault(x => x.Id == vaId) is not { } va) throw StudioFehler.Ungueltig("Die Zeile gehört zu keiner Veranstaltung.");
            var namen = wert.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Trim('?').Length > 0).ToList();
            var datum = v.Datum;
            var bisher = va.LehrerAm(datum);
            var neu = new List<int>();
            foreach (var n in namen)
            {
                var l = ElementId(d, ElementTyp.Lehrer, n)!.Value;
                if (!bisher.Contains(l) && !neu.Contains(l)) neu.Add(l);
            }
            bool Genannt(int l) => namen.Any(n => ix.KLehrer(l).Equals(n, StringComparison.OrdinalIgnoreCase));
            var offen = va.OffenAm(datum);
            var ersatzWeg = va.Ersatz.Where(e => e.Datum == datum && e.ErsatzId is int x && !Genannt(x)).Select(e => e.LehrerId).ToList();
            var zusatzWeg = va.Zusatz.Where(z => z.Datum == datum && !Genannt(z.LehrerId)).Select(z => z.LehrerId).ToList();
            var plaetze = offen.Count + ersatzWeg.Count;
            var alsErsatz = neu.Take(plaetze).ToList();
            var zusaetzlich = neu.Skip(plaetze).ToList();
            (string, string, string[])? frage = null;
            var ganze = va.Von == va.Bis;
            if (zusaetzlich.Count > 0 && !ganze)
            {
                var w = Antwort(antworten, "ganze");
                if (w is null) frage = ("ganze", $"{string.Join(", ", zusaetzlich.Select(ix.KLehrer))} zusätzlich an der Veranstaltung {va.Id} ({va.Text}) teilnehmen lassen?",
                    new[] { $"nur am {datum:dd.MM.yyyy}", $"an der ganzen Veranstaltung ({va.Von:dd.MM.} – {va.Bis:dd.MM.yyyy})" });
                else ganze = w == 1;
            }
            if (frage is null && neu.Count > 0 && Antwort(antworten, "belegt") is null)
            {
                var tp = Tagesplan.Berechnen(d, datum);
                var stunden = VeranstaltungsLogik.Stunden(d, va, datum).ToList();
                var probleme = neu.Select(l => stunden.Select(s => Vertretungen.LehrerBelegt(d, tp, l, s, v)).FirstOrDefault(g => g != null) is { } g ? $"{ix.KLehrer(l)}: {g}" : null).OfType<string>().ToList();
                if (probleme.Count > 0) frage = ("belegt", $"{string.Join("\n", probleme)}\n\nTrotzdem verplanen?", new[] { "Ja", "Nein" });
            }
            return (vaId, datum, ersatzWeg, zusatzWeg, alsErsatz, zusaetzlich, ganze, frage, leer: neu.Count == 0 && ersatzWeg.Count == 0 && zusatzWeg.Count == 0);
        });
        if (plan.frage is { } f) return Frage(f.Item1, f.Item2, f.Item3);
        if (plan.leer || Antwort(antworten, "belegt") == 1) return Results.Ok(new { ok = true, anzahl = 0 });
        await a.AendernAsync(B(p), plan.zusaetzlich.Count > 0 ? "Lehrkraft zur Veranstaltung hinzugefügt" : "Ersatzlehrkraft für Veranstaltung", V, d =>
        {
            foreach (var l in plan.ersatzWeg) VeranstaltungsLogik.ErsatzSetzen(d, plan.vaId, plan.datum, l, null);
            foreach (var l in plan.zusatzWeg) VeranstaltungsLogik.ZusatzEntfernen(d, plan.vaId, plan.datum, l);
            foreach (var l in plan.alsErsatz) VeranstaltungsLogik.ErsatzSetzen(d, plan.vaId, plan.datum, null, l);
            foreach (var l in plan.zusaetzlich) VeranstaltungsLogik.LehrerHinzufuegen(d, plan.vaId, plan.datum, l, plan.ganze);
        });
        return Results.Ok(new { ok = true, anzahl = 1 });
    }

    // =============================================================================================
    // Vertretungsvorschlag

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Zahl(decimal w) => w.ToString("0.000", Inv);
    private static string ZahlLeer(decimal w) => w == 0 ? "" : Zahl(w);
    private static string Zeitwunsch(int w) => w == 0 ? "" : w.ToString("+0;-0", Inv);

    private static object Zellen(List<StundenplanZelle> z) =>
        z.Select(x => new { stunde = x.Stunde, text = x.Text, entfallen = x.Entfallen, ziel = x.Ziel, farbe = StammdatenApi.Farbe(x.Hintergrund), schrift = StammdatenApi.Farbe(x.Vordergrund) }).ToList();

    /// <summary>Kopf wie im Vorbild: „30.8. Mo-1 Se/Mu/05D“.</summary>
    private static string KopfText(DatenIndex ix, Vertretung v)
    {
        var teile = new[] { ix.KLehrer(v.LehrerId), v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : ix.KFach(v.FachId), ix.KKlassen(v.NeueKlassenIds) };
        return $"{v.Datum.Day}.{v.Datum.Month}. {VertretungPlan.TagKurz(v.Datum)}-{Vertretungsplan.StundeText(v)} {string.Join("/", teile.Where(t => t.Length > 0))}";
    }

    private static List<int> Ziele(Schuldatei d, Vertretung v, List<int>? ids, bool block)
    {
        var res = ids is { Count: > 0 } && ids.Contains(v.Id) ? ids.ToList() : new List<int> { v.Id };
        if (block) res.AddRange(VertretungsVorschlag.Block(d, v).Select(x => x.Id));
        return res.Distinct().ToList();
    }

    private static void VorschlagRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/vorschlag/{id:int}", (Arbeitsdokument a, int id, bool? block, bool? alle, bool? disloz, bool? nahe, bool? besetzt) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
            var pause = v.Art == VertretungsArt.Pausenaufsicht;
            var blockMoeglich = !pause && VertretungsVorschlag.Block(d, v).Count > 1;
            var dislozMoeglich = !pause && VertretungsVorschlag.DislozierungenDefiniert(d);
            var bl = blockMoeglich && block == true;
            var langname = d.Vertretungseinstellungen.Vorschlag.Langname;
            var kopf = KopfText(ix, v);
            var details = $"Vtr-Nr. {v.Id}   {Format.Datum(v.Datum)}   {Vertretungsplan.StundeText(v)}. Std.   Klasse(n) {ix.KKlassen(v.NeueKlassenIds)}   Lehrer {ix.KLehrer(v.LehrerId)}   Fach {ix.KFach(v.FachId)}   " +
                          $"{(pause ? "Gang " + ix.K(ElementTyp.Gang, v.GangId) : "Raum " + ix.KRaum(v.VertretungsraumId ?? v.RaumId))}\n" +
                          $"{Vertretungsplan.ArtText(d, v.Art)}{(v.Grund.Length > 0 ? ", " + v.Grund : "")}{(v.Text.Length > 0 ? " – " + v.Text : "")}";
            var kandidaten = new List<object>();
            var betreuungen = new List<object>();
            var raeume = new List<object>();
            var vorziehungen = new List<object>();
            if (pause)
            {
                var pa = new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, v.Datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 };
                var (zv, zb) = Zaehlung.Zeitraum(d, v.Datum);
                foreach (var l in UntPlan.Core.Module.Pausenaufsichten.Kandidaten(d, v.Datum, pa))
                {
                    var z = Zaehlung.Lehrer(d, l, zv, zb);
                    var (vw, ew) = VertretungsVorschlag.Werte(z);
                    kandidaten.Add(new
                    {
                        lehrer = l.Id, name = langname ? l.VollerName : l.Kurzname, lang = l.VollerName, s = new List<object>(), merker = "", vertr = Zahl(vw), entf = Zahl(ew),
                        zaehler = ZahlLeer(z.Summe), zaehlerWert = z.Summe, zw = "", jahr = "", kennz = false, kennzText = "", regB = "", dep = "", fach = false, klasse = "",
                        stdTag = "", woche = "", monat = "", punkte = "", kriterien = $"Aufsichten: {UntPlan.Core.Module.Pausenaufsichten.Anzahl(d, l.Id)}", rot = false, benachrichtigen = false, lage = "",
                    });
                }
            }
            else
            {
                foreach (var k in VertretungsVorschlag.Berechnen(d, v, bl, alle == true, dislozMoeglich && disloz == true))
                    kandidaten.Add(new
                    {
                        lehrer = k.Lehrer.Id, name = langname ? k.Lehrer.VollerName : k.Lehrer.Kurzname, lang = k.Lehrer.VollerName, s = Zellen(k.Zellen), merker = k.Merker,
                        vertr = Zahl(k.VertretungenWert), entf = Zahl(k.EntfaelleWert), zaehler = ZahlLeer(k.Zaehler), zaehlerWert = k.Zaehler, zw = Zeitwunsch(k.Zeitwunsch),
                        jahr = ZahlLeer(k.Jahreszaehler), kennz = k.Kennzeichen, kennzText = k.KennzeichenText, regB = k.RegelBereitschaft > 0 ? k.RegelBereitschaft.ToString() : "",
                        dep = k.Deputat is decimal dep ? ZahlLeer(dep) : "", fach = k.Fachlehrer, klasse = k.Klassenlehrer ? "KL" : k.KenntKlasse ? "✔" : "",
                        stdTag = k.UnterrichtAmTag.ToString(), woche = k.ZaehlerWoche.ToString(), monat = k.ZaehlerMonat.ToString(), punkte = ((int)k.Punkte).ToString(),
                        kriterien = string.Join(", ", k.Kriterien), rot = k.Rot, benachrichtigen = k.Benachrichtigen, lage = k.Lage,
                    });
                if (v.Offen)
                    foreach (var vz in VertretungsVorschlag.Vorziehungen(d, v))
                        vorziehungen.Add(new
                        {
                            lehrer = vz.Lehrer, tag = $"{VertretungPlan.TagKurz(vz.Datum)} {vz.Datum.Day}.{vz.Datum.Month}.", datum = vz.Datum, stunde = vz.Stunde, klassen = vz.Klassen,
                            fach = vz.Fach, raum = vz.Raum, art = vz.Randstunde ? "Randstunde" : "erzeugt neue Lücke", beschreibung = vz.Beschreibung, u = vz.Eintrag.Unterricht?.Id,
                        });
                foreach (var b in VertretungsVorschlag.Betreuungen(d, v).Where(b => nahe != true || b.Naehe <= 2))
                    betreuungen.Add(new
                    {
                        lehrer = b.Lehrer.Id, name = langname ? b.Lehrer.VollerName : b.Lehrer.Kurzname, lang = b.Lehrer.VollerName, s = Zellen(b.Zellen), raum = b.Raum, klasse = b.Klassen, fach = b.Fach,
                        zaehler = ZahlLeer(b.Zaehler), zaehlerWert = b.Zaehler, zw = Zeitwunsch(b.Zeitwunsch), jahr = ZahlLeer(b.Jahreszaehler), kennz = b.Kennzeichen, kennzText = b.KennzeichenText,
                        kopplung = b.Kopplung, naehe = b.NaeheText, heute = b.BetreuungenHeute > 0 ? b.BetreuungenHeute.ToString() : "",
                    });
                foreach (var rm in Raumvertretungen.MoeglicheRaeume(d, v, besetzt == true, bl))
                    raeume.Add(new
                    {
                        raum = rm.Raum.Id, name = langname && rm.Raum.Langname.Length > 0 ? rm.Raum.Langname : rm.Raum.Kurzname, lang = rm.Raum.Langname,
                        kap = rm.KapazitaetDiff?.ToString("+0;-0;0") ?? "", ar = rm.Ausweichraum, sr = rm.AusweichStammraum, besetzt = rm.Besetzt, gesperrt = rm.Gesperrt,
                        belegung = string.Join("; ", rm.Belegung.Select(x => $"{x.Stunde}. Std.: {ix.KKlassen(x.Eintrag.KlassenIds)} {ix.KFach(x.Eintrag.FachId)} {string.Join(",", x.Eintrag.LehrerIds.Select(ix.KLehrer))}".Trim())),
                        rg = rm.Raumgruppen, gang = rm.Gang, statistik = rm.Statistik,
                    });
            }
            return new
            {
                id = v.Id, kopf, details, offen = v.Offen, raumOffen = v.RaumOffen, pause,
                optionen = new { block = blockMoeglich, disloz = dislozMoeglich, dauer = !pause && v.UnterrichtId.HasValue },
                kandidaten, betreuungen, raeume, vorziehungen,
            };
        }));

        g.MapPost("/vorschlag/{id:int}/uebernehmen", async (Arbeitsdokument a, int id, UebernehmenAnfrage u, ClaimsPrincipal p) =>
        {
            if (u.Dauer)
            {
                var uebersprungen = 0;
                var name = await a.LesenAsync(doc => doc.Index.KLehrer(u.Lehrer));
                await a.AendernAsync(B(p), $"Dauervertretung {name}", V, d =>
                {
                    var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
                    uebersprungen = Vertretungen.Dauervertretung(d, v, u.Lehrer, u.Block, B(p)).Uebersprungen.Count;
                });
                return Results.Ok(new { ok = true, hinweis = uebersprungen > 0 ? $"{name} ist in {uebersprungen} Stunde(n) der Folgewochen nicht frei – diese bleiben offen." : null });
            }
            var ids = await a.LesenAsync(doc => Ziele(doc.Daten, doc.Daten.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}"), u.Ids, u.Block));
            var nm = await a.LesenAsync(doc => doc.Index.KLehrer(u.Lehrer));
            await Ausfuehren(a, p, $"Vertreter {nm}", ids, (d, v) => Vertretungen.VertreterSetzen(d, v, u.Lehrer, false, B(p)));
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/vorschlag/{id:int}/betreuung", async (Arbeitsdokument a, int id, UebernehmenAnfrage u, ClaimsPrincipal p) =>
        {
            var ids = await a.LesenAsync(doc => Ziele(doc.Daten, doc.Daten.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}"), u.Ids, u.Block));
            var nm = await a.LesenAsync(doc => doc.Index.KLehrer(u.Lehrer));
            return await Ausfuehren(a, p, $"Betreuung {nm}", ids, (d, v) => Vertretungen.VertreterSetzen(d, v, u.Lehrer, true, B(p)));
        });

        g.MapPost("/vorschlag/{id:int}/raum", async (Arbeitsdokument a, int id, RaumAnfrage r, ClaimsPrincipal p) =>
        {
            var (ids, frage, name) = await a.LesenAsync(doc =>
            {
                var d = doc.Daten;
                var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
                string? f = null;
                if (r.Raum is int rid)
                {
                    var rm = Raumvertretungen.MoeglicheRaeume(d, v, true, r.Block).FirstOrDefault(x => x.Raum.Id == rid);
                    if (rm?.Belegung.FirstOrDefault() is { Eintrag: not null } b && Antwort(r.Antworten, "kollision") is null)
                        f = Raumvertretungen.KollisionsText(d, rm.Raum, v.Datum, b.Stunde, rm.Belegung.Where(x => x.Stunde == b.Stunde).Select(x => x.Eintrag));
                    else if (rm is { Gesperrt: true } && Antwort(r.Antworten, "gesperrt") is null) f = "gesperrt:" + $"Der Raum {rm.Raum.Kurzname} ist zu dieser Zeit gesperrt. Trotzdem zuordnen?";
                }
                return (Ziele(d, v, r.Ids, r.Block), f, r.Raum is int ri ? doc.Index.KRaum(ri) : "");
            });
            if (frage is { } f)
                return f.StartsWith("gesperrt:") ? Frage("gesperrt", f[9..])
                    : Frage("kollision", f, "Raum nicht verplanen", "Raum mit Kollision verplanen", "Raum ohne Kollision verplanen (erzeugt Raumvertretung)", "Räume tauschen (erzeugt zwei Raumvertretungen)");
            var loesung = Antwort(r.Antworten, "kollision") is int k ? (RaumKollisionsLoesung)k : RaumKollisionsLoesung.MitKollision;
            if (loesung == RaumKollisionsLoesung.NichtVerplanen || Antwort(r.Antworten, "gesperrt") == 1) return Results.Ok(new { ok = false, abgebrochen = true });
            return Results.Ok(await Ausfuehren(a, p, r.Raum is null ? "Raum löschen" : $"Raum {name}", ids, (d, v) => Raumvertretungen.Zuordnen(d, v, r.Raum, r.Zusatz, loesung, B(p))));
        });

        g.MapPost("/vorschlag/{id:int}/vorziehen", (Arbeitsdokument a, int id, VorziehenAnfrage z, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vorziehung", V, d =>
            {
                var v = d.Vertretungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vertretung Nr. {id}");
                var vz = VertretungsVorschlag.Vorziehungen(d, v).FirstOrDefault(x => x.Datum == z.Datum && x.Stunde == z.Stunde && (z.Unterricht is null || x.Eintrag.Unterricht?.Id == z.Unterricht))
                         ?? throw StudioFehler.Ungueltig("Die Vorziehung ist nicht mehr möglich.");
                VertretungsVorschlag.VorziehungAusfuehren(d, v, vz, B(p));
            }, _ => new { ok = true }));
    }

    // =============================================================================================
    // Sondereinsätze, Bereitschaften, Bereitschaftsplanung, Vormerkungen

    private static void EreignisseRegistrieren(RouteGroupBuilder g)
    {
        g.MapPost("/sondereinsaetze", (Arbeitsdokument a, SondereinsatzAnfrage s, ClaimsPrincipal p) =>
        {
            var n = 0;
            return a.AendernAsync(B(p), s.Zeilen.Count > 1 ? $"Sondereinsätze ({s.Zeilen.Count})" : "Sondereinsatz", V, d =>
            {
                foreach (var z in s.Zeilen)
                {
                    var lehrer = ElementIds(d, ElementTyp.Lehrer, z.Lehrer);
                    var klassen = ElementIds(d, ElementTyp.Klasse, z.Klassen);
                    if (lehrer.Count == 0 && klassen.Count == 0) throw StudioFehler.Ungueltig("Für einen Sondereinsatz bitte Lehrkräfte oder Klassen angeben.");
                    var max = Math.Max(1, d.Zeitraster.StundenProTag);
                    var sv = Math.Clamp(z.Sv, 1, max);
                    var sb = Math.Clamp(z.Sb ?? sv, sv, max);
                    n += Vertretungen.Sondereinsatz(d, z.Datum, sv, sb, klassen, lehrer, string.IsNullOrWhiteSpace(z.Fach) ? null : ElementId(d, ElementTyp.Fach, z.Fach),
                        string.IsNullOrWhiteSpace(z.Raum) ? null : ElementId(d, ElementTyp.Raum, z.Raum), z.Text ?? "", z.Gruppe ?? "", null, B(p)).Count;
                }
            }, _ => new { ok = true, anzahl = n });
        });

        // Klassenkonferenz: alle Lehrkräfte, die die Klassen unterrichten, und die Klassenleitungen
        g.MapGet("/klassenkonferenz", (Arbeitsdokument a, string klassen) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var kl = ElementIds(d, ElementTyp.Klasse, klassen);
            var ids = d.Unterrichte.SelectMany(u => u.Zeilen).Where(z => z.KlassenIds.Intersect(kl).Any() && z.LehrerId.HasValue).Select(z => z.LehrerId!.Value)
                .Concat(d.Klassen.Where(k => kl.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds)).Distinct().ToList();
            return new { lehrer = string.Join(",", Stammdaten.Sortiert(d.Lehrer).Where(l => ids.Contains(l.Id)).Select(l => l.Kurzname)) };
        }));

        g.MapGet("/bereitschaften", (Arbeitsdokument a, DateOnly datum) => a.LesenAsync(doc => VertretungPlan.Bereitschaften(doc, datum)));

        g.MapGet("/bereitschaftsplanung", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var stunden = BP.Stunden(d);
            var z = d.Zeitraster;
            return new
            {
                tage = Enumerable.Range(0, z.TageProWoche).Select(t => UntPlan.Core.Planung.Planungsdienst.Tagname(d, t)).ToList(),
                stunden = Enumerable.Range(1, z.StundenProTag).Select(s => z.StundenLabel(s)).ToList(),
                zellen = Enumerable.Range(0, z.TageProWoche).Select(t => Enumerable.Range(1, z.StundenProTag).Select(s => new
                {
                    bedarf = BP.Bedarf(d, t, s), lehrer = stunden.Where(b => b.Tag == t && b.Stunde == s).Select(b => new { id = b.Lehrer.Id, kurz = b.Lehrer.Kurzname }).ToList(),
                    verfuegbar = z.Verfuegbar(t, s),
                }).ToList()).ToList(),
                anzahl = stunden.Count, bedarf = d.Bereitschaftsbedarf.Sum(b => b.Anzahl), nurDoppelstunden = d.Einstellungen.BereitschaftNurDoppelstunden,
                fach = BP.Bereitschaftsfach(d)?.Kurzname,
            };
        }));

        g.MapGet("/bereitschaftsplanung/vorschlag", (Arbeitsdokument a, int tag, int stunde) => a.LesenAsync(doc =>
            BP.Vorschlag(doc.Daten, tag, stunde).Select(k => new
            {
                lehrer = k.Lehrer.Id, kurz = k.Lehrer.Kurzname, lang = k.Lehrer.VollerName, punkte = (int)k.Punkte, woche = k.BereitschaftenWoche,
                dep = k.FreiesDeputat.ToString("+0.#;-0.#;0", Format.De), hinweis = k.Hinweis,
            }).ToList()));

        g.MapPost("/bereitschaftsplanung/{aktion}", (Arbeitsdokument a, string aktion, BereitschaftAnfrage b, ClaimsPrincipal p) =>
        {
            var n = 0;
            return aktion switch
            {
                "bedarf" => a.AendernAsync(B(p), "Bereitschaftsbedarf", AenderungsArt.Einstellungen, d => BP.BedarfSetzen(d, b.Tag, b.Stunde, Math.Clamp(b.Anzahl ?? 0, 0, 20)), _ => (object)new { ok = true }),
                "setzen" => a.AendernAsync(B(p), "Bereitschaft eintragen", AenderungsArt.Stundenplan, d => BP.Setzen(d, b.Lehrer ?? 0, b.Tag, b.Stunde), _ => (object)new { ok = true }),
                "entfernen" => a.AendernAsync(B(p), "Bereitschaft entfernen", AenderungsArt.Stundenplan, d => BP.Entfernen(d, b.Lehrer ?? 0, b.Tag, b.Stunde), _ => (object)new { ok = true }),
                "automatisch" => a.AendernAsync(B(p), "Bereitschaften automatisch besetzen", AenderungsArt.Stundenplan, d => n = BP.Automatisch(d), doc => (object)new
                {
                    ok = true, anzahl = n,
                    fehlt = doc.Daten.Bereitschaftsbedarf.Sum(x => Math.Max(0, x.Anzahl - BP.Stunden(doc.Daten).Count(y => y.Tag == x.Tag && y.Stunde == x.Stunde))),
                }),
                "vormittag" => a.AendernAsync(B(p), "Bereitschaftsbedarf", AenderungsArt.Einstellungen, d =>
                {
                    for (var t = 0; t < d.Zeitraster.TageProWoche; t++)
                        for (var s = 1; s <= d.Zeitraster.StundenProTag; s++)
                            if (d.Zeitraster.Art(t, s) == Tageszeit.Vormittag && d.Zeitraster.Verfuegbar(t, s)) BP.BedarfSetzen(d, t, s, Math.Clamp(b.Anzahl ?? 1, 0, 20));
                }, _ => (object)new { ok = true }),
                "alleentfernen" => a.AendernAsync(B(p), "Bereitschaften entfernen", AenderungsArt.Stundenplan, d =>
                {
                    foreach (var x in BP.Stunden(d)) BP.Entfernen(d, x.Lehrer.Id, x.Tag, x.Stunde);
                }, _ => (object)new { ok = true }),
                "doppelstunden" => a.AendernAsync(B(p), "Bereitschaften nur als Doppelstunden", AenderungsArt.Einstellungen, d => d.Einstellungen.BereitschaftNurDoppelstunden = (b.Anzahl ?? 0) > 0, _ => (object)new { ok = true }),
                _ => throw StudioFehler.NichtGefunden($"Die Aktion \"{aktion}\""),
            };
        });

        g.MapGet("/vormerkungen", (Arbeitsdokument a, bool? alle) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            return d.Vormerkungen.Where(v => alle == true || v.Offen > 0).OrderBy(v => v.Datum ?? DateOnly.MaxValue).Select(v => new
            {
                id = v.Id,
                herkunft = v.Quelle switch { VormerkungsQuelle.Vertretung => "Vertretung", VormerkungsQuelle.Verdraengung => "Verdrängung", VormerkungsQuelle.Stundenplan => "Stundenplan", VormerkungsQuelle.Unterricht => "Unterricht", _ => "konkret" },
                stunden = v.Stunden, offen = v.Offen, fach = ix.KFach(v.FachId), lehrer = string.Join(",", v.LehrerIds.Select(ix.KLehrer)), klassen = ix.KKlassen(v.KlassenIds), raum = ix.KRaum(v.RaumId),
                urspruenglich = v.Datum is DateOnly dd ? $"{Format.Datum(dd)} / {v.Stunde}" : "",
                gueltig = v.GueltigVon.HasValue || v.GueltigBis.HasValue ? $"{(v.GueltigVon is DateOnly x ? Format.Datum(x) : "")} – {(v.GueltigBis is DateOnly y ? Format.Datum(y) : "")}" : "",
                text = v.Text, verplant = string.Join(", ", v.VerplantAm.Select(Format.Datum)), u = v.UnterrichtId,
            }).ToList();
        }));

        g.MapPost("/vormerkungen", (Arbeitsdokument a, VormerkungEingabe e, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vormerkung", V, d => d.Vormerkungen.Add(new Vormerkung
            {
                Id = d.NeueId(), Quelle = VormerkungsQuelle.Konkret, FachId = string.IsNullOrWhiteSpace(e.Fach) ? null : ElementId(d, ElementTyp.Fach, e.Fach),
                LehrerIds = ElementIds(d, ElementTyp.Lehrer, e.Lehrer), KlassenIds = ElementIds(d, ElementTyp.Klasse, e.Klassen),
                RaumId = string.IsNullOrWhiteSpace(e.Raum) ? null : ElementId(d, ElementTyp.Raum, e.Raum), Stunden = Math.Clamp(e.Stunden, 1, 99), Text = e.Text ?? "",
            }), _ => new { ok = true }));

        g.MapPost("/vormerkungen/{id:int}/verplanen", (Arbeitsdokument a, int id, VormerkungVerplanen z, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vormerkung verplanen", V, d =>
            {
                var vm = d.Vormerkungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Vormerkung Nr. {id}");
                if (vm.Offen == 0) throw StudioFehler.Ungueltig("Die Vormerkung ist bereits vollständig verplant.");
                Tagesverlegung.VormerkungVerplanen(d, vm, z.Datum, z.Stunde, B(p));
            }, _ => new { ok = true }));

        g.MapPost("/vormerkungen/loeschen", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vormerkung löschen", V, d => d.Vormerkungen.RemoveAll(v => i.Ids.Contains(v.Id)), _ => new { ok = true }));
    }

    // =============================================================================================
    // Tagesaktuelle Stundenpläne

    private static void PlanRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/plan/{typ}/{id:int}", (Arbeitsdokument a, string typ, int id, DateOnly datum) => a.LesenAsync(doc => VertretungPlan.Woche(doc, Typnamen.Typ(typ), id, datum)));

        g.MapGet("/plan/zielstatus", (Arbeitsdokument a, DateOnly von, int stunde, int unterricht, int? zeile, int? laenge, DateOnly? woche) =>
            a.LesenAsync(doc => VertretungPlan.Zielstatus(doc.Daten, von, stunde, unterricht, zeile, laenge ?? 1, woche ?? von)));

        // Ablegen im tagesaktuellen Plan: verlegen, tauschen („Was wollen Sie … machen?“), verdrängen oder Kollision erzeugen
        g.MapPost("/plan/verlegen", async (Arbeitsdokument a, PlanVerlegen v, ClaimsPrincipal p) =>
        {
            if (v.Von == v.Ziel && v.VonStunde == v.ZielStunde) return Results.Ok(new { ok = true });
            var laenge = Math.Max(1, v.Laenge);
            var (frage, modus, analyse) = await a.LesenAsync<((string, string, string[])? Frage, VerlegeModus Modus, VerlegeAnalyse Analyse)>(doc =>
            {
                var d = doc.Daten;
                if (v.ZielStunde + laenge - 1 > d.Zeitraster.StundenProTag) throw StudioFehler.Ungueltig("Der Block passt an dieser Position nicht mehr in den Tag.");
                var an = Tagesverlegung.Analysieren(d, v.Von, v.VonStunde, v.Unterricht, v.Zeile, laenge, v.Ziel, v.ZielStunde);
                if (an.Schritte.Count == 0) return (null, VerlegeModus.Verlegen, an);
                var tpVon = Tagesplan.Berechnen(d, v.Von);
                if (Antwort(v.Antworten, "fix") is null && an.Schritte.Any(x => tpVon.Stunde(x.VonStunde).Any(e => (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == v.Unterricht &&
                        (e.Regel?.Planstunde.Fixiert == true || e.Vertretung?.Fixiert == true))))
                    return (("fix", "Die Stunde ist fixiert. Trotzdem verlegen?", new[] { "Ja", "Nein" }), VerlegeModus.Verlegen, an);
                var m = VerlegeModus.Verlegen;
                if (an.TauschMoeglich)
                {
                    var w = Antwort(v.Antworten, "tausch");
                    if (w is null) return (("tausch", Tagesverlegung.TauschFrage(d, an), new[] { "mit dortiger Unterrichtsstunde tauschen", "nur dorthin verlegen" }), m, an);
                    if (w == 0) m = VerlegeModus.Tauschen; else an = an.NurDorthin();
                }
                if (m != VerlegeModus.Tauschen && an.Kollision)
                {
                    var w = Antwort(v.Antworten, "kollision");
                    if (w is null) return (("kollision", Tagesverlegung.KollisionsFrage(d, an), new[] { "Unterricht nicht verplanen.", "Behindernden Unterricht verdrängen (vormerken)", "Kollision erzeugen" }), m, an);
                    m = w == 1 ? VerlegeModus.Verdraengen : w == 2 ? VerlegeModus.Kollision : (VerlegeModus)(-1);
                }
                if ((int)m >= 0 && m != VerlegeModus.Tauschen && an.LehrerAbwesend && Antwort(v.Antworten, "abwesend") is null)
                    return (("abwesend", "Ein beteiligter Lehrer ist zu diesem Zeitpunkt abwesend. Trotzdem verlegen? (Es entsteht eine offene Vertretung.)", new[] { "Ja", "Nein" }), m, an);
                return (null, m, an);
            });
            if (frage is { } f) return Frage(f.Item1, f.Item2, f.Item3);
            if ((int)modus < 0 || Antwort(v.Antworten, "fix") == 1 || Antwort(v.Antworten, "abwesend") == 1 || analyse.Schritte.Count == 0) return Results.Ok(new { ok = false, abgebrochen = true });
            var text = modus switch { VerlegeModus.Tauschen => "Stundentausch", VerlegeModus.Verdraengen => "Verlegung mit Verdrängung", _ => laenge > 1 ? $"Block verlegen ({laenge} Stunden)" : "Stundenverlegung" };
            await a.AendernAsync(B(p), text, V, d => Tagesverlegung.Ausfuehren(d, analyse, modus, B(p)));
            return Results.Ok(new { ok = true, text });
        });

        g.MapPost("/plan/entfall", async (Arbeitsdokument a, PlanPosition s, ClaimsPrincipal p) =>
        {
            var (verlegung, sonder) = await a.LesenAsync(doc =>
            {
                var d = doc.Daten;
                var vl = Verlegungsaufhebung.VerlegungAn(d, s.Datum, s.Stunde, s.Unterricht);
                return (vl is null ? null : new { id = vl.Id, text = Verlegungsaufhebung.FrageText(d, "Die Stunde ist verlegt:", new[] { vl }, "Soll die Verlegung rückgängig gemacht werden?") },
                    Vertretungen.SondereinsaetzeBeiEntfall(d, s.Unterricht, s.Zeile, s.Datum, s.Stunde).Count > 0);
            });
            if (verlegung != null)
            {
                var w = Antwort(s.Antworten, "verlegung");
                if (w is null) return Frage("verlegung", verlegung.text);
                if (w == 0)
                {
                    await a.AendernAsync(B(p), "Verlegung aufgehoben", V, d => Verlegungsaufhebung.AlleAufheben(d, new[] { verlegung.id }));
                    return Results.Ok(new { ok = true });
                }
            }
            bool? seLoeschen = null;
            if (sonder)
            {
                var w = Antwort(s.Antworten, "sondereinsatz");
                if (w is null) return Frage("sondereinsatz", Vertretungen.SondereinsatzLoeschenFrage);
                seLoeschen = w == 0;
            }
            await a.AendernAsync(B(p), "Entfall", V, d => Vertretungen.Entfallen(d, s.Unterricht, s.Zeile, s.Datum, s.Stunde, B(p), seLoeschen));
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/plan/entfallrueckgaengig", (Arbeitsdokument a, PlanPosition s, ClaimsPrincipal p) =>
        {
            var n = 0;
            return a.AendernAsync(B(p), "Entfall rückgängig", V, d =>
                n = s.Block ? Entfallaufhebung.BlockAufheben(d, s.Unterricht, s.Zeile, s.Datum, s.Stunde, B(p)) : Entfallaufhebung.Aufheben(d, s.Unterricht, s.Zeile, s.Datum, s.Stunde, B(p)),
                _ => new { ok = true, anzahl = n });
        });

        g.MapPost("/plan/vormerken", (Arbeitsdokument a, PlanPosition s, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Vormerkung", V, d => Tagesverlegung.Vormerken(d, s.Datum, s.Stunde, s.Unterricht, s.Zeile, VormerkungsQuelle.Stundenplan, B(p)), _ => new { ok = true }));

        // Änderungen der Stunde zurücknehmen: manuelle Vertretungszeile (mit Gegenstück der Verlegung) löschen
        g.MapPost("/plan/aenderungenrueckgaengig", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Änderungen rückgängig", V, d =>
            {
                var v = d.Vertretungen.FirstOrDefault(x => i.Ids.Contains(x.Id)) ?? throw StudioFehler.NichtGefunden("Die Vertretung");
                var weg = d.Vertretungen.Where(x => x.Id == v.Id || (v.VerlegungsId.HasValue && x.VerlegungsId == v.VerlegungsId)).ToList();
                foreach (var x in weg) d.Vertretungen.Remove(x);
                foreach (var t in weg.Select(x => x.Datum).Distinct()) Vertretungen.Aktualisieren(d, t);
            }, _ => new { ok = true }));

        g.MapPost("/plan/text", (Arbeitsdokument a, PlanPosition s, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Text zur Vertretung", V, d =>
            {
                var tp = Tagesplan.Berechnen(d, s.Datum);
                var e = tp.Stunde(s.Stunde).FirstOrDefault(x => x.Findetstatt && (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == s.Unterricht && (s.Zeile is null || (x.Regel?.ZeilenIndex ?? x.Vertretung?.ZeilenIndex ?? 0) == s.Zeile))
                        ?? throw StudioFehler.NichtGefunden("Die Stunde");
                var v = e.Vertretung;
                if (v is null)
                {
                    v = new Vertretung
                    {
                        Id = d.NeueId(), Datum = s.Datum, Stunde = s.Stunde, UnterrichtId = s.Unterricht, ZeilenIndex = e.Regel?.ZeilenIndex ?? 0, LehrerId = e.Regel?.LehrerId, VertreterId = e.Regel?.LehrerId,
                        FachId = e.FachId, KlassenIds = e.KlassenIds.ToList(), RaumId = e.Regel?.RaumId, VertretungsraumId = e.Regel?.RaumId, Art = VertretungsArt.UnterrichtGeaendert,
                        Quelle = VertretungsQuelle.Manuell, Bearbeitet = true, Benutzer = B(p),
                    };
                    d.Vertretungen.Add(v);
                }
                v.Text = s.Text ?? "";
                Vertretungen.AlsGeaendertMarkieren(v, B(p));
            }, _ => new { ok = true }));
    }

    // =============================================================================================
    // Einstellungen der Vertretungsplanung (Zeitraster, Zähler, Vorschlag, Allgemein, Farben und Bezeichnungen)

    private static void EinstellungenRegistrieren(RouteGroupBuilder g)
    {
        g.MapGet("/einstellungen", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var e = d.Vertretungseinstellungen;
            return new
            {
                einstellungen = e,
                evaRaum = ix.KRaum(e.EvaRaumId),
                klassen = Stammdaten.Sortiert(d.Klassen).Select(k => new { id = k.Id, kurz = k.Kurzname, zeitraster = k.VertretungsZeitraster }).ToList(),
                tage = Enumerable.Range(0, d.Zeitraster.TageProWoche).Select(t => UntPlan.Core.Planung.Planungsdienst.Tagname(d, t)).ToList(),
                stunden = d.Zeitraster.StundenProTag,
                arten = Enum.GetValues<VertretungsArt>().Select(x => new
                {
                    art = x.ToString(), standard = Vertretungsplan.ArtText(x), text = e.Bezeichnungen.GetValueOrDefault(x.ToString()) ?? "",
                    farbe = e.Farben.TryGetValue(x.ToString(), out var f) ? StammdatenApi.Farbe(f) : null,
                }).ToList(),
                ansichten = VertretungListe.Ansichten,
                spalten = VertretungListe.Spalten,
            };
        }));

        g.MapPut("/einstellungen", (Arbeitsdokument a, JsonElement body, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), "Einstellungen Vertretungsplanung", AenderungsArt.Einstellungen, d =>
            {
                var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                if (body.TryGetProperty("einstellungen", out var ein))
                {
                    var neu = ein.Deserialize<VertretungsEinstellungen>(opt) ?? throw StudioFehler.Ungueltig("Ungültige Einstellungen.");
                    if (body.TryGetProperty("evaRaum", out var eva) && eva.ValueKind == JsonValueKind.String)
                        neu.EvaRaumId = string.IsNullOrWhiteSpace(eva.GetString()) ? null : ElementId(d, ElementTyp.Raum, eva.GetString());
                    d.Vertretungseinstellungen = neu;
                }
                if (body.TryGetProperty("klassen", out var kl) && kl.ValueKind == JsonValueKind.Array)
                    foreach (var k in kl.EnumerateArray())
                        if (k.TryGetProperty("id", out var kid) && d.Klassen.FirstOrDefault(x => x.Id == kid.GetInt32()) is { } klasse && k.TryGetProperty("zeitraster", out var zr))
                            klasse.VertretungsZeitraster = zr.Deserialize<Dictionary<int, VertretungsRegel>>(opt) ?? new();
            }, doc => new { ok = true }));
    }
}
