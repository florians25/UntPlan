namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Gemeinsame Regeln aller Aufklapplisten der Desktop-Anwendung. Jede Liste des Programms folgt genau diesen Regeln, egal ob sie ein
/// Formularfeld, ein Kopfbereich, eine Werkzeugleiste, ein Dialog, eine Tabellenzelle oder die Mehrfachauswahl unter einer Zelle ist:
/// <list type="bullet">
/// <item>Tippen öffnet die Liste und markiert sofort den besten Treffer (<see cref="Treffer"/>), Enter und Tab übernehmen ihn wie
/// ein Mausklick, Esc stellt den bisherigen Wert her; ungültige Eingaben bleiben beim alten Wert (mit kurzem Hinweis)</item>
/// <item>Alt+↓ bzw. F4 öffnet, Alt+↑ übernimmt und schließt, ↑/↓ blättern, Bild auf/ab springen seitenweise</item>
/// <item>das Mausrad über der zugeklappten Liste blättert genau einen Eintrag je Rastung (<see cref="Rad"/>), in der offenen Liste
/// rollt es die Liste; steht der Mauszeiger nicht über der Liste, rollt die Umgebung</item>
/// <item>Kurzname und Langname in zwei Spalten, nie fett, höchstens <see cref="SichtbareEintraege"/> Einträge, öffnet nach unten,
/// abgeschnittene Einträge vollständig im Tooltip</item>
/// <item>Markierung in einer gemeinsamen Farbe (<see cref="AuswahlHintergrund"/>), in inaktiven Fenstern ohne blaue Markierung</item>
/// </list>
/// Umgesetzt wird das für Windows-Auswahlfelder in <see cref="ListenSuche"/> (programmweit über <see cref="Auswahlbreite"/>
/// angemeldet, auch für Tabellenzellen über <see cref="SuchKombi"/>) und für die Listen unter Tabellenzellen in
/// <see cref="MehrfachAuswahl"/>; beide verwenden die Funktionen dieser Klasse.
/// </summary>
public static class Aufklappliste
{
    /// <summary>Zahl der Einträge, die eine geöffnete Liste höchstens zeigt (überall gleich, danach Rollbalken).</summary>
    public const int SichtbareEintraege = 20;

    /// <summary>Pause, nach der eine Tippsuche wieder von vorn beginnt (wie die Schnellsuche von Windows).</summary>
    public static readonly TimeSpan TippPause = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Index des besten Treffers für den getippten Text: exakter Kurzname, Kurzname beginnt, Langname beginnt, ein Wort des
    /// Langnamens beginnt, Kurzname enthält, Langname enthält; -1 ohne Treffer.
    /// </summary>
    public static int Treffer(IReadOnlyList<string> kurz, IReadOnlyList<string> lang, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return -1;
        const StringComparison ci = StringComparison.CurrentCultureIgnoreCase;
        string L(int i) => i < lang.Count ? lang[i] ?? "" : "";
        int Suche(Func<int, bool> passt) { for (var i = 0; i < kurz.Count; i++) if (passt(i)) return i; return -1; }
        var r = Suche(i => kurz[i].Equals(text, ci));
        if (r < 0) r = Suche(i => kurz[i].StartsWith(text, ci));
        if (r < 0) r = Suche(i => L(i).StartsWith(text, ci));
        if (r < 0) r = Suche(i => L(i).Split(' ', ',', '-').Any(w => w.StartsWith(text, ci)));
        if (r < 0) r = Suche(i => kurz[i].Contains(text, ci));
        if (r < 0) r = Suche(i => L(i).Contains(text, ci));
        return r;
    }

    /// <summary>Nächster Index beim Blättern (Mausrad, Pfeiltasten): ohne Auswahl beginnt es am Anfang bzw. Ende, an den Enden bleibt es stehen.</summary>
    public static int Blaettern(int index, int anzahl, int richtung)
    {
        if (anzahl <= 0) return -1;
        if (index < 0) return richtung > 0 ? 0 : anzahl - 1;
        return Math.Clamp(index + richtung, 0, anzahl - 1);
    }

    /// <summary>Mausrad einer Liste: sammelt feine Räder bis zu einer vollen Rastung (120) und liefert die Schritte (+1 = nächster Eintrag).</summary>
    public sealed class Rad
    {
        private int _rest;

        public IEnumerable<int> Schritte(int delta)
        {
            _rest += delta;
            while (Math.Abs(_rest) >= 120)
            {
                var r = _rest > 0 ? -1 : 1;
                _rest += r * 120;
                yield return r;
            }
        }
    }

    /// <summary>Liegt der Punkt (Steuerelementkoordinaten) über der Liste? Nur dann blättert das Rad, sonst rollt die Umgebung.</summary>
    public static bool UeberDerListe(Control c, Point p) => p.X >= 0 && p.Y >= 0 && p.X < c.Width && p.Y < c.Height;

    /// <summary>Markierter Eintrag: im Vertretungsmodus in der hellen Auswahlfarbe des Vorbilds, sonst in der Systemfarbe.</summary>
    public static Color AuswahlHintergrund => Ui.Vertretungsmodus ? Ui.AuswahlVertretung : SystemColors.Highlight;

    /// <summary>Textfarbe des markierten Eintrags (passend zu <see cref="AuswahlHintergrund"/>).</summary>
    public static Color AuswahlText => Ui.Vertretungsmodus ? Color.Black : SystemColors.HighlightText;

    /// <summary>Text für den Tooltip eines abgeschnittenen Eintrags (leer, wenn alles sichtbar ist).</summary>
    public static string Tooltip(string kurz, string lang, Font schrift, int platzLang) =>
        lang.Length > 0 && TextRenderer.MeasureText(lang, schrift).Width > platzLang ? $"{kurz}  {lang}" : "";
}
