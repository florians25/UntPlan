using System.Globalization;
using System.Text;

namespace UntPlanWeb.Dienste;

/// <summary>
/// Krankmeldungen der Lehrkräfte an die Verteilerliste: viele Gründe, Teilausfälle, Folge- und Verlängerungsmeldungen, Nachreichen der
/// Arbeitsunfähigkeitsbescheinigung und Gesundmeldungen. Jede Lehrkraft schreibt in einem eigenen, gleichbleibenden Stil (Tonfall, Anrede,
/// Grußformel, Signatur); die Texte entstehen aus Bausteinen, sodass sich über Wochen keine Meldung wiederholt.
/// </summary>
public sealed partial class Schulsimulation
{
    /// <summary>
    /// Art einer Abwesenheit der Lehrkraft: Gewicht, Dauer in Schultagen (min, max), Teilausfall (<c>termin</c>: einzelne Stunden,
    /// <c>spaeter</c>: kommt erst später, <c>frueher</c>: geht früher; leer = ganze Tage), eigene Erkrankung oder Ausfall aus anderem Grund,
    /// Stichwort, Begründung mit „wegen …“, Sätze in der Ich-Form und ausführlichere Sätze.
    /// </summary>
    public sealed record Ausfallart(string Id, double Gewicht, int Min, int Max, string Modus, bool Selbst, string[] Kurz, string Wegen, string[] Saetze, string[] Lang)
    {
        /// <summary>Nur ein Teil des Tages (Arzttermin, später kommen, früher gehen).</summary>
        public bool Teil => Modus.Length > 0;
    }

    public static readonly Ausfallart[] Ausfallarten =
    {
        new("erkaeltung", 20, 1, 3, "", true, new[] { "Erkältung", "stark erkältet", "Halsschmerzen und Schnupfen", "keine Stimme" }, "einer Erkältung",
            new[] { "mich hat eine Erkältung erwischt", "ich habe mir eine starke Erkältung eingefangen", "ich bin stark erkältet und habe kaum Stimme", "ich habe Halsschmerzen und kaum noch Stimme" },
            new[] { "Seit gestern Abend kratzt der Hals, inzwischen habe ich kaum noch Stimme.", "Die Erkältung ist über Nacht deutlich schlimmer geworden.", "Ich habe es heute noch versucht, aber ohne Stimme ist Unterricht nicht möglich." }),
        new("fieber", 10, 1, 3, "", true, new[] { "Fieber", "hohes Fieber", "Fieber und Gliederschmerzen" }, "Fieber",
            new[] { "ich liege mit Fieber im Bett", "ich habe seit heute Nachmittag hohes Fieber", "ich habe Fieber und starke Gliederschmerzen" },
            new[] { "Das Fieber ist heute Abend auf über 39 Grad gestiegen.", "Seit dem Nachmittag habe ich Fieber, an Unterricht ist nicht zu denken.", "Mein Hausarzt hat mich bis auf Weiteres krankgeschrieben." }),
        new("infekt", 12, 3, 6, "", true, new[] { "grippaler Infekt", "Grippe", "Infekt" }, "eines grippalen Infekts",
            new[] { "ich habe einen grippalen Infekt", "mein Arzt hat mich wegen eines fieberhaften Infekts krankgeschrieben", "ich bin an einer Grippe erkrankt" },
            new[] { "Der Infekt hat mich ziemlich erwischt, die Ärztin rechnet mit einigen Tagen.", "Ich war heute beim Arzt, es ist ein ausgewachsener Infekt.", "Nach Fieber und Husten hat mich der Arzt für mehrere Tage krankgeschrieben." }),
        new("magen", 11, 1, 2, "", true, new[] { "Magen-Darm", "Magen-Darm-Infekt", "Magenprobleme" }, "eines Magen-Darm-Infekts",
            new[] { "ich habe mir einen Magen-Darm-Infekt eingefangen", "mir geht es seit heute Nachmittag sehr schlecht, vermutlich Magen-Darm", "ich habe einen Magen-Darm-Infekt" },
            new[] { "Seit der Nacht habe ich mit Magen-Darm zu kämpfen.", "Mir geht es seit dem Nachmittag sehr schlecht, ich vermute einen Magen-Darm-Infekt.", "Aus Rücksicht auf alle bleibe ich zu Hause, bis es vorbei ist." }),
        new("migraene", 6, 1, 1, "", true, new[] { "Migräne", "Migräneattacke" }, "einer Migräne",
            new[] { "ich habe eine starke Migräne", "ich habe eine heftige Migräneattacke", "mich hat eine Migräne erwischt" },
            new[] { "Die Migräne hat in der Nacht begonnen und ist noch nicht abgeklungen.", "Leider hat sich eine Migräneattacke angekündigt, die Medikamente wirken noch nicht." }),
        new("ruecken", 5, 2, 5, "", true, new[] { "Rücken", "Hexenschuss", "Rückenprobleme" }, "akuter Rückenbeschwerden",
            new[] { "ich habe mir den Rücken verrenkt und kann mich kaum bewegen", "ich habe einen Hexenschuss", "mein Rücken macht nicht mit" },
            new[] { "Beim Tragen einer Kiste ist es mir in den Rücken geschossen.", "Ich kann mich seit heute Morgen kaum bewegen, die Orthopädin hat mich krankgeschrieben." }),
        new("corona", 4, 3, 5, "", true, new[] { "Corona positiv", "positiver Coronatest", "Corona-Verdacht" }, "einer Coronainfektion",
            new[] { "mein Coronatest ist heute positiv", "ich habe Symptome und ein positives Testergebnis auf Corona", "ich habe Corona-Verdacht und bleibe vorsorglich zu Hause" },
            new[] { "Nach Halsschmerzen am Nachmittag war der Schnelltest am Abend deutlich positiv.", "Ein Selbsttest war positiv, morgen lasse ich das in der Praxis bestätigen." }),
        new("kind", 8, 1, 2, "", false, new[] { "Kind krank", "krankes Kind", "Kinderkrankentag" }, "der Erkrankung meines Kindes",
            new[] { "mein Sohn ist krank und ich muss ihn betreuen", "meine Tochter hat Fieber und kann nicht in die Kita", "ich muss mein krankes Kind betreuen", "unser Kleiner ist krank und die Betreuung ist nicht anders zu organisieren" },
            new[] { "Unser Kind ist heute Nacht mit Fieber aufgewacht, mein Partner ist beruflich unterwegs.", "Die Betreuung lässt sich leider nicht anders regeln, ich nehme einen Kinderkrankentag." }),
        new("kita", 3, 1, 1, "frueher", false, new[] { "Kita ruft an", "Kind abholen" }, "der Betreuung meines Kindes",
            new[] { "die Kita hat gerade angerufen, mein Kind hat Fieber und muss abgeholt werden", "die Kita schließt heute wegen Personalmangels früher", "die Schule meiner Tochter hat angerufen, ich muss sie abholen" },
            new[] { "Ich habe es eben erst erfahren und versuche, die betroffenen Klassen noch zu informieren." }),
        new("zahn", 3, 1, 1, "spaeter", true, new[] { "Zahnarzt", "Zahnarzt-Notfall" }, "eines zahnärztlichen Notfalls",
            new[] { "ich habe einen Zahnarzt-Notfall und bekomme erst am Morgen einen Termin", "mir ist eine Füllung herausgebrochen, ich muss morgens zum Zahnarzt", "ich habe starke Zahnschmerzen und gehe gleich zum Notdienst" },
            new[] { "Die Schmerzen haben in der Nacht angefangen, die Praxis nimmt mich gleich zu Beginn dran." }),
        new("arzt", 5, 1, 1, "termin", true, new[] { "Arzttermin", "Untersuchung" }, "eines Arzttermins",
            new[] { "ich habe einen dringenden Arzttermin", "ich muss zu einer Untersuchung ins Krankenhaus", "ich habe einen Termin zur Nachuntersuchung" },
            new[] { "Den Termin habe ich kurzfristig bekommen und möchte ihn nicht verschieben." }),
        new("facharzt", 3, 1, 1, "termin", true, new[] { "Facharzt", "Facharzttermin" }, "eines Facharzttermins",
            new[] { "ich habe einen Facharzttermin, der sich nicht verschieben ließ", "ich habe endlich einen Termin beim Facharzt bekommen", "ich muss zu einer fachärztlichen Untersuchung" },
            new[] { "Auf diesen Termin warte ich seit Wochen, eine Verlegung würde wieder Monate dauern." }),
        new("pflege", 2, 1, 3, "", false, new[] { "Pflegefall in der Familie", "familiärer Notfall" }, "eines Pflegefalls in der Familie",
            new[] { "mein Vater ist gestürzt und ich muss mich um ihn kümmern", "in meiner Familie gibt es einen akuten Pflegefall", "meine Mutter kommt aus dem Krankenhaus und ich muss die Pflege organisieren" },
            new[] { "Ich muss mich in den nächsten Tagen um die Versorgung kümmern, bis der Pflegedienst übernimmt." }),
        new("unfall", 2, 3, 10, "", true, new[] { "Unfall", "Sturz" }, "eines Unfalls",
            new[] { "ich hatte einen Fahrradunfall und bin krankgeschrieben", "ich bin gestürzt und habe mir das Handgelenk verletzt", "ich bin umgeknickt, das Sprunggelenk ist stark geschwollen" },
            new[] { "Es ist zum Glück nichts gebrochen, aber ich darf das Bein vorerst nicht belasten.", "In der Notaufnahme wurde eine Bänderverletzung festgestellt." }),
        new("dienstunfall", 1, 2, 8, "", true, new[] { "Dienstunfall", "Unfall auf dem Schulweg" }, "eines Dienstunfalls",
            new[] { "ich hatte auf dem Weg zur Schule einen Unfall", "ich habe mich bei der Pausenaufsicht verletzt", "ich bin auf der Klassenfahrt gestürzt und habe mich verletzt" },
            new[] { "Die Unfallanzeige habe ich im Sekretariat angestoßen.", "Ein Durchgangsarzt hat mich krankgeschrieben." }),
        new("op", 2, 10, 25, "", true, new[] { "Operation", "OP" }, "einer Operation",
            new[] { "ich muss mich leider einer Operation unterziehen", "ich werde operiert und falle danach für einige Zeit aus", "die geplante Operation ist kurzfristig vorgezogen worden" },
            new[] { "Der Eingriff ist geplant, danach rechne ich mit einigen Wochen Genesung.", "Nach der Operation bin ich voraussichtlich mehrere Wochen krankgeschrieben." }),
        new("reha", 1, 15, 30, "", true, new[] { "Reha", "Rehabilitation" }, "einer Rehabilitationsmaßnahme",
            new[] { "meine Reha ist bewilligt worden und beginnt kurzfristig", "ich trete eine Rehabilitationsmaßnahme an", "nach meinem Bandscheibenvorfall beginnt jetzt die Reha" },
            new[] { "Die Maßnahme dauert voraussichtlich drei bis vier Wochen.", "Die Klinik hat einen frei gewordenen Platz angeboten, den ich annehmen möchte." }),
    };

    /// <summary>Schreibstil einer Lehrkraft: 0 knapp, 1 locker, 2 sachlich, 3 förmlich, 4 ausführlich; Signaturform; Kürzel in der Signatur.</summary>
    public sealed record Schreibstil(int Ton, int Signatur, int Anrede, int Gruss);

    public static readonly string[] Tonarten = { "knapp", "locker", "sachlich", "förmlich", "ausführlich" };

    /// <summary>Stil der Lehrkraft (für dieselbe Lehrkraft immer gleich).</summary>
    public static Schreibstil StilDer(long seed, string lehrer)
    {
        var z = Zufall.Fuer(seed, "stil", lehrer);
        var ton = z.Gewichtet(new[] { (0, 22.0), (1, 20.0), (2, 30.0), (3, 13.0), (4, 15.0) }, x => x.Item2).Item1;
        return new(ton, z.Zahl(0, 5), z.Zahl(0, 9), z.Zahl(0, 9));
    }

    private static readonly string[][] AnredeJeTon =
    {
        new[] { "Hallo,", "Moin,", "Hallo zusammen,", "", "Hi," },
        new[] { "Hallo zusammen,", "Hallo ihr Lieben,", "Liebes Vertretungsteam,", "Hi zusammen,", "Hallo liebes Vertretungsteam," },
        new[] { "Hallo zusammen,", "Liebe Kolleginnen und Kollegen,", "Liebes Vertretungsteam,", "Guten Tag,", "Liebe Schulleitung," },
        new[] { "Sehr geehrte Schulleitung,", "Sehr geehrte Damen und Herren,", "Liebe Schulleitung,", "Guten Tag," },
        new[] { "Liebe Kolleginnen und Kollegen,", "Liebes Vertretungsteam,", "Liebe Schulleitung, liebes Vertretungsteam,", "Hallo zusammen," },
    };

    private static readonly string[][] GruesseJeTon =
    {
        new[] { "VG", "LG", "Gruß", "Viele Grüße", "Danke!" },
        new[] { "Liebe Grüße", "LG und danke euch!", "Danke euch und bis bald", "Viele Grüße und bis bald", "Liebe Grüße und danke fürs Einspringen" },
        new[] { "Viele Grüße", "Beste Grüße", "Freundliche Grüße", "Danke und viele Grüße" },
        new[] { "Mit freundlichen Grüßen", "Freundliche Grüße", "Mit besten Grüßen" },
        new[] { "Vielen Dank fürs Organisieren und viele Grüße", "Herzliche Grüße", "Danke für eure Unterstützung und viele Grüße", "Herzliche Grüße und danke im Voraus" },
    };

    private static string Gross(string t) => t.Length == 0 ? t : char.ToUpper(t[0], CultureInfo.GetCultureInfo("de-DE")) + t[1..];

    private static string Name(Stand s, string lehrer, Zufall z) =>
        s.Lehrer.TryGetValue(lehrer, out var l) && l.Vorname.Length > 0 ? z.Chance(0.55) ? l.Vorname : $"{l.Vorname} {l.Name}" : lehrer;

    /// <summary>Signatur in der festen Form der Lehrkraft.</summary>
    private static string Signatur(Stand s, string lehrer, Schreibstil st)
    {
        if (!s.Lehrer.TryGetValue(lehrer, out var l) || l.Vorname.Length == 0) return lehrer;
        return (st.Ton, st.Signatur % 4) switch
        {
            (0, 0 or 1) => l.Vorname,
            (0, _) => lehrer,
            (1, _) => st.Signatur % 2 == 0 ? l.Vorname : $"{l.Vorname} ({lehrer})",
            (3, 0 or 1) => $"{l.Vorname} {l.Name}",
            (3, _) => $"{l.Vorname} {l.Name}\nLehrkraft ({lehrer})",
            (_, 0) => $"{l.Vorname} {l.Name}",
            (_, 1) => $"{l.Vorname} {l.Name} ({lehrer})",
            (_, 2) => $"{l.Vorname[0]}. {l.Name}",
            _ => l.Vorname,
        } + (st.Signatur == 5 ? "\n\nVon meinem Smartphone gesendet" : "");
    }

    private string AnredeVon(Schreibstil st, Zufall z)
    {
        var h = _uhr.Jetzt.Hour;
        var liste = AnredeJeTon[st.Ton];
        // meist die gewohnte Anrede, manchmal eine andere bzw. eine zur Tageszeit
        if (st.Ton is 1 or 2 or 4 && z.Chance(0.25)) return h >= 18 ? "Guten Abend," : h < 9 ? "Guten Morgen," : z.Wahl(liste);
        return z.Chance(0.7) ? liste[st.Anrede % liste.Length] : z.Wahl(liste);
    }

    private static string GrussVon(Schreibstil st, Zufall z)
    {
        var liste = GruesseJeTon[st.Ton];
        return z.Chance(0.7) ? liste[st.Gruss % liste.Length] : z.Wahl(liste);
    }

    /// <summary>Text mit Anrede, Absätzen, Gruß und Signatur zusammensetzen (ohne Anrede beginnt der Text groß).</summary>
    private string Brief(Stand s, string lehrer, Schreibstil st, Zufall z, IEnumerable<string> absaetze)
    {
        var anrede = AnredeVon(st, z);
        var teile = absaetze.Where(a => a.Length > 0).ToList();
        if (teile.Count == 0) teile.Add("");
        if (anrede.Length == 0) teile[0] = Gross(teile[0]);
        var sb = new StringBuilder();
        if (anrede.Length > 0) sb.Append(anrede).Append("\n\n");
        sb.Append(string.Join("\n\n", teile));
        sb.Append("\n\n").Append(GrussVon(st, z)).Append('\n').Append(Signatur(s, lehrer, st));
        return sb.ToString().Replace(".. ", ". ").Replace("..\n", ".\n").Replace(" ,", ",");
    }

    // ------------------------------------------------------------------------------------------ Ereignisse

    private IEnumerable<Ereignis> Meldungen(Stand s, Simulationseinstellungen e, Weltstand w, Ausfall a) =>
        Entwuerfe(s, e, w, a).Select(x => new Ereignis(x.Schluessel, x.Zeit, () => Senden(s, e, a.Lehrer, x.Text())));

    /// <summary>Alle Meldungen der Lehrkräfte im Zeitraum, wie sie verschickt würden (Lehrkraft, Zeitpunkt, Betreff, Text), ohne sie zu senden.</summary>
    public List<(string Lehrer, DateTime Zeit, string Betreff, string Text)> Krankmeldungsvorschau(Stand s, Simulationseinstellungen e, DateTime von, DateTime bis)
    {
        var w = Welt(s, e, von, bis);
        var res = new List<(string, DateTime, string, string)>();
        foreach (var a in w.Ausfaelle)
            foreach (var x in Entwuerfe(s, e, w, a))
                using (_uhr.Simuliert(x.Zeit))
                {
                    var (b, t) = x.Text();
                    res.Add((a.Lehrer, x.Zeit, b, t));
                }
        return res;
    }

    private IEnumerable<(string Schluessel, DateTime Zeit, Func<(string, string)> Text)> Entwuerfe(Stand s, Simulationseinstellungen e, Weltstand w, Ausfall a)
    {
        var z = Zufall.Fuer(w.Seed, "km", a.Lehrer, a.Von);
        var startZeit = DateTime.TryParse(e.Start, Inv, DateTimeStyles.None, out var st) ? st.AddMinutes(1) : DateTime.MinValue;
        var basis = $"km|{a.Lehrer}|{a.Von:yyyy-MM-dd}";
        // Erste Meldung: Termine einige Tage vorher; später kommen am Morgen selbst; früher gehen im Lauf des Vormittags;
        // sonst meist am Nachmittag bzw. Abend davor, manchmal nachts bzw. früh morgens
        DateTime zeit;
        if (a.Art.Modus == "termin") zeit = Um(a.Von.AddDays(-z.Zahl(1, 5)), z.Zahl(14 * 60, 20 * 60 + 30));
        else if (a.Art.Modus == "spaeter") zeit = Um(a.Von, z.Zahl(5 * 60 + 40, 7 * 60 + 15));
        else if (a.Art.Modus == "frueher") zeit = Um(a.Von, z.Zahl(7 * 60 + 30, 9 * 60 + 30));
        else
        {
            var vortag = a.Von.AddDays(-1);
            zeit = z.Gewichtet(new[] { (1, 44.0), (2, 22.0), (3, 11.0), (4, 23.0) }, x => x.Item2).Item1 switch
            {
                1 => Um(vortag, z.Zahl(17 * 60, 22 * 60 + 30)),
                2 => Um(vortag, z.Zahl(13 * 60, 17 * 60)),
                3 => z.Chance(0.5) ? Um(vortag, z.Zahl(22 * 60 + 30, 23 * 60 + 59)) : Um(a.Von, z.Zahl(0, 4 * 60 + 30)),
                _ => Um(a.Von, z.Zahl(5 * 60 + 15, 7 * 60 + 15)),
            };
            // nach einem Wochenende bzw. Ferien: meist am letzten freien Tag (Sonntag bzw. letzter Ferientag) nachmittags oder abends, gelegentlich schon
            // am Samstag bzw. einige Tage vorher (Erkrankung seit Freitag), selten erst am Morgen
            if (!w.Schultag(vortag) && z.Chance(0.12))
            {
                var frei = vortag;
                while (!w.Schultag(frei.AddDays(-1)) && frei.AddDays(-1) >= vortag.AddDays(-2)) frei = frei.AddDays(-1);
                zeit = Um(frei, z.Zahl(10 * 60, 20 * 60 + 30));
            }
            // geplante Operation bzw. Reha: einige Tage vorher angekündigt
            if (a.Art.Id is "op" or "reha" && z.Chance(0.6)) zeit = Um(a.Von.AddDays(-z.Zahl(3, 10)), z.Zahl(13 * 60, 20 * 60));
        }
        // lange Ausfälle: zuerst bis zum Ende der ersten Woche (bzw. bei OP und Reha gleich länger), dann Verlängerungen (Folgebescheinigungen)
        var abschnitt = a.Art.Id is "op" or "reha" ? 10 : 5;
        var lang = a.Tage.Count > abschnitt + 1;
        var ersteBis = lang ? a.Tage[abschnitt - 1] : a.Bis;
        yield return (basis + "|start", Max(zeit, startZeit), () => Krankmeldung(s, w, a, ersteBis, Zufall.Fuer(w.Seed, "km", a.Lehrer, a.Von, "text")));
        if (lang)
            for (var i = abschnitt; i < a.Tage.Count; i += abschnitt)
            {
                var bis = a.Tage[Math.Min(a.Tage.Count - 1, i + abschnitt - 1)];
                var zv = Zufall.Fuer(w.Seed, "kmv", a.Lehrer, a.Von, i);
                var tag = a.Tage[i - 1];
                // liegt ein Wochenende dazwischen, kommt die Folgebescheinigung oft erst am Samstag bzw. Sonntag
                var naechster = a.Tage[i];
                var wochenende = Enumerable.Range(1, Math.Max(0, naechster.DayNumber - tag.DayNumber - 1)).Select(n => tag.AddDays(n)).ToList();
                var zeitV = wochenende.Count > 0 && zv.Chance(0.45) ? Um(zv.Wahl(wochenende), zv.Zahl(10 * 60, 20 * 60 + 30)) : Um(tag, zv.Zahl(12 * 60, 19 * 60));
                yield return ($"{basis}|verl|{i}", Max(zeitV, startZeit), () => Verlaengerung(s, w, a, bis, Zufall.Fuer(w.Seed, "kmv", a.Lehrer, a.Von, i, "text")));
            }
        // Bescheinigung nachgereicht (bei mehrtägiger Erkrankung), Zwischenstand bei Corona
        var za = Zufall.Fuer(w.Seed, "kma", a.Lehrer, a.Von);
        // freie Tage (Wochenende) innerhalb des Ausfalls: Nachträge kommen dann oft am Samstag bzw. Sonntag
        var freiImAusfall = Enumerable.Range(1, Math.Max(0, a.Bis.DayNumber - a.Von.DayNumber - 1)).Select(n => a.Von.AddDays(n)).Where(t => !a.Tage.Contains(t)).ToList();
        DateOnly Nachtragstag() => freiImAusfall.Count > 0 && za.Chance(0.4) ? za.Wahl(freiImAusfall) : a.Tage[1];
        if (a.Art.Selbst && !a.Art.Teil && a.Tage.Count >= 3 && za.Chance(0.35))
            yield return ($"{basis}|au", Max(Um(Nachtragstag(), za.Zahl(9 * 60, 18 * 60)), startZeit), () => AuNachgereicht(s, w, a, Zufall.Fuer(w.Seed, "kma", a.Lehrer, a.Von, "au")));
        if (a.Art.Id == "corona" && a.Tage.Count >= 3 && za.Chance(0.6))
            yield return ($"{basis}|update", Max(Um(Nachtragstag(), za.Zahl(15 * 60, 20 * 60)), startZeit), () => CoronaZwischenstand(s, w, a, Zufall.Fuer(w.Seed, "kma", a.Lehrer, a.Von, "up")));
        if (a.Tage.Count >= 2 && z.Chance(0.5) || a.Tage.Count >= 8 && z.Chance(0.8))
        {
            var zg = Zufall.Fuer(w.Seed, "kmg", a.Lehrer, a.Von);
            // vor einem Wochenende bzw. Ferien: auch am Sonntag bzw. letzten Ferientag
            var wieder = w.Schultag(a.Bis.AddDays(1), 0);
            var tagG = wieder.DayNumber - a.Bis.DayNumber > 1 && zg.Chance(0.4) ? wieder.AddDays(-1) : a.Bis;
            yield return ($"{basis}|gesund", Max(Um(tagG, zg.Zahl(16 * 60, 21 * 60 + 30)), startZeit), () => Gesundmeldung(s, w, a, Zufall.Fuer(w.Seed, "kmg", a.Lehrer, a.Von, "text")));
        }
    }

    // ------------------------------------------------------------------------------------------ Texte

    /// <summary>Erste Meldung: Grund, Dauer bzw. betroffene Stunden, betroffene Klassen, Aufgaben und Hinweise im Stil der Lehrkraft.</summary>
    private (string, string) Krankmeldung(Stand s, Weltstand w, Ausfall a, DateOnly bis, Zufall z)
    {
        var st = StilDer(w.Seed, a.Lehrer);
        var tag = Tagwort(a.Von);
        var art = a.Art;
        var satz = z.Wahl(art.Saetze);
        var kurz = z.Wahl(art.Kurz);
        var mehrtaegig = bis > a.Von;
        var absaetze = new List<string>();
        var erster = new StringBuilder();
        var stunden = a.StundeBis >= 10 ? $"ab der {a.StundeVon}. Stunde" : a.StundeVon == a.StundeBis ? $"in der {a.StundeVon}. Stunde" : $"von der {a.StundeVon}. bis zur {a.StundeBis}. Stunde";
        switch (art.Modus)
        {
            case "termin":
                erster.Append(st.Ton switch
                {
                    0 => z.Wahl(new[] { $"{kurz} {tag}, ich fehle {stunden}.", $"bin {tag} {stunden} wegen {kurz} nicht da." }),
                    3 => z.Wahl(new[] { $"hiermit teile ich Ihnen mit, dass ich {tag} {stunden} wegen {art.Wegen} nicht im Dienst sein kann.", $"wegen {art.Wegen} bin ich {tag} {stunden} verhindert." }),
                    _ => z.Wahl(new[] { $"{satz}, deshalb fehle ich {tag} {stunden}.", $"{satz} und bin deshalb {tag} {stunden} nicht in der Schule.", $"kurze Info vorab: {satz}, ich fehle {tag} {stunden}." }),
                });
                break;
            case "spaeter":
            {
                var ab = a.StundeBis + 1;
                erster.Append(st.Ton switch
                {
                    0 => z.Wahl(new[] { $"{kurz}, komme heute erst zur {ab}. Stunde.", $"bin heute erst ab der {ab}. Stunde da ({kurz})." }),
                    3 => $"wegen {art.Wegen} kann ich heute erst zur {ab}. Stunde zum Dienst erscheinen.",
                    _ => z.Wahl(new[] { $"{satz}, deshalb komme ich heute erst zur {ab}. Stunde.", $"{satz}. Ich bin heute frühestens zur {ab}. Stunde in der Schule." }),
                });
                break;
            }
            case "frueher":
                erster.Append(st.Ton switch
                {
                    0 => z.Wahl(new[] { $"{kurz}, ich muss gleich los und bin {stunden} nicht mehr da.", $"muss heute früher weg ({kurz}), {stunden} bin ich nicht mehr da." }),
                    3 => $"wegen {art.Wegen} muss ich heute den Dienst vorzeitig beenden und bin {stunden} nicht mehr anwesend.",
                    _ => z.Wahl(new[] { $"{satz}. Ich muss deshalb heute früher gehen und bin {stunden} nicht mehr da.", $"{satz}, deshalb bin ich heute {stunden} nicht mehr im Haus." }),
                });
                break;
            default:
            {
                var verb = art.Selbst ? "krankmelden" : "abmelden";
                erster.Append(st.Ton switch
                {
                    0 => art.Selbst ? z.Wahl(new[] { $"{kurz}, {tag} nicht da.", $"bin {tag} krank ({kurz}).", $"{kurz}. Ich falle {tag} aus.", $"{tag} leider raus, {kurz}." })
                                  : z.Wahl(new[] { $"{kurz}, {tag} nicht da.", $"{kurz}. Ich falle {tag} aus.", $"kann {tag} nicht kommen ({kurz})." }),
                    1 => z.Wahl(new[] { $"leider hat es mich erwischt: {satz}. Ich bin {tag} nicht da.", $"{satz}, daher falle ich {tag} leider aus.", $"leider keine guten Nachrichten: {satz}. {Gross(tag)} kann ich deshalb nicht kommen.",
                                       $"ich muss mich leider für {tag} abmelden, {satz}.", $"{satz}. Sorry, aber {tag} wird das nichts mit Unterricht.", $"kurz und schmerzlos: {satz}, {tag} bin ich nicht da.",
                                       $"es tut mir leid, aber {satz}. Ich bleibe {tag} zu Hause.", $"{satz}. Ich bleibe {tag} lieber zu Hause, damit es schnell wieder wird." }),
                    3 => z.Wahl(new[] { $"hiermit melde ich mich für {tag} wegen {art.Wegen} ab.", $"hiermit teile ich Ihnen mit, dass ich {tag} wegen {art.Wegen} nicht zum Dienst erscheinen kann.", $"wegen {art.Wegen} bin ich {tag} leider dienstunfähig." }),
                    4 => z.Wahl(new[] { $"{z.Wahl(art.Lang)} {Gross(satz)}, deshalb muss ich mich für {tag} {verb}.", $"leider muss ich mich für {tag} {verb}. {z.Wahl(art.Lang)}",
                                       $"{satz}. {z.Wahl(art.Lang)} Ich bin {tag} deshalb nicht in der Schule." }),
                    _ => art.Selbst ? z.Wahl(new[] { $"{satz}, deshalb muss ich mich für {tag} {verb}.", $"{satz}. Ich bin {tag} deshalb nicht in der Schule.", $"{satz} und muss mich für {tag} leider {verb}.",
                                                     $"ich melde mich für {tag} krank, {satz}.", $"für {tag} muss ich mich leider krankmelden: {satz}.", $"{satz}. Deshalb falle ich {tag} aus." })
                                    : z.Wahl(new[] { $"{satz}, deshalb kann ich {tag} nicht in die Schule kommen.", $"{satz}. {Gross(tag)} kann ich deshalb leider nicht kommen." }),
                });
                if (mehrtaegig)
                    erster.Append(' ').Append(st.Ton switch
                    {
                        0 => z.Wahl(new[] { $"Voraussichtlich bis {Datum(bis)}.", $"Bis {Datum(bis)} krankgeschrieben." }),
                        3 => z.Wahl(new[] { $"Die Arbeitsunfähigkeit besteht voraussichtlich bis einschließlich {Datum(bis)}.", $"Ich bin bis {Datum(bis)} krankgeschrieben." }),
                        _ => z.Wahl(new[] { $"Voraussichtlich dauert es bis einschließlich {Datum(bis)}.", $"Krankgeschrieben bin ich bis {Datum(bis)}.", $"Ich rechne damit, bis {Datum(bis)} auszufallen.",
                                            $"Die Krankschreibung gilt erst einmal bis {Datum(bis)}.", $"Die Ärztin hat mich bis {Datum(bis)} krankgeschrieben." }),
                    });
                else if (st.Ton != 0 && z.Chance(0.5))
                    erster.Append(' ').Append(z.Wahl(new[] { "Ich hoffe, dass ich übermorgen wieder fit bin.", "Ich melde mich, falls es länger dauert.", "Wenn es besser wird, bin ich am nächsten Tag wieder da.",
                                                             "Ich gehe davon aus, dass es bei einem Tag bleibt.", "Sollte es länger dauern, gebe ich rechtzeitig Bescheid." }));
                break;
            }
        }
        // Telefonisch schon im Sekretariat gemeldet (am Morgen) bzw. kurzfristige Meldung
        if (_uhr.Jetzt.Hour is >= 6 and < 9 && art.Modus != "termin" && z.Chance(0.25))
            erster.Append(' ').Append(z.Wahl(new[] { "Im Sekretariat habe ich mich heute früh schon telefonisch gemeldet, hier noch einmal schriftlich.",
                "Das Sekretariat weiß schon Bescheid, ich habe vorhin angerufen.", "Telefonisch habe ich im Sekretariat schon Bescheid gegeben." }));
        absaetze.Add(erster.ToString());
        var betroffen = BetroffeneStunden(s, a, bis);
        var absatz = st.Ton == 0 && betroffen.Count > 0 && z.Chance(0.4) ? "" : BetroffenText(betroffen, z, a.Tage.Count(t => t <= bis) > 1);
        var bk = betroffen.Any(b => b.Klassen.Split('/').Any(k => Bildungsgang(s, k) is not ("sek1u" or "sek1o" or "sek2")));
        var berufsschule = betroffen.Any(b => b.Klassen.Split('/').Any(k => Bildungsgang(s, k) == "bs"));
        var aufgaben = st.Ton == 0 && z.Chance(0.5) ? "" : AufgabenText(betroffen, z, bk, berufsschule);
        absaetze.Add(absatz.Length > 0 && aufgaben.Length > 0 ? absatz.Contains('\n') ? $"{absatz}\n\n{aufgaben}" : $"{absatz} {aufgaben}" : absatz + aufgaben);
        // weitere Hinweise je nach Stil und Grund
        var hinweise = new List<string>();
        if (art.Id == "corona") hinweise.Add(z.Wahl(new[] { "Ich teste mich in den nächsten Tagen weiter und melde mich.", "Die Klassenleitungen meiner Klassen habe ich informiert.", "Ich bleibe zu Hause, bis der Test negativ ist." }));
        if (art.Id == "dienstunfall") hinweise.Add(z.Wahl(new[] { "Den Unfall habe ich dem Sekretariat gemeldet, die Unfallanzeige folgt.", "Die Unfallmeldung für die Unfallkasse reiche ich nach." }));
        if (art.Id is "op" or "reha") hinweise.Add(z.Wahl(new[] { "Für die Übergabe meiner Kurse habe ich eine Übersicht in Teams abgelegt.", "Notenlisten und Unterlagen liegen in meinem Fach, die Fachschaft weiß Bescheid.", "Wer meine Kurse übernimmt, kann sich gern per Mail bei mir melden." }));
        if (art.Selbst && !art.Teil && st.Ton is 3 or 4 && z.Chance(0.6))
            hinweise.Add(st.Ton == 3 ? "Die Arbeitsunfähigkeitsbescheinigung reiche ich unverzüglich nach." : z.Wahl(new[] { "Die AU reiche ich nach.", "Eine Krankschreibung habe ich, sie kommt per Post.", "Die Bescheinigung bringe ich mit, sobald ich wieder da bin." }));
        if (st.Ton == 4 && betroffen.Count > 0 && z.Chance(0.35))
            hinweise.Add(z.Wahl(new[] { $"Falls in der {z.Wahl(betroffen).Klassen} etwas Schriftliches ansteht, bitte verschieben, ich kümmere mich um einen Nachtermin.",
                "Meine Pausenaufsicht müsste bitte jemand übernehmen.", "Für Rückfragen bin ich per Mail eingeschränkt erreichbar.", "Die Klassen wissen, wo sie weitermachen." }));
        if (st.Ton == 1 && z.Chance(0.35)) hinweise.Add(art.Modus == "termin" ? z.Wahl(new[] { "Danke euch schon mal!", "Danke fürs Einplanen!" }) : z.Wahl(new[] { "Sorry für die Umstände!", "Tut mir leid, dass es so kurzfristig ist.", "Danke euch fürs Auffangen!" }));
        if (!art.Teil && (_uhr.Jetzt.Hour < 7 || _uhr.Jetzt.Hour >= 22) && st.Ton is 2 or 3 or 4 && z.Chance(0.5)) hinweise.Add("Entschuldigung für die kurzfristige Meldung.");
        if (hinweise.Count > 0) absaetze.Add(string.Join(" ", hinweise));

        var name = Name(s, a.Lehrer, Zufall.Fuer(w.Seed, "n", a.Lehrer));
        var betreff = art.Modus switch
        {
            "termin" => z.Wahl(new[] { $"{Gross(kurz)} {Datum(a.Von)}", $"Abwesenheit {Datum(a.Von)}, {stunden}", $"Abwesenheit wegen {kurz}", $"Fehle {Kurztag(a.Von)} {Stunden(a.StundeVon, Math.Min(a.StundeBis, 10))} Std." }),
            "spaeter" => z.Wahl(new[] { $"Komme heute später", $"Später da ({kurz})", $"Heute erst ab der {a.StundeBis + 1}. Stunde" }),
            "frueher" => z.Wahl(new[] { "Muss heute früher gehen", $"Ab der {a.StundeVon}. Stunde nicht mehr da", $"Kurzfristig: {kurz}" }),
            _ => st.Ton switch
            {
                0 => z.Wahl(new[] { "Krank", "Krankmeldung", $"Fehle {Kurztag(a.Von)}", $"{Gross(kurz)}" }),
                1 => z.Wahl(new[] { "Bin leider krank", $"Krankmeldung {name}", "Mich hat es erwischt", $"{Gross(kurz)}, bin raus" }),
                3 => z.Wahl(new[] { "Krankmeldung", $"Krankmeldung ab {Datum(a.Von)}", $"Dienstunfähigkeit ab {Datum(a.Von)}", $"Abwesenheit {Datum(a.Von)}" }),
                4 => z.Wahl(new[] { mehrtaegig ? $"Krankmeldung bis voraussichtlich {Datum(bis)}" : $"Krankmeldung für {Datum(a.Von)}", $"Krankmeldung {name}", $"Ausfall {Datum(a.Von)} wegen {kurz}" }),
                _ => z.Wahl(new[] { $"Krankmeldung {Datum(a.Von)}", $"Ausfall {Datum(a.Von)}", $"Krankmeldung {name}", "Krankmeldung" }),
            },
        };
        if (art.Id is "kind") betreff = z.Chance(0.5) ? betreff : z.Wahl(new[] { "Kind krank", $"Betreuung krankes Kind {Datum(a.Von)}", "Kinderkrankentag" });
        if (art.Id is "corona" && z.Chance(0.5)) betreff = z.Wahl(new[] { "Corona positiv", "Coronatest positiv, Krankmeldung" });
        if (art.Id is "dienstunfall" && z.Chance(0.5)) betreff = "Dienstunfall, Krankmeldung";
        if (art.Id is "op" or "reha" && z.Chance(0.5)) betreff = art.Id == "op" ? $"Krankmeldung wegen OP ab {Datum(a.Von)}" : $"Reha ab {Datum(a.Von)}";
        return (betreff, Brief(s, a.Lehrer, st, z, absaetze));
    }

    private (string, string) Verlaengerung(Stand s, Weltstand w, Ausfall a, DateOnly bis, Zufall z)
    {
        var st = StilDer(w.Seed, a.Lehrer);
        var text = st.Ton switch
        {
            0 => z.Wahl(new[] { $"bin weiter krank, Folgebescheinigung bis {Datum(bis)}.", $"Verlängerung bis {Datum(bis)}." }),
            3 => z.Wahl(new[] { $"meine Arbeitsunfähigkeit wurde bis einschließlich {Datum(bis)} verlängert. Die Folgebescheinigung reiche ich nach.",
                                $"hiermit teile ich Ihnen mit, dass ich weiterhin dienstunfähig bin, voraussichtlich bis {Datum(bis)}." }),
            _ => $"{z.Wahl(new[] { "ich bin leider weiterhin krankgeschrieben.", "leider geht es mir noch nicht besser.", "meine Krankschreibung wurde verlängert.", "die Ärztin hat mich noch nicht gesundgeschrieben.", "es dauert leider doch länger als gedacht." })} " +
                 z.Wahl(new[] { $"Die Folgebescheinigung gilt bis {Datum(bis)}.", $"Ich falle voraussichtlich bis {Datum(bis)} aus.", $"Die neue Bescheinigung reicht bis {Datum(bis)}.", $"Jetzt erst einmal bis {Datum(bis)}." }),
        };
        var zusatz = a.Art.Id is "op" or "reha" && z.Chance(0.5) ? z.Wahl(new[] { "Die Genesung verläuft gut, braucht aber noch etwas Zeit.", "Die Klinik hat die Maßnahme um eine Woche verlängert." })
            : st.Ton is 1 or 4 && z.Chance(0.4) ? z.Wahl(new[] { "Danke, dass ihr meine Klassen so gut versorgt!", "Die Aufgaben in Teams habe ich für die nächste Woche ergänzt.", "Ich halte euch auf dem Laufenden." }) : "";
        return (z.Wahl(new[] { "Verlängerung der Krankmeldung", "Verlängerung Krankmeldung", $"Folgebescheinigung bis {Datum(bis)}", "Folgemeldung", "Verlängerung" }),
            Brief(s, a.Lehrer, st, z, new[] { text, zusatz }));
    }

    private (string, string) AuNachgereicht(Stand s, Weltstand w, Ausfall a, Zufall z)
    {
        var st = StilDer(w.Seed, a.Lehrer);
        var text = st.Ton == 3
            ? z.Wahl(new[] { "anbei der Hinweis, dass die Arbeitsunfähigkeitsbescheinigung heute per Post an das Sekretariat gegangen ist.", "die Arbeitsunfähigkeitsbescheinigung habe ich heute im Sekretariat abgeben lassen." })
            : z.Wahl(new[] { "kurzer Nachtrag: Die AU ist heute an das Sekretariat gegangen.", "die Krankschreibung habe ich meinem Mann mitgegeben, sie liegt im Sekretariat.", "die AU habe ich eben eingescannt und an das Sekretariat geschickt.",
                             "die Bescheinigung kommt per Post, sie sollte morgen da sein.", "nur zur Info: Die AU liegt jetzt im Sekretariat." });
        return (z.Wahl(new[] { "AU nachgereicht", "Nachtrag Krankmeldung", "Nachtrag: Bescheinigung", "AU liegt im Sekretariat" }), Brief(s, a.Lehrer, st, z, new[] { text }));
    }

    private (string, string) CoronaZwischenstand(Stand s, Weltstand w, Ausfall a, Zufall z)
    {
        var st = StilDer(w.Seed, a.Lehrer);
        var text = z.Wahl(new[] { "kurzes Update: Der Test ist weiterhin positiv, ich bleibe wie gemeldet zu Hause.", "der Test in der Praxis hat die Infektion bestätigt, es bleibt bei der Krankmeldung.",
                                  "mir geht es etwas besser, der Test ist aber noch positiv." });
        return (z.Wahl(new[] { "Update Krankmeldung", "Update: Test weiterhin positiv", "Nachtrag Corona" }), Brief(s, a.Lehrer, st, z, new[] { text }));
    }

    private (string, string) Gesundmeldung(Stand s, Weltstand w, Ausfall a, Zufall z)
    {
        var st = StilDer(w.Seed, a.Lehrer);
        var naechster = w.Schultag(a.Bis.AddDays(1), 0);
        var morgen = naechster == _uhr.Heute.AddDays(1);
        var wann = morgen ? "morgen" : $"am {Datum(naechster)}";
        var text = st.Ton switch
        {
            0 => z.Wahl(new[] { $"bin {wann} wieder da.", $"{wann} wieder im Dienst.", $"ab {wann.Replace("am ", "")} wieder an Bord." }),
            3 => z.Wahl(new[] { $"hiermit melde ich mich zum {Datum(naechster)} wieder dienstfähig.", $"ich nehme {wann} meinen Dienst wieder auf." }),
            _ => z.Wahl(new[] { $"ich bin {wann} wieder im Dienst.", $"mir geht es besser, ich bin {wann} wieder da.", $"kurze Info: Ich bin {wann} wieder in der Schule.",
                                $"gute Nachrichten: {Gross(wann)} bin ich wieder da.", $"ich bin wieder gesund und {wann} ganz normal im Unterricht." }),
        };
        var dank = st.Ton switch
        {
            0 => z.Chance(0.3) ? "Danke fürs Vertreten!" : "",
            3 => z.Chance(0.4) ? "Für die Vertretung in der Zwischenzeit bedanke ich mich herzlich." : "",
            _ => z.Chance(0.5) ? z.Wahl(new[] { "Danke fürs Vertreten!", "Vielen Dank an alle, die eingesprungen sind.", "Danke für eure Unterstützung in der Zeit.", "Danke, dass ihr meine Klassen versorgt habt." }) : "",
        };
        var betreff = z.Wahl(new[] { "Gesundmeldung", morgen ? "Ab morgen wieder da" : $"Ab {Datum(naechster)} wieder da", "Wieder gesund", "Bin wieder da", "Wieder im Dienst" });
        return (betreff, Brief(s, a.Lehrer, st, z, new[] { dank.Length > 0 ? $"{text} {dank}" : text }));
    }
}
