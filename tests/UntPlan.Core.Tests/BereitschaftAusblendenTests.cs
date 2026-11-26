using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Bereitschaften werden ausgeblendet, wenn die Lehrkraft in dieser Stunde nun eine Unterrichtsstunde hat (z. B. als Vertretung
/// eingesetzt); ohne eigene Stunde bleibt die Bereitschaft sichtbar.
/// </summary>
public class BereitschaftAusblendenTests
{
    private static (Schuldatei D, DateOnly Tag, RegelStunde Ber, Vertretung V) Lage()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        for (var i = 0; i < 20; i++, tag = Kalender.NaechsterUnterrichtstag(d, tag))
        {
            var regel = Tagesplan.Regulaer(d, tag);
            foreach (var ber in regel.Where(r => r.LehrerId.HasValue && Vertretungen.IstBereitschaftsfach(d, r.FachId)))
            {
                // andere, einfache Stunde derselben Stunde, deren Lehrkraft abwesend wird
                var andere = regel.FirstOrDefault(r => r.Stunde == ber.Stunde && r.LehrerId.HasValue && r.LehrerId != ber.LehrerId
                    && !Vertretungen.IstBereitschaftsfach(d, r.FachId) && r.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(r.Unterricht.Band)
                    && !r.Unterricht.KeineVertretung && r.KlassenIds.Count > 0
                    && !regel.Any(x => x.LehrerId == r.LehrerId && x.Stunde == r.Stunde && x != r));
                if (andere is null) continue;
                var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = andere.LehrerId!.Value, Von = tag, Bis = tag, GrundId = d.Abwesenheitsgruende.FirstOrDefault()?.Id };
                AbsenzVerwaltung.Speichern(d, new[] { a }, Array.Empty<Abwesenheit>());
                var v = d.Vertretungen.FirstOrDefault(x => x.Datum == tag && x.Stunde == ber.Stunde && x.UnterrichtId == andere.Unterricht.Id && x.Offen);
                if (v is null) continue;
                return (d, tag, ber, v);
            }
        }
        throw new InvalidOperationException("keine Bereitschaft mit vertretbarer Stunde gefunden");
    }

    private static TagesEintrag BerEintrag(Tagesplan tp, RegelStunde ber) =>
        tp.Stunde(ber.Stunde).Single(e => e.Regel?.Unterricht.Id == ber.Unterricht.Id && e.Regel.ZeilenIndex == ber.ZeilenIndex);

    [Fact]
    public void Bereitschaft_ohne_eigene_Stunde_bleibt_sichtbar()
    {
        var (d, tag, ber, _) = Lage();
        var tp = Tagesplan.Berechnen(d, tag);
        var e = BerEintrag(tp, ber);
        Assert.True(tp.IstBereitschaft(e));
        Assert.False(tp.BereitschaftAusgeblendet(e));
        Assert.Contains(e, tp.SichtbareStunde(ber.Stunde));
    }

    [Fact]
    public void Bereitschaft_wird_ausgeblendet_wenn_die_Lehrkraft_vertritt()
    {
        var (d, tag, ber, v) = Lage();
        Vertretungen.VertreterSetzen(d, v, ber.LehrerId);
        var tp = Tagesplan.Berechnen(d, tag);
        var e = BerEintrag(tp, ber);
        Assert.True(tp.BereitschaftAusgeblendet(e));
        Assert.DoesNotContain(e, tp.SichtbareStunde(ber.Stunde));
        // die Vertretungsstunde selbst bleibt sichtbar
        Assert.Contains(tp.SichtbareStunde(ber.Stunde), x => x.Vertretung == v && x.LehrerIds.Contains(ber.LehrerId!.Value));
        // andere Stunden der Lehrkraft sind nicht betroffen
        Assert.All(tp.Eintraege.Where(x => x.Stunde != ber.Stunde && tp.IstBereitschaft(x)),
            x => Assert.Equal(tp.Stunde(x.Stunde).Any(y => y != x && y.Findetstatt && !tp.IstBereitschaft(y) && y.LehrerIds.Contains(x.Regel?.LehrerId ?? -1)), tp.BereitschaftAusgeblendet(x)));
    }

    [Fact]
    public void Vertreter_wieder_entfernt_zeigt_die_Bereitschaft_wieder()
    {
        var (d, tag, ber, v) = Lage();
        Vertretungen.VertreterSetzen(d, v, ber.LehrerId);
        Vertretungen.VertreterSetzen(d, v, null);
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.False(tp.BereitschaftAusgeblendet(BerEintrag(tp, ber)));
    }

    [Fact]
    public void Webexport_laesst_ausgeblendete_Bereitschaft_weg()
    {
        var (d, tag, ber, v) = Lage();
        Vertretungen.VertreterSetzen(d, v, ber.LehrerId);
        var tp = Tagesplan.Berechnen(d, tag);
        var sichtbar = tp.Eintraege.Count(e => !tp.BereitschaftAusgeblendet(e));
        Assert.Equal(tp.Eintraege.Count - 1, sichtbar);
    }
}
