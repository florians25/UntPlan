using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UntPlan.App;

/// <summary>
/// Alle Programmsymbole stammen aus dem FatCow-Satz „Farm-Fresh Web Icons“ (FatCow Web Hosting, CC BY 3.0 US,
/// Namensnennung in Lizenzen/FatCow-Symbole.txt). Es gibt keine selbst gezeichneten Symbole: Jeder Programmname
/// verweist auf eine Originaldatei in Bilder/FatCow/16 bzw. Bilder/FatCow/32 (unverändert eingebettet).
/// Deaktivierte Zustände zeigen die offiziellen grauen FatCow-Varianten (Bilder/FatCow/16g bzw. 32g, aus „16x16-grey“/„32x32-grey“).
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
        ["kalender"] = "date",
        ["datum"] = "date",
        ["zeitraster"] = "clock",
        ["zeitwunsch"] = "time",
        ["ferien"] = "sun_cloudy",
        ["veranstaltung"] = "events",
        ["klausur"] = "report",
        ["vormerkung"] = "date_go",
        ["block"] = "bricks",

        // Unterricht und Vertretung
        ["unterricht"] = "education",
        ["vertretung"] = "report_user",
        ["abwesenheit"] = "user_delete",
        ["absenz"] = "bed",
        ["entfall"] = "cross",
        ["bereitschaft"] = "user_female",
        ["sondereinsatz"] = "table_add",
        ["pausenaufsicht"] = "wall",
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
        // Menüband „Vertretungsplanung“ wie im Vorbild (Bild 308)
        ["vertretungsmodus"] = "user_go",
        // Aufklapplisten der Tabellenzellen wie im Vorbild (Bilder 348, 351, 352): Lehrkraft, Klasse, Raum
        ["liste_lehrer"] = "user_student",
        ["liste_klasse"] = "group",
        ["liste_raum"] = "door",
        ["planungsdialog"] = "group_gear",
        ["vertretungsformate"] = "document_copies",
        ["infostundenplan"] = "building",

        // Symbolleisten der Vertretungs- und Absenzfenster in der Reihenfolge des Originals (möglichst ähnliche Motive)
        ["l_normalform"] = "transform_move",
        ["l_felder"] = "application_view_list",
        ["l_loeschen"] = "cross",
        ["l_vorschlag"] = "user_student",
        ["l_vormerkung"] = "date_go",
        ["l_planung"] = "door_in",
        ["l_automatik"] = "calculator",
        ["l_teilen"] = "cut",
        ["l_sortieren"] = "sort_asc_az",
        ["l_inhalt"] = "table_select",
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
        ["excel"] = "table_excel",
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

    /// <summary>Graue Varianten, die als Ressource fehlen (Selbsttest: muss leer bleiben).</summary>
    public static IReadOnlyCollection<string> GrauFehlend =>
        Dateien.SelectMany(d => new[] { 16, 32 }.Where(s => typeof(Symbole).Assembly.GetManifestResourceInfo($"FatCow.{s}g.{d}.png") is null).Select(s => $"{s}g/{d}")).ToList();

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

    private static Bitmap? FatCowBild(string datei, int groesse, bool grau = false)
    {
        var basis = groesse <= 20 ? 16 : 32;
        var b = Skaliert(Ressource($"FatCow.{basis}{(grau ? "g" : "")}.{datei}.png"), groesse, basis);
        if (b != null && !grau) lock (Herkunft) Herkunft.AddOrUpdate(b, datei);
        return b;
    }

    /// <summary>Herkunft jedes geladenen Symbols (Bild → FatCow-Datei), damit deaktivierte Knöpfe die graue Originalfassung finden.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, string> Herkunft = new();
    private static readonly Dictionary<(string, int), Bitmap> GrauCache = new();

    /// <summary>
    /// Graue Fassung für deaktivierte Zustände: die offizielle FatCow-Variante aus „16x16-grey“ bzw. „32x32-grey“ (unverändert eingebettet),
    /// kein berechneter Graufilter. Null, wenn es zum Namen keine FatCow-Datei gibt.
    /// </summary>
    public static Bitmap? Grau(string name, int groesse = 16) =>
        FatCow.TryGetValue(name, out var datei) ? GrauDatei(datei, groesse) : null;

    /// <summary>Graue Fassung zu einem bereits geladenen Symbolbild (z. B. Bild eines ToolStrip-Knopfs); null bei fremden Bildern.</summary>
    public static Bitmap? Grau(Image? bild)
    {
        if (bild is null) return null;
        string? datei;
        lock (Herkunft) if (!Herkunft.TryGetValue(bild, out datei)) return null;
        return GrauDatei(datei, Math.Max(bild.Width, bild.Height));
    }

    private static Bitmap? GrauDatei(string datei, int groesse)
    {
        lock (GrauCache)
        {
            if (GrauCache.TryGetValue((datei, groesse), out var b)) return b;
            b = FatCowBild(datei, groesse, true);
            if (b != null) GrauCache[(datei, groesse)] = b;
            return b;
        }
    }

    private static Icon? _logo, _logoKlein;

    /// <summary>
    /// Fenstersymbol aller Fenster und Dialoge: das UntPlan-Logo (build/untplan.ico mit allen Größen, wie das Programmsymbol).
    /// Windows wählt für Titelleiste und Taskleiste jeweils die passende Größe.
    /// </summary>
    public static Icon Logo => _logo ??= LogoLaden(0);

    /// <summary>Das Logo in 16 × 16 Pixeln für selbst gezeichnete Titelleisten (pixelgenau aus der 16er-Fassung der Symboldatei).</summary>
    public static Icon LogoKlein => _logoKlein ??= LogoLaden(16);

    private static Icon LogoLaden(int groesse)
    {
        using var strom = typeof(Symbole).Assembly.GetManifestResourceStream("Logo.untplan.ico");
        if (strom is null) return SystemIcons.Application;
        return groesse > 0 ? new Icon(strom, groesse, groesse) : new Icon(strom);
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
     * UntPlanWeb und UntPlanMobile. Bildmarke „UP“: ein U, dessen rechter Schaft in die Schüssel eines P übergeht; die
     * Punze des P ist ein Zeitfenster. Programmsymbol ab 32 px als Kachel (Orange, Marke Weiß, Zeitfenster Petrol).
     * Die Anwendung selbst zeichnet nichts,
     * sie lädt das fertige Logo (eingebettete Ressource „Logo.untplan-logo-256.png“).
     */
}
