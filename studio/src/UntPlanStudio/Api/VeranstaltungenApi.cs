using System.Security.Claims;
using System.Text.Json;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record VaFeldAnfrage(string Feld, string? Wert, DateOnly? Datum = null);
public sealed record VaSchnellAnfrage(string Feld, List<int>? Ids, bool Ersetzen = false, DateOnly? Datum = null);
public sealed record VaAusStunde(int Unterricht, DateOnly Datum, int Stunde, int? Va = null);
public sealed record VaKopieren(DateOnly Datum);

/// <summary>
/// Veranstaltungen (Exkursion, Klassenfahrt, Schulveranstaltung) wie im Fenster „Veranst. / Lehrer“ des Desktops: Spalten VA-Nr., Klasse,
/// Kl-Teil, Lehrer, Raum, Von, ab St., Bis, bis St., Grund, Text, zählen, Schülergruppe; Eingabe direkt in der Tabelle („Alle Lehrkräfte“,
/// „Alle Klassen außer 5a“ …), Ersatzlehrkräfte für entfernte Lehrkräfte („???“), Schnellauswahl nach Abteilung/Jahrgang/Gruppe,
/// alle aus den Stunden übernehmen, Kopieren auf ein anderes Datum.
/// </summary>
public sealed class VeranstaltungenApi : IStudioModul
{
    public string Name => "veranstaltungen";

    private static string B(ClaimsPrincipal p) => p.Benutzername();

    private static readonly string[] Felder = { "K", "KT", "L", "R" };

    private static Veranstaltung Kopie(Veranstaltung v) => JsonSerializer.Deserialize<Veranstaltung>(JsonSerializer.Serialize(v))!;

    private static string Trenner(int anzahl) => anzahl > 6 ? ", " : ",\n";

    private static DateOnly? OffenerTag(Veranstaltung v, DateOnly von, DateOnly bis) =>
        v.Ersatz.Where(e => e.ErsatzId is null && e.Datum >= von && e.Datum <= bis && v.LehrerIds.Contains(e.LehrerId)).Select(e => (DateOnly?)e.Datum).OrderBy(x => x).FirstOrDefault();

    private static object Zeile(Schuldatei d, DatenIndex ix, Veranstaltung v, DateOnly von, DateOnly bis, DateOnly heute)
    {
        string Feldtext(ElementTyp typ, List<int> ids) => VeranstaltungsAuswahl.Kurztext(d, typ, ids, v.Von, v.Bis, Trenner(ids.Count));
        var offen = OffenerTag(v, von, bis);
        var lehrer = offen is DateOnly t ? VeranstaltungsLogik.LehrerKurztext(d, v, t, Trenner(v.LehrerIds.Count), "???") : Feldtext(ElementTyp.Lehrer, v.LehrerIds);
        return new
        {
            id = v.Id, nr = v.Id.ToString(), K = Feldtext(ElementTyp.Klasse, v.KlassenIds), KT = Feldtext(ElementTyp.Klasse, v.KlassenteilIds), L = lehrer,
            R = Feldtext(ElementTyp.Raum, v.RaumIds), V = v.Von.ToString("dd.MM.yyyy"), SV = v.StundeVon.ToString(), B = v.Bis.ToString("dd.MM.yyyy"),
            SB = v.StundeBis >= 99 ? d.Zeitraster.StundenProTag.ToString() : v.StundeBis.ToString(), G = ix.K(ElementTyp.Abwesenheitsgrund, v.GrundId), T = v.Text,
            Z = v.Zaehlen ? "X" : "", SG = v.Schuelergruppe,
            von = v.Von, bis = v.Bis,
            offen = offen is DateOnly ot ? $"Am {ot:dd.MM.yyyy} wurde eine Lehrkraft entfernt – Ersatzlehrkraft statt ??? eintragen." : null,
            vergangen = v.Bis < heute,
            anzahl = new { klassen = v.KlassenIds.Count, teil = v.KlassenteilIds.Count, lehrer = v.LehrerIds.Count, raeume = v.RaumIds.Count },
        };
    }

    private static VeranstaltungsAuswahl.Feld FeldVon(string f) => f switch
    {
        "K" or "Klassen" => VeranstaltungsAuswahl.Feld.Klassen,
        "KT" or "Klassenteil" => VeranstaltungsAuswahl.Feld.Klassenteil,
        "L" or "Lehrer" => VeranstaltungsAuswahl.Feld.Lehrer,
        "R" or "Raeume" => VeranstaltungsAuswahl.Feld.Raeume,
        _ => throw StudioFehler.Ungueltig($"Unbekanntes Feld \"{f}\"."),
    };

    /// <summary>Speichern (neu bzw. ersetzt) und Vertretungen im betroffenen Zeitraum neu berechnen.</summary>
    private static void Speichern(Schuldatei d, Veranstaltung v, Veranstaltung? alt)
    {
        if (alt != null) d.Veranstaltungen.RemoveAll(x => x.Id == alt.Id);
        else v.Id = d.NeueId();
        d.Veranstaltungen.Add(v);
        var von = alt is null || v.Von < alt.Von ? v.Von : alt.Von;
        var bis = alt is null || v.Bis > alt.Bis ? v.Bis : alt.Bis;
        Vertretungen.Aktualisieren(d, von, bis);
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/veranstaltungen");

        g.MapGet("", (Arbeitsdokument a, DateOnly von, DateOnly? bis) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var b = bis is DateOnly bb && bb >= von ? bb : von;
            var heute = DateOnly.FromDateTime(DateTime.Today);
            return new
            {
                von, bis = b, letzteStunde = d.Zeitraster.StundenProTag,
                zeilen = d.Veranstaltungen.Where(v => v.Bis >= von && v.Von <= b).OrderBy(v => v.Von).ThenBy(v => v.Id).Select(v => Zeile(d, doc.Index, v, von, b, heute)).ToList(),
                gruende = d.Abwesenheitsgruende.Select(x => x.Kurzname).ToList(),
            };
        }));

        g.MapGet("/schuelergruppen", (Arbeitsdokument a) => a.LesenAsync(doc => VeranstaltungsLogik.Schuelergruppen(doc.Daten)));

        // Schnellauswahl: Gruppen eines Feldes (Alle …, nach Abteilung, Jahrgang, Klassengruppe, Raumgruppe …)
        g.MapGet("/gruppen", (Arbeitsdokument a, string feld, int? va) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var v = va is int id ? d.Veranstaltungen.FirstOrDefault(x => x.Id == id) : null;
            return VeranstaltungsAuswahl.Gruppen(d, VeranstaltungsAuswahl.TypVon(FeldVon(feld)), v).Select(x => new
            {
                name = x.Bereich.Length > 0 && x.Name.StartsWith(x.Bereich + " ") ? x.Name[(x.Bereich.Length + 1)..] : x.Name, bereich = x.Bereich, ids = x.Ids,
                anzahl = x.Ids.Count, hinweis = string.Join(", ", x.Ids.Take(40).Select(i => ix.K(x.Typ, i))) + (x.Ids.Count > 40 ? " …" : ""),
            }).ToList();
        }));

        // Eingabe in einer Zelle (neue Veranstaltung: ohne Id, am Datum)
        g.MapPost("/{id:int?}", async (Arbeitsdokument a, int? id, VaFeldAnfrage f, ClaimsPrincipal p) =>
        {
            var wert = (f.Wert ?? "").Trim();
            return await a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var d = doc.Daten;
                var alt = id is int i ? d.Veranstaltungen.FirstOrDefault(x => x.Id == i) ?? throw StudioFehler.NichtGefunden($"Die Veranstaltung Nr. {i}") : null;
                var start = f.Datum ?? alt?.Von ?? DateOnly.FromDateTime(DateTime.Today);
                var v = alt is null ? new Veranstaltung { Von = start, Bis = start, StundeVon = 1, StundeBis = 99 } : Kopie(alt);
                var letzte = Math.Max(1, d.Zeitraster.StundenProTag);
                var ersatz = new List<int>();
                DateOnly Datum(string s, DateOnly basis) => DateOnly.TryParseExact(s, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "d.M.yy" }, Format.De, System.Globalization.DateTimeStyles.None, out var x) ? x
                    : DateOnly.TryParseExact(s.TrimEnd('.'), new[] { "d.M" }, Format.De, System.Globalization.DateTimeStyles.None, out var y) ? new DateOnly(basis.Year, y.Month, y.Day)
                    : throw StudioFehler.Ungueltig($"Das Datum \"{s}\" ist ungültig.");
                try
                {
                    switch (f.Feld)
                    {
                        case "T": v.Text = wert; break;
                        case "V": v.Von = Datum(wert, v.Von); if (v.Bis < v.Von || alt is null) v.Bis = v.Von; break;
                        case "B": v.Bis = Datum(wert, v.Bis); if (v.Bis < v.Von) v.Von = v.Bis; break;
                        case "SV": v.StundeVon = wert.Length == 0 ? 1 : Math.Clamp(int.Parse(wert), 1, letzte); break;
                        case "SB": v.StundeBis = wert.Length == 0 || int.Parse(wert) >= letzte ? 99 : Math.Max(1, int.Parse(wert)); break;
                        case "K": v.KlassenIds = VeranstaltungsAuswahl.Lesen(d, ElementTyp.Klasse, wert, v.Von, v.Bis); v.KlassenteilIds.RemoveAll(v.KlassenIds.Contains); break;
                        case "KT": v.KlassenteilIds = VeranstaltungsAuswahl.Lesen(d, ElementTyp.Klasse, wert, v.Von, v.Bis); v.KlassenIds.RemoveAll(v.KlassenteilIds.Contains); break;
                        case "L":
                            var namen = VeranstaltungsAuswahl.Lesen(d, ElementTyp.Lehrer, wert.Replace("???", ""), v.Von, v.Bis);
                            if (alt != null && OffenerTag(alt, alt.Von, alt.Bis) is DateOnly tag)
                            {
                                // offene Plätze: neu eingetragene Lehrkräfte sind Ersatzlehrkräfte
                                var bisher = alt.LehrerAm(tag);
                                ersatz = namen.Where(n => !bisher.Contains(n) && !alt.LehrerIds.Contains(n)).ToList();
                                if (ersatz.Count == 0) v.LehrerIds = namen.Union(alt.Ersatz.Where(x => x.ErsatzId is null).Select(x => x.LehrerId)).Where(n => namen.Contains(n) || alt.LehrerIds.Contains(n)).Distinct().ToList();
                            }
                            else v.LehrerIds = namen;
                            break;
                        case "R": v.RaumIds = VeranstaltungsAuswahl.Lesen(d, ElementTyp.Raum, wert, v.Von, v.Bis); break;
                        case "G": v.GrundId = wert.Length == 0 ? null : d.Abwesenheitsgruende.FirstOrDefault(x => x.Kurzname.Equals(wert, StringComparison.OrdinalIgnoreCase))?.Id ?? throw StudioFehler.Ungueltig($"Abwesenheitsgrund \"{wert}\" ist nicht vorhanden."); break;
                        case "Z": v.Zaehlen = wert is "X" or "x" or "1" or "true" or "ja"; break;
                        case "SG": v.Schuelergruppe = wert; break;
                        default: throw StudioFehler.Ungueltig($"Das Feld \"{f.Feld}\" kann nicht bearbeitet werden.");
                    }
                }
                catch (FormatException ex)
                {
                    throw StudioFehler.Ungueltig(ex.Message.StartsWith("Input") || ex.Message.StartsWith("The input") ? $"Die Eingabe \"{wert}\" ist ungültig." : ex.Message);
                }
                if (alt is null && string.IsNullOrWhiteSpace(v.Text) && v.KlassenIds.Count == 0 && v.KlassenteilIds.Count == 0 && v.LehrerIds.Count == 0 && v.RaumIds.Count == 0)
                    return (new { ok = false, id = (int?)null }, null, "");
                var heute = DateOnly.FromDateTime(DateTime.Today);
                if (ersatz.Count > 0 && alt != null)
                {
                    var vid = alt.Id;
                    var tage = alt.Ersatz.Where(x => x.ErsatzId is null).OrderBy(x => x.Datum).ToList();
                    var zusaetzlich = ersatz.Skip(tage.Count).ToList();
                    doc.Aendern("Ersatzlehrkraft für Veranstaltung", AenderungsArt.Vertretung, x =>
                    {
                        var n = 0;
                        foreach (var e in tage) VeranstaltungsLogik.ErsatzSetzen(x, vid, e.Datum, e.LehrerId, ersatz[Math.Min(n++, ersatz.Count - 1)]);
                        if (zusaetzlich.Count > 0 && x.Veranstaltungen.FirstOrDefault(y => y.Id == vid) is { } va)
                        {
                            VeranstaltungsAuswahl.Uebernehmen(va, VeranstaltungsAuswahl.Feld.Lehrer, zusaetzlich);
                            Vertretungen.Aktualisieren(x, va.Von, va.Bis);
                        }
                    });
                    return (new { ok = true, id = (int?)vid }, AenderungsArt.Vertretung, "Ersatzlehrkraft für Veranstaltung");
                }
                var text = alt is null ? "Veranstaltung" : "Veranstaltung geändert";
                doc.Aendern(text, AenderungsArt.Vertretung, x => Speichern(x, v, alt));
                return (new { ok = true, id = (int?)v.Id }, AenderungsArt.Vertretung, text);
            });
        });

        g.MapPost("/schnell/{id:int?}", (Arbeitsdokument a, int? id, VaSchnellAnfrage s, ClaimsPrincipal p) =>
            a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var d = doc.Daten;
                var alt = id is int i ? d.Veranstaltungen.FirstOrDefault(x => x.Id == i) ?? throw StudioFehler.NichtGefunden($"Die Veranstaltung Nr. {i}") : null;
                var start = s.Datum ?? DateOnly.FromDateTime(DateTime.Today);
                var v = alt is null ? new Veranstaltung { Von = start, Bis = start, StundeVon = 1, StundeBis = 99 } : Kopie(alt);
                var n = VeranstaltungsAuswahl.Uebernehmen(v, FeldVon(s.Feld), s.Ids ?? new List<int>(), s.Ersetzen);
                if (n == 0 && !s.Ersetzen) return (new { ok = false, hinweis = "Diese Elemente sind bereits eingetragen.", id = alt?.Id }, null, "");
                if (alt is null && v.KlassenIds.Count == 0 && v.KlassenteilIds.Count == 0 && v.LehrerIds.Count == 0 && v.RaumIds.Count == 0) return (new { ok = false, hinweis = (string?)null, id = (int?)null }, null, "");
                doc.Aendern(alt is null ? "Veranstaltung" : "Veranstaltung geändert", AenderungsArt.Vertretung, x => Speichern(x, v, alt));
                return (new { ok = true, hinweis = (string?)null, id = (int?)v.Id }, AenderungsArt.Vertretung, "Veranstaltung geändert");
            }));

        g.MapPost("/{id:int}/ausstunden", (Arbeitsdokument a, int id, ClaimsPrincipal p) =>
            a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var d = doc.Daten;
                var alt = d.Veranstaltungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Veranstaltung Nr. {id}");
                var e = VeranstaltungsAuswahl.AusStunden(d, alt);
                if (e.Leer) return (new { ok = false, hinweis = "In den Stunden der Veranstaltung findet kein passender Unterricht statt." }, null, "");
                var v = Kopie(alt);
                if (VeranstaltungsAuswahl.AllesUebernehmen(v, e) == 0) return (new { ok = false, hinweis = "Alle Elemente der Stunden sind bereits eingetragen." }, null, "");
                doc.Aendern("Veranstaltung geändert", AenderungsArt.Vertretung, x => Speichern(x, v, alt));
                return (new { ok = true, hinweis = "" }, AenderungsArt.Vertretung, "Veranstaltung geändert");
            }));

        g.MapPost("/ausstunde", (Arbeitsdokument a, VaAusStunde s, ClaimsPrincipal p) =>
            a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var d = doc.Daten;
                var u = d.PeriodeAm(s.Datum).Unterrichte?.FirstOrDefault(x => x.Id == s.Unterricht) ?? d.Unterrichte.FirstOrDefault(x => x.Id == s.Unterricht)
                        ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {s.Unterricht}");
                var tag = Kalender.TagIndex(d, s.Datum);
                var pl = d.Stundenplan.FirstOrDefault(x => x.UnterrichtId == u.Id && x.Tag == tag && x.Stunde == s.Stunde);
                var alt = s.Va is int i ? d.Veranstaltungen.FirstOrDefault(x => x.Id == i) : null;
                var v = alt is null ? new Veranstaltung { Von = s.Datum, Bis = s.Datum, StundeVon = s.Stunde, StundeBis = s.Stunde } : Kopie(alt);
                if (VeranstaltungsAuswahl.AllesUebernehmen(v, VeranstaltungsAuswahl.AusUnterricht(d, u, pl)) == 0) return (new { ok = false, hinweis = "Alle Elemente der Stunde sind bereits eingetragen.", id = alt?.Id }, null, "");
                doc.Aendern(alt is null ? "Veranstaltung" : "Veranstaltung geändert", AenderungsArt.Vertretung, x => Speichern(x, v, alt));
                return (new { ok = true, hinweis = "", id = (int?)v.Id }, AenderungsArt.Vertretung, "Veranstaltung");
            }));

        g.MapPost("/{id:int}/kopieren", (Arbeitsdokument a, int id, VaKopieren k, ClaimsPrincipal p) =>
            a.AusfuehrenAsync<object>(B(p), doc =>
            {
                var d = doc.Daten;
                var va = d.Veranstaltungen.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Veranstaltung Nr. {id}");
                var neu = VeranstaltungsLogik.Kopieren(va, k.Datum);
                doc.Aendern("Veranstaltung", AenderungsArt.Vertretung, x => Speichern(x, neu, null));
                return (new { ok = true, id = neu.Id }, AenderungsArt.Vertretung, "Veranstaltung");
            }));

        g.MapPost("/loeschen", (Arbeitsdokument a, IdsAnfrage i, ClaimsPrincipal p) =>
            a.AendernAsync(B(p), i.Ids.Count > 1 ? $"Veranstaltungen löschen ({i.Ids.Count})" : "Veranstaltung löschen", AenderungsArt.Vertretung, d =>
            {
                var liste = d.Veranstaltungen.Where(v => i.Ids.Contains(v.Id)).ToList();
                if (liste.Count == 0) return;
                d.Veranstaltungen.RemoveAll(v => i.Ids.Contains(v.Id));
                Vertretungen.Aktualisieren(d, liste.Min(v => v.Von), liste.Max(v => v.Bis));
            }, _ => new { ok = true }));
    }
}
