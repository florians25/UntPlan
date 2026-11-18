namespace UntPlanWeb.Tests;

/// <summary>
/// Feinabgleich der Masken mit den Screenshots der Anforderungen (Buchen, Mein Unterricht, Klassenbuch-Übersicht): Reihenfolge und
/// Beschriftung der Felder, Spalten und Knöpfe, Knöpfe links unter der Maske, keine zusätzlichen Kopfknöpfe.
/// </summary>
public class MaskenWieVorlageTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    /// <summary>Die Texte stehen in genau dieser Reihenfolge im Quelltext (ab dem Startpunkt).</summary>
    private static void Reihenfolge(string quelle, string start, params string[] texte)
    {
        var pos = quelle.IndexOf(start, StringComparison.Ordinal);
        Assert.True(pos >= 0, start);
        foreach (var t in texte)
        {
            var neu = quelle.IndexOf(t, pos, StringComparison.Ordinal);
            Assert.True(neu >= 0, $"„{t}“ fehlt oder steht nicht nach der vorigen Angabe");
            pos = neu + t.Length;
        }
    }

    [Fact]
    public void Neue_Aktivitaet_Neuer_Unterricht_und_Veranstaltung_in_der_Feldreihenfolge_der_Vorlage()
    {
        var js = Datei("js", "ansichten", "termin.js");
        // Neue Aktivität (Seite 3): Lehrkraft | Klasse, Räume, Legende, Fach, Von - Bis, Abteilung, Menge | Verantwortlicher, Art, Teilnehmer | Kostenstelle, …
        Reihenfolge(js, "function buchenZeichnen()", "pfeld('Lehrkraft'", "pfeld('Klasse'", "pfeld('Räume'", "LEGENDE", "pfeld('Fach'", "vonBis(",
            "[f.abteilung]", "[f.menge, f.verantwortlich]", "[f.art]", "[f.teilnehmer, f.kostenstelle]", "...textFelder(d.zu)",
            "'E-Mail-Benachrichtigung bei Statusänderung'", "'Lehrkraft der Aktivität benachrichtigen'", "'Buchung sofort bestätigen'", "data-speichern>Speichern</button>", "data-zurueck>Zurück</button>");
        Assert.Contains("const vonBis = (von, bis, periode = '') => `<div class=\"tm-vonbis\">${pfeld('Von - Bis', von)}", js);
        Assert.Contains("aktivitaetsart: 'Unterricht'", js);
        // Neuer Unterricht (Seite 5): ohne zusätzliches Feld „Bezeichnung / Schülergruppe“
        Reihenfolge(js, "export async function neuerUnterricht", "pfeld('Lehrkraft'", "pfeld('Klasse'", "pfeld('Fach'", "vonBis(", "pfeld('Wochenstunden'", "pfeld('Jahresstunden'",
            "[zf.abteilung]", "[zf.verantwortlich]", "[zf.art]", "...textFelder(d.zu)", "data-speichern>Speichern</button>", "data-zurueck>Zurück</button>");
        Assert.DoesNotContain("Bezeichnung / Schülergruppe", js);
        // Veranstaltung am … (Seite 10): Beginnzeit | Dauer, Fach, Abteilung | Verantwortlicher, Art, … ; Buchen links von Abbrechen
        Reihenfolge(js, "function veranstaltung(", "pfeld('Beginnzeit'", "pfeld('Dauer [Min.]'", "pfeld('Fach'", "[zf.abteilung, zf.verantwortlich]", "[zf.art]",
            "'Lehrkraft der Aktivität benachrichtigen'", "'E-Mail-Benachrichtigung bei Statusänderung'", "'Buchung sofort bestätigen'", "data-buchen>Buchen</button>", "data-kbd-zu>Abbrechen</button>");
        // Terminsuche (Seite 9): „Lehrkraftliste“ als offene Liste, kein zusätzlicher Hinweistext
        Assert.Contains("liste('lehrer', 'Lehrkraftliste'", js);
        Assert.Contains("data-nativ data-liste=", js);
        Assert.DoesNotContain("Wählen Sie Lehrkräfte, Klassen und Räume", js);
        // Zusätzliche Stunde (Seite 8): Tagesspalte „16.2.“
        Assert.Contains("const tagPunkt = iso => `${Number(iso.slice(8, 10))}.${Number(iso.slice(5, 7))}.`;", js);
        // ein Titel je Maske (kein zusätzlicher oranger Titel, keine Kopfknöpfe), Knöpfe links
        Assert.DoesNotContain("<h2 class=\"rb-titel\">", js);
        Assert.DoesNotContain("<i class=\"bi bi-list-ul\"></i>Meine Buchungen</a>", js);
        var css = Datei("css", "termin.css");
        Assert.Contains(".rb.tm .tm-fuss { justify-content: flex-start; }", css);
        Assert.Contains(".tm-dialog .kbd-fuss { justify-content: flex-start; }", css);
    }

    [Fact]
    public void Meine_Buchungen_als_Tabelle_mit_den_Spalten_der_Vorlage()
    {
        var js = Datei("js", "ansichten", "buchen.js");
        Reihenfolge(js, "async function meineBuchungen(", "Suche <input", "data-suche-los>Suche ausführen</button>", "data-filter-auf title=\"Filter\"",
            "Buchungsliste <i class=\"bi bi-caret-down-fill\"></i>", ">Neu</a>");
        Reihenfolge(js, "async function meineBuchungen(", ">Auswahl<br>", "<th>Art</th><th>Status</th><th>Art</th><th class=\"sortiert\">Raum</th><th>Tag</th><th class=\"sortiert\">Datum</th><th>Von</th><th>Bis</th><th>Periodizität</th>",
            "<th>Bemerkung</th><th>Anmerkung</th><th>Schüler</th><th class=\"sortiert\">Aufgegeben am</th><th class=\"sortiert\">Buchung</th>",
            "'Kein Element gefunden.'", "'Ein Element gefunden.'", "Elemente gefunden, Anzeige aller Elemente.");
        Assert.Contains("kontext.titel(alle ? 'Buchungen' : 'Meine Buchungen');", js);
        Assert.Contains(".bu-mb-tabelle th.sortiert { color: var(--u-akzent); }", Datei("css", "buchen.css"));
    }

    [Fact]
    public void Mein_Unterricht_mit_Spalten_Abwesenheiten_und_Knoepfen_der_Vorlage()
    {
        var js = Datei("js", "ansichten", "unterricht.js");
        Reihenfolge(js, "inhalt.innerHTML = zeilen.length", "<th></th><th>Art</th><th>Aktivitätsart</th><th>Klasse</th><th>Schülergruppe</th><th>Fach</th><th>Lehrkraft</th>",
            "<th class=\"text-end\">Wst</th><th class=\"mu-sortiert\">Von</th><th class=\"mu-sortiert\">Bis</th><th>Termine</th><th></th><th>Schüler</th><th>Schülergruppen</th><th>Berichte</th>");
        // Knöpfe im neuen Design rechts in der Filterleiste, darunter die Karten „Unterricht“ und „Abwesenheiten“
        Reihenfolge(js, "seite.innerHTML = `", "Zeitraum", "u-fl-aktionen", ">Abwesenheit melden</a>", "data-bericht>Bericht</button>", ">Neuer Unterricht</a>",
            "karteKopf('Abwesenheiten')", "Nichts anzuzeigen");
        Assert.DoesNotContain("data-suche", js);
        // „Neuer Unterricht“ ist wie in der Vorlage kein eigener Menüpunkt mehr
        Assert.Contains("text: 'Neuer Unterricht', icon: 'meineunterrichte', verborgen: true }", Datei("js", "app.js"));
    }

    [Fact]
    public void Klassenbuch_Uebersicht_Leerzustand_wortgenau_und_ohne_Heute_Markierung()
    {
        var kb = Datei("js", "ansichten", "klassenbuch.js");
        Assert.Contains("'Benutzen Sie die Suche, um das Ergebnis mit zumindest einem Lehrer und/oder Klasse einzuschränken.'", kb);
        Assert.DoesNotContain("kbl-heute", kb);
    }
}
