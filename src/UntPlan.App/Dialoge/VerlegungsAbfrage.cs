using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Rückfragen rund um Verlegungen und Sondereinsätze:
/// <list type="bullet">
/// <item>„Entfall“ bzw. „-“ bei einer verlegten bzw. getauschten Stunde (offener Ursprung oder Ziel) wie im Vorbild:
/// „Verlegung rückgängig?“ mit Lehrkraft und „Ursprung -> Ziel“ – Ja hebt die Verlegung bzw. den Tausch auf (Stunde wieder an der
/// ursprünglichen Position), Nein setzt die Stunde auf Entfall, Abbrechen ändert nichts.</item>
/// <item>Neue Lehrer-Absenz: Verlegungen bzw. Sondereinsätze der Lehrkraft im Absenzzeitraum aufheben bzw. löschen?</item>
/// </list>
/// Die Fragen laufen über <see cref="Ui.JaNeinAbbrechen"/> bzw. <see cref="Ui.Frage"/> (im Selbsttest automatisch beantwortet und protokolliert).
/// </summary>
internal static class VerlegungsAbfrage
{
    public const string FrageVerlegung = "Soll die Verlegung rückgängig gemacht werden?";
    public const string FrageSondereinsatz = "Sollen die Sondereinsätze gelöscht werden?";
    /// <summary>Titel der Rückfrage vor dem Entfall einer verlegten Stunde (wie im Vorbild „… - Frage“).</summary>
    public const string Titel = "UntPlan - Frage";

    /// <summary>Braucht die Zeile vor dem Entfall die Rückfrage? Verlegte bzw. getauschte Stunde, die nicht schon entfallen ist.</summary>
    public static bool RueckfrageNoetig(Schuldatei d, Vertretung v) => !v.IstEntfall && Verlegungsaufhebung.IstVerlegung(d, v);

    /// <summary>Rückfrage „Verlegung rückgängig?“ (Ja = Yes, Nein = No, Abbrechen = Cancel); Ja ist vorgewählt.</summary>
    public static DialogResult Fragen(Schuldatei d, Vertretung v, IWin32Window? owner = null) =>
        Ui.JaNeinAbbrechen(Verlegungsaufhebung.RueckfrageText(d, v), Titel, MeldungsArt.Frage, 0, owner);

    /// <summary>
    /// Vor einem Entfall (Vertretungsliste, Vertretungsvorschlag): je verlegter bzw. getauschter Stunde nachfragen.
    /// Ja hebt die Verlegung auf, Nein setzt die Stunde auf Entfall (offener Ursprung: Entfall mit „(Le.) nach“; Ziel: die Stunde
    /// entfällt ganz). Liefert die übrigen Zeilen, für die der Entfall wie bisher umgeschaltet werden soll, bzw. null bei Abbrechen.
    /// </summary>
    public static List<Vertretung>? VorEntfall(IReadOnlyList<Vertretung> liste, Func<string, AenderungsArt, Action<Schuldatei>, bool> aendern)
    {
        var verlegt = liste.Where(v => RueckfrageNoetig(AppZustand.D, v)).ToList();
        if (verlegt.Count == 0) return liste.ToList();
        var erledigt = new HashSet<int>();
        // Kopplung (eine Zeile in der Liste): einmal fragen, die Antwort gilt für alle Zeilen der Kopplung
        foreach (var gruppe in Vertretungen.NachKopplung(verlegt).Select(x => x.Gruppe))
        {
            var d = AppZustand.D;
            // bereits mit erledigt (z. B. Gegenrichtung eines Tausches, der aufgehoben wurde)
            var aktuell = gruppe.Select(v => d.Vertretungen.FirstOrDefault(x => x.Id == v.Id)).OfType<Vertretung>().Where(v => RueckfrageNoetig(d, v)).ToList();
            erledigt.UnionWith(gruppe.Select(v => v.Id));
            if (aktuell.Count == 0) continue;
            // Block (Doppelstunde, 4er-Block): einmal fragen, die Antwort gilt für alle Stunden des Blocks
            erledigt.UnionWith(aktuell.SelectMany(v => Verlegungsaufhebung.BlockZeilen(d, v)).Select(v => v.Id));
            var ids = aktuell.Select(v => v.Id).ToList();
            switch (Fragen(d, aktuell[0]))
            {
                case DialogResult.Yes:
                    aendern("Verlegung aufgehoben", AenderungsArt.Vertretung, x => Verlegungsaufhebung.AlleAufheben(x, ids));
                    break;
                case DialogResult.No:
                    aendern("Entfall", AenderungsArt.Vertretung, x => Verlegungsaufhebung.BlockAufEntfallSetzen(x, ids, AppZustand.Benutzer));
                    break;
                default:
                    return null;
            }
        }
        return liste.Where(v => !erledigt.Contains(v.Id) && AppZustand.D.Vertretungen.Any(x => x.Id == v.Id)).ToList();
    }

    /// <summary>
    /// Vor einem Entfall im Stundenplan bzw. Planungsdialog (Position eines Unterrichts): liegt dort eine verlegte Stunde bzw. ein noch
    /// offener Ursprung, nachfragen. true = erledigt (Verlegung aufgehoben oder abgebrochen – kein Entfall mehr nötig);
    /// false = wie bisher entfallen lassen (keine Verlegung bzw. Antwort Nein).
    /// </summary>
    public static bool VorEntfall(DateOnly datum, int stunde, int unterrichtId, Func<string, AenderungsArt, Action<Schuldatei>, bool> aendern)
    {
        var d = AppZustand.D;
        var v = d.Vertretungen.Where(x => x.Datum == datum && x.Stunde == stunde && x.UnterrichtId == unterrichtId && RueckfrageNoetig(d, x))
            .OrderBy(x => x.VonDatum.HasValue ? 0 : 1).FirstOrDefault();
        if (v is null) return false;
        switch (Fragen(d, v))
        {
            case DialogResult.Yes:
                var id = v.Id;
                aendern("Verlegung aufgehoben", AenderungsArt.Vertretung, x => Verlegungsaufhebung.AlleAufheben(x, new[] { id }));
                return true;
            case DialogResult.No:
                // Block: alle Stunden des Blocks auf Entfall (eine einzelne Stunde entfällt wie bisher beim Aufrufer)
                if (Verlegungsaufhebung.BlockZeilen(d, v).Count < 2) return false;
                var bid = v.Id;
                aendern("Entfall", AenderungsArt.Vertretung, x => Verlegungsaufhebung.BlockAufEntfallSetzen(x, new[] { bid }, AppZustand.Benutzer));
                return true;
            default:
                return true;
        }
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
