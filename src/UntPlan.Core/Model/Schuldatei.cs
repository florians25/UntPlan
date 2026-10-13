using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

public enum Tageszeit
{
    Leer = 0,
    Vormittag = 1,
    Nachmittag = 2,
}

public sealed class Stundenzeit
{
    public int Nr { get; set; }
    public string Bezeichnung { get; set; } = "";
    public TimeOnly Beginn { get; set; }
    public TimeOnly Ende { get; set; }
}

public sealed class Pause
{
    /// <summary>Pause liegt nach dieser Stunde (zwischen Stunde und Stunde+1).</summary>
    public int NachStunde { get; set; }
    public string Bezeichnung { get; set; } = "";
    public int DauerMinuten { get; set; }
    /// <summary>Pausenaufsicht erforderlich.</summary>
    public bool Aufsicht { get; set; } = true;
}

/// <summary>Zeitraster: Tage pro Woche, Stunden pro Tag, Vormittag/Nachmittag/leer, Zeiten, Pausen, Mittagspause.</summary>
public sealed class Zeitraster
{
    public int TageProWoche { get; set; } = 5;
    public int StundenProTag { get; set; } = 10;
    /// <summary>0 = Montag.</summary>
    public int ErsterSchultag { get; set; }
    /// <summary>Nummer der ersten Stunde (0 oder 1).</summary>
    public int ErsteStundennummer { get; set; } = 1;
    public int MaxNachmittagsstunden { get; set; } = 4;
    public List<Stundenzeit> Zeiten { get; set; } = new();
    /// <summary>Tageszeit je Tag/Stunde, Schlüssel Tag*100+Stunde. Fehlt ein Eintrag gilt Standard (Vormittag bis Grenze).</summary>
    public Dictionary<int, Tageszeit> Belegung { get; set; } = new();
    /// <summary>Letzte Vormittagsstunde (Vormittags-/Nachmittagsgrenze).</summary>
    public int LetzteVormittagsstunde { get; set; } = 6;
    public List<Pause> Pausen { get; set; } = new();
    /// <summary>Flexible Mittagspause: früheste und späteste Stunde, die als Mittagspause dienen kann (0 = keine).</summary>
    public int MittagspauseVon { get; set; }
    public int MittagspauseBis { get; set; }
    public int MaxKlassenGleicheMittagspause { get; set; }
    public int NormalstundeMinuten { get; set; } = 45;

    public Tageszeit Art(int tag, int stunde)
    {
        if (tag < 0 || tag >= TageProWoche || stunde < 1 || stunde > StundenProTag) return Tageszeit.Leer;
        if (Belegung.TryGetValue(tag * 100 + stunde, out var t)) return t;
        return stunde <= LetzteVormittagsstunde ? Tageszeit.Vormittag : Tageszeit.Nachmittag;
    }

    public bool Verfuegbar(int tag, int stunde) => Art(tag, stunde) != Tageszeit.Leer;

    public Stundenzeit? Zeit(int stunde) => Zeiten.FirstOrDefault(z => z.Nr == stunde);

    public string StundenLabel(int stunde) => (stunde - 1 + ErsteStundennummer).ToString();
}

public sealed class Ferien
{
    public int Id { get; set; }
    public string Kurzname { get; set; } = "";
    public string Langname { get; set; } = "";
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public bool Feiertag { get; set; }
}

public sealed class Einstellungen
{
    public string Schulname { get; set; } = "";
    public string Schulname2 { get; set; } = "";
    public string Adresse { get; set; } = "";
    public string Schulnummer { get; set; } = "";
    public string Land { get; set; } = "Deutschland";
    public string Region { get; set; } = "Nordrhein-Westfalen";
    public string Schulform { get; set; } = "GY";
    public DateOnly SchuljahrVon { get; set; } = new(2026, 9, 2);
    public DateOnly SchuljahrBis { get; set; } = new(2027, 7, 16);
    /// <summary>Wochenperiodizität (A/B-Wochen) aktiv.</summary>
    public int Wochenperiodizitaet { get; set; } = 1;
    public bool TagesgenaueWertrechnung { get; set; }
    /// <summary>NRW: Mehrarbeit vergütungsfähig, wenn im Kalendermonat mehr als diese Anzahl Stunden.</summary>
    public int Mehrarbeitsgrenze { get; set; } = 3;
    /// <summary>Entfall wird in Randstunden ab diesem Jahrgang vorgeschlagen (Sek I: verlässliche Schule).</summary>
    public string EntfallAbJahrgang { get; set; } = "EF";
    public string Vertretungsplantitel { get; set; } = "Vertretungsplan";
    public Dictionary<string, int> Vertretungsgewichtung { get; set; } = new();
    public PortalEinstellungen Portal { get; set; } = new();
    public string Farbschema { get; set; } = "Blau";
}

public sealed class PortalEinstellungen
{
    public string Adresse { get; set; } = "";
    public string SchulId { get; set; } = "";
    public string ApiSchluessel { get; set; } = "";
    public int TageVoraus { get; set; } = 7;
    public DateTime? LetzteSynchronisation { get; set; }
    public int Revision { get; set; }
}

public enum AbwesenheitsArt
{
    Lehrer,
    Klasse,
    Raum,
}

public sealed class Abwesenheit
{
    public int Id { get; set; }
    public AbwesenheitsArt Art { get; set; }
    public int ElementId { get; set; }
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public int StundeVon { get; set; } = 1;
    public int StundeBis { get; set; } = 99;
    public int? GrundId { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Begleitende Lehrer (z. B. Klassenfahrt) – gelten ebenfalls als abwesend.</summary>
    public List<int> BegleitLehrerIds { get; set; } = new();

    public bool Deckt(DateOnly datum, int stunde)
    {
        if (datum < Von || datum > Bis) return false;
        var von = datum == Von ? StundeVon : 1;
        var bis = datum == Bis ? StundeBis : 99;
        return stunde >= von && stunde <= bis;
    }
}

public enum VertretungsArt
{
    Vertretung,
    Entfall,
    Raumvertretung,
    Verlegung,
    StattVertretung,
    Betreuung,
    Sondereinsatz,
    Freisetzung,
    Tausch,
    Pausenaufsicht,
    Klausur,
}

public sealed class Vertretung
{
    /// <summary>Vertretungsnummer.</summary>
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public int Stunde { get; set; }
    public VertretungsArt Art { get; set; }
    public int? UnterrichtId { get; set; }
    public int ZeilenIndex { get; set; }
    /// <summary>Ursprünglicher (abwesender) Lehrer.</summary>
    public int? LehrerId { get; set; }
    public int? VertreterId { get; set; }
    public int? FachId { get; set; }
    public int? VertretungsfachId { get; set; }
    public List<int> KlassenIds { get; set; } = new();
    public int? RaumId { get; set; }
    public int? VertretungsraumId { get; set; }
    public string Text { get; set; } = "";
    public string Grund { get; set; } = "";
    /// <summary>Zählt als Mehrarbeit (nicht bei Bereitschaft/Freisetzung).</summary>
    public bool Mehrarbeit { get; set; }
    public DateOnly? NachDatum { get; set; }
    public int? NachStunde { get; set; }
    /// <summary>Pausenaufsicht-Vertretung: Gang.</summary>
    public int? GangId { get; set; }
    public DateTime Erstellt { get; set; } = DateTime.Now;

    [JsonIgnore] public bool Offen => Art == VertretungsArt.Vertretung && VertreterId == null;
}

public sealed class Pausenaufsicht
{
    public int GangId { get; set; }
    public int Tag { get; set; }
    public int NachStunde { get; set; }
    public int LehrerId { get; set; }
}

public sealed class Tagestext
{
    public DateOnly Datum { get; set; }
    public string Text { get; set; } = "";
    public bool NurLehrer { get; set; }
}

public sealed class Kommentar
{
    public int Id { get; set; }
    public ElementTyp ElementTyp { get; set; }
    public int ElementId { get; set; }
    public string Feld { get; set; } = "";
    /// <summary>Kategorie 1–8 (Farbe frei definierbar).</summary>
    public int Kategorie { get; set; } = 1;
    public string Text { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
}

/// <summary>Gespeicherte Ansicht eines Stammdaten-/Unterrichtsfensters (eingeblendete Spalten).</summary>
public sealed class Ansicht
{
    public string Fenster { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Spalten { get; set; } = new();
    public List<Sortierkriterium> Sortierung { get; set; } = new();
}

public sealed class Sortierkriterium
{
    public string Feld { get; set; } = "";
    public bool Absteigend { get; set; }
}

public sealed class StundentafelZeile
{
    public string Jahrgang { get; set; } = "";
    public int FachId { get; set; }
    public decimal Stunden { get; set; }
}

public sealed class KommentarKategorie
{
    public int Nr { get; set; }
    public string Name { get; set; } = "";
    public int Farbe { get; set; }
}

/// <summary>Wurzelobjekt einer UntPlan-Schuldatei (*.untplan).</summary>
public sealed class Schuldatei
{
    public const int AktuelleVersion = 1;
    public int Version { get; set; } = AktuelleVersion;
    public Einstellungen Einstellungen { get; set; } = new();
    public Zeitraster Zeitraster { get; set; } = new();
    public List<Ferien> Ferien { get; set; } = new();
    public List<Klasse> Klassen { get; set; } = new();
    public List<Lehrer> Lehrer { get; set; } = new();
    public List<Raum> Raeume { get; set; } = new();
    public List<Fach> Faecher { get; set; } = new();
    public List<Raumgruppe> Raumgruppen { get; set; } = new();
    public List<Gang> Gaenge { get; set; } = new();
    public List<Abteilung> Abteilungen { get; set; } = new();
    public List<Abwesenheitsgrund> Abwesenheitsgruende { get; set; } = new();
    public List<Schueler> Schueler { get; set; } = new();
    public List<Unterricht> Unterrichte { get; set; } = new();
    public List<Periode> Perioden { get; set; } = new();
    public int AktivePeriodeId { get; set; }
    public Dictionary<string, int> Gewichtung { get; set; } = new();
    public List<Abwesenheit> Abwesenheiten { get; set; } = new();
    public List<Vertretung> Vertretungen { get; set; } = new();
    public List<Pausenaufsicht> Pausenaufsichten { get; set; } = new();
    public List<Tagestext> Tagestexte { get; set; } = new();
    public List<Kommentar> Kommentare { get; set; } = new();
    public List<KommentarKategorie> KommentarKategorien { get; set; } = new();
    public List<Ansicht> Ansichten { get; set; } = new();
    public List<StundentafelZeile> Stundentafel { get; set; } = new();
    public List<StundenplanFormat> Formate { get; set; } = new();
    public int NaechsteId { get; set; } = 1;

    public int NeueId() => NaechsteId++;

    [JsonIgnore]
    public Periode AktivePeriode
    {
        get
        {
            if (Perioden.Count == 0) Perioden.Add(new Periode { Id = NeueId(), Name = "Periode 1", Von = Einstellungen.SchuljahrVon });
            return Perioden.FirstOrDefault(p => p.Id == AktivePeriodeId) ?? Perioden[0];
        }
    }

    /// <summary>Stundenplan der aktiven Periode.</summary>
    [JsonIgnore] public List<Planstunde> Stundenplan => AktivePeriode.Plan;

    /// <summary>Periode, die an einem Datum gilt.</summary>
    public Periode PeriodeAm(DateOnly datum)
    {
        var p = Perioden.Where(x => x.Von <= datum).OrderByDescending(x => x.Von).FirstOrDefault();
        return p ?? AktivePeriode;
    }

    public IEnumerable<Element> AlleElemente(ElementTyp typ) => typ switch
    {
        ElementTyp.Klasse => Klassen,
        ElementTyp.Lehrer => Lehrer,
        ElementTyp.Raum => Raeume,
        ElementTyp.Fach => Faecher,
        ElementTyp.Raumgruppe => Raumgruppen,
        ElementTyp.Gang => Gaenge,
        ElementTyp.Abteilung => Abteilungen,
        ElementTyp.Abwesenheitsgrund => Abwesenheitsgruende,
        ElementTyp.Schueler => Schueler,
        _ => Enumerable.Empty<Element>(),
    };
}
