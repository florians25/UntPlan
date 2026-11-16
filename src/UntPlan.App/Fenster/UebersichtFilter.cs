using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App.Fenster;

/// <summary>
/// Handbuch 6.4.2.1–6.4.2.4: Filter der Übersichtspläne (Auswahlliste in der Symbolleiste, „Lehrer der Klasse“, „Lehrer des Faches“,
/// eigene Elementauswahl als speicherbarer Filter) sowie Wochenstunden (Lehrer) bzw. Stammraum (Klassen) in der Zeilenbeschriftung.
/// </summary>
public sealed partial class UebersichtFenster
{
    /// <summary>Angezeigte Elemente (null = alle).</summary>
    private HashSet<int>? _auswahl;
    private ToolStripDropDownButton? _filterKnopf;

    private Benutzereinstellungen E => AppZustand.Einstellungen;
    private string FilterSchluessel => _typ.ToString();

    private bool ZusatzBeschriftung => _typ == ElementTyp.Lehrer && E.UebersichtWochenstunden || _typ == ElementTyp.Klasse && E.UebersichtStammraum;

    private List<Element> Angezeigt() => Stammdaten.Sortiert(AppZustand.D, _typ).Where(e => _auswahl is null || _auswahl.Contains(e.Id)).ToList();

    private string Beschriftung(Element e) =>
        Uebersichtsplan.Zeilenbeschriftung(AppZustand.Doc.Index, _typ, e, E.UebersichtWochenstunden, E.UebersichtStammraum);

    private void FilterLeisteAufbauen(ToolStrip ts)
    {
        ts.Items.Add(new ToolStripSeparator());
        _filterKnopf = new ToolStripDropDownButton("Auswahl: alle") { ToolTipText = "Elemente des Übersichtsplans filtern (Auswahlliste)" };
        _filterKnopf.DropDownOpening += (_, _) => FilterMenue();
        _filterKnopf.DropDownItems.Add("…");
        ts.Items.Add(_filterKnopf);
        if (_typ == ElementTyp.Lehrer)
        {
            var w = new ToolStripButton("Wochenstunden") { CheckOnClick = true, Checked = E.UebersichtWochenstunden, ToolTipText = "Verplante Wochenstunden / noch zu verplanende Stunden in der Zeile anzeigen" };
            w.CheckedChanged += (_, _) => { E.UebersichtWochenstunden = w.Checked; E.Speichern(); _raster.Invalidate(); };
            ts.Items.Add(w);
        }
        if (_typ == ElementTyp.Klasse)
        {
            var r = new ToolStripButton("Stammraum") { CheckOnClick = true, Checked = E.UebersichtStammraum, ToolTipText = "Stammraum der Klasse in der Zeile anzeigen" };
            r.CheckedChanged += (_, _) => { E.UebersichtStammraum = r.Checked; E.Speichern(); _raster.Invalidate(); };
            ts.Items.Add(r);
        }
    }

    private void FilterSetzen(HashSet<int>? auswahl, string text)
    {
        _auswahl = auswahl;
        if (_filterKnopf != null) _filterKnopf.Text = "Auswahl: " + text;
        _raster.GroesseAnpassen(_raster.Parent?.ClientSize ?? ClientSize);
    }

    private void FilterMenue()
    {
        if (_filterKnopf is null) return;
        var d = AppZustand.D;
        var m = _filterKnopf.DropDownItems;
        m.Clear();
        m.Add("Alle", null, (_, _) => FilterSetzen(null, "alle"));
        m.Add("Elemente auswählen…", null, (_, _) => ElementeWaehlen());
        if (_typ == ElementTyp.Lehrer)
        {
            var kl = new ToolStripMenuItem("Lehrer der Klasse");
            foreach (var k in Stammdaten.Sortiert(d, ElementTyp.Klasse))
            {
                var id = k.Id;
                kl.DropDownItems.Add(k.Kurzname, null, (_, _) => FilterSetzen(Uebersichtsplan.LehrerDerKlasse(d, id), "Lehrer der Klasse " + k.Kurzname));
            }
            var fa = new ToolStripMenuItem("Lehrer des Faches");
            foreach (var f in Stammdaten.Sortiert(d, ElementTyp.Fach))
            {
                var id = f.Id;
                fa.DropDownItems.Add(f.Kurzname, null, (_, _) => FilterSetzen(Uebersichtsplan.LehrerDesFaches(d, id), "Lehrer des Faches " + f.Kurzname));
            }
            m.Add(kl);
            m.Add(fa);
        }
        var gespeichert = E.Uebersichtsfilter.GetValueOrDefault(FilterSchluessel);
        if (gespeichert is { Count: > 0 })
        {
            m.Add(new ToolStripSeparator());
            foreach (var (name, liste) in gespeichert.OrderBy(x => x.Key))
            {
                var (n, l) = (name, liste);
                m.Add(n, null, (_, _) => FilterSetzen(Uebersichtsplan.FilterAufloesen(Stammdaten.Sortiert(d, _typ), l).ToHashSet(), n));
            }
        }
        m.Add(new ToolStripSeparator());
        var speichern = m.Add("Auswahl als Filter speichern…", null, (_, _) => FilterSpeichern());
        speichern.Enabled = _auswahl != null;
        var loeschen = new ToolStripMenuItem("Filter löschen") { Enabled = gespeichert is { Count: > 0 } };
        foreach (var name in gespeichert?.Keys.OrderBy(x => x) ?? Enumerable.Empty<string>())
        {
            var n = name;
            loeschen.DropDownItems.Add(n, null, (_, _) => { gespeichert!.Remove(n); E.Speichern(); });
        }
        m.Add(loeschen);
    }

    private void ElementeWaehlen()
    {
        using var dlg = new Dialoge.Dialog("Elemente auswählen", 300, 420);
        var liste = new CheckedListBox { Left = 8, Top = 8, Width = 284, Height = 340, CheckOnClick = true, IntegralHeight = false };
        var alle = Stammdaten.Sortiert(AppZustand.D, _typ).ToList();
        foreach (var e in alle) liste.Items.Add(e.Kurzname + (e.Langname.Length > 0 ? " – " + e.Langname : ""), _auswahl is null || _auswahl.Contains(e.Id));
        var inv = new Button { Text = "Inverse", Left = 8, Top = 356, Width = 80, Height = 26 };
        inv.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, !liste.GetItemChecked(i)); };
        dlg.Controls.AddRange(new Control[] { liste, inv });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var ids = liste.CheckedIndices.Cast<int>().Select(i => alle[i].Id).ToHashSet();
        FilterSetzen(ids.Count == alle.Count ? null : ids, ids.Count == alle.Count ? "alle" : $"{ids.Count} Elemente");
    }

    private void FilterSpeichern()
    {
        if (_auswahl is null) return;
        var name = Ui.Eingabe("Name des Filters:", "Filter speichern", _filterKnopf?.Text.Replace("Auswahl: ", "") ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var namen = Angezeigt().Select(e => e.Kurzname);
        if (!E.Uebersichtsfilter.TryGetValue(FilterSchluessel, out var liste)) E.Uebersichtsfilter[FilterSchluessel] = liste = new();
        liste[name.Trim()] = string.Join(",", namen);
        E.Speichern();
        FilterSetzen(_auswahl, name.Trim());
    }
}
