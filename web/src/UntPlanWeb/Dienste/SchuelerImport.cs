using System.Globalization;
using System.Text;
using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Feld des Schüler-Imports (Ziel einer CSV-Spalte).</summary>
public sealed record ImportFeld(string Id, string Name, string Gruppe, string[] Synonyme);

/// <summary>Gelesene CSV-Datei: erkannte Kodierung und Trennzeichen, Zeilen als Felder.</summary>
public sealed record CsvTabelle(string Kodierung, string Trennzeichen, List<List<string>> Zeilen);

/// <summary>Optionen des Abgleichs.</summary>
/// <param name="Modus">„neu“ = nur neue anlegen, „aktualisieren“ = neue anlegen und vorhandene aktualisieren.</param>
/// <param name="Schluessel">„auto“ (ID/externe Nummer, sonst Name + Geburtsdatum), „id“ oder „name_geburt“.</param>
/// <param name="AustrittMarkieren">Vorhandene Schüler*innen, die nicht in der Datei stehen, als ausgetreten markieren.</param>
public sealed record AbgleichOptionen(string? Modus = "aktualisieren", string? Schluessel = "auto", bool AustrittMarkieren = false,
    bool KontenSchueler = false, bool KontenEltern = false, string? Schema = null);

/// <summary>Anfrage für Trockenlauf bzw. Übernahme: Zeilen der Datei, Kopfzeile, Zuordnung Spaltennummer → Feld.</summary>
public sealed record ImportAnfrage(List<List<string>>? Zeilen, bool Kopfzeile, Dictionary<string, string>? Zuordnung, AbgleichOptionen? Optionen);

/// <summary>Ergebnis einer Zeile: neu, geaendert, unveraendert, uebersprungen, fehler bzw. (für nicht enthaltene) ausgetreten.</summary>
public sealed record ImportErgebnis(int Zeile, string Status, int? Id, string Name, string Klasse, List<string> Aenderungen, string? Fehler, List<string> Hinweise);

/// <summary>Gespeicherte Zuordnungs-Vorlage (Spaltenüberschrift bzw. Spaltennummer → Feld).</summary>
public sealed record ImportVorlage(string Name, bool Kopfzeile, Dictionary<string, string> Ueberschriften, Dictionary<string, string> Spalten);

/// <summary>
/// Schülerdaten: Quelle (Export aus UntPlan oder Web-Verwaltung) und komplexer CSV-Import mit Spaltenzuordnung, Vorlagen, Abgleich,
/// Trockenlauf und Übernahme (optional mit Erziehungsberechtigten, Klassendienst und Konten).
/// </summary>
public sealed class SchuelerImport
{
    public const string QuelleExport = "export", QuelleWeb = "web";
    private const string QuelleSchluessel = "schueler_quelle", VorlagenSchluessel = "schuelerimport_vorlagen";
    public const int MaxBytes = 5 * 1024 * 1024, MaxZeilen = 20000;

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Paketdienst _pakete;
    private readonly Personendienst _personen;
    private readonly Klassenbuchverwaltung _kv;
    private readonly Einstellungen _einstellungen;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SchuelerImport(Db db, Uhr uhr, Paketdienst pakete, Personendienst personen, Klassenbuchverwaltung kv, Einstellungen einstellungen)
    {
        _db = db; _uhr = uhr; _pakete = pakete; _personen = personen; _kv = kv; _einstellungen = einstellungen;
    }

    // ---------------------------------------------------------------- Quelle der Schülerdaten

    /// <summary>Web-Verwaltung: der Export aus UntPlan überschreibt die Schüler*innen nicht (Paketdienst übernimmt die Liste dann nicht).</summary>
    public static bool WebVerwaltung(Db db)
    {
        try { return db.Wert("SELECT wert FROM einstellungen WHERE schluessel = @s", Db.P("@s", QuelleSchluessel)) as string == QuelleWeb; }
        catch (System.Data.Common.DbException) { return false; }
    }

    public string Quelle => WebVerwaltung(_db) ? QuelleWeb : QuelleExport;

    /// <summary>
    /// Quelle umstellen. Beim Wechsel zur Web-Verwaltung werden die bisher übertragenen Schüler*innen in die Web-Verwaltung übernommen,
    /// damit sie erhalten bleiben; beim Wechsel zurück gilt wieder die Liste aus dem Export (mit den Änderungen aus UntPlanWeb).
    /// </summary>
    public int QuelleSetzen(string quelle, int benutzerId)
    {
        if (quelle is not (QuelleExport or QuelleWeb)) throw new ArgumentException("Unbekannte Quelle der Schülerdaten.");
        var uebernommen = 0;
        if (quelle == QuelleWeb && !WebVerwaltung(_db) && _pakete.Aktuell is { } s)
        {
            var vorhanden = _db.Liste("SELECT id FROM schueler_daten", r => r.Zahl("id")).ToHashSet();
            var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
            _db.Transaktion(c =>
            {
                foreach (var x in s.Schueler.Where(x => !vorhanden.Contains(x.Id)))
                {
                    Db.Ausfuehren(c, "INSERT INTO schueler_daten (id, vorname, name, klasse, eigen, geloescht, geaendert, benutzer_id) VALUES (@id, @v, @n, @k, 0, 0, @z, @u)",
                        Db.P("@id", x.Id), Db.P("@v", x.Vorname), Db.P("@n", x.Name), Db.P("@k", x.Klasse), Db.P("@z", jetzt), Db.P("@u", benutzerId));
                    uebernommen++;
                }
            });
        }
        _einstellungen.Schreiben(QuelleSchluessel, quelle);
        _pakete.SchuelerLaden();
        return uebernommen;
    }

    // ---------------------------------------------------------------- Felder

    public static readonly ImportFeld[] Felder =
    {
        new("nachname", "Nachname", "Schüler*in", new[] { "nachname", "name", "familienname", "lastname", "surname", "schuelername" }),
        new("vorname", "Vorname", "Schüler*in", new[] { "vorname", "rufname", "firstname", "givenname" }),
        new("geburtsdatum", "Geburtsdatum", "Schüler*in", new[] { "geburtsdatum", "geburtstag", "gebdatum", "gebdat", "geb", "geboren", "birthday", "dateofbirth" }),
        new("geschlecht", "Geschlecht", "Schüler*in", new[] { "geschlecht", "sex", "gender", "mw" }),
        new("klasse", "Klasse", "Schüler*in", new[] { "klasse", "kl", "klassenbezeichnung", "class", "lerngruppe" }),
        new("id", "Schüler-ID (UntPlan/Web)", "Schüler*in", new[] { "id", "schuelerid", "schueler_id" }),
        new("extern", "Externe Nummer", "Schüler*in", new[] { "externenummer", "extern", "externeid", "schuelernummer", "schuelernr", "nummer", "nr", "sid", "kennung" }),
        new("email", "E-Mail", "Schüler*in", new[] { "email", "mail", "emailadresse" }),
        new("eintritt", "Eintritt", "Schüler*in", new[] { "eintritt", "eintrittsdatum", "aufnahme", "aufnahmedatum", "zugang" }),
        new("austritt", "Austritt", "Schüler*in", new[] { "austritt", "austrittsdatum", "abgang", "abgangsdatum" }),
        new("bemerkung", "Bemerkung", "Schüler*in", new[] { "bemerkung", "notiz", "hinweis" }),
        new("klassendienst", "Klassendienst", "Schüler*in", new[] { "klassendienst", "dienst" }),
        new("eb1_name", "Erziehungsberechtigte*r 1: Name", "Erziehungsberechtigte", new[] { "eb1", "eb1name", "erziehungsberechtigter1", "erziehungsberechtigte1", "erziehungsberechtigte1name", "eltern1", "eltern1name" }),
        new("eb1_vorname", "Erziehungsberechtigte*r 1: Vorname", "Erziehungsberechtigte", new[] { "eb1vorname", "erziehungsberechtigte1vorname", "eltern1vorname" }),
        new("eb1_email", "Erziehungsberechtigte*r 1: E-Mail", "Erziehungsberechtigte", new[] { "eb1email", "eb1mail", "erziehungsberechtigte1email", "eltern1email" }),
        new("eb1_telefon", "Erziehungsberechtigte*r 1: Telefon", "Erziehungsberechtigte", new[] { "eb1telefon", "eb1tel", "erziehungsberechtigte1telefon", "eltern1telefon" }),
        new("eb2_name", "Erziehungsberechtigte*r 2: Name", "Erziehungsberechtigte", new[] { "eb2", "eb2name", "erziehungsberechtigter2", "erziehungsberechtigte2", "erziehungsberechtigte2name", "eltern2", "eltern2name" }),
        new("eb2_vorname", "Erziehungsberechtigte*r 2: Vorname", "Erziehungsberechtigte", new[] { "eb2vorname", "erziehungsberechtigte2vorname", "eltern2vorname" }),
        new("eb2_email", "Erziehungsberechtigte*r 2: E-Mail", "Erziehungsberechtigte", new[] { "eb2email", "eb2mail", "erziehungsberechtigte2email", "eltern2email" }),
        new("eb2_telefon", "Erziehungsberechtigte*r 2: Telefon", "Erziehungsberechtigte", new[] { "eb2telefon", "eb2tel", "erziehungsberechtigte2telefon", "eltern2telefon" }),
    };

    /// <summary>Überschrift vereinheitlichen: Kleinbuchstaben, Umlaute umschrieben, nur Buchstaben und Ziffern.</summary>
    public static string Normalisieren(string s)
    {
        s = s.Trim().ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Zuordnung aus den Überschriften vorschlagen (Spaltennummer → Feld).</summary>
    public static Dictionary<string, string> Vorschlag(IReadOnlyList<string> kopf)
    {
        var res = new Dictionary<string, string>();
        var vergeben = new HashSet<string>();
        for (var i = 0; i < kopf.Count; i++)
        {
            var n = Normalisieren(kopf[i]);
            if (n.Length == 0) continue;
            var f = Felder.FirstOrDefault(f => !vergeben.Contains(f.Id) && f.Synonyme.Contains(n));
            if (f is null) continue;
            res[i.ToString()] = f.Id;
            vergeben.Add(f.Id);
        }
        return res;
    }

    /// <summary>Sieht die erste Zeile wie eine Kopfzeile aus (bekannte Überschriften bzw. keine Ziffern bei Ziffern in Zeile 2)?</summary>
    public static bool KopfzeileVermuten(List<List<string>> zeilen)
    {
        if (zeilen.Count == 0) return false;
        if (Vorschlag(zeilen[0]).Count >= 2) return true;
        return zeilen.Count > 1 && !zeilen[0].Any(z => z.Any(char.IsDigit)) && zeilen[1].Any(z => z.Any(char.IsDigit));
    }

    // ---------------------------------------------------------------- CSV lesen

    /// <summary>Text erkennen: UTF-8 (mit oder ohne BOM), UTF-16 mit BOM, sonst Windows-1252. <paramref name="kodierung"/>: auto, utf-8, windows-1252.</summary>
    public static (string Text, string Kodierung) Dekodieren(byte[] daten, string? kodierung = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1252 = Encoding.GetEncoding(1252);
        switch ((kodierung ?? "auto").ToLowerInvariant())
        {
            case "utf-8" or "utf8": return (new UTF8Encoding(false).GetString(daten).TrimStart('﻿'), "UTF-8");
            case "windows-1252" or "cp1252" or "ansi" or "latin1": return (cp1252.GetString(daten), "Windows-1252");
        }
        if (daten.Length >= 3 && daten[0] == 0xEF && daten[1] == 0xBB && daten[2] == 0xBF) return (Encoding.UTF8.GetString(daten, 3, daten.Length - 3), "UTF-8");
        if (daten.Length >= 2 && daten[0] == 0xFF && daten[1] == 0xFE) return (Encoding.Unicode.GetString(daten, 2, daten.Length - 2), "UTF-16");
        if (daten.Length >= 2 && daten[0] == 0xFE && daten[1] == 0xFF) return (Encoding.BigEndianUnicode.GetString(daten, 2, daten.Length - 2), "UTF-16");
        try { return (new UTF8Encoding(false, true).GetString(daten), "UTF-8"); }
        catch (DecoderFallbackException) { return (cp1252.GetString(daten), "Windows-1252"); }
    }

    /// <summary>Trennzeichen erkennen: ; , oder Tab – das Zeichen, das in den ersten Zeilen (außerhalb von Anführungszeichen) am gleichmäßigsten vorkommt.</summary>
    public static char TrennzeichenErkennen(string text)
    {
        var zeilen = text.Split('\n').Select(z => z.TrimEnd('\r')).Where(z => z.Trim().Length > 0).Take(20).ToList();
        if (zeilen.Count == 0) return ';';
        char beste = ';';
        var wert = -1.0;
        foreach (var t in new[] { ';', '\t', ',' })
        {
            var anzahl = zeilen.Select(z => Zaehlen(z, t)).ToList();
            if (anzahl[0] == 0) continue;
            var gleich = anzahl.Count(a => a == anzahl[0]);
            var w = gleich * 1000.0 + anzahl[0];
            if (w > wert) { wert = w; beste = t; }
        }
        return beste;

        static int Zaehlen(string z, char t)
        {
            int n = 0; var inQ = false;
            foreach (var c in z) { if (c == '"') inQ = !inQ; else if (c == t && !inQ) n++; }
            return n;
        }
    }

    /// <summary>CSV zerlegen (Anführungszeichen mit "" als Escape, Zeilenumbrüche in Anführungszeichen); leere Zeilen entfallen.</summary>
    public static List<List<string>> Zerlegen(string text, char trenner)
    {
        var res = new List<List<string>>();
        var zeile = new List<string>();
        var feld = new StringBuilder();
        var inQ = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else feld.Append(c);
            }
            else if (c == '"' && feld.ToString().Trim().Length == 0) { feld.Clear(); inQ = true; }
            else if (c == trenner) { zeile.Add(feld.ToString().Trim()); feld.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                zeile.Add(feld.ToString().Trim()); feld.Clear();
                if (zeile.Any(f => f.Length > 0)) res.Add(zeile);
                zeile = new List<string>();
                if (res.Count > MaxZeilen) throw new ArgumentException($"Höchstens {MaxZeilen:N0} Zeilen.");
            }
            else feld.Append(c);
        }
        zeile.Add(feld.ToString().Trim());
        if (zeile.Any(f => f.Length > 0)) res.Add(zeile);
        if (res.Count > MaxZeilen) throw new ArgumentException($"Höchstens {MaxZeilen:N0} Zeilen.");
        return res;
    }

    /// <summary>Datei lesen: Kodierung und Trennzeichen automatisch (oder vorgegeben: ";", ",", "tab").</summary>
    public static CsvTabelle Lesen(byte[] daten, string? kodierung = null, string? trennzeichen = null)
    {
        if (daten.Length == 0) throw new ArgumentException("Die Datei ist leer.");
        if (daten.Length > MaxBytes) throw new ArgumentException("Die Datei ist größer als 5 MB.");
        var (text, kod) = Dekodieren(daten, kodierung);
        if (text.Contains('\0')) throw new ArgumentException("Die Datei ist keine Textdatei (CSV/TXT).");
        var t = trennzeichen switch { ";" => ';', "," => ',', "tab" or "\t" => '\t', _ => TrennzeichenErkennen(text) };
        var zeilen = Zerlegen(text, t);
        if (zeilen.Count == 0) throw new ArgumentException("Die Datei enthält keine Daten.");
        return new CsvTabelle(kod, t == '\t' ? "tab" : t.ToString(), zeilen);
    }

    // ---------------------------------------------------------------- Werte

    /// <summary>Datum lesen: TT.MM.JJJJ, T.M.JJ, JJJJ-MM-TT, TT/MM/JJJJ → JJJJ-MM-TT; leer = "", ungültig = null.</summary>
    public static string? Datum(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.Contains(' ')) s = s.Split(' ')[0];
        var formate = new[] { "d.M.yyyy", "d.M.yy", "yyyy-MM-dd", "d/M/yyyy", "yyyy/MM/dd", "yyyyMMdd" };
        if (DateOnly.TryParseExact(s, formate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            if (d.Year < 1900) return null;
            return d.ToString("yyyy-MM-dd");
        }
        return null;
    }

    /// <summary>Geschlecht lesen: m/w/d (auch männlich, weiblich, divers, male, female, x); leer = "", unbekannt = null.</summary>
    public static string? Geschlecht(string? s) => (s ?? "").Trim().Length == 0 ? "" : Normalisieren(s!) switch
    {
        "m" or "maennlich" or "male" or "mann" or "junge" or "j" => "m",
        "w" or "weiblich" or "female" or "f" or "frau" or "maedchen" => "w",
        "d" or "divers" or "x" or "diverse" or "o" or "ohne" => "d",
        _ => null,
    };

    /// <summary>Name einer Person: „Nachname, Vorname“, „Vorname Nachname“ oder getrennt angegeben.</summary>
    public static (string Vorname, string Name) Personenname(string name, string vorname)
    {
        name = name.Trim(); vorname = vorname.Trim();
        if (vorname.Length > 0 || name.Length == 0) return (vorname, name);
        if (name.Contains(','))
        {
            var t = name.Split(',', 2);
            return (t[1].Trim(), t[0].Trim());
        }
        var i = name.LastIndexOf(' ');
        return i > 0 ? (name[..i].Trim(), name[(i + 1)..].Trim()) : ("", name);
    }

    // ---------------------------------------------------------------- Vorlagen

    public List<ImportVorlage> Vorlagen()
    {
        var json = _einstellungen.Lesen(VorlagenSchluessel);
        try { return json is null ? new() : JsonSerializer.Deserialize<List<ImportVorlage>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public ImportVorlage VorlageSpeichern(ImportVorlage v)
    {
        var name = (v.Name ?? "").Trim();
        if (name.Length is < 1 or > 100) throw new ArgumentException("Bitte einen Namen für die Vorlage eingeben.");
        var felder = Felder.Select(f => f.Id).ToHashSet();
        var neu = new ImportVorlage(name, v.Kopfzeile,
            (v.Ueberschriften ?? new()).Where(x => felder.Contains(x.Value) && x.Key.Length <= 200).Take(100).ToDictionary(x => x.Key, x => x.Value),
            (v.Spalten ?? new()).Where(x => felder.Contains(x.Value) && int.TryParse(x.Key, out var i) && i is >= 0 and < 500).ToDictionary(x => x.Key, x => x.Value));
        var liste = Vorlagen().Where(x => !Ci.Equals(x.Name, name)).Append(neu).OrderBy(x => x.Name, Ci).ToList();
        if (liste.Count > 50) throw new ArgumentException("Höchstens 50 Vorlagen.");
        _einstellungen.Schreiben(VorlagenSchluessel, JsonSerializer.Serialize(liste, Json));
        return neu;
    }

    public void VorlageLoeschen(string name) =>
        _einstellungen.Schreiben(VorlagenSchluessel, JsonSerializer.Serialize(Vorlagen().Where(x => !Ci.Equals(x.Name, name)).ToList(), Json));

    // ---------------------------------------------------------------- Abgleich

    private sealed record Bestand(int Id, string Vorname, string Name, string Klasse, string Geschlecht, string Geburtsdatum, string Email, string Bemerkung,
        string Extern, string Eintritt, string Austritt, bool Zeile);

    private sealed record Eintrag(int Zeile, Dictionary<string, string> W, int? Id, string? Fehler, List<string> Hinweise);

    private sealed record Plan(List<ImportErgebnis> Ergebnisse, List<(Eintrag E, Bestand? Alt, string Status, Dictionary<string, string> Neu)> Aktionen, List<Bestand> Austritte);

    private Dictionary<int, Bestand> BestandLaden(Stand s)
    {
        var zeilen = _db.Liste("SELECT * FROM schueler_daten WHERE geloescht = 0", r => new Bestand(r.Zahl("id"), r.Text("vorname"), r.Text("name"), r.Text("klasse"),
            r.Text("geschlecht"), r.Text("geburtsdatum"), r.Text("email"), r.Text("bemerkung"), r.Text("extern_id"), r.Text("eintritt"), r.Text("austritt"), true)).ToDictionary(x => x.Id);
        var res = new Dictionary<int, Bestand>();
        foreach (var x in s.Schueler)
            res[x.Id] = zeilen.TryGetValue(x.Id, out var z) ? z with { Vorname = x.Vorname, Name = x.Name, Klasse = x.Klasse }
                : new Bestand(x.Id, x.Vorname, x.Name, x.Klasse, "", "", "", "", "", "", "", false);
        return res;
    }

    private static string Wert(Bestand b, string feld) => feld switch
    {
        "nachname" => b.Name, "vorname" => b.Vorname, "klasse" => b.Klasse, "geschlecht" => b.Geschlecht, "geburtsdatum" => b.Geburtsdatum,
        "email" => b.Email, "bemerkung" => b.Bemerkung, "extern" => b.Extern, "eintritt" => b.Eintritt, "austritt" => b.Austritt, _ => "",
    };

    private static readonly string[] Stammfelder = { "nachname", "vorname", "klasse", "geschlecht", "geburtsdatum", "email", "extern", "eintritt", "austritt", "bemerkung" };

    private static string NameSchluessel(string name, string vorname, string geburt) => $"{Normalisieren(name)}|{Normalisieren(vorname)}|{geburt}";

    /// <summary>Zeilen lesen und prüfen (Pflichtfelder, Klasse, Datum, Geschlecht, E-Mail).</summary>
    private List<Eintrag> Eintraege(Stand s, ImportAnfrage a)
    {
        var zuordnung = (a.Zuordnung ?? new()).Where(x => int.TryParse(x.Key, out _) && Felder.Any(f => f.Id == x.Value))
            .GroupBy(x => x.Value).ToDictionary(g => g.Key, g => int.Parse(g.First().Key));
        if (!zuordnung.ContainsKey("nachname")) throw new ArgumentException("Bitte mindestens die Spalte „Nachname“ zuordnen.");
        if (!zuordnung.ContainsKey("klasse") && !zuordnung.ContainsKey("id") && !zuordnung.ContainsKey("extern"))
            throw new ArgumentException("Bitte die Spalte „Klasse“ zuordnen (oder eine ID-Spalte für reine Aktualisierungen).");
        var zeilen = a.Zeilen ?? new();
        if (zeilen.Count > MaxZeilen + 1) throw new ArgumentException($"Höchstens {MaxZeilen:N0} Zeilen.");
        var res = new List<Eintrag>();
        for (var i = a.Kopfzeile ? 1 : 0; i < zeilen.Count; i++)
        {
            var z = zeilen[i] ?? new();
            if (z.All(string.IsNullOrWhiteSpace)) continue;
            var w = new Dictionary<string, string>();
            foreach (var (feld, sp) in zuordnung) w[feld] = (sp < z.Count ? z[sp] ?? "" : "").Trim();
            var hinweise = new List<string>();
            string? fehler = null;
            int? id = null;
            if (w.GetValueOrDefault("nachname", "").Length == 0) fehler = "Nachname fehlt";
            else if (w["nachname"].Length > 100 || w.GetValueOrDefault("vorname", "").Length > 100) fehler = "Name zu lang";
            if (fehler is null && w.TryGetValue("klasse", out var kl) && kl.Length > 0)
            {
                if (s.Klassen.TryGetValue(kl, out var k)) w["klasse"] = k.Kurz;
                else fehler = $"Unbekannte Klasse „{kl}“";
            }
            foreach (var df in new[] { "geburtsdatum", "eintritt", "austritt" })
                if (fehler is null && w.TryGetValue(df, out var dv))
                {
                    var d = Datum(dv);
                    if (d is null) fehler = $"Ungültiges Datum „{dv}“ ({Felder.First(f => f.Id == df).Name})";
                    else w[df] = d;
                }
            if (fehler is null && w.TryGetValue("geschlecht", out var gv))
            {
                var g = Geschlecht(gv);
                if (g is null) fehler = $"Unbekanntes Geschlecht „{gv}“";
                else w["geschlecht"] = g;
            }
            foreach (var mf in new[] { "email", "eb1_email", "eb2_email" })
                if (fehler is null && w.TryGetValue(mf, out var mv) && mv.Length > 0 && (mv.Length > 200 || !mv.Contains('@') || mv.Contains(' ')))
                    fehler = $"Ungültige E-Mail-Adresse „{mv}“";
            if (fehler is null && w.TryGetValue("id", out var iv) && iv.Length > 0)
            {
                if (int.TryParse(iv, out var n) && n > 0) id = n;
                else fehler = $"Ungültige Schüler-ID „{iv}“";
            }
            if (w.TryGetValue("extern", out var ev) && ev.Length > 50) fehler ??= "Externe Nummer zu lang";
            res.Add(new Eintrag(i + 1, w, id, fehler, hinweise));
        }
        return res;
    }

    private Plan Planen(Stand s, ImportAnfrage a)
    {
        var o = a.Optionen ?? new AbgleichOptionen();
        var modus = o.Modus is "neu" ? "neu" : "aktualisieren";
        var schluessel = o.Schluessel is "id" or "name_geburt" ? o.Schluessel : "auto";
        var bestand = BestandLaden(s);
        var nachExtern = bestand.Values.Where(b => b.Extern.Length > 0).GroupBy(b => b.Extern, Ci).ToDictionary(g => g.Key, g => g.First(), Ci);
        var nachName = bestand.Values.GroupBy(b => NameSchluessel(b.Name, b.Vorname, b.Geburtsdatum)).ToDictionary(g => g.Key, g => g.ToList());
        var nachNameOhneGeburt = bestand.Values.Where(b => b.Geburtsdatum.Length == 0).GroupBy(b => NameSchluessel(b.Name, b.Vorname, "")).ToDictionary(g => g.Key, g => g.ToList());
        var ergebnisse = new List<ImportErgebnis>();
        var aktionen = new List<(Eintrag, Bestand?, string, Dictionary<string, string>)>();
        var getroffen = new HashSet<int>();
        var dateiSchluessel = new HashSet<string>(Ci);
        foreach (var e in Eintraege(s, a))
        {
            var name = $"{e.W.GetValueOrDefault("nachname", "")}, {e.W.GetValueOrDefault("vorname", "")}".Trim(' ', ',');
            if (e.Fehler != null) { ergebnisse.Add(new ImportErgebnis(e.Zeile, "fehler", null, name, e.W.GetValueOrDefault("klasse", ""), new(), e.Fehler, e.Hinweise)); continue; }
            Bestand? alt = null;
            var mitId = schluessel is "auto" or "id";
            var mitName = schluessel is "auto" or "name_geburt";
            if (mitId && e.Id is int id)
            {
                if (bestand.TryGetValue(id, out var b)) alt = b;
                else if (schluessel == "id") e.Hinweise.Add($"Schüler-ID {id} unbekannt – wird neu angelegt");
            }
            if (alt is null && mitId && e.W.GetValueOrDefault("extern", "") is { Length: > 0 } ex && nachExtern.TryGetValue(ex, out var be)) alt = be;
            var nk = NameSchluessel(e.W["nachname"], e.W.GetValueOrDefault("vorname", ""), e.W.GetValueOrDefault("geburtsdatum", ""));
            if (alt is null && mitName)
            {
                if (nachName.TryGetValue(nk, out var l) && l.Count == 1) alt = l[0];
                else if (nachName.TryGetValue(nk, out var l2) && l2.Count > 1)
                    alt = l2.FirstOrDefault(x => Ci.Equals(x.Klasse, e.W.GetValueOrDefault("klasse", ""))) ?? null;
                else if (nachNameOhneGeburt.TryGetValue(NameSchluessel(e.W["nachname"], e.W.GetValueOrDefault("vorname", ""), ""), out var l3) && l3.Count == 1)
                    alt = l3[0];
            }
            var doppelSchluessel = alt != null ? "id:" + alt.Id : e.W.GetValueOrDefault("extern", "") is { Length: > 0 } x2 ? "ex:" + x2 : "nk:" + nk;
            if (!dateiSchluessel.Add(doppelSchluessel))
            {
                ergebnisse.Add(new ImportErgebnis(e.Zeile, "fehler", alt?.Id, name, e.W.GetValueOrDefault("klasse", ""), new(), "Doppelt in der Datei", e.Hinweise));
                continue;
            }
            if (alt is null)
            {
                if (e.W.GetValueOrDefault("klasse", "").Length == 0)
                {
                    ergebnisse.Add(new ImportErgebnis(e.Zeile, "fehler", null, name, "", new(), "Klasse fehlt (neue Schülerin bzw. neuer Schüler)", e.Hinweise));
                    continue;
                }
                var neu = Stammfelder.Where(e.W.ContainsKey).ToDictionary(f => f, f => e.W[f]);
                Zusatz(e, null);
                ergebnisse.Add(new ImportErgebnis(e.Zeile, "neu", null, name, e.W["klasse"], new(), null, e.Hinweise));
                aktionen.Add((e, null, "neu", neu));
                continue;
            }
            getroffen.Add(alt.Id);
            if (modus == "neu")
            {
                ergebnisse.Add(new ImportErgebnis(e.Zeile, "uebersprungen", alt.Id, name, alt.Klasse, new(), null, e.Hinweise.Append("bereits vorhanden").ToList()));
                continue;
            }
            var aenderungen = new List<string>();
            var werte = new Dictionary<string, string>();
            foreach (var f in Stammfelder.Where(e.W.ContainsKey))
            {
                var nw = e.W[f];
                // leere Felder überschreiben nur die Klasse nicht; sonst gilt die Datei
                if (f == "klasse" && nw.Length == 0) continue;
                if (f is "nachname" && nw.Length == 0) continue;
                if (!string.Equals(Wert(alt, f), nw, StringComparison.Ordinal))
                {
                    aenderungen.Add($"{Felder.First(x => x.Id == f).Name}: „{Wert(alt, f)}“ → „{nw}“");
                    werte[f] = nw;
                }
            }
            Zusatz(e, alt.Id);
            var status = aenderungen.Count > 0 ? "geaendert" : "unveraendert";
            ergebnisse.Add(new ImportErgebnis(e.Zeile, status, alt.Id, name, werte.GetValueOrDefault("klasse", alt.Klasse), aenderungen, null, e.Hinweise));
            aktionen.Add((e, alt, status, werte));
        }
        var austritte = o.AustrittMarkieren ? bestand.Values.Where(b => !getroffen.Contains(b.Id)).OrderBy(b => b.Klasse, Ci).ThenBy(b => b.Name, Ci).ToList() : new();
        foreach (var b in austritte)
            ergebnisse.Add(new ImportErgebnis(0, "ausgetreten", b.Id, $"{b.Name}, {b.Vorname}".Trim(' ', ','), b.Klasse, new() { "nicht in der Datei – wird als ausgetreten markiert" }, null, new()));
        return new Plan(ergebnisse, aktionen, austritte);

        void Zusatz(Eintrag e, int? id)
        {
            foreach (var n in new[] { 1, 2 })
                if (e.W.GetValueOrDefault($"eb{n}_name", "") is { Length: > 0 } eb)
                {
                    var (v, nn) = Personenname(eb, e.W.GetValueOrDefault($"eb{n}_vorname", ""));
                    e.Hinweise.Add($"Erziehungsberechtigte*r {n}: {string.Join(" ", new[] { v, nn }.Where(x => x.Length > 0))}");
                }
            if (e.W.GetValueOrDefault("klassendienst", "") is { Length: > 0 } kd)
            {
                var dienst = _kv.Dienstarten().FirstOrDefault(d => Ci.Equals(d.Kurz, kd) || Ci.Equals(d.Name, kd));
                e.Hinweise.Add(dienst is null ? $"Klassendienst „{kd}“ unbekannt – wird nicht eingetragen" : $"Klassendienst: {dienst.Name}");
            }
            if (e.W.GetValueOrDefault("austritt", "") is { Length: > 0 } au && string.CompareOrdinal(au, Plandienst.Iso(_uhr.Heute)) <= 0)
                e.Hinweise.Add("Austritt erreicht – wird als ausgetreten geführt");
        }
    }

    /// <summary>Trockenlauf: Ergebnis je Zeile, ohne etwas zu speichern.</summary>
    public object Pruefen(ImportAnfrage a)
    {
        var s = _pakete.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen (Klassen fehlen).");
        var p = Planen(s, a);
        return Zusammenfassung(p.Ergebnisse, null);
    }

    private object Zusammenfassung(List<ImportErgebnis> l, List<NeuesKonto>? konten) => new
    {
        quelle = Quelle,
        neu = l.Count(x => x.Status == "neu"), geaendert = l.Count(x => x.Status == "geaendert"), unveraendert = l.Count(x => x.Status == "unveraendert"),
        uebersprungen = l.Count(x => x.Status == "uebersprungen"), fehler = l.Count(x => x.Status == "fehler"), ausgetreten = l.Count(x => x.Status == "ausgetreten"),
        zeilen = l,
        konten = konten?.Select(k => new { k.Login, k.Passwort, k.Rolle, rollenname = Rollen.Name(k.Rolle), k.Name, k.Zuordnung, k.Klasse }),
        csv = konten is { Count: > 0 } ? Personendienst.Csv(konten) : null,
    };

    /// <summary>Übernehmen: Zeilen mit Fehlern werden übersprungen. Liefert die Zusammenfassung samt neu angelegter Konten.</summary>
    public object Uebernehmen(Benutzer ich, ImportAnfrage a)
    {
        var s = _pakete.Aktuell ?? throw new ArgumentException("Es wurden noch keine Daten aus UntPlan übertragen (Klassen fehlen).");
        var o = a.Optionen ?? new AbgleichOptionen();
        var p = Planen(s, a);
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");
        var heute = Plandienst.Iso(_uhr.Heute);
        var ids = new Dictionary<int, int>(); // Zeile → Schüler-ID
        _db.Transaktion(c =>
        {
            var naechste = Math.Max(Schuelerverwaltung.ErsteEigeneId, Convert.ToInt32(Db.Wert(c, "SELECT COALESCE(MAX(id), 0) FROM schueler_daten") ?? 0) + 1);
            foreach (var (e, alt, status, w) in p.Aktionen)
            {
                if (status == "unveraendert") { ids[e.Zeile] = alt!.Id; continue; }
                var b = alt ?? new Bestand(0, "", "", "", "", "", "", "", "", "", "", false);
                string W(string f) => w.TryGetValue(f, out var v) ? v : Wert(b, f);
                var austritt = W("austritt");
                var geloescht = austritt.Length > 0 && string.CompareOrdinal(austritt, heute) <= 0;
                int id;
                if (alt is null)
                {
                    while (s.Schueler.Any(x => x.Id == naechste)) naechste++;
                    id = naechste++;
                }
                else id = alt.Id;
                var par = new[] { Db.P("@id", id), Db.P("@v", W("vorname")), Db.P("@n", W("nachname")), Db.P("@k", W("klasse")), Db.P("@g", W("geschlecht")),
                    Db.P("@gd", W("geburtsdatum")), Db.P("@e", W("email")), Db.P("@b", W("bemerkung")), Db.P("@x", W("extern")), Db.P("@ei", W("eintritt")),
                    Db.P("@au", austritt), Db.P("@ge", geloescht), Db.P("@eig", id >= Schuelerverwaltung.ErsteEigeneId), Db.P("@z", jetzt), Db.P("@u", ich.Id) };
                if (alt is null || Db.Ausfuehren(c, """
                        UPDATE schueler_daten SET vorname = @v, name = @n, klasse = @k, geschlecht = @g, geburtsdatum = @gd, email = @e, bemerkung = @b,
                            extern_id = @x, eintritt = @ei, austritt = @au, geloescht = @ge, geaendert = @z, benutzer_id = @u WHERE id = @id
                        """, par) == 0)
                    Db.Ausfuehren(c, """
                        INSERT INTO schueler_daten (id, vorname, name, klasse, geschlecht, geburtsdatum, email, bemerkung, extern_id, eintritt, austritt, eigen, geloescht, geaendert, benutzer_id)
                        VALUES (@id, @v, @n, @k, @g, @gd, @e, @b, @x, @ei, @au, @eig, @ge, @z, @u)
                        """, par);
                ids[e.Zeile] = id;
            }
            foreach (var b in p.Austritte)
            {
                var par = new[] { Db.P("@id", b.Id), Db.P("@v", b.Vorname), Db.P("@n", b.Name), Db.P("@k", b.Klasse), Db.P("@au", heute), Db.P("@eig", b.Id >= Schuelerverwaltung.ErsteEigeneId),
                    Db.P("@z", jetzt), Db.P("@u", ich.Id) };
                if (Db.Ausfuehren(c, "UPDATE schueler_daten SET austritt = @au, geloescht = 1, geaendert = @z, benutzer_id = @u WHERE id = @id", par) == 0)
                    Db.Ausfuehren(c, "INSERT INTO schueler_daten (id, vorname, name, klasse, austritt, eigen, geloescht, geaendert, benutzer_id) VALUES (@id, @v, @n, @k, @au, @eig, 1, @z, @u)", par);
            }
        });
        _pakete.SchuelerLaden();
        var stand = _pakete.Aktuell!;

        // Erziehungsberechtigte (Personenliste) und Klassendienst
        var elternIds = new HashSet<int>();
        var dienste = _kv.Dienstarten();
        foreach (var (e, _, _, _) in p.Aktionen)
        {
            if (!ids.TryGetValue(e.Zeile, out var sid) || !stand.Schueler.Any(x => x.Id == sid)) continue;
            foreach (var n in new[] { 1, 2 })
            {
                if (e.W.GetValueOrDefault($"eb{n}_name", "") is not { Length: > 0 } eb) continue;
                var (v, nn) = Personenname(eb, e.W.GetValueOrDefault($"eb{n}_vorname", ""));
                var mail = e.W.GetValueOrDefault($"eb{n}_email", "");
                var tel = e.W.GetValueOrDefault($"eb{n}_telefon", "");
                var vorhanden = _personen.Liste(Personendienst.Eltern).FirstOrDefault(x => Ci.Equals(x.Name, nn) && Ci.Equals(x.Vorname, v)
                    && (mail.Length == 0 || x.Email.Length == 0 || Ci.Equals(x.Email, mail)));
                Person person;
                if (vorhanden is null) person = _personen.Anlegen(new PersonEingabe(Personendienst.Eltern, v, nn, "", mail, new() { sid }));
                else
                {
                    var kinder = vorhanden.Kinder.Where(k => stand.Schueler.Any(x => x.Id == k)).Append(sid).Distinct().ToList();
                    person = _personen.Aendern(int.Parse(vorhanden.Schluessel), new PersonEingabe(Personendienst.Eltern, vorhanden.Vorname, vorhanden.Name, vorhanden.Funktion,
                        mail.Length > 0 ? mail : vorhanden.Email, kinder));
                }
                var pid = int.Parse(person.Schluessel);
                if (tel.Length > 0) _db.Ausfuehren("UPDATE personen SET telefon = @t WHERE id = @id", Db.P("@t", tel.Length > 100 ? tel[..100] : tel), Db.P("@id", pid));
                // Elternkonto vorhanden: Kind ergänzen
                if (person.Konto is { } k && Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM benutzer_schueler WHERE benutzer_id = @b AND schueler_id = @s", Db.P("@b", k.Id), Db.P("@s", sid))) == 0)
                    _db.Ausfuehren("INSERT INTO benutzer_schueler (benutzer_id, schueler_id) VALUES (@b, @s)", Db.P("@b", k.Id), Db.P("@s", sid));
                elternIds.Add(pid);
            }
            if (e.W.GetValueOrDefault("klassendienst", "") is { Length: > 0 } kd && dienste.FirstOrDefault(d => Ci.Equals(d.Kurz, kd) || Ci.Equals(d.Name, kd)) is { } dienst)
            {
                var bis = stand.SchuljahrBis == DateOnly.MaxValue || stand.SchuljahrBis < _uhr.Heute ? _uhr.Heute.AddYears(1) : stand.SchuljahrBis;
                if (!_kv.Dienste(stand, null, _uhr.Heute, bis, dienst.Id).Any(x => x.SchuelerId == sid))
                    try { _kv.DienstSpeichern(ich, stand, null, dienst.Id, sid, _uhr.Heute, bis, "Import"); }
                    catch (UnauthorizedAccessException) { e.Hinweise.Add("Klassendienst nicht eingetragen (fehlende Berechtigung)"); }
            }
        }

        // Konten über den Personendienst
        var konten = new List<NeuesKonto>();
        var aktive = ids.Values.Where(i => stand.Schueler.Any(x => x.Id == i)).Select(i => i.ToString()).ToList();
        if (o.KontenSchueler && aktive.Count > 0) konten.AddRange(_personen.KontenAnlegen(Personendienst.Schueler, aktive, null, o.Schema, null));
        if (o.KontenEltern && elternIds.Count > 0) konten.AddRange(_personen.KontenAnlegen(Personendienst.Eltern, elternIds.Select(i => i.ToString()), null, o.Schema, null));
        return Zusammenfassung(p.Ergebnisse, konten);
    }
}
