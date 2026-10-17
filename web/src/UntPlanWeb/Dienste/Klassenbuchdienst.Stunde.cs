using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Hausaufgabe einer Stunde (mehrere je Stunde möglich): Text, Fälligkeit, Bemerkung, erledigt.</summary>
public sealed record Hausaufgabe(int Id, string Datum, int Stunde, int Unterricht, List<string> Klassen, string Fach, string Lehrer,
    string Text, string? Faellig, string Bemerkung, bool Erledigt, string Erstellt)
{
    /// <summary>Einzelstunde eines Blocks, für die die Hausaufgabe allein gilt (dann <see cref="Stunde"/> = diese Stunde); null = ganzer Block.</summary>
    public int? Einzelstunde { get; init; }
}

/// <summary>
/// Klassenbuch einer Stunde wie im Original:
/// <list type="bullet">
/// <item>Hausaufgaben als Liste (Neue Hausaufgabe, Fälligkeit standardmäßig eine Woche später, Bemerkung, erledigt, löschen);
///   eine Hausaufgabe ist „fällig“ in der Stunde desselben Fachs und derselben Klasse am Fälligkeitstag.</item>
/// <item>Notizen und fällige Hausaufgaben für die Symbole auf den Stundenkarten.</item>
/// <item>Abwesenheiten verlängern: aus der Vorstunde in diese Stunde bzw. von dieser Stunde bis zum Tagesbeginn.</item>
/// </list>
/// </summary>
public sealed partial class Klassenbuchdienst
{
    private static Hausaufgabe LesenHausaufgabe(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("datum"), Math.Abs(r.Zahl("stunde")), r.Zahl("unterricht"),
        JsonSerializer.Deserialize<List<string>>(r.Text("klassen")) ?? new(), r.Text("fach"), r.Text("lehrer"), r.Text("text"), r.TextOderNull("faellig"),
        r.Text("bemerkung"), r.Wahr("erledigt"), r.Text("erstellt")) { Einzelstunde = r.Zahl("stunde") < 0 ? -r.Zahl("stunde") : null };

    /// <summary>Hausaufgaben, die in einer Stunde (Block Von–Bis) aufgegeben wurden.</summary>
    public List<Hausaufgabe> HausaufgabenDerStunde(DateOnly d, int von, int bis, int unterricht) =>
        _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE datum = @d AND unterricht = @u AND stunde >= @v AND stunde <= @b ORDER BY id", LesenHausaufgabe,
            Db.P("@d", d), Db.P("@u", unterricht), Db.P("@v", von), Db.P("@b", bis));

    /// <summary>Hausaufgaben genau zur Karte: Block (Stunden von–bis) bzw. nur die Einzelstunde.</summary>
    private List<Hausaufgabe> HausaufgabenDerKarte(DateOnly d, Karte k) => k.Einzelstunde is int n
        ? _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE datum = @d AND unterricht = @u AND stunde = @s ORDER BY id", LesenHausaufgabe,
            Db.P("@d", d), Db.P("@u", k.Unterricht ?? 0), Db.P("@s", -n))
        : HausaufgabenDerStunde(d, k.Von, k.Bis, k.Unterricht ?? 0);

    /// <summary>
    /// Hausaufgaben für die Stundendetails: die des Blocks (gelten für alle Stunden) und die der Einzelstunden – ohne
    /// <paramref name="einzelstunde"/> alle Einzelstunden von–bis, sonst nur diese.
    /// </summary>
    public List<Hausaufgabe> HausaufgabenMitEinzelstunden(DateOnly d, int von, int bis, int unterricht, int? einzelstunde = null)
    {
        var (ev, eb) = einzelstunde is int n ? (n, n) : (von, bis);
        return HausaufgabenDerStunde(d, von, bis, unterricht)
            .Concat(_db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE datum = @d AND unterricht = @u AND stunde <= @v AND stunde >= @b ORDER BY id", LesenHausaufgabe,
                Db.P("@d", d), Db.P("@u", unterricht), Db.P("@v", -ev), Db.P("@b", -eb)))
            .OrderBy(h => h.Id).ToList();
    }

    /// <summary>
    /// Hausaufgaben einer Klasse im Zeitraum: aufgegeben oder fällig im Zeitraum (nachFaelligkeit: nur fällig im Zeitraum bzw. ohne
    /// Fälligkeit aufgegeben im Zeitraum), sortiert nach Fälligkeit.
    /// </summary>
    public List<Hausaufgabe> HausaufgabenZeitraum(DateOnly von, DateOnly bis, string klasse, bool nachFaelligkeit = false) =>
        _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE (datum >= @v AND datum <= @b) OR (faellig >= @v AND faellig <= @b) ORDER BY faellig, datum, stunde, id", LesenHausaufgabe,
                Db.P("@v", von), Db.P("@b", bis))
            .Where(h => h.Klassen.Contains(klasse, Ci) && (!nachFaelligkeit || (h.Faellig is string f ? string.CompareOrdinal(f, Plandienst.Iso(von)) >= 0 && string.CompareOrdinal(f, Plandienst.Iso(bis)) <= 0 : true)))
            .OrderBy(h => h.Faellig ?? h.Datum, StringComparer.Ordinal).ThenBy(h => h.Datum, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Hausaufgaben (Liste Klassenbuch → Hausaufgaben): aufgegeben oder fällig im Zeitraum, optional nur einer Klasse bzw. Lehrkraft;
    /// sortiert nach Aufgabedatum. Ältere Einträge, die nur im Klassenbuch-Feld stehen, kommen ohne Id (nur lesbar) dazu.
    /// </summary>
    public List<Hausaufgabe> Hausaufgabenliste(DateOnly von, DateOnly bis, string? klasse, string? lehrer)
    {
        var liste = _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE (datum >= @v AND datum <= @b) OR (faellig >= @v AND faellig <= @b) ORDER BY datum, stunde, id", LesenHausaufgabe,
            Db.P("@v", von), Db.P("@b", bis));
        var bekannt = liste.Select(h => (h.Datum, h.Unterricht, h.Stunde)).ToHashSet();
        var alt = Zeitraum(von, bis).Where(e => e.Hausaufgabe.Length > 0 && !bekannt.Contains((e.Datum, e.Unterricht, e.Stunde)))
            .Select(e => new Hausaufgabe(0, e.Datum, e.Stunde, e.Unterricht, e.Klassen, e.Fach, e.Lehrer, e.Hausaufgabe, e.Faellig, "", false, e.Geaendert));
        return liste.Concat(alt)
            .Where(h => (string.IsNullOrEmpty(klasse) || h.Klassen.Contains(klasse, Ci)) && (string.IsNullOrEmpty(lehrer) || Ci.Equals(h.Lehrer, lehrer)))
            .OrderBy(h => h.Datum, StringComparer.Ordinal).ThenBy(h => h.Stunde).ThenBy(h => h.Id).ToList();
    }

    /// <summary>Hausaufgaben, die an einem Tag in Stunden dieses Fachs und dieser Klassen fällig sind (aus früheren Stunden).</summary>
    public List<Hausaufgabe> FaelligeHausaufgaben(DateOnly d, string fach, IEnumerable<string> klassen, int? ausserUnterricht = null)
    {
        var k = klassen.ToHashSet(Ci);
        return _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE faellig = @d ORDER BY id", LesenHausaufgabe, Db.P("@d", d))
            .Where(h => Ci.Equals(h.Fach, fach) && h.Klassen.Any(k.Contains) && !(h.Datum == Plandienst.Iso(d) && h.Unterricht == ausserUnterricht)).ToList();
    }

    private Karte StundeZu(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, int? einzelstunde = null) =>
        KarteZu(b, s, d, stunde, unterricht, null, einzelstunde) ?? throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");

    public Hausaufgabe HausaufgabeAnlegen(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, string text, DateOnly? faellig, string bemerkung, int? einzelstunde = null)
    {
        RechtePruefung.Verlangen(b, Recht.HausaufgabenEintragen);
        if (!DarfSchreiben(b, s, d, stunde, unterricht)) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte die Hausaufgabe eingeben.");
        if (faellig is DateOnly f && f < d) throw new ArgumentException("Die Hausaufgabe kann nicht vor der Stunde fällig sein.");
        var karte = StundeZu(b, s, d, stunde, unterricht, einzelstunde);
        var lehrer = b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "";
        var id = (int)_db.Einfuegen("""
            INSERT INTO hausaufgaben_eintraege (datum, stunde, unterricht, klassen, fach, lehrer, text, faellig, bemerkung, erledigt, erstellt, benutzer_id)
            VALUES (@d, @s, @u, @k, @f, @l, @t, @fa, @bem, 0, @e, @b)
            """, Db.P("@d", d), Db.P("@s", Schluessel(karte)), Db.P("@u", unterricht), Db.P("@k", JsonSerializer.Serialize(karte.Klassen.Select(x => x.K).ToList())),
            Db.P("@f", karte.Fach), Db.P("@l", lehrer), Db.P("@t", text.Trim()), Db.P("@fa", faellig ?? d.AddDays(7)), Db.P("@bem", (bemerkung ?? "").Trim()),
            Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
        Abgleichen(b, karte, d, lehrer);
        return _db.Einzel("SELECT * FROM hausaufgaben_eintraege WHERE id = @i", LesenHausaufgabe, Db.P("@i", id))!;
    }

    private Hausaufgabe EigeneHausaufgabe(Benutzer b, Stand s, int id)
    {
        RechtePruefung.Verlangen(b, Recht.HausaufgabenEintragen);
        var h = _db.Einzel("SELECT * FROM hausaufgaben_eintraege WHERE id = @i", LesenHausaufgabe, Db.P("@i", id)) ?? throw new ArgumentException("Unbekannte Hausaufgabe.");
        if (!DarfSchreiben(b, s, DateOnly.Parse(h.Datum), h.Stunde, h.Unterricht)) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        return h;
    }

    public Hausaufgabe HausaufgabeAendern(Benutzer b, Stand s, int id, string? text, DateOnly? faellig, string? bemerkung, bool? erledigt)
    {
        var h = EigeneHausaufgabe(b, s, id);
        if (text is not null && string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte die Hausaufgabe eingeben.");
        if (faellig is DateOnly f && f < DateOnly.Parse(h.Datum)) throw new ArgumentException("Die Hausaufgabe kann nicht vor der Stunde fällig sein.");
        _db.Ausfuehren("UPDATE hausaufgaben_eintraege SET text = COALESCE(@t, text), faellig = COALESCE(@f, faellig), bemerkung = COALESCE(@bem, bemerkung), erledigt = COALESCE(@e, erledigt) WHERE id = @i",
            Db.P("@t", text?.Trim()), Db.P("@f", faellig), Db.P("@bem", bemerkung?.Trim()), Db.P("@e", erledigt), Db.P("@i", id));
        AbgleichenNach(b, s, h);
        return _db.Einzel("SELECT * FROM hausaufgaben_eintraege WHERE id = @i", LesenHausaufgabe, Db.P("@i", id))!;
    }

    public void HausaufgabeLoeschen(Benutzer b, Stand s, int id)
    {
        var h = EigeneHausaufgabe(b, s, id);
        _db.Ausfuehren("DELETE FROM hausaufgaben_eintraege WHERE id = @i", Db.P("@i", id));
        AbgleichenNach(b, s, h);
    }

    private void AbgleichenNach(Benutzer b, Stand s, Hausaufgabe h)
    {
        var d = DateOnly.Parse(h.Datum);
        var karte = KarteZu(b, s, d, h.Stunde, h.Unterricht, null, h.Einzelstunde);
        if (karte != null) Abgleichen(b, karte, d, b.LehrerKuerzel ?? h.Lehrer);
    }

    /// <summary>Feld „Hausaufgabe“ im Klassenbuch (Listen, Berichte, Export an UntPlan) aus der Liste der Hausaufgaben übernehmen.</summary>
    private void Abgleichen(Benutzer b, Karte karte, DateOnly d, string lehrer)
    {
        var liste = HausaufgabenDerKarte(d, karte);
        var text = string.Join("\n", liste.Select(h => h.Text));
        var faellig = liste.Select(h => h.Faellig).Where(f => f != null).OrderBy(f => f).FirstOrDefault();
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, hausaufgabe, faellig, geaendert, benutzer_id)
            VALUES (@d, @s, @u, @k, @f, @l, @h, @fa, @g, @b)
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET hausaufgabe = excluded.hausaufgabe, faellig = excluded.faellig, geaendert = excluded.geaendert
            """, Db.P("@d", d), Db.P("@s", Schluessel(karte)), Db.P("@u", karte.Unterricht ?? 0), Db.P("@k", JsonSerializer.Serialize(karte.Klassen.Select(x => x.K).ToList())),
            Db.P("@f", karte.Fach), Db.P("@l", lehrer), Db.P("@h", text), Db.P("@fa", faellig), Db.P("@g", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
    }

    /// <summary>Altes Einzelfeld geändert: höchstens eine Hausaufgabe in der Liste ersetzen bzw. anlegen oder löschen.</summary>
    private void HausaufgabeAusFeld(Benutzer b, Karte karte, DateOnly d, string lehrer, string text, DateOnly? faellig)
    {
        var liste = HausaufgabenDerKarte(d, karte);
        if (liste.Count > 1) return;
        if (liste.Count == 1 && text.Length == 0) _db.Ausfuehren("DELETE FROM hausaufgaben_eintraege WHERE id = @i", Db.P("@i", liste[0].Id));
        else if (liste.Count == 1) _db.Ausfuehren("UPDATE hausaufgaben_eintraege SET text = @t, faellig = @f WHERE id = @i", Db.P("@t", text), Db.P("@f", faellig), Db.P("@i", liste[0].Id));
        else if (text.Length > 0)
            _db.Ausfuehren("""
                INSERT INTO hausaufgaben_eintraege (datum, stunde, unterricht, klassen, fach, lehrer, text, faellig, erstellt, benutzer_id)
                VALUES (@d, @s, @u, @k, @f, @l, @t, @fa, @e, @b)
                """, Db.P("@d", d), Db.P("@s", Schluessel(karte)), Db.P("@u", karte.Unterricht ?? 0), Db.P("@k", JsonSerializer.Serialize(karte.Klassen.Select(x => x.K).ToList())),
                Db.P("@f", karte.Fach), Db.P("@l", lehrer), Db.P("@t", text), Db.P("@fa", faellig), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@b", b.Id));
    }

    // ------------------------------------------------------------------------------------------ Symbole auf den Karten

    /// <summary>Notizen (Schüler*innen; Lehrkräfte nur für das Kollegium) und fällige Hausaufgaben an die Karten hängen.</summary>
    public List<Karte> Anreichern(List<Karte> karten, bool kollegium)
    {
        // Abwesenheitsgrund einer Klasse nur fürs Kollegium
        if (!kollegium) karten = karten.Select(k => k.AbwesendText.Length > 0 ? k with { AbwesendText = k.Status == "entfall" ? "Klasse abwesend" : "abwesend" } : k).ToList();
        var mitUnterricht = karten.Where(k => k.Unterricht.HasValue).ToList();
        if (mitUnterricht.Count == 0) return karten;
        var von = DateOnly.Parse(mitUnterricht.Min(k => k.Datum)!);
        var bis = DateOnly.Parse(mitUnterricht.Max(k => k.Datum)!);
        var eintraege = Zeitraum(von, bis, true);
        var faellig = _db.Liste("SELECT * FROM hausaufgaben_eintraege WHERE faellig >= @v AND faellig <= @b", LesenHausaufgabe, Db.P("@v", von), Db.P("@b", bis));
        return karten.Select(k =>
        {
            if (k.Unterricht is not int u) return k;
            // gekoppelte Karten mit unterschiedlichen Fächern teilen sich den Unterricht: nur Einträge der Lehrkräfte dieser Karte
            var lehrer = k.Lehrer.SelectMany(w => new[] { w.K, w.Alt ?? "" }).Where(x => x.Length > 0).ToHashSet(Ci);
            var e = eintraege.Where(x => x.Datum == k.Datum && x.Unterricht == u && x.Stunde >= k.Von && x.Stunde <= k.Bis
                                         && (lehrer.Count == 0 || x.Lehrer.Length == 0 || lehrer.Contains(x.Lehrer))).ToList();
            var klassen = k.Klassen.Select(w => w.K.Length > 0 ? w.K : w.Alt ?? "").ToHashSet(Ci);
            var hf = k.Status is not ("entfall" or "abwesend") && faellig.Any(h => h.Faellig == k.Datum && Ci.Equals(h.Fach, k.Fach) && h.Klassen.Any(klassen.Contains)
                                                                                 && !(h.Datum == k.Datum && h.Unterricht == u));
            return k with
            {
                // Notizen einer Einzelstunde des Blocks mit „4. Std.: “ davor (Blocknotizen gelten für alle Stunden)
                NotizSchueler = string.Join("\n", e.Where(x => x.NotizSchueler.Length > 0).Select(x => NotizMitStunde(k, x, x.NotizSchueler)).Distinct()),
                NotizLehrer = kollegium ? string.Join("\n", e.Where(x => x.Notiz.Length > 0).Select(x => NotizMitStunde(k, x, x.Notiz)).Distinct()) : "",
                HausaufgabeFaellig = hf,
                Online = e.Any(x => x.Online),
                OnlineLink = e.Where(x => x.Online).Select(x => x.OnlineLink).FirstOrDefault(x => x.Length > 0) ?? "",
            };
        }).ToList();
    }

    private static string NotizMitStunde(Karte k, Klassenbucheintrag e, string text) =>
        e.Einzelstunde is int n && k.Von < k.Bis ? $"{n}. Std.: {text}" : text;

    // ------------------------------------------------------------------------------------------ Abwesenheiten verlängern

    /// <summary>Fehlzeiten früherer Stunden desselben Tages, die in dieser Stunde (noch) nicht eingetragen sind („Vorstunde – verlängern“).</summary>
    public List<Fehlzeit> Vorstunden(Stand s, DateOnly d, Karte karte)
    {
        var klassen = karte.Klassen.Select(k => k.K).ToHashSet(Ci);
        var gaeste = Klassenzugehoerigkeit.Gaeste(s, karte.Unterricht, d);
        var tag = FehlzeitenZeitraum(d, d).Where(f => (klassen.Contains(f.Klasse) || gaeste.Contains(f.SchuelerId)) && f.Art == "fehlt").ToList();
        var hier = tag.Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis).Select(f => f.SchuelerId).ToHashSet();
        return tag.Where(f => f.Stunde < karte.Von && !hier.Contains(f.SchuelerId))
            .GroupBy(f => f.SchuelerId).Select(g => g.OrderByDescending(f => f.Stunde).First()).ToList();
    }

    /// <summary>
    /// Abwesenheiten verlängern. bis = "stunde": Fehlen aus einer früheren Stunde des Tages bis zum Ende dieser Stunde fortschreiben;
    /// bis = "tagesbeginn": Fehlen dieser Stunde auf alle früheren Stunden des Tages ausdehnen. Grund, Text und Status werden übernommen.
    /// </summary>
    public int AbwesenheitVerlaengern(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, IEnumerable<int> schuelerIds, string bis, int? einzelstunde = null)
    {
        var karte = StundeZu(b, s, d, stunde, unterricht, einzelstunde);
        var klassen = karte.Klassen.Select(k => k.K).ToList();
        if (!DarfSchreiben(b, s, d, stunde, unterricht) && !klassen.Any(k => IstKlassenleitung(b, s, k)))
            throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (d > _uhr.Heute) throw new ArgumentException("Fehlzeiten können nicht im Voraus erfasst werden.");
        var schueler = SchuelerDerStunde(s, karte, d).ToDictionary(x => x.Id);
        var tag = FehlzeitenZeitraum(d, d);
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        var lehrer = b.LehrerKuerzel ?? karte.Lehrer.FirstOrDefault()?.K ?? "";
        var anzahl = 0;
        foreach (var sid in schuelerIds.Distinct().Where(schueler.ContainsKey))
        {
            List<(int Stunde, int Unterricht, string Fach)> ziele;
            Fehlzeit? vorlage;
            if (bis == "tagesbeginn")
            {
                vorlage = tag.FirstOrDefault(f => f.SchuelerId == sid && f.Stunde >= karte.Von && f.Stunde <= karte.Bis);
                ziele = _plan.Karten(s, d, Elementtyp.Klasse, schueler[sid].Klasse)
                    .Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung") && k.Von < karte.Von)
                    .SelectMany(k => Enumerable.Range(k.Von, Math.Min(k.Bis, karte.Von - 1) - k.Von + 1).Select(n => (n, k.Unterricht!.Value, k.Fach))).ToList();
            }
            else
            {
                vorlage = tag.Where(f => f.SchuelerId == sid && f.Stunde < karte.Von && f.Art == "fehlt").OrderByDescending(f => f.Stunde).FirstOrDefault()
                          ?? tag.FirstOrDefault(f => f.SchuelerId == sid && f.Stunde >= karte.Von && f.Stunde <= karte.Bis);
                ziele = Enumerable.Range(karte.Von, karte.Bis - karte.Von + 1).Select(n => (n, unterricht, karte.Fach)).ToList();
            }
            foreach (var (n, u, fach) in ziele)
            {
                _db.Ausfuehren("""
                    INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund, gruppe)
                    VALUES (@d, @s, @u, @sid, @k, @f, 'fehlt', 0, @st, @bem, @l, @e, @b, @g, @gr)
                    ON CONFLICT(datum, stunde, schueler_id) DO UPDATE SET art = 'fehlt', minuten = 0, grund = excluded.grund, bemerkung = excluded.bemerkung
                    """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", u), Db.P("@sid", sid), Db.P("@k", schueler[sid].Klasse), Db.P("@f", fach),
                    Db.P("@st", vorlage?.Status ?? "offen"), Db.P("@bem", vorlage?.Bemerkung ?? ""), Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id),
                    Db.P("@g", vorlage?.Grund ?? ""), Db.P("@gr", vorlage?.Gruppe ?? ""));
                anzahl++;
            }
        }
        _db.Ausfuehren("""
            INSERT INTO klassenbuch (datum, stunde, unterricht, klassen, fach, lehrer, geaendert, benutzer_id, anwesenheit)
            VALUES (@d, @s, @u, @k, @f, @l, @g, @b, @g)
            ON CONFLICT(datum, stunde, unterricht, lehrer) DO UPDATE SET anwesenheit = excluded.anwesenheit
            """, Db.P("@d", d), Db.P("@s", Schluessel(karte)), Db.P("@u", unterricht), Db.P("@k", JsonSerializer.Serialize(klassen)), Db.P("@f", karte.Fach),
            Db.P("@l", lehrer), Db.P("@g", jetzt), Db.P("@b", b.Id));
        return anzahl;
    }
}
