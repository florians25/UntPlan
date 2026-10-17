using UntPlan.App.Dialoge;
using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Optimierung;

namespace UntPlan.App;

/// <summary>
/// Selbsttest (Aufruf: UntPlan.exe --selbsttest=Ordner): öffnet mit der Beispielschule nacheinander alle Fenster
/// und Dialoge, speichert je ein Bildschirmfoto und protokolliert Fehler in selbsttest.log. Für CI und Dokumentation.
/// </summary>
public sealed partial class HauptFenster
{
    /// <summary>Schnelle Druckprobe (UntPlan.exe --druckprobe=Ordner): Beispielschule planen, Stundenplandrucke als Bilder speichern, beenden.</summary>
    internal void Druckprobe(string ordner)
    {
        Directory.CreateDirectory(ordner);
        var d = Demo.Beispielschule();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 1 });
        d.AktivePeriode.Plan = erg.OrderBy(e => e.Kennzahlen.Bewertung).First().Plan;
        d.Einstellungen.Adresse = "Asselborner Weg 101, 51429 Bergisch Gladbach";
        AppZustand.Setzen(new Dokument(d));
        // ein einzelner Plan im kleinen Format: trotzdem das vollständige Formular mit Unterrichtsliste
        StundenplanDruck.Bilder(ElementTyp.Klasse, new List<int> { d.Klassen[0].Id }, new StundenplanFormat { Typ = ElementTyp.Klasse, Gross = false, Schriftgroesse = 8 })[0]
            .Save(Path.Combine(ordner, "druck_einzeln_klein.png"));
        foreach (var (typ, name) in new[] { (ElementTyp.Klasse, "klasse"), (ElementTyp.Lehrer, "lehrer"), (ElementTyp.Raum, "raum") })
        {
            var ids = Stammdaten.Sortiert(d, typ).Take(2).Select(e => e.Id).ToList();
            StundenplanDruck.Bilder(typ, ids, new StundenplanFormat { Typ = typ, Gross = true, Schriftgroesse = 10 })[0].Save(Path.Combine(ordner, $"druck_{name}.png"));
            StundenplanDruck.Bilder(typ, ids, new StundenplanFormat { Typ = typ, Gross = false, Schriftgroesse = 8 })[0].Save(Path.Combine(ordner, $"druck_{name}_klein.png"));
        }
        File.WriteAllText(Path.Combine(ordner, "druckprobe.log"), "ENDE");
        Close();
    }

    internal void SelbstTest(string ordner)
    {
        Directory.CreateDirectory(ordner);
        WindowState = FormWindowState.Normal;
        Bounds = Screen.PrimaryScreen?.Bounds ?? Bounds;
        var log = new List<string>();
        Ui.FehlerProtokoll = new List<string>();
        var d = Demo.Beispielschule();
        var erg = Optimierer.Optimieren(d, new OptimierungsOptionen { Strategie = Strategie.A, AnzahlPlaene = 1, Seed = 1 });
        d.AktivePeriode.Plan = erg.OrderBy(e => e.Kennzahlen.Bewertung).First().Plan;
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var tag = Kalender.IstUnterrichtstag(d, heute) ? heute : Kalender.NaechsterUnterrichtstag(d, heute);
        var krank = d.Lehrer.OrderByDescending(l => d.Unterrichte.Count(u => u.LehrerIds.Contains(l.Id))).First();
        d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Lehrer, ElementId = krank.Id, Von = tag, Bis = tag, GrundId = d.Abwesenheitsgruende.FirstOrDefault()?.Id });
        if (d.Klassen.FirstOrDefault(k => k.Jahrgang == "7") is { } fahrt)
            d.Abwesenheiten.Add(new Abwesenheit { Id = d.NeueId(), Art = AbwesenheitsArt.Klasse, ElementId = fahrt.Id, Von = tag, Bis = tag, Text = "Exkursion" });
        // Veranstaltung (Sportwoche) mit einer wegen Krankheit entfernten Lehrkraft: Vertreter „…,?“ und rotes ??? im Veranstaltungsfenster
        if (d.Klassen.FirstOrDefault(k => k.Jahrgang == "5") is { } sportKlasse)
        {
            var vaLehrer = d.Lehrer.Where(l => l.Id != krank.Id).Take(2).Select(l => l.Id).Append(krank.Id).ToList();
            var va = new Veranstaltung { Id = d.NeueId(), Von = tag, Bis = tag, StundeVon = 1, StundeBis = 99, KlassenIds = new() { sportKlasse.Id }, LehrerIds = vaLehrer, Text = "Sportwoche" };
            d.Veranstaltungen.Add(va);
            va.Ersatz.Add(new VeranstaltungsErsatz { Datum = tag, LehrerId = krank.Id, AbwesenheitId = d.Abwesenheiten[0].Id });
        }
        Core.Module.Pausenaufsichten.Optimieren(d);
        d.Tagestexte.Add(new Tagestext { Datum = tag, Text = "Heute 7. Stunde Lehrerkonferenz – Unterricht endet nach der 6. Stunde." });
        AppZustand.Setzen(new Dokument(d));

        // gespeicherte Spaltenlayouts bzw. Ansicht der Vertretungsliste (aus früheren Läufen) nicht übernehmen: Standard wie im Vorbild
        foreach (var k in AppZustand.Einstellungen.Tabellenlayouts.Keys.Where(k => k.StartsWith("VertretungenFenster", StringComparison.Ordinal)).ToList())
            AppZustand.Einstellungen.Tabellenlayouts.Remove(k);
        AppZustand.Einstellungen.Vertretungsansicht = "Klasse - Standard";
        // Ja/Nein-Abfragen (z. B. „Soll die Verlegung rückgängig gemacht werden?“) nicht modal: mit Ja beantworten und protokollieren
        var fragen = new List<string>();
        Ui.FrageAntwort = (text, _) => { fragen.Add(text); return true; };
        // Hinweise (z. B. „Stundenplanänderungen nacherfasst“) nicht modal: protokollieren
        var hinweise = new List<string>();
        Ui.InfoAntwort = (text, _) => hinweise.Add(text);
        var logPfad = Path.Combine(ordner, "selbsttest.log");
        File.WriteAllText(logPfad, "");
        void Schritt(string name, Action aktion, int ribbonSeite = -1, bool schliessen = true)
        {
            var vorher = log.Count;
            File.AppendAllText(logPfad, $"START {name}{Environment.NewLine}");
            try
            {
                if (schliessen) AlleSchliessen();
                if (ribbonSeite >= 0) { _ribbon.AktiveSeite = ribbonSeite; _ribbon.Invalidate(); }
                aktion();
                Warten(900);
                Foto(Path.Combine(ordner, name + ".png"));
                // jede Tabellenzelle wird beim Überfahren mit der Maus hervorgehoben (Text-, Auswahl- und Kontrollkästchen-Zellen)
                Steuerelemente.Auswahlbreite.AlleErfassen();
                // UP5: Rasterlinien aller Tabellen vollständig (keine verschwommenen/fehlenden Linien nach eigenen Zellzeichnern)
                var raster = Application.OpenForms.Cast<Form>().Where(f => f.Visible && f != this).SelectMany(RasterPruefen).ToList();
                if (raster.Count > 0) log.Add($"INFO  {name}: Rasterlinien abweichend (eigene Zellrahmen?): {string.Join(", ", raster)}");
                var hover = Application.OpenForms.Cast<Form>().Where(f => f.Visible && f != this).SelectMany(HoverPruefen).ToList();
                if (hover.Count > 0) log.Add($"FEHLER {name}: Zellen ohne Hover-Hervorhebung: {string.Join(", ", hover)}");
                if (Ui.FehlerProtokoll is { Count: > 0 } fp)
                {
                    log.Add($"FEHLER {name}: {string.Join(Environment.NewLine, fp)}");
                    fp.Clear();
                }
                else log.Add($"OK    {name}");
            }
            catch (Exception ex)
            {
                log.Add($"FEHLER {name}: {ex}");
            }
            foreach (var f in Application.OpenForms.Cast<Form>().Where(f => f != this && f.Owner == this && !f.IsMdiChild).ToList()) f.Close();
            // schrittweise mitschreiben, damit bei einem Absturz sichtbar bleibt, wo er passierte
            File.AppendAllLines(logPfad, log.Skip(vorher));
        }

        T Max<T>(T f) where T : Form
        {
            f.WindowState = FormWindowState.Maximized;
            return f;
        }

        Schritt("01_Arbeitsbereich", StandardFenster, 0);
        void KopfPruefen(string modus)
        {
            // Sprint 6: Programmkopf wie im Vorbild 2026 – keine Windows-Titelleiste, ein Balken in der Modusfarbe, Titel mittig, helle Registerkarte
            if (!NativerRahmen && FormBorderStyle != FormBorderStyle.None) throw new InvalidOperationException("Hauptfenster hat noch die Windows-Titelleiste");
            if (_schnell.BackColor != Ui.Titel || _ribbon.Rechts.BackColor != Ui.Titel) throw new InvalidOperationException($"Kopf nicht in der Modusfarbe ({modus})");
            _schnell.Refresh();
            var t = KopfTitelBereich;
            if (t.IsEmpty) throw new InvalidOperationException("Fenstertitel wird im Kopf nicht gezeichnet");
            var mitte = t.X + t.Width / 2;
            if (Math.Abs(mitte - _schnell.Width / 2) > _schnell.Width / 6) throw new InvalidOperationException($"Titel nicht mittig: {t} bei Breite {_schnell.Width}");
            if (_ribbon.Rechts.Items.Count != 8) throw new InvalidOperationException("Fensteranordnungen 1–8 fehlen rechts in der Registerleiste");
            if (!Text.StartsWith("UntPlan") || !Text.Contains("-  Stundenplan ")) throw new InvalidOperationException("Fenstertitel nicht wie im Vorbild: " + Text);
            using var bild = new Bitmap(_ribbon.Width, _ribbon.Height);
            _ribbon.DrawToBitmap(bild, new Rectangle(Point.Empty, bild.Size));
            var akzentGefunden = Enumerable.Range(0, Math.Min(bild.Width, 900)).Any(x => bild.GetPixel(x, 3).ToArgb() == Steuerelemente.Ribbon.Akzent.ToArgb());
            if (!akzentGefunden) throw new InvalidOperationException("Gewählte Registerkarte ohne Akzentlinie");
            log.Add($"INFO  Kopf ({modus}): „{Text}“ mittig bei {mitte}/{_schnell.Width}, Farbe {Ui.Titel.R},{Ui.Titel.G},{Ui.Titel.B}");
        }
        Schritt("01a_Programmkopf", () => KopfPruefen("Stundenplanung"), 1, false);
        Schritt("02_Klassen", () => Max(Oeffnen<StammdatenFenster>("st:Klasse", () => new StammdatenFenster(ElementTyp.Klasse))), 1);
        Schritt("03_Lehrer", () => Max(Oeffnen<StammdatenFenster>("st:Lehrer", () => new StammdatenFenster(ElementTyp.Lehrer))), 1);
        Schritt("04_Raeume", () => Max(Oeffnen<StammdatenFenster>("st:Raum", () => new StammdatenFenster(ElementTyp.Raum))), 1);
        Schritt("05_Faecher", () => Max(Oeffnen<StammdatenFenster>("st:Fach", () => new StammdatenFenster(ElementTyp.Fach))), 1);
        Schritt("06_Zeitwuensche", () => Max(ZeitwuenscheFenster.Zeigen(ElementTyp.Lehrer, AppZustand.D.Lehrer.First().Id)), 1);
        Schritt("07_Unterricht", () => Max(Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse))), 1);
        Schritt("08_Stundenplan_Klasse", () => Max(StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false)), 3);
        Schritt("09_Stundenplan_Lehrer", () => Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false)), 3);
        Schritt("09a_Stundenplan_Einzelstunden_Pausen", () =>
        {
            var f = Max(StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false));
            f.FormatAnzeigen(new StundenplanFormat { Name = "Klassenplan Einzelstunden", Typ = ElementTyp.Klasse, Einzelstunden = true, PausenAnzeigen = true, Schriftgroesse = 9 });
            if (AppZustand.D.Stundenplan.FirstOrDefault(p => f.Plan.StundenAm(p.Tag, p.Stunde).Contains(p)) is { } erste) f.StundeAnklicken(erste);
        }, 3);
        // Sprint 6: Formatdialog wie im Vorbild (Allgemein | Auswahl (Bereich) | Layout 1 | Layout 2 | HTML), Grundformen, Stundenplan-Stunde
        Form? formatDialog = null;
        StundenplanFormat FormatTest() => new() { Name = "Klassenplan", Typ = ElementTyp.Klasse, Einzelstunden = true, PausenAnzeigen = true, Farbe = FarbeNach.Lehrer };
        Bitmap PlanBild(Steuerelemente.PlanAnsicht pa)
        {
            var bmp = new Bitmap(Math.Max(10, pa.Width), Math.Max(10, pa.Height));
            pa.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            return bmp;
        }
        T Feld<T>(Form f, string name) where T : Control => StundenplanEinstellungenDialog.AlleKontrollen(f).OfType<T>().First(c => c.Name == name || c.Text == name);
        void FormatKarte(string karte, Action<Form>? aktion = null)
        {
            formatDialog = StundenplanEinstellungenDialog.Anzeigen(this, FormatTest(), ElementTyp.Klasse, AppZustand.D.Klassen.First().Id);
            if (!formatDialog.Text.StartsWith("Format 01 - Einzelstundenplan Tage waagrecht, Stunden senkrecht")) throw new InvalidOperationException("Titel des Formatdialogs: " + formatDialog.Text);
            StundenplanEinstellungenDialog.KarteWaehlen(formatDialog, karte);
            Warten(150);
            aktion?.Invoke(formatDialog);
        }
        Schritt("09b_Stundenplan_Formatdialog", () => FormatKarte("Allgemein"), 3);
        Schritt("09b1_Format_Auswahl_Bereich", () => FormatKarte("Auswahl (Bereich)", d =>
        {
            Feld<TextBox>(d, "VonStunde").Text = "2";
            Feld<TextBox>(d, "BisStunde").Text = "5";
            Feld<TextBox>(d, "BisWochenstunde").Text = Core.Planung.Planungsdienst.Tagname(AppZustand.D, 3) + "-5";
            Warten(100);
            var v = StundenplanEinstellungenDialog.Vorschau(d);
            using var _ = PlanBild(v);
            if (v.SichtbarerBereich != (2, 5, 0, 3)) throw new InvalidOperationException($"Bereich wirkt nicht auf die Vorschau: {v.SichtbarerBereich}");
            if (Feld<TextBox>(d, "VonWochenstunde").Text != Core.Planung.Planungsdienst.Tagname(AppZustand.D, 0) + "-2") throw new InvalidOperationException("Wochenstunden folgen nicht der Stunden-Nummer: " + Feld<TextBox>(d, "VonWochenstunde").Text);
            log.Add($"INFO  Format-Bereich: Stunden {v.SichtbarerBereich.VonStunde}–{v.SichtbarerBereich.BisStunde}, Tage {v.SichtbarerBereich.VonTag}–{v.SichtbarerBereich.BisTag}");
        }), 3);
        Schritt("09b2_Format_Layout1", () => FormatKarte("Layout 1", d =>
        {
            Feld<TextBox>(d, "DickerStrichVor").Text = "3";
            Feld<TextBox>(d, "DoppelstrichVor").Text = "5";
            Feld<CheckBox>(d, "TrennstrichTage").Checked = true;
            var versenkt = StundenplanEinstellungenDialog.AlleKontrollen(d).OfType<GroupBox>().First(g => g.Text == "Freie Stunden").Controls.Cast<Control>()
                .SelectMany(StundenplanEinstellungenDialog.AlleKontrollen).OfType<RadioButton>().First(r => r.Text == "Versenkt");
            versenkt.Checked = true;
            Warten(100);
            var v = StundenplanEinstellungenDialog.Vorschau(d);
            using var _ = PlanBild(v);
            if (!v.GezeichneteLinien.Contains("dick vor 3") || !v.GezeichneteLinien.Contains("doppelt vor 5") || !v.GezeichneteLinien.Any(x => x.StartsWith("tag vor")))
                throw new InvalidOperationException("Linien aus Layout 1 fehlen: " + string.Join(", ", v.GezeichneteLinien));
            if (v.Format.FreieStunden.Raeumlich != Raeumlich.Versenkt) throw new InvalidOperationException("Räumliche Gestaltung wird nicht übernommen");
            log.Add("INFO  Layout 1: " + string.Join(", ", v.GezeichneteLinien));
        }), 3);
        Schritt("09b3_Format_Layout2", () => FormatKarte("Layout 2", d =>
        {
            var stern = Feld<CheckBox>(d, "Fixierung mit * kennzeichnen");
            stern.Checked = false;
            if (StundenplanEinstellungenDialog.Vorschau(d).Format.FixierungMitStern) throw new InvalidOperationException("Layout 2 wird nicht übernommen");
            stern.Checked = true;
        }), 3);
        Schritt("09b4_Format_HTML", () => FormatKarte("HTML"), 3);
        Schritt("09b5_Stundenplanformate_Grundformen", () =>
        {
            var g = StundenplanGrundformDialog.Anzeigen(this);
            var formen = StundenplanEinstellungenDialog.AlleKontrollen(g).OfType<RadioButton>().Select(r => r.Text).ToList();
            if (!new[] { "Format 01", "Format 10", "Format 11", "Format 20", "Format 21", "Format 30", "Format 40" }.All(formen.Contains))
                throw new InvalidOperationException("Grundformen unvollständig: " + string.Join(", ", formen));
        }, 3);
        Schritt("09b6_Stundenplan_Stunde_Editor", () =>
        {
            var f = FormatTest();
            var d = StundenplanStundeDialog.Anzeigen(this, f, ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, out var ergebnis);
            d.WindowState = FormWindowState.Normal;
            var ed = StundenplanStundeDialog.EditorVon(d);
            ed.Hinzufuegen(StundenFeldArt.Fach, null);
            ed.Hinzufuegen(StundenFeldArt.Lehrer, null);
            ed.Ausgewaehlt!.Kursiv = true;
            ed.Hinzufuegen(StundenFeldArt.Raum, null);
            ed.Ausrichten();
            Warten(150);
            var neu = ergebnis();
            if (neu.Felder.Count != 3 || neu.Standardformat) throw new InvalidOperationException($"Editor liefert {neu.Felder.Count} Felder, Standardformat={neu.Standardformat}");
            if (ed.Felder[1].Beschriftung != "1Lehrer") throw new InvalidOperationException("Beschriftung wie im Vorbild erwartet („1Lehrer“): " + ed.Felder[1].Beschriftung);
            // der Stundenplan zeichnet die eigenen Felder
            var plan = new Steuerelemente.PlanAnsicht { Typ = ElementTyp.Klasse, ElementId = AppZustand.D.Klassen.First().Id, Format = neu, Width = 700, Height = 520, Bearbeitbar = false };
            using (PlanBild(plan)) { }
            var arten = plan.GezeichneteFelder.Select(x => x.Art).Distinct().ToList();
            if (!arten.Contains(StundenFeldArt.Lehrer) || !arten.Contains(StundenFeldArt.Fach)) throw new InvalidOperationException("Plan zeichnet die eigenen Felder nicht: " + string.Join(",", arten));
            log.Add($"INFO  Stundenplan-Stunde: {neu.Felder.Count} Felder, gezeichnet {plan.GezeichneteFelder.Count} (z. B. {string.Join(" | ", plan.GezeichneteFelder.Take(3).Select(x => x.Text))})");
            plan.Dispose();
        }, 3);
        Schritt("09b7_Format10_Stunden_waagrecht", () =>
        {
            var f = FormatTest();
            f.Grundform = Grundform.Format10;
            f.Name = "Format 10 (Selbsttest)";
            var sp = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false);
            sp.WindowState = FormWindowState.Normal;
            sp.Bounds = new Rectangle(10, 10, 900, 420);
            sp.Plan.Format = f;
            Warten(200);
            sp.Plan.Refresh();
            using (PlanBild(sp.Plan)) { }
            if (!sp.Plan.Transponiert) throw new InvalidOperationException("Format 10 nicht transponiert");
            log.Add("INFO  Format 10: Stunden waagrecht, Tage senkrecht");
        }, 3);
        Schritt("09c_Tauschpfeil_Stundenplan", () =>
        {
            // Stunde einer Klasse „ziehen“ und über eine Stunde stellen, mit der kollisionsfrei getauscht werden kann
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            foreach (var k in dd.Klassen)
                foreach (var p in ix.PlanVon(ElementTyp.Klasse, k.Id).ToList())
                {
                    var block = Core.Planung.Planungsdienst.BlockVon(dd, p);
                    if (block[0] != p) continue;
                    for (var t = 0; t < dd.Zeitraster.TageProWoche; t++)
                        for (var s = 1; s <= dd.Zeitraster.StundenProTag; s++)
                        {
                            if (Core.Planung.Planungsdienst.Zielstatus(dd, ix, block, t, s) != Core.Planung.Feldstatus.TauschMoeglich) continue;
                            var f = Max(StundenplanFenster.Neu(ElementTyp.Klasse, k.Id, false));
                            f.Plan.Ziehvorschau($"plan:{dd.Stundenplan.IndexOf(p)};{string.Join(",", block.Select(x => dd.Stundenplan.IndexOf(x)))}", t, s);
                            if (f.Plan.ZielstatusUnterMaus != Core.Planung.Feldstatus.TauschMoeglich) throw new InvalidOperationException("Tauschanzeige: Ziel unter der Maus ist kein Tauschpartner.");
                            return;
                        }
                }
            throw new InvalidOperationException("Kein Tausch im Beispielplan gefunden.");
        }, 3);
        Schritt("09d_Fensterverknuepfung", () =>
        {
            var kl = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false);
            var le = StundenplanFenster.Neu(ElementTyp.Lehrer, null, false);
            var un = Oeffnen<UnterrichtFenster>("u:Lehrer", () => new UnterrichtFenster(ElementTyp.Lehrer));
            var st = Oeffnen<StammdatenFenster>("st:Lehrer", () => new StammdatenFenster(ElementTyp.Lehrer));
            LayoutMdi(MdiLayout.TileVertical);
            var p = AppZustand.Doc.Index.PlanVon(ElementTyp.Klasse, kl.ElementId).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).Skip(3).First();
            kl.StundeAnklicken(p);
            Warten(300);
            var u = AppZustand.Doc.Index.Unterrichte[p.UnterrichtId];
            if (!u.LehrerIds.Contains(le.ElementId)) throw new InvalidOperationException("Fensterverknüpfung: Der Lehrerplan folgt der gewählten Stunde nicht.");
            if (le.Plan.Ausgewaehlt != p) throw new InvalidOperationException("Fensterverknüpfung: Die Stunde ist im Lehrerplan nicht markiert.");
            _ = un;
            _ = st;
        }, 3);
        // Sprint 27: Klick auf eine nicht verplante Stunde (leere Zelle) aktualisiert die verknüpften Pläne trotzdem
        void LeereZellePruefen(bool tages)
        {
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            var kl = StundenplanFenster.Neu(ElementTyp.Klasse, dd.Klassen.First().Id, false);
            var le = StundenplanFenster.Neu(ElementTyp.Lehrer, null, false);
            var ra = StundenplanFenster.Neu(ElementTyp.Raum, null, false);
            LayoutMdi(MdiLayout.TileVertical);
            Warten(200);
            if (tages != kl.Plan.Tagesmodus) throw new InvalidOperationException($"Leere Zelle: Plan ist {(kl.Plan.Tagesmodus ? "" : "nicht ")}tagesaktuell.");
            // zuerst eine Stunde wählen (die anderen Pläne folgen), dann eine leere Zelle desselben Klassenplans
            var p = ix.PlanVon(ElementTyp.Klasse, kl.ElementId).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).First();
            kl.StundeAnklicken(p);
            Warten(150);
            var lehrer = le.ElementId;
            var raum = ra.ElementId;
            var belegt = ix.PlanVon(ElementTyp.Klasse, kl.ElementId).Select(x => (x.Tag, x.Stunde)).ToHashSet();
            var leer = Enumerable.Range(0, dd.Zeitraster.TageProWoche).SelectMany(t => Enumerable.Range(1, dd.Zeitraster.StundenProTag).Select(st => (Tag: t, Stunde: st)))
                .Where(z => !belegt.Contains(z)).OrderByDescending(z => z.Stunde).ThenBy(z => z.Tag).First();
            // tagesaktuell: Spalte = Tage ab Montag der angezeigten Woche
            var spalte = tages ? (leer.Tag + dd.Zeitraster.ErsterSchultag) % 7 : leer.Tag;
            kl.LeereZelleAnklicken(spalte, leer.Stunde);
            Warten(150);
            foreach (var (f, id, name) in new[] { (le, lehrer, "Lehrerplan"), (ra, raum, "Raumplan") })
            {
                if (f.ElementId != id) throw new InvalidOperationException($"Leere Zelle: {name} hat das Element gewechselt.");
                if (f.Plan.Ausgewaehlt != null || f.Plan.TagesAuswahl != null || f.Plan.MarkierterUnterricht != null)
                    throw new InvalidOperationException($"Leere Zelle: {name} behält die vorherige Auswahl.");
                if (f.Plan.MarkierteZelle != (spalte, leer.Stunde))
                    throw new InvalidOperationException($"Leere Zelle: {name} markiert nicht Tag {spalte}/Stunde {leer.Stunde} (sondern {f.Plan.MarkierteZelle}).");
            }
            if (kl.Plan.Ausgewaehlt != null) throw new InvalidOperationException("Leere Zelle: Klassenplan behält die vorherige Stunde.");
            log.Add($"INFO  Leere Zelle ({(tages ? "tagesaktuell" : "Regelplan")}): Tag {spalte}, Stunde {leer.Stunde} – Lehrer- und Raumplan synchron, Element unverändert");
        }
        Schritt("27b_Leere_Zelle_Synchronisation", () => LeereZellePruefen(false), 3);
        // ---------------------------------------------------------------- UP5: Stundenplanmodus nach Vorbild (65/66)
        StundenplanFenster[] DreiPlaene(bool klein, bool gross)
        {
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            var kl = dd.Klassen.OrderByDescending(k => ix.UnterrichteVon(ElementTyp.Klasse, k.Id).Count(u => u.Zeilen.Select(z => z.FachId).Distinct().Count() > 1)).First();
            var le = dd.Lehrer.OrderByDescending(l => ix.PlanVon(ElementTyp.Lehrer, l.Id).Count()).First();
            var ra = dd.Raeume.OrderByDescending(r => ix.PlanVon(ElementTyp.Raum, r.Id).Count()).First();
            var f = new[] { StundenplanFenster.Neu(ElementTyp.Klasse, kl.Id, gross, klein), StundenplanFenster.Neu(ElementTyp.Lehrer, le.Id, gross, klein), StundenplanFenster.Neu(ElementTyp.Raum, ra.Id, gross, klein) };
            var x = 10;
            foreach (var p in f)
            {
                if (!klein) p.Size = new Size(520, 700);
                else p.Size = new Size(300, p.Height + 60);
                p.Location = new Point(x, 10);
                x += p.Width + 14;
            }
            return f;
        }
        void Up5PlanPruefen(StundenplanFenster[] f)
        {
            Warten(300);
            foreach (var p in f)
            {
                if (!p.FormatauswahlFrei) throw new InvalidOperationException($"{p.Text}: Formatauswahl liegt am Fensterrand bzw. ist abgeschnitten.");
                if (p.Plan.SelbsttestBlockPruefung() is { Count: > 0 } fb) throw new InvalidOperationException($"{p.Text}: " + string.Join("; ", fb));
            }
        }
        // Daten vor den UP5-Schritten sichern: die Schritte ändern Unterricht und Stundenplan, danach wird der Stand wiederhergestellt
        var up5Sicherung = Dokument.Klonen(AppZustand.D);
        void Up5Zurueck() => AppZustand.Setzen(new Dokument(Dokument.Klonen(up5Sicherung)), false);
        Schritt("09e_Stundenplan_Menue", () =>
        {
            // Sprint 6: Menü „»“ im Stundenplanfenster wie im Vorbild
            var sp = StundenplanFenster.Neu(ElementTyp.Raum, AppZustand.D.Raeume.First().Id, false);
            sp.WindowState = FormWindowState.Normal;
            sp.Bounds = new Rectangle(60, 10, 380, 420);
            Warten(300);
            var m = sp.MehrMenue;
            var texte = m.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).ToList();
            var soll = new[] { "Unterrichts-Farben anzeigen", "Art fixieren", "Ansicht fixiert", "Seitenlayout", "Einstellungen", "Alle Elemente im Unterricht", "Schaltflächen hinzufügen oder entfernen" };
            if (!texte.SequenceEqual(soll)) throw new InvalidOperationException("Menü »: " + string.Join(" | ", texte));
            m.ShowDropDown();
            Warten(200);
            var art = (ToolStripMenuItem)m.DropDownItems[1];
            art.PerformClick();
            if (!sp.ArtFixiert) throw new InvalidOperationException("Art fixieren wirkt nicht");
            var knoepfe = (ToolStripMenuItem)m.DropDownItems[6];
            if (knoepfe.DropDownItems.Count < 8) throw new InvalidOperationException($"Schaltflächen-Liste unvollständig ({knoepfe.DropDownItems.Count})");
            var layout = (ToolStripMenuItem)m.DropDownItems[3];
            if (!m.DropDown.Visible) m.ShowDropDown();
            layout.ShowDropDown();
            if (layout.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).FirstOrDefault() != "Seitenansicht") throw new InvalidOperationException("Seitenlayout ▸ Seitenansicht fehlt");
            log.Add($"INFO  Menü »: {texte.Count} Einträge, {knoepfe.DropDownItems.Count} Schaltflächen ein-/ausblendbar");
        }, 3);
        Schritt("09e1_Stundenplan_Menue_zu", () => { foreach (var f in MdiChildren.OfType<StundenplanFenster>()) f.MehrMenue.HideDropDown(); }, 3);
        Schritt("UP5a_Kleine_Plaene_Stundenplanmodus", () =>
        {
            AppZustand.DatumSetzen(tag);
            var f = DreiPlaene(true, false);
            var p = AppZustand.Doc.Index.PlanVon(ElementTyp.Klasse, f[0].ElementId).OrderBy(x => x.Tag).ThenBy(x => x.Stunde).First();
            f[0].StundeAnklicken(p);
            Up5PlanPruefen(f);
            if (!f[0].LupeOffenText.StartsWith("+")) throw new InvalidOperationException($"Unterrichtsliste: Zeile mit offenen Stunden fehlt ({f[0].LupeOffenText}).");
            log.Add($"INFO  UP5a Unterrichtsliste: {f[0].LupeOffenText} / {f[1].LupeOffenText} / {f[2].LupeOffenText}");
        }, 3);
        Schritt("UP5b_Grosse_Plaene_Stundenplanmodus", () =>
        {
            AppZustand.DatumSetzen(tag);
            // unterrichtsfreier Tag in der Woche: senkrechter Text über die ganze Spalte
            var frei = Kalender.Montag(tag).AddDays(AppZustand.D.Zeitraster.TageProWoche - 1);
            if (frei == tag) frei = Kalender.Montag(tag);
            AppZustand.Doc.Aendern("Selbsttest Feiertag", AenderungsArt.Alles, x => x.Kalender.Add(new Kalendertag { Datum = frei, Feiertag = true }));
            var f = DreiPlaene(false, true);
            Up5PlanPruefen(f);
            Warten(300);
            var zeilen = f.SelectMany(p => p.Plan.GezeichneteZeilen).ToList();
            if (zeilen.Count == 0) throw new InvalidOperationException("Große Pläne: keine Stundentexte gezeichnet.");
            if (!f[0].Plan.FreiBeschriftungen.Any()) throw new InvalidOperationException("Unterrichtsfreier Tag wird im Regelplan nicht gezeigt.");
            log.Add($"INFO  UP5b Zeilen (Auszug): {string.Join(" | ", zeilen.Take(12))}; frei: {string.Join(",", f[0].Plan.FreiBeschriftungen.Select(b => b.Text))}");
        }, 3);
        Schritt("UP5b1_Feiertag_zurueck", () => { Up5Zurueck(); Max(StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, true)); }, 3);
        Schritt("UP5c_Nicht_verplant_Bloecke", () =>
        {
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            // Doppelstunde einer Klasse entplanen: erscheint als doppelt hoher Block unter „nicht verplant“
            var block = dd.Stundenplan.Select(p => Core.Planung.Planungsdienst.BlockVon(dd, p)).First(b => b.Count == 2 && ix.Unterrichte[b[0].UnterrichtId].KlassenIds.Any());
            var uid = block[0].UnterrichtId;
            var kl = ix.Unterrichte[uid].KlassenIds.First();
            var (bt, bs) = (block[0].Tag, block[0].Stunde);
            AppZustand.Doc.Aendern("Selbsttest entplanen", AenderungsArt.Stundenplan, x => { foreach (var p in x.Stundenplan.Where(p => p.UnterrichtId == uid && p.Tag == bt).ToList()) x.Stundenplan.Remove(p); });
            var f = Max(StundenplanFenster.Neu(ElementTyp.Klasse, kl, false));
            Warten(300);
            var bloecke = f.PoolBloecke;
            var doppel = bloecke.FirstOrDefault(b => b.Unterricht == uid && b.Laenge == 2);
            var einzel = bloecke.FirstOrDefault(b => b.Laenge == 1);
            if (doppel == default) throw new InvalidOperationException($"Entplante Doppelstunde fehlt unter „nicht verplant“ ({string.Join(",", bloecke)}).");
            if (einzel != default && doppel.Hoehe < einzel.Hoehe * 1.7) throw new InvalidOperationException("Doppelstunde ist nicht doppelt so hoch wie eine Einzelstunde.");
            // Block auf belegte Stunden ziehen: „Soll behindernder Unterricht entplant werden?“ → Ja
            var vorher = fragen.Count;
            dd = AppZustand.D;
            ix = AppZustand.Doc.Index;
            var unt = ix.Unterrichte[uid];
            var ziel = Enumerable.Range(0, dd.Zeitraster.TageProWoche).SelectMany(t => Enumerable.Range(1, dd.Zeitraster.StundenProTag - 1).Select(s => (t, s)))
                .First(z => z.t != bt && Core.Planung.Planungsdienst.Hindernisse(dd, ix, unt, z.t, z.s).Count > 0 && dd.Zeitraster.Verfuegbar(z.t, z.s + 1));
            f.SelbsttestAblegen($"unterricht:{uid}|2", ziel.t, ziel.s);
            Warten(300);
            if (fragen.Count == vorher || !fragen[^1].Contains("Soll behindernder Unterricht entplant werden?")) throw new InvalidOperationException($"Keine Abfrage „Soll behindernder Unterricht entplant werden?“ (U{uid} nach {ziel.t}/{ziel.s}, Hinweis: {hinweise.LastOrDefault()}, Plan: {string.Join(",", AppZustand.D.Stundenplan.Where(p => p.UnterrichtId == uid).Select(p => $"{p.Tag}/{p.Stunde}"))}).");
            var gesetzt = AppZustand.D.Stundenplan.Count(p => p.UnterrichtId == uid && p.Tag == ziel.t && (p.Stunde == ziel.s || p.Stunde == ziel.s + 1));
            if (gesetzt != 2) throw new InvalidOperationException($"Block wurde nicht als Doppelstunde gesetzt ({gesetzt}; Ziel {ziel.t}/{ziel.s}; Plan U{uid}: {string.Join(",", AppZustand.D.Stundenplan.Where(p => p.UnterrichtId == uid).Select(p => $"{p.Tag}/{p.Stunde}"))}; Frage: {fragen[^1].Replace("\n", " / ")}).");
            if (AppZustand.Doc.Index.PlanVon(ElementTyp.Klasse, kl).Any(p => p.Tag == ziel.t && p.Stunde == ziel.s && p.UnterrichtId != uid && !AppZustand.Doc.Index.Unterrichte[p.UnterrichtId].Band.Any()))
                log.Add("INFO  UP5c: parallele Stunde (Kopplung/anderer Lehrer) bleibt erhalten");
            log.Add($"INFO  UP5c Abfrage: {fragen[^1].Replace("\n", " / ")}");
            log.Add($"INFO  UP5c Pool danach: {string.Join(", ", f.PoolBloecke.Select(b => $"U{b.Unterricht}:{b.Laenge}"))}");
        }, 3);
        Schritt("UP5c1_Pool_zurueck", () => { Up5Zurueck(); Max(StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false)); }, 3);
        Schritt("UP5d_Unterrichtseingabe", () =>
        {
            var dd = AppZustand.D;
            var f = Max(Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)));
            var klasse = dd.Klassen.First();
            f.ElementSetzen(klasse.Id);
            Warten(200);
            // unbekanntes Kürzel: verständlicher Hinweis, Eingabe bleibt offen
            f.SelbsttestWaehlen(null, "Fach");
            if (f.SelbsttestEingabe("XYZQ")) throw new InvalidOperationException($"Unbekanntes Fach wurde übernommen (geprüft: {f.LetztePruefung}, Hinweis: {f.HinweisText}, Position {f.SelbsttestPosition}).");
            if (!f.HinweisText.Contains("nicht vorhanden")) throw new InvalidOperationException("Kein Hinweis zum unbekannten Fach: " + f.HinweisText);
            log.Add("INFO  UP5d Hinweis: " + f.HinweisText);
            f.SelbsttestVerwerfen();
            // neuer Unterricht: Fach (kleingeschrieben) → Enter springt zum Lehrer
            var anzahl = dd.Unterrichte.Count;
            var fach = dd.Faecher.First(x => !x.Bereitschaft);
            var lehrer = dd.Lehrer.First();
            f.SelbsttestWaehlen(null, "Fach");
            if (!f.SelbsttestEingabe(fach.Kurzname.ToLowerInvariant())) throw new InvalidOperationException("Fach wurde nicht übernommen: " + f.HinweisText);
            Warten(300);
            if (AppZustand.D.Unterrichte.Count != anzahl + 1) throw new InvalidOperationException("Kein neuer Unterricht angelegt.");
            var neu = AppZustand.D.Unterrichte.OrderBy(u => u.Id).Last();
            var pos = f.SelbsttestPosition;
            if (pos.Unterricht != neu.Id || pos.Spalte != "Lehrer") throw new InvalidOperationException($"Eingabeposition nach dem Anlegen: U{pos.Unterricht}/{pos.Spalte} statt U{neu.Id}/Lehrer.");
            if (!f.SelbsttestEingabe(lehrer.Kurzname.ToLowerInvariant())) throw new InvalidOperationException("Lehrer wurde nicht übernommen: " + f.HinweisText);
            Warten(300);
            neu = AppZustand.D.Unterrichte.First(u => u.Id == neu.Id);
            if (neu.Zeilen[0].FachId != fach.Id || neu.Zeilen[0].LehrerId != lehrer.Id || !neu.Zeilen[0].KlassenIds.Contains(klasse.Id))
                throw new InvalidOperationException("Neuer Unterricht unvollständig gespeichert.");
            log.Add($"INFO  UP5d neu: U{neu.Id} {AppZustand.Doc.Index.UnterrichtText(neu)}; Position {f.SelbsttestPosition}; Hinweis: {f.HinweisText}");
            f.SelbsttestKopplungszeile();
            Warten(300);
            if (AppZustand.D.Unterrichte.First(u => u.Id == neu.Id).Zeilen.Count != 2 || f.SelbsttestPosition.Zeile != 1) throw new InvalidOperationException("Kopplungszeile nicht angelegt bzw. nicht gewählt.");
            log.Add("INFO  UP5d Kopplung: " + f.HinweisText);
        }, 1);
        Schritt("UP5d1_Klasse_hinzufuegen_behindernd", () =>
        {
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            // verplanter Unterricht einer Klasse + andere Klasse, die zu einer verplanten Zeit schon Unterricht hat
            foreach (var u in dd.Unterrichte.Where(u => u.Zeilen.Count == 1 && u.KlassenIds.Count() == 1 && ix.Verplant(u) > 0 && string.IsNullOrEmpty(u.Band)))
            {
                var p = ix.PlanJeUnterricht[u.Id][0];
                var andere = dd.Stundenplan.Where(x => x.Tag == p.Tag && x.Stunde == p.Stunde && x.UnterrichtId != u.Id).Select(x => ix.Unterrichte[x.UnterrichtId])
                    .SelectMany(x => x.KlassenIds).Where(k => !u.KlassenIds.Contains(k)).Select(k => dd.Klassen.First(c => c.Id == k)).FirstOrDefault();
                if (andere is null) continue;
                var f = Max(Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)));
                f.ElementSetzen(u.KlassenIds.First());
                Warten(200);
                f.SelbsttestWaehlen(u.Id, "Klassen");
                var vorher = fragen.Count;
                f.SelbsttestEingabe(ix.KKlassen(u.KlassenIds) + "," + andere.Kurzname);
                Warten(400);
                if (fragen.Count == vorher || !fragen[^1].Contains("Soll behindernder Unterricht entplant werden?")) throw new InvalidOperationException("Keine Abfrage beim Hinzufügen einer Klasse.");
                if (!AppZustand.D.Unterrichte.First(x => x.Id == u.Id).KlassenIds.Contains(andere.Id)) throw new InvalidOperationException("Klasse wurde nicht hinzugefügt.");
                var nachher = new DatenIndex(AppZustand.D);
                if (Core.Planung.Planungsdienst.Hindernisse(AppZustand.D, nachher, nachher.Unterrichte[u.Id], p.Tag, p.Stunde).Count > 0) throw new InvalidOperationException("Behindernder Unterricht wurde nicht entplant.");
                log.Add($"INFO  UP5d1 Abfrage: {fragen[^1].Replace("\n", " / ")} // {f.HinweisText}");
                return;
            }
            throw new InvalidOperationException("Kein passender Unterricht für den Test gefunden.");
        }, 1);
        Schritt("UP5d2_Unterricht_zurueck", () =>
        {
            Up5Zurueck();
            Max(Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)));
        }, 1);
        Schritt("10_Uebersicht", () => Max(new UebersichtFenster(ElementTyp.Klasse)).Show(), 3);
        Schritt("11_Diagnose", () => Max(Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster())), 2);
        Schritt("12_Gewichtung", () => new GewichtungDialog().Show(this), 2);
        Schritt("13_Zeitraster", () => new ZeitrasterDialog().Show(this), 0);
        Schritt("14_Einstellungen", () => new EinstellungenDialog().Show(this), 0);
        Schritt("15_Ferien", () => Max(Oeffnen<FerienFenster>("ferien", () => new FerienFenster())), 0);
        Schritt("15a_Ferien_Kalender", () =>
        {
            // UP-04: Ferienfenster wie im Vorbild – eintägig unterrichtsfrei (Name „3.9“), Feiertag, Jahreskalender, Allgemeine Daten
            var f = Max(Oeffnen<FerienFenster>("ferien", () => new FerienFenster()));
            var dd = AppZustand.D;
            var frei = Kalender.Tage(dd.Einstellungen.SchuljahrVon, dd.Einstellungen.SchuljahrBis).First(t => t.DayOfWeek == DayOfWeek.Friday && Kalender.IstUnterrichtstag(dd, t));
            var vorher = f.Entwurf.Count;
            f.Neu(frei, frei, $"{frei.Day}.{frei.Month}", $"{frei.Day}.{frei.Month}.{frei.Year}");
            if (f.Entwurf.Count != vorher + 1) throw new InvalidOperationException("Ferien: neuer Eintrag fehlt im Entwurf.");
            if (dd.Ferien.Any(x => x.Von == frei && x.Bis == frei && x.Kurzname == $"{frei.Day}.{frei.Month}")) throw new InvalidOperationException("Ferien: Entwurf wurde vor der Übernahme gespeichert.");
            if (!f.Uebernehmen()) throw new InvalidOperationException("Ferien: Übernahme fehlgeschlagen.");
            if (!AppZustand.D.Ferien.Any(x => x.Von == frei && x.Bis == frei)) throw new InvalidOperationException("Ferien: Übernahme hat den Eintrag nicht gespeichert.");
            if (Kalender.IstUnterrichtstag(AppZustand.D, frei)) throw new InvalidOperationException("Ferien: Tag ist nach der Übernahme noch Unterrichtstag.");
            f.DatumWaehlen(frei);
        }, 0);
        Schritt("15b_Kurzname_Neuanlegen", () =>
        {
            // Neuanlegen von Stammdaten: Dialog „Kurzname“ (Name, Langname) wie im Vorbild
            Max(Oeffnen<StammdatenFenster>("st:Schueler", () => new StammdatenFenster(ElementTyp.Schueler)));
            var vorschlag = KurznameDialog.Vorschlag(AppZustand.D.AlleElemente(ElementTyp.Schueler).Select(e => e.Kurzname));
            var dlg = KurznameDialog.Erzeugen(vorschlag.Length > 0 ? vorschlag : "S68", "Student 80", out _, out _);
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.Show(this);
            log.Add($"INFO  Kurzname-Dialog: Vorschlag „{vorschlag}“");
        }, 1);
        Schritt("16_Vertretungsmodus", () => { VertretungsmodusUmschalten(); Warten(300); KopfPruefen("Vertretungsplanung"); }, -1, false);
        Schritt("17_Vertretungen", () => Max(FensterOeffnen("vertr")!), -1);
        Schritt("17a_Vertretungen_Eingabe_Offene", () =>
        {
            // Vertreter und Text unter dem Filter „Offene Vertretungen“ eingeben: Zeile bleibt bis zum Zeilenwechsel, Fokus bleibt
            var f = Max(FensterOeffnen("vertr")!) as VertretungenFenster ?? throw new InvalidOperationException("Vertretungsfenster fehlt");
            Warten(400);
            log.Add("INFO  " + f.SelbsttestBearbeiten(() => Warten(400)));
        }, -1);
        Schritt("17a1_Vertreterliste_breit", () =>
        {
            // Auswahllisten in Tabellenzellen öffnen immer breit (auch beim wiederholten Bearbeiten)
            var f = Max(FensterOeffnen("vertr")!) as VertretungenFenster ?? throw new InvalidOperationException("Vertretungsfenster fehlt");
            Warten(400);
            log.Add("INFO  " + f.SelbsttestListenbreite(() => Warten(300)));
        }, -1);
        Schritt("17b_Stundenplan_Zeitbereich", () =>
        {
            // Zeitbereich „Periode“ im Vertretungsmodus (Regelplan nur zur Ansicht), danach wieder „Woche“
            var sp = Max(StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false));
            sp.SelbsttestZeitbereich(false);
            Warten(300);
            sp.SelbsttestZeitbereich(true);
        }, -1);
        Schritt("17b1_Stundenplan_Zeitbereich_Tag", () =>
        {
            // Zeitbereich „Tag“: nur der gewählte Tag über die ganze Breite
            var sp = Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false));
            sp.SelbsttestZeitbereich(true, true);
        }, -1);
        // offene Vertretung (möglichst mit Vorziehungen) für den Vertretungsvorschlag
        Vertretung? OffeneVertretung()
        {
            var liste = AppZustand.D.Vertretungen.Where(v => v.Datum == AppZustand.Datum).OrderByDescending(v => v.Offen && v.Art == VertretungsArt.Vertretung).ThenByDescending(v => v.Offen).ThenBy(v => v.Stunde).ToList();
            return liste.Where(v => v.Offen && v.Art == VertretungsArt.Vertretung).FirstOrDefault(v => Core.Vertretungsplanung.VertretungsVorschlag.Vorziehungen(AppZustand.D, v).Count > 0) ?? liste.FirstOrDefault();
        }
        Schritt("17c_Vertretungen_Tausch", () =>
        {
            // Stundentausch zweier Stunden einer Klasse: wie im Vorbild je Position eine Zeile „Tausch“, geänderte Felder magenta
            var dd = AppZustand.D;
            var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(dd, tag);
            bool Frei(Core.Vertretungsplanung.TagesEintrag e) => e.Status == Core.Vertretungsplanung.TagesStatus.Regulaer && e.Unterricht is { Zeilen.Count: 1 } && e.KlassenIds.Count == 1 && !e.LehrerIds.Contains(krank.Id);
            var paar = (from k in dd.Klassen
                        from a in tp.VonKlasse(k.Id).Where(Frei)
                        from b in tp.VonKlasse(k.Id).Where(Frei)
                        where b.Stunde > a.Stunde && b.Unterricht!.Id != a.Unterricht!.Id && !a.LehrerIds.SequenceEqual(b.LehrerIds)
                        select (a, b))
                .FirstOrDefault(p => Core.Vertretungsplanung.Tagesverlegung.Zielstatus(dd, tp, p.a.Stunde, p.a.Unterricht!.Id, null, tp, p.b.Stunde) == Core.Planung.Feldstatus.TauschMoeglich);
            if (paar.a is null) throw new InvalidOperationException("Kein Stundentausch möglich.");
            Core.Vertretungsplanung.Vertretungen.Tauschen(dd, paar.a.Unterricht!.Id, tag, paar.a.Stunde, paar.b.Unterricht!.Id, tag, paar.b.Stunde);
            var f = Max(FensterOeffnen("vertr")!) as VertretungenFenster ?? throw new InvalidOperationException("Vertretungsfenster fehlt");
            Warten(400);
            log.Add("INFO  " + f.SelbsttestTausch());
        }, -1);
        Schritt("18_Vertretungsvorschlag", () =>
        {
            AppZustand.VertretungWaehlen(OffeneVertretung());
            var f = Max(FensterOeffnen("vorschlag")!) as VertretungsvorschlagFenster ?? throw new InvalidOperationException("Vorschlagsfenster fehlt");
            Warten(300);
            log.Add("INFO  " + f.SelbsttestPruefen());
        }, -1);
        Schritt("18a_Vertretungsvorschlag_Betreuungen", () =>
        {
            AppZustand.VertretungWaehlen(OffeneVertretung());
            var f = Max(FensterOeffnen("vorschlag")!) as VertretungsvorschlagFenster ?? throw new InvalidOperationException("Vorschlagsfenster fehlt");
            f.SelbsttestReiter(1);
        }, -1);
        Schritt("18b_Vertretungsvorschlag_Raeume_Vorziehungen", () =>
        {
            AppZustand.VertretungWaehlen(OffeneVertretung());
            var f = Max(FensterOeffnen("vorschlag")!) as VertretungsvorschlagFenster ?? throw new InvalidOperationException("Vorschlagsfenster fehlt");
            f.SelbsttestReiter(2);
            f.SelbsttestAufklappen(true, true);
        }, -1);
        Schritt("19_Absenzen", () => Max(FensterOeffnen("abw")!), -1);
        Schritt("19a_Tagesplan_Lehrer", () => Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false)), -1);
        Schritt("27c_Leere_Zelle_Synchronisation_tagesaktuell", () => LeereZellePruefen(true), -1);
        Schritt("19a1_Kleine_Stundenplaene", () =>
        {
            var vertr = FensterOeffnen("vertr")!;
            var plaene = KleinePlaeneOeffnen();
            var mdi = Controls.OfType<MdiClient>().First().ClientSize;
            vertr.Bounds = new Rectangle(0, 0, Math.Max(300, mdi.Width - plaene.Sum(p => p.Width)), mdi.Height);
            // zweite Reihe: kleiner Plan mit Stundenplanlupe
            var mitLupe = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.Skip(1).First().Id, false, true);
            mitLupe.Location = new Point(plaene[0].Left, plaene.Max(p => p.Bottom));
            if (plaene.Any(p => !p.Klein || p.Width > 220)) throw new InvalidOperationException("Kleine Stundenpläne sind nicht klein.");
        }, -1);
        Schritt("19a2_Tauschpfeil_Vertretung", () =>
        {
            var dd = AppZustand.D;
            var montag = Kalender.Montag(tag);
            foreach (var k in dd.Klassen)
            {
                var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(dd, tag);
                var eintraege = tp.VonKlasse(k.Id).Where(e => e.Findetstatt && e.Unterricht != null).ToList();
                foreach (var a in eintraege)
                    foreach (var b in eintraege.Where(b => b.Stunde != a.Stunde && b.Unterricht!.Id != a.Unterricht!.Id))
                    {
                        if (Core.Vertretungsplanung.Tagesverlegung.Zielstatus(dd, tp, a.Stunde, a.Unterricht!.Id, null, tp, b.Stunde) != Core.Planung.Feldstatus.TauschMoeglich) continue;
                        var f = Max(StundenplanFenster.Neu(ElementTyp.Klasse, k.Id, false));
                        f.Plan.Ziehvorschau($"tag:{tag:yyyyMMdd}:{a.Stunde}:{a.Unterricht.Id}:-1:1", tag.DayNumber - montag.DayNumber, b.Stunde);
                        return;
                    }
            }
            throw new InvalidOperationException("Kein Tausch im tagesaktuellen Plan gefunden.");
        }, -1);
        // UP-01: 14 Stunden pro Tag (mit Pausenzeilen) – Miniplan und Stundenplan scrollen statt zu quetschen, Blöcke nie über Pausen
        var stundenVorher = AppZustand.D.Zeitraster.StundenProTag;
        void VierzehnStunden(bool an) => AppZustand.Doc.Aendern(an ? "14 Stunden (Selbsttest)" : "Stunden zurück (Selbsttest)", AenderungsArt.Alles, x =>
        {
            var z = x.Zeitraster;
            z.StundenProTag = an ? 14 : stundenVorher;
            for (var nr = 1; nr <= 14; nr++)
                if (an && z.Zeit(nr) is null)
                {
                    var beginn = (z.Zeit(nr - 1)?.Ende ?? new TimeOnly(7, 0)).AddMinutes(5);
                    z.Zeiten.Add(new Stundenzeit { Nr = nr, Beginn = beginn, Ende = beginn.AddMinutes(45) });
                }
            if (an && !z.Pausen.Any(p => p.NachStunde == 0)) z.Pausen.Add(new Pause { NachStunde = 0, Bezeichnung = "FA", DauerMinuten = 10 });
            if (!an) z.Pausen.RemoveAll(p => p.NachStunde == 0 && p.Bezeichnung == "FA");
        });
        Schritt("19a3_Miniplan_14_Stunden", () =>
        {
            VierzehnStunden(true);
            var mdi = Controls.OfType<MdiClient>().First().ClientSize;
            var vertr = FensterOeffnen("vertr")!;
            vertr.Bounds = new Rectangle(0, 0, mdi.Width - 420, mdi.Height);
            // kleiner Lehrerplan wie im Fehlerbild: niedriges Fenster → Mindestzeilenhöhe und Rollbalken
            var klein = StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false, true);
            klein.Bounds = new Rectangle(mdi.Width - 420, 0, 200, 300);
            var hoch = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false, true);
            hoch.Bounds = new Rectangle(mdi.Width - 216, 0, 200, Math.Min(mdi.Height, hoch.Height));
            Warten(300);
            if (!klein.Klein) throw new InvalidOperationException("Miniplan ist nicht klein.");
            if (!klein.PlanScrollbar) throw new InvalidOperationException("Miniplan mit 14 Stunden scrollt nicht (Zeilen werden gequetscht).");
            var fehler = klein.Plan.SelbsttestBlockPruefung().Concat(hoch.Plan.SelbsttestBlockPruefung()).ToList();
            if (fehler.Count > 0) throw new InvalidOperationException("Miniplan-Blöcke: " + string.Join("; ", fehler));
            log.Add($"INFO  Miniplan 14 Stunden: Planhöhe {klein.Plan.Height} px, Mindesthöhe {klein.Plan.MindestHoehe} px, Zeile ≥ {klein.Plan.MindestZeile} px, scrollbar");
        }, -1);
        Schritt("19a4_Miniplan_14_Stunden_gescrollt", () =>
        {
            var mdi = Controls.OfType<MdiClient>().First().ClientSize;
            var klein = StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false, true);
            klein.Bounds = new Rectangle(10, 10, 200, 300);
            var gross = StundenplanFenster.Neu(ElementTyp.Klasse, AppZustand.D.Klassen.First().Id, false);
            gross.Bounds = new Rectangle(220, 10, Math.Min(760, mdi.Width - 230), Math.Min(420, mdi.Height - 20));
            Warten(300);
            klein.SelbsttestScrollen(10000);
            if (!gross.PlanScrollbar) throw new InvalidOperationException("Stundenplan mit 14 Stunden scrollt nicht.");
            gross.SelbsttestScrollen(10000);
            var fehler = klein.Plan.SelbsttestBlockPruefung().Concat(gross.Plan.SelbsttestBlockPruefung()).ToList();
            if (fehler.Count > 0) throw new InvalidOperationException("Stundenplan-Blöcke: " + string.Join("; ", fehler));
        }, -1);
        Schritt("19a5_Stunden_zurueck", () => { VierzehnStunden(false); Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false)); }, -1);
        Schritt("19b_Pausenaufsichten", () => Max(FensterOeffnen("pause")!), -1);
        Schritt("19c_Statistik", () => Max(FensterOeffnen("stat")!), -1);
        Schritt("19d_Kalender_Bereitschaften", () => { FensterOeffnen("kal"); FensterOeffnen("bereit"); FensterOeffnen("vorm"); LayoutMdi(MdiLayout.TileVertical); }, -1);
        Schritt("19d1_Veranstaltungen", () => Max(FensterOeffnen("veranst")!), -1);
        Schritt("19d2_Klausuren", () => Max(FensterOeffnen("klaus")!), -1);
        Schritt("27a_Veranstaltung_Schnellauswahl", () =>
        {
            // Sprint 27: in einer Veranstaltung schnell alle Lehrkräfte / Klassen / Räume, nach Gruppe oder alles aus den Stunden hinzufügen
            var sicherung = Dokument.Klonen(AppZustand.D);
            try
            {
                var dd = AppZustand.D;
                var va = dd.Veranstaltungen.First(v => v.Text == "Sportwoche");
                var f = (VeranstaltungenFenster)FensterOeffnen("veranst")!;
                Max(f);
                Warten(150);
                f.SelbsttestWaehlen(va.Id, "R");
                var menue = f.SelbsttestMenue(null);
                foreach (var erwartet in new[] { "Lehrkräfte > Alle Lehrkräfte", "Klassen (entfällt) > Alle Klassen", "Räume > Alle Räume", "Klassen (entfällt) > Nach Jahrgang", "Alle aus den Stunden der Veranstaltung übernehmen" })
                    if (!menue.Any(m => m.StartsWith(erwartet))) throw new InvalidOperationException($"Schnellauswahl ohne „{erwartet}“: {string.Join(" | ", menue)}");
                if (!f.SelbsttestMenue(Core.Vertretungsplanung.VeranstaltungsAuswahl.Feld.Lehrer).Any(m => m.StartsWith("Lehrkräfte der Klassen")))
                    throw new InvalidOperationException("Kontextmenü Lehrer ohne „Lehrkräfte der Klassen“.");
                // alle Räume
                f.Schnell(Core.Vertretungsplanung.VeranstaltungsAuswahl.Feld.Raeume, Core.Vertretungsplanung.VeranstaltungsAuswahl.Alle(dd, ElementTyp.Raum));
                Warten(150);
                var nachher = AppZustand.D.Veranstaltungen.First(v => v.Id == va.Id);
                if (nachher.RaumIds.Count != AppZustand.D.Raeume.Count(r => !r.Ignorieren)) throw new InvalidOperationException($"Alle Räume: {nachher.RaumIds.Count} statt {AppZustand.D.Raeume.Count}.");
                // Jahrgang 6 dazu (Gruppe)
                var jg = Core.Vertretungsplanung.VeranstaltungsAuswahl.Gruppen(AppZustand.D, ElementTyp.Klasse, nachher).First(g => g.Bereich == "Jahrgang" && g.Name != "Jahrgang 5");
                f.SelbsttestWaehlen(va.Id, "K");
                f.Schnell(Core.Vertretungsplanung.VeranstaltungsAuswahl.Feld.Klassen, jg.Ids);
                Warten(150);
                nachher = AppZustand.D.Veranstaltungen.First(v => v.Id == va.Id);
                if (!jg.Ids.All(nachher.KlassenIds.Contains)) throw new InvalidOperationException($"{jg.Name} wurde nicht übernommen.");
                // alles aus den Stunden der Veranstaltung
                f.SelbsttestWaehlen(va.Id, "L");
                var lehrerVorher = nachher.LehrerIds.Count;
                f.AusStundenUebernehmen();
                Warten(150);
                nachher = AppZustand.D.Veranstaltungen.First(v => v.Id == va.Id);
                if (nachher.LehrerIds.Count <= lehrerVorher) throw new InvalidOperationException("Aus den Stunden wurden keine Lehrkräfte übernommen.");
                // Auswahlliste der Spalte Lehrer: „Alle“ wählen und übernehmen
                f.SelbsttestWaehlen(va.Id, "L");
                f.SelbsttestAuswahlZeigen("L");
                Warten(150);
                if (Steuerelemente.MehrfachAuswahl.Offen is null) throw new InvalidOperationException("Auswahlliste Lehrer öffnet nicht.");
                if (Steuerelemente.MehrfachAuswahl.Offen.Parent?.Controls.OfType<FlowLayoutPanel>().FirstOrDefault()?.Controls.OfType<LinkLabel>().Select(l => l.Text).Take(2).SequenceEqual(new[] { "Alle", "Keine" }) != true)
                    throw new InvalidOperationException("Auswahlliste ohne „Alle“/„Keine“.");
                Steuerelemente.MehrfachAuswahl.SelbsttestAlleUebernehmen(true);
                Warten(200);
                nachher = AppZustand.D.Veranstaltungen.First(v => v.Id == va.Id);
                var alleLehrer = AppZustand.D.Lehrer.Count(l => !l.Ignorieren);
                // offener Platz (???) wird mit der ersten neuen Lehrkraft besetzt, die übrigen nehmen zusätzlich teil
                var teilnehmer = nachher.LehrerIds.Concat(nachher.Ersatz.Where(x => x.ErsatzId.HasValue).Select(x => x.ErsatzId!.Value)).Distinct().Count();
                if (teilnehmer < alleLehrer) throw new InvalidOperationException($"„Alle“ in der Auswahlliste: {teilnehmer} von {alleLehrer} Lehrkräften.");
                // „Keine“ leert die Spalte Räume
                f.SelbsttestWaehlen(va.Id, "R");
                f.SelbsttestAuswahlZeigen("R");
                Warten(100);
                Steuerelemente.MehrfachAuswahl.SelbsttestAlleUebernehmen(false);
                Warten(200);
                if (AppZustand.D.Veranstaltungen.First(v => v.Id == va.Id).RaumIds.Count != 0) throw new InvalidOperationException("„Keine“ hat die Räume nicht geleert.");
                // aus einer im Stundenplan gewählten Stunde: neue Veranstaltung in der leeren Zeile
                var r = Core.Vertretungsplanung.Tagesplan.Regulaer(AppZustand.D, tag).First(x => x.LehrerId.HasValue && x.KlassenIds.Count > 0);
                var anzahl = AppZustand.D.Veranstaltungen.Count;
                f.SelbsttestNeueZeile();
                f.AusStundeUebernehmen(r.Unterricht, r.Planstunde);
                Warten(150);
                if (AppZustand.D.Veranstaltungen.Count != anzahl + 1) throw new InvalidOperationException("Aus der gewählten Stunde wurde keine Veranstaltung angelegt.");
                var neu = AppZustand.D.Veranstaltungen.OrderBy(v => v.Id).Last();
                if (!r.Unterricht.LehrerIds.All(neu.LehrerIds.Contains) || !r.Unterricht.KlassenIds.All(neu.KlassenIds.Contains))
                    throw new InvalidOperationException("Aus der gewählten Stunde: Klassen bzw. Lehrkräfte fehlen.");
                f.SelbsttestWaehlen(va.Id, "L");
                log.Add($"INFO  Veranstaltung Schnellauswahl: {nachher.LehrerIds.Count} Lehrkräfte, {nachher.KlassenIds.Count} Klassen; aus Stunde U{r.Unterricht.Id} → VA {neu.Id}");
            }
            finally
            {
                Warten(100);
                AppZustand.Setzen(new Dokument(Dokument.Klonen(sicherung)), false);
            }
        }, -1);
        Schritt("27s_Veranstaltung_alle_Lehrkraefte", () =>
        {
            // Lehrkräftekonferenz bzw. pädagogischer Tag mit allen Lehrkräften und allen Klassen: eine Zeile statt Hunderter,
            // „Alle Lehrkräfte“/„Alle Klassen“ statt endloser Listen – im Vertretungs- und Veranstaltungsfenster, im Aushang und im Export
            var sicherung = Dokument.Klonen(AppZustand.D);
            try
            {
                var vaId = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                AppZustand.Doc.Aendern("Lehrkräftekonferenz (Selbsttest)", AenderungsArt.Vertretung, x =>
                {
                    var va = new Veranstaltung
                    {
                        Id = x.NeueId(), Text = "Lehrkräftekonferenz", Von = tag, Bis = tag, StundeVon = 1, StundeBis = 99,
                        KlassenIds = Core.Vertretungsplanung.VeranstaltungsAuswahl.Alle(x, ElementTyp.Klasse, tag, tag),
                        LehrerIds = Core.Vertretungsplanung.VeranstaltungsAuswahl.Alle(x, ElementTyp.Lehrer, tag, tag),
                    };
                    x.Veranstaltungen.Add(va);
                    vaId = va.Id;
                    Core.Vertretungsplanung.Vertretungen.Aktualisieren(x, tag);
                });
                var anlegen = sw.ElapsedMilliseconds;
                var vf = Max(FensterOeffnen("vertr")!) as VertretungenFenster ?? throw new InvalidOperationException("Vertretungsfenster fehlt");
                Warten(300);
                log.Add("INFO  " + vf.SelbsttestVeranstaltungAlle(vaId, tag, () => Warten(300)));
                var f = (VeranstaltungenFenster)FensterOeffnen("veranst")!;
                Max(f);
                Warten(200);
                var lehrerText = f.SelbsttestZelltext(vaId, "L");
                var klassenText = f.SelbsttestZelltext(vaId, "K");
                if (lehrerText != "Alle Lehrkräfte" || klassenText != "Alle Klassen")
                    throw new InvalidOperationException($"Veranstaltungsfenster: Lehrer „{lehrerText}“, Klassen „{klassenText}“ statt „Alle …“.");
                // Aushang: je Stunde eine Zeile „Alle Lehrkräfte“ statt einer je Lehrkraft
                var aushang = Core.Vertretungsplanung.Vertretungsplan.Zeilen(AppZustand.D, tag).Where(z => z.VeranstaltungId == vaId).ToList();
                if (aushang.Count == 0 || aushang.Count > AppZustand.D.Zeitraster.StundenProTag || aushang.Any(z => z.Vertreter != "Alle Lehrkräfte" || z.Klassen != "Alle Klassen"))
                    throw new InvalidOperationException($"Aushang: {aushang.Count} Zeile(n) der Veranstaltung: {string.Join(" | ", aushang.Take(3).Select(z => $"{z.Stunde} {z.Vertreter} {z.Klassen}"))}");
                // Export: je Stunde eine Veranstaltungsstunde und eine Vertretungszeile, Termin mit „alle“-Kennzeichen
                var paket = Core.Austausch.UntPlanWebExport.PaketErstellen(AppZustand.D, tag, new Core.Austausch.WebExportOptionen { TageVoraus = 1, TageZurueck = 0 });
                var tagIso = tag.ToString("yyyy-MM-dd");
                var stunden = paket.Tage.Single(t => t.Datum == tagIso).Stunden.Count(x => x.Veranstaltung == vaId && x.Status == "zusaetzlich");
                var zeilenExport = paket.Vertretungen.Count(x => x.Veranstaltung == vaId && x.Datum == tagIso);
                var termin = paket.Termine.Single(t => t.Id == vaId);
                if (stunden != aushang.Count || zeilenExport != aushang.Count || !termin.AlleLehrer || !termin.AlleKlassen)
                    throw new InvalidOperationException($"Export: {stunden} Stunden, {zeilenExport} Vertretungszeilen, Termin alle Lehrer {termin.AlleLehrer}, alle Klassen {termin.AlleKlassen}.");
                log.Add($"INFO  Lehrkräftekonferenz: {AppZustand.D.Vertretungen.Count(v => v.Datum == tag && v.VeranstaltungId == vaId)} Zeilen in den Daten, Aushang {aushang.Count}, Export {stunden} Stunden; anlegen {anlegen} ms");
            }
            finally
            {
                Warten(100);
                AppZustand.Setzen(new Dokument(Dokument.Klonen(sicherung)), false);
            }
        }, -1);
        Schritt("19d2a_Klausur_loeschen_Liste", () =>
        {
            // gelöschte Klausur verschwindet sofort aus der Liste – auch wenn eine Zelle gerade bearbeitet wird
            var kl = AppZustand.D.Klassen[0].Id;
            var a = new Klausur { Name = "Selbsttest A", Datum = tag, StundeVon = 1, StundeBis = 1, KlassenIds = { kl } };
            var b = new Klausur { Name = "Selbsttest B", Datum = tag, StundeVon = 2, StundeBis = 2, KlassenIds = { kl } };
            AppZustand.Doc.Aendern("Klausuren (Selbsttest)", AenderungsArt.Vertretung, x => { a.Id = x.NeueId(); x.Klausuren.Add(a); b.Id = x.NeueId(); x.Klausuren.Add(b); });
            var f = (KlausurenFenster)FensterOeffnen("klaus")!;
            Warten(100);
            if (f.SelbsttestLoeschen(a.Id) != 0) throw new InvalidOperationException("Gelöschte Klausur steht noch in der Liste.");
            if (AppZustand.D.Klausuren.Any(k => k.Id == a.Id)) throw new InvalidOperationException("Klausur wurde nicht gelöscht.");
            if (f.SelbsttestAktuelleZeile < 0) throw new InvalidOperationException("Nach dem Löschen ist keine Zeile ausgewählt.");
            if (f.SelbsttestLoeschen(b.Id) != 0) throw new InvalidOperationException("Zweite gelöschte Klausur steht noch in der Liste.");
        }, -1);
        Schritt("19d3_Raum_zuordnen_Vertretung", () =>
        {
            var r = Core.Vertretungsplanung.Tagesplan.Regulaer(AppZustand.D, tag).First(x => x.RaumId.HasValue);
            RaumZuordnenVertretungDialog.Anzeigen(this, tag, r.Stunde, r.Unterricht.Id, r.ZeilenIndex);
        }, -1);
        Schritt("19d4_Vertretungsautomatik", () => VertretungsautomatikDialog.Anzeigen(this, tag, tag), -1);
        Schritt("19d5_Stunde_aendern", () =>
        {
            var e = Core.Vertretungsplanung.Tagesplan.Berechnen(AppZustand.D, tag).Eintraege.First(x => x.Findetstatt && x.Unterricht != null);
            StundeAendernDialog.Anzeigen(this, tag, e.Stunde, new[] { e });
        }, -1);
        Schritt("19d6_Stundenplanaenderungen_UntPlanWeb", () =>
        {
            var r = Core.Vertretungsplanung.Tagesplan.Regulaer(AppZustand.D, tag).First(x => x.LehrerId.HasValue);
            var ix = AppZustand.Doc.Index;
            new StundenplanAenderungenDialog(new[]
            {
                new Web.Contracts.WebRueckmeldung { Id = "B510", Typ = Web.Contracts.WebRueckmeldungsTypen.Raumbuchung, Absender = "admin", Lehrer = ix.KLehrer(r.LehrerId), Von = tag.ToString("yyyy-MM-dd"), StundeVon = r.Stunde, Raum = AppZustand.D.Raeume[0].Kurzname },
                new Web.Contracts.WebRueckmeldung { Id = "E68", Typ = Web.Contracts.WebRueckmeldungsTypen.Klausur, Absender = "admin", Lehrer = ix.KLehrer(r.LehrerId), Von = tag.ToString("yyyy-MM-dd"), StundeVon = 4, Klassen = { AppZustand.D.Klassen[0].Kurzname }, Titel = "Mathe Schularbeit" },
            }).Show(this);
        }, -1);
        Schritt("19d7_Tagestext_formatiert", () =>
        {
            // UP-02: Dialog „Tagestexte“ mit B K U und Farbe; gespeichert als Rich Text und bereinigtes HTML (Export: textHtml)
            var kal = FensterOeffnen("kal")!;
            kal.WindowState = FormWindowState.Normal;
            kal.Bounds = new Rectangle(0, 0, 640, 560);
            var t = new Tagestext { Datum = AppZustand.Datum };
            var dlg = new TagestextFormatDialog(AppZustand.Datum, t) { StartPosition = FormStartPosition.Manual, Location = new Point(PointToScreen(Point.Empty).X + 660, PointToScreen(Point.Empty).Y + 160) };
            dlg.Show(this);
            var rtb = dlg.Textfeld;
            rtb.Text = "Heute Lehrerkonferenz um 14:15 Uhr\nBitte Klassenbücher mitbringen.";
            rtb.Select(0, 5); dlg.StilUmschalten(FontStyle.Bold);
            rtb.Select(6, 15); dlg.StilUmschalten(FontStyle.Italic);
            rtb.Select(25, 9); dlg.FarbeSetzen(Color.Red); dlg.StilUmschalten(FontStyle.Underline);
            rtb.Select(rtb.TextLength, 0);
            dlg.Uebernehmen();
            if (!dlg.Html.Contains("<b>Heute</b>") || !dlg.Html.Contains("<i>") || !dlg.Html.Contains("color:#ff0000") || !dlg.Html.Contains("<u>"))
                throw new InvalidOperationException("Tagestext-HTML unvollständig: " + dlg.Html);
            if (dlg.Rtf.Length == 0) throw new InvalidOperationException("Tagestext: Rich Text fehlt.");
            var neu = new Tagestext { Datum = AppZustand.Datum, Text = dlg.Klartext, TextRtf = dlg.Rtf, TextHtml = dlg.Html };
            AppZustand.Doc.Aendern("Tagestext formatiert", AenderungsArt.Vertretung, x => x.Tagestexte.Add(neu));
            var paket = Core.Austausch.UntPlanWebExport.PaketErstellen(AppZustand.D, AppZustand.Datum);
            var web = paket.Tagestexte.FirstOrDefault(x => x.Text == neu.Text) ?? throw new InvalidOperationException("Export: formatierter Tagestext fehlt.");
            if (web.TextHtml is not { } html || !html.Contains("<b>Heute</b>")) throw new InvalidOperationException("Export: TextHtml ohne Formatierung: " + web.TextHtml);
            log.Add("INFO  Tagestext-HTML: " + dlg.Html);
        }, -1);
        Schritt("19d8_Kalendertag_unterrichtsfrei", () =>
        {
            // UP-03: Kalendertag wie im Vorbild (Normaler Schultag / Feiertag / Unterrichtsfrei [Grund] / Nur diese Stunden …, Notiz)
            var dd = AppZustand.D;
            var montag = Kalender.Montag(tag);
            var tage = Enumerable.Range(0, 5).Select(i => montag.AddDays(i)).Where(t => t != tag).ToList();
            var grund = dd.Abwesenheitsgruende.FirstOrDefault();
            AppZustand.Doc.Aendern("Unterrichtsfrei (Selbsttest)", AenderungsArt.Vertretung, x =>
            {
                x.Kalender.RemoveAll(k => tage.Contains(k.Datum));
                x.Kalender.Add(new Kalendertag { Datum = tage[0], Unterrichtsfrei = true });
                var lang = new Abwesenheitsgrund { Id = x.NeueId(), Kurzname = "PTF", Langname = "Pädagogischer Tag mit ganztägiger Fortbildung des Kollegiums" };
                x.Abwesenheitsgruende.Add(lang);
                x.Kalender.Add(new Kalendertag { Datum = tage[1], Unterrichtsfrei = true, GrundId = lang.Id });
                if (grund != null) x.Kalender.Add(new Kalendertag { Datum = tage[2], FreieStunden = "5-6", Notiz = "Sportfest" });
                Kalender.CacheLeeren();
                foreach (var t in tage) Core.Vertretungsplanung.Vertretungen.Aktualisieren(x, t);
            });
            AppZustand.DatumSetzen(tage[1]);
            var kal = FensterOeffnen("kal") as KalenderFenster ?? throw new InvalidOperationException("Kalenderfenster fehlt");
            kal.WindowState = FormWindowState.Normal;
            kal.Bounds = new Rectangle(0, 0, 600, 560);
            kal.SelbsttestKalendertag();
            var sp = StundenplanFenster.Neu(ElementTyp.Klasse, dd.Klassen.First().Id, false);
            sp.WindowState = FormWindowState.Normal;
            var mdi = Controls.OfType<MdiClient>().First().ClientSize;
            // feste Größe statt Bildschirmrest: auf dem kleinen Windows-Runner (1024 px) wäre die Spalte zu schmal für das volle Datum
            sp.Bounds = new Rectangle(610, 0, 1000, 640);
            var klein = StundenplanFenster.Neu(ElementTyp.Klasse, dd.Klassen.First().Id, false, true);
            klein.WindowState = FormWindowState.Normal;
            klein.Location = new Point(610, Math.Max(0, Math.Min(640, mdi.Height - klein.Height)));
            Warten(400);
            // unabhängig von Fensterüberdeckung zeichnen (Windows schickt verdeckten MDI-Fenstern kein WM_PAINT)
            foreach (var pa in new[] { sp.Plan, klein.Plan })
            {
                using var bild = new Bitmap(Math.Max(1, pa.Width), Math.Max(1, pa.Height));
                pa.DrawToBitmap(bild, new Rectangle(Point.Empty, bild.Size));
            }
            var texte = sp.Plan.FreiBeschriftungen.Select(x => x.Text).ToList();
            var kurz = $"{tage[0].Day}.{tage[0].Month}.{tage[0].Year}";
            if (!texte.Contains(kurz)) throw new InvalidOperationException($"Unterrichtsfrei ohne Bezeichnung zeigt nicht das Datum {kurz}: {string.Join(" | ", texte)}");
            if (!texte.Any(x => x.StartsWith("Päd") && x.EndsWith("…"))) throw new InvalidOperationException("Überlange Bezeichnung wird nicht mit „…“ gekürzt: " + string.Join(" | ", texte));
            if (!klein.Plan.FreiBeschriftungen.Any(x => x.Text.EndsWith("…"))) throw new InvalidOperationException("Miniplan kürzt die Bezeichnung nicht: " + string.Join(" | ", klein.Plan.FreiBeschriftungen.Select(x => x.Text)));
            log.Add("INFO  Unterrichtsfrei: " + string.Join(" | ", texte) + " / klein: " + string.Join(" | ", klein.Plan.FreiBeschriftungen.Select(x => x.Text)));
        }, -1);
        Schritt("19d9_Verlegung_Absenz_Abfragen", () =>
        {
            // Z1/Z2: Verlegung bei „Entfall“ rückgängig machen; neue Lehrer-Absenz fragt nach Verlegungen und Sondereinsätzen
            AppZustand.DatumSetzen(tag);
            var dd = AppZustand.D;
            bool Aendern2(string text, AenderungsArt art, Action<Schuldatei> a) { AppZustand.Doc.Aendern(text, art, a); return true; }
            var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(dd, tag);
            var r = Core.Vertretungsplanung.Tagesplan.Regulaer(dd, tag).First(x => x.LehrerId is int l && l != krank.Id && tp.Stunde(x.Stunde).Any(e => e.Findetstatt && e.Status == Core.Vertretungsplanung.TagesStatus.Regulaer && e.Unterricht?.Id == x.Unterricht.Id) && !dd.Vertretungen.Any(v => v.Datum == tag && v.UnterrichtId == x.Unterricht.Id) && !dd.Abwesenheiten.Any(a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == l && a.Von <= tag && a.Bis >= tag) && x.Unterricht.Zeilen.Count == 1 && string.IsNullOrEmpty(x.Unterricht.Band) &&
                Enumerable.Range(1, dd.Zeitraster.StundenProTag).Any(s => s != x.Stunde && !tp.Regel.Any(y => y.Unterricht.Id == x.Unterricht.Id && y.Stunde == s) && Core.Vertretungsplanung.Vertretungen.Feldstatus(dd, tp, new[] { l }, x.KlassenIds, s, x.Unterricht.Id, x.RaumId) == Core.Planung.Feldstatus.Frei));
            // Ziel nicht auf eine eigene reguläre Stunde des Unterrichts (Doppelstunde) – dort entstünde bei der Absenz zu Recht eine Vertretung
            var ziel = Enumerable.Range(1, dd.Zeitraster.StundenProTag).First(s => s != r.Stunde && !tp.Regel.Any(y => y.Unterricht.Id == r.Unterricht.Id && y.Stunde == s) && Core.Vertretungsplanung.Vertretungen.Feldstatus(dd, tp, new[] { r.LehrerId!.Value }, r.KlassenIds, s, r.Unterricht.Id, r.RaumId) == Core.Planung.Feldstatus.Frei);
            AppZustand.Doc.Aendern("Verlegen (Selbsttest)", AenderungsArt.Vertretung, x => Core.Vertretungsplanung.Vertretungen.Verlegen(x, r.Unterricht.Id, null, tag, r.Stunde, tag, ziel));
            fragen.Clear();
            if (!VerlegungsAbfrage.VorEntfall(tag, ziel, r.Unterricht.Id, Aendern2)) throw new InvalidOperationException("Entfall bei Verlegung: keine Rückfrage bzw. nicht aufgehoben.");
            if (!fragen.Any(f => f.Contains(VerlegungsAbfrage.FrageVerlegung))) throw new InvalidOperationException("Rückfrage „Soll die Verlegung rückgängig gemacht werden?“ fehlt.");
            if (AppZustand.D.Vertretungen.Any(v => v.Datum == tag && v.UnterrichtId == r.Unterricht.Id && (v.Stunde == ziel || v.IstEntfall))) throw new InvalidOperationException("Verlegung wurde nicht aufgehoben.");
            log.Add("INFO  Z2 Rückfrage: " + fragen[0].Replace("\n", " / "));
            // Z1: wieder verlegen, Sondereinsatz eintragen, dann wird die Lehrkraft krank
            AppZustand.Doc.Aendern("Verlegen (Selbsttest)", AenderungsArt.Vertretung, x => Core.Vertretungsplanung.Vertretungen.Verlegen(x, r.Unterricht.Id, null, tag, r.Stunde, tag, ziel));
            var tp2 = Core.Vertretungsplanung.Tagesplan.Berechnen(AppZustand.D, tag);
            var sonderStunde = Enumerable.Range(1, AppZustand.D.Zeitraster.StundenProTag).FirstOrDefault(s => !tp2.Stunde(s).Any(e => e.Findetstatt && e.LehrerIds.Contains(r.LehrerId!.Value)));
            if (sonderStunde > 0)
                AppZustand.Doc.Aendern("Sondereinsatz (Selbsttest)", AenderungsArt.Vertretung, x => Core.Vertretungsplanung.Vertretungen.Sondereinsatz(x, tag, sonderStunde, sonderStunde, new[] { r.KlassenIds[0] }, new[] { r.LehrerId!.Value }, r.FachId, null, "Projekt"));
            var a = new Abwesenheit { Art = AbwesenheitsArt.Lehrer, ElementId = r.LehrerId!.Value, Von = tag, Bis = tag };
            List<Abwesenheit> gespeichert = new();
            AppZustand.Doc.Aendern("Absenz (Selbsttest)", AenderungsArt.Vertretung, x => gespeichert = Core.Vertretungsplanung.AbsenzVerwaltung.Speichern(x, new[] { a }, Array.Empty<Abwesenheit>()));
            fragen.Clear();
            VerlegungsAbfrage.NachAbsenz(gespeichert, Aendern2);
            if (!fragen.Any(f => f.Contains(VerlegungsAbfrage.FrageVerlegung))) throw new InvalidOperationException("Absenz: Rückfrage zur Verlegung fehlt.");
            if (sonderStunde > 0 && !fragen.Any(f => f.Contains(VerlegungsAbfrage.FrageSondereinsatz))) throw new InvalidOperationException("Absenz: Rückfrage zu Sondereinsätzen fehlt.");
            if (AppZustand.D.Vertretungen.Any(v => v.Datum == tag && v.Art == VertretungsArt.Sondereinsatz && v.VertreterId == r.LehrerId)) throw new InvalidOperationException("Sondereinsatz wurde nicht gelöscht.");
            if (AppZustand.D.Vertretungen.Any(v => v.Datum == tag && v.Stunde == ziel && v.UnterrichtId == r.Unterricht.Id)) throw new InvalidOperationException("Verlegung wurde nach der Absenz nicht aufgehoben.");
            log.Add($"INFO  Z1 Rückfragen: {fragen.Count} – " + string.Join(" // ", fragen.Select(f => f.Replace("\n", " / "))));
            Max(FensterOeffnen("vertr")!);
        }, -1);
        Schritt("19d10_Dropdowns_alle_Fenster", () =>
        {
            // Sprint 6: alle Auswahllisten in allen Tabellen, Unterfenstern und Werkzeugleisten gleich – breit genug für Kurz- und Langname
            var geprueft = 0;
            var fehler = new List<string>();
            foreach (var schluessel in new[] { "vertr", "abw", "veranst", "klaus", "bereit", "pause", "vorm", "st:Lehrer", "st:Klasse", "st:Raum", "st:Fach", "u:Klasse", "u:Lehrer" })
            {
                AlleSchliessen();
                if (FensterOeffnen(schluessel) is not { } f) continue;
                f.WindowState = FormWindowState.Maximized;
                Warten(300);
                Steuerelemente.Auswahlbreite.AlleErfassen();
                Warten(100);
                foreach (var (ort, problem) in DropdownsPruefen(f)) { geprueft++; if (problem != null) fehler.Add($"{schluessel}/{ort}: {problem}"); }
            }
            AlleSchliessen();
            log.Add($"INFO  Dropdowns geprüft: {geprueft}, zu schmal/ohne Langnamen: {fehler.Count}" + (fehler.Count > 0 ? " – " + string.Join("; ", fehler.Take(12)) : ""));
            if (geprueft < 8) throw new InvalidOperationException($"Zu wenige Auswahllisten gefunden ({geprueft}).");
            if (fehler.Count > 0) throw new InvalidOperationException("Auswahllisten zu schmal bzw. ohne Langnamen: " + string.Join("; ", fehler.Take(12)));
            Max(FensterOeffnen("vertr")!);
        }, -1);
        Schritt("19d11_Entfall_rueckgaengig_Plan", () =>
        {
            // Sprint 25: im tagesaktuellen Stundenplan (Vertretungsmodus) per Kontextmenü entfallen lassen und wieder aufheben –
            // anwesende Lehrkraft: wieder regulärer Unterricht; abwesende Lehrkraft: offene Vertretung („???“)
            AppZustand.DatumSetzen(tag);
            var dd = AppZustand.D;
            var tp = Core.Vertretungsplanung.Tagesplan.Berechnen(dd, tag);
            bool Abwesend(int l) => Core.Vertretungsplanung.Absenzen.LehrerAbwesend(dd, l, tag, 1) || dd.Abwesenheiten.Any(a => a.Art == AbwesenheitsArt.Lehrer && a.ElementId == l && a.Von <= tag && a.Bis >= tag);
            var e0 = tp.Eintraege.First(x => x.Status == Core.Vertretungsplanung.TagesStatus.Regulaer && x.Vertretung is null && x.Unterricht is { } u && u.Zeilen.Count == 1 && string.IsNullOrEmpty(u.Band)
                && x.Regel?.LehrerId is int l && !Abwesend(l) && x.KlassenIds.Count > 0 && !Core.Vertretungsplanung.Vertretungen.IstBereitschaftsfach(dd, x.FachId)
                && !dd.Vertretungen.Any(v => v.Datum == tag && v.UnterrichtId == u.Id));
            var uid = e0.Unterricht!.Id;
            var stunde = e0.Stunde;
            var sp = Max(StundenplanFenster.Neu(ElementTyp.Klasse, e0.KlassenIds[0], false));
            Warten(300);
            if (!sp.Plan.Tagesmodus) throw new InvalidOperationException("Stundenplan im Vertretungsmodus ist nicht tagesaktuell.");
            Core.Vertretungsplanung.TagesEintrag Eintrag(StundenplanFenster f, Func<Core.Vertretungsplanung.TagesEintrag, bool> wo) =>
                f.Plan.EintraegeAmDatum(tag, stunde).FirstOrDefault(x => (x.Unterricht?.Id ?? x.Vertretung?.UnterrichtId) == uid && wo(x))
                ?? throw new InvalidOperationException($"Stunde U{uid} ({stunde}.) nicht im Plan: " + string.Join(", ", f.Plan.EintraegeAmDatum(tag, stunde).Select(x => $"U{x.Unterricht?.Id}/{x.Status}")));
            ToolStripItem Punkt(ContextMenuStrip cm, string anfang) => cm.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text?.StartsWith(anfang) == true && i.Enabled)
                ?? throw new InvalidOperationException($"Kontextmenü ohne „{anfang}“: " + string.Join(" | ", cm.Items.Cast<ToolStripItem>().Select(i => i.Text)));
            using (var cm = sp.SelbsttestTagesMenue(tag, stunde, Eintrag(sp, x => x.Findetstatt)))
            {
                if (cm.Items.Cast<ToolStripItem>().Any(i => i.Text?.StartsWith("Entfall rückgängig") == true)) throw new InvalidOperationException("„Entfall rückgängig“ bei stattfindender Stunde.");
                Punkt(cm, "Stunde entfallen lassen").PerformClick();
            }
            var entfallen = Eintrag(sp, x => x.Status == Core.Vertretungsplanung.TagesStatus.Entfall);
            using (var cm = sp.SelbsttestTagesMenue(tag, stunde, entfallen)) Punkt(cm, "Entfall rückgängig").PerformClick();
            var zurueck = Eintrag(sp, _ => true);
            if (zurueck.Status != Core.Vertretungsplanung.TagesStatus.Regulaer || AppZustand.D.Vertretungen.Any(v => v.Datum == tag && v.UnterrichtId == uid && v.Stunde == stunde))
                throw new InvalidOperationException($"Entfall nicht aufgehoben: {zurueck.Status}, Zeilen: {AppZustand.D.Vertretungen.Count(v => v.Datum == tag && v.UnterrichtId == uid && v.Stunde == stunde)}");
            log.Add($"INFO  25-Entfall: Klasse U{uid} {stunde}. Std. entfallen und per Kontextmenü wieder regulär");
            // abwesende Lehrkraft (Lehrerplan): Entfall aufheben → offene Vertretung
            var tp2 = Core.Vertretungsplanung.Tagesplan.Berechnen(AppZustand.D, tag);
            var o = tp2.Eintraege.FirstOrDefault(x => x.Status == Core.Vertretungsplanung.TagesStatus.Offen && x.Regel?.LehrerId == krank.Id && x.Vertretung is { Quelle: VertretungsQuelle.Absenz }
                && x.Unterricht is { } u2 && u2.Zeilen.Count == 1 && string.IsNullOrEmpty(u2.Band));
            if (o is null) { log.Add("INFO  25-Entfall: keine offene Vertretung der abwesenden Lehrkraft gefunden"); return; }
            uid = o.Unterricht!.Id;
            stunde = o.Stunde;
            var lp = Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false));
            Warten(300);
            using (var cm = lp.SelbsttestTagesMenue(tag, stunde, Eintrag(lp, x => x.Findetstatt))) Punkt(cm, "Stunde entfallen lassen").PerformClick();
            using (var cm = lp.SelbsttestTagesMenue(tag, stunde, Eintrag(lp, x => x.Status == Core.Vertretungsplanung.TagesStatus.Entfall)))
            {
                var p = Punkt(cm, "Entfall rückgängig");
                if (p.ToolTipText?.Contains("offenen Vertretung") != true) throw new InvalidOperationException("Hinweis zur offenen Vertretung fehlt: " + p.ToolTipText);
                p.PerformClick();
            }
            var offen = Eintrag(lp, _ => true);
            if (offen.Status != Core.Vertretungsplanung.TagesStatus.Offen) throw new InvalidOperationException($"Abwesende Lehrkraft: nach dem Aufheben {offen.Status} statt offener Vertretung.");
            log.Add($"INFO  25-Entfall: Lehrerplan {krank.Kurzname} U{uid} {stunde}. Std. → offene Vertretung (???)");
        }, -1);
        Schritt("19d12_Fensterlage_klemmen", () =>
        {
            // Sprint 25: Arbeitsfenster rutschen nie unter das Menüband, gespeicherte Anordnungen mit negativen Lagen werden geklemmt
            var sp = StundenplanFenster.Neu(ElementTyp.Klasse, null, false);
            sp.WindowState = FormWindowState.Normal;
            sp.Bounds = new Rectangle(60, -90, 500, 400);
            if (sp.Top < 0) throw new InvalidOperationException($"Arbeitsfenster über dem oberen Rand: {sp.Bounds}");
            sp.Location = new Point(-40, -40);
            if (sp.Top < 0 || sp.Left < 0) throw new InvalidOperationException($"Arbeitsfenster außerhalb des Arbeitsbereichs: {sp.Bounds}");
            var mdi = MdiArbeitsbereich!.ClientSize;
            sp.Location = new Point(mdi.Width + 200, mdi.Height + 200);
            if (sp.Right > mdi.Width || sp.Bottom > mdi.Height) throw new InvalidOperationException($"Arbeitsfenster rechts/unten hinaus: {sp.Bounds} bei {mdi}");
            AlleSchliessen();
            var schl = $"sp:Klasse:{AppZustand.D.Klassen[0].Id}:0";
            LayoutOeffnen(new List<FensterPosition> { new() { Schluessel = schl, X = 30, Y = -120, B = 4000, H = 3000 } });
            var f = MdiChildren.OfType<StundenplanFenster>().Single();
            if (f.Top < 0 || f.Width > mdi.Width || f.Height > mdi.Height) throw new InvalidOperationException($"Gespeicherte Anordnung nicht geklemmt: {f.Bounds} bei {mdi}");
            // Hauptfenster: Programmkopf bleibt im Arbeitsbereich
            var vorher = Bounds;
            WindowState = FormWindowState.Normal;
            var wa = Screen.FromControl(this).WorkingArea;
            Bounds = new Rectangle(wa.X + 20, wa.Y - 60, Math.Min(1100, wa.Width - 40), Math.Min(700, wa.Height - 40));
            var oben = Top;
            Bounds = vorher;
            if (oben < wa.Top) throw new InvalidOperationException($"Hauptfenster über dem oberen Rand: Y={oben}, Arbeitsbereich {wa}");
            log.Add($"INFO  Fensterlage: Arbeitsfenster {f.Bounds} im Bereich {mdi}, Hauptfenster oben {oben} (Arbeitsbereich {wa.Top})");
        }, -1);
        Schritt("19e_Standardanordnung", VertretungsStandardFenster, -1);
        Schritt("19f_Stundenplanmodus", VertretungsmodusUmschalten, -1, false);
        var umgeplant = (Id: 0, Stunde: 0);
        Schritt("19g_Umplanung_Stundenplan", () =>
        {
            // Stunde mit bearbeiteter Vertretung im Stundenplan umplanen (gleicher Tag, freie Stunde)
            var dd = AppZustand.D;
            var ix = AppZustand.Doc.Index;
            var tagIdx = Kalender.TagIndex(dd, tag);
            foreach (var v in dd.Vertretungen.Where(v => v.Datum == tag && Core.Vertretungsplanung.Umplanungsabgleich.Uebertragbar(v) && v.UnterrichtId.HasValue).ToList())
            {
                var p = dd.Stundenplan.FirstOrDefault(x => x.UnterrichtId == v.UnterrichtId && x.Tag == tagIdx && x.Stunde == v.Stunde);
                if (p is null || Core.Planung.Planungsdienst.BlockVon(dd, p).Count > 1 || !ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u) || u.Zeilen.Count > 1) continue;
                var frei = Enumerable.Range(1, dd.Zeitraster.StundenProTag).FirstOrDefault(s => s != p.Stunde && dd.Zeitraster.Verfuegbar(tagIdx, s) &&
                    !dd.Stundenplan.Any(x => x.UnterrichtId == u.Id && x.Tag == tagIdx && x.Stunde == s) && Core.Planung.Planungsdienst.Hindernisse(dd, ix, u, tagIdx, s, new[] { p }).Count == 0);
                if (frei == 0) continue;
                var vid = v.Id;
                var idx = dd.Stundenplan.IndexOf(p);
                AppZustand.Doc.Aendern("Selbsttest Vertretung bearbeiten", AenderungsArt.Vertretung, x =>
                {
                    var w = x.Vertretungen.First(y => y.Id == vid);
                    w.Text = "Selbsttest: Aufgaben S. 12";
                    w.Bearbeitet = true;
                });
                AppZustand.Doc.Aendern("Selbsttest umplanen", AenderungsArt.Stundenplan, x => Core.Planung.Planungsdienst.Verschieben(x, x.Stundenplan[idx], tagIdx, frei, false, false));
                umgeplant = (vid, frei);
                Max(StundenplanFenster.Neu(ElementTyp.Klasse, u.KlassenIds.First(), false));
                log.Add($"INFO  19g Vertretung {vid}: U{u.Id} {v.Stunde}. → {frei}. Stunde umgeplant");
                return;
            }
            throw new InvalidOperationException("Keine umplanbare Stunde mit Vertretung gefunden.");
        }, 3);
        Schritt("19h_Umplanung_nacherfasst", () =>
        {
            VertretungsmodusUmschalten();
            var erg = LetzteUmplanung ?? throw new InvalidOperationException("Keine Nacherfassung beim Moduswechsel.");
            var v = AppZustand.D.Vertretungen.FirstOrDefault(x => x.Id == umgeplant.Id) ?? throw new InvalidOperationException("Vertretung ging beim Umplanen verloren.");
            if (v.Stunde != umgeplant.Stunde || v.Text != "Selbsttest: Aufgaben S. 12") throw new InvalidOperationException($"Vertretung nicht übertragen ({v.Stunde}. Std., „{v.Text}“).");
            log.Add($"INFO  19h nacherfasst: {erg.Uebertragen.Count} übertragen, {erg.Vormerkungen.Count} Vormerkungen; Hinweis: {hinweise.LastOrDefault()?.Replace("\n", " / ")}");
            Max(FensterOeffnen("vertr")!);
        }, -1, false);
        Schritt("19i_Stundenplanmodus", VertretungsmodusUmschalten, -1, false);
        Schritt("20_Wertrechnung", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(0))), 6);
        Schritt("21_Unterrichtsmatrix", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(1))), 6);
        Schritt("22_Stundentafel", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2))), 6);
        Schritt("23_Kursplanung", () => Max(Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster())), 4);
        Schritt("24_Hilfe", () => HilfeFenster.Zeigen("Vertretungsplanung"), 0);
        Schritt("24a_Updateanzeige", () =>
        {
            // Releases wie von GitHub: nur Desktop-Tags „v…“ zählen, Entwürfe/Vorabversionen nicht
            var json = """[{"tag_name":"web-v9.9.9","html_url":"https://example/web"},{"tag_name":"v1.0.999","html_url":"https://example/v999","draft":true},{"tag_name":"v1.0.998","html_url":"https://example/v998"},{"tag_name":"v1.0.5","html_url":"https://example/v5"}]""";
            var (neu, url) = UntPlan.App.Update.Updatepruefung.Auswerten(json, "v");
            if (neu != "1.0.998" || url != "https://example/v998") throw new InvalidOperationException($"Release-Auswertung falsch: {neu} {url}");
            var stand = new UntPlan.App.Update.Updatestand(Updates.Version, true, neu, UntPlan.App.Update.Updatepruefung.Vergleichen(neu!, Updates.Version) > 0, url, Updateanzeige.Standardquelle, DateTime.UtcNow, null);
            Updates.Anwenden(stand);
            Warten(200);
            if (!Updates.Label.Visible || !(Updates.Label.Text ?? "").Contains("1.0.998")) throw new InvalidOperationException("Statusleiste zeigt das Update nicht: " + Updates.Label.Text);
            string? frage = null;
            var alt = Ui.FrageAntwort;
            Ui.FrageAntwort = (t, _) => { frage = t; return false; };
            try { Updates.HinweisZeigen(); } finally { Ui.FrageAntwort = alt; }
            if (frage is null || !frage.Contains("Neue Version: 1.0.998") || !frage.Contains("Installierte Version: " + Updates.Version))
                throw new InvalidOperationException("Update-Hinweis fehlt oder unvollständig: " + frage);
            log.Add($"INFO  Update: installiert {Updates.Version}, Statusleiste „{Updates.Label.Text}“");
        }, 0);
        Schritt("24b_Update_aktuell", () =>
        {
            Updates.Anwenden(new UntPlan.App.Update.Updatestand(Updates.Version, true, Updates.Version, false, null, Updateanzeige.Standardquelle, DateTime.UtcNow, null));
            if (Updates.Label.Visible) throw new InvalidOperationException("Hinweis bleibt trotz aktueller Version sichtbar");
        }, 0);
        Schritt("25_Perioden", () => new PeriodenDialog().Show(this), 2);
        Schritt("25a_Optimierung", () => new OptimierungDialog().Show(this), 2);
        Schritt("25b_Bereitschaftsplanung", () => Max(FensterOeffnen("bereitplan")!), 2);
        Schritt("25c_Unterrichtsgruppen", () => Max(FensterOeffnen("ugruppen")!), 2);
        Schritt("25d_UntPlanWeb", () => new UntPlanWebDialog().Show(this), 0);
        Schritt("25d0_Upload_Fortschritt", () =>
        {
            // kleines Fortschrittsfenster beim Export (wie im Vorbild), danach folgt die Erfolgsmeldung
            var f = new UntPlanWebDialog.UploadFortschritt();
            f.Zeigen(this);
            f.Setzen(64, "Unterrichtsbestandteile werden konvertiert (153 von 238)");
        }, 0);
        Schritt("25d1_UntPlanWeb_Fehler", () =>
        {
            var w = new UntPlanWebDialog();
            w.Show(this);
            w.FehlerAnzeigen("UntPlanWeb-Übertragungsfehler!", "Es konnte keine Verbindung zum Server hergestellt werden. Überprüfen Sie Ihre Internetverbindung, die Verbindungseinstellungen und gegebenenfalls den Status des UntPlanWeb-Servers.");
        }, 0);
        Schritt("25d3_Schuelerabwesenheiten", () =>
        {
            var sch = AppZustand.D.Schueler.Take(4).ToList();
            var klasse = sch.FirstOrDefault()?.KlasseId is int k ? AppZustand.D.Klassen.First(x => x.Id == k).Kurzname : "";
            new SchuelerAbwesenheitenDialog(sch.SelectMany((x, i) => Enumerable.Range(1, i == 3 ? 1 : 2).Select(st => new SchuelerAbwesenheit
            {
                WebId = i * 10 + st, SchuelerId = x.Id, Klasse = klasse, Datum = tag, Stunde = st + i, Fach = "M", Lehrer = AppZustand.D.Lehrer[0].Kurzname,
                Verspaetet = i == 3, Minuten = i == 3 ? 10 : 0, Status = i == 0 ? "entschuldigt" : i == 1 ? "unentschuldigt" : "offen", Bemerkung = i == 0 ? "Arzttermin" : "",
            }))).Show(this);
        }, 0);
        Schritt("25d4_Klassenbuch", () =>
        {
            var lehrer = AppZustand.D.Lehrer[0].Kurzname;
            var klasse = AppZustand.D.Klassen[0].Kurzname;
            new KlassenbuchDialog(new[]
            {
                new KlassenbuchEintrag { WebId = 1, Datum = tag, Stunde = 1, Klassen = { klasse }, Fach = "M", Lehrer = lehrer, Lehrstoff = "Bruchrechnung: Erweitern und Kürzen", Hausaufgabe = "S. 12 Nr. 3", Faellig = tag.AddDays(2), AnwesenheitGeprueft = true },
                new KlassenbuchEintrag { WebId = 2, Datum = tag, Stunde = 2, Klassen = { klasse }, Fach = "D", Lehrer = lehrer, Lehrstoff = "Gedichtanalyse", AnwesenheitGeprueft = true },
                new KlassenbuchEintrag { WebId = 3, Datum = tag.AddDays(-1), Stunde = 3, Klassen = { klasse }, Fach = "E", Lehrer = lehrer, Lehrstoff = "Simple past", Hausaufgabe = "Vokabeln Unit 2" },
            }).Show(this);
        }, 0);
        Schritt("25d2_UntPlanWeb_Einstellungen", () => new UntPlanWebEinstellungenDialog().Show(this), 0);
        Schritt("25e_Raumzuordnung", () => RaumZuordnenDialog.Zeigen(this, AppZustand.D.Stundenplan.FirstOrDefault(p => Core.Planung.Planungsdienst.BlockVon(AppZustand.D, p).Count > 1) ?? AppZustand.D.Stundenplan[0], false), 2);
        Schritt("25e1_Raumzuordnung_Raster", () =>
        {
            // Stunde mit belegtem Wunschraum: Häkchen in „Besetzt“ – danach müssen alle Zellen sauber mit Linien gezeichnet sein
            var dd = AppZustand.D;
            var p = dd.Stundenplan.FirstOrDefault(x => dd.Stundenplan.Count(y => y.Tag == x.Tag && y.Stunde == x.Stunde && y.Raeume.Any(r => r.HasValue)) > 3) ?? dd.Stundenplan[0];
            RaumZuordnenDialog.Zeigen(this, p, false);
            Warten(400);
            var dlg = Application.OpenForms.Cast<Form>().Last(f => f.Text == "Raum zuordnen / löschen");
            var fehler = RasterPruefen(dlg);
            if (fehler.Count > 0) throw new InvalidOperationException("Raster „Mögliche Räume“: " + string.Join(", ", fehler));
        }, 2);
        Schritt("26_Kommentare", () => Max(new KommentarUebersicht()).Show(), 1);
        Schritt("27_Monitor", () =>
        {
            var m = new MonitorFenster();
            m.Show(this);
        });
        Schritt("27a_Vertretungsplan_Druck", () =>
        {
            var bilder = Druck.VertretungsplanDruck.Bilder(tag, tag, false);
            bilder[0].Save(Path.Combine(ordner, "27a_Druck_Klassen.png"));
            Druck.VertretungsplanDruck.Bilder(tag, tag, true)[0].Save(Path.Combine(ordner, "27b_Druck_Lehrer.png"));
        }, -1);
        Schritt("27d_Stundenplan_Druck", () =>
        {
            var klassen = AppZustand.D.Klassen.Take(2).Select(k => k.Id).ToList();
            var gross = new StundenplanFormat { Typ = ElementTyp.Klasse, Gross = true, Schriftgroesse = 10 };
            var seiten = StundenplanDruck.Bilder(ElementTyp.Klasse, klassen, gross);
            if (seiten.Count != klassen.Count) throw new InvalidOperationException($"Stundenplandruck: {seiten.Count} Seiten statt {klassen.Count}");
            seiten[0].Save(Path.Combine(ordner, "27d1_Druck_Stundenplan_gross.png"));
            var klein = StundenplanDruck.Bilder(ElementTyp.Klasse, klassen, new StundenplanFormat { Typ = ElementTyp.Klasse, Gross = false, Schriftgroesse = 8 });
            if (klein.Count != 1) throw new InvalidOperationException("Stundenplandruck klein: zwei Pläne gehören auf eine Seite");
            klein[0].Save(Path.Combine(ordner, "27d2_Druck_Stundenplan_klein.png"));
        }, -1);
        // Sprint 6: Seitenlayout und „Felder der Ansicht“ für den Vertretungsplan (Einstellungen danach wiederherstellen)
        var druckVorher = System.Text.Json.JsonSerializer.Serialize(AppZustand.Einstellungen.Vertretungsdruck);
        void DruckZurueck() => AppZustand.Einstellungen.Vertretungsdruck = System.Text.Json.JsonSerializer.Deserialize<Vertretungsdruck>(druckVorher)!;
        Schritt("27a1_Seitenlayout", () =>
        {
            var e = AppZustand.Einstellungen.Vertretungsdruck;
            e.Felder.Clear();
            var f = Druck.SeitenlayoutFenster.Zeigen(this, tag, tag, false);
            f.WindowState = FormWindowState.Maximized;
            Warten(300);
            if (f.Seitenzahl == 0) throw new InvalidOperationException("Seitenlayout ohne Vorschau");
            var standard = new[] { "Element", "Stunde", "Partner", "Abwesend", "Fach", "Raum", "Bemerkung" };
            if (!f.GedruckteFelder.SequenceEqual(standard)) throw new InvalidOperationException("Standardfelder: " + string.Join(",", f.GedruckteFelder));
            // Feld „Art“ zusätzlich drucken, Raster und Überschrift je Element: wirkt sofort auf die Vorschau
            Druck.VertretungsplanDruck.Felder(e).First(x => x.Schluessel == "Art").Drucken = true;
            e.Raster = true;
            e.UeberschriftJeElement = true;
            e.KombinierteKlassen = true;
            f.Neuzeichnen();
            Warten(200);
            if (!f.GedruckteFelder.Contains("Art")) throw new InvalidOperationException("Zusätzliches Feld wird nicht gedruckt: " + string.Join(",", f.GedruckteFelder));
            var zeilen = Druck.VertretungsplanDruck.ZeilenAm(tag, false);
            if (zeilen.Count == 0) throw new InvalidOperationException("Keine Druckzeilen am Testtag");
            // „Nur neue Vertretungen“ bzw. „Druck nur, wenn geändert nach …“ (Zukunft) filtert alle Zeilen
            e.NurGeaendertNach = DateTime.Now.AddDays(1);
            var gefiltert = Druck.VertretungsplanDruck.ZeilenAm(tag, false).Count;
            e.NurGeaendertNach = null;
            if (gefiltert != 0) throw new InvalidOperationException($"„Druck nur, wenn geändert nach …“ filtert nicht ({gefiltert} Zeilen)");
            log.Add($"INFO  Seitenlayout: {f.Seitenzahl} Seite(n), Felder {string.Join(",", f.GedruckteFelder)}, {zeilen.Count} Zeilen");
        }, -1);
        Schritt("27a2_Felder_der_Ansicht", () =>
        {
            var e = AppZustand.Einstellungen.Vertretungsdruck;
            var lay = Druck.SeitenlayoutFenster.Zeigen(this, tag, tag, false);
            lay.WindowState = FormWindowState.Maximized;
            var uebernommen = 0;
            var d = Druck.FelderDerAnsichtDialog.Anzeigen(lay, Druck.VertretungsplanDruck.Felder(e), k => Druck.VertretungsplanDruck.StandardUeberschrift(k, false), () => { uebernommen++; lay.Neuzeichnen(); }, out var raster);
            Warten(200);
            var feld = raster.Rows.Cast<DataGridViewRow>().First(r => (string)r.Cells[0].Value == "Fach");
            feld.Cells[1].Value = false;
            d.Controls.OfType<Button>().First(b => b.Text == "Übernahme").PerformClick();
            Warten(200);
            if (uebernommen != 1 || lay.GedruckteFelder.Contains("Fach")) throw new InvalidOperationException("„Übernahme“ wirkt nicht: " + string.Join(",", lay.GedruckteFelder));
            if (!d.Visible) throw new InvalidOperationException("„Übernahme“ hat den Dialog geschlossen");
        }, -1);
        Schritt("27a3_Seitenlayout_zurueck", DruckZurueck, -1);
        Schritt("27a4_Seitenlayout_Liste", () =>
        {
            var kopf = new[] { "Name", "Langname", "Stunden" };
            var zeilen = AppZustand.D.Lehrer.Select(l => new[] { l.Kurzname, l.Langname, "1" }).ToList();
            var d = ListenDruck.SeitenlayoutAnzeigen(this, "Lehrerliste (Selbsttest)", kopf, new[] { 80, 200, 60 }, zeilen);
            Warten(400);
            var bild = d.Controls.OfType<PictureBox>().First(p => p.Name == "Vorschau").Image;
            if (bild is null) throw new InvalidOperationException("Listen-Seitenlayout ohne Vorschau");
        }, -1);
        Schritt("27c_Symbole", () =>
        {
            var namen = Symbole.Namen;
            using var bmp = new Bitmap(10 * 110, ((namen.Count + 9) / 10) * 64 + 10);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            for (var i = 0; i < namen.Count; i++)
            {
                var x = i % 10 * 110 + 10;
                var y = i / 10 * 64 + 6;
                g.DrawImage(Symbole.Get(namen[i], 32), x + 30, y);
                g.DrawImage(Symbole.Get(namen[i], 16), x + 70, y + 8);
                TextRenderer.DrawText(g, namen[i], Ui.SchriftKlein, new Rectangle(x - 5, y + 36, 105, 16), Color.Black, TextFormatFlags.HorizontalCenter);
            }
            bmp.Save(Path.Combine(ordner, "27c_Symbole_Uebersicht.png"));
        }, -1);
        Schritt("28_Speichern_Laden", () =>
        {
            var pfad = Path.Combine(ordner, "beispiel" + Dokument.Dateiendung);
            var e = AppZustand.Einstellungen;
            var ansichtVorher = e.Vertretungsansicht;
            var formatVorher = e.Stundenplanformate.GetValueOrDefault("Selbsttest");
            var doc = AppZustand.Doc;
            try
            {
                // exklusiver Zugriff: Sperre anlegen, zweiter Versuch wird erkannt
                var sperre = Dateisperre.Anfordern(pfad);
                if (sperre.Status != SperrStatus.Erhalten) throw new InvalidOperationException("Sperre nicht erhalten: " + sperre.Meldung);
                doc.Sperre = sperre.Sperre;
                var zweiter = Dateisperre.Anfordern(pfad);
                if (zweiter.Status != SperrStatus.Belegt || !zweiter.Meldung.Contains(Environment.MachineName))
                    throw new InvalidOperationException($"Zweiter Sperrversuch nicht erkannt ({zweiter.Status})");
                if (!File.Exists(pfad + Dateisperre.Endung)) throw new InvalidOperationException("Sperrdatei fehlt");

                // Ansichtseinstellung ändern → Dokument gilt als geändert → speichern
                doc.Speichern(pfad);
                e.Vertretungsansicht = "Lehrer - Standard";
                e.Stundenplanformate["Selbsttest"] = "Format aus der Datei";
                e.Speichern();
                if (!doc.IstGeaendert) throw new InvalidOperationException("Geänderte Ansichtseinstellung markiert das Dokument nicht als geändert");
                doc.Speichern(pfad);
                if (doc.IstGeaendert) throw new InvalidOperationException("Nach dem Speichern noch geändert");

                // lokal zurückstellen, laden → wiederhergestellt
                e.Vertretungsansicht = "Raum - Standard";
                e.Stundenplanformate["Selbsttest"] = "lokal";
                var geladen = Dokument.Laden(pfad);
                if (geladen.Daten.Unterrichte.Count != AppZustand.D.Unterrichte.Count) throw new InvalidOperationException("Laden lieferte andere Daten");
                if (geladen.Daten.Ansichtseinstellungen is null) throw new InvalidOperationException("Ansichtseinstellungen fehlen in der Datei");
                e.AnsichtUebernehmen(geladen.Daten.Ansichtseinstellungen);
                if (e.Vertretungsansicht != "Lehrer - Standard" || e.Stundenplanformate.GetValueOrDefault("Selbsttest") != "Format aus der Datei")
                    throw new InvalidOperationException("Ansichtseinstellungen nach dem Laden nicht wiederhergestellt");
                if (!e.Fenstergruppen.ContainsKey(AppZustand.Vertretungsmodus ? GruppeVertretung : GruppeStundenplan))
                    throw new InvalidOperationException("Fensteranordnung des Modus nicht in der Datei");

                // schreibgeschützt: Speichern in dieselbe Datei wird verweigert
                geladen.Schreibgeschuetzt = true;
                try { geladen.Speichern(pfad); throw new InvalidOperationException("Schreibgeschützte Datei wurde gespeichert"); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("schreibgeschützt geöffnet")) { /* erwartet */ }

                // Freigabe: danach ist wieder ein Zugriff möglich
                doc.Sperre = null;
                sperre.Sperre!.Freigeben();
                if (File.Exists(pfad + Dateisperre.Endung)) throw new InvalidOperationException("Sperrdatei nach Freigabe noch vorhanden");
                var neu = Dateisperre.Anfordern(pfad);
                if (neu.Status != SperrStatus.Erhalten) throw new InvalidOperationException("Nach Freigabe keine Sperre möglich");
                neu.Sperre!.Freigeben();
            }
            finally
            {
                doc.Sperre?.Freigeben();
                doc.Sperre = null;
                e.Vertretungsansicht = ansichtVorher;
                if (formatVorher is null) e.Stundenplanformate.Remove("Selbsttest"); else e.Stundenplanformate["Selbsttest"] = formatVorher;
                e.AnsichtStandMerken();
            }
        });
        File.WriteAllLines(logPfad, log.Append("ENDE"));
        Application.Exit();
    }

    /// <summary>Simuliert das Überfahren jeder sichtbaren Zelle einer nicht aktuellen Zeile und meldet Zellen, die dabei nicht hervorgehoben werden.</summary>
    internal static List<string> HoverPruefen(Control wurzel)
    {
        var res = new List<string>();
        var bewegen = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var verlassen = typeof(Control).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(k => Alle(k).Prepend(k));
        foreach (var g in Alle(wurzel).OfType<DataGridView>().Where(g => g.Visible && g.Width > 20 && g.Height > 20).ToList())
        {
            if (g.IsCurrentCellInEditMode) continue;
            var zeile = Math.Max(0, g.FirstDisplayedScrollingRowIndex);
            if (g.Rows.Count > 1 && g.CurrentCell?.RowIndex == zeile) zeile++;
            Bitmap Ausschnitt(Rectangle r)
            {
                using var b = new Bitmap(g.Width, g.Height);
                g.DrawToBitmap(b, new Rectangle(0, 0, g.Width, g.Height));
                return b.Clone(r, b.PixelFormat);
            }
            static bool Gleich(Bitmap a, Bitmap b)
            {
                for (var x = 0; x < a.Width; x++)
                    for (var y = 0; y < a.Height; y++)
                        if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
                return true;
            }
            if (g.Rows.Count == 0 || zeile >= g.Rows.Count) continue;
            foreach (var z in new[] { zeile })
                foreach (var c in g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible))
                {
                    var r = g.GetCellDisplayRectangle(c.Index, z, true);
                    if (r.Width < 6 || r.Height < 6) continue;
                    try
                    {
                        verlassen.Invoke(g, new object[] { EventArgs.Empty });
                        g.Update();
                        using var vorher = Ausschnitt(r);
                        bewegen.Invoke(g, new object[] { new MouseEventArgs(MouseButtons.None, 0, r.X + r.Width / 2, r.Y + r.Height / 2, 0) });
                        g.Update();
                        using var waehrend = Ausschnitt(r);
                        if (Gleich(vorher, waehrend)) res.Add($"{wurzel.GetType().Name}/{g.Name}/{c.Name}");
                    }
                    finally { verlassen.Invoke(g, new object[] { EventArgs.Empty }); }
                }
        }
        return res;
    }

    /// <summary>
    /// Prüft die Rasterlinien aller sichtbaren Tabellen (UP5, Raster-Bug „Mögliche Räume“): jede sichtbare Zelle muss unten und rechts
    /// eine Linie in der Rasterfarbe haben – eine stehen gelassene Kantenglättung eigener Zellzeichner zeichnet sie verschwommen.
    /// </summary>
    internal static List<string> RasterPruefen(Control wurzel)
    {
        var res = new List<string>();
        IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(k => Alle(k).Prepend(k));
        foreach (var g in Alle(wurzel).OfType<DataGridView>().Where(g => g.Visible && g.Width > 20 && g.Height > 20 && g.CellBorderStyle == DataGridViewCellBorderStyle.Single && g.Rows.Count > 0).ToList())
        {
            if (g.IsCurrentCellInEditMode) continue;
            using var b = new Bitmap(g.Width, g.Height);
            g.DrawToBitmap(b, new Rectangle(0, 0, g.Width, g.Height));
            var soll = g.GridColor.ToArgb();
            // sichtbare Höhe ohne waagerechten Rollbalken (teilweise verdeckte Zeilen nicht prüfen)
            var sichtbar = g.ClientSize.Height - (g.Controls.OfType<HScrollBar>().FirstOrDefault(x => x.Visible)?.Height ?? 0);
            var falsch = 0;
            var geprueft = 0;
            var beispiele = new List<string>();
            var ende = Math.Min(g.Rows.Count, Math.Max(0, g.FirstDisplayedScrollingRowIndex) + 12);
            for (var z = Math.Max(0, g.FirstDisplayedScrollingRowIndex); z < ende; z++)
                foreach (var c in g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible))
                {
                    var r = g.GetCellDisplayRectangle(c.Index, z, false);
                    if (r.Width < 8 || r.Height < 8 || r.Bottom > sichtbar || r.Right > g.Width) continue;
                    geprueft++;
                    var px = b.GetPixel(r.X + r.Width / 2, r.Bottom - 1);
                    if (px.ToArgb() != soll && b.GetPixel(r.X + 2, r.Bottom - 1).ToArgb() != soll)
                    {
                        falsch++;
                        if (beispiele.Count < 4) beispiele.Add($"{c.Name}/{z}:{px.R},{px.G},{px.B}");
                    }
                }
            if (falsch > 0) res.Add($"{wurzel.GetType().Name}/{g.Name}: {falsch} von {geprueft} Zellen (Raster {g.GridColor.R},{g.GridColor.G},{g.GridColor.B}; {string.Join(" ", beispiele)})");
        }
        return res;
    }

    /// <summary>
    /// Alle Auswahllisten eines Fensters prüfen: einzelne Felder (Dialoge, Werkzeugleisten) werden geöffnet, Auswahlzellen jeder Tabelle
    /// bearbeitet; die geöffnete Liste muss so breit sein wie für Kurz- und Langname nötig und Langnamen anzeigen (eigene Darstellung).
    /// </summary>
    private static List<(string Ort, string? Problem)> DropdownsPruefen(Form f)
    {
        var erg = new List<(string, string?)>();
        string? Bewerten(ComboBox cb)
        {
            var noetig = Math.Min(Steuerelemente.ListenSuche.BenoetigteBreite(cb, langname: Steuerelemente.ListenSuche.Langname), 760);
            if (cb.DropDownWidth < noetig) return $"Liste {cb.DropDownWidth} px statt mind. {noetig} px";
            if (Steuerelemente.Auswahlbreite.MitLangnamen(cb) && cb.DrawMode == DrawMode.Normal) return "zeigt keine Langnamen";
            return null;
        }
        IEnumerable<Control> Alle(Control c) => c.Controls.Cast<Control>().SelectMany(k => new[] { k }.Concat(Alle(k)));
        foreach (var cb in Alle(f).OfType<ComboBox>().Where(c => c.Visible && c.Enabled && c.Items.Count > 0 && c.Parent is not DataGridView).ToList())
        {
            cb.Focus();
            Warten(30);
            cb.DroppedDown = true;
            Warten(60);
            var p = Bewerten(cb);
            cb.DroppedDown = false;
            erg.Add((cb.Name is { Length: > 0 } n ? n : "Feld " + cb.Text, p));
        }
        foreach (var g in Alle(f).OfType<DataGridView>().Where(x => x.Visible && !x.ReadOnly && x.RowCount > 0).ToList())
            foreach (var spalte in g.Columns.OfType<DataGridViewComboBoxColumn>().Where(c => c.Visible && !c.ReadOnly).ToList())
            {
                var zeile = g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && r.Visible && !r.Cells[spalte.Index].ReadOnly && r.Cells[spalte.Index] is DataGridViewComboBoxCell);
                if (zeile is null) continue;
                try
                {
                    g.CurrentCell = zeile.Cells[spalte.Index];
                    g.BeginEdit(true);
                    Warten(80);
                    if (g.EditingControl is ComboBox cb && cb.Items.Count > 0)
                    {
                        cb.DroppedDown = true;
                        Warten(60);
                        erg.Add(($"{g.Name}.{spalte.HeaderText}", Bewerten(cb)));
                        cb.DroppedDown = false;
                    }
                }
                finally { g.CancelEdit(); g.EndEdit(); }
            }
        return erg;
    }

    private static void Warten(int ms)
    {
        var ende = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < ende)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Foto(string pfad)
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1600, 900);
        using var bmp = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
        bmp.Save(pfad, System.Drawing.Imaging.ImageFormat.Png);
    }
}
