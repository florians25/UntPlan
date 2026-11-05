using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Vorbild „Lehrer zuordnen“ (Lehrer im Unterricht ändern): links Unterricht und „Auswahl derzeitiger Unterricht“ (Lehrer, Fach),
/// rechts „Mögliche Lehrer“ mit Wo.St., Nvp., hat Fach, Lehrbefähigt und Kollisionen (Anz, Unt., Zeit); „Auch Lehrer mit Kollisionen
/// anzeigen“; Schaltflächen „Le. zuordnen“, „Lehr. löschen“, „Schließen“.
/// </summary>
public static class LehrerZuordnenDialog
{
    private static bool _mitKollisionen;

    /// <summary>Ergebnis: true, wenn ein Lehrer geändert wurde.</summary>
    public static bool Zeigen(IWin32Window owner, int unterrichtId, bool modal = true)
    {
        var doc = AppZustand.Doc;
        var d = new Dialog("Lehrer zuordnen", 900, 470) { FormBorderStyle = FormBorderStyle.Sizable, MinimumSize = new Size(760, 380) };
        d.Ok.Visible = false;
        d.Abbrechen.Text = "Schließen";
        var geaendert = false;

        var info = new Label { Left = 8, Top = 8, Width = 200, Height = 46, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(3) };
        var lehrerText = new Label { Left = 8, Top = 66, Width = 220, Height = 34 };
        d.Controls.Add(new Label { Text = "Auswahl derzeitiger Unterricht:", Left = 8, Top = 104, AutoSize = true });
        var links = new DataGridView { Left = 8, Top = 122, Width = 220, Height = 290, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
        Ui.RasterGestalten(links, false);
        links.RowHeadersVisible = false;
        links.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        links.MultiSelect = false;
        links.Columns.Add("L", "Lehrer");
        links.Columns.Add("F", "Fach");
        var pfeil = new Button { Text = "←", Left = 236, Top = 180, Width = 34, Height = 34, Font = new Font(Ui.Schrift.FontFamily, 13, FontStyle.Bold) };
        var mitKoll = new CheckBox { Text = "Auch Lehrer mit Kollisionen anzeigen", Left = 280, Top = 56, AutoSize = true, Checked = _mitKollisionen };
        d.Controls.Add(new Label { Text = "Mögliche Lehrer:", Left = 280, Top = 104, AutoSize = true });
        var rechts = new DataGridView { Left = 280, Top = 122, Width = 610, Height = 290, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        Ui.RasterGestalten(rechts, false);
        rechts.RowHeadersVisible = false;
        rechts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rechts.MultiSelect = false;
        foreach (var (n, t, w) in new[] { ("L", "Lehrer", 80), ("W", "Wo.St.", 54), ("N", "Nvp.", 44), ("H", "hat Fach", 70), ("B", "Lehrbefähigt", 84), ("KA", "Anz", 40), ("KU", "Unt.", 70), ("KZ", "Zeit", 110) })
        {
            var c = rechts.Columns[rechts.Columns.Add(n, t)];
            c.Width = w;
            if (n is "W" or "N" or "KA" or "KU") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (n is "H" or "B") c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        rechts.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value as string != "✔" || e.Graphics is null) return;
            Ui.ZellHaken(e, Color.ForestGreen, 2.2f);
        };
        var zuordnen = new Button { Text = "✔  Le. zuordnen", Width = 110, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var loeschen = new Button { Text = "✖  Lehr. löschen", Width = 110, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        d.Controls.AddRange(new Control[] { info, lehrerText, links, pfeil, mitKoll, rechts, zuordnen, loeschen });
        void Knoepfe()
        {
            d.Abbrechen.Location = new Point(d.ClientSize.Width - 96, d.ClientSize.Height - 36);
            loeschen.Location = new Point(d.Abbrechen.Left - 116, d.Abbrechen.Top);
            zuordnen.Location = new Point(loeschen.Left - 116, d.Abbrechen.Top);
        }
        d.Resize += (_, _) => Knoepfe();
        Knoepfe();

        Unterricht? U() => doc.Index.Unterrichte.GetValueOrDefault(unterrichtId);
        int Zeile() => Math.Max(0, links.CurrentRow?.Index ?? 0);

        void Links()
        {
            var u = U();
            if (u is null) return;
            var alt = Zeile();
            var ix = doc.Index;
            links.Rows.Clear();
            foreach (var z in u.Zeilen) links.Rows.Add(ix.KLehrer(z.LehrerId), ix.KFach(z.FachId));
            if (links.Rows.Count > 0) links.CurrentCell = links.Rows[Math.Min(alt, links.Rows.Count - 1)].Cells[0];
            info.Text = $"Unterricht: {u.Id}";
        }

        void Rechts()
        {
            var u = U();
            if (u is null || u.Zeilen.Count == 0) return;
            var ix = doc.Index;
            var dd = doc.Daten;
            var z = u.Zeilen[Math.Min(Zeile(), u.Zeilen.Count - 1)];
            var akt = z.LehrerId is int al ? ix.Lehrer.GetValueOrDefault(al) : null;
            lehrerText.Text = akt is null ? "" : $"{akt.Kurzname}, {akt.VollerName}\n{akt.Status}";
            var stunden = dd.Stundenplan.Where(p => p.UnterrichtId == u.Id).ToList();
            rechts.Rows.Clear();
            var zeilen = new List<(Lehrer L, int Wst, int Nvp, bool HatFach, bool Befaehigt, List<Planstunde> Koll)>();
            foreach (var l in Stammdaten.Sortiert(dd, ElementTyp.Lehrer).OfType<Lehrer>())
            {
                if (l.Id == z.LehrerId || l.Ignorieren) continue;
                var plan = ix.PlanVon(ElementTyp.Lehrer, l.Id).Where(p => p.UnterrichtId != u.Id).ToList();
                var koll = plan.Where(p => stunden.Any(s => s.Tag == p.Tag && s.Stunde == p.Stunde)).ToList();
                if (koll.Count > 0 && !mitKoll.Checked) continue;
                var unts = ix.UnterrichteVon(ElementTyp.Lehrer, l.Id).Where(x => !x.Ignorieren).ToList();
                zeilen.Add((l, unts.Sum(x => x.Wochenstunden), unts.Sum(x => ix.NichtVerplant(x)),
                    z.FachId is int fa && unts.Any(x => x.Zeilen.Any(k => k.LehrerId == l.Id && k.FachId == fa)),
                    z.FachId is int fb && l.FaecherIds.Contains(fb), koll));
            }
            // Vorbild: zuerst kollisionsfreie Lehrer mit Lehrbefähigung bzw. Fach, dann nach Zahl der Kollisionen
            foreach (var x in zeilen.OrderBy(x => x.Koll.Count).ThenByDescending(x => x.Befaehigt).ThenByDescending(x => x.HatFach))
            {
                var r = rechts.Rows.Add(x.L.Kurzname, x.Wst, x.Nvp, x.HatFach ? "✔" : "", x.Befaehigt ? "✔" : "",
                    x.Koll.Count > 0 ? x.Koll.Count : "", string.Join(", ", x.Koll.Select(k => k.UnterrichtId)),
                    string.Join(", ", x.Koll.Select(k => $"{Planungsdienst.Tagname(dd, k.Tag)}-{k.Stunde}").Distinct()));
                rechts.Rows[r].Tag = x.L.Id;
                rechts.Rows[r].Cells[0].Style.BackColor = Ui.Erste;
            }
        }

        void Setzen(int? lehrer)
        {
            var u = U();
            if (u is null || u.Zeilen.Count == 0) return;
            var zeile = Math.Min(Zeile(), u.Zeilen.Count - 1);
            var id = u.Id;
            doc.Aendern(lehrer is null ? "Lehrer löschen" : $"Lehrer {doc.Index.KLehrer(lehrer)} zuordnen", AenderungsArt.Unterricht, dd =>
            {
                var uu = dd.Unterrichte.First(x => x.Id == id);
                uu.Zeilen[zeile].LehrerId = lehrer;
            });
            geaendert = true;
            Links();
            Rechts();
        }

        void Zuordnen()
        {
            if (rechts.CurrentRow?.Tag is not int l) return;
            var koll = rechts.CurrentRow.Cells["KA"].Value is int n && n > 0;
            if (koll && !Ui.Frage("Der Lehrer hat in diesen Stunden bereits Unterricht. Trotzdem zuordnen (Kollision)?")) return;
            Setzen(l);
        }

        links.SelectionChanged += (_, _) => Rechts();
        mitKoll.CheckedChanged += (_, _) => { _mitKollisionen = mitKoll.Checked; Rechts(); };
        rechts.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Zuordnen(); };
        pfeil.Click += (_, _) => Zuordnen();
        zuordnen.Click += (_, _) => Zuordnen();
        loeschen.Click += (_, _) => Setzen(null);
        Links();
        Rechts();
        if (!modal)
        {
            d.Show(owner);
            return false;
        }
        using (d) d.ShowDialog(owner);
        return geaendert;
    }
}
