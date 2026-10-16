using System.Globalization;
using System.Text.Json;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Regeln für Verspätungen und vorzeitiges Gehen (Administration » Verspätungen):
/// <list type="bullet">
/// <item><see cref="AbwesendAbMinuten"/>: ab so vielen Minuten zählt die Stunde als Fehlstunde (0 = nie, nur ganze verpasste Stunden).</item>
/// <item><see cref="MinutenUmrechnen"/>: Verspätungs- und Gehminuten werden im Bericht zu Fehlstunden zusammengerechnet (je <see cref="MinutenProFehlstunde"/>).</item>
/// <item><see cref="HinweisAbAnzahl"/>: ab so vielen Verspätungen im Zeitraum erscheint ein Hinweis für die Klassenleitung (0 = aus).</item>
/// <item><see cref="VorzeitigRestTag"/>: Voreinstellung „für den Rest des Tages abwesend“ beim vorzeitigen Gehen.</item>
/// </list>
/// </summary>
public sealed record Verspaetungsregeln(int AbwesendAbMinuten, bool MinutenUmrechnen, int MinutenProFehlstunde, int HinweisAbAnzahl, bool VorzeitigRestTag)
{
    public static Verspaetungsregeln Standard { get; } = new(0, true, 45, 3, true);

    /// <summary>Volle Fehlstunden aus Minuten (nur bei eingeschalteter Umrechnung).</summary>
    public int StundenAus(int minuten) => MinutenUmrechnen && MinutenProFehlstunde > 0 ? minuten / MinutenProFehlstunde : 0;

    /// <summary>Hinweistext bei auffällig vielen Verspätungen bzw. unentschuldigten Stunden.</summary>
    public string Hinweis(int verspaetungen, int vorzeitig, int unentschuldigt)
    {
        var teile = new List<string>();
        if (HinweisAbAnzahl > 0 && verspaetungen >= HinweisAbAnzahl) teile.Add($"{verspaetungen}× verspätet");
        if (HinweisAbAnzahl > 0 && vorzeitig >= HinweisAbAnzahl) teile.Add($"{vorzeitig}× vorzeitig gegangen");
        if (unentschuldigt >= 10) teile.Add($"{unentschuldigt} unentschuldigte Stunden");
        return string.Join(", ", teile);
    }
}

public sealed partial class Klassenbuchdienst
{
    public const string RegelSchluessel = "einstellung:verspaetung";

    /// <summary>Gespeicherte Verspätungsregeln (fehlende Werte = Standard).</summary>
    public Verspaetungsregeln Regeln()
    {
        var r = Verspaetungsregeln.Standard;
        var json = new Einstellungen(_db).Lesen(RegelSchluessel);
        if (string.IsNullOrEmpty(json)) return r;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var o = doc.RootElement;
            int Z(string k, int std) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : std;
            bool W(string k, bool std) => o.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.True : std;
            return new Verspaetungsregeln(Math.Clamp(Z("abwesendAbMinuten", r.AbwesendAbMinuten), 0, 240), W("minutenUmrechnen", r.MinutenUmrechnen),
                Math.Clamp(Z("minutenProFehlstunde", r.MinutenProFehlstunde), 1, 240), Math.Clamp(Z("hinweisAbAnzahl", r.HinweisAbAnzahl), 0, 100),
                W("vorzeitigRestTag", r.VorzeitigRestTag));
        }
        catch (JsonException) { return r; }
    }

    /// <summary>Länge einer Stunde laut Zeitraster in Minuten (Standard 45).</summary>
    public static int StundenLaenge(Stand s, int nr)
    {
        var z = s.Paket.Zeitraster.Stunden.FirstOrDefault(x => x.Nr == nr);
        if (z is null || !TimeOnly.TryParse(z.Beginn, CultureInfo.InvariantCulture, out var b) || !TimeOnly.TryParse(z.Ende, CultureInfo.InvariantCulture, out var e) || e <= b) return 45;
        return (int)(e - b).TotalMinutes;
    }

    /// <summary>
    /// Verteilt Verspätungs- bzw. Gehminuten auf die Stunden eines Blocks: ganz verpasste Stunden werden zu Fehlstunden,
    /// der Rest bleibt Verspätung bzw. vorzeitiges Gehen – oder wird ab der Schwelle der Regeln ebenfalls zur Fehlstunde.
    /// </summary>
    public static List<(int Stunde, string Art, int Minuten, string Hinweis)> MinutenVerteilen(Stand s, Verspaetungsregeln regeln, int von, int bis, string art, int minuten)
    {
        var erg = new List<(int, string, int, string)>();
        var stunden = Enumerable.Range(von, bis - von + 1).ToList();
        if (art == "vorzeitig") stunden.Reverse();
        var rest = Math.Max(1, minuten);
        var text = art == "vorzeitig" ? "vorzeitig gegangen" : "verspätet";
        foreach (var n in stunden)
        {
            var laenge = StundenLaenge(s, n);
            if (rest >= laenge)
            {
                erg.Add((n, "fehlt", 0, $"{text} ({minuten} min)"));
                rest -= laenge;
                if (rest == 0) break;
                continue;
            }
            if (regeln.AbwesendAbMinuten > 0 && rest >= regeln.AbwesendAbMinuten) erg.Add((n, "fehlt", 0, $"{text} {rest} min"));
            else erg.Add((n, art, rest, ""));
            break;
        }
        return erg.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>„Für den Rest des Tages abwesend“: alle späteren Unterrichtsstunden des Tages als Fehlstunden eintragen (vorhandene Einträge bleiben).</summary>
    private void RestTagEintragen(Benutzer b, Stand s, DateOnly d, Karte karte, int schuelerId, string klasse, string lehrer, string jetzt, string grund)
    {
        foreach (var k in _plan.Karten(s, d, Elementtyp.Klasse, klasse)
                     .Where(k => k.Unterricht.HasValue && k.Status is not ("entfall" or "veranstaltung") && k.Von > karte.Bis))
            for (var n = k.Von; n <= k.Bis; n++)
                _db.Ausfuehren("""
                    INSERT INTO fehlzeiten (datum, stunde, unterricht, schueler_id, klasse, fach, art, minuten, status, bemerkung, lehrer, erstellt, benutzer_id, grund, gruppe)
                    VALUES (@d, @s, @u, @sid, @k, @f, 'fehlt', 0, 'offen', @bem, @l, @e, @b, @g, @gr)
                    ON CONFLICT(datum, stunde, schueler_id) DO NOTHING
                    """, Db.P("@d", d), Db.P("@s", n), Db.P("@u", k.Unterricht), Db.P("@sid", schuelerId), Db.P("@k", klasse), Db.P("@f", k.Fach),
                    Db.P("@bem", $"vorzeitig gegangen ({karte.Von}. Std.)"), Db.P("@l", lehrer), Db.P("@e", jetzt), Db.P("@b", b.Id), Db.P("@g", grund),
                    Db.P("@gr", RestTagGruppe(karte.Unterricht ?? 0, karte.Von)));
    }

    private static readonly System.Text.RegularExpressions.Regex Vermerk =
        new(@"^(?<art>verspätet|vorzeitig gegangen)(?: \((?<ges>\d+) min\)| (?<rest>\d+) min)(?: – (?<text>.*))?$", System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>
    /// Fasst die Einträge einer Schülerin/eines Schülers in einem Block für die Anwesenheitsliste zusammen: Verspätung bzw. vorzeitiges
    /// Gehen mit den ursprünglich erfassten Minuten (auch wenn Stunden daraus als Fehlstunden gewertet wurden) und dem eigenen Text.
    /// </summary>
    internal static object Zusammenfassen(IReadOnlyList<Fehlzeit> eintraege, Stand? s = null, IReadOnlyList<Fehlzeit>? gesamt = null)
    {
        var liste = eintraege.OrderBy(f => f.Stunde).ToList();
        var f0 = liste[0];
        string art = "fehlt";
        int? minuten = null;
        string? text = null;
        foreach (var f in liste)
        {
            var m = Vermerk.Match(f.Bemerkung);
            if (f.Art is "verspaetet" or "vorzeitig") { art = f.Art; minuten ??= f.Minuten; }
            if (f.Art == "fehlt" && m.Success)
            {
                art = m.Groups["art"].Value == "verspätet" ? "verspaetet" : "vorzeitig";
                if (m.Groups["ges"].Success) minuten = int.Parse(m.Groups["ges"].Value);
                else if (minuten is null) minuten = int.Parse(m.Groups["rest"].Value);
                text ??= m.Groups["text"].Success ? m.Groups["text"].Value : "";
            }
        }
        var haupt = liste.FirstOrDefault(f => f.Art == art) ?? f0;
        return new
        {
            id = haupt.Id, datum = haupt.Datum, stunde = haupt.Stunde, unterricht = haupt.Unterricht, schuelerId = haupt.SchuelerId, klasse = haupt.Klasse,
            fach = haupt.Fach, art, minuten = art == "fehlt" ? 0 : minuten ?? 0, status = f0.Status, bemerkung = text ?? haupt.Bemerkung, lehrer = haupt.Lehrer,
            erstellt = haupt.Erstellt, grund = haupt.Grund, gruppe = haupt.Gruppe,
            fehlstunden = liste.Count(f => f.Art == "fehlt"), stunden = liste.Select(f => f.Stunde).ToList(),
            // minutengenau: abwesend von–bis in diesem Block und Zeitraum der ganzen Abwesenheit (Abwesenheitsdialog)
            beginn = s is null ? "" : Hhmm(liste.Min(f => Fenster(s, f).Beginn)), ende = s is null ? "" : Hhmm(liste.Max(f => Fenster(s, f).Ende)),
            vonDatum = (gesamt ?? liste)[0].Datum, vonZeit = s is null ? "" : Hhmm(Fenster(s, (gesamt ?? liste)[0]).Beginn),
            bisDatum = (gesamt ?? liste)[^1].Datum, bisZeit = s is null ? "" : Hhmm(Fenster(s, (gesamt ?? liste)[^1]).Ende),
            text = Klartext(text ?? haupt.Bemerkung), nachweis = haupt.Nachweis, ueberBlock = gesamt?.Count > liste.Count,
        };
    }

    /// <summary>Alle Fehlzeiten der Abwesenheit, zu der die Einträge eines Blocks gehören (Gruppe bzw. „Rest des Tages“), chronologisch.</summary>
    private List<Fehlzeit> Gesamtabwesenheit(List<Fehlzeit> block, Karte karte)
    {
        var sid = block[0].SchuelerId;
        var gruppen = block.Select(f => f.Gruppe).Where(g => g.Length > 0).ToHashSet();
        gruppen.Add($"vorzeitig:{karte.Unterricht ?? 0}:{karte.Von}");
        var res = block.ToDictionary(f => f.Id);
        foreach (var g in gruppen)
            foreach (var f in _db.Liste("SELECT * FROM fehlzeiten WHERE schueler_id = @s AND gruppe = @g", LesenFehlzeit, Db.P("@s", sid), Db.P("@g", g))) res[f.Id] = f;
        return res.Values.OrderBy(f => f.Datum, StringComparer.Ordinal).ThenBy(f => f.Stunde).ToList();
    }

    private static string RestTagGruppe(int unterricht, int stunde) => $"vorzeitig:{unterricht}:{stunde}";
}
