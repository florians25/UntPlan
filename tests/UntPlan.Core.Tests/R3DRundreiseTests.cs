using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UntPlan.Core;
using UntPlan.Core.Model;
using Xunit;

namespace UntPlan.Core.Tests;

/// <summary>
/// R3D – Speichern/Laden: Jede Eigenschaft aller Modelle der Schuldatei (auch die Felder der Runden 1–3) übersteht die Rundreise durch
/// die Datei; ältere Dateien ohne die neuen Felder laden ohne Fehler und erhalten die Standardwerte. Die Prüfung arbeitet per Reflexion,
/// neue Felder werden also automatisch mitgeprüft.
/// </summary>
public class R3DRundreiseTests
{
    [Fact]
    public void JedeEigenschaftUebersteht_SpeichernUndLaden()
    {
        var abweichungen = new List<string>();
        var nichtGefuellt = new List<string>();
        foreach (var t in Rundreise.Modelltypen(typeof(Schuldatei)))
        {
            var o = Activator.CreateInstance(t)!;
            new Rundreise.Fueller().Fuellen(o, 0);
            // jede gespeicherte Eigenschaft weicht nach dem Füllen vom Standardwert ab (sonst prüft die Rundreise nichts)
            var standard = Activator.CreateInstance(t)!;
            foreach (var p in Rundreise.Gespeichert(t).Where(p => !Rundreise.Altformat(p) && (p.SetMethod != null || typeof(IEnumerable).IsAssignableFrom(p.PropertyType))))
            {
                var diff = new List<string>();
                Rundreise.Vergleichen(p.GetValue(o), p.GetValue(standard), p.Name, diff);
                if (diff.Count == 0) nichtGefuellt.Add($"{t.Name}.{p.Name}");
            }
            var json = JsonSerializer.Serialize(o, t, Dokument.JsonOptionen);
            var geladen = JsonSerializer.Deserialize(json, t, Dokument.JsonOptionen)!;
            Rundreise.Vergleichen(o, geladen, t.Name, abweichungen);
            // zweite Rundreise: gleiche Datei
            Assert.Equal(json, JsonSerializer.Serialize(geladen, t, Dokument.JsonOptionen));
        }
        Assert.True(abweichungen.Count == 0, "Beim Speichern/Laden verloren oder verändert:\n" + string.Join("\n", abweichungen.Take(60)));
        Assert.True(nichtGefuellt.Count == 0, "Eigenschaften ohne Testwert (Rundreise nicht geprüft):\n" + string.Join("\n", nichtGefuellt));
    }

    [Fact]
    public void GefuellteSchuldatei_UebstehtDokumentSpeichernUndLaden()
    {
        var d = new Schuldatei();
        new Rundreise.Fueller().Fuellen(d, 0);
        d.Version = Schuldatei.AktuelleVersion;
        var pfad = Path.Combine(Path.GetTempPath(), $"r3d_{Guid.NewGuid():N}{Dokument.Dateiendung}");
        try
        {
            new Dokument(d).Speichern(pfad);
            var geladen = Dokument.Laden(pfad).Daten;
            var abweichungen = new List<string>();
            Rundreise.Vergleichen(d, geladen, "Schuldatei", abweichungen);
            // beim Laden werden nur Raumvertretungen älterer Dateien abgeglichen – sonst bleibt alles gleich
            abweichungen.RemoveAll(a => a.StartsWith("Schuldatei.Vertretungen", StringComparison.Ordinal));
            Assert.True(abweichungen.Count == 0, string.Join("\n", abweichungen.Take(40)));
        }
        finally
        {
            foreach (var f in new[] { pfad, pfad + ".bak", pfad + ".tmp" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    [Fact]
    public void BeispielschuleMitAllenNeuenFeldern_UebstehtSpeichernUndLaden()
    {
        var d = Demo.Beispielschule();
        // die Beispielschule mit Werten in allen Feldern, die noch Standardwerte haben (Felder der Runden 1–3)
        var fueller = new Rundreise.Fueller();
        void Ergaenzen(object o, int tiefe)
        {
            if (tiefe > 6) return;
            var standard = Activator.CreateInstance(o.GetType())!;
            foreach (var p in Rundreise.Gespeichert(o.GetType()).Where(p => !Rundreise.Altformat(p)))
            {
                var wert = p.GetValue(o);
                if (wert is not null && !Rundreise.IstEinfach(p.PropertyType) && wert is not JsonNode)
                {
                    if (wert is IDictionary dict) { foreach (var v in dict.Values.Cast<object?>().Where(v => v != null && !Rundreise.IstEinfach(v.GetType())).ToList()) Ergaenzen(v!, tiefe + 1); }
                    else if (wert is IEnumerable liste) { foreach (var x in liste.Cast<object?>().Where(x => x != null && !Rundreise.IstEinfach(x.GetType())).Take(3).ToList()) Ergaenzen(x!, tiefe + 1); }
                    else if (o.GetType().Namespace == wert.GetType().Namespace) Ergaenzen(wert, tiefe + 1);
                    continue;
                }
                if (p.SetMethod is not { IsPublic: true } || !Rundreise.IstEinfach(p.PropertyType) || !Equals(wert, p.GetValue(standard))) continue;
                // Verweise (…Id) und Zahlenbereiche nicht verfälschen: nur Texte und Schalter ergänzen
                var u = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                if (u == typeof(string) || u == typeof(bool)) p.SetValue(o, fueller.Einfach(p.PropertyType, wert));
            }
        }
        Ergaenzen(d, 0);
        var pfad = Path.Combine(Path.GetTempPath(), $"r3d_{Guid.NewGuid():N}{Dokument.Dateiendung}");
        try
        {
            new Dokument(d).Speichern(pfad);
            var geladen = Dokument.Laden(pfad).Daten;
            var abweichungen = new List<string>();
            Rundreise.Vergleichen(d, geladen, "Schuldatei", abweichungen);
            Assert.True(abweichungen.Count == 0, string.Join("\n", abweichungen.Take(40)));
        }
        finally
        {
            foreach (var f in new[] { pfad, pfad + ".bak", pfad + ".tmp" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    /// <summary>
    /// Kopien der Modelle (Entwürfe in Absenzen, Ferien, Vertretungs-Probeberechnungen …) übernehmen jedes gespeicherte Feld und haben
    /// eigene Listen – sonst geht beim Speichern eines Entwurfs ein Feld verloren.
    /// </summary>
    [Fact]
    public void KopienDerModelle_SindVollstaendigUndUnabhaengig()
    {
        var probleme = new List<string>();
        var typen = typeof(Schuldatei).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null && t.GetMethod("Kopie", Type.EmptyTypes) is { } m && m.ReturnType == t)
            .ToList();
        Assert.Contains(typeof(Abwesenheit), typen);
        Assert.Contains(typeof(Ferien), typen);
        foreach (var t in typen)
        {
            var o = Activator.CreateInstance(t)!;
            new Rundreise.Fueller().Fuellen(o, 0);
            var k = t.GetMethod("Kopie", Type.EmptyTypes)!.Invoke(o, null)!;
            Rundreise.Vergleichen(o, k, t.Name, probleme);
            foreach (var p in Rundreise.Gespeichert(t).Where(p => p.SetMethod != null && !p.PropertyType.IsValueType && p.PropertyType != typeof(string)))
                if (p.GetValue(o) is IList liste && ReferenceEquals(liste, p.GetValue(k))) probleme.Add($"{t.Name}.{p.Name}: Kopie teilt die Liste mit dem Original");
        }
        Assert.True(probleme.Count == 0, string.Join("\n", probleme));
    }

    /// <summary>Jedes Modell lädt aus einem leeren JSON-Objekt (Datei ohne neue Felder) ohne Fehler, Listen und Objekte sind nicht null.</summary>
    [Fact]
    public void ModelleOhneFelder_LadenMitStandardwerten()
    {
        var probleme = new List<string>();
        var nullInfo = new NullabilityInfoContext();
        foreach (var t in Rundreise.Modelltypen(typeof(Schuldatei)))
        {
            var o = JsonSerializer.Deserialize("{}", t, Dokument.JsonOptionen)!;
            foreach (var p in Rundreise.Gespeichert(t).Where(p => !p.PropertyType.IsValueType && !Rundreise.Altformat(p)))
                if (p.GetValue(o) is null && nullInfo.Create(p).ReadState == NullabilityState.NotNull)
                    probleme.Add($"{t.Name}.{p.Name} ist null");
        }
        Assert.True(probleme.Count == 0, string.Join("\n", probleme));
    }

    /// <summary>
    /// Eigenschaften, die mit den Runden 1–3 neu in die Modelle kamen. Eine ältere Datei kennt sie nicht; sie wird hier nachgebildet,
    /// indem diese Felder aus einer vollständigen Datei entfernt werden.
    /// </summary>
    private static readonly string[] NeueFelder =
    {
        "AbwesendeInformieren", "AnzahlSeiten", "AnzahlWochen", "AutomatischeFarbe", "Bandmodus", "BedingtFixieren", "BelegungBeibehalten", "Berichte", "Betreff",
        "Bezug", "BildBreite", "BildX", "BildY", "BodyEigenschaften", "DatumDrucken", "Dislozierung", "DoppelstundeUeberPause", "DoppelstundenEinhalten", "Druck",
        "EinHalbtag", "EntfaelleMelden", "EntfaelleNichtAnzeigen", "ExtName", "FachfolgeKlassen", "FachfolgeLehrer", "FensterImHintergrund", "Fett", "Freifach",
        "FussDateiname", "FussDatum", "FussKalenderwoche", "FussLinie", "FussPeriode", "FussSchulwoche", "FussWochennummer", "Fusszeile", "GeaendertAusrufezeichen",
        "GeaendertFett", "GeaendertKursiv", "GeaendertRot", "GeaendertUnterstreichen", "Geschuetzt", "HauptklasseId", "Hintergrundbild", "HohlstundenZulaessig", "Html",
        "ImMenu", "ImMenue", "ImmerSelbeZeit", "JeElementNeueSeite", "JedeWocheEinPlan", "KeinAusweichraum", "KeineDatenanalyse", "KeineEinzelstunden",
        "KlassengruppeSpaeter", "Klassenkollision", "Klassenzeitraster", "Kursiv", "Langnamen", "LegendeSchriftProzent", "LehrerDruckname", "LehrerFix",
        "LupeAusgeblendet", "LupeLegendeAlias", "LupeSchriftProzent", "ManuellPlanen", "Markiert", "MaxStundenFolge", "MaxVerschiedeneProTag", "MaxVerweildauer",
        "MehrereBloecke", "NachmittagNurDoppel", "Nachmittagsstunden", "Nachrichten", "NeueZeile", "NichtAnzeigenWennZaehlerGroesser", "NichtDrucken",
        "NichtErsteUndLetzte", "NichtInLegende", "NichtInRandstunde", "NichtInRandstunden", "NurEinzeltage", "NurGanzeTage", "OptimierungsKz", "PlaeneNebeneinander",
        "PlaeneUebereinander", "Randstunde", "RaumVerdraengen", "SchaltflaechenOben", "SchaltflaechenUnten", "Schrift", "SchriftProzent", "Schriftart",
        "SchuelerDruckname", "Schulwoche", "SeitenNummer", "Seitenueberschrift", "SelberRaum", "SpaltenDatumMitJahr", "SpaltenFett", "SpaltenKursiv", "SpaltenProBlatt",
        "SpaltenSchriftProzent", "SpaltenStellen", "SpaltenTagUndDatum", "SpaltenUnterstrichen", "Sprechstunde", "Statistik", "StatusNeu", "StatusNeuAb", "StellenMax",
        "Stern", "StundenImRaum", "StundenplanKlassenlehrer", "StundenplanVersion", "Stundensatz", "Stundentafel", "TableEigenschaften", "TageNichtInFolge",
        "TagesrandPause", "Ueberschrift", "Ueberschrift1", "Ueberschrift2", "UeberschriftSchriftProzent", "UhrzeitDrucken", "UnterrichtIds", "Unterrichtsfolgen",
        "UrsprLehrerId", "Utf8", "VariablerLehrer", "VersionDrucken", "Vorjahresname", "VorziehungenFensterhoehe", "VorziehungenHoeheAutomatisch", "WaagrechteLinie",
        "WichtigHohlstunden", "WichtigMittagspause", "WichtigStundenFolge", "WichtigStundenProTag", "WochenstundenBereich", "Zeile", "ZeilenArt", "ZeilenFett",
        "ZeilenKursiv", "ZeilenNurBeginn", "ZeilenProBlatt", "ZeilenSchriftProzent", "ZeilenSpaltenAutomatisch", "ZeilenUnterstrichen", "ZusatzKlassenLehrer",
        "Zusatztext1", "Zusatztext2", "ZweiTageHintereinander", "ZwischenueberschriftNach",
    };

    private static void FelderEntfernen(JsonNode? knoten, ISet<string> felder)
    {
        switch (knoten)
        {
            case JsonObject o:
                foreach (var name in o.Select(x => x.Key).ToList())
                {
                    if (felder.Contains(name)) o.Remove(name);
                    else FelderEntfernen(o[name], felder);
                }
                break;
            case JsonArray a:
                foreach (var x in a) FelderEntfernen(x, felder);
                break;
        }
    }

    [Fact]
    public void AeltereDatei_OhneNeueFelder_LaedtMitStandardwerten()
    {
        var d = Demo.Beispielschule();
        new Rundreise.Fueller().Fuellen(d.Einstellungen, 0);
        var knoten = JsonSerializer.SerializeToNode(d, Dokument.JsonOptionen)!;
        var felder = new HashSet<string>(NeueFelder);
        FelderEntfernen(knoten, felder);
        var pfad = Path.Combine(Path.GetTempPath(), $"r3d_alt_{Guid.NewGuid():N}{Dokument.Dateiendung}");
        try
        {
            File.WriteAllText(pfad, knoten.ToJsonString());
            var geladen = Dokument.Laden(pfad).Daten;
            Dokument.Normalisieren(geladen);
            Assert.Equal(d.Klassen.Count, geladen.Klassen.Count);
            Assert.Equal(d.Unterrichte.Count, geladen.Unterrichte.Count);
            Assert.Equal(d.Stundenplan.Count, geladen.Stundenplan.Count);
            // die entfernten Felder haben nach dem Laden den Standardwert eines neuen Objekts
            var abweichend = new List<string>();
            void Pruefen(object o, string pfadText, int tiefe)
            {
                if (tiefe > 8 || o.GetType().GetConstructor(Type.EmptyTypes) is null) return;
                var standard = Activator.CreateInstance(o.GetType())!;
                foreach (var p in Rundreise.Gespeichert(o.GetType()).Where(p => !Rundreise.Altformat(p)))
                {
                    var wert = p.GetValue(o);
                    if (felder.Contains(p.Name) && (p.SetMethod != null || wert is IEnumerable))
                    {
                        var diff = new List<string>();
                        Rundreise.Vergleichen(wert, p.GetValue(standard), $"{pfadText}.{p.Name}", diff);
                        abweichend.AddRange(diff);
                    }
                    else if (wert is IEnumerable liste and not string and not IDictionary)
                    {
                        var i = 0;
                        foreach (var x in liste.Cast<object?>().Take(3))
                            if (x != null && !Rundreise.IstEinfach(x.GetType())) Pruefen(x, $"{pfadText}.{p.Name}[{i++}]", tiefe + 1);
                    }
                    else if (wert != null && !Rundreise.IstEinfach(p.PropertyType) && wert is not JsonNode && wert is not IDictionary) Pruefen(wert, $"{pfadText}.{p.Name}", tiefe + 1);
                }
            }
            Pruefen(geladen, "Schuldatei", 0);
            Assert.True(abweichend.Count == 0, "Fehlende Felder ohne Standardwert:\n" + string.Join("\n", abweichend.Take(40)));
            // die Daten sind voll verwendbar: Index, Kalender, Vertretungen
            var ix = new DatenIndex(geladen);
            Assert.NotNull(ix);
            Vertretungsplanung.Vertretungen.Aktualisieren(geladen, geladen.Einstellungen.SchuljahrVon, geladen.Einstellungen.SchuljahrVon.AddDays(14));
        }
        finally
        {
            if (File.Exists(pfad)) File.Delete(pfad);
        }
    }

    /// <summary>Die kleinste denkbare ältere Datei (nur Version und Schulname) lädt und ist nach dem Normalisieren verwendbar.</summary>
    [Fact]
    public void MinimaleAlteDatei_Laedt()
    {
        var pfad = Path.Combine(Path.GetTempPath(), $"r3d_min_{Guid.NewGuid():N}{Dokument.Dateiendung}");
        try
        {
            File.WriteAllText(pfad, "{\"Version\":1,\"Einstellungen\":{\"Schulname\":\"Alte Schule\"},\"Klassen\":[{\"Id\":1,\"Kurzname\":\"5a\"}]}");
            var d = Dokument.Laden(pfad).Daten;
            Dokument.Normalisieren(d);
            Assert.Equal("Alte Schule", d.Einstellungen.Schulname);
            Assert.Single(d.Klassen);
            Assert.NotEmpty(d.Perioden);
            Vertretungsplanung.Vertretungen.Aktualisieren(d, d.Einstellungen.SchuljahrVon, d.Einstellungen.SchuljahrVon.AddDays(7));
        }
        finally
        {
            if (File.Exists(pfad)) File.Delete(pfad);
        }
    }
}
