using System.Globalization;
using System.Text;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Berichte des Klassenbuchs im Aussehen des Originals: Serifenschrift, rechts oben Schule und Schuljahr, Titel, darunter der Zeitraum,
/// durchgehende Linie, Kopfzeilen des Berichts (z. B. „Klasse: 7a, Fach: M, Lehrkraft: BEC“), Tabelle mit grau hinterlegtem Kopf,
/// Spaltengruppen (z. B. „Fehlstunden“ über „Schulst“ und „Std.“), Gitterlinien, linksbündigen Werten, Kopfblöcken je Schüler*in
/// („Schüler*innen“, „Text“, „Klasse“) mit Seitenumbruch und Summenzeilen, deren Werte einzeln umrandet sind.
/// </summary>
public static partial class PdfBericht
{
    private const float OSchrift = 9f, OZeile = 11f;

    /// <param name="schuljahr">z. B. „2025/2026“ (rechts oben „Schuljahr : 2025/2026“).</param>
    /// <param name="zeitraum">z. B. „01.09.2025 - 17.11.2025“ unter dem Titel.</param>
    /// <param name="arten">je Zeile: <c>""</c>, <c>block</c> (Kopfblock „Bezeichnung\tWert“ je Zeile), <c>gruppe</c> (Überschrift wie „Klasse: 7a“),
    /// <c>zwischensumme</c>, <c>summe</c> (fette Beschriftung).</param>
    /// <param name="ohneSeitenumbrueche">„ohne Seitenumbrüche“: Blöcke folgen ohne neue Seite aufeinander.</param>
    public static byte[] Original(string schule, string schuljahr, string titel, string zeitraum, IReadOnlyList<string> spalten,
        IReadOnlyList<IReadOnlyList<string>> zeilen, IReadOnlyList<string>? arten, Berichtsdarstellung? d, bool hochformat, bool ohneSeitenumbrueche = false)
    {
        var alt = (_hoch, _serif);
        _hoch = hochformat;
        _serif = true;
        try { return OriginalErzeugen(schule, schuljahr, titel, zeitraum, spalten, zeilen, arten, d ?? new Berichtsdarstellung(), ohneSeitenumbrueche); }
        finally { (_hoch, _serif) = alt; }
    }

    private static byte[] OriginalErzeugen(string schule, string schuljahr, string titel, string zeitraum, IReadOnlyList<string> spalten,
        IReadOnlyList<IReadOnlyList<string>> zeilen, IReadOnlyList<string>? arten, Berichtsdarstellung d, bool ohneSeitenumbrueche)
    {
        const float rand = 34f;
        string Art(int r) => arten != null && r < arten.Count ? arten[r] : "";
        string Zelle(int r, int i) => i < zeilen[r].Count ? zeilen[r][i] ?? "" : "";
        var n = spalten.Count;
        var nutz = Breite - 2 * rand;
        var koepfe = Enumerable.Range(0, n).Select(i => d.Koepfe != null && i < d.Koepfe.Count ? d.Koepfe[i] : spalten[i]).ToList();
        var gruppen = Enumerable.Range(0, n).Select(i => d.Gruppen != null && i < d.Gruppen.Count ? d.Gruppen[i] ?? "" : "").ToList();
        var grau = (d.Grau ?? new List<int>()).ToHashSet();
        bool Senkrecht(int i) => d.SenkrechtAb >= 0 && i >= d.SenkrechtAb;

        // Spaltenbreiten: längste Zeile des Inhalts (Datenzeilen) bzw. des Kopfes, höchstens 38 % der Nutzbreite, auf die Nutzbreite gebracht
        var daten = Enumerable.Range(0, zeilen.Count).Where(r => Art(r) is "" or "zwischensumme" or "summe").ToList();
        var w = new float[n];
        for (var i = 0; i < n; i++)
        {
            var kopf = Senkrecht(i) ? 13f : Math.Max(koepfe[i].Split(' ').Select(x => TextBreite(x, true, OSchrift)).DefaultIfEmpty(0).Max(), TextBreite(koepfe[i], false, OSchrift) * .7f);
            var inhalt = daten.Where(r => Art(r) == "" || i > 0).Select(r => Zelle(r, i).Split('\n').Select(t => TextBreite(t, false, OSchrift)).DefaultIfEmpty(0).Max())
                .DefaultIfEmpty(0).Max();
            w[i] = Math.Max(kopf, Math.Min(inhalt, nutz * .38f)) + 8;
        }
        // Gruppen brauchen Platz für ihren Titel
        for (var i = 0; i < n;)
        {
            var j = i;
            while (j + 1 < n && gruppen[j + 1] == gruppen[i]) j++;
            if (gruppen[i].Length > 0)
            {
                var noetig = TextBreite(gruppen[i], false, OSchrift) + 8;
                var hat = w.Skip(i).Take(j - i + 1).Sum();
                if (hat < noetig) for (var k = i; k <= j; k++) w[k] += (noetig - hat) / (j - i + 1);
            }
            i = j + 1;
        }
        var summe = w.Sum();
        // schmale Tabellen behalten ihre Breite (wie im Original), breite werden auf die Seite gebracht
        var faktor = summe > nutz ? nutz / summe : d.SenkrechtAb >= 0 ? 1f : Math.Min(nutz / summe, 1.35f);
        for (var i = 0; i < n; i++) w[i] *= faktor;
        // volle Seitenbreite (z. B. Arbeitsbericht): der freie Platz geht an die breiten Textspalten (Thema, Hausaufgaben …)
        if (d.Breit is { Count: > 0 } breit && w.Sum() < nutz)
        {
            var frei = nutz - w.Sum();
            foreach (var i in breit.Where(i => i < n)) w[i] += frei / breit.Count(i => i < n);
        }
        var x0 = new float[n + 1];
        x0[0] = rand;
        for (var i = 0; i < n; i++) x0[i + 1] = x0[i] + w[i];
        var tabBreite = x0[n] - rand;
        var mitGruppen = gruppen.Any(g => g.Length > 0);
        var kopfHoehe = d.SenkrechtAb >= 0
            ? Math.Max(OZeile + 6, koepfe.Where((_, i) => Senkrecht(i)).Select(k => TextBreite(k, false, OSchrift)).DefaultIfEmpty(0).Max() + 10)
            : 4 + OZeile * koepfe.Select((k, i) => Umbrechen(k, w[i] - 6, OSchrift).Count()).DefaultIfEmpty(1).Max();

        var seiten = new List<string>();
        var sb = new StringBuilder();
        var y = 0f;
        var seite = 0;
        var kopfNoetig = true;
        var inhaltAufSeite = false;
        var schwarz = Schwarz;

        void Rahmen(float x, float yo, float b, float h) => sb.Append($"0 G 0.5 w {F(x)} {F(yo - h)} {F(b)} {F(h)} re S\n");
        void Fuellen(float x, float yo, float b, float h, float g) => Rechteck(sb, x, yo - h, b, h, Grau(g));
        void Senkrechter(float x, float yo, string t) =>
            sb.Append($"BT 0 0 0 rg /F1 {F(OSchrift)} Tf 0 -1 1 0 {F(x)} {F(yo)} Tm ({Maskieren(t)}) Tj ET\n");

        void Seitenkopf()
        {
            seite++;
            sb.Clear();
            y = Hoehe - rand;
            // rechts oben: Schule und Schuljahr
            var rechts = new List<string> { schule };
            if (schuljahr.Length > 0) rechts.Add($"Schuljahr : {schuljahr}");
            var rx = Breite - rand - rechts.Max(t => TextBreite(t, false, 10));
            for (var i = 0; i < rechts.Count; i++) Text(sb, rx, y - 10 - i * 13, 10, false, rechts[i], schwarz);
            // Titel, darunter links der Zeitraum
            var ty = y - 10 - Math.Max(0, rechts.Count - 1) * 13 - 6;
            Text(sb, Breite * .3f, ty, 11, false, titel, schwarz);
            y = ty - 10;
            if (zeitraum.Length > 0) { Text(sb, rand, y - 12, 10, false, zeitraum, schwarz); y -= 16; }
            y -= 8;
            Linie(sb, rand, y, Breite - rand, y, 0f);
            y -= 8;
            foreach (var k in d.Kontext ?? new List<string>())
            {
                // „Bezeichnung\tWert\tWert“: Spalten wie „Unterrichtsstunden   47   34:40“
                var teile = k.Split('\t');
                var x = rand;
                for (var t = 0; t < teile.Length; t++)
                {
                    Text(sb, x, y - 12, 10, d.KontextFett, teile[t], schwarz);
                    x += Math.Max(TextBreite(teile[t], d.KontextFett, 10) + 18, t == 0 ? 0 : 50);
                }
                y -= 15;
            }
            y -= 6;
            kopfNoetig = true;
            inhaltAufSeite = false;
        }
        void Seitenfuss()
        {
            var t = $"Seite {seite} von {Seitenzahl}";
            Text(sb, Breite - rand - TextBreite($"Seite {seite} von 99", false, 8), rand - 18, 8, false, t, schwarz);
            seiten.Add(sb.ToString());
        }
        void Tabellenkopf()
        {
            if (d.OhneKopf) { kopfNoetig = false; return; }
            if (mitGruppen)
            {
                for (var i = 0; i < n;)
                {
                    var j = i;
                    while (j + 1 < n && gruppen[j + 1] == gruppen[i]) j++;
                    if (gruppen[i].Length > 0)
                    {
                        var b = x0[j + 1] - x0[i];
                        Fuellen(x0[i], y, b, OZeile + 3, 0.83f);
                        Rahmen(x0[i], y, b, OZeile + 3);
                        Text(sb, x0[i] + 2, y - 10.5f, OSchrift, false, Kuerzen(gruppen[i], b - 4, false, OSchrift), schwarz);
                    }
                    i = j + 1;
                }
                y -= OZeile + 3;
            }
            Fuellen(rand, y, tabBreite, kopfHoehe, 0.83f);
            for (var i = 0; i < n; i++)
            {
                if (d.Gitter) Rahmen(x0[i], y, w[i], kopfHoehe);
                if (Senkrecht(i)) { Senkrechter(x0[i] + w[i] / 2 - 3, y - 4, koepfe[i]); continue; }
                var ty = y - 10;
                foreach (var teil in Umbrechen(koepfe[i], w[i] - 4, OSchrift).Take(3))
                {
                    Text(sb, x0[i] + 2, ty, OSchrift, d.KopfFett || !d.Gitter, Kuerzen(teil, w[i] - 4, d.KopfFett || !d.Gitter, OSchrift), schwarz);
                    ty -= OZeile;
                }
            }
            y -= kopfHoehe;
            kopfNoetig = false;
        }
        void Platz(float hoehe)
        {
            if (y - hoehe < rand) { Seitenfuss(); Seitenkopf(); }
            if (kopfNoetig) Tabellenkopf();
        }

        Seitenkopf();
        if (zeilen.Count == 0)
        {
            Tabellenkopf();
            Text(sb, rand + 2, y - 12, OSchrift, false, "Keine Einträge im gewählten Zeitraum.", schwarz);
        }
        for (var r = 0; r < zeilen.Count; r++)
        {
            var art = Art(r);
            if (art is "block" or "gruppe")
            {
                var teile = art == "block" ? Zelle(r, 0).Split('\n') : new[] { Zelle(r, 0) };
                var hoehe = teile.Length * 16 + 10 + kopfHoehe + (mitGruppen ? OZeile + 3 : 0) + OZeile * 2;
                // Seitenumbruch je Block (außer „ohne Seitenumbrüche“), Block nie allein am Seitenende
                if ((art == "block" && d.Seitenumbruch && !ohneSeitenumbrueche && inhaltAufSeite) || y - hoehe < rand) { Seitenfuss(); Seitenkopf(); }
                y -= inhaltAufSeite ? 12 : 2;
                foreach (var t in teile)
                {
                    var kv = t.Split('\t');
                    // Kopfblock: Bezeichnung fett („Schüler*innen“), Überschrift einer Gruppe wie „Klasse: 7a“ normal
                    Text(sb, rand, y - 12, 10.5f, art == "block", kv[0], schwarz);
                    if (kv.Length > 1) Text(sb, rand + 110, y - 12, 10, false, kv[1], schwarz);
                    y -= 16;
                }
                y -= 4;
                kopfNoetig = true;
                inhaltAufSeite = true;
                continue;
            }
            // Zellen: „\n“ trennt Zeilen (jede gekürzt), sonst Umbruch an Wortgrenzen (höchstens 4 Zeilen)
            var summenzeile = art is "zwischensumme" or "summe";
            var inhalt = Enumerable.Range(0, n).Select(i =>
            {
                var t = Zelle(r, i);
                var breite = w[i] - 4;
                if (summenzeile && i == 0) return new List<string> { t };
                return t.Contains('\n') ? t.Split('\n').Select(x => Kuerzen(x, breite, false, OSchrift)).ToList()
                    : Umbrechen(t, breite, OSchrift).Take(4).Select(x => Kuerzen(x, breite, false, OSchrift)).DefaultIfEmpty("").ToList();
            }).ToList();
            var hoeheZ = Math.Max(1, inhalt.Max(z => z.Count)) * OZeile + 4;
            Platz(hoeheZ);
            for (var i = 0; i < n; i++)
            {
                if (grau.Contains(i) && !summenzeile) Fuellen(x0[i], y, w[i], hoeheZ, 0.88f);
                if (d.Gitter && (!summenzeile || (i > 0 && Zelle(r, i).Length > 0))) Rahmen(x0[i], y, w[i], hoeheZ);
                var fett = art == "summe" && !Zahl(Zelle(r, i));
                var ty = y - 10;
                foreach (var teil in inhalt[i])
                {
                    Text(sb, x0[i] + 2, ty, OSchrift, fett, teil, schwarz);
                    ty -= OZeile;
                }
            }
            y -= hoeheZ;
            inhaltAufSeite = true;
        }
        Seitenfuss();
        var anzahl = seiten.Count.ToString(CultureInfo.InvariantCulture);
        return Datei(seiten.Select(x => x.Replace(Seitenzahl, anzahl)).ToList());
    }

    private static bool Zahl(string t) => t.Length > 0 && t.All(c => char.IsDigit(c) || c is ':' or '.' or ',' or '%' or ' ' or '-');
}
