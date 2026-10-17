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

/// <summary>
/// Abwesenheit einer Schülerin/eines Schülers in einer Stunde (Fehlzeit aus dem Klassenbuch von UntPlanWeb).
/// Datum, Stunde und Schüler sind eindeutig und verhindern Doppelimporte; ein erneuter Import aktualisiert den Eintrag
/// (z. B. Entschuldigungsstatus). WebId ist die zuletzt gemeldete Id auf dem Server.
/// </summary>
public sealed class SchuelerAbwesenheit
{
    public int WebId { get; set; }
    public int SchuelerId { get; set; }
    /// <summary>Name laut UntPlanWeb (Anzeige, falls die Schülerin/der Schüler in UntPlan fehlt).</summary>
    public string Name { get; set; } = "";
    public string Klasse { get; set; } = "";
    public DateOnly Datum { get; set; }
    public int Stunde { get; set; }
    public int? UnterrichtId { get; set; }
    public string Fach { get; set; } = "";
    /// <summary>true = verspätet (Minuten), false = fehlt.</summary>
    public bool Verspaetet { get; set; }
    public int Minuten { get; set; }
    /// <summary>offen, entschuldigt, unentschuldigt</summary>
    public string Status { get; set; } = "offen";
    public string Bemerkung { get; set; } = "";
    public string Lehrer { get; set; } = "";
    public DateTime Importiert { get; set; } = DateTime.Now;
}

/// <summary>Klassenbucheintrag einer Stunde aus UntPlanWeb (Lehrstoff, Hausaufgabe) – Gegenstück zum Vorbild-Kursimport.</summary>
public sealed class KlassenbuchEintrag
{
    public int WebId { get; set; }
    public DateOnly Datum { get; set; }
    public int Stunde { get; set; }
    public int? UnterrichtId { get; set; }
    public List<string> Klassen { get; set; } = new();
    public string Fach { get; set; } = "";
    public string Lehrer { get; set; } = "";
    public string Lehrstoff { get; set; } = "";
    public string Hausaufgabe { get; set; } = "";
    public DateOnly? Faellig { get; set; }
    /// <summary>Anwesenheit in UntPlanWeb geprüft.</summary>
    public bool AnwesenheitGeprueft { get; set; }
    public string Geaendert { get; set; } = "";
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
    /// <summary>Raumbuchung (z. B. aus UntPlanWeb): Lehrer bucht einen Raum zu einer Stunde.</summary>
    Raumbuchung,
    /// <summary>„verlegt“: durch die Verlegung einer Unterrichtsstunde entstandene (offene) Vertretung.</summary>
    Verlegt,
    /// <summary>„Unterricht“: regulär stattfindender Unterricht (nur Anzeige im Vertretungsfenster, wird nicht gespeichert).</summary>
    Unterricht,
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
    /// <summary>Beschreibung (Spalte „Beschr.“), z. B. aus den vordefinierten Beschreibungen.</summary>
    public string Beschreibung { get; set; } = "";
    /// <summary>
    /// Veranstaltung: Platz einer Lehrkraft, die wegen einer Absenz aus der Veranstaltung entfernt wurde („?“ beim Vertreter).
    /// VertreterId ist die eingetragene Ersatzlehrkraft oder null (offen).
    /// </summary>
    public int? ErsatzFuerLehrerId { get; set; }
    /// <summary>Unterricht findet als Veranstaltung statt (Lehrkraft und Klasse nehmen gemeinsam teil) – wird in der Liste nicht gezeigt.</summary>
    public bool InVeranstaltung { get; set; }
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public DateTime Geaendert { get; set; } = DateTime.Now;
    public string Benutzer { get; set; } = "";
    /// <summary>
    /// Der Raum wurde für einen anderen Unterricht freigegeben („Raum ohne Kollision verplanen“): Die Stunde hat keinen
    /// Raum mehr, bis ein Ersatzraum eingetragen ist (offene Raumvertretung „???“).
    /// </summary>
    public bool RaumVerdraengt { get; set; }
    /// <summary>
    /// Die Klassen sind abwesend (Veranstaltung bzw. Klassenabsenz): Die Zeile wurde deshalb zur Freisetzung bzw. zum Entfall.
    /// Hält die bisherige Art (z. B. Sondereinsatz, Unterricht geändert), die zurückkehrt, sobald die Klasse wieder anwesend ist.
    /// </summary>
    public VertretungsArt? ArtVorKlassenabwesenheit { get; set; }
    /// <summary>Herkunft aus UntPlanWeb (Kennung der übernommenen Stundenplanänderung), sonst leer.</summary>
    public string WebId { get; set; } = "";

    [JsonIgnore] public bool Offen => ((Art is VertretungsArt.Vertretung or VertretungsArt.Bereitschaftsvertretung or VertretungsArt.Pausenaufsicht or VertretungsArt.Verlegt) && VertreterId == null) ||
                                      VeranstaltungOffen;
    /// <summary>Veranstaltung, aus der eine Lehrkraft entfernt wurde und für die noch keine Ersatzlehrkraft eingetragen ist.</summary>
    [JsonIgnore] public bool VeranstaltungOffen => Art == VertretungsArt.Veranstaltung && ErsatzFuerLehrerId.HasValue && VertreterId == null;
    /// <summary>Raumvertretung wegen gesperrtem Raum, für die noch kein Ersatzraum eingetragen ist (offen wie eine fehlende Lehrkraft).</summary>
    [JsonIgnore] public bool RaumOffen => VertretungsraumId == null && ((Art == VertretungsArt.Raumvertretung && Quelle != VertretungsQuelle.Manuell) || (RaumVerdraengt && !IstEntfall));
    /// <summary>Offen im Sinne der Liste: Lehrkraft oder Raum fehlt.</summary>
    [JsonIgnore] public bool OffenOderRaumOffen => Offen || RaumOffen;
    [JsonIgnore] public bool IstEntfall => Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung or VertretungsArt.BereitschaftEntfaellt;
    [JsonIgnore] public IReadOnlyList<int> NeueKlassenIds => VertretungsKlassenIds ?? KlassenIds;

    /// <summary>Tiefe Kopie (eigene Listen) – schneller als eine JSON-Kopie, z. B. für Probeberechnungen.</summary>
    public Vertretung Kopie()
    {
        var k = (Vertretung)MemberwiseClone();
        k.KlassenIds = KlassenIds.ToList();
        k.VertretungsKlassenIds = VertretungsKlassenIds?.ToList();
        k.ZusatzraumIds = ZusatzraumIds.ToList();
        return k;
    }
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
    /// <summary>Unformatierter Text (Listen, Druck, Monitor).</summary>
    public string Text { get; set; } = "";
    /// <summary>Formatierter Text als Rich Text (Dialog „Tagestexte“ mit Fett, Kursiv, Unterstrichen und Farbe), leer = unformatiert.</summary>
    public string TextRtf { get; set; } = "";
    /// <summary>Formatierter Text als bereinigtes HTML-Subset (b, i, u, span style color, br, p) für den Export, leer = unformatiert.</summary>
    public string TextHtml { get; set; } = "";
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
    /// <summary>
    /// Klassen, von denen nur ein Teil teilnimmt (Spalte „Kl-Teil“): der reguläre Unterricht dieser Klassen findet parallel statt.
    /// Klassen in <see cref="KlassenIds"/> (Spalte „Klasse“) sind dagegen abwesend, ihr kollidierender Unterricht entfällt.
    /// </summary>
    public List<int> KlassenteilIds { get; set; } = new();
    /// <summary>Tageweise aus der Veranstaltung entfernte Lehrkräfte (Absenz) und ihre Ersatzlehrkräfte.</summary>
    public List<VeranstaltungsErsatz> Ersatz { get; set; } = new();
    /// <summary>Tageweise zusätzlich eingetragene Lehrkräfte (im Vertretungsfenster hinzugefügt, nicht als Ersatz).</summary>
    public List<VeranstaltungsZusatz> Zusatz { get; set; } = new();

    public bool Deckt(DateOnly datum, int stunde)
    {
        if (datum < Von || datum > Bis) return false;
        var von = datum == Von ? StundeVon : 1;
        var bis = datum == Bis ? StundeBis : 99;
        return stunde >= von && stunde <= bis;
    }

    /// <summary>Alle beteiligten Klassen (Klasse und Kl-Teil).</summary>
    [JsonIgnore] public IEnumerable<int> AlleKlassenIds => KlassenIds.Concat(KlassenteilIds).Distinct();

    /// <summary>Nimmt die Lehrkraft an diesem Tag teil (eingetragen und nicht entfernt, oder als Ersatz eingetragen)?</summary>
    public bool NimmtTeil(int lehrerId, DateOnly datum) =>
        (LehrerIds.Contains(lehrerId) && !Ersatz.Any(e => e.Datum == datum && e.LehrerId == lehrerId)) ||
        Ersatz.Any(e => e.Datum == datum && e.ErsatzId == lehrerId) || Zusatz.Any(z => z.Datum == datum && z.LehrerId == lehrerId);

    /// <summary>Teilnehmende Lehrkräfte an diesem Tag (in der Reihenfolge der Eingabe, Ersatzlehrkräfte an der Stelle der entfernten).</summary>
    public List<int> LehrerAm(DateOnly datum)
    {
        var res = new List<int>();
        foreach (var l in LehrerIds)
        {
            var e = Ersatz.FirstOrDefault(x => x.Datum == datum && x.LehrerId == l);
            if (e is null) res.Add(l);
            else if (e.ErsatzId is int ers) res.Add(ers);
        }
        res.AddRange(Zusatz.Where(z => z.Datum == datum).Select(z => z.LehrerId));
        return res.Distinct().ToList();
    }

    /// <summary>Entfernte Lehrkräfte an diesem Tag ohne Ersatz (offene Plätze, „?“).</summary>
    public List<int> OffenAm(DateOnly datum) => Ersatz.Where(e => e.Datum == datum && e.ErsatzId is null && LehrerIds.Contains(e.LehrerId)).Select(e => e.LehrerId).ToList();
}

/// <summary>Eine Lehrkraft nimmt an einem Tag zusätzlich an einer Veranstaltung teil.</summary>
public sealed class VeranstaltungsZusatz
{
    public DateOnly Datum { get; set; }
    public int LehrerId { get; set; }
}

/// <summary>Eine Lehrkraft wurde an einem Tag aus einer Veranstaltung entfernt (z. B. wegen Krankheit); optional mit Ersatzlehrkraft.</summary>
public sealed class VeranstaltungsErsatz
{
    public DateOnly Datum { get; set; }
    /// <summary>Entfernte Lehrkraft.</summary>
    public int LehrerId { get; set; }
    /// <summary>Ersatzlehrkraft, null = noch offen („???“).</summary>
    public int? ErsatzId { get; set; }
    /// <summary>Absenz, wegen der die Lehrkraft entfernt wurde.</summary>
    public int? AbwesenheitId { get; set; }
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
    /// <summary>Aus UntPlanWeb übernommene Prüfung (Kennung der Rückmeldung): nur Lehrkraft und Raum änderbar, keine Verlegung.</summary>
    public string WebId { get; set; } = "";
    /// <summary>
    /// Zur Prüfung gewordene Buchungen (Raumbuchung, Raumänderung des Unterrichts aus UntPlanWeb): wird die Prüfung gelöscht bzw.
    /// verlegt, kehren sie zurück.
    /// </summary>
    public List<Vertretung> Buchungen { get; set; } = new();

    [JsonIgnore] public bool Importiert => !string.IsNullOrEmpty(WebId);
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
