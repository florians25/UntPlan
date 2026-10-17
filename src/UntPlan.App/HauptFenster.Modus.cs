using UntPlan.App.Dialoge;
using UntPlan.App.Fenster;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App;

/// <summary>
/// Vertretungsmodus: Die Vertretungsplanung ist vollständig von der Stundenplanung getrennt – eigene Registerkarte,
/// eigene Fenstergruppe, grüne Oberfläche. Beim Wechsel wird die Fensteranordnung des verlassenen Modus gemerkt
/// und die des neuen Modus wiederhergestellt.
/// </summary>
public sealed partial class HauptFenster
{
    private const string GruppeStundenplan = "Stundenplanung";
    private const string GruppeVertretung = "Vertretungsplanung";

    private void VertretungsSeiteAufbauen()
    {
        var vp = _ribbon.Seite("Vertretungsplanung", () => AppZustand.Vertretungsmodus);
        var g = Gruppe(vp, "Kalender");
        g.Eintraege.Add(E("Kalender", "kalender", () => VertretungsFensterOeffnen("kal"), true, new()
        {
            E("Kalender", "kalender", () => VertretungsFensterOeffnen("kal")),
            E("Heute", "kalender", () => AppZustand.DatumSetzen(DateOnly.FromDateTime(DateTime.Today))),
            E("Nächster Unterrichtstag", "vor", () => AppZustand.DatumSetzen(Kalender.NaechsterUnterrichtstag(AppZustand.D, AppZustand.Datum))),
        }));
        g.Eintraege.Add(new RibbonEintrag { Text = "Datum", IstDatum = true, Hinweis = "Planungsdatum: ◂ ▸ blättern (Strg+Bild↑ / Strg+Bild↓), Klick öffnet den Kalender" });

        g = Gruppe(vp, "Vertretungsplanung");
        g.Eintraege.Add(E("Absenzen", "absenz", () => VertretungsFensterOeffnen("abw"), true, new()
        {
            E("Absenzen (Strg+M)", "absenz", () => VertretungsFensterOeffnen("abw")),
            E("Absenzgründe", "absenz", () => Oeffnen<StammdatenFenster>("st:Grund", () => new StammdatenFenster(ElementTyp.Abwesenheitsgrund))),
            E("Klausuren", "klausur", () => VertretungsFensterOeffnen("klaus")),
        }));
        g.Eintraege.Add(E("Vertretungen", "vertretung", () => VertretungsFensterOeffnen("vertr"), true, new()
        {
            E("Vertretungen (Strg+T)", "vertretung", () => VertretungsFensterOeffnen("vertr")),
            E("Vertretungsvorschlag (Strg+G)", "planen", () => VertretungsFensterOeffnen("vorschlag")),
            null,
            E("Vertretungsautomatik…", "optimierung", () => VertretungsautomatikDialog.Zeigen(this, AppZustand.Datum, AppZustand.Datum)),
        }));
        g.Eintraege.Add(E("Ausgabe", "drucken", () => { if (VertretungsFensterOeffnen("vertr") is IDruckbar dr) dr.Drucken(true); }, true, new()
        {
            E("Drucken / Seitenansicht…", "vorschau", () => { if (VertretungsFensterOeffnen("vertr") is IDruckbar dr) dr.Drucken(true); }),
            E("Schülerversion (HTML, 2 Tage)", "html", () => VertretungenFenster.HtmlAusgabe(AppZustand.Datum, 2, false)),
            E("Lehrerversion (HTML, 2 Tage)", "html", () => VertretungenFenster.HtmlAusgabe(AppZustand.Datum, 2, true)),
            E("Woche (HTML)", "html", () => VertretungenFenster.HtmlAusgabe(Kalender.Montag(AppZustand.Datum), 5, true)),
            E("Monitor", "monitor", () => new MonitorFenster().Show()),
            E("Nachrichten zum Tag…", "nachricht", () => TagestextDialog.Zeigen(this, AppZustand.Datum)),
            E("Web-Portal…", "web", () => new PortalDialog().ShowDialog(this)),
        }));
        g.Eintraege.Add(E("Vertretungs-statistik", "statistik", () => VertretungsFensterOeffnen("stat"), true, hinweis: "Abrechnung, Zähler, Mehrarbeit NRW, Entfalltage, Übersicht"));

        g = Gruppe(vp, "Ereignisse");
        g.Eintraege.Add(E("Veranstaltungen", "veranstaltung", () => VertretungsFensterOeffnen("veranst"), menue: new()
        {
            E("Veranstaltungen", "veranstaltung", () => VertretungsFensterOeffnen("veranst")),
            E("Klausuren", "klausur", () => VertretungsFensterOeffnen("klaus")),
        }));
        g.Eintraege.Add(E("Sondereinsätze", "sondereinsatz", () => SondereinsatzDialog.Neu(this, AppZustand.Datum, 1, Array.Empty<int>(), Array.Empty<int>())));
        g.Eintraege.Add(E("Bereitschaften", "bereitschaft", () => VertretungsFensterOeffnen("bereit")));

        g = Gruppe(vp, "Planung");
        g.Eintraege.Add(E("Planungs-dialog", "stundenplan", PlanungsdialogOeffnen, true, hinweis: "Klassen- und Lehrerplan tagesaktuell nebeneinander (Drag & Drop: Verlegen, Tauschen)"));
        g.Eintraege.Add(E("Vormerkungen", "vormerkung", () => VertretungsFensterOeffnen("vorm"), true));
        g.Eintraege.Add(E("Pausen-aufsichten", "pausenaufsicht", () => VertretungsFensterOeffnen("pause"), true));

        g = Gruppe(vp, "Stundenpläne");
        g.Eintraege.Add(E("Klassenplan", "klassen", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false)));
        g.Eintraege.Add(E("Lehrerplan", "lehrer", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false)));
        g.Eintraege.Add(E("Raumplan", "raeume", () => StundenplanFenster.Neu(ElementTyp.Raum, null, false)));
        g.Eintraege.Add(E("Kleine Pläne", "uebersicht", () => KleinePlaeneOeffnen(), menue: new()
        {
            E("Kleine Pläne nebeneinander", "uebersicht", () => KleinePlaeneOeffnen()),
            null,
            E("Kleiner Klassenplan", "klassen", () => StundenplanFenster.Neu(ElementTyp.Klasse, null, false, true)),
            E("Kleiner Lehrerplan", "lehrer", () => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false, true)),
            E("Kleiner Raumplan", "raeume", () => StundenplanFenster.Neu(ElementTyp.Raum, null, false, true)),
        }, hinweis: "Kompakte Stundenpläne (nur Fachkürzel, Pausenzeilen) nebeneinander – folgen über die Fensterverknüpfung der gewählten Vertretung"));

        g = Gruppe(vp, "Einstellungen");
        g.Eintraege.Add(E("Vertretungsformate", "uebersicht", null, menue: new()
        {
            E("Klasse – Standard", "klassen", () => VertretungsformatOeffnen("Klasse - Standard")),
            E("Lehrer – Standard", "lehrer", () => VertretungsformatOeffnen("Lehrer - Standard")),
            E("Druck – Klasse", "drucken", () => VertretungsformatOeffnen("Druck - Klasse")),
            E("Druck – Lehrer", "drucken", () => VertretungsformatOeffnen("Druck - Lehrer")),
            E("Pausenaufsichten", "pausenaufsicht", () => VertretungsformatOeffnen("Pausenaufsichten")),
            null,
            E("Standardanordnung der Fenster", "uebersicht", () => VertretungsStandardFenster()),
            E("Eigene Fensteranordnungen…", "uebersicht", FensteranordnungMenue),
        }));
        g.Eintraege.Add(E("Info-Stundenplan", "html", InfoStundenplan));
        g.Eintraege.Add(E("Einstellungen", "einstellungen", () => VertretungsEinstellungenDialog.Zeigen(this), menue: new()
        {
            E("Einstellungen Vertretungsplanung…", "einstellungen", () => VertretungsEinstellungenDialog.Zeigen(this)),
            E("Vertretungszeitraster…", "zeitraster", () => VertretungsEinstellungenDialog.Zeigen(this, 0)),
            E("Vertretungszähler…", "statistik", () => VertretungsEinstellungenDialog.Zeigen(this, 1)),
            E("Vertretungsvorschlag…", "gewichtung", () => VertretungsEinstellungenDialog.Zeigen(this, 2)),
            null,
            E("Eingabeassistent ein/aus", "assistent", AssistentUmschalten),
        }));

        g = Gruppe(vp, "Modus");
        g.Eintraege.Add(new RibbonEintrag
        {
            Text = "Vertretungs-planung", Symbol = "vertretung", Gross = true, Klick = VertretungsmodusUmschalten, Gedrueckt = () => AppZustand.Vertretungsmodus,
            Hinweis = "Zurück zur Stundenplanung (Strg+Umschalt+V)",
        });
    }

    /// <summary>Planungsdialog: Klassen- und Lehrerplan tagesaktuell nebeneinander.</summary>
    private void PlanungsdialogOeffnen()
    {
        if (!AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        var mdi = Controls.OfType<MdiClient>().First().ClientSize;
        var v = AppZustand.AktiveVertretung;
        var kl = StundenplanFenster.Neu(ElementTyp.Klasse, v?.NeueKlassenIds.FirstOrDefault() is int k && k != 0 ? k : null, false);
        var le = StundenplanFenster.Neu(ElementTyp.Lehrer, v?.LehrerId, false);
        kl.Bounds = new Rectangle(0, 0, mdi.Width / 2, mdi.Height);
        le.Bounds = new Rectangle(mdi.Width / 2, 0, mdi.Width - mdi.Width / 2, mdi.Height);
    }

    /// <summary>
    /// Kleine Stundenpläne nebeneinander am rechten Rand (wie im Vorbild-Fensteranordnungen der Vertretungsplanung): Klasse und Lehrkraft
    /// der gewählten Vertretung (bzw. abwesende Lehrkraft), Vertreter und Raum. Über die Fensterverknüpfung folgen sie der Auswahl.
    /// </summary>
    public List<StundenplanFenster> KleinePlaeneOeffnen()
    {
        if (!AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        var d = AppZustand.D;
        var v = AppZustand.AktiveVertretung;
        var abwesend = d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && AppZustand.Datum >= a.Von && AppZustand.Datum <= a.Bis).Select(a => a.ElementId).ToList();
        var lehrer = Stammdaten.Sortiert(d, ElementTyp.Lehrer).Select(e => e.Id).ToList();
        var erster = v?.LehrerId ?? abwesend.FirstOrDefault(lehrer.Contains);
        if (erster == 0) erster = lehrer.FirstOrDefault();
        var zweiter = v?.VertreterId is int vt && vt != erster ? vt : lehrer.FirstOrDefault(l => l != erster);
        var plaene = new (ElementTyp Typ, int? Id)[]
        {
            (ElementTyp.Klasse, v?.NeueKlassenIds.FirstOrDefault() is int k && k != 0 ? k : null),
            (ElementTyp.Lehrer, erster),
            (ElementTyp.Lehrer, zweiter),
            (ElementTyp.Raum, v?.VertretungsraumId ?? v?.RaumId),
        };
        var mdi = Controls.OfType<MdiClient>().First().ClientSize;
        var res = new List<StundenplanFenster>();
        var x = Math.Max(0, mdi.Width - plaene.Length * 196);
        foreach (var (typ, id) in plaene)
        {
            var f = StundenplanFenster.Neu(typ, id is int i && i != 0 ? i : null, false, true);
            f.Location = new Point(x, 0);
            x += f.Width;
            res.Add(f);
        }
        return res;
    }

    private void VertretungsformatOeffnen(string ansicht)
    {
        AppZustand.Einstellungen.Vertretungsansicht = ansicht;
        AppZustand.Einstellungen.Speichern();
        if (VertretungsFensterOeffnen("vertr") is VertretungenFenster vf) vf.AnsichtSetzen(ansicht);
    }

    /// <summary>Fenster der Vertretungsplanung über ihren Schlüssel öffnen (schaltet bei Bedarf in den Vertretungsmodus).</summary>
    public Form? VertretungsFensterOeffnen(string schluessel)
    {
        if (!AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
        return FensterOeffnen(schluessel);
    }

    /// <summary>Erzeugt bzw. aktiviert ein Fenster anhand seines Schlüssels (Fenstergruppen, Tastenkürzel).</summary>
    public Form? FensterOeffnen(string schluessel)
    {
        Form? f = schluessel switch
        {
            "vertr" => Oeffnen("vertr", () => new VertretungenFenster()),
            "vorschlag" => Oeffnen("vorschlag", () => new VertretungsvorschlagFenster()),
            "abw" => Oeffnen("abw", () => new AbwesenheitenFenster()),
            "kal" => Oeffnen("kal", () => new KalenderFenster()),
            "veranst" => Oeffnen("veranst", () => new VeranstaltungenFenster()),
            "klaus" => Oeffnen("klaus", () => new KlausurenFenster()),
            "vorm" => Oeffnen("vorm", () => new VormerkungenFenster()),
            "bereit" => Oeffnen("bereit", () => new BereitschaftenFenster()),
            "pause" => Oeffnen("pause", () => new PausenaufsichtFenster()),
            "bereitplan" => Oeffnen("bereitplan", () => new BereitschaftsplanungFenster()),
            "ugruppen" => Oeffnen("ugruppen", () => new UGruppenFenster()),
            "stat" => Oeffnen("stat", () => new StatistikFenster()),
            "diag" => Oeffnen("diag", () => new DiagnoseFenster()),
            "ferien" => Oeffnen("ferien", () => new FerienFenster()),
            "wert" => Oeffnen("wert", () => new WertrechnungFenster()),
            "kurs" => Oeffnen("kurs", () => new KursplanungFenster()),
            "komm" => Oeffnen("komm", () => new KommentarUebersicht()),
            _ => null,
        };
        if (f != null) return f;
        var teile = schluessel.Split(':');
        if (teile.Length >= 2 && Enum.TryParse<ElementTyp>(teile[1], out var typ))
        {
            switch (teile[0])
            {
                case "st": return Oeffnen(schluessel, () => new StammdatenFenster(typ));
                case "u": return Oeffnen(schluessel, () => new UnterrichtFenster(typ));
                case "sp" when teile.Length >= 4 && int.TryParse(teile[2], out var id):
                    var vorhanden = AppZustand.D.AlleElemente(typ).Any(e => e.Id == id) ? id : (int?)null;
                    return StundenplanFenster.Neu(typ, vorhanden, teile[3] == "1", teile.Length >= 5 && teile[4] == "k");
            }
        }
        return null;
    }

    /// <summary>Wechsel zwischen Stunden- und Vertretungsplanung.</summary>
    public void VertretungsmodusUmschalten()
    {
        var neu = !AppZustand.Vertretungsmodus;
        // vor dem Öffnen der Vertretungsfenster: im Stundenplan umgeplante Stunden nacherfassen (Vertretungen wandern mit)
        if (neu) UmplanungNacherfassen();
        LayoutMerken(AppZustand.Vertretungsmodus ? GruppeVertretung : GruppeStundenplan);
        SuspendLayout();
        AlleSchliessen();
        if (neu && AppZustand.D.Vertretungseinstellungen.MitAktuellemDatumStarten)
        {
            var heute = DateOnly.FromDateTime(DateTime.Today);
            AppZustand.DatumSetzen(Kalender.IstUnterrichtstag(AppZustand.D, heute) ? heute : Kalender.NaechsterUnterrichtstag(AppZustand.D, heute));
        }
        AppZustand.ModusSetzen(neu);
        FarbenAnwenden();
        _ribbon.AktiveSeite = neu ? Math.Max(0, _ribbon.Seiten.FindIndex(s => s.Text == "Vertretungsplanung")) : 0;
        ResumeLayout();
        if (!LayoutWiederherstellen(neu ? GruppeVertretung : GruppeStundenplan))
        {
            if (neu) VertretungsStandardFenster();
            else StandardFenster();
        }
        DokGeaendert(AenderungsArt.Einstellungen);
    }

    /// <summary>Ergebnis der letzten Nacherfassung umgeplanter Stunden (Selbsttest).</summary>
    internal UmplanungsErgebnis? LetzteUmplanung { get; private set; }

    /// <summary>
    /// Stunden, die im Stundenplan nach dem letzten Abgleich umgeplant wurden, für die Vertretungsplanung nacherfassen: vorhandene
    /// Vertretungen (Vertreter, Raum, Text, Art) wandern auf die neue Lage, nicht zuordenbare bleiben als Vormerkung erhalten.
    /// </summary>
    internal void UmplanungNacherfassen()
    {
        var doc = AppZustand.Doc;
        if (!doc.Daten.Perioden.Any(Umplanungsabgleich.StandVeraltet)) return;
        try
        {
            var probe = Dokument.Klonen(doc.Daten);
            var erg = Umplanungsabgleich.Nacherfassen(probe, AppZustand.Benutzer);
            if (erg.Geaendert && !doc.Schreibgeschuetzt)
                doc.Aendern($"Umgeplante Stunden nacherfasst ({erg.Uebertragen.Count} Vertretung(en) übertragen)", AenderungsArt.Vertretung,
                    d => erg = Umplanungsabgleich.Nacherfassen(d, AppZustand.Benutzer));
            else
            {
                Umplanungsabgleich.StandSichern(doc.Daten);
                doc.AlsGeaendertMarkieren();
            }
            LetzteUmplanung = erg;
            if (erg.Uebertragen.Count == 0 && erg.Hinweise.Count == 0) return;
            var text = (erg.Uebertragen.Count > 0 ? $"{erg.Uebertragen.Count} Vertretung(en) umgeplanter Stunden wurden auf die neue Lage übertragen.\n" : "") +
                       (erg.Vormerkungen.Count > 0 ? $"{erg.Vormerkungen.Count} nicht mehr zuordenbare Vertretung(en) liegen als Vormerkung neben dem Stundenplan.\n" : "") +
                       (erg.Hinweise.Count > 0 ? "\nHinweise:\n" + string.Join("\n", erg.Hinweise.Take(15)) + (erg.Hinweise.Count > 15 ? "\n…" : "") : "");
            Ui.Info(text.Trim(), "Stundenplanänderungen nacherfasst");
        }
        catch (Exception ex)
        {
            Ui.Fehler(ex);
        }
    }

    /// <summary>Farben der Oberfläche nach dem aktuellen Modus setzen (Petrol bzw. Grün).</summary>
    private void FarbenAnwenden()
    {
        foreach (Control c in Controls)
            if (c is MdiClient mdi) mdi.BackColor = Ui.MdiHintergrund;
        KopfFarben();
        _status.BackColor = AppZustand.Vertretungsmodus ? Ui.BlauHell : SystemColors.Control;
        _statusLinks.Text = AppZustand.Vertretungsmodus ? "VERTRETUNGSPLANUNG" : "STUNDENPLANUNG";
        _statusLinks.Font = Ui.SchriftFett;
        _statusLinks.ForeColor = Ui.Blau;
        _ribbon.Invalidate();
        // Listen und Pläne in der Modusfarbe
        foreach (var f in MdiChildren)
        {
            foreach (var g in Ui.Tabellen(f)) Ui.RasterFarben(g);
            FlaechenFaerben(f);
            f.Invalidate(true);
        }
        _assistent.Aktualisieren();
        _assistent.Invalidate(true);
    }

    /// <summary>Werkzeugleisten und Kopfflächen offener Fenster in die Farben des neuen Modus umfärben.</summary>
    private static void FlaechenFaerben(Control c)
    {
        var kopfAlt = new[] { Color.FromArgb(240, 245, 247), Color.FromArgb(229, 239, 235) };
        var leisteAlt = new[] { Color.FromArgb(246, 248, 251), Color.FromArgb(240, 246, 243) };
        foreach (Control k in c.Controls)
        {
            if (k is ToolStrip ts && ts is not MenuStrip && leisteAlt.Contains(ts.BackColor)) ts.BackColor = Ui.Leiste;
            else if (k is not DataGridView && kopfAlt.Contains(k.BackColor)) k.BackColor = Ui.Kopf;
            FlaechenFaerben(k);
        }
    }

    private void LayoutMerken(string gruppe)
    {
        AppZustand.Einstellungen.Fenstergruppen[gruppe] = LayoutAufnehmen();
        AppZustand.Einstellungen.Speichern();
    }

    /// <summary>Positionen aller geöffneten (wiederherstellbaren) Fenster.</summary>
    internal List<FensterPosition> LayoutAufnehmen()
    {
        var liste = new List<FensterPosition>();
        foreach (var f in MdiChildren)
        {
            if (f.Tag is not string s || string.IsNullOrEmpty(s)) continue;
            var b = f.WindowState == FormWindowState.Normal ? f.Bounds : f.RestoreBounds;
            liste.Add(new FensterPosition { Schluessel = s, X = b.X, Y = b.Y, B = b.Width, H = b.Height, Maximiert = f.WindowState == FormWindowState.Maximized });
        }
        return liste;
    }

    private bool LayoutWiederherstellen(string gruppe) =>
        AppZustand.Einstellungen.Fenstergruppen.TryGetValue(gruppe, out var liste) && LayoutOeffnen(liste);

    /// <summary>Fenster laut Positionsliste öffnen und anordnen.</summary>
    internal bool LayoutOeffnen(List<FensterPosition> liste)
    {
        if (liste.Count == 0) return false;
        var n = 0;
        foreach (var p in liste)
        {
            try
            {
                var f = FensterOeffnen(p.Schluessel);
                if (f is null) continue;
                f.WindowState = FormWindowState.Normal;
                // gespeicherte Lage (evtl. von einem größeren Bildschirm, anderer DPI oder mit negativem Y) in den Arbeitsbereich holen
                f.Bounds = GespeicherteLage(p);
                if (p.Maximiert) f.WindowState = FormWindowState.Maximized;
                n++;
            }
            catch (Exception ex) { Ui.Fehler(ex); }
        }
        // falls der Arbeitsbereich gerade erst seine endgültige Größe bekommt (Start, Moduswechsel): danach noch einmal prüfen
        if (n > 0 && IsHandleCreated) BeginInvoke(MdiFensterKlemmen);
        return n > 0;
    }

    /// <summary>Standardanordnung des Vertretungsmodus: Kalender, Absenzen, Vertretungen, Vertretungsvorschlag und tagesaktuelle Pläne.</summary>
    public void VertretungsStandardFenster()
    {
        AlleSchliessen();
        var mdi = Controls.OfType<MdiClient>().First().ClientSize;
        var w = mdi.Width;
        var h = mdi.Height;
        var links = Math.Max(380, (int)(w * 0.27));
        var mitteB = (int)(w * 0.45);
        var oben = (int)(h * 0.36);
        var kal = FensterOeffnen("kal")!;
        kal.Bounds = new Rectangle(0, 0, links, oben);
        var abw = FensterOeffnen("abw")!;
        abw.Bounds = new Rectangle(0, oben, links, h - oben);
        var vertr = FensterOeffnen("vertr")!;
        vertr.Bounds = new Rectangle(links, 0, mitteB, (int)(h * 0.55));
        var vor = FensterOeffnen("vorschlag")!;
        vor.Bounds = new Rectangle(links, (int)(h * 0.55), mitteB, h - (int)(h * 0.55));
        var d = AppZustand.D;
        var kl = StundenplanFenster.Neu(ElementTyp.Klasse, Stammdaten.Sortiert(d, ElementTyp.Klasse).FirstOrDefault()?.Id, false);
        kl.Bounds = new Rectangle(links + mitteB, 0, w - links - mitteB, h / 2);
        var abwesend = d.Abwesenheiten.FirstOrDefault(a => a.Art == AbwesenheitsArt.Lehrer && AppZustand.Datum >= a.Von && AppZustand.Datum <= a.Bis)?.ElementId;
        var le = StundenplanFenster.Neu(ElementTyp.Lehrer, abwesend ?? Stammdaten.Sortiert(d, ElementTyp.Lehrer).FirstOrDefault()?.Id, false);
        le.Bounds = new Rectangle(links + mitteB, h / 2, w - links - mitteB, h - h / 2);
        vertr.Activate();
    }

    /// <summary>Kontextmenü auf dem freien Arbeitsbereich.</summary>
    private void ArbeitsbereichMenue(Point pt, Control mdi)
    {
        var cm = new ContextMenuStrip { Font = Ui.Schrift };
        if (AppZustand.Vertretungsmodus)
        {
            cm.Items.Add("Vertretungen (Strg+T)", Symbole.Get("vertretung"), (_, _) => FensterOeffnen("vertr"));
            cm.Items.Add("Vertretungsvorschlag (Strg+G)", Symbole.Get("planen"), (_, _) => FensterOeffnen("vorschlag"));
            cm.Items.Add("Absenzen (Strg+M)", Symbole.Get("abwesenheit"), (_, _) => FensterOeffnen("abw"));
            cm.Items.Add("Kalender", Symbole.Get("kalender"), (_, _) => FensterOeffnen("kal"));
            cm.Items.Add("Standardanordnung Vertretungsplanung", Symbole.Get("uebersicht"), (_, _) => VertretungsStandardFenster());
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Zur Stundenplanung wechseln", Symbole.Get("stundenplan"), (_, _) => VertretungsmodusUmschalten());
        }
        else
        {
            cm.Items.Add("Klassenplan", Symbole.Get("klassen"), (_, _) => StundenplanFenster.Neu(ElementTyp.Klasse, null, false));
            cm.Items.Add("Lehrerplan", Symbole.Get("lehrer"), (_, _) => StundenplanFenster.Neu(ElementTyp.Lehrer, null, false));
            cm.Items.Add("Unterricht Klassen", Symbole.Get("unterricht"), (_, _) => FensterOeffnen("u:Klasse"));
            cm.Items.Add("Standard-Fenstergruppe", Symbole.Get("uebersicht"), (_, _) => StandardFenster());
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Zur Vertretungsplanung wechseln", Symbole.Get("vertretung"), (_, _) => VertretungsmodusUmschalten());
        }
        cm.Items.Add(new ToolStripSeparator());
        cm.Items.Add(_assistent.Visible ? "Eingabeassistent ausblenden" : "Eingabeassistent einblenden", Symbole.Get("assistent"), (_, _) => AssistentUmschalten());
        cm.Items.Add("Alle Fenster schließen", Symbole.Get("loeschen"), (_, _) => AlleSchliessen());
        cm.Show(mdi, pt);
    }

    /// <summary>Wird im Konstruktor aufgerufen: Kontextmenü und Datumsanzeige im Arbeitsbereich.</summary>
    private void ModusVorbereiten()
    {
        foreach (Control c in Controls)
            if (c is MdiClient mdi)
            {
                mdi.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ArbeitsbereichMenue(e.Location, mdi); };
                mdi.Paint += (_, e) =>
                {
                    // großer Schriftzug im Hintergrund: Modus und Planungsdatum
                    var text = AppZustand.Vertretungsmodus ? $"Vertretungsplanung · {Format.Datum(AppZustand.Datum)}" : "Stundenplanung";
                    using var f = new Font("Segoe UI", 28f, FontStyle.Bold);
                    var farbe = Color.FromArgb(70, Color.White);
                    TextRenderer.DrawText(e.Graphics, text, f, new Rectangle(0, mdi.ClientSize.Height - 70, mdi.ClientSize.Width - 20, 60), farbe, TextFormatFlags.Right | TextFormatFlags.Bottom);
                };
                mdi.Resize += (_, _) => mdi.Invalidate();
            }
        AppZustand.DatumGewechselt += datum =>
        {
            // Vertretungsmodus: die am Planungsdatum gültige Periode ist aktiv (Unterricht, Stammdaten-Zählungen)
            if (AppZustand.Vertretungsmodus && AppZustand.D.PeriodeAm(datum) is { } per && per.Id != AppZustand.D.AktivePeriodeId)
            {
                AppZustand.D.AktivePeriodeId = per.Id;
                AppZustand.Doc.Melden(AenderungsArt.Alles);
            }
            _ribbon.Invalidate();
            foreach (Control c in Controls) if (c is MdiClient m) m.Invalidate();
            if (AppZustand.Vertretungsmodus) _assistent.Aktualisieren();
        };
        _statusRechts.IsLink = true;
        _statusRechts.LinkBehavior = LinkBehavior.HoverUnderline;
        _statusRechts.ToolTipText = "Aktive Periode wählen";
        _statusRechts.Click += (_, _) =>
        {
            var cm = new ContextMenuStrip { Font = Ui.Schrift };
            foreach (var p in AppZustand.D.Perioden.OrderBy(x => x.Von))
            {
                var id = p.Id;
                var it = new ToolStripMenuItem($"{p.Name}  ({Format.Datum(p.Von)} – {Format.Datum(Core.Module.Perioden.Ende(AppZustand.D, p))}){(p.Fixiert ? "  fixiert" : "")}") { Checked = p.Id == AppZustand.D.AktivePeriodeId };
                it.Click += (_, _) => AppZustand.Doc.Aendern("Periode aktivieren", AenderungsArt.Alles, d => d.AktivePeriodeId = id);
                cm.Items.Add(it);
            }
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add("Perioden verwalten…", Symbole.Get("perioden"), (_, _) => new PeriodenDialog().ShowDialog(this));
            cm.Show(_status, new Point(_status.Width - 200, 0), ToolStripDropDownDirection.AboveLeft);
        };
        FarbenAnwenden();
    }
}
