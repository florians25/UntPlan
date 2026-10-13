using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

public enum AbwesenheitsArt
{
    Lehrer,
    Klasse,
    Raum,
}

/// <summary>Absenz eines Lehrers, einer Klasse oder eines Raumes (Datum/Stunde von–bis).</summary>
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
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public string Benutzer { get; set; } = "";

    public bool Deckt(DateOnly datum, int stunde)
    {
        if (datum < Von || datum > Bis) return false;
        var von = datum == Von ? StundeVon : 1;
        var bis = datum == Bis ? StundeBis : 99;
        return stunde >= von && stunde <= bis;
    }

    /// <summary>Anzahl Kalendertage.</summary>
    [JsonIgnore] public int Tage => Bis.DayNumber - Von.DayNumber + 1;
}

/// <summary>Vertretungsarten (Spalte "Art" im Vertretungsfenster).</summary>
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
    /// <summary>"+" als Vertreter: bearbeitet, aber ohne Lehrkraft.</summary>
    VertretungOhneLehrer,
    Bereitschaftsvertretung,
    BereitschaftEntfaellt,
    EigenverantwortlichesArbeiten,
    Lehrertausch,
    Teilvertretung,
    TrotzAbsenz,
    UnterrichtGeaendert,
    Veranstaltung,
    SondereinsatzVormerkung,
    Vormerkung,
}

/// <summary>Woraus eine Vertretungszeile entstanden ist.</summary>
public enum VertretungsQuelle
{
    /// <summary>Aus einer Absenz (Lehrer/Klasse/Raum) – wird beim Löschen der Absenz entfernt.</summary>
    Absenz,
    Veranstaltung,
    Manuell,
    Verlegung,
    Klausur,
    Sondereinsatz,
    Unterrichtsfrei,
}

public sealed class Vertretung
{
    /// <summary>Vertretungsnummer (Vtr-Nr.).</summary>
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public int Stunde { get; set; }
    public VertretungsArt Art { get; set; }
    public VertretungsQuelle Quelle { get; set; }
    /// <summary>Vom Planer bearbeitet (wird bei Neuberechnung nicht überschrieben).</summary>
    public bool Bearbeitet { get; set; }
    public int? UnterrichtId { get; set; }
    public int ZeilenIndex { get; set; }
    /// <summary>Ursprünglicher (abwesender) Lehrer – Spalte "(Lehrer)".</summary>
    public int? LehrerId { get; set; }
    public int? VertreterId { get; set; }
    public int? FachId { get; set; }
    public int? VertretungsfachId { get; set; }
    public List<int> KlassenIds { get; set; } = new();
    /// <summary>Klassen der Vertretung, falls abweichend (Vertretung teilen).</summary>
    public List<int>? VertretungsKlassenIds { get; set; }
    public int? RaumId { get; set; }
    public int? VertretungsraumId { get; set; }
    /// <summary>Zusätzliche Räume (Zusätzlichen Raum zuordnen).</summary>
    public List<int> ZusatzraumIds { get; set; } = new();
    public string Text { get; set; } = "";
    public string Text2 { get; set; } = "";
    public string Grund { get; set; } = "";
    public int? GrundId { get; set; }
    /// <summary>Zählt als Mehrarbeit (nicht bei Bereitschaft/Freisetzung).</summary>
    public bool Mehrarbeit { get; set; }
    /// <summary>Verlegung: Zielposition ("(Le.) nach").</summary>
    public DateOnly? NachDatum { get; set; }
    public int? NachStunde { get; set; }
    /// <summary>Verlegung: Herkunft ("Vertr. von").</summary>
    public DateOnly? VonDatum { get; set; }
    public int? VonStunde { get; set; }
    /// <summary>Verknüpft beide Zeilen einer Verlegung/eines Tausches.</summary>
    public int? VerlegungsId { get; set; }
    /// <summary>Pausenaufsicht-Vertretung: Gang.</summary>
    public int? GangId { get; set; }
    public int? AbwesenheitId { get; set; }
    public int? VeranstaltungId { get; set; }
    public int? VormerkungId { get; set; }
    public int? KlausurId { get; set; }
    /// <summary>Statistik-Kennzeichen (z. B. "n" = nicht zählen, "b" = bezahlt).</summary>
    public string Statistik { get; set; } = "";
    /// <summary>Vertretungskopplung (gleiche Ziffer = gleicher Vertreter und Raum).</summary>
    public int Kopplung { get; set; }
    public bool Fixiert { get; set; }
    public bool NichtDrucken { get; set; }
    /// <summary>Mitbetreuung: Vertreter hält gleichzeitig eigenen Unterricht.</summary>
    public bool Mitbetreuung { get; set; }
    /// <summary>Abweichender Wert für die Zählung (null = Standard).</summary>
    public decimal? Wert { get; set; }
    public string Schuelergruppe { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public DateTime Geaendert { get; set; } = DateTime.Now;
    public string Benutzer { get; set; } = "";

    [JsonIgnore] public bool Offen => (Art is VertretungsArt.Vertretung or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Pausenaufsicht) && VertreterId == null;
    [JsonIgnore] public bool IstEntfall => Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung or VertretungsArt.BereitschaftEntfaellt;
    [JsonIgnore] public IReadOnlyList<int> NeueKlassenIds => VertretungsKlassenIds ?? KlassenIds;
}

public sealed class Pausenaufsicht
{
    public int GangId { get; set; }
    public int Tag { get; set; }
    public int NachStunde { get; set; }
    public int LehrerId { get; set; }
    /// <summary>Teil einer geteilten Aufsicht (1, 2, …).</summary>
    public int Teil { get; set; } = 1;
    /// <summary>Nur an diesem Datum (datumsspezifischer Eintrag), sonst wöchentlich.</summary>
    public DateOnly? Datum { get; set; }
    /// <summary>Unterrichtsgruppe (Woche A/B …), null = jede Woche.</summary>
    public int? UGruppeId { get; set; }
}

/// <summary>Bedarf einer Pausenaufsicht (Zelle "???" bzw. grau = keine Aufsicht).</summary>
public sealed class Aufsichtsbedarf
{
    public int GangId { get; set; }
    public int Tag { get; set; }
    public int NachStunde { get; set; }
    /// <summary>Anzahl Teile (geteilte Aufsicht), 0 = keine Aufsicht.</summary>
    public int Teile { get; set; } = 1;
    /// <summary>Abweichende Minuten (Modus "Minuten anzeigen"), null = Pausendauer.</summary>
    public int? Minuten { get; set; }
}

public enum TagestextArt
{
    Alle,
    Klassen,
    Lehrer,
    /// <summary>Nur für das Kollegium (Lehrerliste).</summary>
    NurLehrer,
}

public sealed class Tagestext
{
    public DateOnly Datum { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Veraltet, ersetzt durch Art = NurLehrer.</summary>
    public bool NurLehrer { get; set; }
    public TagestextArt Art { get; set; }
    public List<int> ElementIds { get; set; } = new();
    /// <summary>Gültig für so viele Tage ab Datum (1 = nur dieser Tag).</summary>
    public int GueltigTage { get; set; } = 1;
    public bool Portal { get; set; } = true;

    public bool GiltAm(DateOnly d) => d >= Datum && d < Datum.AddDays(Math.Max(1, GueltigTage));
    [JsonIgnore] public bool FuerLehrer => NurLehrer || Art is TagestextArt.NurLehrer or TagestextArt.Lehrer;
}

/// <summary>Kalendereintrag eines Tages: unterrichtsfrei, Feiertag, freie Stunden, Notiz.</summary>
public sealed class Kalendertag
{
    public DateOnly Datum { get; set; }
    public bool Unterrichtsfrei { get; set; }
    public bool Feiertag { get; set; }
    /// <summary>"Nur diese Stunden unterrichtsfrei", z. B. "6-8" oder "1,5".</summary>
    public string FreieStunden { get; set; } = "";
    public int? GrundId { get; set; }
    /// <summary>Notiz des Vertretungsplaners (wird nicht gedruckt).</summary>
    public string Notiz { get; set; } = "";

    public bool StundeFrei(int stunde)
    {
        if (Unterrichtsfrei || Feiertag) return true;
        foreach (var teil in FreieStunden.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var b = Bereich.Parse(teil);
            if (!b.IstLeer && stunde >= b.Min && stunde <= b.Max) return true;
        }
        return false;
    }
}

/// <summary>Veranstaltung: Klassen, Lehrer und Räume sind in der Zeit absent (Exkursion, Klassenfahrt, Schulveranstaltung).</summary>
public sealed class Veranstaltung
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public int StundeVon { get; set; } = 1;
    public int StundeBis { get; set; } = 99;
    public List<int> KlassenIds { get; set; } = new();
    public List<int> LehrerIds { get; set; } = new();
    public List<int> RaumIds { get; set; } = new();
    public int? GrundId { get; set; }
    /// <summary>Nur ein Teil der Klasse (Schülergruppe) nimmt teil.</summary>
    public string Schuelergruppe { get; set; } = "";
    /// <summary>Zusätzliche Stunden der Lehrer zählen positiv.</summary>
    public bool Zaehlen { get; set; }
    public decimal? Wert { get; set; }

    public bool Deckt(DateOnly datum, int stunde)
    {
        if (datum < Von || datum > Bis) return false;
        var von = datum == Von ? StundeVon : 1;
        var bis = datum == Bis ? StundeBis : 99;
        return stunde >= von && stunde <= bis;
    }
}

public enum VormerkungsQuelle
{
    Konkret,
    Unterricht,
    Verdraengung,
    Vertretung,
    Stundenplan,
}

/// <summary>Vormerkung: Unterrichtsstunden, die zu einem späteren Zeitpunkt verplant werden.</summary>
public sealed class Vormerkung
{
    public int Id { get; set; }
    public VormerkungsQuelle Quelle { get; set; }
    public int Stunden { get; set; } = 1;
    public int? FachId { get; set; }
    public List<int> LehrerIds { get; set; } = new();
    public List<int> KlassenIds { get; set; } = new();
    public int? RaumId { get; set; }
    public int? UnterrichtId { get; set; }
    /// <summary>Ursprüngliche Position (verdrängte/verschobene Stunde).</summary>
    public DateOnly? Datum { get; set; }
    public int? Stunde { get; set; }
    public DateOnly? GueltigVon { get; set; }
    public DateOnly? GueltigBis { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Bereits verplante Termine.</summary>
    public List<DateOnly> VerplantAm { get; set; } = new();

    [JsonIgnore] public int Offen => Math.Max(0, Stunden - VerplantAm.Count);
}

/// <summary>Klausur (Prüfung) mit Aufsicht.</summary>
public sealed class Klausur
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public DateOnly Datum { get; set; }
    public int StundeVon { get; set; } = 1;
    public int StundeBis { get; set; } = 1;
    public List<int> KlassenIds { get; set; } = new();
    public List<int> LehrerIds { get; set; } = new();
    public List<int> RaumIds { get; set; } = new();
    public int? UnterrichtId { get; set; }
    /// <summary>Kollidierender Unterricht der Klassen entfällt.</summary>
    public bool UnterrichtEntfaellt { get; set; } = true;
    public bool Aktiv { get; set; } = true;
}

/// <summary>Bedarf an Bereitschaften je Tag und Stunde (Bereitschaftsplanung).</summary>
public sealed class Bereitschaftsbedarf
{
    public int Tag { get; set; }
    public int Stunde { get; set; }
    public int Anzahl { get; set; } = 1;
}

/// <summary>Unterrichtsgruppe (Mehrwochenstundenplan): Wochen A/B/…, Zeitraum, Unterbrechungen.</summary>
public sealed class UGruppe
{
    public int Id { get; set; }
    public string Kurzname { get; set; } = "";
    public string Langname { get; set; } = "";
    public DateOnly? Von { get; set; }
    public DateOnly? Bis { get; set; }
    /// <summary>Aktive Wochen als Buchstaben, z. B. "A" oder "AC"; leer = jede Woche.</summary>
    public string Wochen { get; set; } = "";
    public List<Zeitraum> Unterbrechungen { get; set; } = new();
    public bool Fix { get; set; }
    public bool Ignorieren { get; set; }
}

public sealed class Zeitraum
{
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
}

public enum ZaehlZeitraum
{
    Woche,
    Monat,
    Halbjahr,
    Schuljahr,
    Benutzerdefiniert,
}

public enum ZaehlArt
{
    Anzahl,
    Werte,
}

/// <summary>Einstellungen zum Vertretungszähler.</summary>
public sealed class ZaehlerEinstellungen
{
    public bool VertretungenZaehlen { get; set; } = true;
    public bool SondereinsaetzeZaehlen { get; set; } = true;
    public bool FreisetzungenZaehlen { get; set; } = true;
    public bool EntfaelleZaehlen { get; set; } = true;
    public bool PausenaufsichtenZaehlen { get; set; }
    public bool BetreuungenHalb { get; set; }
    public ZaehlZeitraum Zeitraum { get; set; } = ZaehlZeitraum.Monat;
    public DateOnly? ZeitraumVon { get; set; }
    public DateOnly? ZeitraumBis { get; set; }
    public ZaehlArt WieZaehlen { get; set; } = ZaehlArt.Anzahl;
    /// <summary>Entfälle nur zählen, wenn die Absenz einen Absenzgrund mit "Entfall zählen" hat.</summary>
    public bool EntfallNurMitGrund { get; set; } = true;
    public string NichtZaehlenKennzeichen { get; set; } = "n";
    public string BezahltKennzeichen { get; set; } = "b";
    public bool StattVertretungNeutral { get; set; } = true;
    public bool KlausurenNeutral { get; set; }
}

/// <summary>Einstellungen zum Vertretungsvorschlag.</summary>
public sealed class VorschlagEinstellungen
{
    /// <summary>Gewichte 0–99 (Bonus) bzw. Faktoren (Malus).</summary>
    public int Freisetzung { get; set; } = 60;
    public int Bereitschaft { get; set; } = 80;
    public int HatKlasse { get; set; } = 30;
    public int HatFach { get; set; } = 20;
    public int UnterrichtsstundenAmTag { get; set; } = 2;
    public int MerkerFaktor { get; set; } = 6;
    public int SperreFaktor { get; set; } = 10;
    public int ZaehlerFaktor { get; set; } = 4;
    /// <summary>Statistik-Kennzeichen → Punkte (positiv = rückreihen).</summary>
    public Dictionary<string, int> StatistikPunkte { get; set; } = new();
    public int RotWennMerkerGroesser { get; set; } = 8;
    public int NichtAnzeigenWennMerkerGroesser { get; set; } = 9;
    public int RotWennZaehlerGroesser { get; set; } = 10;
    public bool GesperrteNichtAnzeigen { get; set; } = true;
    public bool Langname { get; set; }
    public bool KlasseAuchFuerKlassenteil { get; set; } = true;
    public bool Raumgewicht4Beachten { get; set; } = true;
    public VorziehungsModus Vorziehungen { get; set; } = VorziehungsModus.GemaessVertretungszeitraster;
    public bool NurSpaeterenUnterricht { get; set; }
    public bool NurDieselbeWoche { get; set; }
}

public enum VorziehungsModus
{
    GemaessVertretungszeitraster,
    AusRandstunden,
    AlleVerlegungen,
}

/// <summary>Vertretungszeitraster je Stunde: Standard (vertreten), Entfall, eigenverantwortliches Arbeiten.</summary>
public enum VertretungsRegel
{
    Vertreten = 0,
    Entfall = 1,
    Eigenverantwortlich = 2,
}

/// <summary>Einstellungen der Vertretungsplanung (Modul).</summary>
public sealed class VertretungsEinstellungen
{
    /// <summary>Globaler Vertretungszeitraster: Schlüssel Tag*100+Stunde; fehlt = vertreten.</summary>
    public Dictionary<int, VertretungsRegel> Zeitraster { get; set; } = new();
    public ZaehlerEinstellungen Zaehler { get; set; } = new();
    public VorschlagEinstellungen Vorschlag { get; set; } = new();
    /// <summary>Raum für eigenverantwortliches Arbeiten.</summary>
    public int? EvaRaumId { get; set; }
    /// <summary>Lehrer-Raum-Prinzip: Raum des Vertreters verwenden.</summary>
    public bool LehrerRaumPrinzip { get; set; }
    /// <summary>Freisetzungen erzeugen Pausenaufsichtsvertretungen.</summary>
    public bool FreisetzungPausenaufsicht { get; set; } = true;
    /// <summary>Bezeichnungen und Farben je Vertretungsart.</summary>
    public Dictionary<string, string> Bezeichnungen { get; set; } = new();
    public Dictionary<string, int> Farben { get; set; } = new();
    public bool MitAktuellemDatumStarten { get; set; } = true;

    public VertretungsRegel Regel(int tag, int stunde) => Zeitraster.TryGetValue(tag * 100 + stunde, out var r) ? r : VertretungsRegel.Vertreten;
}

/// <summary>Wertkorrektur eines Lehrers zu einem Datum (Vertretungszähler oder Unterrichtswert).</summary>
public sealed class Wertkorrektur
{
    public DateOnly Datum { get; set; }
    /// <summary>V = Vertretungen, Y = Sondereinsätze, E = Entfälle, + / - = Unterrichtswert.</summary>
    public string Art { get; set; } = "V";
    public decimal Wert { get; set; }
    public string Text { get; set; } = "";
}
