using UntPlan.App.Dialoge;
using UntPlan.App.Fenster;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App;

/// <summary>Hauptfenster: Menüband (Ribbon), Schnellzugriff, MDI-Arbeitsbereich, Eingabeassistent, Statuszeile.</summary>
public sealed partial class HauptFenster : Form
{
    private readonly Ribbon _ribbon = new();
    private readonly ToolStrip _schnell = new();
    private readonly ToolStripLabel _titelLabel = new() { Alignment = ToolStripItemAlignment.Right, ForeColor = Color.White, Font = Ui.SchriftFett, Margin = new Padding(0, 1, 12, 2) };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLinks = new("Mit F1 erhalten Sie Hilfe.");
    private readonly ToolStripStatusLabel _statusMitte = new() { Spring = true };
    private readonly ToolStripStatusLabel _statusRechts = new();
    private readonly Eingabeassistent _assistent;
    private readonly Splitter _assistentSplitter = new() { Dock = DockStyle.Right, Width = 4 };

    public HauptFenster(string? datei)
    {
        AppZustand.Haupt = this;
        Text = "UntPlan";
        Font = Ui.Schrift;
        IsMdiContainer = true;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 640);
        Size = new Size(1400, 900);
        KeyPreview = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _schnell.GripStyle = ToolStripGripStyle.Hidden;
        _schnell.Renderer = new LeistenRenderer();
        _schnell.BackColor = Ui.Titel;
        _schnell.Padding = new Padding(4, 2, 4, 2);
        _schnell.ImageScalingSize = new Size(16, 16);
        _schnell.Items.AddRange(new ToolStripItem[]
        {
            SKnopf("Neu", "neu", Neu), SKnopf("Öffnen", "oeffnen", Oeffnen), SKnopf("Speichern (Strg+S)", "speichern", () => Speichern()),
            new ToolStripSeparator(), SKnopf("Rückgängig (Strg+Z)", "rueckgaengig", () => AppZustand.Doc.Rueckgaengig()),
            SKnopf("Wiederholen (Strg+Y)", "wiederholen", () => AppZustand.Doc.Wiederholen()), new ToolStripSeparator(),
            SKnopf("Drucken", "drucken", () => Drucken(false)), SKnopf("Seitenansicht", "vorschau", () => Drucken(true)),
            SKnopf("Element-Rollup", "rollup", () => ElementRollup.Zeigen()), new ToolStripSeparator(),
            SKnopf("Stunden- / Vertretungsplanung umschalten (Strg+Umschalt+V)", "vertretung", VertretungsmodusUmschalten),
            _titelLabel,
        });
        _status.Items.AddRange(new ToolStripItem[] { _statusLinks, _statusMitte, _statusRechts });
        _status.Font = Ui.Schrift;

        _assistent = new Eingabeassistent { Dock = DockStyle.Right, Width = 230, Visible = AppZustand.Einstellungen.AssistentZeigen };
        _assistentSplitter.Visible = _assistent.Visible;

        Controls.Add(_assistentSplitter);
        Controls.Add(_assistent);
        Controls.Add(_ribbon);
        Controls.Add(_schnell);
        Controls.Add(_status);
        foreach (Control c in Controls)
            if (c is MdiClient mdi) mdi.BackColor = Ui.MdiHintergrund;

        RibbonAufbauen();
        ModusVorbereiten();
        AppZustand.DokumentGewechselt += DokumentAnmelden;
        DokumentAnmelden();

        MultiUser.MultiUserSitzung.ZustandGeaendert += () => { if (!IsDisposed) BeginInvoke(() => DokGeaendert(AenderungsArt.Einstellungen)); };
        Shown += (_, _) =>
        {
            if (Program.Argumente.Any(a => a.Contains('~')) && MultiUser.MultiUserMenue.Befehlszeile(this, Program.Argumente)) return;
            if (datei != null) DateiLaden(datei);
            else if (AppZustand.Einstellungen.BegruessungZeigen) BegruessungDialog.Zeigen(this);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.TitelleisteFaerben(this);
    }

    private static ToolStripButton SKnopf(string text, string symbol, Action a)
    {
        var b = new ToolStripButton(Symbole.Get(symbol, 16)) { ToolTipText = text, DisplayStyle = ToolStripItemDisplayStyle.Image };
        b.Click += (_, _) => a();
        return b;
    }

    private Dokument? _angemeldet;

    private void DokumentAnmelden()
    {
        if (_angemeldet != null) _angemeldet.Geaendert -= DokGeaendert;
        _angemeldet = AppZustand.Doc;
        _angemeldet.Geaendert += DokGeaendert;
        DokGeaendert(AenderungsArt.Alles);
    }

    private void DokGeaendert(AenderungsArt art)
    {
        var d = AppZustand.D;
        var name = AppZustand.Doc.Pfad is null ? "Unbenannt" : Path.GetFileName(AppZustand.Doc.Pfad);
        if (MultiUser.MultiUserSitzung.Aktiv) name = "MultiUser " + MultiUser.MultiUserSitzung.Titel;
        _titelLabel.Text = $"{(AppZustand.Vertretungsmodus ? "Vertretungsplanung" : "Stundenplanung")}  ·  {d.Einstellungen.Schulname}  ·  {d.AktivePeriode.Name}";
        Text = $"UntPlan – {name}{(AppZustand.Doc.IstGeaendert && !MultiUser.MultiUserSitzung.Aktiv ? " *" : "")} – {d.Einstellungen.Schulname} – Stundenplan {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrBis.Year % 100:00}";
        _statusMitte.Text = $"Wertrechnung: {(d.Einstellungen.TagesgenaueWertrechnung ? "tagesgenau" : "alle Wochen zählen")}";
        _statusRechts.Text = $"{d.AktivePeriode.Name} · {d.Klassen.Count} Klassen · {d.Lehrer.Count} Lehrer · {d.Unterrichte.Count} Unterrichte";
        _ribbon.Invalidate();
        _assistent.Aktualisieren();
    }

    // ---------------------------------------------------------------------------------------------
    // Menüband
    // ---------------------------------------------------------------------------------------------

    private static RibbonEintrag E(string text, string symbol, Action? klick, bool gross = false, List<RibbonEintrag?>? menue = null, string hinweis = "", Keys taste = Keys.None) =>
        new() { Text = text, Symbol = symbol, Klick = klick, Gross = gross, Menue = menue, Hinweis = hinweis, Tastenkuerzel = taste };

    private static List<RibbonEintrag?> ElementMenue(ElementTyp typ) => typ switch
    {
        ElementTyp.Klasse => new()
        {
            E("Stammdaten", "klassen", () => Oeffnen<StammdatenFenster>("st:Klasse", () => new StammdatenFenster(ElementTyp.Klasse))),
            E("Unterricht", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse))),
            E("Alle Unterrichte", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Alle", () => new UnterrichtFenster(ElementTyp.Unterricht))),
            null,
            E("Klassenplan", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false)),
            E("Klassenplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, true)),
            E("Klassen Übersicht quer", "uebersicht", () => new UebersichtFenster(ElementTyp.Klasse).Show()),
            null,
            E("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Klasse, null)),
        },
        ElementTyp.Lehrer => new()
        {
            E("Stammdaten", "lehrer", () => Oeffnen<StammdatenFenster>("st:Lehrer", () => new StammdatenFenster(ElementTyp.Lehrer))),
            E("Unterricht", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Lehrer", () => new UnterrichtFenster(ElementTyp.Lehrer))),
            null,
            E("Lehrerplan", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false)),
            E("Lehrerplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, true)),
            E("Lehrer Übersicht quer", "uebersicht", () => new UebersichtFenster(ElementTyp.Lehrer).Show()),
            null,
            E("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Lehrer, null)),
        },
        ElementTyp.Raum => new()
        {
            E("Stammdaten", "raeume", () => Oeffnen<StammdatenFenster>("st:Raum", () => new StammdatenFenster(ElementTyp.Raum))),
            null,
            E("Raumplan", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Raum, null, false)),
            E("Raumplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Raum, null, true)),
            E("Räume Übersicht quer", "uebersicht", () => new UebersichtFenster(ElementTyp.Raum).Show()),
            null,
            E("Raumgruppen", "raeume", () => Oeffnen<StammdatenFenster>("st:Raumgruppe", () => new StammdatenFenster(ElementTyp.Raumgruppe))),
            E("Gänge", "pausenaufsicht", () => Oeffnen<StammdatenFenster>("st:Gang", () => new StammdatenFenster(ElementTyp.Gang))),
            E("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Raum, null)),
        },
        _ => new()
        {
            E("Stammdaten", "faecher", () => Oeffnen<StammdatenFenster>("st:Fach", () => new StammdatenFenster(ElementTyp.Fach))),
            E("Unterricht", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Fach", () => new UnterrichtFenster(ElementTyp.Fach))),
            null,
            E("Fachplan", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Fach, null, false)),
            E("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Fach, null)),
        },
    };

    private void RibbonAufbauen()
    {
        _ribbon.DateiMenue.AddRange(new RibbonEintrag?[]
        {
            E("Neu…", "neu", Neu, taste: Keys.Control | Keys.N),
            E("Öffnen…", "oeffnen", Oeffnen, taste: Keys.Control | Keys.O),
            E("Zuletzt geöffnet", "oeffnen", null, menue: new List<RibbonEintrag?>()),
            null,
            E("Speichern", "speichern", () => Speichern(), taste: Keys.Control | Keys.S),
            E("Speichern unter…", "speichern", () => Speichern(true)),
            null,
            E("Import/Export", "import", null, menue: ImportExportMenue()),
            E("Web-Portal…", "web", () => new PortalDialog().ShowDialog(this)),
            E("MultiUser (Datenbank)", "datenbank", null, menue: MultiUserMenue()),
            E("UntPlanWeb", "web", null, menue: new()
            {
                E("An UntPlanWeb übertragen", "web", UntPlanWebUebertragen),
                E("Rückmeldungen abholen und übernehmen", "import", UntPlanWebRueckmeldungen),
                E("Paket als Datei exportieren (JSON)…", "export", UntPlanWebDatei),
                E("Verbindung einstellen…", "einstellungen", () => new PortalDialog().ShowDialog(this)),
            }),
            null,
            E("Drucken…", "drucken", () => Drucken(false), taste: Keys.Control | Keys.P),
            E("Seitenansicht…", "vorschau", () => Drucken(true)),
            null,
            E("Beispielschule öffnen", "klassen", BeispielOeffnen),
            E("Beenden", "loeschen", Close),
        });
        ZuletztAktualisieren();

        var start = _ribbon.Seite("Start");
        var g = Gruppe(start, "Übersicht");
        g.Eintraege.Add(E("Klassen", "klassen", null, true, ElementMenue(ElementTyp.Klasse)));
        g.Eintraege.Add(E("Lehrer", "lehrer", null, true, ElementMenue(ElementTyp.Lehrer)));
        g.Eintraege.Add(E("Räume", "raeume", null, true, ElementMenue(ElementTyp.Raum)));
        g.Eintraege.Add(E("Fächer", "faecher", null, true, ElementMenue(ElementTyp.Fach)));
        g = Gruppe(start, "Planungswerkzeuge");
        g.Eintraege.Add(E("Diagnose", "diagnose", () => Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster()), menue: new()
        {
            E("Stundenplan-Diagnose", "diagnose", () => Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster())),
            E("Kommentar-Übersicht", "kommentar", () => Oeffnen<KommentarUebersicht>("komm", () => new KommentarUebersicht())),
        }));
        g.Eintraege.Add(E("Gewichtung", "gewichtung", () => new GewichtungDialog().ShowDialog(this)));
        g.Eintraege.Add(E("Optimierung", "optimierung", () => OptimierungDialog.Starten(this)));
        g.Eintraege.Add(E("Planen", "planen", null, true, new()
        {
            E("Stunden mit der Hand verplanen", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false)),
            E("Alle Stunden entplanen", "loeschen", AlleEntplanen),
            E("Alle Stunden fixieren", "fixieren", () => AlleFixieren(true)),
            E("Alle Fixierungen aufheben", "fixieren", () => AlleFixieren(false)),
            null,
            E("Tauschvorschläge", "tausch", () => Ui.Info("Tauschvorschläge erhalten Sie im Stundenplanfenster einer Klasse über die Schaltfläche <Tauschvorschläge>.")),
        }));
        g.Eintraege.Add(E("Perioden", "perioden", () => new PeriodenDialog().ShowDialog(this), true));
        g = Gruppe(start, "Schule");
        g.Eintraege.Add(E("Abteilungen", "abteilungen", () => Oeffnen<StammdatenFenster>("st:Abteilung", () => new StammdatenFenster(ElementTyp.Abteilung)), true));
        g.Eintraege.Add(E("Einstellungen", "einstellungen", null, true, new()
        {
            E("Einstellungen", "einstellungen", () => new EinstellungenDialog().ShowDialog(this)),
            E("Zeitraster", "zeitraster", () => new ZeitrasterDialog().ShowDialog(this)),
            E("Ferien", "ferien", () => Oeffnen<FerienFenster>("ferien", () => new FerienFenster())),
            null,
            E("Eingabeassistent ein/aus", "assistent", AssistentUmschalten),
            E("Begrüßungsbildschirm", "info", () => BegruessungDialog.Zeigen(this)),
        }));
        g.Eintraege.Add(E("Hilfethemen", "hilfe", null, true, new()
        {
            E("Hilfe (F1)", "hilfe", () => HilfeFenster.Zeigen("")),
            E("Handbuch: Erste Schritte", "hilfe", () => HilfeFenster.Zeigen("Eingabeassistent")),
            null,
            E("Über UntPlan", "info", () => Ui.Info("UntPlan 1.0\nStundenplan- und Vertretungsplanung für Schulen in Nordrhein-Westfalen.\n\nAlle Daten werden lokal in der Schuldatei (*.up) gespeichert.", "Über UntPlan")),
        }));
        g.Eintraege.Add(E("MultiUser", "datenbank", null, true, MultiUserMenue(), hinweis: "Gemeinsame Datenbank für mehrere Benutzer (SQLite, SQL Server, MySQL, Access)"));
        g = Gruppe(start, "Module");
        g.Eintraege.Add(new RibbonEintrag { Text = "Vertretungsplanung", Symbol = "vertretung", Klick = VertretungsmodusUmschalten, Gross = true, Gedrueckt = () => AppZustand.Vertretungsmodus, Hinweis = "Vertretungsmodus ein/aus (Strg+Umschalt+V): eigene Fenster, grüne Oberfläche", Tastenkuerzel = Keys.Control | Keys.Shift | Keys.V });
        g.Eintraege.Add(E("Info-Stundenplan", "html", InfoStundenplan));
        g.Eintraege.Add(E("Pausenaufsichten", "pausenaufsicht", () => Oeffnen<PausenaufsichtFenster>("pause", () => new PausenaufsichtFenster())));
        g.Eintraege.Add(E("Kalender / Ferien", "kalender", () => Oeffnen<FerienFenster>("ferien", () => new FerienFenster())));
        g.Eintraege.Add(E("Unterrichtsplanung", "wertrechnung", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster())));
        g.Eintraege.Add(E("Kursplanung", "kurs", () => Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster())));
        g.Eintraege.Add(E("Web-Portal", "web", () => new PortalDialog().ShowDialog(this)));
        g = Gruppe(start, "Werkzeuge");
        g.Eintraege.Add(E("Berichte", "drucken", null, menue: new()
        {
            E("Stundenpläne drucken…", "drucken", () => DruckauswahlDialog.Stundenplaene(this, ElementTyp.Klasse)),
            E("Lehrerpläne drucken…", "drucken", () => DruckauswahlDialog.Stundenplaene(this, ElementTyp.Lehrer)),
            E("Raumpläne drucken…", "drucken", () => DruckauswahlDialog.Stundenplaene(this, ElementTyp.Raum)),
            E("Vertretungsplan (HTML)…", "vertretung", () => VertretungenFenster.HtmlAusgabe(AppZustand.Datum, 2, false)),
        }));
        g.Eintraege.Add(E("Element-Rollup", "rollup", () => ElementRollup.Zeigen()));
        g.Eintraege.Add(E("Vergleichsmodus", "suchen", () => VergleichDialog.Zeigen(this)));
        g = Gruppe(start, "Fenster");
        g.Eintraege.Add(E("Standard-Fenstergruppe", "uebersicht", StandardFenster));
        g.Eintraege.Add(E("Überlappend", "uebersicht", () => LayoutMdi(MdiLayout.Cascade)));
        g.Eintraege.Add(E("Nebeneinander", "uebersicht", () => LayoutMdi(MdiLayout.TileVertical)));
        g.Eintraege.Add(E("Untereinander", "uebersicht", () => LayoutMdi(MdiLayout.TileHorizontal)));
        g.Eintraege.Add(E("Alle schließen", "loeschen", AlleSchliessen));

        var eingabe = _ribbon.Seite("Dateneingabe");
        g = Gruppe(eingabe, "Stammdaten");
        g.Eintraege.Add(E("Klassen", "klassen", () => Oeffnen<StammdatenFenster>("st:Klasse", () => new StammdatenFenster(ElementTyp.Klasse)), true));
        g.Eintraege.Add(E("Lehrer", "lehrer", () => Oeffnen<StammdatenFenster>("st:Lehrer", () => new StammdatenFenster(ElementTyp.Lehrer)), true));
        g.Eintraege.Add(E("Räume", "raeume", () => Oeffnen<StammdatenFenster>("st:Raum", () => new StammdatenFenster(ElementTyp.Raum)), true));
        g.Eintraege.Add(E("Fächer", "faecher", () => Oeffnen<StammdatenFenster>("st:Fach", () => new StammdatenFenster(ElementTyp.Fach)), true));
        g = Gruppe(eingabe, "Spezielle Daten");
        g.Eintraege.Add(E("Raumgruppen", "raeume", () => Oeffnen<StammdatenFenster>("st:Raumgruppe", () => new StammdatenFenster(ElementTyp.Raumgruppe))));
        g.Eintraege.Add(E("Gänge", "pausenaufsicht", () => Oeffnen<StammdatenFenster>("st:Gang", () => new StammdatenFenster(ElementTyp.Gang))));
        g.Eintraege.Add(E("Abteilungen", "abteilungen", () => Oeffnen<StammdatenFenster>("st:Abteilung", () => new StammdatenFenster(ElementTyp.Abteilung))));
        g.Eintraege.Add(E("Abwesenheitsgründe", "abwesenheit", () => Oeffnen<StammdatenFenster>("st:Grund", () => new StammdatenFenster(ElementTyp.Abwesenheitsgrund))));
        g.Eintraege.Add(E("Schüler", "schueler", () => Oeffnen<StammdatenFenster>("st:Schueler", () => new StammdatenFenster(ElementTyp.Schueler))));
        g.Eintraege.Add(E("Unterrichtsgruppen", "perioden", () => FensterOeffnen("ugruppen")));
        g.Eintraege.Add(E("Bereitschaftsplanung", "bereitschaft", () => FensterOeffnen("bereitplan")));
        g.Eintraege.Add(E("Stundentafel", "wertrechnung", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2))));
        g = Gruppe(eingabe, "Unterricht");
        g.Eintraege.Add(E("Klassen", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)), true));
        g.Eintraege.Add(E("Lehrer", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Lehrer", () => new UnterrichtFenster(ElementTyp.Lehrer)), true));
        g.Eintraege.Add(E("Fächer", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Fach", () => new UnterrichtFenster(ElementTyp.Fach))));
        g.Eintraege.Add(E("Alle Unterrichte", "unterricht", () => Oeffnen<UnterrichtFenster>("u:Alle", () => new UnterrichtFenster(ElementTyp.Unterricht))));
        g = Gruppe(eingabe, "Zeit");
        g.Eintraege.Add(E("Zeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Lehrer, null), true));
        g.Eintraege.Add(E("Zeitraster", "zeitraster", () => new ZeitrasterDialog().ShowDialog(this)));
        g.Eintraege.Add(E("Ferien", "ferien", () => Oeffnen<FerienFenster>("ferien", () => new FerienFenster())));
        g.Eintraege.Add(E("Schuljahr", "kalender", () => new EinstellungenDialog().ShowDialog(this)));
        g = Gruppe(eingabe, "Hilfsmittel");
        g.Eintraege.Add(E("Eingabeassistent", "assistent", AssistentUmschalten, true));
        g.Eintraege.Add(E("Kommentare", "kommentar", () => Oeffnen<KommentarUebersicht>("komm", () => new KommentarUebersicht())));
        g.Eintraege.Add(E("Import/Export", "import", null, menue: ImportExportMenue()));
        g.Eintraege.Add(E("BAL-Import", "import", BalImportieren, hinweis: "Stundenplan aus einer WILLI2-BAL-Datei übernehmen"));

        var planung = _ribbon.Seite("Planung", () => !AppZustand.Vertretungsmodus);
        g = Gruppe(planung, "Manuelles Planen");
        g.Eintraege.Add(E("Planen", "planen", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false), true, hinweis: "Stundenplan öffnen und Stunden per Drag & Drop verplanen"));
        g.Eintraege.Add(E("Tauschvorschläge", "tausch", () => StundenplanFenster.AktivesOderNeu()?.TauschvorschlaegeZeigen(), true));
        g.Eintraege.Add(E("Raum zuordnen", "raumzuordnen", () => StundenplanFenster.AktivesOderNeu()?.RaumZuordnen(), true));
        g = Gruppe(planung, "Stundenpläne");
        g.Eintraege.Add(E("Klassen", "klassen", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false)));
        g.Eintraege.Add(E("Lehrer", "lehrer", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false)));
        g.Eintraege.Add(E("Räume", "raeume", () => StundenplanFenster.Neu(ElementTyp.Raum, null, false)));
        g = Gruppe(planung, "Werkzeuge");
        g.Eintraege.Add(E("Diagnose", "diagnose", () => Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster()), true));
        g.Eintraege.Add(E("Gewichtung", "gewichtung", () => new GewichtungDialog().ShowDialog(this), true));
        g.Eintraege.Add(E("Optimierung", "optimierung", () => OptimierungDialog.Starten(this), true));
        g.Eintraege.Add(E("Alle Stunden entplanen", "loeschen", AlleEntplanen));
        g.Eintraege.Add(E("Unterrichtszeitwünsche", "zeitwunsch", () => ZeitwuenscheFenster.Zeigen(ElementTyp.Unterricht, null)));
        g.Eintraege.Add(E("Perioden", "perioden", () => new PeriodenDialog().ShowDialog(this)));

        var plaene = _ribbon.Seite("Stundenpläne");
        g = Gruppe(plaene, "Einzelpläne");
        g.Eintraege.Add(E("Klassenplan", "klassen", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false), true));
        g.Eintraege.Add(E("Lehrerplan", "lehrer", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false), true));
        g.Eintraege.Add(E("Raumplan", "raeume", () => StundenplanFenster.Neu(ElementTyp.Raum, null, false), true));
        g.Eintraege.Add(E("Fachplan", "faecher", () => StundenplanFenster.Neu(ElementTyp.Fach, null, false), true));
        g = Gruppe(plaene, "Formate");
        g.Eintraege.Add(E("Klassenplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, true)));
        g.Eintraege.Add(E("Lehrerplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, true)));
        g.Eintraege.Add(E("Raumplan groß", "stundenplan", () => StundenplanFenster.Neu(ElementTyp.Raum, null, true)));
        g = Gruppe(plaene, "Übersichtspläne");
        g.Eintraege.Add(E("Klassen Übersicht", "uebersicht", () => new UebersichtFenster(ElementTyp.Klasse).Show(), true));
        g.Eintraege.Add(E("Lehrer Übersicht", "uebersicht", () => new UebersichtFenster(ElementTyp.Lehrer).Show(), true));
        g.Eintraege.Add(E("Räume Übersicht", "uebersicht", () => new UebersichtFenster(ElementTyp.Raum).Show(), true));
        g = Gruppe(plaene, "Ausgabe");
        g.Eintraege.Add(E("Drucken", "drucken", () => DruckauswahlDialog.Stundenplaene(this, ElementTyp.Klasse), true));
        g.Eintraege.Add(E("Info-Stundenplan (HTML)", "html", InfoStundenplan, true));
        g.Eintraege.Add(E("iCalendar", "kalender", ICalExport, true));

        var kurs = _ribbon.Seite("Kursplanung", () => !AppZustand.Vertretungsmodus);
        g = Gruppe(kurs, "Kursplanung");
        g.Eintraege.Add(E("Kurse & Bänder", "kurs", () => Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster()), true));
        g.Eintraege.Add(E("Schüler", "schueler", () => Oeffnen<StammdatenFenster>("st:Schueler", () => new StammdatenFenster(ElementTyp.Schueler)), true));
        g.Eintraege.Add(E("Schüler importieren", "import", ImportSchueler, true));

        VertretungsSeiteAufbauen();

        var module = _ribbon.Seite("Module", () => !AppZustand.Vertretungsmodus);
        g = Gruppe(module, "Unterrichtsplanung und Wertrechnung");
        g.Eintraege.Add(E("Lehrer Soll/Ist", "wertrechnung", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(0)), true));
        g.Eintraege.Add(E("Unterrichts-Matrix", "uebersicht", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(1)), true));
        g.Eintraege.Add(E("Stundentafel", "unterricht", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2)), true));
        g = Gruppe(module, "Weitere Module");
        g.Eintraege.Add(E("Mehrwochen / Perioden", "perioden", () => new PeriodenDialog().ShowDialog(this), true));
        g.Eintraege.Add(E("Bereitschafts-planung", "bereitschaft", () => FensterOeffnen("bereitplan"), true));
        g.Eintraege.Add(E("Unterrichts-gruppen", "perioden", () => FensterOeffnen("ugruppen"), true, hinweis: "A/B-Wochen und weitere Wochenzyklen"));
        g.Eintraege.Add(E("Abteilungsplanung", "abteilungen", null, true, new()
        {
            E("Abteilungen", "abteilungen", () => Oeffnen<StammdatenFenster>("st:Abteilung", () => new StammdatenFenster(ElementTyp.Abteilung))),
            E("Abteilungsdatei exportieren…", "export", AbteilungExportieren),
            E("Abteilungsdatei zusammenführen…", "import", AbteilungZusammenfuehren),
        }));
        g.Eintraege.Add(E("Web-Portal", "web", () => new PortalDialog().ShowDialog(this), true));
        g.Eintraege.Add(E("Modulübersicht", "modul", () => ModuleDialog.Zeigen(this), true));
    }

    private static RibbonGruppe Gruppe(RibbonSeite s, string text)
    {
        var g = new RibbonGruppe { Text = text };
        s.Gruppen.Add(g);
        return g;
    }

    private List<RibbonEintrag?> MultiUserMenue() =>
        MultiUser.MultiUserMenue.Eintraege(this).Select(x => (RibbonEintrag?)E(x.Text, x.Symbol, x.Klick)).ToList();

    private List<RibbonEintrag?> ImportExportMenue() => new()
    {
        E("Schild-NRW: Lehrer importieren (CSV)…", "import", () => CsvImport("Lehrer")),
        E("Schild-NRW: Klassen importieren (CSV)…", "import", () => CsvImport("Klassen")),
        E("Schüler importieren (CSV)…", "import", ImportSchueler),
        E("GPU-Dateien importieren (Umstieg)…", "import", GpuImport),
        E("BAL-Datei (WILLI2) importieren…", "import", BalImportieren),
        null,
        E("GPU-Dateien exportieren…", "export", GpuExport),
        E("Info-Stundenplan (HTML)…", "html", InfoStundenplan),
        E("Lehrer-Kalender (iCal)…", "kalender", ICalExport),
        E("Unterrichtsverteilung (CSV)…", "export", UnterrichtCsv),
    };

    private void ZuletztAktualisieren()
    {
        var eintrag = _ribbon.DateiMenue.FirstOrDefault(x => x?.Text == "Zuletzt geöffnet");
        if (eintrag?.Menue is null) return;
        eintrag.Menue.Clear();
        foreach (var f in AppZustand.Einstellungen.ZuletztGeoeffnet.Where(File.Exists))
        {
            var datei = f;
            eintrag.Menue.Add(E(datei, "oeffnen", () => { if (Verwerfen()) DateiLaden(datei); }));
        }
        if (eintrag.Menue.Count == 0) eintrag.Menue.Add(E("(leer)", "", null));
    }

    // ---------------------------------------------------------------------------------------------
    // Fenster
    // ---------------------------------------------------------------------------------------------

    public static T Oeffnen<T>(string schluessel, Func<T> erzeugen) where T : Form
    {
        var vorhanden = AppZustand.Haupt.MdiChildren.OfType<T>().FirstOrDefault(f => (string?)f.Tag == schluessel);
        if (vorhanden != null)
        {
            if (vorhanden.WindowState == FormWindowState.Minimized) vorhanden.WindowState = FormWindowState.Normal;
            vorhanden.Activate();
            return vorhanden;
        }
        var neu = erzeugen();
        neu.Tag = schluessel;
        neu.Show();
        return neu;
    }

    /// <summary>Anordnung wie in der Abbildung des Handbuchs: Stammdaten, Zeitwünsche, Stundenplan, Unterricht.</summary>
    public void StandardFenster()
    {
        AlleSchliessen();
        var mdi = Controls.OfType<MdiClient>().First().ClientSize;
        var w = mdi.Width;
        var h = mdi.Height;
        var links = (int)(w * 0.36);
        var mitte = (int)(w * 0.36);
        var oben = (int)(h * 0.48);
        var st = Oeffnen<StammdatenFenster>("st:Klasse", () => new StammdatenFenster(ElementTyp.Klasse));
        st.WindowState = FormWindowState.Normal;
        st.Bounds = new Rectangle(0, 0, links, oben);
        var zw = ZeitwuenscheFenster.Zeigen(ElementTyp.Klasse, AppZustand.D.Klassen.FirstOrDefault()?.Id);
        zw.Bounds = new Rectangle(links, 0, mitte, oben);
        var sp = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.FirstOrDefault()?.Id, false);
        sp.Bounds = new Rectangle(links + mitte, 0, w - links - mitte, h);
        var u = Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse));
        u.Bounds = new Rectangle(0, oben, links + mitte, h - oben);
    }

    private void AlleSchliessen()
    {
        foreach (var f in MdiChildren) f.Close();
    }

    public void AssistentUmschalten()
    {
        _assistent.Visible = !_assistent.Visible;
        _assistentSplitter.Visible = _assistent.Visible;
        AppZustand.Einstellungen.AssistentZeigen = _assistent.Visible;
        AppZustand.Einstellungen.Speichern();
    }

    // ---------------------------------------------------------------------------------------------
    // Datei
    // ---------------------------------------------------------------------------------------------

    private bool Verwerfen()
    {
        // im MultiUser-Betrieb wird jede Änderung sofort in die Datenbank geschrieben
        if (MultiUser.MultiUserSitzung.Aktiv) return true;
        if (!AppZustand.Doc.IstGeaendert) return true;
        var r = MessageBox.Show("Die Schuldatei wurde geändert. Änderungen speichern?", "UntPlan", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        return r == DialogResult.No || Speichern();
    }

    public void Neu()
    {
        if (!Verwerfen()) return;
        using var dlg = new NeueDateiDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        AlleSchliessen();
        AppZustand.Setzen(new Dokument(dlg.Ergebnis!));
        if (!_assistent.Visible) AssistentUmschalten();
        if (dlg.AssistentStarten) new EinstellungenDialog().ShowDialog(this);
    }

    public void BeispielOeffnen()
    {
        if (!Verwerfen()) return;
        AlleSchliessen();
        AppZustand.Setzen(new Dokument(Demo.Beispielschule()));
        if (AppZustand.Vertretungsmodus) VertretungsStandardFenster(); else StandardFenster();
    }

    public void Oeffnen()
    {
        if (!Verwerfen()) return;
        using var ofd = new OpenFileDialog { Filter = Dokument.DateiFilter, Title = "Schuldatei öffnen" };
        if (ofd.ShowDialog(this) == DialogResult.OK) DateiLaden(ofd.FileName);
    }

    public void DateiLaden(string pfad)
    {
        try
        {
            var d = Dokument.Laden(pfad);
            AlleSchliessen();
            AppZustand.Setzen(d);
            AppZustand.Einstellungen.Merken(pfad);
            ZuletztAktualisieren();
            if (AppZustand.Vertretungsmodus) VertretungsStandardFenster(); else StandardFenster();
        }
        catch (Exception ex)
        {
            Ui.Fehler(new InvalidOperationException($"Die Datei konnte nicht geöffnet werden:\n{ex.Message}"));
        }
    }

    public bool Speichern(bool unter = false)
    {
        if (MultiUser.MultiUserSitzung.Aktiv)
        {
            if (unter) MultiUser.MultiUserMenue.AlsDateiExportieren(this);
            else Ui.Info("Im MultiUser-Betrieb wird jede Änderung sofort in der Datenbank gespeichert.\nÜber \"Speichern unter\" können Sie die Version als Datei exportieren.", "MultiUser");
            return true;
        }
        var doc = AppZustand.Doc;
        var pfad = doc.Pfad;
        if (pfad is null || unter)
        {
            using var sfd = new SaveFileDialog { Filter = Dokument.SpeicherFilter, FileName = string.IsNullOrWhiteSpace(doc.Daten.Einstellungen.Schulname) ? "Schule" + Dokument.Dateiendung : doc.Daten.Einstellungen.Schulname + Dokument.Dateiendung };
            if (sfd.ShowDialog(this) != DialogResult.OK) return false;
            pfad = sfd.FileName;
        }
        try
        {
            doc.Speichern(pfad);
            AppZustand.Einstellungen.Merken(pfad);
            ZuletztAktualisieren();
            DokGeaendert(AenderungsArt.Einstellungen);
            return true;
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Verwerfen()) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Z when ActiveMdiChild?.ActiveControl is not TextBoxBase:
                AppZustand.Doc.Rueckgaengig();
                return true;
            case Keys.Control | Keys.Y when ActiveMdiChild?.ActiveControl is not TextBoxBase:
                AppZustand.Doc.Wiederholen();
                return true;
            case Keys.Control | Keys.T:
                VertretungsFensterOeffnen("vertr");
                return true;
            case Keys.Control | Keys.M when AppZustand.Vertretungsmodus:
                VertretungsFensterOeffnen("abw");
                return true;
            case Keys.Control | Keys.G when AppZustand.Vertretungsmodus:
                VertretungsFensterOeffnen("vorschlag");
                return true;
            case Keys.F1 when ActiveMdiChild is null:
                HilfeFenster.Zeigen("");
                return true;
        }
        if (_ribbon.TasteVerarbeiten(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Drucken(bool vorschau)
    {
        if (ActiveMdiChild is IDruckbar d) d.Drucken(vorschau);
        else DruckauswahlDialog.Stundenplaene(this, ElementTyp.Klasse);
    }

    // ---------------------------------------------------------------------------------------------
    // Planung
    // ---------------------------------------------------------------------------------------------

    private void AlleEntplanen()
    {
        if (!Ui.Frage("Alle nicht fixierten Stunden entplanen?")) return;
        AppZustand.Doc.Aendern("Alle Stunden entplanen", AenderungsArt.Stundenplan, d => Planungsdienst.AlleEntplanen(d, true));
    }

    private void AlleFixieren(bool fix) =>
        AppZustand.Doc.Aendern(fix ? "Alle Stunden fixieren" : "Fixierungen aufheben", AenderungsArt.Stundenplan, d => d.Stundenplan.ForEach(p => p.Fixiert = fix));

    // ---------------------------------------------------------------------------------------------
    // Import / Export
    // ---------------------------------------------------------------------------------------------

    private void CsvImport(string art)
    {
        using var ofd = new OpenFileDialog { Filter = "CSV/Text (*.csv;*.txt)|*.csv;*.txt|Alle Dateien|*.*", Title = $"{art} importieren (Schild-NRW)" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var text = Csv.DateiLesen(ofd.FileName);
        ImportBericht? b = null;
        AppZustand.Doc.Aendern($"Import {art}", AenderungsArt.Stammdaten, d => b = art == "Lehrer" ? Import.LehrerCsv(d, text) : Import.KlassenCsv(d, text));
        Ui.Info($"Import {art}: {b}");
    }

    private void ImportSchueler()
    {
        using var ofd = new OpenFileDialog { Filter = "CSV/Text (*.csv;*.txt)|*.csv;*.txt", Title = "Schüler mit Kurswahlen importieren" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var text = Csv.DateiLesen(ofd.FileName);
        ImportBericht? b = null;
        AppZustand.Doc.Aendern("Import Schüler", AenderungsArt.Stammdaten, d => b = Import.SchuelerCsv(d, text));
        Ui.Info($"Import Schüler: {b}");
    }

    private void GpuImport()
    {
        using var ofd = new OpenFileDialog { Filter = "GPU-Dateien (GPU*.txt)|GPU*.txt;GPU*.TXT|Alle Dateien|*.*", Multiselect = true, Title = "GPU001–GPU006 auswählen" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var dateien = ofd.FileNames.ToDictionary(f => Path.GetFileName(f), Csv.DateiLesen);
        ImportBericht? b = null;
        AppZustand.Doc.Aendern("GPU-Import", AenderungsArt.Alles, d => b = Import.Gpu(d, dateien!));
        Ui.Info($"GPU-Import: {b}");
    }

    /// <summary>BAL-Datei (WILLI2-Stundenplan) importieren: Zeitraster, Stammdaten, Unterricht mit Kopplungen, Stundenplan, Aufsichten.</summary>
    public void BalImportieren()
    {
        using var ofd = new OpenFileDialog { Filter = "BAL-Dateien (*.bal)|*.bal;*.BAL|Alle Dateien|*.*", Title = "BAL-Datei (WILLI2) importieren" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var text = BalImport.DateiLesen(ofd.FileName);
        if (!BalImport.IstBal(text) && !Ui.Frage("Die Datei sieht nicht wie eine WILLI2-BAL-Datei aus. Trotzdem importieren?")) return;
        var o = new BalOptionen();
        using (var f = new Dialog("BAL-Import", 420, 250))
        {
            var y = 14;
            var cz = Ui.Zeile(f, "", new CheckBox { Text = "Zeitraster (Tage, Stundenzeiten, Pausen)", Checked = true, AutoSize = true }, ref y, 10);
            var cs = Ui.Zeile(f, "", new CheckBox { Text = "Stammdaten (Lehrer, Klassen, Fächer, Räume, Gänge)", Checked = true, AutoSize = true }, ref y, 10);
            var cw = Ui.Zeile(f, "", new CheckBox { Text = "Verfügbarkeiten / freie Tage der Lehrer", Checked = true, AutoSize = true }, ref y, 10);
            var cu = Ui.Zeile(f, "", new CheckBox { Text = "Vorhandenen Unterricht der aktiven Periode ersetzen", Checked = true, AutoSize = true }, ref y, 10);
            var cp = Ui.Zeile(f, "", new CheckBox { Text = "Stundenplan übernehmen", Checked = true, AutoSize = true }, ref y, 10);
            var ca = Ui.Zeile(f, "", new CheckBox { Text = "Pausenaufsichten übernehmen", Checked = true, AutoSize = true }, ref y, 10);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            o = new BalOptionen { Zeitraster = cz.Checked, Stammdaten = cs.Checked, Zeitwuensche = cw.Checked, UnterrichtErsetzen = cu.Checked, Stundenplan = cp.Checked, Pausenaufsichten = ca.Checked };
        }
        ImportBericht? b = null;
        try
        {
            using (new WarteCursor())
                AppZustand.Doc.Aendern("BAL-Import", AenderungsArt.Alles, d => b = BalImport.Importieren(d, text, o));
        }
        catch (Exception ex) { Ui.Fehler(ex); return; }
        Ui.Info($"BAL-Import: {b}", "BAL-Import");
        if (AppZustand.Vertretungsmodus) VertretungsStandardFenster(); else StandardFenster();
    }

    private UntPlan.Web.Contracts.WebPaket UntPlanWebPaket() =>
        UntPlanWebExport.PaketErstellen(AppZustand.D, AppZustand.Vertretungsmodus ? AppZustand.Datum : DateOnly.FromDateTime(DateTime.Today));

    private void UntPlanWebDatei()
    {
        using var sfd = new SaveFileDialog { Filter = "UntPlanWeb-Paket (*.json)|*.json", FileName = $"untplanweb_{DateTime.Now:yyyyMMdd_HHmm}.json" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(sfd.FileName, UntPlanWebExport.Json(UntPlanWebPaket()));
        Ui.Info("Das Paket (Schema untplanweb/1) wurde gespeichert.", "UntPlanWeb");
    }

    private async void UntPlanWebUebertragen()
    {
        var ps = AppZustand.D.Einstellungen.Portal;
        if (string.IsNullOrWhiteSpace(ps.Adresse)) { Ui.Info("Bitte zuerst die Verbindung zu UntPlanWeb einstellen (Adresse, Schul-Id, API-Schlüssel).", "UntPlanWeb"); new PortalDialog().ShowDialog(this); return; }
        try
        {
            UseWaitCursor = true;
            var a = await UntPlanWebExport.HochladenAsync(ps, UntPlanWebPaket());
            AppZustand.Doc.Aendern("UntPlanWeb-Übertragung", AenderungsArt.Einstellungen, d => { d.Einstellungen.Portal.Revision = a.Revision; d.Einstellungen.Portal.LetzteSynchronisation = DateTime.Now; });
            Ui.Info($"Übertragung erfolgreich (Revision {a.Revision}).", "UntPlanWeb");
        }
        catch (Exception ex) { Ui.Fehler(new InvalidOperationException("Übertragung an UntPlanWeb fehlgeschlagen: " + ex.Message)); }
        finally { UseWaitCursor = false; }
    }

    private async void UntPlanWebRueckmeldungen()
    {
        var ps = AppZustand.D.Einstellungen.Portal;
        try
        {
            var liste = await UntPlanWebExport.RueckmeldungenAsync(ps);
            if (liste.Count == 0) { Ui.Info("Keine neuen Rückmeldungen.", "UntPlanWeb"); return; }
            List<string> ok = new(), nein = new();
            AppZustand.Doc.Aendern("UntPlanWeb-Rückmeldungen", AenderungsArt.Vertretung, d =>
            {
                (ok, nein) = UntPlanWebExport.Uebernehmen(d, liste);
                var tage = d.Abwesenheiten.Where(a => a.Text.Contains("Portal-Meldung")).SelectMany(a => Kalender.Tage(a.Von, a.Bis)).Distinct().ToList();
                foreach (var t in tage.Where(t => Kalender.IstUnterrichtstag(d, t))) Core.Vertretungsplanung.Vertretungen.Aktualisieren(d, t);
            });
            await UntPlanWebExport.QuittierenAsync(ps, ok);
            Ui.Info($"{ok.Count} Rückmeldungen übernommen{(nein.Count > 0 ? $", {nein.Count} nicht zuordenbar" : "")}.", "UntPlanWeb");
        }
        catch (Exception ex) { Ui.Fehler(new InvalidOperationException("Abruf von UntPlanWeb fehlgeschlagen: " + ex.Message)); }
    }

    private void GpuExport()
    {
        using var fbd = new FolderBrowserDialog { Description = "Zielordner für GPU-Dateien" };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;
        foreach (var (name, inhalt) in Export.Gpu(AppZustand.D)) File.WriteAllText(Path.Combine(fbd.SelectedPath, name), inhalt, System.Text.Encoding.Latin1);
        Ui.Info("GPU-Dateien wurden exportiert.");
    }

    private void InfoStundenplan()
    {
        using var fbd = new FolderBrowserDialog { Description = "Zielordner für den Info-Stundenplan (HTML)" };
        if (fbd.ShowDialog(this) != DialogResult.OK) return;
        foreach (var (name, inhalt) in Export.InfoStundenplan(AppZustand.D)) File.WriteAllText(Path.Combine(fbd.SelectedPath, name), inhalt);
        Starten(Path.Combine(fbd.SelectedPath, "index.html"));
    }

    private void ICalExport()
    {
        var l = AuswahlDialog.Element(this, ElementTyp.Lehrer, "Lehrer für den Kalenderexport wählen");
        if (l is null) return;
        using var sfd = new SaveFileDialog { Filter = "iCalendar (*.ics)|*.ics", FileName = $"Stundenplan_{l.Kurzname}.ics" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var heute = DateOnly.FromDateTime(DateTime.Today);
        File.WriteAllText(sfd.FileName, Export.ICal(AppZustand.D, l.Id, heute, heute.AddDays(27)));
    }

    private void UnterrichtCsv()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Unterrichtsverteilung.csv" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var d = AppZustand.D;
        var ix = AppZustand.Doc.Index;
        var rows = new List<object?[]> { new object?[] { "U-Nr", "Wst", "Fach", "Lehrer", "Klassen", "Fachraum", "Stammraum", "Dopp.Std.", "Block", "Band", "Kurs" } };
        foreach (var u in d.Unterrichte)
            foreach (var z in u.Zeilen)
                rows.Add(new object?[] { u.Id, u.Wochenstunden, ix.KFach(z.FachId), ix.KLehrer(z.LehrerId), ix.KKlassen(z.KlassenIds), ix.KRaum(z.FachraumId), ix.KRaum(z.StammraumId), u.Doppelstunden.ToString(), u.Block, u.Band, u.Kursname });
        File.WriteAllText(sfd.FileName, Csv.Schreiben(rows));
    }

    private void AbteilungExportieren()
    {
        var a = AuswahlDialog.Element(this, ElementTyp.Abteilung, "Abteilung wählen");
        if (a is null) return;
        using var sfd = new SaveFileDialog { Filter = Dokument.SpeicherFilter, FileName = $"Abteilung_{a.Kurzname}{Dokument.Dateiendung}" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        new Dokument(Core.Module.Abteilungen.Exportieren(AppZustand.D, a.Id)).Speichern(sfd.FileName);
        Ui.Info("Die Abteilungsdatei wurde erstellt. Unterrichte anderer Abteilungen sind darin fixiert.");
    }

    private void AbteilungZusammenfuehren()
    {
        var a = AuswahlDialog.Element(this, ElementTyp.Abteilung, "Abteilung wählen");
        if (a is null) return;
        using var ofd = new OpenFileDialog { Filter = Dokument.SpeicherFilter };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        var teil = Dokument.Laden(ofd.FileName).Daten;
        var n = 0;
        AppZustand.Doc.Aendern("Abteilung zusammenführen", AenderungsArt.Alles, d => n = Core.Module.Abteilungen.Zusammenfuehren(d, teil, a.Id));
        Ui.Info($"{n} Unterrichte der Abteilung {a.Kurzname} übernommen.");
    }

    public static void Starten(string pfad)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pfad) { UseShellExecute = true }); }
        catch (Exception ex) { Ui.Fehler(ex); }
    }
}

/// <summary>Fenster, die über "Drucken"/"Seitenansicht" ausgegeben werden können.</summary>
public interface IDruckbar
{
    void Drucken(bool vorschau);
}
