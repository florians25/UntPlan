using System.Runtime.InteropServices;
using System.Text;
using UntPlan.App.Steuerelemente;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>
/// Robustheitsprüfung des Selbsttests (R3D): Fensterrundgang durch alle Einträge des Menübands beider Modi (mit Untermenüs),
/// Bedienung jedes geöffneten Fensters (Zeile wählen, sortieren, Neuaufbau, Datum wechseln), Kontextmenüs der Fenster und
/// Fehleingaben in den wichtigsten Tabellen. Modale Dialoge werden automatisch geschlossen; jede Ausnahme wird protokolliert.
/// </summary>
public sealed partial class HauptFenster
{
    // ------------------------------------------------------------------------------------------
    // Win32: Fenster des eigenen Threads (modale Dialoge, Windows-Meldungen, Kontextmenüs)
    // ------------------------------------------------------------------------------------------

    private delegate bool FensterAufzaehlung(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, FensterAufzaehlung rueckruf, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, FensterAufzaehlung rueckruf, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int laenge);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int laenge);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int nachricht, IntPtr wParam, IntPtr lParam);

    private static List<IntPtr> ThreadFenster()
    {
        var res = new List<IntPtr>();
        EnumThreadWindows(GetCurrentThreadId(), (h, _) => { res.Add(h); return true; }, IntPtr.Zero);
        return res;
    }

    private static string FensterKlasse(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string FensterText(IntPtr h)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Titel und Texte eines Windows-Dialogs (z. B. Meldungsfenster), für das Protokoll.</summary>
    private static string DialogInhalt(IntPtr h)
    {
        var teile = new List<string> { FensterText(h) };
        EnumChildWindows(h, (k, _) =>
        {
            if (FensterKlasse(k).Equals("Static", StringComparison.OrdinalIgnoreCase) && FensterText(k) is { Length: > 0 } t) teile.Add(t);
            return true;
        }, IntPtr.Zero);
        return string.Join(" | ", teile.Where(t => t.Length > 0)).Replace('\n', ' ').Replace("\r", "");
    }

    /// <summary>Geöffnete Kontextmenüs (oberste Ebene) des Threads.</summary>
    private static List<ContextMenuStrip> OffeneKontextmenues() =>
        ThreadFenster().Where(IsWindowVisible).Select(Control.FromHandle).OfType<ContextMenuStrip>().Where(m => m.Visible).ToList();

    private static void MenuesSchliessen()
    {
        foreach (var m in ThreadFenster().Where(IsWindowVisible).Select(Control.FromHandle).OfType<ToolStripDropDown>().Where(m => m.Visible).ToList())
            m.Close(ToolStripDropDownCloseReason.CloseCalled);
    }

    /// <summary>
    /// Nachrichten verarbeiten wie <see cref="Warten"/>, aber nie mit offenem Kontextmenü: unter Wine wirft der Menüfilter von
    /// WinForms (DPI-Kontext „GDI-skaliert“ wird dort nicht unterstützt) bei jeder Nachricht, solange ein Menü offen ist.
    /// </summary>
    private static void Pumpen(int ms)
    {
        var ende = DateTime.Now.AddMilliseconds(ms);
        do
        {
            MenuesSchliessen();
            Application.DoEvents();
            MenuesSchliessen();
            if (ms > 0) Thread.Sleep(20);
        }
        while (DateTime.Now < ende);
    }

    /// <summary>
    /// Schließt modale Dialoge (eigene Formulare und Windows-Dialoge wie Datei-, Ordner-, Druck- und Meldungsfenster) automatisch,
    /// nachdem sie kurz bedient wurden. Läuft als Zeitgeber auf dem Oberflächen-Thread, also auch innerhalb von ShowDialog.
    /// </summary>
    private sealed class DialogWaechter : IDisposable
    {
        private readonly System.Windows.Forms.Timer _zeitgeber = new() { Interval = 150 };
        private readonly Dictionary<object, int> _versuche = new();
        private readonly HashSet<object> _inArbeit = new();
        public List<string> Dialoge { get; } = new();
        public List<string> Probleme { get; } = new();
        public Func<Form, List<string>>? Bedienen { get; init; }
        public string Ort { get; set; } = "";

        public DialogWaechter()
        {
            _zeitgeber.Tick += (_, _) => Pruefen();
            _zeitgeber.Start();
        }

        private void Pruefen()
        {
            MenuesSchliessen();
            foreach (var h in ThreadFenster())
            {
                if (!IsWindowVisible(h)) continue;
                var c = Control.FromHandle(h);
                if (c is Form f)
                {
                    if (!f.Modal || _inArbeit.Contains(f)) continue;
                    var n = _versuche[f] = _versuche.GetValueOrDefault(f) + 1;
                    if (n == 1)
                    {
                        Dialoge.Add($"{Ort}: {f.GetType().Name} „{f.Text}“");
                        _inArbeit.Add(f);
                        try { if (Bedienen != null) Probleme.AddRange(Bedienen(f).Select(p => $"{Ort} / Dialog „{f.Text}“: {p}")); }
                        finally { _inArbeit.Remove(f); }
                    }
                    if (f.IsDisposed || !f.Visible) continue;
                    if (n < 8) f.DialogResult = DialogResult.Cancel;
                    else if (n < 30) f.Close();
                    else if (n == 30)
                    {
                        Probleme.Add($"{Ort}: Dialog {f.GetType().Name} „{f.Text}“ lässt sich nicht schließen (Abbrechen/Schließen wirkungslos)");
                        f.Hide();
                    }
                }
                else if (c is null && FensterKlasse(h) == "#32770")
                {
                    var n = _versuche[h] = _versuche.GetValueOrDefault(h) + 1;
                    if (n == 1)
                    {
                        var inhalt = DialogInhalt(h);
                        Dialoge.Add($"{Ort}: Windows-Dialog „{inhalt}“");
                        // Standardfehlerfenster der Tabelle (DataError ohne Behandlung): unverständliche Meldung für Anwender
                        if (inhalt.Contains("DataGridView", StringComparison.OrdinalIgnoreCase))
                            Probleme.Add($"{Ort}: Standard-Fehlerfenster der Tabelle statt verständlichem Hinweis: {inhalt}");
                    }
                    const int WM_COMMAND = 0x111, WM_CLOSE = 0x10, IDCANCEL = 2;
                    if (n < 6) PostMessage(h, WM_COMMAND, (IntPtr)IDCANCEL, IntPtr.Zero);
                    else if (n < 30) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    else if (n == 30) Probleme.Add($"{Ort}: Windows-Dialog „{FensterText(h)}“ lässt sich nicht schließen");
                }
            }
        }

        public void Dispose()
        {
            _zeitgeber.Stop();
            _zeitgeber.Dispose();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Bedienen: Zeile wählen, Spalten sortieren, Registerkarten durchschalten
    // ------------------------------------------------------------------------------------------

    private static IEnumerable<Control> AlleSteuerelemente(Control c) => c.Controls.Cast<Control>().SelectMany(k => AlleSteuerelemente(k).Prepend(k));

    private static readonly System.Reflection.MethodInfo KopfKlickMethode =
        typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>Führt eine Bedienung aus; eine Ausnahme (auch aus Ereignissen) wird als Text zurückgegeben.</summary>
    private static string? Versuch(string was, Action aktion)
    {
        try
        {
            aktion();
            return null;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            return $"{was}: {ex.InnerException}";
        }
        catch (Exception ex)
        {
            return $"{was}: {ex}";
        }
    }

    /// <summary>Kurze Bedienung eines Fensters bzw. Dialogs: Registerkarten, Zeile wählen, nach den ersten Spalten sortieren.</summary>
    internal static List<string> FormBedienen(Form f)
    {
        var res = new List<string>();
        foreach (var tc in AlleSteuerelemente(f).OfType<TabControl>().Where(t => t.Visible && t.TabCount > 1).ToList())
        {
            var vorher = tc.SelectedIndex;
            for (var i = 0; i < tc.TabCount && !tc.IsDisposed; i++)
            {
                var k = i;
                if (Versuch($"Registerkarte {tc.TabPages[k].Text}", () => { tc.SelectedIndex = k; Pumpen(0); }) is { } p) res.Add(p);
            }
            if (!tc.IsDisposed && vorher >= 0 && vorher < tc.TabCount) tc.SelectedIndex = vorher;
        }
        foreach (var g in AlleSteuerelemente(f).OfType<DataGridView>().Where(g => g.Visible && g.RowCount > 0 && g.ColumnCount > 0).Take(6).ToList())
        {
            if (g.IsDisposed) continue;
            var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            if (spalten.Count == 0) continue;
            if (Versuch($"Zeile wählen in {g.Name}", () =>
                {
                    var z = g.Rows.Cast<DataGridViewRow>().Where(r => r.Visible && !r.IsNewRow).Skip(1).FirstOrDefault() ?? g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible);
                    if (z != null) g.CurrentCell = z.Cells[spalten[0].Index];
                    Pumpen(0);
                }) is { } p1) res.Add(p1);
            foreach (var c in spalten.Where(c => c.SortMode != DataGridViewColumnSortMode.NotSortable).Take(3).ToList())
                for (var i = 0; i < 2 && !g.IsDisposed && c.Index >= 0; i++)
                {
                    if (Versuch($"Sortieren nach {g.Name}.{c.Name}", () =>
                        {
                            var r = g.GetCellDisplayRectangle(c.Index, -1, false);
                            KopfKlickMethode.Invoke(g, new object[] { new DataGridViewCellMouseEventArgs(c.Index, -1, Math.Max(1, r.Width / 2), Math.Max(1, r.Height / 2),
                                new MouseEventArgs(MouseButtons.Left, 1, r.X + r.Width / 2, r.Y + r.Height / 2, 0)) });
                            Pumpen(0);
                        }) is { } p2) res.Add(p2);
                }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------
    // Kontextmenüs
    // ------------------------------------------------------------------------------------------

    /// <summary>Rechtsklick wie mit der Maus (Windows-Nachrichten, Mausposition gesetzt).</summary>
    private static void RechtsKlick(Control c, Point p)
    {
        if (c.IsDisposed || !c.IsHandleCreated) return;
        Cursor.Position = c.PointToScreen(p);
        var lp = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
        const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, MK_RBUTTON = 2;
        SendMessage(c.Handle, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lp);
        SendMessage(c.Handle, WM_RBUTTONUP, IntPtr.Zero, lp);
    }

    /// <summary>Stellen eines Fensters, an denen ein Kontextmenü erwartet wird (Tabellenzeilen, Stundenpläne, Steuerelemente mit Menü).</summary>
    private static List<(Control Ziel, Point Punkt, string Name)> KontextZiele(Control f)
    {
        var res = new List<(Control, Point, string)>();
        foreach (var c in AlleSteuerelemente(f).Where(c => c.Visible && c.Width > 10 && c.Height > 10))
        {
            if (c is DataGridView g)
            {
                if (g.RowCount == 0) continue;
                var spalte = g.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).OrderBy(x => x.DisplayIndex).FirstOrDefault();
                var zeile = g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible && !r.IsNewRow);
                if (spalte is null || zeile is null) continue;
                var r = g.GetCellDisplayRectangle(spalte.Index, zeile.Index, true);
                if (r.Width < 4 || r.Height < 4) continue;
                res.Add((g, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), "Tabelle " + g.Name));
            }
            else if (c is PlanAnsicht)
                res.Add((c, new Point(c.Width / 3, c.Height / 3), "Stundenplan"));
            else if (c.ContextMenuStrip != null && c is not DataGridView)
                res.Add((c, new Point(c.Width / 2, Math.Min(c.Height / 2, 12)), c.GetType().Name + " " + c.Name));
        }
        return res.Take(6).ToList();
    }

    private static readonly string[] KontextAuslassen = { "Beenden" };

    /// <summary>Alle Blatteinträge eines Kontextmenüs (Pfad aus Indizes und Text).</summary>
    private static List<(int[] Pfad, string Text)> MenuePfade(ToolStripItemCollection items, int[] pfad, string text)
    {
        var res = new List<(int[], string)>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not ToolStripMenuItem mi || !mi.Available) continue;
            var p = pfad.Append(i).ToArray();
            var t = text.Length == 0 ? mi.Text ?? "" : text + " › " + mi.Text;
            if (mi.HasDropDownItems)
            {
                mi.ShowDropDown();
                res.AddRange(MenuePfade(mi.DropDownItems, p, t));
                mi.HideDropDown();
            }
            else if (mi.Enabled && !KontextAuslassen.Contains(mi.Text)) res.Add((p, t));
        }
        return res;
    }

    private static ToolStripMenuItem? MenueEintrag(ContextMenuStrip m, int[] pfad)
    {
        ToolStripItemCollection items = m.Items;
        ToolStripMenuItem? mi = null;
        for (var k = 0; k < pfad.Length; k++)
        {
            if (pfad[k] >= items.Count || items[pfad[k]] is not ToolStripMenuItem x) return null;
            mi = x;
            if (k < pfad.Length - 1)
            {
                if (!x.HasDropDownItems) return null;
                x.ShowDropDown();
                items = x.DropDownItems;
            }
        }
        return mi;
    }

    // ------------------------------------------------------------------------------------------
    // Fensterrundgang
    // ------------------------------------------------------------------------------------------

    private static bool IstAusnahme(string text) =>
        text.Contains("Exception", StringComparison.Ordinal) && (text.Contains("   at ", StringComparison.Ordinal) || text.Contains("   bei ", StringComparison.Ordinal));

    /// <summary>Menübandeinträge (mit allen Untermenüs) einer Seite bzw. des Dateimenüs als Pfad und Eintrag.</summary>
    private static IEnumerable<(string Pfad, RibbonEintrag Eintrag)> RibbonFlach(string pfad, RibbonEintrag e)
    {
        var p = pfad + " › " + e.Text;
        yield return (p, e);
        if (e.Text == "Zuletzt geöffnet") yield break;
        foreach (var k in e.AktuellesMenue?.OfType<RibbonEintrag>() ?? Enumerable.Empty<RibbonEintrag>())
            foreach (var x in RibbonFlach(p, k)) yield return x;
    }

    /// <summary>
    /// R3D: Fensterrundgang mit der aktuellen Schuldatei bzw. einer leeren Datei. Jeder Eintrag des Menübands (beide Modi, Dateimenü,
    /// alle Untermenüs) wird ausgeführt, jedes geöffnete Fenster bedient (Zeile, Sortierung, Kontextmenüs beim ersten Auftreten
    /// des Fenstertyps, Datumswechsel auch außerhalb des Schuljahres, Neuaufbau). Liefert die gefundenen Ausnahmen bzw. Probleme.
    /// </summary>
    internal List<string> Fensterrundgang(bool leer, Action<string> melden)
    {
        List<string> fehler = new();
        var docVorher = AppZustand.Doc;
        var modusVorher = AppZustand.Vertretungsmodus;
        var datumVorher = AppZustand.Datum;
        Schuldatei basis = leer ? Demo.NeueDatei("Leere Schule", "GY") : Dokument.Klonen(docVorher.Daten);
        List<string> hinweise = new();
        var infoVorher = Ui.InfoAntwort;
        Ui.InfoAntwort = (t, _) => hinweise.Add(t);
        List<string> protokoll = Ui.FehlerProtokoll ??= new List<string>();
        var kontextGeprueft = new HashSet<string>();
        var dialoge = 0;
        var eintraege = 0;
        var kontextEintraege = 0;
        using DialogWaechter waechter = new DialogWaechter { Bedienen = FormBedienen };

        Dokument? frisch = null;
        var schmutzig = false;
        void Zuruecksetzen()
        {
            frisch = new Dokument(Dokument.Klonen(basis));
            AppZustand.Setzen(frisch, false);
        }
        // nur zurücksetzen, wenn ein Eintrag die Daten verändert hat (Löschen, Entplanen …)
        void BeiBedarfZuruecksetzen()
        {
            if (!ReferenceEquals(AppZustand.Doc, frisch) || schmutzig) Zuruecksetzen();
            schmutzig = false;
        }
        var gesehen = new HashSet<string>();
        // UNTPLAN_R3D_EINTRAEGE (regulärer Ausdruck): nur passende Menüband-Einträge ausführen (gezielte Prüfung)
        var eintragFilter = Environment.GetEnvironmentVariable("UNTPLAN_R3D_EINTRAEGE") is { Length: > 0 } ef ? new System.Text.RegularExpressions.Regex(ef) : null;

        void Auswerten(string ort, IEnumerable<string>? zusatz = null)
        {
            Pumpen(0);
            var probleme = protokoll.ToList();
            protokoll.Clear();
            foreach (var t in probleme)
            {
                if (IstAusnahme(t)) { fehler.Add($"{ort}: {t}"); melden($"FEHLER R3D {ort}: {t}"); }
                else melden($"INFO  R3D Hinweis {ort}: {Kurz(t)}");
            }
            foreach (var p in (zusatz ?? Enumerable.Empty<string>()).Concat(waechter.Probleme).ToList())
            {
                fehler.Add($"{ort}: {p}");
                melden($"FEHLER R3D {ort}: {p}");
            }
            waechter.Probleme.Clear();
            foreach (var h in hinweise) melden($"INFO  R3D Meldung {ort}: {Kurz(h)}");
            hinweise.Clear();
            dialoge += waechter.Dialoge.Count;
            foreach (var d in waechter.Dialoge) melden($"INFO  R3D Dialog {d}");
            waechter.Dialoge.Clear();
        }

        // alles schließen, was nach dem Stand „behalten“ dazugekommen ist (Menüs, Dialoge, Fenster)
        void Aufraeumen(HashSet<Form> behalten)
        {
            MenuesSchliessen();
            foreach (var f in Application.OpenForms.Cast<Form>().Where(f => f != this && !behalten.Contains(f)).ToList())
                if (!f.IsDisposed) f.Close();
            Pumpen(20);
        }

        void DatumWechseln(string ort, bool alle)
        {
            var d = AppZustand.D;
            var start = AppZustand.Datum;
            var naechster = Kalender.NaechsterUnterrichtstag(d, start);
            var ausserhalb = d.Einstellungen.SchuljahrBis.AddDays(40);
            var tage = alle
                ? new[] { ("nächster Tag", naechster), ("außerhalb des Schuljahres", ausserhalb), ("vor dem Schuljahr", d.Einstellungen.SchuljahrVon.AddDays(-30)), ("zurück", start) }
                : new[] { ("außerhalb des Schuljahres", ausserhalb), ("zurück", start) };
            foreach (var (name, tag) in tage)
                if (Versuch("Datum " + name, () => { AppZustand.DatumSetzen(tag); Pumpen(0); }) is { } p) { fehler.Add($"{ort}: {p}"); melden($"FEHLER R3D {ort}: {p}"); }
        }

        void Neuaufbau(string ort)
        {
            if (Versuch("Neuaufbau", () => { AppZustand.Doc.Melden(AenderungsArt.Alles); Pumpen(0); }) is { } p) { fehler.Add($"{ort}: {p}"); melden($"FEHLER R3D {ort}: {p}"); }
        }

        void Kontextmenues(Form f, string ort)
        {
            var ziele = KontextZiele(f);
            if (ziele.Count == 0) melden($"INFO  R3D Kontextmenü {ort}: keine Tabellenzeile/kein Stundenplan");
            foreach (var (ziel, punkt, name) in ziele)
            {
                if (f.IsDisposed || ziel.IsDisposed) break;
                var behalten = Application.OpenForms.Cast<Form>().ToHashSet();
                MenuesSchliessen();
                RechtsKlick(ziel, punkt);
                var menue = OffeneKontextmenues().FirstOrDefault();
                if (menue is null) { MenuesSchliessen(); melden($"INFO  R3D Kontextmenü {ort} / {name}: kein Menü"); continue; }
                var pfade = MenuePfade(menue.Items, Array.Empty<int>(), "");
                MenuesSchliessen();
                Auswerten($"{ort} / Kontextmenü {name}");
                melden($"INFO  R3D Kontextmenü {ort} / {name}: {pfade.Count} Einträge");
                foreach (var (pfad, text) in pfade.Take(40))
                {
                    if (f.IsDisposed || ziel.IsDisposed) break;
                    var o = $"{ort} / Kontextmenü {name} › {text}";
                    waechter.Ort = o;
                    if (f.IsMdiChild && !f.IsDisposed) f.Activate();
                    RechtsKlick(ziel, punkt);
                    var m = OffeneKontextmenues().FirstOrDefault();
                    var eintrag = m is null ? null : MenueEintrag(m, pfad);
                    if (eintrag is null || eintrag.Text != text.Split(" › ").Last() || !eintrag.Enabled) { MenuesSchliessen(); continue; }
                    kontextEintraege++;
                    MenuesSchliessen();
                    var p = Versuch("Klick", eintrag.PerformClick);
                    Pumpen(120);
                    Aufraeumen(behalten);
                    Auswerten(o, p is null ? null : new[] { p });
                    // Daten für den nächsten Eintrag wiederherstellen (z. B. nach „Löschen“)
                    if (AppZustand.Doc.IstGeaendert || !ReferenceEquals(AppZustand.Doc, frisch)) { Zuruecksetzen(); Pumpen(60); Auswerten(o + " (Zurücksetzen)"); }
                }
            }
        }

        void FensterPruefen(Form f, string ort, bool kontext)
        {
            if (f.IsDisposed) return;
            var o = $"{ort} / {f.GetType().Name} „{f.Text}“";
            waechter.Ort = o;
            Auswerten(o, FormBedienen(f));
            if (kontext && kontextGeprueft.Add(f.GetType().Name)) Kontextmenues(f, o);
        }

        try
        {
            if (AppZustand.Vertretungsmodus) VertretungsmodusUmschalten();
            Zuruecksetzen();
            foreach (var vertretung in new[] { false, true })
            {
                var modus = vertretung ? "Vertretungsmodus" : "Stundenplanmodus";
                waechter.Ort = modus + " / Umschalten";
                if (AppZustand.Vertretungsmodus != vertretung)
                    if (Versuch("Modus umschalten", VertretungsmodusUmschalten) is { } pm) Auswerten(modus, new[] { pm });
                Zuruecksetzen();
                waechter.Ort = modus + " / Standardfenster";
                if (Versuch("Standardfenster", () => { if (vertretung) VertretungsStandardFenster(); else StandardFenster(); }) is { } ps) Auswerten(modus, new[] { ps });
                Pumpen(400);
                Auswerten(modus + " / Standardfenster");
                foreach (var f in MdiChildren.ToList()) FensterPruefen(f, modus + " / Standardfenster", true);
                // Kontextmenü des Arbeitsbereichs
                if (Controls.OfType<MdiClient>().FirstOrDefault() is { } mdi)
                {
                    AlleSchliessen();
                    Pumpen(100);
                    var behalten = Application.OpenForms.Cast<Form>().ToHashSet();
                    RechtsKlick(mdi, new Point(mdi.ClientSize.Width / 2, mdi.ClientSize.Height / 2));
                    var m = OffeneKontextmenues().FirstOrDefault();
                    if (m != null)
                    {
                        var pfade = MenuePfade(m.Items, Array.Empty<int>(), "");
                        MenuesSchliessen();
                        foreach (var (pfad, text) in pfade)
                        {
                            var o = $"{modus} / Kontextmenü Arbeitsbereich › {text}";
                            waechter.Ort = o;
                            RechtsKlick(mdi, new Point(mdi.ClientSize.Width / 2, mdi.ClientSize.Height / 2));
                            var mm = OffeneKontextmenues().FirstOrDefault();
                            if (mm is null || MenueEintrag(mm, pfad) is not { Enabled: true } e) { MenuesSchliessen(); continue; }
                            kontextEintraege++;
                            MenuesSchliessen();
                            var p = Versuch("Klick", e.PerformClick);
                            Pumpen(150);
                            Aufraeumen(behalten);
                            AlleSchliessen();
                            Auswerten(o, p is null ? null : new[] { p });
                        }
                    }
                }
                DatumWechseln(modus + " / Standardfenster", true);

                // ein Kontextmenüeintrag kann den Modus gewechselt haben: Menüband des geprüften Modus durchgehen
                if (AppZustand.Vertretungsmodus != vertretung)
                {
                    melden($"INFO  R3D {modus}: Modus wurde durch einen Kontextmenüeintrag gewechselt – zurückgeschaltet");
                    VertretungsmodusUmschalten();
                    AlleSchliessen();
                }
                var liste = new List<(string Pfad, RibbonEintrag Eintrag)>();
                if (!vertretung) liste.AddRange(_ribbon.DateiMenue.OfType<RibbonEintrag>().SelectMany(e => RibbonFlach("Datei", e)));
                foreach (var seite in _ribbon.Seiten.Where(s => s.IstSichtbar))
                    foreach (var gruppe in seite.Gruppen)
                        foreach (var e in gruppe.Eintraege)
                            liste.AddRange(RibbonFlach($"{seite.Text} › {gruppe.Text}", e));
                foreach (var (pfad, e) in liste)
                {
                    var ort = $"{modus} / Menüband {pfad}";
                    waechter.Ort = ort;
                    // Menü wie im Menüband aufbauen (Aktiviert, Gedrückt, erzeugte Untermenüs)
                    if (e.AktuellesMenue is { } menue && Versuch("Menü aufbauen", () => { foreach (var x in Ribbon.MenueEintraege(menue)) x.Dispose(); }) is { } pa)
                        Auswerten(ort, new[] { pa });
                    if (e.Klick is null || e.IstDatum || (eintragFilter != null && !eintragFilter.IsMatch(pfad))) continue;
                    if (e.Text is "Beenden" or "Nach Updates suchen" || e.Klick == (Action)VertretungsmodusUmschalten) { melden($"INFO  R3D ausgelassen: {ort}"); continue; }
                    if (e.Aktiviert?.Invoke() == false) continue;
                    AlleSchliessen();
                    BeiBedarfZuruecksetzen();
                    Auswerten(ort + " (Vorbereitung)");
                    var behalten = Application.OpenForms.Cast<Form>().ToHashSet();
                    eintraege++;
                    melden($"  … {ort}");
                    var p = Versuch("Klick", e.Klick);
                    MenuesSchliessen();
                    Pumpen(250);
                    Auswerten(ort, p is null ? null : new[] { p });
                    bool? vorNeuaufbau = null;
                    var neu = Application.OpenForms.Cast<Form>().Where(f => f != this && !behalten.Contains(f) && f.Visible).ToList();
                    // bereits geprüfte Fenster (gleicher Typ und Titel aus einem anderen Eintrag) nicht erneut bedienen
                    var signatur = string.Join("|", neu.Select(f => f.GetType().Name + ":" + f.Text).OrderBy(x => x));
                    if (neu.Count > 0 && gesehen.Add(signatur))
                    {
                        foreach (var f in neu) FensterPruefen(f, ort, f.IsMdiChild);
                        waechter.Ort = ort + " / Datum und Neuaufbau";
                        // der Neuaufbau selbst verändert nichts – nur echte Änderungen erzwingen das Zurücksetzen
                        vorNeuaufbau = AppZustand.Doc.IstGeaendert;
                        DatumWechseln(ort, false);
                        Neuaufbau(ort);
                        Pumpen(60);
                        Auswerten(ort + " / Datum und Neuaufbau");
                    }
                    Aufraeumen(behalten);
                    if (AppZustand.Vertretungsmodus != vertretung) VertretungsmodusUmschalten();
                    AlleSchliessen();
                    schmutzig = vorNeuaufbau ?? AppZustand.Doc.IstGeaendert;
                    Auswerten(ort + " / Schließen");
                }
            }
        }
        finally
        {
            MenuesSchliessen();
            AlleSchliessen();
            if (AppZustand.Vertretungsmodus != modusVorher) VertretungsmodusUmschalten();
            AlleSchliessen();
            AppZustand.Setzen(docVorher, false);
            AppZustand.DatumSetzen(datumVorher);
            Ui.InfoAntwort = infoVorher;
            Auswerten("Abschluss");
        }
        melden($"INFO  R3D Fensterrundgang ({(leer ? "leere Datei" : "Beispielschule")}): {eintraege} Menübandeinträge, {kontextEintraege} Kontextmenüeinträge, {dialoge} Dialoge automatisch geschlossen, {fehler.Count} Fehler");
        return fehler;
    }

    // ------------------------------------------------------------------------------------------
    // Fehleingaben
    // ------------------------------------------------------------------------------------------

    private enum EingabeArt { Text, Kuerzel, Zahl, Datum, Auswahl }

    /// <summary>Ungültige bzw. extreme Eingaben je Spaltenart; „ungültig“ = darf die Daten nicht verändern.</summary>
    private static IEnumerable<(string Wert, bool Ungueltig, string Beschreibung)> Fehlwerte(EingabeArt art, Schuldatei d) => art switch
    {
        EingabeArt.Zahl => new[] { ("abc", true, "Text im Zahlenfeld"), ("99999999999", true, "zu große Zahl"), ("-7", false, "negative Zahl"), ("", false, "leer") },
        EingabeArt.Datum => new[] { ("31.02.2026", true, "ungültiges Datum"), ("abc", true, "Text im Datumsfeld"),
            (d.Einstellungen.SchuljahrVon.AddYears(-5).ToString("dd.MM.yyyy"), true, "Datum außerhalb des Schuljahres") },
        EingabeArt.Auswahl => new[] { ("XYZ_UNBEKANNT", true, "unbekanntes Kürzel"), ("", false, "leer") },
        EingabeArt.Kuerzel => new[] { ("", true, "leeres Pflichtfeld"), ("XYZ_UNBEKANNT", false, "unbekanntes Kürzel"), (new string('K', 300), false, "sehr langes Kürzel"),
            ("<b>&\"';|\t€ÄÖÜß😀", false, "Sonderzeichen") },
        _ => new[] { (new string('X', 5000), false, "sehr langer Text"), ("<b>&\"';|\t€ÄÖÜß😀\\n", false, "Sonderzeichen"), ("XYZ_UNBEKANNT", false, "unbekanntes Kürzel") },
    };

    private static EingabeArt SpaltenArt(DataGridView g, DataGridViewColumn c, int zeile, bool erste)
    {
        if (c is DataGridViewComboBoxColumn || g.Rows[zeile].Cells[c.Index] is DataGridViewComboBoxCell) return EingabeArt.Auswahl;
        var text = g.Rows[zeile].Cells[c.Index].FormattedValue?.ToString()?.Trim() ?? "";
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        if (text.Length > 0 && decimal.TryParse(text, System.Globalization.NumberStyles.Number, de, out _)) return EingabeArt.Zahl;
        if (text.Length > 0 && DateTime.TryParseExact(text, new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M." }, de, System.Globalization.DateTimeStyles.None, out _)) return EingabeArt.Datum;
        return erste ? EingabeArt.Kuerzel : EingabeArt.Text;
    }

    /// <summary>Allgemeine Regeln der Schuldatei, die keine Eingabe verletzen darf.</summary>
    private static List<string> DatenRegeln(Schuldatei d)
    {
        var res = new List<string>();
        foreach (var typ in new[] { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum, ElementTyp.Fach })
        {
            var elemente = d.AlleElemente(typ).ToList();
            if (elemente.Any(e => string.IsNullOrWhiteSpace(e.Kurzname))) res.Add($"{typ} ohne Kürzel");
            foreach (var doppelt in elemente.GroupBy(e => e.Kurzname.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1 && x.Key.Length > 0))
                res.Add($"{typ} „{doppelt.Key}“ doppelt");
        }
        if (d.Abwesenheiten.Any(a => a.Bis < a.Von)) res.Add("Absenz mit Bis vor Von");
        return res;
    }

    /// <summary>
    /// R3D: Fehleingaben in den wichtigsten Tabellen (Stammdaten, Unterricht, Ferien, Absenzen, Vertretungen, Veranstaltungen). Je Spalte
    /// werden ungültige Werte eingetragen; geprüft wird: keine Ausnahme, kein Standard-Fehlerfenster, bei ungültigen Werten unveränderte
    /// Daten und ein Hinweis (Meldung oder Fehlertext an der Zelle), bei allen Werten die allgemeinen Datenregeln.
    /// </summary>
    internal List<string> Fehleingaben(Action<string> melden)
    {
        List<string> fehler = new();
        var docVorher = AppZustand.Doc;
        var modusVorher = AppZustand.Vertretungsmodus;
        var datumVorher = AppZustand.Datum;
        Schuldatei basis = Dokument.Klonen(docVorher.Daten);
        List<string> hinweise = new();
        var infoVorher = Ui.InfoAntwort;
        Ui.InfoAntwort = (t, _) => hinweise.Add(t);
        List<string> protokoll = Ui.FehlerProtokoll ??= new List<string>();
        using DialogWaechter waechter = new DialogWaechter();
        var regelnVorher = DatenRegeln(basis);
        var versuche = 0;
        var ohneHinweis = 0;

        void Zuruecksetzen() => AppZustand.Setzen(new Dokument(Dokument.Klonen(basis)), false);
        byte[] Stand() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(AppZustand.D, Dokument.JsonOptionen);
        void Fehler(string t) { fehler.Add(t); melden("FEHLER R3D " + t); }

        var tag = basis.Abwesenheiten.Select(a => a.Von).DefaultIfEmpty(AppZustand.Datum).First();
        var fenster = new (string Name, bool Vertretung, string Schluessel)[]
        {
            ("Klassen", false, "st:Klasse"), ("Lehrer", false, "st:Lehrer"), ("Räume", false, "st:Raum"), ("Fächer", false, "st:Fach"),
            ("Unterricht", false, "u:Klasse"), ("Ferien", false, "ferien"),
            ("Absenzen", true, "abw"), ("Vertretungen", true, "vertr"), ("Veranstaltungen", true, "veranst"),
        };

        DataGridView? Tabelle(Form f) => AlleSteuerelemente(f).OfType<DataGridView>().Where(g => g.Visible && !g.ReadOnly && g.ColumnCount > 0)
            .OrderByDescending(g => g.Width * g.Height).FirstOrDefault();

        try
        {
            foreach (var (name, vertretung, schluessel) in fenster)
            {
                if (AppZustand.Vertretungsmodus != vertretung) VertretungsmodusUmschalten();
                AlleSchliessen();
                Zuruecksetzen();
                if (vertretung) AppZustand.DatumSetzen(tag);
                var f = FensterOeffnen(schluessel);
                if (f is null) { Fehler($"{name}: Fenster lässt sich nicht öffnen"); continue; }
                f.WindowState = FormWindowState.Maximized;
                Pumpen(300);
                var g0 = Tabelle(f);
                if (g0 is null || g0.Rows.Cast<DataGridViewRow>().All(r => r.IsNewRow || !r.Visible)) { melden($"INFO  R3D Fehleingaben {name}: keine bearbeitbare Zeile"); continue; }
                var zeile0 = g0.Rows.Cast<DataGridViewRow>().First(r => !r.IsNewRow && r.Visible).Index;
                var spalten = g0.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.Visible && !c.ReadOnly && !g0.Rows[zeile0].Cells[c.Index].ReadOnly && c is not DataGridViewCheckBoxColumn and not DataGridViewButtonColumn and not DataGridViewImageColumn)
                    .OrderBy(c => c.DisplayIndex).Select(c => c.Name).ToList();
                // Spaltenart vorab bestimmen (die Tabelle wird bei jedem Versuch neu aufgebaut)
                var arten = spalten.ToDictionary(n => n, n => SpaltenArt(g0, g0.Columns[n]!, zeile0, n == "Kurzname"));
                foreach (var spaltenName in spalten)
                {
                    var art = arten[spaltenName];
                    foreach (var (wert, ungueltig, beschreibung) in Fehlwerte(art, basis))
                    {
                        var ort = $"Fehleingaben {name}.{spaltenName} ({art}): {beschreibung}";
                        waechter.Ort = ort;
                        Zuruecksetzen();
                        Pumpen(60);
                        protokoll.Clear();
                        hinweise.Clear();
                        if (f.IsDisposed) { Fehler($"{ort}: Fenster wurde geschlossen"); break; }
                        var g = Tabelle(f);
                        var zeile = g?.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && r.Visible);
                        if (g is null || zeile is null || g.Columns[spaltenName] is not { } spalte) continue;
                        var vorher = Stand();
                        // Hinweisleiste des Unterrichtsfensters (ungültige Eingabe: „Bitte prüfen: …“ und Fehlersymbol)
                        var leisteVorher = (f as Fenster.UnterrichtFenster)?.HinweisText;
                        string? leisteNachher = null;
                        var zelleVorher = zeile.Cells[spalte.Index].FormattedValue?.ToString() ?? "";
                        var begonnen = false;
                        string? fehlerText = null;
                        var p = Versuch("Eingabe", () =>
                        {
                            g.CurrentCell = zeile.Cells[spalte.Index];
                            begonnen = g.BeginEdit(true);
                            if (!begonnen) return;
                            switch (g.EditingControl)
                            {
                                case TextBox tb:
                                    tb.Text = wert;
                                    break;
                                case ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList } cb:
                                    cb.Text = wert;
                                    break;
                                default:
                                    begonnen = false;
                                    g.CancelEdit();
                                    g.EndEdit();
                                    return;
                            }
                            g.NotifyCurrentCellDirty(true);
                            var ok = g.EndEdit();
                            leisteNachher = (f as Fenster.UnterrichtFenster)?.HinweisText;
                            if (!ok)
                            {
                                fehlerText = g.CurrentCell?.ErrorText is { Length: > 0 } et ? et : g.CurrentRow?.ErrorText;
                                g.CancelEdit();
                                g.EndEdit();
                            }
                            // Zeile verlassen (Übernahme zeilenweiser Eingaben)
                            var andere = g.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible && !r.IsNewRow && r.Index != zeile.Index);
                            if (andere != null && !g.IsDisposed) g.CurrentCell = andere.Cells[spalte.Index];
                            Pumpen(0);
                        });
                        if (!begonnen && p is null) continue;
                        versuche++;
                        Pumpen(120);
                        var geaendert = !vorher.AsSpan().SequenceEqual(Stand());
                        var ausnahmen = protokoll.Where(IstAusnahme).ToList();
                        var meldungen = protokoll.Where(t => !IstAusnahme(t)).Concat(hinweise).ToList();
                        if (fehlerText is { Length: > 0 }) meldungen.Add(fehlerText);
                        if (leisteNachher != null && leisteNachher != leisteVorher && leisteNachher.StartsWith("Bitte prüfen", StringComparison.Ordinal)) meldungen.Add(leisteNachher);
                        var zeileGueltig = !g.IsDisposed && zeile.Index >= 0 && zeile.Index < g.RowCount;
                        if (zeileGueltig && g.Rows[zeile.Index].Cells[spalte.Index].ErrorText is { Length: > 0 } zt) meldungen.Add(zt);
                        protokoll.Clear();
                        hinweise.Clear();
                        if (p != null) Fehler($"{ort}: {p}");
                        foreach (var a in ausnahmen) Fehler($"{ort}: {a}");
                        foreach (var w in waechter.Probleme) Fehler($"{ort}: {w}");
                        waechter.Probleme.Clear();
                        waechter.Dialoge.Clear();
                        foreach (var r in DatenRegeln(AppZustand.D).Except(regelnVorher)) Fehler($"{ort}: Datenregel verletzt – {r}");
                        var zelleNachher = zeileGueltig ? g.Rows[zeile.Index].Cells[spalte.Index].FormattedValue?.ToString() ?? "" : "";
                        if (ungueltig && geaendert) Fehler($"{ort}: ungültiger Wert „{Kurz(wert)}“ wurde übernommen (Daten geändert; Zelle „{Kurz(zelleVorher)}“ → „{Kurz(zelleNachher)}“)");
                        else if (ungueltig && meldungen.Count == 0)
                        {
                            ohneHinweis++;
                            Fehler($"{ort}: ungültiger Wert „{Kurz(wert)}“ ohne Hinweis verworfen (Zelle „{Kurz(zelleVorher)}“ → „{Kurz(zelleNachher)}“)");
                        }
                        if (meldungen.Count > 0) melden($"INFO  R3D {ort}: Hinweis „{Kurz(string.Join(" / ", meldungen))}“{(geaendert ? " (Daten geändert)" : "")}");
                        else if (!ungueltig) melden($"INFO  R3D {ort}: {(geaendert ? "übernommen" : "unverändert")} (Zelle „{Kurz(zelleNachher)}“)");
                        // der eingegebene Wert übersteht Speichern und Laden
                        if (geaendert && Versuch("Speichern/Laden", () => Dokument.Klonen(AppZustand.D)) is { } pk) Fehler($"{ort}: {pk}");
                    }
                }
                // leere Pflichtfelder in einer neuen Zeile: nur einen Langnamen eingeben
                if (f is Fenster.StammdatenFenster sf && !f.IsDisposed && spalten.Count > 1)
                {
                    var ort = $"Fehleingaben {name}: neue Zeile ohne Kürzel";
                    waechter.Ort = ort;
                    Zuruecksetzen();
                    Pumpen(60);
                    if (Tabelle(f) is not { AllowUserToAddRows: true, NewRowIndex: >= 0 } gn || gn.Columns[spalten[1]] is not { } lang) continue;
                    var anzahl = AppZustand.D.AlleElemente(sf.Typ).Count();
                    var p = Versuch("Neue Zeile", () =>
                    {
                        gn.CurrentCell = gn.Rows[gn.NewRowIndex].Cells[lang.Index];
                        if (!gn.BeginEdit(true) || gn.EditingControl is not TextBox tb) return;
                        tb.Text = "Nur ein Langname";
                        gn.NotifyCurrentCellDirty(true);
                        if (!gn.EndEdit()) { gn.CancelEdit(); gn.EndEdit(); }
                        var erste = gn.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible && !r.IsNewRow);
                        if (erste != null) gn.CurrentCell = erste.Cells[lang.Index];
                        Pumpen(0);
                    });
                    Pumpen(120);
                    if (p != null) Fehler($"{ort}: {p}");
                    foreach (var a in protokoll.Where(IstAusnahme)) Fehler($"{ort}: {a}");
                    foreach (var r in DatenRegeln(AppZustand.D).Except(regelnVorher)) Fehler($"{ort}: Datenregel verletzt – {r}");
                    var meldungen = protokoll.Where(t => !IstAusnahme(t)).Concat(hinweise).ToList();
                    melden($"INFO  R3D {ort}: {AppZustand.D.AlleElemente(sf.Typ).Count() - anzahl} neue(s) Element(e), Hinweis „{Kurz(string.Join(" / ", meldungen))}“");
                    protokoll.Clear();
                    hinweise.Clear();
                }
            }
        }
        finally
        {
            AlleSchliessen();
            if (AppZustand.Vertretungsmodus != modusVorher) VertretungsmodusUmschalten();
            AlleSchliessen();
            AppZustand.Setzen(docVorher, false);
            AppZustand.DatumSetzen(datumVorher);
            Ui.InfoAntwort = infoVorher;
            protokoll.Clear();
        }
        melden($"INFO  R3D Fehleingaben: {versuche} Eingaben geprüft, {ohneHinweis} ohne Hinweis verworfen, {fehler.Count} Fehler");
        return fehler;
    }

    private static string Kurz(string t)
    {
        t = t.Replace("\r", "").Replace('\n', ' ');
        return t.Length > 300 ? t[..300] + "…" : t;
    }
}
