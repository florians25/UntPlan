using System.Text;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Freier Tag bzw. Ferienzeitraum, in UntPlanWeb gepflegt (ergänzt die Ferien aus UntPlan).</summary>
public sealed record FreierTag(int Id, string Von, string Bis, string Art, string Name);

/// <summary>Eintrag im Übertragungsverlauf (je empfangenem Paket).</summary>
public sealed record Paketversion(int Id, int Revision, string Empfangen, string Programmversion, int Klassen, int Lehrer, int Raeume, int Tage, int Vertretungen, string Von, string Bis);

/// <summary>Auf diesem Gerät gemerktes Konto (Konto wechseln).</summary>
public sealed record GeraeteKonto(int Id, int BenutzerId, string Login, string Anzeigename, string Rolle, string Zuletzt);

/// <summary>
/// Verwaltungsfunktionen der Administration, die über die Synchronisation hinausgehen:
/// <list type="bullet">
/// <item>Einstellungen als JSON (Ansicht, Prüfungssperren, Monitoransichten, Zuordnung, Sicherheitsrichtlinie).</item>
/// <item>freie Tage &amp; Ferien, Übertragungsverlauf.</item>
/// <item>Kontowechsel auf einem Gerät, Massenaktionen und CSV-Import für Benutzer.</item>
/// </list>
/// </summary>
public sealed class Verwaltungsdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Einstellungen _einstellungen;
    private readonly Benutzerdienst _benutzer;
    private readonly Paketdienst _paket;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Erlaubte Einstellungsbereiche mit Standardwerten.</summary>
    public static readonly Dictionary<string, Dictionary<string, object>> Bereiche = new()
    {
        ["ansicht"] = new() { ["nachrichtenAnzeigen"] = true, ["rss"] = false, ["wochenende"] = false, ["startseite"] = "heute", ["kartenZeiten"] = false },
        ["pruefungssperren"] = new() { ["aktiv"] = false, ["maxProTag"] = 2, ["maxProWoche"] = 4, ["leitungAusnehmen"] = true },
        ["monitor"] = new() { ["ticker"] = true, ["kopfzeile"] = true, ["aktualisierung"] = 60, ["nurOeffentlich"] = false },
        ["zuordnung"] = new() { ["leitungKuerzel"] = "", ["klassenkonten"] = true },
        ["verspaetung"] = new() { ["abwesendAbMinuten"] = 0, ["minutenUmrechnen"] = true, ["minutenProFehlstunde"] = 45, ["hinweisAbAnzahl"] = 3, ["vorzeitigRestTag"] = true },
        ["passwort"] = new() { ["minLaenge"] = 8, ["grossbuchstaben"] = false, ["ziffern"] = false, ["sonderzeichen"] = false },
    };

    public Verwaltungsdienst(Db db, Uhr uhr, Einstellungen einstellungen, Benutzerdienst benutzer, Paketdienst paket)
    {
        _db = db; _uhr = uhr; _einstellungen = einstellungen; _benutzer = benutzer; _paket = paket;
    }

    // ------------------------------------------------------------------------------------------ Einstellungen

    /// <summary>Einstellungen eines Bereichs (gespeicherte Werte über den Standardwerten).</summary>
    public Dictionary<string, JsonElement> Einstellung(string bereich)
    {
        if (!Bereiche.TryGetValue(bereich, out var standard)) throw new ArgumentException("Unbekannter Einstellungsbereich.");
        var res = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(standard))!;
        var json = _einstellungen.Lesen("einstellung:" + bereich);
        if (json != null)
            try
            {
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new())
                    if (res.ContainsKey(k)) res[k] = v;
            }
            catch (JsonException) { /* Standardwerte */ }
        return res;
    }

    public void EinstellungSpeichern(string bereich, Dictionary<string, JsonElement> werte)
    {
        if (!Bereiche.TryGetValue(bereich, out var standard)) throw new ArgumentException("Unbekannter Einstellungsbereich.");
        var neu = new Dictionary<string, object?>();
        foreach (var (k, v0) in standard)
        {
            if (!werte.TryGetValue(k, out var v)) { neu[k] = Einstellung(bereich)[k]; continue; }
            neu[k] = v0 switch
            {
                bool => v.ValueKind is JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0) || (v.ValueKind == JsonValueKind.String && v.GetString() is "1" or "true"),
                int => v.ValueKind == JsonValueKind.Number ? Math.Clamp(v.GetInt32(), 0, 100000) : int.TryParse(v.GetString(), out var i) ? Math.Clamp(i, 0, 100000) : (int)v0,
                _ => (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())?.Trim() ?? "",
            };
        }
        _einstellungen.Schreiben("einstellung:" + bereich, JsonSerializer.Serialize(neu));
    }

    public bool Wahr(string bereich, string schluessel) => Einstellung(bereich).TryGetValue(schluessel, out var v) && v.ValueKind == JsonValueKind.True;
    public int Zahl(string bereich, string schluessel) => Einstellung(bereich).TryGetValue(schluessel, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    public string Text(string bereich, string schluessel) => Einstellung(bereich).TryGetValue(schluessel, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ------------------------------------------------------------------------------------------ Freie Tage & Ferien

    public List<FreierTag> FreieTage() => _db.Liste("SELECT * FROM freie_tage ORDER BY von, id",
        r => new FreierTag(r.Zahl("id"), r.Text("von"), r.Text("bis"), r.Text("art"), r.Text("name")));

    public FreierTag FreienTagSpeichern(int? id, DateOnly von, DateOnly bis, string art, string name)
    {
        if (bis < von) throw new ArgumentException("Das Ende liegt vor dem Beginn.");
        if (bis.DayNumber - von.DayNumber > 120) throw new ArgumentException("Bitte höchstens 120 Tage auf einmal eintragen.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte eine Bezeichnung eingeben.");
        art = art is "ferien" or "feiertag" ? art : "frei";
        var p = new[] { Db.P("@v", von), Db.P("@b", bis), Db.P("@a", art), Db.P("@n", name.Trim()), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO freie_tage (von, bis, art, name, erstellt) VALUES (@v, @b, @a, @n, @e)", p);
        else if (_db.Ausfuehren("UPDATE freie_tage SET von = @v, bis = @b, art = @a, name = @n WHERE id = @id", p) == 0) throw new ArgumentException("Unbekannter Eintrag.");
        _paket.ZusatzferienLaden();
        return FreieTage().First(f => f.Id == id);
    }

    public void FreienTagLoeschen(int id)
    {
        _db.Ausfuehren("DELETE FROM freie_tage WHERE id = @id", Db.P("@id", id));
        _paket.ZusatzferienLaden();
    }

    // ------------------------------------------------------------------------------------------ Übertragungsverlauf

    public List<Paketversion> Verlauf(int anzahl = 100) => _db.Liste("SELECT * FROM paket_verlauf ORDER BY id DESC LIMIT @n",
        r => new Paketversion(r.Zahl("id"), r.Zahl("revision"), r.Text("empfangen"), r.Text("programmversion"), r.Zahl("klassen"), r.Zahl("lehrer"),
            r.Zahl("raeume"), r.Zahl("tage"), r.Zahl("vertretungen"), r.Text("von"), r.Text("bis")), Db.P("@n", anzahl));

    // ------------------------------------------------------------------------------------------ Konto wechseln

    /// <summary>Konto auf dem Gerät merken (nach erfolgreicher Anmeldung).</summary>
    public void KontoMerken(string geraet, int benutzerId)
    {
        if (geraet.Length < 20) return;
        _db.Ausfuehren("""
            INSERT INTO geraete_konten (geraet, benutzer_id, zuletzt) VALUES (@g, @b, @z)
            ON CONFLICT(geraet, benutzer_id) DO UPDATE SET zuletzt = excluded.zuletzt
            """, Db.P("@g", Sicherheit.Hash(geraet)), Db.P("@b", benutzerId), Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    /// <summary>Gemerkte Konten des Geräts (höchstens 30 Tage seit der letzten Anmeldung).</summary>
    public List<GeraeteKonto> Konten(string? geraet)
    {
        if (string.IsNullOrEmpty(geraet)) return new();
        var grenze = _uhr.Jetzt.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss");
        return _db.Liste("""
            SELECT g.id, g.benutzer_id, g.zuletzt, b.login, b.anzeigename, b.rolle FROM geraete_konten g JOIN benutzer b ON b.id = g.benutzer_id
            WHERE g.geraet = @g AND g.zuletzt >= @z AND b.gesperrt = 0 ORDER BY g.zuletzt DESC
            """, r => new GeraeteKonto(r.Zahl("id"), r.Zahl("benutzer_id"), r.Text("login"), r.Text("anzeigename"), r.Text("rolle"), r.Text("zuletzt")),
            Db.P("@g", Sicherheit.Hash(geraet)), Db.P("@z", grenze));
    }

    public Benutzer? KontoFuerWechsel(string? geraet, int id) =>
        Konten(geraet).FirstOrDefault(k => k.Id == id) is { } k ? _benutzer.Holen(k.BenutzerId) is { Gesperrt: false } b ? b : null : null;

    public void KontoVergessen(string? geraet, int id)
    {
        if (string.IsNullOrEmpty(geraet)) return;
        _db.Ausfuehren("DELETE FROM geraete_konten WHERE geraet = @g AND id = @id", Db.P("@g", Sicherheit.Hash(geraet)), Db.P("@id", id));
    }

    // ------------------------------------------------------------------------------------------ Benutzer: Massenaktionen und Import

    /// <summary>
    /// Massenaktion für ausgewählte Benutzer: sperren, entsperren, löschen, E-Mail aus den Stammdaten übernehmen;
    /// „inaktive_sperren“ sperrt Konten, deren Kürzel nicht mehr in den Stammdaten steht.
    /// </summary>
    public int Massenaktion(Benutzer ich, string aktion, IEnumerable<int> ids)
    {
        var s = _paket.Aktuell;
        var auswahl = aktion == "inaktive_sperren" ? _benutzer.Alle().Select(b => b.Id).ToList() : ids.Distinct().ToList();
        var n = 0;
        foreach (var id in auswahl)
        {
            if (_benutzer.Holen(id) is not { } b) continue;
            switch (aktion)
            {
                case "sperren":
                    if (b.Id == ich.Id) continue;
                    _benutzer.Sperren(b.Id, true); n++; break;
                case "entsperren":
                    _benutzer.Sperren(b.Id, false); n++; break;
                case "loeschen":
                    if (b.Id == ich.Id) continue;
                    _benutzer.Loeschen(b.Id); n++; break;
                case "email_uebernehmen":
                    if (s != null && b.LehrerKuerzel is string l && s.Lehrer.TryGetValue(l, out var le) && !string.IsNullOrWhiteSpace(le.Email) && le.Email != b.Email)
                    { _benutzer.EmailSetzen(b.Id, le.Email!); n++; }
                    break;
                case "inaktive_sperren":
                    if (s is null || b.Gesperrt || b.IstAdmin || b.Element.Length == 0) continue;
                    var vorhanden = b.Familie ? s.Klassen.ContainsKey(b.Element) || b.SchuelerIds.Any(i => s.Schueler.Any(x => x.Id == i)) : s.Lehrer.ContainsKey(b.Element);
                    if (!vorhanden) { _benutzer.Sperren(b.Id, true); n++; }
                    break;
                default: throw new ArgumentException("Unbekannte Aktion.");
            }
        }
        return n;
    }

    public sealed record Importzeile(int Zeile, string Login, string Anzeigename, string Rolle, string Element, string Email, string? Passwort, string? Fehler, bool Vorhanden);

    /// <summary>
    /// CSV-Import für Benutzer (Semikolon oder Komma, UTF-8 oder Windows-1252). Spalten: Benutzername;Name;Rolle;Kürzel;E-Mail[;Passwort].
    /// Rolle als admin/leitung/lehrer/schueler oder Klartext (Lehrkraft, Schulleitung …). Ohne Passwort wird ein Startpasswort erzeugt.
    /// </summary>
    public List<Importzeile> ImportPruefen(byte[] daten)
    {
        if (daten.Length > 5 * 1024 * 1024) throw new ArgumentException("Die Datei ist größer als 5 MB.");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(daten); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(daten); }
        text = text.TrimStart('﻿');
        var zeilen = text.Split('\n').Select(z => z.TrimEnd('\r')).ToList();
        if (zeilen.Count > 20000) throw new ArgumentException("Höchstens 20.000 Zeilen.");
        var trenner = zeilen.FirstOrDefault(z => z.Length > 0)?.Count(c => c == ';') >= 1 ? ';' : ',';
        var res = new List<Importzeile>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < zeilen.Count; i++)
        {
            var z = zeilen[i];
            if (string.IsNullOrWhiteSpace(z)) continue;
            var f = z.Split(trenner).Select(x => x.Trim().Trim('"')).ToList();
            if (res.Count == 0 && (f[0].Equals("benutzername", StringComparison.OrdinalIgnoreCase) || f[0].Equals("login", StringComparison.OrdinalIgnoreCase))) continue;
            string F(int n) => n < f.Count ? f[n] : "";
            var rolle = RolleAus(F(2));
            string? fehler = null;
            if (F(0).Length < 2 || F(0).Any(char.IsWhiteSpace)) fehler = "Benutzername fehlt oder enthält Leerzeichen";
            else if (rolle is null) fehler = $"Unbekannte Rolle „{F(2)}“";
            else if (!gesehen.Add(F(0))) fehler = "Benutzername doppelt in der Datei";
            else if (F(5).Length > 0 && F(5).Length < 8) fehler = "Passwort kürzer als 8 Zeichen";
            res.Add(new Importzeile(i + 1, F(0), F(1), rolle ?? "", F(3), F(4), F(5).Length > 0 ? F(5) : null, fehler, fehler is null && _benutzer.NachLogin(F(0)) != null));
        }
        return res;
    }

    private static string? RolleAus(string s) => s.Trim().ToLowerInvariant() switch
    {
        "admin" or "administration" => Rollen.Admin,
        "leitung" or "schulleitung" => Rollen.Leitung,
        "lehrer" or "lehrkraft" or "lehrkräfte" or "lehrerin" or "" => Rollen.Lehrer,
        "schueler" or "schüler" or "schüler*in" or "klasse" or "schüler*innen" => Rollen.Schueler,
        "eltern" or "erziehungsberechtigte" or "erziehungsberechtigte*r" or "erziehungsberechtigter" => Rollen.Eltern,
        _ => null,
    };

    /// <summary>Geprüfte Zeilen übernehmen. ersetzen: vorhandene Konten werden aktualisiert (Name, Rolle, Kürzel, E-Mail), sonst übersprungen.</summary>
    public (List<(string Login, string Passwort)> Neu, int Aktualisiert, int Uebersprungen) ImportUebernehmen(List<Importzeile> zeilen, bool ersetzen)
    {
        if (zeilen.Any(z => z.Fehler != null)) throw new ArgumentException("Die Datei enthält Fehler. Bitte zuerst korrigieren.");
        var neu = new List<(string, string)>();
        int akt = 0, ueber = 0;
        foreach (var z in zeilen)
        {
            if (_benutzer.NachLogin(z.Login) is { } b)
            {
                if (!ersetzen) { ueber++; continue; }
                _benutzer.Aendern(b.Id, z.Anzeigename.Length > 0 ? z.Anzeigename : b.Anzeigename, b.IstAdmin ? Rollen.Admin : z.Rolle, z.Element, z.Email.Length > 0 ? z.Email : b.Email, b.Gesperrt);
                akt++;
                continue;
            }
            var pw = z.Passwort ?? Sicherheit.Zufall(10);
            _benutzer.Anlegen(z.Login, pw, z.Rolle, z.Element, z.Anzeigename, z.Email, z.Passwort is null);
            neu.Add((z.Login, z.Passwort is null ? pw : "(aus der Datei)"));
        }
        return (neu, akt, ueber);
    }
}
