using UntPlan.Core.Model;

namespace UntPlan.Core.Vertretungsplanung;

/// <summary>Zähler im Rahmen „offene Vertretungen“ des Dialogs Vertretungsautomatik.</summary>
public sealed record OffeneVertretungen(int Vertretungen, int Raumvertretungen, int Pausenaufsichten)
{
    public int Summe => Vertretungen + Raumvertretungen + Pausenaufsichten;
}

/// <summary>
/// Vertretungsautomatik: offene Vertretungen eines Zeitraums nacheinander nach der gewählten Methode bearbeiten.
/// <list type="bullet">
/// <item>Alle (bester Vertreter des Vorschlags): oberster Lehrer des Vertretungsvorschlags, offene Pausenaufsichten und Raumvertretungen (bester freier Raum).</item>
/// <item>Freigesetzte: nur Bereitschaftslehrer und freigesetzte Lehrer.</item>
/// <item>Kopplungslehrer: eine Lehrkraft derselben Kopplung betreut mit.</item>
/// <item>Pausenaufsichten: nur Pausenaufsichtsvertretungen.</item>
/// <item>Raumvertretungen (nur Ausweichräume).</item>
/// <item>Vorziehungen: wo möglich wird Unterricht aus einer Randstunde vorgezogen, sonst bester Vertreter.</item>
/// </list>
/// </summary>
public static class Vertretungsautomatik
{
    /// <summary>Methoden in der Reihenfolge der Auswahlliste.</summary>
    public static readonly (AutomatikMethode Methode, string Text)[] Methoden =
    {
        (AutomatikMethode.BesterVorschlag, "Alle (bester Vertreter des Vorschlags)"),
        (AutomatikMethode.Freigesetzte, "Freigesetzte"),
        (AutomatikMethode.Kopplungslehrer, "Kopplungslehrer"),
        (AutomatikMethode.Pausenaufsichten, "Pausenaufsichten"),
        (AutomatikMethode.Raumvertretungen, "Raumvertretungen (nur Ausweichräume)"),
        (AutomatikMethode.VorziehungenZuerst, "Vorziehungen"),
    };

    public static OffeneVertretungen Zaehlen(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var liste = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis).ToList();
        return new OffeneVertretungen(
            liste.Count(v => v.Offen && v.Art != VertretungsArt.Pausenaufsicht),
            liste.Count(v => v.RaumOffen),
            liste.Count(v => v.Offen && v.Art == VertretungsArt.Pausenaufsicht));
    }

    /// <summary>Wird die Zeile von der Methode bearbeitet?</summary>
    public static bool Betrifft(Vertretung v, AutomatikMethode m) => m switch
    {
        AutomatikMethode.Pausenaufsichten => v.Offen && v.Art == VertretungsArt.Pausenaufsicht,
        AutomatikMethode.Raumvertretungen => v.RaumOffen,
        AutomatikMethode.BesterVorschlag => v.OffenOderRaumOffen,
        _ => v.Offen && v.Art != VertretungsArt.Pausenaufsicht,
    };

    /// <summary>Text der Fortschrittsanzeige „Vertreter statt: Lehrer (Fach / Klasse(n))“.</summary>
    public static string Beschreibung(Schuldatei d, Vertretung v)
    {
        var ix = new DatenIndex(d);
        var was = v.Art == VertretungsArt.Pausenaufsicht ? ix.K(ElementTyp.Gang, v.GangId) : $"{ix.KFach(v.FachId)} / {ix.KKlassen(v.NeueKlassenIds)}";
        return $"{(v.RaumOffen ? "Raum statt" : "Vertreter statt")}: {ix.KLehrer(v.LehrerId)} ({was})  –  {Format.Datum(v.Datum)}, {Vertretungsplan.StundeText(v)}. Std.";
    }

    /// <summary>Automatik ausführen. Liefert die Anzahl bearbeiteter Vertretungen; <paramref name="fortschritt"/> erhält (Zeile, Nummer, Gesamtzahl).</summary>
    public static int Ausfuehren(Schuldatei d, DateOnly von, DateOnly bis, AutomatikMethode methode, Action<Vertretung, int, int>? fortschritt = null, string benutzer = "")
    {
        var tage = Kalender.Tage(von, bis).Where(t => Kalender.IstUnterrichtstag(d, t)).ToList();
        foreach (var t in tage) Vertretungen.Aktualisieren(d, t);
        if (methode == AutomatikMethode.BereitschaftUndFreigesetzteZuerst)
            return tage.Sum(t => VertretungsVorschlag.Automatisch(d, t, methode, benutzer));
        var liste = d.Vertretungen.Where(v => v.Datum >= von && v.Datum <= bis && Betrifft(v, methode)).OrderBy(v => v.Datum).ThenBy(v => v.Stunde).ThenBy(v => v.Id).ToList();
        var anzahl = 0;
        for (var i = 0; i < liste.Count; i++)
        {
            var v = liste[i];
            if (!d.Vertretungen.Contains(v) || !Betrifft(v, methode)) continue;
            fortschritt?.Invoke(v, i + 1, liste.Count);
            if (Bearbeiten(d, v, methode, benutzer)) anzahl++;
        }
        return anzahl;
    }

    /// <summary>Eine offene Vertretung nach der Methode bearbeiten.</summary>
    public static bool Bearbeiten(Schuldatei d, Vertretung v, AutomatikMethode methode, string benutzer = "")
    {
        if (v.RaumOffen && !v.Offen)
        {
            if (methode is not (AutomatikMethode.BesterVorschlag or AutomatikMethode.Raumvertretungen)) return false;
            var raum = Raumvertretungen.BesterRaum(d, v, methode == AutomatikMethode.Raumvertretungen);
            if (raum is null) return false;
            Raumvertretungen.Zuordnen(d, v, raum.Id, false, RaumKollisionsLoesung.MitKollision, benutzer);
            return true;
        }
        if (v.Art == VertretungsArt.Pausenaufsicht)
        {
            if (methode is not (AutomatikMethode.BesterVorschlag or AutomatikMethode.Pausenaufsichten)) return false;
            var pk = Module.Pausenaufsichten.Kandidaten(d, v.Datum, new Pausenaufsicht { GangId = v.GangId ?? 0, Tag = Kalender.TagIndex(d, v.Datum), NachStunde = v.Stunde, LehrerId = v.LehrerId ?? 0 }).FirstOrDefault();
            if (pk is null) return false;
            Vertretungen.VertreterSetzen(d, v, pk.Id, false, benutzer);
            return true;
        }
        switch (methode)
        {
            case AutomatikMethode.Kopplungslehrer:
            {
                var b = VertretungsVorschlag.Betreuungen(d, v).FirstOrDefault(x => x.Kopplung);
                if (b is null) return false;
                Vertretungen.VertreterSetzen(d, v, b.Lehrer.Id, true, benutzer);
                return true;
            }
            case AutomatikMethode.Freigesetzte:
            {
                var k = VertretungsVorschlag.Berechnen(d, v).FirstOrDefault(x => !x.Rot && (x.Bereitschaft || x.Freigesetzt));
                if (k is null) return false;
                Vertretungen.VertreterSetzen(d, v, k.Lehrer.Id, false, benutzer);
                return true;
            }
            case AutomatikMethode.VorziehungenZuerst when VertretungsVorschlag.Vorziehungen(d, v).FirstOrDefault(x => x.Beschreibung.EndsWith("(Randstunde)")) is { } vz:
                VertretungsVorschlag.VorziehungAusfuehren(d, v, vz, benutzer);
                return true;
            case AutomatikMethode.BesterVorschlag or AutomatikMethode.VorziehungenZuerst:
            {
                var k = VertretungsVorschlag.Berechnen(d, v).FirstOrDefault(x => !x.Rot);
                if (k is null)
                {
                    if (!VertretungsVorschlag.EntfallSinnvoll(d, v)) return false;
                    Vertretungen.EntfallUmschalten(d, v, benutzer);
                    return true;
                }
                Vertretungen.VertreterSetzen(d, v, k.Lehrer.Id, false, benutzer);
                return true;
            }
            default:
                return false;
        }
    }
}
