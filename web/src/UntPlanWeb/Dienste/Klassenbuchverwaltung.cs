using System.Globalization;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Abwesenheits- bzw. Befreiungsgrund (Stammdaten in UntPlanWeb).</summary>
public sealed record Abwesenheitsgrund(int Id, string Kurz, string Name, string Art, bool Entschuldigt, int Reihenfolge, bool Aktiv);

/// <summary>Art eines Klassendienstes (z. B. Klassenordner, Klassensprecher*in).</summary>
public sealed record Dienstart(int Id, string Kurz, string Name, bool Aktiv, int Reihenfolge);

/// <summary>Klassendienst einer Schülerin/eines Schülers in einem Zeitraum.</summary>
public sealed record Dienstzuordnung(int Id, int DienstId, string Dienst, string DienstKurz, int SchuelerId, string Schueler, string Klasse, string Von, string Bis, string Text);

/// <summary>Befreiung vom Unterricht (Zeitraum, optional Wochentag, Fach und Uhrzeit).</summary>
public sealed record Befreiung(int Id, int SchuelerId, string Schueler, string Klasse, string Von, string Bis, int Wochentag, string Fach, string Beginn, string Ende, string Grund, string Text);

/// <summary>Gehaltene Stunde mit Stand von Abwesenheitskontrolle und Lehrstoff (Klassenbuch → Offene Stunden).</summary>
public sealed record Stundenzeile(string Lehrer, List<string> Klassen, string Fach, string FachName, string Farbe, string Datum, int Von, int Bis, string Beginn, string Ende,
    string Art, string Status, List<string> Raeume, bool Geprueft, bool Lehrstoff, int Unterricht);

/// <summary>Bericht als Tabelle (Vorschau, Druck, CSV).</summary>
/// <param name="Arten">Optional je Zeile: <c>""</c> (Datenzeile), <c>gruppe</c> (Gruppenkopf, z. B. „Klasse 7a“), <c>zwischensumme</c> (Summe je
/// Schüler*in), <c>summe</c> (Summe je Klasse) – für gegliederte Berichte in der Web-Ansicht und im PDF.</param>
public sealed record Bericht(string Id, string Titel, List<string> Spalten, List<List<object?>> Zeilen, string? Hinweis = null, List<string>? Arten = null);

/// <summary>Verfügbarer Bericht (Gruppe wie im Original: Abwesenheiten, Klassenbucheinträge, Arbeitsbericht, Prüfungen, Weitere).</summary>
public sealed record Berichtsart(string Id, string Gruppe, string Titel, bool KlasseNoetig = false, string? Parameter = null);

/// <summary>
/// Verwaltung im Klassenbuch über die Einträge je Stunde hinaus:
/// <list type="bullet">
/// <item>Offene Stunden (Abwesenheitskontrolle und Lehrstoff je gehaltener Stunde).</item>
/// <item>Klassendienste, Befreiungen, Abwesenheitsgründe (Sitzpläne: <see cref="Sitzplandienst"/>).</item>
/// <item>Abwesenheiten bearbeiten und zusammenfassen.</item>
/// <item>Berichte (Fehlzeiten, Fehltage, Überschreitungen, Einträge, Arbeitsbericht, Prüfungen …).</item>
/// </list>
/// </summary>
public sealed class Klassenbuchverwaltung
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Plandienst _plan;
    private readonly Klassenbuchdienst _kb;
    private readonly Listendienst _listen;
    private readonly Rueckmeldungsdienst _rueck;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new CultureInfo("de-DE"), true);

    public Klassenbuchverwaltung(Db db, Uhr uhr, Plandienst plan, Klassenbuchdienst kb, Listendienst listen, Rueckmeldungsdienst rueck)
    {
        _db = db; _uhr = uhr; _plan = plan; _kb = kb; _listen = listen; _rueck = rueck;
    }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");

    private static Dictionary<int, WebSchueler> Schueler(Stand s) => s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
    private static string Name(Dictionary<int, WebSchueler> d, int id) => d.TryGetValue(id, out var x) ? $"{x.Name} {x.Vorname}".Trim() : $"#{id}";

    private static void Klassenleitung(Benutzer b, Stand s, string klasse)
    {
        if (!Klassenbuchdienst.IstKlassenleitung(b, s, klasse)) throw new UnauthorizedAccessException("Nur die Klassenleitung oder Schulleitung kann dies ändern.");
    }

    private static WebSchueler SchuelerPruefen(Stand s, int id) =>
        s.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");

    // ------------------------------------------------------------------------------------------ Offene Stunden

    /// <summary>
    /// Gehaltene Stunden im Zeitraum (bis jetzt) der gewählten Lehrkräfte. Lehrkräfte sehen nur ihre eigenen Stunden, Klassenleitungen
    /// mit Klassenfilter alle Stunden ihrer Klasse; Schulleitung, Administration und Konten mit „Klassenbuch – alle“ bzw. „Offene Stunden“
    /// alle (ohne Auswahl: alle Lehrkräfte).
    /// </summary>
    public List<Stundenzeile> Stunden(Stand s, Benutzer b, DateOnly von, DateOnly bis, IEnumerable<string> lehrerWahl, string? klasse)
    {
        var heute = _uhr.Heute;
        var jetzt = _uhr.Jetzt.ToString("HH:mm");
        if (bis > heute) bis = heute;
        if (bis.DayNumber - von.DayNumber > 366) von = bis.AddDays(-366);
        var alle = OffeneStunden.DarfAlle(b) || (klasse is not null && Klassenbuchdienst.IstKlassenleitung(b, s, klasse));
        var lehrer = alle
            ? (lehrerWahl.Any() ? lehrerWahl.Where(l => s.Lehrer.ContainsKey(l)).ToList() : s.Paket.Elemente.Lehrer.Select(l => l.Kurz).ToList())
            : b.LehrerKuerzel is string eigen ? new List<string> { eigen } : new List<string>();
        var eintraege = _kb.Zeitraum(von, bis);
        var res = new List<Stundenzeile>();
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            if (_plan.Frei(s, d).Frei) continue;
            var iso = Plandienst.Iso(d);
            var tag = eintraege.Where(e => e.Datum == iso).ToList();
            foreach (var l in lehrer)
                foreach (var k in _plan.Karten(s, d, Elementtyp.Lehrer, l).Where(k => k.Unterricht.HasValue && k.Status is "regulaer" or "geaendert" or "klausur" or "offen"))
                {
                    if (d == heute && string.CompareOrdinal(k.Ende, jetzt) > 0) continue;
                    if (klasse is not null && !k.Klassen.Any(x => Ci.Equals(x.K, klasse))) continue;
                    var zur = tag.Where(e => e.Unterricht == k.Unterricht && e.Stunde >= k.Von && e.Stunde <= k.Bis).ToList();
                    res.Add(new Stundenzeile(l, k.Klassen.Select(x => x.K).Where(x => x.Length > 0).ToList(), k.Fach, k.FachName, k.Farbe, iso, k.Von, k.Bis, k.Beginn, k.Ende,
                        k.Status == "geaendert" ? (k.Art.Length > 0 ? k.Art : "Vertretung") : k.Status == "klausur" ? "Prüfung" : "",
                        k.Status, k.Raeume.Select(x => x.K).Where(x => x.Length > 0).ToList(),
                        zur.Any(e => e.Anwesenheit != null), zur.Any(e => e.Thema.Length > 0 && (Ci.Equals(e.Lehrer, l) || zur.All(z => !Ci.Equals(z.Lehrer, l)))), k.Unterricht!.Value));
                }
        }
        return res.OrderBy(x => x.Datum).ThenBy(x => x.Von).ThenBy(x => x.Lehrer, De).ToList();
    }

    // ------------------------------------------------------------------------------------------ Abwesenheitsgründe

    public List<Abwesenheitsgrund> Gruende(string? art = null) => _db.Liste("SELECT * FROM abwesenheitsgruende WHERE (@a IS NULL OR art = @a) ORDER BY art, reihenfolge, name",
        r => new Abwesenheitsgrund(r.Zahl("id"), r.Text("kurz"), r.Text("name"), r.Text("art"), r.Wahr("entschuldigt"), r.Zahl("reihenfolge"), r.Wahr("aktiv")), Db.P("@a", art));

    public Abwesenheitsgrund GrundSpeichern(int? id, string kurz, string name, string art, bool entschuldigt, int reihenfolge, bool aktiv)
    {
        if (string.IsNullOrWhiteSpace(kurz) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte Kürzel und Bezeichnung eingeben.");
        art = art == "befreiung" ? "befreiung" : "abwesenheit";
        var p = new[] { Db.P("@k", kurz.Trim()), Db.P("@n", name.Trim()), Db.P("@a", art), Db.P("@e", entschuldigt), Db.P("@r", reihenfolge), Db.P("@ak", aktiv), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO abwesenheitsgruende (kurz, name, art, entschuldigt, reihenfolge, aktiv) VALUES (@k, @n, @a, @e, @r, @ak)", p);
        else if (_db.Ausfuehren("UPDATE abwesenheitsgruende SET kurz = @k, name = @n, art = @a, entschuldigt = @e, reihenfolge = @r, aktiv = @ak WHERE id = @id", p) == 0)
            throw new ArgumentException("Unbekannter Eintrag.");
        return Gruende().First(g => g.Id == id);
    }

    public void GrundLoeschen(int id) => _db.Ausfuehren("DELETE FROM abwesenheitsgruende WHERE id = @id", Db.P("@id", id));

    // ------------------------------------------------------------------------------------------ Abwesenheiten bearbeiten

    private List<Fehlzeit> FehlzeitenNachIds(Benutzer b, Stand s, IEnumerable<int> ids)
    {
        var liste = new List<Fehlzeit>();
        foreach (var id in ids.Distinct())
        {
            var f = _db.Einzel("SELECT * FROM fehlzeiten WHERE id = @i", Klassenbuchdienst.FehlzeitLesen, Db.P("@i", id));
            if (f is null) continue;
            Klassenleitung(b, s, f.Klasse);
            liste.Add(f);
        }
        return liste;
    }

    /// <summary>Grund, Text, Status und Details (Nachweis, Entschuldigung am, Eltern informiert, interne Notiz) einer Abwesenheit (aller zugehörigen Stunden) ändern.</summary>
    public int AbwesenheitAendern(Benutzer b, Stand s, IEnumerable<int> ids, string? grund, string? bemerkung, string? status,
        string? nachweis = null, string? entschuldigtAm = null, bool? elternInformiert = null, string? notizIntern = null)
    {
        var n = 0;
        var einst = _kb.Einstellungen();
        foreach (var f in FehlzeitenNachIds(b, s, ids))
        {
            // „Entschuldigungsstatus immer mit Abwesenheitsgrund ändern“: neuer Grund ohne ausdrücklichen Status
            if (status is null && grund is not null && _kb.StatusNachGrund(einst, f.Status, f.Grund, grund.Trim()) is var sg && sg != f.Status) status = sg;
            n += _db.Ausfuehren("UPDATE fehlzeiten SET grund = COALESCE(@g, grund), bemerkung = COALESCE(@b, bemerkung), status = COALESCE(@s, status), " +
                "nachweis = COALESCE(@n, nachweis), entschuldigt_am = COALESCE(@ea, entschuldigt_am), eltern_informiert = COALESCE(@ei, eltern_informiert), " +
                "notiz_intern = COALESCE(@ni, notiz_intern) WHERE id = @i",
                Db.P("@g", grund?.Trim()), Db.P("@b", bemerkung?.Trim()), Db.P("@s", status is "offen" or "entschuldigt" or "unentschuldigt" ? status : null),
                Db.P("@n", nachweis is null ? null : Nachweis(nachweis)), Db.P("@ea", entschuldigtAm is null ? null : DatumOderLeer(entschuldigtAm)),
                Db.P("@ei", elternInformiert is null ? null : elternInformiert.Value ? 1 : 0), Db.P("@ni", notizIntern?.Trim()), Db.P("@i", f.Id));
        }
        return n;
    }

    /// <summary>Erlaubte Nachweisarten einer Entschuldigung.</summary>
    public static readonly string[] Nachweisarten = { "attest", "schriftlich", "muendlich", "telefonisch", "email" };
    internal static string Nachweis(string? n) => Nachweisarten.Contains(n ?? "") ? n! : "";
    internal static string DatumOderLeer(string? d) => DateOnly.TryParse(d, out var x) ? x.ToString("yyyy-MM-dd") : "";

    public int AbwesenheitenLoeschen(Benutzer b, Stand s, IEnumerable<int> ids)
    {
        var n = 0;
        var liste = FehlzeitenNachIds(b, s, ids);
        var einst = _kb.Einstellungen();
        foreach (var f in liste) einst.LoeschenPruefen(b, f.Erstellt, _uhr.Jetzt);
        foreach (var f in liste) n += _db.Ausfuehren("DELETE FROM fehlzeiten WHERE id = @i", Db.P("@i", f.Id));
        return n;
    }

    /// <summary>Abwesenheiten einer Schülerin/eines Schülers zu einer zusammenfassen (gemeinsamer Grund, Text und Status, eine Zeile über mehrere Tage).</summary>
    public int Zusammenfassen(Benutzer b, Stand s, IEnumerable<int> ids)
    {
        var liste = FehlzeitenNachIds(b, s, ids);
        if (liste.Select(f => f.SchuelerId).Distinct().Count() != 1) throw new ArgumentException("Bitte Abwesenheiten genau einer Schülerin bzw. eines Schülers auswählen.");
        var gruppe = Sicherheit.Zufall(10);
        var grund = liste.Select(f => f.Grund).FirstOrDefault(g => g.Length > 0) ?? "";
        var bem = liste.Select(f => f.Bemerkung).FirstOrDefault(g => g.Length > 0) ?? "";
        var status = liste.All(f => f.Status == liste[0].Status) ? liste[0].Status : "offen";
        foreach (var f in liste)
            _db.Ausfuehren("UPDATE fehlzeiten SET gruppe = @gr, grund = @g, bemerkung = @b, status = @s WHERE id = @i",
                Db.P("@gr", gruppe), Db.P("@g", grund), Db.P("@b", bem), Db.P("@s", status), Db.P("@i", f.Id));
        return liste.Count;
    }

    /// <summary>Zuletzt verwendete Texte (Vorschläge beim Entschuldigen).</summary>
    public List<string> LetzteTexte() => _db.Liste("SELECT bemerkung, MAX(id) AS m FROM fehlzeiten WHERE bemerkung <> '' GROUP BY bemerkung ORDER BY m DESC LIMIT 20", r => r.Text("bemerkung"));

    // ------------------------------------------------------------------------------------------ Klassendienste

    public List<Dienstart> Dienstarten() => _db.Liste("SELECT * FROM klassendienste ORDER BY reihenfolge, name",
        r => new Dienstart(r.Zahl("id"), r.Text("kurz"), r.Text("name"), r.Wahr("aktiv"), r.Zahl("reihenfolge")));

    public Dienstart DienstartSpeichern(int? id, string kurz, string name, bool aktiv, int reihenfolge)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte eine Bezeichnung eingeben.");
        var p = new[] { Db.P("@k", (kurz ?? "").Trim()), Db.P("@n", name.Trim()), Db.P("@a", aktiv), Db.P("@r", reihenfolge), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO klassendienste (kurz, name, aktiv, reihenfolge) VALUES (@k, @n, @a, @r)", p);
        else if (_db.Ausfuehren("UPDATE klassendienste SET kurz = @k, name = @n, aktiv = @a, reihenfolge = @r WHERE id = @id", p) == 0) throw new ArgumentException("Unbekannter Dienst.");
        return Dienstarten().First(d => d.Id == id);
    }

    public void DienstartLoeschen(int id) => _db.Ausfuehren("DELETE FROM klassendienste WHERE id = @id", Db.P("@id", id));

    public List<Dienstzuordnung> Dienste(Stand s, string? klasse, DateOnly von, DateOnly bis, int? dienst = null)
    {
        var namen = Schueler(s);
        return _db.Liste("""
            SELECT z.*, d.name AS dienst, d.kurz AS dienst_kurz FROM klassendienst_zuordnungen z JOIN klassendienste d ON d.id = z.dienst_id
            WHERE (@k IS NULL OR z.klasse = @k COLLATE NOCASE) AND z.von <= @b AND z.bis >= @v AND (@d IS NULL OR z.dienst_id = @d)
            ORDER BY z.von, d.reihenfolge
            """, r => new Dienstzuordnung(r.Zahl("id"), r.Zahl("dienst_id"), r.Text("dienst"), r.Text("dienst_kurz"), r.Zahl("schueler_id"), Name(namen, r.Zahl("schueler_id")),
                r.Text("klasse"), r.Text("von"), r.Text("bis"), r.Text("text")),
            Db.P("@k", klasse), Db.P("@v", von), Db.P("@b", bis), Db.P("@d", dienst));
    }

    public Dienstzuordnung DienstSpeichern(Benutzer b, Stand s, int? id, int dienstId, int schuelerId, DateOnly von, DateOnly bis, string? text)
    {
        var sch = SchuelerPruefen(s, schuelerId);
        Klassenleitung(b, s, sch.Klasse);
        if (bis < von) throw new ArgumentException("Das Ende liegt vor dem Beginn.");
        if (!Dienstarten().Any(d => d.Id == dienstId)) throw new ArgumentException("Unbekannter Dienst.");
        var p = new[] { Db.P("@d", dienstId), Db.P("@s", schuelerId), Db.P("@k", sch.Klasse), Db.P("@v", von), Db.P("@b", bis), Db.P("@t", (text ?? "").Trim()),
            Db.P("@e", Jetzt), Db.P("@u", b.Id), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO klassendienst_zuordnungen (dienst_id, schueler_id, klasse, von, bis, text, erstellt, benutzer_id) VALUES (@d, @s, @k, @v, @b, @t, @e, @u)", p);
        else
        {
            var alt = _db.Wert("SELECT klasse FROM klassendienst_zuordnungen WHERE id = @id", Db.P("@id", id)) as string ?? throw new ArgumentException("Unbekannter Eintrag.");
            Klassenleitung(b, s, alt);
            _db.Ausfuehren("UPDATE klassendienst_zuordnungen SET dienst_id = @d, schueler_id = @s, klasse = @k, von = @v, bis = @b, text = @t WHERE id = @id", p);
        }
        return Dienste(s, null, DateOnly.MinValue, DateOnly.MaxValue).First(x => x.Id == id);
    }

    public void DienstLoeschen(Benutzer b, Stand s, int id)
    {
        var klasse = _db.Wert("SELECT klasse FROM klassendienst_zuordnungen WHERE id = @id", Db.P("@id", id)) as string;
        if (klasse is null) return;
        Klassenleitung(b, s, klasse);
        _db.Ausfuehren("DELETE FROM klassendienst_zuordnungen WHERE id = @id", Db.P("@id", id));
    }

    /// <summary>Zeitansicht: Dienst einer Schülerin/eines Schülers für eine Woche (Montag–Sonntag) ein- bzw. austragen.</summary>
    public void DienstWoche(Benutzer b, Stand s, int dienstId, int schuelerId, DateOnly tag, bool an)
    {
        var sch = SchuelerPruefen(s, schuelerId);
        Klassenleitung(b, s, sch.Klasse);
        var mo = Plandienst.Montag(tag);
        var so = mo.AddDays(6);
        var vorhanden = Dienste(s, sch.Klasse, mo, so, dienstId).Where(z => z.SchuelerId == schuelerId).ToList();
        if (an)
        {
            if (vorhanden.Count == 0) DienstSpeichern(b, s, null, dienstId, schuelerId, mo, mo.AddDays(Math.Max(5, s.Paket.Zeitraster.TageProWoche) - 1), "");
            return;
        }
        foreach (var z in vorhanden)
        {
            var v = DateOnly.Parse(z.Von);
            var bi = DateOnly.Parse(z.Bis);
            if (v >= mo && bi <= so) { DienstLoeschen(b, s, z.Id); continue; }
            if (v < mo) _db.Ausfuehren("UPDATE klassendienst_zuordnungen SET bis = @b WHERE id = @id", Db.P("@b", mo.AddDays(-1)), Db.P("@id", z.Id));
            else _db.Ausfuehren("UPDATE klassendienst_zuordnungen SET von = @v WHERE id = @id", Db.P("@v", so.AddDays(1)), Db.P("@id", z.Id));
            if (v < mo && bi > so) DienstSpeichern(b, s, null, dienstId, schuelerId, so.AddDays(1), bi, z.Text);
        }
    }

    /// <summary>Automatische Schülereinteilung: je Woche im Zeitraum <paramref name="proWoche"/> Schüler*innen reihum (alphabetisch).</summary>
    public int DienstAutomatisch(Benutzer b, Stand s, string klasse, int dienstId, DateOnly von, DateOnly bis, int proWoche)
    {
        Klassenleitung(b, s, klasse);
        var schueler = Klassenbuchdienst.Schueler(s, new[] { klasse });
        if (schueler.Count == 0) throw new ArgumentException("Für diese Klasse wurde keine Schülerliste übertragen.");
        proWoche = Math.Clamp(proWoche, 1, Math.Max(1, schueler.Count));
        var mo = Plandienst.Montag(von);
        if (bis.DayNumber - mo.DayNumber > 400) throw new ArgumentException("Der Zeitraum ist zu lang.");
        // bisherige Einteilung ab dieser Woche ersetzen; reihum beim nächsten nach dem zuletzt Eingeteilten weitermachen
        var vorher = Dienste(s, klasse, DateOnly.MinValue, mo.AddDays(-1), dienstId).OrderBy(z => z.Von).LastOrDefault();
        _db.Ausfuehren("DELETE FROM klassendienst_zuordnungen WHERE klasse = @k COLLATE NOCASE AND dienst_id = @d AND von >= @v AND von <= @b",
            Db.P("@k", klasse), Db.P("@d", dienstId), Db.P("@v", mo), Db.P("@b", bis));
        var i = vorher is null ? 0 : (schueler.FindIndex(x => x.Id == vorher.SchuelerId) + 1) % schueler.Count;
        var n = 0;
        for (var w = mo; w <= bis; w = w.AddDays(7))
        {
            if (_plan.Frei(s, w).Frei && _plan.Frei(s, w.AddDays(2)).Frei && _plan.Frei(s, w.AddDays(4)).Frei) continue; // Ferienwoche
            for (var j = 0; j < proWoche; j++, i = (i + 1) % schueler.Count)
            {
                DienstSpeichern(b, s, null, dienstId, schueler[i].Id, w, w.AddDays(Math.Max(5, s.Paket.Zeitraster.TageProWoche) - 1), "");
                n++;
            }
        }
        return n;
    }

    // ------------------------------------------------------------------------------------------ Befreiungen

    public List<Befreiung> Befreiungen(Stand s, string? klasse, int? schueler, DateOnly von, DateOnly bis)
    {
        var namen = Schueler(s);
        return _db.Liste("""
            SELECT * FROM befreiungen WHERE (@k IS NULL OR klasse = @k COLLATE NOCASE) AND (@s IS NULL OR schueler_id = @s) AND von <= @b AND bis >= @v ORDER BY klasse, von
            """, r => new Befreiung(r.Zahl("id"), r.Zahl("schueler_id"), Name(namen, r.Zahl("schueler_id")), r.Text("klasse"), r.Text("von"), r.Text("bis"), r.Zahl("wochentag"),
                r.Text("fach"), r.Text("beginn"), r.Text("ende"), r.Text("grund"), r.Text("text")),
            Db.P("@k", klasse), Db.P("@s", schueler), Db.P("@v", von), Db.P("@b", bis));
    }

    public Befreiung BefreiungSpeichern(Benutzer b, Stand s, int? id, int schuelerId, DateOnly von, DateOnly bis, int wochentag, string? fach, string? beginn, string? ende, string? grund, string? text)
    {
        var sch = SchuelerPruefen(s, schuelerId);
        Klassenleitung(b, s, sch.Klasse);
        if (bis < von) throw new ArgumentException("Das Ende liegt vor dem Beginn.");
        if (wochentag is < 0 or > 7) throw new ArgumentException("Unbekannter Wochentag.");
        static string Zeit(string? z) => TimeOnly.TryParse(z, out var t) ? t.ToString("HH:mm") : "";
        var p = new[] { Db.P("@s", schuelerId), Db.P("@k", sch.Klasse), Db.P("@v", von), Db.P("@b", bis), Db.P("@w", wochentag), Db.P("@f", (fach ?? "").Trim()),
            Db.P("@be", Zeit(beginn)), Db.P("@en", Zeit(ende)), Db.P("@g", (grund ?? "").Trim()), Db.P("@t", (text ?? "").Trim()), Db.P("@e", Jetzt), Db.P("@u", b.Id), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("""
            INSERT INTO befreiungen (schueler_id, klasse, von, bis, wochentag, fach, beginn, ende, grund, text, erstellt, benutzer_id)
            VALUES (@s, @k, @v, @b, @w, @f, @be, @en, @g, @t, @e, @u)
            """, p);
        else
        {
            var alt = _db.Wert("SELECT klasse FROM befreiungen WHERE id = @id", Db.P("@id", id)) as string ?? throw new ArgumentException("Unbekannte Befreiung.");
            Klassenleitung(b, s, alt);
            _db.Ausfuehren("UPDATE befreiungen SET schueler_id = @s, klasse = @k, von = @v, bis = @b, wochentag = @w, fach = @f, beginn = @be, ende = @en, grund = @g, text = @t WHERE id = @id", p);
        }
        return Befreiungen(s, null, null, DateOnly.MinValue, DateOnly.MaxValue).First(x => x.Id == id);
    }

    public void BefreiungLoeschen(Benutzer b, Stand s, int id)
    {
        var klasse = _db.Wert("SELECT klasse FROM befreiungen WHERE id = @id", Db.P("@id", id)) as string;
        if (klasse is null) return;
        Klassenleitung(b, s, klasse);
        _db.Ausfuehren("DELETE FROM befreiungen WHERE id = @id", Db.P("@id", id));
    }

    /// <summary>Befreite Schüler*innen in einer Stunde (Wochentag, Fach und Uhrzeit passend).</summary>
    public List<int> BefreitAm(Stand s, IEnumerable<string> klassen, DateOnly d, string fach, string beginn)
    {
        var res = new List<int>();
        foreach (var k in klassen.Distinct(Ci))
            foreach (var x in Befreiungen(s, k, null, d, d))
            {
                if (x.Wochentag != 0 && x.Wochentag != Plandienst.TagIndex(d) + 1) continue;
                if (x.Fach.Length > 0 && !Ci.Equals(x.Fach, fach)) continue;
                if (x.Beginn.Length > 0 && x.Ende.Length > 0 && x.Beginn != "00:00" && beginn.Length > 0 &&
                    (string.CompareOrdinal(beginn, x.Beginn) < 0 || string.CompareOrdinal(beginn, x.Ende) >= 0)) continue;
                res.Add(x.SchuelerId);
            }
        return res.Distinct().ToList();
    }

    // ------------------------------------------------------------------------------------------ Findet online statt

    /// <summary>
    /// „Findet online statt“ setzen und optional den Link zum Online-Unterricht (Videokonferenz). link = null lässt den gespeicherten
    /// Link unverändert, "" entfernt ihn. Nur Lehrkräfte der Stunde sowie Schulleitung/Administration (Klassenbuch aller Stunden).
    /// </summary>
    public void OnlineSetzen(Benutzer b, Stand s, DateOnly d, int stunde, int unterricht, bool online, string? link = null, int? einzelstunde = null)
    {
        if (!_kb.DarfSchreiben(b, s, d, stunde, unterricht)) throw new UnauthorizedAccessException("Diese Stunde gehört nicht zu Ihrem Unterricht.");
        var url = link is null ? null : OnlineLinkPruefen(link);
        // Einzelstunde eines Blocks: eigener Eintrag nur dieser Stunde
        var block = einzelstunde.HasValue ? _kb.Blockkarte(b, s, d, stunde, unterricht) : null;
        var einzeln = block != null && block.Von < block.Bis;
        if (einzeln && (einzelstunde < block!.Von || einzelstunde > block.Bis)) throw new ArgumentException("Die Einzelstunde gehört nicht zu diesem Block.");
        var e = einzeln
            ? _kb.EinzelEintraege(d, unterricht, einzelstunde!.Value, einzelstunde.Value, b.LehrerKuerzel).FirstOrDefault()
            : _kb.Zeitraum(d, d).Where(x => x.Unterricht == unterricht && stunde >= x.Stunde && (b.LehrerKuerzel is null || Ci.Equals(x.Lehrer, b.LehrerKuerzel)))
                .OrderByDescending(x => x.Stunde).FirstOrDefault();
        var id = e?.Id ?? _kb.Speichern(b, s, d, stunde, unterricht, "", "", null, "", einzelstunde: einzeln ? einzelstunde : null).Id;
        if (url is null) _db.Ausfuehren("UPDATE klassenbuch SET online = @o WHERE id = @i", Db.P("@o", online), Db.P("@i", id));
        else _db.Ausfuehren("UPDATE klassenbuch SET online = @o, online_link = @l WHERE id = @i", Db.P("@o", online), Db.P("@l", url), Db.P("@i", id));
    }

    /// <summary>Link zum Online-Unterricht prüfen: leer oder eine absolute http-/https-Adresse (höchstens 1000 Zeichen).</summary>
    public static string OnlineLinkPruefen(string link)
    {
        var t = link.Trim();
        if (t.Length == 0) return "";
        if (t.Length > 1000) throw new ArgumentException("Der Link ist zu lang (höchstens 1000 Zeichen).");
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(u.Host) || t.Any(char.IsWhiteSpace))
            throw new ArgumentException("Bitte einen gültigen Link mit http:// oder https:// angeben.");
        return t;
    }

    // ------------------------------------------------------------------------------------------ Berichte

    public static readonly List<Berichtsart> Berichtsarten = new()
    {
        new("fehlzeiten_schueler", "Abwesenheiten", "Fehlzeiten pro Schüler*in"),
        new("fehlzeiten_klasse", "Abwesenheiten", "Fehlzeiten pro Klasse"),
        new("fehlzeiten_fach", "Abwesenheiten", "Fehlzeiten pro Fach"),
        new("fehltage_klasse", "Abwesenheiten", "Fehltage pro Klasse"),
        new("verspaetungen", "Abwesenheiten", "Verspätungen und vorzeitiges Gehen"),
        new("verspaetungen_auffaellig", "Abwesenheiten", "Auffällige Verspätungen", false, "mindestens {n1} Verspätungen"),
        new("abwesenheitsueberschreitung", "Abwesenheiten", "Abwesenheitsüberschreitung", false, "mehr als {n1} Fehlstunden in {n2} Wochen"),
        new("abwesenheiten", "Abwesenheiten", "Abwesenheiten"),
        new("anwesenheiten", "Abwesenheiten", "Anwesenheiten"),
        new("unentschuldigt", "Abwesenheiten", "Unentschuldigte Abwesenheiten", false, "mindestens {n1} Tage am Stück"),
        new("statusuebersicht", "Abwesenheiten", "Fehlzeitenübersicht nach Entschuldigungsstatus"),
        new("entschuldigungsschreiben", "Abwesenheiten", "Entschuldigungsschreiben"),
        new("klassenbucheintraege", "Klassenbucheinträge", "Klassenbucheinträge pro Klasse"),
        new("klassenbucheintraege_schueler", "Klassenbucheinträge", "Klassenbucheinträge pro Schüler*in"),
        new("arbeitsbericht_tag", "Arbeitsbericht", "Arbeitsbericht je Tag", true),
        new("tagesuebersicht", "Arbeitsbericht", "Tagesübersicht Klasse", true),
        new("wochenuebersicht", "Arbeitsbericht", "Wochenübersicht Klasse", true),
        new("pruefungen", "Prüfungen", "Prüfungen"),
        new("befreiungen", "Weitere", "Befreiungen"),
        new("klassendienste", "Weitere", "Klassendienste"),
    };

    public Bericht BerichtErstellen(Stand s, Benutzer b, string art, DateOnly von, DateOnly bis, string? klasse, int? schueler, int n1, int n2)
    {
        var info = Berichtsarten.FirstOrDefault(x => x.Id == art) ?? throw new ArgumentException("Unbekannter Bericht.");
        if (info.KlasseNoetig && string.IsNullOrEmpty(klasse)) throw new ArgumentException("Bitte eine Klasse wählen.");
        if (bis < von) (von, bis) = (bis, von);
        if (bis.DayNumber - von.DayNumber > 400) throw new ArgumentException("Bitte einen Zeitraum von höchstens einem Schuljahr wählen.");
        var namen = Schueler(s);
        var klassen = string.IsNullOrEmpty(klasse) ? s.Paket.Elemente.Klassen.Select(k => k.Kurz).ToList() : new List<string> { klasse };
        var schuelerListe = Klassenzugehoerigkeit.ImZeitraum(s, klassen, von, bis).Where(x => schueler is null || x.Id == schueler).ToList();
        List<Fehlzeit> Fz(DateOnly v, DateOnly bi) => _kb.FehlzeitenZeitraum(v, bi, string.IsNullOrEmpty(klasse) ? null : klasse).Where(f => schueler is null || f.SchuelerId == schueler).ToList();
        string N(int id) => Name(namen, id);
        string Datum(string iso) => DateOnly.TryParse(iso, out var d) ? d.ToString("dd.MM.yyyy") : iso;
        var gruende = Gruende().ToDictionary(g => g.Kurz, g => g.Name, Ci);
        string Grund(string g) => gruende.TryGetValue(g, out var n) ? n : g;
        Bericht B(List<string> spalten, IEnumerable<IEnumerable<object?>> zeilen, string? hinweis = null) => new(art, info.Titel, spalten, zeilen.Select(z => z.ToList()).ToList(), hinweis);
        // gegliederter Bericht: Zeilen mit Art (Gruppenkopf, Datenzeile, Zwischensumme je Schüler*in, Summe je Klasse)
        Bericht G(List<string> spalten, List<(string Art, object?[] Zeile)> zeilen, string? hinweis = null) =>
            new(art, info.Titel, spalten, zeilen.Select(z => z.Zeile.ToList()).ToList(), hinweis, zeilen.Select(z => z.Art).ToList());
        // Unterrichtstag einer Klasse: nicht unterrichtsfrei und mindestens eine Stunde laut Plan (ohne Entfall)
        var unterrichtstage = new Dictionary<(string, DateOnly), bool>();
        bool Unterrichtstag(string k, DateOnly d)
        {
            if (unterrichtstage.TryGetValue((k, d), out var ja)) return ja;
            return unterrichtstage[(k, d)] = !_plan.Frei(s, d).Frei
                && _plan.Karten(s, d, Elementtyp.Klasse, k).Any(x => x.Unterricht.HasValue && x.Status is not ("entfall" or "veranstaltung"));
        }
        // Unterrichtstage der Klasse im Zeitraum (bis heute), für „Schultage“ in den Fehltagen
        int Schultage(string k, DateOnly v, DateOnly bi)
        {
            var n = 0;
            for (var d = v; d <= bi && d <= _uhr.Heute; d = d.AddDays(1)) if (Unterrichtstag(k, d)) n++;
            return n;
        }

        switch (art)
        {
            case "fehlzeiten_schueler":
            {
                var fz = Fz(von, bis);
                var regeln = _kb.Regeln();
                return B(new() { "Klasse", "Schüler*in", "Fehltage", "Fehlstunden", "entschuldigt", "unentschuldigt", "offen", "Verspätungen", "Verspätung (Min.)",
                        "vorzeitig", "vorzeitig (Min.)", "Std. aus Minuten", "Fehlstunden gesamt", "Hinweis" },
                    schuelerListe.Select(x =>
                    {
                        var z = Listendienst.Summe(regeln, x.Klasse, x.Id, "", "", fz.Where(y => y.SchuelerId == x.Id).ToList());
                        return new object?[] { x.Klasse, N(x.Id), z.Tage, z.Stunden, z.Entschuldigt, z.Unentschuldigt, z.Offen, z.Verspaetungen, z.Minuten,
                            z.Vorzeitig, z.VorzeitigMinuten, z.StundenAusMinuten, z.StundenGesamt, z.Hinweis };
                    }), regeln.MinutenUmrechnen ? $"Verspätungs- und Gehminuten werden zu Fehlstunden umgerechnet (je {regeln.MinutenProFehlstunde} min eine Stunde)." : null);
            }
            case "verspaetungen":
            {
                var fz = Fz(von, bis).Where(f => f.Art is "verspaetet" or "vorzeitig" || f.Bemerkung.StartsWith("verspätet", StringComparison.Ordinal)
                                                  || f.Bemerkung.StartsWith("vorzeitig", StringComparison.Ordinal)).ToList();
                return B(new() { "Klasse", "Schüler*in", "Datum", "Stunde", "Fach", "Art", "Minuten", "Grund", "Status", "Lehrkraft", "Text" },
                    fz.OrderBy(f => f.Klasse, De).ThenBy(f => N(f.SchuelerId), De).ThenBy(f => f.Datum).ThenBy(f => f.Stunde)
                        .Select(f => new object?[] { f.Klasse, N(f.SchuelerId), Datum(f.Datum), $"{f.Stunde}.", f.Fach,
                            f.Art switch { "verspaetet" => "verspätet", "vorzeitig" => "vorzeitig gegangen", _ => "als Fehlstunde gewertet" },
                            f.Art == "fehlt" ? "" : f.Minuten, Grund(f.Grund), f.Status, f.Lehrer, f.Bemerkung }));
            }
            case "verspaetungen_auffaellig":
            {
                var regeln = _kb.Regeln();
                n1 = n1 > 0 ? n1 : Math.Max(1, regeln.HinweisAbAnzahl);
                var fz = Fz(von, bis);
                return B(new() { "Klasse", "Schüler*in", "Verspätungen", "Minuten", "vorzeitig", "Minuten (vorzeitig)", "Std. aus Minuten", "letzte Verspätung" },
                    schuelerListe.Select(x => (x, f: fz.Where(f => f.SchuelerId == x.Id).ToList()))
                        .Select(t => (t.x, t.f, sp: t.f.Where(f => f.Art == "verspaetet").ToList(), vz: t.f.Where(f => f.Art == "vorzeitig").ToList()))
                        .Where(t => t.sp.Count >= n1 || t.vz.Count >= n1)
                        .OrderByDescending(t => t.sp.Count + t.vz.Count)
                        .Select(t => new object?[] { t.x.Klasse, N(t.x.Id), t.sp.Count, t.sp.Sum(f => f.Minuten), t.vz.Count, t.vz.Sum(f => f.Minuten),
                            regeln.StundenAus(t.sp.Sum(f => f.Minuten) + t.vz.Sum(f => f.Minuten)), t.sp.Count > 0 ? Datum(t.sp.Max(f => f.Datum)!) : "" }),
                    $"Schüler*innen mit mindestens {n1} Verspätungen bzw. vorzeitigem Gehen im Zeitraum.");
            }
            case "fehlzeiten_klasse":
            {
                var fz = Fz(von, bis);
                return B(new() { "Klasse", "Schüler*innen", "Fehlstunden", "Fehltage", "entschuldigt", "unentschuldigt", "offen", "Verspätungen", "Ø Fehlstunden" },
                    klassen.Select(k =>
                    {
                        var anz = schuelerListe.Count(x => Ci.Equals(x.Klasse, k));
                        var f = fz.Where(y => Ci.Equals(y.Klasse, k)).ToList();
                        var fehlt = f.Where(y => y.Art == "fehlt").ToList();
                        return new object?[] { k, anz, fehlt.Count, fehlt.Select(y => (y.SchuelerId, y.Datum)).Distinct().Count(), fehlt.Count(y => y.Status == "entschuldigt"),
                            fehlt.Count(y => y.Status == "unentschuldigt"), fehlt.Count(y => y.Status == "offen"), f.Count(y => y.Art == "verspaetet"),
                            anz == 0 ? "" : Math.Round(fehlt.Count / (double)anz, 1).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) };
                    }));
            }
            case "fehlzeiten_fach":
            {
                var fz = Fz(von, bis).Where(f => f.Art == "fehlt");
                return B(new() { "Klasse", "Fach", "Fehlstunden", "Schüler*innen", "entschuldigt", "unentschuldigt", "offen" },
                    fz.GroupBy(f => (f.Klasse, f.Fach)).OrderBy(g => g.Key.Klasse, De).ThenBy(g => g.Key.Fach, De)
                        .Select(g => new object?[] { g.Key.Klasse, g.Key.Fach.Length > 0 ? g.Key.Fach : "–", g.Count(), g.Select(f => f.SchuelerId).Distinct().Count(),
                            g.Count(f => f.Status == "entschuldigt"), g.Count(f => f.Status == "unentschuldigt"), g.Count(f => f.Status == "offen") }));
            }
            case "fehltage_klasse":
            {
                // wie die Vorlage „Fehltage pro Klasse“: je Klasse, je Schüler*in eine Zeile je Fehltag, Summe je Schüler*in mit Schultagen, Summe je Klasse.
                // Ein Tag gilt als unentschuldigt, wenn eine Stunde unentschuldigt ist, als entschuldigt, wenn alle Stunden entschuldigt sind, sonst als offen.
                var fz = Fz(von, bis).Where(f => f.Art == "fehlt").ToList();
                var zeilen = new List<(string, object?[])>();
                foreach (var k in klassen)
                {
                    var schultage = Schultage(k, von, bis);
                    var kinder = schuelerListe.Where(x => Ci.Equals(x.Klasse, k)).Select(x => (x, tage: fz.Where(f => f.SchuelerId == x.Id).GroupBy(f => f.Datum).OrderBy(g => g.Key).ToList()))
                        .Where(t => t.tage.Count > 0).OrderBy(t => N(t.x.Id), De).ToList();
                    if (kinder.Count == 0) continue;
                    zeilen.Add(("gruppe", new object?[] { $"Klasse {k}", "", "", "", "", "", "", "" }));
                    int sT = 0, sS = 0, sE = 0, sU = 0, sO = 0;
                    foreach (var (x, tage) in kinder)
                    {
                        int tE = 0, tU = 0, tO = 0;
                        for (var i = 0; i < tage.Count; i++)
                        {
                            var g = tage[i];
                            var status = g.Any(f => f.Status == "unentschuldigt") ? "u" : g.All(f => f.Status == "entschuldigt") ? "e" : "o";
                            if (status == "e") tE++; else if (status == "u") tU++; else tO++;
                            zeilen.Add(("", new object?[] { i == 0 ? N(x.Id) : "", Datum(g.Key), 1, g.Count(), status == "e" ? 1 : 0, status == "u" ? 1 : 0, status == "o" ? 1 : 0, "" }));
                        }
                        var stunden = tage.Sum(t => t.Count());
                        zeilen.Add(("zwischensumme", new object?[] { "", $"Summe {N(x.Id)}", tage.Count, stunden, tE, tU, tO, schultage }));
                        sT += tage.Count; sS += stunden; sE += tE; sU += tU; sO += tO;
                    }
                    zeilen.Add(("summe", new object?[] { $"Summe {k}", $"{kinder.Count} Schüler*innen", sT, sS, sE, sU, sO, "" }));
                }
                return G(new() { "Schüler*in", "Datum", "Fehltage", "Fehlstunden", "entsch.", "unent.", "offen", "Schultage" }, zeilen,
                    $"Schultage: Unterrichtstage der Klasse im Zeitraum (bis heute). Ein Fehltag ist unentschuldigt, sobald eine Stunde unentschuldigt ist.");
            }
            case "abwesenheitsueberschreitung":
            {
                n1 = n1 > 0 ? n1 : 16;
                n2 = n2 > 0 ? n2 : 4;
                var ab = bis.AddDays(-7 * n2 + 1);
                var fz = Fz(ab, bis).Where(f => f.Art == "fehlt").ToList();
                return B(new() { "Klasse", "Schüler*in", "Fehlstunden", "unentschuldigt", "Zeitraum" },
                    schuelerListe.Select(x => (x, f: fz.Where(f => f.SchuelerId == x.Id).ToList())).Where(t => t.f.Count > n1)
                        .Select(t => new object?[] { t.x.Klasse, N(t.x.Id), t.f.Count, t.f.Count(f => f.Status == "unentschuldigt"), $"{ab:dd.MM.yyyy} – {bis:dd.MM.yyyy}" }),
                    $"Schüler*innen mit mehr als {n1} Fehlstunden in den letzten {n2} Wochen bis {bis:dd.MM.yyyy}.");
            }
            case "abwesenheiten":
                return B(new() { "Klasse", "Schüler*in", "Datum", "Stunden", "Art", "Grund", "Status", "Text" },
                    _listen.Abwesenheiten(s, b, von, bis, string.IsNullOrEmpty(klasse) ? null : klasse, null).Where(z => schueler is null || z.SchuelerId == schueler)
                        .OrderBy(z => z.Klasse, De).ThenBy(z => z.Schueler, De).ThenBy(z => z.Datum)
                        .Select(z => new object?[] { z.Klasse, z.Schueler, Datum(z.Datum), z.StundeVon == z.StundeBis ? $"{z.StundeVon}." : $"{z.StundeVon}.–{z.StundeBis}.",
                            z.Art switch { "verspaetet" => $"verspätet ({z.Minuten} min)", "vorzeitig" => $"vorzeitig gegangen ({z.Minuten} min)", _ => "abwesend" }, Grund(z.Grund), z.Status, z.Bemerkung }));
            case "unentschuldigt":
            {
                // wie die Vorlage „Unentschuldigte Abwesenheiten“: je Klasse und Schüler*in die zusammenhängenden Abwesenheiten ohne Entschuldigung
                // (unentschuldigt bzw. noch offen) mit Beginn, Ende, Tagen, Stunden und Grund; Summe je Schüler*in und je Klasse
                n1 = Math.Max(1, n1);
                var fz = Fz(von, bis).Where(f => f.Art == "fehlt" && f.Status is "unentschuldigt" or "offen").ToList();
                var zeilen = new List<(string, object?[])>();
                foreach (var k in klassen)
                {
                    var kinder = new List<(int Id, List<List<IGrouping<string, Fehlzeit>>> Bloecke)>();
                    foreach (var x in schuelerListe.Where(x => Ci.Equals(x.Klasse, k)).OrderBy(x => N(x.Id), De))
                    {
                        var tage = fz.Where(f => f.SchuelerId == x.Id).GroupBy(f => f.Datum).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                        // zusammenhängend: zwischen zwei Fehltagen liegen nur unterrichtsfreie Tage
                        var bloecke = new List<List<IGrouping<string, Fehlzeit>>>();
                        foreach (var t in tage)
                        {
                            var d = DateOnly.Parse(t.Key);
                            if (bloecke.Count > 0)
                            {
                                var letzt = DateOnly.Parse(bloecke[^1][^1].Key);
                                var luecke = false;
                                for (var z = letzt.AddDays(1); z < d; z = z.AddDays(1)) if (Unterrichtstag(k, z)) { luecke = true; break; }
                                if (!luecke) { bloecke[^1].Add(t); continue; }
                            }
                            bloecke.Add(new() { t });
                        }
                        bloecke = bloecke.Where(bl => bl.Count >= n1).ToList();
                        if (bloecke.Count > 0) kinder.Add((x.Id, bloecke));
                    }
                    if (kinder.Count == 0) continue;
                    zeilen.Add(("gruppe", new object?[] { $"Klasse {k}", "", "", "", "", "", "" }));
                    int kT = 0, kS = 0;
                    foreach (var (id, bloecke) in kinder)
                    {
                        for (var i = 0; i < bloecke.Count; i++)
                        {
                            var bl = bloecke[i];
                            var alle = bl.SelectMany(g => g).ToList();
                            var gr = alle.Select(f => Grund(f.Grund)).Where(g => g.Length > 0).Distinct(Ci).ToList();
                            zeilen.Add(("", new object?[] { i == 0 ? N(id) : "", Datum(bl[0].Key), Datum(bl[^1].Key), bl.Count, alle.Count, string.Join(", ", gr),
                                alle.Any(f => f.Status == "unentschuldigt") ? "unentschuldigt" : "offen" }));
                        }
                        var tageS = bloecke.Sum(bl => bl.Count);
                        var stundenS = bloecke.Sum(bl => bl.Sum(g => g.Count()));
                        zeilen.Add(("zwischensumme", new object?[] { "", $"Summe {N(id)}", "", tageS, stundenS, "", "" }));
                        kT += tageS; kS += stundenS;
                    }
                    zeilen.Add(("summe", new object?[] { $"Summe {k}", $"{kinder.Count} Schüler*innen", "", kT, kS, "", "" }));
                }
                return G(new() { "Schüler*in", "Beginn", "Ende", "Tage", "Stunden", "Grund", "Status" }, zeilen,
                    n1 > 1 ? $"Zusammenhängende Abwesenheiten ohne Entschuldigung (unentschuldigt oder noch offen) von mindestens {n1} Tagen."
                        : "Zusammenhängende Abwesenheiten ohne Entschuldigung (unentschuldigt oder noch offen).");
            }
            case "statusuebersicht":
            {
                var fz = Fz(von, bis).Where(f => f.Art == "fehlt").ToList();
                var gl = Gruende("abwesenheit");
                var spalten = new List<string> { "Klasse", "Schüler*in", "Fehlstunden", "offen", "entschuldigt", "unentschuldigt" };
                spalten.AddRange(gl.Select(g => g.Name));
                spalten.Add("ohne Grund");
                return B(spalten, schuelerListe.Select(x =>
                {
                    var f = fz.Where(y => y.SchuelerId == x.Id).ToList();
                    var z = new List<object?> { x.Klasse, N(x.Id), f.Count, f.Count(y => y.Status == "offen"), f.Count(y => y.Status == "entschuldigt"), f.Count(y => y.Status == "unentschuldigt") };
                    z.AddRange(gl.Select(g => (object?)f.Count(y => Ci.Equals(y.Grund, g.Kurz))));
                    z.Add(f.Count(y => y.Grund.Length == 0));
                    return z;
                }).Where(z => (int)z[2]! > 0));
            }
            case "anwesenheiten":
            {
                var bisAnw = bis > _uhr.Heute ? _uhr.Heute : bis;
                var fz = Fz(von, bisAnw).Where(f => f.Art == "fehlt").ToList();
                var geplant = new Dictionary<string, int>(Ci);
                foreach (var k in klassen.Where(k => schuelerListe.Any(x => Ci.Equals(x.Klasse, k))))
                {
                    var n = 0;
                    for (var d = von; d <= bisAnw; d = d.AddDays(1))
                        if (!_plan.Frei(s, d).Frei)
                            n += _plan.Karten(s, d, Elementtyp.Klasse, k).Where(x => x.Unterricht.HasValue && x.Status is not ("entfall" or "veranstaltung")).Sum(x => x.Bis - x.Von + 1);
                    geplant[k] = n;
                }
                return B(new() { "Klasse", "Schüler*in", "Unterrichtsstunden", "anwesend", "Fehlstunden", "Anwesenheit" },
                    schuelerListe.Select(x =>
                    {
                        var soll = geplant.GetValueOrDefault(x.Klasse);
                        var fehl = fz.Count(f => f.SchuelerId == x.Id);
                        return new object?[] { x.Klasse, N(x.Id), soll, Math.Max(0, soll - fehl), fehl, soll == 0 ? "–" : $"{Math.Round(100.0 * Math.Max(0, soll - fehl) / soll)} %" };
                    }));
            }
            case "entschuldigungsschreiben":
                return B(new() { "Schüler*in", "Klasse", "Datum", "Stunden", "Fächer", "Grund", "Status" },
                    _listen.Abwesenheiten(s, b, von, bis, string.IsNullOrEmpty(klasse) ? null : klasse, null)
                        .Where(z => z.Art == "fehlt" && z.Status != "entschuldigt" && (schueler is null || z.SchuelerId == schueler))
                        .OrderBy(z => z.Schueler, De).ThenBy(z => z.Datum)
                        .Select(z => new object?[] { z.Schueler, z.Klasse, Datum(z.Datum), z.StundeVon == z.StundeBis ? $"{z.StundeVon}." : $"{z.StundeVon}.–{z.StundeBis}.",
                            string.Join(", ", z.Faecher), Grund(z.Grund), z.Status }),
                    "Je Schüler*in eine Seite mit den noch nicht entschuldigten Fehlstunden zum Unterschreiben durch die Erziehungsberechtigten.");
            case "klassenbucheintraege":
            case "klassenbucheintraege_schueler":
            {
                var liste = _listen.Vermerke(s, von, bis, string.IsNullOrEmpty(klasse) ? null : klasse, null).Where(v => schueler is null || v.SchuelerId == schueler);
                if (art == "klassenbucheintraege_schueler") liste = liste.Where(v => v.SchuelerId != null).OrderBy(v => v.Schueler, De).ThenBy(v => v.Datum);
                else liste = liste.OrderBy(v => v.Klasse, De).ThenBy(v => v.Datum);
                return B(new() { "Datum", "Klasse", "Schüler*in", "Kategorie", "Text", "Lehrkraft" },
                    liste.Select(v => new object?[] { Datum(v.Datum), v.Klasse, v.Schueler, v.Art, v.Text, v.Lehrer }));
            }
            case "arbeitsbericht_tag":
            case "wochenuebersicht":
            {
                if (art == "wochenuebersicht") { von = Plandienst.Montag(von); bis = von.AddDays(Math.Max(5, s.Paket.Zeitraster.TageProWoche) - 1); }
                var fz = _kb.FehlzeitenZeitraum(von, bis, klasse);
                return B(new() { "Datum", "Std.", "Fach", "Lehrkraft", "Lehrstoff", "Hausaufgabe", "Fehlende" },
                    _kb.Klasse(klasse!, von, bis).OrderBy(e => e.Datum).ThenBy(e => e.Stunde).Select(e => new object?[] { Datum(e.Datum), $"{e.Stunde}.", e.Fach, e.Lehrer, e.Thema, e.Hausaufgabe,
                        fz.Where(f => f.Datum == e.Datum && f.Stunde == e.Stunde && f.Art == "fehlt").Select(f => f.SchuelerId).Distinct().Count() }),
                    art == "wochenuebersicht" ? $"Woche {von:dd.MM.} – {bis:dd.MM.yyyy}" : null);
            }
            case "tagesuebersicht":
            {
                var tag = _kb.Woche(s, klasse!, Plandienst.Montag(von));
                var iso = Plandienst.Iso(von);
                var json = JsonSerializer.SerializeToElement(tag, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var zeilen = new List<object?[]>();
                foreach (var t in json.GetProperty("tage").EnumerateArray().Where(t => t.GetProperty("datum").GetString() == iso))
                    foreach (var st in t.GetProperty("stunden").EnumerateArray())
                    {
                        var vonN = st.GetProperty("von").GetInt32();
                        var bisN = st.GetProperty("bis").GetInt32();
                        zeilen.Add(new object?[] { vonN == bisN ? $"{vonN}." : $"{vonN}.–{bisN}.", $"{st.GetProperty("beginn").GetString()}–{st.GetProperty("ende").GetString()}",
                            st.GetProperty("fach").GetString(), string.Join(", ", st.GetProperty("lehrer").EnumerateArray().Select(x => x.GetString())),
                            string.Join(", ", st.GetProperty("raeume").EnumerateArray().Select(x => x.GetString())), st.GetProperty("status").GetString() == "entfall" ? "Entfall" : st.GetProperty("thema").GetString(),
                            string.Join(", ", st.GetProperty("fehlend").EnumerateArray().Select(x => x.GetString())), st.GetProperty("hausaufgabe").GetString() });
                    }
                return B(new() { "Std.", "Zeit", "Fach", "Lehrkraft", "Raum", "Lehrstoff", "Abwesend", "Hausaufgabe" }, zeilen, $"{Plandienst.Wochentage[Plandienst.TagIndex(von)]}, {von:dd.MM.yyyy}");
            }
            case "pruefungen":
                return B(new() { "Datum", "Stunden", "Zeit", "Prüfung", "Klassen", "Lehrkräfte", "Räume", "Status" },
                    _listen.Pruefungen(s, _rueck.OffeneKlausuren(), von, bis, string.IsNullOrEmpty(klasse) ? null : klasse, null)
                        .Select(p => new object?[] { Datum(p.Datum), p.StundeVon == p.StundeBis ? $"{p.StundeVon}." : $"{p.StundeVon}.–{p.StundeBis}.", $"{p.Beginn}–{p.Ende}", p.Titel,
                            string.Join(", ", p.Klassen), string.Join(", ", p.Lehrer), string.Join(", ", p.Raeume), p.Status }));
            case "befreiungen":
                return B(new() { "Klasse", "Schüler*in", "Von", "Bis", "Wochentag", "Fach", "Beginn", "Ende", "Grund", "Text" },
                    Befreiungen(s, string.IsNullOrEmpty(klasse) ? null : klasse, schueler, von, bis).Select(x => new object?[] { x.Klasse, x.Schueler, Datum(x.Von), Datum(x.Bis),
                        x.Wochentag == 0 ? "jeden Tag" : Plandienst.Wochentage[x.Wochentag - 1], x.Fach, x.Beginn, x.Ende, Grund(x.Grund), x.Text }));
            case "klassendienste":
                return B(new() { "Klasse", "Dienst", "Schüler*in", "Von", "Bis", "Text" },
                    Dienste(s, string.IsNullOrEmpty(klasse) ? null : klasse, von, bis).Where(z => schueler is null || z.SchuelerId == schueler)
                        .Select(z => new object?[] { z.Klasse, z.Dienst, z.Schueler, Datum(z.Von), Datum(z.Bis), z.Text }));
            default:
                throw new ArgumentException("Unbekannter Bericht.");
        }
    }

    public static byte[] BerichtCsv(Bericht b) => Listendienst.Csv(b.Spalten, b.Zeilen);
}
