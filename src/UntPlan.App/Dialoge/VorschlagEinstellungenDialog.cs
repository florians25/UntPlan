using System.Globalization;
using UntPlan.App.Fenster;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Einstellungen des Vertretungsvorschlags wie im Vorbild (Handbuch Vertretungsplanung, Kap. 7.3.5 und 7.3.6):
/// <list type="bullet">
/// <item>Karteikarte „Allgemein“: Rot, wenn größer / Nicht anzeigen, wenn größer (Merker zur Stunde, Zähler), Gesperrte Lehrer nicht
/// anzeigen, Langname, Klasse-Häkchen auch f.Klassenteil, Raumgewicht 4 berücksichtigen, Fenster im Hintergrund, Rahmen
/// „Vorziehungen“ (Nur späteren Unterricht, Nur späteren Unterr. der selben Woche, Höhe automatisch anpassen, Max. Prozentsatz der
/// Fensterhöhe) und Schrift.</item>
/// <item>Karteikarte „Gewichtung“: Schieberegler von „unwichtig“ bis „extrem wichtig“ (0–99) für Freisetzung, Bereitschaft, hat Klasse,
/// hat Fach, Unterrichtsstunden am Tag, Merker-, Sperre- und Zähler-Faktor sowie das Feld „Statistik-Kennzeichen“ (z. B. X=20;a=-45).</item>
/// </list>
/// Ok, Abbrechen und Hilfe.
/// </summary>
public static class VorschlagEinstellungenDialog
{
    public static bool Zeigen(IWin32Window owner, int seite = 0)
    {
        using var f = Erzeugen(seite, out var uebernehmen);
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        uebernehmen();
        return true;
    }

    /// <summary>Dialog aufbauen; <paramref name="uebernehmen"/> schreibt die Eingaben in die Schuldatei.</summary>
    internal static Dialog Erzeugen(int seite, out Action uebernehmen)
    {
        var alt = AppZustand.D.Vertretungseinstellungen.Vorschlag;
        // Maße wie im Handbuch-Bild (96 dpi, Kap. 7.3.5/7.3.6): Fenster 421 × 486, Karteikarten bis an den Rand, Ok / Abbrechen / Hilfe
        // 75 × 23 unten rechts. Alle Positionen der Karteikarten-Inhalte sind Fensterkoordinaten wie im Bild (siehe FensterKoordinaten).
        var f = new Dialog("Einstellungen", 421, 486);
        var tabs = new TabControl { Left = 4, Top = 3, Width = 415, Height = 451 };
        f.Controls.Add(tabs);
        f.Ok.Text = "Ok";
        var hilfe = new Button { Text = "Hilfe", Width = 75, Height = 23, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        f.Ok.Size = f.Abbrechen.Size = hilfe.Size;
        f.Ok.Location = new Point(184, 461);
        f.Abbrechen.Location = new Point(264, 461);
        hilfe.Location = new Point(344, 461);
        hilfe.Click += (_, _) => HilfeFenster.Zeigen("Vertretungsvorschlag");
        f.Controls.Add(hilfe);

        // --- Allgemein ---------------------------------------------------------------------------
        var ta = new TabPage("Allgemein") { UseVisualStyleBackColor = true };
        // Zahlenfelder 37 × 23 ohne sichtbare Pfeile wie im Bild, Beschriftung 20 Pixel rechts daneben
        NumericUpDown Zahl(Control c, int y, int wert, int max, string text)
        {
            var n = new NumericUpDown { Left = 15, Top = y, Width = 37, Minimum = 0, Maximum = max, Value = Math.Clamp(wert, 0, max) };
            c.Controls.Add(n);
            c.Controls.Add(new Label { Text = text, Left = 51, Top = y + 5, AutoSize = true });
            return n;
        }
        var rot = new GroupBox { Text = "Rot, wenn größer", Left = 16, Top = 36, Width = 209, Height = 72 };
        var rotMerker = Zahl(rot, 15, alt.RotWennMerkerGroesser, 9, "Merker zur Stunde");
        var rotZaehler = Zahl(rot, 43, alt.RotWennZaehlerGroesser, 999, "Zähler");
        var nicht = new GroupBox { Text = "Nicht anzeigen, wenn größer", Left = 16, Top = 114, Width = 209, Height = 72 };
        var nichtMerker = Zahl(nicht, 15, alt.NichtAnzeigenWennMerkerGroesser, 9, "Merker zur Stunde");
        var nichtZaehler = Zahl(nicht, 44, alt.NichtAnzeigenWennZaehlerGroesser, 999, "Zähler");
        var y = 195;
        CheckBox Haken(string text, bool wert)
        {
            var c = new CheckBox { Text = text, Checked = wert, Left = 16, Top = y, AutoSize = true };
            ta.Controls.Add(c);
            y += 22;
            return c;
        }
        var gesperrt = Haken("Gesperrte Lehrer nicht anzeigen", alt.GesperrteNichtAnzeigen);
        var langname = Haken("Langname", alt.Langname);
        var klassenteil = Haken("Klasse-Häkchen auch f.Klassenteil", alt.KlasseAuchFuerKlassenteil);
        var raumgewicht = Haken("Raumgewicht 4 berücksichtigen", alt.Raumgewicht4Beachten);
        var hintergrund = Haken("Fenster im Hintergrund", alt.FensterImHintergrund);
        var vz = new GroupBox { Text = "Vorziehungen", Left = 14, Top = 310, Width = 331, Height = 108 };
        var spaeter = new CheckBox { Text = "Nur späteren Unterricht", Checked = alt.NurSpaeterenUnterricht, Left = 16, Top = 15, AutoSize = true };
        var woche = new CheckBox { Text = "Nur späteren Unterr. der selben Woche", Checked = alt.NurDieselbeWoche, Left = 16, Top = 37, AutoSize = true };
        var hoehe = new CheckBox { Text = "Höhe automatisch anpassen", Checked = alt.VorziehungenHoeheAutomatisch, Left = 16, Top = 59, AutoSize = true };
        // wie in der Vorlage: „Max. Prozentsatz der Fensterhöhe“ (ältere Einstellungen in Pixeln erscheinen als Vorgabe 50 %)
        var prozent = alt.VorziehungenFensterhoehe is > 0 and <= 100 ? alt.VorziehungenFensterhoehe : 50;
        var fensterhoehe = new NumericUpDown { Left = 27, Top = 82, Width = 40, Minimum = 10, Maximum = 100, Value = Math.Clamp(prozent, 10, 100) };
        vz.Controls.AddRange(new Control[] { spaeter, woche, hoehe, fensterhoehe, new Label { Text = "Max. Prozentsatz der Fensterhöhe", Left = 77, Top = 87, AutoSize = true } });
        var schrift = alt.Schrift;
        var schriftKnopf = new Button { Text = "Schrift", Left = 16, Top = 423, Width = 75, Height = 23 };
        schriftKnopf.Click += (_, _) =>
        {
            using var fd = new FontDialog { Font = Schrift(schrift) ?? Ui.Schrift, ShowEffects = false };
            if (fd.ShowDialog(f) == DialogResult.OK) schrift = $"{fd.Font.FontFamily.Name};{fd.Font.SizeInPoints.ToString(CultureInfo.InvariantCulture)}";
        };
        ta.Controls.AddRange(new Control[] { rot, nicht, vz, schriftKnopf });
        tabs.TabPages.Add(ta);

        // --- Gewichtung --------------------------------------------------------------------------
        // Schieberegler 197 breit (Teilstriche oben bei 0, 50 und 99), Abstand 31 Pixel, Beschriftung rechts ab x = 220
        var tg = new TabPage("Gewichtung") { UseVisualStyleBackColor = true };
        tg.Controls.Add(new Label { Text = "unwichtig", Left = 8, Top = 38, AutoSize = true });
        tg.Controls.Add(new Label { Text = "extrem wichtig", Left = 158, Top = 38, AutoSize = true });
        var regler = new List<TrackBar>();
        var i = 0;
        foreach (var (text, wert) in new[]
                 {
                     ("Freisetzung", alt.Freisetzung), ("Bereitschaft", alt.Bereitschaft), ("hat Klasse", alt.HatKlasse), ("hat Fach", alt.HatFach),
                     ("Unterrichtsstunden am Tag", alt.UnterrichtsstundenAmTag), ("Merker-Faktor", alt.MerkerFaktor), ("Sperre-Faktor", alt.SperreFaktor),
                     ("Zähler-Faktor", alt.ZaehlerFaktor),
                 })
        {
            // Linie des Reglers wie im Bild bei y = 68, 99, 130, 162, 193, 224, 255, 287
            var linie = 68 + (int)Math.Round(i++ * 31.3);
            var t = new TrackBar
            {
                Left = 15, Top = linie - 19, Width = 197, Minimum = 0, Maximum = 99, TickFrequency = 50, TickStyle = TickStyle.TopLeft, SmallChange = 1, LargeChange = 10,
                Value = Math.Clamp(wert, 0, 99), AutoSize = false, Height = 30,
            };
            var tip = new ToolTip();
            tip.SetToolTip(t, t.Value.ToString(CultureInfo.InvariantCulture));
            t.ValueChanged += (_, _) => tip.SetToolTip(t, t.Value.ToString(CultureInfo.InvariantCulture));
            tg.Controls.Add(t);
            tg.Controls.Add(new Label { Text = text, Left = 220, Top = linie - 8, AutoSize = true });
            regler.Add(t);
        }
        var stat = new GroupBox { Text = "Statistik-Kennzeichen", Left = 16, Top = 325, Width = 360, Height = 80 };
        var statText = new TextBox { Left = 10, Top = 31, Width = 86, Text = string.Join(";", alt.StatistikPunkte.Select(p => $"{p.Key}={p.Value}")) };
        stat.Controls.Add(statText);
        // dreizeiliger Hinweis in der schmaleren Schrift des Vorbilds (Zeilenabstand 14–15 Pixel)
        stat.Controls.Add(new Label
        {
            Text = "Statistik-Kz. des Lehrers (auch mehrere)\nund -Punkte (+ reiht rück, - reiht vor),\nz.B. <X=20;a=-45>", Left = 115, Top = 20, Width = 240, Height = 48,
            Font = new Font("Arial", 9f),
        });
        tg.Controls.Add(stat);
        tabs.TabPages.Add(tg);
        tabs.SelectedIndex = Math.Clamp(seite, 0, tabs.TabPages.Count - 1);
        FensterKoordinaten(f, tabs);

        uebernehmen = () =>
        {
            var punkte = StatistikPunkte(statText.Text);
            var w = regler.Select(r => r.Value).ToArray();
            var werte = (rm: (int)rotMerker.Value, rz: (int)rotZaehler.Value, nm: (int)nichtMerker.Value, nz: (int)nichtZaehler.Value, fh: (int)fensterhoehe.Value);
            AppZustand.Doc.Aendern("Einstellungen Vertretungsvorschlag", AenderungsArt.Vertretung, d =>
            {
                var e = d.Vertretungseinstellungen.Vorschlag;
                e.RotWennMerkerGroesser = werte.rm;
                e.RotWennZaehlerGroesser = werte.rz;
                e.NichtAnzeigenWennMerkerGroesser = werte.nm;
                e.NichtAnzeigenWennZaehlerGroesser = werte.nz;
                e.GesperrteNichtAnzeigen = gesperrt.Checked;
                e.Langname = langname.Checked;
                e.KlasseAuchFuerKlassenteil = klassenteil.Checked;
                e.Raumgewicht4Beachten = raumgewicht.Checked;
                e.FensterImHintergrund = hintergrund.Checked;
                e.NurSpaeterenUnterricht = spaeter.Checked;
                e.NurDieselbeWoche = woche.Checked;
                e.VorziehungenHoeheAutomatisch = hoehe.Checked;
                e.VorziehungenFensterhoehe = werte.fh;
                e.Schrift = schrift;
                (e.Freisetzung, e.Bereitschaft, e.HatKlasse, e.HatFach, e.UnterrichtsstundenAmTag, e.MerkerFaktor, e.SperreFaktor, e.ZaehlerFaktor) =
                    (w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7]);
                e.StatistikPunkte = punkte;
            });
        };
        return f;
    }

    /// <summary>
    /// Inhalte der Karteikarten sind in Fensterkoordinaten angegeben (wie im Handbuch-Bild gemessen): beim Laden um die Lage der
    /// Karteikarten-Fläche im Fenster verschieben, damit sie unabhängig vom Rand des Karteikarten-Steuerelements pixelgenau sitzen.
    /// </summary>
    internal static void FensterKoordinaten(Form f, TabControl tabs)
    {
        f.Load += (_, _) =>
        {
            foreach (TabPage seite in tabs.TabPages)
            {
                var versatz = f.PointToClient(seite.PointToScreen(Point.Empty));
                foreach (Control c in seite.Controls) c.Location = new Point(c.Left - versatz.X, c.Top - versatz.Y);
            }
        };
    }

    /// <summary>„X=20;a=-45“ → Statistik-Kennzeichen und Punkte (ungültige Teile werden übergangen).</summary>
    internal static Dictionary<string, int> StatistikPunkte(string text)
    {
        var res = new Dictionary<string, int>();
        foreach (var teil in text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = teil.Split('=', 2, StringSplitOptions.TrimEntries);
            if (p.Length == 2 && p[0].Length > 0 && int.TryParse(p[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) res[p[0]] = n;
        }
        return res;
    }

    /// <summary>Gespeicherte Schrift „Name;Größe“ des Vertretungsvorschlags (null = Standardschrift).</summary>
    internal static Font? Schrift(string wert)
    {
        var p = wert.Split(';');
        if (p.Length != 2 || p[0].Length == 0 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var groesse) || groesse < 5) return null;
        try { return new Font(p[0], groesse); }
        catch (ArgumentException) { return null; }
    }
}
