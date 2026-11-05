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
    /// <summary>Lehrerzuordnung fix: die automatische Lehrerzuordnung (Lehrertausch bei Kennzeichen (V) Variabler Lehrer) verändert den Lehrer dieser Zeile nicht.</summary>
    public bool LehrerFix { get; set; }
    /// <summary>Lehrkraft laut Unterricht vor einem Lehrertausch der Optimierung (null = nicht getauscht).</summary>
    public int? UrsprLehrerId { get; set; }

    /// <summary>Gewünschter Raum: Fachraum vor Stammraum.</summary>
    [JsonIgnore] public int? Wunschraum => FachraumId ?? StammraumId;
    [JsonIgnore] public int? Wunschraumgruppe => FachraumgruppeId ?? StammraumgruppeId;

    public Kopplungszeile Kopie() => new()
    {
        LehrerId = LehrerId, FachId = FachId, KlassenIds = new List<int>(KlassenIds), FachraumId = FachraumId,
        FachraumgruppeId = FachraumgruppeId, StammraumId = StammraumId, StammraumgruppeId = StammraumgruppeId, Wert = Wert, Zeilentext = Zeilentext, UGruppeId = UGruppeId,
        Schuelergruppe = Schuelergruppe, LehrerFix = LehrerFix, UrsprLehrerId = UrsprLehrerId,
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
    // Kennzeichen Teil 1
    /// <summary>(m) markiert.</summary>
    public bool Markiert { get; set; }
    /// <summary>(E) Doppelstunden über *-Pausen.</summary>
    public bool DoppelstundeUeberPause { get; set; }
    /// <summary>(F) Freifach.</summary>
    public bool Freifach { get; set; }
    /// <summary>(G) Nicht in Randstunde.</summary>
    public bool NichtInRandstunde { get; set; }
    /// <summary>(K) Kein Ausweichraum: nur im angegebenen Raum verplanen.</summary>
    public bool KeinAusweichraum { get; set; }
    /// <summary>(k) Keine Datenanalyse (Diagnose der Eingabedaten).</summary>
    public bool KeineDatenanalyse { get; set; }
    /// <summary>(r) Stunden im selben Raum.</summary>
    public bool SelberRaum { get; set; }
    // Kennzeichen Teil 2
    /// <summary>(B) Bedingt fixieren: im Setzlauf fix, in der Tauschoptimierung beweglich.</summary>
    public bool BedingtFixieren { get; set; }
    /// <summary>(D) Doppelstunden einhalten.</summary>
    public bool DoppelstundenEinhalten { get; set; }
    /// <summary>(C) Keine Einzelstunden.</summary>
    public bool KeineEinzelstunden { get; set; }
    /// <summary>(R) Randstunde.</summary>
    public bool Randstunde { get; set; }
    /// <summary>(S) Klassengruppe später planen.</summary>
    public bool KlassengruppeSpaeter { get; set; }
    /// <summary>(V) Variabler Lehrer: die Optimierung darf die Lehrkräfte dieses Unterrichts tauschen (Lehrer-Zuordnung bei Optimierung).</summary>
    public bool VariablerLehrer { get; set; }
    /// <summary>(L) Nicht in Legende.</summary>
    public bool NichtInLegende { get; set; }
    /// <summary>(U) Am Nachmittag nur Doppelstunden.</summary>
    public bool NachmittagNurDoppel { get; set; }
    /// <summary>(M) Manuell planen: wird von der Optimierung nicht verplant.</summary>
    public bool ManuellPlanen { get; set; }
    /// <summary>(s) Immer selbe Zeit.</summary>
    public bool ImmerSelbeZeit { get; set; }
    // Karteikarte Stundenplan
    /// <summary>Stunden im Raum: nur so viele Stunden im Fachraum verplanen (0 = alle).</summary>
    public int StundenImRaum { get; set; }
    /// <summary>Fachfolge Klassen (1–9 positiv, A–E negativ), leer = wie Fach.</summary>
    public string FachfolgeKlassen { get; set; } = "";
    /// <summary>Fachfolge Lehrer (1–9 positiv, A–E negativ), leer = wie Fach.</summary>
    public string FachfolgeLehrer { get; set; } = "";
    /// <summary>
    /// KlassenKoll. Kz. (KKK): Unterrichte derselben Klasse mit gleichem numerischem KKK (1–9) dürfen gleichzeitig liegen;
    /// bei alphabetischem KKK (A–H) dürfen nur Unterrichte mit verschiedenen Buchstaben gleichzeitig liegen.
    /// </summary>
    public string Klassenkollision { get; set; } = "";

    /// <summary>Unterrichts-Kennzeichen (ohne vererbte), z. B. „XiFr“ – Spalte „Kennzeichen“.</summary>
    [JsonIgnore] public string Kennzeichen => string.Concat(
        Fix ? "X" : "", Ignorieren ? "i" : "", Markiert ? "m" : "", DoppelstundeUeberPause ? "E" : "", Freifach ? "F" : "", NichtInRandstunde ? "G" : "",
        KeinAusweichraum ? "K" : "", KeineDatenanalyse ? "k" : "", SelberRaum ? "r" : "", BedingtFixieren ? "B" : "", DoppelstundenEinhalten ? "D" : "",
        KeineEinzelstunden ? "C" : "", Randstunde ? "R" : "", KlassengruppeSpaeter ? "S" : "", MehrmalsProTag ? "2" : "", VariablerLehrer ? "V" : "",
        NichtInLegende ? "L" : "", NachmittagNurDoppel ? "U" : "", ManuellPlanen ? "M" : "", ImmerSelbeZeit ? "s" : "", Zeitwuensche.Leer ? "" : "Z");

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
        // (C) Keine Einzelstunden: so viele Doppelstunden wie möglich
        var doppel = KeineEinzelstunden ? rest / 2 : Math.Min(Doppelstunden.Min, rest / 2);
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
        Markiert = Markiert, DoppelstundeUeberPause = DoppelstundeUeberPause, Freifach = Freifach, NichtInRandstunde = NichtInRandstunde,
        KeinAusweichraum = KeinAusweichraum, KeineDatenanalyse = KeineDatenanalyse, SelberRaum = SelberRaum, BedingtFixieren = BedingtFixieren,
        DoppelstundenEinhalten = DoppelstundenEinhalten, KeineEinzelstunden = KeineEinzelstunden, Randstunde = Randstunde,
        KlassengruppeSpaeter = KlassengruppeSpaeter, VariablerLehrer = VariablerLehrer, NichtInLegende = NichtInLegende,
        NachmittagNurDoppel = NachmittagNurDoppel, ManuellPlanen = ManuellPlanen, ImmerSelbeZeit = ImmerSelbeZeit,
        StundenImRaum = StundenImRaum, FachfolgeKlassen = FachfolgeKlassen, FachfolgeLehrer = FachfolgeLehrer, Klassenkollision = Klassenkollision,
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

/// <summary>Klassenkollisionskennzeichen (KKK, Handbuch „Anwendungshinweise | Klassenkollisionskennzeichen“).</summary>
public static class Klassenkollision
{
    /// <summary>Wirksames Kennzeichen (<see cref="Unterricht.Klassenkollision"/>): erste Ziffer 1–9 bzw. erster Buchstabe A–H (groß), sonst null.</summary>
    public static char? Kennzeichen(Unterricht u)
    {
        var t = (u.Klassenkollision ?? "").Trim();
        if (t.Length == 0) return null;
        var c = char.ToUpperInvariant(t[0]);
        return c is >= '1' and <= '9' or >= 'A' and <= 'H' ? c : null;
    }

    /// <summary>
    /// Dürfen zwei Unterrichte derselben Klasse gleichzeitig liegen? Ja bei gleichem numerischem KKK und bei verschiedenen
    /// alphabetischen KKK (Gruppen, aus denen jeder Schüler nur eine wählt).
    /// </summary>
    public static bool Erlaubt(Unterricht a, Unterricht b)
    {
        if (Kennzeichen(a) is not char x || Kennzeichen(b) is not char y) return false;
        if (char.IsDigit(x) && char.IsDigit(y)) return x == y;
        if (char.IsLetter(x) && char.IsLetter(y)) return x != y;
        return false;
    }

    /// <summary>
    /// Größte Zahl gleichzeitig liegender Unterrichte einer Klasse, die einander ausschließen (1 = keine Kollision): Unterrichte ohne KKK
    /// zählen einzeln, je numerischem KKK zählt eine Gruppe, bei alphabetischen KKK zählt die größte Gruppe eines Buchstabens.
    /// </summary>
    public static int Gleichzeitig(IEnumerable<char?> kennzeichen)
    {
        var ohne = 0;
        var ziffern = new HashSet<char>();
        var buchstaben = new Dictionary<char, int>();
        foreach (var k in kennzeichen)
        {
            if (k is not char c) ohne++;
            else if (char.IsDigit(c)) ziffern.Add(c);
            else buchstaben[c] = buchstaben.GetValueOrDefault(c) + 1;
        }
        return ohne + ziffern.Count + (buchstaben.Count > 0 ? buchstaben.Values.Max() : 0);
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

    /// <summary>
    /// Raum der Kopplungszeile. Ein einzelner Raum für einen Unterricht mit mehreren Zeilen (gemeinsamer Raum der Kopplung, z. B. aus
    /// einem Import bzw. von UntPlanWeb) gilt für alle Zeilen – alle Zeilen belegen ihn.
    /// </summary>
    public int? RaumFuerZeile(int i) => i < Raeume.Count ? Raeume[i] : Raeume.Count == 1 ? Raeume[0] : null;

    /// <summary>
    /// Raumliste auf <paramref name="zeilen"/> Einträge erweitern: ein gemeinsamer Raum (genau ein Eintrag) wird für die weiteren
    /// Zeilen übernommen, sonst bleiben weitere Zeilen ohne Raum.
    /// </summary>
    public void RaeumeErweitern(int zeilen)
    {
        var gemeinsam = Raeume.Count == 1 ? Raeume[0] : null;
        while (Raeume.Count < zeilen) Raeume.Add(gemeinsam);
    }

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
    /// <summary>Unterrichtsfolgen dieser Periode (fixe Fachfolge, Wochenfolge, Gleichzeitigkeit).</summary>
    public List<Unterrichtsfolge>? Unterrichtsfolgen { get; set; }
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
        Unterrichtsfolgen = Unterrichtsfolgen?.Select(f => new Unterrichtsfolge { Name = f.Name, Art = f.Art, UnterrichtIds = new List<int>(f.UnterrichtIds) }).ToList(),
        Pausenaufsichten = (Pausenaufsichten ?? new()).Select(a => new Pausenaufsicht { GangId = a.GangId, Tag = a.Tag, NachStunde = a.NachStunde, LehrerId = a.LehrerId, Teil = a.Teil, Datum = a.Datum, UGruppeId = a.UGruppeId }).ToList(),
    };
}

public enum UnterrichtsfolgeArt
{
    /// <summary>Fixe Fachfolge: die Unterrichte folgen in dieser Reihenfolge unmittelbar aufeinander (alle Stunden als Block).</summary>
    FixeFachfolge = 0,
    /// <summary>Variable fixe Fachfolge: unmittelbar aufeinander, Reihenfolge beliebig.</summary>
    VariableFachfolge = 1,
    /// <summary>Gleichzeitiger Unterricht: erwünscht, aber nicht zwingend gleichzeitig.</summary>
    Gleichzeitig = 2,
    /// <summary>Wochenfolge: im Wochenablauf vor den folgenden Unterrichten (max. 3 Unterrichte).</summary>
    Wochenfolge = 3,
}

/// <summary>Unterrichtsfolge (Registerkarte Planung | Unterrichtsfolgen).</summary>
public sealed class Unterrichtsfolge
{
    /// <summary>Name der Folge (z. B. „G2“).</summary>
    public string Name { get; set; } = "";
    public UnterrichtsfolgeArt Art { get; set; }
    public List<int> UnterrichtIds { get; set; } = new();

    public static string ArtText(UnterrichtsfolgeArt a) => a switch
    {
        UnterrichtsfolgeArt.FixeFachfolge => "Fixe Fachfolge",
        UnterrichtsfolgeArt.VariableFachfolge => "Variable fixe Fachfolge",
        UnterrichtsfolgeArt.Gleichzeitig => "Gleichzeitiger Unterricht",
        _ => "Wochenfolge",
    };

    /// <summary>Höchstzahl an Unterrichten je Folge (Wochenfolge: 3).</summary>
    public static int MaxUnterrichte(UnterrichtsfolgeArt a) => a == UnterrichtsfolgeArt.Wochenfolge ? 3 : 20;
}
