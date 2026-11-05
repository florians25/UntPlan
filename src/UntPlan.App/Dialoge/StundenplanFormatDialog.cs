using System.Globalization;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Stundenplanformat wie im Vorbild: „Format 01 - Einzelstundenplan Tage waagrecht, Stunden senkrecht“ mit den Karten
/// Allgemein | Auswahl (Bereich) | Layout 1 | Layout 2 | HTML und einer Vorschau des aktuellen Plans, die jede Änderung sofort zeigt.
/// Der Inhalt einer Stunde wird im eigenen Editor „Stundenplan-Stunde“ gestaltet (<see cref="StundenplanStundeDialog"/>).
/// </summary>
public static class StundenplanEinstellungenDialog
{
    /// <param name="sofort">wird nach jeder Änderung der Stundenplan-Stunde (Editor) sofort mit dem aktuellen Stand aufgerufen – das Format wird gespeichert und die Pläne zeigen die Änderung direkt</param>
    public static StundenplanFormat? Zeigen(IWin32Window owner, StundenplanFormat f, ElementTyp? typ = null, int? elementId = null, Action<StundenplanFormat>? sofort = null)
    {
        using var d = Erstellen(f, typ, elementId, out var uebernehmen, sofort);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        uebernehmen();
        return f;
    }

    /// <summary>Dialog nicht modal anzeigen (Selbsttest, Dokumentation).</summary>
    public static Form Anzeigen(IWin32Window owner, StundenplanFormat f, ElementTyp? typ = null, int? elementId = null)
    {
        var d = Erstellen(f, typ, elementId, out var uebernehmen);
        d.Tag = uebernehmen;
        d.Show(owner);
        return d;
    }

    /// <summary>Für den Selbsttest: Karte wählen.</summary>
    public static void KarteWaehlen(Form d, string titel)
    {
        var tabs = d.Controls.OfType<TabControl>().First();
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == titel);
    }

    /// <summary>Für den Selbsttest: die Vorschau des Dialogs.</summary>
    public static PlanAnsicht Vorschau(Form d) => d.Controls.OfType<PlanAnsicht>().First();

    private static Dialog Erstellen(StundenplanFormat f, ElementTyp? typ, int? elementId, out Action uebernehmen, Action<StundenplanFormat>? sofort = null)
    {
        var d = new Dialog(StundenplanFormat.GrundformText(f.Grundform), 920, 520) { Font = Ui.Schrift };
        var tabs = new TabControl { Left = 8, Top = 8, Width = 560, Height = 440 };
        var dd = AppZustand.D;
        var tagNamen = Enumerable.Range(0, dd.Zeitraster.TageProWoche).Select(t => Planungsdienst.Tagname(dd, t)).ToList();
        var felder = f.Felder.Select(Kopie).ToList();
        var stundeEinstellungen = (f.FelderHorizontalZentrieren, f.FelderVertikalZentrieren, f.BeginnEndzeit, f.NamensArt, f.AliasVerwenden, f.LehrerUndVertreter);

        // ---------------- Allgemein ----------------
        // Vorbild: Gruppe „Ansicht: Kla1 - Klasse 1“ mit Format, Schrift, Spaltenbeschriftung, Zeilenbeschriftung, Stundenplan-Stunde
        // und „Schriftgröße in %:“ (Stundenlupe, Kopp.-Legende, Überschrift); darunter die weiteren Einstellungen des Formats
        var allg = new TabPage("Allgemein");
        var ansicht = new GroupBox { Text = $"Ansicht: {(f.Kurzname.Length > 0 ? f.Kurzname + " - " : "")}{f.Name}", Left = 6, Top = 4, Width = 336, Height = 196 };
        Label Knopfzeile(string knopf, string text, int yy, out Button k)
        {
            k = new Button { Text = knopf, Left = 8, Top = yy, Width = 112, Height = 26 };
            var l = new Label { Text = text, Left = 126, Top = yy, Width = 204, Height = 34 };
            ansicht.Controls.AddRange(new Control[] { k, l });
            return l;
        }
        var grundform = f.Grundform;
        var schriftart = f.Schriftart;
        var formatText = Knopfzeile("Format", StundenplanFormat.GrundformText(grundform), 20, out var grundKnopf);
        var schriftText = Knopfzeile("Schrift", "", 56, out var schriftKnopf);
        Knopfzeile("Spaltenbeschriftung", "Wochentage - Überschrift", 92, out var spaltenKnopf);
        Knopfzeile("Zeilenbeschriftung", "Stunden - Überschrift", 128, out var zeilenKnopf);
        Knopfzeile("Stundenplan-Stunde", "Inhalt (Felder) einer Unterrichtsstunde", 164, out var stundeKnopf);
        allg.Controls.Add(ansicht);
        var prozente = new GroupBox { Text = "Schriftgröße in %:", Left = 348, Top = 4, Width = 196, Height = 100 };
        NumericUpDown Prozent(string text, int wert, int yy)
        {
            prozente.Controls.Add(new Label { Text = text, Left = 8, Top = yy + 3, Width = 100 });
            var n = new NumericUpDown { Left = 112, Top = yy, Width = 58, Minimum = 50, Maximum = 300, Value = Math.Clamp(wert, 50, 300) };
            prozente.Controls.Add(n);
            return n;
        }
        var lupeProzent = Prozent("Stundenlupe:", f.LupeSchriftProzent, 20);
        var legendeProzent = Prozent("Kopp.-Legende:", f.LegendeSchriftProzent, 46);
        var ueberProzent = Prozent("Überschrift:", f.UeberschriftSchriftProzent, 72);
        allg.Controls.Add(prozente);
        var weitere = new GroupBox { Text = "Weitere Einstellungen", Left = 6, Top = 204, Width = 538, Height = 200 };
        var y = 18;
        var name = Ui.Zeile(weitere, "Langname", new TextBox { Text = f.Name }, ref y, 110, 140);
        var kurzname = Ui.Zeile(weitere, "Name", new TextBox { Text = f.Kurzname, PlaceholderText = "automatisch" }, ref y, 110, 140);
        var farbe = Ui.Zeile(weitere, "Farbe nach", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 110, 140);
        farbe.Items.AddRange(new object[] { "Fach", "Lehrer", "Klasse", "Raum", "keine" });
        farbe.SelectedIndex = (int)f.Farbe;
        var breite = Ui.Zeile(weitere, "Zellenbreite", new NumericUpDown { Minimum = 0, Maximum = 400, Value = Math.Clamp(f.ZellenBreite, 0, 400) }, ref y, 110, 60);
        var hoehe = Ui.Zeile(weitere, "Zellenhöhe", new NumericUpDown { Minimum = 0, Maximum = 200, Value = Math.Clamp(f.ZellenHoehe, 0, 200) }, ref y, 110, 60);
        weitere.Controls.Add(new Label { Text = "(0 = automatisch)", Left = 190, Top = y - 46, AutoSize = true, ForeColor = Color.DimGray });
        var schrift = new NumericUpDown { Minimum = 6, Maximum = 20, Value = Math.Clamp(f.Schriftgroesse, 6, 20), Visible = false };
        weitere.Controls.Add(schrift);
        var wy = 18;
        CheckBox W(string text, bool wert)
        {
            var c = new CheckBox { Text = text, Left = 290, Top = wy, Width = 240, Height = 20, Checked = wert };
            weitere.Controls.Add(c);
            wy += 22;
            return c;
        }
        var lang = W("Langnamen anzeigen", f.Langname);
        var zeiten = W("Uhrzeiten anzeigen", f.ZeitenAnzeigen);
        var einzel = W("Einzelstunden (nicht zusammenfassen)", f.Einzelstunden);
        var pausen = W("Pausen anzeigen", f.PausenAnzeigen);
        var klein = W("Kleiner Stundenplan (nur Fachkürzel)", f.Klein);
        var lupe = W("Stundenplanlupe anzeigen", f.StundenplanlupeAnzeigen);
        var std = W("Stpl-Stunde: Standardformat", f.Standardformat);
        var spaltenWerte = SpaltenBeschriftungWerte.Aus(f);
        var zeilenArt = f.ZeilenArt;
        var zeilenWerte = ZeilenBeschriftungWerte.Aus(f);
        void SchriftAnzeigen() => schriftText.Text = $"{(schriftart.Length > 0 ? schriftart : Ui.Schrift.FontFamily.Name)} {schrift.Value:0.0}";
        SchriftAnzeigen();

        // ---------------- Auswahl (Bereich) ----------------
        var bereich = new TabPage("Auswahl (Bereich)");
        var wochen = new GroupBox { Text = "Auswahl Wochenstd. (Bereich)", Left = 8, Top = 6, Width = 530, Height = 104 };
        wochen.Controls.Add(new Label { Text = "Von:", Left = 30, Top = 20, Width = 50 });
        wochen.Controls.Add(new Label { Text = "Bis:", Left = 100, Top = 20, Width = 50 });
        var vonStd = new TextBox { Name = "VonStunde", Left = 20, Top = 40, Width = 60, Text = f.VonStunde.ToString() };
        var bisStd = new TextBox { Name = "BisStunde", Left = 92, Top = 40, Width = 60, Text = (f.BisStunde <= 0 ? dd.Zeitraster.StundenProTag : f.BisStunde).ToString() };
        wochen.Controls.Add(new Label { Text = "Stunden-Nummer", Left = 164, Top = 43, Width = 120 });
        string WochenStd(int tag, int stunde) => $"{tagNamen.ElementAtOrDefault(tag) ?? "Mo"}-{stunde}";
        var vonWs = new TextBox { Name = "VonWochenstunde", Left = 20, Top = 70, Width = 60, Text = WochenStd(f.VonTag, f.VonStunde) };
        var bisWs = new TextBox { Name = "BisWochenstunde", Left = 92, Top = 70, Width = 60, Text = WochenStd(f.BisTag < 0 ? tagNamen.Count - 1 : f.BisTag, f.BisStunde <= 0 ? dd.Zeitraster.StundenProTag : f.BisStunde) };
        wochen.Controls.Add(new Label { Text = "Wochenstunden", Left = 164, Top = 73, Width = 120 });
        var minut = new GroupBox { Text = "Minut-Modus", Left = 290, Top = 12, Width = 230, Height = 84 };
        var minutAn = new CheckBox { Text = "Minut-Modus", Left = 10, Top = 18, Width = 110, Checked = f.MinutModus };
        var beginn = new TextBox { Left = 10, Top = 42, Width = 60, Text = dd.Zeitraster.Zeit(1)?.Beginn.ToString("H.mm") ?? "", Enabled = f.MinutModus };
        var ende = new TextBox { Left = 90, Top = 42, Width = 60, Text = dd.Zeitraster.Zeit(dd.Zeitraster.StundenProTag)?.Ende.ToString("H.mm") ?? "", Enabled = f.MinutModus };
        var ganzerTag = new CheckBox { Text = "Ganzen Tag zeigen", Left = 10, Top = 64, Width = 150, Checked = f.GanzenTagZeigen, Enabled = f.MinutModus };
        minutAn.CheckedChanged += (_, _) => beginn.Enabled = ende.Enabled = ganzerTag.Enabled = minutAn.Checked;
        minut.Controls.AddRange(new Control[] { minutAn, new Label { Text = "Beginn:", Left = 124, Top = 20, Width = 50 }, new Label { Text = "Ende:", Left = 176, Top = 20, Width = 40 }, beginn, ende, ganzerTag });
        wochen.Controls.AddRange(new Control[] { vonStd, bisStd, vonWs, bisWs, minut });
        bereich.Controls.Add(wochen);
        var datum = new GroupBox { Text = "Datum", Left = 8, Top = 116, Width = 530, Height = 120 };
        var heute = AppZustand.Datum;
        var dVon = new DateTimePicker { Format = DateTimePickerFormat.Short, Left = 10, Top = 22, Width = 110, Value = (f.DatumVon ?? dd.Einstellungen.SchuljahrVon).ToDateTime(TimeOnly.MinValue) };
        var dBis = new DateTimePicker { Format = DateTimePickerFormat.Short, Left = 150, Top = 22, Width = 110, Value = (f.DatumBis ?? dd.Einstellungen.SchuljahrBis).ToDateTime(TimeOnly.MinValue) };
        var drehVon = new Drehknopf("Eine Woche vor / zurück") { Left = 122, Top = 22 };
        var drehBis = new Drehknopf("Eine Woche vor / zurück") { Left = 262, Top = 22 };
        drehVon.Gedreht += r => dVon.Value = dVon.Value.AddDays(7 * r);
        drehBis.Gedreht += r => dBis.Value = dBis.Value.AddDays(7 * r);
        var infoVon = new Label { Left = 10, Top = 50, Width = 130, Height = 60 };
        var infoBis = new Label { Left = 150, Top = 50, Width = 130, Height = 60 };
        var kw = new RadioButton { Text = "Kalenderwoche", Left = 300, Top = 20, Width = 150, Checked = f.Zeitraum == FormatZeitraum.Kalenderwoche };
        // Vorbild: Kalenderwoche, 1 Tag, Ganze Periode
        var einTag = new RadioButton { Text = "1 Tag", Left = 300, Top = 42, Width = 150, Checked = f.Zeitraum == FormatZeitraum.EinTag };
        var sj = new RadioButton { Text = "Ganze Periode", Left = 300, Top = 64, Width = 150, Checked = f.Zeitraum == FormatZeitraum.GanzesSchuljahr };
        string Info(DateTime t)
        {
            var dt = DateOnly.FromDateTime(t);
            var schulwoche = (dt.DayNumber - Kalender.Montag(dd.Einstellungen.SchuljahrVon).DayNumber) / 7 + 1;
            return $"{CultureInfo.GetCultureInfo("de-DE").DateTimeFormat.GetDayName(t.DayOfWeek)}\n{ISOWeek.GetWeekOfYear(t)}. Kalenderwoche\n{schulwoche}. Schulwoche";
        }
        void DatumInfo() { infoVon.Text = Info(dVon.Value); infoBis.Text = Info(dBis.Value); }
        dVon.ValueChanged += (_, _) => DatumInfo();
        dBis.ValueChanged += (_, _) => DatumInfo();
        sj.CheckedChanged += (_, _) =>
        {
            if (!sj.Checked) return;
            dVon.Value = dd.AktivePeriode.Von.ToDateTime(TimeOnly.MinValue);
            dBis.Value = (dd.AktivePeriode.Bis ?? dd.Einstellungen.SchuljahrBis).ToDateTime(TimeOnly.MinValue);
        };
        kw.CheckedChanged += (_, _) =>
        {
            if (!kw.Checked) return;
            var mo = Kalender.Montag(DateOnly.FromDateTime(dVon.Value));
            dVon.Value = mo.ToDateTime(TimeOnly.MinValue);
            dBis.Value = mo.AddDays(Math.Max(0, tagNamen.Count - 1)).ToDateTime(TimeOnly.MinValue);
        };
        einTag.CheckedChanged += (_, _) => { if (einTag.Checked) dBis.Value = dVon.Value; };
        DatumInfo();
        datum.Controls.AddRange(new Control[] { dVon, drehVon, dBis, drehBis, infoVon, infoBis, kw, sj, einTag });
        bereich.Controls.Add(datum);

        // ---------------- Layout 1 ----------------
        var layout1 = new TabPage("Layout 1");
        var gestaltung = new GroupBox { Text = "Gestaltung der Felder", Left = 8, Top = 6, Width = 530, Height = 150 };
        (RadioButton[] Raum, RadioButton[] Eben) Bereichsgruppe(string titel, int x, FeldGestaltung g)
        {
            var box = new GroupBox { Text = titel, Left = x, Top = 18, Width = 160, Height = 124 };
            // eigene Felder für „Räumlich“ und „Eben“, damit beide Auswahlgruppen unabhängig sind (unter der Gruppenüberschrift)
            var raumPanel = new Panel { Left = 2, Top = 14, Width = 156, Height = 68 };
            raumPanel.Controls.Add(new Label { Text = "Räumlich (3D):", Left = 6, Top = 0, Width = 140, Height = 15 });
            var raum = new[] { "Erhaben", "Versenkt", "Flach" }.Select((t, i) => new RadioButton { Text = t, Left = 10, Top = 15 + i * 17, Width = 120, Height = 17, Checked = (int)g.Raeumlich == i }).ToArray();
            raumPanel.Controls.AddRange(raum);
            var ebenPanel = new Panel { Left = 2, Top = 84, Width = 156, Height = 36 };
            ebenPanel.Controls.Add(new Label { Text = "Eben:", Left = 6, Top = 1, Width = 50, Height = 15 });
            var eben = new[] { new RadioButton { Text = "Grau", Left = 64, Top = 0, Width = 70, Height = 17, Checked = g.Grau }, new RadioButton { Text = "Weiß", Left = 64, Top = 17, Width = 70, Height = 17, Checked = !g.Grau } };
            ebenPanel.Controls.AddRange(eben);
            box.Controls.Add(raumPanel);
            box.Controls.Add(ebenPanel);
            gestaltung.Controls.Add(box);
            return (raum, eben);
        }
        var gUeber = Bereichsgruppe("Überschrift", 8, f.Ueberschrift);
        var gBesetzt = Bereichsgruppe("Besetzte Stunden", 184, f.BesetzteStunden);
        var gFrei = Bereichsgruppe("Freie Stunden", 360, f.FreieStunden);
        layout1.Controls.Add(gestaltung);
        // Vorbild: Beschriftung jeweils über dem Eingabefeld
        TextBox Linie(string text, string nameFeld, string wert, int yy)
        {
            layout1.Controls.Add(new Label { Text = text, Left = 10, Top = yy, Width = 200 });
            var t = new TextBox { Name = nameFeld, Text = wert, Left = 10, Top = yy + 18, Width = 170 };
            layout1.Controls.Add(t);
            return t;
        }
        var linieVor = Linie("Linie vor folgenden Stunden:", "LinieVor", f.LinieVor, 164);
        var doppel = Linie("Doppelstrich vor Stunden:", "DoppelstrichVor", f.DoppelstrichVor, 208);
        var dick = Linie("Dicker Strich vor Stunden:", "DickerStrichVor", f.DickerStrichVor, 252);
        var trennVn = new CheckBox { Text = "Trennstrich Vorm. / Nachm.", Left = 220, Top = 180, Width = 200, Height = 20, Checked = f.TrennstrichVormNachm };
        var trennTage = new CheckBox { Name = "TrennstrichTage", Text = "Trennstrich zwischen Tagen", Left = 220, Top = 200, Width = 200, Height = 20, Checked = f.TrennstrichTage };
        layout1.Controls.AddRange(new Control[] { trennVn, trennTage });

        // ---------------- Layout 2 ----------------
        var layout2 = new TabPage("Layout 2");
        CheckBox K(Control box, string text, bool wert, ref int yy, int x = 10, int w = 240)
        {
            var c = new CheckBox { Text = text, Left = x, Top = yy, Width = w, Height = 20, Checked = wert };
            box.Controls.Add(c);
            yy += 20;
            return c;
        }
        var gStunde = new GroupBox { Text = "Stundenplan-Stunde", Left = 8, Top = 4, Width = 262, Height = 186 };
        var sy = 18;
        var dsEinzel = K(gStunde, "Doppelstunden wie Einzelstunden", f.Einzelstunden, ref sy);
        var komb = K(gStunde, "Komb. Klassen (2a,2b->2ab)", f.KombinierteKlassen, ref sy);
        var alleKl = K(gStunde, "Alle Klassen in einem Feld", f.AlleKlassenEinFeld, ref sy);
        var punkt = K(gStunde, "Kopplungen mit Punkt kennzeichnen", f.KopplungenMitPunkt, ref sy);
        var stern = K(gStunde, "Fixierung mit * kennzeichnen", f.FixierungMitStern, ref sy);
        var kolTrennen = K(gStunde, "Stunden bei Kollision trennen", f.KollisionTrennen, ref sy);
        var sortieren = K(gStunde, "Stundenfelder sortieren", f.StundenfelderSortieren, ref sy);
        var verbinden = K(gStunde, "Stunden versch. Elemente verbinden", f.ElementeVerbinden, ref sy);
        // Vorbild: Vertretungsplanung mit eingerückten Absenz-Optionen und eigenem Rahmen „Geänderte Stunden“
        var gVertr = new GroupBox { Text = "Vertretungsplanung", Left = 8, Top = 194, Width = 262, Height = 214 };
        var vy = 16;
        var absenzen = K(gVertr, "Absenzen anzeigen", f.AbsenzenAnzeigen, ref vy);
        var keinGrund = K(gVertr, "Kein Absenzgrund", f.KeinAbsenzgrund, ref vy, 26, 220);
        var nurTag = K(gVertr, "Nur Tagesabsenzen", f.NurTagesabsenzen, ref vy, 26, 220);
        var nurVertr = K(gVertr, "Nur Vertretungsstunden", f.NurVertretungsstunden, ref vy);
        var gGeaendert = new GroupBox { Text = "Geänderte Stunden", Left = 8, Top = vy, Width = 246, Height = 214 - vy - 6 };
        var gy = 16;
        var durchstreichen = K(gGeaendert, "Entfälle durchstreichen", f.EntfaelleDurchstreichen, ref gy, 6, 236);
        var nichtAnzeigen = K(gGeaendert, "Entfälle und Platzhalter nicht anzeigen", f.EntfaelleNichtAnzeigen, ref gy, 22, 222);
        nichtAnzeigen.Enabled = !durchstreichen.Checked;
        durchstreichen.CheckedChanged += (_, _) => nichtAnzeigen.Enabled = !durchstreichen.Checked;
        var rot = K(gGeaendert, "Rot", f.GeaendertRot, ref gy, 6, 110);
        gy -= 20;
        var mitVertr = K(gGeaendert, "Vertretungen anzeigen", f.MitVertretungen, ref gy, 122, 120);
        var fett = K(gGeaendert, "Fett", f.GeaendertFett, ref gy, 6, 236);
        var kursiv = K(gGeaendert, "Kursiv", f.GeaendertKursiv, ref gy, 6, 236);
        var ausrufe = K(gGeaendert, "Mit ! hervorheben", f.GeaendertAusrufezeichen, ref gy, 6, 236);
        var unterstreichen = K(gGeaendert, "Unterstreichen", f.GeaendertUnterstreichen, ref gy, 6, 236);
        gVertr.Controls.Add(gGeaendert);
        var gManuell = new GroupBox { Text = "Manuelles Planen", Left = 280, Top = 4, Width = 262, Height = 102 };
        var my = 18;
        var ddMehrere = K(gManuell, "DragDrop: mehrere Unterrichte", f.DragDropMehrereUnterrichte, ref my);
        var ddFarben = K(gManuell, "DragDrop: Farben laut Zeitwünsche", f.DragDropZeitwunschFarben, ref my);
        var ddTausch = K(gManuell, "DragDrop: Tauschketten berechnen", f.DragDropTauschketten, ref my);
        var bandmodus = K(gManuell, "Bandmodus", f.Bandmodus, ref my);
        var gDruck = new GroupBox { Text = "Druck", Left = 280, Top = 108, Width = 262, Height = 102 };
        var dy = 18;
        var leerZ = K(gDruck, "Leere Zeilen nicht drucken", f.LeereZeilenNichtDrucken, ref dy);
        var leerS = K(gDruck, "Leere Spalte nicht drucken", f.LeereSpalteNichtDrucken, ref dy);
        var sw = K(gDruck, "Schwarz-Weiß-Druck", f.SchwarzWeissDruck, ref dy);
        var eineUeb = K(gDruck, "1 Überschrift je Blatt", f.EineUeberschriftJeBlatt, ref dy);
        var oy = 216;
        var autosize = K(layout2, "Autosize für Lupe", f.AutosizeLupe, ref oy, 284);
        var htmlLang = K(layout2, "Html-Indexseite mit Langnamen", f.HtmlIndexLangnamen, ref oy, 284);
        var minutAnzeige = K(layout2, "Stpl-Anzeige im Minut-Modus", f.MinutModus, ref oy, 284);
        var hauptKl = K(layout2, "Hauptklassen getrennt anzeigen", f.HauptklassenGetrennt, ref oy, 284);
        var pausenBez = K(layout2, "Pausenbezeichnung", f.Pausenbezeichnung, ref oy, 284);
        oy += 30;
        var aliasLupe = K(layout2, "Für Lupe / Legende Alias verwenden", f.LupeLegendeAlias, ref oy, 284);
        layout2.Controls.AddRange(new Control[] { gStunde, gVertr, gManuell, gDruck });
        // „Doppelstunden wie Einzelstunden“ und „Einzelstunden“ (Allgemein) sind dieselbe Einstellung
        dsEinzel.CheckedChanged += (_, _) => { if (einzel.Checked != dsEinzel.Checked) einzel.Checked = dsEinzel.Checked; };
        einzel.CheckedChanged += (_, _) => { if (dsEinzel.Checked != einzel.Checked) dsEinzel.Checked = einzel.Checked; };
        minutAnzeige.CheckedChanged += (_, _) => { if (minutAn.Checked != minutAnzeige.Checked) minutAn.Checked = minutAnzeige.Checked; };
        minutAn.CheckedChanged += (_, _) => { if (minutAnzeige.Checked != minutAn.Checked) minutAnzeige.Checked = minutAn.Checked; };

        // ---------------- HTML ----------------
        var html = new TabPage("HTML");
        var hy = 10;
        var htmlTitel = Ui.Zeile(html, "Überschrift der HTML-Seiten", new TextBox { Text = f.HtmlTitel }, ref hy, 190, 250);
        var htmlIndex = Ui.Zeile(html, "Indexseite erzeugen", new CheckBox { Checked = f.HtmlIndexseite }, ref hy, 190);
        var htmlFarben = Ui.Zeile(html, "Mit Fachfarben", new CheckBox { Checked = f.HtmlMitFarben }, ref hy, 190);
        html.Controls.Add(new Label { Text = "Die HTML-Ausgabe (Werkzeugleiste des Stundenplans) erzeugt je Element eine Seite im Aussehen dieses Formats.", Left = 12, Top = hy + 6, Width = 520, Height = 40, ForeColor = Color.DimGray });

        tabs.TabPages.AddRange(new[] { allg, bereich, layout1, layout2, html });
        d.Controls.Add(tabs);

        // Vorschau mit dem aktuellen Element (rechts, auf allen Karten sichtbar)
        var vorschau = new PlanAnsicht { Left = 578, Top = 30, Width = 330, Height = 418, Bearbeitbar = false, Typ = typ ?? f.Typ };
        vorschau.ElementId = elementId ?? Stammdaten.Sortiert(dd, vorschau.Typ).FirstOrDefault()?.Id ?? 0;
        d.Controls.Add(new Label { Text = $"Vorschau: {AppZustand.Doc.Index.Element(vorschau.Typ, vorschau.ElementId)?.Kurzname}", Left = 578, Top = 10, Width = 300, ForeColor = Color.DimGray });
        d.Controls.Add(vorschau);

        int Zahl(TextBox t, int vorgabe) => int.TryParse(t.Text.Trim(), out var n) ? n : vorgabe;
        (int Tag, int Stunde)? Wochenstunde(string text)
        {
            var teile = text.Trim().Split('-');
            if (teile.Length != 2 || !int.TryParse(teile[1], out var st)) return null;
            var tag = tagNamen.FindIndex(n => n.Equals(teile[0].Trim(), StringComparison.CurrentCultureIgnoreCase));
            return tag < 0 ? null : (tag, st);
        }
        static Raeumlich Raum(RadioButton[] r) => (Raeumlich)Math.Max(0, Array.FindIndex(r, x => x.Checked));

        // Werte in ein Format übernehmen (Vorschau und OK)
        void Schreiben(StundenplanFormat z)
        {
            z.Name = name.Text;
            z.Kurzname = kurzname.Text.Trim();
            z.Grundform = grundform;
            z.Schriftart = schriftart;
            z.LupeSchriftProzent = (int)lupeProzent.Value;
            z.LegendeSchriftProzent = (int)legendeProzent.Value;
            z.UeberschriftSchriftProzent = (int)ueberProzent.Value;
            spaltenWerte.In(z);
            z.ZeilenArt = zeilenArt;
            (z.ZeilenNurBeginn, z.ZeilenFett, z.ZeilenKursiv, z.ZeilenUnterstrichen, z.ZeilenSchriftProzent) =
                (zeilenWerte.NurBeginn, zeilenWerte.Fett, zeilenWerte.Kursiv, zeilenWerte.Unterstrichen, zeilenWerte.Prozent);
            z.Bandmodus = bandmodus.Checked;
            z.LupeLegendeAlias = aliasLupe.Checked;
            z.EntfaelleNichtAnzeigen = nichtAnzeigen.Checked;
            z.GeaendertRot = rot.Checked;
            z.GeaendertFett = fett.Checked;
            z.GeaendertKursiv = kursiv.Checked;
            z.GeaendertAusrufezeichen = ausrufe.Checked;
            z.GeaendertUnterstreichen = unterstreichen.Checked;
            z.Schriftgroesse = (int)schrift.Value;
            z.Farbe = (FarbeNach)Math.Max(0, farbe.SelectedIndex);
            z.Langname = lang.Checked;
            z.ZeitenAnzeigen = zeiten.Checked;
            z.Einzelstunden = einzel.Checked;
            z.PausenAnzeigen = pausen.Checked;
            z.Klein = klein.Checked;
            z.ZellenBreite = (int)breite.Value;
            z.ZellenHoehe = (int)hoehe.Value;
            z.StundenplanlupeAnzeigen = lupe.Checked;
            z.DragDropZeitwunschFarben = ddFarben.Checked;
            z.Felder = felder.Select(Kopie).ToList();
            z.Standardformat = std.Checked || z.Felder.Count == 0;
            (z.FelderHorizontalZentrieren, z.FelderVertikalZentrieren, z.BeginnEndzeit, z.NamensArt, z.AliasVerwenden, z.LehrerUndVertreter) = stundeEinstellungen;
            // Auswahl (Bereich): Wochenstunden bestimmen die Tage, die Stunden-Nummern die Stunden
            z.VonStunde = Math.Max(1, Zahl(vonStd, 1));
            var bis = Zahl(bisStd, 0);
            z.BisStunde = bis >= dd.Zeitraster.StundenProTag ? 0 : Math.Max(0, bis);
            z.VonTag = Wochenstunde(vonWs.Text)?.Tag ?? 0;
            var bisTag = Wochenstunde(bisWs.Text)?.Tag ?? tagNamen.Count - 1;
            z.BisTag = bisTag >= tagNamen.Count - 1 ? -1 : bisTag;
            z.Zeitraum = sj.Checked ? FormatZeitraum.GanzesSchuljahr : einTag.Checked ? FormatZeitraum.EinTag : FormatZeitraum.Kalenderwoche;
            z.DatumVon = DateOnly.FromDateTime(dVon.Value);
            z.DatumBis = DateOnly.FromDateTime(dBis.Value);
            z.MinutModus = minutAn.Checked;
            z.GanzenTagZeigen = ganzerTag.Checked;
            z.Ueberschrift = new FeldGestaltung { Raeumlich = Raum(gUeber.Raum), Grau = gUeber.Eben[0].Checked };
            z.BesetzteStunden = new FeldGestaltung { Raeumlich = Raum(gBesetzt.Raum), Grau = gBesetzt.Eben[0].Checked };
            z.FreieStunden = new FeldGestaltung { Raeumlich = Raum(gFrei.Raum), Grau = gFrei.Eben[0].Checked };
            z.LinieVor = linieVor.Text.Trim();
            z.DoppelstrichVor = doppel.Text.Trim();
            z.DickerStrichVor = dick.Text.Trim();
            z.TrennstrichVormNachm = trennVn.Checked;
            z.TrennstrichTage = trennTage.Checked;
            z.KombinierteKlassen = komb.Checked;
            z.AlleKlassenEinFeld = alleKl.Checked;
            z.KopplungenMitPunkt = punkt.Checked;
            z.FixierungMitStern = stern.Checked;
            z.KollisionTrennen = kolTrennen.Checked;
            z.StundenfelderSortieren = sortieren.Checked;
            z.ElementeVerbinden = verbinden.Checked;
            z.AbsenzenAnzeigen = absenzen.Checked;
            z.KeinAbsenzgrund = keinGrund.Checked;
            z.NurTagesabsenzen = nurTag.Checked;
            z.NurVertretungsstunden = nurVertr.Checked;
            z.EntfaelleDurchstreichen = durchstreichen.Checked;
            z.MitVertretungen = mitVertr.Checked;
            z.DragDropMehrereUnterrichte = ddMehrere.Checked;
            z.DragDropTauschketten = ddTausch.Checked;
            z.LeereZeilenNichtDrucken = leerZ.Checked;
            z.LeereSpalteNichtDrucken = leerS.Checked;
            z.SchwarzWeissDruck = sw.Checked;
            z.EineUeberschriftJeBlatt = eineUeb.Checked;
            z.AutosizeLupe = autosize.Checked;
            z.HtmlIndexLangnamen = htmlLang.Checked;
            z.HauptklassenGetrennt = hauptKl.Checked;
            z.Pausenbezeichnung = pausenBez.Checked;
            z.HtmlTitel = htmlTitel.Text.Trim();
            z.HtmlIndexseite = htmlIndex.Checked;
            z.HtmlMitFarben = htmlFarben.Checked;
        }
        void Vorschau()
        {
            var z = new StundenplanFormat { Typ = f.Typ, Gross = f.Gross };
            Schreiben(z);
            vorschau.Format = z;
            vorschau.Invalidate();
            d.Text = StundenplanFormat.GrundformText(grundform);
        }
        // Stunden-Nummern und Wochenstunden gleich halten
        var abgleich = false;
        void Abgleichen(bool ausWochenstunden)
        {
            if (abgleich) return;
            abgleich = true;
            try
            {
                if (ausWochenstunden)
                {
                    if (Wochenstunde(vonWs.Text) is { } v) vonStd.Text = v.Stunde.ToString();
                    if (Wochenstunde(bisWs.Text) is { } b) bisStd.Text = b.Stunde.ToString();
                }
                else
                {
                    vonWs.Text = WochenStd(Wochenstunde(vonWs.Text)?.Tag ?? 0, Zahl(vonStd, 1));
                    bisWs.Text = WochenStd(Wochenstunde(bisWs.Text)?.Tag ?? tagNamen.Count - 1, Zahl(bisStd, dd.Zeitraster.StundenProTag));
                }
            }
            finally { abgleich = false; }
            Vorschau();
        }
        vonStd.TextChanged += (_, _) => Abgleichen(false);
        bisStd.TextChanged += (_, _) => Abgleichen(false);
        vonWs.TextChanged += (_, _) => Abgleichen(true);
        bisWs.TextChanged += (_, _) => Abgleichen(true);
        foreach (var n in new[] { schrift, breite, hoehe }) n.ValueChanged += (_, _) => Vorschau();
        farbe.SelectedIndexChanged += (_, _) => Vorschau();
        foreach (var t in new[] { linieVor, doppel, dick }) t.TextChanged += (_, _) => Vorschau();
        foreach (var c in AlleKontrollen(tabs).OfType<CheckBox>()) c.CheckedChanged += (_, _) => Vorschau();
        foreach (var r in AlleKontrollen(tabs).OfType<RadioButton>()) r.CheckedChanged += (_, _) => { if (r.Checked) Vorschau(); };
        stundeKnopf.Click += (_, _) =>
        {
            var z = new StundenplanFormat { Typ = f.Typ, Gross = f.Gross };
            Schreiben(z);
            if (StundenplanStundeDialog.Zeigen(d, z, vorschau.Typ, vorschau.ElementId) is not { } neu) return;
            felder = neu.Felder.Select(Kopie).ToList();
            stundeEinstellungen = (neu.FelderHorizontalZentrieren, neu.FelderVertikalZentrieren, neu.BeginnEndzeit, neu.NamensArt, neu.AliasVerwenden, neu.LehrerUndVertreter);
            std.Checked = neu.Standardformat;
            Vorschau();
            if (sofort != null)
            {
                // Stundenplan-Stunde sofort übernehmen und speichern (nicht erst beim OK des Formatdialogs)
                var stand = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.Serialize(f))!;
                Schreiben(stand);
                sofort(stand);
            }
        };
        grundKnopf.Click += (_, _) =>
        {
            if (StundenplanGrundformDialog.Zeigen(d, grundform) is not { } g) return;
            grundform = g;
            formatText.Text = StundenplanFormat.GrundformText(g);
            Vorschau();
        };
        schriftKnopf.Click += (_, _) =>
        {
            using var fd = new FontDialog { ShowEffects = false, AllowVerticalFonts = false, MinSize = 6, MaxSize = 20, FontMustExist = true };
            try { fd.Font = new Font(schriftart.Length > 0 ? schriftart : Ui.Schrift.FontFamily.Name, (float)schrift.Value); } catch (ArgumentException) { }
            if (fd.ShowDialog(d) != DialogResult.OK) return;
            schriftart = fd.Font.FontFamily.Name;
            schrift.Value = Math.Clamp((decimal)Math.Round(fd.Font.SizeInPoints), schrift.Minimum, schrift.Maximum);
            SchriftAnzeigen();
            Vorschau();
        };
        spaltenKnopf.Click += (_, _) =>
        {
            void Setzen(SpaltenBeschriftungWerte w) { spaltenWerte = w; Vorschau(); }
            if (BeschriftungDialoge.Spalten(d, spaltenWerte, Setzen) is { } neu) Setzen(neu);
        };
        zeilenKnopf.Click += (_, _) =>
        {
            void Setzen(ZeilenBeschriftungWerte w)
            {
                zeilenWerte = w;
                if (w.Art != ZeilenBeschriftung.Nummer) zeilenArt = w.Art;
                zeiten.Checked = w.Art != ZeilenBeschriftung.Nummer;
                Vorschau();
            }
            if (BeschriftungDialoge.Zeilen(d, zeilenWerte with { Art = zeiten.Checked ? zeilenArt : ZeilenBeschriftung.Nummer }, Setzen) is { } neu) Setzen(neu);
        };
        // Vorbild: Ok, Abbrechen, Übernehmen
        d.Ok.Text = "Ok";
        var uebernehmenKnopf = new Button { Text = "Übernehmen", Width = 88, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Location = new Point(d.ClientSize.Width - 96, d.ClientSize.Height - 36) };
        d.Ok.Left -= 92;
        d.Abbrechen.Left -= 92;
        uebernehmenKnopf.Click += (_, _) =>
        {
            if (sofort is null) return;
            var stand = System.Text.Json.JsonSerializer.Deserialize<StundenplanFormat>(System.Text.Json.JsonSerializer.Serialize(f))!;
            Schreiben(stand);
            sofort(stand);
        };
        d.Controls.Add(uebernehmenKnopf);
        Vorschau();
        uebernehmen = () => Schreiben(f);
        return d;
    }

    internal static IEnumerable<Control> AlleKontrollen(Control c) => c.Controls.Cast<Control>().SelectMany(k => new[] { k }.Concat(AlleKontrollen(k)));

    internal static StundenFeld Kopie(StundenFeld x) => new()
    {
        Art = x.Art, Zeile = x.Zeile, X = x.X, Y = x.Y, B = x.B, H = x.H, SchriftProzent = x.SchriftProzent, Fett = x.Fett,
        Kursiv = x.Kursiv, Unterstrichen = x.Unterstrichen, Ausrichtung = x.Ausrichtung,
    };
}

/// <summary>Vorbild „Stundenplanformate – Grundformen der Stundenpläne“: Auswahl der Grundform beim Anlegen eines Formats.</summary>
public static class StundenplanGrundformDialog
{
    private static readonly (Grundform Form, string Text, bool Einzel)[] Formen =
    {
        (Grundform.Format01, "Format 01\nEinzelstundenplan Tage waagrecht, Stunden senkrecht", true),
        (Grundform.Format10, "Format 10\nStunden waagrecht, Tage senkrecht", true),
        (Grundform.Format11, "Format 11\nUnterrichtswochen waagrecht, Stunden der Woche senkrecht", true),
        (Grundform.Format20, "Format 20\nStunden der Woche senkrecht, Klassen (Lehrer, Räume) waagrecht", false),
        (Grundform.Format21, "Format 21\nWie Format 20 jedoch mehrere Wochen nebeneinander", false),
        (Grundform.Format30, "Format 30\nStunden der Woche waagrecht, Klassen (Lehrer, Räume) senkrecht", false),
        (Grundform.Format40, "Format 40\nEinzelstundenplan mit mehreren Elementen (Klassen, Lehrer, Räume)", false),
    };

    public static Grundform? Zeigen(IWin32Window owner, Grundform aktuell = Grundform.Format01)
    {
        using var d = Erstellen(aktuell, out var wahl);
        return d.ShowDialog(owner) == DialogResult.OK ? wahl() : null;
    }

    /// <summary>Nicht modal (Selbsttest).</summary>
    public static Form Anzeigen(IWin32Window owner, Grundform aktuell = Grundform.Format01)
    {
        var d = Erstellen(aktuell, out _);
        d.Show(owner);
        return d;
    }

    private static Dialog Erstellen(Grundform aktuell, out Func<Grundform> wahl)
    {
        var d = new Dialog("Stundenplanformate", 640, 420) { Font = Ui.Schrift };
        d.Controls.Add(new Label { Text = "Grundformen der Stundenpläne", Left = 240, Top = 8, Width = 200 });
        var einzel = new GroupBox { Text = "Einzelstundenpläne", Left = 10, Top = 26, Width = 300, Height = 330 };
        var uebersicht = new GroupBox { Text = "Stundenplan-Übersichtspläne", Left = 320, Top = 26, Width = 308, Height = 330 };
        var knoepfe = new List<(RadioButton, Grundform)>();
        int ye = 18, yu = 18;
        foreach (var (form, text, istEinzel) in Formen)
        {
            var box = istEinzel ? einzel : uebersicht;
            var y = istEinzel ? ye : yu;
            var bild = new PictureBox { Left = 8, Top = y, Width = 84, Height = 66, Image = Vorschaubild(form), BorderStyle = BorderStyle.None };
            var teile = text.Split('\n');
            var rb = new RadioButton { Text = teile[0], Left = 98, Top = y, Width = 190, Height = 18, Checked = form == aktuell, Name = form.ToString() };
            var lbl = new Label { Text = teile[1], Left = 98, Top = y + 18, Width = 196, Height = 44 };
            box.Controls.AddRange(new Control[] { bild, rb, lbl });
            knoepfe.Add((rb, form));
            if (istEinzel) ye += 100; else yu += 76;
        }
        // alle Formate gehören zu einer Auswahl (über beide Gruppen)
        foreach (var (rb, _) in knoepfe)
            rb.CheckedChanged += (_, _) => { if (rb.Checked) foreach (var (o, _) in knoepfe) if (o != rb) o.Checked = false; };
        d.Controls.AddRange(new Control[] { einzel, uebersicht });
        d.Ok.Left = 12;
        d.Abbrechen.Left = 110;
        d.Ok.Anchor = d.Abbrechen.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        wahl = () => knoepfe.FirstOrDefault(k => k.Item1.Checked).Item2 is var g && g != 0 ? g : Grundform.Format01;
        return d;
    }

    /// <summary>Kleine schematische Vorschau der Grundform (wie die Bilder im Vorbild-Dialog).</summary>
    public static Bitmap Vorschaubild(Grundform g)
    {
        var bmp = new Bitmap(84, 66);
        using var gr = Graphics.FromImage(bmp);
        gr.Clear(Color.White);
        using var p = new Pen(Color.Black);
        using var f = new Font("Segoe UI", 6.5f, FontStyle.Bold);
        void Raster(int x0, int y0, int spalten, int zeilen, int kopfB, int kopfH, string[] koepfe, string[] zeilenKoepfe)
        {
            var w = (84 - x0 - kopfB - 1) / spalten;
            var h = (66 - y0 - kopfH - 1) / zeilen;
            gr.DrawRectangle(p, x0, y0, kopfB + w * spalten, kopfH + h * zeilen);
            gr.DrawLine(p, x0, y0 + kopfH, x0 + kopfB + w * spalten, y0 + kopfH);
            gr.DrawLine(p, x0 + kopfB, y0, x0 + kopfB, y0 + kopfH + h * zeilen);
            for (var i = 1; i < spalten; i++) gr.DrawLine(p, x0 + kopfB + i * w, y0 + (koepfe.Length > 0 ? 0 : kopfH), x0 + kopfB + i * w, y0 + kopfH + h * zeilen);
            for (var i = 1; i < zeilen; i++) gr.DrawLine(p, x0, y0 + kopfH + i * h, x0 + kopfB + w * spalten, y0 + kopfH + i * h);
            for (var i = 0; i < koepfe.Length && i < spalten; i++) TextRenderer.DrawText(gr, koepfe[i], f, new Rectangle(x0 + kopfB + i * w, y0, w, kopfH), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            for (var i = 0; i < zeilenKoepfe.Length && i < zeilen; i++) TextRenderer.DrawText(gr, zeilenKoepfe[i], f, new Rectangle(x0, y0 + kopfH + i * h, kopfB, h), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        switch (g)
        {
            case Grundform.Format10: Raster(2, 2, 3, 3, 22, 14, new[] { "1", "2", "3" }, new[] { "Mo", "Tu" }); break;
            case Grundform.Format11:
                TextRenderer.DrawText(gr, "Woche", f, new Rectangle(30, 2, 50, 12), Color.Black, TextFormatFlags.HorizontalCenter);
                Raster(2, 14, 4, 3, 26, 12, new[] { "1", "2", "3", "4" }, new[] { "Mo-1", "2", "3" }); break;
            case Grundform.Format20 or Grundform.Format21:
                TextRenderer.DrawText(gr, "Montag", f, new Rectangle(24, 2, 58, 12), Color.Black, TextFormatFlags.HorizontalCenter);
                Raster(2, 14, 4, 2, 22, 12, new[] { "1", "2", "3", "4" }, new[] { "1a", "1b" }); break;
            case Grundform.Format30: Raster(2, 2, 2, 3, 26, 14, new[] { "1a", "1b" }, new[] { "Mo-1", "2", "3" }); break;
            case Grundform.Format40:
                TextRenderer.DrawText(gr, "Mon  Tu", f, new Rectangle(20, 2, 62, 12), Color.Black, TextFormatFlags.HorizontalCenter);
                Raster(2, 14, 4, 2, 16, 12, new[] { "1a", "1b", "1c", "1a" }, new[] { "1", "2" }); break;
            default: Raster(2, 2, 3, 2, 16, 14, new[] { "Mo", "Tu", "" }, new[] { "1", "2" }); break;
        }
        return bmp;
    }
}
