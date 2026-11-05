using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Der gemeinsame Baustein aller Auswahllisten (Formularfelder, Kopfbereiche, Werkzeugleisten, Dialoge und Tabellenzellen):
/// <list type="bullet">
/// <item>Kurzname | Langname in zwei Spalten, normaler Schnitt (nie fett), mindestens so breit wie beide Spalten</item>
/// <item>öffnet immer nach unten, höchstens <see cref="SichtbareEintraege"/> Einträge hoch</item>
/// <item>Tippen öffnet die Liste und springt sofort zum besten Treffer (Kurzname, dann Langname)</item>
/// <item>Enter/Tab übernimmt den markierten Eintrag (wie ein Mausklick), Esc stellt den alten Wert her</item>
/// <item>ungültige Eingabe wird nicht übernommen (kurzer Hinweis, der alte Wert bleibt)</item>
/// <item>das Mausrad ändert den Wert nie, solange die Liste geschlossen ist (es rollt die Umgebung)</item>
/// </list>
/// Freie Eingabefelder (z. B. Land, Schuljahr) nutzen nur Darstellung, Breite, Richtung und Mausradschutz.
/// </summary>
public static class ListenSuche
{
    private sealed class Zustand
    {
        public Func<string, ListenStil?>? Stil;
        public Func<string, string?>? Langname;
        public int Markiert = -1;
        /// <summary>Wert vor der Eingabe (Index und Text) – Escape, ungültige Eingabe und Klick daneben stellen ihn wieder her.</summary>
        public int VorherIndex = -1;
        public string VorherText = "";
        /// <summary>Seit dem Öffnen wurde getippt (die Liste zeigt Suchtreffer, der Text ist noch nicht übernommen).</summary>
        public bool Getippt;
        /// <summary>Freies Eingabefeld: beliebiger Text erlaubt, die Liste bietet nur Vorschläge an.</summary>
        public bool Frei;
    }

    /// <summary>Zahl der Einträge, die eine geöffnete Liste höchstens zeigt (überall gleich, danach Rollbalken).</summary>
    public const int SichtbareEintraege = 20;

    /// <summary>Zuletzt gezeigter Hinweis auf eine ungültige Eingabe (für den Selbsttest).</summary>
    public static string? LetzterHinweis { get; private set; }

    private static HinweisFenster? _hinweis;
    private static DateTime _hinweisZeit;

    /// <summary>
    /// Ungültige Eingabe melden (die Eingabe wird nicht übernommen, der bisherige Wert bleibt). Kurzer Hinweis unter dem Feld
    /// statt eines Meldungsfensters, damit die Eingabe ohne Klick weitergehen kann.
    /// </summary>
    public static void UngueltigMelden(ComboBox cb, string eingabe)
    {
        eingabe = eingabe.Trim();
        if (eingabe.Length == 0 || cb.IsDisposed) return;
        var text = $"„{eingabe}“ ist nicht in der Liste – der bisherige Wert bleibt erhalten.";
        // dieselbe Ablehnung kommt beim Verlassen oft zweimal an (Taste und Prüfung): nur einmal zeigen
        if (text == LetzterHinweis && (DateTime.Now - _hinweisZeit).TotalMilliseconds < 600) return;
        LetzterHinweis = text;
        _hinweisZeit = DateTime.Now;
        if (!cb.IsHandleCreated || !cb.Visible) return;
        try
        {
            // eigenes kleines Fenster ohne Aktivierung (ein ToolTip hielte sich am Feld fest und stürzte ab, wenn die Tabelle es verwirft)
            if (_hinweis is { IsDisposed: false }) _hinweis.Close();
            _hinweis = new HinweisFenster(text);
            var unten = cb.PointToScreen(new Point(0, cb.Height + 2));
            var bereich = Screen.FromControl(cb).WorkingArea;
            _hinweis.Location = new Point(Math.Clamp(unten.X, bereich.Left, Math.Max(bereich.Left, bereich.Right - _hinweis.Width)),
                unten.Y + _hinweis.Height > bereich.Bottom ? cb.PointToScreen(Point.Empty).Y - _hinweis.Height - 2 : unten.Y);
            _hinweis.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ArgumentException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Kurzer Hinweis (gelb wie ein Tooltip), schließt sich nach 2,5 Sekunden selbst und nimmt nie den Fokus.</summary>
    private sealed class HinweisFenster : Form
    {
        private readonly System.Windows.Forms.Timer _zeit = new() { Interval = 2500 };

        public HinweisFenster(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(255, 255, 225);
            Padding = new Padding(1);
            var l = new Label
            {
                Text = "⚠ " + text, AutoSize = true, Font = Ui.ListenSchrift, ForeColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(6, 4, 6, 4), BackColor = BackColor,
            };
            Controls.Add(l);
            ClientSize = l.PreferredSize + new Size(2, 2);
            l.Location = new Point(1, 1);
            Paint += (_, e) => e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Width - 1, Height - 1);
            _zeit.Tick += (_, _) => Close();
            _zeit.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _zeit.Dispose();
            base.Dispose(disposing);
        }
    }

    private static void VorherMerken(ComboBox cb, Zustand z)
    {
        z.VorherIndex = cb.SelectedIndex;
        z.VorherText = cb.SelectedIndex >= 0 ? cb.GetItemText(cb.SelectedItem) ?? "" : cb.Text;
        z.Getippt = false;
    }

    /// <summary>Vorherigen Wert wiederherstellen (Escape, Klick neben die Liste, ungültige Eingabe).</summary>
    private static void Zuruecksetzen(ComboBox cb, Zustand z)
    {
        z.Getippt = false;
        z.Markiert = -1;
        if (z.VorherIndex >= 0 && z.VorherIndex < cb.Items.Count)
        {
            if (cb.SelectedIndex != z.VorherIndex) cb.SelectedIndex = z.VorherIndex;
            var t = cb.GetItemText(cb.Items[z.VorherIndex]) ?? "";
            if (cb.Text != t) cb.Text = t;
        }
        else if (cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Text != z.VorherText) cb.Text = z.VorherText;
        if (cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Focused) cb.SelectAll();
    }

    /// <summary>Ungültige Eingabe verwerfen: alten Wert herstellen und kurz darauf hinweisen.</summary>
    private static void Ablehnen(ComboBox cb, Zustand z)
    {
        var eingabe = cb.Text;
        var geaendert = z.Getippt && !string.Equals(eingabe.Trim(), z.VorherText.Trim(), StringComparison.CurrentCultureIgnoreCase);
        // wie Esc: Liste schließen, alter Wert steht wieder im Feld
        z.Getippt = false;
        if (cb.DroppedDown) cb.DroppedDown = false;
        Zuruecksetzen(cb, z);
        if (geaendert) UngueltigMelden(cb, eingabe);
    }

    private static readonly System.Reflection.MethodInfo? AuswahlBestaetigt =
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>Markierten Treffer übernehmen (Enter, Tab); liefert false, wenn es keinen Treffer gibt.</summary>
    private static bool Uebernehmen(ComboBox cb, Zustand z)
    {
        var i = z.Markiert >= 0 ? z.Markiert : z.Getippt ? Treffer(cb, cb.Text, z) : cb.SelectedIndex;
        // Text gelöscht: den leeren Eintrag wählen (falls die Liste einen hat) – wie das Löschen einer Zelle
        if (i < 0 && z.Getippt && cb.Text.Trim().Length == 0)
            for (var k = 0; k < cb.Items.Count && i < 0; k++) if ((cb.GetItemText(cb.Items[k]) ?? "").Length == 0) i = k;
        if (i < 0 || i >= cb.Items.Count) return false;
        z.Getippt = false;
        z.Markiert = -1;
        if (cb.DroppedDown) cb.DroppedDown = false;
        var neu = cb.SelectedIndex != i || i != z.VorherIndex;
        if (cb.SelectedIndex != i) cb.SelectedIndex = i;
        var t = cb.GetItemText(cb.Items[i]) ?? "";
        if (cb.Text != t) cb.Text = t;
        if (cb.DropDownStyle != ComboBoxStyle.DropDownList) cb.SelectAll();
        VorherMerken(cb, z);
        // mit der Tastatur übernommen wie mit der Maus: dieselbe Reaktion des Fensters (z. B. anderes Element anzeigen)
        if (neu && cb is not IDataGridViewEditingControl)
        {
            try { AuswahlBestaetigt?.Invoke(cb, new object[] { EventArgs.Empty }); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        return true;
    }

    /// <summary>Text entspricht genau einem Eintrag der Liste?</summary>
    private static bool IstEintrag(ComboBox cb, string text)
    {
        foreach (var o in cb.Items)
            if (string.Equals(cb.GetItemText(o), text, StringComparison.CurrentCultureIgnoreCase)) return true;
        return false;
    }

    private static readonly ConditionalWeakTable<ComboBox, Zustand> Zustaende = new();

    /// <summary>
    /// Langname eines Kurznamens (Lehrer mit vollem Namen, Klassen, Räume, Fächer, Gänge) und der Sonderwerte – für die Anzeige in
    /// Auswahllisten, überall gleich.
    /// </summary>
    public static string? Langname(string kurz)
    {
        if (Sonderwert(kurz) is { } s) return s;
        if (string.IsNullOrWhiteSpace(kurz) || AppZustand.Doc is null) return null;
        var d = AppZustand.D;
        foreach (var liste in new IEnumerable<Core.Model.Element>[] { d.Lehrer, d.Klassen, d.Raeume, d.Faecher, d.Gaenge })
            foreach (var e in liste)
                if (e.Kurzname == kurz) return ElementLangname(e);
        return null;
    }

    /// <summary>Sonderwerte der Vertretungsplanung: „???“ offen, „---“ Entfall, „+++“ Vertretung ohne Lehrkraft.</summary>
    public static string? Sonderwert(string kurz) => kurz switch
    {
        "???" => "offen",
        "---" => "Entfall",
        "+++" => "Vertr. o. Lehrkr.",
        _ => null,
    };

    /// <summary>Zusatztext eines Elements in der Liste (Lehrkräfte mit vollem Namen; nichts, wenn er nur den Kurznamen wiederholt).</summary>
    public static string? ElementLangname(Core.Model.Element e)
    {
        var l = e is Core.Model.Lehrer le && le.VollerName.Length > 0 ? le.VollerName : e.Langname;
        return string.IsNullOrWhiteSpace(l) || l == e.Kurzname ? null : l;
    }

    /// <summary>Zusatztext (Langname) eines Listeneintrags: Elemente direkt, sonst über den Kurznamen.</summary>
    private static string? Zusatz(object? eintrag, string text, Func<string, string?>? langname) =>
        eintrag is Core.Model.Element e ? ElementLangname(e) : langname?.Invoke(text);

    /// <summary>Wird die Auswahlliste schon von der Listensuche betreut (eigene Breitenberechnung mit Stil)?</summary>
    public static bool IstEingerichtet(ComboBox cb) => Zustaende.TryGetValue(cb, out _);

    /// <summary>Auswahlliste, die bewusst nicht vereinheitlicht wird (z. B. ein Suchfeld mit Trefferliste statt einer Auswahl).</summary>
    private static readonly ConditionalWeakTable<ComboBox, object> Ausgenommen = new();

    public static void Ausnehmen(ComboBox cb) => Ausgenommen.AddOrUpdate(cb, new object());

    public static bool IstAusgenommen(ComboBox cb) => Ausgenommen.TryGetValue(cb, out _);

    /// <summary>Ist die Liste ein freies Eingabefeld (beliebiger Text, Liste nur als Vorschlag)?</summary>
    public static bool IstFrei(ComboBox cb) => Zustaende.TryGetValue(cb, out var z) && z.Frei;

    public static void Einrichten(ComboBox cb, Func<string, ListenStil?>? stil = null, Func<string, string?>? langname = null, bool frei = false)
    {
        langname ??= Langname;
        if (Zustaende.TryGetValue(cb, out var z))
        {
            // Tabellenzellen setzen beim Öffnen Stil und Ergänzung zurück
            z.Stil = stil;
            z.Langname = langname;
            cb.MaxDropDownItems = SichtbareEintraege;
            if (cb.DropDownStyle == ComboBoxStyle.DropDownList) cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.AutoCompleteMode = AutoCompleteMode.None;
            Ui.ListenSchriftSetzen(cb);
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            // derselbe Editor wird für jede Zelle wiederverwendet: Ausgangswert dieser Zelle merken
            VorherMerken(cb, z);
            return;
        }
        z = new Zustand { Stil = stil, Langname = langname, Frei = frei };
        Zustaende.Add(cb, z);
        cb.MaxDropDownItems = SichtbareEintraege;
        var auswahl = cb.SelectedIndex;
        if (cb.DropDownStyle == ComboBoxStyle.DropDownList) cb.DropDownStyle = ComboBoxStyle.DropDown;
        if (auswahl >= 0 && auswahl < cb.Items.Count && cb.SelectedIndex != auswahl) cb.SelectedIndex = auswahl;
        cb.AutoCompleteMode = AutoCompleteMode.None;
        Ui.ListenSchriftSetzen(cb);
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = Math.Max(cb.ItemHeight, 19);
        cb.IntegralHeight = false;
        cb.DrawItem += Zeichnen;
        MausradSchuetzen(cb);
        VorherMerken(cb, z);
        cb.Enter += (_, _) => VorherMerken(cb, z);
        // Wert ohne Tippen geändert (Programm, Durchschalter, Klick in die Liste): neuer Ausgangswert
        cb.SelectedIndexChanged += (_, _) => { if (!z.Getippt && !cb.DroppedDown) VorherMerken(cb, z); };
        cb.SelectionChangeCommitted += (_, _) => { if (cb.SelectedIndex >= 0) { z.Getippt = false; z.Markiert = -1; } };
        cb.DropDown += (_, _) =>
        {
            Breite(cb, z);
            NachUnten(cb);
            // beim Öffnen den aktuellen Eintrag sichtbar markieren (nicht beim Öffnen durch Tippen – dort markiert die Suche)
            if (!z.Getippt && cb.SelectedIndex >= 0)
            {
                var i = cb.SelectedIndex;
                cb.BeginInvoke(() => { if (Bedienbar(cb) && cb.DroppedDown && !z.Getippt) ListeMarkieren(cb, i); });
            }
        };
        cb.DropDownClosed += (_, _) =>
        {
            z.Markiert = -1;
            if (z.Frei) { z.Getippt = false; return; }
            // Liste ohne Auswahl geschlossen (Klick daneben, Fokuswechsel): getippten Suchtext verwerfen, nichts ändern.
            // Erst nach dem Schließen prüfen – die Reihenfolge von Schließen und Auswahl ist unter Windows nicht festgelegt.
            if (!cb.IsHandleCreated) return;
            cb.BeginInvoke(() =>
            {
                // Tabellen-Editor: Bearbeitung inzwischen beendet bzw. andere Zelle – nichts mehr ändern
                if (!Bedienbar(cb)) { z.Getippt = false; return; }
                if (cb.DroppedDown) return;
                if (z.Getippt && !IstEintrag(cb, cb.Text.Trim())) { Ablehnen(cb, z); return; }
                // ohne Tippen bzw. mit gültigem Eintrag geschlossen: das ist der neue Ausgangswert
                if (cb.SelectedIndex >= 0 || !z.Getippt) VorherMerken(cb, z);
            });
        };
        cb.TextUpdate += (_, _) =>
        {
            // freies Feld: der Text bleibt, wie getippt; eine offene Liste zeigt nur den passenden Vorschlag
            if (z.Frei) { if (cb.DroppedDown) ListeMarkieren(cb, Treffer(cb, cb.Text, z)); return; }
            z.Getippt = true;
            Suchen(cb, z);
        };
        // Tab übernimmt den markierten Treffer (wie Enter), danach geht der Fokus normal weiter; ohne Treffer bleibt der alte Wert
        cb.PreviewKeyDown += (_, e) =>
        {
            if (z.Frei) return;
            if (e.KeyCode == Keys.Tab && (cb.DroppedDown || z.Getippt) && !Uebernehmen(cb, z)) Ablehnen(cb, z);
        };
        cb.KeyDown += (_, e) =>
        {
            if (z.Frei) return;
            if (e.KeyCode == Keys.Enter && (cb.DroppedDown || z.Getippt))
            {
                if (!Uebernehmen(cb, z)) Ablehnen(cb, z);
                // in Dialogen nicht zusätzlich den Standardknopf auslösen, wenn nur die Liste bestätigt wurde
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && (cb.DroppedDown || z.Getippt || cb.SelectedIndex != z.VorherIndex))
            {
                if (cb.DroppedDown) cb.DroppedDown = false;
                Zuruecksetzen(cb, z);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    /// <summary>Aktuellen Wert als Ausgangswert merken (z. B. nachdem eine Tabelle die Liste für eine neue Zelle gesetzt hat).</summary>
    public static void AusgangswertMerken(ComboBox cb)
    {
        if (Zustaende.TryGetValue(cb, out var z)) { z.Markiert = -1; VorherMerken(cb, z); }
    }

    /// <summary>
    /// Getippten Suchtext bzw. markierten Treffer übernehmen (vor dem Beenden einer Tabellen-Eingabe mit Enter): liefert false,
    /// wenn es keinen Treffer gibt – dann ist der Ausgangswert wiederhergestellt.
    /// </summary>
    public static bool SuchtextUebernehmen(ComboBox cb)
    {
        if (!Zustaende.TryGetValue(cb, out var z) || (!z.Getippt && z.Markiert < 0)) return true;
        if (Uebernehmen(cb, z)) return true;
        Ablehnen(cb, z);
        return false;
    }

    /// <summary>
    /// Darf die Liste noch verändert werden? Der Editor einer Tabelle wird für jede Zelle wiederverwendet: nach dem Ende der
    /// Bearbeitung bzw. nach einem Zellwechsel dürfen verzögerte Schritte (Schließen der Liste) nichts mehr ändern – sonst
    /// meldete die Liste der Tabelle eine Änderung, die dann in der neuen Zelle landete.
    /// </summary>
    private static bool Bedienbar(ComboBox cb) =>
        !cb.IsDisposed && (cb is not IDataGridViewEditingControl ec ||
                           (ec.EditingControlDataGridView is { } g && g.IsCurrentCellInEditMode && g.EditingControl == cb && g.CurrentCell?.RowIndex == ec.EditingControlRowIndex));

    private static readonly ConditionalWeakTable<ComboBox, object> MitMausradSchutz = new();

    /// <summary>
    /// Mausradschutz (für jede Auswahlliste genau einmal): über einer geschlossenen Liste ändert das Rad nie den Wert – auch nicht,
    /// wenn sie den Fokus hat (zu leicht passiert das beim Rollen durch einen Dialog oder eine Tabelle); stattdessen rollt die
    /// umgebende Tabelle bzw. Fläche. In der geöffneten Liste blättert das Rad wie gewohnt.
    /// </summary>
    public static void MausradSchuetzen(ComboBox cb)
    {
        if (MitMausradSchutz.TryGetValue(cb, out _)) return;
        MitMausradSchutz.Add(cb, new object());
        cb.MouseWheel += Mausrad;
    }

    /// <summary>Hat die Liste den Mausradschutz?</summary>
    public static bool HatMausradSchutz(ComboBox cb) => MitMausradSchutz.TryGetValue(cb, out _);

    private static void Mausrad(object? sender, MouseEventArgs e)
    {
        if (sender is not ComboBox cb || cb.DroppedDown || e is not HandledMouseEventArgs h) return;
        h.Handled = true;
        for (var p = cb.Parent; p != null; p = p.Parent)
        {
            if (p is DataGridView g)
            {
                if (g.RowCount == 0) return;
                var zeilen = Math.Max(1, SystemInformation.MouseWheelScrollLines) * (e.Delta > 0 ? -1 : 1);
                var erste = Math.Clamp(g.FirstDisplayedScrollingRowIndex + zeilen, 0, g.RowCount - 1);
                // laufende Eingabe in der Zelle nicht abbrechen – nur rollen, wenn die Zeile sichtbar bleiben kann
                try { if (erste >= 0 && g.Rows[erste].Visible && !g.Rows[erste].Frozen) g.FirstDisplayedScrollingRowIndex = erste; } catch (InvalidOperationException) { }
                return;
            }
            if (p is ScrollableControl { AutoScroll: true } sc && sc.VerticalScroll.Visible)
            {
                var y = -sc.AutoScrollPosition.Y - e.Delta;
                sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, Math.Max(0, y));
                return;
            }
        }
    }

    /// <summary>Ist gerade ein getippter, noch nicht übernommener Suchtext in der Liste?</summary>
    public static bool HatSuchtext(ComboBox cb) => Zustaende.TryGetValue(cb, out var z) && z.Getippt;

    /// <summary>Ausgangswert (Text) vor der laufenden Eingabe.</summary>
    public static string? VorherText(ComboBox cb) => Zustaende.TryGetValue(cb, out var z) ? z.VorherText : null;

    /// <summary>Index des besten Treffers (exakt, Kurzname beginnt, Langname beginnt, Kurzname enthält, Langname enthält).</summary>
    public static int Treffer(ComboBox cb, string text, Func<string, string?>? langname = null)
    {
        Zustaende.TryGetValue(cb, out var z);
        return Treffer(cb, text, z ?? new Zustand { Langname = langname ?? Langname });
    }

    private static int Treffer(ComboBox cb, string text, Zustand z)
    {
        text = text.Trim();
        if (text.Length == 0) return -1;
        var eintraege = cb.Items.Cast<object>().ToList();
        var kurz = eintraege.Select(o => cb.GetItemText(o) ?? "").ToList();
        var lang = kurz.Select((k, n) => Zusatz(eintraege[n], k, z.Langname) ?? "").ToList();
        const StringComparison ci = StringComparison.CurrentCultureIgnoreCase;
        var i = kurz.FindIndex(k => k.Equals(text, ci));
        if (i < 0) i = kurz.FindIndex(k => k.StartsWith(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.StartsWith(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.Split(' ', ',', '-').Any(w => w.StartsWith(text, ci)));
        if (i < 0) i = kurz.FindIndex(k => k.Contains(text, ci));
        if (i < 0) i = lang.FindIndex(l => l.Contains(text, ci));
        return i;
    }

    private static void Suchen(ComboBox cb, Zustand z)
    {
        var text = cb.Text;
        var pos = cb.SelectionStart;
        if (!cb.DroppedDown)
        {
            cb.DroppedDown = true;
            Cursor.Current = Cursors.Default;
            // das Öffnen setzt den Text zurück bzw. markiert ihn: getippten Text wiederherstellen
            if (cb.Text != text) cb.Text = text;
            cb.SelectionStart = Math.Min(pos, text.Length);
            cb.SelectionLength = 0;
        }
        z.Markiert = Treffer(cb, text, z);
        ListeMarkieren(cb, z.Markiert);
    }

    private static void Breite(ComboBox cb, Zustand z) => BreiteSetzen(cb, z.Stil, z.Langname);

    /// <summary>
    /// Benötigte Listenbreite, damit Kurzname und Langname vollständig lesbar sind – unabhängig von der Feld- bzw.
    /// Spaltenbreite (auch ohne Fenster-Handle messbar, z. B. für frisch erzeugte Tabellen-Editoren).
    /// </summary>
    public static int BenoetigteBreite(ComboBox cb, Func<string, ListenStil?>? stil = null, Func<string, string?>? langname = null)
    {
        var mitLang = false;
        var schrift = Ui.OhneFett(cb.Font);
        var kurzMax = 0;
        var langMax = 0;
        foreach (var o in cb.Items)
        {
            var t = cb.GetItemText(o) ?? "";
            var zusatz = stil?.Invoke(t)?.Zusatz is { Length: > 0 } sz ? sz : Zusatz(o, t, langname);
            kurzMax = Math.Max(kurzMax, TextRenderer.MeasureText(t, schrift).Width);
            if (!string.IsNullOrEmpty(zusatz)) { mitLang = true; langMax = Math.Max(langMax, TextRenderer.MeasureText(zusatz, schrift).Width); }
        }
        // Kurzname-Spalte wie beim Zeichnen (mind. 64 px, höchstens 160 px), danach der Langname
        var kurzSpalte = mitLang ? Math.Max(64, Math.Min(kurzMax + 10, 160)) : kurzMax;
        var w = kurzSpalte + (mitLang ? langMax + 22 : 0) + SystemInformation.VerticalScrollBarWidth + 18;
        if (mitLang) w = Math.Max(w, 260);
        return w;
    }

    /// <summary>Mindestbreite jeder geöffneten Auswahlliste – nie nur so schmal wie die Tabellenspalte bzw. das Feld.</summary>
    public const int MindestListenbreite = 240;

    public static void BreiteSetzen(ComboBox cb, Func<string, ListenStil?>? stil = null, Func<string, string?>? langname = null)
    {
        if (cb.IsDisposed) return;
        var breite = Math.Max(cb.Width, Math.Max(MindestListenbreite, BenoetigteBreite(cb, stil, langname)));
        // nie auf die Feldbreite zurückfallen: der Platz bis zum Bildschirmrand war bei mehreren Monitoren bzw. unterschiedlicher
        // Skalierung unzuverlässig (dann wurde die Liste plötzlich schmal) – Windows hält die geöffnete Liste selbst auf dem Bildschirm
        var bildschirm = (cb.IsHandleCreated ? Screen.FromControl(cb) : Screen.PrimaryScreen)?.WorkingArea.Width ?? 1024;
        var ziel = Math.Max(cb.Width, Math.Min(breite, Math.Min(760, Math.Max(320, bildschirm - 20))));
        if (cb.DropDownWidth != ziel) cb.DropDownWidth = ziel;
    }

    /// <summary>Breite schon beim Beginn der Bearbeitung setzen (Tabellenzellen setzen sie sonst auf die Spaltenbreite zurück).</summary>
    public static void BreiteAktualisieren(ComboBox cb)
    {
        if (Zustaende.TryGetValue(cb, out var z)) Breite(cb, z);
        else BreiteSetzen(cb, langname: Langname);
    }

    /// <summary>Listenhöhe so wählen, dass die Liste unter dem Feld Platz hat (Windows öffnet sonst nach oben).</summary>
    public static void NachUnten(ComboBox cb)
    {
        if (cb.IsDisposed || !cb.IsHandleCreated) return;
        var unten = cb.PointToScreen(new Point(0, cb.Height)).Y;
        var platz = Screen.FromControl(cb).WorkingArea.Bottom - unten - 6;
        var gewuenscht = Math.Max(1, cb.Items.Count) * cb.ItemHeight + 4;
        var max = Math.Min(gewuenscht, SichtbareEintraege * cb.ItemHeight + 4);
        var hoehe = Math.Max(cb.ItemHeight * 3 + 4, Math.Min(max, platz));
        if (cb.DropDownHeight != hoehe) cb.DropDownHeight = hoehe;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int cbSize;
        public Rectangle rcItem, rcButton;
        public int stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboBoxInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    /// <summary>Treffer in der offenen Liste markieren, ohne den getippten Text oder die Auswahl zu ändern.</summary>
    private static void ListeMarkieren(ComboBox cb, int index)
    {
        try
        {
            var info = new ComboBoxInfo { cbSize = Marshal.SizeOf<ComboBoxInfo>() };
            if (!GetComboBoxInfo(cb.Handle, ref info) || info.hwndList == IntPtr.Zero) return;
            const int LB_SETCURSEL = 0x0186, LB_SETTOPINDEX = 0x0197;
            SendMessage(info.hwndList, LB_SETCURSEL, index, IntPtr.Zero);
            if (index >= 0) SendMessage(info.hwndList, LB_SETTOPINDEX, Math.Max(0, index - 2), IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static readonly ConditionalWeakTable<ComboBox, StrongBox<int>> KurzSpalten = new();

    /// <summary>Breite der Kurzname-Spalte (längster Kurzname) – damit die Langnamen bündig untereinander stehen.</summary>
    private static int KurzSpalte(ComboBox cb, Font schrift)
    {
        var box = KurzSpalten.GetValue(cb, _ => new StrongBox<int>(-1));
        if (box.Value < 0 || cb.DroppedDown == false)
        {
            var max = 0;
            foreach (var o in cb.Items) max = Math.Max(max, TextRenderer.MeasureText(cb.GetItemText(o) ?? "", schrift).Width);
            box.Value = Math.Min(max + 10, 160);
        }
        return box.Value;
    }

    private static void Zeichnen(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb || e.Index < 0 || e.Index >= cb.Items.Count) return;
        Zustaende.TryGetValue(cb, out var z);
        var text = cb.GetItemText(cb.Items[e.Index]) ?? "";
        var s = z?.Stil?.Invoke(text);
        var zusatz = !string.IsNullOrEmpty(s?.Zusatz) ? s.Value.Zusatz : Zusatz(cb.Items[e.Index], text, z?.Langname);
        var ausgewaehlt = (e.State & DrawItemState.Selected) != 0;
        var hinter = ausgewaehlt ? SystemColors.Highlight : s?.Hintergrund is Color h && h != Color.Empty ? h : cb.BackColor;
        var vorder = ausgewaehlt ? SystemColors.HighlightText : s?.Text is Color t && t != Color.Empty ? t : cb.ForeColor;
        using (var b = new SolidBrush(hinter)) e.Graphics.FillRectangle(b, e.Bounds);
        var r = e.Bounds;
        // Kurzname in fester Spalte, damit die Langnamen bündig stehen – wie der Langname nie fett
        var schrift = Ui.OhneFett(cb.Font);
        var kurzBreite = string.IsNullOrEmpty(zusatz) ? r.Width - 4 : Math.Min(Math.Max(64, KurzSpalte(cb, schrift)), r.Width / 2);
        // geglättet (ClearType bzw. Kantenglättung) – weicher als die GDI-Darstellung
        Ui.ListenText(e.Graphics, text, schrift, new Rectangle(r.X + 4, r.Y, kurzBreite, r.Height), vorder);
        if (!string.IsNullOrEmpty(zusatz))
            Ui.ListenText(e.Graphics, zusatz, schrift, new Rectangle(r.X + 10 + kurzBreite, r.Y, r.Width - kurzBreite - 12, r.Height), ausgewaehlt ? vorder : Color.FromArgb(55, 62, 72));
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
