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
/// „Vorziehungen“ (Nur späteren Unterricht, Nur späteren Unterr. der selben Woche, Höhe automatisch anpassen, Fensterhöhe) und Schrift.</item>
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
        var f = new Dialog("Einstellungen", 486, 590);
        var tabs = new TabControl { Left = 8, Top = 8, Width = 470, Height = 538 };
        f.Controls.Add(tabs);
        var hilfe = new Button { Text = "Hilfe", Width = 88, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        f.Ok.Location = new Point(486 - 284, 590 - 36);
        f.Abbrechen.Location = new Point(486 - 192, 590 - 36);
        hilfe.Location = new Point(486 - 100, 590 - 36);
        hilfe.Click += (_, _) => HilfeFenster.Zeigen("Vertretungsvorschlag");
        f.Controls.Add(hilfe);

        // --- Allgemein ---------------------------------------------------------------------------
        var ta = new TabPage("Allgemein") { UseVisualStyleBackColor = true };
        NumericUpDown Zahl(Control c, int y, int wert, int max, string text)
        {
            var n = new NumericUpDown { Left = 10, Top = y, Width = 40, Minimum = 0, Maximum = max, Value = Math.Clamp(wert, 0, max) };
            c.Controls.Add(n);
            c.Controls.Add(new Label { Text = text, Left = 56, Top = y + 3, AutoSize = true });
            return n;
        }
        var rot = new GroupBox { Text = "Rot, wenn größer", Left = 12, Top = 8, Width = 230, Height = 80 };
        var rotMerker = Zahl(rot, 20, alt.RotWennMerkerGroesser, 9, "Merker zur Stunde");
        var rotZaehler = Zahl(rot, 50, alt.RotWennZaehlerGroesser, 999, "Zähler");
        var nicht = new GroupBox { Text = "Nicht anzeigen, wenn größer", Left = 12, Top = 96, Width = 230, Height = 80 };
        var nichtMerker = Zahl(nicht, 20, alt.NichtAnzeigenWennMerkerGroesser, 9, "Merker zur Stunde");
        var nichtZaehler = Zahl(nicht, 50, alt.NichtAnzeigenWennZaehlerGroesser, 999, "Zähler");
        var y = 186;
        CheckBox Haken(string text, bool wert)
        {
            var c = new CheckBox { Text = text, Checked = wert, Left = 14, Top = y, AutoSize = true };
            ta.Controls.Add(c);
            y += 24;
            return c;
        }
        var gesperrt = Haken("Gesperrte Lehrer nicht anzeigen", alt.GesperrteNichtAnzeigen);
        var langname = Haken("Langname", alt.Langname);
        var klassenteil = Haken("Klasse-Häkchen auch f.Klassenteil", alt.KlasseAuchFuerKlassenteil);
        var raumgewicht = Haken("Raumgewicht 4 berücksichtigen", alt.Raumgewicht4Beachten);
        var hintergrund = Haken("Fenster im Hintergrund", alt.FensterImHintergrund);
        var vz = new GroupBox { Text = "Vorziehungen", Left = 12, Top = y + 2, Width = 380, Height = 122 };
        var spaeter = new CheckBox { Text = "Nur späteren Unterricht", Checked = alt.NurSpaeterenUnterricht, Left = 12, Top = 20, AutoSize = true };
        var woche = new CheckBox { Text = "Nur späteren Unterr. der selben Woche", Checked = alt.NurDieselbeWoche, Left = 12, Top = 44, AutoSize = true };
        var hoehe = new CheckBox { Text = "Höhe automatisch anpassen", Checked = alt.VorziehungenHoeheAutomatisch, Left = 12, Top = 68, AutoSize = true };
        var fensterhoehe = new NumericUpDown { Left = 28, Top = 92, Width = 48, Minimum = 60, Maximum = 2000, Value = Math.Clamp(alt.VorziehungenFensterhoehe, 60, 2000) };
        vz.Controls.AddRange(new Control[] { spaeter, woche, hoehe, fensterhoehe, new Label { Text = "Fensterhöhe für Vorziehungen", Left = 84, Top = 95, AutoSize = true } });
        var schrift = alt.Schrift;
        var schriftKnopf = new Button { Text = "Schrift", Left = 12, Top = vz.Bottom + 8, Width = 88, Height = 26 };
        schriftKnopf.Click += (_, _) =>
        {
            using var fd = new FontDialog { Font = Schrift(schrift) ?? Ui.Schrift, ShowEffects = false };
            if (fd.ShowDialog(f) == DialogResult.OK) schrift = $"{fd.Font.FontFamily.Name};{fd.Font.SizeInPoints.ToString(CultureInfo.InvariantCulture)}";
        };
        ta.Controls.AddRange(new Control[] { rot, nicht, vz, schriftKnopf });
        tabs.TabPages.Add(ta);

        // --- Gewichtung --------------------------------------------------------------------------
        var tg = new TabPage("Gewichtung") { UseVisualStyleBackColor = true };
        tg.Controls.Add(new Label { Text = "unwichtig", Left = 6, Top = 8, AutoSize = true });
        tg.Controls.Add(new Label { Text = "extrem wichtig", Left = 170, Top = 8, AutoSize = true });
        var regler = new List<TrackBar>();
        var gy = 26;
        foreach (var (text, wert) in new[]
                 {
                     ("Freisetzung", alt.Freisetzung), ("Bereitschaft", alt.Bereitschaft), ("hat Klasse", alt.HatKlasse), ("hat Fach", alt.HatFach),
                     ("Unterrichtsstunden am Tag", alt.UnterrichtsstundenAmTag), ("Merker-Faktor", alt.MerkerFaktor), ("Sperre-Faktor", alt.SperreFaktor),
                     ("Zähler-Faktor", alt.ZaehlerFaktor),
                 })
        {
            var t = new TrackBar { Left = 10, Top = gy, Width = 220, Minimum = 0, Maximum = 99, TickFrequency = 33, SmallChange = 1, LargeChange = 10, Value = Math.Clamp(wert, 0, 99), AutoSize = false, Height = 30 };
            var tip = new ToolTip();
            tip.SetToolTip(t, t.Value.ToString(CultureInfo.InvariantCulture));
            t.ValueChanged += (_, _) => tip.SetToolTip(t, t.Value.ToString(CultureInfo.InvariantCulture));
            tg.Controls.Add(t);
            tg.Controls.Add(new Label { Text = text, Left = 240, Top = gy + 8, AutoSize = true });
            regler.Add(t);
            gy += 36;
        }
        var stat = new GroupBox { Text = "Statistik-Kennzeichen", Left = 12, Top = gy + 16, Width = 430, Height = 90 };
        var statText = new TextBox { Left = 10, Top = 30, Width = 110, Text = string.Join(";", alt.StatistikPunkte.Select(p => $"{p.Key}={p.Value}")) };
        stat.Controls.Add(statText);
        stat.Controls.Add(new Label { Text = "Statistik-Kz. des Lehrers (auch mehrere)\nund -Punkte (+ reiht rück, - reiht vor),\nz.B. <X=20;a=-45>", Left = 130, Top = 18, Width = 290, Height = 60 });
        tg.Controls.Add(stat);
        tabs.TabPages.Add(tg);
        tabs.SelectedIndex = Math.Clamp(seite, 0, tabs.TabPages.Count - 1);

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
