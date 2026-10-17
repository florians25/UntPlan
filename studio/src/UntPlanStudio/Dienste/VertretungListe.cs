using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Api;

namespace UntPlanStudio.Dienste;

/// <summary>Anfrage der Vertretungsliste (Filter wie im Vertretungsfenster des Desktops).</summary>
public sealed class ListeAnfrage
{
    public DateOnly Von { get; set; }
    public DateOnly? Bis { get; set; }
    /// <summary>0 Klasse – Standard, 1 Lehrer – Standard, 2 Druck – Klasse, 3 Druck – Lehrer, 4 Pausenaufsichten.</summary>
    public int? Ansicht { get; set; }
    public bool? Offen { get; set; }
    public bool? Kollisionen { get; set; }
    /// <summary>Element-Auswahl: Klasse (Klassenansichten) bzw. Lehrkraft (Lehreransichten).</summary>
    public int? Element { get; set; }
    /// <summary>„Veranstaltungen zusammenfassen“ (Entfälle/Freisetzungen einer Veranstaltung je Tag in einer Zeile).</summary>
    public bool? Sammeln { get; set; }
    /// <summary>Gerade bearbeitete Zeilen (Vtr-Nr., durch Komma getrennt) bleiben sichtbar, auch wenn sie nicht mehr zum Filter passen.</summary>
    public string? Behalten { get; set; }
    /// <summary>Spalten mit Anzeige als Langname (durch Komma getrennt).</summary>
    public string? Lang { get; set; }
    /// <summary>Ausgeblendete Vertretungsarten (Enum-Namen, durch Komma getrennt).</summary>
    public string? ArtenAus { get; set; }
    /// <summary>Regulär stattfindenden Unterricht zeigen (Art „Unterricht“).</summary>
    public bool? Unterricht { get; set; }
}

/// <summary>Eine Spalte der Vertretungsliste („Felder der Ansicht“).</summary>
public sealed record VertretungsSpalte(string Schluessel, string Titel, string Feld, int Breite, bool NurLesen, bool Standard, bool Lang);

/// <summary>
/// Vertretungsliste wie im Vertretungsfenster des Desktops (<c>VertretungenFenster</c>): Spalten, Filter (offene, Kollisionen, Element,
/// Druckansichten), Veranstaltungen je Tag zusammengefasst, Tausch/Verlegung in einer Zeile, Unterrichtszeilen, Kollisionen.
/// Die Zeilen tragen die Anzeige-Texte je Spalte und Kennzeichen für die Darstellung.
/// </summary>
public sealed class VertretungListe
{
    public static readonly string[] Ansichten = { "Klasse - Standard", "Lehrer - Standard", "Druck - Klasse", "Druck - Lehrer", "Pausenaufsichten" };

    public static readonly VertretungsSpalte[] Spalten =
    {
        new("Nr", "Vtr-Nr.", "Vertretungsnummer", 54, true, true, false), new("Art", "Art", "Art der Vertretung", 90, true, true, false),
        new("Datum", "Datum", "Datum (TT.MM.)", 50, true, true, false), new("Tag", "Tag", "Tag", 34, true, false, false),
        new("Std", "Stunde", "Std.", 48, true, true, false), new("Zeit", "Zeit", "Beginn- und Endzeit", 84, true, false, false),
        new("Fach", "(Fach)", "statt Fach", 54, true, true, true), new("NFach", "Fach", "Fach", 54, false, true, true),
        new("Lehrer", "(Lehrer)", "statt Lehrer", 60, true, true, true), new("Vertreter", "Vertreter", "Vertreter", 64, false, true, true),
        new("AKlassen", "(Klasse(n))", "statt Klasse(n)", 70, true, true, true), new("Klassen", "Klasse(n)", "Klasse(n)", 70, true, true, true),
        new("Raum", "(Raum)", "statt Raum", 54, true, true, true), new("NRaum", "Raum", "Raum", 58, false, true, true),
        new("Von", "Vertr. von", "Verlegung", 76, true, true, false), new("Nach", "(Le.) nach", "Wegverlegung", 76, true, true, false),
        new("Text", "Text", "Vertretungs-Text", 220, false, true, false), new("Kz", "Statistik", "Statistik-Kennzeichen", 60, false, true, false),
        new("SG", "Schülergruppe", "Schülergruppe", 110, false, true, false), new("Grund", "Absenzgrund", "Absenzgrund", 86, true, false, true),
        new("UText", "Unt.-Text", "Unterrichts-Text", 90, true, false, false), new("UZText", "Unt.-Zeilentext", "Unterrichts-Zeilentext", 100, true, false, false),
        new("Beschr", "Beschr.", "Beschr.", 76, false, false, false), new("Entf", "Entfall", "Entfall", 50, true, false, false),
        new("Mitb", "Mitbetr.", "Mitbetreuung", 56, true, false, false), new("Kopp", "Vtr-Kopplung", "Vertretungs-Kopplung", 60, false, false, false),
        new("Neu", "Neu", "Neue Vertretung", 40, true, false, false), new("Fix", "(X)", "(X) Fixiert", 36, true, false, false),
        new("ND", "(N)", "(N) Nicht Drucken", 36, true, false, false), new("Text2", "Text 2", "Vertretungs-Text 2", 120, false, false, false),
        new("Ben", "Benachr.", "Benachrichtigen", 60, true, false, false), new("Zaehler", "Zähler", "Zähler", 52, true, false, false),
        new("Unterschrift", "Unterschrift", "Unterschrift", 90, true, false, false), new("Wert", "Wert", "Wert bzw. Faktor", 52, false, false, false),
        new("Abt", "Abteilung", "Abteilung", 76, true, false, true), new("Benutzer", "Benutzer", "Benutzer", 76, true, false, false),
        new("Mehr", "Mehrarb.", "Mehrarbeit", 60, true, false, false),
    };

    /// <summary>Ab so vielen Entfällen/Freisetzungen einer Veranstaltung an einem Tag werden sie zusammengefasst.</summary>
    private const int VaSammelAb = 4;

    private readonly Dokument _doc;
    private readonly Schuldatei _d;
    private readonly DatenIndex _ix;
    private readonly ListeAnfrage _a;
    private readonly DateOnly _bis;
    private readonly int _ansicht;
    private readonly bool _offen, _kollisionen, _sammeln, _unterricht;
    private readonly HashSet<int> _behalten;
    private readonly HashSet<string> _lang;
    private readonly HashSet<string> _artenAus;
    private readonly Dictionary<DateOnly, Tagesplan> _plaene = new();
    private readonly Dictionary<int, List<Vertretung>> _gruppen = new();
    private readonly HashSet<int> _vaSammel = new();
    private readonly Dictionary<int, Vertretung> _weg = new();
    private readonly Dictionary<Vertretung, (string? Text, bool Lehrer, bool Raum)> _koll = new(ReferenceEqualityComparer.Instance);

    public VertretungListe(Dokument doc, ListeAnfrage a)
    {
        _doc = doc;
        _d = doc.Daten;
        _ix = doc.Index;
        _a = a;
        _bis = a.Bis is DateOnly b && b >= a.Von ? b : a.Von;
        _ansicht = Math.Clamp(a.Ansicht ?? 0, 0, Ansichten.Length - 1);
        _offen = a.Offen == true;
        _kollisionen = a.Kollisionen == true;
        _sammeln = a.Sammeln != false;
        _unterricht = a.Unterricht == true;
        _behalten = Liste(a.Behalten).Select(s => int.TryParse(s, out var i) ? i : 0).Where(i => i > 0).ToHashSet();
        _lang = Liste(a.Lang).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _artenAus = Liste(a.ArtenAus).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Liste(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private bool LehrerSicht => _ansicht is 1 or 3;

    public Tagesplan Plan(DateOnly datum)
    {
        if (!_plaene.TryGetValue(datum, out var tp)) _plaene[datum] = tp = Tagesplan.Berechnen(_d, datum);
        return tp;
    }

    // ---------------------------------------------------------------------------------------------
    // Kollisionen

    private (string? Text, bool Lehrer, bool Raum) KollisionsInfo(Vertretung v)
    {
        if (_koll.TryGetValue(v, out var k)) return k;
        string? text = null;
        if (v.Id != 0)
            text = _gruppen.TryGetValue(v.Id, out var g) && g.Count > 1 ? g.Select(KollisionEinzeln).FirstOrDefault(x => x != null) : KollisionEinzeln(v);
        var (l, r) = (false, false);
        if (text != null)
        {
            var liste = _gruppen.TryGetValue(v.Id, out var g2) ? g2 : new List<Vertretung> { v };
            l = liste.Any(x => !x.IstEntfall && x.VertreterId is int le && x.Art != VertretungsArt.TrotzAbsenz && !x.Mitbetreuung && Vertretungen.LehrerBelegt(_d, Plan(x.Datum), le, x.Stunde, x) != null);
            r = liste.Any(x => !x.IstEntfall && x.Art != VertretungsArt.Pausenaufsicht && (x.VertretungsraumId ?? x.RaumId) is int rm && Plan(x.Datum).Stunde(x.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(rm)) > 1);
        }
        return _koll[v] = (text, l, r);
    }

    public string? KollisionEinzeln(Vertretung v)
    {
        if (v.IstEntfall) return null;
        if (v.VertreterId is int l && v.Art != VertretungsArt.TrotzAbsenz && !v.Mitbetreuung && Vertretungen.LehrerBelegt(_d, Plan(v.Datum), l, v.Stunde, v) is { } g) return g;
        var raum = v.VertretungsraumId ?? v.RaumId;
        if (raum is int r && v.Art != VertretungsArt.Pausenaufsicht && Plan(v.Datum).Stunde(v.Stunde).Count(e => e.Findetstatt && e.RaumIds.Contains(r)) > 1)
            return $"Raum {_ix.KRaum(r)} doppelt belegt";
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Filter und Zeilen

    private bool ArtSichtbar(VertretungsArt a) => a == VertretungsArt.Unterricht ? _unterricht : !_artenAus.Contains(a.ToString());

    private bool Passt(Vertretung v)
    {
        if (_ansicht == 4 && v.Art != VertretungsArt.Pausenaufsicht) return false;
        var behalten = _behalten.Contains(v.Id);
        if (_offen && !v.OffenOderRaumOffen && !behalten) return false;
        if (_kollisionen && KollisionEinzeln(v) is null && !behalten) return false;
        if (v.Art == VertretungsArt.Veranstaltung) return false;
        if (!ArtSichtbar(v.Art)) return false;
        if (Vertretungen.NichtInListe(_d, v) && !behalten) return false;
        if (_a.Element is int id && !behalten)
        {
            var treffer = LehrerSicht ? v.LehrerId == id || v.VertreterId == id : v.NeueKlassenIds.Contains(id) || v.KlassenIds.Contains(id);
            if (!treffer) return false;
        }
        return true;
    }

    private bool Veranstaltungsfolge(Vertretung v) =>
        v.VeranstaltungId.HasValue && v.Quelle == VertretungsQuelle.Veranstaltung && v.Art is VertretungsArt.Entfall or VertretungsArt.Freisetzung
        && !v.Fixiert && v.NachDatum is null && !_behalten.Contains(v.Id);

    private List<Vertretung> Zusammenfassen(List<Vertretung> liste)
    {
        var res = new List<Vertretung>();
        foreach (var g in liste.GroupBy(v => v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int va ? $"VA{va}/{v.Datum}"
                     : _sammeln && Veranstaltungsfolge(v) ? $"VE{v.VeranstaltungId}/{v.Datum}/{v.Art}/{v.Text}" : $"#{v.Id}"))
        {
            var zeilen = g.OrderBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
            if (g.Key.StartsWith("VE", StringComparison.Ordinal) && zeilen.Count < VaSammelAb)
            {
                res.AddRange(zeilen);
                continue;
            }
            res.Add(zeilen[0]);
            if (zeilen.Count > 1 || zeilen[0].Art == VertretungsArt.Veranstaltung) _gruppen[zeilen[0].Id] = zeilen;
            if (g.Key.StartsWith("VE", StringComparison.Ordinal)) _vaSammel.Add(zeilen[0].Id);
        }
        return res;
    }

    private static bool IstVerlegung(Vertretung v) => v.Art is VertretungsArt.Tausch or VertretungsArt.Verlegung or VertretungsArt.Verlegt;

    private List<Vertretung> TauschZusammenfassen(List<Vertretung> liste)
    {
        var weg = liste.Where(q => q.Art == VertretungsArt.Entfall && q.NachDatum.HasValue && q.VerlegungsId.HasValue && !_behalten.Contains(q.Id))
            .GroupBy(q => (q.Datum, q.Stunde)).ToDictionary(g => g.Key, g => g.ToList());
        if (weg.Count == 0) return liste;
        var versteckt = new HashSet<int>();
        foreach (var z in liste.Where(z => IstVerlegung(z) && z.VonDatum.HasValue && z.VerlegungsId.HasValue))
        {
            if (!weg.TryGetValue((z.Datum, z.Stunde), out var kandidaten)) continue;
            var q = kandidaten.FirstOrDefault(q => !versteckt.Contains(q.Id) && q.VerlegungsId != z.VerlegungsId && q.KlassenIds.ToHashSet().SetEquals(z.NeueKlassenIds));
            if (q is null) continue;
            versteckt.Add(q.Id);
            _weg[z.Id] = q;
        }
        return liste.Where(v => !versteckt.Contains(v.Id)).ToList();
    }

    private IEnumerable<Vertretung> Unterrichtszeilen()
    {
        foreach (var t in Kalender.Tage(_a.Von, _bis).Where(t => Kalender.IstUnterrichtstag(_d, t)))
            foreach (var e in Plan(t).Eintraege.Where(e => e.Status == TagesStatus.Regulaer && e.Regel != null))
            {
                var v = new Vertretung
                {
                    Id = 0, Datum = t, Stunde = e.Stunde, Art = VertretungsArt.Unterricht, UnterrichtId = e.Regel!.Unterricht.Id, ZeilenIndex = e.Regel.ZeilenIndex,
                    LehrerId = e.Regel.LehrerId, VertreterId = e.Regel.LehrerId, FachId = e.FachId, KlassenIds = e.KlassenIds.ToList(), RaumId = e.Regel.RaumId,
                    Schuelergruppe = VeranstaltungsLogik.Schuelergruppe(e.Regel),
                };
                if (Passt(v)) yield return v;
            }
    }

    private static string Schluessel(Vertretung v) => v.Id != 0 ? "V" + v.Id : $"U{v.UnterrichtId}/{v.ZeilenIndex}/{v.Datum:yyyyMMdd}/{v.Stunde}";

    /// <summary>Liste berechnen (Standardsortierung Datum, Klasse bzw. Lehrkraft, Stunde – sortiert wird in der Oberfläche).</summary>
    public object Berechnen()
    {
        IEnumerable<Vertretung> liste = TauschZusammenfassen(Zusammenfassen(Vertretungen.ImZeitraum(_d, _a.Von, _bis).Where(Passt).ToList()));
        if (_ansicht is 2 or 3) liste = liste.Where(v => !v.NichtDrucken || _behalten.Contains(v.Id));
        if (_unterricht && _ansicht != 4 && !_offen && !_kollisionen) liste = liste.Concat(Unterrichtszeilen());
        var vergleich = StringComparer.Create(Format.De, true);
        var sortiert = (LehrerSicht
            ? liste.OrderBy(v => v.Datum).ThenBy(v => _ix.KLehrer(v.VertreterId ?? v.LehrerId), vergleich).ThenBy(v => v.Stunde)
            : liste.OrderBy(v => v.Datum).ThenBy(v => _ix.KKlassen(v.NeueKlassenIds), vergleich).ThenBy(v => v.Stunde)).ThenBy(v => v.Id).ToList();
        return new
        {
            von = _a.Von, bis = _bis,
            ansicht = Ansichten[Math.Clamp(_ansicht, 0, Ansichten.Length - 1)],
            spalten = Spalten.Select(s => new { schluessel = s.Schluessel, titel = s.Titel, feld = s.Feld, breite = s.Breite, nurLesen = s.NurLesen, standard = s.Standard, lang = s.Lang }),
            zeilen = sortiert.Select(Zeile).ToList(),
            offen = sortiert.Count(v => v.OffenOderRaumOffen && v.Id != 0),
            gesamt = Vertretungen.ImZeitraum(_d, _a.Von, _bis).Count(v => v.Art != VertretungsArt.Veranstaltung && !Vertretungen.NichtInListe(_d, v)),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Anzeige-Texte

    private string Anzeige(ElementTyp typ, int? id, string spalte)
    {
        if (id is not int i) return "";
        if (!_lang.Contains(spalte)) return _ix.K(typ, i);
        var e = _ix.Element(typ, i);
        return e is Lehrer l ? l.VollerName : e is null ? "?" : string.IsNullOrEmpty(e.Langname) ? e.Kurzname : e.Langname;
    }

    private string Klassen(IEnumerable<int> ids, string spalte) => string.Join(", ", ids.Select(k => Anzeige(ElementTyp.Klasse, k, spalte)));

    private Dictionary<string, object?> Zeile(Vertretung v)
    {
        var w = Werte(v);
        var gruppe = _gruppen.TryGetValue(v.Id, out var g) ? g : null;
        var (koll, kl, kr) = KollisionsInfo(v);
        var vaOffen = v.Art == VertretungsArt.Veranstaltung && gruppe != null && gruppe.Any(x => x.VeranstaltungOffen);
        w["id"] = Schluessel(v);
        w["m"] = new
        {
            v = v.Id,
            art = v.Art.ToString(),
            farbe = _d.Vertretungseinstellungen.Farben.TryGetValue(v.Art.ToString(), out var f) ? StammdatenApi.Farbe(f) : null,
            offen = v.Offen || vaOffen,
            raumOffen = v.RaumOffen,
            koll, kollL = kl, kollR = kr,
            fix = v.Fixiert,
            nd = v.NichtDrucken,
            entfall = v.IstEntfall,
            pause = v.Art == VertretungsArt.Pausenaufsicht,
            verlegung = IstVerlegung(v),
            unterricht = v.Id == 0,
            sammel = _vaSammel.Contains(v.Id),
            gruppe = gruppe?.Select(x => x.Id).ToList(),
            va = v.VeranstaltungId,
            veranstaltung = v.Art == VertretungsArt.Veranstaltung,
            freisetzung = v.Art == VertretungsArt.Freisetzung,
            abgeleitet = Vertretungen.IstAbgeleitet(v),
            datum = v.Datum,
            stunde = v.Stunde,
            u = v.UnterrichtId,
            zeile = v.ZeilenIndex,
            lehrer = v.LehrerId,
            vertreter = v.VertreterId,
            klasse = v.NeueKlassenIds.Cast<int?>().FirstOrDefault(),
            raum = v.VertretungsraumId ?? v.RaumId,
        };
        return w;
    }

    private Dictionary<string, object?> Werte(Vertretung v)
    {
        var tag = Planungsdienst.TageKurz[((int)v.Datum.DayOfWeek + 6) % 7];
        var gruppe = _gruppen.TryGetValue(v.Id, out var gr) ? gr : null;
        var va = v.Art == VertretungsArt.Veranstaltung && v.VeranstaltungId is int vaId ? _d.Veranstaltungen.FirstOrDefault(x => x.Id == vaId) : null;
        var vertreter = va != null ? VeranstaltungsLogik.LehrerText(_d, va, v.Datum)
            : v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten ? "+++"
            : v.Art == VertretungsArt.Freisetzung ? Anzeige(ElementTyp.Lehrer, v.LehrerId, "Vertreter")
            : v.IstEntfall || v.Art == VertretungsArt.Vormerkung ? "---" : v.VertreterId is int l ? Anzeige(ElementTyp.Lehrer, l, "Vertreter") : v.Offen ? "???" : "";
        var raumNeu = v.IstEntfall ? "---" : v.RaumOffen ? "???" : v.Art == VertretungsArt.Pausenaufsicht ? _ix.K(ElementTyp.Gang, v.GangId)
            : string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).Where(x => x.HasValue).Select(x => Anzeige(ElementTyp.Raum, x, "NRaum")));
        static string Pos(DateOnly? d, int? s) => d is DateOnly dd ? $"{dd.Day}.{dd.Month}. / {s}" : "";
        var stunde = gruppe is { Count: > 1 } ? $"{gruppe.Min(x => x.Stunde)}-{gruppe.Max(x => x.Stunde)}" : Vertretungsplan.StundeText(v);
        var u = v.UnterrichtId is int uid ? Plan(v.Datum).Unterrichte.GetValueOrDefault(uid) : null;
        var lehrer = va != null ? VeranstaltungsLogik.LehrerText(_d, va, v.Datum, ", ")
            : (u is { Zeilen.Count: > 1 } && v.LehrerId.HasValue ? "*" : "") + Anzeige(ElementTyp.Lehrer, v.LehrerId, "Lehrer");
        var neueKlassen = Klassen(v.NeueKlassenIds, "Klassen");
        var weg = _weg.GetValueOrDefault(v.Id);
        if (weg != null) lehrer = (Plan(weg.Datum).Unterrichte.GetValueOrDefault(weg.UnterrichtId ?? -1) is { Zeilen.Count: > 1 } && weg.LehrerId.HasValue ? "*" : "") + Anzeige(ElementTyp.Lehrer, weg.LehrerId, "Lehrer");
        var neuesFach = v.IstEntfall ? "---" : Anzeige(ElementTyp.Fach, v.VertretungsfachId ?? (IstVerlegung(v) ? v.FachId : null), "NFach");
        if (v.Art == VertretungsArt.Freisetzung && neueKlassen.Length > 0) neueKlassen = $"({neueKlassen})";
        var z = _d.Zeitraster;
        var zeit = z.Zeit(gruppe?.Min(x => x.Stunde) ?? v.Stunde) is { } z1 && z.Zeit(gruppe?.Max(x => x.Stunde) ?? v.Stunde) is { } z2 ? $"{z1.Beginn:HH\\:mm}-{z2.Ende:HH\\:mm}" : "";
        var zaehler = v.IstEntfall && v.Art != VertretungsArt.BereitschaftEntfaellt ? "-1" : v.VertreterId.HasValue && v.VertreterId != v.LehrerId && Vertretungen.ZaehlendeArten.Contains(v.Art) ? "+1" : "";
        var abteilung = v.NeueKlassenIds.Select(k => _ix.Klassen.GetValueOrDefault(k)?.AbteilungId).FirstOrDefault(x => x.HasValue);
        var benachrichtigen = v.VertreterId is int vt && v.Datum == DateOnly.FromDateTime(DateTime.Today) && !Plan(v.Datum).VonLehrer(vt).Any(e => e.Stunde < v.Stunde && e.Vertretung != v) ? "X" : "";
        var w = new Dictionary<string, object?>
        {
            ["Nr"] = v.Id == 0 ? "" : v.Id.ToString(),
            ["Art"] = Vertretungsplan.ArtText(_d, v.Art),
            ["Datum"] = $"{v.Datum:dd.MM.}",
            ["Tag"] = tag,
            ["Std"] = stunde,
            ["Zeit"] = zeit,
            ["Fach"] = Anzeige(ElementTyp.Fach, (weg ?? v).FachId, "Fach"),
            ["NFach"] = neuesFach,
            ["Lehrer"] = lehrer,
            ["Vertreter"] = vertreter,
            ["AKlassen"] = Klassen((weg ?? v).KlassenIds, "AKlassen"),
            ["Klassen"] = neueKlassen,
            ["Raum"] = v.Art == VertretungsArt.Pausenaufsicht ? _ix.K(ElementTyp.Gang, v.GangId) : Anzeige(ElementTyp.Raum, (weg ?? v).RaumId, "Raum"),
            ["NRaum"] = raumNeu,
            ["Von"] = Pos(v.VonDatum, v.VonStunde),
            ["Nach"] = weg != null ? Pos(weg.NachDatum, weg.NachStunde) : Pos(v.NachDatum, v.NachStunde),
            ["Text"] = v.Art == VertretungsArt.VertretungOhneLehrer && string.IsNullOrWhiteSpace(v.Text) ? Vertretungen.OhneLehrerText : v.Text,
            ["Kz"] = v.Statistik,
            ["SG"] = v.Schuelergruppe,
            ["Grund"] = _lang.Contains("Grund") && _d.Abwesenheitsgruende.FirstOrDefault(x => x.Id == v.GrundId) is { } gl ? gl.Langname : v.Grund,
            ["UText"] = u?.Text ?? "",
            ["UZText"] = u != null && v.ZeilenIndex < u.Zeilen.Count ? u.Zeilen[v.ZeilenIndex].Zeilentext : "",
            ["Beschr"] = v.Beschreibung,
            ["Entf"] = v.IstEntfall ? "X" : "",
            ["Mitb"] = v.Mitbetreuung ? "X" : "",
            ["Kopp"] = v.Kopplung > 0 ? v.Kopplung.ToString() : "",
            ["Neu"] = Vertretungen.IstNeu(v, _doc.Sitzungsbeginn) ? "Neu" : "",
            ["Fix"] = v.Fixiert ? "X" : "",
            ["ND"] = v.NichtDrucken ? "N" : "",
            ["Text2"] = v.Text2,
            ["Ben"] = benachrichtigen,
            ["Zaehler"] = zaehler,
            ["Unterschrift"] = "",
            ["Wert"] = v.Wert?.ToString("0.##", Format.De) ?? "",
            ["Abt"] = Anzeige(ElementTyp.Abteilung, abteilung, "Abt"),
            ["Benutzer"] = v.Benutzer,
            ["Mehr"] = v.Mehrarbeit ? "x" : "",
        };
        if (gruppe != null && _vaSammel.Contains(v.Id)) SammelWerte(w, v, gruppe);
        return w;
    }

    private void SammelWerte(Dictionary<string, object?> w, Vertretung v, List<Vertretung> gruppe)
    {
        var va = _d.Veranstaltungen.FirstOrDefault(x => x.Id == v.VeranstaltungId);
        string Liste(ElementTyp typ, IEnumerable<int> ids) => VeranstaltungsAuswahl.Kurztext(_d, typ, ids.Distinct().ToList(), va?.Von, va?.Bis, ", ");
        string Gleich(Func<Vertretung, int?> wert, ElementTyp typ) => gruppe.Select(wert).Distinct().ToList() is { Count: 1 } l && l[0] is int id ? _ix.K(typ, id) : "";
        var klassen = Liste(ElementTyp.Klasse, gruppe.SelectMany(x => x.KlassenIds));
        var lehrer = Liste(ElementTyp.Lehrer, gruppe.Where(x => x.LehrerId.HasValue).Select(x => x.LehrerId!.Value));
        w["Art"] = $"{Vertretungsplan.ArtText(_d, v.Art)} ({gruppe.Count})";
        w["AKlassen"] = klassen;
        w["Klassen"] = v.Art == VertretungsArt.Freisetzung && klassen.Length > 0 ? $"({klassen})" : klassen;
        w["Lehrer"] = lehrer;
        w["Vertreter"] = v.Art == VertretungsArt.Freisetzung ? lehrer : "---";
        w["Fach"] = Gleich(x => x.FachId, ElementTyp.Fach);
        w["Raum"] = Gleich(x => x.RaumId, ElementTyp.Raum);
        w["UText"] = "";
        w["UZText"] = "";
        if (va != null && string.IsNullOrWhiteSpace(v.Text)) w["Text"] = va.Text;
    }
}
