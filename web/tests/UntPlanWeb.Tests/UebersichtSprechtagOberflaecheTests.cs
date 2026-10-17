namespace UntPlanWeb.Tests;

/// <summary>
/// Oberfläche der Übersicht (alle Rollen) und der Sprechtag-Ansicht: ruhiger Aufbau mit Kopf/Tageszeile, Kennzahlen-Leiste und
/// zweispaltigem Raster aus gleich hohen Karten (Gruppen mit höchstens fünf Zeilen, Leeres in einer grauen Zeile); Sprechtag mit
/// zweizeiliger Kopfzeile (Status-Pill auf der Datumszeile), gleich hohen Kartenköpfen, gleichen Abständen und bündigen Spalten.
/// </summary>
public class UebersichtSprechtagOberflaecheTests
{
    private static string Datei(params string[] teile)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "UntPlanWeb", "UntPlanWeb.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "src", "UntPlanWeb", "wwwroot" }.Concat(teile).ToArray()));
    }

    [Fact]
    public void Uebersicht_hat_Tageszeile_Kennzahlen_und_vier_Bereiche()
    {
        var js = Datei("js", "ansichten", "uebersicht.js");
        // Kopf mit Begrüßung und Tageszeile (nächste Stunde bzw. kein Unterricht, Ferienhinweis)
        Assert.Contains("class=\"ub-tageszeile\"", js);
        Assert.Contains("Heute kein Unterricht", js);
        Assert.Contains("Als Nächstes", js);
        Assert.Contains("class=\"ub-ferien\"", js);
        // Kennzahlen-Leiste, anklickbar; nur Werte > 0 hervorgehoben
        Assert.Contains("class=\"ub-zahlen\"", js);
        Assert.Contains("<a class=\"ub-kz ${zahl > 0 ? 'an' : ''}\" href=\"${href}\">", js);
        foreach (var t in new[] { "Offene Klassenbuch-Einträge", "Offene Abwesenheiten", "Prüfungen", "Ungelesene Mitteilungen" })
            Assert.Contains($"titel: '{t}'", js);
        // vier Bereiche als Karten mit Gruppen statt vieler Einzelkarten
        foreach (var t in new[] { "Heute & nächste Tage", "Zu erledigen", "Termine", "Mitteilungen & Infos" })
            Assert.Contains($"titel: '{t}'", js);
        // höchstens fünf Zeilen je Gruppe, sonst „Alle anzeigen“; Leeres als eine graue Sammelzeile
        Assert.Contains("export const MAX = 5;", js);
        Assert.Contains("zeilen.slice(0, MAX)", js);
        Assert.Contains("Alle anzeigen", js);
        Assert.Contains("class=\"ub-ruhig\"", js);
        // Vertretungen/Änderungen in die Tage eingeflochten, keine eigene Karte mehr; keine Kennzahl-Kacheln
        Assert.Contains("Weitere Änderungen", js);
        Assert.DoesNotContain("Vertretungen und Änderungen", js);
        Assert.DoesNotContain("ub-kachel", js);
        // in den Ferien wird die Woche nach Ferienende nachgeladen (nächster Unterrichtstag)
        Assert.Contains("plan(montag(plusTage(ferienVorab.bis, 3)))", js);
    }

    [Fact]
    public void Uebersicht_Raster_ist_ruhig_und_buendig()
    {
        var css = Datei("css", "uebersicht.css");
        var app = Datei("css", "app.css");
        // eigene Datei, alte Kachel-/Masonry-Regeln aus app.css entfernt
        Assert.DoesNotContain(".ub-kachel", app);
        Assert.DoesNotContain(".ub-raster", app);
        // zwei Spalten, Karten einer Zeile gleich hoch; ungerade Anzahl: letzte Karte über beide Spalten; mobil einspaltig
        Assert.Contains(".ub-raster { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; align-items: stretch; }", css);
        Assert.Contains(".ub-raster.ungerade > .ub-karte:last-child { grid-column: 1 / -1; }", css);
        Assert.Contains("@container (max-width: 760px) {\n  .ub-raster { grid-template-columns: minmax(0, 1fr); }", css);
        // neutrale Kartenköpfe: dunkelgrauer Titel in Medium, Akzent nur am Symbol; einheitliche Zeilenhöhe
        Assert.Contains(".ub-karte > header h2 { margin: 0; font-size: 1rem; line-height: 1.2; font-weight: 500; color: var(--u-titel);", css);
        Assert.Contains(".ub-karte > header > .bi { font-size: 1.05rem; line-height: 1; color: var(--u-akzent); }", css);
        Assert.Contains(".ub-z { display: flex; align-items: center; gap: 12px; height: 40px;", css);
        // Schrift nur Regular/Medium
        Assert.DoesNotMatch(@"font-weight:\s*[6-9]00", css);
    }

    [Fact]
    public void Sprechtag_Kopf_und_Karten_haben_gleiche_Hoehen()
    {
        var js = Datei("js", "ansichten", "sprechtag.js");
        var css = Datei("css", "sprechtag.css");
        // Kopf: oben Datum, Pill und Phasentext auf einer Linie, darunter Zeiten und Zusatz
        Assert.Contains("<div class=\"st-kopf-zeile\"><b class=\"st-kopf-datum\">${e(t.datumText)}</b>${statusChip(t, familie)}", js);
        Assert.Contains("<div class=\"st-kopf-zeile st-kopf-unter\">", js);
        Assert.Contains(".st-kopf-zeile { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; min-height: 28px;", css);
        // doppeltes Datum in „Meine Termine“ entfällt, Knöpfe im Kopf auf einer Linie
        Assert.DoesNotContain("st-tag", js);
        Assert.Contains("<span class=\"st-kopf-knoepfe\">", js);
        // gleich hohe Kartenköpfe, gleiche Innenabstände, bündige Spalten, gleich hohe Zeilen
        Assert.Contains(".st-karte > header { display: flex; flex-wrap: nowrap; align-items: center; gap: 10px; height: 36px; margin: 0 0 16px;", css);
        Assert.Contains("border-radius: 10px; padding: 20px; margin-bottom: 16px;", css);
        Assert.Contains(".st-raster { display: grid; grid-template-columns: minmax(0, 5fr) minmax(0, 7fr); gap: 16px; align-items: stretch;", css);
        Assert.Contains(".st-links > .st-karte:last-child { flex: 1; }", css);
        Assert.Contains(".st-einladungen td, .st-zeitraster td { height: 48px; white-space: nowrap; }", css);
        Assert.Contains(".st-flach td { vertical-align: middle; height: 56px;", css);
        Assert.DoesNotMatch(@"font-weight:\s*[6-9]00", css);
    }
}
