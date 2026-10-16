using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Rückmeldung (Absenzmeldung, Raumbuchung, Klausur, Nachricht) mit Bearbeitungsstand.</summary>
public sealed record Meldung(string Id, string Typ, string Erstellt, int? BenutzerId, string Absender, string? Lehrer, string? Von, string? Bis,
    int? StundeVon, int? StundeBis, string? Raum, List<string> Raeume, List<string> Klassen, List<string> WeitereLehrer, string? Titel, string? Grund,
    string? Text, string Status, string? Quittiert, int? Unterricht = null)
{
    public WebRueckmeldung AlsVertrag() => new()
    {
        Id = Id, Typ = Typ, Erstellt = DateTime.TryParse(Erstellt, out var e) ? e : DateTime.MinValue, Absender = Absender, Lehrer = Lehrer, Von = Von, Bis = Bis,
        StundeVon = StundeVon, StundeBis = StundeBis, Raum = Raum, Grund = Grund, Text = Text, Titel = Titel, Klassen = Klassen, Raeume = Raeume, WeitereLehrer = WeitereLehrer,
        UnterrichtNr = Unterricht,
    };
}

/// <summary>
/// Rückmeldungen an UntPlan: Lehrkräfte melden Abwesenheiten, buchen Räume und tragen Klausuren ein. UntPlan holt offene
/// Rückmeldungen ab (GET …/rueckmeldungen), übernimmt sie und quittiert sie. Bis dahin erscheinen Buchungen und Klausuren
/// als „vorgemerkt“ in den Plänen.
/// </summary>
public sealed class Rueckmeldungsdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Plandienst _plan;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    public Rueckmeldungsdienst(Db db, Uhr uhr, Plandienst plan) { _db = db; _uhr = uhr; _plan = plan; }

    private static List<string> Liste(string json) => string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<List<string>>(json) ?? new();

    private static Meldung Lesen(System.Data.Common.DbDataReader r) => new(r.Text("id"), r.Text("typ"), r.Text("erstellt"), r.ZahlOderNull("benutzer_id"), r.Text("absender"),
        r.TextOderNull("lehrer"), r.TextOderNull("von"), r.TextOderNull("bis"), r.ZahlOderNull("stunde_von"), r.ZahlOderNull("stunde_bis"), r.TextOderNull("raum"),
        Liste(r.Text("raeume")), Liste(r.Text("klassen")), Liste(r.Text("weitere_lehrer")), r.TextOderNull("titel"), r.TextOderNull("grund"), r.TextOderNull("text"),
        r.Text("status"), r.TextOderNull("quittiert"), r.ZahlOderNull("unterricht"));

    public List<Meldung> Offene() => _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' ORDER BY erstellt", Lesen);
    public List<Meldung> Alle(int max = 500) => _db.Liste("SELECT * FROM rueckmeldungen ORDER BY erstellt DESC LIMIT @n", Lesen, Db.P("@n", max));
    public List<Meldung> OffeneKlausuren() => _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND typ = 'klausur' ORDER BY von", Lesen);
    public List<Meldung> VonBenutzer(int id) => _db.Liste("SELECT * FROM rueckmeldungen WHERE benutzer_id = @b ORDER BY erstellt DESC LIMIT 100", Lesen, Db.P("@b", id));

    public int Quittieren(IEnumerable<string> ids)
    {
        var n = 0;
        foreach (var id in ids.Distinct())
            n += _db.Ausfuehren("UPDATE rueckmeldungen SET status = 'quittiert', quittiert = @z WHERE id = @id AND status = 'offen'",
                Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@id", id));
        return n;
    }

    public void StatusSetzen(string id, string status) =>
        _db.Ausfuehren("UPDATE rueckmeldungen SET status = @s WHERE id = @id", Db.P("@s", status), Db.P("@id", id));

    public void Loeschen(string id) => _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE id = @id", Db.P("@id", id));

    /// <summary>Eigene, noch nicht abgeholte Meldung zurückziehen.</summary>
    public bool Zurueckziehen(string id, int benutzer) =>
        _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE id = @id AND benutzer_id = @b AND status = 'offen'", Db.P("@id", id), Db.P("@b", benutzer)) > 0;

    private Meldung Speichern(Meldung m)
    {
        _db.Ausfuehren("""
            INSERT INTO rueckmeldungen (id, typ, erstellt, benutzer_id, absender, lehrer, von, bis, stunde_von, stunde_bis, raum, raeume, klassen, weitere_lehrer, titel, grund, text, status, unterricht)
            VALUES (@id, @typ, @e, @b, @a, @l, @von, @bis, @sv, @sb, @r, @rs, @k, @wl, @t, @g, @x, 'offen', @u)
            """, Db.P("@id", m.Id), Db.P("@typ", m.Typ), Db.P("@e", m.Erstellt), Db.P("@b", m.BenutzerId), Db.P("@a", m.Absender), Db.P("@l", m.Lehrer),
            Db.P("@von", m.Von), Db.P("@bis", m.Bis), Db.P("@sv", m.StundeVon), Db.P("@sb", m.StundeBis), Db.P("@r", m.Raum),
            Db.P("@rs", JsonSerializer.Serialize(m.Raeume)), Db.P("@k", JsonSerializer.Serialize(m.Klassen)), Db.P("@wl", JsonSerializer.Serialize(m.WeitereLehrer)),
            Db.P("@t", m.Titel), Db.P("@g", m.Grund), Db.P("@x", m.Text), Db.P("@u", m.Unterricht));
        return m;
    }

    private Meldung Neu(string typ, Benutzer b) => new(Guid.NewGuid().ToString("N"), typ, _uhr.Jetzt.ToString("yyyy-MM-ddTHH:mm:ss"), b.Id, b.LehrerKuerzel ?? b.Login,
        b.LehrerKuerzel, null, null, null, null, null, new(), new(), new(), null, null, null, "offen", null);

    private static (int, int) Stunden(Stand s, int? von, int? bis)
    {
        var max = s.Paket.Zeitraster.Stunden.Select(z => z.Nr).DefaultIfEmpty(12).Max();
        var v = Math.Clamp(von ?? 1, 1, max);
        var b = Math.Clamp(bis ?? v, v, max);
        return (v, b);
    }

    /// <summary>Abwesenheit melden (Lehrkraft für sich selbst).</summary>
    public Meldung Abwesenheit(Benutzer b, DateOnly von, DateOnly bis, int? stundeVon, int? stundeBis, string grund, string text)
    {
        if (b.LehrerKuerzel is null) throw new ArgumentException("Nur Lehrkräfte mit Kürzel können Abwesenheiten melden.");
        if (bis < von) throw new ArgumentException("Das Ende liegt vor dem Beginn.");
        if (von < _uhr.Heute.AddDays(-7)) throw new ArgumentException("Abwesenheiten können höchstens eine Woche rückwirkend gemeldet werden.");
        if (bis > von.AddDays(60)) throw new ArgumentException("Bitte höchstens 60 Tage auf einmal melden.");
        return Speichern(Neu(WebRueckmeldungsTypen.Absenz, b) with
        {
            Von = Plandienst.Iso(von), Bis = Plandienst.Iso(bis), StundeVon = stundeVon, StundeBis = stundeBis,
            Grund = string.IsNullOrWhiteSpace(grund) ? "Krankheit" : grund.Trim(), Text = text?.Trim(),
        });
    }

    public Meldung Nachricht(Benutzer b, DateOnly datum, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        return Speichern(Neu(WebRueckmeldungsTypen.Nachricht, b) with { Von = Plandienst.Iso(datum), Text = text.Trim() });
    }

    /// <summary>Raum ist in allen Stunden frei (kein Unterricht laut Plan, keine andere Buchung/Vormerkung)?</summary>
    public bool RaumFrei(Stand s, DateOnly d, string raum, int von, int bis, int? ohneUnterricht = null)
    {
        var belegt = _plan.Rohstunden(s, d).Concat(Vorgemerkt(d).Where(r => ohneUnterricht is null || r.Unterricht != ohneUnterricht))
            .Any(r => r.Stunde >= von && r.Stunde <= bis && r.Status != "entfall" && r.Raeume.Any(x => Plandienst.RaumListe(x).Contains(raum, Ci)) && (ohneUnterricht is null || r.Unterricht != ohneUnterricht));
        var gesperrt = s.Paket.Absenzen.Any(a => a.Art == "raum" && Ci.Equals(a.Element, raum) && DateOnly.TryParse(a.Von, out var av) && DateOnly.TryParse(a.Bis, out var ab) &&
                                                 d >= av && d <= ab && a.StundeVon <= bis && (a.StundeBis >= von || a.StundeBis < a.StundeVon));
        return !belegt && !gesperrt;
    }

    /// <summary>Räume, die gebucht werden dürfen (in UntPlan als „buchbar“ markiert). Ältere Pakete ohne Kennzeichen: alle.</summary>
    public static IEnumerable<WebRaum> Buchbar(Stand s) =>
        (s.Paket.Elemente.Raeume.Any(r => r.Buchbar) ? s.Paket.Elemente.Raeume.Where(r => r.Buchbar) : s.Paket.Elemente.Raeume)
            .Where(r => r.Kurz.Length > 0).DistinctBy(r => r.Kurz, Ci);

    /// <summary>Freie buchbare Räume für einen Zeitraum (für die Buchung).</summary>
    public List<object> FreieRaeume(Stand s, DateOnly d, int von, int bis) =>
        Buchbar(s).Where(r => RaumFrei(s, d, r.Kurz, von, bis))
            .OrderBy(r => r.Kurz, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true))
            .Select(r => (object)new { kurz = r.Kurz, name = r.Name, raumart = r.Raumart, kapazitaet = r.Kapazitaet }).ToList();

    public Meldung Raumbuchung(Stand s, Benutzer b, DateOnly d, int? stundeVon, int? stundeBis, string raum, IEnumerable<string> klassen, string titel, string text)
    {
        if (b.LehrerKuerzel is null && !b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Räume buchen.");
        if (d < _uhr.Heute) throw new ArgumentException("Räume können nicht rückwirkend gebucht werden.");
        if (!s.Raeume.ContainsKey(raum)) throw new ArgumentException($"Unbekannter Raum {raum}.");
        if (!Buchbar(s).Any(r => Ci.Equals(r.Kurz, raum))) throw new ArgumentException($"Raum {raum} ist nicht buchbar.");
        if (_plan.Frei(s, d).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        if (!RaumFrei(s, d, raum, v, bi)) throw new ArgumentException($"Raum {raum} ist in dieser Zeit nicht frei.");
        var k = klassen.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (k.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } unbekannt) throw new ArgumentException($"Unbekannte Klasse {unbekannt}.");
        return Speichern(Neu(WebRueckmeldungsTypen.Raumbuchung, b) with
        {
            Von = Plandienst.Iso(d), StundeVon = v, StundeBis = bi, Raum = s.Raeume[raum].Kurz, Raeume = new() { s.Raeume[raum].Kurz }, Klassen = k,
            Titel = string.IsNullOrWhiteSpace(titel) ? "Raumbuchung" : titel.Trim(), Text = text?.Trim(),
        });
    }

    /// <summary>
    /// Raumänderung einer Unterrichtsstunde aus den Stundendetails (Raum ersetzen, löschen oder zusätzlichen Raum buchen).
    /// raeume = vollständige neue Raumliste der Stunde. Es entsteht nur eine Raumbuchung zum Unterricht (UnterrichtNr) –
    /// ohne Vertretungstext und ohne zusätzliche Stunde. Frühere offene Buchungen derselben Stunde werden ersetzt;
    /// entspricht die Liste wieder den ursprünglichen Räumen, wird nur zurückgesetzt (Ergebnis null).
    /// </summary>
    public Meldung? Raumaenderung(Stand s, Benutzer b, DateOnly d, int stundeVon, int stundeBis, int unterricht, IEnumerable<string> raeume)
    {
        if (!b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Räume buchen.");
        if (d < _uhr.Heute) throw new ArgumentException("Räume können nicht rückwirkend gebucht werden.");
        if (_plan.Frei(s, d).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        var stunden = _plan.Rohstunden(s, d).Where(r => r.Unterricht == unterricht && r.Stunde >= v && r.Stunde <= bi && r.Status != "entfall").ToList();
        if (stunden.Count == 0) throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var bisher = stunden.SelectMany(r => r.Raeume).Distinct(Ci).ToList();
        var neu = raeume.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (neu.FirstOrDefault(x => !s.Raeume.ContainsKey(x)) is { } unbekannt) throw new ArgumentException($"Unbekannter Raum {unbekannt}.");
        neu = neu.Select(x => s.Raeume[x].Kurz).ToList();
        foreach (var x in neu.Where(x => !bisher.Contains(x, Ci)))
        {
            if (!Buchbar(s).Any(r => Ci.Equals(r.Kurz, x))) throw new ArgumentException($"Raum {x} ist nicht buchbar.");
            if (!RaumFrei(s, d, x, v, bi, unterricht)) throw new ArgumentException($"Raum {x} ist in dieser Zeit nicht frei.");
        }
        // frühere, noch nicht übernommene Raumänderungen derselben Stunde ersetzen
        _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE status = 'offen' AND typ = 'raumbuchung' AND unterricht = @u AND von = @d AND stunde_von <= @b AND stunde_bis >= @v",
            Db.P("@u", unterricht), Db.P("@d", Plandienst.Iso(d)), Db.P("@v", v), Db.P("@b", bi));
        if (neu.Count == bisher.Count && neu.All(x => bisher.Contains(x, Ci))) return null;
        var klassen = stunden.SelectMany(r => r.Klassen).Distinct(Ci).ToList();
        return Speichern(Neu(WebRueckmeldungsTypen.Raumbuchung, b) with
        {
            Von = Plandienst.Iso(d), StundeVon = v, StundeBis = bi, Raum = neu.FirstOrDefault(x => !bisher.Contains(x, Ci)), Raeume = neu, Klassen = klassen,
            Titel = null, Text = null, Unterricht = unterricht,
        });
    }

    public Meldung Klausur(Stand s, Benutzer b, DateOnly d, int? stundeVon, int? stundeBis, IEnumerable<string> klassen, IEnumerable<string> raeume, IEnumerable<string> aufsicht, string titel, string text)
    {
        if (!b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Klausuren eintragen.");
        if (d < _uhr.Heute) throw new ArgumentException("Klausuren können nicht rückwirkend eingetragen werden.");
        if (_plan.Frei(s, d).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        var k = klassen.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (k.Count == 0) throw new ArgumentException("Bitte mindestens eine Klasse angeben.");
        if (k.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } uk) throw new ArgumentException($"Unbekannte Klasse {uk}.");
        var r = raeume.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (r.FirstOrDefault(x => !s.Raeume.ContainsKey(x)) is { } ur) throw new ArgumentException($"Unbekannter Raum {ur}.");
        var l = aufsicht.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (l.FirstOrDefault(x => !s.Lehrer.ContainsKey(x)) is { } ul) throw new ArgumentException($"Unbekannte Lehrkraft {ul}.");
        var haupt = l.FirstOrDefault() ?? b.LehrerKuerzel;
        return Speichern(Neu(WebRueckmeldungsTypen.Klausur, b) with
        {
            Von = Plandienst.Iso(d), StundeVon = v, StundeBis = bi, Klassen = k, Raeume = r, Lehrer = haupt, WeitereLehrer = l.Where(x => !Ci.Equals(x, haupt)).ToList(),
            Titel = string.IsNullOrWhiteSpace(titel) ? "Klausur" : titel.Trim(), Text = text?.Trim(),
        });
    }

    /// <summary>Noch nicht übernommene Raumbuchungen und Klausuren eines Tages als Stunden „vorgemerkt“.</summary>
    public IEnumerable<Rohstunde> Vorgemerkt(DateOnly d)
    {
        var iso = Plandienst.Iso(d);
        foreach (var m in _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND von = @d AND typ IN ('raumbuchung', 'klausur') ORDER BY erstellt", Lesen, Db.P("@d", iso)))
        {
            var lehrer = new List<string>();
            if (!string.IsNullOrEmpty(m.Lehrer)) lehrer.Add(m.Lehrer);
            lehrer.AddRange(m.WeitereLehrer);
            for (var st = m.StundeVon ?? 1; st <= (m.StundeBis ?? m.StundeVon ?? 1); st++)
                yield return new Rohstunde(st, m.Typ == WebRueckmeldungsTypen.Raumbuchung ? m.Unterricht : null, "", m.Klassen, lehrer,
                    m.Raeume.Count > 0 || m.Unterricht.HasValue ? m.Raeume : (m.Raum is null ? new List<string>() : new List<string> { m.Raum }),
                    "vorgemerkt", null, m.Titel ?? "", m.Typ == WebRueckmeldungsTypen.Klausur ? "Klausur" : "Raumbuchung");
        }
    }
}
