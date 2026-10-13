using UntPlan.Core.Model;
using UntPlan.Core.Planung;

namespace UntPlan.Core.Optimierung;

public sealed record DiagnoseFall(string Element, int? UnterrichtId, string Zeit, string Detail, ElementTyp? Typ = null, int? ElementId = null);

public sealed class DiagnosePunkt
{
    public string Rubrik { get; init; } = "";
    public string Gruppe { get; init; } = "";
    public string Titel { get; init; } = "";
    public string Beschreibung { get; init; } = "";
    public int Gewicht { get; init; }
    public List<DiagnoseFall> Faelle { get; } = new();
}

/// <summary>Stundenplan-Diagnose: Karteikarte "Eingabedaten" (Konsistenz der Daten) und "Stundenplan" (Verstöße).</summary>
public static class Diagnose
{
    public static List<DiagnosePunkt> Eingabedaten(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var punkte = new List<DiagnosePunkt>();
        var zr = d.Zeitraster;
        DiagnosePunkt P(string gruppe, string titel, string beschr, int gewicht)
        {
            var p = new DiagnosePunkt { Rubrik = "Eingabedaten", Gruppe = gruppe, Titel = titel, Beschreibung = beschr, Gewicht = gewicht };
            punkte.Add(p);
            return p;
        }
        int VerfuegbareTage(Element e) => Enumerable.Range(0, zr.TageProWoche).Count(t => Enumerable.Range(1, zr.StundenProTag).Any(s => zr.Verfuegbar(t, s) && !e.Zeitwuensche.Gesperrt(t, s)))
            - e.UnbestimmteZeitwuensche.Where(z => z.Bereich == ZeitbereichArt.Tage && z.Wunsch <= -3).Sum(z => z.Anzahl);
        int VerfuegbareStunden(Element e) => Enumerable.Range(0, zr.TageProWoche).Sum(t => Enumerable.Range(1, zr.StundenProTag).Count(s => zr.Verfuegbar(t, s) && !e.Zeitwuensche.Gesperrt(t, s)));

        var fach1mal = P("Klasse", "Fach 1 mal pro Tag nicht möglich",
            "Ein Unterricht hat mehr Einzelstunden bzw. Doppelstunden-Einheiten, als Tage zur Verfügung stehen (z. B. wegen eines freien Tages des Lehrers). " +
            "Die Bedingung \"Fach nicht mehrmals am Tag\" oder die Doppelstundenbedingung muss dann verletzt werden.", 3);
        var zuViel = P("Klasse", "Mehr Wochenstunden als verfügbare Stunden", "Die Summe der Wochenstunden einer Klasse übersteigt die im Zeitraster verfügbaren (nicht gesperrten) Stunden.", 5);
        var kern = P("Klasse", "Kernzeit größer als Wochenstunden", "Es sind mehr Kernzeit-Stunden (+3) definiert, als die Klasse Wochenstunden hat. Kernzeitverletzungen sind unvermeidbar.", 3);
        var ohneU = P("Klasse", "Klasse ohne Unterricht", "Für die Klasse ist kein Unterricht eingegeben.", 1);
        foreach (var k in d.Klassen.Where(k => !k.Ignorieren))
        {
            var unts = ix.UnterrichteVon(ElementTyp.Klasse, k.Id).Where(u => !u.Ignorieren).ToList();
            if (unts.Count == 0) { ohneU.Faelle.Add(new DiagnoseFall(k.Kurzname, null, "", "", ElementTyp.Klasse, k.Id)); continue; }
            var wst = unts.GroupBy(u => string.IsNullOrEmpty(u.Band) ? $"u{u.Id}" : u.Band).Sum(g => g.Max(u => u.Wochenstunden));
            var verf = VerfuegbareStunden(k);
            if (wst > verf) zuViel.Faelle.Add(new DiagnoseFall(k.Kurzname, null, "", $"{wst} Wst., {verf} Stunden verfügbar", ElementTyp.Klasse, k.Id));
            var kz = k.Zeitwuensche.Werte.Count(w => w.Value >= 3);
            if (kz > wst) kern.Faelle.Add(new DiagnoseFall(k.Kurzname, null, "", $"{kz} Kernzeit-Stunden, {wst} Wst.", ElementTyp.Klasse, k.Id));
            foreach (var u in unts)
            {
                if (u.MehrmalsProTag) continue;
                var einheiten = u.Bloecke().Count;
                var tage = new[] { VerfuegbareTage(k) }.Concat(u.LehrerIds.Select(l => ix.Lehrer.TryGetValue(l, out var le) ? VerfuegbareTage(le) : zr.TageProWoche)).Min();
                if (einheiten > tage)
                    fach1mal.Faelle.Add(new DiagnoseFall(k.Kurzname, u.Id, "", $"{ix.UnterrichtText(u)}: {einheiten} Einheiten, {tage} Tage verfügbar", ElementTyp.Klasse, k.Id));
            }
        }

        var lehrerZuViel = P("Lehrer", "Mehr Stunden als verfügbar", "Die Wochenstunden des Lehrers übersteigen seine verfügbaren Stunden (Zeitraster, Sperrungen).", 5);
        var fachfremd = P("Lehrer", "Unterricht ohne Lehrbefähigung", "Der Lehrer unterrichtet ein Fach, das nicht in seinen Fächern (Lehrbefähigung) eingetragen ist.", 1);
        var sollIst = P("Lehrer", "Soll/Ist-Differenz", "Wert der Unterrichte weicht vom Soll (Wochensoll abzüglich Ermäßigungen) ab.", 1);
        var lehrerOhne = P("Lehrer", "Lehrer ohne Unterricht", "Für den Lehrer ist kein Unterricht eingegeben.", 0);
        foreach (var l in d.Lehrer.Where(l => !l.Ignorieren))
        {
            var unts = ix.UnterrichteVon(ElementTyp.Lehrer, l.Id).Where(u => !u.Ignorieren).ToList();
            if (unts.Count == 0) { lehrerOhne.Faelle.Add(new DiagnoseFall(l.Kurzname, null, "", "", ElementTyp.Lehrer, l.Id)); continue; }
            var wst = unts.Sum(u => u.Wochenstunden);
            var verf = VerfuegbareStunden(l);
            if (wst > verf) lehrerZuViel.Faelle.Add(new DiagnoseFall(l.Kurzname, null, "", $"{wst} Wst., {verf} Stunden verfügbar", ElementTyp.Lehrer, l.Id));
            foreach (var u in unts)
                foreach (var z in u.Zeilen.Where(z => z.LehrerId == l.Id && z.FachId.HasValue))
                    if (l.FaecherIds.Count > 0 && !l.FaecherIds.Contains(z.FachId!.Value))
                        fachfremd.Faelle.Add(new DiagnoseFall(l.Kurzname, u.Id, "", $"Fach {ix.KFach(z.FachId)}", ElementTyp.Lehrer, l.Id));
            if (l.Wochensoll > 0)
            {
                var ist = Module.Wertrechnung.Ist(d, ix, l);
                if (Math.Abs(ist - l.Soll) >= 1) sollIst.Faelle.Add(new DiagnoseFall(l.Kurzname, null, "", $"Soll {Format.Zahl(l.Soll)}, Ist {Format.Zahl(ist)}", ElementTyp.Lehrer, l.Id));
            }
        }

        var raumUeber = P("Raum", "Raum überbucht", "Für einen Raum (inkl. Ausweichräume / Raumgruppe) sind mehr Stunden mit hohem Raumgewicht vorgesehen, als verfügbar sind.", 3);
        var ringFehler = P("Raum", "Ausweichraum fehlerhaft", "Der eingetragene Ausweichraum existiert nicht oder verweist auf sich selbst.", 2);
        var bedarf = new Dictionary<string, (int Std, int Kap, string Name)>();
        foreach (var u in d.Unterrichte.Where(u => !u.Ignorieren))
            foreach (var z in u.Zeilen)
            {
                var kand = ix.RaumKandidaten(z);
                if (kand.Count == 0 || ix.Raumgewicht(z) < 3) continue;
                var key = string.Join(",", kand.OrderBy(x => x));
                var verf = kand.Sum(r => VerfuegbareStunden(ix.Raeume[r]));
                var name = string.Join(",", kand.Select(ix.KRaum));
                bedarf[key] = (bedarf.GetValueOrDefault(key).Std + u.Wochenstunden, verf, name);
            }
        foreach (var (_, (std, kap, name)) in bedarf)
            if (std > kap) raumUeber.Faelle.Add(new DiagnoseFall(name, null, "", $"{std} Stunden benötigt, {kap} verfügbar"));
        foreach (var r in d.Raeume.Where(r => r.AusweichraumId.HasValue))
            if (r.AusweichraumId == r.Id || !ix.Raeume.ContainsKey(r.AusweichraumId!.Value))
                ringFehler.Faelle.Add(new DiagnoseFall(r.Kurzname, null, "", "Ausweichraum ungültig", ElementTyp.Raum, r.Id));

        var ohneLehrer = P("Unterricht", "Unterricht ohne Lehrer", "Kopplungszeile ohne Lehrer.", 2);
        var ohneKlasse = P("Unterricht", "Unterricht ohne Klasse", "Kopplungszeile ohne Klasse.", 1);
        var ohneFach = P("Unterricht", "Unterricht ohne Fach", "Kopplungszeile ohne Fach.", 2);
        var doppelZuGross = P("Unterricht", "Doppelstunden-Bereich nicht erfüllbar", "Es werden mehr Doppelstunden verlangt, als Wochenstunden vorhanden sind.", 3);
        var ohneRaum = P("Unterricht", "Unterricht mit Lehrer ohne Raum", "Für die Kopplungszeile ist weder Fach- noch Stammraum (oder Raumgruppe) angegeben.", 1);
        var gleicherRaum = P("Unterricht", "2 Lehrer im selben Raum", "Zwei Kopplungszeilen desselben Unterrichts verwenden denselben Raum.", 2);
        foreach (var u in d.Unterrichte.Where(u => !u.Ignorieren))
        {
            foreach (var z in u.Zeilen)
            {
                if (z.LehrerId is null && !(z.FachId.HasValue && ix.Faecher.TryGetValue(z.FachId.Value, out var bf) && bf.Bereitschaft)) ohneLehrer.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", ix.UnterrichtText(u)));
                if (z.KlassenIds.Count == 0 && !(z.FachId.HasValue && ix.Faecher.TryGetValue(z.FachId.Value, out var f2) && f2.Bereitschaft)) ohneKlasse.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", ix.UnterrichtText(u)));
                if (z.FachId is null) ohneFach.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", ix.UnterrichtText(u)));
                if (z.LehrerId.HasValue && z.Wunschraum is null && z.Wunschraumgruppe is null) ohneRaum.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", ix.UnterrichtText(u)));
            }
            if (u.Doppelstunden.Min * 2 > u.Wochenstunden) doppelZuGross.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", $"{u.Wochenstunden} Wst., Doppelstunden {u.Doppelstunden}"));
            var raeume = u.Zeilen.Where(z => z.Wunschraum.HasValue).GroupBy(z => z.Wunschraum).Where(g => g.Count() > 1);
            foreach (var g in raeume) gleicherRaum.Faelle.Add(new DiagnoseFall($"U{u.Id}", u.Id, "", $"Raum {ix.KRaum(g.Key)}"));
        }

        var kurs = P("Kursplanung", "Kurswahl-Kollision im Band", "Ein Schüler hat zwei Kurse gewählt, die im selben Band (gleichzeitig) liegen.", 3);
        foreach (var s in d.Schueler)
        {
            var baender = s.KurswahlIds.Select(id => ix.Unterrichte.GetValueOrDefault(id)).Where(u => u != null && !string.IsNullOrEmpty(u.Band)).GroupBy(u => u!.Band);
            foreach (var g in baender.Where(g => g.Count() > 1))
                kurs.Faelle.Add(new DiagnoseFall($"{s.Kurzname} {s.Vorname}", null, "", $"Band {g.Key}: {string.Join(", ", g.Select(u => u!.Kursname))}", ElementTyp.Schueler, s.Id));
        }
        return punkte;
    }

    public static List<DiagnosePunkt> Stundenplan(Schuldatei d)
    {
        var ix = new DatenIndex(d);
        var v = Bewertung.Verstoesse(d);
        var punkte = new List<DiagnosePunkt>();
        foreach (var g in v.GroupBy(x => (Gruppe: GruppeVon(x), x.Art)).OrderBy(g => g.Key.Gruppe).ThenByDescending(g => g.Sum(x => x.Kosten)))
        {
            var p = new DiagnosePunkt
            {
                Rubrik = "Stundenplan",
                Gruppe = g.Key.Gruppe,
                Titel = Bewertung.ArtText(g.Key.Art),
                Beschreibung = Beschreibung(g.Key.Art),
                Gewicht = g.Max(x => x.Kosten) >= PlanModell.Hart ? 5 : Math.Clamp((int)Math.Log10(1 + g.Average(x => x.Kosten)) + 1, 1, 4),
            };
            foreach (var x in g.OrderBy(x => x.Tag).ThenBy(x => x.Stunde))
            {
                var name = x.ElementTyp == ElementTyp.Unterricht ? $"U{x.ElementId}" : ix.K(x.ElementTyp, x.ElementId);
                var zeit = x.Tag >= 0 ? $"{Planungsdienst.Tagname(d, x.Tag)}{(x.Stunde > 0 ? " " + x.Stunde + "." : "")}" : "Woche";
                var detail = x.Text + (x.UnterrichtId is int uid && ix.Unterrichte.TryGetValue(uid, out var u) ? $" – {ix.UnterrichtText(u)}" : "");
                p.Faelle.Add(new DiagnoseFall(name, x.UnterrichtId, zeit, detail, x.ElementTyp, x.ElementId));
            }
            punkte.Add(p);
        }
        return punkte;
    }

    private static string GruppeVon(Verstoss v) => v.ElementTyp switch
    {
        ElementTyp.Klasse => "Klasse",
        ElementTyp.Lehrer => "Lehrer",
        ElementTyp.Raum => "Raum",
        _ => "Unterricht",
    };

    public static string Beschreibung(VerstossArt a) => a switch
    {
        VerstossArt.HohlstundeKlasse => "Zwischen zwei Unterrichtsstunden einer Klasse liegt eine freie Stunde (außerhalb der Mittagspause).",
        VerstossArt.Kernzeitverletzung => "In einer Stunde mit Zeitwunsch +3 (Kernzeit) findet für die Klasse kein Unterricht statt. Für die Optimierung ein sehr schwerer Verstoß.",
        VerstossArt.HohlstundenLehrer => "Die Anzahl der Hohlstunden des Lehrers liegt außerhalb des in den Stammdaten (HohlStd.) eingegebenen Bereichs.",
        VerstossArt.StdTagLehrer => "In diesen Fällen konnte die Bedingung \"Stunden pro Tag\" aus den Stammdaten der Lehrer nicht eingehalten werden. Soll die Eingabe stärker berücksichtigt werden, den Regler \"Maximale bzw. minimale Stundenzahl je Tag für Lehrer einhalten\" erhöhen und neu optimieren.",
        VerstossArt.StdTagKlasse => "Die Stunden pro Tag der Klasse liegen außerhalb des eingegebenen Bereichs.",
        VerstossArt.FachMehrmalsProTag => "Ein Fach ist mehrmals am selben Tag, aber nicht unmittelbar hintereinander verplant.",
        VerstossArt.DoppelstundenFehler => "Die Anzahl der Doppelstunden entspricht nicht dem Bereich im Feld \"Dopp.Std.\".",
        VerstossArt.MittagspauseKlasse or VerstossArt.MittagspauseLehrer => "Bei Vor- und Nachmittagsunterricht liegt keine ausreichend lange Mittagspause im erlaubten Zeitfenster.",
        VerstossArt.UnbestimmterZeitwunsch => "Ein unbestimmter Zeitwunsch (freier Tag / Halbtag) wurde nicht erfüllt.",
        VerstossArt.KeinRaum => "Weder der gewünschte Raum noch einer seiner Ausweichräume war frei.",
        VerstossArt.NichtVerplant => "Diese Unterrichtsstunden konnten nicht kollisionsfrei verplant werden.",
        VerstossArt.Lehrerkollision or VerstossArt.Klassenkollision or VerstossArt.Raumkollision => "Ein Element ist zur selben Zeit mehrfach verplant.",
        _ => "Verstoß gegen eine Vorgabe aus Stammdaten, Unterricht oder Zeitwünschen.",
    };
}
