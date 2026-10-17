using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Klassenwechsel einer Schülerin/eines Schülers ab einem Datum (Klasse vorher = Klasse am Vortag beim Anlegen).</summary>
public sealed record Klassenwechsel(int Id, int SchuelerId, string Klasse, string KlasseVorher, string Von, string Erstellt);

/// <summary>Zeile im Klassenverlauf (Dialog „Klassenwechsel“): Klasse mit Beginn- und Enddatum (leer = offen).</summary>
public sealed record Klassenzeitraum(string Klasse, string Beginn, string Ende, int? WechselId);

/// <summary>
/// Mitglied einer Schülergruppe mit Zeitraum (leer = unbegrenzt bzw. Zeitraum der Gruppe). In der Standardgruppe eines Unterrichts sind
/// die Zeilen Abweichungen: ausgeschlossen = abgemeldet; Von/Bis = nimmt nur in diesem Zeitraum teil; Schüler*innen ohne Zeile nehmen
/// teil, solange sie in einer Klasse des Unterrichts sind.
/// </summary>
public sealed record Gruppenmitglied(int SchuelerId, string Von, string Bis, bool Ausgeschlossen);

/// <summary>Schülergruppe eines Unterrichts: „standard“ (die Klassen des Unterrichts) oder „gast“ (befristeter Unterrichtswechsel).</summary>
public sealed record Schuelergruppe(int Id, int Unterricht, string Name, string Art, string Von, string Bis, List<Gruppenmitglied> Mitglieder)
{
    public bool Gast => Art == Unterrichtsgruppen.Gast;

    /// <summary>Gilt die Gruppe an diesem Tag?</summary>
    public bool Gilt(DateOnly d) => Klassenzugehoerigkeit.Im(Von, Bis, d);
}

/// <summary>Unveränderlicher Stand der Zuordnungen aus UntPlanWeb (wird nach jeder Änderung neu geladen).</summary>
public sealed class Zuordnungen
{
    public static readonly Zuordnungen Leer = new(new(), new(), DateOnly.MinValue);

    /// <summary>Klassenwechsel je Schüler*in, aufsteigend nach Beginn.</summary>
    public IReadOnlyDictionary<int, List<Klassenwechsel>> Wechsel { get; }
    /// <summary>Gespeicherte Schülergruppen je Unterricht.</summary>
    public IReadOnlyDictionary<int, List<Schuelergruppe>> Gruppen { get; }
    /// <summary>Tag, für den die Klasse von heute in die Schülerliste übernommen wurde.</summary>
    public DateOnly Tag { get; }

    public Zuordnungen(Dictionary<int, List<Klassenwechsel>> wechsel, Dictionary<int, List<Schuelergruppe>> gruppen, DateOnly tag)
    {
        Wechsel = wechsel;
        Gruppen = gruppen;
        Tag = tag;
    }

    public static Zuordnungen Laden(Db db, DateOnly heute)
    {
        var wechsel = db.Liste("SELECT * FROM klassenwechsel ORDER BY schueler_id, von", r => new Klassenwechsel(r.Zahl("id"), r.Zahl("schueler_id"), r.Text("klasse"),
                r.Text("klasse_vorher"), r.Text("von"), r.Text("erstellt")))
            .GroupBy(w => w.SchuelerId).ToDictionary(g => g.Key, g => g.OrderBy(w => w.Von, StringComparer.Ordinal).ToList());
        var mitglieder = db.Liste("SELECT * FROM schuelergruppe_mitglieder", r => (Gruppe: r.Zahl("gruppe_id"),
                M: new Gruppenmitglied(r.Zahl("schueler_id"), r.Text("von"), r.Text("bis"), r.Wahr("ausgeschlossen"))))
            .GroupBy(x => x.Gruppe).ToDictionary(g => g.Key, g => g.Select(x => x.M).ToList());
        var gruppen = db.Liste("SELECT * FROM schuelergruppen ORDER BY unterricht, art DESC, von, id", r => new Schuelergruppe(r.Zahl("id"), r.Zahl("unterricht"), r.Text("name"),
                r.Text("art"), r.Text("von"), r.Text("bis"), new List<Gruppenmitglied>()))
            .Select(g => g with { Mitglieder = mitglieder.GetValueOrDefault(g.Id) ?? new() })
            .GroupBy(g => g.Unterricht).ToDictionary(g => g.Key, g => g.ToList());
        return new Zuordnungen(wechsel, gruppen, heute);
    }
}

/// <summary>
/// Zeitabhängige Klassenzugehörigkeit und Teilnahme am Unterricht. Grundlage ist die Schülerliste aus UntPlan (bzw. der
/// Schülerverwaltung); Klassenwechsel ab Datum und Schülergruppen aus UntPlanWeb überlagern sie, ohne die übertragenen Daten zu ändern.
/// </summary>
public static class Klassenzugehoerigkeit
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);

    /// <summary>Liegt der Tag im Zeitraum (yyyy-MM-dd, leer = offen; beide Grenzen einschließlich)?</summary>
    public static bool Im(string von, string bis, DateOnly d)
    {
        var t = d.ToString("yyyy-MM-dd");
        return (string.IsNullOrEmpty(von) || string.CompareOrdinal(von, t) <= 0) && (string.IsNullOrEmpty(bis) || string.CompareOrdinal(t, bis) <= 0);
    }

    /// <summary>Klasse der Schülerin/des Schülers an diesem Tag.</summary>
    public static string KlasseAm(Stand s, WebSchueler x, DateOnly d)
    {
        if (!s.Zuordnungen.Wechsel.TryGetValue(x.Id, out var liste) || liste.Count == 0) return x.Klasse;
        var t = d.ToString("yyyy-MM-dd");
        var letzte = liste.LastOrDefault(w => string.CompareOrdinal(w.Von, t) <= 0);
        if (letzte != null) return letzte.Klasse;
        return liste[0].KlasseVorher.Length > 0 ? liste[0].KlasseVorher : x.Klasse;
    }

    private static List<WebSchueler> Sortiert(IEnumerable<WebSchueler> liste) =>
        liste.OrderBy(x => x.Klasse, De).ThenBy(x => x.Name, De).ThenBy(x => x.Vorname, De).ToList();

    /// <summary>Schüler*innen der Klassen an diesem Tag (mit der Klasse dieses Tages).</summary>
    public static List<WebSchueler> Am(Stand s, IEnumerable<string> klassen, DateOnly d)
    {
        var k = klassen.ToHashSet(Ci);
        return Sortiert(s.Schueler.Select(x => x with { Klasse = KlasseAm(s, x, d) }).Where(x => k.Contains(x.Klasse)));
    }

    /// <summary>Klassen einer Schülerin/eines Schülers im Zeitraum.</summary>
    public static List<string> KlassenIm(Stand s, WebSchueler x, DateOnly von, DateOnly bis)
    {
        var res = new List<string> { KlasseAm(s, x, von) };
        if (s.Zuordnungen.Wechsel.TryGetValue(x.Id, out var liste))
        {
            var v = von.ToString("yyyy-MM-dd");
            var b = bis.ToString("yyyy-MM-dd");
            res.AddRange(liste.Where(w => string.CompareOrdinal(w.Von, v) > 0 && string.CompareOrdinal(w.Von, b) <= 0).Select(w => w.Klasse));
        }
        return res.Distinct(Ci).ToList();
    }

    /// <summary>Schüler*innen, die im Zeitraum (mindestens zeitweise) in einer der Klassen waren – Klasse = letzte passende Klasse im Zeitraum.</summary>
    public static List<WebSchueler> ImZeitraum(Stand s, IEnumerable<string> klassen, DateOnly von, DateOnly bis)
    {
        var k = klassen.ToHashSet(Ci);
        if (s.Zuordnungen.Wechsel.Count == 0) return Sortiert(s.Schueler.Where(x => k.Contains(x.Klasse)));
        return Sortiert(s.Schueler.Select(x => (X: x, K: KlassenIm(s, x, von, bis).LastOrDefault(k.Contains)))
            .Where(z => z.K != null).Select(z => z.X with { Klasse = z.K! }));
    }

    /// <summary>
    /// Teilnehmer*innen einer Stunde: ohne gespeicherte Schülergruppen alle Schüler*innen der Klassen an diesem Tag; sonst die
    /// Standardgruppe (Klassen mit Abmeldungen bzw. befristeter Teilnahme) und die an diesem Tag gültigen Gast-Schülergruppen.
    /// </summary>
    public static List<WebSchueler> DerStunde(Stand s, int? unterricht, IEnumerable<string> klassen, DateOnly d)
    {
        var basis = Am(s, klassen, d);
        if (unterricht is not int u || !s.Zuordnungen.Gruppen.TryGetValue(u, out var gruppen) || gruppen.Count == 0) return basis;
        var alle = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var res = new Dictionary<int, WebSchueler>();
        void Hinzu(int id) { if (!res.ContainsKey(id) && alle.TryGetValue(id, out var x)) res[id] = x with { Klasse = KlasseAm(s, x, d) }; }
        var standard = gruppen.FirstOrDefault(g => !g.Gast);
        var zeilen = standard?.Mitglieder.ToDictionary(m => m.SchuelerId) ?? new Dictionary<int, Gruppenmitglied>();
        foreach (var x in basis.Where(x => !zeilen.ContainsKey(x.Id))) res[x.Id] = x;
        foreach (var g in gruppen.Where(g => g.Gilt(d)))
            foreach (var m in g.Mitglieder.Where(m => !m.Ausgeschlossen && Im(m.Von, m.Bis, d))) Hinzu(m.SchuelerId);
        return Sortiert(res.Values);
    }

    /// <summary>Gast-Schüler*innen der Stunde (aus einer Gast-Schülergruppe, z. B. befristeter Unterrichtswechsel).</summary>
    public static HashSet<int> Gaeste(Stand s, int? unterricht, DateOnly d) =>
        unterricht is int u && s.Zuordnungen.Gruppen.TryGetValue(u, out var gruppen)
            ? gruppen.Where(g => g.Gast && g.Gilt(d)).SelectMany(g => g.Mitglieder.Where(m => !m.Ausgeschlossen && Im(m.Von, m.Bis, d)).Select(m => m.SchuelerId)).ToHashSet()
            : new HashSet<int>();

    /// <summary>Klassenverlauf: Klasse bis zum ersten Wechsel (ab Schuljahresbeginn), danach je Wechsel bis zum Tag vor dem nächsten.</summary>
    public static List<Klassenzeitraum> Verlauf(Stand s, WebSchueler x)
    {
        var beginn = s.SchuljahrVon == DateOnly.MinValue ? "" : s.SchuljahrVon.ToString("yyyy-MM-dd");
        var ende = s.SchuljahrBis == DateOnly.MaxValue ? "" : s.SchuljahrBis.ToString("yyyy-MM-dd");
        if (!s.Zuordnungen.Wechsel.TryGetValue(x.Id, out var liste) || liste.Count == 0) return new() { new(x.Klasse, beginn, ende, null) };
        static string Vortag(string iso) => DateOnly.Parse(iso).AddDays(-1).ToString("yyyy-MM-dd");
        var res = new List<Klassenzeitraum>();
        if (beginn.Length == 0 || string.CompareOrdinal(liste[0].Von, beginn) > 0)
            res.Add(new(liste[0].KlasseVorher.Length > 0 ? liste[0].KlasseVorher : x.Klasse, beginn, Vortag(liste[0].Von), null));
        for (var i = 0; i < liste.Count; i++)
            res.Add(new(liste[i].Klasse, liste[i].Von, i + 1 < liste.Count ? Vortag(liste[i + 1].Von) : ende, liste[i].Id));
        return res;
    }
}

/// <summary>Eingabe einer Zeile im Dialog „Schüler*innen in der Schülergruppe“.</summary>
public sealed record MitgliedEingabe(int SchuelerId, bool Auswahl, string? Von, string? Bis);

/// <summary>
/// Klassenwechsel ab Datum, Schülergruppen je Unterricht und befristeter Unterrichtswechsel (Unterricht → Unterricht Klassen).
/// Rechte: Klassenwechsel mit „Schülerverwaltung“; Unterricht Klassen lesen bzw. Schülergruppen bearbeiten mit „Unterricht Klassen und Schülergruppen“.
/// </summary>
public sealed class Unterrichtsgruppen
{
    public const string Standard = "standard", Gast = "gast";

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst _pakete;
    private readonly Listendienst _listen;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true);

    public Unterrichtsgruppen(Db db, Uhr uhr, Paketdienst pakete, Listendienst listen) { _db = db; _uhr = uhr; _pakete = pakete; _listen = listen; }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");

    private Stand Neu()
    {
        _pakete.SchuelerLaden();
        return _pakete.Aktuell!;
    }

    public static void Lesen(Benutzer b)
    {
        if (!Rechtedienst.DarfLesen(b, Recht.UnterrichtKlassen)) throw new UnauthorizedAccessException("„Unterricht Klassen“ ist für Ihr Konto nicht freigegeben.");
    }

    public static void Schreiben(Benutzer b)
    {
        if (!Rechtedienst.Darf(b, Recht.UnterrichtKlassen)) throw new UnauthorizedAccessException("Schülergruppen bearbeiten darf Ihr Konto nicht.");
    }

    private static DateOnly DatumPruefen(string? wert, string feld)
    {
        if (!DateOnly.TryParseExact((wert ?? "").Trim(), "yyyy-MM-dd", out var d)) throw new ArgumentException($"Bitte ein gültiges Datum für „{feld}“ eingeben.");
        return d;
    }

    private static string OptionalesDatum(string? wert, string feld) => string.IsNullOrWhiteSpace(wert) ? "" : DatumPruefen(wert, feld).ToString("yyyy-MM-dd");

    private static WebSchueler SchuelerVon(Stand s, int id) => s.Schueler.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Schülerin / unbekannter Schüler.");

    // ------------------------------------------------------------------------------------------ Klassenwechsel

    public List<Klassenzeitraum> Verlauf(Benutzer b, Stand s, int schuelerId)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
        return Klassenzugehoerigkeit.Verlauf(s, SchuelerVon(s, schuelerId));
    }

    /// <summary>Klassenwechsel ab Beginndatum eintragen (gleicher Beginn ersetzt den bisherigen Wechsel).</summary>
    public List<Klassenzeitraum> KlassenwechselAnlegen(Benutzer b, Stand s, int schuelerId, string? klasse, string? beginn)
    {
        if (!Schuelerverwaltung.Verwalten(b)) throw new UnauthorizedAccessException("Den Klassenwechsel nimmt die Administration vor.");
        var x = SchuelerVon(s, schuelerId);
        if (!s.Klassen.TryGetValue((klasse ?? "").Trim(), out var k)) throw new ArgumentException("Bitte die neue Klasse wählen.");
        var von = DatumPruefen(beginn, "Beginndatum");
        if (s.SchuljahrVon != DateOnly.MinValue && (von < s.SchuljahrVon.AddYears(-1) || von > s.SchuljahrBis.AddYears(1)))
            throw new ArgumentException("Das Beginndatum liegt außerhalb des Schuljahres.");
        var t = von.ToString("yyyy-MM-dd");
        // ohne den Wechsel am selben Tag rechnen (er wird ersetzt)
        var ohne = s.Zuordnungen.Wechsel.TryGetValue(schuelerId, out var bisher) ? bisher.Where(w => w.Von != t).ToList() : new List<Klassenwechsel>();
        var vorher = ohne.LastOrDefault(w => string.CompareOrdinal(w.Von, t) < 0)?.Klasse
                     ?? (ohne.Count > 0 && ohne[0].KlasseVorher.Length > 0 ? ohne[0].KlasseVorher : bisher?.FirstOrDefault()?.KlasseVorher is { Length: > 0 } kv ? kv : x.Klasse);
        if (Ci.Equals(vorher, k.Kurz)) throw new ArgumentException($"{x.Vorname} {x.Name} ist zu diesem Datum bereits in der Klasse {k.Kurz}.");
        var p = new[] { Db.P("@s", schuelerId), Db.P("@k", k.Kurz), Db.P("@kv", vorher), Db.P("@v", t), Db.P("@e", Jetzt), Db.P("@b", b.Id) };
        if (_db.Ausfuehren("UPDATE klassenwechsel SET klasse = @k, klasse_vorher = @kv, erstellt = @e, benutzer_id = @b WHERE schueler_id = @s AND von = @v", p) == 0)
            _db.Ausfuehren("INSERT INTO klassenwechsel (schueler_id, klasse, klasse_vorher, von, erstellt, benutzer_id) VALUES (@s, @k, @kv, @v, @e, @b)", p);
        var neu = Neu();
        return Klassenzugehoerigkeit.Verlauf(neu, SchuelerVon(neu, schuelerId));
    }

    public List<Klassenzeitraum> KlassenwechselLoeschen(Benutzer b, Stand s, int schuelerId, int id)
    {
        if (!Schuelerverwaltung.Verwalten(b)) throw new UnauthorizedAccessException("Den Klassenwechsel nimmt die Administration vor.");
        var liste = s.Zuordnungen.Wechsel.GetValueOrDefault(schuelerId) ?? new();
        var w = liste.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannter Klassenwechsel.");
        // der erste Wechsel trägt die Klasse davor – beim Löschen an den nächsten weitergeben
        if (liste.IndexOf(w) == 0 && liste.Count > 1)
            _db.Ausfuehren("UPDATE klassenwechsel SET klasse_vorher = @kv WHERE id = @id", Db.P("@kv", w.KlasseVorher), Db.P("@id", liste[1].Id));
        _db.Ausfuehren("DELETE FROM klassenwechsel WHERE id = @id", Db.P("@id", id));
        var neu = Neu();
        return Klassenzugehoerigkeit.Verlauf(neu, SchuelerVon(neu, schuelerId));
    }

    // ------------------------------------------------------------------------------------------ Unterricht Klassen

    /// <summary>Name der Standardgruppe: Kurs bzw. Schülergruppe aus UntPlan, sonst „Fach_Klassen“ (z. B. „D_1a“).</summary>
    public static string Standardname(WebUnterricht u)
    {
        if (u.Kurs.Length > 0) return u.Kurs;
        if (u.Zeilen.Select(z => z.Gruppe).FirstOrDefault(g => g.Length > 0) is string g) return g;
        var fach = u.Zeilen.Select(z => z.Fach).FirstOrDefault(f => f.Length > 0) ?? "U";
        return $"{fach}_{string.Join("_", u.Zeilen.SelectMany(z => z.Klassen).Distinct(Ci).OrderBy(x => x, De))}";
    }

    private static List<string> Klassen(WebUnterricht u) => u.Zeilen.SelectMany(z => z.Klassen).Distinct(Ci).OrderBy(x => x, De).ToList();

    private static WebUnterricht UnterrichtVon(Stand s, int id) => s.Unterricht.TryGetValue(id, out var u) ? u : throw new ArgumentException("Unbekannter Unterricht.");

    /// <summary>Stichtag innerhalb eines Zeitraums: heute, sonst der nächstgelegene Rand.</summary>
    public static DateOnly Stichtag(DateOnly heute, DateOnly von, DateOnly bis) => heute < von ? von : heute > bis ? bis : heute;

    /// <summary>Unterrichte einer Klasse im Zeitraum mit Schülerzahl (am Stichtag) und Schülergruppen.</summary>
    public object Klassenliste(Benutzer b, Stand s, string klasse, DateOnly von, DateOnly bis)
    {
        Lesen(b);
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (bis < von) (von, bis) = (bis, von);
        var stichtag = Stichtag(_uhr.Heute, von, bis);
        var zeilen = _listen.Unterrichte(s, von, null, klasse).Concat(_listen.Unterrichte(s, bis, null, klasse)).GroupBy(z => z.Id).Select(g => g.Last()).ToList();
        return new
        {
            klasse, von, bis, stichtag, schreiben = Rechtedienst.Darf(b, Recht.UnterrichtKlassen),
            leitung = s.Klassen[klasse].Klassenleitung,
            unterricht = zeilen.Select(z =>
            {
                var u = s.Unterricht.GetValueOrDefault(z.Id);
                var gruppen = s.Zuordnungen.Gruppen.GetValueOrDefault(z.Id) ?? new();
                return new
                {
                    z.Id, z.Klassen, z.Fach, z.FachName, z.Farbe, z.Lehrer, z.Wochenstunden, z.Termine, z.Raeume,
                    gruppe = u is null ? z.Kurs : gruppen.FirstOrDefault(g => !g.Gast)?.Name is { Length: > 0 } n ? n : Standardname(u),
                    schueler = Klassenzugehoerigkeit.DerStunde(s, z.Id, z.Klassen, stichtag).Count,
                    gruppen = gruppen.Count + (gruppen.Any(g => !g.Gast) ? 0 : 1),
                    gaeste = gruppen.Count(g => g.Gast),
                    geaendert = gruppen.Any(g => !g.Gast && g.Mitglieder.Count > 0),
                };
            }).OrderBy(z => z.Fach, De).ThenBy(z => z.Id).ToList(),
        };
    }

    /// <summary>Schülergruppen eines Unterrichts (die Standardgruppe auch, wenn sie noch nicht gespeichert ist – Id 0).</summary>
    public object Gruppen(Benutzer b, Stand s, int unterricht, DateOnly stichtag)
    {
        Lesen(b);
        var u = UnterrichtVon(s, unterricht);
        var klassen = Klassen(u);
        var gespeichert = s.Zuordnungen.Gruppen.GetValueOrDefault(unterricht) ?? new();
        var liste = new List<Schuelergruppe>();
        if (!gespeichert.Any(g => !g.Gast)) liste.Add(new Schuelergruppe(0, unterricht, Standardname(u), Standard, "", "", new()));
        liste.AddRange(gespeichert);
        var alle = Klassenzugehoerigkeit.DerStunde(s, unterricht, klassen, stichtag).Select(x => x.Id).ToHashSet();
        return new
        {
            unterricht, klassen, fach = string.Join(", ", u.Zeilen.Select(z => z.Fach).Where(f => f.Length > 0).Distinct(Ci)),
            lehrer = u.Zeilen.Select(z => z.Lehrer).Where(l => l.Length > 0).Distinct(Ci).ToList(), stichtag,
            schreiben = Rechtedienst.Darf(b, Recht.UnterrichtKlassen),
            gruppen = liste.Select(g => new
            {
                g.Id, g.Name, g.Art, g.Von, g.Bis, gast = g.Gast, gilt = g.Gilt(stichtag),
                anzahl = g.Gast ? g.Mitglieder.Count(m => !m.Ausgeschlossen) : Standardmitglieder(s, u, g, stichtag).Count(alle.Contains),
                abweichungen = g.Gast ? 0 : g.Mitglieder.Count,
            }),
        };
    }

    /// <summary>Teilnehmende der Standardgruppe am Stichtag (ohne Gäste).</summary>
    private static IEnumerable<int> Standardmitglieder(Stand s, WebUnterricht u, Schuelergruppe g, DateOnly d)
    {
        var zeilen = g.Mitglieder.ToDictionary(m => m.SchuelerId);
        return Klassenzugehoerigkeit.Am(s, Klassen(u), d).Where(x => !zeilen.ContainsKey(x.Id)).Select(x => x.Id)
            .Concat(g.Mitglieder.Where(m => !m.Ausgeschlossen && Klassenzugehoerigkeit.Im(m.Von, m.Bis, d)).Select(m => m.SchuelerId)).Distinct();
    }

    private Schuelergruppe GruppeVon(Stand s, int unterricht, int id)
    {
        var u = UnterrichtVon(s, unterricht);
        var gespeichert = s.Zuordnungen.Gruppen.GetValueOrDefault(unterricht) ?? new();
        if (id == 0) return gespeichert.FirstOrDefault(g => !g.Gast) ?? new Schuelergruppe(0, unterricht, Standardname(u), Standard, "", "", new());
        return gespeichert.FirstOrDefault(g => g.Id == id) ?? throw new ArgumentException("Unbekannte Schülergruppe.");
    }

    /// <summary>
    /// Schüler*innen in der Schülergruppe: Standardgruppe = alle Schüler*innen der Klassen am Stichtag und weitere eingetragene;
    /// Gastgruppe = die eingetragenen Gäste. Je Zeile Auswahl (nimmt teil) und Zeitraum.
    /// </summary>
    public object Mitglieder(Benutzer b, Stand s, int unterricht, int gruppeId, DateOnly stichtag)
    {
        Lesen(b);
        var u = UnterrichtVon(s, unterricht);
        var g = GruppeVon(s, unterricht, gruppeId);
        var alle = s.Schueler.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        var geschlecht = _db.Liste("SELECT id, geschlecht FROM schueler_daten", r => (Id: r.Zahl("id"), G: r.Text("geschlecht"))).ToDictionary(x => x.Id, x => x.G);
        var zeilen = g.Mitglieder.ToDictionary(m => m.SchuelerId);
        var ids = (g.Gast ? new List<int>() : Klassenzugehoerigkeit.Am(s, Klassen(u), stichtag).Select(x => x.Id).ToList()).Concat(g.Mitglieder.Select(m => m.SchuelerId)).Distinct();
        var liste = ids.Where(alle.ContainsKey).Select(id =>
        {
            var x = alle[id];
            zeilen.TryGetValue(id, out var m);
            return new
            {
                x.Id, x.Name, x.Vorname, klasse = Klassenzugehoerigkeit.KlasseAm(s, x, stichtag), geschlecht = geschlecht.GetValueOrDefault(id) ?? "",
                auswahl = m is null || !m.Ausgeschlossen, von = m?.Von ?? "", bis = m?.Bis ?? "",
                teilnahme = m is null ? !g.Gast : !m.Ausgeschlossen && Klassenzugehoerigkeit.Im(m.Von, m.Bis, stichtag) && g.Gilt(stichtag),
            };
        }).OrderBy(x => x.Name, De).ThenBy(x => x.Vorname, De).ToList();
        return new
        {
            unterricht, gruppe = new { g.Id, g.Name, g.Art, g.Von, g.Bis, gast = g.Gast }, klassen = Klassen(u),
            fach = string.Join(", ", u.Zeilen.Select(z => z.Fach).Where(f => f.Length > 0).Distinct(Ci)), stichtag,
            schreiben = Rechtedienst.Darf(b, Recht.UnterrichtKlassen), schueler = liste,
        };
    }

    private int StandardgruppeAnlegen(Benutzer b, Stand s, int unterricht)
    {
        var u = UnterrichtVon(s, unterricht);
        _db.Ausfuehren("INSERT INTO schuelergruppen (unterricht, name, art, erstellt, benutzer_id) VALUES (@u, @n, @a, @e, @b)",
            Db.P("@u", unterricht), Db.P("@n", Standardname(u)), Db.P("@a", Standard), Db.P("@e", Jetzt), Db.P("@b", b.Id));
        return Convert.ToInt32(_db.Wert("SELECT MAX(id) FROM schuelergruppen WHERE unterricht = @u AND art = @a", Db.P("@u", unterricht), Db.P("@a", Standard)));
    }

    /// <summary>Auswahl und Zeiträume der Schülergruppe speichern (nur die übergebenen Zeilen).</summary>
    public object MitgliederSpeichern(Benutzer b, Stand s, int unterricht, int gruppeId, List<MitgliedEingabe> zeilen, DateOnly stichtag)
    {
        Schreiben(b);
        var u = UnterrichtVon(s, unterricht);
        var g = GruppeVon(s, unterricht, gruppeId);
        var id = g.Id != 0 ? g.Id : StandardgruppeAnlegen(b, s, unterricht);
        var klassen = Klassen(u);
        foreach (var z in zeilen.DistinctBy(z => z.SchuelerId))
        {
            var x = SchuelerVon(s, z.SchuelerId);
            var von = OptionalesDatum(z.Von, "Von");
            var bis = OptionalesDatum(z.Bis, "Bis");
            if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(von, bis) > 0) throw new ArgumentException($"{x.Vorname} {x.Name}: „Von“ liegt nach „Bis“.");
            // Standardgruppe: Schüler*innen der Klassen ohne Abweichung brauchen keine Zeile
            var ohneAbweichung = !g.Gast && z.Auswahl && von.Length == 0 && bis.Length == 0 && klassen.Contains(Klassenzugehoerigkeit.KlasseAm(s, x, stichtag), Ci);
            if (ohneAbweichung || (g.Gast && !z.Auswahl))
                _db.Ausfuehren("DELETE FROM schuelergruppe_mitglieder WHERE gruppe_id = @g AND schueler_id = @s", Db.P("@g", id), Db.P("@s", z.SchuelerId));
            else
            {
                var p = new[] { Db.P("@g", id), Db.P("@s", z.SchuelerId), Db.P("@v", von), Db.P("@bi", bis), Db.P("@a", !z.Auswahl) };
                if (_db.Ausfuehren("UPDATE schuelergruppe_mitglieder SET von = @v, bis = @bi, ausgeschlossen = @a WHERE gruppe_id = @g AND schueler_id = @s", p) == 0)
                    _db.Ausfuehren("INSERT INTO schuelergruppe_mitglieder (gruppe_id, schueler_id, von, bis, ausgeschlossen) VALUES (@g, @s, @v, @bi, @a)", p);
            }
        }
        return Mitglieder(b, Neu(), unterricht, id, stichtag);
    }

    /// <summary>Gast-Schülergruppe umbenennen bzw. ihren Zeitraum ändern.</summary>
    public void GruppeAendern(Benutzer b, Stand s, int unterricht, int id, string? name, string? von, string? bis)
    {
        Schreiben(b);
        var g = GruppeVon(s, unterricht, id);
        if (!g.Gast) throw new ArgumentException("Die Standardgruppe wird über ihre Schüler*innen bearbeitet.");
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 100) throw new ArgumentException("Bitte einen Namen (höchstens 100 Zeichen) eingeben.");
        var v = DatumPruefen(von, "Von");
        var bi = DatumPruefen(bis, "Bis");
        if (bi < v) throw new ArgumentException("„Bis“ liegt vor „Von“.");
        _db.Ausfuehren("UPDATE schuelergruppen SET name = @n, von = @v, bis = @b WHERE id = @id", Db.P("@n", n), Db.P("@v", v.ToString("yyyy-MM-dd")), Db.P("@b", bi.ToString("yyyy-MM-dd")), Db.P("@id", id));
        Neu();
    }

    /// <summary>Schülergruppe löschen (Standardgruppe: alle Abweichungen zurücksetzen).</summary>
    public void GruppeLoeschen(Benutzer b, Stand s, int unterricht, int id)
    {
        Schreiben(b);
        var g = GruppeVon(s, unterricht, id);
        if (g.Id == 0) return;
        _db.Ausfuehren("DELETE FROM schuelergruppe_mitglieder WHERE gruppe_id = @id", Db.P("@id", g.Id));
        _db.Ausfuehren("DELETE FROM schuelergruppen WHERE id = @id", Db.P("@id", g.Id));
        Neu();
    }

    // ------------------------------------------------------------------------------------------ Befristeter Unterrichtswechsel

    /// <summary>Unterrichte der Zielklasse zur Auswahl im Assistenten (Periode am Beginn des Zeitraums).</summary>
    public object Zielunterrichte(Benutzer b, Stand s, string klasse, DateOnly von, DateOnly bis)
    {
        Lesen(b);
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        return _listen.Unterrichte(s, von, null, klasse).Concat(_listen.Unterrichte(s, bis, null, klasse)).GroupBy(z => z.Id).Select(g => g.Last())
            .OrderBy(z => z.Fach, De).ThenBy(z => z.Id)
            .Select(z => new { z.Id, z.Klassen, z.Fach, z.Lehrer, z.Wochenstunden, gruppen = s.Zuordnungen.Gruppen.GetValueOrDefault(z.Id)?.Count ?? 0 }).ToList();
    }

    /// <summary>
    /// Befristeter Unterrichtswechsel: die Schüler*innen nehmen im Zeitraum an den gewählten Unterrichten der Zielklasse teil. Je Unterricht
    /// entsteht eine Gast-Schülergruppe „(Name)“ genau für den Zeitraum (eine vorhandene mit demselben Zeitraum wird ergänzt).
    /// </summary>
    public object Unterrichtswechsel(Benutzer b, Stand s, string? vonText, string? bisText, List<int>? schuelerIds, string? zielklasse, List<int>? unterrichte)
    {
        Schreiben(b);
        var von = DatumPruefen(vonText, "Von");
        var bis = DatumPruefen(bisText, "Bis");
        if (bis < von) throw new ArgumentException("„Bis“ liegt vor „Von“.");
        if (bis.DayNumber - von.DayNumber > 366) throw new ArgumentException("Der Zeitraum darf höchstens ein Jahr umfassen.");
        var ids = (schuelerIds ?? new()).Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens eine Schülerin bzw. einen Schüler wählen.");
        foreach (var i in ids) SchuelerVon(s, i);
        if (!s.Klassen.TryGetValue((zielklasse ?? "").Trim(), out var ziel)) throw new ArgumentException("Bitte die Zielklasse wählen.");
        var liste = (unterrichte ?? new()).Distinct().ToList();
        if (liste.Count == 0) throw new ArgumentException("Bitte mindestens einen Unterricht wählen.");
        var v = von.ToString("yyyy-MM-dd");
        var bi = bis.ToString("yyyy-MM-dd");
        var res = new List<object>();
        foreach (var uid in liste)
        {
            var u = UnterrichtVon(s, uid);
            if (!Klassen(u).Contains(ziel.Kurz, Ci)) throw new ArgumentException($"Der Unterricht {uid} gehört nicht zur Klasse {ziel.Kurz}.");
            var g = (s.Zuordnungen.Gruppen.GetValueOrDefault(uid) ?? new()).FirstOrDefault(x => x.Gast && x.Von == v && x.Bis == bi);
            var gid = g?.Id ?? 0;
            var name = $"({Standardname(u)})";
            if (gid == 0)
            {
                _db.Ausfuehren("INSERT INTO schuelergruppen (unterricht, name, art, von, bis, erstellt, benutzer_id) VALUES (@u, @n, @a, @v, @b, @e, @bn)",
                    Db.P("@u", uid), Db.P("@n", name), Db.P("@a", Gast), Db.P("@v", v), Db.P("@b", bi), Db.P("@e", Jetzt), Db.P("@bn", b.Id));
                gid = Convert.ToInt32(_db.Wert("SELECT MAX(id) FROM schuelergruppen WHERE unterricht = @u AND art = @a", Db.P("@u", uid), Db.P("@a", Gast)));
            }
            foreach (var sid in ids)
                if (_db.Ausfuehren("UPDATE schuelergruppe_mitglieder SET von = '', bis = '', ausgeschlossen = 0 WHERE gruppe_id = @g AND schueler_id = @s", Db.P("@g", gid), Db.P("@s", sid)) == 0)
                    _db.Ausfuehren("INSERT INTO schuelergruppe_mitglieder (gruppe_id, schueler_id) VALUES (@g, @s)", Db.P("@g", gid), Db.P("@s", sid));
            res.Add(new { unterricht = uid, gruppe = gid, name = g?.Name ?? name });
        }
        Neu();
        return new { ok = true, von = v, bis = bi, gruppen = res };
    }
}
