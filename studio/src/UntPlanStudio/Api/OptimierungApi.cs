using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record OptimierungStart(OptimierungsOptionen? Optionen, bool Zuordnen = false);
public sealed record OptimierungUebernahme(int Nummer, bool TrotzAenderung = false);
public sealed record GewichtungWerte(Dictionary<string, int> Werte);

/// <summary>
/// Optimierung (Hintergrundauftrag mit Fortschritt, Abbrechen, Ergebnisvergleich, Übernehmen), Stundenplan-Diagnose
/// (Eingabedaten / Stundenplan mit Fällen) und Gewichtung (Regler 0–5, Analyse) – wie im Desktop (OptimierungDialog, DiagnoseFenster,
/// GewichtungDialog).
/// </summary>
public sealed class OptimierungApi : IStudioModul
{
    public string Name => "optimierung";

    public void DiensteRegistrieren(IServiceCollection dienste) => dienste.AddSingleton<Optimierungsdienst>();

    /// <summary>Zuordnung Regler → Verstoßarten (Analyse im Gewichtungsdialog des Desktops).</summary>
    private static readonly Dictionary<string, VerstossArt[]> Zuordnung = new()
    {
        ["L.Hohlstunden"] = new[] { VerstossArt.HohlstundenLehrer }, ["L.StdTag"] = new[] { VerstossArt.StdTagLehrer }, ["L.Mittagspause"] = new[] { VerstossArt.MittagspauseLehrer },
        ["L.Nachmittage"] = new[] { VerstossArt.NachmittageLehrer }, ["L.Unbestimmt"] = new[] { VerstossArt.UnbestimmterZeitwunsch }, ["L.Tagesbelastung"] = new[] { VerstossArt.Tagesbelastung },
        ["K.Hohlstunden"] = new[] { VerstossArt.HohlstundeKlasse }, ["K.Kernzeit"] = new[] { VerstossArt.Kernzeitverletzung }, ["K.StdTag"] = new[] { VerstossArt.StdTagKlasse },
        ["K.Mittagspause"] = new[] { VerstossArt.MittagspauseKlasse }, ["K.Nachmittage"] = new[] { VerstossArt.NachmittageKlasse }, ["F.ZweimalTag"] = new[] { VerstossArt.FachMehrmalsProTag },
        ["F.Doppelstunden"] = new[] { VerstossArt.DoppelstundenFehler, VerstossArt.BlockFehler }, ["F.Randstunde"] = new[] { VerstossArt.Randstunde },
        ["F.KeinNachmittag"] = new[] { VerstossArt.KeinNachmittag },
        ["H.ProTag"] = new[] { VerstossArt.HauptfaecherProTag }, ["H.Folge"] = new[] { VerstossArt.HauptfaecherFolge }, ["H.Spaet"] = new[] { VerstossArt.HauptfachSpaet },
        ["R.Raumoptimierung"] = new[] { VerstossArt.KeinRaum }, ["S.Verteilung"] = new[] { VerstossArt.Verteilung }, ["S.Spaet"] = new[] { VerstossArt.SpaeteStunde },
        ["Z.Lehrer"] = new[] { VerstossArt.ZeitwunschLehrer }, ["Z.Klassen"] = new[] { VerstossArt.ZeitwunschKlasse }, ["Z.Raeume"] = new[] { VerstossArt.ZeitwunschRaum },
        ["Z.Faecher"] = new[] { VerstossArt.ZeitwunschFach }, ["Z.Unterricht"] = new[] { VerstossArt.ZeitwunschUnterricht },
    };

    public static object KennzahlenDto(Kennzahlen? k) => k is null ? new { } : new
    {
        bewertung = k.Bewertung, nichtVerplant = k.NichtVerplant, hohlstundenKlassen = k.HohlstundenKlassen, kernzeitverletzungen = k.Kernzeitverletzungen,
        fachZweimalTag = k.FachZweimalTag, doppelstundenfehler = k.Doppelstundenfehler, zeitwunschverletzungen = k.Zeitwunschverletzungen,
        hohlstundenLehrer = k.HohlstundenLehrer, ohneRaum = k.OhneRaum, kollisionen = k.Kollisionen,
    };

    private static object Punkte(Schuldatei d, List<DiagnosePunkt> punkte)
    {
        var unt = d.Unterrichte.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        return punkte.Select(p => new
    {
        rubrik = p.Rubrik, gruppe = p.Gruppe, titel = p.Titel, beschreibung = p.Beschreibung, gewicht = p.Gewicht, anzahl = p.Faelle.Count,
        faelle = p.Faelle.Select(f => new
        {
            element = f.Element, u = f.UnterrichtId, zeit = f.Zeit, detail = f.Detail,
            typ = f.Typ is ElementTyp t && t is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach ? Typnamen.Schluessel(t) : null,
            id = f.Typ is ElementTyp.Klasse or ElementTyp.Lehrer or ElementTyp.Raum or ElementTyp.Fach ? f.ElementId : null,
            // für „Betroffenen Stundenplan anzeigen“ bei Unterrichts-Fällen: erste Klasse bzw. Lehrkraft
            klasse = f.UnterrichtId is int u && unt.TryGetValue(u, out var un) ? un.KlassenIds.Cast<int?>().FirstOrDefault() : null,
            lehrer = f.UnterrichtId is int u2 && unt.TryGetValue(u2, out var un2) ? un2.LehrerIds.Cast<int?>().FirstOrDefault() : null,
        }),
    }).ToList();
    }

    private static object AuftragDto(OptimierungsAuftrag? a, long version) => a is null ? new { status = "keiner" } : new
    {
        id = a.Id,
        status = a.Status,
        benutzer = a.Benutzer,
        start = a.Start,
        ende = a.Ende,
        dauer = Math.Round(((a.Ende ?? DateTime.Now) - a.Start).TotalSeconds, 1),
        anteil = Math.Round(a.Anteil, 4),
        phase = a.Phase,
        plan = a.Plan,
        plaene = a.Plaene == 0 ? a.Optionen.AnzahlPlaene : a.Plaene,
        kosten = Math.Round(a.Kosten),
        fehler = a.Fehler,
        strategie = StrategieInfo.Name(a.Optionen.Strategie),
        optionen = a.Optionen,
        zugeordnet = a.Zugeordnet,
        uebernommen = a.Uebernommen,
        dokumentGeaendert = version != a.BasisVersion,
        aktuell = KennzahlenDto(a.BasisKennzahlen),
        planstunden = a.BasisPlanstunden,
        ergebnisse = a.Ergebnisse?.Select(e => new
        {
            nummer = e.Nummer, kennzahlen = KennzahlenDto(e.Kennzahlen), dauer = Math.Round(e.Dauer.TotalSeconds, 1),
            planstunden = e.Plan.Count, abweichungen = a.Abweichungen.GetValueOrDefault(e.Nummer),
        }),
    };

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/optimierung");

        // ---- Diagnose ----
        g.MapGet("/kennzahlen", (Arbeitsdokument a) => a.LesenAsync(doc => KennzahlenDto(Bewertung.Kennzahlen(doc.Daten))));

        g.MapGet("/diagnose", (Arbeitsdokument a, string? rubrik) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var nurEingabe = string.Equals(rubrik, "eingabedaten", StringComparison.OrdinalIgnoreCase);
            var nurPlan = string.Equals(rubrik, "stundenplan", StringComparison.OrdinalIgnoreCase);
            return new
            {
                eingabedaten = nurPlan ? null : Punkte(d, Diagnose.Eingabedaten(d)),
                stundenplan = nurEingabe ? null : Punkte(d, Diagnose.Stundenplan(d)),
                kennzahlen = nurEingabe ? null : KennzahlenDto(Bewertung.Kennzahlen(d)),
                planstunden = d.Stundenplan.Count,
                periode = d.AktivePeriode.Name,
            };
        }));

        // ---- Gewichtung ----
        g.MapGet("/gewichtung", (Arbeitsdokument a) => a.LesenAsync(doc => new
        {
            karteikarten = Gewichtung.Karteikarten,
            regler = Gewichtung.Alle.Select(x => new
            {
                schluessel = x.Schluessel, karteikarte = x.Karteikarte, text = x.Text, standard = x.Standard,
                wert = doc.Daten.Gewichtung.TryGetValue(x.Schluessel, out var w) ? w : x.Standard,
            }),
            ziffern = Enumerable.Range(0, 6).Select(z => new { wert = z, text = Gewichtung.ZifferText(z) }),
        }));

        g.MapGet("/gewichtung/analyse", (Arbeitsdokument a) => a.LesenAsync(doc =>
        {
            var v = Bewertung.Verstoesse(doc.Daten);
            return Gewichtung.Alle.Select(x => new
            {
                schluessel = x.Schluessel, text = x.Text,
                wert = doc.Daten.Gewichtung.TryGetValue(x.Schluessel, out var w) ? w : x.Standard,
                verstoesse = Zuordnung.TryGetValue(x.Schluessel, out var arten) ? v.Count(y => arten.Contains(y.Art)) : (int?)null,
                kosten = Zuordnung.TryGetValue(x.Schluessel, out var arten2) ? Math.Round(v.Where(y => arten2.Contains(y.Art)).Sum(y => y.Kosten)) : (double?)null,
            });
        }));

        g.MapPut("/gewichtung", (Arbeitsdokument a, GewichtungWerte w, ClaimsPrincipal p) =>
        {
            var neu = new Dictionary<string, int>();
            foreach (var x in Gewichtung.Alle)
                neu[x.Schluessel] = Math.Clamp(w.Werte?.TryGetValue(x.Schluessel, out var z) == true ? z : x.Standard, 0, 5);
            var unbekannt = w.Werte?.Keys.Where(k => !Gewichtung.Standard.ContainsKey(k)).ToList() ?? new();
            if (unbekannt.Count > 0) throw StudioFehler.Ungueltig($"Unbekannter Regler: {string.Join(", ", unbekannt)}");
            return a.AendernAsync(p.Benutzername(), "Gewichtung", AenderungsArt.Einstellungen, d => d.Gewichtung = neu, doc => new { ok = true });
        });

        // ---- Optimierung ----
        g.MapGet("/strategien", () => Enum.GetValues<Strategie>().Select(s => new { wert = s.ToString(), text = StrategieInfo.Name(s) }));

        g.MapPost("/start", async (Arbeitsdokument a, OptimierungStart s, Optimierungsdienst dienst, ClaimsPrincipal p) =>
        {
            var o = s.Optionen ?? new OptimierungsOptionen();
            if (o.Seed == 0) o.Seed = Environment.TickCount;
            var auftrag = await dienst.StartenAsync(a, o, p.Benutzername(), s.Zuordnen);
            return AuftragDto(auftrag, a.Version);
        });

        g.MapGet("/auftrag", (Arbeitsdokument a, Optimierungsdienst dienst) => AuftragDto(dienst.Holen(a.Id), a.Version));

        g.MapPost("/auftrag/abbrechen", (Arbeitsdokument a, Optimierungsdienst dienst) => AuftragDto(dienst.Abbrechen(a.Id), a.Version));

        g.MapDelete("/auftrag", (Arbeitsdokument a, Optimierungsdienst dienst) =>
        {
            dienst.Verwerfen(a.Id);
            return new { ok = true };
        });

        g.MapPost("/auftrag/uebernehmen", async (Arbeitsdokument a, OptimierungUebernahme u, Optimierungsdienst dienst, ClaimsPrincipal p) =>
        {
            var auftrag = dienst.Holen(a.Id) ?? throw StudioFehler.NichtGefunden("Ein Optimierungsergebnis");
            if (auftrag.Laeuft) throw StudioFehler.Ungueltig("Die Optimierung läuft noch.");
            var e = auftrag.Ergebnisse?.FirstOrDefault(x => x.Nummer == u.Nummer) ?? throw StudioFehler.NichtGefunden($"Plan {u.Nummer}");
            if (a.Version != auftrag.BasisVersion && !u.TrotzAenderung)
                throw new StudioFehler(StatusCodes.Status409Conflict, "Das Dokument wurde seit dem Start der Optimierung geändert. Beim Übernehmen gehen diese Stundenplanänderungen verloren.");
            var uebernommen = await a.AendernAsync(p.Benutzername(), $"Optimierung (Plan {e.Nummer})", AenderungsArt.Stundenplan, d =>
            {
                var ids = d.Unterrichte.Select(x => x.Id).ToHashSet();
                d.Stundenplan.Clear();
                d.Stundenplan.AddRange(e.Plan.Where(x => ids.Contains(x.UnterrichtId)).Select(x => x.Kopie()));
            }, doc => doc.Daten.Stundenplan.Count);
            auftrag.Uebernommen = e.Nummer;
            auftrag.Status = "uebernommen";
            auftrag.BasisVersion = a.Version;
            return new { ok = true, planstunden = uebernommen, nummer = e.Nummer };
        });

        // Ergebnisvergleich: Plan n eines Elements (wie GET /stundenplan/{typ}/{id}, aber aus dem Ergebnis)
        g.MapGet("/auftrag/plan/{nummer:int}/{typ}/{id:int}", (Arbeitsdokument a, int nummer, string typ, int id, Optimierungsdienst dienst) =>
        {
            var auftrag = dienst.Holen(a.Id) ?? throw StudioFehler.NichtGefunden("Ein Optimierungsergebnis");
            var t = Typnamen.Typ(typ);
            return Optimierungsdienst.MitVorschau(auftrag, nummer, doc =>
            {
                var e = doc.Daten.AlleElemente(t).FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)} Nr. {id}");
                var ix = doc.Index;
                return new
                {
                    element = new { id = e.Id, typ = Typnamen.Schluessel(t), kurz = e.Kurzname, lang = e is Lehrer l ? l.VollerName : e.Langname },
                    zeitraster = DokumenteApi.Zeitraster(doc.Daten),
                    zellen = StundenplanApi.Zellen(doc, t, id),
                    nichtVerplant = ix.UnterrichteVon(t, id).Where(u => ix.NichtVerplant(u) > 0)
                        .Select(u => new { u = u.Id, text = ix.UnterrichtText(u), anzahl = ix.NichtVerplant(u) }),
                    periode = $"Plan {nummer}",
                };
            });
        });
    }
}
