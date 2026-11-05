using System.Text;
using UntPlan.App.Dialoge;
using UntPlan.Core;
using UntPlan.Core.Austausch;
using UntPlan.Core.Model;
using UntPlan.Core.Planung;
using UntPlan.Core.Vertretungsplanung;

namespace UntPlan.App.Fenster;

/// <summary>
/// Berichte der Vertretungsplanung wie im Handbuch (Berichte | Vertretungsplanung): Vertretungs-Abrechnung je Lehrer mit Bilanzzeilen,
/// Entfalltage, Vertretungs-Übersicht, Mehrarbeit (Tagesbilanz eines Monats) sowie Monatsabrechnung, Mehrarbeit NRW, Wochenwerte und
/// die Abrechnungsübersicht aller Lehrer. Ausgabe als druckfertiges HTML-Dokument.
/// </summary>
public static class StatistikBerichte
{
    public const string Abrechnung = "Vertretungs-Abrechnung";
    public const string Entfalltage = "Entfalltage";
    public const string Uebersicht = "Vertretungs-Übersicht";
    public const string Mehrarbeit = "Mehrarbeit";
    public const string Monatsabrechnung = "Monatsabrechnung";
    public const string MehrarbeitNrw = "Mehrarbeit NRW (Monate)";
    public const string Wochenwerte = "Wochenwerte";
    public const string AbrechnungAlle = "Vertretungsabrechnung (Übersicht aller Lehrer)";

    /// <summary>Berichte im Baum der Karteikarte „Berichte“ (die ersten vier wie im Handbuch).</summary>
    public static readonly string[] Namen = { Abrechnung, Entfalltage, Uebersicht, Mehrarbeit, Monatsabrechnung, MehrarbeitNrw, Wochenwerte, AbrechnungAlle };

    private const string Stil = "<style>body{font-family:Arial,Helvetica,sans-serif;font-size:10pt;margin:16px}.el{page-break-inside:avoid;margin-bottom:28px}.neu{page-break-before:always}" +
        ".kn{font-size:20pt}.ln{font-size:13pt;margin-left:6px}h1{font-size:16pt;font-weight:normal;margin:0 0 12px}h2{font-size:11pt;margin:14px 0 4px}" +
        "table.r{border-collapse:collapse}table.r td,table.r th{padding:1px 10px 1px 0;text-align:left;vertical-align:top;font-weight:normal}table.r th{border-bottom:1px solid #000}" +
        "td.z{text-align:right}tr.s td{border-top:1px solid #000;font-weight:bold}.b{font-weight:bold}.bil{font-weight:bold;margin-top:12px;line-height:1.4}" +
        "table.g{border-collapse:collapse}table.g td,table.g th{border:1px solid #000;padding:1px 6px}table.m td{padding:0 4px;text-align:center}table.m td.l{text-align:left}" +
        "table.m tr.k td{border-bottom:1px solid #000;font-weight:bold}.fuss{border-top:1px solid #000;margin-top:16px;text-align:center;font-size:8pt}</style>";

    public static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string Kurz(DateOnly t) => $"{t.Day}.{t.Month}.";

    private static string Tag(Schuldatei d, DateOnly t) => Planungsdienst.TageKurz[((int)t.DayOfWeek + 6) % 7];

    /// <summary>Kopf eines Lehrers wie im Handbuch: Kurzname groß, daneben der Langname.</summary>
    private static string Kopf(Lehrer l) => $"<div><span class=\"kn\">{H(l.Kurzname)}</span><span class=\"ln\">{H(l.Langname)}</span></div>";

    public static void Oeffnen(string titel, string body, string? ueberschrift = null, int schriftProzent = 100)
    {
        var kopf = string.IsNullOrWhiteSpace(ueberschrift) ? "" : $"<div style=\"text-align:center;margin-bottom:10px\">{H(ueberschrift)}</div>";
        var html = $"<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>{H(titel)}</title>{Stil}</head><body style=\"font-size:{10 * schriftProzent / 100.0:0.#}pt\">{kopf}{body}" +
                   $"<div class=\"fuss\">{H(AppZustand.D.Einstellungen.Schulname)} · Vertretungsplanung mit UntPlan · {DateTime.Now:dd.MM.yyyy HH:mm}</div></body></html>";
        var pfad = Path.Combine(Path.GetTempPath(), $"UntPlan_{new string(titel.Where(char.IsLetterOrDigit).ToArray())}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        File.WriteAllText(pfad, html);
        HauptFenster.Starten(pfad);
    }

    /// <summary>Inhalt eines Rasters als HTML-Tabelle (Druck der Vertretungsstatistik).</summary>
    public static string Tabelle(DataGridView g)
    {
        var sb = new StringBuilder("<table class=\"g\"><tr>");
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        foreach (var c in spalten) sb.Append($"<th>{H(c.HeaderText)}</th>");
        sb.Append("</tr>");
        foreach (DataGridViewRow r in g.Rows)
        {
            sb.Append("<tr>");
            foreach (var c in spalten)
            {
                var zelle = r.Cells[c.Index];
                var text = zelle.Tag is int[] w ? $"{w[0]} / {w[1]} / {w[2]}" : zelle.FormattedValue?.ToString();
                sb.Append($"<td>{H(text)}</td>");
            }
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    public static void CsvSpeichern(IWin32Window owner, DataGridView g, string name)
    {
        using var sfd = new SaveFileDialog { Filter = "CSV-Datei|*.csv", FileName = name };
        if (sfd.ShowDialog(owner) != DialogResult.OK) return;
        var spalten = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var liste = new List<string[]> { spalten.Select(c => c.HeaderText).ToArray() };
        liste.AddRange(g.Rows.Cast<DataGridViewRow>().Select(r => spalten.Select(c => r.Cells[c.Index].FormattedValue?.ToString() ?? "").ToArray()));
        File.WriteAllText(sfd.FileName, Csv.Schreiben(liste, bom: false), new UTF8Encoding(true));
    }

    /// <summary>Matrixdruck der Vertretungsstatistik: Zeilen = erste Gliederung (z. B. Fach), Spalten = zweite Gliederung (z. B. Grund).</summary>
    public static string Matrix(string titel, List<(string Titel, string[] Kopf, List<string[]> Zeilen)> bloecke)
    {
        var sb = new StringBuilder($"<h1>{H(titel)}</h1>");
        foreach (var (t, kopf, zeilen) in bloecke)
        {
            sb.Append($"<div class=\"el\"><div class=\"b\">{H(t)}</div>");
            var daten = zeilen.Where(z => !z.Contains("Summe")).ToList();
            if (kopf.Length < 3) { sb.Append(TabelleAus(kopf, zeilen)).Append("</div>"); continue; }
            var spalten = daten.Select(z => z[2]).Distinct().OrderBy(x => x).ToList();
            sb.Append($"<table class=\"g\"><tr><th>{H(kopf[1])} / {H(kopf[2])}</th>");
            foreach (var s in spalten) sb.Append($"<th>{H(s)}</th>");
            sb.Append("<th>Summe</th></tr>");
            foreach (var zeile in daten.GroupBy(z => z[1]).OrderBy(g => g.Key))
            {
                sb.Append($"<tr><td>{H(zeile.Key)}</td>");
                foreach (var s in spalten) sb.Append($"<td class=\"z\">{zeile.Where(z => z[2] == s).Sum(z => int.TryParse(z[0], out var n) ? n : 0)}</td>");
                sb.Append($"<td class=\"z\">{zeile.Sum(z => int.TryParse(z[0], out var n) ? n : 0)}</td></tr>");
            }
            sb.Append("</table></div>");
        }
        return sb.ToString();
    }

    private static string TabelleAus(string[] kopf, List<string[]> zeilen)
    {
        var sb = new StringBuilder("<table class=\"r\"><tr>");
        foreach (var k in kopf) sb.Append($"<th>{H(k)}</th>");
        sb.Append("</tr>");
        foreach (var z in zeilen)
        {
            sb.Append(z.Contains("Summe") ? "<tr class=\"s\">" : "<tr>");
            foreach (var x in z) sb.Append($"<td>{H(x)}</td>");
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    // --------------------------------------------------------------------------------------------
    // Aufruf: Auswahldialog wie im Handbuch, dann Bericht erzeugen
    // --------------------------------------------------------------------------------------------

    public static void Starten(IWin32Window owner, string bericht, DateOnly von, DateOnly bis)
    {
        var d = AppZustand.D;
        if (bericht == Wochenwerte)
        {
            // Wochenwerte als eigenes Fenster wie im Handbuch (Modul Wertrechnung)
            HauptFenster.Oeffnen("wochenwerte", () => new WochenwerteFenster());
            return;
        }
        var mitLehrer = bericht != Uebersicht;
        using var dlg = new BerichtAuswahlDialog(bericht, von, bis, mitLehrer, bericht == Abrechnung);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        var lehrer = dlg.Lehrer.Count > 0 ? dlg.Lehrer : Stammdaten.Sortiert(d.Lehrer).ToList();
        string body;
        using (new WarteCursor())
            body = bericht switch
            {
                Abrechnung => VertretungsAbrechnung(d, lehrer, dlg.Von, dlg.Bis, dlg.Details),
                Entfalltage => EntfalltageBericht(d, lehrer, dlg.Von, dlg.Bis),
                Uebersicht => VertretungsUebersicht(d, dlg.Von, dlg.Bis),
                Mehrarbeit => MehrarbeitBericht(d, lehrer, dlg.Von, dlg.Bis),
                Monatsabrechnung => MonatsabrechnungBericht(d, lehrer, dlg.Von),
                MehrarbeitNrw => MehrarbeitNrwBericht(d, lehrer, dlg.Von, dlg.Bis),
                Wochenwerte => WochenwerteBericht(d, lehrer, dlg.Von, dlg.Bis),
                _ => AbrechnungUebersicht(d, dlg.Von, dlg.Bis),
            };
        Oeffnen(bericht, body, dlg.Ueberschrift, dlg.Schrift);
    }

    /// <summary>Optionen „Druck - Details“ der Vertretungs-Abrechnung.</summary>
    public sealed class AbrechnungsDetails
    {
        public bool EineSeiteJeElement { get; set; } = true;
        public bool SummeMitUebertrag { get; set; }
        public bool Details { get; set; }
        public bool OhneBereitschaften { get; set; }
        public bool OhnePausenaufsichten { get; set; }
        public bool ZwischensummenMonate { get; set; }
    }

    // --------------------------------------------------------------------------------------------
    // Vertretungs-Abrechnung
    // --------------------------------------------------------------------------------------------

    public static string VertretungsAbrechnung(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly von, DateOnly bis, AbrechnungsDetails opt)
    {
        var sb = new StringBuilder();
        var erste = true;
        string W(decimal w) => Zaehlung.WertText(d, w, d.Vertretungseinstellungen.Zaehler.WieZaehlen == ZaehlArt.Anzahl ? "0.##" : "0.000");
        foreach (var l in lehrer)
        {
            var z = Zaehlung.Lehrer(d, l, von, bis);
            if (z.Zeilen.Count == 0) continue;
            var bil = Zaehlung.Bilanz(d, l, von, bis, z);
            sb.Append($"<div class=\"el{(opt.EineSeiteJeElement && !erste ? " neu" : "")}\">{Kopf(l)}");
            erste = false;
            sb.Append($"<div class=\"b\" style=\"margin-top:10px\">Vertretungen / Entfälle / WertKorrektur : {Kurz(von)} - {Kurz(bis)}</div>");
            sb.Append("<table class=\"r\"><tr><th>Datum</th><th>Stunde</th><th>E/V/F</th><th class=\"z\">Wert</th><th class=\"z\">Zähler</th><th>Fach</th><th>Klasse(n)</th><th>Grund</th><th>Text</th></tr>");
            var zaehler = 0m;
            if (opt.SummeMitUebertrag && von > d.Einstellungen.SchuljahrVon)
            {
                zaehler = Zaehlung.Lehrer(d, l, d.Einstellungen.SchuljahrVon, von.AddDays(-1)).Summe;
                sb.Append($"<tr><td colspan=\"4\">Übertrag</td><td class=\"z\">{W(zaehler)}</td><td colspan=\"4\"></td></tr>");
            }
            (int Jahr, int Monat)? monat = null;
            var monatsSumme = 0m;
            foreach (var x in z.Zeilen)
            {
                if (opt.ZwischensummenMonate && monat is { } m && (x.Datum.Year, x.Datum.Month) != m)
                {
                    sb.Append($"<tr class=\"s\"><td colspan=\"3\">{new DateTime(m.Jahr, m.Monat, 1).ToString("MMMM", Format.De)}</td><td class=\"z\">{W(monatsSumme)}</td><td class=\"z\">{W(zaehler)}</td><td colspan=\"4\"></td></tr>");
                    monatsSumme = 0;
                }
                monat = (x.Datum.Year, x.Datum.Month);
                if (x.Zaehlt) { zaehler += x.Wert; monatsSumme += x.Wert; }
                var stunde = x.Code == "WK" ? Tag(d, x.Datum) : x.Vertretung is { } v ? $"{Tag(d, x.Datum)}/{Vertretungsplan.StundeText(v)}" : $"{Tag(d, x.Datum)}/{x.Stunde}";
                var grund = x.Grund.Length > 0 ? x.Grund : x.Code switch { "+V" => "Vertretung", "P" => "PA-Vertr.", "VA" => "Veranstaltung", "F" => "Freisetzung", "V" => "Statt-Std.", "B" => "Bereitschaft", _ => x.Art };
                var text = x.Text;
                if (x.Vertretung is { NachDatum: DateOnly nach } vn) grund = $"-> {Kurz(nach)} / {vn.NachStunde}";
                if (x.Vertretung is { VonDatum: DateOnly vd } vv && x.Code is "V" or "+V") text = $"{Kurz(vd)} / {vv.VonStunde}" + (text.Length > 0 ? " " + text : "");
                sb.Append($"<tr><td>{Kurz(x.Datum)}</td><td>{H(stunde)}</td><td>{H(x.Code)}</td><td class=\"z\">{W(x.Wert)}</td><td class=\"z\">{W(zaehler)}</td><td>{H(x.Fach)}</td><td>{H(x.Klassen)}</td><td>{H(grund)}</td><td>{H(text)}</td></tr>");
            }
            if (opt.ZwischensummenMonate && monat is { } mm)
                sb.Append($"<tr class=\"s\"><td colspan=\"3\">{new DateTime(mm.Jahr, mm.Monat, 1).ToString("MMMM", Format.De)}</td><td class=\"z\">{W(monatsSumme)}</td><td class=\"z\">{W(zaehler)}</td><td colspan=\"4\"></td></tr>");
            sb.Append("</table>");
            // Bilanzzeilen wie im Handbuch
            var positiv = new[] { "+V", "V", "B", "P", "+Y", "+K", "VA", "WK" };
            var vertr = z.Zeilen.Where(x => x.Zaehlt && positiv.Contains(x.Code)).Sum(x => x.Wert);
            var vertrNicht = z.Zeilen.Where(x => !x.Zaehlt && positiv.Contains(x.Code)).Sum(x => x.Vertretung is null ? 0 : Math.Max(1, Math.Abs(x.Wert)));
            var entf = -z.Zeilen.Where(x => x.Zaehlt && x.Code is "-E" or "F").Sum(x => x.Wert);
            var entfNicht = z.Zeilen.Count(x => !x.Zaehlt && x.Code is "-E" or "F");
            sb.Append($"<div class=\"bil\">Vertretungen: {W(vertr)}<br>Vertretung nicht zu zählen: {W(vertrNicht)}<br>Entfälle: {W(entf)}<br>Entfall nicht zu zählen: {W(entfNicht)}<br>Summe: {W(z.Summe)}");
            if (!(opt.Details && opt.OhneBereitschaften) && bil.BereitschaftenVerplant + bil.BereitschaftenEntfallen + bil.BereitschaftenGenutzt > 0)
                sb.Append($"<br>Bereitschaften verplant: {bil.BereitschaftenVerplant}<br>Bereitschaften entfallen: {bil.BereitschaftenEntfallen}<br>Bereitschaften genutzt: {bil.BereitschaftenGenutzt}");
            if (!(opt.Details && opt.OhnePausenaufsichten) && bil.PaGeplant + bil.PaEntfall + bil.PaVertretung > 0)
                sb.Append($"<br><br>Pausenaufsichten geplant: {bil.PaGeplant}<br>Pausenaufsichtsentfall: {bil.PaEntfall}<br>Pausenaufsichtsvertretung: {bil.PaVertretung}<br>Pausenaufsichten gehalten: {bil.PaGehalten}");
            sb.Append("</div></div>");
        }
        return sb.Length == 0 ? "<p>Keine Vertretungsereignisse im gewählten Zeitraum.</p>" : sb.ToString();
    }

    // --------------------------------------------------------------------------------------------
    // Entfalltage
    // --------------------------------------------------------------------------------------------

    public static string EntfalltageBericht(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly von, DateOnly bis)
    {
        var ids = lehrer.Select(l => l.Id).ToHashSet();
        var tage = Statistik.Entfalltage(d, von, bis).Where(e => ids.Contains(e.Lehrer.Id)).ToList();
        var sb = new StringBuilder($"<h1>Entfalltage/{Kurz(von)} - {Kurz(bis)}</h1>");
        foreach (var g in tage.GroupBy(e => e.Lehrer).OrderBy(g => g.Key.Kurzname, StringComparer.Create(Format.De, true)))
        {
            sb.Append($"<div class=\"el\"><div>{H(g.Key.Kurzname)}</div><table class=\"g\"><tr><th>Datum</th><th>Grund</th><th>Text</th><th>Entfallsstunden</th></tr>");
            foreach (var e in g.OrderBy(e => e.Datum)) sb.Append($"<tr><td>{Kurz(e.Datum)}</td><td>{H(e.Grund)}</td><td>{H(e.Text)}</td><td class=\"z\">{e.Stunden:0.0}</td></tr>");
            sb.Append($"<tr><td>Summe</td><td></td><td></td><td class=\"z\">{g.Sum(e => e.Stunden):0.0}</td></tr></table><div>Entfalltage: {g.Count()}</div></div>");
        }
        if (tage.Count == 0) sb.Append("<p>Keine Entfalltage im gewählten Zeitraum.</p>");
        return sb.ToString();
    }

    // --------------------------------------------------------------------------------------------
    // Vertretungs-Übersicht
    // --------------------------------------------------------------------------------------------

    public static string VertretungsUebersicht(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var u = Statistik.VertretungsUebersicht(d, von, bis);
        var sb = new StringBuilder($"<h1 class=\"b\">Vertretungs-Übersicht : {Kurz(von)} - {Kurz(bis)}</h1><table class=\"r\">");
        void Z(string op, int n, string text, bool fett = false) => sb.Append($"<tr{(fett ? " class=\"b\"" : "")}><td style=\"width:60px\">{op}</td><td class=\"z\" style=\"width:50px\">{n}</td><td>{H(text)}</td></tr>");
        void T(string text) => sb.Append($"<tr><td colspan=\"3\" class=\"b\" style=\"padding-top:14px\">{H(text)}</td></tr>");
        void Strich() => sb.Append("<tr><td></td><td class=\"z\">---</td><td>------</td></tr>");
        T("Überblick");
        Z("", u.Geplant, "Geplanter Unterricht");
        Z("-", u.GemaessPlan, "Gem. Stundenplan gehaltener Unterricht");
        Z("=", u.NichtErteilt, "Vom geplanten Lehrer nicht erteilter Unterricht");
        sb.Append("<tr><td colspan=\"3\">&nbsp;</td></tr>");
        Z("", u.Sondereinsaetze, "Sondereinsätze");
        Z("", u.Veranstaltungen, "Veranstaltungen");
        T("Vom geplanten Lehrer nicht erteilter Unterricht (Absenzgründe)");
        foreach (var (k, v) in u.NachGrund.OrderByDescending(x => x.Value)) Z("", v, k);
        Strich();
        Z("=", u.NachGrund.Values.Sum(), "Summe");
        T("Vertretungen (Art der Vertretung)");
        foreach (var (k, v) in u.NachArt.OrderByDescending(x => x.Value)) Z("", v, k);
        Strich();
        Z("=", u.NachArt.Values.Sum(), "Summe");
        T("Entfall");
        foreach (var (k, v) in u.EntfallNachGrund.OrderByDescending(x => x.Value)) Z("", v, k);
        Strich();
        Z("=", u.EntfallNachGrund.Values.Sum(), "Summe");
        return sb.Append("</table>").ToString();
    }

    // --------------------------------------------------------------------------------------------
    // Mehrarbeit: tägliche Bilanz (Ist, Soll, Entf., Vertr.) je Monat
    // --------------------------------------------------------------------------------------------

    public static string MehrarbeitBericht(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly von, DateOnly bis)
    {
        var sb = new StringBuilder($"<h1 class=\"b\">Mehrarbeit&nbsp; {Kurz(von)} - {Kurz(bis)}</h1>");
        foreach (var l in lehrer)
        {
            var monate = new List<(DateOnly Erster, Dictionary<int, Statistik.Tagesbilanz> Tage)>();
            for (var m = new DateOnly(von.Year, von.Month, 1); m <= bis; m = m.AddMonths(1))
                monate.Add((m, Statistik.Mehrarbeit(d, l, m.Year, m.Month).Where(t => t.Datum >= von && t.Datum <= bis).ToDictionary(t => t.Datum.Day)));
            var alle = monate.SelectMany(m => m.Tage.Values).ToList();
            if (alle.Count == 0) continue;
            var ist = alle.Sum(t => t.Ist);
            var soll = alle.Sum(t => t.Soll);
            sb.Append($"<div class=\"el\">{Kopf(l)}<div><span class=\"b\" style=\"font-size:12pt\">{ist - soll:+0;-0;0} Mehrarbeit</span> &nbsp;= {ist} &nbsp;- {soll}</div>");
            foreach (var (erster, tage) in monate)
            {
                var anzahl = DateTime.DaysInMonth(erster.Year, erster.Month);
                sb.Append($"<table class=\"m\" style=\"margin-top:8px\"><tr class=\"k\"><td class=\"l\" style=\"width:110px\">Tag / {erster.ToDateTime(TimeOnly.MinValue).ToString("MMMM", Format.De)}</td>");
                bool Drin(int tag) => new DateOnly(erster.Year, erster.Month, tag) is var t && t >= von && t <= bis;
                for (var t = 1; t <= 31; t++) sb.Append($"<td>{(t <= anzahl && Drin(t) ? t.ToString() : "/")}</td>");
                sb.Append("</tr>");
                foreach (var (text, wert) in new (string, Func<Statistik.Tagesbilanz, int>)[] { ("Ist", t => t.Ist), ("Soll", t => t.Soll), ("Entf.", t => t.Entfaelle), ("Vertr.", t => t.Vertretungen) })
                {
                    sb.Append($"<tr><td class=\"l\">{text}</td>");
                    for (var t = 1; t <= 31; t++)
                        sb.Append($"<td>{(t > anzahl || !Drin(t) ? "/" : tage.TryGetValue(t, out var b) && (wert(b) != 0 || text is "Ist" or "Soll") ? wert(b).ToString() : "")}</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    // --------------------------------------------------------------------------------------------
    // Weitere Berichte (bisherige Auswertungen)
    // --------------------------------------------------------------------------------------------

    public static string MonatsabrechnungBericht(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly monat)
    {
        var titel = new DateTime(monat.Year, monat.Month, 1).ToString("MMMM yyyy", Format.De);
        var sb = new StringBuilder($"<h1>Monatsabrechnung {H(titel)}</h1>");
        foreach (var l in lehrer)
        {
            var tage = Statistik.Mehrarbeit(d, l, monat.Year, monat.Month);
            if (tage.Count == 0) continue;
            sb.Append($"<div class=\"el\">{Kopf(l)}<table class=\"r\"><tr><th>Tag</th><th class=\"z\">Soll</th><th class=\"z\">Ist</th><th class=\"z\">Vertretungen</th><th class=\"z\">Entfälle</th><th class=\"z\">Differenz</th></tr>");
            foreach (var t in tage)
                sb.Append($"<tr><td>{Tag(d, t.Datum)} {Kurz(t.Datum)}</td><td class=\"z\">{t.Soll}</td><td class=\"z\">{t.Ist}</td><td class=\"z\">{t.Vertretungen}</td><td class=\"z\">{t.Entfaelle}</td><td class=\"z\">{t.Ist - t.Soll:+0;-0;0}</td></tr>");
            sb.Append($"<tr class=\"s\"><td>Summe</td><td class=\"z\">{tage.Sum(t => t.Soll)}</td><td class=\"z\">{tage.Sum(t => t.Ist)}</td><td class=\"z\">{tage.Sum(t => t.Vertretungen)}</td><td class=\"z\">{tage.Sum(t => t.Entfaelle)}</td><td class=\"z\">{tage.Sum(t => t.Ist - t.Soll):+0;-0;0}</td></tr></table>");
            sb.Append("<p style=\"margin-top:30px\">_______________________________ &nbsp; &nbsp; _______________________________<br>Lehrkraft &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; &nbsp; Schulleitung</p></div>");
        }
        return sb.ToString();
    }

    public static string MehrarbeitNrwBericht(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly von, DateOnly bis)
    {
        var sb = new StringBuilder($"<h1>Mehrarbeit NRW {Kurz(von)} - {Kurz(bis)}</h1>");
        foreach (var l in lehrer)
        {
            var monate = Statistik.MehrarbeitNrw(d, l, von, bis);
            if (monate.Count == 0) continue;
            sb.Append($"<div class=\"el\">{Kopf(l)}<table class=\"r\"><tr><th>Monat</th><th class=\"z\">geleistete MA</th><th class=\"z\">Minderleistung</th><th class=\"z\">Saldo</th><th class=\"z\">Grenze</th><th class=\"z\">vergütungsfähig</th></tr>");
            foreach (var m in monate)
                sb.Append($"<tr><td>{new DateTime(m.Jahr, m.Monat, 1).ToString("MMMM yyyy", Format.De)}</td><td class=\"z\">{m.Geleistet}</td><td class=\"z\">{m.Minder}</td><td class=\"z\">{m.Saldo:+0;-0;0}</td><td class=\"z\">{m.Grenze:0.#}</td><td class=\"z\">{m.Verguetungsfaehig}</td></tr>");
            sb.Append($"</table><div class=\"bil\">Teilzeitquote {Statistik.Teilzeitquote(d, l):0.00} · Summe vergütungsfähig: {monate.Sum(m => m.Verguetungsfaehig)} Stunden</div></div>");
        }
        return sb.ToString();
    }

    public static string WochenwerteBericht(Schuldatei d, IEnumerable<Lehrer> lehrer, DateOnly von, DateOnly bis)
    {
        var sb = new StringBuilder($"<h1>Wochenwerte {Kurz(von)} - {Kurz(bis)}</h1>");
        foreach (var l in lehrer)
        {
            var ww = Statistik.Wochenwerte(d, l).Where(w => w.Montag.AddDays(6) >= von && w.Montag <= bis).ToList();
            if (ww.Count == 0) continue;
            sb.Append($"<div class=\"el\">{Kopf(l)}<table class=\"r\"><tr><th>Woche</th><th>Von - Bis</th><th class=\"z\">geplant</th><th class=\"z\">Gehalten</th><th class=\"z\">Entf.</th><th class=\"z\">Vertr.</th><th class=\"z\">Korrektur</th><th class=\"z\">Differenz</th></tr>");
            foreach (var w in ww)
                sb.Append($"<tr><td>{System.Globalization.ISOWeek.GetWeekOfYear(w.Montag.ToDateTime(TimeOnly.MinValue))}</td><td>{Kurz(w.Montag)}-{Kurz(w.Montag.AddDays(6))}</td><td class=\"z\">{w.Unterricht}</td><td class=\"z\">{w.Ist:0.##}</td><td class=\"z\">{w.Entfaelle}</td><td class=\"z\">{w.Vertretungen}</td><td class=\"z\">{w.Korrektur:+0.##;-0.##;}</td><td class=\"z\">{w.Ist - w.Soll:+0.##;-0.##;0}</td></tr>");
            sb.Append("</table></div>");
        }
        return sb.ToString();
    }

    public static string AbrechnungUebersicht(Schuldatei d, DateOnly von, DateOnly bis)
    {
        var liste = Statistik.Abrechnung(d, von, bis);
        var sb = new StringBuilder($"<h1>Vertretungsabrechnung {Kurz(von)} - {Kurz(bis)}</h1><table class=\"g\"><tr><th>Lehrer</th><th>Name</th><th>Vertretungen</th><th>Betreuungen</th><th>Sondereins.</th><th>Pausenaufs.</th><th>Mehrarbeit</th><th>Entfall</th><th>Freisetzung</th><th>Zähler</th><th>vergütungsfähig (NRW)</th></tr>");
        foreach (var z in liste)
            sb.Append($"<tr><td>{H(z.Lehrer.Kurzname)}</td><td>{H(z.Lehrer.VollerName)}</td><td class=\"z\">{z.Vertretungen}</td><td class=\"z\">{z.Betreuungen}</td><td class=\"z\">{z.Sondereinsaetze}</td><td class=\"z\">{z.Aufsichten}</td><td class=\"z\">{z.Mehrarbeit}</td><td class=\"z\">{z.Entfaelle}</td><td class=\"z\">{z.Freisetzungen}</td><td class=\"z\">{Zaehlung.WertText(d, z.Saldo)}</td><td class=\"z\">{z.Verguetungsfaehig}</td></tr>");
        var s = Statistik.Zusammenfassung(d, von, bis);
        sb.Append($"</table><div class=\"bil\">Gesamt: {liste.Sum(z => z.Vertretungen)} Vertretungen, {liste.Sum(z => z.Mehrarbeit)} Mehrarbeitsstunden, davon {liste.Sum(z => z.Verguetungsfaehig)} vergütungsfähig.<br>" +
                  $"Ausfall: {s.Gesamt} Einträge – vertreten {s.Vertreten}, Entfall {s.Entfall}, Freisetzung {s.Freisetzung}, Raum {s.Raum}, Verlegung {s.Verlegung}, offen {s.Offen}.</div>");
        return sb.ToString();
    }
}

/// <summary>
/// Auswahldialog eines Berichts wie im Handbuch (z. B. „Vertretungs-Abrechnung“): Lehrer n/m mit „Auswahl…“, Von, Bis, Monat und rechts
/// „Druck - Details“, „Überschrift“ und „Schrift“.
/// </summary>
public sealed class BerichtAuswahlDialog : Dialog
{
    private readonly DateTimePicker _von = new() { Format = DateTimePickerFormat.Short, Left = 12, Top = 100, Width = 96 };
    private readonly DateTimePicker _bis = new() { Format = DateTimePickerFormat.Short, Left = 116, Top = 100, Width = 96 };
    private readonly ComboBox _monat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 146, Width = 96 };
    private readonly GroupBox _lehrerGruppe = new() { Left = 8, Top = 8, Width = 200, Height = 60 };
    private readonly List<Lehrer> _alle;

    public List<Lehrer> Lehrer { get; private set; }
    public StatistikBerichte.AbrechnungsDetails Details { get; } = new();
    public string Ueberschrift { get; private set; } = "";
    public int Schrift { get; private set; } = 100;
    public DateOnly Von => DateOnly.FromDateTime(_von.Value);
    public DateOnly Bis => DateOnly.FromDateTime(_bis.Value);

    public BerichtAuswahlDialog(string titel, DateOnly von, DateOnly bis, bool mitLehrer, bool mitDetails) : base(titel, 330, 230)
    {
        Ok.Text = "Ok";
        Ok.Left = 12;
        _alle = Stammdaten.Sortiert(AppZustand.D.Lehrer).ToList();
        Lehrer = _alle.ToList();
        var auswahl = new Button { Text = "Auswahl...", Left = 40, Top = 22, Width = 90, Height = 25 };
        auswahl.Click += (_, _) => Auswaehlen();
        _lehrerGruppe.Controls.Add(auswahl);
        _lehrerGruppe.Visible = mitLehrer;
        LehrerText();
        Controls.Add(_lehrerGruppe);
        Controls.Add(new Label { Text = "Von", Left = 12, Top = 82, AutoSize = true });
        Controls.Add(new Label { Text = "Bis", Left = 116, Top = 82, AutoSize = true });
        Controls.Add(new Label { Text = "Monat", Left = 12, Top = 128, AutoSize = true });
        _von.Value = von.ToDateTime(TimeOnly.MinValue);
        _bis.Value = bis.ToDateTime(TimeOnly.MinValue);
        var d = AppZustand.D;
        _monat.Items.Add("");
        for (var m = new DateTime(d.Einstellungen.SchuljahrVon.Year, d.Einstellungen.SchuljahrVon.Month, 1); m <= d.Einstellungen.SchuljahrBis.ToDateTime(TimeOnly.MinValue); m = m.AddMonths(1)) _monat.Items.Add(m);
        _monat.Format += (_, e) => { if (e.ListItem is DateTime m) e.Value = m.ToString("MMM yyyy", Format.De); };
        _monat.SelectedIndexChanged += (_, _) =>
        {
            if (_monat.SelectedItem is not DateTime m) return;
            _von.Value = m;
            _bis.Value = m.AddMonths(1).AddDays(-1);
        };
        Controls.AddRange(new Control[] { _von, _bis, _monat });
        var details = new Button { Text = "Druck - Details", Left = 220, Top = 14, Width = 100, Height = 25, Visible = mitDetails };
        var ueber = new Button { Text = "Überschrift", Left = 220, Top = 44, Width = 100, Height = 25 };
        var schrift = new Button { Text = "Schrift", Left = 220, Top = 74, Width = 100, Height = 25 };
        details.Click += (_, _) => DetailsZeigen();
        ueber.Click += (_, _) => { if (Ui.Eingabe("Überschrift des Berichts:", "Überschrift", Ueberschrift) is { } t) Ueberschrift = t; };
        schrift.Click += (_, _) =>
        {
            if (Ui.Eingabe("Schriftgröße in %:", "Schrift", Schrift.ToString()) is { } t && int.TryParse(t, out var p)) Schrift = Math.Clamp(p, 50, 200);
        };
        Controls.AddRange(new Control[] { details, ueber, schrift });
    }

    private void LehrerText() => _lehrerGruppe.Text = $"Lehrer: {Lehrer.Count}/{_alle.Count}";

    private void Auswaehlen()
    {
        using var f = new Dialog("Lehrer", 260, 380);
        var liste = new CheckedListBox { Left = 8, Top = 8, Width = 244, Height = 300, CheckOnClick = true };
        foreach (var l in _alle) liste.Items.Add(l.Kurzname, Lehrer.Contains(l));
        var alle = new Button { Text = "Alle", Left = 8, Top = 314, Width = 70, Height = 25 };
        var keine = new Button { Text = "Keine", Left = 84, Top = 314, Width = 70, Height = 25 };
        alle.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, true); };
        keine.Click += (_, _) => { for (var i = 0; i < liste.Items.Count; i++) liste.SetItemChecked(i, false); };
        f.Controls.AddRange(new Control[] { liste, alle, keine });
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Lehrer = _alle.Where((_, i) => liste.GetItemChecked(i)).ToList();
        LehrerText();
    }

    /// <summary>„Details“ der Vertretungs-Abrechnung wie im Handbuch.</summary>
    private void DetailsZeigen()
    {
        using var f = new Dialog("Details", 270, 230);
        f.Ok.Text = "OK";
        f.Abbrechen.Text = "Cancel";
        var seite = new CheckBox { Text = "1 Seite / Element", Left = 12, Top = 10, AutoSize = true, Checked = Details.EineSeiteJeElement };
        var uebertrag = new CheckBox { Text = "Summe mit Übertrag", Left = 12, Top = 32, AutoSize = true, Checked = Details.SummeMitUebertrag };
        var det = new CheckBox { Text = "Details", Left = 12, Top = 68, AutoSize = true, Checked = Details.Details };
        var ohneB = new CheckBox { Text = "Ohne Bereitschaften", Left = 30, Top = 90, AutoSize = true, Checked = Details.OhneBereitschaften };
        var ohneP = new CheckBox { Text = "Ohne Pausenaufsichten", Left = 30, Top = 112, AutoSize = true, Checked = Details.OhnePausenaufsichten };
        var monate = new CheckBox { Text = "Zwischensummen für Monate ausgeb...", Left = 12, Top = 156, AutoSize = true, Checked = Details.ZwischensummenMonate };
        void Freigeben() => ohneB.Enabled = ohneP.Enabled = det.Checked;
        det.CheckedChanged += (_, _) => Freigeben();
        Freigeben();
        f.Controls.AddRange(new Control[] { seite, uebertrag, det, ohneB, ohneP, monate });
        if (f.ShowDialog(this) != DialogResult.OK) return;
        Details.EineSeiteJeElement = seite.Checked;
        Details.SummeMitUebertrag = uebertrag.Checked;
        Details.Details = det.Checked;
        Details.OhneBereitschaften = ohneB.Checked;
        Details.OhnePausenaufsichten = ohneP.Checked;
        Details.ZwischensummenMonate = monate.Checked;
    }
}
