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
        _schnell.RenderMode = ToolStripRenderMode.System;
        _schnell.BackColor = Ui.Titel;
        _schnell.Padding = new Padding(4, 1, 4, 1);
        _schnell.Items.AddRange(new ToolStripItem[]
        {
            SKnopf("Neu", "neu", Neu), SKnopf("Öffnen", "oeffnen", Oeffnen), SKnopf("Speichern (Strg+S)", "speichern", () => Speichern()),
            new ToolStripSeparator(), SKnopf("Rückgängig (Strg+Z)", "rueckgaengig", () => AppZustand.Doc.Rueckgaengig()),
            SKnopf("Wiederholen (Strg+Y)", "wiederholen", () => AppZustand.Doc.Wiederholen()), new ToolStripSeparator(),
            SKnopf("Drucken", "drucken", () => Drucken(false)), SKnopf("Seitenansicht", "vorschau", () => Drucken(true)),
            SKnopf("Element-Rollup", "rollup", () => ElementRollup.Zeigen()),
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
        AppZustand.DokumentGewechselt += DokumentAnmelden;
        DokumentAnmelden();

        Shown += (_, _) =>
        {
            if (datei != null) DateiLaden(datei);
            else if (AppZustand.Einstellungen.BegruessungZeigen) BegruessungDialog.Zeigen(this);
        };
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
        Text = $"UntPlan – {name}{(AppZustand.Doc.IstGeaendert ? " *" : "")} – {d.Einstellungen.Schulname} – Stundenplan {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrBis.Year % 100:00}";
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
            E("Über UntPlan", "info", () => Ui.Info("UntPlan 1.0\nStundenplan- und Vertretungsplanung für Schulen in Nordrhein-Westfalen.\n\nAlle Daten werden lokal in der Schuldatei (*.untplan) gespeichert.", "Über UntPlan")),
        }));
        g = Gruppe(start, "Module");
        g.Eintraege.Add(E("Vertretungsplanung", "vertretung", () => Oeffnen<VertretungenFenster>("vertr", () => new VertretungenFenster()), true));
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
            E("Vertretungsplan…", "vertretung", () => Oeffnen<VertretungenFenster>("vertr", () => new VertretungenFenster())),
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

        var planung = _ribbon.Seite("Planung");
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

        var kurs = _ribbon.Seite("Kursplanung");
        g = Gruppe(kurs, "Kursplanung");
        g.Eintraege.Add(E("Kurse & Bänder", "kurs", () => Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster()), true));
        g.Eintraege.Add(E("Schüler", "schueler", () => Oeffnen<StammdatenFenster>("st:Schueler", () => new StammdatenFenster(ElementTyp.Schueler)), true));
        g.Eintraege.Add(E("Schüler importieren", "import", ImportSchueler, true));

        var vp = _ribbon.Seite("Vertretungsplanung");
        g = Gruppe(vp, "Vertretungsplanung");
        g.Eintraege.Add(E("Vertretungen", "vertretung", () => Oeffnen<VertretungenFenster>("vertr", () => new VertretungenFenster()), true));
        g.Eintraege.Add(E("Abwesenheiten", "abwesenheit", () => Oeffnen<AbwesenheitenFenster>("abw", () => new AbwesenheitenFenster()), true));
        g.Eintraege.Add(E("Pausenaufsichten", "pausenaufsicht", () => Oeffnen<PausenaufsichtFenster>("pause", () => new PausenaufsichtFenster()), true));
        g = Gruppe(vp, "Ausgabe");
        g.Eintraege.Add(E("Vertretungsplan (HTML)", "html", () => VertretungenFenster.HtmlAusgabe(DateOnly.FromDateTime(DateTime.Today), 2, false), true));
        g.Eintraege.Add(E("Monitor", "monitor", () => new MonitorFenster().Show(), true));
        g.Eintraege.Add(E("Statistik", "statistik", () => Oeffnen<StatistikFenster>("stat", () => new StatistikFenster()), true));
        g = Gruppe(vp, "Stammdaten");
        g.Eintraege.Add(E("Abwesenheitsgründe", "abwesenheit", () => Oeffnen<StammdatenFenster>("st:Grund", () => new StammdatenFenster(ElementTyp.Abwesenheitsgrund))));
        g.Eintraege.Add(E("Gänge", "pausenaufsicht", () => Oeffnen<StammdatenFenster>("st:Gang", () => new StammdatenFenster(ElementTyp.Gang))));
        g.Eintraege.Add(E("Vertretungsgewichtung", "gewichtung", () => new EinstellungenDialog("Vertretungsplanung").ShowDialog(this)));

        var module = _ribbon.Seite("Module");
        g = Gruppe(module, "Unterrichtsplanung und Wertrechnung");
        g.Eintraege.Add(E("Lehrer Soll/Ist", "wertrechnung", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(0)), true));
        g.Eintraege.Add(E("Unterrichts-Matrix", "uebersicht", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(1)), true));
        g.Eintraege.Add(E("Stundentafel", "unterricht", () => Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2)), true));
        g = Gruppe(module, "Weitere Module");
        g.Eintraege.Add(E("Mehrwochen / Perioden", "perioden", () => new PeriodenDialog().ShowDialog(this), true));
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

    private List<RibbonEintrag?> ImportExportMenue() => new()
    {
        E("Schild-NRW: Lehrer importieren (CSV)…", "import", () => CsvImport("Lehrer")),
        E("Schild-NRW: Klassen importieren (CSV)…", "import", () => CsvImport("Klassen")),
        E("Schüler importieren (CSV)…", "import", ImportSchueler),
        E("GPU-Dateien importieren (Umstieg)…", "import", GpuImport),
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

    private void AssistentUmschalten()
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
        StandardFenster();
    }

    public void Oeffnen()
    {
        if (!Verwerfen()) return;
        using var ofd = new OpenFileDialog { Filter = "UntPlan-Schuldatei (*.untplan)|*.untplan|Alle Dateien|*.*", Title = "Schuldatei öffnen" };
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
            StandardFenster();
        }
        catch (Exception ex)
        {
            Ui.Fehler(new InvalidOperationException($"Die Datei konnte nicht geöffnet werden:\n{ex.Message}"));
        }
    }

    public bool Speichern(bool unter = false)
    {
        var doc = AppZustand.Doc;
        var pfad = doc.Pfad;
        if (pfad is null || unter)
        {
            using var sfd = new SaveFileDialog { Filter = "UntPlan-Schuldatei (*.untplan)|*.untplan", FileName = string.IsNullOrWhiteSpace(doc.Daten.Einstellungen.Schulname) ? "Schule.untplan" : doc.Daten.Einstellungen.Schulname + ".untplan" };
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
        using var sfd = new SaveFileDialog { Filter = "UntPlan-Schuldatei (*.untplan)|*.untplan", FileName = $"Abteilung_{a.Kurzname}.untplan" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        new Dokument(Core.Module.Abteilungen.Exportieren(AppZustand.D, a.Id)).Speichern(sfd.FileName);
        Ui.Info("Die Abteilungsdatei wurde erstellt. Unterrichte anderer Abteilungen sind darin fixiert.");
    }

    private void AbteilungZusammenfuehren()
    {
        var a = AuswahlDialog.Element(this, ElementTyp.Abteilung, "Abteilung wählen");
        if (a is null) return;
        using var ofd = new OpenFileDialog { Filter = "UntPlan-Schuldatei (*.untplan)|*.untplan" };
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
