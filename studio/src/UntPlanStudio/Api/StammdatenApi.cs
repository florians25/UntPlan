using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record FeldInfo(string Schluessel, string Titel, string Karteikarte, string Art, string? RefTyp, string[] Auswahl, bool Standard, int Breite, bool Editierbar, string Hinweis);
public sealed record ElementZeile(int Id, string[] Werte, string? Hintergrund);
public sealed record Feldaenderung(int Id, string Feld, string? Wert);
public sealed record Feldaenderungen(List<Feldaenderung> Aenderungen);
public sealed record EinzelAenderung(string Feld, string? Wert);
public sealed record NeuesElement(string Kurzname, Dictionary<string, string?>? Werte);
public sealed record IdListe(List<int> Ids);
public sealed record Auswahleintrag(int Id, string K, string L);

/// <summary>
/// Stammdaten (Klassen, Lehrkräfte, Räume, Fächer, Raumgruppen, Gänge, Abteilungen, Abwesenheitsgründe, Schüler):
/// Felder, Tabelle, Werte ändern (einzeln oder mehrere in einer Rückgängig-Stufe), Anlegen, Löschen, Auswahllisten.
/// Werte werden als Text übertragen – genau wie im Desktop eingegeben (z. B. Bereich „1-2“, Liste „5a,5b“, Bool „X“).
/// </summary>
public sealed class StammdatenApi : IStudioModul
{
    public string Name => "stammdaten";

    public static string? Farbe(int argb) => argb == 0 ? null : $"#{argb & 0xFFFFFF:x6}";

    public static FeldInfo Info(Feld f) => new(f.Schluessel, f.Titel, f.Karteikarte, f.Art.ToString(),
        f.Art is FeldArt.Element or FeldArt.ElementListe ? Typnamen.Schluessel(f.RefTyp) : null, f.Auswahl, f.Standard, f.Breite,
        f.Editierbar || f.Schluessel == "Kurzname", f.Hinweis);

    public static ElementZeile Zeile(Element e, List<Feld> felder, DatenIndex ix) =>
        new(e.Id, felder.Select(f => f.Anzeige(e, ix)).ToArray(), Farbe(e.Hintergrund));

    /// <summary>Setzt einen Feldwert (Text wie im Desktop). Kurzname = Umbenennen mit Prüfung auf Eindeutigkeit.</summary>
    public static void Setzen(Schuldatei d, List<Feld> felder, ElementTyp typ, int id, string feld, string? wert)
    {
        var e = d.AlleElemente(typ).FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(typ)} Nr. {id}");
        if (feld == "Kurzname")
        {
            Stammdaten.Umbenennen(d, e, wert ?? "");
            return;
        }
        var f = felder.FirstOrDefault(x => x.Schluessel == feld) ?? throw StudioFehler.Ungueltig($"Unbekanntes Feld \"{feld}\".");
        if (!f.Editierbar) throw StudioFehler.Ungueltig($"Das Feld \"{f.Titel}\" kann nicht geändert werden.");
        f.Set!(e, f.Parse(wert ?? "", d));
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/stammdaten/{typ}");

        g.MapGet("/felder", (Arbeitsdokument a, string typ) =>
            a.LesenAsync(doc => Felder.Fuer(Typnamen.Typ(typ), doc).Select(Info)));

        g.MapGet("", (Arbeitsdokument a, string typ) => a.LesenAsync(doc =>
        {
            var t = Typnamen.Typ(typ);
            var felder = Felder.Fuer(t, doc);
            var ix = doc.Index;
            return new
            {
                typ = Typnamen.Schluessel(t),
                titel = Typnamen.Name(t, true),
                felder = felder.Select(Info),
                zeilen = Stammdaten.Sortiert(doc.Daten, t).Select(e => Zeile(e, felder, ix)),
            };
        }));

        g.MapPost("", (Arbeitsdokument a, string typ, NeuesElement n, ClaimsPrincipal p) =>
        {
            var t = Typnamen.Typ(typ);
            var id = 0;
            return a.AendernAsync(p.Benutzername(), $"{Typnamen.Name(t)} {n.Kurzname} angelegt", AenderungsArt.Stammdaten, d =>
            {
                var e = Stammdaten.Neu(d, t, n.Kurzname ?? "");
                id = e.Id;
                if (n.Werte is { Count: > 0 })
                {
                    var felder = Felder.Fuer(t, a.Dokument);
                    foreach (var (feld, wert) in n.Werte) Setzen(d, felder, t, id, feld, wert);
                }
            }, doc => Zeile(doc.Daten.AlleElemente(t).First(x => x.Id == id), Felder.Fuer(t, doc), doc.Index));
        });

        g.MapPatch("/{id:int}", (Arbeitsdokument a, string typ, int id, EinzelAenderung ae, ClaimsPrincipal p) =>
        {
            var t = Typnamen.Typ(typ);
            return a.AendernAsync(p.Benutzername(), $"{Typnamen.Name(t)}: {ae.Feld} geändert", AenderungsArt.Stammdaten,
                d => Setzen(d, Felder.Fuer(t, a.Dokument), t, id, ae.Feld, ae.Wert),
                doc => Zeile(doc.Daten.AlleElemente(t).First(x => x.Id == id), Felder.Fuer(t, doc), doc.Index));
        });

        // mehrere Werte in einer Rückgängig-Stufe (Einfügen aus der Zwischenablage, Serienänderung)
        g.MapPost("/aendern", (Arbeitsdokument a, string typ, Feldaenderungen ae, ClaimsPrincipal p) =>
        {
            var t = Typnamen.Typ(typ);
            if (ae.Aenderungen is not { Count: > 0 }) throw StudioFehler.Ungueltig("Keine Änderungen übergeben.");
            var ids = ae.Aenderungen.Select(x => x.Id).Distinct().ToList();
            return a.AendernAsync(p.Benutzername(), $"{Typnamen.Name(t, true)}: {ae.Aenderungen.Count} Werte geändert", AenderungsArt.Stammdaten, d =>
            {
                var felder = Felder.Fuer(t, a.Dokument);
                foreach (var x in ae.Aenderungen) Setzen(d, felder, t, x.Id, x.Feld, x.Wert);
            }, doc =>
            {
                var felder = Felder.Fuer(t, doc);
                return doc.Daten.AlleElemente(t).Where(e => ids.Contains(e.Id)).Select(e => Zeile(e, felder, doc.Index)).ToList();
            });
        });

        g.MapPost("/loeschen", (Arbeitsdokument a, string typ, IdListe l, ClaimsPrincipal p) =>
        {
            var t = Typnamen.Typ(typ);
            var anzahl = 0;
            return a.AendernAsync(p.Benutzername(), $"{(l.Ids?.Count ?? 0)} {Typnamen.Name(t, true)} gelöscht", AenderungsArt.Stammdaten, d =>
            {
                foreach (var id in l.Ids ?? new())
                    if (d.AlleElemente(t).FirstOrDefault(x => x.Id == id) is { } e) { Stammdaten.Loeschen(d, e); anzahl++; }
                if (anzahl == 0) throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)}");
            }, _ => new { geloescht = anzahl });
        });

        g.MapDelete("/{id:int}", (Arbeitsdokument a, string typ, int id, ClaimsPrincipal p) =>
        {
            var t = Typnamen.Typ(typ);
            return a.AendernAsync(p.Benutzername(), $"{Typnamen.Name(t)} gelöscht", AenderungsArt.Stammdaten, d =>
            {
                var e = d.AlleElemente(t).FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"{Typnamen.Name(t)} Nr. {id}");
                Stammdaten.Loeschen(d, e);
            }, _ => new { geloescht = 1 });
        });

        // Anzahl Unterrichte, in denen die Elemente vorkommen (Hinweis vor dem Löschen)
        g.MapGet("/verwendung", (Arbeitsdokument a, string typ, string? ids) => a.LesenAsync(doc =>
        {
            var t = Typnamen.Typ(typ);
            var liste = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var i) ? i : 0).ToHashSet();
            return new { unterrichte = doc.Daten.AlleElemente(t).Where(e => liste.Contains(e.Id)).Sum(e => Stammdaten.Verwendung(doc.Daten, e)) };
        }));

        // Auswahllisten (Kurz-/Langname) für Eingabefelder, Kombinationsfelder und Stundenplan-Fenster
        r.Dokument.MapGet("/elemente", (Arbeitsdokument a, string? typen) => a.LesenAsync(doc =>
        {
            var gewuenscht = string.IsNullOrWhiteSpace(typen)
                ? new[] { ElementTyp.Klasse, ElementTyp.Lehrer, ElementTyp.Raum, ElementTyp.Fach, ElementTyp.Raumgruppe, ElementTyp.Gang, ElementTyp.Abteilung, ElementTyp.Abwesenheitsgrund }
                : typen.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Typnamen.Typ(x.Trim())).ToArray();
            return gewuenscht.Distinct().ToDictionary(Typnamen.Schluessel, t => Stammdaten.Sortiert(doc.Daten, t).Select(e =>
                new Auswahleintrag(e.Id, e.Kurzname, e is Lehrer l && l.Vorname.Length > 0 ? $"{l.Langname}, {l.Vorname}" : e.Langname)).ToList());
        }));
    }
}
