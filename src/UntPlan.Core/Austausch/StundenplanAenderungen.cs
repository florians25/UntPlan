using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using UntPlan.Web.Contracts;

namespace UntPlan.Core.Austausch;

/// <summary>Spalte „Art“ im Fenster „Stundenplanänderungen aus UntPlanWeb“.</summary>
public enum StplAenderungsArt
{
    /// <summary>Änderung eines bestehenden Unterrichts (Raumänderung) → Raumvertretung.</summary>
    Unterricht,
    /// <summary>Zusätzlicher Unterricht bzw. Buchung ohne Unterricht → Sondereinsatz.</summary>
    Aktivitaet,
    /// <summary>Prüfung → Klausur.</summary>
    Pruefung,
}

/// <summary>Eine Zeile im Fenster „Stundenplanänderungen aus UntPlanWeb“.</summary>
public sealed record StplAenderung(WebRueckmeldung Meldung, DateOnly Datum, int StundeVon, int StundeBis, string Zeit, StplAenderungsArt Art, int? UnterrichtId, int? ZeilenIndex,
    string KlassenFach, string Lehrer, string Raeume, string Wert, string Statistik, string Text, string Ersteller, bool? Uebernommen, string? Fehler)
{
    public string Id => Meldung.Id;
    public string Stunde => StundeVon == StundeBis ? StundeVon.ToString() : $"{StundeVon}-{StundeBis}";
    public string ArtText => Art switch { StplAenderungsArt.Unterricht => "Unterricht", StplAenderungsArt.Aktivitaet => "Aktivität", _ => "Prüfung" };
    /// <summary>Bereits übernommen oder ignoriert.</summary>
    public bool Erledigt => Uebernommen.HasValue;
}

/// <summary>
/// Stundenplanänderungen aus UntPlanWeb: Raumbuchungen, zusätzliche Unterrichte (Aktivitäten) und Prüfungen. Quelle sind die
/// Rückmeldungen (<see cref="UntPlanWebExport.RueckmeldungenAsync"/>). Beim Übernehmen entsteht eine Raumvertretung
/// („(Benutzer) Raumänderung“), ein Sondereinsatz oder eine Klausur; übernommene und ignorierte Änderungen werden in der
/// Datei vermerkt und nicht erneut importiert.
/// </summary>
public static class StundenplanAenderungen
{
    public static readonly string[] Typen = { WebRueckmeldungsTypen.Raumbuchung, WebRueckmeldungsTypen.Klausur };

    private static int? Id(Schuldatei d, ElementTyp t, string? k) =>
        string.IsNullOrWhiteSpace(k) ? null : d.AlleElemente(t).FirstOrDefault(e => e.Kurzname.Equals(k.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>
    /// Rückmeldungen vom Server und bereits bearbeitete Änderungen der Datei zu Zeilen aufbereiten (nach Datum und Stunde sortiert).
    /// </summary>
    public static List<StplAenderung> Analysieren(Schuldatei d, IEnumerable<WebRueckmeldung> rueckmeldungen, DateOnly von, DateOnly bis, bool erledigteAnzeigen)
    {
        var protokoll = d.Einstellungen.Portal.StplAenderungen.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.Last());
        var liste = rueckmeldungen.Where(m => Typen.Contains(m.Typ)).GroupBy(m => m.Id).Select(g => g.First()).ToList();
        if (erledigteAnzeigen) liste.AddRange(protokoll.Values.Where(p => liste.All(m => m.Id != p.Id)).Select(p => p.Meldung));
        var res = new List<StplAenderung>();
        foreach (var m in liste)
        {
            protokoll.TryGetValue(m.Id, out var p);
            if (p != null && !erledigteAnzeigen) continue;
            var z = Zeile(d, m, p?.Uebernommen);
            if (z.Datum < von || z.Datum > bis) continue;
            res.Add(z);
        }
        return res.OrderBy(z => z.Datum).ThenBy(z => z.StundeVon).ThenBy(z => z.Id).ToList();
    }

    /// <summary>Eine Rückmeldung einordnen (Unterricht / Aktivität / Prüfung).</summary>
    public static StplAenderung Zeile(Schuldatei d, WebRueckmeldung m, bool? uebernommen = null)
    {
        var ix = new DatenIndex(d);
        string? fehler = null;
        if (!DateOnly.TryParse(m.Von, System.Globalization.CultureInfo.InvariantCulture, out var datum)) { fehler = "ungültiges Datum"; datum = DateOnly.MinValue; }
        else if (!Kalender.IstUnterrichtstag(d, datum)) fehler = "kein Unterrichtstag";
        var spt = Math.Max(1, d.Zeitraster.StundenProTag);
        var von = Math.Clamp(m.StundeVon ?? 1, 1, spt);
        var bis = Math.Clamp(m.StundeBis ?? von, von, spt);
        var z1 = d.Zeitraster.Zeit(von);
        var z2 = d.Zeitraster.Zeit(bis);
        var zeit = z1 != null && z2 != null ? $"{z1.Beginn:HH\\:mm} - {z2.Ende:HH\\:mm}" : "";
        var raeume = m.Raeume.Append(m.Raum ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var lehrerNamen = new[] { m.Lehrer ?? "" }.Concat(m.WeitereLehrer).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        foreach (var k in m.Klassen) if (Id(d, ElementTyp.Klasse, k) is null) fehler ??= $"Klasse {k} unbekannt";
        foreach (var r in raeume) if (Id(d, ElementTyp.Raum, r) is null) fehler ??= $"Raum {r} unbekannt";
        foreach (var l in lehrerNamen) if (Id(d, ElementTyp.Lehrer, l) is null) fehler ??= $"Lehrer {l} unbekannt";
        var art = m.Typ == WebRueckmeldungsTypen.Klausur ? StplAenderungsArt.Pruefung : StplAenderungsArt.Aktivitaet;
        int? uid = null, zeile = null;
        var fach = m.Fach;
        var klassen = m.Klassen.ToList();
        if (art == StplAenderungsArt.Aktivitaet && fehler is null)
        {
            // Bezieht sich die Buchung auf einen bestehenden Unterricht der Lehrkraft? Dann ist es eine Raumänderung.
            var lehrer = Id(d, ElementTyp.Lehrer, m.Lehrer);
            var tp = Tagesplan.Berechnen(d, datum);
            var e = tp.Stunde(von).FirstOrDefault(x => x.Findetstatt && x.Unterricht != null && (m.UnterrichtNr is int nr ? x.Unterricht.Id == nr : lehrer is int l && x.LehrerIds.Contains(l)) &&
                                                       (m.Klassen.Count == 0 || m.Klassen.All(k => x.KlassenIds.Contains(Id(d, ElementTyp.Klasse, k) ?? -1))));
            if (e != null && (string.IsNullOrWhiteSpace(m.Fach) || Id(d, ElementTyp.Fach, m.Fach) == e.FachId))
            {
                art = StplAenderungsArt.Unterricht;
                uid = e.Unterricht!.Id;
                zeile = e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex;
                fach ??= ix.KFach(e.FachId);
                if (klassen.Count == 0) klassen = e.KlassenIds.Select(k => ix.K(ElementTyp.Klasse, k)).ToList();
            }
        }
        var titel = string.Join(" – ", new[] { m.Titel, m.Text }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var text = art == StplAenderungsArt.Unterricht ? $"({m.Absender}) Raumänderung" : titel;
        var klf = string.Join(", ", klassen) + (string.IsNullOrWhiteSpace(fach) ? "" : " / " + fach);
        return new StplAenderung(m, datum, von, bis, zeit, art, uid, zeile, klf, string.Join(", ", lehrerNamen), string.Join(", ", raeume), "", "", text, m.Absender, uebernommen, fehler);
    }

    /// <summary>Änderungen ignorieren (werden vermerkt und nicht erneut angeboten).</summary>
    public static void Ignorieren(Schuldatei d, IEnumerable<StplAenderung> liste, string benutzer = "")
    {
        foreach (var a in liste.Where(a => !a.Erledigt))
            d.Einstellungen.Portal.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = false, Benutzer = benutzer, Meldung = a.Meldung });
    }

    /// <summary>
    /// Änderungen übernehmen. Bereits übernommene oder ignorierte werden übersprungen. Liefert die übernommenen und die
    /// abgelehnten Kennungen (mit Grund).
    /// </summary>
    public static (List<string> Uebernommen, List<(string Id, string Grund)> Abgelehnt) Uebernehmen(Schuldatei d, IEnumerable<StplAenderung> liste, string benutzer = "")
    {
        var ok = new List<string>();
        var nein = new List<(string, string)>();
        var p = d.Einstellungen.Portal;
        var tage = new HashSet<DateOnly>();
        foreach (var alt in liste)
        {
            if (alt.Erledigt || p.StplAenderungen.Any(x => x.Id == alt.Id)) continue;
            var a = Zeile(d, alt.Meldung);
            if (a.Fehler != null) { nein.Add((a.Id, a.Fehler)); continue; }
            var m = a.Meldung;
            var raeume = m.Raeume.Append(m.Raum ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Id(d, ElementTyp.Raum, x)!.Value).Distinct().ToList();
            var lehrer = new[] { m.Lehrer ?? "" }.Concat(m.WeitereLehrer).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Id(d, ElementTyp.Lehrer, x)!.Value).Distinct().ToList();
            var klassen = m.Klassen.Select(k => Id(d, ElementTyp.Klasse, k)!.Value).ToList();
            var fach = Id(d, ElementTyp.Fach, m.Fach);
            var stunden = Enumerable.Range(a.StundeVon, a.StundeBis - a.StundeVon + 1).ToList();
            switch (a.Art)
            {
                case StplAenderungsArt.Unterricht:
                {
                    if (raeume.Count == 0) { nein.Add((a.Id, "kein Raum angegeben")); continue; }
                    // Die Rückmeldung enthält die vollständige neue Raumliste: der ursprüngliche Raum bleibt Hauptraum, solange er dabei ist;
                    // die übrigen sind genau die Zusatzräume (gelöschte Zusatzräume verschwinden, der ursprüngliche Raum bleibt erhalten)
                    var regel = Tagesplan.Berechnen(d, a.Datum).Stunde(a.StundeVon).FirstOrDefault(x => x.Unterricht?.Id == a.UnterrichtId);
                    var ursprung = regel?.Regel?.RaumId;
                    var haupt = ursprung is int ur && raeume.Contains(ur) ? ur : raeume[0];
                    var zusatz = raeume.Where(r => r != haupt).ToList();
                    var neu = Raumvertretungen.Zuordnen(d, a.UnterrichtId!.Value, a.ZeilenIndex ?? 0, a.Datum, stunden, haupt, false, RaumKollisionsLoesung.MitKollision, benutzer);
                    foreach (var v in neu.Where(v => v.UnterrichtId == a.UnterrichtId))
                    {
                        v.Text = a.Text;
                        v.WebId = a.Id;
                        v.ZusatzraumIds = zusatz.ToList();
                    }
                    // wieder genau der ursprüngliche Raum: bestehende Zusatzräume dieser Stunden entfernen
                    if (zusatz.Count == 0)
                        foreach (var v in d.Vertretungen.Where(v => v.Datum == a.Datum && v.UnterrichtId == a.UnterrichtId && stunden.Contains(v.Stunde)))
                            v.ZusatzraumIds.Clear();
                    break;
                }
                case StplAenderungsArt.Aktivitaet:
                    foreach (var s in stunden)
                        foreach (var l in lehrer.Cast<int?>().DefaultIfEmpty(null))
                        {
                            var tp = Tagesplan.Berechnen(d, a.Datum);
                            d.Vertretungen.Add(new Vertretung
                            {
                                Id = d.NeueId(), Datum = a.Datum, Stunde = s, Art = VertretungsArt.Sondereinsatz, Quelle = VertretungsQuelle.Sondereinsatz, VertreterId = l,
                                FachId = fach, VertretungsfachId = fach, KlassenIds = klassen, VertretungsraumId = raeume.Cast<int?>().FirstOrDefault(), ZusatzraumIds = raeume.Skip(1).ToList(),
                                Text = a.Text, Bearbeitet = true, Benutzer = string.IsNullOrEmpty(benutzer) ? "UntPlanWeb " + m.Absender : benutzer, WebId = a.Id,
                                Mehrarbeit = l is int ll && !Vertretungen.HatBereitschaft(d, tp, ll, s),
                            });
                        }
                    break;
                default:
                {
                    var k = new Klausur
                    {
                        Id = d.NeueId(), Name = string.IsNullOrWhiteSpace(m.Titel) ? "Klausur" : m.Titel!.Trim(), Text = m.Text ?? "", Datum = a.Datum, StundeVon = a.StundeVon,
                        StundeBis = a.StundeBis, KlassenIds = klassen, LehrerIds = lehrer, RaumIds = raeume, UnterrichtEntfaellt = true, WebId = a.Id,
                    };
                    d.Klausuren.Add(k);
                    tage.Add(a.Datum);
                    PausenaufsichtenPruefen(d, k, benutzer);
                    break;
                }
            }
            p.StplAenderungen.Add(new StplAenderungsEintrag { Id = a.Id, Uebernommen = true, Benutzer = benutzer, Meldung = m });
            ok.Add(a.Id);
        }
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        return (ok, nein);
    }

    /// <summary>
    /// Pausenaufsichten der Prüfungsaufsicht direkt vor bzw. nach der Prüfung: wenn nicht „außer Betracht“, entsteht eine
    /// offene Pausenaufsichtsvertretung.
    /// </summary>
    private static void PausenaufsichtenPruefen(Schuldatei d, Klausur k, string benutzer)
    {
        var p = d.Einstellungen.Portal;
        var tag = Kalender.TagIndex(d, k.Datum);
        if (tag < 0) return;
        var aufsichten = d.PeriodeAm(k.Datum).Pausenaufsichten ?? new();
        foreach (var a in aufsichten.Where(a => a.Tag == tag && (a.Datum is null || a.Datum == k.Datum) && k.LehrerIds.Contains(a.LehrerId)))
        {
            var vor = a.NachStunde == k.StundeVon - 1;
            var nach = a.NachStunde == k.StundeBis;
            if (!(vor && !p.PausenaufsichtVorPruefungIgnorieren) && !(nach && !p.PausenaufsichtNachPruefungIgnorieren)) continue;
            if (d.Vertretungen.Any(v => v.Datum == k.Datum && v.Art == VertretungsArt.Pausenaufsicht && v.GangId == a.GangId && v.Stunde == a.NachStunde && v.LehrerId == a.LehrerId)) continue;
            d.Vertretungen.Add(new Vertretung
            {
                Id = d.NeueId(), Datum = k.Datum, Stunde = a.NachStunde, Art = VertretungsArt.Pausenaufsicht, Quelle = VertretungsQuelle.Manuell, GangId = a.GangId,
                LehrerId = a.LehrerId, KlausurId = k.Id, Text = vor ? "vor Prüfung" : "nach Prüfung", Bearbeitet = true, Benutzer = benutzer, WebId = k.WebId,
            });
        }
    }
}
