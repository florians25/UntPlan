using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Wirkung der Klassenbuch-Einstellungen (<see cref="KlassenbuchEinstellungen"/>) im Klassenbuch: Eingabezeitraum, Löschsperre,
/// Abwesenheiten nur während der Stunde, Standardgründe und -status sowie die Vorgabe für das Ende einer Abwesenheit
/// (Tagesende bzw. fixer Zeitpunkt: die folgenden Stunden werden mit eingetragen und lassen sich dort verkürzen).
/// </summary>
public sealed partial class Klassenbuchdienst
{
    /// <summary>Gruppe der Fehlzeiten, die eine Abwesenheit bis Tagesende bzw. bis zum fixen Zeitpunkt in den Folgestunden anlegt.</summary>
    private const string TagesendePraefix = "tagesende:";

    private static string TagesendeGruppe(int unterricht, int stunde) => $"{TagesendePraefix}{unterricht}:{stunde}";

    /// <summary>Aktuelle Klassenbuch-Einstellungen.</summary>
    public KlassenbuchEinstellungen Einstellungen() => Klassenbucheinstellungen.Laden(_db);

    /// <summary>Abwesenheitseintrag nur während der Unterrichtsstunde (Fachlehrkräfte; Klassenleitung und Administration ausgenommen).</summary>
    private void NurInStundePruefen(KlassenbuchEinstellungen einst, Benutzer b, Stand s, DateOnly d, Karte karte)
    {
        if (!einst.AbwesenheitNurInStunde || b.IstAdmin) return;
        var (bb, _) = StundenZeit(s, karte.Von);
        var (_, be) = StundenZeit(s, karte.Bis);
        var beginn = Uhrzeit(karte.Beginn) ?? bb;
        var ende = Uhrzeit(karte.Ende) ?? be;
        var jetzt = TimeOnly.FromDateTime(_uhr.Jetzt);
        if (d != _uhr.Heute || jetzt < beginn || jetzt > ende)
            throw new ArgumentException("Abwesenheiten können nur während der Unterrichtsstunde eingetragen werden.");
    }

    /// <summary>Gilt der Abwesenheitsgrund (Kürzel) als entschuldigt? null = unbekannter bzw. kein Grund.</summary>
    private bool? GrundEntschuldigt(string grund) => grund.Length == 0 ? null
        : _db.Liste("SELECT entschuldigt FROM abwesenheitsgruende WHERE kurz = @k", r => r.Wahr("entschuldigt"), Db.P("@k", grund)) is { Count: > 0 } l ? l[0] : null;

    /// <summary>Status einer neuen Abwesenheit: mit „Status immer mit Abwesenheitsgrund ändern“ nach dem Grund, sonst der Standard-Entschuldigungsstatus.</summary>
    private string StatusNeu(KlassenbuchEinstellungen einst, string grund) =>
        einst.StatusMitGrund && GrundEntschuldigt(grund) == true ? "entschuldigt" : einst.StandardStatus;

    /// <summary>Status nach einer Änderung des Grundes (nur mit „Status immer mit Abwesenheitsgrund ändern“).</summary>
    internal string StatusNachGrund(KlassenbuchEinstellungen einst, string bisher, string alterGrund, string neuerGrund) =>
        einst.StatusMitGrund && neuerGrund != alterGrund && GrundEntschuldigt(neuerGrund) is bool ent ? (ent ? "entschuldigt" : "offen") : bisher;

    /// <summary>
    /// Abwesenheit über die Stunde hinaus fortschreiben (Vorgabe „Tagesende“ bzw. fixer Zeitpunkt): alle späteren Unterrichtsstunden
    /// des Tages bis zum Ende werden als Fehlzeiten eingetragen, eine angebrochene Stunde minutengenau. Vorhandene Einträge bleiben.
    /// </summary>
    private void BisEndeEintragen(Benutzer b, Stand s, DateOnly d, Karte karte, int schuelerId, string klasse, string lehrer, string jetzt,
        string grund, string status, TimeOnly? bis, Verspaetungsregeln regeln)
    {
        var gruppe = TagesendeGruppe(karte.Unterricht ?? 0, karte.Von);
        foreach (var k in _plan.Karten(s, d, Elementtyp.Klasse, klasse)
                     .Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung") && k.Von > karte.Bis))
            foreach (var a in AusFenster(s, regeln, Enumerable.Range(k.Von, k.Bis - k.Von + 1), TimeOnly.MinValue, bis ?? TimeOnly.MaxValue))
                _db.Ausfuehren("""
                    INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund, gruppe, beginn, ende)
                    VALUES (@d, @s, @u, @sid, @k, @f, @a, @m, @st, @bem, @l, @e, @b, @g, @gr, @be, @en)
                    ON CONFLICT(datum, stunde, schueler_id) DO NOTHING
                    """, Db.P("@d", d), Db.P("@s", a.Stunde), Db.P("@u", k.Unterricht), Db.P("@sid", schuelerId), Db.P("@k", klasse), Db.P("@f", k.Fach),
                    Db.P("@a", a.Art), Db.P("@m", a.Minuten), Db.P("@st", status), Db.P("@bem", a.Hinweis), Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id),
                    Db.P("@g", grund), Db.P("@gr", gruppe), Db.P("@be", a.Beginn), Db.P("@en", a.Ende));
    }

    /// <summary>Fortgeschriebene Folgestunden einer Abwesenheit entfernen (Schüler*in in der Ausgangsstunde doch anwesend).</summary>
    private void BisEndeEntfernen(DateOnly d, Karte karte, int schuelerId) =>
        _db.Ausfuehren("DELETE FROM fehlzeiten WHERE datum = @d AND stunde > @s AND schueler_id = @sid AND gruppe = @g",
            Db.P("@d", d), Db.P("@s", karte.Bis), Db.P("@sid", schuelerId), Db.P("@g", TagesendeGruppe(karte.Unterricht ?? 0, karte.Von)));

    /// <summary>Abwesenheiten der übrigen Stunden des Tages (Einstellung „Alle Abwesenheiten des Tages anzeigen“), je Schüler*in die erste.</summary>
    private List<Fehlzeit> UebrigeDesTages(DateOnly d, Karte karte, IReadOnlyCollection<string> klassen)
    {
        var tag = FehlzeitenZeitraum(d, d).Where(f => klassen.Contains(f.Klasse, Ci)).ToList();
        var hier = tag.Where(f => f.Stunde >= karte.Von && f.Stunde <= karte.Bis).Select(f => f.SchuelerId).ToHashSet();
        return tag.Where(f => f.Stunde > karte.Bis && !hier.Contains(f.SchuelerId)).GroupBy(f => f.SchuelerId).Select(g => g.OrderBy(f => f.Stunde).First()).ToList();
    }
}
