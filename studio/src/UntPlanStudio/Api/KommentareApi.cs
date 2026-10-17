using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record KommentarEingabe(string Typ, int Element, string? Feld, int Kategorie, string Text);
public sealed record KommentarIds(List<int> Ids);

/// <summary>
/// Modul Kommentare (Desktop: Kommentar-Dialog an Stammdaten-Feldern und „Kommentar-Übersicht“): Kommentare je Element und Feld
/// mit Kategorie (Farbe), Übersicht aller Kommentare, Anlegen, Bearbeiten, Löschen.
/// </summary>
public sealed class KommentareApi : IStudioModul
{
    public string Name => "kommentare";

    private static string Farbe(int argb) => argb == 0 ? "" : $"#{argb & 0xFFFFFF:x6}";

    private static List<object> Liste(Dokument doc, string? typ, int? element)
    {
        var d = doc.Daten;
        var t = string.IsNullOrEmpty(typ) ? (ElementTyp?)null : Typnamen.Typ(typ);
        return d.Kommentare.Where(k => (t is null || k.ElementTyp == t) && (element is null || k.ElementId == element)).OrderByDescending(k => k.Erstellt).Select(k =>
        {
            var kat = d.KommentarKategorien.FirstOrDefault(x => x.Nr == k.Kategorie);
            return (object)new
            {
                id = k.Id, kategorie = k.Kategorie, kategorieName = kat?.Name ?? $"Kategorie {k.Kategorie}", farbe = Farbe(kat?.Farbe ?? 0),
                typ = Typnamen.Schluessel(k.ElementTyp), typName = Typnamen.Name(k.ElementTyp), element = k.ElementId, elementName = doc.Index.K(k.ElementTyp, k.ElementId),
                feld = k.Feld, text = k.Text, erstellt = k.Erstellt.ToString("dd.MM.yyyy HH:mm"),
            };
        }).ToList();
    }

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/kommentare");

        g.MapGet("", (Arbeitsdokument a, string? typ, int? element) => a.LesenAsync(doc => Liste(doc, typ, element)));
        g.MapGet("/kategorien", (Arbeitsdokument a) => a.LesenAsync(doc =>
            (doc.Daten.KommentarKategorien.Count > 0 ? doc.Daten.KommentarKategorien : Enumerable.Range(1, 8).Select(i => new KommentarKategorie { Nr = i, Name = $"Kategorie {i}" }).ToList())
            .Select(k => new { nr = k.Nr, name = k.Name, farbe = Farbe(k.Farbe) }).ToList()));

        g.MapPost("", (Arbeitsdokument a, KommentarEingabe e, ClaimsPrincipal p) =>
        {
            if (string.IsNullOrWhiteSpace(e.Text)) throw StudioFehler.Ungueltig("Bitte einen Kommentartext eingeben.");
            var typ = Typnamen.Typ(e.Typ);
            return a.AendernAsync(p.Benutzername(), "Kommentar", AenderungsArt.Stammdaten, d =>
            {
                if (d.AlleElemente(typ).All(x => x.Id != e.Element)) throw StudioFehler.NichtGefunden($"{Typnamen.Name(typ)} Nr. {e.Element}");
                d.Kommentare.Add(new Kommentar { Id = d.NeueId(), ElementTyp = typ, ElementId = e.Element, Feld = e.Feld ?? "", Kategorie = Math.Clamp(e.Kategorie, 1, 8), Text = e.Text.Trim() });
            }, doc => Liste(doc, null, null));
        });

        g.MapPut("/{id:int}", (Arbeitsdokument a, int id, KommentarEingabe e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kommentar ändern", AenderungsArt.Stammdaten, d =>
            {
                var k = d.Kommentare.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Der Kommentar Nr. {id}");
                if (string.IsNullOrWhiteSpace(e.Text)) throw StudioFehler.Ungueltig("Bitte einen Kommentartext eingeben.");
                k.Text = e.Text.Trim();
                k.Kategorie = Math.Clamp(e.Kategorie, 1, 8);
                if (e.Feld != null) k.Feld = e.Feld;
            }, doc => Liste(doc, null, null)));

        g.MapPost("/loeschen", (Arbeitsdokument a, KommentarIds e, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kommentare löschen", AenderungsArt.Stammdaten, d => d.Kommentare.RemoveAll(k => e.Ids.Contains(k.Id)), doc => Liste(doc, null, null)));
    }
}
