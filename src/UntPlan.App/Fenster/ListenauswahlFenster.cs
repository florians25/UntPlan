using System.Text;
using UntPlan.App.Dialoge;
using UntPlan.App.Druck;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Listenauswahl wie im Vorbild: alle Berichte und Listen des Programms in einem Baum nach Gruppen (Übersichten, Belegungs - Statistik,
/// Freie Stunden, Fach - Stunden - Liste, Stunden-Listen, Unterricht, Pausenaufsichten, Vertretungsplanung, Klausurpläne) – die Gruppen
/// sind zugeklappt (Ordner mit +). Doppelklick bzw. „Ausgabe“ öffnet den Dialog bzw. Bericht der gewählten Liste.
/// </summary>
public sealed class ListenauswahlFenster : MdiFenster
{
    /// <summary>Eine Liste im Baum: Gruppe, Name, kurze Beschreibung und Ausgabe.</summary>
    internal sealed record Liste(string Gruppe, string Name, string Beschreibung, Action<ListenauswahlFenster> Ausgabe);

    public const string Uebersichten = "Übersichten";
    public const string Belegung = "Belegungs - Statistik";
    public const string FreieStunden = "Freie Stunden";
    public const string FachStunden = "Fach - Stunden - Liste";
    public const string StundenListen = "Stunden-Listen";
    public const string Unterricht = "Unterricht";
    public const string Pausenaufsichten = "Pausenaufsichten";
    public const string Vertretungsplanung = "Vertretungsplanung";
    public const string Klausuren = "Klausurpläne";
    /// <summary>Gruppen in der Reihenfolge des Vorbilds.</summary>
    public static readonly string[] Gruppen = { Uebersichten, Belegung, FreieStunden, FachStunden, StundenListen, Unterricht, Pausenaufsichten, Vertretungsplanung, Klausuren };

    private readonly TreeView _baum = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = true, ItemHeight = 19 }; // Zeilenabstand 19 px wie im Handbuch-Bild
    private readonly Label _beschreibung = new() { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(6), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(250, 250, 245) };
    private readonly ToolStripButton _ausgabe;
    private readonly DateTimePicker _datum = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 100 };

    /// <summary>Datum für Listen mit Zeitbezug (Woche der freien Stunden, Vertretungsplan).</summary>
    private DateOnly Datum => DateOnly.FromDateTime(_datum.Value);

    /// <summary>Vorbelegter Zeitraum der Vertretungsberichte wie im Vorbild: Monatsbeginn des Datums bis Monatsende, höchstens Schuljahresende.</summary>
    private DateOnly BerichtVon => new(Datum.Year, Datum.Month, 1);
    private DateOnly BerichtBis
    {
        get
        {
            var ende = BerichtVon.AddMonths(1).AddDays(-1);
            var sj = AppZustand.D.Einstellungen.SchuljahrBis;
            return sj >= BerichtVon && sj < ende ? sj : ende;
        }
    }

    public ListenauswahlFenster()
    {
        Text = "Listenauswahl";
        HilfeThema = "Berichte";
        Icon = SymbolIcon("drucken");
        Size = new Size(380, 460);
        Positionieren();
        var ts = Werkzeugleiste();
        _ausgabe = Knopf("Ausgabe", "drucken", Ausgeben, false);
        ts.Items.Add(_ausgabe);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripLabel("Datum"));
        ts.Items.Add(new ToolStripControlHost(_datum) { ToolTipText = "Datum für Listen mit Zeitbezug (z. B. Woche der freien Stunden, Vertretungsplan)" });
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Knopf("Berichts-Einstellungen…", "einstellungen", () => BerichtseinstellungenDialog.Zeigen(this)));
        ts.Items.Add(Knopf("Seite einrichten", "vorschau", BerichtsansichtFenster.SeiteEinrichtenOhneBericht));
        _datum.Value = AppZustand.Datum.ToDateTime(TimeOnly.MinValue);
        _baum.ImageList = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        _baum.ImageList.Images.Add("gruppe", Symbole.Get("oeffnen", 16));
        _baum.ImageList.Images.Add("liste", Symbole.Get("drucken", 16));
        foreach (var gruppe in Gruppen)
        {
            var g = _baum.Nodes.Add(gruppe, gruppe, "gruppe", "gruppe");
            g.NodeFont = Ui.SchriftFett;
            foreach (var l in Alle().Where(x => x.Gruppe == gruppe))
                g.Nodes.Add(new TreeNode(l.Name) { Tag = l, ImageKey = "liste", SelectedImageKey = "liste", ToolTipText = l.Beschreibung });
        }
        _baum.ShowNodeToolTips = true;
        // wie im Vorbild: alle Gruppen zugeklappt, Ein-/Ausklappen mit + / − bzw. Doppelklick auf den Ordner
        _baum.CollapseAll();
        _baum.AfterSelect += (_, _) => Gewaehlt();
        _baum.NodeMouseDoubleClick += (_, e) => { if (e.Node.Tag is Liste) Ausgeben(); };
        _baum.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Ausgeben(); e.Handled = true; } };
        Controls.Add(_baum);
        Controls.Add(_beschreibung);
        Controls.Add(ts);
        if (_baum.Nodes.Count > 0) _baum.SelectedNode = _baum.Nodes[0];
        Gewaehlt();
    }

    /// <summary>Selbsttest: Gruppen sind wie im Vorbild zunächst zugeklappt.</summary>
    internal bool SelbsttestZugeklappt => _baum.Nodes.Cast<TreeNode>().All(n => !n.IsExpanded);

    private void Gewaehlt()
    {
        var l = _baum.SelectedNode?.Tag as Liste;
        _ausgabe.Enabled = l != null;
        _beschreibung.Text = l?.Beschreibung ?? "Liste wählen und mit Doppelklick bzw. <Ausgabe> ausgeben.";
    }

    private void Ausgeben()
    {
        if (_baum.SelectedNode?.Tag is not Liste l) return;
        try { l.Ausgabe(this); }
        catch (Exception ex) { Ui.Fehler(ex); }
    }

    /// <summary>Selbsttest: Gruppen mit ihren Listen.</summary>
    internal Dictionary<string, List<string>> SelbsttestListen() =>
        _baum.Nodes.Cast<TreeNode>().ToDictionary(n => n.Text, n => n.Nodes.Cast<TreeNode>().Select(x => x.Text).ToList());

    /// <summary>Selbsttest: Liste per Name ausgeben (wie Doppelklick).</summary>
    internal void SelbsttestAusgeben(string name)
    {
        _baum.SelectedNode = _baum.Nodes.Cast<TreeNode>().SelectMany(n => n.Nodes.Cast<TreeNode>()).First(n => n.Text == name);
        Ausgeben();
    }

    private static void Druckbar(Form? f, bool vorschau = true)
    {
        if (f is IDruckbar d) d.Drucken(vorschau);
    }

    /// <summary>Alle Listen der Listenauswahl.</summary>
    internal static List<Liste> Alle()
    {
        var h = AppZustand.Haupt;
        var res = new List<Liste>
        {
            // Übersichten (Übersichtspläne und Stundenpläne)
            new(Uebersichten, "Klassen-Übersicht", "Stundenpläne aller Klassen auf einen Blick (Querformat, Seitenansicht).", _ => { var f = new UebersichtFenster(ElementTyp.Klasse); f.Show(); f.Drucken(true); }),
            new(Uebersichten, "Lehrer-Übersicht", "Stundenpläne aller Lehrkräfte auf einen Blick (Querformat, Seitenansicht).", _ => { var f = new UebersichtFenster(ElementTyp.Lehrer); f.Show(); f.Drucken(true); }),
            new(Uebersichten, "Raum-Übersicht", "Belegung aller Räume auf einen Blick (Querformat, Seitenansicht).", _ => { var f = new UebersichtFenster(ElementTyp.Raum); f.Show(); f.Drucken(true); }),
            new(Uebersichten, "Klassenpläne", "Stundenpläne der Klassen drucken (Druckauswahl mit Seitenansicht, PDF und HTML).", f => DruckauswahlDialog.Stundenplaene(f, ElementTyp.Klasse)),
            new(Uebersichten, "Lehrerpläne", "Stundenpläne der Lehrkräfte drucken (Druckauswahl mit Seitenansicht, PDF und HTML).", f => DruckauswahlDialog.Stundenplaene(f, ElementTyp.Lehrer)),
            new(Uebersichten, "Raumpläne", "Raumpläne drucken (Druckauswahl mit Seitenansicht, PDF und HTML).", f => DruckauswahlDialog.Stundenplaene(f, ElementTyp.Raum)),
            new(Uebersichten, "Fächerpläne", "Stundenpläne je Fach drucken (Druckauswahl mit Seitenansicht, PDF und HTML).", f => DruckauswahlDialog.Stundenplaene(f, ElementTyp.Fach)),
            // Belegungs - Statistik
            new(Belegung, Listenberichte.BelegungTitel, "Je Tag und Stunde der Woche des gewählten Datums: belegte Lehrkräfte und Räume (Anzahl und Prozent) laut Stundenplan.", f => Listenberichte.Belegung(f.Datum)),
            // Freie Stunden
            new(FreieStunden, Listenberichte.FreieStundenTitel, "Je Tag und Stunde der Woche des gewählten Datums: freie Lehrkräfte und freie Räume laut Stundenplan.", f => Listenberichte.FreieStunden(f.Datum)),
            // Fach - Stunden - Liste
            new(FachStunden, Listenberichte.FachStundenTitel, "Je Fach: Wochenstunden je Klasse und Lehrkraft mit Summen (Unterricht der aktiven Periode).", _ => Listenberichte.FachStunden()),
            // Stunden-Listen
            new(StundenListen, Listenberichte.StundenListeTitel, "Je Lehrkraft alle Stunden der Woche des gewählten Datums (Tag, Stunde, Klasse(n), Fach, Raum) laut Stundenplan.", f => Listenberichte.StundenListe(f.Datum)),
            // Unterricht
            new(Unterricht, "Unterricht der Klassen", "Unterrichtsliste der Klassen (Seitenansicht).", _ => Druckbar(HauptFenster.Oeffnen<UnterrichtFenster>("u:Klasse", () => new UnterrichtFenster(ElementTyp.Klasse)))),
            new(Unterricht, "Unterricht der Lehrkräfte", "Unterrichtsliste der Lehrkräfte (Seitenansicht).", _ => Druckbar(HauptFenster.Oeffnen<UnterrichtFenster>("u:Lehrer", () => new UnterrichtFenster(ElementTyp.Lehrer)))),
            new(Unterricht, "Unterrichtsplanung (Wertrechnung)", "Soll/Ist der Lehrkräfte laut Wertrechnung (Seitenansicht).", _ => Druckbar(h.FensterOeffnen("wert"))),
            // Pausenaufsichten
            new(Pausenaufsichten, "Pausenaufsichtsplan", "Pausenaufsichten je Gang bzw. Lehrkraft (Seitenansicht).", _ => Druckbar(h.FensterOeffnen("pause"))),
            new(Pausenaufsichten, Listenberichte.PausenaufsichtenTitel, "Alle Pausenaufsichten der aktiven Periode als Liste je Lehrkraft mit Summen.", _ => Listenberichte.PausenaufsichtenListe()),
        };
        // Vertretungsplanung: zuerst wie im Vorbild Vertretungs-Abrechnung, Entfalltage, Vertretungs-Übersicht, danach die weiteren Berichte
        // (Zeitraum vorbelegt: Monatsbeginn bis Datum, höchstens bis Schuljahresende)
        foreach (var n in StatistikBerichte.Namen)
            res.Add(new(Vertretungsplanung, n, $"Bericht „{n}“ (Auswahl von Lehrkräften und Zeitraum).", f => StatistikBerichte.Starten(f, n, f.BerichtVon, f.BerichtBis)));
        res.AddRange(new Liste[]
        {
            new(Vertretungsplanung, "Vertretungsplan Klassen (Druck)", "Vertretungsplan des Kalendertages für Schülerinnen und Schüler (Seitenlayout mit Vorschau).", _ => h.VertretungsplanDrucken(false)),
            new(Vertretungsplanung, "Vertretungsplan Lehrer (Druck)", "Vertretungsplan des Kalendertages für Lehrkräfte (Seitenlayout mit Vorschau).", _ => h.VertretungsplanDrucken(true)),
            new(Vertretungsplanung, "Tageslisten", "Alle Vertretungsformate mit dem Kennzeichen „Tagesliste“ für das Datum des Kalenders.", _ => h.TageslistenDrucken()),
            new(Vertretungsplanung, "Vertretungsplan Schüler (HTML, 2 Tage)", "Schülerversion des Vertretungsplans ab dem gewählten Datum in der Berichtsansicht.", f => VertretungenFenster.HtmlAusgabe(f.Datum, 2, false, true)),
            new(Vertretungsplanung, "Vertretungsplan Lehrer (HTML, 2 Tage)", "Lehrerversion des Vertretungsplans ab dem gewählten Datum in der Berichtsansicht.", f => VertretungenFenster.HtmlAusgabe(f.Datum, 2, true, true)),
            new(Vertretungsplanung, "Vertretungsstatistik", "Statistik der Vertretungen und Entfälle mit Druck und Berichten.", _ => h.VertretungsFensterOeffnen("stat")),
            // Klausurpläne
            new(Klausuren, Listenberichte.KlausurenTitel, "Alle Klausuren des Schuljahres mit Datum, Stunden, Klassen, Aufsichten und Räumen.", _ => Listenberichte.KlausurenListe()),
            new(Klausuren, "Klausuren (Fenster)", "Klausurenfenster zum Bearbeiten und Ausgeben.", _ => h.FensterOeffnen("klaus")),
        });
        return res;
    }
}

/// <summary>HTML-Listen der Listenauswahl (Ausgabe in der Berichtsansicht).</summary>
public static class Listenberichte
{
    public const string FreieStundenTitel = "Freie Stunden";
    public const string FachStundenTitel = "Fach-Stunden-Liste";
    public const string PausenaufsichtenTitel = "Pausenaufsichtsliste";
    public const string KlausurenTitel = "Klausurenliste";
    public const string BelegungTitel = "Belegungs-Statistik";
    public const string StundenListeTitel = "Stunden-Liste (Lehrer)";

    private static string H(string? s) => StatistikBerichte.H(s);

    /// <summary>HTML der Liste „Freie Stunden“ für die Woche von <paramref name="datum"/>.</summary>
    public static string FreieStundenHtml(Schuldatei d, DateOnly datum)
    {
        var liste = Berichtslisten.FreieStunden(d, datum);
        var montag = Kalender.Montag(datum);
        var sb = new StringBuilder($"<h1>{FreieStundenTitel}: Woche {Format.Datum(montag)} bis {Format.Datum(montag.AddDays(6))}</h1>");
        if (liste.Count == 0) return sb.Append("<p>In dieser Woche liegen keine Unterrichtstage.</p>").ToString();
        foreach (var tag in liste.GroupBy(z => z.Datum))
        {
            var periode = d.PeriodeAm(tag.Key);
            sb.Append($"<div class=\"el\"><h2>{H(Planungsdienst.TageLang[((int)tag.Key.DayOfWeek + 6) % 7])}, {Format.Datum(tag.Key)}, {H(periode.Name)}</h2>");
            sb.Append("<table class=\"g\"><tr><th>Stunde</th><th class=\"z\">Anz.</th><th>Freie Lehrkräfte</th><th class=\"z\">Anz.</th><th>Freie Räume</th></tr>");
            foreach (var z in tag)
                sb.Append($"<tr><td>{H(d.Zeitraster.StundenLabel(z.Stunde))}.</td><td class=\"z\">{z.Lehrer.Count}</td><td>{H(string.Join(", ", z.Lehrer.Select(l => l.Kurzname)))}</td>" +
                          $"<td class=\"z\">{z.Raeume.Count}</td><td>{H(string.Join(", ", z.Raeume.Select(r => r.Kurzname)))}</td></tr>");
            sb.Append("</table></div>");
        }
        return sb.ToString();
    }

    /// <summary>HTML der „Belegungs-Statistik“ für die Woche von <paramref name="datum"/>.</summary>
    public static string BelegungHtml(Schuldatei d, DateOnly datum)
    {
        var liste = Berichtslisten.Belegung(d, datum);
        var montag = Kalender.Montag(datum);
        var sb = new StringBuilder($"<h1>{BelegungTitel}: Woche {Format.Datum(montag)} bis {Format.Datum(montag.AddDays(6))}</h1>");
        if (liste.Count == 0) return sb.Append("<p>In dieser Woche liegen keine Unterrichtstage.</p>").ToString();
        string P(double p) => p.ToString("0", Format.De) + " %";
        foreach (var tag in liste.GroupBy(z => z.Datum))
        {
            sb.Append($"<div class=\"el\"><h2>{H(Planungsdienst.TageLang[((int)tag.Key.DayOfWeek + 6) % 7])}, {Format.Datum(tag.Key)}</h2>");
            sb.Append("<table class=\"g\"><tr><th>Stunde</th><th class=\"z\">Lehrer belegt</th><th class=\"z\">von</th><th class=\"z\">%</th><th class=\"z\">Räume belegt</th><th class=\"z\">von</th><th class=\"z\">%</th></tr>");
            foreach (var z in tag)
                sb.Append($"<tr><td>{H(d.Zeitraster.StundenLabel(z.Stunde))}.</td><td class=\"z\">{z.LehrerBelegt}</td><td class=\"z\">{z.LehrerGesamt}</td><td class=\"z\">{P(z.LehrerProzent)}</td>" +
                          $"<td class=\"z\">{z.RaeumeBelegt}</td><td class=\"z\">{z.RaeumeGesamt}</td><td class=\"z\">{P(z.RaeumeProzent)}</td></tr>");
            sb.Append($"<tr class=\"s\"><td>Summe</td><td class=\"z\">{tag.Sum(z => z.LehrerBelegt)}</td><td></td><td></td><td class=\"z\">{tag.Sum(z => z.RaeumeBelegt)}</td><td></td><td></td></tr></table></div>");
        }
        return sb.ToString();
    }

    public static void Belegung(DateOnly datum)
    {
        string body;
        using (new WarteCursor()) body = BelegungHtml(AppZustand.D, datum);
        StatistikBerichte.Oeffnen(BelegungTitel, body);
    }

    /// <summary>HTML der „Stunden-Liste“: je Lehrkraft die Stunden der Woche von <paramref name="datum"/>.</summary>
    public static string StundenListeHtml(Schuldatei d, DateOnly datum)
    {
        var liste = Berichtslisten.StundenListe(d, datum);
        var montag = Kalender.Montag(datum);
        var sb = new StringBuilder($"<h1>{StundenListeTitel}: Woche {Format.Datum(montag)} bis {Format.Datum(montag.AddDays(6))}</h1>");
        if (liste.Count == 0) return sb.Append("<p>In dieser Woche liegen keine Stunden.</p>").ToString();
        foreach (var (l, stunden) in liste)
        {
            sb.Append($"<div class=\"el\"><div><span class=\"kn\" style=\"font-size:14pt\">{H(l.Kurzname)}</span><span class=\"ln\">{H(l.VollerName)}</span></div>");
            sb.Append("<table class=\"r\"><tr><th>Datum</th><th>Tag</th><th>Stunde</th><th>Klasse(n)</th><th>Fach</th><th>Raum</th></tr>");
            foreach (var z in stunden)
                sb.Append($"<tr><td>{z.Datum.Day}.{z.Datum.Month}.</td><td>{Planungsdienst.TageKurz[((int)z.Datum.DayOfWeek + 6) % 7]}</td><td>{H(d.Zeitraster.StundenLabel(z.Stunde))}</td>" +
                          $"<td>{H(z.Klassen)}</td><td>{H(z.Fach)}</td><td>{H(z.Raum)}</td></tr>");
            sb.Append($"<tr class=\"s\"><td colspan=\"6\">Summe: {stunden.Count} Stunden</td></tr></table></div>");
        }
        return sb.ToString();
    }

    public static void StundenListe(DateOnly datum)
    {
        string body;
        using (new WarteCursor()) body = StundenListeHtml(AppZustand.D, datum);
        StatistikBerichte.Oeffnen(StundenListeTitel, body);
    }

    public static void FreieStunden(DateOnly datum)
    {
        string body;
        using (new WarteCursor()) body = FreieStundenHtml(AppZustand.D, datum);
        StatistikBerichte.Oeffnen(FreieStundenTitel, body);
    }

    /// <summary>HTML der „Fach-Stunden-Liste“: je Fach Wochenstunden je Klasse und Lehrkraft, Summen je Klasse, je Lehrkraft und gesamt.</summary>
    public static string FachStundenHtml(Schuldatei d)
    {
        var bloecke = Berichtslisten.FachStunden(d);
        var sb = new StringBuilder($"<h1>{FachStundenTitel}: {H(d.AktivePeriode.Name)}</h1>");
        if (bloecke.Count == 0) return sb.Append("<p>Kein Unterricht vorhanden.</p>").ToString();
        foreach (var b in bloecke)
        {
            sb.Append($"<div class=\"el\"><h2>{H(b.Fach.Kurzname)}{(b.Fach.Langname.Length > 0 ? " (" + H(b.Fach.Langname) + ")" : "")}</h2>");
            sb.Append("<table class=\"r\"><tr><th>Klasse(n)</th><th>Lehrkraft</th><th class=\"z\">Wochenstunden</th></tr>");
            foreach (var z in b.Zeilen) sb.Append($"<tr><td>{H(z.Klassen)}</td><td>{H(z.Lehrer.Length > 0 ? z.Lehrer : "---")}</td><td class=\"z\">{z.Stunden}</td></tr>");
            sb.Append($"<tr class=\"s\"><td>Summe</td><td></td><td class=\"z\">{b.Summe}</td></tr></table>");
            sb.Append("<div style=\"margin-top:4px\">Je Klasse: ").Append(H(string.Join(", ", b.JeKlasse.Select(k => $"{k.Klasse} {k.Stunden}"))))
              .Append("<br>Je Lehrkraft: ").Append(H(string.Join(", ", b.JeLehrer.Select(l => $"{l.Lehrer} {l.Stunden}")))).Append("</div></div>");
        }
        sb.Append($"<div class=\"bil\">Gesamt: {bloecke.Sum(b => b.Summe)} Lehrer-Wochenstunden in {bloecke.Count} Fächern</div>");
        return sb.ToString();
    }

    public static void FachStunden()
    {
        string body;
        using (new WarteCursor()) body = FachStundenHtml(AppZustand.D);
        StatistikBerichte.Oeffnen(FachStundenTitel, body);
    }

    /// <summary>HTML der Pausenaufsichtsliste: je Lehrkraft die regulären Aufsichten der aktiven Periode (Tag, Pause, Gang).</summary>
    public static string PausenaufsichtenHtml(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var aufsichten = d.Pausenaufsichten.Where(a => a.Datum is null).ToList();
        var sb = new StringBuilder($"<h1>{PausenaufsichtenTitel}: {H(d.AktivePeriode.Name)}</h1>");
        if (aufsichten.Count == 0) return sb.Append("<p>Keine Pausenaufsichten eingetragen.</p>").ToString();
        string Pause(int nach) => d.Zeitraster.Pausen.FirstOrDefault(p => p.NachStunde == nach) is { Bezeichnung.Length: > 0 } p ? p.Bezeichnung : $"nach {nach}. Std.";
        sb.Append("<table class=\"g\"><tr><th>Lehrkraft</th><th>Name</th><th class=\"z\">Anzahl</th><th>Aufsichten (Tag / Pause / Gang)</th></tr>");
        foreach (var l in Stammdaten.Sortiert(d.Lehrer))
        {
            var eigene = aufsichten.Where(a => a.LehrerId == l.Id).OrderBy(a => a.Tag).ThenBy(a => a.NachStunde).ToList();
            if (eigene.Count == 0) continue;
            sb.Append($"<tr><td>{H(l.Kurzname)}</td><td>{H(l.VollerName)}</td><td class=\"z\">{eigene.Count}</td><td>{H(string.Join("; ", eigene.Select(a => $"{Planungsdienst.Tagname(d, a.Tag)} {Pause(a.NachStunde)} {ix.K(ElementTyp.Gang, a.GangId)}")))}</td></tr>");
        }
        sb.Append($"</table><div class=\"bil\">Gesamt: {aufsichten.Count} Aufsichten</div>");
        return sb.ToString();
    }

    public static void PausenaufsichtenListe() => StatistikBerichte.Oeffnen(PausenaufsichtenTitel, PausenaufsichtenHtml(AppZustand.D));

    /// <summary>HTML der Klausurenliste des Schuljahres.</summary>
    public static string KlausurenHtml(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var liste = d.Klausuren.Where(k => k.Datum >= d.Einstellungen.SchuljahrVon && k.Datum <= d.Einstellungen.SchuljahrBis).OrderBy(k => k.Datum).ThenBy(k => k.StundeVon).ToList();
        var sb = new StringBuilder($"<h1>{KlausurenTitel} {d.Einstellungen.SchuljahrVon.Year}/{d.Einstellungen.SchuljahrBis.Year}</h1>");
        if (liste.Count == 0) return sb.Append("<p>Keine Klausuren im Schuljahr.</p>").ToString();
        sb.Append("<table class=\"g\"><tr><th>Datum</th><th>Tag</th><th>Stunde(n)</th><th>Name</th><th>Klasse(n)</th><th>Aufsicht</th><th>Raum</th><th>Text</th></tr>");
        foreach (var k in liste)
        {
            var std = k.StundeBis > k.StundeVon ? $"{k.StundeVon}.–{k.StundeBis}." : $"{k.StundeVon}.";
            sb.Append($"<tr{(k.Aktiv ? "" : " style=\"color:#888\"")}><td>{Format.Datum(k.Datum)}</td><td>{Planungsdienst.TageKurz[((int)k.Datum.DayOfWeek + 6) % 7]}</td><td>{std}</td><td>{H(k.Name)}</td>" +
                      $"<td>{H(ix.KKlassen(k.KlassenIds))}</td><td>{H(string.Join(",", k.LehrerIds.Select(ix.KLehrer)))}</td><td>{H(string.Join(",", k.RaumIds.Select(ix.KRaum)))}</td><td>{H(k.Text)}</td></tr>");
        }
        sb.Append($"</table><div class=\"bil\">Gesamt: {liste.Count} Klausuren</div>");
        return sb.ToString();
    }

    public static void KlausurenListe() => StatistikBerichte.Oeffnen(KlausurenTitel, KlausurenHtml(AppZustand.D));
}
