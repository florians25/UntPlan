using System.Text;

namespace UntPlanWeb.Dienste;

public static partial class PdfBericht
{
    /// <summary>
    /// Kennwort-Blatt (Anmeldeblätter, Leitfaden Klassenleitung 6.1): DIN A4 hoch, oben rechts Schule und Schuljahr, je Seite sechs Kärtchen
    /// zum Ausschneiden mit „Name, Klasse“, Benutzername, Passwort, Schulname und der Anweisung zur Anmeldung; unten „UntPlanWeb“ und die Seitenzahl.
    /// </summary>
    public static byte[] Kennwortblatt(string schule, string schuljahr, string adresse, IReadOnlyList<Anmeldekarte> karten) =>
        MitAusrichtung(true, () => KennwortblattErzeugen(schule, schuljahr, adresse, karten));

    private static byte[] KennwortblattErzeugen(string schule, string schuljahr, string adresse, IReadOnlyList<Anmeldekarte> karten)
    {
        const int jeSeite = 6;
        var seiten = new List<string>();
        var anzahl = Math.Max(1, (karten.Count + jeSeite - 1) / jeSeite);
        var nutz = Breite - 2 * Rand;
        const float luecke = 18;
        var kb = (nutz - luecke) / 2;
        const float kh = 150;
        var anweisung = $"Bitte öffnen Sie {adresse} oder laden Sie die UntPlanMobile App auf Ihr Smartphone und suchen Sie Ihre Schule. "
                        + "Anschließend können Sie sich mit diesem Benutzernamen und Passwort anmelden.";
        for (var seite = 0; seite < anzahl; seite++)
        {
            var sb = new StringBuilder();
            // Kopf oben rechts: Schule, Schuljahr
            var y = Hoehe - Rand;
            foreach (var (t, fett) in new[] { (schule, true), ($"Schuljahr: {schuljahr}", false) })
            {
                var k = Kuerzen(t, nutz * .5f, fett, 9);
                Text(sb, Breite - Rand - TextBreite(k, fett, 9), y - 9, 9, fett, k, Schwarz);
                y -= 13;
            }
            y -= 30;
            foreach (var (karte, i) in karten.Skip(seite * jeSeite).Take(jeSeite).Select((k, i) => (k, i)))
            {
                var x = Rand + (i % 2) * (kb + luecke);
                var oben = y - (i / 2) * (kh + luecke);
                Kasten(sb, x, oben - kh, kb, kh, 0f);
                var titel = Kuerzen($"{karte.Name}, {karte.Klasse}", kb - 16, true, 10);
                Text(sb, x + 8, oben - 16, 10, true, titel, Schwarz);
                Linie(sb, x, oben - 24, x + kb, oben - 24, 0f);
                var zy = oben - 40;
                foreach (var (feld, wert, fett) in new[] { ("Benutzername", karte.Login, true), ("Passwort", karte.Passwort, true), ("Schulname", schule, false) })
                {
                    Text(sb, x + 8, zy, 8.5f, false, feld, Schwarz);
                    Text(sb, x + 78, zy, 9, fett, Kuerzen(wert, kb - 86, fett, 9), Schwarz);
                    Linie(sb, x + 6, zy - 5, x + kb - 6, zy - 5, 0.75f);
                    zy -= 17;
                }
                zy -= 4;
                foreach (var teil in Umbrechen(anweisung, kb - 16, 7.5f).Take(5))
                {
                    Text(sb, x + 8, zy, 7.5f, false, teil, Schwarz);
                    zy -= 9.5f;
                }
            }
            // Fuß
            Linie(sb, Rand, Rand - 4, Breite - Rand, Rand - 4, 0f);
            Text(sb, Rand, Rand - 16, 7.5f, false, $"UntPlanWeb {DateTime.Now:yyyy}", Schwarz);
            var mitte = Kuerzen(schule, nutz * .4f, false, 7.5f);
            Text(sb, (Breite - TextBreite(mitte, false, 7.5f)) / 2, Rand - 16, 7.5f, false, mitte, Schwarz);
            var rechts = $"Seite {seite + 1} von {anzahl}";
            Text(sb, Breite - Rand - TextBreite(rechts, false, 7.5f), Rand - 16, 7.5f, false, rechts, Schwarz);
            seiten.Add(sb.ToString());
        }
        return Datei(seiten);
    }
}
