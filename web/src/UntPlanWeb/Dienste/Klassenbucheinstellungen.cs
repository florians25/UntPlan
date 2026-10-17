using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Klassenbuch | Einstellungen (Administration): Hauptseite (Stundendetails), Allgemein, Erinnerung an offene Stunden und
/// Unterricht (Prüfungen). Gespeichert als JSON in der Tabelle einstellungen; fehlende Werte erhalten ihren Standard.
/// Zahlen ohne Wert (null) bedeuten „unbegrenzt“ – Standard für alle Eingabegrenzen, damit Lehrstoff, Notizen und
/// „Findet online statt“ ohne zeitliche Begrenzung bleiben.
/// </summary>
public sealed record KlassenbuchEinstellungen
{
    // ---------------------------------------------------------------- Hauptseite
    /// <summary>Sortierung der Schülerliste: nachname | vorname.</summary>
    public string Sortierung { get; init; } = "nachname";
    /// <summary>Anordnung der Schülerkacheln: zeilen (von links nach rechts) | spalten (von oben nach unten).</summary>
    public string Richtung { get; init; } = "zeilen";
    /// <summary>Schülerliste zuerst nach Klasse sortieren (gekoppelter Unterricht).</summary>
    public bool NachKlasse { get; init; }
    /// <summary>Abwesenheiten dürfen Fachlehrkräfte nur während der Unterrichtsstunde eintragen.</summary>
    public bool AbwesenheitNurInStunde { get; init; }
    /// <summary>Vorgabe für das Ende einer Abwesenheit: stunde (Stundenende) | tag (Tagesende) | zeit (fixer Zeitpunkt).</summary>
    public string AbwesenheitEnde { get; init; } = "stunde";
    /// <summary>Fixer Zeitpunkt (HH:mm) für <see cref="AbwesenheitEnde"/> = zeit.</summary>
    public string AbwesenheitEndeZeit { get; init; } = "";
    /// <summary>Direkte Abwesenheitseingabe über das Häkchen in der Schülerkachel.</summary>
    public bool DirekteEingabe { get; init; } = true;
    /// <summary>In den Stundendetails auch die Abwesenheiten der übrigen Stunden des Tages anzeigen.</summary>
    public bool AlleAbwesenheitenDesTages { get; init; }
    /// <summary>Klassenbucheinträge anzeigen ab: woche | schuljahr | datum | tage.</summary>
    public string EintraegeAb { get; init; } = "woche";
    public string EintraegeAbDatum { get; init; } = "";
    public int EintraegeTage { get; init; } = 7;
    /// <summary>Fällige, unerledigte Hausaufgaben in den Stundendetails anzeigen.</summary>
    public bool FaelligeHausaufgaben { get; init; } = true;

    // ---------------------------------------------------------------- Allgemein
    /// <summary>Abwesenheitskontrolle: Stunden ohne Kontrolle gelten als offen.</summary>
    public bool Abwesenheitskontrolle { get; init; } = true;
    /// <summary>Nummerierung von Entschuldigungen: klassenuebergreifend | klasse | schueler | keine.</summary>
    public string Nummerierung { get; init; } = "klassenuebergreifend";
    /// <summary>Erlaubter Datumsbereich für Eingaben (leer = offen).</summary>
    public string DatumVon { get; init; } = "";
    public string DatumBis { get; init; } = "";
    /// <summary>Eingabe bis so viele Tage in die Vergangenheit bzw. Zukunft (null = unbegrenzt).</summary>
    public int? TageVergangenheit { get; init; }
    public int? TageZukunft { get; init; }
    /// <summary>Abwesenheiten und Klassenbucheinträge dürfen nach so vielen Stunden nicht mehr gelöscht werden (0 = jederzeit; die Administration darf immer).</summary>
    public int Loeschsperre { get; init; }
    /// <summary>Offene Stunden anzeigen ab: woche | schuljahr | datum | tage.</summary>
    public string OffenAb { get; init; } = "woche";
    public string OffenAbDatum { get; init; } = "";
    public int OffenTage { get; init; } = 14;
    /// <summary>Offene Stunden anzeigen für: beide | kontrolle | lehrstoff.</summary>
    public string OffenFuer { get; init; } = "beide";
    /// <summary>Fehlstunde bzw. Fehltag zählt ab (Minuten „20“ oder Prozent „50%“).</summary>
    public string FehlstundeAb { get; init; } = "50%";
    public string FehltagAb { get; init; } = "50%";
    /// <summary>Höchstens so viele Fehlminuten je Unterrichtseinheit (0 = ohne Grenze).</summary>
    public int MaxFehlminuten { get; init; }
    /// <summary>Prüfungen für Fehlzeiten ignorieren.</summary>
    public bool PruefungenIgnorieren { get; init; }
    /// <summary>Lehrstoffeintragung bis so viele Tage in die Vergangenheit (null = unbegrenzt).</summary>
    public int? LehrstoffTage { get; init; }
    /// <summary>Lehrstoffeintragung für Stunden in der Zukunft erlauben.</summary>
    public bool LehrstoffZukunft { get; init; } = true;
    /// <summary>Lehrstoff gilt für alle Stunden eines Blocks (Doppel- bzw. Mehrfachstunden).</summary>
    public bool LehrstoffBlock { get; init; } = true;
    /// <summary>Entschuldigungsstatus immer mit dem Abwesenheitsgrund ändern (Grund „gilt als entschuldigt“).</summary>
    public bool StatusMitGrund { get; init; }
    /// <summary>Eingabe von Abwesenheitsgrund und Text durch Fachlehrkräfte nicht erlauben.</summary>
    public bool GrundTextGesperrt { get; init; }
    /// <summary>Standardabwesenheitsgründe (Kürzel): abwesend, später kommend, früher gehend, Abwesenheitsmeldung.</summary>
    public string GrundAbwesend { get; init; } = "";
    public string GrundSpaeter { get; init; } = "";
    public string GrundFrueher { get; init; } = "";
    public string GrundMeldung { get; init; } = "";
    /// <summary>Standard-Entschuldigungsstatus neuer Abwesenheiten: offen | entschuldigt | unentschuldigt.</summary>
    public string StandardStatus { get; init; } = "offen";
    /// <summary>Automatische Entschuldigung nach so vielen Tagen (null = aus) mit dem Status <see cref="AutoStatus"/>.</summary>
    public int? AutoTage { get; init; }
    public string AutoStatus { get; init; } = "entschuldigt";
    /// <summary>Textblöcke der Entschuldigungsberichte.</summary>
    public string TextVorUnterschrift { get; init; } = "";
    public string TextNachUnterschrift { get; init; } = "";
    /// <summary>Entschuldigungsschreiben für Schüler*innen: sichtbar | ausgeblendet.</summary>
    public string Entschuldigungsschreiben { get; init; } = "sichtbar";

    // ---------------------------------------------------------------- Fachlehrer-Entschuldigungen
    /// <summary>Lehrkräfte entschuldigen einzelne Fehlstunden aus ihrem Unterricht (Seite „Fehlzeiten“, App); gilt für alle Jahrgänge.</summary>
    public bool FachlehrerEntschuldigungen { get; init; }
    /// <summary>Nur Fehlstunden von Klassen in den Jahrgangsstufen <see cref="FachlehrerJahrgangVon"/> bis <see cref="FachlehrerJahrgangBis"/>.</summary>
    public bool FachlehrerJahrgangEinschraenken { get; init; }
    public int? FachlehrerJahrgangVon { get; init; }
    public int? FachlehrerJahrgangBis { get; init; }

    /// <summary>Gilt das Modul für eine Klasse mit dieser Jahrgangsstufe (Stammdaten der Klasse, z. B. „11“ oder „Jg11“)?</summary>
    public bool FachlehrerJahrgangErlaubt(string? jahrgang)
    {
        if (!FachlehrerJahrgangEinschraenken) return true;
        var m = Regex.Match(jahrgang ?? "", @"\d+");
        if (!m.Success || !int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var j)) return false;
        return (FachlehrerJahrgangVon is not int v || j >= v) && (FachlehrerJahrgangBis is not int b || j <= b);
    }

    // ---------------------------------------------------------------- Erinnerung an offene Stunden
    public bool Erinnerung { get; init; }
    /// <summary>Wochentag des Versands (1 = Montag … 7 = Sonntag).</summary>
    public int ErinnerungTag { get; init; } = 1;

    // ---------------------------------------------------------------- Unterricht: Prüfungen
    /// <summary>Erlaubter Datumsbereich für Prüfungseingaben (leer = offen).</summary>
    public string PruefungVon { get; init; } = "";
    public string PruefungBis { get; init; } = "";
    /// <summary>Raumkonflikt bei Prüfungen zulassen.</summary>
    public bool Raumkonflikt { get; init; }
    /// <summary>Prüfungen nur so viele Tage in die Zukunft an UntPlan übergeben (null = unbegrenzt).</summary>
    public int? UebernahmeTage { get; init; }
    /// <summary>Nur vertretungsrelevante Prüfungen (mit Raum oder Aufsicht) an UntPlan übergeben.</summary>
    public bool NurVertretungsrelevant { get; init; }

    private static DateOnly? Datum(string s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string De(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Wirkung

    /// <summary>Erlaubter Zeitraum für Eingaben im Klassenbuch (Datumsbereich, Tage in Vergangenheit bzw. Zukunft); die Administration ist ausgenommen.</summary>
    public void EingabePruefen(Benutzer b, DateOnly datum, DateOnly heute)
    {
        if (b.IstAdmin) return;
        if (Datum(DatumVon) is DateOnly v && datum < v) throw new ArgumentException($"Eingaben im Klassenbuch sind erst ab {De(v)} erlaubt.");
        if (Datum(DatumBis) is DateOnly bi && datum > bi) throw new ArgumentException($"Eingaben im Klassenbuch sind nur bis {De(bi)} erlaubt.");
        if (TageVergangenheit is int z && datum < heute.AddDays(-z))
            throw new ArgumentException(z == 0 ? "Eingaben für vergangene Tage sind nicht erlaubt." : $"Eingaben sind nur bis {z} Tage in die Vergangenheit erlaubt.");
        if (TageZukunft is int f && datum > heute.AddDays(f))
            throw new ArgumentException(f == 0 ? "Eingaben für zukünftige Tage sind nicht erlaubt." : $"Eingaben sind nur bis {f} Tage in die Zukunft erlaubt.");
    }

    /// <summary>Lehrstoff: Tage in die Vergangenheit und Stunden in der Zukunft (zusätzlich zum allgemeinen Zeitraum).</summary>
    public void LehrstoffPruefen(Benutzer b, DateOnly datum, DateOnly heute)
    {
        if (b.IstAdmin) return;
        EingabePruefen(b, datum, heute);
        if (!LehrstoffZukunft && datum > heute) throw new ArgumentException("Lehrstoff kann für Stunden in der Zukunft nicht eingetragen werden.");
        if (LehrstoffTage is int t && datum < heute.AddDays(-t))
            throw new ArgumentException(t == 0 ? "Lehrstoff kann nur am Tag der Stunde eingetragen werden." : $"Lehrstoff kann nur bis {t} Tage in die Vergangenheit eingetragen werden.");
    }

    /// <summary>Löschsperre: nach <see cref="Loeschsperre"/> Stunden seit der Erfassung (yyyy-MM-dd HH:mm) darf nur noch die Administration löschen.</summary>
    public void LoeschenPruefen(Benutzer b, string erstellt, DateTime jetzt)
    {
        if (b.IstAdmin || Loeschsperre <= 0) return;
        if (!DateTime.TryParse(erstellt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return;
        if (jetzt - t > TimeSpan.FromHours(Loeschsperre))
            throw new UnauthorizedAccessException($"Abwesenheiten und Klassenbucheinträge können nach {Loeschsperre} Stunden nicht mehr gelöscht werden.");
    }

    /// <summary>Erlaubter Datumsbereich für Prüfungseingaben.</summary>
    public void PruefungPruefen(DateOnly datum)
    {
        if (Datum(PruefungVon) is DateOnly v && datum < v) throw new ArgumentException($"Prüfungen können erst ab {De(v)} eingetragen werden.");
        if (Datum(PruefungBis) is DateOnly b && datum > b) throw new ArgumentException($"Prüfungen können nur bis {De(b)} eingetragen werden.");
    }

    /// <summary>Wird eine offene Prüfung (Rückmeldung) jetzt schon an UntPlan übergeben?</summary>
    public bool PruefungUebergeben(Meldung m, DateOnly heute)
    {
        if (m.Typ != "klausur") return true;
        if (NurVertretungsrelevant && m.Raeume.Count == 0 && string.IsNullOrEmpty(m.Lehrer) && m.WeitereLehrer.Count == 0) return false;
        return UebernahmeTage is not int t || !DateOnly.TryParse(m.Von, CultureInfo.InvariantCulture, out var d) || d <= heute.AddDays(t);
    }

    public Func<Meldung, bool> Uebergabefilter(DateOnly heute) => m => PruefungUebergeben(m, heute);

    /// <summary>Vorgabe für „Offene Stunden anzeigen ab“ als Zeitbereich (Bereich, von, bis) der Seite „Offene Stunden“.</summary>
    public (string Bereich, DateOnly? Von, DateOnly? Bis) OffenVorgabe(DateOnly heute) => OffenAb switch
    {
        "schuljahr" => ("schuljahr", null, null),
        "datum" when Datum(OffenAbDatum) is DateOnly d && d <= heute => ("individuell", d, heute),
        "tage" => ("individuell", heute.AddDays(-Math.Max(0, OffenTage)), heute),
        _ => (OffeneStunden.Vorgabe, null, null),
    };

    /// <summary>Beginn der angezeigten Klassenbucheinträge in den Stundendetails.</summary>
    public DateOnly EintraegeAbFuer(DateOnly datum, DateOnly schuljahrVon) => EintraegeAb switch
    {
        "schuljahr" when schuljahrVon != DateOnly.MinValue && schuljahrVon <= datum => schuljahrVon,
        "schuljahr" => datum.AddDays(-365),
        "datum" when Datum(EintraegeAbDatum) is DateOnly d && d <= datum => d,
        "tage" => datum.AddDays(-Math.Max(0, EintraegeTage)),
        _ => Plandienst.Montag(datum),
    };

    /// <summary>Ist die Stunde offen? (Kriterium „Offene Stunden anzeigen für“; ohne Abwesenheitskontrolle zählt nur der Lehrstoff.)</summary>
    public bool Offen(bool geprueft, bool lehrstoff) => (Abwesenheitskontrolle ? OffenFuer : "lehrstoff") switch
    {
        "kontrolle" => !geprueft,
        "lehrstoff" => !lehrstoff,
        _ => !geprueft || !lehrstoff,
    };

    /// <summary>Standardabwesenheitsgrund je Art (fehlt, verspaetet, vorzeitig, meldung); leer = keiner.</summary>
    public string Standardgrund(string art) => art switch
    {
        "verspaetet" => GrundSpaeter,
        "vorzeitig" => GrundFrueher,
        "meldung" => GrundMeldung,
        _ => GrundAbwesend,
    };

    /// <summary>Fixer Zeitpunkt als Uhrzeit (nur bei <see cref="AbwesenheitEnde"/> = zeit).</summary>
    public TimeOnly? EndeZeit => AbwesenheitEnde == "zeit" && TimeOnly.TryParseExact(AbwesenheitEndeZeit, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Angaben für die Hauptseite (Stundendetails) – für alle Lehrkräfte lesbar.</summary>
    public object Hauptseite(DateOnly datum, DateOnly schuljahrVon) => new
    {
        sortierung = Sortierung, richtung = Richtung, nachKlasse = NachKlasse, abwesenheitNurInStunde = AbwesenheitNurInStunde,
        abwesenheitEnde = AbwesenheitEnde, abwesenheitEndeZeit = AbwesenheitEndeZeit, direkteEingabe = DirekteEingabe,
        alleAbwesenheitenDesTages = AlleAbwesenheitenDesTages, eintraegeAb = Plandienst.Iso(EintraegeAbFuer(datum, schuljahrVon)),
        faelligeHausaufgaben = FaelligeHausaufgaben, abwesenheitskontrolle = Abwesenheitskontrolle, grundTextGesperrt = GrundTextGesperrt,
        lehrstoffBlock = LehrstoffBlock,
    };
}

/// <summary>Laden, Prüfen und Speichern der <see cref="KlassenbuchEinstellungen"/>.</summary>
public static class Klassenbucheinstellungen
{
    public const string Schluessel = "einstellung:klassenbuch";
    public const int LoeschsperreHoechstens = 999;
    private const int TageHoechstens = 3650;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Uhrzeit = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.CultureInvariant);
    private static readonly Regex Schwelle = new(@"^\s*(\d{1,3})\s*(%|min)?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Gespeicherte Einstellungen (fehlende bzw. ungültige Angaben = Standard).</summary>
    public static KlassenbuchEinstellungen Laden(Db db)
    {
        var json = new Einstellungen(db).Lesen(Schluessel);
        if (string.IsNullOrEmpty(json)) return new KlassenbuchEinstellungen();
        try { return JsonSerializer.Deserialize<KlassenbuchEinstellungen>(json, Json) ?? new KlassenbuchEinstellungen(); }
        catch (JsonException) { return new KlassenbuchEinstellungen(); }
    }

    public static KlassenbuchEinstellungen Speichern(Db db, KlassenbuchEinstellungen e)
    {
        var p = Pruefen(e);
        new Einstellungen(db).Schreiben(Schluessel, JsonSerializer.Serialize(p, Json));
        return p;
    }

    private static string Wahl(string? wert, string standard, params string[] erlaubt) => erlaubt.Contains(wert ?? "") ? wert! : standard;

    private static string DatumOderLeer(string? s, string feld) =>
        string.IsNullOrWhiteSpace(s) ? "" : DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d.ToString("yyyy-MM-dd") : throw new ArgumentException($"Ungültiges Datum bei „{feld}“.");

    private static int? Tage(int? t, string feld) => t switch
    {
        null => null,
        < 0 or > TageHoechstens => throw new ArgumentException($"Bitte für „{feld}“ eine Zahl von 0 bis {TageHoechstens} eingeben oder das Feld leer lassen (unbegrenzt)."),
        _ => t,
    };

    private static string SchwelleText(string? s, string feld)
    {
        var m = Schwelle.Match(s ?? "");
        if (!m.Success) throw new ArgumentException($"Bitte bei „{feld}“ Minuten (z. B. 20) oder Prozent (z. B. 50%) eingeben.");
        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (m.Groups[2].Value == "%") return n is >= 0 and <= 100 ? $"{n}%" : throw new ArgumentException($"Bei „{feld}“ höchstens 100 %.");
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static string Text(string? s, int max, string feld)
    {
        var t = (s ?? "").Trim();
        return t.Length <= max ? t : throw new ArgumentException($"„{feld}“ ist zu lang (höchstens {max} Zeichen).");
    }

    private static int? Jahrgang(int? j) => j switch
    {
        null => null,
        < 0 or > 20 => throw new ArgumentException("Bitte für die Jahrgangsstufen eine Zahl von 0 bis 20 eingeben."),
        _ => j,
    };

    private static void Bereich(string von, string bis, string feld)
    {
        if (von.Length > 0 && bis.Length > 0 && string.CompareOrdinal(von, bis) > 0) throw new ArgumentException($"Beim {feld} liegt das Ende vor dem Beginn.");
    }

    /// <summary>Eingaben prüfen und vereinheitlichen.</summary>
    public static KlassenbuchEinstellungen Pruefen(KlassenbuchEinstellungen e)
    {
        if (e.Loeschsperre is < 0 or > LoeschsperreHoechstens)
            throw new ArgumentException($"Die Löschsperre kann höchstens {LoeschsperreHoechstens} Stunden betragen (0 = keine Sperre).");
        var ende = Wahl(e.AbwesenheitEnde, "stunde", "stunde", "tag", "zeit");
        var endeZeit = (e.AbwesenheitEndeZeit ?? "").Trim();
        if (ende == "zeit" && !Uhrzeit.IsMatch(endeZeit)) throw new ArgumentException("Bitte für das Ende einer Abwesenheit eine Uhrzeit (HH:MM) angeben.");
        var r = e with
        {
            Sortierung = Wahl(e.Sortierung, "nachname", "nachname", "vorname"),
            Richtung = Wahl(e.Richtung, "zeilen", "zeilen", "spalten"),
            AbwesenheitEnde = ende,
            AbwesenheitEndeZeit = Uhrzeit.IsMatch(endeZeit) ? endeZeit : "",
            EintraegeAb = Wahl(e.EintraegeAb, "woche", "woche", "schuljahr", "datum", "tage"),
            EintraegeAbDatum = DatumOderLeer(e.EintraegeAbDatum, "Klassenbucheinträge anzeigen ab"),
            EintraegeTage = Math.Clamp(e.EintraegeTage, 0, 366),
            Nummerierung = Wahl(e.Nummerierung, "klassenuebergreifend", "klassenuebergreifend", "klasse", "schueler", "keine"),
            DatumVon = DatumOderLeer(e.DatumVon, "Erlaubter Datumsbereich"),
            DatumBis = DatumOderLeer(e.DatumBis, "Erlaubter Datumsbereich"),
            TageVergangenheit = Tage(e.TageVergangenheit, "Eingabe bis Tage in die Vergangenheit"),
            TageZukunft = Tage(e.TageZukunft, "Eingabe bis Tage in die Zukunft"),
            OffenAb = Wahl(e.OffenAb, "woche", "woche", "schuljahr", "datum", "tage"),
            OffenAbDatum = DatumOderLeer(e.OffenAbDatum, "Offene Stunden anzeigen ab"),
            OffenTage = Math.Clamp(e.OffenTage, 0, 366),
            OffenFuer = Wahl(e.OffenFuer, "beide", "beide", "kontrolle", "lehrstoff"),
            FehlstundeAb = SchwelleText(e.FehlstundeAb, "Fehlstunde zählt ab"),
            FehltagAb = SchwelleText(e.FehltagAb, "Fehltag zählt ab"),
            MaxFehlminuten = Math.Clamp(e.MaxFehlminuten, 0, 600),
            LehrstoffTage = Tage(e.LehrstoffTage, "Lehrstoffeintragung bis Tage in die Vergangenheit"),
            GrundAbwesend = Text(e.GrundAbwesend, 20, "Standardabwesenheitsgrund"),
            GrundSpaeter = Text(e.GrundSpaeter, 20, "Standardabwesenheitsgrund"),
            GrundFrueher = Text(e.GrundFrueher, 20, "Standardabwesenheitsgrund"),
            GrundMeldung = Text(e.GrundMeldung, 20, "Standardabwesenheitsgrund"),
            StandardStatus = Wahl(e.StandardStatus, "offen", "offen", "entschuldigt", "unentschuldigt"),
            AutoTage = Tage(e.AutoTage, "Automatische Entschuldigung nach Tagen"),
            AutoStatus = Wahl(e.AutoStatus, "entschuldigt", "entschuldigt", "unentschuldigt"),
            TextVorUnterschrift = Text(e.TextVorUnterschrift, 2000, "Entschuldigungstext vor Unterschrift"),
            TextNachUnterschrift = Text(e.TextNachUnterschrift, 2000, "Entschuldigungstext nach Unterschrift"),
            Entschuldigungsschreiben = Wahl(e.Entschuldigungsschreiben, "sichtbar", "sichtbar", "ausgeblendet"),
            ErinnerungTag = Math.Clamp(e.ErinnerungTag, 1, 7),
            PruefungVon = DatumOderLeer(e.PruefungVon, "Erlaubter Datumsbereich für Prüfungseingaben"),
            PruefungBis = DatumOderLeer(e.PruefungBis, "Erlaubter Datumsbereich für Prüfungseingaben"),
            UebernahmeTage = Tage(e.UebernahmeTage, "Prüfungsübernahme Tage in die Zukunft"),
            FachlehrerJahrgangVon = Jahrgang(e.FachlehrerJahrgangVon),
            FachlehrerJahrgangBis = Jahrgang(e.FachlehrerJahrgangBis),
        };
        if (r.FachlehrerJahrgangEinschraenken && r.FachlehrerJahrgangVon is null && r.FachlehrerJahrgangBis is null)
            throw new ArgumentException("Bitte für die Einschränkung auf Jahrgangsstufen mindestens eine Jahrgangsstufe (von bzw. bis) angeben.");
        if (r.FachlehrerJahrgangVon is int jv && r.FachlehrerJahrgangBis is int jb && jv > jb)
            throw new ArgumentException("Bei den Jahrgangsstufen liegt „bis“ vor „von“.");
        Bereich(r.DatumVon, r.DatumBis, "erlaubten Datumsbereich");
        Bereich(r.PruefungVon, r.PruefungBis, "Datumsbereich für Prüfungseingaben");
        return r;
    }
}
