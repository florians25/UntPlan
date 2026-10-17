using System.Runtime.CompilerServices;
using System.Text.Json;
using UntPlanWeb.Daten;

namespace UntPlanWeb.Dienste;

/// <summary>Stufe eines Rechts: Nein, Lesen (nur bei zweistufigen Rechten) oder Ja bzw. Schreiben.</summary>
public static class Stufe
{
    public const int Nein = 0, Lesen = 1, Ja = 2;
}

/// <summary>Eintrag im Rechte-Katalog. <see cref="Zweistufig"/>: Lesen/Schreiben statt Ja/Nein; <see cref="NurKollegium"/>: wirkt nur bei Konten des Kollegiums.</summary>
public sealed record RechtInfo(string Id, string Gruppe, string Name, string Beschreibung, bool Zweistufig, bool NurKollegium, int[] Standard);

/// <summary>
/// Feingranulare Rechte. Rollen sind Rechte-Profile; jedes Konto hat eine Basisrolle (admin, leitung, lehrer, schueler, eltern) und
/// optional ein eigenes Profil mit derselben Basisrolle. Die Standardwerte der Basisrollen entsprechen dem bisherigen Verhalten.
/// </summary>
public static class Recht
{
    public const string PlanEigen = "plan.eigen", PlanKlassen = "plan.klassen", PlanLehrer = "plan.lehrer", PlanRaeume = "plan.raeume", PlanTag = "plan.tag";
    /// <summary>Schüler- und Elternkonten: Stundenpläne anderer Klassen ansehen (Pläne der Lehrkräfte bleiben gesperrt).</summary>
    public const string PlanKlassenSchueler = "plan.klassen.schueler";
    public const string VertretungSchueler = "vertretung.schueler", VertretungLehrer = "vertretung.lehrer";
    public const string KlassenbuchEigene = "klassenbuch.eigene", KlassenbuchKlasse = "klassenbuch.klasse", KlassenbuchAlle = "klassenbuch.alle";
    public const string KlassenbuchEinstellungen = "klassenbuch.einstellungen";
    public const string AbwesenheitErfassen = "abwesenheit.erfassen", AbwesenheitEntschuldigen = "abwesenheit.entschuldigen",
        AbwesenheitRueckwirkend = "abwesenheit.rueckwirkend", AbwesenheitAlle = "abwesenheit.alle", AbwesenheitFachlehrer = "abwesenheit.fachlehrer";
    public const string HausaufgabenSehen = "hausaufgaben.sehen", HausaufgabenEintragen = "hausaufgaben.eintragen";
    public const string PruefungSehen = "pruefung.sehen", PruefungAnlegen = "pruefung.anlegen", PruefungSperrenUmgehen = "pruefung.sperren_umgehen";
    public const string BuchungRaum = "buchung.raum", BuchungRessourcen = "buchung.ressourcen", BuchungAndere = "buchung.andere", BuchungVerwalten = "buchung.verwalten";
    public const string MitteilungSenden = "mitteilung.senden", MitteilungAlle = "mitteilung.alle", MitteilungFamilie = "mitteilung.familie",
        NachrichtenPflegen = "nachrichten.pflegen", InfocenterVerwalten = "infocenter.verwalten";
    public const string SprechtagVerwalten = "sprechtag.verwalten";
    public const string BerichteSehen = "berichte.sehen";
    public const string SchuelerVerwalten = "schueler.verwalten", SitzplanKlasse = "sitzplan.klasse";
    public const string UnterrichtKlassen = "unterricht.klassen";
    public const string OffeneStundenAlle = "offenestunden.alle";
    public const string AdminBenutzer = "admin.benutzer", AdminStammdaten = "admin.stammdaten", AdminImport = "admin.import", AdminExport = "admin.export",
        AdminEinstellungen = "admin.einstellungen";
    public const string AdminWartung = "admin.wartung";

    /// <summary>Tage, die Abwesenheiten ohne das Recht „rückwirkend bearbeiten“ zurückliegen dürfen.</summary>
    public const int RueckwirkendTage = 7;

    // Standardwerte je Basisrolle: admin, leitung, lehrer, schueler, eltern
    private static int[] S(int a, int l, int le, int s, int e) => new[] { a, l, le, s, e };
    private const int N = Stufe.Nein, R = Stufe.Lesen, J = Stufe.Ja;

    public static readonly RechtInfo[] Katalog =
    {
        new(PlanEigen, "Stundenplan", "Eigener Stundenplan", "Mein Stundenplan (Lehrkraft, Klasse bzw. Kinder).", false, false, S(J, J, J, J, J)),
        new(PlanKlassen, "Stundenplan", "Pläne aller Klassen", "Stundenpläne aller Klassen ansehen.", false, true, S(J, J, J, N, N)),
        new(PlanKlassenSchueler, "Stundenplan", "Andere Klassenpläne (Schüler*innen, Eltern)", "Schüler- und Elternkonten dürfen die Stundenpläne anderer Klassen ansehen – Pläne der Lehrkräfte bleiben gesperrt.", false, false, S(N, N, N, N, N)),
        new(PlanLehrer, "Stundenplan", "Pläne der Lehrkräfte", "Stundenpläne aller Lehrkräfte ansehen.", false, true, S(J, J, J, N, N)),
        new(PlanRaeume, "Stundenplan", "Raumpläne", "Raumpläne ansehen.", false, false, S(J, J, J, J, J)),
        new(PlanTag, "Stundenplan", "Tagesübersichten", "Tagesübersicht über Klassen, Lehrkräfte und Räume.", false, true, S(J, J, J, N, N)),
        new(VertretungSchueler, "Vertretungsplan", "Vertretungsplan (Schülerversion)", "Vertretungsplan der eigenen Klasse.", false, false, S(J, J, J, J, J)),
        new(VertretungLehrer, "Vertretungsplan", "Vertretungsplan (Lehrerversion)", "Vollständiger Vertretungsplan mit Lehrkräften und Aufsichten.", false, true, S(J, J, J, N, N)),
        new(KlassenbuchEigene, "Klassenbuch", "Klassenbuch eigener Stunden", "Einträge in den eigenen Stunden (laut Plan) lesen bzw. schreiben.", true, true, S(J, J, J, N, N)),
        new(KlassenbuchKlasse, "Klassenbuch", "Klassenbuch der eigenen Klassen", "Als Klassenleitung: Klassenbuch, Dienste, Befreiungen und Schülerdaten der eigenen Klassen.", true, true, S(J, J, J, N, N)),
        new(KlassenbuchAlle, "Klassenbuch", "Klassenbuch aller Klassen", "Lesen: Klassenbuch aller Klassen einsehen. Schreiben: wie Schulleitung in allen Klassen eintragen.", true, true, S(J, J, N, N, N)),
        new(KlassenbuchEinstellungen, "Klassenbuch", "Klassenbuch-Einstellungen", "Einstellungen des Klassenbuchs (Hauptseite, Allgemein, Erinnerung an offene Stunden) und der Prüfungen ändern.", false, true, S(J, N, N, N, N)),
        new(AbwesenheitErfassen, "Abwesenheiten", "Abwesenheiten erfassen", "Abwesenheiten in den eigenen Stunden eintragen.", false, true, S(J, J, J, N, N)),
        new(AbwesenheitEntschuldigen, "Abwesenheiten", "Abwesenheiten entschuldigen", "Entschuldigungsstatus setzen (in den eigenen Klassen bzw. mit „alle Klassen“ überall).", false, true, S(J, J, J, N, N)),
        new(AbwesenheitRueckwirkend, "Abwesenheiten", "Rückwirkend bearbeiten", $"Abwesenheiten bearbeiten, die länger als {RueckwirkendTage} Tage zurückliegen.", false, true, S(J, J, J, N, N)),
        new(AbwesenheitFachlehrer, "Abwesenheiten", "Fehlstunden im eigenen Unterricht entschuldigen", "Fachlehrer-Entschuldigungen: einzelne Fehlstunden aus dem eigenen Unterricht entschuldigen (wenn in den Klassenbuch-Einstellungen eingeschaltet).", false, true, S(J, J, J, N, N)),
        new(AbwesenheitAlle, "Abwesenheiten", "Abwesenheiten aller Klassen", "Abwesenheiten und Fehlzeiten aller Klassen verwalten.", false, true, S(J, J, N, N, N)),
        new(HausaufgabenSehen, "Hausaufgaben", "Hausaufgaben sehen", "Hausaufgaben und Lehrstoff der Klasse sehen.", false, false, S(J, J, J, J, J)),
        new(HausaufgabenEintragen, "Hausaufgaben", "Hausaufgaben eintragen", "Hausaufgaben in den eigenen Stunden eintragen.", false, true, S(J, J, J, N, N)),
        new(PruefungSehen, "Prüfungen", "Prüfungen sehen", "Prüfungen und Prüfungskalender ansehen.", false, false, S(J, J, J, J, J)),
        new(PruefungAnlegen, "Prüfungen", "Prüfungen anlegen", "Prüfungen eintragen, verlegen und löschen.", false, true, S(J, J, J, N, N)),
        new(PruefungSperrenUmgehen, "Prüfungen", "Prüfungssperren umgehen", "Von den Prüfungssperren ausgenommen (wenn in den Prüfungssperren eingeschaltet).", false, true, S(J, J, N, N, N)),
        new(BuchungRaum, "Raumbuchung & Ressourcen", "Räume buchen", "Räume buchen und Raumänderungen melden.", false, true, S(J, J, J, N, N)),
        new(BuchungRessourcen, "Raumbuchung & Ressourcen", "Ressourcen buchen", "Ressourcen (z. B. Laptopwagen) buchen.", false, true, S(J, J, J, N, N)),
        new(BuchungAndere, "Raumbuchung & Ressourcen", "Buchungen anderer stornieren", "Buchungen und angelegten Unterricht anderer Konten stornieren bzw. löschen.", false, true, S(J, N, N, N, N)),
        new(BuchungVerwalten, "Raumbuchung & Ressourcen", "Räume und Ressourcen verwalten", "Ressourcen, Ressourcenarten und Raumdaten pflegen.", false, true, S(J, N, N, N, N)),
        new(MitteilungSenden, "Kommunikation", "Mitteilungen senden", "Mitteilungen schreiben (Schüler- und Elternkonten zusätzlich nach den Mitteilungs-Einstellungen).", false, false, S(J, J, J, J, J)),
        new(MitteilungAlle, "Kommunikation", "Mitteilungen an alle und an Gruppen", "Mitteilungen an alle Benutzer, ganze Rollen und alle Klassen senden.", false, true, S(J, J, N, N, N)),
        new(MitteilungFamilie, "Kommunikation", "Mitteilungen an Schüler*innen und Eltern", "Mitteilungen an Schüler- und Elternkonten senden (sonst nach den Mitteilungs-Einstellungen).", false, true, S(J, J, N, N, N)),
        new(NachrichtenPflegen, "Kommunikation", "Nachrichten zum Tag pflegen", "Nachrichten zum Tag anlegen, ändern und löschen.", false, true, S(J, J, N, N, N)),
        new(InfocenterVerwalten, "Kommunikation", "Infocenter verwalten", "Infos der Schule im Infocenter verwalten.", false, true, S(J, J, N, N, N)),
        new(SprechtagVerwalten, "Sprechtag", "Sprechtag verwalten", "Sprechtage anlegen, Lehrkräfte zuordnen, Termine aller Lehrkräfte verwalten.", false, true, S(J, N, N, N, N)),
        new(BerichteSehen, "Berichte", "Berichte", "Berichte und Auswertungen (Fehlzeiten, Klassenbuch) aufrufen.", false, true, S(J, J, J, N, N)),
        new(SchuelerVerwalten, "Schüler*innen", "Schülerverwaltung", "Schüler*innen aller Klassen anlegen, bearbeiten und löschen.", false, true, S(J, J, N, N, N)),
        new(SitzplanKlasse, "Schüler*innen", "Sitzplan der Klasse", "Sitzpläne der eigenen Klassen bzw. des eigenen Unterrichts bearbeiten.", false, true, S(J, J, J, N, N)),
        new(UnterrichtKlassen, "Schüler*innen", "Unterricht Klassen und Schülergruppen", "Lesen: Unterrichte je Klasse mit Schülergruppen ansehen. Schreiben: Schülergruppen bearbeiten (Abmeldung vom Unterricht) und befristete Unterrichtswechsel anlegen.", true, true, S(J, J, R, N, N)),
        new(OffeneStundenAlle, "Offene Stunden", "Offene Stunden aller Lehrkräfte", "Offene Stunden aller Lehrkräfte kontrollieren und Lehrkräfte erinnern.", false, true, S(J, J, N, N, N)),
        new(AdminBenutzer, "Administration", "Benutzerverwaltung", "Konten und Personen anlegen, ändern, sperren und löschen.", false, true, S(J, N, N, N, N)),
        new(AdminStammdaten, "Administration", "Stammdaten", "Stammdaten (Klassen, Lehrkräfte, Räume, Gründe, Dienste) pflegen.", false, true, S(J, N, N, N, N)),
        new(AdminImport, "Administration", "Import", "Benutzer und Schüler*innen aus CSV-Dateien importieren.", false, true, S(J, N, N, N, N)),
        new(AdminExport, "Administration", "Export", "Daten als CSV exportieren.", false, true, S(J, N, N, N, N)),
        new(AdminEinstellungen, "Administration", "Einstellungen", "Einstellungen der Administration ändern.", false, true, S(J, N, N, N, N)),
        new(AdminWartung, "Administration", "Systemwartung", "Aktive Benutzer ansehen, den Zugriff sperren bzw. freigeben (Wartungsmodus) und „Über UntPlan“ aufrufen; bei gesperrtem Zugriff weiterhin anmelden.", false, true, S(J, N, N, N, N)),
    };

    public static readonly Dictionary<string, RechtInfo> NachId = Katalog.ToDictionary(r => r.Id);

    /// <summary>Gruppen in Katalogreihenfolge.</summary>
    public static IEnumerable<string> Gruppen => Katalog.Select(r => r.Gruppe).Distinct();

    /// <summary>Standardstufe einer Basisrolle.</summary>
    public static int Standard(string rolle, string recht)
    {
        if (!NachId.TryGetValue(recht, out var r)) return Stufe.Nein;
        var i = Array.IndexOf(Rollen.Alle, rolle);
        return i < 0 ? Stufe.Nein : r.Standard[i];
    }

    /// <summary>Bisherige Zusatzrechte als Rechte des neuen Modells (abwärtskompatibel, wirken als zusätzliche Rechte).</summary>
    public static readonly Dictionary<string, (string Recht, int Stufe)[]> AusZusatzrechten = new()
    {
        [Zusatzrechte.Nachrichten] = new[] { (MitteilungAlle, Stufe.Ja), (MitteilungFamilie, Stufe.Ja), (NachrichtenPflegen, Stufe.Ja), (InfocenterVerwalten, Stufe.Ja) },
        [Zusatzrechte.Schuelerverwaltung] = new[] { (SchuelerVerwalten, Stufe.Ja) },
        [Zusatzrechte.KlassenbuchAlle] = new[] { (KlassenbuchAlle, Stufe.Lesen), (OffeneStundenAlle, Stufe.Ja) },
        [Zusatzrechte.OffeneStunden] = new[] { (OffeneStundenAlle, Stufe.Ja) },
    };

    /// <summary>Stufe begrenzen: Ja/Nein-Rechte kennen kein „Lesen“.</summary>
    public static int Begrenzen(string recht, int stufe)
    {
        stufe = Math.Clamp(stufe, Stufe.Nein, Stufe.Ja);
        return NachId.TryGetValue(recht, out var r) && !r.Zweistufig && stufe == Stufe.Lesen ? Stufe.Ja : stufe;
    }
}

/// <summary>Rechte-Profil (Rolle): Basisrolle, Stufe je Recht und das Planfenster (Wochen zurück bzw. voraus, null = unbegrenzt).</summary>
public sealed record Rechteprofil(string Id, string Name, string Basis, string Beschreibung, bool System, Dictionary<string, int> Rechte, int Benutzer = 0)
{
    public int? PlanWochenZurueck { get; init; }
    public int? PlanWochenVor { get; init; }
    public Planfenster Planfenster => new(PlanWochenZurueck, PlanWochenVor);
}

/// <summary>
/// Planfenster einer Rolle: wie viele Kalenderwochen (Wochenbeginn Montag) vor bzw. nach der aktuellen Woche der Stundenplan angezeigt wird.
/// null = unbegrenzt (Standard), 0 = keine Woche in diese Richtung (nur die aktuelle Woche), n = n Wochen.
/// </summary>
public readonly record struct Planfenster(int? Zurueck, int? Vor)
{
    /// <summary>Größter einstellbarer Wert (zehn Jahre).</summary>
    public const int Hoechstens = 520;
    public static readonly Planfenster Unbegrenzt = new(null, null);

    /// <summary>Hinweis, wenn eine Woche außerhalb des Fensters liegt (Web, Apps).</summary>
    public const string Sperrhinweis = "Die Anzeige des Stundenplans ist durch Ihren Administrator beschränkt";

    public bool IstUnbegrenzt => Zurueck is null && Vor is null;

    /// <summary>Liegt die Woche des Datums im Fenster um die Woche von <paramref name="heute"/>?</summary>
    public bool Erlaubt(DateOnly datum, DateOnly heute)
    {
        if (IstUnbegrenzt) return true;
        var wochen = (Plandienst.Montag(datum).DayNumber - Plandienst.Montag(heute).DayNumber) / 7;
        return (Zurueck is not int z || wochen >= -z) && (Vor is not int v || wochen <= v);
    }

    /// <summary>Eingabe prüfen: leer = unbegrenzt, sonst 0–<see cref="Hoechstens"/>.</summary>
    public static int? Pruefen(int? wochen, string richtung) => wochen switch
    {
        null => null,
        < 0 or > Hoechstens => throw new ArgumentException($"Bitte für „Wochen {richtung}“ eine Zahl von 0 bis {Hoechstens} eingeben oder das Feld leer lassen (unbegrenzt)."),
        _ => wochen,
    };
}

/// <summary>Effektives Recht eines Kontos mit Herkunft (Vorschau „effektive Rechte“).</summary>
public sealed record EffektivesRecht(string Recht, int Profil, int? Ueberschreibung, int Zusatz, int Effektiv, string Quelle);

/// <summary>
/// Rechte-Profile, Überschreibungen je Konto und die zentrale Prüffunktion <see cref="Darf(Benutzer, string, int)"/>.
/// Effektive Rechte = Profil (bzw. Standardprofil der Basisrolle) + bisherige Zusatzrechte, danach Überschreibungen des Kontos (gewähren oder entziehen).
/// Administration hat immer alle Rechte; Rechte mit „nur Kollegium“ wirken bei Schüler- und Elternkonten nicht.
/// </summary>
public static class Rechtedienst
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Zwischenspeicher
    {
        public Dictionary<string, Rechteprofil>? Profile;
        public DateTime Geladen;
    }

    private static readonly ConditionalWeakTable<Db, Zwischenspeicher> Speicher = new();

    // ---------------------------------------------------------------- zentrale Prüfung

    /// <summary>Darf das Konto dieses Recht mindestens in der angegebenen Stufe (Standard: Ja bzw. Schreiben)?</summary>
    public static bool Darf(Benutzer b, string recht, int mindestens = Stufe.Ja) => StufeVon(b, recht) >= mindestens;

    /// <summary>Darf das Konto dieses Recht mindestens lesen?</summary>
    public static bool DarfLesen(Benutzer b, string recht) => Darf(b, recht, Stufe.Lesen);

    /// <summary>Effektive Stufe; ohne berechnete Rechte (z. B. in Tests erzeugte Konten) gelten Standardprofil und Zusatzrechte der Basisrolle.</summary>
    public static int StufeVon(Benutzer b, string recht)
    {
        if (b.Rolle == Rollen.Admin) return Stufe.Ja;
        if (b.Rechte != null) return b.Rechte.TryGetValue(recht, out var s) ? s : Stufe.Nein;
        return Berechnen(b, null, null).TryGetValue(recht, out var t) ? t : Stufe.Nein;
    }

    /// <summary>Rechte mit Stufe &gt; Nein (für die Oberfläche).</summary>
    public static Dictionary<string, int> Liste(Benutzer b) =>
        Recht.Katalog.Select(r => (r.Id, S: StufeVon(b, r.Id))).Where(x => x.S > Stufe.Nein).ToDictionary(x => x.Id, x => x.S);

    /// <summary>Effektive Rechte eines Kontos aus Profil, Zusatzrechten und Überschreibungen.</summary>
    public static Dictionary<string, int> Berechnen(Benutzer b, Rechteprofil? profil, IReadOnlyDictionary<string, int>? ueber) =>
        Vorschau(b, profil, ueber).ToDictionary(x => x.Recht, x => x.Effektiv);

    public static List<EffektivesRecht> Vorschau(Benutzer b, Rechteprofil? profil, IReadOnlyDictionary<string, int>? ueber)
    {
        var zusatz = new Dictionary<string, int>();
        foreach (var z in b.Zusatzrechte)
            if (Recht.AusZusatzrechten.TryGetValue(z, out var liste))
                foreach (var (r, s) in liste) zusatz[r] = Math.Max(zusatz.GetValueOrDefault(r), s);
        var res = new List<EffektivesRecht>();
        foreach (var info in Recht.Katalog)
        {
            var p = profil != null && profil.Basis == b.Rolle && profil.Rechte.TryGetValue(info.Id, out var ps) ? ps : Recht.Standard(b.Rolle, info.Id);
            var zs = b.Kollegium ? zusatz.GetValueOrDefault(info.Id) : Stufe.Nein;
            int? u = ueber != null && ueber.TryGetValue(info.Id, out var us) ? us : null;
            int eff; string quelle;
            if (b.Rolle == Rollen.Admin) { eff = Stufe.Ja; quelle = "Administration"; }
            else if (info.NurKollegium && !b.Kollegium) { eff = Stufe.Nein; quelle = "nur Kollegium"; }
            else if (u is int uu) { eff = uu; quelle = uu > Math.Max(p, zs) ? "gewährt" : uu < Math.Max(p, zs) ? "entzogen" : "Überschreibung"; }
            else if (zs > p) { eff = zs; quelle = "Zusatzrecht"; }
            else { eff = p; quelle = "Profil"; }
            res.Add(new EffektivesRecht(info.Id, p, u, zs, eff, quelle));
        }
        return res;
    }

    // ---------------------------------------------------------------- Laden (Benutzerdienst)

    /// <summary>Effektive Rechte eines Kontos aus der Datenbank berechnen.</summary>
    public static Dictionary<string, int>? Laden(Db db, Benutzer b)
    {
        try
        {
            var profilId = db.Wert("SELECT rechteprofil FROM benutzer WHERE id = @id", Db.P("@id", b.Id)) as string ?? "";
            var ueber = Ueberschreibungen(db, b.Id);
            return Berechnen(b, Profil(db, profilId.Length > 0 ? profilId : b.Rolle), ueber);
        }
        catch (System.Data.Common.DbException) { return null; }
    }

    /// <summary>Effektive Rechte vieler Konten (Benutzerliste) mit wenigen Abfragen.</summary>
    public static List<Benutzer> Laden(Db db, List<Benutzer> liste)
    {
        if (liste.Count == 0) return liste;
        try
        {
            var profile = db.Liste("SELECT id, rechteprofil FROM benutzer", r => (Id: r.Zahl("id"), P: r.Text("rechteprofil"))).ToDictionary(x => x.Id, x => x.P);
            var ueber = db.Liste("SELECT benutzer_id, recht, stufe FROM benutzer_rechte", r => (B: r.Zahl("benutzer_id"), R: r.Text("recht"), S: r.Zahl("stufe")))
                .GroupBy(x => x.B).ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.ToDictionary(x => x.R, x => x.S));
            return liste.Select(b =>
            {
                var profil = Profil(db, profile.GetValueOrDefault(b.Id) is { Length: > 0 } p ? p : b.Rolle);
                return b with { Rechte = Berechnen(b, profil, ueber.GetValueOrDefault(b.Id)), Planfenster = FensterAus(b, profil) };
            }).ToList();
        }
        catch (System.Data.Common.DbException) { return liste; }
    }

    // ---------------------------------------------------------------- Planfenster

    /// <summary>Planfenster eines Kontos aus seinem Profil (bzw. der Systemrolle seiner Basisrolle); Administration immer unbegrenzt.</summary>
    public static Planfenster PlanfensterLaden(Db db, Benutzer b)
    {
        if (b.IstAdmin) return Planfenster.Unbegrenzt;
        try
        {
            var profilId = ProfilVon(db, b.Id);
            return FensterAus(b, Profil(db, profilId.Length > 0 ? profilId : b.Rolle));
        }
        catch (System.Data.Common.DbException) { return Planfenster.Unbegrenzt; }
    }

    // wie bei den Rechten: passt das Profil nicht zur Basisrolle, gilt der Standard (unbegrenzt)
    private static Planfenster FensterAus(Benutzer b, Rechteprofil? profil) =>
        !b.IstAdmin && profil != null && profil.Basis == b.Rolle ? profil.Planfenster : Planfenster.Unbegrenzt;

    /// <summary>Darf das Konto die Woche dieses Datums im Stundenplan sehen?</summary>
    public static bool PlanSichtbar(Benutzer b, DateOnly datum, DateOnly heute) => b.IstAdmin || b.Planfenster.Erlaubt(datum, heute);

    // ---------------------------------------------------------------- Profile

    private static Dictionary<string, Rechteprofil> Profile(Db db)
    {
        var z = Speicher.GetOrCreateValue(db);
        lock (z)
        {
            if (z.Profile != null && DateTime.UtcNow - z.Geladen < TimeSpan.FromSeconds(30)) return z.Profile;
            var gespeichert = db.Liste("SELECT id, name, basis, beschreibung, rechte, plan_wochen_zurueck, plan_wochen_vor FROM rechteprofile",
                r => (Id: r.Text("id"), Name: r.Text("name"), Basis: r.Text("basis"), Beschreibung: r.Text("beschreibung"), Rechte: r.Text("rechte"),
                    Zurueck: r.ZahlOderNull("plan_wochen_zurueck"), Vor: r.ZahlOderNull("plan_wochen_vor")));
            var res = new Dictionary<string, Rechteprofil>(StringComparer.OrdinalIgnoreCase);
            foreach (var rolle in Rollen.Alle)
            {
                var g = gespeichert.FirstOrDefault(x => x.Id == rolle);
                // Administration: immer alle Rechte und unbegrenztes Planfenster
                res[rolle] = new Rechteprofil(rolle, Rollen.Name(rolle), rolle, g.Id is null ? "" : g.Beschreibung, true, Aufloesen(rolle, rolle == Rollen.Admin ? null : g.Rechte))
                    { PlanWochenZurueck = rolle == Rollen.Admin ? null : g.Zurueck, PlanWochenVor = rolle == Rollen.Admin ? null : g.Vor };
            }
            foreach (var g in gespeichert.Where(x => !Rollen.Gueltig(x.Id) && Rollen.Gueltig(x.Basis)))
                res[g.Id] = new Rechteprofil(g.Id, g.Name, g.Basis, g.Beschreibung, false, Aufloesen(g.Basis, g.Rechte)) { PlanWochenZurueck = g.Zurueck, PlanWochenVor = g.Vor };
            z.Profile = res;
            z.Geladen = DateTime.UtcNow;
            return res;
        }
    }

    /// <summary>Gespeicherte Werte über den Standardwerten der Basisrolle (neue Rechte erhalten so automatisch ihren Standard).</summary>
    private static Dictionary<string, int> Aufloesen(string basis, string? json)
    {
        var res = Recht.Katalog.ToDictionary(r => r.Id, r => basis == Rollen.Admin ? Stufe.Ja : Recht.Standard(basis, r.Id));
        if (string.IsNullOrEmpty(json)) return res;
        try
        {
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, int>>(json, Json) ?? new())
                if (res.ContainsKey(k)) res[k] = Recht.Begrenzen(k, v);
        }
        catch (JsonException) { /* Standardwerte */ }
        return res;
    }

    private static void Vergessen(Db db)
    {
        if (Speicher.TryGetValue(db, out var z)) lock (z) z.Profile = null;
    }

    public static Rechteprofil? Profil(Db db, string id) => Profile(db).TryGetValue(id, out var p) ? p : null;

    /// <summary>Alle Profile (Systemrollen zuerst) mit Anzahl der Konten.</summary>
    public static List<Rechteprofil> ProfilListe(Db db)
    {
        var konten = db.Liste("SELECT rolle, rechteprofil FROM benutzer", r => (Rolle: r.Text("rolle"), P: r.Text("rechteprofil")));
        int Anzahl(Rechteprofil p) => p.System
            ? konten.Count(k => k.Rolle == p.Id && (k.P.Length == 0 || Profil(db, k.P) is not { } x || x.Basis != k.Rolle))
            : konten.Count(k => k.P.Equals(p.Id, StringComparison.OrdinalIgnoreCase) && k.Rolle == p.Basis);
        return Profile(db).Values.OrderBy(p => p.System ? Array.IndexOf(Rollen.Alle, p.Id) : 100).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => p with { Benutzer = Anzahl(p) }).ToList();
    }

    private static string RechteJson(Dictionary<string, int>? rechte, string basis)
    {
        var aufgeloest = Aufloesen(basis, null);
        foreach (var (k, v) in rechte ?? new())
            if (aufgeloest.ContainsKey(k)) aufgeloest[k] = Recht.Begrenzen(k, v);
        return JsonSerializer.Serialize(aufgeloest, Json);
    }

    /// <summary>
    /// Profil anlegen (<paramref name="id"/> = null) bzw. ändern. Systemrollen: nur Rechte, Beschreibung und Planfenster; Administration ist nicht änderbar.
    /// Planfenster: Wochen zurück bzw. voraus (null = unbegrenzt, 0 = nur die aktuelle Woche).
    /// </summary>
    public static Rechteprofil Speichern(Db db, string? id, string? name, string? basis, string? beschreibung, Dictionary<string, int>? rechte, string jetzt,
        int? planZurueck = null, int? planVor = null)
    {
        planZurueck = Planfenster.Pruefen(planZurueck, "zurück");
        planVor = Planfenster.Pruefen(planVor, "voraus");
        beschreibung = (beschreibung ?? "").Trim();
        if (beschreibung.Length > 500) throw new ArgumentException("Die Beschreibung ist zu lang (höchstens 500 Zeichen).");
        if (id is null)
        {
            name = (name ?? "").Trim();
            if (name.Length is < 2 or > 100) throw new ArgumentException("Bitte einen Namen mit 2–100 Zeichen eingeben.");
            if (!Rollen.Gueltig(basis ?? "") || basis == Rollen.Admin) throw new ArgumentException("Bitte eine Basisrolle wählen (Schulleitung, Lehrkraft, Schüler*in oder Erziehungsberechtigte).");
            if (Profile(db).Values.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"Die Rolle „{name}“ gibt es bereits.");
            var slug = Sicherheit.Slug(name);
            if (slug.Length == 0) slug = "rolle";
            var neu = "p-" + slug;
            for (var i = 2; Profil(db, neu) != null; i++) neu = $"p-{slug}-{i}";
            db.Ausfuehren("INSERT INTO rechteprofile (id, name, basis, beschreibung, rechte, geaendert, plan_wochen_zurueck, plan_wochen_vor) VALUES (@i, @n, @b, @d, @r, @g, @pz, @pv)",
                Db.P("@i", neu), Db.P("@n", name), Db.P("@b", basis), Db.P("@d", beschreibung), Db.P("@r", RechteJson(rechte, basis!)), Db.P("@g", jetzt),
                Db.P("@pz", planZurueck), Db.P("@pv", planVor));
            Vergessen(db);
            return Profil(db, neu)!;
        }
        var p = Profil(db, id) ?? throw new ArgumentException("Unbekannte Rolle.");
        if (p.Id == Rollen.Admin) throw new ArgumentException("Die Administration hat immer alle Rechte.");
        var json = RechteJson(rechte ?? p.Rechte, p.Basis);
        if (p.System)
        {
            db.Ausfuehren("DELETE FROM rechteprofile WHERE id = @i", Db.P("@i", p.Id));
            db.Ausfuehren("INSERT INTO rechteprofile (id, name, basis, beschreibung, rechte, geaendert, plan_wochen_zurueck, plan_wochen_vor) VALUES (@i, @n, @b, @d, @r, @g, @pz, @pv)",
                Db.P("@i", p.Id), Db.P("@n", p.Name), Db.P("@b", p.Basis), Db.P("@d", beschreibung), Db.P("@r", json), Db.P("@g", jetzt),
                Db.P("@pz", planZurueck), Db.P("@pv", planVor));
        }
        else
        {
            name = string.IsNullOrWhiteSpace(name) ? p.Name : name.Trim();
            if (name.Length is < 2 or > 100) throw new ArgumentException("Bitte einen Namen mit 2–100 Zeichen eingeben.");
            if (Profile(db).Values.Any(x => x.Id != p.Id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"Die Rolle „{name}“ gibt es bereits.");
            db.Ausfuehren("UPDATE rechteprofile SET name = @n, beschreibung = @d, rechte = @r, geaendert = @g, plan_wochen_zurueck = @pz, plan_wochen_vor = @pv WHERE id = @i",
                Db.P("@i", p.Id), Db.P("@n", name), Db.P("@d", beschreibung), Db.P("@r", json), Db.P("@g", jetzt), Db.P("@pz", planZurueck), Db.P("@pv", planVor));
        }
        Vergessen(db);
        return Profil(db, p.Id)!;
    }

    /// <summary>Systemrolle auf die Standardrechte zurücksetzen.</summary>
    public static void Zuruecksetzen(Db db, string id)
    {
        if (!Rollen.Gueltig(id)) throw new ArgumentException("Nur Systemrollen lassen sich zurücksetzen.");
        db.Ausfuehren("DELETE FROM rechteprofile WHERE id = @i", Db.P("@i", id));
        Vergessen(db);
    }

    /// <summary>Profil kopieren (auch Systemrollen; die Kopie ist eine eigene Rolle mit derselben Basisrolle).</summary>
    public static Rechteprofil Kopieren(Db db, string id, string? name, string jetzt)
    {
        var p = Profil(db, id) ?? throw new ArgumentException("Unbekannte Rolle.");
        if (p.Id == Rollen.Admin) throw new ArgumentException("Die Administration kann nicht kopiert werden.");
        var neu = string.IsNullOrWhiteSpace(name) ? p.Name + " (Kopie)" : name;
        for (var i = 2; Profile(db).Values.Any(x => x.Name.Equals(neu, StringComparison.OrdinalIgnoreCase)); i++) neu = $"{p.Name} (Kopie {i})";
        return Speichern(db, null, neu, p.Basis, p.Beschreibung, p.Rechte, jetzt, p.PlanWochenZurueck, p.PlanWochenVor);
    }

    /// <summary>Eigene Rolle löschen; deren Konten erhalten wieder das Standardprofil ihrer Basisrolle.</summary>
    public static void Loeschen(Db db, string id)
    {
        var p = Profil(db, id) ?? throw new ArgumentException("Unbekannte Rolle.");
        if (p.System) throw new ArgumentException("Systemrollen können nicht gelöscht werden.");
        db.Ausfuehren("UPDATE benutzer SET rechteprofil = '' WHERE rechteprofil = @i", Db.P("@i", p.Id));
        db.Ausfuehren("DELETE FROM rechteprofile WHERE id = @i", Db.P("@i", p.Id));
        Vergessen(db);
    }

    // ---------------------------------------------------------------- Konten

    public static string ProfilVon(Db db, int benutzerId) => db.Wert("SELECT rechteprofil FROM benutzer WHERE id = @id", Db.P("@id", benutzerId)) as string ?? "";

    public static Dictionary<string, int> Ueberschreibungen(Db db, int benutzerId) =>
        db.Liste("SELECT recht, stufe FROM benutzer_rechte WHERE benutzer_id = @b", r => (R: r.Text("recht"), S: r.Zahl("stufe")), Db.P("@b", benutzerId))
            .Where(x => Recht.NachId.ContainsKey(x.R)).ToDictionary(x => x.R, x => x.S);

    /// <summary>Profil eines Kontos setzen ("" = Standardprofil der Basisrolle). Das Profil muss zur Basisrolle passen.</summary>
    public static void ProfilSetzen(Db db, Benutzer b, string? profil)
    {
        profil = (profil ?? "").Trim();
        if (profil.Length > 0 && !Rollen.Gueltig(profil))
        {
            var p = Profil(db, profil) ?? throw new ArgumentException("Unbekannte Rolle.");
            if (p.Basis != b.Rolle) throw new ArgumentException($"Die Rolle „{p.Name}“ gehört zur Basisrolle {Rollen.Name(p.Basis)}; das Konto hat die Basisrolle {Rollen.Name(b.Rolle)}.");
            profil = p.Id;
        }
        else profil = "";
        db.Ausfuehren("UPDATE benutzer SET rechteprofil = @p WHERE id = @id", Db.P("@p", profil), Db.P("@id", b.Id));
    }

    /// <summary>Überschreibungen eines Kontos ersetzen (Recht → Stufe; null-Werte bzw. fehlende Rechte = keine Überschreibung).</summary>
    public static void UeberschreibungenSetzen(Db db, int benutzerId, Dictionary<string, int?>? werte)
    {
        db.Transaktion(c =>
        {
            Db.Ausfuehren(c, "DELETE FROM benutzer_rechte WHERE benutzer_id = @b", Db.P("@b", benutzerId));
            foreach (var (k, v) in werte ?? new())
                if (v is int s && Recht.NachId.ContainsKey(k))
                    Db.Ausfuehren(c, "INSERT INTO benutzer_rechte (benutzer_id, recht, stufe) VALUES (@b, @r, @s)", Db.P("@b", benutzerId), Db.P("@r", k), Db.P("@s", Recht.Begrenzen(k, s)));
        });
    }

    /// <summary>Vorschau der effektiven Rechte eines Kontos (mit optional geänderten, noch nicht gespeicherten Angaben).</summary>
    public static List<EffektivesRecht> Vorschau(Db db, Benutzer b, string? profil = null, Dictionary<string, int?>? ueber = null)
    {
        var pid = profil ?? ProfilVon(db, b.Id);
        var u = ueber is null ? Ueberschreibungen(db, b.Id) : ueber.Where(x => x.Value is int && Recht.NachId.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => Recht.Begrenzen(x.Key, x.Value!.Value));
        return Vorschau(b, Profil(db, pid.Length > 0 ? pid : b.Rolle), u);
    }
}

/// <summary>Hilfen für Endpunkte: Recht verlangen, Administrationskonten schützen.</summary>
public static class RechtePruefung
{
    /// <summary>Wirft 403, wenn das Konto das Recht nicht (in der Stufe) hat.</summary>
    public static Benutzer Verlangen(Benutzer b, string recht, int mindestens = Stufe.Ja, string? meldung = null) =>
        Rechtedienst.Darf(b, recht, mindestens) ? b : throw new UnauthorizedAccessException(meldung ?? $"Dafür fehlt Ihrem Konto das Recht „{(Recht.NachId.TryGetValue(recht, out var r) ? r.Name : recht)}“.");

    /// <summary>Konten mit Benutzerverwaltungsrecht, aber ohne Administration, dürfen Administrationskonten weder anlegen noch ändern.</summary>
    public static void AdminSchuetzen(Benutzer ich, Benutzer? ziel, string? neueRolle = null)
    {
        if (ich.IstAdmin) return;
        if (ziel?.IstAdmin == true || neueRolle == Rollen.Admin) throw new UnauthorizedAccessException("Administrationskonten verwaltet nur die Administration.");
    }
}
