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

    /// <summary>Offene Prüfungen ohne die schon in UntPlan übernommenen und wieder übertragenen (diese werden quittiert – keine Dubletten).</summary>
    public List<Meldung> OffeneKlausuren(Stand s) => OffeneKlausuren().Where(m => !(DateOnly.TryParse(m.Von, out var d) && SchonUebertragenQuittieren(s, d, m))).ToList();

    /// <summary>Noch nicht übernommene Löschungen von Prüfungen (Prüfungsliste: gelöschte Prüfungen nicht mehr zeigen).</summary>
    public List<Meldung> OffeneLoeschungen() => _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND typ = 'klausurloeschen' ORDER BY von", Lesen);

    private bool SchonUebertragenQuittieren(Stand s, DateOnly d, Meldung m)
    {
        if (!SchonUebertragen(s, d, m)) return false;
        _db.Ausfuehren("UPDATE rueckmeldungen SET status = 'quittiert', quittiert = @z WHERE id = @id AND status = 'offen'",
            Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@id", m.Id));
        return true;
    }
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
        var belegt = _plan.Rohstunden(s, d).Concat(Vorgemerkt(d, s).Where(r => ohneUnterricht is null || r.Unterricht != ohneUnterricht))
            .Any(r => r.Stunde >= von && r.Stunde <= bis && r.Status != "entfall" && r.Raeume.Any(x => Plandienst.RaumListe(x).Contains(raum, Ci)) && (ohneUnterricht is null || r.Unterricht != ohneUnterricht));
        var gesperrt = s.Paket.Absenzen.Any(a => a.Art == "raum" && Ci.Equals(a.Element, raum) && DateOnly.TryParse(a.Von, out var av) && DateOnly.TryParse(a.Bis, out var ab) &&
                                                 d >= av && d <= ab && a.StundeVon <= bis && (a.StundeBis >= von || a.StundeBis < a.StundeVon));
        return !belegt && !gesperrt;
    }

    /// <summary>Räume, die gebucht werden dürfen (in UntPlan als „buchbar“ markiert). Ältere Pakete ohne Kennzeichen: alle.</summary>
    public static IEnumerable<WebRaum> Buchbar(Stand s) =>
        (s.Paket.Elemente.Raeume.Any(r => r.Buchbar) ? s.Paket.Elemente.Raeume.Where(r => r.Buchbar) : s.Paket.Elemente.Raeume)
            .Where(r => r.Kurz.Length > 0).DistinctBy(r => r.Kurz, Ci);

    /// <summary>
    /// Freie buchbare Räume für einen Zeitraum (für die Buchung). Mit Unterricht: dessen eigene Belegung zählt nicht, und seine
    /// ursprünglichen Räume (laut Stundenplan) stehen immer zur Wahl – auch wenn sie nicht als buchbar markiert sind.
    /// </summary>
    public List<object> FreieRaeume(Stand s, DateOnly d, int von, int bis, int? unterricht = null)
    {
        var original = unterricht is int u ? _plan.UrspruenglicheRaeume(s, d, u, von, bis) : new List<string>();
        var kandidaten = Buchbar(s).Concat(s.Paket.Elemente.Raeume.Where(r => original.Contains(r.Kurz, Ci))).DistinctBy(r => r.Kurz, Ci);
        return kandidaten.Where(r => RaumFrei(s, d, r.Kurz, von, bis, unterricht))
            .OrderBy(r => original.Contains(r.Kurz, Ci) ? 0 : 1).ThenBy(r => r.Kurz, StringComparer.Create(new System.Globalization.CultureInfo("de-DE"), true))
            .Select(r => (object)new { kurz = r.Kurz, name = r.Name, raumart = r.Raumart, kapazitaet = r.Kapazitaet, urspruenglich = original.Contains(r.Kurz, Ci) }).ToList();
    }

    public Meldung Raumbuchung(Stand s, Benutzer b, DateOnly d, int? stundeVon, int? stundeBis, string raum, IEnumerable<string> klassen, string titel, string text)
    {
        if (b.LehrerKuerzel is null && !b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Räume buchen.");
        if (!Rechtedienst.Darf(b, Recht.BuchungRaum)) throw new UnauthorizedAccessException("Ihr Konto darf keine Räume buchen.");
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
        if (!Rechtedienst.Darf(b, Recht.BuchungRaum)) throw new UnauthorizedAccessException("Ihr Konto darf keine Räume buchen.");
        if (d < _uhr.Heute) throw new ArgumentException("Räume können nicht rückwirkend gebucht werden.");
        if (_plan.Frei(s, d).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        var stunden = _plan.Rohstunden(s, d).Where(r => r.Unterricht == unterricht && r.Stunde >= v && r.Stunde <= bi && r.Status != "entfall").ToList();
        if (stunden.Count == 0) throw new ArgumentException("Zu dieser Stunde gibt es keinen Unterricht.");
        var bisher = stunden.SelectMany(r => r.Raeume).Distinct(Ci).ToList();
        var neu = raeume.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (neu.FirstOrDefault(x => !s.Raeume.ContainsKey(x)) is { } unbekannt) throw new ArgumentException($"Unbekannter Raum {unbekannt}.");
        neu = neu.Select(x => s.Raeume[x].Kurz).ToList();
        var original = _plan.UrspruenglicheRaeume(s, d, unterricht, v, bi);
        foreach (var x in neu.Where(x => !bisher.Contains(x, Ci)))
        {
            // der ursprüngliche Raum der Stunde darf immer zurück, auch wenn er nicht buchbar ist
            if (!original.Contains(x, Ci) && !Buchbar(s).Any(r => Ci.Equals(r.Kurz, x))) throw new ArgumentException($"Raum {x} ist nicht buchbar.");
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

    /// <summary>
    /// Prüfung löschen: eine noch nicht übernommene Prüfung (eigene Rückmeldung) wird einfach zurückgezogen; eine schon aus UntPlan
    /// übertragene Prüfung erhält eine Rückmeldung „klausurloeschen“, die UntPlan beim Import ausführt. Ergebnis: neue Meldung oder null.
    /// Die Löschmeldung beschreibt den Termin genau (Stunden, Klassen, Räume und Titel aus dem Paket, Kennzeichen <see cref="Exakt"/>),
    /// damit beim Verlegen in dieselbe Zeit (anderer Raum, längere Dauer) nur der alte Termin gelöscht wird.
    /// </summary>
    public Meldung? KlausurLoeschen(Stand s, Benutzer b, DateOnly d, int stundeVon, int stundeBis, IEnumerable<string> klassen, string? titel)
    {
        if (!b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Prüfungen löschen.");
        if (!Rechtedienst.Darf(b, Recht.PruefungAnlegen)) throw new UnauthorizedAccessException("Ihr Konto darf keine Prüfungen eintragen oder löschen.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        var k = klassen.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (!DarfKlausurLoeschen(b, s, d, v, bi, k))
            throw new UnauthorizedAccessException("Eine Prüfung löschen bzw. verlegen nur die Person, die sie eingetragen hat, die Lehrkraft der Prüfung und die Administration.");
        // noch nicht übernommene Prüfung: zurückziehen – außer sie ist schon im Paket (übernommen, aber noch nicht quittiert)
        var offen = _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND typ = 'klausur' AND von = @d AND stunde_von <= @b AND stunde_bis >= @v", Lesen,
                Db.P("@d", Plandienst.Iso(d)), Db.P("@v", v), Db.P("@b", bi))
            .Where(m => k.Count == 0 || m.Klassen.Any(x => k.Contains(x, Ci))).Where(m => !SchonUebertragenQuittieren(s, d, m)).ToList();
        if (offen.Count > 0)
        {
            foreach (var m in offen) _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE id = @i", Db.P("@i", m.Id));
            return null;
        }
        var probe = new Meldung("", WebRueckmeldungsTypen.KlausurLoeschen, "", null, "", null, Plandienst.Iso(d), null, v, bi, null, new(), k, new(), titel, null, null, "offen", null);
        var ziele = Ziele(probe, s.Paket.Termine.Where(t => Ci.Equals(t.Art, "klausur")).ToList());
        if (ziele.Count == 0) throw new ArgumentException("Zu dieser Stunde gibt es keine Prüfung.");
        // schon zum Löschen vorgemerkt: nicht doppelt melden
        var loeschungen = OffeneLoeschungen();
        if (ziele.All(t => Geloescht(t, loeschungen, s.Paket.Termine))) return null;
        Meldung? letzte = null;
        foreach (var t in ziele.Where(t => !Geloescht(t, loeschungen, s.Paket.Termine)))
            letzte = Speichern(Neu(WebRueckmeldungsTypen.KlausurLoeschen, b) with
            {
                Von = Plandienst.Iso(d), StundeVon = t.StundeVon, StundeBis = Bis(t), Klassen = t.Klassen.ToList(), Raeume = t.Raeume.ToList(),
                Titel = string.IsNullOrWhiteSpace(t.Titel) ? (string.IsNullOrWhiteSpace(titel) ? null : titel.Trim()) : t.Titel.Trim(), Grund = Exakt,
            });
        return letzte;
    }

    /// <summary>
    /// Darf das Konto die Prüfung löschen bzw. verlegen? Administration immer; sonst mit dem Recht „Prüfungen anlegen“ die Person, die sie in UntPlanWeb
    /// eingetragen hat (vor und nach der Übernahme in UntPlan), bzw. eine Lehrkraft der Prüfung laut UntPlan (in UntPlan angelegte Prüfungen).
    /// </summary>
    public bool DarfKlausurLoeschen(Benutzer b, Stand s, DateOnly d, int von, int bis, IReadOnlyCollection<string> klassen)
    {
        if (b.IstAdmin) return true;
        if (!b.Kollegium || !Rechtedienst.Darf(b, Recht.PruefungAnlegen)) return false;
        var eigene = _db.Liste("SELECT * FROM rueckmeldungen WHERE typ = 'klausur' AND benutzer_id = @u AND von = @d AND stunde_von <= @b AND stunde_bis >= @v", Lesen,
            Db.P("@u", b.Id), Db.P("@d", Plandienst.Iso(d)), Db.P("@v", von), Db.P("@b", bis));
        if (eigene.Any(m => klassen.Count == 0 || m.Klassen.Any(x => klassen.Contains(x, Ci)))) return true;
        // fremde, noch nicht übernommene Prüfung: nur die Person, die sie eingetragen hat
        if (b.LehrerKuerzel is not { Length: > 0 } l) return false;
        return s.Paket.Termine.Any(t => PasstKlausur(t, d, von, bis, klassen) && t.Lehrer.Contains(l, Ci));
    }

    private static bool PasstKlausur(WebTermin t, DateOnly d, int von, int bis, IReadOnlyCollection<string> klassen) =>
        Ci.Equals(t.Art, "klausur") && DateOnly.TryParse(t.Von, out var tv) && DateOnly.TryParse(string.IsNullOrEmpty(t.Bis) ? t.Von : t.Bis, out var tb)
        && d >= tv && d <= tb && t.StundeVon <= bis && (t.StundeBis == 0 ? t.StundeVon : t.StundeBis) >= von
        && (klassen.Count == 0 || t.Klassen.Any(x => klassen.Contains(x, Ci)));

    /// <summary>Kennzeichen (Feld Grund) einer Löschmeldung, die den Termin genau beschreibt (Stunden, Räume, Titel).</summary>
    public const string Exakt = "exakt";

    private static int Bis(WebTermin t) => t.StundeBis == 0 ? t.StundeVon : Math.Max(t.StundeVon, t.StundeBis);
    private static bool GleicheMenge(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) =>
        a.Distinct(Ci).Count() == b.Distinct(Ci).Count() && a.All(x => b.Contains(x, Ci));

    /// <summary>Titel einer Meldung passt zum Termin (der Kartentext kann „Name Text“ bzw. „Name · Text“ lauten; „Klausur“/„Prüfung“ passt immer).</summary>
    private static bool TitelPasst(string? titel, string termin)
    {
        var a = (titel ?? "").Trim();
        var b = (termin ?? "").Trim();
        return a.Length == 0 || b.Length == 0 || a is "Klausur" or "Prüfung" || a.Equals(b, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Termine, die eine Löschmeldung meint: genau beschriebene Meldung (<see cref="Exakt"/>): gleiche Stunden, Räume und Titel, gemeinsame
    /// Klasse; sonst überlappende Stunden mit gemeinsamer Klasse (bevorzugt gleiche Stunden).
    /// </summary>
    public static List<WebTermin> Ziele(Meldung m, IReadOnlyCollection<WebTermin> termine)
    {
        if (!DateOnly.TryParse(m.Von, out var d)) return new();
        var von = m.StundeVon ?? 1;
        var bis = m.StundeBis ?? von;
        var kandidaten = termine.Where(t => PasstKlausur(t, d, von, bis, m.Klassen) && TitelPasst(m.Titel, t.Titel)).ToList();
        if (m.Grund == Exakt)
            return kandidaten.Where(t => t.StundeVon == von && Bis(t) == bis && GleicheMenge(t.Raeume, m.Raeume)).ToList();
        var gleich = kandidaten.Where(t => t.StundeVon == von && Bis(t) == bis).ToList();
        return gleich.Count > 0 ? gleich : kandidaten;
    }

    /// <summary>Ist der Termin zum Löschen vorgemerkt (offene Löschmeldung, auch der alte Termin einer verlegten Prüfung)?</summary>
    public static bool Geloescht(WebTermin t, IEnumerable<Meldung> loeschungen, IReadOnlyCollection<WebTermin>? termine = null) =>
        Ci.Equals(t.Art, "klausur") && loeschungen.Any(m => m.Typ == WebRueckmeldungsTypen.KlausurLoeschen && Ziele(m, termine ?? new[] { t }).Contains(t));

    /// <summary>
    /// Prüfung verlegen = alten Termin löschen und neu eintragen – als Einheit: schlägt das Eintragen fehl (Raum belegt, Prüfungssperre …),
    /// bleibt die alte Prüfung unverändert. Unveränderte Angaben ändern nichts.
    /// </summary>
    public Meldung KlausurVerlegen(Stand s, Benutzer b, DateOnly altDatum, int altVon, int altBis, IEnumerable<string> altKlassen, string? altTitel,
        DateOnly d, int? stundeVon, int? stundeBis, IEnumerable<string> klassen, IEnumerable<string> raeume, IEnumerable<string> aufsicht, string titel, string text)
    {
        var vorher = Offene().Where(m => m.Typ is WebRueckmeldungsTypen.Klausur or WebRueckmeldungsTypen.KlausurLoeschen).Select(m => m.Id).ToHashSet();
        var gesichert = _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND typ IN ('klausur', 'klausurloeschen') AND von IN (@a, @n)", Lesen,
            Db.P("@a", Plandienst.Iso(altDatum)), Db.P("@n", Plandienst.Iso(d)));
        try
        {
            KlausurLoeschen(s, b, altDatum, altVon, altBis, altKlassen, altTitel);
            return Klausur(s, b, d, stundeVon, stundeBis, klassen, raeume, aufsicht, titel, text);
        }
        catch
        {
            // zurück zum Stand vor dem Verlegen: neue Meldungen weg, zurückgezogene bzw. ersetzte wiederherstellen
            foreach (var m in Offene().Where(m => m.Typ is WebRueckmeldungsTypen.Klausur or WebRueckmeldungsTypen.KlausurLoeschen && !vorher.Contains(m.Id)))
                _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE id = @i", Db.P("@i", m.Id));
            var jetzt = Offene().Select(m => m.Id).ToHashSet();
            foreach (var m in gesichert.Where(m => !jetzt.Contains(m.Id))) Speichern(m);
            throw;
        }
    }

    public Meldung Klausur(Stand s, Benutzer b, DateOnly d, int? stundeVon, int? stundeBis, IEnumerable<string> klassen, IEnumerable<string> raeume, IEnumerable<string> aufsicht, string titel, string text)
    {
        if (!b.Kollegium) throw new ArgumentException("Nur Lehrkräfte können Klausuren eintragen.");
        if (!Rechtedienst.Darf(b, Recht.PruefungAnlegen)) throw new UnauthorizedAccessException("Ihr Konto darf keine Prüfungen eintragen oder löschen.");
        if (d < _uhr.Heute) throw new ArgumentException("Klausuren können nicht rückwirkend eingetragen werden.");
        // Unterricht | Einstellungen: erlaubter Datumsbereich, Raumkonflikt zulassen
        var einst = Klassenbucheinstellungen.Laden(_db);
        einst.PruefungPruefen(d);
        if (_plan.Frei(s, d).Frei) throw new ArgumentException("An diesem Tag ist kein Unterricht.");
        var (v, bi) = Stunden(s, stundeVon, stundeBis);
        var k = klassen.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (k.Count == 0) throw new ArgumentException("Bitte mindestens eine Klasse angeben.");
        if (k.FirstOrDefault(x => !s.Klassen.ContainsKey(x)) is { } uk) throw new ArgumentException($"Unbekannte Klasse {uk}.");
        var r = raeume.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (r.FirstOrDefault(x => !s.Raeume.ContainsKey(x)) is { } ur) throw new ArgumentException($"Unbekannter Raum {ur}.");
        var l = aufsicht.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(Ci).ToList();
        if (l.FirstOrDefault(x => !s.Lehrer.ContainsKey(x)) is { } ul) throw new ArgumentException($"Unbekannte Lehrkraft {ul}.");
        r = r.Select(x => s.Raeume[x].Kurz).ToList();
        k = k.Select(x => s.Klassen[x].Kurz).ToList();
        // Räume müssen frei sein – belegt nur durch den Unterricht der eigenen Klassen zählt nicht (die Prüfung findet dort statt)
        var belegt = _plan.Rohstunden(s, d).Concat(Vorgemerkt(d, s))
            .Where(x => x.Stunde >= v && x.Stunde <= bi && x.Status != "entfall" && !x.Klassen.Any(kl => k.Contains(kl, Ci)))
            .SelectMany(x => x.Raeume.SelectMany(Plandienst.RaumListe)).ToHashSet(Ci);
        if (!einst.Raumkonflikt && r.FirstOrDefault(belegt.Contains) is { } voll) throw new ArgumentException($"Raum {voll} ist in dieser Zeit schon belegt.");
        if (r.FirstOrDefault(x => s.Paket.Absenzen.Any(a => a.Art == "raum" && Ci.Equals(a.Element, x) && DateOnly.TryParse(a.Von, out var av) && DateOnly.TryParse(a.Bis, out var ab)
                                                       && d >= av && d <= ab && a.StundeVon <= bi && (a.StundeBis >= v || a.StundeBis < a.StundeVon))) is { } gesperrt)
            throw new ArgumentException($"Raum {gesperrt} ist in dieser Zeit gesperrt.");
        // schon übernommene Prüfung zu denselben Klassen und Stunden: nicht doppelt anlegen (außer sie ist zum Löschen vorgemerkt,
        // z. B. beim Verlegen in dieselbe Zeit mit anderem Raum bzw. längerer Dauer)
        var loeschungen = OffeneLoeschungen();
        if (s.Paket.Termine.Any(t => Ci.Equals(t.Art, "klausur") && DateOnly.TryParse(t.Von, out var tv) && DateOnly.TryParse(string.IsNullOrEmpty(t.Bis) ? t.Von : t.Bis, out var tb)
                                     && d >= tv && d <= tb && t.StundeVon <= bi && (t.StundeBis == 0 ? t.StundeVon : t.StundeBis) >= v && t.Klassen.Any(x => k.Contains(x, Ci))
                                     && !Geloescht(t, loeschungen, s.Paket.Termine)))
            throw new ArgumentException("Für diese Klasse ist in dieser Zeit bereits eine Prüfung eingetragen.");
        // gleiche, noch offene Prüfung (z. B. doppelt abgeschickt) ersetzen statt verdoppeln
        foreach (var alt in _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND typ = 'klausur' AND von = @d AND stunde_von <= @b AND stunde_bis >= @v", Lesen,
                     Db.P("@d", Plandienst.Iso(d)), Db.P("@v", v), Db.P("@b", bi)).Where(x => x.Klassen.Any(kl => k.Contains(kl, Ci))))
        {
            if (alt.BenutzerId != b.Id) throw new ArgumentException("Für diese Klasse ist in dieser Zeit bereits eine Prüfung vorgemerkt.");
            _db.Ausfuehren("DELETE FROM rueckmeldungen WHERE id = @i", Db.P("@i", alt.Id));
        }
        var haupt = l.FirstOrDefault() ?? b.LehrerKuerzel;
        return Speichern(Neu(WebRueckmeldungsTypen.Klausur, b) with
        {
            Von = Plandienst.Iso(d), StundeVon = v, StundeBis = bi, Klassen = k, Raeume = r, Lehrer = haupt, WeitereLehrer = l.Where(x => !Ci.Equals(x, haupt)).ToList(),
            Titel = string.IsNullOrWhiteSpace(titel) ? "Klausur" : titel.Trim(), Text = text?.Trim(),
        });
    }

    /// <summary>
    /// Ist eine offene Klausur bzw. Raumänderung schon im Paket aus UntPlan enthalten? Klausur: Klausurtermin am Tag mit überlappenden
    /// Stunden und gemeinsamer Klasse. Raumänderung zum Unterricht: die Stunden haben bereits genau die gebuchten Räume.
    /// </summary>
    private bool SchonUebertragen(Stand s, DateOnly d, Meldung m)
    {
        var von = m.StundeVon ?? 1;
        var bis = m.StundeBis ?? von;
        var termine = s.Paket.Termine.Where(t => Ci.Equals(t.Art, "klausur")).ToList();
        // Löschung ist übernommen, sobald der gemeinte Termin im Paket fehlt
        if (m.Typ == WebRueckmeldungsTypen.KlausurLoeschen)
            return Ziele(m, termine).Count == 0;
        if (m.Typ == WebRueckmeldungsTypen.Klausur)
        {
            // genau diese Prüfung (Stunden, Klassen, Titel, Räume) steht im Paket
            var titel = string.IsNullOrWhiteSpace(m.Titel) ? "Klausur" : m.Titel.Trim();
            if (termine.Any(t => PasstKlausur(t, d, von, bis, m.Klassen) && t.StundeVon == von && Bis(t) == bis && GleicheMenge(t.Klassen, m.Klassen)
                                 && Ci.Equals(t.Titel.Trim(), titel) && (m.Raeume.Count == 0 || GleicheMenge(t.Raeume, m.Raeume))))
                return true;
            // sonst (in UntPlan nachbearbeitet) eine überlappende Prüfung derselben Klassen – aber nicht der alte Termin einer verlegten
            // bzw. gelöschten Prüfung, solange dessen Löschung noch aussteht
            var loeschungen = OffeneLoeschungen().Where(x => x.Von == m.Von).ToList();
            return termine.Any(t => PasstKlausur(t, d, von, bis, m.Klassen) && (m.Klassen.Count == 0 || t.Klassen.Any(k => m.Klassen.Contains(k, Ci))))
                   && !loeschungen.Any(x => Ziele(x, termine).Count > 0 && (m.Klassen.Count == 0 || x.Klassen.Any(k => m.Klassen.Contains(k, Ci))));
        }
        if (m.Typ == WebRueckmeldungsTypen.Raumbuchung && m.Unterricht is int u && m.Raeume.Count > 0)
        {
            var stunden = _plan.Rohstunden(s, d).Where(r => r.Unterricht == u && r.Stunde >= von && r.Stunde <= bis && r.Status != "entfall").ToList();
            return stunden.Count > 0 && stunden.All(r => r.Raeume.Count == m.Raeume.Count && r.Raeume.All(x => m.Raeume.Contains(x, Ci))) && stunden.Any(r => r.Status != "regulaer");
        }
        return false;
    }

    /// <summary>Noch nicht übernommene Raumbuchungen und Klausuren eines Tages als Stunden „vorgemerkt“.</summary>
    /// <param name="mitBuchungen">Auch die Buchungen aus dem Buchungsmodul (Status „zusaetzlich“, siehe <see cref="Buchungsdienst.Planstunden"/>).</param>
    public IEnumerable<Rohstunde> Vorgemerkt(DateOnly d, Stand? s = null, bool mitBuchungen = true)
    {
        var iso = Plandienst.Iso(d);
        foreach (var m in _db.Liste("SELECT * FROM rueckmeldungen WHERE status = 'offen' AND von = @d AND typ IN ('raumbuchung', 'klausur', 'klausurloeschen') ORDER BY erstellt", Lesen, Db.P("@d", iso)))
        {
            // schon in UntPlan übernommen und wieder übertragen (auch ohne Quittung, z. B. Import aus Datei): nicht doppelt anzeigen
            if (s != null && SchonUebertragen(s, d, m))
            {
                _db.Ausfuehren("UPDATE rueckmeldungen SET status = 'quittiert', quittiert = @z WHERE id = @id AND status = 'offen'",
                    Db.P("@z", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm")), Db.P("@id", m.Id));
                continue;
            }
            var lehrer = new List<string>();
            if (!string.IsNullOrEmpty(m.Lehrer)) lehrer.Add(m.Lehrer);
            lehrer.AddRange(m.WeitereLehrer);
            for (var st = m.StundeVon ?? 1; st <= (m.StundeBis ?? m.StundeVon ?? 1); st++)
                yield return new Rohstunde(st, m.Typ == WebRueckmeldungsTypen.Raumbuchung ? m.Unterricht : null, "", m.Klassen, lehrer,
                    m.Raeume.Count > 0 || m.Unterricht.HasValue ? m.Raeume : (m.Raum is null ? new List<string>() : new List<string> { m.Raum }),
                    "vorgemerkt", null, m.Titel ?? "", m.Typ switch { WebRueckmeldungsTypen.Klausur => "Klausur", WebRueckmeldungsTypen.KlausurLoeschen => "KlausurLoeschen", _ => "Raumbuchung" });
        }
        if (!mitBuchungen) yield break;
        foreach (var r in Buchungsdienst.Planstunden(_db, d, () => s is null ? null : _plan.Rohstunden(s, d))) yield return r;
    }
}
