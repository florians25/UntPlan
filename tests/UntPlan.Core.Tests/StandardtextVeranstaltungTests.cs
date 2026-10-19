using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Keine automatischen Standardtexte („Vertretung durch Verlegung“, „Vorziehung“); abwesende Lehrkräfte nehmen nicht an Veranstaltungen teil.</summary>
public class StandardtextVeranstaltungTests
{
    [Fact]
    public void AutomatischeStandardtexteWerdenNichtExportiert()
    {
        Assert.Equal("", Austausch.StundenplanAenderungen.ExportText("Vertretung durch Verlegung"));
        Assert.Equal("", Austausch.StundenplanAenderungen.ExportText(" vorziehung "));
        Assert.Equal("Vorziehung wegen Ausflug", Austausch.StundenplanAenderungen.ExportText("Vorziehung wegen Ausflug"));
        Assert.Equal("Bitte Buch mitbringen", Austausch.StundenplanAenderungen.ExportText("Bitte Buch mitbringen"));
        Assert.Equal("", Austausch.StundenplanAenderungen.ExportText(null));
    }

    [Fact]
    public void AbwesendeLehrkraftFaelltAusDerVeranstaltungUndKehrtOhneAbsenzZurueck()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var lehrer = d.Lehrer.Take(2).Select(l => l.Id).ToList();
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = 1, StundeBis = 3, LehrerIds = lehrer.ToList(), Text = "Fortbildung" };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag);
        bool Zeile(int l, int s) => d.Vertretungen.Any(x => x.Datum == tag && x.Stunde == s && x.Art == VertretungsArt.Veranstaltung && x.VeranstaltungId == va.Id && x.VertreterId == l);
        Assert.True(Zeile(lehrer[0], 2));
        // erste Lehrkraft in der 2. Stunde krank: ihre Zeile entfällt nur in dieser Stunde, die andere bleibt
        var absenz = new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer[0], Von = tag, Bis = tag, StundeVon = 2, StundeBis = 2 };
        d.Abwesenheiten.Add(absenz);
        Vertretungen.Aktualisieren(d, tag);
        Assert.False(Zeile(lehrer[0], 2));
        Assert.True(Zeile(lehrer[0], 1));
        Assert.True(Zeile(lehrer[0], 3));
        Assert.True(Zeile(lehrer[1], 2));
        // Absenz entfernt: beim nächsten Abgleich wieder dabei
        d.Abwesenheiten.Remove(absenz);
        Vertretungen.Aktualisieren(d, tag);
        Assert.True(Zeile(lehrer[0], 2));
    }
}
