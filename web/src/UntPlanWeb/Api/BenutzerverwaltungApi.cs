using UntPlanWeb.Dienste;

namespace UntPlanWeb.Api;

/// <summary>
/// Benutzerverwaltung der Administration (nur Administration):
/// <list type="bullet">
/// <item>Kontodetails mit Personenzuordnung und Anmeldehistorie, Sammelaktionen (sperren, Passwort zurücksetzen, Rolle setzen …).</item>
/// <item>Personen (Lehrkräfte, Schüler*innen, Erziehungsberechtigte, sonstiges Personal) mit Konto-Status; fehlende Konten anlegen.</item>
/// <item>Zugangsdaten als PDF (Schwarz-Weiß) bzw. CSV.</item>
/// </list>
/// </summary>
public static class BenutzerverwaltungApi
{
    public sealed record SammelDaten(string? Aktion, List<int>? Ids, string? Rolle);
    public sealed record KontenDaten(string? Art, List<string>? Schluessel, string? Klasse, string? Schema, string? Rolle);
    public sealed record ZugangsDaten(List<NeuesKonto>? Konten, string? Titel, string? Ausrichtung = null);

    private static Benutzer Admin(HttpContext c)
    {
        // Administration bzw. Recht „Benutzerverwaltung“ (Rechte-Modell)
        return RechtePruefung.Verlangen(c.Ich() ?? throw new NichtAngemeldetException(), Recht.AdminBenutzer, meldung: "Nur für die Administration.");
    }

    private static string[] LeitungKuerzel(Verwaltungsdienst vw) =>
        vw.Text("zuordnung", "leitungKuerzel").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

    private static object KontenErgebnis(List<NeuesKonto> neu) => new
    {
        anzahl = neu.Count,
        konten = neu.Select(k => new { k.Login, k.Passwort, k.Rolle, rollenname = Rollen.Name(k.Rolle), k.Name, k.Zuordnung, k.Klasse }),
        csv = Personendienst.Csv(neu),
    };

    public static void Abbilden(WebApplication app)
    {
        var api = app.MapGroup("/api/app/admin");

        // Rollen, Zusatzrechte und Login-Schemata für die Oberfläche
        api.MapGet("/benutzerverwaltung/grundlagen", (HttpContext c) =>
        {
            Admin(c);
            return Results.Json(new
            {
                rollen = Rollen.Alle.Select(r => new { id = r, name = Rollen.Name(r) }),
                zusatzrechte = Zusatzrechte.Alle.Select(z => new { z.Id, z.Name, z.Beschreibung }),
                schemata = Personendienst.Schemata,
            });
        });

        // Kontodetails: Stammdaten, Zuordnung (Lehrkraft, Schüler*innen, Person) und Anmeldehistorie
        api.MapGet("/benutzer/{id:int}", (int id, HttpContext c, Benutzerdienst bd, Paketdienst p, Personendienst pd) =>
        {
            Admin(c);
            var b = bd.Holen(id) ?? throw new ArgumentException("Unbekannter Benutzer.");
            var s = p.Aktuell;
            var lehrer = b.LehrerKuerzel is string k && s != null && s.Lehrer.TryGetValue(k, out var l) ? new { kurz = l.Kurz, name = string.Join(" ", new[] { l.Vorname, l.Name }.Where(x => x.Length > 0)), email = l.Email } : null;
            var schueler = b.SchuelerIds.Select(i => s?.Schueler.FirstOrDefault(x => x.Id == i) is { } x ? new { id = i, name = $"{x.Name}, {x.Vorname}", klasse = x.Klasse } : new { id = i, name = $"#{i} (nicht mehr in der Schülerliste)", klasse = "" }).ToList();
            var person = b.PersonId is int pid ? pd.Liste(null).FirstOrDefault(x => x.Art is Personendienst.Eltern or Personendienst.Personal && x.Schluessel == pid.ToString()) : null;
            return Results.Json(new
            {
                benutzer = new
                {
                    b.Id, b.Login, b.Anzeigename, b.Rolle, rollenname = Rollen.Name(b.Rolle), b.Element, b.Email, b.Gesperrt, b.PasswortAendern, b.Erstellt,
                    b.LetzteAnmeldung, b.SchuelerIds, b.Klassen, b.Zusatzrechte, b.PersonId,
                },
                lehrkraft = lehrer,
                schueler,
                person = person is null ? null : new { art = person.Art, id = person.Schluessel, name = string.Join(" ", new[] { person.Vorname, person.Name }.Where(x => x.Length > 0)), person.Funktion },
                anmeldungen = bd.Anmeldungen(id, 30),
            });
        });

        // Sammelaktionen für ausgewählte Konten
        api.MapPost("/benutzer/sammelaktion", (SammelDaten d, HttpContext c, Benutzerdienst bd, Verwaltungsdienst vw, Protokoll prot) =>
        {
            var ich = Admin(c);
            var ids = (d.Ids ?? new()).Distinct().ToList();
            // ohne Administration: Administrationskonten bleiben unberührt
            if (!ich.IstAdmin) ids = ids.Where(i => bd.Holen(i) is { IstAdmin: false }).ToList();
            var aktion = d.Aktion ?? "";
            if (aktion == "rolle") RechtePruefung.AdminSchuetzen(ich, null, d.Rolle);
            if (ids.Count == 0 && aktion != "inaktive_sperren") throw new ArgumentException("Bitte zuerst Benutzer auswählen.");
            var neu = new List<NeuesKonto>();
            var n = 0;
            switch (aktion)
            {
                case "passwort_zuruecksetzen":
                    foreach (var id in ids)
                        if (bd.Holen(id) is { } b && b.Id != ich.Id)
                        {
                            var pw = Sicherheit.Zufall(10);
                            bd.PasswortSetzen(b.Id, pw, mussAendern: true);
                            neu.Add(new NeuesKonto(b.Login, pw, b.Rolle, b.Anzeigename, b.Element, string.Join(", ", b.Klassen)));
                            n++;
                        }
                    break;
                case "passwort_aendern":
                    foreach (var id in ids)
                        if (bd.Holen(id) is { } b) { bd.PasswortAendernErzwingen(b.Id); n++; }
                    break;
                case "rolle":
                    if (!Rollen.Gueltig(d.Rolle ?? "")) throw new ArgumentException("Bitte eine Rolle wählen.");
                    foreach (var id in ids)
                        if (bd.Holen(id) is { } b && b.Id != ich.Id && b.Rolle != d.Rolle)
                        {
                            // eindeutige Lehrerkürzel bleiben gewahrt
                            if (d.Rolle is Rollen.Admin or Rollen.Leitung or Rollen.Lehrer && !b.Kollegium) bd.ZuordnungPruefen(b.Id, d.Rolle!, b.Element, null);
                            bd.RolleSetzen(b.Id, d.Rolle!);
                            n++;
                        }
                    break;
                default:
                    n = vw.Massenaktion(ich, aktion, ids);
                    break;
            }
            prot.Schreiben(ich.Login, "Benutzer: " + aktion + (aktion == "rolle" ? " " + d.Rolle : ""), n.ToString());
            return Results.Json(new { anzahl = n, konten = neu.Count > 0 ? KontenErgebnis(neu) : null });
        });

        // ---------------------------------------------------------------- Personen

        api.MapGet("/personen", (string? art, HttpContext c, Personendienst pd) =>
        {
            Admin(c);
            if (art != null && !Personendienst.Arten.Contains(art)) throw new ArgumentException("Unbekannte Personenart.");
            return Results.Json(pd.Liste(art));
        });
        api.MapPost("/personen", (PersonEingabe d, HttpContext c, Personendienst pd, Protokoll prot) =>
        {
            var ich = Admin(c);
            var p = pd.Anlegen(d);
            prot.Schreiben(ich.Login, "Person angelegt", $"{p.Name}, {p.Vorname}");
            return Results.Json(p);
        });
        api.MapPut("/personen/{id:int}", (int id, PersonEingabe d, HttpContext c, Personendienst pd) => { Admin(c); return Results.Json(pd.Aendern(id, d)); });
        api.MapDelete("/personen/{id:int}", (int id, HttpContext c, Personendienst pd, Protokoll prot) =>
        {
            var ich = Admin(c);
            pd.Loeschen(id);
            prot.Schreiben(ich.Login, "Person gelöscht", id.ToString());
            return Results.Json(new { ok = true });
        });

        // Konten für Personen ohne Konto anlegen (einzeln, ausgewählte oder alle einer Art bzw. Klasse)
        api.MapPost("/personen/konten", (KontenDaten d, HttpContext c, Personendienst pd, Verwaltungsdienst vw, Protokoll prot) =>
        {
            var ich = Admin(c);
            RechtePruefung.AdminSchuetzen(ich, null, d.Rolle);
            var neu = pd.KontenAnlegen(d.Art ?? "", d.Schluessel, d.Klasse, d.Schema, d.Rolle, LeitungKuerzel(vw));
            prot.Schreiben(ich.Login, "Konten für Personen angelegt", $"{d.Art}: {neu.Count}");
            return Results.Json(KontenErgebnis(neu));
        });

        // Zugangsdaten als PDF (Schwarz-Weiß, zum Ausdrucken und Verteilen)
        api.MapPost("/personen/zugangsdaten.pdf", (ZugangsDaten d, HttpContext c, Einstellungen e) =>
        {
            var ich = Admin(c);
            var konten = d.Konten ?? new();
            if (konten.Count == 0) throw new ArgumentException("Keine Zugangsdaten.");
            var adresse = $"{c.Request.Scheme}://{c.Request.Host}{c.Request.PathBase}";
            var pdf = PdfBericht.Erzeugen(e.Schulname, string.IsNullOrWhiteSpace(d.Titel) ? "Zugangsdaten" : d.Titel!, $"Anmeldung unter {adresse}",
                new[] { "Name", "Klasse / Kürzel", "Rolle", "Benutzername", "Startpasswort" },
                konten.Select(k => (IReadOnlyList<string>)new[] { k.Name, k.Klasse.Length > 0 ? k.Klasse : k.Zuordnung, Rollen.Name(k.Rolle), k.Login, k.Passwort }).ToList(),
                "Das Startpasswort muss bei der ersten Anmeldung geändert werden. Bitte diese Liste vertraulich behandeln und nach dem Verteilen vernichten.",
                ich.Anzeigename.Length > 0 ? ich.Anzeigename : ich.Login, false, PdfBericht.Hochformat(d.Ausrichtung, true));
            return Results.File(pdf, "application/pdf", "zugangsdaten.pdf");
        });
    }
}
