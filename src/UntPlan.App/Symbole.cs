using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace UntPlan.App;

/// <summary>
/// Programmatisch gezeichnete Symbole im einheitlichen flachen Stil (skalierbar, keine externen Grafikdateien).
/// Jedes Symbol ist als kurze Zeichenanweisung in einem 32×32-Raster beschrieben; der <see cref="Zeichner"/>
/// rastet alle Kanten auf ganze Pixel ein, zieht einen dezenten dunklen Umriss (immer genau 1 px bei 16/32 px)
/// und verwendet für Größen bis 16 px eine vereinfachte Variante („name@16“), sofern vorhanden.
/// Dieselben Anweisungen liest build/logo.py, um Programmlogo, ICO-Datei und Vorschaubogen zu erzeugen.
/// </summary>
/// <remarks>
/// Befehle (durch „;“ getrennt; mit „&amp;“ verbundene Befehle bilden eine Gruppe mit gemeinsamem Umriss):
/// R x y b h [r] F – Rechteck (abgerundet) · E x y b h F – Ellipse · P x1 y1 x2 y2 … F – Vieleck ·
/// C x y b h start winkel F – Tortenstück · H x y s F – Person · S cx cy r ri n F – Stern ·
/// Z cx cy r n F – Zahnrad mit Loch · L x1 y1 … F wN – Linienzug · A x y b h start winkel F wN – Bogen ·
/// V x1 y1 … F wN – Pfeil · B x y b h start winkel F wN – Bogenpfeil.
/// Zusätze: „!“ = Fläche ohne Umriss, „h“ = Linie mit dunklem Saum, wN = Strichstärke in Rastereinheiten.
/// Eine Definition „=name“ ist ein Alias.
/// </remarks>
public static class Symbole
{
    private static readonly Dictionary<(string, int), Bitmap> Cache = new();

    /// <summary>Einheitliche Palette (Fluent-ähnlich, kontraststark; Formen unterscheiden sich nie nur durch Rot/Grün).</summary>
    private static readonly Dictionary<string, int> Farben = new()
    {
        ["w"] = 0xFFFFFF,   // Weiß (Papier)
        ["hg"] = 0xD6DBE2,  // Hellgrau
        ["gr"] = 0x6E7787,  // Grau
        ["dk"] = 0x2D333C,  // Dunkel (Zeichenstriche)
        ["b"] = 0x2F6FC0,   // Blau
        ["hb"] = 0xC4DAF5,  // Hellblau
        ["pe"] = 0x125E7A,  // Petrol
        ["nv"] = 0x1D4F73,  // Dunkelblau (Hausfarbe, Logo)
        ["mi"] = 0x5FBF96,  // Mint (Logo-Akzent)
        ["hpe"] = 0xB5DCE6, // Hellpetrol
        ["g"] = 0x2E8048,   // Grün
        ["lg"] = 0x2E7040,  // Logo-Grün (Hausfarbe)
        ["hgr"] = 0xBFE3C4, // Hellgrün
        ["o"] = 0xE5832A,   // Orange
        ["ho"] = 0xFAD7B0,  // Hellorange
        ["r"] = 0xC9372C,   // Rot
        ["y"] = 0xF2BE2E,   // Gelb
        ["hy"] = 0xFBE7A6,  // Hellgelb
        ["gy"] = 0xD49A1A,  // Ocker (Ordnerrückseite)
        ["v"] = 0x7A57B8,   // Violett
        ["hv"] = 0xDCD0F0,  // Hellviolett
        ["br"] = 0x9C6B3E,  // Braun
        ["hbr"] = 0xDDBB92, // Hellbraun
    };

    /// <summary>Zeichenanweisungen aller Symbole (32er-Raster).</summary>
    private static readonly Dictionary<string, string> Definitionen = new()
    {
        // Stammdaten
        ["klassen"] = "H 2 5 13 o; H 17 5 13 b; H 7 10 18 g",
        ["klassen@16"] = "H 2 4 14 o; H 16 4 14 b; H 8 12 16 g",
        ["lehrer"] = "R 9 3 21 15 1 lg; L 13 8 24 8 w w1.5; L 13 12 20 12 w w1.5; H 2 12 17 b",
        ["lehrer@16"] = "R 10 2 20 14 1 lg; L 14 8 24 8 w w2; H 2 12 18 b",
        ["raeume"] = "R 7 3 18 26 1 br; R 10 6 12 8 hbr!; R 10 18 12 8 hbr!; E 19 14.5 3 3 y!",
        ["raeume@16"] = "R 8 2 16 28 1 br; R 12 6 8 8 hbr!; R 18 16 4 4 y!",
        ["faecher"] = "R 8 5 19 24 1 w; R 5 3 19 24 1 b; R 9 7 11 4 hb!",
        ["schueler"] = "H 4 4 24 g",
        ["benutzer"] = "E 2 2 28 28 hpe; H 8 7 16 pe",
        ["abteilungen"] = "L 6 21 6 15 26 15 26 21 dk w2; L 16 11 16 21 dk w2; R 10 3 12 8 1 pe; R 2 21 8 8 1 b; R 12 21 8 8 1 b; R 22 21 8 8 1 b",
        ["abteilungen@16"] = "L 6 22 6 16 26 16 26 22 dk w2; R 10 2 12 10 1 pe; R 2 22 8 8 b; R 22 22 8 8 b",
        ["kurs"] = "P 2 8 16 11 30 8 30 27 16 30 2 27 b; P 16 10 16 27 4 24 4 6 w & P 16 10 28 6 28 24 16 27 w; L 7 11 13 12.5 hg w1.5; L 7 15 13 16.5 hg w1.5; L 19 12.5 25 11 hg w1.5; L 19 16.5 25 15 hg w1.5",
        ["kurs@16"] = "P 2 8 16 12 30 8 30 28 16 30 2 28 b; P 16 10 16 26 4 24 4 6 w & P 16 10 28 6 28 24 16 26 w",
        ["modul"] = "R 4 11 17 17 1 v & E 9 5 7 7 v & E 18 16 7 7 v",
        ["perioden"] = "R 10 3 19 18 2 w & R 10 3 19 6 2 o; R 10 7 19 2 w!; R 3 10 19 19 2 w & R 3 10 19 6 2 pe; R 3 14 19 2 w!; L 7 20 18 20 hg w2; L 7 24 14 24 hg w2",
        ["datenbank"] = "R 5 8 22 17 b & E 5 20 22 9 b; E 5 3 22 9 hb; A 5 11 22 9 0 180 hb w1.5",

        // Pläne und Kalender
        ["stundenplan"] = "R 2 2 28 28 2 w & R 2 2 28 7 2 pe; R 2 7 28 2 w!; R 9 9 7 7 hb!; R 23 9 7 7 hgr!; R 16 16 7 7 ho!; R 2 23 7 7 hy!; R 23 23 7 7 hb!; L 9 9 9 30 hg w1; L 16 9 16 30 hg w1; L 23 9 23 30 hg w1; L 2 16 30 16 hg w1; L 2 23 30 23 hg w1",
        ["stundenplan@16"] = "R 2 2 28 28 2 w & R 2 2 28 8 2 pe; R 2 8 28 2 w!; R 12 10 8 10 hb!; R 22 20 8 10 ho!; R 2 20 8 10 hgr!; L 11 10 11 30 hg w2; L 21 10 21 30 hg w2; L 2 20 30 20 hg w2",
        ["uebersicht"] = "R 2 5 28 22 2 w & R 2 5 7 22 2 b; R 7 5 2 22 w!; L 2 11 30 11 hg w1; L 2 16 30 16 hg w1; L 2 21 30 21 hg w1; R 11 7 7 3 hb!; R 20 12 8 3 ho!; R 13 17 9 3 hgr!; R 11 22 6 3 hy!; R 21 22 7 3 hb!",
        ["uebersicht@16"] = "R 2 6 28 22 2 w & R 2 6 8 22 2 b; R 8 6 2 22 w!; L 2 17 30 17 hg w2; R 12 10 8 4 hb!; R 20 20 8 4 ho!",
        ["planen"] = "R 3 5 26 24 2 w & R 3 5 26 7 2 b; R 3 10 26 2 w!; L 9 20 14 25 23 15 g w3.5 h",
        ["kalender"] = "R 3 5 26 24 2 w & R 3 5 26 8 2 r; R 3 11 26 2 w!; R 8 2 2 6 1 dk!; R 22 2 2 6 1 dk!; R 7 16 4 3 hg!; R 14 16 4 3 hg!; R 21 16 4 3 hg!; R 7 22 4 3 hg!; R 14 22 4 3 r!; R 21 22 4 3 hg!",
        ["kalender@16"] = "R 2 4 28 26 2 w & R 2 4 28 10 2 r; R 2 12 28 2 w!; R 6 18 6 4 hg!; R 14 18 6 4 hg!; R 22 18 4 4 hg!; R 6 24 6 4 hg!; R 14 24 6 4 r!",
        ["zeitraster"] = "R 12 12 18 18 1 w; L 18 12 18 30 hg w1; L 24 12 24 30 hg w1; L 12 18 30 18 hg w1; L 12 24 30 24 hg w1; R 18 18 6 6 hb!; E 2 2 17 17 b; E 5 5 11 11 w!; L 10.5 7 10.5 10.5 13.5 10.5 dk w2",
        ["zeitraster@16"] = "R 12 12 18 18 1 w; R 20 20 10 10 hb!; E 2 2 18 18 b; E 6 6 10 10 w!",
        ["zeitwunsch"] = "E 3 3 26 26 o; E 7 7 18 18 w!; L 16 9.5 16 16 21 16 dk w2.5",
        ["zeitwunsch@16"] = "E 2 2 28 28 o; E 7 7 18 18 w!; L 16 10 16 16 21 16 dk w3",
        ["ferien"] = "E 19 2 10 10 y; C 3 8 22 16 180 180 o; L 14 16 14 26 dk w2; R 2 26 28 4 1 hy",
        ["veranstaltung"] = "R 3 5 26 24 2 w & R 3 5 26 8 2 v; R 3 11 26 2 w!; R 8 2 2 6 1 dk!; R 22 2 2 6 1 dk!; S 16 21 7 3 5 y",
        ["klausur"] = "P 5 3 19 3 24 8 24 29 5 29 w; P 19 3 19 8 24 8 hg; L 9 12 19 12 hg w2; L 9 17 19 17 hg w2; L 9 22 13 22 hg w2; P 15 29 16 24 25 15 29 19 20 28 y; P 15 29 16 24 20 28 hbr!; P 15 29 15.6 26.6 17.4 28.4 dk!",
        ["vormerkung"] = "P 8 3 24 3 24 29 16 22 8 29 o; L 12 9 20 9 w w2",
        ["block"] = "P 16 3 28 9 16 15 4 9 hb & P 4 9 16 15 16 29 4 23 b & P 16 15 28 9 28 23 16 29 pe",

        // Unterricht und Vertretung
        ["unterricht"] = "R 4 3 24 26 2 w & R 4 3 7 26 2 g; R 9 3 2 26 w!; L 14 9 24 9 hg w2; L 14 14 24 14 hg w2; L 14 19 24 19 hg w2; L 14 24 20 24 hg w2",
        ["vertretung"] = "H 2 12 14 gr; H 15 12 15 b; V 7 10 9 6 14 4 19 5 22 9 g w2.5 h",
        ["vertretung@16"] = "H 2 12 14 gr; H 16 12 14 b; V 8 10 12 5 20 5 23 9 g w3 h",
        ["abwesenheit"] = "H 2 3 20 gr; E 17 17 13 13 r; L 20.5 23.5 26.5 23.5 w w3",
        ["absenz"] = "=abwesenheit",
        ["entfall"] = "P 6 3 21 3 26 8 26 29 6 29 w; P 21 3 21 8 26 8 hg; L 11 13 21 23 r w3.5 h & L 21 13 11 23 r w3.5 h",
        ["bereitschaft"] = "E 13 23 6 6 dk; C 8 5 16 16 180 180 o & R 8 13 16 8 o & P 4 25 28 25 24 20 8 20 o & E 14.5 3 3 3 o",
        ["sondereinsatz"] = "P 20 2 7 18 15 18 12 30 25 13 17 13 23 2 o",
        ["pausenaufsicht"] = "P 16 3 28 7 27 19 16 29 5 19 4 7 b; L 10 16 15 21 23 11 w w3",
        ["fixieren"] = "L 16 20 16 30 dk w2; R 11 3 10 4 1 r & P 12 7 20 7 22 17 10 17 r & R 7 16 18 4 1 r",
        ["raumzuordnen"] = "R 3 5 13 23 1 br; R 5 8 9 7 hbr!; E 11 17 2.5 2.5 y!; V 17 17 30 17 g w3.5 h",
        ["tausch"] = "V 4 10 28 10 b w3.5 h; V 28 22 4 22 o w3.5 h",
        ["gewichtung"] = "L 16 6 16 27 dk w2.5; L 9 28 23 28 dk w3; L 7 9 25 9 dk w2.5; E 14 4 4 4 dk!; L 3 18 7 9 11 18 gr w1.5; L 21 18 25 9 29 18 gr w1.5; C 2 13 10 10 0 180 y; C 20 13 10 10 0 180 y",
        ["optimierung"] = "Z 12 12 9 8 gr; Z 23 22 7 8 o",
        ["einstellungen"] = "Z 16 16 13 8 gr",
        ["diagnose"] = "E 2 2 28 28 hb; L 5 17 10 17 13 9 18 24 21 14 27 14 r w2.5 h",
        ["assistent"] = "L 5 28 18 15 dk w3.5 h; S 23 10 8 3.5 5 y; S 9 8 4 1.5 4 y; S 26 25 3.5 1.3 4 y",
        ["normalform"] = "V 16 16 16 3 b w2.5 h & V 16 16 16 29 b w2.5 h & V 16 16 3 16 b w2.5 h & V 16 16 29 16 b w2.5 h",
        ["wertrechnung"] = "R 6 3 20 26 2 gr; R 9 6 14 6 hgr!; R 9 15 3 3 w!; R 14.5 15 3 3 w!; R 20 15 3 3 w!; R 9 20 3 3 w!; R 14.5 20 3 3 w!; R 20 20 3 3 o!; R 9 25 3 2 w!; R 14.5 25 3 2 w!; R 20 25 3 2 o!",
        ["wertrechnung@16"] = "R 6 2 20 28 2 gr; R 10 6 12 6 hgr!; R 10 16 4 4 w!; R 18 16 4 4 o!; R 10 22 4 4 w!; R 18 22 4 4 w!",
        ["statistik"] = "R 4 16 6 13 b; R 13 7 6 22 o; R 22 11 6 18 g",

        // Datei und Bearbeiten
        ["neu"] = "P 7 4 20 4 26 10 26 29 7 29 w; P 20 4 20 10 26 10 hg; S 24 8 6 2 4 y",
        ["oeffnen"] = "P 3 6 12 6 15 9 27 9 27 27 3 27 gy; P 3 27 8 14 30 14 25 27 y",
        ["speichern"] = "P 4 4 24 4 28 8 28 28 4 28 b; R 9 4 13 8 w; R 17 5 3 6 b!; R 8 17 16 11 hb",
        ["drucken"] = "R 9 3 14 10 w; R 3 11 26 13 2 gr; R 9 19 14 10 w; L 12 23 20 23 hg w1.5; L 12 26 18 26 hg w1.5; E 23 14 3 3 hgr!",
        ["vorschau"] = "P 4 3 17 3 22 8 22 28 4 28 w; L 8 9 15 9 hg w2; L 8 13 12 13 hg w2; E 13 13 12 12 hb!; A 13 13 12 12 0 360 dk w2.5; L 23.5 23.5 29 29 dk w3.5",
        ["loeschen"] = "L 8 8 24 24 r w5 h & L 24 8 8 24 r w5 h",
        ["kopieren"] = "R 11 3 17 20 2 hb; R 5 9 17 20 2 w; L 9 15 18 15 hg w2; L 9 19 18 19 hg w2; L 9 23 15 23 hg w2",
        ["rueckgaengig"] = "V 26 26 26 18 24.5 14 21 11 16 10 6 10 b w3.5 h",
        ["wiederholen"] = "V 6 26 6 18 7.5 14 11 11 16 10 26 10 b w3.5 h",
        ["aktualisieren"] = "B 5 5 22 22 160 140 g w3 h & B 5 5 22 22 340 140 g w3 h",
        ["sortieren"] = "R 3 5 14 4 1 b; R 3 13 10 4 1 b; R 3 21 6 4 1 b; V 24 4 24 28 dk w3 h",
        ["filter"] = "P 3 4 29 4 19 16 19 28 13 24 13 16 hpe",
        ["suchen"] = "E 4 4 17 17 hb!; A 4 4 17 17 0 360 dk w3; L 19 19 28 28 dk w4",
        ["import"] = "P 3 17 8 17 8 24 24 24 24 17 29 17 29 29 3 29 gr; V 16 3 16 21 g w3.5 h",
        ["export"] = "P 3 17 8 17 8 24 24 24 24 17 29 17 29 29 3 29 gr; V 16 21 16 3 o w3.5 h",
        ["html"] = "R 3 5 26 22 2 w; R 4 6 24 4 hb!; L 12 13 8 18.5 12 24 o w2.5; L 20 13 24 18.5 20 24 o w2.5; L 18 12 14 25 dk w2",
        ["web"] = "E 3 3 26 26 b; A 10 3 12 26 0 360 w w1.5; L 3 16 29 16 w w1.5; L 6 10 26 10 w w1.5; L 6 22 26 22 w w1.5",
        ["web@16"] = "E 2 2 28 28 b; A 10 2 12 28 0 360 w w2; L 2 16 30 16 w w2",
        ["kommentar"] = "R 3 4 26 18 3 y & P 8 20 15 20 8 28 y; L 8 10 24 10 br w2; L 8 15 19 15 br w2",
        ["nachricht"] = "R 3 7 26 18 2 hb; L 4 8 16 17 28 8 b w2",
        ["rollup"] = "R 5 3 22 26 2 w & R 5 3 22 6 2 b; R 5 7 22 2 w!; L 9 14 23 14 hg w2.5; L 9 19 23 19 hg w2.5; L 9 24 18 24 hg w2.5",
        ["schloss"] = "L 10 15 10 11 gr w3.5 & A 10 5 12 12 180 180 gr w3.5 & L 22 11 22 15 gr w3.5; R 6 13 20 16 2 y; E 14 17 4 4 dk!; R 15 19 2 5 dk!",

        // Navigation und Allgemeines
        ["plus"] = "E 3 3 26 26 g; L 16 9 16 23 w w3.5 h & L 9 16 23 16 w w3.5 h",
        ["minus"] = "E 3 3 26 26 r; L 9 16 23 16 w w3.5 h",
        ["vor"] = "E 3 3 26 26 b; L 13 9 20 16 w w3.5 h & L 20 16 13 23 w w3.5 h",
        ["zurueck"] = "E 3 3 26 26 b; L 19 9 12 16 w w3.5 h & L 12 16 19 23 w w3.5 h",
        ["info"] = "R 3 3 26 26 6 b; E 14 7 4 4 w!; R 14 13 4 12 w!",
        ["hilfe"] = "E 2 2 28 28 pe; A 11 7 10 10 180 270 w w3.5; L 16 17 16 19 w w3.5; E 14 21.5 4 4 w!",
        ["monitor"] = "R 13 22 6 5 gr & R 9 26 14 3 1 gr; R 2 4 28 19 2 dk; R 4 6 24 15 hb!",

        // Programmlogo im klassischen Windows-Stil: Fenster mit blauer Titelleiste und Stundenplanraster (farbige Stunden), Schlagschatten
        ["logo"] = "R 2 3 29 28 3 hg!; R 3 4 27 26 2 gr!; R 1 2 28 27 2 w; R 1 2 28 7 2 b; R 2 6 26 3 b!; R 22 4 2 2 w!; R 25 4 2 2 w!; R 4 4 9 2 hb!; R 3 11 3 16 hg!; R 7 11 21 2 hg!; R 7 14 4 4 o!; R 12 14 4 4 b!; R 17 14 4 4 hg!; R 22 14 5 4 g!; R 7 19 4 4 g!; R 12 19 4 4 y!; R 17 19 4 4 v!; R 22 19 5 4 b!; R 7 24 4 3 hb!; R 12 24 4 3 o!; R 17 24 4 3 hg!; R 22 24 5 3 hgr!",
        ["logo@16"] = "R 4 4 27 27 3 gr!; R 1 1 27 27 3 w; R 1 1 27 9 3 b; R 2 6 25 4 b!; R 4 13 6 6 o!; R 11 13 6 6 b!; R 18 13 7 6 g!; R 4 20 6 5 g!; R 11 20 6 5 y!; R 18 20 7 5 hb!",
    };

    /// <summary>Alle definierten Symbolnamen (ohne Kleinvarianten).</summary>
    public static IReadOnlyList<string> Namen => Definitionen.Keys.Where(k => !k.Contains('@')).ToList();

    /// <summary>
    /// Programmsymbole aus dem FatCow-Satz „Farm-Fresh Web Icons“ (CC BY 3.0, Hinweis in Lizenzen/FatCow-Symbole.txt):
    /// Programmname → Dateiname. Nicht zugeordnete Namen (und das Programmlogo) werden weiterhin gezeichnet.
    /// </summary>
    private static readonly Dictionary<string, string> FatCow = new()
    {
        ["klassen"] = "group",
        ["lehrer"] = "user_suit",
        ["raeume"] = "door",
        ["faecher"] = "book",
        ["schueler"] = "users_men_women",
        ["benutzer"] = "user",
        ["abteilungen"] = "chart_organisation",
        ["kurs"] = "blackboard_steps",
        ["modul"] = "brick",
        ["perioden"] = "calendar_view_month",
        ["datenbank"] = "database",
        ["stundenplan"] = "calendar_view_week",
        ["uebersicht"] = "application_view_tile",
        ["planen"] = "wand",
        ["kalender"] = "calendar",
        ["zeitraster"] = "clock",
        ["zeitwunsch"] = "time",
        ["ferien"] = "sun_cloudy",
        ["veranstaltung"] = "events",
        ["klausur"] = "report",
        ["vormerkung"] = "note_pin",
        ["block"] = "bricks",
        ["unterricht"] = "education",
        ["vertretung"] = "arrow_switch",
        ["abwesenheit"] = "user_delete",
        ["absenz"] = "user_red",
        ["entfall"] = "cancel",
        ["bereitschaft"] = "bell",
        ["sondereinsatz"] = "star",
        ["pausenaufsicht"] = "eye",
        ["fixieren"] = "lock",
        ["raumzuordnen"] = "door_in",
        ["tausch"] = "arrow_refresh",
        ["gewichtung"] = "balance",
        ["optimierung"] = "lightning",
        ["einstellungen"] = "cog",
        ["diagnose"] = "stethoscope",
        ["assistent"] = "lightbulb",
        ["normalform"] = "layout",
        ["wertrechnung"] = "calculator",
        ["statistik"] = "chart_bar",
        ["neu"] = "page_white_add",
        ["oeffnen"] = "folder",
        ["speichern"] = "diskette",
        ["drucken"] = "printer",
        ["vorschau"] = "page_white_magnify",
        ["loeschen"] = "delete",
        ["kopieren"] = "page_white_copy",
        ["rueckgaengig"] = "undo",
        ["wiederholen"] = "redo",
        ["aktualisieren"] = "arrow_refresh_small",
        ["sortieren"] = "sort",
        ["filter"] = "filter",
        ["suchen"] = "find",
        ["import"] = "table_import",
        ["export"] = "table_export",
        ["html"] = "html",
        ["web"] = "world",
        ["kommentar"] = "comment",
        ["nachricht"] = "email",
        ["rollup"] = "application_cascade",
        ["schloss"] = "lock",
        ["plus"] = "add",
        ["minus"] = "bullet_toggle_minus",
        ["vor"] = "resultset_next",
        ["zurueck"] = "resultset_previous",
        ["info"] = "information",
        ["hilfe"] = "help",
        ["monitor"] = "monitor",
        ["farbe"] = "color_wheel",
    };

    /// <summary>FatCow-Symbol in der gewünschten Größe (16er bzw. 32er Vorlage, sonst hochwertig skaliert); null, wenn nicht vorhanden.</summary>
    private static Bitmap? FatCowSymbol(string name, int groesse)
    {
        for (var i = 0; i < 4 && Definitionen.TryGetValue(name, out var alias) && alias.StartsWith('='); i++) name = alias[1..];
        if (!FatCow.TryGetValue(name, out var datei)) return null;
        var basis = groesse <= 20 ? 16 : 32;
        using var strom = typeof(Symbole).Assembly.GetManifestResourceStream($"FatCow.{basis}.{datei}.png");
        if (strom is null) return null;
        using var quelle = new Bitmap(strom);
        var b = new Bitmap(groesse, groesse, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.InterpolationMode = groesse == basis ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(quelle, new Rectangle(0, 0, groesse, groesse));
        return b;
    }

    public static Bitmap Get(string name, int groesse = 16)
    {
        if (Cache.TryGetValue((name, groesse), out var b)) return b;
        if (FatCowSymbol(name, groesse) is { } fc) { Cache[(name, groesse)] = fc; return fc; }
        b = new Bitmap(groesse, groesse, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            var def = Definition(name, groesse);
            if (def is null) Ersatz(g, name, groesse);
            else new Zeichner(g, groesse).Ausfuehren(def);
        }
        Cache[(name, groesse)] = b;
        return b;
    }

    /// <summary>Löst Aliase auf und wählt bei kleinen Größen die vereinfachte Variante.</summary>
    private static string? Definition(string name, int groesse)
    {
        for (var i = 0; i < 4 && Definitionen.TryGetValue(name, out var alias) && alias.StartsWith('='); i++) name = alias[1..];
        if (groesse <= 16 && Definitionen.TryGetValue(name + "@16", out var klein)) return klein;
        return Definitionen.TryGetValue(name, out var def) && !def.StartsWith('=') ? def : null;
    }

    /// <summary>Ersatzsymbol für unbekannte Namen: neutrale Kachel mit Anfangsbuchstaben.</summary>
    private static void Ersatz(Graphics g, string name, int groesse)
    {
        new Zeichner(g, groesse).Ausfuehren("R 3 3 26 26 6 hg");
        var text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        using var f = new Font("Segoe UI", MathF.Max(6, groesse * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var br = new SolidBrush(Farbe("dk"));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(text, f, br, new RectangleF(0, 0, groesse, groesse + 1), sf);
    }

    private static Color Farbe(string schluessel) =>
        Farben.TryGetValue(schluessel, out var rgb) ? Color.FromArgb(255, Color.FromArgb(rgb)) : Color.Magenta;

    /// <summary>Dunkle Umrissfarbe passend zur Füllfarbe (55 % Richtung Anthrazit).</summary>
    private static Color Dunkel(Color c) =>
        Color.FromArgb((int)(c.R * 0.45f + 26 * 0.55f), (int)(c.G * 0.45f + 30 * 0.55f), (int)(c.B * 0.45f + 36 * 0.55f));

    /// <summary>Ein zu zeichnender Teil: Fläche (mit optionalem Umriss) oder Strich (mit optionalem Saum).</summary>
    private sealed record Teil(GraphicsPath Pfad, Color Farbe, bool Flaeche, bool Umriss, float Breite);

    /// <summary>Interpretiert die Zeichenanweisungen pixelgenau für eine Zielgröße.</summary>
    private sealed class Zeichner
    {
        private readonly Graphics _g;
        private readonly float _s;
        private readonly float _rand;

        public Zeichner(Graphics g, int groesse)
        {
            _g = g;
            _s = groesse / 32f;
            // Saum um Formen/Linien: bei kleinen Symbolen (16–20 px) nur ein halber Pixel, sonst wirken sie zu fett
            _rand = groesse <= 20 ? 0.5f : groesse <= 24 ? 0.75f : MathF.Max(1, MathF.Floor(_s + 0.5f));
        }

        public void Ausfuehren(string definition)
        {
            foreach (var gruppe in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var teile = new List<Teil>();
                foreach (var befehl in gruppe.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Befehl(befehl, teile);
                // Erst alle Umrisse/Säume der Gruppe, dann die Füllungen – so verschmelzen Teilformen zu einer Silhouette.
                foreach (var t in teile.Where(t => t.Umriss))
                {
                    using var p = new Pen(Dunkel(t.Farbe), t.Flaeche ? 2 * _rand : t.Breite + 2 * _rand) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    _g.DrawPath(p, t.Pfad);
                }
                foreach (var t in teile)
                {
                    if (t.Flaeche)
                    {
                        using var br = new SolidBrush(t.Farbe);
                        _g.FillPath(br, t.Pfad);
                    }
                    else
                    {
                        using var p = new Pen(t.Farbe, t.Breite) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                        _g.DrawPath(p, t.Pfad);
                    }
                    t.Pfad.Dispose();
                }
            }
        }

        /// <summary>Flächenkante auf ganze Pixel runden.</summary>
        private float F(float v) => MathF.Floor(v * _s + 0.5f);

        /// <summary>Strichstärke in Pixeln (mindestens 1 px, ganzzahlig – keine Haarlinien).</summary>
        // Linienstärke: bei 16 px etwas schlanker und in halben Pixeln, damit kleine Symbole nicht klobig wirken
        private float Lb(float breite) => _s <= 0.625f ? MathF.Max(1, MathF.Round(breite * _s * 0.8f * 2) / 2) : MathF.Max(1, MathF.Floor(breite * _s + 0.5f));

        /// <summary>Linienkoordinate so runden, dass der Strich genau Pixel deckt.</summary>
        private static float L(float v, float s, float lb) => (int)lb % 2 == 1 ? MathF.Floor(v * s) + 0.5f : MathF.Floor(v * s + 0.5f);

        private void Befehl(string befehl, List<Teil> teile)
        {
            var tokens = befehl.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var z = new List<float>();
            var farbe = Color.Magenta;
            var umriss = true;
            var saum = false;
            var breite = 2f;
            foreach (var t in tokens.Skip(1))
            {
                if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) z.Add(v);
                else if (t == "!") umriss = false;
                else if (t == "h") saum = true;
                else if (t.Length > 1 && t[0] == 'w' && (char.IsDigit(t[1]) || t[1] == '.')) breite = float.Parse(t[1..], CultureInfo.InvariantCulture);
                else
                {
                    // „hb!“ = Farbe ohne Umriss
                    if (t.EndsWith('!')) umriss = false;
                    farbe = Farbe(t.TrimEnd('!'));
                }
            }
            var lb = Lb(breite);
            switch (tokens[0])
            {
                case "R":
                    teile.Add(new Teil(Rechteck(z[0], z[1], z[2], z[3], z.Count > 4 ? z[4] : 0), farbe, true, umriss, 0));
                    break;
                case "E":
                    teile.Add(new Teil(Ellipse(z[0], z[1], z[2], z[3]), farbe, true, umriss, 0));
                    break;
                case "C":
                    {
                        var p = new GraphicsPath();
                        var (x, y, w, h) = Box(z[0], z[1], z[2], z[3]);
                        p.AddPie(x, y, w, h, z[4], z[5]);
                        teile.Add(new Teil(p, farbe, true, umriss, 0));
                        break;
                    }
                case "H":
                    {
                        float x = z[0], y = z[1], s = z[2];
                        teile.Add(new Teil(Ellipse(x + 0.29f * s, y, 0.42f * s, 0.42f * s), farbe, true, umriss, 0));
                        var p = new GraphicsPath();
                        var (bx, by, bw, bh) = Box(x, y + 0.48f * s, s, 1.04f * s);
                        p.AddPie(bx, by, bw, bh, 180, 180);
                        teile.Add(new Teil(p, farbe, true, umriss, 0));
                        break;
                    }
                case "P":
                    {
                        var p = new GraphicsPath();
                        p.AddPolygon(Punkte(z, v => F(v)));
                        teile.Add(new Teil(p, farbe, true, umriss, 0));
                        break;
                    }
                case "S":
                    {
                        var pts = new PointF[(int)z[4] * 2];
                        for (var i = 0; i < pts.Length; i++)
                        {
                            var a = -Math.PI / 2 + i * Math.PI / z[4];
                            var r = i % 2 == 0 ? z[2] : z[3];
                            pts[i] = new PointF((float)(z[0] + r * Math.Cos(a)) * _s, (float)(z[1] + r * Math.Sin(a)) * _s);
                        }
                        var p = new GraphicsPath();
                        p.AddPolygon(pts);
                        teile.Add(new Teil(p, farbe, true, umriss, 0));
                        break;
                    }
                case "Z":
                    {
                        float cx = z[0], cy = z[1], r = z[2];
                        var n = (int)z[3];
                        var ri = r * 0.74f;
                        var schritt = 2 * Math.PI / n;
                        var pts = new List<PointF>();
                        for (var i = 0; i < n; i++)
                        {
                            var a = i * schritt - Math.PI / 2;
                            foreach (var (rr, da) in new[] { (ri, -0.27), (r, -0.15), (r, 0.15), (ri, 0.27) })
                                pts.Add(new PointF((float)(cx + rr * Math.Cos(a + da * schritt)) * _s, (float)(cy + rr * Math.Sin(a + da * schritt)) * _s));
                        }
                        var p = new GraphicsPath(FillMode.Alternate);
                        p.AddPolygon(pts.ToArray());
                        var rl = r * 0.34f;
                        p.AddEllipse((cx - rl) * _s, (cy - rl) * _s, 2 * rl * _s, 2 * rl * _s);
                        teile.Add(new Teil(p, farbe, true, umriss, 0));
                        break;
                    }
                case "L":
                    {
                        var p = new GraphicsPath();
                        p.AddLines(Punkte(z, v => L(v, _s, lb)));
                        teile.Add(new Teil(p, farbe, false, saum, lb));
                        break;
                    }
                case "A":
                case "B":
                    {
                        float x = L(z[0], _s, lb), y = L(z[1], _s, lb), w = L(z[0] + z[2], _s, lb) - x, h = L(z[1] + z[3], _s, lb) - y;
                        var p = new GraphicsPath();
                        p.AddArc(x, y, w, h, z[4], z[5]);
                        teile.Add(new Teil(p, farbe, false, saum, lb));
                        if (tokens[0] == "B")
                        {
                            var e = (z[4] + z[5]) * Math.PI / 180;
                            var ende = new PointF(x + w / 2 + (float)(w / 2 * Math.Cos(e)), y + h / 2 + (float)(h / 2 * Math.Sin(e)));
                            var d = new PointF((float)(-w / 2 * Math.Sin(e)), (float)(h / 2 * Math.Cos(e)));
                            if (z[5] < 0) d = new PointF(-d.X, -d.Y);
                            teile.Add(Spitze(ende, d, lb, farbe, saum, false));
                        }
                        break;
                    }
                case "V":
                    {
                        var pts = Punkte(z, v => L(v, _s, lb));
                        var spitze = pts[^1];
                        var vor = pts[^2];
                        var d = new PointF(spitze.X - vor.X, spitze.Y - vor.Y);
                        var teil = Spitze(spitze, d, lb, farbe, saum, true);
                        pts[^1] = Basis(spitze, d, lb);
                        var p = new GraphicsPath();
                        p.AddLines(pts);
                        teile.Add(new Teil(p, farbe, false, saum, lb));
                        teile.Add(teil);
                        break;
                    }
            }
        }

        private static (float l, float b) Kopf(float lb) => (MathF.Max(lb * 2.2f, 3.5f), MathF.Max(lb * 1.6f, 2.5f));

        private static PointF Einheit(PointF d)
        {
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            return len < 1e-4f ? new PointF(1, 0) : new PointF(d.X / len, d.Y / len);
        }

        private static PointF Basis(PointF spitze, PointF d, float lb)
        {
            var u = Einheit(d);
            var (kl, _) = Kopf(lb);
            return new PointF(spitze.X - u.X * kl, spitze.Y - u.Y * kl);
        }

        /// <summary>Pfeilspitze; bei <paramref name="anSpitze"/> liegt <paramref name="p"/> auf der Spitze, sonst auf der Basis.</summary>
        private static Teil Spitze(PointF p, PointF d, float lb, Color farbe, bool saum, bool anSpitze)
        {
            var u = Einheit(d);
            var (kl, kb) = Kopf(lb);
            var basis = anSpitze ? new PointF(p.X - u.X * kl, p.Y - u.Y * kl) : p;
            var spitze = anSpitze ? p : new PointF(p.X + u.X * kl, p.Y + u.Y * kl);
            var n = new PointF(-u.Y, u.X);
            var pfad = new GraphicsPath();
            pfad.AddPolygon(new[] { spitze, new PointF(basis.X + n.X * kb, basis.Y + n.Y * kb), new PointF(basis.X - n.X * kb, basis.Y - n.Y * kb) });
            return new Teil(pfad, farbe, true, saum, 0);
        }

        private static PointF[] Punkte(List<float> z, Func<float, float> runden)
        {
            var pts = new PointF[z.Count / 2];
            for (var i = 0; i < pts.Length; i++) pts[i] = new PointF(runden(z[2 * i]), runden(z[2 * i + 1]));
            return pts;
        }

        private (float x, float y, float w, float h) Box(float x, float y, float w, float h)
        {
            var x0 = F(x);
            var y0 = F(y);
            return (x0, y0, MathF.Max(1, F(x + w) - x0), MathF.Max(1, F(y + h) - y0));
        }

        private GraphicsPath Ellipse(float x, float y, float w, float h)
        {
            var (x0, y0, b, hh) = Box(x, y, w, h);
            var p = new GraphicsPath();
            p.AddEllipse(x0, y0, b, hh);
            return p;
        }

        private GraphicsPath Rechteck(float x, float y, float w, float h, float r)
        {
            var (x0, y0, b, hh) = Box(x, y, w, h);
            var p = new GraphicsPath();
            var d = MathF.Min(2 * r * _s, MathF.Min(b, hh));
            if (d < 0.5f)
            {
                p.AddRectangle(new RectangleF(x0, y0, b, hh));
                return p;
            }
            p.AddArc(x0, y0, d, d, 180, 90);
            p.AddArc(x0 + b - d, y0, d, d, 270, 90);
            p.AddArc(x0 + b - d, y0 + hh - d, d, d, 0, 90);
            p.AddArc(x0, y0 + hh - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
