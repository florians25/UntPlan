using System.Globalization;

namespace UntPlanMobile.Dienste;

/// <summary>
/// Übersetzt den Wochenplan von UntPlanWeb (Karten, Tagesköpfe, Aufsichten) in die Kacheln der Stundenplan-Oberfläche.
/// <list type="bullet">
/// <item>Status → blockType: regulaer → normal, geaendert/offen/abwesend → change, entfall → cancelled, klausur → klausur,
/// bereitschaft → standby, veranstaltung → absence (grün gerahmtes Feld), buchung → event.</item>
/// <item>Geänderte Werte (Plakette) aus dem Kennzeichen „neu“, Ursprungswerte aus „alt“ (…Orig).</item>
/// <item>Ferien, Feiertage und freie Tage als ganztägiges Ferienfeld mit Name und Zeitraum.</item>
/// <item>Pausenaufsichten als Pillen in der Pause, ganztägige Termine als Pille über der ersten Stunde.</item>
/// </list>
/// </summary>
public static class Kachelbau
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Planart von UntPlanWeb (klasse, lehrer, raum) → Planart der Oberfläche (class, teacher, room).</summary>
    public static string Art(string typ) => typ switch { "klasse" => "class", "lehrer" => "teacher", "raum" => "room", _ => typ };

    /// <summary>Planart der Oberfläche → UntPlanWeb.</summary>
    public static string Typ(string art) => art switch { "class" => "klasse", "teacher" => "lehrer", "room" => "raum", _ => art };

    /// <summary>Stundenzeiten im Format der Oberfläche: {"1": {"start": "07:55", "end": "08:40"}, …}.</summary>
    public static Dictionary<string, object> Stundenzeiten(IEnumerable<PlanZeit> zeiten) =>
        zeiten.OrderBy(z => z.Nr).ToDictionary(z => z.Nr.ToString(CultureInfo.InvariantCulture), z => (object)new { start = z.Beginn, end = z.Ende });

    /// <summary>Kacheln der fünf Wochentage (Montag bis Freitag) eines Wochenplans.</summary>
    public static Dictionary<string, List<Kachel>> Woche(Wochenplan plan, IReadOnlyList<Ferienzeit>? ferien = null)
    {
        var montag = DateOnly.Parse(plan.Montag, CultureInfo.InvariantCulture);
        var res = new Dictionary<string, List<Kachel>>();
        for (var i = 0; i < 5; i++)
        {
            var datum = Iso(montag.AddDays(i));
            var tag = plan.Tage.FirstOrDefault(t => t.Datum == datum);
            res[datum] = tag is null ? new List<Kachel>() : Tag(plan, tag, ferien ?? Array.Empty<Ferienzeit>());
        }
        return res;
    }

    /// <summary>Kacheln eines Tages.</summary>
    public static List<Kachel> Tag(Wochenplan plan, PlanTag tag, IReadOnlyList<Ferienzeit> ferien)
    {
        var zeiten = plan.Zeitraster.OrderBy(z => z.Nr).ToList();
        if (tag.Frei)
            return tag.FreiArt == "wochenende" || zeiten.Count == 0 ? new List<Kachel>() : new List<Kachel> { Ferienfeld(tag, zeiten, ferien) };

        var art = Art(plan.Typ);
        var karten = plan.Karten.Where(k => k.Datum == tag.Datum).ToList();
        var res = KlausurenZusammenfuehren(karten.Select(k => AusKarte(k, art)).ToList());
        TeilnehmerErgaenzen(res, tag);
        if (art == "teacher") res = BereitschaftenKuerzen(res, karten);

        // Veranstaltungen, die nicht schon als Karte (Teilnahme) im Plan stehen
        if (zeiten.Count > 0)
        {
            var erste = zeiten[0];
            var letzte = zeiten[^1];
            foreach (var e in tag.Ereignisse)
            {
                if (karten.Any(k => k.Text == e.Titel && k.Von <= e.Bis && k.Bis >= e.Von)) continue;
                var von = Math.Max(e.Von, erste.Nr);
                var bis = Math.Min(Math.Max(e.Bis, von), letzte.Nr);
                var ganz = e.Ganztaegig || (von <= erste.Nr && bis >= letzte.Nr);
                if (!ganz) { res.Add(Veranstaltung(tag.Datum, von, bis, e.Titel, e.Klassen, e.Lehrer, e.Raeume)); continue; }
                // Findet an dem Tag Unterricht statt, steht der Termin als Pille über der ersten Stunde – sonst als Fläche über den Tag.
                var unterricht = res.Any(x => x.Art is not ("cancelled" or "supervision") && x.Feld != true);
                res.Add(unterricht
                    ? new Kachel
                    {
                        Stunde = erste.Nr, Dauer = 1, Datum = tag.Datum, Art = "event", Fach = e.Titel, Typ = "Termin",
                        Klasse = string.Join(",", e.Klassen), Lehrkraft = string.Join(",", e.Lehrer), Raum = string.Join(",", e.Raeume),
                        Ganztaegig = true, Pause = true, VorErsterStunde = true, Von = Minus(erste.Beginn, 15), Bis = erste.Beginn,
                    }
                    : new Kachel
                    {
                        Stunde = von, Dauer = bis - von + 1, Datum = tag.Datum, Art = "event", Fach = e.Titel, Typ = "Termin", Ganztaegig = true,
                        Klasse = string.Join(",", e.Klassen), Lehrkraft = string.Join(",", e.Lehrer), Raum = string.Join(",", e.Raeume),
                    });
            }
        }

        var aufsichten = plan.Aufsichten.Where(a => a.Datum == tag.Datum).ToList();
        if (aufsichten.Count > 0) res = AnPausenTeilen(res, aufsichten.Where(a => a.NachStunde >= 1).Select(a => a.NachStunde).ToHashSet());
        res.AddRange(aufsichten.Select(a => Aufsicht(a, zeiten)));
        return res;
    }

    /// <summary>
    /// Veranstaltungskacheln tragen im Lehrer- oder Raumplan oft nur das eigene Element. Wie in UntPlanWeb sollen dort alle
    /// teilnehmenden Lehrkräfte, Klassen und Räume stehen – sie kommen aus den Veranstaltungen des Tages (gleicher Titel, Zeit überlappt).
    /// </summary>
    private static void TeilnehmerErgaenzen(List<Kachel> kacheln, PlanTag tag)
    {
        static string Vereinen(string vorhanden, IEnumerable<string> weitere) =>
            string.Join(",", vorhanden.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(weitere.Where(x => !string.IsNullOrWhiteSpace(x))).Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var k in kacheln.Where(k => k.Art == "absence" && k.Feld == true))
        {
            var ende = k.Stunde + k.Dauer - 1;
            var e = tag.Ereignisse.FirstOrDefault(x => Ci.Equals(x.Titel, k.Fach) && x.Von <= ende && Math.Max(x.Bis, x.Von) >= k.Stunde);
            if (e is null) continue;
            k.Lehrkraft = Vereinen(k.Lehrkraft, e.Lehrer);
            k.Klasse = Vereinen(k.Klasse, e.Klassen);
            k.Raum = Vereinen(k.Raum, e.Raeume);
        }
    }

    /// <summary>
    /// Wird eine Stunde zur Klausur (Klausur aus UntPlan oder vorgemerkt), entsteht keine zweite Kachel: Die Klausur geht
    /// auf die Unterrichtskachel derselben Zeit und Klasse über (gelb, Bezeichnung als Info), die Klausurkachel entfällt.
    /// </summary>
    private static List<Kachel> KlausurenZusammenfuehren(List<Kachel> kacheln)
    {
        var klausuren = kacheln.Where(k => k.Art == "klausur" && k.Unterricht is null).ToList();
        foreach (var kl in klausuren)
        {
            var klassen = kl.Klasse.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ende = kl.Stunde + kl.Dauer - 1;
            var ziel = kacheln.FirstOrDefault(k => k != kl && k.Art is "normal" or "change" && k.Unterricht != null &&
                                                   k.Stunde <= ende && k.Stunde + k.Dauer - 1 >= kl.Stunde &&
                                                   (klassen.Length == 0 || k.Klasse.Split(',', StringSplitOptions.TrimEntries).Any(x => klassen.Contains(x, Ci))));
            if (ziel is null) continue;
            ziel.Art = "klausur";
            var titel = kl.Fach.Length > 0 && !Ci.Equals(kl.Fach, ziel.Fach) ? kl.Fach : "";
            ziel.Info = string.Join(" · ", new[] { "Klausur", titel, kl.Info != kl.Fach ? kl.Info : "", ziel.Info }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            if (kl.Raum.Length > 0 && !Ci.Equals(kl.Raum, ziel.Raum))
            {
                if (ziel.RaumAlt.Length == 0) ziel.RaumAlt = ziel.Raum;
                ziel.Raum = kl.Raum;
                if (!ziel.Geaendert.Contains("room")) ziel.Geaendert.Add("room");
            }
            kacheln.Remove(kl);
        }
        return kacheln;
    }

    /// <summary>
    /// Pausenaufsichten unterbrechen Blöcke: läuft eine Kachel über die Pause einer Aufsicht hinweg, wird sie dort
    /// geteilt, damit die Aufsicht in einer echten Lücke steht – sonst überdeckte der Block die Pause.
    /// </summary>
    private static List<Kachel> AnPausenTeilen(List<Kachel> kacheln, HashSet<int> pausen)
    {
        if (pausen.Count == 0) return kacheln;
        var res = new List<Kachel>();
        foreach (var k in kacheln)
        {
            var ende = k.Stunde + k.Dauer - 1;
            var schnitte = pausen.Where(p => p >= k.Stunde && p < ende).OrderBy(p => p).ToList();
            if (k.Pause == true || k.Dauer < 2 || schnitte.Count == 0) { res.Add(k); continue; }
            var beginn = k.Stunde;
            foreach (var p in schnitte.Append(ende))
            {
                var teil = k.Kopie();
                teil.Stunde = beginn;
                teil.Dauer = p - beginn + 1;
                res.Add(teil);
                beginn = p + 1;
            }
        }
        return res;
    }

    /// <summary>Eine Stundenkarte als Kachel.</summary>
    public static Kachel AusKarte(PlanKarte karte, string art)
    {
        var k = karte with { Text = karte.Text ?? "", Art = karte.Art ?? "", Fach = karte.Fach ?? "" };
        var kachel = new Kachel
        {
            Stunde = k.Von,
            Dauer = Math.Max(1, k.Bis - k.Von + 1),
            Datum = k.Datum,
            Klasse = Werte(k.Klassen), KlasseAlt = Alte(k.Klassen),
            Lehrkraft = Werte(k.Lehrer), LehrkraftAlt = Alte(k.Lehrer),
            Raum = Werte(k.Raeume), RaumAlt = Alte(k.Raeume),
            Fach = k.Fach,
            FachAlt = k.FachAlt ?? "",
            FachLang = string.IsNullOrEmpty(k.FachName) ? null : k.FachName,
            Farbe = string.IsNullOrWhiteSpace(k.Farbe) ? null : k.Farbe,
            Info = k.Text,
            Typ = k.Art,
            Eintrag = k.Unterricht,
            Unterricht = k.Unterricht,
        };
        if (k.Klassen.Any(w => w.Neu)) kachel.Geaendert.Add("class");
        if (k.Lehrer.Any(w => w.Neu)) kachel.Geaendert.Add("teacher");
        // geänderter oder hinzugefügter Raum (neuer Wert ohne alten) steht in der grünen Plakette
        if (k.Raeume.Any(w => w.Neu || (!string.IsNullOrEmpty(w.Alt) && !Ci.Equals(w.Alt, w.K)))) kachel.Geaendert.Add("room");
        if (!string.IsNullOrEmpty(k.FachAlt) && !Ci.Equals(k.FachAlt, k.Fach)) kachel.Geaendert.Add("subject");
        if (k.Art.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase))
            kachel.Info = string.Join(" · ", new[] { k.Art, k.Text }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var offen = k.Lehrer.Any(w => w.Neu && w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt));
        switch (k.Status)
        {
            case "regulaer":
                kachel.Art = "normal";
                break;
            case "geaendert" when k.Unterricht is null && k.Fach.Length == 0 && k.Text.Length > 0:
            case "veranstaltung":
                return Veranstaltung(k.Datum, k.Von, k.Bis, k.Text.Length > 0 ? k.Text : k.Art, k.Klassen.Select(w => w.K), k.Lehrer.Select(w => w.K), k.Raeume.Select(w => w.K), k.Farbe);
            case "geaendert":
                kachel.Art = "change";
                break;
            case "offen":
            case "abwesend":
                kachel.Art = "change";
                kachel.Offen = offen;
                break;
            case "entfall":
                kachel.Art = "cancelled";
                break;
            case "klausur":
                kachel.Art = "klausur";
                if (kachel.Fach.Length == 0) kachel.Fach = k.Text.Length > 0 ? k.Text : "Klausur";
                break;
            case "bereitschaft":
                kachel.Art = "standby";
                break;
            case "buchung":
                kachel.Art = "event";
                // gebuchter bzw. hinzugefügter Raum steht in der grünen Plakette
                if (kachel.Raum.Length > 0) kachel.Geaendert.Add("room");
                if (kachel.Fach.Length == 0) kachel.Fach = k.Text.Length > 0 && k.Text != "Raumbuchung" ? k.Text : "Raumbuchung";
                break;
            case "vorgemerkt":
                kachel.Art = k.Art.StartsWith("Klausur", StringComparison.OrdinalIgnoreCase) ? "klausur" : "change";
                if (kachel.Fach.Length == 0) kachel.Fach = k.Text.Length > 0 ? k.Text : k.Art;
                break;
            default:
                kachel.Art = "change";
                break;
        }

        // Kopplungen: mehrere Lehrkräfte bzw. Klassen in derselben Stunde
        if (k.Lehrer.Count > 1)
            kachel.Kopplung = k.Lehrer.Select(w => w.Neu && w.K.Length == 0 ? new KoppEintrag(w.Alt ?? "", "absent")
                : w.Neu && !string.IsNullOrEmpty(w.Alt) ? new KoppEintrag(w.K, "sub", w.Alt) : new KoppEintrag(w.K, "ok")).Where(t => t.Code.Length > 0).ToList();
        if (k.Klassen.Count > 1)
            kachel.Klassenliste = k.Klassen.Where(w => w.K.Length > 0).Select(w => new KlassenEintrag(w.K)).ToList();
        return kachel;
    }

    /// <summary>Veranstaltung (Exkursion, Konferenz, Projekttag …) als grün gerahmtes Feld.</summary>
    private static Kachel Veranstaltung(string datum, int von, int bis, string titel, IEnumerable<string> klassen, IEnumerable<string> lehrer, IEnumerable<string> raeume, string? farbe = null) => new()
    {
        Stunde = von, Dauer = Math.Max(1, bis - von + 1), Datum = datum, Art = "absence", Feld = true, Fach = titel, Info = titel, Typ = "Veranstaltung",
        Klasse = string.Join(",", klassen.Where(x => x.Length > 0)), Lehrkraft = string.Join(",", lehrer.Where(x => x.Length > 0)),
        Raum = string.Join(",", raeume.Where(x => x.Length > 0)), Farbe = string.IsNullOrWhiteSpace(farbe) ? null : farbe,
    };

    /// <summary>
    /// Bereitschaft im Lehrerplan: Hält die Lehrkraft in einer Stunde der Bereitschaft eine Vertretung, entfällt nur
    /// diese Stunde; der Rest des Blocks bleibt als Bereitschaft stehen.
    /// </summary>
    private static List<Kachel> BereitschaftenKuerzen(List<Kachel> kacheln, List<PlanKarte> karten)
    {
        var belegt = karten.Where(k => k.Status is "geaendert" or "klausur" or "regulaer" && !Ci.Equals(k.Fach, "BER"))
            .SelectMany(k => Enumerable.Range(k.Von, Math.Max(1, k.Bis - k.Von + 1))).ToHashSet();
        var res = new List<Kachel>();
        foreach (var k in kacheln)
        {
            if (k.Art != "standby" || !Enumerable.Range(k.Stunde, k.Dauer).Any(belegt.Contains)) { res.Add(k); continue; }
            Kachel? teil = null;
            for (var s = k.Stunde; s < k.Stunde + k.Dauer; s++)
            {
                if (belegt.Contains(s)) { if (teil != null) res.Add(teil); teil = null; continue; }
                if (teil == null) { teil = k.Kopie(); teil.Stunde = s; teil.Dauer = 1; }
                else teil.Dauer++;
            }
            if (teil != null) res.Add(teil);
        }
        return res;
    }

    /// <summary>Pausenaufsicht als Pille in der Pause nach der Stunde (vor der ersten Stunde: über dem Raster).</summary>
    public static Kachel Aufsicht(PlanAufsicht a, IReadOnlyList<PlanZeit> zeiten)
    {
        var k = new Kachel
        {
            Stunde = Math.Max(1, a.NachStunde), Dauer = 1, Datum = a.Datum, Art = "supervision", Typ = "Pausenaufsicht",
            Fach = a.GangName.Length > 0 ? a.GangName : a.Gang.Length > 0 ? a.Gang : "Aufsicht",
            Aufsicht = true, Pause = true, Von = a.Beginn, Bis = a.Ende,
        };
        if (a.NachStunde <= 0 && zeiten.Count > 0)
        {
            k.VorErsterStunde = true;
            k.Von = Minus(zeiten[0].Beginn, 15);
            k.Bis = zeiten[0].Beginn;
        }
        else if (string.IsNullOrEmpty(a.Ende) && !string.IsNullOrEmpty(a.Beginn)) k.Bis = Minus(a.Beginn, -15);
        if (a.Status == "vertreten")
        {
            k.AufsichtAbgegeben = true;
            k.Lehrkraft = a.Lehrer is "???" or "" ? "" : a.Lehrer;
            k.LehrkraftAlt = a.Alt ?? "";
            k.Geaendert.Add("teacher");
            k.Info = k.Lehrkraft.Length == 0 ? "Vertretung offen" : $"Vertretung: {k.Lehrkraft}";
        }
        else if (a.Status == "vertretung")
        {
            k.AufsichtUebernommen = true;
            k.Lehrkraft = a.Lehrer;
            k.LehrkraftAlt = a.Alt ?? "";
            k.Info = $"Vertretung für {a.Alt}";
        }
        else k.Lehrkraft = a.Lehrer;
        return k;
    }

    /// <summary>Ganztägiges Feld für Ferien, Feiertage und unterrichtsfreie Tage: Art, Name, Zeitraum.</summary>
    public static Kachel Ferienfeld(PlanTag tag, IReadOnlyList<PlanZeit> zeiten, IReadOnlyList<Ferienzeit> ferien)
    {
        var titel = tag.FreiArt switch { "ferien" => "Ferien", "feiertag" => "Feiertag", _ => "Unterrichtsfrei" };
        var name = string.IsNullOrWhiteSpace(tag.FreiGrund) || Ci.Equals(tag.FreiGrund, titel) ? "" : tag.FreiGrund!;
        var datum = DateOnly.Parse(tag.Datum, CultureInfo.InvariantCulture);
        bool Enthaelt(Ferienzeit f) => DateOnly.TryParse(f.Von, CultureInfo.InvariantCulture, out var v) && DateOnly.TryParse(f.Bis, CultureInfo.InvariantCulture, out var b) && datum >= v && datum <= b;
        var zeit = ferien.FirstOrDefault(f => Ci.Equals(f.Name, name) && Enthaelt(f)) ?? (name.Length == 0 ? null : ferien.FirstOrDefault(Enthaelt));
        var zeitraum = "";
        if (zeit != null)
        {
            var v = DateOnly.Parse(zeit.Von, CultureInfo.InvariantCulture);
            var b = DateOnly.Parse(zeit.Bis, CultureInfo.InvariantCulture);
            zeitraum = v == b ? v.ToString("dd.MM.", CultureInfo.InvariantCulture) : $"{v.ToString("dd.MM.", CultureInfo.InvariantCulture)} bis {b.ToString("dd.MM.", CultureInfo.InvariantCulture)}";
        }
        return new Kachel
        {
            Stunde = zeiten[0].Nr, Dauer = zeiten.Count, Datum = tag.Datum, Art = "holiday", Ferien = true, Ganztaegig = true,
            // Weiches Trennzeichen vor „ferien“: in der schmalen Wochenspalte „Herbst-ferien“ statt „Herbstfer-ien“
            Fach = titel, FerienName = System.Text.RegularExpressions.Regex.Replace(name, @"(?<=\p{L}{3})ferien\b", "\u00ADferien"), FerienZeitraum = zeitraum,
        };
    }

    private static string Werte(IEnumerable<PlanWert> w) => string.Join(",", w.Select(x => x.K).Where(x => x.Length > 0));
    private static string Alte(IEnumerable<PlanWert> w) => string.Join(",", w.Where(x => x.Neu && !string.IsNullOrEmpty(x.Alt)).Select(x => x.Alt!));

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Uhrzeit „HH:mm“ minus Minuten (negativ: plus).</summary>
    public static string Minus(string hhmm, int minuten)
    {
        if (!TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return hhmm;
        return t.AddMinutes(-minuten).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Montag der Woche eines Datums.</summary>
    public static DateOnly Montag(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
