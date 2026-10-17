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
    List<string> Ressourcen, List<int> RessourcenartIds, int KapazitaetUntPlan);

public sealed record BuchungsRessource(int Id, string Name, int Menge);

/// <summary>Einzeltermin einer Buchung. Eine Serie (Periodizität) besteht aus mehreren Buchungen mit derselben Serien-ID.</summary>
public sealed record Buchung(int Id, string Serie, string Art, string Datum, int StundeVon, int StundeBis, string Beginn, string Ende, string Raum,
    List<string> Klassen, string Lehrer, string Fach, string Titel, string Text, int? UnterrichtId, bool Benachrichtigen, string Status, string? Uebertragen,
    int? BenutzerId, string Absender, string Erstellt, string? Storniert)
{
    public List<BuchungsRessource> Ressourcen { get; init; } = new();
    public string ArtName => Buchungsarten.Name(Art);
}

/// <summary>In UntPlanWeb angelegter Unterricht (Unterricht | Neuer Unterricht), dessen Stunden als „Zusätzliche Stunden“ gebucht werden.</summary>
public sealed record EigenerUnterricht(int Id, string Lehrer, string Fach, List<string> Klassen, string Gruppe, string Text, int Wochenstunden, int? BenutzerId,
    string Erstellt, int GebuchteStunden = 0);

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
    List<string>? Klassen = null, string? Lehrer = null, string? Fach = null, string? Titel = null, string? Text = null, bool Benachrichtigen = false, int? UnterrichtId = null);

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
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer De = StringComparer.Create(new CultureInfo("de-DE"), true);
    /// <summary>Prüfen und Speichern hintereinander (keine Doppelbuchung durch gleichzeitige Anfragen).</summary>
    private static readonly object Sperre = new();
    public const int MaxTermine = 200;
    /// <summary>Präfix der Rückmeldungs-Id einer Buchung (Schnittstelle zu UntPlan).</summary>
    public const string RueckmeldungsPraefix = "buchung-";

    public Buchungsdienst(Db db, Uhr uhr, Plandienst plan, Rueckmeldungsdienst rm, Mitteilungsdienst mitteilungen, Benutzerdienst benutzer)
    {
        _db = db; _uhr = uhr; _plan = plan; _rm = rm; _mitteilungen = mitteilungen; _benutzer = benutzer;
    }

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
        var extra = _db.Liste("SELECT * FROM raum_daten", r => (Raum: r.Text("raum"), Kap: r.Zahl("kapazitaet"), Geb: r.Text("gebaeude"), Abt: r.Text("abteilung"), Gruppe: r.Text("raumgruppe")))
            .GroupBy(x => x.Raum, Ci).ToDictionary(g => g.Key, g => g.First(), Ci);
        var fest = Ressourcen(true).Where(x => !x.Mobil).GroupBy(x => x.Raum, Ci).ToDictionary(g => g.Key, g => g.ToList(), Ci);
        var buchbar = Rueckmeldungsdienst.Buchbar(s).Select(r => r.Kurz).ToHashSet(Ci);
        return s.Paket.Elemente.Raeume.Where(r => r.Kurz.Length > 0).DistinctBy(r => r.Kurz, Ci).OrderBy(r => r.Kurz, De).Select(r =>
        {
            extra.TryGetValue(r.Kurz, out var x);
            var res = fest.TryGetValue(r.Kurz, out var l) ? l : new List<Ressource>();
            return new Raumdaten(r.Kurz, r.Name, r.Raumart, x.Kap > 0 ? x.Kap : r.Kapazitaet, x.Geb ?? "", x.Abt ?? "", x.Gruppe ?? "", buchbar.Contains(r.Kurz),
                res.Select(y => y.Name).ToList(), res.Where(y => y.ArtId.HasValue).Select(y => y.ArtId!.Value).Distinct().ToList(), r.Kapazitaet);
        }).ToList();
    }

    public void RaumdatenSpeichern(Stand s, string raum, int? kapazitaet, string? gebaeude, string? abteilung, string? raumgruppe)
    {
        if (!s.Raeume.TryGetValue(raum ?? "", out var r)) throw new ArgumentException($"Unbekannter Raum {raum}.");
        static string T(string? x) { var t = (x ?? "").Trim(); return t.Length > 100 ? t[..100] : t; }
        _db.Ausfuehren("DELETE FROM raum_daten WHERE raum = @r", Db.P("@r", r.Kurz));
        _db.Ausfuehren("INSERT INTO raum_daten (raum, kapazitaet, gebaeude, abteilung, raumgruppe, geaendert) VALUES (@r, @k, @g, @a, @rg, @z)",
            Db.P("@r", r.Kurz), Db.P("@k", Math.Clamp(kapazitaet ?? 0, 0, 9999)), Db.P("@g", T(gebaeude)), Db.P("@a", T(abteilung)), Db.P("@rg", T(raumgruppe)), Db.P("@z", Jetzt));
    }

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
        if (res.Count > MaxTermine) throw new ArgumentException($"Höchstens {MaxTermine} Termine auf einmal – bitte den Zeitraum verkleinern.");
        return (res, weg);
    }

    // ------------------------------------------------------------------------------------------ Belegung

    /// <summary>Belegung aller Räume an einem Tag: Unterricht laut Plan, Klausuren, Termine mit Raum, Vormerkungen, Raumsperren und Buchungen.
    /// ohneUnterricht: Stunden und Vormerkungen dieses Unterrichts zählen nicht (eigene Stunde bei der Raumwahl einer Prüfung).</summary>
    public List<Belegt> RaumBelegung(Stand s, DateOnly d, int? ohneBuchung = null, int? ohneUnterricht = null)
    {
        var res = new List<Belegt>();
        var paket = _plan.Rohstunden(s, d);
        foreach (var r in paket.Concat(_rm.Vorgemerkt(d, s, mitBuchungen: false)))
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
        // Buchungen aus UntPlanWeb (nach der Übernahme in UntPlan stehen sie bereits im Paket)
        foreach (var b in AktiveAm(d).Where(b => b.Id != ohneBuchung && b.Raum.Length > 0 && !SchonImPaket(b, paket)))
            for (var st = b.StundeVon; st <= b.StundeBis; st++)
                res.Add(new Belegt(st, b.Raum, "buchung", $"{(b.Titel.Length > 0 ? b.Titel : b.ArtName)} ({b.Absender})", b.Id));
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
    private Dictionary<(int Ressource, int Stunde), int> RessourcenGebucht(DateOnly d, int? ohneBuchung = null)
    {
        var res = new Dictionary<(int, int), int>();
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
        var gebucht = termine.Select(t => cache.TryGetValue(t.Datum, out var x) ? x : cache[t.Datum] = RessourcenGebucht(t.Datum)).ToList();
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
    public List<string> Hinweise(Stand s, IEnumerable<TerminWahl> termine, string? lehrer, IEnumerable<string>? klassen)
    {
        var kl = (klassen ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        var l = (lehrer ?? "").Trim();
        // je Element die Termine mit Überschneidung (zusammengefasst, damit eine Serie nicht viele gleiche Zeilen erzeugt)
        var treffer = new Dictionary<string, List<DateOnly>>();
        void Merken(string name, DateOnly d) => (treffer.TryGetValue(name, out var x) ? x : treffer[name] = new()).Add(d);
        foreach (var t in termine.Take(MaxTermine))
        {
            var roh = _plan.Rohstunden(s, t.Datum).Where(r => r.Status != "entfall" && r.Stunde >= t.StundeVon && r.Stunde <= t.StundeBis).ToList();
            var buchungen = AktiveAm(t.Datum).Where(b => b.StundeVon <= t.StundeBis && b.StundeBis >= t.StundeVon).ToList();
            if (l.Length > 0 && (roh.Any(r => r.Lehrer.Contains(l, Ci)) || buchungen.Any(b => Ci.Equals(b.Lehrer, l)))) Merken(l, t.Datum);
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

        EigenerUnterricht? eu = null;
        if (art == Buchungsarten.Unterricht)
        {
            eu = EigenerUnterrichtHolen(d.UnterrichtId ?? 0) ?? throw new ArgumentException("Unbekannter Unterricht.");
            if (!Rechtedienst.Darf(b, Recht.BuchungAndere) && eu.BenutzerId != b.Id && !Ci.Equals(eu.Lehrer, b.LehrerKuerzel ?? "")) throw new UnauthorizedAccessException("Nur für eigenen Unterricht.");
        }
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
                if (t.Raum is string raum)
                {
                    if (!s.Raeume.TryGetValue(raum, out var rw)) throw new ArgumentException($"Unbekannter Raum {raum}.");
                    if (!buchbar.Contains(rw.Kurz)) throw new ArgumentException($"Raum {rw.Kurz} ist nicht buchbar.");
                    var belegt = RaumBelegung(s, t.Datum).Where(x => x.Stunde >= t.StundeVon && x.Stunde <= t.StundeBis && Ci.Equals(x.Raum, rw.Kurz)).ToList();
                    if (belegt.Count > 0) konflikte.Add($"Raum {rw.Kurz} ist am {tag} belegt: {string.Join(", ", belegt.Select(x => x.Text).Distinct())}");
                }
                if (ressourcen.Count > 0)
                {
                    var gebucht = RessourcenGebucht(t.Datum);
                    foreach (var (r, menge) in ressourcen.Where(x => Verfuegbar(x.R, gebucht, t.StundeVon, t.StundeBis) < x.Menge))
                        konflikte.Add($"{r.Name} ist am {tag} nicht {(menge > 1 ? $"{menge}× " : "")}verfügbar.");
                }
                if (konflikte.Count >= 10) break;
            }
            if (konflikte.Count > 0) throw new ArgumentException(string.Join("\n", konflikte));

            var serie = termine.Count > 1 ? Guid.NewGuid().ToString("N")[..12] : "";
            var ids = new List<int>();
            _db.Transaktion(c =>
            {
                foreach (var t in termine)
                {
                    var raum = t.Raum is null ? "" : s.Raeume[t.Raum].Kurz;
                    Db.Ausfuehren(c, """
                        INSERT INTO buchungen (serie, art, datum, stunde_von, stunde_bis, beginn, ende, raum, klassen, lehrer, fach, titel, text, unterricht_id,
                            benachrichtigen, status, benutzer_id, absender, erstellt)
                        VALUES (@s, @a, @d, @v, @b, @zb, @ze, @r, @k, @l, @f, @t, @x, @u, @n, 'aktiv', @bid, @abs, @e)
                        """, Db.P("@s", serie), Db.P("@a", art), Db.P("@d", Plandienst.Iso(t.Datum)), Db.P("@v", t.StundeVon), Db.P("@b", t.StundeBis),
                        Db.P("@zb", Beginn(t.StundeVon)), Db.P("@ze", Ende(t.StundeBis)), Db.P("@r", raum), Db.P("@k", JsonSerializer.Serialize(klassen)), Db.P("@l", lehrer),
                        Db.P("@f", fach), Db.P("@t", titel), Db.P("@x", text), Db.P("@u", eu?.Id), Db.P("@n", d.Benachrichtigen), Db.P("@bid", b.Id),
                        Db.P("@abs", b.LehrerKuerzel ?? b.Login), Db.P("@e", Jetzt));
                    var id = (int)Db.LetzteId(c);
                    ids.Add(id);
                    foreach (var (r, menge) in ressourcen)
                        Db.Ausfuehren(c, "INSERT INTO buchung_ressourcen (buchung_id, ressource_id, menge) VALUES (@b, @r, @m)", Db.P("@b", id), Db.P("@r", r.Id), Db.P("@m", menge));
                }
            });
            var neu = ids.Select(i => Holen(i)!).ToList();
            var hinweise = new List<string>();
            if (art == Buchungsarten.Aktivitaet && d.Benachrichtigen && lehrer.Length > 0) hinweise.AddRange(Benachrichtigen(s, b, neu));
            return (neu, hinweise);
        }
    }

    /// <summary>Lehrkraft der Aktivität über den Mitteilungsdienst benachrichtigen (alle Konten mit diesem Kürzel außer dem eigenen).</summary>
    private List<string> Benachrichtigen(Stand s, Benutzer b, List<Buchung> neu)
    {
        var erste = neu[0];
        var konten = _benutzer.Alle().Where(x => !x.Gesperrt && x.Id != b.Id && Ci.Equals(x.LehrerKuerzel ?? "", erste.Lehrer)).ToList();
        if (konten.Count == 0)
            return erste.Lehrer == b.LehrerKuerzel ? new() : new() { $"Für {erste.Lehrer} gibt es kein Benutzerkonto – keine Benachrichtigung." };
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
        r.Text("erstellt"), r.TextOderNull("storniert"));

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
            SELECT {Spalten} FROM buchungen WHERE datum >= @von AND datum <= @bis AND (@alle = 1 OR benutzer_id = @b OR (lehrer <> '' AND lehrer = @l))
            ORDER BY datum, stunde_von, id LIMIT 3000
            """, Lesen, Db.P("@von", Plandienst.Iso(von)), Db.P("@bis", Plandienst.Iso(bis)), Db.P("@alle", alle && Rechtedienst.Darf(b, Recht.BuchungAndere)), Db.P("@b", b.Id), Db.P("@l", b.LehrerKuerzel ?? "\u0001")));

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
            var lehrer = b.Lehrer.Length > 0 ? new List<string> { b.Lehrer } : new List<string>();
            var raeume = b.Raum.Length > 0 ? new List<string> { b.Raum } : new List<string>();
            for (var st = b.StundeVon; st <= b.StundeBis; st++)
                yield return new Rohstunde(st, null, b.Fach, b.Klassen, lehrer, raeume, "zusaetzlich", null, b.Titel, art);
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
        _db.Liste("SELECT * FROM buchungen WHERE status = 'aktiv' AND uebertragen IS NULL AND art <> @r ORDER BY erstellt, id", Lesen, Db.P("@r", Buchungsarten.Ressource))
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
        Raum = b.Raum.Length > 0 ? b.Raum : null, Raeume = b.Raum.Length > 0 ? new() { b.Raum } : new(), Klassen = b.Klassen,
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
                    .Select(g => new { stunde = g.Key, art = g.First().Art, text = string.Join(" · ", g.Select(x => x.Text).Distinct()), buchung = g.Select(x => x.BuchungId).FirstOrDefault(x => x.HasValue) })),
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

    // ------------------------------------------------------------------------------------------ Neuer Unterricht

    private EigenerUnterricht UnterrichtLesen(DbDataReader r) => new(r.Zahl("id"), r.Text("lehrer"), r.Text("fach"), JsonListe(r.Text("klassen")), r.Text("gruppe"),
        r.Text("text"), r.Zahl("wochenstunden"), r.ZahlOderNull("benutzer_id"), r.Text("erstellt"), r.Zahl("gebucht"));

    private const string UnterrichtSql = """
        SELECT u.*, (SELECT COALESCE(SUM(b.stunde_bis - b.stunde_von + 1), 0) FROM buchungen b WHERE b.unterricht_id = u.id AND b.status = 'aktiv') AS gebucht
        FROM eigene_unterrichte u WHERE u.geloescht = 0
        """;

    public EigenerUnterricht? EigenerUnterrichtHolen(int id) => _db.Liste(UnterrichtSql + " AND u.id = @id", UnterrichtLesen, Db.P("@id", id)).FirstOrDefault();

    /// <summary>Eigene Unterrichte (angelegt bzw. als Lehrkraft eingetragen); die Administration sieht alle.</summary>
    public List<EigenerUnterricht> EigeneUnterrichte(Benutzer b) =>
        _db.Liste(UnterrichtSql + " AND (@alle = 1 OR u.benutzer_id = @b OR u.lehrer = @l) ORDER BY u.lehrer, u.fach, u.id", UnterrichtLesen,
            Db.P("@alle", Rechtedienst.Darf(b, Recht.BuchungAndere)), Db.P("@b", b.Id), Db.P("@l", b.LehrerKuerzel ?? "\u0001"));

    public EigenerUnterricht UnterrichtAnlegen(Stand s, Benutzer b, string? lehrer, string? fach, IEnumerable<string>? klassen, string? gruppe, string? text, int? wochenstunden)
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
            INSERT INTO eigene_unterrichte (lehrer, fach, klassen, gruppe, text, wochenstunden, benutzer_id, erstellt, geloescht)
            VALUES (@l, @f, @k, @g, @t, @w, @b, @e, 0)
            """, Db.P("@l", lw.Kurz), Db.P("@f", fw.Kurz), Db.P("@k", JsonSerializer.Serialize(k)), Db.P("@g", T(gruppe, 100)), Db.P("@t", T(text, 1000)),
            Db.P("@w", Math.Clamp(wochenstunden ?? 0, 0, 40)), Db.P("@b", b.Id), Db.P("@e", Jetzt));
        return EigenerUnterrichtHolen((int)id)!;
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
