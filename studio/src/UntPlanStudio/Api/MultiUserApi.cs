using System.Security.Claims;
using UntPlan.Core.MultiUser;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record MuLebenszeichen(string? Taetigkeit, int? NachrichtenNach);
public sealed record MuBereich(string Bereich);
public sealed record MuExklusiv(bool Ein);
public sealed record MuNachrichtEingabe(string? An, string Text);
public sealed record MuGruppeDaten(string Name, List<string>? Rechte, Dictionary<string, string>? Stufen);

/// <summary>
/// Modul MultiUser: Anbindung von <c>UntPlan.Core.MultiUser</c> an den Benutzerdienst des Studios. Mehrere Benutzer arbeiten
/// gleichzeitig am selben Arbeitsdokument; dieses Modul zeigt, wer gerade aktiv ist (mit Tätigkeit), verwaltet Bereichssperren
/// und den Exklusivbetrieb, Nachrichten an andere Benutzer und – für Administratoren – Gruppen mit Rechten und Rechtestufen sowie
/// die Zuordnung der Benutzer. Ein Endpunktfilter auf allen Dokument-Endpunkten prüft jede ändernde Anfrage (Sperrhinweis 423,
/// fehlende Rechte 403) und vermerkt die Aktivität.
/// </summary>
public sealed class MultiUserApi : IStudioModul
{
    public string Name => "multiuser";

    public void DiensteRegistrieren(IServiceCollection d) => d.AddSingleton<MultiUserDienst>();

    private static object RechteDto(Rechte r, string gruppe) => new
    {
        gruppe, administrator = r.IstAdministrator, nurLesen = r.NurLesen, abteilung = r.Abteilung,
        rechte = Enum.GetValues<Recht>().Where(r.Hat).Select(x => x.ToString()),
        bereiche = Enum.GetValues<Rechtebereich>().Select(b => new { bereich = b.ToString(), text = Rechte.Text(b), stufe = r.StufeVon(b).ToString(), stufeText = Rechte.Text(r.StufeVon(b)), bearbeiten = r.DarfBearbeiten(b) }),
    };

    private static object Zustand(MultiUserDienst mu, Arbeitsdokument a, string benutzer, int nachrichtenNach)
    {
        var (r, gruppe) = mu.RechteVon(benutzer);
        var ex = mu.Exklusiv(a.Id);
        return new
        {
            ich = benutzer,
            rechte = RechteDto(r, gruppe),
            aktive = mu.Sitzungen(a.Id),
            sperren = mu.Sperren(a.Id),
            exklusiv = ex is { } e ? new { benutzer = e.Benutzer, anzeigename = e.Anzeigename, seit = e.Seit, eigen = e.Benutzer.Equals(benutzer, StringComparison.OrdinalIgnoreCase) } : null,
            nachrichten = mu.Nachrichten(a.Id, benutzer, nachrichtenNach),
            version = a.Version,
        };
    }

    private static Rechtebereich Bereich(string text) =>
        Enum.TryParse<Rechtebereich>(text, true, out var b) ? b : throw StudioFehler.Ungueltig($"Unbekannter Bereich „{text}“.");

    private static void NurAdmin(ClaimsPrincipal p)
    {
        if (!p.IsInRole(Rollen.Admin)) throw StudioFehler.Verboten("Nur Administratoren dürfen die MultiUser-Gruppen verwalten.");
    }

    private static Gruppe GruppeAus(int id, MuGruppeDaten e)
    {
        var g = new Gruppe { Id = id, Name = e.Name ?? "" };
        foreach (var x in e.Rechte ?? new())
            g.Rechte.Add(Enum.TryParse<Recht>(x, true, out var rr) ? rr : throw StudioFehler.Ungueltig($"Unbekanntes Recht „{x}“."));
        foreach (var b in Enum.GetValues<Rechtebereich>())
            g.Stufen[b] = e.Stufen != null && e.Stufen.TryGetValue(b.ToString(), out var s) && Enum.TryParse<Stufe>(s, true, out var st) ? st : Stufe.Keine;
        return g;
    }

    private static object Verwaltung(MultiUserDienst mu, Benutzerdienst benutzer)
    {
        var k = mu.Konfiguration();
        return new
        {
            gruppen = k.Gruppen.Select(g => new
            {
                id = g.Id, name = g.Name, rechte = g.Rechte.Select(x => x.ToString()),
                stufen = Enum.GetValues<Rechtebereich>().ToDictionary(b => b.ToString(), b => g.Stufen.GetValueOrDefault(b, Stufe.Keine).ToString()),
            }),
            rechteListe = Enum.GetValues<Recht>().Select(x => new { recht = x.ToString(), text = Rechte.Text(x) }),
            bereiche = Enum.GetValues<Rechtebereich>().Select(b => new { bereich = b.ToString(), text = Rechte.Text(b) }),
            stufen = Enum.GetValues<Stufe>().Select(s => new { stufe = s.ToString(), text = Rechte.Text(s) }),
            benutzer = benutzer.Alle().Select(b => new
            {
                name = b.Name, anzeigename = b.Anzeigename, rolle = b.Rolle,
                zuordnung = k.Zuordnung.TryGetValue(b.Name, out var z) ? z : null,
                wirksam = mu.RechteVon(b.Name).Gruppe,
            }),
            sperrTimeoutMinuten = k.SperrTimeoutMinuten,
        };
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        // ---- Prüfung aller ändernden Dokument-Anfragen + Aktivität (gilt für alle Endpunkte der Gruppe /api/dokumente/{dok}) ----
        r.Dokument.AddEndpointFilter(async (ctx, weiter) =>
        {
            var http = ctx.HttpContext;
            var dok = http.Request.RouteValues["dok"] as string;
            var benutzer = http.User.Identity?.IsAuthenticated == true ? http.User.Benutzername() : null;
            if (benutzer != null && Dokumentdienst.IdGueltig(dok))
            {
                var mu = http.RequestServices.GetRequiredService<MultiUserDienst>();
                var pfad = http.Request.Path.Value ?? "";
                var basis = $"/api/dokumente/{dok}";
                var rest = pfad.Length > basis.Length ? pfad[basis.Length..].Trim('/') : "";
                var teile = rest.Split('/');
                var segment = teile[0].ToLowerInvariant();
                var unter = teile.Length > 1 ? teile[1].ToLowerInvariant() : "";
                if (segment != "ereignisse") mu.Lebenszeichen(dok!, benutzer);
                if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && mu.Pruefen(dok!, benutzer, segment, unter) is { } fehler)
                    throw new StudioFehler(fehler.Status, fehler.Meldung);
                if (segment == "schliessen") mu.Abmelden(dok!, benutzer);
            }
            return await weiter(ctx);
        });

        // ---- je Dokument: aktive Benutzer, Sperren, Exklusivbetrieb, Nachrichten ----
        var g = r.Dokument.MapGroup("/multiuser");
        // GET, damit auch Nur-Lesen-Konten ihr Lebenszeichen (mit Tätigkeit) senden können
        g.MapGet("", (Arbeitsdokument a, ClaimsPrincipal p, MultiUserDienst mu, int? nach, string? taetigkeit) =>
        {
            if (taetigkeit?.Trim() is { Length: > 0 } t) mu.Lebenszeichen(a.Id, p.Benutzername(), t.Length > 80 ? t[..80] : t);
            return Zustand(mu, a, p.Benutzername(), nach ?? 0);
        });
        g.MapPost("/lebenszeichen", (Arbeitsdokument a, MuLebenszeichen e, ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            mu.Lebenszeichen(a.Id, p.Benutzername(), e.Taetigkeit?.Trim() is { Length: > 0 } t ? (t.Length > 80 ? t[..80] : t) : null);
            return Zustand(mu, a, p.Benutzername(), e.NachrichtenNach ?? 0);
        });
        g.MapPost("/abmelden", (Arbeitsdokument a, ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            mu.Abmelden(a.Id, p.Benutzername());
            return Results.Ok(new { ok = true });
        });
        g.MapPost("/sperren", (Arbeitsdokument a, MuBereich e, ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            mu.Sperren(a.Id, p.Benutzername(), Bereich(e.Bereich));
            return Zustand(mu, a, p.Benutzername(), int.MaxValue);
        });
        g.MapDelete("/sperren/{bereich}", (Arbeitsdokument a, string bereich, ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            mu.Freigeben(a.Id, p.Benutzername(), Bereich(bereich), p.IsInRole(Rollen.Admin));
            return Zustand(mu, a, p.Benutzername(), int.MaxValue);
        });
        g.MapPost("/exklusiv", (Arbeitsdokument a, MuExklusiv e, ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            mu.ExklusivSetzen(a.Id, p.Benutzername(), e.Ein, p.IsInRole(Rollen.Admin));
            return Zustand(mu, a, p.Benutzername(), int.MaxValue);
        });
        g.MapPost("/nachricht", (Arbeitsdokument a, MuNachrichtEingabe e, ClaimsPrincipal p, MultiUserDienst mu) =>
            mu.NachrichtSenden(a.Id, p.Benutzername(), e.An, e.Text));

        // ---- global: eigene Rechte, Verwaltung (Admin) ----
        var v = r.Api.MapGroup("/multiuser");
        v.MapGet("/ich", (ClaimsPrincipal p, MultiUserDienst mu) =>
        {
            var (rechte, gruppe) = mu.RechteVon(p.Benutzername());
            return RechteDto(rechte, gruppe);
        });
        v.MapGet("/verwaltung", (ClaimsPrincipal p, MultiUserDienst mu, Benutzerdienst b) =>
        {
            NurAdmin(p);
            return Verwaltung(mu, b);
        });
        v.MapPost("/gruppen", (MuGruppeDaten e, ClaimsPrincipal p, MultiUserDienst mu, Benutzerdienst b) =>
        {
            NurAdmin(p);
            mu.GruppeSpeichern(GruppeAus(0, e));
            return Verwaltung(mu, b);
        });
        v.MapPut("/gruppen/{id:int}", (int id, MuGruppeDaten e, ClaimsPrincipal p, MultiUserDienst mu, Benutzerdienst b) =>
        {
            NurAdmin(p);
            if (mu.Konfiguration().Gruppen.All(x => x.Id != id)) throw StudioFehler.NichtGefunden($"Die Gruppe Nr. {id}");
            mu.GruppeSpeichern(GruppeAus(id, e));
            return Verwaltung(mu, b);
        });
        v.MapDelete("/gruppen/{id:int}", (int id, ClaimsPrincipal p, MultiUserDienst mu, Benutzerdienst b) =>
        {
            NurAdmin(p);
            mu.GruppeLoeschen(id);
            return Verwaltung(mu, b);
        });
        v.MapPut("/benutzer/{name}", (string name, MuZuordnung z, ClaimsPrincipal p, MultiUserDienst mu, Benutzerdienst b) =>
        {
            NurAdmin(p);
            mu.Zuordnen(name, z);
            return Verwaltung(mu, b);
        });
    }
}
