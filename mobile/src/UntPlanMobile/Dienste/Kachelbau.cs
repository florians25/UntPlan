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
/// <item>Kopplungen: UntPlanWeb fasst nur Stunden mit gleichem Fach und gleichem Raum zusammen – eine Karte wird eine Kachel,
/// gekoppelte Stunden mit unterschiedlichen Fächern bleiben einzelne Kacheln.</item>
/// <item>Notizen (für Schüler*innen und Lehrkräfte) und fällige Hausaufgaben setzen die Zeichen unten rechts.</item>
/// </list>
/// </summary>
public static class Kachelbau
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Planart von UntPlanWeb (klasse, lehrer, raum) → Planart der Oberfläche (class, teacher, room).</summary>
    /// <summary>Vertretungstext einer Vertretung ohne Lehrkraft, wenn kein anderer Text vorhanden ist (wie in UntPlanWeb).</summary>
    public const string OhneLehrkraftText = "Vertr. o. Lehrkr.";

    /// <summary>Lehrkraft einer Zusatzstunde, für die (noch) keine Lehrkraft eingeteilt ist – nur bei Zusatzstunden (Kachel und Details).</summary>
    public const string KeineLehrkraft = "---";

    /// <summary>
    /// Automatisch erzeugte Standardtexte einer Zusatzstunde („wegen Sondereinsatz“, „Vertretung durch Verlegung“, „Vorziehung“,
    /// „Vertr. o. Lehrkr.“, „aus Vertretung …“, „Zusatzunterricht“, „Sondereinsatz“, „Zusätzl. Unterrichtsstunde“). Sie entfallen –
    /// die Plakette „Zusätzl. Unterrichtsstunde“ sagt dasselbe; manuell eingegebene Vertretungstexte bleiben stehen.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex ZusatzStandardtext =
        new(@"^\s*(\(?\s*wegen\s+sondereins(a|ä)tz(e)?\s*\)?|sondereins(a|ä)tz(e)?|vertretung\s+durch\s+verlegung|vorziehung|vertr\.\s*o\.\s*lehrkr\.?|aus\s+vertretung\b.*|zusatzunterricht|zus(ä|ae)tzl(\.|iche)\s*unterrichtsstunde)\s*\.?\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Standardtext einer Zusatzstunde (leer, einer der festen Texte oder gleich der Vertretungsart)?</summary>
    public static bool IstZusatzStandardtext(string? text, string? art) =>
        string.IsNullOrWhiteSpace(text) || ZusatzStandardtext.IsMatch(text) || Ci.Equals(text.Trim(), (art ?? "").Trim());

    /// <summary>Vertretungsarten, die eine zusätzliche Stunde bedeuten (wie in UntPlanWeb: Marker „+“ statt Änderungsquadrat).</summary>
    private static readonly System.Text.RegularExpressions.Regex ZusatzArt =
        new("sondereinsatz|zusatz|verlegung|statt|mitbetreuung", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Zusatzstunde (zusätzlicher Unterricht): eine geänderte Stunde mit Fach, deren Vertretungsart eine zusätzliche Stunde
    /// bezeichnet (Sondereinsatz, Zusatzunterricht, Verlegung …) oder die ohne Unterricht aus dem Regelplan im Plan steht.
    /// </summary>
    public static bool IstKlassenabwesenheit(PlanKarte k) =>
        k.Status == "entfall" && KlassenabwesenheitArt.IsMatch(k.Art ?? "") && k.Klassen.Count > 0;

    /// <summary>Vertretungsarten, bei denen die Stunde wegen Abwesenheit der Klasse wegfällt (Freisetzung, Klassenabwesenheit).</summary>
    private static readonly System.Text.RegularExpressions.Regex KlassenabwesenheitArt =
        new(@"freisetz|klassenabwes|klasse\s+abwes", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Raumbuchung: eigene Buchungskarte oder ein zu einem Unterricht gebuchter bzw. hinzugefügter Raum.</summary>
    public static bool IstRaumbuchung(PlanKarte k) =>
        k.Status == "buchung" || (k.Art ?? "").StartsWith("Raumbuchung", StringComparison.OrdinalIgnoreCase)
        || (k.Art ?? "").StartsWith("Raumänderung (vorgemerkt)", StringComparison.OrdinalIgnoreCase);

    /// <summary>Texte, die eine Buchung als Änderung ausweisen würden („Raumbuchung“, „Raumänderung (vorgemerkt)“ …).</summary>
    private static bool IstBuchungstext(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (text.StartsWith("Raumbuchung", StringComparison.OrdinalIgnoreCase)
                                             || text.StartsWith("Raumänderung", StringComparison.OrdinalIgnoreCase)
                                             || text.StartsWith("Vertretung", StringComparison.OrdinalIgnoreCase));

    /// <summary>Raumvertretung (Raumänderung), für die noch kein neuer Raum eingeteilt ist.</summary>
    public static bool IstOffeneRaumvertretung(PlanKarte k) =>
        k.Status is "geaendert" or "offen" && System.Text.RegularExpressions.Regex.IsMatch(k.Art ?? "", "raum", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        && !(k.Art ?? "").Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase)
        && !k.Raeume.Any(w => w.Neu && w.K.Length > 0 && w.K != "???")
        // ein ausdrücklich entfernter Raum ({k:"", alt:"R2"}) neben einem verbleibenden Raum ist keine offene Raumvertretung
        && !(k.Raeume.Any(w => w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt)) && k.Raeume.Any(w => w.K.Length > 0 && w.K != "???"));

    /// <summary>Zusatzstunde: siehe <see cref="IstZusatzstunde"/>. Klassenabwesenheit: siehe <see cref="IstKlassenabwesenheit"/>.</summary>
    public static bool IstZusatzstunde(PlanKarte k) =>
        k.Zusaetzlich || (k.Status == "geaendert" && (k.Fach ?? "").Length > 0 && Verlegung(k) is null
                          && (ZusatzArt.IsMatch(k.Art ?? "") || k.Unterricht is null));

    /// <summary>Vertretungsart einer Verlegung (Verlegung, Wegverlegung, Statt-Vertretung).</summary>
    private static readonly System.Text.RegularExpressions.Regex VerlegungsArt =
        new("verleg|statt", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Verlegung: "von" = ursprüngliche Stunde (von hier weg verlegt), "nach" = Zielstunde (hierher verlegt), sonst null.
    /// UntPlanWeb liefert das Feld; ältere Server: aus der Vertretungsart („Verlegung“, „Statt-Vertretung“), nie bei Prüfungen.
    /// </summary>
    public static string? Verlegung(PlanKarte k)
    {
        if (k.Verlegung is "von" or "nach") return k.Verlegung;
        if (k.Zusaetzlich || !VerlegungsArt.IsMatch(k.Art ?? "") || k.Status is "klausur" or "regulaer" or "bereitschaft" or "buchung") return null;
        return k.Status == "entfall" || (k.Status == "abwesend" && !k.LehrerAbwesend) ? "von" : "nach";
    }

    /// <summary>
    /// Bereitschaft, die wegfällt, weil die Lehrkraft abwesend ist (UntPlanWeb: Status „abwesend“ mit Kennzeichen bereitschaft;
    /// ältere Server: Fach BER bzw. Art mit „Bereitschaft“, Status „abwesend“ oder „entfall“).
    /// </summary>
    public static bool IstBereitschaftAbwesend(PlanKarte k) =>
        (k.Bereitschaft || Ci.Equals(k.Fach ?? "", "BER") || (k.Art ?? "").Contains("Bereitschaft", StringComparison.OrdinalIgnoreCase))
        && k.Status is "abwesend" or "entfall";

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

    /// <summary>
    /// Notizen für Lehrkräfte gehören nur ins Kollegium: Für Schüler*innen und Eltern fällt der Text weg,
    /// und das Info-Zeichen steht nur noch, wenn es eine Notiz für Schüler*innen gibt.
    /// </summary>
    public static void NurSchuelerNotizen(Dictionary<string, List<Kachel>> tage)
    {
        foreach (var kachel in tage.Values.SelectMany(t => t))
        {
            if (kachel.Notiz is null && kachel.NotizLehrer is null) continue;
            kachel.NotizLehrer = null;
            kachel.Notiz = string.IsNullOrWhiteSpace(kachel.NotizSchueler) ? null : true;
        }
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
        foreach (var k in res) OhneSondereinsatz(k);
        return res;
    }

    /// <summary>Nur http- und https-Adressen werden als Link zum Online-Unterricht angeboten.</summary>
    public static string? OnlineLinkGueltig(string? link) =>
        Uri.TryCreate((link ?? "").Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : null;

    private static readonly System.Text.RegularExpressions.Regex SondereinsatzText =
        new(@"\(?\bSondereins(a|ä)tz(e)?\b\)?\s*[:–—-]?\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Die interne Vertretungsart „Sondereinsatz“ erscheint weder auf Kacheln noch in den Details (Text, Art, Titel).</summary>
    public static void OhneSondereinsatz(Kachel k)
    {
        static string Weg(string t) => SondereinsatzText.Replace(t, " ").Trim(' ', '·', ',', ';', '-', '–').Replace("  ", " ");
        if (SondereinsatzText.IsMatch(k.Info)) k.Info = Weg(k.Info);
        if (SondereinsatzText.IsMatch(k.Typ)) k.Typ = Weg(k.Typ);
        if (SondereinsatzText.IsMatch(k.Fach)) k.Fach = Weg(k.Fach);
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
            // Veranstaltungen bleiben ein durchgehender Block; die Aufsicht in der Pause tritt dahinter zurück
            // (Wochenansicht ausgeblendet, Tagesansicht als halbe Kachel daneben)
            if (k.Pause == true || k.Feld == true || k.Dauer < 2 || schnitte.Count == 0) { res.Add(k); continue; }
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
        karte = ZusatzNormalisiert(karte);
        var kachel = AusKarteKern(karte, art);
        Zusaetze(kachel, karte);
        return kachel;
    }

    /// <summary>
    /// Verlegung (Ursprung und Ziel mit Text) und hinzugekommene Werte („+“-Labels). Die Marker des Servers (oben rechts) bleiben
    /// unbeachtet: Die Karten der App tragen oben rechts nie ein Zeichen.
    /// </summary>
    private static void Zusaetze(Kachel kachel, PlanKarte k)
    {
        if (kachel.Verlegung != null)
        {
            kachel.VerlegungVon = Verlegungsseite(k.VerlegungVon);
            kachel.VerlegungNach = Verlegungsseite(k.VerlegungNach);
            kachel.VerlegungText = VerlegungText(k.VerlegungVon, k.VerlegungNach);
        }
        static string? Plus(IEnumerable<PlanWert>? w) =>
            w?.Where(x => x.Plus && x.K.Length > 0 && x.K != "???").Select(x => x.K).Distinct(Ci).ToList() is { Count: > 0 } l ? string.Join(",", l) : null;
        // Veranstaltungen zeigen keine Plaketten – dort auch kein „+“
        if (kachel.Feld == true) return;
        kachel.KlassePlus = Plus((k.Klassen ?? new()).Concat(k.Mitklassen ?? new()));
        kachel.LehrkraftPlus = Plus(k.Lehrer);
        kachel.RaumPlus = kachel.RaumOffen == true ? null : Plus(k.Raeume);
    }

    private static KachelVerlegung? Verlegungsseite(PlanVerlegung? v) =>
        v is null || string.IsNullOrWhiteSpace(v.Datum) ? null : new KachelVerlegung(v.Datum, v.Beginn ?? "", v.Ende ?? "", VerlegungSeite(v) ?? "");

    /// <summary>Eine Seite der Verlegung: „09.10.2026 | 11:30 - 13:00“ (ohne Ende nur der Beginn, ohne Zeit nur das Datum).</summary>
    public static string? VerlegungSeite(PlanVerlegung? v)
    {
        if (v is null || !DateOnly.TryParse(v.Datum, CultureInfo.InvariantCulture, out var d)) return null;
        var zeit = string.IsNullOrWhiteSpace(v.Ende) ? v.Beginn ?? "" : $"{v.Beginn} - {v.Ende}";
        var datum = d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(zeit) ? datum : $"{datum} | {zeit}";
    }

    /// <summary>
    /// Verlegung als Text, immer Ursprung → Ziel: „09.10.2026 | 11:30 - 13:00 → 13.10.2026 | 12:15 - 13:00“ (ohne Ende nur der Beginn).
    /// Fehlt eine Seite, gibt es keinen Text (dann nur das Zeichen).
    /// </summary>
    public static string? VerlegungText(PlanVerlegung? von, PlanVerlegung? nach)
    {
        var a = VerlegungSeite(von);
        var b = VerlegungSeite(nach);
        return a is null || b is null ? null : $"{a} → {b}";
    }

    /// <summary>
    /// Lehrkräfte einer Zusatzstunde: alle Beteiligten je einmal als hinzugekommener Wert („+CS“, „+GA“, „+SN“) – auch ein als ersetzt
    /// gemeldeter alter Wert; nie durchgestrichen, nie „alt | neu“. „---“ bzw. „???“ zählen nicht (ohne Lehrkraft bleibt die Liste leer).
    /// </summary>
    public static List<PlanWert> ZusatzLehrer(IEnumerable<PlanWert> lehrer) =>
        lehrer.SelectMany(w => new[] { w.K, w.Alt ?? "" }).Select(x => x.Trim())
            .Where(x => x.Length > 0 && x is not (KeineLehrkraft or "???" or "+" or "+++")).Distinct(Ci)
            .Select(x => new PlanWert(x, null, true, true)).ToList();

    /// <summary>
    /// Zusatzstunde: alle beteiligten Lehrkräfte mit „+“-Label (keine Durchstreichung, kein „alt | neu“) – nicht bei einer Veranstaltung
    /// und nicht bei einer Vertretung ohne bzw. noch ohne Lehrkraft (dort steht die bisherige Lehrkraft durchgestrichen im Label).
    /// </summary>
    public static PlanKarte ZusatzNormalisiert(PlanKarte k)
    {
        if (k.Status != "geaendert" || !IstZusatzstunde(k) || (k.Fach ?? "").Length == 0 || k.VertretungOffen
            || k.Lehrer.Any(w => w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt)))
            return k;
        return k with { Lehrer = ZusatzLehrer(k.Lehrer) };
    }

    private static Kachel AusKarteKern(PlanKarte karte, string art)
    {
        var k = karte with { Text = karte.Text ?? "", Art = karte.Art ?? "", Fach = karte.Fach ?? "" };
        // Offene Raumvertretung („???“ bzw. Raumänderung ohne neuen Raum): wie eine offene Lehrervertretung – bisheriger Raum
        // durchgestrichen in der Plakette
        var raumOffen = k.Raeume.Any(w => w.K == "???") || IstOffeneRaumvertretung(k);
        if (raumOffen)
        {
            var alt = k.Raeume.Select(w => w.K == "???" ? w.Alt ?? "" : !string.IsNullOrEmpty(w.Alt) ? w.Alt! : w.K).Where(x => x.Length > 0 && x != "???").Distinct(Ci).ToList();
            k = k with { Raeume = alt.Count > 0 ? alt.Select(x => new PlanWert("", x, true)).ToList() : new List<PlanWert>() };
        }
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
            Verlegung = Verlegung(k),
        };
        // Notizen für Schüler*innen und Lehrkräfte sowie fällige Hausaufgaben: Zeichen unten rechts auf der Kachel
        if (!string.IsNullOrWhiteSpace(k.NotizSchueler) || !string.IsNullOrWhiteSpace(k.NotizLehrer))
        {
            kachel.Notiz = true;
            kachel.NotizSchueler = string.IsNullOrWhiteSpace(k.NotizSchueler) ? null : k.NotizSchueler;
            kachel.NotizLehrer = string.IsNullOrWhiteSpace(k.NotizLehrer) ? null : k.NotizLehrer;
        }
        if (k.HausaufgabeFaellig) kachel.Hausaufgabe = true;
        if (k.Online && k.Status is not ("entfall" or "abwesend"))
        {
            kachel.Online = true;
            kachel.OnlineLink = OnlineLinkGueltig(k.OnlineLink);
        }
        var raumNeu = k.Raeume.Where(w => w.Neu && w.K.Length > 0 && w.K != "???" && !Ci.Equals(w.Alt ?? "", w.K)).Select(w => w.K).Distinct(Ci).ToList();
        if (raumNeu.Count > 0 && !raumOffen) kachel.RaumNeu = string.Join(",", raumNeu);
        // Entfernter Raum neben verbleibenden Räumen ({k:"", alt:"R2"} neben „R1“): durchgestrichen in der grünen Plakette, der
        // verbleibende Raum bleibt ohne Label (roomOrig allein sähe aus wie „R2 → R1“)
        var raumEntfernt = k.Raeume.Where(w => w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt) && w.Alt != "???").Select(w => w.Alt!)
            .Where(a => !k.Raeume.Any(x => Ci.Equals(x.K, a))).Distinct(Ci).ToList();
        if (!raumOffen && raumEntfernt.Count > 0 && k.Raeume.Any(w => w.K.Length > 0 && w.K != "???")) kachel.RaumEntfernt = string.Join(",", raumEntfernt);
        if (raumOffen)
        {
            kachel.RaumOffen = true;
            if (!kachel.Geaendert.Contains("room")) kachel.Geaendert.Add("room");
        }
        // Nur tatsächlich geänderte Werte bekommen ein grünes Label: „neu“ mit unverändertem Wert (alt == k) zählt nicht
        // (z. B. bei einer reinen Raumänderung keine Plakette an der Lehrkraft)
        static bool Geaendert(PlanWert w) => w.Neu && (w.K.Length == 0 || string.IsNullOrEmpty(w.Alt) || !Ci.Equals(w.Alt, w.K));
        if (k.Klassen.Any(Geaendert)) kachel.Geaendert.Add("class");
        if (k.Lehrer.Any(Geaendert)) kachel.Geaendert.Add("teacher");
        // geänderter oder hinzugefügter Raum (neuer Wert ohne alten) steht in der grünen Plakette
        if (k.Raeume.Any(w => w.Neu || (!string.IsNullOrEmpty(w.Alt) && !Ci.Equals(w.Alt, w.K)))) kachel.Geaendert.Add("room");
        // Fachänderung: kein grünes Label und kein durchgestrichenes Fach – eine Fachänderung steht ohnehin als eigener (neuer) Block da;
        // das bisherige Fach (subjectOrig) nennen nur die Details
        // Raumänderung, die nur an der Art erkennbar ist (kein gekennzeichneter Wert): der Raum steht trotzdem in der grünen Plakette
        if (!kachel.Geaendert.Contains("room") && kachel.Raum.Length > 0 && IstRaumaenderungsArt(k.Art)) kachel.Geaendert.Add("room");
        var buchung = IstRaumbuchung(k);
        if (k.Art.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase) && !buchung)
            kachel.Info = string.Join(" · ", new[] { k.Art, k.Text }.Where(x => !string.IsNullOrWhiteSpace(x)));
        // Raumbuchungen tragen nie einen Vertretungs- oder Änderungstext
        if (buchung && IstBuchungstext(kachel.Info)) kachel.Info = "";

        // Offene Vertretung: UntPlanWeb kennzeichnet sie mit „vertretungOffen“; ohne bisherige Lehrkraft steht „???“
        var offen = k.VertretungOffen || k.Lehrer.Any(w => w.Neu && w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt));
        if (k.VertretungOffen && kachel.Lehrkraft.Length == 0 && kachel.LehrkraftAlt.Length == 0)
        {
            kachel.LehrkraftAlt = "???";
            if (!kachel.Geaendert.Contains("teacher")) kachel.Geaendert.Add("teacher");
        }
        // Vertretung ohne Lehrkraft („+++“): bisherige Lehrkraft durchgestrichen in der Plakette; ohne eigenen Text der Hinweis „Vertr. o. Lehrkr.“
        // (UntPlanWeb liefert ihn bereits, ältere Server nicht)
        if (k.Status == "geaendert" && k.Lehrer.Count > 0 && k.Lehrer.All(w => w.Neu && w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt)) && string.IsNullOrWhiteSpace(kachel.Info))
            kachel.Info = OhneLehrkraftText;
        // Bereitschaft einer abwesenden Lehrkraft: wie eine abgegebene Stunde (Eckklammern, Kürzel durchgestrichen) – kein Entfall
        if (IstBereitschaftAbwesend(k))
        {
            kachel.Art = "standby";
            kachel.BereitschaftAbwesend = true;
            kachel.LehrerAbwesend = true;
            kachel.LehrkraftAlt = string.Join(",", k.Lehrer.Select(w => w.K.Length > 0 ? w.K : w.Alt ?? "").Where(x => x.Length > 0).Distinct(Ci));
            kachel.Lehrkraft = "";
            kachel.Geaendert.Remove("teacher");
            return kachel;
        }
        // Im Raumplan ist eine abwesende Lehrkraft eine grüne Änderung wie im Klassenplan (abwesende Lehrkraft
        // durchgestrichen, Vertretung im grünen Label) – keine Eckklammern
        if (k.LehrerAbwesend && art != "room") kachel.LehrerAbwesend = true;
        if (art == "room" && k.Status is "abwesend" or "offen" && k.Lehrer.Count > 0 && !kachel.Geaendert.Contains("teacher")
            && k.Lehrer.Any(w => w.Neu || !string.IsNullOrEmpty(w.Alt)))
            kachel.Geaendert.Add("teacher");
        // Offene Vertretung: keine Lehrkraft eingeteilt, die bisherige steht durchgestrichen in der grünen Plakette
        if ((k.VertretungOffen && kachel.Lehrkraft.Length == 0)
            || (k.Status is "offen" or "abwesend" or "geaendert" && k.Lehrer.Count > 0
                && k.Lehrer.All(w => w.Neu && w.K.Length == 0 && !string.IsNullOrEmpty(w.Alt))))
            kachel.LehrkraftOffen = true;
        switch (k.Status)
        {
            case "regulaer":
                kachel.Art = "normal";
                break;
            case "geaendert" when k.Unterricht is null && k.Fach.Length == 0 && k.Text.Length > 0:
            case "veranstaltung":
                var veranstaltung = Veranstaltung(k.Datum, k.Von, k.Bis, k.Text.Length > 0 ? k.Text : k.Art, k.Klassen.Select(w => w.K), k.Lehrer.Select(w => w.K), k.Raeume.Select(w => w.K), k.Farbe);
                // Eingetragenes Fach (nicht die Bezeichnung) für den Kopf der Stundendetails
                var echtesFach = k.Fach.Trim();
                if (echtesFach.Length > 0 && !Ci.Equals(echtesFach, veranstaltung.Fach.Trim())) veranstaltung.VeranstaltungFach = echtesFach;
                return veranstaltung;
            case "geaendert":
                kachel.Art = "change";
                if (IstZusatzstunde(k))
                {
                    kachel.Zusatz = true;
                    // Zusätzliche Unterrichtsstunde: Plakette „Zusätzl. Unterrichtsstunde“; nur Standardtexte entfallen,
                    // ein manuell eingegebener Vertretungstext bleibt (Info-Zeichen auf der Kachel)
                    if (IstZusatzStandardtext(kachel.Info, k.Art)) kachel.Info = "";
                    // Zusatzstunde ohne Lehrkraft: „---“ statt einer leeren Zeile (nicht bei „+++“ – dort steht die bisherige Lehrkraft durchgestrichen)
                    if (k.Lehrer.All(w => w.K.Length == 0 && string.IsNullOrEmpty(w.Alt))) kachel.Lehrkraft = KeineLehrkraft;
                }
                break;
            case "offen":
            case "abwesend":
                kachel.Art = "change";
                kachel.Offen = offen;
                break;
            case "entfall" when IstKlassenabwesenheit(k):
                // Klasse abwesend: Entfall wie jede andere ausfallende Stunde, mit Hinweis „Klasse abwesend“ (und Grund)
                kachel.Art = "cancelled";
                if (string.IsNullOrWhiteSpace(kachel.Info)) kachel.Info = "Klasse abwesend";
                else if (!kachel.Info.Contains("abwesend", StringComparison.OrdinalIgnoreCase)) kachel.Info = $"Klasse abwesend – {kachel.Info}";
                break;
            case "entfall":
                kachel.Art = "cancelled";
                break;
            case "klausur" when IstNurRaumaenderung(k):
                // Eine Raumänderung ist nie eine Prüfung: grüne Änderung, neuer Raum in der grünen Plakette (nie gelb/braun)
                kachel.Art = "change";
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

        // Zusätzliche Unterrichtsstunde laut UntPlanWeb (auch bei offener Vertretung): Plakette; nur Standardtexte entfallen
        if (k.Zusaetzlich && kachel.Art is "change" or "normal")
        {
            kachel.Zusatz = true;
            if (IstZusatzStandardtext(kachel.Info, k.Art)) kachel.Info = "";
        }

        // Raumbuchung (auch zu einem Unterricht gebucht oder als zusätzlicher Raum): keine Vertretung – die Kachel bleibt
        // regulär, nur der gebuchte bzw. hinzugefügte Raum steht hervorgehoben. Ändert sich zugleich Lehrkraft, Klasse oder
        // Fach, bleibt es eine Vertretung.
        if (buchung && kachel.Art is "change" or "event" && kachel.Geaendert.All(g => g == "room") && !kachel.Offen)
        {
            kachel.Art = "normal";
            kachel.Zusatz = null;
        }

        // Kopplungen: mehrere Lehrkräfte bzw. Klassen in derselben Stunde
        if (k.Lehrer.Count > 1)
            kachel.Kopplung = k.Lehrer.Select(w => w.Neu && w.K.Length == 0 ? new KoppEintrag(string.IsNullOrEmpty(w.Alt) ? "???" : w.Alt, "absent", Offen: true)
                : w.Neu && !string.IsNullOrEmpty(w.Alt) ? new KoppEintrag(w.K, "sub", w.Alt)
                // hinzugekommene Lehrkraft: grünes Label mit „+“
                : w.Plus ? new KoppEintrag(w.K, "sub", Plus: true)
                : new KoppEintrag(w.K, "ok")).Where(t => t.Code.Length > 0).ToList();
        // Koppelklassen: alle Klassen der Stunde (eigene aus klassen[], die übrigen aus mitklassen[]); eine abwesende Klasse
        // ({k:"", alt:"INF13"}) steht durchgestrichen in der grünen Plakette
        var alleKlassen = k.Mitklassen is { Count: > 0 } ? k.Klassen.Concat(k.Mitklassen).ToList() : k.Klassen;
        if (alleKlassen.Count > 1)
        {
            kachel.Klassenliste = alleKlassen
                .Select(w => w.K.Length > 0 ? new KlassenEintrag(w.K, Plus: w.Plus ? true : null) : !string.IsNullOrEmpty(w.Alt) ? new KlassenEintrag(w.Alt!, "absent") : null)
                .Where(e => e != null).Select(e => e!).DistinctBy(e => e.Name, Ci).ToList();
            // Im Plan der anderen (anwesenden) Klasse wird die Stunde grün – wie bei einer abwesenden Lehrkraft
            if (art == "class" && kachel.Art is "normal" or "change" && kachel.Klassenliste.Any(e => e.Zustand == "absent") && kachel.Klassenliste.Any(e => e.Zustand == "ok"))
            {
                kachel.Art = "change";
                if (!kachel.Geaendert.Contains("class")) kachel.Geaendert.Add("class");
            }
        }
        return kachel;
    }

    /// <summary>Art einer reinen Raumänderung (Raumänderung, Raumvertretung, Raumwechsel).</summary>
    /// <summary>Als „klausur“ gemeldet, die Art nennt aber nur einen Raumwechsel und nirgends eine Klausur oder Prüfung.</summary>
    public static bool IstNurRaumaenderung(PlanKarte k) => IstRaumaenderungsArt(k.Art) && !NenntPruefung(k.Art) && !NenntPruefung(k.Text);

    private static bool NenntPruefung(string? s) => !string.IsNullOrEmpty(s) &&
        new[] { "klausur", "prüfung", "pruefung", "leistungs", "arbeit", "test" }.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static bool IstRaumaenderungsArt(string? art) => !string.IsNullOrEmpty(art) &&
        (art.Contains("raumänderung", StringComparison.OrdinalIgnoreCase) || art.Contains("raumvertretung", StringComparison.OrdinalIgnoreCase)
         || art.Contains("raumwechsel", StringComparison.OrdinalIgnoreCase));

    /// <summary>Werte ohne Leere und ohne Doppelte (Groß-/Kleinschreibung egal), Reihenfolge bleibt.</summary>
    private static IEnumerable<string> Einmal(IEnumerable<string> werte, params string[] ohne) =>
        werte.Select(x => x.Trim()).Where(x => x.Length > 0 && !ohne.Contains(x)).Distinct(Ci);

    /// <summary>Veranstaltung (Exkursion, Konferenz, Projekttag …) als grün gerahmtes Feld. Jede Klasse, Lehrkraft und jeder Raum steht nur einmal.</summary>
    private static Kachel Veranstaltung(string datum, int von, int bis, string titel, IEnumerable<string> klassen, IEnumerable<string> lehrer, IEnumerable<string> raeume, string? farbe = null) => new()
    {
        Stunde = von, Dauer = Math.Max(1, bis - von + 1), Datum = datum, Art = "absence", Feld = true, Fach = titel, Info = titel, Typ = "Veranstaltung",
        Klasse = string.Join(",", Einmal(klassen)), Lehrkraft = string.Join(",", Einmal(lehrer, KeineLehrkraft, "???")),
        Raum = string.Join(",", Einmal(raeume)), Farbe = string.IsNullOrWhiteSpace(farbe) ? null : farbe,
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
        if (a.Status == "entfall")
        {
            // Entfallene Aufsicht: Entfall-Stil (rosa, Ort durchgestrichen), keine Kürzel auf der Kachel; Anlass in den Details
            k.AufsichtEntfall = true;
            k.Lehrkraft = a.Lehrer is "???" or "---" ? "" : a.Lehrer;
            k.Info = string.IsNullOrWhiteSpace(a.Grund) ? "Aufsicht entfällt" : $"Aufsicht entfällt – {a.Grund.Trim()}";
        }
        else if (a.Status == "vertreten")
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
    // Ursprungswerte ohne Doppelte: Bei einem hinzugefügten Raum tragen mehrere neue Räume denselben alten Raum
    private static string Alte(IEnumerable<PlanWert> w) => string.Join(",", w.Where(x => x.Neu && !string.IsNullOrEmpty(x.Alt)).Select(x => x.Alt!).Distinct(Ci));

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
