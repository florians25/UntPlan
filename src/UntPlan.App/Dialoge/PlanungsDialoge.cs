using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>Auswahl beim Ablegen auf eine belegte Stunde: Tausch/behindernden Unterricht entplanen oder Kollision erzeugen.</summary>
public static class PlanungsKonfliktDialog
{
    public static int? Fragen(IWin32Window owner, string titel, string details, string option1, string option2)
    {
        using var d = new Dialog(titel, 460, 250);
        d.Controls.Add(new Label { Text = details, Left = 12, Top = 10, Width = 436, Height = 110, AutoSize = false });
        var grp = new GroupBox { Text = "Optionen", Left = 12, Top = 124, Width = 436, Height = 76 };
        var r1 = new RadioButton { Text = option1, Left = 12, Top = 20, Width = 410, Checked = true };
        var r2 = new RadioButton { Text = option2, Left = 12, Top = 44, Width = 410 };
        grp.Controls.Add(r1);
        grp.Controls.Add(r2);
        d.Controls.Add(grp);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return r1.Checked ? 0 : 1;
    }
}

/// <summary>Antwort auf „Soll behindernder Unterricht entplant werden?“.</summary>
public enum BehinderungsAntwort
{
    /// <summary>Ja: behindernden Unterricht entplanen (er erscheint unter „nicht verplant“).</summary>
    Entplanen,
    /// <summary>Nein: trotzdem ausführen, die Kollision bleibt bestehen.</summary>
    KollisionZulassen,
    /// <summary>Abbrechen: nichts ändern.</summary>
    Abbrechen,
}

/// <summary>
/// Vorbild-Abfrage „Soll behindernder Unterricht entplant werden?“ mit der Liste der behindernden Stunden. Ja entplant den
/// behindernden Unterricht, Nein führt die Aktion mit Kollision aus, Abbrechen ändert nichts. Im Selbsttest beantwortet der
/// Hook <see cref="Ui.FrageAntwort"/> die Frage ohne modales Fenster (true = Ja, false = Nein).
/// </summary>
public static class BehinderungsAbfrage
{
    public const string Titel = "Behindernder Unterricht";

    public static BehinderungsAntwort Fragen(IWin32Window? owner, Schuldatei d, DatenIndex ix, IReadOnlyCollection<Hindernis> hindernisse, string? anlass = null, bool nein = true)
    {
        var text = Behinderung.Fragetext(d, ix, hindernisse, anlass) +
                   "\n\nJa: behindernden Unterricht entplanen (er erscheint unter „nicht verplant“)" +
                   (nein ? "\nNein: trotzdem ausführen – die Kollision bleibt bestehen" : "") + "\nAbbrechen: nichts ändern";
        if (Ui.FrageAntwort is { } antwort) return antwort(text, Titel) ? BehinderungsAntwort.Entplanen : nein ? BehinderungsAntwort.KollisionZulassen : BehinderungsAntwort.Abbrechen;
        var r = nein ? Ui.JaNeinAbbrechen(text, Titel, MeldungsArt.Frage, 0, owner)
            : Ui.Meldung(text, Titel, MeldungsArt.Frage, new[] { "OK" }, 0, true, owner) == 0 ? DialogResult.OK : DialogResult.Cancel;
        return r switch
        {
            DialogResult.Yes or DialogResult.OK => BehinderungsAntwort.Entplanen,
            DialogResult.No => BehinderungsAntwort.KollisionZulassen,
            _ => BehinderungsAntwort.Abbrechen,
        };
    }
}

/// <summary>Frage mit Meldungstext und mehreren Optionen (Optionsfelder), z. B. "Kollision in Zielstunde".</summary>
public static class OptionenDialog
{
    public static int? Fragen(IWin32Window owner, string titel, string meldung, IReadOnlyList<string> optionen, int vorgabe = 0)
    {
        // Maße wie in der Handbuch-Abbildung „Frage“ (96 dpi): Fenster 521 × 236, Meldung oben links, Gruppe „Optionen“
        // 508 × 99 mit Optionsfeldern im Abstand 17, OK/Abbrechen 75 × 23 unten rechts; längere Meldungen bzw. mehr
        // Optionen vergrößern das Fenster nach unten
        var schrift = Dialoge.VorbildMasse.Schrift;
        var text = meldung.Replace("\n", Environment.NewLine);
        var textHoehe = Math.Min(400, TextRenderer.MeasureText(text, schrift, new Size(496, 0), TextFormatFlags.WordBreak).Height);
        var mehrText = Math.Max(0, textHoehe - 88);
        var mehrOptionen = Math.Max(0, optionen.Count - 4) * 17;
        using var d = new Dialog(titel, 521, 236 + mehrText + mehrOptionen) { Font = schrift };
        d.Ok.Size = d.Abbrechen.Size = Dialoge.VorbildMasse.Knopf;
        d.Ok.Location = new Point(356, 210 + mehrText + mehrOptionen);
        d.Abbrechen.Location = new Point(444, 210 + mehrText + mehrOptionen);
        var label = new Label { Text = text, Left = 12, Top = 12, Width = 496, Height = 88 + mehrText, AutoSize = false };
        d.Controls.Add(label);
        var grp = new GroupBox { Text = "Optionen", Left = 11, Top = 106 + mehrText, Width = 508, Height = 99 + mehrOptionen, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        var knoepfe = new List<RadioButton>();
        for (var i = 0; i < optionen.Count; i++)
        {
            var r = new RadioButton { Text = optionen[i], Left = 15, Top = 23 + i * 17, Width = 486, Height = 17, Checked = i == vorgabe };
            grp.Controls.Add(r);
            knoepfe.Add(r);
        }
        d.Controls.Add(grp);
        // wie im Vorbild direkt neben der Maus (beim Planen also neben der Zielstunde)
        Warnhinweis.NebenDerMaus(d);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        return knoepfe.FindIndex(k => k.Checked);
    }
}

/// <summary>
/// Raum zuordnen / löschen.
/// <list type="bullet">
/// <item>Links: die Kopplungszeilen des Unterrichts.</item>
/// <item>Rechts: alle möglichen Räume mit Kapazität, Ausweich- und Stammraum, Belegung, Raumgruppe, Gang, Wochenbelegung, freien Stunden und Kapazitätsdifferenz.</item>
/// <item>Anwenden auf die Stunde, den Block oder alle Stunden des Unterrichts.</item>
/// <item>Filter: nur freie Räume, nur Wunschräume, ausreichende Kapazität und Suche.</item>
/// </list>
/// Doppelklick oder ← ordnet den Raum zu; bei belegten Räumen wird nach Verdrängen, Tauschen oder Kollision gefragt.
/// </summary>
public static class RaumZuordnenDialog
{
    private static int _umfang = 1;
    private static bool _nurFrei;

    public static bool Zeigen(IWin32Window owner, Planstunde p, bool modal = true)
    {
        Dokument doc = AppZustand.Doc;
        DatenIndex ix = doc.Index;
        Unterricht u = ix.Unterrichte[p.UnterrichtId];
        var idx = doc.Daten.Stundenplan.IndexOf(p);
        var d = new Dialog("Raum zuordnen / löschen", 1220, 640);
        d.Ok.Text = "Schließen";
        d.Abbrechen.Visible = false;
        d.MinimumSize = new Size(900, 480);
        d.FormBorderStyle = FormBorderStyle.Sizable;
        var geaendert = false;

        var info = new Label { Left = 10, Top = 10, Width = 500, Height = 60, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(4), BackColor = Color.White };
        var umfang = new GroupBox { Text = "Anwenden auf", Left = 556, Top = 4, Width = 230, Height = 88 };
        RadioButton[] rbs = new[] { "Unterrichtsstunde", "Stundenblock", "Alle Stunden eines Unterr." }
            .Select((t, i) => new RadioButton { Text = t, Left = 10, Top = 18 + i * 22, Width = 210, Checked = i == _umfang }).ToArray();
        umfang.Controls.AddRange(rbs);
        CheckBox lang = new CheckBox { Text = "Langname anzeigen", Left = 800, Top = 12, AutoSize = true };
        var nurFrei = new CheckBox { Text = "Nur freie Räume", Left = 800, Top = 34, AutoSize = true, Checked = _nurFrei };
        var nurWunsch = new CheckBox { Text = "Nur Wunschräume / Raumgruppe", Left = 800, Top = 56, AutoSize = true };
        var nurKap = new CheckBox { Text = "Nur ausreichende Kapazität", Left = 800, Top = 78, AutoSize = true };
        var suche = new TextBox { Left = 700, Top = 100, Width = 180, PlaceholderText = "Raum suchen…" };

        d.Controls.Add(new Label { Text = "Auswahl derzeitiger Unterricht:", Left = 10, Top = 104, AutoSize = true });
        DataGridView links = new DataGridView { Left = 10, Top = 128, Width = 500, Height = 420, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
        Ui.RasterGestalten(links, false);
        links.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        links.MultiSelect = false;
        links.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[] { ("U", "Unt.", 40), ("L", "Lehrer", 60), ("F", "Fach", 50), ("R", "Raum", 64), ("K", "Klasse(n)", 110), ("W", "Wunschraum", 84), ("G", "U-Gruppe", 76) })
            links.Columns[links.Columns.Add(n, k)].Width = w;

        var pfeil = new Button { Text = "←", Left = 516, Top = 220, Width = 34, Height = 34, Font = new Font(Ui.Schrift.FontFamily, 13, FontStyle.Bold) };
        var loeschen = new Button { Image = Symbole.Get("loeschen", 16), Left = 516, Top = 260, Width = 34, Height = 34 };
        var tip = new ToolTip();
        tip.SetToolTip(pfeil, "Markierten Raum zuordnen (Doppelklick)");
        tip.SetToolTip(loeschen, "Raum der markierten Zeile löschen (Entf)");

        d.Controls.Add(new Label { Text = "Mögliche Räume:", Left = 556, Top = 104, AutoSize = true });
        var rechts = new DataGridView { Left = 556, Top = 128, Width = 654, Height = 420, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(rechts, false);
        rechts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rechts.MultiSelect = false;
        rechts.RowHeadersVisible = false;
        foreach (var (n, k, w) in new[] { ("Rm", "Rm.", 64), ("Kap", "Kap.", 38), ("AR", "Ausw.-Rm.", 62), ("SR", "Stammr.", 52), ("B", "Besetzt", 52), ("BD", "Besetzt durch", 118), ("RG", "Raumgruppe", 80), ("Ga", "Gang", 50), ("St", "Wo.-Bel.", 52), ("SF", "Std.frei", 52), ("KD", "Kap.Diff.", 56) })
        {
            var c = rechts.Columns[rechts.Columns.Add(n, k)];
            c.Width = w;
            if (n is "Kap" or "St" or "SF" or "KD") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (n is "AR" or "SR" or "B") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        var legende = new Label
        {
            Text = "grün = Wunschraum bzw. Raumgruppe · rot = besetzt (in mindestens einer Stunde) · grau = gesperrt · Std.frei = freie Stunden von den betroffenen Stunden",
            Left = 10, Top = 556, Width = 1000, Height = 18, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        d.Controls.AddRange(new Control[] { info, umfang, lang, nurFrei, nurWunsch, nurKap, suche, links, pfeil, loeschen, rechts, legende });
        // Häkchen selbst zeichnen (unabhängig von der Schriftart)
        rechts.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
            Ui.ZellHaken(e, rechts.Columns[e.ColumnIndex].Name == "B" ? Color.ForestGreen : Color.SteelBlue, 2.2f);
        };

        int Umfang() => Array.FindIndex(rbs, r => r.Checked);
        Planstunde Aktuell() => doc.Daten.Stundenplan[idx];
        List<Planstunde> Stunden() => Planungsdienst.RaumStunden(doc.Daten, Aktuell(), Umfang());
        int Zeile() => Math.Max(0, links.CurrentRow?.Index ?? 0);
        string K(ElementTyp t, int? id)
        {
            if (id is null) return "";
            var e = ix.Element(t, id.Value);
            return e is null ? "" : lang.Checked && e.Langname.Length > 0 ? e.Langname : e.Kurzname;
        }

        void Info()
        {
            var st = Stunden();
            info.Text = $"Unterricht: {u.Id}: {ix.UnterrichtText(u)}\nStunden: {st.Count}  " +
                        string.Join(", ", st.Select(x => $"{Planungsdienst.Tagname(doc.Daten, x.Tag)}-{x.Stunde}")) + $"\nRaum: {string.Join(", ", st.Select(x => K(ElementTyp.Raum, x.RaumFuerZeile(Zeile()))).Where(x => x.Length > 0).Distinct())}";
        }

        void Links()
        {
            var alt = Zeile();
            links.Rows.Clear();
            var akt = Aktuell();
            for (var i = 0; i < u.Zeilen.Count; i++)
            {
                var z = u.Zeilen[i];
                var gruppe = (z.UGruppeId ?? u.UGruppeId) is int g ? doc.Daten.UGruppen.FirstOrDefault(x => x.Id == g)?.Kurzname ?? "" : "";
                var r = links.Rows.Add(i == 0 ? u.Id.ToString() : "", K(ElementTyp.Lehrer, z.LehrerId), K(ElementTyp.Fach, z.FachId), K(ElementTyp.Raum, akt.RaumFuerZeile(i)),
                    string.Join(",", z.KlassenIds.Select(k => K(ElementTyp.Klasse, k))), z.Wunschraum.HasValue ? K(ElementTyp.Raum, z.Wunschraum) : K(ElementTyp.Raumgruppe, z.Wunschraumgruppe), gruppe);
                links.Rows[r].Cells[0].Style.BackColor = Color.FromArgb(240, 222, 200);
            }
            if (links.Rows.Count > 0) links.CurrentCell = links.Rows[Math.Min(alt, links.Rows.Count - 1)].Cells[1];
        }

        void Rechts()
        {
            var altRaum = (rechts.CurrentRow?.Tag as Planungsdienst.RaumZeile)?.Raum.Id;
            rechts.Rows.Clear();
            var f = suche.Text.Trim();
            foreach (var z in Planungsdienst.RaumUebersicht(doc.Daten, ix, Stunden(), Zeile()))
            {
                if (nurFrei.Checked && !z.GanzFrei) continue;
                if (nurWunsch.Checked && !z.Wunsch) continue;
                if (nurKap.Checked && z.KapazitaetDiff < 0) continue;
                if (f.Length > 0 && !z.Raum.Kurzname.Contains(f, StringComparison.CurrentCultureIgnoreCase) && !z.Raum.Langname.Contains(f, StringComparison.CurrentCultureIgnoreCase) &&
                    !z.Raumgruppen.Contains(f, StringComparison.CurrentCultureIgnoreCase)) continue;
                var durch = string.Join("; ", z.Belegt.Select(b => ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? $"{ix.UnterrichtText(o)}" : "").Distinct());
                var r = rechts.Rows.Add(lang.Checked && z.Raum.Langname.Length > 0 ? z.Raum.Langname : z.Raum.Kurzname, z.Raum.Kapazitaet > 0 ? z.Raum.Kapazitaet : "",
                    z.Ausweichraum ? "✔" : "", z.Stammraum ? "✔" : "", z.Belegt.Count > 0 ? "✔" : "", durch, z.Raumgruppen, z.Gang, z.Wochenbelegung,
                    z.StundenFrei, z.KapazitaetDiff?.ToString("+0;-0;0") ?? "");
                var row = rechts.Rows[r];
                row.Tag = z;
                if (z.Gesperrt > 0 && z.StundenFrei == 0) row.DefaultCellStyle.BackColor = Color.FromArgb(232, 232, 232);
                else if (z.Belegt.Count > 0) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 238, 238);
                else if (z.Wunsch) row.DefaultCellStyle.BackColor = Color.FromArgb(226, 246, 226);
                row.Cells["B"].Style.ForeColor = Color.ForestGreen;
                if (z.KapazitaetDiff < 0) row.Cells["KD"].Style.ForeColor = Color.Firebrick;
                if (z.StundenFrei < z.StundenGesamt) row.Cells["SF"].Style.ForeColor = Color.Firebrick;
                if (z.Raum.Id == altRaum) rechts.CurrentCell = row.Cells[0];
            }
        }

        void Alles()
        {
            ix = doc.Index;
            Info();
            Links();
            Rechts();
        }

        void Zuordnen()
        {
            if (rechts.CurrentRow?.Tag is not Planungsdienst.RaumZeile z) return;
            var zeile = Zeile();
            var loesung = Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen;
            if (z.Belegt.Count > 0)
            {
                var text = string.Join("\n", z.Belegt.Select(b => $"{Planungsdienst.Tagname(doc.Daten, b.Tag)}-{b.Stunde}: {(ix.Unterrichte.TryGetValue(b.UnterrichtId, out var o) ? ix.UnterrichtText(o) : "")}").Distinct());
                var w = OptionenDialog.Fragen(d, "Raum nicht frei", $"Raum {z.Raum.Kurzname} ist belegt:\n{text}",
                    new[] { "Behindernden Unterricht aus dem Raum verdrängen", "Räume tauschen", "Kollision zulassen" });
                if (w is null) return;
                loesung = w switch { 0 => Planungsdienst.RaumKonfliktLoesung.Verdraengen, 1 => Planungsdienst.RaumKonfliktLoesung.RaeumeTauschen, _ => Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen };
            }
            else if (z.Gesperrt > 0 && !Ui.Frage($"Raum {z.Raum.Kurzname} ist in {z.Gesperrt} der Stunden gesperrt. Trotzdem zuordnen?")) return;
            var indizes = Stunden().Select(x => doc.Daten.Stundenplan.IndexOf(x)).Where(i => i >= 0).ToList();
            doc.Aendern($"Raum {z.Raum.Kurzname} zuordnen", AenderungsArt.Stundenplan,
                dd => Planungsdienst.RaumZuordnen(dd, indizes.Select(i => dd.Stundenplan[i]), zeile, z.Raum.Id, loesung));
            geaendert = true;
            Alles();
        }

        void Loeschen()
        {
            var zeile = Zeile();
            var indizes = Stunden().Select(x => doc.Daten.Stundenplan.IndexOf(x)).Where(i => i >= 0).ToList();
            doc.Aendern("Raum löschen", AenderungsArt.Stundenplan,
                dd => Planungsdienst.RaumZuordnen(dd, indizes.Select(i => dd.Stundenplan[i]), zeile, null, Planungsdienst.RaumKonfliktLoesung.KollisionErzeugen));
            geaendert = true;
            Alles();
        }

        links.SelectionChanged += (_, _) => { Info(); Rechts(); };
        foreach (var rb in rbs) rb.CheckedChanged += (_, _) => { if (rb.Checked) { _umfang = Umfang(); Info(); Rechts(); } };
        lang.CheckedChanged += (_, _) => Alles();
        nurFrei.CheckedChanged += (_, _) => { _nurFrei = nurFrei.Checked; Rechts(); };
        nurWunsch.CheckedChanged += (_, _) => Rechts();
        nurKap.CheckedChanged += (_, _) => Rechts();
        suche.TextChanged += (_, _) => Rechts();
        rechts.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Zuordnen(); };
        rechts.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Zuordnen(); e.Handled = true; } };
        links.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { Loeschen(); e.Handled = true; } };
        pfeil.Click += (_, _) => Zuordnen();
        loeschen.Click += (_, _) => Loeschen();
        Alles();
        if (!modal)
        {
            d.Show(owner);
            return false;
        }
        using (d) d.ShowDialog(owner);
        return geaendert;
    }
}

/// <summary>
/// Tauschvorschläge (Handbuch, Manuelles Planen | Tauschvorschläge): mögliche 2er-, 3er- und 4er-Tausche für die aktive Stunde mit den
/// Spalten „Gewinn“ (positiv = Verbesserung), „Auswirkungen“, „Spr.Kla“ und „Gesamt“; Optionen „Mit Klassensprung“ und „Nur verschiedene
/// Lehrer“. Bei einem Klassensprung zeigt der untere Teil, wie die behindernde Stunde in der Sprungklasse verlegt werden kann.
/// &lt;Aktualisieren&gt; rechnet neu, &lt;Tauschen&gt; führt den gewählten Tausch aus (das Fenster bleibt offen).
/// </summary>
public static class TauschvorschlaegeDialog
{
    /// <param name="index">Index der aktiven Stunde in <see cref="Schuldatei.Stundenplan"/>.</param>
    /// <param name="tauschen">Führt die Züge aus (als eine rückgängig machbare Änderung).</param>
    public static void Zeigen(IWin32Window owner, int index, Action<IReadOnlyList<TauschZug>> tauschen)
    {
        using var d = new Dialog("Tauschvorschläge", 820, 470);
        d.Ok.Visible = false;
        d.Abbrechen.Text = "Schließen";
        var klassensprung = new CheckBox { Text = "Mit Klassensprung", Left = 10, Top = 10, AutoSize = true };
        var verschieden = new CheckBox { Text = "Nur verschiedene Lehrer", Left = 160, Top = 10, AutoSize = true };
        var aktualisieren = new Button { Text = "Aktualisieren", Left = 520, Top = 6, Width = 100, Height = 26 };
        var tausch = new Button { Text = "Tauschen", Left = 628, Top = 6, Width = 100, Height = 26 };
        var stundeText = new Label { Left = 10, Top = 34, Width = 790, ForeColor = Color.DimGray };
        var oben = new DataGridView { Left = 10, Top = 54, Width = 800, Height = 230, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var untenText = new Label { Left = 10, Top = 290, Width = 790, Text = "Verlegung der behindernden Stunde in der Sprungklasse:" };
        var unten = new DataGridView { Left = 10, Top = 308, Width = 800, Height = 110, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        foreach (var g in new[] { oben, unten })
        {
            Ui.RasterGestalten(g, false);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.RowHeadersVisible = false;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
        }
        foreach (var (n, t, w) in new[] { ("Art", "Art", 50), ("Gewinn", "Gewinn", 60), ("Auswirkungen", "Auswirkungen", 170), ("Spr", "Spr.Kla", 60), ("Gesamt", "Gesamt", 60), ("Tausch", "Tausch", 390) })
            oben.Columns[oben.Columns.Add(n, t)].Width = w;
        foreach (var (n, t, w) in new[] { ("Gewinn", "Gewinn", 60), ("Auswirkungen", "Auswirkungen", 170), ("Tausch", "Verlegung", 560) })
            unten.Columns[unten.Columns.Add(n, t)].Width = w;
        oben.Columns["Gewinn"]!.ToolTipText = "Positiv: der Stundenplan wird laut Gewichtung besser, negativ: schlechter";
        oben.Columns["Auswirkungen"]!.ToolTipText = "Bei Verschlechterung der gröbste Verstoß, bei Verbesserung die Kategorie der größten Verbesserung";
        oben.Columns["Spr"]!.ToolTipText = "Sprungklasse: Klasse, in der zusätzlich eine Stunde verlegt werden muss";
        d.Controls.AddRange(new Control[] { klassensprung, verschieden, aktualisieren, tausch, stundeText, oben, untenText, unten });

        void Farbe(DataGridViewCell c, double v) => c.Style.ForeColor = v > 0 ? Color.DarkGreen : v < 0 ? Color.DarkRed : Color.Black;
        void Folge()
        {
            unten.Rows.Clear();
            if (oben.CurrentRow?.Tag is not TauschvorschlagZeile z) return;
            foreach (var f in z.Folgetausche)
            {
                var r = unten.Rows.Add(f.Gewinn.ToString("+0;-0;0"), f.Auswirkungen, f.Beschreibung);
                unten.Rows[r].Tag = f;
                Farbe(unten.Rows[r].Cells[0], f.Gewinn);
            }
        }
        void Rechnen()
        {
            oben.Rows.Clear();
            unten.Rows.Clear();
            var plan = AppZustand.D.Stundenplan;
            if (index < 0 || index >= plan.Count) { stundeText.Text = "Keine aktive Stunde."; return; }
            var p = plan[index];
            var ix = AppZustand.Doc.Index;
            stundeText.Text = ix.Unterrichte.TryGetValue(p.UnterrichtId, out var u)
                ? $"Aktive Stunde: {Planungsdienst.Tagname(AppZustand.D, p.Tag)}-{p.Stunde}  {ix.UnterrichtText(u)} (U{u.Id})" : "";
            List<TauschvorschlagZeile> liste;
            using (new WarteCursor())
                liste = Tauschvorschlaege.Berechnen(AppZustand.D, p, new TauschvorschlagOptionen { MitKlassensprung = klassensprung.Checked, NurVerschiedeneLehrer = verschieden.Checked });
            foreach (var v in liste)
            {
                var r = oben.Rows.Add($"{v.Art}er", v.Gewinn.ToString("+0;-0;0"), v.Auswirkungen, v.Sprungklasse, v.Sprungklasse.Length > 0 ? v.Gesamt.ToString("+0;-0;0") : "", v.Beschreibung);
                oben.Rows[r].Tag = v;
                Farbe(oben.Rows[r].Cells["Gewinn"], v.Gewinn);
                if (v.Sprungklasse.Length > 0) Farbe(oben.Rows[r].Cells["Gesamt"], v.Gesamt);
            }
            if (liste.Count == 0) stundeText.Text += "  –  keine Tauschmöglichkeiten gefunden";
            Folge();
        }
        oben.SelectionChanged += (_, _) => Folge();
        aktualisieren.Click += (_, _) => Rechnen();
        klassensprung.CheckedChanged += (_, _) => Rechnen();
        verschieden.CheckedChanged += (_, _) => Rechnen();
        tausch.Click += (_, _) =>
        {
            if (oben.CurrentRow?.Tag is not TauschvorschlagZeile z) { Ui.Info("Bitte zuerst einen Tauschvorschlag auswählen."); return; }
            var folge = unten.CurrentRow?.Tag as TauschvorschlagZeile;
            tauschen(z.AlleZuege(folge));
            Rechnen();
        };
        oben.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) tausch.PerformClick(); };
        d.Shown += (_, _) => Rechnen();
        d.ShowDialog(owner);
    }
}

public sealed class WarteCursor : IDisposable
{
    private readonly Cursor _alt = Cursor.Current ?? Cursors.Default;
    public WarteCursor() => Cursor.Current = Cursors.WaitCursor;
    public void Dispose() => Cursor.Current = _alt;
}
