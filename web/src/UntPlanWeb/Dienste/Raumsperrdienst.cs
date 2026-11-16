using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Raumsperre (Administration → Raumsperren): Räume bzw. Ressourcen stehen zu bestimmten Zeiten nicht für Buchungen zur Verfügung
/// (z. B. Mitnutzung durch eine andere Schule). Art: <c>raum</c> (Ziel = Raumkürzel), <c>raumgruppe</c> (Ziel = Raumgruppe),
/// <c>alle_raeume</c>, <c>ressource</c> (Ziel = Ressourcen-Id), <c>alle_ressourcen</c>.
/// Kontinuierlich: ohne Unterbrechung von Beginn (Datum + Uhrzeit) bis Ende. Sonst nur im Zeitbereich Beginn–Ende an jedem Tag des
/// Datumsbereichs (optional nur an einem Wochentag, 0 = Montag) – so entstehen regelmäßige Sperren.
/// </summary>
public sealed record Raumsperre(int Id, string Art, string Ziel, string Von, string Bis, string Beginn, string Ende, bool Kontinuierlich, int Wochentag,
    string Text, string ErstelltVon, string Erstellt)
{
    public bool FuerRaeume => Art is Raumsperrdienst.Raum or Raumsperrdienst.Raumgruppe or Raumsperrdienst.AlleRaeume;
}

public sealed record RaumsperreDaten(string? Art, string? Ziel, string? Von, string? Bis, string? Beginn, string? Ende, bool? Kontinuierlich, int? Wochentag, string? Text);

public sealed class Raumsperrdienst
{
    public const string Raum = "raum", Raumgruppe = "raumgruppe", AlleRaeume = "alle_raeume", Ressource = "ressource", AlleRessourcen = "alle_ressourcen";
    public static readonly IReadOnlyDictionary<string, string> ArtText = new Dictionary<string, string>
    {
        [Raum] = "Raum", [Raumgruppe] = "Raumgruppe", [AlleRaeume] = "Alle Räume", [Ressource] = "Ressource", [AlleRessourcen] = "Alle Ressourcen",
    };
    /// <summary>Einstellung „Buchungen sperren“ (Buchen → Buchungen ent-/sperren): keine neuen Buchungen für alle Benutzer.</summary>
    public const string BuchungenGesperrtSchluessel = "buchungen_gesperrt";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex Uhrzeit = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly Einstellungen _einstellungen;

    public Raumsperrdienst(Db db, Uhr uhr, Einstellungen einstellungen) { _db = db; _uhr = uhr; _einstellungen = einstellungen; }

    private static Raumsperre Lesen(DbDataReader r) => new(r.Zahl("id"), r.Text("art"), r.Text("ziel"), r.Text("von"), r.Text("bis"), r.Text("beginn"), r.Text("ende"),
        r.Zahl("kontinuierlich") == 1, r.Zahl("wochentag"), r.Text("text"), r.Text("erstellt_von"), r.Text("erstellt"));

    public List<Raumsperre> Alle() => _db.Liste("SELECT * FROM raumsperren ORDER BY von, beginn, id", Lesen);
    public Raumsperre? Holen(int id) => _db.Einzel("SELECT * FROM raumsperren WHERE id = @id", Lesen, Db.P("@id", id));

    public bool BuchungenGesperrt => _einstellungen.Lesen(BuchungenGesperrtSchluessel) == "1";
    public void BuchungenSperren(bool gesperrt) => _einstellungen.Schreiben(BuchungenGesperrtSchluessel, gesperrt ? "1" : "0");

    public Raumsperre Speichern(int? id, RaumsperreDaten d, Benutzer ich)
    {
        var art = (d.Art ?? "").Trim().ToLowerInvariant();
        if (!ArtText.ContainsKey(art)) throw new ArgumentException("Bitte die Art der Sperre wählen.");
        var ziel = art is AlleRaeume or AlleRessourcen ? "" : (d.Ziel ?? "").Trim();
        if (ziel.Length == 0 && art is Raum or Raumgruppe or Ressource) throw new ArgumentException($"Bitte {(art == Raum ? "einen Raum" : art == Raumgruppe ? "eine Raumgruppe" : "eine Ressource")} wählen.");
        if (!DateOnly.TryParse(d.Von, Inv, out var von) || !DateOnly.TryParse(d.Bis, Inv, out var bis)) throw new ArgumentException("Bitte Beginn- und Enddatum angeben.");
        var beginn = string.IsNullOrWhiteSpace(d.Beginn) ? "00:00" : d.Beginn.Trim();
        var ende = string.IsNullOrWhiteSpace(d.Ende) ? "23:59" : d.Ende.Trim();
        if (!Uhrzeit.IsMatch(beginn) || !Uhrzeit.IsMatch(ende)) throw new ArgumentException("Bitte Uhrzeiten im Format HH:MM angeben.");
        var kont = d.Kontinuierlich ?? true;
        if (von.ToDateTime(TimeOnly.Parse(beginn, Inv)) >= bis.ToDateTime(TimeOnly.Parse(ende, Inv))) throw new ArgumentException("Das Ende muss nach dem Beginn liegen.");
        if (!kont && string.CompareOrdinal(beginn, ende) >= 0) throw new ArgumentException("Die Endzeit muss nach der Beginnzeit liegen.");
        var tag = d.Wochentag is int w and >= 0 and <= 6 ? w : -1;
        var text = (d.Text ?? "").Trim();
        if (text.Length > 500) throw new ArgumentException("Der Text darf höchstens 500 Zeichen lang sein.");
        var p = new[] { Db.P("@a", art), Db.P("@z", ziel), Db.P("@v", von.ToString("yyyy-MM-dd", Inv)), Db.P("@b", bis.ToString("yyyy-MM-dd", Inv)), Db.P("@be", beginn),
            Db.P("@en", ende), Db.P("@k", kont ? 1 : 0), Db.P("@w", tag), Db.P("@t", text) };
        if (id is int vorhanden)
        {
            if (Holen(vorhanden) is null) throw new ArgumentException("Unbekannte Sperre.");
            _db.Ausfuehren("UPDATE raumsperren SET art = @a, ziel = @z, von = @v, bis = @b, beginn = @be, ende = @en, kontinuierlich = @k, wochentag = @w, text = @t WHERE id = @id",
                p.Append(Db.P("@id", vorhanden)).ToArray());
            return Holen(vorhanden)!;
        }
        var neu = (int)_db.Einfuegen("""
            INSERT INTO raumsperren (art, ziel, von, bis, beginn, ende, kontinuierlich, wochentag, text, erstellt_von, erstellt)
            VALUES (@a, @z, @v, @b, @be, @en, @k, @w, @t, @u, @e)
            """, p.Concat(new[] { Db.P("@u", ich.Login), Db.P("@e", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm", Inv)) }).ToArray());
        return Holen(neu)!;
    }

    public void Loeschen(int id) => _db.Ausfuehren("DELETE FROM raumsperren WHERE id = @id", Db.P("@id", id));

    private static int Minuten(string hhmm) => int.Parse(hhmm[..2], Inv) * 60 + int.Parse(hhmm[3..5], Inv);

    /// <summary>Gilt die Sperre am Tag <paramref name="d"/> im Zeitraum <paramref name="beginn"/>–<paramref name="ende"/> (HH:mm)?</summary>
    public static bool Gilt(Raumsperre x, DateOnly d, string beginn, string ende)
    {
        if (!DateOnly.TryParse(x.Von, Inv, out var von) || !DateOnly.TryParse(x.Bis, Inv, out var bis) || d < von || d > bis) return false;
        if (beginn.Length < 5 || ende.Length < 5) return false;
        int b = Minuten(beginn), e = Minuten(ende);
        if (x.Kontinuierlich)
        {
            // von Beginn (erster Tag) bis Ende (letzter Tag) ohne Unterbrechung
            var ab = d == von ? Minuten(x.Beginn) : 0;
            var bisZeit = d == bis ? Minuten(x.Ende) : 24 * 60;
            return b < bisZeit && ab < e;
        }
        if (x.Wochentag >= 0 && Plandienst.TagIndex(d) != x.Wochentag) return false;
        return b < Minuten(x.Ende) && Minuten(x.Beginn) < e;
    }

    /// <summary>Betrifft die Sperre den Raum (mit seiner Raumgruppe)?</summary>
    public static bool BetrifftRaum(Raumsperre x, string raum, string raumgruppe) => x.Art switch
    {
        Raum => Ci.Equals(x.Ziel, raum),
        Raumgruppe => raumgruppe.Length > 0 && Ci.Equals(x.Ziel, raumgruppe),
        AlleRaeume => true,
        _ => false,
    };

    public static bool BetrifftRessource(Raumsperre x, int ressource) =>
        x.Art == AlleRessourcen || (x.Art == Ressource && int.TryParse(x.Ziel, Inv, out var id) && id == ressource);

    /// <summary>Sperren, die am Tag gelten können (Datumsbereich).</summary>
    public List<Raumsperre> Am(DateOnly d)
    {
        var iso = d.ToString("yyyy-MM-dd", Inv);
        return _db.Liste("SELECT * FROM raumsperren WHERE von <= @d AND bis >= @d", Lesen, Db.P("@d", iso));
    }

    /// <summary>Beschreibung für Listen und Fehlermeldungen („Raum TH1 · 30.01.2023–30.01.2023 · 08:00–12:00 · montags“).</summary>
    public static string Beschreibung(Raumsperre x, Func<int, string>? ressourcenName = null)
    {
        var wer = x.Art switch
        {
            Raum => $"Raum {x.Ziel}", Raumgruppe => $"Raumgruppe {x.Ziel}", Ressource => ressourcenName?.Invoke(int.TryParse(x.Ziel, out var i) ? i : 0) ?? $"Ressource {x.Ziel}",
            _ => ArtText[x.Art],
        };
        var datum = x.Von == x.Bis ? Datum(x.Von) : $"{Datum(x.Von)} – {Datum(x.Bis)}";
        var zeit = x.Kontinuierlich ? $"{Datum(x.Von)} {x.Beginn} bis {Datum(x.Bis)} {x.Ende}" : $"{datum}, {x.Beginn}–{x.Ende} Uhr{(x.Wochentag >= 0 ? $", {Plandienst.Wochentage[x.Wochentag]}s" : "")}";
        return $"{wer}: {zeit}";
    }

    /// <summary>
    /// Karten „Gesperrt“ für den Raumplan: je Tag zusammenhängende gesperrte Stunden als ein Block (graue Fläche mit „Gesperrt“).
    /// </summary>
    public List<Karte> Karten(Stand s, string raum, string raumgruppe, IEnumerable<DateOnly> tage)
    {
        var res = new List<Karte>();
        var zeiten = s.Paket.Zeitraster.Stunden.Where(z => z.Beginn.Length >= 5 && z.Ende.Length >= 5).OrderBy(z => z.Nr).ToList();
        foreach (var d in tage)
        {
            var sperren = Am(d).Where(x => x.FuerRaeume && BetrifftRaum(x, raum, raumgruppe)).ToList();
            if (sperren.Count == 0) continue;
            var stunden = zeiten.Where(z => sperren.Any(x => Gilt(x, d, z.Beginn, z.Ende))).ToList();
            for (var i = 0; i < stunden.Count; i++)
            {
                var j = i;
                while (j + 1 < stunden.Count && stunden[j + 1].Nr == stunden[j].Nr + 1) j++;
                var a = stunden[i]; var b = stunden[j];
                var text = sperren.Select(x => x.Text).FirstOrDefault(t => t.Length > 0) ?? "";
                res.Add(new Karte(d.ToString("yyyy-MM-dd", Inv), a.Nr, b.Nr, a.Beginn, b.Ende, "gesperrt", "", null, "", "", new(), new(), new() { new Wert(raum) },
                    "Gesperrt", text.Length > 0 ? text : "Gesperrt", null, null, "") { OhneMarker = true });
                i = j;
            }
        }
        return res;
    }

    private static string Datum(string iso) => DateOnly.TryParse(iso, Inv, out var d) ? d.ToString("dd.MM.yyyy", Inv) : iso;
}
