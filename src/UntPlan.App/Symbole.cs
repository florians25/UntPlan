using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UntPlan.App;

/// <summary>
/// Alle Programmsymbole stammen aus dem FatCow-Satz „Farm-Fresh Web Icons“ (FatCow Web Hosting, CC BY 3.0 US,
/// Namensnennung in Lizenzen/FatCow-Symbole.txt). Es gibt keine selbst gezeichneten Symbole: Jeder Programmname
/// verweist auf eine Originaldatei in Bilder/FatCow/16 bzw. Bilder/FatCow/32 (unverändert eingebettet).
/// 16er-Symbole verwenden die 16er-Vorlage, größere die 32er-Vorlage; andere Größen werden daraus hochwertig skaliert.
/// Einzige Ausnahme ist das Programmlogo („logo“), das aus build/untplan-logo-256.png geladen wird.
/// </summary>
public static class Symbole
{
    private static readonly Dictionary<(string, int), Bitmap> Cache = new();
    private static readonly HashSet<string> FehlendeNamen = new();

    /// <summary>Programmname → FatCow-Dateiname (ohne „.png“). Bedeutung vor Aussehen.</summary>
    private static readonly Dictionary<string, string> FatCow = new()
    {
        // Stammdaten
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

        // Pläne und Kalender
        ["stundenplan"] = "calendar_view_week",
        ["uebersicht"] = "application_view_tile",
        ["planen"] = "wand",
        ["kalender"] = "calendar",
        ["datum"] = "date",
        ["zeitraster"] = "clock",
        ["zeitwunsch"] = "time",
        ["ferien"] = "sun_cloudy",
        ["veranstaltung"] = "events",
        ["klausur"] = "report",
        ["vormerkung"] = "note_pin",
        ["block"] = "bricks",

        // Unterricht und Vertretung
        ["unterricht"] = "education",
        ["vertretung"] = "arrow_switch",
        ["abwesenheit"] = "user_delete",
        ["absenz"] = "user_red",
        ["entfall"] = "cross",
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
        ["normalform"] = "transform_move",
        ["wertrechnung"] = "calculator",
        ["statistik"] = "chart_bar",

        // Symbolleisten der Vertretungs- und Absenzfenster in der Reihenfolge des Originals (möglichst ähnliche Motive)
        ["l_normalform"] = "transform_move",
        ["l_felder"] = "form",
        ["l_loeschen"] = "cross",
        ["l_vorschlag"] = "user_student",
        ["l_vormerkung"] = "note_go",
        ["l_planung"] = "calendar_edit",
        ["l_automatik"] = "calculator",
        ["l_teilen"] = "arrow_divide",
        ["l_sortieren"] = "sort_asc_az",
        ["l_inhalt"] = "data_field",
        ["l_farbig"] = "palette",
        ["l_seitenlayout"] = "page_white_gear",
        ["l_aktualisieren"] = "arrow_refresh",
        ["l_kalender"] = "calendar_view_month",

        // Datei und Bearbeiten
        ["neu"] = "page_white_add",
        ["oeffnen"] = "folder",
        ["speichern"] = "diskette",
        ["drucken"] = "printer",
        ["vorschau"] = "page_white_magnify",
        ["loeschen"] = "delete",
        ["kopieren"] = "page_white_copy",
        ["bearbeiten"] = "pencil",
        ["rueckgaengig"] = "undo",
        ["wiederholen"] = "redo",
        ["aktualisieren"] = "arrow_refresh_small",
        ["sortieren"] = "sort_asc_az",
        ["filter"] = "filter",
        ["suchen"] = "find",
        ["import"] = "table_import",
        ["export"] = "table_export",
        ["html"] = "html",
        ["pdf"] = "page_white_acrobat",
        ["web"] = "world",
        ["kommentar"] = "comment",
        ["nachricht"] = "email",
        ["rollup"] = "application_cascade",
        ["anordnung"] = "application_cascade",
        ["nebeneinander"] = "application_tile_horizontal",
        ["schloss"] = "lock",
        ["verknuepft"] = "link",
        ["nicht_verknuepft"] = "link_break",
        ["farbe"] = "color_wheel",
        ["bild"] = "picture",

        // Text und Seitenlayout
        ["fett"] = "text_bold",
        ["kursiv"] = "text_italic",
        ["unterstrichen"] = "text_underline",
        ["schriftfarbe"] = "font_colors",
        ["umbruch"] = "word_wrap",
        ["langname"] = "text_width",
        ["schnitt_waagrecht"] = "text_allcaps",
        ["schnitt_schraeg"] = "text_rotate",
        ["schnitt_senkrecht"] = "transform_rotate_90",
        ["hochformat"] = "page_white",
        ["querformat"] = "page_white_horizontal",
        ["linksbuendig"] = "text_align_left",
        ["zentriert"] = "text_align_center",
        ["rechtsbuendig"] = "text_align_right",

        // Navigation und Allgemeines
        ["plus"] = "add",
        ["minus"] = "bullet_toggle_minus",
        ["vor"] = "resultset_next",
        ["zurueck"] = "resultset_previous",
        ["erste"] = "resultset_first",
        ["letzte"] = "resultset_last",
        ["hoch"] = "arrow_up",
        ["runter"] = "arrow_down",
        ["pfeil_links"] = "arrow_left",
        ["info"] = "information",
        ["hilfe"] = "help",
        ["frage"] = "help",
        ["warnung"] = "error",
        ["fehler"] = "cancel",
        ["monitor"] = "monitor",
    };

    /// <summary>Alle Programmnamen der Symbole (Selbsttest-Übersicht).</summary>
    public static IReadOnlyList<string> Namen => FatCow.Keys.ToList();

    /// <summary>Alle verwendeten FatCow-Dateinamen (Selbsttest: jede Datei muss in 16 und 32 px eingebettet sein).</summary>
    public static IReadOnlyCollection<string> Dateien => FatCow.Values.Distinct().ToList();

    /// <summary>Angeforderte Namen ohne FatCow-Zuordnung (Selbsttest: muss leer bleiben).</summary>
    public static IReadOnlyCollection<string> Fehlend { get { lock (FehlendeNamen) return FehlendeNamen.ToList(); } }

    /// <summary>FatCow-Datei zu einem Programmnamen (null, wenn nicht zugeordnet).</summary>
    public static string? Datei(string name) => FatCow.GetValueOrDefault(name);

    /// <summary>Symbol in der gewünschten Größe (16er bzw. 32er FatCow-Vorlage, andere Größen hochwertig skaliert).</summary>
    public static Bitmap Get(string name, int groesse = 16)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((name, groesse), out var b)) return b;
            b = name == "logo" ? Skaliert(Ressource("Logo.untplan-logo-256.png"), groesse, 256)
                : FatCow.TryGetValue(name, out var datei) ? FatCowBild(datei, groesse) : null;
            if (b is null)
            {
                lock (FehlendeNamen) FehlendeNamen.Add(name);
                b = new Bitmap(groesse, groesse, PixelFormat.Format32bppArgb);
            }
            Cache[(name, groesse)] = b;
            return b;
        }
    }

    private static Bitmap? FatCowBild(string datei, int groesse)
    {
        var basis = groesse <= 20 ? 16 : 32;
        return Skaliert(Ressource($"FatCow.{basis}.{datei}.png"), groesse, basis);
    }

    private static Bitmap? Ressource(string name)
    {
        using var strom = typeof(Symbole).Assembly.GetManifestResourceStream(name);
        return strom is null ? null : new Bitmap(strom);
    }

    /// <summary>Vorlage auf die Zielgröße bringen (bei Originalgröße pixelgenau, sonst bikubisch).</summary>
    private static Bitmap? Skaliert(Bitmap? quelle, int groesse, int basis)
    {
        if (quelle is null) return null;
        using (quelle)
        {
            var b = new Bitmap(groesse, groesse, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(b);
            g.InterpolationMode = groesse == basis ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(quelle, new Rectangle(0, 0, groesse, groesse));
            return b;
        }
    }

    /*
     * Programmlogo (kein Programmsymbol): build/logo.py erzeugt aus einer einzigen Geometrie build/untplan.ico
     * (16, 20, 24, 32, 40, 48, 64 und 256 px), build/untplan-logo-256.png, die SVG-Fassungen sowie die Symbole für
     * UntPlanWeb und UntPlanMobile. Bildmarke „UP“: ein U, dessen rechter Schaft in die Schüssel eines P übergeht (9° nach vorn geneigt); die
     * Punze des P ist ein Zeitfenster. Programmsymbol ab 32 px als Kachel (Petrol, Marke Weiß, Zeitfenster Terrakotta).
     * Die Anwendung selbst zeichnet nichts,
     * sie lädt das fertige Logo (eingebettete Ressource „Logo.untplan-logo-256.png“).
     */
}
