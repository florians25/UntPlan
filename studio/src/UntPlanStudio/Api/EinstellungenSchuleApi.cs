using System.Globalization;
using System.Security.Claims;
using UntPlan.Core;
using UntPlan.Core.Model;
using UntPlan.Core.Module;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;
using UntPlanStudio.Dienste;
using UntPlanStudio.Grundlagen;

namespace UntPlanStudio.Api;

/// <summary>Änderung der Schuldaten (nur gesetzte Felder werden übernommen).</summary>
public sealed record SchuldatenAenderung(
    string? Schulname, string? Schulname2, string? Adresse, string? Schulnummer, string? Land, string? Region, string? Schulform,
    DateOnly? SchuljahrVon, DateOnly? SchuljahrBis, int? Wochenperiodizitaet, string? ErsteSchulwoche, bool? TagesgenaueWertrechnung,
    int? Mehrarbeitsgrenze, string? EntfallAbJahrgang, bool? UnterrichtInFolgeperioden, bool? BereitschaftNurDoppelstunden,
    string? Vertretungsplantitel, Dictionary<string, int>? Vertretungsgewichtung, string? Farbschema);

public sealed record ZeitrasterZeit(int Nr, string Beginn, string Ende);
public sealed record ZeitrasterPause(int NachStunde, string? Bezeichnung, int DauerMinuten, bool Aufsicht);
/// <summary>Vollständiges Zeitraster (wie der Desktop-Dialog „Zeitraster“). Belegung je Tag/Stunde: „V“, „N“ oder „“ (leer).</summary>
public sealed record ZeitrasterDaten(
    int TageProWoche, int StundenProTag, int ErsterSchultag, int ErsteStundennummer, int LetzteVormittagsstunde, int MaxNachmittagsstunden,
    int NormalstundeMinuten, List<ZeitrasterZeit> Zeiten, List<List<string>> Belegung, List<ZeitrasterPause> Pausen,
    int MittagspauseVon, int MittagspauseBis, int MaxKlassenGleicheMittagspause);

public sealed record FerienEintrag(string? Kurzname, string? Langname, DateOnly Von, DateOnly? Bis, bool Feiertag = false, string? WocheDanach = null, bool Wochennummerierung = false);
public sealed record FerienFeld(string Feld, string? Wert);
public sealed record FerienTag(DateOnly Datum, bool Unterrichtsfrei, bool Feiertag);
public sealed record SchuleIdListe(List<int> Ids);
public sealed record PeriodeNeu(int? MutterId, string? Name, DateOnly Von, DateOnly? Bis);
public sealed record PeriodeAenderung(string Name, string? Langname, DateOnly Von, DateOnly? Bis, bool Fixiert);
public sealed record UGruppeDaten(string? Kurzname, string? Langname, DateOnly? Von, DateOnly? Bis, string? Wochen, bool Fix, bool Ignorieren, string? Unterbrechungen);
public sealed record SchuleZahl(int Wert);
public sealed record SchuleSchalter(bool Wert);

/// <summary>
/// Einstellungen der Schuldatei wie im Desktop (Dialoge Einstellungen, Zeitraster, Perioden, Fenster Kalender/Ferien und
/// Unterrichtsgruppen): Schuldaten und Programmeinstellungen, Zeitraster mit Pausen, Ferien/Feiertage (NRW-Vorschlag),
/// Perioden (anlegen, ändern, löschen, aktive Periode), Unterrichtsgruppen und Mehrwochen-Periodizität.
/// Endpunkte unter <c>/api/dokumente/{dok}/schule/…</c>.
/// </summary>
public sealed class EinstellungenSchuleApi : IStudioModul
{
    public string Name => "einstellungen-schule";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // ------------------------------------------------------------------------------------------ DTOs

    internal static object Schuldaten(Schuldatei d)
    {
        var e = d.Einstellungen;
        return new
        {
            e.Schulname, e.Schulname2, e.Adresse, e.Schulnummer, e.Land, e.Region, e.Schulform, e.SchuljahrVon, e.SchuljahrBis,
            e.Wochenperiodizitaet, e.ErsteSchulwoche, e.TagesgenaueWertrechnung, e.Mehrarbeitsgrenze, e.EntfallAbJahrgang,
            e.UnterrichtInFolgeperioden, e.BereitschaftNurDoppelstunden, e.Vertretungsplantitel, e.Farbschema,
            vertretungsgewichtung = VertretungsVorschlag.Kriterien.ToDictionary(k => k.Key, k => e.Vertretungsgewichtung.GetValueOrDefault(k.Key, VertretungsVorschlag.StandardGewichtung.GetValueOrDefault(k.Key))),
            kriterien = VertretungsVorschlag.Kriterien.Select(k => new { schluessel = k.Key, text = k.Text, standard = VertretungsVorschlag.StandardGewichtung.GetValueOrDefault(k.Key) }),
            schulformen = Nrw.Schulformen.Select(x => new { wert = x.Key, text = x.Value }),
            jahrgaenge = Nrw.Jahrgaenge.GetValueOrDefault(e.Schulform, Array.Empty<string>()),
            pflichtstunden = Format.Zahl(Nrw.Pflichtstunden.GetValueOrDefault(e.Schulform, 25.5m)),
        };
    }

    internal static object ZeitrasterDto(Schuldatei d)
    {
        var z = d.Zeitraster;
        return new
        {
            z.TageProWoche, z.StundenProTag, z.ErsterSchultag, z.ErsteStundennummer, z.LetzteVormittagsstunde, z.MaxNachmittagsstunden, z.NormalstundeMinuten,
            zeiten = Enumerable.Range(1, z.StundenProTag).Select(s => new { nr = s, label = z.StundenLabel(s), beginn = z.Zeit(s)?.Beginn.ToString("HH:mm") ?? "", ende = z.Zeit(s)?.Ende.ToString("HH:mm") ?? "" }),
            belegung = Enumerable.Range(0, z.TageProWoche).Select(t => Enumerable.Range(1, z.StundenProTag).Select(s => z.Art(t, s) switch { Tageszeit.Vormittag => "V", Tageszeit.Nachmittag => "N", _ => "" })),
            tage = Enumerable.Range(0, 7).Select(t => Planungsdienst.TageLang[t]),
            pausen = z.Pausen.OrderBy(p => p.NachStunde).Select(p => new { p.NachStunde, p.Bezeichnung, p.DauerMinuten, p.Aufsicht, kurz = Pausenbezeichnung.Kurz(z, p) }),
            z.MittagspauseVon, z.MittagspauseBis, z.MaxKlassenGleicheMittagspause,
        };
    }

    internal static object FerienDto(Schuldatei d)
    {
        var j = Kalender.JahresdatenBerechnen(d);
        return new
        {
            schuljahrVon = d.Einstellungen.SchuljahrVon,
            schuljahrBis = d.Einstellungen.SchuljahrBis,
            ersterSchultag = d.Zeitraster.ErsterSchultag,
            tageProWoche = d.Zeitraster.TageProWoche,
            ferien = d.Ferien.OrderBy(f => f.Von).Select(f => new { f.Id, f.Kurzname, f.Langname, f.Von, f.Bis, f.Feiertag, f.WocheDanach, f.Wochennummerierung, tage = f.Bis.DayNumber - f.Von.DayNumber + 1 }),
            kalender = d.Kalender.Where(k => k.Unterrichtsfrei || k.Feiertag).Select(k => new { k.Datum, k.Unterrichtsfrei, k.Feiertag }),
            jahresdaten = new
            {
                schuljahrTage = j.SchuljahrTage, schuljahrWochen = j.SchuljahrWochen, anzahlFerien = j.AnzahlFerien, anzahlFeiertage = j.AnzahlFeiertage,
                unterrichtTage = j.UnterrichtTage, unterrichtWochen = j.UnterrichtWochen, ferientage = j.Ferientage,
            },
        };
    }

    internal static object PeriodenDto(Schuldatei d) => new
    {
        aktiv = d.AktivePeriode.Id,
        unterrichtInFolgeperioden = d.Einstellungen.UnterrichtInFolgeperioden,
        perioden = d.Perioden.OrderBy(p => p.Von).ThenBy(p => p.Id).Select(p => new
        {
            p.Id, p.Name, p.Langname, p.Von, p.Bis, ende = Perioden.Ende(d, p), p.MutterId,
            mutter = p.MutterId is int m ? d.Perioden.FirstOrDefault(x => x.Id == m)?.Name : null,
            p.Fixiert, schultage = Perioden.Schultage(d, p), unterrichte = p.Unterrichte?.Count ?? 0, planstunden = p.Plan.Count,
            aktiv = p.Id == d.AktivePeriode.Id, ebene = Ebene(d, p),
        }),
    };

    private static int Ebene(Schuldatei d, Periode p)
    {
        var n = 0;
        for (var m = p.MutterId; m is int id && n < 20; n++) m = d.Perioden.FirstOrDefault(x => x.Id == id)?.MutterId;
        return n;
    }

    internal static object UGruppenDto(Schuldatei d)
    {
        var wochen = Math.Clamp(d.Einstellungen.Wochenperiodizitaet, 1, 16);
        return new
        {
            wochenperiodizitaet = wochen,
            wochen = Enumerable.Range(0, wochen).Select(w => ((char)('A' + w)).ToString()),
            gruppen = d.UGruppen.Select(g => new
            {
                g.Id, g.Kurzname, g.Langname, g.Von, g.Bis, wochen = g.Wochen.ToUpperInvariant(), g.Fix, g.Ignorieren,
                unterbrechungen = string.Join("; ", g.Unterbrechungen.Select(z => $"{z.Von:dd.MM.}-{z.Bis:dd.MM.}")),
                faktor = Kalender.UGruppeFaktor(d, g).ToString("0.00", De),
                unterrichte = d.AlleUnterrichte.Count(u => u.UGruppeId == g.Id || u.Zeilen.Any(z => z.UGruppeId == g.Id)),
            }),
        };
    }

    // ------------------------------------------------------------------------------------------ Hilfen

    private static TimeOnly Zeit(string? text, string was)
    {
        if (TimeOnly.TryParseExact((text ?? "").Trim(), new[] { "H:mm", "HH:mm", "H.mm", "HH.mm", "Hmm", "HHmm" }, De, DateTimeStyles.None, out var t)) return t;
        throw StudioFehler.Ungueltig($"Ungültige Uhrzeit bei {was}: „{text}“ (Beispiel 07:55).");
    }

    /// <summary>Datum wie im Desktop: „TT.MM.“ (Jahr aus dem Bezug), „TT.MM.JJJJ“ oder ISO.</summary>
    internal static DateOnly DatumLesen(string text, DateOnly bezug)
    {
        var t = text.Trim();
        if (DateOnly.TryParseExact(t, new[] { "yyyy-MM-dd", "d.M.yyyy", "dd.MM.yyyy", "d.M.yy" }, De, DateTimeStyles.None, out var d)) return d;
        var teile = t.TrimEnd('.').Split('.');
        if (teile.Length == 2 && int.TryParse(teile[0], out var tag) && int.TryParse(teile[1], out var monat) && monat is >= 1 and <= 12 && tag >= 1 && tag <= DateTime.DaysInMonth(bezug.Year, monat))
        {
            var x = new DateOnly(bezug.Year, monat, tag);
            // Schuljahr: Monate vor dem Schuljahresbeginn gehören ins folgende Kalenderjahr
            return x < bezug && monat < bezug.Month ? x.AddYears(1) : x;
        }
        throw StudioFehler.Ungueltig($"Ungültiges Datum „{text}“ (TT.MM. oder TT.MM.JJJJ).");
    }

    private static List<Zeitraum> Unterbrechungen(string? text, DateOnly bezug) =>
        (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t =>
        {
            var teile = t.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var von = DatumLesen(teile[0], bezug);
            var bis = teile.Length > 1 ? DatumLesen(teile[1], bezug) : von;
            return new Zeitraum { Von = von, Bis = bis < von ? von : bis };
        }).ToList();

    private static Periode Periode(Schuldatei d, int id) => d.Perioden.FirstOrDefault(p => p.Id == id) ?? throw StudioFehler.NichtGefunden($"Die Periode Nr. {id}");

    private static UGruppe UGruppeAus(UGruppeDaten n, int id, Schuldatei d)
    {
        var wochen = Math.Clamp(d.Einstellungen.Wochenperiodizitaet, 1, 16);
        var w = new string((n.Wochen ?? "").ToUpperInvariant().Where(c => c >= 'A' && c < 'A' + wochen).Distinct().OrderBy(c => c).ToArray());
        if (w.Length == wochen) w = "";
        if (n.Von is DateOnly v && n.Bis is DateOnly b && b < v) throw StudioFehler.Ungueltig("Das Ende liegt vor dem Beginn.");
        return new UGruppe
        {
            Id = id, Kurzname = string.IsNullOrWhiteSpace(n.Kurzname) ? "UG" : n.Kurzname.Trim(), Langname = (n.Langname ?? "").Trim(),
            Von = n.Von, Bis = n.Bis, Wochen = w, Fix = n.Fix, Ignorieren = n.Ignorieren,
            Unterbrechungen = Unterbrechungen(n.Unterbrechungen, d.Einstellungen.SchuljahrVon),
        };
    }

    // ------------------------------------------------------------------------------------------ Endpunkte

    public void EndpunkteRegistrieren(StudioRouten r)
    {
        var g = r.Dokument.MapGroup("/schule");

        // ---- Schuldaten und Programmeinstellungen ----
        g.MapGet("/schuldaten", (Arbeitsdokument a) => a.LesenAsync(doc => Schuldaten(doc.Daten)));
        g.MapPut("/schuldaten", (Arbeitsdokument a, SchuldatenAenderung n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Einstellungen", n.Wochenperiodizitaet is null ? AenderungsArt.Einstellungen : AenderungsArt.Alles, d =>
            {
                var e = d.Einstellungen;
                var von = n.SchuljahrVon ?? e.SchuljahrVon;
                var bis = n.SchuljahrBis ?? e.SchuljahrBis;
                if (bis <= von) throw StudioFehler.Ungueltig("Das Schuljahresende muss nach dem Beginn liegen.");
                if (n.Schulform is { } sf && !Nrw.Schulformen.ContainsKey(sf)) throw StudioFehler.Ungueltig($"Unbekannte Schulform „{sf}“.");
                if (n.Wochenperiodizitaet is int w && (w < 1 || w > 16)) throw StudioFehler.Ungueltig("Die Wochenperiodizität muss zwischen 1 und 16 liegen.");
                if (n.Schulname is { } s1) e.Schulname = s1.Trim();
                if (n.Schulname2 is { } s2) e.Schulname2 = s2.Trim();
                if (n.Adresse is { } ad) e.Adresse = ad.Trim();
                if (n.Schulnummer is { } nr) e.Schulnummer = nr.Trim();
                if (n.Land is { } la) e.Land = la.Trim();
                if (n.Region is { } re) e.Region = re.Trim();
                if (n.Schulform is { } fo) e.Schulform = fo;
                e.SchuljahrVon = von;
                e.SchuljahrBis = bis;
                if (n.Wochenperiodizitaet is int wp) e.Wochenperiodizitaet = wp;
                if (n.ErsteSchulwoche is { Length: > 0 } ew) e.ErsteSchulwoche = ew.Trim().ToUpperInvariant()[..1];
                if (n.TagesgenaueWertrechnung is bool tg) e.TagesgenaueWertrechnung = tg;
                if (n.Mehrarbeitsgrenze is int mg) e.Mehrarbeitsgrenze = Math.Clamp(mg, 0, 40);
                if (n.EntfallAbJahrgang is { } ej) e.EntfallAbJahrgang = ej.Trim() == "nie" ? "99" : ej.Trim();
                if (n.UnterrichtInFolgeperioden is bool uf) e.UnterrichtInFolgeperioden = uf;
                if (n.BereitschaftNurDoppelstunden is bool bd) e.BereitschaftNurDoppelstunden = bd;
                if (n.Vertretungsplantitel is { } vt) e.Vertretungsplantitel = vt.Trim();
                if (n.Farbschema is { } fs) e.Farbschema = fs;
                if (n.Vertretungsgewichtung is { } vg)
                    foreach (var (k, v) in vg.Where(x => VertretungsVorschlag.StandardGewichtung.ContainsKey(x.Key))) e.Vertretungsgewichtung[k] = Math.Clamp(v, 0, 5);
                Kalender.CacheLeeren();
            }, doc => Schuldaten(doc.Daten)));

        // ---- Zeitraster ----
        g.MapGet("/zeitraster", (Arbeitsdokument a) => a.LesenAsync(doc => ZeitrasterDto(doc.Daten)));
        g.MapPut("/zeitraster", (Arbeitsdokument a, ZeitrasterDaten n, ClaimsPrincipal p) =>
        {
            if (n.TageProWoche is < 1 or > 7) throw StudioFehler.Ungueltig("Anzahl Tage: 1 bis 7.");
            if (n.StundenProTag is < 1 or > 16) throw StudioFehler.Ungueltig("Maximale Stundenzahl pro Tag: 1 bis 16.");
            if (n.ErsterSchultag is < 0 or > 6) throw StudioFehler.Ungueltig("Ungültiger erster Schultag.");
            if (n.ErsteStundennummer is < 0 or > 1) throw StudioFehler.Ungueltig("Die erste Stunde hat die Nummer 0 oder 1.");
            var z = new Zeitraster
            {
                TageProWoche = n.TageProWoche, StundenProTag = n.StundenProTag, ErsterSchultag = n.ErsterSchultag, ErsteStundennummer = n.ErsteStundennummer,
                LetzteVormittagsstunde = Math.Clamp(n.LetzteVormittagsstunde, 1, 16), MaxNachmittagsstunden = Math.Clamp(n.MaxNachmittagsstunden, 0, 16),
                NormalstundeMinuten = Math.Clamp(n.NormalstundeMinuten <= 0 ? 45 : n.NormalstundeMinuten, 10, 120),
                MittagspauseVon = Math.Clamp(n.MittagspauseVon, 0, 16), MittagspauseBis = Math.Clamp(n.MittagspauseBis, 0, 16),
                MaxKlassenGleicheMittagspause = Math.Max(0, n.MaxKlassenGleicheMittagspause),
            };
            foreach (var t in n.Zeiten.Where(t => t.Nr >= 1 && t.Nr <= n.StundenProTag).GroupBy(t => t.Nr).Select(x => x.Last()))
            {
                if (string.IsNullOrWhiteSpace(t.Beginn)) continue;
                var b = Zeit(t.Beginn, $"Stunde {t.Nr} (Beginn)");
                var e = string.IsNullOrWhiteSpace(t.Ende) ? b.AddMinutes(z.NormalstundeMinuten) : Zeit(t.Ende, $"Stunde {t.Nr} (Ende)");
                if (e <= b) throw StudioFehler.Ungueltig($"Stunde {t.Nr}: Das Ende muss nach dem Beginn liegen.");
                z.Zeiten.Add(new Stundenzeit { Nr = t.Nr, Beginn = b, Ende = e });
            }
            for (var tag = 0; tag < Math.Min(n.Belegung.Count, z.TageProWoche); tag++)
                for (var st = 1; st <= Math.Min(n.Belegung[tag].Count, z.StundenProTag); st++)
                {
                    var art = n.Belegung[tag][st - 1] switch { "V" => Tageszeit.Vormittag, "N" => Tageszeit.Nachmittag, _ => Tageszeit.Leer };
                    var standard = st <= z.LetzteVormittagsstunde ? Tageszeit.Vormittag : Tageszeit.Nachmittag;
                    if (art != standard) z.Belegung[tag * 100 + st] = art;
                }
            z.Pausen = n.Pausen.Where(x => x.NachStunde >= 0 && x.NachStunde <= z.StundenProTag).GroupBy(x => x.NachStunde).Select(x => x.Last())
                .OrderBy(x => x.NachStunde).Select(x => new Pause { NachStunde = x.NachStunde, Bezeichnung = (x.Bezeichnung ?? "").Trim(), DauerMinuten = Math.Max(0, x.DauerMinuten), Aufsicht = x.Aufsicht }).ToList();
            return a.AendernAsync(p.Benutzername(), "Zeitraster", AenderungsArt.Alles, d =>
            {
                d.Zeitraster = z;
                Dokument.Normalisieren(d);
                Kalender.CacheLeeren();
            }, doc => ZeitrasterDto(doc.Daten));
        });

        // ---- Ferien und Feiertage ----
        g.MapGet("/ferien", (Arbeitsdokument a) => a.LesenAsync(doc => FerienDto(doc.Daten)));
        g.MapPost("/ferien", (Arbeitsdokument a, FerienEintrag n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Ferien angelegt", AenderungsArt.Einstellungen, d =>
            {
                var bis = n.Bis ?? n.Von;
                if (bis < n.Von) throw StudioFehler.Ungueltig("Das Ende liegt vor dem Beginn.");
                var kurz = string.IsNullOrWhiteSpace(n.Kurzname) ? (n.Von == bis ? $"{n.Von.Day}.{n.Von.Month}" : "Ferien") : n.Kurzname.Trim();
                var lang = string.IsNullOrWhiteSpace(n.Langname) ? (n.Von == bis ? $"{n.Von.Day}.{n.Von.Month}.{n.Von.Year}" : $"Ferien {Format.Datum(n.Von)} – {Format.Datum(bis)}") : n.Langname.Trim();
                d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = kurz, Langname = lang, Von = n.Von, Bis = bis, Feiertag = n.Feiertag, WocheDanach = WocheDanach(n.WocheDanach), Wochennummerierung = n.Wochennummerierung });
                d.Ferien.Sort((x, y) => x.Von.CompareTo(y.Von));
                Kalender.CacheLeeren();
            }, doc => FerienDto(doc.Daten)));
        g.MapPatch("/ferien/{id:int}", (Arbeitsdokument a, int id, FerienFeld n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Ferien geändert", AenderungsArt.Einstellungen, d =>
            {
                var f = d.Ferien.FirstOrDefault(x => x.Id == id) ?? throw StudioFehler.NichtGefunden($"Der Ferieneintrag Nr. {id}");
                var w = (n.Wert ?? "").Trim();
                switch (n.Feld)
                {
                    case "kurzname": if (w.Length == 0) throw StudioFehler.Ungueltig("Bitte einen Namen angeben."); f.Kurzname = w; break;
                    case "langname": f.Langname = w; break;
                    case "von": f.Von = DatumLesen(w, d.Einstellungen.SchuljahrVon); if (f.Bis < f.Von) f.Bis = f.Von; break;
                    case "bis": var b = DatumLesen(w, f.Von); f.Bis = b < f.Von ? f.Von : b; break;
                    case "wocheDanach": f.WocheDanach = WocheDanach(w); break;
                    case "wochennummerierung": f.Wochennummerierung = Bool(w); break;
                    case "feiertag": f.Feiertag = Bool(w); break;
                    default: throw StudioFehler.Ungueltig($"Unbekanntes Feld „{n.Feld}“.");
                }
                d.Ferien.Sort((x, y) => x.Von.CompareTo(y.Von));
                Kalender.CacheLeeren();
            }, doc => FerienDto(doc.Daten)));
        g.MapPost("/ferien/loeschen", (Arbeitsdokument a, SchuleIdListe n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), n.Ids.Count == 1 ? "Ferien gelöscht" : $"{n.Ids.Count} Ferieneinträge gelöscht", AenderungsArt.Einstellungen, d =>
            {
                d.Ferien.RemoveAll(f => n.Ids.Contains(f.Id));
                Kalender.CacheLeeren();
            }, doc => FerienDto(doc.Daten)));
        g.MapPost("/ferien/nrw", (Arbeitsdokument a, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Ferien und Feiertage NRW eingetragen", AenderungsArt.Einstellungen, d =>
            {
                Nrw.FerienEintragen(d);
                Kalender.CacheLeeren();
            }, doc => FerienDto(doc.Daten)));
        // Häkchen „Unterrichtsfrei“/„Feiertag“ für einen Tag (wie im Kalender des Desktops): eintägigen Eintrag anlegen bzw. Tag herausnehmen
        g.MapPost("/ferien/tag", (Arbeitsdokument a, FerienTag n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), $"Kalender {Format.Datum(n.Datum)}", AenderungsArt.Einstellungen, d =>
            {
                var tag = n.Datum;
                var f = d.Ferien.Where(x => tag >= x.Von && tag <= x.Bis).OrderBy(x => x.Bis.DayNumber - x.Von.DayNumber).FirstOrDefault();
                if (n.Unterrichtsfrei || n.Feiertag)
                {
                    if (f is null)
                    {
                        f = new Ferien { Id = d.NeueId(), Kurzname = $"{tag.Day}.{tag.Month}", Langname = $"{tag.Day}.{tag.Month}.{tag.Year}", Von = tag, Bis = tag };
                        d.Ferien.Add(f);
                    }
                    f.Feiertag = n.Feiertag;
                }
                else if (f != null)
                {
                    if (f.Von == f.Bis) d.Ferien.Remove(f);
                    else if (f.Von == tag) f.Von = tag.AddDays(1);
                    else if (f.Bis == tag) f.Bis = tag.AddDays(-1);
                    else
                    {
                        d.Ferien.Add(new Ferien { Id = d.NeueId(), Kurzname = f.Kurzname, Langname = f.Langname, Von = tag.AddDays(1), Bis = f.Bis, Feiertag = f.Feiertag, WocheDanach = f.WocheDanach, Wochennummerierung = f.Wochennummerierung });
                        f.Bis = tag.AddDays(-1);
                    }
                }
                d.Ferien.Sort((x, y) => x.Von.CompareTo(y.Von));
                Kalender.CacheLeeren();
            }, doc => FerienDto(doc.Daten)));

        // ---- Perioden ----
        g.MapGet("/perioden", (Arbeitsdokument a) => a.LesenAsync(doc => PeriodenDto(doc.Daten)));
        g.MapPost("/perioden", (Arbeitsdokument a, PeriodeNeu n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Periode angelegt", AenderungsArt.Alles, d =>
            {
                var mutter = n.MutterId is int m ? Periode(d, m) : d.AktivePeriode;
                Perioden.Neu(d, mutter, n.Name ?? "", n.Von, n.Bis);
            }, doc => PeriodenDto(doc.Daten)));
        g.MapPut("/perioden/{id:int}", (Arbeitsdokument a, int id, PeriodeAenderung n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Periode geändert", AenderungsArt.Alles,
                d => Perioden.Aendern(d, Periode(d, id), n.Name, n.Langname ?? n.Name, n.Von, n.Bis, n.Fixiert), doc => PeriodenDto(doc.Daten)));
        g.MapDelete("/perioden/{id:int}", (Arbeitsdokument a, int id, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Periode gelöscht", AenderungsArt.Alles, d => Perioden.Loeschen(d, Periode(d, id)), doc => PeriodenDto(doc.Daten)));
        g.MapPost("/perioden/{id:int}/aktiv", (Arbeitsdokument a, int id, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Aktive Periode gewechselt", AenderungsArt.Alles, d =>
            {
                var per = Periode(d, id);
                per.Unterrichte ??= new();
                d.AktivePeriodeId = per.Id;
            }, doc => PeriodenDto(doc.Daten)));
        g.MapPut("/perioden/folgeperioden", (Arbeitsdokument a, SchuleSchalter n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Unterrichtsänderungen in Folgeperioden", AenderungsArt.Einstellungen,
                d => d.Einstellungen.UnterrichtInFolgeperioden = n.Wert, doc => PeriodenDto(doc.Daten)));

        // ---- Unterrichtsgruppen und Mehrwochen ----
        g.MapGet("/ugruppen", (Arbeitsdokument a) => a.LesenAsync(doc => UGruppenDto(doc.Daten)));
        g.MapPost("/ugruppen", (Arbeitsdokument a, UGruppeDaten n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Unterrichtsgruppe anlegen", AenderungsArt.Unterricht, d =>
            {
                d.UGruppen.Add(UGruppeAus(n, d.NeueId(), d));
                Kalender.CacheLeeren();
            }, doc => UGruppenDto(doc.Daten)));
        g.MapPut("/ugruppen/{id:int}", (Arbeitsdokument a, int id, UGruppeDaten n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Unterrichtsgruppe ändern", AenderungsArt.Unterricht, d =>
            {
                var i = d.UGruppen.FindIndex(x => x.Id == id);
                if (i < 0) throw StudioFehler.NichtGefunden($"Die Unterrichtsgruppe Nr. {id}");
                d.UGruppen[i] = UGruppeAus(n, id, d);
                Kalender.CacheLeeren();
            }, doc => UGruppenDto(doc.Daten)));
        g.MapPost("/ugruppen/loeschen", (Arbeitsdokument a, SchuleIdListe n, ClaimsPrincipal p) =>
            a.AendernAsync(p.Benutzername(), "Unterrichtsgruppe löschen", AenderungsArt.Unterricht, d =>
            {
                var ids = n.Ids.ToHashSet();
                d.UGruppen.RemoveAll(x => ids.Contains(x.Id));
                foreach (var u in d.AlleUnterrichte)
                {
                    if (u.UGruppeId is int ug && ids.Contains(ug)) u.UGruppeId = null;
                    foreach (var z in u.Zeilen.Where(z => z.UGruppeId is int zg && ids.Contains(zg))) z.UGruppeId = null;
                }
                Kalender.CacheLeeren();
            }, doc => UGruppenDto(doc.Daten)));
        g.MapPut("/wochenperiodizitaet", (Arbeitsdokument a, SchuleZahl n, ClaimsPrincipal p) =>
        {
            if (n.Wert is < 1 or > 16) throw StudioFehler.Ungueltig("Die Wochenperiodizität muss zwischen 1 und 16 liegen.");
            return a.AendernAsync(p.Benutzername(), "Wochenperiodizität", AenderungsArt.Alles, d =>
            {
                d.Einstellungen.Wochenperiodizitaet = n.Wert;
                Kalender.CacheLeeren();
            }, doc => UGruppenDto(doc.Daten));
        });
    }

    private static string WocheDanach(string? w)
    {
        var t = (w ?? "").Trim().ToUpperInvariant();
        return t.Length > 0 && t[0] is >= 'A' and <= 'P' ? t[..1] : "";
    }

    private static bool Bool(string w) => w.Trim().ToLowerInvariant() is "x" or "true" or "1" or "ja" or "j";
}
