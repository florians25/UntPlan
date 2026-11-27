using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Administration → Schulbetrieb simulieren (nur Administration):
/// <list type="bullet">
/// <item><c>GET /api/app/admin/simulation</c> – Einstellungen, Auswahl (Lehrkräfte, Fächer) und Anzahl der simulierten Einträge.</item>
/// <item><c>PUT /api/app/admin/simulation</c> – Einstellungen speichern (Einschalten beginnt die Simulation jetzt).</item>
/// <item><c>POST /api/app/admin/simulation/lauf</c> – fällige Ereignisse sofort ausführen.</item>
/// <item><c>POST /api/app/admin/simulation/nachpflegen</c> – Vergangenheit nachpflegen (Hintergrundauftrag, Stand im GET).</item>
/// <item><c>POST /api/app/admin/simulation/nachpflegen/abbrechen</c>, <c>…/nachbessern/abbrechen</c>: Auftrag nach dem aktuellen Tag abbrechen.</item>
/// <item><c>POST /api/app/admin/simulation/entfernen</c> – alle simulierten Daten und Konten entfernen.</item>
/// </list>
/// </summary>
public static class SimulationApi
{
    public sealed record NachpflegenDaten(DateOnly? Von, DateOnly? Bis);

    private static Benutzer Admin(HttpContext c) =>
        c.Ich() is { } b ? b.IstAdmin ? b : throw new UnauthorizedAccessException("Nur für die Administration.") : throw new NichtAngemeldetException();

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/admin/simulation");
        api.MapGet("", (HttpContext c, Schulsimulation sim) => { Admin(c); return Results.Json(sim.Status()); });
        api.MapPut("", (Simulationseinstellungen d, HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            var vorher = sim.Laden().Aktiv;
            var e = sim.Speichern(b, d);
            if (vorher != e.Aktiv) prot.Schreiben(b.Login, e.Aktiv ? "Schulsimulation eingeschaltet" : "Schulsimulation ausgeschaltet");
            if (e.Aktiv) sim.Lauf();
            return Results.Json(sim.Status());
        });
        api.MapPost("/lauf", (HttpContext c, Schulsimulation sim) => { Admin(c); return Results.Json(sim.Lauf()); });
        api.MapPost("/nachpflegen", (NachpflegenDaten d, HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            var st = sim.NachpflegenStarten(b, d.Von, d.Bis);
            prot.Schreiben(b.Login, "Vergangenheit nachpflegen", $"{st.Von} bis {st.Bis}");
            return Results.Json(st);
        });
        api.MapPost("/nachbessern", (NachpflegenDaten d, HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            var st = sim.NachbessernStarten(b, d.Von, d.Bis);
            prot.Schreiben(b.Login, "Simulationsdaten nachbessern", $"{st.Von} bis {st.Bis}");
            return Results.Json(st);
        });
        api.MapPost("/nachpflegen/abbrechen", (HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            prot.Schreiben(b.Login, "Vergangenheit nachpflegen abgebrochen", "");
            return Results.Json(sim.NachpflegenAbbrechen(b));
        });
        api.MapPost("/nachbessern/abbrechen", (HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            prot.Schreiben(b.Login, "Simulationsdaten nachbessern abgebrochen", "");
            return Results.Json(sim.NachbessernAbbrechen(b));
        });
        api.MapPost("/entfernen", (HttpContext c, Schulsimulation sim, Protokoll prot) =>
        {
            var b = Admin(c);
            var n = sim.Entfernen(b);
            prot.Schreiben(b.Login, "Simulationsdaten entfernt", $"{n} Datensätze");
            return Results.Json(new { entfernt = n });
        });
    }
}
