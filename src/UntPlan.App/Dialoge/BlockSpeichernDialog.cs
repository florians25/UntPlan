using UntPlan.Core.Planung;

namespace UntPlan.App.Dialoge;

/// <summary>
/// Rückfrage „Block speichern - Unterricht: 35 Mus Callas“ beim Ablegen einer Stunde auf ein belegtes Feld (Handbuch Stundenplan
/// Kap. 5.2.5 und 5.2.8): Meldung mit Quelle und Ziel, behindernden Unterrichten und Elementen, darunter die Gruppe „Optionen“ mit
/// „Block speichern (behindernder Unterricht wird entplant)“, „Kollision erzeugen“ und – falls möglich – „2-er-Tausch: …“ und
/// „Tauschkette: …“, links unten „Speichern mit Raum-Kollision“ (nur aktiv, wenn am Ziel ein Raum belegt ist), rechts Ok/Abbrechen.
/// Maße wie in der Handbuch-Abbildung (auf 96 dpi zurückgerechnet): Fenster 572 × 238, Gruppe 549 × 93, Schaltflächen 82 × 23.
/// Im Selbsttest beantwortet <see cref="Ui.FrageAntwort"/> die Frage ohne Fenster (true = Block speichern, false = Kollision erzeugen).
/// </summary>
public static class BlockSpeichernDialog
{
    /// <summary>Ergebnis: gewählte Option und „Speichern mit Raum-Kollision“.</summary>
    public sealed record Antwort(BlockSpeichernWahl Wahl, bool RaumKollision);

    /// <summary>Für den Selbsttest: Titel, Meldung und Optionen der zuletzt gestellten Frage.</summary>
    internal static (string Titel, string Text, IReadOnlyList<string> Optionen)? Zuletzt { get; private set; }

    /// <param name="tausch">Text der Option „2-er-Tausch: …“ (null = kein Tausch möglich).</param>
    /// <param name="kette">Text der Option „Tauschkette: …“ (null = keine Tauschkette).</param>
    /// <param name="raumBelegt">Am Ziel ist ein Raum belegt: „Speichern mit Raum-Kollision“ wählbar.</param>
    public static Antwort? Fragen(IWin32Window? owner, string titel, string text, string? tausch = null, string? kette = null, bool raumBelegt = false)
    {
        var optionen = new List<(string Text, BlockSpeichernWahl Wahl)>
        {
            (Ziehbewertung.OptionBlockSpeichern, BlockSpeichernWahl.BlockSpeichern),
            (Ziehbewertung.OptionKollision, BlockSpeichernWahl.KollisionErzeugen),
        };
        if (tausch != null) optionen.Add((tausch, BlockSpeichernWahl.Tausch));
        if (kette != null) optionen.Add((kette, BlockSpeichernWahl.Tauschkette));
        Zuletzt = (titel, text, optionen.Select(o => o.Text).ToList());
        if (Ui.FrageAntwort is { } hook)
            return new Antwort(hook(titel + "\n" + text, titel) ? BlockSpeichernWahl.BlockSpeichern : BlockSpeichernWahl.KollisionErzeugen, false);

        // wie im Handbuch vorgewählt: Tauschkette, sonst 2-er-Tausch, sonst „Block speichern“
        var vorgabe = optionen.Count - 1;
        if (tausch is null && kette is null) vorgabe = 0;
        var schrift = VorbildMasse.Schrift;
        var mehrOptionen = Math.Max(0, optionen.Count - 4) * 18;
        var meldung = text.Replace("\n", Environment.NewLine);
        var textHoehe = TextRenderer.MeasureText(meldung, schrift, new Size(548, 0), TextFormatFlags.WordBreak).Height;
        var mehrText = Math.Max(0, textHoehe - 95);
        using var d = new Dialog(titel, 572, 238 + mehrText + mehrOptionen) { Font = schrift };
        d.Ok.Text = "Ok";
        d.Ok.Size = d.Abbrechen.Size = new Size(82, 23);
        d.Ok.Location = new Point(389, 211 + mehrText + mehrOptionen);
        d.Abbrechen.Location = new Point(486, 211 + mehrText + mehrOptionen);
        d.Controls.Add(new Label { Text = meldung, Left = 12, Top = 12, Width = 548, Height = 95 + mehrText, AutoSize = false });
        var grp = new GroupBox { Text = "Optionen", Left = 13, Top = 113 + mehrText, Width = 549, Height = 93 + mehrOptionen };
        var knoepfe = new List<RadioButton>();
        for (var i = 0; i < optionen.Count; i++)
        {
            var r = new RadioButton { Text = optionen[i].Text, Left = 16, Top = 21 + i * 18, Width = 525, Height = 18, Checked = i == vorgabe, AutoEllipsis = true };
            grp.Controls.Add(r);
            knoepfe.Add(r);
        }
        d.Controls.Add(grp);
        var raum = new CheckBox { Text = Ziehbewertung.RaumKollision, Left = 13, Top = 213 + mehrText + mehrOptionen, AutoSize = true, Enabled = raumBelegt };
        d.Controls.Add(raum);
        Warnhinweis.NebenDerMaus(d);
        if (d.ShowDialog(owner) != DialogResult.OK) return null;
        var idx = Math.Max(0, knoepfe.FindIndex(k => k.Checked));
        return new Antwort(optionen[idx].Wahl, raum.Enabled && raum.Checked);
    }
}
