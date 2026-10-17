using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

public sealed record SpaltenInfo(string Schluessel, string Titel, int Breite, bool Zeile, bool Standard, bool Bool, bool Editierbar, string Karte, string? RefTyp, bool Liste);
public sealed record UnterrichtZeile(int Id, int Nvpl, int Wst, string[][] Zeilen);
public sealed record UnterrichtAenderung(string Feld, string? Wert, int Zeile = 0);
public sealed record NeuerUnterricht(string? Sicht, int? Element, Dictionary<string, string?>? Werte);

/// <summary>
/// Unterricht der aktiven Periode: Tabelle je Klasse/Lehrkraft/Fach bzw. alle, Werte ändern, Anlegen, Kopplungszeilen,
/// Kopieren, Löschen. Spalten und Eingabelogik wie im Desktop (<see cref="Unterrichtsspalten"/>).
/// </summary>
public sealed class UnterrichtApi : IStudioModul
{
    public string Name => "unterricht";

    public static SpaltenInfo Info(Spalte s) => new(s.Key, s.Titel, s.Breite, s.Zeile, s.Standard, s.Bool, s.Set != null, s.Karte,
        s.RefTyp is { } t ? Typnamen.Schluessel(t) : null, s.Liste);

    public static UnterrichtZeile Zeile(Unterricht u, List<Spalte> spalten, DatenIndex ix) =>
        new(u.Id, ix.NichtVerplant(u), u.Wochenstunden, u.Zeilen.Select(z => spalten.Select(s => s.Get(u, z)).ToArray()).ToArray());

    private static Unterricht Suchen(Schuldatei d, int id) =>
        d.Unterrichte.FirstOrDefault(u => u.Id == id) ?? throw StudioFehler.NichtGefunden($"Der Unterricht U-Nr. {id}");

    private static IEnumerable<Unterricht> Auswahl(Dokument doc, ElementTyp? sicht, int? element) =>
        sicht is null or ElementTyp.Unterricht ? doc.Daten.Unterrichte.OrderBy(u => u.Id)
        : element is int id ? doc.Index.UnterrichteVon(sicht.Value, id).OrderBy(u => u.Id) : Enumerable.Empty<Unterricht>();

    private static ElementTyp? Sicht(string? s) => string.IsNullOrWhiteSpace(s) || s.Equals("alle", StringComparison.OrdinalIgnoreCase) ? null : Typnamen.Typ(s);

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/unterricht");

        g.MapGet("/spalten", (Arbeitsdokument a) => a.LesenAsync(doc => Unterrichtsspalten.Fuer(doc).Select(Info)));

        g.MapGet("", (Arbeitsdokument a, string? sicht, int? element) => a.LesenAsync(doc =>
        {
            var t = Sicht(sicht);
            var spalten = Unterrichtsspalten.Fuer(doc);
            var ix = doc.Index;
            var liste = Auswahl(doc, t, element).Select(u => Zeile(u, spalten, ix)).ToList();
            var el = t is { } tt && element is int id ? doc.Daten.AlleElemente(tt).FirstOrDefault(e => e.Id == id) : null;
            return new
            {
                titel = t is null ? "Alle Unterrichte" : el is null ? $"Unterricht / {Typnamen.Name(t.Value)}" : $"{Typnamen.Name(t.Value)} {el.Kurzname} ({el.Langname})",
                spalten = spalten.Select(Info),
                unterrichte = liste,
                summe = new { wst = liste.Sum(x => x.Wst), nvpl = liste.Sum(x => x.Nvpl) },
            };
        }));

        g.MapPatch("/{uid:int}", (Arbeitsdokument a, int uid, UnterrichtAenderung ae, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"U-Nr. {uid}: {ae.Feld} geändert", AenderungsArt.Unterricht, d =>
            {
                var u = Suchen(d, uid);
                var s = Unterrichtsspalten.Fuer(a.Dokument).FirstOrDefault(x => x.Key == ae.Feld) ?? throw StudioFehler.Ungueltig($"Unbekannte Spalte \"{ae.Feld}\".");
                if (s.Set is null) throw StudioFehler.Ungueltig($"Die Spalte \"{s.Titel}\" kann nicht geändert werden.");
                if (ae.Zeile < 0 || ae.Zeile >= u.Zeilen.Count) throw StudioFehler.Ungueltig("Ungültige Kopplungszeile.");
                s.Set(d, u, u.Zeilen[ae.Zeile], ae.Wert ?? "");
            }, doc => Zeile(Suchen(doc.Daten, uid), Unterrichtsspalten.Fuer(doc), doc.Index)));

        g.MapPost("", (Arbeitsdokument a, NeuerUnterricht n, ClaimsPrincipal p) =>
        {
            var id = 0;
            return a.AendernAsync(p.Benutzername(), "Neuer Unterricht", AenderungsArt.Unterricht, d =>
            {
                var u = Anlegen(d, Sicht(n.Sicht), n.Element);
                id = u.Id;
                if (n.Werte is { Count: > 0 })
                {
                    var spalten = Unterrichtsspalten.Fuer(a.Dokument);
                    // Reihenfolge wie bei der Eingabe: Stunden, Klassen, Fach, Lehrer, dann der Rest
                    string[] vorne = { "Wst", "Klassen", "Fach", "Lehrer" };
                    foreach (var (feld, wert) in n.Werte.OrderBy(w => Array.IndexOf(vorne, w.Key) is var i && i >= 0 ? i : 99))
                    {
                        var s = spalten.FirstOrDefault(x => x.Key == feld) ?? throw StudioFehler.Ungueltig($"Unbekannte Spalte \"{feld}\".");
                        s.Set?.Invoke(d, u, u.Zeilen[0], wert ?? "");
                    }
                }
            }, doc => Zeile(Suchen(doc.Daten, id), Unterrichtsspalten.Fuer(doc), doc.Index));
        });

        g.MapPost("/{uid:int}/kopieren", (Arbeitsdokument a, int uid, ClaimsPrincipal p) =>
        {
            var id = 0;
            return a.AendernAsync(p.Benutzername(), "Unterricht kopieren", AenderungsArt.Unterricht, d =>
            {
                var k = Suchen(d, uid).Kopie(d.NeueId());
                id = k.Id;
                d.Unterrichte.Add(k);
            }, doc => Zeile(Suchen(doc.Daten, id), Unterrichtsspalten.Fuer(doc), doc.Index));
        });

        g.MapPost("/{uid:int}/zeilen", (Arbeitsdokument a, int uid, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kopplungszeile hinzufügen", AenderungsArt.Unterricht, d =>
            {
                var u = Suchen(d, uid);
                var vorlage = u.Zeilen.FirstOrDefault();
                u.Zeilen.Add(new Kopplungszeile { KlassenIds = vorlage?.KlassenIds.ToList() ?? new(), FachId = vorlage?.FachId });
                foreach (var pl in d.Perioden.SelectMany(x => x.Plan).Where(x => x.UnterrichtId == u.Id)) pl.Raeume.Add(null);
            }, doc => Zeile(Suchen(doc.Daten, uid), Unterrichtsspalten.Fuer(doc), doc.Index)));

        g.MapDelete("/{uid:int}/zeilen/{nr:int}", (Arbeitsdokument a, int uid, int nr, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Kopplungszeile löschen", AenderungsArt.Unterricht, d =>
            {
                var u = Suchen(d, uid);
                if (u.Zeilen.Count <= 1) throw StudioFehler.Ungueltig("Die einzige Zeile eines Unterrichts kann nicht gelöscht werden – bitte den Unterricht löschen.");
                if (nr < 0 || nr >= u.Zeilen.Count) throw StudioFehler.Ungueltig("Ungültige Kopplungszeile.");
                u.Zeilen.RemoveAt(nr);
                foreach (var pl in d.Perioden.SelectMany(x => x.Plan).Where(x => x.UnterrichtId == u.Id && x.Raeume.Count > nr)) pl.Raeume.RemoveAt(nr);
            }, doc => Zeile(Suchen(doc.Daten, uid), Unterrichtsspalten.Fuer(doc), doc.Index)));

        g.MapPost("/loeschen", (Arbeitsdokument a, IdListe l, ClaimsPrincipal p) =>
        {
            var ids = l.Ids ?? new();
            return a.AendernAsync(p.Benutzername(), $"{ids.Count} Unterricht(e) gelöscht", AenderungsArt.Unterricht, d =>
            {
                foreach (var id in ids) Stammdaten.UnterrichtLoeschen(d, id);
            }, _ => new { geloescht = ids.Count });
        });
    }

    /// <summary>Neuer Unterricht (1 Wochenstunde), vorbelegt mit dem Element der Sicht – wie im Desktop.</summary>
    private static Unterricht Anlegen(Schuldatei d, ElementTyp? sicht, int? element)
    {
        var z = new Kopplungszeile();
        var u = new Unterricht { Id = d.NeueId(), Wochenstunden = 1, Zeilen = { z } };
        if (element is int id)
        {
            switch (sicht)
            {
                case ElementTyp.Klasse:
                    z.KlassenIds.Add(id);
                    z.StammraumId = d.Klassen.FirstOrDefault(k => k.Id == id)?.StammraumId;
                    break;
                case ElementTyp.Lehrer:
                    z.LehrerId = id;
                    z.StammraumId = d.Lehrer.FirstOrDefault(l => l.Id == id)?.StammraumId;
                    break;
                case ElementTyp.Fach:
                    var f = d.Faecher.FirstOrDefault(x => x.Id == id);
                    z.FachId = id;
                    z.FachraumId = f?.FachraumId;
                    z.FachraumgruppeId = f?.FachraumgruppeId;
                    if (f != null) u.Doppelstunden = f.Doppelstunden;
                    break;
            }
        }
        d.Unterrichte.Add(u);
        return u;
    }
}
