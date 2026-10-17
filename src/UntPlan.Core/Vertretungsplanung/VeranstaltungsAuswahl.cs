using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Benannte Gruppe von Elementen für die Schnellauswahl einer Veranstaltung (z. B. „Alle Klassen“, „Jahrgang 7“, „Abteilung Sek I“).</summary>
/// <param name="Bereich">Oberbegriff für die Menügliederung („“ = direkt, sonst z. B. „Abteilung“, „Jahrgang“, „Raumgruppe“).</param>
public sealed record ElementGruppe(string Name, string Bereich, ElementTyp Typ, IReadOnlyList<int> Ids);

/// <summary>Elemente aus Stunden bzw. einem Unterricht (Klassen, Lehrkräfte, Räume), z. B. zum Übernehmen in eine Veranstaltung.</summary>
public sealed record StundenElemente(IReadOnlyList<int> KlassenIds, IReadOnlyList<int> LehrerIds, IReadOnlyList<int> RaumIds)
{
    public IReadOnlyList<int> Von(ElementTyp typ) => typ switch
    {
        ElementTyp.Klasse => KlassenIds,
        ElementTyp.Lehrer => LehrerIds,
        ElementTyp.Raum => RaumIds,
        _ => Array.Empty<int>(),
    };

    public bool Leer => KlassenIds.Count == 0 && LehrerIds.Count == 0 && RaumIds.Count == 0;
}

/// <summary>
/// Schnellauswahl für Veranstaltungen: alle Lehrkräfte / Klassen / Räume, Auswahl nach Abteilung, Jahrgang, Klassengruppe,
/// Raumgruppe usw. sowie das Übernehmen aller Elemente aus den Stunden der Veranstaltung oder aus einem Unterricht.
/// </summary>
public static class VeranstaltungsAuswahl
{
    /// <summary>Spaltenart der Veranstaltung, in die Elemente übernommen werden.</summary>
    public enum Feld { Klassen, Klassenteil, Lehrer, Raeume }

    public static ElementTyp TypVon(Feld f) => f switch
    {
        Feld.Lehrer => ElementTyp.Lehrer,
        Feld.Raeume => ElementTyp.Raum,
        _ => ElementTyp.Klasse,
    };

    public static string AlleText(ElementTyp typ) => typ switch
    {
        ElementTyp.Lehrer => "Alle Lehrkräfte",
        ElementTyp.Raum => "Alle Räume",
        _ => "Alle Klassen",
    };

    /// <summary>
    /// Wählbare Elemente eines Typs (sortiert, ohne „ignorieren“). Mit Zeitraum nur Klassen bzw. Lehrkräfte, die in diesem Zeitraum
    /// bestehen (Klasse von/bis, Eintritt/Austritt).
    /// </summary>
    public static List<Element> Waehlbar(Schuldatei d, ElementTyp typ, DateOnly? von = null, DateOnly? bis = null) =>
        Stammdaten.Sortiert(d, typ).Where(e => !e.Ignorieren && Besteht(e, von, bis)).ToList();

    private static bool Besteht(Element e, DateOnly? von, DateOnly? bis) => e switch
    {
        Klasse k => (k.BisDatum is not DateOnly kb || von is not DateOnly v1 || kb >= v1) && (k.VonDatum is not DateOnly kv || bis is not DateOnly b1 || kv <= b1),
        Lehrer l => (l.Austritt is not DateOnly la || von is not DateOnly v2 || la >= v2) && (l.Eintritt is not DateOnly le || bis is not DateOnly b2 || le <= b2),
        _ => true,
    };

    /// <summary>Alle wählbaren Elemente eines Typs (Ids).</summary>
    public static List<int> Alle(Schuldatei d, ElementTyp typ, DateOnly? von = null, DateOnly? bis = null) =>
        Waehlbar(d, typ, von, bis).Select(e => e.Id).ToList();

    /// <summary>Ab so vielen wählbaren Elementen heißt eine fast vollständige Auswahl „Alle … außer …“.</summary>
    public const int AusserAb = 10;
    /// <summary>Höchstens so viele fehlende Elemente stehen hinter „außer“.</summary>
    public const int AusserHoechstens = 3;

    /// <summary>Umfasst die Auswahl alle wählbaren Elemente des Typs (im Zeitraum)? Bei weniger als drei wählbaren Elementen nie.</summary>
    public static bool UmfasstAlle(Schuldatei d, ElementTyp typ, IEnumerable<int> ids, DateOnly? von = null, DateOnly? bis = null)
    {
        var alle = Alle(d, typ, von, bis);
        if (alle.Count < 3) return false;
        var set = ids as ISet<int> ?? ids.ToHashSet();
        return alle.All(set.Contains);
    }

    /// <summary>
    /// Kurztext einer Auswahl für Listen, Aushang und Export: „Alle Klassen“, „Alle Lehrkräfte außer A, B“ (ab <see cref="AusserAb"/>
    /// wählbaren Elementen, höchstens <see cref="AusserHoechstens"/> fehlende) oder die Kurznamen in der gegebenen Reihenfolge.
    /// </summary>
    public static string Kurztext(Schuldatei d, ElementTyp typ, IEnumerable<int> ids, DateOnly? von = null, DateOnly? bis = null, string trenner = ",")
    {
        var liste = ids.Distinct().ToList();
        var alle = Alle(d, typ, von, bis);
        if (alle.Count >= 3 && liste.Count >= 3)
        {
            var set = liste.ToHashSet();
            var fehlend = alle.Where(i => !set.Contains(i)).ToList();
            if (fehlend.Count == 0) return AlleText(typ);
            if (alle.Count >= AusserAb && fehlend.Count <= AusserHoechstens && liste.All(alle.Contains))
                return $"{AlleText(typ)} außer {string.Join(", ", fehlend.Select(i => K(d, typ, i)))}";
        }
        return string.Join(trenner, liste.Select(i => K(d, typ, i)));
    }

    private static string K(Schuldatei d, ElementTyp typ, int id) => d.AlleElemente(typ).FirstOrDefault(e => e.Id == id)?.Kurzname ?? "?";

    /// <summary>
    /// Eingabe einer Auswahl lesen: Kurznamen (getrennt durch Komma, Leerzeichen, Semikolon oder Zeilenumbruch) bzw. „Alle“ /
    /// „Alle Klassen“ / „Alle Lehrkräfte“ / „Alle Räume“, optional mit „außer A, B“ (so, wie <see cref="Kurztext"/> sie anzeigt).
    /// Unbekannte Kurznamen: <see cref="FormatException"/>.
    /// </summary>
    public static List<int> Lesen(Schuldatei d, ElementTyp typ, string text, DateOnly? von = null, DateOnly? bis = null)
    {
        var elemente = d.AlleElemente(typ).ToList();
        int Id(string k) => elemente.FirstOrDefault(x => x.Kurzname.Equals(k, StringComparison.OrdinalIgnoreCase))?.Id
                            ?? throw new FormatException($"{TypName(typ)} {k} unbekannt");
        static string[] Teile(string s) => s.Split(new[] { ',', ' ', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var t = text.Trim();
        if (t.StartsWith("alle", StringComparison.OrdinalIgnoreCase) && (t.Length == 4 || !char.IsLetterOrDigit(t[4])) && !elemente.Any(e => e.Kurzname.Equals("alle", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = t[4..].Trim();
            foreach (var wort in new[] { "Lehrkräfte", "Lehrkraefte", "Lehrer", "Klassen", "Räume", "Raeume" })
                if (rest.StartsWith(wort, StringComparison.OrdinalIgnoreCase)) { rest = rest[wort.Length..].Trim(); break; }
            var ohne = new HashSet<int>();
            var dazu = new List<int>();
            foreach (var wort in new[] { "außer", "ausser", "ohne" })
                if (rest.StartsWith(wort, StringComparison.OrdinalIgnoreCase))
                {
                    ohne = Teile(rest[wort.Length..]).Select(Id).ToHashSet();
                    rest = "";
                    break;
                }
            // weitere Kurznamen nach „Alle …“ (z. B. „Alle Klassen, X“) – ignorierte bzw. nicht wählbare Elemente kommen hinzu
            dazu.AddRange(Teile(rest).Select(Id));
            return Alle(d, typ, von, bis).Where(i => !ohne.Contains(i)).Concat(dazu).Distinct().ToList();
        }
        return Teile(t).Select(Id).Distinct().ToList();
    }

    private static string TypName(ElementTyp typ) => typ switch
    {
        ElementTyp.Lehrer => "Lehrkraft",
        ElementTyp.Raum => "Raum",
        ElementTyp.Klasse => "Klasse",
        _ => typ.ToString(),
    };

    /// <summary>
    /// Gruppen für die Schnellauswahl eines Typs: zuerst „Alle …“, dann nach Abteilung und – je Typ – Jahrgang, Stufe und
    /// Klassengruppe (Klassen), Fachgruppe sowie Lehrkräfte bzw. Klassenleitungen der Klassen der Veranstaltung (Lehrer),
    /// Raumgruppe, Raumart, Stockwerk und Stammräume der Klassen (Räume). Leere Gruppen fehlen.
    /// </summary>
    public static List<ElementGruppe> Gruppen(Schuldatei d, ElementTyp typ, Veranstaltung? va = null)
    {
        var liste = Waehlbar(d, typ, va?.Von, va?.Bis);
        var ids = liste.Select(e => e.Id).ToHashSet();
        var res = new List<ElementGruppe>();
        var vergleich = StringComparer.Create(Format.De, true);
        void Neu(string name, string bereich, IEnumerable<int> gruppe)
        {
            // Reihenfolge der Stammdaten beibehalten
            var g = gruppe.ToHashSet();
            var l = liste.Where(e => g.Contains(e.Id)).Select(e => e.Id).ToList();
            if (l.Count > 0) res.Add(new ElementGruppe(name, bereich, typ, l));
        }
        void NachText(string bereich, Func<Element, string?> schluessel)
        {
            foreach (var g in liste.Select(e => (e, k: schluessel(e)?.Trim() ?? "")).Where(x => x.k.Length > 0)
                         .GroupBy(x => x.k, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, vergleich))
                Neu($"{bereich} {g.Key}", bereich, g.Select(x => x.e.Id));
        }

        Neu(AlleText(typ), "", ids);
        foreach (var a in Stammdaten.Sortiert(d.Abteilungen))
            Neu($"Abteilung {a.Kurzname}", "Abteilung", liste.Where(e => e.AbteilungId == a.Id).Select(e => e.Id));
        var vaKlassen = va?.AlleKlassenIds.ToHashSet() ?? new HashSet<int>();
        switch (typ)
        {
            case ElementTyp.Klasse:
                NachText("Jahrgang", e => (e as Klasse)?.Jahrgang);
                // Stufe nur, wenn sie sich vom Jahrgang unterscheidet
                NachText("Stufe", e => e is Klasse { Stufe: { Length: > 0 } s } k && !s.Equals(k.Jahrgang, StringComparison.OrdinalIgnoreCase) ? s : null);
                NachText("Klassengruppe", e => (e as Klasse)?.Klassengruppe);
                break;
            case ElementTyp.Lehrer:
                if (vaKlassen.Count > 0)
                {
                    var unterrichte = d.Unterrichte.Concat(d.Perioden.SelectMany(p => p.Unterrichte ?? new()));
                    Neu("Lehrkräfte der Klassen", "", unterrichte.SelectMany(u => u.Zeilen)
                        .Where(z => z.LehrerId.HasValue && z.KlassenIds.Any(vaKlassen.Contains)).Select(z => z.LehrerId!.Value));
                    Neu("Klassenleitungen der Klassen", "", d.Klassen.Where(k => vaKlassen.Contains(k.Id)).SelectMany(k => k.KlassenlehrerIds));
                }
                var fachgruppe = d.Faecher.Where(f => !string.IsNullOrWhiteSpace(f.Fachgruppe)).ToDictionary(f => f.Id, f => f.Fachgruppe.Trim());
                foreach (var g in liste.OfType<Lehrer>().SelectMany(l => l.FaecherIds.Where(fachgruppe.ContainsKey).Select(f => (l.Id, fg: fachgruppe[f])))
                             .GroupBy(x => x.fg, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, vergleich))
                    Neu($"Fachgruppe {g.Key}", "Fachgruppe", g.Select(x => x.Id));
                NachText("Status", e => (e as Lehrer)?.Status);
                break;
            case ElementTyp.Raum:
                if (vaKlassen.Count > 0)
                    Neu("Stammräume der Klassen", "", d.Klassen.Where(k => vaKlassen.Contains(k.Id) && k.StammraumId.HasValue).Select(k => k.StammraumId!.Value));
                foreach (var rg in Stammdaten.Sortiert(d.Raumgruppen))
                    Neu($"Raumgruppe {rg.Kurzname}", "Raumgruppe", rg.RaumIds);
                NachText("Raumart", e => (e as Raum)?.Raumart);
                NachText("Stockwerk", e => (e as Raum)?.Stockwerk);
                break;
        }
        return res;
    }

    /// <summary>Vorhandene Ids um neue ergänzen (Reihenfolge bleibt, keine Doppelten).</summary>
    public static List<int> Ergaenzen(IEnumerable<int> vorhanden, IEnumerable<int> neu) => vorhanden.Concat(neu).Distinct().ToList();

    /// <summary>
    /// Elemente der regulären Stunden, die in die Veranstaltung fallen (alle Tage und Stunden der Veranstaltung) und an denen eine
    /// Klasse oder eine Lehrkraft der Veranstaltung beteiligt ist: die betroffenen Klassen, ihre Lehrkräfte und Räume.
    /// Ohne Klassen und Lehrkräfte in der Veranstaltung: alle Stunden dieses Zeitraums.
    /// </summary>
    public static StundenElemente AusStunden(Schuldatei d, Veranstaltung va)
    {
        var klassen = va.AlleKlassenIds.ToHashSet();
        var lehrer = va.LehrerIds.ToHashSet();
        var offen = klassen.Count == 0 && lehrer.Count == 0;
        var k = new List<int>();
        var l = new List<int>();
        var r = new List<int>();
        var bis = va.Bis < va.Von.AddDays(62) ? va.Bis : va.Von.AddDays(62);
        foreach (var tag in Kalender.Tage(va.Von, bis))
            foreach (var s in Tagesplan.Regulaer(d, tag).Where(s => va.Deckt(tag, s.Stunde)).OrderBy(s => s.Stunde))
            {
                if (!offen && !s.KlassenIds.Any(klassen.Contains) && !(s.LehrerId is int li && lehrer.Contains(li))) continue;
                k.AddRange(s.KlassenIds);
                if (s.LehrerId is int lid) l.Add(lid);
                if (s.RaumId is int rid) r.Add(rid);
            }
        return Sortieren(d, k, l, r);
    }

    /// <summary>Elemente eines Unterrichts (bzw. einer Planstunde mit ihren Räumen): Klassen, Lehrkräfte, Räume.</summary>
    public static StundenElemente AusUnterricht(Schuldatei d, Unterricht u, Planstunde? p = null)
    {
        var raeume = new List<int>();
        for (var i = 0; i < u.Zeilen.Count; i++)
        {
            var raum = p?.RaumFuerZeile(i) ?? u.Zeilen[i].Wunschraum;
            if (raum is int x) raeume.Add(x);
        }
        return Sortieren(d, u.KlassenIds, u.LehrerIds, raeume);
    }

    private static StundenElemente Sortieren(Schuldatei d, IEnumerable<int> k, IEnumerable<int> l, IEnumerable<int> r)
    {
        List<int> S(ElementTyp typ, IEnumerable<int> ids)
        {
            var set = ids.ToHashSet();
            return Stammdaten.Sortiert(d, typ).Where(e => set.Contains(e.Id)).Select(e => e.Id).ToList();
        }
        return new StundenElemente(S(ElementTyp.Klasse, k), S(ElementTyp.Lehrer, l), S(ElementTyp.Raum, r));
    }

    /// <summary>
    /// Elemente in ein Feld der Veranstaltung übernehmen (ergänzen bzw. mit <paramref name="ersetzen"/> ersetzen). Klassen und Kl-Teil
    /// schließen sich aus: wer in die eine Spalte kommt, verschwindet aus der anderen. Liefert die Anzahl neu hinzugekommener Elemente.
    /// </summary>
    public static int Uebernehmen(Veranstaltung va, Feld feld, IEnumerable<int> ids, bool ersetzen = false)
    {
        var neu = ids.Distinct().ToList();
        List<int> alt = feld switch
        {
            Feld.Klassen => va.KlassenIds,
            Feld.Klassenteil => va.KlassenteilIds,
            Feld.Lehrer => va.LehrerIds,
            _ => va.RaumIds,
        };
        var vorher = alt.ToHashSet();
        var ergebnis = ersetzen ? neu : Ergaenzen(alt, neu);
        switch (feld)
        {
            case Feld.Klassen:
                va.KlassenIds = ergebnis;
                va.KlassenteilIds.RemoveAll(va.KlassenIds.Contains);
                break;
            case Feld.Klassenteil:
                va.KlassenteilIds = ergebnis;
                va.KlassenIds.RemoveAll(va.KlassenteilIds.Contains);
                break;
            case Feld.Lehrer:
                va.LehrerIds = ergebnis;
                break;
            default:
                va.RaumIds = ergebnis;
                break;
        }
        return ergebnis.Count(i => !vorher.Contains(i));
    }

    /// <summary>Alle Elemente aus Stunden bzw. Unterricht übernehmen: Klassen (in die Spalte Klasse, sofern nicht schon Kl-Teil), Lehrkräfte, Räume.</summary>
    public static int AllesUebernehmen(Veranstaltung va, StundenElemente e)
    {
        var kt = va.KlassenteilIds.ToHashSet();
        return Uebernehmen(va, Feld.Klassen, e.KlassenIds.Where(k => !kt.Contains(k)))
               + Uebernehmen(va, Feld.Lehrer, e.LehrerIds)
               + Uebernehmen(va, Feld.Raeume, e.RaumIds);
    }
}
