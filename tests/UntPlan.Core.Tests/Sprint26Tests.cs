using System.Drawing;
using UntPlan.Core;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Sprint 26: Clientbereich des Hauptfensters mit eigener Titelleiste (kein weißer Streifen, maximiert genau der Arbeitsbereich).</summary>
public class FensterClientBereichTests
{
    private static readonly Rectangle Arbeit = new(0, 0, 1920, 1032); // Taskleiste unten (48 px)

    [Fact]
    public void FenstermodusOhneOberenRahmen()
    {
        // System: 8 px Rahmen links/rechts/unten, 31 px Titelleiste + Rahmen oben
        var fenster = new Rectangle(100, 50, 1200, 800);
        var system = Rectangle.FromLTRB(108, 89, 1292, 842);
        var c = Fensterlage.ClientBereich(fenster, system, false, Arbeit);
        Assert.Equal(Rectangle.FromLTRB(108, 50, 1292, 842), c);
    }

    [Fact]
    public void MaximiertGenauDerArbeitsbereich()
    {
        // Windows legt das maximierte Fenster um die Rahmendicke (100 %: 8 px) über den Bildschirm
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1040);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1032);
        Assert.Equal(Arbeit, Fensterlage.ClientBereich(fenster, system, true, Arbeit));
    }

    [Theory]
    [InlineData(1.25, 11)]
    [InlineData(1.5, 12)]
    [InlineData(2.0, 16)]
    public void MaximiertUnabhaengigVonDerDpiSkalierung(double skala, int rahmen)
    {
        // Rahmendicke je DPI verschieden: der Programmkopf beginnt trotzdem genau an der Oberkante des Arbeitsbereichs (kein Hochrutschen, kein Streifen)
        var arbeit = new Rectangle(0, 0, (int)(1920 * skala), (int)(1032 * skala));
        var fenster = Rectangle.FromLTRB(-rahmen, -rahmen, arbeit.Right + rahmen, arbeit.Bottom + rahmen);
        var system = Rectangle.FromLTRB(0, 40, arbeit.Right, arbeit.Bottom);
        var c = Fensterlage.ClientBereich(fenster, system, true, arbeit);
        Assert.Equal(arbeit, c);
    }

    [Fact]
    public void MaximiertMitTaskleisteObenUndZweitemMonitor()
    {
        var arbeit = new Rectangle(1920, 40, 2560, 1400);
        var fenster = Rectangle.FromLTRB(1912, 32, 4488, 1448);
        var system = Rectangle.FromLTRB(1920, 63, 4480, 1440);
        Assert.Equal(arbeit, Fensterlage.ClientBereich(fenster, system, true, arbeit));
    }

    [Fact]
    public void MaximiertOhneArbeitsbereichObenWieLinks()
    {
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1040);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1032);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 1920, 1032), Fensterlage.ClientBereich(fenster, system, true, Rectangle.Empty));
    }

    [Fact]
    public void AutomatischAusgeblendeteTaskleisteBleibtErreichbar()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var fenster = Rectangle.FromLTRB(-8, -8, 1928, 1088);
        var system = Rectangle.FromLTRB(0, 23, 1920, 1080);
        Assert.Equal(Rectangle.FromLTRB(0, 0, 1920, 1079), Fensterlage.ClientBereich(fenster, system, true, monitor, 3));
        Assert.Equal(Rectangle.FromLTRB(0, 1, 1920, 1080), Fensterlage.ClientBereich(fenster, system, true, monitor, 1));
    }
}

/// <summary>Sprint 26: in UntPlanWeb stornierte Buchungen (Rückmeldung „buchungstorno“) und Prüfungs-Löschmeldungen.</summary>
public class BuchungStornoTests
{
    private sealed record Umgebung(Core.Model.Schuldatei D, DateOnly Tag, Core.Vertretungsplanung.TagesEintrag Stunde, Core.Model.Lehrer Lehrer, Core.Model.Raum Frei, int FreieStunde, Core.Model.Lehrer FreierLehrer)
    {
        public string Datum => Tag.ToString("yyyy-MM-dd");
    }

    private static Umgebung Aufbauen()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(d, tag);
        var stunde = tp.Eintraege.First(e => e.Unterricht != null && e.KlassenIds.Count == 1 && e.RaumIds.Count == 1 && e.LehrerIds.Count == 1);
        var frei = d.Raeume.First(r => !tp.Stunde(stunde.Stunde).Any(e => e.RaumIds.Contains(r.Id)));
        // Lehrkraft ohne Unterricht in einer Stunde (für eine Aktivität bzw. einen zusätzlichen Unterricht)
        var (fl, fs) = d.Lehrer.SelectMany(l => Enumerable.Range(1, d.Zeitraster.StundenProTag).Select(s => (l, s)))
            .First(x => !tp.Stunde(x.s).Any(e => e.LehrerIds.Contains(x.l.Id)) && d.Raeume.Any(r => !tp.Stunde(x.s).Any(e => e.RaumIds.Contains(r.Id))));
        return new Umgebung(d, tag, stunde, d.Lehrer.First(l => l.Id == stunde.LehrerIds[0]), frei, fs, fl);
    }

    private static Core.Model.Raum FreierRaum(Umgebung u, int stunde)
    {
        var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(u.D, u.Tag);
        return u.D.Raeume.First(r => !tp.Stunde(stunde).Any(e => e.RaumIds.Contains(r.Id)));
    }

    private static Web.Contracts.WebRueckmeldung Buchung(string id, Umgebung u, string lehrer, int stunde, string raum, string? fach = null, string titel = "Zusätzlicher Unterricht", int? unterricht = null) => new()
    {
        Id = id, Typ = Web.Contracts.WebRueckmeldungsTypen.Raumbuchung, Absender = lehrer, Lehrer = lehrer, Von = u.Datum, StundeVon = stunde, StundeBis = stunde,
        Raum = raum, Raeume = { raum }, Fach = fach, Titel = titel, UnterrichtNr = unterricht,
    };

    private static Web.Contracts.WebRueckmeldung Storno(Web.Contracts.WebRueckmeldung b, bool mitBezug = true) => b with
    {
        Id = "storno-" + b.Id, Typ = Web.Contracts.WebRueckmeldungsTypen.BuchungStorno, BezugId = mitBezug ? b.Id : null, Grund = "storniert",
    };

    private static (List<string> Ok, List<(string Id, string Grund)> Nein) Dialog(Umgebung u, params Web.Contracts.WebRueckmeldung[] liste) =>
        Core.Austausch.StundenplanAenderungen.Uebernehmen(u.D, Core.Austausch.StundenplanAenderungen.Analysieren(u.D, liste, u.Tag.AddDays(-1), u.Tag.AddDays(1), false), "test");

    private static bool ImPlan(Umgebung u, string webId) =>
        Core.Vertretungsplanung.Tagesplan.Berechnen(u.D, u.Tag).Eintraege.Any(e => e.Vertretung?.WebId == webId);

    [Fact]
    public void ZusaetzlicherUnterrichtWirdImDialogStorniert()
    {
        var u = Aufbauen();
        var raum = FreierRaum(u, u.FreieStunde);
        var b = Buchung("buchung-42", u, u.FreierLehrer.Kurzname, u.FreieStunde, raum.Kurzname, u.D.Faecher[0].Kurzname);
        var (ok, nein) = Dialog(u, b);
        Assert.Empty(nein);
        Assert.Contains(u.D.Vertretungen, v => v.WebId == b.Id && v.Art == Core.Model.VertretungsArt.Sondereinsatz);
        Assert.True(ImPlan(u, b.Id));
        var s = Storno(b);
        var zeile = Assert.Single(Core.Austausch.StundenplanAenderungen.Analysieren(u.D, new[] { s }, u.Tag, u.Tag, false));
        Assert.Equal("Storno", zeile.ArtText);
        Assert.Null(zeile.Fehler);
        (ok, nein) = Dialog(u, s);
        Assert.Empty(nein);
        Assert.Equal(new[] { s.Id }, ok);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == b.Id);
        Assert.False(ImPlan(u, b.Id));
        // erneut gemeldet (z. B. Quittieren fehlgeschlagen): erledigt, nichts passiert; die Buchung wird nicht neu angeboten
        Assert.Empty(Core.Austausch.StundenplanAenderungen.Analysieren(u.D, new[] { s, b }, u.Tag, u.Tag, false));
        var n = u.D.Vertretungen.Count;
        Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { s });
        Assert.Equal(n, u.D.Vertretungen.Count);
    }

    [Fact]
    public void RaumbuchungWirdImAutomatischenAbgleichStorniertUndQuittiert()
    {
        var u = Aufbauen();
        var raum = FreierRaum(u, u.FreieStunde);
        var b = Buchung("buchung-7", u, u.FreierLehrer.Kurzname, u.FreieStunde, raum.Kurzname, titel: "Elternabend") with { Lehrer = null };
        var (ok, _) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { b });
        Assert.Contains(b.Id, ok);
        Assert.Contains(u.D.Vertretungen, v => v.WebId == b.Id && v.Art == Core.Model.VertretungsArt.Raumbuchung);
        var s = Storno(b);
        (ok, var nein) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { s });
        Assert.Equal(new[] { s.Id }, ok);
        Assert.Empty(nein);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == b.Id);
        // idempotent: noch einmal → wieder quittiert, keine Änderung
        (ok, nein) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { s });
        Assert.Equal(new[] { s.Id }, ok);
        Assert.Single(u.D.Einstellungen.Portal.StplAenderungen, e => e.Id == s.Id);
    }

    [Fact]
    public void RaumaenderungEinesUnterrichtsWirdZurueckgenommen()
    {
        var u = Aufbauen();
        var urRaum = u.Stunde.RaumIds[0];
        var b = Buchung("buchung-9", u, u.Lehrer.Kurzname, u.Stunde.Stunde, u.Frei.Kurzname, titel: "Raumbuchung", unterricht: u.Stunde.Unterricht!.Id);
        Dialog(u, b);
        var e1 = Core.Vertretungsplanung.Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde).First(e => e.Unterricht?.Id == u.Stunde.Unterricht.Id);
        Assert.Contains(u.Frei.Id, e1.RaumIds);
        Dialog(u, Storno(b));
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == b.Id);
        var e2 = Core.Vertretungsplanung.Tagesplan.Berechnen(u.D, u.Tag).Stunde(u.Stunde.Stunde).First(e => e.Unterricht?.Id == u.Stunde.Unterricht.Id);
        Assert.Equal(new[] { urRaum }, e2.RaumIds);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.Datum == u.Tag && v.UnterrichtId == u.Stunde.Unterricht.Id && v.Art == Core.Model.VertretungsArt.Raumvertretung);
    }

    [Fact]
    public void OhneGespeicherteKennungNurDiePassendeBuchung()
    {
        var u = Aufbauen();
        var raum = FreierRaum(u, u.FreieStunde);
        var andere = u.D.Raeume.First(r => r.Id != raum.Id);
        Core.Model.Vertretung Zeile(int raumId) => new()
        {
            Id = u.D.NeueId(), Datum = u.Tag, Stunde = u.FreieStunde, Art = Core.Model.VertretungsArt.Raumbuchung, Quelle = Core.Model.VertretungsQuelle.Manuell,
            VertreterId = u.FreierLehrer.Id, VertretungsraumId = raumId, Text = "Elternabend", Bearbeitet = true,
        };
        var alt = Zeile(raum.Id);
        var fremd = Zeile(andere.Id);
        u.D.Vertretungen.Add(alt);
        u.D.Vertretungen.Add(fremd);
        var b = Buchung("buchung-3", u, u.FreierLehrer.Kurzname, u.FreieStunde, raum.Kurzname, titel: "Elternabend");
        var (ok, _) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { Storno(b, mitBezug: false) with { Id = "storno-x" } });
        Assert.Single(ok);
        Assert.DoesNotContain(alt, u.D.Vertretungen);
        Assert.Contains(fremd, u.D.Vertretungen);
    }

    [Fact]
    public void NichtVorhandeneBuchungWirdTrotzdemQuittiert()
    {
        var u = Aufbauen();
        var n = u.D.Vertretungen.Count;
        var s = Storno(Buchung("buchung-99", u, "UNBEKANNT", 1, "XYZ"));
        var (ok, nein) = Dialog(u, s);
        Assert.Equal(new[] { s.Id }, ok);
        Assert.Empty(nein);
        Assert.Equal(n, u.D.Vertretungen.Count);
    }

    [Fact]
    public void ZurPruefungGewordeneBuchungKehrtNachStornoNichtZurueck()
    {
        var u = Aufbauen();
        var b = Buchung("buchung-5", u, u.Lehrer.Kurzname, u.Stunde.Stunde, u.Frei.Kurzname, titel: "Raumbuchung", unterricht: u.Stunde.Unterricht!.Id);
        Dialog(u, b);
        var klasse = u.D.Klassen.First(k => k.Id == u.Stunde.KlassenIds[0]);
        Dialog(u, new Web.Contracts.WebRueckmeldung
        {
            Id = "p1", Typ = Web.Contracts.WebRueckmeldungsTypen.Klausur, Lehrer = u.Lehrer.Kurzname, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde,
            Klassen = { klasse.Kurzname }, Raeume = { u.Frei.Kurzname }, Titel = "Mathe Klausur",
        });
        var k = Assert.Single(u.D.Klausuren);
        Assert.Contains(k.Buchungen, v => v.WebId == b.Id);
        Dialog(u, Storno(b));
        Assert.DoesNotContain(k.Buchungen, v => v.WebId == b.Id);
        Core.Austausch.StundenplanAenderungen.KlausurZeilenEntfernen(u.D, k);
        u.D.Klausuren.Remove(k);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.WebId == b.Id);
    }

    [Fact]
    public void PruefungsLoeschungMitUnbekannterKlasseLoeschtNichtsFremdes()
    {
        var u = Aufbauen();
        var klasse = u.D.Klassen.First(k => k.Id == u.Stunde.KlassenIds[0]);
        Dialog(u, new Web.Contracts.WebRueckmeldung
        {
            Id = "p1", Typ = Web.Contracts.WebRueckmeldungsTypen.Klausur, Lehrer = u.Lehrer.Kurzname, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde,
            Klassen = { klasse.Kurzname }, Raeume = { u.Frei.Kurzname }, Titel = "Klausur",
        });
        Assert.Single(u.D.Klausuren);
        var loeschen = new Web.Contracts.WebRueckmeldung
        {
            Id = "l1", Typ = Web.Contracts.WebRueckmeldungsTypen.KlausurLoeschen, Von = u.Datum, StundeVon = u.Stunde.Stunde, StundeBis = u.Stunde.Stunde,
            Klassen = { "GIBTESNICHT" }, Titel = "Klausur",
        };
        var (_, nein) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { loeschen });
        Assert.Contains("l1", nein);
        Assert.Single(u.D.Klausuren);
        Assert.Equal(0, Core.Austausch.StundenplanAenderungen.KlausurenEntfernen(u.D, loeschen, u.Tag, u.Stunde.Stunde, u.Stunde.Stunde));
        // mit Bezug auf die Web-Kennung: genau diese Prüfung, ohne Reste
        var gezielt = loeschen with { Id = "l2", Klassen = new() { klasse.Kurzname }, BezugId = "p1" };
        var (ok, _) = Core.Austausch.UntPlanWebExport.Uebernehmen(u.D, new[] { gezielt });
        Assert.Contains("l2", ok);
        Assert.Empty(u.D.Klausuren);
        Assert.DoesNotContain(u.D.Vertretungen, v => v.KlausurId.HasValue);
    }
}
