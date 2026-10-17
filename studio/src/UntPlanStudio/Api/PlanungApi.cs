using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record StundenRef(int Unterricht, int Tag, int Stunde);
public sealed record StundenListe(List<StundenRef> Stunden, bool Fixieren = false, bool AuchFixierte = false);
public sealed record AlleEntplanen(bool FixierteBehalten = true);
public sealed record AlleFixieren(bool Fixieren = true);
/// <summary>Verschieben einer Stunde bzw. ihres Blocks. aktion: auto (Tausch, sonst Frage), entplanen (behindernde entplanen), kollision (trotzdem).</summary>
public sealed record BlockBewegung(int Unterricht, int Tag, int Stunde, int ZielTag, int ZielStunde, bool Einzeln = false, string? Aktion = null);
/// <summary>Nicht verplante Stunden (Block der Länge <see cref="Laenge"/>) setzen; aktion wie bei <see cref="BlockBewegung"/>.</summary>
public sealed record BlockSetzen(int Unterricht, int Tag, int Stunde, int Laenge = 1, string? Aktion = null, bool Fixieren = false);
/// <summary>Raum zuordnen (raum = Id) bzw. löschen (raum = null). umfang: 0 Stunde, 1 Block, 2 alle Stunden des Unterrichts.</summary>
public sealed record RaumZuordnung(int Unterricht, int Tag, int Stunde, int Zeile = 0, int Umfang = 1, int? Raum = null, string? Loesung = null, bool TrotzGesperrt = false);
public sealed record Stundentausch2(StundenRef A, StundenRef B);

/// <summary>
/// Planung im Stundenplanfenster (wie im Desktop): Zielstatus mit Kollisionsgründen beim Ziehen, Blöcke/Doppelstunden verschieben
/// bzw. setzen mit „Soll behindernder Unterricht entplant werden?“, Mehrfachauswahl (entplanen, fixieren), alle entplanen/fixieren,
/// Raum zuordnen/löschen (Raumliste), Tauschvorschläge und Tausch.
/// </summary>
public sealed class PlanungApi : IStudioModul
{
    public string Name => "planung";

    private sealed record Pruefung(string Art, object? Antwort, int Start);

    private static Planstunde Stunde(Schuldatei d, int unterricht, int tag, int stunde) =>
        d.Stundenplan.FirstOrDefault(p => p.UnterrichtId == unterricht && p.Tag == tag && p.Stunde == stunde)
        ?? throw StudioFehler.NichtGefunden($"Die Stunde von U-Nr. {unterricht} am Tag {tag + 1}, Stunde {stunde}");

    private static Unterricht Unt(Schuldatei d, int id) =>
        d.Unterrichte.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {id}");

    private static List<Planstunde> Gezogen(Schuldatei d, Planstunde p, bool einzeln) =>
        einzeln ? new List<Planstunde> { p } : Planungsdienst.BlockVon(d, p);

    private static string Grund(Schuldatei d, DatenIndex ix, Unterricht u, int tag, int stunde, int laenge, ICollection<Planstunde>? ignorieren, Feldstatus st, IReadOnlyCollection<Planstunde>? gezogen)
    {
        switch (st)
        {
            case Feldstatus.Frei:
                return "";
            case Feldstatus.KeinRaum:
                return "Kein Raum frei (Wunschraum und Ausweichräume belegt)";
            case Feldstatus.TauschMoeglich:
                if (gezogen != null && Planungsdienst.TauschPruefen(d, ix, gezogen, tag, stunde, out _) is { } t) return "Tausch: " + t.Beschreibung;
                var h1 = Planungsdienst.Hindernisse(d, ix, u, tag, stunde, ignorieren);
                return h1.Count > 0 ? $"Tausch mit U-Nr. {h1[0].Unterricht.Id} {ix.UnterrichtText(h1[0].Unterricht)}" : "Tausch möglich";
            case Feldstatus.VorgabeVerletzt:
                var gruende = new List<string>();
                for (var j = 0; j < laenge; j++)
                {
                    var s = stunde + j;
                    if (!d.Zeitraster.Verfuegbar(tag, s)) gruende.Add("außerhalb des Zeitrasters");
                    if (u.Zeitwuensche.Get(tag, s) < 0) gruende.Add($"Zeitwunsch Unterricht {u.Zeitwuensche.Get(tag, s)}");
                    foreach (var l in u.LehrerIds) if (ix.Lehrer.TryGetValue(l, out var le) && le.Zeitwuensche.Get(tag, s) < 0) gruende.Add($"Zeitwunsch {le.Kurzname} {le.Zeitwuensche.Get(tag, s)}");
                    foreach (var k in u.KlassenIds) if (ix.Klassen.TryGetValue(k, out var kl) && kl.Zeitwuensche.Get(tag, s) < 0) gruende.Add($"Zeitwunsch {kl.Kurzname} {kl.Zeitwuensche.Get(tag, s)}");
                    foreach (var f in u.FachIds)
                        if (ix.Faecher.TryGetValue(f, out var fa))
                        {
                            if (fa.Zeitwuensche.Get(tag, s) < 0) gruende.Add($"Zeitwunsch Fach {fa.Kurzname} {fa.Zeitwuensche.Get(tag, s)}");
                            if (fa.KeinNachmittag && d.Zeitraster.Art(tag, s) == Tageszeit.Nachmittag) gruende.Add($"{fa.Kurzname}: kein Nachmittag");
                        }
                }
                return string.Join(", ", gruende.Distinct());
            default:
                var hind = new List<string>();
                if (stunde < 1 || stunde + laenge - 1 > d.Zeitraster.StundenProTag) return "passt nicht in das Zeitraster";
                for (var j = 0; j < laenge; j++)
                    foreach (var h in Planungsdienst.Hindernisse(d, ix, u, tag, stunde + j, ignorieren))
                        hind.Add($"U-Nr. {h.Unterricht.Id} ({h.Grund}){(h.Planstunde.Fixiert ? " fixiert" : "")}");
                if (hind.Count > 0) return "Kollision: " + string.Join(", ", hind.Distinct().Take(4)) + (hind.Distinct().Count() > 4 ? " …" : "");
                if (gezogen != null && Planungsdienst.TauschPruefen(d, ix, gezogen, tag, stunde, out var grund) is null && grund.Length > 0) return grund;
                return "";
        }
    }

    private static object Frage(Schuldatei d, DatenIndex ix, List<Hindernis> hind, string anlass) => new
    {
        ok = false,
        frage = Behinderung.Fragetext(d, ix, hind, anlass),
        hindernisse = hind.Select(h => new { u = h.Unterricht.Id, tag = h.Planstunde.Tag, stunde = h.Planstunde.Stunde, grund = h.Grund, fixiert = h.Planstunde.Fixiert }),
    };

    private static void BlockAnsZiel(Schuldatei d, List<Planstunde> block, int zielTag, int zielStart, bool behinderndeEntplanen)
    {
        var ix = new DatenIndex(d);
        var u = ix.Unterrichte[block[0].UnterrichtId];
        if (behinderndeEntplanen)
            Behinderung.Entplanen(d, Behinderung.AnStellen(d, ix, u, block.Select((_, i) => (zielTag, zielStart + i)), block));
        var sortiert = block.OrderBy(p => p.Stunde).ToList();
        for (var i = 0; i < sortiert.Count; i++)
        {
            sortiert[i].Tag = zielTag;
            sortiert[i].Stunde = zielStart + i;
        }
        ix = new DatenIndex(d);
        foreach (var p in sortiert) p.Raeume = Planungsdienst.RaeumeSuchen(d, ix, u, p.Tag, p.Stunde, new[] { p });
    }

    private static object RaumDialog(Dokument doc, Planstunde p, int zeile, int umfang)
    {
        var d = doc.Daten;
        var ix = doc.Index;
        var u = ix.Unterrichte[p.UnterrichtId];
        zeile = Math.Clamp(zeile, 0, Math.Max(0, u.Zeilen.Count - 1));
        var stunden = Planungsdienst.RaumStunden(d, p, umfang);
        string Ug(Kopplungszeile z) => (z.UGruppeId ?? u.UGruppeId) is int g ? d.UGruppen.FirstOrDefault(x => x.Id == g)?.Kurzname ?? "" : "";
        return new
        {
            unterricht = new { id = u.Id, text = ix.UnterrichtText(u) },
            zeile,
            umfang,
            stunden = stunden.Select(x => new { tag = x.Tag, stunde = x.Stunde, text = $"{Planungsdienst.Tagname(d, x.Tag)}-{x.Stunde}", raum = ix.KRaum(x.RaumFuerZeile(zeile)) }),
            zeilen = u.Zeilen.Select((z, i) => new
            {
                nr = i, lehrer = ix.KLehrer(z.LehrerId), fach = ix.KFach(z.FachId), raum = ix.KRaum(p.RaumFuerZeile(i)), klassen = ix.KKlassen(z.KlassenIds),
                wunschraum = z.Wunschraum.HasValue ? ix.KRaum(z.Wunschraum) : ix.K(ElementTyp.Raumgruppe, z.Wunschraumgruppe), ugruppe = Ug(z),
            }),
            raeume = Planungsdienst.RaumUebersicht(d, ix, stunden, zeile).Select(z => new
            {
                id = z.Raum.Id, kurz = z.Raum.Kurzname, lang = z.Raum.Langname, kap = z.Raum.Kapazitaet, ausweich = z.Ausweichraum, stamm = z.Stammraum,
                wunsch = z.Wunsch, besetzt = z.Belegt.Count > 0,
                besetztDurch = string.Join("; ", z.Belegt.Select(b => ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? $"{ix.UnterrichtText(o)}" : "").Distinct()),
                raumgruppen = z.Raumgruppen, gang = z.Gang, wochenbelegung = z.Wochenbelegung, frei = z.StundenFrei, gesamt = z.StundenGesamt,
                gesperrt = z.Gesperrt, kapDiff = z.KapazitaetDiff,
            }),
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/planung");

        // Zielstatus beim Ziehen (Block bzw. einzelne Stunde, gezogen an der Stunde tag/stunde) mit Gründen je Feld.
        // Ohne tag/stunde: nicht verplante Stunden des Unterrichts als Block der Länge „laenge“.
        g.MapGet("/zielstatus", (Arbeitsdokument a, int unterricht, int? tag, int? stunde, bool? einzeln, int? laenge) => a.LesenAsync(doc =>
        {
            var d = doc.Daten;
            var ix = doc.Index;
            var u = ix.Unterrichte.GetValueOrDefault(unterricht) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {unterricht}");
            var z = d.Zeitraster;
            List<Planstunde>? gezogen = null;
            var versatz = 0;
            var n = Math.Clamp(laenge ?? 1, 1, z.StundenProTag);
            if (tag is int tg && stunde is int st)
            {
                var p = Stunde(d, unterricht, tg, st);
                gezogen = Gezogen(d, p, einzeln == true).OrderBy(x => x.Stunde).ToList();
                versatz = p.Stunde - gezogen[0].Stunde;
                n = gezogen.Count;
            }
            var status = new string[z.TageProWoche][];
            var gruende = new string[z.TageProWoche][];
            for (var t = 0; t < z.TageProWoche; t++)
            {
                status[t] = new string[z.StundenProTag];
                gruende[t] = new string[z.StundenProTag];
                for (var s = 1; s <= z.StundenProTag; s++)
                {
                    var start = s - versatz;
                    Feldstatus fs;
                    if (start < 1 || start + n - 1 > z.StundenProTag) fs = Feldstatus.Kollision;
                    else fs = gezogen is null ? Planungsdienst.Status(d, ix, u, t, start, n) : Planungsdienst.Zielstatus(d, ix, gezogen, t, start);
                    status[t][s - 1] = fs.ToString();
                    var eigen = gezogen != null && gezogen[0].Tag == t && gezogen[0].Stunde == start;
                    gruende[t][s - 1] = eigen ? "aktuelle Position" : Grund(d, ix, u, t, start, n, gezogen, fs, gezogen);
                }
            }
            return new { status, gruende, laenge = n, versatz };
        }));

        // Stunde bzw. Block verschieben (Tausch, falls geprüft möglich; sonst Frage nach behinderndem Unterricht)
        g.MapPost("/verschieben", async (Arbeitsdokument a, BlockBewegung b, ClaimsPrincipal p) =>
        {
            var aktion = (b.Aktion ?? "auto").ToLowerInvariant();
            var pruefung = await a.LesenAsync(doc =>
            {
                var d = doc.Daten;
                var ix = doc.Index;
                var pl = Stunde(d, b.Unterricht, b.Tag, b.Stunde);
                var block = Gezogen(d, pl, b.Einzeln).OrderBy(x => x.Stunde).ToList();
                var start = b.ZielStunde - (pl.Stunde - block[0].Stunde);
                if (b.ZielTag < 0 || b.ZielTag >= d.Zeitraster.TageProWoche || start < 1 || start + block.Count - 1 > d.Zeitraster.StundenProTag)
                    throw StudioFehler.Ungueltig("Die Stunden passen an dieser Position nicht in das Zeitraster.");
                if (block.Any(x => x.Fixiert)) throw StudioFehler.Ungueltig("Die Stunde ist fixiert.");
                if (block[0].Tag == b.ZielTag && block[0].Stunde == start) return new Pruefung("nichts", new { ok = true, unveraendert = true }, start);
                var u = ix.Unterrichte[b.Unterricht];
                var hind = Behinderung.AnStellen(d, ix, u, block.Select((_, i) => (b.ZielTag, start + i)), block);
                if (hind.Count == 0) return new Pruefung("frei", null, start);
                if (aktion == "auto" && Planungsdienst.TauschPruefen(d, ix, block, b.ZielTag, start, out _) != null) return new Pruefung("tausch", null, start);
                if (aktion == "auto") return new Pruefung("frage", Frage(d, ix, hind, $"U-Nr. {u.Id} {ix.UnterrichtText(u)} nach {Planungsdienst.Tagname(d, b.ZielTag)}-{start} verschieben"), start);
                return new Pruefung(aktion == "entplanen" ? "entplanen" : "kollision", null, start);
            });
            if (pruefung.Art is "nichts" or "frage") return pruefung.Antwort!;
            var text = pruefung.Art == "tausch" ? $"U-Nr. {b.Unterricht} getauscht" : $"U-Nr. {b.Unterricht} verschoben";
            return await a.AendernAsync(p.Benutzername(), text, AenderungsArt.Stundenplan, d =>
            {
                var pl = Stunde(d, b.Unterricht, b.Tag, b.Stunde);
                var block = Gezogen(d, pl, b.Einzeln).OrderBy(x => x.Stunde).ToList();
                if (pruefung.Art == "tausch")
                {
                    var ix = new DatenIndex(d);
                    var t = Planungsdienst.TauschPruefen(d, ix, block, b.ZielTag, pruefung.Start, out var grund) ?? throw StudioFehler.Ungueltig(grund);
                    Planungsdienst.Tauschen(d, t);
                }
                else BlockAnsZiel(d, block, b.ZielTag, pruefung.Start, pruefung.Art == "entplanen");
            }, _ => (object)new { ok = true, art = pruefung.Art });
        });

        // Nicht verplante Stunde(n) als Block setzen
        g.MapPost("/verplanen", async (Arbeitsdokument a, BlockSetzen s, ClaimsPrincipal p) =>
        {
            var aktion = (s.Aktion ?? "auto").ToLowerInvariant();
            var frage = await a.LesenAsync(doc =>
            {
                var d = doc.Daten;
                var ix = doc.Index;
                var u = ix.Unterrichte.GetValueOrDefault(s.Unterricht) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {s.Unterricht}");
                var n = Math.Clamp(s.Laenge, 1, d.Zeitraster.StundenProTag);
                if (s.Tag < 0 || s.Tag >= d.Zeitraster.TageProWoche || s.Stunde < 1 || s.Stunde + n - 1 > d.Zeitraster.StundenProTag)
                    throw StudioFehler.Ungueltig("Die Stunden passen an dieser Position nicht in das Zeitraster.");
                if (ix.NichtVerplant(u) < n) throw StudioFehler.Ungueltig(ix.NichtVerplant(u) == 0 ? $"U-Nr. {u.Id} ist bereits vollständig verplant." : $"Von U-Nr. {u.Id} sind nur noch {ix.NichtVerplant(u)} Stunden nicht verplant.");
                if (d.Stundenplan.Any(x => x.UnterrichtId == u.Id && x.Tag == s.Tag && x.Stunde >= s.Stunde && x.Stunde < s.Stunde + n))
                    throw StudioFehler.Ungueltig($"U-Nr. {u.Id} liegt zu dieser Zeit schon im Plan.");
                if (aktion != "auto") return null;
                var hind = Behinderung.AnStellen(d, ix, u, Enumerable.Range(s.Stunde, n).Select(x => (s.Tag, x)));
                return hind.Count == 0 ? null : Frage(d, ix, hind, $"U-Nr. {u.Id} {ix.UnterrichtText(u)} an {Planungsdienst.Tagname(d, s.Tag)}-{s.Stunde} setzen");
            });
            if (frage != null) return frage;
            return await a.AendernAsync(p.Benutzername(), s.Laenge > 1 ? $"U-Nr. {s.Unterricht}: Block verplant" : $"U-Nr. {s.Unterricht} verplant", AenderungsArt.Stundenplan, d =>
            {
                var u = Unt(d, s.Unterricht);
                var n = Math.Clamp(s.Laenge, 1, d.Zeitraster.StundenProTag);
                if (aktion == "entplanen")
                {
                    var ix = new DatenIndex(d);
                    Behinderung.Entplanen(d, Behinderung.AnStellen(d, ix, u, Enumerable.Range(s.Stunde, n).Select(x => (s.Tag, x))));
                }
                for (var i = 0; i < n; i++) Planungsdienst.Verplanen(d, u, s.Tag, s.Stunde + i, behinderndeEntplanen: false, s.Fixieren);
            }, _ => (object)new { ok = true });
        });

        // Mehrfachauswahl: markierte Stunden entplanen / fixieren (eine Rückgängig-Stufe)
        g.MapPost("/entplanen", (Arbeitsdokument a, StundenListe l, ClaimsPrincipal p) =>
        {
            if (l.Stunden is not { Count: > 0 }) throw StudioFehler.Ungueltig("Keine Stunden gewählt.");
            return a.AendernAsync(p.Benutzername(), l.Stunden.Count > 1 ? $"{l.Stunden.Count} Stunden entplant" : "Stunde entplant", AenderungsArt.Stundenplan, d =>
            {
                var liste = l.Stunden.Select(s => Stunde(d, s.Unterricht, s.Tag, s.Stunde)).Distinct().ToList();
                if (!l.AuchFixierte && liste.Any(x => x.Fixiert)) throw StudioFehler.Ungueltig("Unter den gewählten Stunden sind fixierte Stunden.");
                foreach (var x in liste) Planungsdienst.Entplanen(d, x);
            }, doc => new { ok = true, anzahl = l.Stunden.Count });
        });

        g.MapPost("/fixieren", (Arbeitsdokument a, StundenListe l, ClaimsPrincipal p) =>
        {
            if (l.Stunden is not { Count: > 0 }) throw StudioFehler.Ungueltig("Keine Stunden gewählt.");
            return a.AendernAsync(p.Benutzername(), l.Fixieren ? "Stunden fixieren" : "Fixierung lösen", AenderungsArt.Stundenplan, d =>
            {
                foreach (var s in l.Stunden) Stunde(d, s.Unterricht, s.Tag, s.Stunde).Fixiert = l.Fixieren;
            }, doc => new { ok = true, anzahl = l.Stunden.Count });
        });

        g.MapPost("/alle-entplanen", (Arbeitsdokument a, AlleEntplanen e, ClaimsPrincipal p) =>
        {
            var vorher = 0;
            return a.AendernAsync(p.Benutzername(), "Alle Stunden entplanen", AenderungsArt.Stundenplan, d =>
            {
                vorher = d.Stundenplan.Count;
                Planungsdienst.AlleEntplanen(d, e.FixierteBehalten);
            }, doc => new { ok = true, entplant = vorher - doc.Daten.Stundenplan.Count, verbleibend = doc.Daten.Stundenplan.Count });
        });

        g.MapPost("/alle-fixieren", (Arbeitsdokument a, AlleFixieren e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), e.Fixieren ? "Alle Stunden fixieren" : "Fixierungen aufheben", AenderungsArt.Stundenplan,
                d => d.Stundenplan.ForEach(x => x.Fixiert = e.Fixieren), doc => new { ok = true, anzahl = doc.Daten.Stundenplan.Count }));

        // ---- Raum zuordnen / löschen ----
        g.MapGet("/raum", (Arbeitsdokument a, int unterricht, int tag, int stunde, int? zeile, int? umfang) => a.LesenAsync(doc =>
            RaumDialog(doc, Stunde(doc.Daten, unterricht, tag, stunde), zeile ?? 0, Math.Clamp(umfang ?? 1, 0, 2))));

        g.MapPost("/raum", async (Arbeitsdokument a, RaumZuordnung z, ClaimsPrincipal p) =>
        {
            var umfang = Math.Clamp(z.Umfang, 0, 2);
            Planungsdienst.RaumKonfliktLoesung? loesung = (z.Loesung ?? "").ToLowerInvariant() switch
            {
                "verdraengen" => Planungsdienst.RaumKonfliktLoesung.Verdraengen,
                "tauschen" => Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen,
                "kollision" => Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen,
                _ => null,
            };
            if (z.Raum is int rid)
            {
                var frage = await a.LesenAsync(doc =>
                {
                    var d = doc.Daten;
                    var ix = doc.Index;
                    var pl = Stunde(d, z.Unterricht, z.Tag, z.Stunde);
                    if (!ix.Raeume.ContainsKey(rid)) throw StudioFehler.NichtGefunden($"Raum Nr. {rid}");
                    var stunden = Planungsdienst.RaumStunden(d, pl, umfang);
                    var zeileRaum = Planungsdienst.RaumUebersicht(d, ix, stunden, z.Zeile).FirstOrDefault(x => x.Raum.Id == rid);
                    if (zeileRaum is null) return null;
                    if (zeileRaum.Belegt.Count > 0 && loesung is null)
                        return (object)new
                        {
                            ok = false, art = "belegt",
                            frage = $"Raum {zeileRaum.Raum.Kurzname} ist belegt:\n" + string.Join("\n", zeileRaum.Belegt.Select(b => $"{Planungsdienst.Tagname(d, b.Tag)}-{b.Stunde}: {(ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? ix.UnterrichtText(o) : "")}").Distinct()),
                            optionen = new[] { new { wert = "verdraengen", text = "Behindernden Unterricht aus dem Raum verdrängen" }, new { wert = "tauschen", text = "Räume tauschen" }, new { wert = "kollision", text = "Kollision zulassen" } },
                        };
                    if (zeileRaum.Gesperrt > 0 && !z.TrotzGesperrt)
                        return new { ok = false, art = "gesperrt", frage = $"Raum {zeileRaum.Raum.Kurzname} ist in {zeileRaum.Gesperrt} der Stunden gesperrt. Trotzdem zuordnen?", optionen = Array.Empty<object>() };
                    return null;
                });
                if (frage != null) return frage;
            }
            return await a.AendernAsync(p.Benutzername(), z.Raum is null ? "Raum löschen" : "Raum zuordnen", AenderungsArt.Stundenplan, d =>
            {
                var pl = Stunde(d, z.Unterricht, z.Tag, z.Stunde);
                var u = Unt(d, z.Unterricht);
                if (z.Zeile < 0 || z.Zeile >= Math.Max(1, u.Zeilen.Count)) throw StudioFehler.Ungueltig("Ungültige Kopplungszeile.");
                Planungsdienst.RaumZuordnen(d, Planungsdienst.RaumStunden(d, pl, umfang), z.Zeile, z.Raum, loesung ?? Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen);
            }, doc => RaumDialog(doc, Stunde(doc.Daten, z.Unterricht, z.Tag, z.Stunde), z.Zeile, umfang));
        });

        // ---- Tauschvorschläge (Bewertung auf einer Kopie außerhalb der Sperre) ----
        g.MapGet("/tausch", async (Arbeitsdokument a, int unterricht, int tag, int stunde) =>
        {
            var kopie = await a.LesenAsync(doc =>
            {
                Stunde(doc.Daten, unterricht, tag, stunde);
                return Dokument.Klonen(doc.Daten);
            });
            return await Task.Run(() =>
            {
                var p = Stunde(kopie, unterricht, tag, stunde);
                return Planungsdienst.Tauschvorschlaege(kopie, p).Select(v => new
                {
                    gewinn = Math.Round(v.Gewinn), beschreibung = v.Beschreibung,
                    a = new StundenRef(v.A.UnterrichtId, v.A.Tag, v.A.Stunde),
                    b = new StundenRef(v.B.UnterrichtId, v.B.Tag, v.B.Stunde),
                }).ToList();
            });
        });

        g.MapPost("/tauschen", (Arbeitsdokument a, Stundentausch2 t, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Tauschvorschlag", AenderungsArt.Stundenplan, d =>
            {
                var pa = Stunde(d, t.A.Unterricht, t.A.Tag, t.A.Stunde);
                var pb = Stunde(d, t.B.Unterricht, t.B.Tag, t.B.Stunde);
                if (pa.Fixiert || pb.Fixiert) throw StudioFehler.Ungueltig("Fixierte Stunden können nicht getauscht werden.");
                (pa.Tag, pb.Tag) = (pb.Tag, pa.Tag);
                (pa.Stunde, pb.Stunde) = (pb.Stunde, pa.Stunde);
                var ix = new DatenIndex(d);
                pa.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pa.UnterrichtId], pa.Tag, pa.Stunde, new[] { pa });
                pb.Raeume = Planungsdienst.RaeumeSuchen(d, ix, ix.Unterrichte[pb.UnterrichtId], pb.Tag, pb.Stunde, new[] { pb });
            }, _ => new { ok = true }));
    }
}
