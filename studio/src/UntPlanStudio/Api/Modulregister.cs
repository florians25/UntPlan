using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>
/// Zentrale Liste aller Server-Module – die EINZIGE Stelle, an der ein neues Modul eingetragen wird.
/// Ein Modul = eine Datei <c>Api/&lt;Modul&gt;Api.cs</c> (Klasse mit <see cref="IStudioModul"/>), dazu im Browser
/// <c>wwwroot/js/module/&lt;modul&gt;.js</c> (eingetragen in <c>wwwroot/js/module/register.js</c>) und optional
/// <c>wwwroot/css/module/&lt;modul&gt;.css</c>. Details: ARCHITEKTUR.md.
/// </summary>
public static class Modulregister
{
    public static readonly IStudioModul[] Alle =
    {
        // Fundament
        new AnmeldungApi(),
        new BenutzerApi(),
        new EinstellungenApi(),
        new DokumenteApi(),
        // Fachmodule
        new StammdatenApi(),
        new UnterrichtApi(),
        new StundenplanApi(),
        new OptimierungApi(),
        new PlanungApi(),
        new ZeitwuenscheApi(),
        new DruckApi(),
        new EinstellungenSchuleApi(),
        new FormateApi(),
        new UebersichtApi(),
        new VertretungApi(),
        new VeranstaltungenApi(),
        new AbrechnungApi(),
        new AustauschApi(),
        new KommentareApi(),
        new KursplanungApi(),
        new MultiUserApi(),
        new PausenaufsichtApi(),
        new UntPlanWebApi(),
        // ── ERWEITERUNGSSTELLE: weitere Module hier eintragen, je Modul eine Zeile (alphabetisch), z. B.
        // new AbrechnungApi(),
        // new DruckApi(),
        // new MultiUserApi(),
        // new OptimierungApi(),
        // new UntPlanWebApi(),
        // new VeranstaltungenApi(),
        // new VertretungApi(),
    };
}
