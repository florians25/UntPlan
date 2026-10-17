using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlan.Core;
using UntPlan.Core.Model;

namespace UntPlan.App;

/// <summary>Programmweiter Zustand: geöffnete Schuldatei, Element-Synchronisation zwischen Fenstern, Benutzereinstellungen.</summary>
public static class AppZustand
{
    public static Dokument Doc { get; private set; } = new(Demo.NeueDatei("Neue Schule", "GY"));
    public static HauptFenster Haupt { get; set; } = null!;
    public static Benutzereinstellungen Einstellungen { get; } = Benutzereinstellungen.Laden();

    public static event Action? DokumentGewechselt;
    /// <summary>Ein Element wurde in einem Fenster aktiviert (Synchronisation: Zeitwünsche, Stundenpläne).</summary>
    public static event Action<ElementTyp, int, object>? ElementGewaehlt;
    /// <summary>Eine Stunde wurde in einem Stundenplan aktiviert (Stundenplan-Synchronisation).</summary>
    public static event Action<Unterricht, Planstunde?, object>? StundeGewaehlt;
    /// <summary>Eine leere Zelle (nicht verplante Stunde) wurde in einem Stundenplan angeklickt (Stundenplan-Synchronisation).</summary>
    public static event Action<Core.Planung.PlanZelle, ElementTyp, int, object>? ZelleGewaehlt;

    public static Schuldatei D => Doc.Daten;

    /// <summary>Vertretungsmodus: eigene Fenster, eigenes Menüband, grüne Oberfläche.</summary>
    public static bool Vertretungsmodus { get; private set; }
    public static event Action? ModusGewechselt;

    /// <summary>Planungsdatum der Vertretungsplanung (alle Fenster werden auf diesen Tag synchronisiert).</summary>
    public static DateOnly Datum { get; private set; } = DateOnly.FromDateTime(DateTime.Today);
    public static event Action<DateOnly>? DatumGewechselt;

    /// <summary>Aktive Vertretungszeile (Vertretungsvorschlag folgt der Auswahl).</summary>
    public static Vertretung? AktiveVertretung { get; private set; }
    public static event Action<Vertretung?>? VertretungGewaehlt;

    /// <summary>Angemeldeter Benutzer (MultiUser) bzw. Windows-Benutzer.</summary>
    public static string Benutzer { get; set; } = Environment.UserName;

    public static void ModusSetzen(bool vertretung)
    {
        if (Vertretungsmodus == vertretung) return;
        Vertretungsmodus = vertretung;
        Ui.Vertretungsmodus = vertretung;
        ModusGewechselt?.Invoke();
    }

    public static void DatumSetzen(DateOnly datum)
    {
        if (Datum == datum) return;
        Datum = datum;
        DatumGewechselt?.Invoke(datum);
    }

    /// <summary>Im Vertretungsfenster markierte Zeilen (gleichzeitige Bearbeitung mehrerer Stunden).</summary>
    public static List<int> MarkierteVertretungen { get; } = new();

    public static void VertretungWaehlen(Vertretung? v, IEnumerable<int>? markiert = null)
    {
        AktiveVertretung = v;
        MarkierteVertretungen.Clear();
        if (markiert != null) MarkierteVertretungen.AddRange(markiert);
        else if (v != null) MarkierteVertretungen.Add(v.Id);
        VertretungGewaehlt?.Invoke(v);
    }

    static AppZustand()
    {
        AnsichtAnbinden(Doc);
        Einstellungen.AnsichtStandMerken();
        Einstellungen.AnsichtGeaendert += () =>
        {
            // im MultiUser-Betrieb gilt die Datenbank; Ansichten bleiben dort lokal
            if (!MultiUser.MultiUserSitzung.Aktiv) Doc.AlsGeaendertMarkieren();
        };
    }

    /// <summary>Wird vor dem Speichern der Ansicht aufgerufen (z. B. aktuelle Fensteranordnung des Modus aufnehmen).</summary>
    public static Action? AnsichtVorSpeichern { get; set; }

    private static void AnsichtAnbinden(Dokument d)
    {
        d.AnsichtLiefern ??= () =>
        {
            AnsichtVorSpeichern?.Invoke();
            var ansicht = Einstellungen.AnsichtAlsJson();
            Einstellungen.AnsichtStandMerken(ansicht);
            Einstellungen.Speichern();
            return ansicht;
        };
    }

    /// <summary>
    /// Neues Dokument übernehmen: Ansichtseinstellungen der Datei wiederherstellen (ältere Dateien ohne Ansicht behalten die
    /// bisherigen lokalen Einstellungen) und die Sperre des bisherigen Dokuments freigeben.
    /// </summary>
    public static void Setzen(Dokument d, bool ansichtUebernehmen = true)
    {
        var alt = Doc;
        // Stand des Stundenplans für die Nacherfassung umgeplanter Stunden (ältere Dateien ohne Stand)
        Core.Vertretungsplanung.Umplanungsabgleich.StandErgaenzen(d.Daten);
        AnsichtAnbinden(d);
        Doc = d;
        if (!ReferenceEquals(alt, d) && alt.Sperre is { } sperre && !ReferenceEquals(sperre, d.Sperre)) sperre.Freigeben();
        var uebernommen = ansichtUebernehmen && Einstellungen.AnsichtUebernehmen(d.Daten.Ansichtseinstellungen) > 0;
        Einstellungen.AnsichtStandMerken();
        if (uebernommen) Einstellungen.Speichern();
        DokumentGewechselt?.Invoke();
    }

    public static void Waehlen(ElementTyp typ, int id, object quelle) => ElementGewaehlt?.Invoke(typ, id, quelle);

    /// <summary>
    /// Leere Zelle gewählt (Tag/Stunde ohne Unterricht des angezeigten Elements <paramref name="typ"/>/<paramref name="id"/>):
    /// verknüpfte Pläne behalten ihr Element, heben ihre Auswahl auf und markieren dieselbe Stunde.
    /// </summary>
    public static void ZelleWaehlen(Core.Planung.PlanZelle zelle, ElementTyp typ, int id, object quelle) => ZelleGewaehlt?.Invoke(zelle, typ, id, quelle);
    /// <summary>
    /// Stunde gewählt. Optional mit den Elementen der angeklickten Zeile (z. B. im Lehrerplan nur die Klassen dieses Lehrers)
    /// und dem Datum (tagesaktueller Plan) – damit synchronisierte Pläne genau die passende Klasse bzw. Lehrkraft zeigen.
    /// </summary>
    public static void StundeWaehlen(Unterricht u, Planstunde? p, object quelle, IReadOnlyList<int>? klassen = null, IReadOnlyList<int>? lehrer = null,
        IReadOnlyList<int>? raeume = null, DateOnly? datum = null)
    {
        (AuswahlKlassen, AuswahlLehrer, AuswahlRaeume, AuswahlDatum) = (klassen, lehrer, raeume, datum);
        try { StundeGewaehlt?.Invoke(u, p, quelle); }
        finally { (AuswahlKlassen, AuswahlLehrer, AuswahlRaeume, AuswahlDatum) = (null, null, null, null); }
    }

    /// <summary>
    /// Fensterverknüpfung: Element einer Art zur gewählten Stunde (Elemente der angeklickten Zeile haben Vorrang). Gehört das bisher
    /// angezeigte Element zur Stunde, bleibt es (null = kein passendes Element, Fenster bleibt unverändert).
    /// </summary>
    public static int? ElementDerStunde(ElementTyp typ, Unterricht u, Planstunde? p, int? aktuell)
    {
        IEnumerable<int> ids = typ switch
        {
            ElementTyp.Klasse => AuswahlKlassen is { Count: > 0 } k ? k : u.KlassenIds,
            ElementTyp.Lehrer => AuswahlLehrer is { Count: > 0 } l ? l : u.LehrerIds,
            ElementTyp.Raum => AuswahlRaeume is { Count: > 0 } r ? r : p?.Raeume.Where(x => x.HasValue).Select(x => x!.Value) ?? Enumerable.Empty<int>(),
            ElementTyp.Fach => u.FachIds,
            ElementTyp.Unterricht => new[] { u.Id },
            _ => Enumerable.Empty<int>(),
        };
        var liste = ids.Distinct().ToList();
        if (aktuell is int a && liste.Contains(a)) return a;
        return liste.Count > 0 ? liste[0] : null;
    }

    public static IReadOnlyList<int>? AuswahlKlassen { get; private set; }
    public static IReadOnlyList<int>? AuswahlLehrer { get; private set; }
    public static IReadOnlyList<int>? AuswahlRaeume { get; private set; }
    public static DateOnly? AuswahlDatum { get; private set; }
}

/// <summary>
/// Benutzereinstellungen. Gerätebezogenes (zuletzt geöffnete Dateien, Begrüßung, Autospeichern, Dialoggrößen …) bleibt lokal
/// in einstellungen.json; die Ansichtseinstellungen (<see cref="AnsichtsFelder"/>) werden zusätzlich mit der Schuldatei
/// gespeichert und beim Öffnen daraus wiederhergestellt. Die lokale Kopie dient als Vorlage für neue und ältere Dateien.
/// </summary>
public sealed class Benutzereinstellungen
{
    /// <summary>Eigenschaften, die zur Ansicht gehören und in der .up-Datei gespeichert werden.</summary>
    public static readonly string[] AnsichtsFelder =
    {
        nameof(Stundenplanformate), nameof(Schnellanordnungen), nameof(Fensteranordnungen), nameof(StundenplanSynchronisieren),
        nameof(Zeitwunschfarben), nameof(Fenstergruppen), nameof(Vertretungsansicht), nameof(Drucklayouts), nameof(Tabellenformate),
        nameof(AktiveTabellenformate), nameof(Tabellenlayouts), nameof(Zeitraumarten), nameof(Vertretungsdruck), nameof(SichtbareSpalten),
        nameof(AbsenzenVeranstaltungenZeigen), nameof(VertretungsFelder), nameof(VertretungsArtenAus), nameof(VertretungenUnterrichtZeigen),
    };

    private JsonObject? _ansichtStand;

    /// <summary>Eine Ansichtseinstellung wurde geändert (die Schuldatei gilt dann als geändert).</summary>
    public event Action? AnsichtGeaendert;

    /// <summary>Aktuelle Ansichtseinstellungen als JSON (für die Schuldatei).</summary>
    public JsonObject AnsichtAlsJson() => Ansichtsdaten.Herausloesen(this, AnsichtsFelder);

    /// <summary>Ansichtseinstellungen aus einer Schuldatei übernehmen (fehlende Felder behalten den lokalen Wert).</summary>
    public int AnsichtUebernehmen(JsonObject? ansicht) => Ansichtsdaten.Uebernehmen(this, ansicht, AnsichtsFelder);

    /// <summary>Vergleichsstand für die Erkennung geänderter Ansichtseinstellungen (nach Öffnen bzw. Speichern der Datei).</summary>
    public void AnsichtStandMerken(JsonObject? stand = null) => _ansichtStand = stand ?? AnsichtAlsJson();

    private void AnsichtPruefen()
    {
        if (_ansichtStand is null) return;
        var jetzt = AnsichtAlsJson();
        if (Ansichtsdaten.Gleich(jetzt, _ansichtStand)) return;
        _ansichtStand = jetzt;
        AnsichtGeaendert?.Invoke();
    }

    public List<string> ZuletztGeoeffnet { get; set; } = new();
    public bool BegruessungZeigen { get; set; } = true;
    /// <summary>Beim Start die zuletzt geöffnete Schuldatei laden.</summary>
    public bool LetzteDateiLaden { get; set; } = true;
    /// <summary>Automatisch speichern alle n Minuten (0 = aus).</summary>
    public int AutoSpeichernMinuten { get; set; } = 10;
    /// <summary>Beim letzten Beenden war der Vertretungsmodus aktiv.</summary>
    public bool LetzterModusVertretung { get; set; }
    public bool ModusMerken { get; set; } = true;
    /// <summary>Zuletzt gewähltes Stundenplanformat je Planart ("Klasse", "Klasse groß" …).</summary>
    public Dictionary<string, string> Stundenplanformate { get; set; } = new();
    /// <summary>Fenster-Schnellknöpfe 1–8 oben rechts (Nummer → Anordnung).</summary>
    public Dictionary<int, Fensteranordnung> Schnellanordnungen { get; set; } = new();
    /// <summary>Benannte, vom Benutzer gespeicherte Fensteranordnungen.</summary>
    public Dictionary<string, Fensteranordnung> Fensteranordnungen { get; set; } = new();
    public bool AssistentZeigen { get; set; } = true;
    /// <summary>Lage des Hauptfensters im Fenstermodus (x, y, Breite, Höhe) – wird beim nächsten Start wiederhergestellt.</summary>
    public int[]? Hauptfenster { get; set; }
    /// <summary>Hauptfenster war beim Beenden maximiert.</summary>
    public bool HauptfensterMaximiert { get; set; } = true;
    public bool StundenplanSynchronisieren { get; set; } = true;
    public int[] Zeitwunschfarben { get; set; } = Array.Empty<int>();
    /// <summary>Fensteranordnung je Modus (Stundenplan / Vertretung): Schlüssel und Position.</summary>
    public Dictionary<string, List<FensterPosition>> Fenstergruppen { get; set; } = new();
    public string Vertretungsansicht { get; set; } = "Klasse - Standard";
    /// <summary>Druckformulare je Liste.</summary>
    public Dictionary<string, ListenDruck.Layout> Drucklayouts { get; set; } = new();
    /// <summary>Eigene Tabellenformate je Tabelle (Schlüssel = Fenster/Tabelle).</summary>
    public Dictionary<string, List<Tabellenformat>> Tabellenformate { get; set; } = new();
    /// <summary>Zuletzt gewähltes Tabellenformat je Tabelle.</summary>
    public Dictionary<string, string> AktiveTabellenformate { get; set; } = new();
    /// <summary>Aktuelle (automatisch gemerkte) Spaltenanordnung je Tabelle.</summary>
    public Dictionary<string, List<Tabellenspalte>> Tabellenlayouts { get; set; } = new();
    /// <summary>Gewählte Zeitraumart (Tag, Woche, Von-Bis) je Fenster.</summary>
    public Dictionary<string, string> Zeitraumarten { get; set; } = new();
    /// <summary>Einstellungen des Vertretungsplan-Drucks (Aushang).</summary>
    public Vertretungsdruck Vertretungsdruck { get; set; } = new();
    /// <summary>Sichtbare Spalten kleinerer Listen (z. B. „Mögliche Räume“), Schlüssel = Liste.</summary>
    public Dictionary<string, List<string>> SichtbareSpalten { get; set; } = new();
    /// <summary>Dialog „Raum zuordnen / löschen“: Auto-resize beim Öffnen deaktivieren (gemerkte Größe verwenden).</summary>
    public bool RaumDialogAutoResizeAus { get; set; }
    public Size RaumDialogGroesse { get; set; }
    /// <summary>Absenzfenster: Veranstaltungen zur Information mit anzeigen.</summary>
    public bool AbsenzenVeranstaltungenZeigen { get; set; }
    /// <summary>Vertretungsfenster „Felder der Ansicht“ je Ansicht: Feld → "a" (aktiv), "d" (drucken), "L" (Langname).</summary>
    public Dictionary<string, Dictionary<string, string>> VertretungsFelder { get; set; } = new();
    /// <summary>Vertretungsfenster: ausgeblendete Vertretungsarten je Ansicht.</summary>
    public Dictionary<string, List<string>> VertretungsArtenAus { get; set; } = new();
    /// <summary>Vertretungsfenster: regulär stattfindenden Unterricht mit anzeigen (Art „Unterricht“).</summary>
    public bool VertretungenUnterrichtZeigen { get; set; }
    /// <summary>
    /// Vertretungsfenster: Entfälle und Freisetzungen, die eine Veranstaltung mit vielen Stunden auslöst (Lehrkräftekonferenz,
    /// pädagogischer Tag), je Tag in einer Zeile zusammenfassen („Alle Klassen“, Stunde „1-6“) statt Hunderter Einzelzeilen.
    /// </summary>
    public bool VeranstaltungenZusammenfassen { get; set; } = true;

    private static string Pfad => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UntPlan", "einstellungen.json");

    public static Benutzereinstellungen Laden()
    {
        try { return JsonSerializer.Deserialize<Benutzereinstellungen>(File.ReadAllText(Pfad)) ?? new(); }
        catch { return new(); }
    }

    public void Speichern()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
        AnsichtPruefen();
    }

    public void Merken(string datei)
    {
        ZuletztGeoeffnet.Remove(datei);
        ZuletztGeoeffnet.Insert(0, datei);
        if (ZuletztGeoeffnet.Count > 10) ZuletztGeoeffnet.RemoveAt(10);
        Speichern();
    }
}

public sealed class Vertretungsdruck
{
    public string Schriftart { get; set; } = "Comic Sans MS";
    public float Schriftgroesse { get; set; } = 11f;
    public bool Nachrichten { get; set; } = true;
    public bool BlockierteRaeume { get; set; } = true;
    public bool AbwesendeKlassen { get; set; } = true;
    public bool AbwesendeLehrer { get; set; } = true;
    public bool Querformat { get; set; }
    /// <summary>Seitenlayout „Nur neue Vertretungen“: nur in dieser Arbeitssitzung angelegte oder geänderte Vertretungen drucken.</summary>
    public bool NurNeue { get; set; }

    // ---- Seitenlayout wie im Vorbild ----
    /// <summary>Felder (Spalten) des Ausdrucks in Druckreihenfolge; leer = Standardfelder.</summary>
    public List<DruckFeld> Felder { get; set; } = new();
    /// <summary>Auswahl der Klassen (Klassenversion) bzw. Lehrer (Lehrerversion); leer = alle.</summary>
    public List<string> KlassenAuswahl { get; set; } = new();
    public List<string> LehrerAuswahl { get; set; } = new();
    /// <summary>„Druck nur, wenn geändert nach …“.</summary>
    public DateTime? NurGeaendertNach { get; set; }
    public bool ErsteBeschriftungJedeSeite { get; set; } = true;
    public bool Raster { get; set; }
    public bool BeschraenkungSeitenbreite { get; set; } = true;
    public string Hintergrundbild { get; set; } = "";
    /// <summary>Eigene Überschrift (leer = Titel aus den Schuldaten).</summary>
    public string Ueberschrift { get; set; } = "";
    // Auswahl
    public bool AbwesendeNichtDrucken { get; set; }
    public bool AuchElementeOhneVertretung { get; set; }
    // Gliederung
    public bool UeberschriftJeElement { get; set; }
    public bool EineSeiteJeElement { get; set; }
    public bool ElementueberschriftKomprimiert { get; set; }
    /// <summary>Gruppierung (= Sortierkriterium 1): Element, Stunde, Vertreter, Fach.</summary>
    public string Gruppierung { get; set; } = "Element";
    // Form
    public bool EntfallslehrerDurchstreichen { get; set; } = true;
    public bool KombinierteKlassen { get; set; }
    public bool Stundenbezeichnung { get; set; }
    public bool StammdatenEinzeln { get; set; } = true;
    public bool BlockvertretungenZusammenfassen { get; set; } = true;
    public bool KeinEntfallBeiErsatz { get; set; }
    // Abwesend
    public bool LehrerMitAbsenzgrund { get; set; }
}

/// <summary>Feld (Spalte) im Seitenlayout eines Ausdrucks.</summary>
public sealed class DruckFeld
{
    public string Schluessel { get; set; } = "";
    /// <summary>Eigene Spaltenüberschrift (leer = Standard).</summary>
    public string Ueberschrift { get; set; } = "";
    public bool Drucken { get; set; } = true;
    /// <summary>Anteil an der Seitenbreite (relativ zu den übrigen gedruckten Feldern).</summary>
    public float Anteil { get; set; } = 0.1f;
}

public sealed class Tabellenformat
{
    public string Name { get; set; } = "";
    public List<Tabellenspalte> Spalten { get; set; } = new();
}

public sealed class Tabellenspalte
{
    public string Name { get; set; } = "";
    public bool Sichtbar { get; set; } = true;
    public int Breite { get; set; }
    public int Position { get; set; }
    /// <summary>Eigene Spaltenüberschrift (leer = Standard).</summary>
    public string Ueberschrift { get; set; } = "";
    /// <summary>0 = Standard, 1 = links, 2 = zentriert, 3 = rechts.</summary>
    public int Ausrichtung { get; set; }
    /// <summary>Spalte bleibt beim waagerechten Blättern stehen.</summary>
    public bool Fixiert { get; set; }
}

public sealed class Fensteranordnung
{
    public string Name { get; set; } = "";
    public bool Vertretungsmodus { get; set; }
    public List<FensterPosition> Fenster { get; set; } = new();
}

public sealed class FensterPosition
{
    public string Schluessel { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int B { get; set; }
    public int H { get; set; }
    public bool Maximiert { get; set; }
}
