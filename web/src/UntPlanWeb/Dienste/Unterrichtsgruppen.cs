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
public sealed record Gruppenmitglied(int SchuelerId, string Von, string Bis, bool Ausgeschlossen, string Weitere = "")
{
    /// <summary>
    /// Alle Zeiträume der Zugehörigkeit (Zuordnungshistorie, z. B. Gruppenwechsel zum Halbjahr): der erste in Von/Bis, weitere als
    /// „von|bis;von|bis“ in <see cref="Weitere"/>. Leere Liste = ohne zeitliche Begrenzung.
    /// </summary>
    public List<(string Von, string Bis)> Zeitraeume
    {
        get
        {
            var res = new List<(string, string)>();
            if (Von.Length > 0 || Bis.Length > 0) res.Add((Von, Bis));
            foreach (var t in Weitere.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var x = t.Split('|');
                if (x.Length == 2 && (x[0].Length > 0 || x[1].Length > 0)) res.Add((x[0], x[1]));
            }
            return res;
        }
    }

    /// <summary>Nimmt die Schülerin/der Schüler an diesem Tag teil (nicht ausgeschlossen und in einem der Zeiträume)?</summary>
    public bool Am(DateOnly d) => !Ausgeschlossen && (Zeitraeume is var z && (z.Count == 0 || z.Any(x => Klassenzugehoerigkeit.Im(x.Von, x.Bis, d))));
}

/// <summary>Schülergruppe eines Unterrichts: „standard“ (die Klassen des Unterrichts) oder „gast“ (befristeter Unterrichtswechsel).</summary>
public sealed record Schuelergruppe(int Id, int Unterricht, string Name, string Art, string Von, string Bis, List<Gruppenmitglied> Mitglieder)
{
    public bool Gast => Art == Unterrichtsgruppen.Gast;
    /// <summary>Standardgruppe (die Klassen des Unterrichts); sonst nehmen nur die eingetragenen Mitglieder teil (Gast- bzw. weitere Schülergruppe).</summary>
    public bool IstStandard => Art == Unterrichtsgruppen.Standard;

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
                M: new Gruppenmitglied(r.Zahl("schueler_id"), r.Text("von"), r.Text("bis"), r.Wahr("ausgeschlossen"), r.Text("zeitraeume"))))
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
        var standard = gruppen.FirstOrDefault(g => g.IstStandard);
        var zeilen = standard?.Mitglieder.ToDictionary(m => m.SchuelerId) ?? new Dictionary<int, Gruppenmitglied>();
        foreach (var x in basis.Where(x => !zeilen.ContainsKey(x.Id))) res[x.Id] = x;
        foreach (var g in gruppen.Where(g => g.Gilt(d)))
            foreach (var m in g.Mitglieder.Where(m => m.Am(d))) Hinzu(m.SchuelerId);
        return Sortiert(res.Values);
    }

    /// <summary>Gast-Schüler*innen der Stunde (aus einer Gast-Schülergruppe, z. B. befristeter Unterrichtswechsel).</summary>
    public static HashSet<int> Gaeste(Stand s, int? unterricht, DateOnly d) =>
        unterricht is int u && s.Zuordnungen.Gruppen.TryGetValue(u, out var gruppen)
            ? gruppen.Where(g => g.Gast && g.Gilt(d)).SelectMany(g => g.Mitglieder.Where(m => m.Am(d)).Select(m => m.SchuelerId)).ToHashSet()
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
public sealed record MitgliedEingabe(int SchuelerId, bool Auswahl, string? Von, string? Bis, List<ZeitraumEingabe>? Zeitraeume = null);

/// <summary>Zeitraum der Zugehörigkeit zu einer Schülergruppe (yyyy-MM-dd, leer = offen).</summary>
public sealed record ZeitraumEingabe(string? Von, string? Bis);

/// <summary>
/// Klassenwechsel ab Datum, Schülergruppen je Unterricht und befristeter Unterrichtswechsel (Unterricht → Unterricht Klassen).
/// Rechte: Klassenwechsel mit „Schülerverwaltung“; Unterricht Klassen lesen bzw. Schülergruppen bearbeiten mit „Unterricht Klassen und Schülergruppen“.
/// </summary>
public sealed partial class Unterrichtsgruppen
{
    public const string Standard = "standard", Gast = "gast", Weitere = "gruppe";

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
        // nur lesend freigegeben (Lehrkräfte): die eigenen Klassen bzw. Klassen des eigenen Unterrichts
        if (!Rechtedienst.Darf(b, Recht.UnterrichtKlassen)) Klassenbuchdienst.KlasseEinsehenVerlangen(b, s, klasse);
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
                    gruppe = u is null ? z.Kurs : gruppen.FirstOrDefault(g => g.IstStandard)?.Name is { Length: > 0 } n ? n : Standardname(u),
                    schueler = Klassenzugehoerigkeit.DerStunde(s, z.Id, z.Klassen, stichtag).Count,
                    gruppen = gruppen.Count + (gruppen.Any(g => g.IstStandard) ? 0 : 1),
                    gaeste = gruppen.Count(g => g.Gast),
                    geaendert = gruppen.Any(g => g.IstStandard && g.Mitglieder.Count > 0),
                };
            }).OrderBy(z => z.Fach, De).ThenBy(z => z.Id).ToList(),
        };
    }

    /// <summary>
    /// Darf der Benutzer die Schülergruppen dieses Unterrichts bearbeiten? Recht „Unterricht Klassen und Schülergruppen“, die Lehrkraft
    /// des Unterrichts oder die Klassenleitung einer Klasse des Unterrichts (Leitfaden Klassenleitung: Lehrkräfte bzw. Klassenleitungen
    /// ordnen Schüler*innen den Schülergruppen zu).
    /// </summary>
    public static bool DarfBearbeiten(Benutzer b, Stand s, WebUnterricht u) =>
        Rechtedienst.Darf(b, Recht.UnterrichtKlassen)
        || (b.LehrerKuerzel is string l && b.Rolle != Rollen.Admin && (u.Zeilen.Any(z => Ci.Equals(z.Lehrer, l)) || Klassen(u).Any(k => Klassenbuchdienst.IstKlassenleitung(b, s, k))));

    /// <summary>Lesen: Recht „Unterricht Klassen“ oder Lehrkraft/Klassenleitung des Unterrichts.</summary>
    private static void LesenFuer(Benutzer b, Stand s, WebUnterricht u)
    {
        if (!Rechtedienst.DarfLesen(b, Recht.UnterrichtKlassen) && !DarfBearbeiten(b, s, u)) throw new UnauthorizedAccessException("„Unterricht Klassen“ ist für Ihr Konto nicht freigegeben.");
    }

    private static void SchreibenFuer(Benutzer b, Stand s, WebUnterricht u)
    {
        if (!DarfBearbeiten(b, s, u)) throw new UnauthorizedAccessException("Schülergruppen bearbeiten darf Ihr Konto nicht.");
    }

    /// <summary>Schülergruppen eines Unterrichts (die Standardgruppe auch, wenn sie noch nicht gespeichert ist – Id 0).</summary>
    public object Gruppen(Benutzer b, Stand s, int unterricht, DateOnly stichtag)
    {
        var u = UnterrichtVon(s, unterricht);
        LesenFuer(b, s, u);
        var klassen = Klassen(u);
        var gespeichert = s.Zuordnungen.Gruppen.GetValueOrDefault(unterricht) ?? new();
        var liste = new List<Schuelergruppe>();
        if (!gespeichert.Any(g => g.IstStandard)) liste.Add(new Schuelergruppe(0, unterricht, Standardname(u), Standard, "", "", new()));
        liste.AddRange(gespeichert);
        var alle = Klassenzugehoerigkeit.DerStunde(s, unterricht, klassen, stichtag).Select(x => x.Id).ToHashSet();
        return new
        {
            unterricht, klassen, fach = string.Join(", ", u.Zeilen.Select(z => z.Fach).Where(f => f.Length > 0).Distinct(Ci)),
            lehrer = u.Zeilen.Select(z => z.Lehrer).Where(l => l.Length > 0).Distinct(Ci).ToList(), stichtag,
            schreiben = DarfBearbeiten(b, s, u),
            gruppen = liste.Select(g => new
            {
                g.Id, g.Name, g.Art, g.Von, g.Bis, gast = g.Gast, gilt = g.Gilt(stichtag),
                anzahl = !g.IstStandard ? g.Mitglieder.Count(m => m.Am(stichtag)) : Standardmitglieder(s, u, g, stichtag).Count(alle.Contains),
                abweichungen = !g.IstStandard ? 0 : g.Mitglieder.Count,
            }),
        };
    }

    /// <summary>Teilnehmende der Standardgruppe am Stichtag (ohne Gäste).</summary>
    private static IEnumerable<int> Standardmitglieder(Stand s, WebUnterricht u, Schuelergruppe g, DateOnly d)
    {
        var zeilen = g.Mitglieder.ToDictionary(m => m.SchuelerId);
        return Klassenzugehoerigkeit.Am(s, Klassen(u), d).Where(x => !zeilen.ContainsKey(x.Id)).Select(x => x.Id)
            .Concat(g.Mitglieder.Where(m => m.Am(d)).Select(m => m.SchuelerId)).Distinct();
    }

    private Schuelergruppe GruppeVon(Stand s, int unterricht, int id)
    {
        var u = UnterrichtVon(s, unterricht);
        var gespeichert = s.Zuordnungen.Gruppen.GetValueOrDefault(unterricht) ?? new();
        if (id == 0) return gespeichert.FirstOrDefault(g => g.IstStandard) ?? new Schuelergruppe(0, unterricht, Standardname(u), Standard, "", "", new());
        return gespeichert.FirstOrDefault(g => g.Id == id) ?? throw new ArgumentException("Unbekannte Schülergruppe.");
    }

    private static string Kuerzen(string t, int n) => t.Length <= n ? t : t[..n];

    /// <summary>Kurzname wie in der Schülerliste: Nachname (6 Zeichen) und Vorname (3 Zeichen), z. B. „BaskhaMed“.</summary>
    public static string Kurzname(string name, string vorname)
    {
        static string Rein(string t) => new string(t.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss")
            .Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetter).ToArray());
        return Kuerzen(Rein(name), 6) + Kuerzen(Rein(vorname), 3);
    }

    /// <summary>
    /// Schüler*innen in der Schülergruppe: Standardgruppe = alle Schüler*innen der Klassen am Stichtag und weitere eingetragene;
    /// weitere Gruppen = die Schüler*innen der Klassen des Unterrichts zur Auswahl; Gastgruppe = die eingetragenen Gäste. Je Zeile Auswahl
    /// (nimmt am Stichtag bzw. danach teil), aktueller Zeitraum (Von/Bis) und die Zuordnungshistorie (abgelaufene bzw. weitere Zeiträume).
    /// </summary>
    public object Mitglieder(Benutzer b, Stand s, int unterricht, int gruppeId, DateOnly stichtag)
    {
        var u = UnterrichtVon(s, unterricht);
        LesenFuer(b, s, u);
        var g = GruppeVon(s, unterricht, gruppeId);
        var alle = s.Schueler.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        var daten = _db.Liste("SELECT id, geschlecht, extern_id FROM schueler_daten", r => (Id: r.Zahl("id"), G: r.Text("geschlecht"), E: r.Text("extern_id"))).ToDictionary(x => x.Id);
        var zeilen = g.Mitglieder.ToDictionary(m => m.SchuelerId);
        var ids = (g.Gast ? new List<int>() : Klassenzugehoerigkeit.Am(s, Klassen(u), stichtag).Select(x => x.Id).ToList()).Concat(g.Mitglieder.Select(m => m.SchuelerId)).Distinct();
        var t = stichtag.ToString("yyyy-MM-dd");
        var liste = ids.Where(alle.ContainsKey).Select(id =>
        {
            var x = alle[id];
            zeilen.TryGetValue(id, out var m);
            var zr = m?.Zeitraeume ?? new();
            // aktueller Zeitraum: der am Stichtag gültige, sonst der nächste künftige; die übrigen bilden die Zuordnungshistorie
            var aktuell = zr.FirstOrDefault(z => Klassenzugehoerigkeit.Im(z.Von, z.Bis, stichtag));
            if (aktuell == default) aktuell = zr.Where(z => z.Von.Length > 0 && string.CompareOrdinal(z.Von, t) > 0).OrderBy(z => z.Von, StringComparer.Ordinal).FirstOrDefault();
            var historie = zr.Where(z => z != aktuell).OrderBy(z => z.Von, StringComparer.Ordinal).ToList();
            var auswahl = m is null ? g.IstStandard : !m.Ausgeschlossen && (zr.Count == 0 || aktuell != default);
            daten.TryGetValue(id, out var dz);
            return new
            {
                x.Id, x.Name, x.Vorname, klasse = Klassenzugehoerigkeit.KlasseAm(s, x, stichtag), geschlecht = dz.G ?? "",
                auswahl, von = aktuell.Von ?? "", bis = aktuell.Bis ?? "",
                historie = historie.Select(z => new { z.Von, z.Bis }).ToList(),
                zeitraeume = zr.Select(z => new { z.Von, z.Bis }).ToList(),
                kurzname = Kurzname(x.Name, x.Vorname), externeId = dz.E ?? "", katalognummer = 0,
                teilnahme = m is null ? g.IstStandard : m.Am(stichtag) && g.Gilt(stichtag),
            };
        }).OrderBy(x => x.Name, De).ThenBy(x => x.Vorname, De).ToList();
        return new
        {
            unterricht, gruppe = new { g.Id, g.Name, g.Art, g.Von, g.Bis, gast = g.Gast, standard = g.IstStandard }, klassen = Klassen(u),
            lehrer = u.Zeilen.Select(z => z.Lehrer).Where(l => l.Length > 0).Distinct(Ci).ToList(),
            fach = string.Join(", ", u.Zeilen.Select(z => z.Fach).Where(f => f.Length > 0).Distinct(Ci)), stichtag,
            schreiben = DarfBearbeiten(b, s, u), schueler = liste,
        };
    }

    private int StandardgruppeAnlegen(Benutzer b, Stand s, int unterricht)
    {
        var u = UnterrichtVon(s, unterricht);
        _db.Ausfuehren("INSERT INTO schuelergruppen (unterricht, name, art, erstellt, benutzer_id) VALUES (@u, @n, @a, @e, @b)",
            Db.P("@u", unterricht), Db.P("@n", Standardname(u)), Db.P("@a", Standard), Db.P("@e", Jetzt), Db.P("@b", b.Id));
        return Convert.ToInt32(_db.Wert("SELECT MAX(id) FROM schuelergruppen WHERE unterricht = @u AND art = @a", Db.P("@u", unterricht), Db.P("@a", Standard)));
    }

    /// <summary>Zeiträume prüfen, offene/leere entfernen, sortieren; ein Zeitraum ohne Grenzen hebt alle Begrenzungen auf (leere Liste).</summary>
    private static List<(string Von, string Bis)> ZeitraeumePruefen(WebSchueler x, IEnumerable<ZeitraumEingabe> eingabe)
    {
        var res = new List<(string Von, string Bis)>();
        foreach (var z in eingabe)
        {
            var von = OptionalesDatum(z.Von, "Von");
            var bis = OptionalesDatum(z.Bis, "Bis");
            if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(von, bis) > 0) throw new ArgumentException($"{x.Vorname} {x.Name}: „Von“ liegt nach „Bis“.");
            if (von.Length == 0 && bis.Length == 0) return new();
            res.Add((von, bis));
        }
        return res.Distinct().OrderBy(z => z.Von, StringComparer.Ordinal).ThenBy(z => z.Bis, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Auswahl und Zeiträume der Schülergruppe speichern (nur die übergebenen Zeilen). Mit <see cref="MitgliedEingabe.Zeitraeume"/>
    /// (Zuordnungshistorie, Matrix): ohne Häkchen und ohne Zeitraum = nicht in der Gruppe; mit Zeiträumen = nimmt genau in diesen teil.
    /// </summary>
    public object MitgliederSpeichern(Benutzer b, Stand s, int unterricht, int gruppeId, List<MitgliedEingabe> zeilen, DateOnly stichtag)
    {
        var u = UnterrichtVon(s, unterricht);
        SchreibenFuer(b, s, u);
        var g = GruppeVon(s, unterricht, gruppeId);
        var id = g.Id != 0 ? g.Id : StandardgruppeAnlegen(b, s, unterricht);
        foreach (var z in zeilen.DistinctBy(z => z.SchuelerId)) MitgliedSchreiben(s, u, g, id, z, stichtag);
        return Mitglieder(b, Neu(), unterricht, id, stichtag);
    }

    private void MitgliedSchreiben(Stand s, WebUnterricht u, Schuelergruppe g, int id, MitgliedEingabe z, DateOnly stichtag)
    {
        var x = SchuelerVon(s, z.SchuelerId);
        var inKlasse = Klassen(u).Contains(Klassenzugehoerigkeit.KlasseAm(s, x, stichtag), Ci);
        List<(string Von, string Bis)> zr;
        bool ausgeschlossen;
        if (z.Zeitraeume is null)
        {
            // einfacher Dialog: Häkchen und ein Zeitraum
            var von = OptionalesDatum(z.Von, "Von");
            var bis = OptionalesDatum(z.Bis, "Bis");
            if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(von, bis) > 0) throw new ArgumentException($"{x.Vorname} {x.Name}: „Von“ liegt nach „Bis“.");
            zr = von.Length > 0 || bis.Length > 0 ? new() { (von, bis) } : new();
            ausgeschlossen = !z.Auswahl;
        }
        else
        {
            zr = ZeitraeumePruefen(x, z.Zeitraeume);
            ausgeschlossen = !z.Auswahl && zr.Count == 0;
        }
        var loeschen = g.IstStandard ? (!ausgeschlossen && zr.Count == 0 && inKlasse) || (ausgeschlossen && !inKlasse) : ausgeschlossen;
        if (loeschen)
        {
            _db.Ausfuehren("DELETE FROM schuelergruppe_mitglieder WHERE gruppe_id = @g AND schueler_id = @s", Db.P("@g", id), Db.P("@s", z.SchuelerId));
            return;
        }
        var (v1, b1) = zr.Count > 0 ? zr[0] : ("", "");
        var weitere = string.Join(";", zr.Skip(1).Select(t => $"{t.Von}|{t.Bis}"));
        var p = new[] { Db.P("@g", id), Db.P("@s", z.SchuelerId), Db.P("@v", v1), Db.P("@bi", b1), Db.P("@a", ausgeschlossen), Db.P("@w", weitere) };
        if (_db.Ausfuehren("UPDATE schuelergruppe_mitglieder SET von = @v, bis = @bi, ausgeschlossen = @a, zeitraeume = @w WHERE gruppe_id = @g AND schueler_id = @s", p) == 0)
            _db.Ausfuehren("INSERT INTO schuelergruppe_mitglieder (gruppe_id, schueler_id, von, bis, ausgeschlossen, zeitraeume) VALUES (@g, @s, @v, @bi, @a, @w)", p);
    }

    /// <summary>Neue Schülergruppe für den Unterricht („Neu“): nur die ausgewählten Schüler*innen nehmen teil, optional befristet.</summary>
    public object GruppeAnlegen(Benutzer b, Stand s, int unterricht, string? name, string? von, string? bis)
    {
        var u = UnterrichtVon(s, unterricht);
        SchreibenFuer(b, s, u);
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 100) throw new ArgumentException("Bitte einen Namen (höchstens 100 Zeichen) eingeben.");
        var v = OptionalesDatum(von, "Von");
        var bi = OptionalesDatum(bis, "Bis");
        if (v.Length > 0 && bi.Length > 0 && string.CompareOrdinal(v, bi) > 0) throw new ArgumentException("„Bis“ liegt vor „Von“.");
        _db.Ausfuehren("INSERT INTO schuelergruppen (unterricht, name, art, von, bis, erstellt, benutzer_id) VALUES (@u, @n, @a, @v, @b, @e, @bn)",
            Db.P("@u", unterricht), Db.P("@n", n), Db.P("@a", Weitere), Db.P("@v", v), Db.P("@b", bi), Db.P("@e", Jetzt), Db.P("@bn", b.Id));
        var id = Convert.ToInt32(_db.Wert("SELECT MAX(id) FROM schuelergruppen WHERE unterricht = @u", Db.P("@u", unterricht)));
        Neu();
        return new { id, name = n, von = v, bis = bi };
    }

    /// <summary>Schülergruppe umbenennen bzw. ihren Zeitraum ändern (Standardgruppe: nur der Name).</summary>
    public void GruppeAendern(Benutzer b, Stand s, int unterricht, int id, string? name, string? von, string? bis)
    {
        var u = UnterrichtVon(s, unterricht);
        SchreibenFuer(b, s, u);
        var g = GruppeVon(s, unterricht, id);
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 100) throw new ArgumentException("Bitte einen Namen (höchstens 100 Zeichen) eingeben.");
        if (g.IstStandard)
        {
            var gid = g.Id != 0 ? g.Id : StandardgruppeAnlegen(b, s, unterricht);
            _db.Ausfuehren("UPDATE schuelergruppen SET name = @n WHERE id = @id", Db.P("@n", n), Db.P("@id", gid));
            Neu();
            return;
        }
        string v, bi;
        if (g.Gast)
        {
            var dv = DatumPruefen(von, "Von");
            var db = DatumPruefen(bis, "Bis");
            if (db < dv) throw new ArgumentException("„Bis“ liegt vor „Von“.");
            (v, bi) = (dv.ToString("yyyy-MM-dd"), db.ToString("yyyy-MM-dd"));
        }
        else
        {
            (v, bi) = (OptionalesDatum(von, "Von"), OptionalesDatum(bis, "Bis"));
            if (v.Length > 0 && bi.Length > 0 && string.CompareOrdinal(v, bi) > 0) throw new ArgumentException("„Bis“ liegt vor „Von“.");
        }
        _db.Ausfuehren("UPDATE schuelergruppen SET name = @n, von = @v, bis = @b WHERE id = @id", Db.P("@n", n), Db.P("@v", v), Db.P("@b", bi), Db.P("@id", id));
        Neu();
    }

    /// <summary>Schülergruppe löschen (Standardgruppe: alle Abweichungen zurücksetzen).</summary>
    public void GruppeLoeschen(Benutzer b, Stand s, int unterricht, int id)
    {
        SchreibenFuer(b, s, UnterrichtVon(s, unterricht));
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
        // nur lesend freigegeben (Lehrkräfte): die eigenen Klassen bzw. Klassen des eigenen Unterrichts
        if (!Rechtedienst.Darf(b, Recht.UnterrichtKlassen)) Klassenbuchdienst.KlasseEinsehenVerlangen(b, s, klasse);
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
