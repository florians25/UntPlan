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
        Schritt("10_Uebersicht", () => Max(new UebersichtFenster(ElementTyp.Klasse)).Show(), 3);
        Schritt("11_Diagnose", () => Max(Oeffnen<DiagnoseFenster>("diag", () => new DiagnoseFenster())), 2);
        Schritt("12_Gewichtung", () => new GewichtungDialog().Show(this), 2);
        Schritt("13_Zeitraster", () => new ZeitrasterDialog().Show(this), 0);
        Schritt("14_Einstellungen", () => new EinstellungenDialog().Show(this), 0);
        Schritt("15_Ferien", () => Max(Oeffnen<FerienFenster>("ferien", () => new FerienFenster())), 0);
        Schritt("16_Vertretungsmodus", VertretungsmodusUmschalten, -1, false);
        Schritt("17_Vertretungen", () => Max(FensterOeffnen("vertr")!), -1);
        Schritt("18_Vertretungsvorschlag", () =>
        {
            var offen = AppZustand.D.Vertretungen.Where(v => v.Datum == AppZustand.Datum).OrderByDescending(v => v.Offen).FirstOrDefault();
            AppZustand.VertretungWaehlen(offen);
            Max(FensterOeffnen("vorschlag")!);
        }, -1);
        Schritt("19_Absenzen", () => Max(FensterOeffnen("abw")!), -1);
        Schritt("19a_Tagesplan_Lehrer", () => Max(StundenplanFenster.Neu(ElementTyp.Lehrer, krank.Id, false)), -1);
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
            AppZustand.Doc.Speichern(pfad);
            var geladen = Dokument.Laden(pfad);
            if (geladen.Daten.Unterrichte.Count != AppZustand.D.Unterrichte.Count) throw new InvalidOperationException("Laden lieferte andere Daten");
        });
        File.WriteAllLines(logPfad, log.Append("ENDE"));
        Application.Exit();
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
