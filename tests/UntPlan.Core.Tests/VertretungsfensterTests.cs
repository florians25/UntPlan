using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// Zuverlässigkeit des Vertretungsfensters (Zeilen behalten bis Zeilenwechsel, Abgleich ohne Springen), Kennzeichen „Neue
/// Vertretung“, zusätzliche Lehrkraft einer Veranstaltung und „nur dorthin verlegen“.
/// </summary>
public class VertretungsfensterTests
{
    [Fact]
    public void OhneLehrkraftSetztTextNurWennKeinerDaIst()
    {
        var d = new Schuldatei();
        var v = new Vertretung { Id = 1, Art = VertretungsArt.Vertretung };
        Vertretungen.OhneLehrer(v);
        Assert.Equal(Vertretungen.OhneLehrerText, v.Text);
        // anderer Vertreter: der automatische Text verschwindet wieder
        Vertretungen.VertreterSetzen(d, v, null);
        Assert.Equal("", v.Text);
        // eigener Text bleibt erhalten
        var w = new Vertretung { Id = 2, Art = VertretungsArt.Vertretung, Text = "Aufgaben im Raum" };
        Vertretungen.OhneLehrer(w);
        Assert.Equal("Aufgaben im Raum", w.Text);
        Vertretungen.EntfallUmschalten(d, w);
        Assert.Equal("Aufgaben im Raum", w.Text);
    }

    [Fact]
    public void BearbeiteteZeileBleibtBisZumZeilenwechsel()
    {
        var b = new ZeilenBehalten();
        Assert.False(b.Zeilenwechsel(5));
        b.Merken(new[] { 7, 8, 0 });
        Assert.True(b.Aktiv);
        Assert.False(b.Enthaelt(0));
        // weitere Spalte derselben Zeile (bzw. einer mitbearbeiteten Zeile): nichts verschwindet
        Assert.False(b.Zeilenwechsel(7));
        Assert.False(b.Zeilenwechsel(8));
        Assert.True(b.Enthaelt(7));
        // andere Zeile: jetzt verschwinden die behaltenen Zeilen (Neuaufbau nötig), danach ist nichts mehr zu tun
        Assert.True(b.Zeilenwechsel(9));
        Assert.False(b.Aktiv);
        Assert.False(b.Zeilenwechsel(10));
        b.Merken(new[] { 3 });
        Assert.True(b.Zeilenwechsel(null));
    }

    [Fact]
    public void GleicheZeilenWerdenAmOrtAktualisiertOhneZuSpringen()
    {
        var alt = new List<string> { "V1", "V2", "V3" };
        // neue Sortierung (z. B. Vertreter geändert in der Lehreransicht) – angezeigte Reihenfolge bleibt
        var neu = new List<string> { "V2", "V3", "V1" };
        Assert.Equal(alt, Listenabgleich.AmOrt(alt, neu, x => x));
        // ausdrücklich neu sortieren (Filter/Ansicht gewechselt)
        Assert.Null(Listenabgleich.AmOrt(alt, neu, x => x, neuSortieren: true));
        Assert.Equal(alt, Listenabgleich.AmOrt(alt, alt.ToList(), x => x, neuSortieren: true));
        // Zeile weggefallen bzw. hinzugekommen: Neuaufbau
        Assert.Null(Listenabgleich.AmOrt(alt, new List<string> { "V1", "V2" }, x => x));
        Assert.Null(Listenabgleich.AmOrt(alt, new List<string> { "V1", "V2", "V4" }, x => x));
        Assert.False(Listenabgleich.GleicheZeilen(new[] { "V1", "V1" }, new[] { "V1", "V2" }));
    }

    [Fact]
    public void NachNeuaufbauBleibtDieVertretungAktuellUndSichtbar()
    {
        var neu = new List<string> { "V1", "V2", "V4", "V5", "V6" };
        // dieselbe Vertretung, auch an anderer Position
        Assert.Equal(2, Listenabgleich.Zielzeile(neu, "V4", 0));
        // verschwunden (z. B. nicht mehr offen): Zeile an der bisherigen Position, am Ende die letzte
        Assert.Equal(2, Listenabgleich.Zielzeile(neu, "V3", 2));
        Assert.Equal(4, Listenabgleich.Zielzeile(neu, "V9", 12));
        Assert.Equal(-1, Listenabgleich.Zielzeile(new List<string>(), "V1", 0));
        // erste sichtbare Zeile bleibt, solange die aktuelle im Bild ist
        Assert.Equal(1, Listenabgleich.ErsteSichtbare(neu, "V2", 0, 2, 3));
        // aktuelle Zeile unterhalb des Bildes: so weit blättern, dass sie sichtbar ist
        Assert.Equal(2, Listenabgleich.ErsteSichtbare(neu, "V1", 0, 4, 3));
        // aktuelle Zeile oberhalb: nach oben blättern
        Assert.Equal(0, Listenabgleich.ErsteSichtbare(neu, "V5", 3, 0, 3));
    }

    [Fact]
    public void KennzeichenNeueVertretung()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, x.FachId));
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        var v = d.Vertretungen.First(x => x.Datum == tag && x.Offen);
        // aus einer früheren Sitzung (Datei geöffnet nach dem Anlegen)
        var alt = DateTime.Now.AddHours(-2);
        v.Erstellt = v.Geaendert = alt.AddHours(-1);
        var sitzung = alt;
        Assert.False(Vertretungen.IstNeu(v, sitzung));
        // Text oder Vertreter geändert → neu
        Vertretungen.AlsGeaendertMarkieren(v);
        Assert.True(Vertretungen.IstNeu(v, sitzung));
        v.Geaendert = alt.AddHours(-1);
        // Neuberechnung ohne Änderung lässt das Kennzeichen unverändert …
        Vertretungen.Aktualisieren(d, tag);
        Assert.False(Vertretungen.IstNeu(v, sitzung));
        // … eine durch die Neuberechnung geänderte Zeile ist dagegen neu (Absenz gelöscht → Zeile entfällt, andere Lehrkraft absent)
        d.Abwesenheiten.Clear();
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, x => x.Id == v.Id && x.Offen);
        Assert.False(Vertretungen.IstNeu(new Vertretung { Id = 0 }, sitzung));
    }

    [Fact]
    public void ZusaetzlicheLehrkraftFuerVeranstaltung()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var regel = Tagesplan.Regulaer(d, tag).Where(x => x.LehrerId.HasValue && !Vertretungen.IstBereitschaftsfach(d, x.FachId)).ToList();
        var teilnehmer = regel[0].LehrerId!.Value;
        // Zusatzlehrkraft mit eigenem Unterricht an diesem Tag, nicht an der Veranstaltung beteiligt
        var zusatz = regel.Select(x => x.LehrerId!.Value).First(l => l != teilnehmer);
        var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag.AddDays(7), StundeVon = 1, StundeBis = 99, LehrerIds = new() { teilnehmer }, Text = "Projekttage" };
        d.Veranstaltungen.Add(va);
        Vertretungen.Aktualisieren(d, tag, tag.AddDays(7));

        VeranstaltungsLogik.LehrerHinzufuegen(d, va.Id, tag, zusatz, ganzeVeranstaltung: false);
        Assert.Contains(zusatz, va.LehrerAm(tag));
        Assert.True(va.NimmtTeil(zusatz, tag));
        Assert.False(va.NimmtTeil(zusatz, Kalender.NaechsterUnterrichtstag(d, tag)));
        Assert.DoesNotContain(zusatz, va.LehrerIds);
        Assert.Contains(d.Ix().KLehrer(zusatz), VeranstaltungsLogik.LehrerText(d, va, tag));
        // Zeile der Veranstaltung mit der zusätzlichen Lehrkraft; ihr eigener Unterricht wird zur Vertretung
        Assert.Contains(d.Vertretungen, x => x.Datum == tag && x.Art == VertretungsArt.Veranstaltung && x.VertreterId == zusatz && x.ErsatzFuerLehrerId is null);
        Assert.Contains(d.Vertretungen, x => x.Datum == tag && x.LehrerId == zusatz && x.UnterrichtId.HasValue && x.VeranstaltungId == va.Id);
        Assert.Throws<InvalidOperationException>(() => VeranstaltungsLogik.LehrerHinzufuegen(d, va.Id, tag, zusatz, false));

        // wieder herausnehmen
        Assert.True(VeranstaltungsLogik.ZusatzEntfernen(d, va.Id, tag, zusatz));
        Assert.False(va.NimmtTeil(zusatz, tag));
        Assert.DoesNotContain(d.Vertretungen, x => x.Datum == tag && x.Art == VertretungsArt.Veranstaltung && x.VertreterId == zusatz);
        Assert.False(VeranstaltungsLogik.ZusatzEntfernen(d, va.Id, tag, zusatz));

        // für die ganze Veranstaltung
        VeranstaltungsLogik.LehrerHinzufuegen(d, va.Id, tag, zusatz, ganzeVeranstaltung: true);
        Assert.Contains(zusatz, va.LehrerIds);
        Assert.True(va.NimmtTeil(zusatz, Kalender.NaechsterUnterrichtstag(d, tag)));
    }

    [Fact]
    public void NurDorthinVerlegenIstEineEinfacheVerlegung()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Tagesplan.Berechnen(d, tag);
        var normale = Tagesplan.Regulaer(d, tag).Where(r => r.LehrerId.HasValue && r.RaumId.HasValue && !Vertretungen.IstBereitschaftsfach(d, r.FachId) &&
                                                           r.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(r.Unterricht.Band)).ToList();
        (RegelStunde A, RegelStunde B)? paar = null;
        foreach (var a in normale)
        {
            foreach (var b in normale.Where(b => b.Stunde != a.Stunde && b.KlassenIds.SequenceEqual(a.KlassenIds) && b.Unterricht.Id != a.Unterricht.Id))
                if (Tagesverlegung.Zielstatus(d, tp, a.Stunde, a.Unterricht.Id, null, tp, b.Stunde) == Planung.Feldstatus.TauschMoeglich) { paar = (a, b); break; }
            if (paar != null) break;
        }
        Assert.NotNull(paar);
        var (x, y) = paar!.Value;
        var an = Tagesverlegung.Analysieren(d, tag, x.Stunde, x.Unterricht.Id, null, 1, tag, y.Stunde);
        Assert.True(an.TauschMoeglich && an.Kollision);
        var nur = an.NurDorthin();
        // die tauschbare Stunde behindert nicht: keine Kollisionsfrage, sondern eine einfache Verlegung
        Assert.False(nur.TauschMoeglich);
        Assert.False(nur.Kollision);
        Tagesverlegung.Ausfuehren(d, nur, VerlegeModus.Verlegen);
        var neu = Tagesplan.Berechnen(d, tag);
        Assert.Contains(neu.VonKlasse(x.KlassenIds[0]), e => e.Stunde == y.Stunde && e.Findetstatt && (e.Unterricht?.Id ?? e.Vertretung?.UnterrichtId) == x.Unterricht.Id);
        // die dortige Stunde bleibt, wo sie ist (kein Tausch, keine Verdrängung)
        Assert.Contains(neu.VonKlasse(x.KlassenIds[0]), e => e.Stunde == y.Stunde && e.Findetstatt && e.Unterricht?.Id == y.Unterricht.Id);
        Assert.DoesNotContain(neu.VonKlasse(x.KlassenIds[0]), e => e.Stunde == x.Stunde && e.Findetstatt && e.Unterricht?.Id == y.Unterricht.Id);
        Assert.DoesNotContain(d.Vormerkungen, v => v.UnterrichtId == y.Unterricht.Id);
        // ohne Tauschmöglichkeit ändert „nur dorthin“ nichts
        Assert.Same(nur, nur.NurDorthin());
    }
}

internal static class TestErweiterungen
{
    public static DatenIndex Ix(this Schuldatei d) => new(d);
}
