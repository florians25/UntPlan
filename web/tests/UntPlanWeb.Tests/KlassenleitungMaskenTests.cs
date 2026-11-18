namespace UntPlanWeb.Tests;

/// <summary>
/// Feinabgleich der Klassenleitungs-Masken mit den Abbildungen des Leitfadens: Reihenfolge und Beschriftung der Filter, Spalten und Knöpfe
/// (Abwesenheiten, Klassendienste, Befreiungen, Klassenbucheinträge, Berichte, Schülergruppen) und der gemeinsame Zeitraum-Baustein.
/// </summary>
public class KlassenleitungMaskenTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

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
    public void Zeitraum_mit_Auswahl_wie_im_Original()
    {
        var zb = Datei("js", "zeitbereich.js");
        Reihenfolge(zb, "export const ZEITBEREICHE", "'Aktuelle Woche'", "'Aktueller Monat'", "'Bis Schuljahresende'", "'Aktuelles Schuljahr'", "'Datumsbereich'");
        foreach (var datei in new[] { "abwesenheiten.js", "dienste.js", "eintraege.js", "berichte.js" })
            Assert.Contains("zeitbereichFeld(", Datei("js", "ansichten", datei));
    }

    [Fact]
    public void Abwesenheiten_Filter_Spalten_und_Knoepfe()
    {
        var js = Datei("js", "ansichten", "abwesenheiten.js");
        Reihenfolge(js, "<span>Klasse</span>", "<span>Schüler*innen</span>", "<span>Suche</span>", "zeitbereichFeld(", "<span>Abwesenheitsgrund</span>", "<span>Status</span>", "<span>Sortierung</span>");
        Reihenfolge(js, "<th>Art</th>", "<th>Benutzer</th>", "<th>Schüler*innen</th>", "<th>Klasse</th>", "<th>Kl-Lehrkräfte</th>", "<th>Von</th>", "<th>Beginn</th>",
            "<th>Bis</th>", "<th>Ende</th>", "<th>Grund</th>", "<th>Status</th>", "<th>Text</th>");
        Reihenfolge(js, "data-neu>Neu</button>", "Löschen</button>", "Zusammenfassen</button>", "Entschuldigen</button>", "Berichte <i");
        Assert.Contains("Elemente gefunden, Anzeige aller Elemente.", js);
        Assert.Contains("Ein Element gefunden.", js);
        Assert.Contains("Entschuldigungsnummer", js);
        Reihenfolge(js, "titel: 'Abwesenheit', breit: true", "Schüler*innen", "Abwesenheitsgrund", "Text", "Gemeldet an", "Erziehungsberechtigter", "Aufgegeben am");
    }

    [Fact]
    public void Klassendienste_Listenansicht_und_Dialog()
    {
        var js = Datei("js", "ansichten", "dienste.js");
        Reihenfolge(js, "function altListe()", "<span>Klasse</span>", "<span>Art</span>", "zeitbereichFeld(", "<th>Art</th>", "<th>Dienst</th>", "<th>Name</th>", "<th>Klasse</th>",
            "<th>Zeit</th>", "<th>Text</th>", "Nichts anzuzeigen", ">Neu</button>", ">Löschen</button>", "Berichte <i");
        Reihenfolge(js, "async function einteilenDialog", "<span>Dienst</span>", "- Auswahl -", "<span>Von</span>", "<span>Bis</span>", "<span>Anmerkung</span>", "text: 'Speichern'", "text: 'Schließen'");
        Assert.DoesNotContain("data-woche=\"0\">diese Woche", js);
    }

    [Fact]
    public void Befreiung_Dialog_wie_im_Original()
    {
        var js = Datei("js", "ansichten", "befreiungen.js");
        Reihenfolge(js, "titel: b ? 'Befreiung' : 'Neue Befreiung'", ">Klasse</label>", "Schüler*innen (", ">Zeitraum</label>", ">Wochentag</label>", ">Fach</label>",
            "&lt;Auswahl&gt;", ">Beginnzeit</label>", ">Endzeit</label>", ">Befreiungsgrund</label>", ">Text</label>", "text: 'Speichern'", "text: 'Abbrechen'");
        Assert.Contains("<option value=\"\">Bitte wählen Sie</option>", js);
    }

    [Fact]
    public void Klassenbucheintraege_Filter_und_Spalten()
    {
        var js = Datei("js", "ansichten", "eintraege.js");
        Reihenfolge(js, "<span>Klasse</span>", "<span>Schüler*innen</span>", "<span>Eintragskategorie</span>", "<span>Eintragskategoriegruppe</span>", "zeitbereichFeld(");
        Reihenfolge(js, "<th>Art</th>", "<th>Name</th>", "<th>Klasse</th>", "<th>Tag</th>", "<th>Datum</th>", "<th>Zeit</th>", "<th>Fach</th>", "<th>Benutzer</th>",
            "<th>Eintragskategorie</th>", "<th>Kategoriegruppe</th>", "<th>Text</th>");
        Reihenfolge(js, "data-neu>Neu</button>", ">Löschen</button>", ">Bericht</button>", "Berichte <i");
    }

    [Fact]
    public void Berichte_Kopf_und_Einstellungen_in_der_Zeile()
    {
        var js = Datei("js", "ansichten", "berichte.js");
        Reihenfolge(js, "<span>Klasse</span>", "<span>Schüler*innen</span>", "zeitbereichFeld(");
        Assert.Contains("const GEMEINSAM = ['fehlzeiten', 'verspaetungen', 'zaehlend', 'alle'];", js);
        Assert.Contains("zeilenOptionen(a)", js);
        var sg = Datei("js", "ansichten", "schuelergruppen.js");
        Assert.Contains("Elemente gefunden, Anzeige aller Elemente.", sg);
    }
}
