using UntPlan.Core.Model;

namespace UntPlan.Core;

/// <summary>Neue Schuldatei bzw. Beispielschule (fiktives NRW-Gymnasium) zum Kennenlernen aller Funktionen.</summary>
public static class Demo
{
    public static Schuldatei NeueDatei(string schulname, string schulform)
    {
        var d = new Schuldatei();
        d.Einstellungen.Schulname = schulname;
        d.Einstellungen.Schulform = schulform;
        var z = d.Zeitraster;
        z.TageProWoche = 5;
        z.StundenProTag = 10;
        z.LetzteVormittagsstunde = 6;
        z.MittagspauseVon = 6;
        z.MittagspauseBis = 7;
        TimeOnly[] beginn = { new(7, 55), new(8, 45), new(9, 50), new(10, 40), new(11, 45), new(12, 35), new(13, 25), new(14, 15), new(15, 5), new(15, 55) };
        for (var s = 0; s < 10; s++) z.Zeiten.Add(new Stundenzeit { Nr = s + 1, Beginn = beginn[s], Ende = beginn[s].AddMinutes(45) });
        for (var s = 7; s <= 10; s++) z.Belegung[4 * 100 + s] = Tageszeit.Leer; // Freitag Nachmittag kein Unterricht
        z.Pausen.Add(new Pause { NachStunde = 2, Bezeichnung = "1. große Pause", DauerMinuten = 20 });
        z.Pausen.Add(new Pause { NachStunde = 4, Bezeichnung = "2. große Pause", DauerMinuten = 20 });
        z.Pausen.Add(new Pause { NachStunde = 6, Bezeichnung = "Mittagspause", DauerMinuten = 5, Aufsicht = false });
        Nrw.FerienEintragen(d);
        var i = 0;
        foreach (var (k, name, farbe, haupt, _) in Nrw.Faecherkatalog)
            d.Faecher.Add(new Fach { Id = d.NeueId(), Kurzname = k, Langname = name, Hintergrund = unchecked((int)farbe), Hauptfach = haupt, Sortierung = i++, Bereitschaft = k == "BER", MehrmalsProTag = k == "BER" });
        d.Abwesenheitsgruende.AddRange(new[]
        {
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "K", Langname = "Krankheit", Vertraulich = true },
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "F", Langname = "Fortbildung" },
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "D", Langname = "Dienstlich" },
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "KF", Langname = "Klassenfahrt" },
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "EX", Langname = "Exkursion" },
            new Abwesenheitsgrund { Id = d.NeueId(), Kurzname = "S", Langname = "Sonstiges", Statistikrelevant = false },
        });
        Dokument.Normalisieren(d);
        return d;
    }

    private static readonly Dictionary<string, Dictionary<string, int>> Stundentafel = new()
    {
        ["5"] = new() { ["D"] = 4, ["M"] = 4, ["E"] = 4, ["BI"] = 2, ["EK"] = 2, ["GE"] = 1, ["PK"] = 1, ["KR"] = 2, ["KU"] = 2, ["MU"] = 2, ["SP"] = 3, ["KL"] = 1 },
        ["6"] = new() { ["D"] = 4, ["M"] = 4, ["E"] = 4, ["F"] = 3, ["BI"] = 2, ["PH"] = 2, ["GE"] = 2, ["KR"] = 2, ["KU"] = 2, ["MU"] = 1, ["SP"] = 3 },
        ["7"] = new() { ["D"] = 4, ["M"] = 4, ["E"] = 3, ["F"] = 3, ["BI"] = 2, ["PH"] = 2, ["CH"] = 2, ["EK"] = 2, ["GE"] = 2, ["KR"] = 2, ["SP"] = 3 },
    };

    public static Schuldatei Beispielschule()
    {
        var d = NeueDatei("Städtisches Beispiel-Gymnasium", "GY");
        d.Einstellungen.Schulnummer = "199999";
        d.Einstellungen.Schulname2 = "Musterstadt";
        d.Einstellungen.Adresse = "Schulstraße 1, 40000 Musterstadt";
        Fach F(string k) => d.Faecher.First(f => f.Kurzname == k);

        // Gänge (Pausenaufsichtsbereiche)
        var hof = new Gang { Id = d.NeueId(), Kurzname = "Hof", Langname = "Schulhof", Bedarf = 2 };
        var halle = new Gang { Id = d.NeueId(), Kurzname = "Halle", Langname = "Pausenhalle", Bedarf = 1 };
        var og = new Gang { Id = d.NeueId(), Kurzname = "1.OG", Langname = "Flur 1. Obergeschoss", Bedarf = 1 };
        d.Gaenge.AddRange(new[] { hof, halle, og });

        // Räume mit Ausweichraum-Ring
        var klassenraeume = new List<Raum>();
        for (var r = 1; r <= 9; r++)
            klassenraeume.Add(new Raum { Id = d.NeueId(), Kurzname = $"R{100 + r}", Langname = $"Klassenraum {100 + r}", Kapazitaet = 30, Raumgewicht = 2, GangId = r <= 5 ? halle.Id : og.Id, Sortierung = r });
        for (var r = 0; r < klassenraeume.Count; r++) klassenraeume[r].AusweichraumId = klassenraeume[(r + 1) % klassenraeume.Count].Id;
        d.Raeume.AddRange(klassenraeume);
        Raum NeuerRaum(string k, string l, int gewicht, string art, int kap = 30)
        {
            var r = new Raum { Id = d.NeueId(), Kurzname = k, Langname = l, Raumgewicht = gewicht, Raumart = art, Kapazitaet = kap, GangId = og.Id, Sortierung = d.Raeume.Count };
            d.Raeume.Add(r);
            return r;
        }
        var th1 = NeuerRaum("TH1", "Turnhalle 1", 4, "Sporthalle", 60);
        var th2 = NeuerRaum("TH2", "Turnhalle 2", 4, "Sporthalle", 60);
        th1.AusweichraumId = th2.Id;
        th2.AusweichraumId = th1.Id;
        th1.GangId = th2.GangId = hof.Id;
        var ph = NeuerRaum("PH1", "Physiksaal", 3, "Fachraum");
        var ch = NeuerRaum("CH1", "Chemiesaal", 3, "Fachraum");
        var bi = NeuerRaum("BI1", "Biologieraum", 2, "Fachraum");
        var ku = NeuerRaum("KU1", "Kunstraum", 3, "Fachraum");
        var mu = NeuerRaum("MU1", "Musikraum", 3, "Fachraum");
        var edv1 = NeuerRaum("EDV1", "Computerraum 1", 4, "EDV");
        var edv2 = NeuerRaum("EDV2", "Computerraum 2", 4, "EDV");
        ph.AusweichraumId = ch.Id;
        // Raumgruppe
        var edv = new Raumgruppe { Id = d.NeueId(), Kurzname = "EDV", Langname = "EDV-Säle", RaumIds = new() { edv1.Id, edv2.Id } };
        var ober = new Raumgruppe { Id = d.NeueId(), Kurzname = "OS", Langname = "Kursräume Oberstufe", RaumIds = klassenraeume.Skip(5).Select(r => r.Id).ToList() };
        d.Raumgruppen.AddRange(new[] { edv, ober });
        F("SP").FachraumId = th1.Id;
        F("SP").Doppelstunden = new Bereich(1, 1);
        F("PH").FachraumId = ph.Id;
        F("CH").FachraumId = ch.Id;
        F("BI").FachraumId = bi.Id;
        F("KU").FachraumId = ku.Id;
        F("KU").Doppelstunden = new Bereich(1, 1);
        F("MU").FachraumId = mu.Id;
        F("IF").FachraumgruppeId = edv.Id;
        F("KR").Randstunde = true;
        F("ER").Randstunde = true;

        // Klassen mit Kernzeit 1.–5. Stunde
        var klassen = new List<Klasse>();
        var ri = 0;
        foreach (var jg in new[] { "5", "6", "7" })
            foreach (var z in new[] { "a", "b" })
            {
                var k = new Klasse
                {
                    Id = d.NeueId(), Kurzname = jg + z, Langname = $"Klasse {jg}{z}", Jahrgang = jg, StammraumId = klassenraeume[ri++].Id,
                    HauptfaecherProTag = 3, Mittagspause = new Bereich(1, 1), StundenProTag = new Bereich(4, 7), MaxNachmittage = 2,
                    SchuelerMaennlich = 13 + ri % 3, SchuelerWeiblich = 14 - ri % 2, Sortierung = klassen.Count,
                };
                for (var t = 0; t < 5; t++)
                    for (var s = 1; s <= 5; s++) k.Zeitwuensche.Set(t, s, 3);
                for (var t = 0; t < 5; t++) k.Zeitwuensche.Set(t, 8, -2);
                for (var t = 0; t < 5; t++) { k.Zeitwuensche.Set(t, 9, -3); k.Zeitwuensche.Set(t, 10, -3); }
                klassen.Add(k);
            }
        var ef = new Klasse { Id = d.NeueId(), Kurzname = "EF", Langname = "Einführungsphase", Jahrgang = "EF", Mittagspause = new Bereich(1, 2), Sortierung = klassen.Count, SchuelerMaennlich = 38, SchuelerWeiblich = 42 };
        for (var t = 0; t < 5; t++) ef.Zeitwuensche.Set(t, 10, -3);
        klassen.Add(ef);
        d.Klassen.AddRange(klassen);

        // Lehrer
        (string K, string N, string V, string[] Faecher, decimal Soll)[] lehrer =
        {
            ("MUE", "Müller", "Anna", new[] { "D", "GE" }, 25.5m), ("SCH", "Schmidt", "Thomas", new[] { "M", "PH" }, 25.5m),
            ("SNE", "Schneider", "Julia", new[] { "E", "SP" }, 25.5m), ("FIS", "Fischer", "Michael", new[] { "D", "E" }, 25.5m),
            ("WEB", "Weber", "Sabine", new[] { "M", "BI" }, 25.5m), ("MEY", "Meyer", "Andreas", new[] { "BI", "CH" }, 25.5m),
            ("WAG", "Wagner", "Katrin", new[] { "F", "E" }, 18m), ("BEC", "Becker", "Stefan", new[] { "D", "KR" }, 25.5m),
            ("SUL", "Schulz", "Nina", new[] { "M", "EK" }, 25.5m), ("HOF", "Hoffmann", "Markus", new[] { "KU", "D" }, 25.5m),
            ("KOC", "Koch", "Laura", new[] { "MU", "M" }, 25.5m), ("RIC", "Richter", "Jan", new[] { "SP", "BI" }, 25.5m),
            ("KLE", "Klein", "Claudia", new[] { "GE", "PK" }, 25.5m), ("WOL", "Wolf", "Tobias", new[] { "E", "GE" }, 25.5m),
            ("SRO", "Schröder", "Sarah", new[] { "D", "PK" }, 25.5m), ("NEU", "Neumann", "Frank", new[] { "M", "CH" }, 25.5m),
            ("SWA", "Schwarz", "Lena", new[] { "F", "D" }, 25.5m), ("BRA", "Braun", "Daniel", new[] { "SP", "EK" }, 25.5m),
            ("ZIM", "Zimmermann", "Petra", new[] { "ER", "GE" }, 25.5m), ("KRU", "Krüger", "Christian", new[] { "PH", "IF" }, 25.5m),
            ("HAR", "Hartmann", "Miriam", new[] { "E", "KR" }, 25.5m), ("LAN", "Lange", "Jörg", new[] { "D", "MU" }, 25.5m),
        };
        foreach (var (k, n, v, faecher, soll) in lehrer)
        {
            var l = new Lehrer
            {
                Id = d.NeueId(), Kurzname = k, Langname = n, Vorname = v, Wochensoll = soll, FaecherIds = faecher.Select(f => F(f).Id).ToList(),
                Hohlstunden = new Bereich(0, 3), StundenProTag = new Bereich(2, 6), MaxNachmittage = 2, AufsichtenMin = 1, AufsichtenMax = 3,
                Email = $"{k.ToLowerInvariant()}@beispiel-gymnasium.de", Sortierung = d.Lehrer.Count, VertretungenMaxWoche = 3,
            };
            d.Lehrer.Add(l);
        }
        Lehrer L(string k) => d.Lehrer.First(l => l.Kurzname == k);
        L("MUE").Ermaessigungen.Add(new Ermaessigung { Grund = "Schulleitung", Wert = 10 });
        L("SCH").Ermaessigungen.Add(new Ermaessigung { Grund = "Stellv. Schulleitung", Wert = 7 });
        L("BEC").Ermaessigungen.Add(new Ermaessigung { Grund = "Altersermäßigung", Wert = 1 });
        // Zeitwünsche: WAG donnerstags frei (Teilzeit), KOC "lieber nicht" in der 1. Stunde, RUB-ähnlich: ein unbestimmter freier Tag
        for (var s = 1; s <= 10; s++) L("WAG").Zeitwuensche.Set(3, s, -3);
        for (var t = 0; t < 5; t++) L("KOC").Zeitwuensche.Set(t, 1, -1);
        L("HOF").UnbestimmteZeitwuensche.Add(new UnbestimmterZeitwunsch { Bereich = ZeitbereichArt.Tage, Anzahl = 1, Wunsch = -3 });
        L("HOF").UnbestimmteZeitwuensche.Add(new UnbestimmterZeitwunsch { Bereich = ZeitbereichArt.Nachmittage, Anzahl = 2, Wunsch = -1 });
        L("MUE").KeineAufsicht = true;

        // Stundentafel
        foreach (var (jg, zeilen) in Stundentafel)
            foreach (var (fk, std) in zeilen) d.Stundentafel.Add(new StundentafelZeile { Jahrgang = jg, FachId = F(fk).Id, Stunden = std });

        // Unterricht (Sek I) – Lehrer nach Lehrbefähigung und freiem Deputat
        var last = new Dictionary<int, decimal>();
        Lehrer Waehle(string fach, Lehrer? ausser = null) =>
            d.Lehrer.Where(l => l.FaecherIds.Contains(F(fach).Id) && l != ausser).DefaultIfEmpty().Where(l => l != null)
                .Concat(d.Lehrer.Where(l => l != ausser)).Select(l => l!)
                .OrderByDescending(l => l.FaecherIds.Contains(F(fach).Id)).ThenBy(l => (last.GetValueOrDefault(l.Id) + 0.01m) / Math.Max(1, l.Soll)).First();
        foreach (var k in klassen.Where(k => k.Jahrgang != "EF"))
        {
            foreach (var (fk, std) in Stundentafel[k.Jahrgang])
            {
                if (fk == "KR") continue; // Religion gekoppelt (s. u.)
                var l = Waehle(fk);
                last[l.Id] = last.GetValueOrDefault(l.Id) + std;
                var f = F(fk);
                d.Unterrichte.Add(new Unterricht
                {
                    Id = d.NeueId(), Wochenstunden = std, Doppelstunden = f.Doppelstunden.IstLeer ? (std >= 4 && f.Hauptfach ? new Bereich(0, 1) : default) : f.Doppelstunden,
                    Zeilen = { new Kopplungszeile { LehrerId = l.Id, FachId = f.Id, KlassenIds = { k.Id }, FachraumId = f.FachraumId, FachraumgruppeId = f.FachraumgruppeId, StammraumId = k.StammraumId } },
                });
            }
        }
        // Gekoppelter Religionsunterricht je Jahrgang: KR und ER parallel (je eine Kopplungszeile)
        foreach (var jg in new[] { "5", "6", "7" })
        {
            var ks = klassen.Where(k => k.Jahrgang == jg).ToList();
            var kr = Waehle("KR");
            var er = L("ZIM");
            last[kr.Id] = last.GetValueOrDefault(kr.Id) + 2;
            last[er.Id] = last.GetValueOrDefault(er.Id) + 2;
            d.Unterrichte.Add(new Unterricht
            {
                Id = d.NeueId(), Wochenstunden = 2, Text = "Religion gekoppelt",
                Zeilen =
                {
                    new Kopplungszeile { LehrerId = kr.Id, FachId = F("KR").Id, KlassenIds = ks.Select(k => k.Id).ToList(), StammraumId = ks[0].StammraumId },
                    new Kopplungszeile { LehrerId = er.Id, FachId = F("ER").Id, KlassenIds = ks.Select(k => k.Id).ToList(), StammraumId = ks[1].StammraumId },
                },
            });
        }
        // Oberstufe EF: Kurse in Bändern (Kursplanung)
        (string Fach, string Art, string Lehrer, string Band)[] kurse =
        {
            ("D", "GK", "FIS", "EF-1"), ("M", "GK", "SUL", "EF-2"), ("E", "GK", "WOL", "EF-3"), ("BI", "GK", "MEY", "EF-4"),
            ("PH", "GK", "KRU", "EF-4"), ("GE", "GK", "KLE", "EF-5"), ("SP", "GK", "BRA", "EF-6"), ("IF", "GK", "KRU", "EF-5"),
            ("F", "GK", "SWA", "EF-3"), ("D", "GK", "SRO", "EF-2"), ("M", "GK", "NEU", "EF-1"), ("KU", "GK", "HOF", "EF-6"),
        };
        var nr = new Dictionary<string, int>();
        foreach (var (fk, art, lk, band) in kurse)
        {
            var f = F(fk);
            nr[fk] = nr.GetValueOrDefault(fk) + 1;
            d.Unterrichte.Add(new Unterricht
            {
                Id = d.NeueId(), Wochenstunden = Nrw.Kursarten[art].Stunden, Kursname = $"{fk}-{art}{nr[fk]}", Band = band, Doppelstunden = new Bereich(1, 1),
                Zeilen = { new Kopplungszeile { LehrerId = L(lk).Id, FachId = f.Id, KlassenIds = { ef.Id }, FachraumId = f.FachraumId, FachraumgruppeId = f.FachraumgruppeId ?? (f.FachraumId is null ? ober.Id : null) } },
            });
        }
        // Bereitschaftsstunden (Vertretungsreserve)
        foreach (var (lk, _) in new[] { ("RIC", 0), ("SNE", 1), ("NEU", 2), ("HAR", 3), ("LAN", 4) })
            d.Unterrichte.Add(new Unterricht { Id = d.NeueId(), Wochenstunden = 2, MehrmalsProTag = true, Text = "Bereitschaft", Zeilen = { new Kopplungszeile { LehrerId = L(lk).Id, FachId = F("BER").Id } } });

        // Klassenleitungen
        foreach (var k in klassen.Where(k => k.Jahrgang != "EF"))
            k.KlassenlehrerIds = d.Unterrichte.Where(u => u.KlassenIds.Contains(k.Id) && u.HauptfachId == F("D").Id).SelectMany(u => u.LehrerIds).Take(1).ToList();

        // Schüler der EF mit Kurswahlen
        var rnd = new Random(7);
        string[] vornamen = { "Lea", "Ben", "Mia", "Paul", "Emma", "Jonas", "Hanna", "Elias", "Lina", "Noah", "Sofia", "Finn", "Marie", "Luis", "Emilia", "Felix" };
        string[] nachnamen = { "Bauer", "Keller", "Vogel", "Jung", "Roth", "Frank", "Berg", "Lorenz", "Busch", "Otto", "Sommer", "Engel" };
        var efKurse = d.Unterrichte.Where(u => u.Kursname.Length > 0).ToList();
        for (var s = 0; s < 40; s++)
        {
            var sch = new Schueler { Id = d.NeueId(), Kurzname = nachnamen[s % nachnamen.Length], Vorname = vornamen[(s * 7) % vornamen.Length], KlasseId = ef.Id, Sortierung = s };
            foreach (var band in efKurse.GroupBy(u => u.Band))
                sch.KurswahlIds.Add(band.ElementAt(rnd.Next(band.Count())).Id);
            d.Schueler.Add(sch);
        }

        d.Tagestexte.Add(new Tagestext { Datum = new DateOnly(2026, 10, 14), Text = "Lehrerkonferenz um 14:15 Uhr in der Aula.", NurLehrer = true });
        Dokument.Normalisieren(d);
        return d;
    }
}
