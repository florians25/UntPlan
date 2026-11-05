using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Nachricht an eine Lehrkraft mit ihren Vertretungen.</summary>
public sealed record Vertretungsnachricht(int LehrerId, string Kurzname, string Email, string Betreff, string Text, int Anzahl);

/// <summary>
/// Vertretungsnachrichten: je Lehrkraft eine Nachricht mit allen Vertretungen, die sie im Zeitraum betreffen (als Vertreter,
/// Sondereinsatz, Klausuraufsicht, Pausenaufsicht; auf Wunsch Entfälle und – für abwesende Lehrkräfte – Änderungen ihres Unterrichts).
/// Text nach der Vorlage aus den Einstellungen (<see cref="NachrichtenEinstellungen"/>).
/// </summary>
public static class Vertretungsnachrichten
{
    public static List<Vertretungsnachricht> Erzeugen(Schuldatei d, DateOnly von, DateOnly bis, DateTime? geaendertNach = null)
    {
        var e = d.Vertretungseinstellungen.Nachrichten ?? new NachrichtenEinstellungen();
        var ix = new DatenIndex(d);
        var liste = Vertretungen.ImZeitraum(d, von, bis)
            .Where(v => v.Id != 0 && !v.NichtDrucken && v.Art is not (VertretungsArt.Veranstaltung or VertretungsArt.Unterricht) && !Vertretungen.NichtInListe(d, v))
            .Where(v => geaendertNach is null || v.Geaendert >= geaendertNach || v.Erstellt >= geaendertNach)
            .ToList();
        var jeLehrer = new Dictionary<int, List<Vertretung>>();
        void Add(int? l, Vertretung v)
        {
            if (l is not int id) return;
            if (!jeLehrer.TryGetValue(id, out var x)) jeLehrer[id] = x = new List<Vertretung>();
            if (!x.Contains(v)) x.Add(v);
        }
        foreach (var v in liste)
        {
            if (!v.IstEntfall) Add(v.VertreterId, v);
            else if (e.EntfaelleMelden && v.LehrerId is int el && !Absenzen.LehrerAbwesend(d, el, v.Datum, v.Stunde)) Add(el, v);
            if (e.AbwesendeInformieren && v.LehrerId is int al && al != v.VertreterId && Absenzen.LehrerAbwesend(d, al, v.Datum, v.Stunde)) Add(al, v);
        }
        var res = new List<Vertretungsnachricht>();
        foreach (var l in Stammdaten.Sortiert(d.Lehrer))
        {
            if (!jeLehrer.TryGetValue(l.Id, out var vs) || vs.Count == 0) continue;
            vs = vs.OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ToList();
            var zeilen = string.Join("\n", vs.Select(v => Ersetzen(e.Zeile, ZeilenWerte(d, ix, v)).Trim()));
            var datum = von == bis ? Format.Datum(von) : $"{Format.Datum(von)} – {Format.Datum(bis)}";
            var werte = new Dictionary<string, string>
            {
                ["Name"] = l.VollerName.Length > 0 ? l.VollerName : l.Kurzname, ["Kurzname"] = l.Kurzname, ["Datum"] = datum,
                ["Zeilen"] = zeilen, ["Anzahl"] = vs.Count.ToString(),
            };
            res.Add(new Vertretungsnachricht(l.Id, l.Kurzname, l.Email, Ersetzen(e.Betreff, werte), Ersetzen(e.Text, werte), vs.Count));
        }
        return res;
    }

    private static Dictionary<string, string> ZeilenWerte(Schuldatei d, DatenIndex ix, Vertretung v) => new()
    {
        ["Datum"] = v.Datum.ToString("dd.MM.", Format.De),
        ["Tag"] = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7],
        ["Stunde"] = v.Stunde.ToString(),
        ["Art"] = Vertretungsplan.ArtText(d, v.Art),
        ["Klassen"] = ix.KKlassen(v.NeueKlassenIds),
        ["Fach"] = ix.KFach(v.VertretungsfachId ?? v.FachId),
        ["Raum"] = v.VertretungsraumId is int r ? ix.KRaum(r) : v.RaumVerdraengt ? "" : ix.KRaum(v.RaumId),
        ["Lehrer"] = ix.KLehrer(v.LehrerId),
        ["Text"] = v.Text,
    };

    /// <summary>Platzhalter {Name} durch Werte ersetzen (unbekannte Platzhalter bleiben stehen); mehrfache Leerzeichen werden zusammengefasst.</summary>
    public static string Ersetzen(string vorlage, IReadOnlyDictionary<string, string> werte)
    {
        var s = vorlage ?? "";
        foreach (var (k, w) in werte) s = s.Replace("{" + k + "}", w ?? "", StringComparison.OrdinalIgnoreCase);
        return string.Join("\n", s.Split('\n').Select(z => System.Text.RegularExpressions.Regex.Replace(z, "  +", " ")));
    }

    /// <summary>mailto-Verweis der Nachricht (für das E-Mail-Programm).</summary>
    public static string MailTo(Vertretungsnachricht n) =>
        $"mailto:{Uri.EscapeDataString(n.Email)}?subject={Uri.EscapeDataString(n.Betreff)}&body={Uri.EscapeDataString(n.Text.Replace("\n", "\r\n"))}";
}
