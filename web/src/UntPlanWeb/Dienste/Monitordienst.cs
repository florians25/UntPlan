using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

public sealed record Monitor(int Id, string Name, string Token, string Version, int Tage);

/// <summary>Monitoransichten: öffentlicher Vertretungsplan für Info-Bildschirme über einen geheimen Link (ohne Anmeldung).</summary>
public sealed class Monitordienst
{
    private readonly Db _db;

    public Monitordienst(Db db) => _db = db;

    private static Monitor Lesen(System.Data.Common.DbDataReader r) => new(r.Zahl("id"), r.Text("name"), r.Text("token"), r.Text("version"), r.Zahl("tage"));

    public List<Monitor> Alle() => _db.Liste("SELECT * FROM monitore ORDER BY name", Lesen);
    public Monitor? NachToken(string token) => _db.Einzel("SELECT * FROM monitore WHERE token = @t", Lesen, Db.P("@t", token ?? ""));

    public Monitor Speichern(int? id, string name, string version, int tage)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Bitte einen Namen eingeben.");
        if (version is not ("schueler" or "lehrer")) throw new ArgumentException("Unbekannte Version.");
        tage = Math.Clamp(tage, 1, 5);
        if (id is null)
        {
            var token = Sicherheit.Zufall(24);
            var neu = _db.Einfuegen("INSERT INTO monitore (name, token, version, tage, erstellt) VALUES (@n, @t, @v, @g, datetime('now'))",
                Db.P("@n", name.Trim()), Db.P("@t", token), Db.P("@v", version), Db.P("@g", tage));
            return Alle().First(m => m.Id == neu);
        }
        _db.Ausfuehren("UPDATE monitore SET name = @n, version = @v, tage = @g WHERE id = @id", Db.P("@n", name.Trim()), Db.P("@v", version), Db.P("@g", tage), Db.P("@id", id));
        return Alle().First(m => m.Id == id);
    }

    public void Loeschen(int id) => _db.Ausfuehren("DELETE FROM monitore WHERE id = @id", Db.P("@id", id));
}
