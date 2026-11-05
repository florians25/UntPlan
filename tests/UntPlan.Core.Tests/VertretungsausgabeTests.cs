using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>Vertretungsformate (Tageslisten, Format speichern als), Vertretungsimport, Klausuren (Statistik, Raum ohne Kollision), Nachrichten.</summary>
public class VertretungsausgabeTests
{
    [Fact]
    public void FormateErgaenzenLiefertMitgelieferteUndJeArtEinenStandard()
    {
        var liste = Vertretungsformate.Ergaenzen(null);
        Assert.Equal(Vertretungsformate.Mitgeliefert, liste.Select(f => f.Langname));
        Assert.Single(liste, f => f.Standard && f.Art == VertretungsformatArt.Klasse);
        Assert.Single(liste, f => f.Standard && f.Art == VertretungsformatArt.Lehrer);
        Assert.All(liste, f => Assert.False(f.Eigen));

        // gespeicherte Liste mit eigenem Format, doppeltem Eintrag und zwei Standards
        var gespeichert = new List<Vertretungsformat>
        {
            new() { Name = "Leli", Langname = "Vertretungen Lehrende", Art = VertretungsformatArt.Lehrer, Standard = true, Eigen = true, Tagesliste = true },
            new() { Name = "x", Langname = "Lehrer - Standard", Standard = true, Druck = true },
            new() { Name = "y", Langname = "vertretungen lehrende" },
        };
        Vertretungsformate.Ergaenzen(gespeichert);
        Assert.Equal(6, gespeichert.Count);
        Assert.Single(gespeichert, f => f.Standard && f.Art == VertretungsformatArt.Lehrer);
        var std = gespeichert.First(f => f.Langname == "Lehrer - Standard");
        Assert.False(std.Druck); // Eigenschaften der mitgelieferten Formate sind fest
        Assert.Equal(VertretungsformatArt.Lehrer, std.Art);
        Assert.Equal("Vertretungen Lehrende", Assert.Single(Vertretungsformate.Tageslisten(gespeichert)).Langname);
    }

    [Fact]
    public void FormatSpeichernAlsKopiertArtUndLoeschenNurEigene()
    {
        var liste = Vertretungsformate.Ergaenzen(null);
        var vorlage = liste.First(f => f.Langname == "Druck - Lehrer");
        vorlage.Tagesliste = true;
        var neu = Vertretungsformate.Kopieren(liste, vorlage, "Leli", "Vertretungen Lehrende");
        Assert.Equal(liste.IndexOf(vorlage) + 1, liste.IndexOf(neu));
        Assert.Equal(VertretungsformatArt.Lehrer, neu.Art);
        Assert.True(neu.Druck);
        Assert.True(neu.Eigen);
        Assert.False(neu.Standard);
        Assert.False(neu.Tagesliste);
        Assert.NotNull(Vertretungsformate.NamePruefen(liste, "Neu", "vertretungen lehrende"));
        Assert.NotNull(Vertretungsformate.NamePruefen(liste, "leli", "Anders"));
        Assert.Throws<InvalidOperationException>(() => Vertretungsformate.Kopieren(liste, vorlage, "Leli", "Noch eins"));
        Assert.Same(neu, Vertretungsformate.Finden(liste, "Vertretungen Lehrende"));
        Assert.Equal("Klasse - Standard", Vertretungsformate.Finden(liste, "gibt es nicht").Langname);

        Vertretungsformate.StandardSetzen(liste, neu);
        Assert.True(neu.Standard);
        Assert.False(liste.First(f => f.Langname == "Lehrer - Standard").Standard);
        Assert.False(Vertretungsformate.Loeschen(liste, vorlage));
        Assert.True(Vertretungsformate.Loeschen(liste, neu));
        Assert.Single(liste, f => f.Standard && f.Art == VertretungsformatArt.Lehrer);
        Assert.Equal("Druck-Leh_2026-11-05.pdf", Vertretungsformate.PdfDateiname(vorlage, new DateOnly(2026, 11, 5)));
        Assert.Equal("Aushang_heute.pdf", Vertretungsformate.PdfDateiname(vorlage, new DateOnly(2026, 11, 5), "Aushang/heute"));
    }

    private static (Schuldatei quelle, DateOnly tag, int lehrer) QuelleMitAbsenz()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var lehrer = Tagesplan.Regulaer(d, tag).Where(r => r.LehrerId.HasValue).GroupBy(r => r.LehrerId!.Value).OrderByDescending(g => g.Count()).First().Key;
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = lehrer, Von = tag, Bis = tag });
        Vertretungen.Aktualisieren(d, tag);
        d.Tagestexte.Add(new Tagestext { Datum = tag, Text = "Konferenz um 14 Uhr" });
        return (d, tag, lehrer);
    }

    [Fact]
    public void AlteEigeneAnsichtWirdAlsFormatUebernommen()
    {
        var liste = new List<Vertretungsformat>();
        var f = Vertretungsformate.AlteAnsichtUebernehmen(liste, "Leli", "Vertretungen Lehrende", "Druck - Lehrer");
        Assert.NotNull(f);
        Assert.Equal(Vertretungsformate.Mitgeliefert.Length + 1, liste.Count);
        Assert.True(f!.Eigen);
        Assert.True(f.Druck);
        Assert.Equal(VertretungsformatArt.Lehrer, f.Art);
        Assert.False(f.Standard);
        Assert.Equal("Leli", f.Name);
        // erneut (z. B. aus Schuldatei und lokalen Einstellungen): kein zweites Format
        Assert.Same(f, Vertretungsformate.AlteAnsichtUebernehmen(liste, "Leli", "vertretungen lehrende", "Klasse - Standard"));
        // ohne Langname gilt der Kurzname, belegter Kurzname wird ersetzt, unbekannte Basis = Klasse
        var g = Vertretungsformate.AlteAnsichtUebernehmen(liste, "Pausen", "", "unbekannt");
        Assert.Equal("Pausen", g!.Langname);
        Assert.Equal("Pausen2", g.Name);
        Assert.Equal(VertretungsformatArt.Klasse, g.Art);
        Assert.Null(Vertretungsformate.AlteAnsichtUebernehmen(liste, " ", "", null));
    }

    [Fact]
    public void VertretungsimportUebernimmtAbsenzenUndVertretungen()
    {
        var (quelle, tag, lehrer) = QuelleMitAbsenz();
        var ziel = Testdaten.Verplant();
        // im Ziel heißt die Lehrkraft anders (Elementzuordnung über den Kurznamen schlägt fehl, wird von Hand zugeordnet)
        var zielLehrer = ziel.Lehrer.First(l => l.Id == lehrer);
        zielLehrer.Kurzname += "_neu";
        var zuordnung = Vertretungsimport.Zuordnung(ziel, quelle);
        var z = zuordnung.Single(x => x.Typ == ElementTyp.Lehrer && x.QuellId == lehrer);
        Assert.Null(z.ZielId);
        Assert.Equal(ziel.Klassen.Count, zuordnung.Count(x => x.Typ == ElementTyp.Klasse && x.ZielId.HasValue));
        z.ZielId = zielLehrer.Id;

        var vorher = ziel.Vertretungen.Count;
        var erg = Vertretungsimport.Importieren(ziel, quelle, false, zuordnung);
        Assert.Equal(1, erg.Absenzen);
        Assert.Equal(quelle.Tagestexte.Count, erg.Tagestexte);
        Assert.Equal(quelle.Vertretungen.Count(v => v.Datum == tag), erg.Vertretungen);
        Assert.Contains(ziel.Abwesenheiten, a => a.ElementId == zielLehrer.Id && a.Von == tag);
        var offen = ziel.Vertretungen.Where(v => v.Datum == tag && v.Offen).ToList();
        Assert.NotEmpty(offen);
        Assert.All(offen, v => Assert.NotNull(ziel.Abwesenheiten.FirstOrDefault(a => a.Id == v.AbwesenheitId)));
        Assert.Equal(ziel.Vertretungen.Count, ziel.Vertretungen.Select(v => v.Id).Distinct().Count());
        Assert.True(vorher == 0 || ziel.Vertretungen.Count == erg.Vertretungen); // nicht additiv: vorhandene Daten ersetzt

        // erneuter Abgleich der Vertretungsplanung erzeugt keine doppelten Zeilen
        var anzahl = ziel.Vertretungen.Count(v => v.Datum == tag);
        Vertretungen.Aktualisieren(ziel, tag);
        Assert.Equal(anzahl, ziel.Vertretungen.Count(v => v.Datum == tag));
    }

    [Fact]
    public void AdditiverVertretungsimportBehaeltVorhandeneUndUeberspringtGleiche()
    {
        var (quelle, tag, _) = QuelleMitAbsenz();
        var ziel = Testdaten.Verplant();
        var andererTag = Kalender.NaechsterUnterrichtstag(ziel, tag.AddDays(1));
        var andererLehrer = ziel.Lehrer.Last().Id;
        ziel.Abwesenheiten.Add(new Abwesenheit { Id = ziel.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = andererLehrer, Von = andererTag, Bis = andererTag });
        Vertretungen.Aktualisieren(ziel, andererTag);
        var eigene = ziel.Vertretungen.Count;

        var erg = Vertretungsimport.Importieren(ziel, quelle, true);
        Assert.Equal(2, ziel.Abwesenheiten.Count);
        Assert.Equal(eigene + erg.Vertretungen, ziel.Vertretungen.Count);
        Assert.Contains(ziel.Vertretungen, v => v.Datum == andererTag);

        // zweiter additiver Import derselben Datei: nichts kommt doppelt
        var erg2 = Vertretungsimport.Importieren(ziel, quelle, true);
        Assert.Equal(0, erg2.Absenzen);
        Assert.Equal(0, erg2.Vertretungen);
        Assert.Equal(0, erg2.Tagestexte);
        Assert.True(erg2.Uebersprungen > 0);
        Assert.Equal(2, ziel.Abwesenheiten.Count);
    }

    [Fact]
    public void VertretungsimportOhneZuordnungMeldetFehlendeElemente()
    {
        var (quelle, tag, lehrer) = QuelleMitAbsenz();
        var ziel = Testdaten.Verplant();
        ziel.Lehrer.First(l => l.Id == lehrer).Kurzname = "ZZZ";
        var erg = Vertretungsimport.Importieren(ziel, quelle, false);
        Assert.Equal(0, erg.Absenzen);
        Assert.Contains(erg.Hinweise, h => h.StartsWith("Nicht zugeordnet: Lehrer", StringComparison.Ordinal));
        Assert.DoesNotContain(ziel.Vertretungen, v => v.Datum == tag && v.LehrerId == lehrer && v.AbwesenheitId.HasValue);
    }

    [Fact]
    public void KlausurStatistikKennzeichenGehtAufDieKlausurzeile()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var r = Tagesplan.Regulaer(d, tag).First(x => x.LehrerId.HasValue && x.KlassenIds.Count > 0);
        var k = new Klausur { Id = d.NeueId(), Name = "Mathe", Datum = tag, StundeVon = r.Stunde, StundeBis = r.Stunde, KlassenIds = r.KlassenIds.ToList(), LehrerIds = { r.LehrerId!.Value }, Statistik = "K" };
        d.Klausuren.Add(k);
        Vertretungen.Aktualisieren(d, tag);
        var zeile = Assert.Single(d.Vertretungen, v => v.KlausurId == k.Id && v.Art == VertretungsArt.Klausur);
        Assert.Equal("K", zeile.Statistik);
        k.Statistik = "n";
        Vertretungen.Aktualisieren(d, tag);
        Assert.Equal("n", d.Vertretungen.Single(v => v.KlausurId == k.Id && v.Art == VertretungsArt.Klausur).Statistik);
    }

    [Fact]
    public void KlausurRaumOhneKollisionErzeugtRaumvertretung()
    {
        var d = Testdaten.Verplant();
        var tag = Testdaten.Unterrichtstag(d);
        var regel = Tagesplan.Regulaer(d, tag);
        // ein Unterricht mit Raum; die Klausur schreibt eine andere Klasse, die in dieser Stunde nicht in dem Raum ist
        var (opfer, klasse) = regel.Where(x => x.RaumId.HasValue && x.KlassenIds.Count > 0)
            .Select(x => (x, k: d.Klassen.FirstOrDefault(c => !x.KlassenIds.Contains(c.Id) && regel.Any(y => y.Stunde == x.Stunde && y.KlassenIds.Contains(c.Id) && y.RaumId != x.RaumId))))
            .First(p => p.k != null);
        var k = new Klausur
        {
            Id = d.NeueId(), Name = "Englisch", Datum = tag, StundeVon = opfer.Stunde, StundeBis = opfer.Stunde, KlassenIds = { klasse!.Id }, RaumIds = { opfer.RaumId!.Value },
        };
        d.Klausuren.Add(k);
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.Art == VertretungsArt.Raumvertretung && v.UnterrichtId == opfer.Unterricht.Id && v.Stunde == opfer.Stunde);

        // Frage beim Bestätigen: der Raum ist durch den anderen Unterricht belegt
        var belegt = Klausurraeume.Belegungen(d, k);
        Assert.Contains(belegt, b => b.RaumId == opfer.RaumId && b.Stunde == opfer.Stunde && b.Eintrag.Unterricht?.Id == opfer.Unterricht.Id);
        // der Unterricht der Klausur selbst zählt nicht als Belegung
        Assert.Empty(Klausurraeume.Belegungen(d, new Klausur { Id = k.Id, Datum = tag, StundeVon = opfer.Stunde, StundeBis = opfer.Stunde, RaumIds = k.RaumIds, UnterrichtId = opfer.Unterricht.Id, KlassenIds = opfer.KlassenIds.ToList() }));

        k.RaumVerdraengen = true;
        Vertretungen.Aktualisieren(d, tag);
        Assert.Empty(Klausurraeume.Belegungen(d, k));
        var rv = Assert.Single(d.Vertretungen, v => v.Art == VertretungsArt.Raumvertretung && v.UnterrichtId == opfer.Unterricht.Id && v.ZeilenIndex == opfer.ZeilenIndex && v.Stunde == opfer.Stunde);
        Assert.True(rv.RaumVerdraengt);
        Assert.True(rv.RaumOffen);
        Assert.Equal(k.Id, rv.KlausurId);
        Assert.Equal(opfer.LehrerId, rv.VertreterId);
        // im Tagesplan hat der verdrängte Unterricht keinen Raum mehr, die Klausur den Raum allein
        var tp = Tagesplan.Berechnen(d, tag);
        Assert.Single(tp.Stunde(opfer.Stunde), e => e.Findetstatt && e.RaumIds.Contains(opfer.RaumId!.Value));

        // Klausur gelöscht → Raumvertretung verschwindet
        d.Klausuren.Remove(k);
        Vertretungen.Aktualisieren(d, tag);
        Assert.DoesNotContain(d.Vertretungen, v => v.KlausurId == k.Id);
    }

    [Fact]
    public void VertretungsnachrichtenJeLehrkraftNachVorlage()
    {
        var (d, tag, abwesend) = QuelleMitAbsenz();
        var offen = d.Vertretungen.First(v => v.Datum == tag && v.Offen && v.LehrerId == abwesend);
        var vertreter = d.Lehrer.First(l => l.Id != abwesend);
        vertreter.Email = "vertreter@schule.de";
        offen.VertreterId = vertreter.Id;
        offen.Text = "Arbeitsblatt";
        d.Vertretungseinstellungen.Nachrichten.Betreff = "Vertretung {Datum}";
        d.Vertretungseinstellungen.Nachrichten.Zeile = "{Stunde}. Std. {Klassen}  {Text}";
        var n = Vertretungsnachrichten.Erzeugen(d, tag, tag);
        var m = Assert.Single(n, x => x.LehrerId == vertreter.Id);
        Assert.Equal("Vertretung " + Format.Datum(tag), m.Betreff);
        Assert.Contains($"{offen.Stunde}. Std. {new DatenIndex(d).KKlassen(offen.NeueKlassenIds)} Arbeitsblatt", m.Text);
        Assert.Equal("vertreter@schule.de", m.Email);
        Assert.DoesNotContain(n, x => x.LehrerId == abwesend);
        d.Vertretungseinstellungen.Nachrichten.AbwesendeInformieren = true;
        Assert.Contains(Vertretungsnachrichten.Erzeugen(d, tag, tag), x => x.LehrerId == abwesend);
        Assert.StartsWith("mailto:vertreter%40schule.de?subject=", Vertretungsnachrichten.MailTo(m));
    }
}
