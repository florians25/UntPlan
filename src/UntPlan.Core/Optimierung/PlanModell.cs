using UntPlan.Core.Model;

namespace UntPlan.Core.Optimierung;

public enum VerstossArt
{
    // Hart
    Lehrerkollision,
    Klassenkollision,
    Raumkollision,
    SperrungLehrer,
    SperrungKlasse,
    SperrungRaum,
    SperrungFach,
    SperrungUnterricht,
    ZeitrasterLeer,
    NichtVerplant,
    // Klassen
    HohlstundeKlasse,
    Kernzeitverletzung,
    StdTagKlasse,
    MittagspauseKlasse,
    NachmittageKlasse,
    HauptfaecherProTag,
    HauptfaecherFolge,
    ZeitwunschKlasse,
    // Lehrer
    HohlstundenLehrer,
    StdTagLehrer,
    MittagspauseLehrer,
    NachmittageLehrer,
    UnbestimmterZeitwunsch,
    ZeitwunschLehrer,
    Tagesbelastung,
    // Unterricht / Fach
    FachMehrmalsProTag,
    DoppelstundenFehler,
    BlockFehler,
    Randstunde,
    KeinNachmittag,
    Verteilung,
    HauptfachSpaet,
    SpaeteStunde,
    ZeitwunschFach,
    ZeitwunschUnterricht,
    ZeitwunschRaum,
    // Raum
    KeinRaum,
}

public sealed record Verstoss(VerstossArt Art, ElementTyp ElementTyp, int ElementId, int? UnterrichtId, int Tag, int Stunde, double Kosten, string Text);

/// <summary>
/// Internes Rechenmodell für Optimierung und Bewertung. Unterrichtsstunden werden zu Planungsblöcken
/// (Einzel-, Doppelstunden, Blöcke, Bänder) zusammengefasst; Lehrer und Klassen sind "Entitäten" mit Belegungsraster.
/// Dieselben Kostenfunktionen liefern die Bewertungszahl, die Kennzahlen und die Diagnose.
/// </summary>
public sealed class PlanModell
{
    public const double Hart = 10000;
    public const double NichtVerplantKosten = 8000;

    internal sealed class Block
    {
        public int[] Mitglieder = Array.Empty<int>(); // Indizes in _unterrichte
        public int Laenge;
        public int[] Entitaeten = Array.Empty<int>();
        public int[] Pools = Array.Empty<int>();
        public bool Gesperrt;
        public int Tag = -1;
        public int Stunde = -1; // 0-basiert
        public Planstunde? Quelle;
        public double[] Statisch = Array.Empty<double>(); // Kosten je Startzelle
        public bool Platziert => Tag >= 0;
    }

    private readonly Schuldatei _d;
    private readonly DatenIndex _ix;
    internal readonly int T, S, C;
    internal readonly List<Block> Bloecke = new();
    internal readonly List<Unterricht> Unterrichte = new();
    private readonly Dictionary<int, int> _unterrichtIndex = new();
    internal readonly List<int>[] UnterrichtBloecke;
    // Entitäten: 0..nL-1 Lehrer, nL..nL+nK-1 Klassen
    internal readonly int NL, NK, NE;
    internal readonly Lehrer[] LehrerArr;
    internal readonly Klasse[] KlassenArr;
    internal readonly List<int>[] Raster; // [e*C + c] -> Blockindizes
    private readonly bool[] _sperr; // [e*C+c]
    private readonly int[] _wunsch; // [e*C+c]
    // Raum-Pools
    internal readonly List<int[]> PoolRaeume = new();
    private readonly List<int[]> _poolKap = new();
    private readonly List<int[]> _poolBelegt = new();
    private readonly List<double> _poolGewicht = new();
    private readonly Dictionary<string, int> _poolKeys = new();
    private readonly bool _mitPools;

    private readonly double fLHohl, fLStd, fLMitt, fLNach, fLUnb, fLTag, fKHohl, fKKern, fKStd, fKMitt, fKNach, fF2x, fFRand, fFNach,
        fHTag, fHFolge, fHSpaet, fRaum, fVert, fSpaet, fZL, fZK, fZR, fZF, fZU;

    private double fFDopp;
    private bool _nurGewuenschteFreieTage;

    /// <summary>Optionen aus den Steuerdaten zur Optimierung übernehmen.</summary>
    internal void OptionenSetzen(OptimierungsOptionen o)
    {
        if (o.DoppelstundenBesonders) fFDopp *= 2;
        _nurGewuenschteFreieTage = o.NurGewuenschteFreieTage;
    }

    public PlanModell(Schuldatei d, bool mitPools)
    {
        _d = d;
        _ix = new DatenIndex(d);
        _mitPools = mitPools;
        T = Math.Max(1, d.Zeitraster.TageProWoche);
        S = Math.Max(1, d.Zeitraster.StundenProTag);
        C = T * S;
        LehrerArr = d.Lehrer.Where(l => !l.Ignorieren).ToArray();
        KlassenArr = d.Klassen.Where(k => !k.Ignorieren).ToArray();
        NL = LehrerArr.Length;
        NK = KlassenArr.Length;
        NE = NL + NK;
        Raster = new List<int>[NE * C];
        for (var i = 0; i < Raster.Length; i++) Raster[i] = new List<int>(2);
        _sperr = new bool[NE * C];
        _wunsch = new int[NE * C];
        for (var e = 0; e < NE; e++)
        {
            var el = (Element)(e < NL ? LehrerArr[e] : KlassenArr[e - NL]);
            for (var t = 0; t < T; t++)
                for (var s = 0; s < S; s++)
                {
                    var w = el.Zeitwuensche.Get(t, s + 1);
                    _wunsch[e * C + t * S + s] = w;
                    _sperr[e * C + t * S + s] = w <= -3 || !d.Zeitraster.Verfuegbar(t, s + 1);
                }
        }
        var g = d.Gewichtung;
        double F(string k) => Gewichtung.Faktor(g.TryGetValue(k, out var v) ? v : Gewichtung.Standard[k]);
        fLHohl = F("L.Hohlstunden"); fLStd = F("L.StdTag"); fLMitt = F("L.Mittagspause"); fLNach = F("L.Nachmittage");
        fLUnb = F("L.Unbestimmt"); fLTag = F("L.Tagesbelastung"); fKHohl = F("K.Hohlstunden"); fKKern = F("K.Kernzeit");
        fKStd = F("K.StdTag"); fKMitt = F("K.Mittagspause"); fKNach = F("K.Nachmittage"); fF2x = F("F.ZweimalTag");
        fFDopp = F("F.Doppelstunden"); fFRand = F("F.Randstunde"); fFNach = F("F.KeinNachmittag"); fHTag = F("H.ProTag");
        fHFolge = F("H.Folge"); fHSpaet = F("H.Spaet"); fRaum = F("R.Raumoptimierung"); fVert = F("S.Verteilung");
        fSpaet = F("S.Spaet"); fZL = F("Z.Lehrer"); fZK = F("Z.Klassen"); fZR = F("Z.Raeume"); fZF = F("Z.Faecher"); fZU = F("Z.Unterricht");
        UnterrichtBloecke = Array.Empty<List<int>>();
        foreach (var u in d.Unterrichte.Where(IstPlanbar))
        {
            _unterrichtIndex[u.Id] = Unterrichte.Count;
            Unterrichte.Add(u);
        }
        UnterrichtBloecke = Unterrichte.Select(_ => new List<int>()).ToArray();
        _maske = Unterrichte.Select(u => Kalender.WochenMaske(d, u)).ToArray();
        _wochen = Math.Clamp(d.Einstellungen.Wochenperiodizitaet, 1, 16);
    }

    private readonly int[] _maske = Array.Empty<int>();
    private readonly int _wochen = 1;

    private int Maske(int bi) => Bloecke[bi].Mitglieder.Aggregate(0, (m, ui) => m | _maske[ui]);

    /// <summary>
    /// Anzahl gleichzeitiger Gruppen in einer Zelle unter Berücksichtigung der Wochen (A/B): Unterrichte, die nie in derselben
    /// Woche stattfinden, kollidieren nicht. Bei Klassen zählen Unterrichte desselben Bandes als eine Gruppe.
    /// </summary>
    internal int Gleichzeitig(List<int> zelle, bool klasse)
    {
        if (zelle.Count < 2) return zelle.Count;
        var max = 0;
        for (var w = 0; w < _wochen; w++)
        {
            var bit = 1 << w;
            var keys = new HashSet<string>();
            foreach (var bi in zelle)
            {
                if ((Maske(bi) & bit) == 0) continue;
                var bd = klasse ? Unterrichte[Bloecke[bi].Mitglieder[0]].Band : "";
                keys.Add(string.IsNullOrWhiteSpace(bd) ? "#" + bi : bd.Trim());
            }
            max = Math.Max(max, keys.Count);
        }
        return max;
    }

    internal bool Kollidiert(int e, int c) => Raster[e * C + c].Count > 1 && Gleichzeitig(Raster[e * C + c], e >= NL) > 1;

    public bool IstPlanbar(Unterricht u)
    {
        if (u.Ignorieren || u.Wochenstunden <= 0 || u.Zeilen.Count == 0) return false;
        if (u.Zeilen.All(z => z.LehrerId is null && z.KlassenIds.Count == 0)) return false;
        return true;
    }

    private int EntLehrer(int id)
    {
        for (var i = 0; i < NL; i++) if (LehrerArr[i].Id == id) return i;
        return -1;
    }

    private Dictionary<int, int>? _entL, _entK;

    private int[] EntitaetenFuer(IEnumerable<int> unterrichtIdx)
    {
        _entL ??= LehrerArr.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        _entK ??= KlassenArr.Select((k, i) => (k.Id, i)).ToDictionary(x => x.Id, x => NL + x.i);
        var set = new HashSet<int>();
        foreach (var ui in unterrichtIdx)
        {
            var u = Unterrichte[ui];
            foreach (var z in u.Zeilen)
            {
                if (z.LehrerId is int l && _entL.TryGetValue(l, out var el)) set.Add(el);
                foreach (var k in z.KlassenIds) if (_entK.TryGetValue(k, out var ek)) set.Add(ek);
            }
        }
        return set.ToArray();
    }

    private int[] PoolsFuer(IEnumerable<int> unterrichtIdx)
    {
        if (!_mitPools) return Array.Empty<int>();
        var list = new List<int>();
        foreach (var ui in unterrichtIdx)
        {
            foreach (var z in Unterrichte[ui].Zeilen)
            {
                var kand = _ix.RaumKandidaten(z);
                if (kand.Count == 0) continue;
                var gewicht = _ix.Raumgewicht(z);
                if (gewicht <= 0) continue;
                var sorted = kand.OrderBy(x => x).ToArray();
                var key = string.Join(",", sorted);
                if (!_poolKeys.TryGetValue(key, out var p))
                {
                    p = PoolRaeume.Count;
                    _poolKeys[key] = p;
                    PoolRaeume.Add(sorted);
                    var kap = new int[C];
                    for (var t = 0; t < T; t++)
                        for (var s = 0; s < S; s++)
                            kap[t * S + s] = sorted.Count(r => !_ix.Raeume[r].Zeitwuensche.Gesperrt(t, s + 1));
                    _poolKap.Add(kap);
                    _poolBelegt.Add(new int[C]);
                    _poolGewicht.Add(gewicht switch { 1 => 2, 2 => 6, 3 => 25, _ => Hart * 0.4 });
                }
                list.Add(p);
            }
        }
        return list.ToArray();
    }

    internal int NeuerBlock(int[] mitglieder, int laenge, bool gesperrt, Planstunde? quelle = null)
    {
        var b = new Block
        {
            Mitglieder = mitglieder,
            Laenge = laenge,
            Gesperrt = gesperrt,
            Quelle = quelle,
            Entitaeten = EntitaetenFuer(mitglieder),
            Pools = PoolsFuer(mitglieder),
        };
        b.Statisch = StatischeKosten(b);
        var idx = Bloecke.Count;
        Bloecke.Add(b);
        foreach (var m in mitglieder) UnterrichtBloecke[m].Add(idx);
        return idx;
    }

    public int UnterrichtIdx(int unterrichtId) => _unterrichtIndex.TryGetValue(unterrichtId, out var i) ? i : -1;

    private double[] StatischeKosten(Block b)
    {
        var res = new double[C];
        var zr = _d.Zeitraster;
        for (var t = 0; t < T; t++)
        {
            for (var s = 0; s < S; s++)
            {
                double k = 0;
                if (s + b.Laenge > S) { res[t * S + s] = double.PositiveInfinity; continue; }
                for (var j = 0; j < b.Laenge; j++)
                {
                    var stunde = s + j + 1;
                    var art = zr.Art(t, stunde);
                    if (art == Tageszeit.Leer) k += Hart;
                    foreach (var m in b.Mitglieder)
                    {
                        var u = Unterrichte[m];
                        var uw = u.Zeitwuensche.Get(t, stunde);
                        if (uw <= -3) k += Hart;
                        else if (uw < 0) k += -uw * 2 * fZU;
                        foreach (var fid in u.FachIds)
                        {
                            if (!_ix.Faecher.TryGetValue(fid, out var f)) continue;
                            var fw = f.Zeitwuensche.Get(t, stunde);
                            if (fw <= -3) k += Hart;
                            else if (fw < 0) k += -fw * 2 * fZF;
                            if (f.KeinNachmittag && art == Tageszeit.Nachmittag) k += 10 * fFNach;
                        }
                        if (art == Tageszeit.Nachmittag && u.KlassenIds.Any()) k += 1.5 * fSpaet;
                    }
                }
                res[t * S + s] = k;
            }
        }
        return res;
    }

    // ------------------------------------------------------------------------------------------------
    // Belegung
    // ------------------------------------------------------------------------------------------------

    internal void Setzen(int bi, int sign)
    {
        var b = Bloecke[bi];
        if (b.Tag < 0) return;
        for (var j = 0; j < b.Laenge; j++)
        {
            var c = b.Tag * S + b.Stunde + j;
            foreach (var e in b.Entitaeten)
            {
                var list = Raster[e * C + c];
                if (sign > 0) list.Add(bi);
                else list.Remove(bi);
            }
            foreach (var p in b.Pools) _poolBelegt[p][c] += sign;
        }
    }

    internal void Platzieren(int bi, int tag, int stunde)
    {
        Setzen(bi, -1);
        Bloecke[bi].Tag = tag;
        Bloecke[bi].Stunde = stunde;
        Setzen(bi, +1);
    }

    // ------------------------------------------------------------------------------------------------
    // Kosten
    // ------------------------------------------------------------------------------------------------

    internal double BlockKosten(int bi)
    {
        var b = Bloecke[bi];
        if (!b.Platziert) return NichtVerplantKosten * b.Laenge * Math.Max(1, b.Mitglieder.Length > 1 ? 1 : 1);
        return b.Statisch[b.Tag * S + b.Stunde];
    }

    internal double PoolKosten(int p, int c)
    {
        var over = _poolBelegt[p][c] - _poolKap[p][c];
        return over > 0 ? over * _poolGewicht[p] * fRaum / 8.0 : 0;
    }

    internal double PoolKostenBlock(int bi)
    {
        var b = Bloecke[bi];
        if (!b.Platziert || b.Pools.Length == 0) return 0;
        double k = 0;
        foreach (var p in b.Pools.Distinct())
            for (var j = 0; j < b.Laenge; j++)
                k += PoolKosten(p, b.Tag * S + b.Stunde + j);
        return k;
    }

    private Element ElementVon(int e) => e < NL ? LehrerArr[e] : KlassenArr[e - NL];

    /// <summary>Kosten einer Entität (Lehrer/Klasse) für die ganze Woche.</summary>
    internal double EntitaetKosten(int e, Action<Verstoss>? sink = null)
    {
        var istLehrer = e < NL;
        var el = ElementVon(e);
        var typ = istLehrer ? ElementTyp.Lehrer : ElementTyp.Klasse;
        var lehrer = istLehrer ? LehrerArr[e] : null;
        var klasse = istLehrer ? null : KlassenArr[e - NL];
        var zr = _d.Zeitraster;
        double kosten = 0;
        var hohlWoche = 0;
        int freieTage = 0, freieVormittage = 0, freieNachmittage = 0, nachmittage = 0;
        var stdJeTag = new int[T];
        var bAnteil = e * C;
        var mittagBereich = istLehrer ? lehrer!.Mittagspause : klasse!.Mittagspause;
        var stdTag = istLehrer ? lehrer!.StundenProTag : klasse!.StundenProTag;

        void V(VerstossArt art, int tag, int stunde, double k, string text, int? uid = null)
        {
            kosten += k;
            sink?.Invoke(new Verstoss(art, typ, el.Id, uid, tag, stunde, k, text));
        }

        for (var t = 0; t < T; t++)
        {
            int first = -1, last = -1, n = 0;
            bool vorm = false, nachm = false, tagHatNachmittag = false;
            int hauptTag = 0, hauptFolge = 0, hauptFolgeMax = 0;
            for (var s = 0; s < S; s++)
            {
                var c = t * S + s;
                var art = zr.Art(t, s + 1);
                if (art == Tageszeit.Nachmittag) tagHatNachmittag = true;
                var zelle = Raster[bAnteil + c];
                var w = _wunsch[bAnteil + c];
                if (zelle.Count == 0)
                {
                    if (!istLehrer && w >= 3) V(VerstossArt.Kernzeitverletzung, t, s + 1, 40 * fKKern, "Kernzeit (+3) ohne Unterricht");
                    hauptFolge = 0;
                    continue;
                }
                n++;
                if (first < 0) first = s;
                last = s;
                if (art == Tageszeit.Nachmittag) nachm = true; else vorm = true;
                if (zelle.Count > 1)
                {
                    var gruppen = Gleichzeitig(zelle, !istLehrer);
                    if (gruppen > 1)
                        V(istLehrer ? VerstossArt.Lehrerkollision : VerstossArt.Klassenkollision, t, s + 1, Hart * (gruppen - 1),
                            $"{gruppen} Unterrichte gleichzeitig", Unterrichte[Bloecke[zelle[1]].Mitglieder[0]].Id);
                }
                if (_sperr[bAnteil + c])
                    V(art == Tageszeit.Leer ? VerstossArt.ZeitrasterLeer : istLehrer ? VerstossArt.SperrungLehrer : VerstossArt.SperrungKlasse,
                        t, s + 1, Hart * zelle.Count, "Unterricht in gesperrter Stunde (-3)", Unterrichte[Bloecke[zelle[0]].Mitglieder[0]].Id);
                else if (w < 0)
                    V(istLehrer ? VerstossArt.ZeitwunschLehrer : VerstossArt.ZeitwunschKlasse, t, s + 1, -w * 3 * (istLehrer ? fZL : fZK),
                        $"Zeitwunsch {w} verletzt", Unterrichte[Bloecke[zelle[0]].Mitglieder[0]].Id);
                if (!istLehrer)
                {
                    var istHaupt = false;
                    foreach (var bi in zelle)
                    {
                        var u = Unterrichte[Bloecke[bi].Mitglieder[0]];
                        if (u.HauptfachId is int fid && _ix.Faecher.TryGetValue(fid, out var f) && f.Hauptfach) istHaupt = true;
                    }
                    if (istHaupt)
                    {
                        hauptTag++;
                        hauptFolge++;
                        hauptFolgeMax = Math.Max(hauptFolgeMax, hauptFolge);
                        if (s + 1 > zr.LetzteVormittagsstunde || s + 1 >= S) V(VerstossArt.HauptfachSpaet, t, s + 1, 2 * fHSpaet, "Hauptfach in später Stunde");
                    }
                    else hauptFolge = 0;
                }
            }
            stdJeTag[t] = n;
            if (n == 0)
            {
                // freier Lehrertag an einem nicht gewünschten Tag
                if (istLehrer && _nurGewuenschteFreieTage && zr.Verfuegbar(t, 1) && !Enumerable.Range(0, S).Any(x => _wunsch[bAnteil + t * S + x] < 0))
                    V(VerstossArt.Verteilung, t, 0, 20 * fLTag, "freier Tag nicht gewünscht");
                freieTage++;
                freieVormittage++;
                if (tagHatNachmittag) freieNachmittage++;
                continue;
            }
            if (!vorm) freieVormittage++;
            if (!nachm && tagHatNachmittag) freieNachmittage++;
            if (nachm) nachmittage++;

            // Mittagspause & Hohlstunden
            var luecken = new List<int>();
            for (var s = first + 1; s < last; s++) if (Raster[bAnteil + t * S + s].Count == 0 && !_sperrAlleinZeitraster(t, s)) luecken.Add(s);
            var mitVon = zr.MittagspauseVon - 1;
            var mitBis = zr.MittagspauseBis - 1;
            var mittagLaenge = 0;
            if (vorm && nachm && zr.MittagspauseVon > 0 && zr.MittagspauseBis >= zr.MittagspauseVon)
            {
                // längste Lücke im Mittagsfenster
                var run = 0;
                for (var s = mitVon; s <= mitBis && s < S; s++)
                {
                    if (s >= 0 && Raster[bAnteil + t * S + s].Count == 0) { run++; mittagLaenge = Math.Max(mittagLaenge, run); }
                    else run = 0;
                }
                var min = mittagBereich.IstLeer ? 1 : mittagBereich.Min;
                if (mittagLaenge < min)
                    V(istLehrer ? VerstossArt.MittagspauseLehrer : VerstossArt.MittagspauseKlasse, t, mitVon + 1, 15 * (istLehrer ? fLMitt : fKMitt),
                        "Keine ausreichende Mittagspause");
            }
            var erlaubteMittag = mittagBereich.IstLeer ? Math.Max(mittagLaenge, 0) : Math.Min(mittagLaenge, mittagBereich.Max);
            var mittagLuecken = luecken.Count(s => s >= mitVon && s <= mitBis);
            var hohl = luecken.Count - Math.Min(mittagLuecken, erlaubteMittag);
            if (hohl > 0)
            {
                if (istLehrer) hohlWoche += hohl;
                else if (fKHohl > 0)
                    foreach (var s in luecken.Take(hohl)) V(VerstossArt.HohlstundeKlasse, t, s + 1, 30 * fKHohl, "Hohlstunde");
            }
            var dev = stdTag.Abweichung(n);
            if (dev > 0)
                V(istLehrer ? VerstossArt.StdTagLehrer : VerstossArt.StdTagKlasse, t, 0, dev * 8 * (istLehrer ? fLStd : fKStd),
                    $"{n} Stunden (Vorgabe {stdTag})");
            if (!istLehrer)
            {
                if (klasse!.HauptfaecherProTag > 0 && hauptTag > klasse.HauptfaecherProTag)
                    V(VerstossArt.HauptfaecherProTag, t, 0, (hauptTag - klasse.HauptfaecherProTag) * 8 * fHTag, $"{hauptTag} Hauptfächer (max. {klasse.HauptfaecherProTag})");
                if (klasse.HauptfaecherFolge > 0 && hauptFolgeMax > klasse.HauptfaecherFolge)
                    V(VerstossArt.HauptfaecherFolge, t, 0, (hauptFolgeMax - klasse.HauptfaecherFolge) * 8 * fHFolge, $"{hauptFolgeMax} Hauptfächer hintereinander");
                // Randstunden-Fächer
                for (var s = first; s <= last; s++)
                    foreach (var bi in Raster[bAnteil + t * S + s])
                    {
                        var b = Bloecke[bi];
                        if (b.Stunde != s) continue;
                        var u = Unterrichte[b.Mitglieder[0]];
                        if (u.HauptfachId is int fid && _ix.Faecher.TryGetValue(fid, out var f) && f.Randstunde && s != first && s + b.Laenge - 1 != last)
                            V(VerstossArt.Randstunde, t, s + 1, 6 * fFRand, $"{f.Kurzname} nicht am Rand", u.Id);
                    }
            }
        }

        if (istLehrer)
        {
            var hb = lehrer!.Hohlstunden;
            if (hb.IstLeer) { if (hohlWoche > 0) V(VerstossArt.HohlstundenLehrer, -1, 0, hohlWoche * 3 * fLHohl, $"{hohlWoche} Hohlstunden"); }
            else
            {
                var d = hb.Abweichung(hohlWoche);
                if (d > 0) V(VerstossArt.HohlstundenLehrer, -1, 0, d * 12 * fLHohl, $"{hohlWoche} Hohlstunden (erlaubt {hb})");
            }
            if (lehrer.MaxNachmittage > 0 && nachmittage > lehrer.MaxNachmittage)
                V(VerstossArt.NachmittageLehrer, -1, 0, (nachmittage - lehrer.MaxNachmittage) * 15 * fLNach, $"{nachmittage} Nachmittage (max. {lehrer.MaxNachmittage})");
            if (fLTag > 0)
            {
                var aktiv = stdJeTag.Where(x => x > 0).ToArray();
                if (aktiv.Length > 1)
                {
                    var spread = aktiv.Max() - aktiv.Min();
                    if (spread > 3) V(VerstossArt.Tagesbelastung, -1, 0, (spread - 3) * 1.5 * fLTag, "Ungleichmäßige Tagesbelastung");
                }
            }
        }
        else if (klasse!.MaxNachmittage > 0 && nachmittage > klasse.MaxNachmittage)
            V(VerstossArt.NachmittageKlasse, -1, 0, (nachmittage - klasse.MaxNachmittage) * 15 * fKNach, $"{nachmittage} Nachmittage (max. {klasse.MaxNachmittage})");

        foreach (var uz in el.UnbestimmteZeitwuensche)
        {
            var frei = uz.Bereich switch { ZeitbereichArt.Tage => freieTage, ZeitbereichArt.Vormittage => freieVormittage, _ => freieNachmittage };
            // Unbestimmte Zeitwünsche sind additiv zu bestimmten Sperrungen
            var gesperrtGanz = uz.Bereich == ZeitbereichArt.Tage ? Enumerable.Range(0, T).Count(t => Enumerable.Range(0, S).All(s => _wunsch[bAnteil + t * S + s] <= -3)) : 0;
            var fehlt = uz.Anzahl + gesperrtGanz - frei;
            if (fehlt > 0)
                V(VerstossArt.UnbestimmterZeitwunsch, -1, 0, fehlt * (uz.Wunsch <= -3 ? 120 : uz.Wunsch == -2 ? 40 : 12) * (istLehrer ? fLUnb : fKNach),
                    $"{uz.Anzahl} freie(r) {uz.Bereich} gewünscht, {frei} erreicht");
        }
        return kosten;
    }

    /// <summary>Anzahl unterschiedlicher Gruppen in einer Zelle (Unterrichte desselben Bandes zählen als eine Gruppe).</summary>
    private bool _sperrAlleinZeitraster(int t, int s) => !_d.Zeitraster.Verfuegbar(t, s + 1);

    /// <summary>Kosten eines Unterrichts: Fach mehrmals pro Tag, Doppelstunden, Blöcke, Verteilung.</summary>
    internal double UnterrichtKosten(int ui, Action<Verstoss>? sink = null)
    {
        var u = Unterrichte[ui];
        double kosten = 0;
        void V(VerstossArt art, int tag, int stunde, double k, string text)
        {
            kosten += k;
            sink?.Invoke(new Verstoss(art, ElementTyp.Unterricht, u.Id, u.Id, tag, stunde, k, text));
        }
        var proTag = new List<(int Stunde, int Laenge)>[T];
        for (var t = 0; t < T; t++) proTag[t] = new List<(int, int)>();
        var platziert = 0;
        foreach (var bi in UnterrichtBloecke[ui])
        {
            var b = Bloecke[bi];
            if (!b.Platziert) continue;
            proTag[b.Tag].Add((b.Stunde, b.Laenge));
            platziert += b.Laenge;
        }
        var doppel = 0;
        var maxJeTag = (int)Math.Ceiling(u.Wochenstunden / (double)T);
        var bloeckeGewuenscht = u.Bloecke().Where(x => x >= 3).ToList();
        var mehrmals = u.MehrmalsProTag || u.FachIds.Any(f => _ix.Faecher.TryGetValue(f, out var fa) && fa.MehrmalsProTag);
        for (var t = 0; t < T; t++)
        {
            var l = proTag[t];
            if (l.Count == 0) continue;
            l.Sort((a, b) => a.Stunde.CompareTo(b.Stunde));
            var runs = new List<(int Start, int Len, bool AusBlock)>();
            foreach (var (st, len) in l)
            {
                if (runs.Count > 0 && runs[^1].Start + runs[^1].Len == st)
                    runs[^1] = (runs[^1].Start, runs[^1].Len + len, false);
                else runs.Add((st, len, len >= 3));
            }
            var stunden = l.Sum(x => x.Laenge);
            if (runs.Count > 1 && !mehrmals)
                V(VerstossArt.FachMehrmalsProTag, t, runs[1].Start + 1, (runs.Count - 1) * 25 * fF2x, "Fach mehrmals am selben Tag");
            foreach (var r in runs)
            {
                if (r.Len == 2) doppel++;
                else if (r.Len >= 3 && !(r.AusBlock || bloeckeGewuenscht.Contains(r.Len)))
                    V(VerstossArt.BlockFehler, t, r.Start + 1, (r.Len - 2) * 15 * fFDopp, $"{r.Len} Stunden hintereinander");
            }
            if (stunden > Math.Max(maxJeTag, Math.Max(2, bloeckeGewuenscht.DefaultIfEmpty(0).Max())))
                V(VerstossArt.Verteilung, t, 0, (stunden - maxJeTag) * 6 * fVert, $"{stunden} Stunden an einem Tag");
        }
        if (!u.Doppelstunden.IstLeer)
        {
            // Nur bewerten, wenn alle Stunden verplant sind (sonst ist ein Fehler noch nicht feststellbar)
            if (platziert == u.Wochenstunden || doppel > u.Doppelstunden.Max)
            {
                var d = u.Doppelstunden.Abweichung(doppel);
                if (d > 0) V(VerstossArt.DoppelstundenFehler, -1, 0, d * 25 * fFDopp, $"{doppel} Doppelstunden (Vorgabe {u.Doppelstunden})");
            }
        }
        else if (doppel > 0)
            V(VerstossArt.DoppelstundenFehler, -1, 0, doppel * 10 * fFDopp, "Doppelstunde, obwohl Einzelstunden gewünscht");
        return kosten;
    }

    public double Gesamtkosten(Action<Verstoss>? sink = null)
    {
        double k = 0;
        for (var e = 0; e < NE; e++) k += EntitaetKosten(e, sink);
        for (var ui = 0; ui < Unterrichte.Count; ui++) k += UnterrichtKosten(ui, sink);
        for (var bi = 0; bi < Bloecke.Count; bi++)
        {
            var bk = BlockKosten(bi);
            k += bk;
            if (sink != null && bk > 0)
            {
                var b = Bloecke[bi];
                var u = Unterrichte[b.Mitglieder[0]];
                if (!b.Platziert) sink(new Verstoss(VerstossArt.NichtVerplant, ElementTyp.Unterricht, u.Id, u.Id, -1, 0, bk, $"{b.Laenge} Std. nicht verplant"));
                else sink(new Verstoss(bk >= Hart ? VerstossArt.SperrungUnterricht : VerstossArt.ZeitwunschUnterricht, ElementTyp.Unterricht, u.Id, u.Id, b.Tag, b.Stunde + 1, bk,
                    bk >= Hart ? "Gesperrte Stunde (Fach/Unterricht/Zeitraster)" : "Zeitwunsch Fach/Unterricht bzw. Nachmittag"));
            }
        }
        for (var p = 0; p < PoolRaeume.Count; p++)
            for (var c = 0; c < C; c++) k += PoolKosten(p, c);
        return k;
    }
}
