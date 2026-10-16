using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Info der Schule für eine Zielgruppe (Infocenter), in der Datenbank gespeichert.</summary>
public sealed record Info(int Id, string Titel, string Text, string Zielgruppe, string? Von, string? Bis, bool Wichtig, string Erstellt, string Autor, string Geaendert);

/// <summary>Eintrag im Infocenter: Info der Schule oder Neuigkeit zu UntPlanWeb (neuigkeiten.json).</summary>
public sealed record InfocenterEintrag(string Schluessel, string Art, string Kategorie, string Titel, string Text, List<string> Punkte, string Datum,
    bool Wichtig, bool Gelesen, int? Id = null);

/// <summary>
/// Infocenter: Infos der Schule (Titel, Text, Zielgruppe alle/Lehrkräfte/Schüler*innen/Eltern, gültig von/bis, wichtig) und die
/// Neuigkeiten zu UntPlanWeb (wwwroot/neuigkeiten.json) in einer Liste. Gelesen wird je Benutzer in der Datenbank vermerkt
/// (geräteübergreifend). Anlegen, Bearbeiten und Löschen von Infos nur mit Recht „Nachrichten“ (Administration, Schulleitung).
/// </summary>
public sealed class Infocenterdienst
{
    public static readonly string[] Zielgruppen = { "alle", "kollegium", "schueler", "eltern" };

    private readonly Db _db;
    private readonly Uhr _uhr;
    private readonly string? _neuigkeiten;

    public Infocenterdienst(Db db, Uhr uhr, string? neuigkeitenDatei) { _db = db; _uhr = uhr; _neuigkeiten = neuigkeitenDatei; }

    private static Info Lesen(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("titel"), r.Text("text"), r.Text("zielgruppe"),
        r.TextOderNull("von"), r.TextOderNull("bis"), r.Wahr("wichtig"), r.Text("erstellt"), r.Text("autor"), r.Text("geaendert"));

    /// <summary>Alle Infos (Verwaltung), neueste zuerst.</summary>
    public List<Info> Alle() => _db.Liste("SELECT * FROM infos ORDER BY erstellt DESC, id DESC", Lesen);

    public Info? Holen(int id) => _db.Einzel("SELECT * FROM infos WHERE id = @i", Lesen, Db.P("@i", id));

    /// <summary>Gilt die Zielgruppe für diesen Benutzer?</summary>
    public static bool FuerBenutzer(string zielgruppe, Benutzer b) => zielgruppe switch
    {
        "kollegium" => b.Kollegium,
        "schueler" => b.Rolle == Rollen.Schueler,
        "eltern" => b.Rolle == Rollen.Eltern,
        _ => true,
    };

    private static (string Titel, string Text, string Zielgruppe, string? Von, string? Bis) Pruefen(string? titel, string? text, string? zielgruppe, DateOnly? von, DateOnly? bis)
    {
        var t = (titel ?? "").Trim();
        if (t.Length == 0) throw new ArgumentException("Bitte einen Titel angeben.");
        if (t.Length > 200) throw new ArgumentException("Der Titel ist zu lang (höchstens 200 Zeichen).");
        var x = (text ?? "").Trim();
        if (x.Length > 4000) throw new ArgumentException("Der Text ist zu lang (höchstens 4000 Zeichen).");
        var z = string.IsNullOrWhiteSpace(zielgruppe) ? "alle" : zielgruppe.Trim().ToLowerInvariant();
        if (!Zielgruppen.Contains(z)) throw new ArgumentException("Unbekannte Zielgruppe.");
        if (von is { } v && bis is { } bi && bi < v) throw new ArgumentException("„Gültig bis“ liegt vor „gültig von“.");
        return (t, x, z, von?.ToString("yyyy-MM-dd"), bis?.ToString("yyyy-MM-dd"));
    }

    public Info Anlegen(Benutzer autor, string? titel, string? text, string? zielgruppe, DateOnly? von, DateOnly? bis, bool wichtig)
    {
        var p = Pruefen(titel, text, zielgruppe, von, bis);
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");
        var id = _db.Einfuegen("INSERT INTO infos (titel, text, zielgruppe, von, bis, wichtig, erstellt, autor, geaendert) VALUES (@t, @x, @z, @v, @b, @w, @e, @a, @e)",
            Db.P("@t", p.Titel), Db.P("@x", p.Text), Db.P("@z", p.Zielgruppe), Db.P("@v", p.Von), Db.P("@b", p.Bis), Db.P("@w", wichtig ? 1 : 0),
            Db.P("@e", jetzt), Db.P("@a", autor.Anzeigename.Length > 0 ? autor.Anzeigename : autor.Login));
        return Holen((int)id)!;
    }

    public Info Aendern(int id, string? titel, string? text, string? zielgruppe, DateOnly? von, DateOnly? bis, bool wichtig)
    {
        if (Holen(id) is null) throw new ArgumentException("Die Info gibt es nicht (mehr).");
        var p = Pruefen(titel, text, zielgruppe, von, bis);
        _db.Ausfuehren("UPDATE infos SET titel = @t, text = @x, zielgruppe = @z, von = @v, bis = @b, wichtig = @w, geaendert = @g WHERE id = @i",
            Db.P("@t", p.Titel), Db.P("@x", p.Text), Db.P("@z", p.Zielgruppe), Db.P("@v", p.Von), Db.P("@b", p.Bis), Db.P("@w", wichtig ? 1 : 0),
            Db.P("@g", _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss")), Db.P("@i", id));
        // geänderte Info gilt wieder als ungelesen
        _db.Ausfuehren("DELETE FROM infocenter_gelesen WHERE schluessel = @s", Db.P("@s", "i:" + id));
        return Holen(id)!;
    }

    public bool Loeschen(int id)
    {
        _db.Ausfuehren("DELETE FROM infocenter_gelesen WHERE schluessel = @s", Db.P("@s", "i:" + id));
        return _db.Ausfuehren("DELETE FROM infos WHERE id = @i", Db.P("@i", id)) > 0;
    }

    /// <summary>Schlüssel höchstens 250 Zeichen (Spalte infocenter_gelesen.schluessel).</summary>
    private static string Kurz(string s) => s.Length <= 250 ? s : s[..250];

    private sealed record Neuigkeit(string? Id, string Datum, string Titel, List<string>? Punkte);

    /// <summary>Neuigkeiten zu UntPlanWeb aus neuigkeiten.json (Schlüssel wie bisher im Browser: Id bzw. „Datum|Titel“).</summary>
    private List<InfocenterEintrag> Neuigkeiten()
    {
        if (_neuigkeiten is null || !File.Exists(_neuigkeiten)) return new();
        try
        {
            var l = JsonSerializer.Deserialize<List<Neuigkeit>>(File.ReadAllText(_neuigkeiten), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
            return l.Where(n => !string.IsNullOrWhiteSpace(n.Titel)).Select(n => new InfocenterEintrag(Kurz("n:" + (n.Id is { Length: > 0 } id ? id : $"{n.Datum}|{n.Titel}")),
                "neuigkeit", "Update", n.Titel, "", n.Punkte ?? new(), n.Datum, false, false)).ToList();
        }
        catch (JsonException) { return new(); }
    }

    /// <summary>Einträge für einen Benutzer: gültige Infos seiner Zielgruppe (wichtige zuerst) und die Neuigkeiten, je mit Gelesen-Vermerk.</summary>
    public List<InfocenterEintrag> Fuer(Benutzer b)
    {
        var heute = _uhr.Heute.ToString("yyyy-MM-dd");
        var gelesen = _db.Liste("SELECT schluessel FROM infocenter_gelesen WHERE benutzer_id = @b", r => r.Text("schluessel"), Db.P("@b", b.Id)).ToHashSet();
        var infos = Alle().Where(i => FuerBenutzer(i.Zielgruppe, b) && (i.Von is null || string.CompareOrdinal(i.Von, heute) <= 0) && (i.Bis is null || string.CompareOrdinal(i.Bis, heute) >= 0))
            .Select(i => new InfocenterEintrag("i:" + i.Id, "info", i.Wichtig ? "Wichtig" : "Info", i.Titel, i.Text, new(), i.Von ?? i.Erstellt[..Math.Min(10, i.Erstellt.Length)],
                i.Wichtig, gelesen.Contains("i:" + i.Id), i.Id));
        var neu = Neuigkeiten().Select(n => n with { Gelesen = gelesen.Contains(n.Schluessel) });
        return infos.OrderByDescending(i => i.Wichtig && !i.Gelesen).ThenByDescending(i => i.Datum)
            .Concat(neu.OrderByDescending(n => n.Datum)).ToList();
    }

    /// <summary>Einträge als gelesen vermerken (nur Schlüssel, die es für den Benutzer gibt); ohne Angabe alle.</summary>
    public int GelesenMarkieren(Benutzer b, IEnumerable<string>? schluessel)
    {
        var vorhanden = Fuer(b).Where(x => !x.Gelesen).Select(x => x.Schluessel).ToHashSet();
        var liste = schluessel is null ? vorhanden.ToList() : schluessel.Where(vorhanden.Contains).Distinct().ToList();
        var jetzt = _uhr.Jetzt.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var s in liste)
            _db.Ausfuehren("INSERT OR IGNORE INTO infocenter_gelesen (benutzer_id, schluessel, zeit) VALUES (@b, @s, @z)", Db.P("@b", b.Id), Db.P("@s", s), Db.P("@z", jetzt));
        return liste.Count;
    }
}
