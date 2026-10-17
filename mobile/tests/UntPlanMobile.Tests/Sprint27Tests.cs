using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UntPlanMobile.Dienste;

namespace UntPlanMobile.Tests;

/// <summary>
/// Sprint 27 (Parität zur iOS-App, Vertrag Stundenkarten und Sprechtag): Verlegung mit Ursprung und Ziel (ohne Marker oben rechts),
/// hinzugekommene Werte mit „+“, manuelle Vertretungstexte bei Zusatzstunden, Kopplungen nebeneinander, nur geänderte Werte im
/// grünen Label, Sprechtag „angekündigt“ auf der Startseite, andere Klassenpläne für Schüler*innen, neues App-Symbol.
/// </summary>
public class Sprint27Tests
{
    private static string Datei(params string[] teile) => File.ReadAllText(Path.Combine(new[] { Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile" }.Concat(teile).ToArray()));
    private static string AppJs => Datei("wwwroot", "assets", "js", "app.js");
    private static string AppCss => Datei("wwwroot", "assets", "css", "app.css");

    private static PlanKarte Karte(string status, int von, string art = "", string text = "", List<PlanWert>? lehrer = null, List<PlanWert>? raeume = null,
        List<PlanWert>? klassen = null, string fach = "M") =>
        new("2026-10-14", von, von, "", "", status, fach, null, "Mathematik", "#7BAAF7",
            klassen ?? new() { new("5a") }, lehrer ?? new() { new("WEB") }, raeume ?? new() { new("R101") }, art, text, null, 106);

    private static readonly List<PlanZeit> Zeiten = new()
    {
        new(1, "1", "07:55", "08:40"), new(2, "2", "08:45", "09:30"), new(3, "3", "09:50", "10:35"),
    };

    [Fact]
    public void Verlegung_mit_Ursprung_und_Ziel_aus_dem_Vertrag()
    {
        // JSON wie im Vertrag (camelCase, gelesen wie von UntPlanWebVerbindung)
        var json = """
            { "datum":"2026-10-13","von":6,"bis":6,"beginn":"12:15","ende":"13:00","status":"geaendert","fach":"D","fachName":"Deutsch","farbe":"",
              "klassen":[{"k":"5a"}],"lehrer":[{"k":"FIS"}],"raeume":[{"k":"R101"},{"k":"B070","neu":true,"plus":true}],
              "art":"Verlegung","text":"","unterricht":5,"verlegung":"nach",
              "verlegungVon":{"datum":"2026-10-09","beginn":"11:30","ende":"13:00","stunde":4,"bisStunde":5},
              "verlegungNach":{"datum":"2026-10-13","beginn":"12:15","ende":"","stunde":6,"bisStunde":6},
              "marker":["verlegungNach","aenderung"] }
            """;
        var karte = JsonSerializer.Deserialize<PlanKarte>(json, UntPlanWebVerbindung.Json)!;
        var k = Kachelbau.AusKarte(karte, "class");
        Assert.Equal("nach", k.Verlegung);
        Assert.Equal("2026-10-09", k.VerlegungVon!.Datum);
        Assert.Equal("09.10.2026 | 11:30 - 13:00", k.VerlegungVon.Text);
        // ohne Ende nur der Beginn
        Assert.Equal("13.10.2026 | 12:15", k.VerlegungNach!.Text);
        Assert.Equal("09.10.2026 | 11:30 - 13:00 → 13.10.2026 | 12:15", k.VerlegungText);
        // hinzugekommener Raum: grünes Label mit „+“
        Assert.Equal("B070", k.RaumPlus);
        Assert.Null(k.LehrkraftPlus);

        // Gegenseite unbekannt: nur das Zeichen, kein Text
        var ohne = Kachelbau.AusKarte(karte with { VerlegungNach = null }, "class");
        Assert.NotNull(ohne.VerlegungVon);
        Assert.Null(ohne.VerlegungNach);
        Assert.Null(ohne.VerlegungText);

        // Felder im Format der Oberfläche
        var aus = JsonSerializer.SerializeToNode(k)!.AsObject();
        Assert.Equal("09.10.2026 | 11:30 - 13:00", aus["movedFrom"]!["text"]!.GetValue<string>());
        // Marker des Servers (oben rechts) bleiben unbeachtet – die Karten der App tragen oben rechts nie ein Zeichen
        Assert.Null(aus["markers"]);
        Assert.Equal("B070", aus["roomPlus"]!.GetValue<string>());
    }

    [Fact]
    public void Hinzugekommene_Lehrkraft_und_Klasse_in_Kopplungen()
    {
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Vertretung",
            lehrer: new() { new("WEB"), new("MUE", null, true, true) },
            klassen: new() { new("5a"), new("5b", null, true, true) }), "room");
        Assert.Equal("MUE", k.LehrkraftPlus);
        Assert.Equal("5b", k.KlassePlus);
        Assert.Contains(k.Kopplung!, t => t.Code == "MUE" && t.Zustand == "sub" && t.Plus == true);
        Assert.Contains(k.Klassenliste!, t => t.Name == "5b" && t.Plus == true);
    }

    [Fact]
    public void Nur_geaenderte_Werte_im_gruenen_Label()
    {
        // reine Raumänderung: der neue Raum steht im Label, die Lehrkraft nicht (auch wenn sie als „neu“ mit gleichem Wert kommt)
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Raumänderung",
            lehrer: new() { new("WEB", "WEB", true) }, raeume: new() { new("R202", "R101", true) }), "teacher");
        Assert.Contains("room", k.Geaendert);
        Assert.DoesNotContain("teacher", k.Geaendert);
        Assert.Equal("R101", k.RaumAlt);
        // echte Vertretung: die Lehrkraft ist geändert
        var v = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Vertretung", lehrer: new() { new("MUE", "WEB", true) }), "class");
        Assert.Contains("teacher", v.Geaendert);

        // Oberfläche: im eigenen Plan der vertretenden Lehrkraft steht ihr Kürzel als grünes Label auf der Vertretungskachel
        // (wie iOS) – nicht bei einer reinen Raumänderung
        var js = AppJs;
        Assert.Contains("? { ...l, changed:true } : l)", js);
        Assert.Contains("&& (card.changed || []).every(k => k === 'room'))", js);
    }

    [Fact]
    public void Zusatzstunde_behaelt_manuellen_Vertretungstext()
    {
        Assert.True(Kachelbau.IstZusatzStandardtext("wegen Sondereinsatz", "Sondereinsatz"));
        Assert.True(Kachelbau.IstZusatzStandardtext("Vertr. o. Lehrkr.", ""));
        Assert.True(Kachelbau.IstZusatzStandardtext("aus Vertretung 5. Std.", ""));
        Assert.True(Kachelbau.IstZusatzStandardtext("Zusätzliche Unterrichtsstunde", ""));
        Assert.False(Kachelbau.IstZusatzStandardtext("Bitte Laptop mitbringen", "Vertretung"));
        var k = Kachelbau.AusKarte(Karte("geaendert", 2, art: "Sondereinsatz", text: "Bitte Laptop mitbringen") with { Zusaetzlich = true }, "class");
        Assert.True(k.Zusatz);
        Assert.Equal("Bitte Laptop mitbringen", k.Info);
        // Details: Vertretungstext auch bei der Zusatzstunde (neben der Plakette)
        Assert.Contains("} else if (c.info){", AppJs);
        Assert.DoesNotContain("} else if (c.info && !zusatz){", AppJs);
    }

    [Fact]
    public void Kopplung_bleibt_als_einzelne_Kacheln_nebeneinander()
    {
        var plan = new Wochenplan("klasse", "5a", "", "2026-10-12", "A", Zeiten,
            new() { new("2026-10-14", "Mittwoch", "A", false, null, true, new(), new()) },
            new()
            {
                Karte("regulaer", 2, fach: "KR", lehrer: new() { new("WEB") }),
                Karte("geaendert", 2, fach: "ER", art: "Vertretung", lehrer: new() { new("MUE", "BEC", true) }),
                Karte("entfall", 2, fach: "PL", lehrer: new() { new("LAN") }),
            }, new());
        var tag = Kachelbau.Woche(plan)["2026-10-14"];
        Assert.Equal(3, tag.Count);
        Assert.Equal(new[] { "ER", "KR", "PL" }, tag.Select(x => x.Fach).OrderBy(x => x));

        // Oberfläche: nebeneinander nur, solange jeder Block mindestens 40 px breit bleibt, sonst erster Block + Zählkarte
        var js = AppJs;
        Assert.Contains("const MIN_CARD = 40;", js);
        Assert.Contains("const grenzeFuer = () => fitSide;", js);
        Assert.Contains("card._n > grenzeFuer(cards, card) && card._i > 0", js);
        Assert.DoesNotContain("_koppAend", js);
        Assert.DoesNotContain("x.takeover) ? Math.max(fitSide, 2)", js);
        Assert.Contains("data-more=\"${date}\"", js);
    }

    private static string? Node()
    {
        foreach (var kandidat in new[] { "node", "/opt/node22/bin/node", "/usr/bin/node", "/usr/local/bin/node" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(kandidat, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
                p!.WaitForExit(5000);
                if (p.ExitCode == 0) return kandidat;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }

    private static string Abschnitt(string js, string name)
    {
        var beginn = js.IndexOf($"/* {name}-BEGINN", StringComparison.Ordinal);
        var ende = js.IndexOf($"/* {name}-ENDE */", StringComparison.Ordinal);
        Assert.True(beginn > 0 && ende > beginn, $"Abschnitt {name} fehlt");
        return js[beginn..ende];
    }

    private static void JsPruefen(string code)
    {
        var node = Node();
        if (node is null) return;
        var skript = Path.Combine(Path.GetTempPath(), "untplan-s27-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(skript, "const window = {};\nconst pruefe = (ist, soll) => { const a = JSON.stringify(ist), b = JSON.stringify(soll); if (a !== b) { console.error(a + ' statt ' + b); process.exit(1); } };\n" + code + "\nconsole.log('ok');\n");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(node, skript) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            Assert.True(p.ExitCode == 0, aus);
        }
        finally { File.Delete(skript); }
    }

    [Fact]
    public void Plus_und_Verlegung_ohne_Zeichen_auf_der_Kachel()
    {
        var js = AppJs;
        JsPruefen(Abschnitt(js, "PLUS") + """
            const p = window.UntPlanPlus;
            pruefe(p.plusWert({ roomPlus:'B070' }, 'room', 'b070'), '+b070');
            pruefe(p.plusWert({ roomPlus:'B070' }, 'room', 'R101'), 'R101');
            pruefe(p.plusWert({}, 'teacher', 'MUE'), 'MUE');
            """);
        // kein Verlegungszeichen auf der Kachel und in den Details; die Verlegung steht als Detailzeile „Verlegt“
        Assert.DoesNotContain("KACHEL_VERLEGT", js);
        Assert.DoesNotContain("zeigVerlegt", js);
        Assert.Contains("const vorrang = [zeigInfo && 'info', online && 'online', zeigHw && 'hw']", js);
        Assert.Contains("'Hierher verlegt'", js);
        Assert.Contains("['Von: ' + von, 'Nach: ' + nach]", js);
        // oben rechts nie ein Zeichen, Zeilen lassen dort keinen Platz frei
        Assert.DoesNotContain("card__marker", js);
        Assert.DoesNotContain("card.markers", js);
        var css = AppCss;
        Assert.DoesNotContain("card__marker", css);
        Assert.DoesNotContain(".card.mk-", css);
    }

    [Fact]
    public void Sprechtag_angekuendigt_auf_Start_und_Sprechtagseite()
    {
        var js = AppJs;
        JsPruefen(Abschnitt(js, "SPRECHTAG") + """
            const s = window.UntPlanSprechtag;
            pruefe(s.sptStatus({ status:'angekuendigt' }), 'angekuendigt');
            pruefe(s.sptStatus({ phase:'einladung' }), 'angekuendigt');
            pruefe(s.sptStatus({ status:'anmeldung', abgesagt:true }), 'abgesagt');
            pruefe(s.sptUntertitel({ status:'angekuendigt', text:'Angekündigt: Do, 12.11.2026' }), 'Angekündigt: Do, 12.11.2026');
            pruefe(s.sptUntertitel({ status:'angekuendigt', datumText:'Do, 12.11.2026', beginn:'15:00', ende:'19:00', ort:'Aula', anmeldungVonText:'02.11.2026 08:00' }),
                   'Angekündigt: Do, 12.11.2026, 15:00–19:00 Uhr · Aula · Anmeldung ab 02.11.2026 08:00');
            pruefe(s.sptUntertitel({ status:'anmeldung', datumText:'Do, 12.11.2026', text:'2 Termine' }), 'Do, 12.11.2026 · 2 Termine');
            pruefe(s.sptZeiten({ zeitbloecke:[{ von:'15:00', bis:'17:00' }, { von:'17:15', bis:'19:00' }] }), '15:00–17:00, 17:15–19:00');
            pruefe(s.sptZeiten({ beginn:'15:00', ende:'19:00' }), '15:00–19:00');
            """);
        // Startseite: Karten aus der Startseite (heute.sprechtage), sonst einzeln; Schüler*innen nur lesend
        Assert.Contains("sptStart(Array.isArray(d.sprechtage) ? d.sprechtage : undefined);", js);
        Assert.Contains("Termine buchen Ihre Erziehungsberechtigten.", js);
        Assert.Contains("Die Zeiten unten sind eine Vorschau.", js);
        Assert.Contains("sprechtage,", Datei("Api", "StartseiteApi.cs"));
    }

    [Fact]
    public void Schueler_sehen_andere_Klassen_wenn_der_Server_sie_liefert()
    {
        var elemente = new Elemente(new() { new("EF", "EF", null, null), new("5a", "5a", null, new() { "FIS" }) },
            new() { new("FIS", "Michael Fischer") }, new() { new("R101", "R101") }, new(), new(), new());
        var schueler = new Konto(new Sitzungsbenutzer(4, "lea", "Lea Bauer", "schueler", "Schüler*in", "EF", false, false, "klasse"), "Schule", elemente);
        var liste = JsonSerializer.SerializeToNode(Wochendienst.Entitaeten(schueler))!.AsArray();
        Assert.Contains(liste, e => e!["type"]!.GetValue<string>() == "class" && e["id"]!.GetValue<string>() == "5a");
        Assert.DoesNotContain(liste, e => e!["type"]!.GetValue<string>() == "teacher");
    }

    [Fact]
    public void App_Symbol_wie_in_der_iOS_App()
    {
        var wurzel = Path.Combine(Umgebung.Wurzel(), "mobile", "src", "UntPlanMobile", "wwwroot", "assets", "icons");
        foreach (var (datei, groesse) in new[] { ("icon-192.png", 192), ("icon-512.png", 512), ("maskable-192.png", 192), ("maskable-512.png", 512), ("apple-touch-icon.png", 180) })
        {
            var b = File.ReadAllBytes(Path.Combine(wurzel, datei));
            // PNG-Kopf: Breite und Höhe (Big Endian ab Byte 16)
            Assert.Equal(groesse, (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19]);
            Assert.Equal(groesse, (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        }
        // Akzentfarbe des iOS-Symbols
        Assert.Contains("#F56B2A", File.ReadAllText(Path.Combine(wurzel, "favicon.svg")));
        var manifest = JsonNode.Parse(Datei("wwwroot", "manifest.webmanifest"))!;
        Assert.Contains(manifest["icons"]!.AsArray(), i => i!["purpose"]!.GetValue<string>() == "maskable" && i["sizes"]!.GetValue<string>() == "512x512");
    }

    [Fact]
    public void Kleinere_Knoepfe_und_hoehere_Zeilen()
    {
        var css = AppCss;
        var teil = css[css.IndexOf("Sprint 27 – Gleichstand mit der iOS-App", StringComparison.Ordinal)..];
        Assert.Contains(".card .card__line ~ .card__line{ position:relative; top:-.8px; }", teil);
        Assert.Contains("html body .sheet--detail .drow2__done{ width:23px !important; height:23px !important; }", teil);
        Assert.Contains("min-width:28px; height:28px; border-radius:14px; padding:0 7px; font-size:15px;", teil);
    }
}
