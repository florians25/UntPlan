using System.Text.Json;

namespace UntPlanStudio.Grundlagen;

/// <summary>Fachlicher Fehler mit HTTP-Status und Meldung für die Oberfläche.</summary>
public sealed class StudioFehler : Exception
{
    public int Status { get; }

    public StudioFehler(int status, string meldung) : base(meldung) => Status = status;

    public static StudioFehler NichtGefunden(string was) => new(StatusCodes.Status404NotFound, $"{was} wurde nicht gefunden.");
    public static StudioFehler Ungueltig(string meldung) => new(StatusCodes.Status400BadRequest, meldung);
    public static StudioFehler Verboten(string meldung = "Dafür fehlt die Berechtigung.") => new(StatusCodes.Status403Forbidden, meldung);
}

/// <summary>
/// Einheitliche Fehlerantworten der API: <c>{ "fehler": "Meldung" }</c>. Fachliche Fehler aus UntPlan.Core
/// (InvalidOperationException, FormatException) werden als 400 mit der Meldung des Core gemeldet – wie im Desktop als Hinweis.
/// </summary>
public sealed class Fehlerbehandlung
{
    private readonly RequestDelegate _weiter;
    private readonly ILogger<Fehlerbehandlung> _log;

    public Fehlerbehandlung(RequestDelegate weiter, ILogger<Fehlerbehandlung> log)
    {
        _weiter = weiter;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _weiter(ctx);
        }
        catch (Exception ex) when (!ctx.Response.HasStarted && ex is not OperationCanceledException)
        {
            var (status, meldung) = ex switch
            {
                StudioFehler f => (f.Status, f.Message),
                BadHttpRequestException b => (b.StatusCode, "Ungültige Anfrage: " + b.Message),
                JsonException => (400, "Ungültige JSON-Daten."),
                InvalidOperationException or FormatException or InvalidDataException or ArgumentException or OverflowException => (400, ex.Message),
                _ => (500, "Interner Fehler: " + ex.Message),
            };
            if (status >= 500) _log.LogError(ex, "Fehler bei {Pfad}", ctx.Request.Path);
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { fehler = meldung });
        }
    }
}
