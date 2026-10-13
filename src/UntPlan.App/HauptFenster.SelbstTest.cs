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
        Core.Module.Pausenaufsichten.Optimieren(d);
        d.Tagestexte.Add(new Tagestext { Datum = tag, Text = "Heute 7. Stunde Lehrerkonferenz – Unterricht endet nach der 6. Stunde." });
        AppZustand.Setzen(new Dokument(d));

        void Schritt(string name, Action aktion, int ribbonSeite = -1, bool schliessen = true)
        {
            try
            {
                if (schliessen) AlleSchliessen();
                if (ribbonSeite >= 0) { _ribbon.AktiveSeite = ribbonSeite; _ribbon.Invalidate(); }
                aktion();
                Warten(900);
                Foto(Path.Combine(ordner, name + ".png"));
                if (Ui.FehlerProtokoll.Count > 0)
                {
                    log.Add($"FEHLER {name}: {string.Join(Environment.NewLine, Ui.FehlerProtokoll)}");
                    Ui.FehlerProtokoll.Clear();
                }
                else log.Add($"OK    {name}");
            }
            catch (Exception ex)
            {
                log.Add($"FEHLER {name}: {ex}");
            }
            foreach (var f in Application.OpenForms.Cast<Form>().Where(f => f != this && f.Owner == this && !f.IsMdiChild).ToList()) f.Close();
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
        Schritt("19e_Standardanordnung", VertretungsStandardFenster, -1);
        Schritt("19f_Stundenplanmodus", VertretungsmodusUmschalten, -1, false);
        Schritt("20_Wertrechnung", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(0))), 6);
        Schritt("21_Unterrichtsmatrix", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(1))), 6);
        Schritt("22_Stundentafel", () => Max(Oeffnen<WertrechnungFenster>("wert", () => new WertrechnungFenster(2))), 6);
        Schritt("23_Kursplanung", () => Max(Oeffnen<KursplanungFenster>("kurs", () => new KursplanungFenster())), 4);
        Schritt("24_Hilfe", () => HilfeFenster.Zeigen("Vertretungsplanung"), 0);
        Schritt("25_Perioden", () => new PeriodenDialog().Show(this), 2);
        Schritt("26_Kommentare", () => Max(new KommentarUebersicht()).Show(), 1);
        Schritt("27_Monitor", () =>
        {
            var m = new MonitorFenster();
            m.Show(this);
        });
        Schritt("28_Speichern_Laden", () =>
        {
            var pfad = Path.Combine(ordner, "beispiel" + Dokument.Dateiendung);
            AppZustand.Doc.Speichern(pfad);
            var geladen = Dokument.Laden(pfad);
            if (geladen.Daten.Unterrichte.Count != AppZustand.D.Unterrichte.Count) throw new InvalidOperationException("Laden lieferte andere Daten");
        });
        File.WriteAllLines(Path.Combine(ordner, "selbsttest.log"), log);
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
