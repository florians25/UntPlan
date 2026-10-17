namespace UntPlanStudio.Grundlagen;

/// <summary>
/// Ein Modul des Studios (Server-Seite). Jedes Modul liegt in einer eigenen Datei <c>Api/&lt;Modul&gt;Api.cs</c> und wird
/// zentral in <see cref="Api.Modulregister"/> eingetragen. Siehe ARCHITEKTUR.md, „Neues Modul anlegen“.
/// </summary>
public interface IStudioModul
{
    /// <summary>Kurzname des Moduls (für Protokoll und /api/module).</summary>
    string Name { get; }

    /// <summary>Eigene Dienste registrieren (optional). Konfiguration bei Bedarf per DI (<see cref="StudioKonfiguration"/>) holen.</summary>
    void DiensteRegistrieren(IServiceCollection dienste) { }

    /// <summary>Endpunkte registrieren: <see cref="StudioRouten.Api"/> (angemeldet) bzw. <see cref="StudioRouten.Dokument"/> (je Arbeitsdokument).</summary>
    void EndpunkteRegistrieren(StudioRouten routen);
}

/// <summary>
/// Routengruppen für Module:
/// <list type="bullet">
/// <item><see cref="Offen"/>: <c>/api</c> ohne Anmeldung (nur Anmeldung, Systeminfo).</item>
/// <item><see cref="Api"/>: <c>/api</c>, Anmeldung erforderlich.</item>
/// <item><see cref="Dokument"/>: <c>/api/dokumente/{dok}</c>, Anmeldung erforderlich; Handler erhalten das geöffnete
/// <see cref="Dienste.Arbeitsdokument"/> einfach als Parameter.</item>
/// </list>
/// </summary>
public sealed record StudioRouten(RouteGroupBuilder Offen, RouteGroupBuilder Api, RouteGroupBuilder Dokument);
