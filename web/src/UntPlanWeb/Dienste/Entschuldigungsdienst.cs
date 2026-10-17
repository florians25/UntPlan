using System.Globalization;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Entschuldigung einer Fehlstunde (wer, wann, Text, Rolle „fachlehrer“ bzw. „klassenleitung“).</summary>
public sealed record StundenEntschuldigung(string Status, string Text, string Lehrer, string Rolle, string Datum, string Zeit);

/// <summary>Entschuldigungstext einer anderen Stunde derselben Abwesenheit („zuletzt eingegebene Entschuldigungstexte“).</summary>
public sealed record Entschuldigungstext(int Stunde, string Datum, string Fach, string Lehrer, string Status, string Text, string Zeit, string Rolle);

/// <summary>Fehlstunde in der Fachlehrer-Sicht (Seite „Fehlzeiten“, App) mit Entschuldigung, Texten derselben Abwesenheit und Sperre.</summary>
public sealed record FachlehrerZeile(int Id, int SchuelerId, string Schueler, string Klasse, string Datum, int Stunde, string Beginn, string Ende, string Fach,
    string Lehrer, string Art, int Fehlminuten, string Grund, string Status, string AbwesenheitText, StundenEntschuldigung? Entschuldigung,
    List<Entschuldigungstext> Texte, bool Darf, bool Gesperrt, string Hinweis);

/// <summary>Stunde einer Abwesenheit in der Klassenleitungs-Sicht (Seitenleiste „Fehlzeiten“).</summary>
public sealed record AbwesenheitsStunde(int Id, string Datum, int Stunde, string Beginn, string Ende, string Fach, string Lehrer, string Status, StundenEntschuldigung? Entschuldigung);

/// <summary>Abwesenheit mit Stand der Fachlehrer-Entschuldigungen („2/3 Lehrer bearbeitet“, mehrdeutig).</summary>
public sealed record EntschuldigungsAbwesenheit(string Schluessel, List<int> Ids, int SchuelerId, string Schueler, string Klasse, string VonDatum, string VonZeit,
    string BisDatum, string BisZeit, string Art, string Grund, string Status, bool Mehrdeutig, string Text, int Fehlstunden, string Nachweis, string EntschuldigtAm,
    int LehrerGesamt, int LehrerBearbeitet, bool Darf, List<AbwesenheitsStunde> Stunden);

/// <summary>
/// Fachlehrer-Entschuldigungen (Klassenbuch | Einstellungen „Fachlehrer-Entschuldigungen“, Recht „Fehlstunden im eigenen Unterricht entschuldigen“):
/// <list type="bullet">
/// <item>Lehrkräfte entschuldigen einzelne Fehlstunden aus ihrem Unterricht (auch mehrere auf einmal) mit Text; der Text ist für andere Lehrkräfte
/// mit einer Fehlstunde in derselben Abwesenheit sichtbar. Ein einmal gesetzter Status ist für Fachlehrkräfte nicht mehr änderbar.</item>
/// <item>Haben alle Stunden einer Abwesenheit denselben Status, hat die Abwesenheit diesen Status; sonst bleibt sie „offen“ (mehrdeutig).</item>
/// <item>Die Klassenleitung entschuldigt die ganze Abwesenheit und überschreibt dabei die Status der Fachlehrkräfte; sie kann die Entschuldigung
/// auch zurücknehmen. Ihr Text ist für die Fachlehrkräfte im (gesperrten) Entschuldigungsdialog sichtbar.</item>
/// </list>
/// Der Status je Stunde steht wie bisher in <c>fehlzeiten.status</c> (Berichte, Listen, Sync); Text, Lehrkraft und Rolle in
/// <c>fehlzeit_entschuldigungen</c> (Schlüssel Schüler*in, Datum, Stunde – überdauert das Neuspeichern der Anwesenheit).
/// </summary>
public sealed class Entschuldigungsdienst
{
    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Klassenbuchdienst _kb;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    public const string RolleFachlehrer = "fachlehrer", RolleKlassenleitung = "klassenleitung";

    public Entschuldigungsdienst(Db db, Uhr uhr, Klassenbuchdienst kb) { _db = db; _uhr = uhr; _kb = kb; }

    private string Jetzt => _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------ Rechte

    /// <summary>Modul eingeschaltet, Recht vorhanden und Lehrkraft mit Kürzel?</summary>
    public bool FachlehrerAktiv(Benutzer b, KlassenbuchEinstellungen? einst = null) =>
        (einst ?? _kb.Einstellungen()).FachlehrerEntschuldigungen && b.Kollegium && b.LehrerKuerzel is { Length: > 0 } && Rechtedienst.Darf(b, Recht.AbwesenheitFachlehrer);

    /// <summary>Klassen, deren Abwesenheiten das Konto als Klassenleitung (bzw. mit „alle Klassen“) entschuldigen darf.</summary>
    public static List<string> KlassenleitungKlassen(Benutzer b, Stand s) =>
        s.Paket.Elemente.Klassen.Select(k => k.Kurz).Where(k => Klassenbuchdienst.DarfEntschuldigen(b, s, k)).ToList();

    public object Einstellungen(Benutzer b, Stand? s)
    {
        var einst = _kb.Einstellungen();
        var klassen = s is null ? new List<string>() : KlassenleitungKlassen(b, s);
        return new
        {
            aktiv = einst.FachlehrerEntschuldigungen, fachlehrer = FachlehrerAktiv(b, einst), klassenleitung = klassen.Count > 0, klassen,
            jahrgangEinschraenken = einst.FachlehrerJahrgangEinschraenken, jahrgangVon = einst.FachlehrerJahrgangVon, jahrgangBis = einst.FachlehrerJahrgangBis,
        };
    }

    private static string Jahrgang(Stand s, string klasse) => s.Paket.Elemente.Klassen.FirstOrDefault(k => Ci.Equals(k.Kurz, klasse))?.Jahrgang ?? "";

    private static bool Unterrichtet(Fehlzeile z, string kuerzel) =>
        z.Lehrer.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(kuerzel, Ci);

    /// <summary>Darf die Fachlehrkraft diese Fehlstunde entschuldigen? (Darf, Gesperrt, Hinweis)</summary>
    private (bool Darf, bool Gesperrt, string Hinweis) Pruefen(Benutzer b, Stand s, KlassenbuchEinstellungen einst, Fehlzeile z, StundenEntschuldigung? ent)
    {
        if (!FachlehrerAktiv(b, einst)) return (false, false, einst.FachlehrerEntschuldigungen ? "Ihr Konto darf keine Fehlstunden entschuldigen." : "Die Fachlehrer-Entschuldigungen sind nicht eingeschaltet.");
        if (!Unterrichtet(z, b.LehrerKuerzel!)) return (false, false, "Diese Stunde gehört nicht zu Ihrem Unterricht.");
        if (Klassenbuchdienst.IstKlassenleitung(b, s, z.Klasse)) return (false, false, "Als Klassenleitung entschuldigen Sie die ganze Abwesenheit unter „Abwesenheiten“.");
        if (!einst.FachlehrerJahrgangErlaubt(Jahrgang(s, z.Klasse))) return (false, false, "Für diese Jahrgangsstufe sind Fachlehrer-Entschuldigungen nicht freigegeben.");
        if (z.Status != "offen")
            return (false, true, ent?.Rolle == RolleFachlehrer
                ? $"Bereits von {ent.Lehrer} {(z.Status == "entschuldigt" ? "entschuldigt" : "als nicht entschuldigt markiert")} – der Status kann nicht mehr geändert werden."
                : $"Von der Klassenleitung {(z.Status == "entschuldigt" ? "entschuldigt" : z.Status == "unentschuldigt" ? "als nicht entschuldigt markiert" : "bearbeitet")} – der Status kann nicht mehr geändert werden.");
        return (true, false, "");
    }

    // ------------------------------------------------------------------------------------------ Lesen

    private Dictionary<(int, string, int), StundenEntschuldigung> Entschuldigungen(IEnumerable<Fehlzeit> liste)
    {
        var res = new Dictionary<(int, string, int), StundenEntschuldigung>();
        var schueler = liste.Select(f => f.SchuelerId).Distinct().ToList();
        if (schueler.Count == 0) return res;
        var von = liste.Min(f => f.Datum)!;
        var bis = liste.Max(f => f.Datum)!;
        foreach (var teil in schueler.Chunk(400))
            foreach (var (k, e) in _db.Liste($"SELECT * FROM fehlzeit_entschuldigungen WHERE datum >= @v AND datum <= @b AND schueler_id IN ({string.Join(",", teil)})",
                         r => ((r.Zahl("schueler_id"), r.Text("datum"), r.Zahl("stunde")),
                             new StundenEntschuldigung(r.Text("status"), r.Text("text"), r.Text("lehrer"), r.Text("rolle"), r.Text("entschuldigt_am"), r.Text("erstellt"))),
                         Db.P("@v", von), Db.P("@b", bis)))
                res[k] = e;
        return res;
    }

    /// <summary>Abwesenheit einer Fehlzeit: Gruppe (als Zeitraum erfasst) bzw. derselbe Tag.</summary>
    private static string AbwesenheitSchluessel(Fehlzeit f) =>
        f.Gruppe.Length > 0 && !f.Gruppe.StartsWith("vorzeitig:", StringComparison.Ordinal) ? $"{f.SchuelerId}|g|{f.Gruppe}" : $"{f.SchuelerId}|d|{f.Datum}";

    /// <summary>
    /// Fachlehrer-Sicht: Fehlstunden aus dem eigenen Unterricht im Zeitraum (optional Klasse, Status, nur offene). Klassen = Klassen dieser Fehlstunden.
    /// </summary>
    public object Fachlehrerliste(Benutzer b, Stand s, DateOnly von, DateOnly bis, string? klasse, string? status, bool nurOffene)
    {
        if (!b.Kollegium || b.LehrerKuerzel is not { Length: > 0 } kuerzel) throw new UnauthorizedAccessException("Die Fachlehrer-Sicht steht Lehrkräften zur Verfügung.");
        if (bis < von) (von, bis) = (bis, von);
        if (bis.DayNumber - von.DayNumber > 400) von = bis.AddDays(-400);
        var einst = _kb.Einstellungen();
        var alle = _kb.FehlzeitenZeitraum(von, bis);
        var zeilen = _kb.Fehlzeilen(s, alle);
        var fzNachId = alle.ToDictionary(f => f.Id);
        var eigene = zeilen.Where(z => Unterrichtet(z, kuerzel)).ToList();
        // Texte anderer Stunden derselben Abwesenheit (über den Zeitraum hinaus: Abwesenheiten über mehrere Tage bleiben vollständig)
        var ent = Entschuldigungen(alle);
        var proAbwesenheit = alle.GroupBy(AbwesenheitSchluessel).ToDictionary(g => g.Key, g => g.ToList());
        var lehrerVon = zeilen.ToDictionary(z => z.Id, z => z.Lehrer);
        var res = eigene
            .Where(z => klasse is null || Ci.Equals(z.Klasse, klasse))
            .Where(z => (!nurOffene || z.Status == "offen") && (status is null || z.Status == status))
            .Select(z =>
            {
                var f = fzNachId[z.Id];
                var e = ent.GetValueOrDefault((f.SchuelerId, f.Datum, f.Stunde));
                var (darf, gesperrt, hinweis) = Pruefen(b, s, einst, z, e);
                var texte = proAbwesenheit[AbwesenheitSchluessel(f)].Where(x => x.Id != f.Id)
                    .Select(x => (x, e2: ent.GetValueOrDefault((x.SchuelerId, x.Datum, x.Stunde))))
                    .Where(t => t.e2 is { } && t.e2.Text.Length > 0)
                    .Select(t => new Entschuldigungstext(t.x.Stunde, t.x.Datum, t.x.Fach, t.e2!.Lehrer.Length > 0 ? t.e2.Lehrer : lehrerVon.GetValueOrDefault(t.x.Id, ""),
                        t.x.Status, t.e2.Text, t.e2.Zeit, t.e2.Rolle))
                    .DistinctBy(t => (t.Text, t.Lehrer)).OrderByDescending(t => t.Zeit, StringComparer.Ordinal).ToList();
                return new FachlehrerZeile(z.Id, z.SchuelerId, z.Schueler, z.Klasse, z.Datum, z.Stunde, z.Beginn, z.Ende, z.Fach, z.Lehrer, z.Art, z.Fehlminuten, z.Grund,
                    z.Status, z.Text, e, texte, darf, gesperrt, hinweis);
            })
            .OrderByDescending(z => z.Datum, StringComparer.Ordinal).ThenBy(z => z.Stunde).ThenBy(z => z.Schueler, StringComparer.Create(new CultureInfo("de-DE"), true)).ToList();
        return new
        {
            klassen = eigene.Select(z => z.Klasse).Distinct(Ci).OrderBy(k => k, StringComparer.Create(new CultureInfo("de-DE"), true)).ToList(),
            darf = FachlehrerAktiv(b, einst), zeilen = res,
        };
    }

    /// <summary>Klassenleitungs-Sicht: Abwesenheiten der eigenen Klassen mit Stunden, Entschuldigungen und „x/y Lehrer bearbeitet“.</summary>
    public List<EntschuldigungsAbwesenheit> Abwesenheiten(Benutzer b, Stand s, DateOnly von, DateOnly bis, string? klasse, string? status)
    {
        if (!Klassenbuchdienst.FehlzeitenVerwalten(b, s)) throw new UnauthorizedAccessException("Die Abwesenheitsverwaltung steht den Klassenleitungen zur Verfügung.");
        if (bis < von) (von, bis) = (bis, von);
        if (bis.DayNumber - von.DayNumber > 400) von = bis.AddDays(-400);
        var liste = _kb.FehlzeitenZeitraum(von, bis, klasse).Where(f => Klassenbuchdienst.IstKlassenleitung(b, s, f.Klasse)).ToList();
        var res = Anreichern(b, s, liste);
        return status is null ? res : res.Where(z => z.Status == status).ToList();
    }

    private List<EntschuldigungsAbwesenheit> Anreichern(Benutzer b, Stand s, List<Fehlzeit> liste)
    {
        var zeilen = _kb.Fehlzeilen(s, liste).ToDictionary(z => z.Id);
        var ent = Entschuldigungen(liste);
        var fz = liste.ToDictionary(f => f.Id);
        return _kb.Zeitraeume(s, liste, k => Klassenbuchdienst.DarfEntschuldigen(b, s, k)).Select(z =>
        {
            var stunden = z.Ids.Where(fz.ContainsKey).Select(i => fz[i]).OrderBy(f => f.Datum, StringComparer.Ordinal).ThenBy(f => f.Stunde).Select(f =>
            {
                var zl = zeilen.GetValueOrDefault(f.Id);
                return new AbwesenheitsStunde(f.Id, f.Datum, f.Stunde, zl?.Beginn ?? "", zl?.Ende ?? "", f.Fach, zl?.Lehrer ?? f.Lehrer, f.Status, ent.GetValueOrDefault((f.SchuelerId, f.Datum, f.Stunde)));
            }).ToList();
            var proLehrer = stunden.Where(x => x.Lehrer.Length > 0).GroupBy(x => x.Lehrer, Ci).ToList();
            return new EntschuldigungsAbwesenheit(z.Schluessel, z.Ids, z.SchuelerId, z.Schueler, z.Klasse, z.VonDatum, z.VonZeit, z.BisDatum, z.BisZeit, z.Art, z.Grund,
                z.Status, z.StatusGemischt, z.Text, z.Fehlstunden, z.Nachweis, z.EntschuldigtAm, proLehrer.Count, proLehrer.Count(g => g.All(x => x.Status != "offen")),
                z.Verwalten, stunden);
        }).ToList();
    }

    /// <summary>Detail einer Abwesenheit (Seitenleiste): Klassenleitung oder Lehrkraft mit einer eigenen Stunde darin (dann nur lesen).</summary>
    public EntschuldigungsAbwesenheit Detail(Benutzer b, Stand s, IEnumerable<int> ids)
    {
        var liste = Holen(ids);
        if (liste.Count == 0) throw new ArgumentException("Die Abwesenheit gibt es nicht mehr – bitte die Liste neu laden.");
        if (liste.Select(f => f.SchuelerId).Distinct().Count() != 1) throw new ArgumentException("Bitte die Fehlstunden genau einer Abwesenheit angeben.");
        var kl = liste.All(f => Klassenbuchdienst.IstKlassenleitung(b, s, f.Klasse) || Rechtedienst.Darf(b, Recht.AbwesenheitAlle));
        if (!kl)
        {
            var zeilen = _kb.Fehlzeilen(s, liste);
            if (b.LehrerKuerzel is not { Length: > 0 } l || !zeilen.Any(z => Unterrichtet(z, l))) throw new UnauthorizedAccessException("Diese Abwesenheit gehört nicht zu Ihren Klassen bzw. Ihrem Unterricht.");
        }
        return Anreichern(b, s, liste).OrderByDescending(z => z.Ids.Count).First();
    }

    private List<Fehlzeit> Holen(IEnumerable<int> ids)
    {
        var liste = new List<Fehlzeit>();
        foreach (var teil in ids.Distinct().Chunk(400))
            liste.AddRange(_db.Liste($"SELECT * FROM fehlzeiten WHERE id IN ({string.Join(",", teil)})", Klassenbuchdienst.FehlzeitLesen));
        return liste;
    }

    // ------------------------------------------------------------------------------------------ Schreiben

    private static string Text(string? t)
    {
        var x = (t ?? "").Trim();
        return x.Length <= 1000 ? x : throw new ArgumentException("Der Entschuldigungstext darf höchstens 1000 Zeichen lang sein.");
    }

    private static string DatumOderHeute(string? d, DateOnly heute) =>
        string.IsNullOrWhiteSpace(d) ? Plandienst.Iso(heute) : DateOnly.TryParse(d, CultureInfo.InvariantCulture, out var x) ? Plandienst.Iso(x) : throw new ArgumentException("Ungültiges Datum der Entschuldigung.");

    private static void Merken(System.Data.Common.DbConnection c, Fehlzeit f, string status, string text, string lehrer, string rolle, string datum, string jetzt, int benutzer)
    {
        Db.Ausfuehren(c, "DELETE FROM fehlzeit_entschuldigungen WHERE schueler_id = @s AND datum = @d AND stunde = @st", Db.P("@s", f.SchuelerId), Db.P("@d", f.Datum), Db.P("@st", f.Stunde));
        Db.Ausfuehren(c, """
            INSERT INTO fehlzeit_entschuldigungen (schueler_id, datum, stunde, status, text, lehrer, rolle, entschuldigt_am, erstellt, benutzer_id)
            VALUES (@s, @d, @st, @status, @t, @l, @r, @ea, @e, @b)
            """, Db.P("@s", f.SchuelerId), Db.P("@d", f.Datum), Db.P("@st", f.Stunde), Db.P("@status", status), Db.P("@t", text), Db.P("@l", lehrer), Db.P("@r", rolle),
            Db.P("@ea", datum), Db.P("@e", jetzt), Db.P("@b", benutzer));
    }

    /// <summary>
    /// Fachlehrkraft entschuldigt Fehlstunden aus ihrem Unterricht (Status entschuldigt bzw. nicht entschuldigt, Text, Datum). Stunden mit schon
    /// gesetztem Status werden übersprungen (Ergebnis „gesperrt“); fehlt das Recht für eine Stunde, wird nichts geändert.
    /// </summary>
    public (int Geaendert, List<int> Gesperrt) StundenEntschuldigen(Benutzer b, Stand s, IEnumerable<int> ids, string? status, string? text, string? datum)
    {
        if (status is not ("entschuldigt" or "unentschuldigt")) throw new ArgumentException("Bitte „entschuldigt“ oder „nicht entschuldigt“ wählen.");
        var einst = _kb.Einstellungen();
        if (!FachlehrerAktiv(b, einst))
            throw new UnauthorizedAccessException(einst.FachlehrerEntschuldigungen ? "Ihr Konto darf keine Fehlstunden entschuldigen." : "Die Fachlehrer-Entschuldigungen sind nicht eingeschaltet.");
        var t = Text(text);
        var am = DatumOderHeute(datum, _uhr.Heute);
        var liste = Holen(ids);
        if (liste.Count == 0) throw new ArgumentException("Die Fehlstunden gibt es nicht mehr – bitte die Liste neu laden.");
        var zeilen = _kb.Fehlzeilen(s, liste).ToDictionary(z => z.Id);
        var ent = Entschuldigungen(liste);
        var gesperrt = new List<int>();
        var frei = new List<Fehlzeit>();
        foreach (var f in liste)
        {
            var (darf, sperre, hinweis) = Pruefen(b, s, einst, zeilen[f.Id], ent.GetValueOrDefault((f.SchuelerId, f.Datum, f.Stunde)));
            if (sperre) { gesperrt.Add(f.Id); continue; }
            if (!darf) throw new UnauthorizedAccessException(hinweis);
            if (DateOnly.TryParse(f.Datum, CultureInfo.InvariantCulture, out var fd))
            {
                Klassenbuchdienst.RueckwirkendPruefen(b, fd, _uhr.Heute);
                einst.EingabePruefen(b, fd, _uhr.Heute);
            }
            frei.Add(f);
        }
        var jetzt = Jetzt;
        _db.Transaktion(c =>
        {
            foreach (var f in frei)
            {
                Db.Ausfuehren(c, "UPDATE fehlzeiten SET status = @s WHERE id = @i AND status = 'offen'", Db.P("@s", status), Db.P("@i", f.Id));
                Merken(c, f, status, t, b.LehrerKuerzel!, RolleFachlehrer, am, jetzt, b.Id);
            }
        });
        return (frei.Count, gesperrt);
    }

    /// <summary>
    /// Klassenleitung entschuldigt ganze Abwesenheiten (alle Stunden, überschreibt die Status der Fachlehrkräfte) bzw. nimmt die Entschuldigung zurück
    /// (Status „offen“: alle Stunden offen, Entschuldigungstexte entfernt). Ergebnis: geänderte Stunden und überschriebene Fachlehrer-Status.
    /// </summary>
    public (int Geaendert, int Ueberschrieben) AbwesenheitEntschuldigen(Benutzer b, Stand s, IEnumerable<int> ids, string? status, string? text, string? datum, string? nachweis = null)
    {
        if (status is not ("entschuldigt" or "unentschuldigt" or "offen")) throw new ArgumentException("Unbekannter Status.");
        var liste = Holen(ids);
        if (liste.Count == 0) throw new ArgumentException("Die Abwesenheit gibt es nicht mehr – bitte die Liste neu laden.");
        foreach (var f in liste)
        {
            if (!Klassenbuchdienst.DarfEntschuldigen(b, s, f.Klasse)) throw new UnauthorizedAccessException("Die ganze Abwesenheit entschuldigt die Klassenleitung.");
            if (DateOnly.TryParse(f.Datum, CultureInfo.InvariantCulture, out var fd)) Klassenbuchdienst.RueckwirkendPruefen(b, fd, _uhr.Heute);
        }
        var t = Text(text);
        var am = DatumOderHeute(datum, _uhr.Heute);
        var ent = Entschuldigungen(liste);
        var ueber = liste.Count(f => ent.GetValueOrDefault((f.SchuelerId, f.Datum, f.Stunde)) is { Rolle: RolleFachlehrer } e && e.Status != status);
        var jetzt = Jetzt;
        var lehrer = b.LehrerKuerzel ?? b.Login;
        _db.Transaktion(c =>
        {
            foreach (var f in liste)
            {
                if (status == "offen")
                {
                    Db.Ausfuehren(c, "UPDATE fehlzeiten SET status = 'offen' WHERE id = @i", Db.P("@i", f.Id));
                    Db.Ausfuehren(c, "DELETE FROM fehlzeit_entschuldigungen WHERE schueler_id = @s AND datum = @d AND stunde = @st", Db.P("@s", f.SchuelerId), Db.P("@d", f.Datum), Db.P("@st", f.Stunde));
                    continue;
                }
                Db.Ausfuehren(c, "UPDATE fehlzeiten SET status = @s, entschuldigt_am = @ea, nachweis = COALESCE(@n, nachweis) WHERE id = @i",
                    Db.P("@s", status), Db.P("@ea", am), Db.P("@n", nachweis is null ? null : Klassenbuchverwaltung.Nachweis(nachweis)), Db.P("@i", f.Id));
                Merken(c, f, status, t, lehrer, RolleKlassenleitung, am, jetzt, b.Id);
            }
        });
        return (liste.Count, status == "offen" ? 0 : ueber);
    }

    /// <summary>Entschuldigung zurücknehmen (Klassenleitung): alle Stunden offen, Texte entfernt.</summary>
    public int Zuruecknehmen(Benutzer b, Stand s, IEnumerable<int> ids) => AbwesenheitEntschuldigen(b, s, ids, "offen", null, null).Geaendert;
}
