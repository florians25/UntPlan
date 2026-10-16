using System.Text.Json.Serialization;

namespace UntPlan.Core.Model;

/// <summary>
/// Kopplungszeile eines Unterrichts. Ein Unterricht ist gekoppelt, wenn mehr als ein Lehrer und/oder mehr als eine Klasse
/// beteiligt sind. Merksatz: Mehrere Klassen durch Komma getrennt, jeder weitere Lehrer in eine eigene Kopplungszeile.
/// </summary>
public sealed class Kopplungszeile
{
    public int? LehrerId { get; set; }
    public int? FachId { get; set; }
    public List<int> KlassenIds { get; set; } = new();
    public int? FachraumId { get; set; }
    public int? FachraumgruppeId { get; set; }
    public int? StammraumId { get; set; }
    public int? StammraumgruppeId { get; set; }
    /// <summary>Abweichender Wert dieser Zeile (Wertrechnung), null = Wochenstunden × Faktor.</summary>
    public decimal? Wert { get; set; }
    public string Zeilentext { get; set; } = "";
    /// <summary>Zeilen-Unterrichtsgruppe (z. B. Woche A), null = wie Unterricht.</summary>
    public int? UGruppeId { get; set; }
    /// <summary>Schülergruppe dieser Kopplungszeile (z. B. „SportK_1a1b_Rub“), leer = wie Unterricht.</summary>
    public string Schuelergruppe { get; set; } = "";

    /// <summary>Gewünschter Raum: Fachraum vor Stammraum.</summary>
    [JsonIgnore] public int? Wunschraum => FachraumId ?? StammraumId;
    [JsonIgnore] public int? Wunschraumgruppe => FachraumgruppeId ?? StammraumgruppeId;

    public Kopplungszeile Kopie() => new()
    {
        LehrerId = LehrerId, FachId = FachId, KlassenIds = new List<int>(KlassenIds), FachraumId = FachraumId,
        FachraumgruppeId = FachraumgruppeId, StammraumId = StammraumId, StammraumgruppeId = StammraumgruppeId, Wert = Wert, Zeilentext = Zeilentext, UGruppeId = UGruppeId,
        Schuelergruppe = Schuelergruppe,
    };
}

public enum Periodizitaet
{
    JedeWoche = 0,
    AWoche = 1,
    BWoche = 2,
}

public sealed class Unterricht
{
    /// <summary>Unterrichtsnummer (U-Nr).</summary>
    public int Id { get; set; }
    /// <summary>Wochenstunden (Wst).</summary>
    public int Wochenstunden { get; set; }
    public List<Kopplungszeile> Zeilen { get; set; } = new();
    /// <summary>Erlaubter Bereich an Doppelstunden (1-1 = genau eine, 0-1 = kann, 1-2 = eine oder zwei).</summary>
    public Bereich Doppelstunden { get; set; }
    /// <summary>Blockgrößen, z. B. "3" oder "3,3".</summary>
    public string Block { get; set; } = "";
    public string Schuelergruppe { get; set; } = "";
    public string Text { get; set; } = "";
    public string Statistik { get; set; } = "";
    public bool Fix { get; set; }
    public bool Ignorieren { get; set; }
    /// <summary>(M) Fach darf mehrmals am Tag stattfinden.</summary>
    public bool MehrmalsProTag { get; set; }
    /// <summary>Unterrichte desselben Bandes (Kursschiene) finden gleichzeitig statt.</summary>
    public string Band { get; set; } = "";
    /// <summary>Kursname (Kursplanung, z. B. "D-LK1").</summary>
    public string Kursname { get; set; } = "";
    public int MaxSchueler { get; set; }
    // Mehrwochenstundenplan
    public DateOnly? VonDatum { get; set; }
    public DateOnly? BisDatum { get; set; }
    public Periodizitaet Periodizitaet { get; set; }
    /// <summary>Unterrichtsgruppe (Woche A/B/…, Semester), null = jede Woche.</summary>
    public int? UGruppeId { get; set; }
    /// <summary>Keine Vertretungsplanung: fehlt die Lehrkraft, entfällt die Stunde automatisch.</summary>
    public bool KeineVertretung { get; set; }
    /// <summary>Betreuung kann übernommen werden von (automatische Betreuung bei Absenz).</summary>
    public int? BetreuungLehrerId { get; set; }
    // Wertrechnung
    public decimal? WerteFaktor { get; set; }
    public Zeitwuensche Zeitwuensche { get; set; } = new();
    public string Kommentar { get; set; } = "";

    [JsonIgnore] public IEnumerable<int> LehrerIds => Zeilen.Where(z => z.LehrerId.HasValue).Select(z => z.LehrerId!.Value).Distinct();
    [JsonIgnore] public IEnumerable<int> KlassenIds => Zeilen.SelectMany(z => z.KlassenIds).Distinct();
    [JsonIgnore] public IEnumerable<int> FachIds => Zeilen.Where(z => z.FachId.HasValue).Select(z => z.FachId!.Value).Distinct();
    [JsonIgnore] public int? HauptfachId => Zeilen.FirstOrDefault(z => z.FachId.HasValue)?.FachId;

    /// <summary>Planungsblöcke: Blöcke laut Eingabe, dann Doppelstunden (Minimum), Rest Einzelstunden.</summary>
    public List<int> Bloecke()
    {
        var result = new List<int>();
        var rest = Wochenstunden;
        foreach (var teil in (Block ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(teil, out var b) && b >= 2 && b <= rest)
            {
                result.Add(b);
                rest -= b;
            }
        }
        var doppel = Math.Min(Doppelstunden.Min, rest / 2);
        for (var i = 0; i < doppel; i++)
        {
            result.Add(2);
            rest -= 2;
        }
        for (var i = 0; i < rest; i++) result.Add(1);
        return result;
    }

    public Unterricht Kopie(int neueId) => new()
    {
        Id = neueId, Wochenstunden = Wochenstunden, Zeilen = Zeilen.Select(z => z.Kopie()).ToList(), Doppelstunden = Doppelstunden,
        Block = Block, Schuelergruppe = Schuelergruppe, Text = Text, Statistik = Statistik, Fix = Fix, Ignorieren = Ignorieren,
        MehrmalsProTag = MehrmalsProTag, KeineVertretung = KeineVertretung, Band = Band, Kursname = Kursname, MaxSchueler = MaxSchueler, VonDatum = VonDatum,
        BisDatum = BisDatum, Periodizitaet = Periodizitaet, WerteFaktor = WerteFaktor, Zeitwuensche = Zeitwuensche.Kopie(),
        UGruppeId = UGruppeId, BetreuungLehrerId = BetreuungLehrerId, Kommentar = Kommentar,
    };

    public bool GiltAm(DateOnly datum, int schulwoche)
    {
        if (VonDatum.HasValue && datum < VonDatum.Value) return false;
        if (BisDatum.HasValue && datum > BisDatum.Value) return false;
        return Periodizitaet switch
        {
            Periodizitaet.AWoche => schulwoche % 2 == 1,
            Periodizitaet.BWoche => schulwoche % 2 == 0,
            _ => true,
        };
    }
}

/// <summary>Eine verplante Unterrichtsstunde im Stundenplan.</summary>
public sealed class Planstunde
{
    public int UnterrichtId { get; set; }
    public int Tag { get; set; }
    public int Stunde { get; set; }
    /// <summary>Verplanter Raum je Kopplungszeile (Index entspricht Unterricht.Zeilen).</summary>
    public List<int?> Raeume { get; set; } = new();
    /// <summary>Fixierte Stunde (im Stundenplan mit * gekennzeichnet).</summary>
    public bool Fixiert { get; set; }

    public int? RaumFuerZeile(int i) => i < Raeume.Count ? Raeume[i] : null;

    public Planstunde Kopie() => new() { UnterrichtId = UnterrichtId, Tag = Tag, Stunde = Stunde, Raeume = new List<int?>(Raeume), Fixiert = Fixiert };
}

/// <summary>
/// Periode (Periodenstundenplan): ab einem Datum gilt ein eigener Unterricht und Stundenplan. Eine neue Periode ist
/// eine Kopie ihrer Mutterperiode. An einem Tag gilt die innerste (zuletzt beginnende) Periode, die das Datum abdeckt.
/// </summary>
public sealed class Periode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Langname { get; set; } = "";
    public DateOnly Von { get; set; }
    /// <summary>Ende (null = bis Schuljahresende bzw. bis zur nächsten Periode).</summary>
    public DateOnly? Bis { get; set; }
    public int? MutterId { get; set; }
    /// <summary>Fixierte Periode: keine Änderungen zulässig.</summary>
    public bool Fixiert { get; set; }
    public int Farbe { get; set; }
    /// <summary>Unterricht dieser Periode (null nur in älteren Dateien vor der Umstellung).</summary>
    public List<Unterricht>? Unterrichte { get; set; }
    public List<Planstunde> Plan { get; set; } = new();
    public List<Pausenaufsicht>? Pausenaufsichten { get; set; }
    /// <summary>
    /// Stand des Stundenplans beim letzten Abgleich mit der Vertretungsplanung (Moduswechsel/Neuberechnung): Stunden, die seitdem
    /// umgeplant wurden, werden daran erkannt und ihre Vertretungen auf die neue Lage übertragen (null = noch kein Stand).
    /// </summary>
    public List<Planstunde>? Vertretungsstand { get; set; }

    public bool Deckt(DateOnly datum) => datum >= Von && (Bis is null || datum <= Bis.Value);

    /// <summary>Kopie als Tochterperiode (Unterricht, Stundenplan und Pausenaufsichten werden übernommen).</summary>
    public Periode Kopie(int neueId, string name, DateOnly von, DateOnly? bis) => new()
    {
        Id = neueId, Name = name, Langname = name, Von = von, Bis = bis, MutterId = Id,
        Unterrichte = (Unterrichte ?? new()).Select(u => u.Kopie(u.Id)).ToList(),
        Plan = Plan.Select(p => p.Kopie()).ToList(),
        Pausenaufsichten = (Pausenaufsichten ?? new()).Select(a => new Pausenaufsicht { GangId = a.GangId, Tag = a.Tag, NachStunde = a.NachStunde, LehrerId = a.LehrerId, Teil = a.Teil, Datum = a.Datum, UGruppeId = a.UGruppeId }).ToList(),
    };
}
