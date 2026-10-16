using System.Text;

namespace UntPlanWeb.Dienste;

public static partial class PdfBericht
{
    /// <summary>
    /// Sitzplan als schlichtes Schwarz-Weiß-Formular (A4 quer oder hoch, eine Seite): Kopf wie bei Berichten, Titel, Pult als schwarzer Balken
    /// vorne (oben) oder hinten (unten, Sicht vom Pult – Reihen und Plätze gespiegelt), Tische als umrandete Kästen mit Name
    /// (und Klasse), Plätze ohne Tisch bleiben leer, darunter „Ohne Platz“.
    /// </summary>
    /// <param name="plaetze">Alle Plätze (Reihe 1 = am Pult) mit Name, optional Klasse, und ob dort ein Tisch steht.</param>
    public static byte[] Sitzplan(string schule, string titel, string untertitel, int zeilen, int spalten, bool pultHinten,
        IReadOnlyList<(int Reihe, int Spalte, string? Name, string? Klasse, bool Tisch)> plaetze, IReadOnlyList<string> ohnePlatz, string? ersteller, bool hochformat) =>
        MitAusrichtung(hochformat, () => Sitzplan(schule, titel, untertitel, zeilen, spalten, pultHinten, plaetze, ohnePlatz, ersteller));

    public static byte[] Sitzplan(string schule, string titel, string untertitel, int zeilen, int spalten, bool pultHinten,
        IReadOnlyList<(int Reihe, int Spalte, string? Name, string? Klasse, bool Tisch)> plaetze, IReadOnlyList<string> ohnePlatz, string? ersteller)
    {
        var sb = new StringBuilder();
        var nutz = Breite - 2 * Rand;
        var jetzt = DateTime.Now;
        var y = Hoehe - Rand + 6;
        Text(sb, Rand, y - 10, 11, true, schule, Schwarz);
        Text(sb, Rand, y - 23, 8, false, "Klassenbuch · Sitzplan", Schwarz);
        var r1 = $"Erstellt am {jetzt:dd.MM.yyyy} um {jetzt:HH:mm} Uhr";
        Text(sb, Breite - Rand - TextBreite(r1, false, 8), y - 10, 8, false, r1, Schwarz);
        if (!string.IsNullOrWhiteSpace(ersteller)) { var r2 = $"von {ersteller}"; Text(sb, Breite - Rand - TextBreite(r2, false, 8), y - 21, 8, false, r2, Schwarz); }
        y -= 34;
        Balken(sb, Rand, y, nutz, 0.8f, Schwarz);
        y -= 24;
        Text(sb, Rand, y, 16, true, titel, Schwarz);
        if (untertitel.Length > 0) Text(sb, Rand, y - 15, 9.5f, false, untertitel, Schwarz);
        y -= untertitel.Length > 0 ? 30 : 16;

        // Fläche für Pult + Raster (+ Liste ohne Platz) bis zum Fuß
        var ohneZeilen = ohnePlatz.Count == 0 ? 0 : Umbrechen("Ohne Platz: " + string.Join(", ", ohnePlatz), nutz, 8.5f).Count();
        var unten = Rand + 10 + ohneZeilen * 11 + (ohneZeilen > 0 ? 10 : 0);
        const float pultHoehe = 16, abstand = 10, luecke = 6;
        var rasterHoehe = y - unten - pultHoehe - abstand;
        var zh = Math.Min(54, (rasterHoehe - (zeilen - 1) * luecke) / zeilen);
        var sw = Math.Min(130, (nutz - (spalten - 1) * luecke) / spalten);
        var rasterBreite = spalten * sw + (spalten - 1) * luecke;
        var x0 = Rand + (nutz - rasterBreite) / 2;
        var gesamt = zeilen * zh + (zeilen - 1) * luecke;

        void Pult(float py)
        {
            var pb = Math.Min(220, rasterBreite * .5f);
            Rechteck(sb, Rand + (nutz - pb) / 2, py - pultHoehe, pb, pultHoehe, Schwarz);
            const string t = "Lehrerpult / Tafel";
            Text(sb, Rand + (nutz - TextBreite(t, true, 8.5f)) / 2, py - pultHoehe + 4.6f, 8.5f, true, t, new Farbe(1, 1, 1));
        }
        var oben = y;
        if (!pultHinten) { Pult(oben); oben -= pultHoehe + abstand; }
        foreach (var p in plaetze)
        {
            // Darstellungsreihe/-spalte: Pult vorne → Reihe 1 oben, Platz 1 links; Pult hinten → um 180° gedreht
            var dr = pultHinten ? zeilen - p.Reihe : p.Reihe - 1;
            var ds = pultHinten ? spalten - p.Spalte : p.Spalte - 1;
            var bx = x0 + ds * (sw + luecke);
            var by = oben - dr * (zh + luecke) - zh;
            if (!p.Tisch) continue;
            Kasten(sb, bx, by, sw, zh, p.Name is null ? 0.6f : 0f);
            if (p.Name is null) continue;
            var groesse = zh < 30 ? 7.5f : 9f;
            var teile = Umbrechen(p.Name, sw - 8, groesse).Take(2).ToList();
            if (teile.Count == 2 && Umbrechen(p.Name, sw - 8, groesse).Count() > 2) teile[1] = Kuerzen(string.Join(" ", Umbrechen(p.Name, sw - 8, groesse).Skip(1)), sw - 8, true, groesse);
            var zeilenGesamt = teile.Count + (p.Klasse is null ? 0 : 1);
            var ty = by + zh / 2 + (zeilenGesamt * (groesse + 2)) / 2 - groesse;
            foreach (var t in teile)
            {
                var k = Kuerzen(t, sw - 8, true, groesse);
                Text(sb, bx + (sw - TextBreite(k, true, groesse)) / 2, ty, groesse, true, k, Schwarz);
                ty -= groesse + 2;
            }
            if (p.Klasse is not null) Text(sb, bx + (sw - TextBreite(p.Klasse, false, 7.5f)) / 2, ty, 7.5f, false, p.Klasse, Schwarz);
        }
        if (pultHinten) Pult(oben - gesamt - abstand);
        var ly = (pultHinten ? oben - gesamt - abstand - pultHoehe : oben - gesamt) - 14;
        foreach (var teil in ohnePlatz.Count == 0 ? Enumerable.Empty<string>() : Umbrechen("Ohne Platz: " + string.Join(", ", ohnePlatz), nutz, 8.5f))
        {
            Text(sb, Rand, ly, 8.5f, false, teil, Schwarz);
            ly -= 11;
        }
        Linie(sb, Rand, Rand - 4, Breite - Rand, Rand - 4, 0f);
        Text(sb, Rand, Rand - 16, 7.5f, false, Kuerzen($"{schule} · {titel}", nutz * .5f, false, 7.5f), Schwarz);
        const string mitte = "Nur für den Dienstgebrauch";
        Text(sb, (Breite - TextBreite(mitte, false, 7.5f)) / 2, Rand - 16, 7.5f, false, mitte, Schwarz);
        return Datei(new List<string> { sb.ToString() });
    }

    private static void Kasten(StringBuilder sb, float x, float y, float b, float h, float grau) =>
        sb.Append($"{F(grau)} G 0.8 w {F(x)} {F(y)} {F(b)} {F(h)} re S\n");
}
