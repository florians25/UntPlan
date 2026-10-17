using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Mitteilung in einer Liste (Posteingang, Gesendet, Entwürfe).</summary>
/// <param name="Gelesen">Posteingang: Zeitpunkt des Lesens (leer = ungelesen).</param>
/// <param name="Lesebestaetigung">Der Absender hat eine Lesebestätigung angefordert.</param>
/// <param name="Bestaetigt">Posteingang: Zeitpunkt der eigenen Lesebestätigung.</param>
/// <param name="Anzahl">Gesendet: Zahl der Empfänger*innen.</param>
/// <param name="GelesenAnzahl">Gesendet: Zahl der Empfänger*innen, die gelesen haben.</param>
/// <param name="BestaetigtAnzahl">Gesendet: Zahl der Lesebestätigungen.</param>
/// <param name="AntwortAuf">Antwort bzw. Weiterleitung zu dieser Mitteilung.</param>
/// <param name="EmpfaengerText">Gewählte Empfänger (Gruppen bzw. Personen) in Worten, z. B. „Erziehungsberechtigte 7a“.</param>
public sealed record Mitteilung(int Id, int AbsenderId, string Absender, string Betreff, string Text, string Erstellt, bool Wichtig, string? Gelesen, List<string> Empfaenger,
    List<Anhang>? Anhaenge = null, List<string>? EntwurfEmpfaenger = null, bool Lesebestaetigung = false, string? Bestaetigt = null, int Anzahl = 0, int GelesenAnzahl = 0,
    int BestaetigtAnzahl = 0, int? AntwortAuf = null, string EmpfaengerText = "", string AbsenderRolle = "")
{
    /// <summary>false = der Absender hat „Antwort nicht erlaubt“ gewählt (kein Antworten bzw. Allen antworten).</summary>
    public bool AntwortErlaubt { get; init; } = true;
}

/// <summary>Rollen-Pill einer Person in der Empfängerauswahl, z. B. „Lehrkraft“ oder „Klassenlehrkraft | DTI1A“.</summary>
/// <param name="Art">lehrkraft, klassenlehrkraft, schulleitung, administration, schueler, eltern.</param>
public sealed record Rollenpill(string Art, string Text, List<string>? Klassen = null);

/// <summary>Person in der Empfängerauswahl der Apps (Kolleg*innen bzw. Individuell) mit Rollen-Pills, Klassen und Abteilungen für die Filter.
/// <c>Gruppen</c>: Kennungen der Personengruppen für den Filter „Gruppe“ (kollegium, leitung, klassenleitungen, schueler, eltern sowie
/// <c>profil:…</c> für ein eigenes Rechte-Profil des Kontos).</summary>
public sealed record Empfaengerperson(string Id, string Name, string Anzeigename, string Login, string Kuerzel, string Initialen, string Kategorie, string Rolle,
    List<Rollenpill> Rollen, List<string> Klassen, List<string> Abteilungen, List<string>? Gruppen = null);

/// <summary>Anhang einer Mitteilung (ohne Inhalt).</summary>
public sealed record Anhang(int Id, string Name, string Typ, int Groesse);

/// <summary>Eigene Verteilerliste (Mitglieder sind Empfängerkennungen wie benutzer:5, klasse:7a, gruppe:kollegium).</summary>
public sealed record Mitteilungsliste(int Id, string Name, List<string> Mitglieder, List<string> Namen, string Erstellt);

/// <summary>Möglicher Empfänger in der Auswahl.</summary>
/// <param name="Id">Kennung, z. B. <c>benutzer:5</c>, <c>klasse:7a</c>, <c>eltern:7a</c>, <c>gruppe:kollegium</c>, <c>liste:3</c>.</param>
/// <param name="Art"><c>gruppe</c>, <c>klasse</c>, <c>liste</c> bzw. die Rolle eines Benutzers in Worten.</param>
/// <param name="Kategorie">Reiter der Empfängerauswahl: <c>lehrkraefte</c>, <c>klassen</c>, <c>schueler</c>, <c>eltern</c>, <c>gruppen</c>, <c>listen</c>.</param>
/// <param name="Klasse">Zugehörige Klasse (Filter in der Auswahl).</param>
public sealed record Empfaenger(string Id, string Name, string Art, string Kategorie, string? Klasse = null);

/// <summary>Lesestatus einer Empfängerin bzw. eines Empfängers (Lesebestätigungsliste).</summary>
public sealed record Lesestatus(int BenutzerId, string Name, string Rolle, string? Gelesen, string? Bestaetigt);

/// <summary>
/// Einstellungen der Administration für die Mitteilungen: wer an wen schreiben darf, Sperren, Anhänge und Aufbewahrung.
/// </summary>
/// <param name="ElternSenden">Erziehungsberechtigte dürfen Mitteilungen schreiben.</param>
/// <param name="SchuelerSenden">Schüler*innen dürfen Mitteilungen schreiben.</param>
/// <param name="FamilieAnKlassenleitung">Schüler*innen und Erziehungsberechtigte dürfen der Klassenleitung schreiben.</param>
/// <param name="FamilieAnLehrkraefte">… allen Lehrkräften der Klasse.</param>
/// <param name="FamilieAnLeitung">… der Schulleitung.</param>
/// <param name="FamilieAntworten">… auf erhaltene Mitteilungen antworten (auch wenn sie dem Absender sonst nicht schreiben dürften).</param>
/// <param name="LehrerAnFamilie">Lehrkräfte dürfen Schüler*innen und Erziehungsberechtigten schreiben.</param>
/// <param name="LehrerNurEigeneKlassen">… nur in den eigenen Klassen (Unterricht bzw. Klassenleitung).</param>
/// <param name="Gesperrt">Konten, die keine Mitteilungen senden dürfen (lesen bleibt möglich).</param>
public sealed record MitteilungsEinstellungen(bool ElternSenden = true, bool SchuelerSenden = true, bool FamilieAnKlassenleitung = true, bool FamilieAnLehrkraefte = true,
    bool FamilieAnLeitung = true, bool FamilieAntworten = true, bool LehrerAnFamilie = true, bool LehrerNurEigeneKlassen = false, int MaxAnhangMb = 7, int MaxAnhaenge = 5,
    int AufbewahrungTage = 365, List<int>? Gesperrt = null);

/// <summary>
/// Mitteilungen (Posteingang, Gesendet, Entwürfe, Listen) wie im Original:
/// <list type="bullet">
/// <item>Empfänger nach Gruppen: Lehrkräfte, Schulleitung, Klassenlehrkräfte, Klassen (Klassenkonto und Klassenleitung, Lehrkräfte der Klasse),
///   Schüler*innen bzw. Erziehungsberechtigte einer Klasse, einzelne Personen, eigene Listen. Gruppen werden beim Senden aufgelöst.</item>
/// <item>Rechte laut <see cref="MitteilungsEinstellungen"/>; Schüler*innen und Erziehungsberechtigte schreiben nur den erlaubten Lehrkräften.</item>
/// <item>Lesebestätigung anfordern und senden, Lesebestätigungsliste für den Absender.</item>
/// <item>Antworten, Allen antworten, Weiterleiten (mit Anhängen); Entwürfe serverseitig mit Anhängen (Größe und Anzahl begrenzt).</item>
/// <item>Automatisches Löschen nach der Aufbewahrungsfrist (Standard ein Jahr).</item>
/// </list>
/// </summary>
public sealed class Mitteilungsdienst
{
    private const string Schluessel = "mitteilungen_einstellungen";
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Benutzerdienst _benutzer;
    private DateTime _aufgeraeumt = DateTime.MinValue;

    public Mitteilungsdienst(Db db, Uhr uhr, Benutzerdienst benutzer) { _db = db; _uhr = uhr; _benutzer = benutzer; }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm");

    // ------------------------------------------------------------------------------------------ Einstellungen

    public MitteilungsEinstellungen Einstellungen()
    {
        var json = _db.Wert("SELECT wert FROM einstellungen WHERE schluessel = @s", Db.P("@s", Schluessel)) as string;
        MitteilungsEinstellungen? e = null;
        if (!string.IsNullOrWhiteSpace(json)) try { e = JsonSerializer.Deserialize<MitteilungsEinstellungen>(json, JsonOpt); } catch (JsonException) { }
        e ??= new MitteilungsEinstellungen();
        return e with { Gesperrt = e.Gesperrt ?? new() };
    }

    public MitteilungsEinstellungen EinstellungenSpeichern(MitteilungsEinstellungen e)
    {
        if (e.MaxAnhangMb is < 1 or > 25) throw new ArgumentException("Die Größe eines Anhangs muss zwischen 1 und 25 MB liegen.");
        if (e.MaxAnhaenge is < 0 or > 10) throw new ArgumentException("Es sind höchstens 10 Anhänge je Mitteilung möglich.");
        if (e.AufbewahrungTage is < 30 or > 3650) throw new ArgumentException("Die Aufbewahrung muss zwischen 30 und 3650 Tagen liegen.");
        var neu = e with { Gesperrt = (e.Gesperrt ?? new()).Distinct().ToList() };
        _db.Ausfuehren("INSERT INTO einstellungen (schluessel, wert) VALUES (@s, @w) ON CONFLICT(schluessel) DO UPDATE SET wert = excluded.wert",
            Db.P("@s", Schluessel), Db.P("@w", JsonSerializer.Serialize(neu, JsonOpt)));
        return neu;
    }

    public int MaxAnhang => Einstellungen().MaxAnhangMb * 1024 * 1024;

    /// <summary>Darf das Konto Mitteilungen senden (Rolle, Einstellungen, Sperre)?</summary>
    public bool DarfSenden(Benutzer ich, MitteilungsEinstellungen? e = null)
    {
        e ??= Einstellungen();
        if (e.Gesperrt!.Contains(ich.Id) || !Rechtedienst.Darf(ich, Recht.MitteilungSenden)) return false;
        return ich.Rolle switch { Rollen.Eltern => e.ElternSenden, Rollen.Schueler => e.SchuelerSenden, _ => true };
    }

    /// <summary>
    /// Darf das Konto auf erhaltene Mitteilungen antworten? Wer senden darf, immer; Schüler*innen und Erziehungsberechtigte ohne Senderecht
    /// (Einstellungen bzw. Rechte-Modell) nur lesen und – mit „FamilieAntworten“ – dem Absender antworten. Gesperrte Konten nie.
    /// </summary>
    public bool DarfAntworten(Benutzer ich, MitteilungsEinstellungen? e = null)
    {
        e ??= Einstellungen();
        return DarfSenden(ich, e) || (ich.Familie && e.FamilieAntworten && !e.Gesperrt!.Contains(ich.Id));
    }

    /// <summary>Senderecht prüfen; ohne Senderecht ist nur eine Antwort (antwortAuf) mit <see cref="DarfAntworten"/> erlaubt.</summary>
    private void SendenOderAntwortenPruefen(Benutzer ich, int? antwortAuf)
    {
        var e = Einstellungen();
        if (!DarfSenden(ich, e) && !(antwortAuf is not null && DarfAntworten(ich, e)))
            throw new UnauthorizedAccessException("Ihr Konto darf keine Mitteilungen senden.");
    }

    // ------------------------------------------------------------------------------------------ Empfänger

    private static string Anzeige(Benutzer b) => b.Anzeigename.Length > 0 ? $"{b.Anzeigename} ({b.Login})" : b.Login;

    /// <summary>Lehrkräfte (Kürzel), die in der Klasse unterrichten, und die Klassenleitung.</summary>
    private static HashSet<string> LehrkraefteDerKlasse(Stand? s, string klasse)
    {
        var res = new HashSet<string>(Ci);
        if (s == null) return res;
        foreach (var u in s.Unterricht.Values)
            foreach (var z in u.Zeilen)
                if (z.Klassen.Contains(klasse, Ci) && z.Lehrer.Length > 0) res.Add(z.Lehrer);
        res.UnionWith(Klassenleitung(s, klasse));
        return res;
    }

    private static List<string> Klassenleitung(Stand? s, string klasse) => s != null && s.Klassen.TryGetValue(klasse, out var k) ? k.Klassenleitung : new List<string>();

    /// <summary>Klassen, in denen eine Lehrkraft unterrichtet bzw. Klassenleitung ist.</summary>
    private static HashSet<string> EigeneKlassen(Stand? s, string? lehrer)
    {
        var res = new HashSet<string>(Ci);
        if (s == null || lehrer is null) return res;
        foreach (var u in s.Unterricht.Values)
            foreach (var z in u.Zeilen)
                if (Ci.Equals(z.Lehrer, lehrer)) res.UnionWith(z.Klassen);
        res.UnionWith(s.Klassen.Values.Where(k => k.Klassenleitung.Contains(lehrer, Ci)).Select(k => k.Kurz));
        return res;
    }

    private string KinderText(Stand? s, Benutzer b)
    {
        if (s == null || b.SchuelerIds.Count == 0) return b.Klassen.Count > 0 ? string.Join(", ", b.Klassen) : "";
        return string.Join(", ", b.SchuelerIds.Select(i => s.Schueler.FirstOrDefault(x => x.Id == i)).Where(x => x != null).Select(x => $"{x!.Vorname} {x.Name}, {x.Klasse}"));
    }

    /// <summary>Auswahlliste der möglichen Empfänger (Gruppen und Personen) für den Absender – nach den Rechten der Einstellungen.</summary>
    public List<Empfaenger> Empfaengerliste(Benutzer ich, Stand? s, bool mitListen = true)
    {
        var e = Einstellungen();
        var res = new List<Empfaenger>();
        if (!DarfSenden(ich, e)) return res;
        var alle = _benutzer.Alle().Where(b => b.Id != ich.Id && !b.Gesperrt).ToList();
        var klassen = (s?.Paket.Elemente.Klassen ?? new()).Select(k => k.Kurz).ToList();
        if (ich.Kollegium)
        {
            if (mitListen)
                foreach (var l in Listen(ich, s, false)) res.Add(new("liste:" + l.Id, $"Liste: {l.Name}", "liste", "listen"));
            res.Add(new("gruppe:kollegium", "Alle Lehrkräfte", "gruppe", "gruppen"));
            res.Add(new("gruppe:leitung", "Schulleitung und Administration", "gruppe", "gruppen"));
            res.Add(new("gruppe:klassenleitungen", "Alle Klassenlehrkräfte", "gruppe", "gruppen"));
            if (Rechtedienst.Darf(ich, Recht.MitteilungAlle))
            {
                res.Add(new("gruppe:schueler", "Alle Schüler*innen", "gruppe", "gruppen"));
                res.Add(new("gruppe:eltern", "Alle Erziehungsberechtigten", "gruppe", "gruppen"));
                res.Add(new("gruppe:alle", "Alle Benutzer", "gruppe", "gruppen"));
            }
            var familie = Rechtedienst.Darf(ich, Recht.MitteilungFamilie) || e.LehrerAnFamilie;
            var eigene = Rechtedienst.Darf(ich, Recht.MitteilungAlle) || !e.LehrerNurEigeneKlassen ? null : EigeneKlassen(s, ich.LehrerKuerzel);
            bool Erlaubt(string k) => eigene is null || eigene.Contains(k);
            foreach (var k in klassen)
            {
                res.Add(new("klasse:" + k, $"Klasse {k} (Klassenkonto und Klassenleitung)", "klasse", "klassen", k));
                res.Add(new("klassenleitung:" + k, $"Klassenleitung {k}", "klasse", "klassen", k));
                res.Add(new("lehrer:" + k, $"Lehrkräfte der Klasse {k}", "klasse", "klassen", k));
                if (familie && Erlaubt(k))
                {
                    res.Add(new("schueler:" + k, $"Schüler*innen der Klasse {k}", "klasse", "schueler", k));
                    res.Add(new("eltern:" + k, $"Erziehungsberechtigte der Klasse {k}", "klasse", "eltern", k));
                }
            }
            foreach (var b in alle)
            {
                if (b.Kollegium) res.Add(new("benutzer:" + b.Id, Anzeige(b), Rollen.Name(b.Rolle), "lehrkraefte"));
                else if (familie && (eigene is null || b.Klassen.Any(Erlaubt) || (b.Klassen.Count == 0 && Erlaubt(b.Element))))
                {
                    var kinder = KinderText(s, b);
                    var name = b.Rolle == Rollen.Eltern && kinder.Length > 0 ? $"{Anzeige(b)} – {kinder}" : Anzeige(b);
                    res.Add(new("benutzer:" + b.Id, name, Rollen.Name(b.Rolle), b.Rolle == Rollen.Eltern ? "eltern" : "schueler", b.KlassenKuerzel));
                }
            }
            return res;
        }
        // Schüler*innen und Erziehungsberechtigte: Klassenleitung, Lehrkräfte der Klasse(n) bzw. Schulleitung laut Einstellungen
        var kl = ich.Klassen.Count > 0 ? ich.Klassen : ich.KlassenKuerzel is string kk ? new List<string> { kk } : new List<string>();
        var leitung = kl.SelectMany(k => Klassenleitung(s, k)).ToHashSet(Ci);
        var lehrkraefte = kl.SelectMany(k => LehrkraefteDerKlasse(s, k)).ToHashSet(Ci);
        foreach (var b in alle.Where(b => b.Kollegium))
        {
            var istLeitung = b.LehrerKuerzel is string lk && leitung.Contains(lk);
            var istLehrkraft = b.LehrerKuerzel is string lk2 && lehrkraefte.Contains(lk2);
            if ((e.FamilieAnKlassenleitung && istLeitung) || (e.FamilieAnLehrkraefte && istLehrkraft) || (e.FamilieAnLeitung && b.Rolle == Rollen.Leitung))
                res.Add(new("benutzer:" + b.Id, Anzeige(b) + (istLeitung ? " – Klassenleitung" : ""), Rollen.Name(b.Rolle), b.Rolle == Rollen.Leitung ? "gruppen" : "lehrkraefte"));
        }
        return res;
    }

    private static string Initialen(string name)
    {
        var teile = name.Split(new[] { ' ', '_', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return teile.Length >= 2 && char.IsLetter(teile[0][0]) && char.IsLetter(teile[1][0])
            ? $"{char.ToUpperInvariant(teile[0][0])}{char.ToUpperInvariant(teile[1][0])}"
            : name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
    }

    /// <summary>
    /// Personen für die Empfängerauswahl der Apps: typ „kollegium“ nur Kolleg*innen, „individuell“ alle erlaubten Personen
    /// (dazu die Gruppen). Je Person Rollen-Pills (Lehrkraft, Klassenlehrkraft | Klassen, Schulleitung …), Klassen und Abteilungen
    /// (Abteilung des Stammraums der Klassen laut Raum-Stammdaten) für die Filter Klasse und Abteilung.
    /// </summary>
    public (List<Empfaengerperson> Personen, List<Empfaenger> Gruppen) Personen(Benutzer ich, Stand? s, string? typ)
    {
        var individuell = typ == "individuell";
        var erlaubt = Empfaengerliste(ich, s, false);
        var konten = _benutzer.Alle().ToDictionary(b => b.Id);
        var leitung = new Dictionary<string, List<string>>(Ci);
        foreach (var k in s?.Paket.Elemente.Klassen ?? new())
            foreach (var l in k.Klassenleitung) (leitung.TryGetValue(l, out var kl) ? kl : leitung[l] = new()).Add(k.Kurz);
        var abteilungRaum = _db.Liste("SELECT raum, abteilung FROM raum_daten WHERE abteilung <> ''", r => (Raum: r.Text("raum"), Abteilung: r.Text("abteilung")))
            .GroupBy(x => x.Raum, Ci).ToDictionary(g => g.Key, g => g.First().Abteilung, Ci);
        var abteilungKlasse = (s?.Paket.Elemente.Klassen ?? new()).Where(k => abteilungRaum.ContainsKey(k.Stammraum)).ToDictionary(k => k.Kurz, k => abteilungRaum[k.Stammraum], Ci);
        var de = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), true);
        // eigene Rechte-Profile (z. B. „Sekretariat“) als zusätzliche Gruppe; Systemrollen sind bereits über Rolle/Pills abgedeckt
        var profile = _db.Liste("SELECT id, rechteprofil FROM benutzer WHERE rechteprofil IS NOT NULL AND rechteprofil <> ''", r => (Id: r.Zahl("id"), P: r.Text("rechteprofil")))
            .Where(x => !UntPlanWeb.Dienste.Rollen.Gueltig(x.P)).ToDictionary(x => x.Id, x => x.P);
        var personen = new List<Empfaengerperson>();
        foreach (var e in erlaubt.Where(x => x.Id.StartsWith("benutzer:")))
        {
            if (!int.TryParse(e.Id[9..], out var id) || !konten.TryGetValue(id, out var b)) continue;
            if (!individuell && !b.Kollegium) continue;
            var pills = new List<Rollenpill>();
            List<string> klassen;
            var kuerzel = b.Kollegium ? b.LehrerKuerzel ?? "" : "";
            if (b.Kollegium)
            {
                if (b.Rolle == Rollen.Admin) pills.Add(new("administration", Rollen.Name(b.Rolle)));
                if (b.Rolle == Rollen.Leitung) pills.Add(new("schulleitung", Rollen.Name(b.Rolle)));
                if (kuerzel.Length > 0) pills.Add(new("lehrkraft", Rollen.Name(Rollen.Lehrer)));
                var geleitet = kuerzel.Length > 0 && leitung.TryGetValue(kuerzel, out var gl) ? gl.OrderBy(x => x, de).ToList() : new List<string>();
                if (geleitet.Count > 0) pills.Add(new("klassenlehrkraft", $"Klassenlehrkraft | {string.Join(", ", geleitet)}", geleitet));
                klassen = EigeneKlassen(s, kuerzel.Length > 0 ? kuerzel : null).OrderBy(x => x, de).ToList();
            }
            else
            {
                pills.Add(new(b.Rolle == Rollen.Eltern ? "eltern" : "schueler", Rollen.Name(b.Rolle)));
                klassen = (b.Klassen.Count > 0 ? b.Klassen : b.KlassenKuerzel is string kk ? new List<string> { kk } : new List<string>()).ToList();
            }
            var anzeigename = b.Anzeigename.Length > 0 ? b.Anzeigename : b.Login;
            var name = kuerzel.Length > 0 ? kuerzel : anzeigename;
            var gruppenIds = new List<string>();
            if (b.Kollegium) gruppenIds.Add("kollegium");
            if (b.Rolle is Rollen.Leitung or Rollen.Admin) gruppenIds.Add("leitung");
            if (pills.Any(x => x.Art == "klassenlehrkraft")) gruppenIds.Add("klassenleitungen");
            if (b.Rolle == Rollen.Schueler) gruppenIds.Add("schueler");
            if (b.Rolle == Rollen.Eltern) gruppenIds.Add("eltern");
            if (profile.TryGetValue(b.Id, out var profil)) gruppenIds.Add("profil:" + profil);
            personen.Add(new(e.Id, name, anzeigename, b.Login, kuerzel, Initialen(name), e.Kategorie, b.Rolle, pills, klassen,
                klassen.Where(abteilungKlasse.ContainsKey).Select(k => abteilungKlasse[k]).Distinct(Ci).OrderBy(x => x, de).ToList(), gruppenIds));
        }
        var gruppen = individuell ? erlaubt.Where(x => !x.Id.StartsWith("benutzer:")).ToList() : new List<Empfaenger>();
        return (personen.OrderBy(p => p.Name, de).ToList(), gruppen);
    }

    /// <summary>Zusätzlich erlaubte Empfänger einer Antwort: Absender (und bei „Allen antworten“ die übrigen Empfänger*innen) der beantworteten Mitteilung.</summary>
    private HashSet<int> Antwortempfaenger(Benutzer ich, int? antwortAuf, bool alle)
    {
        var res = new HashSet<int>();
        if (antwortAuf is not int id) return res;
        var absender = _db.Wert("""
            SELECT m.absender_id FROM mitteilungen m JOIN mitteilung_empfaenger e ON e.mitteilung_id = m.id
            WHERE m.id = @m AND e.benutzer_id = @b AND m.entwurf = 0 AND m.keine_antwort = 0
            """, Db.P("@m", id), Db.P("@b", ich.Id));
        if (absender is null) return res;
        if (ich.Kollegium || Einstellungen().FamilieAntworten) res.Add(Convert.ToInt32(absender));
        if (alle && ich.Kollegium)
            res.UnionWith(_db.Liste("SELECT benutzer_id FROM mitteilung_empfaenger WHERE mitteilung_id = @m", r => r.Zahl("benutzer_id"), Db.P("@m", id)));
        res.Remove(ich.Id);
        return res;
    }

    /// <summary>Empfänger-Kennungen in Benutzer-Ids auflösen (nur erlaubte Empfänger).</summary>
    public HashSet<int> Aufloesen(Benutzer ich, Stand? s, IEnumerable<string> kennungen, int? antwortAuf = null)
    {
        var erlaubt = Empfaengerliste(ich, s).Select(x => x.Id).ToHashSet();
        SendenOderAntwortenPruefen(ich, antwortAuf);
        var antwort = Antwortempfaenger(ich, antwortAuf, true);
        var alle = _benutzer.Alle().Where(b => !b.Gesperrt).ToList();
        var res = new HashSet<int>();
        // eigene Listen in ihre Mitglieder auflösen (nicht verschachtelt)
        var liste = new List<string>();
        foreach (var k in kennungen.Distinct())
        {
            var antwortErlaubt = k.StartsWith("benutzer:") && int.TryParse(k[9..], out var aid) && antwort.Contains(aid);
            if (!erlaubt.Contains(k) && !antwortErlaubt) throw new ArgumentException("Unzulässiger Empfänger.");
            if (k.StartsWith("liste:") && int.TryParse(k[6..], out var lid) && Listen(ich, s, false).FirstOrDefault(x => x.Id == lid) is { } l)
                liste.AddRange(l.Mitglieder.Where(m => erlaubt.Contains(m) && !m.StartsWith("liste:")));
            else liste.Add(k);
        }
        bool Lehrkraft(Benutzer b, ICollection<string> kuerzel) => b.Kollegium && b.LehrerKuerzel is string lk && kuerzel.Contains(lk);
        foreach (var k in liste.Distinct())
        {
            var teil = k.Split(':', 2);
            var wert = teil.Length > 1 ? teil[1] : "";
            IEnumerable<Benutzer> treffer = teil[0] switch
            {
                "gruppe" => wert switch
                {
                    "kollegium" => alle.Where(b => b.Kollegium),
                    "leitung" => alle.Where(b => b.Rolle is Rollen.Leitung or Rollen.Admin),
                    "klassenleitungen" => alle.Where(b => Lehrkraft(b, (s?.Klassen.Values ?? Enumerable.Empty<UntPlan.Web.Contracts.WebKlasse>()).SelectMany(x => x.Klassenleitung).ToHashSet(Ci))),
                    "schueler" => alle.Where(b => b.Rolle == Rollen.Schueler),
                    "eltern" => alle.Where(b => b.Rolle == Rollen.Eltern),
                    "alle" => alle,
                    _ => Enumerable.Empty<Benutzer>(),
                },
                "klasse" => alle.Where(b => b.HatKlasse(wert) || Lehrkraft(b, Klassenleitung(s, wert).ToHashSet(Ci))),
                "klassenleitung" => alle.Where(b => Lehrkraft(b, Klassenleitung(s, wert).ToHashSet(Ci))),
                "lehrer" => alle.Where(b => Lehrkraft(b, LehrkraefteDerKlasse(s, wert))),
                "schueler" => alle.Where(b => b.Rolle == Rollen.Schueler && b.HatKlasse(wert)),
                "eltern" => alle.Where(b => b.Rolle == Rollen.Eltern && b.HatKlasse(wert)),
                "benutzer" when int.TryParse(wert, out var id) => alle.Where(b => b.Id == id),
                _ => Enumerable.Empty<Benutzer>(),
            };
            res.UnionWith(treffer.Select(b => b.Id));
        }
        res.Remove(ich.Id);
        return res;
    }

    /// <summary>Gewählte Empfänger in Worten (für die Anzeige beim Absender).</summary>
    private string EmpfaengerText(Benutzer ich, Stand? s, IEnumerable<string> kennungen)
    {
        var namen = Empfaengerliste(ich, s).ToDictionary(x => x.Id, x => x.Name);
        var t = string.Join(", ", kennungen.Distinct().Select(k => namen.TryGetValue(k, out var n) ? n
            : k.StartsWith("benutzer:") && int.TryParse(k[9..], out var id) && _benutzer.Holen(id) is { } b ? Anzeige(b) : k));
        return t.Length > 1000 ? t[..997] + "…" : t;
    }

    // ------------------------------------------------------------------------------------------ Senden

    /// <summary>„Antwort nicht erlaubt“: die beantwortete Mitteilung hat ein anderes Konto mit dieser Sperre an mich gesendet.</summary>
    private bool AntwortGesperrt(Benutzer ich, int? antwortAuf) => antwortAuf is int id && _db.Wert("""
        SELECT 1 FROM mitteilungen m JOIN mitteilung_empfaenger e ON e.mitteilung_id = m.id
        WHERE m.id = @m AND e.benutzer_id = @b AND m.absender_id <> @b AND m.keine_antwort = 1
        """, Db.P("@m", id), Db.P("@b", ich.Id)) != null;

    private const string KeineAntwort = "Auf diese Mitteilung ist keine Antwort erlaubt.";

    private static void Pruefen(string betreff, string text)
    {
        if (string.IsNullOrWhiteSpace(betreff)) throw new ArgumentException("Bitte einen Betreff eingeben.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte einen Text eingeben.");
        if (betreff.Length > 200 || text.Length > 20000) throw new ArgumentException("Betreff oder Text ist zu lang.");
    }

    public int Senden(Benutzer ich, Stand? s, IEnumerable<string> empfaenger, string betreff, string text, bool wichtig, bool lesebestaetigung = false, int? antwortAuf = null,
        bool antwortErlaubt = true)
    {
        Pruefen(betreff, text);
        if (AntwortGesperrt(ich, antwortAuf)) throw new ArgumentException(KeineAntwort);
        var kennungen = empfaenger.ToList();
        var ids = Aufloesen(ich, s, kennungen, antwortAuf);
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens einen Empfänger wählen.");
        return Zustellen(ich, ids, betreff, text, wichtig, lesebestaetigung, antwortAuf, EmpfaengerText(ich, s, kennungen), antwortErlaubt);
    }

    /// <summary>Benachrichtigung des Systems (Sprechtag, Buchungen) an einzelne Konten – unabhängig von den Schreibrechten des auslösenden Kontos.</summary>
    public int Benachrichtigen(Benutzer absender, IEnumerable<int> konten, string betreff, string text)
    {
        Pruefen(betreff, text);
        var ids = konten.Where(k => k != absender.Id && _benutzer.Holen(k) is { Gesperrt: false }).ToHashSet();
        if (ids.Count == 0) throw new ArgumentException("Keine Empfänger.");
        return Zustellen(absender, ids, betreff, text, false, false, null, "");
    }

    private int Zustellen(Benutzer ich, HashSet<int> ids, string betreff, string text, bool wichtig, bool lesebestaetigung, int? antwortAuf, string empfaengerText,
        bool antwortErlaubt = true)
    {
        var neu = 0L;
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, """
                INSERT INTO mitteilungen (absender_id, betreff, text, erstellt, wichtig, lesebestaetigung, antwort_auf, empfaenger_text, keine_antwort)
                VALUES (@a, @b, @t, @e, @w, @l, @af, @et, @ka)
                """, Db.P("@a", ich.Id), Db.P("@b", betreff.Trim()), Db.P("@t", text.Trim()), Db.P("@e", Jetzt), Db.P("@w", wichtig && ich.Kollegium),
                Db.P("@l", lesebestaetigung), Db.P("@af", antwortAuf), Db.P("@et", empfaengerText), Db.P("@ka", antwortErlaubt ? 0 : 1));
            neu = Db.LetzteId(c);
            foreach (var id in ids) Db.Ausfuehren(c, "INSERT INTO mitteilung_empfaenger (mitteilung_id, benutzer_id) VALUES (@m, @b)", Db.P("@m", neu), Db.P("@b", id));
        });
        return (int)neu;
    }

    // ------------------------------------------------------------------------------------------ Lesen

    private List<string> EmpfaengerNamen(int id) => _db.Liste("""
        SELECT CASE WHEN b.anzeigename <> '' THEN b.anzeigename ELSE b.login END AS n FROM mitteilung_empfaenger e JOIN benutzer b ON b.id = e.benutzer_id
        WHERE e.mitteilung_id = @m ORDER BY n LIMIT 60
        """, r => r.Text("n"), Db.P("@m", id));

    private static bool Passt(Mitteilung m, string? suche)
    {
        if (string.IsNullOrWhiteSpace(suche)) return true;
        var f = suche.Trim();
        return m.Betreff.Contains(f, StringComparison.OrdinalIgnoreCase) || m.Text.Contains(f, StringComparison.OrdinalIgnoreCase) ||
               m.Absender.Contains(f, StringComparison.OrdinalIgnoreCase) || m.EmpfaengerText.Contains(f, StringComparison.OrdinalIgnoreCase) ||
               m.Empfaenger.Any(x => x.Contains(f, StringComparison.OrdinalIgnoreCase)) || (m.Anhaenge ?? new()).Any(a => a.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <param name="vertraulich">Schüler- bzw. Elternkonto: die übrigen Empfänger*innen (z. B. andere Eltern der Klasse) werden nicht angezeigt.</param>
    public List<Mitteilung> Posteingang(int ich, string? suche = null, bool vertraulich = false)
    {
        Aufraeumen();
        return _db.Liste("""
            SELECT m.*, e.gelesen, e.bestaetigt, CASE WHEN b.anzeigename <> '' THEN b.anzeigename ELSE b.login END AS absender, b.rolle AS absender_rolle
            FROM mitteilung_empfaenger e JOIN mitteilungen m ON m.id = e.mitteilung_id JOIN benutzer b ON b.id = m.absender_id
            WHERE e.benutzer_id = @b AND e.geloescht = 0 ORDER BY m.id DESC LIMIT 300
            """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), r.Text("absender"), r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"),
                r.TextOderNull("gelesen"), new(), Lesebestaetigung: r.Wahr("lesebestaetigung"), Bestaetigt: r.TextOderNull("bestaetigt"), AntwortAuf: r.ZahlOderNull("antwort_auf"),
                EmpfaengerText: r.Text("empfaenger_text"), AbsenderRolle: Rollen.Name(r.Text("absender_rolle"))) { AntwortErlaubt = !r.Wahr("keine_antwort") },
            Db.P("@b", ich)).Select(m => m with { Empfaenger = vertraulich ? new() : EmpfaengerNamen(m.Id), EmpfaengerText = vertraulich ? "" : m.EmpfaengerText, Anhaenge = Anhaenge(m.Id) })
            .Where(m => Passt(m, suche)).ToList();
    }

    public List<Mitteilung> Gesendet(int ich, string? suche = null)
    {
        Aufraeumen();
        return _db.Liste("""
            SELECT m.*, (SELECT COUNT(*) FROM mitteilung_empfaenger e WHERE e.mitteilung_id = m.id) AS anzahl,
                   (SELECT COUNT(*) FROM mitteilung_empfaenger e WHERE e.mitteilung_id = m.id AND e.gelesen IS NOT NULL) AS gelesen_anzahl,
                   (SELECT COUNT(*) FROM mitteilung_empfaenger e WHERE e.mitteilung_id = m.id AND e.bestaetigt IS NOT NULL) AS bestaetigt_anzahl
            FROM mitteilungen m WHERE m.absender_id = @b AND m.geloescht_absender = 0 AND m.entwurf = 0 ORDER BY m.id DESC LIMIT 300
            """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), "", r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), "", new(),
                Lesebestaetigung: r.Wahr("lesebestaetigung"), Anzahl: r.Zahl("anzahl"), GelesenAnzahl: r.Zahl("gelesen_anzahl"), BestaetigtAnzahl: r.Zahl("bestaetigt_anzahl"),
                AntwortAuf: r.ZahlOderNull("antwort_auf"), EmpfaengerText: r.Text("empfaenger_text")) { AntwortErlaubt = !r.Wahr("keine_antwort") },
            Db.P("@b", ich)).Select(m => m with { Empfaenger = EmpfaengerNamen(m.Id), Anhaenge = Anhaenge(m.Id) }).Where(m => Passt(m, suche)).ToList();
    }

    /// <summary>Lesebestätigungsliste (nur für den Absender): alle Empfänger*innen mit Zeitpunkt des Lesens und der Bestätigung.</summary>
    public List<Lesestatus> Lesestatus(Benutzer ich, int id)
    {
        var eigen = Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM mitteilungen WHERE id = @m AND absender_id = @a AND entwurf = 0", Db.P("@m", id), Db.P("@a", ich.Id)));
        if (eigen == 0) throw new UnauthorizedAccessException("Nur der Absender sieht die Lesebestätigungen.");
        return _db.Liste("""
            SELECT e.benutzer_id, e.gelesen, e.bestaetigt, b.rolle, CASE WHEN b.anzeigename <> '' THEN b.anzeigename ELSE b.login END AS n
            FROM mitteilung_empfaenger e JOIN benutzer b ON b.id = e.benutzer_id WHERE e.mitteilung_id = @m
            """, r => new Lesestatus(r.Zahl("benutzer_id"), r.Text("n"), Rollen.Name(r.Text("rolle")), r.TextOderNull("gelesen"), r.TextOderNull("bestaetigt")), Db.P("@m", id))
            .OrderBy(x => x.Bestaetigt is null).ThenBy(x => x.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), true)).ToList();
    }

    /// <summary>Lesebestätigung senden (Empfänger*in einer Mitteilung mit angeforderter Lesebestätigung).</summary>
    public string Bestaetigen(int ich, int id)
    {
        var anf = _db.Wert("""
            SELECT m.lesebestaetigung FROM mitteilungen m JOIN mitteilung_empfaenger e ON e.mitteilung_id = m.id WHERE m.id = @m AND e.benutzer_id = @b AND e.geloescht = 0
            """, Db.P("@m", id), Db.P("@b", ich));
        if (anf is null) throw new ArgumentException("Unbekannte Mitteilung.");
        if (Convert.ToInt32(anf) == 0) throw new ArgumentException("Für diese Mitteilung wurde keine Lesebestätigung angefordert.");
        var z = Jetzt;
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET bestaetigt = @z, gelesen = COALESCE(gelesen, @z) WHERE mitteilung_id = @m AND benutzer_id = @b AND bestaetigt IS NULL",
            Db.P("@z", z), Db.P("@m", id), Db.P("@b", ich));
        return Convert.ToString(_db.Wert("SELECT bestaetigt FROM mitteilung_empfaenger WHERE mitteilung_id = @m AND benutzer_id = @b", Db.P("@m", id), Db.P("@b", ich))) ?? z;
    }

    /// <summary>Vorschlag für eine Antwort: Empfänger (Absender bzw. alle Beteiligten), Betreff „AW: …“ und Zitat.</summary>
    public object Antwort(Benutzer ich, Stand? s, int id, bool alle)
    {
        var m = Posteingang(ich.Id).FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Mitteilung.");
        if (!DarfAntworten(ich)) throw new UnauthorizedAccessException("Ihr Konto darf keine Mitteilungen senden.");
        if (!m.AntwortErlaubt && m.AbsenderId != ich.Id) throw new ArgumentException(KeineAntwort);
        var ids = Antwortempfaenger(ich, id, alle);
        var namen = Empfaengerliste(ich, s).ToDictionary(x => x.Id, x => x.Name);
        foreach (var x in namen.Keys.Where(k => k.StartsWith("benutzer:") && alle).ToList())
            if (int.TryParse(x[9..], out var bid) && !ids.Contains(bid) && _db.Wert("SELECT 1 FROM mitteilung_empfaenger WHERE mitteilung_id = @m AND benutzer_id = @b", Db.P("@m", id), Db.P("@b", bid)) != null)
                ids.Add(bid);
        if (ids.Count == 0) throw new ArgumentException("Auf diese Mitteilung können Sie nicht antworten.");
        var empfaenger = ids.Select(i => _benutzer.Holen(i)).Where(b => b is { Gesperrt: false })
            .Select(b => new { id = "benutzer:" + b!.Id, name = namen.GetValueOrDefault("benutzer:" + b.Id, Anzeige(b)) }).ToList();
        // Absender zuerst
        empfaenger = empfaenger.OrderByDescending(x => x.id == "benutzer:" + m.AbsenderId).ToList();
        return new
        {
            empfaenger, antwortAuf = id,
            betreff = m.Betreff.StartsWith("AW:", StringComparison.OrdinalIgnoreCase) ? m.Betreff : "AW: " + m.Betreff,
            text = $"\n\n----- {m.Absender} schrieb am {m.Erstellt}: -----\n" + string.Join("\n", m.Text.Split('\n').Select(z => "> " + z)),
        };
    }

    /// <summary>Weiterleiten: neuer Entwurf mit Betreff „WG: …“, Zitat und Kopie der Anhänge.</summary>
    public Mitteilung Weiterleiten(Benutzer ich, int id)
    {
        if (!DarfSenden(ich)) throw new UnauthorizedAccessException("Ihr Konto darf keine Mitteilungen senden.");
        var m = Posteingang(ich.Id).FirstOrDefault(x => x.Id == id) ?? Gesendet(ich.Id).FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unbekannte Mitteilung.");
        var betreff = m.Betreff.StartsWith("WG:", StringComparison.OrdinalIgnoreCase) ? m.Betreff : "WG: " + m.Betreff;
        if (betreff.Length > 200) betreff = betreff[..200];
        var text = $"\n\n----- Weitergeleitete Mitteilung -----\nVon: {(m.Absender.Length > 0 ? m.Absender : "Ich")}\nGesendet: {m.Erstellt}\nBetreff: {m.Betreff}\n\n{m.Text}";
        if (text.Length > 20000) text = text[..20000];
        var neu = 0L;
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "INSERT INTO mitteilungen (absender_id, betreff, text, erstellt, wichtig, entwurf, entwurf_empfaenger, antwort_auf) VALUES (@a, @b, @t, @e, 0, 1, '[]', @af)",
                Db.P("@a", ich.Id), Db.P("@b", betreff), Db.P("@t", text), Db.P("@e", Jetzt), Db.P("@af", id));
            neu = Db.LetzteId(c);
            Db.Ausfuehren(c, "INSERT INTO mitteilung_anhaenge (mitteilung_id, name, typ, groesse, daten) SELECT @n, name, typ, groesse, daten FROM mitteilung_anhaenge WHERE mitteilung_id = @m",
                Db.P("@n", neu), Db.P("@m", id));
        });
        return Entwuerfe(ich.Id).First(x => x.Id == neu);
    }

    /// <summary>Eine Mitteilung des eigenen Kontos (Posteingang, Gesendet oder Entwürfe) mit dem Ordner; null = nicht vorhanden.</summary>
    public (Mitteilung Mitteilung, string Ordner)? Detail(Benutzer ich, int id)
    {
        if (Posteingang(ich.Id, null, ich.Familie).FirstOrDefault(x => x.Id == id) is { } p) return (p, "posteingang");
        if (Gesendet(ich.Id).FirstOrDefault(x => x.Id == id) is { } g) return (g, "gesendet");
        if (Entwuerfe(ich.Id).FirstOrDefault(x => x.Id == id) is { } e) return (e, "entwuerfe");
        return null;
    }

    // ------------------------------------------------------------------------------------------ Entwürfe

    public List<Mitteilung> Entwuerfe(int ich, string? suche = null) => _db.Liste("""
        SELECT m.* FROM mitteilungen m WHERE m.absender_id = @b AND m.geloescht_absender = 0 AND m.entwurf = 1 ORDER BY m.id DESC LIMIT 300
        """, r => new Mitteilung(r.Zahl("id"), r.Zahl("absender_id"), "", r.Text("betreff"), r.Text("text"), r.Text("erstellt"), r.Wahr("wichtig"), "", new(),
            null, JsonSerializer.Deserialize<List<string>>(r.Text("entwurf_empfaenger")) ?? new(), Lesebestaetigung: r.Wahr("lesebestaetigung"),
            AntwortAuf: r.ZahlOderNull("antwort_auf"), EmpfaengerText: r.Text("empfaenger_text")) { AntwortErlaubt = !r.Wahr("keine_antwort") },
        Db.P("@b", ich)).Select(m => m with { Anhaenge = Anhaenge(m.Id) }).Where(m => Passt(m, suche)).ToList();

    /// <summary>Entwurf anlegen bzw. ändern. Empfänger werden nur geprüft (aufgelöst wird erst beim Senden).</summary>
    public int EntwurfSpeichern(Benutzer ich, Stand? s, int? id, IEnumerable<string> empfaenger, string betreff, string text, bool wichtig, bool lesebestaetigung = false, int? antwortAuf = null,
        bool antwortErlaubt = true)
    {
        if ((betreff ?? "").Length > 200 || (text ?? "").Length > 20000) throw new ArgumentException("Betreff oder Text ist zu lang.");
        var erlaubt = Empfaengerliste(ich, s).Select(x => x.Id).ToHashSet();
        if (id is int vorhanden && antwortAuf is null)
            antwortAuf = Entwuerfe(ich.Id).FirstOrDefault(m => m.Id == vorhanden)?.AntwortAuf;
        SendenOderAntwortenPruefen(ich, antwortAuf);
        var antwort = Antwortempfaenger(ich, antwortAuf, true);
        var liste = empfaenger.Distinct().ToList();
        if (liste.Any(e => !erlaubt.Contains(e) && !(e.StartsWith("benutzer:") && int.TryParse(e[9..], out var b) && antwort.Contains(b))))
            throw new ArgumentException("Unzulässiger Empfänger.");
        var p = new[] { Db.P("@a", ich.Id), Db.P("@b", (betreff ?? "").Trim()), Db.P("@t", (text ?? "").Trim()), Db.P("@e", Jetzt),
            Db.P("@w", wichtig && ich.Kollegium), Db.P("@em", JsonSerializer.Serialize(liste)), Db.P("@id", id), Db.P("@l", lesebestaetigung), Db.P("@af", antwortAuf),
            Db.P("@et", EmpfaengerText(ich, s, liste)), Db.P("@ka", antwortErlaubt ? 0 : 1) };
        if (AntwortGesperrt(ich, antwortAuf)) throw new ArgumentException(KeineAntwort);
        if (id is null)
            return (int)_db.Einfuegen("""
                INSERT INTO mitteilungen (absender_id, betreff, text, erstellt, wichtig, entwurf, entwurf_empfaenger, lesebestaetigung, antwort_auf, empfaenger_text, keine_antwort)
                VALUES (@a, @b, @t, @e, @w, 1, @em, @l, @af, @et, @ka)
                """, p);
        if (_db.Ausfuehren("""
                UPDATE mitteilungen SET betreff = @b, text = @t, erstellt = @e, wichtig = @w, entwurf_empfaenger = @em, lesebestaetigung = @l, antwort_auf = @af, empfaenger_text = @et,
                    keine_antwort = @ka
                WHERE id = @id AND absender_id = @a AND entwurf = 1
                """, p) == 0)
            throw new ArgumentException("Unbekannter Entwurf.");
        return id.Value;
    }

    /// <summary>Entwurf senden: Empfänger auflösen und zustellen.</summary>
    public int EntwurfSenden(Benutzer ich, Stand? s, int id)
    {
        var e = Entwuerfe(ich.Id).FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException("Unbekannter Entwurf.");
        Pruefen(e.Betreff, e.Text);
        var ids = Aufloesen(ich, s, e.EntwurfEmpfaenger ?? new(), e.AntwortAuf);
        if (ids.Count == 0) throw new ArgumentException("Bitte mindestens einen Empfänger wählen.");
        _db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "UPDATE mitteilungen SET entwurf = 0, erstellt = @e WHERE id = @m", Db.P("@e", Jetzt), Db.P("@m", id));
            foreach (var b in ids) Db.Ausfuehren(c, "INSERT INTO mitteilung_empfaenger (mitteilung_id, benutzer_id) VALUES (@m, @b)", Db.P("@m", id), Db.P("@b", b));
        });
        return id;
    }

    // ------------------------------------------------------------------------------------------ Listen

    public List<Mitteilungsliste> Listen(Benutzer ich, Stand? s, bool mitNamen = true)
    {
        var namen = mitNamen ? Empfaengerliste(ich, s, false).ToDictionary(x => x.Id, x => x.Name) : new();
        return _db.Liste("SELECT * FROM mitteilung_listen WHERE besitzer_id = @b ORDER BY name", r =>
        {
            var m = JsonSerializer.Deserialize<List<string>>(r.Text("mitglieder")) ?? new();
            return new Mitteilungsliste(r.Zahl("id"), r.Text("name"), m, m.Select(x => namen.GetValueOrDefault(x, x)).ToList(), r.Text("erstellt"));
        }, Db.P("@b", ich.Id));
    }

    public Mitteilungsliste ListeSpeichern(Benutzer ich, Stand? s, int? id, string name, IEnumerable<string> mitglieder)
    {
        if (!ich.Kollegium) throw new UnauthorizedAccessException("Listen stehen nur dem Kollegium zur Verfügung.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte einen Namen eingeben.");
        var erlaubt = Empfaengerliste(ich, s, false).Select(x => x.Id).ToHashSet();
        var m = mitglieder.Distinct().Where(erlaubt.Contains).ToList();
        if (m.Count == 0) throw new ArgumentException("Bitte mindestens ein Mitglied wählen.");
        var p = new[] { Db.P("@b", ich.Id), Db.P("@n", name.Trim()), Db.P("@m", JsonSerializer.Serialize(m)), Db.P("@e", Jetzt), Db.P("@id", id) };
        if (id is null) id = (int)_db.Einfuegen("INSERT INTO mitteilung_listen (besitzer_id, name, mitglieder, erstellt) VALUES (@b, @n, @m, @e)", p);
        else if (_db.Ausfuehren("UPDATE mitteilung_listen SET name = @n, mitglieder = @m WHERE id = @id AND besitzer_id = @b", p) == 0) throw new ArgumentException("Unbekannte Liste.");
        return Listen(ich, s).First(l => l.Id == id);
    }

    public void ListeLoeschen(Benutzer ich, int id) => _db.Ausfuehren("DELETE FROM mitteilung_listen WHERE id = @id AND besitzer_id = @b", Db.P("@id", id), Db.P("@b", ich.Id));

    // ------------------------------------------------------------------------------------------ Anhänge

    public List<Anhang> Anhaenge(int mitteilung) => _db.Liste("SELECT id, name, typ, groesse FROM mitteilung_anhaenge WHERE mitteilung_id = @m ORDER BY id",
        r => new Anhang(r.Zahl("id"), r.Text("name"), r.Text("typ"), r.Zahl("groesse")), Db.P("@m", mitteilung));

    /// <summary>Anhang an eine eigene Mitteilung (Entwurf) hängen – Größe und Anzahl laut Einstellungen (Standard 7 MB, 5 Anhänge).</summary>
    public Anhang AnhangHinzufuegen(Benutzer ich, int mitteilung, string name, string typ, byte[] daten)
    {
        var e = Einstellungen();
        var eigen = Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM mitteilungen WHERE id = @m AND absender_id = @a AND entwurf = 1", Db.P("@m", mitteilung), Db.P("@a", ich.Id)));
        if (eigen == 0) throw new ArgumentException("Anhänge können nur an eigene Entwürfe gehängt werden.");
        if (daten.Length == 0) throw new ArgumentException("Die Datei ist leer.");
        if (daten.Length > e.MaxAnhangMb * 1024 * 1024) throw new ArgumentException($"Ein Anhang darf höchstens {e.MaxAnhangMb} MB groß sein.");
        if (Anhaenge(mitteilung).Count >= e.MaxAnhaenge) throw new ArgumentException($"Höchstens {e.MaxAnhaenge} Anhänge je Mitteilung.");
        name = Path.GetFileName(name ?? "Anhang").Trim();
        if (name.Length == 0) name = "Anhang";
        if (name.Length > 120) name = name[..120];
        var id = _db.Einfuegen("INSERT INTO mitteilung_anhaenge (mitteilung_id, name, typ, groesse, daten) VALUES (@m, @n, @t, @g, @d)",
            Db.P("@m", mitteilung), Db.P("@n", name), Db.P("@t", string.IsNullOrWhiteSpace(typ) ? "application/octet-stream" : typ), Db.P("@g", daten.Length), Db.P("@d", daten));
        return Anhaenge(mitteilung).First(a => a.Id == id);
    }

    public void AnhangLoeschen(Benutzer ich, int id) => _db.Ausfuehren("""
        DELETE FROM mitteilung_anhaenge WHERE id = @i AND mitteilung_id IN (SELECT id FROM mitteilungen WHERE absender_id = @a AND entwurf = 1)
        """, Db.P("@i", id), Db.P("@a", ich.Id));

    /// <summary>Anhang laden (nur für Absender und Empfänger).</summary>
    public (string Name, string Typ, byte[] Daten)? AnhangLaden(Benutzer ich, int id) => _db.Einzel("""
        SELECT a.name, a.typ, a.daten FROM mitteilung_anhaenge a JOIN mitteilungen m ON m.id = a.mitteilung_id
        WHERE a.id = @i AND (m.absender_id = @b OR EXISTS (SELECT 1 FROM mitteilung_empfaenger e WHERE e.mitteilung_id = m.id AND e.benutzer_id = @b AND e.geloescht = 0))
        """, r => new Tuple<string, string, byte[]>(r.Text("name"), r.Text("typ"), (byte[])r["daten"]), Db.P("@i", id), Db.P("@b", ich.Id)) is { } t ? (t.Item1, t.Item2, t.Item3) : null;

    // ------------------------------------------------------------------------------------------ Status, Löschen, Aufbewahrung

    public int Ungelesen(int ich) => Convert.ToInt32(_db.Wert("SELECT COUNT(*) FROM mitteilung_empfaenger WHERE benutzer_id = @b AND gelesen IS NULL AND geloescht = 0", Db.P("@b", ich)));

    public void Gelesen(int ich, int id) =>
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET gelesen = @z WHERE benutzer_id = @b AND mitteilung_id = @m AND gelesen IS NULL",
            Db.P("@z", Jetzt), Db.P("@b", ich), Db.P("@m", id));

    /// <summary>Als ungelesen markieren (Posteingang).</summary>
    public void Ungelesen(int ich, int id) =>
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET gelesen = NULL WHERE benutzer_id = @b AND mitteilung_id = @m AND bestaetigt IS NULL", Db.P("@b", ich), Db.P("@m", id));

    public void Loeschen(int ich, int id)
    {
        _db.Ausfuehren("UPDATE mitteilung_empfaenger SET geloescht = 1 WHERE benutzer_id = @b AND mitteilung_id = @m", Db.P("@b", ich), Db.P("@m", id));
        _db.Ausfuehren("UPDATE mitteilungen SET geloescht_absender = 1 WHERE absender_id = @b AND id = @m", Db.P("@b", ich), Db.P("@m", id));
    }

    /// <summary>Mitteilungen nach der Aufbewahrungsfrist löschen (Standard: ein Jahr; Entwürfe bleiben) – höchstens einmal je Stunde.</summary>
    public int Aufraeumen(bool sofort = false)
    {
        if (!sofort && (_uhr.Jetzt - _aufgeraeumt).TotalHours < 1 && _aufgeraeumt <= _uhr.Jetzt) return 0;
        _aufgeraeumt = _uhr.Jetzt;
        var grenze = _uhr.Jetzt.AddDays(-Einstellungen().AufbewahrungTage).ToString("yyyy-MM-dd HH:mm");
        return _db.Ausfuehren("DELETE FROM mitteilungen WHERE entwurf = 0 AND erstellt < @g", Db.P("@g", grenze));
    }
}
