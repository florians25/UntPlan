using System.Diagnostics;
using System.Net.Http.Json;
using UntPlanWeb.Dienste;
using Xunit;

namespace UntPlanWeb.Tests;

/// <summary>
/// Hintergrundaufträge der Schulsimulation (Nachpflegen, Nachbessern): Fortschritt je Tag, Abbrechen nach dem aktuellen Tag, nacheinander statt gegenseitig
/// blockierend, kein hängendes „läuft“ nach einem Neustart, Laufzeit bei einer großen Schule.
/// </summary>
public class SimulationAuftraegeTests
{
    private static async Task<(Testserver S, Schulsimulation Sim, Benutzer Admin)> StartenAsync(System.Text.Json.Nodes.JsonObject? paket = null)
    {
        var s = await Testserver.StartenAsync();
        if (paket is not null) Assert.True((await s.PaketSendenAsync(paket)).IsSuccessStatusCode);
        var uhr = s.Dienst<Uhr>();
        var sim = s.Dienst<Schulsimulation>();
        var admin = s.Dienst<Benutzerdienst>().NachLogin("admin")!;
        uhr.Fest = new DateTime(2026, 9, 2, 6, 30, 0);
        s.Dienst<Einstellungen>().Schreiben("simulation.einstellungen", """{"seed":4711}""");
        sim.Speichern(admin, new Simulationseinstellungen { Aktiv = true });
        uhr.Fest = new DateTime(2026, 10, 15, 12, 0, 0);
        return (s, sim, admin);
    }

    private static async Task WartenAsync(Func<bool> fertig, int sekunden = 120)
    {
        var ende = DateTime.UtcNow.AddSeconds(sekunden);
        while (!fertig() && DateTime.UtcNow < ende) await Task.Delay(50);
        Assert.True(fertig(), "Zeitüberschreitung");
    }

    [Fact]
    public async Task Fortschritt_je_Tag_und_Abbrechen_nach_dem_aktuellen_Tag()
    {
        var (s, sim, admin) = await StartenAsync();
        await using var _ = s;
        // Fortschritt: Tage, bearbeitete Tage, aktueller Tag
        var tage = new HashSet<string>();
        var st = sim.NachpflegenStarten(admin, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 14));
        Assert.True(st.Laeuft);
        await WartenAsync(() => { if (sim.Nachpflegestatus.Tag.Length > 0) tage.Add(sim.Nachpflegestatus.Tag); return !sim.Nachpflegestatus.Laeuft; });
        var np = sim.Nachpflegestatus;
        Assert.Equal("", np.Fehler);
        Assert.False(np.Abgebrochen);
        Assert.True(np.Tage > 20);
        Assert.Equal(np.Tage, np.Fertig);
        Assert.True(tage.Count > 1, "der aktuelle Tag wird laufend gemeldet");

        // Abbrechen: nach dem aktuellen Tag, bereits bearbeitete Tage bleiben
        var api = await s.AlsAsync("admin");
        await api.PostAsJsonAsync("/api/app/admin/simulation/nachbessern", new { von = "2026-09-02", bis = "2027-07-16" });
        await WartenAsync(() => sim.Nachbesserungsstatus.Fertig >= 1 || !sim.Nachbesserungsstatus.Laeuft);
        Assert.True((await api.PostAsync("/api/app/admin/simulation/nachbessern/abbrechen", null)).IsSuccessStatusCode);
        await WartenAsync(() => !sim.Nachbesserungsstatus.Laeuft);
        var nb = sim.Nachbesserungsstatus;
        Assert.True(nb.Abgebrochen);
        Assert.True(nb.Fertig >= 1 && nb.Fertig < nb.Tage, $"{nb.Fertig} von {nb.Tage}");
        Assert.Equal("", nb.Fehler);
        var status = await Testserver.JsonAsync(await api.GetAsync("/api/app/admin/simulation"));
        Assert.True(status["nachbessern"]!["abgebrochen"]!.GetValue<bool>());
        Assert.Equal(nb.Tage, status["nachbessern"]!["tage"]!.GetValue<int>());
    }

    [Fact]
    public async Task Auftraege_laufen_nacheinander_und_der_Minutenlauf_kommt_dazwischen()
    {
        var (s, sim, admin) = await StartenAsync();
        await using var _ = s;
        sim.NachpflegenStarten(admin, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 14));
        await WartenAsync(() => sim.Nachpflegestatus.Fertig >= 1);
        var nb = sim.NachbessernStarten(admin, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 14));
        // das Nachbessern wartet sichtbar auf das Nachpflegen
        Assert.Equal("Vergangenheit nachpflegen", nb.Wartet);
        Assert.True(sim.Nachbesserungsstatus.Laeuft);
        // der Minutenlauf wird nicht bis zum Ende des Auftrags blockiert
        var sw = Stopwatch.StartNew();
        sim.Lauf();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"Minutenlauf {sw.Elapsed}");
        await WartenAsync(() => !sim.Nachpflegestatus.Laeuft && !sim.Nachbesserungsstatus.Laeuft);
        Assert.Equal(sim.Nachpflegestatus.Tage, sim.Nachpflegestatus.Fertig);
        Assert.Equal(sim.Nachbesserungsstatus.Tage, sim.Nachbesserungsstatus.Fertig);
        Assert.Equal("", sim.Nachbesserungsstatus.Wartet);
        // ein wartender Auftrag lässt sich abbrechen, bevor er beginnt
        sim.NachpflegenStarten(admin, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 14));
        sim.NachbessernStarten(admin, new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 14));
        sim.NachbessernAbbrechen(admin);
        sim.NachpflegenAbbrechen(admin);
        await WartenAsync(() => !sim.Nachpflegestatus.Laeuft && !sim.Nachbesserungsstatus.Laeuft);
        Assert.True(sim.Nachbesserungsstatus.Abgebrochen);
    }

    [Fact]
    public async Task Nach_einem_Neustart_haengt_kein_Auftrag_als_laufend()
    {
        var (s, sim, admin) = await StartenAsync();
        await using var _ = s;
        // gespeicherter Stand eines laufenden Auftrags (Server während des Nachbesserns beendet)
        s.Dienst<Einstellungen>().Schreiben("simulation.auftraege",
            """{"nachpflegen":{"laeuft":false,"von":"2026-09-02","bis":"2026-10-14","tage":30,"fertig":30,"eintraege":5,"uebersprungen":0,"beginn":"2026-10-15 08:00","ende":"2026-10-15 08:10","fehler":""},"nachbessern":{"laeuft":true,"von":"2026-09-02","bis":"2027-07-16","geprueft":10,"korrigiert":2,"entfernt":1,"beginn":"2026-10-15 09:00","ende":"","fehler":"","zusammenfassung":[],"tage":190,"fertig":12,"tag":"2026-09-18"}}""");
        sim.AuftraegeLaden(erneut: true);
        var nb = sim.Nachbesserungsstatus;
        Assert.False(nb.Laeuft);
        Assert.True(nb.Abgebrochen);
        Assert.Contains("Neustart", nb.Fehler);
        Assert.Contains("12 von 190", nb.Fehler);
        Assert.False(sim.Nachpflegestatus.Laeuft);
        Assert.Equal(30, sim.Nachpflegestatus.Fertig);
        // auch gespeichert: ein weiterer Neustart bleibt dabei
        sim.AuftraegeLaden(erneut: true);
        Assert.False(sim.Nachbesserungsstatus.Laeuft);
        // ein neuer Auftrag startet normal
        sim.NachbessernStarten(admin, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14));
        await WartenAsync(() => !sim.Nachbesserungsstatus.Laeuft);
        Assert.False(sim.Nachbesserungsstatus.Abgebrochen);
        Assert.Equal("", sim.Nachbesserungsstatus.Fehler);
    }

    [Fact]
    public async Task Grosse_Schule_Nachpflegen_und_Nachbessern_in_vertretbarer_Zeit()
    {
        var (s, sim, admin) = await StartenAsync(GrossesPaket.Erzeugen(6));
        await using var _ = s;
        var st = s.Dienst<Paketdienst>().Aktuell!;
        Assert.True(st.Klassen.Count >= 42 && st.Lehrer.Count >= 120);
        var sw = Stopwatch.StartNew();
        var np = sim.Nachpflegen(st, sim.Laden(), new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 15));
        var dauerNp = sw.Elapsed;
        Assert.True(np.Eintraege > 2000);
        // neuer Stand: Pläne nicht mehr zwischengespeichert (wie nach einem Neustart)
        var paket = GrossesPaket.Erzeugen(6);
        paket["revision"] = 3;
        Assert.True((await s.PaketSendenAsync(paket)).IsSuccessStatusCode);
        st = s.Dienst<Paketdienst>().Aktuell!;
        sw.Restart();
        var nb = sim.Nachbessern(st, sim.Laden(), new DateOnly(2026, 9, 2), new DateOnly(2027, 7, 16));
        var dauerNb = sw.Elapsed;
        Assert.Equal(nb.Tage, nb.Fertig);
        Assert.True(nb.Tage > 150);
        // Schwellen großzügig (Testumgebungen sind unterschiedlich schnell); vorher: rund 6 s je Tag Nachpflegen, 20 s Nachbessern bei einem Monat Daten
        Assert.True(dauerNp < TimeSpan.FromSeconds(60), $"Nachpflegen zwei Wochen: {dauerNp}");
        Assert.True(dauerNb < TimeSpan.FromSeconds(30), $"Nachbessern Schuljahr: {dauerNb}");
    }
}
