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
/// <item>das Mausrad über der zugeklappten Liste blättert je Rastung einen Eintrag weiter (wie ein Klick), sonst rollt die Umgebung</item>
/// </list>
/// Die Regeln selbst (Treffer, Blättern, Farben, Zahl der Einträge) stehen in <see cref="Aufklappliste"/> und gelten genauso für die
/// Listen unter Tabellenzellen (<see cref="MehrfachAuswahl"/>). Freie Eingabefelder (z. B. Land, Schuljahr) nutzen nur Darstellung,
/// Breite, Richtung und das Mausrad.
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
        /// <summary>Anzahl der Meldungen „Auswahl geändert“ insgesamt bzw. beim Merken des Ausgangswerts (stille Änderungen erkennen).</summary>
        public int Meldungen;
        public int MeldungenBeiVorher;
    }

    /// <summary>Zahl der Einträge, die eine geöffnete Liste höchstens zeigt (überall gleich, danach Rollbalken).</summary>
    public const int SichtbareEintraege = Aufklappliste.SichtbareEintraege;

    /// <summary>Zuletzt gezeigter Hinweis auf eine ungültige Eingabe (für den Selbsttest).</summary>
    public static string? LetzterHinweis { get; private set; }

    private static DateTime _hinweisZeit;

    /// <summary>
    /// Ungültige Eingabe melden (die Eingabe wird nicht übernommen, der bisherige Wert bleibt). Gemeinsamer Hinweis aller Masken
    /// (<see cref="Ui.EingabeHinweis"/>: Statuszeile und kurzes Hinweisfenster unter dem Feld) statt eines Meldungsfensters,
    /// damit die Eingabe ohne Klick weitergehen kann.
    /// </summary>
    public static void UngueltigMelden(ComboBox cb, string eingabe)
    {
        eingabe = eingabe.Trim();
        if (eingabe.Length == 0 || cb.IsDisposed) return;
        var text = $"„{eingabe}“ ist nicht in der Liste. Der bisherige Wert bleibt erhalten.";
        // dieselbe Ablehnung kommt beim Verlassen oft zweimal an (Taste und Prüfung): nur einmal zeigen
        if (text == LetzterHinweis && (DateTime.Now - _hinweisZeit).TotalMilliseconds < 600) return;
        LetzterHinweis = text;
        _hinweisZeit = DateTime.Now;
        Ui.EingabeHinweis(cb, text);
    }

    private static void VorherMerken(ComboBox cb, Zustand z)
    {
        z.VorherIndex = cb.SelectedIndex;
        z.VorherText = cb.SelectedIndex >= 0 ? cb.GetItemText(cb.SelectedItem) ?? "" : cb.Text;
        z.Getippt = false;
        z.MeldungenBeiVorher = z.Meldungen;
    }

    private static readonly System.Reflection.MethodInfo? AuswahlGeaendert =
        typeof(ComboBox).GetMethod("OnSelectedIndexChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>
    /// Auswahl ohne Meldung setzen (Windows hat sie beim Tippen in die offene Liste schon still verändert): Wert wiederherstellen,
    /// ohne dass das Fenster eine Änderung sieht.
    /// </summary>
    private static void StillSetzen(ComboBox cb, int index)
    {
        if (cb.IsHandleCreated) SendMessage(cb.Handle, 0x014E /* CB_SETCURSEL */, index, IntPtr.Zero);
        else cb.SelectedIndex = index;
    }

    /// <summary>
    /// Ausgewählten Eintrag melden, als hätte der Benutzer ihn angeklickt: genau eine Meldung „Auswahl geändert“ (auch wenn Windows
    /// den Eintrag beim Tippen schon still gewählt hat) und danach „Auswahl bestätigt“.
    /// </summary>
    private static void Melden(ComboBox cb, int index, int vorherIndex, bool stillGeaendert)
    {
        if (cb.SelectedIndex != index) cb.SelectedIndex = index;
        else if (index != vorherIndex && stillGeaendert)
        {
            try { AuswahlGeaendert?.Invoke(cb, new object[] { EventArgs.Empty }); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
    }

    /// <summary>Vorherigen Wert wiederherstellen (Escape, Klick neben die Liste, ungültige Eingabe).</summary>
    private static void Zuruecksetzen(ComboBox cb, Zustand z)
    {
        z.Getippt = false;
        z.Markiert = -1;
        if (z.VorherIndex >= 0 && z.VorherIndex < cb.Items.Count)
        {
            // beim Tippen still verändert (keine Meldung seit dem Ausgangswert): ebenso still zurück, sonst mit Meldung
            if (cb.SelectedIndex != z.VorherIndex)
            {
                if (z.Meldungen == z.MeldungenBeiVorher) StillSetzen(cb, z.VorherIndex);
                else cb.SelectedIndex = z.VorherIndex;
            }
            var t = cb.GetItemText(cb.Items[z.VorherIndex]) ?? "";
            if (cb.Text != t) cb.Text = t;
        }
        else if (cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Text != z.VorherText) cb.Text = z.VorherText;
        if (cb.DropDownStyle != ComboBoxStyle.DropDownList && cb.Focused) cb.SelectAll();
    }

    /// <summary>
    /// Offene Liste schließen, ohne dass Windows dabei selbst auswählt: Beim Schließen übernimmt die Liste sonst still den markierten
    /// Vorschlag (ohne Meldung „Auswahl geändert“ – das Fenster würde dann z. B. den Plan nicht laden). Die Markierung wird vorher auf
    /// den aktuellen Wert zurückgestellt; die eigentliche Auswahl setzt danach der Aufrufer, mit genau einer Meldung.
    /// </summary>
    private static void Schliessen(ComboBox cb)
    {
        if (!cb.DroppedDown) return;
        ListeMarkieren(cb, cb.SelectedIndex);
        cb.DroppedDown = false;
    }

    /// <summary>Ungültige Eingabe verwerfen: alten Wert herstellen und kurz darauf hinweisen.</summary>
    private static void Ablehnen(ComboBox cb, Zustand z)
    {
        var eingabe = cb.Text;
        var geaendert = z.Getippt && !string.Equals(eingabe.Trim(), z.VorherText.Trim(), StringComparison.CurrentCultureIgnoreCase);
        // wie Esc: Liste schließen, alter Wert steht wieder im Feld
        z.Getippt = false;
        Schliessen(cb);
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
        Schliessen(cb);
        var neu = cb.SelectedIndex != i || i != z.VorherIndex;
        Melden(cb, i, z.VorherIndex, z.Meldungen == z.MeldungenBeiVorher);
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

    /// <summary>Laufende Tippsuche einer selbst gezeichneten Auswahlliste (siehe <see cref="TippsucheEinrichten"/>).</summary>
    private sealed class Tippzustand
    {
        public string Puffer = "";
        public int Markiert = -1;
        /// <summary>Gewählter Eintrag vor dem Tippen.</summary>
        public int Vorher = -1;
        public ToolTip? Hinweis;
        /// <summary>Die Liste wird gerade durch das Tippen geöffnet (ein Schließen dabei beendet die Suche nicht).</summary>
        public bool Oeffnet;
    }

    private static readonly ConditionalWeakTable<ComboBox, Tippzustand> Tippsuchen = new();

    /// <summary>Hat die (selbst gezeichnete) Auswahlliste die Tippsuche?</summary>
    public static bool HatTippsuche(ComboBox cb) => Tippsuchen.TryGetValue(cb, out _);

    /// <summary>Getippter, noch nicht übernommener Suchtext der Tippsuche (leer ohne laufende Suche).</summary>
    public static string Tippsuchtext(ComboBox cb) => Tippsuchen.TryGetValue(cb, out var t) ? t.Puffer : "";

    /// <summary>
    /// Direktsuche für selbst gezeichnete Auswahllisten (z. B. Formatauswahl mit Symbol, Ansichten, Kategorien mit Farbe), die ihre eigene
    /// Darstellung behalten und deshalb kein Eingabefeld bekommen: Tippen öffnet die Liste und markiert den besten Treffer (Kurzname bzw.
    /// Text beginnt, dann Langname, dann enthält), der Suchtext steht in einem kleinen Hinweis über dem Feld; Enter bzw. Tab übernimmt
    /// (wie ein Mausklick, genau einmal), Esc stellt den alten Wert her. Während des Tippens ändert sich der Wert nicht – das Fenster lädt
    /// also nicht bei jedem Tastendruck neu.
    /// </summary>
    public static void TippsucheEinrichten(ComboBox cb)
    {
        if (cb.IsDisposed || IstEingerichtet(cb) || IstAusgenommen(cb) || Tippsuchen.TryGetValue(cb, out _)) return;
        var t = new Tippzustand();
        Tippsuchen.Add(cb, t);
        MausradSchuetzen(cb);
        void HinweisZeigen()
        {
            if (!cb.IsHandleCreated) return;
            t.Hinweis ??= new ToolTip { UseAnimation = false, UseFading = false };
            var text = t.Markiert >= 0 ? $"Suche: {t.Puffer}" : $"Suche: {t.Puffer} (kein Eintrag)";
            try { t.Hinweis.Show(text, cb, 0, -24, 5000); } catch (InvalidOperationException) { }
        }
        void HinweisWeg()
        {
            try { if (t.Hinweis != null && cb.IsHandleCreated) t.Hinweis.Hide(cb); } catch (InvalidOperationException) { }
        }
        void Suchen()
        {
            if (t.Puffer.Length == 0) { Beenden(false); return; }
            if (!cb.DroppedDown)
            {
                // beim ersten Öffnen passen Fenster und Baustein Breite und Höhe der Liste an – schließt Windows sie dabei, erneut öffnen
                t.Oeffnet = true;
                try
                {
                    cb.DroppedDown = true;
                    if (!cb.DroppedDown) cb.DroppedDown = true;
                }
                finally { t.Oeffnet = false; }
                Cursor.Current = Cursors.Default;
            }
            t.Markiert = Treffer(cb, t.Puffer);
            ListeMarkieren(cb, t.Markiert >= 0 ? t.Markiert : t.Vorher);
            HinweisZeigen();
        }
        // übernehmen (Enter/Tab) bzw. verwerfen (Esc, Klick daneben): die Liste wird vorher auf den bisherigen Wert zurückgestellt, damit
        // das Schließen selbst nichts auswählt – die Auswahl und ihre Meldung kommen danach genau einmal
        void Beenden(bool uebernehmen)
        {
            var puffer = t.Puffer;
            var i = t.Markiert;
            t.Puffer = "";
            t.Markiert = -1;
            HinweisWeg();
            if (cb.DroppedDown)
            {
                ListeMarkieren(cb, t.Vorher >= 0 ? t.Vorher : cb.SelectedIndex);
                cb.DroppedDown = false;
            }
            var vorher = t.Vorher;
            if (!uebernehmen || i < 0 || i >= cb.Items.Count)
            {
                // die offene Liste hat den Vorschlag womöglich still gewählt: ebenso still zurück
                if (vorher >= 0 && vorher < cb.Items.Count && cb.SelectedIndex != vorher) StillSetzen(cb, vorher);
                if (uebernehmen) UngueltigMelden(cb, puffer);
                return;
            }
            if (i == vorher) { if (cb.SelectedIndex != i) StillSetzen(cb, i); return; }
            Melden(cb, i, vorher, true);
            try { AuswahlBestaetigt?.Invoke(cb, new object[] { EventArgs.Empty }); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        cb.KeyPress += (_, e) =>
        {
            if (e.KeyChar == '\b')
            {
                if (t.Puffer.Length == 0) return;
                t.Puffer = t.Puffer[..^1];
                e.Handled = true;
                Suchen();
                return;
            }
            if (char.IsControl(e.KeyChar))
            {
                // Enter/Esc der laufenden Suche: schon in KeyDown erledigt, kein Piepton und keine Standardtaste des Dialogs
                if (t.Puffer.Length > 0 || e.KeyChar is '\r' or (char)27 && cb.DroppedDown) e.Handled = true;
                return;
            }
            // keine eingebaute Schnellsuche von Windows (die wählt bei jedem Buchstaben sofort aus und meldet jedes Mal eine Änderung)
            e.Handled = true;
            if (char.IsWhiteSpace(e.KeyChar) && t.Puffer.Length == 0) return;
            if (t.Puffer.Length == 0) t.Vorher = cb.SelectedIndex;
            t.Puffer += e.KeyChar;
            Suchen();
        };
        cb.KeyDown += (_, e) =>
        {
            if (t.Puffer.Length == 0) return;
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    Beenden(true);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    Beenden(false);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End:
                    // mit den Pfeiltasten in der geöffneten Liste weiter: ab hier wie gewohnt (Enter übernimmt den markierten Eintrag)
                    t.Puffer = "";
                    t.Markiert = -1;
                    HinweisWeg();
                    break;
            }
        };
        cb.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Tab && t.Puffer.Length > 0) Beenden(true); };
        cb.DropDownClosed += (_, _) => { if (t.Puffer.Length > 0 && !t.Oeffnet) { t.Puffer = ""; t.Markiert = -1; HinweisWeg(); } };
        // Fokus wirklich verlassen (nicht nur kurz beim Öffnen der Liste bzw. beim Aktivieren des Fensters): Suche verwerfen
        cb.Leave += (_, _) =>
        {
            if (t.Puffer.Length == 0 || t.Oeffnet || !cb.IsHandleCreated) return;
            cb.BeginInvoke(() => { if (t.Puffer.Length > 0 && !cb.IsDisposed && !cb.Focused && !cb.DroppedDown) Beenden(false); });
        };
        cb.Disposed += (_, _) => t.Hinweis?.Dispose();
    }

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
        cb.SelectedIndexChanged += (_, _) => { z.Meldungen++; if (!z.Getippt && !cb.DroppedDown) VorherMerken(cb, z); };
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
                // genau einen Eintrag getippt und daneben geklickt: übernehmen wie mit Enter (mit Meldung, nie still)
                if (z.Getippt && !z.Frei) { if (!Uebernehmen(cb, z)) Ablehnen(cb, z); return; }
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
                Schliessen(cb);
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
    /// Mausrad (für jede Auswahlliste genau einmal, siehe <see cref="Aufklappliste"/>): über der zugeklappten Liste blättert das Rad
    /// je Rastung genau einen Eintrag weiter, wie ein Klick auf diesen Eintrag (das Fenster reagiert wie sonst auch). Steht der
    /// Mauszeiger nicht über der Liste (sie hat nur den Fokus), rollt die umgebende Tabelle bzw. Fläche. In der geöffneten Liste
    /// rollt das Rad die Liste wie gewohnt.
    /// </summary>
    public static void MausradSchuetzen(ComboBox cb)
    {
        if (MitMausradSchutz.TryGetValue(cb, out _)) return;
        MitMausradSchutz.Add(cb, new object());
        cb.MouseWheel += Mausrad;
    }

    /// <summary>Hat die Liste den Mausradschutz?</summary>
    public static bool HatMausradSchutz(ComboBox cb) => MitMausradSchutz.TryGetValue(cb, out _);

    private sealed class Blaetterer { public required Action<int> Schritt; public readonly Aufklappliste.Rad Rad = new(); }
    private static readonly ConditionalWeakTable<ComboBox, Aufklappliste.Rad> Raeder = new();
    private static readonly ConditionalWeakTable<ComboBox, Blaetterer> MitMausradBlaettern = new();

    /// <summary>
    /// Eigener Schritt für das Mausrad (Elementauswahl der Stundenpläne: wie die Hoch/Runter-Knöpfe daneben, z. B. mit Planaufbau);
    /// ohne Anmeldung blättert das Rad den Eintrag der Liste selbst (<see cref="Blaettern"/>).
    /// </summary>
    public static void MausradBlaettern(ComboBox cb, Action<int> schritt)
    {
        MausradSchuetzen(cb);
        MitMausradBlaettern.AddOrUpdate(cb, new Blaetterer { Schritt = schritt });
    }

    /// <summary>Blättert das Mausrad diese Liste (Elementauswahl eines Plans)?</summary>
    public static bool HatMausradBlaettern(ComboBox cb) => MitMausradBlaettern.TryGetValue(cb, out _);

    /// <summary>
    /// Mausrad über der zugeklappten Liste: einen Eintrag weiter (+1) bzw. zurück (-1), gemeldet wie ein Mausklick (Auswahl geändert
    /// und bestätigt); an den Enden bleibt die Liste stehen.
    /// </summary>
    public static void Blaettern(ComboBox cb, int richtung)
    {
        if (cb.IsDisposed || !cb.Enabled || cb.Items.Count == 0) return;
        Waehlbare.TryGetValue(cb, out var waehlbar);
        var i = cb.SelectedIndex;
        // Trenner und Befehle (z. B. „Neues Format…“) überspringen: das Rad wählt nur Werte
        do
        {
            var n = Aufklappliste.Blaettern(i, cb.Items.Count, richtung);
            if (n == i) { i = cb.SelectedIndex; break; }
            i = n;
        } while (waehlbar is not null && !waehlbar(cb.Items[i]));
        if (i < 0 || i == cb.SelectedIndex) return;
        cb.SelectedIndex = i;
        if (Zustaende.TryGetValue(cb, out var z)) VorherMerken(cb, z);
        if (cb is not IDataGridViewEditingControl)
        {
            try { AuswahlBestaetigt?.Invoke(cb, new object[] { EventArgs.Empty }); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
    }

    private static readonly ConditionalWeakTable<ComboBox, Func<object, bool>> Waehlbare = new();

    /// <summary>
    /// Nur diese Einträge sind Werte, die das Mausrad bzw. Blättern wählt (Listen mit Trennern oder Befehlen am Ende, z. B.
    /// „Format speichern“); ohne Angabe sind alle Einträge Werte.
    /// </summary>
    public static void NurWerte(ComboBox cb, Func<object, bool> istWert) => Waehlbare.AddOrUpdate(cb, istWert);

    private static void Mausrad(object? sender, MouseEventArgs e)
    {
        if (sender is not ComboBox cb || cb.DroppedDown || e is not HandledMouseEventArgs h) return;
        h.Handled = true;
        // Rad nach oben = vorheriger Eintrag, nach unten = nächster; je volle Rastung (120) ein Schritt
        if (MitMausradBlaettern.TryGetValue(cb, out var bl))
        {
            foreach (var r in bl.Rad.Schritte(e.Delta)) bl.Schritt(r);
            return;
        }
        // eine laufende Tippsuche bzw. ein getippter Suchtext wird nicht vom Rad überschrieben
        var getippt = Zustaende.TryGetValue(cb, out var zz) && zz.Getippt || Tippsuchtext(cb).Length > 0;
        if (Aufklappliste.UeberDerListe(cb, e.Location) && cb.Enabled && cb.Items.Count > 0 && !getippt && !(zz?.Frei == true && cb.SelectedIndex < 0))
        {
            var rad = Raeder.GetValue(cb, _ => new Aufklappliste.Rad());
            foreach (var r in rad.Schritte(e.Delta)) Blaettern(cb, r);
            return;
        }
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
        return Aufklappliste.Treffer(kurz, lang, text);
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
        // Windows gleicht die offene Liste nach dem Tastendruck selbst mit dem Text ab (bei selbst gezeichneten Einträgen ohne Treffer):
        // den Vorschlag danach noch einmal markieren, damit er sichtbar bleibt
        if (cb.IsHandleCreated)
            cb.BeginInvoke(() => { if (Bedienbar(cb) && cb.DroppedDown && z.Getippt && z.Markiert >= 0) ListeMarkieren(cb, z.Markiert); });
    }

    private static void Breite(ComboBox cb, Zustand z) => BreiteSetzen(cb, z.Stil, z.Langname);

    /// <summary>
    /// Benötigte Listenbreite, damit Kurzname und Langname vollständig lesbar sind – unabhängig von der Feld- bzw.
    /// Spaltenbreite (auch ohne Fenster-Handle messbar, z. B. für frisch erzeugte Tabellen-Editoren).
    /// </summary>
    public static int BenoetigteBreite(ComboBox cb, Func<string, ListenStil?>? stil = null, Func<string, string?>? langname = null)
    {
        var mitLang = false;
        var symbol = false;
        var schrift = Ui.OhneFett(cb.Font);
        var kurzMax = 0;
        var langMax = 0;
        foreach (var o in cb.Items)
        {
            var t = cb.GetItemText(o) ?? "";
            var st = stil?.Invoke(t);
            if (st?.Symbol != null) symbol = true;
            var zusatz = st?.Zusatz is { Length: > 0 } sz ? sz : Zusatz(o, t, langname);
            kurzMax = Math.Max(kurzMax, TextRenderer.MeasureText(t, schrift).Width);
            if (!string.IsNullOrEmpty(zusatz)) { mitLang = true; langMax = Math.Max(langMax, TextRenderer.MeasureText(zusatz, schrift).Width); }
        }
        // Kurzname-Spalte wie beim Zeichnen (mind. 64 px, höchstens 160 px), danach der Langname
        var kurzSpalte = mitLang ? Math.Max(64, Math.Min(kurzMax + 10, 160)) : kurzMax;
        var w = kurzSpalte + (mitLang ? langMax + 22 : 0) + (symbol ? 21 : 0) + SystemInformation.VerticalScrollBarWidth + 18;
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

    private static readonly ConditionalWeakTable<ComboBox, StrongBox<int>> FeldHoehen = new();

    /// <summary>
    /// Kompaktes Auswahlfeld wie im Vorbild (Elementauswahl der Stundenpläne, Bild 312: 18 Pixel hoch): nur das Eingabefeld wird
    /// niedriger, die Einträge der Liste behalten ihre Höhe. Bleibt erhalten, wenn die Suche das Feld später auf eigene Darstellung umstellt.
    /// </summary>
    public static void FeldHoehe(ComboBox cb, int logisch)
    {
        FeldHoehen.AddOrUpdate(cb, new StrongBox<int>(logisch));
        void Anwenden()
        {
            if (cb.IsDisposed || !cb.IsHandleCreated || !FeldHoehen.TryGetValue(cb, out var h)) return;
            var soll = cb.LogicalToDeviceUnits(h.Value);
            if (cb.Height == soll) return;
            // Feldhöhe = Höhe des Auswahlfelds (CB_SETITEMHEIGHT mit -1) + 6 Pixel Rahmen
            try { SendMessage(cb.Handle, 0x0153 /* CB_SETITEMHEIGHT */, (IntPtr)(-1), (IntPtr)Math.Max(8, soll - 6)); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        cb.HandleCreated += (_, _) => cb.BeginInvoke(Anwenden);
        cb.SizeChanged += (_, _) => { if (cb.IsHandleCreated) cb.BeginInvoke(Anwenden); };
        cb.FontChanged += (_, _) => { if (cb.IsHandleCreated) cb.BeginInvoke(Anwenden); };
        if (cb.IsHandleCreated) cb.BeginInvoke(Anwenden);
    }

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
        var hinter = ausgewaehlt ? Aufklappliste.AuswahlHintergrund : s?.Hintergrund is Color h && h != Color.Empty ? h : cb.BackColor;
        var vorder = ausgewaehlt ? Aufklappliste.AuswahlText : s?.Text is Color t && t != Color.Empty ? t : cb.ForeColor;
        using (var b = new SolidBrush(hinter)) e.Graphics.FillRectangle(b, e.Bounds);
        var r = e.Bounds;
        // Symbol vor dem Kurznamen (z. B. Lehrersymbol in der Vertreterliste wie im Vorbild)
        if (s?.Symbol is { } sym)
        {
            var g = Math.Min(16, r.Height - 2);
            e.Graphics.DrawImage(sym, new Rectangle(r.X + 3, r.Y + (r.Height - g) / 2, g, g));
            r = new Rectangle(r.X + g + 5, r.Y, Math.Max(0, r.Width - g - 5), r.Height);
        }
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
