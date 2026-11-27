using System.Globalization;
using UntPlan.Web.Contracts;
using UntPlanWeb.Api;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed partial class Schulsimulation
{
    // ------------------------------------------------------------------------------------------ Klassenbuch je Stunde

    /// <summary>Gehaltene Stunde einer Lehrkraft (wie „offene Stunden“: regulär bzw. geändert, z. B. Vertretung).</summary>
    /// <summary>Anteil der Stunden mit einer Raumänderung durch die Lehrkraft (selten; Tests setzen ihn höher).</summary>
    public static double Raumaenderungsanteil { get; set; } = 0.008;

    private static bool Gehalten(Karte k) => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert" && !k.LehrerAbwesend && !k.NurKopplung;

    private static bool Oberstufe(Stand s, string klasse) =>
        s.Klassen.TryGetValue(klasse, out var k) && (k.Jahrgang is "EF" or "Q1" or "Q2" or "11" or "12" or "13" || k.Kurz.StartsWith("EF", StringComparison.OrdinalIgnoreCase)
                                                    || (k.Kurz.Length >= 2 && k.Kurz[0] is 'Q' or 'q' && char.IsDigit(k.Kurz[1])));

    /// <summary>Abwesenheitskontrolle, Lehrstoff und Klassenbucheinträge der Stunden eines Schultages.</summary>
    /// <param name="vollstaendig">Nachpflegen: jede Stunde erhält Abwesenheitskontrolle und Lehrstoff (keine vergessenen Stunden).</param>
    private IEnumerable<Ereignis> Tagesereignisse(Stand s, Simulationseinstellungen e, Weltstand w, DateOnly d, bool vollstaendig = false)
    {
        var lehrer = w.KlassenDerLehrkraft.Keys.Where(l => !w.Ausgenommen.Contains(l) && s.Lehrer.ContainsKey(l)).Order(StringComparer.Ordinal).ToList();
        var vorabErlaubt = Klassenbucheinstellungen.Laden(_db).LehrstoffZukunft;
        foreach (var l in lehrer)
        {
            List<Karte> karten;
            try { karten = _plan.Karten(s, d, Elementtyp.Lehrer, l).Where(Gehalten).ToList(); }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { continue; }
            foreach (var k in karten.DistinctBy(k => (k.Unterricht, k.Von)))
            {
                if (w.Krank(l, d, k.Von)) continue;
                // kein Unterricht (Bereitschaft, Besprechung, Sprechstunde, Online-Platzhalter …): keine Einträge
                if (Zuordnung(k.Fach, s.Faecher.TryGetValue(k.Fach, out var fk) ? fk.Name : null) is null) continue;
                var u = k.Unterricht!.Value;
                var basis = $"{d:yyyy-MM-dd}|{u}|{k.Von}|{l}";
                var z = Zufall.Fuer(w.Seed, "kb", basis);
                var beginn = Um(d, k.Beginn);
                var ende = Um(d, k.Ende);
                // Abwesenheitskontrolle (bei Kopplungen die erste Lehrkraft): 80 % zu Beginn der Stunde, sonst später am Tag, selten erst beim Nachpflegen
                var kontrolle = e.Abwesenheiten && (k.Lehrer.Count == 0 || Ci.Equals(k.Lehrer[0].K, l));
                if (kontrolle)
                {
                    var wk = z.Wert();
                    DateTime? zeitAn = wk < 0.8 ? beginn.AddMinutes(z.Zahl(0, 5))
                        : wk < 0.97 ? Max(ende.AddMinutes(z.Zahl(1, 20)), Um(d, z.Zahl(12 * 60, 17 * 60)))
                        : vollstaendig ? Um(w.Schultag(d, z.Zahl(2, 6)), z.Zahl(15 * 60, 20 * 60)) : null;
                    if (zeitAn is DateTime ta) yield return new($"an|{basis}", ta, () => Kontrolle(s, w, d, l, u, k.Von));
                }
                // Lehrstoff: 70 % zu Beginn der Stunde, 5 % im Voraus (ein bis zwei Tage vorher bzw. am Morgen), sonst nach der Stunde bis abends bzw. später
                if (e.Klassenbuch)
                {
                    var wt = z.Wert();
                    var vorabTag = d.AddDays(-z.Zahl(1, 2));
                    DateTime? zeit = wt switch
                    {
                        < 0.70 => beginn.AddMinutes(z.Zahl(0, 10)),
                        < 0.75 => vorabErlaubt ? (z.Chance(0.6) ? Um(vorabTag, z.Zahl(15 * 60, 21 * 60)) : Min(Um(d, z.Zahl(6 * 60 + 30, 7 * 60 + 40)), beginn.AddMinutes(-5))) : beginn.AddMinutes(z.Zahl(0, 10)),
                        < 0.87 => ende.AddMinutes(z.Zahl(0, 15)),
                        < 0.93 => Max(ende.AddMinutes(20), Um(d, z.Zahl(13 * 60 + 15, 17 * 60 + 45))),
                        < 0.97 => Um(d, z.Zahl(19 * 60, 22 * 60 + 30)),
                        < 0.99 => Um(w.Schultag(d, z.Zahl(1, 3)), z.Zahl(14 * 60, 20 * 60)),
                        _ => vollstaendig ? Um(w.Schultag(d, z.Zahl(2, 6)), z.Zahl(15 * 60, 20 * 60)) : null,
                    };
                    if (zeit is DateTime t) yield return new($"kb|{basis}", t, () => Lehrstoff(s, e, w, d, l, u, k.Von));
                }
                if (e.Klassenbuch && z.Chance(0.045))
                    yield return new($"vm|{basis}", ende.AddMinutes(z.Zahl(0, 25)), () => Vermerk(s, w, d, l, u, k.Von));
                if (!e.Lehreraktionen) continue;
                // Raumänderung: selten, ein bis zwei Tage bzw. wenige Stunden vorher
                var za = Zufall.Fuer(w.Seed, "ra", basis);
                if (za.Chance(Raumaenderungsanteil))
                {
                    var zeitRa = za.Chance(0.65) || beginn.Hour < 8 ? Um(d.AddDays(-za.Zahl(1, 2)), za.Zahl(15 * 60, 21 * 60)) : Um(d, za.Zahl(6 * 60 + 30, 7 * 60 + 25));
                    yield return new($"ra|{basis}", zeitRa, () => Raumaendern(s, w, d, l, u, k.Von, za));
                }
                // Hinweis an die Klasse am Vortag (Material mitbringen, Treffpunkt)
                var zh = Zufall.Fuer(w.Seed, "hw", basis);
                if (zh.Chance(0.025))
                    yield return new($"hw|{basis}", Um(d.AddDays(-1), zh.Zahl(15 * 60, 21 * 60)), () => Hinweis(s, d, l, u, k.Von, zh));
            }
        }
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>Karte der Stunde jetzt (zur Ausführung neu bestimmt: Vertretungen und Absenzen können sich geändert haben).</summary>
    private Karte KarteJetzt(Stand s, DateOnly d, string l, int u, int von) =>
        _plan.Karten(s, d, Elementtyp.Lehrer, l).Where(Gehalten).FirstOrDefault(k => k.Unterricht == u && k.Von == von)
        ?? throw new InvalidOperationException("Die Stunde findet nicht mehr statt.");

    private void Kontrolle(Stand s, Weltstand w, DateOnly d, string l, int u, int von)
    {
        var karte = KarteJetzt(s, d, l, u, von);
        // schon kontrolliert bzw. Abwesenheiten von anderen erfasst: nichts überschreiben
        if (_db.Wert($"SELECT 1 FROM klassenbuch WHERE datum = @d AND unterricht = @u AND stunde BETWEEN @v AND @b AND anwesenheit IS NOT NULL AND (benutzer_id IS NULL OR benutzer_id NOT IN {SimKonten})",
                Db.P("@d", d), Db.P("@u", u), Db.P("@v", karte.Von), Db.P("@b", karte.Bis)) is not null
            || _db.Wert($"SELECT 1 FROM fehlzeiten WHERE datum = @d AND unterricht = @u AND stunde BETWEEN @v AND @b AND (benutzer_id IS NULL OR benutzer_id NOT IN {SimKonten})",
                Db.P("@d", d), Db.P("@u", u), Db.P("@v", karte.Von), Db.P("@b", karte.Bis)) is not null)
            throw new InvalidOperationException("Die Abwesenheiten wurden bereits erfasst.");
        var b = LehrerKonto(s, l);
        var eintraege = new List<AnwesenheitEintrag>();
        // schon erfasst (z. B. Krankmeldung über die Klassenleitung): als abwesend übernehmen, Grund und Text bleiben, keine Doppelerfassung
        var vorhanden = _kb.FehlzeitenZeitraum(d, d).Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis && f.Art == "fehlt").GroupBy(f => f.SchuelerId).ToDictionary(g => g.Key, g => g.First());
        foreach (var sch in _kb.SchuelerDerStunde(s, karte, d))
        {
            if (vorhanden.TryGetValue(sch.Id, out var vf))
            {
                eintraege.Add(new(sch.Id, "fehlt", 0, Klassenbuchdienst.Klartext(vf.Bemerkung), vf.Grund.Length > 0 ? vf.Grund : null));
                continue;
            }
            // Berufsschule: Klasse in der schriftlichen Kammerprüfung (freigestellt)
            if (KammerpruefungDer(s, w.Seed, sch.Klasse) is { } kp && kp.Tag == d)
            {
                eintraege.Add(new(sch.Id, "fehlt", 0, $"{kp.Name} (Kammer)", "B"));
                continue;
            }
            if (!w.SchuelerAm.TryGetValue((sch.Id, d), out var a)) continue;
            switch (a.Art)
            {
                case "betrieb":
                    eintraege.Add(new(sch.Id, "fehlt", 0, null));
                    break;
                case "krank":
                    eintraege.Add(new(sch.Id, "fehlt", 0, null));
                    break;
                case "arzt" when karte.Von <= a.StundeBis && karte.Bis >= a.StundeVon:
                    eintraege.Add(new(sch.Id, "fehlt", 0, a.StundeBis >= 99 ? "Arzttermin, nach Hause gegangen" : null));
                    break;
                case "verspaetet" when karte.Von <= 3 && karte.Von == ErsteStunde(s, sch.Klasse, d):
                    // vor dem Eintreffen: fehlt (beim Eintreffen wird daraus über „anwesend ab jetzt“ die Verspätung); danach bzw. beim Nachpflegen gleich als Verspätung
                    if (_uhr.Jetzt < Um(d, karte.Beginn).AddMinutes(a.Minuten)) eintraege.Add(new(sch.Id, "fehlt", 0, null));
                    else eintraege.Add(new(sch.Id, "verspaetet", a.Minuten, a.Grund == "Z" ? Verspaetungstext(sch.Id) : null, a.Grund == "Z" ? "Z" : null));
                    break;
            }
        }
        _kb.AnwesenheitSpeichern(b, s, d, von, u, eintraege);
        // erste kontrollierte Stunde des Tages: kranke Schüler*innen meist gleich bis Tagesende als abwesend (die folgenden Stunden zeigen sie dann schon so,
        // auch wenn dort niemand kontrolliert); war die erste Stunde nicht kontrolliert (ausgenommene Lehrkraft), übernimmt das die nächste
        foreach (var x in eintraege.Where(x => x.Art == "fehlt" && !vorhanden.ContainsKey(x.SchuelerId)))
        {
            if (!w.SchuelerAm.TryGetValue((x.SchuelerId, d), out var a) || a.Art != "krank") continue;
            var sch = s.Schueler.FirstOrDefault(y => y.Id == x.SchuelerId);
            if (sch is null || !Zufall.Fuer(w.Seed, "tag", x.SchuelerId, d).Chance(0.8)) continue;
            if (_db.Wert("SELECT 1 FROM fehlzeiten WHERE schueler_id = @s AND datum = @d AND stunde < @v", Db.P("@s", x.SchuelerId), Db.P("@d", d), Db.P("@v", von)) is not null) continue;
            try { _kb.AbwesenheitErfassen(b, s, x.SchuelerId, Um(d, karte.Beginn), d.ToDateTime(new TimeOnly(23, 59)), null, null, null, (d, von, u)); }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException) { }
        }
    }

    private static string Verspaetungstext(int schuelerId) => schuelerId % 2 == 0 ? "Bahn verspätet" : "Bus verspätet";

    /// <summary>
    /// Verspätungen während der Stunde: Die Schülerin bzw. der Schüler steht bei der Kontrolle zu Stundenbeginn als fehlend im Klassenbuch und wird beim
    /// Eintreffen (Ereigniszeit = Ankunft) über „anwesend ab jetzt“ zur Verspätung. Nur in der ersten Stunde des Tages, wenn sie in der 1. bis 3. Stunde liegt.
    /// </summary>
    private IEnumerable<Ereignis> Verspaetungsereignisse(Stand s, Simulationseinstellungen e, Weltstand w, DateOnly d)
    {
        if (!e.Abwesenheiten) yield break;
        foreach (var a in w.Schuelerausfaelle.Where(a => a.Art == "verspaetet" && a.Von == d))
        {
            var erste = ErsteStunde(s, a.Klasse, d);
            if (erste is < 1 or > 3) continue;
            Karte? karte;
            try { karte = _plan.Karten(s, d, Elementtyp.Klasse, a.Klasse).FirstOrDefault(k => Gehalten(k) && k.Von == erste && k.Lehrer.Count > 0); }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { continue; }
            if (karte is null) continue;
            var l = karte.Lehrer[0].K;
            if (w.Ausgenommen.Contains(l) || w.Krank(l, d, erste)) continue;
            var u = karte.Unterricht!.Value;
            var ankunft = Um(d, karte.Beginn).AddMinutes(a.Minuten);
            yield return new($"sp|{d:yyyy-MM-dd}|{u}|{erste}|{a.SchuelerId}", ankunft, () =>
            {
                var b = LehrerKonto(s, l);
                if (_db.Wert("SELECT 1 FROM fehlzeiten WHERE schueler_id = @s AND datum = @d AND stunde = @n AND art = 'fehlt'", Db.P("@s", a.SchuelerId), Db.P("@d", d), Db.P("@n", erste)) is null)
                    throw new InvalidOperationException("Nicht als fehlend erfasst.");
                _kb.Aufgestanden(b, s, d, erste, u, a.SchuelerId);
                // Grund „Zug/Bus verspätet“ mit Text (Format der Bemerkung wie beim Erfassen: Hinweis – Text)
                if (a.Grund == "Z")
                    foreach (var (id, bem) in _db.Liste($"SELECT id, bemerkung FROM fehlzeiten WHERE schueler_id = @s AND datum = @d AND art = 'verspaetet' AND benutzer_id IN {SimKonten}",
                                 r => (r.Zahl("id"), r.Text("bemerkung")), Db.P("@s", a.SchuelerId), Db.P("@d", d)))
                        _db.Ausfuehren("UPDATE fehlzeiten SET grund = 'Z', bemerkung = @b WHERE id = @i",
                            Db.P("@b", bem.Length == 0 ? Verspaetungstext(a.SchuelerId) : $"{bem} – {Verspaetungstext(a.SchuelerId)}"), Db.P("@i", id));
            });
        }
    }

    private readonly Dictionary<(string, DateOnly), int> _ersteStunde = new();

    /// <summary>Erste Unterrichtsstunde der Klasse am Tag (für Verspätungen: nur wenn sie in der 1. bis 3. Stunde liegt).</summary>
    private int ErsteStunde(Stand s, string klasse, DateOnly d)
    {
        if (_ersteStunde.TryGetValue((klasse, d), out var n)) return n;
        if (_ersteStunde.Count > 5000) _ersteStunde.Clear();
        n = _plan.Karten(s, d, Elementtyp.Klasse, klasse).Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "abwesend")).Select(k => k.Von).DefaultIfEmpty(0).Min();
        return _ersteStunde[(klasse, d)] = n;
    }

    /// <summary>Konten der Simulation (für „eigene Einträge des Nutzers nie überschreiben“).</summary>
    private const string SimKonten = "(SELECT id FROM benutzer WHERE login LIKE 'sim~%')";

    private void Lehrstoff(Stand s, Simulationseinstellungen e, Weltstand w, DateOnly d, string l, int u, int von)
    {
        var karte = KarteJetzt(s, d, l, u, von);
        // Lehrstoff, den jemand anderes (z. B. die Schulleitung) schon eingetragen hat, bleibt unverändert
        if (_db.Wert($"SELECT 1 FROM klassenbuch WHERE datum = @d AND stunde = @s AND unterricht = @u AND thema <> '' AND (benutzer_id IS NULL OR benutzer_id NOT IN {SimKonten})",
                Db.P("@d", d), Db.P("@s", Klassenbuchdienst.Schluessel(karte)), Db.P("@u", u)) is not null)
            throw new InvalidOperationException("Lehrstoff ist bereits eingetragen.");
        var b = LehrerKonto(s, l);
        var z = Zufall.Fuer(w.Seed, "ls", d, u, von, l);
        var sekII = karte.Klassen.Any(k => Oberstufe(s, k.K) || Stufe(s, k.K) == "bk");
        var vertretung = Vertretungslehrkraft(s, karte, l);
        var kammer = karte.Klassen.Select(x => KammerpruefungDer(s, w.Seed, x.K)).FirstOrDefault(x => x is not null && x.Tag == d);
        var thema = kammer is not null ? $"Klasse in der {kammer.Name} (Kammer), kein Unterricht"
            : vertretung is not null ? Vertretungsstoff(s, w, e, karte, vertretung, z, d)
            : LehrstoffFuer(s, w, e, karte, d) ?? throw new InvalidOperationException("Kein Unterrichtsfach.");
        if (Zuordnung(karte.Fach, s.Faecher.TryGetValue(karte.Fach, out var fe) ? fe.Name : null)?.Zyklisch == true) z = Zufall.Fuer(w.Seed, "kl", d, u);
        var sprache = karte.Fach.ToUpperInvariant() is "E" or "F" or "L" or "S";
        // Hausaufgaben selten, eher in den Hauptfächern; nie in Vertretungsstunden, Sport, Klassenleitung
        var gruppe = Zuordnung(karte.Fach, s.Faecher.TryGetValue(karte.Fach, out var fh) ? fh.Name : null)?.Id;
        var hauptfach = gruppe is "deutsch" or "mathe" or "englisch" or "franzoesisch" or "latein" or "spanisch" or "fachenglisch";
        // Berufsschule (Teilzeit): gar keine Hausaufgaben
        var berufsschule = karte.Klassen.Any(x => Bildungsgang(s, x.K) == "bs");
        // gelegentlich eine kurze schriftliche Überprüfung in einem Teil der Stunde (nicht angekündigt bis kurz angekündigt)
        if (kammer is null && vertretung is null && hauptfachOderBeruf(gruppe) && Zufall.Fuer(w.Seed, "kt", d, u, von).Chance(0.025) && !thema.StartsWith("Klass") && !thema.StartsWith("Klausur") && !thema.StartsWith("Test"))
            thema = $"{thema}, {Kurztest(gruppe, Zufall.Fuer(w.Seed, "ktt", d, u))}";
        var anteil = vertretung is not null || kammer is not null || berufsschule || gruppe is "sport" or "klassenleitung" or "koerper" or "foerder" ? 0 : hauptfach ? (sekII ? 0.07 : 0.12) : 0.025;
        var hausaufgabe = !z.Chance(anteil) ? ""
            : z.Zahlen(z.Wahl(sprache && z.Chance(0.6) ? HausaufgabenSprachen : Hausaufgaben));
        DateOnly? faellig = hausaufgabe.Length > 0 ? d.AddDays(z.Chance(0.7) ? 7 : z.Zahl(1, 6)) : null;
        var notiz = z.Chance(0.03) ? z.Wahl(Notizen) : "";
        // ein Hinweis an die Klasse vom Vortag bleibt (eigene Notiz und Hausaufgabe gehen vor)
        var alt = _kb.Holen(d, Klassenbuchdienst.Schluessel(karte), u, l);
        _kb.Speichern(b, s, d, von, u, thema, hausaufgabe.Length > 0 ? hausaufgabe : alt?.Hausaufgabe ?? "", hausaufgabe.Length > 0 ? faellig : alt?.Faellig is { } af ? DateOnly.Parse(af) : null,
            notiz.Length > 0 ? notiz : alt?.Notiz ?? "", null, null, null, l);
    }

    private static bool hauptfachOderBeruf(string? gruppe) => gruppe is not (null or "sport" or "klassenleitung" or "koerper" or "foerder" or "musik" or "kunst" or "religion");

    /// <summary>Kurze schriftliche Überprüfung als Teil der Stunde (Klassenbucheintrag).</summary>
    public static string Kurztest(string? gruppe, Zufall z) => gruppe is "englisch" or "franzoesisch" or "latein" or "spanisch" or "fachenglisch"
        ? z.Wahl(new[] { "Vokabeltest (15 Min.)", "Vokabeltest zur Unit", "kurzer Vokabeltest" })
        : z.Wahl(new[] { "schriftliche Übung (20 Min.)", "Kurztest (15 Min.)", "Lernkontrolle (20 Min.)", "schriftliche Überprüfung der Hausaufgabe" });

    /// <summary>Hinweis an die Klasse für die Stunde (für Schüler*innen sichtbar), bisherige Einträge der Stunde bleiben.</summary>
    private void Hinweis(Stand s, DateOnly d, string l, int u, int von, Zufall z)
    {
        var karte = KarteJetzt(s, d, l, u, von);
        var b = LehrerKonto(s, l);
        var alt = _kb.Holen(d, Klassenbuchdienst.Schluessel(karte), u, l);
        var text = z.Wahl(Hinweise.TryGetValue(karte.Fach, out var h) ? h : Hinweise[""]);
        _kb.Speichern(b, s, d, von, u, alt?.Thema ?? "", alt?.Hausaufgabe ?? "", alt?.Faellig is { } f ? DateOnly.Parse(f) : null, alt?.Notiz ?? "", text, null, null, l);
    }

    /// <summary>
    /// Raumänderung der Lehrkraft über den Weg der Stundendetails (Raum ersetzen bzw. hinzufügen), nur mit freien buchbaren Räumen:
    /// Informatik bzw. Recherche in den Computerraum, Naturwissenschaften in den Fachraum, Sport in die andere Halle, sonst ein anderer Raum derselben Art
    /// (Raum belegt, Technik defekt) bzw. ein zweiter Raum für Gruppenarbeit.
    /// </summary>
    private void Raumaendern(Stand s, Weltstand w, DateOnly d, string l, int u, int von, Zufall z)
    {
        if (w.Krank(l, d, von)) throw new InvalidOperationException("Die Lehrkraft ist abwesend.");
        var karte = KarteJetzt(s, d, l, u, von);
        var bisher = karte.Raeume.Select(r => r.K).Where(r => r.Length > 0 && r != "???").ToList();
        if (bisher.Count == 0) throw new InvalidOperationException("Die Stunde hat keinen Raum.");
        // ausgeschlossene Räume (Einstellung) bucht die Simulation nie
        var aus = Laden().RaeumeAusgeschlossen.ToHashSet(Ci);
        var frei = Rueckmeldungsdienst.Buchbar(s).Where(r => !bisher.Contains(r.Kurz, Ci) && !aus.Contains(r.Kurz) && _rm.RaumFrei(s, d, r.Kurz, karte.Von, karte.Bis, u)).ToList();
        var art = s.Raeume.TryGetValue(bisher[0], out var alt) ? alt.Raumart : "";
        bool Passt(WebRaum r, params string[] w) => w.Any(x => r.Raumart.Contains(x, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(x, StringComparison.OrdinalIgnoreCase));
        var fach = karte.Fach.ToUpperInvariant();
        List<WebRaum> kandidaten = fach switch
        {
            "IF" => frei.Where(r => Passt(r, "EDV", "Computer", "Informatik")).ToList(),
            "BI" => frei.Where(r => Passt(r, "Bio")).ToList(),
            "CH" => frei.Where(r => Passt(r, "Chemie")).ToList(),
            "PH" => frei.Where(r => Passt(r, "Physik")).ToList(),
            "SP" => frei.Where(r => Passt(r, "Sport", "Turnhalle", "Halle")).ToList(),
            _ => new(),
        };
        if (kandidaten.Count == 0 && fach is not "SP" && z.Chance(0.3)) kandidaten = frei.Where(r => Passt(r, "EDV", "Computer")).ToList();
        if (kandidaten.Count == 0) kandidaten = frei.Where(r => art.Length > 0 && Ci.Equals(r.Raumart, art)).ToList();
        if (kandidaten.Count == 0) throw new InvalidOperationException("Kein passender freier Raum.");
        var neu = z.Wahl(kandidaten).Kurz;
        var hinzufuegen = fach is not "SP" && Ci.Equals(s.Raeume[neu].Raumart, art) && z.Chance(0.3);
        var raeume = hinzufuegen ? bisher.Append(neu).ToList() : bisher.Skip(1).Prepend(neu).ToList();
        _rm.Raumaenderung(s, LehrerKonto(s, l), d, karte.Von, karte.Bis, u, raeume, bisher);
    }

    private void Vermerk(Stand s, Weltstand w, DateOnly d, string l, int u, int von)
    {
        var karte = KarteJetzt(s, d, l, u, von);
        var b = LehrerKonto(s, l);
        var z = Zufall.Fuer(w.Seed, "vm", d, u, von, l);
        var anwesend = _kb.SchuelerDerStunde(s, karte, d).Where(x => !w.SchuelerAm.ContainsKey((x.Id, d))).ToList();
        var v = z.Gewichtet(Vermerke, x => x.Gewicht);
        if (v.Schueler && anwesend.Count == 0) throw new InvalidOperationException("Keine Schüler*innen anwesend.");
        var sch = v.Schueler ? z.Wahl(anwesend) : null;
        var klasse = sch?.Klasse ?? karte.Klassen.FirstOrDefault()?.K ?? throw new InvalidOperationException("Stunde ohne Klasse.");
        var zeit = karte.Beginn.Length == 5 && TimeOnly.TryParse(karte.Beginn, CultureInfo.InvariantCulture, out var bt) ? bt.AddMinutes(z.Zahl(5, 35)).ToString("HH:mm", Inv) : null;
        _kb.VermerkAnlegen(b, s, d, klasse, sch?.Id, v.Art, v.Text.Replace("{n}", sch?.Vorname ?? ""), zeit);
    }

    // ------------------------------------------------------------------------------------------ Entschuldigungen der Klassenleitung

    /// <summary>Klassenleitung der Klasse, für die die Automatik läuft (nicht ausgenommen); null = keine.</summary>
    private static string? Klassenleitung(Stand s, Weltstand w, string klasse) =>
        s.Klassen.TryGetValue(klasse, out var kl) ? kl.Klassenleitung.FirstOrDefault(x => !w.Ausgenommen.Contains(x) && s.Lehrer.ContainsKey(x)) : null;

    /// <summary>Ein Teil der Krankheiten wird gemeldet (E-Mail, Sekretariat, Betrieb): die Klassenleitung trägt sie als Zeitraum ein und entschuldigt sofort.</summary>
    private static bool Gemeldet(Weltstand w, Schuelerausfall a) => a.Art == "krank" && Zufall.Fuer(w.Seed, "mel", a.SchuelerId, a.Von).Chance(0.45);

    /// <summary>
    /// Krankmeldung von Eltern, volljährigen Schüler*innen bzw. dem Ausbildungsbetrieb: Die Klassenleitung legt die Abwesenheit für den gemeldeten
    /// Zeitraum an (Grund Krankheit, entschuldigt, mit Bemerkung zur Meldung). Gemeldet wird im Voraus (Vorabend, Wochenende), am Morgen, im Lauf des
    /// Tages oder erst Tage später; liegt die Meldung vor der Stunde, steht die Schülerin bzw. der Schüler in der Kontrolle schon als abwesend.
    /// </summary>
    private Ereignis? Schuelerkrankmeldung(Stand s, Simulationseinstellungen e, Weltstand w, Schuelerausfall a)
    {
        if (!Gemeldet(w, a) || Klassenleitung(s, w, a.Klasse) is not { } leitung) return null;
        var z = Zufall.Fuer(w.Seed, "melz", a.SchuelerId, a.Von);
        var vortag = a.Von.AddDays(-1);
        var zeit = z.Gewichtet(new[] { (1, 25.0), (2, 35.0), (3, 15.0), (4, 25.0) }, x => x.Item2).Item1 switch
        {
            1 => Um(vortag, z.Zahl(17 * 60, 21 * 60 + 30)),
            2 => Um(a.Von, z.Zahl(6 * 60 + 40, 7 * 60 + 50)),
            3 => Um(a.Von, z.Zahl(9 * 60, 12 * 60 + 30)),
            _ => Um(w.Schultag(a.Von, z.Zahl(1, 4)), z.Zahl(13 * 60, 17 * 60)),
        };
        return new($"ms|{a.SchuelerId}|{a.Von:yyyy-MM-dd}", zeit, () =>
        {
            if (w.Krank(leitung, DateOnly.FromDateTime(_uhr.Jetzt), 1)) throw new InvalidOperationException("Die Klassenleitung ist krank.");
            var bg = Bildungsgang(s, a.Klasse);
            var (text, nachweis) = Meldungstext(bg, Volljaehrig(bg, a.SchuelerId), z);
            if (a.Tage.Count > 1) text += $" für {a.Tage.Count} Tage";
            var (von, bis) = Klassenbuchdienst.Ganztaegig(a.Von.ToDateTime(TimeOnly.MinValue), a.Bis.ToDateTime(TimeOnly.MinValue));
            _kb.AbwesenheitErfassen(LehrerKonto(s, leitung), s, a.SchuelerId, von, bis, "K", text, "entschuldigt", null, null, nachweis, Plandienst.Iso(DateOnly.FromDateTime(_uhr.Jetzt)));
        });
    }

    /// <summary>Wer meldet wie: Eltern (E-Mail, Sekretariat, Elternportal), Volljährige selbst, in der Berufsschule auch der Ausbildungsbetrieb.</summary>
    public static (string Text, string Nachweis) Meldungstext(string bildungsgang, bool volljaehrig, Zufall z)
    {
        var liste = new List<(string, string)>();
        if (bildungsgang == "bs") liste.AddRange(new[] { ("Mitteilung vom Ausbildungsbetrieb", "email"), ("Krankmeldung über den Ausbildungsbetrieb", "telefonisch") });
        if (volljaehrig) liste.AddRange(new[] { ("Eigene Krankmeldung per E-Mail", "email"), ("Selbst telefonisch im Sekretariat gemeldet", "telefonisch"), ("Eigene Krankmeldung per Mitteilung", "schriftlich") });
        else liste.AddRange(new[] { ("Krankmeldung per E-Mail der Mutter", "email"), ("Krankmeldung per E-Mail des Vaters", "email"), ("Anruf der Mutter im Sekretariat", "telefonisch"),
            ("Telefonisch im Sekretariat gemeldet", "telefonisch"), ("Krankmeldung über das Elternportal", "schriftlich") });
        return z.Wahl(liste);
    }

    private Ereignis? Entschuldigung(Stand s, Simulationseinstellungen e, Weltstand w, Schuelerausfall a)
    {
        if (Gemeldet(w, a)) return null;
        var leitung = Klassenleitung(s, w, a.Klasse);
        if (leitung is null) return null;
        var z = Zufall.Fuer(w.Seed, "en", a.SchuelerId, a.Von);
        var status = a.Art switch
        {
            "verspaetet" => z.Gewichtet(new[] { ("entschuldigt", 45.0), ("unentschuldigt", 35.0), ("", 20.0) }, x => x.Item2).Item1,
            "arzt" => z.Gewichtet(new[] { ("entschuldigt", 90.0), ("", 10.0) }, x => x.Item2).Item1,
            "betrieb" => z.Gewichtet(new[] { ("entschuldigt", 45.0), ("unentschuldigt", 45.0), ("", 10.0) }, x => x.Item2).Item1,
            _ => z.Gewichtet(new[] { ("entschuldigt", 82.0), ("unentschuldigt", 7.0), ("", 11.0) }, x => x.Item2).Item1,
        };
        if (status.Length == 0) return null;
        var tag = w.Schultag(a.Bis, status == "unentschuldigt" ? z.Zahl(8, 12) : z.Zahl(1, 7));
        // Klassenleitung nach Unterrichtsschluss bzw. vor der ersten Stunde; ist sie krank, später
        var zeit = z.Chance(0.75) ? Um(tag, z.Zahl(13 * 60 + 20, 17 * 60 + 40)) : Um(tag, z.Zahl(7 * 60 + 10, 7 * 60 + 50));
        return new($"en|{a.SchuelerId}|{a.Von:yyyy-MM-dd}|{a.Art}", zeit, () =>
        {
            if (w.Krank(leitung, DateOnly.FromDateTime(_uhr.Jetzt), 1)) throw new InvalidOperationException("Die Klassenleitung ist krank.");
            var ids = _db.Liste("""
                SELECT id FROM fehlzeiten WHERE schueler_id = @s AND datum >= @v AND datum <= @b AND status = 'offen'
                    AND benutzer_id IN (SELECT id FROM benutzer WHERE login LIKE 'sim~%')
                """, r => r.Zahl("id"), Db.P("@s", a.SchuelerId), Db.P("@v", a.Von), Db.P("@b", a.Bis));
            if (ids.Count == 0) throw new InvalidOperationException("Keine offenen Fehlzeiten.");
            var (text, nachweis) = EntschuldigungText(a, status, z, Bildungsgang(s, a.Klasse));
            var b = LehrerKonto(s, leitung);
            _ent.AbwesenheitEntschuldigen(b, s, ids, status, text, Plandienst.Iso(DateOnly.FromDateTime(_uhr.Jetzt)), nachweis);
            if (status == "entschuldigt" && Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM abwesenheitsgruende WHERE kurz = @k", Db.P("@k", a.Grund)) ?? 0) > 0)
                foreach (var id in ids) _db.Ausfuehren("UPDATE fehlzeiten SET grund = @g WHERE id = @i", Db.P("@g", a.Grund), Db.P("@i", id));
        });
    }

    public static (string Text, string? Nachweis) EntschuldigungText(Schuelerausfall a, string status, Zufall z, string bildungsgang = "")
    {
        // Berufskolleg: Entschuldigung über den Ausbildungsbetrieb bzw. bei Volljährigen durch die Schülerin bzw. den Schüler selbst, ärztliche Bescheinigung bei längerer Krankheit
        var bs = bildungsgang == "bs";
        var selbst = Volljaehrig(bildungsgang, a.SchuelerId);
        if (a.Art == "betrieb")
            return status == "unentschuldigt"
                ? (z.Wahl(new[] { "Betrieb hat zurückgehalten (Montage), keine Freistellung beantragt. Ausbilder informiert.", "Fehlen wegen Kundentermin im Betrieb, nicht entschuldigungsfähig. Ausbilderin informiert." }), null)
                : (z.Wahl(new[] { "Freistellung durch den Ausbildungsbetrieb bestätigt.", "Betriebliche Gründe, schriftliche Bestätigung des Ausbildungsbetriebs liegt vor." }), "schriftlich");
        if (status != "unentschuldigt" && a.Art == "krank" && (bs || bildungsgang == "fs" || selbst))
        {
            if (a.Tage.Count >= 3) return (bs ? "Arbeitsunfähigkeitsbescheinigung liegt vor, Betrieb informiert." : "Ärztliche Bescheinigung liegt vor.", "attest");
            if (bs) return (z.Wahl(new[] { "Entschuldigung vom Ausbildungsbetrieb gegengezeichnet.", "Krankmeldung vor Unterrichtsbeginn, Bestätigung des Ausbildungsbetriebs liegt vor.",
                "Eigene schriftliche Entschuldigung, vom Betrieb abgezeichnet." }), "schriftlich");
            return (z.Wahl(new[] { "Eigene schriftliche Entschuldigung (volljährig).", "Krankmeldung per E-Mail, eigene Entschuldigung nachgereicht." }), selbst ? "schriftlich" : "email");
        }
        if (status == "unentschuldigt") return (z.Wahl(new[] { "Keine Entschuldigung eingegangen, Frist abgelaufen.", "Trotz Erinnerung keine Entschuldigung abgegeben." }), null);
        if (a.Art == "verspaetet")
            return a.Grund == "Z" ? (z.Wahl(new[] { "Bus verspätet, Bestätigung der Eltern.", "Zugausfall, Mitteilung der Mutter." }), "schriftlich")
                : (z.Wahl(new[] { "Verschlafen, Eltern haben sich gemeldet.", "Arzttermin am Morgen, Bescheinigung liegt vor." }), "muendlich");
        if (a.Art == "arzt") return (z.Wahl(new[] { "Arzttermin, Bescheinigung liegt vor.", "Kieferorthopäde, Terminbestätigung abgegeben.", "Termin beim Facharzt, Entschuldigung der Eltern." }), "schriftlich");
        var (t, n) = z.Wahl(new[]
        {
            ("Entschuldigung der Eltern liegt vor (Erkältung).", "schriftlich"), ("Schriftliche Entschuldigung, Magen-Darm-Infekt.", "schriftlich"),
            ("Attest vorgelegt.", "attest"), ("Telefonische Krankmeldung der Mutter, schriftliche Entschuldigung nachgereicht.", "telefonisch"),
            ("Entschuldigung per E-Mail durch den Vater.", "email"), ("Fieber, Entschuldigung über das Elternportal.", "email"), ("Krank, Entschuldigung der Eltern.", "schriftlich"),
        });
        if (a.Grund == "F") return ("Familiäre Gründe, Entschuldigung der Eltern liegt vor.", "schriftlich");
        return (a.Tage.Count >= 4 && z.Chance(0.7) ? "Ärztliches Attest liegt vor." : t, a.Tage.Count >= 4 ? "attest" : n);
    }

    // ------------------------------------------------------------------------------------------ Krankmeldungen der Lehrkräfte

    private IEnumerable<Ereignis> Meldungen(Stand s, Simulationseinstellungen e, Weltstand w, Ausfall a)
    {
        var z = Zufall.Fuer(w.Seed, "km", a.Lehrer, a.Von);
        var startZeit = DateTime.TryParse(e.Start, Inv, DateTimeStyles.None, out var st) ? st.AddMinutes(1) : DateTime.MinValue;
        var basis = $"km|{a.Lehrer}|{a.Von:yyyy-MM-dd}";
        // Erste Meldung: Arzttermin einige Tage vorher, sonst meist am Nachmittag bzw. Abend davor, manchmal nachts bzw. früh morgens
        DateTime zeit;
        if (a.Art.Teil) zeit = Um(a.Von.AddDays(-z.Zahl(1, 5)), z.Zahl(14 * 60, 20 * 60 + 30));
        else
        {
            var vortag = a.Von.AddDays(-1);
            zeit = z.Gewichtet(new[] { (1, 44.0), (2, 22.0), (3, 11.0), (4, 23.0) }, x => x.Item2).Item1 switch
            {
                1 => Um(vortag, z.Zahl(17 * 60, 22 * 60 + 30)),
                2 => Um(vortag, z.Zahl(13 * 60, 17 * 60)),
                3 => z.Chance(0.5) ? Um(vortag, z.Zahl(22 * 60 + 30, 23 * 60 + 59)) : Um(a.Von, z.Zahl(0, 4 * 60 + 30)),
                _ => Um(a.Von, z.Zahl(5 * 60 + 15, 7 * 60 + 15)),
            };
        }
        // lange Ausfälle: zuerst bis zum Ende der ersten Woche, dann Verlängerungen (Folgebescheinigungen)
        const int Abschnitt = 5;
        var lang = a.Tage.Count > Abschnitt + 1;
        var ersteBis = lang ? a.Tage[Abschnitt - 1] : a.Bis;
        yield return new(basis + "|start", Max(zeit, startZeit), () => Senden(s, e, a.Lehrer, Krankmeldung(s, w, a, ersteBis, z)));
        if (lang)
            for (var i = Abschnitt; i < a.Tage.Count; i += Abschnitt)
            {
                var bis = a.Tage[Math.Min(a.Tage.Count - 1, i + Abschnitt - 1)];
                var zv = Zufall.Fuer(w.Seed, "kmv", a.Lehrer, a.Von, i);
                var tag = a.Tage[i - 1];
                var nummer = i;
                yield return new($"{basis}|verl|{nummer}", Max(Um(tag, zv.Zahl(12 * 60, 19 * 60)), startZeit), () => Senden(s, e, a.Lehrer, Verlaengerung(s, a, bis, zv)));
            }
        if (a.Tage.Count >= 2 && z.Chance(0.5))
        {
            var zg = Zufall.Fuer(w.Seed, "kmg", a.Lehrer, a.Von);
            yield return new($"{basis}|gesund", Max(Um(a.Bis, zg.Zahl(16 * 60, 21 * 60 + 30)), startZeit), () => Senden(s, e, a.Lehrer, Gesundmeldung(s, w, a, zg)));
        }
    }

    private void Senden(Stand s, Simulationseinstellungen e, string lehrer, (string Betreff, string Text) m)
    {
        var b = LehrerKonto(s, lehrer);
        var liste = Verteilerliste(e.Verteiler);
        HashSet<int> ids;
        var text = "";
        if (liste is { } l && _bd.Holen(l.Besitzer) is { } besitzer)
        {
            ids = _mit.Aufloesen(besitzer, s, l.Mitglieder);
            ids.Add(besitzer.Id);
            text = $"Liste: {e.Verteiler}";
        }
        else
        {
            ids = _bd.Alle().Where(x => !x.Gesperrt && x.Rolle is Rollen.Leitung or Rollen.Admin).Select(x => x.Id).ToHashSet();
            text = "Schulleitung und Administration";
        }
        _mit.Simulationszustellung(b, ids, m.Betreff, m.Text, text);
    }

    private static string Datum(DateOnly d) => $"{Wochentage[((int)d.DayOfWeek + 6) % 7]}, {d.ToString("dd.MM.", De)}";

    /// <summary>„morgen“, „heute“ bzw. Wochentag mit Datum, aus Sicht des Sendezeitpunkts.</summary>
    private string Tagwort(DateOnly tag)
    {
        var heute = _uhr.Heute;
        return tag == heute ? "heute" : tag == heute.AddDays(1) ? "morgen" : $"am {Datum(tag)}";
    }

    private static string Name(Stand s, string lehrer, Zufall z) =>
        s.Lehrer.TryGetValue(lehrer, out var l) && l.Vorname.Length > 0 ? z.Chance(0.55) ? l.Vorname : $"{l.Vorname} {l.Name}" : lehrer;

    private string Anrede(Zufall z)
    {
        var h = _uhr.Jetzt.Hour;
        return z.Chance(0.4) ? z.Wahl(h >= 18 ? AnredenAbend : h < 9 ? AnredenMorgen : Anreden) : z.Wahl(Anreden);
    }

    private (string, string) Krankmeldung(Stand s, Weltstand w, Ausfall a, DateOnly bis, Zufall z)
    {
        var tag = Tagwort(a.Von);
        var satz = z.Wahl(a.Art.Saetze);
        var sb = new System.Text.StringBuilder();
        sb.Append(Anrede(z)).Append("\n\n");
        string betreff;
        if (a.Art.Teil)
        {
            var stunden = a.StundeBis >= 10 ? $"ab der {a.StundeVon}. Stunde" : a.StundeVon == a.StundeBis ? $"in der {a.StundeVon}. Stunde" : $"von der {a.StundeVon}. bis zur {a.StundeBis}. Stunde";
            betreff = z.Wahl(new[] { $"Arzttermin {Datum(a.Von)}", "Abwesenheit wegen Arzttermin", $"Ausfall {Datum(a.Von)}, {stunden}" });
            sb.Append($"{satz}, deshalb fehle ich {tag} {stunden}.");
        }
        else
        {
            betreff = z.Wahl(new[] { "Krankmeldung", $"Krankmeldung {Name(s, a.Lehrer, Zufall.Fuer(w.Seed, "n", a.Lehrer))}", $"Krankmeldung {tag}", $"Ausfall {Datum(a.Von)}", "Bin leider krank" });
            sb.Append(a.Art.Id == "kind" ? $"{satz}, deshalb kann ich {tag} leider nicht in die Schule kommen." : $"{satz}, deshalb muss ich mich für {tag} leider krankmelden.");
            if (bis > a.Von)
                sb.Append(' ').Append(z.Wahl(new[]
                {
                    $"Voraussichtlich falle ich bis einschließlich {Datum(bis)} aus.", $"Krankgeschrieben bin ich bis {Datum(bis)}.",
                    $"Ich rechne damit, bis {Datum(bis)} auszufallen.", $"Die Krankschreibung gilt bis {Datum(bis)}.",
                }));
            else if (z.Chance(0.5))
                sb.Append(' ').Append(z.Wahl(new[] { "Ich hoffe, dass ich übermorgen wieder fit bin.", "Ich melde mich, falls es länger dauert.", "Wenn es besser wird, bin ich am nächsten Tag wieder da." }));
        }
        var betroffen = BetroffeneStunden(s, a, bis);
        var absatz = BetroffenText(betroffen, z, a.Tage.Count(t => t <= bis) > 1);
        if (absatz.Length > 0) sb.Append("\n\n").Append(absatz);
        var bk = betroffen.Any(b => b.Klassen.Split('/').Any(k => Bildungsgang(s, k) is not ("sek1u" or "sek1o" or "sek2")));
        var berufsschule = betroffen.Any(b => b.Klassen.Split('/').Any(k => Bildungsgang(s, k) == "bs"));
        var aufgaben = AufgabenText(betroffen, z, bk, berufsschule);
        if (aufgaben.Length > 0) sb.Append(absatz.Length > 0 ? " " : "\n\n").Append(aufgaben);
        if (!a.Art.Teil && (_uhr.Jetzt.Hour < 7 || _uhr.Jetzt.Hour >= 22) && z.Chance(0.6)) sb.Append("\n\nEntschuldigung für die kurzfristige Meldung.");
        sb.Append("\n\n").Append(z.Wahl(Gruesse)).Append('\n').Append(Name(s, a.Lehrer, z));
        return (betreff, sb.ToString().Replace(".. ", ". ").Replace("..\n", ".\n"));
    }

    /// <summary>Stunde, die wegen des Ausfalls entfällt bzw. vertreten werden muss (laut Plan der Lehrkraft).</summary>
    public sealed record BetroffeneStunde(DateOnly Tag, int Von, int Bis, string Klassen, string Fach);

    /// <summary>Gehaltene Unterrichtsstunden der Lehrkraft an den Ausfalltagen bis <paramref name="bis"/> (beim Arzttermin nur die betroffenen Stunden).</summary>
    public List<BetroffeneStunde> BetroffeneStunden(Stand s, Ausfall a, DateOnly bis)
    {
        var res = new List<BetroffeneStunde>();
        foreach (var d in a.Tage.Where(t => t <= bis))
        {
            List<Karte> karten;
            try { karten = _plan.Karten(s, d, Elementtyp.Lehrer, a.Lehrer); }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { continue; }
            foreach (var k in karten.Where(k => Gehalten(k) && k.Klassen.Count > 0 && (!a.Art.Teil || (k.Bis >= a.StundeVon && k.Von <= a.StundeBis)))
                         .Where(k => Zuordnung(k.Fach, s.Faecher.TryGetValue(k.Fach, out var f) ? f.Name : null) is not null)
                         .DistinctBy(k => (k.Unterricht, k.Von)).OrderBy(k => k.Von))
                res.Add(new(d, k.Von, k.Bis, string.Join("/", k.Klassen.Select(x => x.K)), k.Fach));
        }
        return res;
    }

    private static string Stunden(int von, int bis) => von == bis ? $"{von}." : bis == von + 1 ? $"{von}./{bis}." : $"{von}. bis {bis}.";

    private static string Aufzaehlung(IReadOnlyList<string> teile) => teile.Count <= 1 ? string.Join("", teile) : string.Join(", ", teile.Take(teile.Count - 1)) + " und " + teile[^1];

    private static string Kurztag(DateOnly d) => $"{Wochentage[((int)d.DayOfWeek + 6) % 7][..2]} {d.ToString("dd.MM.", De)}";

    /// <summary>Welche Klassen betroffen sind: nur die laut Plan im Ausfallzeitraum, mit Tag und Stunde bzw. knapp als Klassenliste.</summary>
    private static string BetroffenText(List<BetroffeneStunde> b, Zufall z, bool mehrereTage)
    {
        if (b.Count == 0) return z.Chance(0.4) ? "Unterricht hätte ich in dieser Zeit ohnehin keinen." : "";
        var klassen = b.Select(x => x.Klassen).Distinct().ToList();
        var form = z.Wert();
        if (form < 0.35) return $"Betroffen {(klassen.Count == 1 ? "ist die Klasse" : "sind die Klassen")} {Aufzaehlung(klassen)}.";
        if (mehrereTage)
            return "Betroffen:\n" + string.Join("\n", b.GroupBy(x => x.Tag).Select(g => $"{Kurztag(g.Key)}: {string.Join(", ", g.Select(x => $"{x.Klassen} ({Stunden(x.Von, x.Bis)} Std.)"))}"));
        if (form < 0.7) return $"Betroffen {(b.Count == 1 ? "ist" : "sind")} {Aufzaehlung(b.Select(x => $"die {Stunden(x.Von, x.Bis)} Stunde in der {x.Klassen} ({x.Fach})").ToList())}.";
        return $"Klassen {string.Join(", ", klassen)}.";
    }

    /// <summary>Aufgaben: meist über Teams, manchmal keine Angabe, selten eine konkrete Aufgabe für eine der betroffenen Klassen.</summary>
    public static string AufgabenText(List<BetroffeneStunde> b, Zufall z, bool bk = false, bool berufsschule = false)
    {
        if (b.Count == 0) return "";
        var w = z.Wert();
        // Berufsschule: entfallen die letzten Stunden, kann die Klasse früher in die Betriebe
        var betriebe = berufsschule && z.Chance(0.3) ? " Falls die letzten Stunden entfallen, kann die Klasse gern früher in die Betriebe." : "";
        if (w < 0.32) return betriebe.TrimStart();
        if (bk && w < 0.5) return z.Wahl(new[] { "Material liegt im Moodle-Kurs.", "Die Aufgaben liegen in LOGINEO.", "Arbeitsaufträge liegen im Moodle-Kurs der Klasse." }) + betriebe;
        if (w < 0.86) return z.Wahl(new[]
        {
            "Aufgaben liegen in Teams.", "Die Aufgaben liegen in Teams.", "Aufgaben habe ich in Teams hochgeladen.", "Arbeitsaufträge stehen im jeweiligen Teams-Kanal.",
            "Die Klassen finden ihre Aufgaben in Teams.", "Aufgaben sind in Teams unter Dateien.", "Material ist in Teams, die Klassen wissen Bescheid.",
        }) + betriebe;
        var k = z.Wahl(b).Klassen;
        return z.Wahl(new[] { $"Die {k} bearbeitet Buch S. {{s}} Nr. {{a}} bis {{b}}.", $"Die {k} kann am Arbeitsblatt aus der letzten Stunde weiterarbeiten.",
            $"Für die {k} liegen Kopien in meinem Fach im Lehrerzimmer." }) is var t ? z.Zahlen(t) : "";
    }

    private (string, string) Verlaengerung(Stand s, Ausfall a, DateOnly bis, Zufall z)
    {
        var text = $"{z.Wahl(Anreden)}\n\n{z.Wahl(new[] { "ich bin leider weiterhin krankgeschrieben.", "leider geht es mir noch nicht besser.", "meine Krankschreibung wurde verlängert." })} " +
                   $"{z.Wahl(new[] { $"Die Folgebescheinigung gilt bis {Datum(bis)}.", $"Ich falle voraussichtlich bis {Datum(bis)} aus.", $"Die neue Bescheinigung reicht bis {Datum(bis)}." })}\n\n" +
                   $"{z.Wahl(Gruesse)}\n{Name(s, a.Lehrer, z)}";
        return (z.Wahl(new[] { "Verlängerung der Krankmeldung", "Krankmeldung verlängert", "Folgebescheinigung" }), text);
    }

    private (string, string) Gesundmeldung(Stand s, Weltstand w, Ausfall a, Zufall z)
    {
        var naechster = w.Schultag(a.Bis.AddDays(1), 0);
        var wann = naechster == _uhr.Heute.AddDays(1) ? "morgen" : $"am {Datum(naechster)}";
        var text = $"{z.Wahl(Anreden)}\n\n{z.Wahl(new[] { $"ich bin {wann} wieder im Dienst.", $"mir geht es besser, ich bin {wann} wieder da.", $"kurze Info: Ich bin {wann} wieder in der Schule." })}" +
                   (z.Chance(0.4) ? " Danke fürs Vertreten!" : "") + $"\n\n{z.Wahl(Gruesse)}\n{Name(s, a.Lehrer, z)}";
        return (z.Wahl(new[] { "Gesundmeldung", $"Ab {(wann == "morgen" ? "morgen" : Datum(naechster))} wieder da", "Wieder gesund" }), text);
    }

    // ------------------------------------------------------------------------------------------ Klassenarbeiten und Klausuren

    /// <summary>Geplante Prüfung (eine je Klasse, Fach und Halbjahr).</summary>
    public sealed record Pruefungsplan(string Klasse, string Fach, DateOnly Datum, int Von, int Bis, string Lehrer, List<string> Raeume, DateTime Eintragen, DateOnly HjVon, DateOnly HjBis)
    {
        public int Unterricht { get; init; }
        /// <summary>Schriftliche Abschlussprüfung (Fachoberschule 12 und 13 im Frühjahr) statt einer Klassenarbeit bzw. Klausur.</summary>
        public bool Abschluss { get; init; }
        /// <summary>Angekündigter Test (eine Stunde, zählt nicht als Klassenarbeit bzw. Klausur des Fachs).</summary>
        public bool Test { get; init; }
    }

    /// <summary>Art der Prüfung: Klausur in der Oberstufe, im Beruflichen Gymnasium, in der Fachoberschule 12 und 13 und in der Fachschule, sonst Klassenarbeit.</summary>
    public static string Pruefungsart(Stand s, string klasse) => Bildungsgang(s, klasse) is "sek2" or "bg" or "fos12" or "fos13" or "fs" ? "Klausur" : "Klassenarbeit";

    private static readonly string[] Abschlussfaecher = { "D", "DEU", "M", "MDQ", "E", "FEN" };

    private readonly Dictionary<string, List<Pruefungsplan>> _pruefungsCache = new();

    /// <summary>Halbjahre des Schuljahres: bis Ende Januar und ab Februar.</summary>
    public static List<(DateOnly Von, DateOnly Bis)> Halbjahre(Stand s)
    {
        var von = s.SchuljahrVon == DateOnly.MinValue ? new DateOnly(DateTime.Today.Year, 8, 1) : s.SchuljahrVon;
        var bis = s.SchuljahrBis == DateOnly.MaxValue ? von.AddYears(1).AddDays(-1) : s.SchuljahrBis;
        var wechsel = new DateOnly(von.Month >= 8 ? von.Year + 1 : von.Year, 2, 1);
        return wechsel > von && wechsel < bis ? new() { (von, wechsel.AddDays(-1)), (wechsel, bis) } : new() { (von, bis) };
    }

    private IEnumerable<Ereignis> Pruefungsereignisse(Stand s, Simulationseinstellungen e, Weltstand w, DateTime start, DateTime bis)
    {
        foreach (var (hv, hb) in Halbjahre(s))
        {
            if (hb < DateOnly.FromDateTime(start) || hv > DateOnly.FromDateTime(bis).AddDays(60)) continue;
            foreach (var p in Pruefungsplanung(s, e, w, hv, hb, start))
                yield return new($"pr|{hv:yyyy-MM-dd}|{p.Klasse}|{p.Fach}{(p.Test ? "|test" : "")}", p.Eintragen, () => PruefungEintragen(s, e, p));
        }
    }

    /// <summary>
    /// Je Klasse und Prüfungsfach (Sek I bzw. Oberstufe laut Einstellungen) eine Prüfung im Halbjahr: an einem Tag mit Unterricht in diesem Fach, nicht in den ersten vier
    /// und den letzten zwei Wochen, höchstens eine je Tag und zwei je Woche. Eingetragen wird sie zwei bis fünf Wochen vorher (frühestens beim Start).
    /// </summary>
    public List<Pruefungsplan> Pruefungsplanung(Stand s, Simulationseinstellungen e, Weltstand w, DateOnly hv, DateOnly hb, DateTime start)
    {
        var (sekI, sekII) = Pruefungsfaecher(s, e);
        var schluessel = $"{w.Seed}|{s.Empfangen}|{hv:yyyy-MM-dd}|{start:yyyy-MM-dd HH:mm}|{string.Join(",", sekI.Order(StringComparer.Ordinal))}|{string.Join(",", sekII.Order(StringComparer.Ordinal))}|{string.Join(",", w.Ausgenommen.Order(StringComparer.Ordinal))}|{e.Start}";
        if (_pruefungsCache.TryGetValue(schluessel, out var fertig)) return fertig;
        if (_pruefungsCache.Count > 4) _pruefungsCache.Clear();
        var res = new List<Pruefungsplan>();
        // frühestens eine Woche nach dem Start (vorher geplante Prüfungen stehen gleich nach dem Start im Kalender)
        var tage = Schuljahrestage(s).Where(d => d >= hv.AddDays(28) && d <= hb.AddDays(-14) && d >= DateOnly.FromDateTime(start).AddDays(7)).ToList();
        foreach (var kl in s.Paket.Elemente.Klassen.OrderBy(k => k.Kurz, StringComparer.Ordinal))
        {
            var z = Zufall.Fuer(w.Seed, "pr", hv, kl.Kurz);
            // Sek II und Berufskolleg: Liste der Oberstufe; Sek I (Klassen 5 bis 10): eigene Liste
            var erlaubt = Stufe(s, kl.Kurz) is "sek2" or "bk" ? sekII : sekI;
            // Kandidaten je Fach: Tage mit einer gehaltenen Stunde des Fachs bei einer nicht ausgenommenen Lehrkraft
            var kandidaten = new Dictionary<string, List<(DateOnly Tag, Karte Karte)>>(Ci);
            foreach (var d in tage)
            {
                List<Karte> karten;
                try { karten = _plan.Karten(s, d, Elementtyp.Klasse, kl.Kurz); }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { continue; }
                foreach (var k in karten.Where(k => k.Unterricht.HasValue && k.Status is "regulaer" && k.Fach.Length > 0 && erlaubt.Contains(k.Fach) && !k.Bereitschaft
                                                    && k.Lehrer.Count > 0 && !w.Ausgenommen.Contains(k.Lehrer[0].K)))
                {
                    if (!kandidaten.TryGetValue(k.Fach, out var l)) kandidaten[k.Fach] = l = new();
                    if (!l.Any(x => x.Tag == d)) l.Add((d, k));
                }
            }
            var belegt = new HashSet<DateOnly>();
            // Fachoberschule 12 und 13: im zweiten Halbjahr schriftliche Abschlussprüfungen in Deutsch, Mathematik und Englisch (Mitte April bis Ende Mai)
            var abschlussKlasse = Bildungsgang(s, kl.Kurz) is "fos12" or "fos13" && hv.Month < 8;
            foreach (var fach in kandidaten.Keys.Order(StringComparer.Ordinal).OrderBy(_ => z.Naechste()).ToList())
            {
                var abschluss = abschlussKlasse && Abschlussfaecher.Contains(fach, Ci);
                var moeglich = kandidaten[fach].Where(x => !belegt.Contains(x.Tag) && belegt.Count(b => Plandienst.Montag(b) == Plandienst.Montag(x.Tag)) < 2)
                    .Where(x => !abschluss || (x.Tag >= new DateOnly(x.Tag.Year, 4, 15) && x.Tag <= new DateOnly(x.Tag.Year, 5, 31))).ToList();
                if (moeglich.Count == 0) continue;
                // gehäuft in den letzten Wochen vor Ferien bzw. vor dem Halbjahresende (dessen letzte zwei Wochen sind ohnehin frei)
                var (tag, karte) = z.Gewichtet(moeglich, x => Vorferiengewicht(s, x.Tag, hb));
                belegt.Add(tag);
                var eintragen = Um(tag.AddDays(-z.Zahl(14, 35)), z.Zahl(14 * 60, 20 * 60 + 30));
                // vor dem Start geplante Prüfungen stehen gleich nach dem Start im Kalender, aber nicht kurzfristiger als eine Woche
                if (eintragen < start) eintragen = start.AddMinutes(z.Zahl(3, 300));
                res.Add(new(kl.Kurz, karte.Fach, tag, karte.Von, karte.Bis, karte.Lehrer[0].K, karte.Raeume.Select(r => r.K).Where(r => r.Length > 0).ToList(), eintragen, hv, hb) { Unterricht = karte.Unterricht ?? 0, Abschluss = abschluss });
            }
            // zusätzlich selten ein angekündigter Test (nur eine Stunde, kurzfristig eingetragen), nicht am Tag einer anderen Prüfung
            foreach (var fach in kandidaten.Keys.Order(StringComparer.Ordinal).ToList())
            {
                var zt = Zufall.Fuer(w.Seed, "test", hv, kl.Kurz, fach);
                if (!zt.Chance(0.3)) continue;
                var moeglich = kandidaten[fach].Where(x => !belegt.Contains(x.Tag) && belegt.Count(b => Plandienst.Montag(b) == Plandienst.Montag(x.Tag)) < 3).ToList();
                if (moeglich.Count == 0) continue;
                var (tag, karte) = zt.Wahl(moeglich);
                belegt.Add(tag);
                var eintragen = Um(tag.AddDays(-zt.Zahl(1, 6)), zt.Zahl(14 * 60, 20 * 60 + 30));
                if (eintragen < start) eintragen = start.AddMinutes(zt.Zahl(3, 300));
                res.Add(new(kl.Kurz, karte.Fach, tag, karte.Von, karte.Von, karte.Lehrer[0].K, karte.Raeume.Select(r => r.K).Where(r => r.Length > 0).ToList(), eintragen, hv, hb) { Unterricht = karte.Unterricht ?? 0, Test = true });
            }
        }
        _pruefungsCache[schluessel] = res;
        return res;
    }

    /// <summary>Gewicht eines Prüfungstages: drei Wochen vor längeren Ferien bzw. dem Halbjahresende dreifach, fünf Wochen vorher doppelt.</summary>
    private double Vorferiengewicht(Stand s, DateOnly tag, DateOnly hb)
    {
        var tage = Schuljahrestage(s);
        var i = tage.BinarySearch(tag);
        if (i < 0) return 1;
        var bisFerien = int.MaxValue;
        for (var j = i; j + 1 < tage.Count && tage[j] <= tag.AddDays(40); j++)
            if (tage[j + 1].DayNumber - tage[j].DayNumber > 4) { bisFerien = tage[j].DayNumber - tag.DayNumber; break; }
        var bisEnde = hb.AddDays(-14).DayNumber - tag.DayNumber;
        var abstand = Math.Min(bisFerien, bisEnde);
        return abstand <= 21 ? 3 : abstand <= 35 ? 2 : 1;
    }

    private void PruefungEintragen(Stand s, Simulationseinstellungen e, Pruefungsplan p)
    {
        // schon eine Prüfung im Fach (z. B. selbst eingetragen) bzw. am Tag: nichts doppelt
        var vorhanden = _listen.Pruefungen(s, _rm.OffeneKlausuren(s), p.HjVon, p.HjBis, p.Klasse, null, _rm.OffeneLoeschungen());
        var fachName = s.Faecher.TryGetValue(p.Fach, out var f) ? f.Name : p.Fach;
        // Tests zählen nicht als Klassenarbeit bzw. Klausur des Fachs
        if (!p.Test && vorhanden.Where(x => x.Art != "Test").Any(x => Ci.Equals(x.Fach, p.Fach) || (fachName.Length > 2 && x.Titel.Contains(fachName, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Im Fach gibt es in diesem Halbjahr schon eine Prüfung.");
        if (vorhanden.Any(x => x.Datum == Plandienst.Iso(p.Datum))) throw new InvalidOperationException("An diesem Tag gibt es schon eine Prüfung.");
        var b = LehrerKonto(s, p.Lehrer);
        ErweiterungApi.PruefungssperrePruefen(_vw, _listen, _rm, s, b, p.Datum, new[] { p.Klasse });
        var art = p.Test ? "Test" : p.Abschluss ? "Abschlussprüfung" : Pruefungsart(s, p.Klasse);
        var z = Zufall.Fuer(Laden().Seed, "prt", p.Klasse, p.Fach, p.Datum);
        // Thema: die Unterrichtsreihe, die zum Prüfungstermin läuft
        var thema = Lehrstoff(s, Welt(s, e, DateTime.Parse(e.Start, Inv), _uhr.Jetzt), e, p.Fach, p.Klasse, p.Unterricht, p.Datum, p.Von) is { } ls && ls.StartsWith(art + ": ", StringComparison.Ordinal) ? ls[(art.Length + 2)..] : fachName;
        _rm.Klausur(s, b, p.Datum, p.Von, p.Bis, new[] { p.Klasse }, Pruefungsraeume(s, e, p, z), Array.Empty<string>(), $"{art} {p.Fach}", $"{art}: {thema}");
    }

    /// <summary>
    /// Räume der Prüfung: meist der Raum der Stunde. Bei einem Teil der Prüfungen (Einstellung, in der Oberstufe häufiger, in der Sek I ein Drittel davon)
    /// legt die Lehrkraft einen eigenen Prüfungsraum fest: einen freien buchbaren Raum derselben Art und mindestens gleicher Größe, in der Oberstufe
    /// manchmal zwei (Abstand beim Schreiben). Nie Sporthallen, Computer- oder Fachräume.
    /// </summary>
    public List<string> Pruefungsraeume(Stand s, Simulationseinstellungen e, Pruefungsplan p, Zufall z)
    {
        if (p.Test) return p.Raeume;
        var sekII = Oberstufe(s, p.Klasse);
        var anteil = Math.Clamp(e.Klausurraumanteil, 0, 100) / 100.0 * (sekII ? 1.0 : 1.0 / 3);
        if (anteil <= 0 || !z.Chance(anteil)) return p.Raeume;
        var bisher = p.Raeume.Where(s.Raeume.ContainsKey).Select(r => s.Raeume[r]).ToList();
        var art = bisher.FirstOrDefault()?.Raumart ?? "";
        var groesse = bisher.Select(r => r.Kapazitaet).DefaultIfEmpty(0).Max();
        static bool Ungeeignet(WebRaum r) => new[] { "Sport", "Halle", "EDV", "Computer", "Fachraum" }.Any(x => r.Raumart.Contains(x, StringComparison.OrdinalIgnoreCase));
        // Positivliste der Prüfungsräume (Einstellung) bzw. Automatik nach Raumart und Größe; ausgeschlossene Räume nie
        var aus = e.RaeumeAusgeschlossen.ToHashSet(Ci);
        var liste = e.Klausurraeume.ToHashSet(Ci);
        var frei = (liste.Count > 0
                ? s.Paket.Elemente.Raeume.Where(r => liste.Contains(r.Kurz))
                : Rueckmeldungsdienst.Buchbar(s).Where(r => !Ungeeignet(r) && (art.Length == 0 || Ci.Equals(r.Raumart, art)) && r.Kapazitaet >= groesse))
            .Where(r => !aus.Contains(r.Kurz) && !p.Raeume.Contains(r.Kurz, Ci) && _rm.RaumFrei(s, p.Datum, r.Kurz, p.Von, p.Bis)).DistinctBy(r => r.Kurz, Ci).OrderBy(r => r.Kurz, StringComparer.Ordinal).ToList();
        if (frei.Count == 0) return p.Raeume;
        var erster = z.Wahl(frei).Kurz;
        var zweiter = sekII && frei.Count > 1 && z.Chance(0.25) ? frei.Where(r => r.Kurz != erster).ToList() is { Count: > 0 } rest ? z.Wahl(rest).Kurz : null : null;
        return zweiter is null ? new() { erster } : new() { erster, zweiter };
    }

    // ------------------------------------------------------------------------------------------ Sprechtag

    private static DateTime? Zeitpunkt(string s) => DateTime.TryParse(s, Inv, DateTimeStyles.None, out var t) ? t : null;

    private IEnumerable<Ereignis> Sprechtagereignisse(Stand s, Simulationseinstellungen e, Weltstand w, DateTime start)
    {
        List<Sprechtag> alle;
        try { alle = _st.Alle(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { yield break; }
        foreach (var t in alle.Where(t => t.IstVeroeffentlicht && t.K.Abschnitte.Count > 0))
        {
            var p1 = Zeitpunkt(t.K.Phase1Von);
            var p1b = Zeitpunkt(t.K.Phase1Bis);
            var p2 = Zeitpunkt(t.K.Phase2Von);
            var p2b = Zeitpunkt(t.K.Phase2Bis);
            var veroeffentlicht = Zeitpunkt(t.Veroeffentlicht) ?? start;
            var frueh = Max(start.AddMinutes(1), veroeffentlicht.AddMinutes(1));
            foreach (var l in w.KlassenDerLehrkraft.Keys.Where(l => !w.Ausgenommen.Contains(l) && s.Lehrer.ContainsKey(l)).Order(StringComparer.Ordinal))
            {
                var z = Zufall.Fuer(w.Seed, "st", t.Id, l);
                // Berufskolleg: eher Ausbildersprechtag, Eltern nur bei Minderjährigen, insgesamt wenige Termine
                var nurBk = w.KlassenDerLehrkraft.TryGetValue(l, out var kl0) && kl0.Count > 0 && kl0.All(k => Bildungsgang(s, k) is not ("sek1u" or "sek1o" or "sek2"));
                if (!z.Chance(nurBk ? 0.25 : 0.6)) continue;
                var schueler = Sprechtagdienst.SchuelerDerLehrkraft(s, l).Where(x => Sprechtagberechtigt(s, x)).OrderBy(x => x.Id).ToList();
                if (schueler.Count == 0) continue;
                var eingeladen = Enumerable.Range(0, z.Zahl(1, 4)).Select(_ => z.Wahl(schueler).Id).Distinct().ToList();
                var einladen = p1 is DateTime a1 ? a1.AddHours(-z.Zahl(2, 30)) : frueh.AddHours(z.Zahl(1, 48));
                var verpflichtend = z.Chance(0.25);
                yield return new($"st|{t.Id}|ein|{l}", Max(einladen, frueh), () => _st.Einladen(s, t, LehrerKonto(s, l), l, eingeladen,
                    verpflichtend ? "Bitte kommen Sie zum Gespräch über die Leistungen Ihres Kindes." : "Ich würde mich über ein Gespräch freuen.", verpflichtend));
                foreach (var sid in eingeladen)
                {
                    var zb = Zufall.Fuer(w.Seed, "stb", t.Id, l, sid);
                    if (!zb.Chance(0.75)) continue;
                    var (von, bis) = p1 is DateTime x1 && p1b is DateTime y1 ? (x1, y1) : p2 is DateTime x2 && p2b is DateTime y2 ? (x2, y2) : (DateTime.MinValue, DateTime.MinValue);
                    if (von == DateTime.MinValue || bis <= von) continue;
                    var zeit = Max(Einschnitt(von, bis, zb), frueh.AddMinutes(30));
                    yield return new($"st|{t.Id}|bu|{l}|{sid}", zeit, () => Buchen(s, t, l, sid, zb, true));
                }
            }
            if (p2 is DateTime v2 && p2b is DateTime b2 && b2 > v2)
                foreach (var sch in s.Schueler.OrderBy(x => x.Id))
                {
                    var z = Zufall.Fuer(w.Seed, "stf", t.Id, sch.Id);
                    if (!Sprechtagberechtigt(s, sch) || !z.Chance(Bildungsgang(s, sch.Klasse) is "sek1u" or "sek1o" or "sek2" ? 0.12 : 0.03)) continue;
                    var lehrkraefte = Sprechtagdienst.LehrkraefteDesKindes(s, sch).Where(x => !w.Ausgenommen.Contains(x.Kurz)).ToList();
                    if (lehrkraefte.Count == 0) continue;
                    foreach (var lk in Enumerable.Range(0, z.Zahl(1, 2)).Select(_ => z.Wahl(lehrkraefte).Kurz).Distinct(Ci))
                    {
                        var zb = Zufall.Fuer(w.Seed, "stfb", t.Id, sch.Id, lk);
                        yield return new($"st|{t.Id}|frei|{sch.Id}|{lk}", Max(Einschnitt(v2, b2, zb), frueh.AddMinutes(30)), () => Buchen(s, t, lk, sch.Id, zb, false));
                    }
                }
        }
    }

    /// <summary>Sprechtag: Berufsschule über die Ausbildungsbetriebe; übrige Bildungsgänge des Berufskollegs nur für Minderjährige (Eltern).</summary>
    public static bool Sprechtagberechtigt(Stand s, WebSchueler x)
    {
        var bg = Bildungsgang(s, x.Klasse);
        return bg is "sek1u" or "sek1o" or "sek2" or "bs" or "bk" || !Volljaehrig(bg, x.Id);
    }

    /// <summary>Zeitpunkt in der Anmeldephase: meist in den ersten Tagen, abends gehäuft.</summary>
    private static DateTime Einschnitt(DateTime von, DateTime bis, Zufall z)
    {
        var tage = Math.Max(0, (int)(bis - von).TotalDays);
        var tag = DateOnly.FromDateTime(von).AddDays(Math.Min(tage, (int)(Math.Pow(z.Wert(), 2) * (tage + 1))));
        var t = Um(tag, z.Chance(0.6) ? z.Zahl(18 * 60, 22 * 60) : z.Zahl(7 * 60, 17 * 60));
        return t < von ? von.AddMinutes(z.Zahl(5, 120)) : t > bis ? bis.AddMinutes(-z.Zahl(5, 60)) : t;
    }

    private void Buchen(Stand s, Sprechtag t, string lehrer, int schuelerId, Zufall z, bool einladung)
    {
        var aktuell = _st.HolenOderFehler(t.Id);
        int? einladungId = null;
        if (einladung)
        {
            einladungId = _st.Termine(t.Id).FirstOrDefault(x => x.Status == "einladung" && x.SchuelerId == schuelerId && Ci.Equals(x.Lehrer, lehrer))?.Id
                          ?? throw new InvalidOperationException("Keine offene Einladung.");
        }
        var eltern = ElternKonto(s, schuelerId);
        var abschnitte = Enumerable.Range(0, aktuell.K.Abschnitte.Count).OrderBy(_ => z.Naechste()).ToList();
        foreach (var a in abschnitte)
        {
            var frei = _st.Frei(s, aktuell, lehrer, a, schuelerId);
            if (frei.Count == 0) continue;
            _st.Buchen(s, aktuell, eltern, lehrer, a, z.Wahl(frei).Von, schuelerId, einladungId);
            return;
        }
        throw new InvalidOperationException("Kein freier Termin.");
    }
}
