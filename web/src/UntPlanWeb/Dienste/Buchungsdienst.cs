using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Arten einer Buchung.</summary>
public static class Buchungsarten
{
    /// <summary>Raum buchen (Buchen | Raum buchen, Raumübersicht).</summary>
    public const string Raum = "raum";
    /// <summary>Mobile Ressource buchen (Buchen | Ressource buchen).</summary>
    public const string Ressource = "ressource";
    /// <summary>Aktivität (Buchen | Neue Aktivität): Klassen, Lehrkraft, Fach und Raum sind optional.</summary>
    public const string Aktivitaet = "aktivitaet";
    /// <summary>Zusätzliche Stunde eines in UntPlanWeb angelegten Unterrichts (Unterricht | Neuer Unterricht).</summary>
    public const string Unterricht = "unterricht";

    public static readonly string[] Alle = { Raum, Ressource, Aktivitaet, Unterricht };

    public static string Name(string art) => art switch
    {
        Raum => "Raumbuchung", Ressource => "Ressourcenbuchung", Aktivitaet => "Aktivität", Unterricht => "Zusätzlicher Unterricht", _ => art,
    };
}

/// <summary>Ressourcenart mit Symbol (Bootstrap-Icon-Name ohne „bi-“, leer = aus dem Namen abgeleitet).</summary>
public sealed record Ressourcenart(int Id, string Name, int Reihenfolge, string Symbol = "");

/// <summary>Ressource: fest einem Raum zugeordnet (Raum gesetzt, z. B. Internet) oder mobil (Raum leer, z. B. mobiler Projektor) mit Anzahl,
/// Standort (Ausgabe- bzw. Lagerort mobiler Geräte, z. B. „Medienraum“) und dem Symbol ihrer Art.</summary>
public sealed record Ressource(int Id, string Name, int? ArtId, string Art, string Raum, int Anzahl, string Bemerkung, bool Aktiv, string Standort = "", string Symbol = "")
{
    public bool Mobil => Raum.Length == 0;
}

/// <summary>Raum mit Stammdaten aus UntPlan (Raumart, Kapazität, buchbar) und den Ergänzungen aus UntPlanWeb (Gebäude, Abteilung, Raumgruppe, Ressourcen).</summary>
public sealed record Raumdaten(string Kurz, string Name, string Raumart, int Kapazitaet, string Gebaeude, string Abteilung, string Raumgruppe, bool Buchbar,
    List<string> Ressourcen, List<int> RessourcenartIds, int KapazitaetUntPlan)
{
    /// <summary>Nur reservieren: Buchungen müssen von der Raumadministration bestätigt werden (sonst sofort buchbar).</summary>
    public bool NurReservieren { get; init; }
}

public sealed record BuchungsRessource(int Id, string Name, int Menge);

/// <summary>Einzeltermin einer Buchung. Eine Serie (Periodizität) besteht aus mehreren Buchungen mit derselben Serien-ID.</summary>
public sealed record Buchung(int Id, string Serie, string Art, string Datum, int StundeVon, int StundeBis, string Beginn, string Ende, string Raum,
    List<string> Klassen, string Lehrer, string Fach, string Titel, string Text, int? UnterrichtId, bool Benachrichtigen, string Status, string? Uebertragen,
    int? BenutzerId, string Absender, string Erstellt, string? Storniert)
{
    public List<BuchungsRessource> Ressourcen { get; init; } = new();
    public string ArtName => Buchungsarten.Name(Art);
    /// <summary>Weitere Lehrkräfte (Terminsuche/Verabredung mit mehreren Lehrkräften bzw. zusätzliche Stunden eines Unterrichts mit mehreren Lehrkräften).</summary>
    public List<string> WeitereLehrer { get; init; } = new();
    /// <summary>Weitere Räume (Terminsuche/Verabredung mit mehreren Räumen).</summary>
    public List<string> WeitereRaeume { get; init; } = new();
    /// <summary>Zusatzangaben der Buchung (Abteilung, Verantwortliche*r, Art, Teilnehmer, Kostenstelle, Menge, E-Mail, Anmerkung).</summary>
    public BuchungsZusatz Zusatz { get; init; } = new();
    /// <summary>Bestätigt am (null = Reservierung, noch nicht von der Raumadministration bestätigt).</summary>
    public string? Bestaetigt { get; init; }
    public string BestaetigtVon { get; init; } = "";
    public string Geaendert { get; init; } = "";
    public string GeaendertVon { get; init; } = "";
    /// <summary>Schüler*innen der Schülergruppe der Buchung (Ids).</summary>
    public List<int> Schueler { get; init; } = new();
    /// <summary>Unterricht aus UntPlan, zu dem die zusätzliche Stunde gebucht wurde.</summary>
    public int? PlanUnterricht { get; init; }
    /// <summary>Alle Lehrkräfte (Lehrkraft und weitere).</summary>
    public List<string> AlleLehrer => (Lehrer.Length > 0 ? new[] { Lehrer } : Array.Empty<string>()).Concat(WeitereLehrer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    /// <summary>Alle Räume (Raum und weitere).</summary>
    public List<string> AlleRaeume => (Raum.Length > 0 ? new[] { Raum } : Array.Empty<string>()).Concat(WeitereRaeume).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public string StatusText => Status == "storniert" ? "Storniert" : Bestaetigt is null ? "Nicht bestätigt" : "Bestätigt";
}

/// <summary>Zusatzangaben einer Buchung wie im Formular „Neue Aktivität“ bzw. „Veranstaltung“ (Terminsuche).</summary>
public sealed record BuchungsZusatz(string Abteilung = "", string Verantwortlich = "", string Aktivitaetsart = "", int Teilnehmer = 0, string Kostenstelle = "",
    int Menge = 0, string Email = "", bool EmailBenachrichtigung = false, string Anmerkung = "")
{
    public BuchungsZusatz Bereinigt()
    {
        static string T(string? x, int n) { var t = (x ?? "").Trim(); return t.Length > n ? t[..n] : t; }
        var mail = T(Email, 200);
        if (mail.Length > 0 && (!mail.Contains('@') || mail.Contains(' '))) throw new ArgumentException("Bitte eine gültige E-Mail-Adresse eingeben.");
        return new(T(Abteilung, 100), T(Verantwortlich, 100), T(Aktivitaetsart, 60), Math.Clamp(Teilnehmer, 0, 99999), T(Kostenstelle, 100),
            Math.Clamp(Menge, 0, 99999), mail, EmailBenachrichtigung, T(Anmerkung, 1000));
    }
}

/// <summary>In UntPlanWeb angelegter Unterricht (Unterricht | Neuer Unterricht), dessen Stunden als „Zusätzliche Stunden“ gebucht werden.</summary>
public sealed record EigenerUnterricht(int Id, string Lehrer, string Fach, List<string> Klassen, string Gruppe, string Text, int Wochenstunden, int? BenutzerId,
    string Erstellt, int GebuchteStunden = 0)
{
    public UnterrichtZusatz Zusatz { get; init; } = new();
}

/// <summary>Zusatzangaben „Neuer Unterricht“: Zeitraum (Von – Bis), Jahresstunden, Abteilung, Verantwortliche*r, Art, Anmerkung, E-Mail.</summary>
public sealed record UnterrichtZusatz(string Von = "", string Bis = "", int Jahresstunden = 0, string Abteilung = "", string Verantwortlich = "", string Aktivitaetsart = "",
    string Anmerkung = "", string Email = "", bool EmailBenachrichtigung = false);

public sealed record Termin(DateOnly Datum, int StundeVon, int StundeBis);

/// <summary>Belegung eines Raums in einer Stunde (Unterricht, Klausur, Termin, Vormerkung, Sperre oder Buchung).</summary>
public sealed record Belegt(int Stunde, string Raum, string Art, string Text, int? BuchungId);

/// <summary>
/// Zeitbereich der Suche: einmalig (Datum) oder periodisch (Von–Bis, Wiederholung, Wochentage 0 = Montag) und Stunden
/// (StundeVon/StundeBis) bzw. Uhrzeit (Beginn/Ende, HH:mm – wird auf die überlappenden Stunden des Zeitrasters abgebildet).
/// Wiederholung: einmalig, taeglich, woechentlich, 2wochen, 3wochen, 4wochen.
/// </summary>
public sealed record Zeitangabe(string? Wiederholung, DateOnly? Datum, DateOnly? Von, DateOnly? Bis, List<int>? Wochentage, int? StundeVon, int? StundeBis,
    string? Beginn = null, string? Ende = null);

/// <summary>Auswahlkriterien: Ressourcenarten (Raum muss alle haben), Raumart, Kapazität, Abteilung, Gebäude, Raumgruppe, maximale Belegung in Prozent.</summary>
public sealed record Kriterien(List<int>? Ressourcenarten = null, string? Raumart = null, int? KapazitaetMin = null, int? KapazitaetMax = null,
    string? Abteilung = null, string? Gebaeude = null, string? Raumgruppe = null, int? MaxBelegung = null);

public sealed record TerminWahl(DateOnly Datum, int StundeVon, int StundeBis, string? Raum);
public sealed record RessourcenWahl(int Id, int? Menge);

/// <summary>Neue Buchung (Einzeltermin oder Serie). Je Termin kann ein anderer Raum gewählt werden.</summary>
public sealed record BuchungDaten(string Art, List<TerminWahl> Termine, string? Beginn = null, string? Ende = null, List<RessourcenWahl>? Ressourcen = null,
    List<string>? Klassen = null, string? Lehrer = null, string? Fach = null, string? Titel = null, string? Text = null, bool Benachrichtigen = false, int? UnterrichtId = null,
    List<string>? WeitereLehrer = null, List<string>? WeitereRaeume = null, BuchungsZusatz? Zusatz = null, bool? SofortBestaetigen = null,
    bool? LehrerKonflikteZulassen = null, bool? KlassenKonflikteZulassen = null, int? PlanUnterricht = null, int? QuellBuchung = null);

/// <summary>Details einer Buchung ändern (Buchen | Meine Buchungen | Bearbeiten) – gilt für den Termin bzw. alle kommenden Termine der Serie.</summary>
public sealed record BuchungAenderung(List<string>? Klassen = null, string? Lehrer = null, List<string>? WeitereLehrer = null, string? Fach = null, string? Titel = null,
    string? Text = null, BuchungsZusatz? Zusatz = null, bool Serie = true);

/// <summary>Buchungsbeschränkungen (Administration): Standarddauer von Verabredungen in Minuten und Zeitbereich, in dem Verabredungen angelegt werden können.</summary>
public sealed record Buchungsbeschraenkungen(int Dauer = 45, string Beginn = "07:00", string Ende = "18:00", bool NurUnterrichtstage = true);

/// <summary>
/// Buchungsmodul: Räume, Ressourcen, Aktivitäten und zusätzliche Stunden buchen – einmalig oder periodisch (Serie mit Serien-ID,
/// einzeln oder gesamt stornierbar). Die Konfliktprüfung berücksichtigt Unterricht laut Plan, Klausuren, Termine, Raumsperren,
/// Vormerkungen (Rückmeldungen) und andere Buchungen. Buchungen erscheinen in den Plänen als Status „buchung“ bzw. Aktivitäten als
/// „veranstaltung“ – nie als Vertretung, ohne Vertretungstext – und werden UntPlan als Rückmeldung „raumbuchung“ angeboten.
/// </summary>
public sealed class Buchungsdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Plandienst _plan;
    private readonly Rueckmeldungsdienst _rm;
    private readonly Mitteilungsdienst _mitteilungen;
    private readonly Benutzerdienst _benutzer;
    private readonly Raumsperrdienst? _sperren;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new CultureInfo("de-DE"), true);
    /// <summary>Prüfen und Speichern hintereinander (keine Doppelbuchung durch gleichzeitige Anfragen).</summary>
    private static readonly object Sperre = new();
    public const int MaxTermine = 200;
    /// <summary>Präfix der Rückmeldungs-Id einer Buchung (Schnittstelle zu UntPlan).</summary>
    public const string RueckmeldungsPraefix = "buchung-";

    public Buchungsdienst(Db db, Uhr uhr, Plandienst plan, Rueckmeldungsdienst rm, Mitteilungsdienst mitteilungen, Benutzerdienst benutzer, Raumsperrdienst? sperren = null)
    {
        _db = db; _uhr = uhr; _plan = plan; _rm = rm; _mitteilungen = mitteilungen; _benutzer = benutzer; _sperren = sperren;
    }

    /// <summary>Stunden des Zeitrasters, in denen eine Raumsperre (Administration) gilt – je Sperre.</summary>
    private IEnumerable<(Raumsperre Sperre, int Stunde)> SperrStunden(Stand s, DateOnly d, Func<Raumsperre, bool> welche)
    {
        if (_sperren is null) yield break;
        var sperren = _sperren.Am(d).Where(welche).ToList();
        if (sperren.Count == 0) yield break;
        foreach (var z in s.Paket.Zeitraster.Stunden.Where(z => z.Beginn.Length >= 5 && z.Ende.Length >= 5))
            foreach (var x in sperren.Where(x => Raumsperrdienst.Gilt(x, d, z.Beginn, z.Ende)))
                yield return (x, z.Nr);
    }

    /// <summary>Gesperrte Stunden einer Ressource am Tag (Raumsperren der Art „Ressource“ bzw. „Alle Ressourcen“).</summary>
    private HashSet<int> RessourceGesperrt(Stand? s, DateOnly d, int ressource) =>
        s is null ? new() : SperrStunden(s, d, x => Raumsperrdienst.BetrifftRessource(x, ressource)).Select(x => x.Stunde).ToHashSet();

    private static List<string> JsonListe(string json) => string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<List<string>>(json) ?? new();
    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");

    // ------------------------------------------------------------------------------------------ Ressourcenarten und Ressourcen

    public List<Ressourcenart> Arten() =>
        _db.Liste("SELECT * FROM ressourcenarten ORDER BY reihenfolge, name", r => new Ressourcenart(r.Zahl("id"), r.Text("name"), r.Zahl("reihenfolge"), r.Text("symbol")));

    /// <summary>Erlaubte Symbole für Ressourcenarten (Bootstrap Icons).</summary>
    public static readonly string[] Symbole =
    {
        "projector", "laptop", "tablet", "camera-video", "camera", "speaker", "mic", "headphones", "display", "easel", "pc-display",
        "printer", "router", "wifi", "controller", "badge-vr", "robot", "cpu", "lightbulb", "tools", "boxes", "box-seam", "cart3", "bag", "music-note-beamed", "globe2",
    };

    private static string SymbolPruefen(string? symbol)
    {
        var x = (symbol ?? "").Trim();
        if (x.StartsWith("bi-", StringComparison.Ordinal)) x = x[3..];
        return x.Length == 0 || Symbole.Contains(x) ? x : throw new ArgumentException("Unbekanntes Symbol.");
    }

    public Ressourcenart ArtSpeichern(int? id, string name, int? reihenfolge, string? symbol = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Bitte einen Namen eingeben.");
        if (name.Length > 100) throw new ArgumentException("Der Name ist zu lang.");
        if (Arten().Any(a => a.Id != id && Ci.Equals(a.Name, name))) throw new ArgumentException($"Die Ressourcenart „{name}“ gibt es bereits.");
        var sym = SymbolPruefen(symbol);
        if (id is int i)
        {
            if (symbol is null) sym = Arten().FirstOrDefault(a => a.Id == i)?.Symbol ?? "";
            if (_db.Ausfuehren("UPDATE ressourcenarten SET name = @n, reihenfolge = @r, symbol = @s WHERE id = @id", Db.P("@n", name), Db.P("@r", reihenfolge ?? 0), Db.P("@s", sym), Db.P("@id", i)) == 0)
                throw new ArgumentException("Unbekannte Ressourcenart.");
            return new Ressourcenart(i, name, reihenfolge ?? 0, sym);
        }
        var neu = _db.Einfuegen("INSERT INTO ressourcenarten (name, reihenfolge, symbol) VALUES (@n, @r, @s)", Db.P("@n", name), Db.P("@r", reihenfolge ?? 0), Db.P("@s", sym));
        return new Ressourcenart((int)neu, name, reihenfolge ?? 0, sym);
    }

    public void ArtLoeschen(int id)
    {
        _db.Ausfuehren("UPDATE ressourcen SET art_id = NULL WHERE art_id = @id", Db.P("@id", id));
        _db.Ausfuehren("DELETE FROM ressourcenarten WHERE id = @id", Db.P("@id", id));
    }

    private static Ressource RessourceLesen(DbDataReader r) =>
        new(r.Zahl("id"), r.Text("name"), r.ZahlOderNull("art_id"), r.Text("art"), r.Text("raum"), Math.Max(1, r.Zahl("anzahl")), r.Text("bemerkung"), r.Wahr("aktiv"),
            r.Text("standort"), r.Text("symbol"));

    public List<Ressource> Ressourcen(bool nurAktive = false) => _db.Liste($"""
        SELECT x.*, COALESCE(a.name, '') AS art, COALESCE(a.symbol, '') AS symbol FROM ressourcen x LEFT JOIN ressourcenarten a ON a.id = x.art_id
        {(nurAktive ? "WHERE x.aktiv = 1" : "")} ORDER BY x.name
        """, RessourceLesen);

    public Ressource RessourceSpeichern(Stand? s, int? id, string name, int? artId, string? raum, int? anzahl, string? bemerkung, bool aktiv, string? standort = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Bitte einen Namen eingeben.");
        if (name.Length > 100) throw new ArgumentException("Der Name ist zu lang.");
        raum = (raum ?? "").Trim();
        if (raum.Length > 0)
        {
            if (s is null || !s.Raeume.TryGetValue(raum, out var r)) throw new ArgumentException($"Unbekannter Raum {raum}.");
            raum = r.Kurz;
        }
        if (artId is int a && !Arten().Any(x => x.Id == a)) throw new ArgumentException("Unbekannte Ressourcenart.");
        var n = Math.Clamp(anzahl ?? 1, 1, 999);
        var bem = (bemerkung ?? "").Trim();
        if (bem.Length > 500) bem = bem[..500];
        // Standort nur bei mobilen Ressourcen (feste stehen in ihrem Raum); null = beim Ändern unverändert lassen
        var ort = (standort ?? (id is int alt ? Ressourcen().FirstOrDefault(x => x.Id == alt)?.Standort : null) ?? "").Trim();
        if (ort.Length > 100) ort = ort[..100];
        if (raum.Length > 0) ort = "";
        if (id is int i)
        {
            if (_db.Ausfuehren("UPDATE ressourcen SET name = @n, art_id = @a, raum = @r, anzahl = @z, bemerkung = @b, aktiv = @ak, standort = @o WHERE id = @id",
                    Db.P("@n", name), Db.P("@a", artId), Db.P("@r", raum), Db.P("@z", n), Db.P("@b", bem), Db.P("@ak", aktiv), Db.P("@o", ort), Db.P("@id", i)) == 0)
                throw new ArgumentException("Unbekannte Ressource.");
        }
        else
            id = (int)_db.Einfuegen("INSERT INTO ressourcen (name, art_id, raum, anzahl, bemerkung, aktiv, standort) VALUES (@n, @a, @r, @z, @b, @ak, @o)",
                Db.P("@n", name), Db.P("@a", artId), Db.P("@r", raum), Db.P("@z", n), Db.P("@b", bem), Db.P("@ak", aktiv), Db.P("@o", ort));
        return Ressourcen().First(x => x.Id == id);
    }

    /// <summary>Ressource löschen; bestehende Buchungen behalten ihren Termin (die Ressource fällt aus ihnen heraus).</summary>
    public void RessourceLoeschen(int id)
    {
        _db.Ausfuehren("DELETE FROM buchung_ressourcen WHERE ressource_id = @id", Db.P("@id", id));
        _db.Ausfuehren("DELETE FROM ressourcen WHERE id = @id", Db.P("@id", id));
    }

    /// <summary>
    /// Beispiel-Ressourcen (Demo bzw. Knopf „Beispiele anlegen“ in der Verwaltung): typische Ressourcenarten mit Symbol und mobile Geräte
    /// mit Anzahl und Standort, dazu feste Ausstattung in vorhandenen Fachräumen. Nur, solange noch keine Ressourcen angelegt sind.
    /// Ergebnis: Anzahl angelegter Ressourcen (0 = es gab schon welche).
    /// </summary>
    public int BeispieleAnlegen(Stand? s)
    {
        if (Ressourcen().Count > 0) return 0;
        var arten = Arten();
        int Art(string name, string symbol)
        {
            var a = arten.FirstOrDefault(x => Ci.Equals(x.Name, name)) ?? ArtSpeichern(null, name, (arten.Count + 1) * 10, symbol);
            if (!arten.Contains(a)) arten.Add(a);
            return a.Id;
        }
        var beamer = Art("Beamer", "projector");
        var laptop = Art("Laptopwagen", "laptop");
        var tablet = Art("Tablet-Koffer", "tablet");
        var kamera = Art("Dokumentenkamera", "camera-video");
        var audio = Art("Audio", "speaker");
        var vr = Art("VR-Brillen", "badge-vr");
        var tafel = Art("Interaktive Tafel", "display");
        var n = 0;
        void Neu(string name, int art, int anzahl, string standort, string bemerkung, string raum = "")
        {
            if (raum.Length > 0 && (s is null || !s.Raeume.ContainsKey(raum))) return;
            RessourceSpeichern(s, null, name, art, raum, anzahl, bemerkung, true, standort);
            n++;
        }
        Neu("Beamer mobil 1", beamer, 1, "Medienraum", "mit HDMI- und USB-C-Adapter");
        Neu("Beamer mobil 2", beamer, 1, "Medienraum", "mit HDMI- und USB-C-Adapter");
        Neu("Beamer mobil 3", beamer, 1, "Lehrerzimmer", "Kurzdistanz, mit Lautsprecher");
        Neu("Laptopwagen A", laptop, 1, "Medienraum", "16 Laptops, Ladeschrank");
        Neu("Laptopwagen B", laptop, 1, "Neubau, 1. OG", "16 Laptops, Ladeschrank");
        Neu("iPad-Koffer", tablet, 4, "Sekretariat", "je Koffer 15 iPads mit Stift");
        Neu("Dokumentenkamera", kamera, 3, "Medienraum", "");
        Neu("Lautsprecherbox", audio, 2, "Musikraum", "Bluetooth, mit Mikrofon");
        Neu("VR-Brillen-Set", vr, 1, "Informatik-Sammlung", "10 Brillen");
        Neu("Interaktive Tafel PH1", tafel, 1, "", "", "PH1");
        Neu("Interaktive Tafel EDV1", tafel, 1, "", "", "EDV1");
        return n;
    }

    /// <summary>Beispiel-Raumdaten (Demo): Gebäude und Abteilung aus Raumart bzw. Kürzel, solange noch keine Raumdaten gepflegt sind.</summary>
    public int BeispielRaumdatenAnlegen(Stand s)
    {
        if (Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM raum_daten")) > 0) return 0;
        var n = 0;
        foreach (var r in s.Paket.Elemente.Raeume.Where(r => r.Kurz.Length > 0).DistinctBy(r => r.Kurz, Ci))
        {
            var art = r.Raumart.ToLowerInvariant();
            var (geb, abt) = art.Contains("sport") ? ("Sporthalle", "Sport")
                : r.Kurz.StartsWith("EDV", StringComparison.OrdinalIgnoreCase) || art.Contains("edv") ? ("Neubau", "Informatik")
                : r.Kurz is "PH1" or "CH1" or "BI1" ? ("Neubau", "Naturwissenschaften")
                : r.Kurz is "KU1" or "MU1" ? ("Altbau", "Musik & Kunst")
                : ("Hauptgebäude", "Unter- und Mittelstufe");
            RaumdatenSpeichern(s, r.Kurz, 0, geb, abt, art.Contains("fach") ? "Fachräume" : "");
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------------------------------ Raum-Stammdaten

    /// <summary>Alle Räume aus UntPlan mit den Ergänzungen aus UntPlanWeb (Kapazität 0 = Wert aus UntPlan).</summary>
    public List<Raumdaten> Raeume(Stand s)
    {
        var extra = _db.Liste("SELECT * FROM raum_daten", r => (Raum: r.Text("raum"), Kap: r.Zahl("kapazitaet"), Geb: r.Text("gebaeude"), Abt: r.Text("abteilung"), Gruppe: r.Text("raumgruppe"),
                Res: r.Zahl("nur_reservieren") != 0))
            .GroupBy(x => x.Raum, Ci).ToDictionary(g => g.Key, g => g.First(), Ci);
        var fest = Ressourcen(true).Where(x => !x.Mobil).GroupBy(x => x.Raum, Ci).ToDictionary(g => g.Key, g => g.ToList(), Ci);
        var buchbar = Rueckmeldungsdienst.Buchbar(s).Select(r => r.Kurz).ToHashSet(Ci);
        return s.Paket.Elemente.Raeume.Where(r => r.Kurz.Length > 0).DistinctBy(r => r.Kurz, Ci).OrderBy(r => r.Kurz, De).Select(r =>
        {
            extra.TryGetValue(r.Kurz, out var x);
            var res = fest.TryGetValue(r.Kurz, out var l) ? l : new List<Ressource>();
            return new Raumdaten(r.Kurz, r.Name, r.Raumart, x.Kap > 0 ? x.Kap : r.Kapazitaet, x.Geb ?? "", x.Abt ?? "", x.Gruppe ?? "", buchbar.Contains(r.Kurz),
                res.Select(y => y.Name).ToList(), res.Where(y => y.ArtId.HasValue).Select(y => y.ArtId!.Value).Distinct().ToList(), r.Kapazitaet) { NurReservieren = x.Res };
        }).ToList();
    }

    public void RaumdatenSpeichern(Stand s, string raum, int? kapazitaet, string? gebaeude, string? abteilung, string? raumgruppe, bool? nurReservieren = null)
    {
        if (!s.Raeume.TryGetValue(raum ?? "", out var r)) throw new ArgumentException($"Unbekannter Raum {raum}.");
        static string T(string? x) { var t = (x ?? "").Trim(); return t.Length > 100 ? t[..100] : t; }
        // null = unverändert lassen
        var res = nurReservieren ?? NurReservieren().Contains(r.Kurz);
        _db.Ausfuehren("DELETE FROM raum_daten WHERE raum = @r", Db.P("@r", r.Kurz));
        _db.Ausfuehren("INSERT INTO raum_daten (raum, kapazitaet, gebaeude, abteilung, raumgruppe, geaendert, nur_reservieren) VALUES (@r, @k, @g, @a, @rg, @z, @nr)",
            Db.P("@r", r.Kurz), Db.P("@k", Math.Clamp(kapazitaet ?? 0, 0, 9999)), Db.P("@g", T(gebaeude)), Db.P("@a", T(abteilung)), Db.P("@rg", T(raumgruppe)), Db.P("@z", Jetzt),
            Db.P("@nr", res));
    }

    /// <summary>Räume, die nur reserviert werden können (Bestätigung durch die Raumadministration).</summary>
    public HashSet<string> NurReservieren() =>
        _db.Liste("SELECT raum FROM raum_daten WHERE nur_reservieren = 1", r => r.Text("raum")).ToHashSet(Ci);

    private static bool Passt(Raumdaten r, Kriterien k) =>
        (k.Ressourcenarten is not { Count: > 0 } || k.Ressourcenarten.All(r.RessourcenartIds.Contains))
        && (string.IsNullOrWhiteSpace(k.Raumart) || Ci.Equals(r.Raumart, k.Raumart.Trim()))
        && (k.KapazitaetMin is not > 0 || r.Kapazitaet >= k.KapazitaetMin)
        && (k.KapazitaetMax is not > 0 || r.Kapazitaet <= k.KapazitaetMax)
        && (string.IsNullOrWhiteSpace(k.Abteilung) || Ci.Equals(r.Abteilung, k.Abteilung.Trim()))
        && (string.IsNullOrWhiteSpace(k.Gebaeude) || Ci.Equals(r.Gebaeude, k.Gebaeude.Trim()))
        && (string.IsNullOrWhiteSpace(k.Raumgruppe) || Ci.Equals(r.Raumgruppe, k.Raumgruppe.Trim()));

    // ------------------------------------------------------------------------------------------ Termine (Periodizität)

    /// <summary>Stunden aus Stundenangabe bzw. Uhrzeit (überlappende Stunden des Zeitrasters); dazu Beginn und Ende zur Anzeige.</summary>
    public static (int Von, int Bis, string Beginn, string Ende) Stunden(Stand s, int? stundeVon, int? stundeBis, string? beginn, string? ende)
    {
        var raster = s.Paket.Zeitraster.Stunden.Where(z => z.Nr > 0).OrderBy(z => z.Nr).ToList();
        var max = raster.Select(z => z.Nr).DefaultIfEmpty(12).Max();
        if (!string.IsNullOrWhiteSpace(beginn) && !string.IsNullOrWhiteSpace(ende))
        {
            if (!TimeOnly.TryParseExact(beginn.Trim(), "HH:mm", out var b) || !TimeOnly.TryParseExact(ende.Trim(), "HH:mm", out var e)) throw new ArgumentException("Uhrzeit bitte als HH:mm angeben.");
            if (e <= b) throw new ArgumentException("Die Endzeit liegt vor der Beginnzeit.");
            var treffer = raster.Where(z => TimeOnly.TryParseExact(z.Beginn, "HH:mm", out var zb) && TimeOnly.TryParseExact(z.Ende, "HH:mm", out var ze) && zb < e && ze > b).ToList();
            // Verabredungen (Terminsuche) dürfen außerhalb des Zeitrasters liegen: dann gilt die nächstgelegene Stunde
            if (treffer.Count == 0)
                treffer = raster.Where(z => Min(z.Beginn) >= 0 && Min(z.Ende) >= 0)
                    .OrderBy(z => Math.Min(Math.Abs(Min(z.Beginn) - (e.Hour * 60 + e.Minute)), Math.Abs(Min(z.Ende) - (b.Hour * 60 + b.Minute)))).Take(1).ToList();
            if (treffer.Count == 0) throw new ArgumentException("Die Uhrzeit liegt außerhalb des Zeitrasters der Schule.");
            return (treffer.First().Nr, treffer.Last().Nr, b.ToString("HH:mm"), e.ToString("HH:mm"));
        }
        var v = Math.Clamp(stundeVon ?? 1, 1, max);
        var bi = Math.Clamp(stundeBis ?? v, v, max);
        return (v, bi, raster.FirstOrDefault(z => z.Nr == v)?.Beginn ?? "", raster.FirstOrDefault(z => z.Nr == bi)?.Ende ?? "");
    }

    /// <summary>Einzeltermine eines Zeitbereichs; unterrichtsfreie Tage (Wochenende, Ferien) werden übersprungen.</summary>
    public (List<Termin> Termine, List<DateOnly> Uebersprungen) Termine(Stand s, Zeitangabe z)
    {
        var (v, b, _, _) = Stunden(s, z.StundeVon, z.StundeBis, z.Beginn, z.Ende);
        var wdh = (z.Wiederholung ?? "einmalig").Trim().ToLowerInvariant();
        var tage = new List<DateOnly>();
        if (wdh is "" or "einmalig")
            tage.Add(z.Datum ?? z.Von ?? throw new ArgumentException("Bitte ein Datum angeben."));
        else
        {
            var intervall = wdh switch
            {
                "taeglich" or "woechentlich" => 1, "2wochen" or "14taeglich" => 2, "3wochen" => 3, "4wochen" => 4,
                _ => throw new ArgumentException("Unbekannte Wiederholung."),
            };
            var von = z.Von ?? z.Datum ?? throw new ArgumentException("Bitte den Beginn des Zeitraums angeben.");
            var bis = z.Bis ?? throw new ArgumentException("Bitte das Ende des Zeitraums angeben.");
            if (bis < von) throw new ArgumentException("Das Ende des Zeitraums liegt vor dem Beginn.");
            if (bis > von.AddDays(380)) throw new ArgumentException("Der Zeitraum darf höchstens ein Schuljahr umfassen.");
            var wochentage = wdh == "taeglich" ? Enumerable.Range(0, 7).ToHashSet() : (z.Wochentage ?? new()).Where(t => t is >= 0 and <= 6).ToHashSet();
            if (wochentage.Count == 0) wochentage.Add(Plandienst.TagIndex(von));
            var start = Plandienst.Montag(von);
            for (var d = von; d <= bis; d = d.AddDays(1))
                if ((Plandienst.Montag(d).DayNumber - start.DayNumber) / 7 % intervall == 0 && wochentage.Contains(Plandienst.TagIndex(d))) tage.Add(d);
        }
        var res = new List<Termin>();
        var weg = new List<DateOnly>();
        foreach (var d in tage.Distinct().Order())
            if (_plan.Frei(s, d).Frei) weg.Add(d); else res.Add(new Termin(d, v, b));
        if (res.Count > MaxTermine) throw new ArgumentException($"Höchstens {MaxTermine} Termine auf einmal möglich. Bitte verkleinern Sie den Zeitraum.");
        return (res, weg);
    }

    // ------------------------------------------------------------------------------------------ Belegung

    /// <summary>Belegung aller Räume an einem Tag: Unterricht laut Plan, Klausuren, Termine mit Raum, Vormerkungen, Raumsperren und Buchungen.
    /// ohneUnterricht: Stunden und Vormerkungen dieses Unterrichts zählen nicht (eigene Stunde bei der Raumwahl einer Prüfung).</summary>
    public List<Belegt> RaumBelegung(Stand s, DateOnly d, int? ohneBuchung = null, int? ohneUnterricht = null)
    {
        var res = new List<Belegt>();
        var paket = _plan.Rohstunden(s, d);
        // vorgemerkte Raumänderungen eines Unterrichts geben dessen bisherigen Raum frei
        foreach (var r in Plandienst.MitRaumaenderungen(paket, _rm.Vorgemerkt(d, s, mitBuchungen: false)))
        {
            if (r.Status == "entfall" || r.Art == "KlausurLoeschen") continue;
            if (ohneUnterricht is int ou && r.Unterricht == ou) continue;
            var art = Plandienst.IstKlausur(r.Art) ? "klausur" : r.Status == "vorgemerkt" ? "vorgemerkt" : r.Unterricht.HasValue ? "unterricht"
                : Plandienst.IstBuchung(r.Art) ? "buchung" : "termin";
            var text = r.Unterricht.HasValue && r.Fach.Length > 0
                ? string.Join(" ", new[] { r.Fach, string.Join(",", r.Klassen), string.Join(",", r.Lehrer) }.Where(x => x.Length > 0))
                : r.Text.Length > 0 ? r.Text : r.Art.Length > 0 ? r.Art : "belegt";
            foreach (var raum in r.Raeume.SelectMany(Plandienst.RaumListe).Where(x => x.Length > 0)) res.Add(new Belegt(r.Stunde, raum, art, text, null));
        }
        // Termine aus UntPlan mit Raum (auch außerhalb der tagesaktuell übertragenen Tage)
        foreach (var t in s.Paket.Termine.Where(t => t.Raeume.Count > 0 && !Ci.Equals(t.Art, "klausur") && DateOnly.TryParse(t.Von, out var tv) && d >= tv
                                                      && DateOnly.TryParse(string.IsNullOrEmpty(t.Bis) ? t.Von : t.Bis, out var tb) && d <= tb))
            for (var st = Math.Max(1, t.StundeVon); st <= Math.Max(t.StundeVon, t.StundeBis); st++)
                foreach (var raum in t.Raeume) res.Add(new Belegt(st, raum, "termin", t.Titel, null));
        // Raumsperren (Absenzen der Art „raum“)
        var max = s.Paket.Zeitraster.Stunden.Select(z => z.Nr).DefaultIfEmpty(12).Max();
        foreach (var a in s.Paket.Absenzen.Where(a => a.Art == "raum" && DateOnly.TryParse(a.Von, out var av) && DateOnly.TryParse(a.Bis, out var ab) && d >= av && d <= ab))
            for (var st = Math.Max(1, a.StundeVon); st <= (a.StundeBis < a.StundeVon ? max : a.StundeBis); st++)
                res.Add(new Belegt(st, a.Element, "gesperrt", string.IsNullOrWhiteSpace(a.Grund) ? "gesperrt" : a.Grund!, null));
        // Raumsperren der Administration (einzelner Raum, Raumgruppe, alle Räume)
        var gesperrt = SperrStunden(s, d, x => x.FuerRaeume).ToList();
        if (gesperrt.Count > 0)
            foreach (var r in Raeume(s))
                foreach (var (x, st) in gesperrt.Where(g => Raumsperrdienst.BetrifftRaum(g.Sperre, r.Kurz, r.Raumgruppe)))
                    res.Add(new Belegt(st, r.Kurz, "gesperrt", x.Text.Length > 0 ? $"Gesperrt: {x.Text}" : "Gesperrt", null));
        // Buchungen aus UntPlanWeb (nach der Übernahme in UntPlan stehen sie bereits im Paket)
        foreach (var b in AktiveAm(d).Where(b => b.Id != ohneBuchung && b.Raum.Length > 0 && !SchonImPaket(b, paket)))
            for (var st = b.StundeVon; st <= b.StundeBis; st++)
                foreach (var raum in b.AlleRaeume)
                    res.Add(new Belegt(st, raum, "buchung", $"{(b.Titel.Length > 0 ? b.Titel : b.ArtName)} ({b.Absender}){(b.Bestaetigt is null ? ", nicht bestätigt" : "")}", b.Id));
        return res;
    }

    /// <summary>
    /// Raumwahl (z. B. Prüfung eintragen): alle Räume mit Kapazität, buchbar und frei/belegt im Zeitraum laut <see cref="RaumBelegung"/>
    /// (Plan, Prüfungen, Termine, Vormerkungen, Sperren, Buchungen). Mit Unterricht zählt dessen eigene Belegung nicht, seine Räume laut
    /// Plan stehen als „eigen“ vorn. Reihenfolge: eigene Räume, buchbare vor nicht buchbaren, darin freie vor belegten, dann Kürzel.
    /// </summary>
    public List<object> RaumStatus(Stand s, DateOnly d, int von, int bis, int? unterricht = null)
    {
        if (bis < von) (von, bis) = (bis, von);
        var belegung = RaumBelegung(s, d, ohneUnterricht: unterricht);
        var eigen = unterricht is int u ? _plan.UrspruenglicheRaeume(s, d, u, von, bis).ToHashSet(Ci) : new HashSet<string>(Ci);
        var buchbar = Rueckmeldungsdienst.Buchbar(s).Select(r => r.Kurz).ToHashSet(Ci);
        return Raeume(s).Where(r => r.Kurz.Length > 0).Select(r =>
            {
                var b = belegung.Where(x => x.Stunde >= von && x.Stunde <= bis && Ci.Equals(x.Raum, r.Kurz)).ToList();
                return new
                {
                    kurz = r.Kurz, name = r.Name, raumart = r.Raumart, kapazitaet = r.Kapazitaet, gebaeude = r.Gebaeude, abteilung = r.Abteilung, buchbar = buchbar.Contains(r.Kurz),
                    eigen = eigen.Contains(r.Kurz), frei = b.Count == 0,
                    belegt = b.OrderBy(x => x.Stunde).Select(x => new { stunde = x.Stunde, art = x.Art, text = x.Text }).DistinctBy(x => (x.art, x.text)).Take(3).ToList(),
                };
            })
            .OrderByDescending(x => x.eigen).ThenByDescending(x => x.buchbar).ThenByDescending(x => x.frei).ThenBy(x => x.kurz, De)
            .Cast<object>().ToList();
    }

    private static bool Belegt(List<Belegt> belegung, string raum, int von, int bis) =>
        belegung.Any(x => x.Stunde >= von && x.Stunde <= bis && Ci.Equals(x.Raum, raum));

    /// <summary>Gebuchte Menge je mobiler Ressource und Stunde an einem Tag.</summary>
    private Dictionary<(int Ressource, int Stunde), int> RessourcenGebucht(DateOnly d, int? ohneBuchung = null, Stand? s = null)
    {
        var res = new Dictionary<(int, int), int>();
        // gesperrte Ressourcen (Raumsperren): in diesen Stunden nicht verfügbar
        if (s != null)
            foreach (var (x, st) in SperrStunden(s, d, x => x.Art is Raumsperrdienst.Ressource or Raumsperrdienst.AlleRessourcen))
                foreach (var r in Ressourcen(true).Where(r => Raumsperrdienst.BetrifftRessource(x, r.Id)))
                    res[(r.Id, st)] = 1_000_000;
        foreach (var b in AktiveAm(d).Where(b => b.Id != ohneBuchung))
            foreach (var r in b.Ressourcen)
                for (var st = b.StundeVon; st <= b.StundeBis; st++)
                    res[(r.Id, st)] = res.GetValueOrDefault((r.Id, st)) + r.Menge;
        return res;
    }

    /// <summary>Noch verfügbare Anzahl einer mobilen Ressource in einem Zeitraum (kleinster Wert über die Stunden).</summary>
    private static int Verfuegbar(Ressource r, Dictionary<(int, int), int> gebucht, int von, int bis) =>
        Enumerable.Range(von, bis - von + 1).Select(st => r.Anzahl - gebucht.GetValueOrDefault((r.Id, st))).DefaultIfEmpty(r.Anzahl).Min();

    // ------------------------------------------------------------------------------------------ Suche

    private static void Kollegium(Benutzer b)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Buchen können nur Lehrkräfte, Schulleitung und Administration.");
    }

    private static object TerminJson(Termin t, Stand s) => new
    {
        datum = Plandienst.Iso(t.Datum), stundeVon = t.StundeVon, stundeBis = t.StundeBis, wochentag = Plandienst.Wochentage[Plandienst.TagIndex(t.Datum)],
        beginn = s.Paket.Zeitraster.Stunden.FirstOrDefault(z => z.Nr == t.StundeVon)?.Beginn ?? "", ende = s.Paket.Zeitraster.Stunden.FirstOrDefault(z => z.Nr == t.StundeBis)?.Ende ?? "",
    };

    /// <summary>
    /// Freie Räume für einen Zeitbereich. Ohne maximale Belegung nur Räume, die an allen Terminen frei sind; mit maximaler Belegung x %
    /// auch Räume, die an höchstens x % der Termine belegt sind. Je Termin werden alle freien (passenden) Räume mitgeliefert, damit
    /// für belegte Termine ein anderer Raum gewählt werden kann.
    /// </summary>
    public object RaumSuche(Stand s, Benutzer b, Zeitangabe zeit, Kriterien? kriterien)
    {
        Kollegium(b);
        if (!Rechtedienst.Darf(b, Recht.BuchungRaum)) throw new UnauthorizedAccessException("Ihr Konto darf keine Räume buchen.");
        var (termine, weg) = Termine(s, zeit);
        if (termine.Count == 0) throw new ArgumentException("Im gewählten Zeitraum gibt es keinen Unterrichtstag.");
        var k = kriterien ?? new Kriterien();
        var kandidaten = Raeume(s).Where(r => r.Buchbar && Passt(r, k)).ToList();
        var cache = new Dictionary<DateOnly, List<Belegt>>();
        var belegung = termine.Select(t => cache.TryGetValue(t.Datum, out var x) ? x : cache[t.Datum] = RaumBelegung(s, t.Datum)).ToList();
        bool Frei(string raum, int i) => !Belegt(belegung[i], raum, termine[i].StundeVon, termine[i].StundeBis);
        var max = Math.Clamp(k.MaxBelegung ?? 0, 0, 100);
        var raeume = kandidaten.Select(r => (Raum: r, Belegt: Enumerable.Range(0, termine.Count).Where(i => !Frei(r.Kurz, i)).ToList()))
            .Where(x => x.Belegt.Count * 100 <= max * termine.Count)
            .OrderBy(x => x.Belegt.Count).ThenBy(x => x.Raum.Kurz, De)
            .Select(x => new
            {
                kurz = x.Raum.Kurz, name = x.Raum.Name, raumart = x.Raum.Raumart, kapazitaet = x.Raum.Kapazitaet, gebaeude = x.Raum.Gebaeude, abteilung = x.Raum.Abteilung,
                raumgruppe = x.Raum.Raumgruppe, ressourcen = x.Raum.Ressourcen, belegt = x.Belegt, prozent = (int)Math.Round(x.Belegt.Count * 100.0 / termine.Count),
            }).ToList();
        return new
        {
            termine = termine.Select((t, i) => new { termin = TerminJson(t, s), freieRaeume = kandidaten.Where(r => Frei(r.Kurz, i)).Select(r => r.Kurz).ToList() }),
            raeume, uebersprungen = weg.Select(Plandienst.Iso), kandidaten = kandidaten.Count,
        };
    }

    /// <summary>Freie mobile Ressourcen für einen Zeitbereich (Filter: Ressourcenarten, maximale Belegung).</summary>
    public object RessourcenSuche(Stand s, Benutzer b, Zeitangabe zeit, Kriterien? kriterien)
    {
        Kollegium(b);
        if (!Rechtedienst.Darf(b, Recht.BuchungRessourcen)) throw new UnauthorizedAccessException("Ihr Konto darf keine Ressourcen buchen.");
        var (termine, weg) = Termine(s, zeit);
        if (termine.Count == 0) throw new ArgumentException("Im gewählten Zeitraum gibt es keinen Unterrichtstag.");
        var k = kriterien ?? new Kriterien();
        var kandidaten = Ressourcen(true).Where(r => r.Mobil && (k.Ressourcenarten is not { Count: > 0 } || (r.ArtId is int a && k.Ressourcenarten.Contains(a)))).ToList();
        var cache = new Dictionary<DateOnly, Dictionary<(int, int), int>>();
        var gebucht = termine.Select(t => cache.TryGetValue(t.Datum, out var x) ? x : cache[t.Datum] = RessourcenGebucht(t.Datum, null, s)).ToList();
        var max = Math.Clamp(k.MaxBelegung ?? 0, 0, 100);
        var liste = kandidaten.Select(r =>
            {
                var verfuegbar = termine.Select((t, i) => Verfuegbar(r, gebucht[i], t.StundeVon, t.StundeBis)).ToList();
                return (R: r, Verfuegbar: verfuegbar, Belegt: verfuegbar.Select((n, i) => (n, i)).Where(x => x.n < 1).Select(x => x.i).ToList());
            })
            .Where(x => x.Belegt.Count * 100 <= max * termine.Count)
            .OrderBy(x => x.Belegt.Count).ThenBy(x => x.R.Art, De).ThenBy(x => x.R.Name, De)
            .Select(x => new
            {
                id = x.R.Id, name = x.R.Name, art = x.R.Art, artId = x.R.ArtId, anzahl = x.R.Anzahl, bemerkung = x.R.Bemerkung, standort = x.R.Standort, symbol = x.R.Symbol,
                verfuegbar = x.Verfuegbar, frei = x.Verfuegbar.DefaultIfEmpty(0).Min(), belegt = x.Belegt, prozent = (int)Math.Round(x.Belegt.Count * 100.0 / termine.Count),
            }).ToList();
        return new { termine = termine.Select(t => TerminJson(t, s)), ressourcen = liste, uebersprungen = weg.Select(Plandienst.Iso) };
    }

    /// <summary>
    /// Hinweise vor dem Speichern (kein Hindernis): Lehrkraft bzw. Klassen haben zur selben Zeit schon Unterricht oder eine andere Buchung.
    /// </summary>
    public List<string> Hinweise(Stand s, IEnumerable<TerminWahl> termine, string? lehrer, IEnumerable<string>? klassen, IEnumerable<string>? weitereLehrer = null)
    {
        var kl = (klassen ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        var ll = new[] { lehrer ?? "" }.Concat(weitereLehrer ?? Enumerable.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(Ci).ToList();
        // je Element die Termine mit Überschneidung (zusammengefasst, damit eine Serie nicht viele gleiche Zeilen erzeugt)
        var treffer = new Dictionary<string, List<DateOnly>>();
        void Merken(string name, DateOnly d) => (treffer.TryGetValue(name, out var x) ? x : treffer[name] = new()).Add(d);
        foreach (var t in termine.Take(MaxTermine))
        {
            var roh = _plan.Rohstunden(s, t.Datum).Where(r => r.Status != "entfall" && r.Stunde >= t.StundeVon && r.Stunde <= t.StundeBis).ToList();
            var buchungen = AktiveAm(t.Datum).Where(b => b.StundeVon <= t.StundeBis && b.StundeBis >= t.StundeVon).ToList();
            foreach (var l in ll.Where(l => roh.Any(r => r.Lehrer.Contains(l, Ci)) || buchungen.Any(b => b.AlleLehrer.Contains(l, Ci)))) Merken(l, t.Datum);
            foreach (var k in kl.Where(k => roh.Any(r => r.Klassen.Contains(k, Ci)) || buchungen.Any(b => b.Klassen.Contains(k, Ci)))) Merken($"Klasse {k}", t.Datum);
        }
        string Tag(DateOnly d) => $"{Plandienst.Wochentage[Plandienst.TagIndex(d)][..2]} {d:dd.MM.}";
        return treffer.Select(x => (x.Value.Count == 1 ? $"{x.Key} hat am {Tag(x.Value[0])}" : $"{x.Key} hat an {x.Value.Count} Terminen ({string.Join(", ", x.Value.Take(4).Select(Tag))}{(x.Value.Count > 4 ? ", …" : "")})")
            + " zur gleichen Zeit schon Unterricht bzw. eine andere Aktivität.").ToList();
    }

    // ------------------------------------------------------------------------------------------ Speichern und Stornieren

    /// <summary>
    /// Buchung speichern: je Termin werden Raum (frei, buchbar) und Ressourcen (verfügbare Anzahl) geprüft; ist ein Termin belegt,
    /// wird nichts gespeichert. Mehrere Termine bilden eine Serie (gemeinsame Serien-ID). Aktivität: Lehrkraft optional benachrichtigen.
    /// </summary>
    public (List<Buchung> Buchungen, List<string> Hinweise) Speichern(Stand s, Benutzer b, BuchungDaten d)
    {
        Kollegium(b);
        // Buchen → Buchungen ent-/sperren: keine neuen Buchungen (die Administration bucht weiterhin)
        if (_sperren?.BuchungenGesperrt == true && !Rechtedienst.Darf(b, Recht.BuchungVerwalten))
            throw new ArgumentException("Buchungen sind derzeit gesperrt.");
        if (!Rechtedienst.Darf(b, Recht.BuchungRaum) && (d.Termine ?? new()).Any(t => !string.IsNullOrWhiteSpace(t.Raum)))
            throw new UnauthorizedAccessException("Ihr Konto darf keine Räume buchen.");
        if (!Rechtedienst.Darf(b, Recht.BuchungRessourcen) && d.Ressourcen is { Count: > 0 }) throw new UnauthorizedAccessException("Ihr Konto darf keine Ressourcen buchen.");
        var art = (d.Art ?? "").Trim().ToLowerInvariant();
        if (!Buchungsarten.Alle.Contains(art)) throw new ArgumentException("Unbekannte Buchungsart.");
        var eingabe = d.Termine ?? new();
        if (eingabe.Count == 0) throw new ArgumentException("Bitte mindestens einen Termin wählen.");
        if (eingabe.Count > MaxTermine) throw new ArgumentException($"Höchstens {MaxTermine} Termine auf einmal.");
        var raster = s.Paket.Zeitraster.Stunden;
        var maxStunde = raster.Select(z => z.Nr).DefaultIfEmpty(12).Max();
        var termine = eingabe.Select(t =>
        {
            var v = Math.Clamp(t.StundeVon, 1, maxStunde);
            return t with { StundeVon = v, StundeBis = Math.Clamp(t.StundeBis, v, maxStunde), Raum = string.IsNullOrWhiteSpace(t.Raum) ? null : t.Raum.Trim() };
        }).OrderBy(t => t.Datum).ThenBy(t => t.StundeVon).ToList();
        foreach (var t in termine)
        {
            if (t.Datum < _uhr.Heute) throw new ArgumentException("Es kann nicht rückwirkend gebucht werden.");
            if (_plan.Frei(s, t.Datum).Frei) throw new ArgumentException($"Am {t.Datum:dd.MM.yyyy} ist kein Unterricht.");
        }
        if (termine.GroupBy(t => t.Datum).Any(g => g.Count() > 1 && g.Zip(g.Skip(1)).Any(p => p.Second.StundeVon <= p.First.StundeBis)))
            throw new ArgumentException("Termine überschneiden sich.");

        // Zusätzliche Stunden zu einem Unterricht aus UntPlan bzw. zu einer bestehenden Buchung (Aktivität, Unterricht): Angaben übernehmen
        Buchung? quelle = null;
        WebUnterricht? pu = null;
        if (d.QuellBuchung is int qb)
        {
            quelle = Holen(qb) ?? throw new ArgumentException("Unbekannte Buchung.");
            if (quelle.Art == Buchungsarten.Ressource) throw new ArgumentException("Zu einer Ressourcenbuchung gibt es keine zusätzlichen Stunden.");
            if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && quelle.BenutzerId != b.Id && !quelle.AlleLehrer.Contains(b.LehrerKuerzel ?? "\u0001", Ci))
                throw new UnauthorizedAccessException("Nur zu eigenen Buchungen.");
            art = quelle.Art;
            d = d with { UnterrichtId = quelle.UnterrichtId, PlanUnterricht = quelle.PlanUnterricht };
        }
        else if (d.PlanUnterricht is int pnr)
        {
            pu = s.Unterricht.GetValueOrDefault(pnr) ?? throw new ArgumentException("Unbekannter Unterricht.");
            art = Buchungsarten.Unterricht;
            if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && !pu.Zeilen.Any(z => Ci.Equals(z.Lehrer, b.LehrerKuerzel ?? "\u0001")))
                throw new UnauthorizedAccessException("Nur für eigenen Unterricht.");
        }
        var zusaetzlich = quelle != null || pu != null || art == Buchungsarten.Unterricht;
        if (zusaetzlich && !Rechtedienst.Darf(b, Recht.Stundenplanaenderung))
            throw new UnauthorizedAccessException("Für zusätzliche Stunden benötigen Sie das Recht „Stundenplanänderung“. Bitte wenden Sie sich an die Administration.");

        EigenerUnterricht? eu = null;
        if (art == Buchungsarten.Unterricht && pu == null && d.PlanUnterricht == null)
        {
            eu = EigenerUnterrichtHolen(d.UnterrichtId ?? 0) ?? throw new ArgumentException("Unbekannter Unterricht.");
            if (quelle == null && !Rechtedienst.Darf(b, Recht.BuchungAndere) && eu.BenutzerId != b.Id && !Ci.Equals(eu.Lehrer, b.LehrerKuerzel ?? "")) throw new UnauthorizedAccessException("Nur für eigenen Unterricht.");
        }
        if (art == Buchungsarten.Unterricht && pu == null && d.PlanUnterricht is int pnr2) pu = s.Unterricht.GetValueOrDefault(pnr2);
        var puLehrer = pu?.Zeilen.Select(z => z.Lehrer).Where(x => x.Length > 0).Distinct(Ci).ToList();
        if (pu != null && quelle == null)
            d = d with
            {
                Klassen = pu.Zeilen.SelectMany(z => z.Klassen).Distinct(Ci).ToList(), Lehrer = puLehrer!.FirstOrDefault() ?? "", WeitereLehrer = puLehrer!.Skip(1).ToList(),
                Fach = pu.Zeilen.Select(z => z.Fach).FirstOrDefault(x => x.Length > 0) ?? "",
                Titel = string.IsNullOrWhiteSpace(d.Titel) ? "Zusätzlicher Unterricht" : d.Titel,
            };
        if (quelle != null)
            d = d with
            {
                Klassen = quelle.Klassen, Lehrer = quelle.Lehrer, WeitereLehrer = quelle.WeitereLehrer, Fach = quelle.Fach, Titel = quelle.Titel,
                Text = string.IsNullOrWhiteSpace(d.Text) ? quelle.Text : d.Text, Zusatz = d.Zusatz ?? quelle.Zusatz,
            };
        var klassen = eu?.Klassen ?? (d.Klassen ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (klassen.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } uk) throw new ArgumentException($"Unbekannte Klasse {uk}.");
        klassen = klassen.Select(x => s.Klassen[x].Kurz).ToList();
        var lehrer = eu?.Lehrer ?? (d.Lehrer is null ? b.LehrerKuerzel ?? "" : d.Lehrer.Trim());
        if (lehrer.Length > 0)
        {
            if (!s.Lehrer.TryGetValue(lehrer, out var lw)) throw new ArgumentException($"Unbekannte Lehrkraft {lehrer}.");
            lehrer = lw.Kurz;
        }
        var fach = eu?.Fach ?? (d.Fach ?? "").Trim();
        if (fach.Length > 0)
        {
            if (!s.Faecher.TryGetValue(fach, out var fw)) throw new ArgumentException($"Unbekanntes Fach {fach}.");
            fach = fw.Kurz;
        }
        var weitereLehrer = new List<string>();
        foreach (var x in (d.WeitereLehrer ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
        {
            if (!s.Lehrer.TryGetValue(x, out var lw2)) throw new ArgumentException($"Unbekannte Lehrkraft {x}.");
            if (!Ci.Equals(lw2.Kurz, lehrer) && !weitereLehrer.Contains(lw2.Kurz, Ci)) weitereLehrer.Add(lw2.Kurz);
        }
        if (lehrer.Length == 0 && weitereLehrer.Count > 0) { lehrer = weitereLehrer[0]; weitereLehrer.RemoveAt(0); }
        var weitereRaeume = (d.WeitereRaeume ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (weitereRaeume.Count > 0 && !Rechtedienst.Darf(b, Recht.BuchungRaum)) throw new UnauthorizedAccessException("Ihr Konto darf keine Räume buchen.");
        var zusatz = (d.Zusatz ?? new BuchungsZusatz()).Bereinigt();
        var titel = (d.Titel ?? "").Trim();
        if (titel.Length == 0) titel = eu is null ? Buchungsarten.Name(art) : (eu.Gruppe.Length > 0 ? eu.Gruppe : "Zusätzlicher Unterricht");
        if (titel.Length > 200) titel = titel[..200];
        var text = (d.Text ?? "").Trim();
        if (text.Length > 1000) text = text[..1000];

        // Ressourcen: nur mobile, aktive; Menge 1 … Anzahl
        var alleRessourcen = Ressourcen(true).ToDictionary(r => r.Id);
        var ressourcen = new List<(Ressource R, int Menge)>();
        foreach (var w in (d.Ressourcen ?? new()).GroupBy(x => x.Id).Select(g => g.First()))
        {
            if (!alleRessourcen.TryGetValue(w.Id, out var r)) throw new ArgumentException("Unbekannte Ressource.");
            if (!r.Mobil) throw new ArgumentException($"{r.Name} ist fest dem Raum {r.Raum} zugeordnet und wird mit dem Raum gebucht.");
            ressourcen.Add((r, Math.Clamp(w.Menge ?? 1, 1, r.Anzahl)));
        }
        if (art == Buchungsarten.Ressource && ressourcen.Count == 0) throw new ArgumentException("Bitte mindestens eine Ressource wählen.");
        if (art == Buchungsarten.Raum && termine.Any(t => t.Raum is null)) throw new ArgumentException("Bitte für jeden Termin einen Raum wählen.");
        if (art == Buchungsarten.Ressource) termine = termine.Select(t => t with { Raum = null }).ToList();
        var buchbar = Rueckmeldungsdienst.Buchbar(s).Select(r => r.Kurz).ToHashSet(Ci);
        string Beginn(int st) => string.IsNullOrWhiteSpace(d.Beginn) ? raster.FirstOrDefault(z => z.Nr == st)?.Beginn ?? "" : d.Beginn!.Trim();
        string Ende(int st) => string.IsNullOrWhiteSpace(d.Ende) ? raster.FirstOrDefault(z => z.Nr == st)?.Ende ?? "" : d.Ende!.Trim();

        lock (Sperre)
        {
            var konflikte = new List<string>();
            foreach (var t in termine)
            {
                var tag = $"{Plandienst.Wochentage[Plandienst.TagIndex(t.Datum)][..2]} {t.Datum:dd.MM.yyyy} ({t.StundeVon}.{(t.StundeBis != t.StundeVon ? $"–{t.StundeBis}." : "")} Std.)";
                foreach (var raum in (t.Raum is string r0 ? new[] { r0 } : Array.Empty<string>()).Concat(weitereRaeume).Distinct(Ci))
                {
                    if (!s.Raeume.TryGetValue(raum, out var rw)) throw new ArgumentException($"Unbekannter Raum {raum}.");
                    if (!buchbar.Contains(rw.Kurz)) throw new ArgumentException($"Raum {rw.Kurz} ist nicht buchbar.");
                    var belegt = RaumBelegung(s, t.Datum).Where(x => x.Stunde >= t.StundeVon && x.Stunde <= t.StundeBis && Ci.Equals(x.Raum, rw.Kurz)).ToList();
                    if (belegt.Count > 0) konflikte.Add($"Raum {rw.Kurz} ist am {tag} belegt: {string.Join(", ", belegt.Select(x => x.Text).Distinct())}");
                }
                if (ressourcen.Count > 0)
                {
                    var gebucht = RessourcenGebucht(t.Datum, null, s);
                    foreach (var (r, menge) in ressourcen.Where(x => Verfuegbar(x.R, gebucht, t.StundeVon, t.StundeBis) < x.Menge))
                        konflikte.Add($"{r.Name} ist am {tag} nicht {(menge > 1 ? $"{menge}× " : "")}verfügbar.");
                }
                if (konflikte.Count >= 10) break;
            }
            // Lehrkraft- bzw. Klassenkonflikte (zusätzliche Stunden, Terminsuche): nur wenn ausdrücklich nicht zugelassen
            if (konflikte.Count == 0 && (d.LehrerKonflikteZulassen == false || d.KlassenKonflikteZulassen == false))
                konflikte.AddRange(Hinweise(s, termine, d.LehrerKonflikteZulassen == false ? lehrer : null, d.KlassenKonflikteZulassen == false ? klassen : null,
                    d.LehrerKonflikteZulassen == false ? weitereLehrer : null).Select(h => h.Replace(" zur gleichen Zeit schon Unterricht bzw. eine andere Aktivität.", " zur gleichen Zeit schon Unterricht bzw. eine andere Aktivität (Konflikt nicht zugelassen).")));
            if (konflikte.Count > 0) throw new ArgumentException(string.Join("\n", konflikte));

            // Reservierung: Räume „nur reservieren“ (ohne Verwaltungsrecht) bzw. „Buchung sofort bestätigen“ abgewählt → Bestätigung durch die Raumadministration
            var reservieren = NurReservieren();
            var verwalten = Rechtedienst.Darf(b, Recht.BuchungVerwalten);
            var bestaetigt = d.SofortBestaetigen == false || (!verwalten && termine.SelectMany(t => (t.Raum is string r1 ? new[] { r1 } : Array.Empty<string>()).Concat(weitereRaeume)).Any(reservieren.Contains))
                ? null : Jetzt;

            var serie = termine.Count > 1 ? Guid.NewGuid().ToString("N")[..12] : "";
            var ids = new List<int>();
            _db.Transaktion(c =>
            {
                foreach (var t in termine)
                {
                    var raum = t.Raum is null ? "" : s.Raeume[t.Raum].Kurz;
                    Db.Ausfuehren(c, """
                        INSERT INTO buchungen (serie, art, datum, stunde_von, stunde_bis, beginn, ende, raum, klassen, lehrer, fach, titel, text, unterricht_id,
                            benachrichtigen, status, benutzer_id, absender, erstellt, weitere_lehrer, weitere_raeume, zusatz, bestaetigt, bestaetigt_von, schueler, plan_unterricht)
                        VALUES (@s, @a, @d, @v, @b, @zb, @ze, @r, @k, @l, @f, @t, @x, @u, @n, 'aktiv', @bid, @abs, @e, @wl, @wr, @zu, @be, @bv, @sch, @pu)
                        """, Db.P("@s", serie), Db.P("@a", art), Db.P("@d", Plandienst.Iso(t.Datum)), Db.P("@v", t.StundeVon), Db.P("@b", t.StundeBis),
                        Db.P("@zb", Beginn(t.StundeVon)), Db.P("@ze", Ende(t.StundeBis)), Db.P("@r", raum), Db.P("@k", JsonSerializer.Serialize(klassen)), Db.P("@l", lehrer),
                        Db.P("@f", fach), Db.P("@t", titel), Db.P("@x", text), Db.P("@u", eu?.Id), Db.P("@n", d.Benachrichtigen), Db.P("@bid", b.Id),
                        Db.P("@abs", b.LehrerKuerzel ?? b.Login), Db.P("@e", Jetzt),
                        Db.P("@wl", JsonSerializer.Serialize(weitereLehrer)), Db.P("@wr", JsonSerializer.Serialize(weitereRaeume.Select(x => s.Raeume[x].Kurz).Where(x => !Ci.Equals(x, raum)).ToList())),
                        Db.P("@zu", JsonSerializer.Serialize(zusatz, ZusatzJson)), Db.P("@be", bestaetigt), Db.P("@bv", bestaetigt is null ? "" : b.LehrerKuerzel ?? b.Login),
                        Db.P("@sch", JsonSerializer.Serialize(quelle?.Schueler ?? new List<int>())), Db.P("@pu", pu?.Id ?? d.PlanUnterricht));
                    var id = (int)Db.LetzteId(c);
                    ids.Add(id);
                    foreach (var (r, menge) in ressourcen)
                        Db.Ausfuehren(c, "INSERT INTO buchung_ressourcen (buchung_id, ressource_id, menge) VALUES (@b, @r, @m)", Db.P("@b", id), Db.P("@r", r.Id), Db.P("@m", menge));
                }
            });
            var neu = ids.Select(i => Holen(i)!).ToList();
            var hinweise = new List<string>();
            if (art is Buchungsarten.Aktivitaet or Buchungsarten.Unterricht && d.Benachrichtigen && lehrer.Length > 0) hinweise.AddRange(Benachrichtigen(s, b, neu));
            if (bestaetigt is null) hinweise.Add("Die Buchung ist reserviert und muss noch von der Raumadministration bestätigt werden.");
            return (neu, hinweise);
        }
    }

    /// <summary>Lehrkraft der Aktivität über den Mitteilungsdienst benachrichtigen (alle Konten mit diesem Kürzel außer dem eigenen).</summary>
    private List<string> Benachrichtigen(Stand s, Benutzer b, List<Buchung> neu)
    {
        var erste = neu[0];
        var alle = erste.AlleLehrer;
        var konten = _benutzer.Alle().Where(x => !x.Gesperrt && x.Id != b.Id && alle.Contains(x.LehrerKuerzel ?? "\u0001", Ci)).ToList();
        if (konten.Count == 0)
            return alle.All(l => l == b.LehrerKuerzel) ? new() : new() { $"Für {string.Join(", ", alle.Where(l => l != b.LehrerKuerzel))} gibt es kein Benutzerkonto. Es wurde keine Benachrichtigung versendet." };
        var zeilen = neu.Take(30).Select(x => $"• {Plandienst.Wochentage[Plandienst.TagIndex(DateOnly.Parse(x.Datum))]}, {DateOnly.Parse(x.Datum):dd.MM.yyyy}, {x.StundeVon}.{(x.StundeBis != x.StundeVon ? $"–{x.StundeBis}." : "")} Stunde"
            + (x.Beginn.Length > 0 ? $" ({x.Beginn}–{x.Ende})" : "") + (x.Raum.Length > 0 ? $", Raum {x.Raum}" : ""));
        var text = $"{b.Anzeigename.Trim() switch { "" => b.Login, var n => n }} hat Sie für die Aktivität „{erste.Titel}“ eingetragen:\n\n{string.Join("\n", zeilen)}"
            + (neu.Count > 30 ? $"\n… und {neu.Count - 30} weitere Termine" : "")
            + (erste.Klassen.Count > 0 ? $"\n\nKlassen: {string.Join(", ", erste.Klassen)}" : "") + (erste.Fach.Length > 0 ? $"\nFach: {erste.Fach}" : "")
            + (erste.Text.Length > 0 ? $"\n\n{erste.Text}" : "");
        try
        {
            _mitteilungen.Senden(b, s, konten.Select(x => "benutzer:" + x.Id), $"Neue Aktivität: {erste.Titel}", text, false);
            return new();
        }
        catch (ArgumentException ex) { return new() { $"Benachrichtigung nicht möglich: {ex.Message}" }; }
    }

    /// <summary>
    /// Stornieren: einzelner Termin oder (serie = true) alle noch kommenden Termine der Serie. Erlaubt für die Person, die gebucht hat,
    /// und die Administration; vergangene Termine kann nur die Administration stornieren. Ergebnis: Anzahl stornierter Termine.
    /// </summary>
    public int Stornieren(Benutzer b, int id, bool serie)
    {
        var x = Holen(id) ?? throw new ArgumentException("Unbekannte Buchung.");
        if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && x.BenutzerId != b.Id) throw new UnauthorizedAccessException("Nur eigene Buchungen können storniert werden.");
        if (x.Status != "aktiv") throw new ArgumentException("Die Buchung ist bereits storniert.");
        var heute = Plandienst.Iso(_uhr.Heute);
        List<int> ids;
        if (serie && x.Serie.Length > 0)
            ids = _db.Liste("SELECT id FROM buchungen WHERE serie = @s AND status = 'aktiv' AND datum >= @h", r => r.Zahl("id"), Db.P("@s", x.Serie), Db.P("@h", heute));
        else
        {
            if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && string.CompareOrdinal(x.Datum, heute) < 0) throw new ArgumentException("Vergangene Termine können nicht storniert werden.");
            ids = new() { x.Id };
        }
        foreach (var i in ids)
            _db.Ausfuehren("UPDATE buchungen SET status = 'storniert', storniert = @z WHERE id = @id AND status = 'aktiv'", Db.P("@z", Jetzt), Db.P("@id", i));
        return ids.Count;
    }

    // ------------------------------------------------------------------------------------------ Lesen

    private const string Spalten = "*";

    private static Buchung Lesen(DbDataReader r) => new(r.Zahl("id"), r.Text("serie"), r.Text("art"), r.Text("datum"), r.Zahl("stunde_von"), r.Zahl("stunde_bis"),
        r.Text("beginn"), r.Text("ende"), r.Text("raum"), JsonListe(r.Text("klassen")), r.Text("lehrer"), r.Text("fach"), r.Text("titel"), r.Text("text"),
        r.ZahlOderNull("unterricht_id"), r.Wahr("benachrichtigen"), r.Text("status"), r.TextOderNull("uebertragen"), r.ZahlOderNull("benutzer_id"), r.Text("absender"),
        r.Text("erstellt"), r.TextOderNull("storniert"))
    {
        WeitereLehrer = JsonListe(r.Text("weitere_lehrer")), WeitereRaeume = JsonListe(r.Text("weitere_raeume")), Zusatz = ZusatzLesen(r.Text("zusatz")),
        Bestaetigt = r.TextOderNull("bestaetigt"), BestaetigtVon = r.Text("bestaetigt_von"), Geaendert = r.Text("geaendert"), GeaendertVon = r.Text("geaendert_von"),
        Schueler = string.IsNullOrEmpty(r.Text("schueler")) ? new() : JsonSerializer.Deserialize<List<int>>(r.Text("schueler")) ?? new(), PlanUnterricht = r.ZahlOderNull("plan_unterricht"),
    };

    private static readonly JsonSerializerOptions ZusatzJson = new(JsonSerializerDefaults.Web);
    private static BuchungsZusatz ZusatzLesen(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<BuchungsZusatz>(json, ZusatzJson) ?? new(); } catch (JsonException) { return new(); }
    }

    private List<Buchung> MitRessourcen(List<Buchung> liste)
    {
        if (liste.Count == 0) return liste;
        var res = new Dictionary<int, List<BuchungsRessource>>();
        foreach (var teil in liste.Select(b => b.Id).Chunk(400))
            foreach (var (bid, r) in _db.Liste($"""
                         SELECT br.buchung_id, br.ressource_id, br.menge, COALESCE(x.name, '') AS name FROM buchung_ressourcen br LEFT JOIN ressourcen x ON x.id = br.ressource_id
                         WHERE br.buchung_id IN ({string.Join(",", teil)})
                         """, r => (r.Zahl("buchung_id"), new BuchungsRessource(r.Zahl("ressource_id"), r.Text("name"), r.Zahl("menge")))))
                (res.TryGetValue(bid, out var l) ? l : res[bid] = new()).Add(r);
        return liste.Select(b => res.TryGetValue(b.Id, out var l) ? b with { Ressourcen = l } : b).ToList();
    }

    /// <summary>Alle Termine einer Serie (auch stornierte), nach Datum.</summary>
    public List<Buchung> Serie(string serie) => MitRessourcen(_db.Liste($"SELECT {Spalten} FROM buchungen WHERE serie = @s ORDER BY datum, stunde_von", Lesen, Db.P("@s", serie)));

    public Buchung? Holen(int id) => MitRessourcen(_db.Liste($"SELECT {Spalten} FROM buchungen WHERE id = @id", Lesen, Db.P("@id", id))).FirstOrDefault();

    /// <summary>Aktive Buchungen eines Tages.</summary>
    public List<Buchung> AktiveAm(DateOnly d) =>
        MitRessourcen(_db.Liste($"SELECT {Spalten} FROM buchungen WHERE datum = @d AND status = 'aktiv' ORDER BY stunde_von, id", Lesen, Db.P("@d", Plandienst.Iso(d))));

    /// <summary>
    /// Buchungsliste (Meine Buchungen): eigene Buchungen und Buchungen, in denen man als Lehrkraft eingetragen ist;
    /// die Administration sieht mit alle = true alle Buchungen.
    /// </summary>
    public List<Buchung> Liste(Benutzer b, bool alle, DateOnly von, DateOnly bis) =>
        MitRessourcen(_db.Liste($"""
            SELECT {Spalten} FROM buchungen WHERE datum >= @von AND datum <= @bis AND (@alle = 1 OR benutzer_id = @b OR (lehrer <> '' AND lehrer = @l) OR weitere_lehrer LIKE @wl)
            ORDER BY datum, stunde_von, id LIMIT 3000
            """, Lesen, Db.P("@von", Plandienst.Iso(von)), Db.P("@bis", Plandienst.Iso(bis)), Db.P("@alle", alle && Rechtedienst.Darf(b, Recht.BuchungAndere)), Db.P("@b", b.Id), Db.P("@l", b.LehrerKuerzel ?? "\u0001"),
            Db.P("@wl", $"%\"{b.LehrerKuerzel ?? "\u0001"}\"%")));

    // ------------------------------------------------------------------------------------------ Pläne

    /// <summary>
    /// Ist die Buchung nach der Übernahme in UntPlan schon im übertragenen Plan enthalten (gleiche Stunde, geänderter Eintrag mit
    /// demselben Raum bzw. derselben Lehrkraft)? Dann wird sie nicht doppelt angezeigt bzw. gezählt.
    /// </summary>
    public static bool SchonImPaket(Buchung b, IReadOnlyList<Rohstunde>? paket) =>
        b.Uebertragen != null && paket != null && paket.Any(r => r.Stunde >= b.StundeVon && r.Stunde <= b.StundeBis && r.Status is not ("regulaer" or "entfall")
            && (b.Raum.Length > 0 ? r.Raeume.SelectMany(Plandienst.RaumListe).Contains(b.Raum, Ci) : b.Lehrer.Length > 0 && r.Lehrer.Contains(b.Lehrer, Ci)));

    /// <summary>
    /// Buchungen eines Tages als Stunden für die Pläne (Web, App): Raumbuchung bzw. zusätzlicher Unterricht als „zusaetzlich“ mit Art
    /// „Raumbuchung…“ (Kartenstatus „buchung“), Aktivität als Art „Veranstaltung (Aktivität)“ (Kartenstatus „veranstaltung“).
    /// Reine Ressourcenbuchungen erscheinen nicht im Plan. Nie eine Vertretung, kein Vertretungstext.
    /// </summary>
    public static IEnumerable<Rohstunde> Planstunden(Db db, DateOnly d, Func<IReadOnlyList<Rohstunde>?> paket)
    {
        var liste = db.Liste("SELECT * FROM buchungen WHERE datum = @d AND status = 'aktiv' AND art <> @r ORDER BY stunde_von, id", Lesen,
            Db.P("@d", Plandienst.Iso(d)), Db.P("@r", Buchungsarten.Ressource));
        var roh = liste.Any(b => b.Uebertragen != null) ? paket() : null;
        foreach (var b in liste.Where(b => !SchonImPaket(b, roh)))
        {
            var art = b.Art switch
            {
                Buchungsarten.Aktivitaet => "Veranstaltung (Aktivität)",
                Buchungsarten.Unterricht => "Raumbuchung (Zusätzlicher Unterricht)",
                _ => "Raumbuchung",
            };
            var lehrer = b.AlleLehrer;
            var raeume = b.AlleRaeume;
            for (var st = b.StundeVon; st <= b.StundeBis; st++)
                yield return new Rohstunde(st, null, b.Fach, b.Klassen, lehrer, raeume, "zusaetzlich", null, b.Titel, art, Unbestaetigt: true);
        }
    }

    // ------------------------------------------------------------------------------------------ Schnittstelle zu UntPlan

    /// <summary>
    /// Noch nicht übertragene Buchungen als Rückmeldungen „raumbuchung“ (wie bisherige Raumbuchungen): Raumbuchung mit Raum,
    /// Aktivität bzw. zusätzlicher Unterricht mit Fach, Lehrkraft und Klassen (Raum optional). Reine Ressourcenbuchungen und
    /// stornierte Buchungen werden nicht übertragen. Id: „buchung-{Nummer}“.
    /// </summary>
    /// <remarks>
    /// Dazu Stornierungen schon übertragener Buchungen als Rückmeldung „buchungstorno“ (Id „storno-buchung-{Nummer}“, BezugId „buchung-{Nummer}“),
    /// bis UntPlan sie quittiert – UntPlan entfernt dann die übernommene Änderung (Vertrag vertrag_storno.md).
    /// </remarks>
    public List<WebRueckmeldung> Rueckmeldungen() =>
        _db.Liste("SELECT * FROM buchungen WHERE status = 'aktiv' AND uebertragen IS NULL AND bestaetigt IS NOT NULL AND art <> @r ORDER BY erstellt, id", Lesen, Db.P("@r", Buchungsarten.Ressource))
            .Where(b => b.Raum.Length > 0 || b.Art != Buchungsarten.Raum)
            .Select(b => AlsRueckmeldung(b, WebRueckmeldungsTypen.Raumbuchung, RueckmeldungsPraefix + b.Id, b.Erstellt))
            .Concat(Stornierungen().Select(b => AlsRueckmeldung(b, WebRueckmeldungsTypen.BuchungStorno, StornoPraefix + b.Id, b.Storniert ?? b.Erstellt) with
            {
                BezugId = RueckmeldungsPraefix + b.Id, Grund = "storniert",
            })).ToList();

    private static WebRueckmeldung AlsRueckmeldung(Buchung b, string typ, string id, string zeit) => new()
    {
        Id = id, Typ = typ, Erstellt = DateTime.TryParse(zeit, out var e) ? e : DateTime.MinValue,
        Absender = b.Absender, Lehrer = b.Lehrer.Length > 0 ? b.Lehrer : null, Von = b.Datum, StundeVon = b.StundeVon, StundeBis = b.StundeBis,
        Raum = b.Raum.Length > 0 ? b.Raum : null, Raeume = b.AlleRaeume, Klassen = b.Klassen,
        Fach = b.Fach.Length > 0 ? b.Fach : null, Titel = b.Titel, Text = b.Text.Length > 0 ? b.Text : null,
    };

    /// <summary>Präfix der Rückmeldungs-Id einer Stornierung (Schnittstelle zu UntPlan).</summary>
    public const string StornoPraefix = "storno-" + RueckmeldungsPraefix;

    /// <summary>Stornierte Buchungen, die UntPlan schon übernommen hat und deren Stornierung noch nicht quittiert ist.</summary>
    public List<Buchung> Stornierungen() =>
        _db.Liste("SELECT * FROM buchungen WHERE status = 'storniert' AND uebertragen IS NOT NULL AND storno_uebertragen IS NULL AND art <> @r ORDER BY storniert, id", Lesen,
            Db.P("@r", Buchungsarten.Ressource));

    /// <summary>Von UntPlan quittierte Buchungen bzw. Stornierungen als übertragen vermerken. Ergebnis: Anzahl.</summary>
    public int Quittieren(IEnumerable<string> ids)
    {
        var n = 0;
        foreach (var id in ids.Distinct())
            if (id.StartsWith(StornoPraefix, StringComparison.Ordinal) && int.TryParse(id[StornoPraefix.Length..], out var si))
                n += _db.Ausfuehren("UPDATE buchungen SET storno_uebertragen = @z WHERE id = @id AND status = 'storniert' AND storno_uebertragen IS NULL", Db.P("@z", Jetzt), Db.P("@id", si));
            else if (id.StartsWith(RueckmeldungsPraefix, StringComparison.Ordinal) && int.TryParse(id[RueckmeldungsPraefix.Length..], out var i))
                n += _db.Ausfuehren("UPDATE buchungen SET uebertragen = @z WHERE id = @id AND uebertragen IS NULL", Db.P("@z", Jetzt), Db.P("@id", i));
        return n;
    }

    // ------------------------------------------------------------------------------------------ Übersichten

    /// <summary>Raumübersicht: Belegung der buchbaren Räume (gefiltert nach Kriterien) für einen oder mehrere Tage.</summary>
    public object Raumuebersicht(Stand s, DateOnly von, int tage, Kriterien? k)
    {
        tage = Math.Clamp(tage, 1, 7);
        var daten = Enumerable.Range(0, tage).Select(von.AddDays).ToList();
        var raeume = Raeume(s).Where(r => r.Buchbar && Passt(r, k ?? new Kriterien())).ToList();
        var belegung = daten.ToDictionary(d => d, d => _plan.Frei(s, d).Frei ? new List<Belegt>() : RaumBelegung(s, d));
        return new
        {
            zeitraster = _plan.Zeitraster(s),
            tage = daten.Select(d => new { datum = Plandienst.Iso(d), wochentag = Plandienst.Wochentage[Plandienst.TagIndex(d)], frei = _plan.Frei(s, d).Frei, grund = _plan.Frei(s, d).Grund }),
            raeume = raeume.Select(r => new
            {
                kurz = r.Kurz, name = r.Name, raumart = r.Raumart, kapazitaet = r.Kapazitaet, gebaeude = r.Gebaeude, abteilung = r.Abteilung, raumgruppe = r.Raumgruppe,
                ressourcen = r.Ressourcen,
                belegung = daten.ToDictionary(d => Plandienst.Iso(d), d => belegung[d].Where(x => Ci.Equals(x.Raum, r.Kurz))
                    .GroupBy(x => x.Stunde).OrderBy(g => g.Key)
                    .Select(g => new { stunde = g.Key, art = g.First().Art, text = string.Join(", ", g.Select(x => x.Text).Distinct()), buchung = g.Select(x => x.BuchungId).FirstOrDefault(x => x.HasValue) })),
            }),
        };
    }

    /// <summary>
    /// Ressourcenübersicht: Buchungen mobiler Ressourcen, bei festen Ressourcen die Belegung ihres Raums – für die Woche (Standard) bzw.
    /// mit tage = 1 nur für den gewählten Tag. Je Ressource und Tag zusätzlich die gebuchte Menge je Stunde (gebucht: { "3": 2 }), daraus
    /// berechnet die Oberfläche die Verfügbarkeit für einen gewählten Zeitraum und den Belegungs-Zeitstrahl.
    /// </summary>
    public object Ressourcenuebersicht(Stand s, DateOnly datum, List<int>? arten, int? tage = null)
    {
        var montag = Plandienst.Montag(datum);
        var daten = tage == 1 ? new List<DateOnly> { datum }
            : Enumerable.Range(0, Math.Clamp(s.Paket.Zeitraster.TageProWoche, 5, 7)).Select(montag.AddDays).ToList();
        var ressourcen = Ressourcen(true).Where(r => arten is not { Count: > 0 } || (r.ArtId is int a && arten.Contains(a))).ToList();
        var buchungen = daten.ToDictionary(d => d, AktiveAm);
        var belegung = daten.ToDictionary(d => d, d => ressourcen.Any(r => !r.Mobil) && !_plan.Frei(s, d).Frei ? RaumBelegung(s, d) : new List<Belegt>());
        // gebuchte Menge je Stunde (feste Ressource: ganze Anzahl, solange ihr Raum belegt ist)
        Dictionary<string, int> Gebucht(Ressource r, DateOnly d)
        {
            var res = new Dictionary<string, int>();
            if (r.Mobil)
            {
                foreach (var st in RessourceGesperrt(s, d, r.Id)) res[st.ToString()] = r.Anzahl;
                foreach (var b in buchungen[d])
                    foreach (var x in b.Ressourcen.Where(x => x.Id == r.Id))
                        for (var st = b.StundeVon; st <= b.StundeBis; st++) res[st.ToString()] = res.GetValueOrDefault(st.ToString()) + x.Menge;
            }
            else
                foreach (var st in belegung[d].Where(x => Ci.Equals(x.Raum, r.Raum)).Select(x => x.Stunde).Distinct()) res[st.ToString()] = r.Anzahl;
            return res;
        }
        object Eintraege(Ressource r, DateOnly d)
        {
            if (r.Mobil)
                return buchungen[d].SelectMany(b => b.Ressourcen.Where(x => x.Id == r.Id).Select(x => new
                {
                    von = b.StundeVon, bis = b.StundeBis, beginn = b.Beginn, ende = b.Ende, menge = x.Menge, text = b.Titel, absender = b.Absender, raum = b.Raum, buchung = (int?)b.Id,
                })).ToList();
            // feste Ressource: Stunden, in denen ihr Raum belegt ist (zusammenhängende Stunden als Block)
            var stunden = belegung[d].Where(x => Ci.Equals(x.Raum, r.Raum)).Select(x => x.Stunde).Distinct().Order().ToList();
            var bloecke = new List<object>();
            for (var i = 0; i < stunden.Count;)
            {
                var j = i;
                while (j + 1 < stunden.Count && stunden[j + 1] == stunden[j] + 1) j++;
                bloecke.Add(new { von = stunden[i], bis = stunden[j], beginn = "", ende = "", menge = r.Anzahl, text = $"Raum {r.Raum} belegt", absender = "", raum = r.Raum, buchung = (int?)null });
                i = j + 1;
            }
            return bloecke;
        }
        return new
        {
            montag = Plandienst.Iso(montag), zeitraster = _plan.Zeitraster(s),
            tage = daten.Select(d => new { datum = Plandienst.Iso(d), wochentag = Plandienst.Wochentage[Plandienst.TagIndex(d)], frei = _plan.Frei(s, d).Frei, grund = _plan.Frei(s, d).Grund }),
            ressourcen = ressourcen.Select(r => new
            {
                id = r.Id, name = r.Name, art = r.Art, artId = r.ArtId, raum = r.Raum, anzahl = r.Anzahl, mobil = r.Mobil, standort = r.Standort, symbol = r.Symbol, bemerkung = r.Bemerkung,
                belegung = daten.ToDictionary(d => Plandienst.Iso(d), d => Eintraege(r, d)),
                gebucht = daten.ToDictionary(d => Plandienst.Iso(d), d => Gebucht(r, d)),
            }),
        };
    }

    // ------------------------------------------------------------------------------------------ Bearbeiten, Bestätigen, Schülergruppe

    private List<Buchung> Betroffene(Buchung x, bool serie) =>
        serie && x.Serie.Length > 0 ? Serie(x.Serie).Where(t => t.Status == "aktiv" && (t.Id == x.Id || string.CompareOrdinal(t.Datum, Plandienst.Iso(_uhr.Heute)) >= 0)).ToList() : new() { x };

    private static bool DarfBearbeiten(Benutzer b, Buchung x) => Rechtedienst.Darf(b, Recht.BuchungAndere) || x.BenutzerId == b.Id;

    /// <summary>
    /// Details der Buchung ändern (Klassen, Lehrkräfte, Fach, Titel, Bemerkung, Zusatzangaben) – für den Termin bzw. alle kommenden Termine der Serie.
    /// Termin und Raum bleiben (dafür: stornieren und neu buchen bzw. zusätzliche Stunden). Noch nicht übertragene Buchungen gehen geändert an UntPlan.
    /// </summary>
    public List<Buchung> Aendern(Stand s, Benutzer b, int id, BuchungAenderung d)
    {
        var x = Holen(id) ?? throw new ArgumentException("Unbekannte Buchung.");
        if (!DarfBearbeiten(b, x)) throw new UnauthorizedAccessException("Nur eigene Buchungen können geändert werden.");
        if (x.Status != "aktiv") throw new ArgumentException("Die Buchung ist storniert.");
        var klassen = (d.Klassen ?? x.Klassen).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(Ci).ToList();
        if (klassen.FirstOrDefault(k => !s.Klassen.ContainsKey(k)) is { } uk) throw new ArgumentException($"Unbekannte Klasse {uk}.");
        klassen = klassen.Select(k => s.Klassen[k].Kurz).ToList();
        string LehrerKurz(string l) => s.Lehrer.TryGetValue(l, out var w) ? w.Kurz : throw new ArgumentException($"Unbekannte Lehrkraft {l}.");
        var lehrer = (d.Lehrer ?? x.Lehrer).Trim();
        if (lehrer.Length > 0) lehrer = LehrerKurz(lehrer);
        var weitere = (d.WeitereLehrer ?? x.WeitereLehrer).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => LehrerKurz(l.Trim())).Where(l => !Ci.Equals(l, lehrer)).Distinct(Ci).ToList();
        var fach = (d.Fach ?? x.Fach).Trim();
        if (fach.Length > 0) fach = s.Faecher.TryGetValue(fach, out var fw) ? fw.Kurz : throw new ArgumentException($"Unbekanntes Fach {fach}.");
        var titel = (d.Titel ?? x.Titel).Trim();
        if (titel.Length == 0) titel = x.Titel;
        if (titel.Length > 200) titel = titel[..200];
        var text = (d.Text ?? x.Text).Trim();
        if (text.Length > 1000) text = text[..1000];
        var zusatz = (d.Zusatz ?? x.Zusatz).Bereinigt();
        var liste = Betroffene(x, d.Serie);
        foreach (var t in liste)
            _db.Ausfuehren("""
                UPDATE buchungen SET klassen = @k, lehrer = @l, weitere_lehrer = @wl, fach = @f, titel = @t, text = @x, zusatz = @z, geaendert = @g, geaendert_von = @gv
                WHERE id = @id
                """, Db.P("@k", JsonSerializer.Serialize(klassen)), Db.P("@l", lehrer), Db.P("@wl", JsonSerializer.Serialize(weitere)), Db.P("@f", fach), Db.P("@t", titel),
                Db.P("@x", text), Db.P("@z", JsonSerializer.Serialize(zusatz, ZusatzJson)), Db.P("@g", Jetzt), Db.P("@gv", b.LehrerKuerzel ?? b.Login), Db.P("@id", t.Id));
        return liste.Select(t => Holen(t.Id)!).ToList();
    }

    /// <summary>
    /// Reservierung bestätigen (Raumadministration, Recht „Räume und Ressourcen verwalten“): Termin bzw. alle Termine der Serie. Mit
    /// „E-Mail-Benachrichtigung bei Statusänderung“ erhält die buchende Person eine Mitteilung. Ergebnis: Anzahl bestätigter Termine.
    /// </summary>
    public int Bestaetigen(Stand? s, Benutzer b, int id, bool serie)
    {
        if (!Rechtedienst.Darf(b, Recht.BuchungVerwalten)) throw new UnauthorizedAccessException("Nur die Raumadministration bestätigt Reservierungen.");
        var x = Holen(id) ?? throw new ArgumentException("Unbekannte Buchung.");
        var liste = (serie && x.Serie.Length > 0 ? Serie(x.Serie) : new() { x }).Where(t => t.Status == "aktiv" && t.Bestaetigt is null).ToList();
        foreach (var t in liste)
            _db.Ausfuehren("UPDATE buchungen SET bestaetigt = @z, bestaetigt_von = @v WHERE id = @id AND bestaetigt IS NULL",
                Db.P("@z", Jetzt), Db.P("@v", b.LehrerKuerzel ?? b.Login), Db.P("@id", t.Id));
        if (liste.Count > 0 && x.Zusatz.EmailBenachrichtigung && x.BenutzerId is int empf && empf != b.Id && s != null)
            try
            {
                _mitteilungen.Senden(b, s, new[] { "benutzer:" + empf }, $"Buchung bestätigt: {x.Titel}",
                    $"Ihre Buchung „{x.Titel}“ ({string.Join(", ", liste.Take(10).Select(t => $"{DateOnly.Parse(t.Datum):dd.MM.yyyy} {t.StundeVon}.{(t.StundeBis != t.StundeVon ? $"–{t.StundeBis}." : "")} Std."))}"
                    + $"{(x.AlleRaeume.Count > 0 ? $", Raum {string.Join(", ", x.AlleRaeume)}" : "")}) wurde von der Raumadministration bestätigt.", false);
            }
            catch (ArgumentException) { /* Benachrichtigung ist optional */ }
        return liste.Count;
    }

    /// <summary>Schüler*innen der Schülergruppe der Buchung setzen (alle kommenden Termine der Serie). Erlaubt: nur Schüler*innen der gebuchten Klassen.</summary>
    public List<int> SchuelerSetzen(Stand s, Benutzer b, int id, IEnumerable<int> ids)
    {
        var x = Holen(id) ?? throw new ArgumentException("Unbekannte Buchung.");
        if (!DarfBearbeiten(b, x) && !x.AlleLehrer.Contains(b.LehrerKuerzel ?? "\u0001", Ci)) throw new UnauthorizedAccessException("Nur eigene Buchungen.");
        var erlaubt = SchuelerAuswahl(s, x).Select(y => y.Id).ToHashSet();
        var liste = ids.Distinct().Where(erlaubt.Contains).ToList();
        foreach (var t in Betroffene(x, true))
            _db.Ausfuehren("UPDATE buchungen SET schueler = @s, geaendert = @g, geaendert_von = @gv WHERE id = @id", Db.P("@s", JsonSerializer.Serialize(liste)),
                Db.P("@g", Jetzt), Db.P("@gv", b.LehrerKuerzel ?? b.Login), Db.P("@id", t.Id));
        return liste;
    }

    /// <summary>Schüler*innen, die der Schülergruppe einer Buchung hinzugefügt werden können: die der gebuchten Klassen (ohne Klasse: alle).</summary>
    public List<WebSchueler> SchuelerAuswahl(Stand s, Buchung x) =>
        s.Schueler.Where(y => x.Klassen.Count == 0 || x.Klassen.Contains(y.Klasse, Ci)).OrderBy(y => y.Klasse, De).ThenBy(y => y.Name, De).ThenBy(y => y.Vorname, De).ToList();

    // ------------------------------------------------------------------------------------------ Verfügbarkeit (Terminsuche, zusätzliche Stunden)

    private static readonly string[] Uhrzeitformate = { "HH:mm", "H:mm" };
    private static int Min(string hhmm) => TimeOnly.TryParseExact((hhmm ?? "").Trim(), Uhrzeitformate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.Hour * 60 + t.Minute : -1;

    /// <summary>
    /// Belegte Zeiten (Minuten ab Mitternacht) je Element und Tag für die Terminsuche bzw. zusätzliche Stunden: Lehrkräfte und Klassen laut Plan
    /// (ohne Entfall) und Buchungen, Räume laut Raumbelegung (Plan, Prüfungen, Termine, Sperren, Buchungen). Unterrichtsfreie Tage (Ferien,
    /// Feiertage, Wochenende) mit Grund. Schlüssel: „lehrer:KÜRZEL“, „klasse:KÜRZEL“, „raum:KÜRZEL“.
    /// </summary>
    public object Verfuegbarkeit(Stand s, DateOnly von, DateOnly bis, IEnumerable<string>? lehrer, IEnumerable<string>? klassen, IEnumerable<string>? raeume, int? ohneBuchung = null)
    {
        if (bis < von) (von, bis) = (bis, von);
        if (bis > von.AddDays(41)) bis = von.AddDays(41);
        string Kurz<T>(Dictionary<string, T> d, string x, Func<T, string> k, string art) => d.TryGetValue(x.Trim(), out var w) ? k(w) : throw new ArgumentException($"Unbekannte {art} {x}.");
        var ll = (lehrer ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Kurz(s.Lehrer, x, w => w.Kurz, "Lehrkraft")).Distinct(Ci).ToList();
        var kl = (klassen ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Kurz(s.Klassen, x, w => w.Kurz, "Klasse")).Distinct(Ci).ToList();
        var rl = (raeume ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Kurz(s.Raeume, x, w => w.Kurz, "Raum")).Distinct(Ci).ToList();
        var raster = s.Paket.Zeitraster.Stunden.Where(z => z.Nr > 0).ToDictionary(z => z.Nr);
        (int, int) Zeit(int v, int b2) => (raster.TryGetValue(v, out var a) ? Min(a.Beginn) : -1, raster.TryGetValue(b2, out var e) ? Min(e.Ende) : -1);
        var buchbar = Rueckmeldungsdienst.Buchbar(s).Select(r => r.Kurz).ToHashSet(Ci);
        var reserv = NurReservieren();
        var tage = new List<object>();
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            var (frei, grund) = _plan.Frei(s, d);
            var belegt = new Dictionary<string, List<object>>();
            void Dazu(string schluessel, int a, int e, string text, int? buchung = null)
            {
                if (a < 0 || e <= a) return;
                (belegt.TryGetValue(schluessel, out var l) ? l : belegt[schluessel] = new()).Add(new { von = a, bis = e, text, buchung });
            }
            if (!frei)
            {
                var roh = _plan.Rohstunden(s, d).Where(r => r.Status != "entfall").ToList();
                var buchungen = AktiveAm(d).Where(x => x.Id != ohneBuchung && x.Art != Buchungsarten.Ressource).ToList();
                foreach (var r in roh)
                {
                    var (a, e) = Zeit(r.Stunde, r.Stunde);
                    var text = string.Join(" ", new[] { r.Fach, string.Join(",", r.Klassen), string.Join(",", r.Lehrer) }.Where(x => x.Length > 0));
                    foreach (var l in ll.Where(l => r.Lehrer.Contains(l, Ci))) Dazu("lehrer:" + l, a, e, text.Length > 0 ? text : r.Text);
                    foreach (var k in kl.Where(k => r.Klassen.Contains(k, Ci))) Dazu("klasse:" + k, a, e, text.Length > 0 ? text : r.Text);
                }
                foreach (var x in buchungen.Where(x => !SchonImPaket(x, roh)))
                {
                    var (a0, e0) = Zeit(x.StundeVon, x.StundeBis);
                    var a = Min(x.Beginn) is var bb and >= 0 ? bb : a0;
                    var e = Min(x.Ende) is var ee and >= 0 ? ee : e0;
                    var text = $"{(x.Titel.Length > 0 ? x.Titel : x.ArtName)} ({x.Absender})";
                    foreach (var l in ll.Where(l => x.AlleLehrer.Contains(l, Ci))) Dazu("lehrer:" + l, a, e, text, x.Id);
                    foreach (var k in kl.Where(k => x.Klassen.Contains(k, Ci))) Dazu("klasse:" + k, a, e, text, x.Id);
                }
                if (rl.Count > 0)
                {
                    var rb = RaumBelegung(s, d, ohneBuchung);
                    foreach (var r in rl)
                    {
                        // Buchungen mit Uhrzeit genau, sonst die Stunde des Zeitrasters
                        foreach (var x in rb.Where(x => Ci.Equals(x.Raum, r)).GroupBy(x => (x.Stunde, x.BuchungId)))
                        {
                            var bu = x.Key.BuchungId is int bid ? buchungen.FirstOrDefault(y => y.Id == bid) : null;
                            var (a, e) = Zeit(x.Key.Stunde, x.Key.Stunde);
                            if (bu != null && Min(bu.Beginn) >= 0 && Min(bu.Ende) > 0)
                            {
                                if (x.Key.Stunde == bu.StundeVon) a = Min(bu.Beginn);
                                if (x.Key.Stunde == bu.StundeBis) e = Min(bu.Ende);
                            }
                            Dazu("raum:" + r, a, e, x.First().Text, x.Key.BuchungId);
                        }
                        if (!buchbar.Contains(r)) Dazu("raum:" + r, 0, 24 * 60, "Raum ist nicht buchbar");
                    }
                }
            }
            tage.Add(new { datum = Plandienst.Iso(d), wochentag = Plandienst.Wochentage[Plandienst.TagIndex(d)], frei, grund, belegt });
        }
        return new
        {
            zeitraster = _plan.Zeitraster(s), tage, lehrer = ll, klassen = kl, raeume = rl,
            nurReservieren = rl.Where(reserv.Contains).ToList(), beschraenkungen = Beschraenkungen(),
        };
    }

    /// <summary>Buchungsbeschränkungen (Administration | Buchungsbeschränkungen): Standarddauer der Verabredungen und Zeitbereich.</summary>
    public Buchungsbeschraenkungen Beschraenkungen()
    {
        var w = _db.Wert("SELECT wert FROM einstellungen WHERE schluessel = 'buchungsbeschraenkungen'") as string;
        if (string.IsNullOrEmpty(w)) return new();
        try { return JsonSerializer.Deserialize<Buchungsbeschraenkungen>(w, ZusatzJson) ?? new(); } catch (JsonException) { return new(); }
    }

    public Buchungsbeschraenkungen BeschraenkungenSpeichern(Buchungsbeschraenkungen x)
    {
        if (Min(x.Beginn) < 0 || Min(x.Ende) < 0) throw new ArgumentException("Uhrzeit bitte als HH:mm angeben.");
        if (Min(x.Ende) <= Min(x.Beginn)) throw new ArgumentException("Das Ende des Zeitbereichs liegt vor dem Beginn.");
        if (x.Dauer is < 5 or > 600) throw new ArgumentException("Die Dauer muss zwischen 5 und 600 Minuten liegen.");
        var neu = x with { Beginn = $"{Min(x.Beginn) / 60:00}:{Min(x.Beginn) % 60:00}", Ende = $"{Min(x.Ende) / 60:00}:{Min(x.Ende) % 60:00}" };
        new Einstellungen(_db).Schreiben("buchungsbeschraenkungen", JsonSerializer.Serialize(neu, ZusatzJson));
        return neu;
    }

    // ------------------------------------------------------------------------------------------ Neuer Unterricht

    private EigenerUnterricht UnterrichtLesen(DbDataReader r) => new(r.Zahl("id"), r.Text("lehrer"), r.Text("fach"), JsonListe(r.Text("klassen")), r.Text("gruppe"),
        r.Text("text"), r.Zahl("wochenstunden"), r.ZahlOderNull("benutzer_id"), r.Text("erstellt"), r.Zahl("gebucht"))
    {
        Zusatz = string.IsNullOrWhiteSpace(r.Text("zusatz")) ? new() : JsonSerializer.Deserialize<UnterrichtZusatz>(r.Text("zusatz"), ZusatzJson) ?? new(),
    };

    private const string UnterrichtSql = """
        SELECT u.*, (SELECT COALESCE(SUM(b.stunde_bis - b.stunde_von + 1), 0) FROM buchungen b WHERE b.unterricht_id = u.id AND b.status = 'aktiv') AS gebucht
        FROM eigene_unterrichte u WHERE u.geloescht = 0
        """;

    public EigenerUnterricht? EigenerUnterrichtHolen(int id) => _db.Liste(UnterrichtSql + " AND u.id = @id", UnterrichtLesen, Db.P("@id", id)).FirstOrDefault();

    /// <summary>Eigene Unterrichte (angelegt bzw. als Lehrkraft eingetragen); die Administration sieht alle.</summary>
    public List<EigenerUnterricht> EigeneUnterrichte(Benutzer b) =>
        _db.Liste(UnterrichtSql + " AND (@alle = 1 OR u.benutzer_id = @b OR u.lehrer = @l) ORDER BY u.lehrer, u.fach, u.id", UnterrichtLesen,
            Db.P("@alle", Rechtedienst.Darf(b, Recht.BuchungAndere)), Db.P("@b", b.Id), Db.P("@l", b.LehrerKuerzel ?? "\u0001"));

    public EigenerUnterricht UnterrichtAnlegen(Stand s, Benutzer b, string? lehrer, string? fach, IEnumerable<string>? klassen, string? gruppe, string? text, int? wochenstunden,
        UnterrichtZusatz? zusatz = null, bool benachrichtigen = false)
    {
        Kollegium(b);
        var l = (lehrer ?? b.LehrerKuerzel ?? "").Trim();
        if (l.Length == 0) throw new ArgumentException("Bitte eine Lehrkraft wählen.");
        if (!s.Lehrer.TryGetValue(l, out var lw)) throw new ArgumentException($"Unbekannte Lehrkraft {l}.");
        var f = (fach ?? "").Trim();
        if (f.Length == 0) throw new ArgumentException("Bitte ein Fach wählen.");
        if (!s.Faecher.TryGetValue(f, out var fw)) throw new ArgumentException($"Unbekanntes Fach {f}.");
        var k = (klassen ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (k.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } uk) throw new ArgumentException($"Unbekannte Klasse {uk}.");
        k = k.Select(x => s.Klassen[x].Kurz).ToList();
        static string T(string? x, int n) { var t = (x ?? "").Trim(); return t.Length > n ? t[..n] : t; }
        var id = _db.Einfuegen("""
            INSERT INTO eigene_unterrichte (lehrer, fach, klassen, gruppe, text, wochenstunden, benutzer_id, erstellt, geloescht, zusatz)
            VALUES (@l, @f, @k, @g, @t, @w, @b, @e, 0, @z)
            """, Db.P("@l", lw.Kurz), Db.P("@f", fw.Kurz), Db.P("@k", JsonSerializer.Serialize(k)), Db.P("@g", T(gruppe, 100)), Db.P("@t", T(text, 1000)),
            Db.P("@w", Math.Clamp(wochenstunden ?? 0, 0, 40)), Db.P("@b", b.Id), Db.P("@e", Jetzt), Db.P("@z", JsonSerializer.Serialize(ZusatzPruefen(zusatz), ZusatzJson)));
        var neu = EigenerUnterrichtHolen((int)id)!;
        // „Lehrkraft der Aktivität benachrichtigen“: Mitteilung an die eingetragene Lehrkraft (nicht an sich selbst)
        if (benachrichtigen)
            foreach (var konto in _benutzer.Alle().Where(x => !x.Gesperrt && x.Id != b.Id && Ci.Equals(x.LehrerKuerzel ?? "", neu.Lehrer)))
                try
                {
                    _mitteilungen.Senden(b, s, new[] { "benutzer:" + konto.Id }, $"Neuer Unterricht: {neu.Fach} {string.Join(", ", neu.Klassen)}",
                        $"{(b.Anzeigename.Trim().Length > 0 ? b.Anzeigename.Trim() : b.Login)} hat für Sie den Unterricht {neu.Fach}{(neu.Klassen.Count > 0 ? $" ({string.Join(", ", neu.Klassen)})" : "")} angelegt."
                        + (neu.Zusatz.Von.Length > 0 ? $"\nZeitraum: {neu.Zusatz.Von} bis {neu.Zusatz.Bis}" : "") + (neu.Text.Length > 0 ? $"\n\n{neu.Text}" : ""), false);
                }
                catch (ArgumentException) { /* optional */ }
        return neu;
    }

    private static UnterrichtZusatz ZusatzPruefen(UnterrichtZusatz? z)
    {
        z ??= new();
        static string T(string? x, int n) { var t = (x ?? "").Trim(); return t.Length > n ? t[..n] : t; }
        var von = DateOnly.TryParse(z.Von, out var v) ? Plandienst.Iso(v) : "";
        var bis = DateOnly.TryParse(z.Bis, out var bi) ? Plandienst.Iso(bi) : "";
        if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(bis, von) < 0) throw new ArgumentException("Das Ende des Zeitraums liegt vor dem Beginn.");
        var mail = T(z.Email, 200);
        if (mail.Length > 0 && !mail.Contains('@')) throw new ArgumentException("Bitte eine gültige E-Mail-Adresse eingeben.");
        return new(von, bis, Math.Clamp(z.Jahresstunden, 0, 2000), T(z.Abteilung, 100), T(z.Verantwortlich, 100), T(z.Aktivitaetsart, 60), T(z.Anmerkung, 1000), mail, z.EmailBenachrichtigung);
    }

    /// <summary>Unterricht löschen; seine noch kommenden zusätzlichen Stunden werden storniert.</summary>
    public void UnterrichtLoeschen(Benutzer b, int id)
    {
        var u = EigenerUnterrichtHolen(id) ?? throw new ArgumentException("Unbekannter Unterricht.");
        if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && u.BenutzerId != b.Id) throw new UnauthorizedAccessException("Nur selbst angelegten Unterricht löschen.");
        _db.Ausfuehren("UPDATE buchungen SET status = 'storniert', storniert = @z WHERE unterricht_id = @id AND status = 'aktiv' AND datum >= @h",
            Db.P("@z", Jetzt), Db.P("@id", id), Db.P("@h", Plandienst.Iso(_uhr.Heute)));
        _db.Ausfuehren("UPDATE eigene_unterrichte SET geloescht = 1 WHERE id = @id", Db.P("@id", id));
    }
}
