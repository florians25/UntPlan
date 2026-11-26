namespace UntPlan.App.Steuerelemente;

/// <summary>
/// Beschriftungen des Menübands in der Laufweite des Vorbilds (Bild 308): Die Systemschrift läuft bei einzelnen Wörtern 1 bis 3
/// Pixel breiter als dort. Solche Beschriftungen werden Zeichen für Zeichen mit der normalen Schrift gesetzt und die Abstände
/// an gleichmäßig verteilten Stellen um je 1 Pixel enger gestellt. Die Zeichen selbst bleiben unverändert.
/// </summary>
internal static class Laufweite
{
    /// <summary>Pixel, um die eine Beschriftung enger läuft (Tintenbreite hier minus Tintenbreite im Vorbild, 100 %).</summary>
    private static readonly Dictionary<string, int> Abzug = new()
    {
        ["Kalender"] = 1, ["Absenzen"] = 1, ["Ausgabe"] = 2, ["Planungsdialog"] = 1, ["Vormerkungen"] = 2, ["Pausenaufsichten"] = 2,
        ["Sondereinsätze"] = 1, ["Bereitschaften"] = 1, ["Klassenplan"] = 1, ["Lehrerplan"] = 1, ["Raumplan"] = 2,
        ["Vertretungsformate"] = 3, ["Info-Stundenplan"] = 1,
    };

    /// <summary>Waagrechter Versatz mittiger Beschriftungen in Pixeln (gemessen: Lage der Tinte im Vorbild minus hier).</summary>
    private static readonly Dictionary<string, int> Versatz = new()
    {
        ["Vertretungsstatistik"] = -1, ["Pausenaufsichten"] = -1, ["Kalender"] = -1, ["Ausgabe"] = -1, ["Vormerkungen"] = -1,
    };

    private const TextFormatFlags Einzeln = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;

    /// <summary>Abzug für eine Beschriftung (0 = unverändert).</summary>
    internal static int AbzugFuer(string text) => Abzug.TryGetValue(text, out var k) ? k : 0;

    /// <summary>Gezeichnete Breite (ohne Innenabstand) einschließlich Abzug.</summary>
    internal static int Breite(string text, Font schrift) =>
        TextRenderer.MeasureText(text, schrift, Size.Empty, Einzeln).Width - AbzugFuer(text);

    /// <summary>Lücken (Index des folgenden Zeichens), an denen je 1 Pixel weggenommen wird: gleichmäßig über das Wort verteilt.</summary>
    internal static HashSet<int> Stellen(int laenge, int k)
    {
        var s = new HashSet<int>();
        var luecken = laenge - 1;
        if (luecken <= 0) return s;
        for (var i = 0; i < k && i < luecken; i++)
            s.Add(1 + (int)((i + 0.5) * luecken / Math.Min(k, luecken)));
        return s;
    }

    /// <summary>
    /// Wie <see cref="TextRenderer.DrawText(IDeviceContext, string, Font, Rectangle, Color, TextFormatFlags)"/>; Beschriftungen mit
    /// Abzug werden eingeengt gesetzt (nur einzeilig, sonst unverändert). Unterstützt waagrecht links oder mittig, senkrecht oben oder mittig.
    /// </summary>
    internal static void Zeichnen(Graphics g, string text, Font schrift, Rectangle r, Color farbe, TextFormatFlags flags)
    {
        var k = AbzugFuer(text);
        var mittig = (flags & TextFormatFlags.HorizontalCenter) != 0;
        if (mittig && Versatz.TryGetValue(text, out var v)) r.Offset(v, 0);
        var voll = TextRenderer.MeasureText(g, text, schrift, Size.Empty, Einzeln);
        if (k == 0 || text.Length < 2 || voll.Width - k > r.Width)
        {
            TextRenderer.DrawText(g, text, schrift, r, farbe, flags);
            return;
        }
        // Innenabstand wie TextRenderer ohne NoPadding: links und rechts gleich, bei mittiger Ausrichtung ohne Wirkung
        var innen = (flags & TextFormatFlags.NoPadding) != 0 || mittig ? 0 : (int)Math.Ceiling(schrift.Height / 6.0);
        // mittig: Anfang wie bei voller Breite, um die Hälfte des Abzugs (abgerundet) nach rechts (gemessen am Vorbild)
        var x = mittig ? r.X + (r.Width - voll.Width) / 2 + k / 2 : r.X + innen;
        var y = (flags & TextFormatFlags.VerticalCenter) != 0 ? r.Y + (r.Height - voll.Height) / 2 : r.Y;
        var stellen = Stellen(text.Length, k);
        var weg = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (stellen.Contains(i)) weg++;
            var vor = i == 0 ? 0 : TextRenderer.MeasureText(g, text[..i], schrift, Size.Empty, Einzeln).Width;
            TextRenderer.DrawText(g, text[i].ToString(), schrift, new Point(x + vor - weg, y), farbe, Einzeln);
        }
    }
}
