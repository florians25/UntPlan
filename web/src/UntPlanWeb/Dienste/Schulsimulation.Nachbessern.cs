using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Stand des Nachbesserns (Hintergrundauftrag): geprüft, korrigiert, entfernt.</summary>
public sealed record Nachbesserungsstand(bool Laeuft, string Von, string Bis, int Geprueft, int Korrigiert, int Entfernt, string Beginn, string Ende, string Fehler, List<string> Zusammenfassung);

public sealed partial class Schulsimulation
{
    private Nachbesserungsstand _nachbessern = new(false, "", "", 0, 0, 0, "", "", "", new());

    public Nachbesserungsstand Nachbesserungsstatus => _nachbessern;

    /// <summary>Simulationsdaten nachbessern als Hintergrundauftrag (Standard: Schuljahresbeginn bis Schuljahresende, also auch geplante Prüfungen).</summary>
    public Nachbesserungsstand NachbessernStarten(Benutzer ich, DateOnly? von, DateOnly? bis)
    {
        if (!ich.IstAdmin) throw new UnauthorizedAccessException("Nur für die Administration.");
        var s = _paket.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen.");
        lock (_sperre)
        {
            if (_nachbessern.Laeuft) throw new ArgumentException("Das Nachbessern läuft bereits.");
            var v = von ?? Halbjahre(s)[0].Von;
            var b = bis ?? Halbjahre(s)[^1].Bis;
            if (v > b) throw new ArgumentException("Bitte einen gültigen Zeitraum wählen (Beginn vor Ende).");
            _nachbessern = new(true, Plandienst.Iso(v), Plandienst.Iso(b), 0, 0, 0, Zeit(_uhr.Jetzt), "", "", new());
            var e = Laden();
            Task.Run(() =>
            {
                try { Nachbessern(s, e, v, b); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Schulsimulation: Nachbessern fehlgeschlagen");
                    _nachbessern = _nachbessern with { Laeuft = false, Fehler = ex.Message, Ende = Zeit(_uhr.Jetzt) };
                }
            });
            return _nachbessern;
        }
    }

    private sealed record SimMeldung(string Id, string Typ, string Von, int StundeVon, int StundeBis, List<string> Raeume, List<string> Klassen, string Titel, string Status, int? Unterricht, int BenutzerId);

    /// <summary>
    /// Bereits erzeugte Simulationsdaten an die aktuellen Einstellungen und Regeln anpassen. Nur Datensätze der simulierten Konten, nie Einträge der Benutzer.
    /// Noch nicht übernommene Meldungen werden direkt geändert bzw. zurückgezogen; schon an das Desktop-Programm übertragene künftige Raumänderungen und
    /// Prüfungen laufen über den regulären Weg (Raumänderung zurück, Prüfung löschen bzw. verlegen). Idempotent: ein zweiter Lauf ändert nichts mehr.
    /// <list type="bullet">
    /// <item>Einträge und Meldungen ausgenommener Lehrkräfte entfernen.</item>
    /// <item>Raumänderungen in ausgeschlossenen Räumen zurücknehmen; Prüfungsräume, die ausgeschlossen bzw. nicht mehr erlaubt sind, auf den Raum der Stunde zurücksetzen.</item>
    /// <item>Prüfungen in Fächern, die nicht mehr in der Prüfungsliste stehen, entfernen (Tests bleiben).</item>
    /// <item>Hausaufgaben in der Berufsschule entfernen; Unterrichtsform und Gruppenarbeitszusätze aus dem Lehrstoff entfernen.</item>
    /// <item>Lehrstoff der Vertretungsstunden durch Vertretungsstoff ersetzen.</item>
    /// </list>
    /// </summary>
    public Nachbesserungsstand Nachbessern(Stand s, Simulationseinstellungen e, DateOnly von, DateOnly bis)
    {
        lock (_sperre)
        {
            int geprueft = 0, korrigiert = 0, entfernt = 0;
            var notizen = new Dictionary<string, int>();
            void Merke(string was, int n = 1) { if (n > 0) notizen[was] = notizen.GetValueOrDefault(was) + n; }
            var heute = _uhr.Heute;
            var ausgenommen = Ausgenommen(e).ToHashSet(Ci);
            var raeumeAus = e.RaeumeAusgeschlossen.ToHashSet(Ci);
            var klausurraeume = e.Klausurraeume.ToHashSet(Ci);
            var (sekI, sekII) = Pruefungsfaecher(s, e);
            var konten = _db.Liste("SELECT id, element FROM benutzer WHERE login LIKE 'sim~%'", r => (Id: r.Zahl("id"), Element: r.Text("element"))).ToList();
            var konto = konten.ToDictionary(k => k.Id, k => k.Element);
            const string Sim = "(SELECT id FROM benutzer WHERE login LIKE 'sim~%')";
            var pv = Db.P("@v", von);
            var pb = Db.P("@b", bis);

            // 1. ausgenommene Lehrkräfte: keine simulierten Einträge
            foreach (var l in ausgenommen)
            {
                if (!konten.Any(k => Ci.Equals(k.Element, l))) continue;
                var pl = Db.P("@l", l);
                var n = _db.Ausfuehren($"DELETE FROM klassenbuch WHERE lehrer = @l AND datum >= @v AND datum <= @b AND benutzer_id IN {Sim}", pl, pv, pb)
                        + _db.Ausfuehren($"DELETE FROM vermerke WHERE lehrer = @l AND datum >= @v AND datum <= @b AND benutzer_id IN {Sim}", pl, pv, pb)
                        + _db.Ausfuehren($"DELETE FROM hausaufgaben_eintraege WHERE lehrer = @l AND datum >= @v AND datum <= @b AND benutzer_id IN {Sim}", pl, pv, pb);
                entfernt += n;
                Merke("Einträge ausgenommener Lehrkräfte entfernt", n);
            }

            // 2. Meldungen (Raumänderungen, Prüfungen) der Simulation im Zeitraum
            var meldungen = _db.Liste($"SELECT * FROM rueckmeldungen WHERE typ IN ('raumbuchung', 'klausur') AND von >= @v AND von <= @b AND benutzer_id IN {Sim}", r => new SimMeldung(
                r.Text("id"), r.Text("typ"), r.Text("von"), r.Zahl("stunde_von"), r.Zahl("stunde_bis"), JsonSerializer.Deserialize<List<string>>(r.Text("raeume")) ?? new(),
                JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new(), r.Text("titel"), r.Text("status"), r.ZahlOderNull("unterricht"), r.Zahl("benutzer_id")), pv, pb);
            foreach (var m in meldungen)
            {
                geprueft++;
                if (!DateOnly.TryParse(m.Von, out var d)) continue;
                var lehrer = konto.GetValueOrDefault(m.BenutzerId, "");
                var offen = m.Status == "offen";
                var zukunft = d >= heute;
                var b = _bd.Holen(m.BenutzerId);
                if (b is null) continue;
                // ausgenommene Lehrkraft
                if (ausgenommen.Contains(lehrer))
                {
                    if (Entfernen(m, b, d, offen, zukunft)) { entfernt++; Merke("Meldungen ausgenommener Lehrkräfte zurückgenommen"); }
                    continue;
                }
                if (m.Typ == "raumbuchung")
                {
                    if (!m.Raeume.Any(raeumeAus.Contains)) continue;
                    if (offen) { _rm.Loeschen(m.Id); entfernt++; Merke("Raumänderungen in ausgeschlossenen Räumen zurückgenommen"); }
                    else if (zukunft && m.Unterricht is int u)
                    {
                        try
                        {
                            _rm.Raumaenderung(s, b, d, m.StundeVon, m.StundeBis, u, _plan.UrspruenglicheRaeume(s, d, u, m.StundeVon, m.StundeBis));
                            korrigiert++; Merke("Raumänderungen in ausgeschlossenen Räumen über den regulären Weg zurückgenommen");
                        }
                        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { }
                    }
                    continue;
                }
                // Prüfungen: Fach nicht mehr in der Prüfungsliste (Tests bleiben)
                var art = m.Titel.Split(' ')[0];
                var fach = m.Titel.Split(' ').Last();
                var klasse = m.Klassen.FirstOrDefault() ?? "";
                var liste = Stufe(s, klasse) is "sek2" or "bk" ? sekII : sekI;
                if (art is "Klassenarbeit" or "Klausur" && !liste.Contains(fach))
                {
                    if (Entfernen(m, b, d, offen, zukunft)) { entfernt++; Merke("Prüfungen in Fächern außerhalb der Prüfungsliste entfernt"); }
                    continue;
                }
                // Prüfungsraum: ausgeschlossen bzw. nicht erlaubt (Raum der Stunde bleibt immer erlaubt)
                var stundenraeume = s.Paket.Elemente.Klassen.Any(k => Ci.Equals(k.Kurz, klasse))
                    ? _plan.Karten(s, d, Elementtyp.Klasse, klasse).Where(k => k.Von <= m.StundeVon && k.Bis >= m.StundeVon && k.Unterricht.HasValue).SelectMany(k => k.Raeume.Select(r => r.K))
                        .Where(r => r.Length > 0 && r != "???").Distinct(Ci).ToList()
                    : new List<string>();
                bool Unzulaessig(string r) => !stundenraeume.Contains(r, Ci) && (raeumeAus.Contains(r) || (klausurraeume.Count > 0 && !klausurraeume.Contains(r)));
                if (m.Raeume.Any(Unzulaessig))
                {
                    var neu = m.Raeume.Where(r => !Unzulaessig(r)).ToList();
                    if (neu.Count == 0) neu = stundenraeume.Where(r => !raeumeAus.Contains(r)).ToList();
                    if (offen)
                    {
                        _db.Ausfuehren("UPDATE rueckmeldungen SET raeume = @r WHERE id = @i", Db.P("@r", JsonSerializer.Serialize(neu)), Db.P("@i", m.Id));
                        korrigiert++; Merke("Prüfungsräume angepasst");
                    }
                    else if (zukunft)
                    {
                        try
                        {
                            _rm.KlausurVerlegen(s, b, d, m.StundeVon, m.StundeBis, m.Klassen, m.Titel, d, m.StundeVon, m.StundeBis, m.Klassen, neu, Array.Empty<string>(), m.Titel, "");
                            korrigiert++; Merke("Prüfungsräume über den regulären Weg angepasst");
                        }
                        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { }
                    }
                }
            }

            // 3. Klassenbuch: Unterrichtsform, Gruppenarbeitszusätze, Hausaufgaben in der Berufsschule, Vertretungsstoff
            var eintraege = _db.Liste($"SELECT id, datum, stunde, unterricht, klassen, lehrer, thema, hausaufgabe, lehrform FROM klassenbuch WHERE datum >= @v AND datum <= @b AND benutzer_id IN {Sim}",
                r => (Id: r.Zahl("id"), Datum: r.Text("datum"), Stunde: r.Zahl("stunde"), U: r.Zahl("unterricht"), Klassen: JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new(),
                    Lehrer: r.Text("lehrer"), Thema: r.Text("thema"), Ha: r.Text("hausaufgabe"), Lf: r.Text("lehrform")), pv, pb);
            var w = DateTime.TryParse(e.Start, Inv, System.Globalization.DateTimeStyles.None, out var start) ? Welt(s, e, start, _uhr.Jetzt) : null;
            foreach (var x in eintraege)
            {
                geprueft++;
                var thema = x.Thema.Replace(", Vertiefung in Gruppenarbeit", ", Vertiefung").Replace("Gruppenarbeit und Präsentation", "Ergebnisse vorstellen")
                    .Replace("Aufgaben in Partnerarbeit", "Aufgaben zum Thema");
                var ha = x.Ha;
                if (ha.Length > 0 && x.Klassen.Any(k => Bildungsgang(s, k) == "bs")) { ha = ""; Merke("Hausaufgaben in der Berufsschule entfernt"); }
                if (thema.Length > 0 && w is not null && x.Stunde > 0 && DateOnly.TryParse(x.Datum, out var d) && !Vertretungstext(thema))
                {
                    var karte = _plan.Karten(s, d, Elementtyp.Lehrer, x.Lehrer).FirstOrDefault(k => k.Unterricht == x.U && k.Von <= x.Stunde && k.Bis >= x.Stunde);
                    if (karte is not null && Vertretungslehrkraft(s, karte, x.Lehrer) is { } fl)
                    {
                        thema = Vertretungsstoff(s, w, e, karte, fl, Zufall.Fuer(w.Seed, "ls", d, x.U, karte.Von, x.Lehrer), d);
                        Merke("Lehrstoff in Vertretungsstunden ersetzt");
                    }
                }
                if (x.Lf.Length > 0) Merke("Unterrichtsform entfernt");
                if (thema == x.Thema && ha == x.Ha && x.Lf.Length == 0) continue;
                if (thema != x.Thema && !Vertretungstext(thema)) Merke("Gruppenarbeitszusätze entfernt");
                _db.Ausfuehren("UPDATE klassenbuch SET thema = @t, hausaufgabe = @h, faellig = CASE WHEN @h = '' THEN NULL ELSE faellig END, lehrform = '' WHERE id = @i",
                    Db.P("@t", thema), Db.P("@h", ha), Db.P("@i", x.Id));
                korrigiert++;
            }
            var haEntfernt = 0;
            foreach (var (id, kl) in _db.Liste($"SELECT id, klassen FROM hausaufgaben_eintraege WHERE datum >= @v AND datum <= @b AND benutzer_id IN {Sim}",
                         r => (r.Zahl("id"), JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new()), pv, pb))
                if (kl.Any(k => Bildungsgang(s, k) == "bs")) haEntfernt += _db.Ausfuehren("DELETE FROM hausaufgaben_eintraege WHERE id = @i", Db.P("@i", id));
            entfernt += haEntfernt;
            Merke("Hausaufgaben in der Berufsschule entfernt", haEntfernt);

            _nachbessern = new(false, Plandienst.Iso(von), Plandienst.Iso(bis), geprueft, korrigiert, entfernt, _nachbessern.Beginn.Length > 0 ? _nachbessern.Beginn : Zeit(_uhr.Jetzt),
                Zeit(_uhr.Jetzt), "", notizen.Select(n => $"{n.Key}: {n.Value}").ToList());
            _cache.Clear();
            _tageCache.Clear();
            return _nachbessern;
        }
    }

    private static bool Vertretungstext(string thema) => thema.StartsWith("Vertretung", StringComparison.Ordinal) || thema is "Stillarbeit"
        || thema.StartsWith("Arbeitsblatt der Fachlehrkraft", StringComparison.Ordinal);

    /// <summary>Meldung der Simulation zurücknehmen: offen = löschen, schon übertragen und künftig = Prüfung über den regulären Weg löschen bzw. Raum zurück.</summary>
    private bool Entfernen(SimMeldung m, Benutzer b, DateOnly d, bool offen, bool zukunft)
    {
        if (offen) { _rm.Loeschen(m.Id); return true; }
        if (!zukunft || _paket.Aktuell is not { } s) return false;
        try
        {
            if (m.Typ == "klausur") _rm.KlausurLoeschen(s, b, d, m.StundeVon, m.StundeBis, m.Klassen, m.Titel);
            else if (m.Unterricht is int u) _rm.Raumaenderung(s, b, d, m.StundeVon, m.StundeBis, u, _plan.UrspruenglicheRaeume(s, d, u, m.StundeVon, m.StundeBis));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { return false; }
    }
}
