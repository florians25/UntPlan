using UntPlan.Web.Contracts;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Änderung einer Zelle der Schüler-Gruppen-Zuordnung (Matrix): Häkchen und Zeiträume der Zugehörigkeit.</summary>
public sealed record MatrixAenderung(int Unterricht, int Gruppe, int SchuelerId, bool Auswahl, List<ZeitraumEingabe>? Zeitraeume);

/// <summary>
/// Leitfaden Klassenleitung, Kapitel 2 und 3: Unterricht → Schülergruppen (Liste je Klasse mit Filtern Schüler*innen und Fach) und die
/// Schüler-Gruppen-Zuordnung als Matrix (alle Schüler*innen × alle Schülergruppen, Auswahlkriterien Klasse, Schüler*innen, Fach additiv;
/// je Zelle Häkchen und Zeiträume für den Gruppenwechsel im laufenden Schuljahr).
/// </summary>
public sealed partial class Unterrichtsgruppen
{
    /// <summary>Stichtag für Listen ohne Zeitraum: heute, innerhalb des Schuljahres.</summary>
    private DateOnly StichtagSchuljahr(Stand s) =>
        s.SchuljahrVon == DateOnly.MinValue ? _uhr.Heute : Stichtag(_uhr.Heute, s.SchuljahrVon, s.SchuljahrBis == DateOnly.MaxValue ? _uhr.Heute : s.SchuljahrBis);

    private static void Kollegium(Benutzer b)
    {
        if (!b.Kollegium) throw new UnauthorizedAccessException("Nur für das Kollegium.");
    }

    /// <summary>Gruppen eines Unterrichts: Standardgruppe (auch ungespeichert, Id 0) und die gespeicherten weiteren Gruppen.</summary>
    private List<Schuelergruppe> AlleGruppen(Stand s, WebUnterricht u)
    {
        var gespeichert = s.Zuordnungen.Gruppen.GetValueOrDefault(u.Id) ?? new();
        var res = new List<Schuelergruppe>();
        if (!gespeichert.Any(g => g.IstStandard)) res.Add(new Schuelergruppe(0, u.Id, Standardname(u), Standard, "", "", new()));
        res.AddRange(gespeichert);
        return res;
    }

    /// <summary>Ist die Schülerin/der Schüler der Gruppe zugeordnet (Häkchen, unabhängig vom Datum) und mit welchen Zeiträumen?</summary>
    private static (bool Auswahl, List<(string Von, string Bis)> Zeitraeume) Zelle(Stand s, WebUnterricht u, Schuelergruppe g, WebSchueler x, DateOnly stichtag)
    {
        var m = g.Mitglieder.FirstOrDefault(y => y.SchuelerId == x.Id);
        if (m is null) return (g.IstStandard && Klassen(u).Contains(Klassenzugehoerigkeit.KlasseAm(s, x, stichtag), Ci), new());
        return (!m.Ausgeschlossen, m.Zeitraeume);
    }

    /// <summary>Unterricht → Schülergruppen: Schülergruppen der Unterrichte einer Klasse (optional nur die einer Schülerin/eines Schülers bzw. eines Fachs).</summary>
    public object Gruppenliste(Benutzer b, Stand s, string klasse, int? schuelerId, string? fach)
    {
        Kollegium(b);
        if (!s.Klassen.ContainsKey(klasse)) throw new ArgumentException($"Unbekannte Klasse {klasse}.");
        if (!Rechtedienst.Darf(b, Recht.UnterrichtKlassen)) Klassenbuchdienst.KlasseEinsehenVerlangen(b, s, klasse);
        var stichtag = StichtagSchuljahr(s);
        var periode = Listendienst.Periode(s, stichtag);
        var zeilen = _listen.Unterrichte(s, stichtag, null, klasse);
        var schueler = Klassenzugehoerigkeit.Am(s, new[] { klasse }, stichtag);
        var x = schuelerId is int sid ? s.Schueler.FirstOrDefault(y => y.Id == sid) : null;
        var faecher = zeilen.SelectMany(z => z.Fach.Split(", ", StringSplitOptions.RemoveEmptyEntries)).Distinct(Ci).OrderBy(f => f, De).ToList();
        var gruppen = new List<object>();
        foreach (var z in zeilen)
        {
            if (!string.IsNullOrEmpty(fach) && !z.Fach.Split(", ").Contains(fach, Ci)) continue;
            if (!s.Unterricht.TryGetValue(z.Id, out var u)) continue;
            var darf = DarfBearbeiten(b, s, u);
            foreach (var g in AlleGruppen(s, u))
            {
                if (x != null && !Zelle(s, u, g, x, stichtag).Auswahl) continue;
                gruppen.Add(new
                {
                    unterricht = z.Id, g.Id, g.Name, g.Art, gast = g.Gast, standard = g.IstStandard, z.Klassen, z.Fach, z.Lehrer,
                    von = g.Von.Length > 0 ? g.Von : periode?.Von ?? "", bis = g.Bis.Length > 0 ? g.Bis : periode?.Bis ?? "",
                    schueler = g.IstStandard ? Standardmitglieder(s, u, g, stichtag).Count() : g.Mitglieder.Count(m => m.Am(stichtag)),
                    bearbeiten = darf,
                });
            }
        }
        return new
        {
            klasse, stichtag, faecher, gruppen,
            schueler = schueler.Select(y => new { y.Id, y.Name, y.Vorname }).ToList(),
        };
    }

    /// <summary>
    /// Schüler-Gruppen-Zuordnung: die Auswahlkriterien wirken additiv – jede Klasse bringt ihre Schüler*innen und die Schülergruppen ihrer
    /// Unterrichte, jede Schülerin/jeder Schüler sich selbst und die Gruppen der eigenen Klasse, jedes Fach die Gruppen dieses Fachs samt
    /// den Schüler*innen ihrer Klassen.
    /// </summary>
    public object Matrix(Benutzer b, Stand s, IEnumerable<string> klassen, IEnumerable<int> schuelerIds, IEnumerable<string> faecher)
    {
        Kollegium(b);
        var stichtag = StichtagSchuljahr(s);
        var alle = s.Schueler.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var spalten = new List<(Unterrichtszeile Z, WebUnterricht U, Schuelergruppe G)>();
        var gesehen = new HashSet<int>();
        var zeilenIds = new List<int>();
        void Unterricht(Unterrichtszeile z)
        {
            if (!gesehen.Add(z.Id) || !s.Unterricht.TryGetValue(z.Id, out var u)) return;
            foreach (var g in AlleGruppen(s, u)) spalten.Add((z, u, g));
        }
        void Schueler(IEnumerable<WebSchueler> liste) { foreach (var x in liste) if (!zeilenIds.Contains(x.Id)) zeilenIds.Add(x.Id); }
        var kWahl = klassen.Where(k => s.Klassen.ContainsKey(k)).Distinct(Ci).ToList();
        // ohne Recht „Unterricht Klassen“ (Schreiben): nur Klassen, in denen das Konto Klassenleitung ist oder unterrichtet
        var eingeschraenkt = !Rechtedienst.Darf(b, Recht.UnterrichtKlassen);
        if (eingeschraenkt)
        {
            foreach (var k in kWahl) Klassenbuchdienst.KlasseEinsehenVerlangen(b, s, k);
            foreach (var id in schuelerIds.Where(alle.ContainsKey))
                Klassenbuchdienst.KlasseEinsehenVerlangen(b, s, Klassenzugehoerigkeit.KlasseAm(s, alle[id], stichtag));
            if (faecher.Any(f => f.Length > 0) && kWahl.Count == 0 && !schuelerIds.Any())
                throw new ArgumentException("Bitte zusätzlich eine Ihrer Klassen wählen.");
        }
        foreach (var k in kWahl)
        {
            foreach (var z in _listen.Unterrichte(s, stichtag, null, k)) Unterricht(z);
            Schueler(Klassenzugehoerigkeit.Am(s, new[] { k }, stichtag));
        }
        var sWahl = schuelerIds.Where(alle.ContainsKey).Distinct().ToList();
        foreach (var id in sWahl)
        {
            var x = alle[id];
            foreach (var z in _listen.Unterrichte(s, stichtag, null, Klassenzugehoerigkeit.KlasseAm(s, x, stichtag))) Unterricht(z);
            Schueler(new[] { x });
        }
        var fWahl = faecher.Where(f => f.Length > 0).Distinct(Ci).ToList();
        foreach (var f in fWahl)
            foreach (var z in _listen.Unterrichte(s, stichtag, null, null).Where(z => z.Fach.Split(", ").Contains(f, Ci))
                         .Where(z => !eingeschraenkt || z.Klassen.Any(k => Klassenbuchdienst.DarfKlasseEinsehen(b, s, k))))
            {
                Unterricht(z);
                Schueler(Klassenzugehoerigkeit.Am(s, z.Klassen, stichtag));
            }
        var geschlecht = _db.Liste("SELECT id, geschlecht FROM schueler_daten", r => (Id: r.Zahl("id"), G: r.Text("geschlecht"))).ToDictionary(x => x.Id, x => x.G);
        var zeilen = zeilenIds.Select(id => alle[id] with { Klasse = Klassenzugehoerigkeit.KlasseAm(s, alle[id], stichtag) })
            .OrderBy(x => x.Klasse, De).ThenBy(x => x.Name, De).ThenBy(x => x.Vorname, De).ToList();
        spalten = spalten.OrderBy(c => c.Z.Klassen.FirstOrDefault() ?? "", De).ThenBy(c => c.Z.Fach, De).ThenBy(c => c.Z.Id).ThenBy(c => c.G.IstStandard ? 0 : 1).ToList();
        return new
        {
            stichtag,
            kriterien = new
            {
                klassen = kWahl,
                schueler = sWahl.Select(id => new { id, name = $"{alle[id].Name} {alle[id].Vorname}" }).ToList(),
                faecher = fWahl,
            },
            spalten = spalten.Select(c => new
            {
                unterricht = c.Z.Id, gruppe = c.G.Id, c.G.Name, c.G.Art, c.Z.Fach, c.Z.Lehrer, c.Z.Klassen, bearbeiten = DarfBearbeiten(b, s, c.U),
            }).ToList(),
            zeilen = zeilen.Select(x => new
            {
                x.Id, x.Name, x.Vorname, x.Klasse, geschlecht = geschlecht.GetValueOrDefault(x.Id) ?? "",
                zellen = spalten.Select(c =>
                {
                    var (a, zr) = Zelle(s, c.U, c.G, x, stichtag);
                    return new { a, z = zr.Select(t => new { von = t.Von, bis = t.Bis }).ToList() };
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary>Geänderte Zellen der Matrix speichern (je Gruppe wie im Dialog „Schüler*innen in der Schülergruppe“).</summary>
    public object MatrixSpeichern(Benutzer b, Stand s, List<MatrixAenderung> aenderungen)
    {
        Kollegium(b);
        var stichtag = StichtagSchuljahr(s);
        var anzahl = 0;
        foreach (var gruppe in aenderungen.GroupBy(a => (a.Unterricht, a.Gruppe)))
        {
            var u = UnterrichtVon(s, gruppe.Key.Unterricht);
            SchreibenFuer(b, s, u);
            var g = GruppeVon(s, u.Id, gruppe.Key.Gruppe);
            var id = g.Id != 0 ? g.Id : StandardgruppeAnlegen(b, s, u.Id);
            foreach (var a in gruppe.DistinctBy(a => a.SchuelerId))
            {
                MitgliedSchreiben(s, u, g, id, new MitgliedEingabe(a.SchuelerId, a.Auswahl, null, null, a.Zeitraeume ?? new()), stichtag);
                anzahl++;
            }
        }
        Neu();
        return new { ok = true, anzahl };
    }
}
