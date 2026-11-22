using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>
/// Texte der Vertretungsliste (Desktop) wie im Vorbild: Entfall mit „---“ bei Vertreter, Raum und Fach und „Entfall“ in „(Le.) nach“,
/// wegverlegte Stunde mit dem Ziel in „(Le.) nach“, ankommende Stunde mit der Herkunft in „Vertr. von“, offene Vertretung „???“.
/// </summary>
public static class Vertretungsanzeige
{
    public const string Strich = "---";
    public const string Offen = "???";
    public const string OhneLehrer = "+++";
    public const string EntfallText = "Entfall";

    /// <summary>Position „T.M. / Stunde“ (Spalten „Vertr. von“ und „(Le.) nach“).</summary>
    public static string Position(DateOnly? datum, int? stunde) => datum is DateOnly d ? $"{d:d.M.} / {stunde}" : "";

    /// <summary>Entfall wegen einer Veranstaltung ohne Lehrkraft: Vertreter, Raum und Fach bleiben leer statt „---“.</summary>
    public static bool OhneStriche(Schuldatei d, Vertretung v) => v.IstEntfall && VeranstaltungsLogik.OhneLehrer(d, v);

    /// <summary>„Vertr. von“: Herkunft einer verlegten bzw. getauschten Stunde.</summary>
    public static string Von(Vertretung v) => Position(v.VonDatum, v.VonStunde);

    /// <summary>
    /// „(Le.) nach“: Ziel einer wegverlegten Stunde; bei einem reinen Entfall (Art Entfall, nicht verlegt) wie im Vorbild „Entfall“.
    /// </summary>
    public static string Nach(Vertretung v) =>
        v.NachDatum.HasValue ? Position(v.NachDatum, v.NachStunde) : v.Art == VertretungsArt.Entfall ? EntfallText : "";

    /// <summary>Vertreter: „---“ bei Entfall, „+++“ ohne Lehrkraft, „???“ offen, sonst der Kurzname.</summary>
    public static string Vertreter(Schuldatei d, Vertretung v, Func<int, string>? name = null)
    {
        name ??= id => DatenIndex.Fuer(d).KLehrer(id);
        if (v.Art is VertretungsArt.VertretungOhneLehrer or VertretungsArt.EigenverantwortlichesArbeiten) return OhneLehrer;
        if (v.Art == VertretungsArt.Freisetzung) return v.LehrerId is int fl ? name(fl) : "";
        if (OhneStriche(d, v)) return "";
        if (v.IstEntfall || v.Art == VertretungsArt.Vormerkung) return Strich;
        if (v.VertreterId is int l) return name(l);
        return v.Offen ? Offen : "";
    }

    /// <summary>Neuer Raum: „---“ bei Entfall, „???“ bei fehlendem Ersatzraum, sonst Raum (und Zusatzräume).</summary>
    public static string NeuerRaum(Schuldatei d, Vertretung v, Func<int, string>? name = null)
    {
        name ??= id => DatenIndex.Fuer(d).KRaum(id);
        if (OhneStriche(d, v)) return "";
        if (v.IstEntfall) return Strich;
        if (v.RaumOffen) return Offen;
        if (v.Art == VertretungsArt.Pausenaufsicht) return DatenIndex.Fuer(d).K(ElementTyp.Gang, v.GangId);
        return string.Join(",", new[] { v.VertretungsraumId ?? v.RaumId }.Concat(v.ZusatzraumIds.Cast<int?>()).OfType<int>().Select(name));
    }

    /// <summary>Neues Fach: „---“ bei Entfall, sonst das Vertretungsfach (bei Verlegung/Tausch das Fach der Stunde), sonst leer.</summary>
    public static string NeuesFach(Schuldatei d, Vertretung v, Func<int, string>? name = null)
    {
        name ??= id => DatenIndex.Fuer(d).KFach(id);
        if (OhneStriche(d, v)) return "";
        if (v.IstEntfall) return Strich;
        var fach = v.VertretungsfachId ?? (v.Art is VertretungsArt.Tausch or VertretungsArt.Verlegung or VertretungsArt.Verlegt ? v.FachId : null);
        return fach is int f ? name(f) : "";
    }
}

/// <summary>Kennzahlen eines Tages im Kalender der Vertretungsplanung (Spalten Vertr., Offen, PA-Vertr. …).</summary>
public sealed record Tageskennzahlen(int AbwesendeLehrer, int Vertretungen, int Offen, int Pausenvertretungen, int PausenOffen, int AbwesendeKlassen,
    int Freisetzungen, int GesperrteRaeume, int Raumvertretungen, int RaeumeOffen, int Tagestexte, int Entfaelle);

/// <summary>
/// Kennzahlen des Kalenders – gezählt werden die Zeilen, die auch die Vertretungsliste zeigt: ohne Bereitschaften und Unterricht ohne
/// Vertretung, ohne Veranstaltungen selbst und ohne die Zeilen, die mit einer ankommenden Verlegung bzw. einem Tausch zu einer Zeile
/// zusammengefasst sind (Stundentausch: je Position eine Zeile, Vorziehung: die gefüllte offene Stunde).
/// </summary>
public static class Kalenderkennzahlen
{
    /// <summary>Zeilen des Tages, wie sie die Vertretungsliste zählt.</summary>
    public static List<Vertretung> Listenzeilen(Schuldatei d, DateOnly datum)
    {
        var alle = Vertretungen.Am(d, datum).Where(v => v.Art is not (VertretungsArt.Veranstaltung or VertretungsArt.Unterricht) && !Vertretungen.NichtInListe(d, v)).ToList();
        var verdeckt = Vertretungen.ZusammengefassteZeilen(alle).Values.Select(v => v.Id).ToHashSet();
        return alle.Where(v => !verdeckt.Contains(v.Id)).ToList();
    }

    public static Tageskennzahlen Tag(Schuldatei d, DateOnly datum)
    {
        var liste = Listenzeilen(d, datum);
        // offene Plätze einer Veranstaltung zählen mit (die Veranstaltung selbst steht nicht in der Liste)
        var vaOffen = d.Vertretungen.Count(v => v.Datum == datum && v.VeranstaltungOffen);
        bool Gilt(Abwesenheit a) => datum >= a.Von && datum <= a.Bis;
        return new Tageskennzahlen(
            d.Abwesenheiten.Where(a => a.Art == AbwesenheitsArt.Lehrer && Gilt(a)).Select(a => a.ElementId).Distinct().Count(),
            liste.Count(v => v.GangId is null && v.Art is not (VertretungsArt.Raumvertretung or VertretungsArt.Freisetzung or VertretungsArt.Pausenaufsicht)),
            liste.Count(v => v.GangId is null && v.Offen) + vaOffen,
            liste.Count(v => v.GangId != null), liste.Count(v => v.GangId != null && v.Offen),
            d.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Klasse && Gilt(a)),
            liste.Count(v => v.Art == VertretungsArt.Freisetzung),
            d.Abwesenheiten.Count(a => a.Art == AbwesenheitsArt.Raum && Gilt(a)),
            // Raumvertretungen: auch Vertretungen, deren Raum zusätzlich gesperrt ist (Lehrer- und Raumabsenz in derselben Stunde)
            liste.Count(v => v.Art == VertretungsArt.Raumvertretung || (v.RaumGesperrt && !v.IstEntfall)),
            liste.Count(v => v.RaumOffen),
            d.Tagestexte.Count(x => x.GiltAm(datum)),
            liste.Count(v => v.Art == VertretungsArt.Entfall));
    }
}
