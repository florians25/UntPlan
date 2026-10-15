using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Rückfragen rund um Verlegungen und Sondereinsätze:
/// <list type="bullet">
/// <item>„Entfall“ bzw. „-“ bei einer Verlegung: „Soll die Verlegung rückgängig gemacht werden?“ – Ja hebt die Verlegung auf
/// (Stunde wieder an der ursprünglichen Position, kein Entfall), Nein setzt wie bisher den Entfall.</item>
/// <item>Neue Lehrer-Absenz: Verlegungen bzw. Sondereinsätze der Lehrkraft im Absenzzeitraum aufheben bzw. löschen?</item>
/// </list>
/// Die Fragen laufen über <see cref="Ui.Frage"/> (im Selbsttest automatisch beantwortet und protokolliert).
/// </summary>
internal static class VerlegungsAbfrage
{
    public const string FrageVerlegung = "Soll die Verlegung rückgängig gemacht werden?";
    public const string FrageSondereinsatz = "Sollen die Sondereinsätze gelöscht werden?";

    /// <summary>
    /// Vor einem Entfall: Zeilen, die zu einer Verlegung gehören, nach Rückfrage aufheben.
    /// Liefert die Zeilen, für die danach noch wie bisher der Entfall gesetzt werden soll.
    /// </summary>
    public static List<Vertretung> VorEntfall(IReadOnlyList<Vertretung> liste, Func<string, AenderungsArt, Action<Schuldatei>, bool> aendern)
    {
        var d = AppZustand.D;
        var verlegt = liste.Where(v => Verlegungsaufhebung.IstVerlegung(d, v)).ToList();
        if (verlegt.Count == 0) return liste.ToList();
        var text = Verlegungsaufhebung.FrageText(d, verlegt.Count == 1 ? "Die Stunde ist verlegt:" : "Die Stunden sind verlegt:", verlegt, FrageVerlegung);
        if (!Ui.Frage(text, "UntPlan - Frage")) return liste.ToList();
        var ids = verlegt.Select(v => v.Id).ToList();
        aendern(ids.Count > 1 ? $"Verlegung aufgehoben ({ids.Count})" : "Verlegung aufgehoben", AenderungsArt.Vertretung, x => Verlegungsaufhebung.AlleAufheben(x, ids));
        return liste.Where(v => !ids.Contains(v.Id) && AppZustand.D.Vertretungen.Any(x => x.Id == v.Id)).ToList();
    }

    /// <summary>
    /// Vor einem Entfall im Stundenplan (Position eines Unterrichts): liegt dort eine Verlegung, nachfragen.
    /// true = Verlegung wurde aufgehoben (kein Entfall mehr nötig).
    /// </summary>
    public static bool VorEntfall(DateOnly datum, int stunde, int unterrichtId, Func<string, AenderungsArt, Action<Schuldatei>, bool> aendern)
    {
        var d = AppZustand.D;
        if (Verlegungsaufhebung.VerlegungAn(d, datum, stunde, unterrichtId) is not { } v) return false;
        if (!Ui.Frage(Verlegungsaufhebung.FrageText(d, "Die Stunde ist verlegt:", new[] { v }, FrageVerlegung), "UntPlan - Frage")) return false;
        var id = v.Id;
        return aendern("Verlegung aufgehoben", AenderungsArt.Vertretung, x => Verlegungsaufhebung.AlleAufheben(x, new[] { id }));
    }

    /// <summary>
    /// Nach dem Erfassen von Lehrer-Absenzen: Verlegungen und Sondereinsätze im Absenzzeitraum abfragen
    /// (nur wenn es solche Einträge gibt; der Fragetext listet die betroffenen Einträge auf).
    /// </summary>
    public static void NachAbsenz(IEnumerable<Abwesenheit> absenzen, Func<string, AenderungsArt, Action<Schuldatei>, bool> aendern)
    {
        foreach (var a in absenzen.Where(a => a.Art == AbwesenheitsArt.Lehrer).ToList())
        {
            var d = AppZustand.D;
            var name = d.Lehrer.FirstOrDefault(l => l.Id == a.ElementId)?.Kurzname ?? "";
            var verlegungen = Verlegungsaufhebung.VerlegungenImZeitraum(d, a);
            if (verlegungen.Count > 0)
            {
                var text = Verlegungsaufhebung.FrageText(d, $"{name} hat im Absenzzeitraum verlegte Stunden:", verlegungen, FrageVerlegung);
                if (Ui.Frage(text, "UntPlan - Frage"))
                {
                    var ids = verlegungen.Select(v => v.Id).ToList();
                    aendern(ids.Count > 1 ? $"Verlegungen aufgehoben ({ids.Count})" : "Verlegung aufgehoben", AenderungsArt.Vertretung, x => Verlegungsaufhebung.AlleAufheben(x, ids));
                }
            }
            d = AppZustand.D;
            var sonder = Verlegungsaufhebung.SondereinsaetzeImZeitraum(d, a);
            if (sonder.Count > 0)
            {
                var text = Verlegungsaufhebung.FrageText(d, $"{name} hat im Absenzzeitraum Sondereinsätze:", sonder, FrageSondereinsatz);
                if (Ui.Frage(text, "UntPlan - Frage"))
                {
                    var ids = sonder.Select(v => v.Id).ToList();
                    aendern(ids.Count > 1 ? $"Sondereinsätze gelöscht ({ids.Count})" : "Sondereinsatz gelöscht", AenderungsArt.Vertretung, x => Verlegungsaufhebung.SondereinsaetzeLoeschen(x, ids));
                }
            }
        }
    }
}
