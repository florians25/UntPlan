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
        var allg = new TabPage("Allgemein");
        var y = 10;
        var name = Ui.Zeile(allg, "Formatname", new TextBox { Text = f.Name }, ref y, 170, 200);
        var schrift = Ui.Zeile(allg, "Schriftgröße", new NumericUpDown { Minimum = 6, Maximum = 20, Value = Math.Clamp(f.Schriftgroesse, 6, 20) }, ref y, 170, 70);
        var farbe = Ui.Zeile(allg, "Farbe nach", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, ref y, 170, 120);
        farbe.Items.AddRange(new object[] { "Fach", "Lehrer", "Klasse", "Raum", "keine" });
        farbe.SelectedIndex = (int)f.Farbe;
        var lang = Ui.Zeile(allg, "Langnamen anzeigen", new CheckBox { Checked = f.Langname }, ref y);
        var zeiten = Ui.Zeile(allg, "Uhrzeiten anzeigen", new CheckBox { Checked = f.ZeitenAnzeigen }, ref y);
        var einzel = Ui.Zeile(allg, "Einzelstunden (nicht zusammenfassen)", new CheckBox { Checked = f.Einzelstunden }, ref y, 220);
        var pausen = Ui.Zeile(allg, "Pausen anzeigen", new CheckBox { Checked = f.PausenAnzeigen }, ref y, 220);
        var klein = Ui.Zeile(allg, "Kleiner Stundenplan (nur Fachkürzel)", new CheckBox { Checked = f.Klein }, ref y, 220);
        var breite = Ui.Zeile(allg, "Zellenbreite (0 = automatisch)", new NumericUpDown { Minimum = 0, Maximum = 400, Value = Math.Clamp(f.ZellenBreite, 0, 400) }, ref y, 220, 60);
        var hoehe = Ui.Zeile(allg, "Zellenhöhe (0 = automatisch)", new NumericUpDown { Minimum = 0, Maximum = 200, Value = Math.Clamp(f.ZellenHoehe, 0, 200) }, ref y, 220, 60);
        var lupe = Ui.Zeile(allg, "Stundenplanlupe anzeigen", new CheckBox { Checked = f.StundenplanlupeAnzeigen }, ref y, 220);
        var std = Ui.Zeile(allg, "Stpl-Stunde: Standardformat", new CheckBox { Checked = f.Standardformat }, ref y, 220);
        var grundform = f.Grundform;
        var stundeKnopf = new Button { Text = "Stundenplan-Stunde…", Left = 12, Top = y + 6, Width = 170, Height = 26 };
        var grundKnopf = new Button { Text = "Grundform…", Left = 190, Top = y + 6, Width = 120, Height = 26 };
        allg.Controls.AddRange(new Control[] { stundeKnopf, grundKnopf });

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
        minut.Controls.AddRange(new Control[] { minutAn, new Label { Text = "Beginn:", Left = 124, Top = 20, Width = 50 }, beginn, ende, ganzerTag });
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
        var sj = new RadioButton { Text = "Ganzes Schuljahr", Left = 300, Top = 42, Width = 150, Checked = f.Zeitraum == FormatZeitraum.GanzesSchuljahr };
        var einTag = new RadioButton { Text = "1 Tag", Left = 300, Top = 64, Width = 150, Checked = f.Zeitraum == FormatZeitraum.EinTag };
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
            dVon.Value = dd.Einstellungen.SchuljahrVon.ToDateTime(TimeOnly.MinValue);
            dBis.Value = dd.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue);
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
        var ly = 166;
        var linieVor = Ui.Zeile(layout1, "Linie vor folgenden Stunden:", new TextBox { Name = "LinieVor", Text = f.LinieVor }, ref ly, 190, 150);
        var doppel = Ui.Zeile(layout1, "Doppelstrich vor Stunden:", new TextBox { Name = "DoppelstrichVor", Text = f.DoppelstrichVor }, ref ly, 190, 150);
        var dick = Ui.Zeile(layout1, "Dicker Strich vor Stunden:", new TextBox { Name = "DickerStrichVor", Text = f.DickerStrichVor }, ref ly, 190, 150);
        var trennVn = new CheckBox { Text = "Trennstrich Vorm. / Nachm.", Left = 360, Top = 168, Width = 190, Checked = f.TrennstrichVormNachm };
        var trennTage = new CheckBox { Name = "TrennstrichTage", Text = "Trennstrich zwischen Tagen", Left = 360, Top = 192, Width = 190, Checked = f.TrennstrichTage };
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
        var gVertr = new GroupBox { Text = "Vertretungsplanung", Left = 8, Top = 194, Width = 262, Height = 176 };
        var vy = 18;
        var absenzen = K(gVertr, "Absenzen anzeigen", f.AbsenzenAnzeigen, ref vy);
        var keinGrund = K(gVertr, "Kein Absenzgrund", f.KeinAbsenzgrund, ref vy, 26, 220);
        var nurTag = K(gVertr, "Nur Tagesabsenzen", f.NurTagesabsenzen, ref vy, 26, 220);
        var nurVertr = K(gVertr, "Nur Vertretungsstunden", f.NurVertretungsstunden, ref vy);
        gVertr.Controls.Add(new Label { Text = "Geänderte Stunden", Left = 10, Top = vy + 2, Width = 200, ForeColor = Color.DimGray });
        vy += 20;
        var durchstreichen = K(gVertr, "Entfälle durchstreichen", f.EntfaelleDurchstreichen, ref vy);
        var mitVertr = K(gVertr, "Vertretungen anzeigen", f.MitVertretungen, ref vy);
        var gManuell = new GroupBox { Text = "Manuelles Planen", Left = 280, Top = 4, Width = 262, Height = 82 };
        var my = 18;
        var ddMehrere = K(gManuell, "DragDrop: mehrere Unterrichte", f.DragDropMehrereUnterrichte, ref my);
        var ddFarben = K(gManuell, "DragDrop: Farben laut Zeitwünsche", f.DragDropZeitwunschFarben, ref my);
        var ddTausch = K(gManuell, "DragDrop: Tauschketten berechnen", f.DragDropTauschketten, ref my);
        var gDruck = new GroupBox { Text = "Druck", Left = 280, Top = 90, Width = 262, Height = 102 };
        var dy = 18;
        var leerZ = K(gDruck, "Leere Zeilen nicht drucken", f.LeereZeilenNichtDrucken, ref dy);
        var leerS = K(gDruck, "Leere Spalte nicht drucken", f.LeereSpalteNichtDrucken, ref dy);
        var sw = K(gDruck, "Schwarz-Weiß-Druck", f.SchwarzWeissDruck, ref dy);
        var eineUeb = K(gDruck, "1 Überschrift je Blatt", f.EineUeberschriftJeBlatt, ref dy);
        var oy = 200;
        var autosize = K(layout2, "Autosize für Lupe", f.AutosizeLupe, ref oy, 284);
        var htmlLang = K(layout2, "Html-Indexseite mit Langnamen", f.HtmlIndexLangnamen, ref oy, 284);
        var minutAnzeige = K(layout2, "Stpl-Anzeige im Minut-Modus", f.MinutModus, ref oy, 284);
        var hauptKl = K(layout2, "Hauptklassen getrennt anzeigen", f.HauptklassenGetrennt, ref oy, 284);
        var pausenBez = K(layout2, "Pausenbezeichnung", f.Pausenbezeichnung, ref oy, 284);
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
            z.Grundform = grundform;
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
            Vorschau();
        };
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
