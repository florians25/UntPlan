using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Antwort auf die Frage „Kollision! Der Raum … ist bereits in einem Unterricht verplant.“</summary>
public enum RaumKollisionsLoesung
{
    /// <summary>Raum nicht verplanen.</summary>
    NichtVerplanen,
    /// <summary>Raum mit Kollision verplanen (beide Unterrichte im selben Raum).</summary>
    MitKollision,
    /// <summary>Raum ohne Kollision verplanen: der andere Unterricht verliert den Raum (erzeugt eine offene Raumvertretung).</summary>
    OhneKollision,
    /// <summary>Räume tauschen: der andere Unterricht erhält den bisherigen Raum (zwei Raumvertretungen).</summary>
    RaeumeTauschen,
}

/// <summary>Eine Zeile der Liste „Mögliche Räume“ (Vertretungsvorschlag, Reiter Räume, und Dialog Raum zuordnen / löschen).</summary>
/// <param name="Ausweichraum">Ausw-Rm.: Ausweichraum des nicht verfügbaren (ursprünglichen) Raumes.</param>
/// <param name="AusweichStammraum">Ausw-Sr.: Ausweichraum des Stammraumes der Klasse.</param>
/// <param name="Belegung">Unterricht, der den Raum in einer der Stunden belegt.</param>
/// <param name="KapazitaetDiff">Kapazität des Raumes minus benötigte Kapazität (negativ = zu klein), null wenn unbekannt.</param>
public sealed record RaumMoeglichkeit(Raum Raum, bool Ausweichraum, bool AusweichStammraum, bool Gesperrt, IReadOnlyList<(int Stunde, TagesEintrag Eintrag)> Belegung,
    string Raumgruppen, string Gang, string Statistik, int? KapazitaetDiff)
{
    /// <summary>Spalte „Besetzt“: belegt oder gesperrt.</summary>
    public bool Besetzt => Gesperrt || Belegung.Count > 0;
}

/// <summary>Angaben für den linken Teil des Dialogs „Raum zuordnen / löschen“.</summary>
public sealed record RaumAuswahlInfo(int? UnterrichtId, string Stunden, int BenoetigteKapazitaet, int? UrspruenglicherRaum, int? Stammraum);

/// <summary>
/// Raumvertretungen: freie und besetzte Räume für eine Stunde bzw. einen Stundenblock suchen und zuordnen. Ist der Raum
/// besetzt, wird je nach Antwort eine Kollision erzeugt, der andere Unterricht verdrängt (offene Raumvertretung) oder
/// werden die Räume getauscht (zwei Raumvertretungen).
/// </summary>
public static class Raumvertretungen
{
    /// <summary>Stundenblock (Doppelstunde, Block) des Unterrichts an diesem Tag, der die Stunde enthält.</summary>
    public static List<int> Block(Schuldatei d, DateOnly datum, int stunde, int? unterrichtId, int? zeile)
    {
        if (unterrichtId is not int uid) return new List<int> { stunde };
        var tp = Tagesplan.Berechnen(d, datum);
        bool Hat(int s) => tp.Stunde(s).Any(e => e.Findetstatt && Gehoert(e, uid, zeile));
        if (!Hat(stunde)) return new List<int> { stunde };
        var von = stunde;
        while (von > 1 && Hat(von - 1)) von--;
        var bis = stunde;
        while (bis < d.Zeitraster.StundenProTag && Hat(bis + 1)) bis++;
        return Enumerable.Range(von, bis - von + 1).ToList();
    }

    internal static bool Gehoert(TagesEintrag e, int unterrichtId, int? zeile) =>
        (e.Regel?.Unterricht.Id ?? e.Vertretung?.UnterrichtId) == unterrichtId && (zeile is null || (e.Regel?.ZeilenIndex ?? e.Vertretung?.ZeilenIndex) == zeile);

    /// <summary>Benötigte Kapazität: Schülerzahl der Klassen, sonst Kapazität des ursprünglichen Raumes.</summary>
    public static int BenoetigteKapazitaet(Schuldatei d, IEnumerable<int> klassenIds, int? raumId)
    {
        var schueler = d.Klassen.Where(k => klassenIds.Contains(k.Id)).Sum(k => k.Schuelerzahl);
        if (schueler > 0) return schueler;
        return raumId is int r ? d.Raeume.FirstOrDefault(x => x.Id == r)?.Kapazitaet ?? 0 : 0;
    }

    /// <summary>Stammraum der (ersten) Klasse mit eingetragenem Klassenraum.</summary>
    public static int? Stammraum(Schuldatei d, IEnumerable<int> klassenIds) =>
        klassenIds.Select(k => d.Klassen.FirstOrDefault(x => x.Id == k)?.StammraumId).FirstOrDefault(x => x.HasValue);

    /// <summary>Ausweichraum-Ring eines Raumes (Ausweichraum, dessen Ausweichraum …).</summary>
    public static HashSet<int> Ausweichraeume(Schuldatei d, int? raumId)
    {
        var res = new HashSet<int>();
        if (raumId is not int x) return res;
        for (var i = 0; i < 50 && d.Raeume.FirstOrDefault(r => r.Id == x) is { AusweichraumId: int a } && a != raumId && res.Add(a); i++) x = a;
        return res;
    }

    /// <summary>Linker Teil des Dialogs: Unterricht, benötigte Kapazität, ursprünglicher Raum und Stammraum.</summary>
    public static RaumAuswahlInfo Info(Schuldatei d, Vertretung v)
    {
        var tag = Planung.Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        return new RaumAuswahlInfo(v.UnterrichtId, $"{tag}-{v.Stunde}", BenoetigteKapazitaet(d, v.NeueKlassenIds, v.RaumId), v.RaumId, Stammraum(d, v.NeueKlassenIds));
    }

    /// <summary>
    /// Mögliche Räume für die Stunde(n): alle nicht ignorierten Räume in Stammdatenreihenfolge. Belegte und gesperrte
    /// Räume nur mit <paramref name="auchBesetzte"/>.
    /// </summary>
    /// <param name="eigene">Gehört ein Tageseintrag zur betrachteten Stunde selbst (belegt den Raum also nicht)?</param>
    public static List<RaumMoeglichkeit> MoeglicheRaeume(Schuldatei d, DateOnly datum, IReadOnlyList<int> stunden, Func<TagesEintrag, bool> eigene,
        IReadOnlyList<int> klassenIds, int? urspruenglicherRaum, bool auchBesetzte)
    {
        var tp = Tagesplan.Berechnen(d, datum);
        var ring = Ausweichraeume(d, urspruenglicherRaum);
        var ringSr = Ausweichraeume(d, Stammraum(d, klassenIds));
        var kap = BenoetigteKapazitaet(d, klassenIds, urspruenglicherRaum);
        var gaenge = d.Gaenge.ToDictionary(g => g.Id, g => g.Kurzname);
        var res = new List<RaumMoeglichkeit>();
        foreach (var r in Stammdaten.Sortiert(d.Raeume).Where(r => !r.Ignorieren))
        {
            var gesperrt = stunden.Any(s => Absenzen.RaumGesperrt(d, r.Id, datum, s));
            var belegung = stunden.SelectMany(s => tp.Stunde(s).Where(e => e.Findetstatt && e.RaumIds.Contains(r.Id) && !eigene(e)).Select(e => (s, e))).ToList();
            if ((gesperrt || belegung.Count > 0) && !auchBesetzte) continue;
            res.Add(new RaumMoeglichkeit(r, ring.Contains(r.Id), ringSr.Contains(r.Id), gesperrt, belegung,
                string.Join(",", d.Raumgruppen.Where(g => g.RaumIds.Contains(r.Id)).Select(g => g.Kurzname)),
                r.GangId is int gid && gaenge.TryGetValue(gid, out var gn) ? gn : "", r.Statistik,
                kap > 0 && r.Kapazitaet > 0 ? r.Kapazitaet - kap : null));
        }
        return res;
    }

    /// <summary>Mögliche Räume für eine Vertretungszeile (Reiter „Räume“ des Vertretungsvorschlags).</summary>
    public static List<RaumMoeglichkeit> MoeglicheRaeume(Schuldatei d, Vertretung v, bool auchBesetzte, bool stundenblock)
    {
        var stunden = stundenblock ? Block(d, v.Datum, v.Stunde, v.UnterrichtId, v.ZeilenIndex) : new List<int> { v.Stunde };
        return MoeglicheRaeume(d, v.Datum, stunden, e => IstEigene(e, v), v.NeueKlassenIds, v.RaumId, auchBesetzte);
    }

    private static bool IstEigene(TagesEintrag e, Vertretung v) =>
        e.Vertretung == v || (e.Vertretung is null && v.UnterrichtId is int u && e.Regel != null && e.Regel.Unterricht.Id == u && e.Regel.ZeilenIndex == v.ZeilenIndex);

    /// <summary>Bester Ersatzraum: freier Ausweichraum, dann Ausweichraum des Stammraumes, sonst (wenn erlaubt) freier Raum mit ausreichender Kapazität.</summary>
    public static Raum? BesterRaum(Schuldatei d, Vertretung v, bool nurAusweichraeume, bool stundenblock = false)
    {
        var liste = MoeglicheRaeume(d, v, false, stundenblock).Where(r => r.Raum.Id != v.RaumId).ToList();
        return (liste.FirstOrDefault(r => r.Ausweichraum) ?? liste.FirstOrDefault(r => r.AusweichStammraum)
                ?? (nurAusweichraeume ? null : liste.Where(r => r.KapazitaetDiff is null or >= 0).OrderBy(r => r.KapazitaetDiff ?? int.MaxValue).FirstOrDefault()))?.Raum;
    }

    /// <summary>Zeile, die die Stunde eines Unterrichts im tagesaktuellen Plan beschreibt (wird bei Bedarf als Raumvertretung angelegt).</summary>
    public static Vertretung ZeileFuer(Schuldatei d, Tagesplan tp, TagesEintrag e, string benutzer = "")
    {
        if (e.Vertretung is { } v) return v;
        var r = e.Regel ?? throw new InvalidOperationException("Die Stunde gehört zu keinem Unterricht.");
        v = new Vertretung
        {
            Id = d.NeueId(), Datum = tp.Datum, Stunde = e.Stunde, UnterrichtId = r.Unterricht.Id, ZeilenIndex = r.ZeilenIndex, LehrerId = r.LehrerId, VertreterId = r.LehrerId,
            FachId = r.FachId, KlassenIds = r.KlassenIds.ToList(), RaumId = r.RaumId, Art = VertretungsArt.Raumvertretung, Quelle = VertretungsQuelle.Manuell,
            Schuelergruppe = r.Unterricht.Schuelergruppe, Bearbeitet = true, Benutzer = benutzer,
        };
        d.Vertretungen.Add(v);
        return v;
    }

    private static void RaumSetzen(Vertretung v, int? raumId, bool zusaetzlich, int? bisheriger, string benutzer)
    {
        if (zusaetzlich && raumId is int z)
        {
            // der bisherige Raum bleibt (wird bei einer neuen Raumvertretung ausdrücklich eingetragen)
            if (v.VertretungsraumId is null && bisheriger is int b && v.Art == VertretungsArt.Raumvertretung) v.VertretungsraumId = b;
            if (!v.ZusatzraumIds.Contains(z) && v.VertretungsraumId != z) v.ZusatzraumIds.Add(z);
        }
        else
        {
            v.VertretungsraumId = raumId;
            v.ZusatzraumIds.Remove(raumId ?? -1);
            v.RaumVerdraengt = false;
            if (raumId is null && v.Art != VertretungsArt.Raumvertretung) v.RaumVerdraengt = true;
        }
        if (v.Art == VertretungsArt.Vertretung && v.VertreterId != null && v.VertreterId == v.LehrerId) v.Art = VertretungsArt.Raumvertretung;
        v.Bearbeitet = true;
        v.Geaendert = DateTime.Now;
        if (!string.IsNullOrEmpty(benutzer)) v.Benutzer = benutzer;
    }

    /// <summary>Unterricht, der den Raum zur Stunde belegt (ohne die Stunde selbst).</summary>
    public static List<TagesEintrag> Belegung(Schuldatei d, DateOnly datum, int stunde, int raumId, Func<TagesEintrag, bool> eigene) =>
        Tagesplan.Berechnen(d, datum).Stunde(stunde).Where(e => e.Findetstatt && e.RaumIds.Contains(raumId) && !eigene(e)).ToList();

    /// <summary>
    /// Raum für die Stunde(n) eines Unterrichts zuordnen (null = Raum löschen). Liefert die geänderten bzw. neuen Vertretungszeilen.
    /// </summary>
    public static List<Vertretung> Zuordnen(Schuldatei d, int unterrichtId, int zeile, DateOnly datum, IEnumerable<int> stunden, int? raumId, bool zusaetzlich,
        RaumKollisionsLoesung loesung, string benutzer = "")
    {
        var res = new List<Vertretung>();
        if (loesung == RaumKollisionsLoesung.NichtVerplanen) return res;
        foreach (var s in stunden)
        {
            var tp = Tagesplan.Berechnen(d, datum);
            var e = tp.Stunde(s).FirstOrDefault(x => x.Findetstatt && Gehoert(x, unterrichtId, zeile));
            if (e is null) continue;
            res.AddRange(ZuordnenEintrag(d, tp, e, raumId, zusaetzlich, loesung, benutzer));
        }
        return res;
    }

    /// <summary>Raum für eine Vertretungszeile zuordnen (Vertretungsfenster, Vertretungsvorschlag), auch für Stunden ohne Unterricht (Sondereinsatz).</summary>
    public static List<Vertretung> Zuordnen(Schuldatei d, Vertretung v, int? raumId, bool zusaetzlich, RaumKollisionsLoesung loesung, string benutzer = "")
    {
        if (loesung == RaumKollisionsLoesung.NichtVerplanen) return new();
        var tp = Tagesplan.Berechnen(d, v.Datum);
        var e = tp.Stunde(v.Stunde).FirstOrDefault(x => x.Vertretung == v) ?? tp.Stunde(v.Stunde).FirstOrDefault(x => x.Findetstatt && x.Vertretung is null && v.UnterrichtId is int u && Gehoert(x, u, v.ZeilenIndex));
        if (e is null)
        {
            // Stunde findet nicht statt (z. B. offene Raumvertretung einer entfallenen Stunde): nur die Zeile ändern
            RaumSetzen(v, raumId, zusaetzlich, v.VertretungsraumId ?? v.RaumId, benutzer);
            return new() { v };
        }
        if (e.Vertretung is null) e.Vertretung = v;
        return ZuordnenEintrag(d, tp, e, raumId, zusaetzlich, loesung, benutzer);
    }

    private static List<Vertretung> ZuordnenEintrag(Schuldatei d, Tagesplan tp, TagesEintrag e, int? raumId, bool zusaetzlich, RaumKollisionsLoesung loesung, string benutzer)
    {
        var res = new List<Vertretung>();
        var bisher = e.RaumIds.Count > 0 ? e.RaumIds[0] : (int?)null;
        var hindernisse = raumId is int r && loesung is RaumKollisionsLoesung.OhneKollision or RaumKollisionsLoesung.RaeumeTauschen
            ? tp.Stunde(e.Stunde).Where(x => x != e && x.Findetstatt && x.RaumIds.Contains(r)).ToList()
            : new List<TagesEintrag>();
        var v = ZeileFuer(d, tp, e, benutzer);
        RaumSetzen(v, raumId, zusaetzlich, bisher, benutzer);
        res.Add(v);
        foreach (var h in hindernisse)
        {
            var w = h.Vertretung ?? (h.Regel != null ? ZeileFuer(d, tp, h, benutzer) : null);
            if (w is null) continue;
            if (w.ZusatzraumIds.Remove(raumId!.Value) && w.VertretungsraumId != raumId && h.RaumIds.Count > 1) { res.Add(w); continue; }
            if (loesung == RaumKollisionsLoesung.RaeumeTauschen && bisher is int alt && alt != raumId)
                RaumSetzen(w, alt, false, null, benutzer);
            else
            {
                // Raum freigeben: der andere Unterricht hat keinen Raum mehr (offen „???“)
                w.VertretungsraumId = null;
                w.RaumVerdraengt = true;
                if (w.Art == VertretungsArt.Vertretung && w.VertreterId != null && w.VertreterId == w.LehrerId) w.Art = VertretungsArt.Raumvertretung;
                w.Bearbeitet = true;
                w.Geaendert = DateTime.Now;
                if (!string.IsNullOrEmpty(benutzer)) w.Benutzer = benutzer;
            }
            res.Add(w);
        }
        return res;
    }

    /// <summary>Text der Kollisionsfrage: „Kollision! Der Raum R1b ist am 7.11. in der 5. Stunde bereits in einem Unterricht verplant.“ und „Unt. 64: 1b, Cer, Bio, R1b“.</summary>
    public static string KollisionsText(Schuldatei d, Raum raum, DateOnly datum, int stunde, IEnumerable<TagesEintrag> belegung)
    {
        var ix = new DatenIndex(d);
        var zeilen = belegung.Select(e => $"Unt. {e.Unterricht?.Id.ToString() ?? "V" + e.Vertretung?.Id}: " +
                                          string.Join(", ", new[] { ix.KKlassen(e.KlassenIds), string.Join(",", e.LehrerIds.Select(ix.KLehrer)), ix.KFach(e.FachId), string.Join(",", e.RaumIds.Select(ix.KRaum)) }.Where(x => x.Length > 0)));
        return $"Kollision! Der Raum {raum.Kurzname} ist am {datum.Day}.{datum.Month}. in der {stunde}. Stunde bereits in einem Unterricht verplant.\n\n" + string.Join("\n", zeilen.Distinct());
    }
}
