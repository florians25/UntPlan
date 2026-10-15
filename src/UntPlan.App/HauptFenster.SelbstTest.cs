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

        // gespeicherte Spaltenlayouts bzw. Ansicht der Vertretungsliste (aus früheren Läufen) nicht übernehmen: Standard wie Untis
        foreach (var k in AppZustand.Einstellungen.Tabellenlayouts.Keys.Where(k => k.StartsWith("VertretungenFenster", StringComparison.Ordinal)).ToList())
            AppZustand.Einstellungen.Tabellenlayouts.Remove(k);
        AppZustand.Einstellungen.Vertretungsansicht = "Klasse - Standard";
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
        Schritt("09b_Stundenplan_Formatdialog", () =>
            StundenplanEinstellungenDialog.Anzeigen(this, new StundenplanFormat { Name = "Klassenplan", Typ = ElementTyp.Klasse, Einzelstunden = true, PausenAnzeigen = true, Farbe = FarbeNach.Lehrer }, ElementTyp.Klasse, AppZustand.D.Klassen.First().Id), 3);
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
        Schritt("10_Uebersicht", () => Max(new UebersichtFenster(ElementTyp.Klasse)).Show(), 3);
        Schritt("11_Diagnose", () => Max(Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster())), 2);
        Schritt("12_Gewichtung", () => new GewichtungDialog().Show(this), 2);
        Schritt("13_Zeitraster", () => new ZeitrasterDialog().Show(this), 0);
        Schritt("14_Einstellungen", () => new EinstellungenDialog().Show(this), 0);
        Schritt("15_Ferien", () => Max(Oeffnen<FerienFenster>("ferien", () => new FerienFenster())), 0);
        Schritt("16_Vertretungsmodus", VertretungsmodusUmschalten, -1, false);
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
            // Stundentausch zweier Stunden einer Klasse: wie Untis je Position eine Zeile „Tausch“, geänderte Felder magenta
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
        Schritt("19b_Pausenaufsichten", () => Max(FensterOeffnen("pause")!), -1);
        Schritt("19c_Statistik", () => Max(FensterOeffnen("stat")!), -1);
        Schritt("19d_Kalender_Bereitschaften", () => { FensterOeffnen("kal"); FensterOeffnen("bereit"); FensterOeffnen("vorm"); LayoutMdi(MdiLayout.TileVertical); }, -1);
        Schritt("19d1_Veranstaltungen", () => Max(FensterOeffnen("veranst")!), -1);
        Schritt("19d2_Klausuren", () => Max(FensterOeffnen("klaus")!), -1);
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
        Schritt("19e_Standardanordnung", VertretungsStandardFenster, -1);
        Schritt("19f_Stundenplanmodus", VertretungsmodusUmschalten, -1, false);
        Schritt("20_Wertrechnung", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(0))), 6);
        Schritt("21_Unterrichtsmatrix", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(1))), 6);
        Schritt("22_Stundentafel", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2))), 6);
        Schritt("23_Kursplanung", () => Max(Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster())), 4);
        Schritt("24_Hilfe", () => HilfeFenster.Zeigen("Vertretungsplanung"), 0);
        Schritt("25_Perioden", () => new PeriodenDialog().Show(this), 2);
        Schritt("25a_Optimierung", () => new OptimierungDialog().Show(this), 2);
        Schritt("25b_Bereitschaftsplanung", () => Max(FensterOeffnen("bereitplan")!), 2);
        Schritt("25c_Unterrichtsgruppen", () => Max(FensterOeffnen("ugruppen")!), 2);
        Schritt("25d_UntPlanWeb", () => new UntPlanWebDialog().Show(this), 0);
        Schritt("25d0_Upload_Fortschritt", () =>
        {
            // kleines Fortschrittsfenster beim Export (wie Untis), danach folgt die Erfolgsmeldung
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
